import bpy,math,json,sys
from pathlib import Path
from mathutils import Vector as V,Matrix as M
R=Path(__file__).resolve().parent;REPO=Path('C:/Projects/festival-tycoon')
quick='--quick' in sys.argv
refresh='--refresh' in sys.argv
default_only='--default-only' in sys.argv
outfit_only='--outfit-only' in sys.argv
def imp(path,shift=(0,0,0),yaw=0):
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path));new=list(set(bpy.data.objects)-old)
 for o in new:
  if o.parent not in new:o.matrix_world=M.Translation(V(shift))@M.Rotation(yaw,4,'Z')@o.matrix_world
 return new
def setup():
 s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=12;s.cycles.use_denoising=True;s.render.threads_mode='FIXED';s.render.threads=6
 s.world=bpy.data.worlds.new('ReviewWorld');s.world.color=(.4,.4,.4);s.view_settings.view_transform='Standard'
 bpy.ops.mesh.primitive_plane_add(size=100,location=(0,0,-.009));m=bpy.data.materials.new('ReviewGrass');m.diffuse_color=(.32,.4,.22,1);bpy.context.object.data.materials.append(m)
 bpy.ops.object.light_add(type='AREA',location=(-3,-5,9));bpy.context.object.data.energy=1600;bpy.context.object.data.size=6
 bpy.ops.object.camera_add();cam=bpy.context.object;s.camera=cam;cam.data.type='ORTHO'
 return s,cam
def render(name,target,offset,size,w=1200,h=900):
 s.render.resolution_x=w;s.render.resolution_y=h;s.render.resolution_percentage=100
 cam.data.ortho_scale=size;cam.location=V(target)+V(offset);cam.rotation_euler=(V(target)-cam.location).to_track_quat('-Z','Y').to_euler()
 s.render.filepath=str(R/'review'/name);bpy.ops.render.render(write_still=True)
for role in ['bar','food']:
 for sex in ['male','female']:
  name=f'lwf_server_{role}_{sex}_v1'
  bpy.ops.wm.open_mainfile(filepath=str(R/'source'/f'{name}.blend'))
  rig=bpy.data.objects['LWF_Attendee_Rig'];rig.location=(.35,-.30,0) if role=='bar' else (0,-.88,.47);rig.rotation_euler.z=math.pi
  rig.animation_data.action=bpy.data.actions['server_offer'];bpy.context.scene.frame_set(13)
  sock=bpy.data.objects['LWF_RightHand_Cup' if role=='bar' else 'LWF_RightHand_Food']
  bpy.context.view_layer.update()
  prop=imp(REPO/'game/assets/props'/('lwf_beer_cup_v2.glb' if role=='bar' else 'lwf_chips_tray_v1.glb'))
  for o in prop:
   if o.parent not in prop:o.matrix_world=sock.matrix_world@o.matrix_world
  servers=[o for o in bpy.data.objects]
  s,cam=setup()
  if not default_only:
   raised=rig.location.z;rig.location.z=0
   for ob in prop:
    if ob.parent not in prop:ob.location.z-=raised
   render(f'{role}-{sex}-outfit.png',(.2,-.22,.90),(3,-6,2.3),3.2)
   rig.location.z=raised
   for ob in prop:
    if ob.parent not in prop:ob.location.z+=raised
  if outfit_only:continue
  if refresh and (role!='bar' or sex!='male'):continue
  if role=='bar':context=imp(REPO/'game/assets/environment/lwf_drinks_stall_prototype_v1.glb')
  else:
   context=imp(R/'runtime/lwf_food_van_chassis_open_service_v1.glb',(-2.15,0,0))
   # Apply chip livery exactly to palette texture nodes, leave exterior geometry intact.
   image=bpy.data.images.load(str(REPO/'game/assets/environment/lwf_food_van_palette_chip_block_v1.png'))
   for o in context:
    if o.type=='MESH':
     for mat in o.data.materials:
      if mat and mat.name.startswith('LWF_FoodVan_MattePalette'):
       for node in mat.node_tree.nodes:
        if node.type=='TEX_IMAGE':node.image=image
   context+=imp(REPO/'game/assets/environment/lwf_food_van_fascia_v1.glb',(0,-1.18,2.49))
   context+=imp(REPO/'game/assets/environment/lwf_food_van_name_panel_chip_block_v1.glb',(0,-1.236,2.49))
   context+=imp(REPO/'game/assets/environment/lwf_food_van_sign_chips_v1.glb',(-.05,0,2.75))
  if default_only:
   for i,yaw in enumerate([45,135,225,315]):
    a=math.radians(yaw);render(f'{role}-{sex}-default62-{i}.png',(0,0,0),(72*math.sin(a),-72*math.cos(a),58),62*16/9,1280,720)
   continue
  render(f'{role}-{sex}-counter-front.png',(0,-.3,1.25),(4,-8,4),5.6 if role=='food' else 4.7)
  render(f'{role}-{sex}-hand-side.png',(.2,-.85,1.25),(6,-1,1.2),2.7)
  if quick:continue
  for i,yaw in enumerate([45,135,225,315]):
   a=math.radians(yaw);offset=(72*math.sin(a),-72*math.cos(a),58)
   render(f'{role}-{sex}-context-{i}.png',(0,0,1),offset,6 if role=='food' else 5,1000,800)
   render(f'{role}-{sex}-game32-{i}.png',(0,0,0),offset,32,1280,720)
  for clip,frame in [('server_idle',24),('server_turn',5),('server_pickup',13),('server_reset',13)]:
   rig.animation_data.action=bpy.data.actions[clip];s.frame_set(frame);bpy.context.view_layer.update()
   for ob in prop:
    ob.hide_render=clip in ['server_idle','server_turn','server_reset']
    if ob.parent not in prop:ob.matrix_world=sock.matrix_world
   render(f'{role}-{sex}-{clip}.png',(0,-.3,1.3),(2,-8,3),4.7,1000,800)
print('RENDERS COMPLETE')

