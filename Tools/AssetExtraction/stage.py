# Lay out extracted Pudge Wars assets for Unity: Blender job (FBX), PNG textures, material manifest.
# Usage: python3 stage.py <extract_out_dir> <stage_dir>
import json, os, re, shutil, sys

SRC = os.path.join(sys.argv[1], 'PudgeWars/Assets')
DST = sys.argv[2]
ROOT = 'Assets/_Project/Art'

def tex_name(stem):
    s = re.sub(r'_(tga|psd)_[0-9a-f]{6,8}$', '', stem)
    return re.sub(r'^rsz_\d?', '', s)

def mat_textures(mat_json):
    d = json.load(open(mat_json))
    return [t.split("'")[1].split('.')[0].replace('/Game/PudgeWars/Assets/', '') for t in d.get('textures', []) if t]

job, textures, materials, models = [], {}, [], []

def add_tex(src_rel, unity_dir):
    stem = os.path.basename(src_rel)
    info = json.load(open(os.path.join(SRC, src_rel + '.rawinfo.json')))
    dst = f'{unity_dir}/Textures/{tex_name(stem)}.png'
    textures[dst] = dict(src=os.path.join(SRC, src_rel + '.png'), normal=info['normal'], alpha=info['srcfmt'].endswith('RGBA'))
    return dst

def add_material(mat_rel, unity_dir, name=None, sub=''):
    src = os.path.join(SRC, mat_rel + '.material.json')
    d = json.load(open(src))
    texs = mat_textures(src)
    base = next((t for t in texs if 'normal' not in t.lower()), None)
    nrm = next((t for t in texs if 'normal' in t.lower()), None)
    # the Lotar (invisibility) materials point at Skin/Set_Base copies, identical to Texture/Textere
    fix = lambda t: t and t.replace('Pudge/Texture/Skin/Set_Base/Textere/', 'Pudge/Texture/Textere/').replace('Pudge/Hook/', 'Hook/Hook_Standart/')
    base, nrm = fix(base), fix(nrm)
    m = dict(path=f'{unity_dir}/Materials/{sub}{name or d["name"]}.mat', source='/Game/PudgeWars/Assets/' + mat_rel,
             blend=d['blend'], shading=d['shading'], twoSided=d['twoSided'],
             baseMap=add_tex(base, unity_dir) if base else None, normalMap=add_tex(nrm, unity_dir) if nrm else None,
             scalars=d['params']['scalars'])
    materials.append(m)
    return m['path']

def anim_items(char, rig, mesh_json, folder, names, comp, bind_fwd, scale, trims, loops):
    for n in names:
        it = dict(kind='anim', rig=rig, mesh=mesh_json, src=os.path.join(SRC, f'{folder}/Animations/{n}.anim.json'),
                  clip=n, dst=os.path.join(DST, f'{ROOT}/Characters/{char}/Animations/{char}@{n}.fbx'),
                  comp_rot=comp, bind_forward=bind_fwd, scale=scale)
        if n in trims:
            it['last'] = trims[n]
        job.append(it)
        models.append(dict(path=f'{ROOT}/Characters/{char}/Animations/{char}@{n}.fbx', type='anim', avatar=f'{ROOT}/Characters/{char}/{char}.fbx', clip=n, loop=n in loops))

# ---- Pudge ----
pudge_dir = f'{ROOT}/Characters/Pudge'
pmesh = os.path.join(SRC, 'Pudge/pudge.skmesh.json')
job.append(dict(kind='skmesh', name='Pudge', src=pmesh, dst=os.path.join(DST, pudge_dir, 'Pudge.fbx'), bind_forward='+Y', scale=0.01))
remap = {}
for slot in ['blinn1', 'blinn2', 'blinn3', 'blinn4', 'blinn5', 'blinn6', 'blinn8']:
    remap[slot] = add_material(f'Pudge/Texture/{slot}', pudge_dir)
for k in range(1, 9):
    add_material(f'Pudge/Texture/Lotar/blinn{k}_Lotar', pudge_dir, sub='Lotar/')
models.append(dict(path=f'{pudge_dir}/Pudge.fbx', type='model', remaps=[dict(slot=k, material=v) for k, v in remap.items()]))
pudge_anims = ['Idle', 'Walk', 'Pudge_Run', 'Pudge_Run1', 'Pudge_Run2', 'StartHookPudge', 'EndHookPudge',
               'HookStartPudgeDendy', 'EndHookPudgeDendy', 'PopadaniePudge', 'Pudge_Stun', 'Death_HookPudge_pudge1_End_HookPudge',
               'Spawn_1', 'PorojeniePudge', 'Nasmechka']
