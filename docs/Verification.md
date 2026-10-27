# Verification Scope

The subsequent 28 October 2026 controlled game test is recorded separately in
[Live Regression](Live-Regression.md). It adds native Studio DOF, glass, Probe/SH,
Feather and saved-scene checks without changing the immutable Editor evidence.

The section below describes the frozen phase-1 migration. Runtime revisions now
use additive ABI/serialization compatibility plus managed and Bridge behavior
gates, not IL equality. See [Runtime integration](Runtime-Integration.md) for
current progress, evidence and the still-outstanding steps.

## Required Checks

- Every one of the 35 runtime imports still matches its original SHA-256.
- Original project templates, build scripts and license notices are preserved.
- Unified solution builds nine DLLs with no changed plugin or assembly version.
- Fresh controls use the original layout and code-generation paths, in isolation.
- Assembly audit compares identities, references, attributes, all types and
  nested types, field layouts/constants/initial data, signatures, method IL and
  exception regions, properties/events and embedded resources.
- Plugin GUID/name/version values match the machine-readable release manifest.
- Default and explicit prototype staging contain only their exact seven/nine
  DLLs at preserved destinations, with hashes and external-resource warnings.
- Source, SDK references, intermediate outputs and private configuration have
  separate version-control boundaries.

PE timestamps, module MVIDs and debug-document paths are not runtime-equivalence
requirements. Legacy Framework csc embeds a random GUID in Feather's private
array-storage type name on each compilation. The audit normalizes only these
compiler metadata names; it still compares the array payload, layout and IL
references. It does not normalize user strings, plugin GUIDs, component names,
scene-save keys or public types. Full DLL hashes are recorded separately;
metadata/IL equivalence is **not** described as byte-identical DLLs.

`Test-Tooling.ps1` also checks that intentional changes to a copied DLL's plugin
GUID, component field name and runtime string are detected. Only ignored test
copies are mutated; imported sources and build outputs stay untouched.

## Evidence

Local, ignored evidence is produced at:

```text
artifacts/build-receipt.json
artifacts/logs/<run>/build.log
artifacts/baseline/<run>/receipt.json
artifacts/audit/report.json
artifacts/audit/snapshots/<assembly>.current.json
artifacts/audit/snapshots/<assembly>.legacy.json
artifacts/tooling-tests/report.json
artifacts/staging/<profile>-<run>/staging-manifest.json
```

Use current receipts rather than treating older failed/diagnostic folders as
passing evidence. The source fingerprint covers runtime/build inputs; staging
refuses changed inputs or DLLs. Logs and generated files are not Git payloads.

## Not Certified Here

The frozen phase-1 milestone was source-only. All four runtime integration steps
now have local accepted evidence, including current cross-module regression,
actual native RenderProbe and preserved authoring identities. Core, existing
BSDF/Probe authoring DLLs and original TomX/DOF component source mirrors were
imported through Bridge. That Editor milestone did not include game deployment.
The later controlled game cohort adds actual native Studio DOF and saved-scene
evidence; see Live Regression. No Bundle rebuild, installer publication or KK
support is certified. The marked Editor blur surrogate remains historical and
is not relabeled as native DOF. Local CPU intervals are not GPU timing claims.

The final receipt is artifacts/integration/acceptance.json. Default and prototype
local staging audits select exactly eight and ten allowlisted DLLs respectively.
Snapshots, comparison images, original failures and raw timing samples remain
under the ignored artifacts/integration tree. New source revisions invalidate
current qualification rather than overwriting historical acceptance.
