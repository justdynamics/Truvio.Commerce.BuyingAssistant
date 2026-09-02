# Getting started

## Install into a Dynamicweb 10 host

Pick one:

1. **App Store**: DW10 admin, Apps, Appstore, Available apps, search "Buying Assistant", install, then restart the host (the app registers HTTP endpoints at startup).
2. **Package reference**: in `Dynamicweb.Host.Suite.csproj`:
   ```xml
   <PackageReference Include="Truvio.Commerce.BuyingAssistant" Version="0.1.0-beta" />
   ```
3. **Manual (development or hosted upload)**: build, copy `Truvio.Commerce.BuyingAssistant.dll` and `Anthropic.dll` into the host's bin output folder (or, on a hosted install, into `Files/System/AddIns/Installed/Truvio.Commerce.BuyingAssistant.0.1.0-beta/lib/net8.0/`), restart the host. `scripts/deploy-local.ps1 -HostProject <path> -Restart` does this for a local host.

On first start the app writes two files into `Files/` and refreshes the item-type metadata:

| File | Purpose |
|---|---|
| `Files/System/Items/ItemType_Truvio_BuyingAssistant.xml` | The "Buying Assistant" paragraph item type (editor fields) |
| `Files/Templates/Designs/<every design with a Paragraph folder>/Paragraph/Truvio_BuyingAssistant/Truvio_BuyingAssistant.cshtml` | Paragraph layout (Swift 2 markup, Bootstrap classes) |

A file that already exists is left alone unless it is byte-identical to what a previous version of the app wrote, so a customised template survives upgrades. The state lives in `Files/System/Truvio/BuyingAssistant/installed.json`.

## Set up with Dynamo (recommended)

Open Dynamo (the backend AI assistant, panel on the right of the admin) and type `/truvio-buying-assistant-setup`, or "Set up the buying assistant for this site". The app ships that skill (`Files/Dynamo/Skills/truvio-buying-assistant-setup.md`). Dynamo then, turn by turn:

1. reads `Files/Templates/Truvio/BuyingAssistant/status.json` (written by the app at startup, after every settings save and after every run) and reports version, API key source, search query, installed layouts;
2. reads the catalog (shops, groups, sample products, fields) and the pages, and asks two or three questions where the data is silent;
3. drafts business instructions and three to six skills for your products, plus example prompts;
4. places the paragraph on the product details page and creates an "Ask" landing page, filling every field, and reads them back;
5. walks you through the one manual step when needed (pasting the Anthropic API key under Settings > Apps > Buying Assistant; on a site where Dynamo runs, its key is reused automatically);
6. tells you exactly how to run the first storefront test and verifies it through `lastRun` in the status file.

Manual configuration works the same way without Dynamo:

## Configure

DW10 admin, **Settings, Apps, Buying Assistant**:

1. **Assistant tab**: paste the Anthropic API key (or set the `ANTHROPIC_API_KEY` environment variable on the host; the Dynamo assistant key is used as a last fallback). Keep the model at `claude-opus-5` and effort at `medium` unless you have a reason to change them.
2. **Instructions and skills tab**: describe the business and how to size jobs. See `docs/skills.md`.
3. **Catalog and MCP tools tab**: the search query defaults to Swift's `Products/Products.query` with parameter `q`. Optionally connect the Dynamicweb Backend MCP (see `docs/mcp.md`).
4. Save. Settings are live immediately; no restart.

## Place the paragraph

In the Visual Editor add a paragraph of type **Truvio / Buying Assistant**:

- On the **product detail page** (Swift: the "Product details" wrapper page, in its own 1-column row under the product hero): mode `auto` gives the assistant the viewed product as anchor.
- On a **landing page** ("Ask us what you need"): mode `standalone`.

Paragraph fields: title, intro, placeholder, button label, example prompts (one per line, up to four chips), add-all label, anonymous message, and under Assistant: mode, skill filter, **Instructions** (business and placement instructions) and **Skills text** (`## Skill:` sections). Instructions and skills on a paragraph are added to the global ones from the settings, so a site can be configured entirely through paragraphs (this is what the Dynamo setup does).

By default only signed-in users get the assistant (prices and stock are customer specific). Turn on "Allow anonymous visitors" in the settings to open it up.

## Try it

Sign in as a customer, open a product page, type a request and click the button. The activity feed shows what the assistant is doing (searches, product reads, pricing); the proposal table shows every line at the customer's price with quantity-break labels and stock; "Add all to cart" posts every line to the shop's cart service (Swift's `CartService` page). Follow-up questions refine the same proposal; "Start over" resets the conversation.

## Where things are logged

`Files/System/Log/Truvio.BuyingAssistant/<date>.log`: one line per request (user, page, product, lines, total, tokens in/out and cache reads, tool calls, iterations, seconds, prompt), plus install messages and errors.

## Local diagnostics

From the host machine only: `GET /truvio/buying-assistant/diagnose?pageId=<pageId>&productId=<id>&variantId=<vid>&quantity=<n>&q=<search>` shows how the app resolves the shopper context, price, stock and search for the current browser session.
