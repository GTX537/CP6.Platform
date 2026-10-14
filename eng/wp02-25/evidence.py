"""Local WP02-25 provenance and scope checks; never run tests or contact services."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
RUN = ROOT / 'docs/development/evidence/WP02-25/20261014T052841Z'
BASE = 'e5c63a28c94bae4cc66ec50fbe6079553c03d0d0'
MAIN = '1ab2d031e3c501662ecb89a33839fac1cba55d33'


def git(*args, cwd=ROOT):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', *args], cwd=cwd)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        raise ValueError(f'Refusing to overwrite evidence: {path}')
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')


def intake():
    sources = [
        ('dispatch.md', 'docs/development/dispatch/20261013-wave6/WP02-25.md'),
        ('admission.md', 'docs/development/evidence/wave6-dispatch-20261013/admission-observability.md'),
        ('admission.json', 'docs/development/evidence/wave6-dispatch-20261013/admission-observability.json'),
        ('parallel.md', 'docs/development/09-parallel-development.md'),
        ('handbook.md', 'docs/design-handbook/modules/PLT-OBS-01.md'),
    ]
    records = []
    for name, path in sources:
        raw = git('show', f'{MAIN}:{path}', cwd=Path('D:/CP6'))
        dest = RUN / 'inputs' / name
        dest.parent.mkdir(parents=True, exist_ok=True)
        if dest.exists():
            raise ValueError(f'Existing input: {dest}')
        dest.write_bytes(raw)
        records.append(dict(repository='GTX537/CP6', commit=MAIN, path=path,
                            copy=str(dest.relative_to(ROOT)).replace('\\', '/'), sha256=digest(raw)))
    original = Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md')
    raw = original.read_bytes()
    assert digest(raw) == '20c9a128b3f2bab0621459cc6c6494b16fb21cd8340792738ba89e90efa3bc99'
    (RUN / 'inputs/original-PLT-OBS-01.md').write_bytes(raw)
    records.append(dict(source_kind='untracked Main archive, not Git blob', path=str(original),
                        sha256=digest(raw), copy='inputs/original-PLT-OBS-01.md', lines=len(raw.decode().splitlines())))
    roadmap = json.loads(git('show', f'{MAIN}:docs/development/roadmap.json', cwd=Path('D:/CP6')))
    task = next(x for x in roadmap['implementation_tasks'] if x['id'] == 'WP02-25')
    save(RUN / 'inputs/assignment.json', task)
    admission = json.loads((RUN / 'inputs/admission.json').read_bytes())
    catalog = json.loads(git('show', f'{MAIN}:docs/development/assessments/20261011/catalogs/foundation.json', cwd=Path('D:/CP6')))
    save(RUN / 'inputs/catalog-target-17.json', catalog['targets'][17])
    save(RUN / 'intake.json', dict(captured_at=datetime.now(timezone.utc).isoformat(), main=MAIN,
        platform=BASE, source_pins=records, assignment_state=task['state'], entry_gate=task['entry_gate'],
        start_handoff='e1b65c2cfc9cb243914687c1d2af82fd82ffc233',
        predecessor_wp02_11_ancestor='34cf7ab144a02e412de121eee2d9c5083f5a96a8',
        original_ac_count=len(admission['original_ac']), original_business_acceptance='NOT_RUN',
        actions_triggered=[], actions_cancelled=[], databases_used=[], deployment_performed=False))


def scope(label):
    assignment = json.loads((RUN / 'inputs/assignment.json').read_bytes())
    allowed = assignment['assignment']['allowed_write_paths']
    changed = set(git('diff', '--name-only', f'{BASE}...HEAD').decode().splitlines())
    changed.update(git('diff', '--name-only', 'HEAD').decode().splitlines())
    changed.update(git('ls-files', '--others', '--exclude-standard').decode().splitlines())
    violations = [p for p in sorted(changed) if not any(
        p.startswith(a) if a.endswith('/') else p == a for a in allowed)]
    save(RUN / f'scope-{label}.json', dict(baseline=BASE, head=git('rev-parse', 'HEAD').decode().strip(),
        allowed=allowed, changed=sorted(changed), violations=violations))
    if violations:
        raise ValueError(f'Out-of-window paths: {violations}')
    print(f'PASS scope: {len(changed)} paths')


def snapshot(label):
    paths = set(git('ls-files', '-z').decode().split('\0'))
    paths.update(git('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0'))
    records = []
    for name in sorted(paths):
        p = ROOT / name
        if not name or not p.is_file():
            continue
        if (name.startswith(('src/', 'tests/')) and p.suffix in ('.cs', '.csproj', '.json')) or (
                '/' not in name and p.suffix in ('.props', '.targets', '.json', '.config', '.sln')):
            raw = p.read_bytes()
            records.append(dict(path=name, bytes=len(raw), sha256=digest(raw)))
    dependencies = []
    for tree in ('src', 'tests'):
        for p in sorted((ROOT / tree).glob('*/obj/project.assets.json')):
            dependencies.append(dict(path=p.relative_to(ROOT).as_posix(), sha256=digest(p.read_bytes())))
    artifacts = []
    for tree in ('src', 'tests'):
        for p in sorted((ROOT / tree).glob('*/bin/Release/net8.0/CP6.Platform*.dll')):
            artifacts.append(dict(path=p.relative_to(ROOT).as_posix(), sha256=digest(p.read_bytes())))
    save(RUN / f'snapshot-{label}.json', dict(captured_at=datetime.now(timezone.utc).isoformat(),
        head=git('rev-parse', 'HEAD').decode().strip(), baseline=BASE,
        worktree_status=git('status', '--short').decode(), os=platform.platform(),
        source_digest=digest(json.dumps(records, sort_keys=True).encode()), source_files=records,
        resolved_dependency_files=dependencies, build_artifacts=artifacts,
        note='Working bytes, including uncommitted task files. Local build/test provenance, not package trust.'))
    print(f'Snapshot {label}: {len(records)} source/config files, {len(artifacts)} artifacts')


def summarize(label):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    records = []
    for p in sorted(RUN.rglob('*.trx')):
        root = ET.parse(p).getroot()
        summary = root.find('t:ResultSummary', ns)
        counters = summary.find('t:Counters', ns)
        results = root.findall('t:Results/t:UnitTestResult', ns)
        records.append(dict(path=p.relative_to(ROOT).as_posix(), sha256=digest(p.read_bytes()),
            outcome=summary.attrib['outcome'], counters=counters.attrib,
            results=[dict(name=r.attrib['testName'], outcome=r.attrib['outcome']) for r in results]))
    save(RUN / f'trx-summary-{label}.json', dict(captured_at=datetime.now(timezone.utc).isoformat(),
        records=records, note='Includes intermediate failures; each run keeps its original scope.'))
    for r in records:
        print(r['path'], r['outcome'], r['counters'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('action', choices=['intake', 'scope', 'snapshot', 'summarize'])
    parser.add_argument('--label', default='intake')
    options = parser.parse_args()
    if options.action == 'intake':
        intake()
    else:
        globals()[options.action](options.label)
