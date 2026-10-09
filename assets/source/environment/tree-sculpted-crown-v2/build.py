"""Original seeded Sculpted Crown A trees. Blender 4.1; no third-party meshes."""
import bpy, bmesh, math, random, json, hashlib
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
PAL = ['46512F','586538','6C7C40','829348','98A653','AAB864',
       '554A39','6C5B44','827054','958163','A94630','C5643D']
REPORT = {}

def material():
    image=bpy.data.images.new('SculptedTreePalette',width=len(PAL)*8,height=8)
    image.colorspace_settings.name='sRGB'
    def linear(value): return value/12.92 if value<=.04045 else ((value+.055)/1.055)**2.4
    image.pixels=[c for y in range(8) for h in PAL for x in range(8)
                  for c in [*[linear(int(h[i:i+2],16)/255) for i in (0,2,4)],1]]
    image.filepath_raw=str(ROOT/'textures/sculpted_tree_palette.png')
    image.file_format='PNG'; image.save(); image.pack()
    mat=bpy.data.materials.new('SculptedTree_MattePalette'); mat.use_nodes=True
    bs=mat.node_tree.nodes['Principled BSDF']; bs.inputs['Roughness'].default_value=.95
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Closest'
    mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    return mat

def paint(obj, mat, slot):
    obj.data.materials.clear(); obj.data.materials.append(mat)
    uv=obj.data.uv_layers.new(name='PaletteUV')
    for poly in obj.data.polygons:
        poly.use_smooth=False
        index=slot(poly)
        for loop in poly.loop_indices: uv.data[loop].uv=((index+.5)/len(PAL),.5)

def tube(name, points, radii, sides=9):
    pts=[Vector(p) for p in points]; verts=[]
    for k,p in enumerate(pts):
        direction=(pts[min(k+1,len(pts)-1)]-pts[max(0,k-1)]).normalized()
        reference=Vector((1,0,0)) if abs(direction.x)<.9 else Vector((0,1,0))
        u=direction.cross(reference).normalized(); v=direction.cross(u)
        for j in range(sides):
            verts.append(tuple(p+radii[k]*(u*math.cos(j*math.tau/sides)+v*math.sin(j*math.tau/sides))))
    faces=[tuple(reversed(range(sides))),tuple((len(pts)-1)*sides+j for j in range(sides))]
    for k in range(len(pts)-1):
        for j in range(sides): faces.append((k*sides+j,k*sides+(j+1)%sides,(k+1)*sides+(j+1)%sides,(k+1)*sides+j))
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
    return obj

def join(objects, name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]; bpy.ops.object.join()
    obj=bpy.context.object; obj.name=name; return obj

def blob(center, scale, rng, subdiv=3):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv,radius=1,location=center)
    obj=bpy.context.object
    # Softly irregular volumes, not individual leaf shapes.
    for v in obj.data.vertices:
        d=v.co.normalized(); r=1+.035*math.sin(d.x*5+d.z*3)+.025*math.sin(d.y*6-d.z*4)
        v.co=Vector((v.co.x*scale[0]*r,v.co.y*scale[1]*r,v.co.z*scale[2]*r))
    return obj

