#!/bin/bash
# renders the verification set from the installed GLB (uses the rain board's farm_scene.py + rainscene.py)
cd "$(dirname "$0")"; D="$(pwd -W)"
T="$D/tools"
B="/c/Program Files/Blender Foundation/Blender 4.1/blender.exe"
R() { out=$1; shift; t=$1; shift; "$B" -b --python "$T/farm_scene.py" -- $t "$D/$out.png" 48 1600 1000 crowd=0 py="$T/rainscene.py" guests=0 extra="$D/place_tent.py" "$@" > /dev/null 2>&1; echo "done $out"; }
R tent-sun-shade game zoom=11 focus=8,1,10 rmode=dry pud=0 under=sun
R tent-sun-shade-faded game zoom=11 focus=8,1,10 rmode=dry pud=0 under=sun fade=0.3
R tent-rain-opaque game zoom=11 focus=8,1,10 rmode=heavy under=rain
R tent-rain-faded game zoom=11 focus=8,1,10 rmode=heavy under=rain fade=0.3
R tent-rot2-faded game zoom=11 focus=8,1,10 rmode=dry pud=0 under=sun fade=0.3 rot=2
R tent-zoom24-sun game zoom=24 focus=6,0,8 rmode=dry pud=0 under=sun
python -c "import sys; sys.path.insert(0, 'tools'); import overlay; from PIL import Image
for n in ('tent-rain-opaque', 'tent-rain-faded'): overlay.rain(Image.open(n + '.png'), 11, 'B').save(n + '-with-rain.png')"
