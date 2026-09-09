"""HD-005 renderer L1 diagnostics.

Pure-function specimens for UTF-8 chunking, epoch/seq gating, composition,
bounded queues, and web-message allowlists. Synthetic fixtures are not
WinUI, WebView2, IME, or native-renderer proof and do not pass AC08/AC09.
"""
from __future__ import annotations

import base64
import json
from pathlib import Path
from typing import Any

from herddesk_g0.protocol import ProtocolError, TerminalCaptureValidator


class RendererError(ValueError):
    """Stable renderer-rule code; the message is the code only."""


FIXTURE_REL = 'tests/fixtures/renderer-cases.json'
CAPTURE_REL = 'evidence/runtime/windows-renderer-ime.blocked.json'
BASELINE_REL = 'evidence/compatibility-baseline.json'
DECISION_REL = 'docs/spikes/renderer-decision.md'
LEASE_CAPTURE_REL = 'evidence/runtime/windows-terminal-lease.capture.json'
ENDPOINT_CAPTURE_REL = 'evidence/runtime/windows-endpoint-matrix.blocked.json'
BLOCKED_CATEGORY = 'no_authorized_winui_interactive_desktop_or_ime_grant'
ENDPOINT_BLOCKED_CATEGORY = 'no_authorized_isolated_windows_endpoint_or_live_herdr_grant'
SCHEMA_VERSION = 1
MAX_INPUT_BYTES = 64 * 1024
MAX_FRAME_BYTES = 8 * 1024 * 1024
DEFAULT_MAX_IN_FLIGHT = 256 * 1024
DEFAULT_MAX_FRAMES = 8
MATRIX_SCENARIOS = (
    'utf8_cjk_split',
    'utf8_emoji_split',
    'utf8_combining_split',
    'utf8_no_replacement_at_boundary',
    'control_bytes_not_normalized',
    'old_epoch_rejected',
    'seq_not_compared_across_epochs',
    'preedit_not_sent',
    'commit_once_committed_text',
    'observe_user_key_denied',
    'observe_emulator_reply_denied',
    'queue_bounded',
    'overflow_requires_full_reset',
    'parse_consumed_not_presented',
    'stale_ack_does_not_release',
    'web_unknown_type',
    'web_oversize',
    'web_wrong_epoch',
)
L3_UNTESTED = (
    'ime_preedit_candidate_window',
    'ime_commit_space_enter',
    'ime_shortcut_during_composition',
    'dpi_100_150_200',
    'cross_display_drag',
    'narrator_high_contrast',
    'native_raw_stream_candidate',
)
SUCCESS_RESULTS = frozenset({
    'passed', 'verified', 'compatible', 'success', 'ok',
})
ALLOWED_ORIGINS = frozenset({'user_key', 'committed_text', 'explicit_paste'})
ALLOWLIST = {
    ('frame.apply', 'host_to_renderer'),
    ('parse.consumed', 'renderer_to_host'),
    ('input.user_key', 'renderer_to_host'),
    ('input.committed_text', 'renderer_to_host'),
    ('input.explicit_paste', 'renderer_to_host'),
    ('input.emulator_reply', 'renderer_to_host'),
    ('ime.preedit', 'renderer_to_host'),
    ('terminal.resize', 'renderer_to_host'),
    ('selection.local', 'renderer_to_host'),
    ('mouse.intent', 'renderer_to_host'),
}
ORIGIN_FOR_TYPE = {
    'input.user_key': 'user_key',
    'input.committed_text': 'committed_text',
    'input.explicit_paste': 'explicit_paste',
    'input.emulator_reply': 'emulator_reply',
}


def validate_renderer_matrix(root: Path) -> dict[str, Any]:
    root = Path(root)
    fixture = _load_json(root / FIXTURE_REL)
    capture = _load_json(root / CAPTURE_REL)
    baseline = _load_json(root / BASELINE_REL)
    lease_capture = _load_json(root / LEASE_CAPTURE_REL)
    endpoint_capture = _load_json(root / ENDPOINT_CAPTURE_REL)
    if not (root / DECISION_REL).is_file():
        raise RendererError('missing_decision_document')
    return check_renderer_matrix(
        fixture, capture, baseline, lease_capture, endpoint_capture)


