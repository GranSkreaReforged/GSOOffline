import sys, os, collections, UnityPy
data = sys.argv[1]
env = UnityPy.load(os.path.join(data, "globalgamemanagers.assets"))
scripts = {}
for obj in env.objects:
    if obj.type.name == "MonoScript":
        d = obj.read()
        scripts[(obj.assets_file.name, obj.path_id)] = d.m_ClassName
want = {k for k, v in scripts.items() if v in ("Scr_NPCDummy", "Scr_HarvestableDummy", "Scr_SpawnPoint")}
print("monoscripts:", len(scripts), "targets:", [(scripts[k], k[1]) for k in want])
want_ids = {k[1]: scripts[k] for k in want}
for i in range(43):
    f = os.path.join(data, f"level{i}")
    e = UnityPy.load(f)
    c = collections.Counter()
    for obj in e.objects:
        if obj.type.name == "MonoBehaviour":
            raw = obj.get_raw_data()
            # m_GameObject PPtr (4+8), m_Enabled(1)+pad(3), m_Script PPtr fileId(4)+pathId(8)
            import struct
            try:
                fid, pid = struct.unpack_from("<iq", raw, 16)
            except struct.error:
                continue
            if pid in want_ids:
                c[want_ids[pid]] += 1
    if c: print(f"level{i}", dict(c))
