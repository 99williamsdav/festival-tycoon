"""Blender 4.1 reproducible H1/P1 production. Writes only beside this script.
blender --factory-startup -b -t 2 --python build.py [-- hay picnic]
"""
import bpy,bmesh,math,json,random,sys,hashlib,struct,zlib
from pathlib import Path
from mathutils import Vector as V, Matrix
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent
REPO=Path('C:/Projects/festival-tycoon')
for d in ['source','runtime','renders','verification','references']:(R/d).mkdir(exist_ok=True)
PAL={'straw':'EDBC42','strawLight':'F4C854','strawGold':'E4AF39','strawShade':'D8A335','strawPale':'FFD36A','twine':'716346','twineLight':'94805B','honey':'DCA15A','honeyLight':'E8B46B','honeyShade':'C78A45','grain':'BB7E3F','frame':'755735','frameLight':'8B673D','steel':'9C9E93'}
M={}; ASSET=[]; ANCHORS=[]; REVIEW=[]; GUESTS=[]
def mat(k):
 if k in M:return M[k]
 rgb=tuple(int(PAL[k][i:i+2],16)/255 for i in (0,2,4));m=bpy.data.materials.new('Dining_'+k);m.diffuse_color=(*rgb,1);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*rgb,1);bs.inputs['Roughness'].default_value=.87;M[k]=m;return m
def mesh(n,vs,fs,k):
 me=bpy.data.meshes.new(n);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(n,me);bpy.context.collection.objects.link(o);o.data.materials.append(mat(k));ASSET.append(o);return o
def box(n,c,d,k,bevel=0):
 bpy.ops.mesh.primitive_cube_add(size=1,location=c);o=bpy.context.object;o.name=n;o.dimensions=d;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mat(k));ASSET.append(o)
 if bevel:
  m=o.modifiers.new('Crafted edges','BEVEL');m.width=bevel;m.segments=1;bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=m.name)
 return o
def cyl(n,c,r,depth,k,axis='X'):
 bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=r,depth=depth,location=c);o=bpy.context.object;o.name=n;o.rotation_euler.y=math.pi/2 if axis=='X' else 0
 if axis=='Y':o.rotation_euler.x=math.pi/2
 o.data.materials.append(mat(k));ASSET.append(o);return o
def span(n,a,b,w,d,k):
 a,b=V(a),V(b);o=box(n,(a+b)/2,(d,w,(b-a).length),k,.003);o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o
def anchor(n,x,y,facing,seat=.45):
 o=bpy.data.objects.new(n,None);bpy.context.collection.objects.link(o);o.location=(x,y,seat);o.rotation_euler.z=facing;o.empty_display_type='ARROWS';o.empty_display_size=.14
 o['seat_surface_z']=seat;o['facing_local_axis']='Blender +Y; Godot -Z';o['review_only_pose']=True;ANCHORS.append(o);return o
