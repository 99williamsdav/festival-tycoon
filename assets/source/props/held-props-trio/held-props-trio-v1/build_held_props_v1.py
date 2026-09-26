import bpy, math, json, random
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
s=bpy.context.scene;s.unit_settings.system='METRIC';s.unit_settings.scale_length=1
def rgba(h,a=1):
    c=[int(h[i:i+2],16)/255 for i in (0,2,4)]
    return tuple(v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in c)+(a,)
def mat(n,h,a=1):
    m=bpy.data.materials.new(n);m.use_nodes=True;m.diffuse_color=rgba(h,a)
    bs=m.node_tree.nodes['Principled BSDF'];bs.inputs['Base Color'].default_value=rgba(h,a);bs.inputs['Alpha'].default_value=a;bs.inputs['Roughness'].default_value=.85
    if a<1:m.blend_method='BLEND';m.use_backface_culling=True;bs.inputs['Roughness'].default_value=.4
    return m
kraft=mat('Kraft cardboard','AF8960');kraftedge=mat('Cardboard folded edge','C6A77B')
gold=mat('Chips golden','D8AA52');pale=mat('Chips pale','E4C277');toast=mat('Chips toasted end','AD7839')
coral=mat('Soft drink muted coral','BA604F');cream=mat('Cream cup stripe and lid','E3D8BC');sage=mat('Sage straw','687B59')
clear=mat('Clear cup alpha blend','D3E5DF',.22);rim=mat('Clear cup rim','D3E5DF',.65)
amber=mat('Beer amber','BC873B');foam=mat('Pale beer foam','E8DEBB')
groups={};current=None
filenames={'ChipsTrayV1':'lwf_chips_tray_v1.glb','SoftDrinkCupV1':'lwf_soft_drink_cup_v1.glb','BeerCupV1':'lwf_beer_cup_v1.glb'}
def mesh(n,v,f,m):
    me=bpy.data.meshes.new(n);me.from_pydata(v,[],f);me.materials.append(m);o=bpy.data.objects.new(n,me);s.collection.objects.link(o)
    if current:groups[current].append(o)
    return o
