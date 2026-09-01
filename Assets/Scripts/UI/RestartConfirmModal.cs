using SheepGate.Core;
using SheepGate.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The one question worth interrupting the player for: are you sure you want this run gone?
    ///
    /// <b>Why a modal and not a second tap.</b> A button that means "start over" on the first press
    /// and "yes, really" on the second is a control whose meaning depends on something the player
    /// cannot see, and the cost of getting it wrong here is the whole run. The question is asked in
    /// words, on its own surface, with the destructive answer named rather than implied — "Apagar e
    /// começar", not "OK".
    ///
    /// <b>Rule 7 is not in play.</b> Never punish is about what the game does to a player who did
    /// badly; this is a player asking, on purpose, to put a run down. What the rule does buy them
    /// is the sentence saying plainly what goes, before it goes.
    ///
    /// <b>Order of teardown, which is the part that took a read of three files to get right.</b>
    /// Deleting the file first and reloading second is not enough: the state is cached in a static
    /// that outlives a scene load, and tearing down the village runs teardown that writes the run
    /// back out. So writes stop first, then the file goes, then the cache and the locator are
    /// emptied, and only then does Boot run — which finds no save and starts a new game.
    /// </summary>
    public sealed class RestartConfirmModal : MonoBehaviour
    {
        public const string ModalId = "restart_confirm";

        /// <summary>The scene that runs <see cref="BootSequence.Run"/>.</summary>
        const string SceneBoot = "Boot";

        bool _closed;

        public static RestartConfirmModal Show()
        {
            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[RestartConfirmModal] No modal root is available; the question cannot be asked.");
                return null;
            }

            if (root.IsIdOpen(ModalId))
            {
                return null;
            }

            RectTransform container;
            RectTransform card = root.PushCard(ModalId, out container);
            if (card == null)
            {
                return null;
            }

            var modal = container.gameObject.AddComponent<RestartConfirmModal>();
            modal.Build(card);
            return modal;
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

        void Build(RectTransform card)
        {
            UIKit.CreateText(card, "RestartTitle", Loc.T("settings.restart.title"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Title);

            // No height is set on this. The card is a vertical layout group, so it asks the Text
            // how tall it needs to be at the width it was given — which is the only moment that
            // width is known. Reading preferredHeight here instead would read it before any layout
            // pass has run, and a row of height zero is what that produces.
            UIKit.CreateText(card, "RestartBody", Loc.T("settings.restart.body"),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);

            BuildActions(card);
        }

        /// <summary>
        /// The two answers, stacked rather than side by side.
        ///
        /// Side by side would put a destructive control and its escape within a thumb's width of
        /// each other at the bottom of the screen. Stacked, each one is a full-width target, and
        /// the safe answer sits nearest the thumb because that is the one a mis-tap should hit.
        ///
        /// <b>Colour and word, never colour alone.</b> The confirm is drawn in
        /// <c>Feedback.Error</c> and also says what it will do. Neither signal is load-bearing on
        /// its own, which is the design system's rule 2.
        /// </summary>
        void BuildActions(RectTransform card)
        {
            RectTransform actions = UIKit.CreateRect("Actions", card);
            UIKit.VerticalGroup(actions.gameObject, DesignTokens.Space.TouchGap, new RectOffset());

            Button confirm = UIKit.CreateButton(actions, "RestartConfirm", Loc.T("settings.restart.confirm"),
                DesignTokens.Feedback.Error, DesignTokens.Ink.OnPrimary, Restart);
            StretchAction(confirm);

            Button cancel = UIKit.CreateButton(actions, "RestartCancel", Loc.T("settings.restart.cancel"),
                UIKit.ButtonVariant.Secondary, Close);
            StretchAction(cancel);
        }

        /// <summary>Full width, and never under the design system's 48 point floor.</summary>
        static void StretchAction(Button button)
        {
            LayoutElement layout = UIKit.Layout(button);
            layout.flexibleWidth = 1f;
            layout.minHeight = UIKit.ButtonMinHeight;
            layout.preferredHeight = UIKit.ButtonMinHeight;

            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
        }

        void Restart()
        {
            _closed = true;

            Telemetry.Track(TelemetryEvents.RunRestarted, null);
            Telemetry.Flush();

            // First, so nothing that runs during teardown can write the run back out.
            SaveSystem.SuspendWrites();
            SaveSystem.Delete();

            WorldRuntime.Forget();
            ServiceLocator.Clear();

            Debug.Log("[RestartConfirmModal] The run was deleted; reloading from Boot.");
            SceneManager.LoadScene(SceneBoot);
        }
    }
}
