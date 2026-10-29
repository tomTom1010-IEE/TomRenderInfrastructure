import hashlib
from pathlib import Path
import tempfile
import unittest

from package_release import json_bytes, read
from package_registry_release import payload_map, safe_path, verify_authoring, verify_evidence


LOCK = Path(__file__).resolve().parents[1] / 'distribution/0.1.1.json'


class RegistryPackageTests(unittest.TestCase):
    def test_release_inventory(self):
        rows = payload_map(read(LOCK))
        self.assertEqual(12, len(rows))
        self.assertEqual(8, sum(name.endswith('.dll') and '/TomSceneEffects/' not in name for name in rows))

    def test_duplicate_payload_rejected(self):
        lock = read(LOCK)
        lock['payload'].append(dict(lock['payload'][0]))
        with self.assertRaises(RuntimeError):
            payload_map(lock)

    def test_missing_core_rejected(self):
        lock = read(LOCK)
        lock['payload'] = [x for x in lock['payload'] if not x['path'].endswith('/TomRender.Core.dll')]
        with self.assertRaises(RuntimeError):
            payload_map(lock)

    def test_paths(self):
        for path in ['', '../outside', '/outside', 'C:/outside', r'dir\outside', 'a/../b', 'a//b']:
            with self.subTest(path=path), self.assertRaises(RuntimeError):
                safe_path(path)
        self.assertEqual('Assets/A.dll.meta', safe_path('Assets/A.dll.meta'))

    def test_replaced_qualification_receipt_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'freeze.json').write_bytes(b'{}')
            with self.assertRaises(RuntimeError):
                verify_evidence(read(LOCK), root)

    def test_failed_qualification_rejected_even_with_matching_hashes(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            lock = read(LOCK)
            for name, filename in {'freeze': 'freeze.json', 'acceptance': 'acceptance.json',
                                   'evidenceAudit': 'evidence-audit.json', 'cleanupAudit': 'cleanup-audit.json'}.items():
                data = json_bytes({'passed': False})
                (root / filename).write_bytes(data)
                lock['qualification'][name + 'Sha256'] = hashlib.sha256(data).hexdigest()
            with self.assertRaisesRegex(RuntimeError, 'Failed qualification'):
                verify_evidence(lock, root)

    def test_stale_authoring_inventory_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            export, canonical = root / 'export', root / 'canonical'
            export.mkdir()
            canonical.mkdir()
            (export / 'SOURCE-INVENTORY.json').write_bytes(json_bytes({'files': []}))
            (canonical / 'SOURCE-INVENTORY.json').write_bytes(json_bytes({'files': [{'path': 'different'}]}))
            with self.assertRaisesRegex(RuntimeError, 'reviewed source inventory'):
                verify_authoring(export, read(LOCK), canonical)

    def test_missing_authoring_runtime_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            export, canonical = root / 'export', root / 'canonical'
            export.mkdir()
            canonical.mkdir()
            for path in [export, canonical]:
                (path / 'SOURCE-INVENTORY.json').write_bytes(json_bytes({'files': []}))
            with self.assertRaisesRegex(RuntimeError, 'Missing Core/Probe'):
                verify_authoring(export, read(LOCK), canonical)


if __name__ == '__main__':
    unittest.main()
