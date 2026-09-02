using Keys = Truvio.Commerce.BuyingAssistant.Core.Settings.AssistantSettingKeys;

namespace Truvio.Commerce.BuyingAssistant.Core.Settings;

public enum McpMode
{
    /// <summary>No MCP tools.</summary>
    Off,
    /// <summary>The app is the MCP client: it calls the MCP server over HTTP from the host (works for localhost and private URLs).</summary>
    Direct,
    /// <summary>Anthropic's MCP connector calls the MCP server from Anthropic's side (URL must be publicly reachable).</summary>
    Connector,
}

/// <summary>
/// The settings the engine reads, resolved from GlobalSettings with in-memory defaults.
/// Pure data: no Dynamicweb dependency, so it can be built in tests.
/// </summary>
public sealed record AssistantSettings
{
    public string? ApiKey { get; init; }
    public string Model { get; init; } = Keys.Defaults.Model;
    public string Effort { get; init; } = Keys.Defaults.Effort;
    public string AssistantName { get; init; } = Keys.Defaults.AssistantName;
    public string Instructions { get; init; } = "";
    public string Skills { get; init; } = "";

    public string SearchRepository { get; init; } = Keys.Defaults.SearchRepository;
    public string SearchQuery { get; init; } = Keys.Defaults.SearchQuery;
    public string SearchParameter { get; init; } = Keys.Defaults.SearchParameter;
    public int SearchResultCap { get; init; } = Keys.Defaults.SearchResultCap;
    public string CatalogFields { get; init; } = "";
    public bool RecentOrdersEnabled { get; init; } = Keys.Defaults.RecentOrdersEnabled;

    public McpMode McpMode { get; init; } = McpMode.Off;
    public string McpUrl { get; init; } = "";
    public string McpToken { get; init; } = "";
    public string McpAllowedTools { get; init; } = "";
    public bool McpAllowWriteTools { get; init; } = Keys.Defaults.McpAllowWriteTools;
    public string McpServerName { get; init; } = Keys.Defaults.McpServerName;

    public int MaxIterations { get; init; } = Keys.Defaults.MaxIterations;
    public int MaxTokens { get; init; } = Keys.Defaults.MaxTokens;
    public int TimeoutSeconds { get; init; } = Keys.Defaults.TimeoutSeconds;
    public int MaxPromptLength { get; init; } = Keys.Defaults.MaxPromptLength;
    public bool AllowAnonymous { get; init; } = Keys.Defaults.AllowAnonymous;
    public bool LogConversations { get; init; } = Keys.Defaults.LogConversations;
    public string CartServiceTag { get; init; } = Keys.Defaults.CartServiceTag;

    public bool McpConfigured => McpMode != McpMode.Off && !string.IsNullOrWhiteSpace(McpUrl);

    /// <summary>
    /// Builds settings from any key reader. Whitespace-only values count as unset
    /// (an empty GlobalSettings element round-trips as whitespace).
    /// </summary>
    public static AssistantSettings FromReader(Func<string, string?> read, Func<string, string?>? readEnvironment = null)
    {
        string? Str(string key) { var v = read(key); return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }
        string StrOr(string key, string fallback) => Str(key) ?? fallback;
        int IntOr(string key, int fallback) => int.TryParse(Str(key), out var i) && i > 0 ? i : fallback;
        bool BoolOr(string key, bool fallback)
        {
            var v = Str(key);
            if (v == null) return fallback;
            return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        var apiKey = Str(Keys.ApiKey);
        if (apiKey == null && readEnvironment != null)
        {
            var env = readEnvironment(Keys.Fallbacks.ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(env)) apiKey = env.Trim();
        }
        apiKey ??= Str(Keys.Fallbacks.DynamoApiKey);

        var modeRaw = StrOr(Keys.McpMode, Keys.Defaults.McpMode);
        var mode = Enum.TryParse<McpMode>(modeRaw, true, out var parsedMode) ? parsedMode : McpMode.Off;

        return new AssistantSettings
        {
            ApiKey = apiKey,
            Model = StrOr(Keys.Model, Keys.Defaults.Model),
            Effort = StrOr(Keys.Effort, Keys.Defaults.Effort).ToLowerInvariant(),
            AssistantName = StrOr(Keys.AssistantName, Keys.Defaults.AssistantName),
            Instructions = read(Keys.Instructions)?.Trim() ?? "",
            Skills = read(Keys.Skills)?.Trim() ?? "",
            SearchRepository = StrOr(Keys.SearchRepository, Keys.Defaults.SearchRepository),
            SearchQuery = StrOr(Keys.SearchQuery, Keys.Defaults.SearchQuery),
            SearchParameter = StrOr(Keys.SearchParameter, Keys.Defaults.SearchParameter),
            SearchResultCap = Math.Clamp(IntOr(Keys.SearchResultCap, Keys.Defaults.SearchResultCap), 3, 100),
            CatalogFields = Str(Keys.CatalogFields) ?? "",
            RecentOrdersEnabled = BoolOr(Keys.RecentOrdersEnabled, Keys.Defaults.RecentOrdersEnabled),
            McpMode = mode,
            McpUrl = Str(Keys.McpUrl) ?? "",
            McpToken = Str(Keys.McpToken) ?? "",
            McpAllowedTools = read(Keys.McpAllowedTools)?.Trim() ?? "",
            McpAllowWriteTools = BoolOr(Keys.McpAllowWriteTools, Keys.Defaults.McpAllowWriteTools),
            McpServerName = StrOr(Keys.McpServerName, Keys.Defaults.McpServerName),
            MaxIterations = Math.Clamp(IntOr(Keys.MaxIterations, Keys.Defaults.MaxIterations), 2, 40),
            MaxTokens = Math.Clamp(IntOr(Keys.MaxTokens, Keys.Defaults.MaxTokens), 1024, 64000),
            TimeoutSeconds = Math.Clamp(IntOr(Keys.TimeoutSeconds, Keys.Defaults.TimeoutSeconds), 30, 900),
            MaxPromptLength = Math.Clamp(IntOr(Keys.MaxPromptLength, Keys.Defaults.MaxPromptLength), 200, 20000),
            AllowAnonymous = BoolOr(Keys.AllowAnonymous, Keys.Defaults.AllowAnonymous),
            LogConversations = BoolOr(Keys.LogConversations, Keys.Defaults.LogConversations),
            CartServiceTag = StrOr(Keys.CartServiceTag, Keys.Defaults.CartServiceTag),
        };
    }
}
