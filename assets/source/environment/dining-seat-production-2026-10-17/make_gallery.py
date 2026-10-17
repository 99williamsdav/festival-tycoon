"""Create the local review gallery and SHA256 provenance using Python 3."""
from pathlib import Path
import json,hashlib,html
R=Path(__file__).resolve().parent
body=['<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>H1 + P1 production review</title><style>body{margin:0;background:#172322;color:#f2eacf;font:17px/1.55 system-ui}main{max-width:1500px;margin:auto;padding:32px}h1,h2{line-height:1.15}p{max-width:1050px}a{color:#ffd17a}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}figure{margin:0;background:#223330;padding:10px;border-radius:12px}img{display:block;width:100%;height:auto}figcaption{padding:9px 4px}small{color:#c5cebb}.wide{grid-column:1/-1}@media(max-width:850px){.grid{grid-template-columns:1fr}}</style></head><body><main><h1>Approved H1 + P1 → production review</h1><p>Only H1 plain tied hay bale and P1 classic A-frame. Editable sources, clean runtime GLBs and actual current rig seating evidence. Review poses are static authored samples; no runtime seated animation or mechanics are implemented. Independent GPT-6 Astra medium asset review: PASS.</p><p><a href="HANDOFF.md">Handoff</a> · <a href="REVIEW.md">Independent PASS</a> · <a href="runtime/ft_hay_bale_h1_v1.glb">H1 GLB</a> · <a href="source/ft_hay_bale_h1_v1.blend">H1 Blender</a> · <a href="runtime/ft_picnic_aframe_p1_v1.glb">P1 GLB</a> · <a href="source/ft_picnic_aframe_p1_v1.blend">P1 Blender</a> · <a href="verification/export-verification.json">GLB checks</a> · <a href="SEATING-AND-AXES.md">Seat anchors, approach and clearance</a></p>']
for kind,label,ref in [('hay','H1 — plain tied hay bale','approved-H1.png'),('picnic','P1 — classic A-frame picnic table','approved-P1.png')]:
 m=json.loads((R/('manifest-'+kind+'.json')).read_text());body+=['<h2>'+label+'</h2><p>'+str(m['triangles'])+' triangles · one palette material · '+str(len(m['anchors']))+' seat anchors · GLB excludes people and food.</p><div class="grid">']
 def fig(path,cap):return '<figure><a href="'+path+'"><img src="'+path+'" alt="'+html.escape(cap)+'"></a><figcaption>'+html.escape(cap)+'</figcaption></figure>'
 body.append(fig('references/'+ref,'ACTUAL APPROVED CONCEPT — illustrative people and food, not a fit test'))
 body.append(fig('renders/'+kind+'-01-close.png','PRODUCTION MODEL — close view, actual editable geometry'))
 for suffix,cap in [('02-rear','Rear / frame and twine visibility'),('03-above','Above / full footprint'),('04-occupied','Native current male and female rigs — static review seated/eating pose'),('05-occupied-rear','Occupied rear / attachment and pelvis support evidence'),('06-contact-side','Near-level side / grounded feet, leg posture and seat contact')]:body.append(fig('renders/'+kind+'-'+suffix+'.png',cap))
 body.append('</div><h3>Honest gameplay camera — full uncropped 1600 × 900 frames</h3><p>Actual project camera elevation 38.853°. Min zoom vertical span18m / horizontal32m; default vertical62m / horizontal110.222m. These full frames intentionally show the small apparent asset size. Click to open the original PNG.</p><div class="grid">')
 body.append(fig('renders/'+kind+'-07-gameplay-min18.png','Occupied — min18 full frame'))
 body.append(fig('renders/'+kind+'-08-gameplay-default62.png','Occupied — default62 full frame'))
 body.append(fig('renders/'+kind+'-09-gameplay-empty-min18.png','Production geometry alone — min18 full frame'))
 body.append('</div>')
body+=['<p>Ground plane, people, hair, props, lights and cameras exist only in review source scenes and PNGs. Physical fit is verified for these six static review poses at native scale; gameplay capacity is not decided. Traversal, sit/stand transitions and runtime animation remain untested and outside this package.</p></main></body></html>']
(R/'review.html').write_text('\n'.join(body),encoding='utf8')
files=[p for p in R.rglob('*') if p.is_file() and p.suffix.lower() in ['.glb','.blend','.png','.py','.md','.json','.html'] and p.name!='PROVENANCE-sha256.json']
provenance={str(p.relative_to(R)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
refs=[Path('C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out')/('lwf_attendee_'+s+'_rigged_test_v1.blend') for s in ['male','female']]
refs+=[Path('C:/Projects/festival-tycoon/game/assets/props')/n for n in ['lwf_pizza_plate_v1.glb','lwf_chips_tray_v1.glb']]
(R/'PROVENANCE-sha256.json').write_text(json.dumps({'package':provenance,'read_only_source_references':{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in refs}},indent=2))
print('Gallery and provenance written.')

