"""Pond polish: original footprint retained, static basin + independent waterline ducks."""
import bpy,bmesh,math,random,json,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
R=Path(__file__).resolve().parent;W=R.parent
for d in ['source','runtime','textures','review','godot-check']:(R/d).mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
REF=R/'references/farm_beauty_report.json'
old=json.loads(REF.read_text())['lwf_farm_pond_v1'];poly=old['footprint_polygon_godot_xz']
outer=[Vector((p[0]-24,-(p[1]-24.5),.018)) for p in poly]
PAL=['99917B','A19880','899064','92996D','687C4C','B3AC94','918C7A','C7BEA5','728642','566D36','A39861','786442','3B6840','295338','DED7BD','B5AE97','67503B','94754C','B09261','C49842','AC8234','202923','34464C','F3E8CE']
def srgb(v):return v/12.92 if v<.04045 else ((v+.055)/1.055)**2.4
im=bpy.data.images.new('PondPolishPalette',width=len(PAL)*8,height=8)
im.colorspace_settings.name='sRGB'
im.pixels=[channel for y in range(8) for c in PAL for x in range(8) for channel in [*[int(c[i:i+2],16)/255 for i in (0,2,4)],1]]
im.filepath_raw=str(R/'textures/lwf_pond_polish_palette_v1.png');im.file_format='PNG';im.save();im.pack()
mat=bpy.data.materials.new('PondPolish_MattePalette');mat.use_nodes=True
bs=mat.node_tree.nodes['Principled BSDF'];bs.inputs['Roughness'].default_value=.92
tx=mat.node_tree.nodes.new('ShaderNodeTexImage');tx.image=im;tx.interpolation='Closest';mat.node_tree.links.new(tx.outputs['Color'],bs.inputs['Base Color'])
class Geo:
 def __init__(self,name):self.name=name;self.v=[];self.f=[];self.c=[]
 def add(self,v,f,c):
  off=len(self.v);self.v+=list(v);self.f += [tuple(off+i for i in face) for face in f];self.c+=c if isinstance(c,list) else [c]*len(f)
 def blob(self,p,s,c,sub=1,angle=0):
  bm=bmesh.new();res=bmesh.ops.create_icosphere(bm,subdivisions=sub,radius=1);bm.verts.ensure_lookup_table();bm.verts.index_update()
  q=Matrix.Rotation(angle,3,'Z');v=[tuple(Vector(p)+q@Vector((a.co.x*s[0],a.co.y*s[1],a.co.z*s[2]))) for a in bm.verts];f=[tuple(a.index for a in face.verts) for face in bm.faces];self.add(v,f,c);bm.free()
 def rings(self,rings,c,n=10):
  v=[(x,cy+ry*math.cos(math.tau*j/n),z+rz*math.sin(math.tau*j/n)) for x,cy,z,ry,rz in rings for j in range(n)]
  f=[tuple(reversed(range(n))),tuple((len(rings)-1)*n+j for j in range(n))]
  for k in range(len(rings)-1):
   for j in range(n):f.append((k*n+j,k*n+(j+1)%n,(k+1)*n+(j+1)%n,(k+1)*n+j))
  self.add(v,f,c)
 def beam(self,a,b,r,c,n=6):
  a,b=Vector(a),Vector(b);q=(b-a).to_track_quat('Z','Y');v=[tuple(p+q@Vector((r*math.cos(math.tau*j/n),r*math.sin(math.tau*j/n),0))) for p in [a,b] for j in range(n)]
  self.add(v,[tuple(reversed(range(n))),tuple(n+j for j in range(n))]+[(j,(j+1)%n,n+(j+1)%n,n+j) for j in range(n)],c)
 def obj(self):
  me=bpy.data.meshes.new(self.name+'_Mesh');me.from_pydata(self.v,[],self.f);me.materials.append(mat);uv=me.uv_layers.new(name='PaletteUV')
  for face,c in zip(me.polygons,self.c):
   for li in face.loop_indices:uv.data[li].uv=((c+.5)/len(PAL),.5)
  bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
  ob=bpy.data.objects.new(self.name,me);bpy.context.collection.objects.link(ob);return ob
# Subdivide original straight boundary without moving it. The editable inner
# shoreline is scalloped inward only; no new navigation envelope is implied.
edge=[]
for a,b in zip(outer,outer[1:]+outer[:1]):
 for t in [0,1/3,2/3]:edge.append(a.lerp(b,t))
