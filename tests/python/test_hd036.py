from copy import deepcopy
from pathlib import Path
from unittest.mock import patch
import json
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.release import ADMITTED_BOUND_SHA, ReleaseError, bind_release_candidate
import herddesk_g0.release as release_mod
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-036-l2.json'
CATALOG = ROOT / 'evidence' / 'releases' / 'catalog.json'
MATRIX = ROOT / 'evidence' / 'releases' / 'support-matrix.json'
INDEX = ROOT / 'evidence' / 'releases' / 'ac-index.json'
ACCEPTANCE = ROOT / 'planning' / 'acceptance.json'
REQUIRED_CARDS = (
    'user-guide',
    'support-matrix',
    'ac48-trace',
    'ac39-clean-restore',
    'ac40-hosted-required-check',
    'ac47-dep-graph',
    'unpublished-candidate',
    'signed-hash-sbom',
)
REQUIRED_LIVE = (
    'live-user-guide', 'live-support-matrix', 'live-ac48-trace',
    'live-ac39-clean-restore', 'live-ac40-hosted-required-check',
    'live-ac47-dep-graph', 'live-unpublished-candidate',
    'live-signed-hash-sbom',
)
REQUIRED_SCENARIOS = (
    'user_guide', 'support_matrix', 'ac48_trace', 'ac39_clean_restore',
    'ac40_hosted_required_check', 'ac47_dep_graph', 'unpublished_candidate',
    'signed_hash_sbom',
)
REQUIRED_ACS = {'AC39', 'AC40', 'AC45', 'AC47', 'AC48'}
CARD_OWNERS = {
    'user-guide': ['HD-011', 'HD-016', 'HD-030', 'HD-031'],
    'support-matrix': ['HD-001', 'HD-026', 'HD-033', 'HD-034'],
    'ac48-trace': ['HD-026', 'HD-032', 'HD-033', 'HD-034', 'HD-035'],
    'ac39-clean-restore': ['HD-007'],
    'ac40-hosted-required-check': ['HD-007'],
    'ac47-dep-graph': ['HD-007'],
    'unpublished-candidate': ['HD-007'],
    'signed-hash-sbom': ['HD-034', 'HD-035'],
}
CARD_ACS = {
    'user-guide': ['AC45'],
    'support-matrix': ['AC45'],
    'ac48-trace': ['AC48'],
    'ac39-clean-restore': ['AC39'],
    'ac40-hosted-required-check': ['AC40'],
    'ac47-dep-graph': ['AC47'],
    'unpublished-candidate': ['AC48'],
    'signed-hash-sbom': ['AC45'],
}
CARD_GRANTS = {
    'user-guide': 'no_authorized_independent_user_walkthrough',
    'support-matrix': 'no_authorized_live_platform_matrix',
    'ac48-trace': 'no_authorized_final_candidate_sha_evidence_bind',
    'ac39-clean-restore': 'no_authorized_clean_machine_locked_restore',
    'ac40-hosted-required-check': 'no_authorized_github_required_check_on_head',
    'ac47-dep-graph': 'no_authorized_final_sha_module_graph_rerun',
    'unpublished-candidate': 'no_authorized_external_publish',
    'signed-hash-sbom': 'no_authorized_signed_msix_hash',
}
LIVE_GRANTS = {f'live-{key}': value for key, value in CARD_GRANTS.items()}
AC_FLAGS = ('ac39_passed', 'ac40_passed', 'ac45_passed', 'ac47_passed', 'ac48_passed')
L2_L3_L4_KEYS = (
    'l2_independent_user_walkthrough', 'l2_live_platform_matrix',
    'l2_final_candidate_sha_evidence_bind', 'l2_clean_machine_locked_restore',
    'l2_github_required_check_on_head', 'l2_final_sha_module_graph_rerun',
    'l2_external_publish', 'l3_signed_msix_hash', 'l4_complete_1_0_release',
)
SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
NULL_KEYS = (
    'package_sha256', 'msix_sha256', 'signed_package_sha256', 'sbom_sha256',
    'screenshot_sha256', 'screenshot_path', 'publisher', 'certificate_subject',
    'certificate_thumbprint', 'candidate_sha', 'hosted_check_run_id',
    'independent_reviewer',
)
AC_IDS = tuple(f'AC{i:02d}' for i in range(1, 49))


def _load():
    return (
        json.loads(L2.read_text(encoding='utf-8')),
        json.loads(CATALOG.read_text(encoding='utf-8')),
        json.loads(MATRIX.read_text(encoding='utf-8')),
        json.loads(INDEX.read_text(encoding='utf-8')),
    )


