from copy import deepcopy
from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-029-l2.json'
APP_FILES = ROOT / 'src' / 'HerdDesk.App' / 'Files'
PASS_KEYS = ('ac31_passed', 'ac33_passed', 'g0_passed')


class Hd029ResidualTests(unittest.TestCase):
    def test_l2_live_ui_ssh_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd029_l2_status')
        self.assertEqual(doc['l2_live_ui'], 'UNVERIFIED')
        self.assertEqual(doc['l2_live_ssh'], 'UNVERIFIED')
        self.assertFalse(doc['ac31_passed'])
        self.assertFalse(doc['ac33_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_windows'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['live_ui'])
        self.assertTrue(doc['missing']['winui_xaml'])
        self.assertTrue(doc['missing']['live_ssh'])
        self.assertTrue(doc['missing']['live_ui'])
        self.assertTrue(doc['missing']['integration_windows'])
        self.assertTrue((APP_FILES / 'FileWorkspaceViewModel.cs').is_file())
        self.assertTrue((APP_FILES / 'FilePaneViewModel.cs').is_file())
        repository.check_integration_windows_layout(ROOT)
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        self.assertEqual(list(APP_FILES.glob('*.xaml')), [])
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])

    def test_closeout_rejects_pass_claims(self):
        hd029 = json.loads(L2.read_text(encoding='utf-8'))
        repository._check_hd029(hd029)

        for key in PASS_KEYS:
            bad = deepcopy(hd029)
            bad[key] = True
            with self.assertRaises(AssertionError):
                repository._check_hd029(bad)

        bad = deepcopy(hd029)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd029(bad)

        bad = deepcopy(hd029)
        bad['l2_live_ui'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd029(bad)

        bad = deepcopy(hd029)
        bad['l2_live_ssh'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd029(bad)

        bad = deepcopy(hd029)
        bad['winui_admitted'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd029(bad)

        bad = deepcopy(hd029)
        bad['integration_windows'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd029(bad)

        bad = deepcopy(hd029)
        bad['live_ssh'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd029(bad)

        bad = deepcopy(hd029)
        bad['live_ui'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd029(bad)


if __name__ == '__main__':
    unittest.main()
