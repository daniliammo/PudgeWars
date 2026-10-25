# Dota 2's ground textures (~/dota_terrain) tinted to the colours they have on Dota's map, for the terrain layers.
# Usage: python3 tint_terrain.py <dota_terrain dir> <output dir>
#   e.g. python3 tint_terrain.py ~/dota_terrain Assets/Art/Landscape/Terrain
#
# Most of these textures are grey: Dota tints them per map in the terrain shader (the *_tinted copies in the folder
# are tinted far too green and bright, the Dire ground included). Here each texture's brightness keeps its detail
# (cracks, blades, stones) around the average and takes the colour Dota shows: Radiant's muted olive grass, Dire's
# dark grey-brown cracked earth, pale warm sand, grey stones; a contrast below 1 flattens loud textures.
import os, sys
from PIL import Image, ImageStat

LAYERS = {
    # output: (source, colour of the average, contrast)
    'Radiant_Grass': ('radiant_ground/grass_long_01_color_psd_3fbdd738.png', (92, 108, 52), 0.8),
    'Radiant_Flowers': ('radiant_ground/grass_long_00_color_psd_e36d65e.png', (100, 110, 54), 0.8),
    'Radiant_Stones': ('radiant_tiles/stone_path001_angled_color_psd_150d1d7b.png', (128, 124, 110), 0.9),
    'Dire_Ground': ('dire_ground/sand_cracked001_color_psd_1017aede.png', (84, 74, 64), 1.0),
    'Dire_Patches': ('dire_ground/dirt_cracked_patchy_00_psd_3956639e.png', (96, 82, 70), 0.9),
    'Path_Sand': ('path/sand_path009_color_psd_a290f30d.png', (150, 134, 100), 0.8),
}


def tint(src, colour, contrast):
    image = Image.open(src).convert('RGB')
    lum = image.convert('L')
    mean = ImageStat.Stat(lum).mean[0]
    # brightness relative to the average, its spread scaled by contrast, times the colour
    scale = [max(0.0, 1 + (v / mean - 1) * contrast) for v in range(256)]
    bands = [lum.point([min(255, round(c * s)) for s in scale]) for c in colour]
    return Image.merge('RGB', bands)


def main():
    src_dir, out_dir = sys.argv[1:3]
    for name, (src, colour, contrast) in LAYERS.items():
        out = tint(os.path.join(src_dir, src), colour, contrast)
        out.save(os.path.join(out_dir, name + '.png'))
        print(name, [round(m) for m in ImageStat.Stat(out).mean])


if __name__ == '__main__':
    main()
