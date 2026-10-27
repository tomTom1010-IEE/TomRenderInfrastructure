# Build, Audit and Local Staging

## Toolchain and Identity

Use Windows, PowerShell 7, the .NET SDK selected by global.json, .NET Framework
4.6.2 reference assemblies and a local KKS SDK installation. Runtime remains
Unity 2019.4 Built-in / .NET Framework 4.6.2. Proprietary references are read
locally, never redistributed.

Nine original assembly names/versions and six BepInEx identities are preserved.
The tenth assembly, TomRender.Core, has no BepInEx entry. All migrated helper
modules now require it. SDK projects keep their compiler configuration; TomX
still explicitly invokes the original Framework csc.exe with:

```text
/nologo /target:library /optimize+ /define:TOM_X_BEPINEX
```

The new Core reference is appended through explicit project dependencies. This
is not a silent Roslyn substitution for TomX or a runtime/framework upgrade.
GameRoot defaults to the original local KKS installation; use -GameRoot,
KKS_GAME_ROOT or an ignored Local.props for local reference paths.

## Active Verification Workflow

```powershell
pwsh -NoProfile -File build/Build.ps1
pwsh -NoProfile -File build/Audit.ps1
pwsh -NoProfile -File build/Test-Core.ps1
pwsh -NoProfile -File build/Test-Tooling.ps1
pwsh -NoProfile -File build/Build-UnityValidation.ps1
```

Build records all ten outputs, source fingerprint, toolchain and reference hashes.
Audit uses frozen ABI/serialization/plugin/shader-property contracts, not full IL
equality as a substitute for runtime testing. Managed tests use production Core
sources; tooling tests include negative mutations on ignored copies and preserved
GPU capture/integration bodies. Original phase-1 full IL/control evidence remains
under migration/phase1 and artifacts/phase1/accepted, never overwritten.

For a future source revision, prepare an audited authoring candidate with
Prepare-Authoring.ps1 -Step 4, then use the allowlisted Codex Bridge importer.
Preserve script metadata and verify actual loaded binaries after domain reload.
Do not copy BepInEx game adapters into Assets, load duplicate Core assemblies,
or replace Assembly-CSharp component mirrors with game plugin DLLs.

Run the explicit Step-2/3/4 diagnostics and bounded native RenderProbe test,
followed by current cross-module/preset regression. Read complete/passed evidence,
not only a successful command request. Accept-Integration.ps1 verifies current
receipts, imported hashes, actual native output and current diagnostics before
creating immutable final acceptance. Accepted evidence folders are not reused.

Native tests explicitly enter a short Play-mode session and exit automatically;
other fixtures use isolated preview scenes. They never save/rewrite user scenes,
build Bundles, deploy DLLs or control the game. Intentional abort/timeout failures
remain in evidence; classify them separately from new runtime faults.

## Staging Gate

```powershell
pwsh -NoProfile -File build/Stage.ps1 -Profile current
pwsh -NoProfile -File build/Stage.ps1 -Profile with-prototypes
```

Current selects eight DLLs: Core, Probe pair, BSDF pair and three TomX helpers.
With-prototypes selects ten, adding the two local-only SceneEffects DLLs.
Experimental Feather is not promoted to stable by belonging to Current.

Stage refuses stale source, replaced receipts/outputs, missing accepted steps or
changed proof hashes. It creates a new folder only under artifacts/staging.
Its allowlisted inventory, dependencies, support documents and SHA256SUMS are
a local DLL-only staging artifact, not an installer or authorization to deploy.

| DLL | Destination under a future KKS game root |
| --- | --- |
| TomRender.Core.dll | BepInEx/plugins/TomRenderInfrastructure/ |
| TomProbe.Runtime.dll, KKS_TomProbe.dll | BepInEx/plugins/TomProbe/ |
| TomBSDF.Runtime.dll, KKS_TomBSDF.dll | BepInEx/plugins/TomBSDF/ |
| TomHairFeather.dll | BepInEx/plugins/TomHairFeather/ |
| TomSkinSDF.dll, TomTessellation.dll | BepInEx/plugins/TomShadersX/ |
| TomSceneEffects.Runtime.dll, KKS_TomSceneEffects.dll | BepInEx/plugins/TomSceneEffects/ |

Keep the original plugin/runtime pairs and only one Core copy. Existing matching
shader zipmods, LUTs/native resource assets and hairx-feather.unity3d must be
supplied separately. No new Bundle or shader payload is produced here.

## Remaining Release Work

The later [controlled KKS regression](Live-Regression.md) verifies one backed-up
installation, the frozen resource pairing and bounded save/load/render cases.
It does not certify arbitrary third-party interaction or full-scene GPU cost.
Before public release, finish the license and public-source review, broaden game
compatibility/performance checks as needed, and keep DOF prototype opt-in.
No publication, game deployment or KK adaptation is authorized by these scripts.
