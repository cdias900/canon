using System.Collections;
using SheepGate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The note a player gets on the first launch of a day they were away for.
    ///
    /// It replaced the check-in reward card — "Parabéns! +1 talento. Volte amanhã para ganhar
    /// mais" — with its streak pips and its preview of tomorrow's payout. That card was the one
    /// hook the game had for coming back, and it contradicted the game: a streak is a debt, and
    /// rule 7 says absence never costs anything. This says the opposite, in one sentence: nothing
    /// moved without you and nothing went backwards, the wall is where you left it. It appears
    /// only after a real day away, never on the first launch of a run and never twice in a day,
    /// and it pays nothing.
    /// </summary>
    public sealed class WelcomeBackModal : MonoBehaviour
    {
        public const string ModalId = "welcome_back";

        static readonly float PopSeconds = 0.22f;

        RectTransform _card;
        bool _closed;

        public static bool IsOpen
        {
            get { return ModalRoot.IsOpen && ModalRoot.Instance != null && ModalRoot.Instance.IsIdOpen(ModalId); }
        }

        /// <summary>Shows the note for a player who was away this many whole days (at least one).</summary>
        public static void Show(int daysAway)
        {
            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[WelcomeBackModal] No modal root is available; the note cannot open.");
                return;
            }

            if (root.IsIdOpen(ModalId))
            {
                return;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return;
            }

            var modal = container.gameObject.AddComponent<WelcomeBackModal>();
            modal.Build(container, Mathf.Max(1, daysAway));
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            ModalRoot.CloseId(ModalId);
        }

        void Build(RectTransform container, int daysAway)
        {
            Image card = UIKit.CreateCard(container, "WelcomeBackCard", UIKit.CardStyle.Card);
            _card = (RectTransform)card.transform;
            _card.anchorMin = new Vector2(0f, 0.5f);
            _card.anchorMax = new Vector2(1f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.offsetMin = new Vector2(DesignTokens.Space.Gutter, _card.offsetMin.y);
            _card.offsetMax = new Vector2(-DesignTokens.Space.Gutter, _card.offsetMax.y);

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S20), Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S24), Mathf.RoundToInt(DesignTokens.Space.S24)),
                TextAnchor.UpperLeft);

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.CreateText(card.transform, "Title", Loc.T("welcome.title"), DesignTokens.Type.Title,
                DesignTokens.Ink.Primary, TextAnchor.UpperLeft, DesignTokens.TypeRole.Title);

            UIKit.CreateText(card.transform, "Body", Loc.Plural("welcome.away", daysAway), DesignTokens.Type.Body,
                DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);

            UIKit.CreateText(card.transform, "Grace", Loc.T("welcome.grace"), DesignTokens.Type.Body,
                DesignTokens.Ink.Primary, TextAnchor.UpperLeft);

            UIKit.CreateButton(card.transform, "Continue", Loc.T("welcome.continue"),
                UIKit.ButtonVariant.Primary, Close);

            UIKit.RebuildNow(_card);
            StartCoroutine(PopIn());
        }

        IEnumerator PopIn()
        {
            if (_card == null || DesignTokens.Motion.ReduceMotion)
            {
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < PopSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / PopSeconds);
                float eased = 1f - (1f - t) * (1f - t);
                float scale = Mathf.Lerp(0.85f, 1f, eased);
                _card.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            _card.localScale = Vector3.one;
        }
    }
}
