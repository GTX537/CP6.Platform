"""Targeted task-review regression using existing rejected original bytes; no queue reset."""
import argparse
import base64
import json
import time
import runtime as r
from verify_runtime import Harness

a=argparse.ArgumentParser();a.add_argument('--owner',required=True);a.add_argument('--settings',required=True);args=a.parse_args()
c=r.load(args.owner,args.settings);h=Harness(c)
records=h.observe()
bad=next(x for x in records if json.loads(base64.b64decode(x['bodyBase64']))['schemaversion']=='1.0.9')
raw=base64.b64decode(bad['bodyBase64']);key=base64.b64decode(bad['keyBase64']).decode()
before=[x for x in h.evidence()['quarantine'] if x['hash']==bad['bodySha256']]
h.check('original-invalid-quarantine-retained',len(before)==1 and base64.b64decode(before[0]['body'])==raw)
status,_=h.publish(raw,key);h.check('retry-same-invalid-original-published',status==204)
records=h.observe();copies=[x for x in records if x['bodySha256']==bad['bodySha256']]
h.check('invalid-retry-distinct-native-offsets',len(copies)==2 and copies[0]['offset']!=copies[1]['offset'] and all(base64.b64decode(x['bodyBase64'])==raw for x in copies),offsets=[x['offset'] for x in copies])
for _ in range(40):
    evidence=h.evidence();saved=[x for x in evidence['quarantine'] if x['hash']==bad['bodySha256']]
    if len(saved)==2:break
    time.sleep(.5)
h.check('two-committed-quarantine-observations-no-overwrite',len(saved)==2 and len({x['nativeReference'] for x in saved})==2 and all(base64.b64decode(x['body'])==raw for x in saved),rows=saved)
receives=[x for x in evidence['receives'] if x['hash']==bad['bodySha256']]
h.check('real-dapr-bad-retry-drops-after-both-commits',len(receives)==2 and all(x['disposition']=='DROP' for x in receives),receives=receives)
wrong=next(x for x in records if base64.b64decode(x['keyBase64'])==b'wrong-key')
saved_wrong=[x for x in evidence['quarantine'] if x['hash']==wrong['bodySha256']]
h.check('wrong-native-key-is-durably-isolated',len(saved_wrong)==1 and json.loads(saved_wrong[0]['factsJson'])['PartitionKey']=='wrong-key',rows=saved_wrong)
h.check('no-business-effect-claim',evidence['businessApplied'] is False)
r.write(h.run/'summary.json',{'cases':len(h.results),'passed':sum(x['passed'] for x in h.results),'scope':'targeted real Kafka/Dapr/PG task-review regression'})
print(h.run)
