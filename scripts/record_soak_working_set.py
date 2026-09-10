#!/usr/bin/env python3
"""Sample the running soak App PID working set. Not AC29, AC46, or 1/4 pane.

Default CLI validates the optional overlay JSON and does not start processes.
Pass --record on an interactive Windows desktop to sample the current soak
START product-UI PID without launching another --ui. Writes
evidence/quality/live-soak-working-set.json and a detached resource heartbeat
that appends probe-results/hd033-soak-resources.jsonl every 60s. The sampler
exits when the App PID is gone. Do not add the sampler PID to START owned_pids.
Do not overwrite live-working-set.not-run.json. CI and justfile must not
invoke --record. One soak-process sample is not the 1/4-pane lab, not 100
open/close, and not AC29 or AC46.
"""
from __future__ import annotations

import argparse
import ctypes
import json
import os
from pathlib import Path
import sys
import time
from datetime import datetime, timezone
from typing import Any

SCRIPTS = Path(__file__).resolve().parent
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))
from herddesk_g0.quality import (
    LIVE_WORKING_SET_REL,
    QualityError,
    SOAK_START_REL,
    SOAK_WORKING_SET_KIND,
    SOAK_WORKING_SET_REL,
    soak_start_app_pid,
    validate_eight_hour_soak_start,
    validate_soak_working_set,
)
import start_eight_hour_soak as soak_cli

ROOT = Path(__file__).resolve().parents[1]
HEARTBEAT_INTERVAL_SEC = 60
UI_EXE_NAME = 'HerdDesk.App.exe'
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_READ = 0x0010
RESOURCES_JSONL = 'probe-results/hd033-soak-resources.jsonl'


class PROCESS_MEMORY_COUNTERS_EX(ctypes.Structure):
    _fields_ = (
        ('cb', ctypes.c_ulong),
        ('PageFaultCount', ctypes.c_ulong),
        ('PeakWorkingSetSize', ctypes.c_size_t),
        ('WorkingSetSize', ctypes.c_size_t),
        ('QuotaPeakPagedPoolUsage', ctypes.c_size_t),
        ('QuotaPagedPoolUsage', ctypes.c_size_t),
        ('QuotaPeakNonPagedPoolUsage', ctypes.c_size_t),
        ('QuotaNonPagedPoolUsage', ctypes.c_size_t),
        ('PagefileUsage', ctypes.c_size_t),
        ('PeakPagefileUsage', ctypes.c_size_t),
        ('PrivateUsage', ctypes.c_size_t),
    )


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')


def _pid_running(pid: int) -> bool:
    if os.name != 'nt' or pid <= 0:
        return False
    return soak_cli._pid_running(pid)


def _pid_image(pid: int) -> str | None:
    if os.name != 'nt' or pid <= 0:
        return None
    return soak_cli._pid_image(pid)


def _app_image(name: str) -> bool:
    return soak_cli._app_image(name)


def _git_sha(root: Path) -> str:
    return soak_cli._git_sha(root)


def _sample_process(pid: int) -> dict[str, Any]:
    if os.name != 'nt' or pid <= 0:
        raise QualityError('soak_app_pid_not_running')
    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
    psapi = ctypes.WinDLL('psapi', use_last_error=True)
    kernel32.OpenProcess.restype = ctypes.c_void_p
    kernel32.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
    kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
    psapi.GetProcessMemoryInfo.argtypes = [
        ctypes.c_void_p,
        ctypes.POINTER(PROCESS_MEMORY_COUNTERS_EX),
        ctypes.c_ulong,
    ]
    kernel32.GetProcessHandleCount.argtypes = [
        ctypes.c_void_p,
        ctypes.POINTER(ctypes.c_ulong),
    ]
    access = PROCESS_QUERY_LIMITED_INFORMATION
    handle = kernel32.OpenProcess(access, 0, pid)
    if not handle:
        access = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ
        handle = kernel32.OpenProcess(access, 0, pid)
    if not handle:
        raise QualityError('soak_app_pid_not_running')
    working = None
    private_bytes = None
    handle_count = None
    try:
        counters = PROCESS_MEMORY_COUNTERS_EX()
        counters.cb = ctypes.sizeof(PROCESS_MEMORY_COUNTERS_EX)
        if psapi.GetProcessMemoryInfo(
            handle, ctypes.byref(counters), counters.cb
        ):
            working = int(counters.WorkingSetSize)
            private_bytes = int(counters.PrivateUsage)
        count = ctypes.c_ulong(0)
        if kernel32.GetProcessHandleCount(handle, ctypes.byref(count)):
            handle_count = int(count.value)
    finally:
        kernel32.CloseHandle(handle)
    if not isinstance(working, int) or working <= 0:
        raise QualityError('soak_app_pid_not_running')
    return {
        'working_set_bytes': working,
        'private_bytes': private_bytes,
        'handle_count': handle_count,
        'process_count': 1,
    }


