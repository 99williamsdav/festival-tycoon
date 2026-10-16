import json,struct,hashlib
from pathlib import Path
R=Path(__file__).absolute().parent
p=R/'runtime/blue_tractor_B_v1.glb';raw=p.read_bytes();n=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+n]);r=json.loads((R/'verification/glb-reimport.json').read_text())
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
refs={'approved-B.png':R.parent/'blue-tractor-concepts-v1/concepts/b-cab-workhorse.png','lwf_barn_stage_v1.glb':Path('C:/Projects/festival-tycoon/game/assets/environment/barn-stage-v1/lwf_barn_stage_v1.glb')}
for name,original in refs.items():assert sha(R/'references'/name)==sha(original)
node_checks=[{'name':o.get('name'),'translation':o.get('translation',[0,0,0]),'scale':o.get('scale',[1,1,1]),'rotation':o.get('rotation',[0,0,0,1])} for o in g.get('nodes',[])]
manifest={'approved_concept':'B cabbed workhorse','status':'Built and validated; ready for coordinator inspection and Builder integration','runtime':'runtime/blue_tractor_B_v1.glb','editable':'source/blue_tractor_B_v1.blend','triangles':r['reimport']['triangles'],'mesh_nodes':len(g['meshes']),'material_count':len(g['materials']),'surfaces':sum(len(x['primitives']) for x in g['meshes']),'bytes':len(raw),'bounds':r['reimport']['godot_bounds'],'root_and_mesh_transforms':node_checks,'source_and_barn_reference_hashes_preserved':True,'geometry_authorship':'Original deterministic Blender script; no downloaded meshes. Approved concept was imagegen; runtime model is geometry/PBR materials, not generated textures.','textures':len(g.get('textures',[])),'review_render_count':len(list((R/'review').glob('*.png'))),'files':{str(f.relative_to(R)).replace('\\','/'):sha(f) for f in R.rglob('*') if f.is_file() and f.suffix in ['.glb','.blend','.py','.png','.json','.txt','.html'] and f.name not in ['manifest.json']}}
(R/'manifest.json').write_text(json.dumps(manifest,indent=2));print(json.dumps({k:manifest[k] for k in ['triangles','mesh_nodes','material_count','surfaces','bytes','review_render_count']},indent=2))

