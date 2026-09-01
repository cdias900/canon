using System;
using UnityEngine;
using SheepGate.Core;
using SheepGate.UI;

namespace SheepGate.World
{
    /// <summary>
    /// The well. Fishing it fails twice; on the third try the hint lands and the fish is caught.
    ///
    /// The hint itself is an authored dialogue node (by convention "well_fish", which carries the
    /// JHN.21.6 reference), so the text is resolved by the dialogue and scripture layers and never
    /// assembled here.
    ///
    /// The three attempts are a run-wide count, not a per-day one, and only a day with authored
    /// lines can spend one — so the beat happens once in a season, on whichever stage the content
    /// actually lives on, and never as a silent counter ticking somewhere else.
    /// </summary>
    public sealed class Well : InteractableBase
    {
        public const int AttemptsBeforeCatch = 3;
        private const string AttemptsKey = "well_attempts";
        private const string AlreadyCaughtKey = "toast.well.already_caught";

        public static Well Spawn(Vector2Int cell, Transform parent, TilemapBuilder tilemap)
        {
            GameObject wellObject = new GameObject("Well");
            if (parent != null)
            {
                wellObject.transform.SetParent(parent, false);
            }

            Well well = wellObject.AddComponent<Well>();
            well.DisplayName = Loc.T("world.well");
            well.Place(tilemap, cell, true, 0f);
            well.BuildSprite(tilemap != null ? tilemap.Height : 0, cell.y);
            return well;
        }

        private void BuildSprite(int mapHeight, int cellY)
        {
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = WorldRuntime.SortingOrderForCell(mapHeight, cellY);

            Sprite sprite = WorldRuntime.GetSprite("prop_well");
            renderer.sprite = sprite != null ? sprite : WorldRuntime.SolidSprite(new Color(0.33f, 0.38f, 0.42f, 1f));
        }

        public override void Interact()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            int day = state.day;

            if (state.HasFlag(WorldRuntime.FlagFishCaught))
            {
                string afterNode = WorldRuntime.FirstExistingNode("well_after", "well_caught", "well_d" + day, "well");
                if (!string.IsNullOrEmpty(afterNode))
                {
                    WorldRuntime.PlayDialogue(afterNode);
                }
                else
                {
                    // The authored follow-up is the good answer; this is the fallback for a build
                    // where it is missing, and it says so on screen rather than leaving the well
                    // looking broken.
                    Toast.Show(Loc.T(AlreadyCaughtKey));
                    Debug.Log("[World] The fish is already caught and no follow-up node is authored for the well.");
                }

                return;
            }

            // The attempt is resolved before it is spent, and spent only if it has something to say.
            //
            // That ordering is what the day-specific lookup below costs. With a borrowed fallback in
            // the chain there was always a line, so a counter written first could never be written
            // for nothing; without one, a stage with no well content would have burned attempts in
            // silence and — on the third — set the fish caught with no line at all, quietly spending
            // the one beat in the game that carries JHN.21.6.
            int attempts = state.Counter(AttemptsKey) + 1;

            if (attempts < AttemptsBeforeCatch)
            {
                // Authored content names these per day and attempt (well_d2_1), matching the
                // <npc>_d<day> convention used everywhere else; the older well_miss_* names are
                // kept as fallbacks so either naming resolves.
                //
                // What is deliberately NOT in this list any more is a hard-coded well_d2_ rung. It
                // meant every day that had nothing authored for the well silently borrowed the
                // second day's lines, so the beat that carries JHN.21.6 read the same wherever the
                // player happened to fish. The generalised rung above already scales to any stage
                // that gets its own lines; the day-agnostic names below are the deliberate catch-all
                // for the ones that do not.
                string missNode = WorldRuntime.FirstExistingNode(
                    "well_d" + day + "_" + attempts,
                    "well_miss_" + attempts,
                    "well_miss",
                    "well_try");

                if (string.IsNullOrEmpty(missNode))
                {
                    Debug.Log("[World] Nothing is authored for the well on day " + day
                              + "; the attempt was not spent.");
                    return;
                }

                state.counters[AttemptsKey] = attempts;
                WorldRuntime.PlayDialogue(missNode);
                WorldRuntime.SaveNow();
                return;
            }

            // The catch is the third attempt's node, and it is the one carrying JHN.21.6. Same rule
            // as the misses above: this stage's own node, then the day-agnostic names, and never a
            // borrowed one from a stage that happens to have content.
            string catchNode = WorldRuntime.FirstExistingNode(
                "well_d" + day + "_" + AttemptsBeforeCatch,
                "well_fish",
                "well_catch",
                "well");

            if (string.IsNullOrEmpty(catchNode))
            {
                Debug.LogWarning("[World] No catch node is authored for the well on day " + day
                                 + "; the fish stays in the water rather than being caught off screen.");
                return;
            }

            state.counters[AttemptsKey] = attempts;
            state.SetFlag(WorldRuntime.FlagFishCaught);
            WorldRuntime.AwardOnce("exile_fish_awarded", WorldRuntime.VocationExile, 2);
            WorldRuntime.PlayDialogue(catchNode);
            WorldRuntime.SaveNow();
        }
    }
}
