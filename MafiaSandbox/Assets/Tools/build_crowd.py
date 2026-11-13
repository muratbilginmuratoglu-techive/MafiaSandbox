import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

OUT = "/home/claude/out/crowd"
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30

# ------------------------------------------------------------------ palette
PAL = {
    "skin_light": (0.90, 0.70, 0.55), "skin_tan": (0.80, 0.56, 0.40), "skin_med": (0.62, 0.40, 0.26),
    "skin_dark": (0.36, 0.22, 0.14), "skin_pale": (0.95, 0.80, 0.68),
    "navy": (0.10, 0.14, 0.30), "navy2": (0.07, 0.09, 0.20), "black": (0.05, 0.05, 0.06),
    "gold": (0.95, 0.75, 0.15), "white": (0.93, 0.93, 0.93), "eye": (0.05, 0.05, 0.05),
    "lblue": (0.55, 0.68, 0.85), "red": (0.80, 0.12, 0.10), "fire_red": (0.72, 0.10, 0.08),
    "fire_yellow": (0.98, 0.85, 0.10), "hivis": (0.95, 0.95, 0.20), "scrubs": (0.20, 0.55, 0.60),
    "grey": (0.45, 0.46, 0.50), "dgrey": (0.25, 0.26, 0.28), "lgrey": (0.75, 0.76, 0.78),
    "brown": (0.40, 0.25, 0.14), "tan": (0.76, 0.65, 0.48), "denim": (0.25, 0.35, 0.55),
    "green": (0.25, 0.50, 0.30), "olive": (0.40, 0.42, 0.25), "orange": (0.92, 0.45, 0.10),
    "pink": (0.90, 0.45, 0.60), "purple": (0.45, 0.25, 0.60), "teal": (0.15, 0.55, 0.55),
    "yellow": (0.95, 0.80, 0.20), "cream": (0.92, 0.88, 0.78), "mint": (0.60, 0.85, 0.70),
    "hair_black": (0.08, 0.07, 0.07), "hair_brown": (0.30, 0.18, 0.10), "hair_blonde": (0.85, 0.70, 0.40),
    "hair_red": (0.60, 0.25, 0.10), "hair_grey": (0.70, 0.70, 0.72), "hair_white": (0.90, 0.90, 0.90),
    "glass": (0.15, 0.15, 0.18), "steel": (0.60, 0.62, 0.66),
}
MATS = {}
def mat(name):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*PAL[name], 1); b.inputs["Roughness"].default_value = 0.9
    MATS[name] = m; return m

_VM = None
def vcol_material():
    global _VM
    if _VM: return _VM
    m = bpy.data.materials.new("CharacterVertexColor"); m.use_nodes = True
    nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]; bsdf.inputs["Roughness"].default_value = 0.9
    ca = nt.nodes.new("ShaderNodeVertexColor"); ca.layer_name = "Col"
    nt.links.new(ca.outputs["Color"], bsdf.inputs["Base Color"])
    _VM = m; return m

def vcol_material():
    m = bpy.data.materials.get("VColor")
    if m: return m
    m = bpy.data.materials.new("VColor"); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.9
    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Col"
    nt.links.new(attr.outputs["Color"], b.inputs["Base Color"])
    return m

