"""Approved cow concept -> hand-shaped ring topology, palette and economical rig."""
import bpy, bmesh, math, json, hashlib
from pathlib import Path
from mathutils import Vector, Matrix
R=Path(__file__).resolve().parent
for d in ('source','runtime','textures','review'): (R/d).mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
PAL=['E7DCC2','DED0B3','302F2D','514D44','BE8E7D','A77467','36332E','171B18','EFE3CB']
im=bpy.data.images.new('CowPalette',width=len(PAL),height=1)
pixels=[]
for c in PAL: pixels.extend([int(c[i:i+2],16)/255 for i in (0,2,4)]+[1])
im.pixels=pixels; im.filepath_raw=str(R/'textures/lwf_cow_palette_v3.png');im.file_format='PNG';im.save();im.pack()
mat=bpy.data.materials.new('Cow_MattePalette');mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.92
tx=mat.node_tree.nodes.new('ShaderNodeTexImage');tx.image=im;tx.interpolation='Closest';mat.node_tree.links.new(tx.outputs['Color'],bs.inputs['Base Color'])
verts=[];faces=[];cols=[];weights=[];pink_vertices=[]
def mesh(v,f,c,w):
 b=len(verts);verts.extend(v);faces.extend([tuple(b+i for i in p) for p in f]);cols.extend(c if isinstance(c,list) else [c]*len(f));weights.extend(w if isinstance(w,list) else [w]*len(v))
 colors=c if isinstance(c,list) else [c]
 groups=w if isinstance(w,list) else [w]
 if all(col in (4,5) for col in colors) and all(wg=={'Head':1} for wg in groups):pink_vertices.extend(range(b,b+len(v)))