def check_renderer_matrix(
    fixture: dict[str, Any],
    capture: dict[str, Any],
    baseline: dict[str, Any] | None = None,
    lease_capture: dict[str, Any] | None = None,
    endpoint_capture: dict[str, Any] | None = None,
) -> dict[str, Any]:
    _reject_promotion(fixture, capture)
    _check_fixture_shape(fixture)
    _check_capture(capture)
    if lease_capture is not None:
        _check_independent(capture, lease_capture)
    if endpoint_capture is not None:
        _check_independent(capture, endpoint_capture)
        if capture.get('blocked_category') == ENDPOINT_BLOCKED_CATEGORY:
            raise RendererError('runtime_records_not_independent')
    if baseline is not None:
        _check_baseline(baseline, capture)
    for case in fixture['cases']:
        actual = run_case(case)
        _check_case_expect(case, actual)
    return {
        'renderer_validation': 'passed',
        'windows_verified': False,
        'ac08_passed': False,
        'ac09_passed': False,
        'live_matrix': 'blocked',
        'blocked_category': BLOCKED_CATEGORY,
        'simulation': True,
        'winui_executed': False,
        'webview2_executed': False,
        'ime_executed': False,
        'native_candidate_run': False,
        'rows': len(fixture['cases']),
    }


def assemble_utf8(chunks: list[bytes]) -> str:
    assembler = Utf8ChunkAssembler()
    for chunk in chunks:
        assembler.append(chunk)
    return assembler.text


def join_bytes(chunks: list[bytes]) -> bytes:
    return b''.join(chunks)


def evaluate_input_policy(
    *,
    access: str,
    control_verified: bool,
    origin: str,
    payload_bytes: int,
    context_epoch: int,
    input_epoch: int,
    valid_identity: bool = True,
    same_pane: bool = True,
) -> dict[str, Any]:
    if not valid_identity:
        return _decision(False, 'invalid_identity')
    if not same_pane:
        return _decision(False, 'wrong_pane')
    if type(context_epoch) is not int or context_epoch <= 0 or context_epoch != input_epoch:
        return _decision(False, 'stale_epoch')
    if access != 'controlling' or control_verified is not True:
        return _decision(False, 'control_not_verified')
    if origin not in ALLOWED_ORIGINS:
        return _decision(False, 'input_origin_denied')
    if type(payload_bytes) is not int or payload_bytes <= 0 or payload_bytes > MAX_INPUT_BYTES:
        return _decision(False, 'input_bytes_limit')
    return _decision(True, 'allowed')


def evaluate_composition(
    ime_event: str,
    *,
    access: str = 'controlling',
    control_verified: bool = True,
    origin: str | None = None,
    payload_bytes: int = 3,
    context_epoch: int = 1,
    input_epoch: int = 1,
) -> dict[str, Any]:
    if ime_event == 'preedit_update':
        return _decision(False, 'preedit_not_sent')
    if ime_event == 'key_while_composing':
        return _decision(False, 'ime_owns_shortcut')
    if origin is None:
        return _decision(False, 'input_origin_denied')
    if ime_event == 'commit' and origin != 'committed_text':
        return _decision(False, 'commit_origin_required')
    if ime_event == 'key_idle' and origin != 'user_key':
        return _decision(False, 'input_origin_denied')
    return evaluate_input_policy(
        access=access,
        control_verified=control_verified,
        origin=origin,
        payload_bytes=payload_bytes,
        context_epoch=context_epoch,
        input_epoch=input_epoch,
    )


