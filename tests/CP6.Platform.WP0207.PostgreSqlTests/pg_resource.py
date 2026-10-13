"""WP02-07 exclusively owned cluster. New credentials only; no application/global credential reads.
create prints only the non-secret settings path. stop requires that exact path and validates identity.
No recursive deletion; preserve data/logs for review. Never terminate a foreign session.
"""
import datetime
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import time

BIN = Path('C:/Program Files/PostgreSQL/18/bin')
ROOT = Path('D:/CP6-local-test/WP02-07').resolve()
PORT = 55427
NO_WINDOW = 0x08000000 if os.name == 'nt' else 0


def run(args, *, env=None, data=None):
    p = subprocess.run([str(x) for x in args], input=data, capture_output=True,
                       text=True, encoding='utf-8', env=env, creationflags=NO_WINDOW)
    if p.returncode:
        # Do not echo command/SQL/password. Diagnostics retained only for known nonsecret init/server tools.
        raise RuntimeError(f'{Path(str(args[0])).name} failed ({p.returncode})')
    return p.stdout.strip()


def env_for(c):
    e = os.environ.copy()
    for name in list(e):
        if name.startswith('PG'):
            del e[name]
    e.update(PGPASSFILE=c['PassFile'], PGCONNECT_TIMEOUT='5', PGSSLMODE='disable')
    return e


def sql(c, query, database=None):
    return run([BIN/'psql.exe', '-X', '-w', '-h', '127.0.0.1', '-p', PORT,
                '-U', c['Admin'], '-d', database or c['Database'], '-At', '-v', 'ON_ERROR_STOP=1'],
               env=env_for(c), data=query)


def identity(c, database=None):
    text = sql(c, "SELECT json_build_object('SystemIdentifier',system_identifier::text,"
               "'DataDirectory',current_setting('data_directory'),'Port',inet_server_port(),"
               "'Database',current_database(),'Oid',(SELECT oid FROM pg_database WHERE datname=current_database()),"
               "'DatabaseOwner',(SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database()),"
               "'Principal',session_user) FROM pg_control_system();", database)
    result = json.loads(text)
    assert Path(result['DataDirectory']).resolve() == Path(c['DataDirectory']).resolve()
    assert result['Port'] == PORT and result['Principal'] == c['Admin']
    assert result['Database'] == (database or c['Database'])
    if 'SystemIdentifier' in c:
        assert result['SystemIdentifier'] == c['SystemIdentifier']
    if not database and 'DatabaseOid' in c:
        assert result['Oid'] == c['DatabaseOid'] and result['DatabaseOwner'] == c['Admin']
    return result