def tube(rings,n=10,c=0,bone='Body',patch=None):
 # ring: (centre, width Y, depth Z); axis X, explicit hand-shaped sections.
 v=[]; w=[]
 for p,ry,rz in rings:
  for j in range(n):
   a=2*math.pi*j/n
   jitter=.018*math.sin(j*2.1+len(v)//n*1.7) if patch and callable(bone) else 0
   v.append((p[0]+jitter,p[1]+ry*math.cos(a),p[2]+rz*math.sin(a)));w.append(bone(p) if callable(bone) else {bone:1})
 f=[tuple(reversed(range(n)))];cl=[c]
 for i in range(len(rings)-1):
  for j in range(n):
   k=(j+1)%n;ids=(i*n+j,i*n+k,(i+1)*n+k,(i+1)*n+j)
   centre=sum((Vector(v[q]) for q in ids),Vector())/4
   color=patch(centre,i,j) if patch else c
   # Alternating diagonals avoid regular stripe grid.
   f.extend([(ids[0],ids[1],ids[2]),(ids[0],ids[2],ids[3])]);cl.extend([color,color])
 f.append(tuple((len(rings)-1)*n+j for j in range(n)));cl.append(c)
 if patch and callable(bone):
  # Cut broad organic coat contours into the SAME surface, independent of
  # ring boundaries. No decals, coplanar overlay, shading trick or smoothing.
  for face in f:
   for k in range(1,len(face)-1):
    tri=[(Vector(v[q]),w[q],patch(Vector(v[q]),0,0)) for q in (face[0],face[k],face[k+1])]
    for positive,col in [(True,2),(False,0)]:
     poly=[]
     for a,b in zip(tri,tri[1:]+tri[:1]):
      inside=(a[2]>=0)==positive;nextin=(b[2]>=0)==positive
      if inside:poly.append(a)
      if inside!=nextin:
       t=a[2]/(a[2]-b[2]);groups=set(a[1])|set(b[1]);wg={g:a[1].get(g,0)*(1-t)+b[1].get(g,0)*t for g in groups}
       poly.append((a[0].lerp(b[0],t),wg,0))
     if len(poly)>2:mesh([tuple(p[0]) for p in poly],[tuple(range(len(poly)))],col,[p[1] for p in poly])
 else:mesh(v,f,cl,w)
def ell(name,p,s,c,bone,segments=10,rings=6):
 v=[]
 for k in range(rings+1):
  a=math.pi*(.02+.96*k/rings)
  for j in range(segments):
   t=math.tau*j/segments;v.append((p[0]+s[0]*math.cos(a),p[1]+s[1]*math.sin(a)*math.cos(t),p[2]+s[2]*math.sin(a)*math.sin(t)))
 f=[]
 for k in range(rings):
  for j in range(segments):a=k*segments+j;b=k*segments+(j+1)%segments;f.append((a,b,b+segments,a+segments))
 f.extend([tuple(reversed(range(segments))),tuple(rings*segments+j for j in range(segments))]);mesh(v,f,c,{bone:1})
def patch(p,i,j):
 x,y,z=p
 # Concept's rump, broad mid-flank island and shoulder bib: irregular lobes,
 # not full circumference bands. A little asymmetry prevents mirrored spots.
 wobble=.13*math.sin(13*x+8*z+2*y)+.07*math.sin(23*z-6*x)
 middle=1-((x+.10+.06*math.sin(z*10))/.39)**2-((z-1.17)/.40)**2+wobble
 rump=1-((x+.86)/.25)**2-((z-1.20)/.37)**2+wobble
 shoulder=1-((x-.70)/.25)**2-((z-1.33)/.40)**2+.08*math.sin(z*19)
 return max(middle,rump,shoulder)
def torso_weights(p):
 t=max(0,min(1,(p[0]-.43)/.48));return {'Body':1-t,'Neck':t}
bodyrings=[((x,0,z),w,h) for x,z,w,h in [(-.98,1.08,.23,.30),(-.88,1.10,.32,.34),(-.72,1.10,.365,.35),(-.57,1.08,.39,.365),(-.40,1.06,.405,.375),(-.23,1.055,.41,.38),(-.05,1.06,.41,.38),(.13,1.075,.39,.365),(.30,1.10,.35,.34),(.46,1.13,.30,.31),(.60,1.20,.265,.285),(.73,1.29,.225,.26),(.85,1.37,.20,.235),(.94,1.41,.17,.205)]]
tube(bodyrings,16,patch=patch,bone=torso_weights)
def facepatch(p,i,j):return 0 if abs(p.y)<.083 and p.z>1.22 else 2
tube([((.86,0,1.44),.145,.20),((.95,0,1.46),.195,.225),((1.05,0,1.43),.215,.225),((1.15,0,1.35),.198,.193),((1.25,0,1.255),.175,.143),((1.34,0,1.19),.17,.105)],14,2,'Head',facepatch)
tube([((1.29,0,1.19),.172,.101),((1.37,0,1.175),.205,.116),((1.435,0,1.175),.186,.094)],12,4,'Head')
for side in [-1,1]:
 ell('nostril',(1.432,side*.101,1.207),(.010,.031,.018),5,'Head',8,4)
 ell('eye',(1.084,side*.199,1.46),(.026,.014,.027),7,'Head',8,4)
 ell('glint',(1.096,side*.210,1.469),(.005,.003,.006),8,'Head',6,3)
 # Shaped leaf ears, dark inset, no horns.
 b='EarL' if side>0 else 'EarR'
 # Broad rounded leaf silhouette facing forward (+X), only shallow cupping.
 yz=[(.15,1.56),(.24,1.635),(.36,1.67),(.48,1.65),(.52,1.61),(.46,1.55),(.34,1.515),(.23,1.515)]
 v=[(.965,side*y,z) for y,z in yz]+[(.985,side*.345,1.585)]
 v.extend([(.935,side*y,z) for y,z in yz]);v.append((.918,side*.345,1.585))
 f=[];cs=[]
 for j in range(8):
  k=(j+1)%8;f.extend([(j,k,8),(9+k,9+j,17),(j,9+j,9+k,k)]);cs.extend([3,2,2])
 mesh(v,f,cs,{b:1})
# Restrained udder and four teats.
ell('udder',(-.62,0,.735),(.24,.225,.14),4,'Body')
for x in [-.72,-.53]:
 for y in [-.10,.10]:ell('teat',(x,y,.61),(.027,.027,.054),4,'Body',7,4)
bones={'Body':((0,0,.9),(0,0,1.2),None),'Neck':((.49,0,1.18),(.89,0,1.43),'Body'),'Head':((.89,0,1.43),(1.44,0,1.13),'Neck')}
for side in [-1,1]:bones['EarL' if side>0 else 'EarR']=((.94,side*.16,1.57),(.94,side*.43,1.59),'Head')
legs={}
for front in [True,False]:
 for side in [-1,1]:
  name=('Front' if front else 'Hind')+('L' if side>0 else 'R');x=.51 if front else -.76;y=side*.245
  hip=Vector((x,y,1.09));knee=Vector((x+(.015 if front else -.09),y,.55));ankle=Vector((x+.025,y,.13))
  legs[name]=(hip,knee,ankle)
  bones[name+'Upper']=(hip,knee,'Body');bones[name+'Lower']=(knee,ankle,'Body');bones[name+'Hoof']=(ankle,ankle+Vector((.12,0,0)),'Body')
  # Continuous tapered leg surface; weight blend at joint ring.
  ringdata=[(hip+Vector((0,-side*.045,.13)),.082,.085),(hip.lerp(knee,.28),.112,.12),(knee+Vector((0,0,.08)),.079,.085),(knee,.075,.075),(knee.lerp(ankle,.22),.06,.065),(ankle+Vector((0,0,.055)),.049,.052),(ankle,.055,.06)]
  v=[];w=[];n=8
  for idx,(p,rx,ry) in enumerate(ringdata):
   for j in range(n):
    a=math.tau*j/n;v.append(tuple(p+Vector((rx*math.cos(a),ry*math.sin(a),0))))
    w.append({name+'Upper':1} if idx<3 else ({name+'Upper':.5,name+'Lower':.5} if idx==3 else {name+'Lower':1}))
  f=[]
  for i in range(len(ringdata)-1):
   for j in range(n):f.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
  f.extend([tuple(reversed(range(n))),tuple((len(ringdata)-1)*n+j for j in range(n))]);mesh(v,f,0,w)
  for toe in [-1,1]:
   # Wedge hoof, split along travel direction; flat sole exactly on ground.
   cy=y+toe*.033;v=[(ankle.x+xx,cy+yy,z) for xx,yy,z in [(-.065,-.029,0),(.105,-.029,0),(.105,.029,0),(-.065,.029,0),(-.045,-.025,.135),(.065,-.025,.12),(.065,.025,.12),(-.045,.025,.135)]]
   mesh(v,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],6,{name+'Hoof':1})
