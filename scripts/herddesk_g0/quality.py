"""HD-033 Narrator presence overlay. Not screen-reader evidence or AC37."""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
from typing import Any


class QualityError(ValueError):
    """Stable quality-overlay code; the message is the code only."""


POINTER_REL = 'evidence/quality/narrator-overlay-pointer.json'
LIVE_NARRATOR_REL = 'evidence/quality/live-narrator.not-run.json'
DOCUMENT_KIND = 'hd033_narrator_overlay_pointer'
REPORT_KIND = 'hd033_narrator_overlay'
SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
REQUIRED_KEYS = (
    'document_kind', 'result', 'live_narrator', 'ac37_passed', 'l3_narrator',
    'automation_names_are_not_screen_reader_evidence',
    'narrator_started_by_collector',
)
FALSE_KEYS = (
    'ac37_passed', 'live_narrator', 'narrator_started_by_collector',
    'herdr_executed', 'invented_timings', 'g0_passed',
)
TRUE_KEYS = ('automation_names_are_not_screen_reader_evidence',)


def _token(value):
    return value.strip().lower() if isinstance(value, str) else value


def _is_success(value) -> bool:
    if value is True:
        return True
    return _token(value) in SUCCESS


def _false_key_code(key: str) -> str:
    if key in {'ac37_passed', 'g0_passed'}:
        return key
    if key == 'narrator_started_by_collector':
        return 'collector_started_narrator'
    if key == 'live_narrator':
        return 'live_narrator_claimed'
    return key


def _reject_false_keys(doc: dict[str, Any]) -> None:
    for key in FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise QualityError(_false_key_code(key))


def _load_json(path: Path) -> dict[str, Any]:
    if not path.is_file():
        raise QualityError('missing_record_field')
    try:
        loaded = json.loads(path.read_text(encoding='utf-8'))
    except (OSError, json.JSONDecodeError) as exc:
        raise QualityError('missing_record_field') from exc
    if not isinstance(loaded, dict):
        raise QualityError('missing_record_field')
    return loaded


def narrator_exe_path() -> Path:
    """Same System32 path as EnvironmentManifestCollector.NarratorExePath."""
    system = os.environ.get('SystemRoot') or os.environ.get('WINDIR') or r'C:\Windows'
    return Path(system) / 'System32' / 'Narrator.exe'


def narrator_exe_present() -> bool:
    return narrator_exe_path().is_file()


def narrator_launched() -> bool:
    """Process-list observation only. Do not start Narrator.exe."""
    if os.name != 'nt':
        return False
    startupinfo = None
    creationflags = 0
    if hasattr(subprocess, 'STARTUPINFO'):
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= getattr(subprocess, 'STARTF_USESHOWWINDOW', 0)
        creationflags = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
    try:
        completed = subprocess.run(
            ['tasklist', '/FI', 'IMAGENAME eq Narrator.exe', '/FO', 'CSV', '/NH'],
            capture_output=True,
            text=True,
            encoding='utf-8',
            errors='replace',
            check=False,
            timeout=10,
            startupinfo=startupinfo,
            creationflags=creationflags,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    if completed.returncode != 0:
        return False
    for line in (completed.stdout or '').splitlines():
        stripped = line.strip().lstrip('"').lower()
        if stripped.startswith('narrator.exe'):
            return True
    return False


def _reject_pass_claims(doc: dict[str, Any]) -> None:
    if _is_success(doc.get('ac37_passed')):
        raise QualityError('ac37_passed')
    if _is_success(doc.get('g0_passed')):
        raise QualityError('g0_passed')
    if _is_success(doc.get('phase_gate')) or doc.get('phase_gate') == 'passed':
        raise QualityError('ac37_passed')
    if _is_success(doc.get('live_narrator')):
        raise QualityError('live_narrator_claimed')
    if _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if 'l3_narrator' in doc and doc.get('l3_narrator') != 'UNVERIFIED':
        raise QualityError('l3_narrator_claimed')
    if _is_success(doc.get('narrator_started_by_collector')):
        raise QualityError('collector_started_narrator')
    if (
        'automation_names_are_not_screen_reader_evidence' in doc
        and doc.get('automation_names_are_not_screen_reader_evidence') is not True
    ):
        raise QualityError('automation_names_claimed_as_evidence')
    _reject_false_keys(doc)


def _check_acceptance(root: Path) -> None:
    path = root / 'planning' / 'acceptance.json'
    if not path.is_file():
        return
    try:
        loaded = json.loads(path.read_text(encoding='utf-8'))
    except (OSError, json.JSONDecodeError) as exc:
        raise QualityError('missing_record_field') from exc
    criteria = loaded.get('criteria') if isinstance(loaded, dict) else None
    if not isinstance(criteria, list):
        return
    for item in criteria:
        if not isinstance(item, dict) or item.get('id') != 'AC37':
            continue
        if _is_success(item.get('status')) or item.get('status') == 'passed':
            raise QualityError('ac37_passed')
        return


def collect_narrator_overlay(root: Path) -> dict[str, Any]:
    """Record Narrator.exe presence versus launched. Not AC37."""
    root = Path(root)
    pointer = _load_json(root / POINTER_REL)
    live = _load_json(root / LIVE_NARRATOR_REL)
    for key in REQUIRED_KEYS:
        if key not in pointer:
            raise QualityError('missing_record_field')
    if pointer.get('document_kind') != DOCUMENT_KIND:
        raise QualityError('missing_record_field')
    if pointer.get('result') != 'not_run' or _is_success(pointer.get('result')):
        raise QualityError('live_success_claimed')
    if pointer.get('l3_narrator') != 'UNVERIFIED':
        raise QualityError('l3_narrator_claimed')
    _reject_false_keys(pointer)
    for key in TRUE_KEYS:
        if pointer.get(key) is not True:
            raise QualityError('automation_names_claimed_as_evidence')
    _reject_pass_claims(pointer)
    _reject_pass_claims(live)
    if live.get('result') != 'not_run' or _is_success(live.get('result')):
        raise QualityError('live_success_claimed')
    if live.get('kind') not in (None, 'live_narrator'):
        raise QualityError('live_success_claimed')
    _check_acceptance(root)
    present = narrator_exe_present()
    launched = narrator_launched()
    return {
        'document_kind': REPORT_KIND,
        'pointer': POINTER_REL,
        'live_capture': LIVE_NARRATOR_REL,
        'narrator_exe_present': present,
        'narrator_launched': launched,
        'narrator_started_by_collector': False,
        'automation_names_are_not_screen_reader_evidence': True,
        'ac37_passed': False,
        'live_narrator': False,
        'result': 'not_run',
        'l3_narrator': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'invented_timings': False,
    }
