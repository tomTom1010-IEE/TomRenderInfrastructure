import hashlib
from pathlib import Path
import tempfile
import unittest
import zipfile

from package_release import package, verify_zipmod


class PackageTests(unittest.TestCase):
    def test_exact_payload_and_checksums(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'release.zip'
            receipt = package(path, {'mods/test.zipmod':b'original bytes'}, {'schema':1}, True)
            self.assertEqual(receipt['sha256'], hashlib.sha256(path.read_bytes()).hexdigest())
            with zipfile.ZipFile(path) as archive:
                self.assertEqual(archive.read('mods/test.zipmod'), b'original bytes')
                self.assertIn(b'mods/test.zipmod', archive.read('SHA256SUMS.txt'))
                self.assertIsNone(archive.testzip())

    def test_shader_pack_rejects_helpers(self):
        with tempfile.TemporaryDirectory() as directory:
            for name in ['BepInEx/plugins/a.txt','bepinex/plugins/a.txt','helper.dll',
                         'HELPER.DLL','hairx-feather.unity3d','mods/Tom Lighting Probes.zipmod']:
                with self.subTest(name=name), self.assertRaises(RuntimeError):
                    package(Path(directory)/'bad.zip', {name:b'bad'}, {}, True)

    def test_unsafe_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            for name in ['../escape','C:/escape','/escape',r'dir\escape']:
                with self.subTest(name=name), self.assertRaises(RuntimeError):
                    package(Path(directory)/'bad.zip', {name:b'bad'}, {})

    def test_case_collision(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(RuntimeError):
                package(Path(directory)/'bad.zip', {'License':b'a','LICENSE':b'b'}, {})

    def test_existing_archive_not_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'release.zip'
            path.write_bytes(b'preserved')
            with self.assertRaises(FileExistsError):
                package(path, {'a':b'b'}, {})
            self.assertEqual(path.read_bytes(), b'preserved')

    def test_zipmod_identity_and_bundle(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'test.zipmod'
            with zipfile.ZipFile(path,'w') as archive:
                archive.writestr('manifest.xml','<manifest><guid>test</guid><version>1</version><game>Koikatsu Sunshine</game></manifest>')
                archive.writestr('abdata/test.unity3d',b'unchanged')
            self.assertEqual(verify_zipmod(path,'test','1','test.unity3d'),hashlib.sha256(b'unchanged').hexdigest())
            with self.assertRaises(RuntimeError):
                verify_zipmod(path,'wrong','1','test.unity3d')


if __name__ == '__main__':
    unittest.main()
