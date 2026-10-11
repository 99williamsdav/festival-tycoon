import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
REPO=Path('C:/Projects/festival-tycoon')
files=['environment/lwf_drinks_stall_prototype_v1.glb','environment/lwf_food_van_chassis_v1.glb','characters/lwf_attendee_male_rigged_test_v1.glb','characters/lwf_attendee_female_rigged_test_v1.glb','props/lwf_beer_cup_v2.glb','props/lwf_soft_drink_cup_v1.glb','props/lwf_chips_tray_v1.glb']
out={}
for file in files:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.gltf(filepath=str(REPO/'game/assets'/file))
 meshes=[]
 for o in bpy.context.scene.objects:
  if o.type!='MESH': continue
  points=[o.matrix_world@v.co for v in o.data.vertices]
  def bounds(pts):
   g=[Vector((p.x,p.z,-p.y)) for p in pts]
   return [[round(f(p[a] for p in g),5) for a in range(3)] for f in (min,max)]
  adj={v.index:set() for v in o.data.vertices}
  for e in o.data.edges:
   a,b=e.vertices;adj[a].add(b);adj[b].add(a)
  unseen=set(adj);parts=[]
  while unseen:
   seed=unseen.pop();todo=[seed];part=[seed]
   while todo:
    for n in adj[todo.pop()]&unseen:
     unseen.remove(n);todo.append(n);part.append(n)
   parts.append({'vertices':len(part),'bounds_godot':bounds([points[i] for i in part])})
  meshes.append({'name':o.name,'bounds_godot':bounds(points),'components':parts})
 out[file]=meshes
(ROOT/'asset-measurements.json').write_text(json.dumps(out,indent=2))
print('Measured current runtime geometry:',list(out))

