using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using SheepGate.Core;
using SheepGate.Player;
using SheepGate.UI;

namespace SheepGate.World
{
    /// <summary>
    /// Day into night into the next morning.
    ///
    /// <b>The day ends on its own.</b> Work capacity is the only thing a day is spent on, so the
    /// light is that capacity seen a second way: every stone laid pulls it down, and when there is
    /// nothing left to spend the village goes to dusk and the split opens itself. Nothing here runs
    /// on a wall clock, which is the whole point - a player can stand still, talk to everyone and
    /// read a chapter end to end without the day moving an inch. The one thing still asked of them
    /// is the split itself, because that is the decision the chapter is about.
    ///
    /// <see cref="NightAmount"/> runs from 0 (day) to 1 (night) and is applied two ways at once: to
    /// a global URP Light2D when the project has one, and to a full screen tint overlay that always
    /// works. The Light2D path is late-bound so this file compiles and runs with or without URP.
    ///
    /// The night resolves on the split the player chose, and both halves of the split pay out:
    /// the work crew puts stone on the wall, and the watch either holds the exposed segment or is
    /// too thin to count, in which case that segment loses the work in progress inside its current
    /// stage. Completed stages never regress.
    ///
    /// This class is the sole authority on what counts as a watch — see <see cref="WatchThreshold"/>.
    /// The end-of-day panel asks it rather than deciding for itself, so the copy on the panel and
    /// the resolution of the night can never disagree.
    /// </summary>
    public class DayCycle : MonoBehaviour
    {
        public const int FinalDay = 3;
        public const float DuskSeconds = 1.1f;
        public const float NightHoldSeconds = 0.9f;
        public const float DawnSeconds = 1.1f;
        public const float MaxTintAlpha = 0.72f;

        /// <summary>
        /// How dark the village gets by the end of a working day, before the night fade begins.
        ///
        /// Deliberately well short of night: the day gets <i>late</i>, not dark, so the drop to a
        /// full night still reads as an event rather than as more of the same. Short of night, but
        /// not short of visible — this is the clock, and a clock nobody can read is not one.
        /// </summary>
        public const float DuskNightAmount = 0.42f;

        /// <summary>
        /// How fast the light follows the work that has been spent, in night units per second.
        /// Laying stone moves the target in a step; this is what turns that step into a slide, so
        /// the afternoon draws in instead of flicking one shade darker per tap.
        /// </summary>
        public const float DaylightFollowSpeed = 0.55f;

        /// <summary>
        /// Quiet beat between the day running out and the split opening itself, so the panel
        /// arrives as a consequence of the last stone rather than on the same frame as the tap.
        /// The wait restarts whenever something else takes the screen, so it always measures a
        /// clear moment and never a moment spent reading.
        /// </summary>
        public const float DuskSettleSeconds = 1.2f;

        /// <summary>Hold taken by the last day, which ends on the trial and not on a night.</summary>
        public const string HoldFinalDay = "final_day";

        /// <summary>Hold taken while a resident still has something to say before the day may end.</summary>
        public const string HoldPendingBeat = "pending_beat";

        /// <summary>
        /// A watch is half the crew, rounded up. Fewer than that is people awake, not a watch: the
        /// wall is long and a token pair cannot cover it.
        ///
        /// The number is half rather than any other fraction because half is the split the chapter
        /// describes (NEH.4.16) and the split the trial later names as a move, so the rule the
        /// player lives with for three nights is the rule the page turns out to state.
        /// </summary>
        public const int WatchCrewDivisor = 2;

        /// <summary>Floor for a very small crew: two pairs of eyes, whatever the arithmetic says.</summary>
        public const int MinimumWatchCrew = 2;

        /// <summary>
        /// People kept on the work through the night for one unit of stone. A night crew is slower
        /// than a day crew, so the daytime capacity stays the main engine of the wall.
        /// </summary>
        public const int WorkersPerNightWorkUnit = 3;

        /// <summary>
        /// Counter written every night with the work units the night crew actually landed on the
        /// wall. Set, never accumulated: it describes last night and nothing else.
        /// Must match SheepGate.UI.MorningReportUI.NightWorkCounter.
        /// </summary>
        public const string NightWorkCounter = "night_work_done";

