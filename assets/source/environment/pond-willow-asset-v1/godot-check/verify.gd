extends SceneTree
func _initialize():
 var doc=GLTFDocument.new()
 var state=GLTFState.new()
 assert(doc.append_from_file("../runtime/lwf_pond_willow_v1.glb",state)==OK)
 var scene=doc.generate_scene(state)
 root.add_child(scene)
 var records=[]
 var meshes=scene.find_children("*","MeshInstance3D",true,false)
 assert(meshes.size()==2)
 assert(scene.find_children("*","CollisionObject3D",true,false).is_empty())
 assert(scene.find_children("*","AnimationPlayer",true,false).is_empty())
 for node in meshes:
  assert(node.position.is_zero_approx() and node.scale.is_equal_approx(Vector3.ONE))
  var mesh=node.mesh
  assert(mesh.get_surface_count()==1)
  var material=mesh.surface_get_material(0)
  assert(material is StandardMaterial3D and material.albedo_texture!=null)
  var a=mesh.surface_get_arrays(0)
  records.append({"name":str(node.name),"aabb_godot":str(mesh.get_aabb()),"triangles":a[Mesh.ARRAY_INDEX].size()/3,"surfaces":1,"palette_size":str(material.albedo_texture.get_size()),"cull_mode":material.cull_mode,"roughness":material.roughness})
 var report={"engine":Engine.get_version_info(),"meshes":records,"no_collision_or_animation_nodes":true}
 var f=FileAccess.open("res://import-report.json",FileAccess.WRITE)
 f.store_string(JSON.stringify(report,"  "))
 print("WILLOW_GODOT_IMPORT_PASSED")
 quit()
