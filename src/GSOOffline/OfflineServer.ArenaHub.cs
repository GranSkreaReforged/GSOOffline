using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The artisans' hall (#84): the Arena's Team Arena pit (scene 14), unused offline since its 1v1 and team fights
    /// needed other players, becomes a processing hub. The Sign-up board on the walkway takes a player whose
    /// Blacksmithy, Tailoring, Carpentry and Cooking are all at least 20 down into the pit, which has the crafting
    /// stations for those skills (house furniture prefabs, which work as stations anywhere), a banker and their
    /// shopkeepers (content.json). A copy of the board against the pit's north wall brings them back up: the pit's
    /// walls are 7 m high, with no stairs.
    /// </summary>
    public partial class OfflineServer
    {
        private const int SceneArena = 14, InteractArenaBoard = 55;
        private const int HubSkillLevel = 20;
        private static readonly int[] HubSkills = { 4, 11, 10, 9 };   // Blacksmithy, Tailoring, Carpentry, Cooking
        private const string ArenaBoardName = "Prefab_Interactable_55ArenaSignUpBoard";
        private const float PitTop = 86f;   // the pit floor is at 82.5, the walkway round it at 89.5

        private static readonly Vector3 PitArrival = new Vector3(-194f, 82.5f, -82f);
        private static readonly Vector3 WalkwayArrival = new Vector3(-166f, 89.5f, -108f);   // beside the board
        private static readonly Vector3 PitBoardPos = new Vector3(-194f, 82.5f, -75.5f);

        private struct HubStation
        {
            public int furniture;   // the furniture item, whose prefab is ab_furniture_<type>
            public Vector3 pos;
            public float rot;
        }

        // Against the pit's walls (floor x -214..-174, z -144..-74), each skill in its own corner.
        private static readonly HubStation[] HubStations =
        {
            new HubStation { furniture = 10399, pos = new Vector3(-212f, 82.5f, -84f), rot = 90f },    // Forge
            new HubStation { furniture = 10396, pos = new Vector3(-212f, 82.5f, -92f), rot = 90f },    // Anvil
            new HubStation { furniture = 10663, pos = new Vector3(-212f, 82.5f, -120f), rot = 90f },   // Saw
            new HubStation { furniture = 10664, pos = new Vector3(-212f, 82.5f, -128f), rot = 90f },   // Carpenters' workbench
            new HubStation { furniture = 10397, pos = new Vector3(-176f, 82.5f, -84f), rot = 270f },   // Spinning wheel
            new HubStation { furniture = 10661, pos = new Vector3(-176f, 82.5f, -91f), rot = 270f },   // Loom
            new HubStation { furniture = 10398, pos = new Vector3(-176f, 82.5f, -98f), rot = 270f },   // Tailors' workbench
            new HubStation { furniture = 10665, pos = new Vector3(-176f, 82.5f, -122f), rot = 270f },  // Stove (Baker's oven)
        };

        /// <summary>On loading the Arena: the stations and the pit's board. Scene objects, gone with the scene.</summary>
        private static void SetUpArenaHub(int sceneId)
        {
            if (sceneId != SceneArena) return;
            foreach (var s in HubStations)
            {
                var prefab = Resources.Load("1LoadingAssets/Furniture/ab_furniture_" + s.furniture, typeof(GameObject));
                if (prefab == null) { Plugin.Log.LogWarning($"[hub] no furniture prefab {s.furniture}"); continue; }
                var go = Object.Instantiate(prefab) as GameObject;
                go.transform.position = s.pos;
                go.transform.rotation = Quaternion.Euler(0f, s.rot, 0f);
            }
            var board = GameObject.Find(ArenaBoardName);
            if (board == null) { Plugin.Log.LogWarning("[hub] no sign-up board in the Arena"); return; }
            var pitBoard = Object.Instantiate(board);
            pitBoard.transform.position = PitBoardPos;
            pitBoard.transform.rotation = board.transform.rotation;
            Plugin.Log.LogInfo($"[hub] artisans' hall: {HubStations.Length} stations and the pit's board");
        }

        /// <summary>A Sign-up board click: down into the hall if the skills are there, or back up. True if it was one.</summary>
        private bool HandleArenaHubInteract(int typeId)
        {
            if (typeId != InteractArenaBoard || character.scene != SceneArena) return false;
            var player = LocalPlayer;
            if (player == null) return true;
            if (player.transform.position.y < PitTop)
            {
                Teleport(SceneArena, WalkwayArrival);
                return true;
            }
            var missing = new System.Collections.Generic.List<string>();
            foreach (int skill in HubSkills)
            {
                int level = SkillLevel(skill);
                if (level < HubSkillLevel) missing.Add($"{SkillById[skill]} {level}/{HubSkillLevel}");
            }
            if (missing.Count > 0)
            {
                Notice($"The artisans' hall below is for skilled crafters: Blacksmithy, Tailoring, Carpentry and Cooking at level {HubSkillLevel}. You have {string.Join(", ", missing.ToArray())}.", "gray");
                Plugin.Log.LogInfo($"[hub] refused: {string.Join(", ", missing.ToArray())}");
                return true;
            }
            Notice("You climb down into the artisans' hall.", "green");
            Teleport(SceneArena, PitArrival);
            return true;
        }
    }
}
