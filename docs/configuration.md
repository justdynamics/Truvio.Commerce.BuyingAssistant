# Configuration reference

All keys live under `/Globalsettings/Truvio/BuyingAssistant/` in `Files/GlobalSettings.config`. The admin screen (Settings, Apps, Buying Assistant) writes them; deleting the `<BuyingAssistant>` node resets the app to its shipped behaviour.

## Assistant

| Setting | Key | Default | Notes |
|---|---|---|---|
| Anthropic API key | `ApiKey` | (blank) | Fallbacks: `ANTHROPIC_API_KEY` environment variable, then `/Globalsettings/Dynamo/ApiKey` |
| Model | `Model` | `claude-opus-5-5` | Any current Claude model id |
| Effort | `Effort` | `medium` | low, medium, high, max (xhigh maps to high on this SDK) |
| Assistant name | `AssistantName` | `Buying Assistant` | How it refers to itself |
| Business instructions | `Instructions` | (blank) | Free text |
| Skills | `Skills` | (blank) | `## Skill: Name` sections |

## Catalog tools

| Setting | Key | Default | Notes |
|---|---|---|---|
| Search repository | `Search/Repository` | `Products` | |
| Search query | `Search/Query` | `Products.query` | Falls back to the database product search when missing |
| Search parameter | `Search/Parameter` | `q` | Free-text parameter of the query |
| Search result cap | `Search/ResultCap` | `25` | 3 to 100 |
| Catalog fields | `Search/CatalogFields` | (blank = all filled fields) | Field ids, comma separated |
| Recent orders tool | `Tools/RecentOrdersEnabled` | `True` | |

## MCP

| Setting | Key | Default |
|---|---|---|
| Mode | `Mcp/Mode` | `Off` (Direct, Connector) |
| URL | `Mcp/Url` | |
| Token | `Mcp/Token` | |
| Allowed tools | `Mcp/AllowedTools` | (blank = none) |
| Allow write tools | `Mcp/AllowWriteTools` | `False` |
| Server name | `Mcp/ServerName` | `dynamicweb` |

## Behaviour

| Setting | Key | Default |
|---|---|---|
| Max tool steps | `Limits/MaxIterations` | `14` |
| Max output tokens | `Limits/MaxTokens` | `8000` |
| Timeout (s) per model turn | `Limits/TimeoutSeconds` | `300` |
| Max request length | `Limits/MaxPromptLength` | `4000` |
| Allow anonymous visitors | `AllowAnonymous` | `False` |
| Log conversations | `LogConversations` | `True` |
| Cart service navigation tag | `CartServiceTag` | `CartService` |

## Bill of materials

Settings, Apps, Buying Assistant, tab "Bill of materials". The estimator uses the API key (with its fallbacks) and the model from the Assistant tab, the request length and timeout from Behaviour.

| Setting | Key | Default | Notes |
|---|---|---|---|
| Bill-of-materials mode | `Bom/Enabled` | `True` | Off: every estimate returns "The bill-of-materials estimator is switched off." and no model call is made |
| Sizing instructions | `Bom/Instructions` | a short generic estimator instruction | The shop's domain rules (units, coverage, waste, accessories). The catalog mechanics are always added by the app |
| Short request message | `Bom/ShortPromptMessage` | `Describe the job first (roof size, pitch, what needs replacing).` | Returned for a description under 8 characters |
| Descriptive fields | `Bom/DescriptiveFields` | `PackDescription, CoveragePerBundle_SqFt, BundlesPerSq, BundlesPerPallet, Series, Material, ImpactRating, FireRating, Warranty, Coverage, Coverage_SqFt, RValue, Thickness, Width, Length_Ft, Length` | Category field ids sent per product as `Field=value` |
| Pack field | `Bom/PackField` | `PackDescription` | Category field id shown in the pack/coverage column |
| Unit-of-measure field | `Bom/UnitField` | `PrimaryUoM` | Category field id for the selling unit; falls back to the product's default unit, then `EA` |
| Max output tokens | `Bom/MaxTokens` | `8000` | 1024 to 64000 |
| Effort | `Bom/Effort` | `medium` | low, medium, high, max |

### Calling it from a template

```csharp
@using Truvio.Commerce.BuyingAssistant.Bom
var result = BomEstimator.Estimate(new BomRequest
{
    Prompt = prompt,
    ContextProductKey = BomEstimator.CatalogKey(product.Id, product.VariantId),
    User = Pageview.User,
    LanguageId = Dynamicweb.Ecommerce.Common.Context.LanguageID,
    ShopId = Pageview.Area?.EcomShopId,
    CurrencyCode = Dynamicweb.Ecommerce.Common.Context.Currency?.Code ?? "",
    CountryCode = Dynamicweb.Ecommerce.Common.Context.Country?.Code2 ?? "",
    ExtraInstructions = null,
});
```

`Estimate` is synchronous and never throws. It lists every active, visible product of the shop in the request language (masters with variants are listed through their active, visible variants as `id|variant`), sends the catalog and the job description to the model in one Messages API call with the forced tool `submit_estimate`, and re-prices every returned line through Dynamicweb (customer prices, quantity breaks, stock) before returning it. Ids the model invents are dropped, as are duplicate lines.

`BomResult`: `Summary`, `Assumptions` (list), `Lines`, `Notes`, `Total`, `Error`, `ModelId`, `InputTokens`, `OutputTokens`. `BomLine`: `ProductId`, `VariantId`, `Sku`, `Name`, `Quantity` (int), `Unit`, `UnitPrice`, `LineTotal`, `TierLabel` (`25+ tier` when a quantity break applies), `Stock`, `InStock`, `Reason`, `IsContextProduct`.

Errors (in `Error`, nothing else set):

| Condition | Text |
|---|---|
| Mode switched off | `The bill-of-materials estimator is switched off.` |
| Description under 8 characters | the Short request message setting |
| No API key (app key, `ANTHROPIC_API_KEY`, `/Globalsettings/Dynamo/ApiKey` all empty) | `The assistant is not configured (no Anthropic API key found).` |
| No sellable product in the shop | `No sellable products found for this shop.` |
| Any exception from the model call or pricing | `The assistant could not size this job right now. ` + exception type name (for example `AnthropicUnauthorizedException`), logged to `Truvio.BuyingAssistant` |
| The model did not call the tool | `The assistant did not return an estimate. Try describing the job in more detail.` |

## Paragraph fields (item type `Truvio_BuyingAssistant`)

Title, Intro, Placeholder, ButtonLabel, ExamplePrompts (one per line), AddAllLabel, AnonymousMessage, Mode (`auto`, `product`, `standalone`), Skills (filter), Instructions (business and placement instructions, added to the global ones), SkillsText (`## Skill:` sections, added to the global skills; a paragraph skill with the same name replaces the global one).

## Status file

`Files/Templates/Truvio/BuyingAssistant/status.json` is written at startup, after every settings save and after every assistant run. It reports the version, whether an API key is configured and from which source (never the key itself), model and effort, global instructions and skills, search query and whether its file exists, MCP mode and allowlist, installed files, and `lastRun` (time, user, page, product, lines, total, error, seconds, tool calls, prompt). Dynamo reads it through its `read_file` tool to verify the setup.
