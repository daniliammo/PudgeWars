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
    # Dire's paths: a rocky slab path in the menu, darker sand along the pit's rim on Dire's side of the arena
    'Dire_Path_Stone': ('dire_path/stone_path007_tintable_psd_6b1a56e8.png', (112, 104, 96), 1.0),
    'Dire_Path_Sand': ('dire_path/sand_path009_color_psd_324cc500.png', (118, 104, 86), 0.8),
}
# Normal maps keep their original tangent-space values: no colour tint or contrast adjustment.
# The exported grass and sand materials share normals whose names differ from their colour textures.
NORMALS = {
    'Radiant_Grass': 'radiant_ground/grass_long_01b_normal_psd_584fd7c2.png',
    'Radiant_Flowers': 'radiant_ground/grass_long_00_normal_psd_241b7552.png',
    'Radiant_Stones': 'radiant_tiles/stone_path001_angled_normal_psd_591cdc8d.png',
    'Dire_Ground': 'dire_ground/sand_cracked001_normal_psd_c4239674.png',
    'Dire_Patches': 'dire_ground/dirt_cracked_patchy_00_normal_psd_2367b365.png',
    'Path_Sand': 'path/sand_path001_normal_psd_5df4e78b.png',
    'Dire_Path_Stone': 'dire_path/stone_path007_normal_psd_48fab4c3.png',
    'Dire_Path_Sand': 'dire_path/sand_path001_normal_psd_5df4e78b.png',
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
    for name, src in NORMALS.items():
        Image.open(os.path.join(src_dir, src)).convert('RGB').save(os.path.join(out_dir, name + '_normal.png'))


if __name__ == '__main__':
    main()
