# Blender 5: ordinary Dota Dire ground props, glTF -> FBX in project units.
# Usage: blender -b --factory-startup -P Tools/Landscape/build_dire_details.py -- <export root> <output dir>
# Source2Viewer filters: models/props_nature/badside_roots_path,models/props_nature/branches,
# models/props_rock/badside_debris_rocks,models/props_bones/badside_bones
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_trees as trees

MODELS = {
    'props_nature': ['badside_roots_path001', 'badside_roots_path002', 'badside_roots_path003',
                     'branches001', 'branches002'],
    'props_rock': ['badside_debris_rocks001', 'badside_debris_rocks002'],
    'props_bones': ['badside_bones001', 'badside_bones002', 'badside_bones003',
                    'badside_bones004', 'badside_bones005'],
}


def main():
    source, output = sys.argv[sys.argv.index('--') + 1:][:2]
    os.makedirs(os.path.join(output, 'Textures'), exist_ok=True)
    for folder, names in MODELS.items():
        directory = os.path.join(source, 'models', folder)
        for name in names:
            trees.convert(name, os.path.join(directory, name + '.glb'), output, directory)


if __name__ == '__main__':
    main()
