"""Review-only cutaway: hidden roof/fascia exposes actual interior/server. Never exported."""
import bpy,math,sys,json
from pathlib import Path
R=Path(__file__).resolve().parent;sys.path.insert(0,str(R));import build
S=Path('C:/Projects/festival-tycoon/assets/source/characters/service-server-assets-v1')
for kind in ['chips','pizza','curry']:
 bpy.ops.wm.open_mainfile(filepath=str(S/'source/lwf_server_food_male_v1.blend'))
 rig=bpy.data.objects['LWF_Attendee_Rig'];rig.location=(2.15,-.88,.47);rig.rotation_euler.z=math.pi;rig.animation_data.action=bpy.data.actions['server_idle'];bpy.context.scene.frame_set(1)
 with bpy.data.libraries.load(str(R/'source'/('ft_food_'+kind+'_production_v1.blend')),link=False) as (data,out):out.objects=[n for n in data.objects if not n.startswith('REVIEW_')]
 for o in out.objects:
  if o is None:continue
  bpy.context.collection.objects.link(o)
  if any(x in o.name for x in ['roof','Roof','Name fascia','NamePanel','Sign_','Vent','Extractor','Pizza flue','Flue rim','Lintel timber','Curved hatch lintel']):o.hide_render=True
 s,c=build.setup_render();build.render(s,c,R/'renders'/(kind+'-07-review-cutaway-not-exported.png'),9.6 if kind!='curry' else 10.7,yaw=35,elev=38.853,target=(1.6,0,1.5))
