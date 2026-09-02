using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Truvio.Commerce.BuyingAssistant.Core.Catalog;
using Truvio.Commerce.BuyingAssistant.Core.Mcp;
using Truvio.Commerce.BuyingAssistant.Core.Settings;
using Truvio.Commerce.BuyingAssistant.Core.Skills;

namespace Truvio.Commerce.BuyingAssistant.Core.Assistant;

/// <summary>
/// Runs one assistant turn: builds the prompt and tools, drives the Claude tool loop
/// (built-in catalog tools in-process, optional MCP tools), captures the final
/// submit_proposal call, re-prices every proposed line through the catalog gateway and
/// keeps the conversation for follow-ups.
/// </summary>
public sealed class BuyingAssistantEngine
{
    private const string McpConnectorBeta = "mcp-client-2025-11-20";
    private const int ToolResultCap = 48_000;

    private static readonly JsonSerializerOptions ResultJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly AssistantSettings _settings;
    private readonly ICatalogGateway _catalog;
    private readonly ConversationStore<BetaMessageParam> _store;
    private readonly string _ownerKey;
    private readonly Action<AssistantEvent>? _progress;
    private readonly Action<string, Exception?>? _log;
    private readonly Func<AnthropicClient>? _clientFactory;

    private IReadOnlyList<McpToolInfo> _mcpTools = Array.Empty<McpToolInfo>();
    private McpClient? _mcp;

    public BuyingAssistantEngine(
        AssistantSettings settings,
        ICatalogGateway catalog,
        ConversationStore<BetaMessageParam> store,
        string ownerKey,
        Action<AssistantEvent>? progress = null,
        Action<string, Exception?>? log = null,
        Func<AnthropicClient>? clientFactory = null)
    {
        _settings = settings;
        _catalog = catalog;
        _store = store;
        _ownerKey = ownerKey;
        _progress = progress;
        _log = log;
        _clientFactory = clientFactory;
    }

