"""Retain every original AC; attach local subassertions without upgrading business status."""
from pathlib import Path
import json
HERE=Path(__file__).resolve().parent
source=Path('D:/CP6/docs/development/assessments/20261011/catalogs/foundation.json')
rows=[]
def walk(value):
    if isinstance(value,dict):
        if str(value.get('id','')).startswith('PLT-MSG-01::'): rows.append(value)
        else:
            for child in value.values():walk(child)
    elif isinstance(value,list):
        for child in value:walk(child)
walk(json.loads(source.read_bytes()))
assert len(rows)==40
local={
'MSG01-A1':('Legacy101 + Transport exact tuple tests','Actual Owner event inventory remains absent'),
'MSG01-A2':('IndependentFactsMustMatchEveryEnvelopeAndRouteIdentity; broker-native-key','No real Main/CRM subject authorization'),
'MSG01-A3':('Legacy CloudEvent cases; EffectiveLimit; depth diagnostics','Owner payload limits remain their native contracts'),
'MSG01-A4':('DuplicateBusinessFieldsRejectButRootTraceCompatibilityIsPreserved; legacy trace cases','No real production trace pipeline adoption'),
'MSG01-A5':('retry-preserves-original-id-bytes-key; quarantine retry regression','No business operation replay acceptance'),
'MSG01-A6':('IndependentFacts identity checks; no business-key API introduced','Owner business key and authorization remain required'),
'MSG01-A7':('outbox-real-dapr-confirmed; real-receiver-commit-before-ack; businessApplied=false','Observation commit only, no BusinessApplied'),
'MSG01-A8':('DiagnosticsNeverExposePayloadPropertySecretsAndDepthIsBounded','No tenant operational UI/real field authorization'),
'MSG02-A1':('LoadForTransport pinned bundle; affected legacy bundle cases','Only existing example bundle and test-only synthetic versions'),
'MSG02-A2':('ExactTupleHasNoFallback; NativeC02AdoptionIsDisabled','C02 actual closed five-type route unadopted'),
'MSG02-A3':('RegistrationPinsExactHashAndRejectsDuplicateEvenWhenIdentical','No dynamic contract registration service'),
'MSG02-A4':('Existing compatibility evaluator unchanged','Business semantic compatibility review NOT_RUN'),
'MSG02-A5':('SeparatePinnedVersionsCoexistWithoutLatestFallback','1.0.1 is test-only material, not published acceptance'),
'MSG02-A6':('No conversion or stored payload rewrite path','Owner breaking-version inventory/conversion NOT_RUN'),
'MSG02-A7':('Original bundles/old evidence retained, no deletion API','Real deprecated/retired lifecycle NOT_RUN'),
'MSG02-A8':('Exact route registration and authority rejection gates','Real producer/consumer adoption matrix NOT_RUN'),
'MSG03-A1':('EffectiveLimit, duplicate/depth/media identity cases','C02 depth32 validator remains read-only/unadopted'),
'MSG03-A2':('ExactTupleHasNoFallback; unknown-schema-durable-original-quarantine','No real business handler reached or claimed'),
'MSG03-A3':('Fixed diagnostics; no request field paths emitted','Production logging policy NOT_RUN'),
'MSG03-A4':('Distinct schema hash and rawEnvelopeSha256 fields, native Outbox hash readback','Owner and S6 typed digest domains remain external'),
'MSG03-A5':('independent-kafka-record; retry-preserves-original-id-bytes-key','Only controlled local fixture record corpus'),
'MSG03-A6':('C02 adapter and CRM files untouched; generic profile rejects native adoption','CRM alias/region/outer bindings runtime NOT_RUN'),
'MSG03-A7':('unknown schema/wrong key durable quarantine and independent PG readback','Production retention/recovery policy NOT_RUN'),
'MSG03-A8':('Library cancellation/unknown tests, corrected OutboxV2 dependency','S6 consumer precheckpoint business early exits NOT_RUN'),
'MSG04-A1':('Native generic Kafka/Dapr roundtrip; C02 disabled','Actual C02 producer/publisher/consumer NOT_RUN'),
'MSG04-A2':('Transport Confirmed and fixture observation distinct from businessApplied=false','No business application receipt'),
'MSG04-A3':('SASL ACL denials, authenticated app path, independent actual record key/offset/body','Dedicated local topic boundary only; not production mTLS'),
'MSG04-A4':('Real Outbox commit/send-intent, fixture observation commit/ack, injected unknown ACK','No real business transaction or provider-native ACK-loss claim'),
'MSG04-A5':('Fixed local sidecar endpoint; Dapr token rejection','Service invocation/remote business HTTP routes unchanged'),
'MSG04-A6':('Same original transport message repeated, original bytes/id retained','Cross-message same business key/exactly-once NOT_RUN'),
'MSG04-A7':('Unknown/no fallback tests; controlled native unavailable cases','Business version/offset reconciliation and production retry SLO NOT_RUN'),
'MSG04-A8':('C02 disabled; no fallback route or alternate publisher','Old Bridge/new Kafka production single-writer cutover NOT_RUN'),
'MSG05-A1':('Pinned task image/schema/source identities, sanitized config proof','Real Owner inventory/secret provisioning NOT_RUN'),
'MSG05-A2':('Only generic opt-in; old defaults and Main internal paths unchanged','Other Owners and C02 runtime NOT_RUN'),
'MSG05-A3':('Unknown auth/route/contract gates closed; no BusinessApplied API','Real Host adoption requirements remain closed'),
'MSG05-A4':('Corrected existing OutboxV2 consumed, no shared migration/config edits','Real single-writer switch/backlog inventory NOT_RUN'),
'MSG05-A5':('Original raw rows retained during fixture key expansion, read-only readback','Production rollback/in-flight inventory NOT_RUN'),
'MSG05-A6':('No retention/delete API introduced; owned data/logs retained','Business retention/restore/revocation NOT_RUN'),
'MSG05-A7':('Fixed safe diagnostics; fixture endpoints private token authenticated','Real tenant/app operational UI authorization NOT_RUN'),
'MSG05-A8':('Actual Kafka/Dapr/PG evidence separated from fixture and injections','Original real business acceptance remains NOT_RUN')}
for row in rows:
    key=row['source_case_id']; proof,limit=local[key]
    row['state']='NOT_RUN';row['evidence']=[]
    row['local_subassertion']={'scope':'WP02-12 bounded library/local fixture only','description':proof,'limit':limit,
        'evidence_index':['tests/library/RUNS.md','runtime/acceptance.json','runtime/targeted-review-regression.json','REVIEW.md']}
out={'source_catalog':str(source),'original_ac_count':40,'original_business_pass_count':0,
     'rule':'Local subassertions do not change original AC status. Runtime proof is only the named native fixture; injections stay labeled.', 'cases':rows}
(HERE/'acceptance-map.json').write_bytes((json.dumps(out,ensure_ascii=False,indent=2)+'\n').encode())
print('Mapped 40 original ACs; all original business status remains NOT_RUN')
