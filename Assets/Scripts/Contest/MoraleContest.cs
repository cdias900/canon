using System;
using System.Collections;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Vocation;
using SheepGate.World;
using UnityEngine;

namespace SheepGate.Contest
{
    /// <summary>How a contest ended. None of the three is a defeat screen.</summary>
    public enum ContestOutcome
    {
        /// <summary>The other side lost heart and pulled back.</summary>
        EnemyWithdrew,

        /// <summary>The player's people lost heart first. The other side still pulls back.</summary>
        PlayerBroke,

        /// <summary>The turn limit ran out and nobody gave in. A technical draw.</summary>
        TurnLimit
    }

    /// <summary>
    /// A trial of morale: alternating turns, no health bar, no death counter, no game over. Whoever
    /// makes the other side give up wins, and the outcome is decided by what the player did in the
    /// stages before it rather than by a roll:
    ///
    ///   enemy resolve  = base + 10 when no watch was posted on the night before this stage
    ///                         + 10 when the invitation from outside was accepted
    ///   enemy pressure = base + 6 for the missing watch + 6 for the accepted invitation
    ///                         + one for every course of the contested segment still unbuilt
    ///
    /// Nothing here is random. Two players who prepared the same way see the same fight.
    ///
    /// <b>There is more than one contest in a season, and this class is instanced once.</b> The
    /// scene builds a single component and every encounter runs through it, so everything that used
    /// to be "the trial" is now "this contest": which tuning was read, whether it has already been
    /// fought, whose lines the other side speaks. Three run-wide booleans used to carry that, and
    /// each of them was correct only because there had only ever been one fight to be true for:
    ///
    ///   * the resolved flag, which short-circuited a second contest with the first one's ending;
    ///   * the page-dismissed field, per instance rather than per run, which would have replayed
    ///     the reveal in the second contest — the one scene the whole build exists to produce;
    ///   * the enemy's lines, a static array, which gave a later encounter the first one's voice.
    ///
    /// All three are now keyed: the flag by contest id, the page by the persisted state of the run,
    /// the lines by the contest's own locale-key prefix. Each of those failures logged nothing and
    /// looked like a design decision, which is why they are named here rather than left to a diff.
    ///
    /// A contest that declares a page pauses at that turn for <see cref="ThePagePanel"/>: the
    /// reveal that this is the Bible lands at the same moment the Bible becomes the strongest move
    /// on the menu. That pause is the reason the whole build exists, so it is the one thing this
    /// class will not skip — and equally, it happens once in a season and never again.
    ///
    /// <b>Morale is reseeded to full at the start of every contest and never carries forward.</b>
    /// That is rule 7 and not a convenience: a boss that opened already ground down by an earlier
    /// fight would be losing yesterday, which is the one direction this game never moves. What the
    /// earlier stages carry into a later fight is preparation — the watch, the invitation, the wall
    /// — and never damage.
    /// </summary>
    public class MoraleContest : MonoBehaviour
    {
        // ------------------------------------------------------------------ content ids

        public const string MoveHoldLine = "hold_line";
        public const string MoveCallOthers = "call_others";
        public const string MoveShowWatch = "show_watch";
        public const string MoveHalfAndHalf = "half_and_half";

        /// <summary>
        /// The mockery's own move. Mockery has no target to attack — the design's first threat is
        /// a pure morale drain — so its answer is the work carrying on out loud: the other side
        /// loses its audience, and the crew hears the count instead of the joke.
        /// </summary>
        public const string MoveKeepCounting = "keep_counting";

        /// <summary>
        /// The turn the page interrupts when a contest carries a reveal but does not say when.
        ///
        /// A contest declares its own turn in contest.json, and zero there means it carries no
        /// page at all — which is how the second encounter of the season says it is not the place
        /// the reveal happens. This constant is what stops that zero from being ambiguous: a
        /// contest that names a passage for the page and forgets the turn gets the page anyway,
        /// here, with a warning. Without that, one missing number in a data file would delete the
        /// single beat the product is measured by and nothing would say so.
        /// </summary>
        public const int PageTurn = 2;

        /// <summary>
        /// How far past turn one this class will look for a numbered enemy line before it stops
        /// asking. Only ever reached by a locale table that numbers its lines with a gap in it.
        /// </summary>
        const int MaxEnemyLines = 16;

        // ------------------------------------------------------------------ tuning

        const int DefaultPlayerMorale = 100;
        const int DefaultEnemyResolveBase = 60;
        const int DefaultTurnLimit = 8;

        /// <summary>Resolve the other side gains for each thing the player left undone.</summary>
        const int ResolveForMissingWatch = 10;
        const int ResolveForAcceptedInvite = 10;

