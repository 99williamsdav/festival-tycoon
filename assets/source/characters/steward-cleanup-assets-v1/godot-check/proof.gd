extends SceneTree
const DIR="C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/steward-cleanup-assets-v1/"
const POSE=preload("cleanup_pose.gd")
var errors:Array=[]
var checks:Array=[]
var captures:Array=[]
var camera:Camera3D
var vp:SubViewport
var world:Node3D
func _initialize():call_deferred("run")
func glb(p:String)->Node3D:
	var d=GLTFDocument.new();var state=GLTFState.new()
	if d.append_from_file(p,state)!=OK:errors.append("import "+p)
	return d.generate_scene(state)
func capture(label:String,size:float,target:Vector3,yaw:float=45):
	camera.size=size;camera.position=target+Vector3(sin(deg_to_rad(yaw))*72,58,cos(deg_to_rad(yaw))*72);camera.look_at(target,Vector3.UP)
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	if vp.get_texture().get_image().save_png(DIR+"review/"+label+".png")!=OK:errors.append("capture "+label)
	captures.append(label)
func paint_body(root:Node3D):
	var im=Image.load_from_file("C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/person-role-assets-v1/palettes/lwf_steward_body_palette_v1.png")
	var tex=ImageTexture.create_from_image(im)
	for mesh in root.find_children("*","MeshInstance3D",true,false):
		if mesh.name=="FittedVest":continue
		var mat=mesh.mesh.surface_get_material(0).duplicate()
		mat.albedo_texture=tex;mat.texture_filter=BaseMaterial3D.TEXTURE_FILTER_NEAREST
		mesh.material_override=mat
