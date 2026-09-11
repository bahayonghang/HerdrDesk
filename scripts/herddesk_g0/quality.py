"""HD-033 Narrator overlay, product-UI launch, DPI overlay, current-system theme overlay, soak-start, soak-interruption, optional soak-elapsed, optional soak-process working-set overlay, and optional scale-only DPI matrix overlay checks. Not AC37, AC38, AC29, or AC46. Interrupted STARTs are not 8h. Wall-clock elapsed is not AC46. A soak-process sample is not 1/4 pane, not 100 open/close, and not live_working_set. A scale-only 100/150/200 overlay is not AC38, not theme/monitor/high-contrast, and not L3. A current-system theme sample is not a light/dark/high-contrast x monitor matrix and does not pass AC38."""
from __future__ import annotations

import ctypes
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
from typing import Any

try:
    import winreg
except ImportError:
    winreg = None


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
DPI_MATRIX_REL = 'evidence/quality/live-dpi-matrix.json'
DPI_MATRIX_KIND = 'hd033_dpi_matrix'
DPI_MATRIX_TARGETS = (100, 150, 200)
DPI_MATRIX_EXPECTED_DPI = {100: 96, 150: 144, 200: 192}
DPI_MATRIX_DPI_TOLERANCE = 8
DPI_MATRIX_REQUIRED_KEYS = (
    'document_kind', 'result', 'ac38_passed', 'live_dpi', 'l3_dpi',
    'g0_passed', 'herdr_executed', 'invented_timings', 'git_sha',
    'started_at_utc', 'captured_at_utc', 'original_scale_percent',
    'restored_scale_percent', 'samples', 'restore_ok',
    'dpi_matrix_100_150_200_executed', 'display_scale_changed_by_this_record',
    'display_scale_changed_by_collector',
    'theme_matrix_executed', 'high_contrast_executed', 'multi_monitor_executed',
    'resize_rate',
)
DPI_MATRIX_FALSE_KEYS = (
    'ac38_passed', 'live_dpi', 'herdr_executed', 'invented_timings',
    'g0_passed', 'theme_matrix_executed', 'high_contrast_executed',
    'multi_monitor_executed', 'winui_admitted',
    'display_scale_changed_by_collector',
)
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


THEME_POINTER_REL = 'evidence/quality/theme-overlay-pointer.json'
THEME_DOCUMENT_KIND = 'hd033_theme_overlay_pointer'
THEME_REPORT_KIND = 'hd033_theme_overlay'
THEME_REQUIRED_KEYS = (
    'document_kind', 'result', 'live_dpi', 'ac38_passed', 'l3_dpi',
    'theme_matrix_executed', 'high_contrast_executed', 'multi_monitor_executed',
    'apps_use_light_theme_changed_by_collector',
    'high_contrast_changed_by_collector',
    'single_theme_sample_is_not_matrix',
)
THEME_FALSE_KEYS = (
    'ac38_passed', 'live_dpi', 'theme_matrix_executed',
    'high_contrast_executed', 'multi_monitor_executed',
    'apps_use_light_theme_changed_by_collector',
    'high_contrast_changed_by_collector', 'herdr_executed',
    'invented_timings', 'g0_passed',
)
THEME_TRUE_KEYS = ('single_theme_sample_is_not_matrix',)
_SPI_GETHIGHCONTRAST = 66
_HCF_HIGHCONTRASTON = 0x0001
_SM_CMONITORS = 80
_PERSONALIZE = r'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'


class HIGHCONTRASTW(ctypes.Structure):
    _fields_ = (
        ('cbSize', ctypes.c_uint),
        ('dwFlags', ctypes.c_uint),
        ('lpszDefaultScheme', ctypes.c_wchar_p),
    )


def _theme_false_key_code(key: str) -> str:
    if key in {'ac38_passed', 'g0_passed'}:
        return key
    if key == 'live_dpi':
        return 'live_dpi_claimed'
    if key == 'theme_matrix_executed':
        return 'theme_matrix_claimed'
    if key == 'high_contrast_executed':
        return 'high_contrast_claimed'
    if key == 'multi_monitor_executed':
        return 'multi_monitor_claimed'
    if key == 'apps_use_light_theme_changed_by_collector':
        return 'collector_changed_theme'
    if key == 'high_contrast_changed_by_collector':
        return 'collector_changed_high_contrast'
    return key


def _reject_theme_false_keys(doc: dict[str, Any]) -> None:
    for key in THEME_FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise QualityError(_theme_false_key_code(key))


