from __future__ import annotations

import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.renderer import (
    BLOCKED_CATEGORY,
    CAPTURE_REL,
    FIXTURE_REL,
    L3_UNTESTED,
    MATRIX_SCENARIOS,
    RendererByteWindow,
    RendererError,
    Utf8ChunkAssembler,
    check_renderer_matrix,
    evaluate_composition,
    evaluate_input_policy,
    evaluate_web_message,
    validate_renderer_matrix,
)
from herddesk_g0.evidence import EvidenceError, check_evidence, validate_evidence
from herddesk_g0.protocol import ProtocolError
import validate_repository as repository


def _load(rel: str) -> dict:
    return json.loads((ROOT / rel).read_text(encoding='utf-8'))


def _bundle():
    fixture = _load(FIXTURE_REL)
    capture = _load(CAPTURE_REL)
    baseline = _load('evidence/compatibility-baseline.json')
    lease = _load('evidence/runtime/windows-terminal-lease.blocked.json')
    endpoint = _load('evidence/runtime/windows-endpoint-matrix.blocked.json')
    return fixture, capture, baseline, lease, endpoint


def _check(fixture=None, capture=None, baseline=None, lease=None, endpoint=None):
    shipped = _bundle()
    return check_renderer_matrix(
        shipped[0] if fixture is None else fixture,
        shipped[1] if capture is None else capture,
        shipped[2] if baseline is None else baseline,
        shipped[3] if lease is None else lease,
        shipped[4] if endpoint is None else endpoint,
    )


def _evidence_bundle():
    baseline = _load('evidence/compatibility-baseline.json')
    matrix = _load('evidence/version-support-matrix.json')
    documents = {}
    for path in sorted((ROOT / 'evidence' / 'runtime').glob('*.json')):
        documents['evidence/runtime/' + path.name] = json.loads(
            path.read_text(encoding='utf-8'))
    return baseline, matrix, documents


