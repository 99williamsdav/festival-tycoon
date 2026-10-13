import bpy,bmesh,math,json,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
R=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(R/'references/original-large-barn.blend'))
bpy.context.preferences.filepaths.save_version=0
source=[bpy.data.objects[n] for n in ['LWF_LargeBarn_Shell','LWF_LargeBarn_Roof']]
base=source[0].data.materials[0]
materials={}
class Part:
 def __init__(self,name,kind):self.name=name;self.kind=kind;self.v=[];self.f=[];self.uv=[]
 def add(self,vs,fs,uv):
  off=len(self.v);self.v+=list(vs);self.f += [tuple(off+j for j in f) for f in fs];self.uv+=uv
 def box(self,lo,hi,uv):
  x,y,z=lo;X,Y,Z=hi;vs=[(x,y,z),(X,y,z),(X,Y,z),(x,Y,z),(x,y,Z),(X,y,Z),(X,Y,Z),(x,Y,Z)]
  fs=[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)]
  self.add(vs,fs,[[uv]*4 for _ in fs])
 def obj(self):
  me=bpy.data.meshes.new(self.name);me.from_pydata(self.v,[],self.f);me.update();layer=me.uv_layers.new(name='PaletteUV')
  for p,uv in zip(me.polygons,self.uv):
   for j,v in zip(p.loop_indices,uv):layer.data[j].uv=v
  bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
  if self.kind not in materials:
   mat=base.copy();mat.name='Barn_'+self.kind
   if self.kind in ['Roof','WallFade','DoorFade','TrussFade']:mat.use_backface_culling=True
   materials[self.kind]=mat
  me.materials.append(materials[self.kind])
  ob=bpy.data.objects.new(self.name,me);bpy.context.collection.objects.link(ob);ob['visibility_group']=self.kind;return ob
parts={}
def part(n,k):
 if n not in parts:parts[n]=Part(n,k)
 return parts[n]
def components(ob):
 me=ob.data;adj=[[] for _ in me.vertices]
 for e in me.edges:
  a,b=e.vertices;adj[a].append(b);adj[b].append(a)
 seen=set()
 for i in range(len(me.vertices)):
  if i in seen:continue
  ids=[];stack=[i];seen.add(i)
  while stack:
   j=stack.pop();ids.append(j)
   for k in adj[j]:
    if k not in seen:seen.add(k);stack.append(k)
  index={j:k for k,j in enumerate(ids)};ps=[p for p in me.polygons if p.vertices[0] in index]
  vs=[tuple(ob.matrix_world@me.vertices[j].co) for j in ids];fs=[tuple(index[j] for j in p.vertices) for p in ps];uv=[[tuple(me.uv_layers.active.data[j].uv) for j in p.loop_indices] for p in ps]
  yield vs,fs,uv
cuts=0
for ob in source:
 for idx,(vs,fs,uv) in enumerate(components(ob)):
  lo=[min(v[a] for v in vs) for a in range(3)];hi=[max(v[a] for v in vs) for a in range(3)];c=[(a+b)/2 for a,b in zip(lo,hi)]
  if ob==source[1]:n='Barn_RoofFront' if c[1]<0 else 'Barn_RoofRear';k='Roof'
  elif idx in [6,52]:continue # Replace raised slab/step with flush floor.
  elif 24<=idx<=47:
   n='Barn_DoorsLeft' if idx<=35 else 'Barn_DoorsRight';k='DoorFade'
   if idx<=35:
    # Fold the existing left sliding leaf into two half-width stacked leaves.
    vs=[(-4.1786+(v[0]+4.1786)*.5,v[1],v[2]) for v in vs]
    target=part(n,k);target.add(vs,fs,uv)
    target.add([(v[0]-.10,v[1]+.13,v[2]) for v in vs],fs,uv);continue
  elif 655<=idx<=672:n='Barn_Trusses';k='TrussFade'
  elif idx in [48,49,50,51] or (abs(c[0])>9.9 and abs(c[1])>5.7 and hi[0]-lo[0]<.8 and hi[1]-lo[1]<.8):n='Barn_Frame';k='Frame'
  else:
   if abs(c[0])>9.4:n='Barn_WallEast' if c[0]>0 else 'Barn_WallWest'
   else:n='Barn_WallFront' if c[1]<0 else 'Barn_WallRear'
   k='WallFade'
  target=part(n,k)
  # Cut new 2m exit through every front wall/plinth/stone component.
  if ob==source[0] and n=='Barn_WallFront' and lo[1]<-5.45 and hi[1]>-6.8 and lo[0]<-6.65 and hi[0]>-8.65 and lo[2]<2.8:
   for a,b in [(lo,[min(hi[0],-8.65),hi[1],hi[2]]),([max(lo[0],-6.65),lo[1],lo[2]],hi),([max(lo[0],-8.65),lo[1],max(lo[2],2.8)],[min(hi[0],-6.65),hi[1],hi[2]])]:
    if all(b[j]-a[j]>1e-5 for j in range(3)):target.box(a,b,uv[0][0])
   cuts+=1
  else:target.add(vs,fs,uv)
