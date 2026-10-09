# exec()'d by farm_scene.py: the installed curry van, assembled as Main.InstantiateImmersionVendor does, beside chips and pizza.
GA = "C:/Projects/festival-tycoon/game/assets/environment/"
for _o in list(sc.collection.objects):
    if _o.instance_type == 'COLLECTION' and _o.instance_collection and _o.instance_collection.name.startswith("lwf_food_van"):
        bpy.data.objects.remove(_o)
def imp(path, parent, at):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=path); new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        if o.parent is None: o.parent = parent; o.location = gd(*at)
        for c in list(o.users_collection): c.objects.unlink(o)
        sc.collection.objects.link(o)
    return [o for o in new if o.parent is parent]
def van(art, sign, x, z, extra=False):
    root = bpy.data.objects.new("v_" + art, None); sc.collection.objects.link(root); root.location = gd(x, 0, z)
    asm = bpy.data.objects.new("asm_" + art, None); sc.collection.objects.link(asm); asm.parent = root; asm.location = gd(-2.15, 0, 0)
    pal = bpy.data.images.load(GA + f"lwf_food_van_palette_{art}_v1.png")
    imp(GA + "lwf_food_van_chassis_v1.glb", asm, (0, 0, 0)); fas = imp(GA + "lwf_food_van_fascia_v1.glb", asm, (2.15, 2.49, 1.18))[0]
    for o in [o for o in asm.children_recursive if o.type == 'MESH']:                   # ApplyFoodVanLivery
        for i, m in enumerate(o.data.materials):
            if m and m.name.startswith("LWF_FoodVan_MattePalette"):
                m2 = m.copy(); o.data.materials[i] = m2
                for nd in m2.node_tree.nodes:
                    if nd.type == 'TEX_IMAGE': nd.image = pal; nd.interpolation = 'Closest'
    imp(GA + f"lwf_food_van_name_panel_{art}_v1.glb", fas, (0, 0, 0.056))
    imp(GA + f"lwf_food_van_sign_{sign}_v1.glb", asm, (2.10, 2.75, 0))
    if extra:
        imp(GA + "lwf_food_van_kitchen_dressing_v1.glb", asm, (0, 0, 0)); imp(GA + "lwf_food_van_menu_board_korma_v1.glb", asm, (4.10, 0, 2.30))
        steam = bpy.data.materials.new("steam"); steam.use_nodes = True; b = steam.node_tree.nodes["Principled BSDF"]; b.inputs["Alpha"].default_value = 0.45; steam.blend_method = 'BLEND'
        for e in [o for o in bpy.data.objects if o.name.startswith("LWF_FoodVan_SteamVent_") and o.type == 'EMPTY']:
            if getattr(e, "_done", False): continue
            for k, (dy, r) in enumerate(((0.25, 0.14), (0.55, 0.2), (0.9, 0.26))):         # stand-in puffs at the markers
                bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r); s = bpy.context.object; s.data.materials.append(steam)
                s.location = e.matrix_world.translation + mathutils.Vector((0.05 * k, 0, dy))
            e.name = "done_" + e.name
    return root
import mathutils
bpy.context.view_layer.update()
van("chip_block", "chips", 4.0, 8.0); van("pizza", "pizza", 11.5, 8.0); van("korma", "curry", 19.0, 8.0, extra=True)
if OPTS.get("queue", "1") == "1":
    for x, n in ((4.0, 3), (11.5, 2), (19.0, 8)):
        for i in range(n): person(x + 0.15 * math.sin(i * 1.7), 8.0 + 2.3 + i * 0.85, 180 + 15 * math.sin(i))
