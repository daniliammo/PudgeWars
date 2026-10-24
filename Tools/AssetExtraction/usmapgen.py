# Generate a .usmap (unversioned property mappings) from a stripped UE5.3 arm64 libUnreal.so
# by parsing UECodeGen_Private::F*Params reflection tables.
import pickle, struct, sys, re
import numpy as np

img, secs = pickle.load(open(sys.argv[1], 'rb'))
OUT = sys.argv[2]
USMAP_VERSION = int(sys.argv[3]) if len(sys.argv) > 3 else 0

def rng(n):
    a, o, s, t = secs[n]
    return a, a + s
DRR = rng('.data.rel.ro'); RO = rng('.rodata'); TX = rng('.text')
DATA = (secs['.data'][0], secs['.bss'][0] + secs['.bss'][2])

def qw(a): return struct.unpack_from('<Q', img, a)[0]
def u32(a): return struct.unpack_from('<I', img, a)[0]
def u16(a): return struct.unpack_from('<H', img, a)[0]
def i16(a): return struct.unpack_from('<h', img, a)[0]
def i64(a): return struct.unpack_from('<q', img, a)[0]
def in_(a, r): return r[0] <= a < r[1]

IDENT = re.compile(rb'[A-Za-z_][A-Za-z0-9_:]*\Z')
def cstr(a):
    if not in_(a, RO): return None
    e = img.find(b'\0', a, a + 256)
    if e <= a: return None
    s = img[a:e]
    return s.decode() if IDENT.match(s) else None
def wstr(a):
    if not in_(a, RO) or a % 2: return None
    out = []
    while len(out) < 256:
        c = u16(a + 2 * len(out))
        if c == 0: break
        if c > 127: return None
        out.append(chr(c))
    s = ''.join(out)
    return s if s and IDENT.match(s.encode()) else None

# ---- ARM64: collect ADRP+ADD targets of a small function ----
def sx(v, bits): return v - (1 << bits) if v & (1 << (bits - 1)) else v
def func_targets(fa, maxins=48, follow=2):
    regs = {}; res = []
    for i in range(maxins):
        pc = fa + 4 * i
        ins = u32(pc)
        if (ins & 0x9F000000) == 0x90000000:
            imm = sx((((ins >> 5) & 0x7FFFF) << 2) | ((ins >> 29) & 3), 21) << 12
            regs[ins & 31] = (pc & ~0xFFF) + imm
        elif (ins & 0xFF800000) == 0x91000000:
            rn = (ins >> 5) & 31; imm = (ins >> 10) & 0xFFF
            if (ins >> 22) & 1: imm <<= 12
            if rn in regs:
                t = regs[rn] + imm; res.append(t); regs[ins & 31] = t
        elif (ins & 0xFC000000) == 0x14000000:  # B
            if follow and not res:
                return func_targets(pc + sx(ins & 0x3FFFFFF, 26) * 4, maxins, follow - 1)
            break
        elif ins == 0xD65F03C0 and res:  # RET
            pass
    return res

# ---- scan data.rel.ro as qwords ----
base = DRR[0]
Q = np.frombuffer(img[base:DRR[1] - (DRR[1] - base) % 8], dtype='<u8')
n = len(Q)
inTX = (Q >= TX[0]) & (Q < TX[1])
inRO = (Q >= RO[0]) & (Q < RO[1])
inDRR = (Q >= DRR[0]) & (Q < DRR[1])
z = Q == 0

# FStructParams: Outer(tx) Super(0|tx) Ops(0|tx) Name(ro) Props(0|drr) {u16 num,u16 size,u8 align} {i32 objflags,u32 structflags}
structs = {}
cand = np.nonzero(inTX[:-6] & (z[1:-5] | inTX[1:-5]) & (z[2:-4] | inTX[2:-4]) & inRO[3:-3] & (z[4:-2] | inDRR[4:-2]))[0]
for i in cand:
    a = base + int(i) * 8
    name = cstr(qw(a + 24))
    if not name: continue
    nprop, size, align = u16(a + 40), u16(a + 42), img[a + 44]
    if align not in (1, 2, 4, 8, 16, 32, 64) or size == 0: continue
    if (qw(a + 32) == 0) != (nprop == 0): continue
    structs[a] = dict(name=name, super=qw(a + 8), props=qw(a + 32), nprops=nprop)

