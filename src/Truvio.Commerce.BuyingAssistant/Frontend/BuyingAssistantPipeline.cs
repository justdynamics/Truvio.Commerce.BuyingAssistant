using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Anthropic.Models.Beta.Messages;
using Dynamicweb.Content;
using Dynamicweb.Content.Items;
using Dynamicweb.Frontend;
using Dynamicweb.Host.Core;
using Dynamicweb.Security.UserManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Truvio.Commerce.BuyingAssistant.Core.Assistant;
using Truvio.Commerce.BuyingAssistant.Core.Catalog.Dw;
using Truvio.Commerce.BuyingAssistant.Core.Settings;

namespace Truvio.Commerce.BuyingAssistant.Frontend;

/// <summary>
/// Registers the storefront endpoints with the Dynamicweb host:
/// POST /truvio/buying-assistant/ask (server-sent events) and GET /truvio/buying-assistant/assets/{file}.
/// Discovered by AddInManager; runs at host start, so installing the app needs a restart.
/// </summary>
public sealed class BuyingAssistantPipeline : IPipeline
{
    public const string BasePath = "/truvio/buying-assistant";
    private static readonly ConversationStore<BetaMessageParam> Conversations = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int Rank => 500;

    public void RegisterServices(IServiceCollection services, IMvcCoreBuilder mvcBuilder)
    {
        services.AddHttpContextAccessor();
    }

