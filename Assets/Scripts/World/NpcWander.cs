using UnityEngine;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Player;

namespace SheepGate.World
{
    /// <summary>
    /// Walks a resident around the patch of ground they were placed on, so the village reads as
    /// inhabited rather than staged.
    ///
    /// Three properties are what make wandering safe in a game whose whole verb is tapping people:
    ///
    /// <list type="bullet">
    ///   <item><b>Nobody wanders off.</b> Every step stays within <see cref="HomeRadius"/> cells of
    ///   the spawn cell, so a resident the player went looking for is where they were left, give or
    ///   take a couple of tiles. A village where the person you need has walked to the other side
    ///   of the map is a search task, and nobody asked for one.</item>
    ///   <item><b>Nobody walks away from the player.</b> A resident stands still, and turns to face
    ///   them, while the player is inside <see cref="AttentionRadius"/>.
    ///   <c>PlayerController.ApproachAndInteract</c> reads the target's cell when the tap lands and
    ///   interacts on arrival without looking again, so a resident who kept moving would be spoken
    ///   to from across the square.</item>
    ///   <item><b>The walkable grid stays honest.</b> A resident stands on a cell marked unwalkable,
    ///   and this moves that hole with them: the destination is reserved before the step and the
    ///   cell behind is handed back on arrival. Reserving first is what keeps two residents from
    ///   choosing the same tile in the same frame.</item>
    /// </list>
    ///
    /// The village holds still while a conversation plays, while any modal owns the input, and from
    /// dusk onwards — the three moments when something else on screen is asking to be read. A step
    /// already under way finishes rather than freezing mid-tile: it looks better, and it is the only
    /// version that cannot leave a reserved cell stranded.
    ///
    /// Standing still is not the same as paying attention, and the difference is the whole reason
    /// this class turns anybody. While somebody is speaking the residents <b>face them</b>, and when
    /// the speech ends they <b>walk back to where they were standing</b> before drifting again — a
    /// crowd that scattered from wherever the speech happened to catch them would read as the game
    /// having forgotten it.
    ///
    /// Who they turn to depends on who is talking, and the distinction is deliberate:
    /// <list type="bullet">
    ///   <item>An ordinary conversation turns the neighbours within <see cref="ListeningRadius"/>.
    ///   The whole village craning at two people talking by a doorway would be a village with
    ///   nothing better to do.</item>
    ///   <item>An address turns <b>everyone</b>, at any distance, because that is what an address
    ///   is — the man on the stone is why the square filled up. Whoever stages one registers
    ///   themselves in <see cref="Speaker"/>; nothing infers it.</item>
    /// </list>
    ///
    /// Randomness is a <see cref="System.Random"/> seeded from the resident's id, not
    /// <c>UnityEngine.Random</c>. Two reasons, and the second is the one that matters: every
    /// resident gets their own rhythm for free, and the shared generator — which procedural art and
    /// the night resolution both draw from — is left exactly where it was.
    /// </summary>
    public sealed class NpcWander : MonoBehaviour
    {
        /// <summary>Cells from the spawn cell a resident may stray, measured per axis.</summary>
        public const int HomeRadius = 2;

        /// <summary>Roughly a third of the player's pace: a village at work, not a village late.</summary>
        public const float WalkSpeed = 1.15f;

        public const float MinPauseSeconds = 2.5f;
        public const float MaxPauseSeconds = 6.5f;

        /// <summary>How close the player has to be for a resident to stop and look at them.</summary>
        public const float AttentionRadius = 2.4f;

        /// <summary>How far an ordinary conversation carries. An address ignores this.</summary>
        public const float ListeningRadius = 7f;

        /// <summary>
        /// The wait before heading back after a speech. Short on purpose: the village going back to
        /// work is the beat that ends the scene, and a pause the length of an ordinary one would
        /// read as everybody standing around wondering what to do.
        /// </summary>
        public const float ReturnPauseSeconds = 0.35f;

        private const float ArriveEpsilon = 0.015f;

        /// <summary>How often the shared lookups are retried while they resolve to nothing.</summary>
        private const float ReacquireInterval = 1f;

