#!/usr/bin/env python3
"""Record a scale-only 100/150/200 DisplayConfig matrix. Not AC38.

Default CLI validates the optional overlay JSON and does not change scale.
Pass --record on an interactive Windows desktop to apply 100/150/200 via
per-monitor DisplayConfig, sample effective DPI, spawn a short observe-mode
product UI at each applied scale, then restore the original scale. CI and
justfile must not invoke --record. A scale-only overlay is not theme,
multi-monitor, high-contrast, L3, live_dpi, or AC38. Tests must inject hooks
and must not call live DisplayConfig.
"""
from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone
from typing import Any

SCRIPTS = Path(__file__).resolve().parent
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))
from herddesk_g0.quality import (
    DPI_MATRIX_DPI_TOLERANCE,
    DPI_MATRIX_EXPECTED_DPI,
    DPI_MATRIX_KIND,
    DPI_MATRIX_REL,
    DPI_MATRIX_TARGETS,
    QualityError,
    collect_dpi_overlay,
    system_dpi,
    validate_dpi_matrix,
)

ROOT = Path(__file__).resolve().parents[1]
UI_FRAMEWORK = 'net10.0-windows10.0.19041.0'
PINNED_DOTNET_ROOT = Path(r'C:\Users\lyh\AppData\Local\herddesk-dotnet')
UI_EXE_NAME = 'HerdDesk.App.exe'
WINDOW_WAIT_SEC = 45
SCALE_WAIT_SEC = 12
SCALE_TABLE = (100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500)
QDC_ONLY_ACTIVE_PATHS = 2
DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1
DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE = -3
DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE = -4
DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
MDT_EFFECTIVE_DPI = 0
MONITORINFOF_PRIMARY = 1
CCHDEVICENAME = 32
ERROR_SUCCESS = 0
ERROR_INSUFFICIENT_BUFFER = 122
PROBE_JSON = 'probe-results/hd033-dpi-matrix.json'


class LUID(ctypes.Structure):
    _fields_ = (('LowPart', wintypes.DWORD), ('HighPart', wintypes.LONG))


class DISPLAYCONFIG_RATIONAL(ctypes.Structure):
    _fields_ = (('Numerator', wintypes.UINT), ('Denominator', wintypes.UINT))


class DISPLAYCONFIG_PATH_SOURCE_INFO(ctypes.Structure):
    _fields_ = (
        ('adapterId', LUID),
        ('id', wintypes.UINT),
        ('modeInfoIdx', wintypes.UINT),
        ('statusFlags', wintypes.UINT),
    )


class DISPLAYCONFIG_PATH_TARGET_INFO(ctypes.Structure):
    _fields_ = (
        ('adapterId', LUID),
        ('id', wintypes.UINT),
        ('modeInfoIdx', wintypes.UINT),
        ('outputTechnology', ctypes.c_int32),
        ('rotation', ctypes.c_int32),
        ('scaling', ctypes.c_int32),
        ('refreshRate', DISPLAYCONFIG_RATIONAL),
        ('scanLineOrdering', ctypes.c_int32),
        ('targetAvailable', wintypes.BOOL),
        ('statusFlags', wintypes.UINT),
    )


class DISPLAYCONFIG_PATH_INFO(ctypes.Structure):
    _fields_ = (
        ('sourceInfo', DISPLAYCONFIG_PATH_SOURCE_INFO),
        ('targetInfo', DISPLAYCONFIG_PATH_TARGET_INFO),
        ('flags', wintypes.UINT),
    )


class DISPLAYCONFIG_MODE_INFO(ctypes.Structure):
    _fields_ = (
        ('infoType', wintypes.UINT),
        ('id', wintypes.UINT),
        ('adapterId', LUID),
        ('_union', ctypes.c_byte * 48),
    )


class DISPLAYCONFIG_DEVICE_INFO_HEADER(ctypes.Structure):
    _fields_ = (
        ('type', ctypes.c_int32),
        ('size', wintypes.UINT),
        ('adapterId', LUID),
        ('id', wintypes.UINT),
    )


class DISPLAYCONFIG_SOURCE_DPI_SCALE_GET(ctypes.Structure):
    _fields_ = (
        ('header', DISPLAYCONFIG_DEVICE_INFO_HEADER),
        ('minScaleRel', ctypes.c_int32),
        ('curScaleRel', ctypes.c_int32),
        ('maxScaleRel', ctypes.c_int32),
    )


