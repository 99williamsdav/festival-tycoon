import bpy, bmesh, math, os, json
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for d in bpy.data.materials: bpy.data.materials.remove(d)
def mat(n,c,metal=0,rough=.55):
 m=bpy.data.materials.new(n); m.diffuse_color=(*c,1); m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*c,1); p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
 return m
cream=mat('Warm cream enamel',(.78,.72,.59)); teal=mat('Deep teal enamel',(.045,.145,.16)); gold=mat('Muted brass',(.58,.40,.16),.65,.32); sage=mat('Sage cabinetry',(.25,.29,.17)); wood=mat('Warm oak',(.38,.21,.075)); rubber=mat('Rubber',(.024,.029,.032)); black=mat('Charcoal fittings',(.055,.06,.065)); glass=mat('Smoky cab glass',(.105,.14,.16),.25,.23); steel=mat('Brushed steel',(.5,.53,.53),.8,.27); lamp=mat('Headlight silver',(.72,.75,.72),.5,.18); amber=mat('Amber lamps',(.92,.38,.045)); red=mat('Rear red lenses',(.58,.035,.025)); coffee=mat('Coffee beans',(.10,.045,.017)); paper=mat('Paper cups',(.85,.80,.65)); dark=mat('Interior shadows',(.095,.105,.09))
ASSET=[]
def finish(o,n,m,bev=0):
 o.name=n; o.data.materials.append(m); ASSET.append(o)
 if bev:
  mod=o.modifiers.new('Soft manufactured edges','BEVEL'); mod.width=bev; mod.segments=2
  mod=o.modifiers.new('Weighted face normals','WEIGHTED_NORMAL')
 return o
def box(n,loc,dim,m,bev=.015):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.dimensions=dim; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); return finish(o,n,m,bev)
def mesh(n,verts,faces,m,bev=0):
 d=bpy.data.meshes.new(n); d.from_pydata(verts,[],faces); d.update(); o=bpy.data.objects.new(n,d); bpy.context.collection.objects.link(o); return finish(o,n,m,bev)
def cyl(n,loc,r,depth,m,axis='Z',verts=24):
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=depth,location=loc); o=bpy.context.object
 if axis=='Y': o.rotation_euler[0]=math.pi/2
 if axis=='X': o.rotation_euler[1]=math.pi/2
 return finish(o,n,m,.006)
def rod(n,a,b,r,m):
 a,b=Vector(a),Vector(b); o=cyl(n,(a+b)/2,r,(b-a).length,m,verts=12); o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler(); return o
def poly_side(n,pts,y,m,thick=.035):
 v=[(x,y-thick/2,z) for x,z in pts]+[(x,y+thick/2,z) for x,z in pts]; l=len(pts); f=[tuple(range(l-1,-1,-1)),tuple(range(l,2*l))]+[(i,(i+1)%l,(i+1)%l+l,i+l) for i in range(l)]; return mesh(n,v,f,m,.012)
def cut(o,loc,dim):
 c=box('temporary cutter',loc,dim,black,0); mod=o.modifiers.new('Cut opening','BOOLEAN'); mod.operation='DIFFERENCE'; mod.object=c; bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name); ASSET.remove(c); bpy.data.objects.remove(c,do_unlink=True)
def text(n,s,loc,size,m,rot):
 bpy.ops.object.text_add(location=loc,rotation=rot); o=bpy.context.object; o.data.body=s; o.data.size=size; o.data.extrude=.0006; o.data.align_x='CENTER'; return finish(o,n,m)
