from pathlib import Path
import json
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.project_graph import (
    ADMITTED_NPM_LOCK_REL,
    ProjectGraphError,
    check_dist_javascript,
    check_npm_lock,
    check_terminal_webview_host,
    scan_npm_sources,
)
import validate_repository as repository


def _write_npm(root: Path, *, extra_dep=None, unscoped=False, cdn=False, version='6.0.0'):
    terminal = root / 'web' / 'terminal'
    src = terminal / 'src'
    dist = terminal / 'dist'
    src.mkdir(parents=True)
    dist.mkdir(parents=True)
    deps = {'@xterm/xterm': version}
    if extra_dep:
        deps[extra_dep[0]] = extra_dep[1]
    if unscoped:
        deps['xterm'] = '5.3.0'
    package = {
        'name': 'herddesk-terminal',
        'private': True,
        'version': '0.0.0',
        'dependencies': deps,
    }
    (terminal / 'package.json').write_text(json.dumps(package), encoding='utf-8')
    packages = {
        '': {'name': 'herddesk-terminal', 'version': '0.0.0', 'dependencies': deps},
        'node_modules/@xterm/xterm': {
            'version': version,
            'resolved': 'https://registry.npmjs.org/@xterm/xterm/-/xterm-' + version + '.tgz',
            'integrity': 'sha512-test',
            'license': 'MIT',
        },
    }
    if extra_dep:
        packages['node_modules/' + extra_dep[0]] = {
            'version': extra_dep[1],
            'resolved': 'https://registry.npmjs.org/' + extra_dep[0] + '/-/' + extra_dep[0] + '-' + extra_dep[1] + '.tgz',
            'integrity': 'sha512-extra',
        }
    if unscoped:
        packages['node_modules/xterm'] = {
            'version': '5.3.0',
            'resolved': 'https://registry.npmjs.org/xterm/-/xterm-5.3.0.tgz',
            'integrity': 'sha512-deprecated',
        }
    (terminal / 'package-lock.json').write_text(
        json.dumps({'lockfileVersion': 3, 'packages': packages}),
        encoding='utf-8',
    )
    html = '<script src="https://cdn.jsdelivr.net/npm/xterm"></script>' if cdn else '<script type="module" src="./terminal.js"></script>'
    (src / 'index.html').write_text('<!doctype html>' + html, encoding='utf-8')
    (src / 'terminal.ts').write_text('export const ok = 1;\n', encoding='utf-8')
    (dist / 'index.html').write_text('<!doctype html><script type="module" src="./terminal.js"></script>', encoding='utf-8')
    (dist / 'terminal.js').write_text('export const ok = 1;\n', encoding='utf-8')
    (dist / 'protocol.js').write_text('export const SCHEMA_VERSION = 1;\n', encoding='utf-8')
    (dist / 'utf8.js').write_text('export function asWriteBytes(bytes) { return bytes; }\n', encoding='utf-8')
    (dist / 'ime.js').write_text('export function createCompositionGate() { return {}; }\n', encoding='utf-8')
    (dist / 'styles.css').write_text('#terminal{}\n', encoding='utf-8')
    (dist / 'xterm.mjs').write_text('export class Terminal {}\n', encoding='utf-8')
    (dist / 'xterm.css').write_text('.xterm{}\n', encoding='utf-8')
    register = {
        'units': [
            {
                'name': '@xterm/xterm',
                'category': 'npm_package',
                'admission': 'approved',
                'version': version,
                'hash': {'npm_integrity': 'sha512-test'},
                'enters_webview_bundle': True,
                'enters_package_lock': False,
                'lock_allowed': True,
            },
            {
                'name': 'herddesk-terminal-bundle',
                'category': 'renderer_asset',
                'admission': 'approved',
                'enters_webview_bundle': True,
                'enters_package_lock': False,
                'lock_allowed': False,
            },
        ]
    }
    docs = root / 'docs' / 'licensing'
    docs.mkdir(parents=True)
    (docs / 'register.json').write_text(json.dumps(register), encoding='utf-8')


