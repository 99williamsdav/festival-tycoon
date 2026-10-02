# Verify the production instrument kits on the real stage. exec()'d by farm_scene.py with inst=<indie|metal|folk|electronic>.
_SAVE = dict(OPTS); OPTS["genre"] = "none"
exec(open(__file__.replace("farm_scene.py", "band.py"), encoding="utf-8").read())
OPTS.clear(); OPTS.update(_SAVE)
INST = "C:/Projects/festival-tycoon/assets/source/characters/instruments-v1/out/"
SETS = "C:/Projects/festival-tycoon/assets/source/environment/stage-sets-v1/out/"
S = OPTS["inst"]
M0, M1, M2 = (-15.75, 11.25), (-16.75, 9.25), (-17.25, 12.25)
_ext = {}
def ext(path):
    if path in _ext: return _ext[path]
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=path)
    col = bpy.data.collections.new(path.split("/")[-1])
    for o in [o for o in bpy.data.objects if o not in before]:
        for c in list(o.users_collection): c.objects.unlink(o)
        col.objects.link(o)
    _ext[path] = col; return col
def inst_at(col, M):
    e = bpy.data.objects.new("i", None); e.instance_type = 'COLLECTION'; e.instance_collection = col; e.matrix_world = M
    sc.collection.objects.link(e); return e
def kit_performer(sex, kitfile, mark, cos, external=True, yaw=-90.0):
    M = frame(*mark, yaw)
    e = bpy.data.objects.new("body", None); e.instance_type = 'COLLECTION'
    e.instance_collection = recoloured(CH + f"lwf_performer_{sex}_body_v2.glb", cos, drop=("IdleLeftArm", "IdleRightArm"))
    e.matrix_world = M; sc.collection.objects.link(e)
    inst_at(ext(INST + kitfile + ".glb") if external else asset(CH + kitfile + ".glb"), M)
    return M
def mic(sex, mark):
    M = frame(*mark)
    col = ext(INST + "lwf_mic_stand_v1.glb")
    stand = next(o for o in col.objects if o.name.startswith("MicStand")); head = next(o for o in col.objects if o.name.startswith("MicHead"))
    for o, z in ((stand, 0.0), (head, {"male": 1.50, "female": 1.43}[sex])):
        c = o.copy(); sc.collection.objects.link(c); c.parent = None; c.matrix_world = M @ Matrix.Translation((0, 0, z))
def drums(name, external=True):
    inst_at(ext(INST + name + ".glb") if external else asset(CH + name + ".glb"), frame(*M2))
def stage_set(name):
    root = Matrix.Translation(gd(-16, 0, 11)) @ Matrix.Rotation(math.radians(90), 4, 'Z')
    inst_at(ext(SETS + name + ".glb"), root)

if S == "indie":
    kit_performer("male", "lwf_guitarist_male_electric_kit_v2", M0, costume("2E3A4F", "D9C27A", "26262A", "3F2F28")); mic("male", M0)
    kit_performer("female", "lwf_bassist_female_kit_v2", M1, costume("D9C27A", "2E3A4F", "4A5A78", "9A8056"), external=False)
    kit_performer("male", "lwf_drummer_male_kit_v2", M2, costume("8A8F96", "E8DEC4", "26262A", "6A5040"), external=False)
    drums("lwf_drum_hardware_only_v2", external=False); stage_set("lwf_stage_set_indie_v1")
elif S == "metal":
    M = kit_performer("female", "lwf_guitarist_female_flyingv_kit_v2", M0, costume("3F5F8A", "1A1A1A", "1E1F24", "1A1716")); mic("female", M0)
    kit_performer("male", "lwf_bassist_male_kit_v2", M1, costume("1A1A1A", "3F5F8A", "1E1F24", "1A1716"), external=False)
    kit_performer("male", "lwf_drummer_male_kit_v2", M2, costume("3F5F8A", "1A1A1A", "1E1F24", "6A5A4A"), external=False)
    drums("lwf_drum_hardware_double_kick_v1"); stage_set("lwf_stage_set_metal_v1")
elif S == "folk":
    kit_performer("female", "lwf_guitarist_female_kit_v2", M0, costume("B8935A", "E8DCC0", "6B5440", "9A5A30"), external=False); mic("female", M0)
    kit_performer("male", "lwf_bassist_male_kit_v2", M1, costume("6E7F5A", "E8DCC0", "6B5440", "8E8A84"), external=False)
    kit_performer("female", "lwf_drummer_female_kit_v2", M2, costume("C9A3A0", "F2EBDD", "4E5A44", "3F2F28"), external=False)
    drums("lwf_drum_hardware_compact_v1"); stage_set("lwf_stage_set_folk_v1")
elif S == "electronic":
    kit_performer("female", "lwf_electronic_female_desk_kit_v2", M0, costume("4F6B4A", "A0A0A0", "3E3F44", "2A2624"))
    kit_performer("male", "lwf_keyboardist_male_kit_v2", M1, costume("3E3F44", "4F6B4A", "26262A", "6A5040"))
    kit_performer("male", "lwf_drummer_male_kit_v2", M2, costume("A0A0A0", "4F6B4A", "3E3F44", "3F2F28"), external=False)
    drums("lwf_drum_hardware_electronic_v1"); stage_set("lwf_stage_set_electronic_v1")
elif S in ("folk2", "pop2"):
    P2 = INST
    INST_SAVE = INST
    def kp(sex, path, mark, cos):
        M = frame(*mark, -90.0)
        e = bpy.data.objects.new("body", None); e.instance_type = 'COLLECTION'
        e.instance_collection = recoloured(CH + f"lwf_performer_{sex}_body_v2.glb", cos, drop=("IdleLeftArm", "IdleRightArm"))
        e.matrix_world = M; sc.collection.objects.link(e)
        before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=path)
        root = bpy.data.objects.new("kitroot", None); sc.collection.objects.link(root); root.matrix_world = M
        for o in [o for o in bpy.data.objects if o not in before and o.parent is None and o is not root]: o.parent = root
        sc.frame_set(int(OPTS.get("frame", 1)))
    if S == "folk2":
        kit_performer("female", "lwf_guitarist_female_kit_v2", M0, costume("B8935A", "E8DCC0", "6B5440", "9A5A30"), external=False); mic("female", M0)
        kp("male", P2 + "lwf_accordionist_male_kit_v2.glb", M1, costume("6E7F5A", "E8DCC0", "6B5440", "8E8A84"))
        kit_performer("female", "lwf_drummer_female_kit_v2", M2, costume("C9A3A0", "F2EBDD", "4E5A44", "3F2F28"), external=False)
        drums("lwf_drum_hardware_compact_v1"); stage_set("lwf_stage_set_folk_v1")
    else:
        kp("female", P2 + "lwf_singer_female_kit_v2.glb", M0, costume("D86A9A", "F2EBDD", "2E2E3A", "3F2F28")); mic("female", M0)
        kit_performer("male", "lwf_bassist_male_kit_v2", M1, costume("F2EBDD", "D86A9A", "2E2E3A", "6A5040"), external=False)
        kit_performer("male", "lwf_drummer_male_kit_v2", M2, costume("2E2E3A", "D86A9A", "3E3F44", "3F2F28"), external=False)
        drums("lwf_drum_hardware_only_v2", external=False); stage_set("lwf_stage_set_pop_v1")
