"""
Shared low-poly body for build_crowd.py and build_player.py.

The first generation of characters was made of plain boxes. This module replaces the Builder's geometry with faceted but
rounded shapes in the Synty / low-poly style: lofted superellipse cross-sections for the torso, head, arms and legs,
bevelled boxes for everything else, a face (eye whites, pupils, brows, nose, lips), hands with a thumb and shoes with a
sole. The armature, bone names, proportions and animations are unchanged, so the game reads the new models exactly like
the old ones.

Usage (after the Builder class is defined):  import char_body; char_body.upgrade(Builder, PAL)
"""
import math
import bmesh

EXTRA_PAL = {
    "eye_white": (0.95, 0.94, 0.92), "lip": (0.62, 0.32, 0.28), "sole": (0.12, 0.11, 0.10),
    "gang_blue": (0.13, 0.33, 0.85), "gang_green": (0.14, 0.58, 0.27), "gang_yellow": (0.95, 0.76, 0.12),
    "gang_red": (0.78, 0.10, 0.10), "wine": (0.45, 0.06, 0.12), "charcoal": (0.16, 0.16, 0.18),
    "camel": (0.78, 0.64, 0.42), "ivory": (0.93, 0.89, 0.80), "brick": (0.55, 0.20, 0.14),
}


# ------------------------------------------------------------------ geometry helpers
def se_ring(n, wa, wb, p):
    """Superellipse outline with n points, full widths wa x wb, exponent p (2 = ellipse, higher = boxier).
    Half-step offset so a flat face points straight along each axis."""
    pts = []
    for i in range(n):
        a = 2.0 * math.pi * (i + 0.5) / n
        c, s = math.cos(a), math.sin(a)
        pts.append((math.copysign(abs(c) ** (2.0 / p), c) * wa * 0.5, math.copysign(abs(s) ** (2.0 / p), s) * wb * 0.5))
    return pts


def loft(rings, n=12, p=2.6, axis="z", caps=True):
    """rings: (t, wa, wb, oa, ob) along the axis; for axis z, a/b are x/y; for axis x, a/b are y/z.
    Returns a closed bmesh with outward normals."""
    rings = sorted(rings, key=lambda r: r[0])
    bm = bmesh.new(); vr = []
    for t, wa, wb, oa, ob in rings:
        row = []
        for (a, b) in se_ring(n, wa, wb, p):
            co = (a + oa, b + ob, t) if axis == "z" else (t, a + oa, b + ob)
            row.append(bm.verts.new(co))
        vr.append(row)
    for r in range(len(vr) - 1):
        for i in range(n):
            j = (i + 1) % n
            try: bm.faces.new((vr[r][i], vr[r][j], vr[r + 1][j], vr[r + 1][i]))
            except ValueError: pass
    if caps:
        try: bm.faces.new(list(reversed(vr[0])))
        except ValueError: pass
        try: bm.faces.new(vr[-1])
        except ValueError: pass
    return bm


def rounded_box(size, r=0.22, taper_top=1.0, taper_bottom=1.0, p=2.9):
    """A box with chamfered corners and bevelled top/bottom edges, centred on the origin."""
    sx, sy, sz = size; m = min(sx, sy, sz); h = m * r
    n = 12 if m > 0.06 else 8
    rows = []
    for z, shrink in ((-sz / 2, 1.5 * h), (-sz / 2 + h, 0.0), (sz / 2 - h, 0.0), (sz / 2, 1.5 * h)):
        t = z / sz + 0.5; f = taper_bottom + (taper_top - taper_bottom) * t
        rows.append((z, max(0.004, sx * f - shrink), max(0.004, sy * f - shrink), 0.0, 0.0))
    return loft(rows, n=n, p=p)


def interp(rings, t, k):
    """Linear interpolation of column k of a ring profile at axis value t."""
    rs = sorted(rings, key=lambda r: r[0])
    if t <= rs[0][0]: return rs[0][k]
    for a, b in zip(rs, rs[1:]):
        if a[0] <= t <= b[0]:
            u = (t - a[0]) / max(1e-6, b[0] - a[0]); return a[k] + (b[k] - a[k]) * u
    return rs[-1][k]


