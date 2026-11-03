"""Check Unity binary .asset lengths before import; forced text normalization corrupts TerrainData."""
from pathlib import Path
import struct
import sys


def check(path):
    with path.open('rb') as stream:
        header = stream.read(48)
    if header.startswith(b'%YAML'):
        return None
    if len(header) < 20:
        return f'{path}: truncated Unity binary asset header'
    version = struct.unpack_from('>I', header, 8)[0]
    if version >= 22:
        if len(header) < 48:
            return f'{path}: truncated extended Unity binary asset header'
        expected = struct.unpack_from('>Q', header, 24)[0]
    else:
        expected = struct.unpack_from('>I', header, 4)[0]
    actual = path.stat().st_size
    if actual != expected:
        return (f'{path}: binary header declares {expected} bytes, file has {actual}; '
                'check .gitattributes and restore the original binary asset')
    return None


def main():
    failures = [error for path in sorted(Path('Assets').rglob('*.asset')) if (error := check(path))]
    for error in failures:
        print(error, file=sys.stderr)
    print(f'Unity binary asset integrity: {len(failures)} errors')
    return int(bool(failures))


if __name__ == '__main__':
    raise SystemExit(main())