        /// <summary>
        /// Counter written every night with how many segments the night damaged, 0 or 1. Also set
        /// rather than accumulated, so the morning report can distinguish stone knocked over last
        /// night from stone still lying where an earlier night left it.
        /// Must match SheepGate.UI.MorningReportUI.NightDamageCounter.
        /// </summary>
        public const string NightDamageCounter = "night_damaged_segments";

        /// <summary>The wash over a working day as it runs out: low sun, and no gold in it.</summary>
        private static readonly Color EveningTint = new Color(0.42f, 0.22f, 0.10f);

        /// <summary>The wash the night fade carries the village into.</summary>
        private static readonly Color NightTint = new Color(0.05f, 0.07f, 0.16f);

        private const string LightTypeName = "UnityEngine.Rendering.Universal.Light2D";
        private const float LightSearchInterval = 2f;
        private const int FallbackCrew = 12;

        private static readonly string[] PanelOpenMethods = { "Open", "Show", "Compose", "Present" };
        private static readonly string[] ReportOpenMethods = { "Show", "Open", "Compose", "Present" };

        /// <summary>Raised with the new day number once the night has fully resolved.</summary>
        public event Action<int> MorningStarted;

        /// <summary>Raised by <see cref="RequestEndDay"/> so the end-of-day panel can open.</summary>
        public event Action EndDayRequested;

        /// <summary>
        /// Raised once, the moment a day starts turning to evening. It exists for the things a day
        /// owes the player that a morning cannot deliver: day one begins in a cutscene and never
        /// raises <see cref="MorningStarted"/>, so anything hung on the morning simply never
        /// happens on the first day.
        /// </summary>
        public event Action<int> DuskBegan;

        /// <summary>Raised every time <see cref="NightAmount"/> changes.</summary>
        public event Action<float> NightAmountChanged;

        public float NightAmount { get; private set; }

        /// <summary>True while the night coroutine is running; end-of-day requests are ignored then.</summary>
        public bool IsResolving { get; private set; }

        public bool LastNightHadWatch { get; private set; }

        /// <summary>Segment damaged by the last night, or null when the night passed without loss.</summary>
        public string LastNightDamagedSegment { get; private set; }

        public int LastWorkers { get; private set; }
        public int LastWatchers { get; private set; }

        /// <summary>Work units the last night crew actually landed on the wall.</summary>
        public int LastNightWorkApplied { get; private set; }

        private Image _tint;
        private Type _lightType;
        private UnityEngine.Object _light;
        private PropertyInfo _lightIntensity;
        private float _nextLightSearch;
        private float _appliedNightAmount = -1f;

        private readonly HashSet<string> _duskHolds = new HashSet<string>();
        private bool _duskPending;
        private float _duskPendingSince;
        private bool _lightOwnedElsewhere;

        private void Awake()
        {
            CreateTintOverlay();
            _lightType = TypeBridge.Find(LightTypeName);
            if (_lightType != null)
            {
                _lightIntensity = TypeBridge.FindProperty(_lightType, "intensity");
            }

            ApplyNightAmount(0f, true);
        }

        private void CreateTintOverlay()
        {
            GameObject canvasObject = new GameObject("NightOverlay");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Below every other overlay canvas: the world darkens, the HUD stays readable.
            canvas.sortingOrder = -100;

            GameObject tintObject = new GameObject("Tint");
            tintObject.transform.SetParent(canvasObject.transform, false);
            _tint = tintObject.AddComponent<Image>();
            _tint.raycastTarget = false;
            _tint.color = TintFor(0f);

            RectTransform rect = _tint.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------ the daylight clock

        /// <summary>The day cycle in the scene, or null before the world has been composed.</summary>
        public static DayCycle Find()
        {
            DayCycle cycle = null;
            try
            {
                ServiceLocator.TryGet(out cycle);
            }
            catch (Exception)
            {
                cycle = null;
            }

            if (cycle == null)
            {
                cycle = FindFirstObjectByType<DayCycle>();
            }

            return cycle;
        }

        /// <summary>
        /// How much of today's work capacity is gone: 0 at first light, 1 once there is nothing
        /// left to spend. The day's only clock, and the only hand on it is the player's own work.
        /// </summary>
        public float DayProgress
        {
            get
            {
                GameState state = WorldRuntime.State;
                if (state == null || state.workCapacityMax <= 0)
                {
                    return 0f;
                }

                int max = state.workCapacityMax;
                int left = Mathf.Clamp(state.workCapacity, 0, max);
                return 1f - (float)left / max;
            }
        }

        /// <summary>True once the day is over and the split is waiting for a clear moment.</summary>
        public bool IsDuskPending
        {
            get { return _duskPending; }
        }

        /// <summary>True while something has asked the day not to end yet.</summary>
        public bool IsDuskHeld
        {
            get { return _duskHolds.Count > 0; }
        }

        /// <summary>
        /// True when the day could still go on: there is capacity left, so the split was asked for
        /// rather than forced, and the split screen may offer a way back out of it.
        /// </summary>
        public bool CanDeferDusk
        {
            get
            {
                GameState state = WorldRuntime.State;
                return !IsResolving && state != null && state.workCapacity > 0;
            }
        }

        /// <summary>
        /// Asks the day not to end yet, under a name, so two systems holding at once cannot cancel
        /// each other. Day three holds for the whole day; the resident who sends the player down
        /// the valley holds until they are back and have heard the rest of it.
        /// </summary>
        public void HoldDusk(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return;
            }

            if (_duskHolds.Add(reason))
            {
                Debug.Log("[World] The day is being held open: " + reason + ".");
            }
        }

