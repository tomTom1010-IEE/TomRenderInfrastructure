# Preserved License Notices

The repository's existing root AGPL-3.0 license is preserved. On 28 October 2026
the maintainer authorized AGPL-3.0 for new Core, Probe and SceneEffects work,
while retaining all inherited notices. See [scope](Scope.md); this does not
erase the existing BSDF MIT notice or TomX/upstream notices.
Original imports remain byte-identical in the frozen phase-1
archive; current runtime implementations have since been refactored under their
preserved identity and license-provenance boundaries.

| Preserved notice | Source at migration |
| --- | --- |
| `TomBSDF-module.txt` | `Assets/Mods/TomShadersBSDF/LICENSE` (MIT notice) |
| `TomShadersX-root.txt` | `Assets/Mods/TomShadersX/LICENSE` (AGPL v3 text) |
| `TomShadersX-MIT-Upstream.txt` | `Assets/Mods/TomShadersX/LICENSES/MIT-Upstream.txt` |

The source manifest records their exact hashes and provenance. These notices
are retained. Probe and SceneEffects had no separate imported module-root license;
their new public licensing decision is recorded in Scope.md. Historical import
receipts are not rewritten. The source repository supplies the corresponding
runtime source for every distributed helper; no proprietary SDK is bundled.

Game, Unity, BepInEx/Harmony, KKSAPI and other SDK binaries are not redistributed.
The legacy source and build script copies under `migration/` retain the same
origin notices; they are audit references, not a second maintained implementation.
