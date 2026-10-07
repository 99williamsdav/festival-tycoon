import bpy
from pathlib import Path
R=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(R/'source/lwf_pond_polish_v1.blend'))
m=bpy.data.objects['PondWater'].data
print('COLORS',[(tuple(c.color)) for c in m.color_attributes['WaterTint'].data][:15])
print('NORMALS', [tuple(p.normal) for p in m.polygons][:5])
