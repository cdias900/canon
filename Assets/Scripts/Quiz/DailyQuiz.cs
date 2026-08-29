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
    /// to <see cref="DayCycle.MorningStarted"/> and asks for the day the scene opens on, then waits
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

        const float SideMargin = 56f;
        const float CardPadding = 44f;

        /// <summary>Quiet time the screen must have before a queued check-in appears.</summary>
        const float SettleSeconds = 0.6f;

        /// <summary>How often the scene is searched again for the systems this component watches.</summary>
        const float BindRetrySeconds = 0.5f;

        /// <summary>Raised when the panel is gone, whether it was answered or dismissed.</summary>
        public event Action Closed;

        RectTransform _container;
        RectTransform _card;
        Text _noteText;
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

        void OnMorningStarted(int day)
        {
            RequestForDay(day);
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
        }

        void UnbindDayCycle()
        {
            if (_dayCycle == null)
            {
                return;
            }

            _dayCycle.MorningStarted -= OnMorningStarted;
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

        void Build()
        {
            Image card = UIKit.CreatePanel(_container, "QuizCard", UIKit.Palette.Parchment, UiSpriteKeys.Panel);
            _card = (RectTransform)card.transform;
            _card.anchorMin = new Vector2(0f, 0.5f);
            _card.anchorMax = new Vector2(1f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.offsetMin = new Vector2(SideMargin, _card.offsetMin.y);
            _card.offsetMax = new Vector2(-SideMargin, _card.offsetMax.y);

            UIKit.VerticalGroup(
                card.gameObject,
                22f,
                new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.CreateText(
                _card,
                "Eyebrow",
                "UMA PERGUNTA. NÃO VALE NOTA.",
                UIKit.FontSize.Meta,
                UIKit.Palette.Stone,
                TextAnchor.UpperLeft);

            UIKit.CreateText(
                _card,
                "Prompt",
                _question.prompt,
                UIKit.FontSize.Heading,
                UIKit.Palette.Ink,
                TextAnchor.UpperLeft);

            BuildOptions();

            _noteText = UIKit.CreateText(
                _card,
                "Note",
                string.Empty,
                UIKit.FontSize.Body,
                UIKit.Palette.Ink,
                TextAnchor.UpperLeft);
            _noteText.lineSpacing = 1.1f;
            _noteText.gameObject.SetActive(false);

            _continueButton = UIKit.CreateButton(
                _card,
                "Continue",
                "Continuar",
                UIKit.Palette.Clay,
                UIKit.Palette.Parchment,
                Close);
            LayoutElement continueLayout = UIKit.Layout(_continueButton);
            if (continueLayout != null)
            {
                continueLayout.minHeight = 112f;
                continueLayout.preferredHeight = 112f;
            }

            _continueButton.gameObject.SetActive(false);
        }

        void BuildOptions()
        {
            string[] options = _question.options;
            if (options == null || options.Length == 0)
            {
                Debug.LogWarning("[Quiz] The question for day " + _day + " has no options.");
                return;
            }

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                Button button = UIKit.CreateButton(
                    _card,
                    "Option_" + i,
                    options[i] ?? string.Empty,
                    UIKit.Palette.PanelSoft,
                    UIKit.Palette.Parchment,
                    () => OnOptionChosen(index));

                LayoutElement layout = UIKit.Layout(button);
                if (layout != null)
                {
                    layout.minHeight = 116f;
                    layout.preferredHeight = 116f;
                }

                Text label = button.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.fontSize = UIKit.FontSize.Body;
                    label.alignment = TextAnchor.MiddleLeft;
                }

                _optionButtons.Add(button);
            }
        }

        // ------------------------------------------------------------------ answering

        /// <summary>
        /// Records the answer and shows the note. The only difference a wrong answer makes is
        /// which option carries the quiet mark afterwards; nothing is added or taken away.
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

                button.interactable = false;

                if (i == correct)
                {
                    UIKit.TintButton(button, UIKit.Palette.Olive, UIKit.Palette.Parchment);
                }
                else if (i == index)
                {
                    UIKit.TintButton(button, UIKit.Palette.PanelSoft, UIKit.Palette.Parchment);
                }
                else
                {
                    UIKit.TintButton(button, UIKit.Palette.Panel, UIKit.Palette.Stone);
                }
            }

            if (_noteText != null && _question != null)
            {
                _noteText.text = _question.note ?? string.Empty;
                _noteText.gameObject.SetActive(!string.IsNullOrEmpty(_noteText.text));
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
            EnsureBindings();

            GameState state = ResolveState();
            RequestForDay(state != null ? state.day : 1);
        }
    }
}
