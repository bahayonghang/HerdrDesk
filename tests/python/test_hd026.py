from copy import deepcopy
from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-026-l2.json'
CATALOG = ROOT / 'evidence' / 'multi-device-mvp' / 'catalog.json'
MATRIX = ROOT / 'evidence' / 'multi-device-mvp' / 'support-matrix.json'
REQUIRED_CARDS = (
    'ac21-identity-collision',
    'ac22-ac23-ssh-identity-config',
    'ac24-transparent-stream',
    'ac13-recovery',
    'ac14-no-replay',
    'ac15-ownership',
    'ac26-isolation-retry',
    'ac19-search',
    'budget-platform',
)
REQUIRED_LIVE = ('live-ssh', 'live-winui', 'live-three-device', 'live-crash')
REQUIRED_ACS = {
    'AC13', 'AC14', 'AC15', 'AC19', 'AC21', 'AC22', 'AC23', 'AC24', 'AC26',
}
CARD_OWNERS = {
    'ac21-identity-collision': ['HD-023'],
    'ac22-ac23-ssh-identity-config': ['HD-020', 'HD-024'],
    'ac24-transparent-stream': ['HD-022'],
    'ac13-recovery': ['HD-018', 'HD-019', 'HD-022', 'HD-024'],
    'ac14-no-replay': ['HD-016', 'HD-018', 'HD-022'],
    'ac15-ownership': ['HD-013', 'HD-018', 'HD-019', 'HD-022'],
    'ac26-isolation-retry': ['HD-024'],
    'ac19-search': ['HD-011', 'HD-023'],
    'budget-platform': ['HD-025', 'HD-026'],
}
CARD_ACS = {
    'ac21-identity-collision': ['AC21'],
    'ac22-ac23-ssh-identity-config': ['AC22', 'AC23'],
    'ac24-transparent-stream': ['AC24'],
    'ac13-recovery': ['AC13'],
    'ac14-no-replay': ['AC14'],
    'ac15-ownership': ['AC15'],
    'ac26-isolation-retry': ['AC26'],
    'ac19-search': ['AC19'],
    'budget-platform': ['AC27'],
}
LIVE_GRANTS = {
    'live-ssh': 'no_authorized_isolated_windows_openssh_lab',
    'live-winui': 'no_winui_admission',
    'live-three-device': 'no_authorized_third_device_id',
    'live-crash': 'no_authorized_supervised_gui_crash',
}
AC_FLAGS = (
    'ac13_passed', 'ac14_passed', 'ac15_passed', 'ac19_passed',
    'ac21_passed', 'ac22_passed', 'ac23_passed', 'ac24_passed',
    'ac26_passed', 'ac27_passed',
)
SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})


def _load():
    return (
        json.loads(L2.read_text(encoding='utf-8')),
        json.loads(CATALOG.read_text(encoding='utf-8')),
        json.loads(MATRIX.read_text(encoding='utf-8')),
    )


