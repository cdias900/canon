using System;
using System.Collections;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Vocation;
using SheepGate.World;
using UnityEngine;

namespace SheepGate.Contest
{
    /// <summary>How the day-three contest ended. None of the three is a defeat screen.</summary>
    public enum ContestOutcome
    {
        /// <summary>The other side lost heart and pulled back.</summary>
        EnemyWithdrew,

        /// <summary>The player's people lost heart first. The other side still pulls back.</summary>
        PlayerBroke,

        /// <summary>Eight turns went by and nobody gave in. A technical draw.</summary>
        TurnLimit
    }

    /// <summary>
    /// The day-three trial of morale: alternating turns, no health bar, no death counter, no game
    /// over. Whoever makes the other side give up wins, and the outcome is decided by what the
    /// player did on days one and two rather than by a roll:
    ///
    ///   enemy resolve  = base + 10 when no watch was posted on night two
    ///                         + 10 when the invitation from outside was accepted
    ///   enemy pressure = base + 6 for the missing watch + 6 for the accepted invitation
    ///                         + one for every stage of the contested segment still unbuilt
    ///
    /// Nothing here is random. Two players who prepared the same way see the same fight.
    ///
    /// At the start of turn two the fight pauses for <see cref="ThePagePanel"/>: the reveal that
    /// this is the Bible lands at the same moment the Bible becomes the strongest move on the
    /// menu. That pause is the reason the whole POC exists, so it is the one thing this class
    /// will not skip.
    /// </summary>
    public class MoraleContest : MonoBehaviour
    {
        // ------------------------------------------------------------------ content ids

        public const string MoveHoldLine = "hold_line";
        public const string MoveCallOthers = "call_others";
        public const string MoveShowWatch = "show_watch";
        public const string MoveHalfAndHalf = "half_and_half";

        /// <summary>The page interrupts the start of this turn.</summary>
        public const int PageTurn = 2;

        // ------------------------------------------------------------------ tuning

        const int DefaultPlayerMorale = 100;
        const int DefaultEnemyResolveBase = 60;
        const int DefaultTurnLimit = 8;

        /// <summary>Resolve the other side gains for each thing the player left undone.</summary>
        const int ResolveForMissingWatch = 10;
        const int ResolveForAcceptedInvite = 10;

        /// <summary>Morale the other side takes every turn, before the player's preparation.</summary>
        const int BasePressure = 12;
        const int PressureForMissingWatch = 6;
        const int PressureForAcceptedInvite = 6;

        /// <summary>Stages a segment has when finished; an unbuilt stage is one more way in.</summary>
        const int StagesPerSegment = 4;

        /// <summary>The torch only surprises once. After that the walk is just a walk.</summary>
        const int SpentWatchResolveDelta = -4;
        const int UnpostedWatchResolveDelta = -4;

        const int VillageSize = 6;
        const string SpokenCounterKey = "npcs_talked";

        const float MoveBeatSeconds = 0.85f;
        const float TurnBeatSeconds = 0.55f;

        const int ZealotOpeningPoints = 2;
        const int ShepherdCallPoints = 2;
        const string ZealotOpeningCounter = "zealot_contest_open_awarded";
        const string ShepherdCallCounter = "shepherd_call_others_awarded";

        // ------------------------------------------------------------------ events

        /// <summary>Raised at the top of every turn, with the turn number starting at one.</summary>
        public event Action<int> TurnStarted;

        /// <summary>Raised exactly once, when the fight is over.</summary>
        public event Action<ContestOutcome> Finished;

        /// <summary>Raised whenever a displayed value changed and the UI should repaint.</summary>
        public event Action Changed;

        /// <summary>One pt-BR line describing what just happened, for the contest log.</summary>
        public event Action<string> Reported;

        // ------------------------------------------------------------------ state

        ContestConfig _config;
        Coroutine _loop;
        WallSystem _wall;

