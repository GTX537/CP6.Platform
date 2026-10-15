"""WP02-26 ONLY: fresh owned PostgreSQL provision and identity-gated lifecycle.

No deletion/reset, no unknown session termination, no credential output. Partial
provision keeps its marker, private settings and stage journal; it is never treated
as a valid runtime without actual system/database identity. Uses installed PG18.
"""
import argparse
from datetime import datetime, timezone
import json
import hashlib
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import time

BIN = Path('C:/Program Files/PostgreSQL/18/bin')
ROOT = Path('D:/CP6-local-test/WP02-26')
PORT = 55429
NO_WINDOW = 0x08000000 if os.name == 'nt' else 0
PRIVATE_LOG = None


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def command(args, *, env=None, data=None, timeout=45):
    try:
        result = subprocess.run([str(a) for a in args], input=data, capture_output=True,
                                text=True, encoding='utf-8', env=env, timeout=timeout,
                                creationflags=NO_WINDOW)
    except subprocess.TimeoutExpired as error:
        if PRIVATE_LOG:
            (PRIVATE_LOG/('command-timeout-'+str(time.time_ns())+'.log')).write_bytes((error.stdout or b'')+(error.stderr or b''))
        raise
    if PRIVATE_LOG:
        (PRIVATE_LOG/('command-'+str(time.time_ns())+'.json')).write_text(json.dumps(dict(program=Path(args[0]).name,exit_code=result.returncode,stdout=result.stdout,stderr=result.stderr)),encoding='utf-8')
    # Do not echo SQL, arguments, provider diagnostics or private configuration.
    require(result.returncode == 0, f'{Path(args[0]).name} failed (exit {result.returncode}); private logs retained')
    return result.stdout.strip()


def paths(c):
    global PRIVATE_LOG
    require(ROOT.resolve() == ROOT, 'Task root redirects')
    owner = c['Owner']
    require(re.fullmatch('[0-9a-f]{32}', owner) is not None, 'Invalid owner')
    directory = ROOT / owner
    require(directory.resolve() == directory, 'Owner directory redirects')
    for key, expected in [('RunDirectory', directory), ('DataDirectory', directory/'data'), ('PassFile', directory/'pgpass')]:
        require(Path(c[key]).resolve() == expected, 'Contained path mismatch')
    require(c['Marker'] == 'CP6-WP02-26:'+owner and c['Port'] == PORT, 'Marker or endpoint mismatch')
    require(c['Admin'] == 'wp0226_admin_'+owner[:8] and c['Runtime'] == 'wp0226_runtime_'+owner[:8], 'Role mismatch')
    require(re.fullmatch('cp6_wp0226_[0-9]{8}_'+owner[:8], c['Database']) is not None, 'Database name mismatch')
    expected = {k: c[k] for k in ('Owner', 'Marker', 'Port')}
    require(json.loads((directory/'owner-marker.json').read_bytes()) == expected, 'Owner marker mismatch')
    PRIVATE_LOG = directory
    return directory


def load(path):
    file = Path(path).absolute()
    require(file.resolve() == file and file.name == 'settings.json' and file.parent.parent == ROOT, 'Wrong settings path')
    c = json.loads(file.read_bytes())
    require(paths(c) == file.parent and c.get('Stage') == 'Ready', 'Incomplete or mismatched provision')
    require(re.fullmatch('[0-9]+', c.get('SystemIdentifier', '')) is not None and isinstance(c.get('DatabaseOid'), int), 'Missing native identity')
    verify_config(c)
    return c


def private_stage(c, stage):
    directory = paths(c)
    c['Stage'] = stage
    # Each stage is append-only; settings.json is created exactly once at Ready.
    file = directory / f'stage-{time.time_ns()}-{stage}.json'
    with file.open('x', encoding='utf-8') as output:
        json.dump(c, output, indent=2)


def sql(c, query, *, database=None, runtime=False):
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith('PG')}
    env.update(PGPASSFILE=c['PassFile'], PGCONNECT_TIMEOUT='5', PGSSLMODE='disable', PGAPPNAME=c['Marker']+':guard', PGOPTIONS='-c statement_timeout=5000 -c lock_timeout=5000')
    return command([BIN/'psql.exe', '-X', '-w', '-h', '127.0.0.1', '-p', str(PORT),
                    '-U', c['Runtime'] if runtime else c['Admin'], '-d', database or c['Database'],
                    '-At', '-v', 'ON_ERROR_STOP=1'], env=env, data=query, timeout=12)


