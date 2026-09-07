"""Bounded, strict validation for herdr v0.8.2 terminal-session captures.

The limits and fail-closed resynchronization policy belong to HerdDesk. This is
G0 diagnostic code, not an upstream SDK or proof of Windows compatibility.
Errors deliberately omit terminal text, remote paths and JSON payloads.
"""
from __future__ import annotations

import base64
import binascii
from dataclasses import dataclass
import hashlib
import json
from typing import Any, Iterable, Iterator


class ProtocolError(ValueError):
    """A stable error category suitable for redacted diagnostics."""


@dataclass(frozen=True)
class Limits:
    max_line_bytes: int = 16 * 1024 * 1024
    max_frame_bytes: int = 8 * 1024 * 1024
    max_input_bytes: int = 64 * 1024

    def __post_init__(self) -> None:
        for value in (self.max_line_bytes, self.max_frame_bytes, self.max_input_bytes):
            if type(value) is not int or not 0 < value <= 256 * 1024 * 1024:
                raise ValueError('invalid_client_limit')


DEFAULT_LIMITS = Limits()


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ProtocolError('duplicate_json_key')
        result[key] = value
    return result


def _nonfinite(_: str) -> None:
    raise ProtocolError('nonfinite_json_number')


def strict_json_loads(raw: bytes | bytearray | str) -> Any:
    """Reject duplicate keys, non-JSON numbers, invalid UTF-8 and deep nesting."""
    try:
        text = raw if isinstance(raw, str) else bytes(raw).decode('utf-8', errors='strict')
        depth = 0
        quoted = False
        escaped = False
        for char in text:
            if quoted:
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == '"':
                    quoted = False
            elif char == '"':
                quoted = True
            elif char in '[{':
                depth += 1
                if depth > 64:
                    raise ProtocolError('json_depth_limit')
            elif char in ']}':
                depth -= 1
        return json.loads(text, object_pairs_hook=_unique_object, parse_constant=_nonfinite)
    except ProtocolError:
        raise
    except (ValueError, UnicodeError, RecursionError) as exc:
        raise ProtocolError('invalid_json') from exc


def _uint(value: Any, bits: int, *, positive: bool = False) -> bool:
    # bool is a Python int subclass, but is not a wire integer.
    return type(value) is int and (1 if positive else 0) <= value < 2**bits


