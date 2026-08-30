using System.Collections.Generic;
using SheepGate.Core;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// Read-only helpers over <see cref="GameData.Dialogue"/>, plus the bookkeeping that lets the
    /// game know which conversations a player has already been through.
    ///
    /// Nothing here writes to disk, and every display name it returns is authored: from npcs.json
    /// for the builders who exist in the world, from the UI string table for the speakers who do
    /// not. A raw identifier is only ever returned when neither has an entry, which is a bug.
    ///
    /// WHICH NODE A RESIDENT SAYS TODAY IS NOT DECIDED HERE. That lives in
    /// <see cref="SheepGate.World.NpcActor"/>: it tries the follow-up beats first, then the
    /// "&lt;npc&gt;_d&lt;day&gt;" family of spellings, and finally falls back to the highest node
    /// whose day has already arrived, so a resident with nothing authored for today repeats their
    /// most recent line instead of going mute. That fallback is the season's coverage policy, not a
    /// safety net.
    ///
    /// This class used to carry a second resolver - NodeIdFor, TryGetNodeIdFor, NodeIdsForDay -
    /// with a different candidate list, no follow-up handling and no fallback, called by nothing.
    /// It was deleted rather than kept as a convenience precisely because it compiled, validated
    /// and read like the obvious place to change when the season grew past three days: editing it
    /// would have passed every gate and changed nothing a player sees. Day resolution belongs in
    /// NpcActor.ResolveNodeId. Do not grow a rival here.
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
        /// Speakers who talk but do not exist in the world as an NPC: the narrator, the neighbour
        /// before he is a persistent villager, the man from the capital, the crowd, and the two
        /// men who jeer from the road above without ever standing on the map.
        ///
        /// They have no entry in npcs.json because npcs.json describes people with a spawn point
        /// and a palette, so before this map they fell through to the raw id and the player read a
        /// Portuguese word in an English build. The keys are the ones the persisted versions of the
        /// same characters already use in <see cref="IntroCutscene"/>, deliberately: one string per
        /// character, so the bubble and the villager you talk to afterwards can never disagree.
        ///
        /// tools/validate-content.mjs reads this map and fails the build on a speaker that is in
        /// neither it nor npcs.json.
        /// </summary>
        static readonly Dictionary<string, string> SpeakerStringKeys = new Dictionary<string, string>
        {
            { "narrator", "world.speaker.narrator" },
            { "vizinho", "world.speaker.neighbour" },
            { "governador", "world.speaker.governor" },
            { "multidao", "world.speaker.crowd" },

            // Sanballat and Tobiah never get a spawn point or a palette, so they can never be in
            // npcs.json: they are heard from the road above and are never approached. Their names
            // belong in the string table for a second reason too - each locale spells them the way
            // its own translation does (Sambalate/Tobias in pt-BR), and a name hard-coded here
            // would put one language's spelling in front of every player.
            //
            // This mapping and the two ui.json entries behind it may only ever land together. The
            // validator enforces the interlock from both ends: check 10 fails on a key literal with
            // no ui.json entry, and check 9 fails on a dialogue speaker with no display name.
            { "sanballat", "world.speaker.sanballat" },
            { "tobiah", "world.speaker.tobiah" }
        };

        /// <summary>
        /// Display name for a speaker: npcs.json for the builders, the UI string table for everyone
        /// else. Falls back to the raw id so a missing entry shows up as an obvious authoring bug
        /// instead of an empty bubble.
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

            string stringKey;
            if (SpeakerStringKeys.TryGetValue(npcId, out stringKey))
            {
                return Loc.T(stringKey);
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
        /// derived on demand so no counter of "NPCs remaining" can leak into a UI. Rule 10 is the
        /// reason there is no getter here and no progress anywhere: the player is told they have
        /// become something, never how close they are to becoming it.
        ///
        /// THE ROSTER IS FIXED FOR THE WHOLE SEASON, WHICH IS WHY THIS NEEDED NO STAGE ARGUMENT.
        /// npcs.json is one flat list with a spawn point per entry and no day on any of them, so
        /// every resident stands in the village from the first stage and none arrives later. "Every
        /// NPC" therefore means the same six people on stage 9 as on stage 1, and the grant can
        /// fire on whichever stage the player finally gets round to the last conversation. If a
        /// villager who only appears partway through the season is ever added, this becomes wrong
        /// in a way nothing here can detect - the honest fix would be a day on NpcDef and a filter
        /// on it, never a remembered count of who is left.
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
    }
}
