"""Verify retained local evidence and publication path safety, without running tests/Actions."""
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
EVIDENCE = Path(__file__).resolve().parent
BASE = '30bd23af6808d217a23878bd9437513043c52834'
SOURCE = '708253360269396a43c2b25cce1c87cd9f59f9d2'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def git(*args):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', '-C', str(ROOT), *args])


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def main():
    provenance = json.loads((EVIDENCE / 'input-provenance.json').read_bytes())
    input_results = []
    for asset in provenance['imported']:
        path = 'tests/CP6.Platform.UnitTests/TestData/S01/' + asset['file']
        raw = (ROOT / path).read_bytes()
        assert sha(raw) == asset['sha256'], path
        assert git('cat-file', 'blob', SOURCE + ':' + path) == raw, path
        input_results.append(dict(path=path, sha256=sha(raw), committed_bytes_match=True))

    # Source was committed immediately after task-pass without changing tested inputs.
    # These compare Git-filtered bytes; imported raw resources are additionally checked above.
    changed = git('diff', '--name-only', BASE, SOURCE).decode().splitlines()
    code_inputs = [p for p in changed if p.startswith(('src/', 'tests/'))]
    for path in code_inputs:
        assert git('hash-object', '--path=' + path, path).strip() == git('rev-parse', SOURCE + ':' + path).strip(), path
    assert not git('diff', SOURCE, '--', 'src', 'tests', 'contracts', 'Directory.Build.props',
                   'Directory.Packages.props', 'global.json', 'NuGet.Config').strip(), 'Test inputs drifted'

    trx = ET.parse(EVIDENCE / 'test-results/task-pass.trx').getroot()
    counters = trx.find('t:ResultSummary/t:Counters', NS).attrib
    assert counters['total'] == counters['passed'] == counters['executed'] == '110'
    assert counters['failed'] == counters['notExecuted'] == '0'
    results = trx.findall('t:Results/t:UnitTestResult', NS)
    assert len(results) == 110 and all(r.attrib['outcome'] == 'Passed' for r in results)
    actual_ids = [re.search(r'id: "([A-Z0-9-]+)"', r.attrib['testName']).group(1)
                  for r in results if '.FixedVector_UsesProductionBoundary(' in r.attrib['testName']]
    vectors = json.loads((ROOT / 'tests/CP6.Platform.UnitTests/TestData/S01/vectors.json').read_bytes())
    package = json.loads((ROOT / 'tests/CP6.Platform.UnitTests/TestData/S01/profile-inputs.json').read_bytes())
    profiles = {p['id'] for p in package['profiles'] if 'Platform:S01-09' in p['target_consumers']}
    cases = [c for c in vectors['cases'] if c['profile_id'] in profiles]
    assert len(cases) == 35 and Counter(actual_ids) == Counter(c['id'] for c in cases)

    all_paths = sorted(set(git('diff', '--name-only', 'origin/main').decode().splitlines() +
                           git('ls-files', '--others', '--exclude-standard').decode().splitlines()))
    prefixes = ('src/CP6.Platform.Messaging/NativeContracts/', 'tests/CP6.Platform.UnitTests/TestData/S01/',
                'docs/development/evidence/S01-09/')
    explicit = {'src/CP6.Platform.Messaging/Cp6ContractBundle.cs', 'src/CP6.Platform.Messaging/Cp6CloudEventValidator.cs',
                'tests/CP6.Platform.UnitTests/CloudEventContractTests.cs', 'tests/CP6.Platform.UnitTests/NativeProfileContractTests.cs',
                'tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj'}
    assert all(p in explicit or p.startswith(prefixes) for p in all_paths), 'Write allowlist violation'

    workflow_checks = []
    guarded_paths = {'.github/workflows/p10-rfc3161-preflight.yml',
                     'tests/CP6.Platform.ReleaseTests/P10Rfc3161PreflightWorkflowContractTests.cs'}
    for ref in ('HEAD', 'origin/main'):
        for path in git('ls-tree', '-r', '--name-only', ref, '--', '.github/workflows').decode().splitlines():
            raw = git('show', ref + ':' + path)
            lines = raw.decode().splitlines()
            start = lines.index('on:')
            end = next((i for i in range(start + 1, len(lines))
                        if lines[i] and not lines[i].startswith((' ', '#'))), len(lines))
            block = '\n'.join(lines[start + 1:end])
            events = re.findall(r'^  ([a-z_]+):', block, re.M)
            if path == '.github/workflows/p10-rfc3161-preflight.yml':
                assert events == ['workflow_dispatch', 'pull_request']
                filters = set(re.findall(r'^      - "([^"]+)"', block, re.M))
                assert filters == guarded_paths and not (set(all_paths) & filters)
            else:
                assert events == ['workflow_dispatch'], (ref, path, events)
            workflow_checks.append(dict(ref=ref, path=path, sha256=sha(raw), events=events, on_block=block))

    binaries = []
    for path in ['src/CP6.Platform.Messaging/bin/Debug/net8.0/CP6.Platform.Messaging.dll',
                 'tests/CP6.Platform.UnitTests/bin/Debug/net8.0/CP6.Platform.UnitTests.dll']:
        binaries.append(dict(path=path, sha256=sha((ROOT / path).read_bytes())))
    historical_runs = []
    for path in sorted((EVIDENCE / 'test-results').glob('*.trx')):
        result = ET.parse(path).getroot()
        historical_runs.append(dict(path=path.relative_to(ROOT).as_posix(), sha256=sha(path.read_bytes()),
                                    counters=result.find('t:ResultSummary/t:Counters', NS).attrib,
                                    times=result.find('t:Times', NS).attrib))

    report = dict(result='PASS', observed_at=datetime.now(timezone.utc).isoformat(),
                  source_commit=SOURCE, execution_head=BASE,
                  source_binding='task-pass executed working-tree source subsequently committed unchanged as source_commit; Git-filtered source identity and raw imported bytes checked. This script is evidence verification, not a new test execution.',
                  environment='Windows / .NET SDK 8.0.424 / net8.0 / local tests only',
                  executed_command='dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --no-restore --filter "FullyQualifiedName~CloudEventContractTests|FullyQualifiedName~NativeProfileContractTests|FullyQualifiedName~MessagingTelemetryTests|FullyQualifiedName~DaprKafkaContractTests|FullyQualifiedName~TraceContextContractTests" --logger "trx;LogFileName=task-pass.trx" --results-directory docs/development/evidence/S01-09/test-results',
                  input_package_version=package['package_version'], input_files=input_results,
                  test_counters=counters, test_runs=historical_runs, build_artifacts=binaries,
                  vector_results=[dict(id=c['id'], profile_id=c['profile_id'], operation=c['operation'],
                                       verification_level=c['verification_level'], result='PASS',
                                       scope='Local contract subassertion only') for c in cases],
                  workflow_refs={ref: git('rev-parse', ref).decode().strip() for ref in ('HEAD', 'origin/main')},
                  workflow_checks=workflow_checks, publication_paths=all_paths,
                  actions_triggered=0, actions_cancelled=0, workflow_changes=0,
                  business_acceptance='NOT_RUN', package_publication='NOT_RUN', database='NOT_RUN', deployment='NOT_RUN')
    (EVIDENCE / 'validation.json').write_bytes((json.dumps(report, ensure_ascii=False, indent=2) + '\n').encode())
    print('PASS: 110 local results; exact 35 vectors; 5 raw asset pins; allowed files; both workflow path boundaries.')


if __name__ == '__main__':
    main()