bones['TailBase']=((-.95,0,1.34),(-1.045,0,.94),'Body');bones['TailTip']=((-1.045,0,.94),(-1.09,0,.49),'TailBase')
# Tail follows connected tapered chain.
def strand(points,radii,bone,c):
 v=[];n=7
 for p,r in zip(points,radii):
  for j in range(n):v.append((p[0]+r*math.cos(math.tau*j/n),p[1]+r*math.sin(math.tau*j/n),p[2]))
 f=[tuple(reversed(range(n))),tuple((len(points)-1)*n+j for j in range(n))]
 for i in range(len(points)-1):
  for j in range(n):f.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
 mesh(v,f,c,{bone:1})
strand([(-.95,0,1.36),(-1.015,0,1.14),(-1.045,0,.94)],[.032,.025,.02],'TailBase',0)
strand([(-1.045,0,.95),(-1.07,0,.65),(-1.09,0,.49)],[.021,.018,.026],'TailTip',0)
ell('tailtuft',(-1.09,0,.46),(.055,.045,.12),2,'TailTip',7,4)
# V3 bounded rest-shape revision. Preserve coat topology, ears, legs and weights.
# Barrel grows 16% across, 12% deeper, tapering to unchanged neck attachment.
head_pivot=Vector((.89,0,1.43));head_down=Matrix.Rotation(math.radians(7),3,'Y')
pink_set=set(pink_vertices)
for i,(co,w) in enumerate(zip(verts,weights)):
 p=Vector(co)
 if 'Body' in w and 'Neck' in w:
  fullness=max(0,min(1,(.85-p.x)/.50))
  p.y*=1+.16*fullness;p.z=1.10+(p.z-1.10)*(1+.12*fullness)
 if i in pink_set:
  p.x=1.29+(p.x-1.29)*.88;p.y*=.80;p.z=1.18+(p.z-1.18)*.80
 if any(k in w for k in ('Head','EarL','EarR')):p=head_pivot+head_down@(p-head_pivot)
 verts[i]=tuple(p)
for name in ('Head','EarL','EarR'):
 a,b,parent=bones[name];bones[name]=(head_pivot+head_down@(Vector(a)-head_pivot),head_pivot+head_down@(Vector(b)-head_pivot),parent)
me=bpy.data.meshes.new('Cow_CraftedTopology');me.from_pydata(verts,[],faces);me.materials.append(mat);uv=me.uv_layers.new(name='PaletteUV')
for p,c in zip(me.polygons,cols):
 for li in p.loop_indices:uv.data[li].uv=((c+.5)/len(PAL),.5)
bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
ob=bpy.data.objects.new('CowBody',me);bpy.context.collection.objects.link(ob)
arm=bpy.data.armatures.new('CowSkeleton');rig=bpy.data.objects.new('LWF_Cow',arm);bpy.context.collection.objects.link(rig);bpy.context.view_layer.objects.active=rig;rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for name,(a,b,parent) in bones.items():
 eb=arm.edit_bones.new(name);eb.head=a;eb.tail=b
 if parent:eb.parent=arm.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')