# Four metre-class, short-nose high-roof conversion; X runs front to rear.
box('Underbody chassis',(0,0,.52),(4.40,1.67,.18),black,.06)
box('Interior floor',(.65,0,.67),(3.20,1.83,.09),wood)
for y in [-.965,.965]:
 side=box('Lower body service' if y<0 else 'Lower body opposite',(0,y,1.015),(4.55,.10,.92),teal,.04)
 for x in [-1.50,1.52]:
  c=cyl('Wheelarch cutter',(x,y,.45),.505,.5,black,'Y',32); mod=side.modifiers.new('Wheel arch','BOOLEAN'); mod.operation='DIFFERENCE'; mod.object=c; bpy.context.view_layer.objects.active=side; bpy.ops.object.modifier_apply(modifier=mod.name); ASSET.remove(c); bpy.data.objects.remove(c,do_unlink=True)
 # Arch trim is an open band, not a full circular ring.
 for x in [-1.50,1.52]:
  v=[]
  for i in range(25):
   a=math.pi*i/24
   for r in [.505,.557]: v.append((x+r*math.cos(a),y+(-.058 if y<0 else .058),.45+r*math.sin(a)))
  mesh('Pressed wheel arch lip',v,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(24)],teal)
 box('Gold waist pinstripe',(0,y*1.006,1.47),(4.54,.025,.035),gold,.003)
 box('Lower sill',(0,y,.61),(2.03,.13,.065),teal,.016)
 # Front cab window, door panel and rear upper corners.
 poly_side('Cream cab door surround',[(-2.23,1.50),(-1.68,2.40),(-1.27,2.53),(-.68,2.53),(-.68,1.50)],y,cream,.075)
 poly_side('Black side window seal',[(-1.94,1.84),(-1.57,2.36),(-.84,2.36),(-.84,1.84)],y*1.047,black)
 poly_side('Cab side window',[(-1.87,1.88),(-1.53,2.31),(-.89,2.31),(-.89,1.88)],y*1.069,glass,.012)
 box('Door handle',(-.89,y*1.07,1.65),(.20,.05,.065),black,.025)
 rod('Door seam',(-.69,y*1.065,.71),(-.69,y*1.065,2.43),.006,dark)
 box('Cream rear corner',(2.17,y,2.04),(.22,.11,1.12),cream,.035)
 box('Hatch front upright',(-.57,y,2.02),(.18,.11,1.08),cream,.018)
 box('Upper roof rail',(.80,y,2.57),(2.78,.10,.14),cream,.035)
 if y>0: box('Closed opposite panel',(.79,y,2.02),(2.59,.09,.99),cream,.025)
 else: box('Cream apron below counter',(.80,y,1.54),(2.62,.11,.12),cream,.012)
# Chamfered roof with visible faceted shoulders.
sections=[(-1.65,2.54,.91),(-1.38,2.66,.96),(-.70,2.76,.96),(1.94,2.76,.96),(2.28,2.64,.91)]
v=[]
for x,z,w in sections: v.extend([(x,-w,z-.14),(x,-w*.87,z),(x,w*.87,z),(x,w,z-.14)])
f=[]
for j in range(len(sections)-1):
 for k in range(3): f.append((4*j+k,4*j+k+1,4*(j+1)+k+1,4*(j+1)+k))
f.extend([(0,1,2,3),(16,17,18,19)])
mesh('Faceted high roof',v,f,cream,.025)
for sy in [-1,1]:
 mesh('Continuous roof shoulder',[(x,sy*w,z-.14) for x,z,w in sections]+[(2.28,sy*.965,2.50),(-1.38,sy*.965,2.50),(-1.65,sy*.965,2.40)],[(0,1,2,3,4,5,6,7)],cream)
 mesh('Cab A pillar closure',[(-2.30,sy*.96,1.49),(-2.22,sy*.91,1.68),(-1.65,sy*.85,2.40),(-1.27,sy*.965,2.53),(-1.68,sy*.965,2.40),(-2.23,sy*.965,1.50)],[(0,1,2,3,4,5)],cream)
