from copy import deepcopy
from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-032-l2.json'
CATALOG = ROOT / 'evidence' / 'files' / 'catalog.json'
MATRIX = ROOT / 'evidence' / 'files' / 'support-matrix.json'
REQUIRED_CARDS = (
    'payload-integrity',
    'permission-enospc',
    'ssh-link-interrupt',
    'ssh-kill',
    'helper-kill',
    'symlink-junction-reparse-swap',
    'keepboth-32-way',
    'fail-replace-conflict',
    'dual-pane-ui-switch',
    'attach-no-enter',
)
INTERRUPT_KINDS = (
    'ssh-link-interrupt',
    'ssh-kill',
    'helper-kill',
)
REQUIRED_LIVE = (
    'live-fs', 'live-ssh', 'live-toctou', 'live-attack', 'live-ui',
)
REQUIRED_SCENARIOS = (
    'payload', 'permission_enospc', 'ssh_link_interrupt', 'ssh_kill',
    'helper_kill', 'toctou', 'keepboth', 'fail_replace', 'dual_pane',
    'attach_no_enter',
)
REQUIRED_ACS = {'AC31', 'AC32', 'AC33', 'AC34', 'AC35'}
CARD_OWNERS = {
    'payload-integrity': ['HD-028', 'HD-029'],
    'permission-enospc': ['HD-028'],
    'ssh-link-interrupt': ['HD-028'],
    'ssh-kill': ['HD-028'],
    'helper-kill': ['HD-028'],
    'symlink-junction-reparse-swap': ['HD-027', 'HD-028'],
    'keepboth-32-way': ['HD-028', 'HD-029'],
    'fail-replace-conflict': ['HD-028', 'HD-029'],
    'dual-pane-ui-switch': ['HD-029'],
    'attach-no-enter': ['HD-030', 'HD-031'],
}
CARD_ACS = {
    'payload-integrity': ['AC31'],
    'permission-enospc': ['AC32'],
    'ssh-link-interrupt': ['AC32'],
    'ssh-kill': ['AC32'],
    'helper-kill': ['AC32'],
    'symlink-junction-reparse-swap': ['AC34'],
    'keepboth-32-way': ['AC33'],
    'fail-replace-conflict': ['AC33'],
    'dual-pane-ui-switch': ['AC31', 'AC33'],
    'attach-no-enter': ['AC35'],
}
CARD_GRANTS = {
    'payload-integrity': 'no_authorized_disposable_fs_payload_lab',
    'permission-enospc': 'no_authorized_acl_quota_volume',
    'ssh-link-interrupt': 'no_authorized_ssh_link_interrupt',
    'ssh-kill': 'no_authorized_owned_ssh_pid_kill',
    'helper-kill': 'no_authorized_owned_filebridge_pid_kill',
    'symlink-junction-reparse-swap': 'no_authorized_second_process_symlink_attack',
    'keepboth-32-way': 'no_authorized_keepboth_race_lab',
    'fail-replace-conflict': 'no_authorized_replace_target_changed_lab',
    'dual-pane-ui-switch': 'no_winui_admission',
    'attach-no-enter': 'no_authorized_live_agent_path_insert',
}
LIVE_GRANTS = {
    'live-fs': 'no_authorized_disposable_fs_payload_lab',
    'live-ssh': 'no_authorized_ssh_interrupt_or_owned_pid_kill',
    'live-toctou': 'no_authorized_second_process_symlink_attack',
    'live-attack': 'no_authorized_keepboth_replace_race_lab',
    'live-ui': 'no_winui_admission',
}
AC_FLAGS = (
    'ac31_passed', 'ac32_passed', 'ac33_passed', 'ac34_passed',
    'ac35_passed',
)
L2_KEYS = (
    'l2_live_fs', 'l2_live_ssh', 'l2_live_toctou', 'l2_live_attack',
    'l2_live_ui',
)
SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})


def _load():
    return (
        json.loads(L2.read_text(encoding='utf-8')),
        json.loads(CATALOG.read_text(encoding='utf-8')),
        json.loads(MATRIX.read_text(encoding='utf-8')),
    )


