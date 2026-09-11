#!/usr/bin/env python3
"""Record an authorized product-UI + Narrator.exe launch. Not AC37.

Default CLI validates the committed launch JSON and does not start processes.
Pass --record on an interactive Windows desktop to launch --ui and Narrator.exe.
CI and justfile must not invoke --record. The overlay collector still does not
start Narrator.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
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
    QualityError,
    LAUNCH_REL,
    narrator_exe_path,
    narrator_launched,
    validate_narrator_product_ui_launch,
)

ROOT = Path(__file__).resolve().parents[1]
UI_FRAMEWORK = 'net10.0-windows10.0.19041.0'
PINNED_DOTNET_ROOT = Path(r'C:\Users\lyh\AppData\Local\herddesk-dotnet')
WINDOW_WAIT_SEC = 90
OBSERVE_SEC = 8
UI_EXE_NAME = 'HerdDesk.App.exe'
SW_RESTORE = 9
SW_SHOWNORMAL = 1
ASFW_ANY = 0xFFFFFFFF
LSFW_UNLOCK = 2
INPUT_KEYBOARD = 1
KEYEVENTF_KEYUP = 0x0002
VK_CONTROL = 0x11
VK_K = 0x4B
VK_MENU = 0x12
SEARCH_PALETTE_NAMES = frozenset({'搜索'})
CHROME_NAMES = frozenset({
    '搜索', '请求控制', '释放控制', '确认关闭', '设置', '诊断', '关于',
    '设备与会话', '添加设备', '连接状态',
})
KEYBOARD_CHROME_TOKENS = frozenset({
    'ctrl_k_sent', 'set_foreground_failed', 'skipped_non_windows',
})


class KEYBDINPUT(ctypes.Structure):
    _fields_ = (
        ('wVk', ctypes.c_ushort),
        ('wScan', ctypes.c_ushort),
        ('dwFlags', ctypes.c_uint),
        ('time', ctypes.c_uint),
        ('dwExtraInfo', ctypes.c_size_t),
    )


class MOUSEINPUT(ctypes.Structure):
    _fields_ = (
        ('dx', ctypes.c_long),
        ('dy', ctypes.c_long),
        ('mouseData', ctypes.c_uint),
        ('dwFlags', ctypes.c_uint),
        ('time', ctypes.c_uint),
        ('dwExtraInfo', ctypes.c_size_t),
    )


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = (
        ('uMsg', ctypes.c_uint),
        ('wParamL', ctypes.c_ushort),
        ('wParamH', ctypes.c_ushort),
    )


class INPUTUNION(ctypes.Union):
    _fields_ = (
        ('mi', MOUSEINPUT),
        ('ki', KEYBDINPUT),
        ('hi', HARDWAREINPUT),
    )


class INPUT(ctypes.Structure):
    _fields_ = (
        ('type', ctypes.c_uint),
        ('_pad', ctypes.c_uint),
        ('union', INPUTUNION),
    ) if ctypes.sizeof(ctypes.c_void_p) == 8 else (
        ('type', ctypes.c_uint),
        ('union', INPUTUNION),
    )


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')


def _sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


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


def _ui_exe(root: Path) -> Path:
    return (
        Path(root)
        / 'src'
        / 'HerdDesk.App'
        / 'bin'
        / 'Release'
        / UI_FRAMEWORK
        / 'win-x64'
        / UI_EXE_NAME
    )


def _dotnet_env() -> dict[str, str]:
    env = os.environ.copy()
    if PINNED_DOTNET_ROOT.is_dir():
        env['DOTNET_ROOT'] = str(PINNED_DOTNET_ROOT)
        env['PATH'] = str(PINNED_DOTNET_ROOT) + os.pathsep + env.get('PATH', '')
        env['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    elif env.get('DOTNET_ROOT'):
        env['DOTNET_MULTILEVEL_LOOKUP'] = env.get('DOTNET_MULTILEVEL_LOOKUP') or '0'
        env['PATH'] = env['DOTNET_ROOT'] + os.pathsep + env.get('PATH', '')
    return env


def _ensure_ui_exe(root: Path, env: dict[str, str]) -> Path:
    exe = _ui_exe(root)
    if exe.is_file():
        return exe
    completed = subprocess.run(
        [
            str(_dotnet_exe()),
            'build',
            str(root / 'src' / 'HerdDesk.App' / 'HerdDesk.App.csproj'),
            '--framework',
            UI_FRAMEWORK,
            '--configuration',
            'Release',
        ],
        cwd=str(root),
        env=env,
        capture_output=True,
        check=False,
        timeout=300,
    )
    if completed.returncode != 0 or not exe.is_file():
        raise QualityError('product_ui_not_started')
    return exe


def _tasklist_image(name: str) -> list[int]:
    if os.name != 'nt':
        return []
    startupinfo = None
    creationflags = 0
    if hasattr(subprocess, 'STARTUPINFO'):
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= getattr(subprocess, 'STARTF_USESHOWWINDOW', 0)
        creationflags = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
    completed = subprocess.run(
        ['tasklist', '/FI', f'IMAGENAME eq {name}', '/FO', 'CSV', '/NH'],
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        check=False,
        timeout=10,
        startupinfo=startupinfo,
        creationflags=creationflags,
    )
    pids: list[int] = []
    if completed.returncode != 0:
        return pids
    for line in (completed.stdout or '').splitlines():
        parts = [item.strip().strip('"') for item in line.split(',')]
        if len(parts) < 2:
            continue
        if parts[0].lower() != name.lower():
            continue
        try:
            pids.append(int(parts[1]))
        except ValueError:
            continue
    return pids


def _visible_windows() -> list[dict[str, Any]]:
    if os.name != 'nt':
        return []
    try:
        user32 = ctypes.WinDLL('user32', use_last_error=True)
        enum_proc = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
        found: list[dict[str, Any]] = []

        def callback(hwnd, _lparam):
            if not user32.IsWindowVisible(hwnd):
                return True
            length = user32.GetWindowTextLengthW(hwnd)
            buf = ctypes.create_unicode_buffer(length + 1)
            user32.GetWindowTextW(hwnd, buf, length + 1)
            title = buf.value or ''
            pid = ctypes.c_ulong(0)
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            if title:
                found.append({'hwnd': int(hwnd), 'title': title, 'pid': int(pid.value)})
            return True

        user32.EnumWindows(enum_proc(callback), 0)
        return found
    except OSError:
        return []


def _herddesk_windows(pids: set[int]) -> list[dict[str, Any]]:
    matches: list[dict[str, Any]] = []
    for item in _visible_windows():
        pid = item.get('pid')
        if pid not in pids:
            continue
        title = str(item.get('title') or '')
        matches.append({'title': title, 'pid': pid, 'hwnd': item.get('hwnd')})
    return matches


def _start_narrator(path: Path) -> tuple[str, int | None]:
    """Start Narrator.exe via ShellExecute. Direct CreateProcess hits ERROR_ELEVATION_REQUIRED."""
    if os.name != 'nt':
        return 'skipped_non_windows', None
    shell32 = ctypes.WinDLL('shell32', use_last_error=True)
    rc = int(shell32.ShellExecuteW(None, 'open', str(path), None, None, 1))
    if rc > 32:
        return 'shellexecute', rc
    completed = subprocess.run(
        [
            'powershell',
            '-NoLogo',
            '-NoProfile',
            '-Command',
            "Start-Process -FilePath ([IO.Path]::Combine($env:SystemRoot, 'System32', 'Narrator.exe'))",
        ],
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        check=False,
        timeout=20,
    )
    if completed.returncode == 0:
        return 'start_process', completed.returncode
    return f'start_failed_{rc}_{completed.returncode}', completed.returncode


def _kill_pid(pid: int) -> int | None:
    if os.name != 'nt' or pid <= 0:
        return None
    completed = subprocess.run(
        ['taskkill', '/PID', str(pid), '/T', '/F'],
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        check=False,
    )
    return completed.returncode


def _bind_user32() -> tuple[ctypes.WinDLL, ctypes.WinDLL]:
    user32 = ctypes.WinDLL('user32', use_last_error=True)
    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
    hwnd = ctypes.c_void_p
    BOOL = ctypes.c_int
    UINT = ctypes.c_uint
    DWORD = ctypes.c_uint
    user32.IsWindow.argtypes = [hwnd]
    user32.IsWindow.restype = BOOL
    user32.IsIconic.argtypes = [hwnd]
    user32.IsIconic.restype = BOOL
    user32.ShowWindow.argtypes = [hwnd, ctypes.c_int]
    user32.ShowWindow.restype = BOOL
    user32.BringWindowToTop.argtypes = [hwnd]
    user32.BringWindowToTop.restype = BOOL
    user32.SetForegroundWindow.argtypes = [hwnd]
    user32.SetForegroundWindow.restype = BOOL
    user32.GetForegroundWindow.argtypes = []
    user32.GetForegroundWindow.restype = hwnd
    user32.SetFocus.argtypes = [hwnd]
    user32.SetFocus.restype = hwnd
    user32.GetWindowThreadProcessId.argtypes = [hwnd, ctypes.POINTER(DWORD)]
    user32.GetWindowThreadProcessId.restype = DWORD
    user32.AttachThreadInput.argtypes = [DWORD, DWORD, BOOL]
    user32.AttachThreadInput.restype = BOOL
    user32.SendInput.argtypes = [UINT, ctypes.POINTER(INPUT), ctypes.c_int]
    user32.SendInput.restype = UINT
    kernel32.GetCurrentThreadId.argtypes = []
    kernel32.GetCurrentThreadId.restype = DWORD
    return user32, kernel32


def _hwnd_int(value: object) -> int:
    if not value:
        return 0
    return int(value)


def _window_ids(user32: ctypes.WinDLL, hwnd: int) -> tuple[int, int]:
    pid = ctypes.c_uint(0)
    tid = int(user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid)))
    return tid, int(pid.value)


def _foreground_matches(user32: ctypes.WinDLL, hwnd: int) -> bool:
    fg = _hwnd_int(user32.GetForegroundWindow())
    if fg == hwnd:
        return True
    if fg <= 0:
        return False
    _tid, pid = _window_ids(user32, hwnd)
    _fg_tid, fg_pid = _window_ids(user32, fg)
    return pid != 0 and pid == fg_pid


def _try_allow_set_foreground(user32: ctypes.WinDLL, attempts: list[str]) -> None:
    allow = getattr(user32, 'AllowSetForegroundWindow', None)
    if allow is not None:
        try:
            allow.argtypes = [ctypes.c_uint]
            allow.restype = ctypes.c_int
            ok = bool(allow(ASFW_ANY))
            attempts.append('allow_set_foreground_' + ('ok' if ok else 'false'))
        except OSError:
            attempts.append('allow_set_foreground_oserror')
    else:
        attempts.append('allow_set_foreground_unavailable')
    lock = getattr(user32, 'LockSetForegroundWindow', None)
    if lock is not None:
        try:
            lock.argtypes = [ctypes.c_uint]
            lock.restype = ctypes.c_int
            ok = bool(lock(LSFW_UNLOCK))
            attempts.append('lock_set_foreground_unlock_' + ('ok' if ok else 'false'))
        except OSError:
            attempts.append('lock_set_foreground_oserror')


def _send_vk(user32: ctypes.WinDLL, vk: int, *, up: bool) -> int:
    inp = INPUT()
    inp.type = INPUT_KEYBOARD
    inp.union.ki.wVk = vk
    inp.union.ki.wScan = 0
    inp.union.ki.dwFlags = KEYEVENTF_KEYUP if up else 0
    inp.union.ki.time = 0
    inp.union.ki.dwExtraInfo = 0
    return int(user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT)))


def _attach_set_foreground(
    user32: ctypes.WinDLL,
    kernel32: ctypes.WinDLL,
    hwnd: int,
    attempts: list[str],
    *,
    label: str,
) -> None:
    current = int(kernel32.GetCurrentThreadId())
    fg = _hwnd_int(user32.GetForegroundWindow())
    fg_tid = 0
    if fg > 0:
        fg_tid, _fg_pid = _window_ids(user32, fg)
    hwnd_tid, _hwnd_pid = _window_ids(user32, hwnd)
    attached_fg = False
    attached_hwnd = False
    if fg_tid and fg_tid != current:
        attached_fg = bool(user32.AttachThreadInput(current, fg_tid, True))
        attempts.append(f'attach_fg_{label}_' + ('ok' if attached_fg else 'false'))
    else:
        attempts.append(f'attach_fg_{label}_skipped')
    if hwnd_tid and hwnd_tid != current and hwnd_tid != fg_tid:
        attached_hwnd = bool(user32.AttachThreadInput(current, hwnd_tid, True))
        attempts.append(f'attach_hwnd_{label}_' + ('ok' if attached_hwnd else 'false'))
    try:
        user32.ShowWindow(hwnd, SW_RESTORE)
        user32.BringWindowToTop(hwnd)
        set_ok = bool(user32.SetForegroundWindow(hwnd))
        err = ctypes.get_last_error()
        attempts.append(
            f'set_foreground_{label}_' + ('ok' if set_ok else f'false_{err}')
        )
        user32.SetFocus(hwnd)
    finally:
        if attached_hwnd:
            user32.AttachThreadInput(current, hwnd_tid, False)
        if attached_fg:
            user32.AttachThreadInput(current, fg_tid, False)


def _foreground_and_ctrl_k(hwnd: int) -> tuple[str, list[str]]:
    attempts: list[str] = []
    if os.name != 'nt' or hwnd <= 0:
        return 'skipped_non_windows', attempts
    user32, kernel32 = _bind_user32()
    attempts.append(f'hwnd=0x{hwnd:x}')
    if not user32.IsWindow(hwnd):
        attempts.append('not_a_window')
        return 'set_foreground_failed', attempts
    attempts.append(f'input_sizeof={ctypes.sizeof(INPUT)}')
    shown = bool(user32.ShowWindow(hwnd, SW_RESTORE))
    attempts.append('show_restore_' + ('ok' if shown else 'false'))
    _try_allow_set_foreground(user32, attempts)
    _attach_set_foreground(user32, kernel32, hwnd, attempts, label='1')
    matched = _foreground_matches(user32, hwnd)
    attempts.append('foreground_match_1_' + ('ok' if matched else 'false'))
    if not matched:
        alt_down = _send_vk(user32, VK_MENU, up=False)
        alt_up = _send_vk(user32, VK_MENU, up=True)
        attempts.append(
            'alt_pulse_' + ('ok' if alt_down and alt_up else 'false')
        )
        _try_allow_set_foreground(user32, attempts)
        _attach_set_foreground(user32, kernel32, hwnd, attempts, label='2')
        matched = _foreground_matches(user32, hwnd)
        attempts.append('foreground_match_2_' + ('ok' if matched else 'false'))
    if not matched:
        return 'set_foreground_failed', attempts
    time.sleep(0.4)
    sent = (
        _send_vk(user32, VK_CONTROL, up=False)
        and _send_vk(user32, VK_K, up=False)
        and _send_vk(user32, VK_K, up=True)
        and _send_vk(user32, VK_CONTROL, up=True)
    )
    attempts.append('send_input_ctrl_k_' + ('ok' if sent else 'false'))
    if not sent:
        return 'set_foreground_failed', attempts
    return 'ctrl_k_sent', attempts


def _uia_names(window_title: str) -> dict[str, Any]:
    if os.name != 'nt':
        return {'ok': False, 'error': 'non_windows', 'names': []}
    script = (
        'Add-Type -AssemblyName UIAutomationClient; '
        '$root = [System.Windows.Automation.AutomationElement]::RootElement; '
        '$cond = New-Object System.Windows.Automation.PropertyCondition('
        '[System.Windows.Automation.AutomationElement]::NameProperty, '
        + json.dumps(window_title)
        + '); '
        '$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond); '
        'if (-not $win) { Write-Output \'{"ok":false,"error":"window_not_found","names":[]}\'; exit 0 }; '
        '$names = New-Object System.Collections.Generic.List[string]; '
        '$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, '
        '[System.Windows.Automation.Condition]::TrueCondition); '
        'foreach ($el in $all) { $n = $el.Current.Name; if ($n) { $names.Add($n) } }; '
        '$obj = @{ ok = $true; error = $null; names = $names }; '
        '$obj | ConvertTo-Json -Compress -Depth 4'
    )
    completed = subprocess.run(
        ['powershell', '-NoLogo', '-NoProfile', '-Command', script],
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        check=False,
        timeout=20,
    )
    raw = (completed.stdout or '').strip()
    if not raw:
        return {
            'ok': False,
            'error': 'uia_empty_stdout',
            'exit_code': completed.returncode,
            'names': [],
        }
    try:
        loaded = json.loads(raw)
    except json.JSONDecodeError:
        return {'ok': False, 'error': 'uia_json', 'names': []}
    if not isinstance(loaded, dict):
        return {'ok': False, 'error': 'uia_object_required', 'names': []}
    names = loaded.get('names')
    if not isinstance(names, list):
        names = []
    cleaned = [item for item in names if isinstance(item, str) and item]
    return {
        'ok': loaded.get('ok') is True,
        'error': loaded.get('error'),
        'names': cleaned[:80],
        'exit_code': completed.returncode,
    }


def _write_capture(root: Path, doc: dict[str, Any]) -> Path:
    dest = root / LAUNCH_REL
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(
        json.dumps(doc, ensure_ascii=False, indent=2) + '\n',
        encoding='utf-8',
    )
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    (probe / 'hd033-narrator-product-ui-launch.json').write_text(
        json.dumps(doc, ensure_ascii=False, indent=2) + '\n',
        encoding='utf-8',
    )
    return dest


def record(root: Path) -> dict[str, Any]:
    root = Path(root)
    if os.name != 'nt':
        raise QualityError('missing_record_field')
    captured_at = _utc_now()
    git_sha = _git_sha(root)
    narrator_path = narrator_exe_path()
    narrator_present = narrator_path.is_file()
    narrator_was_running = narrator_launched()
    narrator_pids_before = set(_tasklist_image('Narrator.exe'))
    ui_started = False
    narrator_started = False
    ui_pid: int | None = None
    app_pids: list[int] = []
    narrator_pid: int | None = None
    ui_exit: int | None = None
    narrator_exit: int | None = None
    window_seen = False
    windows: list[dict[str, Any]] = []
    keyboard_note = 'not_sent'
    keyboard_attempts: list[str] = []
    uia: dict[str, Any] = {'ok': False, 'error': 'not_run', 'names': []}
    blocker: str | None = None
    owned_narrator_pids: list[int] = []
    temp_root = tempfile.mkdtemp(prefix='herddesk-hd033-ui-')
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    stdout_path = probe / 'hd033-narrator-product-ui-stdout.txt'
    stderr_path = probe / 'hd033-narrator-product-ui-stderr.txt'
    stdout_handle = stdout_path.open('wb')
    stderr_handle = stderr_path.open('wb')
    env = _dotnet_env()
    exe: Path | None = None
    try:
        exe = _ensure_ui_exe(root, env)
    except QualityError:
        stdout_handle.close()
        stderr_handle.close()
        shutil.rmtree(temp_root, ignore_errors=True)
        raise
    ui_argv = [str(exe), '--ui', temp_root]
    ui_command_redacted = [UI_EXE_NAME, '--ui', '<temp-root>']
    narrator_start_method = 'not_started'
    ui_proc: subprocess.Popen[bytes] | None = None
    startupinfo = None
    if hasattr(subprocess, 'STARTUPINFO'):
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= getattr(subprocess, 'STARTF_USESHOWWINDOW', 0)
        startupinfo.wShowWindow = SW_SHOWNORMAL
    try:
        ui_proc = subprocess.Popen(
            ui_argv,
            cwd=str(exe.parent),
            env=env,
            stdout=stdout_handle,
            stderr=stderr_handle,
            startupinfo=startupinfo,
        )
        ui_pid = ui_proc.pid
        app_pids = [ui_pid]
        deadline = time.time() + WINDOW_WAIT_SEC
        while time.time() < deadline:
            if ui_proc.poll() is not None:
                ui_exit = ui_proc.returncode
                blocker = 'product_ui_exited_before_window'
                break
            windows = _herddesk_windows({ui_pid})
            if windows:
                ui_started = True
                window_seen = True
                break
            ui_started = True
            time.sleep(0.5)
        if ui_started and not window_seen:
            windows = _herddesk_windows({ui_pid})
            window_seen = bool(windows)
        if not ui_started and ui_proc.poll() is None:
            ui_started = True
        if ui_started and not window_seen:
            blocker = blocker or 'product_ui_window_not_seen'
        if not narrator_present:
            blocker = blocker or 'narrator_exe_missing'
        elif ui_started:
            narrator_start_method, _shell_rc = _start_narrator(narrator_path)
            for _ in range(20):
                time.sleep(0.5)
                after = set(_tasklist_image('Narrator.exe'))
                new_pids = [pid for pid in after if pid not in narrator_pids_before]
                if new_pids:
                    owned_narrator_pids = new_pids
                    narrator_pid = new_pids[0]
                    narrator_started = True
                    break
                if narrator_launched() and not narrator_was_running:
                    narrator_started = True
                    narrator_pid = next(iter(after), None)
                    if narrator_pid:
                        owned_narrator_pids = [narrator_pid]
                    break
                if narrator_launched() and narrator_was_running:
                    narrator_started = True
                    narrator_pid = next(iter(after), None)
                    break
            if not narrator_started:
                blocker = blocker or f'narrator_exe_did_not_start:{narrator_start_method}'
            if narrator_started:
                time.sleep(OBSERVE_SEC)
                windows = _herddesk_windows({ui_pid})
                window_seen = bool(windows)
                hwnd = 0
                title = 'HerdDesk'
                for item in windows:
                    if str(item.get('title') or '') == 'HerdDesk':
                        hwnd = int(item.get('hwnd') or 0)
                        title = 'HerdDesk'
                        break
                if hwnd <= 0 and windows:
                    hwnd = int(windows[0].get('hwnd') or 0)
                    title = str(windows[0].get('title') or 'HerdDesk')
                if hwnd:
                    keyboard_note, keyboard_attempts = _foreground_and_ctrl_k(hwnd)
                    if keyboard_note == 'ctrl_k_sent':
                        time.sleep(1.2)
                uia = _uia_names(title)
        else:
            blocker = blocker or 'product_ui_not_started'
    except OSError as exc:
        blocker = blocker or f'oserror_{getattr(exc, "winerror", None) or exc.errno}'
    finally:
        stdout_handle.close()
        stderr_handle.close()
        if ui_pid:
            ui_kill = _kill_pid(ui_pid)
            if ui_proc is not None:
                try:
                    ui_proc.wait(timeout=8)
                    ui_exit = ui_proc.returncode
                except subprocess.TimeoutExpired:
                    ui_exit = ui_kill
            elif ui_exit is None:
                ui_exit = ui_kill
        if narrator_started and not narrator_was_running:
            last_kill = None
            for pid in owned_narrator_pids:
                last_kill = _kill_pid(pid)
            narrator_exit = last_kill
        try:
            shutil.rmtree(temp_root, ignore_errors=True)
        except OSError:
            pass

    stdout_bytes = stdout_path.read_bytes() if stdout_path.is_file() else b''
    stderr_bytes = stderr_path.read_bytes() if stderr_path.is_file() else b''
    chrome_names = [
        name for name in (uia.get('names') or [])
        if name in CHROME_NAMES
    ]
    if keyboard_note not in KEYBOARD_CHROME_TOKENS:
        keyboard_note = 'set_foreground_failed'
        keyboard_attempts.append('token_normalized_to_set_foreground_failed')
    search_palette_visible = any(name in SEARCH_PALETTE_NAMES for name in chrome_names)
    doc: dict[str, Any] = {
        'document_kind': 'hd033_narrator_product_ui_launch',
        'template': False,
        'capture_id': 'hd033-narrator-product-ui-launch-2026-09-11',
        'kind': 'narrator_product_ui_launch',
        'captured_at_utc': captured_at,
        'operator_scope': 'product_ui_and_narrator_launch_ac37_workflow_incomplete',
        'git_sha': git_sha,
        'platform': {
            'sys_platform': sys.platform,
            'system': platform.system(),
            'release': platform.release(),
            'version': platform.version(),
            'machine': platform.machine(),
            'platform': platform.platform(),
        },
        'result': 'not_run',
        'evidence_level': 'launch_recorded',
        'live_narrator': False,
        'ac37_passed': False,
        'l3_narrator': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'winui_admitted': False,
        'invented_timings': False,
        'product_ui_started': ui_started,
        'window_seen': window_seen,
        'narrator_exe_present': narrator_present,
        'narrator_started_by_this_run': narrator_started,
        'narrator_started_by_collector': False,
        'narrator_was_already_running': narrator_was_running,
        'automation_names_are_not_screen_reader_evidence': True,
        'ac37_workflow_completed': False,
        'ac37_steps': {
            'search': 'not_completed',
            'request_control': 'not_completed',
            'release': 'not_completed',
            'close_confirm': 'not_completed',
        },
        'keyboard_chrome': keyboard_note,
        'keyboard_chrome_attempts': keyboard_attempts,
        'search_palette_name_seen': search_palette_visible,
        'uia_chrome_names_found': chrome_names,
        'uia': {
            'ok': uia.get('ok') is True,
            'error': uia.get('error'),
            'name_count': len(uia.get('names') or []),
            'names_sample': (uia.get('names') or [])[:20],
        },
        'windows': [
            {'title': item.get('title'), 'pid': item.get('pid')}
            for item in windows
        ],
        'commands': [
            {
                'role': 'product_ui',
                'command_redacted': ui_command_redacted,
                'pid': ui_pid,
                'app_pids': app_pids,
                'exit_code': ui_exit,
                'stdout_sha256': _sha256_hex(stdout_bytes),
                'stderr_sha256': _sha256_hex(stderr_bytes),
                'stdout_gitignored_path': 'probe-results/hd033-narrator-product-ui-stdout.txt',
                'stderr_gitignored_path': 'probe-results/hd033-narrator-product-ui-stderr.txt',
            },
            {
                'role': 'narrator',
                'command_redacted': ['Narrator.exe'],
                'start_method': narrator_start_method,
                'pid': narrator_pid,
                'exit_code': narrator_exit,
                'stdout_sha256': None,
                'stderr_sha256': None,
            },
        ],
        'raw_gitignored_path': 'probe-results/hd033-narrator-product-ui-launch.json',
        'committed_raw': True,
        'blocker': blocker,
        'limitations': [
            'Product UI --ui and Narrator.exe were launched on this Windows desktop.',
            'AC37 search / request-control / release / close-confirm was not completed; no live session; herdr writes remain forbidden.',
            'Keyboard chrome retry is not AC37 workflow completion. AutomationProperties names after Ctrl+K are not screen-reader evidence.',
            'RequestControl was not invoked. Overlay collector did not start Narrator.',
            'Shipped AutomationProperties names are not screen-reader evidence.',
            'This file is not live_narrator success and does not pass AC37 or G0.',
            'Hosted CI is not an interactive desktop and must not run --record.',
        ],
    }
    _write_capture(root, doc)
    return doc


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description='Validate or record a product-UI + Narrator launch. Not AC37.',
    )
    parser.add_argument(
        '--record',
        action='store_true',
        help='Launch --ui and Narrator.exe. Do not use from CI.',
    )
    args = parser.parse_args(argv)
    try:
        if args.record:
            record(ROOT)
        report = validate_narrator_product_ui_launch(ROOT)
    except QualityError as exc:
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2) + '\n', end='')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
