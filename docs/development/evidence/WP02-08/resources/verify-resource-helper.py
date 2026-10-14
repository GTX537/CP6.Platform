"""Run bounded resource ownership checks; records no credential or connection string."""
import datetime
import hashlib
import json
from pathlib import Path
import runpy
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[5]
HELPER = ROOT/'tests/CP6.Platform.WP0208.PostgreSqlTests/pg_resource.py'
OUT = Path(__file__).resolve().parent
api = runpy.run_path(str(HELPER), run_name='resource_helper_validation')
settings = Path(sys.argv[1])
c = api['load_settings'](settings)
results = []


def rejected(name, action):
    try:
        action()
    except (RuntimeError, FileNotFoundError, OSError):
        results.append({'check': name, 'result': 'PASS_EXPECTED_REJECTION'})
    else:
        raise RuntimeError('Expected rejection was absent: '+name)


identity = api['verify'](c)
results.append({'check': 'Exact live cluster, database OID/owner, marker, runtime principal/privileges and zero sessions', 'result': 'PASS'})
rejected('Wrong full Owner token', lambda: api['validate_paths'](dict(c, Owner='0'*32)))
rejected('Wrong declared port', lambda: api['validate_paths'](dict(c, Port=5432)))
rejected('Data directory outside exact owned path', lambda: api['validate_paths'](dict(c, DataDirectory='D:/CP6-local-test/other')))
rejected('Wrong marker', lambda: api['validate_paths'](dict(c, Marker='CP6-WP02-08:wrong')))
rejected('Wrong system identifier', lambda: api['identity'](dict(c, SystemIdentifier='0')))
rejected('Wrong database OID', lambda: api['identity'](dict(c, DatabaseOid=-1)))
rejected('Foreign task settings path before any credentials are read', lambda: api['load_settings']('D:/CP6-local-test/WP02-07/forbidden/settings.json'))
rejected('Occupied task port blocks create before resource mutation', api['create'])

# Own short-lived idle test connection establishes that teardown fails closed;
# natural completion only, no cancellation or session termination.
args = [api['BIN']/'psql.exe', '-X', '-w', '-h', '127.0.0.1', '-p', str(api['PORT']),
        '-U', c['Runtime'], '-d', c['Database'], '-At', '-v', 'ON_ERROR_STOP=1']
with subprocess.Popen([str(x) for x in args], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                      stderr=subprocess.DEVNULL, env=api['env_for'](c), text=True,
                      creationflags=api['NO_WINDOW']) as session:
    session.stdin.write('SELECT pg_sleep(3);\n')
    session.stdin.close()
    for _ in range(30):
        count = api['sql'](c, "SELECT count(*) FROM pg_stat_activity WHERE backend_type='client backend' AND pid<>pg_backend_pid();")
        if count != '0':
            break
        time.sleep(.05)
    else:
        raise RuntimeError('Owned test session was not observed')
    rejected('Any other live client refuses teardown preflight', lambda: api['verify'](c))
    if session.wait(timeout=10) != 0:
        raise RuntimeError('Owned test session did not finish normally')

final = api['verify'](c)
(OUT/'ownership.json').write_text(json.dumps(final, indent=2)+'\n', encoding='utf-8')
report = {'observed_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'helper_sha256': hashlib.sha256(HELPER.read_bytes()).hexdigest(),
          'python': sys.version, 'checks': results, 'count': len(results),
          'resource_left_running': True,
          'boundary': 'Helper preparation only, no product tests, runtime adoption or business AC execution.'}
(OUT/'helper-verification.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(f'PASS {len(results)} ownership/resource checks; own cluster remains running with zero other clients.')