def band_weights(v, bands, width):
    """bands: [(boundary, lower_bone, upper_bone), ...] ascending. Smooth blend over +-width around each boundary."""
    for (zb, lo, hi) in bands:
        if v < zb - width: return {lo: 1.0}
        if v <= zb + width:
            u = (v - (zb - width)) / (2 * width)
            return {lo: 1.0 - u, hi: u} if lo != hi else {lo: 1.0}
    return {bands[-1][2]: 1.0}


# ------------------------------------------------------------------ profiles (unscaled; z up, -y is the front)
TORSO_LOW = [(0.895, .345, .215, 0, 0), (0.93, .36, .225, 0, 0), (0.99, .355, .22, 0, 0), (1.055, .335, .21, 0, 0)]
TORSO_UP = [(1.055, .335, .21, 0, 0), (1.12, .34, .212, 0, 0), (1.20, .365, .225, 0, -.003), (1.29, .392, .238, 0, -.006),
            (1.37, .40, .234, 0, -.004), (1.425, .372, .21, 0, 0), (1.462, .23, .15, 0, .006)]
HEAD = [(1.528, .13, .14, 0, -.032), (1.555, .185, .20, 0, -.022), (1.60, .228, .245, 0, -.006), (1.66, .258, .268, 0, 0),
        (1.72, .264, .274, 0, .004), (1.78, .248, .262, 0, .008), (1.815, .20, .214, 0, .01), (1.836, .10, .11, 0, .01)]
LEG = [(0.10, .088, .10, 0, .006), (0.16, .094, .106, 0, .006), (0.28, .114, .13, 0, .013), (0.40, .128, .148, 0, .012),
       (0.50, .122, .134, 0, 0), (0.60, .142, .158, 0, -.003), (0.75, .162, .178, 0, 0), (0.86, .172, .188, 0, 0), (0.93, .17, .19, 0, 0)]
ARM = [(0.20, .15, .15), (0.25, .148, .14), (0.33, .134, .13), (0.42, .118, .114), (0.47, .104, .104), (0.53, .11, .108),
       (0.62, .098, .094), (0.71, .08, .076), (0.745, .077, .072)]   # (x, height, depth)


