#!/usr/bin/env python3
"""Windows desktop restore gate. Skip is not full-application green."""
from pathlib import Path
import json
import sys

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    record = json.loads((ROOT / 'implementation/hd-007-packages.json').read_text(encoding='utf-8'))
    restore = record.get('windows_desktop_restore')
    required = record.get('github_required_check')
    print('windows_desktop_restore=' + str(restore))
    print('github_required_check=' + str(required))
    if restore != 'admitted':
        print('Windows desktop restore skipped: WinUI packages not admitted to lock.')
        print('This skip is not full-application green and does not pass AC39/AC40/AC47.')
        return 0
    print('Admitted desktop restore is not implemented in this revision.', file=sys.stderr)
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
