extends SceneTree
const R = "C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/cow-asset-v3/"
var vp: SubViewport
var cam: Camera3D
var cow: Node3D
var captures = []
func _initialize():
	call_deferred("run")
func load_glb(path):
	var doc = GLTFDocument.new()
	var state = GLTFState.new()
	assert(doc.append_from_file(path,state)==OK)
	return doc.generate_scene(state)
func shot(label, size, position, target):
	cam.size=size
	cam.position=position
	cam.look_at(target,Vector3.UP)
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	assert(vp.get_texture().get_image().save_png(R+"review/"+label+".png")==OK)
	captures.append(label)
func run():
	vp=SubViewport.new()
	vp.size=Vector2i(1600,1000)
	vp.msaa_3d=Viewport.MSAA_4X
	vp.own_world_3d=true
	vp.render_target_update_mode=SubViewport.UPDATE_ALWAYS
	root.add_child(vp)
	var world=Node3D.new()
	vp.add_child(world)
	cow=load_glb(R+"runtime/lwf_cow_v3.glb")
	world.add_child(cow)
	var skeleton=cow.find_children("*","Skeleton3D",true,false)[0]
	var player=cow.find_children("*","AnimationPlayer",true,false)[0]
	var floor_mesh=MeshInstance3D.new()
	var plane=PlaneMesh.new()
	plane.size=Vector2(200,200)
	floor_mesh.mesh=plane
	floor_mesh.position.y=-.006
	var fm=StandardMaterial3D.new()
	fm.albedo_color=Color("a5ac90")
	fm.roughness=1
	floor_mesh.material_override=fm
	world.add_child(floor_mesh)
	var sun=DirectionalLight3D.new()
	sun.rotation_degrees=Vector3(-45,-35,0)
	sun.light_energy=.45
	sun.shadow_enabled=true
	world.add_child(sun)
	var we=WorldEnvironment.new()
	var env=Environment.new()
	env.background_mode=Environment.BG_COLOR
	env.background_color=Color("ded9c9")
	env.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color=Color.WHITE
	env.ambient_light_energy=.25
	we.environment=env
	world.add_child(we)
	cam=Camera3D.new()
	cam.projection=Camera3D.PROJECTION_ORTHOGONAL
	cam.current=true
	world.add_child(cam)
	var target=Vector3(.2,.85,0)
	await shot("model-three-quarter",2.4,Vector3(4,2.7,6),target)
	await shot("model-side",2.3,Vector3(.2,.9,8),target)
	await shot("model-front",2.3,Vector3(8,1,0),target)
	await shot("model-rear",2.3,Vector3(-8,1,0),target)
	for yaw in [45,135,225,315]:
		var pos=target+Vector3(sin(deg_to_rad(yaw))*72,58,cos(deg_to_rad(yaw))*72)
		await shot("quarter-"+str(yaw)+"-close",3.2,pos,target)
		await shot("quarter-"+str(yaw)+"-game32",32,pos,target)
	for anim in player.get_animation_list():
		if anim=="RESET":continue
		player.play(anim)
		player.seek(.5,true)
		player.pause()
		await shot("pose-"+anim.replace("/","-"),2.4,Vector3(4,2.7,6),target)
	player.stop()
	player.play("Idle")
	player.seek(0,true)
	player.pause()
	if player.has_animation("RESET"):
		player.play("RESET")
		player.advance(0)
		player.stop()
	var adult=load_glb("C:/Projects/festival-tycoon/game/assets/characters/lwf_attendee_male_relaxed_v2.glb")
	adult.add_child(load_glb("C:/Projects/festival-tycoon/game/assets/characters/lwf_hair_male_default_v1.glb"))
	world.add_child(adult)
	adult.position=Vector3(2.2,0,0)
	adult.rotation.y=PI
	await shot("scale-attendee",3.0,Vector3(3,2.3,8),Vector3(.7,.9,0))
	var output=FileAccess.open(R+"godot-verification.json",FileAccess.WRITE)
	output.store_string(JSON.stringify({"engine":Engine.get_version_info(),"bones":skeleton.get_bone_count(),"animations":player.get_animation_list(),"captures":captures,"passed":true,"scope":"Standalone native import/render, not gameplay integration"},"  "))
	print("COW_GODOT_PASSED")
	quit()
