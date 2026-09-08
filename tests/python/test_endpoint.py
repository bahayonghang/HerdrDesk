from __future__ import annotations

import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.endpoint import (
    BLOCKED_CATEGORY,
    CAPTURE_REL,
    FIXTURE_REL,
    MATRIX_SCENARIOS,
    EndpointError,
    check_endpoint_matrix,
    resolve_endpoint,
    validate_endpoint_matrix,
)
from herddesk_g0.evidence import EvidenceError, check_evidence, validate_evidence
import validate_repository as repository


def _load(rel: str) -> dict:
    return json.loads((ROOT / rel).read_text(encoding='utf-8'))


def _bundle():
    fixture = _load(FIXTURE_REL)
    capture = _load(CAPTURE_REL)
    baseline = _load('evidence/compatibility-baseline.json')
    return fixture, capture, baseline


def _evidence_bundle():
    baseline = _load('evidence/compatibility-baseline.json')
    matrix = _load('evidence/version-support-matrix.json')
    documents = {}
    for path in sorted((ROOT / 'evidence' / 'runtime').glob('*.json')):
        documents['evidence/runtime/' + path.name] = json.loads(
            path.read_text(encoding='utf-8'))
    return baseline, matrix, documents


def _check(fixture=None, capture=None, baseline=None):
    shipped_fixture, shipped_capture, shipped_baseline = _bundle()
    return check_endpoint_matrix(
        shipped_fixture if fixture is None else fixture,
        shipped_capture if capture is None else capture,
        shipped_baseline if baseline is None else baseline,
    )


DEVICE = '11111111-1111-4111-8111-111111111111'
OTHER_DEVICE = '22222222-2222-4222-8222-222222222222'
SESSION = {'endpoint_key': 'local-api', 'session_name': None}
DEFAULT_MAPPING = {
    'device': DEVICE,
    'endpoint_key': 'local-api',
    'session_name': None,
    'canonical_location': '\\\\.\\pipe\\herdr-api',
    'kind': 'named_pipe',
    'evidence_id': 'synthetic-default-mapping',
    'access_scope': 'local_user',
}


