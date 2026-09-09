from copy import deepcopy
from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-034-l2.json'
CATALOG = ROOT / 'evidence' / 'packaging' / 'catalog.json'
MATRIX = ROOT / 'evidence' / 'packaging' / 'support-matrix.json'
REQUIRED_CARDS = (
    'clean-install',
    'runtime-missing',
    'signed-update',
    'bad-publisher-or-tamper',
    'signed-rollback',
    'config-backup-restore',
    'file-job-defer',
    'unsigned-local-build',
)
REQUIRED_LIVE = (
    'live-clean-install', 'live-runtime-missing', 'live-signed-update',
    'live-bad-publisher-or-tamper', 'live-signed-rollback',
    'live-config-backup-restore', 'live-file-job-defer',
    'live-unsigned-local-build',
)
REQUIRED_SCENARIOS = (
    'clean_install', 'runtime_missing', 'signed_update',
    'bad_publisher_or_tamper', 'signed_rollback', 'config_backup_restore',
    'file_job_defer', 'unsigned_local_build',
)
REQUIRED_ACS = {'AC41', 'AC42'}
CARD_OWNERS = {
    'clean-install': ['HD-007', 'HD-011'],
    'runtime-missing': ['HD-007'],
    'signed-update': ['HD-007'],
    'bad-publisher-or-tamper': ['HD-021', 'HD-007'],
    'signed-rollback': ['HD-007'],
    'config-backup-restore': ['HD-007'],
    'file-job-defer': ['HD-028'],
    'unsigned-local-build': ['HD-007'],
}
CARD_ACS = {
    'clean-install': ['AC41'],
    'runtime-missing': ['AC41'],
    'signed-update': ['AC42'],
    'bad-publisher-or-tamper': ['AC42'],
    'signed-rollback': ['AC42'],
    'config-backup-restore': ['AC42'],
    'file-job-defer': ['AC42'],
    'unsigned-local-build': ['AC41'],
}
CARD_GRANTS = {
    'clean-install': 'no_authorized_clean_machine_install',
    'runtime-missing': 'no_authorized_runtime_missing_vm',
    'signed-update': 'no_authorized_signed_update_channel',
    'bad-publisher-or-tamper': 'no_authorized_publisher_identity',
    'signed-rollback': 'no_authorized_signed_rollback',
    'config-backup-restore': 'no_authorized_config_restore_on_install',
    'file-job-defer': 'no_authorized_file_job_defer_during_update',
    'unsigned-local-build': 'no_authorized_signing_service',
}
LIVE_GRANTS = {
    'live-clean-install': 'no_authorized_clean_machine_install',
    'live-runtime-missing': 'no_authorized_runtime_missing_vm',
    'live-signed-update': 'no_authorized_signed_update_channel',
    'live-bad-publisher-or-tamper': 'no_authorized_publisher_identity',
    'live-signed-rollback': 'no_authorized_signed_rollback',
    'live-config-backup-restore': 'no_authorized_config_restore_on_install',
    'live-file-job-defer': 'no_authorized_file_job_defer_during_update',
    'live-unsigned-local-build': 'no_authorized_signing_service',
}
AC_FLAGS = ('ac41_passed', 'ac42_passed')
L2_L3_KEYS = (
    'l2_live_install', 'l2_live_sign', 'l2_live_update', 'l2_live_rollback',
    'l3_clean_machine',
)
SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
NULL_KEYS = (
    'publisher', 'certificate_subject', 'certificate_thumbprint',
    'timestamp_url', 'distribution_url', 'app_installer_url',
    'package_sha256', 'msix_sha256', 'appinstaller_sha256', 'sidecar_sha256',
    'install_hours', 'soak_hours',
)


def _load():
    return (
        json.loads(L2.read_text(encoding='utf-8')),
        json.loads(CATALOG.read_text(encoding='utf-8')),
        json.loads(MATRIX.read_text(encoding='utf-8')),
    )


