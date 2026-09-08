"""Compatibility-evidence rules for HD-001.

Structural validation is not product acceptance and never sets
windows_verified. Git blob SHA, distribution binary SHA-256, and runtime
schema SHA-256 stay in distinct fields.
"""
from __future__ import annotations

import json
from pathlib import Path
import re
from typing import Any


class EvidenceError(ValueError):
    """Stable evidence-rule code; the message is the code only."""


BASELINE_REL = 'evidence/compatibility-baseline.json'
MATRIX_REL = 'evidence/version-support-matrix.json'
RUNTIME_REL = 'evidence/runtime'

RECORD_FIELDS = (
    'subject', 'environment', 'observed_at', 'evidence_level',
    'version', 'hashes', 'result', 'redaction', 'limitations', 'attachments',
)
HASH_KEYS = (
    'git_blob_sha',
    'distribution_binary_sha256',
    'runtime_binary_sha256',
    'runtime_schema_sha256',
)
SHA256_HASH_KEYS = HASH_KEYS[1:]
VERSION_KEYS = ('tag', 'commit', 'cli', 'daemon')
DESIGN_CAPTURE_FIELDS = (
    'capture_id', 'kind', 'captured_at_utc', 'operator_scope',
    'host_fingerprint_redacted', 'command_redacted', 'exit_code',
    'stdout_sha256', 'stderr_sha256', 'evidence_level', 'limitations',
)
MATRIX_KEY_FIELDS = (
    'os', 'arch', 'cli_binary_hash', 'daemon_version', 'protocol', 'schema_hash',
)
MATRIX_COLUMNS = ('source', 'windows_runtime', 'remote_runtime')

NON_RUNTIME_LEVELS = frozenset({
    'source_inspection_only', 'synthetic', 'hosted_ci', 'ui_manual',
    'blocked', 'not_run',
})
RUNTIME_LEVELS = frozenset({
    'isolated_windows_runtime', 'remote_runtime',
})
SUCCESS_RESULTS = frozenset({
    'passed', 'verified', 'compatible', 'success', 'ok',
})
UNKNOWN_KEY_VALUES = frozenset({None, '', 'unknown', 'any', '*'})
WINDOWS_PATH_KEYS = frozenset({
    'herdr_binary_on_path', 'path_inspection_only', 'herdr_path_redacted',
})
REQUIRED_TEMPLATES = (
    'evidence/runtime/windows-runtime.template.json',
    'evidence/runtime/remote-runtime.template.json',
)
SOURCE_FIELD_SOURCE_KEYS = (
    'tag', 'commit', 'api_protocol', 'schema_version',
    'distribution_binary_sha256', 'runtime_binary_sha256',
    'runtime_schema_sha256', 'daemon_version',
)
VERIFICATION_KEYS = (
    'windows_local', 'remote_linux', 'named_pipe_acl', 'windows_endpoint',
    'windows_terminal_lease', 'ime',
)

GIT_BLOB_RE = re.compile(r'^[0-9a-f]{40}$')
SHA256_RE = re.compile(r'^[0-9a-f]{64}$')


def validate_evidence(root: Path) -> dict[str, Any]:
    root = Path(root)
    baseline_path = root / BASELINE_REL
    matrix_path = root / MATRIX_REL
    runtime_dir = root / RUNTIME_REL
    if not baseline_path.is_file() or not matrix_path.is_file():
        raise EvidenceError('missing_record_field')
    if not runtime_dir.is_dir():
        raise EvidenceError('missing_runtime_capture')
    baseline = json.loads(baseline_path.read_text(encoding='utf-8'))
    matrix = json.loads(matrix_path.read_text(encoding='utf-8'))
    documents: dict[str, dict[str, Any]] = {}
    for path in sorted(runtime_dir.glob('*.json')):
        rel = path.relative_to(root).as_posix()
        loaded = json.loads(path.read_text(encoding='utf-8'))
        if not isinstance(loaded, dict):
            raise EvidenceError('capture_schema_mismatch')
        documents[rel] = loaded
    if baseline.get('matrix_path') != MATRIX_REL:
        raise EvidenceError('missing_record_field')
    return check_evidence(baseline, matrix, documents)


