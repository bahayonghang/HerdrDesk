from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.project_graph import (
    ALLOWED_PROJECTS,
    ProjectGraphError,
    allowed_graph,
    check_graph,
    check_product_lock_absence,
    check_project,
    validate_project_graph,
)
import validate_repository as repository


class ProjectGraphTests(unittest.TestCase):
    def test_shipped_tree_matches_allowed_graph(self):
        result = validate_project_graph(ROOT)
        self.assertEqual(result['project_graph'], 'passed')
        self.assertEqual(result['package_references'], 0)
        check_graph(allowed_graph())
        repo = repository.validate()
        self.assertEqual(repo['project_graph'], 'passed')
        self.assertEqual(repo['github_required_check'], 'UNVERIFIED')
        self.assertEqual(repo['windows_desktop_restore'], 'not_admitted')
        self.assertFalse((ROOT / 'Directory.Packages.props').is_file())
        check_product_lock_absence(ROOT)

    def test_allowed_edges_pass_individually(self):
        for rel, refs in ALLOWED_PROJECTS.items():
            check_project(rel, {'project': list(refs), 'package': []})

    def test_core_winui_package_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                'src/HerdDesk.Core/HerdDesk.Core.csproj',
                {
                    'project': ['src/HerdDesk.Contracts/HerdDesk.Contracts.csproj'],
                    'package': ['Microsoft.WindowsAppSDK'],
                },
            )
        self.assertEqual(str(ctx.exception), 'forbidden_package_edge')

    def test_core_webview2_package_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                'src/HerdDesk.Core/HerdDesk.Core.csproj',
                {
                    'project': ['src/HerdDesk.Contracts/HerdDesk.Contracts.csproj'],
                    'package': ['Microsoft.Web.WebView2'],
                },
            )
        self.assertEqual(str(ctx.exception), 'forbidden_package_edge')

    def test_core_ssh_package_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                'src/HerdDesk.Core/HerdDesk.Core.csproj',
                {
                    'project': ['src/HerdDesk.Contracts/HerdDesk.Contracts.csproj'],
                    'package': ['SSH.NET'],
                },
            )
        self.assertEqual(str(ctx.exception), 'forbidden_package_edge')

    def test_contracts_third_party_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
                {'project': [], 'package': ['Newtonsoft.Json']},
            )
        self.assertEqual(str(ctx.exception), 'package_reference_forbidden')

    def test_contracts_project_reference_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
                {
                    'project': ['src/HerdDesk.Core/HerdDesk.Core.csproj'],
                    'package': [],
                },
            )
        self.assertEqual(str(ctx.exception), 'contracts_third_party')

    def test_production_to_tests_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                'src/HerdDesk.Infrastructure/HerdDesk.Infrastructure.csproj',
                {
                    'project': [
                        'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
                        'src/HerdDesk.Core/HerdDesk.Core.csproj',
                        'tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj',
                    ],
                    'package': [],
                },
            )
        self.assertEqual(str(ctx.exception), 'production_references_tests')

    def test_core_to_app_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                'src/HerdDesk.Core/HerdDesk.Core.csproj',
                {
                    'project': [
                        'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
                        'src/HerdDesk.App/HerdDesk.App.csproj',
                    ],
                    'package': [],
                },
            )
        self.assertEqual(str(ctx.exception), 'core_forbidden_edge')

    def test_terminal_web_to_core_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                'src/HerdDesk.Terminal.Web/HerdDesk.Terminal.Web.csproj',
                {
                    'project': [
                        'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
                        'src/HerdDesk.Core/HerdDesk.Core.csproj',
                    ],
                    'package': [],
                },
            )
        self.assertEqual(str(ctx.exception), 'forbidden_project_edge')

    def test_unknown_integration_project_fails(self):
        graph = allowed_graph()
        graph['tests/Integration.Windows/HerdDesk.Integration.Windows.csproj'] = {
            'project': ['src/HerdDesk.App/HerdDesk.App.csproj'],
            'package': [],
        }
        with self.assertRaises(ProjectGraphError) as ctx:
            check_graph(graph)
        self.assertEqual(str(ctx.exception), 'unexpected_project_set')

    def test_directory_packages_props_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'Directory.Packages.props').write_text('<Project />\n', encoding='utf-8')
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock_absence(root)
            self.assertEqual(str(ctx.exception), 'directory_packages_props_present')

    def test_csproj_product_display_version_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            proj = root / 'src' / 'HerdDesk.App'
            proj.mkdir(parents=True)
            (proj / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>2.4.0</Version></PropertyGroup></Project>\n',
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock_absence(root)
            self.assertEqual(str(ctx.exception), 'unverified_package_pin')

    def test_csproj_windowsappsdk_text_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            proj = root / 'src' / 'HerdDesk.App'
            proj.mkdir(parents=True)
            (proj / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>'
                '<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.0.0" />'
                '</ItemGroup></Project>\n',
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock_absence(root)
            self.assertEqual(str(ctx.exception), 'forbidden_package_edge')

    def test_packages_lock_json_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            proj = root / 'src' / 'HerdDesk.App'
            proj.mkdir(parents=True)
            (proj / 'packages.lock.json').write_text('{}\n', encoding='utf-8')
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock_absence(root)
            self.assertEqual(str(ctx.exception), 'package_lock_present')


if __name__ == '__main__':
    unittest.main()
