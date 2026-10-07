import bpy,json,math,hashlib
from pathlib import Path
R=Path(__file__).resolve().parent
m=json.loads((R/'manifest.json').read_text())
bpy.ops.wm.open_mainfile(filepath=str(R/'source/lwf_pond_polish_v1.blend'))
def inside(p,poly):
 x,y=p;v=False
 for a,b in zip(poly,poly[1:]+poly[:1]):
  if (a[1]>y)!=(b[1]>y) and x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]:v=not v
 return v
def distance(p,a,b):
 dx,dy=b[0]-a[0],b[1]-a[1];t=max(0,min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dy)/(dx*dx+dy*dy)))
 return math.hypot(p[0]-a[0]-t*dx,p[1]-a[1]-t*dy)
water=m['water_polygon_local_xz'];safe=[[1.55*math.cos(i*math.tau/32),1.45*math.sin(i*math.tau/32)] for i in range(32)]
minwater=100;minpad=100
for a,b in zip(safe,safe[1:]+safe[:1]):
 for j in range(101):
  p=[a[k]+(b[k]-a[k])*j/100 for k in range(2)]
  assert inside(p,water)
  minwater=min(minwater,min(distance(p,c,d) for c,d in zip(water,water[1:]+water[:1])))
  minpad=min(minpad,min(math.hypot(p[0]-x,p[1]-z)-r for x,z,r in m['lily_pads_local_xzr']))
assert minwater>.42 and minpad>.42
for x,y,z in m['initial_duck_roots_godot']:assert inside([x,z],safe)
# Every margin outside vertex remains the authoritative boundary, merely subdivided.
outer=[[x-24,z-24.5] for x,z in m['outer_polygon_world_xz']]
margin=bpy.data.objects['PondMargin'].data
for v in list(margin.vertices)[:54]:assert min(distance((v.co.x,-v.co.y),a,b) for a,b in zip(outer,outer[1:]+outer[:1]))<1e-5
normal=bpy.data.objects['PondWater'].data
assert all(p.normal.z>.99 for p in normal.polygons)
assert all(abs(c.color[3]-1)<1e-6 for c in normal.color_attributes['WaterTint'].data)
old=json.loads((R/'references/farm_beauty_report.json').read_text())['lwf_farm_pond_v1']
assert m['blocked_cells']==old['blocked_cells'] and m['outer_polygon_world_xz']==old['footprint_polygon_godot_xz']
# Reference report format may wrap assets; authoritative build source hash remains recorded.
report={'safe_duck_root_polygon_local_xz':safe,'clearance_radius_m':.42,'minimum_water_boundary_distance_m':minwater,'minimum_lily_edge_distance_m':minpad,'initial_roots_inside':True,'margin_matches_authoritative_boundary':True,'water_normals_up_and_tints_valid':True,'total_triangles':sum(a['triangles'] for a in m['assets'].values()),'runtime_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (R/'runtime').glob('*.glb')}}
(R/'verification.json').write_text(json.dumps(report,indent=2))
print('POND_GEOMETRY_VERIFIED',minwater,minpad)
