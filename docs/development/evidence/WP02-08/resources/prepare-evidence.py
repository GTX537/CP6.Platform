"""Reproduce task source and exact original-AC identity evidence (no credentials)."""
import datetime
import hashlib
import json
from pathlib import Path
import subprocess

OUT = Path(__file__).resolve().parent.parent
PLATFORM = OUT.parents[3]
MAIN = Path('D:/CP6')
MAIN_SHA = '1ab2d031e3c501662ecb89a33839fac1cba55d33'
PLATFORM_SHA = 'e5c63a28c94bae4cc66ec50fbe6079553c03d0d0'
HANDOFF_SHA = 'ad255c21810a101662fd3e380992a03fde71407e'


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def write(name, value):
    (OUT/name).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')


admission = MAIN/'docs/development/evidence/wave6-dispatch-20261013/admission-outbox.json'
spec = json.loads(admission.read_text(encoding='utf-8-sig'))
inputs = []
for source in spec['sources']:
    item = dict(source)
    original = source['path']
    if original.startswith('D:/CP6/'):
        repo, commit, path, name = MAIN, MAIN_SHA, original[len('D:/CP6/'):], 'Main'
    else:
        prefix = 'D:/CP6.Platform-worktrees/wave5-integration-20261013/'
        assert original.startswith(prefix), original
        repo, commit, path, name = PLATFORM, PLATFORM_SHA, original[len(prefix):], 'Platform'
    local = (repo/path).read_bytes()
    blob = subprocess.run(['git', '-C', str(repo), 'show', commit+':'+path], capture_output=True)
    item.update(repository=name, baseline_commit=commit, repository_path=path,
                fixed_blob_raw_sha256=sha(blob.stdout) if blob.returncode == 0 else None,
                local_raw_sha256=sha(local), local_path=(repo/path).as_posix(),
                hash_semantics='SHA256 of exact bytes; git show or read_bytes, no newline/Unicode normalization')
    if blob.returncode:
        assert source['id'] in ('original', 'retention'), 'Unexpected missing tracked source: '+path
        item['provenance'] = 'Local original archive, not tracked in fixed Main commit; content pinned by accepted raw SHA256.'
    else:
        item['provenance'] = 'Fixed repository blob, with separate local working-copy raw identity.'
    if source['id'] in ('original', 'retention'):
        assert sha(local) == '642a2fbc35d17fbfaa0bcc51617e24654cbe193e0f907674a3e6f4c41c47eb84'
    inputs.append(item)

write('source-inputs.json', {
    'task': 'WP02-08', 'observed_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'fixed_inputs': {'Main': MAIN_SHA, 'Platform': PLATFORM_SHA, 'Handoff': HANDOFF_SHA},
    'source_index': {'path': 'docs/development/evidence/wave6-dispatch-20261013/admission-outbox.json',
                     'raw_sha256': sha(admission.read_bytes())},
    'scope': 'Read-only source identities; not runtime or Owner adoption. Original archive is pinned by raw hash, not falsely attributed to a Git blob.',
    'sources': inputs})

catalog_path = MAIN/'docs/development/assessments/20261011/catalogs/foundation.json'
catalog_raw = catalog_path.read_bytes()
catalog = json.loads(catalog_raw)
target = next(t for t in catalog['targets'] if t['id'] == 'PLT-DEL-01')
assert len(target['cases']) == 60 and len({c['id'] for c in target['cases']}) == 60
entries = []
for case in target['cases']:
    assert case['state'] == 'NOT_RUN'
    item = dict(case)
    item['original_business_acceptance'] = {
        'state': 'NOT_RUN', 'evidence': [],
        'responsibility': 'Main/CRM authoritative Owner and host: actual migration, all writers, trusted tenant/route authority, broker transport, complete business transaction and consumer/reference closure.'}
    item['library_subassertions'] = {
        'state': 'PENDING_IMPLEMENTATION_AND_TEST_MAPPING', 'tests': [], 'evidence': [],
        'scope': 'Parent implementation agent will map bounded library assertions and actual results; synthetic fixtures do not pass original business AC.'}
    entries.append(item)
for expected, actual in zip(target['cases'], entries):
    assert all(actual[k] == v for k, v in expected.items())
write('acceptance-map.json', {
    'task': 'WP02-08', 'target': 'PLT-DEL-01',
    'catalog': {'path': 'docs/development/assessments/20261011/catalogs/foundation.json',
                'raw_sha256': sha(catalog_raw), 'main_baseline_commit': MAIN_SHA},
    'case_count': 60, 'source_sets': target['source_sets'], 'original_business_summary': {'NOT_RUN': 60},
    'boundary': 'Original IDs, assertion text, source pointers and SHA256 values copied unchanged. Library evidence is separate. Main/CRM Owner adoption and actual broker/host acceptance remain NOT_RUN.',
    'cases': entries})
print(f'Recorded {len(inputs)} source entries; verified all 60 original AC records unchanged; original business NOT_RUN=60.')
