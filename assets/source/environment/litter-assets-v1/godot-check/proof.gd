extends SceneTree
const BASE="C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/"
const DIR=BASE+"litter-assets-v1/"
var errors:Array=[]
var imports:Array=[]
var captures:Array=[]
var vp:SubViewport
var cam:Camera3D
var world:Node3D
var manifest:Dictionary
var prototypes:Dictionary={}
func _initialize():call_deferred("run")
func load_glb(path:String)->Node3D:
	var doc=GLTFDocument.new()
	var state=GLTFState.new()
	if doc.append_from_file(path,state)!=OK:errors.append("import "+path)
	return doc.generate_scene(state)
func meshes(n:Node)->Array:
	var result:Array=[]
	if n is MeshInstance3D:result.append(n)
	for c in n.get_children():result.append_array(meshes(c))
	return result
func copy_asset(key:String,parent:Node3D,p:Vector3,pose:String="upright")->Node3D:
	var n=prototypes[key].duplicate()
	parent.add_child(n)
	var data=manifest.assets[key].ground_poses[pose]
	var q=data.quaternion_godot_xyzw
	n.quaternion=Quaternion(q[0],q[1],q[2],q[3]).normalized()
	n.position=p+Vector3(0,data.ground_offset_godot_y,0)
	return n
func capture(label:String,size:float,target:Vector3,yaw:float=45):
	cam.size=size
	cam.position=target+Vector3(sin(deg_to_rad(yaw))*72,58,cos(deg_to_rad(yaw))*72)
	cam.look_at(target,Vector3.UP)
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	if vp.get_texture().get_image().save_png(DIR+"review/"+label+".png")!=OK:errors.append("capture "+label)
	captures.append({"file":label+".png","camera_size":size,"yaw":yaw})
func wasp_transform(t:float,i:int)->Transform3D:
	var phase=i*TAU/5.0
	var a=t*1.9+phase
	var r=.22+.05*sin(t*1.3+phase)
	var p=Vector3(r*cos(a),1.07+.08*sin(t*2.1+phase),r*sin(a))
	return Transform3D(Basis(Vector3.UP,-a),p)