class DISPLAYCONFIG_SOURCE_DPI_SCALE_SET(ctypes.Structure):
    _fields_ = (
        ('header', DISPLAYCONFIG_DEVICE_INFO_HEADER),
        ('scaleRel', ctypes.c_int32),
    )


class DISPLAYCONFIG_SOURCE_DEVICE_NAME(ctypes.Structure):
    _fields_ = (
        ('header', DISPLAYCONFIG_DEVICE_INFO_HEADER),
        ('viewGdiDeviceName', ctypes.c_wchar * CCHDEVICENAME),
    )


class RECT(ctypes.Structure):
    _fields_ = (
        ('left', ctypes.c_int32),
        ('top', ctypes.c_int32),
        ('right', ctypes.c_int32),
        ('bottom', ctypes.c_int32),
    )


class MONITORINFOEXW(ctypes.Structure):
    _fields_ = (
        ('cbSize', wintypes.DWORD),
        ('rcMonitor', RECT),
        ('rcWork', RECT),
        ('dwFlags', wintypes.DWORD),
        ('szDevice', ctypes.c_wchar * CCHDEVICENAME),
    )


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')


def _git_sha(root: Path) -> str:
    completed = subprocess.run(
        ['git', 'rev-parse', 'HEAD'],
        cwd=str(root),
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        check=False,
    )
    sha = (completed.stdout or '').strip()
    if completed.returncode != 0 or len(sha) < 7:
        raise QualityError('missing_record_field')
    return sha


def _dotnet_exe() -> Path:
    pin = PINNED_DOTNET_ROOT / 'dotnet.exe'
    if pin.is_file():
        return pin
    root = os.environ.get('DOTNET_ROOT')
    if root:
        candidate = Path(root) / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
        if candidate.is_file():
            return candidate
    which = shutil.which('dotnet')
    if which:
        return Path(which)
    raise QualityError('missing_record_field')


