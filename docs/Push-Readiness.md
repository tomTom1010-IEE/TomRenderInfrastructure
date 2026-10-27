# Push And Release Readiness

The 28 October 2026 runtime cohort is frozen and its controlled KKS regression
has passed. The maintainer subsequently authorized public source/release
publication to TomRenderInfrastructure and dependency-split TomX/BSDF releases.
Core/Probe/SceneEffects licensing is AGPL-3.0 with inherited notices preserved.
This document records the release gates; permission is not inferred from a build.

## Source Candidate

Include maintained `src`, build/test/integration tooling, project/solution files,
portable English documentation and preserved license/provenance contracts.
The frozen phase-1 sources and old build templates under `migration` are explicit
audit references, not another maintained runtime. Do not refresh historical
hashes or remove their notices merely to make the repository look newer.

Keep generated outputs, SDK/game DLLs, bundles/zipmods, `bin`/`obj`, local settings,
credentials, connection descriptors, private AGENTS instructions, raw game scenes,
captures, backups and logs out of the source commit. `.gitignore` covers those
boundaries; the private candidate list/audit is retained under the live evidence
root. No `.dll`, `.exe`, `.zipmod`, `.unity3d` or raw PNG was a source candidate
at this audit. Inspect `git status` and the eventual staged diff again immediately
before any later commit; current ignore rules are not a permanent secret scanner.

## Gates Before Public Push

1. Preserve the remote root AGPL-3.0 and the maintainer's new-module grant,
   along with the original BSDF MIT and TomX/upstream notices.
   See [license scope](../LICENSES/Scope.md). No upstream notice is removed.
2. Review original provenance manifests for public suitability. They intentionally
   retain historical origin/toolchain information; any publication sanitization
   must preserve the immutable local audit records rather than rewriting them.
3. Preserve the existing initial commit/LICENSE in the user-created repository
   `tomTom1010-IEE/TomRenderInfrastructure`; do not replace its history.
4. Freeze a separate source-publication receipt after documentation/license review.
   Retain the accepted runtime fingerprint and native test receipts independently.

## Gates Before Binary Release

- Publish an exact dependency/resource manifest and hashes. The eight-DLL current
  profile and ten-DLL prototype profile are not complete shader installers.
- Reuse only reviewed shader/resource packages. A local Probe repack and an
  unchanged Feather Bundle are not new public releases by themselves.
- Include one Core and each plugin/runtime pair at its original destination;
  reject duplicate old installs and provide a version-aware rollback procedure.
- Keep DOF Native by default, with scene Inherit. Early mode affects all
  transparent geometry; it is not a selective glass blur correction.
- Retain the narrow [live acceptance scope](Live-Regression.md), existing warnings
  and experimental/prototype labels. Broader character/mod/GPU testing remains
  distinct from this controlled installation and its saved-scene round trips.

No shader source, accepted Bundle, Unity native asset or MCP interface was changed
for this freeze/deployment/regression. MCP remains an external optional client,
not a rendering dependency.
