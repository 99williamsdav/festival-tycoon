import bpy,json,hashlib
from pathlib import Path
R=Path(__file__).resolve().parent;P=R.parent/'direction-a-blender-prototypes-v1/attendee-poses-v2'
m=json.loads((P/'manifest.json').read_text());out={}
for sex,entries in m['variants'].items():
 for entry in entries:
  p=P/entry['source_blend'];bpy.ops.wm.open_mainfile(filepath=str(p));o=next(ob for ob in bpy.context.scene.objects if ob.name.startswith('Attendee_') and ob.type=='MESH')
  sets={g.index:set(v.index for v in o.data.vertices if any(w.group==g.index for w in v.groups)) for g in o.vertex_groups};regions={};used=set()
  for g in o.vertex_groups:
   slots=set()
   for poly in o.data.polygons:
    if set(poly.vertices).issubset(sets[g.index]):
     for li in poly.loop_indices:
      uv=o.data.uv_layers.active.data[li].uv;slot=int(uv.x*12);assert abs(uv.x-(slot+.5)/12)<1e-5;slots.add(slot);used.add(slot)
   regions[g.name]=sorted(slots)
  for name,slots in regions.items():
   if 'hair' in name.lower():assert set(slots)<={10,11}
   elif name=='Continuous tee':assert set(slots)<={3,4,5,6}
   elif 'trouser' in name.lower() or name=='Hip volume':assert set(slots)<={7,8}
   elif 'shoe' in name.lower() or 'eyes' in name.lower():assert set(slots)=={9}
   else:assert set(slots)<={0,1,2},(name,slots)
  out[entry['file']]={'source_sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'used_slots':sorted(used),'regions':regions,'safe_semantic_separation':True}
(R/'slot-audit.json').write_text(json.dumps(out,indent=2));print('AUDIT PASS',len(out),'sources; no assets changed')
