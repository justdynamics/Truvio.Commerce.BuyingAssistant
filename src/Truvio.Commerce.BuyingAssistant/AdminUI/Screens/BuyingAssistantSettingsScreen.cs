using Dynamicweb.CoreUI.Data;
using Dynamicweb.CoreUI.Editors;
using Dynamicweb.CoreUI.Editors.Inputs;
using Dynamicweb.CoreUI.Screens;
using Truvio.Commerce.BuyingAssistant.AdminUI.Commands;
using Truvio.Commerce.BuyingAssistant.AdminUI.Models;
using Truvio.Commerce.BuyingAssistant.AdminUI.Security;

namespace Truvio.Commerce.BuyingAssistant.AdminUI.Screens;

/// <summary>Settings, Apps, Buying Assistant: the one screen that configures the app.</summary>
public sealed class BuyingAssistantSettingsScreen : EditScreenBase<BuyingAssistantSettingsModel>
{
    private const string AssistantTab = "Assistant";
    private const string KnowledgeTab = "Instructions and skills";
    private const string ToolsTab = "Catalog and MCP tools";
    private const string BehaviourTab = "Behaviour";

    protected override string GetScreenName() => "Buying Assistant";

    protected override CommandBase<BuyingAssistantSettingsModel>? GetSaveCommand() =>
        BuyingAssistantAccess.CanEditSettings() ? new BuyingAssistantSettingsSaveCommand() : null;

    protected override void BuildEditScreen()
    {
        AddComponents(AssistantTab, "Connection",
        [
            EditorFor(m => m.ApiKey),
            EditorFor(m => m.Model),
            EditorFor(m => m.Effort),
            EditorFor(m => m.AssistantName),
        ]);

        AddComponents(KnowledgeTab, "Business instructions",
        [
            EditorFor(m => m.Instructions),
        ]);

        AddComponents(KnowledgeTab, "Skills",
        [
            EditorFor(m => m.Skills),
        ]);

        AddComponents(ToolsTab, "Catalog search",
        [
            EditorFor(m => m.SearchRepository),
            EditorFor(m => m.SearchQuery),
            EditorFor(m => m.SearchParameter),
            EditorFor(m => m.SearchResultCap),
            EditorFor(m => m.CatalogFields),
            EditorFor(m => m.RecentOrdersEnabled),
        ]);

        AddComponents(ToolsTab, "MCP connection",
        [
            EditorFor(m => m.McpMode),
            EditorFor(m => m.McpUrl),
            EditorFor(m => m.McpToken),
            EditorFor(m => m.McpAllowedTools),
            EditorFor(m => m.McpAllowWriteTools),
            EditorFor(m => m.McpServerName),
        ]);

        AddComponents(BehaviourTab, "Limits",
        [
            EditorFor(m => m.MaxIterations),
            EditorFor(m => m.MaxTokens),
            EditorFor(m => m.TimeoutSeconds),
            EditorFor(m => m.MaxPromptLength),
        ]);

        AddComponents(BehaviourTab, "Access and logging",
        [
            EditorFor(m => m.AllowAnonymous),
            EditorFor(m => m.LogConversations),
        ]);
    }

    protected override EditorBase? GetEditor(string property) => property switch
    {
        nameof(BuyingAssistantSettingsModel.ApiKey) or
        nameof(BuyingAssistantSettingsModel.McpToken) => new Password(),

        nameof(BuyingAssistantSettingsModel.Instructions) => new Textarea { Rows = 14 },
        nameof(BuyingAssistantSettingsModel.Skills) => new Textarea { Rows = 18 },
        nameof(BuyingAssistantSettingsModel.McpAllowedTools) => new Textarea { Rows = 6 },
        nameof(BuyingAssistantSettingsModel.CatalogFields) => new Textarea { Rows = 3 },

        nameof(BuyingAssistantSettingsModel.SearchResultCap) => Num("products"),
        nameof(BuyingAssistantSettingsModel.MaxIterations) => Num("steps"),
        nameof(BuyingAssistantSettingsModel.MaxTokens) => Num("tokens"),
        nameof(BuyingAssistantSettingsModel.TimeoutSeconds) => Num("s"),
        nameof(BuyingAssistantSettingsModel.MaxPromptLength) => Num("chars"),

        _ => null,
    };

    private static Number Num(string append) => new() { Append = append, Step = 1 };
}