def build(name, bounds, radius, seed, apple=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    mat=material(); rng=random.Random(seed)
    lo,hi=bounds; height=hi[2]; factor=height/10.326
    # All low trunk/root geometry remains within the original blocked trunk radius.
    wood=[tube('Trunk',[(0,0,0),(.03*factor,0,height*.13),(-.06*factor,.04*factor,height*.24),(.10*factor,.01*factor,height*.39),(.05*factor,0,height*.58)],
               [radius,radius*.9,radius*.78,radius*.58,radius*.32])]
    for j in range(4):
        angle=j*math.tau/4+.25
        wood.append(tube('Root',[(0,0,.2*factor),(radius*.65*math.cos(angle),radius*.65*math.sin(angle),.06*factor),
                     (radius*.95*math.cos(angle),radius*.95*math.sin(angle),.015)], [radius*.27,radius*.18,.008],6))
    for j in range(3):
        angle=j*math.tau/3+.3
        wood.append(tube('Limb',[(0,0,height*.28),(.30*factor*math.cos(angle),.30*factor*math.sin(angle),height*.40),
                     (1.3*factor*math.cos(angle),1.3*factor*math.sin(angle),height*.58)], [radius*.55,radius*.36,.06*factor],7))
    trunk=join(wood,'Trunk')
    for vertex in trunk.data.vertices:
        vertex.co.z=max(0,vertex.co.z)
        if vertex.co.z<height*.2:
            distance=math.hypot(vertex.co.x,vertex.co.y)
            if distance>radius: vertex.co.x*=radius/distance; vertex.co.y*=radius/distance
    paint(trunk,mat,lambda p: 6+min(3,int((p.normal.x*.5+.5)*3.8)))
    # Strong overlapping masses are fused into one watertight skin. Shallow lobes remain visible.
    crown_parts=[blob((.15,0,7.1),(2.7,2.8,2.0),rng)]
    for j in range(9):
        angle=j*math.tau/9+.19
        distance=rng.uniform(2.3,2.65)
        crown_parts.append(blob((math.cos(angle)*distance,math.sin(angle)*distance,6.4+rng.uniform(-.4,.65)),
                               (rng.uniform(1.65,1.95),rng.uniform(1.65,1.95),rng.uniform(1.4,1.8)),rng))
    for center,scale in [((-1.2,-.7,8.15),(2.0,2.0,1.7)),((1.35,.9,8.0),(2.0,2.0,1.65)),((-.3,1.8,7.5),(2.15,1.85,1.6))]:
        crown_parts.append(blob(center,scale,rng))
    crown=join(crown_parts,'Crown')
    bpy.ops.object.transform_apply(location=True,rotation=False,scale=True)
    remesh=crown.modifiers.new('Connected canopy','REMESH'); remesh.mode='VOXEL'; remesh.voxel_size=.18; remesh.use_smooth_shade=False
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    smooth=crown.modifiers.new('Shallow blended lobes','SMOOTH'); smooth.factor=.7; smooth.iterations=3
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    decimate=crown.modifiers.new('Broad facets','DECIMATE'); decimate.ratio=.075
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    # Match the established world-space crown envelope exactly; the existing placement stays valid.
    minimum=[min(v.co[i] for v in crown.data.vertices) for i in range(3)]
    maximum=[max(v.co[i] for v in crown.data.vertices) for i in range(3)]
    lower=height*(.40 if apple else .40)
    for v in crown.data.vertices:
        for i in range(3):
            target_lo=lo[i] if i<2 else lower; target_hi=hi[i]
            v.co[i]=target_lo+(v.co[i]-minimum[i])/(maximum[i]-minimum[i])*(target_hi-target_lo)
    bpy.context.view_layer.objects.active=crown; bpy.ops.object.transform_apply(location=True,rotation=False,scale=False)
    # The canopy reads through geometry/light, without jagged palette bands or leaf-like markings.
    paint(crown,mat,lambda p: 3)
    if apple:
        fruits=[]
        # Place sparse fruit on the actual fused crown surface, with no leaves or separate branches.
        for j in range(9):
            angle=j*math.tau/9+.2
            origin=Vector((math.cos(angle)*5,math.sin(angle)*5,height*(.48+.035*(j%4))))
            direction=Vector((-math.cos(angle),-math.sin(angle),0))
            hit,point,normal,index=crown.ray_cast(origin,direction)
            if hit:
                fruit=blob(point+normal*.045,(.11,.11,.12),rng,2)
                paint(fruit,mat,lambda p,j=j:10+j%2); fruits.append(fruit)
        crown=join([crown]+fruits,'Crown')
    objects=[trunk,crown]
    for obj in objects:
        bm=bmesh.new(); bm.from_mesh(obj.data); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(obj.data); bm.free()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=trunk
    path=ROOT/'runtime'/f'{name}.glb'
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source'/f'{name}.blend'))
    points=[obj.matrix_world@v.co for obj in objects for v in obj.data.vertices]
    REPORT[name]={'triangles':sum(len(p.vertices)-2 for obj in objects for p in obj.data.polygons),
      'blenderBounds':[[round(min(p[i] for p in points),4) for i in range(3)],[round(max(p[i] for p in points),4) for i in range(3)]],
      'nodes':[obj.name for obj in objects], 'blockedTrunkRadius':radius,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
      'concept':'A Sculpted Crown','individualLeaves':False,'connectedCanopy':True}

build('lwf_tree_oak_v2',([ -3.704,-3.653,0],[4.459,4.631,10.326]),.45,1010)
build('lwf_tree_old_apple_v2',([-1.637,-1.455,0],[1.6,1.484,4.831]),.18,3030,True)
(ROOT/'manifest.json').write_text(json.dumps(REPORT,indent=2))
