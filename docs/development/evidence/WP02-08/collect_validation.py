"""Read-only Git/build inspection; writes task evidence only. Does not connect to databases or emit credentials."""
from pathlib import Path
import hashlib, json, subprocess, xml.etree.ElementTree as ET
from datetime import datetime, timezone

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
def git(*args):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', '-C', str(ROOT), *args])
def sha(raw): return hashlib.sha256(raw).hexdigest()
def write(name, value): (HERE/name).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

head = git('rev-parse', 'HEAD').decode().strip()
paths = git('ls-files', '-z').decode().split('\0')
sources = []
prefixes = ('src/', 'contracts/', 'tests/CP6.Platform.UnitTests/', 'tests/CP6.Platform.ArchitectureTests/', 'tests/CP6.Platform.WP0208.PostgreSqlTests/')
for rel in paths:
    if not rel: continue
    if not ((rel.startswith(prefixes) and Path(rel).suffix in ('.cs', '.csproj', '.props', '.json', '.py')) or rel in ('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'NuGet.config')): continue
    raw = (ROOT/rel).read_bytes()
    blob = git('show', head+':'+rel)
    sources.append(dict(path=rel, bytes=len(raw), working_sha256=sha(raw), git_bytes_sha256=sha(blob), git_blob=git('rev-parse', head+':'+rel).decode().strip()))
dependencies, artifacts = [], []
for path in ROOT.rglob('project.assets.json'):
    if 'obj' not in path.parts: continue
    raw = path.read_bytes(); data = json.loads(raw)
    dependencies.append(dict(path=path.relative_to(ROOT).as_posix(), sha256=sha(raw), libraries=sorted(data['libraries'])))
for project in ('CP6.Platform.UnitTests', 'CP6.Platform.ArchitectureTests', 'CP6.Platform.WP0208.PostgreSqlTests'):
    for path in (ROOT/'tests'/project/'bin'/'Debug'/'net8.0').glob('*'):
        if path.suffix in ('.dll', '.pdb', '.json'):
            artifacts.append(dict(path=path.relative_to(ROOT).as_posix(), bytes=path.stat().st_size, sha256=sha(path.read_bytes())))
write('validation-inputs.json', dict(observed_utc=datetime.now(timezone.utc).isoformat(), source_commit=head,
    configuration='Debug/net8.0', sdk='8.0.424', runtime='8.0.30',
    note='Working and canonical Git bytes recorded separately. Builds executed before the source commit; informational Git SHA may be baseline+dirty. No rebuild claimed for commit-only changes.',
    sources=sources, dependencies=dependencies, artifacts=artifacts))
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
runs=[]
for path in sorted((HERE/'tests').rglob('*.trx')):
    tree=ET.parse(path)
    runs.append(dict(path=path.relative_to(HERE).as_posix(), sha256=sha(path.read_bytes()), times=tree.find('t:Times',ns).attrib,
        counters=tree.find('t:ResultSummary/t:Counters',ns).attrib,
        results=[dict(testName=x.get('testName'),outcome=x.get('outcome')) for x in tree.findall('t:Results/t:UnitTestResult',ns)]))
write('test-runs.json', runs)
full=json.loads((HERE/'tests/pg-agent-review-06-hashes.json').read_text(encoding='utf-8-sig'))
deltas=[]
for entry in full:
    path=Path(entry['Path']); actual=sha(path.read_bytes())
    if actual.lower()!=entry['SHA256'].lower():
        deltas.append(dict(path=path.relative_to(ROOT).as_posix(), full_pg_run_sha256=entry['SHA256'].lower(), final_sha256=actual))
write('validation-reuse.json', dict(full_pg_run='tests/pg-agent-review-06/pg-agent-review-06.trx', full_pg_result='38 passed / 0 failed / 0 skipped',
    source_artifact_snapshot='tests/pg-agent-review-06-hashes.json', differences_after_full_run=deltas,
    later_checks=['tests/07-unit-final.trx:15/15 (strict instant equality, codec and related legacy regression)',
        'tests/08-expiry-final.trx:1/1 (actual DB clock expiry after equivalent helper extraction)',
        'tests/09-mapping-final.trx:5/5 (required-property presence; four malformed mappings and real successful claim)'],
    reuse_reason='Only strict comparison helper extraction and mandatory EF property presence plus two new mapping cases changed after full PG run. Relevant expiry/mapping/positive regressions passed; other PG results reused from their recorded source. Architecture 98/98 from05 reused; no dependency/public shared-contract/project-root changes since it.',
    distinct_successes=dict(unit_and_related=15, postgresql=40, architecture=98, total=153),
    not_run=['real Main/CRM Owner/host adoption', 'actual broker/network end-to-end', 'production migration/cutover', 'original 60 business AC', 'formal package/release/deploy']))
mapping=json.loads((HERE/'acceptance-map.json').read_text(encoding='utf-8-sig'))
groups={
 'DEL01-A1':['Dlq_mutation_rolls_back_native_binding_receipt_dlq_audit_and_seal'],
 'DEL01-A2':['Prepared_claim_is_invisible_until_commit_and_rollback_restores_complete_graph'],
 'DEL01-A8':['Real_commit_then_suppressed_ack_recovers_original_mutation_after_write_revocation'],
 'DEL03-A1':['Every_legacy_writer_excludes_bound_rows_while_unbound_claim_complete_and_retention_work','Native_codec_preserves_unsigned_bits_and_network_order'],
 'DEL03-A2':['Retention_rolls_back_exact_graph_then_commits_deletion_with_original_receipts_preserved'],
 'DEL03-A3':['Ticket_uses_actual_post_write_xmin_and_native_and_binding_are_sealed'],
 'DEL03-A5':['Actual_catalog_guard_predicate_and_unique_index_are_required','Altered_companion_token_or_root_mapping_is_rejected_before_dml','Missing_actual_owner_authority_row_fence_disables_write_binding'],
 'DEL03-A6':['Actual_23505_aborts_transaction_and_original_key_retry_returns_original_ticket'],
 'DEL03-A7':['Root_identity_does_not_have_delimiter_ambiguity_and_ticket_diagnostics_are_redacted'],
 'DEL04-A1':['Prepared_claim_is_invisible_until_commit_and_rollback_restores_complete_graph','Ticket_uses_actual_post_write_xmin_and_native_and_binding_are_sealed'],
 'DEL04-A2':['Epoch_and_completion_serialize_in_both_lock_orderings','Same_claim_key_replays_exact_tuple_and_changed_tuple_has_empty_write_set'],
 'DEL04-A3':['Current_native_binding_hash_tenant_and_epoch_are_rechecked','Renewal_mints_post_write_ticket_and_old_worker_ticket_loses_authority','Database_fence_is_strict_at_expiry_and_compares_instants','Database_wall_clock_expiry_rejects_completion_even_when_transaction_started_before_expiry'],
 'DEL04-A4':['Dispatcher_publishes_without_locks_and_ambiguous_or_unconfirmed_completion_never_republishes'],
 'DEL04-A5':['Dlq_mutation_rolls_back_native_binding_receipt_dlq_audit_and_seal'],
 'DEL04-A6':['Real_commit_then_suppressed_ack_recovers_original_mutation_after_write_revocation','Current_native_binding_hash_tenant_and_epoch_are_rechecked','Actual_23505_aborts_transaction_and_original_key_retry_returns_original_ticket','Concurrent_serializable_claim_loser_gets_40001_then_original_request_can_reenter'],
 'DEL04-A7':['Epoch_and_completion_serialize_in_both_lock_orderings','Drain_denies_claim_and_renew_but_allows_current_completion_then_epoch_advance','Unknown_handover_blocks_epoch_and_caller_rollback_restores_root_and_history'],
 'DEL04-A8':['Every_legacy_writer_excludes_bound_rows_while_unbound_claim_complete_and_retention_work','Direct_legacy_DML_cannot_bypass_database_guard','Missing_guard_and_unsupported_provider_fail_closed'],
 'DEL05-A3':['Real_commit_then_suppressed_ack_recovers_original_mutation_after_write_revocation'],
 'DEL06-A2':['Exact_retention_checks_current_owner_obligations'],
 'DEL06-A3':['Hold_and_cleanup_use_same_root_in_both_lock_orderings'],
 'DEL06-A4':['Retention_rolls_back_exact_graph_then_commits_deletion_with_original_receipts_preserved'],
 'DEL06-A5':['Retention_rolls_back_exact_graph_then_commits_deletion_with_original_receipts_preserved'],
 'DEL06-A7':['Exact_retention_checks_current_owner_obligations'],
 'DEL06-A8':['Exact_retention_checks_current_owner_obligations'],
 'DEL07-A1':['Prepared_claim_is_invisible_until_commit_and_rollback_restores_complete_graph','Every_legacy_writer_excludes_bound_rows_while_unbound_claim_complete_and_retention_work'],
 'DEL07-A3':['Dlq_mutation_rolls_back_native_binding_receipt_dlq_audit_and_seal'],
 'DEL07-A4':['Hold_and_cleanup_use_same_root_in_both_lock_orderings','Missing_actual_owner_authority_row_fence_disables_write_binding'],
 'DEL07-A6':['Real_commit_then_suppressed_ack_recovers_original_mutation_after_write_revocation','Concurrent_serializable_claim_loser_gets_40001_then_original_request_can_reenter'],
 'DEL07-A7':['Actual_catalog_guard_predicate_and_unique_index_are_required','Epoch_and_completion_serialize_in_both_lock_orderings'],
 'DEL07-A8':['Retention_rolls_back_exact_graph_then_commits_deletion_with_original_receipts_preserved','Epoch_and_completion_serialize_in_both_lock_orderings']}
for row in mapping['cases']:
    key=row['source_case_id']; names=groups.get(key,[])
    evidence=[]
    for name in names:
        matches=[run['path'] for run in runs if any(name in r['testName'] and r['outcome']=='Passed' for r in run['results'])]
        assert matches, name
        evidence.extend(matches)
    row['library_subassertions']=dict(state='VERIFIED_LIBRARY_SUBASSERTIONS_ONLY' if names else 'PRESERVED_OR_OUTSIDE_TASK_NOT_RUN',
        tests=names,evidence=sorted(set(evidence)),
        scope='Only the named fixture/library assertions are verified. Actual Owner policy, archive restore, writer inventory, full transaction graph, broker and original business acceptance are not established.' if names else
            'Existing InboxV1/legacy technical behavior and original Owner responsibility preserved; no new execution or acceptance claim for this AC.')
    if key=='DEL04-A3': row['library_subassertions']['expiry_boundary']='Exact before/equal/after instants verified in unit contract; realPG verifies current DB-clock expiry after the boundary. Final SQL uses strict <, no claim that wall-clock test hit the exact same microsecond.'
assert len(mapping['cases'])==60 and all(x['state']=='NOT_RUN' and x['original_business_acceptance']['state']=='NOT_RUN' for x in mapping['cases'])
write('acceptance-map.json',mapping)
print(f'{len(sources)} source pairs; {len(dependencies)} dependency graphs; {len(artifacts)} artifact hashes; {len(runs)} TRX retained; all60 original business AC NOT_RUN.')
