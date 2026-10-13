using Dynamicweb.Data;
using Dynamicweb.Ecommerce.Products;
using Truvio.Commerce.BuyingAssistant.Core.Assistant;
using Truvio.Commerce.BuyingAssistant.Core.Catalog.Dw;
using Truvio.Commerce.BuyingAssistant.Core.Settings;
using EcomServices = Dynamicweb.Ecommerce.Services;

namespace Truvio.Commerce.BuyingAssistant.Bom;

/// <summary>
/// The Dynamicweb side of the estimator: lists every sellable product or variant of the shop in the
/// request's language, and prices chosen lines through <see cref="DwCatalogGateway"/> and
/// <see cref="ProposalPricer"/>, the same path the assistant's proposals take.
/// </summary>
internal sealed class DwBomCatalog : IBomCatalog
{
    private const string ProductRowsSql =
        "SELECT ProductId, ProductVariantId, CAST(ISNULL(ProductActive, 0) AS int), CAST(ISNULL(ProductHidden, 0) AS int), CAST(ISNULL(ProductVariantCounter, 0) AS int) " +
        "FROM EcomProducts WHERE ProductLanguageId = {0}";

    private readonly AssistantSettings _settings;
    private readonly DwCatalogGateway _gateway;
    private readonly string _languageId;
    private readonly string? _shopId;

    public DwBomCatalog(AssistantSettings settings, BomRequest request)
    {
        _settings = settings;
        _gateway = new DwCatalogGateway(settings, request.LanguageId, request.CurrencyCode, request.CountryCode, request.ShopId, request.User);
        _languageId = _gateway.LanguageId;
        _shopId = string.IsNullOrWhiteSpace(request.ShopId) ? null : request.ShopId;
    }

    public IReadOnlyList<BomCatalogEntry> Load()
    {
        var entries = new List<BomCatalogEntry>();
        if (string.IsNullOrEmpty(_shopId)) return entries;

        // Neither Product nor ProductViewModel exposes ProductHidden or the variant counter, so the
        // flags come from one parameterised read of EcomProducts for this language.
        var plan = BomCatalogPlan.Build(ReadProductRows());

        var fieldIds = new List<string>(_settings.BomDescriptiveFields);
        var packField = _settings.BomPackField;
        if (!string.IsNullOrEmpty(packField) && !fieldIds.Contains(packField, StringComparer.OrdinalIgnoreCase)) fieldIds.Add(packField);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var master in EcomServices.Products.GetAllProductsByShopId(_shopId, false))
        {
            if (master == null || string.IsNullOrEmpty(master.Id)) continue;
            if (!string.IsNullOrEmpty(master.VariantId)) continue;
            if (!string.Equals(master.LanguageId, _languageId, StringComparison.OrdinalIgnoreCase)) continue;
            if (plan.ExcludedMasters.Contains(master.Id) || !seen.Add(master.Id)) continue;

            var sellable = new List<Product>();
            if (plan.VariantsByMaster.TryGetValue(master.Id, out var variantIds))
            {
                foreach (var vid in variantIds)
                {
                    var variant = _gateway.GetExact(master.Id, vid);
                    if (variant != null) sellable.Add(variant);
                }
            }
            else
            {
                sellable.Add(master);
            }

            foreach (var product in sellable)
            {
                var entry = Describe(product, fieldIds, packField);
                if (entry != null) entries.Add(entry);
            }
        }
        return entries;
    }

    public BomLine? Price(BomCatalogEntry entry, int quantity, string reason)
    {
        var priced = ProposalPricer.Price(_gateway, entry.ProductId, entry.VariantId, quantity, null, reason);
        if (priced == null) return null;
        return new BomLine
        {
            ProductId = priced.ProductId,
            VariantId = priced.VariantId,
            Sku = string.IsNullOrEmpty(priced.Sku) ? entry.Sku : priced.Sku,
            Name = string.IsNullOrEmpty(priced.Name) ? entry.Name : priced.Name,
            Quantity = (int)priced.Quantity,
            Unit = entry.Unit,
            UnitPrice = priced.UnitPrice,
            LineTotal = priced.LineTotal,
            TierLabel = priced.TierLabel,
            Stock = priced.Stock,
            InStock = priced.InStock,
            Reason = priced.Reason,
        };
    }

    private IEnumerable<BomCatalogPlan.Row> ReadProductRows()
    {
        var rows = new List<BomCatalogPlan.Row>();
        using var reader = Database.CreateDataReader(CommandBuilder.Create(ProductRowsSql, _languageId));
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) continue;
            var variantId = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1)) ?? "";
            rows.Add(new BomCatalogPlan.Row(
                Convert.ToString(reader.GetValue(0)) ?? "",
                variantId,
                Convert.ToInt32(reader.GetValue(2)) != 0,
                Convert.ToInt32(reader.GetValue(3)) != 0,
                Convert.ToInt32(reader.GetValue(4))));
        }
        return rows;
    }

    private BomCatalogEntry? Describe(Product product, IReadOnlyList<string> fieldIds, string packField)
    {
        var attributes = new List<string>();
        var pack = "";
        string? uom = null;
        var categories = EcomServices.ProductCategories.GetCategories(product);
        foreach (var category in categories)
        {
            if (category == null) continue;
            foreach (var fieldId in fieldIds)
            {
                var value = product.GetCategoryValue(category.Id, fieldId, false)?.ToString()?.Trim();
                if (string.IsNullOrEmpty(value)) continue;
                if (fieldId.Equals(packField, StringComparison.OrdinalIgnoreCase)) { if (pack.Length == 0) pack = value; continue; }
                attributes.Add(fieldId + "=" + value);
            }
            if (uom == null && !string.IsNullOrEmpty(_settings.BomUnitField))
            {
                var raw = product.GetCategoryValue(category.Id, _settings.BomUnitField, false)?.ToString();
                if (!string.IsNullOrWhiteSpace(raw)) uom = raw.Trim().ToUpperInvariant();
            }
        }
        var unit = uom ?? (string.IsNullOrWhiteSpace(product.DefaultUnitId) ? "EA" : product.DefaultUnitId.ToUpperInvariant());

        var key = BomEstimator.CatalogKey(product.Id, product.VariantId);
        var unitPrice = _gateway.GetPrice(product.Id, product.VariantId, 1, null)?.UnitPrice ?? 0;
        var line = BomPrompt.FormatLine(key, product.Number, product.Name, unit, pack, unitPrice, _gateway.GetQuantityBreaks(product), product.Stock, attributes);
        return new BomCatalogEntry(key, product.Id, product.VariantId ?? "", product.Number ?? "", product.Name ?? product.Id, unit, line);
    }
}