# ------------------------------------------------------------------ builder
class Builder:
    def __init__(self, spec):
        self.spec = spec
        self.bm = bmesh.new()
        self.weights = {}
        self.facemat = {}
        self.matnames = []
        self.S = spec.get("height", 1.0)         # global height scale
        self.W = spec.get("width", 1.0)          # shoulder/torso width scale

    def mi(self, name):
        if name not in self.matnames: self.matnames.append(name)
        return self.matnames.index(name)

    def _add(self, bm, matname, weight_fn):
        bm.verts.index_update()
        vmap = {}
        for v in bm.verts:
            nv = self.bm.verts.new(v.co); vmap[v.index] = nv
            self.weights[nv] = weight_fn(v.co)
        for f in bm.faces:
            try:
                nf = self.bm.faces.new([vmap[v.index] for v in f.verts])
                self.facemat[nf] = self.mi(matname if isinstance(matname, str) else matname(f))
            except ValueError:
                pass
        bm.free()

    def box(self, center, size, matname, bone, zcuts=(), taper_top=1.0, taper_bottom=1.0, wscale=True):
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
        sx, sy, sz = size; cx, cy, cz = center
        S = self.S; W = self.W if wscale else 1.0
        for v in bm.verts:
            t = v.co.z + 0.5
            f = taper_bottom + (taper_top - taper_bottom) * t
            v.co.x *= sx * f; v.co.y *= sy * f; v.co.z *= sz
        for (zc, _, _) in zcuts:
            geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, zc - cz), plane_no=(0, 0, 1))
        for v in bm.verts:
            v.co.x = (v.co.x + cx) * W; v.co.y += cy; v.co.z = (v.co.z + cz) * S
        def wfn(co):
            if not zcuts: return {bone: 1.0}
            z = co.z / S
            for (zc, a, b) in zcuts:
                if abs(z - zc) < 1e-4: return {a: 0.5, b: 0.5}
            cs = sorted(zcuts, key=lambda c: c[0]); b = cs[0][2]
            for (zc, a, _) in cs:
                if z > zc: b = a
            return {b: 1.0}
        self._add(bm, matname, wfn)

    def arm(self, side, s, sleeve_to, sleeve_mat, skin, thick=0.13):
        sp = self.spec
        shoulder_x = 0.23; elbow = 0.47; wrist = 0.74
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
        L = wrist - shoulder_x
        for v in bm.verts:
            v.co.x = v.co.x * L + (shoulder_x + wrist) / 2; v.co.y *= thick; v.co.z *= thick
        for xc in (elbow, sleeve_to):
            geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(xc, 0, 0), plane_no=(1, 0, 0))
        for v in bm.verts:
            v.co.x *= s * self.W; v.co.z += 1.42; v.co.z *= self.S
        W = self.W
        def wfn(co):
            ax = abs(co.x) / W
            if abs(ax - elbow) < 1e-4: return {f"{side}Arm": 0.5, f"{side}ForeArm": 0.5}
            return {f"{side}ForeArm": 1.0} if ax > elbow else {f"{side}Arm": 1.0}
        def mfn(f):
            cxf = sum(abs(v.co.x) for v in f.verts) / len(f.verts) / W
            return sleeve_mat if cxf < sleeve_to - 1e-4 else skin
        self._add(bm, mfn, wfn)
        hx = s * (wrist + 0.06)
        self.box((hx, 0, 1.415), (0.12, 0.10, 0.13), skin, f"{side}Hand")
        self.box((s * (wrist + 0.04), -0.06, 1.41), (0.05, 0.04, 0.06), skin, f"{side}Hand")

    def build(self):
        sp = self.spec; skin = sp["skin"]
        top, pants, shoes = sp["top"], sp["pants"], sp["shoes"]
        H_HIP, KNEE, ANKLE = 0.92, 0.50, 0.10
        tw = sp.get("torso", 1.0)
        # torso
        self.box((0, 0, 1.31), (0.40 * tw, 0.24, 0.26), top, "Spine2", taper_top=1.08, taper_bottom=0.95)
        self.box((0, 0, 1.12), (0.36 * tw, 0.22, 0.14), top, "Spine1", taper_top=0.98)
        self.box((0, 0, 1.02), (0.36 * tw, 0.22, 0.07), sp.get("belt", pants), "Spine")
        self.box((0, 0, 0.955), (0.36 * tw, 0.23, 0.06), pants, "Hips")
        self.box((0.16 * tw, 0, 1.42), (0.14, 0.22, 0.06), sp.get("shoulder", top), "LeftShoulder")
        self.box((-0.16 * tw, 0, 1.42), (0.14, 0.22, 0.06), sp.get("shoulder", top), "RightShoulder")
        # coat / dress skirt
        if sp.get("coat"):
            c = sp["coat"]
            self.box((0, 0, 0.88), (0.40 * tw, 0.26, 0.26), c, None, zcuts=[(0.92, "Hips", "Hips")])
            self.box((0, -0.127, 1.36), (0.12, 0.012, 0.16), sp["under"], "Spine2", taper_bottom=0.5)  # scrubs V
            self.box((0, -0.128, 1.12), (0.02, 0.012, 0.32), "lgrey", "Spine1")   # coat opening line
            self.box((0.12 * tw, -0.127, 1.12), (0.09, 0.012, 0.08), c, "Spine1")  # pocket
        if sp.get("skirt"):
            self.box((0, 0, 0.80), (0.40 * tw, 0.27, 0.26), sp["skirt"], "Hips", taper_top=0.85)
        # details
        if sp.get("tie"):
            self.box((0, -0.125, 1.30), (0.04, 0.012, 0.20), sp["tie"], "Spine2", taper_bottom=1.5)
        if sp.get("badge"):
            self.box((0.10, -0.125, 1.36), (0.05, 0.015, 0.05), "gold", "Spine2")
        if sp.get("buttons"):
            self.box((0, -0.125, 1.30), (0.03, 0.012, 0.22), sp["buttons"], "Spine2")
        if sp.get("stripes"):   # firefighter reflective stripes
            for z in (1.22, 1.36):
                self.box((0, 0, z), (0.41 * tw, 0.25, 0.03), sp["stripes"], "Spine2" if z > 1.3 else "Spine1")
        if sp.get("stethoscope"):
            self.box((0.07, -0.13, 1.38), (0.02, 0.02, 0.14), "dgrey", "Spine2")
            self.box((-0.07, -0.13, 1.38), (0.02, 0.02, 0.14), "dgrey", "Spine2")
            self.box((0, -0.14, 1.30), (0.16, 0.02, 0.02), "dgrey", "Spine2")
            self.box((0.0, -0.145, 1.27), (0.05, 0.02, 0.05), "steel", "Spine2")
        if sp.get("apron"):
            self.box((0, -0.13, 1.22), (0.30 * tw, 0.012, 0.30), sp["apron"], "Spine1")
            self.box((0, -0.14, 0.86), (0.34 * tw, 0.012, 0.36), sp["apron"], "Hips")
        if sp.get("vest"):
            self.box((0, 0, 1.28), (0.42 * tw, 0.26, 0.24), sp["vest"], "Spine2")
            self.box((0, 0, 1.28), (0.43 * tw, 0.27, 0.04), "lgrey", "Spine2")
        if sp.get("backpack"):
            self.box((0, 0.17, 1.25), (0.28 * tw, 0.12, 0.30), sp["backpack"], "Spine2")
        if sp.get("bag"):
            self.box((0.21 * tw, 0.0, 1.05), (0.06, 0.18, 0.16), sp["bag"], "Spine")
            self.box((0, 0.0, 1.30), (0.03, 0.26, 0.26), sp["bag"], "Spine2")
        # neck & head
        self.box((0, 0, 1.465), (0.10, 0.10, 0.07), skin, "Neck", wscale=False)
        hs = sp.get("head", 1.0)
        self.box((0, 0, 1.68), (0.26 * hs, 0.26 * hs, 0.30), skin, "Head", wscale=False)
        self.box((0.055 * hs, -0.131 * hs, 1.70), (0.035, 0.01, 0.045), "eye", "Head", wscale=False)
        self.box((-0.055 * hs, -0.131 * hs, 1.70), (0.035, 0.01, 0.045), "eye", "Head", wscale=False)
        self.box((0, -0.14 * hs, 1.66), (0.04, 0.03, 0.05), skin, "Head", wscale=False)
        self.box((0.125 * hs, 0, 1.68), (0.03, 0.06, 0.06), skin, "Head", wscale=False)
        self.box((-0.125 * hs, 0, 1.68), (0.03, 0.06, 0.06), skin, "Head", wscale=False)
        hair, hc = sp.get("hair", "short"), sp.get("hair_color", "hair_brown")
        if hair in ("short", "long", "bun", "ponytail", "curly"):
            self.box((0, 0.005, 1.825), (0.29 * hs, 0.29 * hs, 0.09), hc, "Head", wscale=False)    # top (clears head top at 1.83)
            self.box((0, 0.095 * hs, 1.66), (0.29 * hs, 0.10, 0.22), hc, "Head", wscale=False)    # back
        if hair == "long":
            self.box((0, 0.08 * hs, 1.52), (0.28 * hs, 0.12, 0.26), hc, "Head", wscale=False)
            self.box((0.14 * hs, 0.02, 1.62), (0.03, 0.20, 0.30), hc, "Head", wscale=False)
            self.box((-0.14 * hs, 0.02, 1.62), (0.03, 0.20, 0.30), hc, "Head", wscale=False)
        if hair == "bun":
            self.box((0, 0.16 * hs, 1.78), (0.12, 0.10, 0.12), hc, "Head", wscale=False)
        if hair == "ponytail":
            self.box((0, 0.17 * hs, 1.60), (0.08, 0.08, 0.30), hc, "Head", wscale=False)
        if hair == "curly":
            self.box((0, 0.01, 1.85), (0.33 * hs, 0.33 * hs, 0.12), hc, "Head", wscale=False)
        if hair == "sides":   # balding
            self.box((0, 0.09 * hs, 1.64), (0.27 * hs, 0.10, 0.14), hc, "Head", wscale=False)
        if sp.get("beard"):
            self.box((0, -0.12 * hs, 1.56), (0.22 * hs, 0.05, 0.10), sp.get("hair_color", "hair_brown"), "Head", wscale=False)
        if sp.get("mustache"):
            self.box((0, -0.14 * hs, 1.615), (0.10, 0.02, 0.025), sp.get("hair_color", "hair_brown"), "Head", wscale=False)
        if sp.get("glasses"):
            for sx in (1, -1):
                self.box((sx * 0.055 * hs, -0.145 * hs, 1.70), (0.07, 0.01, 0.06), "glass", "Head", wscale=False)
            self.box((0, -0.145 * hs, 1.70), (0.03, 0.01, 0.01), "glass", "Head", wscale=False)
        hat = sp.get("hat")
        if hat == "cap":
            cc = sp.get("hat_color", "navy2")
            self.box((0, 0, 1.86), (0.30 * hs, 0.30 * hs, 0.08), cc, "Head", wscale=False)
            self.box((0, 0, 1.915), (0.26 * hs, 0.26 * hs, 0.05), cc, "Head", taper_top=0.85, wscale=False)
            self.box((0, -0.19 * hs, 1.83), (0.28 * hs, 0.12, 0.02), sp.get("visor", "black"), "Head", wscale=False)
            if sp.get("badge"): self.box((0, -0.151 * hs, 1.87), (0.06, 0.01, 0.05), "gold", "Head", wscale=False)
        elif hat == "cap_back":
            cc = sp.get("hat_color", "red")
            self.box((0, 0, 1.86), (0.30 * hs, 0.30 * hs, 0.08), cc, "Head", wscale=False)
            self.box((0, 0, 1.915), (0.26 * hs, 0.26 * hs, 0.05), cc, "Head", taper_top=0.85, wscale=False)
            self.box((0, 0.19 * hs, 1.83), (0.28 * hs, 0.12, 0.02), cc, "Head", wscale=False)
        elif hat == "toque":
            self.box((0, 0, 1.86), (0.28 * hs, 0.28 * hs, 0.08), "white", "Head", wscale=False)
            self.box((0, 0, 1.99), (0.32 * hs, 0.32 * hs, 0.20), "white", "Head", taper_bottom=0.85, wscale=False)
        elif hat == "hood":
            hc2 = sp.get("hat_color", sp["top"])
            self.box((0, 0.11 * hs, 1.68), (0.34 * hs, 0.12, 0.40), hc2, "Head", wscale=False)
            self.box((0, 0.0, 1.87), (0.34 * hs, 0.34 * hs, 0.08), hc2, "Head", wscale=False)
            self.box((0.155 * hs, -0.02, 1.68), (0.04, 0.26 * hs, 0.36), hc2, "Head", wscale=False)
            self.box((-0.155 * hs, -0.02, 1.68), (0.04, 0.26 * hs, 0.36), hc2, "Head", wscale=False)
        elif hat == "helmet":   # firefighter
            col = sp.get("hat_color", "fire_yellow")
            self.box((0, 0, 1.86), (0.32 * hs, 0.34 * hs, 0.10), col, "Head", wscale=False)
            self.box((0, 0.01, 1.93), (0.26 * hs, 0.28 * hs, 0.06), col, "Head", taper_top=0.8, wscale=False)
            self.box((0, -0.0, 1.815), (0.36 * hs, 0.40 * hs, 0.02), col, "Head", wscale=False)  # brim
            self.box((0, -0.165 * hs, 1.87), (0.08, 0.01, 0.06), "steel", "Head", wscale=False)
        elif hat == "beanie":
            self.box((0, 0, 1.84), (0.29 * hs, 0.29 * hs, 0.10), sp.get("hat_color", "red"), "Head", wscale=False)
            self.box((0, 0, 1.78), (0.30 * hs, 0.30 * hs, 0.04), sp.get("hat_color", "red"), "Head", wscale=False)
        elif hat == "hardhat":
            self.box((0, 0, 1.86), (0.30 * hs, 0.32 * hs, 0.09), sp.get("hat_color", "yellow"), "Head", wscale=False)
            self.box((0, -0.0, 1.815), (0.34 * hs, 0.38 * hs, 0.02), sp.get("hat_color", "yellow"), "Head", wscale=False)
        elif hat == "surgical":
            self.box((0, 0, 1.845), (0.29 * hs, 0.29 * hs, 0.08), sp.get("hat_color", "scrubs"), "Head", wscale=False)
        elif hat == "hijab":
            col = sp.get("hat_color", "purple")
            self.box((0, 0.0, 1.855), (0.32 * hs, 0.32 * hs, 0.07), col, "Head", wscale=False)       # top
            self.box((0, 0.10 * hs, 1.66), (0.32 * hs, 0.14, 0.36), col, "Head", wscale=False)       # back
            self.box((0.145 * hs, -0.02, 1.68), (0.04, 0.26 * hs, 0.34), col, "Head", wscale=False)  # sides
            self.box((-0.145 * hs, -0.02, 1.68), (0.04, 0.26 * hs, 0.34), col, "Head", wscale=False)
            self.box((0, -0.03, 1.515), (0.32 * hs, 0.26 * hs, 0.05), col, "Head", wscale=False)     # chin wrap
            self.box((0, 0, 1.44), (0.30, 0.22, 0.10), col, "Neck", wscale=False)
        elif hat == "bcap":
            col = sp.get("hat_color", "red")
            self.box((0, 0, 1.845), (0.29 * hs, 0.29 * hs, 0.08), col, "Head", wscale=False)
            self.box((0, -0.19 * hs, 1.815), (0.24 * hs, 0.14, 0.02), col, "Head", wscale=False)
        elif hat == "hood":
            col = sp.get("hat_color", top)
            self.box((0, 0.03, 1.85), (0.32 * hs, 0.32 * hs, 0.08), col, "Head", wscale=False)
            self.box((0, 0.11 * hs, 1.66), (0.32 * hs, 0.12, 0.34), col, "Head", wscale=False)
            self.box((0.15 * hs, 0.0, 1.68), (0.03, 0.26 * hs, 0.34), col, "Head", wscale=False)
            self.box((-0.15 * hs, 0.0, 1.68), (0.03, 0.26 * hs, 0.34), col, "Head", wscale=False)
        elif hat == "fedora":
            self.box((0, 0, 1.86), (0.28 * hs, 0.28 * hs, 0.10), sp.get("hat_color", "dgrey"), "Head", wscale=False)
            self.box((0, 0, 1.815), (0.38 * hs, 0.40 * hs, 0.02), sp.get("hat_color", "dgrey"), "Head", wscale=False)
        # arms
        sleeve = {"short": 0.40, "long": 0.74, "none": 0.23}[sp.get("sleeves", "short")]
        for side, s in (("Left", 1), ("Right", -1)):
            self.arm(side, s, sleeve, sp.get("sleeve_mat", top), skin, thick=0.13 * sp.get("arm", 1.0))
        # legs
        lw = sp.get("leg", 1.0)
        for side, s in (("Left", 1), ("Right", -1)):
            x = s * 0.10
            if sp.get("shorts"):
                self.box((x, 0, (H_HIP + KNEE + 0.04) / 2), (0.17 * lw, 0.20 * lw, H_HIP - KNEE - 0.04), pants, None,
                         zcuts=[(KNEE, f"{side}UpLeg", f"{side}Leg")], taper_bottom=0.95)
                self.box((x, 0, (H_HIP + ANKLE) / 2), (0.14 * lw, 0.16 * lw, H_HIP - ANKLE), skin, None,
                         zcuts=[(KNEE, f"{side}UpLeg", f"{side}Leg")], taper_bottom=0.85)
            else:
                self.box((x, 0, (H_HIP + ANKLE) / 2), (0.16 * lw, 0.19 * lw, H_HIP - ANKLE), pants, None,
                         zcuts=[(KNEE, f"{side}UpLeg", f"{side}Leg")], taper_bottom=0.85)
            if sp.get("pant_stripe"):
                self.box((x + s * 0.082 * lw, 0, (H_HIP + ANKLE) / 2), (0.01, 0.03, H_HIP - ANKLE - 0.02), sp["pant_stripe"], None,
                         zcuts=[(KNEE, f"{side}UpLeg", f"{side}Leg")], taper_bottom=0.85)
            if sp.get("leg_stripes"):
                self.box((x, 0, 0.30), (0.17 * lw, 0.20 * lw, 0.03), sp["leg_stripes"], f"{side}Leg")
            if sp.get("boots"):
                self.box((x, 0, 0.22), (0.17 * lw, 0.20 * lw, 0.26), shoes, f"{side}Leg")
            self.box((x, -0.04, 0.05), (0.17, 0.30, 0.10), shoes, f"{side}Foot")
        return self.finish()

    def finish(self):
        name = self.spec["name"]
        self.bm.verts.index_update(); self.bm.faces.index_update()
        me = bpy.data.meshes.new(name + "Mesh"); self.bm.to_mesh(me)
        fm = [0] * len(me.polygons)
        for f, i in self.facemat.items(): fm[f.index] = i
        col = me.color_attributes.new(name="Col", type='BYTE_COLOR', domain='CORNER')
        for p, i in zip(me.polygons, fm):
            p.use_smooth = False
            rgb = PAL[self.matnames[i]]
            for li in p.loop_indices: col.data[li].color_srgb = (*rgb, 1.0)
        me.materials.append(vcol_material())
        wbi = {v.index: w for v, w in self.weights.items()}
        self.bm.free(); me.update()
        obj = bpy.data.objects.new(name, me); scene.collection.objects.link(obj)
        arm = make_armature(name, self.S, self.W)
        obj.parent = arm
        mod = obj.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
        for b in arm.data.bones: obj.vertex_groups.new(name=b.name)
        for vi, w in wbi.items():
            for bn, wt in w.items():
                obj.vertex_groups[f"mixamorig:{bn}"].add([vi], wt, 'REPLACE')
        animate(arm)
        return obj, arm

