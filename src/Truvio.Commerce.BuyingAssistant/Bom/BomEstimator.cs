using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Dynamicweb.Security.UserManagement;
using Truvio.Commerce.BuyingAssistant.Core.Assistant;
using Truvio.Commerce.BuyingAssistant.Core.Settings;

namespace Truvio.Commerce.BuyingAssistant.Bom;

/// <summary>
/// Bill-of-materials mode: turns a job description into a cart-ready, platform-priced bill of materials
/// with one model call (forced tool <c>submit_estimate</c>) over the shop's live catalog.
/// Synchronous and safe to call from a Razor page render: the catalog is read and the lines are priced
/// on the calling thread (inside the request's Dynamicweb context); only the HTTP call to the Messages
/// API runs asynchronously and is awaited without a captured context.
/// Settings: Settings, Apps, Buying Assistant, tab "Bill of materials" (keys under
/// <c>/Globalsettings/Truvio/BuyingAssistant/Bom/</c>); API key and model from the Assistant tab.
/// </summary>
public static class BomEstimator
{
    private static readonly object ClientGate = new();
    private static AnthropicClient? _client;
    private static string? _clientKey;

    /// <summary>The id the model sees and returns for a product: the product id, or id|variant for a variant.</summary>
    public static string CatalogKey(string productId, string? variantId)
    {
        return string.IsNullOrEmpty(variantId) ? productId : productId + "|" + variantId;
    }

    /// <summary>Sizes the job. Never throws: every failure comes back as <see cref="BomResult.Error"/>.</summary>
    public static BomResult Estimate(BomRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        AssistantSettings settings;
        try
        {
            settings = DwAssistantSettings.Current;
        }
        catch (Exception ex)
        {
            Log("BOM settings could not be read", ex);
            return new BomResult { Error = BomMessages.CouldNotSize(ex) };
        }
        return BomRunner.Run(request, settings, () => new DwBomCatalog(settings, request), CallModel, Log);
    }

    /// <summary>Positional overload with the same parameters as the estimator it replaces.</summary>
    public static BomResult Estimate(string prompt, string? contextProductKey, User? user, string languageId, string? shopId, string currencyCode, string countryCode, string? extraInstructions = null)
    {
        return Estimate(new BomRequest
        {
            Prompt = prompt ?? "",
            ContextProductKey = contextProductKey,
            User = user,
            LanguageId = languageId ?? "",
            ShopId = shopId,
            CurrencyCode = currencyCode ?? "",
            CountryCode = countryCode ?? "",
            ExtraInstructions = extraInstructions,
        });
    }

    private static BomModelAnswer CallModel(BomModelCall call)
    {
        var system = new List<BetaTextBlockParam>
        {
            new() { Text = call.SystemPrompt, CacheControl = new BetaCacheControlEphemeral() },
        };
        if (!string.IsNullOrWhiteSpace(call.ExtraInstructions))
            system.Add(new BetaTextBlockParam { Text = "ADDITIONAL INSTRUCTIONS FOR THIS REQUEST:\n" + call.ExtraInstructions });

        var parameters = new MessageCreateParams
        {
            Model = call.Model,
            MaxTokens = call.MaxTokens,
            System = system,
            OutputConfig = new BetaOutputConfig { Effort = BuyingAssistantEngine.ParseEffort(call.Effort) },
            Tools = [BuildTool()],
            ToolChoice = new BetaToolChoiceTool { Name = BomPrompt.ToolName },
            Messages = [new BetaMessageParam { Role = Role.User, Content = call.UserMessage }],
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(30, call.TimeoutSeconds)));
        // No synchronization context is captured (ConfigureAwait(false) inside the SDK and here), so
        // blocking on the call from a page render cannot deadlock; the pipeline does the same.
        var response = Client(call.ApiKey).Beta.Messages.Create(parameters, cancellationToken: timeout.Token)
            .ConfigureAwait(false).GetAwaiter().GetResult();

        IReadOnlyDictionary<string, JsonElement>? input = null;
        foreach (var block in response.Content)
        {
            if (block.TryPickToolUse(out var toolUse) && toolUse.Name == BomPrompt.ToolName) { input = toolUse.Input; break; }
        }
        return new BomModelAnswer(input, response.Usage.InputTokens, response.Usage.OutputTokens);
    }

    /// <summary>One client per API key, reused across requests (it owns an HttpClient).</summary>
    private static AnthropicClient Client(string apiKey)
    {
        lock (ClientGate)
        {
            if (_client == null || !string.Equals(_clientKey, apiKey, StringComparison.Ordinal))
            {
                _client = new AnthropicClient { ApiKey = apiKey };
                _clientKey = apiKey;
            }
            return _client;
        }
    }

    private static BetaTool BuildTool() => new()
    {
        Name = BomPrompt.ToolName,
        Description = "Submit the sized bill of materials for the described job.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["summary"] = JsonSchema.Str("One or two sentences: what was sized and the key numbers."),
                ["assumptions"] = JsonSchema.StrArray("Assumptions made where the description was silent."),
                ["lines"] = JsonSchema.Raw(new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            product_id = new { type = "string", description = "Catalog product id, exactly as listed." },
                            quantity = new { type = "integer", description = "Whole sellable units to order." },
                            reason = new { type = "string", description = "Short sizing math for this line." },
                        },
                        required = new[] { "product_id", "quantity", "reason" },
                    },
                }),
                ["notes"] = JsonSchema.Str("Anything the customer should double-check or that the catalog could not cover. Empty string if none."),
            },
            Required = ["summary", "assumptions", "lines", "notes"],
        },
    };

    private static void Log(string message, Exception ex)
    {
        try { Dynamicweb.Logging.LogManager.Current.GetLogger("Truvio.BuyingAssistant").Error(message, ex); } catch { /* logging is best effort */ }
    }
}
