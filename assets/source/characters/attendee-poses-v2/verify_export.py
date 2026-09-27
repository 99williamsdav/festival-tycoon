import bpy,json,math,hashlib,struct
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parent
tech=json.loads((R/'technical.json').read_text());manifest={'schema_version':1,'units':'metres','front':'Godot -Z','origin':'foot ground centre','body_only':True,'transform_rule':'Attach prop once under stable attendee root using Godot root-local transforms; do not also parent beneath exported socket. Body visual root remains identity.','palette':'Embedded 96x8 palette, preserved source material; no runtime recolouring required.','neutral_sources':{'male':'../attendee-neutral-v5/direction-a-attendee.blend','female':'../female-neutral-v3/female-attendee-neutral.blend'},'source_sha256':tech['bases'],'variants':{}}
checks={}
manifest['props']={key:{'file':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for key,p in {'beer':R.parent.parent/'held-props-trio-lager-v2/lwf_beer_cup_v2.glb','soft':R.parent.parent/'held-props-trio-v1/lwf_soft_drink_cup_v1.glb','tray':R.parent.parent/'held-props-trio-v1/lwf_chips_tray_v1.glb'}.items()}
for prefix,stats in tech['poses'].items():
 sex,pose=prefix.split('-',1);bpy.ops.wm.open_mainfile(filepath=str(R/(prefix+'.blend')))
 o=bpy.data.objects['Attendee_'+sex+'_'+pose];s=bpy.context.scene
 sockets=[ob for ob in s.objects if ob.name.startswith('Attachment_')]
 state='drinking' if pose in ['drinking','drinking_soft'] else pose
 product='soft' if pose=='drinking_soft' else 'beer' if pose=='drinking' else None
 suffix='drinking_soft' if product=='soft' else 'drinking_beer' if product=='beer' else pose
 filename=f'lwf_attendee_{sex}_{suffix}_v1.glb'
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True)
 for ob in sockets:ob.select_set(True)
 bpy.context.view_layer.objects.active=o
 bpy.ops.export_scene.gltf(filepath=str(R/filename),export_format='GLB',use_selection=True,export_yup=True,export_animations=False)
 raw=(R/filename).read_bytes();j=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
 binstart=28+struct.unpack_from('<I',raw,12)[0];imageview=j['bufferViews'][j['images'][0]['bufferView']];start=binstart+imageview.get('byteOffset',0);palettehash=hashlib.sha256(raw[start:start+imageview['byteLength']]).hexdigest()
 assert len(j['meshes'])==len(j['materials'])==1
 assert len(j['meshes'][0]['primitives'])==1
 assert not j.get('skins') and not j.get('animations')
 assert all('bufferView' in im for im in j['images'])
 expected=stats['triangles'];bodypoints=sorted(tuple(round(c,5) for c in v.co) for v in o.data.vertices)
 attachments={}
 if 'attachment' in stats:
  a=stats['attachment'];keys=['beer','soft'] if pose=='drink_hold' else ['soft'] if pose=='drinking_soft' else ['beer'] if pose=='drinking' else ['tray']
  attachments={key:{'position':a['godot_position'],'rotation_degrees':a['godot_rotation_degrees'],'scale':[1,1,1]} for key in keys}
  assert (sockets[0].location-Vector((a['godot_position'][0],-a['godot_position'][2],a['godot_position'][1]))).length<1e-6
 mouthgroup=next((g for g in o.vertex_groups if 'mouth' in g.name.lower()),None)
 mouth=Vector((0,.092,1.56)) if sex=='male' else None
 if mouthgroup:
  verts=[v.co for v in o.data.vertices if any(w.group==mouthgroup.index for w in v.groups)];mouth=sum(verts,Vector())/len(verts)
 mouthcheck={};contact={}
 if sockets:
  inverse=sockets[0].matrix_world.inverted();me=o.data;me.calc_loop_triangles()
  handgroups={g.index for g in o.vertex_groups if g.name in ['Right grip heel','Right curved palm and fingers','Right opposing thumb','Right palm-up support','Right tray steadying thumb']}
  ix={v.index for v in me.vertices if any(w.group in handgroups for w in v.groups)};samples=[]
  for tri in me.loop_triangles:
   if all(i in ix for i in tri.vertices):
    a,b,c=[inverse@me.vertices[i].co for i in tri.vertices];samples.extend([a,b,c,(a+b)/2,(b+c)/2,(a+c)/2,(a+b+c)/3])
  if 'tray' not in attachments:
   for kind in attachments:
    margins=[]
    for p in samples:
     x,y,z=p
     if -.075<=z<=.075:
      r=.033+.012*(z+.075)/.15 if kind=='beer' else (.033+.0056*(z+.075)/.063 if z<-.012 else .0386+.0027*(z+.012)/.034)
      angle=math.atan2(y,x);delta=(angle%(math.pi/6))-math.pi/12
      margins.append(math.hypot(x,y)-r*math.cos(math.pi/12)/math.cos(delta))
    contact[kind]={'maximum_sampled_side_penetration_m':max(0,-min(margins))}
  else:
   depths=[]
   for x,y,z in samples:
    h=z+.025
    if 0<h<.05:
     dx=.075+.3*h-abs(x);dy=.045+.3*h-abs(y)
     if dx>0 and dy>0:depths.append(min(dx,dy,h))
   contact['tray']={'maximum_sampled_intrusion_m':max(depths,default=0),'nominal_palm_support_gap_m':.0003}
 if pose in ['drinking','drinking_soft'] and mouth is not None:
  prop=next(ob for ob in s.objects if ob.type=='MESH' and ob.name.startswith('REVIEW_PROP_'))
  pp=[prop.matrix_world@v.co for v in prop.data.vertices]
  mouthcheck={'mouth_blender':list(mouth),'nearest_prop_vertex_to_mouth_m':min((p-mouth).length for p in pp)}
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(R/filename))
 imported=[ob for ob in bpy.context.scene.objects if ob.type=='MESH'];assert len(imported)==1
 me=imported[0].data;me.calc_loop_triangles();assert len(me.loop_triangles)==expected
 importedpoints=set(tuple(round(c,5) for c in imported[0].matrix_world@v.co) for v in me.vertices)
 assert set(bodypoints)==importedpoints
 assert all(abs(li.uv.y-.5)<1e-5 for li in me.uv_layers.active.data)
 if sockets:
  socket=next(ob for ob in bpy.context.scene.objects if ob.name.startswith('Attachment_'))
  a=stats['attachment'];assert (socket.matrix_world.translation-Vector((a['godot_position'][0],-a['godot_position'][2],a['godot_position'][1]))).length<1e-5
  assert abs(socket.matrix_world.to_euler().x-math.radians(a['godot_rotation_degrees'][0]))<1e-5
 entry={'state':state,'product':product,'file':filename,'source_blend':prefix+'.blend','source_blend_sha256':hashlib.sha256((R/(prefix+'.blend')).read_bytes()).hexdigest(),'sha256':hashlib.sha256(raw).hexdigest(),'embedded_palette_sha256':palettehash,'triangles':expected,'exported_vertices':stats['vertices'],'materials':1,'surfaces':1,'attachments':attachments}
 manifest['variants'].setdefault(sex,[]).append(entry)
 checks[prefix]={'reimport_passed':True,'body_world_vertices_preserved':True,'no_baked_props':True,'palette_embedded':True,'non_arm_regions_exactly_preserved':all(stats['unchanged_regions'].values()),'sampled_contact':contact,**mouthcheck}
(R/'manifest.json').write_text(json.dumps(manifest,indent=2));(R/'verification.json').write_text(json.dumps(checks,indent=2));print(json.dumps(checks))
