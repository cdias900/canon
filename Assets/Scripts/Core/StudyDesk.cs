using System.Collections.Generic;

namespace SheepGate.Core
{
    /// <summary>One extra thing to go and do. Authored, and finished by playing rather than by tapping.</summary>
    public readonly struct ExtraMission
    {
        public readonly string Id;
        public readonly string TitleKey;
        public readonly string LineKey;

        public ExtraMission(string id, string titleKey, string lineKey)
        {
            Id = id;
            TitleKey = titleKey;
            LineKey = lineKey;
        }
    }

    /// <summary>
    /// One suggested study: an authored title and line, and the passage behind it.
    ///
    /// <b><see cref="Reference"/> is a reference and never text.</b> That is rule 1 and it is
    /// structural here rather than conventional: this struct has nowhere to put a verse, so no
    /// caller can put one in, and the words the player reads come out of the corpus by reference
    /// like every other citation in the game.
    /// </summary>
    public readonly struct Study
    {
        public readonly string Id;
        public readonly string TitleKey;
        public readonly string LineKey;
        public readonly string Reference;

        /// <summary>
        /// True when the title and line are words rather than locale keys.
        ///
        /// The authored table carries keys, because authored text is translated. A suggestion from
        /// the endpoint arrives already written, in the language it was asked in — there is nothing
        /// to look up, and looking it up would render the sentence as its own key.
        /// </summary>
        public readonly bool IsLiteral;

        public Study(string id, string titleKey, string lineKey, string reference)
        {
            Id = id;
            TitleKey = titleKey;
            LineKey = lineKey;
            Reference = reference;
            IsLiteral = false;
        }

        // The parameters are deliberately not called title/line. tools/validate-content.mjs derives
        // its player-facing sinks from parameter NAMES, so a constructor taking a "title" makes
        // every locale key passed to the authored table look like a hardcoded string and fails the
        // build. Same trap that once caught a parameter called "label".
        Study(string id, string writtenTitle, string writtenLine, string reference, bool literal)
        {
            Id = id;
            TitleKey = writtenTitle;
            LineKey = writtenLine;
            Reference = reference;
            IsLiteral = literal;
        }

        /// <summary>A suggestion whose words came back written, not as keys.</summary>
        public static Study Written(string id, string writtenTitle, string writtenLine, string reference)
        {
            return new Study(id, writtenTitle, writtenLine, reference, true);
        }
    }

    /// <summary>
    /// Reads what the player has actually done and answers with extra missions and with studies
    /// worth their time.
    ///
    /// ==================================================================================
    /// THIS IS THE SEAM, AND IT IS DELIBERATELY NOT THE MODEL
    /// ==================================================================================
    /// The plan is for a model to do the choosing: look at how somebody has been playing and put
    /// the passage that speaks to it in front of them. <b>That call cannot happen here.</b> Rule 16
    /// says no AI key ever reaches the client, and this POC has no server to hold one — so what
    /// lives in this class is the shape of the answer, filled from an authored table, behind a
    /// signature a server can take over without any screen knowing.
    ///
    /// Two things the model will inherit rather than invent, and both are why the seam is worth
    /// having before the model exists:
    /// <list type="bullet">
    ///   <item><b>It returns references, never text.</b> <see cref="Study.Reference"/> has no room
    ///   for a verse. A model that tried to write one would have nowhere to put it.</item>
    ///   <item><b>It never names the book on the card.</b> The player reads an authored line about
    ///   something they did; <c>ScriptureVisibility</c> owns when chapter-and-verse appears, exactly
    ///   as it does for every other citation. This is rule 12, and a study list that announced its
    ///   sources would undo it on one screen.</item>
    /// </list>
    ///
    /// When the server does arrive, the offline table below stays as the fallback. The game is
    /// offline at runtime by design, and a screen that goes blank without a network would be worse
    /// than a screen whose suggestions are merely general.
    /// </summary>
    public static class StudyDesk
    {
        /// <summary>
        /// The pre-set interaction signals the suggestions are chosen from — the "profile" a model
        /// would be handed. Read from the save, so what the screen shows moves with the run.
        /// </summary>
        public readonly struct Signals
        {
            public readonly int Conversations;
            public readonly int WallStages;
            public readonly bool Read;
            public readonly bool WentDownTheValley;
            public readonly bool StoodTheTrial;

            public Signals(int conversations, int wallStages, bool read, bool wentDown, bool trial)
            {
                Conversations = conversations;
                WallStages = wallStages;
                Read = read;
                WentDownTheValley = wentDown;
                StoodTheTrial = trial;
            }
        }

