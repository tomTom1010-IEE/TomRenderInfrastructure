# Shared Discovery Registry

## Status

Version 0.1.1, qualified 29 October 2026. Historical 0.1.0 archives
are unchanged. No shader changes, Bundle rebuild or KK adaptation are part of
this work. Existing assembly versions, six BepInEx identities, Unity component
types, serialized fields and protocol/config/save keys remain unchanged.

The supplied large scene reproduces the 0.1.0 CPU stall without any modern BSDF
materials. The replacement passed local Editor and KKS Studio qualification.
The original public 0.1.0 artifacts do not contain this fix.

## Discovery Ownership

Core indexes scene Renderer and Light identities through scene-root traversal.
It never calls Unity global object searches from renderer queries or camera
callbacks. One maintenance driver services all interests, including Probe and
the three TomX helpers; there is no separate native discovery pass per module.

`WatchShaders(name, classifier)` leases a `RendererInterest`. Its classifier
may inspect immutable shader identity/declarations only. Classification is
cached per Shader reference, invalidated when interests change. Material floats,
textures, keyword state and override tags must never enter this cache.

Each interest exposes indexed Renderer candidates and an aggregate layer mask.
Materials are not instantiated. A Renderer that has ever matched an active
interest stays on the small live-identity watch list until destroyed or the
registry is shut down. This permits immediate shader switches back into the
supported families without searching unrelated renderers.

Known identities are refreshed once per frame and once per matching camera
invocation. The latter covers direct shader/clone changes after LateUpdate and
repeated Camera.Render calls in one frame. Cameras with no matching watch layers
or a zero culling mask skip the identity walk. Consumers continue to read their
actual shared materials, parameters, visibility, bounds and MPBs at use time.

The old `Renderers()`/`Lights()` signatures remain compatibility views of the
complete indexed scene candidates, not BSDF-only lists. Queries do not initiate
discovery. All maintained hot-path consumers use classified interests instead.
Old native-search counters remain ABI-compatible and stay zero; hierarchy-pass,
node and material-read counters separately describe the background work.

## Notifications and Fallback

- Core observes Unity scene load/unload/active-scene transitions.
- The KKS companion observes public Studio item/character/light add and scene
  load/import APIs through the existing BepInEx-bundled Harmony runtime.
- Optional public MaterialEditor `SetShader`, `GetMaterials` and `SetMaterials`
  hooks notify only the affected Renderer or object subtree. No ME private IL,
  dependency on StudioMCP or mandatory ME assembly reference is introduced.
- `NotifyRenderer` updates an identity immediately; it also accepts an already
  destroyed Unity reference for explicit removal. `NotifyHierarchy` inspects
  only the explicitly supplied object subtree and discovers its lights too.
- Unknown providers and deferred creation use shared hierarchy fallback. Known
  identities are read live; previously unrelated, unnotified renderers may take
  a discovery cycle to join. This is a deliberate, measurable latency boundary.

Background traversal uses an initial default budget of 0.35 ms per maintenance
tick and waits one second between completed passes. Steps walk individual
Transforms and reuse component/root/material storage. The budget is cooperative:
one Unity API call or root-list collection cannot be preempted. Large scenes can
increase discovery latency rather than multiplying callback cost by camera count.
`Pump` refuses to run inside an active render invocation. Explicit object/ME
notifications are not background scans and can perform work proportional to that
specific edited subtree. No fixed worst-case latency is claimed before measurement.

## Consumer Contracts

| Consumer | Cached membership | Still live |
| --- | --- | --- |
| BSDF Tess | Tess shader family | HeightScale, active state, mesh/skinning/bounds ownership |
| BSDF shadows | Alpha, BackFront and Glass families | Camera layers/frustum, receiveShadows opt-out, light state/transforms, budgets and native map freshness |
| BSDF glass | Glass family | Refraction toggle, coverage, visibility and requested buffer resolution |
| BSDF quality | Seven modern surface families | Material tags, SceneLighting, intensity, quality, temporary keyword save/restore and capture leases |
| Probe | Shader declares CustomSHVolumeBlend | Ready volumes, renderer state/position, volume priority/influence and MPBs |
| TomX | SDF/Tess/Feather shader families | Existing helper creation interval, shader parameters, head frame, bounds, queues and writer-pass checks |

No-ready-volume Probe assignment clears its previous MPB contribution once and
returns before receiver iteration. Probe capture scheduling/SH integration is
unchanged and can still make a volume ready. Reusable material/visited/stale
containers replace the per-assignment temporary collections.

Skipping a consumer is not skipping the render scope. Shadow/global restoration,
obsolete buffer demand, bounds ownership and nested quality state still unwind.
An external scene-buffer lease remains valid even if the camera contains no
BSDF material; this is necessary for shared scene effects and future consumers.
Frustum plane arrays are reused. Shadow scope containers are pooled by nesting
depth. This does not claim zero allocations across unrelated plugins or every
existing Core scheduling/resource path.

## Verification

The focused Unity fixture covers automatic full/half glass buffers, known
shader/clone/float changes, external buffer demand with no BSDF material, Tess
lease acquire/zero/release, three empty UI cameras, scoped realtime/reference
area keywords and native directional/spot/point shadows plus opt-out.

