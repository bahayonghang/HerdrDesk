from copy import deepcopy
from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path
import json
import os
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository
import record_lab_msix as lab_msix_cli
from record_lab_msix import (
    LAB_MSIX_KIND,
    LAB_MSIX_NULL_KEYS,
    LAB_MSIX_REL,
    LabMsixError,
    validate_lab_msix,
)

L2 = ROOT / 'implementation' / 'hd-034-l2.json'
CATALOG = ROOT / 'evidence' / 'packaging' / 'catalog.json'
MATRIX = ROOT / 'evidence' / 'packaging' / 'support-matrix.json'
POINTER = ROOT / 'evidence' / 'packaging' / 'lab-sign-overlay-pointer.json'
CERT_SCRIPT = ROOT / 'scripts' / 'new_lab_certificate.ps1'
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
        self.assertEqual(
            doc['lab_sign_overlay_pointer'],
            'evidence/packaging/lab-sign-overlay-pointer.json',
        )
        missing = doc['missing']
        self.assertTrue(missing['clean_machine_install'])
        self.assertTrue(missing['publisher_identity'])
        self.assertTrue(missing['signing_service'])
        self.assertTrue(missing['packaging_project'])
        repository.check_integration_windows_layout(ROOT)
        self.assertTrue((ROOT / 'packaging').is_dir())
        self.assertTrue((ROOT / 'packaging' / 'Package.appxmanifest').is_file())
        self.assertTrue((ROOT / 'src' / 'HerdDesk.App' / 'App.xaml').is_file())
        self.assertFalse((ROOT / 'packaging' / 'HerdDesk.Package.wapproj').exists())
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
        self.assertEqual(
            catalog['lab_sign_overlay_pointer'],
            'evidence/packaging/lab-sign-overlay-pointer.json',
        )
        self.assertEqual(
            catalog['lab_msix_capture'],
            'evidence/packaging/live-lab-msix.json',
        )
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

        pointer = json.loads(POINTER.read_text(encoding='utf-8'))
        repository._check_hd034_lab_sign_pointer(pointer)

        bad = deepcopy(pointer)
        bad['ac41_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_lab_sign_pointer(bad)

        bad = deepcopy(pointer)
        bad['is_release_install'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_lab_sign_pointer(bad)

        bad = deepcopy(pointer)
        bad['result'] = 'success'
        with self.assertRaises(AssertionError):
            repository._check_hd034_lab_sign_pointer(bad)

        bad = deepcopy(pointer)
        bad['l2_live_sign'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd034_lab_sign_pointer(bad)

        bad = deepcopy(pointer)
        bad['signed_msix_built'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_lab_sign_pointer(bad)

        bad = deepcopy(pointer)
        bad['pfx_written'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_lab_sign_pointer(bad)

        bad = deepcopy(pointer)
        bad['pfx_in_git'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd034_lab_sign_pointer(bad)


class Hd034PackageScriptTests(unittest.TestCase):
    def test_verify_valid_layout_uses_shipped_script(self):
        code, report = repository.run_package_release(
            'Verify',
            layout_path='tests/fixtures/packaging/layout-valid',
        )
        self.assertEqual(code, 0)
        self.assertTrue(report['ok'])
        self.assertEqual(report['action'], 'Verify')
        self.assertEqual(report['identity_name'], 'HerdDesk.Lab')
        self.assertEqual(report['publisher'], 'CN=HerdDesk Lab (not release)')
        self.assertFalse(report['private_key_found'])
        self.assertFalse(report['unsigned_local_build_is_release'])
        self.assertFalse(report['signed'])
        self.assertFalse(report['is_release_install'])
        self.assertFalse(report['ac41_passed'])
        self.assertFalse(report['ac42_passed'])
        self.assertFalse(report['g0_passed'])

    def test_verify_store_identity_uses_shipped_script(self):
        code, report = repository.run_package_release(
            'Verify',
            layout_path='tests/fixtures/packaging/layout-store-identity',
        )
        self.assertNotEqual(code, 0)
        self.assertIsNot(report.get('ok'), True)
        self.assertFalse(report.get('ac41_passed', False))
        self.assertFalse(report.get('unsigned_local_build_is_release', False))

    def test_verify_source_manifest_uses_shipped_script(self):
        code, report = repository.run_package_release(
            'Verify',
            layout_path='packaging',
        )
        self.assertEqual(code, 0)
        self.assertTrue(report['ok'])
        self.assertEqual(report['identity_name'], 'HerdDesk.Lab')
        self.assertEqual(report['publisher'], 'CN=HerdDesk Lab (not release)')
        self.assertEqual(report['processor_architecture'], 'x64')
        self.assertFalse(report['unsigned_local_build_is_release'])
        self.assertFalse(report['is_release_install'])
        self.assertFalse(report['signed'])
        self.assertFalse(report['ac41_passed'])
        self.assertFalse(report['ac42_passed'])
        self.assertFalse(report['g0_passed'])

    def test_sign_without_certificate_uses_shipped_script(self):
        code, report = repository.run_package_release('Sign')
        self.assertNotEqual(code, 0)
        self.assertIsNot(report.get('ok'), True)
        self.assertIsNot(report.get('signed'), True)
        self.assertFalse(report.get('ac41_passed', False))
        self.assertFalse(report.get('ac42_passed', False))

    def test_sign_rejects_certificate_under_packaging_and_fixtures(self):
        code, report = repository.run_package_release(
            'Sign',
            certificate_path='packaging/Package.appxmanifest',
        )
        self.assertNotEqual(code, 0)
        self.assertIsNot(report.get('ok'), True)
        self.assertIsNot(report.get('signed'), True)
        self.assertFalse(report.get('ac41_passed', False))
        self.assertFalse(report.get('signed_msix_built', False))
        error = str(report.get('error') or '')
        self.assertIn('packaging', error.lower())
        fixture_code, fixture_report = repository.run_package_release(
            'Sign',
            certificate_path='tests/fixtures/packaging/layout-valid/AppxManifest.xml',
        )
        self.assertNotEqual(fixture_code, 0)
        self.assertIsNot(fixture_report.get('ok'), True)
        self.assertIsNot(fixture_report.get('signed'), True)
        fixture_error = str(fixture_report.get('error') or '')
        self.assertIn('fixtures', fixture_error.lower())

    def test_find_sdk_tools_skip_null_programfiles_and_search_kits_x64(self):
        src = (ROOT / 'scripts' / 'package_release.ps1').read_text(encoding='utf-8')
        find_fn = src[src.find('function Get-WindowsKitsBinRoots') : src.find('function Copy-PackagingOverlay')]
        self.assertIn("GetFolderPath('ProgramFilesX86')", find_fn)
        self.assertIn('Windows Kits\\10\\bin', find_fn)
        self.assertIn("Directory.Name -eq 'x64'", find_fn)
        self.assertIn('IsNullOrWhiteSpace', find_fn)
        self.assertIn('makeappx.exe', find_fn)
        self.assertIn('signtool.exe', find_fn)
        self.assertNotIn('10.0.26100.0', find_fn)

    def test_sign_script_passes_empty_lab_password_without_weakening_fail_closed(self):
        src = (ROOT / 'scripts' / 'package_release.ps1').read_text(encoding='utf-8')
        sign_fn = src[src.find('function Invoke-ActionSign') : src.find("switch ($Action)")]
        self.assertIn('/p', sign_fn)
        self.assertIn('[string]::Empty', sign_fn)
        self.assertIn('AllowEmptyString', src[src.find('function Invoke-External') : src.find('function New-BaseReport')])
        self.assertIn('CertificatePath is required', sign_fn)
        self.assertIn('Sign requires an MSIX', sign_fn)
        self.assertIn('CertificatePath must not live under packaging/', sign_fn)
        self.assertIn('CertificatePath must not live under tests/fixtures.', sign_fn)
        code, report = repository.run_package_release('Sign')
        self.assertNotEqual(code, 0)
        self.assertIsNot(report.get('signed'), True)

    def test_structure_contract_invokes_shipped_script(self):
        repository._HD034_SCRIPT_CONTRACT_OK = False
        repository._check_hd034_package_script_contract()
        self.assertTrue(repository._HD034_SCRIPT_CONTRACT_OK)

    def test_helper_refuses_build_action(self):
        with self.assertRaises(AssertionError):
            repository.run_package_release('Build')


class Hd034LabCertificateTests(unittest.TestCase):
    def test_script_and_pointer_exist_and_stay_unverified(self):
        self.assertTrue(CERT_SCRIPT.is_file())
        self.assertTrue(POINTER.is_file())
        pointer = json.loads(POINTER.read_text(encoding='utf-8'))
        self.assertEqual(pointer['document_kind'], 'hd034_lab_sign_overlay_pointer')
        self.assertEqual(pointer['result'], 'not_run')
        self.assertEqual(pointer['l2_live_sign'], 'UNVERIFIED')
        self.assertEqual(pointer['script'], 'scripts/new_lab_certificate.ps1')
        self.assertFalse(pointer['is_release_install'])
        self.assertFalse(pointer['signed_msix_built'])
        self.assertFalse(pointer['publisher_identity_confirmed'])
        self.assertFalse(pointer['ac41_passed'])
        self.assertFalse(pointer['ac42_passed'])
        self.assertFalse(pointer['g0_passed'])
        self.assertFalse(pointer['pfx_in_git'])
        self.assertFalse(pointer['pfx_written'])
        self.assertFalse(pointer['live_sign'])
        self.assertTrue(pointer['lab_certificate_script_is_not_signed_release_msix'])
        self.assertTrue(pointer['fake_publisher_cannot_pass_ac41'])
        self.assertTrue(pointer['unsigned_local_build_cannot_pass_release_install'])
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(catalog['l2_live_sign'], 'UNVERIFIED')
        self.assertFalse(catalog['ac41_passed'])
        live_sign = json.loads(
            (ROOT / 'evidence' / 'packaging' / 'live-signed-update.not-run.json')
            .read_text(encoding='utf-8')
        )
        self.assertEqual(live_sign['result'], 'not_run')
        self.assertNotIn(str(live_sign['result']).lower(), SUCCESS)

    def test_refuses_pfx_in_packaging_and_source_tree(self):
        repository._check_hd034_lab_certificate_script_contract()

    def test_writes_pfx_only_to_requested_temp_path(self):
        self.assertTrue(CERT_SCRIPT.is_file())
        if os.name != 'nt':
            pointer = json.loads(POINTER.read_text(encoding='utf-8'))
            self.assertEqual(pointer['result'], 'not_run')
            self.assertEqual(pointer['l2_live_sign'], 'UNVERIFIED')
            self.assertFalse(pointer['ac41_passed'])
            self.assertFalse(pointer['is_release_install'])
            return
        with tempfile.TemporaryDirectory(prefix='herddesk-lab-cert-') as tmp:
            pfx = Path(tmp) / 'HerdDesk.Lab.pfx'
            code, report = repository.run_new_lab_certificate(pfx)
            self.assertEqual(code, 0, report)
            self.assertTrue(report['ok'])
            self.assertEqual(report['document_kind'], 'hd034_lab_certificate')
            self.assertEqual(report['subject'], 'CN=HerdDesk Lab (not release)')
            self.assertTrue(report['lab_identity_not_release'])
            self.assertTrue(report['lab_identity_not_store'])
            self.assertFalse(report['is_release_install'])
            self.assertFalse(report['signed_msix_built'])
            self.assertFalse(report['publisher_identity_confirmed'])
            self.assertFalse(report['ac41_passed'])
            self.assertFalse(report['ac42_passed'])
            self.assertFalse(report['g0_passed'])
            self.assertTrue(report['pfx_written'])
            self.assertFalse(report['pfx_in_git'])
            self.assertTrue(pfx.is_file())
            self.assertEqual(
                Path(report['certificate_path']).resolve(),
                pfx.resolve(),
            )
            packaging_hits = list((ROOT / 'packaging').rglob('*.pfx'))
            fixture_hits = list((ROOT / 'tests' / 'fixtures').rglob('*.pfx'))
            self.assertEqual(packaging_hits, [])
            self.assertEqual(fixture_hits, [])
            sign_code, sign_report = repository.run_package_release(
                'Sign',
                certificate_path=str(pfx),
            )
            self.assertNotEqual(sign_code, 0, sign_report)
            self.assertIs(sign_report.get('ok'), False)
            self.assertIs(sign_report.get('signed'), False)
            self.assertFalse(sign_report.get('ac41_passed', False))
            self.assertFalse(sign_report.get('ac42_passed', False))
            self.assertFalse(sign_report.get('is_release_install', False))
            self.assertFalse(sign_report.get('g0_passed', False))
            self.assertFalse(sign_report.get('signed_msix_built', False))
            self.assertFalse(sign_report.get('publisher_identity_confirmed', False))


def _lab_msix_doc(**overrides):
    doc = {
        'document_kind': LAB_MSIX_KIND,
        'template': False,
        'capture_id': 'hd034-live-lab-msix-test',
        'kind': 'lab_msix_pack_sign',
        'started_at_utc': '2026-09-11T01:00:00Z',
        'captured_at_utc': '2026-09-11T01:01:00Z',
        'git_sha': 'c' * 40,
        'result': 'not_run',
        'ac41_passed': False,
        'ac42_passed': False,
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'signed_msix_built': False,
        'live_sign': False,
        'live_install': False,
        'publisher_identity_confirmed': False,
        'is_release_install': False,
        'herdr_executed': False,
        'pfx_in_git': False,
        'winui_admitted': False,
        'fake_publisher_cannot_pass_ac41': True,
        'unsigned_local_build_cannot_pass_release_install': True,
        'lab_msix_packed': True,
        'lab_signature_applied': True,
        'makeappx_found': True,
        'signtool_found': True,
        'lab_msix_sha256': 'a' * 64,
        'publisher': None,
        'package_sha256': None,
        'msix_sha256': None,
        'certificate_subject': None,
        'certificate_thumbprint': None,
        'timestamp_url': None,
    }
    doc.update(overrides)
    return doc


def _write_lab_msix(root: Path, **overrides) -> Path:
    dest = root / LAB_MSIX_REL
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(_lab_msix_doc(**overrides)), encoding='utf-8')
    return dest


class Hd034LabMsixTests(unittest.TestCase):
    def test_overlay_file_is_optional_until_record(self):
        overlay_path = ROOT / LAB_MSIX_REL
        report = validate_lab_msix(ROOT)
        if overlay_path.is_file():
            self.assertIsNotNone(report)
            overlay = json.loads(overlay_path.read_text(encoding='utf-8'))
            self.assertEqual(overlay['document_kind'], LAB_MSIX_KIND)
            self.assertFalse(overlay['ac41_passed'])
            self.assertFalse(overlay['ac42_passed'])
            self.assertFalse(overlay['signed_msix_built'])
            self.assertFalse(overlay['live_sign'])
            self.assertFalse(overlay['live_install'])
            self.assertTrue(overlay['lab_msix_packed'])
            self.assertTrue(overlay['lab_signature_applied'])
            self.assertTrue(overlay['makeappx_found'])
            self.assertTrue(overlay['signtool_found'])
            self.assertEqual(overlay['result'], 'not_run')
            for key in LAB_MSIX_NULL_KEYS:
                if key in overlay:
                    self.assertIsNone(overlay[key], key)
            self.assertFalse(report['ac41_passed'])
            self.assertFalse(report['signed_msix_built'])
            self.assertTrue(report['lab_msix_packed'])
            self.assertTrue(report['lab_signature_applied'])
        else:
            self.assertIsNone(report)

    def test_cli_validates_without_recording(self):
        script = ROOT / 'scripts' / 'record_lab_msix.py'
        self.assertTrue(script.is_file())
        src = script.read_text(encoding='utf-8')
        self.assertIn('--record', src)
        self.assertNotIn('Add-AppxPackage', src[src.find('def record(') : src.find('def _unrecorded_report')])
        self.assertNotIn('DISPLAYCONFIG', src)
        overlay_path = ROOT / LAB_MSIX_REL
        pointer_path = ROOT / 'evidence' / 'packaging' / 'lab-sign-overlay-pointer.json'
        before_overlay = (
            overlay_path.read_text(encoding='utf-8') if overlay_path.is_file() else None
        )
        before_pointer = pointer_path.read_text(encoding='utf-8')
        completed = subprocess.run(
            [sys.executable, str(script)],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding='utf-8',
            check=False,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        report = json.loads(completed.stdout)
        self.assertFalse(report['ac41_passed'])
        self.assertFalse(report['signed_msix_built'])
        self.assertEqual(report['result'], 'not_run')
        self.assertEqual(pointer_path.read_text(encoding='utf-8'), before_pointer)
        pointer = json.loads(before_pointer)
        self.assertFalse(pointer['signed_msix_built'])
        self.assertFalse(pointer['live_sign'])
        self.assertFalse(pointer['pfx_written'])
        if before_overlay is None:
            self.assertFalse(overlay_path.is_file())
            self.assertFalse(report.get('recorded'))
        else:
            self.assertEqual(overlay_path.read_text(encoding='utf-8'), before_overlay)
            self.assertTrue(report.get('recorded'))

    def test_catalog_keeps_missing_grants_and_ac41_false(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(
            catalog['lab_msix_capture'],
            'evidence/packaging/live-lab-msix.json',
        )
        self.assertFalse(catalog['ac41_passed'])
        self.assertFalse(catalog['signed_msix_built'])
        self.assertFalse(catalog['live_sign'])
        self.assertTrue(catalog['missing']['signing_service'])
        cards = {item['id']: item for item in catalog['execution_cards']}
        self.assertEqual(
            cards['unsigned-local-build']['missing_grant'],
            'no_authorized_signing_service',
        )
        self.assertIn('not AC41', catalog['note'])
        self.assertIn('not a production Publisher', catalog['note'])

    def test_structure_contract_invokes_lab_msix(self):
        repository._check_hd034_lab_msix()

    def test_record_packs_and_signs_with_hooks(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            created = []

            def create_certificate(_root, cert):
                created.append(Path(cert))
                Path(cert).parent.mkdir(parents=True, exist_ok=True)
                Path(cert).write_bytes(b'lab-pfx')
                return {'ok': True, 'document_kind': 'hd034_lab_certificate'}

            def run_build(_root, output_root):
                out = Path(output_root)
                out.mkdir(parents=True, exist_ok=True)
                msix = out / 'HerdDesk.Lab.msix'
                msix.write_bytes(b'lab-msix-bytes')
                return {'ok': True, 'action': 'Build', 'package_path': str(msix)}

            def run_sign(_root, output_root, cert):
                self.assertEqual(Path(cert), created[0])
                return {'ok': True, 'signed': True, 'signature': 'lab_cert'}

            doc = lab_msix_cli.record(
                root,
                create_certificate=create_certificate,
                run_build=run_build,
                run_sign=run_sign,
                git_sha='d' * 40,
                now='2026-09-11T02:00:00Z',
            )
            self.assertTrue(doc['lab_msix_packed'])
            self.assertTrue(doc['lab_signature_applied'])
            self.assertTrue(doc['makeappx_found'])
            self.assertTrue(doc['signtool_found'])
            self.assertFalse(doc['ac41_passed'])
            self.assertFalse(doc['signed_msix_built'])
            self.assertFalse(doc['live_sign'])
            self.assertFalse(doc['live_install'])
            self.assertIsNone(doc['msix_sha256'])
            self.assertIsNone(doc['publisher'])
            self.assertEqual(len(doc['lab_msix_sha256']), 64)
            self.assertEqual(created[0].name, 'HerdDesk.Lab.pfx')
            report = validate_lab_msix(root)
            self.assertIsNotNone(report)
            self.assertTrue(report['lab_msix_packed'])
            self.assertTrue(report['lab_signature_applied'])
            self.assertFalse(report['ac41_passed'])

    def test_record_does_not_treat_leftover_msix_as_packed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            leftover = root / 'artifacts' / 'packaging' / 'HerdDesk.Lab.msix'
            leftover.parent.mkdir(parents=True)
            leftover.write_bytes(b'old-msix')
            signed = []

            def create_certificate(_root, cert):
                Path(cert).parent.mkdir(parents=True, exist_ok=True)
                Path(cert).write_bytes(b'lab-pfx')
                return {'ok': True}

            def run_build(_root, output_root):
                return {'ok': False, 'error': 'MakeAppx failed'}

            def run_sign(_root, output_root, cert):
                signed.append(cert)
                return {'ok': True, 'signed': True}

            doc = lab_msix_cli.record(
                root,
                create_certificate=create_certificate,
                run_build=run_build,
                run_sign=run_sign,
                git_sha='d' * 40,
                now='2026-09-11T02:00:00Z',
            )
            self.assertFalse(doc['lab_msix_packed'])
            self.assertFalse(doc['lab_signature_applied'])
            self.assertFalse(doc['signed_msix_built'])
            self.assertFalse(doc['ac41_passed'])
            self.assertIsNone(doc['lab_msix_sha256'])
            self.assertEqual(signed, [])
            report = validate_lab_msix(root)
            self.assertIsNotNone(report)
            self.assertFalse(report['lab_msix_packed'])
            self.assertFalse(report['lab_signature_applied'])

    def test_record_without_hooks_fails_closed_off_windows(self):
        if os.name == 'nt':
            self.skipTest('omitted hooks use live Windows pack and sign')
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            overlay = root / LAB_MSIX_REL
            err = StringIO()
            with patch.object(lab_msix_cli, 'ROOT', root), redirect_stderr(err):
                with self.assertRaises(LabMsixError) as ctx:
                    lab_msix_cli.record(root)
                code = lab_msix_cli.main(['--record'])
            self.assertEqual(str(ctx.exception), 'missing_record_field')
            self.assertEqual(code, 2, err.getvalue())
            self.assertEqual(json.loads(err.getvalue())['error'], 'missing_record_field')
            self.assertFalse(overlay.is_file())

    def test_ac41_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_lab_msix(root, ac41_passed=True)
            with self.assertRaises(LabMsixError) as ctx:
                validate_lab_msix(root)
            self.assertEqual(str(ctx.exception), 'ac41_passed')

    def test_signed_msix_built_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_lab_msix(root, signed_msix_built=True)
            with self.assertRaises(LabMsixError) as ctx:
                validate_lab_msix(root)
            self.assertEqual(str(ctx.exception), 'signed_msix_built_claimed')

    def test_live_sign_true_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_lab_msix(root, live_sign=True)
            with self.assertRaises(LabMsixError) as ctx:
                validate_lab_msix(root)
            self.assertEqual(str(ctx.exception), 'live_sign_claimed')

    def test_invented_msix_sha256_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_lab_msix(root, msix_sha256='b' * 64)
            with self.assertRaises(LabMsixError) as ctx:
                validate_lab_msix(root)
            self.assertEqual(str(ctx.exception), 'invented_identity')

    def test_packed_without_hash_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_lab_msix(root, lab_msix_sha256=None)
            with self.assertRaises(LabMsixError) as ctx:
                validate_lab_msix(root)
            self.assertEqual(str(ctx.exception), 'lab_msix_claimed')

    def test_signature_without_pack_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_lab_msix(
                root,
                lab_msix_packed=False,
                lab_signature_applied=True,
                lab_msix_sha256=None,
            )
            with self.assertRaises(LabMsixError) as ctx:
                validate_lab_msix(root)
            self.assertEqual(str(ctx.exception), 'lab_signature_claimed')

    def test_ci_and_justfile_do_not_record(self):
        ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
        just = (ROOT / 'justfile').read_text(encoding='utf-8')
        self.assertNotIn('record_lab_msix.py --record', ci)
        self.assertNotIn('record_lab_msix.py --record', just)
        self.assertNotIn('-Action Sign', ci)
        self.assertNotIn('-Action Sign', just)
        self.assertNotIn('new_lab_certificate.ps1', ci)
        self.assertNotIn('new_lab_certificate.ps1', just)


if __name__ == '__main__':
    unittest.main()