        /// <summary>
        /// Morale the other side takes every turn, before the player's preparation, when the
        /// contest does not name a figure of its own. It is a default now rather than the number:
        /// a boss has to be able to press harder than a first raid, and while this lived only here
        /// it could not.
        /// </summary>
        const int DefaultBasePressure = 12;
        const int PressureForMissingWatch = 6;
        const int PressureForAcceptedInvite = 6;

        /// <summary>The torch only surprises once. After that the walk is just a walk.</summary>
        const int SpentWatchResolveDelta = -4;
        const int UnpostedWatchResolveDelta = -4;

        /// <summary>
        /// Size of the village when npcs.json could not be read at all. Every other path derives
        /// the number from the roster, so this is a last resort rather than a second opinion about
        /// how many people live here — two copies of that number is how they come to disagree.
        /// </summary>
        const int FallbackVillageSize = 6;

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
        int _pressure = DefaultBasePressure;

        /// <summary>The turn the page interrupts in THIS contest, or zero when it carries none.</summary>
        int _pageTurn;

        bool _running;
        bool _awaitingMove;
        bool _pageBlocking;
        bool _pageDismissed;
        bool _watchShown;
        bool _firstMovePlayed;
        int _enemyLineIndex;
        string[] _enemyLineKeys = DefaultEnemyLineKeys;

        /// <summary>
        /// Whether a watch stood on the night immediately before this stage, read once when the
        /// contest opens. Held rather than re-read at move time so the number the fight was tuned
        /// against and the sentence the torch move prints can never be talking about two different
        /// nights — which is exactly what a second read of a day-keyed flag would eventually do.
        /// </summary>
        bool _watchPostedLastNight;

        string _pendingMoveId;

        public bool IsRunning { get { return _running; } }

        /// <summary>
        /// Which contest is being fought, as contest.json keys it. Empty before the first
        /// <see cref="Begin(string)"/>. The screen reads it to look for words of this encounter's
        /// own before falling back to the shared ones, so a boss can end in its own sentences
        /// instead of repeating the raid's.
        /// </summary>
        public string ContestId { get; private set; }

        /// <summary>
        /// The segment the fight is fought over: the one the wall reports as primarily exposed,
        /// which is the same one an unwatched night damages. Resolved from the data when the
        /// contest begins, never named in code, so the night and the fight can never drift apart.
        /// Null only when neither the wall nor wall_segments.json could offer a segment.
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
            // Deliberately inert: the world builds this component on every stage of the run, and
            // only the stages that declare a contest ever call Begin. The same component runs both
            // of them, one after the other, which is why nothing about a fight may live past its
            // own Begin.
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
        /// Starts the contest the current stage declares. Kept at this exact signature because it
        /// is the shape every existing caller and the acceptance harness already use; it now
        /// resolves which fight it means instead of assuming there is only one.
        /// </summary>
        public void Begin()
        {
            Begin(ResolveStageContestId());
        }

