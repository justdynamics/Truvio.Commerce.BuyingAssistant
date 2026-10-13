# Publishing and releasing

## How the Dynamicweb App Store finds the package

The App Store queries nuget.org for packages tagged `dynamicweb-app-store`, `Addin` and `dw10`, and offers the newest version whose Dynamicweb dependency floors are satisfied by the host. The package therefore:

- is built against the csproj DynamicwebVersion floor, so every 10.27+ host qualifies;
- carries the item type, the Swift 2 layout and the Dynamo skill under `Files/` in the nupkg (extracted into the host's `Files/` on install) and as embedded resources (installed at startup for every other install path);
- lists `Anthropic` as a dependency, which the App Store resolves into the app folder.

Prerelease versions (`0.1.0-beta`) show a BETA flag in the admin.

## Release steps

1. Bump `<Version>` and `<AssemblyVersion>` in `src/Truvio.Commerce.BuyingAssistant/Truvio.Commerce.BuyingAssistant.csproj`, add a section to `CHANGELOG.md`.
2. Test and pack:
   ```powershell
   dotnet test tests\Truvio.Commerce.BuyingAssistant.Tests -c Release -p:DynamicwebVersion=10.27.9
   dotnet pack src\Truvio.Commerce.BuyingAssistant\Truvio.Commerce.BuyingAssistant.csproj -c Release -p:DynamicwebVersion=10.27.9 --output artifacts
   ```
3. Push (one of):
   - manually with a nuget.org API key (Profile > API Keys > Create, scope Push, glob `Truvio.*`):
     ```powershell
     dotnet nuget push artifacts\Truvio.Commerce.BuyingAssistant.<version>.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
     ```
   - or through GitHub Actions trusted publishing: `.github/workflows/publish.yml` runs on a `v*` tag, packs at the floor version and pushes with a short-lived key obtained via OIDC. One-time setup on nuget.org: Profile > Trusted Publishing > Create (owner `justdynamics`, repository `Truvio.Commerce.BuyingAssistant`, workflow `publish.yml`, environment `production`), plus a repository secret `NUGET_USER` holding the nuget.org profile name that created the policy.
4. nuget.org indexes within minutes; the App Store then lists the new version.

## Local-install package (Anthropic bundled)

The Dynamicweb local add-in installer (`Files/System/AddIns/Local`) does not resolve NuGet dependencies the way the App Store does, and when it does try, Anthropic's transitive `Microsoft.*` and `System.*` packages (Microsoft.Extensions.AI.Abstractions 10.2, System.Text.Json 10.0, ...) must never land next to a stock host's own copies. The `BundleAnthropic` switch builds a package for that path:

```powershell
dotnet pack src\Truvio.Commerce.BuyingAssistant\Truvio.Commerce.BuyingAssistant.csproj -c Release -p:BundleAnthropic=true --output <folder>
```

The local installer gives every DLL in a package its own folder under `Files/System/AddIns/Installed` (`<dll name>.<package version>`) and extracts a root `Files/` folder to `Files/System/AddIns/Installed/Files` instead of the host's `Files/` (Dynamicweb 10.29.6, `LocalAddinProvider.InstallNuGetPackage`). So the local package is shaped to land in exactly one Installed folder:

- `lib/net8.0/` holds exactly `Truvio.Commerce.BuyingAssistant.dll`, never a second DLL and never a `Microsoft.*` or `System.*` assembly. `Anthropic.dll` is an embedded resource of the app assembly (`Bundled.Anthropic.dll`); `Packaging/BundledAnthropic.cs` loads it into the add-in's load context when that context cannot find Anthropic elsewhere.
- No `Files/` entries: the app writes the item type, the Swift layout and the Dynamo skill from its embedded resources at startup (`AssetInstaller`).
- The nuspec declares no dependency on Anthropic (the reference is `PrivateAssets="all"`); only the Dynamicweb floor dependencies remain.
- `-p:DynamicwebVersion=10.29.6` builds the same package for `net10.0` (Dynamicweb 10.29 ships net10.0 only); the embedded assembly is then Anthropic's net9.0 one. Prefer the floor build unless a host needs the newer API surface.
- The app only calls the Messages API, so it never loads Anthropic's `Microsoft.Extensions.AI` adapter types; a stock host's Microsoft.Extensions.AI.Abstractions 9.8 is never asked for 10.2.

Verify before handing it out:

```powershell
Expand-Archive <folder>\Truvio.Commerce.BuyingAssistant.<version>.nupkg <folder>\x
Get-ChildItem <folder>\x -Recurse -File | Where-Object Extension -eq '.dll' | Select-Object -ExpandProperty Name   # Truvio.Commerce.BuyingAssistant.dll only
Test-Path <folder>\x\Files   # False
Select-String -Path <folder>\x\Truvio.Commerce.BuyingAssistant.nuspec -Pattern 'Anthropic'   # no <dependency id="Anthropic">
```

After the install and a restart, `Files/System/AddIns/Installed` has one `Truvio.Commerce.BuyingAssistant.<version>` folder and no `Anthropic.*` or `Files` folder.

The nuget.org package is built without the switch and keeps its Anthropic dependency.

## Installing without the App Store

- Package reference in the host csproj: `<PackageReference Include="Truvio.Commerce.BuyingAssistant" Version="<version>" />`
- Hosted install reached only by URL and key: upload `Truvio.Commerce.BuyingAssistant.dll` and `Anthropic.dll` into `Files/System/AddIns/Installed/Truvio.Commerce.BuyingAssistant.<version>/lib/net8.0/` with the Management API `Upload` command, then restart the host (`Files/System/CloudHosting/restart.txt` on Dynamicweb Cloud).
- Development: `scripts\deploy-local.ps1 -HostProject <path> -Restart`.

A host that already has `Anthropic.dll` in its own bin (for example from another add-in) must not get a second copy in the app folder: two assemblies with the same simple name break every Razor template.

## Upgrading

`Files/System/AddIns/Installed/` is version-suffixed: each release gets its own `Truvio.Commerce.BuyingAssistant.<version>/` folder, and the platform's `AddInManager.LoadTypeAliases()` scans every folder under `AddIns/Installed` at startup and registers every discovered add-in type into one flat dictionary keyed by type name. It does not deduplicate by version, so leaving an old version folder in place while a new one is added makes the host fail to start on every subsequent request, with `System.ArgumentException: An item with the same key has already been added` from `AddInManager.LoadTypeAliases()`.

Before placing a new version:

1. Remove every `Truvio.Commerce.BuyingAssistant.*` folder under `Files/System/AddIns/Installed/`, and the `Anthropic.<version>` and `Files` folders an earlier local package (0.3.0-beta builds before the one-folder layout) left there.
2. Place the new version folder (for a manual or hosted install, with `Anthropic.dll` alongside the app DLL as described above; the local package needs nothing else).
3. Restart the host once.

This applies to every install path above except the App Store, which replaces the version folder itself.