def disk_identity(c):
    paths(c)
    verify_config(c)
    control = command([BIN/'pg_controldata.exe', '-D', c['DataDirectory']])
    actual = next(line.split(':', 1)[1].strip() for line in control.splitlines() if line.startswith('Database system identifier:'))
    require(actual == c['SystemIdentifier'], 'Disk cluster system identifier mismatch')
    return control


def config_hashes(c):
    directory=paths(c)/'data'
    result={}
    for name in ('postgresql.conf','postgresql.auto.conf','pg_hba.conf','pg_ident.conf'):
        file=directory/name
        require(file.resolve()==file and file.stat().st_size<=262144,'Configuration path or size mismatch')
        result[name]=hashlib.sha256(file.read_bytes()).hexdigest()
    return result


def verify_config(c):
    seal=json.loads((paths(c)/'config-seal.json').read_bytes())
    require(seal['SystemIdentifier']==c['SystemIdentifier'] and seal['Owner']==c['Owner'] and seal['Hashes']==config_hashes(c),'Immutable configuration seal mismatch')


def seal_config(c):
    directory=paths(c)
    require(not (directory/'data/postmaster.pid').exists(),'Seal only a stopped owned cluster')
    control=command([BIN/'pg_controldata.exe','-D',c['DataDirectory']])
    require(any(l.startswith('Database system identifier:') and l.split(':',1)[1].strip()==c['SystemIdentifier'] for l in control.splitlines()),'Cannot seal unknown cluster')
    for name in ('postgresql.conf','postgresql.auto.conf'):
        require(not any(re.match(r'\s*include(?:_dir|_if_exists)?\s*(?:=|\s)',l) for l in (directory/'data'/name).read_text().splitlines()),'External config include unsupported')
    expected=dict(port=str(PORT),listen_addresses='127.0.0.1',fsync='on',synchronous_commit='on',full_page_writes='on',shared_preload_libraries='')
    for key,value in expected.items():
        require(command([BIN/'postgres.exe','-D',c['DataDirectory'],'-C',key])==value,'Stopped configuration setting mismatch')
    hashes=config_hashes(c)
    with (directory/'config-seal.json').open('x',encoding='utf-8') as file:
        json.dump(dict(Owner=c['Owner'],SystemIdentifier=c['SystemIdentifier'],Hashes=hashes),file,indent=2)


def identity(c, *, bootstrap=False):
    paths(c)
    name = 'postgres' if bootstrap else c['Database']
    value = json.loads(sql(c, "SELECT json_build_object('SystemIdentifier',system_identifier::text,"
        "'DataDirectory',current_setting('data_directory'),'Port',inet_server_port(),'Database',current_database(),"
        "'Oid',(SELECT oid::bigint FROM pg_database WHERE datname=current_database()),"
        "'DatabaseOwner',(SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database()),"
        "'Principal',session_user,'Listen',current_setting('listen_addresses'),'Version',version()) FROM pg_control_system();", database=name))
    require(value['SystemIdentifier'] == c['SystemIdentifier'] and Path(value['DataDirectory']).resolve() == Path(c['DataDirectory']), 'Live cluster identity mismatch')
    require(value['Port'] == PORT and value['Listen'] == '127.0.0.1' and value['Principal'] == c['Admin'], 'Live endpoint/principal mismatch')
    require(value['Database'] == name and value['DatabaseOwner'] == c['Admin'], 'Database owner mismatch')
    if not bootstrap:
        require(value['Oid'] == c['DatabaseOid'], 'Database OID mismatch')
    return value


def sessions(c, expected=(), *, database=None):
    rows = json.loads(sql(c, "SELECT coalesce(json_agg(json_build_object('Pid',pid,'Role',usename,'Database',datname,"
        "'Application',application_name)), '[]'::json) FROM pg_stat_activity WHERE backend_type='client backend' AND pid<>pg_backend_pid();", database=database))
    require({r['Pid'] for r in rows} == set(expected), 'Unexpected sessions; refusing lifecycle operation')
    require(all(r['Role'] == c['Runtime'] and r['Database'] == c['Database'] and r['Application'] == c['Marker']+':crash' for r in rows), 'Session owner/application mismatch')
    return rows


