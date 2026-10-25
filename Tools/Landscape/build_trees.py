# Blender (5.x) script: Dota 2 trees (glTF from props_tree) -> FBX for Unity, plus their colour/normal textures.
# Usage: blender -b --factory-startup -P build_trees.py -- <props_tree dir> <output dir>
#   e.g. -- ~/dota_trees/models/props_tree Assets/Art/Landscape/Trees
#
# Only the standard map trees are taken (Radiant oaks and pines, Dire trees, the ones Dota has cut-down and
# inspector variants of) and the mango tree. Each .glb holds three meshes on top of each other: the tree
# (*_model), its stump and an inspector model; only the tree is kept, without its unused joint weights.
#
# Scale: the glTF files are in metres at Source's 1 inch per unit (an oak is 7.4 m = 291 units); the project
# uses 1 Dota unit = 1 cm, so the trees are scaled by 0.01 / 0.0254 (an oak becomes 2.9 m, Pudge is 1.7 m).
# Axes as in Tools/AssetExtraction/build_fbx.py: FBX forward -Z / up Y, Unity imports with bakeAxisConversion.
import bpy, os, shutil, sys
from mathutils import Matrix

TREES = [
    'tree_oak_01', 'tree_oak_01b', 'tree_oak_02', 'tree_pine_01', 'tree_pine_02', 'tree_pine_03b',  # Radiant
    'dire_tree004', 'dire_tree004b', 'dire_tree007', 'dire_tree008',                                # Dire
    'mango_tree',
]
SCALE = 0.01 / 0.0254


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for block in list(coll):
            coll.remove(block)


def tree_mesh(name):
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    kept = [o for o in meshes if o.name.endswith('_model') and 'stump' not in o.name] or meshes[:1]
    if len(kept) != 1:
        raise RuntimeError(f'{name}: expected one tree mesh, got {[o.name for o in meshes]}')
    tree = kept[0]
    for o in list(bpy.data.objects):
        if o != tree:
            bpy.data.objects.remove(o, do_unlink=True)
    tree.parent = None
    tree.vertex_groups.clear()
    tree.data.transform(tree.matrix_world @ Matrix.Scale(1.0, 4))
    tree.data.transform(Matrix.Scale(SCALE, 4))
    tree.matrix_world = Matrix.Identity(4)
    tree.name = tree.data.name = name
    return tree


def copy_textures(tree, src, out_dir):
    """<material>_color.png and <material>_normal.png next to the models (what the Unity builder looks for)."""
    for slot in tree.material_slots:
        mat = slot.material
        if mat is None or mat.node_tree is None:
            continue
        for node in mat.node_tree.nodes:
            if node.type != 'TEX_IMAGE' or node.image is None:
                continue
            file = os.path.basename(node.image.filepath)
            kind = 'normal' if any(l.to_node.type == 'NORMAL_MAP' for l in node.outputs['Color'].links) else \
                'color' if any(l.to_socket.name == 'Base Color' for o in node.outputs for l in o.links) else None
            if kind:
                shutil.copyfile(os.path.join(src, file), os.path.join(out_dir, 'Textures', f'{mat.name}_{kind}.png'))


def export_fbx(path, obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    # the exporter module is called directly: the operator's properties are not registered under Python 3.14
    from io_scene_fbx import export_fbx_bin
    from bpy_extras.io_utils import axis_conversion

    class _Op:
        def report(self, kind, msg): print('FBX', kind, msg)
    export_fbx_bin.save(
        _Op(), bpy.context, global_matrix=axis_conversion(to_forward='-Z', to_up='Y').to_4x4(),
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,  # Unity: ModelImporter.bakeAxisConversion
        use_mesh_modifiers=False, mesh_smooth_type='OFF', use_tspace=False, use_custom_props=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
        bake_anim=False, bake_anim_use_all_bones=False, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=False, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode='STRIP', embed_textures=False,
        use_mesh_modifiers_render=False, use_subsurf=False, use_armature_deform_only=False, use_metadata=True,
        use_mesh_edges=False, use_triangles=True, colors_type='NONE', prioritize_active_color=False, use_visible=False,
        use_active_collection=False, collection='', batch_mode='OFF', use_batch_own_dir=False)


def main():
    src, out_dir = sys.argv[sys.argv.index('--') + 1:][:2]
    os.makedirs(os.path.join(out_dir, 'Textures'), exist_ok=True)
    for name in TREES:
        clear()
        bpy.ops.import_scene.gltf(filepath=os.path.join(src, name + '.glb'))
        tree = tree_mesh(name)
        copy_textures(tree, src, out_dir)
        export_fbx(os.path.join(out_dir, name + '.fbx'), tree)
        vs = [v.co for v in tree.data.vertices]
        dims = [max(v[i] for v in vs) - min(v[i] for v in vs) for i in range(3)]
        print(f'TREE {name}: {len(tree.data.polygons)} faces, {dims[0]:.2f} x {dims[1]:.2f} x {dims[2]:.2f} m, '
              f'materials {[s.material.name for s in tree.material_slots if s.material]}')


main()
