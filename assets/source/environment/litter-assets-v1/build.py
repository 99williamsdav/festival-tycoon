import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
for d in ('runtime','source','textures','review','godot-check'): (ROOT/d).mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
s=bpy.context.scene;s.unit_settings.system='METRIC'
colours=['D3E5DF','A9BCB7','BA604F','E3D8BC','687B59','AF8960','C6A77B','E9C34B','303532','DFE6D8']
def linear(v):return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
img=bpy.data.images.new('LWF_Litter_Palette',width=80,height=8,alpha=False)
pixels=[]
for y in range(8):
 for x in range(80):
  h=colours[x//8];pixels.extend([int(h[i:i+2],16)/255 for i in (0,2,4)]+[1])
img.pixels=pixels;img.filepath_raw=str(ROOT/'textures/lwf_litter_palette_v1.png');img.file_format='PNG';img.save();img.pack()
mat=bpy.data.materials.new('LWF_Litter_Shared_Opaque');mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.9
tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=img;tex.interpolation='Closest';mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
mat.use_backface_culling=False
verts=[];faces=[];inks=[]
def face_mesh(v,f,c):
 start=len(verts);verts.extend(v)
 for face in f:faces.append(tuple(start+i for i in face));inks.append(c)
def box(p,d,c):
 v=[tuple(p[j]+sign[j]*d[j]/2 for j in range(3)) for sign in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
 face_mesh(v,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],c)
def frustum(r0,r1,z0,z1,c,caps=True,n=12,offset=(0,0)):
 v=[(offset[0]+r*math.cos(i*2*math.pi/n),offset[1]+r*math.sin(i*2*math.pi/n),z) for z,r in ((z0,r0),(z1,r1)) for i in range(n)]
 f=[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 if caps:f += [tuple(reversed(range(n))),tuple(range(n,2*n))]
 face_mesh(v,f,c)
def ring(r,t,z,h,c,n=12):
 v=[(rad*math.cos(i*2*math.pi/n),rad*math.sin(i*2*math.pi/n),zz) for zz,rad in ((z-h/2,r),(z+h/2,r),(z+h/2,r-t),(z-h/2,r-t)) for i in range(n)]
 f=[]
 for k in range(4):
  for i in range(n):f.append((k*n+i,k*n+(i+1)%n,((k+1)%4)*n+(i+1)%n,((k+1)%4)*n+i))
 face_mesh(v,f,c)
assets={};objects=[]
def finish(name,held_anchor=None):
 global verts,faces,inks
 me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.materials.append(mat)
 uv=me.uv_layers.new(name='Palette')
 for poly,c in zip(me.polygons,inks):
  for i in poly.loop_indices:uv.data[i].uv=((c+.5)/10,.5)
 o=bpy.data.objects.new(name,me);s.collection.objects.link(o);objects.append(o)
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 file=name.lower()+'.glb'
 bpy.ops.export_scene.gltf(filepath=str(ROOT/'runtime'/file),export_format='GLB',use_selection=True,export_yup=True,export_apply=True,export_animations=False)
 me.calc_loop_triangles();lo=[min(v.co[i] for v in me.vertices) for i in range(3)];hi=[max(v.co[i] for v in me.vertices) for i in range(3)]
 poses={}
 # Ground offset derived from rotated actual vertices, not guessed collider height.
 for label,angles in [('upright',(0,0,0)),('side',(math.pi/2,0,0)),('tilted',(0,.3,0))]:
  from mathutils import Euler
  m=Euler(angles).to_matrix();zmin=min((m@v.co).z for v in me.vertices)
  q=Euler(angles).to_quaternion();poses[label]={'rotation_blender_euler_rad':angles,'ground_offset_blender_z':-zmin,'quaternion_godot_xyzw':[q.x,q.z,-q.y,q.w],'ground_offset_godot_y':-zmin}
 assets[name]={'file':file,'triangles':len(me.loop_triangles),'bounds_blender':[lo,hi],'dimensions_blender':[hi[i]-lo[i] for i in range(3)],'held_grip_anchor_godot':held_anchor,'ground_poses':poses,'sha256':hashlib.sha256((ROOT/'runtime'/file).read_bytes()).hexdigest()}
 verts=[];faces=[];inks=[]
 return o
# Hollow opaque approximation of actual beer cup. No liquid/foam. Real interior walls/base.
frustum(.033,.045,0,.15,0,False)
frustum(.031,.043,.002,.15,1,False)
frustum(.033,.033,0,.002,0)
ring(.046,.003,.15,.004,0)
finish('LWF_Litter_Beer_Cup_V1',[0,.075,0])
frustum(.033,.0386,0,.063,2);frustum(.0386,.0413,.063,.097,3);frustum(.0413,.045,.097,.15,2)
frustum(.046,.047,.15,.156,3);frustum(.042,.04,.156,.16,3)
frustum(.003,.003,.159,.217,4,n=6,offset=(.009,0))
finish('LWF_Litter_Soft_Cup_V1',[0,.075,0])
box((0,0,.002),(.15,.09,.004),5)
v=[(-.075,-.045,0),(.075,-.045,0),(.075,.045,0),(-.075,.045,0),(-.09,-.06,.05),(.09,-.06,.05),(.09,.06,.05),(-.09,.06,.05)]
face_mesh(v,[(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],5)
for y in (-.06,.06):box((0,y,.049),(.18,.003,.003),6)
for x in (-.09,.09):box((x,0,.049),(.003,.12,.003),6)
finish('LWF_Litter_Chips_Tray_V1',[0,.025,0])
# Wasp centre is hover centre, +Y Blender / -Z Godot forward. Broad facets; no tiny legs.
def body_y(rings,n=6):
 v=[(r*math.cos(2*math.pi*i/n),y,r*.8*math.sin(2*math.pi*i/n)) for y,r,c in rings for i in range(n)]
 for k in range(len(rings)-1):face_mesh(v,[(k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i) for i in range(n)],rings[k][2])
 face_mesh(v,[tuple(reversed(range(n))),tuple(range((len(rings)-1)*n,len(rings)*n))],8)
body_y([(-.023,.001,8),(-.018,.006,7),(-.012,.008,8),(-.007,.008,7),(-.002,.006,8),(.005,.007,8),(.01,.005,7),(.015,.006,8),(.022,.002,8)])
for sign in (-1,1):
 face_mesh([(sign*.003,.005,.004),(sign*.021,.002,.011),(sign*.027,-.011,.009),(sign*.013,-.009,.005)],[(0,1,2,3)],9)
finish('LWF_Wasp_V1')
for o in objects:o['units']='metres';o['material_contract']='one shared opaque palette material'
source=ROOT/'source/lwf_litter_and_wasp_v1.blend';bpy.ops.wm.save_as_mainfile(filepath=str(source))
manifest={'status':'EXPORTED_AWAITING_NATIVE_QA','units':'metres','axes':'Blender Z up -> Godot Y up; wasp forward Blender +Y -> Godot -Z','origins':'litter base centre upright; wasp hover centre','assets':assets,'palette':'textures/lwf_litter_palette_v1.png','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'approved_concept':'../litter-wasp-concepts-v1/litter-wasp-board.png','bin_contract':'reuse shell and at most ONE existing fill at identity; extra litter separate, no bin mesh edits'}
(ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2))
print('LITTER_EXPORTS',json.dumps(assets))
