using System.Text.Json;
using Truvio.Commerce.BuyingAssistant.Bom;
using Truvio.Commerce.BuyingAssistant.Core.Settings;
using Xunit;
using Keys = Truvio.Commerce.BuyingAssistant.Core.Settings.AssistantSettingKeys;

namespace Truvio.Commerce.BuyingAssistant.Tests;

public class BomCatalogFormatTests
{
    [Fact]
    public void Catalog_key_is_id_or_id_pipe_variant()
    {
        Assert.Equal("PROD1", BomEstimator.CatalogKey("PROD1", null));
        Assert.Equal("PROD1", BomEstimator.CatalogKey("PROD1", ""));
        Assert.Equal("PROD1|VO2", BomEstimator.CatalogKey("PROD1", "VO2"));
    }

    [Fact]
    public void Formats_a_full_catalog_line()
    {
        var line = BomPrompt.FormatLine("PROD1|VO2", "SKU-1", "Shingle, Charcoal", "BDL", "3 bundles per square", 41.5,
            new[] { (50.0, 39.9), (25.0, 40.5), (25.0, 40.25), (1.0, 41.5) }, 120.4, new[] { "Series=Duration", "Warranty=Limited lifetime" });
        Assert.Equal("PROD1|VO2 | SKU-1 | Shingle, Charcoal | per BDL | 3 bundles per square | 41.50 | 25+ 40.25, 50+ 39.90 | 120 | Series=Duration; Warranty=Limited lifetime", line);
    }

    [Fact]
    public void Empty_columns_are_dashes_and_values_stay_on_one_line()
    {
        var line = BomPrompt.FormatLine("P9", "S9", "Nails\nbox | 5 lb", "EA", "", 0, null, 0, null);
        Assert.Equal("P9 | S9 | Nails box / 5 lb | per EA | - | 0.00 | - | 0 | -", line);
    }

    [Fact]
    public void User_message_lists_catalog_context_and_job()
    {
        var a = new BomCatalogEntry("P1", "P1", "", "S1", "Starter", "BDL", "P1 | S1 | Starter");
        var msg = BomPrompt.BuildUserMessage(new[] { a }, a, "30 squares tear-off");
        Assert.StartsWith(BomPrompt.CatalogHeader, msg);
        Assert.Contains("P1 | S1 | Starter", msg);
        Assert.Contains("currently viewing product P1 (Starter).", msg);
        Assert.EndsWith("JOB DESCRIPTION:\n30 squares tear-off", msg);
    }

    [Fact]
    public void System_prompt_appends_the_shop_rules_after_the_mechanics()
    {
        Assert.Equal(BomPrompt.Mechanics, BomPrompt.BuildSystemPrompt("  "));
        var s = BomPrompt.BuildSystemPrompt("Waste 10%.");
        Assert.StartsWith(BomPrompt.Mechanics, s);
        Assert.EndsWith("SIZING RULES FOR THIS SHOP:\nWaste 10%.", s);
    }
}

