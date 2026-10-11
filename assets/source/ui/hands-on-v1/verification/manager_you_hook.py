# farm_scene hook: a hi-vis steward standing as "you" (the installed steward rig body, static) near the bar walk
before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath="C:/Projects/festival-tycoon/game/assets/characters/lwf_steward_male_rigged_test_v1.glb")
for o in [o for o in bpy.data.objects if o not in before]:
    if o.name.startswith("Icosphere"): bpy.data.objects.remove(o); continue
    if o.parent is None: o.location = gd(6.5, 0, 6.0); o.rotation_euler = (0, 0, math.radians(200))
