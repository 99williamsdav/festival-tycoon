import bpy,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent
tech=json.loads((ROOT/'technical.json').read_text())
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'lwf_held_props_trio_v1.blend'))
for key,info in tech['assets'].items():
    o=bpy.data.objects['LWF_'+key]
    assert tuple(o.location)==(0,0,0) and tuple(o.scale)==(1,1,1)
    o.data.calc_loop_triangles();assert len(o.data.loop_triangles)==info['triangles']
results={}
for key,info in tech['assets'].items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path=ROOT/info['file'];bpy.ops.import_scene.gltf(filepath=str(path))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert len(meshes)==1
    o=meshes[0];o.data.calc_loop_triangles();assert len(o.data.loop_triangles)==info['triangles']
    vs=[o.matrix_world@v.co for v in o.data.vertices]
    bounds=[[min(v[i] for v in vs) for i in range(3)],[max(v[i] for v in vs) for i in range(3)]]
    assert max(abs(bounds[k][i]-info['bounds_blender'][k][i]) for k in range(2) for i in range(3))<.00001
    results[key]={'reimport_pass':True,'triangles':len(o.data.loop_triangles),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
print(json.dumps(results,indent=2))
