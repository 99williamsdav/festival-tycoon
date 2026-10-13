import json, hashlib, html
from pathlib import Path
R=Path(__file__).absolute().parent
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def save(name,data): (R/name).write_text(json.dumps(data,indent=2),encoding='utf-8')
m=json.loads((R/'manifest.json').read_text())
geo=json.loads((R/'verification/geometry.json').read_text())
native=json.loads((R/'verification/native-import.json').read_text())
for key,v in m['assets'].items():
 sex=v['sex'];p=R/'runtime'/('lwf_attendee_'+sex+'_fisticuffs_v1.tres')
 assert 'ext_resource' not in p.read_text()
 v['native_library']={'path':str(p.relative_to(R)).replace('\\','/'),'sha256':sha(p),'bytes':p.stat().st_size,'tracks_per_clip':51,'authoritative_runtime':True}
 for clip,c in v['clips'].items():
  lo,hi=geo['individual'][sex]['clips'][clip]['bounds_blender']
  c['sampled_body_bounds_godot']=[[lo[0],lo[2],-hi[1]],[hi[0],hi[2],-lo[1]]]
  c['native_duration_s']=native['attendee-'+sex]['clips'][clip]['duration']
m['spacing']={'recommended_root_distance_m':[0.90,1.00],'preview_m':0.90,'conditions':'scale 1, mutually facing, level ground; remove old positional shake','tested_but_not_preferred_m':0.85,'unsafe_universal_target_m':0.80}
m['status']='asset delivery complete; game integration owned by Builder'
m['validation_files']=['verification/'+x+'.json' for x in ['geometry','outfits','interruptions','native-import','native-transitions']]
save('manifest.json',m)
repo=Path('C:/Projects/festival-tycoon')
refs=list((R/'references').glob('*.blend'))
refs+=list((repo/'assets/source/characters/rigged-test-v1/out').glob('lwf_*_rigged_test_v1.blend'))
refs+=list((repo/'game/assets/characters').glob('lwf_hair_*_default_v1.glb'))
refs+=[repo/'game/assets/characters/attendee_palette_v1_contract.json']
refs+=list((R.parent/'fisticuffs-animation-concepts-v1').glob('*.png'))
save('REFERENCES.json',{'note':'Read-only inputs; current source snapshots hashed at delivery. Input attendee copies are retained locally. Review recolour only; runtime has no mesh/material copies.','files':{str(p):{'sha256':sha(p),'bytes':p.stat().st_size} for p in refs if p.exists()}})
sections=[('Paired poses',['pair-side','pair-three-quarter','argue','guard','swing-left','return','reverse','stand-down']),('Camera azimuths',['angle-'+str(i) for i in range(4)]),('Spacing comparisons',['spacing-'+x for x in ['0.75','0.85','0.90','1.00','1.15']]),('Variants and staged intervention',['male-male','female-female','steward-intervention']),('Actual minimum zoom framing',['game-min18-'+str(i) for i in range(4)]),('Actual default zoom framing',['game-default62-'+str(i) for i in range(4)])]
page='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Fisticuffs — asset review</title><style>body{font:17px/1.5 system-ui;background:#18221c;color:#e7eee9;margin:32px auto;padding:0 24px;max-width:1200px}h1{font-size:38px}p{max-width:850px}a{color:#addbcc}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:18px}figure{margin:0;background:#28352c;border-radius:12px;overflow:hidden}img,video{width:100%;display:block}figcaption{padding:12px}video{max-width:850px;border-radius:12px}small{color:#b8cbbf}</style><h1>Fisticuffs · animation assets v1</h1><p>Eight clips per existing adult rig. Recommended spacing: <strong>0.90–1.00 m</strong>; motion shown at 0.90 m. Short alternating swings, planted feet and a restrained stand-down.</p><p><a href="HANDOFF.txt">Builder handoff</a> · <a href="manifest.json">Asset manifest</a> · <a href="verification/native-transitions.json">Native transition results</a></p><p><small>Actual asset renders on a controlled stage. Game camera angles and zoom framing are reproduced; these are not integrated game captures. The sage clothing is an existing review recolour. No new character geometry.</small></p><video controls loop muted playsinline poster="review/motion-000.png"><source src="review/paired-motion.mp4" type="video/mp4"></video><p>Sequence: enter → guard → first swing/reaction → guard → return swing/reaction → stand-down. The spacing comparison at 0.75 m is intentionally outside the recommended range; 0.85 m has less clearance.</p>'''
for title,names in sections:
 page+='<h2>'+title+'</h2><div class="grid">'
 for n in names:
  assert (R/'review'/(n+'.png')).exists(),n
  page+='<figure><a href="review/'+n+'.png"><img loading="lazy" src="review/'+n+'.png" alt="'+html.escape(n)+'"></a><figcaption>'+html.escape(n.replace('-',' '))+'</figcaption></figure>'
 page+='</div>'
page+='<p>Native playback passed on ten current rig variants, including walk entry, mid-swing interruption and zero-speed pause. Builder still owns terrain/navigation, incident timing and state integration. See the handoff for sampling limits.</p></html>'
(R/'review.html').write_text(page,encoding='utf-8')
(R/'CURRENT-TASK.txt').write_text('COMPLETE: approved fisticuffs animation asset package ready for Builder. Eight clips per adult rig, native libraries, paired spacing tests and actual motion previews delivered. Game integration remains Builder-owned.\n')
save('PROVENANCE-sha256.json',{'files':{str(p.relative_to(R)).replace('\\','/'):sha(p) for p in sorted(R.rglob('*')) if p.is_file() and '.godot' not in p.parts and p.name!='PROVENANCE-sha256.json' and p.suffix!='.blend1'}})
print('DELIVERY FINALIZED',len(list((R/'review').glob('*.png'))),'renders')