class Hd036ResidualTests(unittest.TestCase):
    def test_l2_l3_l4_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd036_l2_status')
        for key in L2_L3_L4_KEYS:
            self.assertEqual(doc[key], 'UNVERIFIED', key)
            self.assertNotEqual(doc[key], 'passed', key)
        for key in AC_FLAGS:
            self.assertFalse(doc[key], key)
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['published'])
        self.assertFalse(doc['complete_1_0_claimed'])
        self.assertFalse(doc['independent_user_walkthrough_executed'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['herdr_executed'])
        self.assertFalse(doc['screenshot_as_evidence'])
        self.assertFalse(doc['invented_github_required_check'])
        self.assertFalse(doc['impersonates_1_x_extensions'])
        self.assertEqual(doc['github_required_check'], 'UNVERIFIED')
        self.assertEqual(doc['windows_desktop_restore'], 'not_admitted')
        self.assertTrue(doc['markdown_link_is_not_test_evidence'])
        self.assertTrue(doc['core_1_0_does_not_impersonate_1_x_extensions'])
        self.assertTrue(doc['unpublished_candidate_is_not_published'])
        missing = doc['missing']
        self.assertTrue(missing['independent_user_walkthrough'])
        self.assertTrue(missing['github_required_check_on_head'])
        self.assertTrue(missing['external_publish'])
        self.assertTrue(missing['signed_msix_hash'])
        self.assertEqual(
            doc['hosted_workflow_pointer'],
            'evidence/releases/hosted-workflow-pointer.json',
        )
        repository.check_integration_windows_layout(ROOT)
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
        self.assertFalse(result['windows_verified'])
        self.assertFalse(result['ac02_passed'])

    def test_catalog_execution_cards_stay_not_run(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(catalog['document_kind'], 'hd036_release_docs_catalog')
        self.assertTrue(catalog['simulation'])
        self.assertEqual(catalog['fixture_origin'], 'synthetic')
        self.assertFalse(catalog['template'])
        for key in L2_L3_L4_KEYS:
            self.assertEqual(catalog[key], 'UNVERIFIED', key)
        for key in AC_FLAGS:
            self.assertFalse(catalog[key], key)
        self.assertFalse(catalog['g0_passed'])
        self.assertFalse(catalog['published'])
        self.assertFalse(catalog['complete_1_0_claimed'])
        self.assertFalse(catalog['independent_user_walkthrough_executed'])
        self.assertFalse(catalog['screenshot_as_evidence'])
        self.assertEqual(catalog['github_required_check'], 'UNVERIFIED')
        self.assertEqual(catalog['windows_desktop_restore'], 'not_admitted')
        self.assertEqual(
            catalog['hosted_workflow_pointer'],
            'evidence/releases/hosted-workflow-pointer.json',
        )
        pointer = json.loads(
            (ROOT / catalog['hosted_workflow_pointer']).read_text(encoding='utf-8')
        )
        self.assertEqual(pointer['document_kind'], 'hd036_hosted_workflow_pointer')
        self.assertEqual(pointer['result'], 'not_run')
        self.assertEqual(pointer['bound_sha'], 'd8347522d80ccbf190623f688ab1abe602a7b0f8')
        self.assertEqual(pointer['hosted_workflow_run_id'], '34549580783')
        self.assertEqual(pointer['hosted_workflow_conclusion'], 'success')
        self.assertEqual(pointer['github_required_check'], 'UNVERIFIED')
        self.assertNotIn(str(pointer['github_required_check']).lower(), SUCCESS)
        self.assertTrue(pointer['hosted_workflow_is_not_required_check_ruleset'])
        self.assertFalse(pointer['published'])
        self.assertFalse(pointer['complete_1_0_claimed'])
        self.assertFalse(pointer['ac40_passed'])
        self.assertFalse(pointer['g0_passed'])
        self.assertIsNone(pointer['candidate_sha'])
        self.assertIsNone(pointer['hosted_check_run_id'])
        self.assertIsNone(catalog['candidate_sha'])
        self.assertIsNone(catalog['hosted_check_run_id'])
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
            self.assertFalse(doc['published'])
            self.assertFalse(doc['complete_1_0_claimed'])
            self.assertFalse(doc['screenshot_as_evidence'])
            self.assertEqual(doc['github_required_check'], 'UNVERIFIED')
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
            self.assertFalse(doc['published'])
            self.assertIsNone(doc['host_fingerprint_redacted'])
            self.assertIsNone(doc['command_redacted'])
            blob = json.dumps(doc)
            self.assertNotIn('password', blob.lower())
            self.assertNotIn('.pfx', blob.lower())
            self.assertNotIn('stdout', doc)
            self.assertNotIn('stderr', doc)

    def test_ac_index_covers_ac01_to_ac48_as_not_run(self):
        index = json.loads(INDEX.read_text(encoding='utf-8'))
        planning = json.loads(ACCEPTANCE.read_text(encoding='utf-8'))
        self.assertEqual(index['document_kind'], 'hd036_ac_index')
        self.assertFalse(index['published'])
        self.assertFalse(index['complete_1_0_claimed'])
        self.assertTrue(index['trace_index_is_not_ac_pass_evidence'])
        for key in AC_FLAGS:
            self.assertFalse(index[key], key)
        planning_acs = {item['id']: item for item in planning['criteria']}
        entries = {item['id']: item for item in index['entries']}
        self.assertEqual(tuple(entries), AC_IDS)
        self.assertEqual(len(entries), 48)
        for ac_id in AC_IDS:
            entry = entries[ac_id]
            self.assertEqual(entry['status'], planning_acs[ac_id]['status'])
            self.assertEqual(entry['status'], 'not_run')
            self.assertNotEqual(entry['status'], 'passed')
            self.assertEqual(entry['live_status'], 'UNVERIFIED')
            self.assertEqual(entry['live_result'], 'not_run')
            self.assertTrue(entry['owner_children'])
            self.assertTrue(entry['evidence_class'])
            self.assertTrue(entry['live_residual'])
            self.assertIsNone(entry['candidate_sha'])
            self.assertIsNone(entry['product_hash'])
            self.assertIsNone(entry['independent_reviewer'])
            for rel in entry['l1_artifacts']:
                self.assertTrue((ROOT / rel).is_file(), rel)
            if entry['catalog']:
                self.assertTrue((ROOT / entry['catalog']).is_file(), entry['catalog'])
            if entry['l2_status_file']:
                self.assertTrue((ROOT / entry['l2_status_file']).is_file(), entry['l2_status_file'])
            for owner in entry['owner_children']:
                self.assertTrue((ROOT / 'tasks' / f'{owner}.md').is_file(), owner)
        for ac_id in ('AC39', 'AC40', 'AC45', 'AC47', 'AC48'):
            self.assertEqual(planning_acs[ac_id]['status'], 'not_run')

    def test_support_matrix_promised_rows_stay_not_run(self):
        matrix = json.loads(MATRIX.read_text(encoding='utf-8'))
        self.assertEqual(matrix['document_kind'], 'hd036_support_matrix')
        self.assertFalse(matrix['copy_windows_fields_onto_linux'])
        self.assertFalse(matrix['extrapolate_macos_arm64'])
        self.assertFalse(matrix['linux_msix_client'])
        self.assertTrue(matrix['linux_x64_is_not_windows_client_substitute'])
        self.assertTrue(matrix['core_1_0_does_not_impersonate_1_x_extensions'])
        self.assertFalse(matrix['impersonates_1_x_extensions'])
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
        self.assertEqual(linux['promise'], 'promised')
        self.assertEqual(linux['support'], 'promised_not_run')
        self.assertEqual(linux['role'], 'remote')
        self.assertFalse(linux['linux_msix'])
        self.assertEqual(linux['live_status'], 'not_run')
        self.assertNotIn(linux['support'], ('supported', 'stable', 'passed'))
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
            self.assertIn(cell['platform'], ('windows-11-x64-client', 'linux-x64-remote'))
            self.assertEqual(cell['live_status'], 'not_run')
            self.assertEqual(cell['live_result'], 'not_run')
            self.assertFalse(cell['compatible'])
            self.assertNotIn(cell['live_result'], SUCCESS)
        promised = {item['id'] for item in matrix['promised_range']}
        self.assertEqual(promised, {'windows-11-x64-client', 'linux-x64-remote'})
        self.assertNotEqual(linux['missing_grant'], windows['missing_grant'])
        self.assertEqual(
            tuple(matrix['extensions_out_of_core_1_0']),
            ('EP-01', 'EP-02', 'EP-03', 'EP-04', 'EP-05', 'EP-06', 'EP-07'),
        )

    def test_closeout_rejects_pass_claims_publish_and_screenshots(self):
        hd036, catalog, matrix, index = _load()
        repository._check_hd036_closeout(hd036, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['ac45_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['ac39_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['ac40_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['ac47_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['ac48_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['g0_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['published'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['complete_1_0_claimed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['l2_independent_user_walkthrough'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['l2_github_required_check_on_head'] = 'verified'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['l4_complete_1_0_release'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['github_required_check'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['invented_github_required_check'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['screenshot_as_evidence'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['package_sha256'] = 'abc'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['integration_windows_project'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['herdr_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(hd036)
        bad['impersonates_1_x_extensions'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(bad, catalog, matrix, index)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['live_result'] = 'Passed'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, bad, matrix, index)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['missing_grant'] = ''
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, bad, matrix, index)

        bad = deepcopy(catalog)
        for card in bad['execution_cards']:
            card['missing_grant'] = 'no_authorized_independent_user_walkthrough'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, bad, matrix, index)

        bad = deepcopy(catalog)
        bad['live_rows'][2]['result'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, bad, matrix, index)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['l1_artifacts'] = []
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, bad, matrix, index)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['required_evidence'] = 'L1'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, bad, matrix, index)

        bad = deepcopy(matrix)
        bad['platforms'][0]['support'] = 'supported'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, catalog, bad, index)

        bad = deepcopy(matrix)
        bad['copy_windows_fields_onto_linux'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, catalog, bad, index)

        bad = deepcopy(matrix)
        bad['ac45_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, catalog, bad, index)

        linux = deepcopy(matrix)
        linux['platforms'][1]['support'] = 'supported'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, catalog, linux, index)

        bad = deepcopy(index)
        bad['entries'][0]['status'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, catalog, matrix, bad)

        bad = deepcopy(index)
        bad['complete_1_0_claimed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, catalog, matrix, bad)

        soak = deepcopy(catalog)
        soak['live_rows'][0]['result'] = 'success'
        soak['live_rows'][0]['status'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd036_closeout(hd036, soak, matrix, index)


def _write_pointer(tmp: Path, **overrides) -> None:
    src = json.loads(
        (ROOT / 'evidence' / 'releases' / 'hosted-workflow-pointer.json').read_text(
            encoding='utf-8'
        )
    )
    src.update(overrides)
    dest = tmp / 'evidence' / 'releases' / 'hosted-workflow-pointer.json'
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(src), encoding='utf-8')


class Hd036ReleaseCandidateBindTests(unittest.TestCase):
    def test_shipped_function_binds_hosted_workflow_pointer(self):
        report = bind_release_candidate(ROOT)
        self.assertEqual(report['document_kind'], 'hd036_hosted_workflow_pointer')
        self.assertEqual(report['bound_sha'], 'd8347522d80ccbf190623f688ab1abe602a7b0f8')
        self.assertEqual(report['hosted_workflow_run_id'], '34549580783')
        self.assertEqual(
            report['hosted_workflow_url'],
            'https://github.com/bahayonghang/HerdrDesk/actions/runs/34549580783',
        )
        self.assertEqual(report['hosted_workflow_conclusion'], 'success')
        self.assertEqual(report['github_required_check'], 'UNVERIFIED')
        self.assertNotIn(str(report['github_required_check']).lower(), SUCCESS)
        self.assertTrue(report['hosted_workflow_is_not_required_check_ruleset'])
        self.assertTrue(report['hosted_actions_on_older_sha_is_not_head_proof'])
        self.assertTrue(report['local_just_ci_is_not_hosted_bar'])
        self.assertFalse(report['published'])
        self.assertFalse(report['complete_1_0_claimed'])
        self.assertFalse(report['ac39_passed'])
        self.assertFalse(report['ac40_passed'])
        self.assertFalse(report['ac45_passed'])
        self.assertFalse(report['ac47_passed'])
        self.assertFalse(report['ac48_passed'])
        self.assertFalse(report['g0_passed'])
        self.assertNotEqual(report['phase_gate'], 'passed')
        self.assertFalse(report['invented_github_required_check'])
        self.assertFalse(report['invented_package_hashes'])
        self.assertFalse(report['invented_sbom'])
        self.assertFalse(report['independent_user_walkthrough_executed'])
        self.assertFalse(report['signed_package_unpacked'])
        self.assertIsNone(report['package_sha256'])
        self.assertIsNone(report['msix_sha256'])
        self.assertIsNone(report['sbom_sha256'])
        self.assertIsNone(report['publisher'])
        self.assertIsNone(report['candidate_sha'])
        self.assertIsNone(report['hosted_check_run_id'])
        self.assertEqual(report['result'], 'not_run')
        self.assertIn('git_head', report)
        self.assertIn('head_equals_bound_sha', report)
        git_head = report.get('git_head')
        self.assertEqual(
            report['head_equals_bound_sha'],
            bool(git_head) and git_head == report['bound_sha'],
        )
        if git_head != report['bound_sha']:
            self.assertFalse(report['ac40_passed'])
            self.assertEqual(report['github_required_check'], 'UNVERIFIED')
        else:
            self.assertTrue(report['head_equals_bound_sha'])
            self.assertFalse(report['ac40_passed'])
            self.assertEqual(report['github_required_check'], 'UNVERIFIED')
            self.assertFalse(report['complete_1_0_claimed'])

    def test_cli_prints_one_json_object(self):
        script = ROOT / 'scripts' / 'bind_release_candidate.py'
        self.assertTrue(script.is_file())
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
        self.assertIsInstance(report, dict)
        self.assertEqual(report['document_kind'], 'hd036_hosted_workflow_pointer')
        self.assertEqual(report['bound_sha'], 'd8347522d80ccbf190623f688ab1abe602a7b0f8')
        self.assertEqual(report['hosted_workflow_run_id'], '34549580783')
        self.assertEqual(report['hosted_workflow_conclusion'], 'success')
        self.assertEqual(report['github_required_check'], 'UNVERIFIED')
        self.assertNotIn(str(report['github_required_check']).lower(), SUCCESS)
        self.assertTrue(report['hosted_workflow_is_not_required_check_ruleset'])
        self.assertFalse(report['published'])
        self.assertFalse(report['complete_1_0_claimed'])
        self.assertFalse(report['ac40_passed'])
        self.assertFalse(report['g0_passed'])
        self.assertFalse(report['signed_package_unpacked'])
        self.assertIsNone(report['package_sha256'])
        self.assertIsNone(report['msix_sha256'])
        self.assertIsNone(report['sbom_sha256'])
        self.assertIsNone(report['publisher'])
        self.assertIn('git_head', report)
        self.assertIn('head_equals_bound_sha', report)
        git_head = report.get('git_head')
        if git_head != report['bound_sha']:
            self.assertFalse(report['ac40_passed'])
            self.assertEqual(report['github_required_check'], 'UNVERIFIED')
        else:
            self.assertFalse(report['ac40_passed'])
            self.assertEqual(report['github_required_check'], 'UNVERIFIED')

    def test_matching_head_does_not_pass_ac40(self):
        with patch.object(release_mod, '_git_head', return_value=ADMITTED_BOUND_SHA):
            report = bind_release_candidate(ROOT)
        self.assertEqual(report['git_head'], ADMITTED_BOUND_SHA)
        self.assertTrue(report['head_equals_bound_sha'])
        self.assertFalse(report['ac40_passed'])
        self.assertEqual(report['github_required_check'], 'UNVERIFIED')
        self.assertFalse(report['published'])
        self.assertFalse(report['complete_1_0_claimed'])
        self.assertFalse(report['g0_passed'])
        self.assertEqual(report['result'], 'not_run')

    def test_structure_contract_invokes_shipped_binder(self):
        repository._check_hd036_release_candidate_bind()

    def test_missing_pointer_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ReleaseError) as ctx:
                bind_release_candidate(Path(tmp))
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_complete_1_0_claimed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_pointer(root, complete_1_0_claimed=True)
            with self.assertRaises(ReleaseError) as ctx:
                bind_release_candidate(root)
            self.assertEqual(str(ctx.exception), 'complete_1_0_claimed')

    def test_published_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_pointer(root, published=True)
            with self.assertRaises(ReleaseError) as ctx:
                bind_release_candidate(root)
            self.assertEqual(str(ctx.exception), 'published')

    def test_ac40_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_pointer(root, ac40_passed=True)
            with self.assertRaises(ReleaseError) as ctx:
                bind_release_candidate(root)
            self.assertEqual(str(ctx.exception), 'ac40_passed')

    def test_github_required_check_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_pointer(root, github_required_check=value)
                with self.assertRaises(ReleaseError) as ctx:
                    bind_release_candidate(root)
                self.assertEqual(str(ctx.exception), 'github_required_check_claimed')

    def test_result_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_pointer(root, result=value)
                with self.assertRaises(ReleaseError) as ctx:
                    bind_release_candidate(root)
                self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_invented_candidate_fields_fail_closed(self):
        for key, value in (('candidate_sha', 'abc'), ('hosted_check_run_id', '1')):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_pointer(root, **{key: value})
                with self.assertRaises(ReleaseError) as ctx:
                    bind_release_candidate(root)
                self.assertEqual(str(ctx.exception), 'invented_package_hashes')

    def test_g0_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_pointer(root, g0_passed=True)
            with self.assertRaises(ReleaseError) as ctx:
                bind_release_candidate(root)
            self.assertEqual(str(ctx.exception), 'g0_passed')


if __name__ == '__main__':
    unittest.main()
