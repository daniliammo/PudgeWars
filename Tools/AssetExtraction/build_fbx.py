# Blender (5.x) script: build FBX files for Unity from extractor JSON.
# Usage: blender -b --factory-startup -P build_fbx.py -- <job.json>
#
# Spaces: UE is left-handed, Z up, centimetres. Unity is left-handed, Y up, metres. Blender is right-handed, Z up;
# exported FBX (forward -Z, up Y) + Unity bakeAxisConversion maps Blender (x, y, z) -> Unity (x, z, y).
# Each conversion is a proper rotation UE -> Unity that turns a chosen UE "forward" axis into Unity +Z,
# followed by the fixed Unity -> Blender change of basis.
#
# The bind pose and the animations use different conversions on purpose: in the original Blueprints the
# SkeletalMeshComponent is rotated (Pudge: Yaw -90 Roll 90), and the animation root tracks only look right with
# that rotation applied, whereas the bind pose of the asset faces UE +Y. Skinning only needs
#   pose_b * rest_b^-1 = KSa * C * W_anim_b * W_bind_b^-1 * KSb^-1
# (KSa/KSb: actor/bind conversions, C: mesh component rotation), so both end up facing Unity +Z.
import bpy, json, sys, math, os
from mathutils import Matrix, Quaternion, Vector

UE_TO_UNITY = {
    '+X': ((0, 1, 0), (0, 0, 1), (1, 0, 0)),
    '-X': ((0, -1, 0), (0, 0, 1), (-1, 0, 0)),
    '+Y': ((-1, 0, 0), (0, 0, 1), (0, 1, 0)),
    '-Y': ((1, 0, 0), (0, 0, 1), (0, -1, 0)),
}
# Inverse of what Unity does with these files (FBX -Z forward / Y up, ModelImporter.bakeAxisConversion):
# Blender (x, y, z) -> Unity (x, z, y); verified by rendering the imported prefabs in Unity.
UNITY_TO_BLENDER = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))


class Conv:
    def __init__(self, forward, scale):
        k3 = UNITY_TO_BLENDER @ Matrix(UE_TO_UNITY[forward])
        self.k3 = k3
        self.ks = (k3 * scale).to_4x4()
        self.ks_inv = self.ks.inverted()

    def mat(self, m):  # UE component/actor-space matrix -> Blender
        return self.ks @ m @ self.ks_inv

    def point(self, p):
        return (self.ks @ Vector((p[0], p[1], p[2], 1.0))).xyz

    def dir(self, n):
        return (self.k3 @ Vector(n)).normalized()


def ue_rotator_quat(pitch, yaw, roll):  # FRotator::Quaternion()
    dr = math.pi / 360.0
    sp, cp = math.sin(pitch * dr), math.cos(pitch * dr)
    sy, cy = math.sin(yaw * dr), math.cos(yaw * dr)
    sr, cr = math.sin(roll * dr), math.cos(roll * dr)
    return Quaternion((cr * cp * cy + sr * sp * sy,
                       cr * sp * sy - sr * cp * cy,
                       -cr * sp * cy - sr * cp * sy,
                       cr * cp * sy - sr * sp * cy))


def ue_xf(xf):
    t, r, s = xf['t'], xf['r'], xf['s']
    return Matrix.LocRotScale(Vector(t), Quaternion((r[3], r[0], r[1], r[2])), Vector(s))


def ortho(m):
    r = m.to_3x3()
    r = Matrix((r.col[0].normalized(), r.col[1].normalized(), r.col[2].normalized())).transposed()
    out = r.to_4x4()
    out.translation = m.translation
    return out


def world_mats(locals_, parents):
    w = [None] * len(locals_)
    for i, l in enumerate(locals_):
        w[i] = l if parents[i] < 0 else w[parents[i]] @ l
    return w


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import addon_utils
    addon_utils.enable('io_scene_fbx', default_set=True)


def make_materials(obj, mats):
    for m in mats:
        mat = bpy.data.materials.get(m['slot']) or bpy.data.materials.new(m['slot'])
        obj.data.materials.append(mat)