def evaluate_web_message(message: dict[str, Any], context: dict[str, Any]) -> dict[str, Any]:
    if not isinstance(message, dict) or not isinstance(context, dict):
        return _decision(False, 'unknown_web_message_type')
    type_name = message.get('type')
    direction = message.get('direction')
    if (type_name, direction) not in ALLOWLIST:
        return _decision(False, 'unknown_web_message_type')
    if message.get('version') != SCHEMA_VERSION:
        return _decision(False, 'unsupported_web_message_version')
    context_epoch = context.get('epoch')
    if type(context_epoch) is not int or context_epoch <= 0 or message.get('epoch') != context_epoch:
        return _decision(False, 'stale_epoch')
    if message.get('pane') != context.get('pane'):
        return _decision(False, 'wrong_pane')
    if type_name == 'ime.preedit':
        return _decision(False, 'preedit_not_sent')
    payload = message.get('payload_bytes')
    limit = MAX_FRAME_BYTES if type_name == 'frame.apply' else MAX_INPUT_BYTES
    if type_name in ('parse.consumed', 'terminal.resize', 'selection.local', 'mouse.intent'):
        if payload != 0:
            return _decision(False, 'web_message_bytes_limit')
    elif type(payload) is not int or payload <= 0 or payload > limit:
        return _decision(False, 'web_message_bytes_limit')
    if type_name in ('frame.apply', 'parse.consumed', 'selection.local', 'mouse.intent'):
        return _decision(True, 'allowed')
    if type_name == 'terminal.resize':
        if context.get('access') != 'controlling' or context.get('control_verified') is not True:
            return _decision(False, 'control_not_verified')
        return _decision(True, 'allowed')
    origin = ORIGIN_FOR_TYPE[type_name]
    claimed = message.get('origin')
    if claimed is not None and claimed != origin:
        return _decision(False, 'input_origin_denied')
    return evaluate_input_policy(
        access=str(context.get('access') or ''),
        control_verified=context.get('control_verified') is True,
        origin=origin,
        payload_bytes=payload,
        context_epoch=context_epoch,
        input_epoch=context_epoch,
        same_pane=True,
        valid_identity=True,
    )


class Utf8ChunkAssembler:
    def __init__(self) -> None:
        self._hold = bytearray()
        self._text = ''

    @property
    def text(self) -> str:
        return self._text

    @property
    def held_incomplete(self) -> bool:
        return len(self._hold) > 0

    def append(self, chunk: bytes) -> None:
        if not chunk:
            return
        self._hold.extend(chunk)
        hold = bytes(self._hold)
        tail = _incomplete_utf8_tail(hold)
        complete = hold if tail == 0 else hold[:-tail]
        if complete:
            self._text += complete.decode('utf-8', errors='strict')
        self._hold = bytearray(hold[-tail:] if tail else b'')


class RendererEpochGate:
    def __init__(self, epoch: int):
        if type(epoch) is not int or epoch <= 0:
            raise ProtocolError('stale_epoch')
        self.epoch = epoch
        self.validator = TerminalCaptureValidator()
        self.failed = False

    def accept(self, epoch: int, line: bytes) -> dict[str, Any]:
        if self.failed:
            raise ProtocolError('terminal_stream_not_active')
        if epoch != self.epoch:
            self.failed = True
            raise ProtocolError('stale_epoch')
        try:
            return self.validator.accept(line)
        except Exception:
            self.failed = True
            raise

    def reconnect(self, epoch: int) -> None:
        if type(epoch) is not int or epoch <= 0 or epoch <= self.epoch:
            raise ProtocolError('stale_epoch')
        self.epoch = epoch
        self.validator = TerminalCaptureValidator()
        self.failed = False