def main():
    if sys.argv[1] in ('create', 'resume-started'):
        if sys.argv[1] == 'resume-started':
            # Resume only a pre-start manifest written by this helper; never reconstruct ownership from a name.
            directory = Path(sys.argv[2]).resolve()
            assert directory.parent == ROOT and len(directory.name) == 32
            c = json.loads((directory/'settings-before-start.json').read_text(encoding='utf-8'))
            assert c['Owner'] == directory.name and c['Marker'] == 'CP6-WP02-07:'+directory.name
            assert Path(c['RunDirectory']).resolve() == directory and Path(c['DataDirectory']).resolve() == directory/'data'
            pid_lines = (directory/'data'/'postmaster.pid').read_text().splitlines()
            assert Path(pid_lines[1]).resolve() == directory/'data' and int(pid_lines[3]) == PORT
            identity(c, 'postgres')
            assert sql(c, f"SELECT count(*) FROM pg_database WHERE datname='{c['Database']}';", 'postgres') == '0'
            provision(c, directory)
            return
        probe = socket.socket()
        probe.bind(('127.0.0.1', PORT))
        probe.close()
        owner = secrets.token_hex(16)
        directory = ROOT/owner
        assert directory.parent == ROOT and not directory.exists()
        directory.mkdir(parents=True)
        # Restrict the entire newly-owned directory before writing credentials.
        user = run(['whoami'])
        run(['icacls', directory, '/inheritance:r', '/grant:r', f'{user}:(OI)(CI)F', 'SYSTEM:(OI)(CI)F'])
        admin_pass, runtime_pass = secrets.token_hex(24), secrets.token_hex(24)
        c = dict(Owner=owner, Marker='CP6-WP02-07:'+owner, Port=PORT,
                 Database='cp6_wp0207_'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d')+'_'+owner[:8],
                 Admin='wp0207_admin_'+owner[:8], Runtime='wp0207_runtime_'+owner[:8],
                 RunDirectory=str(directory), DataDirectory=str(directory/'data'), PassFile=str(directory/'pgpass'),
                 AdminPassword=admin_pass, RuntimePassword=runtime_pass)
        pwfile = directory/'init-password'
        pwfile.write_text(admin_pass+'\n', encoding='utf-8')
        Path(c['PassFile']).write_text(f'127.0.0.1:{PORT}:*:{c["Admin"]}:{admin_pass}\n', encoding='utf-8')
        log = run([BIN/'initdb.exe', '-D', c['DataDirectory'], '-U', c['Admin'], '--pwfile='+str(pwfile),
                   '--auth-host=scram-sha-256', '--auth-local=scram-sha-256', '--encoding=UTF8', '--locale=C'])
        (directory/'init.log').write_text(log, encoding='utf-8')
        with (Path(c['DataDirectory'])/'postgresql.conf').open('a', encoding='utf-8') as f:
            f.write(f"\nlisten_addresses='127.0.0.1'\nport={PORT}\npassword_encryption='scram-sha-256'\n")
        control = run([BIN/'pg_controldata.exe', '-D', c['DataDirectory']])
        c['SystemIdentifier'] = next(line.split(':', 1)[1].strip() for line in control.splitlines()
                                     if line.startswith('Database system identifier:'))
        # Start-Process Hidden isolates the server lifecycle from captured tool pipes on Windows.
        (directory/'settings-before-start.json').write_text(json.dumps(c, indent=2), encoding='utf-8')
        quote = lambda value: "'" + str(value).replace("'", "''") + "'"
        args = ','.join(quote(x) for x in ['-D', c['DataDirectory'], '-l', directory/'server.log', '-w', 'start'])
        script = (f"Start-Process -FilePath {quote(BIN/'pg_ctl.exe')} -ArgumentList @({args}) -WindowStyle Hidden "
                  f"-RedirectStandardOutput {quote(directory/'start.stdout.log')} -RedirectStandardError {quote(directory/'start.stderr.log')}")
        run(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', script])
        for attempt in range(50):
            try:
                identity(c, 'postgres')
                break
            except RuntimeError:
                if attempt == 49:
                    raise
                time.sleep(.2)
        provision(c, directory)
    elif sys.argv[1] == 'stop':
        stop()
    else:
        raise ValueError('Expected create, resume-started or stop')


def provision(c, directory):
        owner, runtime_pass = c['Owner'], c['RuntimePassword']
        c['SystemIdentifier'] = identity(c, 'postgres')['SystemIdentifier']
        assert Path(c['DataDirectory']).resolve().is_relative_to(directory.resolve())
        sql(c, f'CREATE ROLE "{c["Runtime"]}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD \'{runtime_pass}\';\n'
               f'CREATE DATABASE "{c["Database"]}" OWNER "{c["Admin"]}";', 'postgres')
        c['DatabaseOid'] = identity(c)['Oid']
        # Dedicated guard schema + task fixture schema; runtime gets no CREATE or administrative privilege.
        sql(c, f'REVOKE ALL ON DATABASE "{c["Database"]}" FROM PUBLIC;'
               f'GRANT CONNECT ON DATABASE "{c["Database"]}" TO "{c["Runtime"]}";'
               'REVOKE CREATE ON SCHEMA public FROM PUBLIC; CREATE SCHEMA wp0207_guard; CREATE SCHEMA fixture;'
               'CREATE TABLE wp0207_guard.resource(owner text PRIMARY KEY, marker text NOT NULL);'
               f"INSERT INTO wp0207_guard.resource VALUES ('{owner}','{c['Marker']}');"
               f'GRANT USAGE ON SCHEMA wp0207_guard, fixture TO "{c["Runtime"]}";'
               f'GRANT SELECT ON wp0207_guard.resource TO "{c["Runtime"]}";'
               f'ALTER DEFAULT PRIVILEGES IN SCHEMA fixture GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "{c["Runtime"]}";'
               f'ALTER DEFAULT PRIVILEGES IN SCHEMA fixture GRANT USAGE, SELECT ON SEQUENCES TO "{c["Runtime"]}";')
        identity(c)
        settings = directory/'settings.json'
        settings.write_text(json.dumps(c, indent=2), encoding='utf-8')
        public = {k:v for k,v in c.items() if k not in ('AdminPassword','RuntimePassword','PassFile')}
        (directory/'ownership.json').write_text(json.dumps(public, indent=2), encoding='utf-8')
        print(settings)


def stop():
        path = Path(sys.argv[2]).resolve()
        assert path.parent.parent == ROOT and path.name == 'settings.json'
        c = json.loads(path.read_text(encoding='utf-8'))
        assert path.parent.name == c['Owner'] and len(c['Owner']) == 32
        assert Path(c['RunDirectory']).resolve() == path.parent
        assert Path(c['DataDirectory']).resolve() == path.parent/'data'
        actual = identity(c)
        assert sql(c, 'SELECT owner || \':\' || marker FROM wp0207_guard.resource;') == c['Owner']+':'+c['Marker']
        sessions = sql(c, 'SELECT count(*) FROM pg_stat_activity WHERE backend_type=\'client backend\' AND pid<>pg_backend_pid();')
        assert sessions == '0', 'Unexpected live sessions; no termination authorized'
        run([BIN/'pg_ctl.exe', '-D', c['DataDirectory'], '-m', 'smart', '-w', '-t', '15', 'stop'])
        (path.parent/'stopped.json').write_text(json.dumps(actual, indent=2), encoding='utf-8')
        print('Verified owned server stopped; data and logs retained; no files deleted.')


if __name__ == '__main__':
    main()
