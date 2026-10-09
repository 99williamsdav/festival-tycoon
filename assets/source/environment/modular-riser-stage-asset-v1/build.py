import bpy,bmesh,math,json,hashlib
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
PAL=['3E4243','474B4B','505452','777F80','939B99','B3BAB5','555E60','242B2D','957D57','AD946C','C0A77B','171D20','2D3539','626B70','CBCDC4','686B60']
im=bpy.data.images.new('RiserPalette',width=128,height=8);im.colorspace_settings.name='sRGB';im.pixels=[c for y in range(8) for h in PAL for x in range(8) for c in [*[int(h[i:i+2],16)/255 for i in (0,2,4)],1]]
im.filepath_raw=str(R/'textures/lwf_modular_riser_palette_v1.png');im.file_format='PNG';im.save();im.pack()
mat=bpy.data.materials.new('Riser_MattePalette');mat.use_nodes=True;bs=mat.node_tree.nodes['Principled BSDF'];bs.inputs['Roughness'].default_value=.8
tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;tex.interpolation='Closest';mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
class Geo:
 def __init__(self,name):self.name=name;self.v=[];self.f=[];self.c=[]
 def add(self,v,f,c):
  off=len(self.v);self.v+=list(v);self.f += [tuple(off+i for i in q) for q in f];self.c+=c if isinstance(c,list) else [c]*len(f)
 def box(self,p,s,c,q=None):
  v=[Vector((x*s[0]/2,y*s[1]/2,z*s[2]/2)) for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
  self.add([tuple(Vector(p)+(q@a if q else a)) for a in v],[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],c)
 def beam(self,a,b,width,c):
  a,b=Vector(a),Vector(b);self.box((a+b)/2,(width,width,(b-a).length),c,(b-a).to_track_quat('Z','Y'))
 def cyl(self,a,b,r,c,n=8):
  a,b=Vector(a),Vector(b);q=(b-a).to_track_quat('Z','Y');v=[tuple(p+q@Vector((r*math.cos(j*math.tau/n),r*math.sin(j*math.tau/n),0))) for p in [a,b] for j in range(n)]
  self.add(v,[tuple(reversed(range(n))),tuple(n+j for j in range(n))]+[(j,(j+1)%n,n+(j+1)%n,n+j) for j in range(n)],c)
 def obj(self):
  me=bpy.data.meshes.new(self.name+'_Mesh');me.from_pydata(self.v,[],self.f);me.materials.append(mat);uv=me.uv_layers.new(name='PaletteUV')
  for face,c in zip(me.polygons,self.c):
   for li in face.loop_indices:uv.data[li].uv=((c+.5)/len(PAL),.5)
  bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
  ob=bpy.data.objects.new(self.name,me);bpy.context.collection.objects.link(ob);return ob
D=.9
deck=Geo('RiserDeck');frame=Geo('RiserSteelFrame');stairs=Geo('RiserAccess');rails=Geo('RiserCornerRails')
# Six equal 2 x 2 m modules. Closed continuous substrate below visual inset seams.
deck.box((0,0,.845),(6,4,.07),0)
for ix in range(3):
 for iy in range(2):
  x=-2+ix*2;y=-1+iy*2
  deck.box((x,y,.8825),(1.978,1.978,.035),[0,1,2][(ix+iy)%3])
  for dx in [-.93,.93]:
   for dy in [-.93,.93]:deck.cyl((x+dx,y+dy,.9),(x+dx,y+dy,.903),.018,4,8)
# Narrow steel seam edge strips, slightly below deck top; no trip-height grid.
for x in [-3,-1,1,3]:deck.box((x,0,.89),(.022,4.02,.018),3)
for y in [-2,0,2]:deck.box((0,y,.89),(6.02,.022,.018),3)
# Wood edges seen in concept; steel shoes and plates carry the assembled-kit reading.
for y in [-1.96,1.96]:deck.box((0,y,.76),(6,.09,.12),9)
for x in [-2.96,2.96]:deck.box((x,0,.76),(.09,4,.12),8)
for y in [-2,0,2]:frame.box((0,y,.76),(6.08,.08,.10),3)
for x in [-3,-1,1,3]:frame.box((x,0,.76),(.08,4.08,.10),4)
for x in [-3,-1,1,3]:
 for y in [-2,0,2]:
  frame.box((x,y,.025),(.28,.28,.05),6);frame.box((x,y,.385),(.075,.075,.72),4);frame.box((x,y,.735),(.105,.105,.12),3)
  for xx in [-.09,.09]:frame.cyl((x+xx,y,.05),(x+xx,y,.063),.015,5)
  # Simple corner plate and visible paired bolts on outer edges.
  if y!=0:
   frame.box((x,y*1.025,.765),(.19,.018,.21),3)
   for zz in [.715,.81]:frame.cyl((x,y*1.03,zz),(x,y*1.041,zz),.019,14)
# Alternating braced bays on front/rear and both short ends, all below deck.
for y in [-1.96,1.96]:
 for i in range(3):
  x=-3+i*2;frame.beam((x,y,.12),(x+2,y,.62),.045,3);frame.beam((x,y,.62),(x+2,y,.12),.045,6)
for x in [-2.96,2.96]:
 for y in [-2,0]:frame.beam((x,y,.12),(x,y+2,.62),.045,4)
# Five .18m rises, .36m treads; top step meets deck without gap.
for i in range(5):
 x=-4.8+(i+.5)*.36;h=(i+1)*.18
 stairs.box((x,-.8,h-.053),(.36,1.04,.07),6)
 stairs.box((x,-.8,h-.008),(.36,.97,.016),1)
 stairs.box((x-.167,-.8,h-.008),(.024,1.04,.02),5)
for y in [-1.33,-.27]:
 stairs.beam((-4.8,y,.055),(-3.0,y,.79),.075,4)
 stairs.box((-4.77,y,.025),(.23,.18,.05),6)
# One slim outer stair handrail, inner side stays open for instrument cases.
for x,z in [(-4.68,.18),(-3.16,.9)]:stairs.cyl((x,-1.34,z),(x,-1.34,z+.68),.025,4)
stairs.cyl((-4.68,-1.34,.86),(-3.16,-1.34,1.58),.025,5)
for xa,xb in [(-2.96,-1.96),(1.96,2.96)]:
 for x in [xa,xb]:rails.cyl((x,1.97,.9),(x,1.97,1.55),.025,4);rails.box((x,1.97,.93),(.09,.09,.08),3)
 for h in [1.2,1.55]:rails.cyl((xa,1.97,h),(xb,1.97,h),.022,5)
structure=[g.obj() for g in [deck,frame,stairs,rails]]
sp=Geo('RiserSpeaker');sp.box((0,0,.4),(.46,.38,.8),11)
sp.box((0,-.198,.4),(.398,.025,.728),12)
for z,r in [(.245,.132),(.56,.105)]:
 sp.cyl((0,-.215,z),(0,-.225,z),r,7,12);sp.cyl((0,-.227,z),(0,-.23,z),r*.72,13,12);sp.cyl((0,-.232,z),(0,-.238,z),r*.25,11,10)
sp.box((0,-.217,.083),(.065,.008,.026),14)
for x in [-.12,.12]:sp.box((x,0,.015),(.07,.20,.03),7)
speaker=sp.obj()
def export(name,obs):
 bpy.ops.object.select_all(action='DESELECT')
 for ob in obs:ob.select_set(True)
 bpy.context.view_layer.objects.active=obs[0];bpy.ops.export_scene.gltf(filepath=str(R/'runtime'/(name+'.glb')),export_format='GLB',use_selection=True,export_yup=True,export_animations=False)
 out={}
 for ob in obs:
  ob.data.calc_loop_triangles();p=[v.co for v in ob.data.vertices]
  out[ob.name]={'triangles':len(ob.data.loop_triangles),'vertices':len(p),'bounds_blender':[[min(v[i] for v in p) for i in range(3)],[max(v[i] for v in p) for i in range(3)]]}
 return out
stats={'structure':export('lwf_modular_riser_stage_v1',structure),'speaker':export('lwf_modular_riser_speaker_v1',[speaker])}
speaker.location=(-2.42,-1.37,.9);other=speaker.copy();other.data=speaker.data;bpy.context.collection.objects.link(other);other.name='RiserSpeakerRight';other.location=(2.42,-1.37,.9)
bpy.ops.wm.save_as_mainfile(filepath=str(R/'source/lwf_modular_riser_stage_v1.blend'))
anchors={'origin':[0,0,0],'deck_height':.9,'audience_forward':[0,0,1],'deck_bounds_xz':[[-3,-2],[3,2]],'stair_approach':[-5.15,0,.8],'stair_toe':[-4.8,0,.8],'stair_top':[-3,.9,.8],'deck_entry':[-2.65,.9,.8],'stair_tread_centres':[[-4.8+(i+.5)*.36,(i+1)*.18,.8] for i in range(5)],'review_band_marks':{'front_left':[-1.25,.9,.5],'front_right':[1.25,.9,.5],'drummer':[0,.9,-.85]},'speaker_roots':[[-2.42,.9,1.37],[2.42,.9,1.37]]}
(R/'anchors.json').write_text(json.dumps(anchors,indent=2));(R/'manifest.json').write_text(json.dumps({'assets':stats,'palette':[128,8],'materials':1,'references_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (R/'references').iterdir() if p.is_file()},'runtime_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (R/'runtime').glob('*.glb')}},indent=2));print('RISER_BUILD_PASSED')



