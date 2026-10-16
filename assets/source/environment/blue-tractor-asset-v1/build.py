import bpy, math, json, random, bmesh
from pathlib import Path
from mathutils import Vector
R=Path(__file__).absolute().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
bpy.context.scene.unit_settings.system='METRIC'
random.seed(17)
def linear(v):return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
def mat(name,h,rough=.8,metal=0,alpha=1):
 m=bpy.data.materials.new(name);m.use_nodes=True;c=tuple(linear(int(h[i:i+2],16)/255) for i in (0,2,4));m.diffuse_color=(*c,alpha);p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal;p.inputs['Alpha'].default_value=alpha
 if alpha<1:m.blend_method='BLEND';m.use_screen_refraction=False;m.use_backface_culling=True
 return m
M={k:mat('Tractor_'+k,*v) for k,v in {'Blue':('4177BC',.72,.1),'BlueLight':('5688C5',.78,.05),'BlueDark':('2C5282',.8,.1),'Cream':('DED3AF',.83,.05),'Rubber':('343631',.96,0),'Tread':('41433B',.96,0),'Iron':('454943',.8,.25),'Steel':('777A6D',.72,.35),'Glass':('91A8AA',.35,.05,.30),'Seat':('55574C',.95,0),'Lamp':('F3E6BE',.5,.05),'Amber':('C57A33',.6,0)}.items()}
parts={}
def register(o,n,ma,group):
 o.name=n;o.data.materials.append(M[ma]);parts.setdefault(group,[]).append(o);return o
def box(n,loc,dim,ma='Blue',bev=.025,group='Body',rot=None):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.dimensions=dim
 if rot:o.rotation_euler=rot
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bev:
  mod=o.modifiers.new('Soft manufactured edges','BEVEL');mod.width=bev;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
 return register(o,n,ma,group)
def cyl(n,loc,r,depth,ma='Iron',axis='Z',verts=16,group='Body'):
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=depth,location=loc,rotation=(0,math.pi/2,0) if axis=='X' else (math.pi/2,0,0) if axis=='Y' else (0,0,0));return register(bpy.context.object,n,ma,group)
def beam(n,a,b,width,ma='Iron',group='Cab'):
 v=Vector(b)-Vector(a);o=box(n,(Vector(a)+Vector(b))/2,(width,width,v.length),ma,.008,group);o.rotation_euler=v.to_track_quat('Z','Y').to_euler();return o
def mesh(n,vs,fs,ma,group):
 me=bpy.data.meshes.new(n);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(n,me);bpy.context.collection.objects.link(o);return register(o,n,ma,group)
# Bonnet forward is Blender -Y, exporting to Godot +Z.
box('Chassis',(0,-.1,.63),(.70,3.25,.30),'BlueDark',.045)
box('Engine',(0,-.58,.91),(.68,1.85,.53),'Iron',.06)
box('Bevelled blue bonnet',(0,-.87,1.32),(.91,1.91,.64),'Blue',.095)
box('Bonnet top',(0,-.87,1.65),(.76,1.73,.055),'BlueLight',.02)
box('Bonnet seam',(0,-.87,1.68),(.014,1.65,.006),'BlueDark',0)
box('Front cream grille surround',(0,-1.84,1.28),(.83,.085,.69),'Cream',.045)
box('Grille dark inset',(0,-1.893,1.35),(.67,.025,.46),'Iron',.012)
for i in range(7):box('Horizontal grille slat',(0,-1.916,1.15+i*.062),(.65,.025,.022),'Steel',.004)
for x in [-.245,.245]:box('Square headlamp',(x,-1.905,.985),(.18,.04,.145),'Lamp',.018)
box('Front weight mount',(0,-1.96,.63),(.62,.22,.18),'Iron',.015)
for x in [-.28,-.14,0,.14,.28]:box('Front ballast weight',(x,-2.04,.70),(.12,.30,.36),'Iron',.02)
for x in [-.466,.466]:
 for y in [-.85,-.66,-.47]:box('Bonnet vent',(x,y,1.40),(.015,.11,.038),'BlueDark',.004)
 box('Bonnet side lower seam',(x,-1.0,1.10),(.018,1.40,.025),'BlueDark',.004)