func run():
	var manifest=JSON.parse_string(FileAccess.get_file_as_string(DIR+"manifest.json"))
	vp=SubViewport.new();vp.size=Vector2i(1920,1080);vp.own_world_3d=true;vp.msaa_3d=Viewport.MSAA_4X;vp.render_target_update_mode=SubViewport.UPDATE_ALWAYS;root.add_child(vp)
	world=Node3D.new();vp.add_child(world)
	var floor_mesh=MeshInstance3D.new();var plane=PlaneMesh.new();plane.size=Vector2(200,200);floor_mesh.mesh=plane;floor_mesh.position.y=-.004
	var groundmat=StandardMaterial3D.new();groundmat.albedo_color=Color("849074");groundmat.roughness=1;floor_mesh.material_override=groundmat;world.add_child(floor_mesh)
	var sun=DirectionalLight3D.new();sun.rotation_degrees=Vector3(-45,-35,0);sun.light_energy=.7;sun.shadow_enabled=true;world.add_child(sun)
	var envnode=WorldEnvironment.new();var env=Environment.new();env.background_mode=Environment.BG_COLOR;env.background_color=Color("bac3b6");env.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR;env.ambient_light_color=Color.WHITE;env.ambient_light_energy=.35;envnode.environment=env;world.add_child(envnode)
	camera=Camera3D.new();camera.projection=Camera3D.PROJECTION_ORTHOGONAL;camera.current=true;world.add_child(camera)
	var nodes:Dictionary={}
	for sex in ["male","female"]:
		var group=Node3D.new();world.add_child(group);group.position=Vector3(-.7 if sex=="male" else .7,.04,0)
		var visual=Node3D.new();group.add_child(visual)
		var body=glb(DIR+"runtime/lwf_steward_cleanup_"+sex+"_v1.glb");visual.add_child(body);paint_body(body)
		var picker=glb(DIR+"runtime/lwf_steward_litter_picker_v1.glb");visual.add_child(picker)
		var bag=glb(DIR+"runtime/lwf_steward_bin_bag_v1.glb");visual.add_child(bag)
		var litter=glb("C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/litter-assets-v1/runtime/lwf_litter_soft_cup_v1.glb");group.add_child(litter);litter.rotation.x=PI/2;litter.position=Vector3(0,.010,-.20)
		nodes[sex]={"group":group,"visual":visual,"body":body,"picker":picker,"bag":bag,"litter":litter}
		# Exact target grid including centre; all poses preserve LowerBody/root identity.
		var failures:Array=[]
		for x in [-.25,0,.25]:
			for z in [-.25,0,.25]:
				# Cleanup child can face a target about unchanged actor origin. No actor/litter translation.
				var radius=Vector2(x,z).length()
				for contact_y in [-.015,.01,.04,.055,.10]:
					for extra in [0.0,.075]:
						for step in range(116):
							var phase=step/100.0
							var contact=Vector3(0,contact_y,-radius-extra)
							var result=POSE.apply(body,manifest.variants[sex],phase,contact,picker,bag)
							if not result.right.reachable or not result.left.reachable:failures.append([x,z,contact_y,extra,phase])
							if phase==.5 and result.jaw.distance_to(contact)>.00001:errors.append("contact "+sex)
							if phase>=.85 and phase<=1 and result.jaw.distance_to(result.bag_mouth)>.00001:errors.append("bag target "+sex)
							if result.bag_bottom<.3:errors.append("bag dragging "+sex)
							if not POSE.node(body,"LowerBody").transform.is_equal_approx(Transform3D.IDENTITY):errors.append("feet moved "+sex)
		checks.append({"sex":sex,"target_grid_count":9,"contact_heights":[-.015,.01,.04,.055,.10],"extra_horizontal_offsets":[0,.075],"phases_per_target":116,"target_normalized_by_child_yaw":true,"unreachable_samples":failures})
		if failures.size()>0:errors.append("unreachable arms "+sex)
	for state in [{"label":"01-equipped","phase":-1.0},{"label":"02-ground-reach","phase":.5},{"label":"03-bag-transfer","phase":.85}]:
		for sex in nodes:
			var n=nodes[sex];var p=POSE.apply(n.body,manifest.variants[sex],state.phase,Vector3(0,.010,-.125),n.picker,n.bag)
			n.litter.rotation=Vector3(PI/2,0,0)
			if state.phase>=.5:n.litter.position=p.jaw-n.litter.basis*Vector3(0,.075,0)
			else:n.litter.position=Vector3(0,.010,-.20)
			if state.phase>=.5 and (n.litter.transform*Vector3(0,.075,0)).distance_to(p.jaw)>.00001:errors.append("carried piece not at jaw")
		await capture(state.label,3.4,Vector3(0,.85,0),225)
		if state.phase==.5:
			for yaw in [135,225,315]:await capture("04-reach-yaw"+str(yaw),3.4,Vector3(0,.85,0),yaw)
		if state.phase<0:
			await capture("05-equipped-game32",32,Vector3(0,.85,0))
			await capture("06-equipped-wide62",62,Vector3(0,.85,0))
	# Centre-contact proof (foot roots unchanged), plus real-sized tray alternative.
	for sex in nodes:
		var n=nodes[sex];n.litter.position=Vector3(0,.010,0)
		n.visual.rotation.y=PI
		var local_contact=n.visual.transform.affine_inverse()*(n.litter.transform*Vector3(0,.075,0))
		var result=POSE.apply(n.body,manifest.variants[sex],.5,local_contact,n.picker,n.bag)
		if (n.visual.transform*result.jaw).distance_to(n.litter.transform*Vector3(0,.075,0))>.00001:errors.append("centre world contact")
	await capture("07-centre-contact",3.4,Vector3(0,.85,0),225)
	await capture("08-centre-contact-front",3.4,Vector3(0,.85,0),45)
	var file=FileAccess.open(DIR+"godot-verification.json",FileAccess.WRITE);file.store_string(JSON.stringify({"passed":errors.is_empty(),"errors":errors,"checks":checks,"captures":captures,"engine":Engine.get_version_info(),"scope":"native standalone cleanup geometry/analytic poses; not integrated gameplay; targetgrid contact and arm lengths/groundedfeet"},"\t"));file.close()
	print("CLEANUP_PROOF ",errors)
	quit(0 if errors.is_empty() else 1)
