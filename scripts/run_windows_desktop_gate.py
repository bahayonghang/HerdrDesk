#!/usr/bin/env python3
"""Windows desktop restore gate. Skip is not full-application green."""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import json
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
APP_PROJECT = Path('src/HerdDesk.App/HerdDesk.App.csproj')
APP_LOCK = Path('src/HerdDesk.App/packages.lock.json')
PACKAGES_PROPS = Path('Directory.Packages.props')
WINDOWS_TFM = 'net10.0-windows10.0.19041.0'


@dataclass(frozen=True)
class DesktopGatePlan:
    action: str
    reason: str
    commands: tuple[tuple[str, ...], ...]


def load_desktop_record(root: Path) -> dict:
    path = Path(root) / 'implementation' / 'hd-007-packages.json'
    return json.loads(path.read_text(encoding='utf-8'))


def admitted_lock_files_present(root: Path) -> bool:
    root = Path(root)
    return (
        (root / PACKAGES_PROPS).is_file()
        and (root / APP_LOCK).is_file()
        and (root / APP_PROJECT).is_file()
    )


def plan_desktop_gate(root: Path) -> DesktopGatePlan:
    root = Path(root)
    record = load_desktop_record(root)
    restore = record.get('windows_desktop_restore')
    if restore != 'admitted':
        return DesktopGatePlan(
            action='skip',
            reason='WinUI packages not admitted to lock.',
            commands=(),
        )
    if not admitted_lock_files_present(root):
        return DesktopGatePlan(
            action='fail',
            reason='admitted restore is missing Directory.Packages.props or App packages.lock.json.',
            commands=(),
        )
    app = str(root / APP_PROJECT)
    return DesktopGatePlan(
        action='run',
        reason='admitted App windows TFM locked restore and build.',
        commands=(
            ('dotnet', 'restore', app, '--locked-mode', '--force-evaluate'),
            (
                'dotnet', 'build', app,
                '--configuration', 'Release',
                '--framework', WINDOWS_TFM,
                '--no-restore',
            ),
        ),
    )


def execute_desktop_gate(
    plan: DesktopGatePlan,
    *,
    cwd: Path,
    runner=subprocess.run,
) -> int:
    if plan.action == 'skip':
        print('windows_desktop_restore skipped: ' + plan.reason)
        print('This skip is not full-application green and does not pass AC39/AC40/AC47.')
        return 0
    if plan.action != 'run':
        print(plan.reason, file=sys.stderr)
        return 1
    for command in plan.commands:
        print('+', ' '.join(command))
        completed = runner(list(command), cwd=str(cwd))
        code = completed.returncode if completed.returncode is not None else 1
        if code != 0:
            print('desktop gate command failed: ' + ' '.join(command), file=sys.stderr)
            return code
    return 0


def main(root: Path | None = None) -> int:
    root = Path(root) if root is not None else ROOT
    record = load_desktop_record(root)
    restore = record.get('windows_desktop_restore')
    required = record.get('github_required_check')
    print('windows_desktop_restore=' + str(restore))
    print('github_required_check=' + str(required))
    plan = plan_desktop_gate(root)
    return execute_desktop_gate(plan, cwd=root)


if __name__ == '__main__':
    raise SystemExit(main())
