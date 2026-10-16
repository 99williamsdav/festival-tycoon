import bpy,json
from pathlib import Path
from mathutils import Vector
R=Path('C:/Projects/festival-tycoon/game/assets/environment')
out={}
for key in ['lwf_food_van_sign_chips_v1','lwf_food_van_name_panel_chip_block_v1','lwf_food_van_sign_pizza_v1','lwf_food_van_sign_curry_v1','lwf_food_van_name_panel_korma_v1']:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(R/(key+'.glb')))
 obs=[o for o in bpy.data.objects if o.type=='MESH'];vs=[o.matrix_world@v.co for o in obs for v in o.data.vertices]
 out[key]={'bounds':[[min(v[i] for v in vs) for i in range(3)],[max(v[i] for v in vs) for i in range(3)]],'objects':[o.name for o in obs]}
Path(__file__).with_name('reference-inspection.json').write_text(json.dumps(out,indent=2))
