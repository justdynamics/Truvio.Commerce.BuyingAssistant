using Dynamicweb.CoreUI.Data;
using Dynamicweb.Security.Permissions;
using Truvio.Commerce.BuyingAssistant.AdminUI.Models;
using Truvio.Commerce.BuyingAssistant.AdminUI.Security;

namespace Truvio.Commerce.BuyingAssistant.AdminUI.Queries;

/// <summary>Constructing the model is the load; the permission level drives the Save button.</summary>
public sealed class BuyingAssistantSettingsQuery : DataQueryModelBase<BuyingAssistantSettingsModel>
{
    public override BuyingAssistantSettingsModel? GetModel() => new()
    {
        PermissionLevelCurrentUser = BuyingAssistantAccess.CanEditSettings() ? PermissionLevel.All : PermissionLevel.Read,
    };
}
