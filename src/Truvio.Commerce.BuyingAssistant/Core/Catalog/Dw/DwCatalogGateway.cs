using System.Globalization;
using System.Text.RegularExpressions;
using Dynamicweb.Ecommerce.International;
using Dynamicweb.Ecommerce.Orders;
using Dynamicweb.Ecommerce.Prices;
using Dynamicweb.Ecommerce.Products;
using Dynamicweb.Ecommerce.Shops;
using Dynamicweb.Ecommerce.Stocks;
using Dynamicweb.Frontend;
using Dynamicweb.Indexing.Querying;
using Dynamicweb.Security.UserManagement;
using Truvio.Commerce.BuyingAssistant.Core.Settings;
using EcomContext = Dynamicweb.Ecommerce.Common.Context;
using EcomGroup = Dynamicweb.Ecommerce.Products.Group;
using EcomServices = Dynamicweb.Ecommerce.Services;

namespace Truvio.Commerce.BuyingAssistant.Core.Catalog.Dw;

/// <summary>
/// Dynamicweb implementation of the catalog gateway. Must be constructed inside a frontend
/// context (a page render or a <see cref="PageViewIsolation"/>) so the ecommerce context
/// (language, currency, country, stock location) and the signed-in user resolve the same way
/// the storefront resolves them. Every price goes through DW's own PriceManager, so price
/// providers, customer agreements and quantity breaks apply exactly as in the cart.
/// </summary>
public sealed class DwCatalogGateway : ICatalogGateway
{
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly AssistantSettings _settings;
    private readonly string _languageId;
    private readonly Currency _currency;
    private readonly Country? _country;
    private readonly Shop? _shop;
    private readonly User? _user;
    private readonly StockLocation? _stockLocation;
    private readonly PriceContext _priceContext;
    private readonly bool _withVat;
    private readonly HashSet<string>? _fieldFilter;
    private readonly Dictionary<string, Product?> _productCache = new(StringComparer.OrdinalIgnoreCase);

    public CatalogContextInfo Context { get; }

    public DwCatalogGateway(AssistantSettings settings)
    {
        _settings = settings;
        var pageView = PageView.Current();
        var area = pageView?.Area;
        _languageId = EcomContext.LanguageID;
        _currency = EcomContext.Currency ?? EcomServices.Currencies.GetDefaultCurrency();
        _country = EcomContext.Country;
        var shopId = area?.EcomShopId;
        _shop = string.IsNullOrEmpty(shopId) ? null : EcomServices.Shops.GetShop(shopId);
        _user = UserContext.Current.User;
        _stockLocation = EcomContext.StockLocation;
        _withVat = EcomContext.DisplayPricesWithVat;
        _priceContext = new PriceContext(_currency, _country ?? EcomServices.Countries.GetCountries().FirstOrDefault()!, _shop, _user, EcomContext.ReverseChargeForVatEnabled, null);
        if (!string.IsNullOrWhiteSpace(settings.CatalogFields))
            _fieldFilter = new HashSet<string>(settings.CatalogFields.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()), StringComparer.OrdinalIgnoreCase);

