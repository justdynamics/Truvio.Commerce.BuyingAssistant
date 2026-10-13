#if BUNDLED_ANTHROPIC
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Truvio.Commerce.BuyingAssistant.Packaging;

/// <summary>
/// Local-install package only (built with -p:BundleAnthropic=true): Anthropic.dll travels inside this assembly as an
/// embedded resource instead of as a second DLL in the nupkg. The Dynamicweb local add-in installer gives every DLL in a
/// package its own Installed folder, so a second DLL would be split off into "Anthropic.&lt;version&gt;"; one DLL keeps
/// the package in exactly one folder. When the add-in's load context cannot find Anthropic (no other add-in or the
/// host provides it), it is loaded from the resource into the same context.
/// </summary>
internal static class BundledAnthropic
{
    private const string ResourceName = "Bundled.Anthropic.dll";

#pragma warning disable CA2255 // a library initializer is the point here: it must run before any Anthropic type is bound
    [ModuleInitializer]
    internal static void Register()
    {
        var own = typeof(BundledAnthropic).Assembly;
        var context = AssemblyLoadContext.GetLoadContext(own) ?? AssemblyLoadContext.Default;
        context.Resolving += (ctx, name) =>
        {
            if (!string.Equals(name.Name, "Anthropic", StringComparison.OrdinalIgnoreCase)) return null;
            using var stream = own.GetManifestResourceStream(ResourceName);
            return stream is null ? null : ctx.LoadFromStream(stream);
        };
    }
}
#endif
