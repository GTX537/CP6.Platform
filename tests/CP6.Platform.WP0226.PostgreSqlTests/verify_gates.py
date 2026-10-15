"""Verify actual final test exits, TRX and unchanged relevant input sets, without rerunning tests."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import run_gate

p=argparse.ArgumentParser(__doc__)
p.add_argument('--label',required=True)
p.add_argument('--output-label',required=True)
args=p.parse_args()
current=run_gate.snapshot()
run_gate.save(run_gate.RUN/f'snapshot-verified-{args.output_label}.json',current)
reports=[]
for suite in ('unit','pg','architecture'):
    directory=run_gate.RUN/args.label/suite
    execution=json.loads((directory/'execution.json').read_bytes())
    if execution['exit_code']!=0: raise ValueError(suite+' did not exit zero')
    before=json.loads((directory/'source-before.json').read_bytes())
    after=json.loads((directory/'source-after.json').read_bytes())
    project=dict(unit='CP6.Platform.WP0226.Tests',pg='CP6.Platform.WP0226.PostgreSqlTests',architecture='CP6.Platform.ArchitectureTests')[suite]
    def project_input(path):
        if not path.startswith('tests/'+project+'/'): return False
        # Native tests execute only pg_resource.py. Evidence writers/auditors are not
        # compiled or invoked by this test assembly; their own executions are recorded separately.
        return not path.endswith('.py') or path.endswith('/pg_resource.py')
    def relevant(source):
        return {f['path']:f['sha256'] for f in source['source_files'] if f['path'].startswith('src/') or
                project_input(f['path']) or
                f['path'] in ('tests/CP6.Platform.UnitTests/TestingUtilityTests.cs','tests/CP6.Platform.UnitTests/SloEvidenceContractTests.cs') or '/' not in f['path'] or run_gate.evidence_input(f['path']) or (suite=='architecture' and run_gate.architecture_input(f['path']))}
    if relevant(before)!=relevant(after) or relevant(after)!=relevant(current): raise ValueError(suite+' relevant source/config inputs changed')
    def dependencies(source):
        return {f['path']:f['sha256'] for f in source['resolved_dependencies'] if f['path'].startswith('src/') or f['path'].startswith('tests/'+project+'/')}
    # Restore may create assets before first execution; after/current establish resolved dependency identity.
    if dependencies(after)!=dependencies(current): raise ValueError(suite+' resolved dependencies changed')
    def artifacts(source):
        return {f['path']:f['sha256'] for f in source['artifacts'] if f['path'].startswith('src/') or f['path'].startswith('tests/'+project+'/')}
    if artifacts(after)!=artifacts(current): raise ValueError(suite+' local CP6 assembly bytes changed')
    trx=directory/f'wp02-26-{suite}.trx'
    root=ET.fromstring(trx.read_bytes())
    ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters=root.find('t:ResultSummary/t:Counters',ns).attrib
    results=root.findall('t:Results/t:UnitTestResult',ns)
    if not results or any(r.attrib['outcome']!='Passed' for r in results) or int(counters['total'])!=int(counters['passed']) or int(counters['total'])!=int(counters['executed']):
        raise ValueError(suite+' has failed or unexecuted tests')
    reports.append(dict(suite=suite,execution=execution,trx=trx.relative_to(run_gate.ROOT).as_posix(),trx_sha256=run_gate.sha(trx.read_bytes()),counters=counters,relevant_inputs=len(relevant(after)),assembly_hashes=artifacts(after)))
run_gate.save(run_gate.RUN/f'verified-gates-{args.output_label}.json',dict(status='LOCAL_PASS',reports=reports,
    scope='Actual local unit/contract, owned native PG fixture and architecture only; original business acceptance NOT_RUN, production unproven.'))
print('PASS: all three actual exits/TRX and current relevant source/dependency identities match.')