def _dotnet_env() -> dict[str, str]:
    env = os.environ.copy()
    if (PINNED_DOTNET_ROOT / 'dotnet.exe').is_file():
        env['DOTNET_ROOT'] = str(PINNED_DOTNET_ROOT)
        env['PATH'] = str(PINNED_DOTNET_ROOT) + os.pathsep + env.get('PATH', '')
        env['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    elif env.get('DOTNET_ROOT'):
        env['DOTNET_MULTILEVEL_LOOKUP'] = env.get('DOTNET_MULTILEVEL_LOOKUP') or '0'
        env['PATH'] = env['DOTNET_ROOT'] + os.pathsep + env.get('PATH', '')
    return env


def _ui_exe(root: Path) -> Path:
    exe = (
        Path(root)
        / 'src'
        / 'HerdDesk.App'
        / 'bin'
        / 'Release'
        / UI_FRAMEWORK
        / 'win-x64'
        / UI_EXE_NAME
    )
    if not exe.is_file():
        raise QualityError('product_ui_not_started')
    return exe


def _snap_percent(percent: int) -> int:
    return min(SCALE_TABLE, key=lambda item: abs(item - percent))


def _percent_from_dpi(dpi: int | None) -> int | None:
    if not isinstance(dpi, int) or dpi <= 0:
        return None
    return _snap_percent(int(round(dpi / 96.0 * 100)))


def _set_dpi_awareness() -> bool:
    if os.name != 'nt':
        return False
    try:
        user32 = ctypes.WinDLL('user32', use_last_error=True)
        fn = user32.SetProcessDpiAwarenessContext
        fn.argtypes = [ctypes.c_void_p]
        fn.restype = wintypes.BOOL
        return bool(fn(ctypes.c_void_p(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)))
    except (AttributeError, OSError):
        return False


def _user32():
    user32 = ctypes.WinDLL('user32', use_last_error=True)
    user32.GetDisplayConfigBufferSizes.argtypes = [
        wintypes.UINT,
        ctypes.POINTER(wintypes.UINT),
        ctypes.POINTER(wintypes.UINT),
    ]
    user32.GetDisplayConfigBufferSizes.restype = ctypes.c_long
    user32.QueryDisplayConfig.argtypes = [
        wintypes.UINT,
        ctypes.POINTER(wintypes.UINT),
        ctypes.POINTER(DISPLAYCONFIG_PATH_INFO),
        ctypes.POINTER(wintypes.UINT),
        ctypes.POINTER(DISPLAYCONFIG_MODE_INFO),
        ctypes.c_void_p,
    ]
    user32.QueryDisplayConfig.restype = ctypes.c_long
    user32.DisplayConfigGetDeviceInfo.argtypes = [ctypes.c_void_p]
    user32.DisplayConfigGetDeviceInfo.restype = ctypes.c_long
    user32.DisplayConfigSetDeviceInfo.argtypes = [ctypes.c_void_p]
    user32.DisplayConfigSetDeviceInfo.restype = ctypes.c_long
    user32.GetMonitorInfoW.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    user32.GetMonitorInfoW.restype = wintypes.BOOL
    user32.EnumDisplayMonitors.argtypes = [
        ctypes.c_void_p,
        ctypes.c_void_p,
        ctypes.c_void_p,
        wintypes.LPARAM,
    ]
    user32.EnumDisplayMonitors.restype = wintypes.BOOL
    return user32


def _enum_hmonitors() -> list[int]:
    if os.name != 'nt':
        return []
    user32 = _user32()
    found: list[int] = []
    enum_proc = ctypes.WINFUNCTYPE(
        wintypes.BOOL, ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(RECT), wintypes.LPARAM
    )

    def callback(hmonitor, _hdc, _rect, _lparam):
        found.append(int(hmonitor))
        return True

    user32.EnumDisplayMonitors(None, None, enum_proc(callback), 0)
    return found


def _monitor_info(hmonitor: int) -> dict[str, Any] | None:
    if os.name != 'nt' or not hmonitor:
        return None
    user32 = _user32()
    info = MONITORINFOEXW()
    info.cbSize = ctypes.sizeof(MONITORINFOEXW)
    if not user32.GetMonitorInfoW(ctypes.c_void_p(hmonitor), ctypes.byref(info)):
        return None
    return {
        'hmonitor': hmonitor,
        'device': info.szDevice,
        'primary': bool(info.dwFlags & MONITORINFOF_PRIMARY),
    }


def _primary_hmonitor() -> int | None:
    for handle in _enum_hmonitors():
        info = _monitor_info(handle)
        if info and info.get('primary'):
            return handle
    handles = _enum_hmonitors()
    return handles[0] if handles else None


def _hmonitor_for_device(device_name: str) -> int | None:
    wanted = (device_name or '').strip().upper()
    for handle in _enum_hmonitors():
        info = _monitor_info(handle)
        if not info:
            continue
        device = str(info.get('device') or '').strip().upper()
        if device and device == wanted:
            return handle
    return _primary_hmonitor()


def _dpi_for_monitor(hmonitor: int | None) -> int | None:
    if os.name != 'nt' or not hmonitor:
        return None
    try:
        shcore = ctypes.WinDLL('shcore', use_last_error=True)
        shcore.GetDpiForMonitor.argtypes = [
            ctypes.c_void_p,
            ctypes.c_int,
            ctypes.POINTER(wintypes.UINT),
            ctypes.POINTER(wintypes.UINT),
        ]
        shcore.GetDpiForMonitor.restype = ctypes.c_long
        dpi_x = wintypes.UINT(0)
        dpi_y = wintypes.UINT(0)
        status = shcore.GetDpiForMonitor(
            ctypes.c_void_p(hmonitor), MDT_EFFECTIVE_DPI, ctypes.byref(dpi_x), ctypes.byref(dpi_y)
        )
        if status != 0:
            return None
        dpi = int(dpi_x.value)
        return dpi if dpi > 0 else None
    except (AttributeError, OSError, ValueError):
        return None


def _query_paths() -> list[DISPLAYCONFIG_PATH_INFO]:
    user32 = _user32()
    path_count = wintypes.UINT(0)
    mode_count = wintypes.UINT(0)
    status = user32.GetDisplayConfigBufferSizes(
        QDC_ONLY_ACTIVE_PATHS, ctypes.byref(path_count), ctypes.byref(mode_count)
    )
    if status != ERROR_SUCCESS or path_count.value == 0:
        raise QualityError('missing_record_field')
    paths = (DISPLAYCONFIG_PATH_INFO * path_count.value)()
    modes = (DISPLAYCONFIG_MODE_INFO * max(mode_count.value, 1))()
    path_count = wintypes.UINT(len(paths))
    mode_count = wintypes.UINT(len(modes))
    status = user32.QueryDisplayConfig(
        QDC_ONLY_ACTIVE_PATHS,
        ctypes.byref(path_count),
        paths,
        ctypes.byref(mode_count),
        modes,
        None,
    )
    if status != ERROR_SUCCESS or path_count.value == 0:
        raise QualityError('missing_record_field')
    return list(paths[: path_count.value])


def _source_device_name(adapter: LUID, source_id: int) -> str:
    user32 = _user32()
    name = DISPLAYCONFIG_SOURCE_DEVICE_NAME()
    name.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
    name.header.size = ctypes.sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME)
    name.header.adapterId = adapter
    name.header.id = source_id
    status = user32.DisplayConfigGetDeviceInfo(ctypes.byref(name.header))
    if status != ERROR_SUCCESS:
        return ''
    return name.viewGdiDeviceName or ''


