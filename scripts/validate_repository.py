#!/usr/bin/env python3
"""Structural validation only; this does not compile C# or pass any live gate."""
from pathlib import Path
import json
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
SCRIPTS=Path(__file__).resolve().parent
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0,str(SCRIPTS))
from herddesk_g0.adr import validate_adr_baseline
from herddesk_g0.evidence import validate_evidence
from herddesk_g0.endpoint import validate_endpoint_matrix
from herddesk_g0.lease import validate_terminal_lease_matrix
from herddesk_g0.licensing import audit_admitted_release_inputs, validate_licensing
from herddesk_g0.quality import (
    DPI_MATRIX_REL,
    LIVE_WORKING_SET_REL,
    SOAK_ELAPSED_REL,
    SOAK_WORKING_SET_REL,
    collect_dpi_overlay,
    collect_narrator_overlay,
    collect_theme_overlay,
    soak_interrupt_capture_rels,
    soak_start_app_pid,
    validate_dpi_matrix,
    validate_eight_hour_soak_elapsed,
    validate_eight_hour_soak_interruption,
    validate_eight_hour_soak_start,
    validate_narrator_product_ui_launch,
    validate_soak_working_set,
)
from herddesk_g0.release import bind_release_candidate
from herddesk_g0.project_graph import ADMITTED_LOCK_REL, ADMITTED_NPM_LOCK_REL, validate_project_graph
import run_windows_desktop_gate as desktop_gate
from herddesk_g0.renderer import validate_renderer_matrix
from record_lab_msix import (
    LAB_MSIX_KIND,
    LAB_MSIX_REL,
    validate_lab_msix,
)

_HD026_PASS_KEYS = (
    'ac13_passed', 'ac14_passed', 'ac15_passed', 'ac19_passed',
    'ac21_passed', 'ac22_passed', 'ac23_passed', 'ac24_passed',
    'ac26_passed', 'ac27_passed', 'g0_passed',
)
_HD026_REQUIRED_ACS = frozenset({
    'AC13', 'AC14', 'AC15', 'AC19', 'AC21', 'AC22', 'AC23', 'AC24', 'AC26',
})
_HD026_CARD_IDS = (
    'ac21-identity-collision', 'ac22-ac23-ssh-identity-config',
    'ac24-transparent-stream', 'ac13-recovery', 'ac14-no-replay',
    'ac15-ownership', 'ac26-isolation-retry', 'ac19-search',
    'budget-platform',
)
_HD026_CARD_ACS = {
    'ac21-identity-collision': ('AC21',),
    'ac22-ac23-ssh-identity-config': ('AC22', 'AC23'),
    'ac24-transparent-stream': ('AC24',),
    'ac13-recovery': ('AC13',),
    'ac14-no-replay': ('AC14',),
    'ac15-ownership': ('AC15',),
    'ac26-isolation-retry': ('AC26',),
    'ac19-search': ('AC19',),
    'budget-platform': ('AC27',),
}
_HD026_CARD_OWNERS = {
    'ac21-identity-collision': ['HD-023'],
    'ac22-ac23-ssh-identity-config': ['HD-020', 'HD-024'],
    'ac24-transparent-stream': ['HD-022'],
    'ac13-recovery': ['HD-018', 'HD-019', 'HD-022', 'HD-024'],
    'ac14-no-replay': ['HD-016', 'HD-018', 'HD-022'],
    'ac15-ownership': ['HD-013', 'HD-018', 'HD-019', 'HD-022'],
    'ac26-isolation-retry': ['HD-024'],
    'ac19-search': ['HD-011', 'HD-023'],
    'budget-platform': ['HD-025', 'HD-026'],
}
_HD026_LIVE_IDS = ('live-ssh', 'live-winui', 'live-three-device', 'live-crash')
_HD026_LIVE_GRANTS = {
    'live-ssh': 'no_authorized_isolated_windows_openssh_lab',
    'live-winui': 'no_winui_admission',
    'live-three-device': 'no_authorized_third_device_id',
    'live-crash': 'no_authorized_supervised_gui_crash',
}
_HD026_SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
_HD026_SUPPORT_PASS = frozenset({'supported', 'stable', 'passed', 'compatible', 'success'})
_HD026_TEMPLATES = (
    'evidence/multi-device-mvp/live-ssh.template.json',
    'evidence/multi-device-mvp/live-winui.template.json',
    'evidence/multi-device-mvp/live-three-device.template.json',
    'evidence/multi-device-mvp/live-crash.template.json',
)
_HD026_NOT_RUN = (
    'evidence/multi-device-mvp/live-ssh.not-run.json',
    'evidence/multi-device-mvp/live-winui.not-run.json',
    'evidence/multi-device-mvp/live-three-device.not-run.json',
    'evidence/multi-device-mvp/live-crash.not-run.json',
)


def _hd026_token(value):
    return value.strip().lower() if isinstance(value, str) else value


def _is_hd026_success(value) -> bool:
    if value is True:
        return True
    return _hd026_token(value) in _HD026_SUCCESS


