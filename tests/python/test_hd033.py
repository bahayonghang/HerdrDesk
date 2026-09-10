from copy import deepcopy
from pathlib import Path
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.quality import (
    AC37_STEP_KEYS,
    EMPTY_SHA256,
    QualityError,
    collect_dpi_overlay,
    collect_narrator_overlay,
    narrator_exe_path,
    system_dpi,
    validate_eight_hour_soak_start,
    validate_narrator_product_ui_launch,
)
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-033-l2.json'
CATALOG = ROOT / 'evidence' / 'quality' / 'catalog.json'
MATRIX = ROOT / 'evidence' / 'quality' / 'support-matrix.json'
REQUIRED_CARDS = (
    'cold-start',
    'input-to-visible-pixel',
    'search-p95',
    'working-set-1-4-pane',
    'hide-show-100',
    'narrator',
    'dpi-100-150-200',
    'eight-hour-soak',
)
REQUIRED_LIVE = (
    'live-cold-start', 'live-input-pixel', 'live-search-p95',
    'live-working-set', 'live-handle-reclaim', 'live-narrator',
    'live-dpi', 'live-soak',
)
REQUIRED_SCENARIOS = (
    'cold_start', 'input_to_visible_pixel', 'search_p95', 'working_set',
    'hide_show_100', 'narrator', 'dpi_theme', 'eight_hour_soak',
)
REQUIRED_ACS = {'AC27', 'AC28', 'AC29', 'AC37', 'AC38', 'AC46'}
CARD_OWNERS = {
    'cold-start': ['HD-011'],
    'input-to-visible-pixel': ['HD-014', 'HD-015'],
    'search-p95': ['HD-011', 'HD-023'],
    'working-set-1-4-pane': ['HD-025'],
    'hide-show-100': ['HD-025', 'HD-011'],
    'narrator': ['HD-011'],
    'dpi-100-150-200': ['HD-011', 'HD-014'],
    'eight-hour-soak': ['HD-025', 'HD-018'],
}
CARD_ACS = {
    'cold-start': ['AC28'],
    'input-to-visible-pixel': ['AC28'],
    'search-p95': ['AC28'],
    'working-set-1-4-pane': ['AC27', 'AC28'],
    'hide-show-100': ['AC29'],
    'narrator': ['AC37'],
    'dpi-100-150-200': ['AC38'],
    'eight-hour-soak': ['AC46'],
}
CARD_GRANTS = {
    'cold-start': 'no_authorized_interactive_desktop_cold_start',
    'input-to-visible-pixel': 'no_authorized_visible_pixel_latency_probe',
    'search-p95': 'no_authorized_live_search_p95',
    'working-set-1-4-pane': 'no_authorized_working_set_process_sample',
    'hide-show-100': 'no_authorized_pane_hide_show_handle_lab',
    'narrator': 'ac37_workflow_incomplete_no_live_session',
    'dpi-100-150-200': 'no_authorized_dpi_theme_monitor_matrix',
    'eight-hour-soak': 'eight_hour_wall_clock_incomplete_no_live_herdr_fault_injection',
}
LIVE_GRANTS = {
    'live-cold-start': 'no_authorized_interactive_desktop_cold_start',
    'live-input-pixel': 'no_authorized_visible_pixel_latency_probe',
    'live-search-p95': 'no_authorized_live_search_p95',
    'live-working-set': 'no_authorized_working_set_process_sample',
    'live-handle-reclaim': 'no_authorized_pane_hide_show_handle_lab',
    'live-narrator': 'ac37_workflow_incomplete_no_live_session',
    'live-dpi': 'no_authorized_dpi_theme_monitor_matrix',
    'live-soak': 'eight_hour_wall_clock_incomplete_no_live_herdr_fault_injection',
}
AC_FLAGS = (
    'ac27_passed', 'ac28_passed', 'ac29_passed', 'ac37_passed',
    'ac38_passed', 'ac46_passed',
)
L3_L4_KEYS = ('l3_ime', 'l3_narrator', 'l3_dpi', 'l4_soak')
SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})


def _load():
    return (
        json.loads(L2.read_text(encoding='utf-8')),
        json.loads(CATALOG.read_text(encoding='utf-8')),
        json.loads(MATRIX.read_text(encoding='utf-8')),
    )


