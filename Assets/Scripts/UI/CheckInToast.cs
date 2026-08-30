using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Economy;
using SheepGate.Player;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// Shows the one pending daily check-in reward, if <see cref="DailyCheckIn.PendingResult"/> is
    /// set. Waits for a quiet screen the same way <see cref="SheepGate.Quiz.DailyQuiz"/> does, so it
    /// never lands on top of the day-1 opening or a conversation. Has nothing left to do once shown,
    /// so unlike DailyQuiz it does not resubscribe to anything — one boot, at most one toast.
    /// </summary>
    public class CheckInToast : MonoBehaviour
    {
        public const string ModalId = "check_in_toast";

        const float SettleSeconds = 0.6f;

        RectTransform _container;
        DialogueSystem _dialogue;
        DayCycle _dayCycle;
        bool _lockHeld;
        float _earliestShowTime;
        bool _shown;

        void Start()
        {
            if (!DailyCheckIn.PendingResult.HasValue)
            {
                enabled = false;
                return;
            }

            _earliestShowTime = Time.unscaledTime + SettleSeconds;
            _dialogue = FindFirstObjectByType<DialogueSystem>();
            _dayCycle = FindFirstObjectByType<DayCycle>();
        }

        void Update()
        {
            if (_shown || !DailyCheckIn.PendingResult.HasValue)
            {
                return;
            }

            if (Time.unscaledTime < _earliestShowTime || !IsScreenQuiet())
            {
                return;
            }

            Show(DailyCheckIn.PendingResult.Value);
        }

        /// <summary>True when nothing else owns the screen — the same rule DailyQuiz applies.</summary>
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

        void Show(DailyCheckIn.Result result)
        {
            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[CheckInToast] No modal root is available; the reward is not shown, but it was already paid.");
                DailyCheckIn.PendingResult = null;
                enabled = false;
                return;
            }

            _shown = true;
            _container = root.Push(ModalId);
            if (_container == null)
            {
                DailyCheckIn.PendingResult = null;
                enabled = false;
                return;
            }

            _lockHeld = true;
            InputLock.Push();

            Build(result);
        }

        void Build(DailyCheckIn.Result result)
        {
            Image card = UIKit.CreateCard(_container, "CheckInCard", UIKit.CardStyle.Card);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0f, 0.5f);
            cardRect.anchorMax = new Vector2(1f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.offsetMin = new Vector2(DesignTokens.Space.Gutter, cardRect.offsetMin.y);
            cardRect.offsetMax = new Vector2(-DesignTokens.Space.Gutter, cardRect.offsetMax.y);

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S20), Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S24), Mathf.RoundToInt(DesignTokens.Space.S24)));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string rewardKey = result.TalentsAwarded >= 3 ? "checkin.reward_many" : "checkin.reward_one";
            UIKit.CreateText(card.transform, "Reward", Loc.T(rewardKey), DesignTokens.Type.Title,
                DesignTokens.Ink.Primary, TextAnchor.UpperLeft, DesignTokens.TypeRole.Title);

            UIKit.CreateButton(card.transform, "Continue", Loc.T("checkin.continue"),
                UIKit.ButtonVariant.Primary, Close);

            UIKit.RebuildNow(cardRect);
        }

        void Close()
        {
            _container = null;
            ModalRoot.CloseId(ModalId);

            if (_lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }

            DailyCheckIn.PendingResult = null;
            enabled = false;
        }

        void OnDestroy()
        {
            if (_lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }
        }
    }
}
