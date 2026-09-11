#!/usr/bin/env python3
"""Record a lab MakeAppx pack + SignTool overlay. Not AC41 or a release install.

Default CLI validates the optional overlay JSON and does not create certs,
pack, or sign. Pass --record on an interactive Windows desktop to create a
gitignored lab PFX, pack HerdDesk.Lab.msix, and apply a lab signature. CI and
justfile must not invoke --record, -Action Sign, or new_lab_certificate.ps1.
A lab pack+sign overlay is not AC41, not live install, and not a production
Publisher. Tests must inject hooks and must not create certs or change
display.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from typing import Any

SCRIPTS = Path(__file__).resolve().parent
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

ROOT = Path(__file__).resolve().parents[1]
LAB_MSIX_KIND = 'hd034_lab_msix'
LAB_MSIX_REL = 'evidence/packaging/live-lab-msix.json'
LAB_MSIX_POINTER_REL = 'evidence/packaging/lab-sign-overlay-pointer.json'
LAB_MSIX_NAME = 'HerdDesk.Lab.msix'
PROBE_JSON = 'probe-results/hd034-lab-msix.json'
PINNED_DOTNET_ROOT = Path(r'C:\Users\lyh\AppData\Local\herddesk-dotnet')
LAB_MSIX_NULL_KEYS = (
    'publisher',
    'publisher_cn',
    'certificate_subject',
    'certificate_thumbprint',
    'thumbprint',
    'timestamp_url',
    'distribution_url',
    'app_installer_url',
    'package_sha256',
    'msix_sha256',
    'appinstaller_sha256',
    'sidecar_sha256',
    'install_hours',
    'soak_hours',
)
LAB_MSIX_FALSE_KEYS = (
    'ac41_passed',
    'ac42_passed',
    'g0_passed',
    'signed_msix_built',
    'live_sign',
    'live_install',
    'publisher_identity_confirmed',
    'is_release_install',
    'herdr_executed',
    'pfx_in_git',
    'winui_admitted',
)
LAB_MSIX_TRUE_KEYS = (
    'fake_publisher_cannot_pass_ac41',
    'unsigned_local_build_cannot_pass_release_install',
)
_SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})


class LabMsixError(ValueError):
    """Stable overlay rule code. Message is the code only."""


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
        raise LabMsixError('missing_record_field')
    return sha


def _file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


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


def _find_pwsh() -> str:
    pwsh = shutil.which('pwsh')
    if not pwsh:
        raise LabMsixError('missing_record_field')
    return pwsh


def _parse_ps_json_object(text: str) -> dict[str, Any]:
    blob = (text or '').strip().lstrip('\ufeff')
    if not blob:
        return {}
    try:
        value = json.loads(blob)
        return value if isinstance(value, dict) else {}
    except json.JSONDecodeError:
        start = blob.find('{')
        end = blob.rfind('}')
        if start >= 0 and end > start:
            try:
                value = json.loads(blob[start : end + 1])
                return value if isinstance(value, dict) else {}
            except json.JSONDecodeError:
                return {}
        return {}


def _load_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding='utf-8'))
    except (OSError, json.JSONDecodeError, UnicodeError) as exc:
        raise LabMsixError('missing_record_field') from exc
    if not isinstance(value, dict):
        raise LabMsixError('missing_record_field')
    return value


def _is_success(value: Any) -> bool:
    if value is True:
        return True
    if isinstance(value, str) and value.strip().lower() in _SUCCESS:
        return True
    return False


def _token(value: Any) -> Any:
    return value.strip().lower() if isinstance(value, str) else value


def _msix_path(output_root: Path) -> Path:
    return Path(output_root) / LAB_MSIX_NAME


def _write_overlay(root: Path, doc: dict[str, Any]) -> Path:
    dest = root / LAB_MSIX_REL
    dest.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(doc, ensure_ascii=False, indent=2) + '\n'
    dest.write_text(text, encoding='utf-8')
    probe = root / 'probe-results'
    probe.mkdir(parents=True, exist_ok=True)
    (probe / 'hd034-lab-msix.json').write_text(text, encoding='utf-8')
    return dest


def validate_lab_msix(root: Path) -> dict[str, Any] | None:
    """Optional lab pack+sign overlay. Missing is allowed. Not AC41.

    Do not call _reject_hd034_pass_claims or _reject_hd034_invented_identity
    on this document: lab_msix_packed and lab_signature_applied may be true
    here after a real MakeAppx/SignTool run. Catalog, L2, pointer, and
    not-run captures stay fail-closed. signed_msix_built stays false.
    """
    root = Path(root)
    path = root / LAB_MSIX_REL
    if not path.is_file():
        return None
    doc = _load_json(path)
    if doc.get('document_kind') != LAB_MSIX_KIND:
        raise LabMsixError('missing_record_field')
    if doc.get('result') != 'not_run' or _is_success(doc.get('result')):
        raise LabMsixError('live_success_claimed')
    if _token(doc.get('phase_gate')) in {'passed', 'pass', 'ok'}:
        raise LabMsixError('phase_gate')
    for key, value in doc.items():
        if isinstance(key, str) and key.endswith('_passed') and value is not False:
            raise LabMsixError(key)
    for key in LAB_MSIX_FALSE_KEYS:
        if key in doc and doc.get(key) is not False:
            raise LabMsixError(key if key.endswith('_passed') else f'{key}_claimed')
    for key in LAB_MSIX_TRUE_KEYS:
        if doc.get(key) is not True:
            raise LabMsixError('missing_record_field')
    for key in LAB_MSIX_NULL_KEYS:
        if key in doc and doc.get(key) is not None:
            raise LabMsixError('invented_identity')
    packed = doc.get('lab_msix_packed')
    signed = doc.get('lab_signature_applied')
    makeappx = doc.get('makeappx_found')
    signtool = doc.get('signtool_found')
    if packed is not True and packed is not False:
        raise LabMsixError('missing_record_field')
    if signed is not True and signed is not False:
        raise LabMsixError('missing_record_field')
    if makeappx is not True and makeappx is not False:
        raise LabMsixError('missing_record_field')
    if signtool is not True and signtool is not False:
        raise LabMsixError('missing_record_field')
    digest = doc.get('lab_msix_sha256')
    if packed is True:
        if makeappx is not True:
            raise LabMsixError('lab_msix_claimed')
        if not isinstance(digest, str) or len(digest) != 64:
            raise LabMsixError('lab_msix_claimed')
        if any(ch not in '0123456789abcdef' for ch in digest):
            raise LabMsixError('lab_msix_claimed')
        msix = root / 'artifacts' / 'packaging' / LAB_MSIX_NAME
        if msix.is_file() and _file_sha256(msix) != digest:
            raise LabMsixError('lab_msix_claimed')
    else:
        if digest is not None:
            raise LabMsixError('invented_identity')
        if signed is True:
            raise LabMsixError('lab_signature_claimed')
    if signed is True:
        if packed is not True or signtool is not True:
            raise LabMsixError('lab_signature_claimed')
    git_sha = doc.get('git_sha')
    if not isinstance(git_sha, str) or len(git_sha) < 7:
        raise LabMsixError('missing_record_field')
    pointer_path = root / LAB_MSIX_POINTER_REL
    if pointer_path.is_file():
        pointer = _load_json(pointer_path)
        if pointer.get('signed_msix_built') is not False:
            raise LabMsixError('signed_msix_built')
        if pointer.get('live_sign') is not False:
            raise LabMsixError('live_sign_claimed')
        if pointer.get('pfx_written') is not False:
            raise LabMsixError('pfx_written_claimed')
        if pointer.get('ac41_passed') is not False:
            raise LabMsixError('ac41_passed')
    return {
        'document_kind': LAB_MSIX_KIND,
        'lab_msix_capture': LAB_MSIX_REL,
        'recorded': True,
        'lab_msix_packed': packed is True,
        'lab_signature_applied': signed is True,
        'makeappx_found': makeappx is True,
        'signtool_found': signtool is True,
        'signed_msix_built': False,
        'live_sign': False,
        'live_install': False,
        'publisher_identity_confirmed': False,
        'ac41_passed': False,
        'ac42_passed': False,
        'is_release_install': False,
        'result': 'not_run',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'git_sha': git_sha,
    }


def _live_create_certificate(root: Path, certificate_path: Path) -> dict[str, Any]:
    pwsh = _find_pwsh()
    cmd = [
        pwsh,
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-File',
        str(root / 'scripts' / 'new_lab_certificate.ps1'),
        '-CertificatePath',
        str(certificate_path),
    ]
    completed = subprocess.run(
        cmd,
        cwd=str(root),
        env=_dotnet_env(),
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        timeout=120,
        check=False,
    )
    report = _parse_ps_json_object(completed.stdout) or _parse_ps_json_object(
        completed.stderr
    )
    report['exit_code'] = completed.returncode if completed.returncode is not None else 1
    return report


def _live_package_action(
    root: Path,
    action: str,
    output_root: Path,
    *,
    certificate_path: Path | None = None,
    restore: bool = False,
    timeout: int = 120,
) -> dict[str, Any]:
    pwsh = _find_pwsh()
    cmd = [
        pwsh,
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-File',
        str(root / 'scripts' / 'package_release.ps1'),
        '-Action',
        action,
        '-OutputRoot',
        str(output_root),
    ]
    if restore:
        cmd.append('-Restore')
    if certificate_path is not None:
        cmd += ['-CertificatePath', str(certificate_path)]
    completed = subprocess.run(
        cmd,
        cwd=str(root),
        env=_dotnet_env(),
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        timeout=timeout,
        check=False,
    )
    report_path = Path(output_root) / 'package-report.json'
    report: dict[str, Any] = {}
    if report_path.is_file():
        loaded = json.loads(report_path.read_text(encoding='utf-8'))
        if isinstance(loaded, dict):
            report = loaded
    if not report:
        report = _parse_ps_json_object(completed.stdout) or _parse_ps_json_object(
            completed.stderr
        )
    report['exit_code'] = completed.returncode if completed.returncode is not None else 1
    return report


def record(
    root: Path,
    *,
    create_certificate: Any = None,
    run_build: Any = None,
    run_sign: Any = None,
    git_sha: str | None = None,
    now: str | None = None,
    output_root: Path | None = None,
    certificate_path: Path | None = None,
) -> dict[str, Any]:
    """Create lab PFX, MakeAppx pack, SignTool sign. Injected hooks skip certs.

    Linux record() without hooks fail-closes. Tests must not create
    certificates or change the developer display.
    """
    root = Path(root)
    hooks = (
        create_certificate is not None
        and run_build is not None
        and run_sign is not None
    )
    if os.name != 'nt' and not hooks:
        raise LabMsixError('missing_record_field')
    started_at = now if now is not None else _utc_now()
    sha = git_sha if git_sha is not None else _git_sha(root)
    out = Path(output_root) if output_root is not None else root / 'artifacts' / 'packaging'
    cert = (
        Path(certificate_path)
        if certificate_path is not None
        else root / 'artifacts' / 'certs' / 'HerdDesk.Lab.pfx'
    )
    creator = create_certificate if create_certificate is not None else _live_create_certificate
    builder = run_build if run_build is not None else None
    signer = run_sign if run_sign is not None else None
    if hooks:
        cert_report = creator(root, cert)
    else:
        cert.parent.mkdir(parents=True, exist_ok=True)
        cert_report = creator(root, cert)
    if not isinstance(cert_report, dict):
        raise LabMsixError('missing_record_field')
    if hooks:
        build_report = builder(root, out)
    else:
        out.mkdir(parents=True, exist_ok=True)
        build_report = _live_package_action(
            root, 'Build', out, restore=True, timeout=900
        )
    if not isinstance(build_report, dict):
        raise LabMsixError('missing_record_field')
    msix = _msix_path(out)
    build_ok = build_report.get('ok') is True
    package_path = build_report.get('package_path')
    packed = (
        msix.is_file()
        and build_ok
        and isinstance(package_path, str)
        and package_path.strip() != ''
    )
    makeappx_found = packed
    digest = _file_sha256(msix) if packed else None
    sign_report: dict[str, Any] = {}
    signature_applied = False
    signtool_found = False
    if packed:
        if hooks:
            sign_report = signer(root, out, cert)
        else:
            sign_report = _live_package_action(
                root, 'Sign', out, certificate_path=cert, timeout=180
            )
        if not isinstance(sign_report, dict):
            raise LabMsixError('missing_record_field')
        error = str(sign_report.get('error') or '')
        signtool_found = 'signtool.exe not found' not in error
        signature_applied = (
            sign_report.get('ok') is True
            and sign_report.get('signed') is True
            and packed
        )
        if signature_applied:
            signtool_found = True
            digest = _file_sha256(msix)
    captured_at = now if now is not None else _utc_now()
    doc: dict[str, Any] = {
        'document_kind': LAB_MSIX_KIND,
        'template': False,
        'capture_id': f'hd034-live-lab-msix-{captured_at[:10]}',
        'kind': 'lab_msix_pack_sign',
        'started_at_utc': started_at,
        'captured_at_utc': captured_at,
        'operator_scope': 'lab_msix_pack_sign_not_ac41_not_release_publisher',
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
        'evidence_level': 'lab_msix_overlay',
        'live_status': 'UNVERIFIED',
        'live_sign': False,
        'live_install': False,
        'ac41_passed': False,
        'ac42_passed': False,
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
        'winui_admitted': False,
        'signed_msix_built': False,
        'publisher_identity_confirmed': False,
        'is_release_install': False,
        'lab_identity_not_release': True,
        'lab_identity_not_store': True,
        'fake_publisher_cannot_pass_ac41': True,
        'unsigned_local_build_cannot_pass_release_install': True,
        'lab_msix_packed': packed,
        'lab_signature_applied': signature_applied,
        'makeappx_found': bool(makeappx_found),
        'signtool_found': bool(signtool_found),
        'lab_msix_sha256': digest,
        'publisher': None,
        'publisher_cn': None,
        'certificate_subject': None,
        'certificate_thumbprint': None,
        'thumbprint': None,
        'timestamp_url': None,
        'distribution_url': None,
        'app_installer_url': None,
        'package_sha256': None,
        'msix_sha256': None,
        'appinstaller_sha256': None,
        'sidecar_sha256': None,
        'install_hours': None,
        'soak_hours': None,
        'pfx_in_git': False,
        'raw_gitignored_path': PROBE_JSON,
        'committed_raw': True,
        'build_ok': build_report.get('ok') is True,
        'sign_ok': sign_report.get('ok') is True if sign_report else False,
        'cert_ok': cert_report.get('ok') is True,
        'limitations': [
            'Lab MakeAppx pack plus SignTool overlay on this interactive desktop.',
            'lab_msix_packed may be true on this file only if MakeAppx wrote HerdDesk.Lab.msix.',
            'lab_signature_applied may be true on this file only if SignTool signed that MSIX.',
            'This is not AC41. Fake/lab Publisher cannot pass AC41. Not a production Publisher.',
            'signed_msix_built stays false. live_sign stays false. live_install stays false.',
            'msix_sha256 stays null. Use lab_msix_sha256 for the lab artifact hash.',
            'PFX and MSIX stay gitignored under artifacts/certs and artifacts/packaging.',
            'Do not install the packed lab MSIX. Hosted CI must not run --record, -Action Sign, or new_lab_certificate.ps1.',
        ],
    }
    _write_overlay(root, doc)
    report = validate_lab_msix(root)
    if report is None:
        raise LabMsixError('missing_record_field')
    return doc


def _unrecorded_report() -> dict[str, Any]:
    return {
        'document_kind': LAB_MSIX_KIND,
        'lab_msix_capture': LAB_MSIX_REL,
        'recorded': False,
        'lab_msix_packed': False,
        'lab_signature_applied': False,
        'signed_msix_built': False,
        'live_sign': False,
        'live_install': False,
        'ac41_passed': False,
        'ac42_passed': False,
        'result': 'not_run',
        'g0_passed': False,
        'phase_gate': 'not_passed',
        'herdr_executed': False,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description='Validate or record a lab MSIX pack+sign overlay. Not AC41.',
    )
    parser.add_argument(
        '--record',
        action='store_true',
        help='Create lab PFX, MakeAppx pack, SignTool sign. Do not use from CI.',
    )
    args = parser.parse_args(argv)
    try:
        if args.record:
            record(ROOT)
        report = validate_lab_msix(ROOT)
        if report is None:
            report = _unrecorded_report()
    except LabMsixError as exc:
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2) + '\n', end='')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