class Hd033ResidualTests(unittest.TestCase):
    def test_l3_l4_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd033_l2_status')
        for key in L3_L4_KEYS:
            self.assertEqual(doc[key], 'UNVERIFIED', key)
            self.assertNotEqual(doc[key], 'passed', key)
        for key in AC_FLAGS:
            self.assertFalse(doc[key], key)
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['live_ime'])
        self.assertFalse(doc['live_narrator'])
        self.assertFalse(doc['live_dpi'])
        self.assertFalse(doc['live_soak'])
        self.assertFalse(doc['live_input_pixel'])
        self.assertFalse(doc['live_working_set'])
        self.assertFalse(doc['eight_hour_soak_executed'])
        self.assertFalse(doc['invented_timings'])
        self.assertTrue(doc['parser_consumed_is_not_presentation'])
        self.assertTrue(doc['parser_callback_cannot_pass_input_to_pixel'])
        self.assertFalse(doc['derive_process_memory_from_q_p'])
        self.assertEqual(doc['mib_bytes'], 1048576)
        self.assertFalse(doc['hosted_ci_is_interactive_desktop'])
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_ssh_project'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['herdr_executed'])
        self.assertEqual(doc['github_required_check'], 'UNVERIFIED')
        self.assertTrue(doc['l2_collectors_are_not_live_pass'])
        self.assertNotIn(str(doc.get('l2_collectors', '')).lower(), SUCCESS)
        self.assertIsNot(doc.get('complete_1_0_claimed'), True)
        missing = doc['missing']
        self.assertTrue(missing['interactive_desktop'])
        self.assertTrue(missing['visible_pixel_probe'])
        self.assertTrue(missing['working_set_lab'])
        self.assertTrue(missing['narrator_desktop'])
        self.assertTrue(missing['dpi_theme_matrix'])
        self.assertTrue(missing['eight_hour_soak'])
        self.assertTrue(missing['winui_shell'])
        repository.check_integration_windows_layout(ROOT)
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])

    def test_catalog_execution_cards_stay_not_run(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(catalog['document_kind'], 'hd033_performance_a11y_soak_catalog')
        self.assertTrue(catalog['simulation'])
        self.assertFalse(catalog['template'])
        for key in L3_L4_KEYS:
            self.assertEqual(catalog[key], 'UNVERIFIED', key)
        for key in AC_FLAGS:
            self.assertFalse(catalog[key], key)
        self.assertFalse(catalog['g0_passed'])
        self.assertFalse(catalog['eight_hour_soak_executed'])
        self.assertTrue(catalog['parser_consumed_is_not_presentation'])
        self.assertFalse(catalog['derive_process_memory_from_q_p'])
        self.assertEqual(catalog['mib_bytes'], 1048576)
        self.assertFalse(catalog['hosted_ci_is_interactive_desktop'])
        self.assertEqual(catalog['redaction']['credential'], 'omitted')
        self.assertEqual(catalog['redaction']['host'], 'omitted')
        self.assertEqual(catalog['github_required_check'], 'UNVERIFIED')
        self.assertTrue(catalog['l2_collectors_are_not_live_pass'])
        self.assertNotIn(str(catalog.get('l2_collectors', '')).lower(), SUCCESS)
        self.assertIsNot(catalog.get('complete_1_0_claimed'), True)
        self.assertEqual(
            catalog['environment_manifest_pointer'],
            'evidence/quality/environment-pointer.json',
        )
        self.assertEqual(
            catalog['narrator_overlay_pointer'],
            'evidence/quality/narrator-overlay-pointer.json',
        )
        self.assertEqual(
            catalog['narrator_product_ui_launch'],
            'evidence/quality/narrator-product-ui-launch.json',
        )
        self.assertEqual(
            catalog['dpi_overlay_pointer'],
            'evidence/quality/dpi-overlay-pointer.json',
        )
        self.assertEqual(
            catalog['soak_start_capture'],
            'evidence/quality/live-soak-start.json',
        )
        pointer = json.loads((ROOT / catalog['environment_manifest_pointer']).read_text(encoding='utf-8'))
        self.assertEqual(pointer['result'], 'not_run')
        self.assertEqual(pointer['live_status'], 'UNVERIFIED')
        self.assertFalse(pointer['committed_raw'])
        self.assertFalse(pointer['live_dpi'])
        self.assertFalse(pointer['live_narrator'])
        self.assertFalse(pointer['eight_hour_soak_executed'])
        self.assertEqual(pointer['github_required_check'], 'UNVERIFIED')
        self.assertTrue(pointer['l2_collectors_are_not_live_pass'])
        self.assertIsNot(pointer.get('complete_1_0_claimed'), True)
        overlay_pointer = json.loads(
            (ROOT / catalog['narrator_overlay_pointer']).read_text(encoding='utf-8')
        )
        self.assertEqual(overlay_pointer['document_kind'], 'hd033_narrator_overlay_pointer')
        self.assertEqual(overlay_pointer['result'], 'not_run')
        self.assertEqual(overlay_pointer['live_status'], 'UNVERIFIED')
        self.assertFalse(overlay_pointer['live_narrator'])
        self.assertFalse(overlay_pointer['ac37_passed'])
        self.assertEqual(overlay_pointer['l3_narrator'], 'UNVERIFIED')
        self.assertTrue(overlay_pointer['automation_names_are_not_screen_reader_evidence'])
        self.assertFalse(overlay_pointer['narrator_started_by_collector'])
        self.assertFalse(overlay_pointer['invented_timings'])
        self.assertFalse(overlay_pointer['committed_raw'])
        self.assertEqual(overlay_pointer['github_required_check'], 'UNVERIFIED')
        self.assertTrue(overlay_pointer['l2_collectors_are_not_live_pass'])
        self.assertIsNot(overlay_pointer.get('complete_1_0_claimed'), True)
        self.assertTrue((ROOT / 'scripts' / 'collect_narrator_overlay.py').is_file())
        self.assertTrue((ROOT / 'scripts' / 'record_narrator_product_ui_launch.py').is_file())
        launch = json.loads(
            (ROOT / catalog['narrator_product_ui_launch']).read_text(encoding='utf-8')
        )
        self.assertEqual(launch['document_kind'], 'hd033_narrator_product_ui_launch')
        self.assertEqual(launch['result'], 'not_run')
        self.assertFalse(launch['live_narrator'])
        self.assertFalse(launch['ac37_passed'])
        self.assertFalse(launch['ac37_workflow_completed'])
        self.assertEqual(launch['l3_narrator'], 'UNVERIFIED')
        self.assertTrue(launch['product_ui_started'])
        self.assertTrue(launch['narrator_started_by_this_run'])
        self.assertFalse(launch['narrator_started_by_collector'])
        self.assertFalse(launch['herdr_executed'])
        self.assertFalse(launch['g0_passed'])
        self.assertTrue(launch['automation_names_are_not_screen_reader_evidence'])
        self.assertEqual(launch['keyboard_chrome'], 'set_foreground_failed')
        for key in AC37_STEP_KEYS:
            self.assertEqual(launch['ac37_steps'][key], 'not_completed', key)
        dpi_overlay_pointer = json.loads(
            (ROOT / catalog['dpi_overlay_pointer']).read_text(encoding='utf-8')
        )
        self.assertEqual(dpi_overlay_pointer['document_kind'], 'hd033_dpi_overlay_pointer')
        self.assertEqual(dpi_overlay_pointer['result'], 'not_run')
        self.assertEqual(dpi_overlay_pointer['live_status'], 'UNVERIFIED')
        self.assertFalse(dpi_overlay_pointer['live_dpi'])
        self.assertFalse(dpi_overlay_pointer['ac38_passed'])
        self.assertEqual(dpi_overlay_pointer['l3_dpi'], 'UNVERIFIED')
        self.assertFalse(dpi_overlay_pointer['dpi_matrix_100_150_200_executed'])
        self.assertFalse(dpi_overlay_pointer['display_scale_changed_by_collector'])
        self.assertTrue(dpi_overlay_pointer['single_dpi_sample_is_not_matrix'])
        self.assertFalse(dpi_overlay_pointer['invented_timings'])
        self.assertFalse(dpi_overlay_pointer['committed_raw'])
        self.assertEqual(dpi_overlay_pointer['github_required_check'], 'UNVERIFIED')
        self.assertTrue(dpi_overlay_pointer['l2_collectors_are_not_live_pass'])
        self.assertIsNot(dpi_overlay_pointer.get('complete_1_0_claimed'), True)
        self.assertTrue((ROOT / 'scripts' / 'collect_dpi_overlay.py').is_file())
        self.assertTrue((ROOT / 'scripts' / 'start_eight_hour_soak.py').is_file())
        start = json.loads(
            (ROOT / catalog['soak_start_capture']).read_text(encoding='utf-8')
        )
        self.assertEqual(start['document_kind'], 'hd033_eight_hour_soak_start')
        self.assertEqual(start['result'], 'not_run')
        self.assertFalse(start['live_soak'])
        self.assertFalse(start['ac46_passed'])
        self.assertFalse(start['eight_hour_soak_executed'])
        self.assertIsNone(start['soak_hours'])
        self.assertEqual(start['l4_soak'], 'UNVERIFIED')
        self.assertTrue(start['product_ui_started'])
        self.assertFalse(start['herdr_executed'])
        self.assertFalse(start['g0_passed'])
        cards = {item['id']: item for item in catalog['execution_cards']}
        self.assertEqual(tuple(cards), REQUIRED_CARDS)
        seen = set()
        for card_id, card in cards.items():
            self.assertEqual(card['owner_children'], CARD_OWNERS[card_id], card_id)
            self.assertEqual(card['ac_ids'], CARD_ACS[card_id], card_id)
            self.assertEqual(card['missing_grant'], CARD_GRANTS[card_id], card_id)
            seen.update(card['ac_ids'])
        self.assertTrue(REQUIRED_ACS <= seen)
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
            for key in (
                'p95_ms', 'visible_pixel_ms', 'parser_consumed_ms', 'soak_hours',
                'working_set_bytes',
            ):
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
            self.assertNotIn('stdout', doc)
            self.assertNotIn('stderr', doc)

    def test_soak_capture_is_not_a_successful_run(self):
        soak = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak.not-run.json').read_text(encoding='utf-8'))
        self.assertEqual(soak['kind'], 'live_eight_hour_soak')
        self.assertEqual(soak['result'], 'not_run')
        self.assertFalse(soak['eight_hour_soak_executed'])
        self.assertIsNone(soak['soak_hours'])
        self.assertIsNone(soak['sample_count'])
        self.assertFalse(soak['invented_timings'])
        start = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(encoding='utf-8'))
        self.assertEqual(start['kind'], 'live_eight_hour_soak_start')
        self.assertEqual(start['result'], 'not_run')
        self.assertFalse(start['eight_hour_soak_executed'])
        self.assertIsNone(start['soak_hours'])
        self.assertFalse(start['live_soak'])
        self.assertFalse(start['ac46_passed'])
        self.assertTrue(start['product_ui_started'])
        template = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak.template.json').read_text(encoding='utf-8'))
        self.assertTrue(template['template'])
        self.assertFalse(template['eight_hour_soak_executed'])
        self.assertIsNone(template['soak_hours'])
        self.assertIsNone(template['result'])

    def test_parser_consumed_is_not_visible_pixel(self):
        pixel = json.loads((ROOT / 'evidence' / 'quality' / 'live-input-pixel.not-run.json').read_text(encoding='utf-8'))
        self.assertTrue(pixel['parser_consumed_is_not_presentation'])
        self.assertTrue(pixel['parser_callback_cannot_pass_input_to_pixel'])
        self.assertIsNone(pixel['visible_pixel_ms'])
        self.assertIsNone(pixel['parser_consumed_ms'])
        working = json.loads((ROOT / 'evidence' / 'quality' / 'live-working-set.not-run.json').read_text(encoding='utf-8'))
        self.assertFalse(working['derive_process_memory_from_q_p'])
        self.assertEqual(working['mib_bytes'], 1048576)
        self.assertIsNone(working['working_set_bytes'])
        self.assertIsNone(working['q_p_bytes'])

    def test_support_matrix_promised_rows_stay_not_run(self):
        matrix = json.loads(MATRIX.read_text(encoding='utf-8'))
        self.assertEqual(matrix['document_kind'], 'hd033_support_matrix')
        self.assertFalse(matrix['copy_windows_fields_onto_linux'])
        self.assertFalse(matrix['extrapolate_macos_arm64'])
        self.assertFalse(matrix['eight_hour_soak_executed'])
        self.assertTrue(matrix['parser_consumed_is_not_presentation'])
        self.assertEqual(matrix['mib_bytes'], 1048576)
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
            self.assertEqual(scenario['live_result'], 'not_run')
            self.assertNotIn(scenario['live_result'], SUCCESS)
        for cell in matrix['cells']:
            self.assertEqual(cell['live_status'], 'not_run')
            self.assertEqual(cell['live_result'], 'not_run')
            self.assertFalse(cell['compatible'])
            self.assertNotIn(cell['live_result'], SUCCESS)
        linux = platforms['linux-x64-remote']
        windows = platforms['windows-11-x64-client']
        self.assertNotEqual(linux['missing_grant'], windows['missing_grant'])

    def test_closeout_rejects_pass_claims_and_fake_soak(self):
        hd033, catalog, matrix = _load()
        repository._check_hd033_closeout(hd033, catalog, matrix)

        bad = deepcopy(hd033)
        bad['ac27_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['ac28_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['ac29_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['l4_soak'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['l3_narrator'] = 'verified'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['l3_dpi'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['g0_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['eight_hour_soak_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['parser_consumed_is_not_presentation'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['derive_process_memory_from_q_p'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['mib_bytes'] = 1000000
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['hosted_ci_is_interactive_desktop'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['herdr_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['github_required_check'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['l2_collectors_are_not_live_pass'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['l2_collectors'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['complete_1_0_claimed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(hd033)
        bad['integration_windows_project'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(bad, catalog, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['live_result'] = 'Passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['missing_grant'] = ''
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['live_rows'][7]['result'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['eight_hour_soak_executed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['p95_ms'] = 90
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['soak_hours'] = 8
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][1]['live_result'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['l1_artifacts'] = []
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['execution_cards'][0]['required_evidence'] = 'L1'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(matrix)
        bad['platforms'][0]['support'] = 'supported'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, catalog, bad)

        bad = deepcopy(matrix)
        bad['copy_windows_fields_onto_linux'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, catalog, bad)

        bad = deepcopy(matrix)
        bad['ac27_passed'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, catalog, bad)

        soak = deepcopy(catalog)
        soak['live_rows'][7]['result'] = 'success'
        soak['live_rows'][7]['status'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, soak, matrix)

        bad = deepcopy(catalog)
        bad.pop('narrator_overlay_pointer')
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad.pop('narrator_product_ui_launch')
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['narrator_overlay_pointer'] = 'evidence/quality/environment-pointer.json'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad.pop('dpi_overlay_pointer')
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['dpi_overlay_pointer'] = 'evidence/quality/environment-pointer.json'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad.pop('soak_start_capture')
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['soak_start_capture'] = 'evidence/quality/live-soak.not-run.json'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)


def _write_overlay(tmp: Path, *, pointer_overrides=None, live_overrides=None) -> None:
    quality = tmp / 'evidence' / 'quality'
    quality.mkdir(parents=True, exist_ok=True)
    pointer = json.loads(
        (ROOT / 'evidence' / 'quality' / 'narrator-overlay-pointer.json').read_text(
            encoding='utf-8'
        )
    )
    live = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-narrator.not-run.json').read_text(
            encoding='utf-8'
        )
    )
    if pointer_overrides:
        pointer.update(pointer_overrides)
    if live_overrides:
        live.update(live_overrides)
    (quality / 'narrator-overlay-pointer.json').write_text(
        json.dumps(pointer), encoding='utf-8'
    )
    (quality / 'live-narrator.not-run.json').write_text(
        json.dumps(live), encoding='utf-8'
    )


class Hd033NarratorOverlayTests(unittest.TestCase):
    def test_shipped_function_records_presence_not_ac37(self):
        report = collect_narrator_overlay(ROOT)
        self.assertEqual(report['document_kind'], 'hd033_narrator_overlay')
        exe = Path(
            os.environ.get('SystemRoot') or os.environ.get('WINDIR') or r'C:\Windows'
        ) / 'System32' / 'Narrator.exe'
        self.assertEqual(narrator_exe_path(), exe)
        self.assertEqual(narrator_exe_path().name, 'Narrator.exe')
        self.assertEqual(narrator_exe_path().parent.name, 'System32')
        collector = (
            ROOT / 'src' / 'HerdDesk.Infrastructure' / 'Quality'
            / 'EnvironmentManifestCollector.cs'
        ).read_text(encoding='utf-8')
        self.assertIn('SpecialFolder.System', collector)
        self.assertIn('Narrator.exe', collector)
        self.assertIn('NarratorStartedByCollector', collector)
        self.assertEqual(report['narrator_exe_present'], exe.is_file())
        self.assertIsInstance(report['narrator_launched'], bool)
        self.assertFalse(report['narrator_started_by_collector'])
        self.assertTrue(report['automation_names_are_not_screen_reader_evidence'])
        self.assertFalse(report['ac37_passed'])
        self.assertFalse(report['live_narrator'])
        self.assertEqual(report['result'], 'not_run')
        self.assertNotIn(str(report['result']).lower(), SUCCESS)
        self.assertEqual(report['l3_narrator'], 'UNVERIFIED')
        self.assertFalse(report['g0_passed'])
        self.assertNotEqual(report['phase_gate'], 'passed')
        self.assertFalse(report['herdr_executed'])
        self.assertFalse(report['invented_timings'])
        self.assertEqual(report['pointer'], 'evidence/quality/narrator-overlay-pointer.json')
        self.assertEqual(
            report['live_capture'], 'evidence/quality/live-narrator.not-run.json'
        )
        live = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-narrator.not-run.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(live['result'], 'not_run')
        self.assertFalse(live['live_narrator'])

    def test_cli_prints_one_json_object(self):
        script = ROOT / 'scripts' / 'collect_narrator_overlay.py'
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
        self.assertEqual(report['document_kind'], 'hd033_narrator_overlay')
        self.assertFalse(report['ac37_passed'])
        self.assertFalse(report['live_narrator'])
        self.assertEqual(report['result'], 'not_run')
        self.assertEqual(report['l3_narrator'], 'UNVERIFIED')
        self.assertTrue(report['automation_names_are_not_screen_reader_evidence'])
        self.assertFalse(report['narrator_started_by_collector'])
        self.assertFalse(report['g0_passed'])

    def test_overlay_collector_does_not_start_narrator(self):
        src = (ROOT / 'scripts' / 'collect_narrator_overlay.py').read_text(
            encoding='utf-8'
        )
        self.assertNotIn('Start-Process', src)
        self.assertNotIn('ShellExecute', src)
        self.assertNotIn('--record', src)

    def test_structure_contract_invokes_shipped_overlay(self):
        repository._check_hd033_narrator_overlay()

    def test_missing_pointer_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(Path(tmp))
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_ac37_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_overlay(root, pointer_overrides={'ac37_passed': True})
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(root)
            self.assertEqual(str(ctx.exception), 'ac37_passed')

    def test_live_narrator_true_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_overlay(root, live_overrides={'live_narrator': True})
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(root)
            self.assertEqual(str(ctx.exception), 'live_narrator_claimed')

    def test_live_narrator_result_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_overlay(root, live_overrides={'result': value})
                with self.assertRaises(QualityError) as ctx:
                    collect_narrator_overlay(root)
                self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_pointer_result_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_overlay(root, pointer_overrides={'result': value})
                with self.assertRaises(QualityError) as ctx:
                    collect_narrator_overlay(root)
                self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_l3_narrator_pass_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_overlay(root, pointer_overrides={'l3_narrator': 'passed'})
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(root)
            self.assertEqual(str(ctx.exception), 'l3_narrator_claimed')

    def test_collector_started_narrator_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_overlay(root, pointer_overrides={'narrator_started_by_collector': True})
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(root)
            self.assertEqual(str(ctx.exception), 'collector_started_narrator')

    def test_automation_names_as_evidence_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_overlay(
                root,
                pointer_overrides={
                    'automation_names_are_not_screen_reader_evidence': False,
                },
            )
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(root)
            self.assertEqual(str(ctx.exception), 'automation_names_claimed_as_evidence')

    def test_g0_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_overlay(root, pointer_overrides={'g0_passed': True})
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(root)
            self.assertEqual(str(ctx.exception), 'g0_passed')

    def test_live_ac37_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_overlay(root, live_overrides={'ac37_passed': True})
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(root)
            self.assertEqual(str(ctx.exception), 'ac37_passed')

    def test_live_narrator_success_token_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass'):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_overlay(root, live_overrides={'live_narrator': value})
                with self.assertRaises(QualityError) as ctx:
                    collect_narrator_overlay(root)
                self.assertEqual(str(ctx.exception), 'live_narrator_claimed')

    def test_acceptance_ac37_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_overlay(root)
            planning = root / 'planning'
            planning.mkdir()
            (planning / 'acceptance.json').write_text(
                json.dumps({'criteria': [{'id': 'AC37', 'status': 'passed'}]}),
                encoding='utf-8',
            )
            with self.assertRaises(QualityError) as ctx:
                collect_narrator_overlay(root)
            self.assertEqual(str(ctx.exception), 'ac37_passed')


def _write_launch(tmp: Path, *, launch_overrides=None, pointer_overrides=None, live_overrides=None) -> None:
    quality = tmp / 'evidence' / 'quality'
    quality.mkdir(parents=True, exist_ok=True)
    launch = json.loads(
        (ROOT / 'evidence' / 'quality' / 'narrator-product-ui-launch.json').read_text(
            encoding='utf-8'
        )
    )
    pointer = json.loads(
        (ROOT / 'evidence' / 'quality' / 'narrator-overlay-pointer.json').read_text(
            encoding='utf-8'
        )
    )
    live = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-narrator.not-run.json').read_text(
            encoding='utf-8'
        )
    )
    if launch_overrides:
        launch.update(launch_overrides)
    if pointer_overrides:
        pointer.update(pointer_overrides)
    if live_overrides:
        live.update(live_overrides)
    (quality / 'narrator-product-ui-launch.json').write_text(
        json.dumps(launch), encoding='utf-8'
    )
    (quality / 'narrator-overlay-pointer.json').write_text(
        json.dumps(pointer), encoding='utf-8'
    )
    (quality / 'live-narrator.not-run.json').write_text(
        json.dumps(live), encoding='utf-8'
    )


class Hd033NarratorProductUiLaunchTests(unittest.TestCase):
    def test_shipped_launch_record_is_not_ac37(self):
        report = validate_narrator_product_ui_launch(ROOT)
        self.assertEqual(report['document_kind'], 'hd033_narrator_product_ui_launch')
        self.assertTrue(report['product_ui_started'])
        self.assertTrue(report['narrator_started_by_this_run'])
        self.assertFalse(report['narrator_started_by_collector'])
        self.assertTrue(report['automation_names_are_not_screen_reader_evidence'])
        self.assertFalse(report['ac37_passed'])
        self.assertFalse(report['ac37_workflow_completed'])
        self.assertFalse(report['live_narrator'])
        self.assertEqual(report['result'], 'not_run')
        self.assertNotIn(str(report['result']).lower(), SUCCESS)
        self.assertEqual(report['l3_narrator'], 'UNVERIFIED')
        self.assertFalse(report['g0_passed'])
        launch = json.loads(
            (ROOT / 'evidence' / 'quality' / 'narrator-product-ui-launch.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(launch['keyboard_chrome'], 'set_foreground_failed')
        for key in AC37_STEP_KEYS:
            self.assertEqual(launch['ac37_steps'][key], 'not_completed', key)
        overlay = collect_narrator_overlay(ROOT)
        self.assertFalse(overlay['ac37_passed'])
        self.assertFalse(overlay['live_narrator'])
        self.assertFalse(overlay['narrator_started_by_collector'])
        self.assertEqual(overlay['result'], 'not_run')
        ui = next(
            item for item in launch['commands'] if item.get('role') == 'product_ui'
        )
        for key, path_key in (
            ('stdout_sha256', 'stdout_gitignored_path'),
            ('stderr_sha256', 'stderr_gitignored_path'),
        ):
            digest = ui[key]
            path = ROOT / ui[path_key]
            self.assertEqual(len(digest), 64)
            if path.is_file():
                actual = hashlib.sha256(path.read_bytes()).hexdigest()
                self.assertEqual(actual, digest, path_key)
                if digest == EMPTY_SHA256:
                    self.assertEqual(path.stat().st_size, 0, path_key)

    def test_cli_validates_without_starting_processes(self):
        script = ROOT / 'scripts' / 'record_narrator_product_ui_launch.py'
        self.assertTrue(script.is_file())
        src = script.read_text(encoding='utf-8')
        self.assertIn('--record', src)
        self.assertIn('--ui', src)
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
        self.assertFalse(report['ac37_passed'])
        self.assertFalse(report['live_narrator'])
        self.assertEqual(report['result'], 'not_run')
        self.assertTrue(report['product_ui_started'])
        self.assertTrue(report['narrator_started_by_this_run'])

    def test_structure_contract_invokes_launch_record(self):
        repository._check_hd033_narrator_product_ui_launch()

    def test_missing_launch_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(Path(tmp))
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_ac37_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(root, launch_overrides={'ac37_passed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'ac37_passed')

    def test_live_narrator_true_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(root, launch_overrides={'live_narrator': True})
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'live_narrator_claimed')

    def test_result_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_launch(root, launch_overrides={'result': value})
                with self.assertRaises(QualityError) as ctx:
                    validate_narrator_product_ui_launch(root)
                self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_l3_narrator_pass_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(root, launch_overrides={'l3_narrator': 'passed'})
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'l3_narrator_claimed')

    def test_workflow_completed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(root, launch_overrides={'ac37_workflow_completed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'ac37_workflow_claimed')

    def test_collector_started_narrator_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(root, launch_overrides={'narrator_started_by_collector': True})
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'collector_started_narrator')

    def test_search_step_completed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(
                root,
                launch_overrides={
                    'ac37_steps': {
                        'search': 'completed',
                        'request_control': 'not_completed',
                        'release': 'not_completed',
                        'close_confirm': 'not_completed',
                    }
                },
            )
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'ac37_workflow_claimed')

    def test_overlay_live_narrator_true_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(root, live_overrides={'live_narrator': True})
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'live_narrator_claimed')

    def test_g0_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(root, launch_overrides={'g0_passed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'g0_passed')

    def test_keyboard_chrome_success_fails_closed(self):
        for value in ('success', 'passed', 'ok', 'completed'):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_launch(root, launch_overrides={'keyboard_chrome': value})
                with self.assertRaises(QualityError) as ctx:
                    validate_narrator_product_ui_launch(root)
                self.assertEqual(str(ctx.exception), 'ac37_workflow_claimed')

    def test_empty_stdout_sha_rejected_when_file_nonempty(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_launch(root)
            probe = root / 'probe-results'
            probe.mkdir(parents=True, exist_ok=True)
            (probe / 'hd033-narrator-product-ui-stdout.txt').write_bytes(b'not-empty')
            (probe / 'hd033-narrator-product-ui-stderr.txt').write_bytes(b'')
            with self.assertRaises(QualityError) as ctx:
                validate_narrator_product_ui_launch(root)
            self.assertEqual(str(ctx.exception), 'invented_hash')


def _write_dpi_overlay(tmp: Path, *, pointer_overrides=None, live_overrides=None) -> None:
    quality = tmp / 'evidence' / 'quality'
    quality.mkdir(parents=True, exist_ok=True)
    pointer = json.loads(
        (ROOT / 'evidence' / 'quality' / 'dpi-overlay-pointer.json').read_text(
            encoding='utf-8'
        )
    )
    live = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-dpi.not-run.json').read_text(
            encoding='utf-8'
        )
    )
    if pointer_overrides:
        pointer.update(pointer_overrides)
    if live_overrides:
        live.update(live_overrides)
    (quality / 'dpi-overlay-pointer.json').write_text(
        json.dumps(pointer), encoding='utf-8'
    )
    (quality / 'live-dpi.not-run.json').write_text(
        json.dumps(live), encoding='utf-8'
    )


class Hd033DpiOverlayTests(unittest.TestCase):
    def test_shipped_function_records_current_dpi_not_ac38(self):
        report = collect_dpi_overlay(ROOT)
        self.assertEqual(report['document_kind'], 'hd033_dpi_overlay')
        collector = (
            ROOT / 'src' / 'HerdDesk.Infrastructure' / 'Quality'
            / 'EnvironmentManifestCollector.cs'
        ).read_text(encoding='utf-8')
        quality_src = (ROOT / 'scripts' / 'herddesk_g0' / 'quality.py').read_text(
            encoding='utf-8'
        )
        self.assertIn('GetDpiForSystem', collector)
        self.assertIn('system_dpi', collector)
        self.assertIn('GetDpiForSystem', quality_src)
        dpi = system_dpi()
        if os.name == 'nt':
            self.assertIsInstance(dpi, int)
            self.assertGreater(dpi, 0)
            self.assertEqual(report['system_dpi'], dpi)
        else:
            self.assertIsNone(dpi)
            self.assertIsNone(report['system_dpi'])
        self.assertTrue(report['single_dpi_sample_is_not_matrix'])
        self.assertFalse(report['display_scale_changed_by_collector'])
        self.assertFalse(report['dpi_matrix_100_150_200_executed'])
        self.assertFalse(report['ac38_passed'])
        self.assertFalse(report['live_dpi'])
        self.assertEqual(report['result'], 'not_run')
        self.assertNotIn(str(report['result']).lower(), SUCCESS)
        self.assertEqual(report['l3_dpi'], 'UNVERIFIED')
        self.assertFalse(report['g0_passed'])
        self.assertNotEqual(report['phase_gate'], 'passed')
        self.assertFalse(report['herdr_executed'])
        self.assertFalse(report['invented_timings'])
        self.assertNotIn('resize_rate', report)
        self.assertEqual(report['pointer'], 'evidence/quality/dpi-overlay-pointer.json')
        self.assertEqual(
            report['live_capture'], 'evidence/quality/live-dpi.not-run.json'
        )
        live = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-dpi.not-run.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(live['result'], 'not_run')
        self.assertFalse(live['live_dpi'])
        self.assertIsNone(live['resize_rate'])

    def test_cli_prints_one_json_object(self):
        script = ROOT / 'scripts' / 'collect_dpi_overlay.py'
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
        self.assertEqual(report['document_kind'], 'hd033_dpi_overlay')
        self.assertFalse(report['ac38_passed'])
        self.assertFalse(report['live_dpi'])
        self.assertEqual(report['result'], 'not_run')
        self.assertEqual(report['l3_dpi'], 'UNVERIFIED')
        self.assertTrue(report['single_dpi_sample_is_not_matrix'])
        self.assertFalse(report['display_scale_changed_by_collector'])
        self.assertFalse(report['dpi_matrix_100_150_200_executed'])
        self.assertFalse(report['g0_passed'])
        if os.name == 'nt':
            self.assertIsInstance(report['system_dpi'], int)
            self.assertGreater(report['system_dpi'], 0)
        else:
            self.assertIsNone(report['system_dpi'])

    def test_structure_contract_invokes_shipped_overlay(self):
        repository._check_hd033_dpi_overlay()

    def test_missing_pointer_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(Path(tmp))
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_ac38_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(root, pointer_overrides={'ac38_passed': True})
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'ac38_passed')

    def test_live_dpi_true_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(root, live_overrides={'live_dpi': True})
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'live_dpi_claimed')

    def test_live_dpi_result_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_dpi_overlay(root, live_overrides={'result': value})
                with self.assertRaises(QualityError) as ctx:
                    collect_dpi_overlay(root)
                self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_pointer_result_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_dpi_overlay(root, pointer_overrides={'result': value})
                with self.assertRaises(QualityError) as ctx:
                    collect_dpi_overlay(root)
                self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_l3_dpi_pass_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(root, pointer_overrides={'l3_dpi': 'passed'})
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'l3_dpi_claimed')

    def test_collector_changed_display_scale_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(
                root, pointer_overrides={'display_scale_changed_by_collector': True}
            )
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'collector_changed_display_scale')

    def test_dpi_matrix_executed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(
                root, pointer_overrides={'dpi_matrix_100_150_200_executed': True}
            )
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'dpi_matrix_claimed')

    def test_single_sample_claimed_as_matrix_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(
                root,
                pointer_overrides={'single_dpi_sample_is_not_matrix': False},
            )
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'single_sample_claimed_as_matrix')

    def test_g0_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(root, pointer_overrides={'g0_passed': True})
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'g0_passed')

    def test_live_ac38_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(root, live_overrides={'ac38_passed': True})
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'ac38_passed')

    def test_live_dpi_success_token_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass'):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_dpi_overlay(root, live_overrides={'live_dpi': value})
                with self.assertRaises(QualityError) as ctx:
                    collect_dpi_overlay(root)
                self.assertEqual(str(ctx.exception), 'live_dpi_claimed')

    def test_acceptance_ac38_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_dpi_overlay(root)
            planning = root / 'planning'
            planning.mkdir()
            (planning / 'acceptance.json').write_text(
                json.dumps({'criteria': [{'id': 'AC38', 'status': 'passed'}]}),
                encoding='utf-8',
            )
            with self.assertRaises(QualityError) as ctx:
                collect_dpi_overlay(root)
            self.assertEqual(str(ctx.exception), 'ac38_passed')


