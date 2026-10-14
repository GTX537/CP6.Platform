"""Run one local WP02-08 correction check and preserve its precise inputs/results."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

root, issue, label, selection = Path(sys.argv[1]), sys.argv[2], sys.argv[3], sys.argv[4]
evidence = root / 'docs/development/evidence/WP02-08/coord-corrections' / issue
evidence.mkdir(parents=True, exist_ok=True)
dotnet = 'C:/Users/tt/.dotnet/dotnet.exe'
project = 'tests/CP6.Platform.WP0208.PostgreSqlTests/CP6.Platform.WP0208.PostgreSqlTests.csproj'
def hashed(path):
    raw = path.read_bytes()
    return dict(path=path.relative_to(root).as_posix(), bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
def git(*args):
    return subprocess.check_output(['git','-c','core.longpaths=true','-C',str(root),*args]).decode().strip()

sources = []
for base in [root/'src',root/'tests/CP6.Platform.WP0208.PostgreSqlTests']:
    for p in base.rglob('*'):
        if p.is_file() and p.suffix in ('.cs','.csproj','.props','.json') and not {'bin','obj'} & set(p.relative_to(root).parts):
            sources.append(hashed(p))
for name in ['Directory.Build.props','Directory.Packages.props','global.json','NuGet.config']:
    sources.append(hashed(root/name))
command = [dotnet,'test',project,'--filter',selection,'--logger',f'trx;LogFileName={label}.trx','--results-directory',str(evidence)]
record = dict(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),working_directory=str(root),input_git_head=git('rev-parse','HEAD'),
    input_status=git('status','--short'),command=command,sources=sources,
    provenance='Exact working input bytes captured before execution. Dirty inputs are not attributed to the HEAD commit. Artifacts below captured after execution.')
env = dict(os.environ, CP6_WP0208_SETTINGS='D:/CP6-local-test/WP02-08/dcdfb04dda48edc0d52d638b5e7bd2f0/settings.json',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
started=time.monotonic()
with (evidence/(label+'.log')).open('w',encoding='utf-8') as log:
    result=subprocess.run(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,creationflags=0x08000000)
record.update(exit_code=result.returncode,elapsed_seconds=time.monotonic()-started)
record['dependencies']=[hashed(p) for p in root.rglob('project.assets.json') if 'obj' in p.parts]
record['artifacts']=[hashed(p) for p in (root/'tests/CP6.Platform.WP0208.PostgreSqlTests/bin/Debug/net8.0').glob('*') if p.suffix in ('.dll','.pdb','.json')]
record['log_sha256']=hashlib.sha256((evidence/(label+'.log')).read_bytes()).hexdigest()
trx=evidence/(label+'.trx')
if trx.exists():record['trx_sha256']=hashlib.sha256(trx.read_bytes()).hexdigest()
(evidence/(label+'-inputs.json')).write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(label=label,exit_code=result.returncode,elapsed_seconds=record['elapsed_seconds'],sources=len(sources),artifacts=len(record['artifacts']),evidence=str(evidence))))
print('\n'.join((evidence/(label+'.log')).read_text(encoding='utf-8').splitlines()[-20:]))
sys.exit(result.returncode)
