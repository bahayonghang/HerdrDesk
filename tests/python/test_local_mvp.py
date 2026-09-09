from pathlib import Path
import json
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import validate_repository as repository

CATALOG = ROOT / 'evidence' / 'local-mvp' / 'catalog.json'
L2 = ROOT / 'implementation' / 'hd-019-l2.json'
L3 = ROOT / 'implementation' / 'hd-019-l3.json'
REQUIRED_SCENARIOS = (
    'observe', 'control', 'ime', 'input', 'resize', 'scroll', 'release',
    'gui-close', 'recovery', 'agent-tui',
)
REQUIRED_TARGETS = ('powershell', 'claude-code', 'codex', 'opencode')
L3_SCENARIOS = frozenset({'ime', 'agent-tui'})


class LocalMvpCatalogTests(unittest.TestCase):
    def test_catalog_live_rows_stay_unverified(self):
        catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
        self.assertEqual(catalog['document_kind'], 'hd019_local_mvp_catalog')
        self.assertTrue(catalog['simulation'])
        self.assertFalse(catalog['ac06_passed'])
        self.assertFalse(catalog['ac07_passed'])
        self.assertFalse(catalog['ac10_passed'])
        self.assertFalse(catalog['ac15_passed'])
        self.assertFalse(catalog['g0_passed'])
        self.assertNotEqual(catalog['phase_gate'], 'passed')
        self.assertFalse(catalog['live_herdr'])
        self.assertFalse(catalog['winui_admitted'])
        self.assertFalse(catalog['webview2_admitted'])
        self.assertFalse(catalog['integration_windows_project'])
        missing = catalog['missing']
        self.assertTrue(missing['disposable_pane'])
        self.assertTrue(missing['webview2'])
        self.assertTrue(missing['ime_desktop'])
        self.assertTrue(missing['agent_tui_versions'])
        targets = [item['id'] for item in catalog['targets']]
        self.assertEqual(tuple(targets), REQUIRED_TARGETS)
        self.assertNotIn('muse', targets)
        self.assertTrue(all(item['live_verified'] is False for item in catalog['targets']))
        unknown = [item['id'] for item in catalog['unknown_agents']]
        self.assertIn('muse', unknown)
        graphics = [item['id'] for item in catalog['graphics_agents']]
        self.assertTrue(graphics)
        self.assertTrue(all(item['status'] == 'UNVERIFIED' for item in catalog['graphics_agents']))
        scenario_ids = [item['id'] for item in catalog['scenarios']]
        self.assertEqual(tuple(scenario_ids), REQUIRED_SCENARIOS)
        for scenario in catalog['scenarios']:
            evidence = scenario['required_evidence']
            self.assertIn(evidence, ('L2', 'L3'))
            self.assertEqual(scenario['live_status'], 'UNVERIFIED')
            if scenario['id'] in L3_SCENARIOS:
                self.assertEqual(evidence, 'L3')
            else:
                self.assertEqual(evidence, 'L2')
        rows = {(item['agent'], item['scenario']): item for item in catalog['live_rows']}
        self.assertEqual(len(rows), len(REQUIRED_TARGETS) * len(REQUIRED_SCENARIOS))
        for agent in REQUIRED_TARGETS:
            for scenario in REQUIRED_SCENARIOS:
                cell = rows[(agent, scenario)]
                self.assertEqual(cell['status'], 'UNVERIFIED')
                self.assertEqual(cell['result'], 'not_run')
                self.assertIsNone(cell['evidence_path'])
                expected = 'L3' if scenario in L3_SCENARIOS else 'L2'
                self.assertEqual(cell['required_evidence'], expected)

    def test_implementation_residuals_stay_unverified(self):
        l2 = json.loads(L2.read_text(encoding='utf-8'))
        l3 = json.loads(L3.read_text(encoding='utf-8'))
        self.assertEqual(l2['l2_live_local_mvp'], 'UNVERIFIED')
        self.assertEqual(l3['l3_ime_desktop'], 'UNVERIFIED')
        self.assertEqual(l3['l3_agent_tui'], 'UNVERIFIED')
        self.assertEqual(l3['agent_tui_versions'], 'UNVERIFIED')
        for doc in (l2, l3):
            self.assertFalse(doc['ac06_passed'])
            self.assertFalse(doc['ac07_passed'])
            self.assertFalse(doc['ac10_passed'])
            self.assertFalse(doc['ac15_passed'])
            self.assertFalse(doc['g0_passed'])
            self.assertNotEqual(doc['phase_gate'], 'passed')
            self.assertFalse(doc['live_herdr'])
            self.assertTrue(doc['missing']['disposable_pane'])
            self.assertTrue(doc['missing']['webview2'])
            self.assertTrue(doc['missing']['ime_desktop'])
            self.assertTrue(doc['missing']['agent_tui_versions'])

    def test_repository_validate_keeps_ac_and_g0_false(self):
        result = repository.validate()
        self.assertEqual(result['structural_validation'], 'passed')
        self.assertFalse(result['g0_passed'])
        self.assertFalse(result['windows_verified'])
        repository.check_integration_windows_layout(ROOT)


if __name__ == '__main__':
    unittest.main()
