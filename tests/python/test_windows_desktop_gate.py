from pathlib import Path
import json
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.project_graph import REQUIRED_SOURCE_PATTERNS, check_nuget_config
import run_windows_desktop_gate as desktop_gate
import validate_repository as repository


def _write_record(root: Path, restore: str) -> None:
    impl = root / 'implementation'
    impl.mkdir(parents=True)
    (impl / 'hd-007-packages.json').write_text(
        json.dumps({
            'windows_desktop_restore': restore,
            'github_required_check': 'UNVERIFIED',
            'ac39_passed': False,
            'ac40_passed': False,
            'ac47_passed': False,
            'phase_gate': 'not_passed',
        }),
        encoding='utf-8',
    )


class WindowsDesktopGateTests(unittest.TestCase):
    def test_not_admitted_skips(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_record(root, 'not_admitted')
            plan = desktop_gate.plan_desktop_gate(root)
            self.assertEqual(plan.action, 'skip')
            self.assertEqual(plan.commands, ())
            self.assertEqual(desktop_gate.execute_desktop_gate(plan, cwd=root), 0)

    def test_admitted_without_lock_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_record(root, 'admitted')
            plan = desktop_gate.plan_desktop_gate(root)
            self.assertEqual(plan.action, 'fail')
            self.assertEqual(desktop_gate.execute_desktop_gate(plan, cwd=root), 1)

    def test_shipped_admitted_plans_restore_and_windows_tfm_build(self):
        record = json.loads((ROOT / 'implementation' / 'hd-007-packages.json').read_text(encoding='utf-8'))
        self.assertEqual(record['windows_desktop_restore'], 'admitted')
        self.assertEqual(record['github_required_check'], 'UNVERIFIED')
        self.assertFalse(record['ac39_passed'])
        self.assertFalse(record['ac40_passed'])
        self.assertFalse(record['ac47_passed'])
        self.assertNotEqual(record['phase_gate'], 'passed')
        check_nuget_config(ROOT)
        self.assertIn('Microsoft.Windows.SDK.NET.Ref', REQUIRED_SOURCE_PATTERNS)
        plan = desktop_gate.plan_desktop_gate(ROOT)
        self.assertEqual(plan.action, 'run')
        self.assertEqual(len(plan.commands), 2)
        self.assertEqual(plan.commands[0][0:2], ('dotnet', 'restore'))
        self.assertIn('--locked-mode', plan.commands[0])
        self.assertIn('--force-evaluate', plan.commands[0])
        self.assertNotIn('HerdDeskBclOnly', ' '.join(plan.commands[0]))
        self.assertNotIn('HerdDeskBclOnly', ' '.join(plan.commands[1]))
        self.assertEqual(plan.commands[1][0:2], ('dotnet', 'build'))
        self.assertIn(desktop_gate.WINDOWS_TFM, plan.commands[1])
        repo = repository.validate()
        self.assertEqual(repo['windows_desktop_restore'], 'admitted')
        self.assertEqual(repo['github_required_check'], 'UNVERIFIED')
        self.assertFalse(repo['g0_passed'])


if __name__ == '__main__':
    unittest.main()
