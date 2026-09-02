namespace Truvio.Commerce.BuyingAssistant.Core.Settings;

/// <summary>
/// Every GlobalSettings key the app reads or writes, plus the shipped defaults.
/// Attributes need compile-time constants, so everything here is a const.
/// Deleting the /Globalsettings/Truvio/BuyingAssistant node resets the app.
/// </summary>
public static class AssistantSettingKeys
{
    public const string Root = "/Globalsettings/Truvio/BuyingAssistant/";

    // Assistant
    public const string ApiKey = Root + "ApiKey";
    public const string Model = Root + "Model";
    public const string Effort = Root + "Effort";
    public const string Instructions = Root + "Instructions";
    public const string Skills = Root + "Skills";
    public const string AssistantName = Root + "AssistantName";

    // Catalog tools
    public const string SearchRepository = Root + "Search/Repository";
    public const string SearchQuery = Root + "Search/Query";
    public const string SearchParameter = Root + "Search/Parameter";
    public const string SearchResultCap = Root + "Search/ResultCap";
    public const string CatalogFields = Root + "Search/CatalogFields";
    public const string RecentOrdersEnabled = Root + "Tools/RecentOrdersEnabled";

    // MCP
    public const string McpMode = Root + "Mcp/Mode";
    public const string McpUrl = Root + "Mcp/Url";
    public const string McpToken = Root + "Mcp/Token";
    public const string McpAllowedTools = Root + "Mcp/AllowedTools";
    public const string McpAllowWriteTools = Root + "Mcp/AllowWriteTools";
    public const string McpServerName = Root + "Mcp/ServerName";

    // Behaviour
    public const string MaxIterations = Root + "Limits/MaxIterations";
    public const string MaxTokens = Root + "Limits/MaxTokens";
    public const string TimeoutSeconds = Root + "Limits/TimeoutSeconds";
    public const string MaxPromptLength = Root + "Limits/MaxPromptLength";
    public const string AllowAnonymous = Root + "AllowAnonymous";
    public const string LogConversations = Root + "LogConversations";
    public const string CartServiceTag = Root + "CartServiceTag";

    public static class Defaults
    {
        public const string Model = "claude-opus-5";
        public const string Effort = "medium";
        public const string AssistantName = "Buying Assistant";
        public const string SearchRepository = "Products";
        public const string SearchQuery = "Products.query";
        public const string SearchParameter = "q";
        public const int SearchResultCap = 25;
        public const bool RecentOrdersEnabled = true;
        public const string McpMode = "Off";
        public const string McpServerName = "dynamicweb";
        public const bool McpAllowWriteTools = false;
        public const int MaxIterations = 14;
        public const int MaxTokens = 8000;
        public const int TimeoutSeconds = 300;
        public const int MaxPromptLength = 4000;
        public const bool AllowAnonymous = false;
        public const bool LogConversations = true;
        public const string CartServiceTag = "CartService";
    }

    /// <summary>Keys outside the app's own node that are consulted as fallbacks.</summary>
    public static class Fallbacks
    {
        public const string DynamoApiKey = "/Globalsettings/Dynamo/ApiKey";
        public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
    }
}