def upgrade(Builder, PAL):
    PAL.update({k: v for k, v in EXTRA_PAL.items() if k not in PAL})

    def setpart(self, p):
        if hasattr(self, "facepart"): self.part = p

    def xform_add(self, bm, matname, wfn, wscale=True, center=(0, 0, 0)):
        S = self.S; W = self.W if wscale else 1.0; cx, cy, cz = center
        for v in bm.verts:
            v.co.x = (v.co.x + cx) * W; v.co.y += cy; v.co.z = (v.co.z + cz) * S
        self._add(bm, matname, wfn)

    # ---------------------------------------------------------- bevelled box (replaces the plain cube)
    def box(self, center, size, matname, bone, zcuts=(), taper_top=1.0, taper_bottom=1.0, wscale=True, r=None):
        sx, sy, sz = size; cx, cy, cz = center
        rr = r if r is not None else (0.0 if min(size) < 0.03 else 0.24)
        if rr <= 0:
            bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
            for v in bm.verts:
                t = v.co.z + 0.5; f = taper_bottom + (taper_top - taper_bottom) * t
                v.co.x *= sx * f; v.co.y *= sy * f; v.co.z *= sz
        else:
            bm = rounded_box(size, rr, taper_top, taper_bottom)
        for (zc, _, _) in zcuts:
            geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, zc - cz), plane_no=(0, 0, 1))
        S = self.S
        def wfn(co):
            if not zcuts: return {bone: 1.0}
            z = co.z / S
            for (zc, a, b) in zcuts:
                if abs(z - zc) < 1e-4: return {a: 0.5, b: 0.5}
            cs = sorted(zcuts, key=lambda c: c[0]); b = cs[0][2]
            for (zc, a, _) in cs:
                if z > zc: b = a
            return {b: 1.0}
        xform_add(self, bm, matname, wfn, wscale, center)

    # ---------------------------------------------------------- arm + hand
    def arm(self, side, s, sleeve_to, sleeve_mat, skin, thick=0.13):
        k = thick / 0.13
        rings = [(x, d * k, h * k, 0.0, 0.0) for (x, h, d) in ARM]
        if sleeve_to > 0.24:   # a cuff: the sleeve ends in a small lip
            h0 = interp([(x, h, d) for x, h, d in ARM], sleeve_to, 1) * k; d0 = interp([(x, h, d) for x, h, d in ARM], sleeve_to, 2) * k
            rings = [r for r in rings if abs(r[0] - sleeve_to) > 0.012]
            rings += [(sleeve_to - 0.012, d0 * 1.1, h0 * 1.1, 0, 0), (sleeve_to, d0 * 1.1, h0 * 1.1, 0, 0), (sleeve_to + 0.002, d0 * 0.98, h0 * 0.98, 0, 0)]
        bm = loft(rings, n=10, p=2.3, axis="x")
        W, S = self.W, self.S
        for v in bm.verts:
            v.co.x *= s * W; v.co.z = (v.co.z + 1.42) * S
        if s < 0: bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        thr = sleeve_to + 0.0015
        def wfn(co):
            ax = abs(co.x) / W
            return {kk.replace("#", side): vv for kk, vv in band_weights(ax, [(0.47, "#Arm", "#ForeArm")], 0.035).items()}
        def mfn(f):
            cxf = sum(abs(v.co.x) for v in f.verts) / len(f.verts) / W
            return sleeve_mat if cxf < thr else skin
        self._add(bm, mfn, wfn)
        wr = 0.745; hb = f"{side}Hand"
        self.box((s * (wr + 0.045), 0.0, 1.418), (0.095, 0.088, 0.062), skin, hb, r=0.3)                   # palm
        self.box((s * (wr + 0.118), 0.0, 1.408), (0.065, 0.084, 0.046), skin, hb, r=0.3, taper_top=0.9)   # fingers, curled a little
        self.box((s * (wr + 0.04), -0.052, 1.41), (0.062, 0.034, 0.036), skin, hb, r=0.3)                 # thumb

    # ---------------------------------------------------------- loft helpers bound to the builder
    def loft_add(self, rings, matfn, wfn, n=12, p=2.6, wscale=True):
        bm = loft(rings, n=n, p=p, axis="z")
        xform_add(self, bm, matfn, wfn, wscale)

    def front_y(self, z, tw=1.0):
        """Torso front surface (y) at height z, for details that must sit on the chest."""
        prof = TORSO_LOW + TORSO_UP
        return -interp(prof, z, 2) / 2 + interp(prof, z, 4)

    def head_front(self, z, hs):
        return (-interp(HEAD, z, 2) / 2 + interp(HEAD, z, 4)) * hs

    # ---------------------------------------------------------- whole body
    def build(self):
        sp = self.spec; skin = sp["skin"]; S = self.S
        top, pants, shoes = sp["top"], sp["pants"], sp["shoes"]
        tw = sp.get("torso", 1.0)
        fem = sp.get("hair") in ("long", "bun", "ponytail") or bool(sp.get("skirt")) or sp.get("hat") == "hijab"
        def tr(prof):
            out = []
            for (z, wa, wb, oa, ob) in prof:
                f = tw
                if fem and z < 1.0: f *= 1.04
                if fem and 1.05 < z < 1.16: f *= 0.93
                out.append((z, wa * f, wb, oa, ob))
            return out
        tbands = [(1.00, "Hips", "Spine"), (1.12, "Spine", "Spine1"), (1.22, "Spine1", "Spine2")]
        def twf(co): return band_weights(co.z / S, tbands, 0.03)
        fy = lambda z: front_y(self, z) - 0.006

        # torso
        setpart(self, "bottom")
        belt = sp.get("belt", pants)
        def lowmat(f):
            zc = sum(v.co.z for v in f.verts) / len(f.verts) / S
            return belt if zc > 0.995 else pants
        loft_add(self, tr(TORSO_LOW), lowmat, twf)
        setpart(self, "top")
        loft_add(self, tr(TORSO_UP), top, twf)
        if sp.get("sleeves", "short") != "none":   # soft shoulder caps bridging torso and sleeve
            for bn, sx in (("LeftShoulder", 1), ("RightShoulder", -1)):
                self.box((sx * 0.172 * tw, 0, 1.425), (0.11, 0.17, 0.045), sp.get("shoulder", top), bn, r=0.5)
        # coat / skirt
        if sp.get("coat"):
            c = sp["coat"]
            self.box((0, 0, 0.88), (0.40 * tw, 0.26, 0.26), c, None, zcuts=[(0.92, "Hips", "Hips")])
            self.box((0, fy(1.36), 1.36), (0.12, 0.012, 0.16), sp["under"], "Spine2", taper_bottom=0.5)
            self.box((0, fy(1.12) - 0.002, 1.12), (0.02, 0.012, 0.32), "lgrey", "Spine1")
            self.box((0.12 * tw, fy(1.12), 1.12), (0.09, 0.012, 0.08), c, "Spine1")
        setpart(self, "bottom")
        if sp.get("skirt"):
            self.box((0, 0, 0.80), (0.41 * tw * (1.04 if fem else 1), 0.27, 0.26), sp["skirt"], "Hips", taper_top=0.93)
        setpart(self, "top")
        if sp.get("tie"):
            self.box((0, fy(1.30) - 0.003, 1.30), (0.045, 0.012, 0.20), sp["tie"], "Spine2", taper_bottom=1.5)
            self.box((0, fy(1.39) - 0.004, 1.39), (0.05, 0.016, 0.035), sp["tie"], "Spine2")   # knot
        if sp.get("badge"):
            self.box((0.10, fy(1.36), 1.36), (0.05, 0.015, 0.05), "gold", "Spine2")
        if sp.get("buttons"):
            for z in (1.18, 1.25, 1.32, 1.39):
                self.box((0, fy(z) + 0.002, z), (0.018, 0.01, 0.018), sp["buttons"], "Spine2" if z > 1.22 else "Spine1")
        if sp.get("stripes"):
            for z in (1.22, 1.36):
                self.box((0, 0, z), (0.42 * tw, 0.255, 0.03), sp["stripes"], "Spine2" if z > 1.3 else "Spine1", r=0.15)
        if sp.get("stethoscope"):
            self.box((0.07, fy(1.38), 1.38), (0.02, 0.02, 0.14), "dgrey", "Spine2")
            self.box((-0.07, fy(1.38), 1.38), (0.02, 0.02, 0.14), "dgrey", "Spine2")
            self.box((0, fy(1.30) - 0.01, 1.30), (0.16, 0.02, 0.02), "dgrey", "Spine2")
            self.box((0.0, fy(1.27) - 0.015, 1.27), (0.05, 0.02, 0.05), "steel", "Spine2")
        if sp.get("apron"):
            self.box((0, fy(1.22) - 0.004, 1.22), (0.30 * tw, 0.012, 0.30), sp["apron"], "Spine1")
            self.box((0, -0.14, 0.86), (0.34 * tw, 0.012, 0.36), sp["apron"], "Hips")
        if sp.get("vest"):
            self.box((0, 0, 1.28), (0.425 * tw, 0.26, 0.25), sp["vest"], "Spine2", r=0.2)
            self.box((0, 0, 1.28), (0.435 * tw, 0.268, 0.04), "lgrey", "Spine2", r=0.15)
        if sp.get("backpack"):
            self.box((0, 0.175, 1.25), (0.28 * tw, 0.12, 0.30), sp["backpack"], "Spine2", r=0.3)
            for sx in (0.08, -0.08):
                self.box((sx * tw, 0, 1.36), (0.035, 0.255, 0.035), sp["backpack"], "Spine2", r=0.0)   # straps over the shoulders
        if sp.get("bag"):
            self.box((0.21 * tw, 0.0, 1.05), (0.06, 0.18, 0.16), sp["bag"], "Spine", r=0.3)
            self.box((0, 0.0, 1.30), (0.03, 0.26, 0.26), sp["bag"], "Spine2", r=0.0)

        # neck & head
        setpart(self, "base")
        self.box((0, 0.005, 1.47), (0.105, 0.10, 0.09), skin, "Neck", wscale=False, r=0.4)
        hs = sp.get("head", 1.0)
        hb = loft([(z, wa * hs, wb * hs, oa, ob * hs) for (z, wa, wb, oa, ob) in HEAD], n=14, p=2.35, axis="z")
        xform_add(self, hb, skin, lambda co: {"Head": 1.0}, wscale=False)
        hf = lambda z: head_front(self, z, hs)
        hc = sp.get("hair_color", "hair_brown")
        for sx in (1, -1):
            self.box((sx * 0.052 * hs, hf(1.70) - 0.001, 1.70), (0.046, 0.012, 0.03), "eye_white", "Head", wscale=False, r=0.0)
            self.box((sx * 0.049 * hs, hf(1.70) - 0.007, 1.699), (0.021, 0.006, 0.025), "eye", "Head", wscale=False, r=0.0)
            if sp.get("hair") != "none" or sp.get("hat") in (None, "cap", "bcap", "fedora", "beanie"):
                self.box((sx * 0.054 * hs, hf(1.738) - 0.004, 1.738), (0.062, 0.014, 0.014), hc, "Head", wscale=False, r=0.0)
            self.box((sx * 0.131 * hs, 0.004, 1.672), (0.03, 0.055, 0.072), skin, "Head", wscale=False, r=0.35)   # ears
        self.box((0, hf(1.655) - 0.016, 1.652), (0.036, 0.036, 0.066), skin, "Head", wscale=False, taper_top=0.55, r=0.3)   # nose
        self.box((0, hf(1.595) - 0.002, 1.595), (0.056, 0.008, 0.013), "lip", "Head", wscale=False, r=0.0)                  # mouth

        hair = sp.get("hair", "short")
        setpart(self, "hair")
        if hair in ("short", "long", "bun", "ponytail", "curly"):
            self.box((0, 0.006, 1.822), (0.292 * hs, 0.296 * hs, 0.10), hc, "Head", wscale=False, taper_top=0.82, r=0.3)
            self.box((0, 0.098 * hs, 1.665), (0.292 * hs, 0.11, 0.24), hc, "Head", wscale=False, r=0.3)
            self.box((0, -0.122 * hs, 1.79), (0.24 * hs, 0.05, 0.05), hc, "Head", wscale=False, r=0.4)   # fringe line
        if hair == "long":
            self.box((0, 0.085 * hs, 1.52), (0.28 * hs, 0.12, 0.28), hc, "Head", wscale=False, taper_bottom=0.85, r=0.35)
            self.box((0.142 * hs, 0.02, 1.62), (0.035, 0.20, 0.30), hc, "Head", wscale=False, r=0.3)
            self.box((-0.142 * hs, 0.02, 1.62), (0.035, 0.20, 0.30), hc, "Head", wscale=False, r=0.3)
        if hair == "bun":
            self.box((0, 0.165 * hs, 1.78), (0.12, 0.10, 0.12), hc, "Head", wscale=False, r=0.45)
        if hair == "ponytail":
            self.box((0, 0.175 * hs, 1.60), (0.08, 0.08, 0.30), hc, "Head", wscale=False, taper_bottom=0.6, r=0.45)
        if hair == "curly":
            self.box((0, 0.012, 1.845), (0.33 * hs, 0.33 * hs, 0.13), hc, "Head", wscale=False, r=0.45)
        if hair == "sides":
            self.box((0, 0.095 * hs, 1.645), (0.278 * hs, 0.105, 0.15), hc, "Head", wscale=False, r=0.35)
        setpart(self, "beard")
        if sp.get("beard"):
            self.box((0, -0.112 * hs, 1.565), (0.20 * hs, 0.06, 0.095), hc, "Head", wscale=False, taper_bottom=0.75, r=0.35)
        setpart(self, "mustache")
        if sp.get("mustache"):
            self.box((0, hf(1.615) - 0.008, 1.615), (0.10, 0.02, 0.025), hc, "Head", wscale=False, r=0.3)
        setpart(self, "glasses")
        if sp.get("glasses"):
            for sx in (1, -1):
                self.box((sx * 0.055 * hs, hf(1.70) - 0.014, 1.70), (0.07, 0.01, 0.055), "glass", "Head", wscale=False, r=0.0)
            self.box((0, hf(1.70) - 0.014, 1.70), (0.03, 0.01, 0.01), "glass", "Head", wscale=False, r=0.0)
            for sx in (1, -1):
                self.box((sx * 0.135 * hs, -0.06, 1.705), (0.01, 0.15, 0.01), "glass", "Head", wscale=False, r=0.0)   # arms of the frame
        setpart(self, "hat")
        hat = sp.get("hat")
        if hat == "cap":
            cc = sp.get("hat_color", "navy2")
            self.box((0, 0, 1.86), (0.30 * hs, 0.30 * hs, 0.08), cc, "Head", wscale=False)
            self.box((0, 0, 1.915), (0.26 * hs, 0.26 * hs, 0.05), cc, "Head", taper_top=0.85, wscale=False)
            self.box((0, -0.19 * hs, 1.83), (0.28 * hs, 0.12, 0.02), sp.get("visor", "black"), "Head", wscale=False, r=0.0)
            if sp.get("badge"): self.box((0, -0.153 * hs, 1.87), (0.06, 0.01, 0.05), "gold", "Head", wscale=False, r=0.0)
        elif hat == "toque":
            self.box((0, 0, 1.86), (0.28 * hs, 0.28 * hs, 0.08), "white", "Head", wscale=False)
            self.box((0, 0, 1.99), (0.32 * hs, 0.32 * hs, 0.20), "white", "Head", taper_bottom=0.85, wscale=False, r=0.4)
        elif hat == "hood":
            col = sp.get("hat_color", top)
            self.box((0, 0.03, 1.85), (0.33 * hs, 0.33 * hs, 0.09), col, "Head", wscale=False, r=0.4)
            self.box((0, 0.115 * hs, 1.66), (0.33 * hs, 0.12, 0.36), col, "Head", wscale=False, r=0.3)
            self.box((0.155 * hs, 0.0, 1.68), (0.035, 0.26 * hs, 0.34), col, "Head", wscale=False, r=0.3)
            self.box((-0.155 * hs, 0.0, 1.68), (0.035, 0.26 * hs, 0.34), col, "Head", wscale=False, r=0.3)
        elif hat == "helmet":
            col = sp.get("hat_color", "fire_yellow")
            self.box((0, 0, 1.86), (0.32 * hs, 0.34 * hs, 0.10), col, "Head", wscale=False, r=0.35)
            self.box((0, 0.01, 1.93), (0.26 * hs, 0.28 * hs, 0.06), col, "Head", taper_top=0.8, wscale=False, r=0.4)
            self.box((0, 0.0, 1.815), (0.36 * hs, 0.40 * hs, 0.02), col, "Head", wscale=False, r=0.0)
            self.box((0, -0.168 * hs, 1.87), (0.08, 0.01, 0.06), "steel", "Head", wscale=False, r=0.0)
        elif hat == "beanie":
            col = sp.get("hat_color", "red")
            self.box((0, 0, 1.84), (0.295 * hs, 0.30 * hs, 0.11), col, "Head", wscale=False, taper_top=0.85, r=0.45)
            self.box((0, 0, 1.78), (0.30 * hs, 0.305 * hs, 0.04), col, "Head", wscale=False, r=0.2)
        elif hat == "hardhat":
            col = sp.get("hat_color", "yellow")
            self.box((0, 0, 1.86), (0.30 * hs, 0.32 * hs, 0.10), col, "Head", wscale=False, taper_top=0.85, r=0.45)
            self.box((0, 0.0, 1.815), (0.34 * hs, 0.38 * hs, 0.02), col, "Head", wscale=False, r=0.0)
        elif hat == "surgical":
            self.box((0, 0, 1.845), (0.29 * hs, 0.29 * hs, 0.08), sp.get("hat_color", "scrubs"), "Head", wscale=False, r=0.35)
        elif hat == "hijab":
            col = sp.get("hat_color", "purple")
            self.box((0, 0.0, 1.855), (0.32 * hs, 0.32 * hs, 0.08), col, "Head", wscale=False, r=0.4)
            self.box((0, 0.10 * hs, 1.66), (0.32 * hs, 0.14, 0.36), col, "Head", wscale=False, r=0.3)
            self.box((0.155 * hs, -0.02, 1.68), (0.035, 0.26 * hs, 0.34), col, "Head", wscale=False, r=0.3)
            self.box((-0.155 * hs, -0.02, 1.68), (0.035, 0.26 * hs, 0.34), col, "Head", wscale=False, r=0.3)
            self.box((0, -0.03, 1.515), (0.32 * hs, 0.26 * hs, 0.05), col, "Head", wscale=False, r=0.3)
            self.box((0, 0, 1.44), (0.30, 0.22, 0.10), col, "Neck", wscale=False, r=0.3)
        elif hat == "bcap":
            col = sp.get("hat_color", "red")
            self.box((0, 0, 1.85), (0.295 * hs, 0.30 * hs, 0.085), col, "Head", wscale=False, taper_top=0.85, r=0.4)
            self.box((0, -0.19 * hs, 1.815), (0.24 * hs, 0.14, 0.018), col, "Head", wscale=False, r=0.0)
        elif hat == "cap_back":
            cc = sp.get("hat_color", "red")
            self.box((0, 0, 1.85), (0.295 * hs, 0.30 * hs, 0.085), cc, "Head", wscale=False, taper_top=0.85, r=0.4)
            self.box((0, 0.19 * hs, 1.815), (0.24 * hs, 0.14, 0.018), cc, "Head", wscale=False, r=0.0)
        elif hat == "fedora":
            col = sp.get("hat_color", "dgrey")
            self.box((0, 0, 1.875), (0.27 * hs, 0.28 * hs, 0.12), col, "Head", wscale=False, taper_top=0.85, r=0.35)
            self.box((0, 0, 1.835), (0.275 * hs, 0.285 * hs, 0.025), sp.get("band", "black"), "Head", wscale=False, r=0.1)
            self.box((0, 0, 1.815), (0.40 * hs, 0.42 * hs, 0.02), col, "Head", wscale=False, r=0.0)

        # arms
        setpart(self, "top")
        sleeve = {"short": 0.40, "long": 0.71, "none": 0.23}[sp.get("sleeves", "short")]
        for side, s in (("Left", 1), ("Right", -1)):
            self.arm(side, s, sleeve, sp.get("sleeve_mat", top), skin, thick=0.13 * sp.get("arm", 1.0))

        # legs and shoes
        setpart(self, "bottom")
        lw = sp.get("leg", 1.0)
        for side, s in (("Left", 1), ("Right", -1)):
            xc = s * 0.10
            prof = [(z, wa * lw, wb * lw, xc, ob) for (z, wa, wb, oa, ob) in LEG]
            if sp.get("shorts"):
                cut = 0.56; wa0 = interp(prof, cut, 1); wb0 = interp(prof, cut, 2)
                prof = [r for r in prof if abs(r[0] - cut) > 0.02] + [(cut - 0.006, wa0, wb0, xc, 0), (cut - 0.004, wa0 * 1.14, wb0 * 1.12, xc, 0), (cut + 0.03, wa0 * 1.16, wb0 * 1.13, xc, 0)]
                def lmat(f, cut=cut):
                    zc = sum(v.co.z for v in f.verts) / len(f.verts) / S
                    return pants if zc > cut - 0.005 else skin
            else:
                prof = [r for r in prof if r[0] > 0.12] + [(0.105, 0.112 * lw, 0.128 * lw, xc, 0.004), (0.13, 0.11 * lw, 0.126 * lw, xc, 0.005)]
                lmat = pants
            lb = [(0.50, f"{side}Leg", f"{side}UpLeg")]
            loft_add(self, prof, lmat, lambda co, lb=lb: band_weights(co.z / S, lb, 0.04), n=10, p=2.3)
            if sp.get("pant_stripe"):
                st = [(z, 0.008, 0.026, xc + s * (wa * lw / 2 + 0.0015), ob) for (z, wa, wb, oa, ob) in LEG if z > 0.12]
                loft_add(self, st, sp["pant_stripe"], lambda co, lb=lb: band_weights(co.z / S, lb, 0.04), n=4, p=2.0)
            if sp.get("leg_stripes"):
                self.box((xc, 0.012, 0.30), (0.135 * lw, 0.158 * lw, 0.03), sp["leg_stripes"], f"{side}Leg", r=0.15)
            if sp.get("boots"):
                self.box((xc, 0.008, 0.22), (0.14 * lw, 0.16 * lw, 0.26), shoes, f"{side}Leg", taper_bottom=1.05, r=0.25)
            sole = "sole" if shoes not in ("black", "sole") else "dgrey"
            self.box((xc, -0.045, 0.068), (0.112, 0.255, 0.085), shoes, f"{side}Foot", taper_top=0.82, r=0.42)
            self.box((xc, -0.05, 0.015), (0.122, 0.272, 0.03), sole, f"{side}Foot", r=0.3)
        return None if hasattr(self, "facepart") else self.finish()

    Builder.box = box
    Builder.arm = arm
    Builder.build = build
