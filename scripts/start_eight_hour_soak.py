#!/usr/bin/env python3
"""Start an observe-mode product-UI soak. Not an 8h pass and not AC46.

Default CLI validates the committed start JSON and does not start processes.
Pass --record on an interactive Windows desktop to launch --ui and leave it
running as a detached owned process. CI and justfile must not invoke --record.
Do not set eight_hour_soak_executed or soak_hours=8 until 8 wall-clock hours
elapse. --shell-smoke / --compose-only are not soak.
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
    SOAK_INTERRUPT_REL,
    SOAK_START_REL,
    soak_interrupt_capture_rels,
    validate_eight_hour_soak_start,
)

ROOT = Path(__file__).resolve().parents[1]
UI_FRAMEWORK = 'net10.0-windows10.0.19041.0'
PINNED_DOTNET_ROOT = Path(r'C:\Users\lyh\AppData\Local\herddesk-dotnet')
WINDOW_WAIT_SEC = 120
CREATE_BREAKAWAY_FROM_JOB = 0x01000000
CREATE_NEW_PROCESS_GROUP = 0x00000200
DETACHED_PROCESS = 0x00000008
STARTF_USESHOWWINDOW = getattr(subprocess, 'STARTF_USESHOWWINDOW', 0x00000001)
SW_SHOWMINNOACTIVE = 7
SPAWN_FLAGS = (
    CREATE_BREAKAWAY_FROM_JOB | CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS
)
SPAWN_FLAGS_NO_BREAKAWAY = CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS
HEARTBEAT_INTERVAL_SEC = 60
UI_EXE_NAME = 'HerdDesk.App.exe'


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


def _pid_running(pid: int) -> bool:
    if os.name != 'nt' or pid <= 0:
        return False
    startupinfo = None
    creationflags = 0
    if hasattr(subprocess, 'STARTUPINFO'):
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= getattr(subprocess, 'STARTF_USESHOWWINDOW', 0)
        creationflags = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
    completed = subprocess.run(
        ['tasklist', '/FI', f'PID eq {pid}', '/FO', 'CSV', '/NH'],
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        check=False,
        timeout=10,
        startupinfo=startupinfo,
        creationflags=creationflags,
    )
    if completed.returncode != 0:
        return False
    for line in (completed.stdout or '').splitlines():
        parts = [item.strip().strip('"') for item in line.split(',')]
        if len(parts) < 2:
            continue
        try:
            if int(parts[1]) == pid:
                return True
        except ValueError:
            continue
    return False


def _visible_windows() -> list[dict[str, Any]]:
    if os.name != 'nt':
        return []
    try:
        user32 = ctypes.WinDLL('user32', use_last_error=True)
        enum_proc = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
        found: list[dict[str, Any]] = []

        def callback(hwnd, _lparam):
            visible = bool(user32.IsWindowVisible(hwnd))
            iconic = bool(user32.IsIconic(hwnd))
            if not visible and not iconic:
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


def _spawn_startupinfo() -> Any:
    if not hasattr(subprocess, 'STARTUPINFO'):
        return None
    info = subprocess.STARTUPINFO()
    info.dwFlags |= STARTF_USESHOWWINDOW
    info.wShowWindow = SW_SHOWMINNOACTIVE
    return info


def _show_min_no_active(hwnd: Any) -> None:
    if os.name != 'nt' or not hwnd:
        return
    try:
        user32 = ctypes.WinDLL('user32', use_last_error=True)
        user32.ShowWindow(int(hwnd), SW_SHOWMINNOACTIVE)
    except (OSError, TypeError, ValueError):
        return


def _spawn_detached(
    argv: list[str],
    *,
    cwd: Path,
    env: dict[str, str],
    stdout_handle: Any,
    stderr_handle: Any,
) -> subprocess.Popen[bytes]:
    kwargs: dict[str, Any] = {
        'args': argv,
        'cwd': str(cwd),
        'env': env,
        'stdout': stdout_handle,
        'stderr': stderr_handle,
        'stdin': subprocess.DEVNULL,
        'close_fds': False,
    }
    startupinfo = _spawn_startupinfo()
    if startupinfo is not None:
        kwargs['startupinfo'] = startupinfo
    try:
        return subprocess.Popen(creationflags=SPAWN_FLAGS, **kwargs)
    except OSError:
        return subprocess.Popen(
            creationflags=SPAWN_FLAGS_NO_BREAKAWAY,
            **kwargs,
        )


def _rotate_prior_soak_probe(probe: Path) -> None:
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    names = (
        'hd033-soak-heartbeat.jsonl',
        'hd033-soak-start.json',
        'hd033-soak-pids.json',
        'hd033-soak-stdout.txt',
        'hd033-soak-stderr.txt',
        'hd033-soak-heartbeat-stderr.txt',
        'hd033-soak-temp-root.txt',
    )
    for name in names:
        src = probe / name
        if not src.is_file():
            continue
        dest = probe / f'{src.stem}-prior-{stamp}{src.suffix}'
        try:
            src.replace(dest)
        except OSError:
            continue


def _write_capture(root: Path, doc: dict[str, Any]) -> Path:
    dest = root / SOAK_START_REL
    dest.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(doc, ensure_ascii=False, indent=2) + '\n'
    dest.write_text(text, encoding='utf-8')
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    (probe / 'hd033-soak-start.json').write_text(text, encoding='utf-8')
    return dest


def _start_heartbeat(root: Path, pids: list[int], started_at: str) -> int | None:
    if os.name != 'nt' or not pids:
        return None
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    heartbeat_path = probe / 'hd033-soak-heartbeat.jsonl'
    python = sys.executable
    argv = [
        python,
        str(SCRIPTS / 'start_eight_hour_soak.py'),
        '--heartbeat',
        '--owned-pids',
        ','.join(str(pid) for pid in pids),
        '--heartbeat-path',
        str(heartbeat_path),
        '--started-at-utc',
        started_at,
    ]
    log_path = probe / 'hd033-soak-heartbeat-stderr.txt'
    handle = log_path.open('ab')
    try:
        proc = _spawn_detached(
            argv,
            cwd=root,
            env=_dotnet_env(),
            stdout_handle=handle,
            stderr_handle=handle,
        )
    except OSError:
        handle.close()
        return None
    handle.close()
    return proc.pid


def run_heartbeat(pids: list[int], path: Path, started_at: str) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    while True:
        observed = _utc_now()
        alive = [pid for pid in pids if _pid_running(pid)]
        row = {
            'observed_at_utc': observed,
            'started_at_utc': started_at,
            'alive_pids': alive,
            'owned_pids': pids,
            'eight_hour_soak_executed': False,
            'soak_hours': None,
            'live_soak': False,
            'ac46_passed': False,
            'invented_timings': False,
        }
        for item in _herddesk_windows(set(alive)):
            _show_min_no_active(item.get('hwnd'))
        with path.open('a', encoding='utf-8') as handle:
            handle.write(json.dumps(row, ensure_ascii=False) + '\n')
        if not alive:
            return 0
        time.sleep(HEARTBEAT_INTERVAL_SEC)


def record(root: Path) -> dict[str, Any]:
    root = Path(root)
    if os.name != 'nt':
        raise QualityError('missing_record_field')
    started_at = _utc_now()
    git_sha = _git_sha(root)
    ui_started = False
    ui_pid: int | None = None
    app_pids: list[int] = []
    window_seen = False
    windows: list[dict[str, Any]] = []
    blocker: str | None = None
    heartbeat_pid: int | None = None
    temp_root = tempfile.mkdtemp(prefix='herddesk-hd033-soak-')
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    _rotate_prior_soak_probe(probe)
    (probe / 'hd033-soak-temp-root.txt').write_text(temp_root + '\n', encoding='utf-8')
    stdout_path = probe / 'hd033-soak-stdout.txt'
    stderr_path = probe / 'hd033-soak-stderr.txt'
    stdout_handle = stdout_path.open('wb')
    stderr_handle = stderr_path.open('wb')
    env = _dotnet_env()
    ui_env = dict(env)
    ui_env['HERDDESK_SOAK_MINIMIZED'] = '1'
    dotnet = _dotnet_exe()
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
        env=env,
        capture_output=True,
        check=False,
        timeout=300,
    )
    if build.returncode != 0:
        stdout_handle.close()
        stderr_handle.close()
        raise QualityError('product_ui_not_started')
    try:
        exe = _ui_exe(root)
    except QualityError:
        stdout_handle.close()
        stderr_handle.close()
        raise
    ui_argv = [str(exe), '--ui', temp_root]
    ui_command_redacted = [UI_EXE_NAME, '--ui', '<temp-root>']
    ui_proc: subprocess.Popen[bytes] | None = None
    try:
        ui_proc = _spawn_detached(
            ui_argv,
            cwd=exe.parent,
            env=ui_env,
            stdout_handle=stdout_handle,
            stderr_handle=stderr_handle,
        )
        ui_pid = ui_proc.pid
        deadline = time.time() + WINDOW_WAIT_SEC
        while time.time() < deadline:
            if ui_proc.poll() is not None:
                blocker = 'product_ui_exited_before_window'
                break
            if _pid_running(ui_pid):
                app_pids = [ui_pid]
            else:
                app_pids = []
            windows = _herddesk_windows(set(app_pids) | {ui_pid})
            if app_pids or windows:
                ui_started = True
                window_seen = any(
                    str(item.get('title') or '') == 'HerdDesk' for item in windows
                )
                if window_seen:
                    for item in windows:
                        _show_min_no_active(item.get('hwnd'))
                    break
            time.sleep(0.5)
        if ui_started and not window_seen:
            windows = _herddesk_windows(set(app_pids) | {ui_pid})
            window_seen = any(
                str(item.get('title') or '') == 'HerdDesk' for item in windows
            )
        if window_seen:
            for item in windows:
                _show_min_no_active(item.get('hwnd'))
        if not ui_started and ui_proc.poll() is None:
            ui_started = _pid_running(ui_pid)
            if ui_started:
                app_pids = [ui_pid]
            else:
                blocker = blocker or 'product_ui_window_not_seen'
        if not ui_started:
            blocker = blocker or 'product_ui_not_started'
    except OSError as exc:
        blocker = blocker or f'oserror_{getattr(exc, "winerror", None) or exc.errno}'
    finally:
        stdout_handle.close()
        stderr_handle.close()

    if ui_started:
        owned = []
        if isinstance(ui_pid, int):
            owned.append(ui_pid)
        for pid in app_pids:
            if pid not in owned:
                owned.append(pid)
        heartbeat_pid = _start_heartbeat(root, owned, started_at)
        (probe / 'hd033-soak-pids.json').write_text(
            json.dumps(
                {
                    'started_at_utc': started_at,
                    'owned_pids': owned,
                    'heartbeat_pid': heartbeat_pid,
                    'eight_hour_soak_executed': False,
                    'soak_hours': None,
                },
                ensure_ascii=False,
                indent=2,
            )
            + '\n',
            encoding='utf-8',
        )

    stdout_bytes = stdout_path.read_bytes() if stdout_path.is_file() else b''
    stderr_bytes = stderr_path.read_bytes() if stderr_path.is_file() else b''
    owned_pids = []
    if isinstance(ui_pid, int):
        owned_pids.append(ui_pid)
    for pid in app_pids:
        if pid not in owned_pids:
            owned_pids.append(pid)
    if isinstance(heartbeat_pid, int):
        owned_pids.append(heartbeat_pid)
    running_at_capture = bool(
        ui_started and ui_pid is not None and _pid_running(ui_pid)
    )
    doc: dict[str, Any] = {
        'document_kind': 'hd033_eight_hour_soak_start',
        'template': False,
        'capture_id': 'hd033-live-soak-start-2026-09-10',
        'kind': 'live_eight_hour_soak_start',
        'started_at_utc': started_at,
        'captured_at_utc': started_at,
        'operator_scope': 'product_ui_eight_hour_soak_started_wall_clock_incomplete',
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
        'evidence_level': 'start_recorded',
        'live_soak': False,
        'eight_hour_soak_executed': False,
        'soak_hours': None,
        'disconnect_switch_count': None,
        'sample_count': None,
        'ac46_passed': False,
        'l4_soak': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'winui_admitted': False,
        'invented_timings': False,
        'product_ui_started': ui_started,
        'window_seen': window_seen,
        'process_running_at_capture': running_at_capture,
        'owned_pids': owned_pids,
        'heartbeat_pid': heartbeat_pid,
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
                'exit_code': None,
                'stdout_sha256': _sha256_hex(stdout_bytes),
                'stderr_sha256': _sha256_hex(stderr_bytes),
                'stdout_gitignored_path': 'probe-results/hd033-soak-stdout.txt',
                'stderr_gitignored_path': 'probe-results/hd033-soak-stderr.txt',
            }
        ],
        'raw_gitignored_path': 'probe-results/hd033-soak-start.json',
        'committed_raw': True,
        'prior_interruption_capture': SOAK_INTERRUPT_REL,
        'prior_interruption_captures': soak_interrupt_capture_rels(root),
        'blocker': blocker,
        'limitations': [
            'Product UI --ui was started in observe mode on this Windows 11 desktop and left running.',
            'This file is a new soak START capture after prior interruption(s). eight_hour_soak_executed stays false until 8 wall-clock hours elapse.',
            'soak_hours stays null. Do not invent an 8h duration. A prior interruption is not 8h completion.',
            '100 disconnect/switch cycles and fault injection that require live herdr/SSH were not authorized and were not faked.',
            'Existing short idle collectors are not this soak. Narrator/DPI overlays were not rebuilt.',
            'This file is not live_soak success and does not pass AC46, L4, or G0.',
            'Hosted CI is not an interactive desktop and must not run --record.',
            'Kill only processes this soak owns. Do not kill user daemons. Activation is per data-root hash.',
        ],
    }
    _write_capture(root, doc)
    if not ui_started:
        raise QualityError('product_ui_not_started')
    return doc


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description='Validate or start a product-UI 8h soak. Not AC46.',
    )
    parser.add_argument(
        '--record',
        action='store_true',
        help='Launch --ui and leave it running. Do not use from CI.',
    )
    parser.add_argument('--heartbeat', action='store_true', help=argparse.SUPPRESS)
    parser.add_argument('--owned-pids', default='', help=argparse.SUPPRESS)
    parser.add_argument('--heartbeat-path', default='', help=argparse.SUPPRESS)
    parser.add_argument('--started-at-utc', default='', help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    try:
        if args.heartbeat:
            pids = []
            for part in (args.owned_pids or '').split(','):
                part = part.strip()
                if not part:
                    continue
                pids.append(int(part))
            if not pids or not args.heartbeat_path or not args.started_at_utc:
                raise QualityError('missing_record_field')
            return run_heartbeat(pids, Path(args.heartbeat_path), args.started_at_utc)
        if args.record:
            record(ROOT)
        report = validate_eight_hour_soak_start(ROOT)
    except QualityError as exc:
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2) + '\n', end='')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
