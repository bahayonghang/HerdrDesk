#!/usr/bin/env python3
"""HerdDesk compatibility probes (Python 3.10+, stdlib only).

Not an application implementation. Defaults never send input or use takeover.
Only the subprocess started by this script is terminated. No daemon lifecycle
commands, package installation, SSH deployment, or process-tree termination.
"""
from __future__ import annotations
import argparse
import base64
import binascii
import hashlib
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import sys
import threading
import time
from typing import Any
from herddesk_g0.protocol import (
    validate_frame, validate_input, strict_json_loads, classify_stream_end,
)

MAX_LINE = 16 * 1024 * 1024
MAX_FRAME = 8 * 1024 * 1024
MAX_STDERR = 64 * 1024


def is_int(value: Any) -> bool:
    return type(value) is int


def stop_owned(process: subprocess.Popen[bytes]) -> None:
    """Terminate ONLY this probe's direct child, never a tree or herdr daemon."""
    if process.poll() is not None:
        return
    process.terminate()
    try:
        process.wait(timeout=1.0)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=1.0)


def capture(argv: list[str], timeout: float, stdout_limit: int = MAX_LINE) -> dict[str, Any]:
    p = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                         stderr=subprocess.PIPE, shell=False)
    buffers = [bytearray(), bytearray()]
    caps = [stdout_limit, MAX_STDERR]
    overflow = threading.Event()
    errors: list[str] = []

    def drain(index: int, stream: Any) -> None:
        try:
            while True:
                block = stream.read(4096)
                if not block:
                    return
                room = max(0, caps[index] - len(buffers[index]))
                buffers[index].extend(block[:room])
                if len(block) > room:
                    overflow.set()
        except OSError:
            errors.append('capture stream read failed')

    threads = [threading.Thread(target=drain, args=(i, st), daemon=True)
               for i, st in enumerate((p.stdout, p.stderr))]
    for t in threads:
        t.start()
    start = time.monotonic()
    timed_out = False
    try:
        while p.poll() is None:
            if overflow.is_set():
                stop_owned(p)
                break
            if time.monotonic() - start > timeout:
                timed_out = True
                stop_owned(p)
                break
            time.sleep(.02)
    finally:
        stop_owned(p)
        for t in threads:
            t.join(timeout=1)
        for st in (p.stdout, p.stderr):
            if st:
                st.close()
    return dict(argv=argv, returncode=p.returncode, timed_out=timed_out,
                overflow=overflow.is_set(), duration_ms=round((time.monotonic()-start)*1000, 1),
                stdout=bytes(buffers[0]), stderr=bytes(buffers[1]), errors=errors)


def safe_summary(result: dict[str, Any], diagnostics: bool = False) -> dict[str, Any]:
    out = {k: result[k] for k in ('returncode','timed_out','overflow','duration_ms','errors')}
    out.update(stdout_bytes=len(result['stdout']), stderr_bytes=len(result['stderr']))
    if diagnostics:
        out['stderr_diagnostic_opt_in'] = result['stderr'].decode('utf-8', errors='replace')
    return out


def executable(path: str) -> str:
    candidate = shutil.which(path)
    if not candidate:
        raise ValueError('herdr executable not found; pass --herdr with an installed path')
    return str(Path(candidate).resolve())


def base_command(args: argparse.Namespace) -> list[str]:
    argv = [executable(args.herdr)]
    if args.session:
        if any(c in args.session for c in '\x00\r\n'):
            raise ValueError('invalid session identifier')
        argv += ['--session', args.session]
    return argv


