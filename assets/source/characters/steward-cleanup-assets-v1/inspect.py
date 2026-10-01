import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parent.parent
for sex,p in [('male','attendee-neutral-v5/direction-a-attendee.blend'),('female','female-neutral-v3/female-attendee-neutral.blend')]:
 bpy.ops.wm.open_mainfile(filepath='C:/Projects/festival-tycoon/assets/source/characters/attendee-v6-draft/'+sex+'-attendee-v6.blend')
 print('OBJECTS',[(o.name,o.type,len(o.vertex_groups) if o.type=='MESH' else 0) for o in bpy.context.scene.objects])
 o=next(o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.vertex_groups)>10)
 report={}
 for g in o.vertex_groups:
  vs=[v.co for v in o.data.vertices if any(a.group==g.index for a in v.groups)]
  report[g.name]={'vertices':len(vs),'bounds':[[min(v[i] for v in vs) for i in range(3)],[max(v[i] for v in vs) for i in range(3)]]}
 print('BODY_GROUPS',sex,json.dumps(report))
