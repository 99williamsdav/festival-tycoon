# farm_scene hook (extra= after rainscene.py): the straw reference ground and the INSTALLED grid-mat panels laid by the run rules.
# OPTS: show = straw | mats
# The farm scene's grass tiles sit a few cm above 0 in places, so the straw plane (6 cm) and the mats (4 cm) are lifted to clear them.
SHOW = OPTS.get("show", "straw"); V = "C:/Projects/festival-tycoon/assets/source/environment/rain-ground-v1/verification/"
if SHOW == "straw":
    W_, H_, cx, cz = 21.0, 8.0, 12.0, 12.0
    me = bpy.data.meshes.new("straw_ground"); me.from_pydata([gd(cx - W_ / 2, 0.06, cz + H_ / 2), gd(cx + W_ / 2, 0.06, cz + H_ / 2), gd(cx + W_ / 2, 0.06, cz - H_ / 2), gd(cx - W_ / 2, 0.06, cz - H_ / 2)], [], [(0, 1, 2, 3)])
    uv = me.uv_layers.new(); [setattr(uv.data[i], "uv", c) for i, c in enumerate(((0, 0), (1, 0), (1, 1), (0, 1)))]
    m = bpy.data.materials.new("straw_ground"); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    t = m.node_tree.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(V + "straw_tileset_ground.png")
    m.node_tree.links.new(t.outputs[0], b.inputs["Base Color"]); m.node_tree.links.new(t.outputs[1], b.inputs["Alpha"]); m.blend_method = 'BLEND'
    b.inputs["Roughness"].default_value = 0.45; me.materials.append(m); o = bpy.data.objects.new("straw_ground", me); sc.collection.objects.link(o)
    WET_SKIP.add(m.name)
else:
    MAT = "environment/lwf_track_mat_grid_v1.glb"
    def run(axis, fixed, a, b):
        """a straight run of 2.0 x 1.0 m panels: one per metre, long side across the path"""
        lo, hi = min(a, b), max(a, b)
        for k in range(int(round(hi - lo))):
            c = lo + k + 0.5
            if axis == "x": place(MAT, c, fixed, 90, y=0.04)
            else: place(MAT, fixed, c, 0, y=0.04)
    # west: off the track edge (x -2) along z 22 to the corner square at x -10..-8, then north to the stage crowd
    run("x", 22.0, -2.0, -10.0); run("z", -9.0, 21.0, 13.0)
    # east: off the track edge (x 2) along z 22 to the corner at x 21.25..23.25, then north to the toilets
    run("x", 22.0, 2.0, 23.0); run("z", 22.25, 21.0, 8.0)
    for x, z, r in ((-4.5, 19.8, 1.4), (8.0, 20.2, 1.6), (20.0, 15.5, 1.2), (-11.2, 17.5, 1.2), (14.0, 24.0, 1.0)): mudpatch(x, z, r, int(x * 13 + z), pools=1, elong=0.7)
