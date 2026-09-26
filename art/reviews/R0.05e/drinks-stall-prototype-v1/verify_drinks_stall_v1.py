import bpy, json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent
meta=json.loads((root/'technical.json').read_text())
def inspect(meshes):
    assert len(meshes)==4
    triangles=0
    coords=[]
    for o in meshes:
        o.data.calc_loop_triangles();triangles+=len(o.data.loop_triangles)
        coords += [o.matrix_world@Vector(c) for c in o.bound_box]
    assert triangles==654
    bounds=[[min(v[i] for v in coords) for i in range(3)],[max(v[i] for v in coords) for i in range(3)]]
    assert all(abs(bounds[j][i]-meta[key][i])<.0001 for j,key in enumerate(['bounds_blender_min','bounds_blender_max']) for i in range(3))
    assert any(tuple(image.size)==(1024,256) for image in bpy.data.images)
    print('DRINKS_STALL_VERIFIED',triangles,bounds)
bpy.ops.wm.open_mainfile(filepath=str(root/'lwf_drinks_stall_prototype_v1.blend'))
inspect([o for o in bpy.context.scene.objects if o.name.startswith('LWF_DrinksStallV1_')])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root/'lwf_drinks_stall_prototype_v1.glb'))
inspect([o for o in bpy.context.scene.objects if o.type=='MESH'])
