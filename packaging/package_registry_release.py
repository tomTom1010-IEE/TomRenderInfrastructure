"""Package the qualified 0.1.1 registry cohort without compiling or deploying."""
import argparse
from pathlib import Path, PurePosixPath
import subprocess

from package_release import REPO, git_commit, json_bytes, package, read, sha, verify_zipmod


def safe_path(value):
    path = PurePosixPath(value)
    if not value or value.startswith(('/', '\\')) or '\\' in value or ':' in value or '..' in path.parts:
        raise RuntimeError('Unsafe locked path: ' + value)
    if path.as_posix() != value:
        raise RuntimeError('Noncanonical locked path: ' + value)
    return value


def payload_map(lock):
    entries = {}
    seen = set()
    for item in lock['payload']:
        name = safe_path(item['path'])
        if name.casefold() in seen:
            raise RuntimeError('Duplicate/case-colliding payload path')
        seen.add(name.casefold())
        entries[name] = item
    dlls = [name for name in entries if name.endswith('.dll')]
    if len(dlls) != 10 or sum('/TomSceneEffects/' in name for name in dlls) != 2:
        raise RuntimeError('Expected the accepted eight-DLL base and two-DLL prototype')
    return entries


def verify_evidence(lock, root):
    names = {'freeze': 'freeze.json', 'acceptance': 'acceptance.json',
             'evidenceAudit': 'evidence-audit.json', 'cleanupAudit': 'cleanup-audit.json'}
    evidence = {}
    for name, filename in names.items():
        path = root / filename
        if sha(path) != lock['qualification'][name + 'Sha256']:
            raise RuntimeError('Qualification receipt replaced: ' + filename)
        evidence[name] = read(path)
    for name in ['acceptance', 'evidenceAudit', 'cleanupAudit']:
        if not evidence[name]['passed']:
            raise RuntimeError('Failed qualification: ' + name)
    for name in ['freeze', 'acceptance', 'cleanupAudit']:
        if evidence[name]['sourceFingerprint'] != lock['runtimeSourceFingerprint']:
            raise RuntimeError('Stale source qualification: ' + name)
    accepted = evidence['acceptance']
    for name in ['editorChecks', 'managedCoordinatorChecks', 'toolingChecks', 'liveChecks', 'pixelComparisons']:
        if accepted[name] != lock['qualification'][name]:
            raise RuntimeError('Unexpected qualification counts')
    for name in ['temporaryDiagnosticRemoved', 'protectedResourcesUnchanged', 'originalSceneUnchanged']:
        if not accepted[name]:
            raise RuntimeError('Incomplete cleanup/integrity: ' + name)
    if accepted['shadersChanged'] or accepted['bundlesBuilt'] or accepted['computerUse']:
        raise RuntimeError('This release must preserve the qualified shader/resource cohort')
    return evidence['freeze']


