import bpy,math,json,sys
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parent;REPO=Path('C:/Projects/festival-tycoon')
def imp(path):
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path));return list(set(bpy.data.objects)-old)
def root(name,objects,at=(0,0,0),yaw=0):
 o=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(o)
 for child in objects:
  if child.parent is None:child.parent=o
 o.location=(at[0],-at[2],at[1]);o.rotation_euler.z=math.radians(yaw);return o
def setup():
 bpy.ops.wm.read_factory_settings(use_empty=True)
 barn=imp(R/'runtime/lwf_barn_stage_v1.glb')
 kit=imp(R/'references/lwf_modular_riser_stage_v1.glb')
 for o in kit:
  if o.name=='RiserAccess':o.rotation_mode='XYZ';o.rotation_euler.z+=math.pi
 stage=root('ReviewStage',kit,(6.2,0,-1.5),-90)
 for x in [-2.42,2.42]:
  sp=imp(R/'references/lwf_modular_riser_speaker_v1.glb');r=root('Speaker',sp,(x,.9,1.37));r.parent=stage
 # Existing review people and original drum kit, never delivered inside barn GLB.
 for i,(x,y,z) in enumerate([(5.7,.9,-.25),(5.7,.9,-2.75),(7.05,.9,-1.5)]+[(x,0,z) for x in [-5,-2.5,0,1.8] for z in [-2.8,-.7,1.5]]):
  people=imp(REPO/'game/assets/characters'/('lwf_attendee_male_relaxed_v2.glb' if i<3 else 'lwf_attendee_male_rigged_test_v1.glb'))
  if i<3:
   for o in people:
    if 'Idle' in o.name and 'Arm' in o.name:o.hide_render=True
   people+=imp(REPO/'game/assets/characters'/['lwf_guitarist_male_kit_v2.glb','lwf_bassist_male_kit_v2.glb','lwf_drummer_male_kit_v2.glb'][i])
  people+=imp(REPO/'game/assets/characters/lwf_hair_male_default_v1.glb')
  root('Band' if i<3 else 'Audience',people,(x,y,z),90 if i<3 else -90)
 drums=imp(REPO/'game/assets/characters/lwf_drum_hardware_only_v2.glb');root('DrumKit',drums,(7.05,.9,-1.5),90)
 s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.transparent_max_bounces=64;s.cycles.use_denoising=True;s.render.threads_mode='FIXED';s.render.threads=6
 s.world=bpy.data.worlds.new('ReviewWorld');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.72,.77,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.7
 s.view_settings.view_transform='Standard'
 bpy.ops.mesh.primitive_plane_add(size=180,location=(0,0,-.005));g=bpy.context.object;mat=bpy.data.materials.new('ReviewGround');mat.diffuse_color=(.32,.40,.23,1);g.data.materials.append(mat)
 bpy.ops.object.light_add(type='AREA',location=(-7,-9,23));bpy.context.object.data.energy=2600;bpy.context.object.data.size=12
 for x,y,z in [(-8.6,5.2,3.4),(0,5.2,3.4),(8.6,5.2,3.4),(-8.6,-5.2,3.4),(8.6,-5.2,3.4),(6.2,1.5,4.5)]:
  bpy.ops.object.light_add(type='POINT',location=(x,y,z));bpy.context.object.data.energy=180;bpy.context.object.data.color=(1,.82,.60);bpy.context.object.data.shadow_soft_size=.65
 bpy.ops.object.camera_add();s.camera=bpy.context.object;s.camera.data.type='ORTHO'
 return barn,s
def fade(barn,on):
 for o in barn:
  if o.type!='MESH':continue
  for mat in o.data.materials:
   if not mat.name.startswith(('Barn_Roof','Barn_WallFade','Barn_DoorFade','Barn_TrussFade')):continue
   if not mat.use_nodes:mat.use_nodes=True
   nodes=mat.node_tree.nodes;links=mat.node_tree.links;out=nodes.get('Material Output')
   if 'ReviewFade' not in nodes:
    original=out.inputs['Surface'].links[0].from_socket
    mix=nodes.new('ShaderNodeMixShader');mix.name='ReviewFade';trans=nodes.new('ShaderNodeBsdfTransparent');links.new(trans.outputs[0],mix.inputs[1]);links.new(original,mix.inputs[2]);links.new(mix.outputs[0],out.inputs['Surface'])
    light=nodes.new('ShaderNodeLightPath');mx=nodes.new('ShaderNodeMath');mx.operation='MAXIMUM';mx.name='ReviewShadow';links.new(light.outputs['Is Shadow Ray'],mx.inputs[1]);links.new(mx.outputs[0],mix.inputs[0])
    geom=nodes.new('ShaderNodeNewGeometry');inv=nodes.new('ShaderNodeMath');inv.operation='SUBTRACT';inv.inputs[0].default_value=1;links.new(geom.outputs['Backfacing'],inv.inputs[1]);mul=nodes.new('ShaderNodeMath');mul.operation='MULTIPLY';mul.name='ReviewAlpha';links.new(inv.outputs[0],mul.inputs[1]);links.new(mul.outputs[0],mx.inputs[0])
   nodes['ReviewAlpha'].inputs[0].default_value=.3 if on else 1
def render(s,name,yaw,size=44,res=(1280,720),target=(0,0,3.5)):
 a=math.radians(yaw);t=Vector(target);s.camera.location=t+Vector((72*math.sin(a),-72*math.cos(a),58));s.camera.rotation_euler=(t-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.ortho_scale=size
 s.render.resolution_x,s.render.resolution_y=res;s.render.resolution_percentage=100;s.render.filepath=str(R/'review'/name);bpy.ops.render.render(write_still=True)
if __name__=='__main__':
 barn,s=setup()
 render(s,'solid-0.png',45)
 fade(barn,True);render(s,'faded-0.png',45);render(s,'faded-end.png',315,44)
 if '--quick' not in sys.argv:
  for i,yaw in enumerate([45,135,225,315]):
   for active in [False,True]:
    fade(barn,active);render(s,('occupied' if active else 'normal')+f'-game-min18-{i}.png',yaw,32,target=(0,0,0));render(s,('occupied' if active else 'normal')+f'-game-default62-{i}.png',yaw,62*16/9,target=(0,0,0))
 print('REVIEW COMPLETE')