def _reject_hd026_pass_claims(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key.endswith('_passed') and value is not False:
                raise AssertionError(f'{key} must stay false')
            if key == 'phase_gate' and _hd026_token(value) in {'passed', 'pass', 'ok'}:
                raise AssertionError('phase_gate must stay not_passed')
            if key == 'l2_live_ssh' and value != 'UNVERIFIED':
                raise AssertionError('l2_live_ssh must stay UNVERIFIED')
            if key in {'live_result', 'result', 'live_status', 'status'} and _is_hd026_success(value):
                raise AssertionError(f'{key} must stay not_run or UNVERIFIED')
            if key == 'support' and _hd026_token(value) in _HD026_SUPPORT_PASS:
                raise AssertionError('support must not be a pass token')
            if key == 'compatible' and value is True:
                raise AssertionError('compatible must stay false')
            if key in {
                'live_ssh', 'live_winui', 'live_three_device', 'live_crash',
                'winui_admitted', 'herdr_executed',
            } and value is True:
                raise AssertionError(f'{key} must stay false')
            _reject_hd026_pass_claims(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd026_pass_claims(item)


def _check_hd026_closeout(hd026: dict, mvp: dict, matrix: dict) -> None:
    assert hd026.get('document_kind') == 'hd026_l2_status'
    assert mvp.get('document_kind') == 'hd026_multi_device_mvp_catalog'
    assert matrix.get('document_kind') == 'hd026_support_matrix'
    assert hd026.get('l2_live_ssh') == 'UNVERIFIED'
    assert mvp.get('l2_live_ssh') == 'UNVERIFIED'
    for doc in (hd026, mvp, matrix):
        _reject_hd026_pass_claims(doc)
        for key in _HD026_PASS_KEYS:
            assert doc.get(key) is False, key
        assert doc.get('phase_gate') != 'passed'
        if 'winui_admitted' in doc:
            assert doc.get('winui_admitted') is False
        if 'live_ssh' in doc:
            assert doc.get('live_ssh') is False
    for key in ('live_winui', 'live_three_device', 'live_crash',
                'integration_ssh_project', 'integration_windows_project',
                'third_device_id_authorized', 'b_ssh_measured',
                'copy_windows_fields_onto_linux', 'extrapolate_macos_arm64'):
        assert hd026.get(key) is False
        if key in mvp:
            assert mvp.get(key) is False
    assert hd026.get('two_sessions_on_one_host_are_not_three_devices') is True
    assert mvp.get('two_sessions_on_one_host_are_not_three_devices') is True
    assert matrix.get('two_sessions_on_one_host_are_not_three_devices') is True
    missing = hd026.get('missing') or {}
    for key in ('isolated_windows_openssh', 'isolated_linux_herdr',
                'authorized_third_device', 'live_ssh_matrix', 'winui_shell',
                'supervised_gui_crash', 'network_partition', 'live_search_p95',
                'measured_b_ssh'):
        assert missing.get(key) is True
    assert hd026.get('catalog') == 'evidence/multi-device-mvp/catalog.json'
    assert hd026.get('support_matrix') == 'evidence/multi-device-mvp/support-matrix.json'
    assert mvp.get('support_matrix') == 'evidence/multi-device-mvp/support-matrix.json'
    assert mvp.get('l2_status') == 'implementation/hd-026-l2.json'
    assert hd026.get('herdr_executed') is False
    assert mvp.get('herdr_executed') is False
    cards = {item['id']: item for item in mvp['execution_cards']}
    assert tuple(cards) == _HD026_CARD_IDS
    seen_acs = set()
    for card in mvp['execution_cards']:
        card_id = card['id']
        assert card['live_status'] == 'UNVERIFIED'
        assert card['live_result'] == 'not_run'
        assert card['l1_status'] == 'shipped'
        assert card['l1_status'] != 'passed'
        assert card['missing_grant']
        assert card['owner_children'] == _HD026_CARD_OWNERS[card_id]
        assert tuple(card['ac_ids']) == _HD026_CARD_ACS[card_id]
        seen_acs.update(card['ac_ids'])
        for rel in card['l1_artifacts']:
            assert (ROOT / rel).is_file(), rel
        capture = ROOT / card['live_capture']
        assert capture.is_file()
        loaded = json.loads(capture.read_text(encoding='utf-8'))
        _reject_hd026_pass_claims(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        assert not _is_hd026_success(loaded.get('result'))
    assert _HD026_REQUIRED_ACS <= seen_acs
    rows = {item['id']: item for item in mvp['live_rows']}
    assert tuple(rows) == _HD026_LIVE_IDS
    for row in mvp['live_rows']:
        assert row['status'] == 'UNVERIFIED'
        assert row['result'] == 'not_run'
        assert row.get('template') is False
        assert row.get('owner_children')
        assert row['missing_grant'] == _HD026_LIVE_GRANTS[row['id']]
        evidence = ROOT / row['evidence_path']
        assert evidence.is_file()
        loaded = json.loads(evidence.read_text(encoding='utf-8'))
        _reject_hd026_pass_claims(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
    for rel in _HD026_TEMPLATES:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd026_pass_claims(doc)
        assert doc.get('template') is True
        assert doc.get('document_kind') == 'template'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('captured_at_utc') is None
        assert not _is_hd026_success(doc.get('result'))
        assert doc.get('herdr_executed') is False
        assert 'stdout' not in doc and 'stderr' not in doc
    for rel in _HD026_NOT_RUN:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd026_pass_claims(doc)
        assert doc.get('template') is False
        assert doc.get('result') == 'not_run'
        assert doc.get('herdr_executed') is False
        assert doc.get('evidence_level') == 'not_run'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert 'stdout' not in doc and 'stderr' not in doc
        assert 'password' not in json.dumps(doc).lower()
    promised = {item['id'] for item in matrix['promised_range']}
    assert promised == {'windows-11-x64-client', 'linux-x64-remote'}
    by_platform = {item['id']: item for item in matrix['platforms']}
    for key in ('windows-11-x64-client', 'linux-x64-remote'):
        row = by_platform[key]
        assert row['promise'] == 'promised'
        assert row['live_status'] == 'not_run'
        assert row['live_result'] == 'not_run'
        assert row['compatible'] is False
        assert row['support'] == 'promised_not_run'
    macos = by_platform['macos-x64']
    assert macos['support'] == 'unsupported'
    assert macos['live_status'] == 'not_run'
    assert macos['compatible'] is False
    for key in ('macos-arm64', 'linux-arm64', 'windows-arm64'):
        row = by_platform[key]
        assert row['support'] in ('unsupported', 'experimental')
        assert row['support'] not in ('supported', 'stable', 'promised')
        assert row['live_status'] == 'not_run'
        assert row['compatible'] is False
    assert by_platform['windows-11-x64-client']['missing_grant'] != \
        by_platform['linux-x64-remote']['missing_grant']
    auth_ids = [item['id'] for item in matrix['auth_combos']]
    assert 'alias' in auth_ids and 'proxyjump' in auth_ids
    assert 'password_argv_stdin' in auth_ids and 'mfa_browser' in auth_ids
    for combo in matrix['auth_combos']:
        assert combo['live_status'] == 'not_run'
        assert combo['live_result'] == 'not_run'
        if combo['id'] in {
            'password_argv_stdin', 'keyboard_interactive', 'hidden_passphrase',
            'mfa_browser', 'tailscale_ssh', 'pkcs11_security_key',
        }:
            assert combo['support'] == 'unsupported'
    cell_keys = {(item['platform'], item['auth']) for item in matrix['cells']}
    for platform in ('windows-11-x64-client', 'linux-x64-remote'):
        for auth in auth_ids:
            assert (platform, auth) in cell_keys
    for cell in matrix['cells']:
        assert cell['live_status'] == 'not_run'
        assert cell['live_result'] == 'not_run'
        assert cell['compatible'] is False
        assert not _is_hd026_success(cell['live_result'])
    assert matrix.get('copy_windows_fields_onto_linux') is False
    assert matrix.get('extrapolate_macos_arm64') is False
    assert matrix.get('compatible_by_default') == []
    assert mvp.get('third_device_id_authorized') is False
    search = cards['ac19-search']
    assert search['live_result'] == 'not_run'
    assert search['missing_grant'] == 'no_authorized_third_device_id'
    assert rows['live-three-device']['missing_grant'] == 'no_authorized_third_device_id'


_HD027_PASS_KEYS = ('ac30_passed', 'ac34_passed', 'g0_passed')
_HD027_VECTORS = (
    'valid-list.hdfb', 'valid-empty-write.hdfb', 'invalid-oversize-json.hdfb',
    'invalid-sequence-gap.hdfb', 'invalid-unknown-kind.hdfb',
)


def _check_hd027(hd027: dict, packages: dict) -> None:
    assert hd027.get('document_kind') == 'hd027_l2_status'
    assert packages.get('document_kind') == 'hd027_package_probe'
    for key in ('l2_filesystem', 'l2_ssh', 'l2_toctou'):
        assert hd027.get(key) == 'UNVERIFIED'
    for key in _HD027_PASS_KEYS:
        assert hd027.get(key) is False, key
        assert packages.get(key) is not True, key
    assert hd027.get('phase_gate') != 'passed'
    assert packages.get('phase_gate') != 'passed'
    for key in ('live_file_ops', 'live_ssh', 'helper_install', 'integration_ssh_project',
                'integration_windows_project', 'winui_admitted'):
        assert hd027.get(key) is False, key
    assert hd027.get('adr_status') == 'accepted'
    missing = hd027.get('missing') or {}
    assert missing.get('live_ssh') is True
    assert missing.get('toctou_lab') is True
    assert missing.get('published_binary') is True
    assert missing.get('hd028_owner_review') is False
    assert packages.get('crates') == []
    assert (ROOT / 'filebridge' / 'src' / 'lib.rs').is_file()
    assert (ROOT / 'filebridge' / 'src' / 'main.rs').is_file()
    cargo = (ROOT / 'filebridge' / 'Cargo.toml').read_text(encoding='utf-8')
    assert '[[bin]]' in cargo
    adr = (ROOT / 'docs' / 'adr' / '0008-filebridge-protocol-v1.md').read_text(encoding='utf-8')
    assert '**accepted**' in adr
    assert 'wire only' in adr.lower()
    vectors = ROOT / 'filebridge' / 'spec' / 'test-vectors'
    manifest = json.loads((vectors / 'manifest.json').read_text(encoding='utf-8'))
    names = {item['file'] for item in manifest['vectors']}
    for name in _HD027_VECTORS:
        assert name in names, name
        assert (vectors / name).is_file()
    assert (ROOT / 'src' / 'HerdDesk.Infrastructure' / 'Files' / 'FileBridgeProtocolCodec.cs').is_file()
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    check_integration_windows_layout(ROOT)


_HD028_PASS_KEYS = ('ac30_passed', 'ac31_passed', 'ac32_passed', 'g0_passed')
_HD029_PASS_KEYS = ('ac31_passed', 'ac33_passed', 'g0_passed')
_HD030_PASS_KEYS = ('ac35_passed', 'ac31_passed', 'ac32_passed', 'ac36_passed', 'g0_passed')
_HD031_PASS_KEYS = ('ac36_passed', 'g0_passed')
_HD031_WATCHER_APIS = (
    'AddClipboardFormatListener', 'SetClipboardViewer', 'WM_CLIPBOARDUPDATE',
)
_HD031_CACHE_GLOB = (
    'Directory.GetFiles', 'EnumerateFiles', 'EnumerateFileSystemEntries',
    'GetFileSystemEntries',
)
_HD031_LIVE_TEST_APIS = ('OpenClipboard', 'GetClipboardData', 'CreateWindows(', 'ReadOnce(')


def _check_hd028(hd028: dict, packages: dict) -> None:
    assert hd028.get('document_kind') == 'hd028_l2_status'
    assert packages.get('document_kind') == 'hd028_package_probe'
    for key in ('l2_filesystem', 'l2_ssh', 'l2_toctou'):
        assert hd028.get(key) == 'UNVERIFIED'
        assert packages.get(key) in (None, 'UNVERIFIED')
    for key in _HD028_PASS_KEYS:
        assert hd028.get(key) is False, key
        assert packages.get(key) is not True, key
    assert hd028.get('phase_gate') != 'passed'
    assert packages.get('phase_gate') != 'passed'
    for key in ('live_file_ops', 'live_ssh', 'helper_install', 'integration_ssh_project',
                'integration_windows_project', 'winui_admitted'):
        assert hd028.get(key) is False, key
    crates = packages.get('crates') or []
    sha2 = next((item for item in crates if item.get('id') == 'sha2'), None)
    assert sha2 is not None
    assert sha2.get('requested') == '0.10.8'
    assert sha2.get('lock_version') == '0.10.8'
    assert packages.get('rust', {}).get('channel') == '1.98.0'
    assert (ROOT / 'filebridge' / 'src' / 'main.rs').is_file()
    cargo = (ROOT / 'filebridge' / 'Cargo.toml').read_text(encoding='utf-8')
    assert '[[bin]]' in cargo
    assert 'herddesk-filebridge' in cargo
    assert (ROOT / 'src' / 'HerdDesk.Core' / 'Files' / 'TransferCoordinator.cs').is_file()
    assert (ROOT / 'src' / 'HerdDesk.Infrastructure' / 'Files' / 'LocalFileEndpoint.cs').is_file()
    assert (ROOT / 'src' / 'HerdDesk.Infrastructure' / 'Files' / 'FileBridgeClient.cs').is_file()
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    check_integration_windows_layout(ROOT)
    lock = (ROOT / 'filebridge' / 'Cargo.lock').read_text(encoding='utf-8')
    from herddesk_g0.project_graph import cargo_lock_package_version
    assert cargo_lock_package_version(lock, 'sha2') == '0.10.8'


_APP_XAML_SKIP = frozenset({'bin', 'obj'})
_APP_SHELL_XAML = (
    'App.xaml',
    'Controls/ControlBar.xaml',
    'Controls/DeviceSessionRail.xaml',
    'Controls/SearchPalette.xaml',
    'Controls/TerminalHost.xaml',
    'Controls/WorkspacePaneTree.xaml',
    'MainWindow.xaml',
    'Views/AboutPage.xaml',
    'Views/DiagnosticsPage.xaml',
    'Views/SettingsPage.xaml',
    'Views/ShellPage.xaml',
)
_APP_BLANK_XAML = _APP_SHELL_XAML


_JUST_RECIPE = re.compile(
    r'(?m)^(?P<name>[a-zA-Z_][a-zA-Z0-9_-]*)(?:[ \t]+[^\n:=]+)?:'
    r'(?P<rest>[^\n]*)\n(?P<body>(?:[ \t].*\n|\n)*)'
)


def just_recipe_texts(just: str) -> dict[str, str]:
    found: dict[str, str] = {}
    for match in _JUST_RECIPE.finditer(just):
        header = match.group(0).split('\n', 1)[0]
        if ':=' in header:
            continue
        name = match.group('name')
        text = match.group('rest') + '\n' + match.group('body')
        found[name] = found.get(name, '') + text
    return found


def assert_justfile_ui_is_opt_in_dev(just: str) -> None:
    """just ci must not launch WinUI. Opt-in `dev` may pass --ui."""
    recipes = just_recipe_texts(just)
    assert 'ci' in recipes
    assert 'dev' not in recipes['ci'].split()
    assert '--ui' not in recipes['ci']
    assert 'dev' in recipes
    assert '--ui' in recipes['dev']
    for name, text in recipes.items():
        if name == 'dev':
            continue
        assert '--ui' not in text, name


def check_integration_windows_layout(root: Path | None = None) -> None:
    """Allow the HD-011 console runner. Catalog token integration_windows_project stays false."""
    base = Path(root) if root is not None else ROOT
    project = base / 'tests' / 'Integration.Windows' / 'HerdDesk.Integration.Windows.csproj'
    assert project.is_file()
    text = project.read_text(encoding='utf-8')
    assert '<PackageReference' not in text
    assert 'Microsoft.NET.Test.Sdk' not in text
    assert 'Microsoft.WindowsAppSDK' not in text
    assert '2.4.0' not in text
    assert 'net10.0' in text
    assert 'HerdDeskBclOnly' in text
    program = base / 'tests' / 'Integration.Windows' / 'Program.cs'
    assert program.is_file()
    src = program.read_text(encoding='utf-8')
    assert 'Application.Start' not in src
    assert 'new MainWindow' not in src
    ci = (base / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
    assert '--ui' not in ci
    assert 'Application.Start' not in ci
    just = (base / 'justfile').read_text(encoding='utf-8')
    assert_justfile_ui_is_opt_in_dev(just)


def app_source_xaml(root: Path | None = None) -> list[str]:
    base = Path(root) if root is not None else ROOT
    app = base / 'src' / 'HerdDesk.App'
    found: list[str] = []
    for path in app.rglob('*.xaml'):
        if any(part in _APP_XAML_SKIP for part in path.parts):
            continue
        found.append(path.relative_to(app).as_posix())
    return sorted(found)


def _check_hd029(hd029: dict) -> None:
    assert hd029.get('document_kind') == 'hd029_l2_status'
    assert hd029.get('l2_live_ui') == 'UNVERIFIED'
    assert hd029.get('l2_live_ssh') == 'UNVERIFIED'
    for key in _HD029_PASS_KEYS:
        assert hd029.get(key) is False, key
    assert hd029.get('phase_gate') != 'passed'
    for key in ('live_ssh', 'live_ui', 'winui_admitted', 'integration_windows',
                'integration_windows_project'):
        assert hd029.get(key) is False, key
    missing = hd029.get('missing') or {}
    assert missing.get('winui_xaml') is True
    assert missing.get('live_ssh') is True
    assert missing.get('live_ui') is True
    assert missing.get('integration_windows') is True
    files = ROOT / 'src' / 'HerdDesk.App' / 'Files'
    assert (files / 'FileWorkspaceViewModel.cs').is_file()
    assert (files / 'FilePaneViewModel.cs').is_file()
    assert (files / 'TransferQueueViewModel.cs').is_file()
    assert (files / 'ConflictDialogViewModel.cs').is_file()
    assert list(files.glob('*.xaml')) == []
    assert tuple(app_source_xaml(ROOT)) == _APP_BLANK_XAML
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    check_integration_windows_layout(ROOT)


def _check_hd030(hd030: dict) -> None:
    assert hd030.get('document_kind') == 'hd030_l2_status'
    assert hd030.get('l2_live_agent') == 'UNVERIFIED'
    assert hd030.get('l2_live_ime') == 'UNVERIFIED'
    assert hd030.get('l2_live_ssh') == 'UNVERIFIED'
    for key in _HD030_PASS_KEYS:
        assert hd030.get(key) is False, key
    assert hd030.get('phase_gate') != 'passed'
    for key in ('live_ssh', 'live_agent', 'live_ime', 'winui_admitted', 'integration_windows',
                'integration_windows_project', 'auto_submit'):
        assert hd030.get(key) is False, key
    missing = hd030.get('missing') or {}
    assert missing.get('winui_xaml') is True
    assert missing.get('live_agent') is True
    assert missing.get('live_ime') is True
    assert missing.get('live_ssh') is True
    assert missing.get('integration_windows') is True
    core = ROOT / 'src' / 'HerdDesk.Core' / 'Attachments'
    assert (core / 'AttachmentCoordinator.cs').is_file()
    assert (core / 'AttachmentCapabilityCatalog.cs').is_file()
    assert (ROOT / 'src' / 'HerdDesk.App' / 'ViewModels' / 'AttachToAgentViewModel.cs').is_file()
    assert (ROOT / 'src' / 'HerdDesk.Contracts' / 'AttachmentPorts.cs').is_file()
    assert tuple(app_source_xaml(ROOT)) == _APP_BLANK_XAML
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    check_integration_windows_layout(ROOT)


def _check_hd031(hd031: dict) -> None:
    assert hd031.get('document_kind') == 'hd031_l2_status'
    assert hd031.get('l2_live_clipboard') == 'UNVERIFIED'
    assert hd031.get('l2_live_ime') == 'UNVERIFIED'
    for key in _HD031_PASS_KEYS:
        assert hd031.get(key) is False, key
    assert hd031.get('phase_gate') != 'passed'
    for key in ('live_clipboard', 'live_ime', 'winui_admitted', 'integration_windows',
                'integration_windows_project', 'clipboard_watcher'):
        assert hd031.get(key) is False, key
    assert hd031.get('osc52_read_default') == 'deny'
    assert hd031.get('osc52_write_default') == 'deny'
    missing = hd031.get('missing') or {}
    assert missing.get('winui_xaml') is True
    assert missing.get('live_clipboard') is True
    assert missing.get('live_ime') is True
    assert missing.get('integration_windows') is True
    assert missing.get('clipboard_watcher') is True
    core = ROOT / 'src' / 'HerdDesk.Core' / 'Clipboard'
    assert (core / 'ClipboardIntentResolver.cs').is_file()
    assert (core / 'PasteCoordinator.cs').is_file()
    infra = ROOT / 'src' / 'HerdDesk.Infrastructure' / 'Clipboard'
    assert (infra / 'AttachmentCache.cs').is_file()
    assert (infra / 'WindowsClipboardSnapshotReader.cs').is_file()
    osc = ROOT / 'src' / 'HerdDesk.Terminal.Web' / 'Input' / 'OscClipboardPolicy.cs'
    assert osc.is_file()
    assert (ROOT / 'src' / 'HerdDesk.App' / 'ViewModels' / 'PastePreviewViewModel.cs').is_file()
    assert (ROOT / 'src' / 'HerdDesk.Contracts' / 'ClipboardPorts.cs').is_file()
    assert tuple(app_source_xaml(ROOT)) == _APP_BLANK_XAML
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    check_integration_windows_layout(ROOT)
    _check_hd031_sources(core, infra, osc)


def _check_hd031_sources(core: Path, infra: Path, osc: Path) -> None:
    src_files = [
        core / 'ClipboardIntentResolver.cs',
        core / 'PasteCoordinator.cs',
        infra / 'AttachmentCache.cs',
        infra / 'WindowsClipboardSnapshotReader.cs',
        osc,
        ROOT / 'src' / 'HerdDesk.App' / 'ViewModels' / 'PastePreviewViewModel.cs',
        ROOT / 'src' / 'HerdDesk.Contracts' / 'ClipboardPorts.cs',
    ]
    for path in src_files:
        text = path.read_text(encoding='utf-8')
        for token in _HD031_WATCHER_APIS:
            assert token not in text, path.name
    cache = (infra / 'AttachmentCache.cs').read_text(encoding='utf-8')
    for token in _HD031_CACHE_GLOB:
        assert token not in cache
    osc_text = osc.read_text(encoding='utf-8')
    for token in ('OpenClipboard', 'GetClipboardData', 'user32.dll'):
        assert token not in osc_text
    for path in (ROOT / 'tests').rglob('*.cs'):
        posix = path.as_posix()
        if 'Clipboard' not in posix and path.name != 'OscClipboardPolicyTests.cs':
            continue
        text = path.read_text(encoding='utf-8')
        for token in _HD031_LIVE_TEST_APIS:
            assert token not in text, path.name


_HD032_PASS_KEYS = (
    'ac31_passed', 'ac32_passed', 'ac33_passed', 'ac34_passed',
    'ac35_passed', 'g0_passed',
)
_HD032_REQUIRED_ACS = frozenset({'AC31', 'AC32', 'AC33', 'AC34', 'AC35'})
_HD032_CARD_IDS = (
    'payload-integrity', 'permission-enospc',
    'ssh-link-interrupt', 'ssh-kill', 'helper-kill',
    'symlink-junction-reparse-swap', 'keepboth-32-way',
    'fail-replace-conflict', 'dual-pane-ui-switch', 'attach-no-enter',
)
_HD032_INTERRUPT_KIND_IDS = (
    'ssh-link-interrupt', 'ssh-kill', 'helper-kill',
)
_HD032_CARD_ACS = {
    'payload-integrity': ('AC31',),
    'permission-enospc': ('AC32',),
    'ssh-link-interrupt': ('AC32',),
    'ssh-kill': ('AC32',),
    'helper-kill': ('AC32',),
    'symlink-junction-reparse-swap': ('AC34',),
    'keepboth-32-way': ('AC33',),
    'fail-replace-conflict': ('AC33',),
    'dual-pane-ui-switch': ('AC31', 'AC33'),
    'attach-no-enter': ('AC35',),
}
_HD032_CARD_OWNERS = {
    'payload-integrity': ['HD-028', 'HD-029'],
    'permission-enospc': ['HD-028'],
    'ssh-link-interrupt': ['HD-028'],
    'ssh-kill': ['HD-028'],
    'helper-kill': ['HD-028'],
    'symlink-junction-reparse-swap': ['HD-027', 'HD-028'],
    'keepboth-32-way': ['HD-028', 'HD-029'],
    'fail-replace-conflict': ['HD-028', 'HD-029'],
    'dual-pane-ui-switch': ['HD-029'],
    'attach-no-enter': ['HD-030', 'HD-031'],
}
_HD032_CARD_GRANTS = {
    'payload-integrity': 'no_authorized_disposable_fs_payload_lab',
    'permission-enospc': 'no_authorized_acl_quota_volume',
    'ssh-link-interrupt': 'no_authorized_ssh_link_interrupt',
    'ssh-kill': 'no_authorized_owned_ssh_pid_kill',
    'helper-kill': 'no_authorized_owned_filebridge_pid_kill',
    'symlink-junction-reparse-swap': 'no_authorized_second_process_symlink_attack',
    'keepboth-32-way': 'no_authorized_keepboth_race_lab',
    'fail-replace-conflict': 'no_authorized_replace_target_changed_lab',
    'dual-pane-ui-switch': 'no_winui_admission',
    'attach-no-enter': 'no_authorized_live_agent_path_insert',
}
_HD032_LIVE_IDS = (
    'live-fs', 'live-ssh', 'live-toctou', 'live-attack', 'live-ui',
)
_HD032_LIVE_GRANTS = {
    'live-fs': 'no_authorized_disposable_fs_payload_lab',
    'live-ssh': 'no_authorized_ssh_interrupt_or_owned_pid_kill',
    'live-toctou': 'no_authorized_second_process_symlink_attack',
    'live-attack': 'no_authorized_keepboth_replace_race_lab',
    'live-ui': 'no_winui_admission',
}
_HD032_L2_KEYS = (
    'l2_live_fs', 'l2_live_ssh', 'l2_live_toctou', 'l2_live_attack',
    'l2_live_ui',
)
_HD032_FALSE_KEYS = (
    'live_fs', 'live_ssh', 'live_toctou', 'live_attack', 'live_ui',
    'winui_admitted', 'herdr_executed', 'silent_overwrite_tested',
    'killed_user_daemon', 'glob_delete', 'auto_submit',
    'copy_windows_fields_onto_linux', 'extrapolate_macos_arm64',
    'integration_ssh_project', 'integration_windows_project',
)
_HD032_SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
_HD032_SUPPORT_PASS = frozenset({'supported', 'stable', 'passed', 'compatible', 'success'})
_HD032_TEMPLATES = (
    'evidence/files/live-fs.template.json',
    'evidence/files/live-ssh.template.json',
    'evidence/files/live-toctou.template.json',
    'evidence/files/live-attack.template.json',
    'evidence/files/live-ui.template.json',
)
_HD032_NOT_RUN = (
    'evidence/files/live-fs.not-run.json',
    'evidence/files/live-ssh.not-run.json',
    'evidence/files/live-toctou.not-run.json',
    'evidence/files/live-attack.not-run.json',
    'evidence/files/live-ui.not-run.json',
)
_HD032_SCENARIOS = (
    'payload', 'permission_enospc', 'ssh_link_interrupt', 'ssh_kill',
    'helper_kill', 'toctou', 'keepboth', 'fail_replace', 'dual_pane',
    'attach_no_enter',
)


def _hd032_token(value):
    return value.strip().lower() if isinstance(value, str) else value


def _is_hd032_success(value) -> bool:
    if value is True:
        return True
    return _hd032_token(value) in _HD032_SUCCESS


def _reject_hd032_pass_claims(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key.endswith('_passed') and value is not False:
                raise AssertionError(f'{key} must stay false')
            if key == 'phase_gate' and _hd032_token(value) in {'passed', 'pass', 'ok'}:
                raise AssertionError('phase_gate must stay not_passed')
            if key in _HD032_L2_KEYS and value != 'UNVERIFIED':
                raise AssertionError(f'{key} must stay UNVERIFIED')
            if key in {'live_result', 'result', 'live_status', 'status'} and _is_hd032_success(value):
                raise AssertionError(f'{key} must stay not_run or UNVERIFIED')
            if key == 'support' and _hd032_token(value) in _HD032_SUPPORT_PASS:
                raise AssertionError('support must not be a pass token')
            if key == 'compatible' and value is True:
                raise AssertionError('compatible must stay false')
            if key in _HD032_FALSE_KEYS and value is True:
                raise AssertionError(f'{key} must stay false')
            if key == 'l1_status' and _hd032_token(value) in _HD032_SUCCESS:
                raise AssertionError('l1_status must not be a pass token')
            _reject_hd032_pass_claims(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd032_pass_claims(item)


def _check_hd032_closeout(hd032: dict, catalog: dict, matrix: dict) -> None:
    assert hd032.get('document_kind') == 'hd032_l2_status'
    assert catalog.get('document_kind') == 'hd032_file_fault_security_catalog'
    assert matrix.get('document_kind') == 'hd032_support_matrix'
    for key in _HD032_L2_KEYS:
        assert hd032.get(key) == 'UNVERIFIED', key
        if key in catalog:
            assert catalog.get(key) == 'UNVERIFIED', key
    for doc in (hd032, catalog, matrix):
        _reject_hd032_pass_claims(doc)
        for key in _HD032_PASS_KEYS:
            assert doc.get(key) is False, key
        assert doc.get('phase_gate') != 'passed'
        if 'winui_admitted' in doc:
            assert doc.get('winui_admitted') is False
        if 'herdr_executed' in doc:
            assert doc.get('herdr_executed') is False
        if 'silent_overwrite_tested' in doc:
            assert doc.get('silent_overwrite_tested') is False
        if 'copy_windows_fields_onto_linux' in doc:
            assert doc.get('copy_windows_fields_onto_linux') is False
    for key in (
        'live_fs', 'live_ssh', 'live_toctou', 'live_attack', 'live_ui',
        'winui_admitted', 'integration_ssh_project', 'integration_windows_project',
        'copy_windows_fields_onto_linux', 'extrapolate_macos_arm64',
        'silent_overwrite_tested', 'killed_user_daemon', 'glob_delete',
        'auto_submit',
    ):
        assert hd032.get(key) is False, key
        if key in catalog:
            assert catalog.get(key) is False, key
    assert hd032.get('fake_fs_cannot_pass_toctou') is True
    assert catalog.get('fake_fs_cannot_pass_toctou') is True
    assert matrix.get('fake_fs_cannot_pass_toctou') is True
    assert hd032.get('cannot_merge_interrupt_kinds') is True
    assert catalog.get('cannot_merge_interrupt_kinds') is True
    assert matrix.get('cannot_merge_interrupt_kinds') is True
    assert tuple(catalog.get('templates') or ()) == _HD032_TEMPLATES
    assert tuple(catalog.get('not_run_captures') or ()) == _HD032_NOT_RUN
    missing = hd032.get('missing') or {}
    for key in (
        'live_fs_matrix', 'live_ssh_matrix', 'toctou_lab', 'attack_lab',
        'winui_shell', 'acl_quota_volume', 'second_process_attacker',
        'integration_ssh', 'integration_windows',
    ):
        assert missing.get(key) is True, key
    assert hd032.get('catalog') == 'evidence/files/catalog.json'
    assert hd032.get('support_matrix') == 'evidence/files/support-matrix.json'
    assert catalog.get('support_matrix') == 'evidence/files/support-matrix.json'
    assert catalog.get('l2_status') == 'implementation/hd-032-l2.json'
    assert hd032.get('herdr_executed') is False
    assert catalog.get('herdr_executed') is False
    redaction = catalog.get('redaction') or {}
    for key in ('host', 'user', 'path', 'credential', 'file_body'):
        assert redaction.get(key) == 'omitted', key
    cards = {item['id']: item for item in catalog['execution_cards']}
    assert tuple(cards) == _HD032_CARD_IDS
    seen_acs = set()
    for card in catalog['execution_cards']:
        card_id = card['id']
        assert card['live_status'] == 'UNVERIFIED'
        assert card['live_result'] == 'not_run'
        assert card['l1_status'] == 'shipped'
        assert card['l1_status'] != 'passed'
        assert card['required_evidence'] in {'L2', 'L3', 'L4'}
        assert card['required_evidence'] != 'L1'
        assert card['missing_grant'] == _HD032_CARD_GRANTS[card_id]
        assert card['owner_children'] == _HD032_CARD_OWNERS[card_id]
        assert tuple(card['ac_ids']) == _HD032_CARD_ACS[card_id]
        seen_acs.update(card['ac_ids'])
        assert card['l1_artifacts']
        for rel in card['l1_artifacts']:
            assert (ROOT / rel).is_file(), rel
        capture = ROOT / card['live_capture']
        assert capture.is_file()
        loaded = json.loads(capture.read_text(encoding='utf-8'))
        _reject_hd032_pass_claims(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        assert not _is_hd032_success(loaded.get('result'))
    assert _HD032_REQUIRED_ACS <= seen_acs
    interrupt_grants = [cards[item]['missing_grant'] for item in _HD032_INTERRUPT_KIND_IDS]
    assert interrupt_grants == [
        _HD032_CARD_GRANTS[item] for item in _HD032_INTERRUPT_KIND_IDS
    ]
    assert len(set(interrupt_grants)) == 3
    assert 'ssh-interrupt-kill-helper-kill' not in cards
    rows = {item['id']: item for item in catalog['live_rows']}
    assert tuple(rows) == _HD032_LIVE_IDS
    for row in catalog['live_rows']:
        assert row['status'] == 'UNVERIFIED'
        assert row['result'] == 'not_run'
        assert row.get('template') is False
        assert row.get('owner_children')
        assert row['missing_grant'] == _HD032_LIVE_GRANTS[row['id']]
        evidence = ROOT / row['evidence_path']
        assert evidence.is_file()
        loaded = json.loads(evidence.read_text(encoding='utf-8'))
        _reject_hd032_pass_claims(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
    for rel in _HD032_TEMPLATES:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd032_pass_claims(doc)
        assert doc.get('template') is True
        assert doc.get('document_kind') == 'template'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('captured_at_utc') is None
        assert not _is_hd032_success(doc.get('result'))
        assert doc.get('herdr_executed') is False
        assert 'stdout' not in doc and 'stderr' not in doc
    for rel in _HD032_NOT_RUN:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd032_pass_claims(doc)
        assert doc.get('template') is False
        assert doc.get('result') == 'not_run'
        assert doc.get('herdr_executed') is False
        assert doc.get('evidence_level') == 'not_run'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('host_fingerprint_redacted') is None
        assert doc.get('command_redacted') is None
        assert 'stdout' not in doc and 'stderr' not in doc
        blob = json.dumps(doc).lower()
        assert 'password' not in blob
        assert 'private_key' not in blob
    promised = {item['id'] for item in matrix['promised_range']}
    assert promised == {'windows-11-x64-client', 'linux-x64-remote'}
    by_platform = {item['id']: item for item in matrix['platforms']}
    for key in ('windows-11-x64-client', 'linux-x64-remote'):
        row = by_platform[key]
        assert row['promise'] == 'promised'
        assert row['live_status'] == 'not_run'
        assert row['live_result'] == 'not_run'
        assert row['compatible'] is False
        assert row['support'] == 'promised_not_run'
    macos = by_platform['macos-x64']
    assert macos['support'] == 'unsupported'
    assert macos['live_status'] == 'not_run'
    assert macos['compatible'] is False
    for key in ('macos-arm64', 'linux-arm64', 'windows-arm64'):
        row = by_platform[key]
        assert row['support'] in ('unsupported', 'experimental')
        assert row['support'] not in ('supported', 'stable', 'promised')
        assert row['live_status'] == 'not_run'
        assert row['compatible'] is False
    assert by_platform['windows-11-x64-client']['missing_grant'] != \
        by_platform['linux-x64-remote']['missing_grant']
    scenario_ids = [item['id'] for item in matrix['scenarios']]
    assert tuple(scenario_ids) == _HD032_SCENARIOS
    for scenario in matrix['scenarios']:
        assert scenario['live_status'] == 'not_run'
        assert scenario['live_result'] == 'not_run'
        assert scenario['support'] == 'l1_only'
    cell_keys = {(item['platform'], item['scenario']) for item in matrix['cells']}
    for platform in ('windows-11-x64-client', 'linux-x64-remote'):
        for scenario in scenario_ids:
            assert (platform, scenario) in cell_keys
    for cell in matrix['cells']:
        assert cell['live_status'] == 'not_run'
        assert cell['live_result'] == 'not_run'
        assert cell['compatible'] is False
        assert not _is_hd032_success(cell['live_result'])
    assert matrix.get('copy_windows_fields_onto_linux') is False
    assert matrix.get('extrapolate_macos_arm64') is False
    assert matrix.get('compatible_by_default') == []
    assert matrix.get('silent_overwrite_tested') is False
    assert catalog.get('silent_overwrite_tested') is False
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    check_integration_windows_layout(ROOT)


_HD033_PASS_KEYS = (
    'ac27_passed', 'ac28_passed', 'ac29_passed', 'ac37_passed',
    'ac38_passed', 'ac46_passed', 'g0_passed',
)
_HD033_REQUIRED_ACS = frozenset({
    'AC27', 'AC28', 'AC29', 'AC37', 'AC38', 'AC46',
})
_HD033_CARD_IDS = (
    'cold-start', 'input-to-visible-pixel', 'search-p95',
    'working-set-1-4-pane', 'hide-show-100', 'narrator',
    'dpi-100-150-200', 'eight-hour-soak',
)
_HD033_CARD_ACS = {
    'cold-start': ('AC28',),
    'input-to-visible-pixel': ('AC28',),
    'search-p95': ('AC28',),
    'working-set-1-4-pane': ('AC27', 'AC28'),
    'hide-show-100': ('AC29',),
    'narrator': ('AC37',),
    'dpi-100-150-200': ('AC38',),
    'eight-hour-soak': ('AC46',),
}
_HD033_CARD_OWNERS = {
    'cold-start': ['HD-011'],
    'input-to-visible-pixel': ['HD-014', 'HD-015'],
    'search-p95': ['HD-011', 'HD-023'],
    'working-set-1-4-pane': ['HD-025'],
    'hide-show-100': ['HD-025', 'HD-011'],
    'narrator': ['HD-011'],
    'dpi-100-150-200': ['HD-011', 'HD-014'],
    'eight-hour-soak': ['HD-025', 'HD-018'],
}
_HD033_CARD_GRANTS = {
    'cold-start': 'no_authorized_interactive_desktop_cold_start',
    'input-to-visible-pixel': 'no_authorized_visible_pixel_latency_probe',
    'search-p95': 'no_authorized_live_search_p95',
    'working-set-1-4-pane': 'no_authorized_working_set_process_sample',
    'hide-show-100': 'no_authorized_pane_hide_show_handle_lab',
    'narrator': 'ac37_workflow_incomplete_no_live_session',
    'dpi-100-150-200': 'no_authorized_dpi_theme_monitor_matrix',
    'eight-hour-soak': 'eight_hour_wall_clock_incomplete_no_live_herdr_fault_injection',
}
_HD033_LIVE_IDS = (
    'live-cold-start', 'live-input-pixel', 'live-search-p95',
    'live-working-set', 'live-handle-reclaim', 'live-narrator',
    'live-dpi', 'live-soak',
)
_HD033_LIVE_GRANTS = {
    'live-cold-start': 'no_authorized_interactive_desktop_cold_start',
    'live-input-pixel': 'no_authorized_visible_pixel_latency_probe',
    'live-search-p95': 'no_authorized_live_search_p95',
    'live-working-set': 'no_authorized_working_set_process_sample',
    'live-handle-reclaim': 'no_authorized_pane_hide_show_handle_lab',
    'live-narrator': 'ac37_workflow_incomplete_no_live_session',
    'live-dpi': 'no_authorized_dpi_theme_monitor_matrix',
    'live-soak': 'eight_hour_wall_clock_incomplete_no_live_herdr_fault_injection',
}
_HD033_L3_L4_KEYS = (
    'l3_ime', 'l3_narrator', 'l3_dpi', 'l4_soak',
)
_HD033_FALSE_KEYS = (
    'live_ime', 'live_narrator', 'live_dpi', 'live_soak',
    'live_input_pixel', 'live_working_set', 'eight_hour_soak_executed',
    'invented_timings', 'derive_process_memory_from_q_p',
    'hosted_ci_is_interactive_desktop', 'winui_admitted', 'herdr_executed',
    'copy_windows_fields_onto_linux', 'extrapolate_macos_arm64',
    'integration_ssh_project', 'integration_windows_project',
)
_HD033_TRUE_KEYS = (
    'parser_consumed_is_not_presentation',
    'parser_callback_cannot_pass_input_to_pixel',
)
_HD033_TIMING_KEYS = (
    'p95_ms', 'visible_pixel_ms', 'parser_consumed_ms', 'soak_hours',
    'working_set_bytes', 'private_bytes', 'handle_count', 'process_count',
    'q_p_bytes', 'sample_count', 'cycle_count', 'disconnect_switch_count',
    'resize_rate',
)
_HD033_SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
_HD033_SUPPORT_PASS = frozenset({'supported', 'stable', 'passed', 'compatible', 'success'})
_HD033_TEMPLATES = (
    'evidence/quality/live-cold-start.template.json',
    'evidence/quality/live-input-pixel.template.json',
    'evidence/quality/live-search-p95.template.json',
    'evidence/quality/live-working-set.template.json',
    'evidence/quality/live-handle-reclaim.template.json',
    'evidence/quality/live-narrator.template.json',
    'evidence/quality/live-dpi.template.json',
    'evidence/quality/live-soak.template.json',
)
_HD033_NOT_RUN = (
    'evidence/quality/live-cold-start.not-run.json',
    'evidence/quality/live-input-pixel.not-run.json',
    'evidence/quality/live-search-p95.not-run.json',
    'evidence/quality/live-working-set.not-run.json',
    'evidence/quality/live-handle-reclaim.not-run.json',
    'evidence/quality/live-narrator.not-run.json',
    'evidence/quality/live-dpi.not-run.json',
    'evidence/quality/live-soak.not-run.json',
)
_HD033_SCENARIOS = (
    'cold_start', 'input_to_visible_pixel', 'search_p95', 'working_set',
    'hide_show_100', 'narrator', 'dpi_theme', 'eight_hour_soak',
)


def _hd033_token(value):
    return value.strip().lower() if isinstance(value, str) else value


def _is_hd033_success(value) -> bool:
    if value is True:
        return True
    return _hd033_token(value) in _HD033_SUCCESS


def _reject_hd033_invented_timings(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key in _HD033_TIMING_KEYS and value is not None:
                raise AssertionError(f'{key} must stay null; do not invent timings')
            _reject_hd033_invented_timings(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd033_invented_timings(item)


def _reject_hd033_pass_claims(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key.endswith('_passed') and value is not False:
                raise AssertionError(f'{key} must stay false')
            if key == 'phase_gate' and _hd033_token(value) in {'passed', 'pass', 'ok'}:
                raise AssertionError('phase_gate must stay not_passed')
            if key in _HD033_L3_L4_KEYS and value != 'UNVERIFIED':
                raise AssertionError(f'{key} must stay UNVERIFIED')
            if key in {'live_result', 'result', 'live_status', 'status'} and _is_hd033_success(value):
                raise AssertionError(f'{key} must stay not_run or UNVERIFIED')
            if key == 'support' and _hd033_token(value) in _HD033_SUPPORT_PASS:
                raise AssertionError('support must not be a pass token')
            if key == 'compatible' and value is True:
                raise AssertionError('compatible must stay false')
            if key in _HD033_FALSE_KEYS and value is True:
                raise AssertionError(f'{key} must stay false')
            if key in _HD033_TRUE_KEYS and value is not True:
                raise AssertionError(f'{key} must stay true')
            if key == 'l1_status' and _hd033_token(value) in _HD033_SUCCESS:
                raise AssertionError('l1_status must not be a pass token')
            if key == 'l2_collectors' and _is_hd033_success(value):
                raise AssertionError('l2_collectors must not be a pass token')
            if key == 'l2_collectors_are_not_live_pass' and value is not True:
                raise AssertionError('l2_collectors_are_not_live_pass must stay true')
            if key == 'complete_1_0_claimed' and value is True:
                raise AssertionError('complete_1_0_claimed must stay false')
            if key == 'mib_bytes' and value not in (None, 1048576):
                raise AssertionError('mib_bytes must be 1048576')
            kind = _hd033_token(doc.get('kind'))
            if kind in {'live_eight_hour_soak', 'eight_hour_soak'} and _is_hd033_success(doc.get('result')):
                raise AssertionError('fake soak success is rejected')
            _reject_hd033_pass_claims(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd033_pass_claims(item)


def _check_hd033_closeout(hd033: dict, catalog: dict, matrix: dict) -> None:
    assert hd033.get('document_kind') == 'hd033_l2_status'
    assert catalog.get('document_kind') == 'hd033_performance_a11y_soak_catalog'
    assert matrix.get('document_kind') == 'hd033_support_matrix'
    for key in _HD033_L3_L4_KEYS:
        assert hd033.get(key) == 'UNVERIFIED', key
        if key in catalog:
            assert catalog.get(key) == 'UNVERIFIED', key
        if key in matrix:
            assert matrix.get(key) == 'UNVERIFIED', key
    for doc in (hd033, catalog, matrix):
        _reject_hd033_pass_claims(doc)
        _reject_hd033_invented_timings(doc)
        for key in _HD033_PASS_KEYS:
            assert doc.get(key) is False, key
        assert doc.get('phase_gate') != 'passed'
        if 'winui_admitted' in doc:
            assert doc.get('winui_admitted') is False
        if 'herdr_executed' in doc:
            assert doc.get('herdr_executed') is False
        if 'eight_hour_soak_executed' in doc:
            assert doc.get('eight_hour_soak_executed') is False
        if 'parser_consumed_is_not_presentation' in doc:
            assert doc.get('parser_consumed_is_not_presentation') is True
        if 'derive_process_memory_from_q_p' in doc:
            assert doc.get('derive_process_memory_from_q_p') is False
        if 'mib_bytes' in doc:
            assert doc.get('mib_bytes') == 1048576
        if 'hosted_ci_is_interactive_desktop' in doc:
            assert doc.get('hosted_ci_is_interactive_desktop') is False
    for key in _HD033_FALSE_KEYS:
        assert hd033.get(key) is False, key
        if key in catalog:
            assert catalog.get(key) is False, key
    for key in _HD033_TRUE_KEYS:
        assert hd033.get(key) is True, key
        assert catalog.get(key) is True, key
        assert matrix.get(key) is True, key
    assert hd033.get('mib_bytes') == 1048576
    assert catalog.get('mib_bytes') == 1048576
    assert matrix.get('mib_bytes') == 1048576
    assert tuple(catalog.get('templates') or ()) == _HD033_TEMPLATES
    assert tuple(catalog.get('not_run_captures') or ()) == _HD033_NOT_RUN
    missing = hd033.get('missing') or {}
    for key in (
        'interactive_desktop', 'visible_pixel_probe', 'working_set_lab',
        'narrator_desktop', 'dpi_theme_matrix', 'eight_hour_soak',
        'winui_shell', 'integration_windows', 'live_search_p95',
    ):
        assert missing.get(key) is True, key
    assert hd033.get('catalog') == 'evidence/quality/catalog.json'
    assert hd033.get('support_matrix') == 'evidence/quality/support-matrix.json'
    assert catalog.get('support_matrix') == 'evidence/quality/support-matrix.json'
    assert catalog.get('l2_status') == 'implementation/hd-033-l2.json'
    assert hd033.get('herdr_executed') is False
    assert catalog.get('herdr_executed') is False
    assert hd033.get('github_required_check') == 'UNVERIFIED'
    assert catalog.get('github_required_check') == 'UNVERIFIED'
    assert hd033.get('l2_collectors_are_not_live_pass') is True
    assert catalog.get('l2_collectors_are_not_live_pass') is True
    assert not _is_hd033_success(hd033.get('l2_collectors'))
    assert not _is_hd033_success(catalog.get('l2_collectors'))
    assert hd033.get('complete_1_0_claimed') is not True
    assert catalog.get('complete_1_0_claimed') is not True
    assert catalog.get('environment_manifest_pointer') == 'evidence/quality/environment-pointer.json'
    pointer = json.loads((ROOT / catalog['environment_manifest_pointer']).read_text(encoding='utf-8'))
    _reject_hd033_pass_claims(pointer)
    _reject_hd033_invented_timings(pointer)
    assert pointer.get('result') == 'not_run'
    assert pointer.get('live_status') == 'UNVERIFIED'
    assert pointer.get('committed_raw') is False
    assert pointer.get('eight_hour_soak_executed') is False
    assert pointer.get('live_dpi') is False
    assert pointer.get('live_narrator') is False
    assert pointer.get('github_required_check') == 'UNVERIFIED'
    assert pointer.get('l2_collectors_are_not_live_pass') is True
    assert pointer.get('complete_1_0_claimed') is not True
    assert catalog.get('narrator_overlay_pointer') == 'evidence/quality/narrator-overlay-pointer.json'
    assert catalog.get('narrator_product_ui_launch') == 'evidence/quality/narrator-product-ui-launch.json'
    overlay_pointer = json.loads((ROOT / catalog['narrator_overlay_pointer']).read_text(encoding='utf-8'))
    _reject_hd033_pass_claims(overlay_pointer)
    _reject_hd033_invented_timings(overlay_pointer)
    assert overlay_pointer.get('document_kind') == 'hd033_narrator_overlay_pointer'
    assert overlay_pointer.get('result') == 'not_run'
    assert overlay_pointer.get('live_status') == 'UNVERIFIED'
    assert overlay_pointer.get('live_narrator') is False
    assert overlay_pointer.get('ac37_passed') is False
    assert overlay_pointer.get('l3_narrator') == 'UNVERIFIED'
    assert overlay_pointer.get('automation_names_are_not_screen_reader_evidence') is True
    assert overlay_pointer.get('narrator_started_by_collector') is False
    assert overlay_pointer.get('invented_timings') is False
    assert overlay_pointer.get('committed_raw') is False
    assert overlay_pointer.get('github_required_check') == 'UNVERIFIED'
    assert overlay_pointer.get('l2_collectors_are_not_live_pass') is True
    assert overlay_pointer.get('complete_1_0_claimed') is not True
    assert (ROOT / 'scripts' / 'collect_narrator_overlay.py').is_file()
    assert catalog.get('dpi_overlay_pointer') == 'evidence/quality/dpi-overlay-pointer.json'
    assert catalog.get('theme_overlay_pointer') == 'evidence/quality/theme-overlay-pointer.json'
    assert catalog.get('soak_start_capture') == 'evidence/quality/live-soak-start.json'
    assert catalog.get('soak_elapsed_capture') == 'evidence/quality/live-soak-elapsed.json'
    assert catalog.get('soak_working_set_capture') == 'evidence/quality/live-soak-working-set.json'
    assert (ROOT / 'scripts' / 'record_soak_working_set.py').is_file()
    assert catalog.get('dpi_matrix_capture') == 'evidence/quality/live-dpi-matrix.json'
    assert (ROOT / 'scripts' / 'record_dpi_matrix.py').is_file()
    assert catalog.get('soak_interruption_capture') == 'evidence/quality/live-soak-interrupted.json'
    assert catalog.get('soak_interruption_captures') == [
        'evidence/quality/live-soak-interrupted.json',
        'evidence/quality/live-soak-interrupted-2.json',
        'evidence/quality/live-soak-interrupted-3.json',
        'evidence/quality/live-soak-interrupted-4.json',
        'evidence/quality/live-soak-interrupted-5.json',
        'evidence/quality/live-soak-interrupted-6.json',
    ]
    interruption = json.loads(
        (ROOT / catalog['soak_interruption_capture']).read_text(encoding='utf-8')
    )
    _reject_hd033_pass_claims(interruption)
    _reject_hd033_invented_timings(interruption)
    assert interruption.get('document_kind') == 'hd033_eight_hour_soak_interruption'
    assert interruption.get('result') == 'not_run'
    assert interruption.get('eight_hour_soak_executed') is False
    assert interruption.get('soak_hours') is None
    assert interruption.get('crash_cause') is None
    assert interruption.get('herddesk_crash_dump_found') is False
    second = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(second)
    _reject_hd033_invented_timings(second)
    assert second.get('document_kind') == 'hd033_eight_hour_soak_interruption'
    assert second.get('result') == 'not_run'
    assert second.get('eight_hour_soak_executed') is False
    assert second.get('soak_hours') is None
    assert second.get('crash_cause') is None
    assert second.get('started_at_utc') == '2026-09-10T11:11:22Z'
    assert second.get('owned_pids') == [46108, 64672, 71980]
    third = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-3.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(third)
    _reject_hd033_invented_timings(third)
    assert third.get('document_kind') == 'hd033_eight_hour_soak_interruption'
    assert third.get('result') == 'not_run'
    assert third.get('eight_hour_soak_executed') is False
    assert third.get('soak_hours') is None
    assert third.get('crash_cause') is None
    assert third.get('started_at_utc') == '2026-09-10T11:35:26Z'
    assert third.get('owned_pids') == [24744, 27844]
    assert third.get('last_heartbeat_alive_at_utc') == '2026-09-10T11:46:44Z'
    assert third.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T11:47:45Z'
    fourth = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-4.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(fourth)
    _reject_hd033_invented_timings(fourth)
    assert fourth.get('document_kind') == 'hd033_eight_hour_soak_interruption'
    assert fourth.get('result') == 'not_run'
    assert fourth.get('eight_hour_soak_executed') is False
    assert fourth.get('soak_hours') is None
    assert fourth.get('crash_cause') is None
    assert fourth.get('started_at_utc') == '2026-09-10T11:56:07Z'
    assert fourth.get('owned_pids') == [21312, 22400]
    assert fourth.get('last_heartbeat_alive_at_utc') == '2026-09-10T11:57:20Z'
    assert fourth.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T11:58:21Z'
    fifth = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-5.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(fifth)
    _reject_hd033_invented_timings(fifth)
    assert fifth.get('document_kind') == 'hd033_eight_hour_soak_interruption'
    assert fifth.get('result') == 'not_run'
    assert fifth.get('eight_hour_soak_executed') is False
    assert fifth.get('soak_hours') is None
    assert fifth.get('crash_cause') is None
    assert fifth.get('started_at_utc') == '2026-09-10T12:19:50Z'
    assert fifth.get('owned_pids') == [91852, 37708]
    assert fifth.get('last_heartbeat_alive_at_utc') == '2026-09-10T12:20:04Z'
    assert fifth.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T12:21:05Z'
    sixth = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-6.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(sixth)
    _reject_hd033_invented_timings(sixth)
    assert sixth.get('document_kind') == 'hd033_eight_hour_soak_interruption'
    assert sixth.get('result') == 'not_run'
    assert sixth.get('eight_hour_soak_executed') is False
    assert sixth.get('soak_hours') is None
    assert sixth.get('crash_cause') is None
    assert sixth.get('started_at_utc') == '2026-09-10T12:49:43Z'
    assert sixth.get('owned_pids') == [69668, 73960]
    assert sixth.get('last_heartbeat_alive_at_utc') == '2026-09-10T13:05:06Z'
    assert sixth.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T13:06:07Z'
    assert interruption.get('started_at_utc') == '2026-09-10T09:49:06Z'
    assert interruption.get('owned_pids') == [89580, 59552, 57712]
    dpi_pointer = json.loads((ROOT / catalog['dpi_overlay_pointer']).read_text(encoding='utf-8'))
    _reject_hd033_pass_claims(dpi_pointer)
    _reject_hd033_invented_timings(dpi_pointer)
    assert dpi_pointer.get('document_kind') == 'hd033_dpi_overlay_pointer'
    assert dpi_pointer.get('result') == 'not_run'
    assert dpi_pointer.get('live_status') == 'UNVERIFIED'
    assert dpi_pointer.get('live_dpi') is False
    assert dpi_pointer.get('ac38_passed') is False
    assert dpi_pointer.get('l3_dpi') == 'UNVERIFIED'
    assert dpi_pointer.get('dpi_matrix_100_150_200_executed') is False
    assert dpi_pointer.get('display_scale_changed_by_collector') is False
    assert dpi_pointer.get('single_dpi_sample_is_not_matrix') is True
    assert dpi_pointer.get('invented_timings') is False
    assert dpi_pointer.get('committed_raw') is False
    assert dpi_pointer.get('github_required_check') == 'UNVERIFIED'
    assert dpi_pointer.get('l2_collectors_are_not_live_pass') is True
    assert dpi_pointer.get('complete_1_0_claimed') is not True
    assert (ROOT / 'scripts' / 'collect_dpi_overlay.py').is_file()
    theme_pointer = json.loads((ROOT / catalog['theme_overlay_pointer']).read_text(encoding='utf-8'))
    _reject_hd033_pass_claims(theme_pointer)
    _reject_hd033_invented_timings(theme_pointer)
    assert theme_pointer.get('document_kind') == 'hd033_theme_overlay_pointer'
    assert theme_pointer.get('result') == 'not_run'
    assert theme_pointer.get('live_status') == 'UNVERIFIED'
    assert theme_pointer.get('live_dpi') is False
    assert theme_pointer.get('ac38_passed') is False
    assert theme_pointer.get('l3_dpi') == 'UNVERIFIED'
    assert theme_pointer.get('theme_matrix_executed') is False
    assert theme_pointer.get('high_contrast_executed') is False
    assert theme_pointer.get('multi_monitor_executed') is False
    assert theme_pointer.get('apps_use_light_theme_changed_by_collector') is False
    assert theme_pointer.get('high_contrast_changed_by_collector') is False
    assert theme_pointer.get('single_theme_sample_is_not_matrix') is True
    assert theme_pointer.get('invented_timings') is False
    assert theme_pointer.get('committed_raw') is False
    assert theme_pointer.get('github_required_check') == 'UNVERIFIED'
    assert theme_pointer.get('l2_collectors_are_not_live_pass') is True
    assert theme_pointer.get('complete_1_0_claimed') is not True
    assert (ROOT / 'scripts' / 'collect_theme_overlay.py').is_file()
    redaction = catalog.get('redaction') or {}
    for key in ('host', 'user', 'path', 'credential', 'terminal_body'):
        assert redaction.get(key) == 'omitted', key
    cards = {item['id']: item for item in catalog['execution_cards']}
    assert tuple(cards) == _HD033_CARD_IDS
    seen_acs = set()
    for card in catalog['execution_cards']:
        card_id = card['id']
        assert card['live_status'] == 'UNVERIFIED'
        assert card['live_result'] == 'not_run'
        assert card['l1_status'] == 'shipped'
        assert card['l1_status'] != 'passed'
        assert card['required_evidence'] in {'L2', 'L3', 'L4'}
        assert card['required_evidence'] != 'L1'
        assert card['missing_grant'] == _HD033_CARD_GRANTS[card_id]
        assert card['owner_children'] == _HD033_CARD_OWNERS[card_id]
        assert tuple(card['ac_ids']) == _HD033_CARD_ACS[card_id]
        seen_acs.update(card['ac_ids'])
        assert card['l1_artifacts']
        for rel in card['l1_artifacts']:
            assert (ROOT / rel).is_file(), rel
        capture = ROOT / card['live_capture']
        assert capture.is_file()
        loaded = json.loads(capture.read_text(encoding='utf-8'))
        _reject_hd033_pass_claims(loaded)
        _reject_hd033_invented_timings(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        assert not _is_hd033_success(loaded.get('result'))
        assert loaded.get('eight_hour_soak_executed') is not True
    assert _HD033_REQUIRED_ACS <= seen_acs
    assert cards['input-to-visible-pixel']['missing_grant'] != 'parser_consumed'
    rows = {item['id']: item for item in catalog['live_rows']}
    assert tuple(rows) == _HD033_LIVE_IDS
    for row in catalog['live_rows']:
        assert row['status'] == 'UNVERIFIED'
        assert row['result'] == 'not_run'
        assert row.get('template') is False
        assert row.get('owner_children')
        assert row['missing_grant'] == _HD033_LIVE_GRANTS[row['id']]
        evidence = ROOT / row['evidence_path']
        assert evidence.is_file()
        loaded = json.loads(evidence.read_text(encoding='utf-8'))
        _reject_hd033_pass_claims(loaded)
        _reject_hd033_invented_timings(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        if row['id'] == 'live-soak':
            assert loaded.get('eight_hour_soak_executed') is False
            assert loaded.get('soak_hours') is None
            assert not _is_hd033_success(loaded.get('result'))
        if row['id'] == 'live-input-pixel':
            assert loaded.get('parser_consumed_is_not_presentation') is True
            assert loaded.get('parser_callback_cannot_pass_input_to_pixel') is True
        if row['id'] == 'live-working-set':
            assert loaded.get('derive_process_memory_from_q_p') is False
            assert loaded.get('mib_bytes') == 1048576
            assert loaded.get('live_working_set') is False
            assert loaded.get('working_set_bytes') is None
            assert row['evidence_path'] == 'evidence/quality/live-working-set.not-run.json'
    for rel in _HD033_TEMPLATES:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd033_pass_claims(doc)
        _reject_hd033_invented_timings(doc)
        assert doc.get('template') is True
        assert doc.get('document_kind') == 'template'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('captured_at_utc') is None
        assert not _is_hd033_success(doc.get('result'))
        assert doc.get('herdr_executed') is False
        assert 'stdout' not in doc and 'stderr' not in doc
        if 'soak' in rel:
            assert doc.get('eight_hour_soak_executed') is False
            assert doc.get('soak_hours') is None
    for rel in _HD033_NOT_RUN:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd033_pass_claims(doc)
        _reject_hd033_invented_timings(doc)
        assert doc.get('template') is False
        assert doc.get('result') == 'not_run'
        assert doc.get('herdr_executed') is False
        assert doc.get('evidence_level') == 'not_run'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('host_fingerprint_redacted') is None
        assert doc.get('command_redacted') is None
        assert 'stdout' not in doc and 'stderr' not in doc
        blob = json.dumps(doc).lower()
        assert 'password' not in blob
        assert 'private_key' not in blob
    promised = {item['id'] for item in matrix['promised_range']}
    assert promised == {'windows-11-x64-client', 'linux-x64-remote'}
    by_platform = {item['id']: item for item in matrix['platforms']}
    for key in ('windows-11-x64-client', 'linux-x64-remote'):
        row = by_platform[key]
        assert row['promise'] == 'promised'
        assert row['live_status'] == 'not_run'
        assert row['live_result'] == 'not_run'
        assert row['compatible'] is False
        assert row['support'] == 'promised_not_run'
    macos = by_platform['macos-x64']
    assert macos['support'] == 'unsupported'
    assert macos['live_status'] == 'not_run'
    assert macos['compatible'] is False
    for key in ('macos-arm64', 'linux-arm64', 'windows-arm64'):
        row = by_platform[key]
        assert row['support'] in ('unsupported', 'experimental')
        assert row['support'] not in ('supported', 'stable', 'promised')
        assert row['live_status'] == 'not_run'
        assert row['compatible'] is False
    assert by_platform['windows-11-x64-client']['missing_grant'] != \
        by_platform['linux-x64-remote']['missing_grant']
    scenario_ids = [item['id'] for item in matrix['scenarios']]
    assert tuple(scenario_ids) == _HD033_SCENARIOS
    for scenario in matrix['scenarios']:
        assert scenario['live_status'] == 'not_run'
        assert scenario['live_result'] == 'not_run'
        assert scenario['support'] == 'l1_only'
    cell_keys = {(item['platform'], item['scenario']) for item in matrix['cells']}
    for platform in ('windows-11-x64-client', 'linux-x64-remote'):
        for scenario in scenario_ids:
            assert (platform, scenario) in cell_keys
    for cell in matrix['cells']:
        assert cell['live_status'] == 'not_run'
        assert cell['live_result'] == 'not_run'
        assert cell['compatible'] is False
        assert not _is_hd033_success(cell['live_result'])
    assert matrix.get('copy_windows_fields_onto_linux') is False
    assert matrix.get('extrapolate_macos_arm64') is False
    assert matrix.get('compatible_by_default') == []
    assert matrix.get('eight_hour_soak_executed') is False
    assert catalog.get('eight_hour_soak_executed') is False
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    check_integration_windows_layout(ROOT)


def _check_hd033_narrator_overlay() -> None:
    """Invoke shipped overlay. Do not start Narrator.exe or pass AC37."""
    assert (ROOT / 'scripts' / 'collect_narrator_overlay.py').is_file()
    assert (ROOT / 'evidence' / 'quality' / 'narrator-overlay-pointer.json').is_file()
    report = collect_narrator_overlay(ROOT)
    assert report.get('document_kind') == 'hd033_narrator_overlay'
    assert isinstance(report.get('narrator_exe_present'), bool)
    assert isinstance(report.get('narrator_launched'), bool)
    assert report.get('narrator_started_by_collector') is False
    assert report.get('automation_names_are_not_screen_reader_evidence') is True
    assert report.get('ac37_passed') is False
    assert report.get('live_narrator') is False
    assert report.get('result') == 'not_run'
    assert not _is_hd033_success(report.get('result'))
    assert report.get('l3_narrator') == 'UNVERIFIED'
    assert report.get('g0_passed') is False
    assert report.get('phase_gate') != 'passed'
    assert report.get('herdr_executed') is False
    assert report.get('invented_timings') is False
    assert report.get('pointer') == 'evidence/quality/narrator-overlay-pointer.json'
    assert report.get('live_capture') == 'evidence/quality/live-narrator.not-run.json'
    live = json.loads((ROOT / 'evidence' / 'quality' / 'live-narrator.not-run.json').read_text(encoding='utf-8'))
    assert live.get('result') == 'not_run'
    assert not _is_hd033_success(live.get('result'))
    assert live.get('live_narrator') is False


def _check_hd033_narrator_product_ui_launch() -> None:
    """Validate launch record. Do not start --ui or Narrator.exe. Not AC37."""
    assert (ROOT / 'scripts' / 'record_narrator_product_ui_launch.py').is_file()
    assert (ROOT / 'evidence' / 'quality' / 'narrator-product-ui-launch.json').is_file()
    report = validate_narrator_product_ui_launch(ROOT)
    assert report.get('document_kind') == 'hd033_narrator_product_ui_launch'
    assert report.get('product_ui_started') is True
    assert report.get('narrator_started_by_this_run') is True
    assert report.get('narrator_started_by_collector') is False
    assert report.get('automation_names_are_not_screen_reader_evidence') is True
    assert report.get('ac37_passed') is False
    assert report.get('ac37_workflow_completed') is False
    assert report.get('live_narrator') is False
    assert report.get('result') == 'not_run'
    assert not _is_hd033_success(report.get('result'))
    assert report.get('l3_narrator') == 'UNVERIFIED'
    assert report.get('g0_passed') is False
    assert report.get('phase_gate') != 'passed'
    assert report.get('herdr_executed') is False
    assert report.get('invented_timings') is False
    launch = json.loads(
        (ROOT / 'evidence' / 'quality' / 'narrator-product-ui-launch.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(launch)
    _reject_hd033_invented_timings(launch)
    assert launch.get('result') == 'not_run'
    assert launch.get('live_narrator') is False
    assert launch.get('ac37_passed') is False
    assert launch.get('ac37_workflow_completed') is False
    assert launch.get('product_ui_started') is True
    assert launch.get('narrator_started_by_this_run') is True
    assert launch.get('keyboard_chrome') in {'ctrl_k_sent', 'set_foreground_failed'}
    assert str(launch.get('keyboard_chrome') or '').lower() not in {
        'success', 'passed', 'ok', 'completed',
    }
    src = (ROOT / 'scripts' / 'record_narrator_product_ui_launch.py').read_text(encoding='utf-8')
    assert 'AttachThreadInput' in src
    assert 'AllowSetForegroundWindow' in src
    assert 'SendInput' in src
    assert 'keybd_event' not in src
    assert 'IMAGENAME eq HerdDesk.App.exe' not in src
    assert "['taskkill', '/PID'" in src
    assert 'win-x64' in src
    steps = launch.get('ac37_steps')
    assert isinstance(steps, dict)
    for key in ('search', 'request_control', 'release', 'close_confirm'):
        assert steps.get(key) == 'not_completed', key
    uia = launch.get('uia') or {}
    assert isinstance(uia.get('names_sample'), list)
    assert uia.get('names_sample') == []
    assert isinstance(uia.get('name_count'), int)
    assert int(uia.get('name_count')) >= 0
    ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
    just = (ROOT / 'justfile').read_text(encoding='utf-8')
    assert 'record_narrator_product_ui_launch.py --record' not in ci
    assert 'record_narrator_product_ui_launch.py --record' not in just
    assert '--ui' not in ci
    assert_justfile_ui_is_opt_in_dev(just)


def _check_hd033_dpi_overlay() -> None:
    """Invoke shipped overlay. Do not change display scale or pass AC38."""
    assert (ROOT / 'scripts' / 'collect_dpi_overlay.py').is_file()
    assert (ROOT / 'evidence' / 'quality' / 'dpi-overlay-pointer.json').is_file()
    report = collect_dpi_overlay(ROOT)
    assert report.get('document_kind') == 'hd033_dpi_overlay'
    dpi = report.get('system_dpi')
    if sys.platform == 'win32':
        assert isinstance(dpi, int)
        assert dpi > 0
    else:
        assert dpi is None
    assert report.get('single_dpi_sample_is_not_matrix') is True
    assert report.get('display_scale_changed_by_collector') is False
    assert report.get('dpi_matrix_100_150_200_executed') is False
    assert report.get('ac38_passed') is False
    assert report.get('live_dpi') is False
    assert report.get('result') == 'not_run'
    assert not _is_hd033_success(report.get('result'))
    assert report.get('l3_dpi') == 'UNVERIFIED'
    assert report.get('g0_passed') is False
    assert report.get('phase_gate') != 'passed'
    assert report.get('herdr_executed') is False
    assert report.get('invented_timings') is False
    assert report.get('pointer') == 'evidence/quality/dpi-overlay-pointer.json'
    assert report.get('live_capture') == 'evidence/quality/live-dpi.not-run.json'
    live = json.loads((ROOT / 'evidence' / 'quality' / 'live-dpi.not-run.json').read_text(encoding='utf-8'))
    assert live.get('result') == 'not_run'
    assert not _is_hd033_success(live.get('result'))
    assert live.get('live_dpi') is False
    assert live.get('dpi_matrix_100_150_200_executed') is False
    assert live.get('display_scale_changed_by_collector') is False


def _check_hd033_theme_overlay() -> None:
    """Invoke shipped overlay. Do not change theme or pass AC38."""
    assert (ROOT / 'scripts' / 'collect_theme_overlay.py').is_file()
    assert (ROOT / 'evidence' / 'quality' / 'theme-overlay-pointer.json').is_file()
    src = (ROOT / 'scripts' / 'herddesk_g0' / 'quality.py').read_text(encoding='utf-8')
    cli = (ROOT / 'scripts' / 'collect_theme_overlay.py').read_text(encoding='utf-8')
    assert 'SPI_SETHIGHCONTRAST' not in src
    assert 'SPI_SETHIGHCONTRAST' not in cli
    assert 'SetValueEx' not in src
    assert 'DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE' not in src
    report = collect_theme_overlay(ROOT)
    assert report.get('document_kind') == 'hd033_theme_overlay'
    count = report.get('monitor_count')
    if sys.platform == 'win32':
        assert isinstance(count, int)
        assert count >= 1
        assert report.get('high_contrast') in (True, False)
    else:
        assert count is None
        assert report.get('apps_use_light_theme') is None
        assert report.get('system_uses_light_theme') is None
        assert report.get('high_contrast') is None
    assert report.get('single_theme_sample_is_not_matrix') is True
    assert report.get('theme_matrix_executed') is False
    assert report.get('high_contrast_executed') is False
    assert report.get('multi_monitor_executed') is False
    assert report.get('apps_use_light_theme_changed_by_collector') is False
    assert report.get('high_contrast_changed_by_collector') is False
    assert report.get('ac38_passed') is False
    assert report.get('live_dpi') is False
    assert report.get('result') == 'not_run'
    assert not _is_hd033_success(report.get('result'))
    assert report.get('l3_dpi') == 'UNVERIFIED'
    assert report.get('g0_passed') is False
    assert report.get('phase_gate') != 'passed'
    assert report.get('herdr_executed') is False
    assert report.get('invented_timings') is False
    assert report.get('pointer') == 'evidence/quality/theme-overlay-pointer.json'
    assert report.get('live_capture') == 'evidence/quality/live-dpi.not-run.json'
    live = json.loads((ROOT / 'evidence' / 'quality' / 'live-dpi.not-run.json').read_text(encoding='utf-8'))
    assert live.get('result') == 'not_run'
    assert not _is_hd033_success(live.get('result'))
    assert live.get('live_dpi') is False
    ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
    just = (ROOT / 'justfile').read_text(encoding='utf-8')
    assert 'collect_theme_overlay.py --record' not in ci
    assert 'collect_theme_overlay.py --record' not in just


def _check_hd033_soak_start() -> None:
    """Validate soak START record. Do not claim AC46 or 8h elapsed."""
    assert (ROOT / 'scripts' / 'start_eight_hour_soak.py').is_file()
    assert (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').is_file()
    spawn_src = (ROOT / 'scripts' / 'start_eight_hour_soak.py').read_text(encoding='utf-8')
    assert 'STARTF_USESHOWWINDOW' in spawn_src
    assert 'SW_SHOWMINNOACTIVE = 7' in spawn_src
    assert "ui_env['HERDDESK_SOAK_MINIMIZED'] = '1'" in spawn_src
    assert (
        'CREATE_BREAKAWAY_FROM_JOB | CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS'
        in spawn_src
    )
    heartbeat_src = spawn_src[
        spawn_src.find('def run_heartbeat'):spawn_src.find('def record(')
    ]
    assert '_show_min_no_active' in heartbeat_src
    assert 'if not alive:' in heartbeat_src
    assert 'return 0' in heartbeat_src
    start_heartbeat_src = spawn_src[
        spawn_src.find('def _start_heartbeat'):spawn_src.find('def run_heartbeat')
    ]
    assert 'def record_elapsed' in spawn_src
    assert '--record-elapsed' in spawn_src
    assert 'def run_elapsed_watch' in spawn_src
    assert 'def watch_elapsed' in spawn_src
    assert '--watch-elapsed' in spawn_src
    assert 'eight_hour_wall_clock_incomplete' in spawn_src
    assert 'env=_dotnet_env()' in start_heartbeat_src
    assert 'HERDDESK_SOAK_MINIMIZED' not in start_heartbeat_src
    policy_src = (
        ROOT / 'src' / 'HerdDesk.App' / 'Quality' / 'SoakLaunchPolicy.cs'
    ).read_text(encoding='utf-8')
    assert 'HERDDESK_SOAK_MINIMIZED' in policy_src
    assert 'MinimizedEnabledValue = "1"' in policy_src
    window_src = (ROOT / 'src' / 'HerdDesk.App' / 'MainWindow.xaml.cs').read_text(
        encoding='utf-8'
    )
    assert 'if (SoakLaunchPolicy.SuppressWindowClose())' in window_src
    assert 'AppWindow.Closing += OnSoakAppWindowClosing' in window_src
    assert 'args.Cancel = true' in window_src
    start = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(start)
    _reject_hd033_invented_timings(start)
    assert start.get('result') == 'not_run'
    assert start.get('live_soak') is False
    assert start.get('ac46_passed') is False
    assert start.get('eight_hour_soak_executed') is False
    assert start.get('soak_hours') is None
    assert start.get('disconnect_switch_count') is None
    assert start.get('product_ui_started') is True
    assert start.get('l4_soak') == 'UNVERIFIED'
    assert start.get('prior_interruption_capture') == 'evidence/quality/live-soak-interrupted.json'
    interruption = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(interruption)
    _reject_hd033_invented_timings(interruption)
    interrupt_report = validate_eight_hour_soak_interruption(ROOT)
    assert interrupt_report.get('document_kind') == 'hd033_eight_hour_soak_interruption'
    assert interrupt_report.get('result') == 'not_run'
    assert interrupt_report.get('eight_hour_soak_executed') is False
    assert interrupt_report.get('soak_hours') is None
    assert interrupt_report.get('ac46_passed') is False
    assert interrupt_report.get('live_soak') is False
    assert interrupt_report.get('process_running_at_capture') is False
    assert interrupt_report.get('crash_cause') is None
    assert interruption.get('result') == 'not_run'
    assert interruption.get('eight_hour_soak_executed') is False
    assert interruption.get('soak_hours') is None
    assert interruption.get('crash_cause') is None
    assert interruption.get('herddesk_crash_dump_found') is False
    assert interruption.get('application_error_herddesk') is False
    assert interruption.get('xerox_print_experience_crash_unrelated') is True
    second = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-2.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(second)
    _reject_hd033_invented_timings(second)
    assert second.get('started_at_utc') == '2026-09-10T11:11:22Z'
    assert second.get('owned_pids') == [46108, 64672, 71980]
    assert second.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T11:14:54Z'
    assert second.get('eight_hour_soak_executed') is False
    assert second.get('soak_hours') is None
    assert second.get('crash_cause') is None
    third = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-3.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(third)
    _reject_hd033_invented_timings(third)
    assert third.get('started_at_utc') == '2026-09-10T11:35:26Z'
    assert third.get('owned_pids') == [24744, 27844]
    assert third.get('last_heartbeat_alive_at_utc') == '2026-09-10T11:46:44Z'
    assert third.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T11:47:45Z'
    assert third.get('eight_hour_soak_executed') is False
    assert third.get('soak_hours') is None
    assert third.get('crash_cause') is None
    fourth = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-4.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(fourth)
    _reject_hd033_invented_timings(fourth)
    assert fourth.get('started_at_utc') == '2026-09-10T11:56:07Z'
    assert fourth.get('owned_pids') == [21312, 22400]
    assert fourth.get('last_heartbeat_alive_at_utc') == '2026-09-10T11:57:20Z'
    assert fourth.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T11:58:21Z'
    assert fourth.get('eight_hour_soak_executed') is False
    assert fourth.get('soak_hours') is None
    assert fourth.get('crash_cause') is None
    fifth = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-5.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(fifth)
    _reject_hd033_invented_timings(fifth)
    assert fifth.get('started_at_utc') == '2026-09-10T12:19:50Z'
    assert fifth.get('owned_pids') == [91852, 37708]
    assert fifth.get('heartbeat_pid') == 37708
    assert fifth.get('last_heartbeat_alive_at_utc') == '2026-09-10T12:20:04Z'
    assert fifth.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T12:21:05Z'
    assert fifth.get('eight_hour_soak_executed') is False
    assert fifth.get('soak_hours') is None
    assert fifth.get('crash_cause') is None
    assert fifth.get('ac46_passed') is False
    assert fifth.get('process_running_at_capture') is False
    sixth = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-interrupted-6.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(sixth)
    _reject_hd033_invented_timings(sixth)
    assert sixth.get('started_at_utc') == '2026-09-10T12:49:43Z'
    assert sixth.get('owned_pids') == [69668, 73960]
    assert sixth.get('heartbeat_pid') == 73960
    assert sixth.get('last_heartbeat_alive_at_utc') == '2026-09-10T13:05:06Z'
    assert sixth.get('first_heartbeat_empty_alive_at_utc') == '2026-09-10T13:06:07Z'
    assert sixth.get('eight_hour_soak_executed') is False
    assert sixth.get('soak_hours') is None
    assert sixth.get('crash_cause') is None
    assert sixth.get('ac46_passed') is False
    assert sixth.get('process_running_at_capture') is False
    committed = soak_interrupt_capture_rels(ROOT)
    assert interruption.get('started_at_utc') == '2026-09-10T09:49:06Z'
    assert interruption.get('owned_pids') == [89580, 59552, 57712]
    commands = start.get('commands')
    assert isinstance(commands, list) and commands
    ui = commands[0]
    argv = [str(part) for part in ui.get('command_redacted') or []]
    assert '--ui' in argv
    assert '--shell-smoke' not in argv
    assert '--compose-only' not in argv
    assert '--project' not in argv
    ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
    just = (ROOT / 'justfile').read_text(encoding='utf-8')
    assert 'start_eight_hour_soak.py --record' not in ci
    assert 'start_eight_hour_soak.py --record' not in just
    assert 'start_eight_hour_soak.py --record-elapsed' not in ci
    assert 'start_eight_hour_soak.py --record-elapsed' not in just
    assert 'start_eight_hour_soak.py --watch-elapsed' not in ci
    assert 'start_eight_hour_soak.py --watch-elapsed' not in just
    elapsed_path = ROOT / SOAK_ELAPSED_REL
    elapsed_report = validate_eight_hour_soak_elapsed(ROOT)
    if elapsed_path.is_file():
        assert elapsed_report is not None
        assert elapsed_report.get('ac46_passed') is False
        assert elapsed_report.get('live_soak') is False
        assert elapsed_report.get('soak_hours') is None
        elapsed = json.loads(elapsed_path.read_text(encoding='utf-8'))
        # Do not call _reject_hd033_pass_claims(elapsed): eight_hour_soak_executed
        # is true on this document by design and is not an AC46 pass.
        assert elapsed.get('ac46_passed') is False
        assert elapsed.get('ac29_passed') is False
        assert elapsed.get('live_working_set') is False
        assert elapsed.get('live_soak') is False
        assert elapsed.get('soak_hours') is None
        assert elapsed.get('disconnect_switch_count') is None
        assert elapsed.get('herdr_executed') is False
        assert elapsed.get('g0_passed') is False
        assert elapsed.get('eight_hour_soak_executed') is True
        assert elapsed.get('owned_pids') == start.get('owned_pids')
        for key, value in elapsed.items():
            if isinstance(key, str) and key.endswith('_passed'):
                assert value is False, key
        assert start.get('eight_hour_soak_executed') is False
        assert start.get('soak_hours') is None
        assert start.get('ac46_passed') is False
    else:
        assert elapsed_report is None
    assert start.get('prior_interruption_captures') == committed
    assert start.get('prior_interruption_captures') == [
        'evidence/quality/live-soak-interrupted.json',
        'evidence/quality/live-soak-interrupted-2.json',
        'evidence/quality/live-soak-interrupted-3.json',
        'evidence/quality/live-soak-interrupted-4.json',
        'evidence/quality/live-soak-interrupted-5.json',
        'evidence/quality/live-soak-interrupted-6.json',
    ]
    report = validate_eight_hour_soak_start(ROOT)
    assert report.get('document_kind') == 'hd033_eight_hour_soak_start'
    assert report.get('product_ui_started') is True
    assert report.get('eight_hour_soak_executed') is False
    assert report.get('soak_hours') is None
    assert report.get('live_soak') is False
    assert report.get('ac46_passed') is False
    assert report.get('result') == 'not_run'
    assert not _is_hd033_success(report.get('result'))
    assert report.get('l4_soak') == 'UNVERIFIED'
    assert report.get('g0_passed') is False
    assert report.get('phase_gate') != 'passed'
    assert report.get('herdr_executed') is False
    assert report.get('invented_timings') is False
    assert isinstance(start.get('started_at_utc'), str)
    assert start.get('started_at_utc') > '2026-09-10T13:06:07Z'
    assert start.get('started_at_utc') != '2026-09-10T12:49:43Z'
    assert start.get('started_at_utc') != '2026-09-10T12:19:50Z'
    assert start.get('started_at_utc') != '2026-09-10T11:56:07Z'
    assert start.get('started_at_utc') != '2026-09-10T11:35:26Z'
    assert start.get('started_at_utc') != '2026-09-10T11:11:22Z'
    assert start.get('started_at_utc') != '2026-09-10T09:49:06Z'
    assert isinstance(start.get('owned_pids'), list) and start.get('owned_pids')
    assert start.get('owned_pids') != [69668, 73960]
    assert start.get('owned_pids') != [91852, 37708]
    assert start.get('owned_pids') != [21312, 22400]
    assert start.get('started_at_utc') != interruption.get('started_at_utc')
    assert start.get('started_at_utc') != second.get('started_at_utc')
    assert start.get('started_at_utc') != third.get('started_at_utc')
    assert start.get('started_at_utc') != fourth.get('started_at_utc')
    assert start.get('started_at_utc') != fifth.get('started_at_utc')
    assert start.get('started_at_utc') != sixth.get('started_at_utc')
    assert set(start['owned_pids']).isdisjoint(set(interruption['owned_pids']))
    assert set(start['owned_pids']).isdisjoint(set(second['owned_pids']))
    assert set(start['owned_pids']).isdisjoint(set(third['owned_pids']))
    assert set(start['owned_pids']).isdisjoint(set(fourth['owned_pids']))
    assert set(start['owned_pids']).isdisjoint(set(fifth['owned_pids']))
    assert set(start['owned_pids']).isdisjoint(set(sixth['owned_pids']))
    assert report.get('prior_interruption_started_at_utc') == (
        interruption.get('started_at_utc')
    )


def _check_hd033_soak_working_set() -> None:
    """Validate optional soak-process overlay. Do not claim AC29 or AC46."""
    assert (ROOT / 'scripts' / 'record_soak_working_set.py').is_file()
    src = (ROOT / 'scripts' / 'record_soak_working_set.py').read_text(encoding='utf-8')
    assert '--record' in src
    assert 'do not launch' in src.lower() or 'without launching another --ui' in src
    assert 'CREATE_BREAKAWAY_FROM_JOB' in src or '_spawn_detached' in src
    assert 'hd033-soak-resources.jsonl' in src
    assert 'live-working-set.not-run.json' in src
    assert 'sampler_added_to_start_owned_pids' in src
    assert 'if not alive:' in src
    assert 'return 0' in src
    assert 'taskkill' not in src.lower()
    assert "ui_argv" not in src
    record_src = src[src.find('def record('):src.find('def _unrecorded_report')]
    assert 'if os.name != \'nt\' and not hooks:' in record_src
    assert (
        "if os.name != 'nt':\n        raise QualityError('missing_record_field')"
        not in record_src
    )
    ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
    just = (ROOT / 'justfile').read_text(encoding='utf-8')
    assert 'record_soak_working_set.py --record' not in ci
    assert 'record_soak_working_set.py --record' not in just
    not_run = json.loads((ROOT / LIVE_WORKING_SET_REL).read_text(encoding='utf-8'))
    _reject_hd033_pass_claims(not_run)
    _reject_hd033_invented_timings(not_run)
    assert not_run.get('result') == 'not_run'
    assert not_run.get('live_working_set') is False
    assert not_run.get('working_set_bytes') is None
    assert not_run.get('kind') == 'live_working_set'
    start = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-soak-start.json').read_text(
            encoding='utf-8'
        )
    )
    app_pid = soak_start_app_pid(start)
    assert isinstance(app_pid, int) and app_pid > 0
    overlay_path = ROOT / SOAK_WORKING_SET_REL
    report = validate_soak_working_set(ROOT)
    if overlay_path.is_file():
        assert report is not None
        assert report.get('ac29_passed') is False
        assert report.get('ac46_passed') is False
        assert report.get('live_working_set') is False
        assert report.get('eight_hour_soak_executed') is False
        assert report.get('soak_hours') is None
        assert report.get('one_quarter_pane_lab') is False
        assert report.get('open_close_100') is False
        overlay = json.loads(overlay_path.read_text(encoding='utf-8'))
        _reject_hd033_pass_claims(overlay)
        assert overlay.get('ac29_passed') is False
        assert overlay.get('ac46_passed') is False
        assert overlay.get('live_working_set') is False
        assert overlay.get('eight_hour_soak_executed') is False
        assert overlay.get('soak_hours') is None
        assert overlay.get('one_quarter_pane_lab') is False
        assert overlay.get('open_close_100') is False
        assert overlay.get('pid') == app_pid
        assert overlay.get('started_at_utc') == start.get('started_at_utc')
        assert isinstance(overlay.get('working_set_bytes'), int)
        assert overlay.get('working_set_bytes') > 0
        sampler = overlay.get('sampler_pid')
        if isinstance(sampler, int):
            assert sampler not in start.get('owned_pids')
        assert overlay.get('sampler_added_to_start_owned_pids') is False
    else:
        assert report is None


def _check_hd033_dpi_matrix() -> None:
    """Validate optional scale-only DPI matrix. Do not claim AC38 or live_dpi."""
    assert (ROOT / 'scripts' / 'record_dpi_matrix.py').is_file()
    src = (ROOT / 'scripts' / 'record_dpi_matrix.py').read_text(encoding='utf-8')
    assert '--record' in src
    assert 'DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE' in src
    assert 'DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE' in src
    assert 'SetProcessDpiAwarenessContext' in src
    assert 'GetDpiForMonitor' in src
    assert 'IMAGENAME eq HerdDesk.App.exe' not in src
    assert 'RunUiSmoke' not in src
    assert 'taskkill' in src
    assert "'/PID'" in src or '"/PID"' in src
    record_src = src[src.find('def record('):src.find('def _unrecorded_report')]
    assert "if os.name != 'nt' and not hooks:" in record_src
    assert (
        "if os.name != 'nt':\n        raise QualityError('missing_record_field')"
        not in record_src
    )
    ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
    just = (ROOT / 'justfile').read_text(encoding='utf-8')
    assert 'record_dpi_matrix.py --record' not in ci
    assert 'record_dpi_matrix.py --record' not in just
    pointer = json.loads(
        (ROOT / 'evidence' / 'quality' / 'dpi-overlay-pointer.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(pointer)
    assert pointer.get('dpi_matrix_100_150_200_executed') is False
    assert pointer.get('display_scale_changed_by_collector') is False
    assert pointer.get('ac38_passed') is False
    assert pointer.get('live_dpi') is False
    live = json.loads(
        (ROOT / 'evidence' / 'quality' / 'live-dpi.not-run.json').read_text(
            encoding='utf-8'
        )
    )
    _reject_hd033_pass_claims(live)
    assert live.get('live_dpi') is False
    assert live.get('dpi_matrix_100_150_200_executed') is False
    assert live.get('display_scale_changed_by_collector') is False
    overlay_path = ROOT / DPI_MATRIX_REL
    report = validate_dpi_matrix(ROOT)
    if overlay_path.is_file():
        assert report is not None
        assert report.get('ac38_passed') is False
        assert report.get('live_dpi') is False
        assert report.get('l3_dpi') == 'UNVERIFIED'
        assert report.get('g0_passed') is False
        assert report.get('herdr_executed') is False
        assert report.get('theme_matrix_executed') is False
        assert report.get('high_contrast_executed') is False
        assert report.get('multi_monitor_executed') is False
        overlay = json.loads(overlay_path.read_text(encoding='utf-8'))
        # Do not call _reject_hd033_pass_claims(overlay) or
        # _reject_dpi_pass_claims(overlay): dpi_matrix_100_150_200_executed
        # and display_scale_changed_by_this_record may be true on this file only.
        assert overlay.get('ac38_passed') is False
        assert overlay.get('live_dpi') is False
        assert overlay.get('l3_dpi') == 'UNVERIFIED'
        assert overlay.get('g0_passed') is False
        assert overlay.get('herdr_executed') is False
        assert overlay.get('theme_matrix_executed') is False
        assert overlay.get('high_contrast_executed') is False
        assert overlay.get('multi_monitor_executed') is False
        assert overlay.get('resize_rate') is None
        assert overlay.get('result') == 'not_run'
        assert overlay.get('restore_ok') is True
        assert overlay.get('display_scale_changed_by_collector') is False
        for key, value in overlay.items():
            if isinstance(key, str) and key.endswith('_passed'):
                assert value is False, key
        assert pointer.get('dpi_matrix_100_150_200_executed') is False
        assert pointer.get('display_scale_changed_by_collector') is False
        assert live.get('dpi_matrix_100_150_200_executed') is False
        assert live.get('display_scale_changed_by_collector') is False
    else:
        assert report is None


_HD034_PASS_KEYS = (
    'ac41_passed', 'ac42_passed', 'g0_passed',
)
_HD034_REQUIRED_ACS = frozenset({'AC41', 'AC42'})
_HD034_CARD_IDS = (
    'clean-install', 'runtime-missing', 'signed-update',
    'bad-publisher-or-tamper', 'signed-rollback', 'config-backup-restore',
    'file-job-defer', 'unsigned-local-build',
)
_HD034_CARD_ACS = {
    'clean-install': ('AC41',),
    'runtime-missing': ('AC41',),
    'signed-update': ('AC42',),
    'bad-publisher-or-tamper': ('AC42',),
    'signed-rollback': ('AC42',),
    'config-backup-restore': ('AC42',),
    'file-job-defer': ('AC42',),
    'unsigned-local-build': ('AC41',),
}
_HD034_CARD_OWNERS = {
    'clean-install': ['HD-007', 'HD-011'],
    'runtime-missing': ['HD-007'],
    'signed-update': ['HD-007'],
    'bad-publisher-or-tamper': ['HD-021', 'HD-007'],
    'signed-rollback': ['HD-007'],
    'config-backup-restore': ['HD-007'],
    'file-job-defer': ['HD-028'],
    'unsigned-local-build': ['HD-007'],
}
_HD034_CARD_GRANTS = {
    'clean-install': 'no_authorized_clean_machine_install',
    'runtime-missing': 'no_authorized_runtime_missing_vm',
    'signed-update': 'no_authorized_signed_update_channel',
    'bad-publisher-or-tamper': 'no_authorized_publisher_identity',
    'signed-rollback': 'no_authorized_signed_rollback',
    'config-backup-restore': 'no_authorized_config_restore_on_install',
    'file-job-defer': 'no_authorized_file_job_defer_during_update',
    'unsigned-local-build': 'no_authorized_signing_service',
}
_HD034_LIVE_IDS = (
    'live-clean-install', 'live-runtime-missing', 'live-signed-update',
    'live-bad-publisher-or-tamper', 'live-signed-rollback',
    'live-config-backup-restore', 'live-file-job-defer',
    'live-unsigned-local-build',
)
_HD034_LIVE_GRANTS = {
    'live-clean-install': 'no_authorized_clean_machine_install',
    'live-runtime-missing': 'no_authorized_runtime_missing_vm',
    'live-signed-update': 'no_authorized_signed_update_channel',
    'live-bad-publisher-or-tamper': 'no_authorized_publisher_identity',
    'live-signed-rollback': 'no_authorized_signed_rollback',
    'live-config-backup-restore': 'no_authorized_config_restore_on_install',
    'live-file-job-defer': 'no_authorized_file_job_defer_during_update',
    'live-unsigned-local-build': 'no_authorized_signing_service',
}
_HD034_L2_L3_KEYS = (
    'l2_live_install', 'l2_live_sign', 'l2_live_update', 'l2_live_rollback',
    'l3_clean_machine',
)
_HD034_FALSE_KEYS = (
    'live_install', 'live_sign', 'live_update', 'live_rollback',
    'live_clean_machine', 'publisher_identity_confirmed', 'signed_msix_built',
    'clean_machine_install_executed', 'unsigned_local_build_is_release',
    'exe_copy_is_rollback', 'silent_admin_runtime_install',
    'parallel_self_update_service', 'official_app_installer_downgrade_default',
    'linux_msix_client', 'invented_package_hashes',
    'invented_publisher_identity',
    'windows_app_sdk_admitted', 'killed_user_daemon', 'winui_admitted',
    'herdr_executed', 'copy_windows_fields_onto_linux',
    'extrapolate_macos_arm64', 'integration_ssh_project',
    'integration_windows_project',
)
_HD034_TRUE_KEYS = (
    'fake_publisher_cannot_pass_ac41',
    'unsigned_local_build_cannot_pass_release_install',
    'exe_copy_cannot_pass_rollback',
    'evergreen_webview2_planned_runtime',
    'missing_runtime_must_prompt',
    'app_installer_planned_channel',
    'herdr_upgrade_independent',
)
_HD034_NULL_KEYS = (
    'publisher', 'publisher_cn', 'certificate_subject',
    'certificate_thumbprint', 'thumbprint', 'timestamp_url',
    'distribution_url', 'app_installer_url', 'package_sha256',
    'msix_sha256', 'appinstaller_sha256', 'sidecar_sha256',
    'install_hours', 'soak_hours',
)
_HD034_SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
_HD034_SUPPORT_PASS = frozenset({'supported', 'stable', 'passed', 'compatible', 'success'})
_HD034_TEMPLATES = (
    'evidence/packaging/live-clean-install.template.json',
    'evidence/packaging/live-runtime-missing.template.json',
    'evidence/packaging/live-signed-update.template.json',
    'evidence/packaging/live-bad-publisher-or-tamper.template.json',
    'evidence/packaging/live-signed-rollback.template.json',
    'evidence/packaging/live-config-backup-restore.template.json',
    'evidence/packaging/live-file-job-defer.template.json',
    'evidence/packaging/live-unsigned-local-build.template.json',
)
_HD034_NOT_RUN = (
    'evidence/packaging/live-clean-install.not-run.json',
    'evidence/packaging/live-runtime-missing.not-run.json',
    'evidence/packaging/live-signed-update.not-run.json',
    'evidence/packaging/live-bad-publisher-or-tamper.not-run.json',
    'evidence/packaging/live-signed-rollback.not-run.json',
    'evidence/packaging/live-config-backup-restore.not-run.json',
    'evidence/packaging/live-file-job-defer.not-run.json',
    'evidence/packaging/live-unsigned-local-build.not-run.json',
)
_HD034_SCENARIOS = (
    'clean_install', 'runtime_missing', 'signed_update',
    'bad_publisher_or_tamper', 'signed_rollback', 'config_backup_restore',
    'file_job_defer', 'unsigned_local_build',
)
_HD034_LAB_NAME = 'HerdDesk.Lab'
_HD034_LAB_PUBLISHER = 'CN=HerdDesk Lab (not release)'
_HD034_SCRIPT = ROOT / 'scripts' / 'package_release.ps1'
_HD034_CERT_SCRIPT = ROOT / 'scripts' / 'new_lab_certificate.ps1'
_HD034_LAB_SIGN_POINTER = 'evidence/packaging/lab-sign-overlay-pointer.json'
_HD034_LAB_MSIX_REL = LAB_MSIX_REL
_HD034_VALID_LAYOUT = 'tests/fixtures/packaging/layout-valid'
_HD034_STORE_LAYOUT = 'tests/fixtures/packaging/layout-store-identity'
_HD034_PRIVATE_KEY_SUFFIXES = ('.pfx', '.p12', '.pem', '.key', '.snk')
_HD034_SCRIPT_CONTRACT_OK = False
_HD034_FORBIDDEN_PFX = (
    'packaging/HerdDesk.Lab.pfx',
    'src/HerdDesk.App/HerdDesk.Lab.pfx',
    'tests/fixtures/packaging/lab.pfx',
)


def find_pwsh() -> str:
    pwsh = shutil.which('pwsh')
    if not pwsh:
        raise AssertionError('pwsh is missing; HD-034 Verify-on-fixture requires PowerShell 7')
    return pwsh


def run_package_release(
    action: str,
    *,
    layout_path: str | None = None,
    certificate_path: str | None = None,
    output_root: Path | None = None,
    timeout: int = 120,
) -> tuple[int, dict]:
    """Invoke shipped scripts/package_release.ps1. Do not reimplement identity checks."""
    assert action in {'Build', 'Verify', 'Sign'}, action
    if action == 'Build':
        raise AssertionError('HD-034 structure/python tests must not run -Action Build')
    out = Path(output_root) if output_root is not None else Path(
        tempfile.mkdtemp(prefix='herddesk-packaging-')
    )
    out.mkdir(parents=True, exist_ok=True)
    cmd = [
        find_pwsh(), '-NoLogo', '-NoProfile', '-NonInteractive',
        '-File', str(_HD034_SCRIPT),
        '-Action', action,
        '-OutputRoot', str(out),
    ]
    if layout_path:
        cmd += ['-LayoutPath', layout_path]
    if certificate_path:
        cmd += ['-CertificatePath', certificate_path]
    completed = subprocess.run(
        cmd,
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        timeout=timeout,
        check=False,
    )
    report_path = out / 'package-report.json'
    report: dict = {}
    if report_path.is_file():
        report = json.loads(report_path.read_text(encoding='utf-8'))
    code = completed.returncode if completed.returncode is not None else 1
    return code, report


def _parse_ps_json_object(text: str) -> dict:
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
            value = json.loads(blob[start:end + 1])
            return value if isinstance(value, dict) else {}
        return {}


def run_new_lab_certificate(
    certificate_path: str | Path,
    *,
    timeout: int = 120,
) -> tuple[int, dict]:
    """Invoke shipped scripts/new_lab_certificate.ps1. Do not reimplement path checks."""
    cmd = [
        find_pwsh(), '-NoLogo', '-NoProfile', '-NonInteractive',
        '-File', str(_HD034_CERT_SCRIPT),
        '-CertificatePath', str(certificate_path),
    ]
    completed = subprocess.run(
        cmd,
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding='utf-8',
        errors='replace',
        timeout=timeout,
        check=False,
    )
    report = _parse_ps_json_object(completed.stdout)
    if not report:
        report = _parse_ps_json_object(completed.stderr)
    code = completed.returncode if completed.returncode is not None else 1
    return code, report


def _reject_hd034_private_key_files(root: Path) -> None:
    if not root.exists():
        return
    for path in root.rglob('*'):
        if not path.is_file():
            continue
        name = path.name.lower()
        if name.endswith(_HD034_PRIVATE_KEY_SUFFIXES) or name.startswith(('id_rsa', 'id_ed25519')):
            raise AssertionError(f'private key material is not allowed in git: {path}')


def _check_hd034_package_files() -> None:
    packaging = ROOT / 'packaging'
    assert packaging.is_dir()
    assert (packaging / 'Package.appxmanifest').is_file()
    assert (packaging / 'runtime.json').is_file()
    assert (packaging / 'CLAUDE.md').is_file()
    assert (packaging / 'Assets' / 'StoreLogo.png').is_file()
    assert (packaging / 'Assets' / 'Square44x44Logo.png').is_file()
    assert (packaging / 'Assets' / 'Square150x150Logo.png').is_file()
    assert _HD034_SCRIPT.is_file()
    assert _HD034_CERT_SCRIPT.is_file()
    assert (ROOT / 'scripts' / 'record_lab_msix.py').is_file()
    assert (ROOT / _HD034_LAB_SIGN_POINTER).is_file()
    assert (ROOT / _HD034_VALID_LAYOUT / 'AppxManifest.xml').is_file()
    assert (ROOT / _HD034_STORE_LAYOUT / 'AppxManifest.xml').is_file()
    assert not (packaging / 'HerdDesk.Package.wapproj').exists()
    assert not list(packaging.rglob('*.wapproj'))
    ignore_lines = [
        line.strip()
        for line in (ROOT / '.gitignore').read_text(encoding='utf-8').splitlines()
        if line.strip() and not line.lstrip().startswith('#')
    ]
    assert '*.pfx' in ignore_lines
    _reject_hd034_private_key_files(packaging)
    _reject_hd034_private_key_files(ROOT / 'tests' / 'fixtures' / 'packaging')
    runtime = json.loads((packaging / 'runtime.json').read_text(encoding='utf-8'))
    assert runtime.get('document_kind') == 'hd034_packaging_method'
    assert runtime.get('script') == 'scripts/package_release.ps1'
    assert runtime.get('method') == 'dotnet_publish_layout'
    assert runtime.get('packaging_project') is False
    assert runtime.get('wapproj') is False
    assert runtime.get('windows_package_type') == 'None'
    assert runtime.get('enable_msix_tooling') is False
    assert runtime.get('unsigned_local_build_is_release') is False
    assert runtime.get('publisher_identity_confirmed') is False
    assert runtime.get('signed_msix_built') is False
    assert runtime.get('ac41_passed') is False
    assert runtime.get('ac42_passed') is False
    assert runtime.get('g0_passed') is False
    assert runtime.get('phase_gate') != 'passed'
    assert (ROOT / 'src' / 'HerdDesk.App' / 'App.xaml').is_file()
    ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
    validation_job, sep, desktop_job = ci.partition('windows-desktop:')
    assert sep, 'windows-desktop job missing'
    assert '-Action Build' not in validation_job
    assert '-Action Sign' not in ci
    assert 'new_lab_certificate.ps1' not in ci
    assert 'record_lab_msix.py --record' not in ci
    assert '--ui' not in ci
    just = (ROOT / 'justfile').read_text(encoding='utf-8')
    assert '-Action Build' not in just
    assert '-Action Sign' not in just
    assert 'new_lab_certificate.ps1' not in just
    assert 'record_lab_msix.py --record' not in just
    assert_justfile_ui_is_opt_in_dev(just)


def _check_hd034_package_script_contract() -> None:
    global _HD034_SCRIPT_CONTRACT_OK
    _check_hd034_package_files()
    if _HD034_SCRIPT_CONTRACT_OK:
        return
    code, report = run_package_release('Verify', layout_path=_HD034_VALID_LAYOUT)
    assert code == 0, report
    assert report.get('ok') is True
    assert report.get('action') == 'Verify'
    assert report.get('identity_name') == _HD034_LAB_NAME
    assert report.get('publisher') == _HD034_LAB_PUBLISHER
    assert report.get('private_key_found') is False
    assert report.get('unsigned_local_build_is_release') is False
    assert report.get('signed') is False
    assert report.get('is_release_install') is False
    assert report.get('ac41_passed') is False
    assert report.get('ac42_passed') is False
    assert report.get('g0_passed') is False
    store_code, store_report = run_package_release('Verify', layout_path=_HD034_STORE_LAYOUT)
    assert store_code != 0, store_report
    assert store_report.get('ok') is not True
    src_code, src_report = run_package_release('Verify', layout_path='packaging')
    assert src_code == 0, src_report
    assert src_report.get('ok') is True
    assert src_report.get('identity_name') == _HD034_LAB_NAME
    assert src_report.get('publisher') == _HD034_LAB_PUBLISHER
    assert src_report.get('processor_architecture') == 'x64'
    assert src_report.get('unsigned_local_build_is_release') is False
    assert src_report.get('is_release_install') is False
    assert src_report.get('signed') is False
    assert src_report.get('ac41_passed') is False
    assert src_report.get('ac42_passed') is False
    assert src_report.get('g0_passed') is False
    sign_code, sign_report = run_package_release('Sign')
    assert sign_code != 0, sign_report
    assert sign_report.get('ok') is not True
    assert sign_report.get('signed') is not True
    _check_hd034_lab_certificate_script_contract()
    _HD034_SCRIPT_CONTRACT_OK = True


def _check_hd034_lab_sign_pointer(doc: dict) -> None:
    assert doc.get('document_kind') == 'hd034_lab_sign_overlay_pointer'
    _reject_hd034_pass_claims(doc)
    _reject_hd034_invented_identity(doc)
    assert doc.get('result') == 'not_run'
    assert not _is_hd034_success(doc.get('result'))
    assert doc.get('l2_live_sign') == 'UNVERIFIED'
    assert doc.get('l2_live_install') == 'UNVERIFIED'
    assert doc.get('l3_clean_machine') == 'UNVERIFIED'
    assert doc.get('script') == 'scripts/new_lab_certificate.ps1'
    assert doc.get('package_script') == 'scripts/package_release.ps1'
    assert doc.get('lab_certificate_script_is_not_signed_release_msix') is True
    assert doc.get('lab_identity_not_release') is True
    assert doc.get('lab_identity_not_store') is True
    assert doc.get('is_release_install') is False
    assert doc.get('signed_msix_built') is False
    assert doc.get('publisher_identity_confirmed') is False
    assert doc.get('live_sign') is False
    assert doc.get('pfx_written') is False
    assert doc.get('pfx_in_git') is False
    assert doc.get('ac41_passed') is False
    assert doc.get('ac42_passed') is False
    assert doc.get('g0_passed') is False
    assert doc.get('phase_gate') != 'passed'
    assert doc.get('fake_publisher_cannot_pass_ac41') is True
    assert doc.get('unsigned_local_build_cannot_pass_release_install') is True
    assert (_HD034_CERT_SCRIPT).is_file()


def _assert_hd034_gitignored(rel: str) -> None:
    completed = subprocess.run(
        ['git', 'check-ignore', '-q', rel],
        cwd=str(ROOT),
        check=False,
    )
    assert completed.returncode == 0, rel


def _check_hd034_lab_msix() -> None:
    """Validate optional lab pack+sign overlay. Do not claim AC41."""
    script = ROOT / 'scripts' / 'record_lab_msix.py'
    assert script.is_file()
    src = script.read_text(encoding='utf-8')
    assert '--record' in src
    assert 'new_lab_certificate.ps1' in src
    assert '-Action' in src
    assert 'Build' in src
    assert 'Sign' in src
    assert 'DISPLAYCONFIG' not in src
    assert "['--ui'" not in src and '"--ui"' not in src
    record_src = src[src.find('def record(') : src.find('def _unrecorded_report')]
    assert "if os.name != 'nt' and not hooks:" in record_src
    assert 'Add-AppxPackage' not in record_src
    ci = (ROOT / '.github' / 'workflows' / 'ci.yml').read_text(encoding='utf-8')
    just = (ROOT / 'justfile').read_text(encoding='utf-8')
    assert 'record_lab_msix.py --record' not in ci
    assert 'record_lab_msix.py --record' not in just
    assert '-Action Sign' not in ci
    assert '-Action Sign' not in just
    assert 'new_lab_certificate.ps1' not in ci
    assert 'new_lab_certificate.ps1' not in just
    pointer = json.loads((ROOT / _HD034_LAB_SIGN_POINTER).read_text(encoding='utf-8'))
    _check_hd034_lab_sign_pointer(pointer)
    overlay_path = ROOT / _HD034_LAB_MSIX_REL
    report = validate_lab_msix(ROOT)
    if overlay_path.is_file():
        assert report is not None
        overlay = json.loads(overlay_path.read_text(encoding='utf-8'))
        # Do not call _reject_hd034_pass_claims(overlay) or
        # _reject_hd034_invented_identity(overlay): lab_msix_packed and
        # lab_signature_applied may be true on this file only.
        assert overlay.get('document_kind') == LAB_MSIX_KIND
        assert overlay.get('result') == 'not_run'
        assert overlay.get('ac41_passed') is False
        assert overlay.get('ac42_passed') is False
        assert overlay.get('g0_passed') is False
        assert overlay.get('phase_gate') != 'passed'
        assert overlay.get('signed_msix_built') is False
        assert overlay.get('live_sign') is False
        assert overlay.get('live_install') is False
        assert overlay.get('publisher_identity_confirmed') is False
        assert overlay.get('is_release_install') is False
        assert overlay.get('herdr_executed') is False
        assert overlay.get('lab_msix_packed') is True
        assert overlay.get('lab_signature_applied') is True
        assert overlay.get('makeappx_found') is True
        assert overlay.get('signtool_found') is True
        assert overlay.get('fake_publisher_cannot_pass_ac41') is True
        assert overlay.get('unsigned_local_build_cannot_pass_release_install') is True
        digest = overlay.get('lab_msix_sha256')
        assert isinstance(digest, str) and len(digest) == 64
        for key in _HD034_NULL_KEYS:
            if key in overlay:
                assert overlay.get(key) is None, key
        assert pointer.get('signed_msix_built') is False
        assert pointer.get('live_sign') is False
        assert pointer.get('pfx_written') is False
        assert report.get('ac41_passed') is False
        assert report.get('signed_msix_built') is False
        assert report.get('lab_msix_packed') is True
        assert report.get('lab_signature_applied') is True
    else:
        assert report is None
    for rel in (
        'artifacts/certs',
        'artifacts/certs/HerdDesk.Lab.pfx',
        'artifacts/packaging',
        'artifacts/packaging/HerdDesk.Lab.msix',
        'probe-results/hd034-lab-msix.json',
    ):
        _assert_hd034_gitignored(rel)


def _check_hd034_lab_certificate_script_contract() -> None:
    assert _HD034_CERT_SCRIPT.is_file()
    text = _HD034_CERT_SCRIPT.read_text(encoding='utf-8')
    assert _HD034_LAB_PUBLISHER in text
    assert 'Cert:\\CurrentUser\\My' in text
    assert 'Cert:\\LocalMachine' not in text
    for rel in _HD034_FORBIDDEN_PFX:
        target = ROOT / rel
        assert not target.exists(), rel
        code, report = run_new_lab_certificate(target)
        assert code != 0, report
        assert report.get('ok') is not True
        assert report.get('pfx_written') is not True
        assert report.get('pfx_in_git') is not True
        assert report.get('ac41_passed') is False
        assert report.get('ac42_passed') is False
        assert report.get('g0_passed') is False
        assert report.get('is_release_install') is False
        assert report.get('signed_msix_built') is False
        assert report.get('publisher_identity_confirmed') is False
        assert report.get('document_kind') == 'hd034_lab_certificate'
        assert report.get('subject') == _HD034_LAB_PUBLISHER
        assert not target.exists(), rel


def _hd034_token(value):
    return value.strip().lower() if isinstance(value, str) else value


def _is_hd034_success(value) -> bool:
    if value is True:
        return True
    return _hd034_token(value) in _HD034_SUCCESS


def _reject_hd034_invented_identity(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key in _HD034_NULL_KEYS and value is not None:
                raise AssertionError(f'{key} must stay null; do not invent identity, hashes, or install hours')
            _reject_hd034_invented_identity(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd034_invented_identity(item)


def _reject_hd034_pass_claims(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key.endswith('_passed') and value is not False:
                raise AssertionError(f'{key} must stay false')
            if key == 'phase_gate' and _hd034_token(value) in {'passed', 'pass', 'ok'}:
                raise AssertionError('phase_gate must stay not_passed')
            if key in _HD034_L2_L3_KEYS and value != 'UNVERIFIED':
                raise AssertionError(f'{key} must stay UNVERIFIED')
            if key in {'live_result', 'result', 'live_status', 'status'} and _is_hd034_success(value):
                raise AssertionError(f'{key} must stay not_run or UNVERIFIED')
            if key == 'support' and _hd034_token(value) in _HD034_SUPPORT_PASS:
                raise AssertionError('support must not be a pass token')
            if key == 'compatible' and value is True:
                raise AssertionError('compatible must stay false')
            if key in _HD034_FALSE_KEYS and value is True:
                raise AssertionError(f'{key} must stay false')
            if key in _HD034_TRUE_KEYS and value is not True:
                raise AssertionError(f'{key} must stay true')
            if key == 'l1_status' and _hd034_token(value) in _HD034_SUCCESS:
                raise AssertionError('l1_status must not be a pass token')
            kind = _hd034_token(doc.get('kind'))
            if kind in {
                'live_clean_install', 'live_unsigned_local_build',
                'live_signed_rollback',
            } and _is_hd034_success(doc.get('result')):
                raise AssertionError('fake install, unsigned-release, or exe-copy rollback is rejected')
            if doc.get('unsigned_local_build_is_release') is True:
                raise AssertionError('unsigned local build cannot pass release install')
            if doc.get('exe_copy_is_rollback') is True:
                raise AssertionError('copying an old EXE cannot pass rollback')
            if doc.get('publisher_identity_confirmed') is True:
                raise AssertionError('fake publisher cannot pass AC41')
            if doc.get('linux_msix_client') is True:
                raise AssertionError('Linux is not an MSIX client')
            _reject_hd034_pass_claims(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd034_pass_claims(item)


def _check_hd034_closeout(hd034: dict, catalog: dict, matrix: dict) -> None:
    assert hd034.get('document_kind') == 'hd034_l2_status'
    assert catalog.get('document_kind') == 'hd034_packaging_catalog'
    assert matrix.get('document_kind') == 'hd034_support_matrix'
    assert catalog.get('simulation') is True
    assert catalog.get('fixture_origin') == 'synthetic'
    assert catalog.get('template') is False
    for key in _HD034_L2_L3_KEYS:
        assert hd034.get(key) == 'UNVERIFIED', key
        if key in catalog:
            assert catalog.get(key) == 'UNVERIFIED', key
        if key in matrix:
            assert matrix.get(key) == 'UNVERIFIED', key
    for doc in (hd034, catalog, matrix):
        _reject_hd034_pass_claims(doc)
        _reject_hd034_invented_identity(doc)
        for key in _HD034_PASS_KEYS:
            assert doc.get(key) is False, key
        assert doc.get('phase_gate') != 'passed'
        if 'winui_admitted' in doc:
            assert doc.get('winui_admitted') is False
        if 'herdr_executed' in doc:
            assert doc.get('herdr_executed') is False
        if 'publisher_identity_confirmed' in doc:
            assert doc.get('publisher_identity_confirmed') is False
        if 'signed_msix_built' in doc:
            assert doc.get('signed_msix_built') is False
        if 'clean_machine_install_executed' in doc:
            assert doc.get('clean_machine_install_executed') is False
        if 'unsigned_local_build_is_release' in doc:
            assert doc.get('unsigned_local_build_is_release') is False
        if 'exe_copy_is_rollback' in doc:
            assert doc.get('exe_copy_is_rollback') is False
        if 'linux_msix_client' in doc:
            assert doc.get('linux_msix_client') is False
        if 'fake_publisher_cannot_pass_ac41' in doc:
            assert doc.get('fake_publisher_cannot_pass_ac41') is True
        if 'unsigned_local_build_cannot_pass_release_install' in doc:
            assert doc.get('unsigned_local_build_cannot_pass_release_install') is True
        if 'exe_copy_cannot_pass_rollback' in doc:
            assert doc.get('exe_copy_cannot_pass_rollback') is True
    for key in _HD034_FALSE_KEYS:
        assert hd034.get(key) is False, key
        if key in catalog:
            assert catalog.get(key) is False, key
    for key in _HD034_TRUE_KEYS:
        assert hd034.get(key) is True, key
        assert catalog.get(key) is True, key
        assert matrix.get(key) is True, key
    assert tuple(catalog.get('templates') or ()) == _HD034_TEMPLATES
    assert tuple(catalog.get('not_run_captures') or ()) == _HD034_NOT_RUN
    missing = hd034.get('missing') or {}
    for key in (
        'clean_machine_install', 'runtime_missing_vm', 'signed_update_channel',
        'publisher_identity', 'signed_rollback', 'config_restore_on_install',
        'file_job_defer_during_update', 'signing_service', 'winui_shell',
        'integration_windows', 'packaging_project',
    ):
        assert missing.get(key) is True, key
    grants = hd034.get('missing_grants') or []
    assert tuple(grants) == tuple(_HD034_CARD_GRANTS[card_id] for card_id in _HD034_CARD_IDS)
    assert hd034.get('catalog') == 'evidence/packaging/catalog.json'
    assert hd034.get('support_matrix') == 'evidence/packaging/support-matrix.json'
    assert catalog.get('support_matrix') == 'evidence/packaging/support-matrix.json'
    assert catalog.get('l2_status') == 'implementation/hd-034-l2.json'
    assert hd034.get('herdr_executed') is False
    assert catalog.get('herdr_executed') is False
    redaction = catalog.get('redaction') or {}
    for key in ('host', 'user', 'path', 'credential', 'package_body'):
        assert redaction.get(key) == 'omitted', key
    cards = {item['id']: item for item in catalog['execution_cards']}
    assert tuple(cards) == _HD034_CARD_IDS
    seen_acs = set()
    seen_grants = []
    for card in catalog['execution_cards']:
        card_id = card['id']
        assert card['live_status'] == 'UNVERIFIED'
        assert card['live_result'] == 'not_run'
        assert card['l1_status'] == 'shipped'
        assert card['l1_status'] != 'passed'
        assert card['required_evidence'] in {'L2', 'L3', 'L4'}
        assert card['required_evidence'] != 'L1'
        assert card['missing_grant'] == _HD034_CARD_GRANTS[card_id]
        assert card['owner_children'] == _HD034_CARD_OWNERS[card_id]
        assert tuple(card['ac_ids']) == _HD034_CARD_ACS[card_id]
        seen_acs.update(card['ac_ids'])
        seen_grants.append(card['missing_grant'])
        assert card['l1_artifacts']
        for rel in card['l1_artifacts']:
            assert (ROOT / rel).is_file(), rel
        capture = ROOT / card['live_capture']
        assert capture.is_file()
        loaded = json.loads(capture.read_text(encoding='utf-8'))
        _reject_hd034_pass_claims(loaded)
        _reject_hd034_invented_identity(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        assert not _is_hd034_success(loaded.get('result'))
        assert loaded.get('publisher_identity_confirmed') is not True
        assert loaded.get('signed_msix_built') is not True
        assert loaded.get('unsigned_local_build_is_release') is not True
        assert loaded.get('exe_copy_is_rollback') is not True
    assert _HD034_REQUIRED_ACS <= seen_acs
    assert len(seen_grants) == len(set(seen_grants))
    rows = {item['id']: item for item in catalog['live_rows']}
    assert tuple(rows) == _HD034_LIVE_IDS
    for row in catalog['live_rows']:
        assert row['status'] == 'UNVERIFIED'
        assert row['result'] == 'not_run'
        assert row.get('template') is False
        assert row.get('owner_children')
        assert row['missing_grant'] == _HD034_LIVE_GRANTS[row['id']]
        evidence = ROOT / row['evidence_path']
        assert evidence.is_file()
        loaded = json.loads(evidence.read_text(encoding='utf-8'))
        _reject_hd034_pass_claims(loaded)
        _reject_hd034_invented_identity(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        if row['id'] == 'live-unsigned-local-build':
            assert loaded.get('unsigned_local_build_is_release') is False
            assert loaded.get('unsigned_local_build_cannot_pass_release_install') is True
            assert not _is_hd034_success(loaded.get('result'))
        if row['id'] == 'live-signed-rollback':
            assert loaded.get('exe_copy_is_rollback') is False
            assert loaded.get('exe_copy_cannot_pass_rollback') is True
        if row['id'] == 'live-clean-install':
            assert loaded.get('clean_machine_install_executed') is False
            assert loaded.get('fake_publisher_cannot_pass_ac41') is True
        if row['id'] == 'live-file-job-defer':
            assert loaded.get('killed_user_daemon') is False
            assert loaded.get('herdr_upgrade_independent') is True
    for rel in _HD034_TEMPLATES:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd034_pass_claims(doc)
        _reject_hd034_invented_identity(doc)
        assert doc.get('template') is True
        assert doc.get('document_kind') == 'template'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('captured_at_utc') is None
        assert not _is_hd034_success(doc.get('result'))
        assert doc.get('herdr_executed') is False
        assert 'stdout' not in doc and 'stderr' not in doc
        if 'unsigned-local-build' in rel:
            assert doc.get('unsigned_local_build_is_release') is False
            assert doc.get('unsigned_local_build_cannot_pass_release_install') is True
        if 'signed-rollback' in rel:
            assert doc.get('exe_copy_is_rollback') is False
            assert doc.get('exe_copy_cannot_pass_rollback') is True
    for rel in _HD034_NOT_RUN:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd034_pass_claims(doc)
        _reject_hd034_invented_identity(doc)
        assert doc.get('template') is False
        assert doc.get('result') == 'not_run'
        assert doc.get('herdr_executed') is False
        assert doc.get('evidence_level') == 'not_run'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('host_fingerprint_redacted') is None
        assert doc.get('command_redacted') is None
        assert 'stdout' not in doc and 'stderr' not in doc
        blob = json.dumps(doc).lower()
        assert 'password' not in blob
        assert 'private_key' not in blob
        assert '.pfx' not in blob
    promised = {item['id'] for item in matrix['promised_range']}
    assert promised == {'windows-11-x64-client'}
    by_platform = {item['id']: item for item in matrix['platforms']}
    windows = by_platform['windows-11-x64-client']
    assert windows['promise'] == 'promised'
    assert windows['live_status'] == 'not_run'
    assert windows['live_result'] == 'not_run'
    assert windows['compatible'] is False
    assert windows['support'] == 'promised_not_run'
    linux = by_platform['linux-x64-remote']
    assert linux['promise'] == 'none'
    assert linux['support'] == 'unsupported'
    assert linux['support'] not in ('supported', 'stable', 'promised', 'promised_not_run', 'passed')
    assert linux['live_status'] == 'not_run'
    assert linux['compatible'] is False
    assert linux.get('linux_msix') is False
    assert linux['role'] == 'remote'
    macos = by_platform['macos-x64']
    assert macos['support'] == 'unsupported'
    assert macos['live_status'] == 'not_run'
    assert macos['compatible'] is False
    for key in ('macos-arm64', 'linux-arm64', 'windows-arm64'):
        row = by_platform[key]
        assert row['support'] in ('unsupported', 'experimental')
        assert row['support'] not in ('supported', 'stable', 'promised')
        assert row['live_status'] == 'not_run'
        assert row['compatible'] is False
    assert windows['missing_grant'] != linux['missing_grant']
    scenario_ids = [item['id'] for item in matrix['scenarios']]
    assert tuple(scenario_ids) == _HD034_SCENARIOS
    for scenario in matrix['scenarios']:
        assert scenario['live_status'] == 'not_run'
        assert scenario['live_result'] == 'not_run'
        assert scenario['support'] == 'l1_only'
    cell_keys = {(item['platform'], item['scenario']) for item in matrix['cells']}
    for scenario in scenario_ids:
        assert ('windows-11-x64-client', scenario) in cell_keys
    for cell in matrix['cells']:
        assert cell['platform'] == 'windows-11-x64-client'
        assert cell['live_status'] == 'not_run'
        assert cell['live_result'] == 'not_run'
        assert cell['compatible'] is False
        assert not _is_hd034_success(cell['live_result'])
    assert matrix.get('copy_windows_fields_onto_linux') is False
    assert matrix.get('extrapolate_macos_arm64') is False
    assert matrix.get('linux_msix_client') is False
    assert matrix.get('compatible_by_default') == []
    assert catalog.get('linux_msix_client') is False
    assert hd034.get('packaging_project') is False
    assert catalog.get('packaging_project') is False
    assert matrix.get('packaging_project') is False
    criteria = json.loads((ROOT / 'planning' / 'acceptance.json').read_text(encoding='utf-8'))['criteria']
    acs = {item['id']: item for item in criteria}
    assert acs['AC41']['status'] != 'passed'
    assert acs['AC42']['status'] != 'passed'
    assert acs['AC41']['status'] == 'not_run'
    assert acs['AC42']['status'] == 'not_run'
    pointer_rel = catalog.get('lab_sign_overlay_pointer')
    assert pointer_rel == _HD034_LAB_SIGN_POINTER
    assert hd034.get('lab_sign_overlay_pointer') == pointer_rel
    pointer = json.loads((ROOT / pointer_rel).read_text(encoding='utf-8'))
    _check_hd034_lab_sign_pointer(pointer)
    assert catalog.get('lab_msix_capture') == _HD034_LAB_MSIX_REL
    assert (ROOT / 'scripts' / 'record_lab_msix.py').is_file()
    _check_hd034_package_files()
    check_integration_windows_layout(ROOT)
    assert not (ROOT / 'packaging' / 'HerdDesk.Package.wapproj').exists()


_HD035_PASS_KEYS = (
    'ac02_passed', 'ac43_passed', 'ac44_passed', 'g0_passed',
)
_HD035_REQUIRED_ACS = frozenset({'AC02', 'AC43', 'AC44'})
_HD035_CARD_IDS = (
    'license-inventory', 'herdrm-not-copied', 'nuget-scan', 'cargo-scan',
    'npm-scan', 'renderer-boundary', 'diagnostic-canary',
    'signed-package-reverse-audit',
)
_HD035_CARD_ACS = {
    'license-inventory': ('AC02',),
    'herdrm-not-copied': ('AC02',),
    'nuget-scan': ('AC43',),
    'cargo-scan': ('AC43',),
    'npm-scan': ('AC43',),
    'renderer-boundary': ('AC44',),
    'diagnostic-canary': ('AC44',),
    'signed-package-reverse-audit': ('AC43',),
}
_HD035_CARD_OWNERS = {
    'license-inventory': ['HD-002'],
    'herdrm-not-copied': ['HD-002', 'HD-006'],
    'nuget-scan': ['HD-007'],
    'cargo-scan': ['HD-008', 'HD-027'],
    'npm-scan': ['HD-014'],
    'renderer-boundary': ['HD-014', 'HD-020', 'HD-024'],
    'diagnostic-canary': ['HD-031'],
    'signed-package-reverse-audit': ['HD-032', 'HD-034'],
}
_HD035_CARD_GRANTS = {
    'license-inventory': 'no_authorized_maintainer_license_decision',
    'herdrm-not-copied': 'no_authorized_herdrm_reverse_audit_of_release_inputs',
    'nuget-scan': 'no_authorized_nuget_advisory_scan',
    'cargo-scan': 'no_authorized_cargo_advisory_scan',
    'npm-scan': 'no_authorized_npm_audit',
    'renderer-boundary': 'no_authorized_webview_process_observation',
    'diagnostic-canary': 'no_authorized_canary_diagnostic_export',
    'signed-package-reverse-audit': 'no_authorized_signed_package_unpack',
}
_HD035_LIVE_IDS = (
    'live-license-inventory', 'live-herdrm-not-copied', 'live-nuget-scan',
    'live-cargo-scan', 'live-npm-scan', 'live-renderer-boundary',
    'live-diagnostic-canary', 'live-signed-package-reverse-audit',
)
_HD035_LIVE_GRANTS = {
    'live-license-inventory': 'no_authorized_maintainer_license_decision',
    'live-herdrm-not-copied': 'no_authorized_herdrm_reverse_audit_of_release_inputs',
    'live-nuget-scan': 'no_authorized_nuget_advisory_scan',
    'live-cargo-scan': 'no_authorized_cargo_advisory_scan',
    'live-npm-scan': 'no_authorized_npm_audit',
    'live-renderer-boundary': 'no_authorized_webview_process_observation',
    'live-diagnostic-canary': 'no_authorized_canary_diagnostic_export',
    'live-signed-package-reverse-audit': 'no_authorized_signed_package_unpack',
}
_HD035_L2_L3_KEYS = (
    'l2_nuget_scan', 'l2_cargo_advisory', 'l2_npm_audit',
    'l2_live_renderer_process', 'l2_canary_export', 'l2_signed_package_unpack',
    'l3_signed_package_reverse_audit',
)
_HD035_FALSE_KEYS = (
    'nuget_scan_executed', 'cargo_advisory_executed', 'npm_audit_executed',
    'live_renderer_process_observed', 'canary_export_executed',
    'signed_package_unpacked', 'project_license_selected', 'herdrm_copied',
    'invented_scan_dates', 'invented_zero_vuln', 'invented_package_hashes',
    'invented_publisher_identity', 'scan_failure_overwritten',
    'final_unpacked_msix',
    'linux_msix_client', 'signed_msix_built', 'publisher_identity_confirmed',
    'winui_admitted', 'herdr_executed', 'copy_windows_fields_onto_linux',
    'extrapolate_macos_arm64', 'integration_ssh_project',
    'integration_windows_project', 'user_config_uploaded_to_scan_service',
    'invented_approved_admissions',
)
_HD035_TRUE_KEYS = (
    'public_visibility_is_not_license_grant',
    'missing_scan_is_not_zero_vuln',
    'confirmed_exploitable_critical_high_must_not_be_hidden_by_exception',
    'inventory_is_not_final_unpacked_msix',
    'cargo_lock_present',
    'nuget_lock_present',
    'npm_lock_present',
    'linux_x64_is_not_windows_renderer_substitute',
)
_HD035_NULL_KEYS = (
    'scan_tool_version', 'advisory_database_date', 'nuget_scan_date',
    'cargo_advisory_date', 'npm_audit_date',
    'confirmed_exploitable_critical_high', 'package_sha256', 'msix_sha256',
    'signed_package_sha256', 'publisher', 'certificate_subject',
    'certificate_thumbprint', 'canary_export_sha256',
)
_HD035_SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
_HD035_SUPPORT_PASS = frozenset({'supported', 'stable', 'passed', 'compatible', 'success'})
_HD035_TEMPLATES = (
    'evidence/security-release/live-license-inventory.template.json',
    'evidence/security-release/live-herdrm-not-copied.template.json',
    'evidence/security-release/live-nuget-scan.template.json',
    'evidence/security-release/live-cargo-scan.template.json',
    'evidence/security-release/live-npm-scan.template.json',
    'evidence/security-release/live-renderer-boundary.template.json',
    'evidence/security-release/live-diagnostic-canary.template.json',
    'evidence/security-release/live-signed-package-reverse-audit.template.json',
)
_HD035_NOT_RUN = (
    'evidence/security-release/live-license-inventory.not-run.json',
    'evidence/security-release/live-herdrm-not-copied.not-run.json',
    'evidence/security-release/live-nuget-scan.not-run.json',
    'evidence/security-release/live-cargo-scan.not-run.json',
    'evidence/security-release/live-npm-scan.not-run.json',
    'evidence/security-release/live-renderer-boundary.not-run.json',
    'evidence/security-release/live-diagnostic-canary.not-run.json',
    'evidence/security-release/live-signed-package-reverse-audit.not-run.json',
)
_HD035_SCENARIOS = (
    'license_inventory', 'herdrm_not_copied', 'nuget_scan', 'cargo_scan',
    'npm_scan', 'renderer_boundary', 'diagnostic_canary',
    'signed_package_reverse_audit',
)


def _hd035_token(value):
    return value.strip().lower() if isinstance(value, str) else value


def _is_hd035_success(value) -> bool:
    if value is True:
        return True
    return _hd035_token(value) in _HD035_SUCCESS


def _reject_hd035_invented_identity(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key in _HD035_NULL_KEYS and value is not None:
                raise AssertionError(
                    f'{key} must stay null; do not invent scan dates, tool versions, hashes, or zero-vuln counts'
                )
            if key == 'confirmed_exploitable_critical_high' and value == 0:
                raise AssertionError('missing scan is not zero vulnerabilities')
            _reject_hd035_invented_identity(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd035_invented_identity(item)


def _reject_hd035_pass_claims(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key.endswith('_passed') and value is not False:
                raise AssertionError(f'{key} must stay false')
            if key == 'phase_gate' and _hd035_token(value) in {'passed', 'pass', 'ok'}:
                raise AssertionError('phase_gate must stay not_passed')
            if key in _HD035_L2_L3_KEYS and value != 'UNVERIFIED':
                raise AssertionError(f'{key} must stay UNVERIFIED')
            if key in {'live_result', 'result', 'live_status', 'status'} and _is_hd035_success(value):
                raise AssertionError(f'{key} must stay not_run or UNVERIFIED')
            if key == 'support' and _hd035_token(value) in _HD035_SUPPORT_PASS:
                raise AssertionError('support must not be a pass token')
            if key == 'compatible' and value is True:
                raise AssertionError('compatible must stay false')
            if key in _HD035_FALSE_KEYS and value is True:
                raise AssertionError(f'{key} must stay false')
            if key in _HD035_TRUE_KEYS and value is not True:
                raise AssertionError(f'{key} must stay true')
            if key == 'l1_status' and _hd035_token(value) in _HD035_SUCCESS:
                raise AssertionError('l1_status must not be a pass token')
            if key == 'admission' and _hd035_token(value) == 'approved' and doc.get('name') == 'herdrm':
                raise AssertionError('herdrm cannot become approved')
            if doc.get('herdrm_copied') is True:
                raise AssertionError('herdrm copy is rejected')
            if doc.get('public_visibility_is_not_license_grant') is False:
                raise AssertionError('public visibility is not a license grant')
            if doc.get('missing_scan_is_not_zero_vuln') is False:
                raise AssertionError('missing scan is not zero vulnerabilities')
            if doc.get('nuget_scan_executed') is True or doc.get('cargo_advisory_executed') is True \
                    or doc.get('npm_audit_executed') is True:
                raise AssertionError('live scan was not executed')
            if doc.get('invented_zero_vuln') is True:
                raise AssertionError('fake zero-vuln scan is rejected')
            if doc.get('invented_scan_dates') is True:
                raise AssertionError('invented scan dates cannot pass as success')
            if doc.get('integration_windows_project') is True:
                raise AssertionError(
                    'integration_windows_project catalog token is not AC pass'
                )
            _reject_hd035_pass_claims(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd035_pass_claims(item)


def _check_hd035_inventory(inventory: dict) -> None:
    assert inventory.get('document_kind') == 'hd035_security_release_inventory'
    assert inventory.get('simulation') is True
    assert inventory.get('fixture_origin') == 'synthetic'
    assert inventory.get('template') is False
    assert inventory.get('source_register') == 'docs/licensing/register.json'
    assert inventory.get('source_markdown') == 'docs/licensing-register.md'
    assert inventory.get('final_unpacked_msix') is False
    assert inventory.get('signed_package_unpacked') is False
    assert inventory.get('inventory_is_not_final_unpacked_msix') is True
    assert inventory.get('project_license_selected') is False
    assert inventory.get('herdrm_copied') is False
    assert inventory.get('invented_approved_admissions') is False
    assert inventory.get('public_visibility_is_not_license_grant') is True
    for key in _HD035_PASS_KEYS:
        assert inventory.get(key) is False, key
    assert inventory.get('phase_gate') != 'passed'
    _reject_hd035_pass_claims(inventory)
    _reject_hd035_invented_identity(inventory)
    register = json.loads((ROOT / 'docs/licensing/register.json').read_text(encoding='utf-8'))
    register_units = [
        (item['name'], item['artifact_kind'], item['admission']) for item in register['units']
    ]
    listed = [
        (item['name'], item['artifact_kind'], item['admission']) for item in inventory['units']
    ]
    assert listed == register_units
    allowed_lock = {
        (item['name'], item['artifact_kind'])
        for item in register['units']
        if item.get('admission') == 'approved' and item.get('lock_allowed') is True
    }
    for name, kind, admission in listed:
        assert admission in {'pending', 'blocked', 'approved'}, (name, kind, admission)
        if admission == 'approved':
            assert (name, kind) in allowed_lock
        else:
            assert (name, kind) not in allowed_lock
    herdrm = [item for item in inventory['units'] if item['name'] == 'herdrm']
    assert len(herdrm) == 1
    assert herdrm[0]['admission'] == 'blocked'
    assert (ROOT / 'docs/licensing/register.json').is_file()
    assert (ROOT / 'docs/licensing-register.md').is_file()
    assert (ROOT / 'LICENSE-STATUS.md').is_file()


def _check_hd035_release_input_audit() -> None:
    """Invoke shipped auditor. Do not reimplement lock or herdrm checks."""
    assert (ROOT / 'scripts' / 'audit_release_inputs.py').is_file()
    report = audit_admitted_release_inputs(ROOT)
    assert report.get('document_kind') == 'hd035_admitted_release_input_audit'
    assert report.get('nuget_lock_present') is True
    assert report.get('npm_lock_present') is True
    assert report.get('cargo_lock_present') is True
    assert report.get('nuget_scan_executed') is False
    assert report.get('cargo_advisory_executed') is False
    assert report.get('npm_audit_executed') is False
    assert report.get('project_license_selected') is False
    assert report.get('herdrm_copied') is False
    assert report.get('herdrm_copies') == []
    assert report.get('webview2_nupkg_in_lock') is True
    assert report.get('webview2_evergreen_in_lock') is False
    assert report.get('winui_direct_version') == '2.3.6'
    assert report.get('wasdk_umbrella_in_lock') is False
    assert report.get('xterm_scoped_version') == '6.0.0'
    assert report.get('unscoped_xterm_admitted') is False
    assert report.get('invented_scan_dates') is False
    assert report.get('invented_zero_vuln') is False
    assert report.get('ac02_passed') is False
    assert report.get('ac43_passed') is False
    assert report.get('ac44_passed') is False
    assert report.get('g0_passed') is False
    assert report.get('phase_gate') != 'passed'
    assert report.get('signed_package_unpacked') is False
    assert report.get('public_visibility_is_not_license_grant') is True
    assert report.get('missing_scan_is_not_zero_vuln') is True
    assert report.get('extras') == []
    assert report.get('missing') == []
    assert report.get('version_drift') == []
    nuget_lock = (ROOT / ADMITTED_LOCK_REL).is_file()
    npm_lock = (ROOT / ADMITTED_NPM_LOCK_REL).is_file()
    cargo_lock = (
        (ROOT / 'bridge' / 'Cargo.lock').is_file()
        and (ROOT / 'filebridge' / 'Cargo.lock').is_file()
    )
    assert nuget_lock and npm_lock and cargo_lock
    assert report.get('nuget_lock_present') is nuget_lock
    assert report.get('npm_lock_present') is npm_lock
    assert report.get('cargo_lock_present') is cargo_lock
    assert not (ROOT / 'packages.lock.json').exists()


def _check_hd035_closeout(hd035: dict, catalog: dict, matrix: dict, inventory: dict) -> None:
    assert hd035.get('document_kind') == 'hd035_l2_status'
    assert catalog.get('document_kind') == 'hd035_security_release_catalog'
    assert matrix.get('document_kind') == 'hd035_support_matrix'
    assert catalog.get('simulation') is True
    assert catalog.get('fixture_origin') == 'synthetic'
    assert catalog.get('template') is False
    for key in _HD035_L2_L3_KEYS:
        assert hd035.get(key) == 'UNVERIFIED', key
        if key in catalog:
            assert catalog.get(key) == 'UNVERIFIED', key
        if key in matrix:
            assert matrix.get(key) == 'UNVERIFIED', key
    for doc in (hd035, catalog, matrix):
        _reject_hd035_pass_claims(doc)
        _reject_hd035_invented_identity(doc)
        for key in _HD035_PASS_KEYS:
            assert doc.get(key) is False, key
        assert doc.get('phase_gate') != 'passed'
        if 'winui_admitted' in doc:
            assert doc.get('winui_admitted') is False
        if 'herdr_executed' in doc:
            assert doc.get('herdr_executed') is False
        if 'herdrm_copied' in doc:
            assert doc.get('herdrm_copied') is False
        if 'project_license_selected' in doc:
            assert doc.get('project_license_selected') is False
        if 'nuget_scan_executed' in doc:
            assert doc.get('nuget_scan_executed') is False
        if 'cargo_advisory_executed' in doc:
            assert doc.get('cargo_advisory_executed') is False
        if 'npm_audit_executed' in doc:
            assert doc.get('npm_audit_executed') is False
        if 'live_renderer_process_observed' in doc:
            assert doc.get('live_renderer_process_observed') is False
        if 'canary_export_executed' in doc:
            assert doc.get('canary_export_executed') is False
        if 'signed_package_unpacked' in doc:
            assert doc.get('signed_package_unpacked') is False
        if 'public_visibility_is_not_license_grant' in doc:
            assert doc.get('public_visibility_is_not_license_grant') is True
        if 'missing_scan_is_not_zero_vuln' in doc:
            assert doc.get('missing_scan_is_not_zero_vuln') is True
        if 'confirmed_exploitable_critical_high_must_not_be_hidden_by_exception' in doc:
            assert doc.get('confirmed_exploitable_critical_high_must_not_be_hidden_by_exception') is True
    for key in _HD035_FALSE_KEYS:
        if key in hd035:
            assert hd035.get(key) is False, key
        if key in catalog:
            assert catalog.get(key) is False, key
    for key in _HD035_TRUE_KEYS:
        assert hd035.get(key) is True, key
        assert catalog.get(key) is True, key
        assert matrix.get(key) is True, key
    assert tuple(catalog.get('templates') or ()) == _HD035_TEMPLATES
    assert tuple(catalog.get('not_run_captures') or ()) == _HD035_NOT_RUN
    missing = hd035.get('missing') or {}
    for key in (
        'maintainer_license_decision', 'herdrm_reverse_audit_of_release_inputs',
        'nuget_advisory_scan', 'cargo_advisory_scan', 'npm_audit',
        'webview_process_observation', 'canary_diagnostic_export',
        'signed_package_unpack', 'winui_shell', 'integration_windows',
    ):
        assert missing.get(key) is True, key
    grants = hd035.get('missing_grants') or []
    assert tuple(grants) == tuple(_HD035_CARD_GRANTS[card_id] for card_id in _HD035_CARD_IDS)
    assert hd035.get('catalog') == 'evidence/security-release/catalog.json'
    assert hd035.get('support_matrix') == 'evidence/security-release/support-matrix.json'
    assert hd035.get('inventory') == 'evidence/security-release/inventory.json'
    assert catalog.get('support_matrix') == 'evidence/security-release/support-matrix.json'
    assert catalog.get('inventory') == 'evidence/security-release/inventory.json'
    assert catalog.get('l2_status') == 'implementation/hd-035-l2.json'
    assert hd035.get('herdr_executed') is False
    assert catalog.get('herdr_executed') is False
    redaction = catalog.get('redaction') or {}
    for key in ('host', 'user', 'path', 'credential', 'scan_body', 'canary'):
        assert redaction.get(key) == 'omitted', key
    cards = {item['id']: item for item in catalog['execution_cards']}
    assert tuple(cards) == _HD035_CARD_IDS
    seen_acs = set()
    seen_grants = []
    for card in catalog['execution_cards']:
        card_id = card['id']
        assert card['live_status'] == 'UNVERIFIED'
        assert card['live_result'] == 'not_run'
        assert card['l1_status'] == 'shipped'
        assert card['l1_status'] != 'passed'
        assert card['required_evidence'] in {'L2', 'L3', 'L4'}
        assert card['required_evidence'] != 'L1'
        assert card['missing_grant'] == _HD035_CARD_GRANTS[card_id]
        assert card['owner_children'] == _HD035_CARD_OWNERS[card_id]
        assert tuple(card['ac_ids']) == _HD035_CARD_ACS[card_id]
        seen_acs.update(card['ac_ids'])
        seen_grants.append(card['missing_grant'])
        assert card['l1_artifacts']
        for rel in card['l1_artifacts']:
            assert (ROOT / rel).is_file(), rel
        capture = ROOT / card['live_capture']
        assert capture.is_file()
        loaded = json.loads(capture.read_text(encoding='utf-8'))
        _reject_hd035_pass_claims(loaded)
        _reject_hd035_invented_identity(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        assert not _is_hd035_success(loaded.get('result'))
        assert loaded.get('herdrm_copied') is not True
        assert loaded.get('nuget_scan_executed') is not True
        assert loaded.get('signed_package_unpacked') is not True
        assert loaded.get('live_renderer_process_observed') is not True
        assert loaded.get('canary_export_executed') is not True
        assert loaded.get('project_license_selected') is not True
    assert _HD035_REQUIRED_ACS <= seen_acs
    assert len(seen_grants) == len(set(seen_grants))
    rows = {item['id']: item for item in catalog['live_rows']}
    assert tuple(rows) == _HD035_LIVE_IDS
    for row in catalog['live_rows']:
        assert row['status'] == 'UNVERIFIED'
        assert row['result'] == 'not_run'
        assert row.get('template') is False
        assert row.get('owner_children')
        assert row['missing_grant'] == _HD035_LIVE_GRANTS[row['id']]
        evidence = ROOT / row['evidence_path']
        assert evidence.is_file()
        loaded = json.loads(evidence.read_text(encoding='utf-8'))
        _reject_hd035_pass_claims(loaded)
        _reject_hd035_invented_identity(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        if row['id'] == 'live-nuget-scan':
            assert loaded.get('nuget_scan_executed') is False
            assert loaded.get('nuget_lock_present') is True
            assert loaded.get('missing_scan_is_not_zero_vuln') is True
        if row['id'] == 'live-cargo-scan':
            assert loaded.get('cargo_advisory_executed') is False
            assert loaded.get('cargo_lock_present') is True
            assert loaded.get('missing_scan_is_not_zero_vuln') is True
        if row['id'] == 'live-npm-scan':
            assert loaded.get('npm_audit_executed') is False
            assert loaded.get('npm_lock_present') is True
        if row['id'] == 'live-renderer-boundary':
            assert loaded.get('live_renderer_process_observed') is False
        if row['id'] == 'live-diagnostic-canary':
            assert loaded.get('canary_export_executed') is False
            assert loaded.get('canary_present_in_export') is None
        if row['id'] == 'live-signed-package-reverse-audit':
            assert loaded.get('signed_package_unpacked') is False
            assert loaded.get('signed_msix_built') is False
            assert loaded.get('final_unpacked_msix') is False
        if row['id'] == 'live-license-inventory':
            assert loaded.get('project_license_selected') is False
            assert loaded.get('public_visibility_is_not_license_grant') is True
        if row['id'] == 'live-herdrm-not-copied':
            assert loaded.get('herdrm_copied') is False
    for rel in _HD035_TEMPLATES:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd035_pass_claims(doc)
        _reject_hd035_invented_identity(doc)
        assert doc.get('template') is True
        assert doc.get('document_kind') == 'template'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('captured_at_utc') is None
        assert doc.get('scan_tool_version') is None
        assert doc.get('advisory_database_date') is None
        assert not _is_hd035_success(doc.get('result'))
        assert doc.get('herdr_executed') is False
        assert 'stdout' not in doc and 'stderr' not in doc
        if 'nuget-scan' in rel:
            assert doc.get('nuget_scan_executed') is False
            assert doc.get('nuget_lock_present') is True
            assert doc.get('missing_scan_is_not_zero_vuln') is True
        if 'cargo-scan' in rel:
            assert doc.get('cargo_advisory_executed') is False
            assert doc.get('cargo_lock_present') is True
        if 'npm-scan' in rel:
            assert doc.get('npm_audit_executed') is False
            assert doc.get('npm_lock_present') is True
        if 'renderer-boundary' in rel:
            assert doc.get('live_renderer_process_observed') is False
        if 'diagnostic-canary' in rel:
            assert doc.get('canary_export_executed') is False
            assert doc.get('canary_present_in_export') is None
        if 'signed-package-reverse-audit' in rel:
            assert doc.get('signed_package_unpacked') is False
            assert doc.get('signed_msix_built') is False
        if 'license-inventory' in rel:
            assert doc.get('project_license_selected') is False
            assert doc.get('public_visibility_is_not_license_grant') is True
        if 'herdrm-not-copied' in rel:
            assert doc.get('herdrm_copied') is False
    for rel in _HD035_NOT_RUN:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd035_pass_claims(doc)
        _reject_hd035_invented_identity(doc)
        assert doc.get('template') is False
        assert doc.get('result') == 'not_run'
        assert doc.get('herdr_executed') is False
        assert doc.get('evidence_level') == 'not_run'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('host_fingerprint_redacted') is None
        assert doc.get('command_redacted') is None
        assert 'stdout' not in doc and 'stderr' not in doc
        blob = json.dumps(doc).lower()
        assert 'password' not in blob
        assert 'private_key' not in blob
        assert '.pfx' not in blob
    promised = {item['id'] for item in matrix['promised_range']}
    assert promised == {'windows-11-x64-client'}
    by_platform = {item['id']: item for item in matrix['platforms']}
    windows = by_platform['windows-11-x64-client']
    assert windows['promise'] == 'promised'
    assert windows['live_status'] == 'not_run'
    assert windows['live_result'] == 'not_run'
    assert windows['compatible'] is False
    assert windows['support'] == 'promised_not_run'
    linux = by_platform['linux-x64-remote']
    assert linux['promise'] == 'none'
    assert linux['support'] == 'unsupported'
    assert linux['support'] not in ('supported', 'stable', 'promised', 'promised_not_run', 'passed')
    assert linux['live_status'] == 'not_run'
    assert linux['compatible'] is False
    assert linux.get('linux_msix') is False
    assert linux.get('linux_renderer_observation') is False
    assert linux['role'] == 'remote'
    macos = by_platform['macos-x64']
    assert macos['support'] == 'unsupported'
    assert macos['live_status'] == 'not_run'
    assert macos['compatible'] is False
    for key in ('macos-arm64', 'linux-arm64', 'windows-arm64'):
        row = by_platform[key]
        assert row['support'] in ('unsupported', 'experimental')
        assert row['support'] not in ('supported', 'stable', 'promised')
        assert row['live_status'] == 'not_run'
        assert row['compatible'] is False
    assert windows['missing_grant'] != linux['missing_grant']
    scenario_ids = [item['id'] for item in matrix['scenarios']]
    assert tuple(scenario_ids) == _HD035_SCENARIOS
    for scenario in matrix['scenarios']:
        assert scenario['live_status'] == 'not_run'
        assert scenario['live_result'] == 'not_run'
        assert scenario['support'] == 'l1_only'
    cell_keys = {(item['platform'], item['scenario']) for item in matrix['cells']}
    for scenario in scenario_ids:
        assert ('windows-11-x64-client', scenario) in cell_keys
    for cell in matrix['cells']:
        assert cell['platform'] == 'windows-11-x64-client'
        assert cell['live_status'] == 'not_run'
        assert cell['live_result'] == 'not_run'
        assert cell['compatible'] is False
        assert not _is_hd035_success(cell['live_result'])
    assert matrix.get('copy_windows_fields_onto_linux') is False
    assert matrix.get('extrapolate_macos_arm64') is False
    assert matrix.get('linux_msix_client') is False
    assert matrix.get('linux_x64_is_not_windows_renderer_substitute') is True
    assert matrix.get('compatible_by_default') == []
    assert catalog.get('linux_msix_client') is False
    assert catalog.get('cargo_lock_present') is True
    assert catalog.get('nuget_lock_present') is True
    assert catalog.get('npm_lock_present') is True
    assert (ROOT / ADMITTED_LOCK_REL).is_file()
    _check_hd035_inventory(inventory)
    criteria = json.loads((ROOT / 'planning' / 'acceptance.json').read_text(encoding='utf-8'))['criteria']
    acs = {item['id']: item for item in criteria}
    assert acs['AC02']['status'] != 'passed'
    assert acs['AC43']['status'] != 'passed'
    assert acs['AC44']['status'] != 'passed'
    assert acs['AC02']['status'] == 'not_run'
    assert acs['AC43']['status'] == 'not_run'
    assert acs['AC44']['status'] == 'not_run'
    check_integration_windows_layout(ROOT)
    assert (ROOT / 'web' / 'terminal' / 'package-lock.json').is_file()
    assert not (ROOT / 'packages.lock.json').exists()
    assert (ROOT / 'bridge' / 'Cargo.lock').is_file()
    assert (ROOT / 'filebridge' / 'Cargo.lock').is_file()


_HD036_PASS_KEYS = (
    'ac39_passed', 'ac40_passed', 'ac45_passed', 'ac47_passed', 'ac48_passed',
    'g0_passed',
)
_HD036_REQUIRED_ACS = frozenset({'AC39', 'AC40', 'AC45', 'AC47', 'AC48'})
_HD036_CARD_IDS = (
    'user-guide', 'support-matrix', 'ac48-trace', 'ac39-clean-restore',
    'ac40-hosted-required-check', 'ac47-dep-graph', 'unpublished-candidate',
    'signed-hash-sbom',
)
_HD036_CARD_ACS = {
    'user-guide': ('AC45',),
    'support-matrix': ('AC45',),
    'ac48-trace': ('AC48',),
    'ac39-clean-restore': ('AC39',),
    'ac40-hosted-required-check': ('AC40',),
    'ac47-dep-graph': ('AC47',),
    'unpublished-candidate': ('AC48',),
    'signed-hash-sbom': ('AC45',),
}
_HD036_CARD_OWNERS = {
    'user-guide': ['HD-011', 'HD-016', 'HD-030', 'HD-031'],
    'support-matrix': ['HD-001', 'HD-026', 'HD-033', 'HD-034'],
    'ac48-trace': ['HD-026', 'HD-032', 'HD-033', 'HD-034', 'HD-035'],
    'ac39-clean-restore': ['HD-007'],
    'ac40-hosted-required-check': ['HD-007'],
    'ac47-dep-graph': ['HD-007'],
    'unpublished-candidate': ['HD-007'],
    'signed-hash-sbom': ['HD-034', 'HD-035'],
}
_HD036_CARD_GRANTS = {
    'user-guide': 'no_authorized_independent_user_walkthrough',
    'support-matrix': 'no_authorized_live_platform_matrix',
    'ac48-trace': 'no_authorized_final_candidate_sha_evidence_bind',
    'ac39-clean-restore': 'no_authorized_clean_machine_locked_restore',
    'ac40-hosted-required-check': 'no_authorized_github_required_check_on_head',
    'ac47-dep-graph': 'no_authorized_final_sha_module_graph_rerun',
    'unpublished-candidate': 'no_authorized_external_publish',
    'signed-hash-sbom': 'no_authorized_signed_msix_hash',
}
_HD036_LIVE_IDS = (
    'live-user-guide', 'live-support-matrix', 'live-ac48-trace',
    'live-ac39-clean-restore', 'live-ac40-hosted-required-check',
    'live-ac47-dep-graph', 'live-unpublished-candidate',
    'live-signed-hash-sbom',
)
_HD036_LIVE_GRANTS = {
    'live-user-guide': 'no_authorized_independent_user_walkthrough',
    'live-support-matrix': 'no_authorized_live_platform_matrix',
    'live-ac48-trace': 'no_authorized_final_candidate_sha_evidence_bind',
    'live-ac39-clean-restore': 'no_authorized_clean_machine_locked_restore',
    'live-ac40-hosted-required-check': 'no_authorized_github_required_check_on_head',
    'live-ac47-dep-graph': 'no_authorized_final_sha_module_graph_rerun',
    'live-unpublished-candidate': 'no_authorized_external_publish',
    'live-signed-hash-sbom': 'no_authorized_signed_msix_hash',
}
_HD036_L2_L3_L4_KEYS = (
    'l2_independent_user_walkthrough', 'l2_live_platform_matrix',
    'l2_final_candidate_sha_evidence_bind', 'l2_clean_machine_locked_restore',
    'l2_github_required_check_on_head', 'l2_final_sha_module_graph_rerun',
    'l2_external_publish', 'l3_signed_msix_hash', 'l4_complete_1_0_release',
)
_HD036_FALSE_KEYS = (
    'published', 'complete_1_0_claimed', 'independent_user_walkthrough_executed',
    'winui_admitted', 'integration_windows_project', 'integration_ssh_project',
    'herdr_executed', 'invented_package_hashes', 'invented_screenshots',
    'invented_winui_button_names', 'invented_signed_package_hashes',
    'invented_sbom', 'invented_github_required_check', 'screenshot_as_evidence',
    'copy_windows_fields_onto_linux', 'extrapolate_macos_arm64',
    'linux_msix_client', 'impersonates_1_x_extensions', 'signed_msix_built',
    'publisher_identity_confirmed', 'not_run_flipped_to_passed',
)
_HD036_TRUE_KEYS = (
    'markdown_link_is_not_test_evidence',
    'structural_validation_is_not_product_acceptance',
    'hosted_actions_on_older_sha_is_not_head_proof',
    'local_just_ci_is_not_hosted_bar',
    'trace_index_is_not_ac_pass_evidence',
    'linux_x64_is_not_windows_client_substitute',
    'core_1_0_does_not_impersonate_1_x_extensions',
    'unpublished_candidate_is_not_published',
)
_HD036_NULL_KEYS = (
    'package_sha256', 'msix_sha256', 'signed_package_sha256', 'sbom_sha256',
    'screenshot_sha256', 'screenshot_path', 'publisher', 'certificate_subject',
    'certificate_thumbprint', 'candidate_sha', 'hosted_check_run_id',
    'independent_reviewer',
)
_HD036_SUCCESS = frozenset({'passed', 'verified', 'compatible', 'success', 'ok', 'pass'})
_HD036_SUPPORT_PASS = frozenset({'supported', 'stable', 'passed', 'compatible', 'success'})
_HD036_TEMPLATES = (
    'evidence/releases/live-user-guide.template.json',
    'evidence/releases/live-support-matrix.template.json',
    'evidence/releases/live-ac48-trace.template.json',
    'evidence/releases/live-ac39-clean-restore.template.json',
    'evidence/releases/live-ac40-hosted-required-check.template.json',
    'evidence/releases/live-ac47-dep-graph.template.json',
    'evidence/releases/live-unpublished-candidate.template.json',
    'evidence/releases/live-signed-hash-sbom.template.json',
)
_HD036_NOT_RUN = (
    'evidence/releases/live-user-guide.not-run.json',
    'evidence/releases/live-support-matrix.not-run.json',
    'evidence/releases/live-ac48-trace.not-run.json',
    'evidence/releases/live-ac39-clean-restore.not-run.json',
    'evidence/releases/live-ac40-hosted-required-check.not-run.json',
    'evidence/releases/live-ac47-dep-graph.not-run.json',
    'evidence/releases/live-unpublished-candidate.not-run.json',
    'evidence/releases/live-signed-hash-sbom.not-run.json',
)
_HD036_SCENARIOS = (
    'user_guide', 'support_matrix', 'ac48_trace', 'ac39_clean_restore',
    'ac40_hosted_required_check', 'ac47_dep_graph', 'unpublished_candidate',
    'signed_hash_sbom',
)
_HD036_AC_IDS = tuple(f'AC{i:02d}' for i in range(1, 49))


def _hd036_token(value):
    return value.strip().lower() if isinstance(value, str) else value


def _is_hd036_success(value) -> bool:
    if value is True:
        return True
    return _hd036_token(value) in _HD036_SUCCESS


def _reject_hd036_invented_identity(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key in _HD036_NULL_KEYS and value is not None:
                raise AssertionError(
                    f'{key} must stay null; do not invent hashes, screenshots, Publisher, or candidate SHA'
                )
            _reject_hd036_invented_identity(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd036_invented_identity(item)


def _reject_hd036_pass_claims(doc) -> None:
    if isinstance(doc, dict):
        for key, value in doc.items():
            if key.endswith('_passed') and value is not False:
                raise AssertionError(f'{key} must stay false')
            if key == 'phase_gate' and _hd036_token(value) in {'passed', 'pass', 'ok'}:
                raise AssertionError('phase_gate must stay not_passed')
            if key in _HD036_L2_L3_L4_KEYS and value != 'UNVERIFIED':
                raise AssertionError(f'{key} must stay UNVERIFIED')
            if key in {'live_result', 'result', 'live_status', 'status'} and _is_hd036_success(value):
                raise AssertionError(f'{key} must stay not_run or UNVERIFIED')
            if key == 'support' and _hd036_token(value) in _HD036_SUPPORT_PASS:
                raise AssertionError('support must not be a pass token')
            if key == 'compatible' and value is True:
                raise AssertionError('compatible must stay false')
            if key in _HD036_FALSE_KEYS and value is True:
                raise AssertionError(f'{key} must stay false')
            if key in _HD036_TRUE_KEYS and value is not True:
                raise AssertionError(f'{key} must stay true')
            if key == 'l1_status' and _hd036_token(value) in _HD036_SUCCESS:
                raise AssertionError('l1_status must not be a pass token')
            if key == 'github_required_check' and _is_hd036_success(value):
                raise AssertionError('github_required_check must stay UNVERIFIED')
            if key == 'windows_desktop_restore' and _hd036_token(value) in {
                'passed', 'ok', 'success', 'verified',
            }:
                raise AssertionError('windows_desktop_restore must stay not_admitted')
            if key == 'windows_desktop_restore' and _hd036_token(value) == 'admitted':
                raise AssertionError(
                    'windows_desktop_restore=admitted is allowed only in implementation/hd-007-packages.json'
                )
            if doc.get('published') is True:
                raise AssertionError('unpublished candidate cannot be published')
            if doc.get('complete_1_0_claimed') is True:
                raise AssertionError('complete 1.0 cannot be claimed')
            if doc.get('screenshot_as_evidence') is True:
                raise AssertionError('screenshot is not evidence')
            if doc.get('invented_github_required_check') is True:
                raise AssertionError('invented hosted required-check cannot pass as success')
            if doc.get('integration_windows_project') is True:
                raise AssertionError(
                    'integration_windows_project catalog token is not AC pass'
                )
            if doc.get('impersonates_1_x_extensions') is True:
                raise AssertionError('core 1.0 must not impersonate EP-01..EP-07')
            _reject_hd036_pass_claims(value)
    elif isinstance(doc, list):
        for item in doc:
            _reject_hd036_pass_claims(item)


def _check_hd036_ac_index(index: dict) -> None:
    assert index.get('document_kind') == 'hd036_ac_index'
    assert index.get('simulation') is True
    assert index.get('fixture_origin') == 'synthetic'
    assert index.get('template') is False
    assert index.get('planning_status_source') == 'planning/acceptance.json'
    assert index.get('published') is False
    assert index.get('complete_1_0_claimed') is False
    assert index.get('trace_index_is_not_ac_pass_evidence') is True
    for key in _HD036_PASS_KEYS:
        assert index.get(key) is False, key
    assert index.get('phase_gate') != 'passed'
    _reject_hd036_pass_claims(index)
    _reject_hd036_invented_identity(index)
    planning = json.loads((ROOT / 'planning' / 'acceptance.json').read_text(encoding='utf-8'))
    planning_acs = {item['id']: item for item in planning['criteria']}
    entries = index.get('entries') or []
    ids = tuple(item['id'] for item in entries)
    assert ids == _HD036_AC_IDS
    assert len(entries) == 48
    for entry in entries:
        ac_id = entry['id']
        assert entry['status'] == planning_acs[ac_id]['status']
        assert entry['status'] == 'not_run'
        assert entry['status'] != 'passed'
        assert entry['live_status'] == 'UNVERIFIED'
        assert entry['live_result'] == 'not_run'
        assert not _is_hd036_success(entry['live_result'])
        assert entry.get('owner_children')
        assert entry.get('evidence_class')
        assert entry.get('live_residual')
        assert entry.get('candidate_sha') is None
        assert entry.get('product_hash') is None
        assert entry.get('independent_reviewer') is None
        assert entry.get('l1_artifacts')
        for rel in entry['l1_artifacts']:
            assert (ROOT / rel).is_file(), rel
        if entry.get('catalog'):
            assert (ROOT / entry['catalog']).is_file(), entry['catalog']
        if entry.get('l2_status_file'):
            assert (ROOT / entry['l2_status_file']).is_file(), entry['l2_status_file']
        for owner in entry['owner_children']:
            assert (ROOT / 'tasks' / f'{owner}.md').is_file(), owner


def _check_hd036_closeout(hd036: dict, catalog: dict, matrix: dict, index: dict) -> None:
    assert hd036.get('document_kind') == 'hd036_l2_status'
    assert catalog.get('document_kind') == 'hd036_release_docs_catalog'
    assert matrix.get('document_kind') == 'hd036_support_matrix'
    assert catalog.get('simulation') is True
    assert catalog.get('fixture_origin') == 'synthetic'
    assert catalog.get('template') is False
    for key in _HD036_L2_L3_L4_KEYS:
        assert hd036.get(key) == 'UNVERIFIED', key
        if key in catalog:
            assert catalog.get(key) == 'UNVERIFIED', key
        if key in matrix:
            assert matrix.get(key) == 'UNVERIFIED', key
    for doc in (hd036, catalog, matrix):
        _reject_hd036_pass_claims(doc)
        _reject_hd036_invented_identity(doc)
        for key in _HD036_PASS_KEYS:
            assert doc.get(key) is False, key
        assert doc.get('phase_gate') != 'passed'
        assert doc.get('published') is False
        assert doc.get('complete_1_0_claimed') is False
        if 'winui_admitted' in doc:
            assert doc.get('winui_admitted') is False
        if 'herdr_executed' in doc:
            assert doc.get('herdr_executed') is False
        if 'independent_user_walkthrough_executed' in doc:
            assert doc.get('independent_user_walkthrough_executed') is False
        if 'screenshot_as_evidence' in doc:
            assert doc.get('screenshot_as_evidence') is False
        if 'github_required_check' in doc:
            assert doc.get('github_required_check') == 'UNVERIFIED'
        if 'windows_desktop_restore' in doc:
            assert doc.get('windows_desktop_restore') == 'not_admitted'
    for key in _HD036_FALSE_KEYS:
        if key in hd036:
            assert hd036.get(key) is False, key
        if key in catalog:
            assert catalog.get(key) is False, key
    for key in _HD036_TRUE_KEYS:
        assert hd036.get(key) is True, key
        assert catalog.get(key) is True, key
        assert matrix.get(key) is True, key
    assert tuple(catalog.get('templates') or ()) == _HD036_TEMPLATES
    assert tuple(catalog.get('not_run_captures') or ()) == _HD036_NOT_RUN
    missing = hd036.get('missing') or {}
    for key in (
        'independent_user_walkthrough', 'live_platform_matrix',
        'final_candidate_sha_evidence_bind', 'clean_machine_locked_restore',
        'github_required_check_on_head', 'final_sha_module_graph_rerun',
        'external_publish', 'signed_msix_hash', 'winui_shell',
        'integration_windows',
    ):
        assert missing.get(key) is True, key
    grants = hd036.get('missing_grants') or []
    assert tuple(grants) == tuple(_HD036_CARD_GRANTS[card_id] for card_id in _HD036_CARD_IDS)
    assert hd036.get('catalog') == 'evidence/releases/catalog.json'
    assert hd036.get('support_matrix') == 'evidence/releases/support-matrix.json'
    assert hd036.get('ac_index') == 'evidence/releases/ac-index.json'
    assert hd036.get('hosted_workflow_pointer') == 'evidence/releases/hosted-workflow-pointer.json'
    assert catalog.get('support_matrix') == 'evidence/releases/support-matrix.json'
    assert catalog.get('ac_index') == 'evidence/releases/ac-index.json'
    assert catalog.get('l2_status') == 'implementation/hd-036-l2.json'
    assert catalog.get('hosted_workflow_pointer') == 'evidence/releases/hosted-workflow-pointer.json'
    pointer_path = ROOT / 'evidence' / 'releases' / 'hosted-workflow-pointer.json'
    assert pointer_path.is_file()
    pointer = json.loads(pointer_path.read_text(encoding='utf-8'))
    assert pointer.get('document_kind') == 'hd036_hosted_workflow_pointer'
    _reject_hd036_pass_claims(pointer)
    _reject_hd036_invented_identity(pointer)
    assert pointer.get('result') == 'not_run'
    assert not _is_hd036_success(pointer.get('result'))
    assert pointer.get('github_required_check') == 'UNVERIFIED'
    assert pointer.get('hosted_workflow_is_not_required_check_ruleset') is True
    assert pointer.get('published') is False
    assert pointer.get('complete_1_0_claimed') is False
    assert pointer.get('ac40_passed') is False
    assert pointer.get('candidate_sha') is None
    assert pointer.get('hosted_check_run_id') is None
    assert hd036.get('herdr_executed') is False
    assert catalog.get('herdr_executed') is False
    redaction = catalog.get('redaction') or {}
    for key in ('host', 'user', 'path', 'credential', 'screenshot', 'publish_token'):
        assert redaction.get(key) == 'omitted', key
    cards = {item['id']: item for item in catalog['execution_cards']}
    assert tuple(cards) == _HD036_CARD_IDS
    seen_acs = set()
    seen_grants = []
    for card in catalog['execution_cards']:
        card_id = card['id']
        assert card['live_status'] == 'UNVERIFIED'
        assert card['live_result'] == 'not_run'
        assert card['l1_status'] == 'shipped'
        assert card['l1_status'] != 'passed'
        assert card['required_evidence'] in {'L2', 'L3', 'L4'}
        assert card['required_evidence'] != 'L1'
        assert card['missing_grant'] == _HD036_CARD_GRANTS[card_id]
        assert card['owner_children'] == _HD036_CARD_OWNERS[card_id]
        assert tuple(card['ac_ids']) == _HD036_CARD_ACS[card_id]
        seen_acs.update(card['ac_ids'])
        seen_grants.append(card['missing_grant'])
        assert card['l1_artifacts']
        for rel in card['l1_artifacts']:
            assert (ROOT / rel).is_file(), rel
        capture = ROOT / card['live_capture']
        assert capture.is_file()
        loaded = json.loads(capture.read_text(encoding='utf-8'))
        _reject_hd036_pass_claims(loaded)
        _reject_hd036_invented_identity(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        assert not _is_hd036_success(loaded.get('result'))
        assert loaded.get('published') is not True
        assert loaded.get('complete_1_0_claimed') is not True
        assert loaded.get('screenshot_as_evidence') is not True
        assert loaded.get('github_required_check') == 'UNVERIFIED'
    assert _HD036_REQUIRED_ACS <= seen_acs
    assert len(seen_grants) == len(set(seen_grants))
    rows = {item['id']: item for item in catalog['live_rows']}
    assert tuple(rows) == _HD036_LIVE_IDS
    for row in catalog['live_rows']:
        assert row['status'] == 'UNVERIFIED'
        assert row['result'] == 'not_run'
        assert row.get('template') is False
        assert row.get('owner_children')
        assert row['missing_grant'] == _HD036_LIVE_GRANTS[row['id']]
        evidence = ROOT / row['evidence_path']
        assert evidence.is_file()
        loaded = json.loads(evidence.read_text(encoding='utf-8'))
        _reject_hd036_pass_claims(loaded)
        _reject_hd036_invented_identity(loaded)
        assert loaded.get('template') is not True
        assert loaded.get('result') == 'not_run'
        if row['id'] == 'live-user-guide':
            assert loaded.get('independent_user_walkthrough_executed') is False
            assert loaded.get('screenshot_as_evidence') is False
        if row['id'] == 'live-ac40-hosted-required-check':
            assert loaded.get('github_required_check') == 'UNVERIFIED'
            assert loaded.get('invented_github_required_check') is False
        if row['id'] == 'live-ac39-clean-restore':
            assert loaded.get('windows_desktop_restore') == 'not_admitted'
        if row['id'] == 'live-unpublished-candidate':
            assert loaded.get('published') is False
        if row['id'] == 'live-signed-hash-sbom':
            assert loaded.get('signed_msix_built') is False
            assert loaded.get('publisher_identity_confirmed') is False
        if row['id'] == 'live-ac47-dep-graph':
            assert loaded.get('l1_graph_check_exists') is True
            assert loaded.get('final_sha_module_graph_rerun') is False
    for rel in _HD036_TEMPLATES:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd036_pass_claims(doc)
        _reject_hd036_invented_identity(doc)
        assert doc.get('template') is True
        assert doc.get('document_kind') == 'template'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('captured_at_utc') is None
        assert not _is_hd036_success(doc.get('result'))
        assert doc.get('herdr_executed') is False
        assert doc.get('published') is False
        assert 'stdout' not in doc and 'stderr' not in doc
        if 'user-guide' in rel:
            assert doc.get('independent_user_walkthrough_executed') is False
            assert doc.get('screenshot_as_evidence') is False
        if 'ac40-hosted-required-check' in rel:
            assert doc.get('github_required_check') == 'UNVERIFIED'
        if 'unpublished-candidate' in rel:
            assert doc.get('published') is False
        if 'signed-hash-sbom' in rel:
            assert doc.get('signed_msix_built') is False
        if 'ac47-dep-graph' in rel:
            assert doc.get('l1_graph_check_exists') is True
            assert doc.get('final_sha_module_graph_rerun') is False
    for rel in _HD036_NOT_RUN:
        path = ROOT / rel
        assert path.is_file(), rel
        doc = json.loads(path.read_text(encoding='utf-8'))
        _reject_hd036_pass_claims(doc)
        _reject_hd036_invented_identity(doc)
        assert doc.get('template') is False
        assert doc.get('result') == 'not_run'
        assert doc.get('herdr_executed') is False
        assert doc.get('evidence_level') == 'not_run'
        assert doc.get('exit_code') is None
        assert doc.get('stdout_sha256') is None
        assert doc.get('stderr_sha256') is None
        assert doc.get('host_fingerprint_redacted') is None
        assert doc.get('command_redacted') is None
        assert 'stdout' not in doc and 'stderr' not in doc
        blob = json.dumps(doc).lower()
        assert 'password' not in blob
        assert 'private_key' not in blob
        assert '.pfx' not in blob
    promised = {item['id'] for item in matrix['promised_range']}
    assert promised == {'windows-11-x64-client', 'linux-x64-remote'}
    by_platform = {item['id']: item for item in matrix['platforms']}
    for key in ('windows-11-x64-client', 'linux-x64-remote'):
        row = by_platform[key]
        assert row['promise'] == 'promised'
        assert row['live_status'] == 'not_run'
        assert row['live_result'] == 'not_run'
        assert row['compatible'] is False
        assert row['support'] == 'promised_not_run'
    linux = by_platform['linux-x64-remote']
    windows = by_platform['windows-11-x64-client']
    assert linux['role'] == 'remote'
    assert linux.get('linux_msix') is False
    macos = by_platform['macos-x64']
    assert macos['support'] == 'unsupported'
    assert macos['live_status'] == 'not_run'
    assert macos['compatible'] is False
    for key in ('macos-arm64', 'linux-arm64', 'windows-arm64'):
        row = by_platform[key]
        assert row['support'] in ('unsupported', 'experimental')
        assert row['support'] not in ('supported', 'stable', 'promised')
        assert row['live_status'] == 'not_run'
        assert row['compatible'] is False
    assert windows['missing_grant'] != linux['missing_grant']
    scenario_ids = [item['id'] for item in matrix['scenarios']]
    assert tuple(scenario_ids) == _HD036_SCENARIOS
    for scenario in matrix['scenarios']:
        assert scenario['live_status'] == 'not_run'
        assert scenario['live_result'] == 'not_run'
        assert scenario['support'] == 'l1_only'
    cell_keys = {(item['platform'], item['scenario']) for item in matrix['cells']}
    for platform in ('windows-11-x64-client', 'linux-x64-remote'):
        for scenario in scenario_ids:
            assert (platform, scenario) in cell_keys
    for cell in matrix['cells']:
        assert cell['live_status'] == 'not_run'
        assert cell['live_result'] == 'not_run'
        assert cell['compatible'] is False
        assert not _is_hd036_success(cell['live_result'])
    assert matrix.get('copy_windows_fields_onto_linux') is False
    assert matrix.get('extrapolate_macos_arm64') is False
    assert matrix.get('linux_msix_client') is False
    assert matrix.get('linux_x64_is_not_windows_client_substitute') is True
    assert matrix.get('core_1_0_does_not_impersonate_1_x_extensions') is True
    assert matrix.get('compatible_by_default') == []
    assert tuple(matrix.get('extensions_out_of_core_1_0') or ()) == (
        'EP-01', 'EP-02', 'EP-03', 'EP-04', 'EP-05', 'EP-06', 'EP-07',
    )
    assert catalog.get('linux_msix_client') is False
    _check_hd036_ac_index(index)
    criteria = json.loads((ROOT / 'planning' / 'acceptance.json').read_text(encoding='utf-8'))['criteria']
    acs = {item['id']: item for item in criteria}
    for ac_id in ('AC39', 'AC40', 'AC45', 'AC47', 'AC48'):
        assert acs[ac_id]['status'] != 'passed'
        assert acs[ac_id]['status'] == 'not_run'
    assert acs['AC39']['status'] == 'not_run'
    assert acs['AC40']['status'] == 'not_run'
    assert acs['AC45']['status'] == 'not_run'
    assert acs['AC47']['status'] == 'not_run'
    assert acs['AC48']['status'] == 'not_run'
    check_integration_windows_layout(ROOT)
    for rel in (
        'docs/user-guide/index.md',
        'docs/user-guide/shell-not-admitted.md',
        'docs/release/notes.md',
        'docs/release/support-matrix.md',
        'docs/testing/release-checklist.md',
        'scripts/bind_release_candidate.py',
        'evidence/releases/hosted-workflow-pointer.json',
    ):
        assert (ROOT / rel).is_file(), rel


def _check_hd036_release_candidate_bind() -> None:
    """Invoke shipped binder. Do not reimplement SHA or required-check checks."""
    assert (ROOT / 'scripts' / 'bind_release_candidate.py').is_file()
    report = bind_release_candidate(ROOT)
    assert report.get('document_kind') == 'hd036_hosted_workflow_pointer'
    assert report.get('bound_sha') == 'd8347522d80ccbf190623f688ab1abe602a7b0f8'
    assert report.get('hosted_workflow_run_id') == '34549580783'
    assert report.get('hosted_workflow_url') == (
        'https://github.com/bahayonghang/HerdrDesk/actions/runs/34549580783'
    )
    assert report.get('hosted_workflow_conclusion') == 'success'
    assert report.get('github_required_check') == 'UNVERIFIED'
    assert not _is_hd036_success(report.get('github_required_check'))
    assert report.get('hosted_workflow_is_not_required_check_ruleset') is True
    assert report.get('hosted_actions_on_older_sha_is_not_head_proof') is True
    assert report.get('local_just_ci_is_not_hosted_bar') is True
    assert report.get('published') is False
    assert report.get('complete_1_0_claimed') is False
    assert report.get('ac39_passed') is False
    assert report.get('ac40_passed') is False
    assert report.get('ac45_passed') is False
    assert report.get('ac47_passed') is False
    assert report.get('ac48_passed') is False
    assert report.get('g0_passed') is False
    assert report.get('phase_gate') != 'passed'
    assert report.get('invented_github_required_check') is False
    assert report.get('invented_package_hashes') is False
    assert report.get('invented_sbom') is False
    assert report.get('independent_user_walkthrough_executed') is False
    assert report.get('signed_package_unpacked') is False
    assert report.get('package_sha256') is None
    assert report.get('msix_sha256') is None
    assert report.get('sbom_sha256') is None
    assert report.get('publisher') is None
    assert report.get('candidate_sha') is None
    assert report.get('hosted_check_run_id') is None
    assert report.get('result') == 'not_run'
    assert 'git_head' in report
    assert 'head_equals_bound_sha' in report
    git_head = report.get('git_head')
    assert report.get('head_equals_bound_sha') is bool(
        git_head and git_head == report.get('bound_sha')
    )
    if git_head != report.get('bound_sha'):
        assert report.get('ac40_passed') is False
        assert report.get('github_required_check') == 'UNVERIFIED'
        assert report.get('complete_1_0_claimed') is False
    else:
        assert report.get('head_equals_bound_sha') is True
        assert report.get('ac40_passed') is False
        assert report.get('github_required_check') == 'UNVERIFIED'
        assert report.get('complete_1_0_claimed') is False
        assert report.get('published') is False


def _check_hd007_package_admission(packages: dict) -> None:
    restore = packages.get('windows_desktop_restore')
    assert packages.get('github_required_check') == 'UNVERIFIED'
    assert packages.get('ac39_passed') is not True
    assert packages.get('ac40_passed') is not True
    assert packages.get('ac47_passed') is not True
    assert packages.get('phase_gate') != 'passed'
    assert packages.get('g0_passed') is not True
    if restore == 'admitted':
        assert (ROOT / 'Directory.Packages.props').is_file()
        assert (ROOT / ADMITTED_LOCK_REL).is_file()
        assert packages.get('directory_packages_props') is True
        plan = desktop_gate.plan_desktop_gate(ROOT)
        assert plan.action == 'run'
        assert plan.commands
        gate_src = (ROOT / 'scripts' / 'run_windows_desktop_gate.py').read_text(encoding='utf-8')
        assert 'Admitted desktop restore is not implemented' not in gate_src
        return
    assert restore == 'not_admitted'


def validate() -> dict:
    files=list(ROOT.rglob('*.json'))
    count=0
    for path in files:
        if any(part in {'.git','obj','bin','target','probe-results','.test-results','node_modules','artifacts'} for part in path.parts):continue
        json.loads(path.read_text(encoding='utf-8'));count+=1
    graph=validate_project_graph(ROOT)
    projects=list((ROOT/'src').rglob('*.csproj'))+list((ROOT/'tests').rglob('*.csproj'))
    packages=json.loads((ROOT/'implementation/hd-007-packages.json').read_text(encoding='utf-8'))
    hd011=json.loads((ROOT/'implementation/hd-011-l2.json').read_text(encoding='utf-8'))
    hd012=json.loads((ROOT/'implementation/hd-012-l2.json').read_text(encoding='utf-8'))
    hd013=json.loads((ROOT/'implementation/hd-013-l2.json').read_text(encoding='utf-8'))
    hd014=json.loads((ROOT/'implementation/hd-014-l2.json').read_text(encoding='utf-8'))
    hd015=json.loads((ROOT/'implementation/hd-015-l3.json').read_text(encoding='utf-8'))
    hd016=json.loads((ROOT/'implementation/hd-016-l2.json').read_text(encoding='utf-8'))
    hd017=json.loads((ROOT/'implementation/hd-017-l2.json').read_text(encoding='utf-8'))
    hd018=json.loads((ROOT/'implementation/hd-018-l2.json').read_text(encoding='utf-8'))
    hd019l2=json.loads((ROOT/'implementation/hd-019-l2.json').read_text(encoding='utf-8'))
    hd019l3=json.loads((ROOT/'implementation/hd-019-l3.json').read_text(encoding='utf-8'))
    hd020=json.loads((ROOT/'implementation/hd-020-l2.json').read_text(encoding='utf-8'))
    hd021=json.loads((ROOT/'implementation/hd-021-l2.json').read_text(encoding='utf-8'))
    hd022=json.loads((ROOT/'implementation/hd-022-l2.json').read_text(encoding='utf-8'))
    hd023=json.loads((ROOT/'implementation/hd-023-l2.json').read_text(encoding='utf-8'))
    hd024=json.loads((ROOT/'implementation/hd-024-l2.json').read_text(encoding='utf-8'))
    hd025=json.loads((ROOT/'implementation/hd-025-l2.json').read_text(encoding='utf-8'))
    hd026=json.loads((ROOT/'implementation/hd-026-l2.json').read_text(encoding='utf-8'))
    hd027=json.loads((ROOT/'implementation/hd-027-l2.json').read_text(encoding='utf-8'))
    hd027pkg=json.loads((ROOT/'implementation/hd-027-packages.json').read_text(encoding='utf-8'))
    hd028=json.loads((ROOT/'implementation/hd-028-l2.json').read_text(encoding='utf-8'))
    hd028pkg=json.loads((ROOT/'implementation/hd-028-packages.json').read_text(encoding='utf-8'))
    hd029=json.loads((ROOT/'implementation/hd-029-l2.json').read_text(encoding='utf-8'))
    hd030=json.loads((ROOT/'implementation/hd-030-l2.json').read_text(encoding='utf-8'))
    hd031=json.loads((ROOT/'implementation/hd-031-l2.json').read_text(encoding='utf-8'))
    hd032=json.loads((ROOT/'implementation/hd-032-l2.json').read_text(encoding='utf-8'))
    hd033=json.loads((ROOT/'implementation/hd-033-l2.json').read_text(encoding='utf-8'))
    hd034=json.loads((ROOT/'implementation/hd-034-l2.json').read_text(encoding='utf-8'))
    hd035=json.loads((ROOT/'implementation/hd-035-l2.json').read_text(encoding='utf-8'))
    hd036=json.loads((ROOT/'implementation/hd-036-l2.json').read_text(encoding='utf-8'))
    catalog=json.loads((ROOT/'evidence/local-mvp/catalog.json').read_text(encoding='utf-8'))
    mvp=json.loads((ROOT/'evidence/multi-device-mvp/catalog.json').read_text(encoding='utf-8'))
    matrix=json.loads((ROOT/'evidence/multi-device-mvp/support-matrix.json').read_text(encoding='utf-8'))
    files_catalog=json.loads((ROOT/'evidence/files/catalog.json').read_text(encoding='utf-8'))
    files_matrix=json.loads((ROOT/'evidence/files/support-matrix.json').read_text(encoding='utf-8'))
    quality_catalog=json.loads((ROOT/'evidence/quality/catalog.json').read_text(encoding='utf-8'))
    quality_matrix=json.loads((ROOT/'evidence/quality/support-matrix.json').read_text(encoding='utf-8'))
    packaging_catalog=json.loads((ROOT/'evidence/packaging/catalog.json').read_text(encoding='utf-8'))
    packaging_matrix=json.loads((ROOT/'evidence/packaging/support-matrix.json').read_text(encoding='utf-8'))
    security_catalog=json.loads((ROOT/'evidence/security-release/catalog.json').read_text(encoding='utf-8'))
    security_matrix=json.loads((ROOT/'evidence/security-release/support-matrix.json').read_text(encoding='utf-8'))
    security_inventory=json.loads((ROOT/'evidence/security-release/inventory.json').read_text(encoding='utf-8'))
    release_catalog=json.loads((ROOT/'evidence/releases/catalog.json').read_text(encoding='utf-8'))
    release_matrix=json.loads((ROOT/'evidence/releases/support-matrix.json').read_text(encoding='utf-8'))
    release_index=json.loads((ROOT/'evidence/releases/ac-index.json').read_text(encoding='utf-8'))
    _check_hd007_package_admission(packages)
    assert packages['github_required_check']=='UNVERIFIED'
    assert packages.get('ac39_passed') is not True
    assert packages.get('ac40_passed') is not True
    assert packages.get('ac47_passed') is not True
    assert packages.get('phase_gate')!='passed'
    assert hd011.get('l2_windows_visual_activation')=='UNVERIFIED'
    assert hd011.get('l3_ime_screen_reader_dpi')=='UNVERIFIED'
    assert hd011.get('ac19_passed') is not True
    assert hd011.get('g0_passed') is not True
    assert hd011.get('winui_admitted') is not True
    assert hd011.get('phase_gate')!='passed'
    assert hd012.get('l2_windows_toast_activation')=='UNVERIFIED'
    assert hd012.get('ac17_passed') is not True
    assert hd012.get('ac18_passed') is not True
    assert hd012.get('g0_passed') is not True
    assert hd012.get('winui_admitted') is not True
    assert hd012.get('phase_gate')!='passed'
    assert hd013.get('l2_live_herdr_terminal_session')=='UNVERIFIED'
    assert hd013.get('ac05_passed') is not True
    assert hd013.get('ac06_passed') is not True
    assert hd013.get('g0_passed') is not True
    assert hd013.get('phase_gate')!='passed'
    assert hd014.get('l2_webview_process')=='UNVERIFIED'
    assert hd014.get('l3_dpi_theme_focus')=='UNVERIFIED'
    assert hd014.get('ac08_passed') is not True
    assert hd014.get('ac27_passed') is not True
    assert hd014.get('g0_passed') is not True
    assert hd014.get('webview2_admitted') is True
    assert hd014.get('npm_xterm_admitted') is True
    assert hd014.get('github_required_check')=='UNVERIFIED'
    assert hd014.get('phase_gate')!='passed'
    assert (ROOT/'web'/'terminal'/'package-lock.json').is_file()
    assert (ROOT/'web'/'terminal'/'dist'/'xterm.mjs').is_file()
    assert 'WebView2' in (ROOT/'src'/'HerdDesk.App'/'Controls'/'TerminalHost.xaml').read_text(encoding='utf-8')
    assert hd015.get('l3_ime_desktop')=='UNVERIFIED'
    assert hd015.get('l2_webview_ime')=='UNVERIFIED'
    assert hd015.get('ac09_passed') is not True
    assert hd015.get('ac10_passed') is not True
    assert hd015.get('g0_passed') is not True
    assert hd015.get('webview2_admitted') is True
    assert hd015.get('npm_xterm_admitted') is True
    assert hd015.get('winui_admitted') is not True
    assert hd015.get('github_required_check')=='UNVERIFIED'
    assert hd015.get('live_herdr') is not True
    assert hd015.get('phase_gate')!='passed'
    assert hd016.get('l2_live_lease')=='UNVERIFIED'
    assert hd016.get('ac07_passed') is not True
    assert hd016.get('ac14_passed') is not True
    assert hd016.get('ac16_passed') is not True
    assert hd016.get('g0_passed') is not True
    assert hd016.get('live_herdr') is not True
    assert hd016.get('phase_gate')!='passed'
    assert hd017.get('l2_live_mutation')=='UNVERIFIED'
    assert hd017.get('ac20_passed') is not True
    assert hd017.get('g0_passed') is not True
    assert hd017.get('live_herdr') is not True
    assert hd017.get('winui_admitted') is not True
    assert hd017.get('phase_gate')!='passed'
    assert hd018.get('l2_live_disconnect')=='UNVERIFIED'
    assert hd018.get('ac13_passed') is not True
    assert hd018.get('ac14_passed') is not True
    assert hd018.get('ac15_passed') is not True
    assert hd018.get('g0_passed') is not True
    assert hd018.get('live_herdr') is not True
    assert hd018.get('winui_admitted') is not True
    assert hd018.get('auto_start_daemon') is not True
    assert hd018.get('phase_gate')!='passed'
    assert hd019l2.get('l2_live_local_mvp')=='UNVERIFIED'
    assert hd019l3.get('l3_ime_desktop')=='UNVERIFIED'
    assert hd019l3.get('l3_agent_tui')=='UNVERIFIED'
    assert hd019l3.get('agent_tui_versions')=='UNVERIFIED'
    assert catalog.get('document_kind')=='hd019_local_mvp_catalog'
    for doc in (hd019l2, hd019l3, catalog):
        assert doc.get('ac06_passed') is not True
        assert doc.get('ac07_passed') is not True
        assert doc.get('ac10_passed') is not True
        assert doc.get('ac15_passed') is not True
        assert doc.get('g0_passed') is not True
        assert doc.get('live_herdr') is not True
        assert doc.get('phase_gate')!='passed'
        assert doc.get('winui_admitted') is not True
        assert doc.get('webview2_admitted') is not True
        assert doc.get('integration_windows_project') is not True
        missing=doc.get('missing') or {}
        assert missing.get('disposable_pane') is True
        assert missing.get('webview2') is True
        assert missing.get('ime_desktop') is True
        assert missing.get('agent_tui_versions') is True
    assert hd020.get('l2_isolated_windows_openssh')=='UNVERIFIED'
    assert hd020.get('ac22_passed') is not True
    assert hd020.get('ac23_passed') is not True
    assert hd020.get('g0_passed') is not True
    assert hd020.get('live_ssh') is not True
    assert hd020.get('herdr_machine_catalog') is not True
    assert hd020.get('endpoint_generation_1') is not True
    assert hd020.get('winui_admitted') is not True
    assert hd020.get('integration_ssh_project') is not True
    assert hd020.get('phase_gate')!='passed'
    assert hd021.get('l2_live_helper_deploy')=='UNVERIFIED'
    assert hd021.get('ac25_passed') is not True
    assert hd021.get('g0_passed') is not True
    assert hd021.get('live_ssh') is not True
    assert hd021.get('live_remote_install') is not True
    assert hd021.get('sudo') is not True
    assert hd021.get('winui_admitted') is not True
    assert hd021.get('integration_ssh_project') is not True
    assert hd021.get('phase_gate')!='passed'
    assert hd022.get('l2_live_ssh')=='UNVERIFIED'
    assert hd022.get('ac24_passed') is not True
    assert hd022.get('ac26_passed') is not True
    assert hd022.get('g0_passed') is not True
    assert hd022.get('live_ssh') is not True
    assert hd022.get('herdr_machine_catalog') is not True
    assert hd022.get('endpoint_generation_1') is not True
    assert hd022.get('winui_admitted') is not True
    assert hd022.get('integration_ssh_project') is not True
    assert hd022.get('tt_forced') is not True
    assert hd022.get('phase_gate')!='passed'
    assert hd023.get('l2_live_three_device_search_p95')=='UNVERIFIED'
    assert hd023.get('ac19_passed') is not True
    assert hd023.get('ac21_passed') is not True
    assert hd023.get('g0_passed') is not True
    assert hd023.get('live_three_device_ssh') is not True
    assert hd023.get('live_three_device_p95') is not True
    assert hd023.get('winui_admitted') is not True
    assert hd023.get('integration_windows_project') is not True
    assert hd023.get('phase_gate')!='passed'
    assert hd024.get('l2_live_auth')=='UNVERIFIED'
    assert hd024.get('ac22_passed') is not True
    assert hd024.get('ac26_passed') is not True
    assert hd024.get('g0_passed') is not True
    assert hd024.get('live_ssh') is not True
    assert hd024.get('winui_admitted') is not True
    assert hd024.get('integration_ssh_project') is not True
    assert hd024.get('phase_gate')!='passed'
    assert hd025.get('l2_live_ssh_perf')=='UNVERIFIED'
    assert hd025.get('l2_live_ssh')=='UNVERIFIED'
    assert hd025.get('ac27_passed') is not True
    assert hd025.get('g0_passed') is not True
    assert hd025.get('b_ssh_measured') is not True
    assert hd025.get('live_ssh') is not True
    assert hd025.get('winui_admitted') is not True
    assert hd025.get('integration_ssh_project') is not True
    assert hd025.get('integration_windows_project') is not True
    assert hd025.get('phase_gate')!='passed'
    _check_hd026_closeout(hd026, mvp, matrix)
    _check_hd027(hd027, hd027pkg)
    _check_hd028(hd028, hd028pkg)
    _check_hd029(hd029)
    _check_hd030(hd030)
    _check_hd031(hd031)
    _check_hd032_closeout(hd032, files_catalog, files_matrix)
    _check_hd033_closeout(hd033, quality_catalog, quality_matrix)
    _check_hd033_narrator_overlay()
    _check_hd033_narrator_product_ui_launch()
    _check_hd033_dpi_overlay()
    _check_hd033_theme_overlay()
    _check_hd033_dpi_matrix()
    _check_hd033_soak_start()
    _check_hd033_soak_working_set()
    _check_hd034_closeout(hd034, packaging_catalog, packaging_matrix)
    _check_hd034_package_script_contract()
    _check_hd034_lab_msix()
    _check_hd035_closeout(hd035, security_catalog, security_matrix, security_inventory)
    _check_hd035_release_input_audit()
    _check_hd036_closeout(hd036, release_catalog, release_matrix, release_index)
    _check_hd036_release_candidate_bind()
    assert not (ROOT/'tests/Integration.Ssh').exists()
    check_integration_windows_layout(ROOT)
    tasks=json.loads((ROOT/'planning/backlog.json').read_text(encoding='utf-8'))['tasks']
    by_id={task['id']:task for task in tasks}
    assert len(by_id)==len(tasks)==36,'Unexpected backlog IDs'
    visiting=set();visited=set()
    def visit(key):
        assert key not in visiting,'Dependency cycle'
        if key in visited:return
        visiting.add(key)
        for dep in by_id[key]['depends_on']:
            assert dep in by_id,'Unknown dependency';visit(dep)
        visiting.remove(key);visited.add(key)
    for key in by_id:visit(key)
    criteria=json.loads((ROOT/'planning/acceptance.json').read_text(encoding='utf-8'))['criteria']
    acs={c['id']:c for c in criteria}
    assert len(acs)==len(criteria)==48,'Unexpected acceptance IDs'
    for task in tasks:
        assert all(key in acs for key in task['acceptance_ids']),'Unknown acceptance ID'
        if task['status']=='completed':
            assert all(by_id[dep]['status']=='completed' for dep in task['depends_on']),'Incomplete dependency'
            for key in task['acceptance_ids']:
                assert acs[key]['status']=='passed' and acs[key].get('evidence'),'Missing acceptance evidence'
    assert (ROOT/'evidence/compatibility-baseline.json').is_file()
    baseline=json.loads((ROOT/'evidence/compatibility-baseline.json').read_text(encoding='utf-8'))
    assert baseline['herdr']['api_protocol']==20 and baseline['default_write_capability'] is False
    evidence=validate_evidence(ROOT)
    assert evidence['windows_verified'] is False
    endpoint=validate_endpoint_matrix(ROOT)
    assert endpoint['windows_verified'] is False
    assert endpoint['ac03_passed'] is False
    lease=validate_terminal_lease_matrix(ROOT)
    assert lease['windows_verified'] is False
    assert lease['ac05_passed'] is False
    renderer=validate_renderer_matrix(ROOT)
    assert renderer['windows_verified'] is False
    assert renderer['ac08_passed'] is False
    assert renderer['ac09_passed'] is False
    licensing=validate_licensing(ROOT)
    assert licensing['windows_verified'] is False
    assert licensing['ac02_passed'] is False
    adr=validate_adr_baseline(ROOT)
    assert adr['windows_verified'] is False
    assert adr['ac44_passed'] is False
    assert adr['g0_passed'] is False
    return {'structural_validation':'passed','json_files':count,'projects':len(projects),
            'tasks':len(tasks),'csharp_compiled':False,'windows_verified':False,
            'ac02_passed':False,'ac03_passed':False,'ac05_passed':False,
            'ac08_passed':False,'ac09_passed':False,'ac43_passed':False,
            'ac44_passed':False,
            'g0_passed':False,
            'project_graph':graph['project_graph'],
            'github_required_check':packages['github_required_check'],
            'windows_desktop_restore':packages['windows_desktop_restore'],
            'evidence_validation':evidence['evidence_validation'],
            'endpoint_validation':endpoint['endpoint_validation'],
            'lease_validation':lease['lease_validation'],
            'renderer_validation':renderer['renderer_validation'],
            'licensing_validation':licensing['licensing_validation'],
            'adr_validation':adr['adr_validation']}


if __name__=='__main__':
    try:print(json.dumps(validate(),indent=2))
    except (OSError,ValueError,AssertionError,KeyError,ET.ParseError) as exc:
        print(f'Structure failed: {exc}',file=sys.stderr);raise SystemExit(1)