    public void RegisterApplicationComponents(IApplicationBuilder app)
    {
        app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments(BasePath, StringComparison.OrdinalIgnoreCase), branch =>
        {
            branch.UseRouting();
            branch.UseEndpoints(endpoints =>
            {
                endpoints.MapPost(BasePath + "/ask", AskAsync);
                endpoints.MapGet(BasePath + "/assets/{file}", ServeAsset);
                endpoints.MapGet(BasePath + "/ping", ctx => ctx.Response.WriteAsync("ok"));
                endpoints.MapGet(BasePath + "/diagnose", DiagnoseAsync);
            });
        });
    }

    public void RunInitializers()
    {
        try
        {
            var filesRoot = Dynamicweb.Core.SystemInformation.MapPath("/Files/");
            var log = AssetInstaller.EnsureInstalled(filesRoot);
            foreach (var line in log) Logger.Info(line);
            if (log.Any(l => l.StartsWith("Installed ") || l.StartsWith("Updated ")))
            {
                // A freshly dropped item-type XML needs the metadata refresh that creates its table.
                ItemManager.UpdateMetadata();
                Logger.Info("Item type metadata refreshed");
            }
            StatusReporter.Write("startup");
        }
        catch (Exception ex)
        {
            Logger.Error("Asset install failed", ex);
        }
    }

    private static Dynamicweb.Logging.ILogger Logger => Dynamicweb.Logging.LogManager.Current.GetLogger("Truvio.BuyingAssistant");

    // ---- Assets --------------------------------------------------------------------------------

    private static Task ServeAsset(HttpContext ctx)
    {
        var file = ctx.Request.RouteValues["file"]?.ToString() ?? "";
        string? contentType = file switch
        {
            "buying-assistant.js" => "application/javascript; charset=utf-8",
            "buying-assistant.css" => "text/css; charset=utf-8",
            _ => null,
        };
        if (contentType == null)
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        }
        var bytes = AssetInstaller.ReadResourceBytes("Assets." + file);
        ctx.Response.ContentType = contentType;
        ctx.Response.Headers["Cache-Control"] = ctx.Request.Query.ContainsKey("v") ? "public, max-age=31536000, immutable" : "no-cache";
        return ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length, ctx.RequestAborted);
    }

    // ---- Ask -------------------------------------------------------------------------------------

    private sealed class AskBody
    {
        public string? ConversationId { get; set; }
        public string? Message { get; set; }
        public int PageId { get; set; }
        public int ParagraphId { get; set; }
        public string? ProductId { get; set; }
        public string? VariantId { get; set; }
        public string? ProductName { get; set; }
    }

    private static async Task AskAsync(HttpContext ctx)
    {
        var ct = ctx.RequestAborted;
        AskBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AskBody>(ctx.Request.Body, Json, ct).ConfigureAwait(false);
        }
        catch { body = null; }
        if (body == null || string.IsNullOrWhiteSpace(body.Message) || body.PageId <= 0)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("Missing message or page.", ct).ConfigureAwait(false);
            return;
        }

        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);

        var channel = Channel.CreateUnbounded<AssistantEvent>(new UnboundedChannelOptions { SingleReader = true });
        var writer = Task.Run(async () =>
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await WriteEventAsync(ctx.Response, evt, ct).ConfigureAwait(false);
            }
        }, ct);

        AssistantResult result;
        try
        {
            result = await Task.Run(() => RunInPageContext(body, e => channel.Writer.TryWrite(e)), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Ask failed", ex);
            result = new AssistantResult { Error = "The assistant is unavailable right now." };
        }
        channel.Writer.TryComplete();
        try { await writer.ConfigureAwait(false); } catch { }

        await WriteEventAsync(ctx.Response, new AssistantEvent(result.Error != null && !result.HasProposal && result.FollowUpQuestion == null ? "error" : "result", result.Error, result), ct).ConfigureAwait(false);
        await WriteEventAsync(ctx.Response, new AssistantEvent("done"), ct).ConfigureAwait(false);
    }

    private static async Task WriteEventAsync(HttpResponse response, AssistantEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { type = evt.Type, text = evt.Text, data = evt.Data }, Json);
        var sb = new StringBuilder();
        sb.Append("event: ").Append(evt.Type).Append('\n');
        sb.Append("data: ").Append(payload.Replace("\n", "\\n")).Append("\n\n");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await response.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the assistant inside a page view isolation for the requesting page, so the area,
    /// ecommerce context and signed-in user resolve exactly like a page render.
    /// </summary>
    private static AssistantResult RunInPageContext(AskBody body, Action<AssistantEvent> progress)
    {
        var settings = DwAssistantSettings.Current;
        using var isolation = new PageViewIsolation(body.PageId);
        var pageView = PageView.Current();
        if (pageView == null || pageView.Area == null)
            return new AssistantResult { Error = "Unknown page." };

        var user = UserContext.Current.User;
        if (user == null && !settings.AllowAnonymous)
            return new AssistantResult { Error = "Sign in to use the assistant." };

        var request = new AssistantRequest
        {
            ConversationId = body.ConversationId ?? "",
            Message = body.Message ?? "",
            ContextProductId = string.IsNullOrWhiteSpace(body.ProductId) ? null : body.ProductId.Trim(),
            ContextVariantId = string.IsNullOrWhiteSpace(body.VariantId) ? null : body.VariantId.Trim(),
            ContextProductName = string.IsNullOrWhiteSpace(body.ProductName) ? null : body.ProductName.Trim(),
        };
        request.PlacementMode = request.ContextProductId != null ? "product" : "standalone";
        ApplyParagraphSettings(body.ParagraphId, request);

        var ownerKey = OwnerKey(user);
        var gateway = new DwCatalogGateway(settings);
        var engine = new BuyingAssistantEngine(settings, gateway, Conversations, ownerKey, progress,
            (msg, ex) => { if (ex != null) Logger.Error(msg, ex); else Logger.Warn(msg); });
        var result = engine.RunAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        StatusReporter.RecordRun(result, request, user?.UserName, body.PageId);
        if (settings.LogConversations)
        {
            Logger.Info($"conversation={result.ConversationId} user={user?.UserName ?? "anonymous"} page={body.PageId} product={body.ProductId} lines={result.Lines.Count} total={result.TotalFormatted} tokens_in={result.InputTokens} cache_read={result.CacheReadTokens} tokens_out={result.OutputTokens} tools={result.ToolCalls} iterations={result.Iterations} seconds={result.ElapsedSeconds} error={result.Error} prompt=\"{Truncate(request.Message, 300)}\"");
        }
        return result;
    }

    private static string OwnerKey(User? user)
    {
        var sessionId = "";
        try { sessionId = Dynamicweb.Context.Current?.Session?.SessionID ?? ""; } catch { }
        return (user?.ID.ToString() ?? "anon") + "|" + sessionId;
    }

    private static void ApplyParagraphSettings(int paragraphId, AssistantRequest request)
    {
        if (paragraphId <= 0) return;
        try
        {
            var paragraph = Services.Paragraphs.GetParagraph(paragraphId);
            if (paragraph == null || string.IsNullOrEmpty(paragraph.ItemType) || string.IsNullOrEmpty(paragraph.ItemId)) return;
            var item = Item.GetItemById(paragraph.ItemType, paragraph.ItemId);
            if (item == null) return;
            if (item.TryGetValue("Instructions", out var extra) && extra is string s && !string.IsNullOrWhiteSpace(s)) request.ExtraInstructions = s;
            if (item.TryGetValue("Skills", out var skills) && skills is string sk && !string.IsNullOrWhiteSpace(sk)) request.SkillFilter = sk;
            if (item.TryGetValue("SkillsText", out var skillsText) && skillsText is string st && !string.IsNullOrWhiteSpace(st)) request.ExtraSkills = st;
            if (item.TryGetValue("Mode", out var mode) && mode is string m && m.Equals("standalone", StringComparison.OrdinalIgnoreCase))
            {
                request.PlacementMode = "standalone";
                request.ContextProductId = null;
                request.ContextVariantId = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Paragraph settings not applied: " + ex.Message);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// Local-only diagnostics: GET /truvio/buying-assistant/diagnose?pageId=..&amp;productId=..[&amp;variantId=..&amp;quantity=..&amp;q=..]
    /// shows how the gateway resolves context, price, stock and search for the current session.
    /// Answers only requests from the loopback address.
    /// </summary>
    private static async Task DiagnoseAsync(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote == null || !System.Net.IPAddress.IsLoopback(remote))
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        int.TryParse(ctx.Request.Query["pageId"], out var pageId);
        var productId = ctx.Request.Query["productId"].ToString();
        var variantId = ctx.Request.Query["variantId"].ToString();
        var q = ctx.Request.Query["q"].ToString();
        double.TryParse(ctx.Request.Query["quantity"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var quantity);
        object payload;
        try
        {
            payload = await Task.Run(() =>
            {
                using var isolation = new PageViewIsolation(pageId);
                var settings = DwAssistantSettings.Current;
                var gateway = new DwCatalogGateway(settings);
                var result = new Dictionary<string, object?>
                {
                    ["context"] = gateway.Context,
                    ["settings"] = new { settings.Model, settings.Effort, settings.McpMode, settings.McpUrl, settings.SearchRepository, settings.SearchQuery, hasApiKey = !string.IsNullOrEmpty(settings.ApiKey) },
                };
                if (!string.IsNullOrEmpty(productId))
                {
                    result["summary"] = gateway.GetSummary(productId, variantId);
                    result["price"] = gateway.GetPrice(productId, variantId, quantity <= 0 ? 1 : quantity, null);
                    result["stock"] = gateway.GetStock(productId, variantId);
                    result["priceTrace"] = PriceTrace(productId, variantId);
                }
                if (!string.IsNullOrEmpty(q)) result["search"] = gateway.Search(q, 5);
                return (object)result;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            payload = new { error = ex.ToString() };
        }
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ctx.RequestAborted).ConfigureAwait(false);
    }

    private static object PriceTrace(string productId, string variantId)
    {
        try
        {
            var languageId = Dynamicweb.Ecommerce.Common.Context.LanguageID;
            var currency = Dynamicweb.Ecommerce.Common.Context.Currency;
            var country = Dynamicweb.Ecommerce.Common.Context.Country;
            var user = UserContext.Current.User;
            var product = Dynamicweb.Ecommerce.Services.Products.GetProductById(productId, variantId ?? "", languageId, user);
            if (product == null) return new { error = "product not found", languageId };
            var pc = new Dynamicweb.Ecommerce.Prices.PriceContext(currency, country!, null, user, false, null);
            var info = Dynamicweb.Ecommerce.Prices.PriceManager.GetPrice(pc, product);
            var found = Dynamicweb.Ecommerce.Prices.PriceManager.FindPrice(pc, new Dynamicweb.Ecommerce.Prices.PriceProductSelection(product, product.DefaultUnitId, 0, 1, 1), false);
            return new
            {
                languageId,
                currency = currency?.Code,
                country = country?.Code2,
                user = user?.UserName,
                product.DefaultPrice,
                product.DefaultUnitId,
                info = new { info.Price, info.PriceWithVAT, info.PriceWithoutVAT, info.PriceFormatted, source = info.PriceSource?.ToString(), currency = info.Currency?.Code },
                foundType = found?.GetType().FullName,
                found = found is Dynamicweb.Ecommerce.Prices.PriceRaw raw ? new { raw.Price, currency = raw.Currency?.Code } : null,
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.ToString() };
        }
    }
}