        int _turn;
        int _turnLimit = DefaultTurnLimit;
        int _morale;
        int _moraleMax = DefaultPlayerMorale;
        int _enemyResolve;
        int _enemyResolveMax = DefaultEnemyResolveBase;
        int _pressure = BasePressure;

        bool _running;
        bool _awaitingMove;
        bool _pageBlocking;
        bool _pageDismissed;
        bool _watchShown;
        bool _firstMovePlayed;
        int _enemyLineIndex;

        string _pendingMoveId;

        public bool IsRunning { get { return _running; } }

        /// <summary>
        /// The segment the fight is fought over: the one the wall reports as primarily exposed,
        /// which is the same one an unwatched night damages. Resolved from the data when the trial
        /// begins, never named in code, so the night and the trial can never drift apart. Null only
        /// when neither the wall nor wall_segments.json could offer a segment.
        /// </summary>
        public string ContestedSegmentId { get; private set; }

        /// <summary>True only while a move from the player is what the fight is waiting for.</summary>
        public bool IsAwaitingMove { get { return _running && _awaitingMove && !_pageBlocking; } }

        /// <summary>True while the fight is paused with the page on screen.</summary>
        public bool IsPagePaused { get { return _pageBlocking; } }

        public bool HasFinished { get; private set; }

        /// <summary>Meaningful only once <see cref="HasFinished"/> is true.</summary>
        public ContestOutcome Outcome { get; private set; }

        public int Turn { get { return _turn; } }
        public int TurnLimit { get { return _turnLimit; } }
        public int Morale { get { return _morale; } }
        public int MoraleMax { get { return _moraleMax; } }
        public int EnemyResolve { get { return _enemyResolve; } }
        public int EnemyResolveMax { get { return _enemyResolveMax; } }

        /// <summary>Every authored move, in the order of contest.json. Never null.</summary>
        public ContestMoveDef[] Moves
        {
            get
            {
                if (_config != null && _config.moves != null)
                {
                    return _config.moves;
                }

                return Array.Empty<ContestMoveDef>();
            }
        }

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            // Deliberately inert: the world builds this component on every day of the run and only
            // day three ever calls Begin.
            VocationTracker.EnsureRegistered();
        }

        void OnDisable()
        {
            if (_loop != null)
            {
                StopCoroutine(_loop);
                _loop = null;
            }
        }

        // ------------------------------------------------------------------ entry point

