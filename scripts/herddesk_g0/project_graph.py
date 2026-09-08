"""Allowed C# project graph for HD-007. Structural only; not product AC pass."""
from __future__ import annotations

from pathlib import Path
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
    'tests/Contract/HerdDesk.ContractTests.csproj': (
        'src/HerdDesk.Contracts/HerdDesk.Contracts.csproj',
        'src/HerdDesk.Core/HerdDesk.Core.csproj',
        'src/HerdDesk.App/HerdDesk.App.csproj',
    ),
}

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

SKIP_DIR_PARTS = frozenset({'.git', 'obj', 'bin', 'probe-results', '.test-results', '__pycache__'})


def validate_project_graph(root: Path) -> dict[str, Any]:
    root = Path(root)
    check_product_lock_absence(root)
    projects = load_tree_projects(root)
    check_graph(projects)
    solution = load_solution_projects(root)
    if set(solution) != set(ALLOWED_PROJECTS):
        raise ProjectGraphError('unexpected_solution_projects')
    return {
        'project_graph': 'passed',
        'projects': len(projects),
        'package_references': 0,
    }


def check_product_lock_absence(root: Path) -> None:
    root = Path(root)
    if (root / 'Directory.Packages.props').is_file():
        raise ProjectGraphError('directory_packages_props_present')
    props = root / 'Directory.Build.props'
    if props.is_file() and '2.4.0' in props.read_text(encoding='utf-8'):
        raise ProjectGraphError('unverified_package_pin')
    for base_name in ('src', 'tests'):
        base = root / base_name
        if not base.is_dir():
            continue
        for lock in base.rglob('packages.lock.json'):
            if any(part in SKIP_DIR_PARTS for part in lock.parts):
                continue
            raise ProjectGraphError('package_lock_present')
        for path in base.rglob('*.csproj'):
            if any(part in SKIP_DIR_PARTS for part in path.parts):
                continue
            text = path.read_text(encoding='utf-8')
            lower = text.lower()
            if '2.4.0' in text:
                raise ProjectGraphError('unverified_package_pin')
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
    return {
        rel: {'project': list(refs), 'package': []}
        for rel, refs in ALLOWED_PROJECTS.items()
    }


def check_graph(projects: dict[str, dict[str, list[str]]]) -> None:
    if set(projects) != set(ALLOWED_PROJECTS):
        raise ProjectGraphError('unexpected_project_set')
    for rel, spec in projects.items():
        check_project(rel, spec)


def check_project(rel: str, spec: dict[str, list[str]]) -> None:
    packages = list(spec.get('package') or [])
    actual = list(spec.get('project') or [])
    joined = ' '.join(packages).lower()
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
