# Reuse the same GLB-based review setup, then show structure alone.
from pathlib import Path
R=Path(__file__).resolve().parent
script=(R/'render_review.py').read_text().split("render('riser-close.png'")[0]
exec(compile(script,str(R/'render_review.py'),'exec'))
for ob in bpy.data.objects:
 top=ob
 while top.parent:top=top.parent
 if top.name.startswith(('ReviewPerson','ReviewDrums','Speaker')):ob.hide_render=True
render('structure-rear.png',(-.6,0,.7),(-10,14,10),10.7)