def check_evidence(
    baseline: dict[str, Any],
    matrix: dict[str, Any],
    documents: dict[str, dict[str, Any]],
) -> dict[str, Any]:
    _check_protocol_and_write(baseline)
    schema, templates, captures = _split_documents(documents)
    _check_capture_schema(schema)
    _check_capture_documents(schema, templates, captures)
    records = baseline.get('records')
    if not isinstance(records, list):
        raise EvidenceError('missing_record_field')
    blobs = _collect_git_blobs(baseline, records)
    _check_hash_conflation(baseline, records, documents, blobs)
    for record in records:
        _check_record_shape(record)
        _check_record_attachments(record, documents)
    source = _record_for(records, 'source_inspection')
    windows = _record_for(records, 'windows_local')
    remote = _record_for(records, 'remote_linux')
    _check_source_field_sources(source)
    _check_independence(windows, remote, documents)
    _check_runtime_summaries(baseline, windows, remote)
    _check_runtime_claims(windows, templates, captures, 'windows_local')
    _check_runtime_claims(remote, templates, captures, 'remote_linux')
    _check_herdr_runtime_hashes(baseline, windows, remote, captures)
    _check_templates(templates)
    _check_matrix(matrix, windows, remote)
    return {
        'evidence_validation': 'passed',
        'windows_verified': False,
        'runtime_windows': windows['result'],
        'runtime_remote': remote['result'],
        'source_evidence_level': source['evidence_level'],
    }


def _check_protocol_and_write(baseline: dict[str, Any]) -> None:
    herdr = baseline.get('herdr')
    if not isinstance(herdr, dict):
        raise EvidenceError('missing_record_field')
    protocol = herdr.get('api_protocol')
    if type(protocol) is not int or protocol != 20:
        raise EvidenceError('api_protocol_mismatch')
    if baseline.get('default_write_capability') is not False:
        raise EvidenceError('default_write_capability_not_false')
    schema_version = herdr.get('schema_version')
    if type(schema_version) is not int or schema_version != 1:
        raise EvidenceError('api_protocol_mismatch')
    for key in ('runtime_binary_sha256', 'runtime_schema_sha256', 'distribution_binary_sha256'):
        if key not in herdr:
            raise EvidenceError('missing_record_field')


def _split_documents(
    documents: dict[str, dict[str, Any]],
) -> tuple[dict[str, Any], dict[str, dict[str, Any]], dict[str, dict[str, Any]]]:
    schema = None
    templates: dict[str, dict[str, Any]] = {}
    captures: dict[str, dict[str, Any]] = {}
    for rel, doc in documents.items():
        kind = doc.get('document_kind')
        if kind == 'capture_schema':
            schema = doc
            continue
        if doc.get('template') is True or kind == 'template':
            templates[rel] = doc
        else:
            captures[rel] = doc
    if schema is None:
        raise EvidenceError('capture_schema_mismatch')
    return schema, templates, captures


def _check_capture_documents(
    schema: dict[str, Any],
    templates: dict[str, dict[str, Any]],
    captures: dict[str, dict[str, Any]],
) -> None:
    required = schema['required_fields']
    for doc in list(templates.values()) + list(captures.values()):
        for key in required:
            if key not in doc:
                raise EvidenceError('capture_schema_mismatch')
        if not isinstance(doc.get('capture_id'), str) or not doc['capture_id']:
            raise EvidenceError('capture_schema_mismatch')
        if not isinstance(doc.get('kind'), str) or not doc['kind']:
            raise EvidenceError('capture_schema_mismatch')
        if not isinstance(doc.get('limitations'), list):
            raise EvidenceError('capture_schema_mismatch')


def _check_capture_schema(schema: dict[str, Any]) -> None:
    fields = schema.get('required_fields')
    if not isinstance(fields, list):
        raise EvidenceError('capture_schema_mismatch')
    if schema.get('encoding') != 'utf-8':
        raise EvidenceError('capture_schema_mismatch')
    missing = [name for name in DESIGN_CAPTURE_FIELDS if name not in fields]
    if missing:
        raise EvidenceError('capture_schema_mismatch')
    distinct = schema.get('hash_fields_must_remain_distinct')
    if not isinstance(distinct, list) or list(HASH_KEYS) != distinct:
        raise EvidenceError('capture_schema_mismatch')


def _collect_git_blobs(baseline: dict[str, Any], records: list[dict[str, Any]]) -> set[str]:
    blobs: set[str] = set()
    source_blobs = baseline['herdr'].get('source_blobs')
    if not isinstance(source_blobs, dict) or not source_blobs:
        raise EvidenceError('missing_record_field')
    for value in source_blobs.values():
        if not isinstance(value, str) or not GIT_BLOB_RE.fullmatch(value):
            raise EvidenceError('invalid_hash_format')
        blobs.add(value)
    for record in records:
        hashes = record.get('hashes')
        if not isinstance(hashes, dict):
            continue
        blobs.update(_blob_values(hashes.get('git_blob_sha')))
    return blobs


