"""HD-033 Narrator overlay, product-UI launch, DPI overlay, and soak-start checks. Not AC37, AC38, or AC46."""
from __future__ import annotations

import ctypes
import hashlib
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


LAUNCH_REL = 'evidence/quality/narrator-product-ui-launch.json'
LAUNCH_KIND = 'hd033_narrator_product_ui_launch'
LAUNCH_REQUIRED_KEYS = (
    'document_kind', 'result', 'live_narrator', 'ac37_passed', 'l3_narrator',
    'product_ui_started', 'narrator_started_by_this_run',
    'narrator_started_by_collector', 'ac37_workflow_completed',
    'automation_names_are_not_screen_reader_evidence', 'git_sha',
    'captured_at_utc', 'platform', 'commands', 'herdr_executed',
    'g0_passed', 'invented_timings', 'keyboard_chrome', 'ac37_steps',
)
EMPTY_SHA256 = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
KEYBOARD_CHROME_PASS = frozenset({'completed', 'complete'})
LAUNCH_FALSE_KEYS = (
    'ac37_passed', 'live_narrator', 'narrator_started_by_collector',
    'ac37_workflow_completed', 'herdr_executed', 'invented_timings',
    'g0_passed',
)
LAUNCH_TRUE_KEYS = ('automation_names_are_not_screen_reader_evidence',)
AC37_STEP_KEYS = ('search', 'request_control', 'release', 'close_confirm')


def _launch_false_key_code(key: str) -> str:
    if key in {'ac37_passed', 'g0_passed'}:
        return key
    if key == 'narrator_started_by_collector':
        return 'collector_started_narrator'
    if key == 'live_narrator':
        return 'live_narrator_claimed'
    if key == 'ac37_workflow_completed':
        return 'ac37_workflow_claimed'
    return key


def _reject_launch_false_keys(doc: dict[str, Any]) -> None:
    for key in LAUNCH_FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise QualityError(_launch_false_key_code(key))


def _reject_keyboard_chrome(doc: dict[str, Any]) -> None:
    chrome = doc.get('keyboard_chrome')
    if not isinstance(chrome, str) or not chrome:
        raise QualityError('missing_record_field')
    token = _token(chrome)
    if _is_success(chrome) or token in KEYBOARD_CHROME_PASS:
        raise QualityError('ac37_workflow_claimed')


def _check_launch_hashes(
    root: Path, item: dict[str, Any], *, compare_files: bool = True
) -> None:
    """Empty SHA is valid only when the gitignored capture file is empty.

    Soak START logs may keep growing after capture. Pass compare_files=False
    so a still-running soak does not fail closed as invented_hash.
    """
    for key, path_key in (
        ('stdout_sha256', 'stdout_gitignored_path'),
        ('stderr_sha256', 'stderr_gitignored_path'),
    ):
        value = item.get(key)
        if value is None:
            continue
        if not isinstance(value, str) or len(value) != 64:
            raise QualityError('missing_record_field')
        try:
            int(value, 16)
        except ValueError as exc:
            raise QualityError('missing_record_field') from exc
        if not compare_files:
            continue
        digest = value.lower()
        rel = item.get(path_key)
        if not isinstance(rel, str) or not rel:
            continue
        path = root / rel
        if not path.is_file():
            continue
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        if actual != digest:
            raise QualityError('invented_hash')


def _reject_launch_pass_claims(doc: dict[str, Any]) -> None:
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
    if _is_success(doc.get('ac37_workflow_completed')):
        raise QualityError('ac37_workflow_claimed')
    if (
        'automation_names_are_not_screen_reader_evidence' in doc
        and doc.get('automation_names_are_not_screen_reader_evidence') is not True
    ):
        raise QualityError('automation_names_claimed_as_evidence')
    _reject_launch_false_keys(doc)