        private static readonly Vector2Int[] Steps =
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0)
        };

        // Resolved once for the whole village rather than once per resident per frame. Unity's
        // null check reports a destroyed object as null, so a scene rebuild — a language switch,
        // a relaunch — re-resolves these rather than holding a dead reference.
        private static DialogueSystem _dialogue;
        private static DayCycle _cycle;
        private static PlayerController _player;

        // Two clocks rather than one: the player is looked up on a different question from the
        // systems that hold the village, and a single shared stamp let whichever asked first push
        // the other a second into the future — which is a second of residents walking through a
        // conversation.
        private static float _nextSystemsLookup;
        private static float _nextPlayerLookup;

        private static int _heldFrame = -1;
        private static bool _heldValue;

        private static int _focusFrame = -1;
        private static Transform _focusValue;
        private static bool _focusAddressesVillage;

        /// <summary>
        /// Someone addressing the whole village, set by whoever is staging the speech and cleared
        /// when it ends. It exists because an address is not something that can be inferred: the
        /// dialogue node knows which resident is speaking, but the man on the stone is not a
        /// resident, and the difference between "two people talking" and "everybody, listen" is a
        /// staging decision rather than a fact about the data.
        /// </summary>
        public static Transform Speaker { get; set; }

        private NpcActor _actor;
        private TilemapBuilder _map;
        private System.Random _random;

        private Vector2Int _home;
        private Vector2Int _cell;
        private Vector2Int _target;
        private Vector3 _targetPoint;
        private bool _walking;
        private float _pauseRemaining;
        private bool _heardSpeech;
        private bool _returning;

        /// <summary>
        /// True while the village is meant to stand still. Public because the end-to-end run has to
        /// be able to tell "nobody moved" from "nobody was allowed to move" when it reports.
        /// </summary>
        public static bool VillageIsHeld()
        {
            if (_heldFrame == Time.frameCount)
            {
                return _heldValue;
            }

            _heldFrame = Time.frameCount;
            _heldValue = ComputeHold();
            return _heldValue;
        }

        /// <summary>True while a conversation is on screen. Resolved once per frame for the village.</summary>
        private static bool SomeoneIsSpeaking()
        {
            ReacquireIfNeeded();
            return _dialogue != null && _dialogue.IsPlaying;
        }

        /// <summary>
        /// Whoever the village should be looking at this frame, or null when nobody can be
        /// resolved — in which case residents simply stand still, which is what they did before
        /// any of this existed.
        ///
        /// A registered <see cref="Speaker"/> wins over the dialogue node. The man on the stone is
        /// not a resident and has no NpcActor to find, and when he is speaking there is nobody else
        /// worth turning to.
        /// </summary>
        private static Transform Focus
        {
            get
            {
                EnsureFocus();
                return _focusValue;
            }
        }

        private static void EnsureFocus()
        {
            if (_focusFrame == Time.frameCount)
            {
                return;
            }

            _focusFrame = Time.frameCount;

            if (Speaker != null)
            {
                _focusValue = Speaker;
                _focusAddressesVillage = true;
                return;
            }

            _focusValue = null;
            _focusAddressesVillage = false;

            if (_dialogue == null)
            {
                return;
            }

            DialogueNode node = DialogueData.GetNode(_dialogue.CurrentNodeId);
            if (node == null || string.IsNullOrEmpty(node.npc))
            {
                return;
            }

            _focusValue = FindResident(node.npc);
        }

        /// <summary>
        /// The resident with this id, from the interactable registry rather than a scene scan: the
        /// registry is already the list of everyone tappable, and this runs at most once a frame.
        /// </summary>
        private static Transform FindResident(string npcId)
        {
            System.Collections.Generic.IReadOnlyList<InteractableBase> all = InteractableBase.All;
            for (int i = 0; i < all.Count; i++)
            {
                NpcActor resident = all[i] as NpcActor;
                if (resident != null && resident.NpcId == npcId)
                {
                    return resident.transform;
                }
            }

            return null;
        }

        /// <summary>
        /// Gives a resident their patch of ground. The cell they already stand on is the centre of
        /// it, so where a resident is authored in npcs.json still decides where they are found.
        /// </summary>
        public static NpcWander AttachTo(NpcActor actor, TilemapBuilder map, Vector2Int home)
        {
            if (actor == null || map == null)
            {
                return null;
            }

            NpcWander wander = actor.gameObject.GetComponent<NpcWander>();
            if (wander == null)
            {
                wander = actor.gameObject.AddComponent<NpcWander>();
            }

            wander._actor = actor;
            wander._map = map;
            wander._home = home;
            wander._cell = home;
            wander._random = new System.Random(SeedFor(actor.NpcId));
            wander._pauseRemaining = wander.NextPause();
            return wander;
        }

        private void Update()
        {
            if (_actor == null || _map == null)
            {
                return;
            }

            if (_walking)
            {
                // Deliberately outside the hold: a step in flight is finished even once the village
                // has been told to stand still, because the alternative parks a resident between two
                // tiles with the cell ahead of them still reserved.
                StepTowardsTarget();
                return;
            }

            if (SomeoneIsSpeaking())
            {
                Listen();
                return;
            }

            if (_heardSpeech)
            {
                // The speech just ended. Back to the cell they were standing on, and only then back
                // to drifting: a resident who resumed wandering from wherever the speech caught them
                // would leave the village slightly rearranged by every conversation.
                _heardSpeech = false;
                _returning = _cell != _home;
                _pauseRemaining = ReturnPauseSeconds;
            }

            if (VillageIsHeld())
            {
                return;
            }

            if (PlayerIsClose())
            {
                FaceThePlayer();
                return;
            }

            _pauseRemaining -= Time.deltaTime;
            if (_pauseRemaining <= 0f)
            {
                BeginStep();
            }
        }

        /// <summary>
        /// Stand still and look at whoever is talking. A resident who is doing the talking looks at
        /// the player instead — they are the one being spoken to, and a speaker facing away from
        /// them reads as talking to somebody else.
        /// </summary>
        private void Listen()
        {
            _heardSpeech = true;

            Transform focus = Focus;
            if (focus == null)
            {
                return;
            }

            if (focus == transform)
            {
                FaceThePlayer();
                return;
            }

            if (!_focusAddressesVillage)
            {
                Vector3 gap = focus.position - transform.position;
                if (new Vector2(gap.x, gap.y).sqrMagnitude > ListeningRadius * ListeningRadius)
                {
                    return;
                }
            }

            CharacterAppearance appearance = _actor.Appearance;
            if (appearance != null)
            {
                Vector3 delta = focus.position - transform.position;
                appearance.SetDirectionFromDelta(new Vector2(delta.x, delta.y));
            }
        }

        /// <summary>
        /// Picks a neighbouring cell and reserves it. Failing to find one is an ordinary outcome —
        /// a resident boxed in by houses and neighbours simply waits — so it costs another pause
        /// rather than a warning.
        /// </summary>
        private void BeginStep()
        {
            if (_returning)
            {
                if (_cell == _home)
                {
                    _returning = false;
                }
                else if (StepHome())
                {
                    return;
                }
                else
                {
                    // Somebody is standing where they were. Giving up beats jittering against a
                    // blocked cell for the rest of the day; ordinary wandering will drift them back.
                    _returning = false;
                }
            }

            for (int attempt = 0; attempt < Steps.Length; attempt++)
            {
                if (Reserve(_cell + Steps[_random.Next(Steps.Length)]))
                {
                    return;
                }
            }

            _pauseRemaining = NextPause();
        }

        /// <summary>
        /// One step towards the spawn cell, along whichever axis is further off. Returns false when
        /// neither axis is free.
        /// </summary>
        private bool StepHome()
        {
            Vector2Int gap = _home - _cell;
            Vector2Int horizontal = new Vector2Int(gap.x > 0 ? 1 : -1, 0);
            Vector2Int vertical = new Vector2Int(0, gap.y > 0 ? 1 : -1);

            bool horizontalFirst = Mathf.Abs(gap.x) >= Mathf.Abs(gap.y);
            Vector2Int first = horizontalFirst ? horizontal : vertical;
            Vector2Int second = horizontalFirst ? vertical : horizontal;

            if (gap.x != 0 && Reserve(_cell + first))
            {
                return true;
            }

            return gap.y != 0 && Reserve(_cell + second);
        }

        /// <summary>
        /// Takes a cell and starts walking to it, or returns false and leaves everything alone.
        /// Reserving before the step is what keeps two residents out of the same tile.
        /// </summary>
        private bool Reserve(Vector2Int candidate)
        {
            if (candidate == _cell)
            {
                return false;
            }

            if (Mathf.Abs(candidate.x - _home.x) > HomeRadius || Mathf.Abs(candidate.y - _home.y) > HomeRadius)
            {
                return false;
            }

            if (!_map.IsWalkable(candidate))
            {
                return false;
            }

            // Walkable is not the same as empty: whatever does not block the grid — the crowd the
            // opening leaves behind, the mat by the door — is still something to not stand on top of.
            if (InteractableBase.FindOnCell(candidate) != null)
            {
                return false;
            }

            _target = candidate;
            _targetPoint = _map.CellToWorldCenter(candidate);
            _targetPoint.y += NpcActor.StandingYOffset;
            _targetPoint.z = transform.position.z;
            _map.SetWalkable(candidate.x, candidate.y, false);
            _walking = true;

            CharacterAppearance appearance = _actor.Appearance;
            if (appearance != null)
            {
                appearance.SetAnimation(CharacterAppearance.AnimationWalk);
                appearance.SetDirectionFromDelta(_targetPoint - transform.position);
            }

            return true;
        }

        private void StepTowardsTarget()
        {
            Vector3 before = transform.position;
            Vector3 moved = Vector3.MoveTowards(before, _targetPoint, WalkSpeed * Time.deltaTime);
            transform.position = moved;

            CharacterAppearance appearance = _actor.Appearance;
            Vector3 delta = moved - before;
            if (appearance != null && delta.sqrMagnitude > 0.000001f)
            {
                appearance.SetDirectionFromDelta(new Vector2(delta.x, delta.y));
            }

            if ((moved - _targetPoint).sqrMagnitude > ArriveEpsilon * ArriveEpsilon)
            {
                return;
            }

            Arrive();
        }

        /// <summary>
        /// Lands the step: the cell behind goes back to the grid, and the resident is re-placed so
        /// that the interactable's own cell, its world position and its draw order all agree with
        /// each other again rather than drifting a little further apart on every step.
        /// </summary>
        private void Arrive()
        {
            Vector2Int vacated = _cell;
            _cell = _target;
            _walking = false;
            _pauseRemaining = _returning ? ReturnPauseSeconds : NextPause();

            _actor.SettleOnCell(_map, _cell);

            if (vacated != _cell)
            {
                _map.SetWalkable(vacated.x, vacated.y, true);
            }

            CharacterAppearance appearance = _actor.Appearance;
            if (appearance != null)
            {
                appearance.SetAnimation(CharacterAppearance.AnimationIdle);
            }
        }

        private float NextPause()
        {
            return MinPauseSeconds + (float)_random.NextDouble() * (MaxPauseSeconds - MinPauseSeconds);
        }

        private bool PlayerIsClose()
        {
            PlayerController player = ResolvePlayer();
            if (player == null)
            {
                return false;
            }

            Vector3 delta = player.transform.position - transform.position;
            return new Vector2(delta.x, delta.y).sqrMagnitude <= AttentionRadius * AttentionRadius;
        }

        private void FaceThePlayer()
        {
            PlayerController player = ResolvePlayer();
            CharacterAppearance appearance = _actor.Appearance;
            if (player == null || appearance == null)
            {
                return;
            }

            Vector3 delta = player.transform.position - transform.position;
            appearance.SetDirectionFromDelta(new Vector2(delta.x, delta.y));
        }

        private static bool ComputeHold()
        {
            if (InputLock.IsLocked)
            {
                return true;
            }

            ReacquireIfNeeded();

            if (_dialogue != null && _dialogue.IsPlaying)
            {
                return true;
            }

            // From the first hint of dusk onwards the day is being wrapped up on screen — the split
            // panel, the night, the report — and figures still strolling behind it read as a village
            // that has not noticed.
            return _cycle != null && _cycle.NightAmount > 0f;
        }

        private static void ReacquireIfNeeded()
        {
            bool missing = _dialogue == null || _cycle == null;
            if (!missing || Time.unscaledTime < _nextSystemsLookup)
            {
                return;
            }

            _nextSystemsLookup = Time.unscaledTime + ReacquireInterval;

            if (_dialogue == null)
            {
                _dialogue = WorldRuntime.FindDialogueSystem();
            }

            if (_cycle == null)
            {
                _cycle = DayCycle.Find();
            }
        }

        private static PlayerController ResolvePlayer()
        {
            if (_player != null)
            {
                return _player;
            }

            if (Time.unscaledTime < _nextPlayerLookup)
            {
                return null;
            }

            _nextPlayerLookup = Time.unscaledTime + ReacquireInterval;
            _player = Object.FindFirstObjectByType<PlayerController>();
            return _player;
        }

        private static int SeedFor(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return 1;
            }

            int hash = 17;
            for (int i = 0; i < npcId.Length; i++)
            {
                hash = unchecked(hash * 31 + npcId[i]);
            }

            return hash == int.MinValue ? 1 : Mathf.Abs(hash);
        }

        /// <summary>
        /// Hands back a cell that was reserved for a step this resident will now never take. Only
        /// matters when a scene is torn down mid-stride and the tilemap outlives it.
        /// </summary>
        private void OnDestroy()
        {
            if (_walking && _map != null)
            {
                _map.SetWalkable(_target.x, _target.y, true);
            }
        }
    }
}
