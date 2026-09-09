"""Allowed C# project graph plus HD-008 Cargo.lock pin. Structural only; not product AC pass."""
from __future__ import annotations

from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET
from typing import Any


class ProjectGraphError(ValueError):
    """Stable project-graph code; the message is the code only."""


ALLOWED_PROJECTS: dict[str, tuple[str, ...]] = {
    'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj': (),
    'src/HerdDesk.Core/HerdDesk.Core.csproj': (
        'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
    ),
    'src/HerdDesk.Infrastructure/HerdDesk.Infrastructure.csproj': (
        'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
        'src/HerdDesk.Core/HerdDesk.Core.csproj',
    ),
    'src/HerdDesk.Terminal.Web/HerdDesk.Terminal.Web.csproj': (
        'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
    ),
    'src/HerdDesk.App/HerdDesk.App.csproj': (
        'src/HerdDesk.Core/HerdDesk.Core.csproj',
        'src/HerdDesk.Infrastructure/HerdDesk.Infrastructure.csproj',
        'src/HerdDesk.Terminal.Web/HerdDesk.Terminal.Web.csproj',
    ),
    'tests/HerdDesk.Core.SmokeTests/HerdDesk.Core.SmokeTests.csproj': (
        'src/HerdDesk.Core/HerdDesk.Core.csproj',
    ),
    'tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj': (
        'src/HerdDesk.Core/HerdDesk.Core.csproj',
    ),
    'tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj': (
        'src/HerdDesk.Infrastructure/HerdDesk.Infrastructure.csproj',
    ),
    'tests/Unit/HerdDesk.App.Tests/HerdDesk.App.Tests.csproj': (
        'src/HerdDesk.App/HerdDesk.App.csproj',
    ),
    'tests/Unit/HerdDesk.Terminal.Web.Tests/HerdDesk.Terminal.Web.Tests.csproj': (
        'src/HerdDesk.Terminal.Web/HerdDesk.Terminal.Web.csproj',
        'src/HerdDesk.Core/HerdDesk.Core.csproj',
    ),
    'tests/Contract/HerdDesk.ContractTests.csproj': (
        'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
        'src/HerdDesk.Core/HerdDesk.Core.csproj',
        'src/HerdDesk.App/HerdDesk.App.csproj',
    ),
}

APP_PROJECT = 'src/HerdDesk.App/HerdDesk.App.csproj'
ADMITTED_APP_PACKAGE_REFS = frozenset({'Microsoft.WindowsAppSDK.WinUI'})
ADMITTED_PACKAGE_VERSION_IDS = frozenset({
    'Microsoft.WindowsAppSDK.WinUI',
    'Microsoft.WindowsAppSDK.Base',
    'Microsoft.WindowsAppSDK.Foundation',
    'Microsoft.WindowsAppSDK.InteractiveExperiences',
    'Microsoft.Web.WebView2',
    'Microsoft.Windows.SDK.BuildTools',
    'Microsoft.Windows.SDK.BuildTools.MSIX',
})
REQUIRED_PACKAGE_VERSION_IDS = frozenset({'Microsoft.WindowsAppSDK.WinUI'})
PINNED_PACKAGE_VERSIONS = {
    'Microsoft.WindowsAppSDK.WinUI': '2.3.6',
    'Microsoft.WindowsAppSDK.Base': '2.0.4',
    'Microsoft.WindowsAppSDK.Foundation': '2.3.9',
    'Microsoft.WindowsAppSDK.InteractiveExperiences': '2.1.3',
    'Microsoft.Web.WebView2': '1.0.3719.77',
    'Microsoft.Windows.SDK.BuildTools': '10.0.26100.4654',
    'Microsoft.Windows.SDK.BuildTools.MSIX': '1.7.251221100',
}
UMBRELLA_PACKAGE_ID = 'Microsoft.WindowsAppSDK'
ADMITTED_LOCK_REL = 'src/HerdDesk.App/packages.lock.json'
WINDOWS_APP_TFM = 'net10.0-windows10.0.19041.0'
NUGET_SOURCE_URL = 'https://api.nuget.org/v3/index.json'
REQUIRED_SOURCE_PATTERNS = frozenset({
    'Microsoft.WindowsAppSDK.*',
    'Microsoft.Web.WebView2',
    'Microsoft.Windows.SDK.BuildTools',
    'Microsoft.Windows.SDK.BuildTools.MSIX',
    'Microsoft.Windows.SDK.NET.Ref',
    'Microsoft.Windows.SDK.NET.Ref.Windows',
})
ALLOWED_SOURCE_PATTERNS = REQUIRED_SOURCE_PATTERNS
UMBRELLA_INCLUDE_RE = re.compile(
    r'Include\s*=\s*"Microsoft\.WindowsAppSDK"',
    re.IGNORECASE,
)

