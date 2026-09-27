import bpy,bmesh,math,json,hashlib,struct
from pathlib import Path
from mathutils import Vector,Matrix
R=Path(__file__).resolve().parent
ROOT=R.parent.parent
SOURCES={'male':R.parent/'attendee-neutral-v5/direction-a-attendee.blend','female':R.parent/'female-neutral-v3/female-attendee-neutral.blend'}
PROPS={'beer':ROOT/'held-props-trio-lager-v2/lwf_beer_cup_v2.glb','soft':ROOT/'held-props-trio-v1/lwf_soft_drink_cup_v1.glb','tray':ROOT/'held-props-trio-v1/lwf_chips_tray_v1.glb'}
def ids(o,name):
 g=o.vertex_groups[name];return [v.index for v in o.data.vertices if any(w.group==g.index for w in v.groups)]
def capture(o,names):
 chosen=set(i for n in names for i in ids(o,n));order=sorted(chosen);mapping={v:i for i,v in enumerate(order)}
 return ([tuple(o.data.vertices[i].co) for i in order],[(tuple(mapping[i] for i in p.vertices),[tuple(o.data.uv_layers.active.data[i].uv) for i in p.loop_indices]) for p in o.data.polygons if all(i in chosen for i in p.vertices)],{n:[mapping[i] for i in ids(o,n)] for n in names})
HANDS={}
for old in ['cup','tray']:
 bpy.ops.wm.open_mainfile(filepath=str(R.parent/f'holding-poses-v1/attendee-{old}-pose.blend'))
 o=bpy.data.objects[f'Attendee_{old}_static'];HANDS[old]=capture(o,['Right grip heel','Right curved palm and fingers','Right opposing thumb'] if old=='cup' else ['Right palm-up support','Right tray steadying thumb'])
