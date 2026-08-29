using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// Whether the game prints chapter-and-verse next to the words it quotes.
    ///
    /// Early on it does not. A player who has not been told this is scripture reads the quotations
    /// as what they are in the fiction — things people said, written down — and the citation would
    /// answer a question they have not asked yet. Later the references appear, and from then on
    /// every quotation carries one.
    ///
    /// The distinction that keeps this honest, and the reason it is not the trap AGENTS.md rule 12
    /// warns about: the reference is **withheld from the footer, never removed from the game**. The
    /// "Saber mais" affordance stays on every quotation from the first minute, and opening it shows
    /// the chapter, its name and its numbering in full. A curious player is one tap from the whole
    /// answer at any point. What is deferred is the game volunteering it, not the player's access —
    /// and that tap is the deep_read signal the whole product exists to measure, so gating it would
    /// defeat the point twice over.
    /// </summary>
    public static class ScriptureVisibility
    {
        /// <summary>
        /// Raised the first time the game shows the page in the trial — the moment the text stops
        /// being background and becomes the strongest move available. From then on, citations show.
        /// </summary>
        public const string RevealedFlag = "references_revealed";

        /// <summary>True once the game prints references beside what it quotes.</summary>
        public static bool ReferencesVisible(GameState state)
        {
            if (state == null)
            {
                // No state to consult means no run in progress; show them rather than hide them.
                // Defaulting to concealment would make a missing save look like a design decision.
                return true;
            }

            return state.HasFlag(RevealedFlag);
        }

        /// <summary>Convenience overload for callers that have no state to hand.</summary>
        public static bool ReferencesVisible()
        {
            GameState state;
            return !ServiceLocator.TryGet(out state) || ReferencesVisible(state);
        }

        /// <summary>
        /// Turns citations on for the rest of the run. Idempotent, and deliberately one-way: a game
        /// that started showing references and then stopped would read as the game hiding something.
        /// </summary>
        public static void Reveal(GameState state)
        {
            if (state == null || state.HasFlag(RevealedFlag))
            {
                return;
            }

            state.SetFlag(RevealedFlag);
            Debug.Log("[Scripture] References are now shown beside quotations.");
        }
    }
}