def hay():
 # Connected chamfered shell. Broad facets keep the approved straw read at game distance.
 rng=random.Random(1729);xs=[-.585+1.17*i/13 for i in range(14)]
 cross=[(-.235,0),(.235,0),(.275,.035),(.278,.11),(.272,.19),(.275,.27),(.28,.35),(.275,.407),(.238,.45),(-.238,.45),(-.275,.412),(-.28,.34),(-.273,.26),(-.279,.18),(-.273,.10),(-.275,.035)]
 vs=[]
 for i,x in enumerate(xs):
  for j,(y,z) in enumerate(cross):
   # Top contact plane flat .45, perimeter deliberately irregular by millimetres.
   dy=rng.uniform(-.010,.010) if z not in [0,.45] else 0
   dz=rng.uniform(-.014,.014) if z not in [0,.45] else 0
   xx=x+rng.uniform(-.018,.018) if z not in [0,.45] else x
   vs.append((xx,y+dy,z+dz))
 n=len(cross);fs=[tuple(reversed(range(n))),tuple((len(xs)-1)*n+j for j in range(n))]
 for i in range(len(xs)-1):
  for j in range(n):
   a=i*n+j;b=(i+1)*n+j;c=(i+1)*n+(j+1)%n;d=i*n+(j+1)%n
   if j in [2,3,4,5,6,10,11,12,13,14]:
    # Irregular diagonal broad straw wedges, intrinsic to the closed solid.
    fs.extend([(a,b,d),(b,c,d)] if rng.random()<.5 else [(a,b,c),(a,c,d)])
   else:fs.append((a,b,c,d))
 o=mesh('H1 compressed irregular straw core',vs,fs,'straw');bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(o.data);bm.free()
 for k in ['strawLight','strawGold','strawShade','strawPale']:o.data.materials.append(mat(k))
 for f in o.data.polygons:f.material_index=rng.choices(range(5),[16,4,4,1,1])[0]
 # Coarse horizontal ribbons flush against top and sides. No individual straw geometry.
 for j in range(5):
  y=-.215+j*.086
  for i in range(9):
   x=-.57+i*.127;ln=rng.uniform(.09,.16);yy=y+rng.uniform(-.012,.012);w=rng.uniform(.022,.055)
   mesh('H1 broad top straw facet',[(x,yy,.4504),(min(.57,x+ln),yy+.015,.4504),(min(.57,x+ln-.016),yy+w,.4504),(x+.012,yy+w*.8,.4504)],[(0,1,2,3)],rng.choice(['straw','strawLight','strawPale','strawGold']))
 # Two simple continuous woven bands; low, slightly flattened profile.
 path=[(-.238,0),(.238,0),(.300,.035),(.300,.414),(.24,.455),(-.24,.455),(-.300,.414),(-.300,.035)]
 for ix in [-.475,.475]:
  vs=[(x,y,z) for x in [ix-.0125,ix+.0125] for y,z in path];fs=[]
  for j in range(8):fs.append((j,(j+1)%8,(j+1)%8+8,j+8))
  ob=mesh('H1 continuous twine band',vs,fs,'twine');ob.data.materials.append(mat('twineLight'))
  for p in ob.data.polygons:p.material_index=1 if p.index%3==0 else 0
  # Root grounded exactly: buried underside of twine is above zero to prevent stray bound.
  for v in ob.data.vertices:v.co.z=max(0,v.co.z)
 anchor('Seat_H1_01',-.286,.235,0);anchor('Seat_H1_02',.286,.235,0)