        /// <summary>
        /// Starts the fight, building the contest screen if nobody built one. Whoever triggers day
        /// three gets the whole experience from this single call.
        /// </summary>
        public void Begin()
        {
            if (_running)
            {
                Debug.LogWarning("[Contest] Begin was called while the trial was already running.");
                return;
            }

            GameState state = State;
            if (state != null && state.HasFlag(GameFlags.ContestResolved))
            {
                // Already fought in this run. Report the ending again so whatever waits on the
                // event keeps day three moving instead of stalling on a fight that cannot repeat.
                Debug.LogWarning("[Contest] The trial was already resolved in this run; replaying its ending only.");
                StartCoroutine(FinishNextFrame(HasFinished ? Outcome : ContestOutcome.TurnLimit));
                return;
            }

            _config = ReadConfig();
            if (_config == null || _config.moves == null || _config.moves.Length == 0)
            {
                Debug.LogError("[Contest] contest.json has no moves; the trial cannot be played. Day three continues without it.");
                StartCoroutine(FinishNextFrame(ContestOutcome.TurnLimit));
                return;
            }

            _moraleMax = _config.player_morale > 0 ? _config.player_morale : DefaultPlayerMorale;
            _turnLimit = _config.turn_limit > 0 ? _config.turn_limit : DefaultTurnLimit;

            int resolveBase = _config.enemy_resolve_base > 0 ? _config.enemy_resolve_base : DefaultEnemyResolveBase;
            bool watchPosted = state != null && state.HasFlag(GameFlags.WatchPostedD2);
            bool acceptedInvite = state != null && state.HasFlag(GameFlags.AcceptedInvite);

            // Resolved before the pressure below, which counts the stages this segment still lacks.
            ContestedSegmentId = ResolveContestedSegmentId();

            _enemyResolveMax = resolveBase
                               + (watchPosted ? 0 : ResolveForMissingWatch)
                               + (acceptedInvite ? ResolveForAcceptedInvite : 0);

            _pressure = BasePressure
                        + (watchPosted ? 0 : PressureForMissingWatch)
                        + (acceptedInvite ? PressureForAcceptedInvite : 0)
                        + Mathf.Clamp(StagesPerSegment - CompletedStages(), 0, StagesPerSegment);

            _morale = _moraleMax;
            _enemyResolve = _enemyResolveMax;
            _turn = 0;
            _awaitingMove = false;
            _pageBlocking = false;
            _pageDismissed = false;
            _watchShown = false;
            _firstMovePlayed = false;
            _enemyLineIndex = 0;
            _pendingMoveId = null;
            HasFinished = false;
            _running = true;

            // NEH.4.20 — the trumpet that calls everyone to the breach. It is the only sound in
            // the game a passage asks for by name, so it plays where the passage puts it: the
            // moment the assault arrives, not at a victory.
            SheepGate.Audio.AudioDirector.Play(SheepGate.Audio.AudioKeys.Trumpet);

            ContestUI ui = ContestUI.EnsureInstance();
            if (ui != null)
            {
                ui.Bind(this);
                ui.Show();
            }

            RaiseChanged();
            _loop = StartCoroutine(RunLoop());
        }

        /// <summary>
        /// Plays a move. Ignored when it is not the player's turn or when the move is not on the
        /// menu, so a double tap cannot spend two turns.
        /// </summary>
        public void UseMove(string moveId)
        {
            if (!_running || !_awaitingMove || _pageBlocking || string.IsNullOrEmpty(moveId))
            {
                return;
            }

            if (!IsMoveAvailable(moveId))
            {
                Debug.LogWarning("[Contest] Move '" + moveId + "' is not available right now.");
                return;
            }

            _pendingMoveId = moveId;
            _awaitingMove = false;
            RaiseChanged();
        }

        /// <summary>
        /// Whether the move is on the menu at all. A move flagged unlocked_by_page does not exist
        /// until the page has been closed; everything else is always offered.
        /// </summary>
        public bool IsMoveAvailable(string moveId)
        {
            ContestMoveDef move = FindMove(moveId);
            if (move == null)
            {
                return false;
            }

            return !move.unlocked_by_page || _pageDismissed;
        }

        /// <summary>
        /// The authored description, plus an honest note when the move no longer does what the
        /// description promises. Nothing here is a number: the bars carry those.
        /// </summary>
        public string DescriptionFor(string moveId)
        {
            ContestMoveDef move = FindMove(moveId);
            if (move == null)
            {
                return string.Empty;
            }

            string description = move.description ?? string.Empty;
            if (moveId == MoveShowWatch && _watchShown)
            {
                description += Loc.T("contest.move.watch.spent_note");
            }

            return description;
        }

        // ------------------------------------------------------------------ the fight