class RendererMatrixTests(unittest.TestCase):
    def test_shipped_matrix_is_simulation_and_not_ac08_ac09(self):
        result = validate_renderer_matrix(ROOT)
        self.assertEqual(result['renderer_validation'], 'passed')
        self.assertFalse(result['windows_verified'])
        self.assertFalse(result['ac08_passed'])
        self.assertFalse(result['ac09_passed'])
        self.assertEqual(result['live_matrix'], 'blocked')
        self.assertEqual(result['blocked_category'], BLOCKED_CATEGORY)
        self.assertTrue(result['simulation'])
        self.assertFalse(result['winui_executed'])
        self.assertFalse(result['ime_executed'])
        self.assertFalse(result['native_candidate_run'])
        self.assertEqual(result['rows'], 18)
        repo = repository.validate()
        self.assertEqual(repo['structural_validation'], 'passed')
        self.assertEqual(repo['renderer_validation'], 'passed')
        self.assertFalse(repo['windows_verified'])
        self.assertFalse(repo['ac08_passed'])
        self.assertFalse(repo['ac09_passed'])
        evidence = validate_evidence(ROOT)
        self.assertFalse(evidence['windows_verified'])

    def test_shipped_fixture_has_eighteen_blocked_rows(self):
        fixture = _load(FIXTURE_REL)
        self.assertIs(fixture['simulation'], True)
        self.assertIs(fixture['runtime_pass'], False)
        self.assertIs(fixture['windows_verified'], False)
        self.assertIs(fixture['ac08_passed'], False)
        self.assertIs(fixture['ac09_passed'], False)
        self.assertIs(fixture['herdr_executed'], False)
        self.assertIs(fixture['winui_executed'], False)
        self.assertIs(fixture['webview2_executed'], False)
        self.assertIs(fixture['ime_executed'], False)
        self.assertIs(fixture['native_candidate_run'], False)
        self.assertEqual(fixture['fixture_origin'], 'synthetic')
        self.assertEqual(fixture['all_live_checks'], 'blocked')
        self.assertEqual(fixture['blocked_category'], BLOCKED_CATEGORY)
        scenarios = [item['scenario'] for item in fixture['cases']]
        self.assertEqual(scenarios, list(MATRIX_SCENARIOS))
        self.assertEqual(len(scenarios), 18)

    def test_shipped_capture_is_blocked_ime_desktop(self):
        capture = _load(CAPTURE_REL)
        baseline = _load('evidence/compatibility-baseline.json')
        self.assertEqual(capture['blocked_category'], BLOCKED_CATEGORY)
        self.assertIs(capture['herdr_executed'], False)
        self.assertIs(capture['winui_executed'], False)
        self.assertIs(capture['ime_executed'], False)
        self.assertIs(capture['native_candidate_run'], False)
        self.assertIs(capture['ac08_passed'], False)
        self.assertIs(capture['ac09_passed'], False)
        self.assertIs(capture['windows_verified'], False)
        self.assertEqual(set(capture['untested_l3']), set(L3_UNTESTED))
        self.assertEqual(baseline['runtime_verification']['ime'], 'blocked')
        matches = [item for item in baseline['records'] if item['environment'] == 'ime']
        self.assertEqual(len(matches), 1)
        self.assertEqual(matches[0]['blocked_category'], BLOCKED_CATEGORY)

    def test_renderer_record_is_independent_of_lease_and_endpoint(self):
        capture = _load(CAPTURE_REL)
        lease = _load('evidence/runtime/windows-terminal-lease.blocked.json')
        endpoint = _load('evidence/runtime/windows-endpoint-matrix.blocked.json')
        self.assertNotEqual(capture['capture_id'], lease['capture_id'])
        self.assertNotEqual(capture['capture_id'], endpoint['capture_id'])
        self.assertNotEqual(capture['kind'], lease['kind'])
        self.assertNotEqual(capture['kind'], endpoint['kind'])
        self.assertNotEqual(capture['blocked_category'], lease['blocked_category'])
        self.assertNotEqual(capture['blocked_category'], endpoint['blocked_category'])
        self.assertNotIn('named_pipe_connected', capture)

    def test_utf8_split_has_no_replacement_or_duplication(self):
        assembler = Utf8ChunkAssembler()
        assembler.append(bytes.fromhex('e4'))
        self.assertEqual(assembler.text, '')
        self.assertTrue(assembler.held_incomplete)
        self.assertNotIn('\ufffd', assembler.text)
        assembler.append(bytes.fromhex('bda0e5a5bd'))
        self.assertEqual(assembler.text, '你好')
        self.assertFalse(assembler.held_incomplete)
        self.assertNotIn('\ufffd', assembler.text)
        self.assertNotEqual(assembler.text, '你好你好')

    def test_naive_getstring_would_replace_but_assembler_does_not(self):
        lead = bytes.fromhex('e4')
        naive = lead.decode('utf-8', errors='replace')
        self.assertIn('\ufffd', naive)
        assembler = Utf8ChunkAssembler()
        assembler.append(lead)
        self.assertNotIn('\ufffd', assembler.text)

    def test_preedit_is_not_sent(self):
        result = evaluate_composition(
            'preedit_update', origin='committed_text', control_verified=True)
        self.assertFalse(result['allowed'])
        self.assertEqual(result['code'], 'preedit_not_sent')

    def test_observe_user_key_and_emulator_reply_denied(self):
        observe = evaluate_input_policy(
            access='observing', control_verified=False, origin='user_key',
            payload_bytes=1, context_epoch=1, input_epoch=1)
        self.assertFalse(observe['allowed'])
        self.assertEqual(observe['code'], 'control_not_verified')
        reply = evaluate_input_policy(
            access='controlling', control_verified=True, origin='emulator_reply',
            payload_bytes=1, context_epoch=1, input_epoch=1)
        self.assertFalse(reply['allowed'])
        self.assertEqual(reply['code'], 'input_origin_denied')

    def test_web_allowlist_rejects_unknown_oversize_wrong_epoch(self):
        context = {
            'epoch': 1, 'pane': 'p1',
            'access': 'controlling', 'control_verified': True,
        }
        unknown = evaluate_web_message({
            'type': 'host.exec', 'direction': 'renderer_to_host', 'version': 1,
            'epoch': 1, 'pane': 'p1', 'payload_bytes': 1,
        }, context)
        self.assertEqual(unknown['code'], 'unknown_web_message_type')
        oversize = evaluate_web_message({
            'type': 'input.user_key', 'direction': 'renderer_to_host', 'version': 1,
            'epoch': 1, 'pane': 'p1', 'payload_bytes': 65537,
        }, context)
        self.assertEqual(oversize['code'], 'web_message_bytes_limit')
        stale = evaluate_web_message({
            'type': 'input.committed_text', 'direction': 'renderer_to_host',
            'version': 1, 'epoch': 2, 'pane': 'p1', 'payload_bytes': 3,
        }, context)
        self.assertEqual(stale['code'], 'stale_epoch')

    def test_ack_matches_oldest_frame_and_reset_rejects_lower_epoch(self):
        window = RendererByteWindow(2, 8, 2)
        self.assertTrue(window.try_enqueue(2, 4)['accepted'])
        self.assertTrue(window.try_enqueue(2, 3)['accepted'])
        mismatch = window.acknowledge_parse_consumed(2, 3)
        self.assertFalse(mismatch['accepted'])
        self.assertEqual(mismatch['code'], 'queue_ack_mismatch')
        self.assertEqual(window.in_flight_bytes, 7)
        consumed = window.acknowledge_parse_consumed(2, 4)
        self.assertTrue(consumed['accepted'])
        self.assertFalse(consumed['parse_consumed_is_presented'])
        self.assertEqual(window.in_flight_bytes, 3)
        with self.assertRaises(ProtocolError) as ctx:
            window.reset(1)
        self.assertEqual(str(ctx.exception), 'stale_epoch')
        self.assertEqual(window.in_flight_bytes, 3)
        window.reset(2)
        self.assertEqual(window.in_flight_bytes, 0)
        self.assertEqual(window.state, 'ready')

    def test_ac08_claim_is_rejected(self):
        fixture, _, _, _, _ = _bundle()
        fixture['ac08_passed'] = True
        with self.assertRaises(RendererError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'ac08_ac09_claimed_passed')

    def test_ac09_claim_is_rejected(self):
        fixture, _, _, _, _ = _bundle()
        fixture['ac09_passed'] = True
        with self.assertRaises(RendererError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'ac08_ac09_claimed_passed')

    def test_ime_executed_claim_is_rejected(self):
        _, capture, _, _, _ = _bundle()
        capture['ime_executed'] = True
        with self.assertRaises(RendererError) as ctx:
            _check(capture=capture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_live_success_is_rejected(self):
        fixture, _, _, _, _ = _bundle()
        fixture['cases'][0]['live_result'] = 'passed'
        with self.assertRaises(RendererError) as ctx:
            _check(fixture=fixture)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_ime_success_in_baseline_is_rejected(self):
        _, _, baseline, _, _ = _bundle()
        baseline['runtime_verification']['ime'] = 'passed'
        with self.assertRaises(RendererError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')
        baseline, matrix, documents = _evidence_bundle()
        baseline['runtime_verification']['ime'] = 'passed'
        with self.assertRaises(EvidenceError) as ctx:
            check_evidence(baseline, matrix, documents)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_decision_document_exists(self):
        text = (ROOT / 'docs/spikes/renderer-decision.md').read_text(encoding='utf-8')
        self.assertIn('WebView2', text)
        self.assertIn('UNVERIFIED', text)
        self.assertIn(BLOCKED_CATEGORY, text)
        self.assertIn('not promoted', text.lower())
        self.assertIn('Not product AC08/AC09 pass', text)
        self.assertIn('Do not treat this document as AC08 pass', text)
        self.assertIn('HD-015 owns product AC08/AC09 roll-up', text)
        self.assertIn('Source review is not L3 IME evidence', text)
        self.assertIn('L1 host PASS is not WebView2', text)


if __name__ == '__main__':
    unittest.main()