# ------------------------------------------------------------------ armature
def make_armature(name, S, W):
    ad = bpy.data.armatures.new(name + "Arm"); arm = bpy.data.objects.new(name + "_Armature", ad)
    scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    def bone(n, h, t, parent=None, connect=False):
        b = ad.edit_bones.new(f"mixamorig:{n}")
        b.head = Vector((h[0] * W, h[1], h[2] * S)); b.tail = Vector((t[0] * W, t[1], t[2] * S))
        if parent: b.parent = ad.edit_bones[f"mixamorig:{parent}"]; b.use_connect = connect
    bone("Hips", (0, 0, 0.92), (0, 0, 1.02))
    bone("Spine", (0, 0, 1.02), (0, 0, 1.12), "Hips", True)
    bone("Spine1", (0, 0, 1.12), (0, 0, 1.22), "Spine", True)
    bone("Spine2", (0, 0, 1.22), (0, 0, 1.42), "Spine1", True)
    bone("Neck", (0, 0, 1.42), (0, 0, 1.52), "Spine2", True)
    bone("Head", (0, 0, 1.52), (0, 0, 1.95), "Neck", True)
    bone("HeadTop_End", (0, 0, 1.95), (0, 0, 2.05), "Head", True)
    for side, s in (("Left", 1), ("Right", -1)):
        bone(f"{side}Shoulder", (s * 0.05, 0, 1.40), (s * 0.23, 0, 1.42), "Spine2")
        bone(f"{side}Arm", (s * 0.23, 0, 1.42), (s * 0.47, 0, 1.42), f"{side}Shoulder", True)
        bone(f"{side}ForeArm", (s * 0.47, 0, 1.42), (s * 0.74, 0, 1.42), f"{side}Arm", True)
        bone(f"{side}Hand", (s * 0.74, 0, 1.42), (s * 0.86, 0, 1.42), f"{side}ForeArm", True)
        bone(f"{side}UpLeg", (s * 0.10, 0, 0.92), (s * 0.10, 0, 0.50), "Hips")
        bone(f"{side}Leg", (s * 0.10, 0, 0.50), (s * 0.10, 0, 0.10), f"{side}UpLeg", True)
        bone(f"{side}Foot", (s * 0.10, 0, 0.10), (s * 0.10, -0.15, 0.02), f"{side}Leg", True)
        bone(f"{side}ToeBase", (s * 0.10, -0.15, 0.02), (s * 0.10, -0.22, 0.02), f"{side}Foot", True)
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm

# ------------------------------------------------------------------ animation
def smooth(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)
def env(t, peak=0.22):
    return smooth(t / peak) if t < peak else 1 - smooth((t - peak) / (1 - peak))