def _get_dpi_scale(adapter: LUID, source_id: int) -> DISPLAYCONFIG_SOURCE_DPI_SCALE_GET:
    user32 = _user32()
    get = DISPLAYCONFIG_SOURCE_DPI_SCALE_GET()
    get.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE
    get.header.size = ctypes.sizeof(DISPLAYCONFIG_SOURCE_DPI_SCALE_GET)
    get.header.adapterId = adapter
    get.header.id = source_id
    status = user32.DisplayConfigGetDeviceInfo(ctypes.byref(get.header))
    if status != ERROR_SUCCESS:
        raise QualityError('missing_record_field')
    return get


def _set_dpi_scale(adapter: LUID, source_id: int, scale_rel: int) -> bool:
    user32 = _user32()
    payload = DISPLAYCONFIG_SOURCE_DPI_SCALE_SET()
    payload.header.type = DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE
    payload.header.size = ctypes.sizeof(DISPLAYCONFIG_SOURCE_DPI_SCALE_SET)
    payload.header.adapterId = adapter
    payload.header.id = source_id
    payload.scaleRel = int(scale_rel)
    status = user32.DisplayConfigSetDeviceInfo(ctypes.byref(payload.header))
    return status == ERROR_SUCCESS


def _available_percents(recommended_idx: int, min_rel: int, max_rel: int) -> list[int]:
    found: list[int] = []
    for rel in range(min_rel, max_rel + 1):
        idx = recommended_idx + rel
        if 0 <= idx < len(SCALE_TABLE):
            found.append(SCALE_TABLE[idx])
    return found


def _live_read_display() -> dict[str, Any]:
    paths = _query_paths()
    chosen: DISPLAYCONFIG_PATH_INFO | None = None
    device_name = ''
    hmonitor = _primary_hmonitor()
    primary_info = _monitor_info(hmonitor) if hmonitor else None
    primary_device = str((primary_info or {}).get('device') or '').strip().upper()
    for path in paths:
        name = _source_device_name(path.sourceInfo.adapterId, path.sourceInfo.id)
        if primary_device and name.strip().upper() == primary_device:
            chosen = path
            device_name = name
            break
    if chosen is None:
        chosen = paths[0]
        device_name = _source_device_name(chosen.sourceInfo.adapterId, chosen.sourceInfo.id)
        hmonitor = _hmonitor_for_device(device_name) or hmonitor
    else:
        matched = _hmonitor_for_device(device_name)
        if matched:
            hmonitor = matched
    scale = _get_dpi_scale(chosen.sourceInfo.adapterId, chosen.sourceInfo.id)
    dpi = _dpi_for_monitor(hmonitor) if hmonitor else None
    if not isinstance(dpi, int):
        dpi = system_dpi()
    percent = _percent_from_dpi(dpi)
    if not isinstance(percent, int):
        raise QualityError('missing_record_field')
    cur_idx = SCALE_TABLE.index(percent) if percent in SCALE_TABLE else None
    if cur_idx is None:
        raise QualityError('missing_record_field')
    recommended_idx = cur_idx - int(scale.curScaleRel)
    available = _available_percents(
        recommended_idx, int(scale.minScaleRel), int(scale.maxScaleRel)
    )
    recommended_percent = (
        SCALE_TABLE[recommended_idx]
        if 0 <= recommended_idx < len(SCALE_TABLE)
        else percent
    )
    return {
        'adapter': chosen.sourceInfo.adapterId,
        'source_id': int(chosen.sourceInfo.id),
        'device_name': device_name,
        'hmonitor': hmonitor,
        'min_scale_rel': int(scale.minScaleRel),
        'cur_scale_rel': int(scale.curScaleRel),
        'max_scale_rel': int(scale.maxScaleRel),
        'recommended_idx': recommended_idx,
        'recommended_percent': recommended_percent,
        'available_scale_percents': available,
        'scale_percent': percent,
        'effective_dpi': dpi,
        'system_dpi': system_dpi(),
    }


