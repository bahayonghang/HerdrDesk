#!/usr/bin/env python3
"""Validate a local NDJSON capture without connecting to or controlling herdr."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import sys
from herddesk_g0.protocol import analyze_capture, ProtocolError


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    parser.add_argument('--output', type=Path, help='Create a new report; never overwrite')
    args = parser.parse_args(argv)
    try:
        with args.capture.open('rb') as stream:
            report = analyze_capture(iter(lambda: stream.read(64 * 1024), b''))
    except (OSError, ValueError) as exc:
        report = {'kind': 'offline_terminal_capture_validation', 'validated': False,
                  'error': str(exc) if isinstance(exc, ProtocolError) else type(exc).__name__}
    encoded = json.dumps(report, ensure_ascii=False, indent=2) + '\n'
    if args.output:
        try:
            # Exclusive creation prevents overwriting captures or existing evidence.
            with args.output.open('x', encoding='utf-8') as destination:
                destination.write(encoded)
        except OSError as exc:
            print(json.dumps({'validated': False, 'error': type(exc).__name__}), file=sys.stderr)
            return 2
    else:
        print(encoded, end='')
    return 0 if report['validated'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
