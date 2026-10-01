import bpy,bmesh,math,json,hashlib,struct,zlib
from pathlib import Path
from mathutils import Vector,Matrix
R=Path(__file__).resolve().parent
for d in ['runtime','source','textures','review','godot-check']: (R/d).mkdir(exist_ok=True)
GAME=Path('C:/Projects/festival-tycoon/assets/source/characters')
report={'units':'metres','forward':'Godot -Z / Blender +Y','status':'EXPORTED_QA_PENDING','assets':{},'variants':{},'source_authority':{}}
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def palette(name,cols):
 def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
 raw=b''.join(b'\0'+b''.join(bytes.fromhex(c)*8 for c in cols) for _ in range(8))
 p=R/'textures'/f'{name}.png';p.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',8*len(cols),8,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))
 im=bpy.data.images.load(str(p));im.pack();m=bpy.data.materials.new(name);m.use_nodes=True;m.use_backface_culling=False
 t=m.node_tree.nodes.new('ShaderNodeTexImage');t.image=im;t.interpolation='Closest';bs=m.node_tree.nodes['Principled BSDF'];bs.inputs['Roughness'].default_value=.92;m.node_tree.links.new(t.outputs['Color'],bs.inputs['Base Color']);return m
def godot(p):return [p[0],p[2],-p[1]]
def empty(n,p=(0,0,0),parent=None):
 o=bpy.data.objects.new(n,None);bpy.context.scene.collection.objects.link(o);o.location=p;o.parent=parent;return o
def make(n,v,faces,mat,uvs=None,slots=None,pivot=(0,0,0),parent=None):
 me=bpy.data.meshes.new(n);me.from_pydata([tuple(Vector(p)-Vector(pivot)) for p in v],[],faces);me.materials.append(mat);uv=me.uv_layers.new(name='PaletteUV')
 for poly in me.polygons:
  for k,li in enumerate(poly.loop_indices):uv.data[li].uv=uvs[poly.index][k] if uvs else ((slots[poly.index]+.5)/len(mat['colours']),.5)
 bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
 o=bpy.data.objects.new(n,me);bpy.context.scene.collection.objects.link(o);o.parent=parent;return o
def extract(src,ix,n,pivot=(0,0,0),parent=None):
 order=sorted(ix);mapping={i:k for k,i in enumerate(order)};polys=[p for p in src.data.polygons if all(i in ix for i in p.vertices)]
 return make(n,[src.data.vertices[i].co[:] for i in order],[tuple(mapping[i] for i in p.vertices) for p in polys],src.data.materials[0],[[tuple(src.data.uv_layers.active.data[i].uv) for i in p.loop_indices] for p in polys],pivot=pivot,parent=parent)
def group(src,n):
 g=src.vertex_groups[n];return sorted(v.index for v in src.data.vertices if any(a.group==g.index for a in v.groups))
