from __future__ import annotations

import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.lease import (
    BLOCKED_CATEGORY,
    CAPTURE_REL,
    FIXTURE_REL,
    MATRIX_SCENARIOS,
    REAL_INDEX_REL,
    LeaseError,
    check_terminal_lease_matrix,
    map_lease,
    validate_terminal_lease_matrix,
)
from herddesk_g0.protocol import classify_stream_end
from herddesk_g0.evidence import EvidenceError, check_evidence, validate_evidence
import validate_repository as repository


def _load(rel: str) -> dict:
    return json.loads((ROOT / rel).read_text(encoding='utf-8'))


def _bundle():
    fixture = _load(FIXTURE_REL)
    real_index = _load(REAL_INDEX_REL)
    capture = _load(CAPTURE_REL)
    baseline = _load('evidence/compatibility-baseline.json')
    endpoint = _load('evidence/runtime/windows-endpoint-matrix.blocked.json')
    return fixture, real_index, capture, baseline, endpoint


def _check(fixture=None, real_index=None, capture=None, baseline=None, endpoint_capture=None):
    shipped = _bundle()
    return check_terminal_lease_matrix(
        shipped[0] if fixture is None else fixture,
        shipped[1] if real_index is None else real_index,
        shipped[2] if capture is None else capture,
        shipped[3] if baseline is None else baseline,
        shipped[4] if endpoint_capture is None else endpoint_capture,
    )


def _evidence_bundle():
    baseline = _load('evidence/compatibility-baseline.json')
    matrix = _load('evidence/version-support-matrix.json')
    documents = {}
    for path in sorted((ROOT / 'evidence' / 'runtime').glob('*.json')):
        documents['evidence/runtime/' + path.name] = json.loads(
            path.read_text(encoding='utf-8'))
    return baseline, matrix, documents