class Hd026ResidualTests(unittest.TestCase):
    def test_l2_live_ssh_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd026_l2_status')
        self.assertEqual(doc['l2_live_ssh'], 'UNVERIFIED')
        self.assertNotEqual(doc['l2_live_ssh'], 'passed')
        for key in AC_FLAGS:
            self.assertFalse(doc[key], key)
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['live_winui'])
        self.assertFalse(doc['live_three_device'])
        self.assertFalse(doc['live_crash'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['third_device_id_authorized'])
        self.assertTrue(doc['two_sessions_on_one_host_are_not_three_devices'])
        self.assertFalse(doc['b_ssh_measured'])
        self.assertFalse(doc['copy_windows_fields_onto_linux'])
        self.assertFalse(doc['extrapolate_macos_arm64'])
        self.assertFalse(doc['herdr_executed'])
        missing = doc['missing']
        self.assertTrue(missing['isolated_windows_openssh'])
        self.assertTrue(missing['isolated_linux_herdr'])
        self.assertTrue(missing['authorized_third_device'])
        self.assertTrue(missing['live_search_p95'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        self.assertFalse((ROOT / 'tests' / 'Integration.Windows').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])

    def test_catalog_execution_cards_stay_not_run(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(catalog['document_kind'], 'hd026_multi_device_mvp_catalog')
        self.assertTrue(catalog['simulation'])
        self.assertFalse(catalog['template'])
        self.assertEqual(catalog['l2_live_ssh'], 'UNVERIFIED')
        for key in AC_FLAGS:
            self.assertFalse(catalog[key], key)
        self.assertFalse(catalog['g0_passed'])
        self.assertNotEqual(catalog['phase_gate'], 'passed')
        self.assertFalse(catalog['live_ssh'])
        self.assertFalse(catalog['winui_admitted'])
        self.assertFalse(catalog['third_device_id_authorized'])
        self.assertTrue(catalog['two_sessions_on_one_host_are_not_three_devices'])
        self.assertEqual(catalog['redaction']['credential'], 'omitted')
        self.assertEqual(catalog['redaction']['terminal_body'], 'omitted')
        self.assertEqual(catalog['redaction']['host'], 'omitted')
        cards = {item['id']: item for item in catalog['execution_cards']}
        self.assertEqual(tuple(cards), REQUIRED_CARDS)
        seen = set()
        for card_id, card in cards.items():
            self.assertEqual(card['owner_children'], CARD_OWNERS[card_id], card_id)
            self.assertEqual(card['ac_ids'], CARD_ACS[card_id], card_id)
            seen.update(card['ac_ids'])
        self.assertTrue(REQUIRED_ACS <= seen)
        self.assertEqual(cards['ac19-search']['missing_grant'],
                         'no_authorized_third_device_id')
        for card in catalog['execution_cards']:
            self.assertEqual(card['live_status'], 'UNVERIFIED')
            self.assertEqual(card['live_result'], 'not_run')
            self.assertNotIn(str(card['live_result']).lower(), SUCCESS)
            self.assertTrue(card['missing_grant'])
            self.assertEqual(card['l1_status'], 'shipped')
            self.assertTrue(card['l1_artifacts'])
            for rel in card['l1_artifacts']:
                self.assertTrue((ROOT / rel).is_file(), rel)
            capture = json.loads((ROOT / card['live_capture']).read_text(encoding='utf-8'))
            self.assertIs(capture['template'], False)
            self.assertEqual(capture['result'], 'not_run')
        rows = {item['id']: item for item in catalog['live_rows']}
        self.assertEqual(tuple(rows), REQUIRED_LIVE)
        for row in catalog['live_rows']:
            self.assertEqual(row['status'], 'UNVERIFIED')
            self.assertEqual(row['result'], 'not_run')
            self.assertIs(row['template'], False)
            self.assertTrue(row['owner_children'])
            self.assertEqual(row['missing_grant'], LIVE_GRANTS[row['id']])
            evidence = json.loads((ROOT / row['evidence_path']).read_text(encoding='utf-8'))
            self.assertIs(evidence['template'], False)
            self.assertEqual(evidence['result'], 'not_run')

    def test_templates_are_not_successful_runs(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        for rel in catalog['templates']:
            doc = json.loads((ROOT / rel).read_text(encoding='utf-8'))
            self.assertTrue(doc['template'])
            self.assertEqual(doc['document_kind'], 'template')
            self.assertIsNone(doc['captured_at_utc'])
            self.assertIsNone(doc['exit_code'])
            self.assertIsNone(doc['stdout_sha256'])
            self.assertIsNone(doc['stderr_sha256'])
            self.assertNotIn(
                (doc.get('result') or '').lower() if isinstance(doc.get('result'), str)
                else doc.get('result'),
                SUCCESS,
            )
            self.assertFalse(doc['herdr_executed'])
            self.assertNotIn('stdout', doc)
            self.assertNotIn('stderr', doc)
        for rel in catalog['not_run_captures']:
            doc = json.loads((ROOT / rel).read_text(encoding='utf-8'))
            self.assertFalse(doc['template'])
            self.assertEqual(doc['result'], 'not_run')
            self.assertEqual(doc['evidence_level'], 'not_run')
            self.assertFalse(doc['herdr_executed'])
            self.assertIsNone(doc['host_fingerprint_redacted'])
            self.assertIsNone(doc['command_redacted'])
            self.assertNotIn('password', json.dumps(doc))
            self.assertNotIn('stdout', doc)
            self.assertNotIn('stderr', doc)

    def test_support_matrix_promised_rows_stay_not_run(self):
        matrix = json.loads(MATRIX.read_text(encoding='utf-8'))
        self.assertEqual(matrix['document_kind'], 'hd026_support_matrix')
        self.assertFalse(matrix['copy_windows_fields_onto_linux'])
        self.assertFalse(matrix['extrapolate_macos_arm64'])
        self.assertFalse(matrix['third_device_id_authorized'])
        self.assertEqual(matrix['compatible_by_default'], [])
        for key in AC_FLAGS:
            self.assertFalse(matrix[key], key)
        platforms = {item['id']: item for item in matrix['platforms']}
        for key in ('windows-11-x64-client', 'linux-x64-remote'):
            row = platforms[key]
            self.assertEqual(row['promise'], 'promised')
            self.assertEqual(row['live_status'], 'not_run')
            self.assertEqual(row['live_result'], 'not_run')
            self.assertFalse(row['compatible'])
            self.assertEqual(row['support'], 'promised_not_run')
        self.assertEqual(platforms['macos-x64']['support'], 'unsupported')
        self.assertEqual(platforms['macos-arm64']['support'], 'experimental')
        self.assertEqual(platforms['linux-arm64']['support'], 'experimental')
        self.assertEqual(platforms['windows-arm64']['support'], 'experimental')
        for row in platforms.values():
            self.assertEqual(row['live_status'], 'not_run')
            self.assertFalse(row['compatible'])
            self.assertNotIn(row['support'], ('supported', 'stable', 'passed'))
        for combo in matrix['auth_combos']:
            self.assertEqual(combo['live_status'], 'not_run')
            self.assertNotIn(combo['live_result'], SUCCESS)
        for cell in matrix['cells']:
            self.assertEqual(cell['live_status'], 'not_run')
            self.assertFalse(cell['compatible'])
        linux = platforms['linux-x64-remote']
        windows = platforms['windows-11-x64-client']
        self.assertNotEqual(linux['missing_grant'], windows['missing_grant'])

    def test_closeout_rejects_pass_claims_and_grant_gaps(self):
        hd026, mvp, matrix = _load()
        repository._check_hd026_closeout(hd026, mvp, matrix)

        bad = deepcopy(hd026)
        bad['ac13_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(bad, mvp, matrix)

        bad = deepcopy(hd026)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(bad, mvp, matrix)

        bad = deepcopy(hd026)
        bad['l2_live_ssh'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(bad, mvp, matrix)

        bad = deepcopy(hd026)
        bad['g0_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(bad, mvp, matrix)

        bad = deepcopy(mvp)
        bad['execution_cards'][0]['live_result'] = 'Passed'
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(hd026, bad, matrix)

        bad = deepcopy(mvp)
        bad['execution_cards'][0]['missing_grant'] = ''
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(hd026, bad, matrix)

        bad = deepcopy(mvp)
        bad['live_rows'][2]['result'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(hd026, bad, matrix)

        bad = deepcopy(mvp)
        bad['third_device_id_authorized'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(hd026, bad, matrix)

        bad = deepcopy(mvp)
        bad['two_sessions_on_one_host_are_not_three_devices'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(hd026, bad, matrix)

        bad = deepcopy(matrix)
        bad['platforms'][0]['support'] = 'supported'
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(hd026, mvp, bad)

        bad = deepcopy(matrix)
        bad['copy_windows_fields_onto_linux'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(hd026, mvp, bad)

        bad = deepcopy(matrix)
        bad['ac19_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd026_closeout(hd026, mvp, bad)


if __name__ == '__main__':
    unittest.main()
