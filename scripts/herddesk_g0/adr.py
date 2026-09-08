"""Approved-baseline rules for HD-006.

Structural validation is not AC44 passed and never sets windows_verified
or phase_gate. Blocked and unknown ledger rows cannot be recorded as
passed. R5 phase-rule sync stays not_executed while HD-001 through HD-005
target evidence is blocked or unknown. Cited evidence paths must exist.
AGENTS.md G0 prohibitions stay while R5 is not executed.
"""
from __future__ import annotations

import json
from pathlib import Path
import re
from typing import Any


class AdrError(ValueError):
    """Stable ADR-rule code; the message is the code only."""


JSON_REL = 'docs/adr/approved-baseline.json'
MARKDOWN_REL = 'docs/adr/approved-baseline.md'
BOOTSTRAP_REL = 'docs/adr/0001-g0-bootstrap.md'
STATUS_REL = 'implementation/status.json'
ACCEPTANCE_REL = 'planning/acceptance.json'
BASELINE_REL = 'evidence/compatibility-baseline.json'
AGENTS_REL = 'AGENTS.md'

REQUIRED_ADR_IDS = (
    'ADR-001', 'ADR-002', 'ADR-003', 'ADR-004',
    'ADR-005', 'ADR-006', 'ADR-007',
)
REQUIRED_SUBCONTRACTS = (
    'dependency_admission',
    'renderer_ipc',
    'endpoint_discovery',
    'logging_redaction',
    'filebridge_capability',
)
SUBCONTRACT_PARENTS = {
    'dependency_admission': 'ADR-001',
    'renderer_ipc': 'ADR-002',
    'endpoint_discovery': 'ADR-003',
    'logging_redaction': 'ADR-006',
    'filebridge_capability': 'ADR-007',
}
REQUIRED_GATES = (
    'hd001-source-protocol',
    'hd001-windows-runtime',
    'hd001-named-pipe-acl',
    'hd001-remote-runtime',
    'hd001-runtime-hashes',
    'hd003-endpoint-l1',
    'hd003-windows-endpoint',
    'hd003-unc-and-env-guess',
    'hd004-lease-l1',
    'hd004-windows-lease',
    'hd004-control-inference',
    'hd004-unknown-control-signal',
    'hd005-renderer-l1',
    'hd005-ime-desktop',
    'hd005-native-raw-stream',
    'hd005-webview-origin-runtime',
)
GATE_TASKS = {
    'hd001-source-protocol': 'HD-001',
    'hd001-windows-runtime': 'HD-001',
    'hd001-named-pipe-acl': 'HD-001',
    'hd001-remote-runtime': 'HD-001',
    'hd001-runtime-hashes': 'HD-001',
    'hd003-endpoint-l1': 'HD-003',
    'hd003-windows-endpoint': 'HD-003',
    'hd003-unc-and-env-guess': 'HD-003',
    'hd004-lease-l1': 'HD-004',
    'hd004-windows-lease': 'HD-004',
    'hd004-control-inference': 'HD-004',
    'hd004-unknown-control-signal': 'HD-004',
    'hd005-renderer-l1': 'HD-005',
    'hd005-ime-desktop': 'HD-005',
    'hd005-native-raw-stream': 'HD-005',
    'hd005-webview-origin-runtime': 'HD-005',
}
GATE_PATHS = {
    'hd001-source-protocol': 'confirmed',
    'hd001-windows-runtime': 'blocked',
    'hd001-named-pipe-acl': 'blocked',
    'hd001-remote-runtime': 'unknown',
    'hd001-runtime-hashes': 'unknown',
    'hd003-endpoint-l1': 'confirmed',
    'hd003-windows-endpoint': 'blocked',
    'hd003-unc-and-env-guess': 'degrade',
    'hd004-lease-l1': 'confirmed',
    'hd004-windows-lease': 'blocked',
    'hd004-control-inference': 'degrade',
    'hd004-unknown-control-signal': 'unknown',
    'hd005-renderer-l1': 'confirmed',
    'hd005-ime-desktop': 'blocked',
    'hd005-native-raw-stream': 'unknown',
    'hd005-webview-origin-runtime': 'unknown',
}
GATE_BASELINE = {
    'hd001-windows-runtime': ('windows_local', 'blocked'),
    'hd001-named-pipe-acl': ('named_pipe_acl', 'blocked'),
    'hd001-remote-runtime': ('remote_linux', 'not_run'),
    'hd003-windows-endpoint': ('windows_endpoint', 'blocked'),
    'hd004-windows-lease': ('windows_terminal_lease', 'blocked'),
    'hd005-ime-desktop': ('ime', 'blocked'),
}

