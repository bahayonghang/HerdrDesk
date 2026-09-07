#!/usr/bin/env python3
"""Structural validation only; this does not compile C# or pass any live gate."""
from pathlib import Path
import json
import sys
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]


def validate() -> dict:
    files=list(ROOT.rglob('*.json'))
    count=0
    for path in files:
        if any(part in {'.git','obj','bin','probe-results','.test-results'} for part in path.parts):continue
        json.loads(path.read_text(encoding='utf-8'));count+=1
    projects=list((ROOT/'src').rglob('*.csproj'))+list((ROOT/'tests').rglob('*.csproj'))
    for path in projects:
        doc=ET.parse(path)
        assert not doc.findall('.//PackageReference'), 'Unexpected dependency before G0 approval'
        for reference in doc.findall('.//ProjectReference'):
            assert (path.parent/reference.attrib['Include']).is_file(),'Missing project reference'
    for project in ET.parse(ROOT/'HerdDesk.slnx').findall('.//Project'):
        assert (ROOT/project.attrib['Path']).is_file(),'Missing solution project'
    tasks=json.loads((ROOT/'planning/backlog.json').read_text())['tasks']
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
    criteria=json.loads((ROOT/'planning/acceptance.json').read_text())['criteria']
    acs={c['id']:c for c in criteria}
    assert len(acs)==len(criteria)==48,'Unexpected acceptance IDs'
    for task in tasks:
        assert all(key in acs for key in task['acceptance_ids']),'Unknown acceptance ID'
        if task['status']=='completed':
            assert all(by_id[dep]['status']=='completed' for dep in task['depends_on']),'Incomplete dependency'
            for key in task['acceptance_ids']:
                assert acs[key]['status']=='passed' and acs[key].get('evidence'),'Missing acceptance evidence'
    assert (ROOT/'evidence/compatibility-baseline.json').is_file()
    baseline=json.loads((ROOT/'evidence/compatibility-baseline.json').read_text())
    assert baseline['herdr']['api_protocol']==20 and baseline['default_write_capability'] is False
    return {'structural_validation':'passed','json_files':count,'projects':len(projects),
            'tasks':len(tasks),'csharp_compiled':False,'windows_verified':False}


if __name__=='__main__':
    try:print(json.dumps(validate(),indent=2))
    except (OSError,ValueError,AssertionError,KeyError,ET.ParseError) as exc:
        print(f'Structure failed: {exc}',file=sys.stderr);raise SystemExit(1)