def _blob_values(value: Any) -> set[str]:
    if value is None:
        return set()
    if isinstance(value, str):
        if not GIT_BLOB_RE.fullmatch(value):
            raise EvidenceError('invalid_hash_format')
        return {value}
    if isinstance(value, dict):
        result: set[str] = set()
        for item in value.values():
            if not isinstance(item, str) or not GIT_BLOB_RE.fullmatch(item):
                raise EvidenceError('invalid_hash_format')
            result.add(item)
        return result
    raise EvidenceError('invalid_hash_format')


def _check_hash_conflation(
    baseline: dict[str, Any],
    records: list[dict[str, Any]],
    documents: dict[str, dict[str, Any]],
    blobs: set[str],
) -> None:
    herdr = baseline['herdr']
    for key in SHA256_HASH_KEYS:
        _reject_sha256(herdr.get(key), blobs)
    for record in records:
        hashes = record.get('hashes')
        if not isinstance(hashes, dict):
            raise EvidenceError('missing_record_field')
        for key in HASH_KEYS:
            if key not in hashes:
                raise EvidenceError('missing_record_field')
        for key in SHA256_HASH_KEYS:
            _reject_sha256(hashes.get(key), blobs)
    for doc in documents.values():
        if doc.get('document_kind') == 'capture_schema':
            continue
        _reject_sha256(doc.get('stdout_sha256'), blobs)
        _reject_sha256(doc.get('stderr_sha256'), blobs)


def _reject_sha256(value: Any, blobs: set[str]) -> None:
    if value is None:
        return
    if not isinstance(value, str):
        raise EvidenceError('invalid_sha256')
    if value in blobs or GIT_BLOB_RE.fullmatch(value):
        raise EvidenceError('hash_conflation')
    if not SHA256_RE.fullmatch(value):
        raise EvidenceError('invalid_sha256')


def _check_record_shape(record: Any) -> None:
    if not isinstance(record, dict):
        raise EvidenceError('missing_record_field')
    for key in RECORD_FIELDS:
        if key not in record:
            raise EvidenceError('missing_record_field')
    version = record['version']
    hashes = record['hashes']
    if not isinstance(version, dict) or not isinstance(hashes, dict):
        raise EvidenceError('missing_record_field')
    for key in VERSION_KEYS:
        if key not in version:
            raise EvidenceError('missing_record_field')
    if not isinstance(record['limitations'], list) or not isinstance(record['attachments'], list):
        raise EvidenceError('missing_record_field')
    if not isinstance(record['subject'], str) or not record['subject']:
        raise EvidenceError('missing_record_field')
    if record['result'] == 'blocked':
        category = record.get('blocked_category')
        if not isinstance(category, str) or not category.strip():
            raise EvidenceError('missing_blocked_category')


def _check_record_attachments(record: dict[str, Any], documents: dict[str, dict[str, Any]]) -> None:
    for rel in record['attachments']:
        if not isinstance(rel, str) or not rel:
            raise EvidenceError('missing_record_field')
        if rel.startswith('evidence/runtime/') and rel.endswith('.json'):
            if rel not in documents:
                raise EvidenceError('missing_runtime_capture')


def _record_for(records: list[dict[str, Any]], environment: str) -> dict[str, Any]:
    matches = [record for record in records if record.get('environment') == environment]
    if len(matches) != 1:
        raise EvidenceError('missing_record_field')
    return matches[0]


def _check_independence(
    windows: dict[str, Any],
    remote: dict[str, Any],
    documents: dict[str, dict[str, Any]],
) -> None:
    if windows['subject'] == remote['subject']:
        raise EvidenceError('runtime_records_not_independent')
    win_att = set(windows['attachments'])
    remote_att = set(remote['attachments'])
    if not win_att or not remote_att or win_att & remote_att:
        raise EvidenceError('runtime_records_not_independent')
    ignore = {
        'capture_id', 'kind', 'subject', 'environment', 'observed_at',
        'captured_at_utc', 'attachments',
    }
    if _public_items(windows, ignore) == _public_items(remote, ignore):
        raise EvidenceError('runtime_records_not_independent')
    for key in WINDOWS_PATH_KEYS:
        if key in remote or _capture_has(remote, documents, key):
            raise EvidenceError('runtime_records_not_independent')
    win_ids = _capture_ids(windows, documents)
    remote_ids = _capture_ids(remote, documents)
    if not win_ids or not remote_ids or win_ids & remote_ids:
        raise EvidenceError('runtime_records_not_independent')


def _public_items(record: dict[str, Any], ignore: set[str]) -> dict[str, Any]:
    return {key: value for key, value in record.items() if key not in ignore}


