"""Read-only source/build inspection; writes only task evidence JSON. No credentials or remote mutations."""
from pathlib import Path
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
MAIN = Path('D:/CP6')
PIN = 'a1824fec3d4824d45e27693f2e715b311ee5f665'


def git(root, *args):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', '-C', str(root), *args])


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write(name, value):
    (HERE/name).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')


def main():
    admission = json.loads((MAIN/'docs/development/evidence/wave5-dispatch-20261013/admission-platform.json').read_text(encoding='utf-8-sig'))
    inputs = []
    for item in admission['source_evidence']:
        if item['repository'] not in ('Main', 'Platform'):
            continue
        repo = MAIN if item['repository'] == 'Main' else ROOT
        blob = git(repo, 'rev-parse', item['pin']+':'+item['path']).decode().strip()
        assert blob == item['git_blob'], item['path']
        raw = git(repo, 'show', item['pin']+':'+item['path'])
        inputs.append(dict(item, git_bytes_sha256=sha(raw)))
    for rel, expected in [
        ('docs/CP6_完整成果归档_20261009/完整成果/docs/originals/64/642a2fbc35d17fbf__PLT-DEL-01_完整静态设计_v0.2.1.md', '642a2fbc35d17fbfaa0bcc51617e24654cbe193e0f907674a3e6f4c41c47eb84'),
        ('docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md', 'd9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc')]:
        raw = (MAIN/rel).read_bytes()
        assert sha(raw) == expected
        inputs.append(dict(repository='Main archive (raw working original)', path=rel, raw_sha256=expected, bytes=len(raw)))
    for rel in ['docs/design-handbook/remediation/assets/finance-recovery/contracts.schema.json',
                'docs/development/assessments/20261011/catalogs/foundation.json',
                'docs/development/assessments/20261011/implementation-assessment.json']:
        raw = git(MAIN, 'show', PIN+':'+rel)
        inputs.append(dict(repository='Main', path=rel, pin=PIN, git_blob=git(MAIN, 'rev-parse', PIN+':'+rel).decode().strip(), git_bytes_sha256=sha(raw)))
    dispatch_pin = '47c9b12c796620effe41c62d16992ccd629ee949'
    for rel in ['docs/development/dispatch/20261013-wave5/WP02-07.md',
                'docs/development/evidence/wave5-dispatch-20261013/admission-platform.md',
                'docs/development/evidence/wave5-dispatch-20261013/admission-platform.json']:
        raw = git(MAIN, 'show', dispatch_pin+':'+rel)
        inputs.append(dict(repository='Main', path=rel, pin=dispatch_pin, git_blob=git(MAIN, 'rev-parse', dispatch_pin+':'+rel).decode().strip(), git_bytes_sha256=sha(raw)))
    write('source-inputs.json', inputs)

    catalogue_path = 'docs/development/assessments/20261011/catalogs/foundation.json'
    catalogue = json.loads(git(MAIN, 'show', PIN+':'+catalogue_path))
    target = next(t for t in catalogue['targets'] if t['id'] == 'PLT-DEL-01')
    assert len(target['cases']) == 60
    local = {
        'DEL01-A1': ('Pending_is_invisible_until_caller_commit_and_all_nine_tables_commit_together', 'Receiver business/journal/evidence same commit only; producer Outbox remains outside task.'),
        'DEL01-A2': ('Missing_wrong_connection_ended_transaction_and_wrong_tenant_are_rejected', 'Exact native handles only; original Outbox Enqueue unchanged, not exercised here.'),
        'DEL01-A3': ('Lost_entire_response_recovers_by_original_key_only_and_cannot_ack_a_delivery', 'Immutable Owner-key technical copy and fixture original journal; real producer/Owner adoption not run.'),
        'DEL01-A8': ('Lost_commit_confirmation_queries_original_bytes_without_a_new_write_grant', 'Real commit plus application-level confirmation-loss injection; no broker/network acceptance claim.'),
        'DEL02-A1': ('Duplicate_old_equal_and_known_historical_conflict_all_pass_the_content_gate;Actual_unique_violation_is_rolled_back_then_original_reenters_admission', 'All new-entry early decisions and whole-transaction retry pass content admission. Legacy entry unchanged.'),
        'DEL02-A2': ('Same_message_different_raw_or_binding_has_priority_and_preserves_original_result;Duplicate_old_equal_and_known_historical_conflict_all_pass_the_content_gate', 'Distinct original raw/data bytes and conflict preservation in the fixture.'),
        'DEL02-A3': ('Conflict_write_set_rolls_back_together_with_published_health_and_original_bytes', 'Fixture projection/Published/observation/journal/technical evidence atomicity; CRM actual audit/reconcile scheduling not adopted.'),
        'DEL02-A4': ('Duplicate_old_equal_and_known_historical_conflict_all_pass_the_content_gate', 'Historical observed conflict wins over duplicate/old; projection/checkpoint do not regress.'),
        'DEL02-A5': ('Real_transaction_locks_are_held_until_caller_rollback;Wrong_lock_order_and_preheld_late_lock_are_not_accepted_as_proof', 'Owner-delegated exact advisory profile only; seven real transaction locks, no library commit.'),
        'DEL02-A6': ('Pending_is_invisible_until_caller_commit_and_all_nine_tables_commit_together;Lost_entire_response_recovers_by_original_key_only_and_cannot_ack_a_delivery', 'Pending has no transport mapping; readonly exact receipt/original-key recovery.'),
        'DEL02-A7': ('Authority_wrong_tenant_or_missing_lineage_never_becomes_available_from_incoming', 'Library consumes Owner snapshot and closes missing/wrong anchors; real source snapshot/revocation origin adoption not run.'),
        'DEL02-A8': ('Unavailable_anchor_never_completes_duplicate_or_checkpoint', 'Dependency closes new success; Owner recovery selection/CAS and competing reconciliation remain NOT_RUN.'),
        'DEL03-A3': ('Two_contexts_can_participate_only_when_the_actual_connection_and_transaction_are_shared;Real_xmin_token_rejects_stale_checkpoint_update', 'Only recorded PostgreSQL18.6/Npgsql8/EF8 Windows combination; no other provider/host assertion.'),
        'DEL03-A6': ('Actual_unique_violation_is_rolled_back_then_original_reenters_admission;Same_key_competition_recovers_whole_original_input_through_a_new_content_gate', 'Actual23505, original input/key retained; no internal exception bypass.'),
        'DEL05-A3': ('Lost_commit_confirmation_queries_original_bytes_without_a_new_write_grant', 'Original readonly recovery uses current read policy; full ERP replay lookup/eligibility not adopted.'),
        'DEL05-A4': ('Same_transport_cannot_be_rebound_to_another_original_operation', 'Immutable original-key binding only; full replay tuple/queue revision remains other Owner task.'),
        'DEL05-A8': ('Final_write_denial_and_cancellation_do_not_create_success_receipts;Lost_commit_confirmation_queries_original_bytes_without_a_new_write_grant', 'Fixture write denial and independent read policy; real security fence shared-writer adoption not run.'),
        'DEL07-A1': ('Pending_is_invisible_until_caller_commit_and_all_nine_tables_commit_together', 'New caller-owned C entry explicit, legacy B/Outbox A unchanged; no requeue nesting test.'),
        'DEL07-A2': ('Two_contexts_can_participate_only_when_the_actual_connection_and_transaction_are_shared;Missing_wrong_connection_ended_transaction_and_wrong_tenant_are_rejected', 'Actual shared connection/transaction works, separate physical connection rejected.'),
        'DEL07-A3': ('Explicit_rollback_or_failure_after_owner_save_leaves_no_partial_graph', 'Nine receiver fixture tables only; full business/Outbox writer inventory remains host obligation.'),
        'DEL07-A4': ('Final_write_denial_and_cancellation_do_not_create_success_receipts', 'Trusted Owner hook refuses fresh writes when its fixture gate denies; real IAM/Owner final-fence protocol not implemented by Platform.'),
        'DEL07-A6': ('Actual_serialization_failure_preserves_original_version_key_on_retry;Actual_unique_violation_is_rolled_back_then_original_reenters_admission', 'Actual40001/23505 and re-entry in fresh caller transaction, no replacement key.'),
    }
    mapping = []
    for case in target['cases']:
        key = case['id'].split('::')[-1]
        row = {'original': case, 'original_acceptance_status': 'NOT_RUN', 'host_adoption': 'UNPROVEN'}
        if key in local:
            names, limit = local[key]
            row.update(wp0207_scope='LOCAL_LIBRARY_SUBASSERTIONS_ONLY', tests=names.split(';'), limitation=limit)
        elif key in ('DEL02-A9', 'DEL03-A1', 'DEL03-A2', 'DEL03-A4', 'DEL03-A5', 'DEL06-A2', 'DEL06-A4', 'DEL06-A5', 'DEL07-A5', 'DEL07-A8', 'DEL07-A9'):
            row.update(wp0207_scope='STATIC_COMPATIBILITY_OR_BOUNDARY_ONLY', tests=[], limitation='Legacy source unchanged; new technical copy/tables opt-in and separate from legacy retention. No historical migration, ERP/CRM adoption, retention execution or business acceptance claim. See complete diff and integration document.')
        else:
            row.update(wp0207_scope='OUTSIDE_TASK_NOT_RUN', tests=[], limitation='Producer/lease/requeue/retention/UI/route-cutover and real Owner governance remain their original Owner/follow-up obligations; no AC removed or reduced.')
        mapping.append(row)
    write('acceptance-map.json', dict(source_pin=PIN, catalogue_path=catalogue_path, original_case_count=60,
        business_acceptance='NOT_RUN', library_tests_are_not_original_AC_passes=True, cases=mapping))

    source_commit = git(ROOT, 'rev-parse', 'HEAD').decode().strip()
    files = git(ROOT, 'ls-files', '-z').decode().split('\0')
    sources = []
    for rel in files:
        if not rel:
            continue
        accepted = (rel.startswith(('src/', 'contracts/', 'tests/CP6.Platform.UnitTests/', 'tests/CP6.Platform.ArchitectureTests/', 'tests/CP6.Platform.WP0207.PostgreSqlTests/'))
                    and Path(rel).suffix in ('.cs', '.csproj', '.json', '.props', '.py')) or rel in ('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'NuGet.config')
        if accepted:
            data = (ROOT/rel).read_bytes()
            blob = git(ROOT, 'show', source_commit+':'+rel)
            sources.append(dict(path=rel, bytes=len(data), working_sha256=sha(data), git_bytes_sha256=sha(blob), git_blob=git(ROOT, 'rev-parse', source_commit+':'+rel).decode().strip()))
    dependencies, artifacts = [], []
    for path in ROOT.rglob('project.assets.json'):
        if 'obj' not in path.parts:
            continue
        data = path.read_bytes()
        j = json.loads(data)
        dependencies.append(dict(path=path.relative_to(ROOT).as_posix(), sha256=sha(data), libraries=sorted(j['libraries'].keys())))
    for project in ['CP6.Platform.UnitTests', 'CP6.Platform.ArchitectureTests', 'CP6.Platform.WP0207.PostgreSqlTests']:
        for path in (ROOT/'tests'/project/'bin'/'Debug'/'net8.0').glob('*'):
            if path.suffix in ('.dll', '.pdb', '.json'):
                artifacts.append(dict(path=path.relative_to(ROOT).as_posix(), bytes=path.stat().st_size, sha256=sha(path.read_bytes())))
    write('validation-inputs.json', dict(source_commit=source_commit, configuration='Debug/net8.0', sources=sources, dependencies=dependencies, artifacts=artifacts))
    runs = []
    ns = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    for path in sorted((HERE/'test-results').glob('*.trx')):
        tree = ET.parse(path)
        counters = tree.find('t:ResultSummary/t:Counters', ns)
        runs.append(dict(path=path.relative_to(HERE).as_posix(), sha256=sha(path.read_bytes()),
                         times=tree.find('t:Times', ns).attrib, counters=counters.attrib,
                         results=[dict(testName=x.get('testName'), outcome=x.get('outcome')) for x in tree.findall('t:Results/t:UnitTestResult', ns)]))
    write('test-runs.json', runs)
    print(f'{len(inputs)} fixed input identities; {len(mapping)} original AC; {len(sources)} working/Git source pairs; {len(dependencies)} dependency graphs; {len(artifacts)} artifacts; {len(runs)} actual TRX runs')


if __name__ == '__main__':
    main()
