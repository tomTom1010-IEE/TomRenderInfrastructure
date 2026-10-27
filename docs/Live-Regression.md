# Controlled KKS Regression

Date: 28 October 2026. Status: passed within the scope below; unpublished.
This is additional game evidence after the four-step Editor integration, not
a claim that every scene, graphics device or third-party plugin is certified.

## Frozen Cohort

Runtime/build source fingerprint:

```text
4d737e3abc655272773acb9e3d6ebc00f016b073b3187a00b1469645a80e16d6
```

The accepted `with-prototypes` cohort contains ten DLLs: the nine preserved
assemblies and entry-free Core. Six BepInEx GUIDs and the authoring component
identities retain the previously accepted contracts. TomX's final schema-2
consumer handoff was checked against this same cohort before deployment.
Runtime/build sources and all installed payload bytes were rechecked after play.

The local evidence root is `artifacts/live/20261028-01/`. `freeze.json` records
the source/acceptance/handoff and payload hashes. `deployed.json` records exact
installed paths and hashes. `live-acceptance.json` contains the final checks;
`Evidence/index.json` identifies preserved, unmodified captures and checkpoints.
These files and the original backups are intentionally excluded from Git.

## Deployment And Recovery

- Original installed helper DLLs and all three involved shader zipmods were
  backed up and hash-verified before any replacement.
- Only the frozen cohort, the already-built Feather helper and a local Probe
  0.2.2 resource repack were deployed. The previous Probe 0.2.1 archive was moved
  outside active mods, not destroyed. Probe 0.2.2 needs its matching HDR background
  resource; installing only its newer DLLs over 0.2.1 resources is insufficient.
- The Probe repack used the previously accepted Bundle with SHA-256
  `d904a499082a9864e7055f474f727349160950dab525e40a494188f7fc4d733a`.
  Its manifest/list files were packaged without running Unity or rebuilding.
- Installed TomBSDF 0.2.0 RC1 and TomX 0.4.0 shader zipmods remained byte-identical.
  The matching Feather helper Bundle was also copied unchanged.
- There were no duplicate installed DLL filenames in the frozen inventory.
  StudioMCP itself was neither rebuilt nor replaced.

For rollback, first close the affected game process normally. Use `freeze.json`
and its verified `Backup` tree, not filenames from an unrelated release. Restore
the original DLL pairs and original Probe archive; move the test Probe archive
outside active mods. Retire only test files marked originally absent. Do not
remove a shared Core while leaving migrated dependents installed. Keep both
backup and test payloads until the restored environment is checked.

## Actual Game Checks

The installed StudioMCP host drove a newly launched, owned test Studio process.
No computer use, arbitrary runtime execution, user-scene overwrite, shader edit
or Bundle compilation was used. Render diagnostics reported Linear / D3D11.

| Area | Evidence and result |
| --- | --- |
| Defaults | Global Native and scene Inherit remained the initial and restored defaults. |
| Native DOF | The actual Studio DepthOfField component executed; this was not an Editor blur surrogate. |
| Early DOF | Successful opaque execution and late passthrough counters stayed one-to-one with zero provider failures. Native DOF parameters were unchanged by mode switching. |
| Glass routing | Refraction on/off produced a measurable image change. Early mode kept the glass boundary sharp and its opaque color snapshot contained the blurred background. Returning to Native restored the earlier Native image exactly. |
| Policy precedence | Scene Native overrode global Early; Inherit followed global Early; explicit scene Early with global Native produced identical comparison pixels. |
| Probe | The packaged native 256 capture completed with a ready HDR texture. Disable/re-enable affected and restored the reflection receiver. |
| SH | The packaged volume completed capture. Independent disable/re-enable changed diffuse lighting and restored identical pixels. |
| Feather | Real EyeWX stencil-writer and HairX receiver passes produced distinct Hard/Feather images. Logs confirmed one eligible source draw and seven filter passes; screenshot pixels confirmed the soft boundary. |
| Fallback and resize | Hiding the writer removed its effect; showing it restored the same Feather image. 640x360, 960x540 and 1280x720 captures completed; the 960x540 round trip matched exactly. |
| Persistence | New native scene PNGs were independently reloaded. ME shader/texture/value bindings, Probe/SH controls and DOF policy/parameters survived. Feather reload pixels matched exactly; the combined reload passed the recorded small pixel tolerance. |
| Teardown | The owned empty checkpoint was restored. No test objects or Probe controllers remained; early relay was disabled and Native/Inherit restored. |

The final automated report includes **154 checks**, combining backup/payload/
source integrity, deployment boundaries and the live comparisons above. It is
not 154 independent rendering scenarios. Captures are saved as original game PNGs;
no screenshots were synthesized or retouched. The test Studio was left open in
the restored empty state; saved test checkpoints remain available locally.

## Warnings And Limits

No error-level Tom-provider messages or DOF failures were observed. The installed
environment was not error-free: unrelated corrupt Sideloader archives, a missing
SplashScreen GUI, development-build notices, Autosave focus exceptions and a
LightSettings `OnSceneSave` null-collection exception were recorded separately.
The latter also occurred when saving the initial empty checkpoint. Sideloader
category/group resolution warnings did not prevent this fixture from reloading.
None of these third-party components was modified to hide its diagnostics.

Feather used controlled synthetic mesh/stencil fixtures, not a new whole-character
or animation certification. This MCP version did not expose a partial-viewport
control; the existing Editor partial-viewport gate remains separate. Capture
latency and a still image are not GPU timing or exhaustive resource-leak proofs.
Face SDF and Tess bounds retain their prior Editor qualification, not new active
character/displacement coverage here. No KK qualification, publication or push
was performed. SceneEffects remains an opt-in local prototype.
