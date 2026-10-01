import bpy,bmesh,math,json,struct,zlib,hashlib,random
from pathlib import Path
from mathutils import Vector,Matrix
R=Path(__file__).resolve().parent
for d in ['source','runtime','textures','review','godot-check']:(R/d).mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
colors=['858F94','717D83','9AA4A7','333C41','252E32','59656A','E7DFC6','CDBE9E','AF895C','D8B680','A85143','EFE7D2','698C97','9B7450','C5CDCC','514C43']
def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
png=R/'textures/lwf_bin_palette_v1.png'
raw=b''.join(b'\0'+b''.join(bytes.fromhex(c)*8 for c in colors) for _ in range(8))
png.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',128,8,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))
im=bpy.data.images.load(str(png));im.pack()
mat=bpy.data.materials.new('LWF_Bin_MattePalette');mat.use_nodes=True
bs=mat.node_tree.nodes['Principled BSDF'];bs.inputs['Roughness'].default_value=.88
tx=mat.node_tree.nodes.new('ShaderNodeTexImage');tx.image=im;tx.interpolation='Closest';mat.node_tree.links.new(tx.outputs['Color'],bs.inputs['Base Color'])
class Mesh:
 def __init__(self):self.v=[];self.f=[];self.c=[]
 def face(self,ids,c):self.f.append(ids);self.c.append(c)
 def profile(self,profile,c,n=24,center=(0,0,0),rot=None):
  # Revolved closed section, including annular lips or liner walls/bottom.
  base=len(self.v);rot=rot or Matrix.Identity(3)
  for radius,z in profile:
   for i in range(n):self.v.append(tuple(Vector(center)+rot@Vector((radius*math.cos(math.tau*i/n),radius*math.sin(math.tau*i/n),z))))
  for j in range(len(profile)):
   k=(j+1)%len(profile)
   for i in range(n):self.face([base+j*n+i,base+j*n+(i+1)%n,base+k*n+(i+1)%n,base+k*n+i],c)
 def box(self,center,size,c,angle=0):
  base=len(self.v);q=Matrix.Rotation(angle,3,'Z')
  for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]:self.v.append(tuple(Vector(center)+q@Vector((x*size[0]/2,y*size[1]/2,z*size[2]/2))))
  for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]:self.face([base+i for i in f],c)
 def paper(self,p,r,c,seed):
  rng=random.Random(seed);base=len(self.v)
  self.v.append((p[0],p[1],p[2]+r*.55))
  for i in range(7):
   a=math.tau*i/7;rr=r*rng.uniform(.65,1);self.v.append((p[0]+rr*math.cos(a),p[1]+rr*math.sin(a),p[2]+rng.uniform(-.015,.02)))
  self.v.append((p[0],p[1],p[2]-r*.30))
  for i in range(7):
   self.face([base,base+1+i,base+1+(i+1)%7],c if i%3 else 7)
   self.face([base+8,base+1+(i+1)%7,base+1+i],c)
 def obj(self,name,bevel=False):
  me=bpy.data.meshes.new(name);me.from_pydata(self.v,[],self.f);me.materials.append(mat)
  uv=me.uv_layers.new(name='PaletteUV')
  for poly,c in zip(me.polygons,self.c):
   for li in poly.loop_indices:uv.data[li].uv=((c+.5)/16,.5)
  bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
  ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob)
  if bevel:
   bpy.context.view_layer.objects.active=ob;ob.select_set(True)
   mod=ob.modifiers.new('Restrained edge bevel','BEVEL');mod.width=.002;mod.segments=1;mod.affect='EDGES';mod.angle_limit=math.radians(35)
   bpy.ops.object.modifier_apply(modifier=mod.name);ob.select_set(False)
  return ob
g=Mesh()
# Broad annular rim, bevelled via explicit section; max diameter .65, height .95.
g.profile([(.289,.918),(.320,.918),(.325,.923),(.325,.945),(.320,.95),(.289,.95),(.284,.945),(.284,.923)],0)
g.profile([(.285,.035),(.316,.035),(.32,.040),(.32,.067),(.316,.072),(.285,.072)],1)
for i in range(16):
 a=math.tau*i/16
 g.box((.300*math.cos(a),.300*math.sin(a),.489),(.025,.037,.866),0 if i%4 else 1,a)
