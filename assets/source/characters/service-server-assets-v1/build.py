import bpy,bmesh,math,json,hashlib,struct,zlib,sys
from pathlib import Path
from mathutils import Vector as V, Matrix as M, Quaternion as Q
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent
REPO=Path('C:/Projects/festival-tycoon')
report={'assets':{},'units':'metres','forward':'Godot -Z','fps':24}
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def gd(p):return [round(p.x,6),round(p.z,6),round(-p.y,6)]
def palette(name,cols):
 def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
 raw=b''.join(b'\0'+b''.join(bytes.fromhex(c)*8 for c in cols) for _ in range(8))
 p=R/'textures'/f'{name}.png';p.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',8*len(cols),8,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))
 im=bpy.data.images.load(str(p));im.pack()
 mat=bpy.data.materials.new(name);mat.use_nodes=True;mat.use_backface_culling=False
 tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;tex.interpolation='Closest'
 bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.88;mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
 return mat
def simplemesh(name,verts,faces,mat,slots):
 me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.materials.append(mat);uv=me.uv_layers.new()
 for p in me.polygons:
  for li in p.loop_indices:uv.data[li].uv=((slots[p.index]+.5)/16,.5)
 bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
 ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob);return ob
def imported(path):
 before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path));return list(set(bpy.data.objects)-before)
