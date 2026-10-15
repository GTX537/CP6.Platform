"""Collect safe runtime identities, gracefully stop only verified owned services, retain all data."""
import argparse,json,subprocess,urllib.request
from pathlib import Path
import runtime as r
a=argparse.ArgumentParser();a.add_argument('--owner',required=True);a.add_argument('--settings',required=True);args=a.parse_args()
c=r.load(args.owner,args.settings);p=Path(c['RunDirectory'])/'transport'
tokens=json.loads((p/'secrets.json').read_bytes())['tokens']
process=json.loads(sorted(p.glob('fixture-process-*.json'))[-1].read_text(encoding='utf-8-sig'))
r.pg.require(process['Owner']==c['Owner'],'Fixture process owner mismatch')
pid=int(process['ProcessId'])
script=f"$p=Get-Process -Id {pid} -ErrorAction Stop; $c=Get-CimInstance Win32_Process -Filter 'ProcessId = {pid}'; @{{Pid=$p.Id;StartTime=$p.StartTime.ToUniversalTime().ToString('O');Path=$p.Path;CommandLine=$c.CommandLine;Ports=@(Get-NetTCPConnection -State Listen -OwningProcess {pid} | Select-Object LocalAddress,LocalPort)}} | ConvertTo-Json -Depth 4"
actual=json.loads(subprocess.check_output(['pwsh','-NoProfile','-Command',script],creationflags=r.pg.NO_WINDOW))
r.pg.require(actual['StartTime']==process['StartTime'] and process['Dll'].replace('\\','/') in actual['CommandLine'].replace('\\','/'),'Fixture process identity drift')
r.pg.require(r.hashlib.sha256(Path(process['Dll']).read_bytes()).hexdigest().lower()==process['DllHash'].lower(),'Fixture DLL identity drift')
r.pg.require(sorted(x['LocalPort'] for x in actual['Ports'])==[18012,18013] and all(x['LocalAddress']=='127.0.0.1' for x in actual['Ports']),'Fixture binding mismatch')
with urllib.request.urlopen('http://127.0.0.1:18012/healthz',timeout=5) as res: health=json.load(res)
r.pg.require(health['owner']==c['Owner'],'HTTP owner mismatch')
containers=[]
for suffix in ('kafka','publisher-dapr','receiver-dapr','probe','kafka-setup-failed1'):
    value=r.inspect(c,c['Project']+'-'+suffix)
    containers.append({'name':value['Name'],'id':value['Id'],'image':value['Image'],'labels':value['Config']['Labels'],
        'running':value['State']['Running'],'ports':value['HostConfig']['PortBindings'],'networks':list(value['NetworkSettings']['Networks'])})
networks=[]
for suffix in ('net','host'):
    value=r.inspect(c,c['Project']+'-'+suffix,'network')
    networks.append({'id':value['Id'],'name':value['Name'],'internal':value['Internal'],'labels':value['Labels']})
acl=r.kafka(c,'acls',['--command-config','/private/admin.properties','--list']).stdout
(p/'final-acl-proof.txt').write_bytes(acl)
# This native config includes JAAS credentials: retain privately, export only an allowlist.
command=['docker','exec',c['Project']+'-kafka','cat','/opt/kafka/config/server.properties']
raw=subprocess.check_output(command,creationflags=r.pg.NO_WINDOW);(p/'actual-kafka-server.private').write_bytes(raw)
allowed={'listeners','advertised.listeners','listener.security.protocol.map','controller.listener.names','inter.broker.listener.name',
         'sasl.enabled.mechanisms','sasl.mechanism.inter.broker.protocol','authorizer.class.name','allow.everyone.if.no.acl.found','super.users','auto.create.topics.enable'}
config={}
for line in raw.decode().splitlines():
    if '=' in line:
        key,value=line.split('=',1)
        if key.strip() in allowed:config[key.strip()]=value.strip()
r.write(p/'native-identity-before-stop.json',{'owner':c['Owner'],'process':actual,'containers':containers,'networks':networks,
    'brokerConfigCommand':command,'brokerConfigAllowlist':config,'rawConfigSha256':r.hashlib.sha256(raw).hexdigest(),
    'authBoundary':'Dedicated SASL publisher topic ACL; task-local deployment admin remains trusted; no production TLS claim'})
request=urllib.request.Request('http://127.0.0.1:18013/test/stop',data=b'',headers={'x-fixture-control':tokens['control']})
with urllib.request.urlopen(request,timeout=10) as res:r.pg.require(res.status==200,'Fixture stop response')
r.stop(c)
r.pg.stop(c)
for suffix in ('kafka','publisher-dapr','receiver-dapr','probe','kafka-setup-failed1'):
    r.inspect(c,c['Project']+'-'+suffix,running=False)
for port in (*r.PORTS,55430):
    with r.socket.socket() as sock:
        r.pg.require(sock.connect_ex(('127.0.0.1',port))!=0,'Owned port still listening')
r.write(p/'stop-result.json',{'owner':c['Owner'],'allOwnedContainersStopped':True,'fixtureStopped':True,'postgresSmartStopped':True,
    'portsClosed':[*r.PORTS,55430],'dataLogsFailuresRetained':True,'noDeletesOrPrune':True})
print('Verified owned fixture, containers and PostgreSQL stopped; data/logs retained')
