# Publishing and releasing

## How the Dynamicweb App Store finds the package

The App Store queries nuget.org for packages tagged `dynamicweb-app-store`, `Addin` and `dw10`, and offers the newest version whose Dynamicweb dependency floors are satisfied by the host. The package therefore:

- is built against the floor version (`-p:DynamicwebVersion=10.27.9`), so every 10.27+ host qualifies;
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

## Installing without the App Store

- Package reference in the host csproj: `<PackageReference Include="Truvio.Commerce.BuyingAssistant" Version="<version>" />`
- Hosted install reached only by URL and key: upload `Truvio.Commerce.BuyingAssistant.dll` and `Anthropic.dll` into `Files/System/AddIns/Installed/Truvio.Commerce.BuyingAssistant.<version>/lib/net8.0/` with the Management API `Upload` command, then restart the host (`Files/System/CloudHosting/restart.txt` on Dynamicweb Cloud).
- Development: `scripts\deploy-local.ps1 -HostProject <path> -Restart`.

A host that already has `Anthropic.dll` in its own bin (for example from another add-in) must not get a second copy in the app folder: two assemblies with the same simple name break every Razor template.
