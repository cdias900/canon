using System;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Player;
using SheepGate.UI;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.Quiz
{
    /// <summary>
    /// One question per day, from quiz.json.
    ///
    /// It is a check-in, not a test. Answering is what counts, and answering wrong costs exactly
    /// nothing: no score, no streak, no points, no second chance to earn back, and not one word
    /// telling the player they were wrong. The note that follows is there to inform, and it is
    /// the same note either way.
    ///
    /// It schedules itself. The morning is when the check-in belongs, so the component subscribes
    /// to <see cref="DayCycle.MorningStarted"/>, then waits
    /// for a quiet screen — no modal, no conversation, no night resolving — before sliding in. The
    /// day is recorded in a persisted counter the moment the panel appears, so a day gets its
    /// question exactly once even if the player closes the app and comes back.
    ///
    /// It gates nothing. A day with no authored question, a missing modal root, a player who never
    /// answers: in every case the rest of the day carries on untouched.
    ///
    /// Nothing here is scripture. The prompt, the options and the note are authored pt-BR from
    /// quiz.json, which cites references and never quotes text.
    /// </summary>
    public class DailyQuiz : MonoBehaviour
    {
        public const string ModalId = "daily_quiz";

        const string SeenCounterPrefix = "quiz_seen_d";
        const string AnswerCounterPrefix = "quiz_answer_d";

        /// <summary>
        /// Left and right inset of the card. The screen gutter, so the check-in lines up with
        /// every other panel in the game rather than with the number it happened to be built at.
        /// </summary>
        static readonly float SideMargin = DesignTokens.Space.Gutter;

        /// <summary>Padding inside the card: a little tighter across than down the page.</summary>
        static readonly float CardPaddingX = DesignTokens.Space.S20;
        static readonly float CardPaddingY = DesignTokens.Space.S24;

        /// <summary>Quiet time the screen must have before a queued check-in appears.</summary>
        const float SettleSeconds = 0.6f;

        /// <summary>How often the scene is searched again for the systems this component watches.</summary>
        const float BindRetrySeconds = 0.5f;

        /// <summary>Raised when the panel is gone, whether it was answered or dismissed.</summary>
        public event Action Closed;

        RectTransform _container;
        RectTransform _card;
        Text _noteText;
        Text _hookText;
        Button _continueButton;

        readonly List<Button> _optionButtons = new List<Button>();

        QuizQuestion _question;
        int _day;
        bool _answered;
        bool _lockHeld;

        DayCycle _dayCycle;
        DialogueSystem _dialogue;
        int _pendingDay;
        float _earliestShowTime;
        float _nextBindAttempt;

        public bool IsShowing
        {
            get { return _container != null; }
        }

        /// <summary>The day queued for a check-in that has not appeared yet, or 0.</summary>
        public int PendingDay
        {
            get { return _pendingDay; }
        }

        /// <summary>Finds the quiz component in the scene, creating a host object when there is none.</summary>
        public static DailyQuiz EnsureInstance()
        {
            DailyQuiz existing = FindFirstObjectByType<DailyQuiz>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("DailyQuiz");
            return go.AddComponent<DailyQuiz>();
        }

        // ------------------------------------------------------------------ scheduling

        /// <summary>
        /// Queues the check-in for a day instead of showing it at once. The morning starts with the
        /// report on screen and often a conversation right after it, and a panel that lands on top
        /// of those reads as an interruption rather than as a question.
        ///
        /// A day already seen, a day with no authored question and a day already queued are all
        /// dropped in silence: this is called on every morning and on every scene start, so it has
        /// to be safe to call more often than it acts.
        /// </summary>
        public void RequestForDay(int day)
        {
            if (day <= 0 || IsShowing || _pendingDay == day)
            {
                return;
            }

            GameState state = ResolveState();
            if (state != null && state.Counter(SeenCounterPrefix + day) != 0)
            {
                return;
            }

            if (FindQuestion(day) == null)
            {
                return;
            }

            _pendingDay = day;
            _earliestShowTime = Time.unscaledTime + SettleSeconds;
        }

        void OnDuskBegan(int day)
        {
            // Only when the night can no longer be turned down. The mat lets a player ask for the
            // evening with work still in hand and then think better of it, and a check-in spent on
            // an evening that gets deferred is a check-in the real end of the day never gets — on
            // top of holding the split shut while the player is still deciding.
            if (_dayCycle != null && _dayCycle.CanDeferDusk)
            {
                return;
            }

            RequestForDay(day);
        }

        void OnMorningStarted(int day)
        {
            // Every other day asks at dusk, so the question closes the session and its hook is
            // the last thing read before the split. The dedication has no dusk once the gate is
            // earned, so the last day is the one morning that still asks.
            StageDef stage = GameData.Stage(day);
            if (stage != null && stage.terminal)
            {
                RequestForDay(day);
            }
        }

        void TryShowPending()
        {
            if (_pendingDay <= 0 || IsShowing)
            {
                return;
            }

            if (Time.unscaledTime < _earliestShowTime || !IsScreenQuiet())
            {
                return;
            }

            int day = _pendingDay;

            // Cleared first: Show can finish synchronously on the paths where there is nothing to
            // display, and a day left queued there would be retried on every frame.
            _pendingDay = 0;
            Show(day);
        }

        /// <summary>
        /// True when nothing else owns the screen. The dialogue system is asked directly because a
        /// conversation holds neither the modal stack nor the input lock.
        /// </summary>
        bool IsScreenQuiet()
        {
            if (ModalRoot.IsOpen || InputLock.IsLocked)
            {
                return false;
            }

            if (_dayCycle != null && _dayCycle.IsResolving)
            {
                return false;
            }

            if (_dialogue != null && _dialogue.IsPlaying)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Finds the day cycle and the dialogue system, retrying on a slow timer while either is
        /// missing so composition order cannot leave this component deaf.
        /// </summary>
        void EnsureBindings()
        {
            if (_dayCycle != null && _dialogue != null)
            {
                return;
            }

            if (Time.unscaledTime < _nextBindAttempt)
            {
                return;
            }

            _nextBindAttempt = Time.unscaledTime + BindRetrySeconds;

            if (_dayCycle == null)
            {
                BindDayCycle();
            }

            if (_dialogue == null)
            {
                _dialogue = FindFirstObjectByType<DialogueSystem>();
            }
        }

        void BindDayCycle()
        {
            DayCycle cycle = null;
            try
            {
                ServiceLocator.TryGet(out cycle);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Quiz] Could not resolve the day cycle: " + exception.Message);
                cycle = null;
            }

            if (cycle == null)
            {
                cycle = FindFirstObjectByType<DayCycle>();
            }

            if (cycle == null)
            {
                return;
            }

            _dayCycle = cycle;
            _dayCycle.MorningStarted += OnMorningStarted;

            // A day that reaches its evening without having been checked in gets one now. In
            // practice that is day one, which opens in a cutscene and has no morning at all — but
            // it is written as a rule rather than as "if day == 1", because RequestForDay already
            // refuses a day it has shown, so this can only fire for a check-in genuinely missed.
            _dayCycle.DuskBegan += OnDuskBegan;
        }

        void UnbindDayCycle()
        {
            if (_dayCycle == null)
            {
                return;
            }

            _dayCycle.MorningStarted -= OnMorningStarted;
            _dayCycle.DuskBegan -= OnDuskBegan;
            _dayCycle = null;
        }

        // ------------------------------------------------------------------ the panel

        /// <summary>
        /// Shows the question authored for this day. A day with no question closes immediately and
        /// still raises <see cref="Closed"/>, so a caller can always chain on it.
        /// </summary>
        public void Show(int day)
        {
            if (IsShowing)
            {
                Debug.LogWarning("[Quiz] The check-in is already on screen.");
                return;
            }

            _day = day;
            _question = FindQuestion(day);
            if (_question == null || string.IsNullOrEmpty(_question.prompt))
            {
                Debug.LogWarning("[Quiz] No question is authored for day " + day + " in quiz.json.");
                RaiseClosed();
                return;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[Quiz] No modal root is available; the check-in was skipped.");
                RaiseClosed();
                return;
            }

            _container = root.Push(ModalId);
            if (_container == null)
            {
                RaiseClosed();
                return;
            }

            _answered = false;
            _optionButtons.Clear();

            GameState state = ResolveState();
            if (state != null)
            {
                EnsureCounters(state);
                state.counters[SeenCounterPrefix + day] = 1;

                // Written to disk here, not at the end of the day: the guard that keeps a day to one
                // question has to survive the app being closed on this very panel.
                PersistState(state);
            }

            // Queued for this day or not, it is on screen now; nothing may show it twice.
            if (_pendingDay == day)
            {
                _pendingDay = 0;
            }

            if (!_lockHeld)
            {
                _lockHeld = true;
                InputLock.Push();
            }

            Build();
        }

        /// <summary>Closes the panel. Always available and never refused.</summary>
        public void Close()
        {
            if (_container == null)
            {
                return;
            }

            _container = null;
            _card = null;
            _noteText = null;
            _hookText = null;
            _continueButton = null;
            _optionButtons.Clear();

            ModalRoot.CloseId(ModalId);

            if (_lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }

            RaiseClosed();
        }

        void OnDestroy()
        {
            UnbindDayCycle();

            if (_lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }
        }

        void Update()
        {
            ReleaseIfClosedFromOutside();
            EnsureBindings();
            TryShowPending();
        }

        /// <summary>
        /// Watchdog for a panel closed from outside, such as the hardware back button: the world
        /// must not stay locked and a caller waiting on <see cref="Closed"/> must not stall.
        /// </summary>
        void ReleaseIfClosedFromOutside()
        {
            if (!_lockHeld || _container != null)
            {
                return;
            }

            _card = null;
            _noteText = null;
            _hookText = null;
            _continueButton = null;
            _optionButtons.Clear();

            _lockHeld = false;
            InputLock.Pop();
            RaiseClosed();
        }

        static void EnsureCounters(GameState state)
        {
            if (state.counters == null)
            {
                state.counters = new Dictionary<string, int>();
            }
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Builds the card.
        ///
        /// SURFACE: a modal is elev.2 in the design system, which it defines as
        /// <c>Surface.Card</c> over <c>Surface.Scrim</c>. <see cref="ModalRoot"/> draws the scrim,
        /// so the card is a <see cref="UIKit.CardStyle.Card"/> and not the pergaminho. The
        /// pergaminho is the surface for reading at length, and it is also the one surface in the
        /// game where the secondary and ghost button skins — near-white fills — disappear. This
        /// screen is three quarters buttons, so it takes the surface the buttons were drawn for
        /// and leaves the scroll to the vocation reveal.
        ///
        /// Height is left to a <see cref="ContentSizeFitter"/>. The card grows twice: once for the
        /// prompt, which is authored per day and per locale, and again when the note and the way
        /// out appear after an answer.
        /// </summary>
        void Build()
        {
            Image card = UIKit.CreateCard(_container, "QuizCard", UIKit.CardStyle.Card);
            _card = (RectTransform)card.transform;
            _card.anchorMin = new Vector2(0f, 0.5f);
            _card.anchorMax = new Vector2(1f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.offsetMin = new Vector2(SideMargin, _card.offsetMin.y);
            _card.offsetMax = new Vector2(-SideMargin, _card.offsetMax.y);

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, Pad(CardPaddingX, CardPaddingY));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Eyebrow in the accent, which is what marks a thing as new. Gold reads at about 8:1
            // on this card, and the words carry the meaning on their own if it does not.
            UIKit.CreateText(
                _card,
                "Eyebrow",
                Loc.T("quiz.eyebrow"),
                DesignTokens.Type.Mono,
                DesignTokens.Brand.Secondary,
                TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);

            UIKit.CreateText(
                _card,
                "Prompt",
                _question.prompt,
                DesignTokens.Type.Title,
                DesignTokens.Ink.Primary,
                TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Title);

            BuildOptions();

            _noteText = UIKit.CreateText(
                _card,
                "Note",
                string.Empty,
                DesignTokens.Type.Body,
                DesignTokens.Ink.Secondary,
                TextAnchor.UpperLeft);
            _noteText.gameObject.SetActive(false);

            // Tomorrow, in one line, under the answer. The question closes the session now, so
            // this is the last thing read before the split: a hook into the next day's chapter,
            // never a reward. Muted, and absent on the last day, which has no tomorrow.
            _hookText = UIKit.CreateText(
                _card,
                "Hook",
                string.Empty,
                DesignTokens.Type.Body,
                DesignTokens.Ink.Muted,
                TextAnchor.UpperLeft);
            _hookText.gameObject.SetActive(false);

            _continueButton = UIKit.CreateButton(
                _card,
                "Continue",
                Loc.T("quiz.continue"),
                UIKit.ButtonVariant.Primary,
                Close);
            _continueButton.gameObject.SetActive(false);

            // The option labels wrap, and how many lines they wrap to depends on the day, the
            // locale and the width the phone ended up with. Measure once the card has a real
            // width, then give each button the height its own label turned out to need.
            UIKit.RebuildNow(_card);
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                FitOptionHeight(_optionButtons[i]);
            }

            UIKit.RebuildNow(_card);
        }

        /// <summary>
        /// The options, as design-system secondary buttons: a translucent fill with a hairline
        /// border, which is the variant for "one of several equal choices".
        ///
        /// They are grouped in a rect of their own so the gap between two touch targets can be
        /// tighter than the gap between the sections of the card, and still clear the accessibility
        /// floor of eight design points between them.
        /// </summary>
        void BuildOptions()
        {
            string[] options = _question.options;
            if (options == null || options.Length == 0)
            {
                Debug.LogWarning("[Quiz] The question for day " + _day + " has no options.");
                return;
            }

            RectTransform group = UIKit.CreateRect("Options", _card);
            UIKit.VerticalGroup(group.gameObject, DesignTokens.Space.S12, new RectOffset());

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                Button button = UIKit.CreateButton(
                    group,
                    "Option_" + i,
                    options[i] ?? string.Empty,
                    UIKit.ButtonVariant.Secondary,
                    () => OnOptionChosen(index));

                // Left-aligned: these are sentences, and a centred sentence that wraps to two
                // lines makes a ragged shape the eye has to re-find on every row.
                Text label = LabelOf(button);
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleLeft;
                }

                _optionButtons.Add(button);
            }
        }

        /// <summary>
        /// Grows one option button until its wrapped label fits inside it, never below the touch
        /// target of 48 design points.
        ///
        /// This is called again after an answer, because both marks this screen can apply — the
        /// check on the answer and the dot on the player's own choice — take room out of the
        /// label's leading padding and can push it onto one more line. The content is data, so the
        /// day where that happens cannot be ruled out by reading quiz.json today.
        /// </summary>
        void FitOptionHeight(Button button)
        {
            if (button == null)
            {
                return;
            }

            Text label = LabelOf(button);
            LayoutElement layout = UIKit.Layout(button);
            if (label == null || layout == null)
            {
                return;
            }

            float needed = label.preferredHeight + UIKit.ButtonVerticalPadding * 2f;
            float height = Mathf.Max(UIKit.ButtonMinHeight, needed);

            layout.minHeight = height;
            layout.preferredHeight = height;
        }

        /// <summary>
        /// Marks the option the player chose, when it was not the one quiz.json names.
        ///
        /// A dot in <c>Feedback.Info</c>, in the same leading padding the success check uses, so
        /// the two marks sit in one column and the row reads as "this is the answer, and this is
        /// what you said" rather than as a correction. It is information and it is drawn as
        /// information: <c>Feedback.Error</c> never appears on this screen.
        ///
        /// A dot and not a character. None of the three bundled font families carries a bullet,
        /// and the design system's rule is that a missing glyph is never solved by substituting a
        /// lookalike, so status marks are sprites.
        /// </summary>
        void MarkChosen(Button button)
        {
            Text label = LabelOf(button);
            if (label == null)
            {
                return;
            }

            Image mark = UIKit.CreateIcon(button.transform, "ChoiceMark", UiSpriteKeys.IconDot,
                                          DesignTokens.Feedback.Info, UIKit.ButtonCheckSize);
            var markRect = (RectTransform)mark.transform;
            markRect.anchorMin = new Vector2(0f, 0.5f);
            markRect.anchorMax = new Vector2(0f, 0.5f);
            markRect.pivot = new Vector2(0f, 0.5f);
            markRect.anchoredPosition = new Vector2(UIKit.ButtonPadding, 0f);

            // The same inset VariantButton reserves for its own check. This button never enters
            // the loading or success state, so nothing will recompute the insets underneath us.
            var labelRect = (RectTransform)label.transform;
            labelRect.offsetMin = new Vector2(
                UIKit.ButtonPadding + UIKit.ButtonCheckSize + DesignTokens.Space.S8,
                UIKit.ButtonVerticalPadding);
            labelRect.offsetMax = new Vector2(-UIKit.ButtonPadding, -UIKit.ButtonVerticalPadding);
        }

        /// <summary>The button's own label, by name, so an added mark cannot be picked up instead.</summary>
        static Text LabelOf(Button button)
        {
            if (button == null)
            {
                return null;
            }

            Transform label = button.transform.Find("Label");
            return label != null ? label.GetComponent<Text>() : null;
        }

        /// <summary>Layout padding from two spacing tokens. RectOffset is integral; tokens are not.</summary>
        static RectOffset Pad(float horizontal, float vertical)
        {
            int h = Mathf.RoundToInt(horizontal);
            int v = Mathf.RoundToInt(vertical);
            return new RectOffset(h, h, v, v);
        }

        // ------------------------------------------------------------------ answering

        /// <summary>
        /// Records the answer and shows the note. The only difference a wrong answer makes is
        /// which option carries the quiet mark afterwards; nothing is added or taken away.
        ///
        /// Nothing on this path is allowed to read as a penalty, so nothing here uses
        /// <c>Feedback.Error</c>. There is no cross, no red, no score and no word for "wrong":
        /// the answer is shown in <c>Feedback.Success</c> with a check beside it, the option the
        /// player actually chose carries a <c>Feedback.Info</c> dot when it was a different one,
        /// and the note underneath is the same note either way. Colour never carries either of
        /// those states alone — both are a mark plus a colour, which is the accessibility rule and
        /// also what keeps the screen legible to a player who reads the two hues as one.
        /// </summary>
        void OnOptionChosen(int index)
        {
            if (_answered)
            {
                return;
            }

            _answered = true;

            GameState state = ResolveState();
            if (state != null)
            {
                // Stored as one-based so "no answer" and "first option" stay distinguishable.
                // The answer is recorded the same way whether it matches quiz.json or not: the
                // check-in counts because it was answered, not because it was answered correctly.
                EnsureCounters(state);
                state.counters[AnswerCounterPrefix + _day] = index + 1;
                PersistState(state);
            }

            int correct = _question != null ? _question.answer : -1;

            for (int i = 0; i < _optionButtons.Count; i++)
            {
                Button button = _optionButtons[i];
                if (button == null)
                {
                    continue;
                }

                // Every option stops taking taps and keeps its words. The design system draws a
                // disabled control at 0.40 with its label intact, which is what turns the row into
                // something to read rather than something that has been taken away.
                button.interactable = false;

                if (i == correct)
                {
                    // The success state is a fill plus a check sprite, and it outranks disabled in
                    // the button's own precedence chain, so the answer stays at full strength
                    // while everything around it recedes. Set, never flashed: the answer has to
                    // still be on screen while the player reads the note under it.
                    var variant = button as VariantButton;
                    if (variant != null)
                    {
                        variant.SetSuccess(true);
                    }

                    FitOptionHeight(button);
                }
                else if (i == index)
                {
                    MarkChosen(button);
                    FitOptionHeight(button);
                }
            }

            if (_noteText != null && _question != null)
            {
                _noteText.text = _question.note ?? string.Empty;
                _noteText.gameObject.SetActive(!string.IsNullOrEmpty(_noteText.text));
            }

            if (_hookText != null && _question != null)
            {
                _hookText.text = _question.hook ?? string.Empty;
                _hookText.gameObject.SetActive(!string.IsNullOrEmpty(_hookText.text));
            }

            if (_continueButton != null)
            {
                _continueButton.gameObject.SetActive(true);
            }

            if (_card != null)
            {
                UIKit.RebuildNow(_card);
            }
        }

        // ------------------------------------------------------------------ helpers

        static QuizQuestion FindQuestion(int day)
        {
            QuizQuestion[] questions;
            try
            {
                questions = GameData.Quiz;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Quiz] Could not read quiz.json: " + exception.Message);
                return null;
            }

            if (questions == null)
            {
                return null;
            }

            for (int i = 0; i < questions.Length; i++)
            {
                QuizQuestion question = questions[i];
                if (question != null && question.day == day)
                {
                    return question;
                }
            }

            return null;
        }

        static GameState ResolveState()
        {
            GameState state;
            try
            {
                if (ServiceLocator.TryGet(out state) && state != null)
                {
                    return state;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Quiz] Could not read the game state: " + exception.Message);
            }

            return null;
        }

        static void PersistState(GameState state)
        {
            if (state == null)
            {
                return;
            }

            try
            {
                SaveSystem.Save(state);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Quiz] The check-in could not be written to the save: " + exception.Message);
            }
        }

        void RaiseClosed()
        {
            Action handler = Closed;
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
                Debug.LogError("[Quiz] A listener threw while the check-in closed: " + exception.Message);
            }
        }

        void Awake()
        {
            SheepGate.Vocation.VocationTracker.EnsureRegistered();
        }

        /// <summary>
        /// Binds to the day cycle and queues the day the scene opened on. Day one never raises a
        /// morning — the run starts there — and a save resumed on day two or three would otherwise
        /// wait for a morning that already happened, so the opening day is asked for directly. The
        /// persisted counter is what stops that becoming a second showing.
        /// </summary>
        void Start()
        {
            // Deliberately does NOT request a check-in for the day the scene opens on. Composing the
            // village used to queue one immediately, which put a question on screen on top of the
            // opening - the first thing a new player met was a quiz. The check-in now arrives only
            // from MorningStarted, so it belongs to a morning the player has earned by ending a day.
            EnsureBindings();
        }
    }
}