        IEnumerator RunLoop()
        {
            for (int turn = 1; turn <= _turnLimit; turn++)
            {
                _turn = turn;
                RaiseTurnStarted(turn);
                RaiseChanged();

                if (turn == PageTurn && !_pageDismissed)
                {
                    yield return ShowPage(turn);
                }

                _awaitingMove = true;
                RaiseChanged();

                while (_pendingMoveId == null)
                {
                    if (!_running)
                    {
                        yield break;
                    }

                    yield return null;
                }

                string moveId = _pendingMoveId;
                _pendingMoveId = null;
                _awaitingMove = false;

                ApplyPlayerMove(moveId);
                RaiseChanged();

                if (_enemyResolve <= 0)
                {
                    _enemyResolve = 0;
                    Finish(ContestOutcome.EnemyWithdrew);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(MoveBeatSeconds);

                ApplyEnemyPressure();
                RaiseChanged();

                if (_morale <= 0)
                {
                    _morale = 0;
                    Finish(ContestOutcome.PlayerBroke);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(TurnBeatSeconds);
            }

            Finish(ContestOutcome.TurnLimit);
        }

        IEnumerator ShowPage(int turn)
        {
            _pageBlocking = true;
            RaiseChanged();

            ThePagePanel panel = ThePagePanel.Show(turn, OnPageDismissed);
            if (panel == null)
            {
                Debug.LogWarning("[Contest] The page could not be shown; the move it unlocks is granted anyway.");
                _pageBlocking = false;
                _pageDismissed = true;
                RaiseChanged();
                yield break;
            }

            while (_pageBlocking)
            {
                yield return null;
            }

            Report(Loc.T("contest.log.page_unlocked"));
            RaiseChanged();
        }

        void OnPageDismissed()
        {
            _pageBlocking = false;
            _pageDismissed = true;
        }

        void ApplyPlayerMove(string moveId)
        {
            ContestMoveDef move = FindMove(moveId);
            if (move == null)
            {
                return;
            }

            int resolveDelta = move.resolve_delta;
            int moraleDelta = move.morale_delta;
            string line;

            switch (moveId)
            {
                case MoveHoldLine:
                {
                    int stages = CompletedStages();
                    resolveDelta -= stages;
                    line = stages > 0
                        ? Loc.T("contest.log.hold.built")
                        : Loc.T("contest.log.hold.bare");

                    if (!_firstMovePlayed)
                    {
                        AwardOnce(ZealotOpeningCounter, VocationIds.Zealot, ZealotOpeningPoints);
                    }

                    break;
                }

                case MoveCallOthers:
                {
                    int total;
                    int spoken = SpokenNpcCount(out total);
                    float share = total > 0 ? spoken / (float)total : 0f;
                    moraleDelta = Mathf.RoundToInt(move.morale_delta * share);
                    line = spoken > 0
                        ? Loc.T("contest.log.call.answered")
                        : Loc.T("contest.log.call.unanswered");
                    AwardOnce(ShepherdCallCounter, VocationIds.Shepherd, ShepherdCallPoints);
                    break;
                }

                case MoveShowWatch:
                {
                    GameState state = State;
                    bool watchPosted = state != null && state.HasFlag(GameFlags.WatchPostedD2);

                    if (_watchShown)
                    {
                        resolveDelta = SpentWatchResolveDelta;
                        line = Loc.T("contest.log.watch.spent");
                    }
                    else if (watchPosted)
                    {
                        line = Loc.T("contest.log.watch.posted");
                    }
                    else
                    {
                        resolveDelta = UnpostedWatchResolveDelta;
                        line = Loc.T("contest.log.watch.unposted");
                    }

                    _watchShown = true;
                    break;
                }

                case MoveHalfAndHalf:
                {
                    line = Loc.T("contest.log.half_and_half");
                    break;
                }

                default:
                {
                    line = move.display ?? moveId;
                    break;
                }
            }

            _firstMovePlayed = true;

            _enemyResolve = Mathf.Clamp(_enemyResolve + resolveDelta, 0, _enemyResolveMax);
            _morale = Mathf.Clamp(_morale + moraleDelta, 0, _moraleMax);

            Report(line);
        }

        // Keys rather than sentences: the words live in the locale table like every other
        // line the player reads. The order is the order they are shown in.
        static readonly string[] EnemyLineKeys =
        {
            "contest.log.enemy.1",
            "contest.log.enemy.2",
            "contest.log.enemy.3",
            "contest.log.enemy.4"
        };

        void ApplyEnemyPressure()
        {
            _morale = Mathf.Clamp(_morale - _pressure, 0, _moraleMax);

            string line = Loc.T(EnemyLineKeys[_enemyLineIndex % EnemyLineKeys.Length]);
            _enemyLineIndex++;
            Report(line);
        }

        void Finish(ContestOutcome outcome)
        {
            _running = false;
            _awaitingMove = false;
            _pendingMoveId = null;
            _loop = null;
            HasFinished = true;
            Outcome = outcome;

            GameState state = State;

            switch (outcome)
            {
                case ContestOutcome.EnemyWithdrew:
                    Report(Loc.T("contest.log.end.withdrew"));
                    break;

                case ContestOutcome.PlayerBroke:
                    Report(Loc.T("contest.log.end.broke"));
                    LoseUnfinishedWork();
                    break;

                case ContestOutcome.TurnLimit:
                    Report(Loc.T("contest.log.end.limit"));
                    break;
            }

            if (state != null)
            {
                state.morale = Mathf.Clamp(_morale, 0, _moraleMax);
                state.SetFlag(GameFlags.ContestResolved);

                try
                {
                    SaveSystem.Save(state);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Contest] Could not save after the trial: " + exception.Message);
                }
            }

            RaiseChanged();

            Action<ContestOutcome> handler = Finished;
            if (handler != null)
            {
                try
                {
                    handler(outcome);
                }
                catch (Exception exception)
                {
                    Debug.LogError("[Contest] A listener threw while the trial ended: " + exception.Message);
                }
            }
        }

        IEnumerator FinishNextFrame(ContestOutcome outcome)
        {
            yield return null;

            HasFinished = true;
            Outcome = outcome;

            Action<ContestOutcome> handler = Finished;
            if (handler != null)
            {
                handler(outcome);
            }
        }

        /// <summary>
        /// The only thing losing costs: the work in progress on the contested segment. Damage
        /// clears the unfinished work inside the current stage and nothing else, so a stage that is
        /// already standing never comes down. There is no defeat screen, and day three carries on
        /// to the reading either way.
        /// </summary>
        void LoseUnfinishedWork()
        {
            string segmentId = ContestedSegmentId;
            if (string.IsNullOrEmpty(segmentId))
            {
                return;
            }

            WallSystem wall = Wall;
            if (wall != null)
            {
                try
                {
                    wall.DamageSegment(segmentId);
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Contest] WallSystem.DamageSegment failed: " + exception.Message);
                }
            }

            GameState state = State;
            WallSegmentState segment = state != null ? state.Segment(segmentId) : null;
            if (segment == null)
            {
                return;
            }

            // Same guarantee without the wall present: only the work in progress is lost.
            segment.workInStage = 0;
            segment.damaged = true;
        }