def _reject_theme_pass_claims(doc: dict[str, Any]) -> None:
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
    if _is_success(doc.get('theme_matrix_executed')):
        raise QualityError('theme_matrix_claimed')
    if _is_success(doc.get('high_contrast_executed')):
        raise QualityError('high_contrast_claimed')
    if _is_success(doc.get('multi_monitor_executed')):
        raise QualityError('multi_monitor_claimed')
    if _is_success(doc.get('apps_use_light_theme_changed_by_collector')):
        raise QualityError('collector_changed_theme')
    if _is_success(doc.get('high_contrast_changed_by_collector')):
        raise QualityError('collector_changed_high_contrast')
    if (
        'single_theme_sample_is_not_matrix' in doc
        and doc.get('single_theme_sample_is_not_matrix') is not True
    ):
        raise QualityError('single_sample_claimed_as_matrix')
    _reject_theme_false_keys(doc)


def _dword_bool(value: Any) -> bool | None:
    if isinstance(value, bool):
        return value
    if isinstance(value, int) and value in (0, 1):
        return value == 1
    return None


def current_theme_sample() -> dict[str, Any]:
    """Read current AppsUseLightTheme, HighContrast, and monitor count.

    Does not change theme, high-contrast, or display topology. Not AC38.
    """
    sample = {
        'apps_use_light_theme': None,
        'system_uses_light_theme': None,
        'high_contrast': None,
        'monitor_count': None,
    }
    if os.name != 'nt' or winreg is None:
        return sample
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, _PERSONALIZE) as key:
            try:
                apps, _ = winreg.QueryValueEx(key, 'AppsUseLightTheme')
                sample['apps_use_light_theme'] = _dword_bool(apps)
            except OSError:
                pass
            try:
                system, _ = winreg.QueryValueEx(key, 'SystemUsesLightTheme')
                sample['system_uses_light_theme'] = _dword_bool(system)
            except OSError:
                pass
    except (ImportError, OSError, ValueError):
        pass
    try:
        user32 = ctypes.WinDLL('user32')
        get_metrics = user32.GetSystemMetrics
        get_metrics.restype = ctypes.c_int
        get_metrics.argtypes = [ctypes.c_int]
        count = int(get_metrics(_SM_CMONITORS))
        if count >= 1:
            sample['monitor_count'] = count
        get_spi = user32.SystemParametersInfoW
        get_spi.restype = ctypes.c_int
        get_spi.argtypes = [
            ctypes.c_uint, ctypes.c_uint, ctypes.c_void_p, ctypes.c_uint,
        ]
        hc = HIGHCONTRASTW()
        hc.cbSize = ctypes.sizeof(HIGHCONTRASTW)
        if int(get_spi(_SPI_GETHIGHCONTRAST, hc.cbSize, ctypes.byref(hc), 0)):
            sample['high_contrast'] = bool(hc.dwFlags & _HCF_HIGHCONTRASTON)
    except (AttributeError, OSError, ValueError):
        pass
    return sample


