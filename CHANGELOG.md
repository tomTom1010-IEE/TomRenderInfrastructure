# Changelog

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
