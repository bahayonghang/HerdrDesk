"""HD-036 hosted-workflow pointer bind. Not a required-check or 1.0 claim."""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
from typing import Any


class ReleaseError(ValueError):
    """Stable release-bind code; the message is the code only."""


POINTER_REL = 'evidence/releases/hosted-workflow-pointer.json'
DOCUMENT_KIND = 'hd036_hosted_workflow_pointer'
ADMITTED_BOUND_SHA = '602c252ae10303b63c6d8fc584e193e1ed5654c4'
ADMITTED_RUN_ID = '34438599236'
ADMITTED_CONCLUSION = 'success'
SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
NULL_KEYS = (
    'package_sha256', 'msix_sha256', 'signed_package_sha256', 'sbom_sha256',
    'publisher', 'candidate_sha', 'hosted_check_run_id',
)
FALSE_KEYS = (
    'published', 'complete_1_0_claimed',
    'ac39_passed', 'ac40_passed', 'ac45_passed', 'ac47_passed', 'ac48_passed',
    'g0_passed', 'invented_github_required_check', 'invented_package_hashes',
    'invented_sbom', 'independent_user_walkthrough_executed',
    'signed_package_unpacked', 'herdr_executed',
)
TRUE_KEYS = (
    'hosted_workflow_is_not_required_check_ruleset',
    'hosted_actions_on_older_sha_is_not_head_proof',
    'local_just_ci_is_not_hosted_bar',
)
REQUIRED_KEYS = (
    'document_kind', 'bound_sha', 'hosted_workflow_run_id',
    'hosted_workflow_conclusion', 'github_required_check', 'result',
    *TRUE_KEYS, *FALSE_KEYS, 'phase_gate',
)


def _token(value):
    return value.strip().lower() if isinstance(value, str) else value


def _is_success(value) -> bool:
    if value is True:
        return True
    return _token(value) in SUCCESS


def _git_head(root: Path) -> str | None:
    try:
        completed = subprocess.run(
            ['git', 'rev-parse', 'HEAD'],
            cwd=str(root),
            capture_output=True,
            text=True,
            encoding='utf-8',
            check=False,
        )
    except OSError:
        return None
    if completed.returncode != 0:
        return None
    head = completed.stdout.strip().lower()
    if len(head) != 40 or any(ch not in '0123456789abcdef' for ch in head):
        return None
    return head


def bind_release_candidate(root: Path) -> dict[str, Any]:
    """Bind the committed hosted-workflow pointer. Not AC40 or 1.0."""
    root = Path(root)
    pointer_path = root / POINTER_REL
    if not pointer_path.is_file():
        raise ReleaseError('missing_record_field')
    try:
        pointer = json.loads(pointer_path.read_text(encoding='utf-8'))
    except (OSError, json.JSONDecodeError) as exc:
        raise ReleaseError('missing_record_field') from exc
    if not isinstance(pointer, dict):
        raise ReleaseError('missing_record_field')
    for key in REQUIRED_KEYS:
        if key not in pointer:
            raise ReleaseError('missing_record_field')
    if pointer.get('document_kind') != DOCUMENT_KIND:
        raise ReleaseError('missing_record_field')
    if pointer.get('complete_1_0_claimed') is True:
        raise ReleaseError('complete_1_0_claimed')
    if pointer.get('published') is True:
        raise ReleaseError('published')
    if pointer.get('ac40_passed') is True:
        raise ReleaseError('ac40_passed')
    if _is_success(pointer.get('github_required_check')):
        raise ReleaseError('github_required_check_claimed')
    if pointer.get('github_required_check') != 'UNVERIFIED':
        raise ReleaseError('github_required_check_claimed')
    if pointer.get('result') != 'not_run' or _is_success(pointer.get('result')):
        raise ReleaseError('live_success_claimed')
    if pointer.get('bound_sha') != ADMITTED_BOUND_SHA:
        raise ReleaseError('bound_sha_mismatch')
    if pointer.get('hosted_workflow_run_id') != ADMITTED_RUN_ID:
        raise ReleaseError('bound_sha_mismatch')
    if pointer.get('hosted_workflow_conclusion') != ADMITTED_CONCLUSION:
        raise ReleaseError('bound_sha_mismatch')
    if _is_success(pointer.get('phase_gate')) or pointer.get('phase_gate') == 'passed':
        raise ReleaseError('ac40_passed')
    if pointer.get('signed_package_unpacked') is True:
        raise ReleaseError('invented_package_hashes')
    for key in NULL_KEYS:
        if pointer.get(key) is not None:
            raise ReleaseError('invented_package_hashes')
    for key in FALSE_KEYS:
        if pointer.get(key) is not False:
            raise ReleaseError(key)
    for key in TRUE_KEYS:
        if pointer.get(key) is not True:
            raise ReleaseError('missing_record_field')
    git_head = _git_head(root)
    bound_sha = ADMITTED_BOUND_SHA
    return {
        'document_kind': DOCUMENT_KIND,
        'bound_sha': bound_sha,
        'hosted_workflow_run_id': ADMITTED_RUN_ID,
        'hosted_workflow_url': pointer.get('hosted_workflow_url'),
        'hosted_workflow_conclusion': ADMITTED_CONCLUSION,
        'github_required_check': 'UNVERIFIED',
        'hosted_workflow_is_not_required_check_ruleset': True,
        'hosted_actions_on_older_sha_is_not_head_proof': True,
        'local_just_ci_is_not_hosted_bar': True,
        'published': False,
        'complete_1_0_claimed': False,
        'ac39_passed': False,
        'ac40_passed': False,
        'ac45_passed': False,
        'ac47_passed': False,
        'ac48_passed': False,
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'invented_github_required_check': False,
        'invented_package_hashes': False,
        'invented_sbom': False,
        'independent_user_walkthrough_executed': False,
        'signed_package_unpacked': False,
        'package_sha256': None,
        'msix_sha256': None,
        'signed_package_sha256': None,
        'sbom_sha256': None,
        'publisher': None,
        'candidate_sha': None,
        'hosted_check_run_id': None,
        'git_head': git_head,
        'head_equals_bound_sha': bool(git_head) and git_head == bound_sha,
        'result': 'not_run',
    }
