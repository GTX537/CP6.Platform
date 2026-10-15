"""Real fixture acceptance. Deliberate application fault injection is labeled.
Only original fixture messages are retried; no payload reconstruction proves broker metadata.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import secrets
import time
import urllib.request
import urllib.error
import urllib.parse
import runtime as r


class Harness:
    def __init__(self,c):
        self.c=c;self.private=Path(c['RunDirectory'])/'transport'
        self.tokens=json.loads((self.private/'secrets.json').read_bytes())['tokens']
        self.run=self.private/('acceptance-'+r.stamp());self.run.mkdir()
        self.results=[]

    def http(self,path,*,data=None,port=18013,auth='control',headers=None):
        h=dict(headers or {})
        if auth: h['x-fixture-control' if auth=='control' else 'dapr-api-token']=self.tokens[auth]
        req=urllib.request.Request(f'http://127.0.0.1:{port}'+path,data=data,headers=h)
        try:
            with urllib.request.urlopen(req,timeout=40) as res: status,body=res.status,res.read()
        except urllib.error.HTTPError as e: status,body=e.code,e.read()
        except (urllib.error.URLError,TimeoutError): status,body=0,b''
        r.write(self.run/(r.stamp()+'-http.json'),{'port':port,'path':path,'authKind':auth,
            'status':status,'responseBase64':base64.b64encode(body).decode(),
            'requestSha256':hashlib.sha256(data).hexdigest() if data is not None else None})
        return status,body

    def check(self,name,condition,**evidence):
        row={'case':name,'passed':bool(condition),'evidence':evidence};self.results.append(row)
        r.write(self.run/'results.json',self.results);print(name,'PASS' if condition else 'FAIL',flush=True)
        if not condition: raise RuntimeError('Runtime assertion failed; all evidence retained: '+name)

    def evidence(self):
        status,b=self.http('/test/evidence');r.pg.require(status==200,'Fixture read failed');return json.loads(b)

    def observe(self,topic=r.TOPIC):
        p=r.kafka(self.c,'get-offsets',['--command-config','/private/observer.properties','--topic',topic,'--time','latest'])
        count=int(p.stdout.decode().strip().split(':')[-1]);r.pg.require(count>0,'No native broker record')
        path=r.observe(self.c,topic,count)
        return json.loads(path.read_bytes())

    def wait_receipt(self,hash_value,disposition):
        for _ in range(30):
            rows=self.evidence()['receives']
            found=[x for x in rows if x['hash']==hash_value and x['disposition']==disposition]
            if found:return found
            time.sleep(.5)
        return []

    def publish(self,body,key,topic=r.TOPIC,auth='dapr'):
        return self.http('/v1.0/publish/cp6-kafka-pubsub/'+topic+'?metadata.rawPayload=true&metadata.partitionKey='+urllib.parse.quote(key,safe=''),
            data=body,port=13500,auth=auth,headers={'Content-Type':'application/cloudevents+json'})

    def run_cases(self):
        status,_=self.http('/healthz',port=18012,auth=None)
        self.check('owned-fixture-health',status==200)
        status,_=self.http('/v1.0/metadata',port=13500,auth=None)
        self.check('dapr-api-rejects-missing-token',status in (401,403),status=status)
        status,_=self.http('/events',data=b'{}',port=18012,auth=None,headers={'__topic':r.TOPIC,'__key':'forged','__offset':'0','__partition':'0'})
        self.check('forged-direct-delivery-headers',status==401,status=status)
        status,b=self.http('/test/dispatch/confirmed',data=b'')
        result=json.loads(b) if status==200 else {}
        self.check('outbox-real-dapr-confirmed',status==200 and result.get('status')=='Published' and not result.get('businessApplied',True),result=result)
        records=self.observe();record=next((x for x in records if x['bodySha256']==result['rawSha256']),None)
        self.check('independent-kafka-record',record is not None,record=record)
        original=base64.b64decode(record['bodyBase64']);key=base64.b64decode(record['keyBase64']).decode()
        self.check('broker-native-key',key=='11111111-1111-4111-8111-111111111111/example-1' and json.loads(original)['id']==result['messageId'],topic=record['topic'],offset=record['offset'],sha256=record['bodySha256'])
        received=self.wait_receipt(result['rawSha256'],'SUCCESS')
        self.check('real-receiver-commit-before-ack',bool(received) and base64.b64decode(received[0]['body'])==original,received=received)
        # Authenticated test injection exercises metadata gate; it is not a forged valid token attack.
        wrapper=json.dumps({'data_base64':base64.b64encode(original).decode()}).encode()
        status,b=self.http('/events',data=wrapper,port=18012,auth='app')
        self.check('missing-native-metadata-retry',status==200 and json.loads(b).get('safeCode')=='NATIVE_METADATA_REQUIRED',response=b.decode(),injected=True)
        status,_=self.publish(original,key)
        self.check('same-original-message-retry-accepted',status==204,status=status)
        records=self.observe();copies=[x for x in records if x['bodySha256']==result['rawSha256']]
        self.check('retry-preserves-original-id-bytes-key',len(copies)==2 and all(base64.b64decode(x['bodyBase64'])==original and base64.b64decode(x['keyBase64']).decode()==key for x in copies),offsets=[x['offset'] for x in copies],noBusinessExactlyOnceClaim=True)
        status,b=self.http('/test/dispatch/unknown',data=b'');unknown=json.loads(b) if status==200 else {}
        self.check('outbox-ack-unknown-keeps-original-mutation',status==200 and unknown.get('status')=='Dispatching' and unknown['observation'][0]['state']=='UNCERTAIN_DELIVERY' and unknown['observation'][0]['mutationId'] is not None,result=unknown,injection='application ACK suppression AFTER actual Dapr successful response')
        records=self.observe()
        self.check('unknown-ack-actually-reached-broker',any(x['bodySha256']==unknown['rawSha256'] for x in records),hash=unknown['rawSha256'])
        unknown_id='wp0212-unknownschema-'+secrets.token_hex(6)
        invalid=original.replace(result['messageId'].encode(),unknown_id.encode()).replace(b'"schemaversion": "1.0.0"',b'"schemaversion": "1.0.9"')
        status,_=self.publish(invalid,key);self.check('unknown-schema-transport-accepted',status==204)
        invalid_hash=hashlib.sha256(invalid).hexdigest();self.observe()
        received=self.wait_receipt(invalid_hash,'DROP')
        stored=[x for x in self.evidence()['quarantine'] if x['hash']==invalid_hash]
        self.check('unknown-schema-durable-original-quarantine',bool(received) and len(stored)==1 and base64.b64decode(stored[0]['body'])==invalid,rows=stored)
        facts=json.loads(stored[0]['factsJson'])
        self.check('quarantine-native-metadata-readback',facts['PartitionKey']==key and facts['Topic']==r.TOPIC and facts['MessageId']==unknown_id and facts['RawEnvelopeSha256']==invalid_hash,nativeReference=facts['NativeReference'])
        # Real broker ACL denials, using authenticated principals without publisher entitlement.
        compact=json.dumps(json.loads(original),separators=(',',':')).encode()
        for principal,topic,name in [('intruder',r.TOPIC,'wrong-producer-acl-denied'),('publisher',r.OTHER,'wrong-topic-acl-denied')]:
            p=r.kafka(self.c,'console-producer',['--producer.config','/private/'+principal+'.properties','--topic',topic,
                '--producer-property','max.block.ms=5000','--producer-property','delivery.timeout.ms=6000',
                '--producer-property','request.timeout.ms=3000','--producer-property','enable.idempotence=false'],data=compact+b'\n',allowed=(0,1),timeout=20)
            self.check(name,b'TopicAuthorizationException' in p.stderr or b'TopicAuthorizationException' in p.stdout,returnCode=p.returncode,stderrHash=hashlib.sha256(p.stderr).hexdigest())
        wrong=original.replace(result['messageId'].encode(),('wp0212-wrongkey-'+secrets.token_hex(6)).encode())
        status,_=self.publish(wrong,'wrong-key');self.check('real-wrong-key-published',status==204)
        records=self.observe();wrong_hash=hashlib.sha256(wrong).hexdigest();wrong_record=next(x for x in records if x['bodySha256']==wrong_hash)
        self.check('actual-wrong-key-observed',base64.b64decode(wrong_record['keyBase64'])==b'wrong-key',record=wrong_record)
        wrapper=json.dumps({'data_base64':base64.b64encode(wrong).decode()}).encode()
        status,b=self.http('/events',data=wrapper,port=18012,auth='app',headers={'__topic':r.TOPIC,'__key':'wrong-key','__partition':'0','__offset':str(wrong_record['offset'])})
        self.check('wrong-key-durable-quarantine-no-effect',status==200 and json.loads(b)['status']=='DROP' and any(x['hash']==wrong_hash for x in self.evidence()['quarantine']),response=b.decode(),injectedDeliveryWithActualBrokerRecord=True)
        # Stop/restart ONLY the owned sidecar, then broker, preserving containers and records.
        for suffix,label in [('publisher-dapr','sidecar-unavailable'),('kafka','broker-unavailable')]:
            native=r.inspect(self.c,self.c['Project']+'-'+suffix,running=True)
            r.command(self.c,['docker','stop','--time','20',native['Id']],timeout=30)
            try:
                status,b=self.http('/test/dispatch/confirmed',data=b'');failure=json.loads(b) if status==200 else {}
                self.check(label,status==200 and failure.get('status')=='Dispatching' and failure['observation'][0]['state']=='UNCERTAIN_DELIVERY',result=failure,controlledOwnedServiceStop=True)
            finally:
                r.inspect(self.c,self.c['Project']+'-'+suffix,running=False)
                r.command(self.c,['docker','start',native['Id']],timeout=30)
        r.write(self.run/'summary.json',{'cases':len(self.results),'passed':sum(x['passed'] for x in self.results),'scope':'native local fixture; no business adoption', 'runDirectory':str(self.run)})
        print(str(self.run),flush=True)


if __name__=='__main__':
    a=argparse.ArgumentParser();a.add_argument('--owner',required=True);a.add_argument('--settings',required=True);args=a.parse_args()
    c=r.load(args.owner,args.settings)
    Harness(c).run_cases()
