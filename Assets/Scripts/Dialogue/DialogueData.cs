using System;
using System.Collections.Generic;
using SheepGate.Core;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// Read-only helpers over <see cref="GameData.Dialogue"/>, plus the bookkeeping that lets the
    /// game know which conversations a player has already been through.
    ///
    /// Nothing here writes to disk and nothing here is player facing: every string this class
    /// returns is either an authored display name coming from npcs.json or a raw identifier.
    /// </summary>
    public static class DialogueData
    {
        /// <summary>
        /// Counter key prefix used to remember how many times a node has been played to the end.
        /// Lives in <see cref="GameState.counters"/> so it survives a save/load round trip.
        /// </summary>
        public const string SeenCounterPrefix = "dialogue_seen:";

        private static readonly Dictionary<string, DialogueNode> EmptyNodes = new Dictionary<string, DialogueNode>();

        /// <summary>Every authored node, keyed by node id. Never null, even before GameData.LoadAll.</summary>
        public static IReadOnlyDictionary<string, DialogueNode> All
        {
            get
            {
                IReadOnlyDictionary<string, DialogueNode> nodes = GameData.Dialogue;
                return nodes ?? EmptyNodes;
            }
        }

        public static bool TryGetNode(string nodeId, out DialogueNode node)
        {
            node = null;
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            return All.TryGetValue(nodeId, out node) && node != null;
        }

        /// <summary>Returns the node, or null when the id is unknown.</summary>
        public static DialogueNode GetNode(string nodeId)
        {
            DialogueNode node;
            return TryGetNode(nodeId, out node) ? node : null;
        }

        /// <summary>
        /// Resolves the node an NPC should say on a given day. Prefers the authoring convention
        /// "&lt;npc&gt;_d&lt;day&gt;" and otherwise scans in ordinal key order so the result is stable.
        /// Returns null when that NPC has nothing to say that day.
        /// </summary>
        public static string NodeIdFor(string npcId, int day)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return null;
            }

            string conventional = npcId + "_d" + day;
            DialogueNode direct;
            if (All.TryGetValue(conventional, out direct) && direct != null && Matches(direct, npcId, day))
            {
                return conventional;
            }

            List<string> keys = SortedNodeIds();
            for (int i = 0; i < keys.Count; i++)
            {
                DialogueNode node;
                if (All.TryGetValue(keys[i], out node) && Matches(node, npcId, day))
                {
                    return keys[i];
                }
            }

            return null;
        }

        public static bool TryGetNodeIdFor(string npcId, int day, out string nodeId)
        {
            nodeId = NodeIdFor(npcId, day);
            return !string.IsNullOrEmpty(nodeId);
        }

        /// <summary>Every node authored for a day, in ordinal key order.</summary>
        public static List<string> NodeIdsForDay(int day)
        {
            List<string> result = new List<string>();
            List<string> keys = SortedNodeIds();
            for (int i = 0; i < keys.Count; i++)
            {
                DialogueNode node;
                if (All.TryGetValue(keys[i], out node) && node != null && node.day == day)
                {
                    result.Add(keys[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// Display name for an NPC, taken from npcs.json. Falls back to the raw id so a missing
        /// entry shows up as an obvious authoring bug instead of an empty bubble.
        /// </summary>
        public static string DisplayNameOf(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return string.Empty;
            }

            NpcDef[] npcs = GameData.Npcs;
            if (npcs != null)
            {
                for (int i = 0; i < npcs.Length; i++)
                {
                    NpcDef npc = npcs[i];
                    if (npc != null && npc.id == npcId && !string.IsNullOrEmpty(npc.display))
                    {
                        return npc.display;
                    }
                }
            }

            return npcId;
        }

        public static string SeenCounterKey(string nodeId)
        {
            return SeenCounterPrefix + (nodeId ?? string.Empty);
        }

        /// <summary>How many times this node has been played through to the end.</summary>
        public static int TimesSeen(GameState state, string nodeId)
        {
            if (state == null || string.IsNullOrEmpty(nodeId))
            {
                return 0;
            }

            return state.Counter(SeenCounterKey(nodeId));
        }

        /// <summary>Records one completed playthrough of a node.</summary>
        public static void RecordSeen(GameState state, string nodeId)
        {
            if (state == null || string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            state.Bump(SeenCounterKey(nodeId));
        }

        /// <summary>True once any node belonging to this NPC has been played to the end.</summary>
        public static bool HasSpokenWith(GameState state, string npcId)
        {
            if (state == null || string.IsNullOrEmpty(npcId))
            {
                return false;
            }

            foreach (KeyValuePair<string, DialogueNode> pair in All)
            {
                DialogueNode node = pair.Value;
                if (node == null || node.npc != npcId)
                {
                    continue;
                }

                if (TimesSeen(state, pair.Key) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the player has finished at least one conversation with every NPC in npcs.json.
        /// This is the condition behind the scribe grant for talking to everyone; it is deliberately
        /// derived on demand so no counter of "NPCs remaining" can leak into a UI.
        /// </summary>
        public static bool HasSpokenWithEveryNpc(GameState state)
        {
            if (state == null)
            {
                return false;
            }

            NpcDef[] npcs = GameData.Npcs;
            if (npcs == null || npcs.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < npcs.Length; i++)
            {
                NpcDef npc = npcs[i];
                if (npc == null || string.IsNullOrEmpty(npc.id))
                {
                    continue;
                }

                if (!HasSpokenWith(state, npc.id))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// AUTHORING METADATA ONLY — NEVER SURFACE THIS TO THE PLAYER.
        ///
        /// On day 2 two NPCs contradict each other and exactly one of them is right. The whole point
        /// of that day is that the game does not tell you which. No icon, no colour, no tooltip, no
        /// bubble decoration, nothing derived from this value may reach the screen. It exists so
        /// content tooling and tests can assert the authored setup, and for nothing else.
        /// </summary>
        internal static bool IsReliable(string nodeId)
        {
            DialogueNode node;
            return TryGetNode(nodeId, out node) && node.reliable;
        }

        private static bool Matches(DialogueNode node, string npcId, int day)
        {
            return node != null && node.npc == npcId && node.day == day;
        }

        private static List<string> SortedNodeIds()
        {
            List<string> keys = new List<string>(All.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
    }
}
