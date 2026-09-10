from copy import deepcopy
from contextlib import redirect_stderr
from datetime import datetime, timedelta
from io import StringIO
from pathlib import Path
from unittest.mock import patch
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
    LIVE_WORKING_SET_REL,
    QualityError,
    SOAK_ELAPSED_REL,
    SOAK_WORKING_SET_REL,
    collect_dpi_overlay,
    collect_narrator_overlay,
    narrator_exe_path,
    soak_interrupt_capture_rels,
    soak_start_app_pid,
    system_dpi,
    validate_eight_hour_soak_elapsed,
    validate_eight_hour_soak_interruption,
    validate_eight_hour_soak_start,
    validate_narrator_product_ui_launch,
    validate_soak_working_set,
)
import record_soak_working_set as working_set_cli
import start_eight_hour_soak as soak_cli
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
        self.assertEqual(
            catalog['soak_working_set_capture'],
            'evidence/quality/live-soak-working-set.json',
        )
        self.assertTrue((ROOT / 'scripts' / 'record_soak_working_set.py').is_file())
        self.assertEqual(
            catalog['soak_interruption_capture'],
            'evidence/quality/live-soak-interrupted.json',
        )
        self.assertEqual(
            catalog['soak_interruption_captures'],
            SOAK_INTERRUPT_CAPTURES,
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
        interruption = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(encoding='utf-8'))
        self.assertEqual(interruption['kind'], 'live_eight_hour_soak_interruption')
        self.assertEqual(interruption['result'], 'not_run')
        self.assertFalse(interruption['eight_hour_soak_executed'])
        self.assertIsNone(interruption['soak_hours'])
        self.assertIsNone(interruption['crash_cause'])
        self.assertFalse(interruption['process_running_at_capture'])
        second = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(encoding='utf-8'))
        self.assertEqual(second['kind'], 'live_eight_hour_soak_interruption')
        self.assertEqual(second['result'], 'not_run')
        self.assertFalse(second['eight_hour_soak_executed'])
        self.assertIsNone(second['soak_hours'])
        self.assertIsNone(second['crash_cause'])
        self.assertFalse(second['process_running_at_capture'])
        self.assertEqual(second['started_at_utc'], '2026-09-10T11:11:22Z')
        self.assertEqual(second['owned_pids'], [46108, 64672, 71980])
        third = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-3.json').read_text(encoding='utf-8'))
        self.assertEqual(third['kind'], 'live_eight_hour_soak_interruption')
        self.assertEqual(third['result'], 'not_run')
        self.assertFalse(third['eight_hour_soak_executed'])
        self.assertIsNone(third['soak_hours'])
        self.assertIsNone(third['crash_cause'])
        self.assertFalse(third['process_running_at_capture'])
        self.assertEqual(third['started_at_utc'], '2026-09-10T11:35:26Z')
        self.assertEqual(third['owned_pids'], [24744, 27844])
        fourth = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-4.json').read_text(encoding='utf-8'))
        self.assertEqual(fourth['kind'], 'live_eight_hour_soak_interruption')
        self.assertEqual(fourth['result'], 'not_run')
        self.assertFalse(fourth['eight_hour_soak_executed'])
        self.assertIsNone(fourth['soak_hours'])
        self.assertIsNone(fourth['crash_cause'])
        self.assertFalse(fourth['process_running_at_capture'])
        self.assertEqual(fourth['started_at_utc'], '2026-09-10T11:56:07Z')
        self.assertEqual(fourth['owned_pids'], [21312, 22400])
        fifth = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-5.json').read_text(encoding='utf-8'))
        self.assertEqual(fifth['kind'], 'live_eight_hour_soak_interruption')
        self.assertEqual(fifth['result'], 'not_run')
        self.assertFalse(fifth['eight_hour_soak_executed'])
        self.assertIsNone(fifth['soak_hours'])
        self.assertIsNone(fifth['crash_cause'])
        self.assertFalse(fifth['process_running_at_capture'])
        self.assertEqual(fifth['started_at_utc'], '2026-09-10T12:19:50Z')
        self.assertEqual(fifth['owned_pids'], [91852, 37708])
        sixth = json.loads((ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-6.json').read_text(encoding='utf-8'))
        self.assertEqual(sixth['kind'], 'live_eight_hour_soak_interruption')
        self.assertEqual(sixth['result'], 'not_run')
        self.assertFalse(sixth['eight_hour_soak_executed'])
        self.assertIsNone(sixth['soak_hours'])
        self.assertIsNone(sixth['crash_cause'])
        self.assertFalse(sixth['process_running_at_capture'])
        self.assertEqual(sixth['started_at_utc'], '2026-09-10T12:49:43Z')
        self.assertEqual(sixth['owned_pids'], [69668, 73960])
        self.assertEqual(interruption['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertEqual(interruption['owned_pids'], [89580, 59552, 57712])

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
        self.assertFalse(working['live_working_set'])
        self.assertEqual(working['result'], 'not_run')

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

        bad = deepcopy(catalog)
        bad.pop('soak_working_set_capture')
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['soak_working_set_capture'] = 'evidence/quality/live-working-set.not-run.json'
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad.pop('soak_interruption_capture')
        with self.assertRaises(AssertionError):
            repository._check_hd033_closeout(hd033, bad, matrix)

        bad = deepcopy(catalog)
        bad['soak_interruption_capture'] = 'evidence/quality/live-soak.not-run.json'
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


SOAK_INTERRUPT_CAPTURES = [
    'evidence/quality/live-soak-interrupted.json',
    'evidence/quality/live-soak-interrupted-2.json',
    'evidence/quality/live-soak-interrupted-3.json',
    'evidence/quality/live-soak-interrupted-4.json',
    'evidence/quality/live-soak-interrupted-5.json',
    'evidence/quality/live-soak-interrupted-6.json',
]
# Fixture START after interruption 6. Do not freeze live-soak-start.json to 69668/73960.
_SOAK_START_AFTER_INTERRUPT6_UTC = '2026-09-10T13:07:00Z'
_SOAK_START_AFTER_INTERRUPT6_PIDS = [99021, 99022]


def _collect_interrupt_bounds(docs):
    pids = set()
    latest = ''
    for doc in docs:
        for key in ('owned_pids', 'last_heartbeat_alive_pids'):
            value = doc.get(key)
            if isinstance(value, list):
                pids.update(pid for pid in value if isinstance(pid, int) and pid > 0)
        heartbeat = doc.get('heartbeat_pid')
        if isinstance(heartbeat, int) and heartbeat > 0:
            pids.add(heartbeat)
        for key in ('started_at_utc', 'first_heartbeat_empty_alive_at_utc'):
            value = doc.get(key)
            if isinstance(value, str) and value > latest:
                latest = value
    return pids, latest


def _rebase_start_after_interrupts(start, interrupts):
    """Drop an inherited START that is itself an interruption (e.g. 69668/73960)."""
    interrupt_pids, latest = _collect_interrupt_bounds(interrupts)
    start_at = start.get('started_at_utc')
    start_pids = {
        pid for pid in (start.get('owned_pids') or [])
        if isinstance(pid, int) and pid > 0
    }
    if not (
        (isinstance(start_at, str) and latest and start_at <= latest)
        or (start_pids & interrupt_pids)
    ):
        return
    start['started_at_utc'] = _SOAK_START_AFTER_INTERRUPT6_UTC
    start['captured_at_utc'] = _SOAK_START_AFTER_INTERRUPT6_UTC
    start['owned_pids'] = list(_SOAK_START_AFTER_INTERRUPT6_PIDS)
    start['heartbeat_pid'] = _SOAK_START_AFTER_INTERRUPT6_PIDS[1]
    windows = start.get('windows')
    if isinstance(windows, list) and windows and isinstance(windows[0], dict):
        windows[0]['pid'] = _SOAK_START_AFTER_INTERRUPT6_PIDS[0]
    commands = start.get('commands')
    if isinstance(commands, list) and commands and isinstance(commands[0], dict):
        commands[0]['pid'] = _SOAK_START_AFTER_INTERRUPT6_PIDS[0]
        commands[0]['app_pids'] = [_SOAK_START_AFTER_INTERRUPT6_PIDS[0]]
    start['prior_interruption_captures'] = list(SOAK_INTERRUPT_CAPTURES)


def _write_soak_start(
    tmp: Path, *, start_overrides=None, live_overrides=None,
    interruption_overrides=None, skip_interruption=False,
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
    interruption = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
            encoding='utf-8'
        )
    )
    interrupt_docs = [interruption]
    if live_overrides:
        live.update(live_overrides)
    if interruption_overrides:
        interruption.update(interruption_overrides)
    (quality / 'live-soak.not-run.json').write_text(
        json.dumps(live), encoding='utf-8'
    )
    if not skip_interruption:
        (quality / 'live-soak-interrupted.json').write_text(
            json.dumps(interruption), encoding='utf-8'
        )
        for name in (
            'live-soak-interrupted-2.json',
            'live-soak-interrupted-3.json',
            'live-soak-interrupted-4.json',
            'live-soak-interrupted-5.json',
            'live-soak-interrupted-6.json',
        ):
            src = ROOT / 'evidence' / 'quality' / name
            if src.is_file():
                copied = json.loads(src.read_text(encoding='utf-8'))
                interrupt_docs.append(copied)
                (quality / name).write_text(json.dumps(copied), encoding='utf-8')
    _rebase_start_after_interrupts(start, interrupt_docs)
    if start_overrides:
        start.update(start_overrides)
    if 'prior_interruption_captures' not in start:
        start['prior_interruption_captures'] = list(SOAK_INTERRUPT_CAPTURES)
    (quality / 'live-soak-start.json').write_text(
        json.dumps(start), encoding='utf-8'
    )


class Hd033SoakStartTests(unittest.TestCase):
    def test_shipped_start_record_is_not_ac46(self):
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
        self.assertEqual(
            start['prior_interruption_capture'],
            'evidence/quality/live-soak-interrupted.json',
        )
        interruption = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
                encoding='utf-8'
            )
        )
        second = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(
                encoding='utf-8'
            )
        )
        third = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-3.json').read_text(
                encoding='utf-8'
            )
        )
        fourth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-4.json').read_text(
                encoding='utf-8'
            )
        )
        fifth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-5.json').read_text(
                encoding='utf-8'
            )
        )
        sixth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-6.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(interruption['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertEqual(interruption['owned_pids'], [89580, 59552, 57712])
        self.assertEqual(second['started_at_utc'], '2026-09-10T11:11:22Z')
        self.assertEqual(second['owned_pids'], [46108, 64672, 71980])
        self.assertEqual(
            second['last_heartbeat_alive_at_utc'], '2026-09-10T11:13:53Z'
        )
        self.assertEqual(
            second['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T11:14:54Z',
        )
        self.assertEqual(third['started_at_utc'], '2026-09-10T11:35:26Z')
        self.assertEqual(third['owned_pids'], [24744, 27844])
        self.assertEqual(
            third['last_heartbeat_alive_at_utc'], '2026-09-10T11:46:44Z'
        )
        self.assertEqual(
            third['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T11:47:45Z',
        )
        self.assertEqual(fourth['started_at_utc'], '2026-09-10T11:56:07Z')
        self.assertEqual(fourth['owned_pids'], [21312, 22400])
        self.assertEqual(fourth['heartbeat_pid'], 22400)
        self.assertEqual(
            fourth['last_heartbeat_alive_at_utc'], '2026-09-10T11:57:20Z'
        )
        self.assertEqual(
            fourth['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T11:58:21Z',
        )
        self.assertEqual(fifth['started_at_utc'], '2026-09-10T12:19:50Z')
        self.assertEqual(fifth['owned_pids'], [91852, 37708])
        self.assertEqual(fifth['heartbeat_pid'], 37708)
        self.assertEqual(
            fifth['last_heartbeat_alive_at_utc'], '2026-09-10T12:20:04Z'
        )
        self.assertEqual(
            fifth['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T12:21:05Z',
        )
        self.assertEqual(sixth['started_at_utc'], '2026-09-10T12:49:43Z')
        self.assertEqual(sixth['owned_pids'], [69668, 73960])
        self.assertEqual(sixth['heartbeat_pid'], 73960)
        self.assertEqual(
            sixth['last_heartbeat_alive_at_utc'], '2026-09-10T13:05:06Z'
        )
        self.assertEqual(
            sixth['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T13:06:07Z',
        )
        self.assertFalse(sixth['eight_hour_soak_executed'])
        self.assertIsNone(sixth['soak_hours'])
        self.assertIsNone(sixth['crash_cause'])
        self.assertFalse(fifth['eight_hour_soak_executed'])
        self.assertIsNone(fifth['soak_hours'])
        self.assertIsNone(fifth['crash_cause'])
        self.assertFalse(start['eight_hour_soak_executed'])
        self.assertIsNone(start['soak_hours'])
        self.assertFalse(start['ac46_passed'])
        self.assertNotIn('--project', argv)
        self.assertNotIn('dotnet', argv)
        self.assertEqual(
            start['prior_interruption_captures'],
            soak_interrupt_capture_rels(ROOT),
        )
        self.assertEqual(start['prior_interruption_captures'], SOAK_INTERRUPT_CAPTURES)
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
        self.assertGreater(start['started_at_utc'], '2026-09-10T13:06:07Z')
        self.assertNotEqual(start['started_at_utc'], '2026-09-10T12:49:43Z')
        self.assertNotEqual(start['started_at_utc'], '2026-09-10T12:19:50Z')
        self.assertNotEqual(start['started_at_utc'], '2026-09-10T11:56:07Z')
        self.assertNotEqual(start['started_at_utc'], '2026-09-10T11:35:26Z')
        self.assertNotEqual(start['started_at_utc'], '2026-09-10T11:11:22Z')
        self.assertNotEqual(start['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertIsInstance(start['owned_pids'], list)
        self.assertTrue(start['owned_pids'])
        self.assertNotEqual(start['owned_pids'], [69668, 73960])
        self.assertNotEqual(start['owned_pids'], [91852, 37708])
        self.assertNotEqual(start['owned_pids'], [21312, 22400])
        self.assertEqual(
            report['prior_interruption_started_at_utc'],
            interruption['started_at_utc'],
        )
        self.assertGreater(start['started_at_utc'], interruption['started_at_utc'])
        self.assertGreater(
            start['started_at_utc'], second['first_heartbeat_empty_alive_at_utc']
        )
        self.assertGreater(
            start['started_at_utc'], third['first_heartbeat_empty_alive_at_utc']
        )
        self.assertGreater(
            start['started_at_utc'], fourth['first_heartbeat_empty_alive_at_utc']
        )
        self.assertGreater(
            start['started_at_utc'], fifth['first_heartbeat_empty_alive_at_utc']
        )
        self.assertGreater(
            start['started_at_utc'], sixth['first_heartbeat_empty_alive_at_utc']
        )
        self.assertTrue(
            set(start['owned_pids']).isdisjoint(interruption['owned_pids'])
        )
        self.assertTrue(set(start['owned_pids']).isdisjoint(second['owned_pids']))
        self.assertTrue(set(start['owned_pids']).isdisjoint(third['owned_pids']))
        self.assertTrue(set(start['owned_pids']).isdisjoint(fourth['owned_pids']))
        self.assertTrue(set(start['owned_pids']).isdisjoint(fifth['owned_pids']))
        self.assertTrue(set(start['owned_pids']).isdisjoint(sixth['owned_pids']))
        self.assertNotEqual(start['owned_pids'], [24744, 27844])

    def test_cli_validates_without_starting_processes(self):
        script = ROOT / 'scripts' / 'start_eight_hour_soak.py'
        self.assertTrue(script.is_file())
        src = script.read_text(encoding='utf-8')
        self.assertIn('--record', src)
        self.assertIn('--record-elapsed', src)
        self.assertIn('--watch-elapsed', src)
        self.assertIn('--ui', src)
        elapsed_path = ROOT / SOAK_ELAPSED_REL
        before_elapsed = (
            elapsed_path.read_text(encoding='utf-8') if elapsed_path.is_file() else None
        )
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
        if before_elapsed is None:
            self.assertFalse(elapsed_path.is_file())
        else:
            self.assertEqual(elapsed_path.read_text(encoding='utf-8'), before_elapsed)

    def test_cli_spawns_breakaway_exe_not_dotnet_run(self):
        src = (ROOT / 'scripts' / 'start_eight_hour_soak.py').read_text(
            encoding='utf-8'
        )
        self.assertIn('CREATE_BREAKAWAY_FROM_JOB', src)
        self.assertIn('CREATE_NEW_PROCESS_GROUP', src)
        self.assertIn('DETACHED_PROCESS', src)
        self.assertIn(
            'CREATE_BREAKAWAY_FROM_JOB | CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS',
            src,
        )
        self.assertIn('STARTF_USESHOWWINDOW', src)
        self.assertIn('SW_SHOWMINNOACTIVE = 7', src)
        self.assertIn('HerdDesk.App.exe', src)
        self.assertIn("ui_env['HERDDESK_SOAK_MINIMIZED'] = '1'", src)
        heartbeat_src = src[src.find('def run_heartbeat'):src.find('def record(')]
        self.assertIn('_show_min_no_active', heartbeat_src)
        self.assertIn('_herddesk_windows', heartbeat_src)
        self.assertIn('if not alive:', heartbeat_src)
        self.assertIn('return 0', heartbeat_src)
        start_heartbeat_src = src[
            src.find('def _start_heartbeat'):src.find('def run_heartbeat')
        ]
        self.assertIn('def record_elapsed', src)
        self.assertIn('def run_elapsed_watch', src)
        self.assertIn('def watch_elapsed', src)
        self.assertIn('--watch-elapsed', src)
        self.assertIn('eight_hour_wall_clock_incomplete', src)
        self.assertIn('env=_dotnet_env()', start_heartbeat_src)
        self.assertNotIn('HERDDESK_SOAK_MINIMIZED', start_heartbeat_src)
        self.assertIn('subprocess.DEVNULL', src)
        self.assertIn("'stdin': subprocess.DEVNULL", src)
        self.assertNotIn('taskkill', src.lower())
        self.assertNotIn('76508', src)
        self.assertNotIn('85848', src)
        self.assertNotRegex(src, r"ui_argv = \[\s*str\(dotnet\),\s*'run'")
        policy = (
            ROOT / 'src' / 'HerdDesk.App' / 'Quality' / 'SoakLaunchPolicy.cs'
        ).read_text(encoding='utf-8')
        self.assertIn('HERDDESK_SOAK_MINIMIZED', policy)
        self.assertIn('MinimizedEnabledValue = "1"', policy)
        window = (ROOT / 'src' / 'HerdDesk.App' / 'MainWindow.xaml.cs').read_text(
            encoding='utf-8'
        )
        self.assertIn(
            'if (SoakLaunchPolicy.SuppressWindowClose())', window
        )
        self.assertIn(
            'AppWindow.Closing += OnSoakAppWindowClosing', window
        )
        self.assertIn('args.Cancel = true', window)

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

    def test_missing_interruption_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, skip_interruption=True)
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_wrong_interruption_pointer_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root,
                start_overrides={
                    'prior_interruption_capture': 'evidence/quality/live-soak.not-run.json',
                },
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_soak_hours_one_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'soak_hours': 1})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'invented_timings')

    def test_same_started_at_as_interruption_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T09:49:06Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_before_empty_heartbeat_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T10:50:00Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_empty_heartbeat_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T10:59:47Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_reused_dead_pids_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'owned_pids': [89580, 59552, 57712]}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_reused_second_interrupt_pids_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'owned_pids': [46108, 64672, 71980]}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_reused_third_interrupt_pids_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'owned_pids': [24744, 27844]}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_reused_fourth_interrupt_pids_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'owned_pids': [21312, 22400]}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_reused_fifth_interrupt_pids_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'owned_pids': [91852, 37708]}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_reused_sixth_interrupt_pids_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'owned_pids': [69668, 73960]}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_between_interruptions_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T11:12:00Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_second_empty_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T11:14:54Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_third_empty_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T11:47:45Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_third_start_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T11:35:26Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_fourth_empty_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T11:58:21Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_fourth_start_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T11:56:07Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_fifth_empty_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T12:21:05Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_fifth_start_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T12:19:50Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_sixth_empty_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T13:06:07Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_started_at_equal_sixth_start_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, start_overrides={'started_at_utc': '2026-09-10T12:49:43Z'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_omitted_second_interruption_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root,
                start_overrides={
                    'prior_interruption_captures': [
                        'evidence/quality/live-soak-interrupted.json',
                    ],
                },
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_omitted_third_interruption_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root,
                start_overrides={
                    'prior_interruption_captures': [
                        'evidence/quality/live-soak-interrupted.json',
                        'evidence/quality/live-soak-interrupted-2.json',
                    ],
                },
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_omitted_fourth_interruption_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root,
                start_overrides={
                    'prior_interruption_captures': [
                        'evidence/quality/live-soak-interrupted.json',
                        'evidence/quality/live-soak-interrupted-2.json',
                        'evidence/quality/live-soak-interrupted-3.json',
                    ],
                },
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_omitted_fifth_interruption_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root,
                start_overrides={
                    'prior_interruption_captures': [
                        'evidence/quality/live-soak-interrupted.json',
                        'evidence/quality/live-soak-interrupted-2.json',
                        'evidence/quality/live-soak-interrupted-3.json',
                        'evidence/quality/live-soak-interrupted-4.json',
                    ],
                },
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_omitted_sixth_interruption_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root,
                start_overrides={
                    'prior_interruption_captures': [
                        'evidence/quality/live-soak-interrupted.json',
                        'evidence/quality/live-soak-interrupted-2.json',
                        'evidence/quality/live-soak-interrupted-3.json',
                        'evidence/quality/live-soak-interrupted-4.json',
                        'evidence/quality/live-soak-interrupted-5.json',
                    ],
                },
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_reused_command_pid_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            start = json.loads(
                (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            start['commands'][0]['pid'] = 89580
            _write_soak_start(root, start_overrides=start)
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'continuation_of_interrupted_soak')

    def test_empty_owned_pids_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, start_overrides={'owned_pids': []})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_start(root)
            self.assertEqual(str(ctx.exception), 'missing_record_field')


def _elapsed_images(start):
    app_pid = start['commands'][0]['pid']
    owned = list(start['owned_pids'])

    def pid_running(pid):
        return pid in owned

    def pid_image(pid):
        if pid == app_pid:
            return 'HerdDesk.App.exe'
        return 'python.exe'

    return pid_running, pid_image


class Hd033SoakElapsedTests(unittest.TestCase):
    def test_elapsed_file_is_optional(self):
        elapsed_path = ROOT / SOAK_ELAPSED_REL
        report = validate_eight_hour_soak_elapsed(ROOT)
        if elapsed_path.is_file():
            self.assertIsNotNone(report)
            self.assertFalse(report['ac46_passed'])
            self.assertFalse(report['live_soak'])
            self.assertIsNone(report['soak_hours'])
        else:
            self.assertIsNone(report)

    def test_cli_record_elapsed_fails_closed_before_eight_hours(self):
        live_elapsed = ROOT / SOAK_ELAPSED_REL
        live_start = ROOT / 'evidence' / 'quality' / 'live-soak-start.json'
        before_live_start = live_start.read_text(encoding='utf-8')
        before_live_elapsed = (
            live_elapsed.read_text(encoding='utf-8') if live_elapsed.is_file() else None
        )
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start_path = root / 'evidence' / 'quality' / 'live-soak-start.json'
            elapsed_path = root / SOAK_ELAPSED_REL
            before_start = start_path.read_text(encoding='utf-8')
            err = StringIO()
            with patch.object(soak_cli, 'ROOT', root), redirect_stderr(err):
                code = soak_cli.main(['--record-elapsed'])
            self.assertEqual(code, 2, err.getvalue())
            parsed = json.loads(err.getvalue())
            self.assertEqual(parsed['error'], 'eight_hour_wall_clock_incomplete')
            self.assertEqual(start_path.read_text(encoding='utf-8'), before_start)
            after = json.loads(start_path.read_text(encoding='utf-8'))
            self.assertFalse(after['eight_hour_soak_executed'])
            self.assertIsNone(after['soak_hours'])
            self.assertFalse(after['ac46_passed'])
            self.assertFalse(after['live_soak'])
            self.assertFalse(elapsed_path.is_file())
        self.assertEqual(live_start.read_text(encoding='utf-8'), before_live_start)
        if before_live_elapsed is None:
            self.assertFalse(live_elapsed.is_file())
        else:
            self.assertEqual(
                live_elapsed.read_text(encoding='utf-8'), before_live_elapsed
            )

    def test_record_elapsed_too_early_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            running, image = _elapsed_images(start)
            with self.assertRaises(QualityError) as ctx:
                soak_cli.record_elapsed(
                    root,
                    now=started + timedelta(hours=7, minutes=59),
                    pid_running=running,
                    pid_image=image,
                    git_sha=start['git_sha'],
                )
            self.assertEqual(str(ctx.exception), 'eight_hour_wall_clock_incomplete')
            self.assertFalse((root / SOAK_ELAPSED_REL).is_file())
            after = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            self.assertFalse(after['eight_hour_soak_executed'])
            self.assertIsNone(after['soak_hours'])

    def test_record_elapsed_dead_app_pid_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            with self.assertRaises(QualityError) as ctx:
                soak_cli.record_elapsed(
                    root,
                    now=started + timedelta(hours=8),
                    pid_running=lambda _pid: False,
                    pid_image=lambda _pid: None,
                )
            self.assertEqual(str(ctx.exception), 'eight_hour_wall_clock_incomplete')
            self.assertFalse((root / SOAK_ELAPSED_REL).is_file())

    def test_record_elapsed_pid_image_mismatch_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            owned = list(start['owned_pids'])
            with self.assertRaises(QualityError) as ctx:
                soak_cli.record_elapsed(
                    root,
                    now=started + timedelta(hours=8),
                    pid_running=lambda pid: pid in owned,
                    pid_image=lambda _pid: 'notepad.exe',
                )
            self.assertEqual(str(ctx.exception), 'eight_hour_wall_clock_incomplete')
            self.assertFalse((root / SOAK_ELAPSED_REL).is_file())

    def test_record_elapsed_success_path_is_not_ac46(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            running, image = _elapsed_images(start)
            doc = soak_cli.record_elapsed(
                root,
                now=started + timedelta(hours=8),
                pid_running=running,
                pid_image=image,
                git_sha=start['git_sha'],
            )
            self.assertTrue(doc['eight_hour_soak_executed'])
            self.assertFalse(doc['ac46_passed'])
            self.assertFalse(doc['live_soak'])
            self.assertIsNone(doc['soak_hours'])
            self.assertIsNone(doc['disconnect_switch_count'])
            self.assertFalse(doc['herdr_executed'])
            self.assertFalse(doc['ac29_passed'])
            self.assertFalse(doc['live_working_set'])
            self.assertIsNone(doc['last_heartbeat_observed_at_utc'])
            self.assertIsNone(doc['last_resources_observed_at_utc'])
            self.assertIsNone(doc['last_working_set_bytes'])
            self.assertEqual(doc['result'], 'not_run')
            self.assertEqual(doc['l4_soak'], 'UNVERIFIED')
            self.assertEqual(doc['owned_pids'], start['owned_pids'])
            self.assertEqual(doc['started_at_utc'], start['started_at_utc'])
            self.assertEqual(doc['start_capture'], 'evidence/quality/live-soak-start.json')
            after_start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            self.assertFalse(after_start['eight_hour_soak_executed'])
            self.assertIsNone(after_start['soak_hours'])
            self.assertFalse(after_start['ac46_passed'])
            report = validate_eight_hour_soak_elapsed(root)
            self.assertIsNotNone(report)
            self.assertTrue(report['eight_hour_soak_executed'])
            self.assertFalse(report['ac46_passed'])
            self.assertFalse(report['live_soak'])
            self.assertIsNone(report['soak_hours'])

    def test_elapsed_ac46_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            running, image = _elapsed_images(start)
            soak_cli.record_elapsed(
                root,
                now=started + timedelta(hours=8),
                pid_running=running,
                pid_image=image,
                git_sha=start['git_sha'],
            )
            elapsed_path = root / SOAK_ELAPSED_REL
            loaded = json.loads(elapsed_path.read_text(encoding='utf-8'))
            loaded['ac46_passed'] = True
            elapsed_path.write_text(json.dumps(loaded), encoding='utf-8')
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_elapsed(root)
            self.assertEqual(str(ctx.exception), 'ac46_passed')

    def test_elapsed_soak_hours_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            running, image = _elapsed_images(start)
            soak_cli.record_elapsed(
                root,
                now=started + timedelta(hours=8),
                pid_running=running,
                pid_image=image,
                git_sha=start['git_sha'],
            )
            elapsed_path = root / SOAK_ELAPSED_REL
            loaded = json.loads(elapsed_path.read_text(encoding='utf-8'))
            loaded['soak_hours'] = 8
            elapsed_path.write_text(json.dumps(loaded), encoding='utf-8')
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_elapsed(root)
            self.assertEqual(str(ctx.exception), 'invented_timings')

    def test_record_elapsed_probe_summaries_from_jsonl(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            probe = root / 'probe-results'
            probe.mkdir(parents=True, exist_ok=True)
            (probe / 'hd033-soak-heartbeat.jsonl').write_text(
                json.dumps(
                    {
                        'observed_at_utc': '2026-09-10T20:00:00Z',
                        'alive_pids': start['owned_pids'][:1],
                    }
                )
                + '\n',
                encoding='utf-8',
            )
            (probe / 'hd033-soak-resources.jsonl').write_text(
                json.dumps(
                    {
                        'observed_at_utc': '2026-09-10T20:01:00Z',
                        'working_set_bytes': 131072,
                        'ac29_passed': False,
                    }
                )
                + '\n',
                encoding='utf-8',
            )
            running, image = _elapsed_images(start)
            doc = soak_cli.record_elapsed(
                root,
                now=started + timedelta(hours=8),
                pid_running=running,
                pid_image=image,
                git_sha=start['git_sha'],
            )
            self.assertEqual(doc['last_heartbeat_observed_at_utc'], '2026-09-10T20:00:00Z')
            self.assertEqual(doc['last_resources_observed_at_utc'], '2026-09-10T20:01:00Z')
            self.assertEqual(doc['last_working_set_bytes'], 131072)
            self.assertFalse(doc['ac29_passed'])
            self.assertFalse(doc['ac46_passed'])
            self.assertFalse(doc['live_working_set'])
            self.assertIsNone(doc['soak_hours'])

    def test_run_elapsed_watch_before_due_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            running, image = _elapsed_images(start)
            slept = []
            code = soak_cli.run_elapsed_watch(
                root,
                now=started + timedelta(hours=7, minutes=59),
                sleep=slept.append,
                pid_running=running,
                pid_image=image,
                interval_sec=60,
                max_polls=1,
            )
            self.assertEqual(code, 0)
            self.assertEqual(slept, [60])
            self.assertFalse((root / SOAK_ELAPSED_REL).is_file())
            after = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            self.assertFalse(after['eight_hour_soak_executed'])
            self.assertIsNone(after['soak_hours'])
            self.assertFalse(after['ac46_passed'])
            self.assertEqual(after['owned_pids'], start['owned_pids'])

    def test_run_elapsed_watch_dead_app_pid_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            code = soak_cli.run_elapsed_watch(
                root,
                now=started + timedelta(hours=1),
                sleep=lambda _sec: None,
                pid_running=lambda _pid: False,
                pid_image=lambda _pid: None,
                max_polls=1,
            )
            self.assertEqual(code, 0)
            self.assertFalse((root / SOAK_ELAPSED_REL).is_file())

    def test_run_elapsed_watch_existing_elapsed_not_rewritten(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            running, image = _elapsed_images(start)
            soak_cli.record_elapsed(
                root,
                now=started + timedelta(hours=8),
                pid_running=running,
                pid_image=image,
                git_sha=start['git_sha'],
            )
            elapsed_path = root / SOAK_ELAPSED_REL
            before = elapsed_path.read_text(encoding='utf-8')
            code = soak_cli.run_elapsed_watch(
                root,
                now=started + timedelta(hours=9),
                sleep=lambda _sec: None,
                pid_running=running,
                pid_image=image,
            )
            self.assertEqual(code, 0)
            self.assertEqual(elapsed_path.read_text(encoding='utf-8'), before)
            loaded = json.loads(before)
            self.assertFalse(loaded['ac46_passed'])
            self.assertFalse(loaded['live_soak'])
            self.assertIsNone(loaded['soak_hours'])

    def test_run_elapsed_watch_success_path_is_not_ac46(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            running, image = _elapsed_images(start)
            code = soak_cli.run_elapsed_watch(
                root,
                now=started + timedelta(hours=8),
                sleep=lambda _sec: None,
                pid_running=running,
                pid_image=image,
            )
            self.assertEqual(code, 0)
            report = validate_eight_hour_soak_elapsed(root)
            self.assertIsNotNone(report)
            self.assertTrue(report['eight_hour_soak_executed'])
            self.assertFalse(report['ac46_passed'])
            self.assertFalse(report['live_soak'])
            self.assertIsNone(report['soak_hours'])
            after_start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            self.assertFalse(after_start['eight_hour_soak_executed'])
            self.assertEqual(after_start['owned_pids'], start['owned_pids'])
            elapsed = json.loads(
                (root / SOAK_ELAPSED_REL).read_text(encoding='utf-8')
            )
            self.assertFalse(elapsed['ac29_passed'])
            self.assertIsNone(elapsed['disconnect_switch_count'])

    def test_watch_elapsed_does_not_add_pid_to_start_owned(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start_path = root / 'evidence' / 'quality' / 'live-soak-start.json'
            before = start_path.read_text(encoding='utf-8')
            start = json.loads(before)
            spawned = []

            def fake_spawn(watch_root):
                spawned.append(watch_root)
                return 424242

            report = soak_cli.watch_elapsed(root, start_watch=fake_spawn)
            self.assertEqual(spawned, [root])
            self.assertTrue(report['elapsed_watcher_spawned'])
            self.assertEqual(report['elapsed_watcher_pid'], 424242)
            self.assertFalse(report['elapsed_already_present'])
            self.assertFalse(report['elapsed_watcher_added_to_start_owned_pids'])
            self.assertFalse(report['ac46_passed'])
            self.assertFalse(report['eight_hour_soak_executed'])
            self.assertIsNone(report['soak_hours'])
            self.assertFalse(report['ac29_passed'])
            after = json.loads(start_path.read_text(encoding='utf-8'))
            self.assertEqual(start_path.read_text(encoding='utf-8'), before)
            self.assertEqual(after['owned_pids'], start['owned_pids'])
            self.assertNotIn(424242, after['owned_pids'])
            self.assertFalse((root / SOAK_ELAPSED_REL).is_file())

    def test_watch_elapsed_existing_does_not_spawn_or_rewrite(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            started = datetime.fromisoformat(
                str(start['started_at_utc']).replace('Z', '+00:00')
            )
            running, image = _elapsed_images(start)
            soak_cli.record_elapsed(
                root,
                now=started + timedelta(hours=8),
                pid_running=running,
                pid_image=image,
                git_sha=start['git_sha'],
            )
            elapsed_path = root / SOAK_ELAPSED_REL
            before = elapsed_path.read_text(encoding='utf-8')
            start_path = root / 'evidence' / 'quality' / 'live-soak-start.json'
            before_start = start_path.read_text(encoding='utf-8')

            def boom(_root):
                raise AssertionError('must not spawn when elapsed exists')

            report = soak_cli.watch_elapsed(root, start_watch=boom)
            self.assertFalse(report['elapsed_watcher_spawned'])
            self.assertTrue(report['elapsed_already_present'])
            self.assertIsNone(report['elapsed_watcher_pid'])
            self.assertFalse(report['ac46_passed'])
            self.assertFalse(report['eight_hour_soak_executed'])
            self.assertEqual(elapsed_path.read_text(encoding='utf-8'), before)
            self.assertEqual(start_path.read_text(encoding='utf-8'), before_start)

    def test_cli_watch_elapsed_does_not_write_before_due(self):
        live_elapsed = ROOT / SOAK_ELAPSED_REL
        live_start = ROOT / 'evidence' / 'quality' / 'live-soak-start.json'
        before_live_start = live_start.read_text(encoding='utf-8')
        before_live_elapsed = (
            live_elapsed.read_text(encoding='utf-8') if live_elapsed.is_file() else None
        )
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            start_path = root / 'evidence' / 'quality' / 'live-soak-start.json'
            elapsed_path = root / SOAK_ELAPSED_REL
            before_start = start_path.read_text(encoding='utf-8')
            fake_pid = 424242
            with patch.object(soak_cli, 'ROOT', root), patch.object(
                soak_cli, '_start_elapsed_watch', lambda _root: fake_pid
            ):
                code = soak_cli.main(['--watch-elapsed'])
            self.assertEqual(code, 0)
            self.assertEqual(start_path.read_text(encoding='utf-8'), before_start)
            self.assertFalse(elapsed_path.is_file())
            after = json.loads(start_path.read_text(encoding='utf-8'))
            self.assertFalse(after['eight_hour_soak_executed'])
            self.assertIsNone(after['soak_hours'])
            self.assertFalse(after['ac46_passed'])
            self.assertNotIn(fake_pid, after['owned_pids'])
        self.assertEqual(live_start.read_text(encoding='utf-8'), before_live_start)
        if before_live_elapsed is None:
            self.assertFalse(live_elapsed.is_file())
        else:
            self.assertEqual(
                live_elapsed.read_text(encoding='utf-8'), before_live_elapsed
            )

    def test_live_record_elapsed_fails_closed_before_eight_hours(self):
        script = ROOT / 'scripts' / 'start_eight_hour_soak.py'
        live_elapsed = ROOT / SOAK_ELAPSED_REL
        live_start = ROOT / 'evidence' / 'quality' / 'live-soak-start.json'
        before_start = live_start.read_text(encoding='utf-8')
        before_elapsed = (
            live_elapsed.read_text(encoding='utf-8') if live_elapsed.is_file() else None
        )
        completed = subprocess.run(
            [sys.executable, str(script), '--record-elapsed'],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding='utf-8',
            check=False,
        )
        self.assertEqual(completed.returncode, 2, completed.stderr)
        parsed = json.loads(completed.stderr)
        self.assertEqual(parsed['error'], 'eight_hour_wall_clock_incomplete')
        self.assertEqual(live_start.read_text(encoding='utf-8'), before_start)
        after = json.loads(live_start.read_text(encoding='utf-8'))
        self.assertFalse(after['eight_hour_soak_executed'])
        self.assertIsNone(after['soak_hours'])
        self.assertFalse(after['ac46_passed'])
        self.assertFalse(after['live_soak'])
        if before_elapsed is None:
            self.assertFalse(live_elapsed.is_file())
        else:
            self.assertEqual(live_elapsed.read_text(encoding='utf-8'), before_elapsed)


class Hd033SoakInterruptionTests(unittest.TestCase):
    def test_shipped_interruption_is_not_ac46(self):
        report = validate_eight_hour_soak_interruption(ROOT)
        self.assertEqual(report['document_kind'], 'hd033_eight_hour_soak_interruption')
        self.assertTrue(report['product_ui_started'])
        self.assertFalse(report['process_running_at_capture'])
        self.assertFalse(report['eight_hour_soak_executed'])
        self.assertIsNone(report['soak_hours'])
        self.assertFalse(report['live_soak'])
        self.assertFalse(report['ac46_passed'])
        self.assertEqual(report['result'], 'not_run')
        self.assertNotIn(str(report['result']).lower(), SUCCESS)
        self.assertEqual(report['l4_soak'], 'UNVERIFIED')
        self.assertFalse(report['g0_passed'])
        self.assertIsNone(report['crash_cause'])
        interruption = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(interruption['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertEqual(
            interruption['last_heartbeat_alive_at_utc'], '2026-09-10T10:58:46Z'
        )
        self.assertEqual(
            interruption['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T10:59:47Z',
        )
        self.assertEqual(interruption['owned_pids'], [89580, 59552, 57712])
        self.assertFalse(interruption['herddesk_crash_dump_found'])
        self.assertFalse(interruption['application_error_herddesk'])
        self.assertTrue(interruption['xerox_print_experience_crash_unrelated'])
        self.assertTrue(interruption['soak_stdout_empty'])
        self.assertTrue(interruption['soak_stderr_empty'])
        self.assertIsNone(interruption['soak_hours'])
        self.assertIsNone(interruption['disconnect_switch_count'])

    def test_shipped_second_interruption_is_not_ac46(self):
        second = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(second['document_kind'], 'hd033_eight_hour_soak_interruption')
        self.assertEqual(second['started_at_utc'], '2026-09-10T11:11:22Z')
        self.assertEqual(
            second['last_heartbeat_alive_at_utc'], '2026-09-10T11:13:53Z'
        )
        self.assertEqual(
            second['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T11:14:54Z',
        )
        self.assertEqual(second['owned_pids'], [46108, 64672, 71980])
        self.assertFalse(second['eight_hour_soak_executed'])
        self.assertIsNone(second['soak_hours'])
        self.assertFalse(second['ac46_passed'])
        self.assertFalse(second['live_soak'])
        self.assertFalse(second['g0_passed'])
        self.assertIsNone(second['crash_cause'])
        self.assertFalse(second['herddesk_crash_dump_found'])
        self.assertFalse(second['application_error_herddesk'])
        self.assertFalse(second['process_running_at_capture'])
        self.assertTrue(second['xerox_print_experience_crash_unrelated'])
        first = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(first['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertEqual(first['owned_pids'], [89580, 59552, 57712])
        self.assertNotEqual(first['started_at_utc'], second['started_at_utc'])
        self.assertTrue(set(first['owned_pids']).isdisjoint(second['owned_pids']))

    def test_shipped_third_interruption_is_not_ac46(self):
        third = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-3.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(third['document_kind'], 'hd033_eight_hour_soak_interruption')
        self.assertEqual(third['started_at_utc'], '2026-09-10T11:35:26Z')
        self.assertEqual(
            third['last_heartbeat_alive_at_utc'], '2026-09-10T11:46:44Z'
        )
        self.assertEqual(
            third['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T11:47:45Z',
        )
        self.assertEqual(third['owned_pids'], [24744, 27844])
        self.assertEqual(third['heartbeat_pid'], 27844)
        self.assertFalse(third['eight_hour_soak_executed'])
        self.assertIsNone(third['soak_hours'])
        self.assertFalse(third['ac46_passed'])
        self.assertFalse(third['live_soak'])
        self.assertFalse(third['g0_passed'])
        self.assertIsNone(third['crash_cause'])
        self.assertFalse(third['herddesk_crash_dump_found'])
        self.assertFalse(third['application_error_herddesk'])
        self.assertFalse(third['process_running_at_capture'])
        self.assertTrue(third['xerox_print_experience_crash_unrelated'])
        first = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
                encoding='utf-8'
            )
        )
        second = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(first['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertEqual(first['owned_pids'], [89580, 59552, 57712])
        self.assertEqual(second['started_at_utc'], '2026-09-10T11:11:22Z')
        self.assertEqual(second['owned_pids'], [46108, 64672, 71980])
        self.assertNotEqual(first['started_at_utc'], third['started_at_utc'])
        self.assertNotEqual(second['started_at_utc'], third['started_at_utc'])
        self.assertTrue(set(first['owned_pids']).isdisjoint(third['owned_pids']))
        self.assertTrue(set(second['owned_pids']).isdisjoint(third['owned_pids']))

    def test_shipped_fourth_interruption_is_not_ac46(self):
        fourth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-4.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(fourth['document_kind'], 'hd033_eight_hour_soak_interruption')
        self.assertEqual(fourth['started_at_utc'], '2026-09-10T11:56:07Z')
        self.assertEqual(
            fourth['last_heartbeat_alive_at_utc'], '2026-09-10T11:57:20Z'
        )
        self.assertEqual(
            fourth['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T11:58:21Z',
        )
        self.assertEqual(fourth['owned_pids'], [21312, 22400])
        self.assertEqual(fourth['heartbeat_pid'], 22400)
        self.assertFalse(fourth['eight_hour_soak_executed'])
        self.assertIsNone(fourth['soak_hours'])
        self.assertFalse(fourth['ac46_passed'])
        self.assertFalse(fourth['live_soak'])
        self.assertFalse(fourth['g0_passed'])
        self.assertIsNone(fourth['crash_cause'])
        self.assertFalse(fourth['herddesk_crash_dump_found'])
        self.assertFalse(fourth['application_error_herddesk'])
        self.assertFalse(fourth['process_running_at_capture'])
        self.assertTrue(fourth['xerox_print_experience_crash_unrelated'])
        first = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
                encoding='utf-8'
            )
        )
        second = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(
                encoding='utf-8'
            )
        )
        third = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-3.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(first['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertEqual(first['owned_pids'], [89580, 59552, 57712])
        self.assertEqual(second['started_at_utc'], '2026-09-10T11:11:22Z')
        self.assertEqual(second['owned_pids'], [46108, 64672, 71980])
        self.assertEqual(third['started_at_utc'], '2026-09-10T11:35:26Z')
        self.assertEqual(third['owned_pids'], [24744, 27844])
        self.assertNotEqual(first['started_at_utc'], fourth['started_at_utc'])
        self.assertNotEqual(second['started_at_utc'], fourth['started_at_utc'])
        self.assertNotEqual(third['started_at_utc'], fourth['started_at_utc'])
        self.assertTrue(set(first['owned_pids']).isdisjoint(fourth['owned_pids']))
        self.assertTrue(set(second['owned_pids']).isdisjoint(fourth['owned_pids']))
        self.assertTrue(set(third['owned_pids']).isdisjoint(fourth['owned_pids']))

    def test_shipped_fifth_interruption_is_not_ac46(self):
        fifth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-5.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(fifth['document_kind'], 'hd033_eight_hour_soak_interruption')
        self.assertEqual(fifth['started_at_utc'], '2026-09-10T12:19:50Z')
        self.assertEqual(
            fifth['last_heartbeat_alive_at_utc'], '2026-09-10T12:20:04Z'
        )
        self.assertEqual(
            fifth['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T12:21:05Z',
        )
        self.assertEqual(fifth['owned_pids'], [91852, 37708])
        self.assertEqual(fifth['heartbeat_pid'], 37708)
        self.assertFalse(fifth['eight_hour_soak_executed'])
        self.assertIsNone(fifth['soak_hours'])
        self.assertFalse(fifth['ac46_passed'])
        self.assertFalse(fifth['live_soak'])
        self.assertFalse(fifth['g0_passed'])
        self.assertIsNone(fifth['crash_cause'])
        self.assertFalse(fifth['herddesk_crash_dump_found'])
        self.assertFalse(fifth['application_error_herddesk'])
        self.assertFalse(fifth['process_running_at_capture'])
        self.assertTrue(fifth['xerox_print_experience_crash_unrelated'])
        first = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
                encoding='utf-8'
            )
        )
        second = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(
                encoding='utf-8'
            )
        )
        third = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-3.json').read_text(
                encoding='utf-8'
            )
        )
        fourth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-4.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(first['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertEqual(first['owned_pids'], [89580, 59552, 57712])
        self.assertEqual(second['started_at_utc'], '2026-09-10T11:11:22Z')
        self.assertEqual(second['owned_pids'], [46108, 64672, 71980])
        self.assertEqual(third['started_at_utc'], '2026-09-10T11:35:26Z')
        self.assertEqual(third['owned_pids'], [24744, 27844])
        self.assertEqual(fourth['started_at_utc'], '2026-09-10T11:56:07Z')
        self.assertEqual(fourth['owned_pids'], [21312, 22400])
        self.assertNotEqual(first['started_at_utc'], fifth['started_at_utc'])
        self.assertNotEqual(second['started_at_utc'], fifth['started_at_utc'])
        self.assertNotEqual(third['started_at_utc'], fifth['started_at_utc'])
        self.assertNotEqual(fourth['started_at_utc'], fifth['started_at_utc'])
        self.assertTrue(set(first['owned_pids']).isdisjoint(fifth['owned_pids']))
        self.assertTrue(set(second['owned_pids']).isdisjoint(fifth['owned_pids']))
        self.assertTrue(set(third['owned_pids']).isdisjoint(fifth['owned_pids']))
        self.assertTrue(set(fourth['owned_pids']).isdisjoint(fifth['owned_pids']))

    def test_shipped_sixth_interruption_is_not_ac46(self):
        sixth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-6.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(sixth['document_kind'], 'hd033_eight_hour_soak_interruption')
        self.assertEqual(sixth['started_at_utc'], '2026-09-10T12:49:43Z')
        self.assertEqual(
            sixth['last_heartbeat_alive_at_utc'], '2026-09-10T13:05:06Z'
        )
        self.assertEqual(
            sixth['first_heartbeat_empty_alive_at_utc'],
            '2026-09-10T13:06:07Z',
        )
        self.assertEqual(sixth['owned_pids'], [69668, 73960])
        self.assertEqual(sixth['heartbeat_pid'], 73960)
        self.assertFalse(sixth['eight_hour_soak_executed'])
        self.assertIsNone(sixth['soak_hours'])
        self.assertFalse(sixth['ac46_passed'])
        self.assertFalse(sixth['live_soak'])
        self.assertFalse(sixth['g0_passed'])
        self.assertIsNone(sixth['crash_cause'])
        self.assertFalse(sixth['herddesk_crash_dump_found'])
        self.assertFalse(sixth['application_error_herddesk'])
        self.assertFalse(sixth['process_running_at_capture'])
        self.assertTrue(sixth['xerox_print_experience_crash_unrelated'])
        first = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
                encoding='utf-8'
            )
        )
        second = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(
                encoding='utf-8'
            )
        )
        third = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-3.json').read_text(
                encoding='utf-8'
            )
        )
        fourth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-4.json').read_text(
                encoding='utf-8'
            )
        )
        fifth = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-5.json').read_text(
                encoding='utf-8'
            )
        )
        self.assertEqual(first['started_at_utc'], '2026-09-10T09:49:06Z')
        self.assertEqual(first['owned_pids'], [89580, 59552, 57712])
        self.assertEqual(second['started_at_utc'], '2026-09-10T11:11:22Z')
        self.assertEqual(second['owned_pids'], [46108, 64672, 71980])
        self.assertEqual(third['started_at_utc'], '2026-09-10T11:35:26Z')
        self.assertEqual(third['owned_pids'], [24744, 27844])
        self.assertEqual(fourth['started_at_utc'], '2026-09-10T11:56:07Z')
        self.assertEqual(fourth['owned_pids'], [21312, 22400])
        self.assertEqual(fifth['started_at_utc'], '2026-09-10T12:19:50Z')
        self.assertEqual(fifth['owned_pids'], [91852, 37708])
        self.assertNotEqual(first['started_at_utc'], sixth['started_at_utc'])
        self.assertNotEqual(second['started_at_utc'], sixth['started_at_utc'])
        self.assertNotEqual(third['started_at_utc'], sixth['started_at_utc'])
        self.assertNotEqual(fourth['started_at_utc'], sixth['started_at_utc'])
        self.assertNotEqual(fifth['started_at_utc'], sixth['started_at_utc'])
        self.assertTrue(set(first['owned_pids']).isdisjoint(sixth['owned_pids']))
        self.assertTrue(set(second['owned_pids']).isdisjoint(sixth['owned_pids']))
        self.assertTrue(set(third['owned_pids']).isdisjoint(sixth['owned_pids']))
        self.assertTrue(set(fourth['owned_pids']).isdisjoint(sixth['owned_pids']))
        self.assertTrue(set(fifth['owned_pids']).isdisjoint(sixth['owned_pids']))

    def test_structure_contract_invokes_interruption_record(self):
        repository._check_hd033_soak_start()

    def test_ac46_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, interruption_overrides={'ac46_passed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_interruption(root)
            self.assertEqual(str(ctx.exception), 'ac46_passed')

    def test_eight_hour_executed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, interruption_overrides={'eight_hour_soak_executed': True}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_interruption(root)
            self.assertEqual(str(ctx.exception), 'eight_hour_soak_executed')

    def test_soak_hours_fails_closed(self):
        for value in (1, 8, 1.16):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_soak_start(root, interruption_overrides={'soak_hours': value})
                with self.assertRaises(QualityError) as ctx:
                    validate_eight_hour_soak_interruption(root)
                self.assertEqual(str(ctx.exception), 'invented_timings')

    def test_invented_crash_cause_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, interruption_overrides={'crash_cause': 'herddesk_crash'}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_interruption(root)
            self.assertEqual(str(ctx.exception), 'invented_crash_cause')

    def test_crash_dump_claimed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root, interruption_overrides={'herddesk_crash_dump_found': True}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_interruption(root)
            self.assertEqual(str(ctx.exception), 'invented_crash_cause')

    def test_xerox_as_cause_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(
                root,
                interruption_overrides={
                    'xerox_print_experience_crash_unrelated': False,
                },
            )
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_interruption(root)
            self.assertEqual(str(ctx.exception), 'invented_crash_cause')

    def test_result_success_fails_closed(self):
        for value in ('success', 'passed', 'verified', 'ok', 'pass', True):
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                _write_soak_start(root, interruption_overrides={'result': value})
                with self.assertRaises(QualityError) as ctx:
                    validate_eight_hour_soak_interruption(root)
                self.assertEqual(str(ctx.exception), 'live_success_claimed')

    def test_l4_soak_pass_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root, interruption_overrides={'l4_soak': 'passed'})
            with self.assertRaises(QualityError) as ctx:
                validate_eight_hour_soak_interruption(root)
            self.assertEqual(str(ctx.exception), 'l4_soak_claimed')


def _copy_live_working_set_not_run(tmp: Path) -> None:
    quality = tmp / 'evidence' / 'quality'
    quality.mkdir(parents=True, exist_ok=True)
    src = ROOT / LIVE_WORKING_SET_REL
    (quality / 'live-working-set.not-run.json').write_text(
        src.read_text(encoding='utf-8'), encoding='utf-8'
    )


def _overlay_doc(start: dict, **overrides):
    pid = soak_start_app_pid(start)
    doc = {
        'document_kind': 'hd033_soak_working_set_overlay',
        'template': False,
        'result': 'not_run',
        'live_working_set': False,
        'live_soak': False,
        'ac29_passed': False,
        'ac46_passed': False,
        'eight_hour_soak_executed': False,
        'soak_hours': None,
        'pid': pid,
        'started_at_utc': start.get('started_at_utc'),
        'sampled_at_utc': '2026-09-10T14:00:00Z',
        'working_set_bytes': 123456789,
        'private_bytes': 1000,
        'handle_count': 200,
        'process_count': 1,
        'git_sha': start.get('git_sha') or ('a' * 40),
        'start_capture': 'evidence/quality/live-soak-start.json',
        'herdr_executed': False,
        'g0_passed': False,
        'invented_timings': False,
        'one_quarter_pane_lab': False,
        'open_close_100': False,
        'derive_process_memory_from_q_p': False,
        'mib_bytes': 1048576,
        'sampler_added_to_start_owned_pids': False,
        'l4_soak': 'UNVERIFIED',
        'phase_gate': 'not_passed',
        'sampler_pid': 4242,
    }
    doc.update(overrides)
    return doc


def _write_soak_working_set(tmp: Path, *, overlay_overrides=None) -> None:
    _write_soak_start(tmp)
    _copy_live_working_set_not_run(tmp)
    start = json.loads(
        (tmp / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
            encoding='utf-8'
        )
    )
    overrides = overlay_overrides or {}
    doc = _overlay_doc(start, **overrides)
    (tmp / 'evidence' / 'quality' / 'live-soak-working-set.json').write_text(
        json.dumps(doc), encoding='utf-8'
    )


class Hd033SoakWorkingSetTests(unittest.TestCase):
    def test_overlay_file_is_optional_until_record(self):
        overlay_path = ROOT / SOAK_WORKING_SET_REL
        report = validate_soak_working_set(ROOT)
        if overlay_path.is_file():
            self.assertIsNotNone(report)
            self.assertFalse(report['ac29_passed'])
            self.assertFalse(report['live_working_set'])
            self.assertFalse(report['ac46_passed'])
            self.assertFalse(report['eight_hour_soak_executed'])
            self.assertIsNone(report['soak_hours'])
            start = json.loads(
                (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            self.assertEqual(report['pid'], soak_start_app_pid(start))
            self.assertEqual(report['started_at_utc'], start['started_at_utc'])
            self.assertIsInstance(report['working_set_bytes'], int)
            self.assertGreater(report['working_set_bytes'], 0)
        else:
            self.assertIsNone(report)

    def test_cli_validates_without_recording(self):
        script = ROOT / 'scripts' / 'record_soak_working_set.py'
        self.assertTrue(script.is_file())
        src = script.read_text(encoding='utf-8')
        self.assertIn('--record', src)
        self.assertNotIn('76508', src)
        self.assertNotIn('85848', src)
        overlay_path = ROOT / SOAK_WORKING_SET_REL
        start_path = ROOT / 'evidence' / 'quality' / 'live-soak-start.json'
        not_run_path = ROOT / LIVE_WORKING_SET_REL
        before_overlay = (
            overlay_path.read_text(encoding='utf-8') if overlay_path.is_file() else None
        )
        before_start = start_path.read_text(encoding='utf-8')
        before_not_run = not_run_path.read_text(encoding='utf-8')
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
        self.assertFalse(report['ac29_passed'])
        self.assertFalse(report['live_working_set'])
        self.assertFalse(report['ac46_passed'])
        self.assertFalse(report['eight_hour_soak_executed'])
        self.assertIsNone(report['soak_hours'])
        self.assertEqual(report['result'], 'not_run')
        self.assertEqual(start_path.read_text(encoding='utf-8'), before_start)
        self.assertEqual(not_run_path.read_text(encoding='utf-8'), before_not_run)
        if before_overlay is None:
            self.assertFalse(overlay_path.is_file())
            self.assertFalse(report.get('recorded'))
        else:
            self.assertEqual(overlay_path.read_text(encoding='utf-8'), before_overlay)
            self.assertTrue(report.get('recorded'))

    def test_cli_does_not_launch_ui_or_freeze_start_pids(self):
        src = (ROOT / 'scripts' / 'record_soak_working_set.py').read_text(
            encoding='utf-8'
        )
        self.assertIn('without launching another --ui', src)
        self.assertNotIn("ui_argv", src)
        self.assertNotIn('taskkill', src.lower())
        self.assertIn('hd033-soak-resources.jsonl', src)
        self.assertIn('if not alive:', src)
        self.assertIn('return 0', src)
        self.assertNotIn('76508', src)
        self.assertNotIn('85848', src)
        start = json.loads(
            (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                encoding='utf-8'
            )
        )
        pid = soak_start_app_pid(start)
        self.assertIsInstance(pid, int)
        self.assertGreater(pid, 0)
        self.assertIn(pid, start['owned_pids'])

    def test_not_run_working_set_row_stays_not_run(self):
        working = json.loads((ROOT / LIVE_WORKING_SET_REL).read_text(encoding='utf-8'))
        self.assertEqual(working['kind'], 'live_working_set')
        self.assertEqual(working['result'], 'not_run')
        self.assertFalse(working['live_working_set'])
        self.assertIsNone(working['working_set_bytes'])
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        rows = {item['id']: item for item in catalog['live_rows']}
        self.assertEqual(
            rows['live-working-set']['evidence_path'],
            'evidence/quality/live-working-set.not-run.json',
        )
        cards = {item['id']: item for item in catalog['execution_cards']}
        self.assertEqual(
            cards['working-set-1-4-pane']['live_capture'],
            'evidence/quality/live-working-set.not-run.json',
        )

    def test_structure_contract_invokes_working_set_overlay(self):
        repository._check_hd033_soak_working_set()

    def test_record_samples_start_pid_without_mutating_start(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            _copy_live_working_set_not_run(root)
            start_path = root / 'evidence' / 'quality' / 'live-soak-start.json'
            not_run_path = root / LIVE_WORKING_SET_REL
            before_start = start_path.read_text(encoding='utf-8')
            before_not_run = not_run_path.read_text(encoding='utf-8')
            start = json.loads(before_start)
            app_pid = soak_start_app_pid(start)

            def sample(pid):
                self.assertEqual(pid, app_pid)
                return {
                    'working_set_bytes': 987654321,
                    'private_bytes': 1111,
                    'handle_count': 222,
                    'process_count': 1,
                }

            doc = working_set_cli.record(
                root,
                pid_running=lambda pid: True,
                pid_image=lambda pid: 'HerdDesk.App.exe',
                sample_process=sample,
                start_heartbeat=lambda _root, pid, _started: 4242,
                git_sha='b' * 40,
                now='2026-09-10T14:05:00Z',
            )
            self.assertEqual(doc['pid'], app_pid)
            self.assertEqual(doc['working_set_bytes'], 987654321)
            self.assertEqual(doc['sampler_pid'], 4242)
            self.assertFalse(doc['live_working_set'])
            self.assertFalse(doc['ac29_passed'])
            self.assertFalse(doc['ac46_passed'])
            self.assertFalse(doc['eight_hour_soak_executed'])
            self.assertIsNone(doc['soak_hours'])
            self.assertFalse(doc['one_quarter_pane_lab'])
            self.assertFalse(doc['open_close_100'])
            self.assertEqual(start_path.read_text(encoding='utf-8'), before_start)
            self.assertEqual(not_run_path.read_text(encoding='utf-8'), before_not_run)
            after = json.loads(before_start)
            self.assertNotIn(4242, after['owned_pids'])
            report = validate_soak_working_set(root)
            self.assertIsNotNone(report)
            self.assertEqual(report['pid'], app_pid)
            self.assertEqual(report['working_set_bytes'], 987654321)
            self.assertEqual(report['sampler_pid'], 4242)

    def test_record_without_hooks_fails_closed_off_windows(self):
        if os.name == 'nt':
            self.skipTest('omitted hooks use live Windows APIs')
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            _copy_live_working_set_not_run(root)
            overlay = root / SOAK_WORKING_SET_REL
            start_path = root / 'evidence' / 'quality' / 'live-soak-start.json'
            not_run_path = root / LIVE_WORKING_SET_REL
            before_start = start_path.read_text(encoding='utf-8')
            before_not_run = not_run_path.read_text(encoding='utf-8')
            err = StringIO()
            with patch.object(working_set_cli, 'ROOT', root), redirect_stderr(err):
                with self.assertRaises(QualityError) as ctx:
                    working_set_cli.record(root)
                code = working_set_cli.main(['--record'])
            self.assertEqual(str(ctx.exception), 'missing_record_field')
            self.assertEqual(code, 2, err.getvalue())
            self.assertEqual(json.loads(err.getvalue())['error'], 'missing_record_field')
            self.assertFalse(overlay.is_file())
            self.assertEqual(start_path.read_text(encoding='utf-8'), before_start)
            self.assertEqual(not_run_path.read_text(encoding='utf-8'), before_not_run)

    def test_heartbeat_exits_when_app_pid_gone(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'hd033-soak-resources.jsonl'
            code = working_set_cli.run_resource_heartbeat(
                12,
                path,
                '2026-09-10T13:30:51Z',
                interval_sec=60,
                pid_running=lambda pid: False,
                sample_process=lambda pid: (_ for _ in ()).throw(
                    AssertionError('dead pid must not be sampled')
                ),
            )
            self.assertEqual(code, 0)
            rows = [
                json.loads(line)
                for line in path.read_text(encoding='utf-8').splitlines()
                if line.strip()
            ]
            self.assertEqual(len(rows), 1)
            self.assertFalse(rows[0]['alive'])
            self.assertFalse(rows[0]['ac29_passed'])
            self.assertFalse(rows[0]['live_working_set'])
            self.assertIsNone(rows[0]['soak_hours'])

    def test_heartbeat_sleeps_then_exits_after_app_gone(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'hd033-soak-resources.jsonl'
            state = {'n': 0}

            def running(_pid):
                state['n'] += 1
                return state['n'] == 1

            with patch.object(working_set_cli.time, 'sleep') as slept:
                code = working_set_cli.run_resource_heartbeat(
                    12,
                    path,
                    '2026-09-10T13:30:51Z',
                    interval_sec=60,
                    pid_running=running,
                    sample_process=lambda pid: {
                        'working_set_bytes': 50,
                        'private_bytes': 40,
                        'handle_count': 3,
                        'process_count': 1,
                    },
                )
            self.assertEqual(code, 0)
            slept.assert_called_once_with(60)
            rows = [
                json.loads(line)
                for line in path.read_text(encoding='utf-8').splitlines()
                if line.strip()
            ]
            self.assertEqual(len(rows), 2)
            self.assertTrue(rows[0]['alive'])
            self.assertEqual(rows[0]['working_set_bytes'], 50)
            self.assertFalse(rows[1]['alive'])

    def test_ac29_passed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_working_set(root, overlay_overrides={'ac29_passed': True})
            with self.assertRaises(QualityError) as ctx:
                validate_soak_working_set(root)
            self.assertEqual(str(ctx.exception), 'ac29_passed')

    def test_live_working_set_true_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_working_set(root, overlay_overrides={'live_working_set': True})
            with self.assertRaises(QualityError) as ctx:
                validate_soak_working_set(root)
            self.assertEqual(str(ctx.exception), 'live_working_set_claimed')

    def test_eight_hour_executed_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_working_set(
                root, overlay_overrides={'eight_hour_soak_executed': True}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_soak_working_set(root)
            self.assertEqual(str(ctx.exception), 'eight_hour_soak_executed')

    def test_soak_hours_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_working_set(root, overlay_overrides={'soak_hours': 8})
            with self.assertRaises(QualityError) as ctx:
                validate_soak_working_set(root)
            self.assertEqual(str(ctx.exception), 'invented_timings')

    def test_one_quarter_pane_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_working_set(
                root, overlay_overrides={'one_quarter_pane_lab': True}
            )
            with self.assertRaises(QualityError) as ctx:
                validate_soak_working_set(root)
            self.assertEqual(str(ctx.exception), 'one_quarter_pane_lab_claimed')

    def test_open_close_100_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_working_set(root, overlay_overrides={'open_close_100': True})
            with self.assertRaises(QualityError) as ctx:
                validate_soak_working_set(root)
            self.assertEqual(str(ctx.exception), 'open_close_100_claimed')

    def test_sampler_in_start_owned_pids_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_soak_start(root)
            _copy_live_working_set_not_run(root)
            start = json.loads(
                (root / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
                    encoding='utf-8'
                )
            )
            owned = list(start['owned_pids'])
            doc = _overlay_doc(start, sampler_pid=owned[0])
            (root / 'evidence' / 'quality' / 'live-soak-working-set.json').write_text(
                json.dumps(doc), encoding='utf-8'
            )
            with self.assertRaises(QualityError) as ctx:
                validate_soak_working_set(root)
            self.assertEqual(str(ctx.exception), 'sampler_added_to_start_owned_pids')

    def test_ci_and_justfile_do_not_record(self):
        ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
        just = (ROOT / 'justfile').read_text(encoding='utf-8')
        self.assertNotIn('record_soak_working_set.py --record', ci)
        self.assertNotIn('record_soak_working_set.py --record', just)


if __name__ == '__main__':
    unittest.main()
