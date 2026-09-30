# Shared garment refit: transfers static overlay geometry from the male-v5/female-v3 body
# onto the v6 attendee body, region by region, keeping each vertex's offset from the body surface.
import bpy, bmesh, math, random
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

V3 = Vector
ROOT = "C:/Projects/festival-tycoon/assets/source/characters/"

OLD_REGION = {  # old vertex group -> region
    "Continuous tee": "tee", "Hip volume": "hip", "Neck": "neck",
    "Left trouser leg": "leg", "Right trouser leg": "leg",
}


def append(path, pred):
    with bpy.data.libraries.load(path, link=False) as (s, d):
        d.objects = [n for n in s.objects if pred(n)]
    for o in d.objects:
        bpy.context.scene.collection.objects.link(o)
    return list(d.objects)


def islands(bm, faces):
    faces = set(faces); seen = set(); out = []
    for f in faces:
        if f in seen: continue
        st = [f]; comp = []
        while st:
            g = st.pop()
            if g in seen: continue
            seen.add(g); comp.append(g)
            for e in g.edges:
                st += [h for h in e.link_faces if h in faces and h not in seen]
        out.append(comp)
    return out


def centroid(faces):
    ps = [v.co for f in faces for v in f.verts]
    return sum(ps, V3()) / len(ps)


def side(c):
    return "" if abs(c.x) < 0.03 else ("L" if c.x < 0 else "R")


def region_faces_old(ob):
    """old body: faces grouped by majority vertex group, legs split by side."""
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
    dl = bm.verts.layers.deform.active
    names = {g.index: g.name for g in ob.vertex_groups}
    regions = {}
    for f in bm.faces:
        votes = {}
        for v in f.verts:
            for gi, w in v[dl].items():
                votes[names[gi]] = votes.get(names[gi], 0) + w
        if not votes: continue
        g = max(votes, key=votes.get)
        r = OLD_REGION.get(g)
        if r is None: continue
        if r == "leg": r += side(f.calc_center_median())
        regions.setdefault(r, []).append(f)
    return bm, regions


def region_faces_new(ob):
    """v6 body: groups are whole generator parts; lofts are separate islands."""
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
    dl = bm.verts.layers.deform.active
    names = {g.index: g.name for g in ob.vertex_groups}
    by = {}
    for f in bm.faces:
        g = names[next(iter(f.verts[0][dl].keys()))]
        by.setdefault(g, []).append(f)
    regions = {"tee": by["Tee"]}
    for isl in islands(bm, by["Trousers"]):
        s = side(centroid(isl))
        regions["hip" if s == "" else "leg" + s] = isl
    head = islands(bm, by["Head"])
    regions["neck"] = min(head, key=lambda i: centroid(i).z)
    return bm, regions


def sample(faces, n):
    """uniform surface samples (area weighted)"""
    tris = []
    for f in faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1):
            a, b, c = vs[0], vs[i], vs[i + 1]
            tris.append((a, b, c, (b - a).cross(c - a).length / 2))
    tot = sum(t[3] for t in tris); rng = random.Random(3); out = []
    for a, b, c, ar in tris:
        k = max(1, round(n * ar / tot))
        for _ in range(k):
            u, v = rng.random(), rng.random()
            if u + v > 1: u, v = 1 - u, 1 - v
            out.append(a + (b - a) * u + (c - a) * v)
    return out


class Slices:
    """per-height centre and half-extents of a region surface"""
    N = 40

    def __init__(self, faces):
        pts = sample(faces, 12000)
        self.z0 = min(p.z for p in pts); self.z1 = max(p.z for p in pts)
        self.rows = []
        for i in range(self.N):
            t = (i + 0.5) / self.N
            zc = self.z0 + t * (self.z1 - self.z0); h = (self.z1 - self.z0) / self.N
            s = [p for p in pts if abs(p.z - zc) <= h]
            if len(s) < 6:
                self.rows.append(None); continue
            xs = [p.x for p in s]; ys = [p.y for p in s]
            self.rows.append((V3(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, zc)),
                              max((max(xs) - min(xs)) / 2, 1e-3), max((max(ys) - min(ys)) / 2, 1e-3)))
        # fill gaps from neighbours
        good = [i for i, r in enumerate(self.rows) if r]
        for i, r in enumerate(self.rows):
            if r is None:
                self.rows[i] = self.rows[min(good, key=lambda g: abs(g - i))]

    def t(self, zz):
        return (zz - self.z0) / (self.z1 - self.z0)

    def at(self, t):
        f = min(max(t * self.N - 0.5, 0), self.N - 1.0001); i = int(f); a = f - i
        r0, r1 = self.rows[i], self.rows[i + 1]
        return r0[0].lerp(r1[0], a), r0[1] + (r1[1] - r0[1]) * a, r0[2] + (r1[2] - r0[2]) * a


