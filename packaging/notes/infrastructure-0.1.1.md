# TomRenderInfrastructure 0.1.1 - KKS Discovery Performance Fix

This update fixes excessive per-camera scene searches in the 0.1.0 shared runtime.
Install this base once for TomX and/or TomBSDF. **Existing shader packs do not need
to be rebuilt or reinstalled.** KKS / Unity 2019.4 Built-in / Windows D3D11 only.

## Downloads

- **Game base:** `TomRenderInfrastructure-KKS-0.1.1.zip` - eight DLLs plus the
  unchanged Feather helper Bundle and matching Tom Lighting Probes 0.2.2 resources.
- **Optional DOF:** `TomRenderInfrastructure-KKS-SceneEffects-Prototype-0.1.1.zip`
  - two DLLs, base required; Native / scene Inherit defaults. BeforeTransparents
  affects all transparent geometry, not glass alone. This remains a prototype.
- **Developers:** `TomRenderInfrastructure-KKS-Authoring-0.1.1.zip` - preserved
  Probe/Core Unity authoring resources, metadata and matching runtime DLLs.
  This is not a game install.
- `SHA256SUMS.txt` verifies the three archives; each ZIP has per-file hashes.

## Changes

- One persistent shared Renderer/Light registry replaces repeated world searches.
  Public Studio/ME notifications update affected objects; budgeted background
  traversal covers unnotified providers, outside camera rendering.
- Migrate BSDF bounds, transparent shadows, glass and quality consumers, plus
  Probe and TomX SDF/Tess/Feather. Keep material values live and restore scoped
  resources/keywords correctly when consumers disappear.
- No Shader/Bundle, GPU algorithm, DLL name, BepInEx GUID, Unity component,
  serialization identity, config key or saved-scene key change. Per-plugin
  versions stay unchanged; use the suite version and hashes to identify this fix.

## Measured Scope

In the supplied 9,818-Renderer scene, old active-service performance was about
**4.52 FPS**, versus **60.21 FPS** with this revision; disabling the new BSDF
service measured **60.84 FPS**. Instrumented BSDF callbacks fell from about
**183 ms to 0.115 ms/frame**, with about **0.328 ms/frame** of separate background
discovery. Candidate native global renderer/light search counters stayed zero.
Adding three empty UI cameras did not multiply discovery work.

Qualification passed **170 Editor checks, 49 coordinator checks, 80 tooling
checks and 26 recorded Studio checks**, including glass, both Alpha variants,
Tess, Probe/SH, actual Screencap quality, ME edits and save/reload. The final
integration fixture matches the previously accepted image pixel for pixel.
These are local measurements, not a promise for every scene, GPU or third-party mod.

Known membership/material changes remain live. Previously unrelated objects or
direct Shader changes with no notification wait for fallback discovery; one
large-scene test took **11.49 seconds**. The maintenance budget is cooperative.

The unchanged rectangle-light catalog entry failed native loading in this test
environment; real Screencap routing was tested with the same public control
component on an owned in-memory object. Third-party LightSettings save warnings
were retained separately; requested ME data survived independent reload. This
release does not claim to repair those catalog/host issues.

## Upgrade

Close KKS/Studio. Back up old DLLs outside active plugin/mod folders, then replace
the complete base cohort. **If the optional DOF prototype is installed, update
its pair too.** Remove duplicate DLLs; keep only one Core and one version per
zipmod GUID. Keep configuration and saved scenes. Merge only `BepInEx` and `mods`
from the game base into the game root; never unpack zipmods into `abdata`.

AGPL-3.0 for new work, original BSDF MIT and TomX/upstream notices retained.
No game/Unity/loader SDK, temporary diagnostics or private test scene is included.

[Install/upgrade guide](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/blob/v0.1.1/INSTALL.md)
| [Qualification and limits](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/blob/v0.1.1/docs/Discovery-Registry.md)
| [License scope](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/blob/v0.1.1/LICENSES/Scope.md)
