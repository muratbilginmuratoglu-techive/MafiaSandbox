"""Low-poly city prop pack (same art style as the characters). Blender bpy. Units: metres, Z up. Character height 1.9 m."""
import bpy, bmesh, math, os, sys, random, json
from mathutils import Vector, Matrix

OUT = os.environ.get("PROPS_OUT", "/home/claude/out/props")
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

PAL = {
    # walls
    "wall_cream": (0.93, 0.88, 0.76), "wall_white": (0.95, 0.95, 0.93), "wall_sand": (0.88, 0.78, 0.60), "wall_peach": (0.94, 0.78, 0.66),
    "wall_grey": (0.78, 0.79, 0.80), "wall_blue": (0.72, 0.80, 0.90), "wall_green": (0.76, 0.84, 0.70), "wall_brick": (0.70, 0.36, 0.28),
    "wall_yellow": (0.95, 0.85, 0.50), "wall_rose": (0.90, 0.68, 0.68), "wall_dark": (0.40, 0.42, 0.46), "wall_terracotta": (0.80, 0.52, 0.38),
    # roofs / trims
    "roof_red": (0.62, 0.22, 0.16), "roof_brown": (0.42, 0.28, 0.18), "roof_grey": (0.35, 0.36, 0.40), "roof_dark": (0.22, 0.23, 0.26), "roof_green": (0.25, 0.42, 0.30), "roof_blue": (0.22, 0.32, 0.50),
    "trim": (0.97, 0.97, 0.97), "trim_dark": (0.25, 0.25, 0.28), "wood": (0.55, 0.36, 0.20), "wood_dark": (0.36, 0.23, 0.13), "wood_light": (0.75, 0.58, 0.38),
    "glass": (0.55, 0.72, 0.85), "glass_dark": (0.30, 0.42, 0.55), "door_red": (0.60, 0.15, 0.12), "door_blue": (0.15, 0.30, 0.55), "door_green": (0.15, 0.40, 0.25),
    "concrete": (0.72, 0.72, 0.70), "concrete_dark": (0.55, 0.55, 0.54), "asphalt": (0.30, 0.31, 0.34), "stone": (0.62, 0.60, 0.56), "metal": (0.60, 0.62, 0.66), "metal_dark": (0.28, 0.29, 0.32),
    "black": (0.05, 0.05, 0.06), "white": (0.95, 0.95, 0.95), "red": (0.80, 0.12, 0.10), "blue": (0.15, 0.35, 0.80), "yellow": (0.95, 0.80, 0.15), "orange": (0.95, 0.50, 0.10), "green": (0.20, 0.60, 0.30),
    "lblue": (0.55, 0.68, 0.85), "navy": (0.10, 0.14, 0.30), "silver": (0.80, 0.82, 0.85), "chrome": (0.88, 0.90, 0.92), "tyre": (0.10, 0.10, 0.11), "rim": (0.70, 0.72, 0.75),
    "car_red": (0.75, 0.12, 0.10), "car_blue": (0.15, 0.30, 0.65), "car_white": (0.92, 0.92, 0.92), "car_black": (0.12, 0.12, 0.14), "car_yellow": (0.95, 0.78, 0.10), "car_green": (0.20, 0.45, 0.30), "car_grey": (0.55, 0.57, 0.60), "car_teal": (0.15, 0.55, 0.55),
    "leaf1": (0.30, 0.56, 0.29), "leaf2": (0.42, 0.66, 0.31), "leaf3": (0.25, 0.47, 0.25), "leaf_autumn": (0.85, 0.50, 0.15), "trunk": (0.45, 0.30, 0.18), "hedge": (0.22, 0.45, 0.22), "grass": (0.50, 0.64, 0.42), "soil": (0.40, 0.28, 0.18),
    "rust": (0.45, 0.25, 0.15), "wall_faded": (0.64, 0.60, 0.52), "plank_old": (0.42, 0.34, 0.26), "dirt": (0.35, 0.30, 0.25), "moss": (0.35, 0.45, 0.28), "mattress": (0.72, 0.70, 0.62),
    "flower_r": (0.90, 0.25, 0.30), "flower_y": (0.95, 0.85, 0.25), "flower_p": (0.85, 0.40, 0.70), "water": (0.45, 0.70, 0.90), "light": (1.0, 0.95, 0.70),
}
_vmat = None
def vcol_material():
    global _vmat
    if _vmat: return _vmat
    m = bpy.data.materials.new("VColor"); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.9
    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Col"; nt.links.new(attr.outputs["Color"], b.inputs["Base Color"])
    _vmat = m; return m

class P:
    """Prop builder: accumulates coloured primitives into one flat-shaded mesh."""
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.facecol = {}
    def _add(self, bm, col, mat=None):
        bm.verts.index_update()
        if mat is not None: bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
        vmap = {v.index: self.bm.verts.new(v.co) for v in bm.verts}
        for f in bm.faces:
            try:
                nf = self.bm.faces.new([vmap[v.index] for v in f.verts]); self.facecol[nf] = PAL[col] if isinstance(col, str) else col
            except ValueError: pass
        bm.free()
    def box(self, c, s, col, rz=0.0, rx=0.0, ry=0.0):
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts: v.co.x *= s[0]; v.co.y *= s[1]; v.co.z *= s[2]
        m = Matrix.Translation(c) @ Matrix.Rotation(rz, 4, 'Z') @ Matrix.Rotation(ry, 4, 'Y') @ Matrix.Rotation(rx, 4, 'X')
        self._add(bm, col, m)
    def wedge(self, c, s, col, rz=0.0):
        """Triangular prism (gable): base s[0] x s[1], ridge along Y at height s[2]."""
        bm = bmesh.new(); w, d, h = s[0]/2, s[1]/2, s[2]
        v = [bm.verts.new(p) for p in [(-w,-d,0),(w,-d,0),(w,d,0),(-w,d,0),(0,-d,h),(0,d,h)]]
        for idx in [(0,1,4),(2,3,5),(0,4,5,3),(1,2,5,4),(3,2,1,0)]: bm.faces.new([v[i] for i in idx])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self._add(bm, col, Matrix.Translation(c) @ Matrix.Rotation(rz, 4, 'Z'))
    def hip(self, c, s, col, ridge=0.4, rz=0.0):
        """Hip roof: base s[0] x s[1], ridge length fraction along Y, height s[2]."""
        bm = bmesh.new(); w, d, h = s[0]/2, s[1]/2, s[2]; r = d * ridge
        v = [bm.verts.new(p) for p in [(-w,-d,0),(w,-d,0),(w,d,0),(-w,d,0),(0,-r,h),(0,r,h)]]
        for idx in [(0,1,4),(2,3,5),(0,4,5,3),(1,2,5,4),(3,2,1,0)]: bm.faces.new([v[i] for i in idx])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self._add(bm, col, Matrix.Translation(c) @ Matrix.Rotation(rz, 4, 'Z'))
    def pyramid(self, c, s, col):
        bm = bmesh.new(); w, d, h = s[0]/2, s[1]/2, s[2]
        v = [bm.verts.new(p) for p in [(-w,-d,0),(w,-d,0),(w,d,0),(-w,d,0),(0,0,h)]]
        for idx in [(0,1,4),(1,2,4),(2,3,4),(3,0,4),(3,2,1,0)]: bm.faces.new([v[i] for i in idx])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces); self._add(bm, col, Matrix.Translation(c))
    def cyl(self, c, r, h, col, seg=8, axis='Z', r2=None, rz=0.0):
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
        rot = {'Z': Matrix.Identity(4), 'X': Matrix.Rotation(math.pi/2, 4, 'Y'), 'Y': Matrix.Rotation(math.pi/2, 4, 'X')}[axis]
        self._add(bm, col, Matrix.Translation(c) @ Matrix.Rotation(rz, 4, 'Z') @ rot)
    def cone(self, c, r, h, col, seg=7):
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=0.0, depth=h)
        self._add(bm, col, Matrix.Translation((c[0], c[1], c[2] + h/2)))
    def ico(self, c, r, col, sub=1, sz=1.0):
        bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
        self._add(bm, col, Matrix.Translation(c) @ Matrix.Scale(sz, 4, (0, 0, 1)))
    def finish(self):
        self.bm.verts.index_update(); self.bm.faces.index_update()
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me)
        col = me.color_attributes.new(name="Col", type='FLOAT_COLOR', domain='CORNER')
        cols = [(0.8, 0.8, 0.8)] * len(me.polygons)
        for f, cc in self.facecol.items(): cols[f.index] = cc
        for p, cc in zip(me.polygons, cols):
            p.use_smooth = False
            for li in p.loop_indices: col.data[li].color = (cc[0], cc[1], cc[2], 1.0)
        me.materials.append(vcol_material()); self.bm.free(); me.update()
        ob = bpy.data.objects.new(self.name, me); scene.collection.objects.link(ob); return ob

# ============================================================== building parts
FLOOR_H = 3.0
def window(p, x, y, z, w, h, facing, frame="trim", glass="glass", sill=True, shutters=None, depth=0.06):
    """facing: 'S' (-Y), 'N' (+Y), 'E' (+X), 'W' (-X). (x,y) on the wall plane, z = sill height."""
    n = {'S': (0, -1), 'N': (0, 1), 'E': (1, 0), 'W': (-1, 0)}[facing]; along = (abs(n[1]), abs(n[0]))   # along-wall axis
    def B(cx, cz, ww, hh, dd, col):
        sx = ww if along[0] else dd; sy = ww if along[1] else dd
        p.box((x + n[0] * dd / 2 + (cx if along[0] else 0), y + n[1] * dd / 2 + (cx if along[1] else 0), cz), (sx, sy, hh), col)
    B(0, z + h/2, w + 0.16, h + 0.16, depth, frame)          # frame
    B(0, z + h/2, w, h, depth + 0.02, glass)                  # glass
    B(0, z + h/2, 0.05, h, depth + 0.04, frame)               # mullion
    B(0, z + h * 0.55, w, 0.05, depth + 0.04, frame)          # transom
    if sill: B(0, z - 0.04, w + 0.3, 0.08, depth + 0.1, frame)
    if shutters:
        B(-(w/2 + 0.22), z + h/2, 0.3, h + 0.1, depth, shutters); B((w/2 + 0.22), z + h/2, 0.3, h + 0.1, depth, shutters)

def door(p, x, y, facing, col="door_red", w=1.0, h=2.1, frame="trim", step=True, knob=True, glass_top=False):
    n = {'S': (0, -1), 'N': (0, 1), 'E': (1, 0), 'W': (-1, 0)}[facing]; along = (abs(n[1]), abs(n[0]))
    def B(cx, cz, ww, hh, dd, c2, off=0.0):
        sx = ww if along[0] else dd; sy = ww if along[1] else dd
        p.box((x + n[0] * (dd / 2 + off) + (cx if along[0] else 0), y + n[1] * (dd / 2 + off) + (cx if along[1] else 0), cz), (sx, sy, hh), c2)
    B(0, h/2 + 0.05, w + 0.2, h + 0.1, 0.08, frame); B(0, h/2, w, h, 0.1, col)
    B(0, h * 0.72, w * 0.6, 0.6 if glass_top else 0.5, 0.12, "glass" if glass_top else col)
    B(0, h * 0.3, w * 0.6, 0.6, 0.12, col)
    if knob: B(w * 0.3, 1.0, 0.08, 0.08, 0.16, "yellow")
    if step: B(0, 0.08, w + 0.8, 0.16, 0.6, "concrete", off=0.3)

