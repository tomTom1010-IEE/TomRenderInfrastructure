# Frozen Release Packaging

This tooling repackages already accepted bytes. It does not compile shaders,
build Bundles, rebuild runtime DLLs, deploy to a game or write to GitHub.

The public `distribution/0.1.0.json` locks the runtime cohort, resource hashes
and unchanged shader zipmods. Its runtime fingerprint belongs to the accepted
build; documentation/packaging/authoring-source additions do not change that
frozen input set. The older `release.manifest.json` is the preserved integration
build contract; its historical status string is not the public release status.

## Maintainer Procedure

### 0.1.1 Registry Update

This patch uses `distribution/0.1.1.json`, not the historical 0.1.0 lock. Its
accepted runtime fingerprint and private freeze, acceptance, evidence and cleanup
hashes are immutable inputs. The packager verifies the current runtime/build
fingerprint, frozen ABI-audited output hashes, exact eight-plus-two DLL inventory,
unchanged Probe/Feather resources and the reviewed 53-file authoring inventory.

After reviewing and committing the source, run:

```powershell
python packaging/package_registry_release.py `
  --evidence-root '<qualified registry evidence directory>' `
  --resource-root '<previous accepted Frozen/Payload directory>' `
  --authoring-export '<fresh read-only Codex Bridge export>' `
  --output '<new publication directory>'
python -m unittest discover -s packaging -p 'test_*.py'
```

The resource root supplies only the two locked resource files, never its old
DLLs. New DLLs come exclusively from the accepted registry build. Authoring
Core/Probe DLLs must match that cohort; all native source/meta files must match
the reviewed Git tree. Output contains the base, separate optional prototype,
developer authoring ZIP and archive checksums. Every entry is read back and
hashed; each archive identifies the corresponding source commit. Private
diagnostics/scenes, SDKs and logs are not included. Packaging/English-doc changes
do not replace runtime acceptance or mutate the old 0.1.0 history.

### Historical 0.1.0 Coordinated Release

1. Preserve the immutable freeze and passing, hash-bound live acceptance.
2. Use the Editor exporter in `UnityEditor` through the matching Codex Bridge
   `exporttomrenderauthoring` dispatch to a **new** `Build/Source` directory.
   Its read-only copies preserve original native GUIDs and bytes. Do not build.
3. Commit the reviewed infrastructure, TomX and BSDF source trees. Keep SDKs,
   private test scenes/tokens, generated binaries and raw evidence out of Git.
4. Run `package_release.py` with explicit paths:

```powershell
python packaging/package_release.py `
  --frozen-root '<private frozen live-test directory>' `
  --live-report '<matching private live-acceptance.json>' `
  --authoring-export '<Unity-exported directory>' `
  --tomx-root '<TomX source checkout>' `
  --bsdf-root '<BSDF publication checkout>' `
  --output '<new output directory>'
```

Every input must match its locked hash; every ZIP entry is CRC/hash checked on
readback. Base has eight DLLs and matching Probe/Feather resources. The optional
DOF archive contains two DLLs and no second Core. Shader archives reject DLLs,
loose helper Bundles, Probe zipmods and BepInEx payloads. Each archive records
all three corresponding source commits and per-file checksums.

The original TomX/BSDF Complete releases remain historical. Do not use their
old embedded-provider packaging scripts for the Infra1 split. New tags are
`v0.1.0`, `v0.4.0-infra.1` and `v0.2.0-rc1-infra.1`; BSDF remains an RC pre-release.
Publish/upload only with explicit authority and verify remote asset digests.

## Documentation Refresh

Install the optional versions in `requirements.txt`. After updating consumer
Markdown/source exports, run `refresh_consumer_docs.py --tomx <root> --bsdf <root>`.
It regenerates manual article content and adds install notices through an HTML
parser; showcase PNGs are never modified. Preserve each project's original
styling, accepted images, native resources and upstream notices.