def _live_apply_scale(display: dict[str, Any], percent: int) -> bool:
    available = display.get('available_scale_percents') or []
    if percent not in available:
        return False
    recommended_idx = display.get('recommended_idx')
    if not isinstance(recommended_idx, int):
        return False
    try:
        scale_rel = SCALE_TABLE.index(percent) - recommended_idx
    except ValueError:
        return False
    min_rel = display.get('min_scale_rel')
    max_rel = display.get('max_scale_rel')
    if not isinstance(min_rel, int) or not isinstance(max_rel, int):
        return False
    if scale_rel < min_rel or scale_rel > max_rel:
        return False
    adapter = display.get('adapter')
    source_id = display.get('source_id')
    if adapter is None or not isinstance(source_id, int):
        return False
    return _set_dpi_scale(adapter, source_id, scale_rel)


def _live_restore_scale(display: dict[str, Any], original_rel: int) -> bool:
    adapter = display.get('adapter')
    source_id = display.get('source_id')
    if adapter is None or not isinstance(source_id, int):
        return False
    return _set_dpi_scale(adapter, source_id, original_rel)


def _expected_dpi(percent: int) -> int:
    if percent in DPI_MATRIX_EXPECTED_DPI:
        return DPI_MATRIX_EXPECTED_DPI[percent]
    return int(round(96 * percent / 100.0))


def _wait_effective_dpi(
    display: dict[str, Any],
    target_percent: int,
    *,
    timeout_sec: float = SCALE_WAIT_SEC,
    read_display: Any = None,
) -> dict[str, Any]:
    expected = _expected_dpi(target_percent)
    deadline = time.time() + timeout_sec
    last_dpi = None
    last_percent = None
    while True:
        if read_display is not None:
            current = read_display()
            last_dpi = current.get('effective_dpi')
            last_percent = current.get('scale_percent')
        else:
            hmonitor = display.get('hmonitor') or _primary_hmonitor()
            last_dpi = _dpi_for_monitor(hmonitor) if hmonitor else system_dpi()
            last_percent = _percent_from_dpi(last_dpi)
        applied = (
            last_percent == target_percent
            or (
                isinstance(last_dpi, int)
                and abs(last_dpi - expected) <= DPI_MATRIX_DPI_TOLERANCE
            )
        )
        if applied:
            return {
                'applied': True,
                'effective_dpi': last_dpi,
                'scale_percent': last_percent,
            }
        if time.time() >= deadline:
            return {
                'applied': False,
                'effective_dpi': last_dpi,
                'scale_percent': last_percent,
            }
        time.sleep(0.2)


def _visible_windows() -> list[dict[str, Any]]:
    if os.name != 'nt':
        return []
    try:
        user32 = _user32()
        enum_proc = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
        found: list[dict[str, Any]] = []

        def callback(hwnd, _lparam):
            if not user32.IsWindowVisible(hwnd):
                return True
            length = user32.GetWindowTextLengthW(hwnd)
            buf = ctypes.create_unicode_buffer(length + 1)
            user32.GetWindowTextW(hwnd, buf, length + 1)
            title = buf.value or ''
            pid = wintypes.DWORD(0)
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            if title:
                found.append({'hwnd': int(hwnd), 'title': title, 'pid': int(pid.value)})
            return True

        user32.EnumWindows(enum_proc(callback), 0)
        return found
    except OSError:
        return []


def _client_size(hwnd: int) -> tuple[int | None, int | None]:
    if os.name != 'nt' or not hwnd:
        return None, None
    try:
        user32 = _user32()
        rect = RECT()
        if not user32.GetClientRect(ctypes.c_void_p(hwnd), ctypes.byref(rect)):
            return None, None
        return int(rect.right - rect.left), int(rect.bottom - rect.top)
    except (OSError, ValueError):
        return None, None


def _kill_owned_pid(pid: int) -> None:
    if os.name != 'nt' or pid <= 0:
        return
    subprocess.run(
        ['taskkill', '/PID', str(pid), '/T', '/F'],
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        check=False,
    )


def _ensure_ui_exe(root: Path) -> Path | None:
    try:
        return _ui_exe(root)
    except QualityError:
        pass
    try:
        dotnet = _dotnet_exe()
    except QualityError:
        return None
    build = subprocess.run(
        [
            str(dotnet),
            'build',
            str(root / 'src' / 'HerdDesk.App' / 'HerdDesk.App.csproj'),
            '--framework',
            UI_FRAMEWORK,
            '--configuration',
            'Release',
        ],
        cwd=str(root),
        env=_dotnet_env(),
        capture_output=True,
        check=False,
        timeout=300,
    )
    if build.returncode != 0:
        return None
    try:
        return _ui_exe(root)
    except QualityError:
        return None