public class BomToolInputTests
{
    private static IReadOnlyDictionary<string, JsonElement> Input(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void Parses_summary_assumptions_lines_and_notes()
    {
        var draft = BomToolInput.Parse(Input("""
            {"summary":" 30 sq ","assumptions":["10% waste",""," ",3],"notes":"check pitch",
             "lines":[{"product_id":"P1|V1","quantity":90,"reason":"30 sq x 3"},
                      {"product_id":"P2","quantity":2.2,"reason":"round up"},
                      {"product_id":"P3","quantity":"4","reason":"string qty"},
                      {"product_id":"","quantity":1,"reason":"no id"},
                      {"product_id":"P4","quantity":0,"reason":"zero"},
                      {"product_id":"P5","quantity":-3,"reason":"negative"},
                      "not an object"]}
            """));
        Assert.Equal("30 sq", draft.Summary);
        Assert.Equal("check pitch", draft.Notes);
        Assert.Equal(new[] { "10% waste" }, draft.Assumptions);
        Assert.Equal(3, draft.Lines.Count);
        Assert.Equal(("P1|V1", 90, "30 sq x 3"), draft.Lines[0]);
        Assert.Equal(3, draft.Lines[1].Quantity);
        Assert.Equal(4, draft.Lines[2].Quantity);
    }

    [Fact]
    public void Missing_fields_parse_to_empty()
    {
        var draft = BomToolInput.Parse(Input("{}"));
        Assert.Equal("", draft.Summary);
        Assert.Empty(draft.Assumptions);
        Assert.Empty(draft.Lines);
    }
}

public class BomCatalogPlanTests
{
    [Fact]
    public void Excludes_hidden_inactive_and_variant_masters_and_lists_visible_variants()
    {
        var plan = BomCatalogPlan.Build(new[]
        {
            new BomCatalogPlan.Row("A", "", true, false, 0),
            new BomCatalogPlan.Row("B", "", true, true, 0),
            new BomCatalogPlan.Row("C", "", false, false, 0),
            new BomCatalogPlan.Row("D", "", true, false, 3),
            new BomCatalogPlan.Row("D", "V1", true, false, 0),
            new BomCatalogPlan.Row("D", "V2", false, false, 0),
            new BomCatalogPlan.Row("D", "V3", true, true, 0),
            new BomCatalogPlan.Row("D", "V1", true, false, 0),
        });
        Assert.DoesNotContain("A", plan.ExcludedMasters);
        Assert.Contains("B", plan.ExcludedMasters);
        Assert.Contains("C", plan.ExcludedMasters);
        Assert.Contains("D", plan.ExcludedMasters);
        Assert.Equal(new[] { "V1" }, plan.VariantsByMaster["D"]);
    }
}

public class BomSettingsTests
{
    [Fact]
    public void Defaults_apply_when_nothing_is_saved()
    {
        var s = AssistantSettings.FromReader(_ => null);
        Assert.False(s.BomEnabled);
        Assert.Equal("", s.BomPackField);
        Assert.Equal("", s.BomUnitField);
        Assert.Equal(8000, s.BomMaxTokens);
        Assert.Equal("medium", s.BomEffort);
        Assert.Equal("Describe the job first (sizes, materials, what it is for).", s.BomShortPromptMessage);
        Assert.Empty(s.BomDescriptiveFields);
        Assert.DoesNotContain("roof", s.BomInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Saved_values_override_defaults()
    {
        var map = new Dictionary<string, string>
        {
            [Keys.BomEnabled] = "False",
            [Keys.BomDescriptiveFields] = "A, B;\nA",
            [Keys.BomEffort] = "HIGH",
            [Keys.BomMaxTokens] = "12",
        };
        var s = AssistantSettings.FromReader(k => map.TryGetValue(k, out var v) ? v : null);
        Assert.False(s.BomEnabled);
        Assert.Equal(new[] { "A", "B" }, s.BomDescriptiveFields);
        Assert.Equal("high", s.BomEffort);
        Assert.Equal(1024, s.BomMaxTokens);
    }

    [Fact]
    public void Dynamo_key_is_the_fallback_when_the_app_key_and_environment_are_empty()
    {
        var map = new Dictionary<string, string> { [Keys.ApiKey] = " ", [Keys.Fallbacks.DynamoApiKey] = "dynamo-marker" };
        var s = AssistantSettings.FromReader(k => map.TryGetValue(k, out var v) ? v : null, _ => null);
        Assert.Equal("dynamo-marker", s.ApiKey);
    }
}

public class BomRunnerTests
{
    private sealed class FakeCatalog : IBomCatalog
    {
        public List<BomCatalogEntry> Entries { get; } = new();
        public bool Loaded { get; private set; }

        public IReadOnlyList<BomCatalogEntry> Load() { Loaded = true; return Entries; }

        public BomLine? Price(BomCatalogEntry entry, int quantity, string reason)
        {
            var unit = quantity >= 25 ? 9.0 : 10.0;
            return new BomLine
            {
                ProductId = entry.ProductId, VariantId = entry.VariantId, Sku = entry.Sku, Name = entry.Name, Unit = entry.Unit,
                Quantity = quantity, UnitPrice = unit, LineTotal = Math.Round(unit * quantity, 2), TierLabel = quantity >= 25 ? "25+ tier" : "",
                Stock = 30, InStock = quantity <= 30, Reason = reason,
            };
        }
    }

    private static AssistantSettings Settings(bool enabled = true, string? key = "k") =>
        new() { ApiKey = key, BomEnabled = enabled, Model = "claude-test" };

    private static BomRequest Request(string prompt = "30 squares, 6/12 pitch, full tear-off", string? context = null) =>
        new() { Prompt = prompt, ContextProductKey = context, LanguageId = "LANG1", ShopId = "SHOP1", CurrencyCode = "USD", CountryCode = "US" };

    private static FakeCatalog Catalog()
    {
        var c = new FakeCatalog();
        c.Entries.Add(new BomCatalogEntry("P1|V1", "P1", "V1", "S1", "Shingle", "BDL", "P1|V1 | S1 | Shingle"));
        c.Entries.Add(new BomCatalogEntry("P2", "P2", "", "S2", "Starter", "BDL", "P2 | S2 | Starter"));
        return c;
    }

    private static Func<BomModelCall, BomModelAnswer> Answer(string json) =>
        _ => new BomModelAnswer(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json), 1200, 300);

    private static Func<BomModelCall, BomModelAnswer> MustNotCall => _ => throw new Xunit.Sdk.XunitException("model must not be called");

    [Fact]
    public void Switched_off_makes_no_call()
    {
        var catalog = Catalog();
        var r = BomRunner.Run(Request(), Settings(enabled: false), () => catalog, MustNotCall, null);
        Assert.Equal("The bill-of-materials estimator is switched off.", r.Error);
        Assert.False(catalog.Loaded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  roof  ")]
    [InlineData("1234567")]
    public void Short_prompt_returns_the_default_message(string prompt)
    {
        var r = BomRunner.Run(Request(prompt), Settings(), Catalog, MustNotCall, null);
        Assert.Equal("Describe the job first (sizes, materials, what it is for).", r.Error);
    }

    [Fact]
    public void Short_prompt_message_follows_the_setting()
    {
        var r = BomRunner.Run(Request("short"), Settings() with { BomShortPromptMessage = "Tell us about the pool." }, Catalog, MustNotCall, null);
        Assert.Equal("Tell us about the pool.", r.Error);
    }

    [Fact]
    public void Missing_key_is_reported()
    {
        var r = BomRunner.Run(Request(), Settings(key: null), Catalog, MustNotCall, null);
        Assert.Equal("The assistant is not configured (no Anthropic API key found).", r.Error);
    }

    [Fact]
    public void Empty_catalog_is_reported()
    {
        var r = BomRunner.Run(Request(), Settings(), () => new FakeCatalog(), MustNotCall, null);
        Assert.Equal("No sellable products found for this shop.", r.Error);
    }

    [Fact]
    public void Sdk_exception_is_logged_and_named()
    {
        Exception? logged = null;
        var r = BomRunner.Run(Request(), Settings(), Catalog, _ => throw new InvalidOperationException("401"), (_, ex) => logged = ex);
        Assert.Equal("The assistant could not size this job right now. InvalidOperationException", r.Error);
        Assert.IsType<InvalidOperationException>(logged);
    }

    [Fact]
    public void No_tool_call_is_reported()
    {
        var r = BomRunner.Run(Request(), Settings(), Catalog, _ => new BomModelAnswer(null, 5, 6), null);
        Assert.Equal(BomMessages.NoEstimate, r.Error);
        Assert.Equal(5, r.InputTokens);
    }

    [Fact]
    public void Prices_known_lines_once_and_flags_the_context_product()
    {
        BomModelCall? seen = null;
        var answer = Answer("""
            {"summary":"30 sq","assumptions":["10% waste"],"notes":"",
             "lines":[{"product_id":"p1|v1","quantity":99,"reason":"33 sq x 3"},
                      {"product_id":"P1|V1","quantity":5,"reason":"duplicate"},
                      {"product_id":"NOPE","quantity":5,"reason":"invented"},
                      {"product_id":"P2","quantity":4,"reason":"eaves"}]}
            """);
        var r = BomRunner.Run(Request(context: "P1|V1"),Settings(), Catalog, c => { seen = c; return answer(c); }, null);

        Assert.Null(r.Error);
        Assert.Equal("claude-test", r.ModelId);
        Assert.Equal(1200, r.InputTokens);
        Assert.Equal(300, r.OutputTokens);
        Assert.Equal("30 sq", r.Summary);
        Assert.Equal(2, r.Lines.Count);
        Assert.Equal("P1", r.Lines[0].ProductId);
        Assert.Equal("V1", r.Lines[0].VariantId);
        Assert.Equal(99, r.Lines[0].Quantity);
        Assert.Equal("25+ tier", r.Lines[0].TierLabel);
        Assert.True(r.Lines[0].IsContextProduct);
        Assert.False(r.Lines[1].IsContextProduct);
        Assert.Equal(891 + 40, r.Total);

        Assert.NotNull(seen);
        Assert.Equal("k", seen!.ApiKey);
        Assert.Equal(8000, seen.MaxTokens);
        Assert.Contains("currently viewing product P1|V1 (Shingle)", seen.UserMessage);
        Assert.Contains("SIZING RULES FOR THIS SHOP:", seen.SystemPrompt);
    }
}
