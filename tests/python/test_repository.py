from pathlib import Path
import hashlib
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository


class RepositoryPortabilityTests(unittest.TestCase):
    def test_repository_structure(self):
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['windows_verified'])
        self.assertFalse(result['ac02_passed'])
        self.assertFalse(result['ac03_passed'])
        self.assertFalse(result['ac05_passed'])
        self.assertFalse(result['ac08_passed'])
        self.assertFalse(result['ac09_passed'])
        self.assertFalse(result['ac44_passed'])
        self.assertFalse(result['g0_passed'])
        self.assertEqual(result['project_graph'], 'passed')
        self.assertEqual(result['github_required_check'], 'UNVERIFIED')
        self.assertEqual(result['windows_desktop_restore'], 'not_admitted')
        self.assertEqual(result['endpoint_validation'], 'passed')
        self.assertEqual(result['lease_validation'], 'passed')
        self.assertEqual(result['renderer_validation'], 'passed')
        self.assertEqual(result['adr_validation'], 'passed')

    def test_all_metadata_reads_use_explicit_utf8(self):
        original = Path.read_text

        def read_explicit(path, *args, **kwargs):
            encoding = kwargs.get('encoding', args[0] if args else None)
            self.assertEqual(encoding, 'utf-8', 'Metadata must not depend on Windows locale')
            return original(path, *args, **kwargs)

        with patch.object(Path, 'read_text', read_explicit):
            repository.validate()

    def test_fixture_bytes_are_stable_after_checkout(self):
        data = (ROOT / 'tests/fixtures/terminal-valid.ndjson').read_bytes()
        self.assertNotIn(b'\r\n', data)
        self.assertEqual(hashlib.sha256(data).hexdigest(),
                         'd206d2ad30aac1814193b2f0423bbf30405d113e6d172adb2a9f5bed34f6f599')


if __name__ == '__main__':
    unittest.main()
