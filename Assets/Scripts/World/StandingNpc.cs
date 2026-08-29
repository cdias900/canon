using System;
using SheepGate.Dialogue;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// Someone the opening leaves behind: the neighbour, the people at the gathering, the man on
    /// the stone. They stay in the village after the cutscene ends and answer when tapped.
    ///
    /// Separate from <see cref="NpcActor"/>, which resolves a node per day from npcs.json and
    /// carries the vocation bookkeeping for the six named residents. These are scenery that talks:
    /// one node, no day logic, no scoring. Making them NpcActors would put them in the "talked to
    /// everyone" count and quietly change the scribe vocation.
    ///
    /// Attached to a GameObject that already has its visuals, so it adds a tap target and nothing
    /// else.
    /// </summary>
    public sealed class StandingNpc : InteractableBase
    {
        string _nodeId;

        public static StandingNpc AttachTo(GameObject host, TilemapBuilder tilemap, Vector2Int cell,
                                           string nodeId, string displayName)
        {
            if (host == null)
            {
                return null;
            }

            StandingNpc npc = host.GetComponent<StandingNpc>();
            if (npc == null)
            {
                npc = host.AddComponent<StandingNpc>();
            }

            npc._nodeId = nodeId;
            npc.DisplayName = displayName;

            // Placed without blocking the cell: a crowd the player cannot walk through would turn
            // the square into a maze on the way to the wall.
            npc.Place(tilemap, cell, false, 0f);
            return npc;
        }

        public override void Interact()
        {
            if (string.IsNullOrEmpty(_nodeId))
            {
                return;
            }

            DialogueSystem dialogue = WorldRuntime.FindDialogueSystem();
            if (dialogue == null || dialogue.IsPlaying)
            {
                return;
            }

            try
            {
                dialogue.Play(_nodeId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] StandingNpc could not play \"" + _nodeId + "\": " + exception.Message);
            }
        }
    }
}