class RendererByteWindow:
    def __init__(
        self,
        epoch: int,
        max_in_flight_bytes: int = DEFAULT_MAX_IN_FLIGHT,
        max_queued_frames: int = DEFAULT_MAX_FRAMES,
    ):
        if type(epoch) is not int or epoch <= 0:
            raise ProtocolError('stale_epoch')
        if type(max_in_flight_bytes) is not int or max_in_flight_bytes < 1:
            raise ValueError('invalid_client_limit')
        if type(max_queued_frames) is not int or max_queued_frames < 1:
            raise ValueError('invalid_client_limit')
        self.epoch = epoch
        self.max_bytes = max_in_flight_bytes
        self.max_frames = max_queued_frames
        self.in_flight_bytes = 0
        self._queued: list[int] = []
        self.faulted = False

    @property
    def queued_frames(self) -> int:
        return len(self._queued)

    @property
    def state(self) -> str:
        if self.faulted:
            return 'faulted'
        if self._at_capacity():
            return 'backpressured'
        return 'ready'

    def try_enqueue(self, epoch: int, byte_count: int) -> dict[str, Any]:
        if self.faulted:
            return _queue(False, 'faulted', 'terminal_stream_not_active', True)
        if epoch != self.epoch:
            self.faulted = True
            return _queue(False, 'faulted', 'stale_epoch', True)
        if type(byte_count) is not int or byte_count <= 0 or byte_count > MAX_FRAME_BYTES:
            return _queue(False, self.state, 'decoded_bytes_limit', False)
        if (
            self.in_flight_bytes + byte_count > self.max_bytes
            or len(self._queued) + 1 > self.max_frames
        ):
            return _queue(False, 'backpressured', 'queue_bytes_limit', False)
        self._queued.append(byte_count)
        self.in_flight_bytes += byte_count
        return _queue(True, self.state, 'queued', False)

    def acknowledge_parse_consumed(self, epoch: int, byte_count: int) -> dict[str, Any]:
        if self.faulted:
            return _queue(False, 'faulted', 'terminal_stream_not_active', True)
        if epoch != self.epoch:
            return _queue(False, self.state, 'stale_epoch', False)
        if not self._queued or byte_count != self._queued[0]:
            return _queue(False, self.state, 'queue_ack_mismatch', False)
        self._queued.pop(0)
        self.in_flight_bytes -= byte_count
        return _queue(True, self.state, 'parse_consumed', False)

    def classify_delta_drop(self) -> dict[str, Any]:
        self.faulted = True
        return _queue(False, 'faulted', 'delta_drop_forbidden', True)

    def reset(self, epoch: int) -> None:
        if type(epoch) is not int or epoch <= 0 or epoch < self.epoch:
            raise ProtocolError('stale_epoch')
        self.epoch = epoch
        self.in_flight_bytes = 0
        self._queued = []
        self.faulted = False

    def _at_capacity(self) -> bool:
        return self.in_flight_bytes >= self.max_bytes or len(self._queued) >= self.max_frames


def run_case(case: dict[str, Any]) -> dict[str, Any]:
    kind = case.get('kind')
    if kind == 'utf8_chunks':
        chunks = [_hex(item) for item in case['chunks_hex']]
        text = assemble_utf8(chunks)
        return {
            'text': text,
            'replacement': '\ufffd' in text,
            'duplicated': _duplicated(text, case.get('expect', {}).get('text')),
        }
    if kind == 'utf8_boundary':
        assembler = Utf8ChunkAssembler()
        for item in case['chunks_hex']:
            assembler.append(_hex(item))
        return {
            'text': assembler.text,
            'replacement': '\ufffd' in assembler.text,
            'held_incomplete': assembler.held_incomplete,
        }
    if kind == 'raw_bytes':
        joined = join_bytes([_hex(item) for item in case['chunks_hex']])
        return {
            'bytes_hex': joined.hex(),
            'text_normalized': b'^C' in joined or joined.decode('latin-1') == '^C',
        }
    if kind == 'epoch_gate':
        return _run_epoch_steps(case['steps'])
    if kind == 'composition':
        return evaluate_composition(
            case['ime_event'],
            access=case.get('access', 'controlling'),
            control_verified=case.get('control_verified', True),
            origin=case.get('origin'),
            payload_bytes=case.get('payload_bytes', 3),
            context_epoch=case.get('context_epoch', 1),
            input_epoch=case.get('input_epoch', 1),
        )
    if kind == 'input_policy':
        return evaluate_input_policy(
            access=case.get('access', 'observing'),
            control_verified=case.get('control_verified', False),
            origin=case['origin'],
            payload_bytes=case.get('payload_bytes', 3),
            context_epoch=case.get('context_epoch', 1),
            input_epoch=case.get('input_epoch', 1),
        )
    if kind == 'queue':
        return _run_queue(case)
    if kind == 'web_message':
        return evaluate_web_message(case['message'], case['context'])
    raise RendererError('missing_matrix_row')