def animate(arm):
    arm.animation_data_create()
    pb = arm.pose.bones
    for b in pb: b.rotation_mode = 'XYZ'
    def P(n): return pb[f"mixamorig:{n}"]
    def key_rot(n, f, rx=0, ry=0, rz=0):
        b = P(n); b.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz)); b.keyframe_insert("rotation_euler", frame=f)
    def key_loc(n, f, x=0, y=0, z=0):
        # y = back(+)/forward(-), z = up(+)/down(-). Hips local Y is world up, local Z is world +Y.
        b = P(n); b.location = (x, z, y); b.keyframe_insert("location", frame=f)
    def clear():
        for b in pb: b.rotation_euler = (0, 0, 0); b.location = (0, 0, 0)
    cur = {"act": None, "slot": None}
    def attach():
        arm.animation_data.action = cur["act"]
        if cur["slot"] is not None: arm.animation_data.action_slot = cur["slot"]
    def new_action(name):
        act = bpy.data.actions.new(name); cur["act"] = act; cur["slot"] = None
        arm.animation_data.action = act
        if hasattr(act, "slots"):
            slot = act.slots.new(id_type='OBJECT', name="Armature"); arm.animation_data.action_slot = slot; cur["slot"] = slot
        return act
    def push(act, last):
        act.frame_range = (1, last)
        tr = arm.animation_data.nla_tracks.new(); tr.name = act.name
        tr.strips.new(act.name, 1, act); tr.mute = True; arm.animation_data.action = None; cur["act"] = None
    ALL = ["Hips","Spine","Spine1","Spine2","Neck","Head","LeftShoulder","RightShoulder","LeftArm","RightArm","LeftForeArm","RightForeArm",
           "LeftHand","RightHand","LeftUpLeg","RightUpLeg","LeftLeg","RightLeg","LeftFoot","RightFoot"]
    def key_all(f):
        attach()
        for n in ALL: P(n).keyframe_insert("rotation_euler", frame=f)
        P("Hips").keyframe_insert("location", frame=f)
    HANG = -75
    # pose helpers write into bones without keying; key_all(f) snapshots everything -> smooth per-frame curves
    def pose_arms(swing=0.0, lflex=12, rflex=12, spread=0, lift=0):
        # swing>0 => LEFT arm forward (opposite of left leg). Left forward = ry<0, Right forward = ry>0.
        P("LeftArm").rotation_euler = (math.radians(HANG + spread + lift), math.radians(-swing), 0)
        P("RightArm").rotation_euler = (math.radians(HANG + spread + lift), math.radians(-swing), 0)
        P("LeftForeArm").rotation_euler = (0, 0, math.radians(-lflex))
        P("RightForeArm").rotation_euler = (0, 0, math.radians(rflex))
    def R(n, rx=0, ry=0, rz=0): P(n).rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
    def L(n, x=0, y=0, z=0): P(n).location = (x, z, y)

    # ---------------- Idle (90 f): breathing + subtle weight shift + head look
    A = new_action("Idle"); clear()
    N = 90
    for f in range(1, N + 1):
        t = (f - 1) / N; br = 0.5 - 0.5 * math.cos(t * 2 * math.pi); sway = math.sin(t * 2 * math.pi)
        L("Hips", z=-0.006 * br, x=0.012 * sway); R("Hips", rz=1.2 * sway)
        R("Spine1", rx=1.5 * br, rz=-0.8 * sway); R("Spine2", rx=1.2 * br)
        R("Head", rx=-1.5 * br, ry=4 * math.sin(t * 2 * math.pi + 1.0), rz=1.0 * sway)
        pose_arms(swing=0, lflex=12 + 2 * br, rflex=12 + 2 * br, spread=2.5 * br)
        R("LeftUpLeg", rz=-1.0 * sway); R("RightUpLeg", rz=-1.0 * sway)
        key_all(f)
    push(A, N)

    # ---------------- locomotion cycles
    def cycle(name, N, amp, knee, bob, lean, armswing, flex, twist, hipsway, footroll):
        act = new_action(name); clear()
        for f in range(1, N + 1):
            t = (f - 1) / N; w = t * 2 * math.pi
            sw = math.sin(w)                         # +1 : left leg forward
            L("Hips", z=-bob * (0.5 - 0.5 * math.cos(2 * w)), x=0.015 * hipsway * math.sin(w))
            R("Hips", ry=-twist * 0.6 * sw, rz=hipsway * 1.2 * sw)
            R("LeftUpLeg", rx=-amp * sw); R("RightUpLeg", rx=amp * sw)
            R("LeftLeg", rx=max(0.0, knee * -math.sin(w + 1.25)))
            R("RightLeg", rx=max(0.0, knee * math.sin(w + 1.25)))
            R("LeftFoot", rx=footroll * -math.sin(w + 0.6)); R("RightFoot", rx=footroll * math.sin(w + 0.6))
            R("Spine", rx=lean * 0.4); R("Spine1", rx=lean * 0.4, ry=twist * sw, rz=-1.5 * sw)
            R("Spine2", rx=lean * 0.3, ry=twist * 0.5 * sw)
            R("Neck", rx=-lean * 0.5); R("Head", rx=-lean * 0.5, ry=-twist * 1.2 * sw, rz=1.5 * sw)
            pose_arms(swing=armswing * sw, lflex=flex + 0.5 * flex * max(0, sw), rflex=flex + 0.5 * flex * max(0, -sw))
            key_all(f)
        push(act, N)
    cycle("Walk", 32, amp=30, knee=42, bob=0.025, lean=3, armswing=28, flex=16, twist=5, hipsway=1.0, footroll=12)
    cycle("Run", 22, amp=48, knee=78, bob=0.055, lean=12, armswing=42, flex=70, twist=8, hipsway=0.6, footroll=18)

    # ---------------- shooting
    def aim_upper(recoil=0.0, sway=0.0):
        R("RightArm", rx=-8 + 18 * recoil, rz=88 + sway)
        R("RightForeArm", rz=6 + 25 * recoil)
        R("LeftArm", rx=-30, rz=-62 + sway)
        R("LeftForeArm", rz=-55 - 10 * recoil)
        R("Spine2", rz=-12 + 4 * recoil, rx=2 + 3 * recoil)
        R("Head", rz=12, rx=-2)
    SA = new_action("Pistol_Aim"); clear()
    N = 90
    for f in range(1, N + 1):
        t = (f - 1) / N; br = 0.5 - 0.5 * math.cos(t * 2 * math.pi)
        aim_upper(sway=1.5 * br); L("Hips", z=-0.005 * br)
        R("LeftUpLeg", rx=-6); R("RightUpLeg", rx=6); key_all(f)
    push(SA, N)
    SF = new_action("Pistol_Fire"); clear()
    N = 16
    for f in range(1, N + 1):
        t = (f - 1) / (N - 1); rc = 0.7 * env(t, 0.22)
        aim_upper(recoil=rc); R("LeftUpLeg", rx=-6); R("RightUpLeg", rx=6); key_all(f)
    push(SF, N)
    SW = new_action("Pistol_Walk"); clear()
    N = 32
    for f in range(1, N + 1):
        t = (f - 1) / N; w = t * 2 * math.pi; sw = math.sin(w)
        L("Hips", z=-0.02 * (0.5 - 0.5 * math.cos(2 * w)))
        R("LeftUpLeg", rx=-26 * sw); R("RightUpLeg", rx=26 * sw)
        R("LeftLeg", rx=max(0.0, 38 * -math.sin(w + 1.25))); R("RightLeg", rx=max(0.0, 38 * math.sin(w + 1.25)))
        R("LeftFoot", rx=-10 * math.sin(w + 0.6)); R("RightFoot", rx=10 * math.sin(w + 0.6))
        R("Spine1", rx=3); aim_upper(); key_all(f)
    push(SW, N)

    # ---------------- hit reactions (per-frame envelope; return to neutral)
    def hit_clip(name, pose_fn, N=20, peak=0.2):
        act = new_action(name); clear()
        for f in range(1, N + 1):
            t = (f - 1) / (N - 1); a = env(t, peak)
            clear(); pose_arms(); pose_fn(a); key_all(f)
        push(act, N)
    def hit_front(a):
        R("Spine1", rx=-14 * a); R("Spine2", rx=-10 * a); R("Head", rx=-18 * a)
        L("Hips", y=0.08 * a, z=-0.02 * a)
        R("LeftArm", rx=HANG + 15 * a, ry=-30 * a); R("RightArm", rx=HANG + 15 * a, ry=-30 * a)
        R("LeftForeArm", rz=-35 * a); R("RightForeArm", rz=35 * a)
        R("LeftUpLeg", rx=8 * a); R("RightUpLeg", rx=8 * a)
    def hit_back(a):
        R("Spine1", rx=18 * a); R("Spine2", rx=12 * a); R("Head", rx=14 * a)
        L("Hips", y=-0.08 * a, z=-0.03 * a)
        R("LeftArm", rx=HANG + 10 * a, ry=40 * a); R("RightArm", rx=HANG + 10 * a, ry=40 * a)
        R("LeftForeArm", rz=-20 * a); R("RightForeArm", rz=20 * a)
        R("LeftLeg", rx=10 * a); R("RightLeg", rx=10 * a)
    def hit_left(a):
        R("Spine1", rz=16 * a); R("Spine2", rz=10 * a); R("Head", rz=12 * a)
        L("Hips", x=-0.07 * a, z=-0.02 * a)
        R("LeftArm", rx=HANG + 45 * a); R("LeftForeArm", rz=-40 * a)
        R("RightArm", rx=HANG - 5 * a); R("RightForeArm", rz=20 * a)
    def hit_right(a):
        R("Spine1", rz=-16 * a); R("Spine2", rz=-10 * a); R("Head", rz=-12 * a)
        L("Hips", x=0.07 * a, z=-0.02 * a)
        R("RightArm", rx=HANG + 45 * a); R("RightForeArm", rz=40 * a)
        R("LeftArm", rx=HANG - 5 * a); R("LeftForeArm", rz=-20 * a)
    def hit_head(a):
        R("Head", rx=-30 * a, rz=14 * a); R("Neck", rx=-10 * a); R("Spine2", rx=-8 * a)
        L("Hips", z=-0.05 * a, y=0.03 * a)
        R("LeftLeg", rx=14 * a); R("RightLeg", rx=14 * a); R("LeftUpLeg", rx=-8 * a); R("RightUpLeg", rx=-8 * a)
        R("LeftArm", rx=HANG + 20 * a); R("RightArm", rx=HANG + 20 * a)
        R("LeftForeArm", rz=-15 * a); R("RightForeArm", rz=15 * a)
    def hit_leg(side):
        other = "Right" if side == "Left" else "Left"; sgn = 1 if side == "Left" else -1
        def fn(a):
            R(f"{side}Leg", rx=55 * a); R(f"{side}UpLeg", rx=-25 * a)
            R(f"{other}Leg", rx=10 * a); R(f"{other}UpLeg", rx=-5 * a)
            L("Hips", z=-0.16 * a, x=-sgn * 0.03 * a); R("Hips", rz=-sgn * 6 * a)
            R("Spine1", rx=12 * a, rz=sgn * 8 * a); R("Head", rx=8 * a)
            R("LeftArm", rx=HANG + 25 * a, ry=-20 * a); R("RightArm", rx=HANG + 25 * a, ry=20 * a)
            R("LeftForeArm", rz=-25 * a); R("RightForeArm", rz=25 * a)
        return fn
    hit_clip("Hit_Front", hit_front); hit_clip("Hit_Back", hit_back)
    hit_clip("Hit_Left", hit_left); hit_clip("Hit_Right", hit_right)
    hit_clip("Hit_Head", hit_head, N=22)
    hit_clip("Hit_LegLeft", hit_leg("Left"), N=26, peak=0.25); hit_clip("Hit_LegRight", hit_leg("Right"), N=26, peak=0.25)

    # ---------------- Death (animated fallback; NPCs in the demo use physics ragdoll)
    D = new_action("Death"); clear()
    N = 34
    def lerp(a, b, t): return a + (b - a) * t
    for f in range(1, N + 1):
        t = (f - 1) / (N - 1)
        s1 = smooth(t / 0.3) if t < 0.3 else 1.0                 # stagger
        s2 = smooth((t - 0.25) / 0.5) if t > 0.25 else 0.0       # fall
        s3 = smooth((t - 0.8) / 0.2) if t > 0.8 else 0.0         # settle
        L("Hips", y=lerp(0, 0.06, s1) + lerp(0, 0.5, s2), z=lerp(0, -0.10, s1) + lerp(0, -0.72, s2))
        R("Hips", rx=lerp(0, -15, s1) + lerp(0, -75, s2))
        R("Spine1", rx=lerp(0, -10, s1) + lerp(0, 10, s2)); R("Spine2", rx=lerp(0, -4, s1)); R("Head", rx=lerp(0, -15, s1) + lerp(0, 5, s2), rz=18 * s2)
        R("LeftLeg", rx=lerp(0, 25, s1) - lerp(0, 15, s2)); R("RightLeg", rx=lerp(0, 25, s1) - lerp(0, 21, s2))
        R("LeftUpLeg", rx=lerp(0, -15, s1) + lerp(0, 3, s2), rz=-8 * s2); R("RightUpLeg", rx=lerp(0, -15, s1) + lerp(0, 3, s2), rz=8 * s2)
        R("LeftArm", rx=HANG + 20 * s1 + 5 * s2, ry=-25 * s1 + 15 * s2); R("RightArm", rx=HANG + 20 * s1 + 20 * s2, ry=25 * s1 - 5 * s2)
        R("LeftForeArm", rz=-20 * s1); R("RightForeArm", rz=20 * s1 + 30 * s2)
        key_all(f)
    push(D, N)

    # ================= limb hits =================
    def hit_hand(side):
        o = "Right" if side == "Left" else "Left"; sg = 1 if side == "Left" else -1
        def fn(a):
            R(f"{side}Arm", rx=HANG + 10 * a, ry=sg * 28 * a)             # jerk back
            R(f"{side}ForeArm", rz=-sg * (12 + 70 * a))                    # pull hand up
            R(f"{side}Hand", rx=35 * a * math.sin(a * 9), rz=-sg * 20 * a) # shake
            R("Spine1", rz=sg * 6 * a, rx=4 * a); R("Head", rz=sg * 14 * a, rx=10 * a)
            R(f"{o}Arm", rx=HANG + 8 * a)
        return fn
    def hit_arm(side):
        o = "Right" if side == "Left" else "Left"; sg = 1 if side == "Left" else -1
        def fn(a):
            R(f"{side}Shoulder", ry=-sg * 10 * a); R(f"{side}Arm", rx=HANG + 35 * a, ry=sg * 40 * a)
            R(f"{side}ForeArm", rz=-sg * (12 + 30 * a))
            R("Spine1", rz=sg * 12 * a, ry=sg * 10 * a); R("Spine2", rz=sg * 6 * a); R("Head", rz=sg * 10 * a, ry=-sg * 12 * a)
            L("Hips", x=-sg * 0.05 * a)
            R(f"{o}Arm", rx=HANG + 6 * a)
        return fn
    hit_clip("Hit_HandLeft", hit_hand("Left"), N=22, peak=0.18); hit_clip("Hit_HandRight", hit_hand("Right"), N=22, peak=0.18)
    hit_clip("Hit_ArmLeft", hit_arm("Left"), N=22); hit_clip("Hit_ArmRight", hit_arm("Right"), N=22)

    # ================= social =================
    act = new_action("Talk"); clear(); N = 150
    for f in range(1, N + 1):
        t = (f - 1) / N; w = t * 2 * math.pi
        g1 = 0.5 + 0.5 * math.sin(w * 3); g2 = 0.5 + 0.5 * math.sin(w * 2 + 1.7); nod = math.sin(w * 5)
        R("RightArm", rx=HANG + 25 * g1, ry=20 * g1 + 8 * g2); R("RightForeArm", rz=40 + 50 * g1, ry=-20 * g2)
        R("LeftArm", rx=HANG + 12 * g2, ry=-10 * g2); R("LeftForeArm", rz=-(25 + 45 * g2))
        R("RightHand", rz=15 * math.sin(w * 7)); R("LeftHand", rz=-10 * math.sin(w * 6 + 1))
        R("Spine1", rx=2 + 2 * g1, ry=4 * math.sin(w * 2)); R("Head", rx=3 * nod, ry=6 * math.sin(w * 1.5), rz=3 * math.sin(w * 2.5))
        L("Hips", z=-0.004 * g1); key_all(f)
    push(act, N)

    act = new_action("Wave"); clear(); N = 54
    for f in range(1, N + 1):
        t = (f - 1) / N; w = t * 2 * math.pi; wv = math.sin(w * 3)
        R("RightArm", rx=35, rz=15 + 6 * wv); R("RightForeArm", rz=70 + 28 * wv, rx=10 * wv); R("RightHand", rz=15 * wv)
        R("LeftArm", rx=HANG); R("LeftForeArm", rz=-12)
        R("Spine1", rz=-3, rx=1); R("Head", rz=-4 + 2 * wv, ry=-6); key_all(f)
    push(act, N)

    act = new_action("Dance"); clear(); N = 64
    for f in range(1, N + 1):
        t = (f - 1) / N; w = t * 2 * math.pi; s1 = math.sin(w); s2 = math.sin(2 * w); c1 = math.cos(w)
        L("Hips", z=-0.05 * (0.5 - 0.5 * math.cos(2 * w)), x=0.06 * s1); R("Hips", rz=10 * s1, ry=-12 * s1)
        R("Spine1", rz=-8 * s1, ry=12 * s1, rx=4 * s2); R("Spine2", rz=-5 * s1); R("Head", rz=8 * s1, ry=-10 * s1, rx=-5 * s2)
        R("LeftArm", rx=HANG + 95 * max(0, s1) + 20, ry=-20 * c1, rz=-10 * max(0, s1)); R("LeftForeArm", rz=-(40 + 50 * max(0, -s1)))
        R("RightArm", rx=HANG + 95 * max(0, -s1) + 20, ry=20 * c1, rz=10 * max(0, -s1)); R("RightForeArm", rz=40 + 50 * max(0, s1))
        R("LeftUpLeg", rx=-12 * max(0, s1), rz=-6 * s1); R("RightUpLeg", rx=-12 * max(0, -s1), rz=-6 * s1)
        R("LeftLeg", rx=22 * max(0, s1) + 6); R("RightLeg", rx=22 * max(0, -s1) + 6)
        R("LeftFoot", rx=-8 * max(0, s1)); R("RightFoot", rx=-8 * max(0, -s1)); key_all(f)
    push(act, N)

    def reach_clip(name, take):
        act = new_action(name); clear(); N = 70
        for f in range(1, N + 1):
            t = (f - 1) / (N - 1)
            out = smooth(t / 0.3) if t < 0.3 else (1.0 if t < 0.6 else 1 - smooth((t - 0.6) / 0.4))     # reach forward
            pull = (smooth((t - 0.45) / 0.2) if t > 0.45 else 0.0) * (1 - (smooth((t - 0.8) / 0.2) if t > 0.8 else 0.0)) if take else 0.0
            R("RightArm", rx=HANG + 35 * out - 10 * pull, ry=55 * out - 30 * pull)
            R("RightForeArm", rz=10 + 10 * out + 70 * pull, ry=-40 * out * (0 if take else 1))     # palm up when giving
            R("RightHand", rx=-20 * out * (0 if take else 1), rz=10 * pull)
            R("LeftArm", rx=HANG + 5 * out); R("LeftForeArm", rz=-(12 + 10 * out + (30 * pull if take else 0)))
            R("Spine1", rx=6 * out - 4 * pull, ry=-8 * out); R("Spine2", rx=3 * out); R("Head", rx=10 * out - 4 * pull, ry=4 * out)
            L("Hips", y=-0.03 * out, z=-0.01 * out); key_all(f)
        push(act, N)
    reach_clip("Give", False); reach_clip("Take", True)

    # ================= melee: knife & bat =================
    def knife_upper(thrust=0.0, sway=0.0):
        # right hand low & forward, blade along forearm; left arm up as guard
        R("RightArm", rx=-55 + 10 * thrust, ry=25 + 55 * thrust, rz=5 + sway)
        R("RightForeArm", rz=max(0.0, 60 - 75 * thrust), ry=0)
        R("LeftArm", rx=-45, ry=-35, rz=-5); R("LeftForeArm", rz=-85)
        R("Spine1", rx=10, ry=-10 + 25 * thrust, rz=0); R("Spine2", rx=4, ry=-6 + 12 * thrust); R("Head", rx=-6, ry=10 - 20 * thrust)
        L("Hips", z=-0.05)
    def bat_upper(wind=0.0, swing=0.0):
        # two-handed grip raised over the right shoulder; wind back then sweep across the body
        R("RightArm", rx=-10 - 10 * wind + 15 * swing, ry=-35 - 25 * wind + 110 * swing, rz=10)
        R("RightForeArm", rz=100 + 15 * wind - 70 * swing, ry=0)
        R("LeftArm", rx=-35 - 5 * wind + 10 * swing, ry=-25 - 20 * wind + 95 * swing, rz=-15)
        R("LeftForeArm", rz=-95 - 10 * wind + 60 * swing)
        R("Spine1", rx=6, ry=-18 - 20 * wind + 50 * swing, rz=-4 * wind); R("Spine2", ry=-10 - 10 * wind + 25 * swing); R("Head", ry=20 + 15 * wind - 45 * swing, rx=-4)
        L("Hips", z=-0.04 - 0.03 * swing); R("Hips", ry=-6 * wind + 14 * swing)
    for wname, upper in (("Knife", knife_upper), ("Bat", bat_upper)):
        act = new_action(f"{wname}_Idle"); clear(); N = 90
        for f in range(1, N + 1):
            t = (f - 1) / N; br = 0.5 - 0.5 * math.cos(t * 2 * math.pi)
            upper(0.0 if wname == "Knife" else 0.08 * br, 1.5 * br) if wname == "Knife" else upper(0.08 * br, 0.0)
            R("LeftUpLeg", rx=-14, rz=-4); R("RightUpLeg", rx=10, rz=4); R("LeftLeg", rx=18); R("RightLeg", rx=14); key_all(f)
        push(act, N)
        act = new_action(f"{wname}_Walk"); clear(); N = 32
        for f in range(1, N + 1):
            t = (f - 1) / N; w = t * 2 * math.pi
            L("Hips", z=-0.03 - 0.02 * (0.5 - 0.5 * math.cos(2 * w)))
            R("LeftUpLeg", rx=-24 * math.sin(w)); R("RightUpLeg", rx=24 * math.sin(w))
            R("LeftLeg", rx=max(0.0, 40 * -math.sin(w + 1.25)) + 6); R("RightLeg", rx=max(0.0, 40 * math.sin(w + 1.25)) + 6)
            R("LeftFoot", rx=-10 * math.sin(w + 0.6)); R("RightFoot", rx=10 * math.sin(w + 0.6))
            upper(0.0, 0.0); key_all(f)
        push(act, N)
    act = new_action("Knife_Attack"); clear(); N = 16
    for f in range(1, N + 1):
        t = (f - 1) / (N - 1)
        th = -0.35 * smooth(t / 0.25) if t < 0.25 else (-0.35 + 1.35 * smooth((t - 0.25) / 0.3) if t < 0.55 else 1.0 - smooth((t - 0.55) / 0.45))
        knife_upper(th); R("LeftUpLeg", rx=-14 - 10 * max(0, th)); R("RightUpLeg", rx=10); R("LeftLeg", rx=18 + 12 * max(0, th)); R("RightLeg", rx=14); L("Hips", z=-0.05, y=-0.08 * max(0, th)); key_all(f)
    push(act, N)
    act = new_action("Bat_Attack"); clear(); N = 24
    for f in range(1, N + 1):
        t = (f - 1) / (N - 1)
        wind = smooth(t / 0.3) * (1 - smooth((t - 0.3) / 0.15)) if t < 0.45 else 0.0
        sw = 0.0 if t < 0.3 else (smooth((t - 0.3) / 0.22) if t < 0.52 else 1 - smooth((t - 0.7) / 0.3) if t > 0.7 else 1.0)
        bat_upper(wind, sw); R("LeftUpLeg", rx=-14 - 8 * sw, rz=-4); R("RightUpLeg", rx=10, rz=4); R("LeftLeg", rx=18); R("RightLeg", rx=14 + 8 * sw); key_all(f)
    push(act, N)

    # ================= two-bone IK helpers (armature space; character faces -Y, left = +X, up = +Z) =================
    def _upd():
        arm.animation_data.action = None          # no animation evaluation may overwrite the hand-set pose
        bpy.context.view_layer.update()
    def set_bone(pb, head, dirv, zhint):
        Y = dirv.normalized(); Z = (zhint - Y * zhint.dot(Y)); Z = Z.normalized() if Z.length > 1e-4 else Vector((0, 0, 1)); X = Y.cross(Z).normalized(); Z = X.cross(Y).normalized()
        M = Matrix(((X.x, Y.x, Z.x, head.x), (X.y, Y.y, Z.y, head.y), (X.z, Y.z, Z.z, head.z), (0, 0, 0, 1)))
        pb.matrix = M
    def arm_ik(side, target, pole, hand_up=Vector((0, 0, 1)), hand_dir=None):
        up_, fo, ha = P(f"{side}Arm"), P(f"{side}ForeArm"), P(f"{side}Hand")
        _upd(); S = up_.head.copy(); l1 = up_.length; l2 = fo.length
        d = target - S; dist = max(0.05, min(l1 + l2 - 0.005, d.length)); dT = d.normalized()
        cosA = max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist))); a = math.acos(cosA)
        pv = (pole - S); pv = pv - dT * pv.dot(dT); pv = pv.normalized() if pv.length > 1e-4 else Vector((0, 0, -1))
        E = S + dT * (l1 * math.cos(a)) + pv * (l1 * math.sin(a))
        set_bone(up_, S, E - S, -pv); _upd()
        set_bone(fo, fo.head.copy(), target - fo.head, hand_up); _upd()
        set_bone(ha, ha.head.copy(), (hand_dir if hand_dir is not None else (target - fo.head)), hand_up); _upd()
    def hand_frame(side):
        _upd(); return P(f"{side}Hand").matrix.copy()

    # ================= rifle / shotgun with IK grips =================
    RIFLE = dict(rh=Vector((-0.10, -0.27, 1.30)), rpole=Vector((-0.35, 0.15, -0.7)), loff=Vector((0.0, 0.36, -0.03)), lpole=Vector((0.35, -0.05, -0.6)), spine=-22, head=(5, 20), hdir=Vector((0.06, -1.0, 0.0)))
    SHOTG = dict(rh=Vector((-0.12, -0.22, 1.24)), rpole=Vector((-0.4, 0.2, -0.7)), loff=Vector((0.0, 0.50, -0.02)), lpole=Vector((0.4, 0.0, -0.6)), spine=-18, head=(6, 16), hdir=Vector((0.08, -1.0, -0.02)))
    def long_gun_pose(cfg, recoil=0.0, pump=0.0, sway=0.0):
        R("Spine1", rz=cfg["spine"] * 0.35, rx=3 + 3 * recoil); R("Spine2", rz=cfg["spine"] * 0.65 + sway, rx=2 + 4 * recoil)
        R("Head", rx=cfg["head"][0] - 6 * recoil, rz=cfg["head"][1]); R("Neck", rz=4)
        R("RightShoulder", rz=0); R("LeftShoulder", rz=0)
        _upd()
        rh = cfg["rh"] + Vector((0, 0.07 * recoil, 0.03 * recoil))
        hd = cfg["hdir"].copy(); hd.z += 0.12 * recoil
        arm_ik("Right", rh, rh + cfg["rpole"], hand_dir=hd)
        Mh = hand_frame("Right")
        loff = cfg["loff"].copy(); loff.y -= 0.14 * pump
        lt = Mh @ loff
        arm_ik("Left", lt, lt + cfg["lpole"], hand_dir=(Mh.to_3x3() @ Vector((0, 1, 0))))
    for wname, cfg in (("Rifle", RIFLE), ("Shotgun", SHOTG)):
        for suffix in ("Aim", "Walk", "Fire"):
            act = new_action(f"{wname}_{suffix}"); clear()
            N = {"Aim": 90, "Walk": 32, "Fire": 8 if wname == "Rifle" else 30}[suffix]
            for f in range(1, N + 1):
                t = (f - 1) / N; clear()
                if suffix == "Aim":
                    br = 0.5 - 0.5 * math.cos(t * 2 * math.pi); L("Hips", z=-0.005 * br)
                    R("LeftUpLeg", rx=-8); R("RightUpLeg", rx=8); long_gun_pose(cfg, sway=1.2 * br)
                elif suffix == "Walk":
                    w = t * 2 * math.pi; L("Hips", z=-0.02 * (0.5 - 0.5 * math.cos(2 * w)))
                    R("LeftUpLeg", rx=-24 * math.sin(w)); R("RightUpLeg", rx=24 * math.sin(w))
                    R("LeftLeg", rx=max(0.0, 36 * -math.sin(w + 1.25))); R("RightLeg", rx=max(0.0, 36 * math.sin(w + 1.25)))
                    R("LeftFoot", rx=-10 * math.sin(w + 0.6)); R("RightFoot", rx=10 * math.sin(w + 0.6))
                    long_gun_pose(cfg)
                else:
                    tt = (f - 1) / (N - 1)
                    if wname == "Rifle": rc, pm = env(tt, 0.3) * 0.45, 0.0
                    else:
                        rc = env(min(1, tt / 0.35), 0.3) * 0.9 if tt < 0.35 else 0.0
                        pm = math.sin(max(0.0, min(1.0, (tt - 0.35) / 0.55)) * math.pi)
                    R("LeftUpLeg", rx=-8); R("RightUpLeg", rx=8); long_gun_pose(cfg, recoil=rc, pump=pm)
                key_all(f)
            push(act, N)

    # ================= crouch & jump =================
    def crouch_pose(ph=None, amp=0.0):
        # tactical crouch: knees ~90°, torso leaning forward, head up, hands in front of the knees
        sw = math.sin(ph) if ph is not None else 0.0
        L("Hips", z=-0.36, y=0.02); R("Hips", rx=4)
        R("Spine", rx=8); R("Spine1", rx=14); R("Spine2", rx=6); R("Neck", rx=-10); R("Head", rx=-18)
        R("LeftUpLeg", rx=-64 - amp * sw, rz=-5); R("RightUpLeg", rx=-64 + amp * sw, rz=5)
        R("LeftLeg", rx=94 + amp * 0.7 * sw); R("RightLeg", rx=94 - amp * 0.7 * sw)
        R("LeftFoot", rx=-30 + amp * 0.3 * sw); R("RightFoot", rx=-30 - amp * 0.3 * sw)
        pose_arms(swing=0.6 * amp * sw, lflex=42, rflex=42, spread=-6)
    act = new_action("Crouch_Idle"); clear(); N = 90
    for f in range(1, N + 1):
        t = (f - 1) / N; br = 0.5 - 0.5 * math.cos(t * 2 * math.pi); clear(); crouch_pose()
        L("Hips", z=-0.36 - 0.008 * br, y=0.02); R("Spine1", rx=14 + 2 * br); R("Head", rx=-18, ry=5 * math.sin(t * 2 * math.pi + 1)); key_all(f)
    push(act, N)
    act = new_action("Crouch_Walk"); clear(); N = 40
    for f in range(1, N + 1):
        t = (f - 1) / N; w = t * 2 * math.pi; clear(); crouch_pose(w, amp=16)
        L("Hips", z=-0.36 - 0.015 * (0.5 - 0.5 * math.cos(2 * w)), x=0.012 * math.sin(w), y=0.02); R("Hips", rz=3 * math.sin(w), rx=4); key_all(f)
    push(act, N)
    act = new_action("Jump"); clear(); N = 26
    for f in range(1, N + 1):
        t = (f - 1) / (N - 1); clear()
        ant = smooth(t / 0.15) * (1 - smooth((t - 0.15) / 0.1)) if t < 0.25 else 0.0          # crouch before launch
        air = smooth((t - 0.15) / 0.15) * (1 - smooth((t - 0.6) / 0.15)) if t > 0.15 else 0.0  # tuck in the air
        land = smooth((t - 0.7) / 0.12) * (1 - smooth((t - 0.85) / 0.15)) if t > 0.7 else 0.0  # absorb landing
        kb = 45 * ant + 70 * air + 40 * land
        R("LeftUpLeg", rx=-25 * ant - 50 * air - 20 * land); R("RightUpLeg", rx=-25 * ant - 40 * air - 20 * land)
        R("LeftLeg", rx=kb); R("RightLeg", rx=kb * 0.9); R("LeftFoot", rx=-10 * ant + 25 * air); R("RightFoot", rx=-10 * ant + 25 * air)
        R("Spine1", rx=12 * ant + 6 * air + 10 * land); R("Head", rx=-8 * ant - 5 * air)
        L("Hips", z=-0.18 * ant - 0.12 * land)   # the engine moves the body up; hips only dip on anticipation/landing
        R("LeftArm", rx=HANG + 20 * ant + 95 * air, ry=-20 * air); R("RightArm", rx=HANG + 20 * ant + 95 * air, ry=-20 * air)
        R("LeftForeArm", rz=-20 - 20 * air); R("RightForeArm", rz=20 + 20 * air)
        key_all(f)
    push(act, N)
    for tr in arm.animation_data.nla_tracks: tr.mute = False
    arm["idle_action"] = A.name