FORBIDDEN_PACKAGE_MARKERS = (
    'microsoft.windowsappsdk',
    'microsoft.web.webview2',
    'winui',
    'ssh.net',
    'renci.sshnet',
)

FORBIDDEN_CSPROJ_TEXT = (
    'microsoft.windowsappsdk',
    'microsoft.web.webview2',
    'ssh.net',
    'renci.sshnet',
)

SKIP_DIR_PARTS = frozenset({'.git', 'obj', 'bin', 'target', 'probe-results', '.test-results', '__pycache__'})


def validate_project_graph(root: Path) -> dict[str, Any]:
    root = Path(root)
    check_product_lock(root)
    check_lock_licensing_alignment(root)
    rust = validate_rust_bridge_lock(root)
    validate_rust_filebridge_lock(root)
    projects = load_tree_projects(root)
    check_graph(projects)
    solution = load_solution_projects(root)
    if set(solution) != set(ALLOWED_PROJECTS):
        raise ProjectGraphError('unexpected_solution_projects')
    package_count = sum(len(spec.get('package') or []) for spec in projects.values())
    return {
        'project_graph': 'passed',
        'projects': len(projects),
        'package_references': package_count,
        'interprocess_lock_version': rust['interprocess'],
        'l2_windows_named_pipe_acl': rust['l2_windows_named_pipe_acl'],
    }


def cargo_lock_package_version(text: str, crate: str) -> str | None:
    lines = text.splitlines()
    target = f'name = "{crate}"'
    for index, line in enumerate(lines):
        if line != target:
            continue
        for follow in lines[index + 1:index + 8]:
            if follow.startswith('version = '):
                return follow.split('=', 1)[1].strip().strip('"')
            if follow.startswith('name = '):
                break
    return None


def validate_rust_bridge_lock(root: Path) -> dict[str, Any]:
    root = Path(root)
    lock_path = root / 'bridge' / 'Cargo.lock'
    if not lock_path.is_file():
        raise ProjectGraphError('missing_cargo_lock')
    lock_text = lock_path.read_text(encoding='utf-8')
    version = cargo_lock_package_version(lock_text, 'interprocess')
    if version is None:
        raise ProjectGraphError('missing_cargo_lock_crate')
    probe_path = root / 'implementation' / 'hd-008-packages.json'
    l2_path = root / 'implementation' / 'hd-008-l2.json'
    if not probe_path.is_file() or not l2_path.is_file():
        raise ProjectGraphError('missing_hd008_probe')
    probe = json.loads(probe_path.read_text(encoding='utf-8'))
    l2 = json.loads(l2_path.read_text(encoding='utf-8'))
    recorded = next((item for item in probe.get('crates') or [] if item.get('id') == 'interprocess'), None)
    if recorded is None or recorded.get('lock_version') != version or recorded.get('requested') != version:
        raise ProjectGraphError('cargo_lock_version_mismatch')
    if probe.get('l2_windows_named_pipe_acl') != 'UNVERIFIED' or l2.get('l2_windows_named_pipe_acl') != 'UNVERIFIED':
        raise ProjectGraphError('l2_claimed_verified')
    if probe.get('ac03_passed') is True or probe.get('ac04_passed') is True:
        raise ProjectGraphError('ac03_claimed_passed')
    if l2.get('ac03_passed') is True or l2.get('ac04_passed') is True:
        raise ProjectGraphError('ac03_claimed_passed')
    if probe.get('phase_gate') == 'passed' or l2.get('phase_gate') == 'passed':
        raise ProjectGraphError('phase_gate_claimed_passed')
    return {
        'rust_lock': 'passed',
        'interprocess': version,
        'l2_windows_named_pipe_acl': 'UNVERIFIED',
    }


