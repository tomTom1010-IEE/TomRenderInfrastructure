# Tom Shaders X 0.4.0 - Infra1 Distribution

**Install [TomRenderInfrastructure KKS 0.1.0](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/releases/tag/v0.1.0)
first**, then this shader pack. Shared runtime DLLs, Probe 0.2.2 resources and the
Feather helper are supplied by the base, **not** this archive. Install one base
for both TomX and TomBSDF, not duplicated copies per shader project.

- `TomShadersX-KKS-0.4.0-Infra1.zip`: unchanged accepted shader zipmod, neutral
  Skin import textures, updated English/Chinese manuals and preserved notices.
- `TomShadersX-KKS-0.4.0.zipmod`: byte-identical standalone main shader archive.
- `SHA256SUMS.txt`: checksums for both downloads; ZIP has internal payload hashes.

This is a **packaging/dependency update**, not a shader feature update or recompile.
All twelve shader entries, material defaults, accepted Bundle and neutral assets
remain unchanged. Existing saved material overrides are preserved. Close the game,
back up/remove duplicate old DLLs and superseded Probe sets, install the base,
then merge the shader pack's `mods` folder. Do not unpack zipmods over `abdata`.
The base's integrated DLLs replace the old pre-Core provider cohort; unchanged
plugin feature-version labels are not a reason to retain the older DLL bytes.

Native DOF/glass, Probe/SH and synthetic Feather/save-load integration passed
the base's controlled KKS regression. Whole-character/GPU and arbitrary mod
compatibility are not claimed. The optional DOF prototype is a separate base
download; Native remains its default. Old Complete releases remain available
as historical artifacts but must not be mixed with the new cohort.

KKS / Unity 2019.4 / Built-in Forward / D3D11. No KK port or new Bundle build.
Requires the usual BepInEx, KKSAPI, Extended Save, Sideloader and MaterialEditor.

[Release notes](https://github.com/tomTom1010-IEE/TomshadersX/blob/main/Documents/ReleaseNotes.en.md)
| [User manual](https://tomtom1010-iee.github.io/TomshadersX/UserManual.html)