ADR_FIELDS = (
    'id', 'title', 'problem', 'decision', 'evidence_class',
    'input_evidence', 'input_tasks', 'allow_callers', 'deny_callers',
    'fail_state', 'rollback', 'owner_module', 'verification_task',
    'spec_update_trigger', 'adoption', 'runtime_verified',
)
SUBCONTRACT_FIELDS = (
    'id', 'parent_adr', 'title', 'child_ac', 'problem', 'decision',
    'evidence_class', 'input_evidence', 'allow_callers', 'deny_callers',
    'fail_state', 'rollback', 'owner_module', 'verification_task',
    'spec_update_trigger', 'adoption', 'runtime_verified',
)
GATE_FIELDS = (
    'id', 'task', 'capability', 'path', 'evidence_class', 'result',
    'live_result', 'blocked_category', 'degrade', 'claim_passed',
    'attachments', 'verification_task',
)
PATH_VALUES = frozenset({'confirmed', 'unknown', 'blocked', 'degrade'})
RESULT_FOR_PATH = {
    'confirmed': frozenset({'recorded'}),
    'unknown': frozenset({'unknown', 'not_run', 'unverified'}),
    'blocked': frozenset({'blocked'}),
    'degrade': frozenset({'degrade'}),
}
ADOPTION_VALUES = frozenset({'adopted_as_policy', 'deferred', 'blocked'})
EVIDENCE_CLASSES = frozenset({
    'source_inspection_only', 'synthetic', 'hosted_ci',
    'blocked', 'not_run', 'unverified',
})
SUCCESS_RESULTS = frozenset({
    'passed', 'verified', 'compatible', 'success', 'ok',
})
ALLOWED_ADR_NUMBERS = frozenset({
    '0001', '001', '002', '003', '004', '005', '006', '007',
})
ADR_TOKEN_RE = re.compile(r'\bADR-(\d+)\b')
REQUIRED_DENY = {
    'ADR-003': frozenset({
        'json_rpc_on_terminal_stdio',
        'json_rpc_on_herdr_binary_client_socket',
    }),
    'ADR-005': frozenset({
        'server_stop',
        'kill_daemon',
        'kill_agent',
    }),
    'ADR-006': frozenset({
        'control_verified_from_first_frame',
        'control_verified_from_process_alive',
        'control_verified_from_window_focus',
        'input_replay_after_disconnect',
    }),
    'renderer_ipc': frozenset({
        'host.exec',
        'generic_exec_proxy',
    }),
    'filebridge_capability': frozenset({
        'arbitrary_command',
        'arbitrary_file',
        'cross_target_reinterpret',
    }),
}
REQUIRED_ALLOW = {
    'ADR-003': frozenset({'herdr_terminal_session_stdio'}),
    'ADR-005': frozenset({'stop_owned_bridge_child'}),
    'ADR-006': frozenset({'observe_without_grant'}),
}
AGENTS_MARKERS = (
    'The product phase is **G0**.',
    'phase_gate=not_passed',
    'Do not mark G0 or AC01–AC48 as passed.',
    'Do not run live herdr writes',
)

G0_NOT_PASSED = 'G0 is not passed.'
AC44_NOT_PASSED = 'AC44 is not passed.'
R5_NOT_EXECUTED = 'R5 phase-rule sync is not executed.'
UNKNOWN_REMAINS = 'Unknown remains Unknown.'
BLOCKED_DEGRADE = (
    'A blocked capability stays observe or explicit configuration.'
)
NO_PARALLEL = (
    'This freeze keeps ADR-001 through ADR-007 from '
    'docs/plan/docs/04_技术选型与ADR.md. It does not create a parallel ADR series.'
)
PLANE_RULE = (
    'Do not send JSON RPC to the herdr binary client socket.'
)
OBSERVE_RULE = 'Default observe.'
NO_REPLAY_RULE = 'After disconnect, do not replay input.'
CHILD_PROCESS_RULE = (
    "Closing the GUI stops only this application's direct child processes."
)
FILEBRIDGE_RULE = 'No arbitrary command.'
MARKDOWN_DISCLAIMERS = (
    G0_NOT_PASSED,
    AC44_NOT_PASSED,
    R5_NOT_EXECUTED,
    UNKNOWN_REMAINS,
    BLOCKED_DEGRADE,
    NO_PARALLEL,
    PLANE_RULE,
    OBSERVE_RULE,
    NO_REPLAY_RULE,
    CHILD_PROCESS_RULE,
    FILEBRIDGE_RULE,
)


