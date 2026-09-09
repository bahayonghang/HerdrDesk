from pathlib import Path
import json
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from herddesk_g0.project_graph import (
    ADMITTED_APP_PACKAGE_REFS,
    ADMITTED_LOCK_REL,
    APP_PROJECT,
    ALLOWED_PROJECTS,
    REQUIRED_SOURCE_PATTERNS,
    ProjectGraphError,
    allowed_graph,
    cargo_lock_package_version,
    check_directory_packages_props,
    check_graph,
    check_nuget_config,
    check_product_lock,
    check_product_lock_absence,
    check_project,
    check_lock_licensing_alignment,
    validate_project_graph,
    validate_rust_bridge_lock,
    validate_rust_filebridge_lock,
)
import validate_repository as repository
import run_windows_desktop_gate as desktop_gate


def _write_nuget_config(root: Path, *, extra_pattern=None, extra_source=None):
    patterns = sorted(REQUIRED_SOURCE_PATTERNS)
    if extra_pattern:
        patterns.append(extra_pattern)
    sources = ['    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />']
    if extra_source:
        sources.append(f'    <add key="other" value="{extra_source}" />')
    mapped = '\n'.join(f'      <package pattern="{item}" />' for item in patterns)
    (root / 'NuGet.Config').write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<configuration>\n  <packageSources>\n'
        '    <clear />\n' + '\n'.join(sources) +
        '\n  </packageSources>\n  <packageSourceMapping>\n    <packageSource key="nuget.org">\n'
        + mapped +
        '\n    </packageSource>\n  </packageSourceMapping>\n</configuration>\n',
        encoding='utf-8',
    )


def _write_packages_props(root: Path, *, extra=None, version='2.3.6', umbrella=False):
    lines = [
        '    <PackageVersion Include="Microsoft.WindowsAppSDK.WinUI" Version="' + version + '" />',
    ]
    if extra:
        ident, ver = extra
        lines.append(f'    <PackageVersion Include="{ident}" Version="{ver}" />')
    if umbrella:
        lines.append('    <PackageVersion Include="Microsoft.WindowsAppSDK" Version="1.0.0" />')
    (root / 'Directory.Packages.props').write_text(
        '<Project>\n  <PropertyGroup>\n    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>\n'
        '  </PropertyGroup>\n  <ItemGroup>\n' + '\n'.join(lines) + '\n  </ItemGroup>\n</Project>\n',
        encoding='utf-8',
    )


def _write_app_lock(root: Path, *, include_winui=True, umbrella=False, extra=None):
    app = root / 'src' / 'HerdDesk.App'
    app.mkdir(parents=True, exist_ok=True)
    deps = {}
    if include_winui:
        deps['Microsoft.WindowsAppSDK.WinUI'] = {
            'type': 'Direct',
            'requested': '[2.3.6, )',
            'resolved': '2.3.6',
            'contentHash': 'winui-hash',
        }
    if extra:
        ident, spec = extra
        deps[ident] = spec
    if umbrella:
        deps['Microsoft.WindowsAppSDK'] = {
            'type': 'Direct',
            'requested': '[2.4.0, )',
            'resolved': '2.4.0',
            'contentHash': 'umbrella-hash',
        }
    (app / 'packages.lock.json').write_text(
        json.dumps({'version': 1, 'dependencies': {'net10.0-windows10.0.19041.0': deps}}),
        encoding='utf-8',
    )


