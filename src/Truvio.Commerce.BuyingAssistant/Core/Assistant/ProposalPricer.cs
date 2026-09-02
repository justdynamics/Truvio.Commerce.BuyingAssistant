using System.Globalization;
using System.Text;
using Truvio.Commerce.BuyingAssistant.Core.Catalog;

namespace Truvio.Commerce.BuyingAssistant.Core.Assistant;

/// <summary>
/// Prices one proposal line through the catalog gateway. Used when the model submits a proposal
/// and again when the shopper edits quantities in the widget, so both paths apply the same
/// customer prices, quantity breaks and stock rules.
/// </summary>
public static class ProposalPricer
{
    public static ProposalLine? Price(ICatalogGateway catalog, string productId, string? variantId, double quantityRaw, string? unitId, string reason, string? contextProductId = null, string? contextVariantId = null)
    {
        var qty = Math.Max(1, Math.Ceiling(quantityRaw));
        var summary = catalog.GetSummary(productId, string.IsNullOrEmpty(variantId) ? null : variantId);
        if (summary == null) return null;
        var quote = catalog.GetPrice(summary.ProductId, summary.VariantId, qty, string.IsNullOrEmpty(unitId) ? null : unitId);
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
            UnitPriceFormatted = quote?.UnitPriceFormatted ?? summary.UnitPriceFormatted ?? catalog.FormatMoney(unitPrice),
            LineTotal = Math.Round(unitPrice * qty, 2),
            TierLabel = quote?.TierLabel ?? "",
            Stock = summary.Stock,
            InStock = summary.NeverOutOfStock || summary.Stock >= qty,
            Reason = reason ?? "",
            IsContextProduct = contextProductId != null && summary.ProductId.Equals(contextProductId, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(contextVariantId) || summary.VariantId.Equals(contextVariantId, StringComparison.OrdinalIgnoreCase)),
        };
        line.LineTotalFormatted = quote?.LineTotalFormatted ?? catalog.FormatMoney(line.LineTotal);
        line.StockLabel = line.InStock ? "In stock" : (summary.Stock <= 0 ? "Out of stock" : $"{summary.Stock:0.##} on hand");
        return line;
    }

    /// <summary>
    /// Turns the shopper's edits of the previous proposal into a short note the model sees
    /// before the follow-up message, so the next turn builds on the edited list.
    /// </summary>
    public static string? DescribeEdits(IReadOnlyList<ProposalEdit>? edits)
    {
        if (edits == null || edits.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var e in edits.Take(60))
        {
            var label = string.IsNullOrWhiteSpace(e.Name) ? e.ProductId : e.Name;
            if (!string.IsNullOrWhiteSpace(e.Sku)) label += " (" + e.Sku + ")";
            if (e.Removed)
                sb.Append("- removed ").AppendLine(label);
            else if (e.Quantity > 0 && Math.Abs(e.Quantity - e.OriginalQuantity) > 0.0001)
                sb.Append("- changed ").Append(label).Append(" from ").Append(e.OriginalQuantity.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(" to ").AppendLine(e.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
        }
        if (sb.Length == 0) return null;
        return "The shopper edited the previous proposal by hand before sending this message:\n" + sb.ToString().TrimEnd() +
               "\nTreat the edited list as the current proposal: keep these changes unless the shopper asks otherwise, and include every kept line in your next submit_proposal.";
    }
}

/// <summary>One manual change the shopper made to a proposal line in the widget.</summary>
public sealed class ProposalEdit
{
    public string ProductId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public double Quantity { get; set; }
    public double OriginalQuantity { get; set; }
    public bool Removed { get; set; }
}