def preflight(args: argparse.Namespace) -> dict[str, Any]:
    base = base_command(args)
    exe = Path(base[0])
    report: dict[str, Any] = {'kind':'preflight', 'os':sys.platform,
        'binary_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),
        'checked_at_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
        'windows_gui_tested':False, 'checks':{}}
    commands = [('version',[base[0],'--version']),
                ('schema',base+['api','schema','--json']),
                ('snapshot',base+['api','snapshot']),
                ('observe_help',base+['terminal','session','observe','--help']),
                ('control_help',base+['terminal','session','control','--help'])]
    for name, argv in commands:
        result = capture(argv, args.timeout)
        info = safe_summary(result, args.include_diagnostics)
        report['checks'][name] = info
        if result['returncode'] or result['timed_out'] or result['overflow']:
            continue
        if name == 'version':
            info['version_text'] = result['stdout'].decode('utf-8',errors='replace').strip()[:512]
        elif name in ('schema','snapshot'):
            try:
                data = strict_json_loads(result['stdout'])
                if not isinstance(data, dict):
                    raise ValueError('root is not an object')
                info['json_valid'] = True
                if name == 'schema':
                    if not is_int(data.get('protocol')) or not is_int(data.get('schema_version')):
                        raise ValueError('schema protocol/schema_version must be integers')
                    info['matches_reference_header'] = data['protocol'] == 20 and data['schema_version'] == 1
                    info.update(protocol=data.get('protocol'), schema_version=data.get('schema_version'),
                                sha256=hashlib.sha256(result['stdout']).hexdigest())
                    # Export only the non-session schema. Never overwrite without explicit consent.
                    schema_path = Path(args.output).with_suffix('.schema.json')
                    write_new(schema_path, result['stdout'], args.overwrite)
                    info['schema_export'] = schema_path.name
                else:
                    info['root_keys'] = sorted(data.keys())
                    info['has_error'] = 'error' in data
                    if args.include_snapshot:
                        info['snapshot_opt_in'] = data
            except (ValueError, UnicodeError) as exc:
                info['json_valid'] = False
                info['parse_error'] = str(exc)
    report['all_readonly_commands_succeeded'] = all(
        r['returncode'] == 0 and not r['timed_out'] and not r['overflow']
        and r.get('json_valid', True) and not r.get('has_error', False)
        for r in report['checks'].values())
    report['scope'] = 'Read-only CLI diagnostics; does not test event subscription, GUI, IME or control.'
    return report