N=len(edge);shore=[];mid=[]
for i,p in enumerate(edge):
 scale=.805+.026*math.sin(i*.71)+.013*math.cos(i*1.53)
 shore.append(Vector((p.x*scale,p.y*scale,.075)))
 mid.append(Vector((p.x*.925,p.y*.925,.065+.025*math.sin(i*.57)**2)))
bank=Geo('PondMargin');bank.v=[tuple(p) for ring in [edge,mid,shore] for p in ring]
for band in range(2):
 for i in range(N):
  j=(i+1)%N;a=band*N+i;b=band*N+j;c=(band+1)*N+j;d=(band+1)*N+i
  bank.f.extend([(a,b,c),(a,c,d)]);bank.c.extend(([3,2] if i%4==0 else [2,3]) if band==0 else ([0,1] if i%3 else [1,0]))
margin=bank.obj()
# Single planar water surface, planar UV and gentle static vertex tint. No
# reflections, animated shader or transparent depth-sorting cost baked here.
watermat=bpy.data.materials.new('PondWater_RuntimeSurface');watermat.use_nodes=True
wb=watermat.node_tree.nodes['Principled BSDF'];wb.inputs['Roughness'].default_value=.65
vc=watermat.node_tree.nodes.new('ShaderNodeVertexColor');vc.layer_name='WaterTint';watermat.node_tree.links.new(vc.outputs['Color'],wb.inputs['Base Color'])
wv=[(0,0,.075)]+[tuple(p*.48+Vector((0,0,.075*.52))) for p in shore]+[tuple(p) for p in shore];wf=[]
for i in range(N):
 j=(i+1)%N;wf += [(0,1+i,1+j),(1+i,1+N+i,1+N+j),(1+i,1+N+j,1+j)]
me=bpy.data.meshes.new('PondWater_Mesh');me.from_pydata(wv,[],[tuple(reversed(f)) for f in wf]);me.update();me.materials.append(watermat);uv=me.uv_layers.new(name='WaterPlanarUV');col=me.color_attributes.new(name='WaterTint',type='FLOAT_COLOR',domain='CORNER');uv=me.uv_layers['WaterPlanarUV'];col=me.color_attributes['WaterTint']
for face in me.polygons:
 for li in face.loop_indices:
  p=Vector(wv[me.loops[li].vertex_index]);uv.data[li].uv=(p.x/12+.5,p.y/10+.5)
  edgefactor=min(1,math.sqrt((p.x/4.1)**2+(p.y/3.1)**2));s=.018*math.sin(p.x*.8+p.y*.65)
  rgb=[.27+.055*edgefactor+s,.43+.045*edgefactor+s,.41+.03*edgefactor+s];col.data[li].color=(*[srgb(v) for v in rgb],1)
water=bpy.data.objects.new('PondWater',me);bpy.context.collection.objects.link(water)
rng=random.Random(2707);details=Geo('BankDetails')
for angle in [.35,2.2,3.25,4.55]:
 i=round(angle/math.tau*N)%N;c=shore[i].lerp(edge[i],.52)
 for k in range(2 if angle!=3.25 else 3):
  p=c+Vector((rng.uniform(-.21,.21),rng.uniform(-.15,.15),.035));details.blob(p,(rng.uniform(.12,.24),rng.uniform(.11,.18),rng.uniform(.08,.14)),5+k%3,sub=1,angle=rng.random())
# Reeds kept to existing negative-local-X planting side, low and sparse.
for i in [22,29,35,40]:
 c=shore[i].lerp(edge[i],.30)
 for k in range(6):
  base=c+Vector((rng.uniform(-.12,.12),rng.uniform(-.12,.12),0));h=rng.uniform(.25,.44);a=rng.random()*math.tau
  tip=base+Vector((.13*math.cos(a),.13*math.sin(a),h));side=Vector((-.035*math.sin(a),.035*math.cos(a),0));middle=base.lerp(tip,.5)+Vector((.025*math.cos(a),.025*math.sin(a),.04))
  details.add([tuple(base),tuple(middle+side),tuple(tip),tuple(middle-side)],[(0,1,2),(0,2,3),(2,1,0),(3,2,0)],8 if k%2 else 4)
  if k<2:details.beam(base,tip,.008,10,5);details.beam(tip-Vector((0,0,.09)),tip,.020,11,6)
pad_records=[]
for x,y,r in [(2.35,.6,.23),(2.65,.88,.17),(2.15,1.0,.19),(-1.85,-1.65,.21),(-2.1,-1.4,.16),(1.65,-1.6,.20),(1.96,-1.75,.15)]:
 v=[(x,y,.083)]+[(x+r*math.cos(.18+j*(math.tau-.36)/9),y+r*math.sin(.18+j*(math.tau-.36)/9),.083) for j in range(10)]
 details.add(v,[(0,j,j+1) for j in range(1,10)],8);pad_records.append([x,-y,r])
