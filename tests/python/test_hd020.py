from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-020-l2.json'


class Hd020ResidualTests(unittest.TestCase):
    def test_l2_isolated_openssh_stays_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd020_l2_status')
        self.assertEqual(doc['l2_isolated_windows_openssh'], 'UNVERIFIED')
        self.assertFalse(doc['ac22_passed'])
        self.assertFalse(doc['ac23_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['herdr_machine_catalog'])
        self.assertFalse(doc['endpoint_generation_1'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertTrue(doc['missing']['isolated_windows_openssh'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
