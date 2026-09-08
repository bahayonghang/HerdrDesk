from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-024-l2.json'


class Hd024ResidualTests(unittest.TestCase):
    def test_l2_live_auth_stays_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd024_l2_status')
        self.assertEqual(doc['l2_live_auth'], 'UNVERIFIED')
        self.assertFalse(doc['ac22_passed'])
        self.assertFalse(doc['ac26_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertTrue(doc['missing']['live_auth_deny'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
