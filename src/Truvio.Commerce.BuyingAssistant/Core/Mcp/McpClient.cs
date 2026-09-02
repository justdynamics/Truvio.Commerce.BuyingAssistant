using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Truvio.Commerce.BuyingAssistant.Core.Mcp;

public sealed record McpToolInfo(string Name, string? Description, JsonElement InputSchema);

public sealed record McpCallResult(string Text, bool IsError);

/// <summary>
/// Minimal MCP client over Streamable HTTP (JSON-RPC 2.0 POST, responses as JSON or SSE).
/// Covers exactly what the assistant needs: initialize, tools/list, tools/call.
/// One instance per (url, token); the tool list is cached for a short while.
/// </summary>
public sealed class McpClient
{
    private static readonly ConcurrentDictionary<string, McpClient> Clients = new();
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private static readonly TimeSpan ToolListTtl = TimeSpan.FromMinutes(10);

    private readonly string _url;
    private readonly string? _bearer;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private string? _sessionId;
    private int _nextId = 1;
    private IReadOnlyList<McpToolInfo>? _tools;
    private DateTime _toolsLoadedAt;

    public const string ProtocolVersion = "2025-03-26";

    public McpClient(string url, string? bearer, HttpClient? http = null)
    {
        _url = url;
        _bearer = string.IsNullOrWhiteSpace(bearer) ? null : bearer.Trim();
        _http = http ?? SharedHttp;
    }

    public static McpClient For(string url, string? bearer)
    {
        var key = url + "\n" + (bearer ?? "");
        return Clients.GetOrAdd(key, _ => new McpClient(url, bearer));
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler();
        // Local development hosts run on self-signed certificates; MCP URLs on localhost are trusted.
        handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || (msg.RequestUri != null && (msg.RequestUri.IsLoopback || msg.RequestUri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)));
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            var init = await SendAsync("initialize", new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { },
                clientInfo = new { name = "Truvio.Commerce.BuyingAssistant", version = typeof(McpClient).Assembly.GetName().Version?.ToString() ?? "0.0" },
            }, ct).ConfigureAwait(false);
            if (init.Error != null) throw new McpException("initialize failed: " + init.Error);
            try { await NotifyAsync("notifications/initialized", ct).ConfigureAwait(false); } catch { /* optional for stateless servers */ }
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (!forceRefresh && _tools != null && DateTime.UtcNow - _toolsLoadedAt < ToolListTtl) return _tools;
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var tools = new List<McpToolInfo>();
        string? cursor = null;
        for (var page = 0; page < 20; page++)
        {
            var resp = await SendAsync("tools/list", cursor == null ? new { } : new { cursor }, ct).ConfigureAwait(false);
            if (resp.Error != null) throw new McpException("tools/list failed: " + resp.Error);
            if (resp.Result is { } result && result.TryGetProperty("tools", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in arr.EnumerateArray())
                {
                    var name = t.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var desc = t.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    var schema = t.TryGetProperty("inputSchema", out var s) ? s.Clone() : JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement;
                    tools.Add(new McpToolInfo(name!, desc, schema));
                }
            }
            cursor = resp.Result is { } r2 && r2.TryGetProperty("nextCursor", out var nc) && nc.ValueKind == JsonValueKind.String ? nc.GetString() : null;
            if (string.IsNullOrEmpty(cursor)) break;
        }
        _tools = tools;
        _toolsLoadedAt = DateTime.UtcNow;
        return tools;
    }

    public async Task<McpCallResult> CallToolAsync(string name, JsonElement? arguments, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        object args = arguments.HasValue && arguments.Value.ValueKind == JsonValueKind.Object ? arguments.Value : new { };
        var resp = await SendAsync("tools/call", new { name, arguments = args }, ct).ConfigureAwait(false);
        if (resp.Error != null) return new McpCallResult("MCP error: " + resp.Error, true);
        if (resp.Result is not { } result) return new McpCallResult("", false);

        var isError = result.TryGetProperty("isError", out var ie) && ie.ValueKind == JsonValueKind.True;
        var sb = new StringBuilder();
        if (result.TryGetProperty("structuredContent", out var structured) && structured.ValueKind != JsonValueKind.Null && structured.ValueKind != JsonValueKind.Undefined)
        {
            sb.Append(structured.GetRawText());
        }
        else if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "text" && block.TryGetProperty("text", out var text)) { if (sb.Length > 0) sb.Append('\n'); sb.Append(text.GetString()); }
                else { if (sb.Length > 0) sb.Append('\n'); sb.Append(block.GetRawText()); }
            }
        }
        else sb.Append(result.GetRawText());
        return new McpCallResult(sb.ToString(), isError);
    }

    private sealed record RpcResponse(JsonElement? Result, string? Error);

    private async Task NotifyAsync(string method, CancellationToken ct)
    {
        using var req = BuildRequest(new { jsonrpc = "2.0", method });
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        // 202 Accepted for notifications; anything else is ignored.
    }

    private async Task<RpcResponse> SendAsync(string method, object @params, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        using var req = BuildRequest(new { jsonrpc = "2.0", id, method, @params });
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.Headers.TryGetValues("Mcp-Session-Id", out var sids)) _sessionId = sids.FirstOrDefault() ?? _sessionId;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var snippet = body.Length > 300 ? body[..300] : body;
            throw new McpException($"HTTP {(int)resp.StatusCode} from MCP server for {method}: {snippet}");
        }
        var contentType = resp.Content.Headers.ContentType?.MediaType;
        var message = ExtractMessage(body, contentType, id);
        if (message == null) throw new McpException($"No JSON-RPC response for {method}.");
        var root = message.Value;
        if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : err.GetRawText();
            return new RpcResponse(null, msg ?? "unknown error");
        }
        return new RpcResponse(root.TryGetProperty("result", out var res) ? res.Clone() : null, null);
    }

    private HttpRequestMessage BuildRequest(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var req = new HttpRequestMessage(HttpMethod.Post, _url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        if (_bearer != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearer);
        if (_sessionId != null) req.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        return req;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// Extracts the JSON-RPC message with the given id from a plain JSON body or an SSE body
    /// ("event: message" / "data: {...}" blocks). Falls back to the last message with a result or error.
    /// </summary>
    internal static JsonElement? ExtractMessage(string body, string? contentType, int expectedId)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var candidates = new List<string>();
        if (contentType != null && contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase) || body.TrimStart().StartsWith("event:") || body.TrimStart().StartsWith("data:"))
        {
            var current = new StringBuilder();
            foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
            {
                if (rawLine.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (current.Length > 0) current.Append('\n');
                    current.Append(rawLine[5..].TrimStart());
                }
                else if (rawLine.Length == 0 && current.Length > 0)
                {
                    candidates.Add(current.ToString());
                    current.Clear();
                }
            }
            if (current.Length > 0) candidates.Add(current.ToString());
        }
        else candidates.Add(body);

        JsonElement? fallback = null;
        foreach (var c in candidates)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(c); } catch { continue; }
            var root = doc.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var id) && id == expectedId) return root;
            if (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _)) fallback = root;
        }
        return fallback;
    }
}

public sealed class McpException : Exception
{
    public McpException(string message) : base(message) { }
}
