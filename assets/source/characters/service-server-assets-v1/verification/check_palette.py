import bpy,json
from pathlib import Path
R=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(R/'references/male.blend'))
m=bpy.data.objects['LWF_Attendee_Body'].data.materials[0]
im=next(n.image for n in m.node_tree.nodes if n.type=='TEX_IMAGE')
im.filepath_raw=str(R/'verification/original-body-palette.png');im.file_format='PNG';im.save()
print('FIRST_PIXEL_FLOAT',list(im.pixels[:4]),im.colorspace_settings.name)