def glb_report(path):
 raw=path.read_bytes();j=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
 return {'sha256':sha(path),'bytes':len(raw),'triangles':sum(j['accessors'][p['indices']]['count']//3 for m in j.get('meshes',[]) for p in m['primitives']),'surfaces':sum(len(m['primitives']) for m in j.get('meshes',[])),'animations':[a['name'] for a in j.get('animations',[])],'bones':len(j.get('skins',[{}])[0].get('joints',[])),'materials':len(j.get('materials',[]))}
def export(name,objects,animated=True):
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects:o.select_set(True)
 bpy.context.view_layer.objects.active=next(o for o in objects if o.type=='MESH')
 path=R/'runtime'/f'{name}.glb'
 bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_yup=True,export_skins=True,export_animations=animated,export_animation_mode='NLA_TRACKS',export_force_sampling=True,export_def_bones=False,export_frame_range=False)
 bpy.ops.wm.save_as_mainfile(filepath=str(R/'source'/f'{name}.blend'))
 report['assets'][name]=glb_report(path)
 return report['assets'][name]
def smooth(x):x=max(0,min(1,x));return x*x*(3-2*x)

for role in ['bar','food']:
 for sex in ['male','female']:
  bpy.ops.wm.open_mainfile(filepath=str(R/'references'/f'{sex}.blend'))
  rig=bpy.data.objects['LWF_Attendee_Rig'];body=bpy.data.objects['LWF_Attendee_Body']
  cup=bpy.data.objects['LWF_RightHand_Cup'];food=bpy.data.objects['LWF_RightHand_Food']
  rig.animation_data_clear()
  for ob in list(bpy.data.objects):
   if ob not in [rig,body,cup,food]:bpy.data.objects.remove(ob,do_unlink=True)
  for act in list(bpy.data.actions):bpy.data.actions.remove(act)
  pb=rig.pose.bones
  for b in pb:b.rotation_mode='QUATERNION';b.matrix_basis=M.Identity(4)
  bpy.context.view_layer.update()
  # Recover exact shipped socket-to-hand transforms in rest, including Blender bone-tail parenting.
  hand_in={key:(rig.matrix_world.inverted()@sock.matrix_world).inverted()@pb['RightHand'].matrix for key,sock in [('cup',cup),('food',food)]}
  oldmat=body.data.materials[0];im=next(n.image for n in oldmat.node_tree.nodes if n.type=='TEX_IMAGE')
  px=list(im.pixels);w,h=im.size
  # Use original skin/face shades, replace only clothing palette swatches.
  cols=[]
  for i in range(12):
   at=(int(h/2)*w+min(w-1,int((i+.5)/12*w)))*4
   # Packed byte palette pixels are already sRGB; preserve exact source bytes.
   cols.append(''.join(f'{round(max(0,min(1,px[at+k]))*255):02X}' for k in range(3)))
  cols[3]='E6DFCB' if role=='bar' else '84967B';cols[5]=cols[3];cols[7]='303C43';cols[9]='222B30'
  cols+=['24565B' if role=='bar' else 'DDD3B7','2C6367' if role=='bar' else 'C5B99B','C7AA50' if role=='bar' else '24565B','84967B']
  mat=palette('lwf_server_'+role+'_palette_v1',cols);body.data.materials.clear();body.data.materials.append(mat)
  for uv in body.data.uv_layers.active.data:
   slot=min(11,int(uv.uv.x*12));uv.uv.x=(slot+.5)/16
  # Same geometry and weights as approved body, only material reassigned.
  source_geometry_hash=hashlib.sha256(b''.join(struct.pack('<3f',*v.co) for v in body.data.vertices)).hexdigest()
  bm=bmesh.new();bm.from_mesh(body.data)
  tee_faces=[list(p.vertices) for p in body.data.polygons if int(body.data.uv_layers.active.data[p.loop_start].uv.x*16) in (3,5)]
  tee_tree=BVHTree.FromPolygons([v.co for v in body.data.vertices],tee_faces)
  tree=BVHTree.FromBMesh(bm)
  scale=1 if sex=='male' else .958
  verts=[];faces=[];slots=[]
  def poly(v,slot):
   ix=len(verts);verts.extend(v);faces.append(tuple(range(ix,ix+len(v))));slots.append(slot)
  def front(x,z,extra=.018):
   hit=tree.ray_cast(V((x,2,z)),V((0,-1,0)),3)[0]
   return max(.12,hit.y if hit is not None else .12)+extra
  # Bib and skirt: low-poly strips fitted to actual torso and hips.
  rings=[]
  for z,half in [(.61,.185),(.82,.18),(.98,.165),(1.10,.145),(1.20,.13),(1.32,.13)]:
   z*=scale;half*=scale
   rings.append([(x,front(x,z),z) for x in [-half,0,half]])
  for a,b in zip(rings,rings[1:]):
   for i in range(2):poly([a[i],a[i+1],b[i+1],b[i]],12)
  # One broad shallow pocket; no bulky tools.
  for za,zb in [(.87,1.015)]:
   za*=scale;zb*=scale;x=.126*scale
   poly([(-x,front(-x,za,.026),za),(x,front(x,za,.026),za),(x,front(x,zb,.026),zb),(-x,front(-x,zb,.026),zb)],13)
  # Two straps over shoulders and simple back waist band.
  for sign in [-1,1]:
   x=sign*.113*scale
   top=tee_tree.ray_cast(V((x,0,2)),V((0,0,-1)),2)[0]
   topz=top.z+.009
   path=[(front(x,1.30*scale,.017),1.30*scale),(.065,topz-.022),(0,topz),(-.06,topz-.022),(-.12,1.12*scale),(-.145,1.01*scale)]
   for (y,z),(yy,zz) in zip(path,path[1:]):poly([(x-.017,y,z),(x+.017,y,z),(x+.017,yy,zz),(x-.017,yy,zz)],12)
  for i in range(8):
   a=math.tau*i/8;b=math.tau*(i+1)/8
   poly([(.178*scale*math.cos(a),.15*math.sin(a),.99*scale),(.178*scale*math.cos(b),.15*math.sin(b),.99*scale),(.178*scale*math.cos(b),.15*math.sin(b),1.02*scale),(.178*scale*math.cos(a),.15*math.sin(a),1.02*scale)],12)
  z=1.30*scale;x=.045*scale
  poly([(x,front(x,z,.027),z),(x+.045,front(x+.045,z,.027),z),(x+.045,front(x+.045,z+.016,.027),z+.016),(x,front(x,z+.016,.027),z+.016)],14)
  bm.free()
  apron=simplemesh('LWF_Server_Apron',verts,faces,mat,slots);apron.parent=rig
  for n in ['Hips','Spine','Chest']:apron.vertex_groups.new(name=n)
  for v in apron.data.vertices:
   z=v.co.z/scale;a=smooth((z-1.00)/.12);c=smooth((z-1.12)/.12)
   weights={'Hips':1-a,'Spine':a*(1-c),'Chest':a*c}
   for n,wgt in weights.items():
    if wgt:apron.vertex_groups[n].add([v.index],wgt,'REPLACE')
  apron.modifiers.new('Armature','ARMATURE').object=rig
  # Existing head assets preserved, remap cap to sage using same shared palette.
  heads=[]
  hp=f'lwf_hair_{sex}_default'+('_under_cap_v1.glb' if role=='food' else '_v1.glb')
  for path in [hp]+([f'lwf_hat_cap_{sex}_v1.glb'] if role=='food' else []):
   obs=imported(REPO/'game/assets/characters'/path)
   for ob in obs:
    if ob.type!='MESH':
     bpy.data.objects.remove(ob,do_unlink=True);continue
    world=ob.matrix_world.copy();ob.parent=None;ob.matrix_world=world
    for v in ob.data.vertices:v.co=world@v.co
    ob.matrix_world=M.Identity(4)
    ob.data.materials.clear();ob.data.materials.append(mat)
    uv=ob.data.uv_layers.active or ob.data.uv_layers.new()
    for loop in uv.data:loop.uv=((15.5 if 'hat' in path else 10.5)/16,.5)
    ob.vertex_groups.clear();vg=ob.vertex_groups.new(name='Head');vg.add(list(range(len(ob.data.vertices))),1,'REPLACE')
    ob.parent=rig;ob.modifiers.new('Armature','ARMATURE').object=rig;heads.append(ob)
  # Exact socket solver from existing rig conventions, two-bone analytic IK.
  max_reach=0
  def reset():
   for b in pb:b.matrix_basis=M.Identity(4)
   bpy.context.view_layer.update()
  def rotate(name,axis,deg):
   rest=rig.data.bones[name].matrix_local.to_3x3();pb[name].rotation_quaternion=Q(rest.inverted()@V(axis),math.radians(deg))
  def aim(name,target):
   b=pb[name];b.rotation_quaternion=Q();b.location=V();bpy.context.view_layer.update()
   m=b.matrix.copy();q=m.col[1].xyz.normalized().rotation_difference((target-m.translation).normalized())
   b.matrix=M.Translation(m.translation)@q.to_matrix().to_4x4()@m.to_3x3().to_4x4();bpy.context.view_layer.update()
  def solve(C,kind):
   global max_reach
   H=C@hand_in[kind];W=H.translation.copy();bpy.context.view_layer.update();S=pb['RightUpperArm'].matrix.translation.copy()
   Lu=rig.data.bones['RightUpperArm'].length;Lf=rig.data.bones['RightLowerArm'].length;d=(W-S).length
   max_reach=max(max_reach,d/(Lu+Lf))
   if d>(Lu+Lf)*.995:raise RuntimeError(f'Unreachable {role}/{sex}: {d/(Lu+Lf)} {C.translation}')
   a=(Lu*Lu-Lf*Lf+d*d)/(2*d);h=math.sqrt(max(0,Lu*Lu-a*a));axis=(W-S).normalized();pole=V((.8,-1,-.2) if role=='bar' else (.35,-.55,-1));pole-=axis*pole.dot(axis)
   elbow=S+axis*a+pole.normalized()*h
   aim('RightUpperArm',elbow);aim('RightLowerArm',W)
   pb['RightHand'].matrix=M.Translation(pb['RightHand'].matrix.translation)@H.to_3x3().to_4x4();bpy.context.view_layer.update()
  kind='cup' if role=='bar' else 'food'
  # Body forward +Y; yaw PI in Godot puts the item toward the vendor +Z front.
  height=1.35 if role=='bar' else 1.10
  x=.19 if sex=='male' else .17
  low=V((x,.02 if role=='bar' else .15,1.07 if role=='bar' else 1.02))
  pickup=V((x,.28,height));offer=V((x,.44,height+.015))
  clips=[('server_idle',97),('server_turn',9),('server_pickup',13),('server_offer',13),('server_retract',13),('server_reset',25)]
  if role=='bar':clips+=[('server_offer_soft',13)]
  spec={}
  for name,nframes in clips:
   rig.animation_data_create();action=bpy.data.actions.new(name);rig.animation_data.action=action
   for i in range(nframes):
    fr=i+1;t=i/(nframes-1);u=smooth(t);reset()
    if name=='server_idle':
     rotate('Spine',(1,0,0),.45*math.sin(math.tau*t));rotate('Chest',(0,1,0),.65*math.sin(math.tau*t))
     rotate('Head',(0,0,1),3*math.sin(math.tau*t));rotate('RightLowerArm',(1,0,0),5);rotate('LeftLowerArm',(1,0,0),5)
    elif name=='server_turn':
     rotate('Chest',(0,0,1),-5*math.sin(math.pi*t));rotate('Head',(0,0,1),-12*math.sin(math.pi*t))
     rotate('RightLowerArm',(1,0,0),5);rotate('LeftLowerArm',(1,0,0),5)
    elif name=='server_reset':
     # Brief empty-hand stock adjustment gesture, not an endless wipe loop.
     p=pickup+V((.055*math.sin(math.tau*t),-.035,.035*math.sin(math.pi*t)))
     solve(M.Translation(p),kind);rotate('Head',(1,0,0),3*math.sin(math.pi*t))
    else:
     if name=='server_pickup':
      high=V((x,low.y,height))
      p=low.lerp(high,smooth(t*2)) if t<.5 else high.lerp(pickup,smooth((t-.5)*2))
     elif name in ['server_offer','server_offer_soft']:p=pickup.lerp(offer,u)
     else:
      high=V((x,low.y,height))
      p=offer.lerp(high,smooth(t*2)) if t<.5 else high.lerp(low,smooth((t-.5)*2))
     solve(M.Translation(p),kind)
     rotate('LeftLowerArm',(1,0,0),5)
    for b in pb:
     b.keyframe_insert('rotation_quaternion',frame=fr);b.keyframe_insert('location',frame=fr);b.keyframe_insert('scale',frame=fr)
   for fc in action.fcurves:
    for kp in fc.keyframe_points:kp.interpolation='LINEAR'
   action.use_fake_user=True;track=rig.animation_data.nla_tracks.new();track.name=name;track.strips.new(name,1,action);track.mute=True;rig.animation_data.action=None
   spec[name]={'frames':nframes,'duration_s':(nframes-1)/24,'loop':name=='server_idle'}
  reset();bpy.context.scene.render.fps=24;bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=97
  name=f'lwf_server_{role}_{sex}_v1'
  rec=export(name,[rig,body,apron,cup,food]+heads)
  rec.update({'role':role,'sex':sex,'clips':spec,'body_geometry_sha256':source_geometry_hash,'max_arm_reach_fraction':max_reach,'source_blend_sha256':sha(R/'references'/f'{sex}.blend'),'socket':cup.name if role=='bar' else food.name,'pickup_local_godot':gd(pickup),'offer_local_godot':gd(offer),'vendor_root_position':[.35,0,.30] if role=='bar' else [0,.47,.88],'vendor_yaw_rad':math.pi})
  (R/'manifest.json').write_text(json.dumps(report,indent=2))

# Open only the front serving region in a distinct derivative of the current chassis.
bpy.ops.wm.read_factory_settings(use_empty=True)
obs=imported(REPO/'game/assets/environment/lwf_food_van_chassis_v1.glb');ob=next(o for o in obs if o.type=='MESH')
me=ob.data;uv=me.uv_layers.active;keep=[];removed=[];front_slot=None
def gcoord(v):return V((v.co.x,v.co.z,-v.co.y))
for p in me.polygons:
 pts=[gcoord(me.vertices[i]) for i in p.vertices]
 lo=[min(v[a] for v in pts) for a in range(3)];hi=[max(v[a] for v in pts) for a in range(3)]
 panel=lo[0]>=.8999 and hi[0]<=3.4001 and lo[1]>=1.2099 and hi[1]<=2.2301 and lo[2]>=1.0924 and hi[2]<=1.1376
 front=all(abs(v.z-1.1)<.0001 for v in pts) and lo[0]<.0001 and hi[0]>4.1999 and lo[1]<.4301 and hi[1]>2.6099
 if panel or front:
  removed.append({'polygon':p.index,'reason':'dark hatch panel' if panel else 'solid front wall','vertices_godot':[list(v) for v in pts]})
  if front:front_slot=(p.material_index,[tuple(uv.data[i].uv) for i in p.loop_indices])
 else:keep.append(p)
verts=[];faces=[];uvs=[];mats=[]
for p in keep:
 j=len(verts);verts += [tuple(me.vertices[i].co) for i in p.vertices];faces.append(tuple(range(j,len(verts))));uvs.append([tuple(uv.data[i].uv) for i in p.loop_indices]);mats.append(p.material_index)
# Four coplanar remaining front-wall rectangles, same material/UV, preserve external silhouette.
for x0,x1,z0,z1 in [(0,.9,.43,2.61),(3.4,4.2,.43,2.61),(.9,3.4,.43,1.21),(.9,3.4,2.23,2.61)]:
 pts=[(x0,-1.1,z0),(x1,-1.1,z0),(x1,-1.1,z1),(x0,-1.1,z1)]
 j=len(verts);verts+=pts;faces.append(tuple(range(j,j+4)));uvs.append([front_slot[1][0]]*4);mats.append(front_slot[0])
new=bpy.data.meshes.new('FoodVanOpenedChassis');new.from_pydata(verts,[],faces)
for mat in me.materials:new.materials.append(mat)
uv=new.uv_layers.new()
for p,coords,idx in zip(new.polygons,uvs,mats):
 p.material_index=idx
 for li,coord in zip(p.loop_indices,coords):uv.data[li].uv=coord
ob.data=new;ob.name='lwf_food_van_chassis_open_service_v1'
rec=export('lwf_food_van_chassis_open_service_v1',obs,False)
rec.update({'source':'game/assets/environment/lwf_food_van_chassis_v1.glb','source_sha256':sha(REPO/'game/assets/environment/lwf_food_van_chassis_v1.glb'),'removed_polygons':removed,'replacement_front_rectangles':4,'opening_assembly_godot':{'x':[.9,3.4],'y':[1.21,2.23],'front_z':1.1},'unchanged':'all other polygons, UVs, materials, origin, counter, chassis, roof, wheels, supports; fascia/sign modules remain separate'})
(R/'manifest.json').write_text(json.dumps(report,indent=2))
print('SERVER BUILD COMPLETE')

