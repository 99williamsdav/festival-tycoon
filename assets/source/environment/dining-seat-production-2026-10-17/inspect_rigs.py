import bpy,json
from pathlib import Path
R=Path(__file__).resolve().parent
out={}
for sex in ['male','female']:
 p=Path('C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out')/f'lwf_attendee_{sex}_rigged_test_v1.blend'
 bpy.ops.wm.open_mainfile(filepath=str(p))
 out[sex]={'objects':[],'rigs':{}}
 for o in bpy.data.objects:
  if o.type=='MESH':
   vs=[o.matrix_world@v.co for v in o.data.vertices]
   out[sex]['objects'].append({'name':o.name,'n':len(vs),'bounds':[[min(v[i] for v in vs) for i in range(3)],[max(v[i] for v in vs) for i in range(3)]],'groups':[g.name for g in o.vertex_groups]})
  if o.type=='ARMATURE':out[sex]['rigs'][o.name]=[{'name':b.name,'head':list(b.head_local),'tail':list(b.tail_local),'parent':b.parent.name if b.parent else None} for b in o.data.bones]
 (R/'rig-inspection.json').write_text(json.dumps(out,indent=2))
