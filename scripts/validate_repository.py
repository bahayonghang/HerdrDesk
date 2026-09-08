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