# Four tyres: low-poly shaped sidewalls with a broad V agricultural tread.
def wheel(name,x,y,r,width):
 g=name;z=r+.035;N=24
 profile=[(-width/2,.57*r),(-width/2,.83*r),(-width*.35,.97*r),(width*.35,.97*r),(width/2,.83*r),(width/2,.57*r)]
 vs=[(x+xx,y+rr*math.sin(i*2*math.pi/N),z+rr*math.cos(i*2*math.pi/N)) for xx,rr in profile for i in range(N)]
 fs=[]
 for j in range(len(profile)):
  k=(j+1)%len(profile)
  for i in range(N):fs.append((j*N+i,j*N+(i+1)%N,k*N+(i+1)%N,k*N+i))
 mesh(name+' tyre',vs,fs,'Rubber',g)
 for i in range(18):
  t=i*2*math.pi/18
  for side in [-1,1]:
   o=box(name+' diagonal lug',(x+side*width*.22,y+r*math.sin(t),z+r*math.cos(t)),(width*.58,.115 if r>.6 else .085,.07),'Tread',.015,g)
   from mathutils import Euler
   o.rotation_euler=(Euler((-t,0,0)).to_matrix()@Euler((0,0,side*.42)).to_matrix()).to_euler()
 for side in [-1,1]:
  cyl(name+' cream rim',(x+side*(width/2+.005),y,z),r*.57,.035,'Cream','X',24,g)
  cyl(name+' inner dish',(x+side*(width/2+.025),y,z),r*.40,.04,'Cream','X',20,g)
  cyl(name+' hub',(x+side*(width/2+.052),y,z),r*.18,.08,'Steel','X',12,g)
  for i in range(6):
   t=i*math.tau/6;cyl(name+' bolt',(x+side*(width/2+.05),y+r*.28*math.sin(t),z+r*.28*math.cos(t)),.026,.025,'Iron','X',6,g)
 return z
for side in [-1,1]:
 wheel('RearWheel_'+str(side),side*.82,.97,.76,.43)
 wheel('FrontWheel_'+str(side),side*.77,-1.23,.45,.30)
 cyl('Rear axle',(side*.55,.97,.795),.10,.60,'Iron','X')
 cyl('Front axle',(side*.45,-1.23,.485),.07,.65,'Iron','X')
# Rear mudguards follow upper wheel arcs; larger flat crown like approved B.
for side in [-1,1]:
 x=side*.82
 box('Rear blue mudguard crown',(x,.98,1.64),(.58,1.02,.09),'BlueLight',.035)
 for y,ang in [(.32,.48),(1.64,-.48)]:box('Rear mudguard slope',(x,y,1.51),(.58,.43,.08),'Blue',.025,rot=(ang,0,0))
 box('Rear fender inner wall',(side*.56,.94,1.37),(.06,1.17,.44),'Blue',.02)
 box('Front mudguard',(side*.77,-1.23,1.00),(.39,.65,.065),'Blue',.025)
 box('Front mudguard rear slope',(side*.77,-.84,.925),(.39,.20,.06),'Blue',.015,rot=(-.55,0,0))
 box('Rear amber lamp',(side*1.025,1.40,1.61),(.08,.16,.10),'Amber',.012)
# Closed cab trapezoid: cream roof, glazing, thin charcoal frames, visible seat.
box('Cab platform',(0,.64,1.02),(1.23,1.56,.16),'BlueDark',.045,'Cab')
corners=[(-.55,-.13,1.18),(.55,-.13,1.18),(.58,1.38,1.18),(-.58,1.38,1.18),(-.51,.07,2.55),(.51,.07,2.55),(.55,1.37,2.55),(-.55,1.37,2.55)]
for i,j in [(0,4),(1,5),(2,6),(3,7),(4,5),(5,6),(6,7),(7,4),(0,1),(1,2),(2,3),(3,0)]:beam('Cab frame',corners[i],corners[j],.065)
# Windows slightly inset, one outward-facing quad per surface to prevent layered alpha.
mesh('Windscreen',[(-.48,-.115,1.23),(.48,-.115,1.23),(.45,.075,2.49),(-.45,.075,2.49)],[(0,1,2,3)],'Glass','Windows')
mesh('Rear glazing',[(.51,1.37,1.24),(-.51,1.37,1.24),(-.49,1.365,2.49),(.49,1.365,2.49)],[(0,1,2,3)],'Glass','Windows')
for side in [-1,1]:
 vs=[(side*.55,-.06,1.24),(side*.58,1.31,1.24),(side*.55,1.31,2.49),(side*.51,.13,2.49)]
 mesh('Door glazing',vs,[(0,1,2,3) if side<0 else (3,2,1,0)],'Glass','Windows')
 beam('Door midrail',(side*.56,.70,1.22),(side*.53,.72,2.51),.045)
 box('Door handle',(side*.595,.49,1.69),(.08,.18,.065),'Iron',.014,'Cab')
 beam('Mirror stalk',(side*.54,.08,2.38),(side*.79,.0,2.36),.028)
 box('Mirror',(side*.81,.0,2.29),(.07,.15,.22),'Iron',.025,'Cab')
 box('Step1',(side*.68,.15,.38),(.30,.45,.065),'Iron',.015)
 box('Step2',(side*.68,.25,.61),(.30,.44,.065),'Iron',.015)
 box('Step3',(side*.65,.34,.83),(.24,.43,.06),'Iron',.015)
 for yy in [.0,.14,.28]:box('Step grip',(side*.68,yy,.416),(.22,.025,.012),'Steel',.003)
