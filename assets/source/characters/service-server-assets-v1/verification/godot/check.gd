extends SceneTree
var results = {}
func _initialize():
    call_deferred("check")
func check():
    for role in ["bar", "food"]:
        for sex in ["male", "female"]:
            var key = "lwf_server_%s_%s_v1" % [role, sex]
            var scene = load("res://assets/%s.glb" % key)
            assert(scene is PackedScene)
            var actor = scene.instantiate()
            root.add_child(actor)
            var sk = actor.find_children("*", "Skeleton3D", true, false)[0]
            var ap = actor.find_children("*", "AnimationPlayer", true, false)[0]
            var socket = actor.find_child("LWF_RightHand_Cup" if role == "bar" else "LWF_RightHand_Food", true, false)
            assert(sk.get_bone_count() == 17)
            assert(socket != null)
            var r = {"bones": sk.get_bone_count(), "animations": Array(ap.get_animation_list()), "samples": {}, "foot_motion_max": 0.0, "prop_clearance": {}}
            var baseline = sk.get_bone_global_pose(sk.find_bone("LeftFoot")).origin
            for clip in ap.get_animation_list():
                if clip == "RESET": continue
                var anim = ap.get_animation(clip)
                ap.play(clip)
                var max_tilt = 0.0
                var max_foot = 0.0
                for i in range(25):
                    ap.seek(anim.length * i / 24.0, true)
                    sk.force_update_all_bone_transforms()
                    await process_frame
                    var foot = sk.get_bone_global_pose(sk.find_bone("LeftFoot")).origin
                    max_foot = max(max_foot, foot.distance_to(baseline))
                    if "offer" in clip:
                        var up = socket.global_transform.basis.y.normalized()
                        max_tilt = max(max_tilt, rad_to_deg(up.angle_to(Vector3.UP)))
                r.samples[clip] = {"duration":anim.length, "max_foot_motion":max_foot, "max_offer_tilt":max_tilt}
                r.foot_motion_max = max(r.foot_motion_max, max_foot)
            var offer = "server_offer"
            ap.play(offer)
            ap.seek(ap.get_animation(offer).length,true)
            sk.force_update_all_bone_transforms()
            await process_frame
            r.offer_socket_actor_local = Array([socket.global_position.x, socket.global_position.y, socket.global_position.z])
            var items = ["lwf_beer_cup_v2", "lwf_soft_drink_cup_v1"] if role == "bar" else ["lwf_chips_tray_v1","lwf_pizza_plate_v1","lwf_curry_tray_v1"]
            for item in items:
                var prop = load("res://assets/%s.glb" % item).instantiate()
                socket.add_child(prop)
                prop.transform = Transform3D.IDENTITY
                var min_y = 100.0
                for mesh in prop.find_children("*", "MeshInstance3D", true, false):
                    var box = mesh.get_aabb()
                    for i in range(8):
                        var p = mesh.global_transform * box.get_endpoint(i)
                        min_y = min(min_y, p.y)
                r.prop_clearance[item] = min_y + (0.0 if role == "bar" else 0.47) - (1.1 if role == "bar" else 1.235)
                prop.queue_free()
            assert(r.foot_motion_max < 0.001)
            results[key] = r
            actor.queue_free()
            await process_frame
    var van = load("res://assets/lwf_food_van_chassis_open_service_v1.glb").instantiate()
    root.add_child(van)
    assert(van.find_children("*","MeshInstance3D",true,false).size() > 0)
    var file = FileAccess.open("res://../godot-import-check.json",FileAccess.WRITE)
    file.store_string(JSON.stringify(results,"\t"))
    print("NATIVE_SERVER_CHECK_COMPLETE ", JSON.stringify(results))
    quit()

