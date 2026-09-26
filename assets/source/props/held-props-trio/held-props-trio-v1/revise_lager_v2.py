"""Change only beer liquid colour in the exact v1 source and review copies."""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
OUT=ROOT.parent/'held-props-trio-lager-v2'
OUT.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'lwf_held_props_trio_v1.blend'))
s=bpy.context.scene
h='E3B83D'
c=[int(h[i:i+2],16)/255 for i in (0,2,4)]
rgba=tuple(v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in c)+(1,)
m=bpy.data.materials['Beer amber'];m.name='Beer golden lager';m.diffuse_color=rgba
m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=rgba
beer=bpy.data.objects['LWF_BeerCupV1'];beer.name='LWF_BeerCupV2'
bpy.ops.object.select_all(action='DESELECT');beer.select_set(True);bpy.context.view_layer.objects.active=beer
bpy.ops.export_scene.gltf(filepath=str(OUT/'lwf_beer_cup_v2.glb'),export_format='GLB',use_selection=True,export_yup=True,export_apply=True,export_animations=False)
review=[o for o in s.objects if o.name.startswith('REVIEW_')]
ground=bpy.data.objects['REVIEW_Ground'];cam=s.camera
assets=[o for o in s.objects if o.name.startswith('LWF_')]
def render(file,active,target,width,yaw,elev,res=(1600,1000)):
    for o in assets:o.hide_render=True
    for o in review:o.hide_render=o not in active and o!=ground
    t=Vector(target);a=math.radians(yaw);cam.location=t+Vector((10*math.sin(a),-10*math.cos(a),10*math.tan(math.radians(elev))))
    cam.rotation_euler=(t-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=width
    s.render.resolution_x,s.render.resolution_y=res;s.render.filepath=str(OUT/file);bpy.ops.render.render(write_still=True)
lineup=[o for o in review if o.name.startswith('REVIEW_Lineup')]
adults=[o for o in review if o.name.startswith(('REVIEW_lwf_generic','REVIEW_AdultProp')) or o.type=='FONT']
render('01-held-props-trio-lager.png',lineup,(0,0,.08),.96,175,30)
render('02-adult-hand-scale-lager.png',adults,(.1,0,1.0),4.6,165,15)
render('03-game-camera-32m-lager.png',adults,(0,0,.6),32,155,math.degrees(math.atan2(58,72)),(1920,1080))
for o in review:o.hide_render=True
for o in assets:o.hide_render=False
bpy.ops.object.select_all(action='DESELECT');beer.select_set(True);bpy.context.view_layer.objects.active=beer
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lwf_held_props_trio_lager_v2.blend'))
tech=json.loads((ROOT/'technical.json').read_text());tech['status']='Beer-only lager revision v2; exact trio dual approval pending'
tech['assets']['BeerCupV1']['file']='lwf_beer_cup_v2.glb';tech['assets']['BeerCupV2']=tech['assets'].pop('BeerCupV1')
tech['change']='ONLY beer liquid sRGB BC873B -> E3B83D; cup/foam/geometry/origin unchanged. Chips and soft drink use unchanged v1 files.'
tech['sha256']={'beer_glb':hashlib.sha256((OUT/'lwf_beer_cup_v2.glb').read_bytes()).hexdigest(),'source':hashlib.sha256((OUT/'lwf_held_props_trio_lager_v2.blend').read_bytes()).hexdigest()}
(OUT/'technical.json').write_text(json.dumps(tech,indent=2));print(json.dumps(tech['sha256']))