def collect_theme_overlay(root: Path) -> dict[str, Any]:
    """Record current theme/high-contrast/monitor count. Not AC38 or a matrix."""
    root = Path(root)
    pointer = _load_json(root / THEME_POINTER_REL)
    live = _load_json(root / LIVE_DPI_REL)
    for key in THEME_REQUIRED_KEYS:
        if key not in pointer:
            raise QualityError('missing_record_field')
    if pointer.get('document_kind') != THEME_DOCUMENT_KIND:
        raise QualityError('missing_record_field')
    if pointer.get('result') != 'not_run' or _is_success(pointer.get('result')):
        raise QualityError('live_success_claimed')
    if pointer.get('l3_dpi') != 'UNVERIFIED':
        raise QualityError('l3_dpi_claimed')
    _reject_theme_false_keys(pointer)
    for key in THEME_TRUE_KEYS:
        if pointer.get(key) is not True:
            raise QualityError('single_sample_claimed_as_matrix')
    _reject_theme_pass_claims(pointer)
    _reject_theme_pass_claims(live)
    _reject_dpi_pass_claims(live)
    if live.get('result') != 'not_run' or _is_success(live.get('result')):
        raise QualityError('live_success_claimed')
    if live.get('kind') not in (None, 'live_dpi', 'live_dpi_theme'):
        raise QualityError('live_success_claimed')
    _check_dpi_acceptance(root)
    sample = current_theme_sample()
    if os.name == 'nt' and (
        sample.get('monitor_count') is None
        or sample.get('high_contrast') not in (True, False)
    ):
        raise QualityError('missing_record_field')
    return {
        'document_kind': THEME_REPORT_KIND,
        'pointer': THEME_POINTER_REL,
        'live_capture': LIVE_DPI_REL,
        'apps_use_light_theme': sample.get('apps_use_light_theme'),
        'system_uses_light_theme': sample.get('system_uses_light_theme'),
        'high_contrast': sample.get('high_contrast'),
        'monitor_count': sample.get('monitor_count'),
        'single_theme_sample_is_not_matrix': True,
        'theme_matrix_executed': False,
        'high_contrast_executed': False,
        'multi_monitor_executed': False,
        'apps_use_light_theme_changed_by_collector': False,
        'high_contrast_changed_by_collector': False,
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
SOAK_INTERRUPT_REL = 'evidence/quality/live-soak-interrupted.json'
SOAK_INTERRUPT_2_REL = 'evidence/quality/live-soak-interrupted-2.json'
SOAK_INTERRUPT_3_REL = 'evidence/quality/live-soak-interrupted-3.json'
SOAK_INTERRUPT_4_REL = 'evidence/quality/live-soak-interrupted-4.json'
SOAK_INTERRUPT_5_REL = 'evidence/quality/live-soak-interrupted-5.json'
SOAK_INTERRUPT_6_REL = 'evidence/quality/live-soak-interrupted-6.json'
SOAK_ELAPSED_REL = 'evidence/quality/live-soak-elapsed.json'
SOAK_WORKING_SET_REL = 'evidence/quality/live-soak-working-set.json'
LIVE_WORKING_SET_REL = 'evidence/quality/live-working-set.not-run.json'
SOAK_START_KIND = 'hd033_eight_hour_soak_start'
SOAK_WORKING_SET_KIND = 'hd033_soak_working_set_overlay'
SOAK_INTERRUPT_KIND = 'hd033_eight_hour_soak_interruption'
SOAK_ELAPSED_KIND = 'hd033_eight_hour_soak_elapsed'
SOAK_WALL_CLOCK = timedelta(hours=8)
SOAK_START_REQUIRED_KEYS = (
    'document_kind', 'result', 'live_soak', 'ac46_passed', 'l4_soak',
    'product_ui_started', 'eight_hour_soak_executed', 'soak_hours',
    'started_at_utc', 'owned_pids', 'git_sha', 'platform', 'commands',
    'herdr_executed', 'g0_passed', 'invented_timings',
    'disconnect_switch_count', 'prior_interruption_capture',
    'prior_interruption_captures',
)
SOAK_ELAPSED_REQUIRED_KEYS = (
    'document_kind', 'result', 'live_soak', 'ac46_passed', 'l4_soak',
    'product_ui_started', 'eight_hour_soak_executed', 'soak_hours',
    'started_at_utc', 'elapsed_at_utc', 'owned_pids', 'git_sha',
    'herdr_executed', 'g0_passed', 'invented_timings',
    'disconnect_switch_count', 'start_capture',
)
SOAK_ELAPSED_FALSE_KEYS = (
    'ac46_passed', 'ac29_passed', 'live_soak', 'live_working_set',
    'herdr_executed', 'invented_timings', 'g0_passed',
)
SOAK_INTERRUPT_REQUIRED_KEYS = (
    'document_kind', 'result', 'live_soak', 'ac46_passed', 'l4_soak',
    'product_ui_started', 'eight_hour_soak_executed', 'soak_hours',
    'started_at_utc', 'last_heartbeat_alive_at_utc',
    'first_heartbeat_empty_alive_at_utc', 'owned_pids', 'git_sha',
    'herddesk_crash_dump_found', 'application_error_herddesk', 'crash_cause',
    'herdr_executed', 'g0_passed', 'invented_timings',
    'disconnect_switch_count', 'xerox_print_experience_crash_unrelated',
    'process_running_at_capture',
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


def _parse_utc(value: Any) -> datetime:
    if not isinstance(value, str) or not value.strip():
        raise QualityError('missing_record_field')
    text = value.strip()
    if text.endswith('Z'):
        text = text[:-1] + '+00:00'
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError as exc:
        raise QualityError('missing_record_field') from exc
    if parsed.tzinfo is None:
        raise QualityError('missing_record_field')
    return parsed.astimezone(timezone.utc)


def _collect_owned_pids(doc: dict[str, Any]) -> set[int]:
    found: set[int] = set()
    for key in (
        'owned_pids',
        'last_heartbeat_alive_pids',
        'watchdog_alive_pids',
    ):
        value = doc.get(key)
        if isinstance(value, list):
            for pid in value:
                if isinstance(pid, int) and pid > 0:
                    found.add(pid)
    heartbeat = doc.get('heartbeat_pid')
    if isinstance(heartbeat, int) and heartbeat > 0:
        found.add(heartbeat)
    windows = doc.get('windows')
    if isinstance(windows, list):
        for item in windows:
            if isinstance(item, dict):
                pid = item.get('pid')
                if isinstance(pid, int) and pid > 0:
                    found.add(pid)
    commands = doc.get('commands')
    if isinstance(commands, list):
        for item in commands:
            if not isinstance(item, dict):
                continue
            pid = item.get('pid')
            if isinstance(pid, int) and pid > 0:
                found.add(pid)
            app_pids = item.get('app_pids')
            if isinstance(app_pids, list):
                for pid in app_pids:
                    if isinstance(pid, int) and pid > 0:
                        found.add(pid)
    return found


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


def soak_interrupt_capture_rels(root: Path) -> list[str]:
    """Committed interruption captures. First 09:49 file stays first."""
    rels = [SOAK_INTERRUPT_REL]
    for rel in (
        SOAK_INTERRUPT_2_REL,
        SOAK_INTERRUPT_3_REL,
        SOAK_INTERRUPT_4_REL,
        SOAK_INTERRUPT_5_REL,
        SOAK_INTERRUPT_6_REL,
    ):
        if (Path(root) / rel).is_file():
            rels.append(rel)
    return rels


def _validate_interrupt_document(root: Path, doc: dict[str, Any]) -> None:
    for key in SOAK_INTERRUPT_REQUIRED_KEYS:
        if key not in doc:
            raise QualityError('missing_record_field')
    if doc.get('document_kind') != SOAK_INTERRUPT_KIND:
        raise QualityError('missing_record_field')
    if doc.get('result') != 'not_run' or _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if doc.get('l4_soak') != 'UNVERIFIED':
        raise QualityError('l4_soak_claimed')
    if doc.get('product_ui_started') is not True:
        raise QualityError('product_ui_not_started')
    if doc.get('process_running_at_capture') is not False:
        raise QualityError('missing_record_field')
    if doc.get('herddesk_crash_dump_found') is not False:
        raise QualityError('invented_crash_cause')
    if doc.get('application_error_herddesk') is not False:
        raise QualityError('invented_crash_cause')
    if doc.get('crash_cause') is not None:
        raise QualityError('invented_crash_cause')
    if doc.get('xerox_print_experience_crash_unrelated') is not True:
        raise QualityError('invented_crash_cause')
    _reject_soak_pass_claims(doc)
    _check_ac_status(root, 'AC46', 'ac46_passed')
    git_sha = doc.get('git_sha')
    if not isinstance(git_sha, str) or len(git_sha) < 7:
        raise QualityError('missing_record_field')
    for key in (
        'started_at_utc',
        'last_heartbeat_alive_at_utc',
        'first_heartbeat_empty_alive_at_utc',
    ):
        value = doc.get(key)
        if not isinstance(value, str) or not value:
            raise QualityError('missing_record_field')
    owned = doc.get('owned_pids')
    if not isinstance(owned, list) or not owned:
        raise QualityError('missing_record_field')
    for pid in owned:
        if not isinstance(pid, int) or pid <= 0:
            raise QualityError('missing_record_field')


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
    owned = doc.get('owned_pids')
    if not isinstance(owned, list) or not owned:
        raise QualityError('missing_record_field')
    for pid in owned:
        if not isinstance(pid, int) or pid <= 0:
            raise QualityError('missing_record_field')
    pointer = doc.get('prior_interruption_capture')
    if pointer != SOAK_INTERRUPT_REL:
        raise QualityError('missing_record_field')
    listed = doc.get('prior_interruption_captures')
    committed = soak_interrupt_capture_rels(root)
    if not isinstance(listed, list) or [str(item) for item in listed] != committed:
        raise QualityError('missing_record_field')
    interruption = validate_eight_hour_soak_interruption(root)
    start_at = _parse_utc(started)
    start_pids = _collect_owned_pids(doc)
    interrupt_pids: set[int] = set()
    for rel in committed:
        interrupt_doc = _load_json(root / rel)
        if start_at <= _parse_utc(interrupt_doc.get('started_at_utc')):
            raise QualityError('continuation_of_interrupted_soak')
        if start_at <= _parse_utc(
            interrupt_doc.get('first_heartbeat_empty_alive_at_utc')
        ):
            raise QualityError('continuation_of_interrupted_soak')
        interrupt_pids |= _collect_owned_pids(interrupt_doc)
    if not start_pids or start_pids & interrupt_pids:
        raise QualityError('continuation_of_interrupted_soak')
    return {
        'document_kind': SOAK_START_KIND,
        'soak_start_capture': SOAK_START_REL,
        'soak_interruption_capture': SOAK_INTERRUPT_REL,
        'soak_interruption_captures': committed,
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
        'prior_interruption_started_at_utc': interruption.get('started_at_utc'),
    }


def validate_eight_hour_soak_interruption(root: Path) -> dict[str, Any]:
    """Fail-closed check of interrupted soak START captures. Not 8h and not AC46."""
    root = Path(root)
    doc = _load_json(root / SOAK_INTERRUPT_REL)
    _validate_interrupt_document(root, doc)
    if (root / SOAK_INTERRUPT_2_REL).is_file():
        _validate_interrupt_document(root, _load_json(root / SOAK_INTERRUPT_2_REL))
    if (root / SOAK_INTERRUPT_3_REL).is_file():
        _validate_interrupt_document(root, _load_json(root / SOAK_INTERRUPT_3_REL))
    if (root / SOAK_INTERRUPT_4_REL).is_file():
        _validate_interrupt_document(root, _load_json(root / SOAK_INTERRUPT_4_REL))
    if (root / SOAK_INTERRUPT_5_REL).is_file():
        _validate_interrupt_document(root, _load_json(root / SOAK_INTERRUPT_5_REL))
    if (root / SOAK_INTERRUPT_6_REL).is_file():
        _validate_interrupt_document(root, _load_json(root / SOAK_INTERRUPT_6_REL))
    return {
        'document_kind': SOAK_INTERRUPT_KIND,
        'soak_interruption_capture': SOAK_INTERRUPT_REL,
        'soak_interruption_captures': soak_interrupt_capture_rels(root),
        'product_ui_started': True,
        'process_running_at_capture': False,
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
        'crash_cause': None,
        'git_sha': doc.get('git_sha'),
        'started_at_utc': doc.get('started_at_utc'),
    }


def validate_eight_hour_soak_elapsed(root: Path) -> dict[str, Any] | None:
    """Optional elapsed capture. Missing is allowed. Not AC46."""
    root = Path(root)
    path = root / SOAK_ELAPSED_REL
    if not path.is_file():
        return None
    doc = _load_json(path)
    for key in SOAK_ELAPSED_REQUIRED_KEYS:
        if key not in doc:
            raise QualityError('missing_record_field')
    if doc.get('document_kind') != SOAK_ELAPSED_KIND:
        raise QualityError('missing_record_field')
    if doc.get('result') != 'not_run' or _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if doc.get('l4_soak') != 'UNVERIFIED':
        raise QualityError('l4_soak_claimed')
    if doc.get('product_ui_started') is not True:
        raise QualityError('product_ui_not_started')
    if doc.get('eight_hour_soak_executed') is not True:
        raise QualityError('eight_hour_wall_clock_incomplete')
    if doc.get('start_capture') != SOAK_START_REL:
        raise QualityError('missing_record_field')
    if _is_success(doc.get('ac46_passed')):
        raise QualityError('ac46_passed')
    if _is_success(doc.get('g0_passed')):
        raise QualityError('g0_passed')
    if _is_success(doc.get('phase_gate')) or doc.get('phase_gate') == 'passed':
        raise QualityError('ac46_passed')
    if _is_success(doc.get('live_soak')):
        raise QualityError('live_soak_claimed')
    for key, value in doc.items():
        if isinstance(key, str) and key.endswith('_passed') and value is not False:
            raise QualityError(key)
    for key in SOAK_ELAPSED_FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise QualityError(_soak_false_key_code(key))
    _reject_soak_invented_timings(doc)
    _check_ac_status(root, 'AC46', 'ac46_passed')
    git_sha = doc.get('git_sha')
    if not isinstance(git_sha, str) or len(git_sha) < 7:
        raise QualityError('missing_record_field')
    started = _parse_utc(doc.get('started_at_utc'))
    elapsed_at = _parse_utc(doc.get('elapsed_at_utc'))
    if elapsed_at < started + SOAK_WALL_CLOCK:
        raise QualityError('eight_hour_wall_clock_incomplete')
    start = _load_json(root / SOAK_START_REL)
    if doc.get('started_at_utc') != start.get('started_at_utc'):
        raise QualityError('missing_record_field')
    owned = doc.get('owned_pids')
    start_owned = start.get('owned_pids')
    if not isinstance(owned, list) or not owned:
        raise QualityError('missing_record_field')
    if not isinstance(start_owned, list) or list(owned) != list(start_owned):
        raise QualityError('eight_hour_wall_clock_incomplete')
    for pid in owned:
        if not isinstance(pid, int) or pid <= 0:
            raise QualityError('missing_record_field')
    return {
        'document_kind': SOAK_ELAPSED_KIND,
        'soak_elapsed_capture': SOAK_ELAPSED_REL,
        'soak_start_capture': SOAK_START_REL,
        'product_ui_started': True,
        'eight_hour_soak_executed': True,
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
        'started_at_utc': doc.get('started_at_utc'),
        'elapsed_at_utc': doc.get('elapsed_at_utc'),
    }


def soak_start_app_pid(doc: dict[str, Any]) -> int:
    """Product-UI PID from a soak START document. Identity-loose; not a frozen PID."""
    commands = doc.get('commands')
    if not isinstance(commands, list):
        raise QualityError('missing_record_field')
    for item in commands:
        if not isinstance(item, dict) or item.get('role') != 'product_ui':
            continue
        pid = item.get('pid')
        if isinstance(pid, int) and pid > 0:
            return pid
    raise QualityError('missing_record_field')


SOAK_WORKING_SET_REQUIRED_KEYS = (
    'document_kind', 'result', 'live_working_set', 'ac29_passed',
    'ac46_passed', 'eight_hour_soak_executed', 'soak_hours', 'pid',
    'started_at_utc', 'sampled_at_utc', 'working_set_bytes', 'git_sha',
    'start_capture', 'herdr_executed', 'g0_passed', 'invented_timings',
    'one_quarter_pane_lab', 'open_close_100', 'derive_process_memory_from_q_p',
    'mib_bytes', 'sampler_added_to_start_owned_pids',
)
SOAK_WORKING_SET_FALSE_KEYS = (
    'live_working_set', 'ac29_passed', 'ac46_passed',
    'eight_hour_soak_executed', 'live_soak', 'herdr_executed',
    'invented_timings', 'g0_passed', 'one_quarter_pane_lab',
    'open_close_100', 'derive_process_memory_from_q_p',
    'sampler_added_to_start_owned_pids',
)
SOAK_WORKING_SET_FORBIDDEN_TIMING_KEYS = (
    'soak_hours', 'p95_ms', 'visible_pixel_ms', 'parser_consumed_ms',
    'q_p_bytes', 'cycle_count', 'disconnect_switch_count', 'resize_rate',
    'visible_pane_count',
)


def _working_set_false_key_code(key: str) -> str:
    if key in {
        'ac29_passed', 'ac46_passed', 'g0_passed', 'eight_hour_soak_executed',
    }:
        return key
    if key == 'live_working_set':
        return 'live_working_set_claimed'
    if key == 'live_soak':
        return 'live_soak_claimed'
    if key == 'one_quarter_pane_lab':
        return 'one_quarter_pane_lab_claimed'
    if key == 'open_close_100':
        return 'open_close_100_claimed'
    if key == 'sampler_added_to_start_owned_pids':
        return 'sampler_added_to_start_owned_pids'
    return key


def _reject_soak_working_set_false_keys(doc: dict[str, Any]) -> None:
    for key in SOAK_WORKING_SET_FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise QualityError(_working_set_false_key_code(key))


def _reject_soak_working_set_invented(doc: Any) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key in SOAK_WORKING_SET_FORBIDDEN_TIMING_KEYS and value is not None:
                raise QualityError('invented_timings')
            _reject_soak_working_set_invented(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_soak_working_set_invented(item)


def validate_soak_working_set(root: Path) -> dict[str, Any] | None:
    """Optional soak-process working-set overlay. Missing is allowed. Not AC29."""
    root = Path(root)
    path = root / SOAK_WORKING_SET_REL
    if not path.is_file():
        return None
    doc = _load_json(path)
    for key in SOAK_WORKING_SET_REQUIRED_KEYS:
        if key not in doc:
            raise QualityError('missing_record_field')
    if doc.get('document_kind') != SOAK_WORKING_SET_KIND:
        raise QualityError('missing_record_field')
    if doc.get('result') != 'not_run' or _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if doc.get('l4_soak') not in (None, 'UNVERIFIED'):
        raise QualityError('l4_soak_claimed')
    if _is_success(doc.get('ac29_passed')):
        raise QualityError('ac29_passed')
    if _is_success(doc.get('ac46_passed')):
        raise QualityError('ac46_passed')
    if _is_success(doc.get('g0_passed')):
        raise QualityError('g0_passed')
    if _is_success(doc.get('phase_gate')) or doc.get('phase_gate') == 'passed':
        raise QualityError('ac29_passed')
    if _is_success(doc.get('live_working_set')):
        raise QualityError('live_working_set_claimed')
    if _is_success(doc.get('eight_hour_soak_executed')):
        raise QualityError('eight_hour_soak_executed')
    _reject_soak_working_set_false_keys(doc)
    _reject_soak_working_set_invented(doc)
    if doc.get('start_capture') != SOAK_START_REL:
        raise QualityError('missing_record_field')
    if doc.get('mib_bytes') != 1048576:
        raise QualityError('missing_record_field')
    working = doc.get('working_set_bytes')
    if not isinstance(working, int) or working <= 0:
        raise QualityError('missing_record_field')
    pid = doc.get('pid')
    if not isinstance(pid, int) or pid <= 0:
        raise QualityError('missing_record_field')
    private_bytes = doc.get('private_bytes')
    if private_bytes is not None and (
        not isinstance(private_bytes, int) or private_bytes < 0
    ):
        raise QualityError('missing_record_field')
    handle_count = doc.get('handle_count')
    if handle_count is not None and (
        not isinstance(handle_count, int) or handle_count < 0
    ):
        raise QualityError('missing_record_field')
    process_count = doc.get('process_count')
    if process_count is not None and process_count != 1:
        raise QualityError('invented_timings')
    git_sha = doc.get('git_sha')
    if not isinstance(git_sha, str) or len(git_sha) < 7:
        raise QualityError('missing_record_field')
    _parse_utc(doc.get('started_at_utc'))
    _parse_utc(doc.get('sampled_at_utc'))
    _check_ac_status(root, 'AC29', 'ac29_passed')
    _check_ac_status(root, 'AC46', 'ac46_passed')
    start = _load_json(root / SOAK_START_REL)
    if doc.get('started_at_utc') != start.get('started_at_utc'):
        raise QualityError('missing_record_field')
    start_pid = soak_start_app_pid(start)
    if pid != start_pid:
        raise QualityError('missing_record_field')
    owned = start.get('owned_pids')
    if not isinstance(owned, list) or start_pid not in owned:
        raise QualityError('missing_record_field')
    sampler_pid = doc.get('sampler_pid')
    if sampler_pid is not None:
        if not isinstance(sampler_pid, int) or sampler_pid <= 0:
            raise QualityError('missing_record_field')
        if sampler_pid in owned:
            raise QualityError('sampler_added_to_start_owned_pids')
    not_run = _load_json(root / LIVE_WORKING_SET_REL)
    if not_run.get('live_working_set') is not False:
        raise QualityError('live_working_set_claimed')
    if not_run.get('working_set_bytes') is not None:
        raise QualityError('invented_timings')
    if not_run.get('result') != 'not_run':
        raise QualityError('live_success_claimed')
    return {
        'document_kind': SOAK_WORKING_SET_KIND,
        'soak_working_set_capture': SOAK_WORKING_SET_REL,
        'soak_start_capture': SOAK_START_REL,
        'live_working_set_row': LIVE_WORKING_SET_REL,
        'recorded': True,
        'pid': pid,
        'working_set_bytes': working,
        'private_bytes': private_bytes,
        'handle_count': handle_count,
        'started_at_utc': doc.get('started_at_utc'),
        'sampled_at_utc': doc.get('sampled_at_utc'),
        'sampler_pid': sampler_pid if isinstance(sampler_pid, int) else None,
        'live_working_set': False,
        'ac29_passed': False,
        'ac46_passed': False,
        'eight_hour_soak_executed': False,
        'soak_hours': None,
        'one_quarter_pane_lab': False,
        'open_close_100': False,
        'result': 'not_run',
        'l4_soak': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'invented_timings': False,
        'git_sha': git_sha,
    }


def _dpi_matrix_false_key_code(key: str) -> str:
    if key in {'ac38_passed', 'g0_passed'}:
        return key
    if key == 'live_dpi':
        return 'live_dpi_claimed'
    if key == 'display_scale_changed_by_collector':
        return 'collector_changed_display_scale'
    if key == 'theme_matrix_executed':
        return 'theme_matrix_claimed'
    if key == 'high_contrast_executed':
        return 'high_contrast_claimed'
    if key == 'multi_monitor_executed':
        return 'multi_monitor_claimed'
    return key


def _dpi_matches_target(dpi: Any, percent: int) -> bool:
    expected = DPI_MATRIX_EXPECTED_DPI.get(percent)
    if expected is None or not isinstance(dpi, int):
        return False
    return abs(dpi - expected) <= DPI_MATRIX_DPI_TOLERANCE


def _dpi_matrix_sample_applied(sample: Any, percent: int) -> bool:
    if not isinstance(sample, dict):
        return False
    if sample.get('target_percent') != percent:
        return False
    if sample.get('applied') is not True:
        return False
    return _dpi_matches_target(sample.get('effective_dpi'), percent)


def validate_dpi_matrix(root: Path) -> dict[str, Any] | None:
    """Optional scale-only 100/150/200 overlay. Missing is allowed. Not AC38.

    Do not call _reject_dpi_pass_claims or _reject_hd033_pass_claims on this
    document: dpi_matrix_100_150_200_executed and
    display_scale_changed_by_this_record may be true here after a real
    DisplayConfig change. Catalog, L2, and the current-system-DPI pointer
    stay false.
    """
    root = Path(root)
    path = root / DPI_MATRIX_REL
    if not path.is_file():
        return None
    doc = _load_json(path)
    for key in DPI_MATRIX_REQUIRED_KEYS:
        if key not in doc:
            raise QualityError('missing_record_field')
    if doc.get('document_kind') != DPI_MATRIX_KIND:
        raise QualityError('missing_record_field')
    if doc.get('result') != 'not_run' or _is_success(doc.get('result')):
        raise QualityError('live_success_claimed')
    if doc.get('l3_dpi') != 'UNVERIFIED':
        raise QualityError('l3_dpi_claimed')
    if _is_success(doc.get('phase_gate')) or doc.get('phase_gate') == 'passed':
        raise QualityError('ac38_passed')
    for key, value in doc.items():
        if isinstance(key, str) and key.endswith('_passed') and value is not False:
            raise QualityError(key)
    for key in DPI_MATRIX_FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise QualityError(_dpi_matrix_false_key_code(key))
    if doc.get('resize_rate') is not None:
        raise QualityError('invented_timings')
    if 'soak_hours' in doc and doc.get('soak_hours') is not None:
        raise QualityError('invented_timings')
    git_sha = doc.get('git_sha')
    if not isinstance(git_sha, str) or len(git_sha) < 7:
        raise QualityError('missing_record_field')
    _parse_utc(doc.get('started_at_utc'))
    _parse_utc(doc.get('captured_at_utc'))
    original = doc.get('original_scale_percent')
    restored = doc.get('restored_scale_percent')
    if not isinstance(original, int) or original <= 0:
        raise QualityError('missing_record_field')
    if not isinstance(restored, int) or restored <= 0:
        raise QualityError('missing_record_field')
    samples = doc.get('samples')
    if not isinstance(samples, list):
        raise QualityError('missing_record_field')
    restore_ok = doc.get('restore_ok')
    if restore_ok is not True and restore_ok is not False:
        raise QualityError('missing_record_field')
    executed = doc.get('dpi_matrix_100_150_200_executed')
    if executed is not True and executed is not False:
        raise QualityError('missing_record_field')
    changed = doc.get('display_scale_changed_by_this_record')
    if changed is not True and changed is not False:
        raise QualityError('missing_record_field')
    applied = {
        percent: any(_dpi_matrix_sample_applied(sample, percent) for sample in samples)
        for percent in DPI_MATRIX_TARGETS
    }
    honest = (
        restore_ok is True
        and restored == original
        and all(applied[percent] for percent in DPI_MATRIX_TARGETS)
    )
    if executed is True:
        if not honest or changed is not True:
            raise QualityError('dpi_matrix_claimed')
    _check_ac_status(root, 'AC38', 'ac38_passed')
    pointer = _load_json(root / DPI_POINTER_REL) if (root / DPI_POINTER_REL).is_file() else None
    if pointer is not None:
        _reject_dpi_pass_claims(pointer)
        if pointer.get('dpi_matrix_100_150_200_executed') is not False:
            raise QualityError('dpi_matrix_claimed')
        if pointer.get('display_scale_changed_by_collector') is not False:
            raise QualityError('collector_changed_display_scale')
    live = _load_json(root / LIVE_DPI_REL) if (root / LIVE_DPI_REL).is_file() else None
    if live is not None:
        _reject_dpi_pass_claims(live)
        if live.get('dpi_matrix_100_150_200_executed') is not False:
            raise QualityError('dpi_matrix_claimed')
        if live.get('display_scale_changed_by_collector') is not False:
            raise QualityError('collector_changed_display_scale')
        if live.get('live_dpi') is not False:
            raise QualityError('live_dpi_claimed')
    return {
        'document_kind': DPI_MATRIX_KIND,
        'dpi_matrix_capture': DPI_MATRIX_REL,
        'recorded': True,
        'dpi_matrix_100_150_200_executed': executed is True,
        'display_scale_changed_by_this_record': changed is True,
        'restore_ok': restore_ok is True,
        'original_scale_percent': original,
        'restored_scale_percent': restored,
        'live_dpi': False,
        'ac38_passed': False,
        'theme_matrix_executed': False,
        'high_contrast_executed': False,
        'multi_monitor_executed': False,
        'result': 'not_run',
        'l3_dpi': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'invented_timings': False,
        'git_sha': git_sha,
    }
