"""Unsigned TaskLocal packaging only; dirty candidate identity is recorded, never formal publication."""
import hashlib, json, os, pathlib, subprocess, zipfile, datetime, shutil
ROOT = pathlib.Path(__file__).resolve().parents[4]
EVIDENCE = ROOT / 'docs/development/evidence/WP02-02/formal-registration/implementation'
DOTNET = pathlib.Path(os.environ.get('CP6_DOTNET') or shutil.which('dotnet') or 'dotnet')
def sha(data): return hashlib.sha256(data).hexdigest()
def capture(args): return subprocess.check_output(args, cwd=ROOT, text=True).strip()
head = capture(['git', 'rev-parse', 'HEAD'])
attempt = str(int(datetime.datetime.now(datetime.timezone.utc).timestamp()*1000000))
version = '0.10.3-wp02.27.g' + head + '.' + attempt
output = ROOT / 'artifacts/wp02-02/formal-registration/implementation' / attempt
output.mkdir(parents=True)
env = dict(os.environ, PATH=str(DOTNET.parent) + os.pathsep + os.environ['PATH'])
inputs = sorted(set([*ROOT.glob('src/CP6.Platform.Release/**/*.cs'), *ROOT.glob('contracts/release/**/*'),
    *(ROOT / p for p in ['Directory.Build.props', 'Directory.Packages.props', 'global.json', 'NuGet.config',
      'src/CP6.Platform.Release/CP6.Platform.Release.csproj', 'README.md', '.gitattributes'])]))
inputs = [p for p in inputs if p.is_file() and not any(part in ('bin','obj') for part in p.parts)]
input_hashes = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in inputs}
command = [str(DOTNET), 'pack', 'src/CP6.Platform.Release/CP6.Platform.Release.csproj', '-c', 'Release',
    '--no-restore', '-m:1', '-p:UseSharedCompilation=false', '-p:PackageVersion='+version,
    '-p:SourceRevisionId='+head, '-p:RepositoryCommit='+head, '-p:IncludeSymbols=false', '--output', str(output)]
with (EVIDENCE/'tasklocal-pack.log').open('w', encoding='utf-8') as log:
    result = subprocess.run(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
record = {'status': 'UNSIGNED_TASKLOCAL_DIAGNOSTIC_ONLY', 'baseGitSha': head,
    'sourceIdentity': 'dirty working-tree candidate; baseGitSha alone does not identify packaged source',
    'sdk': capture([str(DOTNET), '--version']), 'command': command, 'exitCode': result.returncode,
    'sourceFileSha256': input_hashes, 'dependencies': json.loads((ROOT/'src/CP6.Platform.Release/obj/project.assets.json').read_text())['libraries'],
    'gitDiffSha256': sha(subprocess.check_output(['git','diff','--binary','--','src/CP6.Platform.Release','contracts/release'], cwd=ROOT))}
if result.returncode == 0:
    package = next(output.glob('*.nupkg'))
    with zipfile.ZipFile(package) as archive:
        assert '.signature.p7s' not in archive.namelist()
        names = ['contracts/release/v1/formal-package-publication.v1.schema.json', 'contracts/release/capabilities/v1/capability-matrix.v1.schema.json']
        record['schemas'] = {name: {'sourceSha256':sha((ROOT/name).read_bytes()), 'packageSha256':sha(archive.read(name))} for name in names}
        record['nuspec'] = archive.read('CP6.Platform.Release.nuspec').decode()
    record.update(packagePath=str(package), packageSha256=sha(package.read_bytes()), version=version)
(EVIDENCE/'tasklocal-pack.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:record[k] for k in ('status','exitCode','sdk')}))
raise SystemExit(result.returncode)