# Dark closed-bottom thin liner, open at top, with real visible inner wall.
g.profile([(.274,.075),(.274,.858),(.270,.865),(.258,.865),(.254,.858),(.254,.096),(.002,.096),(.002,.075)],3)
g.profile([(.272,.848),(.278,.848),(.278,.864),(.272,.868),(.256,.868),(.252,.864),(.252,.853)],5)
for z in [.18,.73]:g.profile([(.275,z),(.288,z),(.288,z+.018),(.275,z+.018)],1)
for a in [math.pi/4+i*math.pi/2 for i in range(4)]:g.box((.29*math.cos(a),.29*math.sin(a),.020),(.043,.043,.040),4,a)
shell=g.obj('LWF_Bin_Slatted_Shell')
def rubbish(name,z,seed):
 g=Mesh();rng=random.Random(seed)
 # Compact opaque mound hides no per-piece buried geometry; only visible top props.
 g.profile([(.002,z-.05),(.238,z-.05),(.244,z),(.17,z+.01),(.002,z+.025)],15,n=16)
 for i in range(18):
  a=i*2.39996;r=.175*math.sqrt((i+.5)/18)
  g.paper((r*math.cos(a),r*math.sin(a),z+rng.uniform(.018,.05)),rng.uniform(.062,.077),6 if i%2 else 11,seed+i)
 for j,(x,y) in enumerate([(-.09,.09),(.12,.055),(.015,-.13)]):
  q=Matrix.Rotation([.65,-.72,.4][j],3,'X')@Matrix.Rotation([-.25,.30,.12][j],3,'Y')
  g.profile([(.027,0),(.039,.091),(.035,.095),(.031,.09),(.020,.009)],10 if j!=1 else 12,n=10,center=(x,y,z+.045),rot=q)
  g.profile([(.039,.090),(.040,.094),(.035,.098),(.031,.093)],11,n=10,center=(x,y,z+.045),rot=q)
 # Open folded food tray, broad readable planes.
 b=len(g.v);x,y=.09,-.035
 g.v.extend([(x+u,y+v,z+w) for u,v,w in [(-.05,-.035,.05),(.05,-.035,.05),(.05,.035,.05),(-.05,.035,.05),(-.065,-.05,.105),(.065,-.05,.105),(.065,.05,.105),(-.065,.05,.105)]])
 for f in [(0,1,2,3),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]:g.face([b+i for i in f],9)
 ob=g.obj(name);# Tray thin planes render both sides through a shared opaque material.
 return ob
part=rubbish('LWF_Bin_Rubbish_PartFull',.60,42)
full=rubbish('LWF_Bin_Rubbish_Full',.88,82)
# Material double-sided for paper trays; no alpha blending.
mat.use_backface_culling=False
assets=[shell,part,full]
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
report={'units':'metres','origin':'ground centre','blender_axes':'Z up','godot_axes':'Y up, -Z forward (rotationally symmetric)','assets':{},'collision':{'suggested_cylinder_radius':.325,'height':.95,'centre_godot':[0,.475,0],'baked':False},'fill_rule':'shell always present; choose at most ONE fill overlay at identity; empty=no overlay'}
for ob in assets:
 bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
 p=R/'runtime'/(ob.name.lower()+'_v1.glb')
 bpy.ops.export_scene.gltf(filepath=str(p),export_format='GLB',use_selection=True,export_yup=True,export_animations=False)
 data=p.read_bytes();j=json.loads(data[20:20+struct.unpack_from('<I',data,12)[0]])
 report['assets'][ob.name]={'file':p.name,'sha256':sha(p),'triangles':sum(j['accessors'][pr['indices']]['count']//3 for m in j['meshes'] for pr in m['primitives']),'meshes':len(j['meshes']),'materials':len(j.get('materials',[])),'bounds_blender':[list(map(float,minmax)) for minmax in zip(*[(min(v.co[k] for v in ob.data.vertices),max(v.co[k] for v in ob.data.vertices)) for k in range(3)])]}
part.hide_render=True;full.hide_render=True;part.hide_set(True);full.hide_set(True)
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24;s.render.resolution_percentage=100
s.world=bpy.data.worlds.new('Review world');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.72,.76,.8,1);s.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.7
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.005));floor=bpy.context.object;floor.name='REVIEW_Ground'
fm=bpy.data.materials.new('REVIEW_Ground');fm.diffuse_color=(.32,.38,.25,1);floor.data.materials.append(fm)
bpy.ops.object.light_add(type='AREA',location=(-3,4,6));light=bpy.context.object;light.data.energy=650;light.data.shape='DISK';light.data.size=4;light.rotation_euler=(Vector((0,0,.5))-light.location).to_track_quat('-Z','Y').to_euler()
adultpath=R.parent/'direction-a-blender-prototypes-v1/attendee-neutral-v5/direction-a-attendee.blend'
with bpy.data.libraries.load(str(adultpath),link=False) as (src,dst):dst.objects=['DirectionA_Attendee']
adult=dst.objects[0];s.collection.objects.link(adult);adult.name='REVIEW_ApprovedAdult';adult.location=(.95,0,0)
report['adult']={'source':str(adultpath),'height_m':float(adult.dimensions.z),'scale':list(adult.scale)}
bpy.ops.object.camera_add();cam=bpy.context.object;cam.name='REVIEW_Camera';cam.data.type='ORTHO';s.camera=cam
def render(name,angle=35,elev=28,width=2.8,target=(.35,0,.82),adultshow=True):
 adult.hide_render=not adultshow;t=Vector(target);a=math.radians(angle);cam.location=t+Vector((5*math.sin(a),5*math.cos(a),5*math.tan(math.radians(elev))));cam.rotation_euler=(t-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=width;s.render.resolution_x=1200;s.render.resolution_y=1000;s.render.filepath=str(R/'review'/(name+'.png'));bpy.ops.render.render(write_still=True)
render('hero-approved-adult')
render('empty-threequarter',width=1.45,target=(0,0,.5),adultshow=False)
for angle,label in [(0,'front'),(90,'left'),(180,'back'),(270,'right')]:render('orthographic-'+label,angle,0,1.4,(0,0,.48),False)
part.hide_render=False;render('part-full',width=1.45,target=(0,0,.5),adultshow=False);part.hide_render=True
full.hide_render=False;render('full',width=1.45,target=(0,0,.5),adultshow=False);full.hide_render=True
adult.hide_render=False
bpy.ops.wm.save_as_mainfile(filepath=str(R/'source/lwf_bin_slatted_v1.blend'))
report['source_sha256']=sha(R/'source/lwf_bin_slatted_v1.blend')
(R/'manifest.json').write_text(json.dumps(report,indent=2))
print('BIN_BUILD_COMPLETE')