def picnic():
 for i in range(4):box('P1 tabletop plank %02d'%i,(0,-.306+i*.204,.720),(1.96,.196,.06),'honeyLight' if i%3==0 else 'honey',.005)
 for s in [-1,1]:
  for j in [-1,1]:box('P1 attached bench plank',(0,s*.705+j*.077,.4225),(1.96,.148,.055),'honeyLight' if j==1 else 'honey',.004)
 for x in [-.755,.755]:
  # End A-frames leave four seated positions within both frames.
  for s in [-1,1]:
   # Horizontal cuts give all four A-frame feet a genuine z=0 ground plane.
   poly=[(s*.775-.065,0),(s*.775+.065,0),(s*.265+.065,.69),(s*.265-.065,.69)]
   vs=[(xx,y,z) for xx in [x-.0425,x+.0425] for y,z in poly];fs=[(3,2,1,0),(4,5,6,7)]+[(i,(i+1)%4,(i+1)%4+4,i+4) for i in range(4)]
   leg=mesh('P1 dark splayed A-frame leg',vs,fs,'frame');bm=bmesh.new();bm.from_mesh(leg.data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(leg.data);bm.free()
   cyl('P1 visible leg bolt',(x+(.052 if x>0 else -.052),s*.545,.33),.017,.012,'steel')
  box('P1 under-table transverse bearer',(x,0,.673),(.115,.86,.05),'frameLight',.003)
  box('P1 transverse bench bearer',(x,0,.369),(.105,1.66,.055),'frameLight',.003)
  for yy in [-.306,-.102,.102,.306]:cyl('P1 tabletop bolt',(x,yy,.752),.009,.004,'steel','Z')
  for s in [-1,1]:
   for yy in [s*.705-.077,s*.705+.077]:cyl('P1 bench bolt',(x,yy,.452),.009,.004,'steel','Z')
 # Long low stretcher below shins and inside foot lanes.
 box('P1 longitudinal centre stretcher',(0,0,.255),(1.51,.065,.085),'frame',.003)
 # Sparse inset-style grain facets on planks, no photographic texture.
 for i in range(4):
  y=-.306+i*.204
  mesh('P1 broad timber grain', [(-.65,y-.026,.7503),(.49,y-.015,.7503),(.62,y-.006,.7503),(-.54,y-.016,.7503)],[(0,1,2,3)],'honeyShade')
  mesh('P1 light timber facet',[(-.89,y+.058,.7504),(.73,y+.052,.7504),(.49,y+.073,.7504),(-.65,y+.076,.7504)],[(0,1,2,3)],'honeyLight')
 for s in [-1,1]:
  for x in [-.372,.372]:anchor('Seat_P1_%s_%s'%('North' if s<0 else 'South','West' if x<0 else 'East'),x,s*.565,0 if s<0 else math.pi)
def palette():
 keys=list(PAL);w=len(keys)*8;raw=b''.join(b'\0'+b''.join(bytes.fromhex(PAL[keys[x//8]]) for x in range(w)) for y in range(8))
 def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
 p=R/'source'/'dining_palette.png';p.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,8,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))
 im=bpy.data.images.load(str(p),check_existing=True);im.pack();m=bpy.data.materials.new('Dining_SinglePalette');m.use_nodes=True;bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.87;t=m.node_tree.nodes.new('ShaderNodeTexImage');t.image=im;t.interpolation='Closest';m.node_tree.links.new(t.outputs['Color'],bs.inputs['Base Color'])
 for o in ASSET:
  uv=o.data.uv_layers.new(name='DiningPaletteUV');uv.active_render=True
  for p in o.data.polygons:
   key=next(k for k,v in M.items() if v==o.data.materials[p.material_index])
   for li in p.loop_indices:uv.data[li].uv=((keys.index(key)+.5)/len(keys),.5)
  o.data.materials.clear();o.data.materials.append(m)
def bounds(obs,evaluated=False):
 vs=[];dg=bpy.context.evaluated_depsgraph_get()
 for o in obs:
  if o.type!='MESH':continue
  ev=o.evaluated_get(dg) if evaluated else o
  vs.extend(ev.matrix_world@v.co for v in ev.data.vertices)
 return [[round(min(v[i] for v in vs),6) for i in range(3)],[round(max(v[i] for v in vs),6) for i in range(3)]]
def setup():
 s=bpy.context.scene;s.unit_settings.system='METRIC';s.render.engine='BLENDER_EEVEE';s.eevee.use_gtao=True;s.eevee.gtao_distance=1.4;s.eevee.gtao_factor=1.17;s.eevee.use_soft_shadows=True;s.eevee.taa_render_samples=96;s.render.resolution_x=1600;s.render.resolution_y=1000;s.render.resolution_percentage=100;s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'
 s.world=bpy.data.worlds.new('Review sky');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.55,.64,.75,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.65
 bpy.ops.object.light_add(type='SUN',location=(-4,6,9));o=bpy.context.object;o.name='REVIEW sun';o.data.energy=2.1;o.data.angle=.11;o.rotation_euler=(.4,-.5,-.4);REVIEW.append(o)
 bpy.ops.object.light_add(type='AREA',location=(0,1,6));o=bpy.context.object;o.name='REVIEW fill';o.data.energy=300;o.data.size=7;REVIEW.append(o)
 bpy.ops.mesh.primitive_plane_add(size=400,location=(0,0,-.002));o=bpy.context.object;o.name='REVIEW grass ground';m=bpy.data.materials.new('Review meadow');m.diffuse_color=(.24,.34,.105,1);o.data.materials.append(m);REVIEW.append(o)
 bpy.ops.object.camera_add();c=bpy.context.object;c.name='REVIEW camera';c.data.type='ORTHO';c.data.clip_end=1000;s.camera=c;REVIEW.append(c);return s,c
def render(s,c,name,scale,yaw=35,elev=35,target=(0,0,.5)):
 if '--check' in sys.argv:return
 t=V(target);a=math.radians(yaw);e=math.radians(elev);d=max(8,scale*2);c.location=t+V((math.sin(a)*d,math.cos(a)*d,math.tan(e)*d));c.rotation_euler=(t-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=scale;s.render.filepath=str(R/'renders'/name);bpy.ops.render.render(write_still=True)
def set_dir(rig,name,target):
 b=rig.pose.bones[name];head=b.head.copy();rot=b.bone.matrix_local.to_3x3();dire=(V(target)-head).normalized();b.matrix=Matrix.Translation(head)@(rot.col[1].rotation_difference(dire).to_matrix()@rot).to_4x4();bpy.context.view_layer.update()
def arm_to(rig,side,H):
 b=rig.pose.bones;S=b[side+'UpperArm'].head.copy();W=H.translation;lu=b[side+'UpperArm'].bone.length;lf=b[side+'LowerArm'].bone.length;ax=(W-S).normalized();d=min((W-S).length,(lu+lf)*.998);W=S+ax*d;a=(lu*lu-lf*lf+d*d)/(2*d);h=math.sqrt(max(lu*lu-a*a,0));pole=V((.7 if side=='Right' else -.7,-.4,-1));pole=(pole-ax*pole.dot(ax)).normalized();E=S+ax*a+pole*h
 set_dir(rig,side+'UpperArm',E);set_dir(rig,side+'LowerArm',W);b[side+'Hand'].matrix=Matrix.Translation(W)@H.to_3x3().to_4x4();bpy.context.view_layer.update()
def import_glb(path):
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path));obs=list(set(bpy.data.objects)-old)
 for o in obs:
  if o.type=='MESH':
   for m in o.data.materials:
    if m and m.use_nodes:
     for n in m.node_tree.nodes:
      if n.type=='TEX_IMAGE':
       # Always replace the importer placeholder for known external palettes.
       candidate=path.parent/(path.stem+'_attendee-palette.png')
       if candidate.exists():
        n.image=bpy.data.images.load(str(candidate),check_existing=False);n.image.pack();print('RESOLVED_IMAGE',str(candidate),list(n.image.size),flush=True)
       elif n.image is not None and n.image.size[0]>0:n.image.pack()
 return obs
def evaluated_vertices(o):
 ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());return [ev.matrix_world@v.co for v in ev.data.vertices]
def guest(sex,a,index,kind):
 path=REPO/'assets/source/characters/rigged-test-v1/out'/f'lwf_attendee_{sex}_rigged_test_v1.blend'
 with bpy.data.libraries.load(str(path),link=False) as (src,dst):dst.objects=list(src.objects);dst.actions=[n for n in src.actions if n=='eat']
 obs=dst.objects
 for o in obs:
  if o.name not in bpy.context.scene.objects:bpy.context.collection.objects.link(o)
 rig=next(o for o in obs if o.type=='ARMATURE');body=next(o for o in obs if o.type=='MESH');foodsocket=next(o for o in obs if o.name.startswith('LWF_RightHand_Food'))
 act=dst.actions[0];rig.animation_data_create();rig.animation_data.action=act;bpy.context.scene.frame_set(31);bpy.context.view_layer.update()
 frozen={b.name:b.matrix_basis.copy() for b in rig.pose.bones};rig.animation_data_clear()
 for b in rig.pose.bones:b.matrix_basis=frozen[b.name]
 bpy.context.view_layer.update();food_rel=rig.pose.bones['RightHand'].matrix.inverted()@foodsocket.matrix_world
 # Only upper body retains the existing native eat pose. Lower body is review-only seating IK.
 for b in rig.pose.bones:
  if b.name in ['Hips'] or 'Leg' in b.name or 'Foot' in b.name:b.matrix_basis=Matrix.Identity(4)
 bpy.context.view_layer.update()
 hip=rig.pose.bones['Hips'];hip.matrix=Matrix.Translation((0,0,.535))@hip.bone.matrix_local.to_3x3().to_4x4();bpy.context.view_layer.update()
 for side in ['Left','Right']:
  b=rig.pose.bones[side+'UpperLeg'];h=b.head.copy();L=b.bone.length
  set_dir(rig,b.name,h+V((0,math.sqrt(L*L-.065*.065),-.065)))
 # Put the evaluated buttock mesh (actual Hips-weighted vertices) in contact with .45 top.
 hipids=[v.index for v in body.data.vertices if any(body.vertex_groups[g.group].name=='Hips' and g.weight>.5 for g in v.groups)]
 vs=evaluated_vertices(body);contact=min(vs[i].z for i in hipids);dz=.451-contact
 hip.matrix=Matrix.Translation((0,0,dz))@hip.matrix;bpy.context.view_layer.update()
 # Ground feet using real foot mesh and an analytic lower leg direction, keeping thigh lengths native.
 for side in ['Left','Right']:
  rest=rig.data.bones[side+'Foot'].matrix_local.copy();knee=rig.pose.bones[side+'LowerLeg'].head.copy();L=rig.data.bones[side+'LowerLeg'].length;anklez=rig.data.bones[side+'Foot'].head_local.z-.00125;drop=knee.z-anklez
  if drop>L:raise RuntimeError('Seat too tall for native leg: '+sex)
  ankle=V((knee.x,knee.y-math.sqrt(max(0,L*L-drop*drop)),anklez));set_dir(rig,side+'LowerLeg',ankle)
  rig.pose.bones[side+'Foot'].matrix=Matrix.Translation(ankle)@rest.to_3x3().to_4x4();bpy.context.view_layer.update()
 # Raise actual held tray above picnic top; right palm retains source socket relationship.
 trayz=.87 if kind=='picnic' else .71;tray=Matrix.Translation((.15,.30,trayz));arm_to(rig,'Right',tray@food_rel.inverted());bpy.context.view_layer.update()
 # Hair is a separate current game attachment. Its coordinates are native head-space.
 hair=import_glb(REPO/'game/assets/characters'/f'lwf_hair_{sex}_default_v1.glb')
 head=rig.pose.bones['Head'];headrest=rig.data.bones['Head'].matrix_local
 # Current hair GLB is authored at the unposed world head location, attached by head delta.
 head_delta=head.matrix@headrest.inverted()
 for o in hair:
  if o.parent is None:o.matrix_world=head_delta@o.matrix_world
 food=import_glb(REPO/'game/assets/props'/('lwf_chips_tray_v1.glb' if index%2 else 'lwf_pizza_plate_v1.glb'))
 # Actual socket after IK is authoritative, props have root at held tray frame.
 bpy.context.view_layer.update();T=foodsocket.matrix_world.copy()
 for o in food:
  if o.parent is None:o.matrix_world=T@o.matrix_world
 transform=Matrix.Translation((a.location.x,a.location.y,0))@Matrix.Rotation(a.rotation_euler.z,4,'Z')
 for o in obs+hair+food:
  if o.parent is None:o.matrix_world=transform@o.matrix_world
 rig.name=f'REVIEW_{kind}_{index}_{sex}_native_rig';body.name=f'REVIEW_{kind}_{index}_{sex}_native_body'
 bpy.context.view_layer.update()
 # Native hand is a closed mitten. Lift held prop only until its shell touches
 # the palm without triangle penetration. Store the measured review grip offset.
 bt=tree([body])[0];offset=0
 for step in range(51):
  if not tree(food)[0].overlap(bt):break
  for o in food:
   if o.parent is None:o.location.z+=.001
  offset+=.001;bpy.context.view_layer.update()
 allobs=obs+hair+food
 for o in allobs:o['review_only']=True
 bpy.context.view_layer.update()
 # Pack all native image dependencies so review file is portable.
 for image in bpy.data.images:
  if image.source=='FILE' and not image.packed_file:
   try:image.pack()
   except:pass
 GUESTS.append({'sex':sex,'rig':rig,'body':body,'objects':allobs,'food':food,'anchor':a.name,'hips_ids':hipids,'food_socket':foodsocket,'review_grip_lift_m':offset})
 return allobs
def tree(obs):
 vs=[];faces=[];dg=bpy.context.evaluated_depsgraph_get()
 for o in obs:
  if o.type!='MESH':continue
  ev=o.evaluated_get(dg);me=ev.to_mesh();me.calc_loop_triangles();base=len(vs);vs.extend(ev.matrix_world@v.co for v in me.vertices);faces.extend(tuple(base+i for i in p.vertices) for p in me.loop_triangles);ev.to_mesh_clear()
 return BVHTree.FromPolygons(vs,faces,all_triangles=True),vs,faces
def clipped_under_table_max_z(vs,faces):
 values=[]
 for face in faces:
  poly=[vs[i] for i in face]
  for axis,limit,greater in [(0,-.98,True),(0,.98,False),(1,-.404,True),(1,.404,False)]:
   result=[]
   for i,p in enumerate(poly):
    q=poly[(i+1)%len(poly)];ip=p[axis]>=limit if greater else p[axis]<=limit;iq=q[axis]>=limit if greater else q[axis]<=limit
    if ip:result.append(p)
    if ip!=iq:result.append(p+(q-p)*((limit-p[axis])/(q[axis]-p[axis])))
   poly=result
   if not poly:break
  values.extend(p.z for p in poly)
 return max(values) if values else None
def inside(t,p):
 # Odd/even ray crossings; deterministic oblique direction avoids grid edges.
 direction=V((.873,.321,.366)).normalized();start=p.copy();count=0
 for i in range(30):
  hit,normal,index,distance=t.ray_cast(start,direction)
  if hit is None:break
  count+=1;start=hit+direction*.000001
 return count%2==1
def verify(kind):
 at,av,af=tree(ASSET);table=[o for o in ASSET if o.name.startswith('P1 tabletop')];tt=tree(table)[0] if table else None;records=[]
 for g in GUESTS:
  body=g['body'];bt,bv,bf=tree([body]);pairs=bt.overlap(at);vs=evaluated_vertices(body);rig=g['rig'];T=rig.matrix_world;feet={}
  for side in ['Left','Right']:
   ids=[v.index for v in body.data.vertices if any(body.vertex_groups[x.group].name==side+'Foot' and x.weight>.5 for x in v.groups)]
   feet[side]={'sole_min_z':round(min(vs[i].z for i in ids),6),'mesh_bounds':[[round(min(vs[j][i] for j in ids),5) for i in range(3)],[round(max(vs[j][i] for j in ids),5) for i in range(3)]]}
  hipmin=min(vs[i].z for i in g['hips_ids']);ft,_,_=tree(g['food']);food_pairs=ft.overlap(at);food_body=ft.overlap(bt)
  # Exact nearest distance from evaluated thigh vertices to table underside, where horizontally under top.
  thighids=[v.index for v in body.data.vertices if any(body.vertex_groups[x.group].name.endswith('UpperLeg') and x.weight>.5 for x in v.groups)]
  thighfaces=[f for f in bf if any(i in thighids for i in f)];maxthigh=clipped_under_table_max_z(vs,thighfaces) if kind=='picnic' else None
  foodgroups={}
  for fp,bp in food_body:
   for vi in bf[bp]:
    vertex=body.data.vertices[vi];gr=max(vertex.groups,key=lambda x:x.weight);n=body.vertex_groups[gr.group].name;foodgroups[n]=foodgroups.get(n,0)+1
  assetgroups={}
  for ao in ASSET:
   overlaps=bt.overlap(tree([ao])[0])
   if overlaps:
    groups={}
    for bp,_ in overlaps:
     for vi in bf[bp]:
      vertex=body.data.vertices[vi];gr=max(vertex.groups,key=lambda x:x.weight);n=body.vertex_groups[gr.group].name;groups[n]=groups.get(n,0)+1
    assetgroups[ao.name]={'triangles':len(overlaps),'body_vertex_group_votes':groups}
  (R/'verification'/(g['anchor']+'-intersection-detail.json')).write_text(json.dumps(assetgroups,indent=2))
  foodvs=tree(g['food'])[1];handids=[v.index for v in body.data.vertices if any(body.vertex_groups[x.group].name=='RightHand' and x.weight>.5 for x in v.groups)];min_dist=min([bt.find_nearest(v)[3] for v in foodvs]+[ft.find_nearest(vs[i])[3] for i in handids])
  contactpoints=[]
  for i in g['hips_ids']:
   p=vs[i]
   if p.z>.461:continue
   nearest,normal,fi,dist=at.find_nearest(p)
   if nearest is not None and abs(nearest.z-.45)<.002 and normal.z>.7 and dist<.011:contactpoints.append({'evaluated_pelvis_vertex':list(p),'nearest_seat_surface':list(nearest),'distance_m':round(dist,6)})
  (R/'verification'/(g['anchor']+'-contact-patch.json')).write_text(json.dumps(contactpoints,indent=2))
  if not contactpoints:raise RuntimeError('No actual supported pelvis contact at '+g['anchor'])
  # Triangle crossing alone cannot rule out containment. Test actual evaluated
  # vertices against every solid furniture component as a separate check.
  solids=[o for o in ASSET if ('core' in o.name or kind=='picnic') and not any(s in o.name for s in ['grain','facet'])]
  contained={}
  for so in solids:
   st=tree([so])[0];bb=bounds([so]);candidates=[p for p in vs+foodvs if all(bb[0][i]+.00001<p[i]<bb[1][i]-.00001 for i in range(3))]
   count=sum(inside(st,p) for p in candidates)
   if count:contained[so.name]=count
  records.append({'sex':g['sex'],'native_scale':list(rig.scale),'anchor':g['anchor'],'hips_contact_min_z':round(hipmin,6),'contact_gap_to_seat_m':round(hipmin-.45,6),'feet':feet,'body_asset_triangle_overlap_count':len(pairs),'body_overlapping_triangle_centroids_NOT_contact_coordinates':[list((bv[bf[p[0]][0]]+bv[bf[p[0]][1]]+bv[bf[p[0]][2]])/3) for p in pairs[:20]],'evaluated_body_or_food_vertices_inside_solid_furniture':contained,'food_asset_triangle_overlap_count':len(food_pairs),'food_body_triangle_overlap_count':len(food_body),'food_body_overlap_vertex_group_votes':foodgroups,'food_or_hand_nearest_vertex_surface_gap_m':round(min_dist,6) if min_dist else 0,'review_grip_lift_m':round(g['review_grip_lift_m'],3),'food_bounds':bounds(g['food'],True),'table_underside_thigh_gap_m':round(.690-maxthigh,5) if maxthigh is not None else None,'body_bounds':bounds([body],True)})
 neighbors=[]
 for i,g in enumerate(GUESTS):
  for h in GUESTS[i+1:]:
   gt,gv,_=tree([g['body']]);ht,hv,_=tree([h['body']]);fgt=tree(g['food'])[0];fht=tree(h['food'])[0]
   neighbors.append({'seats':[g['anchor'],h['anchor']],'body_body_triangles':len(gt.overlap(ht)),'food_food_triangles':len(fgt.overlap(fht)),'food_neighbor_body_triangles':len(fgt.overlap(ht))+len(fht.overlap(gt)),'nearest_body_vertex_surface_gap_m':round(min([ht.find_nearest(v)[3] for v in gv]+[gt.find_nearest(v)[3] for v in hv]),5)})
 (R/'verification'/(kind+'-neighbor-checks.json')).write_text(json.dumps(neighbors,indent=2))
 return records
def export(kind):
 base='ft_'+('hay_bale_h1' if kind=='hay' else 'picnic_aframe_p1')+'_v1';bpy.ops.object.select_all(action='DESELECT');copies=[]
 for o in ASSET:
  cp=o.copy();cp.data=o.data.copy();bpy.context.collection.objects.link(cp);cp.select_set(True);copies.append(cp)
 bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();combined=bpy.context.object;combined.name=base
 bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
 mod=combined.modifiers.new('Export triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name);combined.data.calc_loop_triangles();tri=len(combined.data.loop_triangles)
 for o in ANCHORS:o.select_set(True)
 bpy.ops.export_scene.gltf(filepath=str(R/'runtime'/(base+'.glb')),export_format='GLB',use_selection=True,export_yup=True,export_apply=True,export_extras=True,export_animations=False)
 rec={'asset':base,'triangles':tri,'mesh_vertices_blender':len(combined.data.vertices),'material_count':len(combined.data.materials),'bounds_blender':bounds(ASSET),'source_components':len(ASSET),'anchors':[{'name':a.name,'blender_local_surface_position':list(a.location),'blender_yaw_rad':a.rotation_euler.z,'godot_local_surface_position':[a.location.x,a.location.z,-a.location.y],'godot_facing':[round(-math.sin(a.rotation_euler.z),5),0,round(-math.cos(a.rotation_euler.z),5)]} for a in ANCHORS]}
 bpy.data.objects.remove(combined,do_unlink=True);return base,rec
def main():
 kinds=[k for k in sys.argv[sys.argv.index('--')+1:] if k in ['hay','picnic']] if '--' in sys.argv else ['hay','picnic']
 for kind in kinds:
  global M,ASSET,ANCHORS,REVIEW,GUESTS
  bpy.ops.wm.read_factory_settings(use_empty=True);M={};ASSET=[];ANCHORS=[];REVIEW=[];GUESTS=[]
  hay() if kind=='hay' else picnic();palette();bpy.context.view_layer.update();base,rec=export(kind);s,c=setup()
  for a in ANCHORS:a.hide_render=True
  bpy.ops.wm.save_as_mainfile(filepath=str(R/'source'/(base+'.blend')))
  scale=2.0 if kind=='hay' else 3.35
  render(s,c,kind+'-01-close.png',scale,yaw=-35,target=(0,0,.23 if kind=='hay' else .37))
  render(s,c,kind+'-02-rear.png',scale,yaw=145,target=(0,0,.23 if kind=='hay' else .37))
  render(s,c,kind+'-03-above.png',scale,yaw=0,elev=85,target=(0,0,.23 if kind=='hay' else .37))
  for i,a in enumerate(ANCHORS):guest('male' if i%2==0 else 'female',a,i,kind)
  rec['seating_verification']=verify(kind);(R/'verification'/(kind+'-metrics.json')).write_text(json.dumps(rec,indent=2));print('SEAT_METRICS',json.dumps(rec),flush=True)
  bpy.ops.wm.save_as_mainfile(filepath=str(R/'source'/(base+'_occupied_review.blend')))
  render(s,c,kind+'-04-occupied.png',3.4 if kind=='hay' else 4.2,yaw=-35,target=(0,.28,.65))
  render(s,c,kind+'-05-occupied-rear.png',3.4 if kind=='hay' else 4.2,yaw=145,target=(0,.28,.65))
  render(s,c,kind+'-06-contact-side.png',3.4 if kind=='hay' else 4.0,yaw=90,elev=4,target=(0,.10,.66))
  # Match actual project camera view height and azimuth. Full frame, no enlargement.
  s.render.resolution_x=1600;s.render.resolution_y=900
  render(s,c,kind+'-07-gameplay-min18.png',32,yaw=325,elev=38.853,target=(0,0,.5))
  render(s,c,kind+'-08-gameplay-default62.png',110.222222,yaw=325,elev=38.853,target=(0,0,.5))
  for g in GUESTS:
   for o in g['objects']:o.hide_render=True
  render(s,c,kind+'-09-gameplay-empty-min18.png',32,yaw=325,elev=38.853,target=(0,0,.5))
  rec['sha256_glb']=hashlib.sha256((R/'runtime'/(base+'.glb')).read_bytes()).hexdigest();(R/('manifest-'+kind+'.json')).write_text(json.dumps(rec,indent=2))
 print('DINING_PRODUCTION_COMPLETE',flush=True)
if __name__=='__main__':main()