class ProjectGraphTests(unittest.TestCase):
    def test_shipped_tree_matches_allowed_graph(self):
        result = validate_project_graph(ROOT)
        self.assertEqual(result['project_graph'], 'passed')
        self.assertEqual(result['package_references'], 1)
        check_graph(allowed_graph())
        repo = repository.validate()
        self.assertEqual(repo['project_graph'], 'passed')
        self.assertEqual(repo['github_required_check'], 'UNVERIFIED')
        self.assertEqual(repo['windows_desktop_restore'], 'admitted')
        self.assertTrue((ROOT / 'Directory.Packages.props').is_file())
        self.assertTrue((ROOT / ADMITTED_LOCK_REL).is_file())
        check_product_lock_absence(ROOT)
        check_product_lock(ROOT)
        check_nuget_config(ROOT)
        self.assertIn('Microsoft.Windows.SDK.NET.Ref', REQUIRED_SOURCE_PATTERNS)
        rust = validate_rust_bridge_lock(ROOT)
        self.assertEqual(rust['l2_windows_named_pipe_acl'], 'UNVERIFIED')
        self.assertEqual(result['l2_windows_named_pipe_acl'], 'UNVERIFIED')
        lock_text = (ROOT / 'bridge' / 'Cargo.lock').read_text(encoding='utf-8')
        self.assertEqual(cargo_lock_package_version(lock_text, 'interprocess'), rust['interprocess'])
        self.assertEqual(result['interprocess_lock_version'], rust['interprocess'])
        plan = desktop_gate.plan_desktop_gate(ROOT)
        self.assertEqual(plan.action, 'run')

    def test_allowed_edges_pass_individually(self):
        for rel, refs in ALLOWED_PROJECTS.items():
            packages = list(ADMITTED_APP_PACKAGE_REFS) if rel == APP_PROJECT else []
            check_project(rel, {'project': list(refs), 'package': packages})

    def test_admitted_app_winui_package_passes(self):
        check_project(
            APP_PROJECT,
            {
                'project': list(ALLOWED_PROJECTS[APP_PROJECT]),
                'package': ['Microsoft.WindowsAppSDK.WinUI'],
            },
        )

    def test_app_missing_admitted_package_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                APP_PROJECT,
                {'project': list(ALLOWED_PROJECTS[APP_PROJECT]), 'package': []},
            )
        self.assertEqual(str(ctx.exception), 'package_reference_forbidden')

    def test_app_random_package_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                APP_PROJECT,
                {
                    'project': list(ALLOWED_PROJECTS[APP_PROJECT]),
                    'package': ['Newtonsoft.Json'],
                },
            )
        self.assertEqual(str(ctx.exception), 'package_reference_forbidden')

    def test_app_umbrella_wasdk_fails(self):
        with self.assertRaises(ProjectGraphError) as ctx:
            check_project(
                APP_PROJECT,
                {
                    'project': list(ALLOWED_PROJECTS[APP_PROJECT]),
                    'package': ['Microsoft.WindowsAppSDK'],
                },
            )
        self.assertEqual(str(ctx.exception), 'forbidden_package_edge')

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
        graph['tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj'] = {
            'project': ['src/HerdDesk.Infrastructure/HerdDesk.Infrastructure.csproj'],
            'package': [],
        }
        with self.assertRaises(ProjectGraphError) as ctx:
            check_graph(graph)
        self.assertEqual(str(ctx.exception), 'unexpected_project_set')

    def test_directory_packages_props_missing_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ProjectGraphError) as ctx:
                check_directory_packages_props(Path(tmp))
            self.assertEqual(str(ctx.exception), 'directory_packages_props_missing')

    def test_directory_packages_props_extra_package_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_packages_props(root, extra=('Newtonsoft.Json', '13.0.1'))
            with self.assertRaises(ProjectGraphError) as ctx:
                check_directory_packages_props(root)
            self.assertEqual(str(ctx.exception), 'package_reference_forbidden')

    def test_directory_packages_props_umbrella_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_packages_props(root, umbrella=True)
            with self.assertRaises(ProjectGraphError) as ctx:
                check_directory_packages_props(root)
            self.assertEqual(str(ctx.exception), 'forbidden_package_edge')

    def test_directory_packages_props_display_version_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_packages_props(root, version='2.4.0')
            with self.assertRaises(ProjectGraphError) as ctx:
                check_directory_packages_props(root)
            self.assertEqual(str(ctx.exception), 'unverified_package_pin')

    def test_nuget_extra_source_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root, extra_source='https://example.invalid/v3/index.json')
            with self.assertRaises(ProjectGraphError) as ctx:
                check_nuget_config(root)
            self.assertEqual(str(ctx.exception), 'nuget_source_unmapped')

    def test_nuget_extra_pattern_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root, extra_pattern='Newtonsoft.Json')
            with self.assertRaises(ProjectGraphError) as ctx:
                check_nuget_config(root)
            self.assertEqual(str(ctx.exception), 'nuget_source_unmapped')

    def test_csproj_product_display_version_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root)
            _write_packages_props(root)
            _write_app_lock(root)
            proj = root / 'src' / 'HerdDesk.App'
            (proj / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>2.4.0</Version></PropertyGroup></Project>\n',
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock(root)
            self.assertEqual(str(ctx.exception), 'unverified_package_pin')

    def test_csproj_windowsappsdk_umbrella_text_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root)
            _write_packages_props(root)
            _write_app_lock(root)
            proj = root / 'src' / 'HerdDesk.App'
            (proj / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>'
                '<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.0.0" />'
                '</ItemGroup></Project>\n',
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock(root)
            self.assertEqual(str(ctx.exception), 'forbidden_package_edge')

    def test_core_windowsappsdk_text_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root)
            _write_packages_props(root)
            _write_app_lock(root)
            (root / 'src' / 'HerdDesk.App' / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>'
                '</PropertyGroup><ItemGroup>'
                '<PackageReference Include="Microsoft.WindowsAppSDK.WinUI" />'
                '</ItemGroup></Project>\n',
                encoding='utf-8',
            )
            core = root / 'src' / 'HerdDesk.Core'
            core.mkdir(parents=True)
            (core / 'HerdDesk.Core.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>'
                '<PackageReference Include="Microsoft.WindowsAppSDK.WinUI" />'
                '</ItemGroup></Project>\n',
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock(root)
            self.assertEqual(str(ctx.exception), 'forbidden_package_edge')

    def test_missing_cargo_lock_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'bridge').mkdir()
            with self.assertRaises(ProjectGraphError) as ctx:
                validate_rust_bridge_lock(root)
            self.assertEqual(str(ctx.exception), 'missing_cargo_lock')

    def test_cargo_lock_version_mismatch_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'bridge').mkdir()
            (root / 'implementation').mkdir()
            (root / 'bridge' / 'Cargo.lock').write_text(
                '[[package]]\nname = "interprocess"\nversion = "0.0.0"\n',
                encoding='utf-8',
            )
            (root / 'implementation' / 'hd-008-packages.json').write_text(
                json.dumps({
                    'crates': [{'id': 'interprocess', 'requested': '2.4.4', 'lock_version': '2.4.4'}],
                    'l2_windows_named_pipe_acl': 'UNVERIFIED',
                    'ac03_passed': False,
                    'ac04_passed': False,
                    'phase_gate': 'not_passed',
                }),
                encoding='utf-8',
            )
            (root / 'implementation' / 'hd-008-l2.json').write_text(
                json.dumps({
                    'l2_windows_named_pipe_acl': 'UNVERIFIED',
                    'ac03_passed': False,
                    'ac04_passed': False,
                    'phase_gate': 'not_passed',
                }),
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                validate_rust_bridge_lock(root)
            self.assertEqual(str(ctx.exception), 'cargo_lock_version_mismatch')

    def test_filebridge_extra_crate_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'filebridge').mkdir()
            (root / 'implementation').mkdir()
            (root / 'filebridge' / 'Cargo.lock').write_text(
                '[[package]]\nname = "herddesk-filebridge"\nversion = "0.1.0"\n'
                '[[package]]\nname = "serde"\nversion = "1.0.0"\nsource = "registry+https://example"\n',
                encoding='utf-8',
            )
            (root / 'implementation' / 'hd-027-packages.json').write_text(
                json.dumps({
                    'crates': [],
                    'ac30_passed': False,
                    'ac34_passed': False,
                    'phase_gate': 'not_passed',
                    'l2_filesystem': 'UNVERIFIED',
                    'l2_ssh': 'UNVERIFIED',
                    'l2_toctou': 'UNVERIFIED',
                }),
                encoding='utf-8',
            )
            (root / 'implementation' / 'hd-027-l2.json').write_text(
                json.dumps({
                    'l2_filesystem': 'UNVERIFIED',
                    'l2_ssh': 'UNVERIFIED',
                    'l2_toctou': 'UNVERIFIED',
                    'ac30_passed': False,
                    'ac34_passed': False,
                    'phase_gate': 'not_passed',
                }),
                encoding='utf-8',
            )
            (root / 'implementation' / 'hd-028-packages.json').write_text(
                json.dumps({
                    'crates': [{'id': 'sha2', 'requested': '0.10.8', 'lock_version': '0.10.8'}],
                    'ac30_passed': False,
                    'ac31_passed': False,
                    'ac32_passed': False,
                    'phase_gate': 'not_passed',
                }),
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                validate_rust_filebridge_lock(root)
            self.assertEqual(str(ctx.exception), 'filebridge_unexpected_crate')

    def test_packages_lock_json_outside_app_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root)
            _write_packages_props(root)
            _write_app_lock(root)
            (root / 'src' / 'HerdDesk.App' / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>'
                '</PropertyGroup><ItemGroup>'
                '<PackageReference Include="Microsoft.WindowsAppSDK.WinUI" />'
                '</ItemGroup></Project>\n',
                encoding='utf-8',
            )
            core = root / 'src' / 'HerdDesk.Core'
            core.mkdir(parents=True)
            (core / 'packages.lock.json').write_text('{}\n', encoding='utf-8')
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock(root)
            self.assertEqual(str(ctx.exception), 'package_lock_present')

    def test_app_lock_without_winui_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root)
            _write_packages_props(root)
            _write_app_lock(root, include_winui=False)
            (root / 'src' / 'HerdDesk.App' / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>'
                '</PropertyGroup><ItemGroup>'
                '<PackageReference Include="Microsoft.WindowsAppSDK.WinUI" />'
                '</ItemGroup></Project>\n',
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock(root)
            self.assertEqual(str(ctx.exception), 'admitted_package_missing')

    def test_app_lock_extra_package_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root)
            _write_packages_props(root)
            _write_app_lock(
                root,
                extra=('Newtonsoft.Json', {
                    'type': 'Transitive',
                    'resolved': '13.0.1',
                    'contentHash': 'json-hash',
                }),
            )
            (root / 'src' / 'HerdDesk.App' / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>'
                '</PropertyGroup><ItemGroup>'
                '<PackageReference Include="Microsoft.WindowsAppSDK.WinUI" />'
                '</ItemGroup></Project>\n',
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock(root)
            self.assertEqual(str(ctx.exception), 'package_reference_forbidden')

    def test_app_lock_wrong_winui_version_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _write_nuget_config(root)
            _write_packages_props(root)
            app = root / 'src' / 'HerdDesk.App'
            app.mkdir(parents=True, exist_ok=True)
            (app / 'packages.lock.json').write_text(
                json.dumps({
                    'version': 1,
                    'dependencies': {
                        'net10.0-windows10.0.19041.0': {
                            'Microsoft.WindowsAppSDK.WinUI': {
                                'type': 'Direct',
                                'requested': '[2.3.0, )',
                                'resolved': '2.3.0',
                                'contentHash': 'old',
                            },
                        },
                    },
                }),
                encoding='utf-8',
            )
            (app / 'HerdDesk.App.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>'
                '</PropertyGroup><ItemGroup>'
                '<PackageReference Include="Microsoft.WindowsAppSDK.WinUI" />'
                '</ItemGroup></Project>\n',
                encoding='utf-8',
            )
            with self.assertRaises(ProjectGraphError) as ctx:
                check_product_lock(root)
            self.assertEqual(str(ctx.exception), 'unverified_package_pin')

    def test_shipped_lock_matches_licensing_units(self):
        check_lock_licensing_alignment(ROOT)


if __name__ == '__main__':
    unittest.main()