def validate_adr_baseline(root: Path) -> dict[str, Any]:
    root = Path(root)
    json_path = root / JSON_REL
    markdown_path = root / MARKDOWN_REL
    bootstrap_path = root / BOOTSTRAP_REL
    status_path = root / STATUS_REL
    acceptance_path = root / ACCEPTANCE_REL
    baseline_path = root / BASELINE_REL
    agents_path = root / AGENTS_REL
    if not json_path.is_file() or not markdown_path.is_file():
        raise AdrError('missing_record_field')
    if not bootstrap_path.is_file():
        raise AdrError('missing_record_field')
    if not status_path.is_file() or not acceptance_path.is_file():
        raise AdrError('missing_record_field')
    if not baseline_path.is_file():
        raise AdrError('missing_record_field')
    if not agents_path.is_file():
        raise AdrError('missing_record_field')
    document = json.loads(json_path.read_text(encoding='utf-8'))
    status = json.loads(status_path.read_text(encoding='utf-8'))
    acceptance = json.loads(acceptance_path.read_text(encoding='utf-8'))
    baseline = json.loads(baseline_path.read_text(encoding='utf-8'))
    markdown = markdown_path.read_text(encoding='utf-8')
    agents = agents_path.read_text(encoding='utf-8')
    return check_adr_baseline(
        document,
        markdown,
        status=status,
        acceptance=acceptance,
        baseline=baseline,
        agents=agents,
        root=root,
    )


def check_adr_baseline(
    document: dict[str, Any],
    markdown: str,
    *,
    status: dict[str, Any] | None = None,
    acceptance: dict[str, Any] | None = None,
    baseline: dict[str, Any] | None = None,
    agents: str | None = None,
    root: Path | None = None,
) -> dict[str, Any]:
    if not isinstance(document, dict) or not isinstance(markdown, str):
        raise AdrError('missing_record_field')
    _check_header(document)
    _check_markdown(document, markdown)
    _check_adrs(document)
    _check_subcontracts(document)
    _check_gates(document)
    if status is not None:
        _check_status(document, status)
    if acceptance is not None:
        _check_acceptance(document, acceptance)
    if baseline is not None:
        _check_evidence_baseline(document, baseline)
    if agents is not None:
        _check_agents(agents)
    if root is not None:
        _check_paths(document, Path(root))
    return {
        'adr_validation': 'passed',
        'ac44_passed': False,
        'windows_verified': False,
        'g0_passed': False,
        'r5_phase_rule_sync': 'not_executed',
        'adr_count': len(REQUIRED_ADR_IDS),
        'subcontract_count': len(REQUIRED_SUBCONTRACTS),
        'gate_count': len(REQUIRED_GATES),
    }


def _check_header(document: dict[str, Any]) -> None:
    if document.get('schema_version') != 1:
        raise AdrError('missing_record_field')
    if document.get('document_kind') != 'approved_baseline':
        raise AdrError('missing_record_field')
    if document.get('source_numbering') != 'docs/plan/docs/04_技术选型与ADR.md':
        raise AdrError('parallel_numbering')
    if document.get('markdown') != MARKDOWN_REL:
        raise AdrError('missing_record_field')
    if document.get('bootstrap_adr') != BOOTSTRAP_REL:
        raise AdrError('missing_record_field')
    if document.get('owner') != 'HD-006':
        raise AdrError('missing_record_field')
    if document.get('final_review_task') != 'HD-035':
        raise AdrError('missing_record_field')
    if document.get('phase') != 'G0':
        raise AdrError('missing_record_field')
    if document.get('phase_gate') != 'not_passed':
        raise AdrError('g0_claimed_passed')
    if document.get('g0_passed') is not False:
        raise AdrError('g0_claimed_passed')
    if document.get('ac44_passed') is not False:
        raise AdrError('ac44_claimed_passed')
    if document.get('ac44_status') != 'not_run':
        raise AdrError('ac44_claimed_passed')
    if document.get('windows_verified') is not False:
        raise AdrError('evidence_level_promotion')
    if document.get('r5_phase_rule_sync') != 'not_executed':
        raise AdrError('r5_claimed_executed')
    reason = document.get('r5_blocked_reason')
    if not isinstance(reason, str) or 'HD-001' not in reason:
        raise AdrError('missing_record_field')
    children = document.get('ac44_child')
    if not isinstance(children, dict):
        raise AdrError('missing_record_field')
    for key in ('AC44-C1', 'AC44-C2', 'AC44'):
        if key not in children:
            raise AdrError('missing_record_field')
    if children.get('AC44') != 'not_run':
        raise AdrError('ac44_claimed_passed')
    if children.get('AC44-C1') == 'passed' or children.get('AC44-C2') == 'passed':
        raise AdrError('ac44_claimed_passed')
    if children.get('AC44-C1') != 'recorded':
        raise AdrError('missing_record_field')
    if children.get('AC44-C2') != 'recorded':
        raise AdrError('missing_record_field')
    if _is_success(document.get('result')):
        raise AdrError('g0_claimed_passed')