def validate_rust_filebridge_lock(root: Path) -> dict[str, Any]:
    root = Path(root)
    lock_path = root / 'filebridge' / 'Cargo.lock'
    if not lock_path.is_file():
        raise ProjectGraphError('missing_filebridge_cargo_lock')
    lock_text = lock_path.read_text(encoding='utf-8')
    if cargo_lock_package_version(lock_text, 'herddesk-filebridge') is None:
        raise ProjectGraphError('missing_filebridge_package')
    probe_path = root / 'implementation' / 'hd-027-packages.json'
    l2_path = root / 'implementation' / 'hd-027-l2.json'
    hd028_path = root / 'implementation' / 'hd-028-packages.json'
    if not probe_path.is_file() or not l2_path.is_file():
        raise ProjectGraphError('missing_hd027_probe')
    probe = json.loads(probe_path.read_text(encoding='utf-8'))
    l2 = json.loads(l2_path.read_text(encoding='utf-8'))
    admitted = {'herddesk-filebridge'}
    sha2_pin = None
    if hd028_path.is_file():
        hd028 = json.loads(hd028_path.read_text(encoding='utf-8'))
        for item in hd028.get('crates') or []:
            crate_id = item.get('id')
            if crate_id:
                admitted.add(crate_id)
            if crate_id == 'sha2':
                sha2_pin = item.get('lock_version') or item.get('requested')
        if hd028.get('ac30_passed') is True or hd028.get('ac31_passed') is True or hd028.get('ac32_passed') is True:
            raise ProjectGraphError('ac30_claimed_passed')
        if hd028.get('phase_gate') == 'passed' or hd028.get('g0_passed') is True:
            raise ProjectGraphError('phase_gate_claimed_passed')
    lock_sha2 = cargo_lock_package_version(lock_text, 'sha2')
    if sha2_pin and lock_sha2 is not None and lock_sha2 != sha2_pin:
        raise ProjectGraphError('filebridge_sha2_pin_mismatch')
    for line in lock_text.splitlines():
        if line.startswith('name = '):
            name = line.split('=', 1)[1].strip().strip('"')
            if name not in admitted and name != 'herddesk-filebridge':
                raise ProjectGraphError('filebridge_unexpected_crate')
    for doc in (probe, l2):
        if doc.get('ac30_passed') is True or doc.get('ac34_passed') is True:
            raise ProjectGraphError('ac30_claimed_passed')
        if doc.get('phase_gate') == 'passed':
            raise ProjectGraphError('phase_gate_claimed_passed')
        if doc.get('g0_passed') is True:
            raise ProjectGraphError('g0_claimed_passed')
    for key in ('l2_filesystem', 'l2_ssh', 'l2_toctou'):
        if l2.get(key) != 'UNVERIFIED':
            raise ProjectGraphError('l2_claimed_verified')
        if probe.get(key) not in (None, 'UNVERIFIED'):
            raise ProjectGraphError('l2_claimed_verified')
    return {
        'rust_lock': 'passed',
        'filebridge_registry_crates': 0,
        'l2_filesystem': 'UNVERIFIED',
    }


def check_product_lock_absence(root: Path) -> None:
    """Backward-compatible name; HD-007 L2 admits the App windows lock only."""
    check_product_lock(root)


def check_product_lock(root: Path) -> None:
    root = Path(root)
    check_nuget_config(root)
    check_directory_packages_props(root)
    props = root / 'Directory.Build.props'
    if props.is_file() and '2.4.0' in props.read_text(encoding='utf-8'):
        raise ProjectGraphError('unverified_package_pin')
    lock_found = False
    for base_name in ('src', 'tests'):
        base = root / base_name
        if not base.is_dir():
            continue
        for lock in base.rglob('packages.lock.json'):
            if any(part in SKIP_DIR_PARTS for part in lock.parts):
                continue
            rel = lock.relative_to(root).as_posix()
            if rel != ADMITTED_LOCK_REL:
                raise ProjectGraphError('package_lock_present')
            lock_found = True
            check_admitted_lock_file(lock)
        for path in base.rglob('*.csproj'):
            if any(part in SKIP_DIR_PARTS for part in path.parts):
                continue
            rel = path.relative_to(root).as_posix()
            text = path.read_text(encoding='utf-8')
            check_csproj_lock_text(rel, text)
    if not lock_found:
        raise ProjectGraphError('package_lock_missing')
    if (root / 'packages.lock.json').is_file():
        raise ProjectGraphError('package_lock_present')


