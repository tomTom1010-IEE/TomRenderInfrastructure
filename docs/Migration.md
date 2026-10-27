# Frozen Migration Baseline

## Source Map

The original root alias `KKS_UNITY_ROOT` refers to the maintainer's pre-migration
KKS Unity project. Resolve it to your own compatible project when reviewing origins.

| Origin relative to KKS_UNITY_ROOT | New source |
| --- | --- |
| `Tools/TomProbeRuntime/TomProbe.Runtime` | `src/Probe/TomProbe.Runtime` |
| `Tools/TomProbeRuntime/KKS_TomProbe` | `src/Probe/KKS_TomProbe` |
| `Tools/TomBSDFRuntime/TomBSDF.Runtime` | `src/BSDF/TomBSDF.Runtime` |
| `Tools/TomBSDFRuntime/KKS_TomBSDF` | `src/BSDF/KKS_TomBSDF` |
| `Assets/Mods/TomShadersX/Runtime/TomHairFeather*.cs` | `src/TomX/TomHairFeather` |
| `Assets/Mods/TomShadersX/Runtime/TomSkinSDF*.cs` | `src/TomX/TomSkinSDF` |
| `Assets/Mods/TomShadersX/Runtime/TomTessellation*.cs` | `src/TomX/TomTessellation` |
| `Assets/TomSceneEffects/Runtime/*.cs` | `src/SceneEffects/TomSceneEffects.Runtime` |
| `Tools/TomSceneEffects/KKS_TomSceneEffects` | `src/SceneEffects/KKS_TomSceneEffects` |

`migration/source-manifest.json` records exact origin/destination paths, roles
and SHA-256 values for 35 runtime sources, six project templates, three legacy
build scripts and three license notices. The import preserves original bytes,
including line endings. Do not refresh these hashes to hide drift.

Only build structure changes: the SceneEffects runtime project now includes the
same two local source files instead of reaching back into `Assets/`. The SDK
projects share output/GameRoot settings; new TomX wrappers call the original
compiler with original source order and flags. All six original SDK project
templates and all three TomX scripts remain as audit inputs under `migration/`.

## Ownership After This Milestone

This independent repository is the source home for future rendering-helper
development. The existing Unity project trees are retained unchanged as frozen
authoring/compatibility mirrors, not a second auto-synchronized source home.
They remain usable with their original scripts and DLLs. No files are removed
from those trees, and no installed or authoring DLL is replaced in phase 1.

Before any later explicit synchronization, compare the mirrors against this
baseline and reconcile independent changes. `Audit.ps1 -LegacyRoot <path>`
performs read-only drift checks. It does not copy anything back. Native Unity
assets and metadata remain Editor-managed via Codex Bridge.

The original importer is deliberately one-shot. It refuses existing destination
files or an existing source manifest. Do not rerun it as an update/sync tool.

## No Automatic Installed-Version Upgrade

Fresh control builds use current frozen sources, **not** an older installed DLL
as the source baseline. In particular, Probe current source is 0.2.2, while
some existing deployments/releases may still contain 0.2.1-era binaries. Passing
source migration audits does not authorize deploying the new Probe version.

No assembly identity is renamed to `TomRenderInfrastructure`. There is no
automatic second BepInEx install location, component reserialization, config
reset, extended-save migration or global default change.

## Excluded Material

- Shader sources, manifests/tooltips and accepted shader Bundles stay unchanged.
- Unity `.meta`, `.mat`, `.prefab`, `.asset`, scenes and resource assets are not imported.
- Old HeadBand code and historical LinearBuildBridge tooling are not runtime modules.
- StudioMCP and general character/bone tools remain independent projects.
- Game/Unity/BepInEx/KKSAPI DLLs are local references, not source payloads.
- KK and Unity 5.6 are outside this phase.
