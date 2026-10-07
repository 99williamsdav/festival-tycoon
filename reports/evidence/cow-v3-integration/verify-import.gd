extends SceneTree
func _initialize():
    var packed = load("res://assets/characters/animals/lwf_cow_v3.glb") as PackedScene
    assert(packed != null, "Cow PackedScene import failed")
    var cow = packed.instantiate()
    root.add_child(cow)
    var meshes = cow.find_children("*", "MeshInstance3D", true, false)
    var skeletons = cow.find_children("*", "Skeleton3D", true, false)
    var players = cow.find_children("*", "AnimationPlayer", true, false)
    assert(meshes.size() == 1 and skeletons.size() == 1 and players.size() == 1)
    var mesh = meshes[0] as MeshInstance3D
    var skeleton = skeletons[0] as Skeleton3D
    var player = players[0] as AnimationPlayer
    assert(mesh.mesh.get_surface_count() == 1 and mesh.skin != null)
    assert(skeleton.get_bone_count() == 19)
    var material = mesh.get_active_material(0) as StandardMaterial3D
    assert(material != null and material.albedo_texture != null)
    var actions = []
    for action in ["Idle", "WalkPreview", "Alert"]:
        assert(player.has_animation(action))
        var animation = player.get_animation(action)
        assert(animation.length > 0 and animation.get_track_count() > 0)
        player.play(action)
        player.seek(animation.length * 0.5, true)
        player.advance(0)
        for i in skeleton.get_bone_count():
            assert(skeleton.get_bone_global_pose(i).origin.is_finite())
        actions.append({"name": action, "length": animation.length, "tracks": animation.get_track_count(), "previewOnly": true})
    var output = FileAccess.open("C:/Projects/festival-tycoon/reports/evidence/cow-v3-integration/native-import.json", FileAccess.WRITE)
    output.store_string(JSON.stringify({"passed":true,"engine":Engine.get_version_info()["string"],"meshes":meshes.size(),"surfaces":mesh.mesh.get_surface_count(),"bones":skeleton.get_bone_count(),"skinPresent":true,"paletteLoaded":true,"actions":actions,"scope":"Reusable asset import and finite midpoint preview poses; no gameplay or locomotion-controller validation."}, "  "))
    print("COW_V3_REPO_IMPORT_OK")
    quit(0)
