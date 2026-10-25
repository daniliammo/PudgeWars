# What a saved TerrainData asset really holds (not what the editor shows from the GPU): trees, grass, layers, and
# whether the alphamaps vary at all (one colour = every point on one layer: the blending was lost).
# Needs UnityPy in a venv outside the project: python3 -m venv <dir> && <dir>/bin/pip install UnityPy
# Usage: <dir>/bin/python -I Tools/Check/terrain_check.py Assets/Art/Landscape/Terrain/*.asset
import sys
import UnityPy

for path in sys.argv[1:]:
    env = UnityPy.load(path)
    for obj in env.objects:
        if obj.type.name == 'TerrainData':
            tree = obj.read_typetree()
            det = tree['m_DetailDatabase']
            print(path.split('/')[-1], 'trees', len(det.get('m_TreeInstances', [])), 'tree kinds', len(det.get('m_TreePrototypes', [])),
                  'grass kinds', len(det.get('m_DetailPrototypes', [])), 'layers', len(tree['m_SplatDatabase']['m_TerrainLayers']))
        elif obj.type.name == 'Texture2D':
            texture = obj.read()
            colours = texture.image.convert('RGBA').getcolors(1 << 22)
            print('   ', texture.m_Name, 'distinct colours', len(colours) if colours else 'many')
