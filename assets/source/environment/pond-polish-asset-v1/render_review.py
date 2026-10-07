import bpy,math
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
for name in ['lwf_pond_polish_static_v1','lwf_duck_drake_v1','lwf_duck_brown_v1']:
 bpy.ops.import_scene.gltf(filepath=str(R/'runtime'/(name+'.glb')))
for name,p,yaw in [('DuckDrake',(-1,-.6,.075),.6),('DuckBrown',(-.2,-1.3,.075),1.1)]:
 ob=bpy.data.objects[name];ob.location=p;ob.rotation_euler.z=yaw
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.use_denoising=True
s.render.resolution_x=1000;s.render.resolution_y=750;s.render.resolution_percentage=100
s.world=bpy.data.worlds.new('ReviewWorld');s.world.color=(.35,.35,.35)
s.view_settings.view_transform='Standard'
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012));ground=bpy.context.object;ground.name='ReviewGround'
m=bpy.data.materials.new('ReviewGround');m.diffuse_color=(.31,.40,.17,1);ground.data.materials.append(m)
bpy.ops.object.light_add(type='AREA',location=(-4,-6,12));bpy.context.object.data.energy=1800;bpy.context.object.data.size=8
bpy.ops.object.camera_add();cam=bpy.context.object;s.camera=cam;cam.data.type='ORTHO'
def render(name,target,offset,size):
 cam.location=Vector(target)+Vector(offset);cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=size
 s.render.filepath=str(R/'review'/name);bpy.ops.render.render(write_still=True)
render('pond-close.png',(0,0,0),(10,-14,14),12)
render('ducks-close.png',(-.6,-.95,.20),(1.1,-2.0,1.2),1.65)
s.render.resolution_x=1280;s.render.resolution_y=720
for i in range(4):
 a=math.radians(45+90*i);render('game-scale-%d.png'%i,(0,0,0),(math.sin(a)*72,-math.cos(a)*72,58),32)
render('default-zoom.png',(0,0,0),(50.91,-50.91,58),62*16/9)