# ------------------------------------------------------------------ specs
SPECS = [
    # --- 3 polis
    dict(name="Polis_01", skin="skin_light", hair="short", hair_color="hair_brown", top="navy", pants="navy", shoes="black",
         belt="black", shoulder="navy2", hat="cap", badge=1, buttons="navy2", pant_stripe="lblue"),
    dict(name="Polis_02", skin="skin_dark", hair="short", hair_color="hair_black", top="navy", pants="navy", shoes="black",
         belt="black", shoulder="navy2", hat="cap", badge=1, buttons="navy2", pant_stripe="lblue", mustache=1, width=1.08, torso=1.05),
    dict(name="Polis_03", skin="skin_tan", hair="bun", hair_color="hair_black", top="navy", pants="navy", shoes="black",
         belt="black", shoulder="navy2", hat="cap", badge=1, buttons="navy2", pant_stripe="lblue", height=0.95, width=0.92, torso=0.92),
    # --- 2 doktor
    dict(name="Doktor_01", skin="skin_pale", hair="short", hair_color="hair_grey", top="white", under="scrubs", pants="scrubs", shoes="white",
         coat="white", belt="white", sleeves="long", sleeve_mat="white", shoulder="white", stethoscope=1, glasses=1, height=1.03),
    dict(name="Doktor_02", skin="skin_med", hair="long", hair_color="hair_black", top="white", under="scrubs", pants="scrubs", shoes="white",
         coat="white", belt="white", sleeves="long", sleeve_mat="white", shoulder="white", stethoscope=1, width=0.94, torso=0.94),
    # --- 2 itfaiyeci
    dict(name="Itfaiyeci_01", skin="skin_light", hair="short", hair_color="hair_red", top="fire_red", pants="fire_red", shoes="black",
         belt="black", sleeves="long", hat="helmet", hat_color="fire_yellow", stripes="fire_yellow", leg_stripes="fire_yellow",
         boots=1, width=1.10, torso=1.08, arm=1.15, leg=1.1, beard=1),
    dict(name="Itfaiyeci_02", skin="skin_dark", hair="short", hair_color="hair_black", top="dgrey", pants="dgrey", shoes="black",
         belt="black", sleeves="long", hat="helmet", hat_color="red", stripes="fire_yellow", leg_stripes="fire_yellow",
         boots=1, width=1.06, torso=1.06, arm=1.12, leg=1.08),
    # --- 10 vatandaş
    dict(name="Vatandas_01", skin="skin_light", hair="short", hair_color="hair_blonde", top="white", pants="dgrey", shoes="black",
         tie="red", sleeves="long", bag="brown"),                                                   # ofis çalışanı
    dict(name="Vatandas_02", skin="skin_tan", hair="long", hair_color="hair_brown", top="pink", pants="denim", shoes="white",
         sleeves="none", width=0.93, torso=0.92),                                                    # kolsuz tişört
    dict(name="Vatandas_03", skin="skin_med", hair="curly", hair_color="hair_black", top="green", pants="tan", shoes="brown",
         backpack="orange", sleeves="short"),                                                        # öğrenci
    dict(name="Vatandas_04", skin="skin_pale", hair="sides", hair_color="hair_grey", top="olive", pants="brown", shoes="brown",
         sleeves="long", glasses=1, mustache=1, torso=1.12, width=1.05, height=0.97),               # yaşlı adam
    dict(name="Vatandas_05", skin="skin_light", hair="ponytail", hair_color="hair_red", top="teal", pants="black", shoes="black",
         skirt="teal", sleeves="short", width=0.92, torso=0.92),                                     # etekli
    dict(name="Vatandas_06", skin="skin_dark", hair="short", hair_color="hair_black", top="hivis", pants="denim", shoes="brown",
         vest="orange", hat="hardhat", sleeves="long", sleeve_mat="hivis", width=1.08, torso=1.05, arm=1.1),  # inşaat işçisi
    dict(name="Vatandas_07", skin="skin_tan", hair="none", hair_color="hair_black", top="cream", pants="dgrey", shoes="black",
         hat="hijab", hat_color="purple", skirt="cream", sleeves="long", width=0.93),                # başörtülü
    dict(name="Vatandas_08", skin="skin_light", hair="short", hair_color="hair_brown", top="red", pants="denim", shoes="white",
         hat="beanie", hat_color="dgrey", sleeves="long", sleeve_mat="red", height=0.80, width=0.85, head=1.08),  # çocuk
    dict(name="Vatandas_09", skin="skin_med", hair="short", hair_color="hair_white", top="lgrey", pants="black", shoes="black",
         hat="fedora", hat_color="dgrey", sleeves="long", tie="black", beard=1, height=1.0),         # şapkalı yaşlı
    dict(name="Vatandas_10", skin="skin_pale", hair="bun", hair_color="hair_blonde", top="yellow", pants="purple", shoes="pink",
         sleeves="short", glasses=1, bag="mint", width=0.95, torso=0.93),                           # renkli genç
    dict(name="Vatandas_11", skin="skin_med", hair="short", hair_color="hair_black", top="dgrey", pants="dgrey", shoes="black",
         sleeves="long", tie="red", buttons="black", glasses=0),                                     # takım elbise
    dict(name="Vatandas_12", skin="skin_light", hair="curly", hair_color="hair_blonde", top="orange", pants="denim", shoes="white",
         sleeves="short", shorts=1, hat="bcap", hat_color="white", width=0.95),                      # şortlu turist
    dict(name="Vatandas_13", skin="skin_dark", hair="none", hair_color="hair_black", top="purple", pants="black", shoes="black",
         hat="hood", hat_color="purple", sleeves="long", width=1.02),                                # kapüşonlu
    dict(name="Vatandas_14", skin="skin_tan", hair="long", hair_color="hair_black", top="white", pants="denim", shoes="pink",
         sleeves="long", bag="red", width=0.92, torso=0.92),                                          # çantalı kadın
    dict(name="Vatandas_15", skin="skin_pale", hair="short", hair_color="hair_grey", top="cream", pants="brown", shoes="brown",
         sleeves="long", vest="olive", glasses=1, torso=1.08, width=1.04, height=0.98),              # yelekli yaşlı
    dict(name="Vatandas_16", skin="skin_light", hair="ponytail", hair_color="hair_brown", top="mint", pants="black", shoes="white",
         sleeves="none", shorts=1, width=0.92, torso=0.9),                                            # koşucu kadın
    dict(name="Vatandas_17", skin="skin_med", hair="short", hair_color="hair_black", top="hivis", pants="navy", shoes="black",
         sleeves="long", sleeve_mat="hivis", vest="orange", width=1.06, torso=1.04),                  # yol işçisi
    dict(name="Vatandas_18", skin="skin_dark", hair="curly", hair_color="hair_black", top="yellow", pants="purple", shoes="white",
         sleeves="short", backpack="teal", height=0.82, width=0.86, head=1.08),                       # çocuk 2
    dict(name="Vatandas_19", skin="skin_tan", hair="bun", hair_color="hair_red", top="teal", pants="cream", shoes="brown",
         sleeves="long", glasses=1, width=0.94, torso=0.93),                                          # öğretmen
    dict(name="Vatandas_20", skin="skin_light", hair="short", hair_color="hair_brown", top="red", pants="black", shoes="black",
         sleeves="short", hat="bcap", hat_color="black", bag="black", width=1.0),                     # kurye
    dict(name="Vatandas_21", skin="skin_pale", hair="long", hair_color="hair_white", top="lgrey", pants="dgrey", shoes="black",
         sleeves="long", skirt="lgrey", glasses=1, width=0.93, torso=0.95, height=0.96),              # yaşlı kadın
    dict(name="Vatandas_22", skin="skin_med", hair="short", hair_color="hair_brown", top="green", pants="tan", shoes="brown",
         sleeves="short", mustache=1, torso=1.15, width=1.08),                                        # iri adam
    dict(name="Vatandas_23", skin="skin_light", hair="none", hair_color="hair_black", top="pink", pants="denim", shoes="white",
         hat="hijab", hat_color="teal", sleeves="long", bag="brown", width=0.93),                     # başörtülü 2
    dict(name="Vatandas_24", skin="skin_dark", hair="short", hair_color="hair_black", top="white", pants="navy", shoes="black",
         sleeves="long", tie="navy", buttons="lgrey", backpack="dgrey"),                              # ofis 2
    dict(name="Vatandas_25", skin="skin_tan", hair="curly", hair_color="hair_brown", top="denim", pants="black", shoes="red",
         sleeves="long", hat="beanie", hat_color="green", shorts=0, width=0.97),                      # bereli genç
    # --- 15 yeni vatandaş
    dict(name="Vatandas_11", skin="skin_pale", hair="bun", hair_color="hair_grey", top="purple", pants="dgrey", shoes="black",
         skirt="dgrey", sleeves="long", glasses=1, width=0.9, torso=0.95, height=0.95),                       # yaşlı kadın
    dict(name="Vatandas_12", skin="skin_tan", hair="short", hair_color="hair_black", top="mint", pants="black", shoes="white",
         sleeves="none", shorts=1, width=0.97, torso=0.95),                                                  # koşucu
    dict(name="Vatandas_13", skin="skin_light", hair="short", hair_color="hair_black", top="dgrey", pants="dgrey", shoes="black",
         sleeves="long", tie="red", beard=1, shoulder="dgrey"),                                               # takım elbise
    dict(name="Vatandas_14", skin="skin_med", hair="short", hair_color="hair_black", top="red", pants="black", shoes="white",
         sleeves="long", hat="hood", height=0.92, width=0.92),                                                # kapüşonlu genç
    dict(name="Vatandas_15", skin="skin_tan", hair="short", hair_color="hair_brown", top="orange", pants="black", shoes="black",
         sleeves="short", hat="cap", hat_color="orange", visor="orange", backpack="yellow"),                  # kurye
    dict(name="Vatandas_16", skin="skin_light", hair="short", hair_color="hair_brown", top="white", pants="dgrey", shoes="black",
         sleeves="long", hat="toque", apron="white", mustache=1, torso=1.08, width=1.04),                     # aşçı
    dict(name="Vatandas_17", skin="skin_dark", hair="short", hair_color="hair_black", top="white", pants="black", shoes="black",
         sleeves="long", vest="black", tie="black", width=0.96),                                              # garson
    dict(name="Vatandas_18", skin="skin_pale", hair="long", hair_color="hair_blonde", top="teal", pants="teal", shoes="black",
         skirt="teal", sleeves="none", width=0.92, torso=0.9),                                                # elbiseli kadın
    dict(name="Vatandas_19", skin="skin_light", hair="ponytail", hair_color="hair_blonde", top="pink", pants="pink", shoes="white",
         skirt="pink", sleeves="short", height=0.78, width=0.84, head=1.1),                                   # kız çocuk
    dict(name="Vatandas_20", skin="skin_med", hair="none", hair_color="hair_black", top="cream", pants="dgrey", shoes="black",
         hat="hijab", hat_color="green", sleeves="long", bag="brown", width=0.93),                            # başörtülü 2
    dict(name="Vatandas_21", skin="skin_pale", hair="short", hair_color="hair_brown", top="lblue", pants="tan", shoes="brown",
         sleeves="short", shorts=1, hat="fedora", hat_color="tan", glasses=1, bag="black", torso=1.05),        # turist
    dict(name="Vatandas_22", skin="skin_tan", hair="short", hair_color="hair_brown", top="denim", pants="grey", shoes="brown",
         vest="orange", hat="hardhat", hat_color="white", sleeves="long", sleeve_mat="denim", beard=1, width=1.06, arm=1.1),  # inşaat 2
    dict(name="Vatandas_23", skin="skin_light", hair="bun", hair_color="hair_black", top="black", pants="black", shoes="black",
         skirt="black", sleeves="long", bag="red", width=0.92, torso=0.92),                                   # iş kadını
    dict(name="Vatandas_24", skin="skin_tan", hair="sides", hair_color="hair_grey", top="brown", pants="grey", shoes="black",
         sleeves="long", mustache=1, torso=1.12, width=1.04, height=0.95),                                    # yaşlı adam 2
    dict(name="Vatandas_25", skin="skin_light", hair="curly", hair_color="hair_red", top="purple", pants="denim", shoes="black",
         sleeves="none", glasses=1, width=0.95),                                                              # genç
]

