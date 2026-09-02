# Architecture

```
storefront paragraph (Razor, Swift 2)  --POST JSON-->  /truvio/buying-assistant/ask  (IPipeline endpoint)
        ^                                                     |
        |  server-sent events: status, tool_call,             v
        |  tool_result, text, result, done            PageViewIsolation(pageId)
        |                                                     |
   buying-assistant.js                         BuyingAssistantEngine (Claude tool loop)
                                                    |                 |
                                         ICatalogGateway         McpClient (Direct)
                                         (DwCatalogGateway)      or Anthropic MCP connector
                                                    |
                                     Dynamicweb services: index query / product search,
                                     PriceManager (price providers, breaks), StockService,
                                     groups, user context, cart, orders
```

## Request flow

1. The paragraph renders the request box with data attributes (page id, paragraph id, viewed product, cart service URL, labels). Assets are served from embedded resources at `/truvio/buying-assistant/assets/`.
2. The JS posts `{ conversationId, message, pageId, paragraphId, productId, variantId, productName }` and reads the SSE stream.
3. The endpoint opens a `PageViewIsolation` for the page so language, currency, country, stock location and the signed-in user resolve exactly like a page render, reads the paragraph's item fields (mode, skill filter, placement instructions) and runs the engine on a worker thread while a channel forwards progress events to the response.
4. The engine builds the system prompt (stable part first for prompt caching: rules, business instructions, skills; then the per-request shopper context), the tool list (built-ins, allowlisted MCP tools, `submit_proposal`), and loops: create message, echo the assistant content back verbatim (text, thinking blocks with signature, tool_use), execute tools, append all tool results in one user message, until `submit_proposal` is called or the step cap is hit.
5. Every proposed line is re-priced through `PriceManager.FindPrice` with the quantity (price providers, customer agreements and quantity breaks apply), stock is checked, unknown ids are dropped. Nothing the model writes reaches the cart unpriced.
6. The conversation (append-only model history) is kept server-side per session for follow-ups.

## Built-in tools

`search_products` (index query with the configured free-text parameter, database product search as fallback), `get_product` (description, fields, categories, variants, quantity breaks, stock per location, related products, units), `get_price` (unit price and total at a quantity), `get_stock` (per stock location), `list_categories`, `products_in_category`, `customer_context` (account, groups, currency, home location, cart lines), `recent_orders` (signed-in shopper's completed orders).

## Model settings

Default `claude-opus-5`, adaptive thinking (the API default on Opus 5), effort from the settings, `tool_choice` auto with an explicit instruction to finish with `submit_proposal` (forced tool choice is not used so the same code runs on Claude Fable 5.1). Stop reasons `refusal`, `max_tokens` and the step cap turn into shopper-readable errors.

## Admin

`Settings, Apps, Buying Assistant` is an `EditScreenBase` over a `SettingsViewModelBase`; every value is a GlobalSettings key under `/Globalsettings/Truvio/BuyingAssistant/`. Read to view, Edit on the "Truvio Buying Assistant" permission to save.

## Packaging

NuGet package with tags `dynamicweb-app-store Addin dw10` (App Store discovery), the item type and Swift template under `Files/` in the package (extracted by the App Store) and embedded as resources (installed at startup for every other install path). Built against the Dynamicweb version floor (`-p:DynamicwebVersion=10.27.9`); a newer host loads it, an older host would skip the types silently.

## Source layout

```
src/Truvio.Commerce.BuyingAssistant/
  Core/Assistant/    engine, prompt builder, models, conversation store
  Core/Catalog/      ICatalogGateway + Dw/DwCatalogGateway (Dynamicweb implementation)
  Core/Mcp/          McpClient (Streamable HTTP JSON-RPC), ToolNamePolicy
  Core/Settings/     keys, settings record, DW reader
  Core/Skills/       SkillParser
  Frontend/          IPipeline endpoints, AssetInstaller, Assets (item type XML, cshtml, js, css)
  AdminUI/           settings model, query, screen, save command, node under Settings > Apps, permission
tests/               xunit tests for the pure Core parts
scripts/             deploy-local.ps1
```