        /// <summary>Releases one named hold. A name that is not held is ignored.</summary>
        public void ReleaseDusk(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return;
            }

            if (_duskHolds.Remove(reason))
            {
                Debug.Log("[World] The day is no longer held open by " + reason + ".");
            }
        }

        /// <summary>
        /// True when stopping for the night is something the player may do right now.
        ///
        /// Two cases, and they are not the same one: a day still running, where turning in is the
        /// player deciding they are finished early; and a dusk that is pending but held, where
        /// turning in is the player's way past a beat they have decided not to go back for. That
        /// second case is what keeps day two from stranding anyone — accepting the invitation
        /// spends the whole day's capacity <i>and</i> holds the evening for the other half of the
        /// conversation, so without it a player who never went back to the resident would have had
        /// no way to reach tomorrow at all.
        ///
        /// The last day refuses both: it ends on the trial and has no night to divide anyone over.
        /// </summary>
        public bool CanRest
        {
            get
            {
                if (IsResolving || _duskHolds.Contains(HoldFinalDay))
                {
                    return false;
                }

                // An evening already on its way in with nothing in its path is not something to
                // ask for twice.
                return !_duskPending || IsDuskHeld;
            }
        }

        /// <summary>
        /// Stops for the night. The mat by the door is the only way in, and it is optional: a day
        /// ends on its own once its capacity is spent, so this is a player deciding they are done,
        /// never a chore the game asks them to perform.
        /// </summary>
        public void RequestRest()
        {
            if (!CanRest)
            {
                return;
            }

            // Turning in forgives a beat that was still owed. The player has decided the day is
            // over, and a line they chose to walk away from is theirs to walk away from — never a
            // reason to refuse them the night. The last day's hold survives this, and CanRest has
            // already refused there.
            ReleaseDusk(HoldPendingBeat);
            BeginDusk("the player turned in");
        }

        /// <summary>Takes back a dusk the player decided against; the light comes back up with it.</summary>
        public void CancelDusk()
        {
            if (!_duskPending)
            {
                return;
            }

            _duskPending = false;
            Debug.Log("[World] Dusk was called off; the day goes on.");
        }

