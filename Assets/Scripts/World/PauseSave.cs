using SheepGate.Core;
using SheepGate.Player;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// Saves when the app leaves the foreground.
    ///
    /// Every save in the game is taken after an action — a conversation, a pile, a course, the
    /// end of the day — and none was taken on the way out. That is right for the wall, which only
    /// changes by action, and wrong for two things a player notices: where they were standing,
    /// recorded on arrival and so a walk behind when the phone locks, and a fight in progress,
    /// which the contest now writes into the state turn by turn and which nothing flushed to disk
    /// between turns. On iOS and Android the pause callback is the only reliable signal before a
    /// kill; quit is kept for the editor and desktop players.
    ///
    /// Nothing here decides what is saved. It asks the player to note the cell under their feet
    /// and hands the state to the same save every action uses.
    /// </summary>
    public sealed class PauseSave : MonoBehaviour
    {
        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveOnTheWayOut("pause");
            }
        }

        void OnApplicationQuit()
        {
            SaveOnTheWayOut("quit");
        }

        static void SaveOnTheWayOut(string reason)
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                player.RecordCurrentCell();
            }

            WorldRuntime.SaveNow();
            Debug.Log("[World] Saved on " + reason + " (day " + state.day + ").");
        }
    }
}
