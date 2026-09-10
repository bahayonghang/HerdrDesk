#!/usr/bin/env python3
"""Audit admitted working-tree release inputs. Not a live scan or AC pass."""
from __future__ import annotations
import json
from pathlib import Path
import sys

SCRIPTS = Path(__file__).resolve().parent
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))
from herddesk_g0.licensing import LicensingError, audit_admitted_release_inputs

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    try:
        report = audit_admitted_release_inputs(ROOT)
    except LicensingError as exc:
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2) + '\n', end='')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
