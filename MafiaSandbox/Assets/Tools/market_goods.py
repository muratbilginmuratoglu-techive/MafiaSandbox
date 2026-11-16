#!/usr/bin/env python3
"""Replace the grey/colour cubes on the Dukkan_Market.glb shelves with low-poly shop goods (cans, bottles, cereal boxes, bread, tins, soft packs).
Usage: market_goods.py <in.glb> <out.glb>.  Goods = the 0.3 m cube-ish boxes (24 vertices each) at shelf height, everything else is kept."""
import json, struct, sys, math
import numpy as np

src, dst = sys.argv[1], sys.argv[2]
f = open(src, 'rb').read()
jl = struct.unpack('<I', f[12:16])[0]; j = json.loads(f[20:20 + jl]); b = f[20 + jl + 8:]
bv = j['bufferViews']
def acc(i, dt, n):
    a = j['accessors'][i]; v = bv[a['bufferView']]; return np.frombuffer(b, dtype=dt, count=a['count'] * n, offset=v.get('byteOffset', 0)).reshape(a['count'], n).copy()
P, N, C = acc(0, '<f4', 3), acc(1, '<f4', 3), acc(2, '<f4', 3)
I = acc(3, '<u2', 1).reshape(-1).astype(np.int64)

# --- which boxes are goods: ~0.3 x 0.18-0.4 x 0.3, sitting between y 0.3 and 1.7
goods = []
for k in range(0, len(P), 24):
    p = P[k:k + 24]; mn, mx = p.min(0), p.max(0); s = mx - mn
    if abs(s[0] - 0.3) < 0.02 and abs(s[2] - 0.3) < 0.02 and 0.15 < s[1] < 0.45 and 0.2 < mn[1] < 1.7: goods.append((k, mn, mx, C[k].copy()))
print('goods boxes:', len(goods))
keep = np.ones(len(P), bool)
for k, *_ in goods: keep[k:k + 24] = False
remap = -np.ones(len(P), np.int64); remap[keep] = np.arange(keep.sum())
tri = I.reshape(-1, 3); tri = tri[keep[tri].all(1)]; tri = remap[tri]
P0, N0, C0 = P[keep], N[keep], C[keep]

# --- geometry helpers (flat shaded)
Pn, Nn, Cn, Tn = [], [], [], []
def quad(v, n, col):
    base = sum(len(x) for x in Pn)
    Pn.append(np.array(v, np.float32)); Nn.append(np.tile(np.array(n, np.float32), (4, 1))); Cn.append(np.tile(np.array(col, np.float32), (4, 1)))
    Tn.append(np.array([[base, base + 1, base + 2], [base, base + 2, base + 3]]))
