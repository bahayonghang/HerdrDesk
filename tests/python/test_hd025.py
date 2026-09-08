from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-025-l2.json'


class Hd025ResidualTests(unittest.TestCase):
    def test_l2_live_ssh_perf_stays_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd025_l2_status')
        self.assertEqual(doc['l2_live_ssh_perf'], 'UNVERIFIED')
        self.assertEqual(doc['l2_live_ssh'], 'UNVERIFIED')
        self.assertNotEqual(doc['l2_live_ssh_perf'], 'passed')
        self.assertFalse(doc['ac27_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['b_ssh_measured'])
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertTrue(doc['missing']['measured_b_ssh'])
        self.assertTrue(doc['missing']['live_ssh_matrix'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        self.assertFalse((ROOT / 'tests' / 'Integration.Windows').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
