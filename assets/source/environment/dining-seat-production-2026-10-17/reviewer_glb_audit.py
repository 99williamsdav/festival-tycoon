import json, struct, hashlib
from pathlib import Path
root=Path(__file__).resolve().parent
records=[]
for p in sorted((root/'runtime').glob('*.glb')):
 raw=p.read_bytes(); magic,version,length=struct.unpack_from('<III',raw)
 n,typ=struct.unpack_from('<II',raw,12); doc=json.loads(raw[20:20+n]); bo=20+n
 binary=raw[bo+8:]
 rec={'file':p.name,'sha256':hashlib.sha256(raw).hexdigest(),'header_valid':magic==0x46546c67 and version==2 and length==len(raw),'nodes':doc.get('nodes',[]),'meshes':len(doc.get('meshes',[])),'skins':len(doc.get('skins',[])),'animations':len(doc.get('animations',[])),'cameras':len(doc.get('cameras',[])),'materials':doc.get('materials',[]),'images':doc.get('images',[]),'textures':doc.get('textures',[]),'primitives':[]}
 for m in doc.get('meshes',[]):
  for prim in m['primitives']:
   a=doc['accessors'][prim['attributes']['POSITION']];bv=doc['bufferViews'][a['bufferView']];start=bv.get('byteOffset',0)+a.get('byteOffset',0);step=bv.get('byteStride',12)
   vs=[struct.unpack_from('<fff',binary,start+i*step) for i in range(a['count'])]
   rec['primitives'].append({'vertices':len(vs),'triangles':doc['accessors'][prim['indices']]['count']//3,'position_min':[min(v[i] for v in vs) for i in range(3)],'position_max':[max(v[i] for v in vs) for i in range(3)],'material':prim.get('material'),'attributes':list(prim['attributes'])})
 records.append(rec)
(root/'verification'/'independent-glb-audit.json').write_text(json.dumps(records,indent=2))
print(json.dumps(records,indent=2))