def verify_authoring(export, lock, canonical):
    inventory = read(export / 'SOURCE-INVENTORY.json')
    if inventory != read(canonical / 'SOURCE-INVENTORY.json'):
        raise RuntimeError('Authoring export differs from the reviewed source inventory')
    expected_dlls = {
        'Assets/TomRenderInfrastructure/Runtime/TomRender.Core.dll': 'BepInEx/plugins/TomRenderInfrastructure/TomRender.Core.dll',
        'Assets/Mods/TomLightingProbes/Runtime/TomProbe.Runtime.dll': 'BepInEx/plugins/TomProbe/TomProbe.Runtime.dll',
    }
    locked = payload_map(lock)
    files = {}
    seen = set()
    for item in inventory['files']:
        name = safe_path(item['path'])
        if name.casefold() in seen:
            raise RuntimeError('Duplicate authoring path')
        seen.add(name.casefold())
        path = export / name
        if sha(path) != item['sha256'] or path.stat().st_size != item['bytes']:
            raise RuntimeError('Authoring file/metadata changed: ' + name)
        if name.endswith('.dll'):
            if name not in expected_dlls or sha(path) != locked[expected_dlls[name]]['sha256']:
                raise RuntimeError('Wrong authoring runtime DLL: ' + name)
        elif sha(canonical / name) != item['sha256']:
            raise RuntimeError('Authoring source is not the reviewed Git content: ' + name)
        files[name] = path
    if set(expected_dlls) != {name for name in files if name.endswith('.dll')}:
        raise RuntimeError('Missing Core/Probe authoring runtime')
    files['SOURCE-INVENTORY.json'] = export / 'SOURCE-INVENTORY.json'
    return files


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--evidence-root', type=Path, required=True)
    parser.add_argument('--resource-root', type=Path, required=True)
    parser.add_argument('--authoring-export', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise RuntimeError('Choose a new output directory; never overwrite a release archive')
    lock = read(REPO / 'distribution/0.1.1.json')
    locked = payload_map(lock)
    freeze = verify_evidence(lock, args.evidence_root)
    commit = git_commit(REPO)
    fingerprint = subprocess.check_output([
        'pwsh', '-NoProfile', '-Command', '. ./build/Common.ps1; Get-SourceFingerprint'
    ], cwd=REPO, text=True).strip()
    if fingerprint != lock['runtimeSourceFingerprint']:
        raise RuntimeError('Runtime/build inputs changed after qualification')
    built = read(args.evidence_root / 'build-receipt.json')
    abi = read(args.evidence_root / 'abi-audit.json')
    if built['status'] != 'built' or not abi['passed'] or built['sourceFingerprint'] != fingerprint or abi['sourceFingerprint'] != fingerprint:
        raise RuntimeError('Missing current ABI-audited build')
    if abi['buildReceiptSha256'] != sha(args.evidence_root / 'build-receipt.json'):
        raise RuntimeError('ABI audit does not bind the frozen build')
    expected_installs = {x['install'] for x in read(REPO / 'release.manifest.json')['assemblies']}
    if expected_installs != {name for name in locked if name.endswith('.dll')}:
        raise RuntimeError('DLL install inventory changed')
    frozen = {item['install']: item for item in freeze['files']}
    files = {}
    for name, item in locked.items():
        if name.endswith('.dll'):
            frozen_entry = frozen[name]
            output = [x for x in built['outputs'] if x['name'] == frozen_entry['name']]
            if len(output) != 1 or output[0]['path'] != frozen_entry['source']:
                raise RuntimeError('DLL source path does not match its frozen build output')
            path = REPO / safe_path(output[0]['path'])
            if output[0]['sha256'] != item['sha256'] or frozen_entry['after'] != item['sha256']:
                raise RuntimeError('DLL is not the accepted binary: ' + name)
        else:
            path = args.resource_root / name
        if sha(path) != item['sha256']:
            raise RuntimeError('Payload input hash mismatch: ' + name)
        files[name] = path
    protected = {x['path']: x['sha256'] for x in freeze['protected']}
    for item in lock['payload'] + lock['shaderZipmods']:
        if not item['path'].endswith('.dll') and protected.get(item['path']) != item['sha256']:
            raise RuntimeError('Resources differ from the accepted game cohort')
    probe = next(path for name, path in files.items() if name.endswith('.zipmod'))
    if verify_zipmod(probe, 'tom.lightingprobes', '0.2.2', 'studio/tom/tom_lighting_probes.unity3d') != lock['probeBundleSha256']:
        raise RuntimeError('Wrong Probe Bundle')
    if sha(files['BepInEx/plugins/TomHairFeather/hairx-feather.unity3d']) != lock['featherBundleSha256']:
        raise RuntimeError('Wrong Feather Bundle')
    base = {name: path for name, path in files.items() if '/TomSceneEffects/' not in name}
    prototype = {name: path for name, path in files.items() if '/TomSceneEffects/' in name}
    authoring = verify_authoring(args.authoring_export, lock, REPO / 'authoring')
    docs = {name: REPO / name for name in [
        'LICENSE', 'INSTALL.md', 'CHANGELOG.md', 'docs/Discovery-Registry.md',
        'docs/Live-Regression.md', 'LICENSES/Scope.md', 'LICENSES/README.md']}
    docs.update({f'LICENSES/{path.name}': path for path in (REPO / 'LICENSES').glob('*.txt')})
    metadata = {
        'schema': 1, 'version': lock['version'], 'date': lock['date'],
        'sourceCommit': commit,
        'correspondingSource': f'https://github.com/tomTom1010-IEE/TomRenderInfrastructure/tree/{commit}',
        'runtimeSourceFingerprint': fingerprint,
        'distributionLockSha256': sha(REPO / 'distribution/0.1.1.json'),
        'qualification': lock['qualification'], 'requiredShaderPacks': lock['requiredShaderPacks'],
        'shaderBuildPerformed': False, 'runtimeRebuiltDuringPackaging': False,
        'pluginIdentitiesChanged': False,
    }
    receipts = []
    for filename, payload, kind in [
        ('TomRenderInfrastructure-KKS-0.1.1.zip', base, 'base-eight-DLLs'),
        ('TomRenderInfrastructure-KKS-SceneEffects-Prototype-0.1.1.zip', prototype, 'optional-two-DLL-prototype'),
        ('TomRenderInfrastructure-KKS-Authoring-0.1.1.zip', authoring, 'Unity-authoring-not-game-install'),
    ]:
        payload.update(docs)
        if kind == 'Unity-authoring-not-game-install':
            payload['README.md'] = REPO / 'authoring/README.md'
        receipts.append(package(args.output / filename, payload, {**metadata, 'kind': kind}))
    (args.output / 'SHA256SUMS.txt').write_text(''.join(
        f"{item['sha256']}  {item['name']}\n" for item in sorted(receipts, key=lambda row: row['name'])
    ), encoding='ascii')
    (args.output / 'package-audit.json').write_bytes(json_bytes({
        'passed': True, **metadata, 'artifacts': receipts,
        'baseDLLs': 8, 'optionalDLLs': 2, 'authoringRuntimeDLLs': 2,
        'privateEvidenceIncluded': False, 'thirdPartySDKIncluded': False,
    }))
    print('Qualified 0.1.1 packages read back and hashed. No compilation or deployment.')


if __name__ == '__main__':
    main()
