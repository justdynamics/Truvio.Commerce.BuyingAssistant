using System.Globalization;
using System.Text;
using System.Text.Json;
using Truvio.Commerce.BuyingAssistant.Core.Settings;

namespace Truvio.Commerce.BuyingAssistant.Bom;

/// <summary>The fixed error texts of the estimator. Page templates and proof harnesses match on them.</summary>
public static class BomMessages
{
    public const string Disabled = "The bill-of-materials estimator is switched off.";
    public const string NoApiKey = "The assistant is not configured (no Anthropic API key found).";
    public const string CouldNotSizePrefix = "The assistant could not size this job right now. ";
    public const string NoCatalog = "No sellable products found for this shop.";
    public const string NoEstimate = "The assistant did not return an estimate. Try describing the job in more detail.";

    /// <summary>Minimum length of a job description, in characters after trimming.</summary>
    public const int MinPromptLength = 8;

    public static string CouldNotSize(Exception ex) => CouldNotSizePrefix + ex.GetType().Name;
}

/// <summary>One product or variant the model may pick, with its catalog line.</summary>
internal sealed record BomCatalogEntry(string Key, string ProductId, string VariantId, string Sku, string Name, string Unit, string Line);

/// <summary>The platform side of the estimator: lists what may be sold and prices a chosen line.</summary>
internal interface IBomCatalog
{
    IReadOnlyList<BomCatalogEntry> Load();

    /// <summary>Prices the entry at the quantity through the platform (customer prices, quantity breaks, stock). Null drops the line.</summary>
    BomLine? Price(BomCatalogEntry entry, int quantity, string reason);
}

/// <summary>Everything one model call needs.</summary>
internal sealed record BomModelCall(
    string ApiKey,
    string Model,
    int MaxTokens,
    string Effort,
    int TimeoutSeconds,
    string SystemPrompt,
    string? ExtraInstructions,
    string UserMessage);

/// <summary>What came back: the submit_estimate tool input (null when the model did not call it) and usage.</summary>
internal sealed record BomModelAnswer(IReadOnlyDictionary<string, JsonElement>? ToolInput, long InputTokens, long OutputTokens);

/// <summary>A parsed submit_estimate call before pricing.</summary>
internal sealed class BomDraft
{
    public string Summary { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> Assumptions { get; } = new();
    public List<(string Key, int Quantity, string Reason)> Lines { get; } = new();
}

/// <summary>Prompt texts and the catalog line format.</summary>
internal static class BomPrompt
{
    public const string ToolName = "submit_estimate";

    public const string CatalogHeader =
        "CATALOG (one product per line: id | sku | name | sold per | pack/coverage | unit price | quantity breaks | stock on hand | attributes):";

    /// <summary>The catalog mechanics the app always adds; the shop's own sizing rules follow them.</summary>
    public const string Mechanics =
        "You size a bill of materials for a job a customer describes in plain language, using ONLY products from the catalog supplied in the user message; never invent ids or SKUs.\n" +
        "- Return every product id exactly as listed. Ids may carry a variant suffix (PROD1|VO1).\n" +
        "- Quantities are whole sellable units: use the 'sold per' and pack/coverage columns and round every line UP.\n" +
        "- When a product comes in several variants (colours, sizes), pick the one the customer asked for, otherwise the one they are viewing, otherwise the first listed.\n" +
        "- If the customer is viewing a product that fits a role in the job, prefer it for that role.\n" +
        "- If the job is outside what this catalog supplies, or the description is too thin to size, return zero lines and say in the summary what you need.\n" +
        "- Keep reasons short and concrete (the math, not prose).\n" +
        "Respond only by calling the " + ToolName + " tool.";

    public static string BuildSystemPrompt(string? sizingInstructions)
    {
        var rules = (sizingInstructions ?? "").Trim();
        return rules.Length == 0 ? Mechanics : Mechanics + "\n\nSIZING RULES FOR THIS SHOP:\n" + rules;
    }

