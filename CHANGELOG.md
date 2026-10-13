# Changelog

## 0.3.0-beta (2026-10-13)

- Bill-of-materials mode: `Truvio.Commerce.BuyingAssistant.Bom.BomEstimator.Estimate(BomRequest)` (and a positional overload) turns a job description into a cart-ready bill of materials with one Messages API call (forced tool `submit_estimate`) over the shop's live catalog, synchronously, so a page template can call it during a render. Every line is re-priced through `DwCatalogGateway` and `ProposalPricer` (customer prices, quantity breaks with a "25+ tier" label, stock). `BomEstimator.CatalogKey(productId, variantId)` builds the `id` or `id|variant` key.
- New settings tab "Bill of materials" (keys under `Bom/`): mode on/off, sizing instructions, short request message, descriptive category fields, pack field, unit-of-measure field, max tokens, effort. Defaults are generic; the model and API key (app key, `ANTHROPIC_API_KEY`, Dynamo key) are shared with the assistant.
- The catalog read uses a parameterised query for the hidden, inactive and variant flags.
- `DwCatalogGateway` gains a constructor with an explicit language, currency, country, shop and user.
- The mode is off by default (`Bom/Enabled` False), like every other optional feature; switch it on in the settings.
- Packaging: `-p:BundleAnthropic=true` produces the local-install package: `lib/<tfm>/` holds only the app DLL, with `Anthropic.dll` embedded in it and loaded from there when the host has no Anthropic, no `Files/` entries (the app writes its files at startup), and a nuspec without an Anthropic dependency, so the local installer creates exactly one `Installed` folder. `-p:DynamicwebVersion=10.29.x` builds for net10.0 (Dynamicweb 10.29 ships net10.0 only).

## 0.2.1-beta (2026-09-25)

- Default model is `claude-opus-5-5` (settings default, docs, the Dynamo setup skill and the App Store description). A Model value already saved in the settings is kept.
- Upgrade docs: `docs/publishing.md` documents removing old `Truvio.Commerce.BuyingAssistant.*` version folders under `Files/System/AddIns/Installed/` before placing a new version, which avoids the duplicate AddIn key crash at startup (#1).
- The Dynamicweb version floor has one source, the csproj `DynamicwebVersion`; the docs and the publish workflow read it from there.
- Getting-started install lines use a version-neutral placeholder instead of a pinned version.

## 0.2.0-beta (2026-09-02)

- Proposal lines are editable: change a quantity or remove a line (restore it again) before adding to the cart. Every edit is re-priced by the platform (customer prices, quantity breaks, stock at the new quantity) through the new `POST /truvio/buying-assistant/reprice` endpoint; the total and the "Add all to cart" count follow.
- Follow-up turns receive the shopper's edits, so the assistant builds on the edited list instead of its original proposal.
- Line pricing shared between the proposal and the reprice path (`ProposalPricer`).
- Paragraph layout: new labels (Remove, Restore, Removed, Updating prices, ...) and asset version bump. Customised copies of the layout keep working; the JS falls back to English labels.

## 0.1.0-beta (2026-09-02)

First public beta.

- Storefront paragraph `Truvio_BuyingAssistant` with live activity feed, priced proposal, "Add all to cart" and follow-up turns.
- Built-in tools: catalog search (index query with database fallback), product detail, price at quantity, stock per location, categories, customer context and cart, recent orders.
- Optional MCP connection (Direct or Anthropic connector) with allowlist and write guard.
- Settings screen under Settings > Apps > Buying Assistant.
- Dynamo skill `/truvio-buying-assistant-setup` for guided setup, and a status file for verification.
- Swift 2 paragraph layout installed into every design that has a `Paragraph` folder; customised copies are kept.
