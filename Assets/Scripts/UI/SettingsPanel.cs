using System;
using SheepGate.Core;
using SheepGate.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The settings modal: language, and the two sound switches.
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

        /// <summary>
        /// Title, one labelled field, and the way out — stacked, not positioned.
        ///
        /// The card used to be 900x560 with four hand-tuned offsets inside it. Every number in
        /// that arrangement was measured against a type scale two steps below the design system's
        /// floor, so raising the type would have pushed the close button off the bottom of a card
        /// whose height was a constant. A column that measures itself cannot go wrong that way,
        /// and it is what lets the next setting be one more child rather than four more offsets.
        ///
        /// The card is elev.1 over the modal scrim <see cref="ModalRoot"/> already painted, which
        /// is the design system's elev.2: a modal is a card with a dimmed world behind it, never a
        /// third surface colour.
        /// </summary>
        void Build(RectTransform container)
        {
            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Card);
            var cardRect = (RectTransform)card.transform;

            // The screen gutter either side, measured off the container rather than off a width
            // written down here. The canvas scaler matches width and height together, so the
            // number of reference units across a phone is not 1080 and is not the same on two
            // phones — a card 963 units wide is comfortable on one device and edge to edge on the
            // next. Stretching to the gutter is the same intent, expressed so it cannot drift.
            cardRect.anchorMin = new Vector2(0f, 0.5f);
            cardRect.anchorMax = new Vector2(1f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.offsetMin = new Vector2(DesignTokens.Space.Gutter, 0f);
            cardRect.offsetMax = new Vector2(-DesignTokens.Space.Gutter, 0f);

            int pad = Mathf.RoundToInt(DesignTokens.Space.S24);
            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S24, new RectOffset(pad, pad, pad, pad));

            // Height follows the column; width stays with the anchors above. Horizontal fit is
            // unconstrained on purpose — a fitter that sized the card to its longest word would
            // make the modal a different width in every language.
            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.CreateText(cardRect, "Title", Loc.T("settings.title"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Title);

            BuildLanguageField(cardRect);

            BuildToggleField(cardRect, "Music", Loc.T("settings.music"),
                () => AudioDirector.MusicMuted, muted => AudioDirector.MusicMuted = muted);

            BuildToggleField(cardRect, "Effects", Loc.T("settings.effects"),
                () => AudioDirector.EffectsMuted, muted => AudioDirector.EffectsMuted = muted);

            UIKit.CreateButton(cardRect, "Close", Loc.T("settings.close"),
                UIKit.ButtonVariant.Primary, Close);
        }

        /// <summary>
        /// The label and its control, in their own column.
        ///
        /// Nested rather than flat so a label sits nearer the thing it names than the next setting
        /// does: a single spacing value down the card would put exactly as much air between
        /// "Idioma" and the chips as between the chips and the way out, and a form read that way
        /// makes the player work out which label belongs to which control.
        /// </summary>
        static void BuildLanguageField(RectTransform cardRect)
        {
            RectTransform field = UIKit.CreateRect("LanguageField", cardRect);
            UIKit.VerticalGroup(field.gameObject, DesignTokens.Space.S8, new RectOffset());

            UIKit.CreateText(field, "LanguageLabel", Loc.T("settings.language"),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft);

            // The row carries its own LayoutElement, so it arrives in a column already knowing how
            // tall a chip is; see LanguageToggle.Create.
            LanguageToggle.Create(field);
        }

        /// <summary>
        /// One sound channel, in the same field shape as the language row: what it is, then what
        /// it is currently doing.
        ///
        /// Two rows rather than one switch because music and effects are two different complaints.
        /// A player tired of the theme is not asking for a silent village, and one switch makes
        /// them pick between the sound they like and the sound they are done with.
        ///
        /// The button says what the sound IS, never what pressing it would do: a control reading
        /// "Desligar" on a game that is already silent is the same ambiguity that once made the
        /// language chips unreadable. The state is re-read from the setting after the write rather
        /// than assumed from the click, so the label cannot disagree with what is actually stored.
        /// </summary>
        static void BuildToggleField(RectTransform cardRect, string name, string label,
                                     Func<bool> isMuted, Action<bool> setMuted)
        {
            RectTransform field = UIKit.CreateRect(name + "Field", cardRect);
            UIKit.VerticalGroup(field.gameObject, DesignTokens.Space.S8, new RectOffset());

            UIKit.CreateText(field, name + "Label", label,
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft);

            Button toggle = UIKit.CreateButton(field, name + "Toggle", StateLabel(isMuted()),
                UIKit.ButtonVariant.Secondary, null);

            toggle.onClick.AddListener(() =>
            {
                setMuted(!isMuted());

                Text text = toggle.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.text = StateLabel(isMuted());
                }
            });
        }

        static string StateLabel(bool muted)
        {
            return muted ? Loc.T("settings.sound.off") : Loc.T("settings.sound.on");
        }
    }
}