# 64 s clips that repeat a 1 s (Run1: 3.5 s) cycle with slowly growing drift: keep the first, cleanly closing cycle
anim_items('Pudge', 'Pudge', pmesh, 'Pudge', pudge_anims, (0, -90, 90), '+Y', 0.01,
           trims={'Pudge_Run': 24, 'Pudge_Run2': 24, 'Walk': 24, 'Pudge_Run1': 84},
           loops={'Idle', 'Walk', 'Pudge_Run', 'Pudge_Run1', 'Pudge_Run2', 'Pudge_Stun'})

# ---- Toy Pudge (skin): mesh component has Yaw -90 and scale 0.5 in Player_PW_Child ----
toy_dir = f'{ROOT}/Characters/PudgeToy'
tmesh = os.path.join(SRC, 'Toy/Toy.skmesh.json')
job.append(dict(kind='skmesh', name='PudgeToy', src=tmesh, dst=os.path.join(DST, toy_dir, 'PudgeToy.fbx'), bind_forward='+Y', scale=0.005))
remap = {}
for slot in ['pudge_cute_arm_left', 'pudge_cute_arm_right', 'pudge_cute_head', 'pudge_cute', 'pudge_cute_hair', 'pudge_cute_back', 'pudge_cute_offhand']:
    remap[slot] = add_material(f'Toy/Texture/{slot}', toy_dir)
models.append(dict(path=f'{toy_dir}/PudgeToy.fbx', type='model', remaps=[dict(slot=k, material=v) for k, v in remap.items()]))
anim_items('PudgeToy', 'PudgeToy', tmesh, 'Toy', ['Walk', 'Run', 'Hook_Start', 'Hook_End', 'Polet', 'Nasmechka', 'Nasmechka2', 'Nasmechka3'],
           (0, -90, 0), '+Y', 0.005, trims={}, loops={'Walk', 'Run', 'Polet'})

# ---- Hooks (point along Unity +Z, the flight direction) ----
hooks = [('Standart', 'Hook/Hook_Standart/HookPudge_Standart', ['Hook/Hook_Standart/blinn7'], 0.01),
         ('Dragonclaw', 'Hook/DragonclawHook/DragonclawHook', ['Hook/DragonclawHook/pudge_skeleton_hook'], 0.01),
         ('SorrowfulPrey', 'Hook/Hook_OTSP/Hook_OTSP', ['Hook/Hook_OTSP/hook_of_the_sorrowful_prey'], 0.01),
         ('ScorchingTalon', 'Hook/Hook_ScorchingTalon/ScorchingTalon', ['Hook/Hook_ScorchingTalon/scorching_talon'], 0.01),
         ('Toy', 'Toy/Hook/HookToy', ['Toy/Hook/pudge_cute_arm_right', 'Toy/Hook/pudge_cute_hook'], 0.005)]
for name, mesh, mats, scale in hooks:
    hd = f'{ROOT}/Hooks/{name}'
    job.append(dict(kind='stmesh', name=f'Hook_{name}', src=os.path.join(SRC, mesh + '.stmesh.json'),
                    dst=os.path.join(DST, hd, f'Hook_{name}.fbx'), bind_forward='+Y', scale=scale))
    remap = {os.path.basename(m): add_material(m, hd) for m in mats}
    models.append(dict(path=f'{hd}/Hook_{name}.fbx', type='model', remaps=[dict(slot=k, material=v) for k, v in remap.items()]))

os.makedirs(DST, exist_ok=True)
for dst, t in textures.items():
    os.makedirs(os.path.dirname(os.path.join(DST, dst)), exist_ok=True)
    shutil.copyfile(t['src'], os.path.join(DST, dst))
json.dump(job, open(os.path.join(DST, 'blender_job.json'), 'w'), indent=1)
manifest = dict(textures=[dict(path=k, normal=v['normal']) for k, v in textures.items()], materials=materials, models=models)
json.dump(manifest, open(os.path.join(DST, 'unity_manifest.json'), 'w'), indent=1)
print('jobs', len(job), 'textures', len(textures), 'materials', len(materials), 'models', len(models))