def bvh(bm, faces):
    verts = sorted({v for f in faces for v in f.verts}, key=lambda v: v.index)
    idx = {v: i for i, v in enumerate(verts)}
    return BVHTree.FromPolygons([v.co.copy() for v in verts], [[idx[v] for v in f.verts] for f in faces])


def depth(tree, p):
    loc, n, _, d = tree.find_nearest(p)
    return d if (p - loc).dot(n) >= 0 else -d


class Transfer:
    def __init__(self, old_body, new_body):
        self.obm, self.oreg = region_faces_old(old_body)
        self.nbm, self.nreg = region_faces_new(new_body)
        keys = [k for k in self.oreg if k in self.nreg]
        self.keys = keys
        self.otree = {k: bvh(self.obm, self.oreg[k]) for k in keys}
        self.ntree = {k: bvh(self.nbm, self.nreg[k]) for k in keys}
        self.oall = bvh(self.obm, [f for k in keys for f in self.oreg[k]])
        self.nall = bvh(self.nbm, [f for k in keys for f in self.nreg[k]])
        self.osl = {k: Slices(self.oreg[k]) for k in keys}
        self.nsl = {k: Slices(self.nreg[k]) for k in keys}

    def map_point(self, p):
        best = None
        for k in self.keys:
            loc, n, _, d = self.otree[k].find_nearest(p)
            if best is None or d < best[3]: best = (k, loc, n, d)
        k, q, n, _ = best
        t = self.osl[k].t(q.z)
        oc, orx, ory = self.osl[k].at(t)
        nc, nrx, nry = self.nsl[k].at(t)
        qm = V3((nc.x + (q.x - oc.x) * nrx / orx, nc.y + (q.y - oc.y) * nry / ory, nc.z))
        q2, n2, _, _ = self.ntree[k].find_nearest(qm)
        d = p - q; dn = d.dot(n); dt = d - n * dn
        dt = dt - n2 * dt.dot(n2)
        return q2 + n2 * dn + dt

    def fit(self, mesh, sigma=0.012, clearance=0.004):
        P = [v.co.copy() for v in mesh.vertices]
        old_depth = [depth(self.oall, p) for p in P]
        D = [self.map_point(p) - p for p in P]
        kd = KDTree(len(P))
        for i, p in enumerate(P): kd.insert(p, i)
        kd.balance()

        def smooth(F, s):
            out = []
            for i, p in enumerate(P):
                acc = V3(); ws = 0
                for _, j, dist in kd.find_range(p, 3 * s):
                    w = math.exp(-dist * dist / (2 * s * s)); acc += F[j] * w; ws += w
                out.append(acc / ws)
            return out

        D = smooth(D, sigma)
        # keep what was outside the old body outside the new one, at least by `clearance`
        for _ in range(3):
            push = []
            for i, p in enumerate(P):
                q = p + D[i]
                want = min(old_depth[i], clearance) if old_depth[i] > 0 else None
                if want is None: push.append(V3()); continue
                loc, n, _, dist = self.nall.find_nearest(q)
                dd = dist if (q - loc).dot(n) >= 0 else -dist
                push.append(n * (want - dd) if dd < want else V3())
            D = [d + e for d, e in zip(D, smooth(push, sigma * 0.5))]
        # hard guarantee for anything still under the minimum
        for i, p in enumerate(P):
            if old_depth[i] <= 0: continue
            q = p + D[i]
            loc, n, _, dist = self.nall.find_nearest(q)
            dd = dist if (q - loc).dot(n) >= 0 else -dist
            want = min(old_depth[i], clearance)
            if dd < want: D[i] += n * (want - dd)
        for v, p, d in zip(mesh.vertices, P, D):
            v.co = p + d
        new_depth = [depth(self.nall, v.co) for v in mesh.vertices]
        stats = dict(
            vertices=len(P),
            old_min_mm=round(min(old_depth) * 1000, 2),
            new_min_mm=round(min(d for d, o in zip(new_depth, old_depth) if o > 0) * 1000, 2),
            inside_new=sum(1 for d, o in zip(new_depth, old_depth) if d < 0 and o > 0),
            old_median_mm=round(sorted(old_depth)[len(P) // 2] * 1000, 2),
            new_median_mm=round(sorted(new_depth)[len(P) // 2] * 1000, 2))
        return stats
