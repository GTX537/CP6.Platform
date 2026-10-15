"""Bind scoped local portions to actual passed TRX names; retain all 56 original NOT_RUN cases."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[2]
RUN=ROOT/'docs/development/evidence/WP02-26/20261015T001945Z'
p=argparse.ArgumentParser(__doc__)
p.add_argument('--unit',type=Path,required=True)
p.add_argument('--pg',type=Path,required=True)
p.add_argument('--label',required=True)
args=p.parse_args()
records=[]
for file in (args.unit,args.pg):
    file=file.resolve()
    if not file.is_relative_to(RUN): raise ValueError('TRX outside task run')
    raw=file.read_bytes()
    for r in ET.fromstring(raw).findall('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult'):
        records.append(dict(trx=file.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(raw).hexdigest(),test_name=r.attrib['testName'],outcome=r.attrib['outcome']))
bindings={}
def bind(case,scope,*methods):
    proof=[]
    for method in methods:
        matches=[r for r in records if '.'+method in r['test_name']]
        if not matches or any(r['outcome']!='Passed' for r in matches): raise ValueError('No passed exact method evidence: '+method)
        proof.extend(matches)
    bindings[case]=dict(local_result='LOCAL_PASS',local_scope=scope,evidence=proof)

bind('OBS01-A3','Actual synthetic fixture technical success followed by native rollback; not a proof about actual application Owner counters.','TechnicalSuccessBeforeRollbackDoesNotProveOwnerCommit')
bind('OBS02-A1','Unchanged legacy parser/schema/wire regression only.','SchemaExamples_MatchDeclaredOutcomes','Parse_RejectsDuplicateJsonPropertiesWithoutEchoingValues','ReleaseIdentity_SerializesOnlyTheApprovedWireShape')
bind('OBS02-A2','Unchanged legacy fail-closed evaluator plus explicit separate local recomputation.','Evaluate_UsesFailClosedMatrix','Same_frozen_bytes_recompute_exact_K_over_N_and_remain_nonproduction')
bind('OBS02-A3','Actual supplied immutable local bytes and independent caller pins compared; producer authenticity and trusted runtime provenance remain unproven.','Same_frozen_bytes_recompute_exact_K_over_N_and_remain_nonproduction','Forged_self_count_inventory_and_query_drift_do_not_replace_independent_binding')
bind('OBS02-A4','Synthetic exact ratio, weighted nearest-rank and gauge grid recalculation; no business SLI threshold approval.','Same_frozen_bytes_recompute_exact_K_over_N_and_remain_nonproduction','Weighted_nearest_rank_is_not_average_of_percentiles','Gauge_maximum_uses_bound_sampling_grid_and_tail_gap')
bind('OBS02-A5','Missing/altered synthetic inventory, bad flags, gaps, zero and incomplete source cases; real population not qualified.','Bad_evidence_cannot_qualify','Missing_required_false_or_zero_field_cannot_be_defaulted_into_completeness','Gauge_sampling_gap_is_rejected_even_when_member_count_matches')
bind('OBS02-A6','Local qualification always excludes production/independent acceptance; no production authorization exercised.','Same_frozen_bytes_recompute_exact_K_over_N_and_remain_nonproduction')
bind('OBS02-A7','In-memory immutable versions preserve prior failure and reject overwrite; mixed-unbound releases rejected; no durable production retention/fleet rollout.','Journal_retains_original_failure_and_refuses_late_overwrite','Bad_evidence_cannot_qualify')
bind('TEST01-A1','Legacy four-outcome semantics and explicitly bounded new terminal behavior.','FaultScript_ExecutesStatusExceptionDelayAndSuccessExactlyOnce','Status_and_throw_never_reach_terminal','Success_and_delay_reach_only_named_terminal_with_deterministic_clock_and_random')
bind('TEST01-A2','New sealed in-memory fixture terminal cannot fall back to network; no real remote transport qualification.','Success_and_delay_reach_only_named_terminal_with_deterministic_clock_and_random','Cancellation_after_script_observer_prevents_inner_and_terminal_exhaustion_has_no_fallback')
bind('TEST01-A3','Unchanged exact Test/CI registration regression; production dependency adoption remains external.','FaultInjection_RejectsNonTestEnvironmentBeforeChangingServices','FaultInjection_AcceptsOnlyExactApprovedEnvironments')
bind('TEST01-A4','Finite script/inner counters and exhaustion are separated; no claim these count real network sends.','Retry_placement_explicitly_changes_script_and_terminal_counts','Exhaustion_cancellation_observer_and_cleanup_failures_are_visible')
bind('TEST01-A5','Each new fault case owns its random/clock/script; native database concurrency independently observed, no local sequence commit-order inference.','Success_and_delay_reach_only_named_terminal_with_deterministic_clock_and_random','ConcurrentOriginalKeysCommitOnceAndPreserveNativeExactValues')
bind('TEST01-A6','Virtual clock, caller cancellation and explicit inner/outer retry placement.','Retry_placement_explicitly_changes_script_and_terminal_counts','FaultDelay_PreservesCallerCancellationAndSkipsTransport')
bind('TEST01-A7','Synthetic local retry placement and injected lost application ACK recover original actual PG receipt; real HTTP Owner idempotency contract not adopted.','Retry_placement_explicitly_changes_script_and_terminal_counts','InjectedLostApplicationAckReadsOriginalCommittedReceiptWithoutNewKeys')
bind('TEST01-A8','Real owned PG fixture transactions/crash; no actual Main/CRM Owner/fence or production fault interface.','ControlledOwnedClusterCrashRecoversCommittedBytesAndRollsBackUncommittedKeys','TechnicalSuccessBeforeRollbackDoesNotProveOwnerCommit')
bind('TEST02-A1','Legacy capture regression plus opt-in exact CLR/arbitrary-precision and original-byte distinction; local sequence is not commit order.','Recorder_CapturesConcurrentActivitiesAndMetricsInSequence','Clr_kind_and_binary_rational_are_preserved','Exact_assertions_distinguish_large_or_fine_values','Numeric_equality_does_not_erase_original_lexical_or_bytes','Nonterminating_rational_is_never_rounded','Pinned_S01_vectors_are_consumed_as_arithmetic_provenance_not_owner_acceptance')
bind('TEST02-A2','Actual Activity parent/link/ancestor graph retains intermediate Dapr/retry/fanout spans; synthetic in-process graph only.','Actual_fanout_retry_intermediate_Dapr_receive_and_apply_DAG_is_preserved','Graph_rejects_cycles_duplicate_span_ids_and_missing_ancestor_edges')
bind('TEST02-A3','Legacy allowed-key/forbidden-token assertion scope remains unchanged; no all-channel privacy proof.','Recorder_RejectsForbiddenTagNamesAndValues')
bind('TEST02-A5','Actual concurrent cases and bounded capture flags; no real external exporter completeness.','Concurrent_cases_capture_only_their_own_metrics','Limits_and_lifecycle_failures_never_produce_complete','Cross_case_and_dangling_edges_are_not_joined')
bind('TEST02-A6','Listener disposal, in-flight detection and case separation.','Dispose_with_inflight_span_cannot_claim_complete_or_capture_afterwards','Recorder_DisposeStopsBothListeners')
bind('TEST02-A7','Native PG rollback counterexample and exact receipt keys, no actual application Owner adapter.','TechnicalSuccessBeforeRollbackDoesNotProveOwnerCommit','ConcurrentOriginalKeysCommitOnceAndPreserveNativeExactValues')
bind('TEST02-A8','Synthetic observer exceptions keep terminal result separate from incomplete evidence.','Exhaustion_cancellation_observer_and_cleanup_failures_are_visible')
bind('TEST03-A4','Fresh owned PG identity and restricted DML role actually verified; exact case input/clock sources remain fixture-bound.','RestrictedRuntimeCannotCreateTruncateOrRewriteIdentityGuard','ControlledOwnedClusterCrashRecoversCommittedBytesAndRollsBackUncommittedKeys')
bind('TEST03-A6','Cancellation, missing input/capture and cleanup failures cannot become Complete.','Exhaustion_cancellation_observer_and_cleanup_failures_are_visible','Limits_and_lifecycle_failures_never_produce_complete','Missing_required_false_or_zero_field_cannot_be_defaulted_into_completeness')
bind('TEST03-A7','Local immutable journal keeps prior artifacts/failures; repository preserves distinct attempts; no external retention claim.','Journal_retains_original_failure_and_refuses_late_overwrite')

catalog=json.loads((RUN/'inputs/catalog.json').read_bytes())
rows=[]
for target in catalog:
    for case in target['cases']:
        key=case['id'].split('::')[-1]
        local=bindings.get(key,dict(local_result='NOT_RUN',local_scope='No new execution claimed for this original case.',evidence=[]))
        rows.append(dict(**case,original_business_acceptance='NOT_RUN',local_result=local['local_result'],local_scope=local['local_scope'],local_evidence=local['evidence'],
            remaining='Actual Main/CRM/Owner acceptance, trusted producer/environment/production sources, authorization, durable retention/hold and independent acceptance remain external as applicable.'))
if len(rows)!=56 or len({r['id'] for r in rows})!=56: raise ValueError('Original 32 OBS + 24 TEST case identity mismatch')
path=RUN/f'acceptance-{args.label}.json'
with path.open('x',encoding='utf-8') as f: json.dump(dict(original_count=56,original_business_acceptance='NOT_RUN',rows=rows),f,ensure_ascii=False,indent=2)
print(f'Preserved all 56 original NOT_RUN cases; {len(bindings)} explicitly scoped local portions bound to passed TRX.')
