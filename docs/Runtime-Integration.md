# Runtime Integration

## Scope and Status

The authorized objective is all four steps below, not just source consolidation.
Keep the nine legacy DLLs, assembly versions, six BepInEx identities, public
facades, serialized component types/fields, native GUID/file IDs, shader
contracts, config keys and scene-save keys. Add `TomRender.Core.dll` without
a new BepInEx entry. The four-step integration itself did not deploy to a game.
The separately authorized 28 October controlled deployment and StudioMCP
regression are documented in [Live Regression](Live-Regression.md). No shader
edits, Bundle rebuild, public release or KK adaptation were added to either scope.

- [x] Phase-1 source/build/IL evidence preserved as immutable history.
- [x] Step 1 implementation: ABI/serialization gate and observer-only Core.
- [x] Step 1 final evidence archive: hash-matched Bridge binaries and complete reports.
- [x] Step 2: BSDF camera scope migration and explicit DOF color routing.
- [x] Step 3: Feather, shared discovery, resource and state ownership migration.
- [x] Step 4: Probe integration preserving capture algorithms and policies.
- [x] Current cross-module rendering/lifecycle/performance and dependency inventory gates.
- [x] Frozen test cohort, original-file backup and controlled KKS deployment.
- [x] Native Studio DOF/glass, Probe/SH, Feather and saved-scene StudioMCP regression.

This checklist must be updated as evidence is accepted. A passing observer
fixture must never be presented as completion of the remaining migrations.

## Frozen Evidence and New Gates

`migration/phase1/frozen/` holds all 35 original C# sources byte-for-byte.
`contracts.json` freezes the nine legacy ABI/serialization contracts and plugin
identities. Original full IL snapshots, accepted DLLs, receipts and build scripts
are retained locally under `artifacts/phase1/accepted/`. The source manifest and
its hashes are never refreshed to disguise runtime changes.

The active release manifest has schema 2 and ten assemblies. `Audit.ps1` now
routes to `Audit-Runtime.ps1`. It permits additive API and implementation-body
rewrites, but rejects removed/changed public members, component identity and
serialized fields, plugin identities and shader property contracts. Separate
behavior gates are mandatory. Sensitivity tests demonstrate both rejection of
serialization/GUID damage and allowance of an IL-only change, so ABI success
is explicitly not claimed to prove visual equivalence.

Staging is blocked while integration is incomplete. Merely having an ABI report
does not qualify the four-step goal or justify installing the new binaries.

### Accepted Step-1 Result

The accepted snapshot is `artifacts/integration/accepted-step1/acceptance.json`:
27 managed coordinator checks, 38 tooling checks, nine preserved ABI contracts,
29 Unity observer checks and 24 original DOF scheduling checks passed. All 35
legacy runtime sources were still unchanged at this boundary. Fourteen authoring
GUID/local-file-ID/type/assembly identities were frozen through AssetDatabase.

The six original presets had zero maximum pixel difference with observation on,
zero additional GPU objects or camera command buffers, and final callback/driver/
camera-index teardown passed. Paired median CPU Camera.Render delta was about
0.00428 ms in this small fixture with tracing disabled; it is not a GPU cost
claim or a promised universal overhead. Raw pairs are retained.

Step 1 remains the immutable pre-migration boundary, not evidence of later changes.

### Accepted Step-2 Result

`artifacts/integration/accepted-step2/acceptance.json` records 40 Unity integration
checks, 29 preset/observer checks, 43 managed routing/coordinator checks and 46
tooling checks. The imported Core and original BSDF Runtime DLL were verified by
hash; all fourteen original authoring identities survived. Native and early-DOF
final images and the actual opaque BSDF snapshot matched the frozen surrogate
baseline exactly. Six original BSDF preset PNGs also match their frozen hashes.

BSDF and DOF use a single Hub callback set. Scene-color protocol 1 publishes only
successful early effects and re-records the existing event buffer. The private
BSDF IL transpiler is gone; the guarded native DOF prefix remains. Exception,
nested, repeated-frame, pre-Begin abort, no-demand, missing-resource, lost-target
and teardown gates passed. CPU timing pairs are saved, not GPU timing claims.
The editor blur surrogate is not native Studio DOF acceptance. No game binaries
were deployed. Steps 3 and 4 were still outstanding at this historical boundary.

### Accepted Step-3 Result

`artifacts/integration/accepted-step3/acceptance.json` records 53 Unity checks,
the 13-check original Feather baseline, repeated 40-check BSDF/DOF and 29-check
preset regression, fourteen authoring identities, 43 managed checks and 54 tooling
checks. Full/partial viewports, world-width, binary proxy and distance-field PNGs
match exactly. CPU render intervals and old/new medians are retained; they are
separate small-fixture runs, not paired GPU measurements.

