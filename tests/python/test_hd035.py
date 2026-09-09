from copy import deepcopy
from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-035-l2.json'
CATALOG = ROOT / 'evidence' / 'security-release' / 'catalog.json'
MATRIX = ROOT / 'evidence' / 'security-release' / 'support-matrix.json'
INVENTORY = ROOT / 'evidence' / 'security-release' / 'inventory.json'
REGISTER = ROOT / 'docs' / 'licensing' / 'register.json'
REQUIRED_CARDS = (
    'license-inventory',
    'herdrm-not-copied',
    'nuget-scan',
    'cargo-scan',
    'npm-scan',
    'renderer-boundary',
    'diagnostic-canary',
    'signed-package-reverse-audit',
)
REQUIRED_LIVE = (
    'live-license-inventory', 'live-herdrm-not-copied', 'live-nuget-scan',
    'live-cargo-scan', 'live-npm-scan', 'live-renderer-boundary',
    'live-diagnostic-canary', 'live-signed-package-reverse-audit',
)
REQUIRED_SCENARIOS = (
    'license_inventory', 'herdrm_not_copied', 'nuget_scan', 'cargo_scan',
    'npm_scan', 'renderer_boundary', 'diagnostic_canary',
    'signed_package_reverse_audit',
)
REQUIRED_ACS = {'AC02', 'AC43', 'AC44'}
CARD_OWNERS = {
    'license-inventory': ['HD-002'],
    'herdrm-not-copied': ['HD-002', 'HD-006'],
    'nuget-scan': ['HD-007'],
    'cargo-scan': ['HD-008', 'HD-027'],
    'npm-scan': ['HD-014'],
    'renderer-boundary': ['HD-014', 'HD-020', 'HD-024'],
    'diagnostic-canary': ['HD-031'],
    'signed-package-reverse-audit': ['HD-032', 'HD-034'],
}
CARD_ACS = {
    'license-inventory': ['AC02'],
    'herdrm-not-copied': ['AC02'],
    'nuget-scan': ['AC43'],
    'cargo-scan': ['AC43'],
    'npm-scan': ['AC43'],
    'renderer-boundary': ['AC44'],
    'diagnostic-canary': ['AC44'],
    'signed-package-reverse-audit': ['AC43'],
}
CARD_GRANTS = {
    'license-inventory': 'no_authorized_maintainer_license_decision',
    'herdrm-not-copied': 'no_authorized_herdrm_reverse_audit_of_release_inputs',
    'nuget-scan': 'no_authorized_nuget_advisory_scan',
    'cargo-scan': 'no_authorized_cargo_advisory_scan',
    'npm-scan': 'no_authorized_npm_audit',
    'renderer-boundary': 'no_authorized_webview_process_observation',
    'diagnostic-canary': 'no_authorized_canary_diagnostic_export',
    'signed-package-reverse-audit': 'no_authorized_signed_package_unpack',
}
LIVE_GRANTS = {
    'live-license-inventory': 'no_authorized_maintainer_license_decision',
    'live-herdrm-not-copied': 'no_authorized_herdrm_reverse_audit_of_release_inputs',
    'live-nuget-scan': 'no_authorized_nuget_advisory_scan',
    'live-cargo-scan': 'no_authorized_cargo_advisory_scan',
    'live-npm-scan': 'no_authorized_npm_audit',
    'live-renderer-boundary': 'no_authorized_webview_process_observation',
    'live-diagnostic-canary': 'no_authorized_canary_diagnostic_export',
    'live-signed-package-reverse-audit': 'no_authorized_signed_package_unpack',
}
AC_FLAGS = ('ac02_passed', 'ac43_passed', 'ac44_passed')
L2_L3_KEYS = (
    'l2_nuget_scan', 'l2_cargo_advisory', 'l2_npm_audit',
    'l2_live_renderer_process', 'l2_canary_export', 'l2_signed_package_unpack',
    'l3_signed_package_reverse_audit',
)
SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
NULL_KEYS = (
    'scan_tool_version', 'advisory_database_date', 'nuget_scan_date',
    'cargo_advisory_date', 'npm_audit_date',
    'confirmed_exploitable_critical_high', 'package_sha256', 'msix_sha256',
    'signed_package_sha256', 'publisher', 'certificate_subject',
    'certificate_thumbprint', 'canary_export_sha256',
)