def check_nuget_config(root: Path) -> None:
    path = Path(root) / 'NuGet.Config'
    if not path.is_file():
        raise ProjectGraphError('nuget_config_missing')
    doc = ET.parse(path)
    sources = doc.find('packageSources')
    if sources is None:
        raise ProjectGraphError('nuget_source_unmapped')
    if sources.find('clear') is None:
        raise ProjectGraphError('nuget_source_unmapped')
    added = [
        node.attrib.get('value', '')
        for node in sources.findall('add')
    ]
    if added != [NUGET_SOURCE_URL]:
        raise ProjectGraphError('nuget_source_unmapped')
    if any(not item.startswith('https://') for item in added):
        raise ProjectGraphError('nuget_source_unmapped')
    mapping = doc.find('packageSourceMapping')
    if mapping is None:
        raise ProjectGraphError('nuget_source_unmapped')
    mapped: set[str] = set()
    for source in mapping.findall('packageSource'):
        if source.attrib.get('key') != 'nuget.org':
            raise ProjectGraphError('nuget_source_unmapped')
        for package in source.findall('package'):
            pattern = package.attrib.get('pattern') or ''
            if pattern not in ALLOWED_SOURCE_PATTERNS:
                raise ProjectGraphError('nuget_source_unmapped')
            mapped.add(pattern)
    if not REQUIRED_SOURCE_PATTERNS <= mapped:
        raise ProjectGraphError('nuget_source_unmapped')


def check_directory_packages_props(root: Path) -> None:
    path = Path(root) / 'Directory.Packages.props'
    if not path.is_file():
        raise ProjectGraphError('directory_packages_props_missing')
    text = path.read_text(encoding='utf-8')
    if '2.4.0' in text:
        raise ProjectGraphError('unverified_package_pin')
    if UMBRELLA_INCLUDE_RE.search(text):
        raise ProjectGraphError('forbidden_package_edge')
    doc = ET.parse(path)
    versions: dict[str, str] = {}
    for node in doc.findall('.//PackageVersion'):
        include = node.attrib.get('Include') or ''
        version = node.attrib.get('Version') or ''
        if include == UMBRELLA_PACKAGE_ID:
            raise ProjectGraphError('forbidden_package_edge')
        if include not in ADMITTED_PACKAGE_VERSION_IDS:
            raise ProjectGraphError('package_reference_forbidden')
        if include in PINNED_PACKAGE_VERSIONS and version != PINNED_PACKAGE_VERSIONS[include]:
            raise ProjectGraphError('unverified_package_pin')
        versions[include] = version
    if not REQUIRED_PACKAGE_VERSION_IDS <= set(versions):
        raise ProjectGraphError('admitted_package_missing')


def check_admitted_lock_file(path: Path) -> None:
    text = path.read_text(encoding='utf-8')
    if not text.strip():
        raise ProjectGraphError('package_lock_missing')
    if '2.4.0' in text:
        raise ProjectGraphError('unverified_package_pin')
    if re.search(r'"Microsoft\.WindowsAppSDK"\s*:', text):
        raise ProjectGraphError('forbidden_package_edge')
    data = json.loads(text)
    if not isinstance(data, dict):
        raise ProjectGraphError('package_lock_missing')
    dependencies = data.get('dependencies')
    if not isinstance(dependencies, dict) or not dependencies:
        raise ProjectGraphError('package_lock_missing')
    found_winui = False
    for tfm, deps in dependencies.items():
        if not isinstance(deps, dict):
            raise ProjectGraphError('package_lock_missing')
        for name, spec in deps.items():
            if not isinstance(name, str) or not isinstance(spec, dict):
                raise ProjectGraphError('package_lock_missing')
            if spec.get('type') == 'Project' or name.lower().startswith('herddesk.'):
                continue
            if name == UMBRELLA_PACKAGE_ID:
                raise ProjectGraphError('forbidden_package_edge')
            if name not in ADMITTED_PACKAGE_VERSION_IDS:
                raise ProjectGraphError('package_reference_forbidden')
            resolved = spec.get('resolved') or ''
            if name in PINNED_PACKAGE_VERSIONS and resolved != PINNED_PACKAGE_VERSIONS[name]:
                raise ProjectGraphError('unverified_package_pin')
            if name == 'Microsoft.WindowsAppSDK.WinUI':
                if spec.get('type') != 'Direct':
                    raise ProjectGraphError('admitted_package_missing')
                found_winui = True
    if not found_winui:
        raise ProjectGraphError('admitted_package_missing')


