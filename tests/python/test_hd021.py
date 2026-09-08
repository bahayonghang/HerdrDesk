from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-021-l2.json'


class Hd021ResidualTests(unittest.TestCase):
    def test_l2_live_helper_deploy_stays_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd021_l2_status')
        self.assertEqual(doc['l2_live_helper_deploy'], 'UNVERIFIED')
        self.assertFalse(doc['ac25_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['live_remote_install'])
        self.assertFalse(doc['sudo'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertTrue(doc['missing']['isolated_linux_account'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        for status in doc['targets'].values():
            self.assertIn(status, ('not_run', 'UNVERIFIED'))
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