def stream_probe(args: argparse.Namespace) -> dict[str, Any]:
    if args.mode == 'control' and not args.disposable_target:
        raise ValueError('control requires --disposable-target; acquisition itself changes control/size')
    if args.input_file and (args.mode != 'control' or not args.allow_input or not args.disposable_target):
        raise ValueError('input needs control + --allow-input + --disposable-target')
    if not args.target or args.target.startswith('-') or any(c in args.target for c in '\x00\r\n'):
        raise ValueError('invalid target')
    argv = base_command(args) + ['terminal','session',args.mode,args.target,
                               '--cols',str(args.cols),'--rows',str(args.rows)]
    input_command = None
    if args.input_file:
        f = Path(args.input_file)
        if f.stat().st_size > 65536:
            raise ValueError('input file is limited to 64 KiB for this probe')
        text = f.read_text(encoding='utf-8')
        input_command = {'type':'terminal.input','text':text}
        validate_input(input_command)
    p = subprocess.Popen(argv, stdin=subprocess.PIPE if args.mode == 'control' else subprocess.DEVNULL,
                         stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=False)
    items: queue.Queue[tuple[str, Any]] = queue.Queue(maxsize=4)
    stop = threading.Event()
    stderr = bytearray()
    stderr_truncated = threading.Event()

    def offer(kind: str, data: Any) -> None:
        while not stop.is_set():
            try:
                items.put((kind, data), timeout=.1)
                return
            except queue.Full:
                pass

    def stdout_reader() -> None:
        try:
            while not stop.is_set():
                line = p.stdout.readline(MAX_LINE + 1)  # binary; bounded per NDJSON line
                if not line:
                    offer('eof', None)
                    return
                if len(line) > MAX_LINE:
                    offer('error', 'NDJSON line exceeds safety budget')
                    return
                if not line.endswith(b'\n'):
                    offer('error', 'unterminated NDJSON line at EOF')
                    return
                if line.strip():
                    offer('line', line)
        except OSError:
            offer('error', 'stdout read failed')

    def stderr_reader() -> None:
        try:
            while True:
                data = p.stderr.read(4096)
                if not data:
                    return
                room = max(0, MAX_STDERR-len(stderr))
                stderr.extend(data[:room])
                if len(data) > room:
                    stderr_truncated.set()
        except OSError:
            pass

    threads = [threading.Thread(target=f, daemon=True) for f in (stdout_reader, stderr_reader)]
    for t in threads:
        t.start()
    start = time.monotonic()
    report: dict[str, Any] = {'kind':args.mode, 'checked_at_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),
        'takeover_used':False, 'input_sent':False, 'frames':[], 'errors':[],
        'terminal_closed_seen':False, 'stdout_eof_seen':False, 'scope':'wire-shape probe, NOT TUI/IME acceptance'}
    previous = None
    frame_count = 0
    try:
        while time.monotonic()-start < args.seconds:
            try:
                kind, value = items.get(timeout=.1)
            except queue.Empty:
                continue
            if kind == 'eof':
                report['stdout_eof_seen'] = True
                break
            if kind == 'error':
                report['errors'].append(value)
                break
            try:
                obj = strict_json_loads(value)
                previous, meta = validate_frame(obj, previous)
                if meta['type'] == 'terminal.closed':
                    report['terminal_closed_seen'] = True
                    break
                frame_count += 1
                if len(report['frames']) < 200:
                    report['frames'].append(meta)
                if input_command and not report['input_sent']:
                    # No implicit Enter/newline is appended to the user's text.
                    p.stdin.write(json.dumps(input_command,ensure_ascii=False).encode('utf-8')+b'\n')
                    p.stdin.flush()
                    report['input_sent'] = True
                    payload=input_command['text'].encode('utf-8')
                    report['input_bytes'] = len(payload)
                    report['input_sha256'] = hashlib.sha256(payload).hexdigest()
            except (ValueError, UnicodeError, BrokenPipeError, OSError) as exc:
                report['errors'].append(type(exc).__name__+': '+str(exc))
                break
    finally:
        if args.mode == 'control' and p.poll() is None:
            try:
                p.stdin.write(b'{"type":"terminal.release"}\n')
                p.stdin.flush()
                p.stdin.close()
                p.wait(timeout=.8)
            except (OSError, subprocess.TimeoutExpired):
                pass
        stop.set()
        stop_owned(p)
        for t in threads:
            t.join(timeout=1)
        for st in (p.stdout,p.stderr,p.stdin):
            if st and not st.closed:
                st.close()
    report.update(frame_count=frame_count, metadata_capped_at=200,
                  duration_ms=round((time.monotonic()-start)*1000,1),
                  bridge_exit_code=p.returncode, stderr_bytes=len(stderr),
                  stderr_truncated=stderr_truncated.is_set())
    end = classify_stream_end(
        stdout_eof_seen=report['stdout_eof_seen'],
        terminal_closed_seen=report['terminal_closed_seen'],
        bridge_process_exited=p.returncode is not None,
        pane_alive_observed=None,
        daemon_alive_observed=None,
    )
    report['stream_end'] = end['kind']
    report['pane_exit_verified'] = end['pane_exit_verified']
    report['control_verified'] = False
    report['wire_shape_checks_passed'] = frame_count > 0 and not report['errors']
    report['release_acknowledged'] = False  # no fabricated command acknowledgement
    if args.include_diagnostics:
        report['stderr_diagnostic_opt_in'] = stderr.decode('utf-8',errors='replace')
    return report


def write_new(path: Path, raw: bytes, overwrite: bool = False) -> None:
    path.parent.mkdir(parents=True,exist_ok=True)
    with path.open('wb' if overwrite else 'xb') as f:
        f.write(raw)


