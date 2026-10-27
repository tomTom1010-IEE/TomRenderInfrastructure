# Codex Bridge Validation Adapter

`TomRenderInfrastructureBridge.cs` is the source copy of the bounded Editor
adapter used by this project. Its matching Assets copy is Editor tooling, not
a replacement runtime component. Unity creates and preserves its metadata.

The project bridge dispatches explicit operations:

- `observetomrendercore`: invoke the isolated observer fixture with repository
  root in `assetPath` and a new evidence directory in `outputDirectory`.
- `statustomrendercore`: read the script-domain epoch and exact loaded diagnostic
  binary hash pair. It does not start rendering.
- `reloadtomrendercore`: request script compilation, then verify the epoch
  actually changes before trying to load modified diagnostic DLLs.
- `integratetomrendercore`, `integratestep3tomrendercore` and
  `integratestep4tomrendercore`: import only the ABI-audited allowlist, backing up
  old files and verifying fourteen original script identities. The Step-3/4
  `core` variants import only Core while capturing an original-module baseline.
- `validatestep2tomrendercore`, `validatestep3tomrendercore` and
  `validatestep4tomrendercore`: isolated routing, ownership, rendering and capture
  fixtures. Step-3/4 `baseline` operations preserve original comparisons first.
- `nativeprobetomrendercore`: bounded asynchronous actual native RenderProbe
  fixture; explicitly enters Play mode and automatically exits. Command success
  means requested, not finished. Read its complete/passed report separately.
- `nativeprobeinventorytomrendercore`: read native probe inventory without capture.

Consume rendering commands before GPU work to prevent replay after a crash.
Always read the result JSON. A reload-request success is not proof that the
domain has changed. The adapter refuses stale loaded code rather than silently
loading a second assembly with the same identity.

Build the diagnostic DLL outside Assets with `Build-UnityValidation.ps1`. No
game SDK DLLs are imported. Authoring integration copies only the explicit Core/
existing Runtime DLLs and original component source mirrors, preserving metadata.
Diagnostics use preview scenes/transient objects and an explicitly owned native
Play-mode session; they do not save scenes, rebind user materials, compile Bundles
or deploy a game plugin. Native fixture cleanup immediately retires its exact
owned probes before exiting; it must never broadly sweep probes by position.