def _capture_has(record: dict[str, Any], documents: dict[str, dict[str, Any]], key: str) -> bool:
    for rel in record['attachments']:
        doc = documents.get(rel)
        if isinstance(doc, dict) and key in doc:
            return True
    return False


def _capture_ids(record: dict[str, Any], documents: dict[str, dict[str, Any]]) -> set[str]:
    ids: set[str] = set()
    for rel in record['attachments']:
        doc = documents.get(rel)
        if not isinstance(doc, dict):
            continue
        capture_id = doc.get('capture_id')
        if isinstance(capture_id, str) and capture_id:
            ids.add(capture_id)
    return ids


def _check_runtime_summaries(
    baseline: dict[str, Any],
    windows: dict[str, Any],
    remote: dict[str, Any],
) -> None:
    verification = baseline.get('runtime_verification')
    if not isinstance(verification, dict):
        raise EvidenceError('missing_record_field')
    for key in VERIFICATION_KEYS:
        if key not in verification:
            raise EvidenceError('missing_record_field')
    if verification.get('windows_local') != windows['result']:
        raise EvidenceError('runtime_summary_mismatch')
    if verification.get('remote_linux') != remote['result']:
        raise EvidenceError('runtime_summary_mismatch')
    for key in ('named_pipe_acl', 'windows_endpoint', 'windows_terminal_lease', 'ime'):
        if _is_success(verification.get(key)):
            raise EvidenceError('evidence_level_promotion')


def _check_runtime_claims(
    record: dict[str, Any],
    templates: dict[str, dict[str, Any]],
    captures: dict[str, dict[str, Any]],
    environment: str,
) -> None:
    result = record['result']
    level = record['evidence_level']
    success = _is_success(result)
    if success and level in NON_RUNTIME_LEVELS:
        raise EvidenceError('evidence_level_promotion')
    if success and level not in RUNTIME_LEVELS:
        raise EvidenceError('evidence_level_promotion')
    attached_templates = [rel for rel in record['attachments'] if rel in templates]
    if success and attached_templates:
        raise EvidenceError('template_used_as_runtime_proof')
    attached = _attached_captures(record, captures, environment)
    for cap in attached:
        fake = _looks_like_successful_run(cap) or cap.get('exit_code') == 0
        if fake and (not success or cap.get('evidence_level') in NON_RUNTIME_LEVELS):
            raise EvidenceError('evidence_level_promotion')
    runtime_cap = _runtime_capture(record, captures, environment)
    if success and runtime_cap is None:
        raise EvidenceError('missing_runtime_capture')
    herdr_runtime = (
        record['hashes'].get('runtime_binary_sha256'),
        record['hashes'].get('runtime_schema_sha256'),
        record['hashes'].get('distribution_binary_sha256'),
    )
    if any(value is not None for value in herdr_runtime) and runtime_cap is None:
        raise EvidenceError('missing_runtime_capture')


def _attached_captures(
    record: dict[str, Any],
    captures: dict[str, dict[str, Any]],
    environment: str,
) -> list[dict[str, Any]]:
    expected_kind = 'windows_runtime' if environment == 'windows_local' else 'remote_runtime'
    attached: list[dict[str, Any]] = []
    for rel in record['attachments']:
        cap = captures.get(rel)
        if cap is None or cap.get('template') is True:
            continue
        if cap.get('kind') != expected_kind:
            continue
        attached.append(cap)
    return attached


def _runtime_capture(
    record: dict[str, Any],
    captures: dict[str, dict[str, Any]],
    environment: str,
) -> dict[str, Any] | None:
    for cap in _attached_captures(record, captures, environment):
        if cap.get('evidence_level') in RUNTIME_LEVELS:
            return cap
    return None


def _looks_like_successful_run(capture: dict[str, Any]) -> bool:
    if capture.get('template') is True:
        return False
    if _is_success(capture.get('result')):
        return True
    if capture.get('exit_code') == 0 and capture.get('stdout_sha256'):
        return True
    return False


def _check_templates(templates: dict[str, dict[str, Any]]) -> None:
    for rel in REQUIRED_TEMPLATES:
        if rel not in templates:
            raise EvidenceError('capture_schema_mismatch')
    for doc in templates.values():
        for key in DESIGN_CAPTURE_FIELDS:
            if key not in doc:
                raise EvidenceError('capture_schema_mismatch')
        if doc.get('template') is not True:
            raise EvidenceError('template_used_as_runtime_proof')
        if (
            _is_success(doc.get('result'))
            or _looks_like_successful_run(doc)
            or doc.get('exit_code') == 0
        ):
            raise EvidenceError('template_used_as_runtime_proof')
        if doc.get('stdout_sha256') is not None or doc.get('stderr_sha256') is not None:
            raise EvidenceError('template_used_as_runtime_proof')
        if doc.get('captured_at_utc') is not None:
            raise EvidenceError('template_used_as_runtime_proof')


