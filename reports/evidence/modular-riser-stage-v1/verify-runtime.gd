extends SceneTree
func _initialize():
 var report = {}
 for filename in ["lwf_modular_riser_stage_v1", "lwf_modular_riser_speaker_v1"]:
  var packed = load("res://assets/environment/modular-riser-v1/" + filename + ".glb")
  assert(packed is PackedScene)
  var scene = packed.instantiate()
  root.add_child(scene)
  var meshes = scene.find_children("*", "MeshInstance3D", true, false)
  assert(meshes.size() == (4 if filename.contains("stage") else 1))
  for kind in ["CollisionObject3D", "NavigationRegion3D", "NavigationObstacle3D", "AnimationPlayer", "AudioStreamPlayer3D"]:
   assert(scene.find_children("*", kind, true, false).is_empty())
  var triangles = 0
  for node in meshes:
   assert(node.transform.is_equal_approx(Transform3D.IDENTITY))
   assert(node.mesh.get_surface_count() == 1)
   var material = node.get_active_material(0)
   assert(material is StandardMaterial3D)
   assert(material.albedo_texture.get_size() == Vector2(128, 8))
   triangles += node.mesh.surface_get_arrays(0)[Mesh.ARRAY_INDEX].size() / 3
  assert(triangles == (3396 if filename.contains("stage") else 308))
  report[filename] = {"surfaces": meshes.size(), "triangles": triangles, "identity_mesh_transforms": true, "visual_only": true, "palette": [128, 8]}
 var file = FileAccess.open(OS.get_cmdline_user_args()[0], FileAccess.WRITE)
 file.store_string(JSON.stringify(report, "  "))
 print("RISER_RUNTIME_RESOURCES_PASSED")
 quit()