    public static string BuildUserMessage(IEnumerable<BomCatalogEntry> entries, BomCatalogEntry? contextEntry, string prompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CatalogHeader);
        foreach (var e in entries) sb.AppendLine(e.Line);
        if (contextEntry != null) sb.Append("\nThe customer is currently viewing product ").Append(contextEntry.Key).Append(" (").Append(contextEntry.Name).Append(").");
        sb.Append("\n\nJOB DESCRIPTION:\n").Append(prompt);
        return sb.ToString();
    }

    /// <summary>
    /// One catalog line: id | sku | name | per UNIT | pack | unit price | breaks | stock | attributes.
    /// Empty columns are "-"; numbers use the invariant culture.
    /// </summary>
    public static string FormatLine(string key, string? sku, string? name, string unit, string? pack, double unitPrice,
        IEnumerable<(double Quantity, double UnitPrice)>? breaks, double stock, IEnumerable<string>? attributes)
    {
        var breakText = FormatBreaks(breaks);
        var attrs = attributes?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList() ?? new List<string>();
        return string.Join(" | ",
            key,
            Clean(sku),
            Clean(name),
            "per " + unit,
            string.IsNullOrWhiteSpace(pack) ? "-" : Clean(pack),
            unitPrice.ToString("0.00", CultureInfo.InvariantCulture),
            breakText.Length == 0 ? "-" : breakText,
            stock.ToString("0", CultureInfo.InvariantCulture),
            attrs.Count == 0 ? "-" : string.Join("; ", attrs.Select(Clean)));
    }

    /// <summary>"25+ 41.10, 50+ 39.90": one entry per whole break quantity above 1, cheapest amount at that break.</summary>
    public static string FormatBreaks(IEnumerable<(double Quantity, double UnitPrice)>? breaks)
    {
        if (breaks == null) return "";
        return string.Join(", ", breaks
            .Where(b => b.Quantity > 1)
            .GroupBy(b => (int)b.Quantity)
            .OrderBy(g => g.Key)
            .Select(g => g.Key.ToString(CultureInfo.InvariantCulture) + "+ " + g.Min(b => b.UnitPrice).ToString("0.00", CultureInfo.InvariantCulture)));
    }

    /// <summary>Keeps one product on one line: no line breaks, no column separators inside a value.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/").Trim();
    }
}

/// <summary>Parses the submit_estimate tool input.</summary>
internal static class BomToolInput
{
    public static BomDraft Parse(IReadOnlyDictionary<string, JsonElement> input)
    {
        var draft = new BomDraft
        {
            Summary = GetString(input, "summary"),
            Notes = GetString(input, "notes"),
        };
        if (input.TryGetValue("assumptions", out var assumptions) && assumptions.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assumptions.EnumerateArray())
            {
                var s = a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                if (!string.IsNullOrWhiteSpace(s)) draft.Assumptions.Add(s.Trim());
            }
        }
        if (input.TryGetValue("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in lines.EnumerateArray())
            {
                if (l.ValueKind != JsonValueKind.Object) continue;
                var key = l.TryGetProperty("product_id", out var pe) && pe.ValueKind == JsonValueKind.String ? (pe.GetString() ?? "").Trim() : "";
                var reason = l.TryGetProperty("reason", out var re) && re.ValueKind == JsonValueKind.String ? (re.GetString() ?? "").Trim() : "";
                var qty = l.TryGetProperty("quantity", out var qe) ? ParseQuantity(qe) : 0;
                if (key.Length == 0 || qty <= 0) continue;
                draft.Lines.Add((key, qty, reason));
            }
        }
        return draft;
    }

    /// <summary>Whole sellable units: integers as given, fractions rounded up, numeric strings accepted, anything else 0.</summary>
    public static int ParseQuantity(JsonElement value)
    {
        double d;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var i)) return i;
            d = value.GetDouble();
        }
        else if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            d = parsed;
        }
        else
        {
            return 0;
        }
        if (double.IsNaN(d) || d <= 0) return 0;
        return d >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(d - 1e-9);
    }

    private static string GetString(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        return input.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "").Trim() : "";
    }
}

