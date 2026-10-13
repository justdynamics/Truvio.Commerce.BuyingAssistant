using Dynamicweb.Security.UserManagement;

namespace Truvio.Commerce.BuyingAssistant.Bom;

/// <summary>One bill-of-materials request, as a page template knows it.</summary>
public sealed class BomRequest
{
    /// <summary>The job description typed by the customer.</summary>
    public string Prompt { get; set; } = "";

    /// <summary>The product the customer is viewing, as <see cref="BomEstimator.CatalogKey"/> builds it (id or id|variant). Optional.</summary>
    public string? ContextProductKey { get; set; }

    /// <summary>The signed-in user (customer prices and price agreements); null for an anonymous visitor.</summary>
    public User? User { get; set; }

    /// <summary>Ecommerce language id (e.g. LANG1). Blank uses the current ecommerce context.</summary>
    public string LanguageId { get; set; } = "";

    /// <summary>Shop whose products the estimator may pick from. Required: without a shop the catalog is empty.</summary>
    public string? ShopId { get; set; }

    /// <summary>Currency code (e.g. USD). Blank uses the current ecommerce context.</summary>
    public string CurrencyCode { get; set; } = "";

    /// <summary>Two-letter country code (e.g. US). Blank uses the current ecommerce context.</summary>
    public string CountryCode { get; set; } = "";

    /// <summary>Extra instructions for this one request (e.g. from a paragraph field), added after the global sizing instructions.</summary>
    public string? ExtraInstructions { get; set; }
}

/// <summary>One priced line of a bill of materials.</summary>
public sealed class BomLine
{
    public string ProductId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public string Unit { get; set; } = "EA";
    public double UnitPrice { get; set; }
    public double LineTotal { get; set; }

    /// <summary>"25+ tier" when a quantity break applies at this quantity, else empty.</summary>
    public string TierLabel { get; set; } = "";
    public double Stock { get; set; }
    public bool InStock { get; set; }
    public string Reason { get; set; } = "";
    public bool IsContextProduct { get; set; }
}

/// <summary>The estimator's answer. <see cref="Error"/> is set instead of lines when no estimate could be made.</summary>
public sealed class BomResult
{
    public string Summary { get; set; } = "";
    public List<string> Assumptions { get; } = new();
    public List<BomLine> Lines { get; } = new();
    public string Notes { get; set; } = "";
    public double Total => Lines.Sum(l => l.LineTotal);
    public string? Error { get; set; }
    public string ModelId { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}
