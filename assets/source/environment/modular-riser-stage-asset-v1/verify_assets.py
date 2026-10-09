import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent;bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(R/'runtime/lwf_modular_riser_stage_v1.glb'))
obs=[o for o in bpy.data.objects if o.type=='MESH'];assert {o.name for o in obs}=={'RiserDeck','RiserSteelFrame','RiserAccess','RiserCornerRails'}
bpy.context.view_layer.update()
def tree(o):
 o.data.calc_loop_triangles();return BVHTree.FromPolygons([o.matrix_world@v.co for v in o.data.vertices],[t.vertices for t in o.data.loop_triangles],all_triangles=True)
deck=tree(bpy.data.objects['RiserDeck']);stair=tree(bpy.data.objects['RiserAccess'])
def height(bvh,x,y):
 hit=bvh.ray_cast(Vector((x,y,3)),Vector((0,0,-1)),5);assert hit[0] is not None;return hit[0].z
support=tree(bpy.data.objects['RiserSteelFrame'])
for x in [-1,1]:assert abs(height(support,x,0)-.81)<.005
hits=[]
for x in [-2.8,-2,-1,0,1,2,2.8]:
 for y in [-1.8,-1,0,1,1.8]:
  h=height(deck,x,y);assert .879<h<.906;hits.append(h)
a=json.loads((R/'anchors.json').read_text())
for x,y,z in a['stair_tread_centres']:assert abs(height(stair,x,-z)-y)<.005
for name,(x,h,z) in a['review_band_marks'].items():assert abs(height(deck,x,-z)-h)<.005
# Verify top-landing interface reaches deck plane, with no step gap in the central route.
for x in [-3.3,-3.1,-3.01]:assert abs(height(stair,x,-.8)-.9)<.005
allp=[o.matrix_world@v.co for o in obs for v in o.data.vertices]
report={'stage_bounds_blender':[[min(v[i] for v in allp) for i in range(3)],[max(v[i] for v in allp) for i in range(3)]],'deck_raycast_samples':len(hits),'deck_height_range': [min(hits),max(hits)],'five_treads_match_anchors':True,'top_landing_matches_deck':True,'review_band_marks_on_deck':True,'triangles':sum(len(o.data.loop_triangles) for o in obs),'runtime_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (R/'runtime').glob('*.glb')}}
# Measure reference person after import using transforms; not a scaled proxy.
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(R/'references/lwf_attendee_male_relaxed_v2.glb'));bpy.context.view_layer.update();pts=[o.matrix_world@v.co for o in bpy.data.objects if o.type=='MESH' for v in o.data.vertices]
report['reference_attendee_height_m']=max(v.z for v in pts)-min(v.z for v in pts)
(R/'verification.json').write_text(json.dumps(report,indent=2));print('RISER_GEOMETRY_PASSED',json.dumps(report))



