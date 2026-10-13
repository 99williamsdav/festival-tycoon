exec(open(__file__.replace('verify.py','review_scene.py')).read().split("if __name__=='__main__':")[0])
from mathutils.bvhtree import BVHTree
from mathutils import Matrix
barn,s=setup();bpy.context.view_layer.update()
def tree(objects):
 vs=[];fs=[]
 for o in objects:
  if o.type!='MESH':continue
  off=len(vs);vs += [o.matrix_world@v.co for v in o.data.vertices];fs += [tuple(off+j for j in p.vertices) for p in o.data.polygons]
 return BVHTree.FromPolygons(vs,fs)
t=tree(barn);floors=tree([o for o in barn if o.name=='Barn_Floor']);fails=[];door_samples=0
for label,(a,b) in {'main':(-3.7,3.7),'exit':(-8.45,-6.85)}.items():
 for i in range(17):
  x=a+(b-a)*i/16
  for z in [.15,.8,1.65,2.15]:
   hit=t.ray_cast(Vector((x,-7.5,z)),Vector((0,1,0)),2.5);door_samples+=1
   if hit[0] is not None:fails.append([label,x,z,tuple(hit[0])])
floor_samples=0;max_floor_error=0
for x in [-9,-7.65,-5,-2,0,2,4,6,8,9]:
 for y in [-5.3,-3,0,3,5.3]:
  hit=floors.ray_cast(Vector((x,y,2)),Vector((0,0,-1)),3);floor_samples+=1
  if hit[0] is None:fails.append(['missing floor',x,y])
  else:max_floor_error=max(max_floor_error,abs(hit[0].z))
def godot(p):return [p.x,p.z,-p.y]
kit=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith(('RiserDeck','RiserSteelFrame','RiserAccess','RiserCornerRails'))];kt=tree(kit)
stage_checks=[]
for x,z in [(5.7,-.25),(5.7,-2.75),(7.05,-1.5),(6.2,-1.5)]:
 hit=kt.ray_cast(Vector((x,-z,3)),Vector((0,0,-1)),4);stage_checks.append({'xz':[x,z],'surface_height':hit[0].z if hit[0] else None})
 if hit[0] is None or abs(hit[0].z-.9)>.02:fails.append(['bad deck',x,z])
access=next(o for o in kit if o.name=='RiserAccess');rails=next(o for o in kit if o.name=='RiserCornerRails')
access_tree=tree([access]);rail_tree=tree([rails]);overlaps=access_tree.overlap(rail_tree)
if overlaps:fails.append(['stair rail overlaps',len(overlaps)])
stairs=[]
for i in range(5):
 local=Vector((4.62-i*.36,.8,0));p=Vector((6.2,1.5,0))+Matrix.Rotation(-math.pi/2,3,'Z')@local
 hit=access_tree.ray_cast(Vector((p.x,p.y,3)),Vector((0,0,-1)),4);expected=.18*(i+1)
 stairs.append({'godot_xz':[p.x,-p.y],'height':hit[0].z if hit[0] else None,'expected':expected})
 if hit[0] is None or abs(hit[0].z-expected)>.02:fails.append(['bad stair',i])
verts=[o.matrix_world@v.co for o in barn if o.type=='MESH' for v in o.data.vertices];lo=[min(v[j] for v in verts) for j in range(3)];hi=[max(v[j] for v in verts) for j in range(3)]
report={'door_samples':door_samples,'floor_samples':floor_samples,'max_floor_error':max_floor_error,'stage_checks':stage_checks,'stairs':stairs,'stair_corner_rail_triangle_overlaps':len(overlaps),'bounds_godot':[[lo[0],lo[2],-hi[1]],[hi[0],hi[2],-lo[1]]],'failures':fails}
(R/'verification/geometry.json').write_text(json.dumps(report,indent=2));print('GEOMETRY',json.dumps(report));assert not fails
