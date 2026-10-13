extends SceneTree
func _initialize():
    var actor=load("res://assets/lwf_attendee_male_rigged_test_v1.glb").instantiate()
    root.add_child(actor)
    var ap=actor.find_children("*","AnimationPlayer",true,false)[0]
    var sk=actor.find_children("*","Skeleton3D",true,false)[0]
    print("BASE CLIPS ",ap.get_animation_list())
    for name in ["RESET","idle","walk"]:
        if not ap.has_animation(name):continue
        var a=ap.get_animation(name)
        for i in a.get_track_count():
            if "Foot" in str(a.track_get_path(i)) or "Hips" in str(a.track_get_path(i)):
                print(name," ",a.track_get_path(i)," TYPE ",a.track_get_type(i)," KEY ",a.track_get_key_value(i,0))
    var donor=load("res://assets/lwf_attendee_male_fisticuffs_clips_v1.glb").instantiate()
    root.add_child(donor)
    var lib=donor.find_children("*","AnimationPlayer",true,false)[0].get_animation_library("")
    ap.add_animation_library("f",lib)
    ap.play("walk");ap.seek(.3,true);ap.advance(0);sk.force_update_all_bone_transforms()
    print("WALK FOOT ",sk.get_bone_global_pose(sk.find_bone("LeftFoot")).origin)
    ap.play("f/fight_guard");ap.seek(.1,true);ap.advance(0);sk.force_update_all_bone_transforms()
    print("GUARD AFTER WALK FOOT ",sk.get_bone_global_pose(sk.find_bone("LeftFoot")).origin," REST ",sk.get_bone_global_rest(sk.find_bone("LeftFoot")).origin)
    quit()

