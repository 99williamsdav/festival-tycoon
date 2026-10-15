import bpy,os,json
p=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(p,'coffee-van-server-review.blend'))
rig=next(o for o in bpy.data.collections['REVIEW ONLY actual game food server'].objects if o.type=='ARMATURE')
for n in ['RightUpperArm','RightLowerArm','RightHand']:
 b=rig.pose.bones.get(n)
 if b:print('BONE',n,'head',list(rig.matrix_world@b.head),'length',b.length,flush=True)
for o in bpy.data.collections['REVIEW ONLY actual game food server'].objects:
 if o.type=='MESH':
  e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();v=[e.matrix_world@p.co for p in m.vertices]
  print('MESH',o.name,len(v),o.hide_render,'bounds',[[min(p[i] for p in v) for i in range(3)],[max(p[i] for p in v) for i in range(3)]],flush=True);e.to_mesh_clear()
