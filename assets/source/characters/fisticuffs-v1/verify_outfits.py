import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector as V,Matrix as M
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent;REPO=Path('C:/Projects/festival-tycoon')
manifest=json.loads((R/'manifest.json').read_text());result={}
for sex in ['male','female']:
 for role in ['attendee','steward','medic','maintenance','sound']:
  path=REPO/'assets/source/characters/rigged-test-v1/out'/f'lwf_{role}_{sex}_rigged_test_v1.blend'
  bpy.ops.wm.open_mainfile(filepath=str(path));rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');body=bpy.data.objects['LWF_Attendee_Body']
  rig.animation_data_clear()
  for a in list(bpy.data.actions):bpy.data.actions.remove(a)
  for b in rig.pose.bones:b.matrix_basis=M.Identity(4)
  with bpy.data.libraries.load(str(R/'source'/f'lwf_attendee_{sex}_fisticuffs_clips_v1.blend'),link=False) as (a,b):b.actions=list(manifest['assets'][f'lwf_attendee_{sex}_fisticuffs_clips_v1']['clips'])
  acts={a.name:a for a in b.actions}
  old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(REPO/'game/assets/characters'/f'lwf_hair_{sex}_default_v1.glb'))
  hairs=[o for o in set(bpy.data.objects)-old if o.type=='MESH']
  for hair in hairs:
   hair.data.transform(hair.matrix_world);hair.matrix_world=M.Identity(4);hair.parent=rig
   vg=hair.vertex_groups.new(name='Head');vg.add(list(range(len(hair.data.vertices))),1,'REPLACE');hair.modifiers.new('HeadAttach','ARMATURE').object=rig
  groups={g.index:g.name for g in body.vertex_groups}
  dom={v.index:groups[max(v.groups,key=lambda x:x.weight).group] for v in body.data.vertices}
  faces=[tuple(p.vertices) for p in body.data.polygons]
  hf={s:[f for f in faces if all(dom[i] in [s+'LowerArm',s+'Hand'] for i in f)] for s in ['Left','Right']}
  tf=[f for f in faces if all(dom[i] in ['Hips','Spine','Chest','Neck','Head'] for i in f)]
  rec={'source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'samples':0,'self_collisions':[],'hand_hair_collisions':[],'max_forward_extent':-99,'rest_bone_error':0}
  originalrest=manifest['assets'][f'lwf_attendee_{sex}_fisticuffs_clips_v1']['bone_rest']
  for bone in rig.data.bones:
   rec['rest_bone_error']=max(rec['rest_bone_error'],max(abs(bone.matrix_local[i][j]-originalrest[bone.name][i][j]) for i in range(4) for j in range(4)))
  rig.animation_data_create()
  for name,action in acts.items():
   rig.animation_data.action=action
   for step in range(int(action.frame_range[1])*2+1):
    fr=step/2;bpy.context.scene.frame_set(int(fr),subframe=fr%1);bpy.context.view_layer.update();rec['samples']+=1
    ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();vs=[v.co.copy() for v in me.vertices];ev.to_mesh_clear()
    tt=BVHTree.FromPolygons(vs,tf);ht={s:BVHTree.FromPolygons(vs,hf[s]) for s in hf}
    for s in hf:
     if ht[s].overlap(tt):rec['self_collisions'].append([name,fr,s])
    for hair in hairs:
     ev=hair.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();hv=[v.co.copy() for v in me.vertices];hfaces=[tuple(p.vertices) for p in me.polygons];ev.to_mesh_clear()
     tree=BVHTree.FromPolygons(hv,hfaces)
     for s in hf:
      if ht[s].overlap(tree):rec['hand_hair_collisions'].append([name,fr,s])
     rec['max_forward_extent']=max(rec['max_forward_extent'],max(v.y for v in hv))
    rec['max_forward_extent']=max(rec['max_forward_extent'],max(v.y for v in vs))
  result[role+'-'+sex]=rec
(R/'verification/outfits.json').write_text(json.dumps(result,indent=2))
print(json.dumps({k:{'samples':v['samples'],'self':len(v['self_collisions']),'hair':len(v['hand_hair_collisions']),'rest':v['rest_bone_error'],'front':v['max_forward_extent']} for k,v in result.items()},indent=2))

