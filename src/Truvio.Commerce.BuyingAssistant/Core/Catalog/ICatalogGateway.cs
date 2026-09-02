namespace Truvio.Commerce.BuyingAssistant.Core.Catalog;

/// <summary>What the engine knows about the shopper's context; rendered into the system prompt and tool results.</summary>
public sealed record CatalogContextInfo(
    string LanguageId,
    string CurrencyCode,
    string CountryCode,
    string? ShopId,
    string? ShopName,
    bool PricesIncludeVat,
    string? StockLocationName,
    bool IsLoggedIn,
    string? CustomerName,
    string? CompanyName,
    string? CustomerNumber);

public sealed record CatalogProductSummary(
    string ProductId,
    string VariantId,
    string Sku,
    string Name,
    string? Unit,
    double? UnitPrice,
    string? UnitPriceFormatted,
    double Stock,
    bool NeverOutOfStock,
    string? Category,
    string? ShortDescription,
    IReadOnlyDictionary<string, string>? Fields,
    bool HasVariants);

public sealed record CatalogQuantityBreak(double Quantity, double UnitPrice, string UnitPriceFormatted);

public sealed record CatalogProductDetail(
    CatalogProductSummary Summary,
    string? LongDescription,
    string? Manufacturer,
    IReadOnlyList<string> Categories,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<CatalogQuantityBreak> QuantityBreaks,
    IReadOnlyList<CatalogStockInfo> StockByLocation,
    IReadOnlyList<CatalogProductSummary> Variants,
    IReadOnlyList<CatalogProductSummary> RelatedProducts,
    IReadOnlyList<string> Units);

public sealed record CatalogPriceQuote(
    string ProductId,
    string VariantId,
    double Quantity,
    string? UnitId,
    double UnitPrice,
    string UnitPriceFormatted,
    double LineTotal,
    string LineTotalFormatted,
    string TierLabel,
    string CurrencyCode);

public sealed record CatalogStockInfo(string LocationName, long LocationId, double Quantity, string? UnitId, DateTime? ExpectedDelivery, bool IsShopperLocation);

public sealed record CatalogCategory(string GroupId, string Name, string? ParentGroupId, int ProductCount);

public sealed record CartLineInfo(string ProductId, string VariantId, string Sku, string Name, double Quantity);

public sealed record CustomerContextInfo(
    bool IsLoggedIn,
    string? Name,
    string? Company,
    string? CustomerNumber,
    string? Email,
    IReadOnlyList<string> Groups,
    string CurrencyCode,
    string? StockLocationName,
    IReadOnlyList<CartLineInfo> CartLines);

public sealed record PastOrderLine(string ProductId, string VariantId, string Sku, string Name, double Quantity, double UnitPrice);

public sealed record PastOrderInfo(string OrderId, DateTime Date, string? State, double Total, string TotalFormatted, IReadOnlyList<PastOrderLine> Lines);

/// <summary>
/// Everything the engine needs from the commerce platform. The Dynamicweb implementation lives
/// in Core/Catalog/Dw; tests use a fake. All members are synchronous because the platform APIs are.
/// </summary>
public interface ICatalogGateway
{
    CatalogContextInfo Context { get; }

    IReadOnlyList<CatalogProductSummary> Search(string query, int max);

    CatalogProductDetail? GetProduct(string productId, string? variantId);

    /// <summary>Lightweight lookup used to validate proposal lines (no related products, no per-location stock).</summary>
    CatalogProductSummary? GetSummary(string productId, string? variantId);

    CatalogPriceQuote? GetPrice(string productId, string? variantId, double quantity, string? unitId);

    IReadOnlyList<CatalogStockInfo> GetStock(string productId, string? variantId);

    IReadOnlyList<CatalogCategory> GetCategories(string? parentGroupId);

    IReadOnlyList<CatalogProductSummary> GetProductsInCategory(string groupId, int max);

    CustomerContextInfo GetCustomerContext();

    IReadOnlyList<PastOrderInfo> GetRecentOrders(int max);

    string FormatMoney(double amount);
}