# Sloping cab screen and short bonnet.
mesh('Windshield surround',[(-2.22,-.91,1.68),(-2.22,.91,1.68),(-1.65,.85,2.40),(-1.65,-.85,2.40)],[(0,1,2,3)],black,.025)
mesh('Windshield glass',[(-2.226,-.84,1.73),(-2.226,.84,1.73),(-1.69,.79,2.34),(-1.69,-.79,2.34)],[(0,1,2,3)],glass)
mesh('Cream short bonnet',[(-2.30,-.96,1.49),(-2.30,.96,1.49),(-2.22,.91,1.69),(-2.22,-.91,1.69)],[(0,1,2,3)],cream)
box('Front face',(-2.29,0,1.08),(.105,1.92,.77),teal,.035)
box('Front gold stripe',(-2.35,0,1.465),(.018,1.92,.035),gold,.002)
box('Front black grille recess',(-2.354,0,1.12),(.026,.94,.42),black,.025)
for z in [.96,1.06,1.16,1.26]: box('Horizontal grille slat',(-2.376,0,z),(.038,.88,.028),rubber,.006)
for y in [-.73,.73]:
 box('Headlight bezel',(-2.36,y,1.13),(.06,.32,.43),black,.065)
 cyl('Round chrome headlamp',(-2.405,y,1.18),.139,.035,steel,'X',24)
 cyl('Headlamp lens',(-2.428,y,1.18),.117,.025,lamp,'X',24)
 box('Amber front indicator',(-2.40,y,.91),(.045,.20,.09),amber,.012)
 box('Wing mirror',(-1.73,y*1.54,1.90),(.10,.17,.28),black,.04)
 rod('Mirror mount',(-1.67,y*1.29,1.85),(-1.73,y*1.52,1.85),.022,black)
 rod('Wiper',(-2.245,y*.2,1.74),(-2.18,y,1.79),.013,rubber)
box('Front bumper',(-2.37,0,.69),(.20,2.02,.28),black,.055)
box('Front registration',(-2.48,0,.67),(.013,.44,.09),cream,.004)
box('Rear bumper',(2.35,0,.65),(.18,2.02,.20),black,.045)
for y in [-.48,.48]:
 box('Rear lower door',(2.285,y,1.12),(.08,.94,.88),teal,.02)
 box('Rear upper door',(2.285,y,2.04),(.08,.94,1.00),cream,.045)
 box('Rear pinstripe',(2.333,y,1.47),(.02,.94,.035),gold,.002)
for y in [-.91,.91]:
 box('Tail lamp housing',(2.35,y,1.33),(.07,.12,.54),black,.02)
 box('Tail lamp red',(2.39,y,1.43),(.025,.09,.29),red,.01)
 box('Tail indicator',(2.39,y,1.22),(.025,.09,.10),amber,.005)
 box('Reverse lamp',(2.39,y,1.12),(.025,.09,.08),paper,.005)
box('Rear door latch',(2.35,-.10,1.28),(.05,.08,.19),black,.012)
box('Rear registration',(2.405,0,.76),(.02,.42,.11),gold,.004)
# Wheels, steel rims, lug nuts and modest geometric tread.
for x in [-1.50,1.52]:
 for y in [-.98,.98]:
  cyl('Tyre',(x,y,.43),.43,.25,rubber,'Y',32)
  sy=-1 if y<0 else 1; face=y+sy*.132
  cyl('Cream steel wheel',(x,face,.43),.30,.035,cream,'Y',32)
  cyl('Steel hub',(x,face+sy*.025,.43),.15,.045,steel,'Y',24)
  cyl('Hub cap',(x,face+sy*.052,.43),.10,.022,black,'Y',20)
  for i in range(10):
   a=2*math.pi*i/10; cyl('Rim ventilation',(x+.245*math.cos(a),face+sy*.023,.43+.245*math.sin(a)),.026,.008,black,'Y',10)
  for i in range(5):
   a=2*math.pi*i/5; cyl('Wheel bolt',(x+.115*math.cos(a),face+sy*.055,.43+.115*math.sin(a)),.014,.012,steel,'Y',8)
  for i in range(32):
   a=2*math.pi*i/32; o=box('Tyre tread',(x+.423*math.sin(a),y,.43+.423*math.cos(a)),(.048,.24,.016),black,.004); o.rotation_euler[1]=a