class EndpointMatrixTests(unittest.TestCase):
    def test_shipped_matrix_is_simulation_and_not_ac03(self):
        result = validate_endpoint_matrix(ROOT)
        self.assertEqual(result['endpoint_validation'], 'passed')
        self.assertFalse(result['windows_verified'])
        self.assertFalse(result['ac03_passed'])
        self.assertEqual(result['ac03_c1'], 'blocked')
        self.assertEqual(result['ac03_c2'], 'hd-008_not_implemented')
        self.assertEqual(result['live_matrix'], 'blocked')
        self.assertEqual(result['blocked_category'], BLOCKED_CATEGORY)
        self.assertTrue(result['simulation'])
        self.assertEqual(result['rows'], 7)
        repo = repository.validate()
        self.assertEqual(repo['structural_validation'], 'passed')
        self.assertEqual(repo['endpoint_validation'], 'passed')
        self.assertFalse(repo['windows_verified'])
        self.assertFalse(repo['ac03_passed'])
        evidence = validate_evidence(ROOT)
        self.assertFalse(evidence['windows_verified'])

    def test_shipped_fixture_has_seven_blocked_rows(self):
        fixture = _load(FIXTURE_REL)
        self.assertIs(fixture['simulation'], True)
        self.assertIs(fixture['runtime_pass'], False)
        self.assertIs(fixture['windows_verified'], False)
        self.assertIs(fixture['ac03_passed'], False)
        self.assertIs(fixture['bridge_implemented'], False)
        self.assertEqual(fixture['all_live_checks'], 'blocked')
        self.assertEqual(fixture['blocked_category'], BLOCKED_CATEGORY)
        scenarios = [item['scenario'] for item in fixture['cases']]
        self.assertEqual(set(scenarios), set(MATRIX_SCENARIOS))
        self.assertEqual(len(scenarios), 7)

    def test_shipped_capture_consumes_hd001_residual(self):
        capture = _load(CAPTURE_REL)
        baseline = _load('evidence/compatibility-baseline.json')
        self.assertEqual(capture['blocked_category'], BLOCKED_CATEGORY)
        self.assertIs(capture['herdr_executed'], False)
        self.assertIs(capture['named_pipe_connected'], False)
        self.assertIs(capture['ac03_passed'], False)
        self.assertIs(capture['windows_verified'], False)
        self.assertEqual(
            baseline['runtime_verification']['windows_endpoint'], 'blocked')
        self.assertEqual(
            baseline['runtime_verification']['named_pipe_acl'], 'blocked')
        matches = [
            item for item in baseline['records']
            if item['environment'] == 'windows_endpoint'
        ]
        self.assertEqual(len(matches), 1)
        self.assertEqual(matches[0]['blocked_category'], BLOCKED_CATEGORY)

    def test_default_does_not_guess_appdata(self):
        result = resolve_endpoint(
            DEVICE, SESSION, {'kind': 'default'},
            {'verified_mappings': [], 'observations': []},
        )
        self.assertFalse(result['resolved'])
        self.assertEqual(result['failure']['code'], 'explicit_configuration_required')
        self.assertTrue(result['failure']['requires_explicit_configuration'])
        self.assertIsNone(result['endpoint'])
        self.assertNotIn('APPDATA', result['failure']['code'].upper())
        self.assertNotIn('APPDATA', result['failure']['diagnostic_id'].upper())
        self.assertNotIn('\\', result['failure']['diagnostic_id'])
        self.assertNotIn('/', result['failure']['diagnostic_id'])

    def test_named_does_not_fall_back_to_default(self):
        result = resolve_endpoint(
            DEVICE,
            {'endpoint_key': 'local-api', 'session_name': 'work'},
            {'kind': 'named', 'session_name': 'work'},
            {'verified_mappings': [DEFAULT_MAPPING], 'observations': []},
        )
        self.assertFalse(result['resolved'])
        self.assertEqual(result['failure']['code'], 'named_session_unmapped')
        self.assertTrue(result['failure']['requires_explicit_configuration'])
        self.assertIsNone(result['endpoint'])

    def test_unc_is_rejected_and_not_converted(self):
        config = {'verified_mappings': [DEFAULT_MAPPING], 'observations': []}
        for location in (
            '\\\\fileserver\\share\\herdr',
            '\\\\?\\UNC\\fileserver\\share',
            '\\\\fileserver\\pipe\\herdr-api',
            '//fileserver/share/herdr',
        ):
            with self.subTest(location=location):
                result = resolve_endpoint(
                    DEVICE, SESSION, {'kind': 'explicit', 'location': location}, config)
                self.assertFalse(result['resolved'])
                self.assertEqual(result['failure']['code'], 'remote_unc_rejected')
                self.assertIsNone(result['endpoint'])

    def test_pane_key_is_not_endpoint_identity(self):
        config = {
            'verified_mappings': [{
                'endpoint_key': 'local-api',
                'session_name': 'work',
                'canonical_location': '\\\\.\\pipe\\herdr-work',
                'kind': 'named_pipe',
                'evidence_id': 'synthetic-named-mapping',
                'access_scope': 'local_user',
            }],
            'observations': [],
        }
        by_pane = resolve_endpoint(
            DEVICE, SESSION, {'kind': 'named', 'session_name': 'p1'}, config)
        self.assertFalse(by_pane['resolved'])
        self.assertEqual(by_pane['failure']['code'], 'named_session_unmapped')
        by_title = resolve_endpoint(
            DEVICE, SESSION, {'kind': 'explicit', 'location': 'Claude Code'},
            {'verified_mappings': [], 'observations': []},
        )
        self.assertFalse(by_title['resolved'])
        self.assertEqual(by_title['failure']['code'], 'explicit_configuration_required')
        by_agent = resolve_endpoint(
            DEVICE, SESSION, {'kind': 'explicit', 'location': 'claude-code'},
            {'verified_mappings': [], 'observations': []},
        )
        self.assertFalse(by_agent['resolved'])
        self.assertEqual(by_agent['failure']['code'], 'explicit_configuration_required')

    def test_explicit_does_not_rewrite_to_another_path(self):
        result = resolve_endpoint(
            DEVICE, SESSION,
            {'kind': 'explicit', 'location': '\\\\.\\pipe\\herdr-other'},
            {'verified_mappings': [DEFAULT_MAPPING], 'observations': []},
        )
        self.assertTrue(result['resolved'])
        self.assertEqual(
            result['endpoint']['canonical_location'], '\\\\.\\pipe\\herdr-other')

    def test_appdata_token_is_not_expanded(self):
        result = resolve_endpoint(
            DEVICE, SESSION,
            {'kind': 'explicit', 'location': '%APPDATA%\\herdr\\api.sock'},
            {'verified_mappings': [], 'observations': []},
        )
        self.assertFalse(result['resolved'])
        self.assertEqual(result['failure']['code'], 'explicit_configuration_required')
        self.assertNotIn('\\', result['failure']['diagnostic_id'])
        self.assertNotIn('%APPDATA%', result['failure']['diagnostic_id'])

    def test_unpaired_surrogate_is_encoding_error(self):
        result = resolve_endpoint(
            DEVICE, SESSION,
            {'kind': 'explicit', 'location': '\\\\.\\pipe\\' + '\ud800'},
            {'verified_mappings': [], 'observations': []},
        )
        self.assertFalse(result['resolved'])
        self.assertEqual(result['failure']['code'], 'unicode_encoding_error')

    def test_unicode_location_is_preserved(self):
        location = '\\\\.\\pipe\\牧台-api-测试'
        result = resolve_endpoint(
            DEVICE, SESSION, {'kind': 'explicit', 'location': location},
            {'verified_mappings': [], 'observations': []},
        )
        self.assertTrue(result['resolved'])
        self.assertEqual(result['endpoint']['canonical_location'], location)
        self.assertNotIn('\ufffd', result['endpoint']['canonical_location'])

    def test_permission_denied_is_not_retried_as_other_user(self):
        result = resolve_endpoint(
            DEVICE, SESSION,
            {'kind': 'explicit', 'location': '\\\\.\\pipe\\herdr-api'},
            {
                'verified_mappings': [DEFAULT_MAPPING],
                'observations': [{
                    'canonical_location': '\\\\.\\pipe\\herdr-api',
                    'kind': 'permission_denied',
                    'diagnostic_id': 'synthetic-acl-denied',
                }],
            },
        )
        self.assertFalse(result['resolved'])
        self.assertEqual(result['failure']['code'], 'permission_denied')
        self.assertFalse(result['failure']['requires_explicit_configuration'])

    def test_core_resolver_source_has_no_os_or_pipe_apis(self):
        text = (ROOT / 'src' / 'HerdDesk.Core' / 'EndpointResolver.cs').read_text(
            encoding='utf-8')
        for needle in (
            'GetEnvironmentVariable',
            'GetFolderPath',
            'NamedPipeClientStream',
            'WindowsIdentity',
            'DllImport',
            'System.IO.Pipes',
            'System.Net.Sockets',
            'JsonRpc',
            'herdr terminal',
        ):
            self.assertNotIn(needle, text)

    def test_fixture_runtime_pass_is_rejected(self):
        fixture, _, _ = _bundle()
        fixture['runtime_pass'] = True
        with self.assertRaises(EndpointError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_fixture_cannot_claim_windows_verified(self):
        fixture, _, _ = _bundle()
        fixture['windows_verified'] = True
        with self.assertRaises(EndpointError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_fixture_cannot_claim_ac03_passed(self):
        fixture, _, _ = _bundle()
        fixture['ac03_passed'] = True
        with self.assertRaises(EndpointError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'ac03_claimed_passed')

    def test_capture_success_looks_like_runtime_is_rejected(self):
        _, capture, _ = _bundle()
        capture['result'] = 'passed'
        capture['exit_code'] = 0
        capture['stdout_sha256'] = 'a' * 64
        with self.assertRaises(EndpointError) as ctx:
            _check(capture=capture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_row_live_result_passed_is_rejected(self):
        fixture, _, _ = _bundle()
        fixture['cases'][0]['live_result'] = 'passed'
        with self.assertRaises(EndpointError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_missing_matrix_row_is_rejected(self):
        fixture, _, _ = _bundle()
        fixture['cases'] = fixture['cases'][:6]
        with self.assertRaises(EndpointError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'missing_matrix_row')

    def test_windows_endpoint_success_without_runtime_is_rejected(self):
        baseline, matrix, documents = _evidence_bundle()
        baseline['runtime_verification']['windows_endpoint'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            check_evidence(baseline, matrix, documents)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_resolver_does_not_create_session_or_send_rpc(self):
        source = (ROOT / 'src' / 'HerdDesk.Core' / 'EndpointResolver.cs').read_text(
            encoding='utf-8')
        self.assertNotIn('Process.Start', source)
        self.assertNotIn('NamedPipeClient', source)
        self.assertNotIn('herdr terminal session', source)

    def test_other_device_mapping_is_ignored(self):
        mapping = dict(DEFAULT_MAPPING)
        mapping['device'] = OTHER_DEVICE
        result = resolve_endpoint(
            DEVICE, SESSION, {'kind': 'default'},
            {'verified_mappings': [mapping], 'observations': []},
        )
        self.assertFalse(result['resolved'])
        self.assertEqual(result['failure']['code'], 'explicit_configuration_required')
        self.assertIsNone(result['endpoint'])

    def test_conventional_pipe_name_is_not_guessed(self):
        empty = {'verified_mappings': [], 'observations': []}
        bare = resolve_endpoint(
            DEVICE, SESSION, {'kind': 'explicit', 'location': 'herdr-api'}, empty)
        self.assertFalse(bare['resolved'])
        self.assertEqual(bare['failure']['code'], 'explicit_configuration_required')
        self.assertIsNone(bare['endpoint'])
        device_ns = resolve_endpoint(
            DEVICE, SESSION, {'kind': 'explicit', 'location': '//./herdr-api'}, empty)
        self.assertFalse(device_ns['resolved'])
        self.assertEqual(device_ns['failure']['code'], 'explicit_configuration_required')
        self.assertIsNone(device_ns['endpoint'])


if __name__ == '__main__':
    unittest.main()
