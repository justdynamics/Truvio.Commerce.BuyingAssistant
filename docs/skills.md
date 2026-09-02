# Instructions and skills

The assistant's behaviour is shaped by three layers of text, all plain text or markdown:

1. **Operating rules** (built in): use only products returned by the tools, never invent ids or prices, round up to sellable units, state assumptions, ask at most one clarifying question, finish every turn with a proposal.
2. **Business instructions** (setting): who the shop is, who buys, house rules, units, waste factors, pricing and stock conventions, tone.
3. **Skills** (setting): named playbooks for kinds of requests. Each skill is a section:

```
## Skill: Roof takeoff
When: the shopper describes a roof (square footage, pitch, tear-off, re-roof) or asks for a full material list.
How:
- Convert roof area to squares (1 square = 100 sq ft). Pitch factors: 4/12 = 1.054, 6/12 = 1.118, 8/12 = 1.202.
- Waste: 10% gable, 15% hip or cut-up.
- Shingles are sold per bundle; use the bundles-per-square field; round up.
- A complete tear-off needs field shingles, starter, hip and ridge cap, underlayment, ice and water at eaves, drip edge, pipe boots per penetration, ridge vent, nails.

## Skill: Pool opening
When: opening, starting up or shocking a pool; a pool volume in gallons or dimensions.
How:
- Estimate gallons from length x width x average depth x 7.5 when only dimensions are given.
- Shock: 1 lb cal-hypo per 10,000 gallons, doubled for green water. Chlorine tabs: 2 x 3 in tabs per 10,000 gallons per week; propose a 4 week supply.
- Always include test strips; add algaecide and clarifier for an opening after winter.
```

Write skills the way you would brief a new counter salesperson: when it applies, what to ask for, how to size, what always goes with it, what never to add. Real product families and field names from your catalog help the assistant pick the right products.

A paragraph can limit itself to a subset of skills through its **Skills** field (comma separated names, trailing `*` matches a prefix), and add **placement instructions** of its own. A product-page placement typically leaves the filter blank; a "pool opening kit" landing page might set `Pool*`.

## Example prompts

Give shoppers two to four example chips per placement that show the range: a full job, a replacement part, a reorder. They double as a live test of your skills.

## Tips

- Keep each skill under about 40 lines; move shared rules into the business instructions.
- The assistant sees product names, numbers, descriptions and every filled category or product field (limit them with the "Catalog fields" setting). Fill the fields that sizing depends on (coverage per unit, pack size, dose per volume, dimensions).
- Check the log line per request: many tool calls with few results usually mean the search terms in your catalog differ from the words shoppers use; add synonyms to product descriptions or to the skill text.
