using System.Text.Json;
using System.Text.Json.Serialization;
using Truvio.Commerce.BuyingAssistant.Core.Assistant;
using Truvio.Commerce.BuyingAssistant.Core.Settings;

namespace Truvio.Commerce.BuyingAssistant.Frontend;

/// <summary>
/// Writes a small status file that Dynamo (the backend AI assistant) can read through its
/// read_file tool, so it can check the configuration and the last run without any HTTP access.
/// Lives under /Files/Templates because that is a folder the backend MCP may read.
/// Never contains secrets: the API key is reported as present/absent and by source only.
/// </summary>
public static class StatusReporter
{
    public const string RelativePath = "/Files/Templates/Truvio/BuyingAssistant/status.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly object Gate = new();
    private static LastRunInfo? _lastRun;

    public sealed record LastRunInfo(DateTime Time, string? User, int PageId, string? ProductId, int Lines, string? Total, string? Error, double Seconds, int ToolCalls, string Prompt);

    public static void RecordRun(AssistantResult result, AssistantRequest request, string? user, int pageId)
    {
        _lastRun = new LastRunInfo(DateTime.Now, user, pageId, request.ContextProductId, result.Lines.Count, result.TotalFormatted, result.Error ?? (result.FollowUpQuestion != null ? "follow-up question: " + result.FollowUpQuestion : null), result.ElapsedSeconds, result.ToolCalls, request.Message.Length > 200 ? request.Message[..200] + "..." : request.Message);
        Write("run");
    }

    public static void Write(string reason)
    {
        try
        {
            lock (Gate)
            {
                var settings = DwAssistantSettings.Current;
                var filesRoot = Dynamicweb.Core.SystemInformation.MapPath("/Files/");
                string apiKeySource = "none";
                if (!string.IsNullOrWhiteSpace(Dynamicweb.Configuration.SystemConfiguration.Instance.GetValue(AssistantSettingKeys.ApiKey))) apiKeySource = "app settings";
                else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AssistantSettingKeys.Fallbacks.ApiKeyEnvironmentVariable))) apiKeySource = "environment variable";
                else if (!string.IsNullOrWhiteSpace(Dynamicweb.Configuration.SystemConfiguration.Instance.GetValue(AssistantSettingKeys.Fallbacks.DynamoApiKey))) apiKeySource = "Dynamo assistant key";

                var queryPath = Path.Combine(filesRoot, "System", "Repositories", settings.SearchRepository, settings.SearchQuery);
                var designs = new List<string>();
                var designsRoot = Path.Combine(filesRoot, "Templates", "Designs");
                if (Directory.Exists(designsRoot))
                {
                    foreach (var d in Directory.EnumerateDirectories(designsRoot))
                    {
                        if (File.Exists(Path.Combine(d, "Paragraph", AssetInstaller.ItemTypeSystemName, AssetInstaller.ItemTypeSystemName + ".cshtml")))
                            designs.Add(Path.GetFileName(d));
                    }
                }

                var status = new
                {
                    app = "Truvio Buying Assistant",
                    version = typeof(StatusReporter).Assembly.GetName().Version?.ToString(3),
                    writtenAt = DateTime.Now,
                    reason,
                    endpoint = BuyingAssistantPipeline.BasePath + "/ask",
                    settingsScreen = "Settings > Apps > Buying Assistant",
                    configuration = new
                    {
                        apiKeyConfigured = apiKeySource != "none",
                        apiKeySource,
                        model = settings.Model,
                        effort = settings.Effort,
                        assistantName = settings.AssistantName,
                        globalInstructionsChars = settings.Instructions.Length,
                        globalSkills = Core.Skills.SkillParser.Parse(settings.Skills).Select(s => s.Name).ToList(),
                        allowAnonymous = settings.AllowAnonymous,
                        recentOrdersEnabled = settings.RecentOrdersEnabled,
                        search = new { repository = settings.SearchRepository, query = settings.SearchQuery, parameter = settings.SearchParameter, queryFileFound = File.Exists(queryPath), fallback = "database product search when the query file is missing" },
                        mcp = new { mode = settings.McpMode.ToString(), urlConfigured = !string.IsNullOrWhiteSpace(settings.McpUrl), tokenConfigured = !string.IsNullOrWhiteSpace(settings.McpToken), allowedTools = Core.Mcp.ToolNamePolicy.ParsePatterns(settings.McpAllowedTools), allowWriteTools = settings.McpAllowWriteTools },
                    },
                    installed = new
                    {
                        itemType = File.Exists(Path.Combine(filesRoot, "System", "Items", "ItemType_" + AssetInstaller.ItemTypeSystemName + ".xml")),
                        paragraphLayoutInDesigns = designs,
                        dynamoSkill = File.Exists(Path.Combine(filesRoot, "Dynamo", "Skills", AssetInstaller.DynamoSkillFileName)),
                    },
                    lastRun = _lastRun,
                    howToVerify = new[]
                    {
                        "apiKeyConfigured must be true; otherwise paste an Anthropic API key under Settings > Apps > Buying Assistant and save.",
                        "Place a paragraph of item type Truvio_BuyingAssistant on the product details page (mode auto) and on a landing page (mode standalone); set Instructions and SkillsText on it.",
                        "Sign in on the storefront (or set allowAnonymous) and run an example prompt; lastRun then shows lines, total and any error.",
                    },
                };
                var target = Dynamicweb.Core.SystemInformation.MapPath(RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, JsonSerializer.Serialize(status, Json));
            }
        }
        catch (Exception ex)
        {
            try { Dynamicweb.Logging.LogManager.Current.GetLogger("Truvio.BuyingAssistant").Warn("Status file not written: " + ex.Message); } catch { }
        }
    }
}
