"""Reproduce production assets using Blender 4.1: blender -b --python build.py.
Only writes beside this script. Reference GLBs are read-only dependencies.
"""
import bpy,math,json,sys,struct,zlib
from pathlib import Path
from mathutils import Vector as V
R=Path(__file__).resolve().parent
REF=R/'references'
for p in ['source','runtime','renders','verification']: (R/p).mkdir(exist_ok=True)
PALETTE={'cream':'F4E8BF','blue':'4799B9','blueDark':'245E75','timber':'CD9250','timberLight':'DEA95D','wood':'513C2C','terra':'AA5038','terraLight':'BE6244','plum':'643F59','plumLight':'75516B','plumDark':'493443','mustard':'E4B944','steel':'B5B5A5','metal':'555A55','black':'272D2B','rubber':'303431','white':'F4EDCE','red':'BF4235','orange':'E4A13C','glass':'729D9D','inside':'686B5C'}
M={};ASSET=[]
def mat(k):
 if k in M:return M[k]
 h=PALETTE[k];rgb=[int(h[i:i+2],16)/255 for i in (0,2,4)]
 m=bpy.data.materials.new('FT2026_'+k);m.diffuse_color=(*rgb,1);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*rgb,1);bs.inputs['Roughness'].default_value=.88
 M[k]=m;return m
def track(o,k):
 o.data.materials.append(mat(k));ASSET.append(o);return o
def mesh(n,vs,fs,k):
 me=bpy.data.meshes.new(n);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(n,me);bpy.context.collection.objects.link(o);return track(o,k)
def box(n,c,d,k,bev=0):
 bpy.ops.mesh.primitive_cube_add(size=1,location=c);o=bpy.context.object;o.name=n;o.dimensions=d;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);track(o,k)
 if bev:
  mod=o.modifiers.new('Small crafted edge','BEVEL');mod.width=bev;mod.segments=1
  bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
 return o
def cyl(n,c,r,depth,k,axis='Z',sides=12):
 bpy.ops.mesh.primitive_cylinder_add(vertices=sides,radius=r,depth=depth,location=c);o=bpy.context.object;o.name=n
 if axis=='Y':o.rotation_euler.x=math.pi/2
 if axis=='X':o.rotation_euler.y=math.pi/2
 return track(o,k)
def slab(n,poly,y,depth,k):
 vs=[(x,y-depth/2,z) for x,z in poly]+[(x,y+depth/2,z) for x,z in poly];l=len(poly)
 fs=[tuple(reversed(range(l))),tuple(range(l,2*l))]+[(i,(i+1)%l,(i+1)%l+l,i+l) for i in range(l)]
 return mesh(n,vs,fs,k)
def span(n,a,b,w,k):
 d=V(b)-V(a);o=box(n,(V(a)+V(b))/2,(w,w,d.length),k);o.rotation_euler=d.to_track_quat('Z','Y').to_euler();return o
def import_art(key,loc,scale=1):
 before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(REF/(key+'.glb')))
 obs=list(set(bpy.data.objects)-before)
 for o in obs:
  o.location=V(loc)+o.location*scale;o.scale*=scale
  if o.type=='MESH':
   ASSET.append(o);o['reference_asset']=key
   if o.data.uv_layers:
    # Join preserves per-loop coordinates only when layer names match.
    o.data.uv_layers.active.name='ProductionPaletteUV';o.data.uv_layers.active.active_render=True
   for m in o.data.materials:
    if m and m.use_nodes:
     for node in m.node_tree.nodes:
      if node.type=='TEX_IMAGE' and node.image:
       # Legacy GLBs use external PNGs; resolve explicitly, then embed.
       candidate=REF/(node.image.name.split('.')[0]+'.png')
       if candidate.exists():node.image=bpy.data.images.load(str(candidate),check_existing=True)
       node.image.pack()
 return obs
