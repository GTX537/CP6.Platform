"""Local correction gate: immutable command, source/dependency/artifact hashes and real TRX."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
parser.add_argument('--issue', required=True)
parser.add_argument('--stage', required=True)
parser.add_argument('--filter', required=True)
args = parser.parse_args()
root = args.root.resolve()
directory = root / 'docs/development/evidence/WP02-25/20261014T052841Z/coord-corrections' / args.issue / args.stage
directory.mkdir(parents=True, exist_ok=False)
sdk = 'C:/Users/tt/.dotnet/dotnet.exe'

def git(*command):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', *command], cwd=root).decode()

def record(path):
    return {'path': path.relative_to(root).as_posix(), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}

def snapshot():
    files = set(git('ls-files', '-z').split('\0')) | set(git('ls-files', '--others', '--exclude-standard', '-z').split('\0'))
    source = [record(root / name) for name in sorted(files) if name and (root / name).is_file() and (
        (name.startswith(('src/', 'tests/')) and Path(name).suffix in ('.cs', '.csproj', '.json')) or
        ('/' not in name and Path(name).suffix in ('.props', '.targets', '.json', '.config', '.sln')))]
    return {'at_utc': datetime.now(timezone.utc).isoformat(), 'head': git('rev-parse', 'HEAD').strip(),
            'working_status': git('status', '--short'), 'source_files': source,
            'source_digest': hashlib.sha256(json.dumps(source, sort_keys=True).encode()).hexdigest(),
            'dependencies': [record(p) for tree in ('src', 'tests') for p in sorted((root / tree).glob('*/obj/project.assets.json'))],
            'artifacts': [record(p) for tree in ('src', 'tests') for p in sorted((root / tree).glob('*/bin/Release/net8.0/CP6.Platform*.dll'))]}

def save(name, data):
    (directory / name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

save('source-before.json', snapshot())
command = [sdk, 'test', 'tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj', '-c', 'Release',
           '--filter', args.filter, '--logger', 'trx;LogFileName=results.trx', '--results-directory', str(directory)]
started = datetime.now(timezone.utc).isoformat()
sdk_info = subprocess.check_output([sdk, '--info'], cwd=root).decode(errors='replace')
with (directory / 'console.log').open('wb') as log:
    process = subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT)
save('source-after.json', snapshot())
summary = None
if (directory / 'results.trx').exists():
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    doc = ET.parse(directory / 'results.trx')
    summary = {'counters': doc.find('t:ResultSummary/t:Counters', ns).attrib,
               'results': [{'name': x.attrib['testName'], 'outcome': x.attrib['outcome'],
                            'failure': x.findtext('t:Output/t:ErrorInfo/t:Message', default='', namespaces=ns)}
                           for x in doc.findall('t:Results/t:UnitTestResult', ns)],
               'trx_sha256': hashlib.sha256((directory / 'results.trx').read_bytes()).hexdigest()}
save('execution.json', {'started_at_utc': started, 'ended_at_utc': datetime.now(timezone.utc).isoformat(),
                      'command': command, 'worktree': str(root), 'exit_code': process.returncode,
                      'sdk_info': sdk_info, 'summary': summary,
                      'scope': 'SDK8 local focused issue regression only; no DB/Actions/production or actual Owner adoption.'})
print((directory / 'console.log').read_text(encoding='utf-8', errors='replace')[-10000:])
print(json.dumps({'evidence': str(directory), 'exit_code': process.returncode, 'summary': summary}, ensure_ascii=False))
sys.exit(process.returncode)