        /// <summary>
        /// Starts a named fight, building the contest screen if nobody built one. Whoever triggers
        /// the stage gets the whole experience from this single call.
        ///
        /// The id is the key in contest.json, and it is also the key everything about this fight is
        /// remembered under. That is the whole point of the overload: the resolved flag is per
        /// contest, so the second encounter of a season is not short-circuited by the ending of the
        /// first — a failure that would have replayed stage six's outcome on stage eight, in
        /// silence, and read as a deliberate ending rather than as a bug.
        /// </summary>
        public void Begin(string contestId)
        {
            if (_running)
            {
                Debug.LogWarning("[Contest] Begin was called while a contest was already running.");
                return;
            }

            // Set before the early returns below, so the replay path and the screen bound to it
            // still agree about which fight this component is standing in for.
            ContestId = contestId ?? string.Empty;

            GameState state = State;
            if (state != null && !string.IsNullOrEmpty(contestId) &&
                state.HasFlag(GameFlags.ContestResolvedFor(contestId)))
            {
                // This contest was already fought in this run. Report its ending again so whatever
                // waits on the event keeps the stage moving instead of stalling on a fight that
                // cannot repeat. Keyed by id, so a later contest is never answered with an earlier
                // one's outcome.
                Debug.LogWarning("[Contest] Contest '" + contestId +
                                 "' was already resolved in this run; replaying its ending only.");
                StartCoroutine(FinishNextFrame(HasFinished ? Outcome : ContestOutcome.TurnLimit));
                return;
            }

            _config = ReadConfig(contestId);
            if (_config == null || _config.moves == null || _config.moves.Length == 0)
            {
                Debug.LogError("[Contest] contest.json defines no moves for '" + contestId +
                               "'; the fight cannot be played. The stage continues without it.");
                StartCoroutine(FinishNextFrame(ContestOutcome.TurnLimit));
                return;
            }

            _moraleMax = _config.player_morale > 0 ? _config.player_morale : DefaultPlayerMorale;
            _turnLimit = _config.turn_limit > 0 ? _config.turn_limit : DefaultTurnLimit;
            _pageTurn = ResolvePageTurn(_config, _turnLimit);
            _enemyLineKeys = ResolveEnemyLineKeys(_config);

            int resolveBase = _config.enemy_resolve_base > 0 ? _config.enemy_resolve_base : DefaultEnemyResolveBase;
            int basePressure = _config.base_pressure > 0 ? _config.base_pressure : DefaultBasePressure;

            // The night immediately before this stage, and not one named in code. For as long as
            // this read the day-two flag by name, every contest past the second was being tuned
            // against a night that had nothing to do with it — and because a flag for a night that
            // was never played is simply absent, the answer came back "no watch stood" every time.
            // That is the harshest possible reading of a player who may have posted a guard on
            // every night of the season, arrived at silently, in the direction rule 7 forbids.
            _watchPostedLastNight = state != null &&
                                    state.HasFlag(GameFlags.WatchPostedForDay(state.day - 1));
            bool acceptedInvite = state != null && state.HasFlag(GameFlags.AcceptedInvite);

            // Resolved before the pressure below, which counts the courses this segment still lacks.
            ContestedSegmentId = ResolveContestedSegmentId();

            _enemyResolveMax = resolveBase
                               + (_watchPostedLastNight ? 0 : ResolveForMissingWatch)
                               + (acceptedInvite ? ResolveForAcceptedInvite : 0);

            _pressure = basePressure
                        + (_watchPostedLastNight ? 0 : PressureForMissingWatch)
                        + (acceptedInvite ? PressureForAcceptedInvite : 0)
                        + Mathf.Clamp(WallSystem.StagesPerSegment - CompletedStages(),
                                      0, WallSystem.StagesPerSegment);

            // Full, every time, and never carried over from an earlier fight. See the class doc:
            // a boss that opened already worn down would be the player losing a yesterday they
            // already survived, which rule 7 forbids outright.
            _morale = _moraleMax;
            _enemyResolve = _enemyResolveMax;
            _turn = 0;
            _awaitingMove = false;
            _pageBlocking = false;

            // Seeded from the run and NOT reset to false. The page happens once in a season: this
            // field used to be per-instance state, so a second contest would have shown the reveal
            // again — and it would also have re-locked the move the first reveal unlocked, taking
            // back something the player had already earned. Reading the persisted flag fixes both
            // at once, because "the page has been seen" is a fact about the run, not about a fight.
            _pageDismissed = state != null && state.HasFlag(GameFlags.PageShown);

            _watchShown = false;
            _firstMovePlayed = false;
            _enemyLineIndex = 0;
            _pendingMoveId = null;
            HasFinished = false;
            _running = true;

            // NEH.4.20 — the trumpet that calls everyone to the breach. It is the only sound in
            // the game a passage asks for by name, so it plays where the passage puts it: the
            // moment the assault arrives, not at a victory — and not at all for a contest with no
            // assault in it, which is what the row's trumpet flag says.
            if (_config.trumpet)
            {
                SheepGate.Audio.AudioDirector.Play(SheepGate.Audio.AudioKeys.Trumpet);
            }

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
        /// Which contest the day the player is standing in declares.
        ///
        /// Falls back to the first contest in the file when the stage names none, which is what
        /// keeps the no-argument overload behaving exactly as it always did for a caller that has
        /// no stage to consult — the editor harness constructs a state at a bare day number and
        /// expects a fight. The fallback reads its id out of the config rather than spelling one,
        /// so there is no second place in the tree that believes it knows the first contest's name.
        /// </summary>
        static string ResolveStageContestId()
        {
            GameState state = State;

            try
            {
                if (state != null)
                {
                    StageDef stage = GameData.Stage(state.day);
                    if (stage != null && !string.IsNullOrEmpty(stage.contest))
                    {
                        return stage.contest;
                    }
                }

                ContestConfig first = GameData.Contest;
                return first != null ? first.id : null;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Contest] Could not resolve which contest this stage declares: " +
                               exception.Message);
                return null;
            }
        }