# FEnumParams: Outer(tx) DisplayName(0|tx) Name(ro) CppType(ro) Enumerators(drr) {i32 objflags, i16 num, u8 flags, u8 cppform}
enums = {}
cand = np.nonzero(inTX[:-5] & (z[1:-4] | inTX[1:-4]) & inRO[2:-3] & inRO[3:-2] & inDRR[4:-1])[0]
for i in cand:
    a = base + int(i) * 8
    name = cstr(qw(a + 16))
    if not name or not cstr(qw(a + 24)): continue
    num = i16(a + 44)
    if num <= 0 or num > 4096 or img[a + 47] > 2: continue
    ep = qw(a + 32); entries = []
    ok = True
    for k in range(num):
        en = cstr(qw(ep + 16 * k))
        if en is None: ok = False; break
        entries.append((en, i64(ep + 16 * k + 8)))
    if ok: enums[a] = dict(name=name, entries=entries)

# Class registration infos: OuterRegister(tx) InnerRegister(tx) Name(utf16) Info(data/bss)
classes = {}
cand = np.nonzero(inTX[:-3] & inTX[1:-2] & inRO[2:-1])[0]
for i in cand:
    a = base + int(i) * 8
    if not in_(qw(a + 24), DATA): continue
    name = wstr(qw(a + 16))
    if not name: continue
    zc, sc = qw(a), qw(a + 8)
    for t in func_targets(zc):
        if in_(t, DRR) and qw(t) == sc:
            classes[zc] = dict(name=name, params=t)
            break

print('structs', len(structs), 'enums', len(enums), 'classes', len(classes))

# ---- map Z_Construct function -> struct/enum ----
fn_struct = {}; fn_enum = {}
def resolve_struct(f):
    if f in fn_struct: return fn_struct[f]
    r = None
    for t in func_targets(f):
        if t in structs: r = structs[t]['name']; break
    fn_struct[f] = r
    return r
def resolve_enum(f):
    if f in fn_enum: return fn_enum[f]
    r = None
    for t in func_targets(f):
        if t in enums: r = enums[t]['name']; break
    fn_enum[f] = r
    return r

# ---- property parsing ----
T = dict(Byte=0, Bool=1, Int=2, Float=3, Object=4, Name=5, Delegate=6, Double=7, Array=8, Struct=9, Str=10, Text=11,
         Interface=12, MulticastDelegate=13, WeakObject=14, LazyObject=15, SoftObject=17, UInt64=18, UInt32=19,
         UInt16=20, Int64=21, Int16=22, Int8=23, Map=24, Set=25, Enum=26, FieldPath=27)
GEN = {0: 'Byte', 1: 'Int8', 2: 'Int16', 3: 'Int', 4: 'Int64', 5: 'UInt16', 6: 'UInt32', 7: 'UInt64', 0xA: 'Float',
       0xB: 'Double', 0xC: 'Bool', 0xD: 'SoftObject', 0xE: 'WeakObject', 0xF: 'LazyObject', 0x10: 'SoftObject',
       0x11: 'Object', 0x12: 'Object', 0x13: 'Interface', 0x14: 'Name', 0x15: 'Str', 0x16: 'Array', 0x17: 'Map',
       0x18: 'Set', 0x19: 'Struct', 0x1A: 'Delegate', 0x1B: 'MulticastDelegate', 0x1C: 'MulticastDelegate',
       0x1D: 'Text', 0x1E: 'Enum', 0x1F: 'FieldPath', 0x20: 'Double'}
unresolved = set()

def parse_props(arr, num, owner):
    ptrs = [qw(arr + 8 * k) for k in range(num)]
    out = []
    i = num - 1
    def construct(i):
        p = ptrs[i]
        name = cstr(qw(p))
        gen = img[p + 24] & 0x3F
        kind = GEN.get(gen)
        if kind is None or name is None:
            raise ValueError('bad prop %x in %s gen %x' % (p, owner, gen))
        node = dict(name=name, dim=u16(p + 48), kind=kind)
        i -= 1
        if kind in ('Array', 'Set'):
            node['inner'], i = construct(i)
        elif kind == 'Map':
            node['key'], i = construct(i)
            node['value'], i = construct(i)
        elif kind == 'Enum':
            node['inner'], i = construct(i)
            node['enum'] = resolve_enum(qw(p + 56))
        elif kind == 'Byte':
            f = qw(p + 56)
            node['enum'] = resolve_enum(f) if f else None
        elif kind == 'Struct':
            node['struct'] = resolve_struct(qw(p + 56))
            if node['struct'] is None: unresolved.add((owner, name))
        return node, i
    while i >= 0:
        node, i = construct(i)
        out.append(node)
    out.reverse()
    return out