# Open hatch, counter, clear interior aisle, cabinets against opposite wall.
box('Oak hatch header',(.77,-1.015,2.49),(2.63,.13,.10),wood)
box('Oak counter',(.77,-1.075,1.59),(2.85,.37,.085),wood,.022)
for x in [-.55,2.08]: box('Oak hatch jamb',(x,-1.01,2.04),(.065,.10,.86),wood,.008)
hatch=box('Open upward hatch',(.77,-1.53,2.68),(2.93,1.15,.065),cream,.025); hatch.rotation_euler[0]=math.radians(-9)
under=box('Sage hatch underside',(.77,-1.53,2.644),(2.80,1.06,.023),sage,.01); under.rotation_euler[0]=math.radians(-9)
for x in [-.48,2.01]:
 rod('Hatch support gas cylinder',(x,-1.04,2.02),(x,-1.61,2.64),.023,black)
 rod('Hatch support chrome piston',(x,-1.35,2.36),(x,-1.92,2.68),.012,steel)
 cyl('Hatch hinge',(x,-.99,2.56),.04,.13,steel,'X',12)
box('Interior opposite backing',(.78,.885,1.96),(2.64,.045,1.10),cream)
box('Oak backsplash',(.78,.855,1.66),(2.63,.03,.40),wood)
box('Rear interior wall',(2.16,0,1.98),(.045,1.80,1.10),cream)
box('Back worktop',(.77,.59,1.38),(2.66,.56,.065),wood)
for x in [-.25,.38,1.01,1.64]:
 box('Sage base cabinet',(x,.63,1.04),(.60,.46,.64),sage,.012)
 box('Cabinet inset door',(x,.389,1.03),(.52,.018,.51),sage,.008)
 box('Brass cabinet pull',(x,.365,1.25),(.13,.035,.025),gold,.008)
box('Overhead cupboard',(.68,.66,2.27),(.48,.40,.43),sage,.014)
box('Cupboard pull',(.82,.44,2.19),(.025,.025,.08),gold,.007)
# Equipment shelf is confined to the cab end, not across the standing aisle.
box('Coffee equipment shelf',(-.23,-.58,1.56),(.72,.67,.065),wood)
box('Espresso machine body',(-.32,-.66,1.87),(.50,.41,.49),steel,.035)
box('Espresso dark front',(-.32,-.884,1.85),(.43,.025,.24),black,.009)
box('Machine drip tray',(-.32,-.94,1.65),(.52,.22,.045),steel,.008)
box('Machine control fascia',(-.32,-.90,2.04),(.50,.035,.105),steel,.01)
for x in [-.47,-.20]:
 cyl('Espresso dial',(x,-.927,2.04),.03,.015,black,'Y',16)
 rod('Group head',(x,-.91,1.92),(x,-.91,1.85),.035,steel)
 rod('Portafilter handle',(x,-.91,1.88),(x,-1.025,1.88),.015,black)
box('Grinder pedestal',(.12,-.63,1.72),(.19,.27,.22),black,.02)
cyl('Grinder motor',(.12,-.63,1.91),.10,.27,steel,verts=16)
cyl('Bean hopper',(.12,-.63,2.12),.125,.20,coffee,verts=16)
cyl('Hopper lid',(.12,-.63,2.23),.13,.025,black,verts=16)
box('Grinder spout',(.12,-.77,1.91),(.07,.13,.07),steel,.008)
def cup(x,y,z):
 bpy.ops.mesh.primitive_cone_add(vertices=16,radius1=.034,radius2=.047,depth=.085,location=(x,y,z)); return finish(bpy.context.object,'Paper coffee cup',paper,.003)
