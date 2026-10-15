"""Read-only exact candidate/main workflow and fixed START transport checks."""
from pathlib import Path
import hashlib, json, re, subprocess, sys
from datetime import datetime, timezone
HERE=Path(__file__).resolve().parent; ROOT=HERE.parents[3]
def run(*args,cwd=ROOT):return subprocess.check_output(args,cwd=cwd)
def git(*args,cwd=ROOT):return run('git','-c','core.longpaths=true',*args,cwd=cwd)
def api(path):return json.loads(run('gh','api',path))
def sha(b):return hashlib.sha256(b).hexdigest()
def write(name,value):
    if '--check-only' not in sys.argv:(HERE/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
head=git('rev-parse','HEAD').decode().strip();base=git('rev-parse','origin/main').decode().strip()
assert git('ls-remote','origin','refs/heads/main').decode().split()[0]==base
changed=git('diff','--name-only',base+'...HEAD').decode().splitlines()
prefixes=('src/CP6.Platform.Messaging/TransportV1/','src/CP6.Platform.EntityFramework/TransportV1/',
          'tests/CP6.Platform.WP0212.Tests/','tests/CP6.Platform.WP0212.Fixture/','tests/wp02-12-runtime/',
          'docs/development/evidence/WP02-12/')
allowed=('src/CP6.Platform.Messaging/Cp6CloudEventValidator.cs','src/CP6.Platform.Messaging/Cp6ContractBundle.cs','docs/WP02-12-MESSAGE-TRANSPORT-V1.md')
assert changed and len(changed)<=300,(len(changed),'Full path-filter boundary must be reviewed')
assert all(p.startswith(prefixes) or p in allowed for p in changed)
workflow=[]
paths=git('ls-tree','-r','--name-only',base,'--','.github/workflows/').decode().splitlines()
assert paths==git('ls-tree','-r','--name-only',head,'--','.github/workflows/').decode().splitlines()
for path in paths:
    a=git('show',base+':'+path);b=git('show',head+':'+path);assert a==b
    text=a.decode();on=text[text.index('\non:')+1:];end=re.search(r'\n[^\s#]',on[3:]);on=on[:end.start()+3] if end else on
    events=re.findall(r'^  ([a-z_]+):',on,re.M)
    assert events and set(events)<= {'workflow_dispatch','pull_request'},(path,events)
    if 'pull_request' in events:
        assert path=='.github/workflows/p10-rfc3161-preflight.yml'
        filters=re.findall(r"^      - ['\"]?([^'\"\r\n]+)",on,re.M)
        assert set(filters)=={'.github/workflows/p10-rfc3161-preflight.yml','tests/CP6.Platform.ReleaseTests/P10Rfc3161PreflightWorkflowContractTests.cs'},filters
        assert not set(filters)&set(changed)
    workflow.append(dict(path=path,sha256=sha(a),events=events,on_block=on,dependency_uses=re.findall(r'uses:\s*([^\r\n]+)',text)))
active=[{k:r[k] for k in ('id','name','status','head_branch','event','head_sha')} for r in api('repos/GTX537/CP6.Platform/actions/runs?per_page=100')['workflow_runs'] if r['status']!='completed']
write('prepublication-workflows.json',dict(observed_utc=datetime.now(timezone.utc).isoformat(),candidate=head,protected_main=base,
    task_branch='codex/wp02-12-message-transport-20261014',changed_count=len(changed),changed=changed,workflows=workflow,active_runs=active,
    conclusion='Candidate/protected main workflow bytes identical; no push event; only existing two-file PR filter, no matching diff and full changed set within 300. No workflow changes, dispatch, rerun, cancellation, tag/package/deploy.',actions_started=0,actions_cancelled=0))
handoff=Path('D:/CP6.AI-Handoff-worktrees/wp02-12-20261014')
repo=api('repos/GTX537/CP6.AI-Handoff');permissions=api('repos/GTX537/CP6.AI-Handoff/actions/permissions')
assert repo['private'] and not permissions['enabled']
default=repo['default_branch'];default_sha=git('ls-remote','origin','refs/heads/'+default,cwd=handoff).decode().split()[0]
task_sha=git('ls-remote','origin','refs/heads/codex/dev-handoff-wp02-12',cwd=handoff).decode().split()[0]
for ref in (default_sha,task_sha,'HEAD'):
    assert not git('ls-tree','-r','--name-only',ref,'--','.github/workflows/',cwd=handoff).strip()
start='cd3e3ca14a1cf31bd6efbf62b32e2cd6602b2f8d'
path='handoffs/CP6-DEV-20261012/tasks/WP02-12/messages/20261015T002253Z-c-start/MESSAGE.md'
remote=run('gh','api','repos/GTX537/CP6.AI-Handoff/contents/'+path+'?ref='+start,'-H','Accept: application/vnd.github.raw+json')
assert remote==(handoff/path).read_bytes()==git('show',start+':'+path,cwd=handoff)
assert len(remote)==2039 and sha(remote)=='ab5230d1f399784f5fc901c942567ed9ed241766a246884dfc3c2c3474247ac8'
write('start-transport.json',dict(observed_utc=datetime.now(timezone.utc).isoformat(),private=True,actions_enabled=False,
    default_branch=default,default_commit=default_sha,task_commit=task_sha,start_commit=start,path=path,bytes=len(remote),sha256=sha(remote),remote_git_working_bytes_equal=True,workflows=[]))
print(f'Candidate {head}; main {base}; {len(changed)} changed files; {len(workflow)} identical workflows; {len(active)} active runs; START raw bytes verified.')