Existing buffer/DOF, Feather and Probe suites remain separate gates, including
frozen-pixel comparisons, nested cameras, abort recovery and capture lifecycle.
The deliberate Probe timeout test emits a controlled error; preserve and classify
it rather than clearing the Editor log or relabeling it as an unexpected failure.

Player qualification must additionally use the supplied large scene for paired
timings, actual public ME edits and actual Screencap lifecycle. Record source
fingerprints, loaded DLL hashes, preserved Bundles, raw windows and screenshots.
Editor surrogate/capture-lease checks are not renamed as native Studio tests.

### Local Results, 29 October 2026

The same supplied scene contained 2,816 Studio objects, 9,818 Renderers, seven
rendering cameras, no modern BSDF renderer and no ready SH volume. Paired windows
were 30 seconds, after settling, on Unity 2019.4.9f1 / D3D11 with the same accepted
Bundles. Service/component disable is an in-process A/B, not DLL removal.

| Window | FPS | Mean frame ms | BSDF callback ms/frame | Background discovery ms/frame |
| --- | ---: | ---: | ---: | ---: |
| Original 0.1.0 active | 4.52 | 221.44 | 182.98 | Not separately available |
| Original 0.1.0 BSDF service disabled | 55.93 | 17.88 | 0 | Not separately available |
| Candidate active | 60.21 | 16.61 | 0.115 | 0.328 |
| Candidate BSDF service disabled | 60.84 | 16.44 | 0 | 0.319 |
| Candidate BSDF and Probe manager disabled | 61.01 | 16.39 | 0 | 0.318 |
| Candidate reenabled | 60.19 | 16.61 | 0.110 | 0.328 |

Callback timing sums Prepare/Begin/End only; nested helper timings must not be
added again. Harmony overhead is included. These are CPU-hook and frame-interval
measurements, not GPU timings or an all-scenes performance guarantee. Native
renderer/light search counters remained zero in every candidate window. Probe
Update in the no-ready-volume window was about 0.001 ms/frame. An empty Studio
measured 0.057 ms/frame in BSDF callbacks and 0.007 ms/frame in discovery.

Adding three zero-mask UI cameras increased BSDF callback time from 0.124 to
0.152 ms/frame in separate 15-second windows; discovery remained about 0.328
ms/frame, with zero native global searches. Explicit ME changes and known-identity
changes after LateUpdate were checked separately. An unnotified new Renderer
joined the large-scene index in 1.71 seconds; changing its unrelated material
directly to BSDF, without an event, took 11.49 seconds. Destroying it removed its
watch entry. This fallback latency is real, not an immediate-discovery promise.

The 170 Editor checks comprise 33 discovery/native-shadow checks, 40 buffer/DOF
checks, 53 Feather checks and 44 Probe checks. The managed coordinator and tooling
gates passed 49 and 80 checks respectively. The ABI gate preserved all nine
original assembly/serialization contracts, six plugin identities and ten current
assembly names. Imported Unity component GUID/type/local-file identities match.

Real StudioMCP edits exercised glass off/on, shader changes to Tess and both Alpha
entries, zero/positive Tess height, SH disable/recovery and saved-scene ME readback.
Actual Screencap supersampling promoted area quality to Reference, then returned
to Realtime without overwriting the ME value. Positive directional, spot and point
shadow/opt-out pixel differences were checked in Editor; native shadow resources
were additionally verified for both Alpha entries in Studio.

The final integration fixture is pixel-identical to the previously accepted game
capture. SH recovery and saved-scene reload captures are also pixel-identical.
Large-scene active/disabled captures match; historical large-scene capture
differences are small but nonzero, so no historical pixel-identity claim is made
for its animated content. Original full-resolution captures, not MCP thumbnails,
are retained for comparison.

### Separately Recorded Issues

- The unchanged published rectangle-light catalog entry returned a null native
  object in this test environment. A third-party LightSettings patch then threw.
  No Bundle/prefab repair was attempted. Actual capture-quality validation used
  the same public control component attached temporarily to an owned in-memory
  fixture object. That validates quality routing, not catalog loading.
- LightSettings also reported a null collection while saving its own scene data.
  The native scene saved and the requested ME values survived independent reload;
  this does not certify that plugin's unrelated payload.
- Existing corrupt third-party archives, missing Splash GUI and other environment
  warnings remain in the private logs. No related content was modified or hidden.
- The Probe timeout in Editor is an intentional negative test. Temporary diagnostic
  lookup/invocation failures were retained, corrected and rerun; they are not
  represented as product failures or discarded from the evidence trail.

Local evidence for this round is under ignored
`artifacts/performance/20261029-registry/`. Original DLLs were backed up before
deploying the local test cohort. Do not reuse the old 0.1.0 distribution lock or
the frozen integration receipt to package this revision. The maintainer separately
authorized 0.1.1 publication after qualification. `distribution/0.1.1.json` binds
the new cohort and acceptance hashes; private evidence and user scenes are not
redistributed. See [release packaging](../packaging/README.md).
