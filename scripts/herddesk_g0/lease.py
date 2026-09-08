"""HD-004 terminal-lease diagnostics.

Maps observed CLI/protocol facts to TerminalAccess. This module does not
run herdr, send input, or invent a Granted wire message. Synthetic fixtures
are not observe/control runtime proof and do not pass AC05.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from herddesk_g0.protocol import classify_stream_end


class LeaseError(ValueError):
    """Stable lease-rule code; the message is the code only."""


FIXTURE_REL = 'tests/fixtures/lease-cases.json'
REAL_INDEX_REL = 'tests/fixtures/real-terminal-v082/index.json'
CAPTURE_REL = 'evidence/runtime/windows-terminal-lease.capture.json'
BASELINE_REL = 'evidence/compatibility-baseline.json'
ENDPOINT_CAPTURE_REL = 'evidence/runtime/windows-endpoint-matrix.blocked.json'
BLOCKED_CATEGORY = 'no_authorized_isolated_pane_or_live_herdr_grant'
ENDPOINT_BLOCKED_CATEGORY = 'no_authorized_isolated_windows_endpoint_or_live_herdr_grant'
MATRIX_SCENARIOS = (
    'observe_first_frame',
    'observe_stdout_eof_not_pane_exit',
    'observe_terminal_closed_not_pane_exit',
    'observe_cannot_send_input',
    'control_unconfirmed_stays_acquiring',
    'control_busy_second_controller_no_takeover',
    'control_not_inferred_from_frame_process_focus',
    'fictional_granted_rejected',
    'takeover_without_confirmation',
    'takeover_confirmed_adapter_proved',
    'no_input_ack_unknown',
    'resize_requires_verified_writer',
    'resize_unacknowledged_while_verified',
    'release_ends_bridge_pane_continues',
)
CHILD_AC_FIELDS = (
    'ac05_c1', 'ac06_c1', 'ac07_c1', 'ac14_c1', 'ac15_c1', 'ac16_c1',
)
REQUIRED_LIVE_COVERS = frozenset({
    'observe_first_frame',
    'observe_bridge_exit_not_pane_exit',
    'control_unconfirmed_after_stdin_write',
    'release_ends_bridge_pane_continues',
})
PANE_DEATH_ENDS = frozenset({'pane_exit', 'pane_death'})
STREAM_ENDS_NOT_PANE = frozenset({
    'stdout_eof', 'terminal_closed', 'bridge_process_exit',
})
FRAME_PAYLOAD_KEYS = frozenset({'bytes', 'data', 'payload'})
PROBE_TEXT_KEYS = frozenset({'stdout', 'stderr', 'input_text', 'input'})
SUCCESS_RESULTS = frozenset({
    'passed', 'verified', 'compatible', 'success', 'ok',
})
OPERATIONS = {
    'observe', 'request_control', 'request_takeover',
    'resize_while_verified', 'release',
}


def validate_terminal_lease_matrix(root: Path) -> dict[str, Any]:
    root = Path(root)
    fixture = _load_json(root / FIXTURE_REL)
    real_index = _load_json(root / REAL_INDEX_REL)
    capture = _load_json(root / CAPTURE_REL)
    baseline = _load_json(root / BASELINE_REL)
    endpoint_capture = _load_json(root / ENDPOINT_CAPTURE_REL)
    return check_terminal_lease_matrix(
        fixture, real_index, capture, baseline, endpoint_capture)


def check_terminal_lease_matrix(
    fixture: dict[str, Any],
    real_index: dict[str, Any],
    capture: dict[str, Any],
    baseline: dict[str, Any] | None = None,
    endpoint_capture: dict[str, Any] | None = None,
) -> dict[str, Any]:
    _reject_promotion(fixture, real_index, capture)
    _check_fixture_shape(fixture)
    _check_real_index(real_index)
    _check_capture(capture)
    if endpoint_capture is not None:
        _check_independent_of_endpoint(capture, endpoint_capture)
    if baseline is not None:
        _check_baseline(baseline, capture)
    for case in fixture['cases']:
        _check_case_invariants(case)
        actual = map_lease(case['observation'])
        _check_case_expect(case, actual)
    return {
        'lease_validation': 'passed',
        'windows_verified': False,
        'ac05_passed': False,
        'ac05_c1': capture.get('ac05_c1') or 'recorded',
        'ac06_c1': capture.get('ac06_c1') or 'blocked',
        'ac07_c1': capture.get('ac07_c1') or 'blocked',
        'ac14_c1': capture.get('ac14_c1') or 'blocked',
        'ac15_c1': capture.get('ac15_c1') or 'blocked',
        'ac16_c1': capture.get('ac16_c1') or 'blocked',
        'live_matrix': capture.get('live_matrix') or 'recorded_subset',
        'control_verified': False,
        'simulation': True,
        'rows': len(fixture['cases']),
    }


def map_lease(observation: dict[str, Any]) -> dict[str, Any]:
    if not isinstance(observation, dict):
        return _result('unknown', False, 'none', False,
                       'invalid_observation', 'diag-invalid-observation')
    operation = observation.get('operation')
    if operation not in OPERATIONS:
        return _result('unknown', False, 'none', False,
                       'invalid_observation', 'diag-invalid-observation')
    stream = classify_stream_end(
        stdout_eof_seen=_flag(observation, 'stdout_eof_seen'),
        terminal_closed_seen=_flag(observation, 'terminal_closed_seen'),
        bridge_process_exited=_flag(observation, 'bridge_process_exited'),
        pane_alive_observed=_opt_bool(observation, 'pane_alive_observed'),
        daemon_alive_observed=_opt_bool(observation, 'daemon_alive_observed'),
    )
    stream_end = stream['kind']
    pane_exit = stream['pane_exit_verified']
    if _is_fictional_granted(observation):
        return _result('unknown', False, stream_end, pane_exit,
                       'fictional_granted_rejected', 'diag-fictional-granted')
    if operation == 'observe' and _flag(observation, 'input_sent'):
        return _result('observing', False, stream_end, pane_exit,
                       'observe_input_denied', 'diag-observe-input-denied')
    if operation == 'observe':
        return _map_observe(stream_end, pane_exit)
    if operation == 'request_control':
        return _map_request_control(observation, stream_end, pane_exit)
    if operation == 'request_takeover':
        return _map_request_takeover(observation, stream_end, pane_exit)
    if operation == 'resize_while_verified':
        return _map_resize(observation, stream_end, pane_exit)
    return _map_release(observation, stream_end, pane_exit)


def _map_observe(stream_end: str, pane_exit: bool) -> dict[str, Any]:
    if pane_exit:
        return _result('disconnected', False, stream_end, True,
                       'disconnected', 'diag-disconnected')
    if stream_end != 'none':
        return _result('disconnected', False, stream_end, False,
                       'disconnected', 'diag-disconnected')
    return _result('observing', False, 'none', False, 'observing', 'diag-observing')


def _map_request_control(
    observation: dict[str, Any], stream_end: str, pane_exit: bool,
) -> dict[str, Any]:
    if pane_exit:
        return _result('disconnected', False, stream_end, True,
                       'disconnected', 'diag-disconnected')
    signal = observation.get('control_signal') or 'none'
    if signal == 'busy':
        return _result('observing', False, 'none', False, 'busy', 'diag-busy')
    if signal == 'rejected':
        return _result('observing', False, 'none', False, 'rejected', 'diag-rejected')
    if signal == 'takeover_required' and not _flag(observation, 'takeover_confirmed'):
        return _result('observing', False, 'none', False,
                       'takeover_required', 'diag-takeover-required')
    if signal == 'unknown':
        return _result('unknown', False, stream_end, False,
                       'unknown_control_signal', 'diag-unknown-signal')
    if _adapter_proved(observation) and stream_end == 'none':
        return _result('controlling', True, 'none', False,
                       'control_verified', 'diag-control-verified')
    if stream_end != 'none':
        return _result('disconnected', False, stream_end, False,
                       'disconnected', 'diag-disconnected')
    if _flag(observation, 'input_sent') and not _flag(observation, 'input_acknowledged'):
        return _result('acquiring', False, 'none', False,
                       'input_result_unknown', 'diag-input-result-unknown')
    return _result('acquiring', False, 'none', False,
                   'control_unconfirmed', 'diag-control-unconfirmed')


def _map_request_takeover(
    observation: dict[str, Any], stream_end: str, pane_exit: bool,
) -> dict[str, Any]:
    if not _flag(observation, 'takeover_confirmed'):
        return _result('observing', False, stream_end, pane_exit,
                       'takeover_not_confirmed', 'diag-takeover-not-confirmed')
    if pane_exit:
        return _result('disconnected', False, stream_end, True,
                       'disconnected', 'diag-disconnected')
    if stream_end != 'none':
        return _result('disconnected', False, stream_end, False,
                       'disconnected', 'diag-disconnected')
    if _adapter_proved(observation):
        return _result('controlling', True, 'none', False,
                       'control_verified', 'diag-control-verified')
    return _result('acquiring', False, 'none', False, 'acquiring', 'diag-acquiring')


def _map_resize(
    observation: dict[str, Any], stream_end: str, pane_exit: bool,
) -> dict[str, Any]:
    if pane_exit:
        return _result('disconnected', False, stream_end, True,
                       'disconnected', 'diag-disconnected')
    if stream_end != 'none':
        return _result('disconnected', False, stream_end, False,
                       'disconnected', 'diag-disconnected')
    access_before = observation.get('access_before') or 'unknown'
    allowed = {
        'disconnected', 'observing', 'acquiring', 'controlling', 'unknown',
    }
    if (
        access_before != 'controlling'
        or observation.get('control_verified_before') is not True
        or not _adapter_proved(observation)
    ):
        return _result(
            access_before if access_before in allowed else 'unknown',
            False,
            'none',
            False,
            'control_not_verified',
            'diag-control-not-verified',
        )
    if not _flag(observation, 'resize_acknowledged'):
        return _result('controlling', True, 'none', False,
                       'resize_unacknowledged', 'diag-resize-unacknowledged')
    return _result('controlling', True, 'none', False, 'resized', 'diag-resized')


def _map_release(
    observation: dict[str, Any], stream_end: str, pane_exit: bool,
) -> dict[str, Any]:
    if _flag(observation, 'release_acknowledged'):
        code, diagnostic = 'released', 'diag-released'
    else:
        code, diagnostic = 'release_unacknowledged', 'diag-release-unacknowledged'
    if pane_exit:
        return _result('disconnected', False, stream_end, True, code, diagnostic)
    return _result('observing', False, stream_end, False, code, diagnostic)


def _adapter_proved(observation: dict[str, Any]) -> bool:
    return _flag(observation, 'adapter_proved_write_ownership') and not _is_fictional_granted(
        observation)


def _is_fictional_granted(observation: dict[str, Any]) -> bool:
    return (
        observation.get('observed_wire_type') == 'terminal.granted'
        or observation.get('control_signal') == 'granted'
    )


def _flag(observation: dict[str, Any], key: str) -> bool:
    return observation.get(key) is True


def _opt_bool(observation: dict[str, Any], key: str) -> bool | None:
    if key not in observation or observation[key] is None:
        return None
    if observation[key] is True:
        return True
    if observation[key] is False:
        return False
    return None


def _result(
    access: str,
    control_verified: bool,
    stream_end: str,
    pane_exit_verified: bool,
    code: str,
    diagnostic_id: str,
) -> dict[str, Any]:
    if access != 'controlling':
        control_verified = False
    elif not control_verified:
        access = 'acquiring'
    return {
        'access': access,
        'control_verified': control_verified,
        'stream_end': stream_end,
        'pane_exit_verified': pane_exit_verified,
        'code': code,
        'diagnostic_id': diagnostic_id,
    }


def _reject_promotion(
    fixture: dict[str, Any],
    real_index: dict[str, Any],
    capture: dict[str, Any],
) -> None:
    for doc in (fixture, real_index, capture):
        if doc.get('runtime_pass') is True:
            raise LeaseError('evidence_level_promotion')
        if doc.get('windows_verified') is True:
            raise LeaseError('evidence_level_promotion')
        if doc.get('ac05_passed') is True:
            raise LeaseError('ac05_claimed_passed')
        if _is_success(doc.get('result')) or _is_success(doc.get('live_result')):
            raise LeaseError('evidence_level_promotion')
        if _is_success(doc.get('all_live_checks')) or _is_success(doc.get('live_matrix')):
            raise LeaseError('evidence_level_promotion')
        for field in CHILD_AC_FIELDS:
            if _is_success(doc.get(field)):
                raise LeaseError('evidence_level_promotion')
    if fixture.get('simulation') is not True:
        raise LeaseError('evidence_level_promotion')
    if fixture.get('kind') != 'synthetic_terminal_lease_matrix':
        raise LeaseError('missing_record_field')
    if fixture.get('fixture_origin') != 'synthetic':
        raise LeaseError('evidence_level_promotion')
    if fixture.get('real_captures') is True:
        raise LeaseError('evidence_level_promotion')


def _check_fixture_shape(fixture: dict[str, Any]) -> None:
    if fixture.get('runtime_pass') is not False:
        raise LeaseError('evidence_level_promotion')
    if fixture.get('windows_verified') is not False:
        raise LeaseError('evidence_level_promotion')
    if fixture.get('ac05_passed') is not False:
        raise LeaseError('ac05_claimed_passed')
    if fixture.get('herdr_executed') is not False:
        raise LeaseError('evidence_level_promotion')
    if fixture.get('all_live_checks') != 'blocked':
        raise LeaseError('missing_record_field')
    if fixture.get('blocked_category') != BLOCKED_CATEGORY:
        raise LeaseError('missing_blocked_category')
    cases = fixture.get('cases')
    if not isinstance(cases, list) or len(cases) != len(MATRIX_SCENARIOS):
        raise LeaseError('missing_matrix_row')
    seen: list[str] = []
    for case in cases:
        if not isinstance(case, dict):
            raise LeaseError('missing_matrix_row')
        scenario = case.get('scenario')
        if scenario not in MATRIX_SCENARIOS or scenario in seen:
            raise LeaseError('missing_matrix_row')
        seen.append(scenario)
        if case.get('live_result') != 'blocked':
            raise LeaseError('evidence_level_promotion')
        if case.get('evidence_level') != 'synthetic':
            raise LeaseError('evidence_level_promotion')
        if case.get('simulation') is not True:
            raise LeaseError('evidence_level_promotion')
        expect = case.get('expect')
        if not isinstance(expect, dict):
            raise LeaseError('missing_record_field')
    if seen != list(MATRIX_SCENARIOS):
        raise LeaseError('missing_matrix_row')


def _check_real_index(index: dict[str, Any]) -> None:
    if index.get('kind') != 'real_terminal_capture_index':
        raise LeaseError('missing_record_field')
    if index.get('runtime_pass') is not False:
        raise LeaseError('evidence_level_promotion')
    if index.get('windows_verified') is not False:
        raise LeaseError('evidence_level_promotion')
    if index.get('ac05_passed') is not False:
        raise LeaseError('ac05_claimed_passed')
    if index.get('herdr_executed') is not False:
        raise LeaseError('evidence_level_promotion')
    if index.get('live_matrix') != 'blocked':
        raise LeaseError('evidence_level_promotion')
    if index.get('blocked_category') != BLOCKED_CATEGORY:
        raise LeaseError('missing_blocked_category')
    captures = index.get('captures')
    if captures is None:
        captures = []
    if not isinstance(captures, list) or captures:
        raise LeaseError('evidence_level_promotion')


def _check_capture(capture: dict[str, Any]) -> None:
    if capture.get('template') is True:
        raise LeaseError('evidence_level_promotion')
    if capture.get('kind') != 'windows_terminal_lease':
        raise LeaseError('missing_record_field')
    if capture.get('ac05_passed') is not False:
        raise LeaseError('ac05_claimed_passed')
    if capture.get('windows_verified') is not False:
        raise LeaseError('evidence_level_promotion')
    if _is_success(capture.get('result')) or _is_success(capture.get('live_matrix')):
        raise LeaseError('evidence_level_promotion')
    if capture.get('control_verified') is True:
        raise LeaseError('control_verified_without_adapter')
    covers = capture.get('covers')
    if not isinstance(covers, list) or set(covers) != set(MATRIX_SCENARIOS):
        raise LeaseError('missing_matrix_row')
    if capture.get('evidence_level') == 'isolated_windows_runtime':
        if capture.get('result') != 'recorded':
            raise LeaseError('evidence_level_promotion')
        if capture.get('herdr_executed') is not True:
            raise LeaseError('missing_record_field')
        if capture.get('takeover_used') is not False:
            raise LeaseError('evidence_level_promotion')
        if capture.get('pane_exit_verified') is not False:
            raise LeaseError('eof_classified_as_pane_exit')
        if capture.get('wire_shape_checks_passed') is not True:
            raise LeaseError('missing_record_field')
        if capture.get('ac05_c1') != 'recorded':
            raise LeaseError('missing_record_field')
        for field in ('ac06_c1', 'ac07_c1', 'ac14_c1', 'ac15_c1', 'ac16_c1'):
            if _is_success(capture.get(field)):
                raise LeaseError('evidence_level_promotion')
        _check_live_probes(capture)
        return
    if capture.get('result') != 'blocked':
        raise LeaseError('evidence_level_promotion')
    if capture.get('blocked_category') != BLOCKED_CATEGORY:
        raise LeaseError('missing_blocked_category')
    if capture.get('herdr_executed') is not False:
        raise LeaseError('evidence_level_promotion')
    if capture.get('live_matrix') != 'blocked':
        raise LeaseError('evidence_level_promotion')
    if capture.get('exit_code') == 0 and capture.get('stdout_sha256'):
        raise LeaseError('evidence_level_promotion')


def _check_live_probes(capture: dict[str, Any]) -> None:
    live_covers = capture.get('live_covers')
    if not isinstance(live_covers, list) or set(live_covers) != REQUIRED_LIVE_COVERS:
        raise LeaseError('missing_matrix_row')
    probes = capture.get('probes')
    if not isinstance(probes, dict) or 'observe' not in probes or 'control_input' not in probes:
        raise LeaseError('missing_record_field')
    for probe in probes.values():
        if not isinstance(probe, dict):
            raise LeaseError('missing_record_field')
        _check_live_probe(probe)
    observe = probes['observe']
    control_input = probes['control_input']
    if control_input.get('input_sent') is not True:
        raise LeaseError('missing_record_field')
    if control_input.get('control_verified') is True:
        raise LeaseError('control_verified_without_adapter')
    if control_input.get('takeover_used') is not False:
        raise LeaseError('evidence_level_promotion')
    if observe.get('wire_shape_checks_passed') is not True:
        raise LeaseError('missing_record_field')
    if control_input.get('wire_shape_checks_passed') is not True:
        raise LeaseError('missing_record_field')
    frames = control_input.get('frames')
    if not isinstance(frames, list) or len(frames) < 2:
        raise LeaseError('missing_record_field')
    if frames[0].get('full') is not True:
        raise LeaseError('missing_record_field')
    if not any(item.get('full') is False for item in frames[1:]):
        raise LeaseError('missing_record_field')


def _check_live_probe(probe: dict[str, Any]) -> None:
    if probe.get('control_verified') is True:
        raise LeaseError('control_verified_without_adapter')
    stream_end = probe.get('stream_end')
    if (
        stream_end not in STREAM_ENDS_NOT_PANE
        and stream_end not in PANE_DEATH_ENDS
        and stream_end != 'none'
    ):
        raise LeaseError('missing_record_field')
    if stream_end in PANE_DEATH_ENDS or probe.get('pane_exit_verified') is True:
        raise LeaseError('eof_classified_as_pane_exit')
    if stream_end in STREAM_ENDS_NOT_PANE:
        if probe.get('pane_alive_after_bridge_exit') is not True:
            raise LeaseError('eof_classified_as_pane_exit')
    pane_alive = probe.get('pane_alive_after_bridge_exit')
    classified = classify_stream_end(
        stdout_eof_seen=probe.get('stdout_eof_seen') is True,
        terminal_closed_seen=probe.get('terminal_closed_seen') is True,
        bridge_process_exited=stream_end == 'bridge_process_exit',
        pane_alive_observed=True if pane_alive is True else (
            False if pane_alive is False else None),
    )
    if classified['pane_exit_verified'] or (
        classified['kind'] in STREAM_ENDS_NOT_PANE
        and probe.get('pane_exit_verified') is True
    ):
        raise LeaseError('eof_classified_as_pane_exit')
    if PROBE_TEXT_KEYS & set(probe):
        raise LeaseError('payload_not_redacted')
    frames = probe.get('frames')
    if not isinstance(frames, list):
        return
    for frame in frames:
        if not isinstance(frame, dict):
            raise LeaseError('missing_record_field')
        if FRAME_PAYLOAD_KEYS & set(frame):
            raise LeaseError('payload_not_redacted')


def _check_independent_of_endpoint(
    capture: dict[str, Any],
    endpoint_capture: dict[str, Any],
) -> None:
    if capture.get('capture_id') == endpoint_capture.get('capture_id'):
        raise LeaseError('runtime_records_not_independent')
    if capture.get('kind') == endpoint_capture.get('kind'):
        raise LeaseError('runtime_records_not_independent')
    if capture.get('kind') == 'windows_endpoint_matrix':
        raise LeaseError('runtime_records_not_independent')
    if capture.get('blocked_category') == ENDPOINT_BLOCKED_CATEGORY:
        raise LeaseError('runtime_records_not_independent')
    if 'named_pipe_connected' in capture:
        raise LeaseError('runtime_records_not_independent')


def _check_baseline(baseline: dict[str, Any], capture: dict[str, Any]) -> None:
    verification = baseline.get('runtime_verification')
    if not isinstance(verification, dict):
        raise LeaseError('missing_record_field')
    lease_status = verification.get('windows_terminal_lease')
    if _is_success(lease_status):
        raise LeaseError('evidence_level_promotion')
    if lease_status not in {'blocked', 'recorded'}:
        raise LeaseError('missing_record_field')
    records = baseline.get('records')
    if not isinstance(records, list):
        raise LeaseError('missing_record_field')
    matches = [item for item in records if item.get('environment') == 'windows_terminal_lease']
    if len(matches) != 1:
        raise LeaseError('missing_record_field')
    record = matches[0]
    if _is_success(record.get('result')):
        raise LeaseError('evidence_level_promotion')
    if CAPTURE_REL not in record.get('attachments', []):
        raise LeaseError('missing_record_field')
    if lease_status == 'recorded':
        if record.get('result') != 'recorded':
            raise LeaseError('missing_record_field')
        if record.get('evidence_level') != 'isolated_windows_runtime':
            raise LeaseError('evidence_level_promotion')
        if capture.get('control_verified') is True:
            raise LeaseError('control_verified_without_adapter')
    else:
        if record.get('result') != 'blocked':
            raise LeaseError('evidence_level_promotion')
        if record.get('blocked_category') != BLOCKED_CATEGORY:
            raise LeaseError('missing_blocked_category')
    endpoint_matches = [
        item for item in records if item.get('environment') == 'windows_endpoint'
    ]
    if endpoint_matches:
        endpoint = endpoint_matches[0]
        if record.get('subject') == endpoint.get('subject'):
            raise LeaseError('runtime_records_not_independent')
        if set(record.get('attachments') or ()) & set(endpoint.get('attachments') or ()):
            raise LeaseError('runtime_records_not_independent')


def _check_case_invariants(case: dict[str, Any]) -> None:
    observation = case['observation']
    expect = case['expect']
    if expect.get('control_verified') is True:
        if observation.get('observed_wire_type') == 'terminal.granted':
            raise LeaseError('fictional_granted_required')
        if observation.get('adapter_proved_write_ownership') is not True:
            if (
                observation.get('first_frame_seen')
                or observation.get('process_alive')
                or observation.get('window_focused')
            ):
                raise LeaseError('control_verified_from_frame_process_focus')
            raise LeaseError('control_verified_without_adapter')
    if expect.get('pane_exit_verified') is True:
        if observation.get('pane_alive_observed') is not False:
            raise LeaseError('eof_classified_as_pane_exit')
    if expect.get('stream_end') == 'pane_exit':
        raise LeaseError('eof_classified_as_pane_exit')
    if expect.get('code') == 'observe_input_denied' and observation.get('operation') != 'observe':
        raise LeaseError('matrix_expect_mismatch')
    if observation.get('operation') == 'observe' and observation.get('input_sent') is True:
        if expect.get('code') != 'observe_input_denied':
            raise LeaseError('observe_input_not_denied')


def _check_case_expect(case: dict[str, Any], actual: dict[str, Any]) -> None:
    expect = case['expect']
    for key in ('access', 'control_verified', 'stream_end', 'pane_exit_verified', 'code'):
        if actual.get(key) != expect.get(key):
            raise LeaseError('matrix_expect_mismatch')
    _assert_redacted(actual.get('code'))
    _assert_redacted(actual.get('diagnostic_id'))
    if actual.get('control_verified') is True and actual.get('access') != 'controlling':
        raise LeaseError('control_verified_without_adapter')


def _assert_redacted(value: Any) -> None:
    if not isinstance(value, str):
        return
    if '\\' in value or '/' in value or '%APPDATA%' in value.upper():
        raise LeaseError('path_not_redacted')


def _is_success(result: Any) -> bool:
    return result in SUCCESS_RESULTS


def _load_json(path: Path) -> dict[str, Any]:
    loaded = json.loads(path.read_text(encoding='utf-8'))
    if not isinstance(loaded, dict):
        raise LeaseError('missing_record_field')
    return loaded
