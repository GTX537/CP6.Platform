"""Preserve original AC identities; attach only explicit, scoped local test evidence."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import evidence

parser = argparse.ArgumentParser(__doc__)
parser.add_argument('--label', required=True)
parser.add_argument('--bindings', type=Path)
args = parser.parse_args()
admission = json.loads((evidence.RUN / 'inputs/admission.json').read_bytes())
catalog = json.loads((evidence.RUN / 'inputs/catalog-target-17.json').read_bytes())
source = (evidence.RUN / 'inputs/original-PLT-OBS-01.md').read_bytes()
assert evidence.digest(source) == '20c9a128b3f2bab0621459cc6c6494b16fb21cd8340792738ba89e90efa3bc99'
cases = {c['id']: c for c in catalog['cases']}
assert len(cases) == 32 and set(cases) == {c['id'] for c in admission['original_ac']}
bindings = {} if not args.bindings else json.loads(args.bindings.read_bytes())
assert set(bindings).issubset(cases)
rows = []
for row in admission['original_ac']:
    original = cases[row['id']]
    assert row['assertion'] == original['assertion']
    assert row['source_line'] == original['source_line']
    local = bindings.get(row['id'], {})
    if local.get('result') in ('LOCAL_PASS', 'REGRESSION_PASS'):
        assert local.get('evidence'), f"Missing local proof: {row['id']}"
        for proof in local['evidence']:
            path = (evidence.ROOT / proof['trx']).resolve()
            assert path.is_relative_to(evidence.RUN.resolve()), 'Evidence must belong to this run'
            document = ET.parse(path).getroot()
            results = document.findall('{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Results/{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult')
            matches = [r for r in results if r.attrib['testName'] == proof['test_name']]
            assert len(matches) == 1 and matches[0].attrib['outcome'] == 'Passed', proof
            proof['trx_sha256'] = evidence.digest(path.read_bytes())
    rows.append(dict(id=row['id'], assertion=row['assertion'], source_line=row['source_line'],
        source_sha256=original['source_sha256'], original_business_acceptance='NOT_RUN',
        responsibility=row['responsibility'], local_result=local.get('result', 'NOT_RUN'),
        local_scope=local.get('scope', 'Awaiting candidate execution; no acceptance promotion.'),
        evidence=local.get('evidence', []), remaining=local.get('remaining', row['responsibility'])))
evidence.save(evidence.RUN / f'acceptance-{args.label}.json', dict(target='PLT-OBS-01',
    count=32, original_business_acceptance='NOT_RUN', production_acceptance='UNPROVEN',
    task_scope='Platform library and isolated loopback host only; no Main/CRM or real Owner adoption.',
    cases=rows))
print('PASS: 32 exact original AC preserved with separate local coverage')