def selftest() -> dict[str, Any]:
    folder=Path(__file__).resolve().parent.parent/'tests'/'fixtures'
    seq = None
    checks = 0
    for line in (folder/'terminal-valid.ndjson').read_text().splitlines():
        seq,_=validate_frame(strict_json_loads(line),seq)
        checks+=1
    for line in (folder/'input-valid.ndjson').read_text().splitlines():
        validate_input(strict_json_loads(line)); checks+=1
    for case in strict_json_loads((folder/'invalid-cases.json').read_text()):
        try:
            (validate_frame if case['kind']=='frame' else validate_input)(case['message'])
        except (ValueError,binascii.Error):
            checks+=1
        else:
            raise AssertionError('invalid fixture was accepted: '+case['name'])
    # Exercise bounded capture and direct-child cleanup on the current platform.
    normal=capture([sys.executable,'-c','print("ok")'],2,1024)
    assert normal['returncode']==0 and normal['stdout'].strip()==b'ok';checks+=1
    timed=capture([sys.executable,'-c','import time;time.sleep(3)'],.1,1024)
    assert timed['timed_out'];checks+=1
    big=capture([sys.executable,'-c','import sys;sys.stdout.write("x"*8192)'],2,1024)
    assert big['overflow'] and len(big['stdout'])<=1024;checks+=1
    from herddesk_g0.lease import map_lease
    eof=classify_stream_end(stdout_eof_seen=True)
    assert eof['kind']=='stdout_eof' and eof['pane_exit_verified'] is False;checks+=1
    mapped=map_lease({'operation':'observe','access_before':'disconnected',
                      'control_verified_before':False,'first_frame_seen':True,
                      'process_alive':True,'window_focused':True})
    assert mapped['access']=='observing' and mapped['control_verified'] is False;checks+=1
    granted=map_lease({'operation':'request_control','access_before':'observing',
                       'control_verified_before':False,
                       'observed_wire_type':'terminal.granted',
                       'adapter_proved_write_ownership':True})
    assert granted['code']=='fictional_granted_rejected' and granted['control_verified'] is False
    checks+=1
    return {'selftest_passed':True,'checks':checks,'platform':sys.platform,
            'uses_synthetic_fixtures':True,'herdr_executed':False,'windows_gui_tested':False}


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    subs=parser.add_subparsers(dest='mode',required=True)
    subs.add_parser('selftest')
    for mode in ('preflight','observe','control'):
        p=subs.add_parser(mode)
        p.add_argument('--herdr',default='herdr',help='installed executable, preferably absolute path')
        p.add_argument('--session',default=None)
        p.add_argument('--output',required=True)
        p.add_argument('--overwrite',action='store_true')
        p.add_argument('--include-diagnostics',action='store_true')
        if mode=='preflight':
            p.add_argument('--timeout',type=float,default=10)
            p.add_argument('--include-snapshot',action='store_true',help='may expose private session metadata')
        else:
            p.add_argument('--target',required=True)
            p.add_argument('--seconds',type=float,default=5)
            p.add_argument('--cols',type=int,default=120)
            p.add_argument('--rows',type=int,default=40)
            p.add_argument('--disposable-target',action='store_true')
            p.add_argument('--allow-input',action='store_true')
            p.add_argument('--input-file',default=None,help='UTF-8 text, sent once without an added Enter')
    args=parser.parse_args()
    try:
        if args.mode=='selftest':
            print(json.dumps(selftest(),ensure_ascii=False,indent=2));return 0
        if Path(args.output).exists() and not args.overwrite:
            raise ValueError('output exists; choose a new path or explicitly pass --overwrite')
        if args.mode=='preflight':
            if not 0 < args.timeout <= 120:
                raise ValueError('timeout must be in (0,120] seconds')
            report=preflight(args)
        else:
            if not 0 < args.seconds <= 120:
                raise ValueError('seconds must be in (0,120]')
            if not 0 < args.cols <= 65535 or not 0 < args.rows <= 65535:
                raise ValueError('rows/cols must be positive u16')
            report=stream_probe(args)
        write_new(Path(args.output),json.dumps(report,ensure_ascii=False,indent=2).encode('utf-8'),args.overwrite)
        print('Report written:',args.output)
        ok=report.get('all_readonly_commands_succeeded',report.get('wire_shape_checks_passed',False))
        return 0 if ok else 2
    except (OSError,ValueError,AssertionError) as exc:
        print('Probe failed:',exc,file=sys.stderr)
        return 2

if __name__=='__main__':
    raise SystemExit(main())