/// <summary>
/// Which masters and variants may be offered, from the EcomProducts rows of one language: hidden or
/// inactive masters are left out, and masters that carry variants are listed through their active,
/// visible variants (a master with variants cannot go into the cart without a VariantId).
/// </summary>
internal sealed class BomCatalogPlan
{
    public HashSet<string> ExcludedMasters { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> VariantsByMaster { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal sealed record Row(string ProductId, string VariantId, bool Active, bool Hidden, int VariantCounter);

    public static BomCatalogPlan Build(IEnumerable<Row> rows)
    {
        var plan = new BomCatalogPlan();
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.ProductId)) continue;
            if (string.IsNullOrEmpty(r.VariantId))
            {
                if (r.Hidden || !r.Active || r.VariantCounter > 0) plan.ExcludedMasters.Add(r.ProductId);
            }
            else if (r.Active && !r.Hidden)
            {
                if (!plan.VariantsByMaster.TryGetValue(r.ProductId, out var list)) plan.VariantsByMaster[r.ProductId] = list = new List<string>();
                if (!list.Contains(r.VariantId, StringComparer.OrdinalIgnoreCase)) list.Add(r.VariantId);
            }
        }
        return plan;
    }
}

/// <summary>The estimator flow, free of Dynamicweb and of the SDK so every branch can be tested.</summary>
internal static class BomRunner
{
    public static BomResult Run(
        BomRequest request,
        AssistantSettings settings,
        Func<IBomCatalog> catalogFactory,
        Func<BomModelCall, BomModelAnswer> callModel,
        Action<string, Exception>? log)
    {
        var result = new BomResult { ModelId = settings.Model };
        if (!settings.BomEnabled)
        {
            result.Error = BomMessages.Disabled;
            return result;
        }

        var prompt = (request.Prompt ?? "").Trim();
        if (prompt.Length < BomMessages.MinPromptLength)
        {
            result.Error = settings.BomShortPromptMessage;
            return result;
        }
        if (prompt.Length > settings.MaxPromptLength) prompt = prompt[..settings.MaxPromptLength];

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            result.Error = BomMessages.NoApiKey;
            return result;
        }

        try
        {
            var catalog = catalogFactory();
            var entries = catalog.Load();
            if (entries.Count == 0)
            {
                result.Error = BomMessages.NoCatalog;
                return result;
            }

            var byKey = new Dictionary<string, BomCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries) byKey.TryAdd(e.Key, e);
            var contextKey = (request.ContextProductKey ?? "").Trim();
            byKey.TryGetValue(contextKey, out var contextEntry);

            var call = new BomModelCall(
                settings.ApiKey!,
                settings.Model,
                settings.BomMaxTokens,
                settings.BomEffort,
                settings.TimeoutSeconds,
                BomPrompt.BuildSystemPrompt(settings.BomInstructions),
                string.IsNullOrWhiteSpace(request.ExtraInstructions) ? null : request.ExtraInstructions.Trim(),
                BomPrompt.BuildUserMessage(byKey.Values, contextEntry, prompt));

            var answer = callModel(call);
            result.InputTokens = answer.InputTokens;
            result.OutputTokens = answer.OutputTokens;
            if (answer.ToolInput == null)
            {
                result.Error = BomMessages.NoEstimate;
                return result;
            }

            var draft = BomToolInput.Parse(answer.ToolInput);
            result.Summary = draft.Summary;
            result.Notes = draft.Notes;
            result.Assumptions.AddRange(draft.Assumptions);

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, quantity, reason) in draft.Lines)
            {
                if (!byKey.TryGetValue(key, out var entry) || !taken.Add(entry.Key)) continue;
                var line = catalog.Price(entry, quantity, reason);
                if (line == null) continue;
                line.IsContextProduct = contextEntry != null && string.Equals(entry.Key, contextEntry.Key, StringComparison.OrdinalIgnoreCase);
                result.Lines.Add(line);
            }
        }
        catch (Exception ex)
        {
            try { log?.Invoke("BOM estimate failed", ex); } catch { /* logging is best effort */ }
            result.Error = BomMessages.CouldNotSize(ex);
        }
        return result;
    }
}
