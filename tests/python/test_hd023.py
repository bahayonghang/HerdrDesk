from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-023-l2.json'


class Hd023ResidualTests(unittest.TestCase):
    def test_l2_live_three_device_p95_stays_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd023_l2_status')
        self.assertEqual(doc['l2_live_three_device_search_p95'], 'UNVERIFIED')
        self.assertFalse(doc['ac19_passed'])
        self.assertFalse(doc['ac21_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_three_device_ssh'])
        self.assertFalse(doc['live_three_device_p95'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertTrue(doc['missing']['live_three_device_ssh'])
        self.assertTrue(doc['missing']['live_search_p95'])
        repository.check_integration_windows_layout(ROOT)
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
