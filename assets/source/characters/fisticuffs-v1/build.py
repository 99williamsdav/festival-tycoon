import bpy,math,json,hashlib,struct
from pathlib import Path
from mathutils import Vector as V,Matrix as M,Quaternion as Q
R=Path(__file__).resolve().parent
FPS=24
CLIPS={'argue_gesture':18,'fight_enter':7,'fight_guard':32,'fight_swing_l':12,'fight_swing_r':12,'fight_flinch':10,'fight_recover':10,'fight_stand_down':8}
def smooth(t): t=max(0,min(1,t));return t*t*(3-2*t)
def pulse(t,peak=.3):return smooth(t/peak) if t<peak else 1-smooth((t-peak)/(1-peak))
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def meshhash(body):
 return hashlib.sha256(b''.join(struct.pack('<3f',*v.co) for v in body.data.vertices)+b''.join(struct.pack('<2f',*v.uv) for v in body.data.uv_layers.active.data)+repr([[tuple((g.group,round(g.weight,8))) for g in v.groups] for v in body.data.vertices]).encode()).hexdigest()
report={'fps':FPS,'forward':'Blender +Y / Godot -Z','root_motion':False,'assets':{}}
for sex in ['male','female']:
 bpy.ops.wm.open_mainfile(filepath=str(R/'references'/f'{sex}.blend'))
 rig=bpy.data.objects['LWF_Attendee_Rig'];body=bpy.data.objects['LWF_Attendee_Body']
 before=meshhash(body);rig.animation_data_clear()
 for act in list(bpy.data.actions):bpy.data.actions.remove(act)
 for o in list(bpy.data.objects):
  if o not in [rig,body]:bpy.data.objects.remove(o,do_unlink=True)
 pb=rig.pose.bones
 for b in pb:b.rotation_mode='QUATERNION';b.matrix_basis=M.Identity(4)
 bpy.context.view_layer.update()
 scale=1 if sex=='male' else .958
 maxreach=0
 def reset():
  for b in pb:b.matrix_basis=M.Identity(4)
  bpy.context.view_layer.update()
 def rotate(name,axis,angle):
  rest=rig.data.bones[name].matrix_local.to_3x3()
  pb[name].rotation_quaternion=Q(rest.inverted()@V(axis),math.radians(angle))
  bpy.context.view_layer.update()
 def aim(name,target):
  b=pb[name];b.rotation_quaternion=Q();b.location=V();bpy.context.view_layer.update()
  m=b.matrix.copy();q=m.col[1].xyz.normalized().rotation_difference((target-m.translation).normalized())
  b.matrix=M.Translation(m.translation)@q.to_matrix().to_4x4()@m.to_3x3().to_4x4()
  bpy.context.view_layer.update()
 def arm(side,wrist,handdir,pole):
  global maxreach
  shoulder=pb[side+'UpperArm'].head.copy();w=V(wrist)
  lu=rig.data.bones[side+'UpperArm'].length;ll=rig.data.bones[side+'LowerArm'].length
  d=(w-shoulder).length;maxreach=max(maxreach,d/(lu+ll))
  if d>(lu+ll)*.985:raise ValueError((sex,side,'overreach',d/(lu+ll)))
  axis=(w-shoulder).normalized();a=(lu*lu-ll*ll+d*d)/(2*d)
  p=V(pole);p-=axis*p.dot(axis)
  elbow=shoulder+axis*a+p.normalized()*math.sqrt(max(0,lu*lu-a*a))
  aim(side+'UpperArm',elbow);aim(side+'LowerArm',w)
  aim(side+'Hand',pb[side+'Hand'].head+V(handdir).normalized()*.095)
 def pose(name,t):
  reset()
  swing=name.startswith('fight_swing');fl=pulse(t,.3) if name=='fight_flinch' else 0
  beat=pulse(t,.45) if swing else 0
  attack_side='Left' if name.endswith('_l') else 'Right'
  attack_sign=-1 if attack_side=='Left' else 1
  breath=math.sin(math.tau*t) if name=='fight_guard' else 0
  lean=5.5*fl-.9*beat+.45*breath
  twist=-attack_sign*5*beat if swing else 0
  if name=='fight_recover':lean=1.8*math.sin(math.pi*t);twist=2*math.sin(math.pi*t)
  if name=='argue_gesture':
   u=pulse(t,.4);rotate('Chest',(0,0,1),-3*u);rotate('Head',(0,0,1),4*u)
   arm('Right',V((.255,.12,.99))*scale,V((.1,.8,.25)),(1,-.3,-1))
   targets={b.name:(b.rotation_quaternion.copy(),b.location.copy()) for b in pb}
   reset()
   for b in pb:b.rotation_quaternion=Q().slerp(targets[b.name][0],u);b.location=targets[b.name][1]*u
   bpy.context.view_layer.update();return
  rotate('Spine',(1,0,0),lean);rotate('Chest',(0,0,1),twist)
  rotate('Head',(1,0,0),-lean*.3)
  for side,sign in [('Left',-1),('Right',1)]:
   wrist=V((sign*.150,.205,1.34))*scale
   wrist.y+=.005*breath
   handdir=V((0,.28,1))
   if swing and side==attack_side:
    # Anticipate for 2 frames, then extend to frame5; return by frame12.
    sec=t*12
    if sec<2:amount=-.10*smooth(sec/2)
    elif sec<5:amount=-.10+1.10*smooth((sec-2)/3)
    else:amount=1-smooth((sec-5)/7)
    wrist+=V((sign*.006,.150,-.027))*scale*amount
    handdir=V((0,.28,1)).lerp(V((0,1,.18)),max(0,amount))
   if fl:
    wrist.y-=.025*fl;wrist.z+=.012*fl
   arm(side,wrist,handdir,(sign*.30,-.12,-1))
  if name in ['fight_enter','fight_stand_down']:
   weight=smooth(t) if name=='fight_enter' else 1-smooth(t)
   for b in pb:
    b.rotation_quaternion=Q().slerp(b.rotation_quaternion,weight);b.location*=weight
   bpy.context.view_layer.update()
 rec={'sex':sex,'source_sha256':sha(R/'references'/f'{sex}.blend'),'source_body_geometry_uv_weights_sha256':before,'clips':{},'bone_rest':{b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}}
 for name,intervals in CLIPS.items():
  rig.animation_data_create();action=bpy.data.actions.new(name);rig.animation_data.action=action
  previous={}
  for frame in range(intervals+1):
   pose(name,frame/intervals)
   for b in pb:
    q=b.rotation_quaternion
    if b.name in previous and q.dot(previous[b.name])<0:b.rotation_quaternion=-q
    previous[b.name]=b.rotation_quaternion.copy()
    b.keyframe_insert('rotation_quaternion',frame=frame);b.keyframe_insert('location',frame=frame);b.keyframe_insert('scale',frame=frame)
  for fc in action.fcurves:
   for kp in fc.keyframe_points:kp.interpolation='LINEAR'
  action.use_fake_user=True;tr=rig.animation_data.nla_tracks.new();tr.name=name;tr.strips.new(name,0,action);tr.mute=True
  rig.animation_data.action=None
  rec['clips'][name]={'frames':intervals+1,'start_frame':0,'end_frame':intervals,'duration_s':intervals/FPS,'loop':name=='fight_guard'}
 reset();assert before==meshhash(body)
 rec['max_arm_reach_fraction']=maxreach
 scene=bpy.context.scene;scene.render.fps=FPS;scene.frame_start=0;scene.frame_end=32
 bpy.context.preferences.filepaths.save_version=0
 # Source contains unchanged reference body for inspection; runtime carries no mesh.
 name=f'lwf_attendee_{sex}_fisticuffs_clips_v1'
 bpy.ops.wm.save_as_mainfile(filepath=str(R/'source'/f'{name}.blend'))
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
 path=R/'runtime'/f'{name}.glb'
 bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_yup=True,export_skins=True,export_animations=True,export_animation_mode='NLA_TRACKS',export_force_sampling=True,export_def_bones=False,export_frame_range=False)
 raw=path.read_bytes();j=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
 rec.update({'sha256':sha(path),'bytes':len(raw),'meshes':len(j.get('meshes',[])),'animations':[a['name'] for a in j.get('animations',[])]})
 assert rec['meshes']==0
 report['assets'][name]=rec
(R/'manifest.json').write_text(json.dumps(report,indent=2))
print('FISTICUFFS BUILD COMPLETE',json.dumps({k:{'reach':v['max_arm_reach_fraction'],'bytes':v['bytes']} for k,v in report['assets'].items()}))


