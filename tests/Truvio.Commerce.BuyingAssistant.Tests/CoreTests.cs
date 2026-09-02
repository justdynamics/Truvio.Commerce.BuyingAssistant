using System.Text.Json;
using Truvio.Commerce.BuyingAssistant.Core.Assistant;
using Truvio.Commerce.BuyingAssistant.Core.Catalog;
using Truvio.Commerce.BuyingAssistant.Core.Mcp;
using Truvio.Commerce.BuyingAssistant.Core.Settings;
using Truvio.Commerce.BuyingAssistant.Core.Skills;
using Xunit;

namespace Truvio.Commerce.BuyingAssistant.Tests;

public class SkillParserTests
{
    [Fact]
    public void Parses_named_sections_and_ignores_preamble()
    {
        var text = "intro text\n## Skill: Pool opening\nWhen: opening a pool.\nDo: size chemicals.\n\n## Skill: Roof takeoff\nWhen: roofing.\n";
        var skills = SkillParser.Parse(text);
        Assert.Equal(2, skills.Count);
        Assert.Equal("Pool opening", skills[0].Name);
        Assert.Contains("size chemicals", skills[0].Body);
        Assert.Equal("Roof takeoff", skills[1].Name);
    }

    [Fact]
    public void Filter_matches_names_and_prefixes_case_insensitively()
    {
        var skills = SkillParser.Parse("## Skill: Pool opening\na\n## Skill: Pool closing\nb\n## Skill: Roof\nc");
        Assert.Equal(2, SkillParser.Filter(skills, "pool*").Count);
        Assert.Single(SkillParser.Filter(skills, "ROOF"));
        Assert.Equal(3, SkillParser.Filter(skills, "").Count);
    }
}

public class ToolNamePolicyTests
{
    [Fact]
    public void Blocks_write_tools_unless_allowed()
    {
        var patterns = ToolNamePolicy.ParsePatterns("get_*, search_orders\ncreate_products");
        Assert.True(ToolNamePolicy.IsAllowed("get_products", patterns, false));
        Assert.True(ToolNamePolicy.IsAllowed("search_orders", patterns, false));
        Assert.False(ToolNamePolicy.IsAllowed("create_products", patterns, false));
        Assert.True(ToolNamePolicy.IsAllowed("create_products", patterns, true));
        Assert.False(ToolNamePolicy.IsAllowed("delete_users", patterns, true));
    }

    [Fact]
    public void Empty_allowlist_allows_nothing()
    {
        Assert.False(ToolNamePolicy.IsAllowed("get_products", ToolNamePolicy.ParsePatterns(""), true));
    }
}

public class McpClientParsingTests
{
    [Fact]
    public void Extracts_message_from_sse_body()
    {
        var body = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"tools\":[]}}\n\n";
        var msg = McpClient.ExtractMessage(body, "text/event-stream", 7);
        Assert.NotNull(msg);
        Assert.True(msg!.Value.TryGetProperty("result", out _));
    }

    [Fact]
    public void Extracts_message_from_plain_json_body()
    {
        var body = "{\"jsonrpc\":\"2.0\",\"id\":3,\"error\":{\"code\":-1,\"message\":\"nope\"}}";
        var msg = McpClient.ExtractMessage(body, "application/json", 3);
        Assert.NotNull(msg);
        Assert.Equal("nope", msg!.Value.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public void Falls_back_to_last_result_when_id_differs()
    {
        var body = "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\"}\n\ndata: {\"jsonrpc\":\"2.0\",\"id\":99,\"result\":{\"ok\":true}}\n\n";
        var msg = McpClient.ExtractMessage(body, "text/event-stream", 1);
        Assert.NotNull(msg);
        Assert.True(msg!.Value.GetProperty("result").GetProperty("ok").GetBoolean());
    }
}

public class SettingsTests
{
    [Fact]
    public void Reads_defaults_and_fallback_api_key()
    {
        var values = new Dictionary<string, string?>
        {
            [AssistantSettingKeys.Fallbacks.DynamoApiKey] = "sk-dynamo",
            [AssistantSettingKeys.McpMode] = "direct",
            [AssistantSettingKeys.McpUrl] = "https://localhost/admin/mcp",
            [AssistantSettingKeys.Effort] = "HIGH",
            [AssistantSettingKeys.MaxIterations] = "3",
        };
        var s = AssistantSettings.FromReader(k => values.TryGetValue(k, out var v) ? v : null);
        Assert.Equal("sk-dynamo", s.ApiKey);
        Assert.Equal("claude-opus-5", s.Model);
        Assert.Equal("high", s.Effort);
        Assert.Equal(McpMode.Direct, s.McpMode);
        Assert.True(s.McpConfigured);
        Assert.Equal(3, s.MaxIterations);
        Assert.False(s.AllowAnonymous);
    }

    [Fact]
    public void Environment_variable_beats_dynamo_key()
    {
        var values = new Dictionary<string, string?> { [AssistantSettingKeys.Fallbacks.DynamoApiKey] = "sk-dynamo" };
        var s = AssistantSettings.FromReader(k => values.TryGetValue(k, out var v) ? v : null, _ => "sk-env");
        Assert.Equal("sk-env", s.ApiKey);
    }
}

public class PromptBuilderTests
{
    [Fact]
    public void Stable_part_contains_instructions_and_skills()
    {
        var settings = new AssistantSettings { Instructions = "We sell pool supplies.", AssistantName = "Horner Helper" };
        var skills = SkillParser.Parse("## Skill: Dosing\nUse 1 lb per 10k gallons.");
        var text = PromptBuilder.BuildStablePart(settings, skills, false, true);
        Assert.Contains("Horner Helper", text);
        Assert.Contains("We sell pool supplies.", text);
        Assert.Contains("## Dosing", text);
        Assert.Contains(PromptBuilder.ProposalToolName, text);
    }

    [Fact]
    public void Context_part_mentions_viewed_product_and_customer()
    {
        var ctx = new CatalogContextInfo("ENU", "USD", "US", "SHOP1", "Main", false, "Fort Lauderdale", true, "Pat", "Blue Pools", "C-100");
        var req = new AssistantRequest { PlacementMode = "product", ContextProductId = "PROD1", ContextProductName = "Pump" };
        var text = PromptBuilder.BuildContextPart(ctx, req, new DateTime(2026, 9, 2));
        Assert.Contains("Pump", text);
        Assert.Contains("Blue Pools", text);
        Assert.Contains("Fort Lauderdale", text);
    }
}

public class ConversationStoreTests
{
    [Fact]
    public void Conversations_are_bound_to_their_owner()
    {
        var store = new ConversationStore<string>();
        store.Put("c1", "owner-a", new List<string> { "hello" });
        Assert.Single(store.Get("c1", "owner-a"));
        Assert.Empty(store.Get("c1", "owner-b"));
    }
}

public class ProposalSerializationTests
{
    [Fact]
    public void Result_serializes_camel_case_with_total()
    {
        var r = new AssistantResult { ConversationId = "x", Summary = "s" };
        r.Lines.Add(new ProposalLine { ProductId = "P1", Quantity = 2, UnitPrice = 10, LineTotal = 20 });
        var json = JsonSerializer.Serialize(r, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Contains("\"total\":20", json);
        Assert.Contains("\"hasProposal\":true", json);
    }
}