Feather uses Hub preparation once per invocation and a shared nested global guard;
its public Prepare remains usable. Hard fallbacks, lost targets, aborts, helper-free
children and final disposal passed. Candidate discovery is invocation-scoped while
material values/MPBs stay live. Exclusive RT leases carry purpose/stage, camera
generation/invocation and accounting; no cross-purpose aliasing or new GPU pass
is introduced. Destroyed-camera RTs are collected without another render.

TomX and BSDF bounds now combine against one baseline and one mesh clone. Tests
cover smoothing, inward height, replacement, no accumulation and skin offscreen
updates. Per-renderer BSDF math is invoked directly in a preview scene because
Unity global discovery omits preview objects; the user scene is not the fixture.
Field-scoped MPB leases preserve unrelated edits and newer external writes.
Unity 2019 cannot remove a single MPB field: absent owned flags restore to their
neutral zero rather than clearing another provider's block. The five native-shadow
GPU command-recorder/policy bodies remain IL-identical to phase 1.

Framework csc can reorder class attributes. The ABI gate now compares complete
attribute payloads as a set; sensitivity tests still reject missing attributes.
The frozen contract itself was not changed. Probe and final cross-module gates
were still outstanding at this historical boundary.

### Accepted Final Result

`artifacts/integration/acceptance.json` links the four immutable step receipts.
The current candidate passed 44 Probe, 53 Feather/shared-ownership, 40 BSDF/DOF
and 29 preset/observer Unity checks, 49 managed checks and 69 tooling checks.
All fourteen original authoring identities survive. Native/early DOF, Feather
full/partial/world-width, six presets, six-face floating radiance and all SH
coefficients match their original baselines. Actual time-sliced native RenderProbe
also completed six faces in a bounded, automatically exited Play-mode fixture;
its PNGs match the old runtime. No shader or Bundle was rebuilt.

Probe camera callbacks now use Core, explicit/guarded capture roles exclude
screen effects without forbidding legitimate lighting, and the original queue
owns a cross-frame capture lane. SH-first ordering, single-job execution, sample
budget, original 120-second timeout, cancellation and atomic publication passed.
Nine capture/integration/publication method bodies remain IL-identical. The
private compiler-generated lambda-cache ordinal is excluded from ABI comparison,
not actual Unity component or public/serialized contracts.

Small-fixture CPU Camera.Render medians were approximately 0.11 ms before and
0.17 ms after for Feather; six-face SH sample intervals were about 0.67 ms before
and 0.76 ms after. Raw samples are retained. These are separate Editor runs,
include render submission/readback effects, and are not isolated GPU timings,
whole-scene frame budgets or a performance-improvement claim.

Diagnostic failures remain recorded: the first unlit/texture-filter fixtures
were corrected without relaxing comparison gates; native RenderProbe required
Play mode; an exact diagnostic-owned orphan was inspected and retired rather
than bypassing ambiguous-owner protection. Intentional timeout/fault injections
are expected test errors. Existing CS0649/AssetBundle Browser environment warnings
are separate from runtime acceptance. Final probe inventory is empty.

This is local authoring/runtime integration acceptance, not a public game release.
At that Editor boundary, native Studio DOF was covered only by a marked surrogate.
The subsequent game regression is separate evidence, not a rewrite of those
reports. Shader assets and accepted Bundles were unchanged by integration.
Broader game compatibility, GPU performance, publication/licensing and KK
adaptation remain separate work.

## Step 1: Observation Before Ownership Transfer

The production `RenderCoordinator` is a pure managed state machine. It provides
per-invocation IDs, parent scope, live-camera generation, target descriptor,
deterministic participant order, reverse cleanup, exception isolation, safe
unregistration and bounded optional traces. It never allocates GPU resources.

`UnityCameraHub` owns one native subscription per camera event while participants
exist. The final lease detaches those subscriptions and removes its transient
driver and camera index. At this step all original modules are untouched; the
hub only observes explicit validation renders. Old module callback ownership
does not change until subsequent steps.

Pure tests link the production coordinator source and run on .NET 9. They are
not Mono/Unity timing claims. Bridge tests load the exact Core/diagnostic DLL
bytes outside Assets, render six existing BSDF presets in a preview scene,
compare pixels, native event counts, GPU object sets and command buffers, test
same-frame and nested Camera.Render calls, target changes and final teardown.
AB/BA CPU Camera.Render intervals are saved; they are not GPU shader timings.

