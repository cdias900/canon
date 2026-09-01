using SheepGate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// One line, for a beat, when the game has to say why nothing happened.
    ///
    /// ==================================================================================
    /// WHY THIS EXISTS
    /// ==================================================================================
    /// The village answers a tap in one of three ways: it does the thing, it walks you there and
    /// does the thing, or it does nothing at all. The third was silent. Tapping a wall with no
    /// material, a pile already collected today, a pile whose day has not come, or the well after
    /// the fish is caught all took the same path — a <c>Debug.Log</c> in English, into a console no
    /// player will ever open — and on screen they were indistinguishable from a tap that missed.
    ///
    /// This project already learned that lesson once, on a button covered by a CanvasGroup:
    /// reachable is not the same as operable, and a control that refuses in silence looks exactly
    /// like a broken one. The wall is the central verb of the game. It cannot refuse in silence.
    ///
    /// ==================================================================================
    /// WHAT IT IS NOT
    /// ==================================================================================
    /// <list type="bullet">
    ///   <item><b>Not a modal.</b> It takes no input lock, pushes nothing onto
    ///   <see cref="ModalRoot"/> and never blocks a tap: <c>raycastTarget</c> is off on every
    ///   graphic it builds. A message about a tap that did nothing must not be able to eat the
    ///   next one.</item>
    ///   <item><b>Not a reward and not a scolding.</b> It states a fact — the work is spent, the
    ///   pile is empty — and never tells the player what they should have done instead. Rule 13's
    ///   voice test: it may disagree, it may not pastor.</item>
    ///   <item><b>Not a queue.</b> A second message replaces the first rather than lining up
    ///   behind it. Someone tapping a spent wall four times is asking the same question four
    ///   times, and four toasts is the game nagging; the same one, held again, is an answer.</item>
    /// </list>
    ///
    /// ==================================================================================
    /// PLACEMENT
    /// ==================================================================================
    /// Bottom of the screen, above the safe-area inset, under the two thumb-corner controls the
    /// HUD keeps out there. It sits below the dialogue canvas and below every modal, because a
    /// message about the village has nothing to say over a conversation or a panel — and above the
    /// HUD, because the HUD is what it is usually answering for.
    ///
    /// Rule 4 applies: the line is laid over the scene, so it sits on the Glass card, which is
    /// <c>Surface.SceneVeil</c> at 88%.
    /// </summary>
    public sealed class Toast : MonoBehaviour
    {
        /// <summary>
        /// Above the HUD (50), below dialogue (100). A toast is about the world and never has
        /// anything to add over a conversation.
        /// </summary>
        public const int CanvasSortingOrder = 80;

        /// <summary>Distance from the safe-area floor to the card.</summary>
        static readonly float BottomMargin =
            DesignTokens.Space.SafeAreaBottom + DesignTokens.Space.TouchTarget + DesignTokens.Space.S12;

        /// <summary>Padding from the card's edge to the line inside it.</summary>
        static readonly float PaddingX = DesignTokens.Space.S16;
        static readonly float PaddingY = DesignTokens.Space.S12;

        /// <summary>Floor under the card's height, so a one-line message still reads as a card.</summary>
        static readonly float MinimumHeight = DesignTokens.Space.S32;

        static Toast _current;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _card;
        Text _line;
        Coroutine _run;

        /// <summary>The message on screen right now, or empty. Read by the e2e run.</summary>
        public static string Showing
        {
            get
            {
                if (_current == null || _current._line == null || _current._group == null)
                {
                    return string.Empty;
                }

                return _current._group.alpha > 0f ? _current._line.text : string.Empty;
            }
        }

        /// <summary>
        /// Puts one line on screen for <see cref="DesignTokens.Motion.ToastHold"/> seconds.
        ///
        /// Takes a resolved sentence rather than a key, so a caller can pass a formatted one; every
        /// caller in the game resolves it with <c>Loc.T</c> immediately before the call, which is
        /// what keeps this file free of player-facing words.
        /// </summary>
        public static void Show(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Toast toast = Ensure();
            if (toast == null)
            {
                return;
            }

            toast.Display(message);
        }

        /// <summary>Takes the toast off screen at once. Used when a modal or a conversation opens.</summary>
        public static void Dismiss()
        {
            if (_current == null)
            {
                return;
            }

            _current.StopRun();
            if (_current._group != null)
            {
                _current._group.alpha = 0f;
            }
        }

        static Toast Ensure()
        {
            if (_current != null)
            {
                return _current;
            }

            // Belongs to the scene that raised it, exactly as ModalRoot does. A toast that outlived
            // a scene load would be a sentence about a village that is no longer there — and the
            // restart reloads the whole game from Boot.
            var host = new GameObject("Toast");

            _current = host.AddComponent<Toast>();
            _current.Build();

            // A modal or a conversation owns the screen. Dismissing rather than queueing is the
            // same rule the class states about repeats: the answer to a question asked while the
            // world was being played is not worth showing over the panel that replaced it.
            ModalRoot.OpenStateChanged += OnModalOpenStateChanged;

            return _current;
        }

        void Build()
        {
            _canvas = UIKit.CreateCanvas("ToastCanvas", CanvasSortingOrder);
            _canvas.transform.SetParent(transform, false);

            RectTransform root = UIKit.SafeArea(_canvas);

            Image card = UIKit.CreateCard(root, "ToastCard", UIKit.CardStyle.Glass);

            // Nothing here ever takes a tap. A message explaining that a tap did nothing must not
            // be able to swallow the next one — and the card spans most of the screen's width.
            card.raycastTarget = false;

            _card = (RectTransform)card.transform;
            _card.anchorMin = new Vector2(0f, 0f);
            _card.anchorMax = new Vector2(1f, 0f);
            _card.pivot = new Vector2(0.5f, 0f);
            _card.offsetMin = new Vector2(DesignTokens.Space.Gutter, BottomMargin);
            _card.offsetMax = new Vector2(-DesignTokens.Space.Gutter, BottomMargin + MinimumHeight);

            _line = UIKit.CreateText(_card, "ToastLine", string.Empty,
                DesignTokens.Type.Body, DesignTokens.Ink.OnScene, TextAnchor.MiddleCenter);
            UIKit.Stretch((RectTransform)_line.transform, PaddingX, PaddingX, PaddingY, PaddingY);

            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            // Belt and braces with the raycastTarget above: whatever a future edit adds under this
            // canvas, the whole thing stays deaf to the finger.
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        void Display(string message)
        {
            _line.text = message;

            // The card grows to fit the sentence, in whichever language is running. Measured off
            // the text rather than given a height, because the English and the Portuguese of the
            // same message do not wrap at the same word.
            //
            // The width is taken from the canvas rather than from the card's own rect: the rect is
            // resolved by the layout pass, and the first toast of a session is shown in the same
            // frame the canvas was built — a rect of zero there would measure the sentence as
            // infinitely tall and the card would be drawn off the top of the screen.
            float width = UIKit.CanvasWidth() - 2f * DesignTokens.Space.Gutter - 2f * PaddingX;
            if (width <= 0f)
            {
                width = UIKit.ReferenceWidth - 2f * DesignTokens.Space.Gutter - 2f * PaddingX;
            }

            var generator = new TextGenerator();
            TextGenerationSettings settings = _line.GetGenerationSettings(new Vector2(width, 0f));
            float height = generator.GetPreferredHeight(message, settings) / _line.pixelsPerUnit;

            // Never shorter than one line's worth of card, so a message that measures oddly still
            // has a plate under it rather than a strip of nothing.
            _card.sizeDelta = new Vector2(_card.sizeDelta.x,
                                          Mathf.Max(MinimumHeight, height + 2f * PaddingY));

            StopRun();
            _run = StartCoroutine(Cycle());
        }

        void StopRun()
        {
            if (_run != null)
            {
                StopCoroutine(_run);
                _run = null;
            }
        }

        System.Collections.IEnumerator Cycle()
        {
            // The fades are the design system's own toast durations, and they keep running under
            // reduced motion: rule says parallax, pulse and shake stop, fades do not. A line that
            // appeared and vanished instantly would read as a glitch, which is the opposite of
            // what reduced motion is for.
            yield return FadeTo(1f, DesignTokens.Motion.ToastIn);
            yield return new WaitForSeconds(DesignTokens.Motion.ToastHold);
            yield return FadeTo(0f, DesignTokens.Motion.ToastOut);

            _run = null;
        }

        System.Collections.IEnumerator FadeTo(float target, float seconds)
        {
            float from = _group.alpha;
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(from, target, seconds <= 0f ? 1f : elapsed / seconds);
                yield return null;
            }

            _group.alpha = target;
        }

        static void OnModalOpenStateChanged(bool open)
        {
            if (open)
            {
                Dismiss();
            }
        }

        void OnDestroy()
        {
            // A static event outlives the scene this toast belongs to, so the handler has to come
            // off with it. Removing one that was never added is a no-op.
            ModalRoot.OpenStateChanged -= OnModalOpenStateChanged;

            if (_current == this)
            {
                _current = null;
            }
        }
    }
}
