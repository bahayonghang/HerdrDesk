from copy import deepcopy
from pathlib import Path
import hashlib
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-027-l2.json'
PACKAGES = ROOT / 'implementation' / 'hd-027-packages.json'
ADR = ROOT / 'docs' / 'adr' / '0008-filebridge-protocol-v1.md'
VECTORS = ROOT / 'filebridge' / 'spec' / 'test-vectors'
REQUIRED = (
    'valid-list.hdfb',
    'valid-empty-write.hdfb',
    'invalid-oversize-json.hdfb',
    'invalid-sequence-gap.hdfb',
    'invalid-unknown-kind.hdfb',
)


class Hd027ResidualTests(unittest.TestCase):
    def test_l2_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd027_l2_status')
        self.assertEqual(doc['l2_filesystem'], 'UNVERIFIED')
        self.assertEqual(doc['l2_ssh'], 'UNVERIFIED')
        self.assertEqual(doc['l2_toctou'], 'UNVERIFIED')
        self.assertFalse(doc['ac30_passed'])
        self.assertFalse(doc['ac34_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_file_ops'])
        self.assertFalse(doc['binary_implemented'])
        self.assertFalse(doc['main_rs'])
        self.assertEqual(doc['adr_status'], 'proposed')
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        self.assertFalse((ROOT / 'tests' / 'Integration.Windows').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])

    def test_no_binary_and_adr_stays_proposed(self):
        self.assertFalse((ROOT / 'filebridge' / 'src' / 'main.rs').exists())
        cargo = (ROOT / 'filebridge' / 'Cargo.toml').read_text(encoding='utf-8')
        self.assertNotIn('[[bin]]', cargo)
        adr = ADR.read_text(encoding='utf-8')
        self.assertIn('proposed', adr.lower())
        self.assertNotIn('status: accepted', adr.lower())
        self.assertIn('**proposed**', adr)
        packages = json.loads(PACKAGES.read_text(encoding='utf-8'))
        self.assertEqual(packages['crates'], [])
        self.assertEqual(packages['rust']['channel'], '1.98.0')

    def test_golden_vector_files_match_manifest_hashes(self):
        manifest = json.loads((VECTORS / 'manifest.json').read_text(encoding='utf-8'))
        names = {item['file'] for item in manifest['vectors']}
        for name in REQUIRED:
            self.assertIn(name, names)
        for item in manifest['vectors']:
            data = (VECTORS / item['file']).read_bytes()
            digest = hashlib.sha256(data).hexdigest()
            self.assertEqual(digest, item['sha256'], item['id'])

    def test_codecs_do_not_import_build_vectors(self):
        builder = (VECTORS / 'build_vectors.py').read_text(encoding='utf-8')
        self.assertIn('Tests must not import this file', builder)
        rust = (ROOT / 'filebridge' / 'tests' / 'protocol_vectors.rs').read_text(encoding='utf-8')
        csharp = (ROOT / 'tests' / 'Contract' / 'Files' / 'FileBridgeProtocolVectorTests.cs').read_text(
            encoding='utf-8'
        )
        self.assertNotIn('build_vectors', rust)
        self.assertNotIn('build_vectors', csharp)
        self.assertIn('spec/test-vectors', rust)
        self.assertIn('spec', csharp)
        self.assertIn('test-vectors', csharp)

    def test_closeout_rejects_pass_claims(self):
        hd027 = json.loads(L2.read_text(encoding='utf-8'))
        packages = json.loads(PACKAGES.read_text(encoding='utf-8'))
        repository._check_hd027(hd027, packages)

        bad = deepcopy(hd027)
        bad['ac30_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd027(bad, packages)

        bad = deepcopy(hd027)
        bad['ac34_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd027(bad, packages)

        bad = deepcopy(hd027)
        bad['g0_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd027(bad, packages)

        bad = deepcopy(hd027)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd027(bad, packages)

        bad = deepcopy(hd027)
        bad['l2_filesystem'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd027(bad, packages)

        bad = deepcopy(hd027)
        bad['binary_implemented'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd027(bad, packages)

        bad = deepcopy(packages)
        bad['crates'] = [{'id': 'serde'}]
        with self.assertRaises(AssertionError):
            repository._check_hd027(hd027, bad)


if __name__ == '__main__':
    unittest.main()