def build_mesh(name, d, conv):
    pos = d['pos']; nrm = d['nrm']; uv = d['uv']; idx = d['idx']
    nv = len(pos) // 3
    verts = [conv.point(pos[3 * i:3 * i + 3]) for i in range(nv)]
    normals = [conv.dir(nrm[3 * i:3 * i + 3]) for i in range(nv)]
    tris = [tuple(idx[i:i + 3]) for i in range(0, len(idx), 3)]
    # pick the winding that agrees with the source normals (UE -> Blender flips handedness)
    score = 0.0
    for a, b, c in tris[:: max(1, len(tris) // 500)]:
        fn = (verts[b] - verts[a]).cross(verts[c] - verts[a])
        score += fn.dot(normals[a] + normals[b] + normals[c])
    if score < 0:
        tris = [(a, c, b) for a, b, c in tris]
    tri_mat = [0] * len(tris)
    for s in d['sections']:
        f0 = s['first'] // 3
        for k in range(f0, f0 + s['faces']):
            tri_mat[k] = s['mat']
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], tris)
    me.validate(clean_customdata=False)
    for k, layer in enumerate([uv] + list(d.get('uv2') or [])):
        uvl = me.uv_layers.new(name='UVMap' if k == 0 else 'UVMap%d' % (k + 1))
        buf = []
        for poly in me.polygons:
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                buf.extend((layer[2 * vi], 1.0 - layer[2 * vi + 1]))
        uvl.data.foreach_set('uv', buf)
    me.polygons.foreach_set('material_index', tri_mat)
    me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
    me.normals_split_custom_set_from_vertices([tuple(n) for n in normals])
    me.update()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    make_materials(obj, d['materials'])
    return obj


def build_armature(name, bones, conv):
    locals_ = [ue_xf(b['xf']) for b in bones]
    parents = [b['parent'] for b in bones]
    full = [conv.mat(m) for m in world_mats(locals_, parents)]
    rest = [ortho(m) for m in full]
    arm = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    ebs = []
    for i, b in enumerate(bones):
        eb = arm.edit_bones.new(b['name'])
        kids = [j for j, p in enumerate(parents) if p == i]
        L = 0.05
        if kids:
            L = max(0.01, min((rest[j].translation - rest[i].translation).length for j in kids))
        eb.head = (0, 0, 0); eb.tail = (0, L, 0)
        eb.matrix = rest[i]
        ebs.append(eb)
    for i, p in enumerate(parents):
        if p >= 0:
            ebs[i].parent = ebs[p]; ebs[i].use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj, full, rest, parents


def skin(mesh_obj, arm_obj, bones, d):
    groups = [mesh_obj.vertex_groups.new(name=b['name']) for b in bones]
    for vi, inf in enumerate(d['inf']):
        for k in range(0, len(inf), 2):
            groups[int(inf[k])].add([vi], inf[k + 1], 'REPLACE')
    mod = mesh_obj.modifiers.new('Armature', 'ARMATURE')
    mod.object = arm_obj
    mesh_obj.parent = arm_obj


def apply_anim(arm_obj, mesh_bones, full_rest, rest, parents, a, frame_range, conv, comp, bind):
    names = [b['name'] for b in mesh_bones]
    ab = a['bones']; ap = a['parents']
    aidx = {nm: i for i, nm in enumerate(ab)}
    ref = [ue_xf(x) for x in a['refpose']]
    tr = a['tracks']
    f0, f1 = frame_range
    rest_inv = [m.inverted() for m in rest]
    corr = [full_rest[i].inverted() @ rest[i] for i in range(len(names))]
    act = bpy.data.actions.new(a['name'])
    arm_obj.animation_data_create()
    arm_obj.animation_data.action = act
    pbs = arm_obj.pose.bones
    for pb in pbs:
        pb.rotation_mode = 'QUATERNION'
    keys = {nm: ([], [], []) for nm in names}
    prevq = {}
    for f in range(f0, f1 + 1):
        loc_ue = []
        for i, nm in enumerate(ab):
            t = tr[i]
            if t is None:
                loc_ue.append(ref[i])
            else:
                p = t['pos'][3 * f:3 * f + 3]; r = t['rot'][4 * f:4 * f + 4]; s = t['scl'][3 * f:3 * f + 3]
                loc_ue.append(Matrix.LocRotScale(Vector(p), Quaternion((r[3], r[0], r[1], r[2])), Vector(s)))
        wue = world_mats(loc_ue, ap)
        # pose_b * rest_b^-1 must equal KSa * C * W_anim * W_bind^-1 * KSb^-1 (vertices live in bind space)
        P = [conv.ks @ comp @ wue[aidx[nm]] @ bind.ks_inv @ corr[i] for i, nm in enumerate(names)]
        for i, nm in enumerate(names):
            p = parents[i]
            if p < 0:
                basis = rest_inv[i] @ P[i]
            else:
                basis = (rest_inv[p] @ rest[i]).inverted() @ (P[p].inverted() @ P[i])
            loc, rot, scl = basis.decompose()
            if nm in prevq and prevq[nm].dot(rot) < 0:
                rot = -rot
            prevq[nm] = rot
            fr = f - f0
            keys[nm][0].append((fr, loc)); keys[nm][1].append((fr, rot)); keys[nm][2].append((fr, scl))
    if hasattr(act, 'fcurves'):
        fcurves = act.fcurves
        new_fc = lambda p, c, g: fcurves.new(p, index=c, action_group=g)
    else:
        from bpy_extras.anim_utils import action_ensure_channelbag_for_slot
        slot = act.slots.new('OBJECT', arm_obj.name)
        arm_obj.animation_data.action_slot = slot
        cb = action_ensure_channelbag_for_slot(act, slot)
        new_fc = lambda p, c, g: cb.fcurves.new(p, index=c, group_name=g)
    for nm in names:
        for path, arr, comps in (('location', keys[nm][0], 3), ('rotation_quaternion', keys[nm][1], 4), ('scale', keys[nm][2], 3)):
            for c in range(comps):
                fc = new_fc('pose.bones["%s"].%s' % (nm, path), c, nm)
                fc.keyframe_points.add(len(arr))
                co = []
                for fr, v in arr:
                    co.extend((fr, v[c]))
                fc.keyframe_points.foreach_set('co', co)
                fc.keyframe_points.foreach_set('interpolation', [1] * len(arr))  # LINEAR
                fc.update()
    return act


