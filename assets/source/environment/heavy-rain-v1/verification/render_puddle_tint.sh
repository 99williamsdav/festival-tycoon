#!/bin/bash
# Puddle tint by ground state (INTEGRATION section 3b): rendered on the farm under the game camera in a heavy shower.
cd "$(dirname "$0")"; D="$(pwd -W)"; T="$D/tools"; B="/c/Program Files/Blender Foundation/Blender 4.1/blender.exe"
R() { out=$1; shift; "$B" -b --python "$T/farm_scene.py" -- game "$D/$out.png" 40 1600 1000 crowd=0 py="$T/rainscene.py" rmode=heavy "$@" > /dev/null 2>&1; echo "done $out"; }
R puddle-tint-stage-zoom9 zoom=9 focus=-6.5,0,11 dirty=1
R puddle-tint-stage-zoom9-before zoom=9 focus=-6.5,0,11
R puddle-tint-zoom24 zoom=24 dirty=1
python -c "import sys; sys.path.insert(0, 'tools'); import overlay; from PIL import Image
for n, z in (('puddle-tint-stage-zoom9', 9), ('puddle-tint-zoom24', 24)): overlay.rain(Image.open(n + '.png'), z, 'B').save(n + '-with-rain.png')"
