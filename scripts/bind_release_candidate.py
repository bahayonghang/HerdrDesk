#!/usr/bin/env python3
"""Bind observed hosted workflow SHA. Not a required-check or 1.0 claim."""
from __future__ import annotations
import json
from pathlib import Path
import sys

SCRIPTS = Path(__file__).resolve().parent
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))
from herddesk_g0.release import ReleaseError, bind_release_candidate

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    try:
        report = bind_release_candidate(ROOT)
    except ReleaseError as exc:
        print(json.dumps({'error': str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2) + '\n', end='')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