        static readonly ExtraMission[] Missions =
        {
            new ExtraMission("mission_round", "profile.mission.round.title", "profile.mission.round.line"),
            new ExtraMission("mission_neighbours", "profile.mission.neighbours.title", "profile.mission.neighbours.line"),
            new ExtraMission("mission_chapter", "profile.mission.chapter.title", "profile.mission.chapter.line")
        };

        // The authored table. Every entry carries a reference that tools/verses.manifest.json
        // already fetches, because a study pointing at a passage the build does not ship would open
        // an empty reader. NEH.6.15 was the obvious fourth choice and is not in the manifest, so the
        // entry uses NEH.6.3 instead rather than adding a reference the build would have to refetch.
        static readonly Study[] Catalogue =
        {
            new Study("study_hands", "profile.study.hands.title", "profile.study.hands.line", "NEH.4.17"),
            new Study("study_watch", "profile.study.watch.title", "profile.study.watch.line", "NEH.4.9"),
            new Study("study_share", "profile.study.share.title", "profile.study.share.line", "NEH.3.1"),
            new Study("study_finish", "profile.study.finish.title", "profile.study.finish.line", "NEH.6.3")
        };

        /// <summary>Reads the run into the shape a suggestion is chosen from.</summary>
        public static Signals Read(GameState state)
        {
            if (state == null)
            {
                return new Signals(0, 0, false, false, false);
            }

            int stages = 0;
            if (state.segments != null)
            {
                for (int i = 0; i < state.segments.Count; i++)
                {
                    WallSegmentState segment = state.segments[i];
                    if (segment != null && segment.stage > 0)
                    {
                        stages += segment.stage;
                    }
                }
            }

            return new Signals(
                state.Counter("npcs_talked"),
                stages,
                state.HasFlag(GameFlags.ChapterOpened),
                state.HasFlag(GameFlags.AcceptedInvite),
                state.HasFlag(GameFlags.ContestResolved));
        }

        /// <summary>The extra missions on offer. Authored, and the same for everybody so far.</summary>
        public static IReadOnlyList<ExtraMission> MissionsFor(GameState state)
        {
            return Missions;
        }

        /// <summary>
        /// Studies worth this player's time, most relevant first.
        ///
        /// The choosing is a plain reading of the signals, and it stays plain on purpose: this is
        /// the behaviour the model has to beat, and a heuristic nobody can follow is a baseline
        /// nobody can tell it has beaten. Somebody who has spent their days talking is pointed at
        /// the passage about the people who built; somebody who has spent them building is pointed
        /// at the one about building with one hand.
        /// </summary>
        public static IReadOnlyList<Study> SuggestFor(GameState state)
        {
            Signals signals = Read(state);
            var chosen = new List<Study>(Catalogue.Length);

            if (signals.WallStages >= signals.Conversations)
            {
                Add(chosen, "study_hands");
                Add(chosen, "study_share");
            }
            else
            {
                Add(chosen, "study_share");
                Add(chosen, "study_hands");
            }

            if (signals.WentDownTheValley || signals.StoodTheTrial)
            {
                Add(chosen, "study_watch");
            }

            if (signals.StoodTheTrial)
            {
                Add(chosen, "study_finish");
            }

            return chosen;
        }

        static void Add(List<Study> into, string id)
        {
            for (int i = 0; i < Catalogue.Length; i++)
            {
                if (Catalogue[i].Id == id)
                {
                    into.Add(Catalogue[i]);
                    return;
                }
            }
        }
    }
}
