# Tom Lighting Probes 0.2.6

This is a KKS **resource-only** release. The shared runtime remains at 0.1.1. ME now obtains the probe and SH control shaders from dedicated prefabs, not from the placeable 512 Standard and SH Standard items. The original five probe/SH prefabs and existing reflection controls, GUID, slots and material names are unchanged from the tested 0.2.4 package.

1. Install [the KKS base](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/releases/tag/v0.1.1) first. Close Studio and KKS.
2. Move the old Lighting Probes zipmod outside `mods/`. Install `Tom-Lighting-Probes-KKS-0.2.6.zipmod` in `mods/`; keep exactly one `tom.lightingprobes`. Keep existing shared DLLs, ME and scene cards. No cache reset is needed.
3. Under Tom Lighting add the 512 Standard probe and SH Standard volume. Edit in ME, save and reopen. For the companion rectangle-light fix install [TomBSDF 0.2.1](https://github.com/tomTom1010-IEE/TomBSDF/releases/tag/v0.2.1). Optional planar/SSR rendering still requires its separate runtime; the two reflection items alone do not install it.
4. To roll back, close Studio and restore the original resource zipmod. Do not install GitHub's source archive in the game.

Stock ME 4.0.3 field regression: all seven catalog items plus the BSDF rectangle added, saved and reloaded; a real 512 HDR capture, reversible box projection, SH illumination and editable rectangle radiance were observed. The ME 5.0 loader follows the same borrowed-prefab contract. No ME fork is included or required.

The [fingerprint](0.2.6.json) records all stored Unity bundles. For authoring, overlay this directory's `Assets` on the frozen 0.2.2 [authoring tree](../../authoring/README.md). Export the two ME-only prefabs from `Assets/TomMEControlSources` in a self-contained `studio/tom/me/lighting_control_sources.unity3d` (explicit AssetBundleBuild). Keep the five placeable prefabs in the original probe bundle. The planar item and its ME prefab belong to `chara/tom/renderinfrastructure/planar.unity3d`; the two SSR prefabs, trace compute and screen depth shader belong to `chara/tom/renderinfrastructure/ssr.unity3d`. Source-overlay assets are not game files.
