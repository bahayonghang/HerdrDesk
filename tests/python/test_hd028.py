from copy import deepcopy
from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-028-l2.json'
PACKAGES = ROOT / 'implementation' / 'hd-028-packages.json'
ADR = ROOT / 'docs' / 'adr' / '0008-filebridge-protocol-v1.md'


class Hd028ResidualTests(unittest.TestCase):
    def test_l2_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd028_l2_status')
        self.assertEqual(doc['l2_filesystem'], 'UNVERIFIED')
        self.assertEqual(doc['l2_ssh'], 'UNVERIFIED')
        self.assertEqual(doc['l2_toctou'], 'UNVERIFIED')
        self.assertFalse(doc['ac30_passed'])
        self.assertFalse(doc['ac31_passed'])
        self.assertFalse(doc['ac32_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        repository.check_integration_windows_layout(ROOT)
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])

    def test_serve_binary_and_zero_crates(self):
        self.assertTrue((ROOT / 'filebridge' / 'src' / 'main.rs').is_file())
        cargo = (ROOT / 'filebridge' / 'Cargo.toml').read_text(encoding='utf-8')
        self.assertIn('[[bin]]', cargo)
        self.assertIn('herddesk-filebridge', cargo)
        packages = json.loads(PACKAGES.read_text(encoding='utf-8'))
        sha2 = next(item for item in packages['crates'] if item['id'] == 'sha2')
        self.assertEqual(sha2['requested'], '0.10.8')
        self.assertEqual(sha2['lock_version'], '0.10.8')
        self.assertEqual(packages['rust']['channel'], '1.98.0')
        adr = ADR.read_text(encoding='utf-8')
        self.assertIn('**accepted**', adr)
        self.assertIn('wire only', adr.lower())
        lock = (ROOT / 'filebridge' / 'Cargo.lock').read_text(encoding='utf-8')
        self.assertIn('name = "sha2"', lock)
        self.assertIn('version = "0.10.8"', lock)

    def test_closeout_rejects_pass_claims(self):
        hd028 = json.loads(L2.read_text(encoding='utf-8'))
        packages = json.loads(PACKAGES.read_text(encoding='utf-8'))
        repository._check_hd028(hd028, packages)

        for key in ('ac30_passed', 'ac31_passed', 'ac32_passed', 'g0_passed'):
            bad = deepcopy(hd028)
            bad[key] = True
            with self.assertRaises(AssertionError):
                repository._check_hd028(bad, packages)

        bad = deepcopy(hd028)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd028(bad, packages)

        bad = deepcopy(hd028)
        bad['l2_filesystem'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd028(bad, packages)

        bad = deepcopy(hd028)
        bad['live_ssh'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd028(bad, packages)

        bad = deepcopy(packages)
        bad['crates'] = []
        with self.assertRaises(AssertionError):
            repository._check_hd028(hd028, bad)

        bad = deepcopy(packages)
        bad['crates'] = [{'id': 'sha2', 'requested': '0.10.0', 'lock_version': '0.10.0'}]
        with self.assertRaises(AssertionError):
            repository._check_hd028(hd028, bad)


if __name__ == '__main__':
    unittest.main()
