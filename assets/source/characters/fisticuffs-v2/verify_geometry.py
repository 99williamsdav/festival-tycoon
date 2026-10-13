import bpy,json,math,hashlib,struct
from pathlib import Path
from mathutils import Vector as V,Matrix as M
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent
manifest=json.loads((R/'manifest.json').read_text())
result={'individual':{},'pairs':{},'sample_step_frames':.25}
cache={}
for sex in ['male','female']:
 key=f'lwf_attendee_{sex}_fisticuffs_clips_v2'
 bpy.ops.wm.open_mainfile(filepath=str(R/'source'/f'{key}.blend'))
 rig=bpy.data.objects['LWF_Attendee_Rig'];body=bpy.data.objects['LWF_Attendee_Body']
 faces=[tuple(p.vertices) for p in body.data.polygons]
 groups={g.index:g.name for g in body.vertex_groups}
 dominant={v.index:groups[max(v.groups,key=lambda x:x.weight).group] for v in body.data.vertices}
 handfaces={side:[f for f in faces if all(dominant[i] in [side+'Hand',side+'LowerArm'] for i in f)] for side in ['Left','Right']}
 torso=[f for f in faces if all(dominant[i] in ['Hips','Spine','Chest','Neck','Head'] for i in f)]
 feet=[v.index for v in body.data.vertices if v.co.z<.045]
 reference=[v.co.copy() for v in body.data.vertices]
 rec={'clips':{},'self_intersections':[],'max_foot_motion_m':0}
 hand_only={s:[f for f in faces if all(dominant[i]==s+'Hand' for i in f)] for s in ['Left','Right']}
 nonhand=[f for f in faces if f not in hand_only['Left']+hand_only['Right']]
 cache[sex]={'faces':faces,'hands':hand_only['Left']+hand_only['Right'],'nonhand':nonhand,'clips':{}}
 for name,spec in manifest['assets'][key]['clips'].items():
  rig.animation_data.action=bpy.data.actions[name]
  samples=[];mn=V((99,99,99));mx=V((-99,-99,-99));maxfeet=0
  for step in range(spec['end_frame']*4+1):
   fr=step/4;bpy.context.scene.frame_set(int(fr),subframe=fr%1);bpy.context.view_layer.update()
   ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();vs=[v.co.copy() for v in me.vertices];ev.to_mesh_clear()
   tt=BVHTree.FromPolygons(vs,torso)
   ht={s:BVHTree.FromPolygons(vs,handfaces[s]) for s in handfaces}
   for label,over in [('left_torso',ht['Left'].overlap(tt)),('right_torso',ht['Right'].overlap(tt)),('forearms_each_other',ht['Left'].overlap(ht['Right']))]:
    if over:rec['self_intersections'].append({'clip':name,'frame':fr,'parts':label,'pairs':len(over)})
   for i in range(3):mn[i]=min(mn[i],min(v[i] for v in vs));mx[i]=max(mx[i],max(v[i] for v in vs))
   maxfeet=max(maxfeet,max((vs[i]-reference[i]).length for i in feet))
   samples.append(vs)
  cache[sex]['clips'][name]=samples
  rec['clips'][name]={'samples':len(samples),'bounds_blender':[list(mn),list(mx)],'max_foot_motion_m':maxfeet}
  rec['max_foot_motion_m']=max(rec['max_foot_motion_m'],maxfeet)
 result['individual'][sex]=rec
def sample(sex,clip,t):
 arr=cache[sex]['clips'][clip];return arr[round(max(0,min(1,t))*(len(arr)-1))]
# Paired choreography: one swing only, receiver response delayed0.08s after extension starts.
events=[]
for side in ['l','r']:
 for actor in [0,1]:
  for i in range(49):
   t=i/48;sec=t*.25
   a=('fight_swing_'+side,t)
   b=('fight_flinch',max(0,min(1,(sec-.07)/(.2083333)))) if sec>=.07 else ('fight_guard',0)
   events.append((a,b) if actor==0 else (b,a))
for clip in ['fight_enter','fight_guard','fight_stand_down','fight_recover','argue_gesture']:
 for i in range(33):events.append(((clip,i/32),(clip,i/32)))
for sexes in [('male','male'),('male','female'),('female','male'),('female','female')]:
 label='-'.join(sexes);out={}
 # Trees rebuilt for each transform; actual triangle intersections, not AABB-only.
 for distance in [.65,.70,math.sqrt(.5),.75,.80,.90,1.0]:
  hits=[];contacts=[];min_gap=99;closest_gloves=99
  for index,((ac,at),(bc,bt)) in enumerate(events):
   av=sample(sexes[0],ac,at);bv=[V((-p.x,distance-p.y,p.z)) for p in sample(sexes[1],bc,bt)]
   ta=BVHTree.FromPolygons(av,cache[sexes[0]]['faces']);tb=BVHTree.FromPolygons(bv,cache[sexes[1]]['faces'])
   ah=BVHTree.FromPolygons(av,cache[sexes[0]]['hands']);bh=BVHTree.FromPolygons(bv,cache[sexes[1]]['hands'])
   an=BVHTree.FromPolygons(av,cache[sexes[0]]['nonhand']);bn=BVHTree.FromPolygons(bv,cache[sexes[1]]['nonhand'])
   forbidden=an.overlap(tb)+ah.overlap(bn)
   overlap=ah.overlap(bh)
   if forbidden:hits.append({'sample':index,'a':[ac,at],'b':[bc,bt],'triangle_pairs':len(forbidden)})
   if overlap:contacts.append({'sample':index,'a':[ac,at],'b':[bc,bt],'triangle_pairs':len(overlap)})
   if ac.startswith('fight_swing') or bc.startswith('fight_swing'):
    for vi in set(i for f in cache[sexes[0]]['hands'] for i in f):
     near=bh.find_nearest(av[vi]);closest_gloves=min(closest_gloves,near[3])
   # Conservative forward-envelope gap, stricter than actual per-triangle lateral separation.
   min_gap=min(min_gap,min(p.y for p in bv)-max(p.y for p in av))
  out[str(distance)]={'samples':len(events),'forbidden_collision_samples':len(hits),'first_collisions':hits[:3],'glove_contact_samples':len(contacts),'first_contacts':contacts[:3],'closest_glove_surface_gap_m':closest_gloves,'min_forward_envelope_gap_m':min_gap}
 result['pairs'][label]=out
(R/'verification/geometry.json').write_text(json.dumps(result,indent=2))
print('INDIVIDUAL',json.dumps({k:{'self_hits':len(v['self_intersections']),'feet':v['max_foot_motion_m']} for k,v in result['individual'].items()}))
print('PAIRS',json.dumps({k:{d:(v['forbidden_collision_samples'],v['glove_contact_samples'],round(v['closest_glove_surface_gap_m'],4)) for d,v in val.items()} for k,val in result['pairs'].items()}))