class Hd014NpmLockTests(unittest.TestCase):
    def test_shipped_npm_lock_and_host_pass(self):
        check_npm_lock(ROOT)
        check_terminal_webview_host(ROOT)
        repo = repository.validate()
        self.assertEqual(repo['project_graph'], 'passed')
        hd014 = json.loads((ROOT / 'implementation' / 'hd-014-l2.json').read_text(encoding='utf-8'))
        self.assertTrue(hd014['npm_xterm_admitted'])
        self.assertTrue(hd014['webview2_admitted'])
        self.assertEqual(hd014['l2_webview_process'], 'UNVERIFIED')
        self.assertFalse(hd014['ac08_passed'])
        self.assertFalse(hd014['g0_passed'])
        lock = json.loads((ROOT / ADMITTED_NPM_LOCK_REL).read_text(encoding='utf-8'))
        admitted = lock['packages']['node_modules/@xterm/xterm']
        self.assertEqual(admitted['version'], '6.0.0')
        self.assertEqual(admitted['license'], 'MIT')

    def test_missing_lock_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ProjectGraphError) as ctx:
                check_npm_lock(Path(tmp))
            self.assertEqual(str(ctx.exception), 'npm_lock_missing')

    def test_extra_lock_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_npm(root)
            other = root / 'web' / 'other'
            other.mkdir()
            (other / 'package-lock.json').write_text('{}', encoding='utf-8')
            with self.assertRaises(ProjectGraphError) as ctx:
                check_npm_lock(root)
            self.assertEqual(str(ctx.exception), 'npm_lock_present')

    def test_unscoped_xterm_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_npm(root, unscoped=True)
            with self.assertRaises(ProjectGraphError) as ctx:
                check_npm_lock(root)
            self.assertEqual(str(ctx.exception), 'forbidden_npm_package')

    def test_extra_npm_package_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_npm(root, extra_dep=('lodash', '4.17.21'))
            with self.assertRaises(ProjectGraphError) as ctx:
                check_npm_lock(root)
            self.assertEqual(str(ctx.exception), 'forbidden_npm_package')

    def test_cdn_source_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_npm(root, cdn=True)
            with self.assertRaises(ProjectGraphError) as ctx:
                scan_npm_sources(root)
            self.assertEqual(str(ctx.exception), 'cdn_source_forbidden')

    def test_typescript_in_dist_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            dist = root / 'web' / 'terminal' / 'dist'
            dist.mkdir(parents=True)
            (dist / 'terminal.js').write_text('function f(): void {}\n', encoding='utf-8')
            (dist / 'protocol.js').write_text('export const x = 1;\n', encoding='utf-8')
            (dist / 'utf8.js').write_text('export const x = 1;\n', encoding='utf-8')
            (dist / 'ime.js').write_text('export const x = 1;\n', encoding='utf-8')
            with self.assertRaises(ProjectGraphError) as ctx:
                check_dist_javascript(root)
            self.assertEqual(str(ctx.exception), 'dist_typescript_forbidden')

    def test_dist_javascript_has_no_typescript(self):
        for name in ('terminal.js', 'protocol.js', 'utf8.js', 'ime.js'):
            text = (ROOT / 'web' / 'terminal' / 'dist' / name).read_text(encoding='utf-8')
            self.assertNotIn(' as {', text, name)
            self.assertNotIn(': void', text, name)
            self.assertNotIn('interface ', text, name)
            self.assertNotIn(': unknown', text, name)

    def test_observe_source_gates_binary_and_resize(self):
        text = (ROOT / 'web' / 'terminal' / 'src' / 'terminal.ts').read_text(encoding='utf-8')
        self.assertRegex(text, r'onData\([\s\S]*?readOnly')
        self.assertRegex(text, r'onBinary\([\s\S]*?readOnly')
        self.assertRegex(text, r'onResize\([\s\S]*?readOnly')
        self.assertRegex(text, r'suppressData')
        self.assertRegex(text, r'compositionstart')
