# KKS Installation And Upgrade

Version **0.1.0**, 28 October 2026. KKS / Unity 2019.4 Built-in Forward / D3D11.
This is not a Koikatsu / Unity 5.6 package.

## Install In This Order

1. Keep the usual BepInEx 5, KKSAPI, Extended Save, Sideloader and MaterialEditor
   stack installed. These third-party dependencies are not redistributed.
2. Close KKS and Studio. Back up old Tom DLLs and affected zipmods **outside**
   `BepInEx/plugins` and `mods`; backup subfolders there can still be loaded.
3. Download `TomRenderInfrastructure-KKS-0.1.0.zip`, extract to a temporary folder
   and merge only its `BepInEx` and `mods` folders into the game root.
4. Install the separate TomX and/or TomBSDF **Infra1 shader pack** afterwards.
   Those archives contain no helper DLLs. Install the base only once when using both.
5. Verify checksums and startup logs. Keep one copy of each assembly and one
   version per zipmod GUID. Keep existing configuration; no reset is necessary.
6. Test a saved-scene copy before changing an important scene or character.

Never unpack a zipmod into `abdata`. Keep it intact in `mods`. Do not copy an
authoring/source ZIP or Unity prefabs/materials/metadata into the game.

## Base Payload

| Location under game root | Files |
| --- | --- |
| `BepInEx/plugins/TomRenderInfrastructure/` | `TomRender.Core.dll` |
| `BepInEx/plugins/TomBSDF/` | `KKS_TomBSDF.dll`, `TomBSDF.Runtime.dll` |
| `BepInEx/plugins/TomProbe/` | `KKS_TomProbe.dll`, `TomProbe.Runtime.dll` |
| `BepInEx/plugins/TomHairFeather/` | `TomHairFeather.dll`, `hairx-feather.unity3d` |
| `BepInEx/plugins/TomShadersX/` | `TomSkinSDF.dll`, `TomTessellation.dll` |
| `mods/` | `[tomTom][KKS] Tom Lighting Probes v0.2.2.zipmod` |

Replace plugin/runtime pairs together at their original locations. Remove old
duplicate DLL copies, including copies left by earlier TomX/BSDF Complete packs.
Remove superseded Probe 0.2.1 zipmods; the 0.2.2 capture path needs its matching
background resource. Do not combine new DLLs with old Probe resources. The
Feather helper must stay beside its DLL. Retire the old `TomHairHeadBand.dll` if
present; Head Band is not part of this distribution.

All six BepInEx GUIDs and previous plugin versions remain unchanged. The suite
version describes the tested cohort, not an increase to every plugin's version.
Use the release file hashes, not only Windows FileVersion, to distinguish it.

## Optional DOF Prototype

After the base, optionally merge the `BepInEx` folder from
`TomRenderInfrastructure-KKS-SceneEffects-Prototype-0.1.0.zip`. It adds
`KKS_TomSceneEffects.dll` and `TomSceneEffects.Runtime.dll` under
`BepInEx/plugins/TomSceneEffects/`; it does not install a second Core.

Defaults are **global Native / scene Inherit**. Native uses the game's original
postprocessing order. Explicit BeforeTransparents runs native Studio DOF before
transparent geometry; **all transparents stay sharp**, not only glass. It uses
the game's DOF parameters and saved scene override, not a replacement effect.
Core protocol 1 routes successful opaque-effect output into BSDF's scene copy.
The prototype is not required for ordinary shading or base installation.

Global mode is the existing BepInEx setting `[Depth Of Field] Default Mode` in
`BepInEx/config/com.tomtom1010.sceneeffects.cfg`; a compatible configuration UI
can change it immediately. Scene overrides use the preserved typed provider API
(also exposed by optional StudioMCP) and save with the scene. The prototype does
not add its own scene-settings GUI or require StudioMCP for normal rendering.

## Configuration And Fallback

Existing plugin GUID/config paths and saved scene keys are retained. No `.cfg`,
scene, character, connection token, debug command or validation settings are
installed. Keep BSDF `Validation / File Bridge=false` for ordinary use.

Shader core colors, native lighting and material math do not require the optional
extensions to exist. Missing services take the documented shader fallback; that
does not create missing scene data, refraction, soft Feather or automatic bounds.
Use the installed base for the supported integrated feature set.

- BSDF: up to four unshadowed rectangles, scene buffers/refraction, native-map
  transparent shadow reuse and capture quality routing. No extra shadow maps or SSR.
- Probe: Studio-only native reflection captures and SH volumes; wait for a ready
  capture. Intensity cannot create missing lighting data.
- Feather: experimental recognized eye-writer/HairX soft boundary; unsupported
  configurations retain hard behavior. Not a universal transparency algorithm.
- Face SDF / Tess bounds: preserve their documented fallback and mesh/bounds limits.

## Rollback

Close the game, move the new cohort outside active plugin/mod directories and
restore the complete backed-up pairs and resource versions together. Do not remove
Core while migrated helpers remain. Recheck a scene copy after rollback; do not
overwrite a user scene or delete all configuration as a repair shortcut.

The [controlled regression](docs/Live-Regression.md) covers actual native DOF,
glass, Probe/SH, synthetic Feather and persistence. It is not exhaustive GPU,
whole-character, exporter or third-party-mod certification.