for x in [-.47,-.31,-.15]:
 for i in range(4): cup(x,-.65,2.17+i*.027)
# Small sink in back counter and swan-neck tap.
box('Sink steel rim',(1.24,.58,1.419),(.36,.30,.018),steel,.02)
box('Sink dark bowl',(1.24,.58,1.431),(.28,.22,.01),dark,.02)
rod('Tap upright',(1.37,.73,1.42),(1.37,.73,1.66),.014,steel)
rod('Tap neck',(1.37,.73,1.66),(1.37,.58,1.69),.014,steel)
rod('Tap outlet',(1.37,.58,1.69),(1.37,.58,1.62),.014,steel)
box('Menu oak frame',(1.73,-.66,2.06),(.53,.055,.79),wood,.012)
box('Menu black face',(1.73,-.695,2.06),(.47,.016,.73),black,.003)
text('Menu heading','COFFEE',(1.73,-.708,2.33),.062,paper,(math.pi/2,0,0))
for i,s in enumerate(['Espresso   2.50','Americano  2.80','Latte      3.00','Flat White 3.00','Cappuccino 3.00','Mocha      3.20']): text('Menu item',s,(1.73,-.709,2.23-i*.082),.030,paper,(math.pi/2,0,0))
# Simple bean and COFFEE door branding exactly as dominant reference.
bean=cyl('Gold bean emblem',(-1.02,-1.037,1.23),.102,.012,gold,'Y',24); bean.scale.x=.68; bean.rotation_euler[1]=-.30
rod('Bean crease',(-1.06,-1.047,1.15),(-.99,-1.047,1.31),.006,teal)
text('COFFEE side wordmark','C O F F E E',(-1.02,-1.043,1.04),.070,paper,(math.pi/2,0,0))
# Cab seats and dash, visible through opaque smoky glass only as silhouettes where relevant.
box('Cab dashboard',(-1.94,0,1.60),(.35,1.67,.12),black,.04)
for y in [-.49,.49]:
 box('Cab seat cushion',(-1.17,y,1.03),(.45,.43,.13),black,.04)
 box('Cab seat back',(-.94,y,1.38),(.13,.43,.61),black,.055)
# Small under-header practical lights, not baked tricks.
for x in [-.25,1.05]: cyl('Service downlight',(x,-.93,2.445),.055,.013,paper)
# Apply all modifiers and convert fonts before export for identical runtime geometry.
bpy.ops.object.select_all(action='DESELECT')
for o in ASSET: o.select_set(True)
bpy.context.view_layer.objects.active=ASSET[0]; bpy.ops.object.convert(target='MESH')
ASSET=list(bpy.context.selected_objects)
for o in ASSET:
 bm=bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001); bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001); bmesh.ops.triangulate(bm,faces=list(bm.faces)); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(o.data); bm.free()
 for p in o.data.polygons: p.use_smooth=False
 bm=bmesh.new(); bm.from_mesh(o.data); bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.calc_area()<1e-12],context='FACES'); bm.to_mesh(o.data); bm.free()