        /// <summary>
        /// The turn this contest's page interrupts, or zero for a contest that carries no reveal.
        ///
        /// Both corrections here exist because of how the miss fails. A contest that names a
        /// passage for the page but leaves the turn at zero, or puts it past its own last turn,
        /// plays through to its ending looking completely healthy and simply never shows the page
        /// — which deletes the reveal, and with it the one number the product is measured by,
        /// without a single line in the log. So each is repaired loudly instead of obeyed quietly.
        /// </summary>
        static int ResolvePageTurn(ContestConfig config, int turnLimit)
        {
            int turn = config.page_turn;
            bool carriesPage = !string.IsNullOrEmpty(config.page_verse);

            if (turn <= 0)
            {
                if (!carriesPage)
                {
                    return 0;
                }

                Debug.LogWarning("[Contest] Contest '" + config.id + "' names a passage for the page but no " +
                                 "turn to show it on; falling back to turn " + PageTurn + ".");
                turn = PageTurn;
            }

            if (turn > turnLimit)
            {
                Debug.LogWarning("[Contest] Contest '" + config.id + "' puts the page on turn " + turn +
                                 ", past its own limit of " + turnLimit + "; moving it to the last turn.");
                turn = turnLimit;
            }

            return turn;
        }

        /// <summary>
        /// The other side's lines, in the order they are spoken, discovered from this contest's own
        /// key prefix.
        ///
        /// A prefix and a probe rather than a count, so how many things an enemy has to say is a
        /// decision made in the locale file where the sentences are written — a number held in
        /// contest.json instead would have to agree with two translations of a list it cannot see.
        ///
        /// Falling back to the shared lines is deliberate and noisy. A boss with the first raid's
        /// voice is a weaker scene, but a boss with an empty log is a broken one, and the warning
        /// names the prefix that came up empty so the missing sentences can be written.
        /// </summary>
        static string[] ResolveEnemyLineKeys(ContestConfig config)
        {
            string prefix = config.enemy_line_prefix;
            if (string.IsNullOrEmpty(prefix))
            {
                return DefaultEnemyLineKeys;
            }

            var keys = new List<string>();
            for (int line = 1; line <= MaxEnemyLines; line++)
            {
                string key = prefix + "." + line;
                if (!Loc.Has(key))
                {
                    break;
                }

                keys.Add(key);
            }

            if (keys.Count == 0)
            {
                Debug.LogWarning("[Contest] Locale " + Loc.LoadedLocale + " has no lines under '" + prefix +
                                 "' for contest '" + config.id + "'; the other side borrows the shared ones.");
                return DefaultEnemyLineKeys;
            }

            return keys.ToArray();
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
        /// Whether the move is on the menu at all.
        ///
        /// Two gates, and a move may carry either. A move flagged unlocked_by_page does not exist
        /// until the page has been closed — which, since the page is a fact about the run, means it
        /// stays unlocked in every later contest. A move naming a flag exists only for a player
        /// whose run raised it.
        ///
        /// What the flag gate may name is the constraint that matters, and it is rule 19 rather
        /// than a mechanism: the boss's extra move is gated on a choice made in the fiction, never
        /// on anything the player read. A move that only appeared for someone who opened a chapter
        /// would make reading pay in numbers, and the motivation would evaporate with the number.
        /// Everything else is always offered, so a player who did neither still has a full menu of
        /// valid moves — worse ones, which is the whole shape of this design.
        /// </summary>
        public bool IsMoveAvailable(string moveId)
        {
            ContestMoveDef move = FindMove(moveId);
            if (move == null)
            {
                return false;
            }

            if (move.unlocked_by_page && !_pageDismissed)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(move.unlocked_by_flag))
            {
                GameState state = State;
                if (state == null || !state.HasFlag(move.unlocked_by_flag))
                {
                    return false;
                }
            }

            return true;
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

                // _pageTurn is zero for a contest that carries no reveal, and _pageDismissed is
                // already true for a run that has seen the page — so the second contest of a
                // season falls through here on both counts and never interrupts itself.
                if (_pageTurn > 0 && turn == _pageTurn && !_pageDismissed)
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

            // The passage and the stage travel with the call. The passage because a season may put
            // its reveal anywhere and the panel must not hold an opinion about which one it is; the
            // stage because reveal_shown and verse_shown are the funnel the north-star metric is
            // read out of, and two reveal moments that reported the same properties would be
            // indistinguishable in exactly the query that has to tell them apart.
            ThePagePanel panel = ThePagePanel.Show(turn, _config.page_verse, StageId(), OnPageDismissed);
            if (panel == null)
            {
                Debug.LogWarning("[Contest] The page did not open (already seen this run, or no modal root); " +
                                 "the move it unlocks is granted anyway.");
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
                    // The same reading the fight was tuned against, taken at Begin. Re-reading a
                    // day-keyed flag here would be a second chance to name the wrong night.
                    bool watchPosted = _watchPostedLastNight;

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

                case MoveKeepCounting:
                {
                    line = Loc.T("contest.log.keep_counting");
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

        /// <summary>
        /// The lines the first raid speaks, and the ones any contest borrows when its own prefix
        /// resolves nothing. Keys rather than sentences: the words live in the locale table like
        /// every other line the player reads. The order is the order they are shown in.
        ///
        /// Spelled out in full rather than built from a prefix on purpose. The content validator
        /// reads key-shaped literals out of this tree and asserts every one of them exists in
        /// ui.json, and a bare prefix looks exactly like a key that has gone missing.
        /// </summary>
        static readonly string[] DefaultEnemyLineKeys =
        {
            "contest.log.enemy.1",
            "contest.log.enemy.2",
            "contest.log.enemy.3",
            "contest.log.enemy.4"
        };

        void ApplyEnemyPressure()
        {
            _morale = Mathf.Clamp(_morale - _pressure, 0, _moraleMax);

            string[] keys = _enemyLineKeys != null && _enemyLineKeys.Length > 0
                ? _enemyLineKeys
                : DefaultEnemyLineKeys;

            string line = Loc.T(keys[_enemyLineIndex % keys.Length]);
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

                // Keyed, and always through the helper rather than a hand-spelled string: the save
                // migration raises exactly this key for a legacy run that had already fought, and
                // one letter of drift between the two spellings would make a migrated player fight
                // the raid a second time.
                if (!string.IsNullOrEmpty(ContestId))
                {
                    state.SetFlag(GameFlags.ContestResolvedFor(ContestId));
                    state.counters[GameFlags.ContestOutcomeCounter(ContestId)] = 1 + (int)outcome;
                }

                // The flat flag stays written as well. It costs one byte, it is what any caller
                // still asking the old question reads, and it is the value the migration keys off
                // — so keeping it is what lets the keyed flag arrive without a second schema step.
                state.SetFlag(GameFlags.ContestResolved);

                try
                {
                    SaveSystem.Save(state);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Contest] Could not save after the contest: " + exception.Message);
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
                    Debug.LogError("[Contest] A listener threw while the contest ended: " + exception.Message);
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
        /// already standing never comes down. There is no defeat screen, and the stage carries on
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
        /// Finds the segment the contest is fought over, in the data and never in a constant: the
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

            Debug.LogWarning("[Contest] No wall segment could be resolved; the contest runs without one.");
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

        /// <summary>
        /// This contest's tuning. A miss is an error and not a silent fall back to the other
        /// contest's numbers: a stage that named a fight the file does not define would otherwise
        /// play the wrong one, correctly, with nothing to notice.
        /// </summary>
        static ContestConfig ReadConfig(string contestId)
        {
            try
            {
                if (string.IsNullOrEmpty(contestId))
                {
                    Debug.LogError("[Contest] No contest was named; this stage cannot pick a fight to run.");
                    return null;
                }

                ContestConfig config;
                if (GameData.Contests != null && GameData.Contests.TryGetValue(contestId, out config) && config != null)
                {
                    return config;
                }

                Debug.LogError("[Contest] contest.json defines no contest called '" + contestId + "'.");
                return null;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Contest] Could not read contest.json: " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// The id of the stage this contest is being fought on, for telemetry. Empty when there is
        /// no run in progress, which is the editor harness's case and not a player's.
        /// </summary>
        static string StageId()
        {
            GameState state = State;
            if (state == null)
            {
                return string.Empty;
            }

            try
            {
                StageDef stage = GameData.Stage(state.day);
                return stage != null ? stage.id ?? string.Empty : string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Contest] Could not name the stage for telemetry: " + exception.Message);
                return string.Empty;
            }
        }

        /// <summary>Courses of the contested segment already finished, zero to four.</summary>
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
                        return Mathf.Clamp(wall.StageOf(segmentId), 0, WallSystem.StagesPerSegment);
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

            return Mathf.Clamp(segment.stage, 0, WallSystem.StagesPerSegment);
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

            // Derived wherever the roster can be read at all — this branch is also reached with a
            // perfectly good npcs.json and no run in progress, and answering that with a literal
            // six would be a second opinion about the size of the village that nothing keeps in
            // step with the file. The constant is only for a roster that could not be read.
            int roster = npcs != null ? npcs.Length : 0;
            total = roster > 0 ? roster : FallbackVillageSize;

            if (state == null)
            {
                return 0;
            }

            return Mathf.Clamp(state.Counter(SpokenCounterKey), 0, total);
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
