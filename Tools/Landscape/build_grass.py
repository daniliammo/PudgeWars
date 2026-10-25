# Blender (5.x) script: Dota 2 grass clumps (glTF from props_nature) -> FBX for Unity, plus their colour textures.
# Usage: blender -b --factory-startup -P build_grass.py -- <props_nature dir> <output dir>
#   e.g. -- ~/dota_grass/models/props_nature Assets/Art/Landscape/Grass
#
# The usual grass is grass_clump_00* with its green texture. Dota has no winter grass of its own: winter maps tint
# the grey tintable grass (grass_clump_02*); here that tint is baked into its texture (frosty white with a little
# blue-green), the material renamed to grass_clump_winter. Scale and axes as for the trees (build_trees.py).
import bpy, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_trees as trees

GRASS = ['grass_clump_00a', 'grass_clump_00b', 'grass_clump_00d', 'grass_clump_00e', 'grass_clump_00f']
WINTER = {'grass_clump_02b': 'grass_clump_winter_b', 'grass_clump_02c': 'grass_clump_winter_c',
          'grass_clump_02d': 'grass_clump_winter_d', 'grass_clump_02e': 'grass_clump_winter_e',
          'grass_clump_02f': 'grass_clump_winter_f'}
WINTER_MATERIAL = 'grass_clump_winter'
# a tint multiplies the grey texture (sRGB); frost: pale blue-grey, a touch of green left in the blades
WINTER_TINT = (0.88, 0.96, 1.0)
WINTER_GAIN = 2.6


def tint_winter(out_dir):
    """The tinted copy of the grey tintable texture, in place."""
    path = os.path.join(out_dir, 'Textures', f'{WINTER_MATERIAL}_color.png')
    image = bpy.data.images.load(path)
    px = list(image.pixels)
    for i in range(0, len(px), 4):
        for c in range(3):  # a PNG's pixels come as stored, in sRGB
            px[i + c] = min(1.0, px[i + c] * WINTER_TINT[c] * WINTER_GAIN)
    image.pixels = px
    image.filepath_raw = path
    image.file_format = 'PNG'
    image.save()


def main():
    src, out_dir = sys.argv[sys.argv.index('--') + 1:][:2]
    os.makedirs(os.path.join(out_dir, 'Textures'), exist_ok=True)
    for source in GRASS + list(WINTER):
        name = WINTER.get(source, source)
        trees.clear()
        bpy.ops.import_scene.gltf(filepath=os.path.join(src, source + '.glb'))
        grass = trees.tree_mesh(name)
        if source in WINTER:
            grass.material_slots[0].material.name = WINTER_MATERIAL
        trees.copy_textures(grass, src, out_dir)
        trees.export_fbx(os.path.join(out_dir, name + '.fbx'), grass)
        vs = [v.co for v in grass.data.vertices]
        dims = [max(v[i] for v in vs) - min(v[i] for v in vs) for i in range(3)]
        print(f'GRASS {name}: {len(grass.data.polygons)} faces, {dims[0]:.2f} x {dims[1]:.2f} x {dims[2]:.2f} m, '
              f'materials {[s.material.name for s in grass.material_slots if s.material]}')
    tint_winter(out_dir)


if __name__ == '__main__':
    main()
