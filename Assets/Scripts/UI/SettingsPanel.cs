using SheepGate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The settings modal. One setting so far, language, and a place to put the next one.
    ///
    /// The language chips used to sit loose in the corner of the HUD, over the village. That put a
    /// permanent control on top of the world for a choice a player makes once, and it read as part
    /// of the game rather than as a setting. Behind a button they are somewhere a player looks on
    /// purpose, and the HUD goes back to being about the day.
    ///
    /// The character creation screen keeps its chips in the open. That screen is the one place a
    /// player who cannot read the interface is guaranteed to pass through, and asking them to find
    /// a settings button first is asking them to read the thing they cannot read.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public const string ModalId = "settings";

        const float CardWidth = 900f;
        const float CardHeight = 560f;

        bool _closed;

        /// <summary>Opens the modal, or does nothing when it is already up.</summary>
        public static SettingsPanel Show()
        {
            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[SettingsPanel] No modal root is available; settings cannot open.");
                return null;
            }

            if (root.IsIdOpen(ModalId))
            {
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<SettingsPanel>();
            panel.Build(container);
            return panel;
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

        void Build(RectTransform container)
        {
            Image card = UIKit.CreatePanel(container, "Card", UIKit.Palette.Panel);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            cardRect.anchoredPosition = Vector2.zero;

            Text title = UIKit.CreateText(cardRect, "Title", Loc.T("settings.title"),
                UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.MiddleLeft);
            UIKit.AnchorTop((RectTransform)title.transform, 72f, 44f, 44f, 40f);

            Text label = UIKit.CreateText(cardRect, "LanguageLabel", Loc.T("settings.language"),
                UIKit.FontSize.Body, UIKit.Palette.Muted, TextAnchor.MiddleLeft);
            UIKit.AnchorTop((RectTransform)label.transform, 52f, 44f, 44f, 150f);

            RectTransform languageRow = LanguageToggle.Create(cardRect);
            languageRow.anchorMin = new Vector2(0f, 1f);
            languageRow.anchorMax = new Vector2(0f, 1f);
            languageRow.pivot = new Vector2(0f, 1f);
            languageRow.sizeDelta = new Vector2(LanguageToggle.Width, LanguageToggle.Height);
            languageRow.anchoredPosition = new Vector2(44f, -214f);

            Button close = UIKit.CreateButton(cardRect, "Close", Loc.T("settings.close"),
                UIKit.Palette.Clay, UIKit.Palette.Parchment, Close);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(0.5f, 0f);
            closeRect.anchorMax = new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0.5f, 0f);
            closeRect.sizeDelta = new Vector2(320f, 104f);
            closeRect.anchoredPosition = new Vector2(0f, 48f);
        }
    }
}