def _write_overlay(root: Path, doc: dict[str, Any]) -> Path:
    dest = root / SOAK_WORKING_SET_REL
    dest.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(doc, ensure_ascii=False, indent=2) + '\n'
    dest.write_text(text, encoding='utf-8')
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    (probe / 'hd033-soak-working-set.json').write_text(text, encoding='utf-8')
    return dest


def _rotate_resources_probe(probe: Path) -> None:
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    for name in (
        'hd033-soak-resources.jsonl',
        'hd033-soak-resources-stderr.txt',
    ):
        src = probe / name
        if not src.is_file():
            continue
        dest = probe / f'{src.stem}-prior-{stamp}{src.suffix}'
        try:
            src.replace(dest)
        except OSError:
            continue


def run_resource_heartbeat(
    pid: int,
    path: Path,
    started_at: str,
    *,
    interval_sec: int = HEARTBEAT_INTERVAL_SEC,
    pid_running: Any = None,
    sample_process: Any = None,
) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    running = pid_running if pid_running is not None else _pid_running
    sample = sample_process if sample_process is not None else _sample_process
    while True:
        observed = _utc_now()
        alive = bool(running(pid))
        row: dict[str, Any] = {
            'observed_at_utc': observed,
            'started_at_utc': started_at,
            'pid': pid,
            'alive': alive,
            'live_working_set': False,
            'ac29_passed': False,
            'ac46_passed': False,
            'eight_hour_soak_executed': False,
            'soak_hours': None,
            'one_quarter_pane_lab': False,
            'open_close_100': False,
            'invented_timings': False,
        }
        if alive:
            try:
                row.update(sample(pid))
            except QualityError:
                row['alive'] = False
                alive = False
        with path.open('a', encoding='utf-8') as handle:
            handle.write(json.dumps(row, ensure_ascii=False) + '\n')
        if not alive:
            return 0
        time.sleep(interval_sec)


def _start_resource_heartbeat(
    root: Path, pid: int, started_at: str
) -> int | None:
    if os.name != 'nt' or pid <= 0:
        return None
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    heartbeat_path = probe / 'hd033-soak-resources.jsonl'
    python = sys.executable
    argv = [
        python,
        str(SCRIPTS / 'record_soak_working_set.py'),
        '--heartbeat',
        '--app-pid',
        str(pid),
        '--heartbeat-path',
        str(heartbeat_path),
        '--started-at-utc',
        started_at,
    ]
    log_path = probe / 'hd033-soak-resources-stderr.txt'
    handle = log_path.open('ab')
    try:
        proc = soak_cli._spawn_detached(
            argv,
            cwd=root,
            env=soak_cli._dotnet_env(),
            stdout_handle=handle,
            stderr_handle=handle,
        )
    except OSError:
        handle.close()
        return None
    handle.close()
    return proc.pid


