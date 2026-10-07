import sys, os, UnityPy
src, out = sys.argv[1], sys.argv[2]
env = UnityPy.load(src)
n = 0
for obj in env.objects:
    if obj.type.name == "TextAsset":
        d = obj.read()
        name = getattr(d, "m_Name", None) or d.name
        data = d.m_Script if hasattr(d, "m_Script") else d.script
        if isinstance(data, str):
            data = data.encode("utf-8", "surrogateescape")
        path = os.path.join(out, f"{name}.txt")
        if os.path.exists(path):
            path = os.path.join(out, f"{name}_{obj.path_id}.txt")
        with open(path, "wb") as f:
            f.write(data)
        n += 1
print("dumped", n)