def verify(c, expected=()):
    actual = identity(c)
    guard = json.loads(sql(c, "SELECT json_build_object('Owner',g.owner,'Marker',g.marker,'SystemIdentifier',g.system_identifier,"
        "'DatabaseOid',g.database_oid,'Principal',session_user,'Superuser',r.rolsuper,'CreateDatabase',r.rolcreatedb,"
        "'CreateRole',r.rolcreaterole,'Inherit',r.rolinherit,'BypassRls',r.rolbypassrls,"
        "'CreateSchema',has_database_privilege(current_database(),'CREATE'),'CreateFixture',has_schema_privilege('fixture','CREATE'),"
        "'Native',wp0226_guard.native_identity()) FROM wp0226_guard.resource g CROSS JOIN pg_roles r WHERE r.rolname=session_user;", runtime=True))
    require(guard['Owner'] == c['Owner'] and guard['Marker'] == c['Marker'] and guard['SystemIdentifier'] == c['SystemIdentifier'] and guard['DatabaseOid'] == c['DatabaseOid'], 'Stored resource marker mismatch')
    require(guard['Principal'] == c['Runtime'] and guard['Native'] == c['SystemIdentifier']+'|'+str(Path(c['DataDirectory'])).replace('\\','/'), 'Runtime native identity mismatch')
    require(not any(guard[k] for k in ('Superuser','CreateDatabase','CreateRole','Inherit','BypassRls','CreateSchema','CreateFixture')), 'Runtime privileges exceed DML scope')
    require(sql(c,"SELECT count(*) FROM pg_auth_members WHERE member=(SELECT oid FROM pg_roles WHERE rolname='"+c['Runtime']+"');")=='0','Runtime has unexpected role memberships')
    actual.update(Owner=c['Owner'], Marker=c['Marker'], Runtime=guard, Sessions=sessions(c, expected))
    return actual


def free_port():
    with socket.socket() as sock:
        if os.name == 'nt':
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        sock.bind(('127.0.0.1', PORT))


def launch(c):
    directory = paths(c)
    disk_identity(c)
    require(not (directory/'data/postmaster.pid').exists(), 'Postmaster PID exists; not starting unknown process')
    free_port()
    stamp = str(time.time_ns())
    def quote(v):
        return "'"+str(v).replace("'", "''")+"'"
    args = ','.join(quote(v) for v in ['-D', c['DataDirectory'], '-l', directory/'server.log', '-w', '-t', '30', 'start'])
    script = f"Start-Process -FilePath {quote(BIN/'pg_ctl.exe')} -ArgumentList @({args}) -WindowStyle Hidden -RedirectStandardOutput {quote(directory/('start-'+stamp+'.out'))} -RedirectStandardError {quote(directory/('start-'+stamp+'.err'))}"
    with (directory/('launch-'+stamp+'.log')).open('x') as log:
        subprocess.Popen(['powershell.exe','-NoProfile','-NonInteractive','-Command',script], stdin=subprocess.DEVNULL, stdout=log, stderr=log, creationflags=NO_WINDOW)
    deadline=time.monotonic()+35
    while True:
        try:
            return identity(c, bootstrap=c.get('Stage') != 'Ready')
        except RuntimeError:
            if time.monotonic()>=deadline:
                raise
            time.sleep(.2)