# Palette swatches sampled from original component UVs rather than new textures.
rows=list(components(source[0]));stone=rows[0][2][0][0];oak=rows[48][2][0][0];flooruv=rows[6][2][0][0]
floor=part('Barn_Floor','Floor');floor.box((-9.5429,-5.5733,-.10),(9.5429,5.5733,0),flooruv)
floor.box((-3.9,-6.6469,-.10),(3.9,-5.5733,0),flooruv);floor.box((-8.65,-6.6469,-.10),(-6.65,-5.5733,0),flooruv)
frame=part('Barn_Frame','Frame')
for x in [-8.75,-6.55]:frame.box((x-.10,-6.18,0),(x+.10,-5.55,2.98),oak)
frame.box((-8.85,-6.18,2.8),(-6.45,-5.55,3.04),oak)
sign=part('Barn_SignBacking','Sign');sign.box((-2.5,-6.61,5.25),(2.5,-6.43,5.95),oak)
sign.box((-8.25,-6.23,3.14),(-7.05,-6.11,3.52),oak)
for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
objects=[p.obj() for p in parts.values()]
def lettering(name,txt,loc,size,color):
 cu=bpy.data.curves.new(name,'FONT');cu.body=txt;cu.align_x='CENTER';cu.align_y='CENTER';cu.size=size;cu.extrude=.002;cu.resolution_u=2
 ob=bpy.data.objects.new(name,cu);bpy.context.collection.objects.link(ob);ob.location=loc;ob.rotation_euler=(math.pi/2,0,0)
 mat=bpy.data.materials.new(name+'_Opaque');mat.diffuse_color=(*color,1);cu.materials.append(mat)
 bpy.context.view_layer.objects.active=ob;ob.select_set(True);bpy.ops.object.convert(target='MESH');ob.select_set(False);objects.append(ob)
lettering('Barn_Name','BARN STAGE',(0,-6.625,5.60),.50,(.89,.83,.61))
lettering('Barn_ExitSign','EXIT',(-7.65,-6.245,3.33),.28,(.70,.90,.64))
# Small warm opaque sconces outside the central opening, no sprawling overhead light rig.
lampmat=bpy.data.materials.new('Barn_Lamps');lampmat.diffuse_color=(.9,.64,.28,1)
for i,(x,y,z) in enumerate([(-8.6,5.35,3.4),(0,5.35,3.4),(8.6,5.35,3.4),(-8.6,-5.35,3.4),(8.6,-5.35,3.4)]):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=8,ring_count=4,radius=.105,location=(x,y,z));o=bpy.context.object;o.name=f'Barn_Lamp_{i}';o.data.materials.append(lampmat);objects.append(o)
for o in objects:
 o.select_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(R/'source/lwf_barn_stage_v1.blend'))
bpy.ops.export_scene.gltf(filepath=str(R/'runtime/lwf_barn_stage_v1.glb'),export_format='GLB',use_selection=True,export_yup=True,export_apply=True)
report={'exit_components_cut':cuts,'source_sha256':hashlib.sha256((R/'references/original-large-barn.blend').read_bytes()).hexdigest(),'nodes':[]}
for o in objects:
 pts=[o.matrix_world@v.co for v in o.data.vertices];lo=[min(v[j] for v in pts) for j in range(3)];hi=[max(v[j] for v in pts) for j in range(3)]
 report['nodes'].append({'name':o.name,'materials':[m.name for m in o.data.materials],'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'bounds_godot':[[lo[0],lo[2],-hi[1]],[hi[0],hi[2],-lo[1]]]})
(R/'manifest.json').write_text(json.dumps(report,indent=2));print('BUILD COMPLETE',cuts,len(objects))
