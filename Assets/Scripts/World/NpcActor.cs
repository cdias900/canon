using System;
using System.Collections.Generic;
using UnityEngine;
using SheepGate.Core;
using SheepGate.Dialogue;

namespace SheepGate.World
{
    /// <summary>
    /// A resident standing in the village. Tapping one plays the dialogue node authored for that
    /// resident on the current day; everything the conversation grants is applied by the dialogue
    /// layer. What this class records is mechanical: who the player actually finished talking to,
    /// which feeds the scribe and shepherd vocations and the contest's "call the others" move.
    ///
    /// Only references travel through here (for example the source reference on NpcDef). No verse
    /// text is ever built in code.
    /// </summary>
    public sealed class NpcActor : InteractableBase
    {
        private const string TalkedPrefix = "talked_";
        private const string PlayedPrefix = "dialogue_played_";
        private const string DistinctTalkedKey = "npcs_talked";
        private const int FallbackVillageSize = 6;

        private static readonly string ShepherdFirstNpc = "hananias";
        private static readonly string ShepherdSecondNpc = "salum";

        private static readonly Color[] PaletteColors =
        {
            new Color(0.72f, 0.60f, 0.48f, 1f),
            new Color(0.55f, 0.47f, 0.40f, 1f),
            new Color(0.45f, 0.52f, 0.55f, 1f),
            new Color(0.63f, 0.55f, 0.42f, 1f),
            new Color(0.50f, 0.44f, 0.48f, 1f),
            new Color(0.58f, 0.52f, 0.46f, 1f)
        };

        public string NpcId { get; private set; }

        /// <summary>Reference the resident's name comes from, such as NEH.3.8. Reference only.</summary>
        public string SourceRef { get; private set; }

        private DialogueSystem _subscribedSystem;
        private string _pendingNodeId;

        public static NpcActor Spawn(NpcDef def, Transform parent, TilemapBuilder tilemap)
        {
            if (def == null)
            {
                return null;
            }

            GameObject actorObject = new GameObject("Npc_" + def.id);
            if (parent != null)
            {
                actorObject.transform.SetParent(parent, false);
            }

            NpcActor actor = actorObject.AddComponent<NpcActor>();
            actor.Configure(def);

            Vector2Int cell = new Vector2Int(def.spawn != null ? def.spawn.x : 0, def.spawn != null ? def.spawn.y : 0);
            if (tilemap != null)
            {
                cell = tilemap.NearestWalkable(tilemap.ClampCell(cell));
            }

            actor.Place(tilemap, cell, true, 0.25f);
            actor.BuildSprites(tilemap != null ? tilemap.Height : 0, cell.y);
            return actor;
        }

        private void Configure(NpcDef def)
        {
            NpcId = def.id;
            SourceRef = def.source_ref;
            DisplayName = string.IsNullOrEmpty(def.display) ? def.id : def.display;
            _paletteKey = def.palette;
        }

        private string _paletteKey;

        private void BuildSprites(int mapHeight, int cellY)
        {
            int order = WorldRuntime.SortingOrderForCell(mapHeight, cellY);
            Color tint = ColorForPalette(_paletteKey, NpcId);

            int bodyIndex = StableIndex(NpcId, 2);
            int topIndex = StableIndex(NpcId + "_top", 4);
            int legsIndex = StableIndex(NpcId + "_legs", 4);

            AddLayer("Legs", WorldRuntime.FirstSprite("legs_" + legsIndex), order + 1, tint * 0.85f);
            AddLayer("Body", WorldRuntime.FirstSprite(
                "body_" + bodyIndex + "_down_0",
                "body_" + bodyIndex + "_down",
                "body_" + bodyIndex + "_0",
                "body_" + bodyIndex), order + 2, tint);
            AddLayer("Top", WorldRuntime.FirstSprite("top_" + topIndex), order + 3, tint * 0.92f);
        }

        private void AddLayer(string layerName, Sprite sprite, int sortingOrder, Color tint)
        {
            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(transform, false);
            SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = sortingOrder;

            if (sprite != null)
            {
                renderer.sprite = sprite;
                renderer.color = new Color(tint.r, tint.g, tint.b, 1f);
            }
            else
            {
                renderer.sprite = WorldRuntime.SolidSprite(new Color(tint.r, tint.g, tint.b, 1f));
                renderer.enabled = layerName == "Body";
            }
        }

        private static Color ColorForPalette(string palette, string fallbackSeed)
        {
            string seed = string.IsNullOrEmpty(palette) ? fallbackSeed : palette;
            return PaletteColors[StableIndex(seed, PaletteColors.Length)];
        }

        private static int StableIndex(string seed, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (string.IsNullOrEmpty(seed))
            {
                return 0;
            }

            int hash = 17;
            for (int i = 0; i < seed.Length; i++)
            {
                hash = unchecked(hash * 31 + seed[i]);
            }

            return Mathf.Abs(hash) % count;
        }

