using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The message boards in town (interactable 26) open the client's "Messages" window (27). On the original server
    /// every player shared one board; offline there is no one to read a post, so it is read-only and holds the
    /// developer's messages. The window asks for a page (3/45) and gets one 2/33 per message, ten to a page.
    /// </summary>
    public partial class OfflineServer
    {
        private const int WindowMessageBoard = 27, InteractMessageBoard = 26;
        private const int BoardPageSize = 10;

        [System.Serializable]
        private class BoardMessage   // the client's Scr_GlobalMessageBoard.Message
        {
            public string subject, message, playerName, date;

            public BoardMessage(string subject, string message)
            {
                this.subject = subject;
                this.message = message;
                playerName = "The Developer";
                date = "October 26, 2026";
            }
        }

        // Shown in this order. Subjects stay under the client's 30 characters, messages under its 300.
        private static readonly BoardMessage[] BoardMessages =
        {
            new BoardMessage("Thank you for playing!",
                "Welcome back to Gran Skrea! The official servers are gone, but the world lives on. This mod is a free, " +
                "non-commercial fan project to keep the game playable for everyone who owns it. Thank you for playing, " +
                "and for keeping Gran Skrea alive. See you out there!"),
            new BoardMessage("A special thanks to Lukebox",
                "None of this would exist without Lukebox, the original developer of Gran Skrea Online. Thank you for " +
                "creating this world, its towns, quests and creatures, and for the years of work you poured into it. " +
                "This mod is a love letter to your game. Thank you, Lukebox!"),
            new BoardMessage("About the offline server",
                "The server now runs inside your game, and your characters are saved in OfflineSaves in the game folder. " +
                "Some systems had to be rebuilt from scratch, so a few things may differ from the original. Type /help " +
                "in chat for commands."),
            new BoardMessage("Bugs, ideas and updates",
                "Found a bug or have an idea? Use \"Report a bug or give feedback\" in the menu, or open an issue at " +
                "github.com/GranSkreaReforged/GSOOffline. When a new version is out, the top-left corner says so. " +
                "Happy adventuring!"),
        };

        private void RegisterMessageBoardHandlers()
        {
            On(3, 45, c => SendBoardPage(c.Length > 2 && c[2] is int p ? p : 1));   // the window's page buttons
            On(0, 5, c => Notice("The message board is read-only offline: there is no one else to read your post.", "gray"));
        }

        /// <summary>A message board click: opens the Messages window, which then asks for page 1. True if it was one.</summary>
        private bool HandleMessageBoard(int typeId)
        {
            if (typeId != InteractMessageBoard) return false;
            var board = Script_WindowController.instance?.window_globalMessageBoard;
            if (board != null)   // open on the first post rather than an empty page
            {
                var first = BoardMessages[0];
                board.showingMessageSubject = first.subject;
                board.showingMessageText = first.message;
                board.showingMessagePlayer = first.playerName + " on " + first.date;   // as the client's list buttons write it
            }
            Send(3, 33, character.name, WindowMessageBoard);
            Plugin.Log.LogInfo("[board] opened");
            return true;
        }

        private void SendBoardPage(int page)
        {
            if (character == null) return;
            int first = (Mathf.Max(page, 1) - 1) * BoardPageSize;
            for (int i = first; i < BoardMessages.Length && i < first + BoardPageSize; i++)
                Send(2, 33, character.name, JsonUtility.ToJson(BoardMessages[i]));
        }
    }
}
