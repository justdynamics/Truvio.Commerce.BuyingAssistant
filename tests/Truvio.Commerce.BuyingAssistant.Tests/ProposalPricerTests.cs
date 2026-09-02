using Truvio.Commerce.BuyingAssistant.Core.Assistant;
using Truvio.Commerce.BuyingAssistant.Core.Catalog;
using Xunit;

namespace Truvio.Commerce.BuyingAssistant.Tests;

public class ProposalPricerTests
{
    private sealed class FakeCatalog : ICatalogGateway
    {
        public CatalogContextInfo Context => new("LANG1", "USD", "US", "SHOP1", "Shop", false, "Main", true, "Sam", "Acme", "C1");

        public CatalogProductSummary? GetSummary(string productId, string? variantId)
            => productId == "P1"
                ? new CatalogProductSummary("P1", variantId ?? "", "SKU-1", "Shingle bundle", "bundle", 30, "$30.00", 12, false, null, null, null, false)
                : null;

        public CatalogPriceQuote? GetPrice(string productId, string? variantId, double quantity, string? unitId)
        {
            // Quantity break at 10: unit price drops from 30 to 25.
            var unit = quantity >= 10 ? 25 : 30;
            return new CatalogPriceQuote(productId, variantId ?? "", quantity, unitId, unit, $"${unit:0.00}", unit * quantity, $"${unit * quantity:0.00}", quantity >= 10 ? "10+ price" : "", "USD");
        }

        public string FormatMoney(double amount) => $"${amount:0.00}";

        public IReadOnlyList<CatalogProductSummary> Search(string query, int max) => Array.Empty<CatalogProductSummary>();
        public CatalogProductDetail? GetProduct(string productId, string? variantId) => null;
        public IReadOnlyList<CatalogStockInfo> GetStock(string productId, string? variantId) => Array.Empty<CatalogStockInfo>();
        public IReadOnlyList<CatalogCategory> GetCategories(string? parentGroupId) => Array.Empty<CatalogCategory>();
        public IReadOnlyList<CatalogProductSummary> GetProductsInCategory(string groupId, int max) => Array.Empty<CatalogProductSummary>();
        public CustomerContextInfo GetCustomerContext() => new(true, "Sam", "Acme", "C1", null, Array.Empty<string>(), "USD", "Main", Array.Empty<CartLineInfo>());
        public IReadOnlyList<PastOrderInfo> GetRecentOrders(int max) => Array.Empty<PastOrderInfo>();
    }

    [Fact]
    public void Price_applies_quantity_break_and_stock_at_new_quantity()
    {
        var catalog = new FakeCatalog();
        var small = ProposalPricer.Price(catalog, "P1", null, 4, null, "4 squares");
        var large = ProposalPricer.Price(catalog, "P1", null, 14, null, "14 squares");

        Assert.NotNull(small);
        Assert.Equal(30, small!.UnitPrice);
        Assert.Equal("", small.TierLabel);
        Assert.True(small.InStock);

        Assert.NotNull(large);
        Assert.Equal(25, large!.UnitPrice);
        Assert.Equal("10+ price", large.TierLabel);
        Assert.Equal(350, large.LineTotal);
        Assert.False(large.InStock); // 12 on hand
        Assert.Equal("12 on hand", large.StockLabel);
    }

    [Fact]
    public void Price_rounds_up_and_rejects_unknown_products()
    {
        var catalog = new FakeCatalog();
        var line = ProposalPricer.Price(catalog, "P1", null, 2.2, null, "");
        Assert.Equal(3, line!.Quantity);
        Assert.Equal(1, ProposalPricer.Price(catalog, "P1", null, 0, null, "")!.Quantity);
        Assert.Null(ProposalPricer.Price(catalog, "NOPE", null, 1, null, ""));
    }

    [Fact]
    public void DescribeEdits_lists_removed_and_changed_lines_only()
    {
        var edits = new List<ProposalEdit>
        {
            new() { ProductId = "P1", Sku = "SKU-1", Name = "Shingle bundle", Quantity = 6, OriginalQuantity = 8 },
            new() { ProductId = "P2", Sku = "SKU-2", Name = "Ridge cap", Quantity = 2, OriginalQuantity = 2, Removed = true },
            new() { ProductId = "P3", Sku = "SKU-3", Name = "Nails", Quantity = 1, OriginalQuantity = 1 },
        };
        var note = ProposalPricer.DescribeEdits(edits);
        Assert.NotNull(note);
        Assert.Contains("changed Shingle bundle (SKU-1) from 8 to 6", note);
        Assert.Contains("removed Ridge cap (SKU-2)", note);
        Assert.DoesNotContain("Nails", note);
        Assert.Null(ProposalPricer.DescribeEdits(null));
        Assert.Null(ProposalPricer.DescribeEdits(new List<ProposalEdit> { edits[2] }));
    }
}
