# Ground textures for the arena terrain, made from the original map's grass (MapPW_DefaultMaterial_BaseColor:
# two grass strips with dirt borders, not tileable).
# Usage: python3 -I make_ground_textures.py <MapPW_DefaultMaterial_BaseColor.png> <output dir>
#   e.g. Assets/Art/Extracted/Assets/Landscape/Map/MapPW_DefaultMaterial_BaseColor.png Assets/Art/Landscape/Terrain
#
# Grass_Radiant.png: a square from the left grass strip, made seamless (blended with a copy shifted by half a
# tile, which has its seams in the middle, where the original is kept).
# Ground_Dire.png: the same grass graded to Dire's scorched, ashy ground (desaturated, browner, darker).
# Cliff.png: rock for the steep hills and the pit walls: periodic fractal noise (tileable by construction) with ridged
# cracks, grey-brown; the terrain projects it from above, so on a cliff it stretches into vertical streaks.
# Abyss.png: the same rock, nearly black, for the depth of the pit.
import sys
import numpy as np
from PIL import Image

SIZE = 1024
CROP = (80, 520, 1000, 1440)  # inside the left grass strip, clear of its dirt borders


def seamless(img):
    a = np.asarray(img, dtype=np.float32)
    shifted = np.roll(a, (SIZE // 2, SIZE // 2), axis=(0, 1))
    t = np.abs(np.linspace(-1.0, 1.0, SIZE))
    edge = np.maximum.outer(t, t)  # 0 in the middle, 1 at the tile's edges
    w = np.clip((edge - 0.55) / 0.4, 0.0, 1.0)
    w = (w * w * (3 - 2 * w))[..., None]
    return a * (1 - w) + shifted * w


def dire(a):
    lum = a @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    grey = np.repeat(lum[..., None], 3, axis=2)
    a = grey * 0.8 + a * 0.2  # most of the green is gone
    a = a * np.array([1.05, 0.86, 0.72], dtype=np.float32) * 0.62  # ashy brown, darker
    return a


def periodic_noise(rng, size, falloff):
    """Noise with a 1/f^falloff spectrum: periodic, so the texture tiles without seams. Normalised to 0..1."""
    f = np.fft.fftfreq(size)
    r = np.sqrt(f[:, None] ** 2 + f[None, :] ** 2)
    r[0, 0] = 1.0
    spectrum = (rng.standard_normal((size, size)) + 1j * rng.standard_normal((size, size))) / r ** falloff
    spectrum[0, 0] = 0
    n = np.real(np.fft.ifft2(spectrum))
    return (n - n.min()) / (n.max() - n.min())


def cliff(rng):
    base = periodic_noise(rng, SIZE, 1.1)
    detail = periodic_noise(rng, SIZE, 0.6)
    cracks = 1.0 - np.abs(periodic_noise(rng, SIZE, 1.4) * 2.0 - 1.0)  # ridges along the noise's mid-level
    cracks = np.clip((cracks - 0.9) / 0.1, 0.0, 1.0) ** 2
    shade = 0.55 + 0.35 * base + 0.15 * detail - 0.35 * cracks
    colour = np.array([112, 100, 88], dtype=np.float32)
    return shade[..., None] * colour


def main():
    src, out = sys.argv[1:3]
    grass = Image.open(src).convert('RGB').crop(CROP).resize((SIZE, SIZE), Image.LANCZOS)
    radiant = seamless(grass)
    Image.fromarray(np.clip(radiant, 0, 255).astype(np.uint8)).save(f'{out}/Grass_Radiant.png')
    Image.fromarray(np.clip(dire(radiant), 0, 255).astype(np.uint8)).save(f'{out}/Ground_Dire.png')
    rock = cliff(np.random.default_rng(1300))
    Image.fromarray(np.clip(rock, 0, 255).astype(np.uint8)).save(f'{out}/Cliff.png')
    Image.fromarray(np.clip(rock * 0.22, 0, 255).astype(np.uint8)).save(f'{out}/Abyss.png')


main()
