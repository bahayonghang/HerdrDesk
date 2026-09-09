from copy import deepcopy
from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

L2 = ROOT / 'implementation' / 'hd-031-l2.json'
CORE = ROOT / 'src' / 'HerdDesk.Core' / 'Clipboard'
INFRA = ROOT / 'src' / 'HerdDesk.Infrastructure' / 'Clipboard'
WEB = ROOT / 'src' / 'HerdDesk.Terminal.Web' / 'Input' / 'OscClipboardPolicy.cs'
APP_VM = ROOT / 'src' / 'HerdDesk.App' / 'ViewModels' / 'PastePreviewViewModel.cs'
PASS_KEYS = ('ac36_passed', 'g0_passed')


class Hd031ResidualTests(unittest.TestCase):
    def test_l2_live_clipboard_ime_and_product_acs_stay_unverified(self):
        doc = json.loads(L2.read_text(encoding='utf-8'))
        self.assertEqual(doc['document_kind'], 'hd031_l2_status')
        self.assertEqual(doc['l2_live_clipboard'], 'UNVERIFIED')
        self.assertEqual(doc['l2_live_ime'], 'UNVERIFIED')
        self.assertFalse(doc['ac36_passed'])
        self.assertFalse(doc['g0_passed'])
        self.assertNotEqual(doc['phase_gate'], 'passed')
        self.assertFalse(doc['winui_admitted'])
        self.assertFalse(doc['integration_windows'])
        self.assertFalse(doc['integration_windows_project'])
        self.assertFalse(doc['live_clipboard'])
        self.assertFalse(doc['live_ime'])
        self.assertFalse(doc['clipboard_watcher'])
        self.assertEqual(doc['osc52_read_default'], 'deny')
        self.assertEqual(doc['osc52_write_default'], 'deny')
        self.assertTrue(doc['missing']['winui_xaml'])
        self.assertTrue(doc['missing']['live_clipboard'])
        self.assertTrue(doc['missing']['live_ime'])
        self.assertTrue(doc['missing']['integration_windows'])
        self.assertTrue(doc['missing']['clipboard_watcher'])
        self.assertTrue((CORE / 'ClipboardIntentResolver.cs').is_file())
        self.assertTrue((CORE / 'PasteCoordinator.cs').is_file())
        self.assertTrue((INFRA / 'AttachmentCache.cs').is_file())
        self.assertTrue((INFRA / 'WindowsClipboardSnapshotReader.cs').is_file())
        self.assertTrue(WEB.is_file())
        self.assertTrue(APP_VM.is_file())
        repository.check_integration_windows_layout(ROOT)
        self.assertFalse((ROOT / 'tests' / 'Integration.Ssh').exists())
        self.assertEqual(repository.app_source_xaml(ROOT), list(repository._APP_SHELL_XAML))
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])

    def test_closeout_rejects_pass_claims(self):
        hd031 = json.loads(L2.read_text(encoding='utf-8'))
        repository._check_hd031(hd031)

        for key in PASS_KEYS:
            bad = deepcopy(hd031)
            bad[key] = True
            with self.assertRaises(AssertionError):
                repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['phase_gate'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['l2_live_clipboard'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['l2_live_ime'] = 'passed'
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['winui_admitted'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['integration_windows'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['clipboard_watcher'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['live_clipboard'] = True
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['osc52_read_default'] = 'allow'
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

        bad = deepcopy(hd031)
        bad['missing'] = dict(hd031['missing'])
        bad['missing']['clipboard_watcher'] = False
        with self.assertRaises(AssertionError):
            repository._check_hd031(bad)

    def test_sources_forbid_watcher_glob_and_live_clipboard_in_tests(self):
        watcher = ('AddClipboardFormatListener', 'SetClipboardViewer', 'WM_CLIPBOARDUPDATE')
        for path in (
            CORE / 'ClipboardIntentResolver.cs',
            CORE / 'PasteCoordinator.cs',
            INFRA / 'AttachmentCache.cs',
            INFRA / 'WindowsClipboardSnapshotReader.cs',
            WEB,
            APP_VM,
            ROOT / 'src' / 'HerdDesk.Contracts' / 'ClipboardPorts.cs',
        ):
            text = path.read_text(encoding='utf-8')
            for token in watcher:
                self.assertNotIn(token, text)

        cache = (INFRA / 'AttachmentCache.cs').read_text(encoding='utf-8')
        for token in (
            'Directory.GetFiles', 'EnumerateFiles', 'EnumerateFileSystemEntries',
            'GetFileSystemEntries',
        ):
            self.assertNotIn(token, cache)

        osc = WEB.read_text(encoding='utf-8')
        for token in ('OpenClipboard', 'GetClipboardData', 'user32.dll'):
            self.assertNotIn(token, osc)

        live = ('OpenClipboard', 'GetClipboardData', 'CreateWindows(', 'ReadOnce(')
        for path in (ROOT / 'tests').rglob('*.cs'):
            posix = path.as_posix()
            if 'Clipboard' not in posix and path.name != 'OscClipboardPolicyTests.cs':
                continue
            text = path.read_text(encoding='utf-8')
            for token in live:
                self.assertNotIn(token, text, path.name)


if __name__ == '__main__':
    unittest.main()