        /// <summary>The scene's wall, resolved once and re-resolved if it was not there yet.</summary>
        WallSystem Wall
        {
            get
            {
                if (_wall == null)
                {
                    _wall = FindWallSystem();
                }

                return _wall;
            }
        }

        static WallSystem FindWallSystem()
        {
            WallSystem wall;
            try
            {
                if (ServiceLocator.TryGet(out wall) && wall != null)
                {
                    return wall;
                }
            }
            catch (Exception)
            {
                // Fall through to the scene lookup.
            }

            return FindFirstObjectByType<WallSystem>();
        }

        /// <summary>
        /// Finds the segment the trial is fought over, in the data and never in a constant: the
        /// wall's primary exposed segment first, because that is exactly what an unwatched night
        /// damages; then the first definition in wall_segments.json carrying the exposed flag; then
        /// the first definition of any kind, so a data file that forgot the flag still fights over
        /// a real segment.
        /// </summary>
        string ResolveContestedSegmentId()
        {
            WallSystem wall = Wall;
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
                    Debug.LogWarning("[Contest] Reading the wall's exposed segment failed: " + exception.Message);
                }
            }

            WallSegmentDef[] defs = null;
            try
            {
                defs = GameData.WallSegments;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Contest] Could not read wall_segments.json: " + exception.Message);
            }

            if (defs != null)
            {
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
            }

            Debug.LogWarning("[Contest] No wall segment could be resolved; the trial runs without one.");
            return null;
        }

        // ------------------------------------------------------------------ helpers

        ContestMoveDef FindMove(string moveId)
        {
            if (string.IsNullOrEmpty(moveId) || _config == null || _config.moves == null)
            {
                return null;
            }

            for (int i = 0; i < _config.moves.Length; i++)
            {
                ContestMoveDef move = _config.moves[i];
                if (move != null && move.id == moveId)
                {
                    return move;
                }
            }

            return null;
        }

        static ContestConfig ReadConfig()
        {
            try
            {
                return GameData.Contest;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Contest] Could not read contest.json: " + exception.Message);
                return null;
            }
        }

        /// <summary>Stages of the contested segment already finished, zero to four.</summary>
        int CompletedStages()
        {
            string segmentId = ContestedSegmentId;
            if (string.IsNullOrEmpty(segmentId))
            {
                return 0;
            }

            WallSystem wall = Wall;
            if (wall != null)
            {
                try
                {
                    if (wall.Contains(segmentId))
                    {
                        return Mathf.Clamp(wall.StageOf(segmentId), 0, StagesPerSegment);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Contest] Reading the wall stage failed: " + exception.Message);
                }
            }

            GameState state = State;
            WallSegmentState segment = state != null ? state.Segment(segmentId) : null;
            if (segment == null)
            {
                return 0;
            }

            return Mathf.Clamp(segment.stage, 0, StagesPerSegment);
        }

        /// <summary>
        /// How many residents the player has actually spoken with, derived on demand so no counter
        /// of "people remaining" can leak into a screen.
        /// </summary>
        static int SpokenNpcCount(out int total)
        {
            GameState state = State;
            NpcDef[] npcs = null;

            try
            {
                npcs = GameData.Npcs;
            }
            catch (Exception)
            {
                npcs = null;
            }

            if (state != null && npcs != null && npcs.Length > 0)
            {
                total = npcs.Length;
                int spoken = 0;
                for (int i = 0; i < npcs.Length; i++)
                {
                    NpcDef npc = npcs[i];
                    if (npc != null && !string.IsNullOrEmpty(npc.id) && DialogueData.HasSpokenWith(state, npc.id))
                    {
                        spoken++;
                    }
                }

                return spoken;
            }

            total = VillageSize;
            if (state == null)
            {
                return 0;
            }

            return Mathf.Clamp(state.Counter(SpokenCounterKey), 0, VillageSize);
        }

        static void AwardOnce(string counterKey, string vocationId, int points)
        {
            GameState state = State;
            if (state == null || string.IsNullOrEmpty(counterKey))
            {
                return;
            }

            if (state.Counter(counterKey) != 0)
            {
                return;
            }

            state.Bump(counterKey);

            VocationTracker tracker = VocationTracker.EnsureRegistered();
            if (tracker != null)
            {
                tracker.Add(vocationId, points);
            }
        }

        static GameState State
        {
            get
            {
                GameState state;
                try
                {
                    if (ServiceLocator.TryGet(out state) && state != null)
                    {
                        return state;
                    }
                }
                catch (Exception)
                {
                    // A missing registry is not worth a log line every turn.
                }

                return null;
            }
        }

        void Report(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            Action<string> handler = Reported;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(line);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Contest] A listener threw while reporting a line: " + exception.Message);
            }
        }

        void RaiseTurnStarted(int turn)
        {
            Action<int> handler = TurnStarted;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(turn);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Contest] A listener threw while a turn started: " + exception.Message);
            }
        }

        void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Contest] A listener threw while repainting: " + exception.Message);
            }
        }
    }
}
