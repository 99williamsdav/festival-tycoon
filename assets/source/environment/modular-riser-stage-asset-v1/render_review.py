import bpy,math,json,sys
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parent;bpy.ops.wm.read_factory_settings(use_empty=True)
def imp(path,name,p=(0,0,0)):
 before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path));new=set(bpy.data.objects)-before
 root=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(root)
 for ob in new:
  if ob.parent not in new:ob.parent=root
 root.location=p;return root,list(new)
stage,_=imp(R/'runtime/lwf_modular_riser_stage_v1.glb','Stage')
for i,x in enumerate([-2.42,2.42]):imp(R/'runtime/lwf_modular_riser_speaker_v1.glb','Speaker%d'%i,(x,-1.37,.9))
context=[]
for i,p in enumerate([(-1.25,-.5,.9),(1.25,-.5,.9),(-4,-2.7,0)]):
 root,obs=imp(R/'references/lwf_attendee_male_relaxed_v2.glb','ReviewPerson%d'%i,p);context+=obs
drums,_=imp(R/'references/lwf_drum_hardware_only_v2.glb','ReviewDrums',(0,.85,.9));drums.rotation_euler.z=-math.pi/2
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=16;s.cycles.use_denoising=True;s.render.threads_mode='FIXED';s.render.threads=6
s.world=bpy.data.worlds.new('ReviewWorld');s.world.color=(.4,.4,.4);s.view_settings.view_transform='Standard'
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012));m=bpy.data.materials.new('ReviewGround');m.diffuse_color=(.32,.4,.20,1);bpy.context.object.data.materials.append(m)
bpy.ops.object.light_add(type='AREA',location=(-3,-6,12));bpy.context.object.data.energy=2100;bpy.context.object.data.size=7
bpy.ops.object.camera_add();cam=bpy.context.object;s.camera=cam;cam.data.type='ORTHO'
def render(name,target,offset,size,w=1280,h=960):
 s.render.resolution_x=w;s.render.resolution_y=h;s.render.resolution_percentage=100;cam.data.ortho_scale=size;cam.location=Vector(target)+Vector(offset);cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(R/'review'/name);bpy.ops.render.render(write_still=True)
render('riser-close.png',(-.6,0,.7),(10,-14,11),10.7)
if '--quick' in sys.argv:sys.exit(0)
for i in range(4):
 a=math.radians(45+90*i);render('game-scale-%d.png'%i,(-.5,0,0),(72*math.sin(a),-72*math.cos(a),58),32,1280,720)
render('stairs-detail.png',(-3.7,-.8,.6),(-5,-7,4),4.5)
# Actual current trailer, unscaled, to the right of the candidate; shared people are unscaled too.
imp(R/'references/lwf_trailer_stage_v3.glb','ReviewCurrentTrailer',(10,0,0))
imp(R/'references/lwf_attendee_male_relaxed_v2.glb','ReviewTrailerPerson',(10,-3.3,0))
render('current-stage-scale-comparison.png',(4,0,.5),(7,-18,14),23,1600,900)

