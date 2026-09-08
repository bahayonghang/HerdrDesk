"""HD-003 endpoint-matrix diagnostics.

Controlled-config mapping only. This module does not connect to a pipe,
query OS credentials, expand APPDATA, or implement HD-008 bridge.
Synthetic fixtures are not Windows runtime proof and do not pass AC03.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any


class EndpointError(ValueError):
    """Stable endpoint-rule code; the message is the code only."""


FIXTURE_REL = 'tests/fixtures/endpoint-cases.json'
CAPTURE_REL = 'evidence/runtime/windows-endpoint-matrix.blocked.json'
BASELINE_REL = 'evidence/compatibility-baseline.json'
BLOCKED_CATEGORY = 'no_authorized_isolated_windows_endpoint_or_live_herdr_grant'
MATRIX_SCENARIOS = (
    'explicit_endpoint',
    'default_session',
    'named_session',
    'unicode_endpoint',
    'acl_denied',
    'cross_user',
    'remote_unc',
)
SUCCESS_RESULTS = frozenset({
    'passed', 'verified', 'compatible', 'success', 'ok',
})
FORBIDDEN_ENV = (
    '%APPDATA%', '%LOCALAPPDATA%', '%USERPROFILE%', '%HOMEPATH%', '%HOMEDRIVE%',
)


def validate_endpoint_matrix(root: Path) -> dict[str, Any]:
    root = Path(root)
    fixture = _load_json(root / FIXTURE_REL)
    capture = _load_json(root / CAPTURE_REL)
    baseline = _load_json(root / BASELINE_REL)
    return check_endpoint_matrix(fixture, capture, baseline)


def check_endpoint_matrix(
    fixture: dict[str, Any],
    capture: dict[str, Any],
    baseline: dict[str, Any] | None = None,
) -> dict[str, Any]:
    _reject_promotion(fixture, capture)
    _check_fixture_shape(fixture)
    _check_capture(capture)
    if baseline is not None:
        _check_baseline(baseline, capture)
    for case in fixture['cases']:
        actual = resolve_endpoint(
            case['device'],
            case['session'],
            case['preference'],
            case['config'],
        )
        _check_case_expect(case, actual)
    return {
        'endpoint_validation': 'passed',
        'windows_verified': False,
        'ac03_passed': False,
        'ac03_c1': 'blocked',
        'ac03_c2': 'hd-008_not_implemented',
        'live_matrix': 'blocked',
        'blocked_category': BLOCKED_CATEGORY,
        'simulation': True,
        'rows': len(fixture['cases']),
    }


def resolve_endpoint(
    device: str,
    session: dict[str, Any],
    preference: dict[str, Any],
    config: dict[str, Any],
) -> dict[str, Any]:
    if not _valid_identity(device, session):
        return _fail('invalid_identity', 'diag-invalid-identity', False)
    encoding = _check_unicode(session.get('endpoint_key')) or _check_unicode(
        session.get('session_name'))
    if encoding is not None:
        return encoding
    kind = preference.get('kind')
    if kind == 'explicit':
        return _resolve_explicit(device, session, preference, config)
    if kind == 'default':
        return _resolve_default(device, session, preference, config)
    if kind == 'named':
        return _resolve_named(device, session, preference, config)
    return _fail('invalid_preference', 'diag-invalid-preference', True)


def _resolve_explicit(device, session, preference, config):
    if preference.get('session_name'):
        return _fail('invalid_preference', 'diag-invalid-preference', True)
    location = preference.get('location')
    if not isinstance(location, str) or not location.strip():
        return _fail('invalid_preference', 'diag-invalid-preference', True)
    encoding = _check_unicode(location)
    if encoding is not None:
        return encoding
    if _has_illegal_control(location):
        return _fail('invalid_preference', 'diag-invalid-preference', True)
    if _looks_like_environment_guess(location):
        return _fail('explicit_configuration_required', 'diag-unmapped-default', True)
    if _is_remote_unc(location):
        return _fail('remote_unc_rejected', 'diag-remote-unc', False)
    classified = _classify(location)
    if classified is None:
        return _fail('explicit_configuration_required', 'diag-unmapped-default', True)
    evidence_id = 'explicit-preference'
    scope = 'local_user'
    kind = classified
    for mapping in config.get('verified_mappings') or ():
        if not _mapping_matches_identity(mapping, device, session):
            continue
        if mapping.get('canonical_location') != location:
            continue
        evidence_id = _redact(mapping.get('evidence_id'), evidence_id)
        kind = mapping.get('kind') or kind
        scope = mapping.get('access_scope') or scope
        break
    return _apply_observations(
        location,
        {
            'kind': kind,
            'canonical_location': location,
            'evidence_id': evidence_id,
            'access_scope': scope,
        },
        config,
    )


def _resolve_default(device, session, preference, config):
    if session.get('session_name') or preference.get('session_name') or preference.get('location'):
        return _fail('invalid_preference', 'diag-invalid-preference', True)
    return _resolve_mapped(
        device, session, None, config,
        'explicit_configuration_required', 'diag-unmapped-default',
    )


def _resolve_named(device, session, preference, config):
    name = preference.get('session_name')
    if not isinstance(name, str) or not name or preference.get('location'):
        return _fail('invalid_preference', 'diag-invalid-preference', True)
    encoding = _check_unicode(name)
    if encoding is not None:
        return encoding
    session_name = session.get('session_name')
    if not _is_default_name(session_name) and session_name != name:
        return _fail('invalid_preference', 'diag-invalid-preference', True)
    return _resolve_mapped(
        device, session, name, config,
        'named_session_unmapped', 'diag-unmapped-named',
    )


def _resolve_mapped(device, session, session_name, config, unmapped_code, unmapped_diag):
    found = None
    count = 0
    for mapping in config.get('verified_mappings') or ():
        if not _mapping_matches_identity(mapping, device, session):
            continue
        if not _session_name_equals(mapping.get('session_name'), session_name):
            continue
        found = mapping
        count += 1
    if count == 0:
        return _fail(unmapped_code, unmapped_diag, True)
    if count > 1 or found is None:
        return _fail('ambiguous_mapping', 'diag-ambiguous-mapping', True)
    location = found.get('canonical_location')
    if not isinstance(location, str):
        return _fail('explicit_configuration_required', 'diag-unmapped-default', True)
    encoding = _check_unicode(location)
    if encoding is not None:
        return encoding
    if _has_illegal_control(location) or _looks_like_environment_guess(location):
        return _fail('explicit_configuration_required', 'diag-unmapped-default', True)
    if _is_remote_unc(location):
        return _fail('remote_unc_rejected', 'diag-remote-unc', False)
    return _apply_observations(
        location,
        {
            'kind': found.get('kind'),
            'canonical_location': location,
            'evidence_id': _redact(found.get('evidence_id'), 'verified-mapping'),
            'access_scope': found.get('access_scope'),
        },
        config,
    )


def _apply_observations(location, candidate, config):
    for observation in config.get('observations') or ():
        if observation.get('canonical_location') != location:
            continue
        kind = observation.get('kind')
        if kind == 'permission_denied':
            return _fail(
                'permission_denied',
                _redact(observation.get('diagnostic_id'), 'diag-permission-denied'),
                False,
            )
        if kind == 'cross_user':
            return _fail(
                'cross_user_denied',
                _redact(observation.get('diagnostic_id'), 'diag-cross-user'),
                False,
            )
        if kind == 'missing':
            return _fail(
                'endpoint_not_found',
                _redact(observation.get('diagnostic_id'), 'diag-explicit-missing'),
                True,
            )
        return _fail('invalid_preference', 'diag-invalid-preference', True)
    return {'resolved': True, 'endpoint': candidate, 'failure': None}


def _valid_identity(device, session) -> bool:
    if not isinstance(device, str) or not device or device == '00000000-0000-0000-0000-000000000000':
        return False
    if not isinstance(session, dict):
        return False
    session_device = session.get('device')
    if session_device is not None and session_device != device:
        return False
    endpoint_key = session.get('endpoint_key')
    return isinstance(endpoint_key, str) and bool(endpoint_key.strip())


def _mapping_matches_identity(mapping, device, session) -> bool:
    if not isinstance(mapping, dict):
        return False
    mapped_device = mapping.get('device')
    if mapped_device is not None and mapped_device != device:
        return False
    return mapping.get('endpoint_key') == session.get('endpoint_key')


def _is_default_name(name) -> bool:
    return name is None or name == ''


def _session_name_equals(left, right) -> bool:
    if _is_default_name(left) and _is_default_name(right):
        return True
    if _is_default_name(left) or _is_default_name(right):
        return False
    return left == right


def _has_illegal_control(value: str) -> bool:
    return '\0' in value or '\r' in value or '\n' in value


def _looks_like_environment_guess(location: str) -> bool:
    upper = location.upper()
    return any(token in upper for token in FORBIDDEN_ENV)


def _is_remote_unc(location: str) -> bool:
    if location.startswith('//') and not location.startswith('//./') and not location.startswith('//?/'):
        return True
    if not location.startswith('\\\\'):
        return False
    if location.upper().startswith('\\\\?\\UNC\\'):
        return True
    if location.startswith('\\\\.\\') or location.startswith('\\\\?\\'):
        return False
    return True


def _classify(location: str) -> str | None:
    if _is_local_named_pipe(location):
        return 'named_pipe'
    if location.startswith('/') and not location.startswith('//'):
        return 'unix_socket'
    if _is_windows_drive_path(location):
        return 'filesystem_path'
    return None


def _is_local_named_pipe(location: str) -> bool:
    lower = location.lower()
    return (
        lower.startswith('\\\\.\\pipe\\')
        or lower.startswith('\\\\?\\pipe\\')
        or lower.startswith('//./pipe/')
        or lower.startswith('//?/pipe/')
    )


def _is_windows_drive_path(location: str) -> bool:
    return (
        len(location) >= 3
        and location[0].isascii()
        and location[0].isalpha()
        and location[1] == ':'
        and location[2] in '\\/'
    )


def _check_unicode(value):
    if value is None:
        return None
    if not isinstance(value, str):
        return _fail('unicode_encoding_error', 'diag-unicode-encoding', False)
    try:
        value.encode('utf-8')
    except UnicodeEncodeError:
        return _fail('unicode_encoding_error', 'diag-unicode-encoding', False)
    return None


def _redact(candidate, fallback: str) -> str:
    if not isinstance(candidate, str) or not candidate.strip():
        return fallback
    if '\\' in candidate or '/' in candidate or '%' in candidate:
        return fallback
    try:
        candidate.encode('utf-8')
    except UnicodeEncodeError:
        return fallback
    return candidate


def _fail(code: str, diagnostic_id: str, requires_explicit: bool) -> dict[str, Any]:
    return {
        'resolved': False,
        'endpoint': None,
        'failure': {
            'code': code,
            'diagnostic_id': diagnostic_id,
            'requires_explicit_configuration': requires_explicit,
        },
    }


def _reject_promotion(fixture: dict[str, Any], capture: dict[str, Any]) -> None:
    for doc in (fixture, capture):
        if doc.get('runtime_pass') is True:
            raise EndpointError('evidence_level_promotion')
        if doc.get('windows_verified') is True:
            raise EndpointError('evidence_level_promotion')
        if doc.get('ac03_passed') is True:
            raise EndpointError('ac03_claimed_passed')
        if _is_success(doc.get('result')) or _is_success(doc.get('live_result')):
            raise EndpointError('evidence_level_promotion')
        if _is_success(doc.get('all_live_checks')) or _is_success(doc.get('live_matrix')):
            raise EndpointError('evidence_level_promotion')
    if fixture.get('simulation') is not True:
        raise EndpointError('evidence_level_promotion')
    if fixture.get('kind') != 'synthetic_endpoint_matrix':
        raise EndpointError('missing_record_field')


def _check_fixture_shape(fixture: dict[str, Any]) -> None:
    if fixture.get('runtime_pass') is not False:
        raise EndpointError('evidence_level_promotion')
    if fixture.get('windows_verified') is not False:
        raise EndpointError('evidence_level_promotion')
    if fixture.get('ac03_passed') is not False:
        raise EndpointError('ac03_claimed_passed')
    if fixture.get('bridge_implemented') is not False:
        raise EndpointError('evidence_level_promotion')
    if fixture.get('all_live_checks') != 'blocked':
        raise EndpointError('missing_record_field')
    if fixture.get('blocked_category') != BLOCKED_CATEGORY:
        raise EndpointError('missing_blocked_category')
    cases = fixture.get('cases')
    if not isinstance(cases, list) or len(cases) != 7:
        raise EndpointError('missing_matrix_row')
    seen: list[str] = []
    for case in cases:
        if not isinstance(case, dict):
            raise EndpointError('missing_matrix_row')
        scenario = case.get('scenario')
        if scenario not in MATRIX_SCENARIOS or scenario in seen:
            raise EndpointError('missing_matrix_row')
        seen.append(scenario)
        if case.get('live_result') != 'blocked':
            raise EndpointError('evidence_level_promotion')
        if case.get('evidence_level') != 'synthetic':
            raise EndpointError('evidence_level_promotion')
        if case.get('simulation') is not True:
            raise EndpointError('evidence_level_promotion')
        expect = case.get('expect')
        if not isinstance(expect, dict):
            raise EndpointError('missing_record_field')
        location = (expect.get('canonical_location') or '')
        if '%APPDATA%' in location.upper():
            raise EndpointError('evidence_level_promotion')
    if set(seen) != set(MATRIX_SCENARIOS):
        raise EndpointError('missing_matrix_row')


def _check_capture(capture: dict[str, Any]) -> None:
    if capture.get('template') is True:
        raise EndpointError('evidence_level_promotion')
    if capture.get('kind') != 'windows_endpoint_matrix':
        raise EndpointError('missing_record_field')
    if capture.get('result') != 'blocked':
        raise EndpointError('evidence_level_promotion')
    if capture.get('blocked_category') != BLOCKED_CATEGORY:
        raise EndpointError('missing_blocked_category')
    if capture.get('herdr_executed') is not False:
        raise EndpointError('evidence_level_promotion')
    if capture.get('named_pipe_connected') is not False:
        raise EndpointError('evidence_level_promotion')
    if capture.get('live_matrix') != 'blocked':
        raise EndpointError('evidence_level_promotion')
    if capture.get('ac03_passed') is not False:
        raise EndpointError('ac03_claimed_passed')
    if capture.get('windows_verified') is not False:
        raise EndpointError('evidence_level_promotion')
    if capture.get('bridge_implemented') is not False:
        raise EndpointError('evidence_level_promotion')
    if capture.get('exit_code') == 0 and capture.get('stdout_sha256'):
        raise EndpointError('evidence_level_promotion')
    covers = capture.get('covers')
    if not isinstance(covers, list) or set(covers) != set(MATRIX_SCENARIOS):
        raise EndpointError('missing_matrix_row')


def _check_baseline(baseline: dict[str, Any], capture: dict[str, Any]) -> None:
    verification = baseline.get('runtime_verification')
    if not isinstance(verification, dict):
        raise EndpointError('missing_record_field')
    if verification.get('windows_endpoint') != 'blocked':
        if _is_success(verification.get('windows_endpoint')):
            raise EndpointError('evidence_level_promotion')
        raise EndpointError('missing_record_field')
    if verification.get('named_pipe_acl') != 'blocked':
        if _is_success(verification.get('named_pipe_acl')):
            raise EndpointError('evidence_level_promotion')
    records = baseline.get('records')
    if not isinstance(records, list):
        raise EndpointError('missing_record_field')
    matches = [item for item in records if item.get('environment') == 'windows_endpoint']
    if len(matches) != 1:
        raise EndpointError('missing_record_field')
    record = matches[0]
    if record.get('result') != 'blocked':
        raise EndpointError('evidence_level_promotion')
    if record.get('blocked_category') != BLOCKED_CATEGORY:
        raise EndpointError('missing_blocked_category')
    if CAPTURE_REL not in record.get('attachments', []):
        raise EndpointError('missing_record_field')
    if record.get('evidence_level') == 'synthetic' and _is_success(record.get('result')):
        raise EndpointError('evidence_level_promotion')


def _check_case_expect(case: dict[str, Any], actual: dict[str, Any]) -> None:
    expect = case['expect']
    if actual.get('resolved') is not expect.get('resolved'):
        raise EndpointError('matrix_expect_mismatch')
    if expect.get('resolved') is True:
        endpoint = actual.get('endpoint') or {}
        if endpoint.get('kind') != expect.get('kind'):
            raise EndpointError('matrix_expect_mismatch')
        if endpoint.get('canonical_location') != expect.get('canonical_location'):
            raise EndpointError('matrix_expect_mismatch')
        if endpoint.get('access_scope') != expect.get('access_scope'):
            raise EndpointError('matrix_expect_mismatch')
        pref = case.get('preference') or {}
        if pref.get('kind') == 'explicit' and endpoint.get('canonical_location') != pref.get('location'):
            raise EndpointError('matrix_expect_mismatch')
        _assert_redacted(endpoint.get('evidence_id'))
        return
    failure = actual.get('failure') or {}
    if failure.get('code') != expect.get('code'):
        raise EndpointError('matrix_expect_mismatch')
    if failure.get('requires_explicit_configuration') is not expect.get(
            'requires_explicit_configuration'):
        raise EndpointError('matrix_expect_mismatch')
    _assert_redacted(failure.get('code'))
    _assert_redacted(failure.get('diagnostic_id'))


def _assert_redacted(value: Any) -> None:
    if not isinstance(value, str):
        return
    if '\\' in value or '/' in value or '%APPDATA%' in value.upper():
        raise EndpointError('path_not_redacted')


def _is_success(result: Any) -> bool:
    return result in SUCCESS_RESULTS


def _load_json(path: Path) -> dict[str, Any]:
    loaded = json.loads(path.read_text(encoding='utf-8'))
    if not isinstance(loaded, dict):
        raise EndpointError('missing_record_field')
    return loaded