built = []
for sp in SPECS:
    obj, arm = Builder(sp).build()
    for pb in arm.pose.bones: pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); arm.select_set(True)
    bpy.ops.export_scene.gltf(filepath=f"{OUT}/{sp['name']}.glb", export_format='GLB', use_selection=True,
                              export_apply=False, export_animations=True, export_animation_mode='NLA_TRACKS',
                              export_skins=True, export_yup=True)
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    print(f"{sp['name']}: {tris} tris")
    built.append((sp["name"], obj, arm))
    if os.environ.get('LITE'):
        os.makedirs(os.environ['LITE'], exist_ok=True)
        bpy.ops.export_scene.gltf(filepath=f"{os.environ['LITE']}/{sp['name']}.glb", export_format='GLB', use_selection=True,
                                  export_apply=False, export_animations=(len(built) == 1), export_animation_mode='NLA_TRACKS',
                                  export_skins=True, export_yup=True)
    if True:
        for tr in list(arm.animation_data.nla_tracks):
            for st in tr.strips: bpy.data.actions.remove(st.action, do_unlink=True)

if os.environ.get('NO_RENDER') and not os.environ.get('CARDS'):
    print('DONE'); sys.exit(0)
# ------------------------------------------------------------------ lineup render
scene.render.engine = 'CYCLES'; scene.cycles.samples = 40; scene.cycles.device = 'CPU'
scene.view_settings.view_transform = 'Standard'
world = bpy.data.worlds.new("W"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.93, 0.93, 0.95, 1)
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sun.data.energy = 3.0; sun.data.angle = math.radians(20)
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(25)); scene.collection.objects.link(sun)
bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
PAL["ground"] = (0.86, 0.86, 0.88); bpy.context.object.data.materials.append(mat("ground"))
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam); scene.camera = cam
cam.data.lens = 50