def _load():
    return (
        json.loads(L2.read_text(encoding='utf-8')),
        json.loads(CATALOG.read_text(encoding='utf-8')),
        json.loads(MATRIX.read_text(encoding='utf-8')),
        json.loads(INVENTORY.read_text(encoding='utf-8')),
    )


class Hd035ResidualTests(unittest.TestCase):
    def test_l2_l3_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd035_l2_status')
        for key in L2_L3_KEYS:
            self.assertEqual(doc[key], 'UNVERIFIED', key)
            self.assertNotEqual(doc[key], 'passed', key)
        for key in AC_FLAGS:
            self.assertFalse(doc[key], key)
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['nuget_scan_executed'])
        self.assertFalse(doc['cargo_advisory_executed'])
        self.assertFalse(doc['npm_audit_executed'])
        self.assertFalse(doc['live_renderer_process_observed'])
        self.assertFalse(doc['canary_export_executed'])
        self.assertFalse(doc['signed_package_unpacked'])
        self.assertFalse(doc['project_license_selected'])
        self.assertFalse(doc['herdrm_copied'])
        self.assertFalse(doc['final_unpacked_msix'])
        self.assertFalse(doc['nuget_lock_present'])
        self.assertFalse(doc['npm_lock_present'])
        self.assertTrue(doc['cargo_lock_present'])
        self.assertTrue(doc['public_visibility_is_not_license_grant'])
        self.assertTrue(doc['missing_scan_is_not_zero_vuln'])
        self.assertTrue(doc['confirmed_exploitable_critical_high_must_not_be_hidden_by_exception'])
        self.assertTrue(doc['linux_x64_is_not_windows_renderer_substitute'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['herdr_executed'])
        self.assertFalse(doc['invented_scan_dates'])
        self.assertFalse(doc['invented_zero_vuln'])
        missing = doc['missing']
        self.assertTrue(missing['maintainer_license_decision'])
        self.assertTrue(missing['nuget_advisory_scan'])
        self.assertTrue(missing['webview_process_observation'])
        self.assertTrue(missing['signed_package_unpack'])
        self.assertFalse((ROOT / 'tests' / 'Integration.Windows').exists())
        self.assertFalse((ROOT / 'web' / 'terminal' / 'package-lock.json').exists())
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
        self.assertFalse(result['windows_verified'])
        self.assertFalse(result['ac02_passed'])
        self.assertFalse(result['ac43_passed'])
        self.assertFalse(result['ac44_passed'])

    def test_catalog_execution_cards_stay_not_run(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(catalog['document_kind'], 'hd035_security_release_catalog')
        self.assertTrue(catalog['simulation'])
        self.assertEqual(catalog['fixture_origin'], 'synthetic')
        self.assertFalse(catalog['template'])
        for key in L2_L3_KEYS:
            self.assertEqual(catalog[key], 'UNVERIFIED', key)
        for key in AC_FLAGS:
            self.assertFalse(catalog[key], key)
        self.assertFalse(catalog['g0_passed'])
        self.assertFalse(catalog['nuget_scan_executed'])
        self.assertFalse(catalog['herdrm_copied'])
        self.assertFalse(catalog['project_license_selected'])
        self.assertFalse(catalog['signed_package_unpacked'])
        self.assertTrue(catalog['public_visibility_is_not_license_grant'])
        self.assertTrue(catalog['missing_scan_is_not_zero_vuln'])
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
            self.assertIsNone(doc['scan_tool_version'])
            self.assertIsNone(doc['advisory_database_date'])
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

    def test_inventory_points_at_register_and_stays_pending_or_blocked(self):
        inventory = json.loads(INVENTORY.read_text(encoding='utf-8'))
        register = json.loads(REGISTER.read_text(encoding='utf-8'))
        self.assertEqual(inventory['document_kind'], 'hd035_security_release_inventory')
        self.assertEqual(inventory['source_register'], 'docs/licensing/register.json')
        self.assertEqual(inventory['source_markdown'], 'docs/licensing-register.md')
        self.assertFalse(inventory['final_unpacked_msix'])
        self.assertFalse(inventory['signed_package_unpacked'])
        self.assertFalse(inventory['project_license_selected'])
        self.assertFalse(inventory['herdrm_copied'])
        self.assertFalse(inventory['ac02_passed'])
        self.assertTrue(inventory['inventory_is_not_final_unpacked_msix'])
        self.assertTrue(inventory['public_visibility_is_not_license_grant'])
        listed = [
            (item['name'], item['artifact_kind'], item['admission'])
            for item in inventory['units']
        ]
        expected = [
            (item['name'], item['artifact_kind'], item['admission'])
            for item in register['units']
        ]
        self.assertEqual(listed, expected)
        allowed_lock = {
            (item['name'], item['artifact_kind'])
            for item in register['units']
            if item.get('admission') == 'approved' and item.get('lock_allowed') is True
        }
        for name, kind, admission in listed:
            self.assertIn(admission, ('pending', 'blocked', 'approved'))
            if admission == 'approved':
                self.assertIn((name, kind), allowed_lock)
                self.assertEqual(kind, 'prebuilt_binary')
                self.assertNotEqual(name, 'herdrm')
            else:
                self.assertNotIn((name, kind), allowed_lock)
        herdrm = [item for item in inventory['units'] if item['name'] == 'herdrm']
        self.assertEqual(len(herdrm), 1)
        self.assertEqual(herdrm[0]['admission'], 'blocked')

    def test_support_matrix_promised_rows_stay_not_run(self):
        matrix = json.loads(MATRIX.read_text(encoding='utf-8'))
        self.assertEqual(matrix['document_kind'], 'hd035_support_matrix')
        self.assertFalse(matrix['copy_windows_fields_onto_linux'])
        self.assertFalse(matrix['extrapolate_macos_arm64'])
        self.assertFalse(matrix['linux_msix_client'])
        self.assertTrue(matrix['linux_x64_is_not_windows_renderer_substitute'])
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
        self.assertFalse(linux['linux_renderer_observation'])
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

    def test_closeout_rejects_pass_claims_fake_scans_and_public_visibility(self):
        hd035, catalog, matrix, inventory = _load()
        repository._check_hd035_closeout(hd035, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['ac02_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['ac43_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['ac44_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['g0_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['l2_nuget_scan'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['l2_live_renderer_process'] = 'verified'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['l2_canary_export'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['l3_signed_package_reverse_audit'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['nuget_scan_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['herdrm_copied'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['public_visibility_is_not_license_grant'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['missing_scan_is_not_zero_vuln'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['confirmed_exploitable_critical_high'] = 0
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['advisory_database_date'] = '2026-09-09'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['scan_tool_version'] = '1.0.0'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['invented_scan_dates'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['invented_zero_vuln'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['integration_windows_project'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['herdr_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['project_license_selected'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(hd035)
        bad['signed_package_unpacked'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(bad, catalog, matrix, inventory)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['live_result'] = 'Passed'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, bad, matrix, inventory)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['missing_grant'] = ''
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, bad, matrix, inventory)

        bad = deepcopy(catalog)
        for card in bad['execution_cards']:
            card['missing_grant'] = 'no_authorized_maintainer_license_decision'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, bad, matrix, inventory)

        bad = deepcopy(catalog)
        bad['live_rows'][2]['result'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, bad, matrix, inventory)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['l1_artifacts'] = []
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, bad, matrix, inventory)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['required_evidence'] = 'L1'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, bad, matrix, inventory)

        bad = deepcopy(matrix)
        bad['platforms'][0]['support'] = 'supported'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, catalog, bad, inventory)

        bad = deepcopy(matrix)
        bad['copy_windows_fields_onto_linux'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, catalog, bad, inventory)

        bad = deepcopy(matrix)
        bad['ac02_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, catalog, bad, inventory)

        linux = deepcopy(matrix)
        linux['platforms'][1]['support'] = 'promised_not_run'
        linux['platforms'][1]['promise'] = 'promised'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, catalog, linux, inventory)

        bad = deepcopy(inventory)
        bad['units'][0]['admission'] = 'approved'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, catalog, matrix, bad)

        bad = deepcopy(inventory)
        bad['final_unpacked_msix'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, catalog, matrix, bad)

        soak = deepcopy(catalog)
        soak['live_rows'][0]['result'] = 'success'
        soak['live_rows'][0]['status'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd035_closeout(hd035, soak, matrix, inventory)


if __name__ == '__main__':
    unittest.main()
