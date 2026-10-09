# python make_soaked_palette.py -> out/attendee_palette_soaked_v1_contract.json
# A full copy of game/assets/characters/attendee_palette_v1_contract.json (same schema, so AttendeePalette.Parse reads it unchanged)
# with every colourway's soaking slots multiplied by 0.58: shirt 3/4, trousers 7/8 and hair 10/11 go about 40% darker.
# Badge 5/6 and skin don't soak. Everything else in the contract is untouched.
import json, os, copy
HERE = os.path.dirname(os.path.abspath(__file__))
C = json.load(open("C:/Projects/festival-tycoon/game/assets/characters/attendee_palette_v1_contract.json"))
SOAK, K = (3, 4, 7, 8, 10, 11), 0.58
dark = lambda h: "".join(f"{round(int(h[i:i + 2], 16) * K):02X}" for i in (0, 2, 4))
S = copy.deepcopy(C)
for group in ("clothing_colourways", "hair_colours"):
    for entry in S[group]:
        entry["slots"] = {k: (dark(v) if int(k) in SOAK else v) for k, v in entry["slots"].items()}
        entry["id"] = entry["id"] + "-soaked"
# "version" stays 1: AttendeePalette.Parse requires it. The "soaked" key marks this copy.
S["soaked"] = dict(source="attendee_palette_v1_contract.json", rule=f"slot hex x {K} on slots {list(SOAK)}", built_by="assets/source/characters/rain-guests-v1/make_soaked_palette.py")
json.dump(S, open(os.path.join(HERE, "out", "attendee_palette_soaked_v1_contract.json"), "w"), indent=1)
print("keys", list(S.keys()))
