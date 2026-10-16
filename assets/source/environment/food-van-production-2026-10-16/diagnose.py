import bpy,math,json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
R=Path(__file__).parent;S=Path('C:/Projects/festival-tycoon/assets/source/characters/service-server-assets-v1')
bpy.ops.wm.open_mainfile(filepath=str(S/'source/lwf_server_food_male_v1.blend'));rig=bpy.data.objects['LWF_Attendee_Rig'];rig.location=(2.15,-.88,.47);rig.rotation_euler.z=math.pi;rig.animation_data.action=bpy.data.actions['server_idle'];bpy.context.scene.frame_set(1)
old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(R/'runtime/ft_food_chips_production_v1.glb'));env=next(o for o in set(bpy.data.objects)-old if o.type=='MESH')
def data(o):
 e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());me=e.to_mesh();vs=[e.matrix_world@v.co for v in me.vertices];fs=[tuple(p.vertices) for p in me.polygons];e.to_mesh_clear();return vs,fs
vs,fs=data(env);tree=BVHTree.FromPolygons(vs,fs);out=[]
for name in ['LWF_Attendee_Body','LWF_Server_Apron']:
 v,f=data(bpy.data.objects[name]);over=BVHTree.FromPolygons(v,f).overlap(tree)
 for a,b in over:out.append({'actor':name,'environment_triangle':[list(vs[i]) for i in fs[b]],'actor_triangle':[list(v[i]) for i in f[a]]})
(R/'verification/diagnostic.json').write_text(json.dumps(out,indent=2))
