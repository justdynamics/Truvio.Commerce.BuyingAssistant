# Truvio Buying Assistant for Dynamicweb 10

An AI buying assistant for Dynamicweb 10 storefronts, shipped as a Dynamicweb App Store app.

A shopper describes what they need in plain language ("everything to open a 20,000 gallon pool", "1,800 sq ft gable roof, full tear-off, two pipe penetrations", "replacement union set for a 1 HP pump", "same as my last order but for 32 squares"). The assistant searches the live catalog, sizes the job with the rules you give it, prices every line at that customer's own prices with quantity breaks, checks stock per location and returns a cart-ready proposal: a live activity feed while it works, the reasoning per line, one "Add all to cart" button. Follow-up questions refine the same proposal. Everything the model proposes is re-priced by Dynamicweb before it reaches the cart.

- Package: `Truvio.Commerce.BuyingAssistant` on nuget.org (Dynamicweb App Store: Apps > Appstore > Available apps)
- Requires Dynamicweb 10.27 or newer, an Anthropic API key (the Dynamo key is reused when present), and a host restart after installation
- License: MIT

## Install and set up in three steps

1. **Install** from the App Store (Apps > Appstore > Available apps > Truvio Buying Assistant) and **restart** the solution. On first start the app installs its item type, its Swift 2 paragraph layout and a Dynamo skill, and writes a status file.
2. **Open Dynamo** (the backend AI assistant panel) and type:

   ```
   /truvio-buying-assistant-setup
   ```

   Dynamo checks the installation, reads your catalog and pages, drafts the business instructions and skills for your products, places the assistant on the product details page and on an "Ask" landing page, walks you through the one manual step when needed (the Anthropic API key under Settings > Apps > Buying Assistant) and verifies the first storefront run with you. Every write is shown as an approval card before it happens.
3. **Test** on the storefront: sign in as a customer, open a product, type a request, click the button, then "Add all to cart".

Without Dynamo, the same setup takes ten minutes by hand: [docs/getting-started.md](docs/getting-started.md).

## What is in the box

| Piece | Where |
|---|---|
| "Buying Assistant" paragraph (item type `Truvio_BuyingAssistant`): copy, example prompts, mode (product page or standalone), business instructions, skills | Visual Editor, category Truvio |
| Built-in tools that run inside the host: catalog search, product detail with specs and variants, price at quantity, stock per stock location, categories, the shopper's account and cart, recent orders | no configuration |
| Optional MCP connection (Direct from the host, or Anthropic's connector) with a tool allowlist and a write guard | Settings > Apps > Buying Assistant |
| Settings screen: API key, model (`claude-opus-5` by default), effort, global instructions and skills, search query, MCP, limits, anonymous access, logging | Settings > Apps > Buying Assistant |
| Dynamo setup skill and a status file Dynamo can read | `Files/Dynamo/Skills/`, `Files/Templates/Truvio/BuyingAssistant/status.json` |
| Storefront endpoint (server-sent events) and assets | `/truvio/buying-assistant/ask`, `/truvio/buying-assistant/assets/` |

## Documentation

- [Getting started](docs/getting-started.md): install paths, Dynamo setup, manual configuration, placing the paragraph, logging and diagnostics
- [Configuration reference](docs/configuration.md): every setting and paragraph field, the status file
- [Instructions and skills](docs/skills.md): how to brief the assistant for your business
- [MCP connection](docs/mcp.md): modes, allowlist, write guard
- [Architecture](docs/architecture.md): request flow, tools, model settings, packaging, source layout
- [Publishing and releasing](docs/publishing.md): for maintainers

## For partners

- The app is generic: nothing in it is specific to one shop. Everything shop specific lives in the instructions and skills (text fields) and can be authored by Dynamo from the customer's own catalog.
- Prices, stock, assortments and customer agreements come from Dynamicweb's own services (`PriceManager`, stock locations, the signed-in user's context), so what the assistant quotes is what the cart charges.
- The paragraph layout is a normal Razor file under the design folder; customise it freely, the app never overwrites an edited copy.
- Log lines per request (user, tokens, cache reads, lines, total, seconds) go to `Files/System/Log/Truvio.BuyingAssistant/`.
- Issues and feature requests: https://github.com/justdynamics/Truvio.Commerce.BuyingAssistant/issues

## Building from source

```powershell
dotnet test tests\Truvio.Commerce.BuyingAssistant.Tests
dotnet pack src\Truvio.Commerce.BuyingAssistant\Truvio.Commerce.BuyingAssistant.csproj -c Release -p:DynamicwebVersion=10.27.9 --output artifacts
```

Always build against the Dynamicweb floor version (`10.27.9`); a DLL compiled against a newer Dynamicweb version loads on older hosts but its types are skipped silently. `scripts\deploy-local.ps1 -HostProject <Dynamicweb.Host.Suite folder> -Restart` drops a development build into a local host.
