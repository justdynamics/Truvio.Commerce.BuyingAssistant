# MCP connection

The built-in tools cover the catalog, prices, stock, categories, the shopper's account, cart and recent orders. An MCP connection adds backend tools on top, for example from the Dynamicweb Backend MCP app (Settings, Integration, MCP configurations) or any other MCP server that speaks Streamable HTTP.

## Modes

| Mode | Who calls the MCP server | Use when |
|---|---|---|
| `Off` | nobody | default |
| `Direct` | this Dynamicweb host (the app is the MCP client) | the server is on localhost, a private network, or the same host |
| `Connector` | Anthropic's MCP connector, from Anthropic's side | the server is publicly reachable over HTTPS |

Settings: URL (for the Dynamicweb Backend MCP: `https://<host>/admin/mcp`), bearer token, allowed tools, "Allow write tools", server name (Connector mode label).

## Allowlist and write guard

The assistant only ever sees the tools that match the **Allowed MCP tools** list (one per line or comma separated; trailing `*` matches a prefix; empty means no MCP tools at all). On top of that, tool names that look like mutations (`create_`, `update_`, `delete_`, `save_`, `patch_`, `set_`, `assign_`, `remove_`, `add_`, `build_`, `import_`, `upload_`, ...) are blocked unless **Allow write tools** is on.

In Direct mode the allowlist filters the `tools/list` result before the tools are given to the model; in Connector mode the explicit names (no wildcards) are passed as the server's allowed tools.

## What to allow

Keep it read-only and shopper-relevant. Reasonable starting points on the Dynamicweb Backend MCP:

```
get_stock_locations
get_units
get_variant_groups_by_product_id
search_documentation
```

Think before allowing tools that read other customers' data (orders, users): the assistant is prompted to stay on the shopper's own account, and the built-in `recent_orders` tool is already scoped to the signed-in user, but a backend tool with a customer id parameter is only as safe as the model's discipline.

## Skills and MCP

Tell the assistant in a skill when to use a backend tool: "For warranty questions call search_documentation first" or "Use get_stock_locations to name the branch". Tool descriptions from the server are passed to the model as-is, prefixed with "Backend tool".