for name in bones:
 g=ob.vertex_groups.new(name=name)
 for i,w in enumerate(weights):
  if name in w:g.add([i],w[name],'REPLACE')
ob.parent=rig;mod=ob.modifiers.new('CowSkin','ARMATURE');mod.object=rig
scene=bpy.context.scene;scene.render.fps=24
def reset():
 for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
def keys(frame):
 for p in rig.pose.bones:
  p.rotation_mode='QUATERNION';p.keyframe_insert('location',frame=frame);p.keyframe_insert('rotation_quaternion',frame=frame);p.keyframe_insert('scale',frame=frame)
def orient(name,a,b):
 pb=rig.pose.bones[name];rest=arm.bones[name];q=(Vector(b)-Vector(a)).to_track_quat('Y','Z');pb.matrix=Matrix.Translation(a)@q.to_matrix().to_4x4()
def action(name):
 reset();rig.animation_data_create();act=bpy.data.actions.new(name);rig.animation_data.action=act;return act
action('Idle')
for f in [1,13,25,37,49]:
 reset();t=(f-1)/48*math.tau
 for name,amp in [('Head',.018),('EarL',.07),('EarR',-.05),('TailBase',.05)]:rig.pose.bones[name].rotation_mode='XYZ';rig.pose.bones[name].rotation_euler.y=amp*math.sin(t)
 # Convert the evaluated Euler rotation before quaternion keying.
 for p in rig.pose.bones:
  if p.rotation_mode=='XYZ':q=p.rotation_euler.to_quaternion();p.rotation_mode='QUATERNION';p.rotation_quaternion=q
 keys(f)
idle=rig.animation_data.action
action('WalkPreview')
contact=[]
for f in range(1,26):
 reset();t=(f-1)/24
 for k,(name,(hip,knee,ankle)) in enumerate(legs.items()):
  phase=(t+[0,.5,.75,.25][k])%1
  dx=.16-.32*phase/.65 if phase<.65 else -.16+.32*(phase-.65)/.35
  lift=0 if phase<.65 else .10*math.sin(math.pi*(phase-.65)/.35)
  foot=ankle+Vector((dx,0,lift));mid=knee+Vector((dx*.45,0,lift*.48))
  orient(name+'Upper',hip,mid);orient(name+'Lower',mid,foot);orient(name+'Hoof',foot,foot+Vector((.12,0,0)))
  contact.append({'frame':f,'leg':name,'sole_height':round(lift,6),'stance':phase<.65})
 keys(f)
walk=rig.animation_data.action
action('Alert')
for f,ang in [(1,0),(13,-.18),(25,-.18),(37,0)]:
 reset()
 # world Y lifts the head facing +X.
 for name,a in [('Neck',ang),('Head',ang*.35)]:
  p=rig.pose.bones[name];p.rotation_quaternion=arm.bones[name].matrix_local.to_quaternion().inverted() @ __import__('mathutils').Quaternion((0,1,0),a) @ arm.bones[name].matrix_local.to_quaternion()
 keys(f)
alert=rig.animation_data.action
# Named NLA tracks make all three actions explicit to GLTF exporter.
rig.animation_data.action=None
for act in [idle,walk,alert]:
 tr=rig.animation_data.nla_tracks.new();tr.name=act.name;tr.strips.new(act.name,1,act);tr.mute=True
reset();scene.frame_set(1)
rig['forward']='Blender +X / Godot +X';rig['approval']='User approval relayed by coordinator 2026-10-06; cow-asset-concepts-v1/cow-concept-sheet-v1.png'
rig['walk_note']='In-place pose/movement preview, not final root-motion or gameplay gait. Controller speed and foot locking require integration testing.'
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);ob.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(R/'source/lwf_cow_v3.blend'))
bpy.ops.export_scene.gltf(filepath=str(R/'runtime/lwf_cow_v3.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',export_force_sampling=True,export_yup=True)
me.calc_loop_triangles()
report={'triangles':len(me.loop_triangles),'vertices':len(me.vertices),'mesh_count':1,'materials':1,'bones':len(bones),'forward':'Godot +X','units':'metres','bounds_blender':[list(map(min,zip(*verts))),list(map(max,zip(*verts)))],'animations':['Idle','WalkPreview','Alert'],'walk_contact_targets':contact,'concept_sha256':hashlib.sha256((R.parent/'cow-asset-concepts-v1/cow-concept-sheet-v1.png').read_bytes()).hexdigest()}
(R/'build-report.json').write_text(json.dumps(report,indent=2))
print('COW_BUILD_COMPLETE',len(me.loop_triangles))
