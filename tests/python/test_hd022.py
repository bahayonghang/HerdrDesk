from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-022-l2.json'


class Hd022ResidualTests(unittest.TestCase):
    def test_l2_live_ssh_stays_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd022_l2_status')
        self.assertEqual(doc['l2_live_ssh'], 'UNVERIFIED')
        self.assertFalse(doc['ac24_passed'])
        self.assertFalse(doc['ac26_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['herdr_machine_catalog'])
        self.assertFalse(doc['endpoint_generation_1'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertFalse(doc['tt_forced'])
        self.assertTrue(doc['missing']['isolated_linux_herdr'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