def roof_height(x,y):
 # Rounded cap edge, with small bevels at both ends. Real hollow shell.
 a=abs(y);return 2.75-(max(0,a-.78)/.32)**1.5*.10-(max(0,.45-x)/.45)**1.4*.36-(max(0,x-3.75)/.45)**1.4*.36
def rounded_roof(k):
 xs=[0,.06,.17,.30,.45,1,2.1,3.2,3.75,3.9,4.03,4.14,4.2];ys=[-1.1,-1.04,-.94,-.78,0,.78,.94,1.04,1.1]
 vs=[(x,y,roof_height(x,y)) for x in xs for y in ys];n=len(ys);fs=[]
 for i in range(len(xs)-1):
  for j in range(n-1):fs.append((i*n+j,(i+1)*n+j,(i+1)*n+j+1,i*n+j+1))
 o=mesh('Curved hollow roof shell',vs,fs,k);sol=o.modifiers.new('Roof thickness .075m','SOLIDIFY');sol.thickness=.075;bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=sol.name)
def shell(kind):
 primary='cream' if kind=='chips' else 'plum';base='blue' if kind=='chips' else 'plum'
 box('Chassis', (2.1,0,.35),(4.3,1.9,.16),'black')
 box('Interior floor',(2.1,0,.44),(4.2,2.16,.06),'inside')
 box('Public lower body',(2.1,-1.09,.84),(4.2,.02,.74),base)
 # Exact open aperture, all geometry remains outside x.9..3.4 z1.21..2.23.
 for x,w in [(.45,.9),(3.8,.8)]:box('Hatch end panel',(x,-1.055,1.72),(w,.09,1.02),primary)
 top=[(x,roof_height(x,-1.1)-.02) for x in [4.2,4.14,4.03,3.9,3.75,3.2,2.1,1,.45,.30,.17,.06,0]]
 slab('Curved hatch lintel',[(0,2.23),(4.2,2.23)]+top,-1.055,.09,primary)
 box('Back lower wall',(2.1,1.055,.84),(4.2,.09,.74),base)
 slab('Curved back upper wall',[(0,1.21),(4.2,1.21)]+top,1.055,.09,primary)
 for x in [0,4.2]:
  ys=[-1.1,-1.04,-.94,-.78,0,.78,.94,1.04,1.1]
  p=[(-1.1,1.21),(1.1,1.21)]+[(y,roof_height(x,y)-.02) for y in reversed(ys)]
  vs=[(xx,y,z) for xx in [x-.045,x+.045] for y,z in p];l=len(p)
  mesh('Enclosed shaped end wall',vs,[tuple(reversed(range(l))),tuple(range(l,2*l))]+[(i,(i+1)%l,(i+1)%l+l,i+l) for i in range(l)],primary)
  box('End lower colour wrap',(x,0,.84),(.09,2.2,.74),base)
 rounded_roof(primary)
 for y in [-1.107,1.107]:box('Waist trim',(2.1,y,1.23),(4.18,.024,.035),'steel' if kind=='chips' else 'mustard')
 if kind=='chips':
  # Pale wheel arch distinct against blue skirt.
  for y in [-1.17,1.17]:arch('Cream wheel arch',2.75,y,.46,.48,.58,'cream')
 else:
  for x in [.68,3.62]:box('Mustard sliding hatch shutter',(x,-1.125,1.72),(.40,.035,1.02),'mustard')
  for i in range(29):
   x=.055+i*4.09/28
   for y in [-1.107,1.107]:
    box('Pressed lower rib',(x,y,.83),(.022,.024,.68),'plumDark')
    if x<.88 or x>3.42:box('Pressed upper rib',(x,y,1.76),(.022,.024,.96),'plumLight')
  for x in [.34,.88,1.45,2.04,2.65,3.25,3.86]:
   pts=[(x,y,roof_height(x,y)+.008) for y in [-1.1,-1.04,-.94,-.78,0,.78,.94,1.04,1.1]]
   for a,b in zip(pts,pts[1:]):span('Roof pressed seam',a,b,.018,'plumDark')
