from pathlib import Path
exec(Path(__file__).with_name('render_review.py').read_text().split('bpy.ops.wm.read_factory_settings(use_empty=True)')[0])
for sa,sb in [('male','male'),('female','female')]:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 a=actor(sa,'A',(0,-.45,0),0);b=actor(sb,'B',(0,.45,0),math.pi);s=setup()
 pose(a,'fight_swing_l',5);pose(b,'fight_flinch',2)
 render(sa+'-'+sb+'.png',(5,-7,3),3.7)
bpy.ops.wm.read_factory_settings(use_empty=True)
a=actor('male','A',(0,-.45,0),0);b=actor('female','B',(0,.45,0),math.pi)
c=actor('male','Steward',(1.05,0,0),math.pi/2,'steward');s=setup()
for who in [a,b,c]:
 pose(who,'fight_stand_down',8)
render('steward-intervention.png',(5,-7,3),4.2,target=(.3,0,.85))
print('EXTRA PREVIEWS COMPLETE')



