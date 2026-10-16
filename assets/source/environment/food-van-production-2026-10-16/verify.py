"""Roundtrip geometry, embedded textures, and actual source server BVH checks."""
import bpy,math,json,sys,struct
from pathlib import Path
from mathutils import Vector as V
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent;S=Path('C:/Projects/festival-tycoon/assets/source/characters/service-server-assets-v1')
sys.path.insert(0,str(R));import build
manifest=json.loads((R/'manifest.json').read_text());rigmanifest=json.loads((S/'manifest.json').read_text())
result={'roundtrip':{},'clearance':{},'limitations':'All keyed integer frames of all actual source clips, static shell triangles; excludes runtime crossfades and terrain. Floor foot contact is expected. No altered staff placement or scale.'}
def imp(p):
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(p));return list(set(bpy.data.objects)-old)
def tree_for(ob):
 deps=bpy.context.evaluated_depsgraph_get();e=ob.evaluated_get(deps);me=e.to_mesh();vs=[e.matrix_world@v.co for v in me.vertices];fs=[tuple(p.vertices) for p in me.polygons];e.to_mesh_clear();return BVHTree.FromPolygons(vs,fs),vs
for kind,spec in manifest['assets'].items():
 path=R/spec['runtime'];raw=path.read_bytes();l,t=struct.unpack_from('<II',raw,12);g=json.loads(raw[20:20+l]);assert all('uri' not in i for i in g.get('images',[])),'External image in GLB'
 bpy.ops.wm.read_factory_settings(use_empty=True);obs=imp(path);meshes=[o for o in obs if o.type=='MESH'];bb=build.bounds(meshes);tris=sum(len(o.data.polygons) for o in meshes)
 assert tris==spec['triangles'],(kind,tris,spec['triangles']);assert max(abs(a-b) for v,w in zip(bb,spec['bounds_blender']) for a,b in zip(v,w))<1e-4,(kind,bb)
 assert not any(o.type=='ARMATURE' for o in obs);assert all(im.size[0]>0 for im in bpy.data.images),'Missing image data'
 result['roundtrip'][kind]={'triangles':tris,'bounds_blender':bb,'meshes':len(meshes),'materials':len(g.get('materials',[])),'embedded_images':len(g.get('images',[])),'file_bytes':len(raw),'no_armature':True,'external_image_uris':False,'steam_markers':[o.name for o in obs if o.name.startswith('LWF_FoodVan_SteamVent_')]}
 # Reimported final geometry visual QA, not editable source render.
 s,c=build.setup_render();build.render(s,c,R/'renders'/(kind+'-05-roundtrip.png'),9.6 if kind!='curry' else 10.7,yaw=35,target=(1.6,0,1.5))
for sex in ['male','female']:
 key='lwf_server_food_'+sex+'_v1'
 for kind,spec in manifest['assets'].items():
  bpy.ops.wm.open_mainfile(filepath=str(S/'source'/(key+'.blend')))
  rig=bpy.data.objects['LWF_Attendee_Rig'];body=bpy.data.objects['LWF_Attendee_Body'];actors=[o for o in bpy.data.objects if o.type=='MESH'];rig.location=(2.15,-.88,.47);rig.rotation_euler.z=math.pi
  env=imp(R/spec['runtime']);context=[(o.name,tree_for(o)[0]) for o in env if o.type=='MESH'];sock=bpy.data.objects['LWF_RightHand_Food'];props={}
  for item in ['lwf_chips_tray_v1','lwf_pizza_plate_v1','lwf_curry_tray_v1']:
   obs=imp(Path('C:/Projects/festival-tycoon/game/assets/props')/(item+'.glb'));pts=[];faces=[]
   for o in obs:
    if o.type=='MESH':
     offset=len(pts);pts.extend(o.matrix_world@v.co for v in o.data.vertices);faces.extend(tuple(offset+i for i in p.vertices) for p in o.data.polygons)
   props[item]=(pts,faces)
   for o in obs:bpy.data.objects.remove(o,do_unlink=True)
  rec={'frames_checked':0,'collisions':[],'max_foot_motion_m':0,'max_socket_tilt_degrees':0,'prop_min_counter_clearance_m':{},'held_prop_geometry_collisions':[],'held_prop_frames_checked':0,'body_sweep_bounds':None}
  footidx=[v.index for v in body.data.vertices if v.co.z<.04];firstfeet=None;lo=[99]*3;hi=[-99]*3
  for clip,cspec in rigmanifest['assets'][key]['clips'].items():
   rig.animation_data.action=bpy.data.actions[clip]
   for fr in range(1,cspec['frames']+1):
    bpy.context.scene.frame_set(fr);bpy.context.view_layer.update();rec['frames_checked']+=1
    for ob in actors:
     tree,vs=tree_for(ob)
     if ob==body:
      feet=[vs[i] for i in footidx]
      if firstfeet is None:firstfeet=feet
      rec['max_foot_motion_m']=max(rec['max_foot_motion_m'],max((p-q).length for p,q in zip(feet,firstfeet)))
     for v in vs:
      for a in range(3):lo[a]=min(lo[a],v[a]);hi[a]=max(hi[a],v[a])
     for name,et in context:
      overlap=tree.overlap(et)
      if overlap:rec['collisions'].append({'clip':clip,'frame':fr,'mesh':ob.name,'environment':name,'triangle_pairs':len(overlap)})
    if clip=='server_offer' or (clip=='server_pickup' and fr==cspec['frames']):
     up=sock.matrix_world.to_3x3()@V((0,0,1));rec['max_socket_tilt_degrees']=max(rec['max_socket_tilt_degrees'],math.degrees(up.angle(V((0,0,1)))))
     rec['held_prop_frames_checked']+=1
     for item,(pts,faces) in props.items():
      transformed=[sock.matrix_world@p for p in pts];clear=min(p.z for p in transformed)-1.235;rec['prop_min_counter_clearance_m'][item]=min(rec['prop_min_counter_clearance_m'].get(item,99),clear)
      ptree=BVHTree.FromPolygons(transformed,faces)
      for name,et in context:
       overlaps=ptree.overlap(et)
       if overlaps:rec['held_prop_geometry_collisions'].append({'clip':clip,'frame':fr,'prop':item,'environment':name,'triangle_pairs':len(overlaps)})
  rec['body_sweep_bounds']=[lo,hi];result['clearance'][kind+'_'+sex]=rec
  if sex=='male':
   rig.animation_data.action=bpy.data.actions['server_offer'];bpy.context.scene.frame_set(rigmanifest['assets'][key]['clips']['server_offer']['frames']);bpy.context.view_layer.update()
   s,c=build.setup_render();build.render(s,c,R/'renders'/(kind+'-06-actual-server-clearance.png'),9.6 if kind!='curry' else 10.7,yaw=35,target=(1.6,0,1.5))
   build.render(s,c,R/'renders'/(kind+'-08-actual-server-gameplay-min18.png'),32,yaw=325)
   build.render(s,c,R/'renders'/(kind+'-09-actual-server-gameplay-default62.png'),110.222222,yaw=325)
  (R/'verification'/'checks.json').write_text(json.dumps(result,indent=2))
print('VERIFICATION_DONE',json.dumps({k:{'frames':v['frames_checked'],'collisions':len(v['collisions']),'clearance':v['prop_min_counter_clearance_m']} for k,v in result['clearance'].items()}))