def arch(n,x,y,z,r1,r2,k):
 N=10;vs=[]
 for yy in [y-.045,y+.045]:
  for r in [r1,r2]:
   vs.extend((x+r*math.cos(math.pi*i/N),yy,z+r*math.sin(math.pi*i/N)) for i in range(N+1))
 fs=[];stride=2*(N+1)
 for i in range(N):
  fs.extend([(i,i+1,N+2+i,N+1+i),(stride+i,stride+N+1+i,stride+N+2+i,stride+i+1),(N+1+i,N+2+i,stride+N+2+i,stride+N+1+i),(i,stride+i,stride+i+1,i+1)])
 fs.extend([(0,N+1,stride+N+1,stride),(N,stride+N,stride+2*N+1,2*N+1)])
 return mesh(n,vs,fs,k)
def running(kind):
 xs=[2.75] if kind!='curry' else [-.67,1.16,3.48]
 for x in xs:
  cyl('Axle',(x,0,.42),.065,2.33,'metal','Y')
  for y in [-1.16,1.16]:
   r=.43 if kind!='curry' else .36;cyl('12 sided tyre',(x,y,r),r,.19,'rubber','Y')
   cyl('Wheel hub',(x,y+(-.11 if y<0 else .11),r),r*.60,.045,'cream' if kind=='chips' else 'steel','Y')
   cyl('Hub cap',(x,y+(-.14 if y<0 else .14),r),r*.25,.028,'metal','Y',8)
   if kind=='curry':arch('Dark wheel guard',x,y,.36,.38,.46,'black')
 for x in [.3,3.93]:
  for y in [-.91,.91]:box('Stabiliser leg',(x,y,.25),(.08,.08,.45),'metal');box('Stabiliser foot',(x,y,.035),(.29,.24,.07),'black')
 if kind!='curry':
  for y in [-.58,.58]:span('A frame towbar',(-1.1,0,.32),(.12,y,.32),.10,'metal')
  box('Coupling',(-1.12,0,.32),(.27,.17,.13),'metal',.025);cyl('Hitch post',(-1.16,0,.37),.065,.23,'black');cyl('Jockey wheel',(-.7,0,.13),.10,.09,'rubber','Y')
def staff_door(kind):
 k='mustard' if kind=='curry' else 'cream'
 box('Staff door frame',(4.255,.2,1.38),(.055,.91,1.79),'metal')
 box('Staff door closed',(4.292,.2,1.38),(.035,.81,1.67),k,.012)
 box('Staff door handle',(4.322,-.1,1.38),(.035,.035,.13),'black',.005)
 for z in [.72,1.92]:box('Staff door hinge',(4.325,.56,z),(.035,.045,.10),'steel')
 box('Staff step',(4.41,.2,.30),(.38,1.01,.13),'black',.015)
 for y in [-.9,.88]:
  box('Rear lamp housing',(4.26,y,.80),(.065,.16,.25),'black')
  box('Rear red lamp',(4.299,y,.84),(.018,.11,.12),'red');box('Rear amber lamp',(4.30,y,.74),(.02,.11,.05),'orange')
 for x in [.23,3.98]:box('Side reflector',(x,-1.114,.65),(.14,.027,.065),'orange')
