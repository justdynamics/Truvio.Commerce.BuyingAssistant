# Configuration reference

All keys live under `/Globalsettings/Truvio/BuyingAssistant/` in `Files/GlobalSettings.config`. The admin screen (Settings, Apps, Buying Assistant) writes them; deleting the `<BuyingAssistant>` node resets the app to its shipped behaviour.

## Assistant

| Setting | Key | Default | Notes |
|---|---|---|---|
| Anthropic API key | `ApiKey` | (blank) | Fallbacks: `ANTHROPIC_API_KEY` environment variable, then `/Globalsettings/Dynamo/ApiKey` |
| Model | `Model` | `claude-opus-5` | Any current Claude model id |
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

## Paragraph fields (item type `Truvio_BuyingAssistant`)

Title, Intro, Placeholder, ButtonLabel, ExamplePrompts (one per line), AddAllLabel, AnonymousMessage, Mode (`auto`, `product`, `standalone`), Skills (filter), Instructions (business and placement instructions, added to the global ones), SkillsText (`## Skill:` sections, added to the global skills; a paragraph skill with the same name replaces the global one).

## Status file

`Files/Templates/Truvio/BuyingAssistant/status.json` is written at startup, after every settings save and after every assistant run. It reports the version, whether an API key is configured and from which source (never the key itself), model and effort, global instructions and skills, search query and whether its file exists, MCP mode and allowlist, installed files, and `lastRun` (time, user, page, product, lines, total, error, seconds, tool calls, prompt). Dynamo reads it through its `read_file` tool to verify the setup.
