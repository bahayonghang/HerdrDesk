from __future__ import annotations

import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.adr import (
    JSON_REL,
    MARKDOWN_REL,
    REQUIRED_ADR_IDS,
    REQUIRED_GATES,
    REQUIRED_SUBCONTRACTS,
    AdrError,
    check_adr_baseline,
    validate_adr_baseline,
)
import validate_repository as repository


def _load(rel: str):
    path = ROOT / rel
    if rel.endswith('.md'):
        return path.read_text(encoding='utf-8')
    return json.loads(path.read_text(encoding='utf-8'))


def _bundle():
    return (
        _load(JSON_REL),
        _load(MARKDOWN_REL),
        _load('implementation/status.json'),
        _load('planning/acceptance.json'),
        _load('evidence/compatibility-baseline.json'),
    )


def _agents(text=None):
    if text is not None:
        return text
    return (ROOT / 'AGENTS.md').read_text(encoding='utf-8')


def _check(document=None, markdown=None, status=None, acceptance=None, baseline=None,
           agents=None):
    shipped = _bundle()
    return check_adr_baseline(
        shipped[0] if document is None else document,
        shipped[1] if markdown is None else markdown,
        status=shipped[2] if status is None else status,
        acceptance=shipped[3] if acceptance is None else acceptance,
        baseline=shipped[4] if baseline is None else baseline,
        agents=_agents(agents),
        root=ROOT,
    )


def _gate(document: dict, gate_id: str) -> dict:
    matches = [item for item in document['gates'] if item['id'] == gate_id]
    if len(matches) != 1:
        raise AssertionError('missing shipped gate ' + gate_id)
    return matches[0]


def _adr(document: dict, adr_id: str) -> dict:
    matches = [item for item in document['adrs'] if item['id'] == adr_id]
    if len(matches) != 1:
        raise AssertionError('missing shipped ADR ' + adr_id)
    return matches[0]


def _ac44(acceptance: dict) -> dict:
    for item in acceptance['criteria']:
        if item['id'] == 'AC44':
            return item
    raise AssertionError('missing AC44')


