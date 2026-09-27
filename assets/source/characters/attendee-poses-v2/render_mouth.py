import bpy,math
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parent
for sex in ['male','female']:
 bpy.ops.wm.open_mainfile(filepath=str(R/(sex+'-eating.blend')));s=bpy.context.scene;c=s.camera;t=Vector((0,.10,1.51))
 c.location=t+Vector((3,8,1));c.rotation_euler=(t-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=.40;s.render.resolution_x=s.render.resolution_y=800;s.render.filepath=str(R/(sex+'-eating-mouth.png'));bpy.ops.render.render(write_still=True)