    public async Task<AssistantResult> RunAsync(AssistantRequest request, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var result = new AssistantResult { ModelId = _settings.Model, CurrencyCode = _catalog.Context.CurrencyCode };
        var message = (request.Message ?? "").Trim();
        if (message.Length < 3)
        {
            result.Error = "Describe what you need first.";
            return result;
        }
        if (message.Length > _settings.MaxPromptLength) message = message[.._settings.MaxPromptLength];
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            result.Error = "The assistant is not configured yet (no Anthropic API key). Set it under Settings, Apps, Buying Assistant.";
            return result;
        }

        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId) ? ConversationStore<BetaMessageParam>.NewId() : request.ConversationId.Trim();
        result.ConversationId = conversationId;
        var gate = _store.GetLock(conversationId, _ownerKey);
        if (!await gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            result.Error = "The assistant is still working on your previous request.";
            return result;
        }

        try
        {
            var history = new List<BetaMessageParam>(_store.Get(conversationId, _ownerKey));
            history.Add(new BetaMessageParam { Role = Role.User, Content = message });

            Report(AssistantEvent.Status("Reading your request"));
            await PrepareMcpAsync(ct).ConfigureAwait(false);

            var allSkills = new List<Skill>(SkillParser.Parse(_settings.Skills));
            foreach (var extra in SkillParser.Parse(request.ExtraSkills))
            {
                allSkills.RemoveAll(s => s.Name.Equals(extra.Name, StringComparison.OrdinalIgnoreCase));
                allSkills.Add(extra);
            }
            var skills = SkillParser.Filter(allSkills, request.SkillFilter);
            var stable = PromptBuilder.BuildStablePart(_settings, skills, _mcpTools.Count > 0 || _settings.McpMode == McpMode.Connector, _settings.RecentOrdersEnabled);
            var context = PromptBuilder.BuildContextPart(_catalog.Context, request, DateTime.Now);
            var tools = BuildTools();

            var client = _clientFactory?.Invoke() ?? new AnthropicClient { ApiKey = _settings.ApiKey };
            ProposalDraft? proposal = null;
            string lastText = "";

            for (var iteration = 1; iteration <= _settings.MaxIterations; iteration++)
            {
                ct.ThrowIfCancellationRequested();
                result.Iterations = iteration;
                var useConnector = _settings.McpMode == McpMode.Connector && _settings.McpConfigured;
                var parameters = new MessageCreateParams
                {
                    Model = _settings.Model,
                    MaxTokens = _settings.MaxTokens,
                    System = new List<BetaTextBlockParam>
                    {
                        new() { Text = stable, CacheControl = new BetaCacheControlEphemeral() },
                        new() { Text = context },
                    },
                    Tools = tools,
                    Messages = history,
                    OutputConfig = new BetaOutputConfig { Effort = ParseEffort(_settings.Effort) },
                    Betas = useConnector ? [McpConnectorBeta] : null,
                    McpServers = useConnector ? [BuildConnectorServer()] : null,
                };

                BetaMessage response;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
                    response = await client.Beta.Messages.Create(parameters, cancellationToken: timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    result.Error = "The assistant took too long to answer. Try a shorter or more specific request.";
                    break;
                }

                result.InputTokens += response.Usage.InputTokens;
                result.OutputTokens += response.Usage.OutputTokens;
                result.CacheReadTokens += response.Usage.CacheReadInputTokens ?? 0;

                var assistantContent = new List<BetaContentBlockParam>();
                var toolResults = new List<BetaContentBlockParam>();
                foreach (var block in response.Content)
                {
                    if (block.TryPickText(out var text))
                    {
                        assistantContent.Add(new BetaTextBlockParam { Text = text.Text });
                        if (!string.IsNullOrWhiteSpace(text.Text)) { lastText = text.Text.Trim(); Report(AssistantEvent.Narrative(lastText)); }
                    }
                    else if (block.TryPickThinking(out var thinking))
                    {
                        assistantContent.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                    }
                    else if (block.TryPickRedactedThinking(out var redacted))
                    {
                        assistantContent.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                    }
                    else if (block.TryPickToolUse(out var toolUse))
                    {
                        assistantContent.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                        result.ToolCalls++;
                        var (content, isError, draft) = await ExecuteToolAsync(toolUse.Name, toolUse.Input, ct).ConfigureAwait(false);
                        if (draft != null) proposal = draft;
                        toolResults.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = content, IsError = isError });
                    }
                    else if (block.TryPickMcpToolUse(out var mcpUse))
                    {
                        result.ToolCalls++;
                        Report(AssistantEvent.ToolCall(mcpUse.Name, "Using backend tool " + mcpUse.Name));
                        assistantContent.Add(new BetaMcpToolUseBlockParam { ID = mcpUse.ID, Name = mcpUse.Name, ServerName = mcpUse.ServerName, Input = mcpUse.Input });
                    }
                    else if (block.TryPickMcpToolResult(out var mcpResult))
                    {
                        assistantContent.Add(new BetaRequestMcpToolResultBlockParam { ToolUseID = mcpResult.ToolUseID, IsError = mcpResult.IsError });
                    }
                }
                if (assistantContent.Count > 0) history.Add(new BetaMessageParam { Role = Role.Assistant, Content = assistantContent });
                if (toolResults.Count > 0) history.Add(new BetaMessageParam { Role = Role.User, Content = toolResults });

                var stop = response.StopReason?.ToString() ?? "";
                if (proposal != null) break;
                if (stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
                {
                    result.Error = "The assistant declined this request.";
                    break;
                }
                if (stop.Contains("max_tokens", StringComparison.OrdinalIgnoreCase))
                {
                    result.Error = "The answer got too long. Try a more specific request.";
                    break;
                }
                if (stop.Contains("pause_turn", StringComparison.OrdinalIgnoreCase)) continue;
                if (toolResults.Count == 0)
                {
                    // end_turn without a proposal: treat the narrative as a follow-up question.
                    break;
                }
                if (iteration == _settings.MaxIterations)
                {
                    result.Error = "The assistant ran out of steps before finishing. Try a simpler request.";
                }
            }

            _store.Put(conversationId, _ownerKey, history);

            if (proposal != null)
            {
                Report(AssistantEvent.Status("Pricing your proposal"));
                ApplyProposal(result, proposal, request);
            }
            else if (result.Error == null)
            {
                result.FollowUpQuestion = string.IsNullOrWhiteSpace(lastText) ? "Could you tell me a bit more about what you need?" : lastText;
            }
            result.TotalFormatted = _catalog.FormatMoney(result.Total);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result.Error = "Request cancelled.";
        }
        catch (Exception ex)
        {
            _log?.Invoke("Assistant run failed", ex);
            result.Error = "The assistant is unavailable right now. " + ex.GetType().Name;
        }
        finally
        {
            gate.Release();
        }
        result.ElapsedSeconds = Math.Round(watch.Elapsed.TotalSeconds, 1);
        return result;
    }

    // ---- MCP -----------------------------------------------------------------------------------

    private async Task PrepareMcpAsync(CancellationToken ct)
    {
        if (_settings.McpMode != McpMode.Direct || !_settings.McpConfigured) return;
        try
        {
            _mcp = McpClient.For(_settings.McpUrl, _settings.McpToken);
            var all = await _mcp.ListToolsAsync(ct).ConfigureAwait(false);
            var patterns = ToolNamePolicy.ParsePatterns(_settings.McpAllowedTools);
            _mcpTools = all.Where(t => ToolNamePolicy.IsAllowed(t.Name, patterns, _settings.McpAllowWriteTools) && !BuiltInToolNames.Contains(t.Name)).ToList();
        }
        catch (Exception ex)
        {
            _log?.Invoke("MCP tools unavailable: " + ex.Message, ex);
            _mcpTools = Array.Empty<McpToolInfo>();
        }
    }

    private BetaRequestMcpServerUrlDefinition BuildConnectorServer()
    {
        var patterns = ToolNamePolicy.ParsePatterns(_settings.McpAllowedTools);
        var explicitNames = patterns.Where(p => !p.Contains('*')).ToList();
        return new BetaRequestMcpServerUrlDefinition
        {
            Name = _settings.McpServerName,
            Url = _settings.McpUrl,
            AuthorizationToken = string.IsNullOrWhiteSpace(_settings.McpToken) ? null : _settings.McpToken,
            ToolConfiguration = explicitNames.Count > 0
                ? new BetaRequestMcpServerToolConfiguration { Enabled = true, AllowedTools = explicitNames }
                : null,
        };
    }

    // ---- Tools ---------------------------------------------------------------------------------

    private static readonly HashSet<string> BuiltInToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "search_products", "get_product", "get_price", "get_stock", "list_categories", "products_in_category",
        "customer_context", "recent_orders", PromptBuilder.ProposalToolName,
    };

    private List<BetaToolUnion> BuildTools()
    {
        var tools = new List<BetaToolUnion>
        {
            new BetaTool
            {
                Name = "search_products",
                Description = "Full-text search in the shop catalog. Returns up to max_results products with id, sku, name, unit, unit price at quantity 1 in the shopper's currency, stock and key attributes. Search product types, synonyms, brands, part numbers or category words; run several searches when the first is thin.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["query"] = JsonSchema.Str("Search terms (2 to 6 words work best)."),
                        ["max_results"] = JsonSchema.Int("Maximum products to return (default 12, max " + _settings.SearchResultCap + ")."),
                    },
                    Required = ["query"],
                },
            },
            new BetaTool
            {
                Name = "get_product",
                Description = "Full detail for one product: description, specifications, categories, variants (colours/sizes), quantity breaks, stock per location, units and related products.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["product_id"] = JsonSchema.Str("Product id exactly as returned by search_products."),
                        ["variant_id"] = JsonSchema.Str("Variant id when the product has variants (optional)."),
                    },
                    Required = ["product_id"],
                },
            },
            new BetaTool
            {
                Name = "get_price",
                Description = "Exact unit price and line total for a product at a given quantity, in the shopper's currency with their price agreements and quantity breaks applied.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["product_id"] = JsonSchema.Str("Product id."),
                        ["variant_id"] = JsonSchema.Str("Variant id (optional)."),
                        ["quantity"] = JsonSchema.Num("Quantity in sellable units."),
                        ["unit_id"] = JsonSchema.Str("Unit id when the product sells in several units (optional)."),
                    },
                    Required = ["product_id", "quantity"],
                },
            },
            new BetaTool
            {
                Name = "get_stock",
                Description = "Stock on hand per stock location (branch/warehouse) for a product, with the shopper's home location flagged.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["product_id"] = JsonSchema.Str("Product id."),
                        ["variant_id"] = JsonSchema.Str("Variant id (optional)."),
                    },
                    Required = ["product_id"],
                },
            },
            new BetaTool
            {
                Name = "list_categories",
                Description = "Lists product categories (top level, or the children of parent_group_id) with ids and product counts. Use it to find the right category words before searching or browsing.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["parent_group_id"] = JsonSchema.Str("Group id to list children of (omit for top level)."),
                    },
                    Required = [],
                },
            },
            new BetaTool
            {
                Name = "products_in_category",
                Description = "Lists products in a category (group id from list_categories).",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["group_id"] = JsonSchema.Str("Category group id."),
                        ["max_results"] = JsonSchema.Int("Maximum products to return (default 20)."),
                    },
                    Required = ["group_id"],
                },
            },
            new BetaTool
            {
                Name = "customer_context",
                Description = "Who the shopper is (company, customer number, groups), their currency and home stock location, and what is in their cart right now.",
                InputSchema = new() { Properties = new Dictionary<string, JsonElement>(), Required = [] },
            },
        };

        if (_settings.RecentOrdersEnabled)
        {
            tools.Add(new BetaTool
            {
                Name = "recent_orders",
                Description = "The signed-in shopper's most recent orders with their lines (product ids, quantities, prices). Use for reorders and replenishment.",
                InputSchema = new()
                {
                    Properties = new Dictionary<string, JsonElement> { ["max_orders"] = JsonSchema.Int("How many orders to return (default 5, max 20).") },
                    Required = [],
                },
            });
        }

        foreach (var mcpTool in _mcpTools)
        {
            var schema = mcpTool.InputSchema;
            var props = new Dictionary<string, JsonElement>();
            var required = new List<string>();
            if (schema.ValueKind == JsonValueKind.Object)
            {
                if (schema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object)
                    foreach (var prop in p.EnumerateObject()) props[prop.Name] = prop.Value.Clone();
                if (schema.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.Array)
                    foreach (var req in r.EnumerateArray()) if (req.ValueKind == JsonValueKind.String) required.Add(req.GetString()!);
            }
            tools.Add(new BetaTool
            {
                Name = mcpTool.Name,
                Description = Truncate("Backend tool. " + (mcpTool.Description ?? ""), 1000),
                InputSchema = new() { Properties = props, Required = required },
            });
        }

        if (_settings.McpMode == McpMode.Connector && _settings.McpConfigured)
        {
            tools.Add(new BetaMcpToolset { McpServerName = _settings.McpServerName });
        }

        tools.Add(new BetaTool
        {
            Name = PromptBuilder.ProposalToolName,
            Description = "Submit the final answer for this turn: the sized proposal (or a single follow-up question when nothing can be sized yet). Call it exactly once at the end of every turn.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["summary"] = JsonSchema.Str("One or two sentences for the shopper: what was sized and the key numbers."),
                    ["assumptions"] = JsonSchema.StrArray("Assumptions made where the request was silent. Empty array if none."),
                    ["lines"] = JsonSchema.Raw(new
                    {
                        type = "array",
                        description = "The proposed products. Empty when asking a follow-up question.",
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            properties = new
                            {
                                product_id = new { type = "string", description = "Product id exactly as returned by the tools." },
                                variant_id = new { type = "string", description = "Variant id when applicable, else empty string." },
                                quantity = new { type = "number", description = "Whole sellable units to order." },
                                unit_id = new { type = "string", description = "Unit id when the product sells in several units, else empty string." },
                                reason = new { type = "string", description = "Short sizing math for this line." },
                            },
                            required = new[] { "product_id", "variant_id", "quantity", "unit_id", "reason" },
                        },
                    }),
                    ["notes"] = JsonSchema.Str("What the shopper should double-check or what the catalog could not cover. Empty string if none."),
                    ["follow_up_question"] = JsonSchema.Str("The single question you need answered before you can propose. Empty string when lines are given."),
                },
                Required = ["summary", "assumptions", "lines", "notes", "follow_up_question"],
            },
        });
        return tools;
    }

    private sealed class ProposalDraft
    {
        public string Summary = "";
        public List<string> Assumptions = new();
        public List<(string ProductId, string VariantId, double Quantity, string UnitId, string Reason)> Lines = new();
        public string Notes = "";
        public string FollowUp = "";
    }

    private async Task<(string Content, bool IsError, ProposalDraft? Draft)> ExecuteToolAsync(string name, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        try
        {
            switch (name)
            {
                case "search_products":
                {
                    var q = GetString(input, "query");
                    var max = Math.Clamp(GetInt(input, "max_results", 12), 1, _settings.SearchResultCap);
                    Report(AssistantEvent.ToolCall(name, $"Searching the catalog for \"{q}\""));
                    var hits = _catalog.Search(q, max);
                    Report(AssistantEvent.ToolResult(name, hits.Count == 0 ? $"No products for \"{q}\"" : $"{hits.Count} product{(hits.Count == 1 ? "" : "s")} for \"{q}\""));
                    return (Serialize(new { query = q, count = hits.Count, products = hits }), false, null);
                }
                case "get_product":
                {
                    var pid = GetString(input, "product_id");
                    var vid = GetString(input, "variant_id");
                    Report(AssistantEvent.ToolCall(name, "Reading product " + pid));
                    var detail = _catalog.GetProduct(pid, vid);
                    if (detail == null) return ("Product not found: " + pid, true, null);
                    Report(AssistantEvent.ToolResult(name, detail.Summary.Name));
                    return (Serialize(detail), false, null);
                }
                case "get_price":
                {
                    var pid = GetString(input, "product_id");
                    var vid = GetString(input, "variant_id");
                    var qty = GetDouble(input, "quantity", 1);
                    var unit = GetString(input, "unit_id");
                    Report(AssistantEvent.ToolCall(name, $"Pricing {qty:0.##} x {pid}"));
                    var quote = _catalog.GetPrice(pid, vid, qty, string.IsNullOrEmpty(unit) ? null : unit);
                    if (quote == null) return ("No price available for " + pid, true, null);
                    Report(AssistantEvent.ToolResult(name, $"{quote.UnitPriceFormatted} each{(quote.TierLabel.Length > 0 ? " (" + quote.TierLabel + ")" : "")}"));
                    return (Serialize(quote), false, null);
                }
                case "get_stock":
                {
                    var pid = GetString(input, "product_id");
                    var vid = GetString(input, "variant_id");
                    Report(AssistantEvent.ToolCall(name, "Checking stock for " + pid));
                    var stock = _catalog.GetStock(pid, vid);
                    Report(AssistantEvent.ToolResult(name, stock.Count == 0 ? "No stock records" : $"{stock.Count} location{(stock.Count == 1 ? "" : "s")}"));
                    return (Serialize(new { product_id = pid, locations = stock }), false, null);
                }
                case "list_categories":
                {
                    var parent = GetString(input, "parent_group_id");
                    Report(AssistantEvent.ToolCall(name, string.IsNullOrEmpty(parent) ? "Listing categories" : "Listing subcategories of " + parent));
                    var cats = _catalog.GetCategories(string.IsNullOrEmpty(parent) ? null : parent);
                    Report(AssistantEvent.ToolResult(name, cats.Count + " categories"));
                    return (Serialize(new { categories = cats }), false, null);
                }
                case "products_in_category":
                {
                    var gid = GetString(input, "group_id");
                    var max = Math.Clamp(GetInt(input, "max_results", 20), 1, 60);
                    Report(AssistantEvent.ToolCall(name, "Browsing category " + gid));
                    var items = _catalog.GetProductsInCategory(gid, max);
                    Report(AssistantEvent.ToolResult(name, items.Count + " products"));
                    return (Serialize(new { group_id = gid, count = items.Count, products = items }), false, null);
                }
                case "customer_context":
                {
                    Report(AssistantEvent.ToolCall(name, "Looking at your account and cart"));
                    var info = _catalog.GetCustomerContext();
                    return (Serialize(info), false, null);
                }
                case "recent_orders":
                {
                    var max = Math.Clamp(GetInt(input, "max_orders", 5), 1, 20);
                    Report(AssistantEvent.ToolCall(name, "Reading your recent orders"));
                    var orders = _catalog.GetRecentOrders(max);
                    Report(AssistantEvent.ToolResult(name, orders.Count + " orders"));
                    return (Serialize(new { orders }), false, null);
                }
                case PromptBuilder.ProposalToolName:
                {
                    var draft = new ProposalDraft
                    {
                        Summary = GetString(input, "summary"),
                        Notes = GetString(input, "notes"),
                        FollowUp = GetString(input, "follow_up_question"),
                    };
                    if (input.TryGetValue("assumptions", out var a) && a.ValueKind == JsonValueKind.Array)
                        foreach (var s in a.EnumerateArray()) { var v = s.ValueKind == JsonValueKind.String ? s.GetString() : null; if (!string.IsNullOrWhiteSpace(v)) draft.Assumptions.Add(v.Trim()); }
                    if (input.TryGetValue("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var l in lines.EnumerateArray())
                        {
                            if (l.ValueKind != JsonValueKind.Object) continue;
                            var pid = l.TryGetProperty("product_id", out var pe) && pe.ValueKind == JsonValueKind.String ? pe.GetString() ?? "" : "";
                            var vid = l.TryGetProperty("variant_id", out var ve) && ve.ValueKind == JsonValueKind.String ? ve.GetString() ?? "" : "";
                            var unit = l.TryGetProperty("unit_id", out var ue) && ue.ValueKind == JsonValueKind.String ? ue.GetString() ?? "" : "";
                            var reason = l.TryGetProperty("reason", out var re) && re.ValueKind == JsonValueKind.String ? re.GetString() ?? "" : "";
                            double qty = 0;
                            if (l.TryGetProperty("quantity", out var qe))
                            {
                                if (qe.ValueKind == JsonValueKind.Number) qty = qe.GetDouble();
                                else if (qe.ValueKind == JsonValueKind.String) double.TryParse(qe.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out qty);
                            }
                            if (string.IsNullOrWhiteSpace(pid) || qty <= 0) continue;
                            draft.Lines.Add((pid.Trim(), vid.Trim(), qty, unit.Trim(), reason.Trim()));
                        }
                    }
                    Report(AssistantEvent.Status(draft.Lines.Count > 0 ? "Proposal ready" : "Needs more information"));
                    return ("Proposal recorded.", false, draft);
                }
                default:
                {
                    if (_mcp != null && _mcpTools.Any(t => t.Name == name))
                    {
                        Report(AssistantEvent.ToolCall(name, "Using backend tool " + name));
                        var args = JsonSerializer.SerializeToElement(input);
                        var r = await _mcp.CallToolAsync(name, args, ct).ConfigureAwait(false);
                        Report(AssistantEvent.ToolResult(name, r.IsError ? "Backend tool reported an error" : "Backend tool answered"));
                        return (Truncate(r.Text, ToolResultCap), r.IsError, null);
                    }
                    return ("Unknown tool: " + name, true, null);
                }
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke("Tool " + name + " failed", ex);
            return ("Tool failed: " + ex.Message, true, null);
        }
    }

    private void ApplyProposal(AssistantResult result, ProposalDraft draft, AssistantRequest request)
    {
        result.Summary = draft.Summary;
        result.Notes = draft.Notes;
        result.Assumptions.AddRange(draft.Assumptions);
        if (draft.Lines.Count == 0)
        {
            result.FollowUpQuestion = string.IsNullOrWhiteSpace(draft.FollowUp) ? null : draft.FollowUp;
            if (result.FollowUpQuestion == null && string.IsNullOrWhiteSpace(result.Summary)) result.FollowUpQuestion = "Could you tell me a bit more about what you need?";
            return;
        }
        var stockByLocation = new Dictionary<string, IReadOnlyList<CatalogStockInfo>>();
        foreach (var (pid, vid, qtyRaw, unitId, reason) in draft.Lines)
        {
            var qty = Math.Ceiling(qtyRaw);
            var key = pid + "|" + vid;
            if (result.Lines.Any(x => (x.ProductId + "|" + x.VariantId).Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
            var summary = _catalog.GetSummary(pid, string.IsNullOrEmpty(vid) ? null : vid);
            if (summary == null) continue;
            var quote = _catalog.GetPrice(summary.ProductId, summary.VariantId, qty, string.IsNullOrEmpty(unitId) ? null : unitId);
            var unitPrice = quote?.UnitPrice ?? summary.UnitPrice ?? 0;
            var line = new ProposalLine
            {
                ProductId = summary.ProductId,
                VariantId = summary.VariantId,
                Sku = summary.Sku,
                Name = summary.Name,
                Quantity = qty,
                Unit = summary.Unit ?? "",
                UnitId = string.IsNullOrEmpty(unitId) ? null : unitId,
                UnitPrice = unitPrice,
                UnitPriceFormatted = quote?.UnitPriceFormatted ?? summary.UnitPriceFormatted ?? _catalog.FormatMoney(unitPrice),
                LineTotal = Math.Round(unitPrice * qty, 2),
                TierLabel = quote?.TierLabel ?? "",
                Stock = summary.Stock,
                InStock = summary.NeverOutOfStock || summary.Stock >= qty,
                Reason = reason,
                IsContextProduct = request.ContextProductId != null && summary.ProductId.Equals(request.ContextProductId, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(request.ContextVariantId) || summary.VariantId.Equals(request.ContextVariantId, StringComparison.OrdinalIgnoreCase)),
            };
            line.LineTotalFormatted = quote?.LineTotalFormatted ?? _catalog.FormatMoney(line.LineTotal);
            line.StockLabel = line.InStock ? "In stock" : (summary.Stock <= 0 ? "Out of stock" : $"{summary.Stock:0.##} on hand");
            result.Lines.Add(line);
        }
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private void Report(AssistantEvent evt)
    {
        try { _progress?.Invoke(evt); } catch { /* progress is best effort */ }
    }

    private static Effort ParseEffort(string effort) => effort switch
    {
        "low" => Effort.Low,
        "high" or "xhigh" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    private static string Serialize(object value) => Truncate(JsonSerializer.Serialize(value, ResultJson), ToolResultCap);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + " ...[truncated]";

    private static string GetString(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var el)) return "";
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()?.Trim() ?? "",
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => el.GetRawText(),
        };
    }

    private static int GetInt(IReadOnlyDictionary<string, JsonElement> input, string key, int fallback)
    {
        if (!input.TryGetValue(key, out var el)) return fallback;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
        if (el.ValueKind == JsonValueKind.Number) return (int)Math.Round(el.GetDouble());
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
        return fallback;
    }

    private static double GetDouble(IReadOnlyDictionary<string, JsonElement> input, string key, double fallback)
    {
        if (!input.TryGetValue(key, out var el)) return fallback;
        if (el.ValueKind == JsonValueKind.Number) return el.GetDouble();
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return fallback;
    }
}
