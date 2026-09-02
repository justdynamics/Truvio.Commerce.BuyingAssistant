using Dynamicweb.CoreUI.Data;
using Dynamicweb.Extensibility.Settings;
using Keys = Truvio.Commerce.BuyingAssistant.Core.Settings.AssistantSettingKeys;
using Defaults = Truvio.Commerce.BuyingAssistant.Core.Settings.AssistantSettingKeys.Defaults;

namespace Truvio.Commerce.BuyingAssistant.AdminUI.Models;

/// <summary>
/// The editable face of the app settings, built the way DW builds its own settings screens:
/// SettingsViewModelBase loads every [Settings(path)] property out of GlobalSettings in its
/// constructor and SettingsService.Persist writes them back.
/// </summary>
public sealed class BuyingAssistantSettingsModel : SettingsViewModelBase
{
    // ---- Assistant -------------------------------------------------------------------------------

    [ConfigurableProperty("Anthropic API key", "Falls back to the ANTHROPIC_API_KEY environment variable, then to the Dynamo assistant key, when blank.")]
    [Settings(Keys.ApiKey)]
    public string ApiKey { get; set; } = string.Empty;

    [ConfigurableProperty("Model", "Claude model id, e.g. claude-opus-5 (default) or claude-sonnet-5.")]
    [Settings(Keys.Model, Defaults.Model)]
    public string Model { get; set; } = Defaults.Model;

    [ConfigurableProperty("Effort", "low, medium, high, xhigh or max. Medium balances quality and speed for a storefront.")]
    [Settings(Keys.Effort, Defaults.Effort)]
    public string Effort { get; set; } = Defaults.Effort;

    [ConfigurableProperty("Assistant name", "How the assistant refers to itself.")]
    [Settings(Keys.AssistantName, Defaults.AssistantName)]
    public string AssistantName { get; set; } = Defaults.AssistantName;

    [ConfigurableProperty("Business instructions", "Who the shop is, who buys, how to size jobs, units, waste factors, house rules. Plain text or markdown.")]
    [Settings(Keys.Instructions)]
    public string Instructions { get; set; } = string.Empty;

    [ConfigurableProperty("Skills", "Sections that start with a line '## Skill: Name'. Each says when it applies and how to size that kind of request. A paragraph can limit itself to named skills.")]
    [Settings(Keys.Skills)]
    public string Skills { get; set; } = string.Empty;

    // ---- Catalog tools ---------------------------------------------------------------------------

    [ConfigurableProperty("Search repository", "Repository holding the product query used by the search tool.")]
    [Settings(Keys.SearchRepository, Defaults.SearchRepository)]
    public string SearchRepository { get; set; } = Defaults.SearchRepository;

    [ConfigurableProperty("Search query", "Query file (e.g. Products.query). When missing, the assistant falls back to the database product search.")]
    [Settings(Keys.SearchQuery, Defaults.SearchQuery)]
    public string SearchQuery { get; set; } = Defaults.SearchQuery;

    [ConfigurableProperty("Search parameter", "Free-text parameter of that query (Swift uses q).")]
    [Settings(Keys.SearchParameter, Defaults.SearchParameter)]
    public string SearchParameter { get; set; } = Defaults.SearchParameter;

    [ConfigurableProperty("Search result cap", "Most products one search may return to the assistant.")]
    [Settings(Keys.SearchResultCap, Defaults.SearchResultCap)]
    public int SearchResultCap { get; set; } = Defaults.SearchResultCap;

    [ConfigurableProperty("Catalog fields", "Category or product field ids to expose to the assistant, comma separated. Blank exposes every filled field.")]
    [Settings(Keys.CatalogFields)]
    public string CatalogFields { get; set; } = string.Empty;

    [ConfigurableProperty("Recent orders tool", "Let the assistant read the signed-in shopper's own recent orders for reorders.")]
    [Settings(Keys.RecentOrdersEnabled, Defaults.RecentOrdersEnabled)]
    public bool RecentOrdersEnabled { get; set; } = Defaults.RecentOrdersEnabled;

    // ---- MCP -------------------------------------------------------------------------------------

    [ConfigurableProperty("MCP mode", "Off, Direct (this host calls the MCP server; works for localhost) or Connector (Anthropic calls it; URL must be public).")]
    [Settings(Keys.McpMode, Defaults.McpMode)]
    public string McpMode { get; set; } = Defaults.McpMode;

    [ConfigurableProperty("MCP URL", "Streamable HTTP MCP endpoint, e.g. https://shop.example.com/admin/mcp")]
    [Settings(Keys.McpUrl)]
    public string McpUrl { get; set; } = string.Empty;

    [ConfigurableProperty("MCP token", "Bearer token for the MCP server.")]
    [Settings(Keys.McpToken)]
    public string McpToken { get; set; } = string.Empty;

    [ConfigurableProperty("Allowed MCP tools", "Tool names the assistant may call, one per line or comma separated; trailing * matches a prefix. Empty = no MCP tools.")]
    [Settings(Keys.McpAllowedTools)]
    public string McpAllowedTools { get; set; } = string.Empty;

    [ConfigurableProperty("Allow write tools", "Off blocks tool names that look like mutations (create_, update_, delete_, save_, ...) even when the allowlist matches them.")]
    [Settings(Keys.McpAllowWriteTools, Defaults.McpAllowWriteTools)]
    public bool McpAllowWriteTools { get; set; } = Defaults.McpAllowWriteTools;

    [ConfigurableProperty("MCP server name", "Label used for the server in Connector mode.")]
    [Settings(Keys.McpServerName, Defaults.McpServerName)]
    public string McpServerName { get; set; } = Defaults.McpServerName;

    // ---- Behaviour -------------------------------------------------------------------------------

    [ConfigurableProperty("Max tool steps", "Model turns per request before the assistant gives up.")]
    [Settings(Keys.MaxIterations, Defaults.MaxIterations)]
    public int MaxIterations { get; set; } = Defaults.MaxIterations;

    [ConfigurableProperty("Max output tokens", "Per model turn.")]
    [Settings(Keys.MaxTokens, Defaults.MaxTokens)]
    public int MaxTokens { get; set; } = Defaults.MaxTokens;

    [ConfigurableProperty("Timeout", "Seconds a single model turn may take.")]
    [Settings(Keys.TimeoutSeconds, Defaults.TimeoutSeconds)]
    public int TimeoutSeconds { get; set; } = Defaults.TimeoutSeconds;

    [ConfigurableProperty("Max request length", "Characters of a shopper request that are sent to the model.")]
    [Settings(Keys.MaxPromptLength, Defaults.MaxPromptLength)]
    public int MaxPromptLength { get; set; } = Defaults.MaxPromptLength;

    [ConfigurableProperty("Allow anonymous visitors", "Off requires a signed-in user (prices and stock are customer specific).")]
    [Settings(Keys.AllowAnonymous, Defaults.AllowAnonymous)]
    public bool AllowAnonymous { get; set; } = Defaults.AllowAnonymous;

    [ConfigurableProperty("Log conversations", "Write one line per request (user, tokens, lines, total) to the Truvio.BuyingAssistant log.")]
    [Settings(Keys.LogConversations, Defaults.LogConversations)]
    public bool LogConversations { get; set; } = Defaults.LogConversations;
}
