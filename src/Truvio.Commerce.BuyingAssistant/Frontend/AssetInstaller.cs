using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Truvio.Commerce.BuyingAssistant.Frontend;

/// <summary>
/// Puts the item type XML and the paragraph layout into the host's Files folder.
/// Runs at startup. Writes a file when it is missing; overwrites only when the file on disk
/// is byte-identical to what a previous version of this app wrote (never a customer edit).
/// Works for every install path (App Store, local package, manual DLL drop, hosted upload).
/// </summary>
public static class AssetInstaller
{
    private const string ItemTypeResource = "Assets.ItemType_Truvio_BuyingAssistant.xml";
    private const string TemplateResource = "Assets.Truvio_BuyingAssistant.cshtml";
    public const string ItemTypeSystemName = "Truvio_BuyingAssistant";
    private const string DynamoSkillResource = "Assets.truvio-buying-assistant-setup.md";
    public const string DynamoSkillFileName = "truvio-buying-assistant-setup.md";

    public static string ReadResource(string logicalName)
    {
        using var stream = typeof(AssetInstaller).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException("Embedded resource missing: " + logicalName);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static byte[] ReadResourceBytes(string logicalName)
    {
        using var stream = typeof(AssetInstaller).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException("Embedded resource missing: " + logicalName);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Installs into the given Files root. Returns a log of what happened.</summary>
    public static IReadOnlyList<string> EnsureInstalled(string filesRoot)
    {
        var log = new List<string>();
        if (string.IsNullOrWhiteSpace(filesRoot) || !Directory.Exists(filesRoot))
        {
            log.Add("Files root not found: " + filesRoot);
            return log;
        }
        var stateDir = Path.Combine(filesRoot, "System", "Truvio", "BuyingAssistant");
        var statePath = Path.Combine(stateDir, "installed.json");
        var state = LoadState(statePath);

        // Dynamicweb rewrites item-type XML files itself (metadata refresh, admin edits), so the
        // "customised" check cannot apply here: the XML is written whenever the shipped version changed.
        var itemXml = ReadResourceBytes(ItemTypeResource);
        var itemTarget = Path.Combine(filesRoot, "System", "Items", "ItemType_" + ItemTypeSystemName + ".xml");
        Place(itemTarget, itemXml, state, log, overwriteWhenShippedVersionChanged: true);

        var template = ReadResourceBytes(TemplateResource);
        var designsRoot = Path.Combine(filesRoot, "Templates", "Designs");
        if (Directory.Exists(designsRoot))
        {
            foreach (var design in Directory.EnumerateDirectories(designsRoot))
            {
                var paragraphDir = Path.Combine(design, "Paragraph");
                if (!Directory.Exists(paragraphDir)) continue;
                var target = Path.Combine(paragraphDir, ItemTypeSystemName, ItemTypeSystemName + ".cshtml");
                Place(target, template, state, log);
            }
        }

        // Dynamo (the backend AI assistant) picks up custom skills from Files/Dynamo/Skills.
        Place(Path.Combine(filesRoot, "Dynamo", "Skills", DynamoSkillFileName), ReadResourceBytes(DynamoSkillResource), state, log);

        try
        {
            Directory.CreateDirectory(stateDir);
            File.WriteAllText(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { log.Add("Could not persist install state: " + ex.Message); }
        return log;
    }

    private static void Place(string target, byte[] content, Dictionary<string, string> state, List<string> log, bool overwriteWhenShippedVersionChanged = false)
    {
        try
        {
            var hash = Hash(content);
            var key = target.Replace('\\', '/');
            var shippedKey = key + "#shipped";
            if (File.Exists(target))
            {
                var existing = Hash(File.ReadAllBytes(target));
                if (existing == hash) { state[key] = hash; state[shippedKey] = hash; return; }
                if (overwriteWhenShippedVersionChanged)
                {
                    if (state.TryGetValue(shippedKey, out var shipped) && shipped == hash) { state[key] = existing; return; }
                    File.WriteAllBytes(target, content);
                    state[key] = hash;
                    state[shippedKey] = hash;
                    log.Add("Updated " + target);
                    return;
                }
                if (state.TryGetValue(key, out var lastInstalled) && lastInstalled == existing)
                {
                    File.WriteAllBytes(target, content);
                    state[key] = hash;
                    log.Add("Updated " + target);
                }
                else
                {
                    log.Add("Kept customised " + target);
                }
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
            state[key] = hash;
            state[shippedKey] = hash;
            log.Add("Installed " + target);
        }
        catch (Exception ex)
        {
            log.Add("Failed " + target + ": " + ex.Message);
        }
    }

    private static Dictionary<string, string> LoadState(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch { }
        return new Dictionary<string, string>();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