def _check_markdown(document: dict[str, Any], markdown: str) -> None:
    if not markdown:
        raise AdrError('missing_markdown_anchor')
    for phrase in MARKDOWN_DISCLAIMERS:
        if phrase not in markdown:
            raise AdrError('missing_markdown_anchor')
    for adr_id in REQUIRED_ADR_IDS:
        if adr_id not in markdown:
            raise AdrError('missing_adr')
    for subcontract_id in REQUIRED_SUBCONTRACTS:
        if subcontract_id.replace('_', ' ') not in markdown.lower() and (
                subcontract_id not in markdown):
            title_ok = False
            for item in document.get('subcontracts') or []:
                if item.get('id') == subcontract_id and item.get('title') in markdown:
                    title_ok = True
                    break
            if not title_ok:
                raise AdrError('missing_markdown_anchor')
    for gate_id in REQUIRED_GATES:
        if gate_id not in markdown:
            raise AdrError('missing_ledger_row')
    for adr in document.get('adrs') or []:
        title = adr.get('title')
        if not isinstance(title, str) or title not in markdown:
            raise AdrError('missing_markdown_anchor')
    for match in ADR_TOKEN_RE.finditer(markdown):
        if match.group(1) not in ALLOWED_ADR_NUMBERS:
            raise AdrError('parallel_numbering')
    if 'AC44-C1' not in markdown or 'AC44-C2' not in markdown:
        raise AdrError('missing_markdown_anchor')


def _check_adrs(document: dict[str, Any]) -> None:
    adrs = document.get('adrs')
    if not isinstance(adrs, list) or len(adrs) != len(REQUIRED_ADR_IDS):
        raise AdrError('missing_adr')
    seen: list[str] = []
    for item in adrs:
        _check_record(item, ADR_FIELDS)
        adr_id = item['id']
        if adr_id not in REQUIRED_ADR_IDS or adr_id in seen:
            raise AdrError('missing_adr')
        seen.append(adr_id)
        _check_adoption(item)
        _check_callers(item)
        tasks = item.get('input_tasks')
        if not isinstance(tasks, list) or not tasks:
            raise AdrError('missing_record_field')
        for task in tasks:
            if not isinstance(task, str) or not task.startswith('HD-'):
                raise AdrError('missing_record_field')
        evidence = item.get('input_evidence')
        if not isinstance(evidence, list) or not evidence:
            raise AdrError('missing_record_field')
    if seen != list(REQUIRED_ADR_IDS):
        raise AdrError('missing_adr')


def _check_subcontracts(document: dict[str, Any]) -> None:
    items = document.get('subcontracts')
    if not isinstance(items, list) or len(items) != len(REQUIRED_SUBCONTRACTS):
        raise AdrError('missing_record_field')
    seen: list[str] = []
    for item in items:
        _check_record(item, SUBCONTRACT_FIELDS)
        sub_id = item['id']
        if sub_id not in REQUIRED_SUBCONTRACTS or sub_id in seen:
            raise AdrError('missing_record_field')
        seen.append(sub_id)
        if item.get('parent_adr') != SUBCONTRACT_PARENTS[sub_id]:
            raise AdrError('parallel_numbering')
        _check_adoption(item)
        _check_callers(item)
        if sub_id == 'renderer_ipc' and item.get('child_ac') != 'AC44-C1':
            raise AdrError('missing_record_field')
        if sub_id == 'filebridge_capability' and item.get('child_ac') != 'AC44-C2':
            raise AdrError('missing_record_field')
    if seen != list(REQUIRED_SUBCONTRACTS):
        raise AdrError('missing_record_field')