        Context = new CatalogContextInfo(
            _languageId,
            _currency.Code,
            _country?.Code2 ?? "",
            shopId,
            _shop?.Name,
            _withVat,
            _stockLocation?.GetName(_languageId),
            _user != null,
            _user?.Name,
            _user?.Company,
            _user?.CustomerNumber);
    }

    public string FormatMoney(double amount) => EcomServices.Currencies.Format(_currency, amount);

    // ---- Search ----------------------------------------------------------------------------------

    public IReadOnlyList<CatalogProductSummary> Search(string query, int max)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return Array.Empty<CatalogProductSummary>();
        var results = new List<CatalogProductSummary>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var product in SearchIndex(query, max))
        {
            var summary = Summarize(product, false);
            if (summary != null && seen.Add(summary.ProductId + "|" + summary.VariantId)) results.Add(summary);
            if (results.Count >= max) break;
        }
        if (results.Count < Math.Min(3, max))
        {
            foreach (var product in SearchDatabase(query, max))
            {
                var summary = Summarize(product, false);
                if (summary != null && seen.Add(summary.ProductId + "|" + summary.VariantId)) results.Add(summary);
                if (results.Count >= max) break;
            }
        }
        return results;
    }

    private IEnumerable<Product> SearchIndex(string term, int max)
    {
        IQuery? query;
        try
        {
            query = new QueryService().LoadQuery(_settings.SearchRepository, _settings.SearchQuery);
        }
        catch { query = null; }
        if (query == null) yield break;

        var settings = new QuerySettings { Skip = 0, Take = max * 2 };
        settings.Parameters[_settings.SearchParameter] = term;
        IQueryResult? result;
        try { result = new QueryService().Query(query, settings); }
        catch { yield break; }
        if (result?.QueryResult == null) yield break;

        foreach (var doc in result.QueryResult)
        {
            if (doc is not IDictionary<string, object> fields) continue;
            var id = ReadField(fields, "ID", "ProductID", "ProductId");
            if (string.IsNullOrEmpty(id)) continue;
            var variantId = ReadField(fields, "VariantID", "VariantId", "ProductVariantID", "ProductVariantId") ?? "";
            var product = LoadProduct(id, variantId);
            if (product != null) yield return product;
        }
    }

    private IEnumerable<Product> SearchDatabase(string term, int max)
    {
        ProductSearchResult result;
        try
        {
            result = EcomServices.Products.GetProductsBySearch(new ProductSearchFilter
            {
                SearchValue = term,
                SearchInAllFields = true,
                LanguageIds = new[] { _languageId },
                ActiveFilter = ProductSearchFilter.ActiveStateFilter.Active,
                PageNumber = 1,
                PageSize = max,
            });
        }
        catch { yield break; }
        foreach (var p in result.Products)
        {
            if (p == null || !p.Active) continue;
            if (!string.IsNullOrEmpty(p.LanguageId) && !p.LanguageId.Equals(_languageId, StringComparison.OrdinalIgnoreCase)) continue;
            yield return p;
        }
    }

    private static string? ReadField(IDictionary<string, object> fields, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (fields.TryGetValue(k, out var v) && v != null)
            {
                var s = v is IEnumerable<object> list ? list.FirstOrDefault()?.ToString() : v.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    // ---- Products ------------------------------------------------------------------------------

    private Product? LoadProduct(string productId, string? variantId)
    {
        var key = productId + "|" + (variantId ?? "");
        if (_productCache.TryGetValue(key, out var cached)) return cached;
        Product? product = null;
        try
        {
            product = EcomServices.Products.GetProductById(productId, variantId ?? "", _languageId, _user);
            if (product == null && !string.IsNullOrEmpty(variantId)) product = EcomServices.Products.GetProductById(productId, "", _languageId, _user);
        }
        catch { }
        _productCache[key] = product;
        return product;
    }

    public CatalogProductSummary? GetSummary(string productId, string? variantId)
    {
        var product = LoadProduct(productId, variantId);
        return product == null || !product.Active ? null : Summarize(product, true);
    }

    public CatalogProductDetail? GetProduct(string productId, string? variantId)
    {
        var product = LoadProduct(productId, variantId);
        if (product == null) return null;
        var summary = Summarize(product, true);
        if (summary == null) return null;

        var categories = new List<string>();
        try
        {
            foreach (var g in product.Groups)
            {
                if (g == null) continue;
                var name = g.Name;
                if (!string.IsNullOrWhiteSpace(name)) categories.Add(name);
            }
        }
        catch { }

        var breaks = new List<CatalogQuantityBreak>();
        try
        {
            foreach (var kv in PriceManager.GetQuantityPrices(_priceContext, product))
            {
                if (kv.Key.Quantity <= 1) continue;
                var amount = Amount(kv.Value);
                breaks.Add(new CatalogQuantityBreak(kv.Key.Quantity, amount, FormatMoney(amount)));
            }
        }
        catch { }
        breaks = breaks.OrderBy(b => b.Quantity).ToList();

        var variants = new List<CatalogProductSummary>();
        if (string.IsNullOrEmpty(product.VariantId))
        {
            try
            {
                foreach (var combo in EcomServices.VariantCombinations.GetVariantCombinations(product.Id))
                {
                    if (combo == null || string.IsNullOrEmpty(combo.VariantId)) continue;
                    var variant = LoadProduct(product.Id, combo.VariantId);
                    var vs = variant == null ? null : Summarize(variant, false);
                    if (vs != null) variants.Add(vs);
                    if (variants.Count >= 40) break;
                }
            }
            catch { }
        }

        var related = new List<CatalogProductSummary>();
        try
        {
            foreach (var rel in EcomServices.ProductRelated.GetRelations(product.Id, product.VariantId ?? "", _languageId, true, _shop?.Id, _country?.Code2))
            {
                if (rel == null || string.IsNullOrEmpty(rel.RelatedProductId)) continue;
                var rp = LoadProduct(rel.RelatedProductId, rel.RelatedProductVariantId);
                var rs = rp == null || !rp.Active ? null : Summarize(rp, false);
                if (rs != null && related.All(r => r.ProductId != rs.ProductId)) related.Add(rs);
                if (related.Count >= 12) break;
            }
        }
        catch { }

        var units = new List<string>();
        try
        {
            foreach (var u in EcomServices.UnitOfMeasure.GetUnitOfMeasures(product.Id))
            {
                if (u != null && !string.IsNullOrEmpty(u.UnitId)) units.Add(u.UnitId);
            }
        }
        catch { }
        if (units.Count == 0 && !string.IsNullOrEmpty(product.DefaultUnitId)) units.Add(product.DefaultUnitId);

        string? manufacturer = null;
        try
        {
            if (!string.IsNullOrEmpty(product.ManufacturerId)) manufacturer = EcomServices.Manufacturers.GetManufacturerById(product.ManufacturerId)?.Name;
        }
        catch { }

        return new CatalogProductDetail(
            summary,
            Clean(product.LongDescription, 1200),
            manufacturer,
            categories,
            CollectFields(product, true),
            breaks,
            GetStock(product.Id, product.VariantId),
            variants,
            related,
            units);
    }

    private CatalogProductSummary? Summarize(Product product, bool includeFields)
    {
        if (product == null || string.IsNullOrEmpty(product.Id)) return null;
        double? unitPrice = null;
        string? formatted = null;
        try
        {
            var info = PriceManager.GetPrice(_priceContext, product);
            if (info != null) { unitPrice = Amount(info); formatted = FormatMoney(unitPrice.Value); }
        }
        catch { }

        string? category = null;
        try { category = product.Groups?.FirstOrDefault()?.Name; } catch { }

        var hasVariants = false;
        try { hasVariants = string.IsNullOrEmpty(product.VariantId) && EcomServices.VariantCombinations.GetVariantCombinations(product.Id).Count > 0; } catch { }

        return new CatalogProductSummary(
            product.Id,
            product.VariantId ?? "",
            product.Number ?? "",
            product.Name ?? product.Id,
            ResolveUnit(product),
            unitPrice,
            formatted,
            product.Stock,
            product.NeverOutOfStock,
            category,
            Clean(product.ShortDescription, 240),
            includeFields ? CollectFields(product, false) : null,
            hasVariants);
    }

    private string? ResolveUnit(Product product)
    {
        if (!string.IsNullOrWhiteSpace(product.DefaultUnitId))
        {
            try
            {
                var unit = EcomServices.Units.GetUnit(product.DefaultUnitId);
                var name = unit?.GetName(_languageId);
                return string.IsNullOrWhiteSpace(name) ? product.DefaultUnitId : name;
            }
            catch { return product.DefaultUnitId; }
        }
        return null;
    }

    private Dictionary<string, string> CollectFields(Product product, bool all)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var category in EcomServices.ProductCategories.GetCategories(product))
            {
                if (category == null) continue;
                foreach (var field in EcomServices.ProductCategories.GetFieldsByCategoryId(category.Id))
                {
                    if (field == null) continue;
                    if (_fieldFilter != null && !_fieldFilter.Contains(field.Id) && !_fieldFilter.Contains(category.Id + "." + field.Id)) continue;
                    var value = product.GetCategoryValue(category.Id, field.Id, false)?.ToString();
                    value = Clean(value, all ? 300 : 120);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var label = field.GetLabel(_languageId);
                    fields[string.IsNullOrWhiteSpace(label) ? field.Id : label] = value;
                    if (!all && fields.Count >= 12) return fields;
                }
            }
        }
        catch { }
        try
        {
            foreach (var fv in product.ProductFieldValues)
            {
                if (fv?.ProductField == null || !fv.HasValue) continue;
                var systemName = fv.ProductField.SystemName;
                if (_fieldFilter != null && !_fieldFilter.Contains(systemName)) continue;
                var value = Clean(fv.Value?.ToString(), all ? 300 : 120);
                if (string.IsNullOrWhiteSpace(value)) continue;
                var label = fv.ProductField.Name;
                fields[string.IsNullOrWhiteSpace(label) ? systemName : label] = value;
                if (!all && fields.Count >= 16) break;
            }
        }
        catch { }
        return fields;
    }

    // ---- Prices ----------------------------------------------------------------------------------

    public CatalogPriceQuote? GetPrice(string productId, string? variantId, double quantity, string? unitId)
    {
        var product = LoadProduct(productId, variantId);
        if (product == null) return null;
        if (quantity <= 0) quantity = 1;
        var stockLocationId = _stockLocation?.ID ?? 0;
        var unit = string.IsNullOrWhiteSpace(unitId) ? product.DefaultUnitId : unitId;

        double unitPrice = 0;
        var tier = "";
        try
        {
            var selection = new PriceProductSelection(product, unit, stockLocationId, quantity, quantity);
            var found = PriceManager.FindPrice(_priceContext, selection, false);
            switch (found)
            {
                case PriceInfo info:
                    unitPrice = Amount(info);
                    break;
                case PriceRaw raw:
                {
                    var calculated = PriceCalculated.Create(_priceContext, raw, product);
                    calculated.Calculate();
                    unitPrice = Amount(calculated);
                    break;
                }
                default:
                    unitPrice = Amount(PriceManager.GetPrice(_priceContext, product, unit, stockLocationId));
                    break;
            }
        }
        catch
        {
            try { unitPrice = Amount(PriceManager.GetPrice(_priceContext, product)); } catch { }
        }

        try
        {
            var basePrice = Amount(PriceManager.GetPrice(_priceContext, product, unit, stockLocationId));
            if (quantity > 1 && unitPrice > 0 && unitPrice < basePrice - 0.0001)
            {
                double? bestBreak = null;
                foreach (var kv in PriceManager.GetQuantityPrices(_priceContext, product))
                    if (kv.Key.Quantity > 1 && kv.Key.Quantity <= quantity && (bestBreak == null || kv.Key.Quantity > bestBreak)) bestBreak = kv.Key.Quantity;
                if (bestBreak == null)
                {
                    // Custom price providers may not report quantity prices; read the matrix rows directly for the label.
                    foreach (var row in EcomServices.Prices.GetByProductId(product.Id) ?? Enumerable.Empty<Price>())
                    {
                        if (row == null || row.IsInformative || row.Quantity <= 1 || row.Quantity > quantity) continue;
                        if (!string.IsNullOrEmpty(row.CurrencyCode) && !row.CurrencyCode.Equals(_currency.Code, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.IsNullOrEmpty(row.VariantId) && !row.VariantId.Equals(product.VariantId ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                        if (bestBreak == null || row.Quantity > bestBreak) bestBreak = row.Quantity;
                    }
                }
                tier = bestBreak.HasValue ? ((int)bestBreak.Value).ToString(CultureInfo.InvariantCulture) + "+ tier" : "quantity price";
            }
        }
        catch { }

        var lineTotal = Math.Round(unitPrice * quantity, 2);
        return new CatalogPriceQuote(product.Id, product.VariantId ?? "", quantity, unit, unitPrice, FormatMoney(unitPrice), lineTotal, FormatMoney(lineTotal), tier, _currency.Code);
    }

    /// <summary>DW's display price (already with or without VAT per the area setting); falls back to the explicit VAT variants.</summary>
    private double Amount(PriceInfo? info)
    {
        if (info == null) return 0;
        if (info.Price > 0) return info.Price;
        return _withVat ? info.PriceWithVAT : info.PriceWithoutVAT;
    }

    // ---- Stock -----------------------------------------------------------------------------------

    public IReadOnlyList<CatalogStockInfo> GetStock(string productId, string? variantId)
    {
        var list = new List<CatalogStockInfo>();
        var product = LoadProduct(productId, variantId);
        if (product == null) return list;
        try
        {
            foreach (var unit in EcomServices.StockService.GetStockUnits(product.Id, product.VariantId))
            {
                if (unit == null) continue;
                var location = unit.StockLocationId > 0 ? EcomServices.StockService.GetStockLocation(unit.StockLocationId) : null;
                var name = location?.GetName(_languageId);
                if (string.IsNullOrWhiteSpace(name)) name = unit.StockLocationId > 0 ? "Location " + unit.StockLocationId : "Default";
                list.Add(new CatalogStockInfo(name, unit.StockLocationId, unit.StockQuantity, unit.UnitId, unit.ExpectedDeliveryDate, _stockLocation != null && _stockLocation.ID == unit.StockLocationId));
            }
        }
        catch { }
        if (list.Count == 0)
        {
            list.Add(new CatalogStockInfo(_stockLocation?.GetName(_languageId) ?? "Warehouse", _stockLocation?.ID ?? 0, product.Stock, product.DefaultUnitId, null, _stockLocation != null));
        }
        return list.OrderByDescending(l => l.IsShopperLocation).ThenBy(l => l.LocationName).ToList();
    }

    // ---- Categories --------------------------------------------------------------------------------

    public IReadOnlyList<CatalogCategory> GetCategories(string? parentGroupId)
    {
        var list = new List<CatalogCategory>();
        try
        {
            IEnumerable<EcomGroup> groups;
            if (!string.IsNullOrEmpty(parentGroupId))
            {
                var parent = EcomServices.ProductGroups.GetGroup(parentGroupId, _languageId);
                groups = parent == null ? Array.Empty<EcomGroup>() : EcomServices.ProductGroups.GetSubgroups(parent);
            }
            else
            {
                var shopId = _shop?.Id;
                var all = EcomServices.ProductGroups.GetGroups(_languageId)
                    .Where(g => g != null && (shopId == null || string.Equals(g.ShopId, shopId, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                var childIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var g in all)
                {
                    try { foreach (var child in EcomServices.ProductGroups.GetSubgroups(g)) if (child != null) childIds.Add(child.Id); } catch { }
                }
                groups = all.Where(g => !childIds.Contains(g.Id));
            }
            foreach (var g in groups)
            {
                if (g == null || string.IsNullOrEmpty(g.Id)) continue;
                int count = 0;
                try { count = EcomServices.ProductGroups.GetProductCountForBackendTree(g.Id, _languageId); } catch { }
                list.Add(new CatalogCategory(g.Id, g.Name ?? g.Id, parentGroupId, count));
                if (list.Count >= 200) break;
            }
        }
        catch { }
        return list;
    }

    public IReadOnlyList<CatalogProductSummary> GetProductsInCategory(string groupId, int max)
    {
        var list = new List<CatalogProductSummary>();
        try
        {
            // The model sometimes passes a category name instead of the id; resolve it.
            if (EcomServices.ProductGroups.GetGroup(groupId, _languageId) == null)
            {
                var byName = EcomServices.ProductGroups.GetGroups(_languageId)
                    .FirstOrDefault(g => g != null && (_shop == null || string.Equals(g.ShopId, _shop.Id, StringComparison.OrdinalIgnoreCase))
                                         && string.Equals(g.Name, groupId, StringComparison.OrdinalIgnoreCase));
                if (byName != null) groupId = byName.Id;
            }
            foreach (var p in EcomServices.Products.GetProductsByGroupId(groupId, true, _languageId, true))
            {
                if (p == null || !string.IsNullOrEmpty(p.VariantId)) continue;
                var s = Summarize(p, false);
                if (s != null) list.Add(s);
                if (list.Count >= max) break;
            }
        }
        catch { }
        return list;
    }

    // ---- Customer ----------------------------------------------------------------------------------

    public CustomerContextInfo GetCustomerContext()
    {
        var groups = new List<string>();
        if (_user != null)
        {
            try { groups.AddRange(_user.GetGroups().Where(g => g != null && !string.IsNullOrEmpty(g.Name)).Select(g => g.Name!).Take(20)); } catch { }
        }
        var cart = new List<CartLineInfo>();
        try
        {
            var order = EcomContext.Cart;
            if (order != null)
            {
                foreach (var line in order.OrderLines)
                {
                    if (line == null || !line.HasType(OrderLineType.Product) || string.IsNullOrEmpty(line.ProductId)) continue;
                    cart.Add(new CartLineInfo(line.ProductId, line.ProductVariantId ?? "", line.ProductNumber ?? "", line.ProductName ?? "", line.Quantity));
                }
            }
        }
        catch { }
        return new CustomerContextInfo(_user != null, _user?.Name, _user?.Company, _user?.CustomerNumber, _user?.Email, groups, _currency.Code, _stockLocation?.GetName(_languageId), cart);
    }

    public IReadOnlyList<PastOrderInfo> GetRecentOrders(int max)
    {
        var list = new List<PastOrderInfo>();
        if (_user == null) return list;
        try
        {
            var result = EcomServices.Orders.GetOrdersBySearch(new OrderSearchFilter
            {
                CustomerId = _user.ID,
                HideCarts = true,
                Completed = OrderSearchFilter.CompletedStates.Completed,
                PageNumber = 1,
                PageSize = max,
                OrderBy = "OrderDate",
                DoSearch = true,
            });
            foreach (var order in result.GetResultOrders().OrderByDescending(o => o.Date))
            {
                if (order == null || order.IsCart) continue;
                var lines = new List<PastOrderLine>();
                foreach (var line in order.OrderLines)
                {
                    if (line == null || !line.HasType(OrderLineType.Product) || string.IsNullOrEmpty(line.ProductId)) continue;
                    lines.Add(new PastOrderLine(line.ProductId, line.ProductVariantId ?? "", line.ProductNumber ?? "", line.ProductName ?? "", line.Quantity, Amount(line.UnitPrice)));
                    if (lines.Count >= 40) break;
                }
                var total = Amount(order.Price);
                list.Add(new PastOrderInfo(order.Id, order.Date, order.StateId, total, FormatMoney(total), lines));
                if (list.Count >= max) break;
            }
        }
        catch { }
        return list;
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private static string? Clean(string? html, int max)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = System.Net.WebUtility.HtmlDecode(HtmlTag.Replace(html, " "));
        text = Whitespace.Replace(text, " ").Trim();
        if (text.Length > max) text = text[..max] + "...";
        return text;
    }
}
