namespace Truvio.Commerce.BuyingAssistant.Core.Assistant;

public sealed class AssistantRequest
{
    public string ConversationId { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ContextProductId { get; set; }
    public string? ContextVariantId { get; set; }
    public string? ContextProductName { get; set; }
    /// <summary>Extra instructions from the paragraph (placement specific).</summary>
    public string? ExtraInstructions { get; set; }
    /// <summary>Comma separated skill names the placement limits the assistant to. Blank = all.</summary>
    public string? SkillFilter { get; set; }
    /// <summary>Extra "## Skill:" sections from the paragraph, merged with the skills from the settings.</summary>
    public string? ExtraSkills { get; set; }
    /// <summary>"product" when placed on a product page with a product in context, otherwise "standalone".</summary>
    public string PlacementMode { get; set; } = "standalone";
    /// <summary>Manual edits the shopper made to the previous proposal (quantity changes, removed lines) before this turn.</summary>
    public List<ProposalEdit>? Edits { get; set; }
}

public sealed class ProposalLine
{
    public string ProductId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public double Quantity { get; set; }
    public string Unit { get; set; } = "";
    public string? UnitId { get; set; }
    public double UnitPrice { get; set; }
    public string UnitPriceFormatted { get; set; } = "";
    public double LineTotal { get; set; }
    public string LineTotalFormatted { get; set; } = "";
    public string TierLabel { get; set; } = "";
    public double Stock { get; set; }
    public bool InStock { get; set; }
    public string StockLabel { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool IsContextProduct { get; set; }
    public string? ImageUrl { get; set; }
    public string? Url { get; set; }
}

public sealed class AssistantResult
{
    public string ConversationId { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Assumptions { get; } = new();
    public List<ProposalLine> Lines { get; } = new();
    public string Notes { get; set; } = "";
    /// <summary>Set when the assistant needs more information before proposing anything.</summary>
    public string? FollowUpQuestion { get; set; }
    public double Total => Lines.Sum(l => l.LineTotal);
    public string TotalFormatted { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public string? Error { get; set; }
    public string ModelId { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public int ToolCalls { get; set; }
    public int Iterations { get; set; }
    public double ElapsedSeconds { get; set; }
    public bool HasProposal => Lines.Count > 0;
}

/// <summary>Progress events the engine emits while it works (rendered live by the storefront widget).</summary>
public sealed record AssistantEvent(string Type, string? Text = null, object? Data = null)
{
    public static AssistantEvent Status(string text) => new("status", text);
    public static AssistantEvent ToolCall(string tool, string summary) => new("tool_call", summary, new { tool });
    public static AssistantEvent ToolResult(string tool, string summary) => new("tool_result", summary, new { tool });
    public static AssistantEvent Narrative(string text) => new("text", text);
}
