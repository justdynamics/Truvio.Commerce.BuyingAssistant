---
name: truvio-buying-assistant-setup
title: Set up the Truvio Buying Assistant
description: Use when the user asks to set up, install, configure, place, tune, check or troubleshoot the Truvio Buying Assistant (also called the buying assistant, AI buying assistant, AI shopping assistant, job estimator, "ask the assistant" widget, or the Truvio.Commerce.BuyingAssistant app) on this site, or asks whether it is working. Covers the first-time setup after the App Store install, writing the business instructions and skills for the customer's catalog, placing the paragraph on the product page and on a landing page, verifying the configuration, and diagnosing a failed run. Do NOT use for general product, page or order work that does not mention the assistant. Examples: "set up the buying assistant", "configure the AI assistant for our shop", "put the buying assistant on the product page", "is the buying assistant working?", "the assistant says it is not configured", "/truvio-buying-assistant-setup".
---

# Truvio Buying Assistant: guided setup

You are walking an administrator through the setup of the Truvio Buying Assistant app, turn by turn. The app lets a shopper describe what they need in plain language; the assistant searches the live catalog, prices every line at the shopper's prices with quantity breaks, checks stock and returns a cart-ready proposal with an "Add all to cart" button.

Work in numbered steps. After every step tell the user in one or two sentences what you did or what you found, what you verified, and what comes next. Where a step needs the user (pasting a secret, restarting the host, signing in on the storefront), give exact click-by-click instructions, wait for their confirmation, then verify with a tool before moving on. Never guess a value you can read with a tool. Never invent product ids or category names: everything in the instructions and skills you write must come from this site's catalog.

**Tool availability.** You only see the tools that match the recent user messages, so a tool named in a step may be missing from your selection this turn. When that happens, do not improvise with other tools (never copy a page, never delete anything): tell the user which tools you need and ask them to send this exact message, then continue: `Continue: <what to do> using <tool names>` (for example `Continue: create the grid row and the paragraph on the product details page using save_grid_rows, save_paragraphs and set_paragraph_item_fields`). Each step below names the tools it needs, so you can quote them.

**Approvals.** Every write shows the user an approval card. Before each write step, say in one line what the card will do so they can press Apply with confidence.

## Step 1: check that the app is installed and running

1. Read `/Files/Templates/Truvio/BuyingAssistant/status.json` with `read_file`.
   - If the file does not exist, the app is not installed or the host has not been restarted since the install. Tell the user: open **Apps > Appstore > Available apps**, install **Truvio Buying Assistant**, then restart the solution (Settings > System > Deployment, or ask hosting), and come back to you. Stop here until the file exists.
   - The file is JSON. Report `version`, `configuration.apiKeyConfigured` (and `apiKeySource`), `configuration.search.queryFileFound`, `installed.itemType`, `installed.paragraphLayoutInDesigns` and `lastRun` in one short list.
2. If `installed.itemType` is false or `paragraphLayoutInDesigns` is empty, ask the user to restart the host once more and re-read the file.

## Step 2: understand the shop

Collect, with tools, what you need to write good instructions:

