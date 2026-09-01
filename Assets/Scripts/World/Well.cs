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

            int attempts = state.Counter(AttemptsKey) + 1;
            state.counters[AttemptsKey] = attempts;

            if (attempts < AttemptsBeforeCatch)
            {
                // Authored content names these per day and attempt (well_d2_1), matching the
                // <npc>_d<day> convention used everywhere else; the older well_miss_* names are
                // kept as fallbacks so either naming resolves.
                string missNode = WorldRuntime.FirstExistingNode(
                    "well_d" + day + "_" + attempts,
                    "well_d2_" + attempts,
                    "well_miss_" + attempts,
                    "well_miss",
                    "well_try");

                if (!string.IsNullOrEmpty(missNode))
                {
                    WorldRuntime.PlayDialogue(missNode);
                }
                else
                {
                    Debug.LogWarning("[World] No failed-attempt node authored for the well; attempt " + attempts + " passed silently.");
                }

                WorldRuntime.SaveNow();
                return;
            }

            state.SetFlag(WorldRuntime.FlagFishCaught);
            WorldRuntime.AwardOnce("exile_fish_awarded", WorldRuntime.VocationExile, 2);

            // The catch is the third attempt's node, and it is the one carrying JHN.21.6.
            string catchNode = WorldRuntime.FirstExistingNode(
                "well_d" + day + "_" + AttemptsBeforeCatch,
                "well_d2_" + AttemptsBeforeCatch,
                "well_fish",
                "well_catch",
                "well");
            if (!string.IsNullOrEmpty(catchNode))
            {
                WorldRuntime.PlayDialogue(catchNode);
            }
            else
            {
                Debug.LogWarning("[World] No \"well_fish\" dialogue node is authored; the catch happened without its line.");
            }

            WorldRuntime.SaveNow();
        }
    }
}
