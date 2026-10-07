"""Dump Scr_Interactable / Scr_NPCDummy / Scr_HarvestableDummy placements (world space) from every level file."""
import sys, os, struct, json, UnityPy

data_dir, out_path = sys.argv[1], sys.argv[2]

gg = UnityPy.load(os.path.join(data_dir, "globalgamemanagers.assets"))
script_ids = {}
for obj in gg.objects:
    if obj.type.name == "MonoScript":
        script_ids[obj.path_id] = obj.read().m_ClassName
WANT = {"Scr_Interactable", "Scr_NPCDummy", "Scr_HarvestableDummy"}

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by,
            aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw,
            aw*bw - ax*bx - ay*by - az*bz)

def qrot(q, v):
    x, y, z, w = q
    p = (v[0], v[1], v[2], 0.0)
    r = qmul(qmul(q, p), (-x, -y, -z, w))
    return r[:3]

def read_str(raw, off):
    n, = struct.unpack_from("<i", raw, off); off += 4
    s = raw[off:off+n].decode("utf-8", "replace"); off += n
    off = (off + 3) & ~3
    return s, off

result = []
for i in range(43):
    env = UnityPy.load(os.path.join(data_dir, f"level{i}"))
    objs = {o.path_id: o for o in env.objects}
    transforms = {}
    go_transform = {}
    for o in env.objects:
        if o.type.name in ("Transform", "RectTransform"):
            t = o.read()
            transforms[o.path_id] = t
            go_transform[t.m_GameObject.path_id] = o.path_id

    def world_pos(tid):
        t = transforms[tid]
        pos = (t.m_LocalPosition.x, t.m_LocalPosition.y, t.m_LocalPosition.z)
        parent = t.m_Father.path_id
        while parent and parent in transforms:
            p = transforms[parent]
            s = (p.m_LocalScale.x, p.m_LocalScale.y, p.m_LocalScale.z)
            q = (p.m_LocalRotation.x, p.m_LocalRotation.y, p.m_LocalRotation.z, p.m_LocalRotation.w)
            pos = qrot(q, (pos[0]*s[0], pos[1]*s[1], pos[2]*s[2]))
            pos = (pos[0] + p.m_LocalPosition.x, pos[1] + p.m_LocalPosition.y, pos[2] + p.m_LocalPosition.z)
            parent = p.m_Father.path_id
        return [round(c, 2) for c in pos]

    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        raw = o.get_raw_data()
        try:
            go_fid, go_pid, enabled, sc_fid, sc_pid = struct.unpack_from("<iqBxxxiq", raw, 0)
        except struct.error:
            continue
        cls = script_ids.get(sc_pid)
        if cls not in WANT:
            continue
        off = 28
        name, off = read_str(raw, off)  # m_Name
        rec = {"level": i, "cls": cls, "goEnabled": enabled}
        rec["typeId"], = struct.unpack_from("<i", raw, off); off += 4
        if cls == "Scr_Interactable":
            rec["name"], off = read_str(raw, off)
            rec["customDistance"], = struct.unpack_from("<i", raw, off)
        go = objs.get(go_pid)
        if go is not None:
            g = go.read()
            rec["goName"] = g.m_Name
            rec["active"] = bool(g.m_IsActive)
        tid = go_transform.get(go_pid)
        rec["pos"] = world_pos(tid) if tid else None
        result.append(rec)
    print(f"level{i}: {sum(1 for r in result if r['level']==i)}", flush=True)

with open(out_path, "w", encoding="utf-8") as f:
    json.dump(result, f, indent=0)
print("total", len(result))