tri=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in ASSET)
materials=set(m.name for o in ASSET for m in o.data.materials)
stats={'dimensions_m':{'body_length':4.55,'width_without_mirrors':2.03,'closed_roof_height':2.76,'open_hatch_extent_y':-2.10},'triangles':tri,'mesh_objects':len(ASSET),'material_count':len(materials),'materials':sorted(materials),'textures':0,'server_aisle':{'clear_width_m':.84,'usable_length_m':1.48,'floor_z_m':.715,'suggested_feet_position_m':[.86,-.08,.715],'note':'Standing zone behind counter, between equipment shelf ending X .23 and menu end. Worktop front Y .31 and counter rear Y -.89; reserve central .84m unobstructed width. Roof interior headroom approximately 2m.'}}
with open(os.path.join(OUT,'asset-statistics.json'),'w') as f: json.dump(stats,f,indent=2)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'coffee-van.glb'),export_format='GLB',use_selection=True,export_apply=True)
# Studio is separate and excluded from GLB.
for o in ASSET: o.select_set(False)
stage=box('STUDIO ground',(0,0,-.05),(200,200,.10),mat('STUDIO warm cream',(.78,.75,.68)),0); ASSET.remove(stage)
world=bpy.context.scene.world; world.use_nodes=True; world.node_tree.nodes['Background'].inputs[0].default_value=(.78,.81,.84,1); world.node_tree.nodes['Background'].inputs[1].default_value=.45
def area(n,loc,power,size):
 bpy.ops.object.light_add(type='AREA',location=loc); o=bpy.context.object; o.name=n; o.data.energy=power; o.data.shape='DISK'; o.data.size=size; o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
area('STUDIO key',(-4,-5,7),1200,5); area('STUDIO fill',(2,4,6),850,5)
bpy.ops.object.camera_add(); cam=bpy.context.object; cam.name='STUDIO camera'; cam.data.type='ORTHO'; bpy.context.scene.camera=cam
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=40; scene.cycles.use_denoising=True
scene.render.resolution_x=1400; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'; scene.render.image_settings.file_format='PNG'
views={'main':(-5.7,-9.5,3.4),'rear-opposite':(6.5,7.5,3.5),'front':(-9,0,2.8),'rear':(9,0,2.8)}
def view(loc):
 cam.location=loc; cam.rotation_euler=(Vector((0,-.22,1.35))-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.ortho_scale=6.5
view(views['main']); bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'coffee-van.blend'))
for name,loc in views.items():
 view(loc); scene.render.filepath=os.path.join(OUT,'render-'+name+'.png'); bpy.ops.render.render(write_still=True)
# Game-scale four-angle sheet: renders at 480 px each, combined without altering pixels.
scene.render.resolution_x=480; scene.render.resolution_y=360
imgs=[]
for i,loc in enumerate([(-6,-8,5),(6,-8,5),(6,8,5),(-6,8,5)]):
 view(loc); path=os.path.join(OUT,'game-angle-'+str(i+1)+'.png'); scene.render.filepath=path; bpy.ops.render.render(write_still=True); imgs.append(path)
try:
 from PIL import Image
 sheet=Image.new('RGB',(960,720))
 for i,p in enumerate(imgs): sheet.paste(Image.open(p).convert('RGB'),((i%2)*480,(i//2)*360))
 sheet.save(os.path.join(OUT,'game-scale-four-angle.png'))
except ImportError:
 # Blender ships no Pillow on some systems: compositor combines actual renders.
 scene.use_nodes=True; nt=scene.node_tree; nt.nodes.clear(); base=nt.nodes.new('CompositorNodeRGB'); base.outputs[0].default_value=(.78,.75,.68,1)
 prev=base.outputs[0]
 for i,p in enumerate(imgs):
  im=nt.nodes.new('CompositorNodeImage'); im.image=bpy.data.images.load(p)
  tr=nt.nodes.new('CompositorNodeTransform'); tr.inputs['X'].default_value=(-240 if i%2==0 else 240); tr.inputs['Y'].default_value=(180 if i<2 else -180); nt.links.new(im.outputs['Image'],tr.inputs['Image'])
  over=nt.nodes.new('CompositorNodeAlphaOver'); nt.links.new(prev,over.inputs[1]); nt.links.new(tr.outputs[0],over.inputs[2]); prev=over.outputs[0]
 comp=nt.nodes.new('CompositorNodeComposite'); nt.links.new(prev,comp.inputs[0]); scene.render.resolution_x=960; scene.render.resolution_y=720; scene.render.filepath=os.path.join(OUT,'game-scale-four-angle.png'); bpy.ops.render.render(write_still=True)
print('ASSET_STATS',json.dumps(stats))