schemas = {}
for a, s in structs.items():
    sup = resolve_struct(s['super']) if s['super'] else None
    try:
        props = parse_props(s['props'], s['nprops'], s['name']) if s['nprops'] else []
    except ValueError as e:
        print('skip struct', s['name'], e); continue
    if s['name'] in schemas and schemas[s['name']]['props'] and not props: continue
    schemas[s['name']] = dict(super=sup, props=props)
for zc, c in classes.items():
    p = c['params']
    deps = qw(p + 24); bits = u32(p + 56)
    ndeps = bits & 0xF; nprops = (bits >> 15) & 0x7FF
    sup = None
    if ndeps and deps:
        f = qw(deps)
        if f in classes: sup = classes[f]['name'][1:]
    try:
        props = parse_props(qw(p + 40), nprops, c['name']) if nprops else []
    except ValueError as e:
        print('skip class', c['name'], e); continue
    schemas[c['name'][1:]] = dict(super=sup, props=props)

print('schemas', len(schemas), 'unresolved struct refs', len(unresolved), sorted(unresolved)[:20])

# ---- write usmap ----
names = {}
def nid(s):
    if s not in names: names[s] = len(names)
    return names[s]
body = bytearray()
def w(fmt, *v): body.extend(struct.pack('<' + fmt, *v))
def wtype(node):
    k = node['kind']
    if k == 'Byte' and node.get('enum'):
        w('B', T['Enum']); w('B', T['Byte']); w('I', nid(node['enum'])); return
    w('B', T[k])
    if k == 'Enum':
        wtype(node['inner']); w('I', nid(node['enum'] or 'None'))
    elif k == 'Struct':
        w('I', nid(node['struct'] or 'None'))
    elif k in ('Array', 'Set'):
        wtype(node['inner'])
    elif k == 'Map':
        wtype(node['key']); wtype(node['value'])

enum_list = {}
for e in enums.values(): enum_list.setdefault(e['name'], e['entries'])
eb = bytearray()
w('I', len(enum_list))
for en, entries in enum_list.items():
    w('I', nid(en))
    if USMAP_VERSION >= 3: w('H', len(entries))
    else: w('B', min(len(entries), 255))
    for k, (nm, val) in enumerate(entries if USMAP_VERSION >= 3 else entries[:255]):
        if USMAP_VERSION >= 4: w('q', val)
        w('I', nid(nm))
w('I', len(schemas))
for sn, sc in schemas.items():
    w('I', nid(sn)); w('I', nid(sc['super']) if sc['super'] else 0xFFFFFFFF)
    total = sum(max(p['dim'], 1) for p in sc['props'])
    w('H', total); w('H', len(sc['props']))
    idx = 0
    for p in sc['props']:
        w('H', idx); w('B', max(p['dim'], 1)); w('I', nid(p['name'])); wtype(p)
        idx += max(p['dim'], 1)
nb = bytearray(struct.pack('<I', len(names)))
for s in names:
    b = s.encode()
    nb += (struct.pack('<H', len(b)) if USMAP_VERSION >= 2 else struct.pack('<B', len(b))) + b
payload = bytes(nb + body)
hdr = struct.pack('<HB', 0x30C4, USMAP_VERSION)
if USMAP_VERSION >= 1: hdr += struct.pack('<i', 0)
hdr += struct.pack('<BII', 0, len(payload), len(payload))
open(OUT, 'wb').write(hdr + payload)
print('wrote', OUT, len(hdr) + len(payload), 'bytes, names', len(names))
for chk in ('SkeletalMesh', 'SkinnedAsset', 'StreamableRenderAsset', 'AnimSequence', 'AnimSequenceBase', 'Skeleton', 'Texture2D', 'Texture', 'SkeletalMaterial', 'MaterialInstanceConstant', 'Object'):
    sc = schemas.get(chk)
    print(chk, '->', sc and sc['super'], sc and [(p['name'], p['kind'], p.get('struct') or p.get('enum')) for p in sc['props']][:12])
