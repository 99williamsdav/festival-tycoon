extends SceneTree
const ROOT = "C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/bin-slatted-asset-v1"
var errors: Array = []
var imports: Array = []
var captures: Array = []
var viewport: SubViewport
var camera: Camera3D
func _initialize():
	call_deferred("run")
func load_asset(path: String) -> Node3D:
	var doc = GLTFDocument.new()
	var state = GLTFState.new()
	var code = doc.append_from_file(path,state)
	if code != OK:
		errors.append("Import error: " + path)
		return Node3D.new()
	var node = doc.generate_scene(state)
	return node
func inspect(node: Node):
	if node is MeshInstance3D:
		var material = node.mesh.surface_get_material(0)
		if node.mesh.get_surface_count() != 1:
			errors.append("More than one surface: " + node.name)
		if not material is StandardMaterial3D or material.albedo_texture == null:
			errors.append("Missing palette: " + node.name)
		imports.append({"name":node.name,"surfaces":node.mesh.get_surface_count(),"aabb":str(node.get_aabb())})
	for child in node.get_children(): inspect(child)
func capture(label: String, size: float):
	camera.size = size
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	var image = viewport.get_texture().get_image()
	if image.save_png(ROOT + "/review/godot-" + label + ".png") != OK: errors.append("Capture failed")
	captures.append({"label":label,"camera_size":size,"resolution":[1920,1080]})
func run():
	viewport = SubViewport.new()
	viewport.size = Vector2i(1920,1080)
	viewport.own_world_3d = true
	viewport.msaa_3d = Viewport.MSAA_4X
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	var world = Node3D.new()
	viewport.add_child(world)
	var shell = load_asset(ROOT + "/runtime/lwf_bin_slatted_shell_v1.glb")
	world.add_child(shell)
	inspect(shell)
	var part = load_asset(ROOT + "/runtime/lwf_bin_rubbish_partfull_v1.glb")
	var full = load_asset(ROOT + "/runtime/lwf_bin_rubbish_full_v1.glb")
	world.add_child(part)
	world.add_child(full)
	inspect(part)
	inspect(full)
	part.visible=false
	full.visible=false
	var adult = load_asset("C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/direction-a-blender-prototypes-v1/attendee-neutral-v5/direction-a-attendee.glb")
	world.add_child(adult)
	adult.position=Vector3(.95,0,0)
	adult.rotation.y=PI
	var ground=MeshInstance3D.new()
	var plane=PlaneMesh.new()
	plane.size=Vector2(200,200)
	ground.mesh=plane
	ground.position.y=-.005
	var material=StandardMaterial3D.new()
	material.albedo_color=Color("849074")
	material.roughness=1
	ground.material_override=material
	world.add_child(ground)
	var sun=DirectionalLight3D.new()
	sun.rotation_degrees=Vector3(-45,-35,0)
	sun.light_energy=.7
	sun.shadow_enabled=true
	world.add_child(sun)
	var environment=WorldEnvironment.new()
	var env=Environment.new()
	env.background_mode=Environment.BG_COLOR
	env.background_color=Color("bac3b6")
	env.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color=Color.WHITE
	env.ambient_light_energy=.35
	environment.environment=env
	world.add_child(environment)
	camera=Camera3D.new()
	camera.projection=Camera3D.PROJECTION_ORTHOGONAL
	camera.current=true
	world.add_child(camera)
	var target=Vector3(.35,.8,0)
	camera.position=target+Vector3(sin(PI/4)*72,58,cos(PI/4)*72)
	camera.look_at(target,Vector3.UP)
	for fill in ["empty","part-full","full"]:
		part.visible=fill=="part-full"
		full.visible=fill=="full"
		await capture(fill+"-close",2.5)
		await capture(fill+"-game32",32)
		await capture(fill+"-wide62",62)
	var file=FileAccess.open(ROOT+"/godot-verification.json",FileAccess.WRITE)
	file.store_string(JSON.stringify({"passed":errors.is_empty(),"errors":errors,"imports":imports,"captures":captures,"engine":Engine.get_version_info(),"scope":"Standalone actual GL Compatibility import/render, game-matched yaw45 elevationatan(58/72); not game integration"},"\t"))
	file.close()
	print("BIN_GODOT_PROOF ",errors)
	quit(0 if errors.is_empty() else 1)
