import bpy,math,json,sys,struct
from pathlib import Path
from mathutils import Vector
R=Path(__file__).absolute().parent
def imp(path):
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path));return list(set(bpy.data.objects)-old)
def stats(obs):
 pts=[o.matrix_world@v.co for o in obs if o.type=='MESH' for v in o.data.vertices]
 lo=[min(v[i] for v in pts) for i in range(3)];hi=[max(v[i] for v in pts) for i in range(3)]
 bad=[];deg=[];tris=0
 for o in obs:
  if o.type!='MESH':continue
  o.data.calc_loop_triangles();tris+=len(o.data.loop_triangles)
  for v in o.data.vertices:
   if not all(math.isfinite(x) for x in v.co):bad.append(o.name)
  deg += [o.name for p in o.data.polygons if p.area<1e-10]
 return {'blender_bounds':[lo,hi],'godot_bounds':[[lo[0],lo[2],-hi[1]],[hi[0],hi[2],-lo[1]]],'dimensions_godot':[hi[0]-lo[0],hi[2]-lo[2],hi[1]-lo[1]],'triangles':tris,'mesh_nodes':len([o for o in obs if o.type=='MESH']),'materials':sorted({m.name for o in obs if o.type=='MESH' for m in o.data.materials}),'nonfinite_vertices':bad,'degenerate_faces':deg,'wheel_floor_min':{o.name:min((o.matrix_world@v.co).z for v in o.data.vertices) for o in obs if o.type=='MESH' and 'Wheel' in o.name}}
bpy.ops.wm.open_mainfile(filepath=str(R/'source/blue_tractor_B_v1.blend'));source=stats(list(bpy.context.scene.objects))
bpy.ops.wm.read_factory_settings(use_empty=True);tractor=imp(R/'runtime/blue_tractor_B_v1.glb');export=stats(tractor)
data=(R/'runtime/blue_tractor_B_v1.glb').read_bytes();length,kind=struct.unpack_from('<II',data,12);doc=json.loads(data[20:20+length])
extras=[]
def scan(o,p=''):
 if isinstance(o,dict):
  for k,v in o.items():
   if k=='extras':extras.append(p)
   scan(v,p+'/'+k)
 elif isinstance(o,list):
  for i,v in enumerate(o):scan(v,p+'/'+str(i))
scan(doc)
result={'source':source,'reimport':export,'bounds_match':all(abs(source['blender_bounds'][a][b]-export['blender_bounds'][a][b])<1e-5 for a in range(2) for b in range(3)),'glb_extras':extras,'animations':len(doc.get('animations',[])),'skins':len(doc.get('skins',[])),'cameras':len(doc.get('cameras',[])),'external_uris':[x['uri'] for key in ['buffers','images'] for x in doc.get(key,[]) if 'uri' in x],'bytes':len(data)}
print('VALIDATION',json.dumps(result)); assert result['bounds_match'] and not extras and not export['nonfinite_vertices'] and not export['degenerate_faces']
(R/'verification/glb-reimport.json').write_text(json.dumps(result,indent=2))
def material(name,color):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);return m
def setup():
 s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.use_denoising=True;s.cycles.transparent_max_bounces=32;s.render.threads_mode='FIXED';s.render.threads=6
 s.world=bpy.data.worlds.new('Review world');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.73,.8,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.7
 s.view_settings.view_transform='AgX'
 bpy.ops.mesh.primitive_plane_add(size=160,location=(0,0,-.015));g=bpy.context.object;g.name='REVIEW_GROUND';g.data.materials.append(material('Review warm ground',(.35,.32,.26)))
 bpy.ops.object.light_add(type='AREA',location=(-4,-6,10));bpy.context.object.data.energy=1700;bpy.context.object.data.size=8
 bpy.ops.object.light_add(type='SUN',location=(0,0,15));bpy.context.object.rotation_euler=(.45,-.4,-.6);bpy.context.object.data.energy=2
 bpy.ops.object.camera_add();s.camera=bpy.context.object;s.camera.data.type='ORTHO';return s
def render(s,name,yaw,size,target,res=(1280,720)):
 a=math.radians(yaw);t=Vector(target);s.camera.location=t+Vector((72*math.sin(a),-72*math.cos(a),58));s.camera.rotation_euler=(t-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.ortho_scale=size;s.render.resolution_x,s.render.resolution_y=res;s.render.resolution_percentage=100;s.render.filepath=str(R/'review'/name);bpy.ops.render.render(write_still=True)
s=setup()
for i,yaw in enumerate([45,135,225,315]):render(s,'isolated-'+str(i)+'.png',yaw,8.1,(0,0,1.25))
# Actual imported barn only, with the exact inactive conversion-node exclusions from Main.BarnStage.
barn=imp(R/'references/lwf_barn_stage_v1.glb')
hidden=[]
for o in barn:
 if o.name in ['Barn_SignBacking','Barn_Name','Barn_ExitSign'] or o.name.startswith('Barn_Lamp_'):
  o.hide_render=True;hidden.append(o.name)
for o in tractor:
 if o.parent is None:o.location.y=-2.1
# A simple review fill, not a new runtime lamp, prevents Cycles interior becoming entirely unlit.
bpy.ops.object.light_add(type='AREA',location=(0,-2,6));bpy.context.object.data.energy=300;bpy.context.object.data.size=8
fade_materials=[]
for o in barn:
 if o.type!='MESH':continue
 for m in o.data.materials:
  if m in fade_materials or not m.name.startswith(('Barn_Roof','Barn_WallFade','Barn_DoorFade','Barn_TrussFade')):continue
  fade_materials.append(m);m.use_nodes=True;n=m.node_tree.nodes;l=m.node_tree.links;out=n.get('Material Output');original=out.inputs['Surface'].links[0].from_socket;mix=n.new('ShaderNodeMixShader');mix.name='REVIEW_FADE';transparent=n.new('ShaderNodeBsdfTransparent');l.new(transparent.outputs[0],mix.inputs[1]);l.new(original,mix.inputs[2]);l.new(mix.outputs[0],out.inputs['Surface']);mix.inputs[0].default_value=1
for i,yaw in enumerate([45,135,225,315]):
 for faded in [False,True]:
  for m in fade_materials:m.node_tree.nodes['REVIEW_FADE'].inputs[0].default_value=.17 if faded else 1
  render(s,('barn-faded-' if faded else 'barn-solid-')+str(i)+'.png',yaw,32,(0,0,2))
# One honest default-zoom readability view (62 vertical ->110.22 horizontal).
render(s,'barn-default62-faded.png',45,62*16/9,(0,0,2))
(R/'verification/review-settings.json').write_text(json.dumps({'camera_yaws':[45,135,225,315],'depression_degrees':math.degrees(math.atan(58/72)),'barn_view_horizontal_width_m':32,'godot_equivalent_vertical_zoom':18,'fade_alpha':.17,'tractor_barn_local_godot':[0,0,2.1],'conversion_nodes_hidden':hidden,'notes':'Blender Cycles approximation of runtime fade, not Godot shader. No stage kit imported; review fill is not runtime lamp.'},indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(R/'source/review-barn.blend'))
print('REIMPORT AND 13 RENDERS COMPLETE')