def _check_matrix(
    matrix: dict[str, Any],
    windows: dict[str, Any],
    remote: dict[str, Any],
) -> None:
    rows = matrix.get('rows')
    if not isinstance(rows, list) or not rows:
        raise EvidenceError('missing_record_field')
    if list(matrix.get('columns') or ()) != list(MATRIX_COLUMNS):
        raise EvidenceError('missing_record_field')
    if list(matrix.get('row_key') or ()) != list(MATRIX_KEY_FIELDS):
        raise EvidenceError('missing_record_field')
    default = matrix.get('compatible_by_default')
    if not isinstance(default, list):
        raise EvidenceError('missing_record_field')
    for combo in default:
        if not isinstance(combo, dict):
            raise EvidenceError('unknown_combo_marked_compatible')
        _reject_unknown_compatible(combo.get('key') or combo, True)
    if default and (
        windows['evidence_level'] not in RUNTIME_LEVELS
        or remote['evidence_level'] not in RUNTIME_LEVELS
    ):
        raise EvidenceError('unknown_combo_marked_compatible')
    for row in rows:
        if not isinstance(row, dict):
            raise EvidenceError('missing_record_field')
        key = row.get('key')
        if not isinstance(key, dict):
            raise EvidenceError('missing_record_field')
        for field in MATRIX_KEY_FIELDS:
            if field not in key:
                raise EvidenceError('missing_record_field')
        for column in MATRIX_COLUMNS:
            if column not in row or not isinstance(row[column], dict):
                raise EvidenceError('missing_record_field')
        if _is_success(row['windows_runtime'].get('status')):
            if windows['evidence_level'] not in RUNTIME_LEVELS:
                raise EvidenceError('evidence_level_promotion')
        if _is_success(row['remote_runtime'].get('status')):
            if remote['evidence_level'] not in RUNTIME_LEVELS:
                raise EvidenceError('evidence_level_promotion')
        compatible = row.get('compatible')
        if _unknown_key_fields(key):
            if compatible is not False:
                raise EvidenceError('unknown_combo_marked_compatible')
            continue
        if compatible is True:
            if row['windows_runtime'].get('status') not in SUCCESS_RESULTS:
                raise EvidenceError('unknown_combo_marked_compatible')
            if row['remote_runtime'].get('status') not in SUCCESS_RESULTS:
                raise EvidenceError('unknown_combo_marked_compatible')
            if windows['evidence_level'] not in RUNTIME_LEVELS or remote['evidence_level'] not in RUNTIME_LEVELS:
                raise EvidenceError('evidence_level_promotion')
            for column in ('windows_runtime', 'remote_runtime'):
                if row[column].get('evidence_level') in {'source_inspection_only', 'synthetic', 'hosted_ci'}:
                    raise EvidenceError('evidence_level_promotion')


def _unknown_key_fields(key: dict[str, Any]) -> list[str]:
    unknown: list[str] = []
    for field in MATRIX_KEY_FIELDS:
        value = key.get(field)
        if value in UNKNOWN_KEY_VALUES:
            unknown.append(field)
    return unknown


def _reject_unknown_compatible(key: dict[str, Any], compatible: bool) -> None:
    if compatible and _unknown_key_fields(key):
        raise EvidenceError('unknown_combo_marked_compatible')


def _check_herdr_runtime_hashes(
    baseline: dict[str, Any],
    windows: dict[str, Any],
    remote: dict[str, Any],
    captures: dict[str, dict[str, Any]],
) -> None:
    herdr = baseline['herdr']
    filled = any(
        herdr.get(key) is not None
        for key in SHA256_HASH_KEYS
    )
    if not filled:
        return
    if (
        _runtime_capture(windows, captures, 'windows_local') is None
        and _runtime_capture(remote, captures, 'remote_linux') is None
    ):
        raise EvidenceError('missing_runtime_capture')


def _check_source_field_sources(source: dict[str, Any]) -> None:
    sources = source.get('field_sources')
    if not isinstance(sources, dict):
        raise EvidenceError('missing_record_field')
    for key in SOURCE_FIELD_SOURCE_KEYS:
        value = sources.get(key)
        if not isinstance(value, str) or not value.strip():
            raise EvidenceError('missing_record_field')


def _is_success(result: Any) -> bool:
    return result in SUCCESS_RESULTS
