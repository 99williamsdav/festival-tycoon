import bpy,math,json,hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(R/'source/lwf_pond_willow_v1.blend'))
verts=[];faces=[]
for ob in [bpy.data.objects['WillowTrunkBranches'],bpy.data.objects['WillowHangingFoliage']]:
 off=len(verts);verts += [Vector((4.5-v.co.x,v.co.z,.5+v.co.y)) for v in ob.data.vertices]
 ob.data.calc_loop_triangles();faces += [tuple(off+i for i in t.vertices) for t in ob.data.loop_triangles]
bvh=BVHTree.FromPolygons(verts,faces,all_triangles=True)
knots={'drake':[(-1.25,-.3),(-1.12,.5),(-.68,.3),(-.82,-.65)],'brown':[(.65,-.65),(1.28,-.12),(1.15,.6),(.6,.7),(.5,.05)]}
def spline(a,b,c,d,t):return .5*(2*b+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)
report={'paths_snapshot_sha256':hashlib.sha256((R/'references/PondMotion.cs').read_bytes()).hexdigest(),'sampling':'201 uniformly spaced spline parameters per segment; conservative radius 0.42 m centred Y=0.25; actual game knots from snapshot','routes':{}}
for name,path in knots.items():
 closest=100;occluded=[0]*4;count=0
 for i in range(len(path)):
  a,b,c,d=[path[j%len(path)] for j in [i-1,i,i+1,i+2]]
  for step in range(201):
   t=step/200;x,z=[spline(a[k],b[k],c[k],d[k],t) for k in range(2)];p=Vector((x,.25,z));count+=1
   hit=bvh.find_nearest(p);closest=min(closest,hit[3])
   for orientation in range(4):
    yaw=math.radians(45+orientation*90);direction=Vector((math.sin(yaw)*72,58,math.cos(yaw)*72)).normalized()
    occluded[orientation]+=bvh.ray_cast(Vector((x,.4,z)),direction,30)[0] is not None
 report['routes'][name]={'minimum_mesh_distance_m':closest,'sphere_clearance_m':closest-.42,'samples':count,'headpoint_occlusion_fraction_S_W_N_E':[v/count for v in occluded]}
 assert closest>.42, (name,closest)
pond=json.loads((R/'references/pond-manifest.json').read_text())
def inside(p,poly):
 x,y=p;v=False
 for a,b in zip(poly,poly[1:]+poly[:1]):
  if (a[1]>y)!=(b[1]>y) and x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]:v=not v
 return v
poly=[[x-24,z-24.5] for x,z in pond['outer_polygon_world_xz']]
for v in bpy.data.objects['WillowTrunkBranches'].data.vertices:
 if v.co.z<.3: assert inside([4.5-v.co.x,.5+v.co.y],poly), 'Root outside authoritative pond boundary'
water=pond['water_polygon_local_xz'];covered=0;total=0
for ix in range(-50,51):
 for iz in range(-45,46):
  x,z=ix*.1,iz*.1
  if inside([x,z],water):
   total+=1;covered+=bvh.ray_cast(Vector((x,6,z)),Vector((0,-1,0)),5.925)[0] is not None
report['water_projected_canopy_coverage_fraction_grid_0_1m']=covered/total
report['root_low_geometry_inside_existing_pond_boundary']=True
report['runtime_sha256']=hashlib.sha256((R/'runtime/lwf_pond_willow_v1.glb').read_bytes()).hexdigest()
(R/'verification.json').write_text(json.dumps(report,indent=2));print('WILLOW_GEOMETRY_PASSED',json.dumps(report))

