import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parent;OLD=ROOT.parent/'held-props-trio-v1'
def signature(o):
    return ([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons],tuple(o.location),tuple(o.scale))
bpy.ops.wm.open_mainfile(filepath=str(OLD/'lwf_held_props_trio_v1.blend'))
before={key:signature(bpy.data.objects['LWF_'+key]) for key in ('ChipsTrayV1','SoftDrinkCupV1','BeerCupV1')}
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'lwf_held_props_trio_lager_v2.blend'))
for old,new in [('ChipsTrayV1','ChipsTrayV1'),('SoftDrinkCupV1','SoftDrinkCupV1'),('BeerCupV1','BeerCupV2')]:
    assert before[old]==signature(bpy.data.objects['LWF_'+new]),'Geometry or origin changed'
for key in ('ChipsTrayV1','SoftDrinkCupV1'):
    assert bpy.data.objects['LWF_'+key].data.materials.find('Beer golden lager')==-1
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(ROOT/'lwf_beer_cup_v2.glb'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert len(meshes)==1
o=meshes[0];o.data.calc_loop_triangles();assert len(o.data.loop_triangles)==252
expected=json.loads((ROOT/'technical.json').read_text())['assets']['BeerCupV2']['bounds_blender']
vs=[o.matrix_world@v.co for v in o.data.vertices]
bounds=[[min(v[i] for v in vs) for i in range(3)],[max(v[i] for v in vs) for i in range(3)]]
assert max(abs(bounds[k][i]-expected[k][i]) for k in range(2) for i in range(3))<.00001
print('PASS: all three geometry/origins identical to v1; beer GLB reimports at 252 triangles and matching bounds')
