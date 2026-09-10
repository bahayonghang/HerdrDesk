"""HD-033 Narrator presence and current-system-DPI overlays. Not AC37 or AC38."""
from __future__ import annotations

import ctypes
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
DPI_POINTER_REL = 'evidence/quality/dpi-overlay-pointer.json'
LIVE_DPI_REL = 'evidence/quality/live-dpi.not-run.json'
DPI_DOCUMENT_KIND = 'hd033_dpi_overlay_pointer'
DPI_REPORT_KIND = 'hd033_dpi_overlay'
DPI_REQUIRED_KEYS = (
    'document_kind', 'result', 'live_dpi', 'ac38_passed', 'l3_dpi',
    'dpi_matrix_100_150_200_executed',
    'display_scale_changed_by_collector',
    'single_dpi_sample_is_not_matrix',
)
DPI_FALSE_KEYS = (
    'ac38_passed', 'live_dpi', 'display_scale_changed_by_collector',
    'dpi_matrix_100_150_200_executed', 'herdr_executed', 'invented_timings',
    'g0_passed',
)
DPI_TRUE_KEYS = ('single_dpi_sample_is_not_matrix',)
# LOGPIXELSX; same fallback index as EnvironmentManifestCollector.ReadWindowsSystemDpi.
_LOGPIXELSX = 88


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


def _dpi_false_key_code(key: str) -> str:
    if key in {'ac38_passed', 'g0_passed'}:
        return key
    if key == 'display_scale_changed_by_collector':
        return 'collector_changed_display_scale'
    if key == 'live_dpi':
        return 'live_dpi_claimed'
    if key == 'dpi_matrix_100_150_200_executed':
        return 'dpi_matrix_claimed'
    return key


def _reject_dpi_false_keys(doc: dict[str, Any]) -> None:
    for key in DPI_FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise QualityError(_dpi_false_key_code(key))


def _reject_dpi_pass_claims(doc: dict[str, Any]) -> None:
    if _is_success(doc.get('ac38_passed')):
        raise QualityError('ac38_passed')
    if _is_success(doc.get('g0_passed')):
        raise QualityError('g0_passed')
    if _is_success(doc.get('phase_gate')) or doc.get('phase_gate') == 'passed':
        raise QualityError('ac38_passed')
    if _is_success(doc.get('live_dpi')):
        raise QualityError('live_dpi_claimed')
    if _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if 'l3_dpi' in doc and doc.get('l3_dpi') != 'UNVERIFIED':
        raise QualityError('l3_dpi_claimed')
    if _is_success(doc.get('display_scale_changed_by_collector')):
        raise QualityError('collector_changed_display_scale')
    if _is_success(doc.get('dpi_matrix_100_150_200_executed')):
        raise QualityError('dpi_matrix_claimed')
    if (
        'single_dpi_sample_is_not_matrix' in doc
        and doc.get('single_dpi_sample_is_not_matrix') is not True
    ):
        raise QualityError('single_sample_claimed_as_matrix')
    _reject_dpi_false_keys(doc)


def _check_dpi_acceptance(root: Path) -> None:
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
        if not isinstance(item, dict) or item.get('id') != 'AC38':
            continue
        if _is_success(item.get('status')) or item.get('status') == 'passed':
            raise QualityError('ac38_passed')
        return


def system_dpi() -> int | None:
    """Same GetDpiForSystem path as EnvironmentManifestCollector.ReadWindowsSystemDpi."""
    if os.name != 'nt':
        return None
    user32 = ctypes.WinDLL('user32')
    try:
        get_dpi = user32.GetDpiForSystem
        get_dpi.restype = ctypes.c_uint
        get_dpi.argtypes = []
        dpi = int(get_dpi())
        if dpi > 0:
            return dpi
    except (AttributeError, OSError, ValueError):
        pass
    try:
        get_dc = user32.GetDC
        get_dc.restype = ctypes.c_void_p
        get_dc.argtypes = [ctypes.c_void_p]
        release_dc = user32.ReleaseDC
        release_dc.restype = ctypes.c_int
        release_dc.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
        gdi32 = ctypes.WinDLL('gdi32')
        get_caps = gdi32.GetDeviceCaps
        get_caps.restype = ctypes.c_int
        get_caps.argtypes = [ctypes.c_void_p, ctypes.c_int]
        dc = get_dc(None)
        if not dc:
            return None
        try:
            dpi = int(get_caps(dc, _LOGPIXELSX))
            return dpi if dpi > 0 else None
        finally:
            release_dc(None, dc)
    except (AttributeError, OSError, ValueError):
        return None


def collect_dpi_overlay(root: Path) -> dict[str, Any]:
    """Record current system DPI. Not AC38 and not a 100/150/200 matrix."""
    root = Path(root)
    pointer = _load_json(root / DPI_POINTER_REL)
    live = _load_json(root / LIVE_DPI_REL)
    for key in DPI_REQUIRED_KEYS:
        if key not in pointer:
            raise QualityError('missing_record_field')
    if pointer.get('document_kind') != DPI_DOCUMENT_KIND:
        raise QualityError('missing_record_field')
    if pointer.get('result') != 'not_run' or _is_success(pointer.get('result')):
        raise QualityError('live_success_claimed')
    if pointer.get('l3_dpi') != 'UNVERIFIED':
        raise QualityError('l3_dpi_claimed')
    _reject_dpi_false_keys(pointer)
    for key in DPI_TRUE_KEYS:
        if pointer.get(key) is not True:
            raise QualityError('single_sample_claimed_as_matrix')
    _reject_dpi_pass_claims(pointer)
    _reject_dpi_pass_claims(live)
    if live.get('result') != 'not_run' or _is_success(live.get('result')):
        raise QualityError('live_success_claimed')
    if live.get('kind') not in (None, 'live_dpi', 'live_dpi_theme'):
        raise QualityError('live_success_claimed')
    _check_dpi_acceptance(root)
    dpi = system_dpi()
    if os.name == 'nt' and not isinstance(dpi, int):
        raise QualityError('missing_record_field')
    return {
        'document_kind': DPI_REPORT_KIND,
        'pointer': DPI_POINTER_REL,
        'live_capture': LIVE_DPI_REL,
        'system_dpi': dpi,
        'single_dpi_sample_is_not_matrix': True,
        'display_scale_changed_by_collector': False,
        'dpi_matrix_100_150_200_executed': False,
        'ac38_passed': False,
        'live_dpi': False,
        'result': 'not_run',
        'l3_dpi': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'invented_timings': False,
    }
