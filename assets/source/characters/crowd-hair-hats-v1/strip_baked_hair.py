# Remove the baked hair from v2 guest pose GLBs and performer bodies, in place, so hair can be a separate sibling mesh.
#   python strip_baked_hair.py file.glb [...]
# Hair is every triangle on guest palette slots 10-11 (u in [80/96, 96/96)): nothing else uses them. Only the index
# buffer changes (the kept triangles are written back in order and the accessor count shrinks); vertices, nodes,
# sockets, materials and images are untouched. Re-running on a stripped file is a no-op.
import json, struct, sys

COMP = {5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
TYPE_N = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def strip(path):
    raw = bytearray(open(path, "rb").read())
    magic, version, _ = struct.unpack_from("<III", raw, 0)
    jlen, _ = struct.unpack_from("<II", raw, 12)
    j = json.loads(bytes(raw[20:20 + jlen]))
    blen, _ = struct.unpack_from("<II", raw, 20 + jlen)
    bin_off = 28 + jlen
    def view(acc):
        a = j["accessors"][acc]; bv = j["bufferViews"][a["bufferView"]]
        fmt, size = COMP[a["componentType"]]; n = TYPE_N[a["type"]]
        start = bin_off + bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride", size * n)
        return a, start, fmt, size, n, stride
    removed = kept_total = 0
    for mesh in j["meshes"]:
        for prim in mesh["primitives"]:
            if "indices" not in prim or "TEXCOORD_0" not in prim["attributes"]: continue
            ua, us, uf, uz, un, ust = view(prim["attributes"]["TEXCOORD_0"])
            u = [struct.unpack_from("<" + uf, raw, us + i * ust)[0] for i in range(ua["count"])]
            ia, ist, ifmt, isz, _, _ = view(prim["indices"])
            idx = list(struct.unpack_from("<%d%s" % (ia["count"], ifmt), raw, ist))
            tris = [idx[k:k + 3] for k in range(0, len(idx), 3)]
            keep = [t for t in tris if not (80 / 96 <= u[t[0]] < 1.0)]
            removed += len(tris) - len(keep); kept_total += len(keep)
            flat = [i for t in keep for i in t]
            struct.pack_into("<%d%s" % (len(flat), ifmt), raw, ist, *flat)
            struct.pack_into("<%d%s" % (len(idx) - len(flat), ifmt), raw, ist + len(flat) * isz, *([0] * (len(idx) - len(flat))))
            ia["count"] = len(flat)
            if "max" in ia: ia["max"] = [max(flat)]; ia["min"] = [min(flat)]
    body = json.dumps(j, separators=(",", ":")).encode()
    body += b" " * ((4 - len(body) % 4) % 4)
    rest = bytes(raw[20 + jlen:])
    out = struct.pack("<III", magic, version, 12 + 8 + len(body) + len(rest)) + struct.pack("<II", len(body), 0x4E4F534A) + body + rest
    open(path, "wb").write(out)
    return removed, kept_total


if __name__ == "__main__":
    for p in sys.argv[1:]:
        r, k = strip(p)
        print(f"{p}: removed {r} hair triangles, {k} kept")
