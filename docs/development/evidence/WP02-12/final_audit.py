"""Static delivery audit: task scope, exact recorded source bytes, syntax, links and generated-secret exclusion."""
import argparse,ast,hashlib,json,re,subprocess,sys
from pathlib import Path
from datetime import datetime,timezone
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[3]
sys.path.insert(0,str(ROOT/'tests/wp02-12-runtime'))
import runtime as r
a=argparse.ArgumentParser();a.add_argument('--owner',required=True);a.add_argument('--settings',required=True);args=a.parse_args()
c=r.load(args.owner,args.settings);s=json.loads((Path(c['RunDirectory'])/'transport/secrets.json').read_bytes())
secrets=[c['AdminPassword'],c['RuntimePassword'],*s['passwords'].values(),*s['tokens'].values()]
prefixes=('src/CP6.Platform.Messaging/TransportV1/','src/CP6.Platform.EntityFramework/TransportV1/',
          'tests/CP6.Platform.WP0212.Tests/','tests/CP6.Platform.WP0212.Fixture/','tests/wp02-12-runtime/','docs/development/evidence/WP02-12/')
allowed=('src/CP6.Platform.Messaging/Cp6CloudEventValidator.cs','src/CP6.Platform.Messaging/Cp6ContractBundle.cs','docs/WP02-12-MESSAGE-TRANSPORT-V1.md')
files=subprocess.check_output(['git','diff','--cached','--name-only'],cwd=ROOT).decode().splitlines()
assert files and len(files)<=300
for name in files:
    assert name.startswith(prefixes) or name in allowed,name
    assert not any(part in ('bin','obj','__pycache__') for part in Path(name).parts),name
    raw=(ROOT/name).read_bytes()
    assert not any(value.encode() in raw for value in secrets),'Generated credential in staged source'
    if name.endswith('.py'):ast.parse(raw,filename=name)
    if name.endswith('.json'):json.loads(raw)
    if name.endswith('.md'):
        for target in re.findall(r'\[[^\]]+\]\(([^)]+)\)',raw.decode('utf-8-sig')):
            if '://' in target or target.startswith('#'):continue
            assert ((ROOT/name).parent/target.split('#')[0]).exists(),(name,target)
inputs=json.loads((HERE/'validation-inputs.json').read_bytes())['inputs']
for row in inputs:
    assert hashlib.sha256((ROOT/row['path']).read_bytes()).hexdigest()==row['workingSha256'],row['path']
subprocess.run(['git','-c','core.longpaths=true','diff','--cached','--check'],cwd=ROOT,check=True)
result={'observed_utc':datetime.now(timezone.utc).isoformat(),'staged_files':len(files),'recorded_inputs_verified':len(inputs),
        'scope_and_300_file_boundary':True,'generated_secret_scan':True,'python_syntax_and_json':True,'markdown_local_links':True,
        'staged_diff_check_exit':0,'no_build_or_runtime_rerun_claim':True}
(HERE/'final-static-audit.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result))
