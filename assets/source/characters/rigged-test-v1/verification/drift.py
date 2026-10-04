import bpy, sys
from mathutils.bvhtree import BVHTree
rig = bpy.data.objects['LWF_Attendee_Rig']; body = bpy.data.objects['LWF_Attendee_Body']
gar = next(o for o in bpy.data.objects if o.name.endswith('_Garment'))
for t in rig.animation_data.nla_tracks: t.mute = True
names = {vg.index: vg.name for vg in gar.vertex_groups}
bn = {vg.index: vg.name for vg in body.vertex_groups}
armv = {v.index for v in body.data.vertices if sum(g.weight for g in v.groups if any(k in bn[g.group] for k in ('Arm', 'Hand'))) > 0.3}
polys = [list(pp.vertices) for pp in body.data.polygons if not any(i in armv for i in pp.vertices)]
def dists(dg):
    eb = body.evaluated_get(dg).to_mesh(); tree = BVHTree.FromPolygons([v.co.copy() for v in eb.vertices], polys)
    eg = gar.evaluated_get(dg).to_mesh(); out = [tree.find_nearest(v.co)[3] for v in eg.vertices]
    body.evaluated_get(dg).to_mesh_clear(); gar.evaluated_get(dg).to_mesh_clear(); return out
rig.animation_data.action = None; bpy.context.scene.frame_set(1); d0 = dists(bpy.context.evaluated_depsgraph_get())
worst = (0, None, None, None)
for act in ('walk', 'walk_hurry'):
    rig.animation_data.action = bpy.data.actions[act]
    for f in range(1, int(bpy.data.actions[act].frame_range[1]) + 1, 2):
        bpy.context.scene.frame_set(f); d = dists(bpy.context.evaluated_depsgraph_get())
        for i, (a, b) in enumerate(zip(d0, d)):
            if b - a > worst[0]: worst = (b - a, i, act, f)
dd, i, act, f = worst; v = gar.data.vertices[i]
print('DRIFT', gar.name, round(dd * 1000, 1), 'mm', act, f, tuple(round(x, 3) for x in v.co), [(names[g.group], round(g.weight, 2)) for g in v.groups])
