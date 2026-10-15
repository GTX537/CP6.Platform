"""Task-private native runtime. No pulls, deletes, global stop, or secret output.

Each operation requires the full owner token and private settings path. Logs and
containers are retained. Broker observations use Kafka's binary console output,
not envelope-derived addressing. The record delimiter is forbidden in fixtures.
"""
import argparse
import base64
import datetime
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import time
import urllib.request
import pg_resource as pg

ROOT = Path(__file__).resolve().parents[2]
IMAGES = {
    'kafka': 'sha256:77e3df9054047a88b520d0cc46e16696d3b22022e1d580aeccd2632df6532837',
    'dapr': 'sha256:8e94ba37d6bc95875e88545ce7d8ff781354f29080db02706d57446285fcc4a5',
    'sdk': 'sha256:bb32ba3ba3ea36e38572d9d8db76fa15f7cbf722f3f886e06bca6d528bd4fba8'}
PORTS = (19092, 13500, 15001, 18012, 18013)
TOPIC = 'cp6.platform.contract-example-changed.v1'
OTHER = 'cp6.wp0212.wrong-topic.v1'
SEP = b'|WP0212-COLUMN|'
END = b'|WP0212-END-RECORD|'


def stamp():
    return datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')


def write(path, obj):
    path.write_bytes(json.dumps(obj, indent=2).encode('utf-8'))


def command(c, args, *, data=None, allowed=(0,), timeout=45):
    # Command arguments must not contain credentials. All credentials are private files.
    logs = Path(c['RunDirectory'])/'runtime-logs'
    logs.mkdir(exist_ok=True)
    name = stamp()
    try:
        p = subprocess.run(args, input=data, capture_output=True, timeout=timeout,
                           creationflags=pg.NO_WINDOW)
    except subprocess.TimeoutExpired as e:
        (logs/(name+'.stdout')).write_bytes(e.stdout or b'')
        (logs/(name+'.stderr')).write_bytes(e.stderr or b'')
        write(logs/(name+'.json'), {'args': args, 'exit': 'TIMEOUT', 'timeoutSeconds': timeout})
        raise RuntimeError('Owned runtime command timed out; private logs retained') from None
    (logs/(name+'.stdout')).write_bytes(p.stdout)
    (logs/(name+'.stderr')).write_bytes(p.stderr)
    write(logs/(name+'.json'), {'args': args, 'exit': p.returncode})
    pg.require(p.returncode in allowed, 'Owned runtime command failed; private logs retained: '+name)
    return p


def load(owner, path):
    c = pg.load_settings(path)
    pg.require(c['Owner'] == owner, 'Explicit owner mismatch')
    c['Project'] = 'cp6-wp0212-'+owner[:12]
    return c


def inspect(c, name, kind='container', running=None):
    p = command(c, ['docker', kind, 'inspect', name])
    value = json.loads(p.stdout)[0]
    labels = value['Config']['Labels'] if kind == 'container' else value['Labels']
    pg.require(labels.get('cp6.task') == 'WP02-12' and labels.get('cp6.owner') == c['Owner'], 'Docker owner mismatch')
    if running is not None:
        pg.require(value['State']['Running'] == running, 'Unexpected container state')
    return value


def validate(c):
    for image in IMAGES.values():
        actual = json.loads(command(c, ['docker', 'image', 'inspect', image]).stdout)[0]
        pg.require(actual['Id'] == image, 'Cached image identity mismatch')
        pg.require(actual['Architecture'] == 'amd64' and actual['Os'] == 'linux', 'Cached platform mismatch')
    for port in PORTS:
        with socket.socket() as s:
            if os.name == 'nt': s.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
            s.bind(('127.0.0.1', port))
    return {'Owner': c['Owner'], 'Images': IMAGES, 'PortsFree': PORTS, 'observedUtc': stamp()}