def validate_narrator_product_ui_launch(root: Path) -> dict[str, Any]:
    """Fail-closed check of a product-UI + Narrator launch record. Not AC37."""
    root = Path(root)
    doc = _load_json(root / LAUNCH_REL)
    for key in LAUNCH_REQUIRED_KEYS:
        if key not in doc:
            raise QualityError('missing_record_field')
    if doc.get('document_kind') != LAUNCH_KIND:
        raise QualityError('missing_record_field')
    if doc.get('result') != 'not_run' or _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if doc.get('l3_narrator') != 'UNVERIFIED':
        raise QualityError('l3_narrator_claimed')
    for key in LAUNCH_TRUE_KEYS:
        if doc.get(key) is not True:
            raise QualityError('automation_names_claimed_as_evidence')
    _reject_launch_pass_claims(doc)
    _check_acceptance(root)
    git_sha = doc.get('git_sha')
    if not isinstance(git_sha, str) or len(git_sha) < 7:
        raise QualityError('missing_record_field')
    captured = doc.get('captured_at_utc')
    if not isinstance(captured, str) or not captured:
        raise QualityError('missing_record_field')
    platform = doc.get('platform')
    if not isinstance(platform, dict):
        raise QualityError('missing_record_field')
    commands = doc.get('commands')
    if not isinstance(commands, list) or not commands:
        raise QualityError('missing_record_field')
    if not isinstance(doc.get('product_ui_started'), bool):
        raise QualityError('missing_record_field')
    if not isinstance(doc.get('narrator_started_by_this_run'), bool):
        raise QualityError('missing_record_field')
    steps = doc.get('ac37_steps')
    if not isinstance(steps, dict):
        raise QualityError('missing_record_field')
    for key in AC37_STEP_KEYS:
        if steps.get(key) != 'not_completed':
            raise QualityError('ac37_workflow_claimed')
    _reject_keyboard_chrome(doc)
    roles: dict[str, Any] = {}
    for item in commands:
        if not isinstance(item, dict):
            raise QualityError('missing_record_field')
        role = item.get('role')
        if not isinstance(role, str):
            raise QualityError('missing_record_field')
        roles[role] = item
        command = item.get('command_redacted')
        if not isinstance(command, list) or not command:
            raise QualityError('missing_record_field')
        if item.get('pid') is not None and (
            not isinstance(item.get('pid'), int) or item.get('pid') <= 0
        ):
            raise QualityError('missing_record_field')
        _check_launch_hashes(root, item)
    if doc.get('product_ui_started') is True:
        ui = roles.get('product_ui')
        if not isinstance(ui, dict) or not isinstance(ui.get('pid'), int):
            raise QualityError('missing_record_field')
        argv = [str(part) for part in ui.get('command_redacted') or []]
        joined = ' '.join(argv)
        if '--ui' not in argv and '--ui' not in joined:
            raise QualityError('missing_record_field')
        if '--shell-smoke' in argv or '--compose-only' in argv:
            raise QualityError('missing_record_field')
    if doc.get('narrator_started_by_this_run') is True:
        narrator = roles.get('narrator')
        if not isinstance(narrator, dict) or not isinstance(narrator.get('pid'), int):
            raise QualityError('missing_record_field')
        argv = [str(part).lower() for part in narrator.get('command_redacted') or []]
        if not any('narrator.exe' in part for part in argv):
            raise QualityError('missing_record_field')
    pointer = _load_json(root / POINTER_REL)
    live = _load_json(root / LIVE_NARRATOR_REL)
    _reject_pass_claims(pointer)
    _reject_pass_claims(live)
    if pointer.get('narrator_started_by_collector') is not False:
        raise QualityError('collector_started_narrator')
    if live.get('result') != 'not_run' or live.get('live_narrator') is not False:
        raise QualityError('live_narrator_claimed')
    return {
        'document_kind': LAUNCH_KIND,
        'launch_capture': LAUNCH_REL,
        'pointer': POINTER_REL,
        'live_capture': LIVE_NARRATOR_REL,
        'product_ui_started': doc.get('product_ui_started') is True,
        'narrator_started_by_this_run': doc.get('narrator_started_by_this_run') is True,
        'narrator_started_by_collector': False,
        'automation_names_are_not_screen_reader_evidence': True,
        'ac37_passed': False,
        'ac37_workflow_completed': False,
        'live_narrator': False,
        'result': 'not_run',
        'l3_narrator': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'invented_timings': False,
        'git_sha': git_sha,
    }


SOAK_START_REL = 'evidence/quality/live-soak-start.json'
LIVE_SOAK_REL = 'evidence/quality/live-soak.not-run.json'
SOAK_START_KIND = 'hd033_eight_hour_soak_start'
SOAK_START_REQUIRED_KEYS = (
    'document_kind', 'result', 'live_soak', 'ac46_passed', 'l4_soak',
    'product_ui_started', 'eight_hour_soak_executed', 'soak_hours',
    'started_at_utc', 'git_sha', 'platform', 'commands', 'herdr_executed',
    'g0_passed', 'invented_timings', 'disconnect_switch_count',
)
SOAK_START_FALSE_KEYS = (
    'ac46_passed', 'live_soak', 'eight_hour_soak_executed',
    'herdr_executed', 'invented_timings', 'g0_passed',
)
SOAK_TIMING_KEYS = (
    'soak_hours', 'sample_count', 'disconnect_switch_count',
    'p95_ms', 'visible_pixel_ms', 'parser_consumed_ms',
    'working_set_bytes', 'private_bytes', 'handle_count', 'process_count',
    'q_p_bytes', 'cycle_count', 'resize_rate',
)


def _soak_false_key_code(key: str) -> str:
    if key in {'ac46_passed', 'g0_passed', 'eight_hour_soak_executed'}:
        return key
    if key == 'live_soak':
        return 'live_soak_claimed'
    return key


def _reject_soak_false_keys(doc: dict[str, Any]) -> None:
    for key in SOAK_START_FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise QualityError(_soak_false_key_code(key))