def box(n,p,d,m,rz=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=bpy.context.object;o.name=n;o.dimensions=d;o.rotation_euler.z=rz;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(m)
    if current:groups[current].append(o)
    return o
def frustum(n,r0,r1,z0,z1,m,caps=True,sides=12):
    v=[(r*math.cos(2*math.pi*i/sides),r*math.sin(2*math.pi*i/sides),z) for z,r in ((z0,r0),(z1,r1)) for i in range(sides)]
    f=[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    if caps:f += [tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
    return mesh(n,v,f,m)
def ring(n,r,t,z,h,m,sides=12):
    v=[(rad*math.cos(2*math.pi*i/sides),rad*math.sin(2*math.pi*i/sides),zz) for zz,rad in ((z-h/2,r),(z+h/2,r),(z+h/2,r-t),(z-h/2,r-t)) for i in range(sides)]
    f=[]
    for k in range(4):
        for i in range(sides):f.append((k*sides+i,k*sides+(i+1)%sides,((k+1)%4)*sides+(i+1)%sides,((k+1)%4)*sides+i))
    return mesh(n,v,f,m)
def finish(key):
    obs=groups[key];bpy.ops.object.select_all(action='DESELECT')
    for o in obs:o.select_set(True)
    bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join();o=bpy.context.object;o.name='LWF_'+key
    s.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR');bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    o['origin_contract']='presentation grip centre; metres; Blender Z up / Godot Y up'
    o.data.calc_loop_triangles();lo=[min(v.co[i] for v in o.data.vertices) for i in range(3)];hi=[max(v.co[i] for v in o.data.vertices) for i in range(3)]
    bpy.ops.export_scene.gltf(filepath=str(ROOT/filenames[key]),export_format='GLB',use_selection=True,export_yup=True,export_apply=True,export_animations=False)
    info={'file':filenames[key],'triangles':len(o.data.loop_triangles),'bounds_blender':[lo,hi],'dimensions_xyz_m':[hi[i]-lo[i] for i in range(3)],'mesh_nodes':1,'materials':len(o.data.materials)}
    o.hide_render=True;return o,info
assets=[];tech={}
current='ChipsTrayV1';groups[current]=[]
# Hollow sloping paper tray: 180 x 120 x 50 mm; grip at tray-volume centre.
box('Tray base',(0,0,-.023),(.150,.090,.004),kraft)
v=[(-.075,-.045,-.025),(.075,-.045,-.025),(.075,.045,-.025),(-.075,.045,-.025),(-.09,-.06,.025),(.09,-.06,.025),(.09,.06,.025),(-.09,.06,.025)]
tray=mesh('Sloped cardboard walls',v,[(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],kraft);tray.data.materials[0].use_backface_culling=False
for y in (-.060,.060):box('Folded long rim',(0,y,.024),(.180,.003,.003),kraftedge)
for x in (-.090,.090):box('Folded short rim',(x,0,.024),(.003,.120,.003),kraftedge)
rng=random.Random(17)
for i in range(10):
    x=-.058+(i%5)*.028;y=-.022+(i//5)*.041;z=.014+(i%3)*.006
    o=box('Chunky chip',(x,y,z),(.013,.068,.013),gold if i%3 else pale,rng.uniform(-.28,.28))
    o.rotation_euler.x=rng.uniform(-.1,.1)
    # Sparse toasted end cue, not fine photoreal detail.
    if i in (1,6):box('Toasted chip end',(x,y+.03,z+.001),(.013,.008,.013),toast)
o,t=finish(current);assets.append(o);tech[current]=t
current='SoftDrinkCupV1';groups[current]=[]
# Cup body 90 mm diameter x 150 mm high; origin at body mid-height.
frustum('Lower paper cup',.033,.0386,-.075,-.012,coral)
frustum('Broad cream stripe',.0386,.0413,-.012,.022,cream)
frustum('Upper paper cup',.0413,.045,.022,.075,coral)
frustum('Cream lid',.046,.047,.075,.081,cream)
frustum('Raised lid centre',.042,.040,.081,.085,cream)
straw=frustum('Short sage straw',.003,.003,.084,.142,sage,sides=6);straw.location.x=.009
o,t=finish(current);assets.append(o);tech[current]=t
current='BeerCupV1';groups[current]=[]
frustum('Thin clear cup wall',.033,.045,-.075,.075,clear,caps=False)
frustum('Clear cup bottom',.033,.033,-.075,-.073,clear)
ring('Rolled clear rim',.046,.003,.075,.004,rim)
frustum('Amber beer',.031,.0415,-.071,.047,amber)
frustum('Pale foam cap',.0415,.043,.047,.061,foam)
o,t=finish(current);assets.append(o);tech[current]=t
current=None
review=[];lineup=[];adultctx=[]
def copyprop(o,p,n):
    q=o.copy();q.data=o.data;q.name='REVIEW_'+n;s.collection.objects.link(q);q.location=p;q.hide_render=False;review.append(q);return q
for i,o in enumerate(assets):lineup.append(copyprop(o,((i-1)*.28,0,.08),'Lineup'+str(i)))
def text(n,txt,p,size=.023):
    bpy.ops.object.text_add(location=p);o=bpy.context.object;o.name='REVIEW_'+n;o.data.body=txt;o.data.size=size;o.data.align_x='CENTER';o.rotation_euler=(math.pi/2,0,math.pi);o.data.materials.append(mat('Label '+n,'E3D8BC'));review.append(o);return o
# Existing adult model, unchanged. Props touch the neutral hand for size review;
# no pretend verified hand socket, no baked pose/arms in exports.
for i,prop in enumerate(assets):
    x=(i-1)*1.2;before=set(s.objects);bpy.ops.import_scene.gltf(filepath='C:/Projects/festival-tycoon/assets/runtime/characters/lwf_generic_attendee_v1.glb')
    added=[o for o in s.objects if o not in before]
    for o in added:
        if o.parent not in added:o.matrix_world=Matrix.Translation(Vector((x,0,0)))@o.matrix_world
        o.name='REVIEW_'+o.name
    review.extend(added);adultctx.extend(added)
    if i==0:loc=(x+.48,.04,1.05)
    else:loc=(x+.43,.04,.94)
    adultctx.append(copyprop(prop,loc,'AdultProp'+str(i)))
    adultctx.append(text(str(i),['CHIPS','SOFT DRINK','BEER'][i],(x,.04,1.94),.075))
ground=box('REVIEW_Ground',(0,0,-.035),(60,60,.05),mat('Review warm green','8B995E'));review.append(ground)
s.render.engine='BLENDER_EEVEE';s.render.resolution_x=1600;s.render.resolution_y=1000;s.render.resolution_percentage=100
s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'
s.world=bpy.data.worlds.new('Review daylight');s.world.color=(.34,.38,.34)
bpy.ops.object.light_add(type='SUN',location=(0,0,8));bpy.context.object.rotation_euler=(.52,-.4,-.44);bpy.context.object.data.energy=2.1
bpy.ops.object.light_add(type='AREA',location=(0,4,6));bpy.context.object.data.energy=700;bpy.context.object.data.size=6
bpy.ops.object.camera_add();cam=bpy.context.object;cam.data.type='ORTHO';s.camera=cam
def render(file,active,target,width,yaw=160,elev=35,res=(1600,1000)):
    for o in review:o.hide_render=o not in active and o!=ground
    t=Vector(target);a=math.radians(yaw);cam.location=t+Vector((10*math.sin(a),-10*math.cos(a),10*math.tan(math.radians(elev))))
    cam.rotation_euler=(t-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=width;s.render.resolution_x,s.render.resolution_y=res;s.render.filepath=str(ROOT/file);bpy.ops.render.render(write_still=True)
render('01-held-props-trio.png',lineup,(0,0,.08),.96,175,30)
render('02-adult-hand-scale.png',adultctx,(.1,0,1.0),4.6,165,15)
render('03-game-camera-32m.png',adultctx,(0,0,.6),32,155,math.degrees(math.atan2(58,72)),(1920,1080))
for o in review:o.hide_render=True
for o in assets:o.hide_render=False
bpy.ops.object.select_all(action='DESELECT')
for o in assets:o.select_set(True)
bpy.context.view_layer.objects.active=assets[0]
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'lwf_held_props_trio_v1.blend'))
tech={'status':'ONE trio checkpoint; exact dual approval pending; no integration','units':'metres','assets':tech,'origin':'cup body mid-height; chips tray-volume centre. Grip centre proposals only; no adult hand socket verified','review_context':'unchanged 1.75m adult with neutral arms; props tangent to hands, not a holding animation','beer_material':'alpha-blended single-sided clear shell, opaque amber/foam; no transmission dependency; Godot sorting unverified','provenance':'original Blender procedural mesh and flat materials; no external meshes, images or textures'}
(ROOT/'technical.json').write_text(json.dumps(tech,indent=2));print(json.dumps(tech))