def counter(kind):
 k='steel' if kind in ['chips','curry'] else 'cream'
 box('Serving counter fixed top1.235',(2.15,-1.30,1.17),(2.72,.4,.13),k,.012)
 for x in [.86,3.44]:span('Counter bracket',(x,-1.10,.96),(x,-1.43,1.095),.045,'metal')
 for x in [.89,3.41]:box('Hatch jamb',(x,-1.106,1.72),(.035,.035,1.02),'steel' if kind=='chips' else ('wood' if kind=='pizza' else 'mustard'))
 # Kitchen stays behind server sweep, leaving clear working floor x.96..3.34 y-.99...35.
 box('Rear kitchen base',(2.10,.73,.82),(2.5,.55,.70),'inside')
 box('Rear kitchen top',(2.10,.73,1.185),(2.55,.60,.04),'steel')
 for x in [1.4,2.2,2.85]:box('Cupboard handle',(x,.431,.91),(.15,.02,.025),'black')
 if kind=='chips':
  box('Fryer enclosure',(1.13,.62,1.28),(.49,.53,.17),'metal');box('Fryer well',(1.13,.62,1.375),(.38,.41,.025),'black')
  for i in range(9):box('Fryer chips',(1.01+(i%3)*.10,.49+(i//3)*.09,1.42),(.035,.13,.06),'mustard')
  for i,k in enumerate(['red','white','orange']):
   cyl('Sauce bottle',(.98+i*.10,-1.29,1.36),.035,.23,k,sides=8);cyl('Sauce nozzle',(.98+i*.10,-1.29,1.495),.012,.05,k,sides=8)
 if kind=='pizza':
  box('Pizza oven',(3.55,.59,1.32),(.57,.7,.46),'terra',.06)
  # Dark arched mouth and warm interior, mounted toward public side.
  slab('Oven arch black',[(3.3,1.17),(3.3,1.34),(3.36,1.47),(3.51,1.53),(3.66,1.47),(3.72,1.34),(3.72,1.17)],.223,.025,'black')
  slab('Oven warm opening',[(3.35,1.18),(3.35,1.32),(3.4,1.4),(3.51,1.45),(3.62,1.4),(3.67,1.32),(3.67,1.18)],.203,.02,'orange')
 if kind=='curry':
  for x in [1.45,2.75]:
   box('Cooker',(x,.63,1.26),(.51,.47,.13),'metal');cyl('Curry pot',(x,.63,1.45),.19,.28,'steel');cyl('Pot lid',(x,.63,1.60),.20,.025,'metal');cyl('Pot lid knob',(x,.63,1.635),.035,.04,'black')
 for x in ([3.28] if kind!='curry' else [1.01,3.27]):
  for i in range(4):box('Paper tray stack',(x,-1.30,1.25+i*.025),(.22,.17,.019),'white')
def timber_shell():
 box('Chassis',(2.1,0,.35),(4.30,1.9,.16),'black');box('Interior floor',(2.1,0,.44),(4.2,2.16,.06),'inside')
 box('Terracotta lower service skirt',(2.1,-1.09,.84),(4.2,.02,.74),'terra')
 box('Terracotta back skirt',(2.1,1.055,.84),(4.2,.09,.74),'terra')
 for y in [-1.055,1.055]:
  for i in range(7):
   z=1.21+(i+.5)*.19
   if y>0:box('Back timber plank',(2.1,y,z),(4.15,.09,.182),'timberLight' if i%3==0 else 'timber')
   else:
    for x,w in [(.45,.9),(3.8,.8)]:box('Hatch timber plank',(x,y,z),(w,.09,.182),'timberLight' if i%3==0 else 'timber')
  if y<0:box('Lintel timber',(2.15,y,2.365),(2.5,.09,.27),'wood')
 for x in [0,4.2]:
  box('End lower red skirt',(x,0,.84),(.09,2.2,.74),'terra')
  for i in range(7):box('End timber plank',(x,0,1.21+(i+.5)*.19),(.09,2.2,.182),'timber' if i%3 else 'timberLight')
 for x in [.04,4.16]:
  for y in [-1.12,1.12]:box('Heavy corner post',(x,y,1.52),(.115,.11,2.1),'wood')
 # Shallow terracotta gable roof, ridge parallel X.
 poly=[(-1.20,2.52),(0,2.85),(1.20,2.52),(1.20,2.44),(0,2.76),(-1.20,2.44)]
 vs=[(x,y,z) for x in [-.12,4.32] for y,z in poly];l=len(poly);fs=[tuple(reversed(range(l))),tuple(range(l,2*l))]+[(i,(i+1)%l,(i+1)%l+l,i+l) for i in range(l)]
 mesh('Shallow pitched terracotta roof',vs,fs,'terra')
 for x in [i*.37 for i in range(12)]:
  for side in [-1,1]:span('Roof plank seam',(x,0,2.855),(x,side*1.19,2.529),.012,'terraLight')
 for side in [-1,1]:span('Roof shallow seam',(-.12,side*.6,2.69),(4.32,side*.6,2.69),.012,'terraLight')
 cyl('Pizza flue',(3.28,.35,3.06),.09,.50,'metal',sides=8);cyl('Flue rim',(3.28,.35,3.32),.13,.07,'black',sides=8)
def cab():
 # Cab consumes towbar zone. Nose -1.36, bumper -1.47, within reserved -1.60.
 profile=[(-1.36,.47),(.06,.47),(.06,2.10),(-.58,2.10),(-1.00,1.28),(-1.36,1.18)]
 slab('Cab body',profile,0,1.98,'plum')
 # Glass on both sides, door/window upright edge aligned with body bulkhead.
 window=[(-.88,1.32),(-.53,1.99),(-.07,1.99),(-.07,1.32)]
 for y in [-1.002,1.002]:
  slab('Cab window dark gasket',[(-.96,1.27),(-.59,2.045),(-.02,2.045),(-.02,1.27)],y,.016,'black')
  slab('Blue cab side glass',window,y+(-.012 if y<0 else .012),.01,'glass')
  box('Cab door handle',(-.16,y+(-.024 if y<0 else .024),1.13),(.11,.03,.05),'black')
  span('Cab rear door seam',(-.005,y,.59),(-.005,y,2.03),.017,'plumDark')
  span('Mirror stalk',(-.82,y,1.36),(-.92,y*1.12,1.46),.03,'black');box('Cab wing mirror',(-.92,y*1.14,1.46),(.08,.09,.20),'black',.01)
 # Sloping front windscreen on the cab nose plane.
 mesh('Cab windscreen',[(-1.006,-.86,1.31),(-1.006,.86,1.31),(-.614,.86,2.025),(-.614,-.86,2.025)],[(0,1,2,3)],'glass')
 box('Cab front bumper',(-1.415,0,.54),(.11,2.10,.21),'black',.015)
 for y in [-.71,.71]:box('Headlamp',(-1.371,y,.98),(.02,.22,.17),'cream')
 box('Front grille',(-1.373,0,.80),(.02,.65,.16),'black')
 for z in [.76,.81,.86]:box('Grille bar',(-1.388,0,z),(.015,.62,.015),'metal')
def vents(kind):
 if kind=='chips':
  box('Extractor base',(3.27,.42,2.83),(.28,.28,.23),'metal')
  for z in [2.77,2.83,2.89,2.95]:box('Extractor louvre',(3.27,.42,z),(.33,.33,.025),'steel')
 if kind=='curry':
  for name,x in [('L',.85),('R',3.36)]:
   cyl('Vent plinth',(x,.42,2.76),.18,.055,'black',sides=8);cyl('Vent stem',(x,.42,2.90),.105,.28,'steel',sides=8)
   bpy.ops.mesh.primitive_cone_add(vertices=8,radius1=.19,radius2=.12,depth=.20,location=(x,.42,3.09));o=bpy.context.object;o.name='Vent faceted cap';track(o,'steel')
   bpy.ops.mesh.primitive_cone_add(vertices=8,radius1=.12,radius2=0,depth=.10,location=(x,.42,3.24));track(bpy.context.object,'white')
   e=bpy.data.objects.new('LWF_FoodVan_SteamVent_'+name,None);bpy.context.collection.objects.link(e);e.location=(x,.42,3.30);ASSET.append(e)
def decoration(kind):
 art={'chips':'chip_block','pizza':'pizza','curry':'korma'}[kind]
 fascia='cream' if kind=='chips' else ('wood' if kind=='pizza' else 'mustard')
 box('Name fascia',(2.15,-1.225,2.40),(2.85,.09,.34),fascia,.025 if kind=='chips' else 0)
 import_art('lwf_food_van_name_panel_'+art+'_v1',(2.15,-1.274,2.40),1.04)
 import_art('lwf_food_van_sign_'+kind+'_v1',(2.10,0,2.75 if kind!='pizza' else 2.85),.59)
def bounds(obs):
 vs=[o.matrix_world@v.co for o in obs if o.type=='MESH' for v in o.data.vertices]
 return [[round(min(v[i] for v in vs),6) for i in range(3)],[round(max(v[i] for v in vs),6) for i in range(3)]]
def consolidate_palette():
 keys=list(PALETTE);w=len(keys)*8
 raw=b''.join(b'\0'+b''.join(bytes.fromhex(PALETTE[keys[x//8]]) for x in range(w)) for y in range(8))
 def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
 p=R/'source'/'production_palette.png';p.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,8,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))
 im=bpy.data.images.load(str(p),check_existing=True);im.pack();m=bpy.data.materials.new('FT2026_ProductionPalette');m.use_nodes=True;bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.88;t=m.node_tree.nodes.new('ShaderNodeTexImage');t.image=im;t.interpolation='Closest';m.node_tree.links.new(t.outputs['Color'],bs.inputs['Base Color'])
 for o in ASSET:
  if o.type!='MESH' or len(o.data.materials)!=1 or o.data.materials[0].name not in [x.name for x in M.values()]:continue
  key=next(k for k,v in M.items() if v==o.data.materials[0])
  while len(o.data.uv_layers):o.data.uv_layers.remove(o.data.uv_layers[0])
  uv=o.data.uv_layers.new(name='ProductionPaletteUV');uv.active_render=True
  for d in uv.data:d.uv=((keys.index(key)*8+4)/w,.5)
  o.data.materials.clear();o.data.materials.append(m)
def setup_render():
 s=bpy.context.scene;s.unit_settings.system='METRIC';s.render.engine='BLENDER_EEVEE';s.eevee.use_gtao=True;s.eevee.gtao_distance=3;s.eevee.gtao_factor=1.25;s.eevee.use_soft_shadows=True;s.render.resolution_x=1600;s.render.resolution_y=900;s.render.resolution_percentage=100;s.view_settings.view_transform='Standard';s.view_settings.look='Medium High Contrast';s.view_settings.exposure=0;s.view_settings.gamma=1
 s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast';s.world=bpy.data.worlds.new('Review soft sky');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.48,.56,.63,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.65
 bpy.ops.object.light_add(type='SUN',location=(-4,-7,10));sun=bpy.context.object;sun.name='REVIEW_Sun';sun.rotation_euler=(math.radians(25),math.radians(-25),math.radians(-30));sun.data.energy=2.2;sun.data.angle=.08
 bpy.ops.object.light_add(type='AREA',location=(1,-5,7));bpy.context.object.name='REVIEW_Fill';bpy.context.object.data.energy=400;bpy.context.object.data.size=7
 bpy.ops.mesh.primitive_plane_add(size=400,location=(0,0,-.007));g=bpy.context.object;g.name='REVIEW_Ground';gm=bpy.data.materials.new('REVIEW_Grass');gm.diffuse_color=(.30,.40,.16,1);g.data.materials.append(gm)
 bpy.ops.object.camera_add();c=bpy.context.object;c.name='REVIEW_Camera';c.data.type='ORTHO';s.camera=c;return s,c
def render(s,c,path,hscale=8.5,yaw=145,elev=38.853,target=(1.6,0,1.8)):
 # Blender horizontal ortho at landscape aspect; actual game vertical62 -> horizontal110.222.
 t=V(target);a=math.radians(yaw);e=math.radians(elev);distance=max(12,hscale*2);c.location=t+V((math.sin(a)*distance,-math.cos(a)*distance,math.tan(e)*distance));c.rotation_euler=(t-c.location).to_track_quat('-Z','Y').to_euler();c.data.clip_end=1000;c.data.ortho_scale=hscale;s.render.filepath=str(path);bpy.ops.render.render(write_still=True)
def main():
 manifest={'units':'metres','blender_to_godot':'(x,y,z)->(x,z,-y)','assembly_offset_godot':[-2.15,0,0],'server_blender_assembly':[2.15,-.88,.47],'server_yaw_radians':math.pi,'hatch':{'x':[.9,3.4],'z':[1.21,2.23],'y_plane':-1.1},'counter_top_z':1.235,'assets':{}}
 kinds=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['chips','pizza','curry']
 for kind in kinds:
  global M,ASSET
  bpy.ops.wm.read_factory_settings(use_empty=True);M={};ASSET=[]
  if kind=='pizza':timber_shell()
  else:shell(kind)
  running(kind);staff_door(kind);counter(kind);vents(kind);decoration(kind)
  if kind=='curry':cab()
  consolidate_palette()
  bpy.context.view_layer.update();base='ft_food_'+kind+'_production_v1';obs=list(ASSET);bb=bounds(obs)
  for o in obs:o['production_asset']=kind
  # Editable scene saved with named component meshes; export duplicate single consolidated mesh plus markers.
  bpy.ops.object.select_all(action='DESELECT');copies=[]
  for o in obs:
   if o.type!='MESH':continue
   cp=o.copy();cp.data=o.data.copy();bpy.context.collection.objects.link(cp);cp.select_set(True);copies.append(cp)
  bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();combined=bpy.context.object;combined.name=base
  # Triangulate before export for deterministic verification.
  mod=combined.modifiers.new('Deterministic triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name)
  combined.data.calc_loop_triangles();tri=len(combined.data.loop_triangles)
  for o in obs:
   if o.type=='EMPTY':o.select_set(True)
  bpy.ops.export_scene.gltf(filepath=str(R/'runtime'/(base+'.glb')),export_format='GLB',use_selection=True,export_yup=True,export_apply=True,export_extras=True)
  mats=[{'name':m.name,'triangles':sum(1 for p in combined.data.polygons if combined.data.materials[p.material_index]==m)} for m in combined.data.materials if m]
  bpy.data.objects.remove(combined,do_unlink=True)
  s,c=setup_render()
  bpy.ops.wm.save_as_mainfile(filepath=str(R/'source'/(base+'.blend')))
  render(s,c,R/'renders'/(kind+'-01-close.png'),9.6 if kind!='curry' else 10.7,yaw=35,target=(1.6,0,1.5))
  render(s,c,R/'renders'/(kind+'-02-rear.png'),9.6 if kind!='curry' else 10.7,yaw=145,target=(1.6,0,1.5))
  render(s,c,R/'renders'/(kind+'-03-gameplay-min18.png'),32,yaw=325)
  render(s,c,R/'renders'/(kind+'-04-gameplay-default62.png'),110.222222,yaw=325)
  rec={'runtime':'runtime/'+base+'.glb','source':'source/'+base+'.blend','triangles':tri,'materials':mats,'material_count':len(mats),'bounds_blender':bb,'mesh_components_editable':sum(o.type=='MESH' for o in obs),'interior_working_volume_blender':{'x':[.96,3.34],'y':[-.99,.35],'floor_z':.47,'roof_min_z':2.44},'staff_door_clear_visual_dimensions':[.81,1.67],'old_physical_x_bounds':[-1.26,4.59],'reserved_x_bounds':[-1.6,4.9],'reserved_y_bounds':[-1.75,1.75],'outside_old_physical_front_m':round(max(0,-1.26-bb[0][0]),4),'outside_reserved_front_m':round(max(0,-1.6-bb[0][0]),4)}
  manifest['assets'][kind]=rec
  (R/('manifest-'+kind+'.json')).write_text(json.dumps(rec,indent=2))
 (R/'manifest.json').write_text(json.dumps(manifest,indent=2));print('PRODUCTION_DONE',json.dumps(manifest))
if __name__=='__main__':main()