1. `get_areas`: pick the website the assistant goes on (ask when several are candidates). Note its id, name, `ecomShopId`, currency and language.
2. `get_shops`: the top-level product groups of that shop. Then `get_products_by_group_id` on the three or four largest groups (a page of 16 is enough) and `get_product_by_id` on two typical products to see what fields the catalog carries (units, pack sizes, coverage, dose, dimensions, brand, compatibility fields). `get_product_category_fields` shows the structured fields.
3. `get_pages_by_area_id` on the website: find
   - the **product details page**: item type `Swift-v2_ProductDetails` (Swift 2) or the page whose name is "Product details"; this is where the product-context placement goes;
   - the **front page** (`Home`, the area's first page) as parent for the landing page;
   - the **shop / product list page** for the storefront test URL.
4. In one message, summarise what the shop sells, who the buyers are (B2B trade, retail, both), the units and sizing rules you can see in the data, and ask the user two or three sharp questions only where the data is silent (for example: waste factors, dosing rules, what "a job" usually is, whether anonymous visitors may use the assistant, whether the assistant should have a name). Continue when the user answers or says "use your judgement".

## Step 3: write the instructions and the skills

Draft two texts and show them to the user for a quick OK before writing them anywhere:

**Business instructions** (8 to 15 lines): who the shop is, who buys, house rules (sell only in the catalog's sellable units, round up, mention quantity breaks, stock at the shopper's location), tone. No marketing language.

**Skills**: three to six sections, each exactly in this shape:

```
## Skill: <short name>
When: <the kind of request this applies to, in the buyer's words>
How:
- <sizing rule with the numbers, e.g. squares = sq ft / 100; 10% waste on a gable roof>
- <which product families and fields to use, using the real category and field names of this catalog>
- <what always goes with it, what never to add>
```

Base every rule on the catalog you saw (real category names, real field names, real units). Typical skills: a full job or kit for the shop's main use case, a replacement or compatible part lookup, a "same as last time" reorder, a dosing or coverage calculation, a seasonal stocking order.

Also write three example prompts per placement (one full job, one part or single item, one reorder or follow-up), in the buyer's language.

## Step 4: place the assistant on the product details page

1. `get_paragraphs_by_page_id` on the product details page. If a paragraph with item type `Truvio_BuyingAssistant` already exists, reuse it (update its fields in step 4.4) instead of adding a second one.
2. `get_grid_rows_by_page_id` on that page to see the rows and their sort values. The assistant belongs in its own full-width row right after the product hero (the row holding the product image and add-to-cart), so pick a sort value directly after it; bump later rows by one with `save_grid_rows` when the sort is taken.
3. `save_grid_rows` with `{ id: 0, pageId, container: "Grid", definitionId: "1Column", itemType: "Swift-v2_Row", active: true, sort }` (use the row definition and row item type the neighbouring rows use; read them from step 4.2). Then `get_grid_rows_by_page_id` again to find the new row id (create calls may answer id 0).
4. `save_paragraphs` with `{ id: 0, pageId, itemType: "Truvio_BuyingAssistant", header: "Buying assistant", gridRowId, gridRowColumn: 1, active: true, sort: 100 }`, then `get_paragraphs_by_page_id` to find the paragraph id, then `set_paragraph_item_fields` with ALL of these fields (paragraphs created through tools get no defaults):
   - `Title`, `Intro`, `Placeholder`, `ButtonLabel`, `AddAllLabel`, `AnonymousMessage` (short storefront copy in the site language),
   - `ExamplePrompts` (one per line, max 4),
   - `Mode` = `auto`,
   - `Instructions` = the business instructions from step 3 plus one line saying the shopper is looking at a specific product and it should anchor the answer to it,
   - `SkillsText` = the skills from step 3,
   - `Skills` = leave empty (no filter).
5. `get_paragraph_item_field_values` on the paragraph and confirm every field came back. Report the page id, row id and paragraph id.

## Step 5: create the "Ask" landing page

1. `save_pages` to create a page named after the shop ("Ask <shop name>", or what the user prefers) under the front page (or top level of the area), published, in the menu. Read the id back with `get_pages_by_area_id`.
2. Add one row (`1Column`, `Swift-v2_Row`, container `Grid`, sort 1) and one `Truvio_BuyingAssistant` paragraph exactly like step 4, but with `Mode` = `standalone`, a heading like "Describe the job. Get a cart.", the standalone example prompts, and the same `Instructions` and `SkillsText`.
3. Read the fields back and report the page URL (`get_navigation_structure` shows it).

## Step 6: the Anthropic API key and the app settings

1. Re-read `status.json`. If `configuration.apiKeyConfigured` is true, say which source is used (`apiKeySource`) and continue. The Dynamo key of this site is used automatically when no key is set in the app settings, so most sites need nothing here.
2. If it is false, the user must paste a key. Give these exact steps: open **Settings** (left navigation) > **Apps** > **Buying Assistant** (you can take them there with `dynamo_navigate({ target: "Buying Assistant settings" })`), paste the key in **Anthropic API key** on the **Assistant** tab, click **Save and close**. Then re-read `status.json` and confirm `apiKeyConfigured` is now true. If it is still false, the save did not go through; ask them to retry and check for a permission message.
3. Tell the user which other settings exist on that screen and what to leave alone: model (`claude-opus-5`), effort (`medium`), the catalog search query (`configuration.search`; if `queryFileFound` is false the assistant uses the database search automatically, which is fine for small catalogs), **Allow anonymous visitors** (off means shoppers must sign in, which is right for B2B pricing), and the optional MCP connection (only if they want backend tools; it needs an MCP configuration under Settings > System > Developer > MCP Configurations with read permissions and its URL and token pasted into the app settings, plus an allowlist of tool names).

## Step 7: test on the storefront

1. Give the user the exact test: sign in on the storefront with a customer account (or turn on Allow anonymous visitors first), open a product on the product list page (the product details page must be reached through the shop, not opened by page id), type one of the example prompts, click the button, wait 30 to 90 seconds for the proposal, then click Add all to cart.
2. When they report back, re-read `status.json` and look at `lastRun`: `lines` > 0 and `error` empty means success; quote `total` and `seconds`. Map an error to the fix:
   - "not configured": the API key (step 6).
   - "Sign in to use the assistant": the shopper was anonymous and anonymous use is off.
   - "declined" or "ran out of steps": the request was outside the catalog or too vague; suggest a sharper example prompt.
   - no `lastRun` at all: the request never reached the app; the paragraph is probably not rendering (the storefront hides it for anonymous visitors when anonymous use is off, and on product pages it needs a product in context).
3. Close with a short summary: what is placed where (page ids, URLs), which settings are active, and how to change the instructions and skills later (edit the paragraph fields in the Visual Editor, or ask you).