detail=details.obj()
def duck(name,drake):
 g=Geo(name)
 # Designed boat-shaped body and chest, not an unmodified primitive assembly.
 g.rings([(-.285,0,.105,.03,.022),(-.22,0,.10,.10,.060),(-.13,0,.095,.148,.100),(0,0,.09,.155,.12),(.12,0,.09,.12,.118),(.205,0,.12,.067,.092)],14 if drake else 17,12)
 # Continuous tilted neck built from horizontal elliptical cross-sections.
 neck=[]
 for x,z,rx,ry in [(.12,.10,.09,.09),(.17,.18,.064,.065),(.18,.25,.044,.047),(.195,.315,.05,.051)]:
  neck += [(x+rx*math.cos(math.tau*j/10),ry*math.sin(math.tau*j/10),z) for j in range(10)]
 faces=[tuple(reversed(range(10))),tuple(30+j for j in range(10))]
 for k in range(3):
  for j in range(10):faces.append((k*10+j,k*10+(j+1)%10,(k+1)*10+(j+1)%10,(k+1)*10+j))
 g.add(neck,faces,16 if drake else 18)
 if drake:
  vv=[(.184+.046*math.cos(math.tau*j/10),.049*math.sin(math.tau*j/10),z) for z in [.266,.283] for j in range(10)]
  g.add(vv,[(j,(j+1)%10,10+(j+1)%10,10+j) for j in range(10)],23)
 g.blob((.20,0,.328),(.077,.066,.078),12 if drake else 18,sub=2)
 # Broad flattened tapered bill with small dark tip and readable nostrils.
 g.rings([(.247,0,.321,.040,.019),(.293,0,.31,.045,.016),(.34,0,.304,.031,.009)],19,8)
 for side in [-1,1]:
  g.blob((.223,side*.061,.349),(.010,.007,.011),21,sub=1)
  g.blob((.288,side*.018,.325),(.005,.0035,.0025),20,sub=1)
  # Volumetric folded wing embedded in the body's side, no hovering plates.
  g.blob((-.055,side*.103,.137),(.162,.055,.066),15 if drake else 18,sub=2,angle=side*.06)
 g.blob((-.255,0,.14),(.085,.068,.033),22 if drake else 16,sub=1)
 return g.obj()
drake=duck('DuckDrake',True);brown=duck('DuckBrown',False)
static=[margin,water,detail]
def bounds(obs):
 points=[o.matrix_world@v.co for o in obs for v in o.data.vertices];return [[min(p[i] for p in points) for i in range(3)],[max(p[i] for p in points) for i in range(3)]]
def export(name,obs):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0]
 bpy.ops.export_scene.gltf(filepath=str(R/'runtime'/f'{name}.glb'),export_format='GLB',use_selection=True,export_yup=True,export_animations=False)
 tris=0
 for o in obs:o.data.calc_loop_triangles();tris+=len(o.data.loop_triangles)
 return {'triangles':tris,'meshes':len(obs),'materials':len({m.name for o in obs for m in o.data.materials}),'bounds_blender':bounds(obs),'bytes':(R/'runtime'/f'{name}.glb').stat().st_size}
assets={}
for name,obs in [('lwf_pond_polish_static_v1',static),('lwf_duck_drake_v1',[drake]),('lwf_duck_brown_v1',[brown])]:assets[name]=export(name,obs)
for ob,p,yaw in [(drake,(-1,-.6,.075),.6),(brown,(-.2,-1.3,.075),1.1)]:ob.location=p;ob.rotation_euler.z=yaw
bpy.ops.wm.save_as_mainfile(filepath=str(R/'source/lwf_pond_polish_v1.blend'))
files=[R/'references/lwf_farm_pond_v1.glb',REF,R/'references/NavigationFixture.cs',R/'references/pond-polish-concept-sheet.png']
report={'assets':assets,'placement_godot':[24,0,24.5],'outer_polygon_world_xz':poly,'blocked_cells':old['blocked_cells'],'water_polygon_local_xz':[[p.x,-p.y] for p in shore],'water_y':.075,'lily_pads_local_xzr':pad_records,'duck_forward':'Godot +X','duck_origin':'body centre at waterline; place root Y=0.075','duck_recommended_clearance_radius':.42,'initial_duck_roots_godot':[[-1,.075,.6],[-.2,.075,1.3]],'references_sha256':{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
(R/'manifest.json').write_text(json.dumps(report,indent=2));print('POND_BUILD_PASSED')