def prepare(c):
    validate(c)
    private = Path(c['RunDirectory'])/'transport'
    pg.require(not private.exists(), 'Transport directory already exists; no overwrite')
    private.mkdir()
    passwords = {p: secrets.token_hex(24) for p in ('admin','publisher','receiver','observer','intruder')}
    tokens = {p: secrets.token_hex(32) for p in ('dapr','app','control')}
    write(private/'secrets.json', {'passwords': passwords, 'tokens': tokens})
    for principal, password in passwords.items():
        (private/(principal+'.properties')).write_text(
            'security.protocol=SASL_PLAINTEXT\nsasl.mechanism=PLAIN\n'
            'sasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required '
            f'username="{principal}" password="{password}";\n'
            'request.timeout.ms=10000\ndefault.api.timeout.ms=30000\n', encoding='utf-8')
    jaas = ('org.apache.kafka.common.security.plain.PlainLoginModule required '
            f'username="admin" password="{passwords["admin"]}" '+
            ' '.join(f'user_{k}="{v}"' for k,v in passwords.items())+';')
    (private/'kafka-jaas.conf').write_text('KafkaServer { '+jaas+' };\n', encoding='utf-8')
    labels = {'cp6.task': 'WP02-12', 'cp6.owner': c['Owner']}
    network = c['Project']+'-net'
    common = {'pull_policy': 'never', 'labels': labels, 'networks': ['owned'], 'restart': 'no'}
    services = {'kafka': dict(common, image=IMAGES['kafka'], container_name=c['Project']+'-kafka',
        ports=['127.0.0.1:19092:9092'], volumes=[str(private).replace('\\','/')+':/private:ro'],
        environment={
            'KAFKA_NODE_ID':'1', 'KAFKA_PROCESS_ROLES':'broker,controller',
            'KAFKA_LISTENERS':'SASL_PLAINTEXT://:9092,CONTROLLER://:9093',
            'KAFKA_ADVERTISED_LISTENERS':'SASL_PLAINTEXT://kafka:9092',
            'KAFKA_LISTENER_SECURITY_PROTOCOL_MAP':'SASL_PLAINTEXT:SASL_PLAINTEXT,CONTROLLER:PLAINTEXT',
            'KAFKA_CONTROLLER_LISTENER_NAMES':'CONTROLLER', 'KAFKA_CONTROLLER_QUORUM_VOTERS':'1@kafka:9093',
            'KAFKA_INTER_BROKER_LISTENER_NAME':'SASL_PLAINTEXT',
            'KAFKA_SASL_ENABLED_MECHANISMS':'PLAIN', 'KAFKA_SASL_MECHANISM_INTER_BROKER_PROTOCOL':'PLAIN',
            'KAFKA_LISTENER_NAME_SASL_PLAINTEXT_PLAIN_SASL_JAAS_CONFIG':jaas,
            'KAFKA_OPTS':'-Djava.security.auth.login.config=/private/kafka-jaas.conf',
            'KAFKA_AUTHORIZER_CLASS_NAME':'org.apache.kafka.metadata.authorizer.StandardAuthorizer',
            'KAFKA_SUPER_USERS':'User:admin;User:ANONYMOUS',
            'KAFKA_ALLOW_EVERYONE_IF_NO_ACL_FOUND':'false', 'KAFKA_AUTO_CREATE_TOPICS_ENABLE':'false',
            'KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR':'1', 'KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR':'1',
            'KAFKA_TRANSACTION_STATE_LOG_MIN_ISR':'1', 'KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS':'0',
            'KAFKA_NUM_PARTITIONS':'1', 'KAFKA_MESSAGE_MAX_BYTES':'1048576'})}
    for role in ('publisher','receiver'):
        comp = private/(role+'-components'); comp.mkdir()
        metadata = {
            'brokers':'kafka:9092','consumerGroup':'wp0212-'+role, 'clientID':'wp0212-'+role,
            'authType':'password','saslUsername':role,'saslPassword':passwords[role],
            'saslMechanism':'PLAINTEXT','disableTls':'true','initialOffset':'oldest',
            'maxMessageBytes':'1048576','consumeRetryInterval':'1s', 'version':'3.0.0',
            'excludeHeaderMetaRegex':'^(__.*|dapr-api-token|x-fixture-control)$'}
        # JSON is valid YAML. Only private component files hold fixture credentials.
        write(comp/'kafka.yaml', {'apiVersion':'dapr.io/v1alpha1','kind':'Component',
            'metadata':{'name':'cp6-kafka-pubsub'},'spec':{'type':'pubsub.kafka','version':'v1',
            'metadata':[{'name':k,'value':v} for k,v in metadata.items()]},'scopes':['wp0212-'+role]})
        services[role+'-dapr'] = dict(common, image=IMAGES['dapr'], container_name=c['Project']+'-'+role+'-dapr',
            entrypoint=['/daprd'], command=['--app-id','wp0212-'+role,'--app-port','18012' if role=='receiver' else '18013',
                '--app-channel-address','host.docker.internal','--dapr-http-port','3500','--dapr-grpc-port','50001',
                '--resources-path','/components','--log-level','warn','--dapr-listen-addresses','0.0.0.0'],
            volumes=[str(comp).replace('\\','/')+':/components:ro'],
            environment={'DAPR_API_TOKEN':tokens['dapr'],'APP_API_TOKEN':tokens['app']},
            ports=['127.0.0.1:13500:3500','127.0.0.1:15001:50001'] if role=='publisher' else [])
    write(private/'compose.json', {'services':services,'networks':{'owned':{'name':network,'internal':False,'labels':labels}}})
    write(private/'public-plan.json', {'Owner':c['Owner'],'Project':c['Project'],'Images':IMAGES,
        'topics':[TOPIC,OTHER], 'principals':list(passwords), 'loopbackPorts':PORTS,
        'transportSecurity':'SASL/PLAIN on task-private internal Docker network; no production TLS claim',
        'aclAuthority':'task-private admin only; no untrusted controller network peers',
        'noImagePull':True})


def compose(c, *args):
    return command(c, ['docker','compose','-p',c['Project'],'-f',str(Path(c['RunDirectory'])/'transport'/'compose.json'),*args], timeout=60)


