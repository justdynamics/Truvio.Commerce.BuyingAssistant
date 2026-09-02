using Dynamicweb.Security.Permissions;

namespace Truvio.Commerce.BuyingAssistant.AdminUI.Security;

/// <summary>
/// Unified-permission entity for the app: Read to see the settings, Edit to change them.
/// Open until an administrator explicitly manages it; built-in admins are always elevated.
/// </summary>
public sealed class BuyingAssistantPermissionEntity : IPermissionEntity
{
    public const string PermissionName = "Truvio Buying Assistant";
    public const string SettingsKey = "truvio-buying-assistant-settings";

    private readonly string _key;

    public BuyingAssistantPermissionEntity(string key) => _key = key;

    public string GetPermissionKey() => _key;

    public IEnumerable<IPermissionEntity> GetPermissionParents() => Enumerable.Empty<IPermissionEntity>();
}

public sealed class BuyingAssistantPermissionEntityLookup : IPermissionEntityLookup
{
    public string PermissionName => BuyingAssistantPermissionEntity.PermissionName;

    public IPermissionEntity? GetPermissionEntityByKey(string key) =>
        key == BuyingAssistantPermissionEntity.SettingsKey ? new BuyingAssistantPermissionEntity(key) : null;
}

public static class BuyingAssistantAccess
{
    public static bool CanViewSettings() => HasLevel(PermissionLevel.Read);

    public static bool CanEditSettings() => HasLevel(PermissionLevel.Edit);

    private static bool HasLevel(PermissionLevel level)
    {
        try
        {
            return new BuyingAssistantPermissionEntity(BuyingAssistantPermissionEntity.SettingsKey).GetPermission().HasPermission(level);
        }
        catch
        {
            return false;
        }
    }
}