def lock_nuget_packages(data: dict[str, Any]) -> dict[str, dict[str, str]]:
    found: dict[str, dict[str, str]] = {}
    dependencies = data.get('dependencies')
    if not isinstance(dependencies, dict):
        return found
    for deps in dependencies.values():
        if not isinstance(deps, dict):
            continue
        for name, spec in deps.items():
            if not isinstance(name, str) or not isinstance(spec, dict):
                continue
            if spec.get('type') == 'Project' or name.lower().startswith('herddesk.'):
                continue
            resolved = spec.get('resolved') or ''
            content = spec.get('contentHash') or ''
            previous = found.get(name)
            if previous is not None and (
                previous['resolved'] != resolved or previous['contentHash'] != content
            ):
                raise ProjectGraphError('unverified_package_pin')
            found[name] = {'resolved': resolved, 'contentHash': content}
    return found


def check_lock_licensing_alignment(root: Path) -> None:
    root = Path(root)
    register_path = root / 'docs' / 'licensing' / 'register.json'
    lock_path = root / ADMITTED_LOCK_REL
    if not register_path.is_file() or not lock_path.is_file():
        raise ProjectGraphError('missing_record_field')
    register = json.loads(register_path.read_text(encoding='utf-8'))
    lock = json.loads(lock_path.read_text(encoding='utf-8'))
    if not isinstance(register, dict) or not isinstance(lock, dict):
        raise ProjectGraphError('missing_record_field')
    lock_packages = lock_nuget_packages(lock)
    register_units: dict[str, dict[str, Any]] = {}
    for unit in register.get('units') or []:
        if not isinstance(unit, dict):
            raise ProjectGraphError('missing_record_field')
        if unit.get('enters_package_lock') is not True:
            continue
        if unit.get('admission') != 'approved' or unit.get('lock_allowed') is not True:
            raise ProjectGraphError('pending_treated_as_approved')
        name = unit.get('name')
        if not isinstance(name, str) or not name:
            raise ProjectGraphError('missing_record_field')
        if name in register_units:
            raise ProjectGraphError('missing_record_field')
        register_units[name] = unit
    if set(lock_packages) != set(register_units):
        raise ProjectGraphError('licensing_lock_mismatch')
    for name, spec in lock_packages.items():
        unit = register_units[name]
        if unit.get('version') != spec['resolved']:
            raise ProjectGraphError('licensing_lock_mismatch')
        hash_obj = unit.get('hash') or {}
        if not isinstance(hash_obj, dict):
            raise ProjectGraphError('licensing_lock_mismatch')
        if hash_obj.get('nuget_content_hash') != spec['contentHash']:
            raise ProjectGraphError('licensing_lock_mismatch')
        if name in PINNED_PACKAGE_VERSIONS and spec['resolved'] != PINNED_PACKAGE_VERSIONS[name]:
            raise ProjectGraphError('unverified_package_pin')


def check_csproj_lock_text(rel: str, text: str) -> None:
    if '2.4.0' in text:
        raise ProjectGraphError('unverified_package_pin')
    lower = text.lower()
    if rel == APP_PROJECT:
        if UMBRELLA_INCLUDE_RE.search(text):
            raise ProjectGraphError('forbidden_package_edge')
        if 'ssh.net' in lower or 'renci.sshnet' in lower:
            raise ProjectGraphError('forbidden_package_edge')
        if WINDOWS_APP_TFM not in text:
            raise ProjectGraphError('admitted_package_missing')
        if 'Microsoft.WindowsAppSDK.WinUI' not in text:
            raise ProjectGraphError('admitted_package_missing')
        return
    if any(marker in lower for marker in FORBIDDEN_CSPROJ_TEXT):
        raise ProjectGraphError('forbidden_package_edge')