def create(recovery=None):
    require(ROOT.resolve() == ROOT, 'Root redirects')
    free_port()
    if recovery:
        file=Path(recovery).absolute()
        require(file.resolve()==file and file.parent.parent==ROOT and re.fullmatch('stage-[0-9]+-Allocated.json',file.name), 'Invalid partial recovery path')
        c=json.loads(file.read_bytes()); directory=paths(c); owner=c['Owner']
        require(file.parent==directory and c['Stage']=='Allocated' and not (directory/'settings.json').exists(), 'Not an allocated partial provision')
        require(not (directory/'data/postmaster.pid').exists(), 'Partial cluster may be running')
        failures=[json.loads(p.read_bytes()) for p in directory.glob('failed-*.json')]
        require(any(f==dict(stage='Allocated',error_type='TimeoutExpired',owner=owner) for f in failures), 'No matching init timeout evidence')
        require((directory/'data/PG_VERSION').read_text().strip()=='18', 'Unexpected initialized PG version')
        control=command([BIN/'pg_controldata.exe','-D',c['DataDirectory']])
        require(any(l.startswith('Database cluster state:') and l.split(':',1)[1].strip()=='shut down' for l in control.splitlines()), 'Partial cluster not safely shut down')
        for line in (directory/'data/pg_hba.conf').read_text().splitlines():
            if line.strip() and not line.lstrip().startswith('#'):
                require(line.split()[-1]=='scram-sha-256','Partial cluster authentication mismatch')
        (directory/('recovery-control-'+str(time.time_ns())+'.txt')).write_text(control,encoding='utf-8')
    else:
        owner = secrets.token_hex(16)
        directory = ROOT/owner
        require(not directory.exists() and directory.resolve() == directory, 'Owner directory not fresh')
        directory.mkdir(parents=True)
        user = command(['whoami'])
        command(['icacls', str(directory), '/inheritance:r', '/grant:r', user+':(OI)(CI)F', 'SYSTEM:(OI)(CI)F'])
        c = dict(Owner=owner, Marker='CP6-WP02-26:'+owner, Port=PORT,
                 Database='cp6_wp0226_'+datetime.now(timezone.utc).strftime('%Y%m%d')+'_'+owner[:8],
                 Admin='wp0226_admin_'+owner[:8], Runtime='wp0226_runtime_'+owner[:8],
                 RunDirectory=str(directory), DataDirectory=str(directory/'data'), PassFile=str(directory/'pgpass'),
                 AdminPassword=secrets.token_hex(24), RuntimePassword=secrets.token_hex(24))
        (directory/'owner-marker.json').write_text(json.dumps({k:c[k] for k in ('Owner','Marker','Port')}),encoding='utf-8')
        private_stage(c, 'Allocated')
    try:
        if not recovery:
            password = directory/'init-password'
            password.write_text(c['AdminPassword']+'\n',encoding='utf-8')
            Path(c['PassFile']).write_text(f'127.0.0.1:{PORT}:*:{c["Admin"]}:{c["AdminPassword"]}\n127.0.0.1:{PORT}:{c["Database"]}:{c["Runtime"]}:{c["RuntimePassword"]}\n',encoding='utf-8')
            result = command([BIN/'initdb.exe','-D',c['DataDirectory'],'-U',c['Admin'],'--pwfile='+str(password),'--auth-host=scram-sha-256','--auth-local=scram-sha-256','--encoding=UTF8','--locale=C'],timeout=180)
            (directory/'init.log').write_text(result,encoding='utf-8')
        with (directory/'data/postgresql.conf').open('a',encoding='utf-8') as f:
            f.write(f"\nlisten_addresses='127.0.0.1'\nport={PORT}\npassword_encryption='scram-sha-256'\nfsync=on\nsynchronous_commit=on\nfull_page_writes=on\n")
        control = command([BIN/'pg_controldata.exe','-D',c['DataDirectory']])
        c['SystemIdentifier'] = next(l.split(':',1)[1].strip() for l in control.splitlines() if l.startswith('Database system identifier:'))
        private_stage(c, 'Initialized')
        seal_config(c)
        launch(c)
        private_stage(c, 'Started')
        sessions(c, database='postgres')
        sql(c, f'CREATE ROLE "{c["Runtime"]}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD \'{c["RuntimePassword"]}\'; CREATE DATABASE "{c["Database"]}" OWNER "{c["Admin"]}";', database='postgres')
        c['DatabaseOid'] = int(sql(c, 'SELECT oid FROM pg_database WHERE datname=current_database();'))
        identity(c)
        private_stage(c, 'DatabaseCreated')
        sessions(c)
        configure_database(c)
    except Exception as error:
        # Keep partial state for an explicit identity-based recovery; never delete to retry.
        (directory/('failed-'+str(time.time_ns())+'.json')).write_text(json.dumps(dict(stage=c.get('Stage'),error_type=type(error).__name__,owner=owner)),encoding='utf-8')
        raise RuntimeError('Provision failed; private partial state retained at '+str(directory)) from None