def _write_soak_start(
    tmp: Path, *, start_overrides=None, live_overrides=None
) -> None:
    quality = tmp / 'evidence' / 'quality'
    quality.mkdir(parents=True, exist_ok=True)
    start = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
            encoding='utf-8'
        )
    )
    live = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak.not-run.json').read_text(
            encoding='utf-8'
        )
    )
    if start_overrides:
        start.update(start_overrides)
    if live_overrides:
        live.update(live_overrides)
    (quality / 'live-soak-start.json').write_text(
        json.dumps(start), encoding='utf-8'
    )
    (quality / 'live-soak.not-run.json').write_text(
        json.dumps(live), encoding='utf-8'
    )


class Hd033SoakStartTests(unittest.TestCase):
    def test_shipped_start_record_is_not_ac46(self):
        report = validate_eight_hour_soak_start(ROOT)
        self.assertEqual(report['document_kind'], 'hd033_eight_hour_soak_start')
        self.assertTrue(report['product_ui_started'])
        self.assertFalse(report['eight_hour_soak_executed'])
        self.assertIsNone(report['soak_hours'])
        self.assertFalse(report['live_soak'])
        self.assertFalse(report['ac46_passed'])
        self.assertEqual(report['result'], 'not_run')
        self.assertNotIn(str(report['result']).lower(), SUCCESS)
        self.assertEqual(report['l4_soak'], 'UNVERIFIED')
        self.assertFalse(report['g0_passed'])
        start = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertTrue(start['window_seen'])
        self.assertIsInstance(start['started_at_utc'], str)
        self.assertGreaterEqual(len(start['git_sha']), 7)
        ui = next(
            item for item in start['commands'] if item.get('role') == 'product_ui'
        )
        argv = [str(part) for part in ui.get('command_redacted') or []]
        self.assertIn('--ui', argv)
        self.assertNotIn('--shell-smoke', argv)
        self.assertNotIn('--compose-only', argv)
        self.assertIsInstance(ui['pid'], int)
        self.assertGreater(ui['pid'], 0)
        live = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak.not-run.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(live['result'], 'not_run')
        self.assertFalse(live['eight_hour_soak_executed'])
        self.assertIsNone(live['soak_hours'])
        self.assertIsNone(live['disconnect_switch_count'])
        self.assertIsNone(start['disconnect_switch_count'])

    def test_cli_validates_without_starting_processes(self):
        script = ROOT / 'scripts' / 'start_eight_hour_soak.py'
        self.assertTrue(script.is_file())
        src = script.read_text(encoding='utf-8')
        self.assertIn('--record', src)
        self.assertIn('--ui', src)
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
        self.assertFalse(report['ac46_passed'])
        self.assertFalse(report['live_soak'])
        self.assertFalse(report['eight_hour_soak_executed'])
        self.assertIsNone(report['soak_hours'])
        self.assertEqual(report['result'], 'not_run')
        self.assertTrue(report['product_ui_started'])

    def test_structure_contract_invokes_start_record(self):
        repository._check_hd033_soak_start()

    def test_missing_start_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(Path(tmp))
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_ac46_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'ac46_passed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'ac46_passed')

    def test_live_soak_true_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'live_soak': True})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'live_soak_claimed')

    def test_eight_hour_executed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'eight_hour_soak_executed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'eight_hour_soak_executed')

    def test_soak_hours_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'soak_hours': 8})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'invented_timings')

    def test_result_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_soak_start(root, start_overrides={'result': value})
                with self.assertRaises(QualityError) as ctx:
                    validate_eight_hour_soak_start(root)
                self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_l4_soak_pass_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'l4_soak': 'passed'})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'l4_soak_claimed')

    def test_shell_smoke_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            start = json.loads(
                (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            start['commands'][0]['command_redacted'] = [
                'dotnet', 'run', '--', '--shell-smoke'
            ]
            _write_soak_start(root, start_overrides=start)
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'not_product_ui')

    def test_compose_only_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            start = json.loads(
                (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            start['commands'][0]['command_redacted'] = [
                'dotnet', 'run', '--', '--compose-only', '<temp-root>'
            ]
            _write_soak_start(root, start_overrides=start)
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'not_product_ui')

    def test_product_ui_not_started_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'product_ui_started': False})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'product_ui_not_started')

    def test_live_row_success_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, live_overrides={'result': 'success'})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_g0_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'g0_passed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'g0_passed')

    def test_acceptance_ac46_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            planning = root / 'planning'
            planning.mkdir()
            (planning / 'acceptance.json').write_text(
                json.dumps({'criteria': [{'id': 'AC46', 'status': 'passed'}]}),
                encoding='utf-8',
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'ac46_passed')

    def test_disconnect_switch_count_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'disconnect_switch_count': 100})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'invented_timings')

    def test_live_eight_hour_executed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, live_overrides={'eight_hour_soak_executed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'eight_hour_soak_executed')

    def test_live_soak_hours_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, live_overrides={'soak_hours': 8})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'invented_timings')

    def test_growing_soak_logs_are_not_invented_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            probe = root / 'probe-results'
            probe.mkdir(parents=True, exist_ok=True)
            (probe / 'hd033-soak-stdout.txt').write_bytes(b'later-log')
            (probe / 'hd033-soak-stderr.txt').write_bytes(b'later-err')
            report = validate_eight_hour_soak_start(root)
            self.assertFalse(report['eight_hour_soak_executed'])
            self.assertIsNone(report['soak_hours'])
            self.assertFalse(report['ac46_passed'])
            self.assertEqual(report['result'], 'not_run')

    def test_phase_gate_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'phase_gate': 'passed'})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'ac46_passed')


if __name__ == '__main__':
    unittest.main()