class Hd032ResidualTests(unittest.TestCase):
    def test_l2_live_rows_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd032_l2_status')
        for key in L2_KEYS:
            self.assertEqual(doc[key], 'UNVERIFIED', key)
            self.assertNotEqual(doc[key], 'passed', key)
        for key in AC_FLAGS:
            self.assertFalse(doc[key], key)
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_fs'])
        self.assertFalse(doc['live_ssh'])
        self.assertFalse(doc['live_toctou'])
        self.assertFalse(doc['live_attack'])
        self.assertFalse(doc['live_ui'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['herdr_executed'])
        self.assertFalse(doc['copy_windows_fields_onto_linux'])
        self.assertFalse(doc['extrapolate_macos_arm64'])
        self.assertFalse(doc['silent_overwrite_tested'])
        self.assertFalse(doc['killed_user_daemon'])
        self.assertFalse(doc['glob_delete'])
        self.assertFalse(doc['auto_submit'])
        self.assertTrue(doc['fake_fs_cannot_pass_toctou'])
        self.assertTrue(doc['cannot_merge_interrupt_kinds'])
        missing = doc['missing']
        self.assertTrue(missing['live_fs_matrix'])
        self.assertTrue(missing['live_ssh_matrix'])
        self.assertTrue(missing['toctou_lab'])
        self.assertTrue(missing['attack_lab'])
        self.assertTrue(missing['winui_shell'])
        self.assertTrue(missing['acl_quota_volume'])
        self.assertTrue(missing['second_process_attacker'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        self.assertFalse((ROOT / 'tests' / 'Integration.Windows').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])

    def test_catalog_execution_cards_stay_not_run(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(catalog['document_kind'], 'hd032_file_fault_security_catalog')
        self.assertTrue(catalog['simulation'])
        self.assertFalse(catalog['template'])
        for key in L2_KEYS:
            self.assertEqual(catalog[key], 'UNVERIFIED', key)
        for key in AC_FLAGS:
            self.assertFalse(catalog[key], key)
        self.assertFalse(catalog['g0_passed'])
        self.assertNotEqual(catalog['phase_gate'], 'passed')
        self.assertFalse(catalog['live_fs'])
        self.assertFalse(catalog['live_ssh'])
        self.assertFalse(catalog['winui_admitted'])
        self.assertFalse(catalog['silent_overwrite_tested'])
        self.assertTrue(catalog['fake_fs_cannot_pass_toctou'])
        self.assertTrue(catalog['cannot_merge_interrupt_kinds'])
        self.assertFalse(catalog['live_toctou'])
        self.assertFalse(catalog['live_attack'])
        self.assertFalse(catalog['live_ui'])
        self.assertEqual(catalog['redaction']['credential'], 'omitted')
        self.assertEqual(catalog['redaction']['file_body'], 'omitted')
        self.assertEqual(catalog['redaction']['host'], 'omitted')
        self.assertEqual(catalog['redaction']['path'], 'omitted')
        cards = {item['id']: item for item in catalog['execution_cards']}
        self.assertEqual(tuple(cards), REQUIRED_CARDS)
        seen = set()
        for card_id, card in cards.items():
            self.assertEqual(card['owner_children'], CARD_OWNERS[card_id], card_id)
            self.assertEqual(card['ac_ids'], CARD_ACS[card_id], card_id)
            seen.update(card['ac_ids'])
        self.assertTrue(REQUIRED_ACS <= seen)
        self.assertNotIn('ssh-interrupt-kill-helper-kill', cards)
        interrupt_grants = [cards[item]['missing_grant'] for item in INTERRUPT_KINDS]
        self.assertEqual(len(set(interrupt_grants)), 3)
        for card_id, card in cards.items():
            self.assertEqual(card['missing_grant'], CARD_GRANTS[card_id], card_id)
        for card in catalog['execution_cards']:
            self.assertEqual(card['live_status'], 'UNVERIFIED')
            self.assertEqual(card['live_result'], 'not_run')
            self.assertNotIn(str(card['live_result']).lower(), SUCCESS)
            self.assertTrue(card['missing_grant'])
            self.assertEqual(card['l1_status'], 'shipped')
            self.assertIn(card['required_evidence'], ('L2', 'L3', 'L4'))
            self.assertNotEqual(card['required_evidence'], 'L1')
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
            blob = json.dumps(doc)
            self.assertNotIn('password', blob.lower())
            self.assertNotIn('stdout', doc)
            self.assertNotIn('stderr', doc)

    def test_support_matrix_promised_rows_stay_not_run(self):
        matrix = json.loads(MATRIX.read_text(encoding='utf-8'))
        self.assertEqual(matrix['document_kind'], 'hd032_support_matrix')
        self.assertFalse(matrix['copy_windows_fields_onto_linux'])
        self.assertFalse(matrix['extrapolate_macos_arm64'])
        self.assertFalse(matrix['silent_overwrite_tested'])
        self.assertTrue(matrix['fake_fs_cannot_pass_toctou'])
        self.assertTrue(matrix['cannot_merge_interrupt_kinds'])
        self.assertEqual(matrix['compatible_by_default'], [])
        self.assertEqual(
            tuple(item['id'] for item in matrix['scenarios']),
            REQUIRED_SCENARIOS,
        )
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
        for scenario in matrix['scenarios']:
            self.assertEqual(scenario['live_status'], 'not_run')
            self.assertNotIn(scenario['live_result'], SUCCESS)
        for cell in matrix['cells']:
            self.assertEqual(cell['live_status'], 'not_run')
            self.assertFalse(cell['compatible'])
        linux = platforms['linux-x64-remote']
        windows = platforms['windows-11-x64-client']
        self.assertNotEqual(linux['missing_grant'], windows['missing_grant'])

    def test_closeout_rejects_pass_claims_and_grant_gaps(self):
        hd032, catalog, matrix = _load()
        repository._check_hd032_closeout(hd032, catalog, matrix)

        bad = deepcopy(hd032)
        bad['ac31_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['ac35_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['l2_live_fs'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['l2_live_toctou'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['l2_live_attack'] = 'verified'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['g0_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['cannot_merge_interrupt_kinds'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['herdr_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(hd032)
        bad['integration_ssh_project'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(bad, catalog, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['live_result'] = 'Passed'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['missing_grant'] = ''
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        bad['live_rows'][2]['result'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        bad['silent_overwrite_tested'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        bad['fake_fs_cannot_pass_toctou'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        bad['cannot_merge_interrupt_kinds'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'] = [
            item for item in bad['execution_cards']
            if item['id'] != 'ssh-kill'
        ]
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        for card in bad['execution_cards']:
            if card['id'] in INTERRUPT_KINDS:
                card['missing_grant'] = 'no_authorized_network_down'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['l1_artifacts'] = []
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['required_evidence'] = 'L1'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, bad, matrix)

        bad = deepcopy(matrix)
        bad['platforms'][0]['support'] = 'supported'
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, catalog, bad)

        bad = deepcopy(matrix)
        bad['copy_windows_fields_onto_linux'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, catalog, bad)

        bad = deepcopy(matrix)
        bad['cannot_merge_interrupt_kinds'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, catalog, bad)

        bad = deepcopy(matrix)
        bad['ac34_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd032_closeout(hd032, catalog, bad)


if __name__ == '__main__':
    unittest.main()
