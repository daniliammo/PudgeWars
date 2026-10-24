# Render preview frames of a rigged mesh + animation (Workbench) to validate conversion.
# args: build_fbx.py mesh.json anim.json|- outprefix frames bind_forward "p,y,r" scale [ortho_scale]
import bpy, sys, json, importlib.util, math
A = sys.argv[sys.argv.index('--') + 1:]
spec = importlib.util.spec_from_file_location('bf', A[0]); bf = importlib.util.module_from_spec(spec); spec.loader.exec_module(bf)
mesh_json, anim_json, out, frames, bind_fwd, comp, scale = A[1:8]
oscale = float(A[8]) if len(A) > 8 else 2.6
scale = float(scale); comp = [float(x) for x in comp.split(',')]
bf.reset()
d = json.load(open(mesh_json))
bind = bf.Conv(bind_fwd, scale)
arm, full, rest, parents = bf.build_armature('Rig', d['bones'], bind)
mesh = bf.build_mesh('Mesh', d, bind)
bf.skin(mesh, arm, d['bones'], d)
if anim_json != '-':
    a = json.load(open(anim_json))
    bf.apply_anim(arm, d['bones'], full, rest, parents, a, (0, a['frames'] - 1), bf.Conv('+X', scale), bf.ue_rotator_quat(*comp).to_matrix().to_4x4(), bind)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'
sc.render.resolution_x = 360; sc.render.resolution_y = 360
cam_data = bpy.data.cameras.new('cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = oscale
cam = bpy.data.objects.new('cam', cam_data); sc.collection.objects.link(cam); sc.camera = cam
# Unity +Z (character forward) == Blender -Y; front camera sits at Blender -Y looking +Y.
for fr in [int(x) for x in frames.split(',')]:
    sc.frame_set(fr)
    for tag, loc, rot in (('front', (0, -8, oscale * 0.35), (math.radians(90), 0, 0)), ('side', (8, 0, oscale * 0.35), (math.radians(90), 0, math.radians(90)))):
        cam.location = loc; cam.rotation_euler = rot
        sc.render.filepath = f'{out}_{fr}_{tag}.png'
        bpy.ops.render.render(write_still=True)
