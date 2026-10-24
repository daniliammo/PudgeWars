# Convert raw UE5 BINKA sound data (SoundsRaw/, dumped by SoundDumper/) to WAV for Unity.
#
# UE5 BINKA is Bink Audio 2 (RAD). FFmpeg's binka decoder only handles the older Bink Audio and produces noise,
# so decoding needs RAD's decoder build `binkadec` (shipped with FModel as Output/.data/binkadec.exe; not in this repo).
#
# Usage:
#   python3 sounds.py --binkadec /path/to/binkadec.exe [--wine [WINE]] [--all]
# --wine without a path uses the wine of PortProton (~/PortProton/data/dist/GE-PROTON*) with its own prefix
# (~/.local/share/pudgewars-wine), so PortProton's game prefixes stay untouched.
# Writes Assets/_Project/Audio/Pudge/<Folder>_<Name>.wav (hook set by default, --all for every sound), then run
# "Pudge Wars/Assign Hook Sounds" (or rebuild the arena) in Unity.
import argparse, glob, os, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, 'SoundsRaw')
OUT = os.path.join(HERE, '..', '..', 'Assets', '_Project', 'Audio', 'Pudge')

# Hook sounds wired in Player_PW (see HookSounds in HookAbility.cs)
HOOK_SET = ['Chain/Hook', 'Chain/ReturnHook', 'Popal/PopalSound', 'Popal/Pud_1', 'Popal/Pud_2', 'Popal/Pud_3',
            'Popal/TargetHook', 'Return/Pud_anger', 'Return/Pud_1']


def wine_command(path):
    """Returns (argv prefix, env) for running a Windows executable."""
    env = dict(os.environ)
    if path is None:
        builds = sorted(glob.glob(os.path.expanduser('~/PortProton/data/dist/GE-PROTON*/files')))
        if builds:
            root = builds[-1]
            env['LD_LIBRARY_PATH'] = ':'.join([os.path.join(root, 'lib64'), os.path.join(root, 'lib'), env.get('LD_LIBRARY_PATH', '')])
            path = os.path.join(root, 'bin', 'wine')
        else:
            path = shutil.which('wine') or 'wine'
    env.setdefault('WINEPREFIX', os.path.expanduser('~/.local/share/pudgewars-wine'))
    env.setdefault('WINEDEBUG', '-all')
    return [path], env


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--binkadec', required=True)
    ap.add_argument('--wine', nargs='?', const='', default=None, help='run a Windows binkadec.exe through wine (default: PortProton)')
    ap.add_argument('--all', action='store_true')
    a = ap.parse_args()
    names = [os.path.relpath(os.path.join(r, f), RAW)[:-len('.binka')] for r, _, fs in os.walk(RAW) for f in fs if f.endswith('.binka')] \
        if a.all else HOOK_SET
    os.makedirs(OUT, exist_ok=True)
    prefix, env = wine_command(a.wine or None) if a.wine is not None else ([], None)
    for n in sorted(names):
        src = os.path.join(RAW, n + '.binka')
        dst = os.path.join(OUT, n.replace('/', '_') + '.wav')
        with tempfile.TemporaryDirectory() as tmp:
            # FModel's binkadec takes the UEBA stream as a .binka file: -i <in> -o <out.wav>
            tin, tout = os.path.join(tmp, 'in.binka'), os.path.join(tmp, 'out.wav')
            shutil.copyfile(src, tin)
            r = subprocess.run(prefix + [a.binkadec, '-i', tin, '-o', tout], capture_output=True, text=True, env=env)
            if r.returncode != 0 or not os.path.exists(tout):
                print('FAIL', n, r.stderr.strip()[:200], file=sys.stderr)
                continue
            shutil.copyfile(tout, dst)
            print('ok', os.path.relpath(dst, os.path.join(HERE, '..', '..')))


if __name__ == '__main__':
    main()
