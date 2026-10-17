import bpy,json
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parent
out=[]
for kind,base,scale in [('hay','ft_hay_bale_h1_v1',2.1),('picnic','ft_picnic_aframe_p1_v1',3.5)]:
 bpy.ops.wm.open_mainfile(filepath=str(R/'source'/(base+'.blend')))
 for o in list(bpy.context.scene.objects):
  if o.name.startswith(('H1 ','P1 ','Seat_')):bpy.data.objects.remove(o,do_unlink=True)
 before=set(bpy.data.objects)
 bpy.ops.import_scene.gltf(filepath=str(R/'runtime'/(base+'.glb')))
 objects=set(bpy.data.objects)-before;mesh=next(o for o in objects if o.type=='MESH');mat=mesh.data.materials[0]
 tex=next(n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE');image=tex.image
 out.append({'asset':base,'image_size':list(image.size),'image_loaded':image.has_data,'packed':bool(image.packed_file),'mesh_material':mat.name,'uv_layers':[u.name for u in mesh.data.uv_layers]})
 scene=bpy.context.scene;c=scene.camera;c.location=(-3,4,3);target=Vector((0,0,.23 if kind=='hay' else .37));c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=scale
 scene.render.resolution_x=1000;scene.render.resolution_y=700;scene.render.filepath=str(R/'verification'/('independent-'+kind+'-glb-roundtrip.png'));bpy.ops.render.render(write_still=True)
(R/'verification'/'independent-roundtrip.json').write_text(json.dumps(out,indent=2))
