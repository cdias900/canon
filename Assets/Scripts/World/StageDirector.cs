using System;
using System.Collections;
using System.Collections.Generic;
using SheepGate.Contest;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.UI;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// Runs whatever a stage of the season asks for beyond an ordinary working day.
    ///
    /// Nothing else in the project knows the order of a stage's scripted beats, so the whole payoff
    /// of the build hangs off this one component: without it no contest ever begins, the page never
    /// appears, the gate never closes and no vocation is ever named.
    ///
    /// <b>It is driven entirely by stages.json.</b> The stage the run is standing in says whether it
    /// has a beat at all (intro and work do not), which kind it is, which contest it fights, which
    /// gathering it plays, whether it finishes the wall, whether it hangs the doors and whether it
    /// names the vocation. Adding a stage is a data change; nothing here counts days.
    ///
    /// <b>The one rule this class exists to keep.</b> A scripted beat holds the day open only for as
    /// long as it is running, and then gives the hold back, so the stage finishes through the one
    /// and only end-of-day path every other stage uses — the split panel, the night, the morning.
    /// The single exception is the stage that declares <c>terminal</c>: that one takes
    /// <see cref="DayCycle.HoldFinalDay"/> and never releases it, because it has no tomorrow. Before
    /// stages were data this hold was taken unconditionally by the director of the last day, which
    /// silently stopped the calendar wherever the director first ran and made every stage after it
    /// unreachable — a season that ended early and logged nothing about why.
    ///
    /// <b>Re-entry is per stage and it is persisted.</b> The director listens to
    /// <see cref="DayCycle.MorningStarted"/> and also checks on its own Start, so a save resumed
    /// straight into a directed stage still gets its beat. A stage whose beat has already played out
    /// in this run — <see cref="GameFlags.StageDoneCounter"/> — never plays it again, and a run
    /// interrupted midway through a contest skips the fight (the contest refuses to be fought twice)
    /// and picks the stage up at whatever came after it.
    /// </summary>
    public sealed class StageDirector : MonoBehaviour
    {
        /// <summary>Marks that the finished segment carries the player's name in the save.</summary>
        public const string BuilderRecordedCounter = "gate_builder_recorded";

        /// <summary>
        /// Chapter the closing panel offers to open, and the passage the record plate comes from.
        ///
        /// Deliberately consts here rather than fields in stages.json. A reference only one branch
        /// of one class ever reads buys nothing by being data: no author can retune it without also
        /// authoring the copy around it, and moving it into the table would put a value in every
        /// stage's row that eight of the nine have no use for.
        /// </summary>
        private const string GateChapterRef = "NEH.12";

        /// <summary>Where the naming of builders comes from. Shown as a reference, never as text.</summary>
        private const string GateRecordRef = "NEH.3";

        /// <summary>
        /// Work units handed to a segment so it finishes. Deliberately more than the four courses
        /// can cost: the leftover is discarded by <see cref="WallSystem.ApplyWork"/>, and this is
        /// the story closing the wall rather than the player spending anything.
        /// </summary>
        private const int WorkUnitsToFinishASegment = 64;

        /// <summary>The stretch of ordinary day before a beat lands. Short by design, and readable.</summary>
        private const float MorningBeatSeconds = 6f;

        /// <summary>Time to look at the finished wall before the first closing panel.</summary>
        private const float GateBeatSeconds = 1.8f;

        /// <summary>
        /// How long the director waits on a contest that is neither running nor finished before it
        /// gives up and carries on. Only ever reached when the fight failed to start at all — a
        /// fight the player is still playing is never timed out.
        /// </summary>
        private const float StalledContestSeconds = 6f;

        private DayCycle _dayCycle;
        private MoraleContest _contest;
        private string _gateSegmentId;

        private bool _beatRunning;
        private bool _outcomeReceived;

        private void Start()
        {
            _dayCycle = FindDayCycle();
            if (_dayCycle != null)
            {
                _dayCycle.MorningStarted += OnMorningStarted;
            }
            else
            {
                Debug.LogWarning("[World] StageDirector found no DayCycle; it will only react to the stage it starts in.");
            }

            GameState state = WorldRuntime.State;
            TryStartBeat(state != null ? state.day : 1);
        }

        private void OnDestroy()
        {
            if (_dayCycle != null)
            {
                _dayCycle.MorningStarted -= OnMorningStarted;
                _dayCycle = null;
            }

            if (_contest != null)
            {
                _contest.Finished -= OnContestFinished;
                _contest = null;
            }
        }

        private void OnMorningStarted(int day)
        {
            TryStartBeat(day);
        }

        // ------------------------------------------------------------------ entry

        /// <summary>
        /// Decides whether this stage has anything scripted in it and starts it if so.
        ///
        /// The terminal hold is taken first, before every other guard, because it is true of the
        /// stage rather than of the beat: a resumed final stage whose beat has already played still
        /// has no tomorrow, and letting the daylight clock end it would resolve another night on a
        /// run that is already over.
        /// </summary>
        private void TryStartBeat(int day)
        {
            StageDef stage = GameData.Stage(day);
            if (stage == null || string.IsNullOrEmpty(stage.id))
            {
                // GameData already logged the missing or blank stage table. Directing a stage that
                // declares nothing would be inventing an ending out of a content error.
                return;
            }

            if (stage.terminal)
            {
                HoldTheLastDayOpen(stage);
            }

            if (_beatRunning || !StageHasADirectedBeat(stage))
            {
                return;
            }

            GameState state = WorldRuntime.State;
            if (state != null && state.Counter(GameFlags.StageDoneCounter(stage.id)) > 0)
            {
                Debug.Log("[World] Stage \"" + stage.id + "\" already played out in this run; its beat stays closed.");
                return;
            }

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("[World] StageDirector is inactive; stage \"" + stage.id + "\" cannot start its beat.");
                return;
            }

            _beatRunning = true;
            StartCoroutine(RunStage(stage));
        }

        /// <summary>
        /// True when this stage asks for something the ordinary day loop cannot deliver.
        ///
        /// This is the single authority on that question and <see cref="RunStage"/>'s switch mirrors
        /// it; the two have to name the same types or a stage would take a hold and then find no
        /// branch to spend it on. An unknown type answers false, so a typo in the table costs a
        /// missing beat and a logged error from the loader rather than a day that never ends.
        /// </summary>
        private static bool StageHasADirectedBeat(StageDef stage)
        {
            if (stage == null)
            {
                return false;
            }

            switch (stage.type)
            {
                case StageTypes.Rest:
                case StageTypes.Gate:
                case StageTypes.Battle:
                case StageTypes.Boss:
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ the stage

        private IEnumerator RunStage(StageDef stage)
        {
            // try/finally rather than a release at the bottom: the hold has to come back even if
            // this coroutine is stopped by the scene being torn down mid-beat, because a hold that
            // outlives its beat is exactly the failure this class was written to remove.
            try
            {
                Debug.Log("[World] Stage \"" + stage.id + "\" (day " + stage.day + ", " + stage.type
                          + ") has started its beat.");

                TakeTheBeatHold(stage);

                yield return WaitForTheMorning();

                switch (stage.type)
                {
                    case StageTypes.Rest:
                    case StageTypes.Gate:
                        yield return RunCutsceneBeat(stage);
                        break;

                    case StageTypes.Battle:
                    case StageTypes.Boss:
                        yield return RunContestBeat(stage);
                        break;

                    default:
                        // Unreachable while StageHasADirectedBeat is the gate above; logged rather
                        // than ignored so the two lists cannot drift apart in silence.
                        Debug.LogError("[World] Stage \"" + stage.id + "\" was directed but its type \""
                                       + stage.type + "\" has no beat. The day continues.");
                        break;
                }

                // Hoisted out of the type branches on purpose: what finishes the wall, hangs the
                // doors and names the vocation is what the stage DECLARES, not what kind it is.
                if (stage.finishes_wall)
                {
                    FinishTheWall(stage);
                }

                if (stage.closes_gate)
                {
                    yield return RunGateBeat(stage);
                }

                if (stage.reveals_vocation)
                {
                    yield return RunRevealBeat();
                }

                MarkStagePlayedOut(stage);
                WorldRuntime.SaveNow();
                Debug.Log("[World] Stage \"" + stage.id + "\" has finished its beat.");
            }
            finally
            {
                _beatRunning = false;
                ReleaseTheBeatHold(stage);
            }
        }

        /// <summary>
        /// A stretch of ordinary day before the beat lands, so the stage reads as a day and not as
        /// a cutscene. The clock only runs while the world is quiet, so the seconds are never eaten
        /// by the morning report or by a conversation, and nothing scripted lands on top of
        /// something being read.
        /// </summary>
        private IEnumerator WaitForTheMorning()
        {
            float remaining = MorningBeatSeconds;
            while (remaining > 0f)
            {
                if (!IsWorldBusy())
                {
                    remaining -= Time.unscaledDeltaTime;
                }

                yield return null;
            }
        }

        /// <summary>Waits until no panel is up and no line is being spoken.</summary>
        private IEnumerator WaitForQuietWorld()
        {
            while (IsWorldBusy())
            {
                yield return null;
            }
        }

        /// <summary>
        /// True while something has the player's attention: any modal panel — a morning report is
        /// one — or a dialogue mid-line.
        /// </summary>
        private static bool IsWorldBusy()
        {
            if (ModalRoot.IsOpen)
            {
                return true;
            }

            DialogueSystem dialogue = WorldRuntime.FindDialogueSystem();
            return dialogue != null && dialogue.IsPlaying;
        }

        // ------------------------------------------------------------------ the gathering

        /// <summary>
        /// Plays the gathering this stage names and waits for it to finish.
        ///
        /// The crowd itself is already standing in the square — <see cref="IntroCutscene"/> rebuilds
        /// it on every composition — so a stage's gathering is the authored node, not a second set
        /// of actors. A stage that names no node, or names one nobody authored, is a logged warning
        /// and an ordinary day; that degradation is what keeps a missing content file from stranding
        /// a player in a day that cannot end.
        /// </summary>
        private IEnumerator RunCutsceneBeat(StageDef stage)
        {
            string nodeId = stage.cutscene_node;
            if (string.IsNullOrEmpty(nodeId))
            {
                Debug.LogWarning("[World] Stage \"" + stage.id + "\" names no gathering node; its beat has nothing to play.");
                yield break;
            }

            // Play into a quiet world, because DialogueSystem refuses a node while one is running
            // and would drop this one on the floor with only a warning to show for it.
            yield return WaitForQuietWorld();

            if (!WorldRuntime.PlayDialogue(nodeId))
            {
                Debug.LogWarning("[World] The gathering node \"" + nodeId + "\" could not be played; stage \""
                                 + stage.id + "\" carries on without it.");
                yield break;
            }

            yield return WaitForQuietWorld();
        }

        // ------------------------------------------------------------------ the contest

        private IEnumerator RunContestBeat(StageDef stage)
        {
            MoraleContest contest = FindContest();
            if (contest == null)
            {
                Debug.LogError("[World] No MoraleContest is in the scene; stage \"" + stage.id
                               + "\" continues without its contest.");
                yield break;
            }

            if (string.IsNullOrEmpty(stage.contest))
            {
                Debug.LogError("[World] Stage \"" + stage.id + "\" is a " + stage.type
                               + " stage but names no contest; the day continues without one.");
                yield break;
            }

            WarnIfTheRevealHasNowhereToLand(stage);

            _contest = contest;
            _outcomeReceived = false;
            contest.Finished += OnContestFinished;

            ContestUI ui = ContestUI.EnsureInstance();
            if (ui != null)
            {
                ui.Bind(contest);
            }

            // Begin() is what loads the contest's tuning, and the move menu is built from that
            // config, so the screen has to be shown from inside Begin rather than before it.
            // Showing first would build a menu with no moves and leave the fight waiting on a
            // button that does not exist.
            contest.Begin(stage.contest);

            // The contest names the segment it is fought over; the gate that closes later is that
            // same segment and never one picked independently here.
            _gateSegmentId = ResolveGateSegmentId(contest);

            if (ui != null && contest.IsRunning)
            {
                ui.Show();
            }

            float stalled = 0f;
            while (!_outcomeReceived)
            {
                if (contest == null)
                {
                    Debug.LogWarning("[World] The contest went away mid-fight; stage \"" + stage.id + "\" carries on.");
                    break;
                }

                if (contest.IsRunning)
                {
                    stalled = 0f;
                }
                else
                {
                    stalled += Time.unscaledDeltaTime;
                    if (stalled >= StalledContestSeconds)
                    {
                        Debug.LogWarning("[World] The contest on stage \"" + stage.id
                                         + "\" never started; the day carries on.");
                        break;
                    }
                }

                yield return null;
            }

            if (contest != null)
            {
                contest.Finished -= OnContestFinished;
            }

            _contest = null;

            // The ending of the fight is read at the player's pace: the screen closes when they
            // close it, and only then does the day move on.
            while (ui != null && ui.IsVisible)
            {
                yield return null;
            }
        }

        private void OnContestFinished(ContestOutcome outcome)
        {
            _outcomeReceived = true;

            // Every outcome continues the day. Losing costs the work in progress on the segment and
            // nothing else: no defeat screen, and nothing already finished is taken away.
            Debug.Log("[World] The contest ended: " + outcome + ". The day continues.");
        }

        /// <summary>
        /// Says so when the stage that is supposed to turn chapter-and-verse on is fighting a
        /// contest whose tuning carries no page.
        ///
        /// The reveal is driven by the contest config's page turn, not by the stage flag, so the two
        /// can disagree without anything failing — and the failure is the whole build's payoff
        /// quietly not happening on the one stage that exists to produce it. A warning is the entire
        /// fix: nothing here can invent the page, and refusing to run the contest would cost the
        /// player a stage over a content mismatch.
        /// </summary>
        private static void WarnIfTheRevealHasNowhereToLand(StageDef stage)
        {
            if (!stage.reveals_page)
            {
                return;
            }

            ContestConfig config;
            if (GameData.Contests == null || !GameData.Contests.TryGetValue(stage.contest, out config) || config == null)
            {
                // Already an error from the loader's stage checks; not repeated here per stage.
                return;
            }

            if (config.page_turn <= 0)
            {
                Debug.LogError("[World] Stage \"" + stage.id + "\" declares the reveal but contest \""
                               + stage.contest + "\" has page_turn " + config.page_turn
                               + "; the page will never arrive and references stay hidden for the rest of the run.");
            }
        }

        // ------------------------------------------------------------------ the wall

        /// <summary>
        /// Finishes every segment except the one the gate is hung on.
        ///
        /// Work is only ever added, so a course that was already standing cannot regress and rule 4
        /// holds by construction. It exists so the dedication cannot open on a half-built wall when
        /// a player — or a cheap traversal of the season — under-builds, and the segment left short
        /// is the one the closing stage names, because a wall with its doors already hung has
        /// nothing left to dedicate.
        /// </summary>
        private void FinishTheWall(StageDef stage)
        {
            WallSystem wall = FindWallSystem();
            if (wall == null)
            {
                Debug.LogWarning("[World] No WallSystem in the scene; stage \"" + stage.id
                                 + "\" could not finish the wall.");
                return;
            }

            IReadOnlyList<string> ids = wall.SegmentIds;
            if (ids == null || ids.Count == 0)
            {
                Debug.LogWarning("[World] The wall has no segments; stage \"" + stage.id + "\" finished nothing.");
                return;
            }

            string spared = SeasonGateSegmentId();
            int raised = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id) || id == spared || wall.IsComplete(id))
                {
                    continue;
                }

                if (wall.ApplyWork(id, WorkUnitsToFinishASegment))
                {
                    raised++;
                }
            }

            Debug.Log("[World] Stage \"" + stage.id + "\" finished the wall: " + raised
                      + " segment(s) raised, \"" + (spared ?? "?") + "\" left for the gate.");
        }

        /// <summary>
        /// The segment the season's closing stage hangs its doors on, read from the stage table.
        ///
        /// Read from the stage that declares <c>closes_gate</c> rather than from the stage asking,
        /// because the stage that finishes the wall and the stage that closes the gate are not the
        /// same one and only the second of them knows which segment is being saved.
        /// </summary>
        private static string SeasonGateSegmentId()
        {
            StageDef[] stages = GameData.Stages;
            if (stages != null)
            {
                for (int i = 0; i < stages.Length; i++)
                {
                    StageDef stage = stages[i];
                    if (stage != null && stage.closes_gate && !string.IsNullOrEmpty(stage.gate_segment))
                    {
                        return stage.gate_segment;
                    }
                }
            }

            return ResolveGateSegmentId(null);
        }

        // ------------------------------------------------------------------ the gate

        private IEnumerator RunGateBeat(StageDef stage)
        {
            // The stage table is the authority on which segment the doors are hung on; the contest
            // that named a segment earlier in the day, and then the wall's own exposed segment, are
            // fallbacks for a table that says nothing.
            if (!string.IsNullOrEmpty(stage.gate_segment))
            {
                _gateSegmentId = stage.gate_segment;
            }
            else if (string.IsNullOrEmpty(_gateSegmentId))
            {
                _gateSegmentId = ResolveGateSegmentId(null);
            }

            CloseTheGate();
            RecordBuilder();
            WorldRuntime.SaveNow();

            // A moment to see the wall closed before the first panel of the ending.
            yield return new WaitForSecondsRealtime(GateBeatSeconds);
            yield return WaitForQuietWorld();

            bool continued = false;
            GateClosedPanel gate = GateClosedPanel.Show(BuilderName(), GateChapterRef, GateRecordRef,
                delegate { continued = true; });
            if (gate == null)
            {
                Debug.LogWarning("[World] The gate panel could not open; the stage continues to whatever follows it.");
                continued = true;
            }

            while (!continued)
            {
                yield return null;
            }
        }

        private IEnumerator RunRevealBeat()
        {
            bool revealClosed = false;
            VocationRevealPanel reveal = VocationRevealPanel.Show(delegate { revealClosed = true; });
            if (reveal == null)
            {
                Debug.LogWarning("[World] The reveal panel could not open; the season ends without its last screen.");
                revealClosed = true;
            }

            while (!revealClosed)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Finishes the segment the stage hangs its doors on. Work is only ever added, so a course
        /// that was already standing cannot regress here.
        /// </summary>
        private void CloseTheGate()
        {
            string segmentId = _gateSegmentId;
            if (string.IsNullOrEmpty(segmentId))
            {
                Debug.LogWarning("[World] No segment could be named for the gate; it stays as it is.");
                return;
            }

            WallSystem wall = FindWallSystem();
            if (wall != null && wall.Contains(segmentId))
            {
                if (wall.IsComplete(segmentId))
                {
                    Debug.Log("[World] Segment \"" + segmentId + "\" was already finished before the doors were hung.");
                    return;
                }

                wall.ApplyWork(segmentId, WorkUnitsToFinishASegment);
                return;
            }

            Debug.LogWarning("[World] No WallSystem holds \"" + segmentId + "\"; closing the gate in the save only.");

            GameState state = WorldRuntime.State;
            WallSegmentState segment = state != null ? state.Segment(segmentId) : null;
            if (segment == null)
            {
                Debug.LogWarning("[World] The run has no segment \"" + segmentId + "\"; the gate cannot be closed.");
                return;
            }

            if (segment.stage < WallSystem.StagesPerSegment)
            {
                segment.stage = WallSystem.StagesPerSegment;
                segment.workInStage = 0;
                segment.damaged = false;
            }
        }

        /// <summary>
        /// Names the segment a beat belongs to when the stage table did not. The contest is the
        /// authority whenever one ran, so the gate that closes is always the one that was defended;
        /// otherwise the resolution mirrors <see cref="MoraleContest.ContestedSegmentId"/> — the
        /// wall's exposed segment, which is also the one an unwatched night damages, then the data,
        /// and never a constant in code.
        /// </summary>
        private static string ResolveGateSegmentId(MoraleContest contest)
        {
            if (contest != null && !string.IsNullOrEmpty(contest.ContestedSegmentId))
            {
                return contest.ContestedSegmentId;
            }

            WallSystem wall = FindWallSystem();
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

            if (defs == null)
            {
                return null;
            }

            for (int i = 0; i < defs.Length; i++)
            {
                WallSegmentDef def = defs[i];
                if (def != null && def.exposed && !string.IsNullOrEmpty(def.id))
                {
                    return def.id;
                }
            }

            for (int i = 0; i < defs.Length; i++)
            {
                WallSegmentDef def = defs[i];
                if (def != null && !string.IsNullOrEmpty(def.id))
                {
                    return def.id;
                }
            }

            return null;
        }

        /// <summary>
        /// Records who repaired the segment. The record is the finished segment plus the name
        /// already carried by the run; the counter only marks that it has been written once, so a
        /// resumed closing stage does not claim the wall twice.
        /// </summary>
        private void RecordBuilder()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            if (state.Counter(BuilderRecordedCounter) != 0)
            {
                return;
            }

            state.counters[BuilderRecordedCounter] = 1;

            string name = BuilderName();
            Debug.Log("[World] Segment \"" + (_gateSegmentId ?? "?") + "\" recorded its builder"
                      + (name != null ? ": " + name : " without a name."));
        }

        /// <summary>The player's name, or null when the run never collected one.</summary>
        private static string BuilderName()
        {
            GameState state = WorldRuntime.State;
            string name = state != null ? state.playerName : null;
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            name = name.Trim();
            return name.Length > 0 ? name : null;
        }

        // ------------------------------------------------------------------ bookkeeping

        /// <summary>
        /// Writes that this stage's beat played to its end.
        ///
        /// Keyed by stage id rather than by day, and persisted rather than held in a field, so it
        /// survives the relaunch or the language switch that rebuilds this component — the old
        /// single in-memory pair had a run resumed mid-ending replay the beat it was halfway
        /// through.
        /// </summary>
        private static void MarkStagePlayedOut(StageDef stage)
        {
            GameState state = WorldRuntime.State;
            if (state == null || stage == null || string.IsNullOrEmpty(stage.id))
            {
                return;
            }

            string key = GameFlags.StageDoneCounter(stage.id);
            state.counters[key] = state.Counter(key) + 1;
        }

        // ------------------------------------------------------------------ holds

        /// <summary>
        /// Holds the day open for the length of the beat, and only for that.
        ///
        /// The terminal stage is the exception and it is already holding: it took
        /// <see cref="DayCycle.HoldFinalDay"/> in <see cref="TryStartBeat"/>, before any check about
        /// whether its beat still had anything to play, and never gives it back.
        /// </summary>
        private static void TakeTheBeatHold(StageDef stage)
        {
            if (stage != null && stage.terminal)
            {
                return;
            }

            DayCycle cycle = DayCycle.Find();
            if (cycle == null)
            {
                Debug.LogWarning("[World] No DayCycle to hold open; a scripted beat could be cut short by the daylight clock.");
                return;
            }

            cycle.HoldDusk(DayCycle.HoldStageBeat);
        }

        /// <summary>
        /// Gives the hold back so the stage ends the way every other stage ends: the split panel,
        /// the night, the next morning. This is the line that makes a season longer than one
        /// directed stage possible at all.
        /// </summary>
        private static void ReleaseTheBeatHold(StageDef stage)
        {
            if (stage != null && stage.terminal)
            {
                return;
            }

            DayCycle cycle = DayCycle.Find();
            if (cycle == null)
            {
                return;
            }

            cycle.ReleaseDusk(DayCycle.HoldStageBeat);
        }

        /// <summary>
        /// Stops the daylight clock ending the last stage of the season, for the rest of it.
        ///
        /// Nothing is taken from the player: the season ends on the dedication, and a night after it
        /// would resolve nothing. The hold is never released, which is the point — this is the stage
        /// that does not have a tomorrow to divide people over. It is taken from the stage's own
        /// <c>terminal</c> declaration and from nowhere else, so it can no longer stop the calendar
        /// on whichever stage happened to be directed first.
        /// </summary>
        private static void HoldTheLastDayOpen(StageDef stage)
        {
            DayCycle cycle = DayCycle.Find();
            if (cycle == null)
            {
                Debug.LogWarning("[World] No DayCycle to hold open; stage \"" + (stage != null ? stage.id : "?")
                                 + "\" could end itself on the daylight clock.");
                return;
            }

            cycle.HoldDusk(DayCycle.HoldFinalDay);
        }

        // ------------------------------------------------------------------ lookups

        private static DayCycle FindDayCycle()
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

        private static MoraleContest FindContest()
        {
            MoraleContest contest = FindFirstObjectByType<MoraleContest>();
            return contest;
        }

        private static WallSystem FindWallSystem()
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
    }
}
