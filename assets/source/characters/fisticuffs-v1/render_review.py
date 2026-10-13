import bpy,math,json,sys
from pathlib import Path
from mathutils import Vector as V,Matrix as M
R=Path(__file__).resolve().parent;REPO=Path('C:/Projects/festival-tycoon')
quick='--quick' in sys.argv
def actor(sex,name,loc,yaw,role="attendee"):
 path=R/'source'/f'lwf_attendee_{sex}_fisticuffs_clips_v1.blend' if role=='attendee' else REPO/'assets/source/characters/rigged-test-v1/out'/f'lwf_{role}_{sex}_rigged_test_v1.blend'
 with bpy.data.libraries.load(str(path),link=False) as (a,b):
  b.objects=[x for x in a.objects if x in ['LWF_Attendee_Rig','LWF_Attendee_Body']]
 for o in b.objects:bpy.context.collection.objects.link(o)
 rig=next(o for o in b.objects if o.type=='ARMATURE');body=next(o for o in b.objects if o.type=='MESH')
 acts={tr.name:tr.strips[0].action for tr in rig.animation_data.nla_tracks}
 rig.animation_data.action=None
 if role!='attendee':
  names=list(json.loads((R/'manifest.json').read_text())['assets'][f'lwf_attendee_{sex}_fisticuffs_clips_v1']['clips'])
  with bpy.data.libraries.load(str(R/'source'/f'lwf_attendee_{sex}_fisticuffs_clips_v1.blend'),link=False) as (aa,bb):bb.actions=names.copy()
  acts=dict(zip(names,bb.actions))
 # Keep original shapes/materials; imported default hair uses existing palette.
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(REPO/'game/assets/characters'/f'lwf_hair_{sex}_default_v1.glb'))
 hair=[o for o in set(bpy.data.objects)-old if o.type=='MESH']
 for o in hair:
  o.data.transform(o.matrix_world);o.matrix_world=M.Identity(4);o.parent=rig
  vg=o.vertex_groups.new(name='Head');vg.add(list(range(len(o.data.vertices))),1,'REPLACE');o.modifiers.new('HeadAttach','ARMATURE').object=rig
 # Review-only standard recolour from shipped contract for second adult.
 if name=='B':
  contract=json.loads((REPO/'game/assets/characters/attendee_palette_v1_contract.json').read_text())
  cols=contract['clothing_colourways'][2]['slots']
  for idx,mat in enumerate(body.data.materials):
   mat=mat.copy();body.data.materials[idx]=mat
   for n in mat.node_tree.nodes:
    if n.type=='TEX_IMAGE':
     im=n.image.copy();px=list(im.pixels);w,h=im.size
     for slot,hx in cols.items():
      rgb=[int(hx[i:i+2],16)/255 for i in (0,2,4)]
      for yy in range(h):
       for xx in range(int(slot)*8,int(slot)*8+8):
        j=(yy*w+xx)*4;px[j:j+3]=rgb
     im.pixels=px;n.image=im
 root=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(root);rig.parent=root;root.location=loc;root.rotation_euler.z=yaw
 return {'rig':rig,'body':body,'actions':acts,'root':root}
def pose(a,name,fr):
 rig=a['rig'];rig.animation_data.action=a['actions'][name];bpy.context.scene.frame_set(int(fr),subframe=fr%1);bpy.context.view_layer.update()
 mats={b.name:b.matrix_basis.copy() for b in rig.pose.bones};rig.animation_data.action=None
 for b in rig.pose.bones:b.matrix_basis=mats[b.name]
 bpy.context.view_layer.update()