class TerminalLeaseTests(unittest.TestCase):
    def test_shipped_matrix_is_simulation_and_not_ac05(self):
        result = validate_terminal_lease_matrix(ROOT)
        self.assertEqual(result['lease_validation'], 'passed')
        self.assertFalse(result['windows_verified'])
        self.assertFalse(result['ac05_passed'])
        self.assertEqual(result['ac05_c1'], 'blocked')
        self.assertEqual(result['live_matrix'], 'blocked')
        self.assertEqual(result['blocked_category'], BLOCKED_CATEGORY)
        self.assertTrue(result['simulation'])
        self.assertEqual(result['rows'], 14)
        repo = repository.validate()
        self.assertEqual(repo['structural_validation'], 'passed')
        self.assertEqual(repo['lease_validation'], 'passed')
        self.assertFalse(repo['windows_verified'])
        self.assertFalse(repo['ac05_passed'])
        evidence = validate_evidence(ROOT)
        self.assertFalse(evidence['windows_verified'])

    def test_shipped_fixture_has_fourteen_blocked_rows(self):
        fixture = _load(FIXTURE_REL)
        self.assertIs(fixture['simulation'], True)
        self.assertIs(fixture['runtime_pass'], False)
        self.assertIs(fixture['windows_verified'], False)
        self.assertIs(fixture['ac05_passed'], False)
        self.assertIs(fixture['herdr_executed'], False)
        self.assertIs(fixture['real_captures'], False)
        self.assertEqual(fixture['fixture_origin'], 'synthetic')
        self.assertEqual(fixture['all_live_checks'], 'blocked')
        self.assertEqual(fixture['blocked_category'], BLOCKED_CATEGORY)
        scenarios = [item['scenario'] for item in fixture['cases']]
        self.assertEqual(scenarios, list(MATRIX_SCENARIOS))
        self.assertEqual(len(scenarios), 14)

    def test_real_index_is_empty_placeholder(self):
        index = _load(REAL_INDEX_REL)
        self.assertEqual(index['kind'], 'real_terminal_capture_index')
        self.assertEqual(index['captures'], [])
        self.assertIs(index['herdr_executed'], False)
        self.assertIs(index['runtime_pass'], False)
        self.assertIs(index['ac05_passed'], False)
        self.assertEqual(index['blocked_category'], BLOCKED_CATEGORY)

    def test_shipped_capture_consumes_hd001_residual(self):
        capture = _load(CAPTURE_REL)
        baseline = _load('evidence/compatibility-baseline.json')
        self.assertEqual(capture['blocked_category'], BLOCKED_CATEGORY)
        self.assertIs(capture['herdr_executed'], False)
        self.assertIs(capture['ac05_passed'], False)
        self.assertIs(capture['windows_verified'], False)
        self.assertEqual(
            baseline['runtime_verification']['windows_terminal_lease'], 'blocked')
        matches = [
            item for item in baseline['records']
            if item['environment'] == 'windows_terminal_lease'
        ]
        self.assertEqual(len(matches), 1)
        self.assertEqual(matches[0]['blocked_category'], BLOCKED_CATEGORY)
        self.assertNotEqual(matches[0]['subject'], 'herdr-windows-endpoint-matrix')

    def test_lease_record_is_independent_of_endpoint(self):
        capture = _load(CAPTURE_REL)
        endpoint = _load('evidence/runtime/windows-endpoint-matrix.blocked.json')
        self.assertNotEqual(capture['capture_id'], endpoint['capture_id'])
        self.assertNotEqual(capture['kind'], endpoint['kind'])
        self.assertNotIn('named_pipe_connected', capture)
        self.assertNotEqual(
            capture['blocked_category'],
            endpoint['blocked_category'],
        )

    def test_observe_first_frame_does_not_verify_control(self):
        result = map_lease({
            'operation': 'observe',
            'access_before': 'disconnected',
            'control_verified_before': False,
            'first_frame_seen': True,
            'process_alive': True,
            'window_focused': True,
        })
        self.assertEqual(result['access'], 'observing')
        self.assertIs(result['control_verified'], False)
        self.assertEqual(result['code'], 'observing')

    def test_stdout_eof_is_not_pane_exit(self):
        end = classify_stream_end(stdout_eof_seen=True)
        self.assertEqual(end['kind'], 'stdout_eof')
        self.assertIs(end['pane_exit_verified'], False)
        self.assertTrue(end['stdout_eof_is_not_pane_exit'])
        result = map_lease({
            'operation': 'observe',
            'access_before': 'observing',
            'control_verified_before': False,
            'stdout_eof_seen': True,
        })
        self.assertEqual(result['stream_end'], 'stdout_eof')
        self.assertIs(result['pane_exit_verified'], False)

    def test_terminal_closed_is_not_pane_exit(self):
        end = classify_stream_end(terminal_closed_seen=True, stdout_eof_seen=True)
        self.assertEqual(end['kind'], 'terminal_closed')
        self.assertIs(end['pane_exit_verified'], False)
        result = map_lease({
            'operation': 'observe',
            'access_before': 'observing',
            'control_verified_before': False,
            'terminal_closed_seen': True,
        })
        self.assertEqual(result['stream_end'], 'terminal_closed')
        self.assertIs(result['pane_exit_verified'], False)

    def test_observe_cannot_send_input(self):
        result = map_lease({
            'operation': 'observe',
            'access_before': 'observing',
            'control_verified_before': False,
            'input_sent': True,
        })
        self.assertEqual(result['code'], 'observe_input_denied')
        self.assertIs(result['control_verified'], False)
        self.assertEqual(result['access'], 'observing')

    def test_control_not_inferred_from_frame_process_focus(self):
        result = map_lease({
            'operation': 'request_control',
            'access_before': 'observing',
            'control_verified_before': False,
            'first_frame_seen': True,
            'process_alive': True,
            'window_focused': True,
        })
        self.assertEqual(result['access'], 'acquiring')
        self.assertIs(result['control_verified'], False)
        self.assertEqual(result['code'], 'control_unconfirmed')

    def test_fictional_granted_is_rejected(self):
        result = map_lease({
            'operation': 'request_control',
            'access_before': 'observing',
            'control_verified_before': False,
            'observed_wire_type': 'terminal.granted',
            'adapter_proved_write_ownership': True,
        })
        self.assertEqual(result['code'], 'fictional_granted_rejected')
        self.assertEqual(result['access'], 'unknown')
        self.assertIs(result['control_verified'], False)

    def test_takeover_requires_confirmation(self):
        result = map_lease({
            'operation': 'request_takeover',
            'access_before': 'observing',
            'control_verified_before': False,
            'takeover_confirmed': False,
        })
        self.assertEqual(result['code'], 'takeover_not_confirmed')
        self.assertIs(result['control_verified'], False)

    def test_input_write_is_not_ack(self):
        result = map_lease({
            'operation': 'request_control',
            'access_before': 'acquiring',
            'control_verified_before': False,
            'input_sent': True,
            'input_acknowledged': False,
        })
        self.assertEqual(result['code'], 'input_result_unknown')
        self.assertIs(result['control_verified'], False)

    def test_input_ack_is_not_control_verified(self):
        result = map_lease({
            'operation': 'request_control',
            'access_before': 'acquiring',
            'control_verified_before': False,
            'input_sent': True,
            'input_acknowledged': True,
        })
        self.assertEqual(result['code'], 'control_unconfirmed')
        self.assertEqual(result['access'], 'acquiring')
        self.assertIs(result['control_verified'], False)

    def test_rejected_is_not_a_grant(self):
        result = map_lease({
            'operation': 'request_control',
            'access_before': 'observing',
            'control_verified_before': False,
            'control_signal': 'rejected',
        })
        self.assertEqual(result['code'], 'rejected')
        self.assertEqual(result['access'], 'observing')
        self.assertIs(result['control_verified'], False)

    def test_observe_adapter_proof_does_not_verify(self):
        result = map_lease({
            'operation': 'observe',
            'access_before': 'disconnected',
            'control_verified_before': False,
            'first_frame_seen': True,
            'adapter_proved_write_ownership': True,
        })
        self.assertEqual(result['access'], 'observing')
        self.assertEqual(result['code'], 'observing')
        self.assertIs(result['control_verified'], False)

    def test_resize_eof_does_not_keep_verified_control(self):
        result = map_lease({
            'operation': 'resize_while_verified',
            'access_before': 'controlling',
            'control_verified_before': True,
            'adapter_proved_write_ownership': True,
            'stdout_eof_seen': True,
            'resize_attempted': True,
            'resize_acknowledged': True,
        })
        self.assertEqual(result['access'], 'disconnected')
        self.assertEqual(result['stream_end'], 'stdout_eof')
        self.assertIs(result['pane_exit_verified'], False)
        self.assertIs(result['control_verified'], False)
        self.assertEqual(result['code'], 'disconnected')

    def test_resize_pane_death_does_not_keep_verified_control(self):
        result = map_lease({
            'operation': 'resize_while_verified',
            'access_before': 'controlling',
            'control_verified_before': True,
            'adapter_proved_write_ownership': True,
            'pane_alive_observed': False,
            'resize_attempted': True,
            'resize_acknowledged': True,
        })
        self.assertEqual(result['access'], 'disconnected')
        self.assertTrue(result['pane_exit_verified'])
        self.assertIs(result['control_verified'], False)
        self.assertEqual(result['code'], 'disconnected')

    def test_resize_without_current_adapter_proof_is_not_controlling(self):
        result = map_lease({
            'operation': 'resize_while_verified',
            'access_before': 'controlling',
            'control_verified_before': True,
            'adapter_proved_write_ownership': False,
            'resize_attempted': True,
        })
        self.assertEqual(result['code'], 'control_not_verified')
        self.assertNotEqual(result['access'], 'controlling')
        self.assertIs(result['control_verified'], False)

    def test_takeover_eof_is_not_verified_control(self):
        result = map_lease({
            'operation': 'request_takeover',
            'access_before': 'observing',
            'control_verified_before': False,
            'takeover_confirmed': True,
            'adapter_proved_write_ownership': True,
            'stdout_eof_seen': True,
        })
        self.assertEqual(result['access'], 'disconnected')
        self.assertEqual(result['stream_end'], 'stdout_eof')
        self.assertIs(result['control_verified'], False)
        self.assertIs(result['pane_exit_verified'], False)

    def test_resize_requires_verified_writer(self):
        result = map_lease({
            'operation': 'resize_while_verified',
            'access_before': 'observing',
            'control_verified_before': False,
            'resize_attempted': True,
        })
        self.assertEqual(result['code'], 'control_not_verified')
        self.assertIs(result['control_verified'], False)

    def test_release_is_not_pane_exit(self):
        result = map_lease({
            'operation': 'release',
            'access_before': 'controlling',
            'control_verified_before': True,
            'bridge_process_exited': True,
            'pane_alive_observed': True,
            'daemon_alive_observed': True,
            'release_acknowledged': False,
        })
        self.assertEqual(result['access'], 'observing')
        self.assertIs(result['control_verified'], False)
        self.assertEqual(result['stream_end'], 'bridge_process_exit')
        self.assertIs(result['pane_exit_verified'], False)
        self.assertEqual(result['code'], 'release_unacknowledged')

    def test_core_lease_probe_is_pure_mapping(self):
        text = (ROOT / 'src' / 'HerdDesk.Core' / 'TerminalLeaseProbe.cs').read_text(
            encoding='utf-8')
        for needle in (
            'GetEnvironmentVariable',
            'GetFolderPath',
            'NamedPipeClientStream',
            'Process.Start',
            'DllImport',
            'System.IO.Pipes',
            'JsonRpc',
            'herdr terminal',
        ):
            self.assertNotIn(needle, text)
        self.assertIn('fictional_granted_rejected', text)
        contracts = (ROOT / 'src' / 'HerdDesk.Contracts' / 'LeaseModels.cs').read_text(
            encoding='utf-8')
        self.assertNotIn('Granted', contracts)

    def test_fixture_runtime_pass_is_rejected(self):
        fixture, *_ = _bundle()
        fixture['runtime_pass'] = True
        with self.assertRaises(LeaseError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_fixture_cannot_claim_ac05_passed(self):
        fixture, *_ = _bundle()
        fixture['ac05_passed'] = True
        with self.assertRaises(LeaseError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'ac05_claimed_passed')

    def test_real_index_with_captures_is_rejected(self):
        _, real_index, *_ = _bundle()
        real_index['captures'] = ['synthetic-as-real.ndjson']
        with self.assertRaises(LeaseError) as ctx:
            _check(real_index=real_index)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_capture_success_looks_like_runtime_is_rejected(self):
        _, _, capture, *_ = _bundle()
        capture['result'] = 'passed'
        capture['exit_code'] = 0
        capture['stdout_sha256'] = 'a' * 64
        with self.assertRaises(LeaseError) as ctx:
            _check(capture=capture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_row_live_result_passed_is_rejected(self):
        fixture, *_ = _bundle()
        fixture['cases'][0]['live_result'] = 'passed'
        with self.assertRaises(LeaseError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_fictional_granted_required_as_success_is_rejected(self):
        fixture, *_ = _bundle()
        fixture['cases'][9]['observation']['observed_wire_type'] = 'terminal.granted'
        with self.assertRaises(LeaseError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'fictional_granted_required')

    def test_control_verified_from_frame_in_fixture_is_rejected(self):
        fixture, *_ = _bundle()
        case = fixture['cases'][6]
        case['expect']['control_verified'] = True
        case['expect']['access'] = 'controlling'
        case['expect']['code'] = 'control_verified'
        with self.assertRaises(LeaseError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'control_verified_from_frame_process_focus')

    def test_control_verified_without_adapter_in_fixture_is_rejected(self):
        fixture, *_ = _bundle()
        case = fixture['cases'][5]
        case['expect']['control_verified'] = True
        case['expect']['access'] = 'controlling'
        case['expect']['code'] = 'control_verified'
        with self.assertRaises(LeaseError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'control_verified_without_adapter')

    def test_eof_classified_as_pane_exit_in_fixture_is_rejected(self):
        fixture, *_ = _bundle()
        fixture['cases'][1]['expect']['pane_exit_verified'] = True
        fixture['cases'][1]['expect']['stream_end'] = 'pane_exit'
        with self.assertRaises(LeaseError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'eof_classified_as_pane_exit')

    def test_observe_input_accepted_in_fixture_is_rejected(self):
        fixture, *_ = _bundle()
        fixture['cases'][3]['expect']['code'] = 'observing'
        with self.assertRaises(LeaseError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'observe_input_not_denied')

    def test_windows_terminal_lease_success_without_runtime_is_rejected(self):
        baseline, matrix, documents = _evidence_bundle()
        baseline['runtime_verification']['windows_terminal_lease'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            check_evidence(baseline, matrix, documents)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_endpoint_capture_cannot_stand_in_for_lease(self):
        endpoint = _load('evidence/runtime/windows-endpoint-matrix.blocked.json')
        with self.assertRaises(LeaseError) as ctx:
            _check(capture=endpoint)
        self.assertIn(str(ctx.exception), {
            'missing_record_field',
            'runtime_records_not_independent',
            'missing_blocked_category',
        })


if __name__ == '__main__':
    unittest.main()
