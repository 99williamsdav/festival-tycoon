"""Read-only verification of final GLBs and generated evaluated-mesh evidence.
Run with normal Python 3: python verify_exports.py
"""
import json,struct,hashlib
from pathlib import Path
R=Path(__file__).resolve().parent
out={}
for kind,base,seats in [('hay','ft_hay_bale_h1_v1',2),('picnic','ft_picnic_aframe_p1_v1',4)]:
 raw=(R/'runtime'/(base+'.glb')).read_bytes();magic,version,length=struct.unpack_from('<4sII',raw);assert magic==b'glTF' and version==2 and length==len(raw)
 n,t=struct.unpack_from('<II',raw,12);g=json.loads(raw[20:20+n].decode('utf8'))
 mesh_nodes=[n for n in g['nodes'] if 'mesh' in n];anchors=[n for n in g['nodes'] if n.get('name','').startswith('Seat_')]
 assert len(mesh_nodes)==1 and len(anchors)==seats
 assert 'animations' not in g and 'skins' not in g and 'cameras' not in g
 assert len(g['materials'])==1 and len(g['meshes'])==1 and len(g['meshes'][0]['primitives'])==1
 assert all('bufferView' in i and 'uri' not in i for i in g['images'])
 node=mesh_nodes[0];assert node.get('translation',[0,0,0])==[0,0,0] and node.get('scale',[1,1,1])==[1,1,1] and node.get('rotation',[0,0,0,1])==[0,0,0,1] and 'matrix' not in node
 prim=g['meshes'][0]['primitives'][0];pos=g['accessors'][prim['attributes']['POSITION']];triangles=g['accessors'][prim['indices']]['count']//3
 rec=json.loads((R/('manifest-'+kind+'.json')).read_text());assert triangles==rec['triangles']
 assert abs(pos['min'][1])<.000001
 for guest in rec['seating_verification']:
  assert guest['native_scale']==[1,1,1]
  assert guest['body_asset_triangle_overlap_count']==0 and guest['food_asset_triangle_overlap_count']==0 and guest['food_body_triangle_overlap_count']==0
  assert not guest['evaluated_body_or_food_vertices_inside_solid_furniture']
  assert 0<=guest['contact_gap_to_seat_m']<=.002
  assert all(-.0001<=v['sole_min_z']<=.001 for v in guest['feet'].values())
  assert guest['food_or_hand_nearest_vertex_surface_gap_m']<=.002
  if kind=='picnic':assert guest['table_underside_thigh_gap_m']>.1
 pairs=json.loads((R/'verification'/(kind+'-neighbor-checks.json')).read_text())
 for pair in pairs:assert pair['body_body_triangles']==pair['food_food_triangles']==pair['food_neighbor_body_triangles']==0
 out[kind]={'sha256':hashlib.sha256(raw).hexdigest(),'bytes':len(raw),'triangles':triangles,'glb_mesh_nodes':mesh_nodes,'godot_bounds':[pos['min'],pos['max']],'anchors':anchors,'single_palette_embedded':True,'no_people_food_skins_or_animations':True,'evaluated_seating_checks':'PASS','neighbor_checks':'PASS'}
(R/'verification'/'export-verification.json').write_text(json.dumps(out,indent=2))
print(json.dumps(out,indent=2))