box('Cream cab roof',(0,.69,2.64),(1.41,1.77,.19),'Cream',.075,'Cab')
box('Roof panel',(0,.69,2.742),(1.08,1.34,.025),'Cream',.025,'Cab')
for x in [-.42,.42]:box('Cab working lamp',(x,-.218,2.52),(.17,.09,.09),'Lamp',.012,'Cab')
box('Seat cushion',(0,.94,1.46),(.46,.47,.14),'Seat',.06,'Cab')
box('Seat back',(0,1.14,1.70),(.47,.13,.46),'Seat',.055,'Cab',rot=(.10,0,0))
cyl('Seat pedestal',(0,.95,1.23),.10,.30,'Iron',group='Cab')
beam('Steering shaft',(0,.13,1.15),(0,.36,1.79),.035,'Iron','Cab')
bpy.ops.mesh.primitive_torus_add(major_segments=16,minor_segments=6,location=(0,.36,1.79),major_radius=.19,minor_radius=.018);o=bpy.context.object;o.rotation_euler.x=.35;register(o,'Steering wheel','Iron','Cab')
for x in [-.15,.15]:beam('Steering spoke',(0,.36,1.79),(x,.36,1.79),.018,'Iron','Cab')
box('Dashboard',(0,.04,1.56),(.57,.24,.20),'Iron',.04,'Cab')
# Exhaust and rear linkage are static, no tow attachment or rig.
cyl('Exhaust foot',(-.43,-.43,1.46),.065,.33,'Iron')
cyl('Exhaust silencer',(-.43,-.43,1.87),.075,.56,'Iron')
cyl('Exhaust pipe',(-.43,-.43,2.30),.041,.36,'Iron')
beam('Exhaust tip',(-.43,-.43,2.45),(-.43,-.50,2.53),.065,'Iron','Body')
for side in [-1,1]:beam('Rear link',(side*.25,1.15,.60),(side*.31,1.78,.37),.06,'Iron','Body')
box('Rear hitch',(0,1.76,.44),(.22,.20,.12),'Steel',.015)
# Restrained broad wear accents: palette patches, not dense scratches.
for side in [-1,1]:
 for y,z in [(-1.46,1.56),(-.68,1.59)]:box('Paint worn edge',(side*.43,y,z),(.009,.10,.018),'Steel',.002)
 box('Fender scuff',(side*.92,.60,1.689),(.12,.035,.002),'Steel',0)
for o in list(bpy.data.objects):
 if o.type=='MESH':
  bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True);o.select_set(False)
# Join by semantic group, retain editable components inside each mesh.
for group,objects in parts.items():
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects:o.select_set(True)
 bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();o=bpy.context.object;o.name='Tractor_'+group
 bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.000001);bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=0.000001);bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.calc_area()<1e-10],context='FACES');bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
 # Wheels have small angled tread overhang. Ground each wheel mesh exactly.
 if 'Wheel' in group:
  low=min(v.co.z for v in o.data.vertices)
  for v in o.data.vertices:v.co.z-=low
root=bpy.data.objects.new('BlueTractor_B',None);bpy.context.collection.objects.link(root)
for o in list(bpy.context.scene.objects):
 if o.type=='MESH':o.parent=root
root['front']='Godot +Z / Blender -Y';root['purpose']='Static ordinary-barn storage prop; remove upon conversion'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.wm.save_as_mainfile(filepath=str(R/'source/blue_tractor_B_v1.blend'))
bpy.ops.export_scene.gltf(filepath=str(R/'runtime/blue_tractor_B_v1.glb'),export_format='GLB',use_selection=True,export_extras=False,export_yup=True,export_animations=False,export_cameras=False,export_lights=False)
palette={k:list(v.diffuse_color) for k,v in M.items()}
(R/'source/materials-linear-rgba.json').write_text(json.dumps(palette,indent=2))
print('TRACTOR BUILD COMPLETE')


