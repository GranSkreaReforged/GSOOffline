import sys, UnityPy
env = UnityPy.load(sys.argv[1])
for obj in env.objects:
    if obj.type.name == "BuildSettings":
        d = obj.read_typetree()
        for i, s in enumerate(d.get("scenes", [])):
            print(i, s)