REPORT={'bases':{k:hashlib.sha256(p.read_bytes()).hexdigest() for k,p in SOURCES.items()},'poses':{}}
def render(name,yaw=25,elev=12,width=2.15,target=(0,0,.88),res=(900,900)):
 c=s.camera;t=Vector(target);a=math.radians(yaw);c.location=t+Vector((10*math.sin(a),10*math.cos(a),10*math.tan(math.radians(elev))));c.rotation_euler=(t-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=width;s.render.resolution_x,s.render.resolution_y=res;s.render.filepath=str(R/name);bpy.ops.render.render(write_still=True)
def export(name,obs):
 bpy.ops.object.select_all(action='DESELECT')
 for ob in obs:ob.hide_set(False);ob.select_set(True)
 bpy.context.view_layer.objects.active=obs[0]
 bpy.ops.export_scene.gltf(filepath=str(R/name),export_format='GLB',use_selection=True,export_yup=True,export_animations=False)
 raw=(R/name).read_bytes();j=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
 return {'sha256':hashlib.sha256(raw).hexdigest(),'triangles':sum(j['accessors'][p['indices']]['count']//3 for m in j['meshes'] for p in m['primitives']),'meshes':len(j['meshes']),'surfaces':sum(len(m['primitives']) for m in j['meshes']),'materials':len(j['materials']),'vertices':sum(j['accessors'][p['attributes']['POSITION']]['count'] for m in j['meshes'] for p in m['primitives']),'embedded_images':all('bufferView' in im for im in j.get('images',[]))}
for variant,source in SOURCES.items():
 for pose in ['relaxed','drink_hold','food_hold','drinking','drinking_soft','eating']:
  bpy.ops.wm.open_mainfile(filepath=str(source));s=bpy.context.scene
  o=next(ob for ob in s.objects if ob.type=='MESH' and len(ob.vertex_groups)>10);mat=o.data.materials[0]
  original={g.name:sorted(tuple(o.data.vertices[i].co) for i in ids(o,g.name)) for g in o.vertex_groups}
  excluded=set()
  if pose!='relaxed':excluded.update(['Right bare arm','Right mitten palm','Right thumb'])
  if pose=='eating':excluded.update(['Left bare arm','Left mitten palm','Left thumb'])
  V,pairs,groups=capture(o,[g.name for g in o.vertex_groups if g.name not in excluded]);F=[p[0] for p in pairs];UV=[p[1] for p in pairs]
  def face(f):F.append(tuple(f));UV.append([(4/96,.5)]*len(f))
  def limb(name,centres,radii):
   start=len(V)
   for k,(pt,rad) in enumerate(zip(centres,radii)):
    tangent=Vector(centres[min(k+1,len(centres)-1)])-Vector(centres[max(k-1,0)])
    q=tangent.to_track_quat('Z','Y')
    V.extend(tuple(Vector(pt)+q@Vector((rad[0]*math.cos(math.tau*i/8),rad[1]*math.sin(math.tau*i/8),0))) for i in range(8))
   for k in range(len(centres)-1):
    for i in range(8):face((start+k*8+i,start+k*8+(i+1)%8,start+(k+1)*8+(i+1)%8,start+(k+1)*8+i))
   face([start+i for i in reversed(range(8))]);face([start+(len(centres)-1)*8+i for i in range(8)])
   groups[name]=list(range(start,len(V)))
  sockets=[];attach=None;rotation=Matrix.Identity(4);key=None
  mouth=Vector((0,.092,1.56) if variant=='male' else (0,.0858,1.539908))
  if pose!='relaxed':
   iscup=pose in ['drink_hold','drinking','drinking_soft'];kind='cup' if iscup else 'tray';key='beer' if iscup else 'tray'
   oldattach=Vector((.195,.350,1.120) if iscup else (.140,.350,1.075))
   attach=Vector((.20,.245,1.18) if iscup else (.115,.245,1.155))
   if pose=='drinking':
    rotation=Matrix.Rotation(math.radians(55),4,'X');attach=mouth+Vector((0,.001,0))-(rotation@Vector((0,-.046,.076)))
   if pose=='drinking_soft':attach=mouth-Vector((.009,0,.142));key='soft'
   if variant=='female':attach.x-=.008 if pose not in ['drinking','drinking_soft'] else 0
   trans=Matrix.Translation(attach)@rotation@Matrix.Translation(-oldattach)
   hv,hf,hg=HANDS[kind];offset=len(V);V.extend(tuple(trans@Vector(p)) for p in hv)
   for f,uv in hf:F.append(tuple(offset+i for i in f));UV.append(uv)
   for n,indices in hg.items():groups[n]=[offset+i for i in indices]
   wrist=trans@Vector((.252,.270,1.075) if iscup else (.171,.266,1.021))
   start=(.209 if variant=='male' else .203,.016,1.215)
   elbow=Vector((.238,.045,1.045)) if pose not in ['drinking','drinking_soft'] else Vector((.240,.190,1.235))
   fore=elbow.lerp(wrist,.52)
   limb('Right posed arm',[start,Vector(start).lerp(elbow,.55),elbow,fore,wrist],[(.038,.043),(.039,.041),(.036,.037),(.031,.033),(.028,.030)])
   # Join the arm's final ring to the preserved fitted hand heel, avoiding a seam gap.
   heel='Right grip heel' if iscup else 'Right palm-up support'
   for dst,src in zip(groups['Right posed arm'][-8:],groups[heel][:8]):V[dst]=V[src]
   socket=bpy.data.objects.new('Attachment_Cup' if iscup else 'Attachment_Food',None);s.collection.objects.link(socket);socket.matrix_world=Matrix.Translation(attach)@rotation;sockets.append(socket)
  if pose=='eating':
   limb('Left eating arm',[(-.209 if variant=='male' else -.203,.016,1.215),(-.235,.105,1.22),(-.238,.185,1.245),(-.155,.187,1.365),(-.075,.175,1.485)],[(.038,.043),(.039,.041),(.036,.037),(.032,.033),(.026,.028)])
   limb('Left eating palm',[(-.075,.175,1.485),(-.063,.155,1.515),(-.048,.135,1.540)],[(.026,.028),(.030,.022),(.022,.017)])
   for dst,src in zip(groups['Left eating palm'][:8],groups['Left eating arm'][-8:]):V[dst]=V[src]
   limb('Left pinching fingers',[(-.048,.135,1.54),(-.025,.119,1.554),(-.009,.107,1.554)],[(.022,.017),(.016,.012),(.009,.009)])
   limb('Left opposing eating thumb',[(-.067,.143,1.517),(-.036,.114,1.531),(-.015,.107,1.544)],[(.012,.014),(.012,.011),(.009,.008)])
   delta=Vector((0,mouth.y+.002-.107,mouth.z-1.554))
   for name in ['Left eating palm','Left pinching fingers','Left opposing eating thumb']:
    for i in groups[name]:V[i]=tuple(Vector(V[i])+delta)
   for k,i in enumerate(groups['Left eating arm'][-16:]):V[i]=tuple(Vector(V[i])+delta*(.5 if k<8 else 1))
  mesh=bpy.data.meshes.new(variant+'_'+pose);mesh.from_pydata(V,[],F);mesh.materials.append(mat);uv=mesh.uv_layers.new(name='PaletteUV')
  for p,coords in zip(mesh.polygons,UV):
   for li,co in zip(p.loop_indices,coords):uv.data[li].uv=co
  bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free();o.data=mesh;o.vertex_groups.clear()
  for n,ix in groups.items():g=o.vertex_groups.new(name=n);g.add(ix,1,'REPLACE')
  o.name='Attendee_'+variant+'_'+pose
  unchanged={n:original[n]==sorted(tuple(mesh.vertices[i].co) for i in groups[n]) for n in original if n not in excluded};assert all(unchanged.values()),str([n for n,v in unchanged.items() if not v])
  prefix=variant+'-'+pose
  # Draft body export; final contract is checked with Builder before handoff.
  stats=export(prefix+'.glb',[o]+sockets);stats['unchanged_regions']=unchanged;stats['authored_vertices']=len(V)
  if attach is not None:
   stats['attachment']={'node':sockets[0].name,'godot_position':[attach.x,attach.z,-attach.y],'godot_rotation_degrees':[55 if pose=='drinking' else 0,0,0],'scale':[1,1,1],'prop':key}
   before=set(s.objects);bpy.ops.import_scene.gltf(filepath=str(PROPS[key]));props=[ob for ob in s.objects if ob not in before]
   for ob in props:
    if ob.parent not in props:ob.matrix_world=Matrix.Translation(attach)@rotation@ob.matrix_world
    ob.name='REVIEW_PROP_'+ob.name
   render(prefix+'-contact.png',50,18,.63,(attach.x,attach.y,attach.z))
   render(prefix+'-contact-side.png',105,8,.63,(attach.x,attach.y,attach.z))
  render(prefix+'-front.png',0,5)
  render(prefix+'-side.png',90,5)
  render(prefix+'-game.png',45,math.degrees(math.atan2(58,72)),32,(0,0,.88),(1920,1080))
  render(prefix+'-beauty.png')
  bpy.ops.wm.save_as_mainfile(filepath=str(R/(prefix+'.blend')))
  REPORT['poses'][prefix]=stats
(R/'technical.json').write_text(json.dumps(REPORT,indent=2));print(json.dumps(REPORT))
