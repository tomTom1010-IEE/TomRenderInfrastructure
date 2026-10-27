# TomRenderInfrastructure 0.1.0 - KKS

Install this shared base **before** the new TomX and/or TomBSDF Infra1 shader packs.
The shader archives no longer contain runtime helper DLLs. Install the base once
when using both. KKS / Unity 2019.4 Built-in / Windows D3D11 only, not KK.

## Downloads

- **Base:** `TomRenderInfrastructure-KKS-0.1.0.zip` - eight DLLs, matching Feather
  helper and Tom Lighting Probes 0.2.2 resources.
- **Optional:** `TomRenderInfrastructure-KKS-SceneEffects-Prototype-0.1.0.zip` -
  two-DLL native Studio DOF compatibility prototype. Base required; Native / scene
  Inherit defaults. Early mode affects **all transparent geometry**, not glass alone.
- **Developers only:** `TomRenderInfrastructure-KKS-Authoring-0.1.0.zip` - Probe/Core
  Unity authoring files and metadata, not a game install.
- `SHA256SUMS.txt` verifies the three archives; each archive also has payload hashes.

Close KKS/Studio, back up files outside active mod/plugin folders, merge only
`BepInEx` and `mods` from the base into the game root, then install a shader pack.
Replace old plugin/runtime pairs together and remove duplicates. Replace Probe
0.2.1 resource/DLL sets with matching 0.2.2; keep Feather's helper beside its DLL.
Keep configuration and scene keys. Never unpack zipmods over `abdata`.

## Integration And Acceptance

The preserved nine assemblies/six BepInEx GUIDs gain one entry-free Core for
camera scopes, render scheduling, discovery and resource/state ownership.
No shader algorithm, sample count, Bundle or native component identity changed.
An explicit SceneColor-v1 route replaces private BSDF IL matching for early DOF.

Controlled StudioMCP regression passed **154 combined integrity/deployment/render/
persistence checks**, including actual native DOF, glass refraction, Probe/SH,
synthetic HairX Feather, resize and scene reload. No Tom-provider error-level
messages or DOF failures were observed; existing unrelated environment errors
remain documented. This is not whole-character/GPU/universal-mod certification.
Feather stays experimental and DOF stays an optional prototype.

AGPL-3.0 for new Core/Probe/SceneEffects work, original root license and all
BSDF MIT, TomX AGPL/upstream notices retained. No game/Unity/loader SDK included.

[Install/upgrade guide](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/blob/main/INSTALL.md)
| [License scope](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/blob/main/LICENSES/Scope.md)
| [Regression scope](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/blob/main/docs/Live-Regression.md)
