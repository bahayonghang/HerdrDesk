from __future__ import annotations

import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.evidence import EvidenceError, check_evidence, validate_evidence
import validate_repository as repository


def _load(rel: str) -> dict:
    return json.loads((ROOT / rel).read_text(encoding='utf-8'))


def _bundle() -> tuple[dict, dict, dict]:
    baseline = _load('evidence/compatibility-baseline.json')
    matrix = _load('evidence/version-support-matrix.json')
    documents = {}
    for path in sorted((ROOT / 'evidence' / 'runtime').glob('*.json')):
        documents['evidence/runtime/' + path.name] = json.loads(
            path.read_text(encoding='utf-8'))
    return baseline, matrix, documents


def _check(baseline=None, matrix=None, documents=None):
    shipped_baseline, shipped_matrix, shipped_documents = _bundle()
    return check_evidence(
        shipped_baseline if baseline is None else baseline,
        shipped_matrix if matrix is None else matrix,
        shipped_documents if documents is None else documents,
    )


def _record(baseline: dict, environment: str) -> dict:
    matches = [item for item in baseline['records'] if item['environment'] == environment]
    if len(matches) != 1:
        raise AssertionError('missing shipped record for ' + environment)
    return matches[0]


class EvidenceBaselineTests(unittest.TestCase):
    def test_shipped_evidence_passes_structure_and_is_not_windows_verified(self):
        result = validate_evidence(ROOT)
        self.assertEqual(result['evidence_validation'], 'passed')
        self.assertFalse(result['windows_verified'])
        self.assertEqual(result['runtime_windows'], 'recorded')
        self.assertEqual(result['runtime_remote'], 'not_run')
        self.assertEqual(result['source_evidence_level'], 'source_inspection_only')
        repo = repository.validate()
        self.assertEqual(repo['structural_validation'], 'passed')
        self.assertEqual(repo['evidence_validation'], 'passed')
        self.assertFalse(repo['windows_verified'])

    def test_shipped_hash_fields_are_distinct(self):
        result = validate_evidence(ROOT)
        self.assertFalse(result['windows_verified'])
        baseline = _load('evidence/compatibility-baseline.json')
        herdr = baseline['herdr']
        blobs = herdr['source_blobs']
        self.assertEqual(herdr['tag'], 'v0.8.2')
        self.assertEqual(herdr['commit'], '9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c')
        self.assertEqual(blobs['src/client/mod.rs'], 'c33157fb0d9b7c1632562fced6bd9e439de80856')
        self.assertEqual(blobs['src/ipc.rs'], '36e69ea2a571de096e3150779eb9cae6e803d7a8')
        self.assertEqual(
            blobs['src/server/render_stream.rs'],
            'f14deb5e7e2f3183410020e5f36f7fb11dda6780',
        )
        self.assertEqual(
            blobs['docs/next/api/herdr-api.schema.json'],
            'f9642ffa0deb4dc87052a5247d700e1dcd50a753',
        )
        self.assertEqual(len(set(blobs.values())), 4)
        self.assertEqual(
            herdr['runtime_binary_sha256'],
            'd3e69a7810beb6077c47bd8d876f50929152828a6c210c5e6d001f9220137da6',
        )
        self.assertEqual(
            herdr['runtime_schema_sha256'],
            '5fb46b13fdaf39c88cf699b9806685868c7ee6b0142523d84391b1606416dc0a',
        )
        self.assertIsNone(herdr['distribution_binary_sha256'])
        self.assertEqual(herdr['api_protocol'], 20)
        self.assertNotEqual(herdr['runtime_binary_sha256'], blobs['src/client/mod.rs'])
        self.assertNotEqual(
            herdr['runtime_schema_sha256'],
            blobs['docs/next/api/herdr-api.schema.json'],
        )
        self.assertIs(baseline['default_write_capability'], False)
        source = _record(baseline, 'source_inspection')
        for key in ('tag', 'commit', 'api_protocol', 'schema_version'):
            self.assertTrue(source['field_sources'][key])

    def test_shipped_windows_record_is_isolated_preview_not_compatible(self):
        baseline = _load('evidence/compatibility-baseline.json')
        windows = _record(baseline, 'windows_local')
        capture = _load('evidence/runtime/windows-runtime.capture.json')
        self.assertEqual(windows['result'], 'recorded')
        self.assertEqual(windows['evidence_level'], 'isolated_windows_runtime')
        self.assertEqual(windows['runtime_protocol'], 22)
        self.assertIs(windows['matches_reference_header'], False)
        self.assertNotIn('blocked_category', windows)
        self.assertEqual(capture['protocol'], 22)
        self.assertIs(capture['matches_reference_header'], False)
        self.assertIs(capture['herdr_executed'], True)
        self.assertIs(capture['template'], False)
        self.assertEqual(capture['named_pipe_acl'], 'not_captured')
        self.assertEqual(baseline['herdr']['api_protocol'], 20)
        self.assertFalse(validate_evidence(ROOT)['windows_verified'])
        self.assertEqual(validate_evidence(ROOT)['runtime_windows'], 'recorded')

    def test_shipped_remote_record_is_not_run_and_not_copied_from_windows(self):
        baseline = _load('evidence/compatibility-baseline.json')
        windows = _record(baseline, 'windows_local')
        remote = _record(baseline, 'remote_linux')
        remote_capture = _load('evidence/runtime/remote-runtime.not-run.json')
        self.assertEqual(remote['result'], 'not_run')
        self.assertNotEqual(windows['subject'], remote['subject'])
        self.assertNotEqual(windows['attachments'], remote['attachments'])
        self.assertNotIn('blocked_category', remote)
        self.assertNotIn('herdr_binary_on_path', remote)
        self.assertNotIn('blocked_category', remote_capture)
        self.assertNotIn('herdr_binary_on_path', remote_capture)
        self.assertNotIn('herdr_path_redacted', remote_capture)
        self.assertIs(remote_capture['herdr_executed'], False)
        self.assertNotEqual(
            _load('evidence/runtime/windows-runtime.capture.json')['capture_id'],
            remote_capture['capture_id'],
        )
        self.assertEqual(validate_evidence(ROOT)['runtime_remote'], 'not_run')

    def test_templates_are_not_successful_runs(self):
        windows_template = _load('evidence/runtime/windows-runtime.template.json')
        remote_template = _load('evidence/runtime/remote-runtime.template.json')
        for doc in (windows_template, remote_template):
            self.assertIs(doc['template'], True)
            self.assertIsNone(doc['exit_code'])
            self.assertIsNone(doc['stdout_sha256'])
            self.assertIsNone(doc['captured_at_utc'])
        _check()

    def test_conflating_git_blob_into_runtime_binary_is_rejected(self):
        baseline, _, _ = _bundle()
        blob = baseline['herdr']['source_blobs']['src/client/mod.rs']
        baseline['herdr']['runtime_binary_sha256'] = blob
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'hash_conflation')

    def test_conflating_git_blob_into_runtime_schema_is_rejected(self):
        baseline, _, _ = _bundle()
        blob = baseline['herdr']['source_blobs']['docs/next/api/herdr-api.schema.json']
        baseline['herdr']['runtime_schema_sha256'] = blob
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'hash_conflation')

    def test_conflating_git_blob_into_record_runtime_hash_is_rejected(self):
        baseline, _, _ = _bundle()
        blob = baseline['herdr']['source_blobs']['src/ipc.rs']
        _record(baseline, 'windows_local')['hashes']['runtime_binary_sha256'] = blob
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'hash_conflation')

    def test_runtime_success_without_capture_is_rejected(self):
        baseline, _, _ = _bundle()
        windows = _record(baseline, 'windows_local')
        windows['result'] = 'passed'
        windows['evidence_level'] = 'isolated_windows_runtime'
        windows['attachments'] = [
            'evidence/runtime/windows-endpoint-matrix.blocked.json',
        ]
        windows['hashes']['runtime_binary_sha256'] = 'a' * 64
        windows['hashes']['runtime_schema_sha256'] = None
        windows.pop('blocked_category', None)
        baseline['runtime_verification']['windows_local'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'missing_runtime_capture')

    def test_template_cannot_prove_runtime_success(self):
        baseline, _, _ = _bundle()
        windows = _record(baseline, 'windows_local')
        windows['result'] = 'passed'
        windows['evidence_level'] = 'isolated_windows_runtime'
        windows['attachments'] = ['evidence/runtime/windows-runtime.template.json']
        if 'blocked_category' in windows:
            del windows['blocked_category']
        baseline['runtime_verification']['windows_local'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'template_used_as_runtime_proof')

    def test_non_runtime_levels_cannot_be_promoted(self):
        for level in ('source_inspection_only', 'synthetic', 'hosted_ci'):
            with self.subTest(level=level):
                baseline, _, _ = _bundle()
                windows = _record(baseline, 'windows_local')
                windows['result'] = 'passed'
                windows['evidence_level'] = level
                if 'blocked_category' in windows:
                    del windows['blocked_category']
                baseline['runtime_verification']['windows_local'] = 'passed'
                with self.assertRaises(EvidenceError) as ctx:
                    _check(baseline=baseline)
                self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_windows_and_remote_must_be_independent_records(self):
        baseline, _, documents = _bundle()
        windows = _record(baseline, 'windows_local')
        remote = _record(baseline, 'remote_linux')
        remote.clear()
        remote.update(copy.deepcopy(windows))
        remote['environment'] = 'remote_linux'
        remote['subject'] = 'copied-windows'
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline, documents=documents)
        self.assertEqual(str(ctx.exception), 'runtime_records_not_independent')

    def test_remote_cannot_reuse_windows_path_fields(self):
        baseline, _, documents = _bundle()
        remote = _record(baseline, 'remote_linux')
        documents[remote['attachments'][0]]['herdr_path_redacted'] = (
            'user_local_programs/Herdr/bin/herdr.EXE'
        )
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline, documents=documents)
        self.assertEqual(str(ctx.exception), 'runtime_records_not_independent')

    def test_remote_may_record_its_own_blocked_category(self):
        baseline, matrix, _ = _bundle()
        remote = _record(baseline, 'remote_linux')
        remote['result'] = 'blocked'
        remote['evidence_level'] = 'blocked'
        remote['blocked_category'] = 'no_authorized_remote_test_device'
        baseline['runtime_verification']['remote_linux'] = 'blocked'
        matrix['rows'][0]['remote_runtime']['status'] = 'blocked'
        matrix['rows'][0]['remote_runtime']['evidence_level'] = 'blocked'
        result = _check(baseline=baseline, matrix=matrix)
        self.assertEqual(result['runtime_remote'], 'blocked')
        self.assertFalse(result['windows_verified'])

    def test_unknown_matrix_key_is_not_compatible(self):
        _, matrix, _ = _bundle()
        matrix['rows'][0]['compatible'] = True
        with self.assertRaises(EvidenceError) as ctx:
            _check(matrix=matrix)
        self.assertEqual(str(ctx.exception), 'unknown_combo_marked_compatible')

    def test_default_compatible_set_rejects_unknown_keys(self):
        _, matrix, _ = _bundle()
        matrix['compatible_by_default'] = [{
            'os': None,
            'arch': None,
            'cli_binary_hash': None,
            'daemon_version': None,
            'protocol': 20,
            'schema_hash': None,
        }]
        with self.assertRaises(EvidenceError) as ctx:
            _check(matrix=matrix)
        self.assertEqual(str(ctx.exception), 'unknown_combo_marked_compatible')

    def test_omitted_compatible_with_unknown_key_is_rejected(self):
        _, matrix, _ = _bundle()
        del matrix['rows'][0]['compatible']
        with self.assertRaises(EvidenceError) as ctx:
            _check(matrix=matrix)
        self.assertEqual(str(ctx.exception), 'unknown_combo_marked_compatible')

    def test_default_compatible_set_rejects_runtime_unknown(self):
        _, matrix, _ = _bundle()
        matrix['compatible_by_default'] = [{
            'os': 'windows',
            'arch': 'x64',
            'cli_binary_hash': 'a' * 64,
            'daemon_version': '0.8.2',
            'protocol': 20,
            'schema_hash': 'b' * 64,
        }]
        with self.assertRaises(EvidenceError) as ctx:
            _check(matrix=matrix)
        self.assertEqual(str(ctx.exception), 'unknown_combo_marked_compatible')

    def test_api_protocol_must_remain_20(self):
        baseline, _, _ = _bundle()
        baseline['herdr']['api_protocol'] = 21
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'api_protocol_mismatch')

    def test_bool_protocol_is_rejected(self):
        baseline, _, _ = _bundle()
        baseline['herdr']['api_protocol'] = True
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'api_protocol_mismatch')

    def test_default_write_capability_must_remain_false(self):
        baseline, _, _ = _bundle()
        baseline['default_write_capability'] = True
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'default_write_capability_not_false')

    def test_runtime_hash_without_capture_is_rejected(self):
        baseline, _, _ = _bundle()
        windows = _record(baseline, 'windows_local')
        windows['hashes']['runtime_binary_sha256'] = 'a' * 64
        windows['hashes']['runtime_schema_sha256'] = None
        windows['attachments'] = [
            'evidence/runtime/windows-endpoint-matrix.blocked.json',
        ]
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'missing_runtime_capture')

    def test_template_with_exit_code_zero_is_rejected(self):
        _, _, documents = _bundle()
        documents['evidence/runtime/windows-runtime.template.json']['exit_code'] = 0
        with self.assertRaises(EvidenceError) as ctx:
            _check(documents=documents)
        self.assertEqual(str(ctx.exception), 'template_used_as_runtime_proof')

    def test_template_result_passed_is_rejected(self):
        _, _, documents = _bundle()
        documents['evidence/runtime/windows-runtime.template.json']['result'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            _check(documents=documents)
        self.assertEqual(str(ctx.exception), 'template_used_as_runtime_proof')

    def test_blocked_capture_cannot_look_like_success(self):
        _, _, documents = _bundle()
        cap = documents['evidence/runtime/remote-runtime.not-run.json']
        cap['exit_code'] = 0
        cap['stdout_sha256'] = 'a' * 64
        cap['stderr_sha256'] = 'b' * 64
        with self.assertRaises(EvidenceError) as ctx:
            _check(documents=documents)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_named_pipe_acl_success_without_runtime_is_rejected(self):
        baseline, _, _ = _bundle()
        baseline['runtime_verification']['named_pipe_acl'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_windows_endpoint_success_without_runtime_is_rejected(self):
        baseline, _, _ = _bundle()
        baseline['runtime_verification']['windows_endpoint'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_ime_success_is_rejected(self):
        baseline, _, _ = _bundle()
        baseline['runtime_verification']['ime'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_source_record_requires_field_sources(self):
        baseline, _, _ = _bundle()
        _record(baseline, 'source_inspection').pop('field_sources')
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_capture_stdout_cannot_use_git_blob(self):
        baseline, _, documents = _bundle()
        blob = baseline['herdr']['source_blobs']['src/client/mod.rs']
        documents['evidence/runtime/windows-runtime.capture.json']['stdout_sha256'] = blob
        with self.assertRaises(EvidenceError) as ctx:
            _check(documents=documents)
        self.assertEqual(str(ctx.exception), 'hash_conflation')

    def test_conflating_git_blob_into_distribution_binary_is_rejected(self):
        baseline, _, _ = _bundle()
        blob = baseline['herdr']['source_blobs']['src/client/mod.rs']
        baseline['herdr']['distribution_binary_sha256'] = blob
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'hash_conflation')

    def test_structural_validation_never_sets_windows_verified(self):
        baseline, matrix, documents = _bundle()
        fake_hash = 'a' * 64
        other_hash = 'b' * 64
        windows_cap = copy.deepcopy(documents['evidence/runtime/windows-runtime.capture.json'])
        windows_cap.update({
            'capture_id': 'windows-runtime-fake-success',
            'evidence_level': 'isolated_windows_runtime',
            'result': 'passed',
            'exit_code': 0,
            'stdout_sha256': fake_hash,
            'stderr_sha256': other_hash,
            'command_redacted': ['herdr --version'],
            'protocol': 20,
            'matches_reference_header': True,
            'herdr_executed': True,
        })
        for key in ('blocked_category', 'path_inspection_only',
                    'herdr_binary_on_path', 'herdr_path_redacted'):
            windows_cap.pop(key, None)
        remote_cap = copy.deepcopy(documents['evidence/runtime/remote-runtime.not-run.json'])
        remote_cap.update({
            'capture_id': 'remote-runtime-fake-success',
            'evidence_level': 'remote_runtime',
            'result': 'passed',
            'exit_code': 0,
            'stdout_sha256': other_hash,
            'stderr_sha256': fake_hash,
            'command_redacted': ['herdr --version'],
            'herdr_executed': True,
        })
        documents['evidence/runtime/windows-runtime.capture.json'] = windows_cap
        documents['evidence/runtime/remote-runtime.not-run.json'] = remote_cap
        windows = _record(baseline, 'windows_local')
        remote = _record(baseline, 'remote_linux')
        windows['result'] = 'passed'
        windows['evidence_level'] = 'isolated_windows_runtime'
        windows['runtime_protocol'] = 20
        windows['matches_reference_header'] = True
        windows['hashes']['runtime_binary_sha256'] = fake_hash
        windows.pop('blocked_category', None)
        remote['result'] = 'passed'
        remote['evidence_level'] = 'remote_runtime'
        remote['hashes']['runtime_binary_sha256'] = other_hash
        baseline['runtime_verification']['windows_local'] = 'passed'
        baseline['runtime_verification']['remote_linux'] = 'passed'
        result = _check(baseline=baseline, matrix=matrix, documents=documents)
        self.assertEqual(result['evidence_validation'], 'passed')
        self.assertFalse(result['windows_verified'])

    def test_protocol_22_cannot_be_marked_passed(self):
        baseline, _, _ = _bundle()
        windows = _record(baseline, 'windows_local')
        windows['result'] = 'passed'
        baseline['runtime_verification']['windows_local'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'protocol_mismatch_not_compatible')

    def test_protocol_22_cannot_enter_compatible_by_default(self):
        _, matrix, _ = _bundle()
        matrix['compatible_by_default'] = [{
            'os': 'windows',
            'arch': 'x64',
            'cli_binary_hash': 'd' * 64,
            'daemon_version': '0.9.0-preview',
            'protocol': 22,
            'schema_hash': 'e' * 64,
        }]
        with self.assertRaises(EvidenceError) as ctx:
            _check(matrix=matrix)
        self.assertEqual(str(ctx.exception), 'protocol_mismatch_not_compatible')

    def test_protocol_22_row_cannot_be_marked_compatible(self):
        _, matrix, _ = _bundle()
        matrix['rows'][1]['compatible'] = True
        with self.assertRaises(EvidenceError) as ctx:
            _check(matrix=matrix)
        self.assertEqual(str(ctx.exception), 'protocol_mismatch_not_compatible')

    def test_shipped_preview_stays_out_of_default_compatible_set(self):
        matrix = _load('evidence/version-support-matrix.json')
        self.assertEqual(matrix['compatible_by_default'], [])
        self.assertIs(matrix['rows'][1]['compatible'], False)
        self.assertEqual(matrix['rows'][1]['key']['protocol'], 22)
        self.assertEqual(matrix['channel_diff'][1]['channel'], 'preview')
        self.assertEqual(matrix['channel_diff'][1]['status'],
                         'recorded_not_in_default_compatible_set')
        result = validate_evidence(ROOT)
        self.assertFalse(result['windows_verified'])
        self.assertEqual(result['runtime_windows'], 'recorded')

    def test_matches_reference_header_cannot_be_true_for_protocol_22(self):
        baseline, _, documents = _bundle()
        documents['evidence/runtime/windows-runtime.capture.json'][
            'matches_reference_header'] = True
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline, documents=documents)
        self.assertEqual(str(ctx.exception), 'protocol_mismatch_not_compatible')

    def test_preview_channel_cannot_enter_compatible_by_default(self):
        _, matrix, _ = _bundle()
        matrix['compatible_by_default'] = [{
            'os': 'windows',
            'arch': 'x64',
            'cli_binary_hash': 'd' * 64,
            'daemon_version': '0.9.0-preview',
            'protocol': 20,
            'schema_hash': 'e' * 64,
            'channel': 'preview',
        }]
        with self.assertRaises(EvidenceError) as ctx:
            _check(matrix=matrix)
        self.assertEqual(str(ctx.exception), 'unknown_combo_marked_compatible')

    def test_named_pipe_acl_recorded_is_rejected(self):
        baseline, _, _ = _bundle()
        baseline['runtime_verification']['named_pipe_acl'] = 'recorded'
        with self.assertRaises(EvidenceError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_schema_export_shipped_is_rejected(self):
        _, _, documents = _bundle()
        documents['evidence/runtime/windows-runtime.capture.json'][
            'schema_export_shipped'] = True
        with self.assertRaises(EvidenceError) as ctx:
            _check(documents=documents)
        self.assertEqual(str(ctx.exception), 'payload_not_redacted')

    def test_snapshot_body_in_capture_is_rejected(self):
        _, _, documents = _bundle()
        documents['evidence/runtime/windows-runtime.capture.json'][
            'commands']['snapshot']['result'] = {'id': 1}
        with self.assertRaises(EvidenceError) as ctx:
            _check(documents=documents)
        self.assertEqual(str(ctx.exception), 'payload_not_redacted')

    def test_schema_dump_in_capture_is_rejected(self):
        _, _, documents = _bundle()
        documents['evidence/runtime/windows-runtime.capture.json'][
            'commands']['schema']['methods'] = [{'name': 'x'}]
        with self.assertRaises(EvidenceError) as ctx:
            _check(documents=documents)
        self.assertEqual(str(ctx.exception), 'payload_not_redacted')

    def test_shipped_runtime_omits_schema_snapshot_and_keeps_acl_blocked(self):
        capture = _load('evidence/runtime/windows-runtime.capture.json')
        baseline = _load('evidence/compatibility-baseline.json')
        self.assertIs(capture['schema_export_shipped'], False)
        self.assertIs(capture['snapshot_body_shipped'], False)
        self.assertNotIn('bytes', capture.get('commands', {}).get('schema', {}))
        self.assertNotIn('result', capture.get('commands', {}).get('snapshot', {}))
        self.assertEqual(baseline['runtime_verification']['named_pipe_acl'], 'blocked')
        self.assertEqual(baseline['runtime_verification']['ime'], 'blocked')
        self.assertEqual(baseline['runtime_verification']['remote_linux'], 'not_run')
        self.assertFalse(validate_evidence(ROOT)['windows_verified'])


if __name__ == '__main__':
    unittest.main()