def setup():
 s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=16;s.cycles.use_denoising=True;s.render.threads_mode='FIXED';s.render.threads=6
 s.world=bpy.data.worlds.new('ReviewWorld');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.7,.75,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.6
 s.view_settings.view_transform='Standard'
 bpy.ops.mesh.primitive_plane_add(size=160,location=(0,0,-.003));m=bpy.data.materials.new('Ground');m.diffuse_color=(.30,.38,.20,1);bpy.context.object.data.materials.append(m)
 bpy.ops.object.light_add(type='AREA',location=(2,-4,8));bpy.context.object.data.energy=650;bpy.context.object.data.size=5
 bpy.ops.object.camera_add();s.camera=bpy.context.object;s.camera.data.type='ORTHO'
 return s
def render(name,offset,scale,w=1100,h=800,target=(0,0,.85)):
 s.render.resolution_x=w;s.render.resolution_y=h;s.render.resolution_percentage=100
 c=s.camera;c.data.ortho_scale=scale;c.location=V(target)+V(offset);c.rotation_euler=(V(target)-c.location).to_track_quat('-Z','Y').to_euler()
 s.render.filepath=str(R/'review'/name);bpy.ops.render.render(write_still=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
a=actor('male','A',(0,-.45,0),0);b=actor('female','B',(0,.45,0),math.pi)
s=setup()
pose(a,'fight_swing_r',5);pose(b,'fight_flinch',2)
render('pair-side.png',(6,0,1),3.5)
render('pair-three-quarter.png',(5,-7,3),3.7)
if quick:print('QUICK COMPLETE');raise SystemExit()
for i,yaw in enumerate([45,135,225,315]):
 t=math.radians(yaw);off=(72*math.sin(t),-72*math.cos(t),58)
 render(f'angle-{i}.png',off,4,1100,800)
 render(f'game-min18-{i}.png',off,32,1280,720,(0,0,0))
 render(f'game-default62-{i}.png',off,62*16/9,1280,720,(0,0,0))
for title,ca,fa,cb,fb in [('argue','argue_gesture',8,'argue_gesture',3),('guard','fight_guard',0,'fight_guard',0),('swing-left','fight_swing_l',5,'fight_flinch',2),('return','fight_swing_r',10,'fight_flinch',8),('reverse','fight_flinch',2,'fight_swing_l',5),('stand-down','fight_stand_down',8,'fight_stand_down',8)]:
 pose(a,ca,fa);pose(b,cb,fb);render(title+'.png',(5,-7,3),3.7)
for distance in [.75,.85,.9,1.0,1.15]:
 a['root'].location.y=-distance/2;b['root'].location.y=distance/2
 pose(a,'fight_swing_r',5);pose(b,'fight_guard',0);render(f'spacing-{distance:.2f}.png',(6,0,1),3.5)
# Actual motion strip as sequential frames (for browser playback, no synthetic inbetween images).
a['root'].location.y=-.45;b['root'].location.y=.45
for i in range(49):
 t=i/12
 if t<.3:ac,af,bc,bf='fight_enter',min(7,t*24),'fight_enter',min(7,t*24)
 elif t<.8:ac,af,bc,bf='fight_guard',0,'fight_guard',0
 elif t<1.3:
  sec=t-.8;ac,af='fight_swing_r',min(12,sec*24);bc,bf=('fight_flinch',min(10,max(0,sec-.16)*24)) if sec>=.16 else ('fight_guard',0)
 elif t<2.0:ac,af,bc,bf='fight_guard',0,'fight_guard',0
 elif t<2.5:
  sec=t-2;bc,bf='fight_swing_l',min(12,sec*24);ac,af=('fight_flinch',min(10,max(0,sec-.16)*24)) if sec>=.16 else ('fight_guard',0)
 elif t<3.1:ac,af,bc,bf='fight_guard',0,'fight_guard',0
 else:ac,af,bc,bf='fight_stand_down',min(8,(t-3.1)*24),'fight_stand_down',min(8,(t-3.1)*24)
 pose(a,ac,af);pose(b,bc,bf);render(f'motion-{i:03d}.png',(5,-7,3),3.7,640,480)
print('REVIEW COMPLETE')