def _run_epoch_steps(steps: list[dict[str, Any]]) -> dict[str, Any]:
    gate = None
    last: dict[str, Any] = {}
    for step in steps:
        action = step['action']
        if action == 'start':
            gate = RendererEpochGate(step['epoch'])
            last = {'code': None, 'accepted': True}
            continue
        if gate is None:
            raise RendererError('missing_matrix_row')
        if action == 'reconnect':
            try:
                gate.reconnect(step['epoch'])
                last = {'code': None, 'accepted': True}
            except ProtocolError as exc:
                last = {'code': str(exc), 'accepted': False}
            continue
        if action == 'accept':
            line = _frame_line(step.get('seq', 1), step.get('full', True), step.get('data', 'hello'))
            try:
                gate.accept(step['epoch'], line)
                last = {'code': None, 'accepted': True, 'seq': step.get('seq', 1)}
            except ProtocolError as exc:
                last = {'code': str(exc), 'accepted': False}
            continue
        raise RendererError('missing_matrix_row')
    return last


def _run_queue(case: dict[str, Any]) -> dict[str, Any]:
    window = RendererByteWindow(
        case.get('epoch', 1),
        case.get('max_bytes', DEFAULT_MAX_IN_FLIGHT),
        case.get('max_frames', DEFAULT_MAX_FRAMES),
    )
    last: dict[str, Any] = {}
    for step in case['steps']:
        action = step['action']
        if action == 'enqueue':
            last = window.try_enqueue(step.get('epoch', case.get('epoch', 1)), step['bytes'])
            last['in_flight_bytes'] = window.in_flight_bytes
            continue
        if action == 'ack':
            last = window.acknowledge_parse_consumed(
                step.get('epoch', case.get('epoch', 1)), step['bytes'])
            last['in_flight_bytes'] = window.in_flight_bytes
            continue
        if action == 'classify_drop':
            last = window.classify_delta_drop()
            last['in_flight_bytes'] = window.in_flight_bytes
            continue
        if action == 'reset':
            window.reset(step['epoch'])
            last = {
                'accepted': True,
                'state': window.state,
                'code': 'reset',
                'parse_consumed_is_presented': False,
                'requires_full_reset': False,
                'in_flight_bytes': window.in_flight_bytes,
            }
            continue
        raise RendererError('missing_matrix_row')
    return last


def _frame_line(seq: int, full: bool, data: str) -> bytes:
    payload = base64.b64encode(data.encode('utf-8')).decode('ascii')
    obj = {
        'type': 'terminal.frame',
        'seq': seq,
        'encoding': 'ansi',
        'width': 120,
        'height': 40,
        'full': full,
        'bytes': payload,
    }
    return (json.dumps(obj, separators=(',', ':')) + '\n').encode('utf-8')


def _incomplete_utf8_tail(buf: bytes) -> int:
    if not buf:
        return 0
    for i in range(1, min(4, len(buf)) + 1):
        lead = buf[-i]
        if (lead & 0xC0) == 0x80:
            continue
        if lead < 0x80:
            need = 1
        elif lead & 0xE0 == 0xC0:
            need = 2
        elif lead & 0xF0 == 0xE0:
            need = 3
        elif lead & 0xF8 == 0xF0:
            need = 4
        else:
            return 0
        if i < need:
            return i
        return 0
    return 0


def _hex(value: str) -> bytes:
    if not isinstance(value, str):
        raise RendererError('missing_matrix_row')
    return bytes.fromhex(value)


def _duplicated(actual: str, expected: Any) -> bool:
    if not isinstance(expected, str) or not expected:
        return False
    return actual == expected + expected


def _decision(allowed: bool, code: str) -> dict[str, Any]:
    return {'allowed': allowed, 'code': code}


def _queue(
    accepted: bool, state: str, code: str, requires_full_reset: bool,
) -> dict[str, Any]:
    return {
        'accepted': accepted,
        'state': state,
        'code': code,
        'parse_consumed_is_presented': False,
        'requires_full_reset': requires_full_reset,
    }