Native authoring inspection must preserve the important split: TomX components
and the DOF relay currently live in `Assembly-CSharp` in the Editor, but game
helper types live in their original plugin DLLs. Do not replace the authoring
scripts with those plugin DLLs. BSDF/Probe Runtime DLL script GUIDs and local
file IDs are inspected through AssetDatabase as well.

## Step 2: BSDF + DOF

Transfer BSDF and the DOF plugin's native camera callbacks to registrations in
the hub, initially preserving each module's existing internal order. Old entry
points remain as facades. Disposal during an active render must unwind only
that participant, not erase another camera's published resources. Aborted
captures must not become valid completed snapshots.

Replace `BsdfCaptureAdapter`'s private `PrepareCamera` IL matching with an explicit
versioned scene-color route. The DOF native prefix itself stays guarded; it is
the BSDF transpiler, not native DOF behavior, that is being removed.

Recommended implementation contract:

- Default buffer source is the existing native CameraTarget route.
- An active Core invocation owns its route/observers; camera ID or frame number
  alone is not sufficient. Nested cameras and repeated renders cannot share a stamp.
- Only successful early DOF processing publishes its post-opaque-effects source.
  A scheduled pre-cull relay is not a completion signal.
- The buffer provider re-records the same not-yet-executed BeforeForwardAlpha
  command buffer when that route changes. It does not execute another copy,
  add a sampling pass, move the copy past transparency or retain a stale source.
- Missing DOF/provider/consumer and faults preserve native routing and existing
  explicit availability semantics. No screen buffer is created without demand.
- Keep `TomBSDFSceneService.Current`, `Buffers`, `TryGetSnapshot`, snapshot fields,
  and the DOF StateJson/ApplyScenePolicy/ApplyGlobalMode methods in their old
  assemblies. MCP stays a client, not a rendering or capture dependency.

Test the actual candidate implementation, not just the old Editor DLL. Before
updating authoring mirrors, prove ABI, preserve the frozen GUID/file-ID contract,
and use a Bridge-managed integration path. If isolated aliased diagnostic
assemblies are used, clearly state that they test behavior, not final native
assembly identity/resource deserialization; those require their own gate.

## Step 3: Feather and Shared Ownership

Transfer callback execution exactly once; old callbacks must only forward or
be disconnected, never run alongside a second renderer. Preserve Feather's
partial-rect immediate preparation path and later publication, format checks,
writer ordering and missing-pass hard fallback. Shader algorithms and helper
Bundle remain unchanged.

Share candidate discovery, not stale material values. Keep ME clones, shader
switches, per-slot MPBs and Timeline edits visible. A shared bounds broker must
combine contributions against the original bounds without accumulating padding
or cloning the same Mesh twice; preserve TomX smoothing and BSDF inward-height
semantics as separate contribution calculators. Restore only owned state and
do not replace entire property blocks over unrelated providers' updates.

Unify resource leases/descriptors/lifetime/budget first. Color, raw depth,
Feather masks and probe cubemaps cannot alias just because their sizes match.
Resource identity includes camera/generation/invocation/stage, format and color
encoding. Zero demand, camera destruction, disable, shader changes and failed
allocation must release or invalidate owned resources and restore borrowed state.

## Step 4: Probe Last

Keep single active capture, SH priority within shared refresh batches, sample
budget, timeout, cancellation and atomic publish. Do not replace the native
RenderProbe path, SH integration or readback algorithms. Preserve explicit
SH camera ownership and guarded native reflection-camera identification,
including rejection of ambiguous coincident probes.

Register capture roles explicitly. Do not apply screen refraction or DOF to
auxiliary captures accidentally, and do not globally forbid reflection-camera
lighting contributions that the old capture legitimately includes. Capture-job
lifetime spans faces/frames and differs from one camera render invocation.

## Completion Requirements

Each transferred module needs duplicate-subscription/one-execution checks,
nested and same-frame renders, exception/abort/disable/destruction cleanup,
resource-demand and unsupported-host fallbacks, pixel comparison and raw CPU/
available GPU timing evidence. Native asset identities and old reflection APIs
must remain intact. Only after all four steps and their shared cases pass may
`artifacts/integration/acceptance.json` authorize migrated-binary staging.

Classify existing AssetBundle Browser duplicate-GUID errors and old Editor
unassigned-field warnings separately from new runtime/fixture failures. Early
diagnostic attempts (unknown operation before refresh, incomplete report
serialization, stale loaded-DLL rejection) remain historical evidence; use only
the final complete, hash-matched acceptance report for qualification.
