extends SceneTree
func _initialize():
 var report={}
 for filename in ["lwf_modular_riser_stage_v1","lwf_modular_riser_speaker_v1"]:
  var doc=GLTFDocument.new()
  var state=GLTFState.new()
  assert(doc.append_from_file("../runtime/"+filename+".glb",state)==OK)
  var scene=doc.generate_scene(state)
  root.add_child(scene)
  var meshes=scene.find_children("*","MeshInstance3D",true,false)
  assert(meshes.size()==(4 if filename.contains("stage") else 1))
  assert(scene.find_children("*","CollisionObject3D",true,false).is_empty())
  assert(scene.find_children("*","AnimationPlayer",true,false).is_empty())
  var items=[]
  for node in meshes:
   assert(node.position.is_zero_approx() and node.scale.is_equal_approx(Vector3.ONE))
   var mesh=node.mesh
   assert(mesh.get_surface_count()==1)
   var material=mesh.surface_get_material(0)
   assert(material is StandardMaterial3D and material.albedo_texture!=null)
   var a=mesh.surface_get_arrays(0)
   items.append({"name":str(node.name),"aabb_godot":str(mesh.get_aabb()),"triangles":a[Mesh.ARRAY_INDEX].size()/3,"surfaces":1,"palette_size":str(material.albedo_texture.get_size())})
  report[filename]=items
 var f=FileAccess.open("res://import-report.json",FileAccess.WRITE)
 f.store_string(JSON.stringify(report,"  "))
 print("RISER_GODOT_IMPORT_PASSED")
 quit()