def _reject_promotion(fixture: dict[str, Any], capture: dict[str, Any]) -> None:
    for doc in (fixture, capture):
        if doc.get('runtime_pass') is True:
            raise RendererError('evidence_level_promotion')
        if doc.get('windows_verified') is True:
            raise RendererError('evidence_level_promotion')
        if doc.get('ac08_passed') is True or doc.get('ac09_passed') is True:
            raise RendererError('ac08_ac09_claimed_passed')
        if _is_success(doc.get('result')) or _is_success(doc.get('live_result')):
            raise RendererError('evidence_level_promotion')
        if _is_success(doc.get('all_live_checks')) or _is_success(doc.get('live_matrix')):
            raise RendererError('evidence_level_promotion')
        for key in (
            'winui_executed', 'webview2_executed', 'ime_executed',
            'native_candidate_run', 'herdr_executed',
        ):
            if doc.get(key) is True:
                raise RendererError('evidence_level_promotion')
    if fixture.get('simulation') is not True:
        raise RendererError('evidence_level_promotion')
    if fixture.get('kind') != 'synthetic_renderer_l1_matrix':
        raise RendererError('missing_record_field')
    if fixture.get('fixture_origin') != 'synthetic':
        raise RendererError('evidence_level_promotion')


def _check_fixture_shape(fixture: dict[str, Any]) -> None:
    if fixture.get('runtime_pass') is not False:
        raise RendererError('evidence_level_promotion')
    if fixture.get('windows_verified') is not False:
        raise RendererError('evidence_level_promotion')
    if fixture.get('ac08_passed') is not False:
        raise RendererError('ac08_ac09_claimed_passed')
    if fixture.get('ac09_passed') is not False:
        raise RendererError('ac08_ac09_claimed_passed')
    if fixture.get('herdr_executed') is not False:
        raise RendererError('evidence_level_promotion')
    if fixture.get('winui_executed') is not False:
        raise RendererError('evidence_level_promotion')
    if fixture.get('all_live_checks') != 'blocked':
        raise RendererError('missing_record_field')
    if fixture.get('blocked_category') != BLOCKED_CATEGORY:
        raise RendererError('missing_blocked_category')
    cases = fixture.get('cases')
    if not isinstance(cases, list) or len(cases) != len(MATRIX_SCENARIOS):
        raise RendererError('missing_matrix_row')
    seen: list[str] = []
    for case in cases:
        if not isinstance(case, dict):
            raise RendererError('missing_matrix_row')
        scenario = case.get('scenario')
        if scenario not in MATRIX_SCENARIOS or scenario in seen:
            raise RendererError('missing_matrix_row')
        seen.append(scenario)
        if case.get('live_result') != 'blocked':
            raise RendererError('evidence_level_promotion')
        if case.get('evidence_level') != 'synthetic':
            raise RendererError('evidence_level_promotion')
        if case.get('simulation') is not True:
            raise RendererError('evidence_level_promotion')
        if not isinstance(case.get('expect'), dict):
            raise RendererError('missing_record_field')
    if seen != list(MATRIX_SCENARIOS):
        raise RendererError('missing_matrix_row')


def _check_capture(capture: dict[str, Any]) -> None:
    if capture.get('template') is True:
        raise RendererError('evidence_level_promotion')
    if capture.get('kind') != 'windows_renderer_ime':
        raise RendererError('missing_record_field')
    if capture.get('result') != 'blocked':
        raise RendererError('evidence_level_promotion')
    if capture.get('blocked_category') != BLOCKED_CATEGORY:
        raise RendererError('missing_blocked_category')
    if capture.get('herdr_executed') is not False:
        raise RendererError('evidence_level_promotion')
    if capture.get('winui_executed') is not False:
        raise RendererError('evidence_level_promotion')
    if capture.get('webview2_executed') is not False:
        raise RendererError('evidence_level_promotion')
    if capture.get('ime_executed') is not False:
        raise RendererError('evidence_level_promotion')
    if capture.get('native_candidate_run') is not False:
        raise RendererError('evidence_level_promotion')
    if capture.get('live_matrix') != 'blocked':
        raise RendererError('evidence_level_promotion')
    if capture.get('ac08_passed') is not False:
        raise RendererError('ac08_ac09_claimed_passed')
    if capture.get('ac09_passed') is not False:
        raise RendererError('ac08_ac09_claimed_passed')
    if capture.get('windows_verified') is not False:
        raise RendererError('evidence_level_promotion')
    if capture.get('exit_code') == 0 and capture.get('stdout_sha256'):
        raise RendererError('evidence_level_promotion')
    covers = capture.get('covers')
    if not isinstance(covers, list) or set(covers) != set(MATRIX_SCENARIOS):
        raise RendererError('missing_matrix_row')
    untested = capture.get('untested_l3')
    if not isinstance(untested, list) or set(untested) != set(L3_UNTESTED):
        raise RendererError('missing_matrix_row')
    if capture.get('simulation_fixture') != FIXTURE_REL:
        raise RendererError('missing_record_field')
    if capture.get('decision') != DECISION_REL:
        raise RendererError('missing_record_field')


