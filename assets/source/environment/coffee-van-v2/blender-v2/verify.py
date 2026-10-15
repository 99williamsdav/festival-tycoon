import bpy,os,json,struct,math
out=os.path.dirname(os.path.abspath(__file__)); path=os.path.join(out,'coffee-van.glb')
with open(path,'rb') as f:
 magic,version,length=struct.unpack('<4sII',f.read(12)); n,t=struct.unpack('<II',f.read(8)); doc=json.loads(f.read(n))
assert magic==b'glTF' and version==2 and length==os.path.getsize(path)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.gltf(filepath=path)
objs=[o for o in bpy.context.scene.objects if o.type=='MESH'];verts=[o.matrix_world@v.co for o in objs for v in o.data.vertices];bad=0
for o in objs:
 o.data.calc_loop_triangles()
 for t in o.data.loop_triangles:
  a,b,c=[o.data.vertices[i].co for i in t.vertices]
  if (b-a).cross(c-a).length<1e-12:bad+=1
report={'valid_glb':True,'meshes':len(objs),'triangles':sum(doc['accessors'][p['indices']]['count']//3 for m in doc['meshes'] for p in m['primitives']),'materials':len(doc['materials']),'images':len(doc.get('images',[])),'skins':len(doc.get('skins',[])),'animations':len(doc.get('animations',[])),'zero_area_triangles':bad,'nonfinite_vertices':sum(not all(math.isfinite(c) for c in v) for v in verts),'bounds_m':{'min':[min(v[i] for v in verts) for i in range(3)],'max':[max(v[i] for v in verts) for i in range(3)]},'excluded_nodes':not any(any(s in o.name for s in ['STUDIO','LWF_Attendee','LWF_Server','Open upward hatch','Hatch support','Sage hatch underside']) for o in objs)}
assert report['excluded_nodes'] and bad==0 and report['images']==0 and report['skins']==0
with open(os.path.join(out,'export-verification.json'),'w') as f:json.dump(report,f,indent=2)
print(json.dumps(report))