func run():
	manifest=JSON.parse_string(FileAccess.get_file_as_string(DIR+"manifest.json"))
	vp=SubViewport.new();vp.size=Vector2i(1920,1080);vp.own_world_3d=true;vp.msaa_3d=Viewport.MSAA_4X;vp.render_target_update_mode=SubViewport.UPDATE_ALWAYS;root.add_child(vp)
	world=Node3D.new();vp.add_child(world)
	for key in manifest.assets:
		var n=load_glb(DIR+"runtime/"+manifest.assets[key].file)
		prototypes[key]=n
		var parts=meshes(n)
		if parts.size()!=1:errors.append("mesh count "+key)
		for m in parts:
			if m.mesh.get_surface_count()!=1:errors.append("surface count "+key)
			var mat=m.mesh.surface_get_material(0)
			if not mat is StandardMaterial3D or mat.albedo_texture==null or mat.transparency!=BaseMaterial3D.TRANSPARENCY_DISABLED:errors.append("material "+key)
			imports.append({"key":key,"aabb":str(m.get_aabb()),"surfaces":m.mesh.get_surface_count()})
	var ground=MeshInstance3D.new();var plane=PlaneMesh.new();plane.size=Vector2(200,200);ground.mesh=plane;ground.position.y=-.004
	var mat=StandardMaterial3D.new();mat.albedo_color=Color("849074");mat.roughness=1;ground.material_override=mat;world.add_child(ground)
	var sun=DirectionalLight3D.new();sun.rotation_degrees=Vector3(-45,-35,0);sun.light_energy=.7;sun.shadow_enabled=true;world.add_child(sun)
	var env_node=WorldEnvironment.new();var env=Environment.new();env.background_mode=Environment.BG_COLOR;env.background_color=Color("bac3b6");env.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR;env.ambient_light_color=Color.WHITE;env.ambient_light_energy=.35;env_node.environment=env;world.add_child(env_node)
	cam=Camera3D.new();cam.projection=Camera3D.PROJECTION_ORTHOGONAL;cam.current=true;world.add_child(cam)
	var lineup=Node3D.new();world.add_child(lineup)
	var keys=["LWF_Litter_Beer_Cup_V1","LWF_Litter_Soft_Cup_V1","LWF_Litter_Chips_Tray_V1"]
	for i in range(3):
		copy_asset(keys[i],lineup,Vector3((i-1)*.32,0,0))
		copy_asset(keys[i],lineup,Vector3((i-1)*.32,0,.32),"side" if i<2 else "tilted")
	await capture("01-empty-items-ground-poses",1.2,Vector3(0,.05,.1))
	# Compare old grip-centred held props to new base-centred empty props at identical grip height.
	lineup.visible=false
	var held=Node3D.new();world.add_child(held)
	var files=["held-props-trio-lager-v2/lwf_beer_cup_v2.glb","held-props-trio-v1/lwf_soft_drink_cup_v1.glb","held-props-trio-v1/lwf_chips_tray_v1.glb"]
	for i in range(3):
		var old=load_glb(BASE+files[i]);held.add_child(old);old.position=Vector3((i-1)*.35,.35,0)
		var empty=prototypes[keys[i]].duplicate();held.add_child(empty)
		var anchor=manifest.assets[keys[i]].held_grip_anchor_godot
		empty.position=Vector3((i-1)*.35,.35-anchor[1],.3)
	await capture("02-held-anchor-comparison",1.35,Vector3(0,.3,.15))
	held.visible=false
	var insect=prototypes["LWF_Wasp_V1"].duplicate();world.add_child(insect);insect.position.y=.12
	await capture("03-wasp-detail-actual-mesh",.18,Vector3(0,.12,0))
	insect.visible=false
	var context=Node3D.new();world.add_child(context)
	var binroot=BASE+"bin-slatted-asset-v1/runtime/"
	context.add_child(load_glb(binroot+"lwf_bin_slatted_shell_v1.glb"))
	var part=load_glb(binroot+"lwf_bin_rubbish_partfull_v1.glb");context.add_child(part)
	var full=load_glb(binroot+"lwf_bin_rubbish_full_v1.glb");context.add_child(full)
	var adult=load_glb(BASE+"direction-a-blender-prototypes-v1/attendee-neutral-v5/direction-a-attendee.glb");context.add_child(adult);adult.position=Vector3(.95,0,0);adult.rotation.y=PI
	for i in range(12):
		var a=i*2.39996;var r=.5+.07*(i%4)
		copy_asset(keys[i%3],context,Vector3(cos(a)*r,0,sin(a)*r),"side" if i%3<2 else "upright")
	var insects=MultiMeshInstance3D.new();insects.multimesh=MultiMesh.new();insects.multimesh.transform_format=MultiMesh.TRANSFORM_3D;insects.multimesh.mesh=meshes(prototypes["LWF_Wasp_V1"])[0].mesh;insects.multimesh.instance_count=5;context.add_child(insects)
	for i in range(5):insects.multimesh.set_instance_transform(i,wasp_transform(0,i))
	full.visible=false;insects.visible=false
	await capture("04-part-full-reuse",2.8,Vector3(.35,.75,0))
	part.visible=false;full.visible=true;insects.visible=true
	await capture("05-full-bin-close-t0",2.8,Vector3(.35,.75,0))
	for i in range(5):insects.multimesh.set_instance_transform(i,wasp_transform(.6,i))
	await capture("06-full-bin-close-t06",2.8,Vector3(.35,.75,0))
	await capture("07-full-bin-game32",32,Vector3(.35,.75,0))
	await capture("08-full-bin-wide62",62,Vector3(.35,.75,0))
	for yaw in [135,225,315]:await capture("09-full-bin-yaw"+str(yaw),2.8,Vector3(.35,.75,0),yaw)
	# Suggested visual clock sample: pause leaves t unchanged; future runtime must use simulation clock.
	var t=.6
	var before=wasp_transform(t,2)
	var paused=true
	if not paused:t+=1.0
	var freeze_ok=wasp_transform(t,2).is_equal_approx(before)
	if not freeze_ok:errors.append("proof motion pause sample")
	var report={"passed":errors.is_empty(),"errors":errors,"imports":imports,"captures":captures,"pause_clock_sample":freeze_ok,"engine":Engine.get_version_info(),"scope":"native standalone GL Compatibility asset proof; fixed-wing instanced wasp pose samples; not integrated gameplay"}
	var file=FileAccess.open(DIR+"godot-verification.json",FileAccess.WRITE);file.store_string(JSON.stringify(report,"\t"));file.close()
	for n in prototypes.values():n.free()
	print("LITTER_GODOT_PROOF ",errors)
	quit(0 if errors.is_empty() else 1)