        public override void Interact()
        {
            GameState state = WorldRuntime.State;
            int day = state != null ? state.day : 1;

            DialogueSystem dialogue = WorldRuntime.FindDialogueSystem();
            if (dialogue == null)
            {
                Debug.LogWarning("[World] No DialogueSystem in the scene; \"" + NpcId + "\" has nothing to say.");
                return;
            }

            if (dialogue.IsPlaying)
            {
                return;
            }

            string nodeId = ResolveNodeId(day);
            if (string.IsNullOrEmpty(nodeId))
            {
                Debug.LogWarning("[World] No dialogue node authored for npc \"" + NpcId + "\" on day " + day + ".");
                return;
            }

            Unsubscribe();
            _pendingNodeId = nodeId;
            _subscribedSystem = dialogue;
            dialogue.NodeFinished += OnNodeFinished;

            try
            {
                dialogue.Play(nodeId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] DialogueSystem.Play(\"" + nodeId + "\") failed: " + exception.Message);
                Unsubscribe();
            }
        }

        private string ResolveNodeId(int day)
        {
            string direct = WorldRuntime.FirstExistingNode(
                NpcId + "_d" + day,
                NpcId + "_day" + day,
                NpcId + "_" + day,
                NpcId);

            if (!string.IsNullOrEmpty(direct))
            {
                return direct;
            }

            // Fall back to any node authored for this resident, preferring the latest day that has
            // already arrived, so a missing node never leaves a resident mute.
            IReadOnlyDictionary<string, DialogueNode> nodes = null;
            try
            {
                nodes = GameData.Dialogue;
            }
            catch (Exception)
            {
                nodes = null;
            }

            if (nodes == null)
            {
                return null;
            }

            string best = null;
            int bestDay = int.MinValue;
            foreach (KeyValuePair<string, DialogueNode> entry in nodes)
            {
                DialogueNode node = entry.Value;
                if (node == null || node.npc != NpcId)
                {
                    continue;
                }

                if (node.day > day)
                {
                    continue;
                }

                if (node.day > bestDay)
                {
                    bestDay = node.day;
                    best = entry.Key;
                }
            }

            return best;
        }

        private void OnNodeFinished(string finishedNodeId)
        {
            string nodeId = _pendingNodeId;
            if (string.IsNullOrEmpty(nodeId))
            {
                Unsubscribe();
                return;
            }

            // Another node finishing first is not this conversation; keep waiting for ours.
            if (!string.IsNullOrEmpty(finishedNodeId) && finishedNodeId != nodeId)
            {
                return;
            }

            Unsubscribe();
            RecordConversation(nodeId);
        }

        private void RecordConversation(string nodeId)
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            int day = state.day;

            string playedKey = PlayedPrefix + nodeId;
            int timesPlayed = state.Counter(playedKey);
            state.counters[playedKey] = timesPlayed + 1;
            if (timesPlayed > 0)
            {
                // Reading a conversation a second time.
                WorldRuntime.AwardOnce("scribe_reread_awarded", WorldRuntime.VocationScribe, 2);
            }

            string talkedKey = TalkedPrefix + NpcId;
            if (state.Counter(talkedKey) == 0)
            {
                state.counters[talkedKey] = 1;
                state.Bump(DistinctTalkedKey);
                if (state.Counter(DistinctTalkedKey) >= VillageSize())
                {
                    WorldRuntime.AwardOnce("scribe_all_npcs_awarded", WorldRuntime.VocationScribe, 2);
                }
            }

            state.counters[TalkedPrefix + NpcId + "_d" + day] = 1;

            if (TalkedOnBothDays(state, ShepherdFirstNpc) && TalkedOnBothDays(state, ShepherdSecondNpc))
            {
                WorldRuntime.AwardOnce("shepherd_pair_awarded", WorldRuntime.VocationShepherd, 2);
            }

            WorldRuntime.SaveNow();
        }

        /// <summary>How many residents the village has, so the scribe rule follows the content.</summary>
        private static int VillageSize()
        {
            try
            {
                NpcDef[] defs = GameData.Npcs;
                if (defs != null && defs.Length > 0)
                {
                    return defs.Length;
                }
            }
            catch (Exception)
            {
                // Fall through to the authored village size.
            }

            return FallbackVillageSize;
        }

        private static bool TalkedOnBothDays(GameState state, string npcId)
        {
            return state.Counter(TalkedPrefix + npcId + "_d1") > 0
                   && state.Counter(TalkedPrefix + npcId + "_d2") > 0;
        }

        private void Unsubscribe()
        {
            if (_subscribedSystem != null)
            {
                _subscribedSystem.NodeFinished -= OnNodeFinished;
                _subscribedSystem = null;
            }

            _pendingNodeId = null;
        }

        protected override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }
    }
}