def _reject_soak_invented_timings(doc: Any) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key in SOAK_TIMING_KEYS and value is not None:
                raise QualityError('invented_timings')
            _reject_soak_invented_timings(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_soak_invented_timings(item)


def _reject_soak_pass_claims(doc: dict[str, Any]) -> None:
    if _is_success(doc.get('ac46_passed')):
        raise QualityError('ac46_passed')
    if _is_success(doc.get('g0_passed')):
        raise QualityError('g0_passed')
    if _is_success(doc.get('phase_gate')) or doc.get('phase_gate') == 'passed':
        raise QualityError('ac46_passed')
    if _is_success(doc.get('live_soak')):
        raise QualityError('live_soak_claimed')
    if _is_success(doc.get('eight_hour_soak_executed')):
        raise QualityError('eight_hour_soak_executed')
    if _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if 'l4_soak' in doc and doc.get('l4_soak') != 'UNVERIFIED':
        raise QualityError('l4_soak_claimed')
    _reject_soak_false_keys(doc)
    _reject_soak_invented_timings(doc)


def _check_ac_status(root: Path, ac_id: str, error_code: str) -> None:
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
        if not isinstance(item, dict) or item.get('id') != ac_id:
            continue
        if _is_success(item.get('status')) or item.get('status') == 'passed':
            raise QualityError(error_code)
        return


def validate_eight_hour_soak_start(root: Path) -> dict[str, Any]:
    """Fail-closed check of a product-UI soak START record. Not AC46."""
    root = Path(root)
    doc = _load_json(root / SOAK_START_REL)
    for key in SOAK_START_REQUIRED_KEYS:
        if key not in doc:
            raise QualityError('missing_record_field')
    if doc.get('document_kind') != SOAK_START_KIND:
        raise QualityError('missing_record_field')
    if doc.get('result') != 'not_run' or _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if doc.get('l4_soak') != 'UNVERIFIED':
        raise QualityError('l4_soak_claimed')
    if doc.get('product_ui_started') is not True:
        raise QualityError('product_ui_not_started')
    _reject_soak_pass_claims(doc)
    _check_ac_status(root, 'AC46', 'ac46_passed')
    git_sha = doc.get('git_sha')
    if not isinstance(git_sha, str) or len(git_sha) < 7:
        raise QualityError('missing_record_field')
    started = doc.get('started_at_utc')
    if not isinstance(started, str) or not started:
        raise QualityError('missing_record_field')
    platform = doc.get('platform')
    if not isinstance(platform, dict):
        raise QualityError('missing_record_field')
    commands = doc.get('commands')
    if not isinstance(commands, list) or not commands:
        raise QualityError('missing_record_field')
    roles: dict[str, Any] = {}
    for item in commands:
        if not isinstance(item, dict):
            raise QualityError('missing_record_field')
        role = item.get('role')
        if not isinstance(role, str):
            raise QualityError('missing_record_field')
        roles[role] = item
        command = item.get('command_redacted')
        if not isinstance(command, list) or not command:
            raise QualityError('missing_record_field')
        if item.get('pid') is not None and (
            not isinstance(item.get('pid'), int) or item.get('pid') <= 0
        ):
            raise QualityError('missing_record_field')
        _check_launch_hashes(root, item, compare_files=False)
        argv = [str(part) for part in command]
        joined = ' '.join(argv)
        if '--shell-smoke' in argv or '--compose-only' in argv:
            raise QualityError('not_product_ui')
        if role == 'product_ui' and '--ui' not in argv and '--ui' not in joined:
            raise QualityError('not_product_ui')
    ui = roles.get('product_ui')
    if not isinstance(ui, dict) or not isinstance(ui.get('pid'), int):
        raise QualityError('missing_record_field')
    app_pids = ui.get('app_pids')
    if app_pids is not None:
        if not isinstance(app_pids, list) or not app_pids:
            raise QualityError('missing_record_field')
        for pid in app_pids:
            if not isinstance(pid, int) or pid <= 0:
                raise QualityError('missing_record_field')
    live = _load_json(root / LIVE_SOAK_REL)
    _reject_soak_pass_claims(live)
    if live.get('result') != 'not_run':
        raise QualityError('live_success_claimed')
    if live.get('eight_hour_soak_executed') is not False:
        raise QualityError('eight_hour_soak_executed')
    if live.get('soak_hours') is not None:
        raise QualityError('invented_timings')
    return {
        'document_kind': SOAK_START_KIND,
        'soak_start_capture': SOAK_START_REL,
        'live_capture': LIVE_SOAK_REL,
        'product_ui_started': True,
        'eight_hour_soak_executed': False,
        'soak_hours': None,
        'live_soak': False,
        'ac46_passed': False,
        'result': 'not_run',
        'l4_soak': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'invented_timings': False,
        'git_sha': git_sha,
        'started_at_utc': started,
    }
