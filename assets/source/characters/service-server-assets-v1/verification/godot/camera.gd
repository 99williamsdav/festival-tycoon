extends SceneTree
func _initialize():
    call_deferred("check")
func check():
    root.size = Vector2i(1280,720)
    var cam = Camera3D.new()
    cam.projection = Camera3D.PROJECTION_ORTHOGONAL
    cam.size = 62
    root.add_child(cam)
    var p = cam.get_camera_projection()
    print("CAMERA_CHECK width=",2.0/p.x.x," height=",2.0/p.y.y," keep_aspect=",cam.keep_aspect)
    quit()
