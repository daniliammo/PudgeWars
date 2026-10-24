# Load an ELF .so into a flat memory image with R_AARCH64_RELATIVE relocations applied (supports APS2 packed relocs).
import struct, sys, pickle, os

def sleb(buf, pos):
    res = 0; shift = 0
    while True:
        b = buf[pos]; pos += 1
        res |= (b & 0x7f) << shift; shift += 7
        if not (b & 0x80):
            if b & 0x40: res -= 1 << shift
            return res, pos

def load(path):
    data = open(path, 'rb').read()
    e_phoff, = struct.unpack_from('<Q', data, 0x20)
    e_shoff, = struct.unpack_from('<Q', data, 0x28)
    e_phentsize, e_phnum, e_shentsize, e_shnum, e_shstrndx = struct.unpack_from('<HHHHH', data, 0x36)
    segs = []
    top = 0
    for i in range(e_phnum):
        p_type, p_flags, p_offset, p_vaddr, p_paddr, p_filesz, p_memsz, p_align = struct.unpack_from('<IIQQQQQQ', data, e_phoff + i * e_phentsize)
        if p_type == 1:
            segs.append((p_offset, p_vaddr, p_filesz, p_memsz)); top = max(top, p_vaddr + p_memsz)
    img = bytearray(top)
    for off, va, fs, ms in segs:
        img[va:va + fs] = data[off:off + fs]
    shstr_off = struct.unpack_from('<Q', data, e_shoff + e_shstrndx * e_shentsize + 0x18)[0]
    secs = {}
    for i in range(e_shnum):
        name, typ, flags, addr, off, size = struct.unpack_from('<IIQQQQ', data, e_shoff + i * e_shentsize)
        n = data[shstr_off + name:data.index(b'\0', shstr_off + name)].decode()
        secs[n] = (addr, off, size, typ)
    # relocations
    nrel = 0
    a, off, size, typ = secs['.rela.dyn']
    buf = data[off:off + size]
    if buf[:4] == b'APS2':
        pos = 4
        count, pos = sleb(buf, pos)
        r_offset, pos = sleb(buf, pos)
        r_info = 0; r_addend = 0
        done = 0
        while done < count:
            gsize, pos = sleb(buf, pos)
            gflags, pos = sleb(buf, pos)
            if gflags & 2: gdelta, pos = sleb(buf, pos)
            if gflags & 1: r_info, pos = sleb(buf, pos)
            if (gflags & 8) and (gflags & 4):
                d, pos = sleb(buf, pos); r_addend += d
            elif not (gflags & 8):
                r_addend = 0
            for _ in range(gsize):
                if gflags & 2: r_offset += gdelta
                else:
                    d, pos = sleb(buf, pos); r_offset += d
                if not (gflags & 1): r_info, pos = sleb(buf, pos)
                if (gflags & 8) and not (gflags & 4):
                    d, pos = sleb(buf, pos); r_addend += d
                if (r_info & 0xffffffff) == 1027:
                    struct.pack_into('<Q', img, r_offset, r_addend & 0xffffffffffffffff); nrel += 1
            done += gsize
    else:
        for i in range(0, size, 24):
            ro, ri, ra = struct.unpack_from('<QQq', buf, i)
            if (ri & 0xffffffff) == 1027:
                struct.pack_into('<Q', img, ro, ra & 0xffffffffffffffff); nrel += 1
    return img, secs, nrel

if __name__ == '__main__':
    img, secs, nrel = load(sys.argv[1])
    print('image', hex(len(img)), 'relative relocs', nrel)
    with open(sys.argv[2], 'wb') as f: pickle.dump((bytes(img), secs), f)