def set_idle_pose(arm, walk=False, frame=1):
    # Force a specific action as active (overrides NLA) for deterministic render pose
    act = bpy.data.actions[arm["idle_action"]] if not walk else None
    for tr in arm.animation_data.nla_tracks:
        tr.mute = (tr.name == "Idle") if walk else (tr.name == "Walk")

def shoot(path, names, walk, cam_loc, look_at, resx, resy, frame=1):
    for n, o, a in built:
        vis = n in names
        o.hide_render = not vis; a.hide_render = not vis
        set_idle_pose(a, walk)
    scene.frame_set(frame); scene.frame_set(frame + 1); scene.frame_set(frame)
    cam.location = cam_loc
    d = Vector(look_at) - Vector(cam_loc); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.render.resolution_x = resx; scene.render.resolution_y = resy
    scene.render.filepath = path; bpy.ops.render.render(write_still=True)

# arrange in a row, 0.85m apart, grouped: polis, doktor, itfaiye, vatandaş
spacing = 0.85
for i, (n, o, a) in enumerate(built):
    a.location.x = (i - (len(built) - 1) / 2) * spacing
    a.rotation_euler.z = math.radians(8)   # slight 3/4 turn
names_all = [n for n, _, _ in built]
if not os.environ.get('CARDS'):
    cam.data.lens = 40
    shoot(f"{OUT}/lineup_all.png", names_all, False, (0.3, -17.5, 2.4), (0.0, 0, 1.0), 3200, 720)
    cam.data.lens = 50
# per-character cards (walk pose, 3/4 view)
scene.cycles.samples = 24
import re as _re
for i, (n, o, a) in enumerate(built):
    if os.environ.get('CARDS') and not _re.match(os.environ['CARDS'], n): continue
    x = a.location.x
    shoot(f"{OUT}/card_{n}.png", [n], True, (x + 1.5, -4.0, 1.55), (x, 0, 0.98), 520, 760, frame=4)
print("DONE")
