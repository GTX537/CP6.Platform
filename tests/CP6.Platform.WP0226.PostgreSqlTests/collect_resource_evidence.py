"""Publish reviewed own-cluster lifecycle evidence after smart-stop; never copy settings or credentials."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import pg_resource as pg
import run_gate

p=argparse.ArgumentParser(__doc__)
p.add_argument('--settings',required=True)
p.add_argument('--label',required=True)
args=p.parse_args()
c=pg.load(args.settings)
directory=pg.paths(c)
control=pg.disk_identity(c)
pg.require(not (directory/'data/postmaster.pid').exists(),'Owned cluster still has a PID file')
pg.require(any(line.startswith('Database cluster state:') and line.split(':',1)[1].strip()=='shut down' for line in control.splitlines()),'Owned cluster is not cleanly shut down')
pg.free_port()
target=(run_gate.RUN/args.label/'pg-resource').resolve()
pg.require(target.is_relative_to(run_gate.RUN.resolve()) and not target.exists(),'Fresh contained evidence directory required')
selected={directory/name for name in ('owner-marker.json','ownership.json','config-seal.json','server.log')}
for pattern in ('start-*.json','crash-*.json','stop-*.json','failed-*.json'):
    selected.update(directory.glob(pattern))
secrets=[c[key].encode() for key in ('AdminPassword','RuntimePassword')]
payloads=[]
for source in sorted(selected):
    pg.require(source.resolve()==source and source.stat().st_size<=4_194_304,'Resource evidence path or size mismatch')
    raw=source.read_bytes()
    pg.require(not any(secret in raw for secret in secrets),'Private credential detected; refusing evidence publication')
    payloads.append((source,raw))
stops=[json.loads(raw) for source,raw in payloads if source.name.startswith('stop-')]
pg.require(stops and all(row['SystemIdentifier']==c['SystemIdentifier'] and row['Oid']==c['DatabaseOid'] and row['Sessions']==[] for row in stops),'Smart-stop identity evidence missing or mismatched')
target.mkdir(parents=True)
records=[]
for source,raw in payloads:
    (target/source.name).write_bytes(raw)
    records.append(dict(path=source.name,bytes=len(raw),sha256=hashlib.sha256(raw).hexdigest()))
run_gate.save(target/'collection.json',dict(utc=datetime.now(timezone.utc).isoformat(),owner=c['Owner'],system_identifier=c['SystemIdentifier'],database_oid=c['DatabaseOid'],
    clean_shutdown=True,postmaster_pid_absent=True,reserved_loopback_port_free=True,credentials_copied=False,data_retained=True,
    files=records,note='Native owned-cluster lifecycle evidence only. Full private stages, credentials and command diagnostics remain private; no production acceptance.'))
print(f'Collected {len(records)} reviewed resource evidence files after verified clean shutdown; private credentials excluded.')
