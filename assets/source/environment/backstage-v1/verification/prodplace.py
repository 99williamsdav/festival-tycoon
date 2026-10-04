# exec()'d by farm_scene.py: replace the stage with v3 and place everything from backstage_layout_v1.json exactly as the
# game will (Godot position + yaw about +Y), to verify the production files and the layout.
import json
L = json.load(open("C:/Projects/festival-tycoon/assets/source/environment/backstage-v1/out/backstage_layout_v1.json"))
for o in list(sc.collection.objects):
    if o.instance_type == 'COLLECTION' and o.instance_collection and o.instance_collection.name.startswith("lwf_trailer_stage_v2"):
        o.instance_collection = asset("environment/lwf_trailer_stage_v3.glb")
for b in L["barriers"]:
    place("environment/" + L["barrier_file"], b["x"], b["z"], b["yaw"])
for p in L["props"]:
    place("environment/" + p["file"], p["x"], p["z"], p["yaw"])
