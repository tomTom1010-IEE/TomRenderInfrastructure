# Changelog

## 0.1.1 - 2026-10-29

- Replace per-camera global renderer searches with shared shader-interest views,
  explicit Studio/ME notifications and budgeted background hierarchy discovery.
- Migrate BSDF Tess bounds, alpha-shadow receivers, refractive glass and area
  quality; keep live material values, capture quality restoration and external
  scene-buffer leases independent of BSDF renderer demand.
- Migrate Probe receiver assignment and TomX SDF/Tess/Feather discovery; clear
  old SH assignments once when no usable volume remains, then skip assignment.
- Preserve all existing assembly/plugin/component identities and GPU algorithms.
  The suite version changes; individual plugin versions remain unchanged.
  Existing 0.1.0 archives and audit history remain intact.
  See [implementation and validation scope](docs/Discovery-Registry.md).
- Local qualification: 170 Editor checks, real ME/Screencap regression and a
  supplied 9,818-renderer scene recovered from about 4.52 to 60.21 FPS. Unknown,
  unnotified shader changes retain budgeted discovery latency; published rectangle
  catalog loading has a separately recorded environment/resource issue.

## 0.1.0 - 2026-10-28

- First shared KKS distribution for TomX and TomBSDF runtime helpers.
- One entry-free Core owns camera invocation scopes, scheduling, discovery and
  resource/state ownership. Existing DLL names, six plugin GUIDs and Unity
  component/serialization identities remain unchanged.
- Preserves Probe/SH capture algorithms, Feather filtering and BSDF physical
  shader algorithms. The explicit SceneColor-v1 route replaces private BSDF IL
  matching for the optional native-DOF prototype.
- Base contains eight DLLs and matching existing Probe/Feather resources;
  optional SceneEffects adds two DLLs with Native/Inherit defaults.
- Controlled KKS StudioMCP regression passed 154 combined integrity, deployment,
  rendering and persistence checks. No new shader or Bundle compilation.
- New Core/Probe/SceneEffects work is AGPL-3.0; inherited notices are preserved.

Experimental Feather, optional prototype DOF and documented shader/host fallback
limits remain labeled. No KK, SSR or new shadow-map generation is introduced.
