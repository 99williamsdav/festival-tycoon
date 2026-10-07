extends SceneTree
var report = {}
func _initialize():
 for filename in ["lwf_pond_polish_static_v1", "lwf_duck_drake_v1", "lwf_duck_brown_v1"]:
  var doc = GLTFDocument.new()
  var state = GLTFState.new()
  var err = doc.append_from_file("../runtime/" + filename + ".glb", state)
  assert(err == OK, "GLB decode failed")
  var scene = doc.generate_scene(state)
  assert(scene != null)
  root.add_child(scene)
  var records = []
  for node in scene.find_children("*", "MeshInstance3D", true, false):
   var mesh = node.mesh
   var record = {"name": str(node.name), "position": str(node.position), "scale": str(node.scale), "aabb": str(mesh.get_aabb()), "surfaces": mesh.get_surface_count()}
   var tris = 0
   for i in range(mesh.get_surface_count()):
    var a = mesh.surface_get_arrays(i)
    tris += a[Mesh.ARRAY_INDEX].size() / 3 if a[Mesh.ARRAY_INDEX] != null else a[Mesh.ARRAY_VERTEX].size() / 3
    assert(mesh.surface_get_material(i) != null)
    if node.name == "PondWater":
     for normal in a[Mesh.ARRAY_NORMAL]: assert(normal.y > .99, "Water faces down")
     for c in a[Mesh.ARRAY_COLOR]: assert(c.a > .99 and c.b < .5, "Corrupted water tint")
     record["water_upward_normals_and_colors"] = true
   record["triangles"] = tris
   records.append(record)
  assert(records.size() == (3 if filename.contains("static") else 1))
  if filename.contains("static"):
   assert(scene.find_child("Ducks",true,false) == null)
  report[filename] = records
  scene.queue_free()
 var f = FileAccess.open("res://import-report.json", FileAccess.WRITE)
 f.store_string(JSON.stringify(report,"  "))
 print("POND_GODOT_IMPORT_PASSED")
 quit()
