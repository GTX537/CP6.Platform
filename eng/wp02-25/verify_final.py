"""Verify final gate records against unchanged relevant working inputs and real TRX results."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import evidence

parser = argparse.ArgumentParser(__doc__)
parser.add_argument('--label', required=True)
parser.add_argument('--unit', type=Path, required=True)
parser.add_argument('--host', type=Path, required=True)
parser.add_argument('--architecture', type=Path, required=True)
args = parser.parse_args()
evidence.snapshot(f'verification-{args.label}')
current = json.loads((evidence.RUN / f'snapshot-verification-{args.label}.json').read_bytes())
projects = {'unit': 'CP6.Platform.UnitTests', 'host': 'CP6.Platform.AspNetCoreTests',
            'architecture': 'CP6.Platform.ArchitectureTests'}
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
reports = []
for suite in projects:
    directory = getattr(args, suite)
    execution = json.loads((directory / 'execution.json').read_bytes())
    assert execution['exit_code'] == 0, f'{suite} failed'
    before = json.loads((evidence.RUN / execution['source_before']).read_bytes())
    after = json.loads((evidence.RUN / execution['source_after']).read_bytes())
    def relevant(row):
        name = row['path']
        return not name.startswith('tests/') or name.startswith(f'tests/{projects[suite]}/')
    left = {r['path']: r['sha256'] for r in before['source_files'] if relevant(r)}
    right = {r['path']: r['sha256'] for r in after['source_files'] if relevant(r)}
    assert left == right, f'{suite} relevant source changed during execution'
    now = {r['path']: r['sha256'] for r in current['source_files'] if relevant(r)}
    assert now == right, f'{suite} relevant inputs differ from final working tree'
    for name, sha in right.items():
        assert evidence.digest((evidence.ROOT / name).read_bytes()) == sha, f'{suite} stale input: {name}'
    for record in after['resolved_dependency_files']:
        # Architecture has no runtime project references; other gates use the same production closure.
        name = record['path']
        if name.startswith(f'tests/{projects[suite]}/') or (suite != 'architecture' and name.startswith('src/')):
            assert evidence.digest((evidence.ROOT / name).read_bytes()) == record['sha256'], f'Stale restore: {name}'
    trx = list(directory.glob('*.trx'))
    assert len(trx) == 1
    root = ET.parse(trx[0]).getroot()
    summary = root.find('t:ResultSummary', ns)
    counters = {k: int(v) for k, v in summary.find('t:Counters', ns).attrib.items()}
    assert summary.attrib['outcome'] == 'Completed'
    assert counters['total'] > 0 and counters['passed'] == counters['total'] == counters['executed']
    assert counters.get('failed', 0) == counters.get('notExecuted', 0) == 0
    results = root.findall('t:Results/t:UnitTestResult', ns)
    assert len(results) == counters['total'] and all(r.attrib['outcome'] == 'Passed' for r in results)
    if suite != 'architecture':
        assert any('.ObservabilityV1.' in r.attrib['testName'] for r in results), f'{suite} missed new capability'
    reports.append(dict(suite=suite, execution=str(directory / 'execution.json'),
        input_snapshot=execution['source_after'], relevant_inputs_verified=len(right),
        trx=str(trx[0]), trx_sha256=evidence.digest(trx[0].read_bytes()), counters=counters))
evidence.save(evidence.RUN / f'final-verification-{args.label}.json', dict(
    status='LOCAL_GATE_PASS', reports=reports,
    scope='Exact local source/dependency and executed test scope; no production or business acceptance.'))
print('PASS: final relevant inputs, command exit status, actual TRX cases and zero skips verified')