def _spawn_product_ui(root: Path, percent: int) -> dict[str, Any]:
    sample: dict[str, Any] = {
        'product_ui_started': False,
        'window_seen': False,
        'pid': None,
        'title': None,
        'client_width': None,
        'client_height': None,
        'blocker': None,
    }
    if os.name != 'nt':
        sample['blocker'] = 'skipped_non_windows'
        return sample
    exe = _ensure_ui_exe(root)
    if exe is None:
        sample['blocker'] = 'product_ui_not_started'
        return sample
    temp_root = tempfile.mkdtemp(prefix=f'herddesk-hd033-dpi-{percent}-')
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    stdout_path = probe / f'hd033-dpi-ui-{percent}-stdout.txt'
    stderr_path = probe / f'hd033-dpi-ui-{percent}-stderr.txt'
    stdout_handle = stdout_path.open('wb')
    stderr_handle = stderr_path.open('wb')
    proc: subprocess.Popen[bytes] | None = None
    owned_pid: int | None = None
    try:
        proc = subprocess.Popen(
            [str(exe), '--ui', temp_root],
            cwd=str(exe.parent),
            env=_dotnet_env(),
            stdout=stdout_handle,
            stderr=stderr_handle,
            stdin=subprocess.DEVNULL,
        )
        owned_pid = proc.pid
        sample['pid'] = owned_pid
        deadline = time.time() + WINDOW_WAIT_SEC
        windows: list[dict[str, Any]] = []
        while time.time() < deadline:
            if proc.poll() is not None:
                sample['blocker'] = 'product_ui_exited_before_window'
                break
            windows = [
                item
                for item in _visible_windows()
                if item.get('pid') == owned_pid or str(item.get('title') or '') == 'HerdDesk'
            ]
            owned_windows = [item for item in windows if item.get('pid') == owned_pid]
            if owned_windows:
                windows = owned_windows
            if windows:
                sample['product_ui_started'] = True
                sample['window_seen'] = True
                chosen = windows[0]
                sample['title'] = chosen.get('title')
                sample['pid'] = chosen.get('pid') or owned_pid
                width, height = _client_size(int(chosen.get('hwnd') or 0))
                sample['client_width'] = width
                sample['client_height'] = height
                break
            time.sleep(0.4)
        if not sample['window_seen'] and proc.poll() is None and owned_pid:
            sample['product_ui_started'] = True
            sample['blocker'] = sample.get('blocker') or 'product_ui_window_not_seen'
    except OSError as exc:
        sample['blocker'] = f'oserror_{getattr(exc, "winerror", None) or exc.errno}'
    finally:
        stdout_handle.close()
        stderr_handle.close()
        if owned_pid:
            _kill_owned_pid(owned_pid)
        if proc is not None:
            try:
                proc.wait(timeout=8)
            except subprocess.TimeoutExpired:
                proc.kill()
        try:
            shutil.rmtree(temp_root, ignore_errors=True)
        except OSError:
            pass
    return sample


def _write_overlay(root: Path, doc: dict[str, Any]) -> Path:
    dest = root / DPI_MATRIX_REL
    dest.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(doc, ensure_ascii=False, indent=2) + '\n'
    dest.write_text(text, encoding='utf-8')
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    (probe / 'hd033-dpi-matrix.json').write_text(text, encoding='utf-8')
    return dest