def centre(src,ix):return sum((src.data.vertices[i].co for i in ix),Vector())/len(ix)
def export(name,obs,source):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=next(o for o in obs if o.type=='MESH')
 p=R/'runtime'/f'{name}.glb';bpy.ops.export_scene.gltf(filepath=str(p),export_format='GLB',use_selection=True,export_yup=True,export_animations=False)
 raw=p.read_bytes();j=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
 report['assets'][name]={'file':p.name,'sha256':sha(p),'bytes':len(raw),'triangles':sum(j['accessors'][p['indices']]['count']//3 for m in j['meshes'] for p in m['primitives']),'meshes':len(j['meshes']),'surfaces':sum(len(m['primitives']) for m in j['meshes']),'materials':len(j['materials']),'nodes':[n['name'] for n in j['nodes']],'source':source}
 bpy.ops.wm.save_as_mainfile(filepath=str(R/'source'/source));report['assets'][name]['source_sha256']=sha(R/'source'/source)
class Geo:
 def __init__(self,mat):self.v=[];self.f=[];self.c=[];self.mat=mat
 def poly(self,v,c):
  b=len(self.v);self.v.extend(v);self.f.append(tuple(range(b,b+len(v))));self.c.append(c)
 def box(self,p,d,c):
  v=[tuple(p[j]+s[j]*d[j]/2 for j in range(3)) for s in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
  for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]:self.poly([v[i] for i in f],c)
 def beam(self,a,b,w,c):
  a,b=Vector(a),Vector(b);q=(b-a).to_track_quat('Z','Y');start=len(self.v);self.box((0,0,0),(w,w,(b-a).length),c)
  for i in range(start,len(self.v)):self.v[i]=tuple((a+b)/2+q@Vector(self.v[i]))
 def obj(self,n):return make(n,self.v,self.f,self.mat,slots=self.c)
bpy.ops.wm.read_factory_settings(use_empty=True)
cols=['A9B2AF','30373B','242B2D','40474A','596063']
m=palette('lwf_cleanup_equipment_palette_v1',cols);m['colours']=cols
# Handle pivot origin; down Blender-Z / Godot-Y toward jaws at -0.8.
g=Geo(m);g.box((0,0,0),(.028,.035,.09),1);g.box((.019,0,-.009),(.012,.019,.058),2);g.beam((0,0,-.03),(0,0,-.735),.017,0)
g.box((0,0,-.735),(.045,.032,.035),1)
for sign in [-1,1]:
 g.beam((sign*.013,0,-.745),(sign*.045,0,-.772),.013,1);g.beam((sign*.045,0,-.772),(sign*.025,0,-.812),.012,1)
picker=g.obj('PickerMesh');jaw=empty('PickerJawSocket',(0,0,-.8));root=empty('CleanupPicker');picker.parent=root;jaw.parent=root
export('lwf_steward_litter_picker_v1',[root,picker,jaw],'lwf_steward_litter_picker_v1.blend')
bpy.ops.wm.read_factory_settings(use_empty=True)
m=palette('lwf_cleanup_equipment_palette_v1',cols);m['colours']=cols
# Inner rim grasp at origin; bag extends outward left, mouth centre (-.175,0,0).
g=Geo(m);n=8
rings=[]
for z,rx,ry in [(0,.175,.125),(-.04,.163,.118),(-.29,.18,.12),(-.44,.135,.09),(-.48,.07,.045)]:
 rings.append([(-.175+rx*math.cos(math.tau*i/n),ry*math.sin(math.tau*i/n),z) for i in range(n)])
for k in range(len(rings)-1):
 for i in range(n):g.poly([rings[k][i],rings[k][(i+1)%n],rings[k+1][(i+1)%n],rings[k+1][i]],2 if i%3 else 3)
g.poly(list(reversed(rings[-1])),2)
inner=[(-.175+.15*math.cos(math.tau*i/n),.103*math.sin(math.tau*i/n),-.016) for i in range(n)]
for i in range(n):
 g.poly([rings[0][i],rings[0][(i+1)%n],inner[(i+1)%n],inner[i]],4)
 g.poly([inner[i],inner[(i+1)%n],(-.175+.12*math.cos(math.tau*(i+1)/n),.08*math.sin(math.tau*(i+1)/n),-.36),(-.175+.12*math.cos(math.tau*i/n),.08*math.sin(math.tau*i/n),-.36)],2)
g.poly([(-.175+.12*math.cos(math.tau*i/n),.08*math.sin(math.tau*i/n),-.36) for i in range(n)],2)
bag=g.obj('BagMesh');opening=empty('BagMouthSocket',(-.175,0,0));root=empty('CleanupBag');bag.parent=root;opening.parent=root
export('lwf_steward_bin_bag_v1',[root,bag,opening],'lwf_steward_bin_bag_v1.blend')
# Cleanup-only hierarchy from actual current body/garment, not concept historical sources.
for sex in ['male','female']:
 src=GAME/'attendee-v6-draft'/f'{sex}-attendee-v6.blend';vestsrc=GAME/'role-assets-v2'/f'lwf_steward_{sex}_overlay_v2.blend'
 bpy.ops.wm.open_mainfile(filepath=str(src));s=bpy.context.scene;body=next(o for o in s.objects if o.type=='MESH' and len(o.vertex_groups)>10)
 original={g.name:group(body,g.name) for g in body.vertex_groups};hip=Vector((0,0,.9 if sex=='male' else .865))
 root=empty('CleanupBody');upper=empty('UpperPivot',hip,root);parts=[root,upper]
 lower=set(original['Trousers']+original['Shoes']);parts.append(extract(body,lower,'LowerBody',parent=root))
 excluded=set(original['Arms']+original['SleeveLining'])|lower
 parts.append(extract(body,set(range(len(body.data.vertices)))-excluded,'UpperBody',hip,upper))
 # Exact current fitted overlay follows the SAME hip transform as torso.
 with bpy.data.libraries.load(str(vestsrc),link=False) as (a,b):b.objects=[n for n in a.objects if 'Overlay' in n]
 vest=next(o for o in b.objects if o.type=='MESH');s.collection.objects.link(vest)
 for v in vest.data.vertices:v.co-=hip
 vest.name='FittedVest';vest.parent=upper;vest.location=(0,0,0);vest.rotation_euler=(0,0,0);vest.scale=(1,1,1);parts.append(vest)
 data={'hip_godot':godot(hip),'height_m':1.78 if sex=='male' else 1.70,'joint_nodes':{},'body_palette_rule':'Only LowerBody/UpperBody/arm/hand/joint meshes use96x8 body palette; FittedVest uses128x8 trim and MUST NOT receive role body palette replacement.'}
 armids=original['Arms'];liningids=original['SleeveLining']
 for index,side in enumerate(['Left','Right']):
  ix=armids[index*84:(index+1)*84];lin=liningids[index*16:(index+1)*16]
  shoulder=centre(body,lin[:8]);elbow=centre(body,ix[16:24]);wrist=centre(body,ix[32:40]);handtop=centre(body,ix[40:48])
  node_upper=empty(side+'UpperPivot',shoulder,root);node_lower=empty(side+'ForePivot',elbow,root);node_hand=empty(side+'HandPivot',handtop,root)
  parts += [node_upper,node_lower,node_hand]
  parts.append(extract(body,set(ix[:24]+lin),side+'UpperArm',shoulder,node_upper))
  forearm=extract(body,set(ix[16:40]),side+'Forearm',elbow,node_lower);parts.append(forearm)
  parts.append(extract(body,set(ix[40:]),side+'Hand',handtop,node_hand))
  # Small elbow joint masks independent rigid ring orientations when bending.
  bpy.ops.mesh.primitive_uv_sphere_add(segments=8,ring_count=4,radius=.037 if sex=='male' else .034)
  joint=bpy.context.object;joint.name=side+'ElbowJoint';joint.parent=node_lower;joint.location=(0,0,0);joint.data.materials.append(body.data.materials[0])
  while joint.data.uv_layers:joint.data.uv_layers.remove(joint.data.uv_layers[0])
  uv=joint.data.uv_layers.new(name='PaletteUV')
  for l in uv.data:l.uv=(4/96,.5)
  bpy.ops.object.select_all(action='DESELECT');joint.select_set(True);forearm.select_set(True);bpy.context.view_layer.objects.active=forearm;bpy.ops.object.join()
  data['joint_nodes'][side]={'shoulder_godot':godot(shoulder),'elbow_godot':godot(elbow),'wrist_godot':godot(wrist),'hand_pivot_godot':godot(handtop),'upper_length':(elbow-shoulder).length,'fore_length':(handtop-elbow).length,'hand_grip_offset_godot':[0,-.044,0]}
 bpy.data.objects.remove(body,do_unlink=True)
 report['variants'][sex]=data;report['source_authority'][sex]={'body':str(src),'body_sha256':sha(src),'vest':str(vestsrc),'vest_sha256':sha(vestsrc)}
 export(f'lwf_steward_cleanup_{sex}_v1',parts,f'lwf_steward_cleanup_{sex}_v1.blend')
(R/'manifest.json').write_text(json.dumps(report,indent=2));print('CLEANUP_EXPORT',json.dumps(report))
