"""Verify delivered GLBs, their bounds, topology and low-trunk clearance."""
import bpy, json, math
from pathlib import Path
ROOT=Path(__file__).resolve().parent
manifest=json.loads((ROOT/'manifest.json').read_text()); report={}
for name,data in manifest.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(ROOT/'runtime'/f'{name}.glb'))
    meshes=[obj for obj in bpy.data.objects if obj.type=='MESH']
    assert sorted(obj.name for obj in meshes)==['Crown','Trunk']
    assert not bpy.data.actions and not bpy.data.armatures
    trunk=next(obj for obj in meshes if obj.name=='Trunk'); crown=next(obj for obj in meshes if obj.name=='Crown')
    trunk_points=[trunk.matrix_world@v.co for v in trunk.data.vertices]
    height=data['blenderBounds'][1][2]
    # Imported glTF is converted back to Blender Z up by Blender's importer.
    low=[p for p in trunk_points if p.z<height*.2]
    radius=max(math.hypot(p.x,p.y) for p in low)
    assert radius<=data['blockedTrunkRadius']+.001, radius
    # UV seams split export vertices; weld positions to measure actual connected foliage skin.
    positions={}; adjacency={}
    for vertex in crown.data.vertices:
        key=tuple(round(c,4) for c in vertex.co); positions[vertex.index]=key; adjacency.setdefault(key,set())
    for edge in crown.data.edges:
        a,b=[positions[i] for i in edge.vertices]; adjacency[a].add(b); adjacency[b].add(a)
    pending=set(adjacency); sizes=[]
    while pending:
        stack=[pending.pop()]; size=0
        while stack:
            point=stack.pop(); size+=1
            for neighbour in adjacency[point]:
                if neighbour in pending: pending.remove(neighbour); stack.append(neighbour)
        sizes.append(size)
    sizes.sort(reverse=True)
    assert len(sizes)==(10 if 'apple' in name else 1), sizes
    assert sizes[0]>500
    triangles=sum(len(p.vertices)-2 for obj in meshes for p in obj.data.polygons)
    assert triangles==data['triangles']
    assert all(len(obj.data.materials)==1 for obj in meshes)
    all_points=[obj.matrix_world@v.co for obj in meshes for v in obj.data.vertices]
    bounds=[[min(p[i] for p in all_points) for i in range(3)],[max(p[i] for p in all_points) for i in range(3)]]
    assert all(abs(bounds[j][i]-data['blenderBounds'][j][i])<.001 for j in range(2) for i in range(3))
    report[name]={'passed':True,'triangles':triangles,'lowTrunkRadius':radius,
                  'importedBounds':bounds,
                  'components':len(sizes),'connectedFoliageSkin':True,'fruitComponents':len(sizes)-1,
                  'meshNodes':sorted(obj.name for obj in meshes),'animations':0,'skeletons':0}
(ROOT/'verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