class ApprovedBaselineTests(unittest.TestCase):
    def test_shipped_baseline_passes_and_does_not_claim_ac44_or_g0(self):
        result = validate_adr_baseline(ROOT)
        self.assertEqual(result['adr_validation'], 'passed')
        self.assertFalse(result['ac44_passed'])
        self.assertFalse(result['windows_verified'])
        self.assertFalse(result['g0_passed'])
        self.assertEqual(result['r5_phase_rule_sync'], 'not_executed')
        self.assertEqual(result['adr_count'], 7)
        self.assertEqual(result['subcontract_count'], 5)
        self.assertEqual(result['gate_count'], 16)
        repo = repository.validate()
        self.assertEqual(repo['structural_validation'], 'passed')
        self.assertEqual(repo['adr_validation'], 'passed')
        self.assertFalse(repo['windows_verified'])
        self.assertFalse(repo['ac44_passed'])
        self.assertFalse(repo['g0_passed'])

    def test_shipped_numbering_and_children_match_plan(self):
        document, markdown, status, acceptance, _ = _bundle()
        self.assertEqual(
            [item['id'] for item in document['adrs']],
            list(REQUIRED_ADR_IDS),
        )
        self.assertEqual(
            [item['id'] for item in document['subcontracts']],
            list(REQUIRED_SUBCONTRACTS),
        )
        self.assertEqual(
            [item['id'] for item in document['gates']],
            list(REQUIRED_GATES),
        )
        self.assertEqual(document['source_numbering'], 'docs/plan/docs/04_技术选型与ADR.md')
        self.assertEqual(document['ac44_child']['AC44-C1'], 'recorded')
        self.assertEqual(document['ac44_child']['AC44-C2'], 'recorded')
        self.assertEqual(document['ac44_child']['AC44'], 'not_run')
        self.assertEqual(document['r5_phase_rule_sync'], 'not_executed')
        self.assertEqual(status['phase_gate'], 'not_passed')
        self.assertEqual(_ac44(acceptance)['status'], 'not_run')
        self.assertIn('ADR-0001', markdown)
        for adr_id in REQUIRED_ADR_IDS:
            self.assertIn(adr_id, markdown)
        _check()

    def test_ac44_passed_claim_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        document['ac44_passed'] = True
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'ac44_claimed_passed')

    def test_g0_passed_claim_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        document['g0_passed'] = True
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'g0_claimed_passed')

    def test_phase_gate_passed_claim_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        document['phase_gate'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'g0_claimed_passed')

    def test_status_phase_gate_passed_is_rejected(self):
        _, _, status, _, _ = _bundle()
        status = copy.deepcopy(status)
        status['phase_gate'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(status=status)
        self.assertEqual(str(ctx.exception), 'g0_claimed_passed')

    def test_acceptance_ac44_passed_is_rejected(self):
        _, _, _, acceptance, _ = _bundle()
        acceptance = copy.deepcopy(acceptance)
        _ac44(acceptance)['status'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(acceptance=acceptance)
        self.assertEqual(str(ctx.exception), 'ac44_claimed_passed')

    def test_child_ac44_passed_claim_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        document['ac44_child']['AC44-C1'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'ac44_claimed_passed')

    def test_final_ac44_child_passed_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        document['ac44_child']['AC44'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'ac44_claimed_passed')

    def test_r5_executed_claim_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        document['r5_phase_rule_sync'] = 'executed'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'r5_claimed_executed')

    def test_blocked_gate_passed_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _gate(document, 'hd001-windows-runtime')['result'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'blocked_treated_as_passed')

    def test_unknown_gate_passed_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _gate(document, 'hd001-remote-runtime')['result'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'unknown_treated_as_passed')

    def test_blocked_gate_claim_passed_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _gate(document, 'hd005-ime-desktop')['claim_passed'] = True
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'blocked_treated_as_passed')

    def test_confirmed_live_pass_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _gate(document, 'hd003-endpoint-l1')['live_result'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_adr_runtime_verified_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _adr(document, 'ADR-002')['runtime_verified'] = True
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_adr_adoption_passed_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _adr(document, 'ADR-006')['adoption'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'g0_claimed_passed')

    def test_parallel_adr_number_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _adr(document, 'ADR-007')['id'] = 'ADR-008'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'parallel_numbering')

    def test_markdown_parallel_adr_is_rejected(self):
        _, markdown, _, _, _ = _bundle()
        markdown = markdown + '\nADR-008 extra decision.\n'
        with self.assertRaises(AdrError) as ctx:
            _check(markdown=markdown)
        self.assertEqual(str(ctx.exception), 'parallel_numbering')

    def test_missing_degrade_path_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _gate(document, 'hd003-windows-endpoint')['degrade'] = ''
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'degrade_path_missing')

    def test_markdown_missing_disclaimer_is_rejected(self):
        _, markdown, _, _, _ = _bundle()
        markdown = markdown.replace('G0 is not passed.', 'G0 freeze complete.')
        with self.assertRaises(AdrError) as ctx:
            _check(markdown=markdown)
        self.assertEqual(str(ctx.exception), 'missing_markdown_anchor')

    def test_baseline_ime_pass_is_rejected(self):
        _, _, _, _, baseline = _bundle()
        baseline = copy.deepcopy(baseline)
        baseline['runtime_verification']['ime'] = 'passed'
        with self.assertRaises(AdrError) as ctx:
            _check(baseline=baseline)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_windows_verified_claim_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        document['windows_verified'] = True
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'evidence_level_promotion')

    def test_missing_evidence_path_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _adr(document, 'ADR-001')['input_evidence'] = [
            'docs/missing-hd006-evidence.md',
        ]
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'missing_evidence_path')

    def test_agents_g0_prohibition_removed_is_rejected(self):
        agents = _agents().replace(
            'The product phase is **G0**.',
            'The product phase is P1.',
        )
        with self.assertRaises(AdrError) as ctx:
            _check(agents=agents)
        self.assertEqual(str(ctx.exception), 'r5_claimed_executed')

    def test_required_deny_caller_missing_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        _adr(document, 'ADR-003')['deny_callers'] = [
            'json_rpc_on_terminal_stdio',
        ]
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_child_ac44_c1_not_run_is_rejected(self):
        document, _, _, _, _ = _bundle()
        document = copy.deepcopy(document)
        document['ac44_child']['AC44-C1'] = 'not_run'
        with self.assertRaises(AdrError) as ctx:
            _check(document=document)
        self.assertEqual(str(ctx.exception), 'missing_record_field')

    def test_markdown_missing_plane_rule_is_rejected(self):
        _, markdown, _, _, _ = _bundle()
        markdown = markdown.replace(
            'Do not send JSON RPC to the herdr binary client socket.',
            'JSON RPC may share the terminal socket.',
        )
        with self.assertRaises(AdrError) as ctx:
            _check(markdown=markdown)
        self.assertEqual(str(ctx.exception), 'missing_markdown_anchor')


if __name__ == '__main__':
    unittest.main()
