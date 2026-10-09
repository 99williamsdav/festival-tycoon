"""Render the exported GLBs, not unexported authoring meshes."""
import bpy, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
for name,x in [('lwf_tree_oak_v2',-4.8),('lwf_tree_old_apple_v2',4.3)]:
    before=set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=str(ROOT/'runtime'/f'{name}.glb'))
    added=set(bpy.data.objects)-before
    for obj in added:
        if obj.parent not in added: obj.location.x+=x
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=16
scene.render.threads_mode='FIXED'; scene.render.threads=6; scene.cycles.use_denoising=True
scene.world=bpy.data.worlds.new('World'); scene.world.color=(.42,.42,.42)
scene.view_settings.view_transform='Standard'
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015))
mat=bpy.data.materials.new('ReviewGround'); mat.diffuse_color=(.32,.39,.20,1); bpy.context.object.data.materials.append(mat)
bpy.ops.object.light_add(type='AREA',location=(-10,-12,20)); bpy.context.object.data.energy=3000; bpy.context.object.data.size=10
bpy.ops.object.camera_add(); camera=bpy.context.object; scene.camera=camera; camera.data.type='ORTHO'
scene.render.resolution_x=1440; scene.render.resolution_y=960; scene.render.resolution_percentage=100
for index in range(4):
    angle=math.radians(15+index*90); target=Vector((-1,0,4.2))
    camera.location=target+Vector((math.sin(angle)*22,-math.cos(angle)*22,14))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler(); camera.data.ortho_scale=21
    scene.render.filepath=str(ROOT/'review'/f'exported-trees-{index}.png'); bpy.ops.render.render(write_still=True)
