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
    /// <b>There is exactly one way a day ends, and every stage of the season uses it</b> — the
    /// split panel, <see cref="ResolveNightOutcome"/>, <see cref="AdvanceToMorning"/>. A stage whose
    /// night threatens nothing (stages.json, <c>night_threat: false</c>) still runs all of it and
    /// still records its watch; it simply applies no damage. That graft is deliberate and the
    /// alternative was considered and rejected: a second, quieter way to end a day fails by leaving
    /// last night's counters in place and letting the next morning report them as this morning's
    /// fact — with no exception and no log.
    ///
    /// How long the season is comes from the stage table and from nowhere here; see
    /// <see cref="FinalDay"/>.
    ///
    /// This class is the sole authority on what counts as a watch — see <see cref="WatchThreshold"/>.
    /// The end-of-day panel asks it rather than deciding for itself, so the copy on the panel and
    /// the resolution of the night can never disagree.
    /// </summary>
    public class DayCycle : MonoBehaviour
    {
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

        /// <summary>
        /// Hold taken by the stage that declares itself terminal, which ends on its own beat and not
        /// on a night. Taken once and never released — it is the only hold in the game that is meant
        /// to outlive the thing that took it.
        /// </summary>
        public const string HoldFinalDay = "final_day";

        /// <summary>Hold taken while a resident still has something to say before the day may end.</summary>
        public const string HoldPendingBeat = "pending_beat";

        /// <summary>
        /// Hold taken by <see cref="StageDirector"/> while a stage's scripted beat is running.
        ///
        /// A separate name from <see cref="HoldPendingBeat"/> on purpose. Holds are a set of names,
        /// not a count, so two systems sharing one name means whichever finishes first releases the
        /// other's hold as well — and the resident who sends the player down the valley already owns
        /// that name. Sharing it would let a contest ending on the same day hand the evening back
        /// while a conversation was still owed.
        /// </summary>
        public const string HoldStageBeat = "stage_beat";

        /// <summary>
        /// The last stage's day, which is the last day the calendar advances to.
        ///
        /// Read from the stage table rather than declared here, because how long a season is, is a
        /// content decision. It stopped being a <c>const</c> when the season stopped being three
        /// days: neither call site ever used it in a constant expression, so the shape of the reads
        /// did not change.
        ///
        /// The fallback exists for the window before content is loaded and for a stage table that
        /// failed to parse. It is the length of the season this game shipped with first, which is a
        /// playable answer rather than a plausible-looking one: a fallback of zero or one would end
        /// the run on the first night, and GameData has already logged the real problem loudly.
        /// </summary>
        public static int FinalDay
        {
            get
            {
                StageDef[] stages = GameData.Stages;
                if (stages == null || stages.Length == 0)
                {
                    return FallbackFinalDay;
                }

                // The highest declared day rather than the last entry: the loader asserts the table
                // is contiguous and ordered, and reading the maximum means a table that is neither
                // still yields a season long enough to contain every stage in it.
                int last = 0;
                for (int i = 0; i < stages.Length; i++)
                {
                    StageDef stage = stages[i];
                    if (stage != null && stage.day > last)
                    {
                        last = stage.day;
                    }
                }

                return last > 0 ? last : FallbackFinalDay;
            }
        }

        /// <summary>Season length assumed when the stage table is unreadable. See <see cref="FinalDay"/>.</summary>
        private const int FallbackFinalDay = 3;

        /// <summary>
        /// A watch is half the crew, rounded up. Fewer than that is people awake, not a watch: the
        /// wall is long and a token pair cannot cover it.
        ///
        /// The number is half rather than any other fraction because half is the split the chapter
        /// describes (NEH.4.16) and the split a contest later names as a move, so the rule the
        /// player has been living with every night since the first one is the rule the page turns
        /// out to state.
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
        /// From this stage on, the night crew works with the other hand on the weapon (NEH.4.17)
        /// and lands a unit for every two people rather than every three: the whole city is on the
        /// wall from that day, and a crew that is already there builds more of it. Content, not
        /// calendar — it is the stage whose narration cites the rule — and a literal here because
        /// one line reads it.
        /// </summary>
        public const int HalfAndHalfStage = 7;

        /// <summary>Workers per night unit once every hand is on the wall. See <see cref="HalfAndHalfStage"/>.</summary>
        public const int WorkersPerNightWorkUnitArmed = 2;

        /// <summary>Counter set every night: 1 when that night's crew built double on a cleared path.</summary>
        public const string NightPathClearedCounter = "night_path_cleared";

        /// <summary>Counter set every night: the units the Tekoites returned on the player's stretch that morning.</summary>
        public const string NightTekoaReturnedCounter = "night_tekoa_returned";

        /// <summary>Marks the Tekoites' hour as returned, so it is returned once.</summary>
        private const string TekoaReturnedKey = "tekoa_returned";

        /// <summary>Marks the cleared path as spent, so it doubles one night and not every night after.</summary>
        private const string PathClearedSpentKey = "path_cleared_spent";

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

        /// <summary>
        /// The one stage whose watch scores the prophet, because it is the one stage where posting
        /// a watch means believing a resident against another resident rather than following the
        /// standing rule. Content, not calendar: if that conversation ever moves, this moves with
        /// it, and it stays a literal here rather than a column in stages.json because exactly one
        /// line in the game reads it.
        /// </summary>
        private const int ProphetWatchStage = 2;

        /// <summary>The wash over a working day as it runs out: low sun, and no gold in it.</summary>
        private static readonly Color EveningTint = new Color(0.42f, 0.22f, 0.10f);

        /// <summary>The wash the night fade carries the village into.</summary>
        private static readonly Color NightTint = new Color(0.05f, 0.07f, 0.16f);

        private const string LightTypeName = "UnityEngine.Rendering.Universal.Light2D";
        private const float LightSearchInterval = 2f;

        /// <summary>
        /// The people a night divides between the work and the watch. A fixed number, and no
        /// longer the day's work capacity: the two used to share <c>workCapacityMax</c>, which was
        /// only ever a coincidence of both being twelve. The day is four courses now
        /// (<see cref="GameState.DefaultWorkCapacityMax"/>), and a crew of four would have made the
        /// watch threshold two and the night crew's work zero. The split panel and the fallback
        /// split both read this and nothing else.
        /// </summary>
        public const int CrewSize = 12;

        private static readonly string[] PanelOpenMethods = { "Open", "Show", "Compose", "Present" };
        private static readonly string[] ReportOpenMethods = { "Show", "Open", "Compose", "Present" };

        /// <summary>Raised with the new day number once the night has fully resolved.</summary>
        public event Action<int> MorningStarted;

        /// <summary>Raised by <see cref="RequestEndDay"/> so the end-of-day panel can open.</summary>
        public event Action EndDayRequested;

        /// <summary>
        /// Raised once, the moment a day starts turning to evening. It exists for the things a day
        /// owes the player that a morning cannot deliver: the season's <c>intro</c> stage begins in
        /// a cutscene and never raises <see cref="MorningStarted"/>, so anything hung on the morning
        /// simply never happens there. Every other stage has both, and hanging a beat on the evening
        /// is what lets one piece of code serve all of them.
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
        /// each other. A stage's scripted beat holds until the beat is over; the resident who sends
        /// the player down the valley holds until they are back and have heard the rest of it; the
        /// terminal stage holds for good.
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
        /// The dedication refuses both: it ends on its own beat and has no night to divide anyone
        /// over. That is read from <see cref="HoldFinalDay"/> being held and from nothing else, and
        /// the reason is the gate: the last stage only takes that hold once the player's own
        /// segment is standing (<see cref="StageDirector"/>), and until then the last day is a
        /// working day like the others, with a night, a split and a morning after it on the same
        /// date. Refusing on the stage's declaration would leave that player with no way to reach
        /// the morning that refills the piles. The hold is the one name no other beat may take, so
        /// reading it cannot mistake a raid for the ending.
        /// </summary>
        public bool CanRest
        {
            get
            {
                if (IsResolving || IsFinalDayHeld)
                {
                    return false;
                }

                // An evening already on its way in with nothing in its path is not something to
                // ask for twice.
                return !_duskPending || IsDuskHeld;
            }
        }

        /// <summary>True while the dedication is holding the last day open for good.</summary>
        public bool IsFinalDayHeld
        {
            get { return _duskHolds.Contains(HoldFinalDay); }
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
            // reason to refuse them the night.
            //
            // Only that one hold. A stage's scripted beat and the terminal stage both survive this
            // deliberately: a raid is not something to be walked away from by lying down, and
            // CanRest has already refused on the last stage anyway.
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

        /// <summary>Work units a night crew of this size produces, before the guard changed the count.</summary>
        public static int NightWorkUnits(int workers)
        {
            return NightWorkUnits(workers, 1);
        }

        /// <summary>
        /// Work units a night crew of this size produces on the night of this day: one for every
        /// three people until <see cref="HalfAndHalfStage"/>, one for every two from it.
        /// </summary>
        public static int NightWorkUnits(int workers, int day)
        {
            if (workers <= 0)
            {
                return 0;
            }

            int perUnit = day >= HalfAndHalfStage ? WorkersPerNightWorkUnitArmed : WorkersPerNightWorkUnit;
            return workers / perUnit;
        }

        /// <summary>Crew the fallback split divides, when no screen supplied one.</summary>
        private static int ResolveCrewSize()
        {
            return CrewSize;
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
                // One line, computed, rather than a branch per day. The branch per day had no else,
                // so every night after the second left no record at all and every morning after it
                // reported "no watch" as fact — silently, and for most of a season.
                state.SetFlag(WorldRuntime.FlagWatchPostedForDay(day));

                if (day == ProphetWatchStage)
                {
                    // Believing the resident who saw the riders on the road. Still that one night,
                    // because it is that one conversation it answers, not the habit of watching.
                    WorldRuntime.AwardOnce("prophet_watch_d2_awarded", WorldRuntime.VocationProphet, 3);
                }
            }
            else if (wall != null && NightThreatensTheWall(day))
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
            int nightUnits = NightWorkUnits(LastWorkers, day);

            // The carriers' path, cleared that afternoon for an hour of the player's own work: the
            // crew reaches the wall without tripping and lands double, on this one night.
            bool pathCleared = state.HasFlag(GameFlags.PathCleared) && state.Counter(PathClearedSpentKey) == 0;
            if (pathCleared)
            {
                state.counters[PathClearedSpentKey] = 1;
                nightUnits *= 2;
            }

            LastNightWorkApplied = ApplyNightWork(wall, nightUnits, LastNightDamagedSegment);
            SetCounter(state, NightWorkCounter, LastNightWorkApplied);
            SetCounter(state, GameFlags.NightWorkCounter(day), LastNightWorkApplied);
            SetCounter(state, NightDamageCounter, LastNightDamagedSegment != null ? 1 : 0);
            SetCounter(state, NightPathClearedCounter, pathCleared ? 1 : 0);
            SetCounter(state, NightTekoaReturnedCounter, ReturnTheTekoitesHour(state, wall));

            ScoreSteward(state, day);
        }

        /// <summary>
        /// The hour given to the Tekoites' stretch comes back on the player's own, the next
        /// morning, once. Applied after the night's damage on purpose: it is tomorrow's stone, and
        /// stone laid in the morning is not stone an unwatched night can knock over. Returns the
        /// units that landed, 0 on every other night.
        /// </summary>
        private static int ReturnTheTekoitesHour(GameState state, WallSystem wall)
        {
            if (wall == null || !state.HasFlag(GameFlags.TekoaHelped) || state.Counter(TekoaReturnedKey) != 0)
            {
                return 0;
            }

            state.counters[TekoaReturnedKey] = 1;

            string yours = wall.PrimaryExposedSegmentId;
            if (string.IsNullOrEmpty(yours) || wall.IsComplete(yours))
            {
                return 0;
            }

            const int hour = 1;
            return wall.ApplyWork(yours, hour) ? hour : 0;
        }

        /// <summary>
        /// Whether an unwatched night on this stage costs the exposed segment its work in progress.
        ///
        /// The only thing <c>night_threat: false</c> switches off. Everything else about the night
        /// runs exactly as it always does — the split is asked for, the crew is recorded, the watch
        /// flag is written, the night crew builds, the counters are replaced and the morning report
        /// opens on true numbers. Skipping the whole night instead would have left yesterday's
        /// counters standing and let the next morning present them as last night's, which is a lie
        /// the player has no way to see and the build has no way to log.
        ///
        /// A blank fallback stage answers true rather than false. A stage table that failed to load
        /// must not quietly turn the season into one with nothing at stake: that is a content error
        /// that would play as a design decision, which is the exact failure this project keeps
        /// paying for.
        /// </summary>
        private static bool NightThreatensTheWall(int day)
        {
            StageDef stage = GameData.Stage(day);
            if (stage == null || string.IsNullOrEmpty(stage.id))
            {
                return true;
            }

            return stage.night_threat;
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

        /// <summary>Counter written by every night that resolves, keyed by the day it ended.</summary>
        private const string NightPlayedPrefix = "night_played_d";

        /// <summary>Counter written by a night the player ended with their whole capacity spent.</summary>
        private const string StewardCapacityPrefix = "steward_capacity_qualified_d";

        /// <summary>Counter written by a night the player ended with no pile left lying in the village.</summary>
        private const string StewardRubblePrefix = "steward_rubble_qualified_d";

        /// <summary>
        /// The steward scores for ending every night of the season with the work capacity fully
        /// spent, and for ending every one of them with no rubble left lying around. Each is a
        /// single action worth three points, checked once, when the last night resolves.
        ///
        /// It used to name day one and day two by hand, which was correct exactly as long as the
        /// season had two nights in it; in a longer season that conjunction hands the vocation out
        /// on the second night and asks nothing of the rest. Counting instead against the nights the
        /// run actually played is what makes it a habit rather than a start.
        ///
        /// "Actually played" is not the same as "in the table", and the difference matters: a save
        /// carried across a season's renumbering can arrive having never resolved some middle night.
        /// Asking it about a night it never had would make the vocation unreachable through no fault
        /// of the player, so each night records that it happened and the check only asks about those.
        /// </summary>
        private static void ScoreSteward(GameState state, int day)
        {
            SetCounter(state, NightPlayedPrefix + day, 1);

            ResourceSystem resources = ResourceSystem.Find();
            if (resources != null && resources.Capacity <= 0)
            {
                SetCounter(state, StewardCapacityPrefix + day, 1);
            }

            // The village, not the hands. This used to test state.rubble, which is the stone the
            // player is carrying: with a spare pile in the village every day and a block costing
            // three, that number ended almost every night at three, and half the steward was
            // unreachable by anyone who did exactly what the vocation describes — cleared the
            // ruins. What the vocation names is entulho left lying, so that is what is counted.
            if (NoPileLeftInTheVillage())
            {
                SetCounter(state, StewardRubblePrefix + day, 1);
            }

            // Only on the season's last night. Awarding earlier would hand the points to a player
            // who has not yet had the chance to break the habit — and AwardOnce cannot take them
            // back, because nothing in this game ever takes anything back.
            if (day < LastNightOfTheSeason())
            {
                return;
            }

            if (QualifiedEveryNight(state, StewardCapacityPrefix, day))
            {
                WorldRuntime.AwardOnce("steward_capacity_awarded", WorldRuntime.VocationSteward, 3);
            }

            if (QualifiedEveryNight(state, StewardRubblePrefix, day))
            {
                WorldRuntime.AwardOnce("steward_rubble_awarded", WorldRuntime.VocationSteward, 3);
            }
        }

        /// <summary>
        /// True when every pile that could be picked up today has been. A pile whose day has not
        /// come does not count against the player, and neither does a scene with no piles at all —
        /// the acceptance harness resolves nights without composing a village, and an empty
        /// village is a cleared one.
        /// </summary>
        private static bool NoPileLeftInTheVillage()
        {
            RubblePile[] piles = FindObjectsByType<RubblePile>(FindObjectsSortMode.None);
            for (int i = 0; i < piles.Length; i++)
            {
                RubblePile pile = piles[i];
                if (pile != null && pile.IsAvailable)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The last day that ends on a night. The terminal stage ends on its own beat and never
        /// resolves one, so the season's last night is the day before it.
        /// </summary>
        private static int LastNightOfTheSeason()
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0)
            {
                return FallbackFinalDay;
            }

            int last = 0;
            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage != null && !stage.terminal && stage.day > last)
                {
                    last = stage.day;
                }
            }

            return last > 0 ? last : FallbackFinalDay;
        }

        /// <summary>
        /// True when every night this run has actually resolved, up to and including tonight, wrote
        /// the qualifying counter this prefix names.
        /// </summary>
        private static bool QualifiedEveryNight(GameState state, string prefix, int throughDay)
        {
            for (int day = 1; day <= throughDay; day++)
            {
                if (state.Counter(NightPlayedPrefix + day) <= 0)
                {
                    continue;
                }

                if (state.Counter(prefix + day) <= 0)
                {
                    return false;
                }
            }

            return true;
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

            int finalDay = FinalDay;
            if (state.day < finalDay)
            {
                state.day = state.day + 1;
            }
            else
            {
                // A night on the last day: the gate was not earned yet, so the dedication waited
                // and the day ended like any other. The calendar never moves past the season, but
                // the morning has to be a real one — the piles come back, because a day with no
                // material would be a day the player cannot finish their segment in, and rule 7
                // says absence delays and never strands.
                RefillThePiles(state);
                Debug.Log("[World] A night resolved on the last stage of the season; the day counter stays at "
                          + finalDay + " and the village has its piles back (extra morning "
                          + state.Counter(ExtraMorningsCounter) + ").");
            }

            ResourceSystem resources = ResourceSystem.Find();
            if (resources != null)
            {
                resources.ResetDailyCapacity();
            }

            WorldRuntime.SaveNow();
        }

        /// <summary>Counter of mornings the last day has had beyond its first, for the log and the harness.</summary>
        public const string ExtraMorningsCounter = "extra_mornings";

        /// <summary>
        /// Puts every pile back in the village without moving the date. Piles record the day they
        /// were emptied and compare it with today, so on a repeated day they would stay empty for
        /// ever; clearing the records is what makes the repeated morning a morning. Nothing the
        /// player holds is touched.
        /// </summary>
        public static void RefillThePiles(GameState state)
        {
            if (state == null || state.counters == null)
            {
                return;
            }

            List<string> taken = new List<string>();
            foreach (KeyValuePair<string, int> pair in state.counters)
            {
                if (pair.Key.StartsWith(RubblePile.StoneTakenPrefix, StringComparison.Ordinal)
                    || pair.Key.StartsWith(RubblePile.TimberTakenPrefix, StringComparison.Ordinal))
                {
                    taken.Add(pair.Key);
                }
            }

            for (int i = 0; i < taken.Count; i++)
            {
                state.counters.Remove(taken[i]);
            }

            state.Bump(ExtraMorningsCounter);
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
        /// Sets the night level directly and takes the light away from the daylight clock, for a
        /// scripted moment such as a raid arriving. <see cref="ReleaseLight"/> hands it back, and so
        /// does the next morning, so a scripted beat can never strand the village in the dark.
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