def _base64(value: Any, max_bytes: int) -> bytes:
    if not isinstance(value, str):
        raise ProtocolError('base64_string_required')
    if len(value) > 4 * ((max_bytes + 2) // 3):
        raise ProtocolError('decoded_bytes_limit')
    try:
        result = base64.b64decode(value, validate=True)
    except (ValueError, binascii.Error) as exc:
        raise ProtocolError('invalid_base64') from exc
    if len(result) > max_bytes:
        raise ProtocolError('decoded_bytes_limit')
    # Reject noncanonical padding as well as whitespace. Upstream emits STANDARD.
    if base64.b64encode(result).decode('ascii') != value:
        raise ProtocolError('noncanonical_base64')
    return result


def validate_frame(obj: Any, previous: int | None = None,
                   limits: Limits = DEFAULT_LIMITS) -> tuple[int | None, dict[str, Any]]:
    if not isinstance(obj, dict):
        raise ProtocolError('object_required')
    kind = obj.get('type')
    if kind == 'terminal.closed':
        reason = obj.get('reason')
        if reason is not None and not isinstance(reason, str):
            raise ProtocolError('invalid_closed_reason')
        return previous, {'type': kind, 'reason_present': bool(reason)}
    if kind != 'terminal.frame':
        raise ProtocolError('unknown_terminal_type')
    seq = obj.get('seq')
    if not _uint(seq, 64, positive=True):
        raise ProtocolError('invalid_sequence')
    if previous is not None and seq != previous + 1:
        raise ProtocolError('sequence_gap_or_replay')
    if obj.get('encoding') != 'ansi':
        raise ProtocolError('unsupported_encoding')
    if not all(_uint(obj.get(k), 16, positive=True) for k in ('width', 'height')):
        raise ProtocolError('invalid_frame_dimensions')
    if type(obj.get('full')) is not bool:
        raise ProtocolError('boolean_full_required')
    if previous is None and not obj['full']:
        raise ProtocolError('initial_full_frame_required')
    raw = _base64(obj.get('bytes'), limits.max_frame_bytes)
    # Raw bytes may split a UTF-8 code point between frames. Never decode here.
    return seq, {'type': kind, 'seq': seq, 'width': obj['width'], 'height': obj['height'],
                 'full': obj['full'], 'decoded_bytes': len(raw),
                 'sha256': hashlib.sha256(raw).hexdigest()}


def validate_input(obj: Any, limits: Limits = DEFAULT_LIMITS) -> None:
    if not isinstance(obj, dict):
        raise ProtocolError('object_required')
    kind = obj.get('type')
    if kind == 'terminal.input':
        has_text, has_bytes = 'text' in obj, 'bytes' in obj
        if has_text == has_bytes:
            raise ProtocolError('exactly_one_input_payload_required')
        if has_text:
            if not isinstance(obj['text'], str):
                raise ProtocolError('input_string_required')
            try:
                payload = obj['text'].encode('utf-8', errors='strict')
            except UnicodeError as exc:
                raise ProtocolError('invalid_input_unicode') from exc
        else:
            payload = _base64(obj['bytes'], limits.max_input_bytes)
        if not payload:
            raise ProtocolError('empty_input_rejected')
        if len(payload) > limits.max_input_bytes:
            raise ProtocolError('input_bytes_limit')
    elif kind == 'terminal.resize':
        if not all(_uint(obj.get(k), 16, positive=True) for k in ('cols', 'rows')):
            raise ProtocolError('invalid_resize_dimensions')
        if not all(_uint(obj.get(k, 0), 32) for k in ('cell_width_px', 'cell_height_px')):
            raise ProtocolError('invalid_cell_dimensions')
    elif kind == 'terminal.scroll':
        if obj.get('direction') not in ('up', 'down'):
            raise ProtocolError('invalid_scroll_direction')
        if not _uint(obj.get('lines'), 16, positive=True):
            raise ProtocolError('invalid_scroll_lines')
        if obj.get('source', 'wheel') not in ('wheel', 'page_key'):
            raise ProtocolError('invalid_scroll_source')
        for key in ('column', 'row'):
            if obj.get(key) is not None and not _uint(obj[key], 16):
                raise ProtocolError('invalid_scroll_coordinate')
        if not _uint(obj.get('modifiers', 0), 8):
            raise ProtocolError('invalid_scroll_modifiers')
    elif kind != 'terminal.release':
        raise ProtocolError('unknown_input_type')


class NdjsonDecoder:
    """Incremental byte framer with bounded storage and latched failures.

    feed() is lazy: callers must consume it before supplying the next chunk.
    LF is the record delimiter; CRLF works through JSON whitespace handling.
    EOF inside a record fails, even when its bytes happen to form valid JSON.
    """

    def __init__(self, max_line_bytes: int = DEFAULT_LIMITS.max_line_bytes):
        if type(max_line_bytes) is not int or max_line_bytes < 1:
            raise ValueError('invalid_line_limit')
        self.limit = max_line_bytes
        self.buffer = bytearray()
        self.finished = False
        self.failed = False

    def feed(self, chunk: bytes) -> Iterator[bytes]:
        if self.finished or self.failed:
            raise ProtocolError('decoder_not_active')
        start = 0
        while start < len(chunk):
            newline = chunk.find(b'\n', start)
            end = len(chunk) if newline < 0 else newline
            if len(self.buffer) + end - start > self.limit:
                self.failed = True
                self.buffer.clear()
                raise ProtocolError('line_bytes_limit')
            self.buffer.extend(memoryview(chunk)[start:end])
            if newline < 0:
                return
            line = bytes(self.buffer)
            self.buffer.clear()
            start = newline + 1
            if line.strip(b' \t\r'):
                yield line

    def finish(self) -> None:
        if self.finished or self.failed:
            raise ProtocolError('decoder_not_active')
        self.finished = True
        if self.buffer:
            self.buffer.clear()
            self.failed = True
            raise ProtocolError('truncated_ndjson_record')


class TerminalCaptureValidator:
    """Single epoch, ordered frames. Construct a new instance after reconnect."""

    def __init__(self, limits: Limits = DEFAULT_LIMITS):
        self.limits = limits
        self.previous: int | None = None
        self.closed = False
        self.failed = False
        self.frames = 0
        self.decoded_bytes = 0

    def accept(self, line: bytes) -> dict[str, Any]:
        if self.failed or self.closed:
            raise ProtocolError('terminal_stream_not_active')
        try:
            if len(line) > self.limits.max_line_bytes:
                raise ProtocolError('line_bytes_limit')
            obj = strict_json_loads(line)
            seq, summary = validate_frame(obj, self.previous, self.limits)
            if summary['type'] == 'terminal.closed':
                self.closed = True
            else:
                self.previous = seq
                self.frames += 1
                self.decoded_bytes += summary['decoded_bytes']
            return summary
        except (ValueError, TypeError):
            self.failed = True
            raise


def analyze_capture(chunks: Iterable[bytes], limits: Limits = DEFAULT_LIMITS,
                    max_total_bytes: int = 256 * 1024 * 1024) -> dict[str, Any]:
    if type(max_total_bytes) is not int or max_total_bytes < 1:
        raise ValueError('invalid_total_limit')
    decoder = NdjsonDecoder(limits.max_line_bytes)
    validator = TerminalCaptureValidator(limits)
    digest = hashlib.sha256()
    size = 0
    for chunk in chunks:
        size += len(chunk)
        if size > max_total_bytes:
            raise ProtocolError('capture_bytes_limit')
        digest.update(chunk)
        for line in decoder.feed(chunk):
            validator.accept(line)
    decoder.finish()
    if not validator.frames:
        raise ProtocolError('no_terminal_frames')
    return {'kind': 'offline_terminal_capture_validation', 'validated': True,
            'frame_count': validator.frames, 'decoded_bytes': validator.decoded_bytes,
            'capture_bytes': size, 'capture_sha256': digest.hexdigest(),
            'last_sequence': validator.previous, 'saw_terminal_closed': validator.closed,
            'daemon_or_pane_exit_verified': False, 'windows_verified': False,
            'ime_verified': False, 'input_execution_verified': False}