def windows_along(p, length, origin, facing, z, w=1.2, h=1.4, spacing=2.4, skip=(), **kw):
    """Place windows evenly along a wall of given length. origin = (x,y) of wall centre."""
    n = max(1, int(length // spacing)); step = length / n
    for i in range(n):
        if i in skip: continue
        off = -length / 2 + step * (i + 0.5)
        if facing in ('S', 'N'): window(p, origin[0] + off, origin[1], z, w, h, facing, **kw)
        else: window(p, origin[0], origin[1] + off, z, w, h, facing, **kw)

def chimney(p, x, y, z, col="wall_brick"):
    p.box((x, y, z + 0.6), (0.6, 0.6, 1.2), col); p.box((x, y, z + 1.25), (0.75, 0.75, 0.12), "concrete_dark"); p.box((x, y, z + 1.4), (0.3, 0.3, 0.2), "metal_dark")

def villa(i, rng):
    p = P(f"Villa_{i+1:02d}")
    W = rng.choice([8, 9, 10, 11, 12]); D = rng.choice([7, 8, 9, 10]); floors = 1 if i < 9 else 2
    wall = rng.choice(["wall_cream", "wall_white", "wall_sand", "wall_peach", "wall_blue", "wall_green", "wall_yellow", "wall_rose", "wall_grey", "wall_terracotta"])
    roofc = rng.choice(["roof_red", "roof_brown", "roof_grey", "roof_dark", "roof_green", "roof_blue"])
    dcol = rng.choice(["door_red", "door_blue", "door_green", "wood_dark", "wood"]); trim = rng.choice(["trim", "trim_dark", "wood"])
    H = FLOOR_H * floors; roof = rng.choice(["gable", "hip", "flat", "gable"])
    shut = rng.choice([None, None, "trim_dark", "roof_green", "wood_dark"])
    # foundation + walls
    p.box((0, 0, 0.15), (W + 0.3, D + 0.3, 0.3), "concrete_dark")
    p.box((0, 0, H / 2 + 0.3), (W, D, H), wall)
    # floor band for 2-storey
    if floors == 2: p.box((0, 0, FLOOR_H + 0.3), (W + 0.1, D + 0.1, 0.18), trim)
    # roof
    ov = 0.6
    if roof == "gable":
        rh = rng.uniform(2.0, 3.0); rz = rng.choice([0, math.pi / 2])
        rs = (W + 2 * ov, D + 2 * ov, rh) if rz == 0 else (D + 2 * ov, W + 2 * ov, rh)
        p.wedge((0, 0, H + 0.3), rs, roofc, rz=rz)
        # gable wall infill
        gs = (W - 0.02, D - 0.02, rh - 0.05) if rz == 0 else (D - 0.02, W - 0.02, rh - 0.05)
        p.wedge((0, 0, H + 0.3), gs, wall, rz=rz)
        p.box((0, 0, H + 0.3 + rh), (0.3 if rz == 0 else D + 2 * ov + 0.2, D + 2 * ov + 0.2 if rz == 0 else 0.3, 0.14), roofc)
    elif roof == "hip":
        p.hip((0, 0, H + 0.3), (W + 2 * ov, D + 2 * ov, rng.uniform(1.8, 2.6)), roofc, ridge=0.35)
    else:
        p.box((0, 0, H + 0.3 + 0.12), (W + 0.4, D + 0.4, 0.25), roofc); p.box((0, 0, H + 0.3 + 0.45), (W + 0.4, D + 0.4, 0.5), wall)
        p.box((0, 0, H + 0.3 + 0.5), (W - 0.6, D - 0.6, 0.5), roofc)
    # eaves board
    p.box((0, 0, H + 0.3 - 0.1), (W + 2 * ov, D + 2 * ov, 0.2), trim)
    if rng.random() < 0.6 and roof != "flat": chimney(p, W * 0.3, D * 0.2, H + 0.3 + 1.0)
    # door on the south facade (porch)
    dx = rng.uniform(-W * 0.25, W * 0.25)
    door(p, dx, -D / 2, 'S', col=dcol, frame=trim, glass_top=rng.random() < 0.5)
    # porch roof
    p.box((dx, -D / 2 - 0.9, 2.55), (2.4, 1.8, 0.14), roofc); p.box((dx - 1.0, -D / 2 - 1.6, 1.25), (0.14, 0.14, 2.5), trim); p.box((dx + 1.0, -D / 2 - 1.6, 1.25), (0.14, 0.14, 2.5), trim)
    # windows per facade / floor
    for fl in range(floors):
        z = 0.3 + fl * FLOOR_H + 0.9
        skipS = set(); n = max(1, int(W // 2.4)); step = W / n
        for k in range(n):
            off = -W / 2 + step * (k + 0.5)
            if abs(off - dx) < 1.4 and fl == 0: skipS.add(k)
        windows_along(p, W, (0, -D / 2), 'S', z, skip=skipS, frame=trim, shutters=shut)
        windows_along(p, W, (0, D / 2), 'N', z, frame=trim, shutters=shut, spacing=3.0)
        windows_along(p, D, (W / 2, 0), 'E', z, frame=trim, shutters=shut, spacing=3.0)
        windows_along(p, D, (-W / 2, 0), 'W', z, frame=trim, shutters=shut, spacing=3.0)
    # garage (some)
    if rng.random() < 0.45:
        gw, gd = 3.6, 5.0; gx = W / 2 + gw / 2
        p.box((gx, -D / 2 + gd / 2, 1.3 + 0.3), (gw, gd, 2.6), wall); p.box((gx, -D / 2 + gd / 2, 0.15), (gw + 0.2, gd + 0.2, 0.3), "concrete_dark")
        p.box((gx, -D / 2 + gd / 2, 2.9 + 0.3 + 0.1), (gw + 0.6, gd + 0.6, 0.2), roofc)
        p.box((gx, -D / 2 - 0.05, 1.25 + 0.3), (2.8, 0.1, 2.3), "trim");
        for k in range(4): p.box((gx, -D / 2 - 0.1, 0.6 + 0.3 + k * 0.55), (2.8, 0.04, 0.06), "trim_dark")
        p.box((gx, -D / 2 - 1.5, 0.02), (3.2, 3.0, 0.04), "concrete")
    # balcony on 2nd floor
    if floors == 2 and rng.random() < 0.7:
        bx = rng.uniform(-W * 0.2, W * 0.2); p.box((bx, -D / 2 - 0.7, FLOOR_H + 0.3 + 0.08), (2.8, 1.4, 0.16), "concrete")
        for k in range(7): p.box((bx - 1.3 + k * 0.43, -D / 2 - 1.35, FLOOR_H + 0.3 + 0.6), (0.06, 0.06, 1.0), trim)
        p.box((bx, -D / 2 - 1.35, FLOOR_H + 0.3 + 1.1), (2.8, 0.08, 0.08), trim)
        p.box((bx - 1.37, -D / 2 - 0.7, FLOOR_H + 0.3 + 1.1), (0.08, 1.4, 0.08), trim); p.box((bx + 1.37, -D / 2 - 0.7, FLOOR_H + 0.3 + 1.1), (0.08, 1.4, 0.08), trim)
    # path + small garden bits
    p.box((dx, -D / 2 - 2.6, 0.02), (1.4, 3.0, 0.04), "stone")
    p.box((-W / 2 + 1.2, -D / 2 - 1.5, 0.3), (0.8, 0.8, 0.6), "concrete"); p.ico((-W / 2 + 1.2, -D / 2 - 1.5, 0.9), 0.55, "hedge", sub=1)
    ob = p.finish(); ob["footprint"] = [W + 4.4, D + 5.0]; return ob

def apartment(i, rng):
    p = P(f"Apartman_{i+1:02d}")
    floors = [2, 3, 4, 3, 4, 2, 4, 3][i % 8]; W = rng.choice([12, 14, 16, 18]); D = rng.choice([10, 11, 12])
    wall = rng.choice(["wall_cream", "wall_white", "wall_grey", "wall_sand", "wall_peach", "wall_blue", "wall_brick", "wall_dark"])
    trim = "trim" if wall != "wall_white" else "trim_dark"; accent = rng.choice(["roof_red", "roof_blue", "roof_green", "wall_terracotta", "navy", "roof_dark"])
    H = FLOOR_H * floors + 0.3; shop = rng.random() < 0.5
    p.box((0, 0, 0.15), (W + 0.4, D + 0.4, 0.3), "concrete_dark")
    p.box((0, 0, H / 2 + 0.15), (W, D, H - 0.3), wall)
    # ground floor band / shop front
    if shop:
        p.box((0, 0, 0.3 + FLOOR_H / 2), (W + 0.06, D + 0.06, FLOOR_H), "wall_dark")
        for k in range(3):
            sx = -W / 2 + W * (k + 0.5) / 3
            p.box((sx, -D / 2 - 0.05, 1.7), (W / 3 - 0.8, 0.1, 2.2), "glass_dark"); p.box((sx, -D / 2 - 0.5, 3.0), (W / 3 - 0.6, 1.0, 0.1), accent)
            p.box((sx, -D / 2 - 0.95, 2.75), (W / 3 - 0.6, 0.1, 0.5), accent)
        door(p, W / 2 - 1.6, -D / 2, 'S', col="glass_dark", frame="metal_dark", w=1.2, h=2.3, step=False)
    else:
        door(p, 0, -D / 2, 'S', col="glass_dark", frame="metal_dark", w=1.6, h=2.4, step=True)
        p.box((0, -D / 2 - 0.8, 2.7), (3.2, 1.6, 0.14), accent); p.box((-1.5, -D / 2 - 1.5, 1.35), (0.12, 0.12, 2.7), "metal_dark"); p.box((1.5, -D / 2 - 1.5, 1.35), (0.12, 0.12, 2.7), "metal_dark")
    # floors
    for fl in range(floors):
        z = 0.3 + fl * FLOOR_H + 0.9
        if fl == 0 and shop: continue
        n = max(2, int(W // 2.8)); step = W / n
        for k in range(n):
            x = -W / 2 + step * (k + 0.5)
            if fl == 0 and abs(x - (0 if not shop else W / 2 - 1.6)) < 1.6: continue
            balcony = fl > 0 and (k % 2 == 1) and rng.random() < 0.8
            if balcony:
                window(p, x, -D / 2, z - 0.2, 1.4, 2.0, 'S', frame=trim, glass="glass", sill=False)   # balcony door
                p.box((x, -D / 2 - 0.75, z - 0.95 + 0.08), (2.4, 1.5, 0.16), "concrete")
                p.box((x, -D / 2 - 1.45, z - 0.95 + 0.6), (2.4, 0.1, 1.0), accent)
                p.box((x - 1.15, -D / 2 - 0.75, z - 0.95 + 0.6), (0.1, 1.5, 1.0), accent); p.box((x + 1.15, -D / 2 - 0.75, z - 0.95 + 0.6), (0.1, 1.5, 1.0), accent)
                if rng.random() < 0.5: p.box((x + 0.7, -D / 2 - 0.5, z - 0.95 + 0.4), (0.5, 0.4, 0.4), "metal")  # AC unit
            else:
                window(p, x, -D / 2, z, 1.3, 1.4, 'S', frame=trim)
        windows_along(p, W, (0, D / 2), 'N', z, w=1.3, h=1.4, spacing=2.8, frame=trim)
        windows_along(p, D, (W / 2, 0), 'E', z, w=1.2, h=1.4, spacing=3.2, frame=trim)
        windows_along(p, D, (-W / 2, 0), 'W', z, w=1.2, h=1.4, spacing=3.2, frame=trim)
        if fl > 0: p.box((0, 0, 0.3 + fl * FLOOR_H), (W + 0.12, D + 0.12, 0.14), trim)
    # roof: parapet, stair box, water tank, antenna
    p.box((0, 0, H + 0.3), (W + 0.2, D + 0.2, 0.6), wall); p.box((0, 0, H + 0.05), (W - 0.4, D - 0.4, 0.1), "roof_grey")
    p.box((W * 0.25, D * 0.2, H + 1.2), (2.6, 2.6, 2.4), wall); p.box((W * 0.25, D * 0.2, H + 2.5), (3.0, 3.0, 0.2), "roof_dark")
    p.cyl((-W * 0.3, -D * 0.2, H + 0.9), 0.7, 1.4, "metal", seg=8); p.box((-W * 0.3, -D * 0.2, H + 0.1), (1.6, 1.6, 0.2), "metal_dark")
    p.box((-W * 0.1, D * 0.3, H + 1.5), (0.06, 0.06, 3.0), "metal_dark"); p.box((-W * 0.1, D * 0.3, H + 2.6), (1.0, 0.06, 0.06), "metal_dark"); p.box((-W * 0.1, D * 0.3, H + 2.2), (0.7, 0.06, 0.06), "metal_dark")
    # pavement
    p.box((0, -D / 2 - 1.5, 0.02), (W + 0.4, 3.0, 0.04), "concrete")
    ob = p.finish(); ob["footprint"] = [W + 2.0, D + 4.0]; return ob

# ============================================================== vehicles
WHEELS = []   # (name, x, y, r, w) collected while building a vehicle; turned into child objects at finish
def wheel(p, x, y, r=0.34, w=0.24):
    WHEELS.append((x, y, r, w))
def make_wheel_obj(parent, idx, x, y, r, w):
    q = P(f"Wheel_{idx}")
    q.cyl((0, 0, 0), r, w, "tyre", seg=12, axis='Y'); q.cyl((0, 0, 0), r * 0.62, w + 0.02, "rim", seg=8, axis='Y')
    for k in range(5):
        a = k / 5 * math.pi * 2; q.box((0.03 + r * 0.3 * math.cos(a), 0, r * 0.3 * math.sin(a)), (r * 0.45, w + 0.04, 0.05), "metal_dark", rx=a)
    ob = q.finish(); ob.parent = parent; ob.location = (x, y, r); return ob
def _prism(p, prof, col, taper=None, dx=0.0):
    """Extrude a side profile [(x, z, halfwidth), ...] (counter-clockwise in the XZ plane) across Y. One flat-shaded solid."""
    bm = bmesh.new(); n = len(prof)
    a = [bm.verts.new((x + dx, -w, z)) for (x, z, w) in prof]; b = [bm.verts.new((x + dx, w, z)) for (x, z, w) in prof]
    for i in range(n): j = (i + 1) % n; bm.faces.new([a[i], a[j], b[j], b[i]])
    bm.faces.new(a[::-1]); bm.faces.new(b)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); p._add(bm, col)

def car(name, kind, col, rng=None):
    """Low-poly vehicle: profile-extruded body + glass house, arches, trims. Front = +X."""
    p = P(name)
    spec = {  # L, W, hood, cabin, trunk, belt(z), roof(z), sill(z), ground clearance
        "sedan": (4.5, 1.82, 1.25, 1.95, 1.05, 0.98, 1.48, 0.32),
        "hatch": (4.0, 1.76, 1.05, 2.1, 0.45, 0.98, 1.52, 0.32),
        "suv": (4.7, 1.95, 1.2, 2.5, 0.7, 1.12, 1.78, 0.40),
        "pickup": (5.2, 1.95, 1.3, 1.5, 2.1, 1.1, 1.82, 0.42),
        "taxi": (4.5, 1.82, 1.25, 1.95, 1.05, 0.98, 1.48, 0.32),
        "police": (4.7, 1.88, 1.3, 2.0, 1.1, 1.0, 1.5, 0.32),
        "van": (5.3, 2.0, 0.9, 3.6, 0.5, 1.05, 2.35, 0.36),
        "ambulance": (5.6, 2.15, 1.0, 3.8, 0.5, 1.1, 2.5, 0.38),
        "bus": (10.5, 2.5, 0.0, 10.5, 0.0, 1.2, 3.1, 0.34),
    }[kind]
    L, Wd, hood, cab, trunk, belt, roof, sill = spec
    hw = Wd / 2; fx, bx = L / 2, -L / 2
    glass = "glass_dark"; dark = "metal_dark"
    if kind == "bus":
        # body: one tall slab with rounded-ish front, windows band, door
        _prism(p, [(bx, sill, hw), (bx, roof - 0.25, hw), (bx + 0.1, roof, hw - 0.05), (fx - 0.35, roof, hw - 0.05), (fx, roof - 0.5, hw), (fx, sill, hw)], col)
        p.box((fx - 0.08, 0, 2.0), (0.08, Wd - 0.3, 1.5), glass)                                  # windscreen
        for k in range(6): p.box((bx + 1.6 + k * 1.45, 0, 2.15), (1.15, Wd + 0.03, 0.95), glass)   # side windows
        p.box((bx - 0.03, 0, 2.1), (0.06, Wd - 0.5, 1.1), glass)                                   # rear window
        p.box((fx - 1.8, -hw - 0.02, 1.35), (1.0, 0.05, 2.0), glass); p.box((bx + 3.2, -hw - 0.02, 1.35), (1.0, 0.05, 2.0), glass)   # doors
        p.box((0, 0, belt - 0.05), (L + 0.02, Wd + 0.04, 0.12), "white")                           # stripe
        for k in [-1, 1]: wheel(p, fx - 1.6, k * (hw - 0.12), 0.48, 0.3); wheel(p, bx + 2.4, k * (hw - 0.12), 0.48, 0.3)
        p.box((fx - 0.05, 0, roof - 0.72), (0.08, 1.3, 0.28), "black")   # destination sign
        for sy in [-1, 1]: p.box((fx - 0.3, sy * (hw + 0.12), 2.1), (0.1, 0.1, 0.3), dark)
    else:
        wagon = kind in ("van", "ambulance")
        # ---- lower body profile (front→top→back, counter-clockwise seen from -Y)
        if wagon:
            body = [(fx, sill, hw * 0.96), (fx, belt - 0.15, hw), (fx - 0.15, belt + 0.1, hw), (fx - hood, belt + 0.25, hw),
                    (bx + 0.05, belt + 0.25, hw), (bx, belt - 0.1, hw), (bx, sill, hw * 0.96)]
        else:
            body = [(fx, sill, hw * 0.95), (fx, belt - 0.28, hw), (fx - 0.12, belt - 0.05, hw), (fx - hood, belt + 0.02, hw),
                    (bx + trunk, belt + 0.06 if kind != "pickup" else belt - 0.02, hw), (bx + 0.05, belt - 0.02, hw), (bx, belt - 0.25, hw), (bx, sill, hw * 0.95)]
        _prism(p, body, col)
        # ---- glass house / cabin
        c0 = fx - hood; c1 = c0 - cab            # windshield base x, rear glass base x
        gw = hw - 0.06
        if wagon:
            gh = [(c0, belt + 0.2, gw), (c0 - 0.75, roof, gw - 0.1), (bx + 0.12, roof, gw - 0.1), (bx + 0.08, belt + 0.2, gw)]
            _prism(p, gh, col)
            p.box((c0 - 0.35, 0, belt + 0.72), (0.14, Wd - 0.4, 0.95), glass, ry=-0.55)                                   # windscreen
            p.box((c0 - 1.25, -hw + 0.0, belt + 0.7), (1.0, 0.06, 0.8), glass); p.box((c0 - 1.25, hw - 0.0, belt + 0.7), (1.0, 0.06, 0.8), glass)   # cab side windows
            if kind == "van": p.box((bx + 1.6, -hw - 0.0, belt + 0.7), (1.2, 0.06, 0.7), glass); p.box((bx + 1.6, hw + 0.0, belt + 0.7), (1.2, 0.06, 0.7), glass)
            p.box((bx - 0.0, 0, belt + 0.75), (0.08, Wd - 0.5, 0.75), glass)                                             # rear window
            p.box((c0 - 2.1, -hw - 0.005, belt - 0.1), (0.015, 0.01, 1.7), dark); p.box((c0 - 2.1, hw + 0.005, belt - 0.1), (0.015, 0.01, 1.7), dark)   # door seams
        else:
            slant_f = 0.85 if kind != "suv" else 0.7; slant_b = 0.75 if kind in ("sedan", "taxi", "police") else (0.3 if kind == "hatch" else 0.15)
            if kind == "pickup": slant_b = 0.2
            gh = [(c0, belt + 0.02, gw), (c0 - slant_f, roof, gw - 0.12), (c1 + slant_b, roof, gw - 0.12), (c1, belt + 0.06, gw)]
            _prism(p, gh, glass)
            # roof skin + pillars in body colour
            p.box(((c0 - slant_f + c1 + slant_b) / 2, 0, roof + 0.025), (cab - slant_f - slant_b + 0.1, Wd - 0.34, 0.06), col)
            for sy in [-1, 1]:
                p.box((c0 - slant_f / 2, sy * (gw - 0.06), (belt + roof) / 2 + 0.02), (0.09, 0.07, math.hypot(slant_f, roof - belt) + 0.05), col, ry=-math.atan2(slant_f, roof - belt))   # A pillar
                p.box((c1 + slant_b / 2, sy * (gw - 0.06), (belt + roof) / 2 + 0.03), (0.09, 0.07, math.hypot(slant_b, roof - belt) + 0.05), col, ry=math.atan2(slant_b, roof - belt))   # C pillar
                p.box((c0 - slant_f - (cab - slant_f - slant_b) * 0.48, sy * (gw - 0.05), (belt + roof) / 2), (0.07, 0.06, roof - belt - 0.05), col)   # B pillar
                p.box((c0 - slant_f + 0.05, sy * (hw + 0.09), belt + 0.22), (0.12, 0.2, 0.09), col); p.box((c0 - slant_f + 0.05, sy * (hw + 0.09), belt + 0.22), (0.11, 0.14, 0.07), glass)   # mirrors
                # door seams + handles
                p.box((c0 - slant_f - 0.05, sy * (hw + 0.004), belt - 0.3), (0.015, 0.01, 0.6), dark); p.box((c1 + slant_b + 0.05, sy * (hw + 0.004), belt - 0.3), (0.015, 0.01, 0.6), dark)
                p.box((c0 - slant_f - (cab - slant_f - slant_b) * 0.48, sy * (hw + 0.004), belt - 0.3), (0.015, 0.01, 0.6), dark)
                p.box((c0 - slant_f - 0.45, sy * (hw + 0.015), belt - 0.12), (0.16, 0.03, 0.035), dark); p.box((c0 - slant_f - (cab - slant_f - slant_b) * 0.48 - 0.4, sy * (hw + 0.015), belt - 0.12), (0.16, 0.03, 0.035), dark)
            if kind == "pickup":   # open bed
                bed_x0 = c1 - 0.05; p.box(((bed_x0 + bx) / 2, 0, belt - 0.02), (bed_x0 - bx, Wd - 0.16, 0.06), dark)
                p.box(((bed_x0 + bx) / 2, 0, belt + 0.12), (bed_x0 - bx - 0.12, Wd - 0.6, 0.02), dark)
                p.box((bed_x0, 0, belt + 0.12), (0.06, Wd - 0.1, 0.3), col)
                for sy in [-1, 1]: p.box(((bed_x0 + bx) / 2, sy * (hw - 0.05), belt + 0.12), (bed_x0 - bx, 0.08, 0.3), col)
                p.box((bx + 0.03, 0, belt + 0.1), (0.06, Wd - 0.1, 0.3), col)
        # ---- wheel arches
        wr = 0.36 if not wagon and kind != "suv" and kind != "pickup" else 0.4
        wx_f, wx_b = fx - (0.95 if kind != "pickup" else 1.0), bx + (0.95 if not wagon else 1.15)
        for wx in (wx_f, wx_b):
            for sy in [-1, 1]: wheel(p, wx, sy * (hw - 0.08), wr, 0.26)
        # ---- front / rear details
        p.box((fx + 0.02, 0, sill + 0.08), (0.22, Wd + 0.06, 0.26), dark); p.box((bx - 0.02, 0, sill + 0.08), (0.22, Wd + 0.06, 0.26), dark)    # bumpers
        p.box((fx + 0.015, 0, belt - 0.42), (0.04, Wd * 0.42, 0.14), "black")                                                              # grille
        for sy in [-1, 1]:
            p.box((fx + 0.02, sy * (hw - 0.3), belt - 0.4), (0.05, 0.4, 0.16), "light"); p.box((bx - 0.02, sy * (hw - 0.3), belt - 0.4), (0.05, 0.42, 0.14), "red")
            p.box((fx + 0.02, sy * (hw - 0.28), sill + 0.3), (0.03, 0.22, 0.08), "yellow")                                                 # fog lamps
        p.box((fx + 0.03, 0, sill + 0.2), (0.03, 0.5, 0.11), "white"); p.box((bx - 0.03, 0, sill + 0.2), (0.03, 0.5, 0.11), "white")        # plates
        p.cyl((bx - 0.05, -hw + 0.35, sill + 0.02), 0.04, 0.15, dark, seg=8, axis='X')                                                       # exhaust
        # ---- liveries
        if kind == "taxi":
            p.box((c0 - 1.2, 0, roof + 0.22), (0.65, 0.26, 0.3), "yellow"); p.box((c0 - 1.2, 0, roof + 0.22), (0.67, 0.2, 0.14), "black")
            for sy in [-1, 1]:
                for k in range(8): p.box((bx + 0.5 + k * 0.45, sy * (hw + 0.006), belt - 0.22 + (0.07 if k % 2 else 0)), (0.2, 0.01, 0.07), "black")
        if kind == "police":
            p.box((c0 - 1.0, 0, roof + 0.12), (0.28, 1.25, 0.16), dark)
            p.box((c0 - 1.0, 0.4, roof + 0.16), (0.3, 0.45, 0.2), "blue"); p.box((c0 - 1.0, -0.4, roof + 0.16), (0.3, 0.45, 0.2), "red")
            for sy in [-1, 1]: p.box((0, sy * (hw + 0.006), belt - 0.28), (L * 0.72, 0.01, 0.22), "navy")
            p.box((fx - hood * 0.45, 0, belt + 0.03), (hood * 0.5, Wd - 0.7, 0.02), "navy", ry=0.04)
            p.box((fx + 0.04, 0, sill + 0.02), (0.08, Wd * 0.9, 0.2), "black")   # push bar
        if kind == "ambulance":
            for sy in [-1, 1]:
                p.box((bx + 1.6, sy * (hw + 0.005), belt + 0.55), (0.75, 0.01, 0.22), "red"); p.box((bx + 1.6, sy * (hw + 0.005), belt + 0.55), (0.22, 0.01, 0.75), "red")
                p.box((0, sy * (hw + 0.005), belt - 0.15), (L * 0.95, 0.01, 0.12), "red")
            p.box((c0 - 0.6, 0, roof + 0.1), (0.5, 1.2, 0.2), "blue"); p.box((c0 - 0.6, 0, roof + 0.1), (0.52, 0.3, 0.22), "red")
            p.box((bx + 0.0, 0, belt + 0.1), (0.04, Wd - 0.4, 0.5), "white")
            p.box((bx - 0.0, 0, belt + 0.5), (0.03, 0.8, 0.5), "red"); p.box((bx - 0.0, 0, belt + 0.5), (0.035, 0.25, 0.5), "white"); p.box((bx - 0.0, 0, belt + 0.5), (0.035, 0.8, 0.16), "white")
        if kind == "van":
            for sy in [-1, 1]: p.box((0, sy * (hw + 0.005), belt - 0.1), (L * 0.9, 0.01, 0.1), "white")
    ob = p.finish(); ob["footprint"] = [L, Wd]
    for i, (x, y, r, w) in enumerate(WHEELS): make_wheel_obj(ob, i, x, y, r, w)
    WHEELS.clear(); return ob

def motorcycle(name, col):
    p = P(name)
    p.cyl((0.75, 0, 0.33), 0.33, 0.12, "tyre", seg=12, axis='Y'); p.cyl((0.75, 0, 0.33), 0.2, 0.14, "rim", seg=8, axis='Y')
    p.cyl((-0.7, 0, 0.33), 0.33, 0.16, "tyre", seg=12, axis='Y'); p.cyl((-0.7, 0, 0.33), 0.2, 0.18, "rim", seg=8, axis='Y')
    p.box((0.0, 0, 0.6), (1.1, 0.3, 0.3), col, ry=-0.1)                       # tank/frame
    p.box((-0.35, 0, 0.78), (0.7, 0.32, 0.12), "black")                         # seat
    p.box((0.1, 0, 0.42), (0.7, 0.42, 0.3), "metal_dark")                       # engine
    p.box((-0.5, 0.2, 0.3), (0.9, 0.08, 0.06), "chrome", ry=0.1); p.box((-0.5, -0.2, 0.3), (0.9, 0.08, 0.06), "chrome", ry=0.1)   # exhausts
    p.box((0.55, 0, 0.65), (0.08, 0.08, 0.7), "chrome", ry=0.5)                 # fork
    p.box((0.45, 0, 0.98), (0.1, 0.7, 0.05), "metal_dark"); p.box((0.45, 0.33, 1.0), (0.05, 0.12, 0.05), "black"); p.box((0.45, -0.33, 1.0), (0.05, 0.12, 0.05), "black")
    p.box((0.62, 0, 0.9), (0.15, 0.2, 0.2), "light")                           # headlight
    p.box((-0.95, 0, 0.6), (0.12, 0.15, 0.1), "red")
    p.box((-0.75, 0, 0.72), (0.5, 0.36, 0.08), col)                            # rear fender
    p.box((0.75, 0, 0.72), (0.5, 0.2, 0.06), col)
    ob = p.finish(); ob["footprint"] = [2.0, 0.8]; return ob

def bicycle(name, col):
    p = P(name)
    for x in (0.55, -0.55): p.cyl((x, 0, 0.34), 0.34, 0.03, "tyre", seg=14, axis='Y'); p.cyl((x, 0, 0.34), 0.3, 0.02, "metal", seg=14, axis='Y')
    p.box((0.0, 0, 0.6), (0.9, 0.04, 0.04), col, ry=0.25); p.box((-0.3, 0, 0.55), (0.04, 0.04, 0.5), col, ry=0.3); p.box((0.4, 0, 0.6), (0.04, 0.04, 0.6), col, ry=-0.4)
    p.box((-0.3, 0, 0.85), (0.28, 0.14, 0.06), "black"); p.box((0.45, 0, 0.95), (0.04, 0.5, 0.04), "metal")
    ob = p.finish(); ob["footprint"] = [1.7, 0.5]; return ob

# ============================================================== street props & nature
def tree_pine(name, s=1.0, col=("leaf1", "leaf3")):
    p = P(name); p.cyl((0, 0, 0.7 * s), 0.18 * s, 1.4 * s, "trunk", seg=6)
    p.cone((0, 0, 1.1 * s), 1.3 * s, 2.2 * s, col[0]); p.cone((0, 0, 2.3 * s), 1.0 * s, 1.9 * s, col[1]); p.cone((0, 0, 3.4 * s), 0.7 * s, 1.5 * s, col[0])
    ob = p.finish(); ob["footprint"] = [2.6 * s, 2.6 * s]; return ob
def tree_round(name, s=1.0, col="leaf2"):
    p = P(name); p.cyl((0, 0, 1.0 * s), 0.2 * s, 2.0 * s, "trunk", seg=6)
    p.ico((0, 0, 2.6 * s), 1.3 * s, col, sub=1, sz=0.9); p.ico((0.6 * s, 0.3 * s, 2.2 * s), 0.8 * s, col, sub=1); p.ico((-0.5 * s, -0.4 * s, 2.4 * s), 0.9 * s, "leaf1", sub=1)
    ob = p.finish(); ob["footprint"] = [2.8 * s, 2.8 * s]; return ob
def tree_palm(name):
    p = P(name)
    for k in range(5): p.cyl((0.08 * k, 0.03 * k, 0.6 + k * 1.1), 0.18 - k * 0.015, 1.2, "trunk", seg=6)
    for k in range(7):
        a = k / 7 * math.pi * 2; p.box((0.4 + 1.1 * math.cos(a), 0.15 + 1.1 * math.sin(a), 5.9), (2.4, 0.5, 0.08), "leaf2", rz=a, ry=0.35)
    p.ico((0.4, 0.15, 5.7), 0.35, "wood_dark", sub=0)
    ob = p.finish(); ob["footprint"] = [3, 3]; return ob
def bush(name, s=1.0, col="hedge"):
    p = P(name); p.ico((0, 0, 0.5 * s), 0.6 * s, col, sub=1, sz=0.85); p.ico((0.5 * s, 0.2 * s, 0.4 * s), 0.45 * s, "leaf1", sub=1); p.ico((-0.4 * s, -0.3 * s, 0.4 * s), 0.4 * s, col, sub=1)
    ob = p.finish(); ob["footprint"] = [1.6 * s, 1.6 * s]; return ob
def hedge(name, L=2.0):
    p = P(name); p.box((0, 0, 0.6), (L, 0.6, 1.2), "hedge"); p.box((0, 0, 1.22), (L - 0.1, 0.5, 0.06), "leaf2")
    ob = p.finish(); ob["footprint"] = [L, 0.6]; return ob
def fence_wood(name, L=2.0):
    p = P(name)
    for x in (-L / 2 + 0.05, L / 2 - 0.05): p.box((x, 0, 0.6), (0.1, 0.1, 1.2), "wood_dark")
    p.box((0, 0, 0.45), (L, 0.05, 0.1), "wood"); p.box((0, 0, 0.95), (L, 0.05, 0.1), "wood")
    n = int(L / 0.16)
    for k in range(n): p.box((-L / 2 + 0.08 + k * (L - 0.16) / (n - 1), 0.04, 0.6), (0.09, 0.03, 1.15), "wood_light")
    ob = p.finish(); ob["footprint"] = [L, 0.2]; return ob
def fence_metal(name, L=2.0):
    p = P(name)
    for x in (-L / 2 + 0.05, L / 2 - 0.05): p.box((x, 0, 0.8), (0.08, 0.08, 1.6), "metal_dark"); p.box((x, 0, 1.63), (0.12, 0.12, 0.06), "metal_dark")
    p.box((0, 0, 0.3), (L, 0.04, 0.05), "metal_dark"); p.box((0, 0, 1.4), (L, 0.04, 0.05), "metal_dark")
    n = int(L / 0.15)
    for k in range(n): p.box((-L / 2 + 0.1 + k * (L - 0.2) / (n - 1), 0, 0.85), (0.025, 0.025, 1.4), "metal_dark")
    p.box((0, 0, 0.1), (L, 0.3, 0.2), "concrete")
    ob = p.finish(); ob["footprint"] = [L, 0.3]; return ob
def wall_low(name, L=2.0):
    p = P(name); p.box((0, 0, 0.4), (L, 0.3, 0.8), "stone"); p.box((0, 0, 0.84), (L + 0.04, 0.4, 0.08), "concrete")
    ob = p.finish(); ob["footprint"] = [L, 0.4]; return ob
def atm(name):
    p = P(name); p.box((0, 0, 0.95), (0.9, 0.7, 1.9), "metal"); p.box((0, -0.36, 1.4), (0.6, 0.04, 0.45), "glass_dark"); p.box((0, -0.36, 1.05), (0.5, 0.04, 0.12), "metal_dark")
    p.box((0, -0.36, 0.85), (0.3, 0.04, 0.04), "black"); p.box((0, -0.36, 1.75), (0.7, 0.04, 0.12), "blue"); p.box((0, -0.4, 0.98), (0.7, 0.1, 0.06), "metal_dark")
    ob = p.finish(); ob["footprint"] = [0.9, 0.7]; return ob
def bench(name):
    p = P(name)
    for x in (-0.7, 0.7): p.box((x, 0, 0.22), (0.08, 0.5, 0.44), "metal_dark"); p.box((x, 0.28, 0.6), (0.08, 0.06, 0.5), "metal_dark", rx=-0.25)
    for k in range(4): p.box((0, -0.22 + k * 0.14, 0.46), (1.8, 0.11, 0.05), "wood")
    for k in range(3): p.box((0, 0.3 + k * 0.03, 0.62 + k * 0.14), (1.8, 0.05, 0.11), "wood", rx=-0.25)
    ob = p.finish(); ob["footprint"] = [1.8, 0.6]; return ob
def lamp_post(name):
    p = P(name); p.cyl((0, 0, 0.1), 0.2, 0.2, "metal_dark", seg=8); p.cyl((0, 0, 2.3), 0.07, 4.4, "metal_dark", seg=6)
    p.box((0.5, 0, 4.4), (1.0, 0.07, 0.07), "metal_dark"); p.box((1.0, 0, 4.3), (0.5, 0.3, 0.18), "metal_dark"); p.box((1.0, 0, 4.2), (0.4, 0.24, 0.05), "light")
    ob = p.finish(); ob["footprint"] = [0.4, 0.4]; return ob
def trash_bin(name):
    p = P(name); p.cyl((0, 0, 0.5), 0.3, 1.0, "metal_dark", seg=8); p.cyl((0, 0, 1.02), 0.33, 0.06, "green", seg=8); p.box((0, 0, 0.5), (0.64, 0.64, 0.5), "green"); p.box((0, -0.32, 0.85), (0.3, 0.02, 0.15), "black")
    ob = p.finish(); ob["footprint"] = [0.7, 0.7]; return ob
def dumpster(name):
    p = P(name); p.box((0, 0, 0.75), (1.8, 1.1, 1.3), "green"); p.box((0, 0, 1.45), (1.84, 1.14, 0.1), "roof_green", rx=0.05)
    for x in (-0.7, 0.7):
        for y in (-0.4, 0.4): p.cyl((x, y, 0.08), 0.08, 0.16, "black", seg=8, axis='Y')
    p.box((0, -0.56, 0.9), (1.2, 0.02, 0.3), "yellow")
    ob = p.finish(); ob["footprint"] = [1.8, 1.1]; return ob
def bus_stop(name):
    p = P(name)
    p.box((0, 0.6, 2.6), (3.6, 1.6, 0.1), "metal_dark"); p.box((0, 0.6, 2.72), (3.5, 1.5, 0.1), "glass")
    for x in (-1.7, 1.7): p.box((x, 1.3, 1.3), (0.1, 0.1, 2.6), "metal_dark"); p.box((x, -0.1, 1.3), (0.1, 0.1, 2.6), "metal_dark")
    p.box((0, 1.35, 1.4), (3.4, 0.04, 2.2), "glass"); p.box((1.7, 0.6, 1.4), (0.04, 1.4, 2.2), "glass")
    p.box((0, 0.9, 0.45), (2.6, 0.4, 0.08), "wood"); p.box((-1.0, 0.9, 0.2), (0.08, 0.3, 0.4), "metal_dark"); p.box((1.0, 0.9, 0.2), (0.08, 0.3, 0.4), "metal_dark")
    p.box((-1.2, 1.32, 1.6), (0.8, 0.03, 1.0), "white"); p.box((-1.2, 1.31, 1.9), (0.6, 0.02, 0.15), "blue"); p.box((-1.2, 1.31, 1.5), (0.6, 0.02, 0.4), "lblue")
    p.box((1.9, -0.6, 1.5), (0.08, 0.08, 3.0), "metal_dark"); p.box((1.9, -0.6, 2.9), (0.5, 0.06, 0.5), "blue"); p.box((1.9, -0.64, 2.9), (0.3, 0.02, 0.3), "white")
    ob = p.finish(); ob["footprint"] = [4.0, 1.8]; return ob
def hydrant(name):
    p = P(name); p.cyl((0, 0, 0.35), 0.14, 0.7, "red", seg=8); p.cyl((0, 0, 0.75), 0.16, 0.1, "red", seg=8); p.ico((0, 0, 0.84), 0.14, "red", sub=0)
    p.box((0, 0, 0.5), (0.5, 0.12, 0.12), "red"); p.cyl((0, -0.1, 0.5), 0.06, 0.2, "metal", seg=6, axis='Y'); p.cyl((0, 0, 0.04), 0.18, 0.08, "metal_dark", seg=8)
    ob = p.finish(); ob["footprint"] = [0.5, 0.5]; return ob
def traffic_light(name):
    p = P(name); p.cyl((0, 0, 0.08), 0.18, 0.16, "metal_dark", seg=8); p.cyl((0, 0, 2.0), 0.06, 3.9, "metal_dark", seg=6)
    p.box((0, 0, 3.3), (0.3, 0.26, 0.95), "metal_dark")
    for k, c in enumerate(["red", "yellow", "green"]): p.cyl((0, -0.14, 3.6 - k * 0.3), 0.1, 0.04, c, seg=8, axis='Y'); p.box((0, -0.2, 3.7 - k * 0.3), (0.3, 0.14, 0.03), "metal_dark")
    p.box((0.8, 0, 3.95), (1.6, 0.06, 0.06), "metal_dark"); p.box((1.5, 0, 3.4), (0.3, 0.26, 0.95), "metal_dark")
    for k, c in enumerate(["red", "yellow", "green"]): p.cyl((1.5, -0.14, 3.7 - k * 0.3), 0.1, 0.04, c, seg=8, axis='Y')
    ob = p.finish(); ob["footprint"] = [0.4, 0.4]; return ob
def sign(name, kind):
    p = P(name); p.cyl((0, 0, 1.2), 0.03, 2.4, "metal", seg=6)
    if kind == "stop": p.cyl((0, -0.03, 2.3), 0.4, 0.04, "red", seg=8, axis='Y'); p.box((0, -0.06, 2.3), (0.5, 0.02, 0.12), "white")
    elif kind == "parking": p.box((0, -0.03, 2.3), (0.6, 0.04, 0.6), "blue"); p.box((0, -0.06, 2.3), (0.12, 0.02, 0.36), "white"); p.box((0.08, -0.06, 2.4), (0.2, 0.02, 0.18), "white"); p.box((0.08, -0.07, 2.4), (0.08, 0.02, 0.06), "blue")
    else: p.box((0, -0.03, 2.4), (0.9, 0.04, 0.35), "green"); p.box((0, -0.06, 2.4), (0.7, 0.02, 0.08), "white")
    ob = p.finish(); ob["footprint"] = [0.3, 0.3]; return ob
def mailbox(name):
    p = P(name); p.box((0, 0, 0.5), (0.1, 0.1, 1.0), "metal_dark"); p.box((0, 0, 1.2), (0.5, 0.4, 0.45), "blue"); p.box((0, 0, 1.45), (0.5, 0.4, 0.1), "navy"); p.box((0, -0.21, 1.3), (0.3, 0.02, 0.05), "metal_dark")
    ob = p.finish(); ob["footprint"] = [0.5, 0.4]; return ob
def planter(name, big=False):
    p = P(name); s = 1.6 if big else 0.9
    p.box((0, 0, 0.25), (s, s * 0.6, 0.5), "concrete"); p.box((0, 0, 0.47), (s - 0.15, s * 0.6 - 0.15, 0.1), "soil")
    for k in range(5 if big else 3):
        x = -s / 2 + 0.25 + k * (s - 0.5) / (4 if big else 2); p.ico((x, (k % 2 - 0.5) * 0.2, 0.75), 0.22, "hedge", sub=0); p.ico((x, (k % 2 - 0.5) * 0.2, 0.95), 0.1, ["flower_r", "flower_y", "flower_p"][k % 3], sub=0)
    ob = p.finish(); ob["footprint"] = [s, s * 0.6]; return ob
def kiosk(name):
    p = P(name); p.box((0, 0, 1.25), (2.4, 2.0, 2.5), "wall_green"); p.pyramid((0, 0, 2.5), (2.9, 2.5, 0.6), "roof_dark")
    p.box((0, -1.02, 1.45), (1.8, 0.04, 1.0), "glass_dark"); p.box((0, -1.1, 0.9), (2.0, 0.3, 0.1), "wood"); p.box((0, -1.4, 2.3), (2.6, 0.8, 0.06), "red")
    for k in range(6): p.box((-0.8 + k * 0.32, -1.04, 1.65 - (k % 2) * 0.3), (0.22, 0.02, 0.28), ["yellow", "lblue", "white", "orange"][k % 4])
    ob = p.finish(); ob["footprint"] = [2.9, 2.9]; return ob
def picnic_table(name):
    p = P(name); p.box((0, 0, 0.74), (1.8, 0.8, 0.06), "wood"); p.box((0, 0.65, 0.44), (1.8, 0.3, 0.05), "wood"); p.box((0, -0.65, 0.44), (1.8, 0.3, 0.05), "wood")
    for x in (-0.6, 0.6): p.box((x, 0, 0.38), (0.08, 0.08, 0.72), "wood_dark", rx=0.5); p.box((x, 0, 0.38), (0.08, 0.08, 0.72), "wood_dark", rx=-0.5); p.box((x, 0, 0.42), (0.08, 1.6, 0.08), "wood_dark")
    ob = p.finish(); ob["footprint"] = [1.8, 1.6]; return ob
def phone_box(name):
    p = P(name); p.box((0, 0, 1.2), (0.6, 0.6, 2.4), "metal"); p.box((0, -0.31, 1.3), (0.5, 0.02, 1.6), "glass"); p.box((0, 0, 2.5), (0.7, 0.7, 0.2), "blue")
    ob = p.finish(); ob["footprint"] = [0.7, 0.7]; return ob
def fountain(name):
    p = P(name); p.cyl((0, 0, 0.35), 2.6, 0.7, "stone", seg=10); p.cyl((0, 0, 0.72), 2.3, 0.1, "water", seg=10); p.cyl((0, 0, 1.0), 0.3, 0.6, "stone", seg=8); p.cyl((0, 0, 1.4), 1.1, 0.16, "stone", seg=10); p.cyl((0, 0, 1.5), 0.95, 0.08, "water", seg=10); p.cyl((0, 0, 1.8), 0.15, 0.7, "stone", seg=6)
    ob = p.finish(); ob["footprint"] = [5.4, 5.4]; return ob
def gazebo(name):
    p = P(name)
    for k in range(6):
        a = k / 6 * math.pi * 2; p.box((2.0 * math.cos(a), 2.0 * math.sin(a), 1.4), (0.14, 0.14, 2.8), "wood")
    p.cyl((0, 0, 0.1), 2.4, 0.2, "concrete", seg=6); p.cyl((0, 0, 2.9), 2.5, 0.12, "wood", seg=6); p.cyl((0, 0, 3.6), 2.7, 1.3, "roof_red", seg=6, r2=0.2)
    ob = p.finish(); ob["footprint"] = [5.4, 5.4]; return ob
def wall_ad(name):
    p = P(name); p.box((0, 0, 0.1), (3.2, 0.6, 0.2), "concrete"); p.box((-1.4, 0, 1.6), (0.12, 0.12, 3.0), "metal_dark"); p.box((1.4, 0, 1.6), (0.12, 0.12, 3.0), "metal_dark")
    p.box((0, 0, 2.3), (3.2, 0.1, 1.8), "metal_dark"); p.box((0, -0.06, 2.3), (3.0, 0.02, 1.6), "white"); p.box((0, -0.07, 2.6), (2.0, 0.02, 0.4), "orange"); p.box((0, -0.07, 1.9), (2.4, 0.02, 0.25), "lblue")
    ob = p.finish(); ob["footprint"] = [3.2, 0.6]; return ob
def playground(name):
    p = P(name)
    p.box((0, 0, 0.75), (1.6, 1.6, 0.12), "wood");
    for x in (-0.7, 0.7):
        for y in (-0.7, 0.7): p.box((x, y, 0.9), (0.12, 0.12, 1.8), "wood")
    p.pyramid((0, 0, 1.8), (2.0, 2.0, 0.8), "roof_red"); p.box((1.8, 0, 0.45), (2.4, 0.7, 0.08), "yellow", ry=0.5); p.box((1.3, 0.5, 0.45), (0.05, 0.05, 2.2), "metal", ry=0.5)
    for k in range(5): p.box((-1.2, 0, 0.2 + k * 0.17), (0.5, 0.06, 0.05), "metal", ry=0.0)
    p.box((-1.4, 0.0, 0.5), (0.05, 0.05, 1.0), "metal"); p.box((-1.0, 0.0, 0.5), (0.05, 0.05, 1.0), "metal")
    p.box((-3.0, 0, 2.0), (2.0, 0.08, 0.08), "metal");
    for x in (-3.9, -2.1): p.box((x, 0.5, 1.0), (0.06, 0.06, 2.1), "metal", rx=0.25); p.box((x, -0.5, 1.0), (0.06, 0.06, 2.1), "metal", rx=-0.25)
    for x in (-3.4, -2.6): p.box((x, 0, 0.5), (0.4, 0.2, 0.05), "red"); p.box((x - 0.15, 0, 1.25), (0.02, 0.02, 1.5), "metal"); p.box((x + 0.15, 0, 1.25), (0.02, 0.02, 1.5), "metal")
    ob = p.finish(); ob["footprint"] = [7.0, 2.2]; return ob


# ============================================================== homeless camp props
def tent(name, col="wall_blue", L=2.6, W=2.0, H=1.5):
    p = P(name); p.wedge((0, 0, 0.02), (W, L, H), col, rz=0)
    p.wedge((0, 0, 0.02), (W - 0.1, L + 0.02, H - 0.06), "wall_dark", rz=0)          # dark interior seen through the open front
    p.box((0, -L / 2 - 0.02, H * 0.35), (W * 0.35, 0.03, H * 0.6), col)              # one closed flap
    p.cyl((0, -L / 2 + 0.05, H / 2), 0.03, H, "wood", seg=5); p.cyl((0, L / 2 - 0.05, H / 2), 0.03, H, "wood", seg=5)
    for sx in (-1, 1): p.box((sx * (W / 2 + 0.3), 0, 0.03), (0.06, 0.06, 0.06), "metal_dark")
    p.box((0, 0, 0.01), (W + 0.4, L + 0.3, 0.02), "soil")
    ob = p.finish(); ob["footprint"] = [W + 0.4, L + 0.3]; return ob
def tarp_shelter(name):
    p = P(name)
    p.box((0, 0, 2.0), (3.2, 2.6, 0.04), "wall_blue", rx=0.12); 
    for x, y in ((-1.4, -1.1), (1.4, -1.1), (-1.4, 1.1), (1.4, 1.1)): p.cyl((x, y, 1.0 + (0.15 if y > 0 else 0)), 0.04, 2.0 + (0.3 if y > 0 else 0), "wood", seg=5)
    p.box((0, 0, 0.12), (1.9, 0.9, 0.24), "wall_grey"); p.box((0, 0, 0.3), (1.8, 0.8, 0.12), "wall_cream")     # mattress
    p.box((0.9, 0.9, 0.25), (0.5, 0.5, 0.5), "wood"); p.box((-1.0, 0.8, 0.2), (0.6, 0.4, 0.4), "wall_brick")
    ob = p.finish(); ob["footprint"] = [3.2, 2.6]; return ob
def fire_barrel(name):
    p = P(name); p.cyl((0, 0, 0.45), 0.3, 0.9, "metal_dark", seg=10); p.cyl((0, 0, 0.15), 0.31, 0.04, "red", seg=10); p.cyl((0, 0, 0.75), 0.31, 0.04, "red", seg=10)
    p.cyl((0, 0, 0.86), 0.26, 0.06, "black", seg=10)
    p.box((0.05, 0.02, 0.95), (0.12, 0.08, 0.5), "wood_dark", rx=0.3, ry=0.4); p.box((-0.06, -0.03, 0.92), (0.1, 0.08, 0.45), "wood_dark", rx=-0.4, ry=-0.2)
    p.box((0, 0, 1.0), (0.22, 0.22, 0.26), "orange"); p.box((0.03, 0.02, 1.2), (0.14, 0.14, 0.2), "yellow"); p.box((-0.02, -0.03, 1.36), (0.08, 0.08, 0.14), "yellow")
    ob = p.finish(); ob["footprint"] = [0.7, 0.7]; return ob
def pallet(name):
    p = P(name)
    for k in range(5): p.box((0, -0.5 + k * 0.25, 0.13), (1.2, 0.1, 0.03), "wood_light")
    for x in (-0.55, 0, 0.55): p.box((x, 0, 0.06), (0.1, 1.1, 0.1), "wood")
    ob = p.finish(); ob["footprint"] = [1.2, 1.1]; return ob
def crate_stack(name):
    p = P(name); p.box((0, 0, 0.3), (0.6, 0.6, 0.6), "wood"); p.box((0.5, 0.1, 0.25), (0.5, 0.5, 0.5), "wood_light"); p.box((0.1, 0.05, 0.85), (0.5, 0.5, 0.5), "wood_dark")
    for z in (0.3, 0.85): p.box((0.1 if z > 0.5 else 0, 0.05 if z > 0.5 else 0, z), (0.52 if z > 0.5 else 0.62, 0.08, 0.06), "wood_dark")
    ob = p.finish(); ob["footprint"] = [1.2, 0.8]; return ob
def shopping_cart(name):
    p = P(name); p.box((0, 0, 0.75), (0.9, 0.55, 0.5), "metal"); p.box((0, 0, 0.75), (0.84, 0.49, 0.46), "metal_dark")
    p.box((0.5, 0, 1.0), (0.05, 0.6, 0.05), "red"); 
    for x in (-0.3, 0.3):
        for y in (-0.22, 0.22): p.cyl((x, y, 0.08), 0.08, 0.05, "black", seg=8, axis='Y'); p.box((x, y, 0.3), (0.04, 0.04, 0.4), "metal")
    p.box((0, 0, 1.05), (0.7, 0.4, 0.12), "wall_grey"); p.box((-0.2, 0.1, 1.15), (0.3, 0.2, 0.1), "wall_blue")   # bags
    ob = p.finish(); ob["footprint"] = [1.0, 0.7]; return ob
def junk_pile(name):
    p = P(name); p.box((0, 0, 0.2), (1.6, 1.2, 0.4), "wall_dark", rz=0.3); p.box((0.3, 0.2, 0.5), (0.7, 0.5, 0.3), "wall_grey", rz=-0.4); p.cyl((-0.5, -0.3, 0.3), 0.18, 0.6, "metal", seg=8, axis='Y')
    p.box((0.4, -0.4, 0.6), (0.5, 0.5, 0.1), "wood_light", rz=0.6); p.box((-0.2, 0.4, 0.7), (0.3, 0.3, 0.3), "wall_brick"); p.cyl((0.6, 0.5, 0.15), 0.25, 0.3, "black", seg=8)
    ob = p.finish(); ob["footprint"] = [1.8, 1.4]; return ob
def clothes_line(name):
    p = P(name); p.cyl((-1.5, 0, 0.9), 0.03, 1.8, "wood", seg=5); p.cyl((1.5, 0, 0.9), 0.03, 1.8, "wood", seg=5); p.box((0, 0, 1.75), (3.0, 0.015, 0.015), "white")
    for k, c in enumerate(["red", "white", "lblue", "yellow", "wall_green"]): p.box((-1.1 + k * 0.55, 0, 1.45), (0.4, 0.02, 0.55), c)
    ob = p.finish(); ob["footprint"] = [3.0, 0.2]; return ob
def graffiti_wall(name):
    p = P(name); p.box((0, 0, 1.3), (4.0, 0.3, 2.6), "concrete_dark")
    for k, (c, w, h) in enumerate([("red", 1.2, 0.8), ("yellow", 0.9, 0.5), ("lblue", 1.4, 0.6), ("green", 0.7, 0.9), ("orange", 1.0, 0.4)]):
        p.box((-1.4 + k * 0.7, -0.16, 0.9 + (k % 3) * 0.45), (w, 0.02, h), c)
    ob = p.finish(); ob["footprint"] = [4.0, 0.3]; return ob
def barrier(name):
    p = P(name); p.box((0, 0, 0.5), (2.4, 0.3, 1.0), "concrete", taper_top=0.6) if False else p.box((0, 0, 0.5), (2.4, 0.4, 1.0), "concrete"); p.box((0, 0, 0.9), (2.4, 0.42, 0.14), "yellow"); p.box((0, 0, 0.6), (2.4, 0.42, 0.14), "yellow")
    ob = p.finish(); ob["footprint"] = [2.4, 0.5]; return ob


# ============================================================== construction site & industrial
def construction_building(name):
    """3-storey reinforced-concrete skeleton under construction, 12 x 10 m. Front (open side with the ramp) = -Y."""
    p = P(name); W, D, FH = 12.0, 10.0, 3.2
    cols_x = [-5.6, -1.9, 1.9, 5.6]; cols_y = [-4.6, 0.0, 4.6]
    for f in range(4):
        z = f * FH
        if f < 3: p.box((0, 0, z + 0.15), (W, D, 0.3), "concrete")                                 # slab
        else: p.box((-2.5, 0, z + 0.15), (7, D, 0.3), "concrete")                                   # roof slab only half poured
        if f < 3:
            for x in cols_x:
                for y in cols_y:
                    if f == 2 and x > 0 and y > 0: p.box((x, y, z + 0.3 + 1.0), (0.42, 0.42, 2.0), "concrete")   # unfinished columns
                    else: p.box((x, y, z + 0.3 + FH / 2 - 0.15), (0.42, 0.42, FH - 0.3), "concrete")
    # rebar sticking out of the top of every column / unfinished column
    for x in cols_x:
        for y in cols_y:
            top = 3 * FH + 0.3 if not (x > 0 and y > 0) else 2 * FH + 2.3
            for dx, dy in [(-0.12, -0.12), (0.12, -0.12), (-0.12, 0.12), (0.12, 0.12)]: p.cyl((x + dx, y + dy, top + 0.5), 0.02, 1.0, "roof_brown", seg=4)
    # brick infill: back wall ground floor, side walls partly, first-floor back half
    p.box((0, 4.8, 0.3 + 1.4), (W - 0.9, 0.25, 2.8), "wall_brick"); p.box((0, 4.8, FH + 0.3 + 0.9), (W - 0.9, 0.25, 1.8), "wall_brick")
    p.box((-5.85, 1.5, 0.3 + 1.4), (0.25, 6.0, 2.8), "wall_brick"); p.box((5.85, -1.5, 0.3 + 1.0), (0.25, 5.0, 2.0), "wall_brick")
    for i in range(6): p.box((-2.6 + i * 0.35, -4.7, FH + 0.3 + 0.35), (0.3, 0.2, 0.7), "wall_brick")              # stacked bricks on 1st floor edge
    # stairs between floors (wedges) on the east side
    for f in range(2): p.wedge((4.4, -2.5, f * FH + 0.3), (1.6, 5.0, FH - 0.3), "concrete_dark", rz=0.0)
    # formwork / shoring props (wooden posts) on the second floor
    for x in [-4, -1, 2]: p.box((x, -2, 2 * FH + 0.3 + 1.4), (0.14, 0.14, 2.8), "wood")
    for x in [-4, -1, 2]: p.box((x, 2.5, 2 * FH + 0.3 + 1.4), (0.14, 0.14, 2.8), "wood")
    p.box((-1, 0, 3 * FH - 0.05), (7, 5.2, 0.1), "wood_light")                                                       # plywood formwork ceiling
    # safety net / tarp on the west face, planks, a cement bag pile and a ramp at the front
    p.box((-6.1, 0.5, FH + 1.8), (0.06, 8.5, 3.4), "orange")
    p.box((-6.1, -2.0, 2 * FH + 1.6), (0.06, 5.0, 3.0), "lblue")
    p.wedge((0, -6.2, 0.0), (2.4, 2.4, 0.3), "wood", rz=0.0)
    for i in range(3): p.box((3.2, 3.5, 0.3 + 0.18 + i * 0.3), (1.2, 0.7, 0.3), "wall_grey")
    p.box((3.2, 3.5, 0.3 + 1.1), (1.0, 0.6, 0.3), "wall_grey")
    for i in range(4): p.box((-3.5 + i * 0.3, 2.5, 0.3 + 0.15), (0.25, 3.0, 0.3), "wood_light")                       # planks lying on the ground floor
    p.box((-4.5, -3.5, 0.3 + 0.45), (1.0, 1.0, 0.9), "metal_dark"); p.cyl((-4.5, -3.5, 1.25), 0.5, 0.15, "metal", seg=10)   # mixer tub
    ob = p.finish(); ob["footprint"] = [W + 0.6, D + 2.6]; return ob

def scaffold(name, L=6.0, levels=2):
    p = P(name); H = 2.6; D = 1.2; n = int(L / 2)
    for i in range(n + 1):
        x = -L / 2 + i * 2
        for y in [-D / 2, D / 2]: p.cyl((x, y, levels * H / 2 + 0.3), 0.04, levels * H + 0.6, "metal", seg=6)
        for l in range(1, levels + 1): p.cyl((x, 0, l * H), 0.035, D, "metal", seg=6, axis='Y')
    for l in range(1, levels + 1):
        for y in [-D / 2, D / 2]: p.cyl((0, y, l * H), 0.035, L, "metal", seg=6, axis='X')
        p.box((0, 0, l * H + 0.05), (L, D - 0.2, 0.06), "wood_light")
        p.cyl((0, -D / 2 - 0.02, l * H + 1.0), 0.025, L, "metal", seg=5, axis='X')                                    # guard rail
    for i in range(n): p.box((-L / 2 + 1 + i * 2, D / 2, H * 0.5 + 0.3), (0.05, 0.05, math.hypot(2, H) * 0.95), "metal", ry=math.atan2(2, H) * (1 if i % 2 else -1))
    p.box((L / 2 - 0.9, -D / 2 - 0.25, H / 2 + 0.1), (0.5, 0.06, H + 0.2), "wood", rx=0.15)                           # ladder
    for i in range(6): p.box((L / 2 - 0.9, -D / 2 - 0.25 + (i - 2.5) * 0.015, 0.3 + i * 0.45), (0.46, 0.05, 0.05), "wood")
    for y in [-D / 2, D / 2]:
        for i in range(n + 1): p.box((-L / 2 + i * 2, y, 0.08), (0.3, 0.3, 0.16), "metal_dark")
    ob = p.finish(); ob["footprint"] = [L + 0.4, D + 0.6]; return ob

def tower_crane(name):
    p = P(name); H = 17.0; m = 0.9
    p.box((0, 0, 0.4), (3.2, 3.2, 0.8), "concrete"); p.box((0, 0, 1.0), (2.0, 2.0, 0.4), "concrete_dark")
    for sx in [-1, 1]:
        for sy in [-1, 1]: p.box((sx * m / 2, sy * m / 2, 1.2 + H / 2), (0.12, 0.12, H), "yellow")
    k = 0
    for z in range(2, int(H) + 1, 2):
        for sx in [-1, 1]: p.box((sx * m / 2, 0, 1.2 + z), (0.08, m, 0.08), "yellow")
        for sy in [-1, 1]: p.box((0, sy * m / 2, 1.2 + z), (m, 0.08, 0.08), "yellow")
        for sx in [-1, 1]: p.box((sx * m / 2, 0, 1.2 + z - 1), (0.06, 0.06, math.hypot(m, 2) * 0.98), "yellow", rx=math.atan2(m, 2) * (1 if (k + (sx > 0)) % 2 else -1))
        k += 1
    top = 1.2 + H
    p.box((0, 0, top + 0.3), (1.4, 1.4, 0.6), "yellow"); p.cyl((0, 0, top + 0.7), 0.5, 0.3, "metal_dark", seg=10)      # slewing unit
    p.box((1.1, -0.9, top + 1.1), (1.6, 1.2, 1.4), "white"); p.box((1.9, -0.9, top + 1.2), (0.1, 1.0, 0.9), "glass_dark")   # cab
    J = 14.0; CJ = 5.5
    for sy in [-1, 1]: p.box((J / 2, sy * 0.4, top + 1.9), (J, 0.1, 0.1), "yellow")
    p.box((J / 2, 0, top + 2.8), (J, 0.1, 0.1), "yellow")
    for i in range(int(J / 1.5)): p.box((0.75 + i * 1.5, 0, top + 2.35), (0.07, 0.07, 1.3), "yellow", ry=0.6 * (1 if i % 2 else -1))
    for sy in [-1, 1]: p.box((-CJ / 2, sy * 0.4, top + 1.9), (CJ, 0.1, 0.1), "yellow")
    p.box((-CJ + 0.8, 0, top + 1.4), (1.4, 1.0, 1.2), "concrete_dark"); p.box((-CJ + 0.8, 0, top + 1.0), (0.8, 0.8, 0.5), "concrete")   # counterweights
    p.box((0, 0, top + 4.5), (0.3, 0.3, 3.4), "yellow")                                                                  # apex
    p.cyl((J / 2, 0, top + 3.6), 0.02, J * 1.02, "metal_dark", seg=4, axis='X', rz=0.0)                                  # pendant lines (approx)
    p.box((J * 0.5, 0, top + 3.7), (J, 0.03, 0.03), "metal_dark", ry=-math.atan2(0.9, J / 2))
    p.box((-CJ * 0.5, 0, top + 3.7), (CJ, 0.03, 0.03), "metal_dark", ry=math.atan2(0.9, CJ / 2))
    hx = 9.0; p.cyl((hx, 0, top + 1.9 - 4.0), 0.015, 8.0, "metal_dark", seg=4)                                             # hoist rope
    p.box((hx, 0, top + 1.9 - 8.2), (0.4, 0.3, 0.5), "metal_dark"); p.box((hx, 0, top + 1.9 - 8.8), (0.1, 0.3, 0.5), "yellow")   # hook block
    p.box((hx, 0, top + 1.9 - 10.0), (2.0, 1.0, 0.25), "wood_light"); p.box((hx, 0, top + 1.9 - 9.6), (1.2, 0.8, 0.5), "wall_brick")   # pallet of bricks hanging
    ob = p.finish(); ob["footprint"] = [3.4, 3.4]; return ob

def cement_mixer(name):
    p = P(name)
    p.box((0, 0, 0.45), (1.0, 0.7, 0.1), "metal_dark"); p.cyl((0, 0, 0.25), 0.03, 0.5, "metal_dark", seg=5)
    for sy in [-1, 1]: p.cyl((-0.35, sy * 0.45, 0.25), 0.25, 0.12, "tyre", seg=10, axis='Y'); p.cyl((-0.35, sy * 0.45, 0.25), 0.12, 0.14, "rim", seg=8, axis='Y')
    p.box((0.45, 0, 0.5), (0.5, 0.08, 0.08), "metal", ry=-0.6); p.box((-0.6, 0, 0.5), (0.5, 0.08, 0.08), "metal", ry=0.6)
    p.cyl((0.0, 0, 1.0), 0.45, 0.55, "orange", seg=10, rz=0.0); p.cyl((0.0, 0, 1.4), 0.45, 0.35, "orange", seg=10, r2=0.28)
    p.box((0, -0.4, 0.75), (0.3, 0.3, 0.35), "metal_dark"); p.cyl((0, 0.5, 0.95), 0.18, 0.06, "black", seg=10, axis='Y')   # motor + hand wheel
    ob = p.finish(); ob["footprint"] = [1.4, 1.1]; return ob

def brick_pallet(name, col="wall_brick"):
    p = P(name)
    p.box((0, 0, 0.07), (1.2, 1.0, 0.14), "wood_light")
    for z in range(4):
        for i in range(5): p.box((-0.48 + i * 0.24, 0, 0.14 + 0.1 + z * 0.2), (0.22, 0.95, 0.19), col if (i + z) % 2 else "wall_terracotta")
    p.box((0, 0, 0.14 + 0.4), (1.22, 0.04, 0.82), "lblue"); p.box((0, 0, 0.14 + 0.4), (0.04, 0.97, 0.82), "lblue")           # straps
    ob = p.finish(); ob["footprint"] = [1.3, 1.1]; return ob

def heap(name, col, r=1.6, h=0.9, seg=9):
    p = P(name); p.cone((0, 0, 0), r, h, col, seg=seg); p.cone((r * 0.45, -r * 0.3, 0), r * 0.55, h * 0.55, col, seg=7)
    ob = p.finish(); ob["footprint"] = [r * 2.2, r * 2.0]; return ob

def site_container(name, col="blue"):
    p = P(name); L, W, H = 6.0, 2.45, 2.6
    p.box((0, 0, 0.1), (L, W, 0.2), "metal_dark"); p.box((0, 0, H / 2 + 0.1), (L, W, H - 0.1), col); p.box((0, 0, H + 0.08), (L + 0.05, W + 0.05, 0.1), "metal_dark")
    for i in range(10): p.box((-L / 2 + 0.4 + i * 0.6, W / 2 + 0.01, H / 2 + 0.1), (0.1, 0.03, H - 0.3), "white")
    p.box((-1.8, -W / 2 - 0.02, 1.1), (0.9, 0.05, 2.0), "white"); p.box((-1.5, -W / 2 - 0.05, 1.1), (0.06, 0.04, 0.2), "black")   # door + handle
    p.box((1.2, -W / 2 - 0.02, 1.6), (1.4, 0.05, 0.8), "glass_dark"); p.box((1.2, -W / 2 - 0.03, 1.6), (0.06, 0.03, 0.8), "white")
    p.box((0, 0, H + 0.3), (1.6, 0.9, 0.3), "metal"); p.box((-L / 2 - 0.15, 0, 0.5), (0.3, 1.0, 0.1), "metal_dark")      # AC unit, step
    ob = p.finish(); ob["footprint"] = [L + 0.3, W + 0.4]; return ob

def warehouse(name, col="wall_grey", roof="roof_dark", sign_col="blue"):
    p = P(name); W, D, H = 16.0, 12.0, 5.5
    p.box((0, 0, 0.1), (W + 1.0, D + 2.0, 0.2), "concrete_dark")
    p.box((0, 0, H / 2 + 0.2), (W, D, H), col)
    p.wedge((0, 0, H + 0.2), (W + 0.6, D + 0.6, 1.6), roof, rz=math.pi / 2)
    for i in range(7): p.box((-W / 2 + 1.3 + i * 2.2, -D / 2 - 0.02, H / 2 + 0.2), (0.06, 0.1, H - 0.2), "metal_dark")                # wall ribs front
    for i in range(7): p.box((-W / 2 + 1.3 + i * 2.2, D / 2 + 0.02, H / 2 + 0.2), (0.06, 0.1, H - 0.2), "metal_dark")
    p.box((-3.5, -D / 2 - 0.05, 2.2), (4.6, 0.1, 4.0), "metal_dark"); p.box((-3.5, -D / 2 - 0.08, 2.2), (4.3, 0.06, 3.8), "metal")   # roll-up door frame + door
    for i in range(7): p.box((-3.5, -D / 2 - 0.12, 0.55 + i * 0.55), (4.3, 0.02, 0.06), "metal_dark")
    p.box((3.5, -D / 2 - 0.05, 1.25), (1.0, 0.1, 2.3), "door_blue"); p.box((3.9, -D / 2 - 0.1, 1.2), (0.08, 0.04, 0.25), "black")      # man door
    for x in [1.0, 6.0]: p.box((x, -D / 2 - 0.04, 4.2), (2.0, 0.08, 0.9), "glass_dark")                                                # high windows
    for i in range(5): p.box((-W / 2 + 2 + i * 3, D / 2 + 0.04, 4.2), (2.0, 0.08, 0.9), "glass_dark")
    p.box((0, -D / 2 - 0.15, H + 0.9), (6.0, 0.15, 1.2), sign_col); p.box((0, -D / 2 - 0.24, H + 0.9), (5.4, 0.04, 0.6), "white")      # sign board
    p.box((-W / 2 - 0.4, 0, 1.0), (0.8, 3.0, 0.3), "concrete"); p.box((-W / 2 - 0.4, 2.0, 1.4), (0.8, 0.1, 0.9), "metal_dark")        # loading dock
    p.cyl((W / 2 - 1.5, D / 2 + 0.6, 3.0), 0.25, 6.0, "metal", seg=8)                                                                    # vent stack
    ob = p.finish(); ob["footprint"] = [W + 1.0, D + 2.0]; return ob

def tyre_pile(name):
    p = P(name); rng2 = random.Random(7)
    for (x, y, n) in [(0, 0, 4), (0.75, 0.2, 3), (-0.6, 0.5, 2), (0.2, 0.8, 1)]:
        for k in range(n):
            p.cyl((x + rng2.uniform(-0.05, 0.05), y + rng2.uniform(-0.05, 0.05), 0.11 + k * 0.22), 0.36, 0.2, "tyre", seg=12, rz=rng2.uniform(0, 1)); p.cyl((x, y, 0.11 + k * 0.22), 0.2, 0.22, "metal_dark", seg=8)
    p.cyl((-0.4, -0.8, 0.36), 0.36, 0.2, "tyre", seg=12, axis='X')
    ob = p.finish(); ob["footprint"] = [2.4, 2.4]; return ob

def rebar_bundle(name):
    p = P(name)
    for i in range(6):
        for j in range(2): p.cyl((0, -0.25 + i * 0.1, 0.06 + j * 0.1), 0.025, 4.0, "roof_brown", seg=5, axis='X')
    for x in [-1.5, 1.5]: p.box((x, 0, 0.12), (0.1, 0.7, 0.25), "wood")
    ob = p.finish(); ob["footprint"] = [4.2, 0.9]; return ob

def site_fence(name):
    """Temporary construction fence panel (3.5 m) with mesh suggested by a few diagonal bars, on concrete feet."""
    p = P(name); L = 3.5
    for x in [-L / 2 + 0.05, L / 2 - 0.05]: p.cyl((x, 0, 1.0), 0.03, 2.0, "metal", seg=6); p.box((x, 0, 0.1), (0.6, 0.25, 0.2), "concrete")
    for z in [0.15, 1.0, 1.95]: p.cyl((0, 0, z), 0.025, L, "metal", seg=6, axis='X')
    for i in range(6): p.box((-L / 2 + 0.3 + i * 0.58, 0, 1.05), (0.02, 0.02, 1.75), "metal")
    p.box((0, 0, 1.1), (L - 0.3, 0.02, 0.5), "orange"); p.box((0, 0, 1.1), (L - 0.5, 0.025, 0.25), "white")
    ob = p.finish(); ob["footprint"] = [L, 0.3]; return ob


# ============================================================== enterable buildings (shops, houses, bank, police) with interiors
# Local frame (Blender): front = -Y. Exported meta: solids (collider boxes) + points, converted to three.js local coords (x, z=-y) at export.
class Shell:
    def __init__(self, name, W, D, H, wall, floor, trim, sign=None):
        self.p = P(name); self.W, self.D, self.H = W, D, H; self.wall, self.floor, self.trim = wall, floor, trim
        self.solids = []; self.pts = {"shelves": [], "seats": []}; self.T = 0.25
        self.p.box((0, 0, 0.06), (W + 0.6, D + 0.6, 0.12), "concrete_dark"); self.p.box((0, 0, 0.13), (W - 0.1, D - 0.1, 0.04), floor)
    def solid(self, c, s, col=None, add=True, **kw):
        if col: self.p.box(c, s, col, **kw)
        if add: self.solids.append([round(c[0], 3), round(c[1], 3), round(s[0], 3), round(s[1], 3), round(c[2] + s[2] / 2, 3)])
    def walls(self, door_x=0.0, door_w=2.0, windows=((-3.0, 2.0), (3.0, 2.0)), win_h=1.6, win_z=1.35, back_door=None):
        W, D, H, T = self.W, self.D, self.H, self.T; hw, hd = W / 2, D / 2
        self.solid((0, hd - T / 2, H / 2), (W, T, H), self.wall)                                  # back
        if back_door is not None:   # service door in the back wall (visual only)
            self.p.box((back_door, hd - T / 2 - 0.02, 1.1), (1.0, 0.06, 2.1), "metal_dark")
        for sx in [-1, 1]: self.solid((sx * (hw - T / 2), 0, H / 2), (T, D, H), self.wall)         # sides
        # front: segments around the door, lintel, windows
        segs = []; x0 = -hw; dl, dr = door_x - door_w / 2, door_x + door_w / 2
        self.solid(((x0 + dl) / 2, -hd + T / 2, H / 2), (dl - x0, T, H), self.wall)
        self.solid(((dr + hw) / 2, -hd + T / 2, H / 2), (hw - dr, T, H), self.wall)
        self.p.box((door_x, -hd + T / 2, (2.3 + H) / 2), (door_w + 0.1, T, H - 2.3), self.wall)   # lintel (no collider: door opening below)
        self.p.box((door_x, -hd - 0.03, 2.3), (door_w + 0.25, 0.1, 0.12), self.trim)
        for sx in [-1, 1]: self.p.box((door_x + sx * (door_w / 2 + 0.06), -hd - 0.03, 1.15), (0.12, 0.1, 2.3), self.trim)
        for (wx, ww) in windows:
            self.p.box((wx, -hd - 0.02, win_z), (ww, 0.06, win_h), "glass"); self.p.box((wx, hd * 0 - hd + T - 0.02, win_z), (ww - 0.1, 0.02, win_h - 0.1), "glass_dark")
            self.p.box((wx, -hd - 0.05, win_z - win_h / 2 - 0.05), (ww + 0.2, 0.12, 0.1), self.trim); self.p.box((wx, -hd - 0.05, win_z + win_h / 2 + 0.05), (ww + 0.2, 0.12, 0.1), self.trim)
        # roof: slab + parapet
        self.p.box((0, 0, H + 0.12), (W + 0.3, D + 0.3, 0.25), "roof_dark"); self.p.box((0, 0, H + 0.4), (W + 0.3, D + 0.3, 0.3), self.wall)
        self.p.box((0, 0, H + 0.42), (W - 0.3, D - 0.3, 0.3), "roof_grey")
        self.p.box((hw - 1.5, hd - 1.5, H + 0.9), (1.2, 1.0, 0.7), "metal"); self.p.box((-hw + 1.2, hd - 1.0, H + 0.85), (0.5, 0.5, 0.6), "metal_dark")   # AC + vent
        self.pts["door"] = [round(door_x, 3), round(-hd, 3)]
    def sign(self, text_col, board_col="trim_dark", w=None, awning=None):
        W, D, H = self.W, self.D, self.H; w = w or W * 0.7
        self.p.box((0, -D / 2 - 0.1, H - 0.45), (w, 0.14, 0.8), board_col); self.p.box((0, -D / 2 - 0.19, H - 0.45), (w - 0.3, 0.04, 0.5), text_col)
        if awning:
            for i in range(int(w / 0.8)): self.p.box((-w / 2 + 0.4 + i * 0.8, -D / 2 - 0.65, H - 1.1), (0.8, 1.2, 0.06), awning if i % 2 else "white", rx=0.35)
    def counter(self, c, L, along='x', col="wood_dark", top="wood_light", register=True, glass=False):
        x, y = c; s = (L, 0.7, 1.05) if along == 'x' else (0.7, L, 1.05)
        self.solid((x, y, 0.525), s, col); self.p.box((x, y, 1.07), (s[0] + 0.1, s[1] + 0.1, 0.06), top)
        if glass: self.p.box((x, y, 0.75), (s[0] - 0.1, s[1] - 0.1, 0.6), "glass")
        if register: self.p.box((x + (L / 2 - 0.5 if along == 'x' else 0), y + (0 if along == 'x' else L / 2 - 0.5), 1.25), (0.35, 0.3, 0.3), "metal_dark")
    def shelf(self, c, L, along='x', col="wood", goods=("red", "blue", "yellow", "green", "orange", "white"), rng=None, levels=3, slot_y=None):
        """Shelf unit with coloured goods; also registers item slots (top level) for pickable items."""
        x, y = c; s = (L, 0.6, 1.7) if along == 'x' else (0.6, L, 1.7); rng = rng or random.Random(int(abs(x * 7 + y * 13)))
        self.solid((x, y, 0.85), s, None)
        for sx in [-1, 1]:
            if along == 'x': self.p.box((x + sx * (L / 2 - 0.03), y, 0.85), (0.06, 0.6, 1.7), col)
            else: self.p.box((x, y + sx * (L / 2 - 0.03), 0.85), (0.6, 0.06, 1.7), col)
        for lv in range(levels):
            z = 0.25 + lv * 0.55; self.p.box((x, y, z), (s[0], s[1], 0.05), col)
            n = int(L / 0.45)
            for i in range(n):
                if rng.random() < 0.25: continue
                off = -L / 2 + 0.3 + i * 0.45; gc = rng.choice(goods); gh = 0.18 + rng.random() * 0.22
                if along == 'x': self.p.box((x + off, y + rng.uniform(-0.12, 0.12), z + 0.03 + gh / 2), (0.3, 0.3, gh), gc)
                else: self.p.box((x + rng.uniform(-0.12, 0.12), y + off, z + 0.03 + gh / 2), (0.3, 0.3, gh), gc)
        for i in range(max(1, int(L / 1.2))):
            off = -L / 2 + 0.6 + i * 1.2
            self.pts["shelves"].append([round(x + (off if along == 'x' else 0), 2), round(y + (0 if along == 'x' else off), 2), round(0.25 + (levels - 1) * 0.55 + 0.06, 2)])
    def table(self, c, sq=0.9, col="wood", chairs=4, round_=False):
        x, y = c
        if round_: self.p.cyl((x, y, 0.75), sq / 2, 0.05, col, seg=10); self.p.cyl((x, y, 0.37), 0.05, 0.72, "metal_dark", seg=6); self.p.cyl((x, y, 0.03), 0.3, 0.05, "metal_dark", seg=8)
        else: self.p.box((x, y, 0.75), (sq, sq, 0.06), col); [self.p.box((x + sx * (sq / 2 - 0.06), y + sy * (sq / 2 - 0.06), 0.36), (0.06, 0.06, 0.72), col) for sx in [-1, 1] for sy in [-1, 1]]
        self.solids.append([round(x, 3), round(y, 3), round(sq + 0.9, 3), round(sq + 0.9, 3), 0.8])
        for k in range(chairs):
            a = k * math.pi * 2 / chairs + (math.pi / 4 if not round_ and chairs == 4 else 0); cx, cy = x + math.cos(a) * (sq / 2 + 0.35), y + math.sin(a) * (sq / 2 + 0.35)
            self.p.box((cx, cy, 0.45), (0.4, 0.4, 0.06), "wood_dark", rz=a); self.p.cyl((cx, cy, 0.22), 0.04, 0.44, "metal_dark", seg=5)
            self.p.box((cx + math.cos(a) * 0.18, cy + math.sin(a) * 0.18, 0.72), (0.06, 0.4, 0.5), "wood_dark", rz=a)
            self.pts["seats"].append([round(cx, 2), round(cy, 2)])
    def finish(self, kind, footprint_pad=0.6):
        ob = self.p.finish(); ob["footprint"] = [self.W + footprint_pad, self.D + footprint_pad]
        self.pts["kind"] = kind; self.pts["solids"] = self.solids; self.pts["size"] = [self.W, self.D, self.H]
        ob["meta"] = json.dumps(self.pts); return ob

def shop_market(name):
    s = Shell(name, 12, 10, 3.6, "wall_cream", "stone", "roof_green"); s.walls(door_x=1.5, windows=((-2.5, 2.4), (4.3, 1.4)), back_door=4.0); s.sign("white", "roof_green", 7)
    for y in [-1.4, 1.0]: s.shelf((-1.5, y), 5.0, 'x', goods=("red", "yellow", "green", "blue", "orange", "wall_white"))
    s.shelf((4.5, 0.5), 5.5, 'y', goods=("lblue", "white", "yellow", "red"))
    for i in range(4): s.solid((-4.0 + i * 1.3, 4.1, 1.0), (1.2, 0.8, 2.0), "white"); s.p.box((-4.0 + i * 1.3, 3.65, 1.1), (1.0, 0.05, 1.5), "glass")   # fridges
    s.counter((-3.5, -3.2), 3.0, 'x', col="roof_green", top="white"); s.pts["counter"] = [-3.5, -4.2]; s.pts["keeper"] = [-3.5, -2.3, 0]; s.pts["register"] = [-2.3, -3.2]
    s.p.box((4.6, -3.6, 0.5), (1.2, 1.2, 1.0), "wall_terracotta"); s.p.box((4.6, -3.6, 1.1), (1.2, 1.2, 0.2), "green")   # fruit stand
    s.pts["shelves"].append([4.6, -3.6, 1.2]); s.solids.append([4.6, -3.6, 1.3, 1.3, 1.2])
    return s.finish("market")

def shop_gun(name):
    s = Shell(name, 10, 8, 3.4, "wall_dark", "asphalt", "metal_dark"); s.walls(door_x=-2.5, windows=((2.5, 3.0),), win_h=1.2, win_z=1.6); s.sign("yellow", "black", 6)
    s.counter((0.5, -0.5), 6.0, 'x', col="wood_dark", top="wood_light", glass=True); s.pts["counter"] = [0.5, -1.6]; s.pts["keeper"] = [0.5, 0.6, 0]; s.pts["register"] = [3.0, -0.5]
    for i in range(5):   # wall racks with long guns
        x = -3.5 + i * 1.7; s.p.box((x, 3.75, 1.9), (1.3, 0.1, 1.6), "wood_dark")
        for k in range(3): s.p.box((x - 0.4 + k * 0.4, 3.65, 1.9), (0.08, 0.1, 1.2), "metal_dark", rx=0.0); s.p.box((x - 0.4 + k * 0.4, 3.65, 1.5), (0.1, 0.12, 0.35), "wood")
    s.shelf((-4.5, 1.2), 3.0, 'y', col="metal_dark", goods=("olive", "black", "metal", "yellow") if "olive" in PAL else ("green", "black", "metal", "yellow"))
    s.p.box((3.8, 2.8, 0.5), (1.0, 0.8, 1.0), "metal_dark"); s.p.box((3.8, 2.8, 1.05), (1.0, 0.8, 0.1), "metal")   # ammo crates
    s.pts["shelves"] += [[3.8, 2.8, 1.12], [1.5, -0.5, 1.12], [-1.5, -0.5, 1.12]]
    return s.finish("gun")

def shop_pawn(name):
    s = Shell(name, 10, 8, 3.4, "wall_terracotta", "wood_dark", "wood"); s.walls(door_x=2.5, windows=((-2.0, 3.5),), win_h=1.4); s.sign("yellow", "wood_dark", 6)
    s.counter((-0.5, 0.5), 5.0, 'x', col="wood_dark", top="wood_light", glass=True); s.pts["counter"] = [-0.5, -0.6]; s.pts["keeper"] = [-0.5, 1.6, 0]; s.pts["register"] = [1.5, 0.5]
    s.shelf((0, 3.6), 8.0, 'x', col="wood", goods=("wood_light", "silver", "red", "navy", "yellow", "metal", "white"), levels=3)
    s.shelf((-4.5, 0.5), 4.0, 'y', col="wood", goods=("silver", "wood_light", "black", "red"))
    s.p.box((4.2, 3.0, 0.9), (1.2, 0.6, 1.8), "metal_dark"); s.p.cyl((4.2, 2.68, 1.0), 0.15, 0.05, "metal", seg=8, axis='Y')   # safe
    s.solids.append([4.2, 3.0, 1.3, 0.7, 1.8]); s.pts["safe"] = [4.2, 2.2]
    return s.finish("pawn")

def shop_cafe(name):
    s = Shell(name, 10, 8, 3.4, "wall_peach", "wood_light", "roof_brown"); s.walls(door_x=0, windows=((-3.0, 2.8), (3.0, 2.8)), win_h=1.8, win_z=1.3); s.sign("white", "roof_brown", 5, awning="roof_brown")
    s.counter((-2.0, 3.0), 4.5, 'x', col="wood_dark", top="wood_light"); s.pts["counter"] = [-2.0, 1.9]; s.pts["keeper"] = [-2.0, 3.65, 0]; s.pts["register"] = [-0.2, 3.0]
    s.p.box((-3.3, 3.0, 1.35), (0.6, 0.5, 0.5), "metal"); s.p.box((-3.3, 3.0, 1.62), (0.5, 0.4, 0.1), "metal_dark")      # espresso machine
    s.p.box((2.8, 3.75, 2.2), (3.0, 0.08, 1.2), "black"); s.p.box((2.8, 3.7, 2.2), (2.6, 0.04, 0.3), "white")              # menu board
    s.p.box((3.2, 3.0, 0.5), (1.6, 0.7, 1.0), "white"); s.p.box((3.2, 2.63, 0.6), (1.4, 0.05, 0.7), "glass")             # pastry display
    s.solids.append([3.2, 3.0, 1.6, 0.8, 1.0])
    for c in [(-2.6, -1.0), (2.6, -1.0), (-2.4, -3.3), (2.6, -3.4)]: s.table(c, 0.8, "wood_light", chairs=2, round_=True)
    s.p.box((-4.3, -3.2, 0.3), (0.5, 0.5, 0.6), "wall_terracotta"); s.p.ico((-4.3, -3.2, 1.0), 0.45, "leaf1", sz=1.3)
    s.pts["shelves"].append([3.2, 3.0, 1.1])
    return s.finish("cafe")

def shop_restaurant(name):
    s = Shell(name, 14, 10, 3.8, "wall_rose", "wood", "roof_red"); s.walls(door_x=-1.0, windows=((-5.0, 3.0), (3.0, 3.0), (5.5, 1.5)), win_h=1.8, win_z=1.3, back_door=5.5); s.sign("white", "roof_red", 8, awning="roof_red")
    s.counter((2.5, 3.9), 8.0, 'x', col="wood_dark", top="wood_light", register=True); s.pts["counter"] = [-0.5, 2.8]; s.pts["keeper"] = [-0.5, 4.5, 0]; s.pts["register"] = [-1.0, 3.9]
    for i in range(3): s.p.box((1.5 + i * 2.0, 4.6, 0.9), (1.6, 0.6, 1.8), "metal")   # kitchen line
    s.p.box((5.5, 4.6, 1.3), (1.2, 0.5, 0.5), "metal_dark")
    for c in [(-4.5, 1.0), (-4.5, -2.6), (-1.0, 0.0), (2.5, 1.0), (2.5, -2.6), (5.5, -1.0)]: s.table(c, 1.0, "wood", chairs=4)
    s.p.box((-6.4, 3.0, 0.3), (0.6, 0.6, 0.6), "wall_terracotta"); s.p.ico((-6.4, 3.0, 1.1), 0.5, "leaf2", sz=1.4)
    s.p.box((6.4, -3.8, 0.3), (0.6, 0.6, 0.6), "wall_terracotta"); s.p.ico((6.4, -3.8, 1.1), 0.5, "leaf2", sz=1.4)
    s.pts["shelves"].append([3.5, 4.6, 1.85])
    return s.finish("restaurant")

def shop_pharmacy(name):
    s = Shell(name, 10, 8, 3.4, "wall_white", "wall_white", "green"); s.walls(door_x=2.0, windows=((-2.5, 3.5),), win_h=1.6); s.sign("white", "green", 5)
    s.p.box((0, -4.2, 3.0), (0.9, 0.3, 0.9), "green"); s.p.box((0, -4.38, 3.0), (0.7, 0.05, 0.22), "white"); s.p.box((0, -4.38, 3.0), (0.22, 0.05, 0.7), "white")   # cross
    s.counter((-1.5, 0.0), 5.0, 'x', col="white", top="green"); s.pts["counter"] = [-1.5, -1.1]; s.pts["keeper"] = [-1.5, 1.1, 0]; s.pts["register"] = [0.5, 0.0]
    s.shelf((0, 3.6), 8.5, 'x', col="white", goods=("white", "green", "lblue", "white", "red")); s.shelf((-4.5, 0.5), 4.0, 'y', col="white", goods=("white", "lblue", "green"))
    s.shelf((3.9, -2.5), 1.8, 'x', col="white", goods=("white", "yellow", "lblue"), levels=2)
    return s.finish("pharmacy")

def shop_jeweler(name):
    s = Shell(name, 8, 8, 3.4, "wall_dark", "wall_white", "yellow"); s.walls(door_x=0, windows=((-2.6, 1.6), (2.6, 1.6)), win_h=1.2, win_z=1.5); s.sign("yellow", "black", 5)
    s.counter((0, 2.2), 5.0, 'x', col="wood_dark", top="glass", glass=True, register=True); s.pts["counter"] = [0, 1.1]; s.pts["keeper"] = [0, 3.0, 0]; s.pts["register"] = [2.0, 2.2]
    for sx in [-1, 1]: s.counter((sx * 2.9, -0.6), 4.2, 'y', col="wood_dark", top="glass", glass=True, register=False)
    for (x, y) in [(-1.5, 2.2), (0.5, 2.2), (-2.9, -1.5), (2.9, 0.3), (2.9, -2.0)]: s.p.box((x, y, 1.16), (0.3, 0.3, 0.1), "red"); s.pts["shelves"].append([x, y, 1.22])
    s.p.box((0, 3.6, 1.0), (1.4, 0.7, 2.0), "metal_dark"); s.p.cyl((0, 3.22, 1.1), 0.2, 0.06, "metal", seg=8, axis='Y'); s.solids.append([0, 3.6, 1.5, 0.8, 2.0]); s.pts["safe"] = [0, 2.6]
    s.p.cyl((0, -1.5, 3.0), 0.5, 0.2, "yellow", seg=10)   # chandelier
    return s.finish("jeweler")

def bank(name):
    s = Shell(name, 16, 12, 4.4, "wall_grey", "stone", "navy"); s.walls(door_x=0, door_w=2.2, windows=((-5.0, 3.5), (5.0, 3.5)), win_h=2.2, win_z=1.6); s.sign("white", "navy", 9)
    for sx in [-1, 1]: s.p.box((sx * 2.0, -6.4, 2.0), (0.6, 0.6, 4.0), "wall_white")   # columns
    s.counter((0, 1.5), 11.0, 'x', col="wood_dark", top="wood_light", register=False); s.p.box((0, 1.5, 1.8), (11.0, 0.06, 1.4), "glass")
    for i in range(4): s.p.box((-4.2 + i * 2.8, 1.5, 1.8), (0.08, 0.1, 1.4), "metal")
    s.pts["counter"] = [-1.4, 0.4]; s.pts["keeper"] = [-1.4, 2.6, 0]; s.pts["keeper2"] = [1.4, 2.6, 0]; s.pts["register"] = [0, 1.5]
    for i in range(3): s.p.box((-4.2 + i * 2.8, 2.6, 0.72), (1.2, 0.6, 0.06), "wood"); s.p.box((-4.2 + i * 2.8, 2.7, 0.95), (0.4, 0.05, 0.3), "black")
    s.p.cyl((0, 5.7, 1.6), 1.3, 0.3, "metal", seg=16, axis='Y'); s.p.cyl((0, 5.5, 1.6), 1.0, 0.2, "metal_dark", seg=16, axis='Y'); s.p.cyl((0, 5.35, 1.6), 0.25, 0.1, "silver", seg=8, axis='Y')   # vault door
    s.p.box((0, 5.85, 1.6), (3.2, 0.3, 3.2), "concrete_dark"); s.solids.append([0, 5.6, 3.2, 0.8, 3.2]); s.pts["safe"] = [0, 4.6]
    for sx in [-1, 1]: s.solid((sx * 6.0, 3.5, 0.4), (1.6, 0.8, 0.75), "wood_dark"); s.p.box((sx * 6.0, 3.5, 0.95), (0.5, 0.05, 0.35), "black")   # desks
    for xx in [-5.6, -3.2, 3.2, 5.6]: s.p.box((xx, -3.5, 0.45), (1.6, 0.5, 0.08), "navy"); s.p.box((xx, -3.3, 0.75), (1.6, 0.06, 0.5), "navy"); s.solids.append([xx, -3.5, 1.7, 0.6, 0.8])
    for xx in [-5.5, -2.0, 2.0, 5.5]: s.p.cyl((xx, -1.6, 0.5), 0.04, 1.0, "metal", seg=5); s.p.cyl((xx, -1.6, 0.03), 0.18, 0.06, "metal", seg=8)
    s.p.box((-3.75, -1.6, 0.95), (3.5, 0.03, 0.03), "red"); s.p.box((3.75, -1.6, 0.95), (3.5, 0.03, 0.03), "red")
    s.p.box((-6.5, 0, 0.8), (0.6, 0.8, 1.6), "metal"); s.p.box((-6.5, -0.42, 1.1), (0.3, 0.05, 0.3), "black"); s.solids.append([-6.5, 0, 0.7, 0.9, 1.6]); s.pts["atm"] = [-6.5, -1.0]   # ATM inside
    return s.finish("bank")

def shop_barber(name):
    s = Shell(name, 8, 8, 3.2, "wall_blue", "wall_white", "white"); s.walls(door_x=-2.0, windows=((1.5, 3.0),), win_h=1.6); s.sign("navy", "white", 4.5)
    s.p.cyl((-3.6, -3.9, 2.2), 0.12, 0.8, "red", seg=8); s.p.cyl((-3.6, -3.9, 2.2), 0.13, 0.5, "white", seg=8)   # pole
    for i in range(3):
        y = -1.8 + i * 1.8; s.p.box((3.0, y, 0.5), (0.7, 0.7, 0.1), "black"); s.p.cyl((3.0, y, 0.25), 0.06, 0.5, "metal", seg=6); s.p.box((3.3, y, 0.95), (0.1, 0.7, 0.8), "black")
        s.p.box((3.85, y, 1.5), (0.06, 1.2, 1.2), "glass"); s.p.box((3.5, y, 0.95), (0.7, 1.2, 0.1), "wood_light"); s.solids.append([3.2, y, 1.4, 1.2, 1.0])
    s.counter((-2.5, 3.2), 2.5, 'x', col="wood", top="white"); s.pts["counter"] = [-2.5, 2.1]; s.pts["keeper"] = [-2.5, 3.75, 0]; s.pts["register"] = [-1.5, 3.2]
    for i in range(3): s.p.box((-3.45, -2.5 + i * 0.8, 0.45), (0.5, 0.6, 0.1), "navy"); s.p.box((-3.68, -2.5 + i * 0.8, 0.8), (0.06, 0.6, 0.6), "navy")
    s.solids.append([-3.45, -1.7, 0.6, 2.6, 0.9]); s.pts["shelves"].append([-2.5, 3.2, 1.12])
    return s.finish("barber")

def house_interior(name, wall, roof, flip=False):
    """One-storey house: living room + kitchen in front, bedroom at the back behind a partition wall. Width 11, depth 10."""
    s = Shell(name, 11, 10, 3.3, wall, "wood_light", "trim"); f = -1 if flip else 1
    s.walls(door_x=f * 2.5, door_w=1.9, windows=((-f * 2.5, 2.2), (f * 5.0 * 0 + f * -0.5 * 0 + f * 4.6, 0.9)), win_h=1.4, win_z=1.4)
    s.p.wedge((0, 0, 3.55), (11.6, 10.6, 2.2), roof, rz=math.pi / 2)   # pitched roof replaces the parapet look (parapet stays as eave)
    # partition: bedroom at back (y > 1.0), opening at x = -f*1.5
    s.solid((f * 2.2, 1.0, 1.6), (6.6, 0.2, 3.2), wall); s.solid((-f * 4.4, 1.0, 1.6), (2.2, 0.2, 3.2), wall)
    # living room (front, side f): sofa, coffee table, TV
    s.solid((f * 4.9, -2.8, 0.35), (0.9, 2.4, 0.7), "navy"); s.p.box((f * 5.25, -2.8, 0.8), (0.3, 2.4, 0.6), "navy"); s.p.box((f * 4.85, -2.8, 0.75), (0.7, 2.2, 0.15), "lblue")
    s.p.box((f * 3.7, -2.8, 0.4), (0.6, 1.0, 0.05), "wood"); s.p.box((f * 3.7, -2.8, 0.2), (0.1, 0.1, 0.4), "wood"); s.solids.append([f * 3.7, -2.8, 0.7, 1.1, 0.45])
    s.p.box((f * 3.2, 0.5, 0.3), (1.6, 0.4, 0.6), "wood_dark"); s.p.box((f * 3.2, 0.5, 1.15), (1.5, 0.08, 0.9), "black"); s.p.box((f * 3.2, 0.44, 1.15), (1.35, 0.03, 0.75), "lblue"); s.solids.append([f * 3.2, 0.5, 1.6, 0.5, 1.6])
    s.pts["shelves"] += [[f * 3.7, -2.8, 0.44], [f * 3.2, 0.5, 1.62]]
    s.p.box((f * 5.2, -0.2, 1.0), (0.3, 1.6, 2.0), "wood"); [s.p.box((f * 5.2, -0.2, 0.3 + k * 0.5), (0.26, 1.5, 0.04), "wood_light") for k in range(4)]; s.solids.append([f * 5.2, -0.2, 0.4, 1.7, 2.0])
    for k in range(8): s.p.box((f * 5.2, -0.8 + k * 0.18, 0.5 + (k % 4) * 0.5), (0.2, 0.12, 0.25), ["red", "blue", "green", "yellow", "navy"][k % 5])
    # kitchen (front, side -f)
    s.solid((-f * 3.6, -4.1, 0.45), (3.6, 0.6, 0.9), "wall_white"); s.p.box((-f * 3.6, -4.1, 0.93), (3.7, 0.7, 0.06), "stone"); s.p.box((-f * 2.4, -4.1, 0.96), (0.5, 0.4, 0.06), "metal")
    s.solid((-f * 5.0, -2.6, 0.95), (0.8, 0.8, 1.9), "white"); s.p.box((-f * 4.58, -2.6, 0.95), (0.03, 0.02, 1.6), "metal")   # fridge
    s.table((-f * 2.8, -1.8), 0.9, "wood_light", chairs=4); s.pts["shelves"] += [[-f * 3.6, -4.1, 1.0], [-f * 2.8, -1.8, 0.8]]
    # bedroom (back)
    s.solid((f * 2.5, 3.3, 0.3), (1.8, 2.2, 0.6), "wood"); s.p.box((f * 2.5, 3.6, 0.7), (1.7, 1.5, 0.2), "white"); s.p.box((f * 2.5, 2.6, 0.7), (1.7, 0.5, 0.2), "roof_red"); s.p.box((f * 2.5, 4.3, 0.9), (1.8, 0.2, 0.6), "wood")
    s.pts["bed"] = [f * 2.5, 3.3]; s.p.box((f * 0.6, 4.2, 0.3), (0.5, 0.5, 0.6), "wood_dark"); s.p.box((f * 0.6, 4.2, 0.75), (0.2, 0.2, 0.3), "yellow"); s.solids.append([f * 0.6, 4.2, 0.6, 0.6, 0.6])
    s.solid((-f * 3.6, 4.2, 1.1), (2.4, 0.6, 2.2), "wood_dark"); s.p.box((-f * 3.6, 3.88, 1.1), (0.04, 0.04, 1.8), "metal")   # wardrobe
    s.pts["shelves"] += [[f * 0.6, 4.2, 0.9], [-f * 3.6, 4.2, 2.25]]
    s.p.box((-f * 2.5, 3.0, 0.01), (2.0, 1.4, 0.03), "roof_red")   # rug
    s.pts["owner"] = [f * 2.6, -1.4, 0]
    return s.finish("house")

def ruin_house(name):
    """Derelict one-storey house (the player's home): crumbling walls, boarded windows, a holed plank roof, sagging porch, junk everywhere. Enterable; door on the front (-Y)."""
    W, D, H = 11.0, 9.0, 3.1; s = Shell(name, W, D, H, "wall_faded", "dirt", "plank_old"); rng = random.Random(77); T = s.T; hw, hd = W / 2, D / 2
    p = s.p
    # cracked floor: patches of concrete + a few loose planks
    for k in range(6): p.box((rng.uniform(-4, 4), rng.uniform(-3, 3), 0.145), (rng.uniform(0.8, 2.0), rng.uniform(0.6, 1.5), 0.02), rng.choice(["concrete_dark", "stone", "plank_old"]))
    def wall_pieces(axis, pos, length, z0, holes, col):
        """A wall along `axis` ('x' or 'y') at offset pos, made of column pieces with some missing chunks (holes list: (u0,u1,zlo,zhi)). Collider = full wall."""
        n = 11; seg = length / n
        for i in range(n):
            u0 = -length / 2 + i * seg; u1 = u0 + seg; um = (u0 + u1) / 2
            # vertical span pieces avoiding holes
            spans = [(0.0, H)]
            for (h0, h1, zl, zh) in holes:
                if u1 <= h0 or u0 >= h1: continue
                ns = []
                for (a, b) in spans:
                    if zh <= a or zl >= b: ns.append((a, b)); continue
                    if zl > a: ns.append((a, zl))
                    if zh < b: ns.append((zh, b))
                spans = ns
            top_crumble = rng.uniform(0, 0.5) if rng.random() < 0.35 else 0.0
            for (a, b) in spans:
                b2 = b - top_crumble if b >= H - 0.01 else b
                if b2 - a < 0.05: continue
                c = col if rng.random() > 0.15 else "wall_brick"
                if axis == 'x': p.box((um, pos, (a + b2) / 2), (seg + 0.01, T, b2 - a), c)
                else: p.box((pos, um, (a + b2) / 2), (T, seg + 0.01, b2 - a), c)
        if axis == 'x': s.solids.append([0.0, round(pos, 3), round(length, 3), T, H])
        else: s.solids.append([round(pos, 3), 0.0, T, round(length, 3), H])
    # back wall: big hole top-left, crumbled top
    wall_pieces('x', hd - T / 2, W, 0, [(-4.5, -2.8, 1.6, 3.2)], "wall_faded")
    # side walls
    wall_pieces('y', -(hw - T / 2), D, 0, [(1.0, 2.3, 0.0, 1.1)], "wall_faded")       # west: hole at the bottom (crawl gap, blocked by collider anyway)
    wall_pieces('y', hw - T / 2, D, 0, [(-1.6, 0.2, 1.2, 2.5)], "wall_faded")           # east: window-size hole
    # front wall: door at x=2.0 (w 1.9), boarded window at x=-2.6
    door_x, door_w = 2.0, 1.9; y = -hd + T / 2
    dl, dr = door_x - door_w / 2, door_x + door_w / 2
    s.solid(((-hw + dl) / 2, y, H / 2), (dl + hw, T, H), None)           # colliders
    s.solid(((dr + hw) / 2, y, H / 2), (hw - dr, T, H), None)
    # visual pieces of the front wall
    for (x0, x1) in [(-hw, dl), (dr, hw)]:
        n = max(2, int((x1 - x0) / 1.0)); seg = (x1 - x0) / n
        for i in range(n):
            um = x0 + seg * (i + 0.5); crum = rng.uniform(0, 0.45) if rng.random() < 0.3 else 0
            col = "wall_faded" if rng.random() > 0.15 else "wall_brick"
            if abs(um - (-2.6)) < 1.0: p.box((um, y, 0.5), (seg + 0.01, T, 1.0), col); p.box((um, y, (2.4 + H - crum) / 2), (seg + 0.01, T, H - crum - 2.4), col)   # window gap 1.0..2.4
            else: p.box((um, y, (H - crum) / 2), (seg + 0.01, T, H - crum), col)
    p.box((door_x, y, (2.3 + H) / 2), (door_w + 0.1, T, H - 2.3), "wall_faded")      # lintel
    # door frame + door hanging ajar on one hinge
    for sx in [-1, 1]: p.box((door_x + sx * (door_w / 2 + 0.06), -hd - 0.03, 1.15), (0.12, 0.1, 2.3), "plank_old")
    p.box((door_x - door_w / 2 + 0.45, -hd - 0.25, 1.05), (0.9, 0.06, 2.1), "wood_dark", rz=0.55, ry=0.06)
    # boarded window (front) and planks over the east hole
    p.box((-2.6, -hd - 0.02, 1.7), (1.5, 0.06, 1.4), "glass_dark")
    for k, (dz, ang) in enumerate([(-0.35, 0.18), (0.15, -0.12), (0.55, 0.25)]): p.box((-2.6, -hd - 0.08, 1.7 + dz), (1.9, 0.08, 0.22), "plank_old", rx=0, ry=0, rz=0) if False else p.box((-2.6, -hd - 0.08, 1.7 + dz), (1.9, 0.08, 0.22), "plank_old", rx=ang)
    for dz, ang in [(-0.3, 0.2), (0.3, -0.25)]: p.box((hw + 0.08, -0.7, 1.85 + dz), (0.08, 2.1, 0.2), "plank_old", rx=ang)
    # roof: rafters + planks with holes
    pitch = 1.9; ang = math.atan2(pitch, hd + 0.3)
    p.box((0, 0, H + pitch + 0.05), (W + 0.8, 0.16, 0.16), "wood_dark")   # ridge beam
    for i in range(9):
        x = -hw - 0.3 + i * (W + 0.6) / 8
        for sy in [-1, 1]: p.box((x, sy * (hd + 0.3) / 2, H + pitch / 2 - 0.05), (0.12, math.hypot(hd + 0.3, pitch), 0.12), "wood_dark", rx=-sy * ang)
    nplank = 15; pw = (W + 0.8) / nplank
    missing = {(3, 1), (4, 1), (9, -1), (10, -1), (11, -1), (6, 1)}
    for i in range(nplank):
        x = -hw - 0.4 + pw * (i + 0.5)
        for sy in [-1, 1]:
            if (i, sy) in missing: continue
            L = math.hypot(hd + 0.3, pitch) * (0.75 if (i, sy) in {(2, 1), (12, -1)} else 1.0)
            col = rng.choice(["roof_brown", "roof_brown", "plank_old", "rust"])
            p.box((x, sy * (hd + 0.3) / 2, H + pitch / 2 + 0.04), (pw - 0.04, L, 0.06), col, rx=-sy * ang)
    # crumbling chimney
    p.box((hw - 1.4, 1.5, H + 1.6), (0.7, 0.7, 1.6), "wall_brick"); p.box((hw - 1.4, 1.5, H + 2.45), (0.5, 0.6, 0.3), "wall_brick")
    # sagging porch: deck, 3 posts (one leaning), tilted roof
    p.box((0.5, -hd - 1.3, 0.08), (7.0, 2.2, 0.1), "plank_old"); s.solids.append([0.5, -hd - 1.3, 7.0, 2.2, 0.3]) if False else None
    for (px, lean) in [(-2.6, 0.0), (0.4, 0.12), (3.6, -0.05)]: p.box((px, -hd - 2.3, 1.25), (0.16, 0.16, 2.5), "plank_old", ry=lean); s.solids.append([px, -hd - 2.3, 0.3, 0.3, 2.5])
    p.box((0.5, -hd - 1.35, 2.55), (7.4, 2.6, 0.08), "rust", rx=0.1, ry=0.04)
    p.box((0.5, -hd - 1.35, 2.4), (7.4, 0.12, 0.12), "wood_dark", ry=0.04)
    # graffiti on the east wall
    for k, (yy, zz, col) in enumerate([(-1.2, 1.4, "red"), (-0.4, 1.0, "yellow"), (0.6, 1.5, "blue"), (1.8, 1.1, "white")]): p.box((hw + 0.01, yy + 2.0, zz), (0.02, 0.6 + k * 0.1, 0.35), col, rx=0.2 * (k % 2))
    # interior: stained mattress, broken table + stool, torn sofa, rusty barrel, buckets, bottles, a crate used as a shelf, hanging bulb
    p.box((-3.2, 2.6, 0.2), (1.9, 1.1, 0.25), "mattress"); p.box((-3.9, 2.6, 0.38), (0.5, 0.9, 0.12), "stone"); s.solids.append([-3.2, 2.6, 1.9, 1.1, 0.35]); s.pts["bed"] = [-3.2, 2.6]
    p.box((2.5, 1.0, 0.72), (1.2, 0.8, 0.05), "plank_old", ry=0.08); p.box((2.0, 0.7, 0.35), (0.08, 0.08, 0.7), "plank_old"); p.box((3.0, 1.3, 0.35), (0.08, 0.08, 0.7), "plank_old"); p.box((3.0, 0.7, 0.3), (0.08, 0.08, 0.6), "plank_old", rx=0.3)
    s.solids.append([2.5, 1.0, 1.4, 1.0, 0.8]); s.pts["shelves"] += [[2.5, 1.0, 0.78]]
    p.box((3.4, -0.3, 0.22), (0.4, 0.4, 0.45), "wood_dark"); s.solids.append([3.4, -0.3, 0.5, 0.5, 0.45])
    s.solid((-4.4, -1.6, 0.35), (0.9, 2.2, 0.7), "navy"); p.box((-4.8, -1.6, 0.85), (0.25, 2.2, 0.5), "navy"); p.box((-4.3, -1.0, 0.75), (0.6, 0.5, 0.12), "mattress"); p.box((-4.3, -2.2, 0.76), (0.6, 0.5, 0.12), "glass_dark")
    p.cyl((4.2, 3.2, 0.45), 0.32, 0.9, "rust", seg=8); s.solids.append([4.2, 3.2, 0.7, 0.7, 0.9]); s.pts["shelves"] += [[4.2, 3.2, 0.92]]
    p.cyl((-0.6, 3.6, 0.17), 0.18, 0.34, "metal", seg=7); p.cyl((0.1, 3.5, 0.15), 0.16, 0.3, "blue", seg=7)
    for k in range(6): p.cyl((rng.uniform(-2, 2), rng.uniform(-3.5, -1.5), 0.12), 0.05, 0.24, rng.choice(["green", "glass_dark", "wall_brick"]), seg=5)
    p.box((0.8, 3.7, 0.3), (0.9, 0.6, 0.6), "wood"); p.box((0.8, 3.7, 0.65), (0.8, 0.5, 0.08), "plank_old"); s.solids.append([0.8, 3.7, 1.0, 0.7, 0.7]); s.pts["shelves"] += [[0.8, 3.7, 0.72]]
    p.box((1.2, -2.0, 0.08), (2.2, 1.4, 0.03), "roof_red"); p.box((1.4, -1.9, 0.09), (0.8, 0.5, 0.03), "dirt")      # worn rug
    p.cyl((0.0, 0.0, H - 0.35), 0.01, 0.7, "black", seg=3); p.ico((0.0, 0.0, H - 0.75), 0.09, "light", sub=0)
    # debris around the house + weeds
    for k in range(10): p.box((rng.uniform(-6.5, 6.5), rng.uniform(-6.6, -4.9) if k < 6 else rng.uniform(4.8, 5.6), 0.12), (rng.uniform(0.3, 0.8), rng.uniform(0.3, 0.6), rng.uniform(0.1, 0.35)), rng.choice(["plank_old", "wall_brick", "rust", "concrete_dark"]), rz=rng.uniform(0, 3))
    for k in range(14): x, yy = rng.uniform(-6.5, 6.5), rng.choice([rng.uniform(-6.8, -5.2), rng.uniform(4.8, 5.8)]); p.box((x, yy, 0.17), (0.18, 0.18, 0.34), "moss"); p.box((x + 0.1, yy + 0.08, 0.12), (0.22, 0.1, 0.24), "grass", rz=0.7)
    for k in range(3): p.cyl((-hw - 0.9, 2.0 + k * 0.0, 0.14 + k * 0.26), 0.36, 0.26, "tyre", seg=9)
    s.solids.append([-hw - 0.9, 2.0, 0.9, 0.9, 1.0])
    p.box((-hw - 0.05, -3.0, 1.0), (0.2, 1.2, 2.0), "metal", rz=0.0, ry=0.2)     # old door leaning on the wall
    s.pts["door"] = [door_x, -hd]; s.pts["porch"] = [0.5, -hd - 1.3]
    return s.finish("home", footprint_pad=1.0)

def police_station(name):
    s = Shell(name, 14, 12, 4.0, "wall_white", "stone", "navy"); s.walls(door_x=0, door_w=2.2, windows=((-4.5, 3.0), (4.5, 3.0)), win_h=1.8, win_z=1.5); s.sign("white", "navy", 8)
    s.p.box((-5.5, -6.5, 2.5), (0.2, 0.2, 5.0), "metal"); s.p.box((-5.1, -6.5, 4.6), (0.7, 0.04, 0.45), "red")   # flag
    s.counter((-3.2, -2.5), 4.6, 'x', col="navy", top="wood_light"); s.pts["counter"] = [-3.2, -3.6]; s.pts["keeper"] = [-3.2, -1.4, 0]; s.pts["register"] = [-1.3, -2.5]
    for i in range(3): s.solid((-4.0 + i * 3.2, 1.0, 0.4), (1.6, 0.8, 0.75), "wood_dark"); s.p.box((-4.0 + i * 3.2, 1.1, 0.95), (0.45, 0.05, 0.35), "black"); s.p.box((-4.0 + i * 3.2, 0.1, 0.5), (0.45, 0.45, 0.06), "black")
    # two cells at the back right
    for k in range(2):
        x0 = 2.3 + k * 2.4
        s.solid((x0 + 1.2, 4.0, 1.6), (2.4, 0.2, 3.2), None); s.p.box((x0 + 1.2, 4.0, 3.1), (2.4, 0.08, 0.1), "metal_dark")
        for j in range(9): s.p.box((x0 + 0.1 + j * 0.28, 4.0, 1.5), (0.05, 0.05, 3.0), "metal_dark")
        s.p.box((x0 + 0.0, 5.0, 1.6), (0.08, 2.0, 3.2), "metal_dark"); s.solids.append([x0, 5.0, 0.2, 2.0, 3.2])
        s.p.box((x0 + 1.2, 5.3, 0.3), (1.6, 0.8, 0.5), "wood"); s.p.box((x0 + 1.2, 5.3, 0.6), (1.5, 0.7, 0.12), "white")
    s.p.box((7.0 - 0.1, 5.0, 1.6), (0.08, 2.0, 3.2), "metal_dark")
    s.solid((-5.5, 4.5, 1.0), (1.6, 0.8, 2.0), "metal"); s.pts["shelves"].append([-5.5, 4.5, 2.05])   # evidence locker
    s.pts["spawn"] = [0, -5.0]
    return s.finish("police")

def item(name, kind):
    p = P(name)
    if kind == "phone": p.box((0, 0, 0.01), (0.08, 0.16, 0.02), "black"); p.box((0, 0, 0.022), (0.07, 0.14, 0.004), "lblue")
    elif kind == "laptop": p.box((0, 0, 0.012), (0.34, 0.24, 0.024), "silver"); p.box((0, 0.12, 0.13), (0.34, 0.02, 0.23), "silver"); p.box((0, 0.108, 0.13), (0.31, 0.005, 0.2), "lblue")
    elif kind == "watch": p.cyl((0, 0, 0.03), 0.045, 0.02, "metal", seg=10); p.cyl((0, 0, 0.042), 0.035, 0.005, "white", seg=10); p.box((0, 0, 0.02), (0.02, 0.2, 0.012), "black")
    elif kind == "cash": [p.box((0, 0, 0.01 + i * 0.02), (0.16, 0.07, 0.02), "green" if i % 2 else "leaf2") for i in range(3)]; p.box((0, 0, 0.035), (0.03, 0.075, 0.07), "wood_light")
    elif kind == "cola": p.cyl((0, 0, 0.065), 0.033, 0.13, "red", seg=8); p.cyl((0, 0, 0.133), 0.03, 0.006, "silver", seg=8)
    elif kind == "bread": p.ico((0, 0, 0.06), 0.09, "wall_sand", sz=0.7); p.box((0, 0, 0.05), (0.26, 0.12, 0.09), "wall_sand")
    elif kind == "can": p.cyl((0, 0, 0.05), 0.04, 0.1, "silver", seg=8); p.cyl((0, 0, 0.05), 0.041, 0.06, "red", seg=8)
    elif kind == "medkit": p.box((0, 0, 0.06), (0.22, 0.15, 0.12), "white"); p.box((0, -0.076, 0.07), (0.1, 0.004, 0.03), "red"); p.box((0, -0.076, 0.07), (0.03, 0.004, 0.1), "red")
    elif kind == "jewel": p.box((0, 0, 0.04), (0.14, 0.1, 0.08), "roof_red"); p.ico((0, 0, 0.1), 0.03, "yellow", sz=1.0); p.box((0, 0, 0.082), (0.14, 0.1, 0.004), "yellow")
    elif kind == "gold": p.box((0, 0, 0.03), (0.18, 0.08, 0.06), "yellow"); p.box((0, 0, 0.062), (0.15, 0.06, 0.004), "wall_yellow")
    elif kind == "wine": p.cyl((0, 0, 0.1), 0.035, 0.2, "roof_green", seg=8); p.cyl((0, 0, 0.26), 0.015, 0.12, "roof_green", seg=6); p.box((0, -0.036, 0.1), (0.05, 0.004, 0.07), "wall_cream")
    elif kind == "headphones": p.cyl((0, 0, 0.09), 0.08, 0.03, "black", seg=10, axis='Y', r2=0.08); p.box((0, 0, 0.09), (0.16, 0.03, 0.02), "black")
    elif kind == "tv": p.box((0, 0, 0.3), (0.9, 0.05, 0.5), "black"); p.box((0, -0.03, 0.3), (0.84, 0.01, 0.44), "lblue"); p.box((0, 0, 0.03), (0.3, 0.2, 0.06), "black")
    elif kind == "painting": p.box((0, 0, 0.25), (0.6, 0.04, 0.45), "wood_dark"); p.box((0, -0.022, 0.25), (0.52, 0.01, 0.37), "lblue"); p.box((0, -0.028, 0.18), (0.5, 0.005, 0.12), "leaf2"); p.cone((0.1, -0.03, 0.2), 0.08, 0.15, "yellow")
    elif kind == "ammo": p.box((0, 0, 0.04), (0.14, 0.1, 0.08), "roof_green"); p.box((0, -0.051, 0.04), (0.08, 0.004, 0.03), "yellow")
    elif kind == "drugs": p.box((0, 0, 0.03), (0.2, 0.14, 0.06), "wall_cream"); p.box((0, 0, 0.062), (0.2, 0.03, 0.004), "trim_dark")
    elif kind == "radio": p.box((0, 0, 0.06), (0.06, 0.03, 0.12), "black"); p.cyl((0.015, 0, 0.18), 0.006, 0.12, "black", seg=5)
    elif kind == "cuffs": p.cyl((-0.035, 0, 0.01), 0.035, 0.012, "metal", seg=10); p.cyl((0.035, 0, 0.01), 0.035, 0.012, "metal", seg=10)
    elif kind == "cigs": p.box((0, 0, 0.045), (0.06, 0.025, 0.09), "white"); p.box((0, 0, 0.07), (0.061, 0.026, 0.03), "red")
    elif kind == "keys": p.cyl((0, 0, 0.005), 0.02, 0.01, "metal", seg=8); p.box((0.04, 0, 0.005), (0.06, 0.012, 0.008), "metal"); p.box((0.06, 0.01, 0.005), (0.02, 0.012, 0.008), "metal")
    elif kind == "burger": p.cyl((0, 0, 0.02), 0.06, 0.04, "wall_sand", seg=9); p.cyl((0, 0, 0.05), 0.062, 0.02, "wood_dark", seg=9); p.cyl((0, 0, 0.065), 0.064, 0.01, "green", seg=9); p.ico((0, 0, 0.08), 0.06, "wall_sand", sz=0.55)
    elif kind == "coffee": p.cyl((0, 0, 0.045), 0.035, 0.09, "white", seg=8); p.cyl((0, 0, 0.088), 0.03, 0.006, "wood_dark", seg=8); p.box((0.045, 0, 0.045), (0.02, 0.01, 0.05), "white")
    ob = p.finish(); ob["footprint"] = [0.3, 0.3]; return ob

# ============================================================== build everything
rng = random.Random(42)
props = []
for i in range(20): props.append(villa(i, rng))
for i in range(8): props.append(apartment(i, rng))
cars = [("Araba_Sedan_Kirmizi", "sedan", "car_red"), ("Araba_Sedan_Mavi", "sedan", "car_blue"), ("Araba_Hatchback_Beyaz", "hatch", "car_white"), ("Araba_Hatchback_Yesil", "hatch", "car_green"),
        ("Araba_SUV_Siyah", "suv", "car_black"), ("Araba_SUV_Gri", "suv", "car_grey"), ("Araba_Pickup_Turuncu", "pickup", "orange"), ("Araba_Taksi", "taxi", "car_yellow"),
        ("Arac_Polis", "police", "car_white"), ("Arac_Ambulans", "ambulance", "white"), ("Arac_Van_Teal", "van", "car_teal"), ("Arac_Otobus", "bus", "car_blue")]
for n, k, c in cars: props.append(car(n, k, c))
props.append(motorcycle("Motosiklet_Kirmizi", "car_red")); props.append(motorcycle("Motosiklet_Siyah", "car_black")); props.append(bicycle("Bisiklet", "car_blue"))
props += [tree_pine("Agac_Cam_Buyuk", 1.3), tree_pine("Agac_Cam_Kucuk", 0.8, ("leaf3", "leaf1")), tree_round("Agac_Yuvarlak", 1.0), tree_round("Agac_Yuvarlak_Sonbahar", 0.9, "leaf_autumn"), tree_palm("Agac_Palmiye"),
          bush("Cali", 1.0), bush("Cali_Kucuk", 0.6, "leaf1"), hedge("Cit_Cali_2m"), fence_wood("Cit_Ahsap_2m"), fence_metal("Cit_Metal_2m"), wall_low("Duvar_Alcak_2m"),
          atm("ATM"), bench("Bank"), lamp_post("Sokak_Lambasi"), trash_bin("Cop_Kutusu"), dumpster("Cop_Konteyneri"), bus_stop("Otobus_Duragi"), hydrant("Yangin_Musluk"),
          traffic_light("Trafik_Isigi"), sign("Tabela_Dur", "stop"), sign("Tabela_Park", "parking"), sign("Tabela_Yon", "dir"), mailbox("Posta_Kutusu"), planter("Saksi"), planter("Saksi_Buyuk", True),
          kiosk("Bufe"), picnic_table("Piknik_Masasi"), phone_box("Telefon_Kulubesi"), fountain("Cesme"), gazebo("Kamelya"), wall_ad("Reklam_Panosu"), playground("Oyun_Parki"),
          tent("Cadir_Mavi", "wall_blue"), tent("Cadir_Yesil", "roof_green", 2.2, 1.8, 1.3), tent("Cadir_Turuncu", "orange", 2.8, 2.2, 1.6), tarp_shelter("Branda_Baraka"), fire_barrel("Ates_Varili"),
          pallet("Palet"), crate_stack("Kasa_Yigini"), shopping_cart("Market_Arabasi"), junk_pile("Hurda_Yigini"), clothes_line("Camasir_Ipi"), graffiti_wall("Grafiti_Duvari"), barrier("Beton_Bariyer"),
          construction_building("Insaat_Bina"), scaffold("Iskele"), tower_crane("Vinc_Kule"), cement_mixer("Beton_Mikseri"), brick_pallet("Tugla_Paleti"), brick_pallet("Briket_Paleti", "wall_grey"), heap("Kum_Yigini", "wall_sand"), heap("Cakil_Yigini", "concrete_dark", 1.3, 0.7), site_container("Santiye_Konteyneri"), site_container("Santiye_Konteyneri_Beyaz", "white"), warehouse("Depo_Gri"), warehouse("Depo_Mavi", "wall_blue", "roof_grey", "red"), tyre_pile("Lastik_Yigini"), rebar_bundle("Demir_Destesi"), site_fence("Santiye_Citi"),
          shop_market("Dukkan_Market"), shop_gun("Dukkan_Silah"), shop_pawn("Dukkan_Rehinci"), shop_cafe("Dukkan_Kafe"), shop_restaurant("Dukkan_Restoran"), shop_pharmacy("Dukkan_Eczane"), shop_jeweler("Dukkan_Kuyumcu"), bank("Bina_Banka"), shop_barber("Dukkan_Berber"), police_station("Bina_Karakol"),
          house_interior("Ev_Ic_Krem", "wall_cream", "roof_red"), house_interior("Ev_Ic_Mavi", "wall_blue", "roof_grey", True), house_interior("Ev_Ic_Yesil", "wall_green", "roof_brown"), house_interior("Ev_Ic_Sari", "wall_yellow", "roof_dark", True), ruin_house("Ev_Harabe")]
for _k, _n in [("phone","Esya_Telefon"),("laptop","Esya_Laptop"),("watch","Esya_Saat"),("cash","Esya_Para"),("cola","Esya_Kola"),("bread","Esya_Ekmek"),("can","Esya_Konserve"),("medkit","Esya_Ilac"),("jewel","Esya_Mucevher"),("gold","Esya_Altin"),("wine","Esya_Sarap"),("headphones","Esya_Kulaklik"),("tv","Esya_TV"),("painting","Esya_Tablo"),("ammo","Esya_Mermi"),("drugs","Esya_Paket"),("radio","Esya_Telsiz"),("cuffs","Esya_Kelepce"),("cigs","Esya_Sigara"),("keys","Esya_Anahtar"),("burger","Esya_Burger"),("coffee","Esya_Kahve")]: props.append(item(_n, _k))

meta = {}
for ob in props:
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
    for ch in ob.children: ch.select_set(True)
    bpy.ops.export_scene.gltf(filepath=f"{OUT}/{ob.name}.glb", export_format='GLB', use_selection=True, export_apply=True, export_animations=False, export_yup=True)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons) + sum(sum(len(p.vertices) - 2 for p in ch.data.polygons) for ch in ob.children)
    meta[ob.name] = {"footprint": list(ob["footprint"]), "tris": tris, "wheels": len(ob.children)}
    if "meta" in ob:
        m = json.loads(ob["meta"]); cv = {}
        for k, v in m.items():
            if k == "solids": cv[k] = [[b[0], -b[1], b[2], b[3], b[4]] for b in v]
            elif k == "shelves": cv[k] = [[q[0], q[2], -q[1]] for q in v]          # x, y(up), z
            elif k == "seats": cv[k] = [[q[0], -q[1]] for q in v]
            elif isinstance(v, list) and len(v) >= 2 and isinstance(v[0], (int, float)): cv[k] = [v[0], -v[1]] + list(v[2:])
            else: cv[k] = v
        meta[ob.name]["meta"] = cv
    ob.hide_render = True
    for ch in ob.children: ch.hide_render = True
json.dump(meta, open(f"{OUT}/props.json", "w"), indent=1)
print("EXPORTED", len(props))

# ============================================================== renders (contact cards)
if not os.environ.get('NO_RENDER'):
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 20; scene.cycles.device = 'CPU'; scene.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.new("W"); scene.world = world; world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (0.93, 0.93, 0.95, 1)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sun.data.energy = 2.0; sun.data.angle = math.radians(20); sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(35)); scene.collection.objects.link(sun); scene.view_settings.exposure = -0.5
    bpy.ops.mesh.primitive_plane_add(size=200); gm = bpy.data.materials.new("G"); gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.86, 0.86, 0.88, 1); bpy.context.object.data.materials.append(gm)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam); scene.camera = cam; cam.data.lens = 45
    scene.render.resolution_x = 640; scene.render.resolution_y = 520
    # reference character silhouette for scale (simple 1.9 m blocky figure)
    ref = P("RefMan"); ref.box((0, 0, 0.5), (0.3, 0.2, 1.0), "navy"); ref.box((0, 0, 1.35), (0.4, 0.24, 0.7), "lblue"); ref.box((0, 0, 1.85), (0.26, 0.26, 0.3), (0.87, 0.62, 0.47)); refob = ref.finish(); refob.hide_render = True
    for ob in props:
        ob.hide_render = False
        for ch in ob.children: ch.hide_render = False
        fp = ob["footprint"]; ext = max(fp[0], fp[1], max(v.co.z for v in ob.data.vertices) * 1.2)
        refob.hide_render = not (ob.name.startswith(("Villa", "Apartman", "Arac", "Araba", "Oto", "Agac")))
        refob.location = (fp[0] / 2 + 1.0, -fp[1] / 2 - 1.0, 0)
        d = ext * 1.5 + 2.5; cam.location = (d * 0.75, -d * 0.85, d * 0.55 + 0.5)
        look = Vector((0, 0, min(ext * 0.3, 6))); dirv = look - cam.location; cam.rotation_euler = dirv.to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f"{OUT}/card_{ob.name}.png"; bpy.ops.render.render(write_still=True)
        ob.hide_render = True
        for ch in ob.children: ch.hide_render = True
    print("RENDERED")
print("DONE")
