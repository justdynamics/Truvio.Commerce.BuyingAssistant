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

    // Bill of materials (BOM estimator mode)
    public const string BomEnabled = Root + "Bom/Enabled";
    public const string BomInstructions = Root + "Bom/Instructions";
    public const string BomDescriptiveFields = Root + "Bom/DescriptiveFields";
    public const string BomPackField = Root + "Bom/PackField";
    public const string BomUnitField = Root + "Bom/UnitField";
    public const string BomMaxTokens = Root + "Bom/MaxTokens";
    public const string BomEffort = Root + "Bom/Effort";
    public const string BomShortPromptMessage = Root + "Bom/ShortPromptMessage";

    public static class Defaults
    {
        public const string Model = "claude-opus-5-5";
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

        public const bool BomEnabled = false;
        public const string BomInstructions =
            "You are the bill-of-materials estimator for this shop. A customer describes a job in plain language; size the materials it needs.\n" +
            "- Convert the job's dimensions into the catalog's sellable units using the pack and coverage information listed for each product.\n" +
            "- Add a sensible waste allowance for the kind of job and state it as an assumption, unless the customer gives one.\n" +
            "- Include the accessories and consumables the job normally needs when the catalog supplies them, and only what the job actually needs.\n" +
            "- State every assumption you make where the description is silent.";
        public const string BomDescriptiveFields =
            "PackDescription, CoveragePerBundle_SqFt, BundlesPerSq, BundlesPerPallet, Series, Material, ImpactRating, FireRating, Warranty, " +
            "Coverage, Coverage_SqFt, RValue, Thickness, Width, Length_Ft, Length";
        public const string BomPackField = "PackDescription";
        public const string BomUnitField = "PrimaryUoM";
        public const int BomMaxTokens = 8000;
        public const string BomEffort = "medium";
        public const string BomShortPromptMessage = "Describe the job first (roof size, pitch, what needs replacing).";
    }

    /// <summary>Keys outside the app's own node that are consulted as fallbacks.</summary>
    public static class Fallbacks
    {
        public const string DynamoApiKey = "/Globalsettings/Dynamo/ApiKey";
        public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
    }
}
