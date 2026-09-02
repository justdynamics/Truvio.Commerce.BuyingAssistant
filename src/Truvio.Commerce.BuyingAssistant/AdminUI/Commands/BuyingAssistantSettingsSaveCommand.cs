using Dynamicweb.CoreUI.Data;
using Dynamicweb.Extensibility.Settings;
using Truvio.Commerce.BuyingAssistant.AdminUI.Models;
using Truvio.Commerce.BuyingAssistant.AdminUI.Security;

namespace Truvio.Commerce.BuyingAssistant.AdminUI.Commands;

/// <summary>Persists the settings into GlobalSettings (SettingsService.Persist writes and saves each key).</summary>
public sealed class BuyingAssistantSettingsSaveCommand : CommandBase<BuyingAssistantSettingsModel>
{
    public override CommandResult Handle()
    {
        var model = GetModel();
        if (!BuyingAssistantAccess.CanEditSettings())
        {
            return new CommandResult
            {
                Model = model,
                Status = CommandResult.ResultType.NotAllowed,
                Message = "Changing Buying Assistant settings requires Edit on the 'Truvio Buying Assistant' permission.",
            };
        }
        model.Effort = (model.Effort ?? "").Trim().ToLowerInvariant();
        model.Model = (model.Model ?? "").Trim();
        model.McpUrl = (model.McpUrl ?? "").Trim();
        SettingsService.Persist(model);
        Frontend.StatusReporter.Write("settings saved");
        return new CommandResult { Model = model, Status = CommandResult.ResultType.Ok };
    }
}
