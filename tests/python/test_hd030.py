from copy import deepcopy
from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-030-l2.json'
CORE_ATTACH = ROOT / 'src' / 'HerdDesk.Core' / 'Attachments'
APP_VM = ROOT / 'src' / 'HerdDesk.App' / 'ViewModels' / 'AttachToAgentViewModel.cs'
PASS_KEYS = ('ac35_passed', 'ac31_passed', 'ac32_passed', 'ac36_passed', 'g0_passed')


class Hd030ResidualTests(unittest.TestCase):
    def test_l2_live_agent_ime_ssh_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd030_l2_status')
        self.assertEqual(doc['l2_live_agent'], 'UNVERIFIED')
        self.assertEqual(doc['l2_live_ime'], 'UNVERIFIED')
        self.assertEqual(doc['l2_live_ssh'], 'UNVERIFIED')
        self.assertFalse(doc['ac35_passed'])
        self.assertFalse(doc['ac31_passed'])
        self.assertFalse(doc['ac32_passed'])
        self.assertFalse(doc['ac36_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_windows'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['live_agent'])
        self.assertFalse(doc['live_ime'])
        self.assertFalse(doc['auto_submit'])
        self.assertTrue(doc['missing']['winui_xaml'])
        self.assertTrue(doc['missing']['live_agent'])
        self.assertTrue(doc['missing']['live_ime'])
        self.assertTrue(doc['missing']['live_ssh'])
        self.assertTrue(doc['missing']['integration_windows'])
        self.assertTrue((CORE_ATTACH / 'AttachmentCoordinator.cs').is_file())
        self.assertTrue((CORE_ATTACH / 'AttachmentCapabilityCatalog.cs').is_file())
        self.assertTrue(APP_VM.is_file())
        self.assertFalse((ROOT / 'tests' / 'Integration.Windows').exists())
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        self.assertEqual(list((ROOT / 'src' / 'HerdDesk.App').rglob('*.xaml')), [])
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])

    def test_closeout_rejects_pass_claims(self):
        hd030 = json.loads(L2.read_text(encoding='utf-8'))
        repository._check_hd030(hd030)

        for key in PASS_KEYS:
            bad = deepcopy(hd030)
            bad[key] = True
            with self.assertRaises(AssertionError):
                repository._check_hd030(bad)

        bad = deepcopy(hd030)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd030(bad)

        bad = deepcopy(hd030)
        bad['l2_live_agent'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd030(bad)

        bad = deepcopy(hd030)
        bad['l2_live_ime'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd030(bad)

        bad = deepcopy(hd030)
        bad['l2_live_ssh'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd030(bad)

        bad = deepcopy(hd030)
        bad['winui_admitted'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd030(bad)

        bad = deepcopy(hd030)
        bad['integration_windows'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd030(bad)

        bad = deepcopy(hd030)
        bad['auto_submit'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd030(bad)

        bad = deepcopy(hd030)
        bad['live_ssh'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd030(bad)


if __name__ == '__main__':
    unittest.main()
