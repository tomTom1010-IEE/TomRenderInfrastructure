# License Scope

Maintainer decision: 28 October 2026. The existing repository root `LICENSE`
contains GNU Affero General Public License version 3 and is preserved verbatim.

- New `src/Core`, `src/Probe` and `src/SceneEffects` code is offered under
  AGPL-3.0. The maintainer also applies this grant to the corresponding original
  Probe/SceneEffects material retained in the frozen migration references.
- TomX helper sources retain the original AGPL-3.0 project licensing and
  `TomShadersX-MIT-Upstream.txt` notice. Original upstream notices remain intact.
- BSDF helper sources retain `TomBSDF-module.txt`, the original MIT notice.
  Grouping the sources does not remove this permission or its notice obligation.
- Build, integration and test infrastructure newly authored for this repository
  follows the root AGPL-3.0 license. Imported templates and frozen references
  retain their recorded origin and applicable original notices.

This is a source/provenance map, not removal of third-party rights or a claim
that every imported line was originally authored under the same license.
Distributions include these notices and identify the exact corresponding source
commit. The external shader/resource projects retain their own licenses.

Game, Unity, BepInEx, Harmony and KKSAPI binaries are not included. Install their
legitimate host/dependency packages separately; this repository uses local SDK
references for compilation only.
