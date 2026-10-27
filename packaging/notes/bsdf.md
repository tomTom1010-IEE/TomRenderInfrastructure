# TomBSDF 0.2.0 RC1 - Infra1 Distribution

**Install [TomRenderInfrastructure KKS 0.1.0](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/releases/tag/v0.1.0)
first**, then this shader pack. Runtime DLLs and Probe 0.2.2 resources are now
supplied by the shared base, **not** embedded in the shader archive. Use one base
for both BSDF and TomX. BSDF remains an **RC pre-release**, not a non-RC promotion.

- `TomBSDF-KKS-0.2.0-RC1-Infra1.zip`: unchanged main zipmod, updated installation/
  offline manual and inherited license notices; no helper DLLs.
- `TomBSDF-KKS-0.2.0-RC1.zipmod`: byte-identical standalone accepted shader archive.
- `SHA256SUMS.txt`: verifies both assets; ZIP contains per-file payload hashes.

Close KKS/Studio, back up old files outside active directories and replace duplicate
standalone providers with the matching base cohort. Install the base first, then
merge only the shader pack's `mods` directory into the game root. Keep one shader
GUID registration. Do not extract zipmods into `abdata` or install authoring assets
as game files. Keep configuration; BSDF Validation/File Bridge should remain false.

This is a dependency/distribution update: the accepted Bundle and **1642 modern /
1790 total valid native programs** are unchanged. No shader/runtime recompilation,
feature reduction, variant stripping or new cold-build claim. Seven modern entries,
three legacy identities, LUTs and all physical-material behavior remain intact.
The base supplies the tested integrated DLLs, which differ from the old pre-Core
Complete releases despite retaining plugin feature-version labels; use release hashes.

The base's controlled StudioMCP regression covers actual native DOF, glass screen
refraction, Probe/SH, synthetic Feather and scene persistence. It is not GPU timing
or all-mod certification. SceneEffects is an optional separate prototype with
Native/Inherit defaults. No SSR, complete GGX BTDF, caustics or KK adaptation added.
Root AGPL, inherited module MIT and LTC notices remain preserved.

[Installation](https://github.com/tomTom1010-IEE/TomBSDF/blob/main/Assets/Mods/TomShadersBSDF/Documents/Installation.md)
| [Showcase/manual](https://tomtom1010-iee.github.io/TomBSDF/)
