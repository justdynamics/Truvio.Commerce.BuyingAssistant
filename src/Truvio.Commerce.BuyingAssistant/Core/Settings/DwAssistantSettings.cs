using Dynamicweb.Configuration;

namespace Truvio.Commerce.BuyingAssistant.Core.Settings;

/// <summary>
/// Reads the app settings straight out of GlobalSettings on every call (a dictionary lookup;
/// no extra cache layer, so a save in the admin is live immediately). Defaults are applied in
/// memory rather than written back, which keeps the read path side-effect free.
/// </summary>
public static class DwAssistantSettings
{
    public static AssistantSettings Current => AssistantSettings.FromReader(
        key => SystemConfiguration.Instance.GetValue(key),
        Environment.GetEnvironmentVariable);
}
