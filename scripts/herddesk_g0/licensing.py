"""Licensing-register rules for HD-002.

Structural validation is not AC02 passed and never sets windows_verified.
Admission (approved/blocked/pending) stays distinct from technical and
security fields. Public visibility is not a license grant.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import re
from typing import Any


class LicensingError(ValueError):
    """Stable licensing-rule code; the message is the code only."""


REGISTER_REL = 'docs/licensing/register.json'
REGISTER_MARKDOWN_REL = 'docs/licensing-register.md'
LICENSE_STATUS_REL = 'LICENSE-STATUS.md'
CANDIDATE_DIR_REL = 'docs/licensing/candidates'

REQUIRED_TEMPLATES = (
    'docs/licensing/candidates/nuget.template.json',
    'docs/licensing/candidates/npm.template.json',
    'docs/licensing/candidates/cargo.template.json',
    'docs/licensing/candidates/renderer-asset.template.json',
    'docs/licensing/candidates/bridge-binary.template.json',
    'docs/licensing/candidates/fixture.template.json',
)
REQUIRED_NAME_KINDS = {
    'HerdDesk': 'application_solution_namespace',
    'HerdrDesk': 'github_repository',
    '牧台': 'chinese_work_name',
    'herdr': 'upstream_protocol',
    'herdrm': 'product_reference',
}
NAME_FIELDS = (
    'name', 'kind', 'source', 'usage', 'affiliation',
    'trademark_check', 'package_identity_check',
    'evidence_date', 'owner', 'final_review_task',
)
UNIT_FIELDS = (
    'name', 'category', 'artifact_kind', 'source', 'version', 'hash',
    'license', 'notice', 'notice_status', 'modification', 'distribution',
    'technical', 'security', 'admission', 'conflict',
    'evidence_date', 'owner', 'final_review_task',
    'lock_allowed', 'enters_package_lock', 'enters_webview_bundle',
    'enters_msix', 'enters_release_manifest',
)
HERDR_ARTIFACT_KINDS = ('source', 'prebuilt_binary', 'runtime_download')
ARTIFACT_KINDS = frozenset(HERDR_ARTIFACT_KINDS + ('host', 'ci'))
ADMISSION_VALUES = frozenset({'approved', 'blocked', 'pending'})
TECHNICAL_VALUES = frozenset({'feasible', 'not_evaluated', 'not_applicable'})
SECURITY_VALUES = frozenset({'not_evaluated', 'pending', 'blocked'})
NOTICE_STATUS_VALUES = frozenset({'not_required', 'pending', 'recorded'})
APPROVAL_FLAGS = (
    'lock_allowed',
    'enters_package_lock',
    'enters_webview_bundle',
    'enters_msix',
    'enters_release_manifest',
)
SKIP_DIR_PARTS = frozenset({
    '.git', 'obj', 'bin', 'probe-results', '.test-results', '__pycache__',
    '.agents', '.codex', '.grok', '.kimi-code', '.omp', '.claude',
})
VENDORED_SUFFIXES = frozenset({
    '.swift', '.m', '.mm', '.h', '.c', '.cpp', '.hpp', '.rs',
    '.storyboard', '.xib', '.pbxproj',
    '.png', '.svg', '.ico', '.icns', '.jpg', '.jpeg', '.gif', '.webp', '.bmp',
    '.woff', '.woff2', '.ttf', '.otf', '.eot',
    '.exe', '.dll', '.so', '.dylib', '.wasm',
})
HERDRM_FILENAME_RE = re.compile(r'^herdrm([._-].*)?$', re.IGNORECASE)
PUBLIC_GRANT_RE = re.compile(
    r'public visibility(?: alone)? (?:is|equals|means) (?:an? )?(?:MIT|Apache)',
    re.I,
)
PUBLIC_DISCLAIMER = (
    'Public visibility alone must not be treated as an MIT, Apache-2.0 '
    'or other license grant.'
)
LICENSE_PENDING_DISCLAIMER = (
    'No project-wide open-source license has been selected yet.'
)


def validate_licensing(root: Path) -> dict[str, Any]:
    root = Path(root)
    register_path = root / REGISTER_REL
    markdown_path = root / REGISTER_MARKDOWN_REL
    status_path = root / LICENSE_STATUS_REL
    candidate_dir = root / CANDIDATE_DIR_REL
    if not register_path.is_file() or not markdown_path.is_file() or not status_path.is_file():
        raise LicensingError('missing_record_field')
    if not candidate_dir.is_dir():
        raise LicensingError('missing_record_field')
    register = json.loads(register_path.read_text(encoding='utf-8'))
    if not isinstance(register, dict):
        raise LicensingError('missing_record_field')
    candidates: dict[str, dict[str, Any]] = {}
    for path in sorted(candidate_dir.glob('*.json')):
        rel = path.relative_to(root).as_posix()
        loaded = json.loads(path.read_text(encoding='utf-8'))
        if not isinstance(loaded, dict):
            raise LicensingError('missing_record_field')
        candidates[rel] = loaded
    return check_licensing(
        register,
        candidates,
        license_status_text=status_path.read_text(encoding='utf-8'),
        register_markdown=markdown_path.read_text(encoding='utf-8'),
        herdrm_copy_paths=tuple(find_herdrm_copies(root)),
    )


def check_licensing(
    register: dict[str, Any],
    candidates: dict[str, dict[str, Any]],
    *,
    license_status_text: str,
    register_markdown: str,
    herdrm_copy_paths: tuple[str, ...] = (),
) -> dict[str, Any]:
    if not isinstance(register, dict) or not isinstance(candidates, dict):
        raise LicensingError('missing_record_field')
    _check_project_license(register, license_status_text)
    _check_ac02_not_passed(register)
    _check_names(register.get('names'), register_markdown)
    units = register.get('units')
    if not isinstance(units, list) or not units:
        raise LicensingError('missing_record_field')
    approved_units = 0
    for unit in units:
        _check_record(unit, template=False)
        if unit['admission'] == 'approved':
            approved_units += 1
    _check_herdr_separation(units)
    _check_herdrm_unit(units, herdrm_copy_paths)
    approved_candidates = _check_candidates(register, candidates)
    if herdrm_copy_paths:
        raise LicensingError('herdrm_copy_present')
    return {
        'licensing_validation': 'passed',
        'ac02_passed': False,
        'windows_verified': False,
        'herdrm_copy_present': False,
        'approved_unit_count': approved_units,
        'approved_candidate_count': approved_candidates,
    }


def find_herdrm_copies(root: Path) -> list[str]:
    found: list[str] = []
    root = Path(root)
    for dirpath, dirnames, filenames in os.walk(root, followlinks=False):
        dirnames[:] = [name for name in dirnames if name not in SKIP_DIR_PARTS]
        current = Path(dirpath)
        try:
            rel_dir = current.relative_to(root).as_posix()
        except ValueError:
            continue
        if current.name.lower() == 'herdrm':
            found.append(rel_dir)
        for name in filenames:
            if not _filename_is_herdrm_asset(name):
                continue
            found.append((current / name).relative_to(root).as_posix())
    return found


def _filename_is_herdrm_asset(name: str) -> bool:
    path = Path(name)
    if path.suffix.lower() not in VENDORED_SUFFIXES:
        return False
    return HERDRM_FILENAME_RE.fullmatch(path.name) is not None


def _check_ac02_not_passed(register: dict[str, Any]) -> None:
    if register.get('ac02_status') != 'not_run':
        raise LicensingError('missing_record_field')
    if register.get('ac02_passed') is not False:
        raise LicensingError('pending_treated_as_approved')
    if register.get('final_review_task') != 'HD-035':
        raise LicensingError('missing_record_field')
    children = register.get('ac02_child')
    if not isinstance(children, dict):
        raise LicensingError('missing_record_field')
    for key in ('AC02-C1', 'AC02-C2', 'AC02'):
        if key not in children:
            raise LicensingError('missing_record_field')
    if children.get('AC02') != 'not_run':
        raise LicensingError('pending_treated_as_approved')
    if children.get('AC02-C1') == 'passed' or children.get('AC02-C2') == 'passed':
        raise LicensingError('pending_treated_as_approved')


def _check_project_license(register: dict[str, Any], license_status_text: str) -> None:
    project = register.get('project_license')
    if not isinstance(project, dict):
        raise LicensingError('missing_record_field')
    if project.get('status') != 'pending':
        raise LicensingError('missing_record_field')
    if project.get('public_visibility_is_license_grant') is not False:
        raise LicensingError('public_visibility_as_license_grant')
    if project.get('final_review_task') != 'HD-035':
        raise LicensingError('missing_record_field')
    if PUBLIC_DISCLAIMER not in license_status_text:
        raise LicensingError('public_visibility_as_license_grant')
    if LICENSE_PENDING_DISCLAIMER not in license_status_text:
        raise LicensingError('public_visibility_as_license_grant')
    if PUBLIC_GRANT_RE.search(license_status_text):
        raise LicensingError('public_visibility_as_license_grant')
    if project.get('spdx') not in (None, ''):
        raise LicensingError('public_visibility_as_license_grant')


def _check_names(names: Any, register_markdown: str) -> None:
    if not isinstance(names, list):
        raise LicensingError('missing_record_field')
    have: dict[str, dict[str, Any]] = {}
    for item in names:
        if not isinstance(item, dict):
            raise LicensingError('missing_record_field')
        for key in NAME_FIELDS:
            if key not in item:
                raise LicensingError('missing_record_field')
        name = item['name']
        if not isinstance(name, str) or not name:
            raise LicensingError('missing_record_field')
        if name in have:
            raise LicensingError('missing_name_record')
        have[name] = item
    for name, kind in REQUIRED_NAME_KINDS.items():
        item = have.get(name)
        if item is None or item.get('kind') != kind:
            raise LicensingError('missing_name_record')
        if name not in register_markdown:
            raise LicensingError('missing_name_record')
        if item.get('final_review_task') != 'HD-035':
            raise LicensingError('missing_record_field')


def _check_record(record: Any, *, template: bool) -> None:
    if not isinstance(record, dict):
        raise LicensingError('missing_record_field')
    for key in UNIT_FIELDS:
        if key not in record:
            raise LicensingError('missing_record_field')
    if record.get('template') is True and not template:
        raise LicensingError('template_used_as_admission_proof')
    if template and record.get('template') is not True:
        raise LicensingError('template_used_as_admission_proof')
    if template and record.get('document_kind') != 'admission_template':
        raise LicensingError('missing_record_field')
    admission = record.get('admission')
    if admission not in ADMISSION_VALUES:
        raise LicensingError('unknown_admission_value')
    if record.get('technical') not in TECHNICAL_VALUES:
        raise LicensingError('missing_record_field')
    if record.get('security') not in SECURITY_VALUES:
        raise LicensingError('missing_record_field')
    if record.get('notice_status') not in NOTICE_STATUS_VALUES:
        raise LicensingError('missing_record_field')
    artifact_kind = record.get('artifact_kind')
    if artifact_kind not in ARTIFACT_KINDS:
        raise LicensingError('missing_record_field')
    conflict = record.get('conflict')
    if conflict is not True and conflict is not False:
        raise LicensingError('missing_record_field')
    for key in APPROVAL_FLAGS:
        value = record.get(key)
        if value is not True and value is not False:
            raise LicensingError('missing_record_field')
    if record.get('public_visibility_is_license_grant') is True:
        raise LicensingError('public_visibility_as_license_grant')
    if record.get('license_basis') == 'public_visibility':
        raise LicensingError('public_visibility_as_license_grant')
    if not template:
        name = record.get('name')
        if not isinstance(name, str) or not name:
            raise LicensingError('missing_record_field')
        if record.get('final_review_task') != 'HD-035':
            raise LicensingError('missing_record_field')
    _reject_unapproved_as_approved(record, template=template)


def _reject_unapproved_as_approved(record: dict[str, Any], *, template: bool) -> None:
    admission = record['admission']
    flagged = any(record[key] is True for key in APPROVAL_FLAGS)
    if record.get('approved') is True:
        flagged = True
    if template and admission == 'approved':
        raise LicensingError('template_used_as_admission_proof')
    if admission == 'blocked' and (flagged or admission == 'approved'):
        raise LicensingError('blocked_treated_as_approved')
    if record['conflict'] is True and admission != 'blocked':
        raise LicensingError('blocked_treated_as_approved')
    if admission == 'pending' and flagged:
        raise LicensingError('pending_treated_as_approved')
    if admission == 'approved' and record['notice_status'] == 'pending':
        raise LicensingError('pending_treated_as_approved')
    if admission == 'approved' and _license_unknown(record):
        raise LicensingError('pending_treated_as_approved')


def _license_unknown(record: dict[str, Any]) -> bool:
    license_value = record.get('license')
    if not isinstance(license_value, str) or not license_value.strip():
        return True
    lowered = license_value.lower()
    if lowered in {'unknown', 'pending', 'n/a', 'none'}:
        return True
    if 'not selected' in lowered or 'pending maintainer' in lowered:
        return True
    return False


def _check_herdr_separation(units: list[dict[str, Any]]) -> None:
    kinds: set[str] = set()
    seen: set[tuple[str, str]] = set()
    for unit in units:
        key = (unit['name'], unit['artifact_kind'])
        if key in seen:
            raise LicensingError('missing_record_field')
        seen.add(key)
        if unit['name'] == 'herdr':
            kinds.add(unit['artifact_kind'])
    if kinds != set(HERDR_ARTIFACT_KINDS):
        raise LicensingError('missing_record_field')


def _check_herdrm_unit(
    units: list[dict[str, Any]],
    herdrm_copy_paths: tuple[str, ...],
) -> None:
    matches = [unit for unit in units if unit['name'] == 'herdrm']
    if len(matches) != 1:
        raise LicensingError('missing_record_field')
    unit = matches[0]
    if unit.get('admission') != 'blocked':
        raise LicensingError('blocked_treated_as_approved')
    if 'vendored' not in unit or 'copied_files' not in unit:
        raise LicensingError('missing_record_field')
    copied = unit.get('copied_files')
    if unit.get('vendored') is not False:
        raise LicensingError('herdrm_copy_present')
    if not isinstance(copied, list) or copied:
        raise LicensingError('herdrm_copy_present')
    if herdrm_copy_paths:
        raise LicensingError('herdrm_copy_present')


def _check_candidates(
    register: dict[str, Any],
    candidates: dict[str, dict[str, Any]],
) -> int:
    listed = register.get('candidate_templates')
    if not isinstance(listed, list):
        raise LicensingError('missing_record_field')
    if set(listed) != set(REQUIRED_TEMPLATES):
        raise LicensingError('missing_record_field')
    approved = 0
    for rel in REQUIRED_TEMPLATES:
        doc = candidates.get(rel)
        if not isinstance(doc, dict):
            raise LicensingError('missing_record_field')
        _check_record(doc, template=True)
        if doc['admission'] == 'approved':
            approved += 1
    for rel, doc in candidates.items():
        if rel in REQUIRED_TEMPLATES:
            continue
        _check_record(doc, template=doc.get('template') is True)
        if doc.get('admission') == 'approved':
            approved += 1
    return approved
