import bpy,math,json,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;REPO=Path('C:/Projects/festival-tycoon')
bpy.ops.wm.read_factory_settings(use_empty=True);s=bpy.context.scene
s.unit_settings.system='METRIC';s.unit_settings.scale_length=1
def imp(file,shift,prefix):
    before=set(s.objects);bpy.ops.import_scene.gltf(filepath=str(REPO/file));new=[o for o in s.objects if o not in before]
    for o in new:
        if o.parent not in new:o.matrix_world=Matrix.Translation(Vector(shift))@o.matrix_world
        o.name=prefix+o.name
    return new
van=imp('assets/runtime/environment/lwf_food_van_chassis_v1.glb',(-2.15,0,0),'REVIEW_')
van+=imp('assets/runtime/environment/lwf_food_van_fascia_v1.glb',(0,-1.18,2.49),'REVIEW_')
for i,x in enumerate((-.65,.65)):
    imp('assets/runtime/characters/lwf_generic_attendee_v1.glb',(x,-2.05,0),'REVIEW_Customer'+str(i)+'_')
# No serving flap imported. Chassis counter and fascia are untouched.
groundmat=bpy.data.materials.new('Review green');groundmat.diffuse_color=(.24,.31,.14,1)
bpy.ops.mesh.primitive_cube_add(size=1,location=(0,0,-.06));o=bpy.context.object;o.name='REVIEW_Ground';o.dimensions=(60,60,.1);o.data.materials.append(groundmat)
s.render.engine='BLENDER_EEVEE';s.render.resolution_x=1600;s.render.resolution_y=1000;s.render.resolution_percentage=100
s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast';s.world=bpy.data.worlds.new('ReviewWorld');s.world.color=(.3,.35,.3)
bpy.ops.object.light_add(type='SUN',location=(0,0,8));bpy.context.object.rotation_euler=(.52,-.4,-.44);bpy.context.object.data.energy=2
bpy.ops.object.light_add(type='AREA',location=(0,-5,8));bpy.context.object.data.energy=1000;bpy.context.object.data.size=7
bpy.ops.object.camera_add();cam=bpy.context.object;cam.data.type='ORTHO';s.camera=cam
target=Vector((0,-.25,1.15));elev=math.atan2(58,72)
for i,yaw in enumerate((45,135,225,315)):
    a=math.radians(yaw);cam.location=target+Vector((20*math.sin(a),-20*math.cos(a),20*math.tan(elev)))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=8.5
    s.render.filepath=str(ROOT/f'0{i+1}-view-{yaw}.png');bpy.ops.render.render(write_still=True)
paths=['assets/runtime/environment/lwf_food_van_chassis_v1.glb','assets/runtime/environment/lwf_food_van_fascia_v1.glb','assets/runtime/environment/lwf_food_van_serving_flap_v1.glb']
meta={'change':'omit raised serving-flap instance only; no asset changes','retained':[paths[0],paths[1]],'omitted_from_open_assembly':paths[2],'sha256':{p:hashlib.sha256((REPO/p).read_bytes()).hexdigest() for p in paths},'game_assembly_offset_godot':[-2.15,0,0],'fascia_local_godot':[2.15,2.49,1.18],'camera_yaw_degrees':[45,135,225,315],'elevation_degrees':math.degrees(elev),'context':'unchanged adult models; local review, not new-world-site game capture'}
(ROOT/'technical.json').write_text(json.dumps(meta,indent=2));print(json.dumps(meta))
