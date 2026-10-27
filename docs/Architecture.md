# Runtime Architecture

## Stable Boundaries

Nine original assemblies, six BepInEx entries, their versions/GUIDs, component
names, serialized fields, config/save keys and public facades stay in place.
The additional `TomRender.Core.dll` has no BepInEx entry. Migrated helpers require
it, but shaders do not require a mandatory replacement renderer.

Phase-1 byte-identical sources, build controls and full IL snapshots remain
immutable history. The active gate is ABI/serialization compatibility plus
per-step behavior, rendering and lifecycle evidence. Compiler-generated private
lambda-cache ordinals and class-attribute metadata order are not public API;
attribute payloads, actual component types and serialized fields remain checked.

## Shared Core

| Service | Ownership and limits |
| --- | --- |
| RenderCoordinator | Per-invocation IDs, camera generations, nesting, deterministic order, reverse disposal, aborts, bounded traces and fault isolation |
| UnityCameraHub | One native callback set, transient maintenance driver, no implicit GPU pass |
| SceneColorRouter | Protocol-1 successful opaque-effect completion, native source by default, current invocation only |
| SceneDiscovery | Candidates cached only for an invocation; material values, bounds, visibility and MPBs remain live |
| RendererBoundsBroker | One original baseline/mesh clone per renderer, union of separate TomX/BSDF contribution policies, owned-state restoration |
| RendererPropertyBroker | Float/matrix/vector field leases; preserve unrelated MPB edits and newer external writers |
| RenderResourceLease | Exclusive transient 2D/cubemap allocations, purpose/stage/encoding descriptors, accounting, optional owner budget, camera generation/invocation and explicit disposal |
| CameraRoles | Explicit SH camera claims and guarded pooled reflection-camera resolvers for the lifetime of an owned capture |
| CaptureJobs | Exclusive cross-frame capture lane and diagnostic identity; existing scheduler retains selection, sampling budget, timeout and publication |

No equal-size cross-purpose texture aliasing, automatic scene-color acquisition
or global capture-quality override is introduced. Native shadow maps and native
ReflectionProbe cubemaps remain Unity-owned. Shared leases own only the helper's
transient copies/SH capture target, not those native resources. Module-owned
materials/readbacks retain their explicit disposal paths.

## Camera Order

Prepare/begin participants run in this order and unwind in reverse:

1. Probe sky/owned capture (50).
2. Native DOF preparation (200).
3. BSDF service, retaining its internal bounds/shadows/glass/buffers order (300).
4. Feather field preparation (400).
5. Feather nested-global guard (450).

The Core scope follows native pre-cull/pre-render/post-render callbacks; it is
not a universal postprocessing graph. Early DOF still executes through its
ImageEffectOpaque relay. Only a successful result publishes the opaque-effects
route, re-recording the same not-yet-executed BeforeForwardAlpha buffer. There
is no second copy pass, new sampler or late transparent capture.

Feather keeps its partial-rect immediate preparation and later publication.
A helper-free nested camera cannot inherit a parent's field. Existing public
Prepare and module entrypoints remain facades; old native subscriptions do not
run in parallel.

## Probe and Fallback Policy

SH-first batch ordering, one active capture, per-frame budget, 120-second
timeout, cancellation, atomic atlas publication and native RenderProbe are
preserved. Explicit SH cameras and guarded native reflection cameras receive
the Probe role. Screen refraction and early DOF are excluded; legitimate scene
lighting is not globally forbidden. Coincident realtime probe owners remain
an error, not an ownership guess.

DOF remains a local-only prototype, Native by default with scene Inherit.
Only the private BSDF IL transpiler was removed; the guarded native DOF prefix
remains. MCP is an optional client using the original facades, never a rendering
dependency. Missing helper resources retain explicit unavailable/hard fallbacks.

## Unity Authoring and Delivery

The original BSDF/Probe Runtime DLL paths remain unchanged. TomX components and
DOF relay source mirrors remain Assembly-CSharp in the authoring Editor; do not
replace them with duplicate game plugin DLLs. Fourteen original GUID/local-file-ID/
type/assembly records are checked through AssetDatabase.

Shaders, LUTs, MaterialEditor catalogs, native assets and GPU algorithms remain
in their shader projects. No shader edit, Bundle rebuild, game deployment,
publication or KK adaptation is authorized by this integration. See
[status and accepted evidence](Runtime-Integration.md) and
[build/staging gates](Build-and-Staging.md).
