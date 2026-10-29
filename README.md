# Tom Render Infrastructure

Shared source, build and local staging home for Tom's KKS rendering helpers.
The source migration preserves existing runtime behavior and identities. It is
**not a new combined plugin, a shader release, or an installer**.

**Install first:** [KKS 0.1.1 release](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/releases/tag/v0.1.1)
then install the separate [TomX](https://github.com/tomTom1010-IEE/TomshadersX/releases)
and/or [TomBSDF](https://github.com/tomTom1010-IEE/TomBSDF/releases) shader packs.
Shader releases no longer bundle these DLLs. Install this base **once**, not once
per shader project. See [installation and upgrades](INSTALL.md).

**Status:** version 0.1.1 fixes the scene-size-dependent discovery overhead in
0.1.0. The shared registry, BSDF, Probe and TomX migration passed Editor and
controlled KKS Studio regression on 29 October 2026. In the supplied large-scene
test, active-service performance recovered from about 4.52 to 60.21 FPS. These
are local measurements, not a universal frame-rate guarantee. Existing shader
packs and resource Bundles are unchanged; no Shader/Bundle rebuild is required.
See [runtime integration](docs/Runtime-Integration.md),
[live regression scope](docs/Live-Regression.md) and
[release preparation](docs/Push-Readiness.md). Broader compatibility/performance
qualification remains separate. No shader edits, Bundle rebuild or KK adaptation
occurred in this test. The SceneEffects DOF prototype is a separate opt-in download.

## Components

**Upgrade from 0.1.0:** replace the complete base DLL cohort and, if installed,
the matching optional DOF prototype pair. Keep configuration and saved scenes.
Per-plugin versions and identities stay unchanged; use the suite version and
checksums to identify this update. See [Discovery Registry](docs/Discovery-Registry.md)
for measured scope, notification/fallback latency and separately recorded issues.

| Module | Preserved assemblies | Plugin version | Scope |
| --- | --- | --- | --- |
| Probe | `TomProbe.Runtime`, `KKS_TomProbe` | 0.2.2 | Reflection / SH probe controls and capture scheduling |
| BSDF companion | `TomBSDF.Runtime`, `KKS_TomBSDF` | 0.6.0 | Area lights, quality routing, scene buffers, native-shadow reuse and bounds |
| TomX Feather | `TomHairFeather` | 0.3.0 | Experimental eye/hair feather helper; external helper Bundle required |
| TomX Face SDF | `TomSkinSDF` | 0.1.1 | Character head frame / face-SDF binding |
| TomX Tessellation | `TomTessellation` | 0.1.1 | Displacement bounds management |
| Scene Effects | `TomSceneEffects.Runtime`, `KKS_TomSceneEffects` | 0.1.0 | Separate opt-in Studio DOF compatibility prototype |

These are **current source** versions, not a claim that every installed DLL or
previous shader release has these versions. Probe source is already 0.2.2;
this migration does not downgrade it to an earlier published package.

All six BepInEx GUIDs, nine assembly names, namespaces, Unity component names,
serialized fields and plugin versions remain unchanged. A tenth, entry-free
`TomRender.Core.dll` provides shared contexts, explicit scene-color routing and
ownership services. Migrated helpers require it. Local staging is gated by the
accepted current receipts. Existing DLL names, versions, configuration and scene
keys stay intact; Core is a new shared assembly, not a seventh BepInEx plugin.
See [Architecture](docs/Architecture.md).

## Build

Windows prerequisites: PowerShell 7, .NET SDK 9.0.315 (latest patch in its feature
band is permitted), .NET Framework 4.6.2 targeting pack, the original Windows
Framework `csc.exe`, and a local KKS installation with its existing mod SDK DLLs.
The SDK is a **build tool**; runtime targets remain .NET Framework 4.6.2 / Unity
2019.4 Built-in. Proprietary references are read locally, never vendored.

From this repository root:

```powershell
pwsh -NoProfile -File build/Build.ps1 -GameRoot 'D:/Program Files/KoikatuSunshine'
pwsh -NoProfile -File build/Audit.ps1
pwsh -NoProfile -File build/Test-Core.ps1
pwsh -NoProfile -File build/Test-Tooling.ps1
# Stage.ps1 is blocked until all four integration steps are accepted.
```

Set `KKS_GAME_ROOT`, pass `-GameRoot` to build/audit commands, or use a private
`Local.props` based on `Local.props.example` for solution builds. Pass the same
GameRoot to both control and migrated builds. Release is the audited
configuration. SDK projects retain their existing compiler settings; the three
TomX projects keep their original Framework compiler and optimized flags.

Outputs and receipts live under ignored `artifacts/`. There is no install
target. See [Build and Staging](docs/Build-and-Staging.md) before using any output.

## Inventory and Boundaries

- [Release manifest](release.manifest.json): exact DLL destinations, GUIDs,
  versions, module profiles, external dependencies and fallback notes.
- [Migration](docs/Migration.md): origin paths, source freeze and identity rules.
- [Verification](docs/Verification.md): what this phase checks and what it does not.
- [Live regression](docs/Live-Regression.md): backed-up deployment, native Studio
  DOF/glass/Probe/Feather comparisons, persistence and remaining limits.
- [Push readiness](docs/Push-Readiness.md): source-only candidate boundaries,
  license review and unpublished package/rollback requirements.
- [License provenance](LICENSES/README.md): preserved upstream notices; no blanket
  relicensing of the consolidated repository.

## Downloads And Source

- **Base ZIP:** eight DLLs, matching Feather helper and Probe 0.2.2 resource zipmod.
- **SceneEffects prototype ZIP:** optional two-DLL DOF add-on; base required.
- **Authoring ZIP:** preserved Probe/Core Unity assets and metadata for developers,
  not for the game folder. Source equivalents live under `authoring/`; build the
  matching Runtime/Core DLLs first or use the matching authoring download.
- **Shader packs:** separate TomX/BSDF releases, not included here.

The new Core/Probe/SceneEffects work follows **AGPL-3.0**. The remote's existing
root license and all BSDF MIT, TomX AGPL and upstream notices are retained;
see [license scope](LICENSES/Scope.md). No game/Unity/loader SDK is redistributed.

The default `current` inventory contains eight DLLs including Core, not the DOF prototype.
It means current non-prototype inventory, **not stable certification** of every
module (Feather remains experimental). `with-prototypes` lists all ten DLLs.
Both are DLL-only local staging: existing shader zipmods, native authoring assets,
LUTs and `hairx-feather.unity3d` remain in their shader projects. Neither profile
is a complete game installer.

DOF still defaults to **Native**, with per-scene **Inherit**. The opt-in early
mode uses Studio-owned settings and affects **all** transparent geometry, not
only TomBSDF glass. StudioMCP is an independent optional test client.

Do not install a second copy of a module under a new directory: preserve the
listed destinations and keep each plugin/runtime pair together. Future updates
must audit authoring assembly identity before any explicit Unity integration.
