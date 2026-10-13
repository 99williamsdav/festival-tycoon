extends SceneTree
var result = {}
func _initialize():
    call_deferred("check")
func check():
    for sex in ["male","female"]:
        var donor = load("res://assets/lwf_attendee_%s_fisticuffs_clips_v1.glb" % sex).instantiate()
        root.add_child(donor)
        var dp = donor.find_children("*","AnimationPlayer",true,false)[0]
        var library = dp.get_animation_library("").duplicate(true)
        var names = []
        for n in library.get_animation_list():
            if n != "RESET": names.append(n)
        for role in ["attendee","steward","medic","maintenance","sound"]:
            var actor = load("res://assets/lwf_%s_%s_rigged_test_v1.glb" % [role,sex]).instantiate()
            root.add_child(actor)
            var sk = actor.find_children("*","Skeleton3D",true,false)[0]
            var ap = actor.find_children("*","AnimationPlayer",true,false)[0]
            # glTF removes immutable tracks; restore every missing bone component from actual target rest.
            if role == "attendee":
                for n in names:
                    var anim = library.get_animation(n)
                    anim.loop_mode = Animation.LOOP_LINEAR if n == "fight_guard" else Animation.LOOP_NONE
                    for bone in sk.get_bone_count():
                        var path = NodePath("LWF_Attendee_Rig/Skeleton3D:" + sk.get_bone_name(bone))
                        var rest = sk.get_bone_rest(bone)
                        for type in [Animation.TYPE_POSITION_3D,Animation.TYPE_ROTATION_3D,Animation.TYPE_SCALE_3D]:
                            if anim.find_track(path,type) >= 0: continue
                            var tr = anim.add_track(type)
                            anim.track_set_path(tr,path)
                            var value = rest.origin if type == Animation.TYPE_POSITION_3D else rest.basis.get_rotation_quaternion() if type == Animation.TYPE_ROTATION_3D else rest.basis.get_scale()
                            anim.track_insert_key(tr,0.0,value)
                            anim.track_insert_key(tr,anim.length,value)
                assert(ResourceSaver.save(library,"res://assets/lwf_attendee_%s_fisticuffs_v1.tres" % sex)==OK)
            ap.add_animation_library("fisticuffs",library)
            ap.stop()
            var foot_l = sk.get_bone_global_rest(sk.find_bone("LeftFoot")).origin
            var foot_r = sk.get_bone_global_rest(sk.find_bone("RightFoot")).origin
            var rec = {"bones":sk.get_bone_count(),"clips":{},"max_foot_motion":0.0}
            assert(sk.get_bone_count()==17)
            for n in names:
                var clip = "fisticuffs/"+n
                var anim = ap.get_animation(clip)
                var paths = []
                for t in anim.get_track_count():
                    var path = anim.track_get_path(t)
                    paths.append(str(path))
                    assert(ap.get_node(ap.root_node).has_node(NodePath(str(path).get_slice(":",0))),"Unbound animation path")
                ap.play("walk")
                ap.seek(0.3,true)
                ap.advance(0)
                ap.play(clip)
                var motion = 0.0
                var first_hand = Vector3.ZERO
                for i in range(33):
                    ap.seek(anim.length*i/32.0,true)
                    ap.advance(0)
                    sk.force_update_all_bone_transforms()
                    var fl = sk.get_bone_global_pose(sk.find_bone("LeftFoot")).origin
                    var fr = sk.get_bone_global_pose(sk.find_bone("RightFoot")).origin
                    rec.max_foot_motion=max(rec.max_foot_motion,max(fl.distance_to(foot_l),fr.distance_to(foot_r)))
                    var hand=sk.get_bone_global_pose(sk.find_bone("RightHand")).origin
                    if i==0:first_hand=hand
                    motion=max(motion,hand.distance_to(first_hand))
                rec.clips[n]={"duration":anim.length,"tracks":anim.get_track_count(),"right_hand_motion":motion,"first_path":paths[0]}
            assert(rec.max_foot_motion<0.001)
            assert(rec.clips.fight_swing_r.right_hand_motion>0.10,"Clip not moving target rig")
            result[role+"-"+sex]=rec
            actor.queue_free()
            await process_frame
        donor.queue_free()
        await process_frame
    FileAccess.open("res://../native-import.json",FileAccess.WRITE).store_string(JSON.stringify(result,"  "))
    print("FISTICUFFS_NATIVE_COMPLETE")
    quit()