def record(
    root: Path,
    *,
    set_dpi_awareness: Any = None,
    read_display: Any = None,
    apply_scale: Any = None,
    wait_effective_dpi: Any = None,
    restore_scale: Any = None,
    spawn_ui: Any = None,
    git_sha: str | None = None,
    now: str | None = None,
    started: str | None = None,
) -> dict[str, Any]:
    """Apply 100/150/200 if available, sample, restore. Injected hooks skip DisplayConfig.

    Linux record() without hooks fail-closes. Tests must not change the
    developer display.
    """
    root = Path(root)
    hooks = (
        read_display is not None
        and apply_scale is not None
        and restore_scale is not None
    )
    if os.name != 'nt' and not hooks:
        raise QualityError('missing_record_field')
    started_at = started if started is not None else _utc_now()
    captured_at = now if now is not None else started_at
    sha = git_sha if git_sha is not None else _git_sha(root)
    awareness = set_dpi_awareness if set_dpi_awareness is not None else _set_dpi_awareness
    reader = read_display if read_display is not None else _live_read_display
    applier = apply_scale if apply_scale is not None else None
    restorer = restore_scale if restore_scale is not None else None
    waiter = wait_effective_dpi
    spawner = spawn_ui
    if not hooks:
        awareness()
    display = reader()
    if not isinstance(display, dict):
        raise QualityError('missing_record_field')
    original_percent = display.get('scale_percent')
    original_rel = display.get('cur_scale_rel')
    original_dpi = display.get('effective_dpi')
    original_system = display.get('system_dpi')
    available = display.get('available_scale_percents')
    if not isinstance(original_percent, int) or original_percent <= 0:
        raise QualityError('missing_record_field')
    if not isinstance(original_rel, int):
        raise QualityError('missing_record_field')
    if not isinstance(available, list):
        available = []
    available_ints = [item for item in available if isinstance(item, int)]
    samples: list[dict[str, Any]] = []
    changed = False
    restore_ok = False
    blocker: str | None = None
    restored_percent = original_percent
    restored_dpi = original_dpi
    restored_system = original_system
    try:
        for percent in DPI_MATRIX_TARGETS:
            sample: dict[str, Any] = {
                'target_percent': percent,
                'expected_effective_dpi': DPI_MATRIX_EXPECTED_DPI[percent],
                'available': percent in available_ints,
                'applied': False,
                'effective_dpi': None,
                'scale_percent': None,
                'scale_rel': None,
                'product_ui': None,
            }
            if percent not in available_ints:
                sample['blocker'] = 'scale_not_available'
                samples.append(sample)
                continue
            recommended_idx = display.get('recommended_idx')
            if isinstance(recommended_idx, int) and percent in SCALE_TABLE:
                sample['scale_rel'] = SCALE_TABLE.index(percent) - recommended_idx
            if hooks:
                ok = bool(applier(percent, sample.get('scale_rel')))
            else:
                ok = _live_apply_scale(display, percent)
            if ok:
                changed = True
            if waiter is not None:
                waited = waiter(percent)
            elif hooks:
                current = reader()
                waited = {
                    'applied': current.get('scale_percent') == percent,
                    'effective_dpi': current.get('effective_dpi'),
                    'scale_percent': current.get('scale_percent'),
                }
            else:
                waited = _wait_effective_dpi(display, percent)
            sample['applied'] = bool(ok and waited.get('applied') is True)
            sample['effective_dpi'] = waited.get('effective_dpi')
            sample['scale_percent'] = waited.get('scale_percent')
            if not sample['applied']:
                sample['blocker'] = sample.get('blocker') or 'scale_did_not_apply'
                samples.append(sample)
                continue
            if spawner is not None:
                sample['product_ui'] = spawner(percent)
            elif hooks:
                sample['product_ui'] = None
            else:
                sample['product_ui'] = _spawn_product_ui(root, percent)
            samples.append(sample)
    finally:
        if hooks:
            restore_ok = bool(restorer(original_rel))
            current = reader()
            restored_percent = current.get('scale_percent')
            restored_dpi = current.get('effective_dpi')
            restored_system = current.get('system_dpi')
        else:
            restore_ok = _live_restore_scale(display, original_rel)
            waited = _wait_effective_dpi(display, original_percent)
            restored_percent = waited.get('scale_percent')
            restored_dpi = waited.get('effective_dpi')
            restored_system = system_dpi()
            if (not restore_ok) or restored_percent != original_percent:
                restore_ok = _live_restore_scale(display, original_rel)
                waited = _wait_effective_dpi(display, original_percent)
                restored_percent = waited.get('scale_percent')
                restored_dpi = waited.get('effective_dpi')
                restored_system = system_dpi()
            if restore_ok and restored_percent != original_percent:
                restore_ok = False
        if not restore_ok:
            blocker = 'restore_failed'
    if not isinstance(restored_percent, int) or restored_percent <= 0:
        restore_ok = False
        restored_percent = original_percent if isinstance(original_percent, int) else 100
        blocker = blocker or 'restore_failed'
    applied_ok = {
        percent: any(
            item.get('target_percent') == percent
            and item.get('applied') is True
            and isinstance(item.get('effective_dpi'), int)
            and abs(item['effective_dpi'] - DPI_MATRIX_EXPECTED_DPI[percent])
            <= DPI_MATRIX_DPI_TOLERANCE
            for item in samples
        )
        for percent in DPI_MATRIX_TARGETS
    }
    executed = (
        restore_ok
        and restored_percent == original_percent
        and all(applied_ok[percent] for percent in DPI_MATRIX_TARGETS)
    )
    if not restore_ok:
        executed = False
    ui_started = any(
        isinstance(item.get('product_ui'), dict)
        and item['product_ui'].get('product_ui_started') is True
        for item in samples
    )
    captured_at = now if now is not None else _utc_now()
    doc: dict[str, Any] = {
        'document_kind': DPI_MATRIX_KIND,
        'template': False,
        'capture_id': f'hd033-live-dpi-matrix-{captured_at[:10]}',
        'kind': 'live_dpi_scale_matrix',
        'started_at_utc': started_at,
        'captured_at_utc': captured_at,
        'operator_scope': 'scale_only_100_150_200_not_theme_monitor_high_contrast_not_ac38',
        'git_sha': sha,
        'platform': {
            'sys_platform': sys.platform,
            'system': platform.system(),
            'release': platform.release(),
            'version': platform.version(),
            'machine': platform.machine(),
            'platform': platform.platform(),
        },
        'result': 'not_run',
        'evidence_level': 'scale_only_matrix',
        'live_status': 'UNVERIFIED',
        'live_dpi': False,
        'ac38_passed': False,
        'ac27_passed': False,
        'ac28_passed': False,
        'ac29_passed': False,
        'ac37_passed': False,
        'ac46_passed': False,
        'l3_dpi': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'winui_admitted': False,
        'invented_timings': False,
        'dpi_matrix_100_150_200_executed': executed,
        'display_scale_changed_by_this_record': changed,
        'display_scale_changed_by_collector': False,
        'theme_matrix_executed': False,
        'high_contrast_executed': False,
        'multi_monitor_executed': False,
        'resize_rate': None,
        'soak_hours': None,
        'original_scale_percent': original_percent,
        'original_effective_dpi': original_dpi,
        'original_system_dpi': original_system,
        'original_scale_rel': original_rel,
        'restored_scale_percent': restored_percent,
        'restored_effective_dpi': restored_dpi,
        'restored_system_dpi': restored_system,
        'restore_ok': restore_ok,
        'available_scale_percents': available_ints,
        'recommended_percent': display.get('recommended_percent'),
        'min_scale_rel': display.get('min_scale_rel'),
        'max_scale_rel': display.get('max_scale_rel'),
        'samples': samples,
        'product_ui_started': ui_started,
        'raw_gitignored_path': PROBE_JSON,
        'committed_raw': True,
        'blocker': blocker,
        'limitations': [
            'Scale-only DisplayConfig 100/150/200 overlay on this interactive desktop.',
            'dpi_matrix_100_150_200_executed may be true on this file only after 100, 150, and 200 were applied and original scale was restored.',
            'This is not AC38. Theme, high-contrast, multi-monitor, font scaling, and resize-jitter evidence were not run.',
            'live_dpi stays false. l3_dpi stays UNVERIFIED. Catalog, L2, and dpi-overlay-pointer keep matrix flags false.',
            'display_scale_changed_by_this_record may be true here because scale was changed. The pointer collector still does not change scale.',
            'Product UI --ui at each applied scale is observe-mode with a unique data-root. Owned PID only is terminated. Not winui_admitted.',
            'herdr_executed stays false. Hosted CI is not an interactive desktop and must not run --record.',
        ],
    }
    _write_overlay(root, doc)
    report = validate_dpi_matrix(root)
    if report is None:
        raise QualityError('missing_record_field')
    return doc


def _unrecorded_report() -> dict[str, Any]:
    return {
        'document_kind': DPI_MATRIX_KIND,
        'dpi_matrix_capture': DPI_MATRIX_REL,
        'recorded': False,
        'dpi_matrix_100_150_200_executed': False,
        'display_scale_changed_by_this_record': False,
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
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description='Validate or record a scale-only 100/150/200 overlay. Not AC38.',
    )
    parser.add_argument(
        '--record',
        action='store_true',
        help='Change display scale, sample 100/150/200, restore. Do not use from CI.',
    )
    args = parser.parse_args(argv)
    try:
        if args.record:
            record(ROOT)
        report = validate_dpi_matrix(ROOT)
        if report is None:
            report = _unrecorded_report()
        pointer = collect_dpi_overlay(ROOT)
        if pointer.get('dpi_matrix_100_150_200_executed') is not False:
            raise QualityError('dpi_matrix_claimed')
        if pointer.get('display_scale_changed_by_collector') is not False:
            raise QualityError('collector_changed_display_scale')
    except QualityError as exc:
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2) + '\n', end='')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
