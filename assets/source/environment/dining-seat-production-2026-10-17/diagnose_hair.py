import bpy,json
from pathlib import Path
R=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(R/'source/ft_hay_bale_h1_v1_occupied_review.blend'))
out={}
for o in bpy.data.objects:
 if o.type=='MESH' and 'Hair' in o.name:
  out[o.name]=[]
  for m in o.data.materials:
   out[o.name].append({'material':m.name,'nodes':[{'name':n.name,'image':n.image.name if n.image else None,'path':n.image.filepath if n.image else None,'packed':bool(n.image.packed_file) if n.image else None,'size':list(n.image.size) if n.image else None} for n in m.node_tree.nodes if n.type=='TEX_IMAGE']})
(R/'verification/hair-diagnostic.json').write_text(json.dumps(out,indent=2))
