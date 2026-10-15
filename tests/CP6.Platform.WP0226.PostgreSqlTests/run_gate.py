"""Task-private local gate and provenance runner. Never prints private PG settings."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
RUN = ROOT/'docs/development/evidence/WP02-26/20261015T001945Z'
DOTNET = 'C:/Users/tt/.dotnet/dotnet.exe'


def evidence_input(name):
    return name.startswith('contracts/observability/slo-evidence/v1/') or name == 'docs/development/evidence/WP02-26/20261015T001945Z/inputs/vectors.json'


def architecture_input(name):
    return name in ('eng/verify.ps1','eng/pack-release.ps1','.github/workflows/publish-alpha.yml','README.md','VERSION','docs/P10-RELEASE-GOVERNANCE.md') or name.startswith(('docs/P08-','docs/runbooks/P08-'))


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def git(*args):
    return subprocess.check_output(['git','-c','core.longpaths=true',*args],cwd=ROOT)


def save(path,obj):
    with path.open('x',encoding='utf-8',newline='\n') as file:
        json.dump(obj,file,indent=2)
        file.write('\n')


def snapshot():
    paths=set(git('ls-files','-co','--exclude-standard','-z').decode().split('\0'))
    source=[]
    for name in sorted(paths):
        p=ROOT/name
        if not name or not p.is_file(): continue
        if ((name.startswith(('src/','tests/')) and p.suffix in ('.cs','.csproj','.py','.json','.props','.config')) or
            ('/' not in name and p.suffix in ('.props','.targets','.config','.json','.sln')) or evidence_input(name) or architecture_input(name)):
            raw=p.read_bytes(); source.append(dict(path=name,bytes=len(raw),sha256=sha(raw)))
    deps=[]; artifacts=[]
    for tree in ('src','tests'):
        for path in sorted((ROOT/tree).glob('*/obj/project.assets.json')):
            deps.append(dict(path=path.relative_to(ROOT).as_posix(),sha256=sha(path.read_bytes())))
        for path in sorted((ROOT/tree).glob('*/bin/Release/net8.0/CP6.Platform*.dll')):
            artifacts.append(dict(path=path.relative_to(ROOT).as_posix(),sha256=sha(path.read_bytes())))
    return dict(utc=datetime.now(timezone.utc).isoformat(),head=git('rev-parse','HEAD').decode().strip(),
                source_digest=sha(json.dumps(source,sort_keys=True).encode()),source_files=source,
                resolved_dependencies=deps,artifacts=artifacts,note='Actual local working bytes, not production/package trust.')


if __name__ == '__main__':
    p=argparse.ArgumentParser(__doc__)
    p.add_argument('suite',choices=['unit','pg','architecture','legacy'])
    p.add_argument('--label',required=True)
    args=p.parse_args()
    target=(RUN/args.label/args.suite).resolve()
    if not target.is_relative_to(RUN.resolve()) or target.exists(): raise ValueError('New contained evidence directory required')
    target.mkdir(parents=True)
    save(target/'source-before.json',snapshot())
    sdk=subprocess.run([DOTNET,'--info'],cwd=ROOT,capture_output=True)
    (target/'sdk-info.txt').write_bytes(sdk.stdout+sdk.stderr)
    projects=dict(unit='CP6.Platform.WP0226.Tests',pg='CP6.Platform.WP0226.PostgreSqlTests',architecture='CP6.Platform.ArchitectureTests',legacy='CP6.Platform.UnitTests')
    project=projects[args.suite]
    cmd=[DOTNET,'test',f'tests/{project}/{project}.csproj','-c','Release','--logger',f'trx;LogFileName=wp02-26-{args.suite}.trx','--results-directory',str(target)]
    if args.suite=='legacy': cmd+=['--filter','FullyQualifiedName~TestingUtilityTests|FullyQualifiedName~SloEvidenceContractTests']
    started=datetime.now(timezone.utc).isoformat()
    with (target/'console.log').open('xb') as log:
        result=subprocess.run(cmd,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,env=os.environ.copy())
    save(target/'source-after.json',snapshot())
    save(target/'execution.json',dict(command=cmd,cwd=str(ROOT),started_at=started,ended_at=datetime.now(timezone.utc).isoformat(),
         exit_code=result.returncode,settings_supplied=bool(os.environ.get('CP6_WP0226_SETTINGS')) if args.suite=='pg' else False))
    sys.stdout.reconfigure(encoding='utf-8')
    print((target/'console.log').read_text(encoding='utf-8',errors='replace'))
    sys.exit(result.returncode)