        private void BeginDusk(string cause)
        {
            if (_duskPending || IsResolving)
            {
                return;
            }

            _duskPending = true;
            _duskPendingSince = Time.unscaledTime;
            Debug.Log("[World] Dusk is pending: " + cause + ".");

            GameState state = WorldRuntime.State;
            int day = state != null ? state.day : 1;

            Action<int> handler = DuskBegan;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(day);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] A DuskBegan listener threw: " + exception.Message);
            }
        }

        private void Update()
        {
            if (IsResolving || _lightOwnedElsewhere)
            {
                return;
            }

            if (!_duskPending && DayProgress >= 1f)
            {
                BeginDusk("the day's work is spent");
            }

            DriveDaylight();

            if (_duskPending)
            {
                TryOpenTheSplit();
            }
        }

        /// <summary>
        /// The night level a day this far spent settles at.
        ///
        /// Gently superlinear rather than square. The square curve reads better on paper — the
        /// morning barely moves, the last stones pull the light down hard — but on screen it left
        /// a half-spent day at 6% wash, which is to say invisible, and a clock the player cannot
        /// read is not a clock at all. This keeps the late acceleration and buys back an
        /// afternoon that can actually be seen.
        ///
        /// Pure and public so the end-to-end run can assert what the curve is worth in alpha
        /// rather than only that it moved.
        /// </summary>
        public static float DaylightFor(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return DuskNightAmount * t * (0.5f + 0.5f * t);
        }

        /// <summary>Slides the light toward the level today's spent work implies.</summary>
        private void DriveDaylight()
        {
            float target = DaylightFor(_duskPending ? 1f : DayProgress);
            ApplyNightAmount(Mathf.MoveTowards(NightAmount, target, DaylightFollowSpeed * Time.deltaTime), false);
        }

        /// <summary>
        /// Whether a dusk that is already pending has to keep waiting.
        ///
        /// The whole rule, in one pure function, so it can be asserted rather than trusted: a
        /// night waits for a hold, for a cutscene, and for any open panel. The chapter reader is a
        /// panel like any other, and that is the point — a night that resolved while somebody was
        /// reading would charge them for the one thing this game exists to get them to do.
        /// </summary>
        public static bool DuskWaits(bool held, bool inputLocked, bool modalOpen)
        {
            return held || inputLocked || modalOpen;
        }

        /// <summary>
        /// Opens the split once nothing else is going on: no panel up, no cutscene, no
        /// conversation, and nothing holding the day open.
        /// </summary>
        private void TryOpenTheSplit()
        {
            if (DuskWaits(IsDuskHeld, InputLock.IsLocked, ModalRoot.IsOpen))
            {
                // The settle only ever measures a clear moment, so it restarts rather than
                // running down behind whatever has the screen.
                _duskPendingSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - _duskPendingSince < DuskSettleSeconds)
            {
                return;
            }

            RequestEndDay();
        }

        // ------------------------------------------------------------------ the night

        /// <summary>
        /// Asks for the end-of-day split. The UI layer either subscribes to
        /// <see cref="EndDayRequested"/> or exposes an opener on SheepGate.UI.EndDayPanel.
        /// </summary>
        public void RequestEndDay()
        {
            if (IsResolving)
            {
                return;
            }

            Action handler = EndDayRequested;
            if (handler != null)
            {
                try
                {
                    handler();
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] An EndDayRequested listener threw: " + exception.Message);
                    return;
                }
            }

            Type panelType = TypeBridge.Find("SheepGate.UI.EndDayPanel");
            if (panelType != null)
            {
                if (TypeBridge.InvokeStatic(panelType, PanelOpenMethods, new object[] { this }))
                {
                    return;
                }

                if (TypeBridge.InvokeStatic(panelType, PanelOpenMethods, new object[0]))
                {
                    return;
                }

                if (typeof(Component).IsAssignableFrom(panelType))
                {
                    UnityEngine.Object instance = FindFirstObjectByType(panelType);
                    if (instance != null && (TypeBridge.Invoke(instance, PanelOpenMethods, new object[] { this })
                                             || TypeBridge.Invoke(instance, PanelOpenMethods, new object[0])))
                    {
                        return;
                    }
                }
            }

            // Never punish for a missing screen: the fallback split posts a watch that clears the
            // threshold, so a UI failure can never cost the player a segment.
            int crew = ResolveCrewSize();
            int fallbackWatchers = WatchThreshold(crew);
            int fallbackWorkers = Mathf.Max(1, crew - fallbackWatchers);

            Debug.LogWarning("[World] Nothing answered RequestEndDay; resolving the night with a default split of "
                             + fallbackWorkers + " on the work and " + fallbackWatchers + " on the watch.");
            EndDay(fallbackWorkers, fallbackWatchers);
        }

        /// <summary>
        /// Smallest number of people on the wall that counts as a watch for a crew of this size:
        /// half of it, rounded up, never below <see cref="MinimumWatchCrew"/>, and never so large
        /// that the work would have to be emptied to meet it — neither side of the split may reach
        /// zero (NEH.4.16), so the wall can ask for at most everyone but one.
        ///
        /// Integer arithmetic on purpose: a float fraction rounds the wrong way on some crew sizes
        /// and the threshold has to be exactly what the panel prints.
        /// </summary>
        public static int WatchThreshold(int totalPeople)
        {
            int total = Mathf.Max(0, totalPeople);
            if (total <= 0)
            {
                return MinimumWatchCrew;
            }

            int half = (total + WatchCrewDivisor - 1) / WatchCrewDivisor;
            int threshold = Mathf.Max(MinimumWatchCrew, half);
            return Mathf.Min(threshold, Mathf.Max(1, total - 1));
        }

        /// <summary>
        /// Whether this split posts a watch. The single answer to that question in the whole
        /// project: the end-of-day panel prints what this returns, and the night resolves on it.
        /// </summary>
        public static bool CountsAsWatch(int watchers, int totalPeople)
        {
            return watchers >= WatchThreshold(totalPeople);
        }

        /// <summary>Work units a night crew of this size produces.</summary>
        public static int NightWorkUnits(int workers)
        {
            return workers <= 0 ? 0 : workers / WorkersPerNightWorkUnit;
        }

        /// <summary>Crew the run implies, used only when no screen supplied a split.</summary>
        private static int ResolveCrewSize()
        {
            GameState state = WorldRuntime.State;
            if (state != null && state.workCapacityMax >= 2)
            {
                return state.workCapacityMax;
            }

            return FallbackCrew;
        }

        /// <summary>Resolves the night on the chosen split and advances to the next morning.</summary>
        public void EndDay(int workers, int watchers)
        {
            if (IsResolving)
            {
                Debug.LogWarning("[World] EndDay ignored: the night is already resolving.");
                return;
            }

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("[World] EndDay called on an inactive DayCycle; resolving without the fade.");
                ResolveImmediately(workers, watchers);
                return;
            }

            StartCoroutine(ResolveNight(workers, watchers));
        }

        private void ResolveImmediately(int workers, int watchers)
        {
            IsResolving = true;
            ApplyNightAmount(1f, false);
            ResolveNightOutcome(workers, watchers);
            AdvanceToMorning();
            ApplyNightAmount(0f, false);
            IsResolving = false;
            RaiseMorning();
        }

        private IEnumerator ResolveNight(int workers, int watchers)
        {
            IsResolving = true;

            // From wherever the daylight clock left the light, not from full day: by now the
            // village is already late, and restarting at noon would flash before it darkened.
            yield return Fade(NightAmount, 1f, DuskSeconds);

            ResolveNightOutcome(workers, watchers);

            yield return new WaitForSeconds(NightHoldSeconds);

            AdvanceToMorning();

            yield return Fade(1f, 0f, DawnSeconds);

            IsResolving = false;
            RaiseMorning();
        }

        private void ResolveNightOutcome(int workers, int watchers)
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            LastWorkers = Mathf.Max(0, workers);
            LastWatchers = Mathf.Max(0, watchers);
            state.workAssigned = LastWorkers;
            state.watchAssigned = LastWatchers;

            int day = state.day;
            int crew = LastWorkers + LastWatchers;
            LastNightHadWatch = CountsAsWatch(LastWatchers, crew);
            LastNightDamagedSegment = null;
            LastNightWorkApplied = 0;

            WallSystem wall = FindWallSystem();
            if (wall == null)
            {
                Debug.LogWarning("[World] No WallSystem found; the night resolved without damage and without the night crew's work.");
            }

            if (LastNightHadWatch)
            {
                if (day <= 1)
                {
                    state.SetFlag(WorldRuntime.FlagWatchPostedD1);
                }
                else if (day == 2)
                {
                    state.SetFlag(WorldRuntime.FlagWatchPostedD2);
                    // Believing the resident who saw the riders on the road.
                    WorldRuntime.AwardOnce("prophet_watch_d2_awarded", WorldRuntime.VocationProphet, 3);
                }
            }
            else if (wall != null)
            {
                string exposed = wall.PrimaryExposedSegmentId;
                if (!string.IsNullOrEmpty(exposed))
                {
                    wall.DamageSegment(exposed);
                    LastNightDamagedSegment = exposed;
                }
            }

            // The other half of the split, and the reason the choice is a dilemma rather than a
            // formality: the people left on the work build while everyone else is on the wall.
            // Recorded so the morning can tell the player what their split actually bought.
            LastNightWorkApplied = ApplyNightWork(wall, NightWorkUnits(LastWorkers), LastNightDamagedSegment);
            SetCounter(state, NightWorkCounter, LastNightWorkApplied);
            SetCounter(state, NightDamageCounter, LastNightDamagedSegment != null ? 1 : 0);

            ScoreSteward(state, day);
        }

        /// <summary>
        /// Puts the night crew's labour into the wall and returns the units that actually landed.
        ///
        /// Sheltered segments are served before the exposed one, and the exposed one only when
        /// nothing else is unfinished. That order is load-bearing: the exposed segment is where an
        /// unwatched night lands its damage, so building it in the dark would hand the same stone
        /// straight back and the morning report would read as a contradiction. It also means the
        /// night never quietly repairs what the raid just knocked over.
        /// </summary>
        private static int ApplyNightWork(WallSystem wall, int units, string damagedSegmentId)
        {
            if (wall == null || units <= 0)
            {
                return 0;
            }

            int applied = ApplyNightWorkPass(wall, units, damagedSegmentId, false);
            if (applied < units)
            {
                applied += ApplyNightWorkPass(wall, units - applied, damagedSegmentId, true);
            }

            return applied;
        }

        /// <summary>
        /// One sweep over the segments, either the sheltered ones or the deferred ones. A segment
        /// takes at most the units its current stage still needs, so a night rolls a stage over
        /// but never runs several stages at once.
        /// </summary>
        private static int ApplyNightWorkPass(WallSystem wall, int units, string damagedSegmentId, bool onlyDeferred)
        {
            IReadOnlyList<string> ids = wall.SegmentIds;
            if (ids == null)
            {
                return 0;
            }

            int remaining = units;
            int applied = 0;

            for (int i = 0; i < ids.Count && remaining > 0; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id) || wall.IsComplete(id))
                {
                    continue;
                }

                bool deferred = wall.IsExposed(id) || id == damagedSegmentId;
                if (deferred != onlyDeferred)
                {
                    continue;
                }

                int room = wall.RemainingInStage(id);
                if (room <= 0)
                {
                    continue;
                }

                int chunk = Mathf.Min(room, remaining);
                if (!wall.ApplyWork(id, chunk))
                {
                    continue;
                }

                applied += chunk;
                remaining -= chunk;
            }

            return applied;
        }

        /// <summary>Writes a counter that describes last night only, replacing yesterday's value.</summary>
        private static void SetCounter(GameState state, string key, int value)
        {
            if (state == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            if (state.counters == null)
            {
                state.counters = new Dictionary<string, int>();
            }

            state.counters[key] = value;
        }

        /// <summary>
        /// The steward scores for finishing both day one and day two with the work capacity fully
        /// spent, and for finishing both of them with no rubble left lying around. Each is a single
        /// action worth three points, checked once, when the second night resolves.
        /// </summary>
        private static void ScoreSteward(GameState state, int day)
        {
            if (day > 2)
            {
                return;
            }

            ResourceSystem resources = ResourceSystem.Find();
            if (resources != null && resources.Capacity <= 0)
            {
                SetCounter(state, "steward_capacity_qualified_d" + day, 1);
            }

            if (state.rubble <= 0)
            {
                SetCounter(state, "steward_rubble_qualified_d" + day, 1);
            }

            if (day < 2)
            {
                return;
            }

            if (state.Counter("steward_capacity_qualified_d1") > 0 && state.Counter("steward_capacity_qualified_d2") > 0)
            {
                WorldRuntime.AwardOnce("steward_capacity_awarded", WorldRuntime.VocationSteward, 3);
            }

            if (state.Counter("steward_rubble_qualified_d1") > 0 && state.Counter("steward_rubble_qualified_d2") > 0)
            {
                WorldRuntime.AwardOnce("steward_rubble_awarded", WorldRuntime.VocationSteward, 3);
            }
        }

        private void AdvanceToMorning()
        {
            // A new day owns its own light and its own clock, whatever yesterday was holding.
            _duskPending = false;
            _lightOwnedElsewhere = false;

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            if (state.day < FinalDay)
            {
                state.day = state.day + 1;
            }
            else
            {
                Debug.LogWarning("[World] EndDay resolved on the final day; the day counter stays at " + FinalDay + ".");
            }

            ResourceSystem resources = ResourceSystem.Find();
            if (resources != null)
            {
                resources.ResetDailyCapacity();
            }

            WorldRuntime.SaveNow();
        }

        private void RaiseMorning()
        {
            GameState state = WorldRuntime.State;
            int day = state != null ? state.day : 1;

            Action<int> handler = MorningStarted;
            bool userInterfaceListening = false;

            if (handler != null)
            {
                userInterfaceListening = HasUserInterfaceListener(handler);
                try
                {
                    handler(day);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] A MorningStarted listener threw: " + exception.Message);
                }
            }

            if (userInterfaceListening)
            {
                return;
            }

            // World objects such as the rubble piles always listen, so the presence of subscribers
            // proves nothing about the UI. Only reach for the panel when no UI type is listening.
            Type reportType = TypeBridge.Find("SheepGate.UI.MorningReportUI");
            if (reportType != null)
            {
                if (TypeBridge.InvokeStatic(reportType, ReportOpenMethods, new object[] { day })
                    || TypeBridge.InvokeStatic(reportType, ReportOpenMethods, new object[0]))
                {
                    return;
                }
            }

            Debug.LogWarning("[World] No UI listened to MorningStarted; day " + day + " begins without a morning report.");
        }

        private static bool HasUserInterfaceListener(Delegate handler)
        {
            Delegate[] listeners = handler.GetInvocationList();
            for (int i = 0; i < listeners.Length; i++)
            {
                object target = listeners[i].Target;
                Type type = target != null ? target.GetType() : listeners[i].Method.DeclaringType;
                if (type == null || string.IsNullOrEmpty(type.Namespace))
                {
                    continue;
                }

                if (type.Namespace == "SheepGate.UI" || type.Namespace.StartsWith("SheepGate.UI."))
                {
                    return true;
                }
            }

            return false;
        }

        private WallSystem FindWallSystem()
        {
            WallSystem wall = null;
            try
            {
                ServiceLocator.TryGet(out wall);
            }
            catch (Exception)
            {
                wall = null;
            }

            if (wall == null)
            {
                wall = FindFirstObjectByType<WallSystem>();
            }

            return wall;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            if (seconds <= 0f)
            {
                ApplyNightAmount(to, false);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);
                ApplyNightAmount(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)), false);
                yield return null;
            }

            ApplyNightAmount(to, false);
        }

        /// <summary>
        /// The wash at a given night level, alpha included.
        ///
        /// Two colours rather than one: below <see cref="DuskNightAmount"/> it is the low warm
        /// light of a day running out, and above it the night the fade is carrying the village
        /// into. A single navy tint made a working afternoon read as an overcast morning.
        /// </summary>
        private static Color TintFor(float amount)
        {
            float level = Mathf.Clamp01(amount);
            float toNight = level <= DuskNightAmount ? 0f : Mathf.InverseLerp(DuskNightAmount, 1f, level);
            Color color = Color.Lerp(EveningTint, NightTint, toNight);
            color.a = level * MaxTintAlpha;
            return color;
        }

        /// <summary>
        /// Sets the night level directly and takes the light away from the daylight clock, for
        /// scripted moments such as the day three assault. <see cref="ReleaseLight"/> hands it
        /// back, and so does the next morning, so a scripted beat can never strand the village
        /// in the dark.
        /// </summary>
        public void SetNightAmount(float amount)
        {
            _lightOwnedElsewhere = true;
            ApplyNightAmount(Mathf.Clamp01(amount), false);
        }

        /// <summary>Gives the light back to the daylight clock.</summary>
        public void ReleaseLight()
        {
            _lightOwnedElsewhere = false;
        }

        private void ApplyNightAmount(float amount, bool force)
        {
            NightAmount = Mathf.Clamp01(amount);
            if (!force && Mathf.Approximately(_appliedNightAmount, NightAmount))
            {
                return;
            }

            _appliedNightAmount = NightAmount;

            if (_tint != null)
            {
                _tint.color = TintFor(NightAmount);
            }

            ApplyToLight();

            Action<float> handler = NightAmountChanged;
            if (handler != null)
            {
                try
                {
                    handler(NightAmount);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] A NightAmountChanged listener threw: " + exception.Message);
                }
            }
        }

        private void ApplyToLight()
        {
            if (_lightType == null || _lightIntensity == null)
            {
                return;
            }

            if (_light == null && Time.unscaledTime >= _nextLightSearch)
            {
                _nextLightSearch = Time.unscaledTime + LightSearchInterval;
                try
                {
                    _light = FindFirstObjectByType(_lightType);
                }
                catch (Exception)
                {
                    _light = null;
                }
            }

            if (_light == null)
            {
                return;
            }

            try
            {
                float intensity = Mathf.Lerp(1f, 0.25f, NightAmount);
                _lightIntensity.SetValue(_light, intensity, null);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Driving the 2D light failed; falling back to the tint overlay only: " + exception.Message);
                _lightIntensity = null;
            }
        }
    }
}