def kafka(c, tool, args, *, principal='admin', data=None, allowed=(0,), timeout=45):
    name=c['Project']+'-kafka'; inspect(c,name,running=True)
    return command(c,['docker','exec','-i',name,'/opt/kafka/bin/kafka-'+tool+'.sh',
        '--bootstrap-server','kafka:9092',*args],data=data,allowed=allowed,timeout=timeout)


def broker(c):
    present=command(c,['docker','container','inspect',c['Project']+'-kafka'],allowed=(0,1))
    if present.returncode == 0:
        pg.require(inspect(c,c['Project']+'-kafka',running=True)['Image']==IMAGES['kafka'],'Broker image mismatch')
    else:
        compose(c,'up','-d','--pull','never','kafka')
    inspect(c,c['Project']+'-net','network')
    for attempt in range(30):
        try:
            kafka(c,'topics',['--command-config','/private/admin.properties','--list'],timeout=15)
            break
        except RuntimeError:
            if attempt==29: raise
            time.sleep(1)
    for topic in (TOPIC, OTHER):
        kafka(c,'topics',['--command-config','/private/admin.properties','--create','--topic',topic,
            '--partitions','1','--replication-factor','1'])
    for principal,topics,ops in [('publisher',[TOPIC],['Write','Describe']),
                                ('receiver',[TOPIC,OTHER],['Read','Describe']),
                                ('observer',[TOPIC,OTHER],['Read','Describe'])]:
        for topic in topics:
            args=['--command-config','/private/admin.properties','--add','--allow-principal','User:'+principal,'--topic',topic]
            for op in ops: args+=['--operation',op]
            kafka(c,'acls',args)
        if principal!='publisher':
            kafka(c,'acls',['--command-config','/private/admin.properties','--add','--allow-principal','User:'+principal,
                '--group','wp0212-'+principal,'--operation','Read'])
    acl=kafka(c,'acls',['--command-config','/private/admin.properties','--list']).stdout
    (Path(c['RunDirectory'])/'transport'/'acl-proof.txt').write_bytes(acl)


def observe(c, topic, count):
    pg.require(topic in (TOPIC,OTHER) and 0<count<=100,'Observation bounds invalid')
    p=kafka(c,'console-consumer',['--command-config','/private/observer.properties','--topic',topic,
        '--partition','0','--offset','earliest','--max-messages',str(count),'--timeout-ms','10000',
        '--formatter-property','print.key=true','--formatter-property','print.partition=true','--formatter-property','print.offset=true',
        '--formatter-property','key.separator='+SEP.decode(),'--formatter-property','line.separator='+END.decode()],timeout=20)
    chunks=p.stdout.split(END)
    pg.require(chunks[-1]==b'' and len(chunks)==count+1,'Incomplete native broker output')
    observations=[]
    for chunk in chunks[:-1]:
        partition,offset,key,body=chunk.split(SEP,3)
        pg.require(partition==b'Partition:0' and offset.startswith(b'Offset:'),'Native metadata missing')
        observations.append({'topic':topic,'partition':0,'offset':int(offset[7:]),
            'keyBase64':base64.b64encode(key).decode(), 'bodyBase64':base64.b64encode(body).decode(),
            'bodySha256':hashlib.sha256(body).hexdigest(), 'source':'Kafka4.3.1-console-consumer-byte-output',
            'authenticatedPrincipal':'observer','owner':c['Owner']})
    path=Path(c['RunDirectory'])/'transport'/('observed-'+stamp()+'.json')
    write(path,observations)
    return path


def stop(c):
    # Exact names and immutable image identities checked before stopping. No compose down/removal.
    for role in ('publisher-dapr','receiver-dapr','kafka'):
        name=c['Project']+'-'+role
        value=inspect(c,name)
        pg.require(value['Image']==IMAGES['kafka' if role=='kafka' else 'dapr'],'Image mismatch')
        if value['State']['Running']: command(c,['docker','stop','--time','20',value['Id']],timeout=30)
        log=command(c,['docker','logs',value['Id']])
        (Path(c['RunDirectory'])/'transport'/(role+'-final.log')).write_bytes(log.stdout+log.stderr)


def main():
    a=argparse.ArgumentParser(); a.add_argument('mode',choices=['ValidatePlan','Prepare','Broker','Dapr','Observe','Stop'])
    a.add_argument('--owner',required=True);a.add_argument('--settings',required=True)
    a.add_argument('--topic',default=TOPIC);a.add_argument('--count',type=int,default=1); args=a.parse_args()
    c=load(args.owner,args.settings)
    if args.mode=='ValidatePlan': print(json.dumps(validate(c),indent=2))
    elif args.mode=='Prepare': prepare(c); print('Private transport plan prepared')
    elif args.mode=='Broker': broker(c); print('Owned authenticated broker and ACLs ready')
    elif args.mode=='Dapr':
        inspect(c,c['Project']+'-kafka',running=True)
        compose(c,'up','-d','--pull','never','publisher-dapr','receiver-dapr'); print('Owned sidecars started')
    elif args.mode=='Observe': print(observe(c,args.topic,args.count))
    elif args.mode=='Stop': stop(c); print('Owned containers stopped; all logs/data retained')


if __name__=='__main__': main()
