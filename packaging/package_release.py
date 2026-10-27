"""Package the frozen 0.1.0 cohort and unchanged consumer shader archives.

No compilation, Unity edits, deployment or GitHub writes are performed here.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[1]


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(stream):
    sha = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b''):
        sha.update(block)
    return sha.hexdigest()


def sha(path):
    with path.open('rb') as stream:
        return digest(stream)


def json_bytes(value):
    return (json.dumps(value, indent=2, ensure_ascii=False) + '\n').encode('utf-8')


def git_commit(path):
    status = subprocess.check_output(['git','status','--porcelain'], cwd=path, text=True)
    if status.strip():
        raise RuntimeError(f'Commit the reviewed source before packaging: {path.name}')
    return subprocess.check_output(['git','rev-parse','HEAD'], cwd=path, text=True).strip()


def verify_zipmod(path, guid, version, bundle):
    with zipfile.ZipFile(path) as archive:
        if archive.testzip():
            raise RuntimeError('Zipmod CRC failure: ' + path.name)
        manifest = ET.fromstring(archive.read('manifest.xml'))
        if manifest.findtext('guid') != guid or manifest.findtext('version') != version or manifest.findtext('game') != 'Koikatsu Sunshine':
            raise RuntimeError('Wrong zipmod identity: ' + path.name)
        with archive.open('abdata/' + bundle) as stream:
            return digest(stream)


def package(destination, files, metadata, forbid_helpers=False):
    files = dict(files)
    if len({name.casefold() for name in files}) != len(files):
        raise RuntimeError('Case-colliding Windows package paths')
    for name in files:
        if name.startswith(('/', '\\')) or '\\' in name or ':' in name or '..' in Path(name).parts:
            raise RuntimeError('Unsafe package path: ' + name)
        lower = name.casefold()
        if forbid_helpers and (lower.startswith('bepinex/') or lower.endswith(('.dll','.unity3d')) or 'lighting probes' in lower):
            raise RuntimeError('Shader archive embeds a helper: ' + name)
    files['release-manifest.json'] = json_bytes(metadata)
    expected = {}
    for name, value in files.items():
        expected[name] = sha(value) if isinstance(value, Path) else hashlib.sha256(value).hexdigest()
    files['SHA256SUMS.txt'] = ''.join(f'{expected[name]}  {name}\n' for name in sorted(expected)).encode('ascii')
    expected['SHA256SUMS.txt'] = hashlib.sha256(files['SHA256SUMS.txt']).hexdigest()
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(destination, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name, value in sorted(files.items()):
            if isinstance(value, Path):
                archive.write(value, name)
            else:
                archive.writestr(name, value)
    with zipfile.ZipFile(destination) as archive:
        if len(archive.namelist()) != len(expected) or len(set(archive.namelist())) != len(expected) or archive.testzip():
            raise RuntimeError('Archive layout/CRC failure: ' + destination.name)
        for name, wanted in expected.items():
            with archive.open(name) as stream:
                if digest(stream) != wanted:
                    raise RuntimeError('Archive payload mismatch: ' + name)
    return {'name':destination.name, 'bytes':destination.stat().st_size,
            'sha256':sha(destination), 'entries':len(expected)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--frozen-root', type=Path, required=True)
    parser.add_argument('--live-report', type=Path, required=True)
    parser.add_argument('--authoring-export', type=Path, required=True)
    parser.add_argument('--tomx-root', type=Path, required=True)
    parser.add_argument('--bsdf-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise RuntimeError('Choose a new output directory; previous packages are never overwritten')
    lock = read(REPO / 'distribution/0.1.0.json')
    frozen = read(args.frozen_root / 'freeze.json')
    live = read(args.live_report)
    if not live.get('complete') or not live.get('passed') or live['sourceFingerprint'] != lock['runtimeSourceFingerprint'] or frozen['sourceFingerprint'] != lock['runtimeSourceFingerprint']:
        raise RuntimeError('Missing matching complete live acceptance')
    if live.get('frozenReceiptSha256') != sha(args.frozen_root/'freeze.json'):
        raise RuntimeError('Live acceptance is not bound to the supplied freeze')
    sources = {name:git_commit(root) for name,root in [
        ('TomRenderInfrastructure',REPO),('TomshadersX',args.tomx_root),('TomBSDF',args.bsdf_root)]}
    frozen_files = {}
    for item in lock['payload']:
        path = args.frozen_root/'Frozen/Payload'/item['path']
        if sha(path) != item['sha256']:
            raise RuntimeError('Frozen payload changed: ' + item['path'])
        frozen_files[item['path']] = path
    shader_files = {}
    for item in lock['shaderZipmods']:
        path = args.frozen_root/'Backup'/item['path']
        if sha(path) != item['sha256']:
            raise RuntimeError('Accepted shader zipmod changed: ' + item['path'])
        shader_files[path.name] = path
    probe = next(path for name,path in frozen_files.items() if name.endswith('.zipmod'))
    if verify_zipmod(probe,'tom.lightingprobes','0.2.2','studio/tom/tom_lighting_probes.unity3d') != lock['probeBundleSha256']:
        raise RuntimeError('Wrong Probe resource Bundle')
    bundles = {
        'TomshadersX':verify_zipmod(shader_files['TomShadersX-KKS-0.4.0.zipmod'],'tom.Shaders.X','0.4.0','chara/tom/shaders/tomx.unity3d'),
        'TomBSDF':verify_zipmod(shader_files['TomBSDF-KKS-0.2.0-RC1.zipmod'],'tom.Shaders.BSDF','0.2.0','chara/tom/shaders/tombsdf.unity3d')}
    if bundles['TomBSDF'] != '0904d5aa53b43639d02660c040ae8a08d1774c38af965ceefe0335c22890c200':
        raise RuntimeError('Unaccepted BSDF Bundle')
    common = {'schema':1,'date':lock['date'],'sources':sources,
              'runtimeSourceFingerprint':lock['runtimeSourceFingerprint'],
              'shaderBuildPerformed':False,'runtimeRebuilt':False,'shaderBundles':bundles,
              'baseDependency':'TomRenderInfrastructure KKS 0.1.0',
              'correspondingSource':{name:f'https://github.com/tomTom1010-IEE/{name}/tree/{commit}' for name,commit in sources.items()}}
    licenses = {'LICENSE':REPO/'LICENSE', 'LICENSES/Scope.md':REPO/'LICENSES/Scope.md',
                'LICENSES/README.md':REPO/'LICENSES/README.md'}
    licenses.update({f'LICENSES/{p.name}':p for p in (REPO/'LICENSES').glob('*.txt')})
    base = {name:path for name,path in frozen_files.items() if '/TomSceneEffects/' not in name}
    prototype = {name:path for name,path in frozen_files.items() if '/TomSceneEffects/' in name}
    if sum(name.endswith('.dll') for name in base) != 8 or sum(name.endswith('.dll') for name in prototype) != 2:
        raise RuntimeError('Wrong base/prototype DLL inventory')
    for files in [base,prototype]:
        files.update(licenses)
        files['INSTALL.md'] = REPO/'INSTALL.md'
        files['CHANGELOG.md'] = REPO/'CHANGELOG.md'
        files['docs/Live-Regression.md'] = REPO/'docs/Live-Regression.md'
    authoring = dict(licenses)
    for item in read(args.authoring_export/'SOURCE-INVENTORY.json')['files']:
        path = args.authoring_export/item['path']
        if sha(path) != item['sha256']:
            raise RuntimeError('Authoring source/metadata changed: ' + item['path'])
        authoring[item['path']] = path
    authoring['README.md'] = REPO/'authoring/README.md'
    receipts = []
    for filename,files,kind in [
        ('TomRenderInfrastructure-KKS-0.1.0.zip',base,'base-eight-DLLs'),
        ('TomRenderInfrastructure-KKS-SceneEffects-Prototype-0.1.0.zip',prototype,'optional-two-DLL-prototype'),
        ('TomRenderInfrastructure-KKS-Authoring-0.1.0.zip',authoring,'Unity-authoring-not-game-install')]:
        receipts.append({'project':'TomRenderInfrastructure',**package(args.output/'TomRenderInfrastructure'/filename,files,{**common,'kind':kind,'version':'0.1.0'})})
    for name,root,zip_name,version,tag in [
        ('TomshadersX',args.tomx_root,'TomShadersX-KKS-0.4.0.zipmod','0.4.0','v0.4.0-infra.1'),
        ('TomBSDF',args.bsdf_root,'TomBSDF-KKS-0.2.0-RC1.zipmod','0.2.0 RC1','v0.2.0-rc1-infra.1')]:
        files = {'mods/'+zip_name:shader_files[zip_name], 'LICENSE':root/'LICENSE'}
        if name == 'TomshadersX':
            for relative in ['Material/SkinDetailNeutral.png','Material/SkinControlNeutral.png','THIRD_PARTY_NOTICES.md','LICENSES/MIT-Upstream.txt',
                             'Documents/ReleaseNotes.en.md','Documents/ReleaseNotes.zh-CN.md','Documents/TomShadersX-UserManual.en.md','Documents/TomShadersX-UserManual.zh-CN.md']:
                files[relative] = root/relative
            files['INSTALL.md'] = (f'# TomX {version} Infra1\n\nInstall [TomRenderInfrastructure 0.1.0](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/releases/tag/v0.1.0) first. Then merge only mods into the KKS game root. This shader archive contains no DLLs or Probe/helper Bundles. Keep the neutral Skin textures for explicit ME import into genuinely empty slots; do not replace existing game maps.\n').encode('utf-8')
        else:
            for relative in ['Assets/Mods/TomShadersBSDF/LICENSE','Assets/Mods/TomShadersBSDF/ThirdParty/LTC-LICENSE.txt',
                             'docs/TomBSDF-UserManual.en.md']:
                files[relative] = root/relative
            files['INSTALL.md'] = (
                '# TomBSDF 0.2.0 RC1 Infra1\n\n'
                'Install [TomRenderInfrastructure KKS 0.1.0](https://github.com/tomTom1010-IEE/TomRenderInfrastructure/releases/tag/v0.1.0) first. '
                'Close KKS/Studio, back up old files outside active folders, then merge only this archive\'s mods folder into the game root. '
                'This shader pack contains no runtime DLLs or Probe resources; keep one shared base and remove old duplicate providers. '
                'Do not unpack zipmods over abdata or install authoring materials/prefabs as game files.\n\n'
                '[Offline manual](docs/TomBSDF-UserManual.en.md) | '
                '[Full installation guide](https://github.com/tomTom1010-IEE/TomBSDF/blob/main/Assets/Mods/TomShadersBSDF/Documents/Installation.md)\n'
            ).encode('utf-8')
        filename = ('TomShadersX-KKS-0.4.0-Infra1.zip' if name == 'TomshadersX' else 'TomBSDF-KKS-0.2.0-RC1-Infra1.zip')
        receipts.append({'project':name,**package(args.output/name/filename,files,{**common,'kind':'shader-pack-no-helpers','version':version,'tag':tag},True)})
        target = args.output/name/zip_name
        shutil.copy2(shader_files[zip_name],target)
        if sha(target) != sha(shader_files[zip_name]):
            raise RuntimeError('Standalone copy mismatch')
        receipts.append({'project':name,'name':target.name,'bytes':target.stat().st_size,'sha256':sha(target)})
    for project in ['TomRenderInfrastructure','TomshadersX','TomBSDF']:
        entries = [entry for entry in receipts if entry['project'] == project]
        (args.output/project/'SHA256SUMS.txt').write_text(''.join(f"{entry['sha256']}  {entry['name']}\n" for entry in sorted(entries,key=lambda x:x['name'])),encoding='ascii')
    report = {'passed':True,'sources':sources,'artifacts':receipts,'shaderPacksEmbedDLLs':False,'bundlesRebuilt':False}
    (args.output/'package-audit.json').write_bytes(json_bytes(report))
    print(json.dumps(report,indent=2))


if __name__ == '__main__':
    main()
