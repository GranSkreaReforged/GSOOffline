using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GSOOffline
{
    public partial class OfflineServer
    {
        private static readonly string[] SkillKeys =
        {
            "Cooking", "Woodcutting", "Blacksmithy", "Mining", "Farming", "Fishing", "Alchemy", "Carpentry",
            "Tailoring", "Cartography", "Sailing", "Thieving", "Swordsmanship", "Fencing", "Archery",
            "Macefighting", "Lightarmor", "Heavyarmor", "Elementalgrim", "Mentalgrim", "Arcanegrim",
            "Wrestling", "Healing", "Jewellery", "Animaltaming",
        };

        private AccountSave account;
        private CharacterSave character;

        private void RegisterAccountHandlers()
        {
            On(26, 1, OnLogin);
            On(0, 7, OnCreateAccount);
            On(6, 30, c => Send(7, 55, AccountPacketJson()));
            On(7, 47, OnCreateCharacter);
            On(7, 49, OnDeleteCharacter);
            On(7, 50, OnCharacterLogin);
            On(7, 4, OnCharacterCreationDone);
            On(8, 9, c => Edit(ch => { ch.fightingStyle = (int)c[2]; ch.profession = (int)c[3]; }));
            On(3, 22, c => Edit(ch => ch.gender = (int)c[2]));
            On(3, 23, c => Edit(ch => ch.hairstyle = (int)c[2]));
            On(3, 24, c => Edit(ch => ch.bodyType = (int)c[2]));
            On(3, 25, c => Edit(ch => ch.eyebrow = (int)c[2]));
            On(3, 26, c => Edit(ch => ch.bodySize = (int)c[2]));
            On(4, 6, c => Edit(ch => { ch.hairR = (int)c[2]; ch.hairG = (int)c[3]; ch.hairB = (int)c[4]; }));
            On(198, 4, c => Send(198, 4));   // appearance change: let the client open its editor
            On(198, 6, c => SaveCurrentCharacter());
            On(7, 7, OnDisconnect);
        }

        private void Edit(System.Action<CharacterSave> change)
        {
            if (character == null) return;
            change(character);
        }

        // Any username/password is accepted; the account is created on first login.
        private void OnLogin(object[] c)
        {
            string user = (c[1] as string ?? string.Empty).Trim();
            if (user.Length == 0) user = "Player";

            account = SaveSystem.LoadAccount(user);
            if (account == null)
            {
                account = new AccountSave { name = user };
                SaveSystem.SaveAccount(account);
                Plugin.Log.LogInfo($"Created offline account '{user}'.");
            }
            string result = "{\"resultCode\":1,\"charName\":\"\",\"accName\":\"" + JsonEscape(account.name) + "\"}";
            Send(0, 6, AccountPacketJson(), account.name, result);
        }

        // The "create account" screen: treat it as a login so the client's follow-up attemptLogin succeeds.
        private void OnCreateAccount(object[] c)
        {
            Send(25, 29, 1);
        }

        private string AccountPacketJson()
        {
            var packet = new Scr_Packets.AccountLoginPacket { accountName = account?.name ?? string.Empty };
            if (account != null)
            {
                foreach (string name in account.characters)
                {
                    var ch = SaveSystem.LoadCharacter(name);
                    if (ch == null) continue;
                    packet.characters.Add(new Scr_Packets.AccountLoginPacket.CharacterInfo
                    {
                        name = ch.name,
                        bodySize = ch.bodySize,
                        bodyType = ch.bodyType,
                        gender = (byte)ch.gender,
                        hairstyle = ch.hairstyle,
                        hairColorR = (byte)ch.hairR,
                        hairColorG = (byte)ch.hairG,
                        hairColorB = (byte)ch.hairB,
                        equippedItems = EquippedTypeIds(ch),
                    });
                }
            }
            return JsonUtility.ToJson(packet);
        }

        private void OnCreateCharacter(object[] c)
        {
            string name = (c[1] as string ?? string.Empty).Trim();
            int code = ValidateCharacterName(name);
            if (code < 0)
            {
                Send(3, 60, string.Empty, code);
                return;
            }
            var ch = new CharacterSave { name = name, account = account.name };
            SaveSystem.SaveCharacter(ch);
            account.characters.Add(name);
            SaveSystem.SaveAccount(account);
            Plugin.Log.LogInfo($"Created character '{name}'.");
            Send(3, 60, AccountPacketJson(), 1);
        }

        private int ValidateCharacterName(string name)
        {
            if (account == null) return -1;
            if (name.Length < 3 || name.Length > 16) return -2;
            foreach (char ch in name)
                if (!char.IsLetter(ch) && ch != ' ') return -3;
            if (SaveSystem.CharacterExists(name)) return -4;
            return 1;
        }

        private void OnDeleteCharacter(object[] c)
        {
            string name = c[1] as string;
            if (account == null || name == null || !account.characters.Remove(name))
            {
                Send(3, 61, string.Empty, -1);
                return;
            }
            SaveSystem.SaveAccount(account);
            SaveSystem.DeleteCharacter(name);
            Send(3, 61, AccountPacketJson(), 1);
        }

        private void OnCharacterLogin(object[] c)
        {
            GameData.EnsureLoaded();
            string name = c[1] as string;
            var ch = name == null ? null : SaveSystem.LoadCharacter(name);
            if (ch == null || account == null || !account.characters.Contains(ch.name))
            {
                Send(2, 41, "Character not found.");
                return;
            }

            character = ch;
            if (ch.currentHealth <= 0) ch.currentHealth = ch.health;   // saved while dead
            ResetWorldState();
            Plugin.Log.LogInfo($"'{ch.name}' entering world: scene {ch.scene} at {ch.Position}.");

            Send(2, 42, ch.name, BuildPlayerData(ch));
            SendInventory();
            if (!ch.creationDone)
                Send(7, 3, ch.name);
            Send(25, 28, 1);
            ChangeScene(ch.scene, ch.Position);
        }

        private void OnCharacterCreationDone(object[] c)
        {
            if (character == null) return;
            character.creationDone = true;
            GrantStarterKit();
            Send(1, 4, character.name, true);
            SaveCurrentCharacter();
        }

        private void OnDisconnect(object[] c)
        {
            SaveCurrentCharacter();
            if (character != null)
                Plugin.Log.LogInfo($"'{character.name}' logged out.");
            character = null;
            ResetWorldState();
        }

        private void SaveCurrentCharacter()
        {
            if (character == null) return;
            var player = Scr_PlayerHandler.instance != null ? Scr_PlayerHandler.instance.player : null;
            if (player != null && sceneReady && pendingSpawn == null)
            {
                character.Position = player.transform.position;
                character.rot = (int)player.transform.eulerAngles.y;
            }
            SaveSystem.SaveCharacter(character);
        }

        /// <summary>The key=value block consumed by Scr_PlayerHandler.addPlayer.</summary>
        private string BuildPlayerData(CharacterSave ch)
        {
            var sb = new StringBuilder();
            void Line(string k, object v) => sb.Append(k).Append('=').Append(System.Convert.ToString(v, CultureInfo.InvariantCulture)).Append('\n');

            Line("Silver", ch.silver);
            Line("Banksilver", ch.bankSilver);
            Line("Gold", ch.gold);
            Line("Gender", ch.gender);
            Line("Hairstyle", ch.hairstyle);
            Line("Haircolor", ch.hairR + "," + ch.hairG + "," + ch.hairB);
            Line("Bodytype", ch.bodyType);
            Line("Bodysize", ch.bodySize);
            Line("Eyebrow", ch.eyebrow);
            Line("Health", ch.health);
            Line("Currenthealth", ch.currentHealth);
            Line("Mana", ch.mana);
            Line("CarryingCap", 150);
            Line("Currentmana", ch.mana);
            Line("Position", SceneWorld.Vec(ch.Position));
            Line("HomeWayshrine", ch.homeWayshrine);
            if (ch.quests.Count > 0) Line("Quests", QuestsLine());
            if (ch.abilitySlots.Count > 0)
                Line("Abilityslots", string.Join(",", ch.abilitySlots.ConvertAll(a => a.slot + "_" + a.ability).ToArray()));
            Line("Wayshrines", string.Join(",", UnlockedWayshrines(ch).ConvertAll(i => i.ToString()).ToArray()));
            foreach (string skill in SkillKeys)
                Line("s_" + skill, ch.GetXp(skill));
            return sb.ToString();
        }

        private static string JsonEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
