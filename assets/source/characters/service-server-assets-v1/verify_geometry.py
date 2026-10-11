import bpy,json,math
from pathlib import Path
from mathutils import Vector as V,Matrix as M
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent;REPO=Path('C:/Projects/festival-tycoon');manifest=json.loads((R/'manifest.json').read_text())
result={}
def imp(path):
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path));return list(set(bpy.data.objects)-old)
def tree_for(ob):
 deps=bpy.context.evaluated_depsgraph_get();e=ob.evaluated_get(deps);me=e.to_mesh()
 verts=[e.matrix_world@v.co for v in me.vertices];faces=[tuple(p.vertices) for p in me.polygons];e.to_mesh_clear()
 return BVHTree.FromPolygons(verts,faces),verts
for role in ['bar','food']:
 for sex in ['male','female']:
  key=f'lwf_server_{role}_{sex}_v1';bpy.ops.wm.open_mainfile(filepath=str(R/'source'/f'{key}.blend'))
  rig=bpy.data.objects['LWF_Attendee_Rig'];body=bpy.data.objects['LWF_Attendee_Body'];meshes=[o for o in bpy.data.objects if o.type=='MESH']
  rig.location=(.35,-.30,0) if role=='bar' else (0,-.88,.47);rig.rotation_euler.z=math.pi
  orig=imp(REPO/'game/assets/environment/lwf_drinks_stall_prototype_v1.glb' if role=='bar' else R/'runtime/lwf_food_van_chassis_open_service_v1.glb')
  if role=='food':
   for o in orig:o.location.x-=2.15
  context=[]
  for o in orig:
   if o.type=='MESH':context.append((o.name,tree_for(o)[0]))
  sock=bpy.data.objects['LWF_RightHand_Cup' if role=='bar' else 'LWF_RightHand_Food']
  items=['lwf_beer_cup_v2','lwf_soft_drink_cup_v1'] if role=='bar' else ['lwf_chips_tray_v1','lwf_pizza_plate_v1','lwf_curry_tray_v1']
  props={}
  for item in items:
   objs=imp(REPO/'game/assets/props'/f'{item}.glb');props[item]=[o.matrix_world@v.co for o in objs if o.type=='MESH' for v in o.data.vertices]
   for o in objs:bpy.data.objects.remove(o,do_unlink=True)
  rec={'collisions':[],'max_socket_tilt_degrees':0,'prop_min_counter_clearance_m':{},'max_foot_motion_m':0,'frames_checked':0}
  footidx=[v.index for v in body.data.vertices if v.co.z<.04];firstfeet=None
  for name,spec in manifest['assets'][key]['clips'].items():
   rig.animation_data.action=bpy.data.actions[name]
   for fr in range(1,spec['frames']+1):
    bpy.context.scene.frame_set(fr);bpy.context.view_layer.update();rec['frames_checked']+=1
    for ob in meshes:
     tree,vs=tree_for(ob)
     if ob==body:
      feet=[vs[i] for i in footidx]
      if firstfeet is None:firstfeet=feet
      rec['max_foot_motion_m']=max(rec['max_foot_motion_m'],max((p-q).length for p,q in zip(feet,firstfeet)))
     for env,etree in context:
      overlaps=tree.overlap(etree)
      if overlaps:rec['collisions'].append({'clip':name,'frame':fr,'mesh':ob.name,'environment':env,'triangle_pairs':len(overlaps)})
    if name in ['server_offer','server_offer_soft'] or (name=='server_pickup' and fr==spec['frames']):
     up=sock.matrix_world.to_3x3()@V((0,0,1))
     rec['max_socket_tilt_degrees']=max(rec['max_socket_tilt_degrees'],math.degrees(up.angle(V((0,0,1)))))
     for item,pts in props.items():
      clearance=min((sock.matrix_world@p).z for p in pts)-(1.10 if role=='bar' else 1.235)
      rec['prop_min_counter_clearance_m'][item]=min(rec['prop_min_counter_clearance_m'].get(item,99),clearance)
  result[key]=rec
(R/'verification/geometry-check.json').write_text(json.dumps(result,indent=2))
print(json.dumps({k:{'collisions':len(v['collisions']),'tilt':v['max_socket_tilt_degrees'],'feet':v['max_foot_motion_m'],'clearance':v['prop_min_counter_clearance_m'],'frames':v['frames_checked']} for k,v in result.items()},indent=2))

