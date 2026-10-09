#!/bin/bash
# verification renders from the INSTALLED files, on the farm under the game camera, in a heavy shower
cd "$(dirname "$0")"; D="$(pwd -W)"; T="C:/Projects/festival-tycoon/assets/source/environment/stretch-tent-v1/verification/tools"
B="/c/Program Files/Blender Foundation/Blender 4.1/blender.exe"
R() { out=$1; shift; "$B" -b --python "$T/farm_scene.py" -- game "$D/$out.png" 40 1600 1000 crowd=0 py="$T/rainscene.py" guests=0 pud=0 rmode=heavy extra="$D/place_ground.py" "$@" > /dev/null 2>&1; echo "done $out"; }
R straw-tileset-zoom13 zoom=13 focus=12,0,12 show=straw
R straw-fresh-close zoom=5 focus=7.5,0,12 show=straw
R straw-trampled-close zoom=5 focus=18.5,0,12 show=straw
R mats-zoom30 zoom=30 focus=5,0,16 show=mats
R mats-corner-close zoom=6 focus=-8.5,0,20.5 show=mats
python -c "import sys; sys.path.insert(0, '$T'); import overlay; from PIL import Image
for n, z in (('straw-tileset-zoom13', 13), ('mats-zoom30', 30)): overlay.rain(Image.open(n + '.png'), z, 'B').save(n + '-with-rain.png')"
