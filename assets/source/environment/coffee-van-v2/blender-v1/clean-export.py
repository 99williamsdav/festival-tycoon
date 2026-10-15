import bpy, bmesh, os, json
out=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(out,'coffee-van.blend'))
bpy.ops.object.select_all(action='DESELECT')
asset=[o for o in bpy.context.scene.objects if o.type=='MESH' and not o.name.startswith('STUDIO')]
for o in asset:
 bm=bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001); bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001); bmesh.ops.triangulate(bm,faces=list(bm.faces)); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(o.data); bm.free(); o.select_set(True)
for o in asset:
 bm=bmesh.new(); bm.from_mesh(o.data); bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.calc_area()<1e-12],context='FACES'); bm.to_mesh(o.data); bm.free()
bpy.ops.export_scene.gltf(filepath=os.path.join(out,'coffee-van.glb'),export_format='GLB',use_selection=True,export_apply=True)
with open(os.path.join(out,'asset-statistics.json')) as f: stats=json.load(f)
stats['triangles']=sum(len(o.data.polygons) for o in asset)
with open(os.path.join(out,'asset-statistics.json'),'w') as f: json.dump(stats,f,indent=2)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out,'coffee-van.blend'))