def _check_gates(document: dict[str, Any]) -> None:
    gates = document.get('gates')
    if not isinstance(gates, list) or len(gates) != len(REQUIRED_GATES):
        raise AdrError('missing_ledger_row')
    seen: list[str] = []
    blocked_or_unknown = False
    for item in gates:
        _check_record(item, GATE_FIELDS)
        gate_id = item['id']
        if gate_id not in REQUIRED_GATES or gate_id in seen:
            raise AdrError('missing_ledger_row')
        seen.append(gate_id)
        if item.get('task') != GATE_TASKS[gate_id]:
            raise AdrError('missing_ledger_row')
        path = item.get('path')
        if path != GATE_PATHS[gate_id]:
            raise AdrError('missing_ledger_row')
        if path not in PATH_VALUES:
            raise AdrError('missing_record_field')
        result = item.get('result')
        if result not in RESULT_FOR_PATH[path]:
            if path == 'blocked':
                raise AdrError('blocked_treated_as_passed')
            if path == 'unknown':
                raise AdrError('unknown_treated_as_passed')
            raise AdrError('evidence_level_promotion')
        if item.get('claim_passed') is not False:
            if path == 'blocked':
                raise AdrError('blocked_treated_as_passed')
            if path == 'unknown':
                raise AdrError('unknown_treated_as_passed')
            raise AdrError('g0_claimed_passed')
        if _is_success(result) or _is_success(item.get('live_result')):
            if path == 'blocked':
                raise AdrError('blocked_treated_as_passed')
            if path == 'unknown':
                raise AdrError('unknown_treated_as_passed')
            raise AdrError('evidence_level_promotion')
        degrade = item.get('degrade')
        if not isinstance(degrade, str) or not degrade.strip():
            raise AdrError('degrade_path_missing')
        if item.get('evidence_class') not in EVIDENCE_CLASSES:
            raise AdrError('missing_record_field')
        attachments = item.get('attachments')
        if not isinstance(attachments, list) or not attachments:
            raise AdrError('missing_record_field')
        if path in {'blocked', 'unknown'}:
            blocked_or_unknown = True
        if path == 'blocked' and not item.get('blocked_category'):
            raise AdrError('missing_record_field')
        if path == 'confirmed' and item.get('live_result') == 'passed':
            raise AdrError('evidence_level_promotion')
    if seen != list(REQUIRED_GATES):
        raise AdrError('missing_ledger_row')
    if blocked_or_unknown and document.get('r5_phase_rule_sync') != 'not_executed':
        raise AdrError('r5_claimed_executed')


def _check_record(record: Any, fields: tuple[str, ...]) -> None:
    if not isinstance(record, dict):
        raise AdrError('missing_record_field')
    for key in fields:
        if key not in record:
            raise AdrError('missing_record_field')
    adr_id = record.get('id')
    if isinstance(adr_id, str) and adr_id.startswith('ADR-'):
        number = adr_id.removeprefix('ADR-')
        if number not in ALLOWED_ADR_NUMBERS or adr_id not in REQUIRED_ADR_IDS:
            raise AdrError('parallel_numbering')


def _check_adoption(record: dict[str, Any]) -> None:
    adoption = record.get('adoption')
    if adoption not in ADOPTION_VALUES:
        if adoption in SUCCESS_RESULTS or adoption == 'passed':
            raise AdrError('g0_claimed_passed')
        raise AdrError('missing_record_field')
    if record.get('runtime_verified') is True:
        raise AdrError('evidence_level_promotion')
    if record.get('runtime_verified') is not False:
        raise AdrError('missing_record_field')
    if record.get('evidence_class') not in EVIDENCE_CLASSES:
        raise AdrError('missing_record_field')
    fail_state = record.get('fail_state')
    if not isinstance(fail_state, str) or not fail_state.strip():
        raise AdrError('missing_record_field')
    trigger = record.get('spec_update_trigger')
    if not isinstance(trigger, str) or not trigger.strip():
        raise AdrError('missing_record_field')
    owner = record.get('owner_module')
    if not isinstance(owner, str) or not owner.strip():
        raise AdrError('missing_record_field')
    task = record.get('verification_task')
    if not isinstance(task, str) or not task.startswith('HD-'):
        raise AdrError('missing_record_field')


