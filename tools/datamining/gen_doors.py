"""Build src/GSOOffline/Data/doors.json from extracted/markers.json.

The old server mapped each door interactable to a destination; that table is lost. The scene
objects are named after their role ("Interactable_9EntranceToYorkhillMiningCave" /
"Interactable_10ExitFromYorkhillMiningCave"), so pairs are rebuilt from the names. Arrival points sit
a couple of metres off the destination door, toward where the rest of that scene's markers are
(i.e. into the walkable area rather than into the wall).

Usage: python -I gen_doors.py <markers.json> <doors.json>
"""
import json, math, re, sys

markers = json.load(open(sys.argv[1], encoding="utf-8"))
out_path = sys.argv[2]

doors = [m for m in markers if m["cls"] == "Scr_Interactable" and m["pos"]]
by_level = {}
for m in markers:
    if m["pos"]:
        by_level.setdefault(m["level"], []).append(m["pos"])


def arrival(level, pos):
    near = [p for p in by_level.get(level, []) if 1.0 < math.dist(p, pos) < 40.0]
    if not near:
        return [round(pos[0], 2), round(pos[1] + 0.5, 2), round(pos[2], 2)]
    cx = sum(p[0] for p in near) / len(near) - pos[0]
    cz = sum(p[2] for p in near) / len(near) - pos[2]
    n = math.hypot(cx, cz) or 1.0
    return [round(pos[0] + 2.0 * cx / n, 2), round(pos[1] + 0.5, 2), round(pos[2] + 2.0 * cz / n, 2)]


NAME = re.compile(r"Interactable_(\d+)_?(Entrance|Exit)(?:To|From)?(\w+?)(?:To\w+|From\w+)?(?:\s*\(\d+\))?$")


def key(goname):
    m = NAME.search(goname or "")
    if not m:
        return None
    return m.group(2), m.group(3).replace("Entrance", "")


# Explicit pairings where names differ between the two sides (verified by hand against the markers).
MANUAL = [
    # (fromType, fromLevel, toType, toLevel)
    (9, 1, 10, 2), (10, 2, 9, 1),                 # Yorkhill mining cave
    (12, 1, 11, 5), (11, 5, 12, 1),               # Evil monk dungeon
    (15, 1, 16, 5), (16, 5, 15, 1),               # Roke dungeon <-> forest
    (14, 1, 13, 5), (13, 5, 14, 1), (13, 11, 14, 1),   # Roke dungeon <-> monastery hatch
    (22, 1, 23, 16), (23, 16, 22, 1),             # Silk crab cave
    (24, 1, 25, 1), (25, 1, 24, 1),               # Yorkhill monastery (same scene)
    (33, 8, 34, 25), (34, 25, 33, 8),             # Ulan's dungeon
    (35, 25, 38, 26), (38, 26, 35, 25),           # Ulan's 2nd floor
]

result = []
for ft, fl, tt, tl in MANUAL:
    src = [d for d in doors if d["typeId"] == ft and d["level"] == fl]
    dst = [d for d in doors if d["typeId"] == tt and d["level"] == tl]
    if not src or not dst:
        print("missing", ft, fl, tt, tl)
        continue
    for s in src:
        result.append({"type": ft, "scene": fl, "pos": s["pos"], "toScene": tl,
                       "toPos": arrival(tl, dst[0]["pos"]), "note": dst[0]["goName"]})

# Doors into a whole scene without a matching exit door: arrive at that scene's first marker cluster.
def scene_entry(level):
    pts = by_level.get(level, [])
    return arrival(level, pts[0]) if pts else [0, 50, 0]

for d in doors:
    if d["typeId"] == 42 and d["level"] == 16:   # Coral dungeon entrance
        result.append({"type": 42, "scene": 16, "pos": d["pos"], "toScene": 27, "toPos": scene_entry(27), "note": "Coral dungeon"})

json.dump({"doors": result,
           # building door scene -> interior scene (type 29 in, type 28 out)
           "interiors": [{"scene": 1, "interior": 23}, {"scene": 8, "interior": 20}, {"scene": 21, "interior": 22}],
           # interior exits for each interior scene (arrival spots)
           "interiorSpawns": [{"scene": l, "pos": arrival(l, d["pos"])}
                              for d in doors if d["typeId"] == 28 for l in [d["level"]]],
           "portal": {"scene": 34, "pos": scene_entry(34)}},
          open(out_path, "w", encoding="utf-8"), indent=1)
print(len(result), "door links written")