def record(
    root: Path,
    *,
    pid_running: Any = None,
    pid_image: Any = None,
    sample_process: Any = None,
    start_heartbeat: Any = None,
    git_sha: str | None = None,
    now: str | None = None,
) -> dict[str, Any]:
    """Sample the START App PID. Do not launch --ui. Do not edit START owned_pids.

    Injected pid_running / pid_image / sample_process / start_heartbeat allow
    tempfile tests on non-Windows. Live --record without those hooks stays
    fail-closed off Windows and must not write an overlay.
    """
    root = Path(root)
    hooks = (
        pid_running is not None
        and pid_image is not None
        and sample_process is not None
        and start_heartbeat is not None
    )
    if os.name != 'nt' and not hooks:
        raise QualityError('missing_record_field')
    validate_eight_hour_soak_start(root)
    not_run_path = root / LIVE_WORKING_SET_REL
    before_not_run = (
        not_run_path.read_text(encoding='utf-8') if not_run_path.is_file() else None
    )
    start_path = root / SOAK_START_REL
    before_start = start_path.read_text(encoding='utf-8')
    start = json.loads(before_start)
    if not isinstance(start, dict):
        raise QualityError('missing_record_field')
    app_pid = soak_start_app_pid(start)
    running = pid_running if pid_running is not None else _pid_running
    image = pid_image if pid_image is not None else _pid_image
    sample = sample_process if sample_process is not None else _sample_process
    spawn = (
        start_heartbeat if start_heartbeat is not None else _start_resource_heartbeat
    )
    if not running(app_pid) or not _app_image(image(app_pid) or ''):
        raise QualityError('soak_app_pid_not_running')
    sampled = sample(app_pid)
    working = sampled.get('working_set_bytes')
    if not isinstance(working, int) or working <= 0:
        raise QualityError('soak_app_pid_not_running')
    sampled_at = now if now is not None else _utc_now()
    started_at = start.get('started_at_utc')
    if not isinstance(started_at, str) or not started_at.strip():
        raise QualityError('missing_record_field')
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    _rotate_resources_probe(probe)
    sampler_pid = spawn(root, app_pid, started_at)
    sha = git_sha if git_sha is not None else _git_sha(root)
    doc: dict[str, Any] = {
        'document_kind': SOAK_WORKING_SET_KIND,
        'template': False,
        'capture_id': f'hd033-live-soak-working-set-{sampled_at[:10]}',
        'kind': 'soak_process_working_set_sample',
        'started_at_utc': started_at,
        'sampled_at_utc': sampled_at,
        'operator_scope': 'soak_app_pid_working_set_sample_not_1_4_pane_not_ac29',
        'git_sha': sha,
        'start_git_sha': start.get('git_sha'),
        'start_capture': SOAK_START_REL,
        'pid': app_pid,
        'image': UI_EXE_NAME,
        'working_set_bytes': working,
        'private_bytes': sampled.get('private_bytes'),
        'handle_count': sampled.get('handle_count'),
        'process_count': 1,
        'sampler_pid': sampler_pid if isinstance(sampler_pid, int) else None,
        'resource_heartbeat_gitignored_path': RESOURCES_JSONL,
        'result': 'not_run',
        'evidence_level': 'soak_process_sample',
        'live_status': 'UNVERIFIED',
        'live_working_set': False,
        'live_soak': False,
        'ac27_passed': False,
        'ac28_passed': False,
        'ac29_passed': False,
        'ac46_passed': False,
        'eight_hour_soak_executed': False,
        'soak_hours': None,
        'disconnect_switch_count': None,
        'cycle_count': None,
        'visible_pane_count': None,
        'q_p_bytes': None,
        'derive_process_memory_from_q_p': False,
        'mib_bytes': 1048576,
        'one_quarter_pane_lab': False,
        'open_close_100': False,
        'l4_soak': 'UNVERIFIED',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'invented_timings': False,
        'product_ui_started': True,
        'process_running_at_capture': True,
        'sampler_added_to_start_owned_pids': False,
        'raw_gitignored_path': 'probe-results/hd033-soak-working-set.json',
        'committed_raw': True,
        'limitations': [
            'One soak-process working-set sample of the already running START App PID.',
            'This is not App plus WebView2 1/4 visible pane working set.',
            'This is not 100 open/close handle reclaim and is not AC29.',
            'live_working_set stays false. live-working-set.not-run.json stays the 1/4-pane lab not_run row.',
            'eight_hour_soak_executed stays false. soak_hours stays null. This is not AC46.',
            'The resource heartbeat appends probe-results/hd033-soak-resources.jsonl every 60s and exits when the App PID is gone.',
            'The sampler PID is not added to live-soak-start.json owned_pids. The existing soak heartbeat is left running.',
            'Do not derive process memory from Q_p. MiB is 1,048,576 bytes.',
            'Hosted CI is not an interactive desktop and must not run --record.',
        ],
    }
    _write_overlay(root, doc)
    after_start = start_path.read_text(encoding='utf-8')
    if after_start != before_start:
        raise QualityError('sampler_added_to_start_owned_pids')
    if before_not_run is not None:
        after_not_run = not_run_path.read_text(encoding='utf-8')
        if after_not_run != before_not_run:
            raise QualityError('invented_timings')
    report = validate_soak_working_set(root)
    if report is None:
        raise QualityError('missing_record_field')
    return doc


def _unrecorded_report() -> dict[str, Any]:
    return {
        'document_kind': SOAK_WORKING_SET_KIND,
        'soak_working_set_capture': SOAK_WORKING_SET_REL,
        'soak_start_capture': SOAK_START_REL,
        'live_working_set_row': LIVE_WORKING_SET_REL,
        'recorded': False,
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
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description='Validate or sample soak App PID working set. Not AC29.',
    )
    parser.add_argument(
        '--record',
        action='store_true',
        help='Sample the running START App PID. Do not use from CI.',
    )
    parser.add_argument('--heartbeat', action='store_true', help=argparse.SUPPRESS)
    parser.add_argument('--app-pid', default='', help=argparse.SUPPRESS)
    parser.add_argument('--heartbeat-path', default='', help=argparse.SUPPRESS)
    parser.add_argument('--started-at-utc', default='', help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    try:
        if args.heartbeat:
            if not args.app_pid or not args.heartbeat_path or not args.started_at_utc:
                raise QualityError('missing_record_field')
            pid = int(args.app_pid)
            if pid <= 0:
                raise QualityError('missing_record_field')
            return run_resource_heartbeat(
                pid, Path(args.heartbeat_path), args.started_at_utc
            )
        if args.record:
            record(ROOT)
        validate_eight_hour_soak_start(ROOT)
        report = validate_soak_working_set(ROOT)
        if report is None:
            report = _unrecorded_report()
    except QualityError as exc:
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2) + '\n', end='')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