def _check_callers(record: dict[str, Any]) -> None:
    allow = record.get('allow_callers')
    deny = record.get('deny_callers')
    if not isinstance(allow, list) or not allow:
        raise AdrError('missing_record_field')
    if not isinstance(deny, list) or not deny:
        raise AdrError('missing_record_field')
    for name in allow + deny:
        if not isinstance(name, str) or not name.strip():
            raise AdrError('missing_record_field')
    overlap = set(allow) & set(deny)
    if overlap:
        raise AdrError('missing_record_field')
    record_id = record.get('id')
    required_deny = REQUIRED_DENY.get(record_id)
    if required_deny and not required_deny.issubset(deny):
        raise AdrError('missing_record_field')
    required_allow = REQUIRED_ALLOW.get(record_id)
    if required_allow and not required_allow.issubset(allow):
        raise AdrError('missing_record_field')


def _check_status(document: dict[str, Any], status: dict[str, Any]) -> None:
    if not isinstance(status, dict):
        raise AdrError('missing_record_field')
    if status.get('phase_gate') != 'not_passed':
        raise AdrError('g0_claimed_passed')
    if status.get('phase') != 'G0':
        raise AdrError('g0_claimed_passed')
    if document.get('phase_gate') != status.get('phase_gate'):
        raise AdrError('g0_claimed_passed')
    verified = status.get('verified_acceptance_ids')
    if isinstance(verified, list) and 'AC44' in verified:
        raise AdrError('ac44_claimed_passed')


def _check_acceptance(document: dict[str, Any], acceptance: dict[str, Any]) -> None:
    if not isinstance(acceptance, dict):
        raise AdrError('missing_record_field')
    criteria = acceptance.get('criteria')
    if not isinstance(criteria, list):
        raise AdrError('missing_record_field')
    ac44 = None
    for item in criteria:
        if isinstance(item, dict) and item.get('id') == 'AC44':
            ac44 = item
            break
    if ac44 is None:
        raise AdrError('missing_record_field')
    if ac44.get('status') != 'not_run':
        raise AdrError('ac44_claimed_passed')
    if ac44.get('evidence') not in (None, '', []):
        raise AdrError('ac44_claimed_passed')
    if document.get('ac44_status') != 'not_run':
        raise AdrError('ac44_claimed_passed')


def _check_evidence_baseline(
    document: dict[str, Any],
    baseline: dict[str, Any],
) -> None:
    if not isinstance(baseline, dict):
        raise AdrError('missing_record_field')
    verification = baseline.get('runtime_verification')
    if not isinstance(verification, dict):
        raise AdrError('missing_record_field')
    if baseline.get('default_write_capability') is not False:
        raise AdrError('g0_claimed_passed')
    gates = {item['id']: item for item in document['gates']}
    for gate_id, (key, expected) in GATE_BASELINE.items():
        actual = verification.get(key)
        if actual != expected:
            raise AdrError('evidence_level_promotion')
        gate = gates[gate_id]
        if expected == 'blocked' and gate['path'] != 'blocked':
            raise AdrError('blocked_treated_as_passed')
        if expected == 'not_run' and gate['path'] != 'unknown':
            raise AdrError('unknown_treated_as_passed')
        if _is_success(actual):
            raise AdrError('evidence_level_promotion')


def _check_agents(agents: str) -> None:
    if not isinstance(agents, str) or not agents:
        raise AdrError('missing_record_field')
    for phrase in AGENTS_MARKERS:
        if phrase not in agents:
            raise AdrError('r5_claimed_executed')


def _check_paths(document: dict[str, Any], root: Path) -> None:
    rels: list[str] = []
    for group in ('adrs', 'subcontracts'):
        for item in document.get(group) or []:
            evidence = item.get('input_evidence')
            if isinstance(evidence, list):
                rels.extend(evidence)
    for item in document.get('gates') or []:
        attachments = item.get('attachments')
        if isinstance(attachments, list):
            rels.extend(attachments)
    for rel in rels:
        if not isinstance(rel, str) or not rel.strip():
            raise AdrError('missing_evidence_path')
        candidate = Path(rel)
        if candidate.is_absolute() or '..' in candidate.parts:
            raise AdrError('missing_evidence_path')
        if not (root / rel).exists():
            raise AdrError('missing_evidence_path')


def _is_success(value: Any) -> bool:
    if value is True:
        return True
    if isinstance(value, str) and value.lower() in SUCCESS_RESULTS:
        return True
    return False