class Hd034ResidualTests(unittest.TestCase):
    def test_l2_l3_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd034_l2_status')
        for key in L2_L3_KEYS:
            self.assertEqual(doc[key], 'UNVERIFIED', key)
            self.assertNotEqual(doc[key], 'passed', key)
        for key in AC_FLAGS:
            self.assertFalse(doc[key], key)
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_install'])
        self.assertFalse(doc['live_sign'])
        self.assertFalse(doc['live_update'])
        self.assertFalse(doc['live_rollback'])
        self.assertFalse(doc['live_clean_machine'])
        self.assertFalse(doc['publisher_identity_confirmed'])
        self.assertFalse(doc['signed_msix_built'])
        self.assertFalse(doc['clean_machine_install_executed'])
        self.assertFalse(doc['unsigned_local_build_is_release'])
        self.assertFalse(doc['exe_copy_is_rollback'])
        self.assertFalse(doc['linux_msix_client'])
        self.assertFalse(doc['invented_package_hashes'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['herdr_executed'])
        self.assertFalse(doc['packaging_project'])
        self.assertTrue(doc['fake_publisher_cannot_pass_ac41'])
        self.assertTrue(doc['unsigned_local_build_cannot_pass_release_install'])
        self.assertTrue(doc['exe_copy_cannot_pass_rollback'])
        self.assertTrue(doc['evergreen_webview2_planned_runtime'])
        self.assertTrue(doc['missing_runtime_must_prompt'])
        self.assertTrue(doc['app_installer_planned_channel'])
        self.assertFalse(doc['silent_admin_runtime_install'])
        self.assertFalse(doc['parallel_self_update_service'])
        self.assertFalse(doc['official_app_installer_downgrade_default'])
        missing = doc['missing']
        self.assertTrue(missing['clean_machine_install'])
        self.assertTrue(missing['publisher_identity'])
        self.assertTrue(missing['signing_service'])
        self.assertTrue(missing['packaging_project'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Windows').exists())
        self.assertFalse((ROOT / 'packaging').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
        self.assertFalse(result['windows_verified'])

    def test_catalog_execution_cards_stay_not_run(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(catalog['document_kind'], 'hd034_packaging_catalog')
        self.assertTrue(catalog['simulation'])
        self.assertEqual(catalog['fixture_origin'], 'synthetic')
        self.assertFalse(catalog['template'])
        for key in L2_L3_KEYS:
            self.assertEqual(catalog[key], 'UNVERIFIED', key)
        for key in AC_FLAGS:
            self.assertFalse(catalog[key], key)
        self.assertFalse(catalog['g0_passed'])
        self.assertFalse(catalog['publisher_identity_confirmed'])
        self.assertFalse(catalog['signed_msix_built'])
        self.assertFalse(catalog['clean_machine_install_executed'])
        self.assertFalse(catalog['linux_msix_client'])
        self.assertTrue(catalog['fake_publisher_cannot_pass_ac41'])
        self.assertTrue(catalog['unsigned_local_build_cannot_pass_release_install'])
        self.assertTrue(catalog['exe_copy_cannot_pass_rollback'])
        self.assertEqual(catalog['redaction']['credential'], 'omitted')
        self.assertEqual(catalog['redaction']['host'], 'omitted')
        for key in NULL_KEYS:
            self.assertIsNone(catalog[key], key)
        cards = {item['id']: item for item in catalog['execution_cards']}
        self.assertEqual(tuple(cards), REQUIRED_CARDS)
        seen = set()
        grants = []
        for card_id, card in cards.items():
            self.assertEqual(card['owner_children'], CARD_OWNERS[card_id], card_id)
            self.assertEqual(card['ac_ids'], CARD_ACS[card_id], card_id)
            self.assertEqual(card['missing_grant'], CARD_GRANTS[card_id], card_id)
            seen.update(card['ac_ids'])
            grants.append(card['missing_grant'])
        self.assertTrue(REQUIRED_ACS <= seen)
        self.assertEqual(len(grants), len(set(grants)))
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
            for key in NULL_KEYS:
                if key in doc:
                    self.assertIsNone(doc[key], rel)
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
            self.assertNotIn('.pfx', blob.lower())
            self.assertNotIn('stdout', doc)
            self.assertNotIn('stderr', doc)

    def test_unsigned_build_and_exe_copy_are_not_release(self):
        unsigned = json.loads(
            (ROOT / 'evidence' / 'packaging' / 'live-unsigned-local-build.not-run.json')
            .read_text(encoding='utf-8')
        )
        self.assertEqual(unsigned['kind'], 'live_unsigned_local_build')
        self.assertEqual(unsigned['result'], 'not_run')
        self.assertFalse(unsigned['unsigned_local_build_is_release'])
        self.assertTrue(unsigned['unsigned_local_build_cannot_pass_release_install'])
        self.assertFalse(unsigned['signed_msix_built'])
        self.assertIsNone(unsigned['package_sha256'])
        template = json.loads(
            (ROOT / 'evidence' / 'packaging' / 'live-unsigned-local-build.template.json')
            .read_text(encoding='utf-8')
        )
        self.assertTrue(template['template'])
        self.assertFalse(template['unsigned_local_build_is_release'])
        self.assertIsNone(template['result'])
        rollback = json.loads(
            (ROOT / 'evidence' / 'packaging' / 'live-signed-rollback.not-run.json')
            .read_text(encoding='utf-8')
        )
        self.assertFalse(rollback['exe_copy_is_rollback'])
        self.assertTrue(rollback['exe_copy_cannot_pass_rollback'])
        self.assertFalse(rollback['killed_user_daemon'])

    def test_support_matrix_promised_rows_stay_not_run(self):
        matrix = json.loads(MATRIX.read_text(encoding='utf-8'))
        self.assertEqual(matrix['document_kind'], 'hd034_support_matrix')
        self.assertFalse(matrix['copy_windows_fields_onto_linux'])
        self.assertFalse(matrix['extrapolate_macos_arm64'])
        self.assertFalse(matrix['linux_msix_client'])
        self.assertEqual(matrix['compatible_by_default'], [])
        self.assertEqual(
            tuple(item['id'] for item in matrix['scenarios']),
            REQUIRED_SCENARIOS,
        )
        for key in AC_FLAGS:
            self.assertFalse(matrix[key], key)
        platforms = {item['id']: item for item in matrix['platforms']}
        windows = platforms['windows-11-x64-client']
        self.assertEqual(windows['promise'], 'promised')
        self.assertEqual(windows['live_status'], 'not_run')
        self.assertEqual(windows['live_result'], 'not_run')
        self.assertFalse(windows['compatible'])
        self.assertEqual(windows['support'], 'promised_not_run')
        linux = platforms['linux-x64-remote']
        self.assertEqual(linux['promise'], 'none')
        self.assertEqual(linux['support'], 'unsupported')
        self.assertEqual(linux['role'], 'remote')
        self.assertFalse(linux['linux_msix'])
        self.assertNotIn(linux['support'], ('supported', 'stable', 'promised', 'promised_not_run'))
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
            self.assertEqual(scenario['live_result'], 'not_run')
            self.assertNotIn(scenario['live_result'], SUCCESS)
        for cell in matrix['cells']:
            self.assertEqual(cell['platform'], 'windows-11-x64-client')
            self.assertEqual(cell['live_status'], 'not_run')
            self.assertEqual(cell['live_result'], 'not_run')
            self.assertFalse(cell['compatible'])
            self.assertNotIn(cell['live_result'], SUCCESS)
        promised = {item['id'] for item in matrix['promised_range']}
        self.assertEqual(promised, {'windows-11-x64-client'})
        self.assertNotEqual(linux['missing_grant'], windows['missing_grant'])

    def test_closeout_rejects_pass_claims_and_fake_publisher(self):
        hd034, catalog, matrix = _load()
        repository._check_hd034_closeout(hd034, catalog, matrix)

        bad = deepcopy(hd034)
        bad['ac41_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['ac42_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['l2_live_install'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['l2_live_sign'] = 'verified'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['l2_live_update'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['l2_live_rollback'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['l3_clean_machine'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['g0_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['publisher_identity_confirmed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['signed_msix_built'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['clean_machine_install_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['unsigned_local_build_is_release'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['exe_copy_is_rollback'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['linux_msix_client'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['herdr_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['integration_windows_project'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['packaging_project'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['fake_publisher_cannot_pass_ac41'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['unsigned_local_build_cannot_pass_release_install'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['exe_copy_cannot_pass_rollback'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['killed_user_daemon'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['publisher'] = 'CN=Contoso'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['package_sha256'] = 'a' * 64
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(hd034)
        bad['install_hours'] = 2
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(bad, catalog, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['live_result'] = 'Passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['missing_grant'] = ''
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, bad, matrix)

        bad = deepcopy(catalog)
        for card in bad['execution_cards']:
            card['missing_grant'] = 'no_authorized_clean_machine_install'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, bad, matrix)

        bad = deepcopy(catalog)
        bad['live_rows'][7]['result'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, bad, matrix)

        bad = deepcopy(catalog)
        bad['soak_hours'] = 8
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][7]['live_result'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['l1_artifacts'] = []
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['required_evidence'] = 'L1'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, bad, matrix)

        bad = deepcopy(matrix)
        bad['platforms'][0]['support'] = 'supported'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, catalog, bad)

        bad = deepcopy(matrix)
        bad['copy_windows_fields_onto_linux'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, catalog, bad)

        bad = deepcopy(matrix)
        bad['linux_msix_client'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, catalog, bad)

        bad = deepcopy(matrix)
        bad['ac41_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, catalog, bad)

        linux = deepcopy(matrix)
        linux['platforms'][1]['support'] = 'promised_not_run'
        linux['platforms'][1]['promise'] = 'promised'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, catalog, linux)

        soak = deepcopy(catalog)
        soak['live_rows'][0]['result'] = 'success'
        soak['live_rows'][0]['status'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_closeout(hd034, soak, matrix)


if __name__ == '__main__':
    unittest.main()
