# farm_scene hook (after the rain scene's rainscene.py, as extra=): the INSTALLED lwf_stretch_tent_v1.glb at (8, 0, 10), yaw 0,
# with guests packed inside its shelter rectangle. OPTS: fade = 0.3 sets LWF_StretchTent_Canvas alpha (the in-game fade); under = sun|rain
import json
TENT = "C:/Projects/festival-tycoon/game/assets/environment/lwf_stretch_tent_v1.glb"
LAY = json.load(open("C:/Projects/festival-tycoon/assets/source/environment/stretch-tent-v1/out/lwf_stretch_tent_layout_v1.json"))
TX, TZ, TY = 8.0, 10.0, float(OPTS.get("tyaw", 0))
before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=TENT)
for o in [o for o in bpy.data.objects if o not in before]:
    if o.parent is None: o.location = gd(TX, 0, TZ); o.rotation_euler = (0, 0, math.radians(TY))
fade = float(OPTS.get("fade", 1))
for m in bpy.data.materials:
    if m.name.startswith("LWF_StretchTent_Canvas"):
        WET_SKIP.add(m.name) if RMODE == "dry" else None
        if fade < 1:
            m.node_tree.nodes["Principled BSDF"].inputs["Alpha"].default_value = fade; m.blend_method = 'BLEND'
under = OPTS.get("under", "sun"); gq = random.Random(21); R_ = LAY["shelter_rect_m"]
for i in range(int(OPTS.get("n", 14))):
    x = TX + gq.uniform(*R_["x"]); z = TZ + gq.uniform(*R_["z"])
    guest(x, z, gq.uniform(0, 360), ("huddle" if i % 3 else "idle") if under == "rain" else ("idle" if i % 4 else "walk"), wet=(under == "rain" and i % 3 == 0))
