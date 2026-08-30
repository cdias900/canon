using System;
using System.Collections;
using SheepGate.Contest;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.UI;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// Runs the last day. Nothing else in the project knows the order of the closing beats, so the
    /// whole payoff of the POC hangs off this one component: without it the trial never begins, the
    /// page never appears, the gate never closes and no vocation is ever named.
    ///
    /// The order is fixed and comes from the spec:
    ///   1. a short morning, so the day reads as a day and not as a cutscene;
    ///   2. the assault, which is the trial of morale;
    ///   3. the gate closes whatever the outcome — losing is not a game over, the other side
    ///      withdraws either way and the day carries on;
    ///   4. the segment records who repaired it, the way Nehemiah 3 records its builders;
    ///   5. a secondary, entirely optional way into the chapter — the free choice to open it is
    ///      the one number this POC exists to measure, so it is never opened for the player and
    ///      the ending is never gated behind it;
    ///   6. the vocation is named.
    ///
    /// Re-entry is deliberate. The director listens to <see cref="DayCycle.MorningStarted"/> and
    /// also checks on its own Start, so a save resumed straight into day three still gets an
    /// ending. A run that already reached the reveal never runs again; a run interrupted between
    /// the trial and the reveal skips the fight (the contest refuses to be fought twice) and picks
    /// the sequence up at the ending.
    /// </summary>
    public sealed class Day3Director : MonoBehaviour
    {
        /// <summary>Marks that the finished segment carries the player's name in the save.</summary>
        public const string BuilderRecordedCounter = "gate_builder_recorded";

        /// <summary>Marks the whole closing sequence as played out, for a resumed run.</summary>
        public const string SequenceDoneCounter = "day3_sequence_done";

        /// <summary>
        /// Work units handed to the segment so the gate closes. Deliberately more than the four
        /// stages can cost: the leftover is discarded by <see cref="WallSystem.ApplyWork"/>, and
        /// this is the story closing the wall rather than the player spending anything.
        /// </summary>
        private const int WorkUnitsToCloseTheGate = 64;

        /// <summary>The morning before the assault. Short by design, and readable.</summary>
        private const float MorningBeatSeconds = 6f;

        /// <summary>Time to look at the finished wall before the first closing panel.</summary>
        private const float GateBeatSeconds = 1.8f;

        /// <summary>
        /// How long the director waits on a contest that is neither running nor finished before it
        /// gives up and carries on. Only ever reached when the trial failed to start at all — a
        /// fight the player is still playing is never timed out.
        /// </summary>
        private const float StalledContestSeconds = 6f;

        private DayCycle _dayCycle;
        private MoraleContest _contest;
        private string _gateSegmentId;

        private bool _sequenceRunning;
        private bool _sequenceFinished;
        private bool _outcomeReceived;
        private ContestOutcome _outcome;

        /// <summary>True once the closing sequence has played out in this session.</summary>
        public bool HasPlayed
        {
            get { return _sequenceFinished; }
        }

        /// <summary>How the trial ended. Meaningful only once <see cref="HasPlayed"/> is true.</summary>
        public ContestOutcome Outcome
        {
            get { return _outcome; }
        }

        private void Start()
        {
            _dayCycle = FindDayCycle();
            if (_dayCycle != null)
            {
                _dayCycle.MorningStarted += OnMorningStarted;
            }
            else
            {
                Debug.LogWarning("[World] Day3Director found no DayCycle; it will only react to the day it starts in.");
            }

            GameState state = WorldRuntime.State;
            TryStartSequence(state != null ? state.day : 1);
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
            TryStartSequence(day);
        }

        // ------------------------------------------------------------------ entry

        private void TryStartSequence(int day)
        {
            if (_sequenceRunning || _sequenceFinished)
            {
                return;
            }

            if (day < DayCycle.FinalDay)
            {
                return;
            }

            GameState state = WorldRuntime.State;
            if (state != null && state.HasFlag(GameFlags.VocationRevealed))
            {
                // This run already reached its ending. Day three stays open to walk around in, but
                // nothing replays — and the day cannot be ended again, or each press would resolve
                // another night on a run that is already over.
                _sequenceFinished = true;
                HoldTheDayOpen();
                Debug.Log("[World] Day three already ended in this run; the closing sequence stays closed.");
                return;
            }

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("[World] Day3Director is inactive; the closing sequence cannot start.");
                return;
            }

            _sequenceRunning = true;
            StartCoroutine(RunDayThree());
        }

        // ------------------------------------------------------------------ the day

        private IEnumerator RunDayThree()
        {
            Debug.Log("[World] Day three has started; the assault is scheduled.");

            // The day is going to end on its own terms from here.
            HoldTheDayOpen();

            yield return WaitForTheMorning();

            GameState state = WorldRuntime.State;
            bool alreadyResolved = state != null && state.HasFlag(GameFlags.ContestResolved);

            if (alreadyResolved)
            {
                Debug.Log("[World] The trial was already resolved in this run; day three resumes at the ending.");
            }
            else
            {
                yield return RunTrial();
            }

            // Whatever happened, the gate closes and the segment keeps the name of whoever built it.
            if (string.IsNullOrEmpty(_gateSegmentId))
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
            GateClosedPanel gate = GateClosedPanel.Show(BuilderName(), delegate { continued = true; });
            if (gate == null)
            {
                Debug.LogWarning("[World] The gate panel could not open; the sequence continues to the reveal.");
                continued = true;
            }

            while (!continued)
            {
                yield return null;
            }

            bool revealClosed = false;
            VocationRevealPanel reveal = VocationRevealPanel.Show(delegate { revealClosed = true; });
            if (reveal == null)
            {
                Debug.LogWarning("[World] The reveal panel could not open; day three ends without its last screen.");
                revealClosed = true;
            }

            while (!revealClosed)
            {
                yield return null;
            }

            _sequenceRunning = false;
            _sequenceFinished = true;

            GameState finalState = WorldRuntime.State;
            if (finalState != null)
            {
                finalState.counters[SequenceDoneCounter] = 1;
            }

            WorldRuntime.SaveNow();
            Debug.Log("[World] Day three is complete.");
        }

        /// <summary>
        /// The morning: a short stretch of day the player actually gets to spend. The clock only
        /// runs while the world is quiet, so the seconds are never eaten by the morning report or
        /// by a conversation, and the assault never lands on top of something being read.
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
        /// True while something has the player's attention: any modal panel — the morning report
        /// of day three is one — or a dialogue mid-line.
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

        // ------------------------------------------------------------------ the trial

        private IEnumerator RunTrial()
        {
            MoraleContest contest = FindContest();
            if (contest == null)
            {
                Debug.LogError("[World] No MoraleContest is in the scene; day three continues without the trial.");
                yield break;
            }

            _contest = contest;
            _outcomeReceived = false;
            contest.Finished += OnContestFinished;

            ContestUI ui = ContestUI.EnsureInstance();
            if (ui != null)
            {
                ui.Bind(contest);
            }

            // Begin() is what loads contest.json, and the move menu is built from that config, so
            // the screen has to be shown from inside Begin rather than before it. Showing first
            // would build a menu with no moves and leave the trial waiting on a button that does
            // not exist.
            contest.Begin();

            // The trial names the segment it is fought over; the gate that closes afterwards is
            // that same segment and never one picked independently here.
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
                    Debug.LogWarning("[World] The contest went away mid-trial; day three carries on.");
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
                        Debug.LogWarning("[World] The trial never started; day three carries on to the ending.");
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
            _outcome = outcome;
            _outcomeReceived = true;

            // Every outcome continues the day. Losing costs the work in progress on the segment and
            // nothing else: no defeat screen, and nothing already finished is taken away.
            Debug.Log("[World] The trial ended: " + outcome + ". Day three continues.");
        }

        // ------------------------------------------------------------------ the gate

        /// <summary>
        /// Finishes the segment the day was fought over. Work is only ever added, so a stage that
        /// was already standing cannot regress here.
        /// </summary>
        private void CloseTheGate()
        {
            string segmentId = _gateSegmentId;
            if (string.IsNullOrEmpty(segmentId))
            {
                Debug.LogWarning("[World] No segment could be named for day three; the gate stays as it is.");
                return;
            }

            WallSystem wall = FindWallSystem();
            if (wall != null && wall.Contains(segmentId))
            {
                if (wall.IsComplete(segmentId))
                {
                    Debug.Log("[World] Segment \"" + segmentId + "\" was already finished before the trial ended.");
                    return;
                }

                wall.ApplyWork(segmentId, WorkUnitsToCloseTheGate);
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
        /// Names the segment the day belongs to. The trial is the authority whenever it ran, so the
        /// gate that closes is always the one that was defended; otherwise the resolution mirrors
        /// <see cref="MoraleContest.ContestedSegmentId"/> — the wall's exposed segment, which is
        /// also the one an unwatched night damages, then the data, and never a constant in code.
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
        /// resumed day three does not claim the wall twice.
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

        /// <summary>
        /// Stops the daylight clock ending day three, for the rest of day three.
        ///
        /// Nothing is taken from the player: the last day ends on the trial, and a night after it
        /// would resolve nothing. The hold is never released, which is the point — this is the day
        /// that does not have a tomorrow to divide people over.
        /// </summary>
        private static void HoldTheDayOpen()
        {
            DayCycle cycle = DayCycle.Find();
            if (cycle == null)
            {
                Debug.LogWarning("[World] No DayCycle to hold open; day three could end itself on the daylight clock.");
                return;
            }

            cycle.HoldDusk(DayCycle.HoldFinalDay);
        }
    }
}
