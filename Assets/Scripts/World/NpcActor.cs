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

        private static readonly string ShepherdFirstNpc = "hananias";
        private static readonly string ShepherdSecondNpc = "salum";

        // The day-2 invitation, authored in dialogue.json. Malquias hands over the letter and the
        // branch the player takes is what raises accepted_invite or refused_invite — the two flags
        // the day-3 contest reads to decide how hard the night is going to be.
        private const string InviteNpcId = "malquias";
        private const int InviteDay = 2;
        private const string InviteAcceptNodeId = "malquias_d2_accept";
        private const string InviteRefuseNodeId = "malquias_d2_refuse";
        private const string InviteReturnNodeId = "malquias_d2_return";
        private const string InviteDaySpentKey = "invite_day_spent";

        // Handing a resident material scores the shepherd. What changes hands is stone - the raw
        // material, not a finished block: a neighbour who needs to patch a doorway needs stone, and
        // charging a block would make the kindest move in the day cost the most.
        //
        // The amount mirrors "requires_rubble" on the donation branch authored in dialogue.json.
        // That JSON key, and the "donated_rubble" flag the branch sets, keep the old spelling on
        // purpose: both are save state and authored content, and renaming either would strand every
        // run in flight. requires_rubble is checked against the same count this spends.
        private const string DonationNodeId = "hananias_d2_donate";
        private const string DonationPaidKey = "shepherd_donation_paid";
        private const int DonationStoneCost = 3;

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

        /// <summary>
        /// Puts back the hold on the end of the day when this scene was rebuilt in the middle of
        /// the trip down the valley — a language switch, a relaunch. The hold is derived from the
        /// run rather than remembered, so it cannot survive the beat it belongs to.
        /// </summary>
        private void Start()
        {
            if (!OwesTheReturn())
            {
                return;
            }

            DayCycle cycle = DayCycle.Find();
            if (cycle != null)
            {
                cycle.HoldDusk(DayCycle.HoldPendingBeat);
            }
        }

        /// <summary>
        /// True while this resident sent the player down the valley today and has not yet said the
        /// other half of it. The trip spends the whole day's capacity, so without this the daylight
        /// clock would take the village to dusk the moment the player got back — and the line that
        /// makes the trip mean anything would never be said.
        /// </summary>
        private bool OwesTheReturn()
        {
            if (NpcId != InviteNpcId)
            {
                return false;
            }

            GameState state = WorldRuntime.State;
            if (state == null || state.day != InviteDay || !state.HasFlag(GameFlags.AcceptedInvite))
            {
                return false;
            }

            if (string.IsNullOrEmpty(WorldRuntime.FirstExistingNode(InviteReturnNodeId)))
            {
                // Nothing authored to come back for; holding the day open would strand it.
                return false;
            }

            return DialogueData.TimesSeen(state, InviteReturnNodeId) == 0;
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

            EnsureSubscribed(dialogue);

            try
            {
                dialogue.Play(nodeId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] DialogueSystem.Play(\"" + nodeId + "\") failed: " + exception.Message);
            }
        }

        private string ResolveNodeId(int day)
        {
            string followUp = ResolveFollowUpNodeId(day);
            if (!string.IsNullOrEmpty(followUp))
            {
                return followUp;
            }

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

        /// <summary>
        /// Once a decision has been made, this resident has the follow-up to say and not the
        /// question again. Returns null whenever the ordinary per-day node is the right one.
        /// </summary>
        private string ResolveFollowUpNodeId(int day)
        {
            if (NpcId != InviteNpcId || day != InviteDay)
            {
                return null;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return null;
            }

            if (state.HasFlag(GameFlags.AcceptedInvite))
            {
                return WorldRuntime.FirstExistingNode(InviteReturnNodeId);
            }

            if (state.HasFlag(GameFlags.RefusedInvite))
            {
                return WorldRuntime.FirstExistingNode(InviteRefuseNodeId);
            }

            return null;
        }

        private void EnsureSubscribed(DialogueSystem dialogue)
        {
            if (_subscribedSystem == dialogue)
            {
                return;
            }

            Unsubscribe();
            _subscribedSystem = dialogue;
            dialogue.NodeFinished += OnNodeFinished;
        }

        /// <summary>
        /// Fires for every node the dialogue system finishes, the ones a branch leads to included.
        /// Only what this resident actually says is ours to record and to act on.
        /// </summary>
        private void OnNodeFinished(string finishedNodeId)
        {
            if (string.IsNullOrEmpty(finishedNodeId) || !Speaks(finishedNodeId))
            {
                return;
            }

            RecordConversation(finishedNodeId);
            ApplyNodeConsequences(finishedNodeId);
        }

        private bool Speaks(string nodeId)
        {
            DialogueNode node = DialogueData.GetNode(nodeId);
            return node != null && node.npc == NpcId;
        }

        /// <summary>
        /// What a finished conversation costs in the world. The dialogue layer owns points and
        /// flags; capacity and material are the world's to move.
        /// </summary>
        private void ApplyNodeConsequences(string nodeId)
        {
            if (nodeId == InviteAcceptNodeId)
            {
                SpendDayOnTheInvitation();
                return;
            }

            if (nodeId == InviteReturnNodeId)
            {
                // The day is his to give back: he took it when he sent the player away.
                SetDuskHold(false);
                return;
            }

            if (nodeId == DonationNodeId)
            {
                TakeDonatedStone();
            }
        }

        /// <summary>
        /// Going down the valley costs the whole day: whatever work capacity was left goes with it
        /// and the stretch nobody covered loses the work in progress on it. The day is not ended
        /// here on purpose — the player walks back into the village, and the resident who handed
        /// over the letter still has something to say about the trip.
        /// </summary>
        private void SpendDayOnTheInvitation()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            if (state.Counter(InviteDaySpentKey) != 0)
            {
                return;
            }

            state.counters[InviteDaySpentKey] = 1;

            // The capacity is about to go to zero, which is what the daylight clock reads as the
            // end of a day. Hold it: the player still has to walk back and hear the rest of this.
            SetDuskHold(true);

            int remaining = state.workCapacity;
            ResourceSystem resources = ResourceSystem.Find();
            if (resources != null)
            {
                if (remaining > 0)
                {
                    resources.Spend(remaining);
                }
            }
            else
            {
                state.workCapacity = 0;
            }

            // DamageSegment clears work in progress and never a finished stage: a day away can
            // cost tomorrow, never yesterday.
            WallSystem wall = FindFirstObjectByType<WallSystem>();
            if (wall != null)
            {
                // Resolved from the data, never a literal id: the night, the trial and the day away
                // must all punish the same segment, or re-authoring wall_segments.json quietly makes
                // the invitation free. DamageSegment on an unknown id only warns, so a hardcoded id
                // that drifts fails silently — which is the worst way for a cost to disappear.
                string exposed = ResolveExposedSegmentId(wall);
                if (!string.IsNullOrEmpty(exposed))
                {
                    wall.DamageSegment(exposed);
                }
                else
                {
                    Debug.LogWarning("[World] No wall segment could be resolved; the day away cost nothing.");
                }
            }
            else
            {
                Debug.LogWarning("[World] No WallSystem in the scene; the day away damaged nothing.");
            }

            WorldRuntime.SaveNow();
        }

        /// <summary>
        /// The segment a day away costs, taken from the data rather than named in code. Mirrors
        /// MoraleContest's resolver so the night, the trial and the invitation all agree.
        /// </summary>
        private static string ResolveExposedSegmentId(WallSystem wall)
        {
            if (wall != null)
            {
                try
                {
                    string primary = wall.PrimaryExposedSegmentId;
                    if (!string.IsNullOrEmpty(primary))
                    {
                        return primary;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] Reading the wall's exposed segment failed: " + exception.Message);
                }
            }

            WallSegmentDef[] defs = null;
            try
            {
                defs = GameData.WallSegments;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Could not read wall_segments.json: " + exception.Message);
            }

            if (defs != null)
            {
                for (int i = 0; i < defs.Length; i++)
                {
                    if (defs[i] != null && defs[i].exposed && !string.IsNullOrEmpty(defs[i].id))
                    {
                        return defs[i].id;
                    }
                }

                for (int i = 0; i < defs.Length; i++)
                {
                    if (defs[i] != null && !string.IsNullOrEmpty(defs[i].id))
                    {
                        return defs[i].id;
                    }
                }
            }

            return null;
        }

        /// <summary>Material actually leaves the player's hands. Charged once.</summary>
        private void TakeDonatedStone()
        {
            GameState state = WorldRuntime.State;
            if (state == null || state.Counter(DonationPaidKey) != 0)
            {
                return;
            }

            state.counters[DonationPaidKey] = 1;

            ResourceSystem resources = ResourceSystem.Find();
            if (resources != null)
            {
                // A negative amount is how a donation is paid; AddStone never takes it below zero.
                resources.AddStone(-DonationStoneCost);
            }
            else
            {
                state.stone = Mathf.Max(0, state.stone - DonationStoneCost);
            }

            WorldRuntime.SaveNow();
        }

        /// <summary>Takes or gives back the hold that keeps the day from ending on this beat.</summary>
        private static void SetDuskHold(bool held)
        {
            DayCycle cycle = DayCycle.Find();
            if (cycle == null)
            {
                Debug.LogWarning("[World] No DayCycle in the scene; the day could not be held open for the trip down the valley.");
                return;
            }

            if (held)
            {
                cycle.HoldDusk(DayCycle.HoldPendingBeat);
            }
            else
            {
                cycle.ReleaseDusk(DayCycle.HoldPendingBeat);
            }
        }

        private void RecordConversation(string nodeId)
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            int day = state.day;

            // Re-reading a conversation and having spoken with the whole village both score the
            // scribe, and both are granted by DialogueSystem, which owns node completion. Scoring
            // them here as well paid the scribe twice for one action.
            string playedKey = PlayedPrefix + nodeId;
            state.counters[playedKey] = state.Counter(playedKey) + 1;

            string talkedKey = TalkedPrefix + NpcId;
            if (state.Counter(talkedKey) == 0)
            {
                state.counters[talkedKey] = 1;
                state.Bump(DistinctTalkedKey);
            }

            state.counters[TalkedPrefix + NpcId + "_d" + day] = 1;

            if (TalkedOnBothDays(state, ShepherdFirstNpc) && TalkedOnBothDays(state, ShepherdSecondNpc))
            {
                WorldRuntime.AwardOnce("shepherd_pair_awarded", WorldRuntime.VocationShepherd, 2);
            }

            WorldRuntime.SaveNow();
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
        }

        protected override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }
    }
}
