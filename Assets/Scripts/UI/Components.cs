using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The one place a UI animation is written.
    ///
    /// Every screen that needed a fade used to carry its own coroutine, its own duration and its
    /// own idea of whether reduced motion applied to it. That is three chances per screen to
    /// disagree with the design system, and the disagreement is invisible until someone watches
    /// two screens back to back. The durations live in <see cref="DesignTokens.Motion"/>; this is
    /// the thing that plays them.
    ///
    /// The split that matters is <see cref="Run"/> against <see cref="Decorative"/>. Fades and
    /// bars are information — the design system is explicit that a progress bar which does not
    /// move reads as broken rather than as calm — so they run whatever the accessibility setting
    /// says. Parallax, pulse and shake are decoration, so they check
    /// <see cref="DesignTokens.Motion.ReduceMotion"/> first and jump straight to their end state.
    ///
    /// Everything runs on unscaled time. A menu that stops animating because the world was paused
    /// looks like a hang, and this project pauses the world for every modal.
    /// </summary>
    public static class UIMotion
    {
        /// <summary>
        /// Plays a tween that carries information: fades and bars. Runs under reduced motion.
        ///
        /// <paramref name="apply"/> receives eased progress, already through
        /// <see cref="DesignTokens.Motion.EaseOutBack"/>, so no caller has to know the curve. The
        /// tween stops the moment <paramref name="target"/> is destroyed, which is what keeps a
        /// screen torn down mid-animation from throwing into the runner.
        /// </summary>
        public static Coroutine Run(UnityEngine.Object target, float duration, Action<float> apply, Action onComplete = null)
        {
            if (apply == null || target == null)
            {
                return null;
            }

            if (duration <= 0f)
            {
                apply(1f);
                if (onComplete != null)
                {
                    onComplete();
                }

                return null;
            }

            MotionRunner runner = MotionRunner.Instance;
            if (runner == null)
            {
                apply(1f);
                if (onComplete != null)
                {
                    onComplete();
                }

                return null;
            }

            return runner.StartCoroutine(Tween(target, duration, apply, onComplete));
        }

        /// <summary>
        /// Plays a tween that is decoration: parallax, pulse, shake. Under reduced motion the end
        /// state is applied at once and nothing animates.
        /// </summary>
        public static Coroutine Decorative(UnityEngine.Object target, float duration, Action<float> apply, Action onComplete = null)
        {
            if (apply == null || target == null)
            {
                return null;
            }

            if (DesignTokens.Motion.ReduceMotion)
            {
                apply(1f);
                if (onComplete != null)
                {
                    onComplete();
                }

                return null;
            }

            return Run(target, duration, apply, onComplete);
        }

        /// <summary>Fades a group to an alpha. Information, so it runs under reduced motion.</summary>
        public static Coroutine Fade(CanvasGroup group, float to, float duration, Action onComplete = null)
        {
            if (group == null)
            {
                return null;
            }

            float from = group.alpha;
            return Run(group, duration, progress =>
            {
                if (group != null)
                {
                    group.alpha = Mathf.Lerp(from, to, progress);
                }
            }, onComplete);
        }

        /// <summary>
        /// A one-shot scale bump, for a reward card or a value that just changed. Decoration: the
        /// design system suppresses it under reduced motion, and the number it decorates has
        /// already updated by then.
        /// </summary>
        public static Coroutine Pulse(RectTransform rect, float peakScale, float duration)
        {
            if (rect == null)
            {
                return null;
            }

            if (DesignTokens.Motion.ReduceMotion)
            {
                rect.localScale = Vector3.one;
                return null;
            }

            return Run(rect, duration, progress =>
            {
                if (rect == null)
                {
                    return;
                }

                // One half sine: 1 at both ends, peakScale in the middle, no overshoot to unwind.
                float scale = 1f + (peakScale - 1f) * Mathf.Sin(progress * Mathf.PI);
                rect.localScale = new Vector3(scale, scale, 1f);
            }, () =>
            {
                if (rect != null)
                {
                    rect.localScale = Vector3.one;
                }
            });
        }

        /// <summary>
        /// A horizontal shake, for a rejected input. Decoration, and the one animation most likely
        /// to make someone motion sick, so reduced motion drops it entirely rather than shortening
        /// it — a shorter shake is still a shake.
        /// </summary>
        public static Coroutine Shake(RectTransform rect, float amplitude, float duration)
        {
            if (rect == null || DesignTokens.Motion.ReduceMotion)
            {
                return null;
            }

            Vector2 origin = rect.anchoredPosition;
            return Run(rect, duration, progress =>
            {
                if (rect == null)
                {
                    return;
                }

                float decay = 1f - progress;
                rect.anchoredPosition = origin + new Vector2(Mathf.Sin(progress * Mathf.PI * 6f) * amplitude * decay, 0f);
            }, () =>
            {
                if (rect != null)
                {
                    rect.anchoredPosition = origin;
                }
            });
        }

        /// <summary>Runs an action after a delay, cancelled if the target is destroyed first.</summary>
        public static Coroutine After(UnityEngine.Object target, float delay, Action action)
        {
            if (action == null || target == null)
            {
                return null;
            }

            MotionRunner runner = MotionRunner.Instance;
            if (runner == null)
            {
                return null;
            }

            return runner.StartCoroutine(Wait(target, delay, action));
        }

        /// <summary>
        /// Runs an action at the start of the next frame.
        ///
        /// This exists for measurements: a rect built this frame has no size until the layout pass
        /// has run, so anything that reads <c>rect.width</c> to decide a geometry has to wait one
        /// frame or force a rebuild. Waiting is the cheaper of the two.
        /// </summary>
        public static Coroutine NextFrame(UnityEngine.Object target, Action action)
        {
            return After(target, 0f, action);
        }

        /// <summary>Stops a handle returned by any of the above. Null-safe on both sides.</summary>
        public static void Stop(Coroutine handle)
        {
            if (handle == null)
            {
                return;
            }

            MotionRunner runner = MotionRunner.Existing;
            if (runner != null)
            {
                runner.StopCoroutine(handle);
            }
        }

        static IEnumerator Tween(UnityEngine.Object target, float duration, Action<float> apply, Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (target == null)
                {
                    yield break;
                }

                apply(DesignTokens.Motion.EaseOutBack(elapsed / duration));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (target == null)
            {
                yield break;
            }

            apply(1f);
            if (onComplete != null)
            {
                onComplete();
            }
        }

        static IEnumerator Wait(UnityEngine.Object target, float delay, Action action)
        {
            float elapsed = 0f;
            do
            {
                yield return null;
                if (target == null)
                {
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
            }
            while (elapsed < delay);

            action();
        }
    }

    /// <summary>
    /// The MonoBehaviour that owns every UI coroutine.
    ///
    /// A shared runner rather than a coroutine per screen: a tween that outlives the panel that
    /// started it is a real case here — a success flash on a button inside a modal that closes —
    /// and a coroutine started on a destroyed behaviour dies mid-animation, leaving whatever it was
    /// moving stranded halfway. Each tween carries its own target and stops when that target dies,
    /// which is the guarantee that actually matters.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class MotionRunner : MonoBehaviour
    {
        static MotionRunner _instance;
        static bool _quitting;

        static MotionRunner()
        {
            // A tween started while the player is shutting down would otherwise create this
            // GameObject during teardown, which Unity reports as an error on the way out.
            Application.quitting += () => _quitting = true;
        }

        /// <summary>The runner, created on first use. Null once the player is quitting.</summary>
        internal static MotionRunner Instance
        {
            get
            {
                if (_quitting || !Application.isPlaying)
                {
                    return null;
                }

                if (_instance != null)
                {
                    return _instance;
                }

                var go = new GameObject("UIMotionRunner");

                // HideAndDontSave already survives a scene load, so DontDestroyOnLoad would be both
                // redundant and a warning about an object that is not a scene root.
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<MotionRunner>();
                return _instance;
            }
        }

        /// <summary>The runner if one exists. Never creates one, so stopping is free.</summary>
        internal static MotionRunner Existing
        {
            get { return _instance; }
        }

        void OnApplicationQuit()
        {
            _quitting = true;
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }

    /// <summary>
    /// The name of a control that shows no words.
    ///
    /// An icon button is a square with a glyph in it, and a glyph is not a label — a player who
    /// does not recognise the shape has nothing to fall back on, and neither does anything reading
    /// the interface on their behalf. The design system's rule is that an icon-only control always
    /// carries an accessible name, so this holds it.
    ///
    /// Unity draws the whole game into one Metal view, and this project used to note that the
    /// engine published no accessibility tree at all. That stopped being true: the project is on
    /// 6000.3.23f1 with <c>com.unity.modules.accessibility</c> 1.0.0 in the manifest, and Unity 6's
    /// Accessibility module drives VoiceOver over uGUI. Nothing consumes this label yet only
    /// because no one has built the <c>AccessibilityHierarchy</c> — that is engine-wide work, not
    /// this type's. It is still worth carrying meanwhile: the string is a real localised name
    /// rather than a comment, it is greppable, and <c>tools/acceptance.sh</c> can assert that every
    /// icon button has one. The alternative — a Text with zero alpha — would be a lie that looks
    /// like a fix.
    ///
    /// When someone does build that hierarchy: a locked wardrobe row must NOT map to a
    /// disabled-style state. <c>AGENTS.md</c> rule 7 keeps it fully interactable, and its
    /// accessible name already carries the unlock sentence whole.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AccessibleLabel : MonoBehaviour
    {
        /// <summary>The localised name of this control, as a player would say it aloud.</summary>
        public string Label { get; set; }

        /// <summary>Attaches or updates the name on a GameObject.</summary>
        public static AccessibleLabel Apply(GameObject target, string label)
        {
            if (target == null)
            {
                return null;
            }

            AccessibleLabel existing = target.GetComponent<AccessibleLabel>();
            if (existing == null)
            {
                existing = target.AddComponent<AccessibleLabel>();
            }

            existing.Label = label;
            return existing;
        }
    }

    /// <summary>
    /// A button in the Sistema Vale, in every state the system specifies: default, hover, pressed,
    /// focus, disabled, loading and success.
    ///
    /// It is a <see cref="Button"/> subclass rather than a sibling component because the two
    /// states nobody remembers — disabled and focus — are exactly the ones only the base class
    /// knows about. <c>Selectable.interactable</c> reaches
    /// <see cref="DoStateTransition(SelectionState, bool)"/> through <c>OnSetProperty</c>, so a
    /// screen that writes <c>button.interactable = false</c> the way fifteen screens already do
    /// gets the 0.40 opacity without knowing this type exists. A sibling component would have had
    /// to poll for that.
    ///
    /// <c>transition</c> is set to <see cref="Selectable.Transition.None"/> on purpose: the base
    /// tint is a multiply over the graphic's colour and cannot express "clay becomes clay-light",
    /// so this type owns the colours outright and the base is left with nothing to fight over.
    ///
    /// The states are a precedence chain, not a set. Loading beats success beats disabled beats
    /// pressed and hover — otherwise a loading button, which is not interactable, would draw at
    /// disabled opacity and read as broken rather than as busy.
    /// </summary>
    [DisallowMultipleComponent]
    public class VariantButton : Button
    {
        UIKit.ButtonVariant _variant;
        Image _fill;
        Image _border;
        Image _focusRing;
        Image _glyph;
        Image _successIcon;
        Text _label;
        RectTransform _labelRect;
        RectTransform _dotsHolder;
        Image[] _dots;
        CanvasGroup _group;

        SelectionState _state = SelectionState.Normal;
        Color _indicatorInk = Color.white;
        bool _loading;
        bool _success;
        bool _interactableBeforeLoading = true;
        bool _selectedByPointer;
        float _dotsWidth;
        Coroutine _successTimer;

        /// <summary>Which of the six skins this button wears. Fixed when it is built.</summary>
        public UIKit.ButtonVariant Variant
        {
            get { return _variant; }
        }

        public bool IsLoading
        {
            get { return _loading; }
        }

        public bool IsSuccess
        {
            get { return _success; }
        }

        /// <summary>Wires the pieces <see cref="UIKit.CreateButton"/> built. Called once.</summary>
        internal void Bind(UIKit.ButtonVariant variant, Image fill, Image border, Image focusRing, Text label, CanvasGroup group)
        {
            _variant = variant;
            _fill = fill;
            _border = border;
            _focusRing = focusRing;
            _label = label;
            _labelRect = label != null ? (RectTransform)label.transform : null;
            _group = group;
            ApplyVisuals();
        }

        /// <summary>
        /// Registers the glyph of an icon button so it is tinted with the label colour. Kept
        /// separate from <see cref="Bind"/> because the glyph is chosen by the caller, not by the
        /// variant.
        /// </summary>
        public void SetGlyph(Image glyph)
        {
            _glyph = glyph;
            ApplyVisuals();
        }

        /// <summary>
        /// Puts the button in its busy state: it stops taking input, keeps its label, and grows a
        /// row of dots in the padding beside it.
        ///
        /// The interactability the caller had set is remembered and restored, so a button that was
        /// already disabled for a game reason does not come back enabled when the work finishes.
        /// </summary>
        public void SetLoading(bool loading)
        {
            if (_loading == loading)
            {
                return;
            }

            _loading = loading;

            if (loading)
            {
                _interactableBeforeLoading = interactable;
                EnsureLoadingDots();
                interactable = false;
            }
            else
            {
                interactable = _interactableBeforeLoading;
            }

            if (_dotsHolder != null)
            {
                _dotsHolder.gameObject.SetActive(loading);
            }

            UpdateLabelInsets();
            ApplyVisuals();
        }

        /// <summary>
        /// Puts the button in its success state: a success fill and a check mark beside the label.
        ///
        /// The check is a sprite and not a character. None of the three bundled families carries a
        /// check glyph, and the design system's own rule is that a missing glyph is never solved by
        /// substituting a lookalike. It is also what keeps this state legible for anyone who cannot
        /// separate the success green from the clay: colour never carries a state alone here.
        /// </summary>
        public void SetSuccess(bool success)
        {
            if (_success == success)
            {
                return;
            }

            _success = success;

            if (success)
            {
                EnsureSuccessIcon();
            }

            if (_successIcon != null)
            {
                _successIcon.gameObject.SetActive(success);
            }

            UpdateLabelInsets();
            ApplyVisuals();
        }

        /// <summary>Shows success for the toast hold, then returns to the resting state.</summary>
        public void FlashSuccess()
        {
            FlashSuccess(DesignTokens.Motion.ToastHold);
        }

        /// <summary>Shows success for <paramref name="hold"/> seconds, then returns.</summary>
        public void FlashSuccess(float hold)
        {
            SetSuccess(true);
            UIMotion.Stop(_successTimer);
            _successTimer = UIMotion.After(this, hold, () => SetSuccess(false));
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ApplyVisuals();
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            _state = state;
            ApplyVisuals();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            // Set before the base call, not after: Selectable.OnPointerDown is what selects this
            // GameObject, so OnSelect fires inside it and has to already know a pointer caused it.
            // Only when the press can actually select something: a press on a disabled button
            // selects nothing, so no deselect ever arrives to clear the flag, and the next
            // keyboard focus after the button is re-enabled would come up without its ring.
            if (IsInteractable())
            {
                _selectedByPointer = true;
            }

            base.OnPointerDown(eventData);
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);

            // The ring is keyboard focus, not selection. uGUI leaves a button selected after a tap,
            // and a ring that appears under every finger would be noise on a phone and would show
            // up in every e2e screenshot.
            ShowFocusRing(!_selectedByPointer);
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            _selectedByPointer = false;
            ShowFocusRing(false);
        }

        void Update()
        {
            if (!_loading || _dots == null || DesignTokens.Motion.ReduceMotion)
            {
                return;
            }

            float time = Time.unscaledTime;
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i] == null)
                {
                    continue;
                }

                float phase = time * DotCyclesPerSecond - i * DotPhaseOffset;
                float wave = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f);
                _dots[i].color = UIKit.WithAlpha(_indicatorInk, Mathf.Lerp(DotAlphaFloor, 1f, wave));
            }
        }

        void ApplyVisuals()
        {
            if (_fill == null)
            {
                // DoStateTransition runs from the base OnEnable, which happens before Bind.
                return;
            }

            UIKit.ButtonSkin skin = UIKit.SkinFor(_variant);

            Color fill;
            Color ink;
            Color border = skin.Border;
            bool dimmed = false;

            if (_loading)
            {
                fill = skin.FillPressed;
                ink = skin.LabelPressed;
            }
            else if (_success)
            {
                fill = DesignTokens.Feedback.Success;
                ink = DesignTokens.Ink.OnPrimary;
                border = Color.clear;
            }
            else
            {
                switch (_state)
                {
                    case SelectionState.Highlighted:
                        fill = skin.FillHover;
                        ink = skin.LabelHover;
                        break;

                    case SelectionState.Pressed:
                        fill = skin.FillPressed;
                        ink = skin.LabelPressed;
                        break;

                    case SelectionState.Disabled:
                        // Disabled keeps its own colours and loses opacity. Recolouring it as well
                        // would read as a different control rather than as the same one, unusable.
                        fill = skin.Fill;
                        ink = skin.Label;
                        dimmed = true;
                        break;

                    default:
                        // Selected included: focus is the ring, never a fill. Leaving the hover
                        // colour on a button that was merely tapped is uGUI's oldest wrong default.
                        fill = skin.Fill;
                        ink = skin.Label;
                        break;
                }
            }

            _fill.color = fill;
            _indicatorInk = ink;

            if (_label != null)
            {
                _label.color = ink;
            }

            if (_glyph != null)
            {
                _glyph.color = ink;
            }

            if (_successIcon != null)
            {
                _successIcon.color = ink;
            }

            if (_border != null)
            {
                _border.color = border;
                _border.enabled = border.a > 0.001f;
            }

            if (_dots != null)
            {
                for (int i = 0; i < _dots.Length; i++)
                {
                    if (_dots[i] != null)
                    {
                        _dots[i].color = UIKit.WithAlpha(ink, DesignTokens.Motion.ReduceMotion ? 1f : DotAlphaFloor);
                    }
                }
            }

            if (_group != null)
            {
                _group.alpha = dimmed ? DesignTokens.Feedback.DisabledOpacity : 1f;
            }

            if (dimmed)
            {
                ShowFocusRing(false);
            }
        }

        void ShowFocusRing(bool visible)
        {
            if (_focusRing == null)
            {
                return;
            }

            _focusRing.gameObject.SetActive(visible && IsInteractable());
        }

        /// <summary>
        /// Reserves room beside the label for whichever indicator is showing.
        ///
        /// The indicators live in the button's own horizontal padding rather than in a layout
        /// group, because a layout group on a button re-centres the label the moment an indicator
        /// appears and the word visibly jumps. Reserving the space instead keeps the label where
        /// the player was already reading it.
        /// </summary>
        void UpdateLabelInsets()
        {
            if (_labelRect == null)
            {
                return;
            }

            float left = UIKit.ButtonPadding;
            float right = UIKit.ButtonPadding;

            if (_success)
            {
                left += UIKit.ButtonCheckSize + DesignTokens.Space.S8;
            }

            if (_loading)
            {
                right += _dotsWidth + DesignTokens.Space.S8;
            }

            _labelRect.offsetMin = new Vector2(left, UIKit.ButtonVerticalPadding);
            _labelRect.offsetMax = new Vector2(-right, -UIKit.ButtonVerticalPadding);
        }

        void EnsureLoadingDots()
        {
            if (_dots != null)
            {
                return;
            }

            float size = UIKit.ButtonIndicatorSize;
            float gap = DesignTokens.Space.S4;
            _dotsWidth = size * DotCount + gap * (DotCount - 1);

            _dotsHolder = UIKit.CreateRect("LoadingDots", transform);
            _dotsHolder.sizeDelta = new Vector2(_dotsWidth, size);
            PlaceIndicator(_dotsHolder, _label != null ? 1f : 0.5f);

            _dots = new Image[DotCount];
            for (int i = 0; i < DotCount; i++)
            {
                Image dot = UIKit.CreateIcon(_dotsHolder, "Dot" + i, UiSpriteKeys.IconDot, _indicatorInk, size);
                var dotRect = (RectTransform)dot.transform;
                dotRect.anchorMin = new Vector2(0f, 0.5f);
                dotRect.anchorMax = new Vector2(0f, 0.5f);
                dotRect.pivot = new Vector2(0f, 0.5f);
                dotRect.anchoredPosition = new Vector2(i * (size + gap), 0f);
                _dots[i] = dot;
            }
        }

        void EnsureSuccessIcon()
        {
            if (_successIcon != null)
            {
                return;
            }

            _successIcon = UIKit.CreateIcon(transform, "SuccessIcon", UiSpriteKeys.IconCheck,
                                            _indicatorInk, UIKit.ButtonCheckSize);
            PlaceIndicator((RectTransform)_successIcon.transform, _label != null ? 0f : 0.5f);
        }

        /// <summary>
        /// Pins an indicator inside the horizontal padding. <paramref name="edge"/> is 0 for the
        /// left inset, 1 for the right, 0.5 for the centre of a button with no label.
        /// </summary>
        void PlaceIndicator(RectTransform rect, float edge)
        {
            rect.anchorMin = new Vector2(edge, 0.5f);
            rect.anchorMax = new Vector2(edge, 0.5f);
            rect.pivot = new Vector2(edge, 0.5f);

            float offset = 0f;
            if (edge <= 0f)
            {
                offset = UIKit.ButtonPadding;
            }
            else if (edge >= 1f)
            {
                offset = -UIKit.ButtonPadding;
            }

            rect.anchoredPosition = new Vector2(offset, 0f);
        }

        const int DotCount = 3;
        const float DotCyclesPerSecond = 0.9f;
        const float DotPhaseOffset = 0.18f;
        const float DotAlphaFloor = 0.30f;
    }

    /// <summary>
    /// Progress, the only way the design system allows it to be drawn: a label, a bar and a
    /// fraction, together.
    ///
    /// A bare bar is the thing this component exists to make impossible. "Half full" answers
    /// nothing on its own — half of what, and half of how many — and the fraction is what turns a
    /// decoration into a number a player can act on. The three pieces are built together and there
    /// is no constructor that omits one.
    ///
    /// The fraction is mono and tabular so the digits do not shuffle sideways as the value climbs,
    /// which is the design system's rule for every quantity in the game.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProgressBar : MonoBehaviour
    {
        /// <summary>Locale key for "current of total". Words never live in a .cs file.</summary>
        const string FractionFormatKey = "common.fraction";

        Text _label;
        Text _fraction;
        RectTransform _track;
        RectTransform _fill;
        Coroutine _tween;

        int _current;
        int _total;
        float _shown;

        /// <summary>What the bar is currently drawing, 0 to 1.</summary>
        public float Shown
        {
            get { return _shown; }
        }

        /// <summary>What the bar is heading for, 0 to 1. Zero when the total is zero.</summary>
        public float Value
        {
            get { return _total > 0 ? Mathf.Clamp01((float)_current / _total) : 0f; }
        }

        public int Current
        {
            get { return _current; }
        }

        public int Total
        {
            get { return _total; }
        }

        internal void Bind(Text label, Text fraction, RectTransform track, RectTransform fill)
        {
            _label = label;
            _fraction = fraction;
            _track = track;
            _fill = fill;
            SetValue(0, 0);
        }

        /// <summary>
        /// Moves the bar to a new value and rewrites the fraction.
        ///
        /// The transition takes <see cref="DesignTokens.Motion.BarFill"/>, and under reduced motion
        /// it snaps instead. Snapping is not the same as freezing: the design system's line is that
        /// a progress bar which does not move reads as broken rather than as calm, and a bar that
        /// arrives instantly has still moved. What reduced motion drops is the travel, not the
        /// news.
        /// </summary>
        public void SetValue(int current, int total)
        {
            _total = Mathf.Max(total, 0);
            _current = Mathf.Clamp(current, 0, _total);

            if (_fraction != null)
            {
                _fraction.text = SheepGate.Core.Loc.T(FractionFormatKey, _current, _total);
            }

            bool animate = isActiveAndEnabled && !DesignTokens.Motion.ReduceMotion;
            ApplyValue(Value, animate);
        }

        /// <summary>Replaces the label. The words arrive localised; this never composes them.</summary>
        public void SetLabel(string label)
        {
            if (_label != null)
            {
                _label.text = label ?? string.Empty;
            }
        }

        void OnEnable()
        {
            // A panel built hidden and shown later never ran a layout pass, so the track has no
            // width yet and the capped fill width below cannot be computed. Snap now with whatever
            // is known, then correct once the first layout has happened.
            ApplyValue(Value, false);
            UIMotion.NextFrame(this, () =>
            {
                if (_tween == null)
                {
                    DrawFill(_shown);
                }
            });
        }

        void OnDisable()
        {
            // A tween runs on the shared runner and would otherwise keep writing into a panel the
            // player cannot see, and would leave the fill stranded halfway when it came back.
            StopTween();
            _shown = Value;
            DrawFill(_shown);
        }

        void ApplyValue(float target, bool animate)
        {
            StopTween();

            if (!animate)
            {
                _shown = target;
                DrawFill(_shown);
                return;
            }

            float from = _shown;
            _tween = UIMotion.Run(this, DesignTokens.Motion.BarFill, progress =>
            {
                _shown = Mathf.Lerp(from, target, progress);
                DrawFill(_shown);
            }, () => _tween = null);
        }

        void StopTween()
        {
            if (_tween != null)
            {
                UIMotion.Stop(_tween);
                _tween = null;
            }
        }

        /// <summary>
        /// Draws the fill at a fraction of the track.
        ///
        /// The width is clamped to at least the track's own height, because the fill is a pill: a
        /// nine-slice narrower than its two rounded caps renders as a squashed lozenge and reads as
        /// a rendering fault rather than as a small number. At exactly zero the fill is hidden
        /// instead, which is the only honest way to draw nothing.
        /// </summary>
        void DrawFill(float fraction)
        {
            if (_fill == null)
            {
                return;
            }

            fraction = Mathf.Clamp01(fraction);
            bool visible = fraction > 0.0001f;
            if (_fill.gameObject.activeSelf != visible)
            {
                _fill.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            float trackWidth = _track != null ? _track.rect.width : 0f;
            if (trackWidth <= 1f)
            {
                // No layout pass yet. Anchor proportionally so the bar is never wrong, and let
                // OnEnable's next-frame pass replace this with the capped width.
                _fill.anchorMax = new Vector2(fraction, 1f);
                _fill.sizeDelta = Vector2.zero;
                return;
            }

            float minimum = _track != null ? _track.rect.height : 0f;
            float width = Mathf.Clamp(trackWidth * fraction, minimum, trackWidth);
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.sizeDelta = new Vector2(width, 0f);
        }
    }
}
