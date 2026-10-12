"""Import pinned S01 bytes; no archive code is executed. Python 3.10+."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

SOURCE = 'dfc043b1473dcea51d34ff87a7808692349c278a'
PINS = {
    'profile-inputs.json': '2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e',
    'vectors.json': 'b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01',
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--main-repo', type=Path, required=True)
    args = parser.parse_args()
    destination = Path(__file__).resolve().parents[4] / 'tests/CP6.Platform.UnitTests/TestData/S01'
    destination.mkdir(parents=True, exist_ok=True)

    def blob(path):
        return subprocess.check_output(['git', '-c', 'core.longpaths=true', '-C', str(args.main_repo),
                                        'cat-file', 'blob', SOURCE + ':' + path])

    imported = []

    def write(name, raw, expected, source):
        actual = hashlib.sha256(raw).hexdigest()
        if actual != expected:
            raise ValueError(f'{name}: source hash mismatch')
        (destination / name).write_bytes(raw)
        imported.append(dict(file=name, sha256=actual, bytes=len(raw), source=source))

    for name, expected in PINS.items():
        path = 'eng/contracts/s01/' + name
        write(name, blob(path), expected, dict(commit=SOURCE, path=path, basis='git_blob_bytes'))

    package = json.loads((destination / 'profile-inputs.json').read_bytes())
    for source_id, variant, name in [
        ('composed-20', 'composed', 'crm-inq.openapi.json'),
        ('erp-registry-current', 'composed', 'erp-current.registry.json'),
        ('erp-registry-frozen', 'original', 'erp-frozen.registry.json'),
    ]:
        source = next(x for x in package['sources'] if x['id'] == source_id)
        selector = source['selector']
        raw_index = blob(selector['path'])
        assert hashlib.sha256(raw_index).hexdigest() == selector['sha256']
        node = json.loads(raw_index)
        for part in selector['json_pointer'].split('/')[1:]:
            node = node[int(part)] if isinstance(node, list) else node[part.replace('~1', '/').replace('~0', '~')]
        path = node['local_derived_path'] if variant == 'composed' else node['absolute_path']
        write(name, Path(path).read_bytes(), source[variant + '_sha256'],
              dict(commit=SOURCE, source_id=source_id, variant=variant, selector=selector,
                   patch_bundles=source['patch_bundles'], basis='original_asset_bytes'))

    evidence = dict(source_repository='GTX537/CP6', source_commit=SOURCE,
                    package_version=package['package_version'], imported=imported,
                    scope='Exact input and native identity asset import; no business execution')
    (Path(__file__).parent / 'input-provenance.json').write_bytes(
        (json.dumps(evidence, indent=2, ensure_ascii=False) + '\n').encode('utf-8'))
    print(f'Imported and verified {len(imported)} files')


if __name__ == '__main__':
    main()
