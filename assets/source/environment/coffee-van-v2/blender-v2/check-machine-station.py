import bpy,os,json
from mathutils import Vector
from mathutils.bvhtree import BVHTree
p=os.path.dirname(os.path.abspath(__file__));bpy.ops.wm.open_mainfile(filepath=os.path.join(p,'coffee-van-server-review.blend'))
server=list(bpy.data.collections['REVIEW ONLY actual game food server'].objects);rig=next(o for o in server if o.type=='ARMATURE')
for o in server:
 if o.parent not in server:o.location.x-=.54
rig.animation_data.action=next(a for a in bpy.data.actions if a.name.startswith('server_idle'));bpy.context.scene.frame_set(1);bpy.context.view_layer.update()
helpers={b.custom_shape for b in rig.pose.bones if b.custom_shape}
def tree(o):
 e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();v=[e.matrix_world@x.co for x in m.vertices];f=[tuple(x.vertices) for x in m.polygons];e.to_mesh_clear();return BVHTree.FromPolygons(v,f)
env=[(o.name,tree(o)) for o in bpy.context.scene.objects if o.type=='MESH' and o not in server and not o.name.startswith('STUDIO') and o.name!='Interior floor']
hits={}
for o in server:
 if o.type!='MESH' or o in helpers:continue
 t=tree(o)
 for n,e in env:
  pairs=t.overlap(e)
  if pairs:hits[n]=hits.get(n,0)+len(pairs)
s=rig.matrix_world@rig.pose.bones['RightUpperArm'].head;target=Vector((-.32,-.927,2.04));reach=sum(rig.pose.bones[n].length for n in ['RightUpperArm','RightLowerArm','RightHand'])
r={'machine_station_root_m':[.32,-.52,.715],'step_from_handoff_m':.54,'idle_surface_intersections':hits,'shoulder_to_machine_controls_m':(s-target).length,'arm_plus_hand_bone_length_m':reach,'limit':'Static pose and straight-line reach envelope only. No machine operating animation or reach path validated.'}
with open(os.path.join(p,'machine-reach-review.json'),'w') as f:json.dump(r,f,indent=2)
print(json.dumps(r))
