import bpy,json,math,struct,hashlib,os
from pathlib import Path
R=Path(__file__).resolve().parent
H=Path(os.environ.get('COW_HISTORY_ROOT',str(R.parent)))
OUT=Path(os.environ.get('COW_VERIFY_OUTPUT',str(R/'verification.json')))
bpy.ops.wm.open_mainfile(filepath=str(R/'source/lwf_cow_v3.blend'))
rig=bpy.data.objects['LWF_Cow'];ob=bpy.data.objects['CowBody'];scene=bpy.context.scene
assert len(rig.data.bones)==19
assert all(abs(sum(g.weight for g in v.groups)-1)<1e-5 for v in ob.data.vertices)
from mathutils import Vector,Matrix
with bpy.data.libraries.load(str(H/'cow-asset-v2/source/lwf_cow_v2.blend'),link=False) as (src,dst):dst.meshes=['Cow_CraftedTopology']
previous=dst.meshes[0]
assert len(previous.vertices)==len(ob.data.vertices)
inverse_head=Matrix.Rotation(math.radians(-7),3,'Y');pivot=Vector((.89,0,1.43))
ear_error=0;leg_error=0
for v in ob.data.vertices:
 names=[ob.vertex_groups[g.group].name for g in v.groups if g.weight>.001]
 if any(n.startswith(('Front','Hind')) for n in names):leg_error=max(leg_error,(v.co-previous.vertices[v.index].co).length)
 if any(n.startswith('Ear') for n in names):ear_error=max(ear_error,(pivot+inverse_head@(v.co-pivot)-previous.vertices[v.index].co).length)
assert ear_error<1e-6 and leg_error<1e-6,(ear_error,leg_error)
original_v2=json.loads((H/'cow-asset-v2/verification.json').read_text())['sha256']
assert all(hashlib.sha256((H/'cow-asset-v2'/p).read_bytes()).hexdigest()==h for p,h in original_v2.items())
samples=[]
for act in bpy.data.actions:
 rig.animation_data.action=act
 for frame in range(int(act.frame_range[0]),int(act.frame_range[1])+1):
  scene.frame_set(frame);deps=bpy.context.evaluated_depsgraph_get();ev=ob.evaluated_get(deps);me=ev.to_mesh()
  assert all(math.isfinite(c) for v in me.vertices for c in v.co)
  feet={}
  for name in ['FrontL','FrontR','HindL','HindR']:
   gi=ob.vertex_groups[name+'Hoof'].index;ids=[v.index for v in ob.data.vertices if any(g.group==gi for g in v.groups)]
   feet[name]=min(me.vertices[i].co.z for i in ids)
  assert min(feet.values())>-.002,(act.name,frame,feet)
  head_indices=[v.index for v in ob.data.vertices if any(ob.vertex_groups[g.group].name=='Head' and g.weight>.5 for g in v.groups)]
  assert min(me.vertices[i].co.z for i in head_indices)>.70
  samples.append({'action':act.name,'frame':frame,'sole_heights':feet})
  ev.to_mesh_clear()
rig.animation_data.action=None
data=(R/'runtime/lwf_cow_v3.glb').read_bytes();n=struct.unpack_from('<I',data,12)[0];g=json.loads(data[20:20+n])
assert len(g['skins'])==1 and len(g['skins'][0]['joints'])==19
assert len(g['materials'])==1 and len(g['meshes'])==1
assert set(a['name'] for a in g['animations'])=={'Idle','WalkPreview','Alert'}
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(R/'runtime/lwf_cow_v3.glb'))
assert len([o for o in bpy.data.objects if o.type=='ARMATURE'])==1
print('IMPORTED_OBJECTS',[(o.name,o.type) for o in bpy.data.objects])
assert bpy.data.objects.get('CowBody') is not None
# Blender glTF importer creates an Icosphere custom bone-display shape. It is
# not exported geometry; GLB mesh count above remains the authoritative check.
assert len([o for o in bpy.context.scene.objects if o.type=='MESH' and o.name!='Icosphere'])==1
targets=json.loads((R/'build-report.json').read_text())['walk_contact_targets']
for target in targets:
 sample=next(s for s in samples if s['action']=='WalkPreview' and s['frame']==target['frame'])
 assert abs(sample['sole_heights'][target['leg']]-target['sole_height'])<.002
for sample in samples:
 if sample['action']!='WalkPreview':assert max(abs(v) for v in sample['sole_heights'].values())<.002
original=json.loads((H/'cow-asset-v1/verification.json').read_text())['sha256']
assert all(hashlib.sha256((H/'cow-asset-v1'/p).read_bytes()).hexdigest()==h for p,h in original.items())
report={'passed':True,'source_animation_samples':samples,'glb_joints':19,'glb_materials':1,'glb_meshes':1,'glb_animations':[a['name'] for a in g['animations']],'checks':['normalized skin weights','finite animated vertices','sampled hoof soles above ground within 2mm','walk stance and swing sole heights agree with targets within 2mm','idle and alert hooves planted within 2mm','Blender GLB reimport','GLB skin and animation presence','v1 source/export/palette hashes unchanged'],'limitations':['No locomotion controller/root motion test','No exhaustive self-intersection solver','WalkPreview poses are not final production gait'],'sha256':{str(p.relative_to(R)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [R/'runtime/lwf_cow_v3.glb',R/'source/lwf_cow_v3.blend',R/'textures/lwf_cow_palette_v3.png']}}
OUT.write_text(json.dumps(report,indent=2));print('COW_VERIFY_PASSED')
report['v3_specific']={'v2_hashes_unchanged':True,'ear_shape_error_after_undoing_head_pitch_m':ear_error,'leg_geometry_error_m':leg_error,'sampled_head_ground_clearance_above_m':.70,'rest_head_pitch_down_degrees':7}
OUT.write_text(json.dumps(report,indent=2))