def load_tree_projects(root: Path) -> dict[str, dict[str, list[str]]]:
    found: dict[str, dict[str, list[str]]] = {}
    for base in (root / 'src', root / 'tests'):
        if not base.is_dir():
            continue
        for path in sorted(base.rglob('*.csproj')):
            if any(part in SKIP_DIR_PARTS for part in path.parts):
                continue
            rel = path.relative_to(root).as_posix()
            found[rel] = parse_csproj(path, root)
    return found


def load_solution_projects(root: Path) -> list[str]:
    doc = ET.parse(root / 'HerdDesk.slnx')
    items: list[str] = []
    for project in doc.findall('.//Project'):
        rel = project.attrib['Path'].replace('\\', '/')
        items.append(rel)
        if not (root / rel).is_file():
            raise ProjectGraphError('missing_solution_project')
    return items


def parse_csproj(path: Path, root: Path) -> dict[str, list[str]]:
    doc = ET.parse(path)
    project_refs: list[str] = []
    package_refs: list[str] = []
    for reference in doc.findall('.//ProjectReference'):
        include = reference.attrib.get('Include')
        if not include:
            raise ProjectGraphError('missing_project_reference')
        target = (path.parent / include).resolve()
        if not target.is_file():
            raise ProjectGraphError('missing_project_reference')
        project_refs.append(target.relative_to(root.resolve()).as_posix())
    for reference in doc.findall('.//PackageReference'):
        include = reference.attrib.get('Include') or ''
        package_refs.append(include)
    return {'project': project_refs, 'package': package_refs}


def allowed_graph() -> dict[str, dict[str, list[str]]]:
    graph: dict[str, dict[str, list[str]]] = {}
    for rel, refs in ALLOWED_PROJECTS.items():
        packages = sorted(ADMITTED_APP_PACKAGE_REFS) if rel == APP_PROJECT else []
        graph[rel] = {'project': list(refs), 'package': packages}
    return graph


def check_graph(projects: dict[str, dict[str, list[str]]]) -> None:
    if set(projects) != set(ALLOWED_PROJECTS):
        raise ProjectGraphError('unexpected_project_set')
    for rel, spec in projects.items():
        check_project(rel, spec)


def check_project(rel: str, spec: dict[str, list[str]]) -> None:
    packages = list(spec.get('package') or [])
    actual = list(spec.get('project') or [])
    joined = ' '.join(packages).lower()
    if rel == APP_PROJECT:
        if any('ssh.net' in item.lower() or 'renci.sshnet' in item.lower() for item in packages):
            raise ProjectGraphError('forbidden_package_edge')
        if UMBRELLA_PACKAGE_ID in packages:
            raise ProjectGraphError('forbidden_package_edge')
        if frozenset(packages) != ADMITTED_APP_PACKAGE_REFS:
            raise ProjectGraphError('package_reference_forbidden')
    else:
        if any(marker in joined for marker in FORBIDDEN_PACKAGE_MARKERS):
            raise ProjectGraphError('forbidden_package_edge')
        if packages:
            raise ProjectGraphError('package_reference_forbidden')
    if rel.startswith('src/') and any(item.startswith('tests/') for item in actual):
        raise ProjectGraphError('production_references_tests')
    if rel == 'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj' and (actual or packages):
        raise ProjectGraphError('contracts_third_party')
    if rel == 'src/HerdDesk.Core/HerdDesk.Core.csproj':
        if any('winui' in item.lower() or 'webview' in item.lower() or 'ssh' in item.lower()
               for item in actual + packages):
            raise ProjectGraphError('core_forbidden_edge')
        if frozenset(actual) != {'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj'}:
            raise ProjectGraphError('core_forbidden_edge')
    allowed = ALLOWED_PROJECTS.get(rel)
    if allowed is None:
        raise ProjectGraphError('unexpected_project_set')
    if frozenset(actual) != frozenset(allowed):
        raise ProjectGraphError('forbidden_project_edge')
