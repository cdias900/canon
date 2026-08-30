using System.Collections;
using SheepGate.Core;
using SheepGate.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The congratulations modal shown right after the HUD's coin button pays out a check-in.
    ///
    /// Player-initiated, like <see cref="BackpackPanel"/> — the reward only exists because the
    /// player tapped for it, so this does not queue itself or wait for a quiet screen the way
    /// <see cref="SheepGate.Quiz.DailyQuiz"/> does, and it does not hold <c>InputLock</c> either;
    /// the tap that opened it already proves the screen was interactive a moment ago.
    /// </summary>
    public sealed class CheckInRewardModal : MonoBehaviour
    {
        public const string ModalId = "check_in_reward";

        /// <summary>How many past days the timeline draws before it stops adding pips.</summary>
        const int TimelineCap = 7;

        static readonly float PipSize = DesignTokens.Space.S16;
        static readonly float PopSeconds = 0.22f;

        RectTransform _card;
        bool _closed;

        public static bool IsOpen
        {
            get { return ModalRoot.IsOpen && ModalRoot.Instance != null && ModalRoot.Instance.IsIdOpen(ModalId); }
        }

        /// <summary>
        /// Opens the modal for one check-in outcome. <paramref name="newTotal"/> is the balance
        /// after the reward (state.talents, already updated by the caller) and
        /// <paramref name="tomorrowTalents"/> is what continuing the streak one more day would pay —
        /// computed by the caller via <see cref="DailyCheckIn.TalentsForStreak"/> on
        /// <c>result.Streak + 1</c>, never by this class, so the modal never re-derives game rules
        /// it was only handed the answer to.
        /// </summary>
        public static void Show(DailyCheckIn.Result result, int newTotal, int tomorrowTalents)
        {
            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[CheckInRewardModal] No modal root is available; the reward was paid but not shown.");
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

            var modal = container.gameObject.AddComponent<CheckInRewardModal>();
            modal.Build(container, result, newTotal, tomorrowTalents);
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

        // ------------------------------------------------------------------ construction

        void Build(RectTransform container, DailyCheckIn.Result result, int newTotal, int tomorrowTalents)
        {
            Image card = UIKit.CreateCard(container, "CheckInRewardCard", UIKit.CardStyle.Card);
            _card = (RectTransform)card.transform;
            _card.anchorMin = new Vector2(0f, 0.5f);
            _card.anchorMax = new Vector2(1f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.offsetMin = new Vector2(DesignTokens.Space.Gutter, _card.offsetMin.y);
            _card.offsetMax = new Vector2(-DesignTokens.Space.Gutter, _card.offsetMax.y);

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S20), Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S24), Mathf.RoundToInt(DesignTokens.Space.S24)),
                TextAnchor.UpperCenter);

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.CreateText(card.transform, "Title", Loc.T("checkin.title"), DesignTokens.Type.Title,
                DesignTokens.Ink.Primary, TextAnchor.MiddleCenter, DesignTokens.TypeRole.Title);

            string rewardKey = result.TalentsAwarded >= 3 ? "checkin.reward_many" : "checkin.reward_one";
            UIKit.CreateText(card.transform, "Reward", Loc.T(rewardKey), DesignTokens.Type.Title,
                DesignTokens.Brand.Secondary, TextAnchor.MiddleCenter, DesignTokens.TypeRole.Title);

            BuildTimeline(card.transform, result.Streak, tomorrowTalents);

            UIKit.CreateButton(card.transform, "Continue", Loc.T("checkin.continue"),
                UIKit.ButtonVariant.Primary, Close);

            UIKit.RebuildNow(_card);
            StartCoroutine(PopIn());
        }

        /// <summary>
        /// One pip per day already claimed (capped at <see cref="TimelineCap"/>, most recent first,
        /// so a long streak still reads instead of running off the card), then one more pip for
        /// tomorrow — dim, since it has not happened, captioned with the number continuing the
        /// streak would pay. This is the whole "sense of the days logged in" the reward asks for:
        /// derived from the streak count alone, nothing new persisted for it.
        /// </summary>
        void BuildTimeline(Transform parent, int streak, int tomorrowTalents)
        {
            RectTransform row = UIKit.CreateRect("Timeline", parent);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S8, new RectOffset(),
                                  TextAnchor.MiddleCenter);

            int pastPips = Mathf.Clamp(streak, 0, TimelineCap);
            for (int i = 0; i < pastPips; i++)
            {
                UIKit.CreateIcon(row, "Past_" + i, UiSpriteKeys.IconDot, DesignTokens.Brand.Secondary, PipSize);
            }

            RectTransform tomorrowCell = UIKit.CreateRect("Tomorrow", row);
            UIKit.VerticalGroup(tomorrowCell.gameObject, DesignTokens.Space.S4, new RectOffset(),
                                TextAnchor.MiddleCenter);

            UIKit.CreateIcon(tomorrowCell, "Icon", UiSpriteKeys.IconDot, DesignTokens.Ink.Muted, PipSize);

            UIKit.CreateText(tomorrowCell, "Label", Loc.T("checkin.tomorrow", tomorrowTalents),
                DesignTokens.Type.Minimum, DesignTokens.Ink.Muted, TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// A brief scale-up on open — the one flourish this screen spends on the word
        /// "congratulations". Never a loop, never a flash: rule 13's checklist bars anything that
        /// reads as a slot-machine payout, and a single settle is the line this stays on the right
        /// side of.
        /// </summary>
        IEnumerator PopIn()
        {
            if (_card == null)
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
