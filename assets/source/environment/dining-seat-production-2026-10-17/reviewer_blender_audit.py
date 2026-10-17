import bpy,json,hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent
OUT=[]
def bvh(obs):
 vs=[];fs=[];dg=bpy.context.evaluated_depsgraph_get()
 for o in obs:
  e=o.evaluated_get(dg);m=e.to_mesh();m.calc_loop_triangles();n=len(vs)
  vs += [e.matrix_world@v.co for v in m.vertices]
  fs += [tuple(n+i for i in t.vertices) for t in m.loop_triangles]
  e.to_mesh_clear()
 return BVHTree.FromPolygons(vs,fs,all_triangles=True),vs
for kind,base in [('hay','ft_hay_bale_h1_v1'),('picnic','ft_picnic_aframe_p1_v1')]:
 bpy.ops.wm.open_mainfile(filepath=str(R/'source'/(base+'_occupied_review.blend')))
 assets=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith(('H1 ','P1 '))]
 at,_=bvh(assets);bodies=[o for o in bpy.context.scene.objects if o.name.endswith('native_body')];rec={'asset':base,'seats':[]}
 for body in bodies:
  bt,vs=bvh([body]);sex='female' if '_female_' in body.name else 'male';rig=next(m.object for m in body.modifiers if m.type=='ARMATURE')
  hip=[v.index for v in body.data.vertices if any(body.vertex_groups[g.group].name=='Hips' and g.weight>.5 for g in v.groups)]
  low=[vs[i] for i in hip if vs[i].z<.481];hits=[]
  for v in low:
   hit,normal,idx,dist=at.ray_cast(v,Vector((0,0,-1)),.04)
   if hit is not None and normal.z>.5:hits.append({'point':list(v),'gap':dist,'seat_z':hit.z})
  source=Path('C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out')/('lwf_attendee_'+sex+'_rigged_test_v1.blend')
  with bpy.data.libraries.load(str(source),link=False) as (src,dst):dst.objects=list(src.objects)
  orig=next(o for o in dst.objects if o.type=='MESH');orig_rig=next(o for o in dst.objects if o.type=='ARMATURE')
  unchanged=len(orig.data.vertices)==len(body.data.vertices) and all((a.co-b.co).length<1e-7 for a,b in zip(orig.data.vertices,body.data.vertices))
  bones_unchanged=all((rig.data.bones[b.name].head_local-b.head_local).length<1e-7 and (rig.data.bones[b.name].tail_local-b.tail_local).length<1e-7 for b in orig_rig.data.bones)
  rec['seats'].append({'body':body.name,'unchanged_source_mesh_coordinates':unchanged,'unchanged_rest_bones':bones_unchanged,'rig_world_scale':list(rig.matrix_world.to_scale()),'body_world_scale':list(body.matrix_world.to_scale()),'body_furniture_surface_overlap_pairs':len(bt.overlap(at)),'hip_points_within_31mm_of_seat_height':len(low),'hip_points_supported_within40mm':hits})
 OUT.append(rec)
(R/'verification'/'independent-blender-audit.json').write_text(json.dumps(OUT,indent=2));print(json.dumps(OUT,indent=2))