def box(cx, y0, cz, sx, sy, sz, col):
    x0, x1, z0, z1, y1 = cx - sx / 2, cx + sx / 2, cz - sz / 2, cz + sz / 2, y0 + sy
    quad([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0, 0, 1), col)
    quad([(x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0)], (0, 0, -1), col)
    quad([(x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1)], (1, 0, 0), col)
    quad([(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], (-1, 0, 0), col)
    quad([(x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0)], (0, 1, 0), col)
    quad([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (0, -1, 0), col)
def cyl(cx, y0, cz, dia, h, col, seg=10):
    r = dia / 2; y1 = y0 + h
    for s in range(seg):
        a0, a1 = 2 * math.pi * s / seg, 2 * math.pi * (s + 1) / seg; am = (a0 + a1) / 2
        quad([(cx + r * math.cos(a0), y0, cz + r * math.sin(a0)), (cx + r * math.cos(a1), y0, cz + r * math.sin(a1)),
              (cx + r * math.cos(a1), y1, cz + r * math.sin(a1)), (cx + r * math.cos(a0), y1, cz + r * math.sin(a0))], (math.cos(am), 0, math.sin(am)), col)
    base = sum(len(x) for x in Pn)   # top cap
    ring = [(cx + r * math.cos(2 * math.pi * s / seg), y1, cz + r * math.sin(2 * math.pi * s / seg)) for s in range(seg)]
    Pn.append(np.array(ring + [(cx, y1, cz)], np.float32)); Nn.append(np.tile(np.array([0, 1, 0], np.float32), (seg + 1, 1))); Cn.append(np.tile(np.array(col, np.float32), (seg + 1, 1)))
    Tn.append(np.array([[base + seg, base + (s + 1) % seg, base + s] for s in range(seg)]))

WHITE, SILVER, BROWN, BROWN2, DARK = (0.95, 0.95, 0.92), (0.78, 0.8, 0.84), (0.72, 0.46, 0.22), (0.84, 0.58, 0.3), (0.15, 0.15, 0.17)
for gi, (k, mn, mx, col) in enumerate(goods):
    cx, cz, y0 = (mn[0] + mx[0]) / 2, (mn[2] + mx[2]) / 2, mn[1]; col = tuple(col)
    if col == (0.95, 0.95, 0.93) or sum(col) > 2.7: col = (0.85, 0.3, 0.25)   # white cubes become coloured packaging
    kind = gi % 6
    if kind == 0:     # cans, 2 x 2
        for dx in (-0.075, 0.075):
            for dz in (-0.075, 0.075):
                cyl(cx + dx, y0, cz + dz, 0.13, 0.22, col); cyl(cx + dx, y0 + 0.07, cz + dz, 0.134, 0.08, WHITE); cyl(cx + dx, y0 + 0.22, cz + dz, 0.11, 0.012, SILVER)
    elif kind == 1:   # bottles, 2
        for dx in (-0.075, 0.075):
            cyl(cx + dx, y0, cz, 0.12, 0.26, col); cyl(cx + dx, y0 + 0.09, cz, 0.124, 0.1, WHITE); cyl(cx + dx, y0 + 0.26, cz, 0.05, 0.1, col); cyl(cx + dx, y0 + 0.36, cz, 0.058, 0.03, WHITE)
    elif kind == 2:   # cereal / pasta box
        box(cx, y0, cz, 0.24, 0.36, 0.12, col); box(cx, y0 + 0.08, cz, 0.246, 0.16, 0.124, WHITE); box(cx, y0 + 0.11, cz, 0.15, 0.09, 0.126, col)
    elif kind == 3:   # bread loaves
        for t in range(2):
            box(cx, y0 + t * 0.15, cz, 0.3, 0.11, 0.14, BROWN); box(cx, y0 + 0.1 + t * 0.15, cz, 0.26, 0.045, 0.12, BROWN2)
    elif kind == 4:   # tins, stacked 2 x 2 x 2
        for dx in (-0.075, 0.075):
            for dz in (-0.075, 0.075):
                for t in range(2): cyl(cx + dx, y0 + t * 0.09, cz + dz, 0.13, 0.085, SILVER); cyl(cx + dx, y0 + t * 0.09 + 0.02, cz + dz, 0.134, 0.045, col)
    else:             # soft packs (crisps, rice)
        for dz in (-0.07, 0.07):
            box(cx, y0, cz + dz, 0.26, 0.34, 0.07, col); box(cx, y0 + 0.1, cz + dz, 0.262, 0.1, 0.074, WHITE); box(cx, y0 + 0.32, cz + dz, 0.27, 0.03, 0.08, tuple(c * 0.7 for c in col))

Pa, Na, Ca = np.concatenate(Pn), np.concatenate(Nn), np.concatenate(Cn)
base = len(P0)
tri = np.concatenate([tri, np.concatenate(Tn) + base])
P1, N1, C1 = np.concatenate([P0, Pa]), np.concatenate([N0, Na]), np.concatenate([C0, Ca])
assert len(P1) < 65535, len(P1)

# --- rewrite the glb: [positions][normals][colors][indices]
pos, nor, col, idx = P1.astype('<f4').tobytes(), N1.astype('<f4').tobytes(), C1.astype('<f4').tobytes(), tri.astype('<u2').reshape(-1).tobytes()
idx += b'\0' * ((4 - len(idx) % 4) % 4)
binb = pos + nor + col + idx
j['bufferViews'] = [{'buffer': 0, 'byteLength': len(pos), 'byteOffset': 0, 'target': 34962}, {'buffer': 0, 'byteLength': len(nor), 'byteOffset': len(pos), 'target': 34962},
                    {'buffer': 0, 'byteLength': len(col), 'byteOffset': len(pos) + len(nor), 'target': 34962}, {'buffer': 0, 'byteLength': tri.size * 2, 'byteOffset': len(pos) + len(nor) + len(col), 'target': 34963}]
n = len(P1)
j['accessors'] = [{'bufferView': 0, 'componentType': 5126, 'count': n, 'type': 'VEC3', 'min': P1.min(0).tolist(), 'max': P1.max(0).tolist()},
                  {'bufferView': 1, 'componentType': 5126, 'count': n, 'type': 'VEC3'}, {'bufferView': 2, 'componentType': 5126, 'count': n, 'type': 'VEC3'},
                  {'bufferView': 3, 'componentType': 5123, 'count': int(tri.size), 'type': 'SCALAR'}]
j['buffers'] = [{'byteLength': len(binb)}]
js = json.dumps(j, separators=(',', ':')).encode(); js += b' ' * ((4 - len(js) % 4) % 4)
out = struct.pack('<4sII', b'glTF', 2, 12 + 8 + len(js) + 8 + len(binb)) + struct.pack('<I4s', len(js), b'JSON') + js + struct.pack('<I4s', len(binb), b'BIN\0') + binb
open(dst, 'wb').write(out); print('wrote', dst, len(out), 'bytes,', n, 'vertices')