def _check_independent(capture: dict[str, Any], other: dict[str, Any]) -> None:
    if capture.get('capture_id') == other.get('capture_id'):
        raise RendererError('runtime_records_not_independent')
    if capture.get('kind') == other.get('kind'):
        raise RendererError('runtime_records_not_independent')
    if capture.get('blocked_category') == other.get('blocked_category'):
        raise RendererError('runtime_records_not_independent')
    if capture.get('kind') in {
        'windows_endpoint_matrix', 'windows_terminal_lease', 'windows_runtime',
    }:
        raise RendererError('runtime_records_not_independent')
    if 'named_pipe_connected' in capture:
        raise RendererError('runtime_records_not_independent')


def _check_baseline(baseline: dict[str, Any], capture: dict[str, Any]) -> None:
    verification = baseline.get('runtime_verification')
    if not isinstance(verification, dict):
        raise RendererError('missing_record_field')
    if verification.get('ime') != 'blocked':
        if _is_success(verification.get('ime')):
            raise RendererError('evidence_level_promotion')
        raise RendererError('missing_record_field')
    records = baseline.get('records')
    if not isinstance(records, list):
        raise RendererError('missing_record_field')
    matches = [item for item in records if item.get('environment') == 'ime']
    if len(matches) != 1:
        raise RendererError('missing_record_field')
    record = matches[0]
    if record.get('result') != 'blocked':
        raise RendererError('evidence_level_promotion')
    if record.get('blocked_category') != BLOCKED_CATEGORY:
        raise RendererError('missing_blocked_category')
    if CAPTURE_REL not in record.get('attachments', []):
        raise RendererError('missing_record_field')
    for env in ('windows_terminal_lease', 'windows_endpoint', 'windows_local'):
        others = [item for item in records if item.get('environment') == env]
        if not others:
            continue
        other = others[0]
        if record.get('subject') == other.get('subject'):
            raise RendererError('runtime_records_not_independent')
        if set(record.get('attachments') or ()) & set(other.get('attachments') or ()):
            raise RendererError('runtime_records_not_independent')


def _check_case_expect(case: dict[str, Any], actual: dict[str, Any]) -> None:
    expect = case['expect']
    for key, value in expect.items():
        if actual.get(key) != value:
            raise RendererError('matrix_expect_mismatch')
    code = actual.get('code')
    if isinstance(code, str):
        _assert_redacted(code)
    if actual.get('parse_consumed_is_presented') is True:
        raise RendererError('parse_consumed_marked_presented')
    if case.get('kind') == 'utf8_chunks' and actual.get('replacement') is True:
        raise RendererError('utf8_replacement_present')
    if case.get('kind') == 'composition' and case.get('ime_event') == 'preedit_update':
        if actual.get('allowed') is True:
            raise RendererError('preedit_sent')


def _assert_redacted(value: str) -> None:
    if '\\' in value or '/' in value or '%APPDATA%' in value.upper():
        raise RendererError('path_not_redacted')


def _is_success(result: Any) -> bool:
    return result in SUCCESS_RESULTS


def _load_json(path: Path) -> dict[str, Any]:
    loaded = json.loads(path.read_text(encoding='utf-8'))
    if not isinstance(loaded, dict):
        raise RendererError('missing_record_field')
    return loaded
