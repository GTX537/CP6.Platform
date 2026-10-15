"""Export only task evidence, never credentials/private settings or raw container inspect.
Raw private logs remain retained. Secret scanning precedes every public file write.
"""
import argparse,base64,hashlib,json,sys,xml.etree.ElementTree as ET
from pathlib import Path
HERE=Path(__file__).resolve().parent; ROOT=HERE.parents[3]
sys.path.insert(0,str(ROOT/'tests/wp02-12-runtime'))
import runtime as r
a=argparse.ArgumentParser();a.add_argument('--owner',required=True);a.add_argument('--settings',required=True);args=a.parse_args()
c=r.load(args.owner,args.settings);private=Path(c['RunDirectory']);transport=private/'transport'
secret=json.loads((transport/'secrets.json').read_bytes())
forbidden=[c['AdminPassword'],c['RuntimePassword'],*secret['passwords'].values(),*secret['tokens'].values()]
def hash(b):return hashlib.sha256(b).hexdigest()
def check(b):
    for value in forbidden:
        if value.encode() in b:raise RuntimeError('Secret found; export refused')
def put(name,b):
    check(b);p=HERE/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
def write(name,o):put(name,(json.dumps(o,ensure_ascii=False,indent=2)+'\n').encode())
def copy(name,p):put(name,p.read_bytes())

complete=[]
for path in sorted(transport.glob('acceptance-*')):
    if (path/'summary.json').exists():complete.append(path)
assert len(complete)==2,'Expected exactly original suite and targeted regression; inspect added runs explicitly'
copy('runtime/acceptance.json',complete[0]/'results.json')
copy('runtime/targeted-review-regression.json',complete[1]/'results.json')
summary=[json.loads((p/'summary.json').read_bytes()) for p in complete]
write('runtime/reuse-boundaries.json',{'runs':summary,
    'original_runtime_result_preserved':True,
    'superseded_case':'wrong-key-no-success-no-effect (original RETRY) superseded by targeted wrong-native-key-is-durably-isolated; this case proves durable isolation, not an exported receive DROP observation',
    'reuse':'Unchanged actual producer entitlement, transport exact bytes, genuine token/ACL negatives, same-id retry, corrected Outbox commit/send budget and native service failure behavior; no rerun claimed.',
    'targeted_changes':'Quarantine nativeReference lookup and wrong-key permanent isolation only. Real repeated invalid raw/id/hash at distinct offsets retains both native observations and reaches DROP.'})
copy('runtime/acl-proof.txt',transport/'acl-proof.txt')
copy('runtime/resource-ownership.json',private/'ownership.json')
copy('runtime/fixture-key-migration.json',transport/'review-fixture-key-migration.json')
copy('runtime/public-plan.json',transport/'public-plan.json')
for name in ('native-identity-before-stop.json','stop-result.json','final-acl-proof.txt'):
    copy('runtime/'+name,transport/name)
copy('runtime/final-observed-records.json',sorted(transport.glob('observed-*.json'))[-1])

logs=[];raw_manifest=[]
for path in sorted((private/'runtime-logs').glob('*.json')):
    item=json.loads(path.read_bytes()); row={'log':path.name,'command':item['args'],'exit':item['exit']}
    for stream in ('stdout','stderr'):
        file=path.with_suffix('.'+stream);b=file.read_bytes();raw_manifest.append({'path':str(file.relative_to(private)),'bytes':len(b),'sha256':hash(b)})
        row[stream+'Sha256']=hash(b)
        if 'inspect' in item['args']:
            row[stream+'Disposition']='Private inspect may contain credentials; raw retained privately, hash only'
        else:
            check(b);row[stream+'Base64']=base64.b64encode(b).decode()
    logs.append(row)
write('runtime/commands.json',logs)
write('runtime/private-log-identities.json',raw_manifest)
failures=[]
for path in sorted(transport.glob('acceptance-*')):
    if not (path/'summary.json').exists():
        failures.append({'run':path.name,'results':json.loads((path/'results.json').read_bytes()),'complete':False})
write('runtime/incomplete-runs.json',failures)

inputs=[]
for path in sorted(ROOT.rglob('*')):
    rel=path.relative_to(ROOT).as_posix()
    if not path.is_file() or any(x in path.parts for x in ('bin','obj','.git','__pycache__')):continue
    if (rel.startswith('src/') and path.suffix in ('.cs','.csproj')) or rel in ('Directory.Build.props','Directory.Packages.props','global.json','NuGet.config') or rel.startswith(('contracts/','tests/CP6.Platform.WP0212.','tests/wp02-12-runtime/')):
        b=path.read_bytes();check(b);inputs.append({'path':rel,'bytes':len(b),'workingSha256':hash(b)})
write('validation-inputs.json',{'baseline':'b28db763f70ab6f164cb7c87d6ce49a6d97267f1','inputs':inputs,'identity':'Working file bytes at final validation; source SHA recorded separately after audit commit, no rebuilt-after-commit claim'})
artifacts=[]
for project in ('CP6.Platform.WP0212.Fixture','CP6.Platform.WP0212.Tests','CP6.Platform.ArchitectureTests'):
    for path in sorted((ROOT/'tests'/project/'bin/Release/net8.0').glob('*')):
        if path.is_file():artifacts.append({'path':path.relative_to(ROOT).as_posix(),'bytes':path.stat().st_size,'sha256':hash(path.read_bytes())})
write('validation-artifacts.json',artifacts)
runs=[]
for path in sorted(HERE.rglob('*.trx')):
    tree=ET.parse(path);ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters=tree.find('.//t:Counters',ns)
    runs.append({'path':path.relative_to(HERE).as_posix(),'sha256':hash(path.read_bytes()),'counters':counters.attrib if counters is not None else {},
        'tests':[{'name':x.attrib['testName'],'outcome':x.attrib['outcome']} for x in tree.findall('.//t:UnitTestResult',ns)]})
write('test-runs.json',runs)
print('Exported native evidence, private-log hashes, source/artifact inputs and TRX inventory; secret checks passed')
