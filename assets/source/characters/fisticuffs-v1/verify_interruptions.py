import bpy,json,math
from pathlib import Path
from mathutils import Matrix as M,Vector as V
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent
out={}
for sex in ['male','female']:
 bpy.ops.wm.open_mainfile(filepath=str(R/'source'/f'lwf_attendee_{sex}_fisticuffs_clips_v1.blend'))
 rig=bpy.data.objects['LWF_Attendee_Rig'];body=bpy.data.objects['LWF_Attendee_Body']
 faces=[tuple(p.vertices) for p in body.data.polygons];gn={g.index:g.name for g in body.vertex_groups}
 dom={v.index:gn[max(v.groups,key=lambda g:g.weight).group] for v in body.data.vertices}
 hf={s:[f for f in faces if all(dom[i] in [s+'Hand',s+'LowerArm'] for i in f)] for s in ['Left','Right']}
 tf=[f for f in faces if all(dom[i] in ['Hips','Spine','Chest','Neck','Head'] for i in f)]
 foot=[v.index for v in body.data.vertices if v.co.z<.045]
 ref=[v.co.copy() for v in body.data.vertices]
 def getpose(name,fr):
  rig.animation_data.action=bpy.data.actions[name];bpy.context.scene.frame_set(int(fr),subframe=fr%1);bpy.context.view_layer.update()
  snap={b.name:(b.location.copy(),b.rotation_quaternion.copy(),b.scale.copy()) for b in rig.pose.bones}
  rig.animation_data.action=None
  return snap
 rec={'samples':0,'self_collisions':[],'max_foot_motion':0,'max_forward_extent':0,'stand_down_blend_seconds':.125}
 for clip in ['fight_swing_l','fight_swing_r','fight_flinch','fight_guard']:
  end=int(bpy.data.actions[clip].frame_range[1])
  for fr in range(0,end+1,2):
   initial=getpose(clip,fr)
   for i in range(17):
    seconds=i/48;target=getpose('fight_stand_down',min(8,seconds*24));blend=min(1,seconds/.125)
    for b in rig.pose.bones:
     a=initial[b.name];z=target[b.name];b.location=a[0].lerp(z[0],blend);b.rotation_quaternion=a[1].slerp(z[1],blend);b.scale=a[2].lerp(z[2],blend)
    bpy.context.view_layer.update();rec['samples']+=1
    ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();vs=[v.co.copy() for v in me.vertices];ev.to_mesh_clear()
    tt=BVHTree.FromPolygons(vs,tf)
    for side in hf:
     if BVHTree.FromPolygons(vs,hf[side]).overlap(tt):rec['self_collisions'].append([clip,fr,i,side])
    rec['max_forward_extent']=max(rec['max_forward_extent'],max(v.y for v in vs))
    rec['max_foot_motion']=max(rec['max_foot_motion'],max((vs[i]-ref[i]).length for i in foot))
 out[sex]=rec
(R/'verification/interruptions.json').write_text(json.dumps(out,indent=2))
print(json.dumps(out,indent=2))

