"""Run only the assigned local gate, preserving immutable command/output/input evidence."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import evidence

SDK = 'C:/Users/tt/.dotnet/dotnet.exe'
GATES = {
    'unit': ('CP6.Platform.UnitTests', 'FullyQualifiedName~ObservabilityV1|FullyQualifiedName~TelemetryConventionTests|FullyQualifiedName~ReleaseIdentityTests|FullyQualifiedName~SloEvidenceContractTests'),
    'host': ('CP6.Platform.AspNetCoreTests', 'FullyQualifiedName~ObservabilityV1|FullyQualifiedName~OperationalEndpointTests|FullyQualifiedName~ObservabilityRegistrationTests|FullyQualifiedName~ObservabilityEndToEndTests|FullyQualifiedName~TraceContextDistributedPropagatorTests'),
    'architecture': ('CP6.Platform.ArchitectureTests', None),
}

parser = argparse.ArgumentParser(__doc__)
parser.add_argument('suite', choices=GATES)
parser.add_argument('--label', required=True)
args = parser.parse_args()
directory = evidence.RUN / args.label / args.suite
directory.mkdir(parents=True, exist_ok=False)
project, selection = GATES[args.suite]
command = [SDK, 'test', f'tests/{project}/{project}.csproj', '-c', 'Release',
           '--logger', f'trx;LogFileName=wp02-25-{args.suite}.trx',
           '--results-directory', str(directory)]
if selection:
    command += ['--filter', selection]
label = f'{args.label}-{args.suite}'
evidence.snapshot(label + '-before')
started = datetime.now(timezone.utc).isoformat()
sdk_info = subprocess.check_output([SDK, '--info'], cwd=evidence.ROOT).decode(errors='replace')
with (directory / 'console.log').open('wb') as log:
    process = subprocess.run(command, cwd=evidence.ROOT, stdout=log, stderr=subprocess.STDOUT)
evidence.snapshot(label + '-after')
evidence.save(directory / 'execution.json', dict(started_at=started,
    ended_at=datetime.now(timezone.utc).isoformat(), command=command,
    working_directory=str(evidence.ROOT), exit_code=process.returncode,
    sdk_info=sdk_info, source_before=f'snapshot-{label}-before.json',
    source_after=f'snapshot-{label}-after.json',
    scope='Local library/loopback gate only. No DB/Actions/package/environment/production acceptance.'))
print((directory / 'console.log').read_text(encoding='utf-8', errors='replace')[-7000:])
sys.exit(process.returncode)
