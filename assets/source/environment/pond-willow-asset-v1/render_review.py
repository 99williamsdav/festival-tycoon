import bpy,math,json,sys
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
for name in ['lwf_pond_polish_static_v1','lwf_duck_drake_v1','lwf_duck_brown_v1']:
 bpy.ops.import_scene.gltf(filepath=str(R/'references'/(name+'.glb')))
for name,p in [('DuckDrake',(-1,-.1,.075)),('DuckBrown',(.9,-.5,.075))]:bpy.data.objects[name].location=p
before=set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(R/'runtime/lwf_pond_willow_v1.glb'))
added=set(bpy.data.objects)-before
root=bpy.data.objects.new('WillowPlacement',None);bpy.context.collection.objects.link(root)
for ob in added:
 if ob.parent not in added:ob.parent=root
root.location=(4.5,-.5,0);root.rotation_euler.z=math.pi
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=16;s.render.threads_mode='FIXED';s.render.threads=6;s.cycles.use_denoising=True
s.world=bpy.data.worlds.new('ReviewWorld');s.world.color=(.42,.42,.42);s.view_settings.view_transform='Standard'
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012));ground=bpy.context.object
m=bpy.data.materials.new('ReviewGround');m.diffuse_color=(.31,.4,.17,1);ground.data.materials.append(m)
bpy.ops.object.light_add(type='AREA',location=(-5,-6,13));bpy.context.object.data.energy=2100;bpy.context.object.data.size=7
bpy.ops.object.camera_add();cam=bpy.context.object;s.camera=cam;cam.data.type='ORTHO'
def render(name,target,offset,size,w=1280,h=960):
 s.render.resolution_x=w;s.render.resolution_y=h;s.render.resolution_percentage=100
 cam.location=Vector(target)+Vector(offset);cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=size
 s.render.filepath=str(R/'review'/name);bpy.ops.render.render(write_still=True)
render('pond-willow-close.png',(0,-.4,1.65),(-12,15,13),13.5)
if '--quick' in sys.argv: sys.exit(0)
render('willow-detail.png',(3.4,-.5,2.1),(-8,10,5),6.7)
for i in range(4):
 a=math.radians(45+i*90)
 render('game-scale-%d.png'%i,(0,0,.4),(math.sin(a)*72,-math.cos(a)*72,58),32,1280,720)
render('top-clearance.png',(0,0,0),(0,0,20),12)





