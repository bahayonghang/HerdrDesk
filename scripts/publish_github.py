#!/usr/bin/env python3
"""Audit the historical PUBLICATION_MANIFEST.json first-import bundle.

Default (no --publish): offline historical-bundle audit. Compare listed files
to the frozen SHA-256 snapshot from the 2026-09-07 source import. Later
maintenance can change listed bytes; exit 2 then reports bundle drift.
Daily offline G0 gate is `just ci`, not this audit and not a hash refresh.

--publish is the historical empty-repo create path. It refuses an existing
GitHub repository and any checkout that already has .git. Do not run
--publish against bahayonghang/HerdrDesk. Default never publishes and does
not use the network.

No tokens are requested or stored. Only files listed in the reviewed
publication manifest would be staged on --publish.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
OWNER = 'bahayonghang'
NAME = 'HerdrDesk'
MANIFEST = 'PUBLICATION_MANIFEST.json'


class PublishError(RuntimeError):
    pass


def checked(argv: list[str], root: Path, *, timeout: int = 120) -> str:
    try:
        result = subprocess.run(argv, cwd=root, text=True, encoding='utf-8',
                                capture_output=True, timeout=timeout, check=False)
    except (OSError, subprocess.SubprocessError) as exc:
        raise PublishError(f'{argv[0]} failed: {type(exc).__name__}') from exc
    if result.returncode:
        # gh/git may include account paths or credentials in diagnostics.
        # Run the failing command locally for details; never embed those in reports.
        raise PublishError(f'{argv[0]} {argv[1]} exited {result.returncode}; no force push or deletion attempted')
    return result.stdout.strip()


def validate_manifest(root: Path) -> list[str]:
    try:
        manifest = json.loads((root / MANIFEST).read_text(encoding='utf-8'))
        items = manifest['files']
    except (OSError, ValueError, KeyError) as exc:
        raise PublishError('Missing or invalid PUBLICATION_MANIFEST.json') from exc
    if manifest.get('repository') != f'{OWNER}/{NAME}' or manifest.get('visibility') != 'public':
        raise PublishError('Publication target or visibility does not match the reviewed bundle')
    if not isinstance(items, dict) or not items:
        raise PublishError('Publication manifest has no files')
    allowed: list[str] = []
    for relative, expected in items.items():
        path = Path(relative)
        if path.is_absolute() or '..' in path.parts or '\\' in relative or relative.startswith('-'):
            raise PublishError('Unsafe manifest path')
        target = root / path
        try:
            target.resolve().relative_to(root.resolve())
        except ValueError as exc:
            raise PublishError('Manifest file escapes the source root') from exc
        if target.is_symlink() or not target.is_file():
            raise PublishError(f'Publication file missing or symlink: {relative}')
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        if digest != expected:
            raise PublishError(
                f'Publication file changed: {relative}; historical first-import bundle; daily gate is just ci')
        allowed.append(relative)
    return sorted(allowed) + [MANIFEST]


def validate_identity(profile: Any) -> tuple[str, int]:
    if not isinstance(profile, dict) or profile.get('login') != OWNER:
        raise PublishError(f'Authenticated account is not {OWNER}; no GitHub changes made')
    ident = profile.get('id')
    if type(ident) is not int or ident < 1:
        raise PublishError('GitHub profile has no valid numeric ID')
    return OWNER, ident


def create_command(root: Path) -> list[str]:
    return ['gh', 'repo', 'create', f'{OWNER}/{NAME}', '--public',
            '--source', str(root), '--remote', 'origin', '--push', '--disable-wiki',
            '--description', 'Windows-native herdr console; G0 protocol and safety implementation in progress.']


def publish(root: Path, files: list[str]) -> dict[str, Any]:
    for tool in ('gh', 'git'):
        if not shutil.which(tool):
            raise PublishError(f'{tool} is not installed or not on PATH')
    # Refuse to mutate any pre-existing checkout, including a parent repository.
    if (root / '.git').exists() or any((parent / '.git').exists() for parent in root.parents):
        raise PublishError('Extract into a fresh directory outside existing Git repositories')
    profile = json.loads(checked(['gh','api','user'], root))
    login, user_id = validate_identity(profile)
    # A successful read proves the repository already exists; abort, never replace it.
    existing = subprocess.run(['gh','repo','view',f'{OWNER}/{NAME}',
                              '--json','nameWithOwner'],cwd=root,capture_output=True,
                              timeout=60,check=False)
    if existing.returncode == 0:
        raise PublishError('Repository already exists; use a reviewed branch/PR workflow instead')
    # A failed read is NOT treated as proof of absence. The create call below
    # can reject conflicts/permissions atomically and will not overwrite a repo.
    checked([sys.executable,'scripts/validate_repository.py'],root)
    checked([sys.executable,'-m','unittest','discover','-s','tests/python','-v'],root)
    checked([sys.executable,'scripts/probe_herdr.py','selftest'],root)
    checked(['git','init','-b','main'],root)
    # Stage only audited manifest paths, not arbitrary local secrets or probe logs.
    checked(['git','add','--',*files],root)
    checked(['git','-c',f'user.name={login}',
             '-c',f'user.email={user_id}+{login}@users.noreply.github.com',
             'commit','-m','feat(g0): bootstrap protocol validation and safety fixtures'],root)
    checked(create_command(root),root,timeout=180)
    remote=json.loads(checked(['gh','repo','view',f'{OWNER}/{NAME}',
                              '--json','nameWithOwner,visibility,url'],root))
    if remote.get('nameWithOwner') != f'{OWNER}/{NAME}' or remote.get('visibility') != 'PUBLIC':
        raise PublishError('Remote name/visibility verification failed; inspect GitHub before retrying')
    local=checked(['git','rev-parse','HEAD'],root)
    heads=checked(['git','ls-remote','origin','refs/heads/main'],root)
    if not heads or heads.split()[0] != local:
        raise PublishError('Remote main commit was not verified; repository may exist with push incomplete')
    return {'repository':remote['nameWithOwner'],'visibility':'public',
            'url':remote['url'],'commit':local,'push_verified':True,
            'ci_status':'not_checked','windows_acceptance':'not_run'}


def dry_run_report(*, ok: bool, files: list[str] | None = None, error: str | None = None) -> dict[str, Any]:
    report: dict[str, Any] = {
        'mode': 'offline_dry_run',
        'kind': 'historical_bundle_audit',
        'daily_gate': 'just ci',
        'repository': f'{OWNER}/{NAME}',
        'visibility': 'public',
        'github_changed': False,
        'ok': ok,
    }
    if files is not None:
        report['files_to_stage'] = len(files)
        report['command'] = create_command(ROOT)
    if error is not None:
        report['error'] = error
    return report


def main(argv: list[str] | None = None) -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish',action='store_true',
                        help='Historical empty-repo create. Refuses an existing repository. Daily gate is just ci.')
    args=parser.parse_args(argv)
    try:
        files=validate_manifest(ROOT)
    except (PublishError,OSError,ValueError) as exc:
        if not args.publish:
            print(json.dumps(dry_run_report(ok=False, error=str(exc)),ensure_ascii=False,indent=2))
        print(f'Publish stopped: {exc}',file=sys.stderr)
        return 2
    try:
        if not args.publish:
            print(json.dumps(dry_run_report(ok=True, files=files),ensure_ascii=False,indent=2))
            return 0
        print(json.dumps(publish(ROOT,files),ensure_ascii=False,indent=2))
        return 0
    except (PublishError,OSError,ValueError,subprocess.SubprocessError) as exc:
        print(f'Publish stopped: {exc}',file=sys.stderr)
        return 2


if __name__=='__main__':raise SystemExit(main())