def export_fbx(path, objs, anim):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Call the exporter module directly: the operator's annotated properties are not registered under Python 3.14.
    from io_scene_fbx import export_fbx_bin
    from bpy_extras.io_utils import axis_conversion

    class _Op:
        def report(self, kind, msg): print('FBX', kind, msg)
    export_fbx_bin.save(
        _Op(), bpy.context, global_matrix=axis_conversion(to_forward='-Z', to_up='Y').to_4x4(),
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,  # Unity: ModelImporter.bakeAxisConversion
        use_mesh_modifiers=False, mesh_smooth_type='OFF', use_tspace=False, use_custom_props=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
        bake_anim=anim, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode='STRIP', embed_textures=False,
        use_mesh_modifiers_render=False, use_subsurf=False, use_armature_deform_only=False, use_metadata=True,
        use_mesh_edges=False, use_triangles=True, colors_type='NONE', prioritize_active_color=False, use_visible=False,
        use_active_collection=False, collection='', batch_mode='OFF', use_batch_own_dir=False)


def set_fps(rate):
    sc = bpy.context.scene
    base = max(1, round(rate))
    sc.render.fps = base
    sc.render.fps_base = base / rate


def comp_matrix(it):
    r = it.get('comp_rot', (0, 0, 0))
    return ue_rotator_quat(*r).to_matrix().to_4x4()


def run_job(job):
    for it in job:
        reset()
        scale = it.get('scale', 0.01)
        bind = Conv(it.get('bind_forward', '+X'), scale)
        if it['kind'] == 'skmesh':
            d = json.load(open(it['src']))
            arm, full, rest, parents = build_armature(it['name'], d['bones'], bind)
            mesh = build_mesh(it['name'] + '_Mesh', d, bind)
            skin(mesh, arm, d['bones'], d)
            export_fbx(it['dst'], [arm, mesh], False)
            print('EXPORTED', it['dst'])
        elif it['kind'] == 'stmesh':
            d = json.load(open(it['src']))
            mesh = build_mesh(it['name'], d, bind)
            export_fbx(it['dst'], [mesh], False)
            print('EXPORTED', it['dst'])
        elif it['kind'] == 'anim':
            d = json.load(open(it['mesh']))
            a = json.load(open(it['src']))
            arm, full, rest, parents = build_armature(it['rig'], d['bones'], bind)
            n = a['frames']
            rate = (n - 1) / a['length'] if a['length'] > 0 and n > 1 else a['fps']
            f1 = it.get('last', n - 1)
            set_fps(rate)
            sc = bpy.context.scene
            sc.frame_start = 0; sc.frame_end = f1
            act = apply_anim(arm, d['bones'], full, rest, parents, a, (0, f1), Conv('+X', scale), comp_matrix(it), bind)
            act.name = it['clip']
            export_fbx(it['dst'], [arm], True)
            print('EXPORTED', it['dst'], 'rate %.4f frames %d' % (rate, f1 + 1))


if __name__ == '__main__':
    run_job(json.load(open(sys.argv[sys.argv.index('--') + 1])))
