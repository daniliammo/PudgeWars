# Convert decoded raw RGBA dumps to PNG; rebuild Z for RG normal maps and flip green (UE DirectX -> Unity OpenGL).
import json, glob, sys, os, numpy as np
from PIL import Image
src = sys.argv[1]
for info in glob.glob(os.path.join(src, '**/*.rawinfo.json'), recursive=True):
    d = json.load(open(info)); base = info[:-len('.rawinfo.json')]
    a = np.frombuffer(open(base + '.raw', 'rb').read(), np.uint8).reshape(d['h'], d['w'], 4).copy()
    if d['fmt'] == 'PF_B8G8R8A8': a = a[..., [2, 1, 0, 3]].copy()  # uncompressed UI textures come as BGRA
    if d['normal']:
        x = a[..., 0] / 127.5 - 1; y = a[..., 1] / 127.5 - 1
        zz = np.sqrt(np.clip(1 - x * x - y * y, 0, 1))
        a[..., 1] = 255 - a[..., 1]
        a[..., 2] = np.round((zz * 0.5 + 0.5) * 255).astype(np.uint8)
        a[..., 3] = 255
        Image.fromarray(a[..., :3], 'RGB').save(base + '.png')
    else:
        mode = 'RGBA' if d['srcfmt'] != 'PF_ETC2_RGB' and a[..., 3].min() < 255 else 'RGB'
        Image.fromarray(a if mode == 'RGBA' else a[..., :3], mode).save(base + '.png')
    print(os.path.relpath(base, src), d['w'], d['h'], 'normal' if d['normal'] else ('alpha %d..%d' % (a[..., 3].min(), a[..., 3].max())))
