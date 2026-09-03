using System;
using System.Collections;
using SheepGate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The first thing on screen at every launch, and — for a run that already has a character —
    /// the screen that hands the game back to the player instead of dropping them into the middle
    /// of a day they last saw hours ago.
    ///
    /// <b>It is two screens in one component on purpose.</b> A launch always shows the wordmark;
    /// what happens next depends on whether this save has a character. Without one, the mark fades
    /// on its own and the opening cutscene takes over, because <b>MVP-SCOPE §04 decides that the
    /// opening is a cutscene and not a menu</b> — the first thing a NEW player sees has to be a
    /// city rather than a form, and a title screen with a Play button in front of it would be
    /// exactly the form that rule forbids. With a character, the mark is joined by the figure the
    /// player built, the name they chose and where the season left them, behind one button. The
    /// rule is about the first impression; this is the tenth, and the tenth wants its bearings.
    ///
    /// <b>Why a modal rather than a scene.</b> Everything the day must not do while this is up —
    /// the light falling, dusk resolving, the split panel opening on its own — is already held off
    /// by <see cref="ModalRoot.IsOpen"/> through <see cref="World.DayCycle.DuskWaits"/>. A new
    /// scene would need every one of those holds reinvented, and would have to load and unload the
    /// world twice. This way the village is composed and waiting behind the card, so Jogar costs
    /// nothing: it closes a panel.
    ///
    /// <b>There is one save and Jogar resumes it.</b> The game has always loaded the last state at
    /// boot; this screen does not choose between states, because there is only ever one. What it
    /// adds is the pause and the bearings before that state starts moving again.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        const string ModalId = "title_screen";

        /// <summary>How long the wordmark holds when there is nothing to press.</summary>
        const float SplashSeconds = 1.4f;

        /// <summary>The fade out, short enough that it reads as a hand-off rather than a scene change.</summary>
        const float FadeSeconds = 0.28f;

        static readonly float FigureHeight = DesignTokens.Px(180f);

        /// <summary>
        /// Once per launch, not once per compose.
        ///
        /// <see cref="World.GameScene.Compose"/> runs again on a language switch — the toggle
        /// reloads the scene, which is the only way every runtime-built label gets rebuilt — and a
        /// static survives that load. Without this latch, changing the language would put the
        /// player back on a screen they had already dismissed, which reads as the game restarting.
        /// It is a static rather than a save flag because it is about this launch, not this run: a
        /// player who quits and comes back should see it again, and does.
        /// </summary>
        static bool _shownThisLaunch;

        CanvasGroup _group;
        bool _closed;

        /// <summary>
        /// Shows the screen if this launch has not shown it. Returns true when something was put
        /// on screen, so the caller knows whether the world is being held.
        /// </summary>
        public static bool ShowOnce(GameState state)
        {
            if (_shownThisLaunch)
            {
                return false;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                // Not an error worth failing a launch over: the world behind is composed and
                // playable, and the only thing lost is the wordmark.
                Debug.LogWarning("[TitleScreen] No modal root; the launch goes straight to the game.");
                return false;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return false;
            }

            _shownThisLaunch = true;

            var screen = container.gameObject.AddComponent<TitleScreen>();
            screen.Build(state);
            return true;
        }

        /// <summary>
        /// Whether this save has a character to show. A name is the test rather than the
        /// appearance, because appearance is a struct that always has a value: every run has some
        /// body and some hair, and only a run that went through creation has a name the player
        /// typed. A save far into the season with no name is a corrupt save, and showing a portrait
        /// of nobody over the top of it would hide that rather than surface it.
        /// </summary>
        public static bool HasCharacter(GameState state)
        {
            return state != null && !string.IsNullOrEmpty(state.playerName);
        }

        /// <summary>Forgets that this launch showed the screen. For tests and for the e2e runner.</summary>
        public static void ResetForTesting()
        {
            _shownThisLaunch = false;
        }

        void Build(GameState state)
        {
            var container = (RectTransform)transform;

            _group = container.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f;

            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Card);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0f, 0.5f);
            cardRect.anchorMax = new Vector2(1f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(-2f * DesignTokens.Space.Gutter, 0f);
            cardRect.anchoredPosition = Vector2.zero;

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S24),
                Mathf.RoundToInt(DesignTokens.Space.S24)));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The wordmark, and it is type rather than a drawing. Rule 13 rules out the imagery a
            // title card reaches for by reflex, and a name set in the game's own display face says
            // what this is without any of it.
            UIKit.CreateText(cardRect, "Wordmark", Loc.T("title.wordmark"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.MiddleCenter,
                DesignTokens.TypeRole.Title);

            UIKit.CreateText(cardRect, "Chapter", Loc.T("title.chapter"),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.MiddleCenter);

            if (!HasCharacter(state))
            {
                // Nothing to press. The mark holds, fades, and the opening takes the screen.
                StartCoroutine(HoldThenClose());
                return;
            }

            BuildReturningPlayer(cardRect, state);
        }

        /// <summary>
        /// The figure, the name and where the season stopped, then the one button.
        ///
        /// The three readings are deliberately the three the player cannot get anywhere else
        /// without playing: the character is only visible in the backpack, the name only on the
        /// gate at the end, and the stage nowhere at all outside the HUD's day counter.
        /// </summary>
        void BuildReturningPlayer(RectTransform cardRect, GameState state)
        {
            RectTransform figureArea = UIKit.CreateRect("FigureArea", cardRect);
            figureArea.sizeDelta = new Vector2(0f, FigureHeight);

            var layout = figureArea.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = FigureHeight;
            layout.minHeight = FigureHeight;

            // The same paper doll the backpack and the creation preview draw, through the class
            // that exists so no screen keeps a private copy of the draw order. Facing down, like
            // every other flat figure in the interface: this is a portrait, not a turn.
            RectTransform figure = CharacterFigure.CreateFigureRect(figureArea, "Figure");
            CharacterFigure.Apply(CharacterFigure.Build(figure), state.appearance,
                SheepGate.Player.FacingDirection.Down);

            UIKit.CreateText(cardRect, "PlayerName", SheepGate.Player.CharacterCatalog.PlayerName(state),
                DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.MiddleCenter,
                DesignTokens.TypeRole.BodyStrong);

            // The same sentence the HUD uses for the same number, so the two readings of "where am
            // I" cannot drift apart. The stage's id is not shown: it is an authoring handle
            // (enemies_rise), never a thing a player was told.
            UIKit.CreateText(cardRect, "Stage", string.Format(Loc.T("hud.day"), state.day),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.MiddleCenter);

            UIKit.CreateButton(cardRect, "Play", Loc.T("title.play"),
                UIKit.ButtonVariant.Primary, Close);
        }

        IEnumerator HoldThenClose()
        {
            yield return new WaitForSeconds(SplashSeconds);
            Close();
        }

        /// <summary>Fades out and hands the screen back. Idempotent — a double tap closes once.</summary>
        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            StartCoroutine(FadeAndClose());
        }

        IEnumerator FadeAndClose()
        {
            if (_group != null)
            {
                // Untouchable the moment it starts leaving, so a second tap during the fade cannot
                // land on a button that is on its way out. This project has already lost a click to
                // a CanvasGroup that was still raycasting while it animated.
                _group.blocksRaycasts = false;
                _group.interactable = false;

                float elapsed = 0f;
                while (elapsed < FadeSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    _group.alpha = Mathf.Clamp01(1f - elapsed / FadeSeconds);
                    yield return null;
                }
            }

            ModalRoot.CloseId(ModalId);
        }
    }
}