def configure_database(c):
    directory=paths(c); owner=c['Owner']
    sql(c, f'''BEGIN;
REVOKE ALL ON DATABASE "{c['Database']}" FROM PUBLIC;
GRANT CONNECT ON DATABASE "{c['Database']}" TO "{c['Runtime']}";
REVOKE ALL ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA wp0226_guard; CREATE SCHEMA fixture;
CREATE TABLE wp0226_guard.resource(owner text PRIMARY KEY, marker text NOT NULL, system_identifier text NOT NULL, database_oid bigint NOT NULL);
INSERT INTO wp0226_guard.resource VALUES ('{owner}','{c['Marker']}','{c['SystemIdentifier']}',{c['DatabaseOid']});
CREATE FUNCTION wp0226_guard.native_identity() RETURNS text LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$ SELECT system_identifier::text || '|' || current_setting('data_directory') FROM pg_control_system() $$;
REVOKE ALL ON FUNCTION wp0226_guard.native_identity() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION wp0226_guard.native_identity() TO "{c['Runtime']}";
GRANT USAGE ON SCHEMA wp0226_guard,fixture TO "{c['Runtime']}";
GRANT SELECT ON wp0226_guard.resource TO "{c['Runtime']}";
CREATE TABLE fixture.receipts(case_id text NOT NULL, operation_key text NOT NULL, message_key text NOT NULL, amount numeric(38,8) NOT NULL, quantity numeric NOT NULL, raw_input bytea NOT NULL, PRIMARY KEY(case_id,operation_key), UNIQUE(case_id,message_key));
CREATE TABLE fixture.balances(case_id text PRIMARY KEY, amount numeric NOT NULL);
GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA fixture TO "{c['Runtime']}";
COMMIT;''')
    actual = verify(c)
    private_stage(c,'Ready')
    with (directory/'settings.json').open('x',encoding='utf-8') as f:
        json.dump(c,f,indent=2)
    (directory/'ownership.json').write_text(json.dumps(actual,indent=2),encoding='utf-8')
    print(directory/'settings.json')


def recover_database(path, oid):
    file=Path(path).absolute()
    require(file.resolve()==file and file.parent.parent==ROOT and re.fullmatch('stage-[0-9]+-Started.json',file.name), 'Wrong partial database recovery path')
    c=json.loads(file.read_bytes()); directory=paths(c)
    require(file.parent==directory and c['Stage']=='Started' and not (directory/'settings.json').exists(), 'Not a recoverable started provision')
    require(isinstance(oid,int) and oid>0,'Explicit previously observed database OID required')
    c['DatabaseOid']=oid
    disk_identity(c); identity(c); sessions(c)
    require(sql(c,"SELECT count(*) FROM pg_namespace WHERE nspname IN ('fixture','wp0226_guard');")=='0', 'Partial schema exists; manual stage-specific recovery required')
    role=json.loads(sql(c,"SELECT json_build_object('super',rolsuper,'createdb',rolcreatedb,'createrole',rolcreaterole,'inherit',rolinherit,'bypass',rolbypassrls,'login',rolcanlogin) FROM pg_roles WHERE rolname='"+c['Runtime']+"';"))
    require(role==dict(super=False,createdb=False,createrole=False,inherit=False,bypass=False,login=True),'Partial runtime role mismatch')
    private_stage(c,'DatabaseCreated')
    configure_database(c)


def lifecycle(c, action, expected=()):
    directory = paths(c)
    if action == 'start':
        launch(c)
        actual = verify(c)
    else:
        actual = verify(c, expected)
        mode = 'immediate' if action == 'crash' else 'smart'
        require(action == 'crash' or not expected, 'Normal shutdown requires zero sessions')
        command([BIN/'pg_ctl.exe','-D',c['DataDirectory'],'-m',mode,'-w','-t','30','stop'])
        require(not (directory/'data/postmaster.pid').exists(), 'Shutdown did not remove PID file')
        disk_identity(c)
    actual.update(Action=action, ControlledFailure=action=='crash', AtUtc=datetime.now(timezone.utc).isoformat())
    (directory/(action+'-'+str(time.time_ns())+'.json')).write_text(json.dumps(actual,indent=2),encoding='utf-8')
    print(json.dumps(actual,indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('action',choices=['create','recover-initialized','recover-database','verify','start','stop','crash'])
    parser.add_argument('settings',nargs='?')
    parser.add_argument('--expected-pid',type=int,action='append',default=[])
    parser.add_argument('--database-oid',type=int)
    args = parser.parse_args()
    if args.action == 'create':
        require(args.settings is None and not args.expected_pid,'Create does not accept existing identity')
        create()
    elif args.action=='recover-initialized':
        require(args.settings and not args.expected_pid,'Recovery requires one partial-stage file')
        create(args.settings)
    elif args.action=='recover-database':
        require(args.settings and not args.expected_pid,'Recovery requires one partial-stage file')
        recover_database(args.settings,args.database_oid)
    else:
        c = load(args.settings)
        require(args.action == 'crash' or not args.expected_pid,'Only controlled crash permits explicit owned sessions')
        if args.action == 'verify':
            print(json.dumps(verify(c),indent=2))
        else:
            lifecycle(c,args.action,args.expected_pid)
