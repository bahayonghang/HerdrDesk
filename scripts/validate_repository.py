#!/usr/bin/env python3
"""Structural validation only; this does not compile C# or pass any live gate."""
from pathlib import Path
import json
import sys
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
SCRIPTS=Path(__file__).resolve().parent
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0,str(SCRIPTS))
from herddesk_g0.adr import validate_adr_baseline
from herddesk_g0.evidence import validate_evidence
from herddesk_g0.endpoint import validate_endpoint_matrix
from herddesk_g0.lease import validate_terminal_lease_matrix
from herddesk_g0.licensing import validate_licensing
from herddesk_g0.project_graph import validate_project_graph
from herddesk_g0.renderer import validate_renderer_matrix

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
    assert not (ROOT / 'tests' / 'Integration.Windows').exists()


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
    assert not (ROOT / 'tests' / 'Integration.Windows').exists()
    lock = (ROOT / 'filebridge' / 'Cargo.lock').read_text(encoding='utf-8')
    from herddesk_g0.project_graph import cargo_lock_package_version
    assert cargo_lock_package_version(lock, 'sha2') == '0.10.8'


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
    assert not list((ROOT / 'src' / 'HerdDesk.App').rglob('*.xaml'))
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    assert not (ROOT / 'tests' / 'Integration.Windows').exists()


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
    assert list((ROOT / 'src' / 'HerdDesk.App').rglob('*.xaml')) == []
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    assert not (ROOT / 'tests' / 'Integration.Windows').exists()


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
    assert list((ROOT / 'src' / 'HerdDesk.App').rglob('*.xaml')) == []
    assert not (ROOT / 'tests' / 'Integration.Ssh').exists()
    assert not (ROOT / 'tests' / 'Integration.Windows').exists()
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


def validate() -> dict:
    files=list(ROOT.rglob('*.json'))
    count=0
    for path in files:
        if any(part in {'.git','obj','bin','target','probe-results','.test-results'} for part in path.parts):continue
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
    catalog=json.loads((ROOT/'evidence/local-mvp/catalog.json').read_text(encoding='utf-8'))
    mvp=json.loads((ROOT/'evidence/multi-device-mvp/catalog.json').read_text(encoding='utf-8'))
    matrix=json.loads((ROOT/'evidence/multi-device-mvp/support-matrix.json').read_text(encoding='utf-8'))
    assert not (ROOT/'Directory.Packages.props').is_file()
    assert packages.get('directory_packages_props') is False
    assert packages['github_required_check']=='UNVERIFIED'
    assert packages['windows_desktop_restore']=='not_admitted'
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
    assert hd014.get('webview2_admitted') is not True
    assert hd014.get('npm_xterm_admitted') is not True
    assert hd014.get('phase_gate')!='passed'
    assert hd015.get('l3_ime_desktop')=='UNVERIFIED'
    assert hd015.get('l2_webview_ime')=='UNVERIFIED'
    assert hd015.get('ac09_passed') is not True
    assert hd015.get('ac10_passed') is not True
    assert hd015.get('g0_passed') is not True
    assert hd015.get('webview2_admitted') is not True
    assert hd015.get('npm_xterm_admitted') is not True
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
    assert not (ROOT/'tests/Integration.Ssh').exists()
    assert not (ROOT/'tests/Integration.Windows').exists()
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
            'ac08_passed':False,'ac09_passed':False,'ac44_passed':False,
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
