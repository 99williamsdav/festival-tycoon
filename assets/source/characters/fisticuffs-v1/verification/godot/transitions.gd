extends SceneTree
var results={}
func _initialize():
    call_deferred("check")
func check():
    for sex in ["male","female"]:
        for role in ["attendee","steward","medic","maintenance","sound"]:
            var a=load("res://assets/lwf_%s_%s_rigged_test_v1.glb"%[role,sex]).instantiate()
            root.add_child(a)
            var sk=a.find_children("*","Skeleton3D",true,false)[0]
            var ap=a.find_children("*","AnimationPlayer",true,false)[0]
            ap.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
            ap.add_animation_library("f",load("res://assets/lwf_attendee_%s_fisticuffs_v1.tres"%sex))
            var foot=sk.get_bone_global_rest(sk.find_bone("LeftFoot")).origin
            var resthand=sk.get_bone_global_rest(sk.find_bone("RightHand")).origin
            var r={"interruptions":0,"max_foot_motion":0.0,"max_final_hand_error":0.0,"pause_pose_error":0.0}
            ap.play("walk");ap.seek(.31,true);ap.advance(0)
            ap.play("f/fight_enter",.125)
            for i in range(24):ap.advance(1.0/60.0)
            sk.force_update_all_bone_transforms()
            assert(sk.get_bone_global_pose(sk.find_bone("LeftFoot")).origin.distance_to(foot)<.001)
            for clip in ["fight_swing_l","fight_swing_r","fight_flinch","fight_guard"]:
                for fraction in [.15,.42,.75]:
                    ap.play("f/"+clip);ap.seek(ap.get_animation("f/"+clip).length*fraction,true);ap.advance(0)
                    sk.force_update_all_bone_transforms()
                    var before=[]
                    for bone in sk.get_bone_count():before.append(sk.get_bone_global_pose(bone))
                    ap.speed_scale=0
                    ap.advance(.5)
                    sk.force_update_all_bone_transforms()
                    for bone in sk.get_bone_count():
                        var p=sk.get_bone_global_pose(bone)
                        r.pause_pose_error=max(r.pause_pose_error,p.origin.distance_to(before[bone].origin))
                    ap.speed_scale=1
                    ap.play("f/fight_stand_down",.125)
                    for i in range(24):
                        ap.advance(1.0/60.0);sk.force_update_all_bone_transforms()
                        r.max_foot_motion=max(r.max_foot_motion,sk.get_bone_global_pose(sk.find_bone("LeftFoot")).origin.distance_to(foot))
                    r.max_final_hand_error=max(r.max_final_hand_error,sk.get_bone_global_pose(sk.find_bone("RightHand")).origin.distance_to(resthand))
                    r.interruptions+=1
            assert(r.max_foot_motion<.001)
            assert(r.max_final_hand_error<.001)
            assert(r.pause_pose_error<.00001)
            results[role+"-"+sex]=r
            a.queue_free()
            await process_frame
    FileAccess.open("res://../native-transitions.json",FileAccess.WRITE).store_string(JSON.stringify(results,"  "))
    print("NATIVE_TRANSITIONS_COMPLETE")
    quit()

