using SheepGate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The one control that changes language: a row of short codes, the active one lit.
    ///
    /// It shows every locale at once rather than a single button labelled with the other language,
    /// because a lone "EN" is ambiguous in exactly the situation the control exists for — a player
    /// who cannot read the screen cannot tell whether it reports the current language or offers
    /// the next one. A lit chip among dim ones says which is which without a word.
    ///
    /// The toggle is placed where a player who needs it will actually be: on the character
    /// creation screen, which is the game's only setup beat, and behind the settings button for
    /// the rest of the run. It is deliberately absent during the cutscene, where the HUD is hidden
    /// and nothing is meant to compete with the opening.
    ///
    /// Switching reloads the scene. See BootSequence.SwitchLocale for why.
    /// </summary>
    public static class LanguageToggle
    {
        /// <summary>
        /// Width of one chip. Two of them plus the spacing is the control's width.
        ///
        /// A chip is a button, so it inherits the design system's floor: 48 design points on both
        /// axes with 8 of clear space beside it. 64 rather than the bare 48 because a button insets
        /// its label by 18 design points either side, and a two-letter code at body-strong weight
        /// needs the difference — at the old 116 reference units the label had 40 left and both
        /// chips rendered "PT" stacked vertically.
        /// </summary>
        public static readonly float ChipWidth = DesignTokens.Px(64f);

        /// <summary>The design system's touch target. A chip is never shorter than a thumb.</summary>
        public static readonly float Height = DesignTokens.Space.TouchTarget;

        /// <summary>Clear space the accessibility rule requires between two touch targets.</summary>
        static readonly float Spacing = DesignTokens.Space.TouchGap;

        /// <summary>The control's total width for the locales this build ships.</summary>
        public static float Width
        {
            get
            {
                int count = Mathf.Max(1, Locales.Supported.Length);
                return count * ChipWidth + (count - 1) * Spacing;
            }
        }

        /// <summary>
        /// Builds the row under <paramref name="parent"/> and returns its RectTransform, positioned
        /// by the caller. Every chip is live except the one for the language already running.
        ///
        /// The row carries a <see cref="LayoutElement"/> as well as a size, so it is correct in a
        /// layout group and outside one. Without it a column would hand the row zero height and
        /// the chips — which stretch to their parent's height — would disappear rather than
        /// misdraw, which is the failure mode nothing in this project logs.
        /// </summary>
        public static RectTransform Create(RectTransform parent, string name = "LanguageToggle")
        {
            RectTransform row = UIKit.CreateRect(name, parent);
            row.sizeDelta = new Vector2(Width, Height);

            LayoutElement rowLayout = UIKit.Layout(row);
            rowLayout.minHeight = Height;
            rowLayout.preferredHeight = Height;
            rowLayout.flexibleHeight = 0f;

            string active = Locales.Active;
            for (int i = 0; i < Locales.Supported.Length; i++)
            {
                string locale = Locales.Supported[i];
                bool isActive = locale == active;

                // Clay against a translucent parchment fill with a hairline border: the design
                // system's primary and secondary, doing exactly what they are for. Both labels
                // stay fully opaque, which is the point — the dim label the inactive chip used to
                // carry was about 4:1 against its own background, under the readable threshold for
                // text this small, and unreadable is the one thing a language control cannot
                // afford. The lit chip is told apart by its fill, never by its ink.
                Button chip = UIKit.CreateButton(
                    row,
                    "Locale_" + locale,
                    Locales.ShortLabel(locale),
                    isActive ? UIKit.ButtonVariant.Primary : UIKit.ButtonVariant.Secondary,
                    () => OnChipClicked(locale));

                // The active chip is not a no-op waiting to happen: it is not clickable at all,
                // so a player cannot ask for a scene reload that would change nothing.
                chip.interactable = !isActive;

                if (isActive)
                {
                    NeutraliseDisabledDimming(chip);
                }

                var chipRect = (RectTransform)chip.transform;
                chipRect.anchorMin = new Vector2(0f, 0f);
                chipRect.anchorMax = new Vector2(0f, 1f);
                chipRect.pivot = new Vector2(0f, 0.5f);
                chipRect.sizeDelta = new Vector2(ChipWidth, 0f);
                chipRect.anchoredPosition = new Vector2(i * (ChipWidth + Spacing), 0f);
            }

            return row;
        }

        /// <summary>
        /// Stops the current language from being drawn as an unavailable control.
        ///
        /// <see cref="VariantButton"/> implements the design system's rule 6 — disabled is opacity
        /// 0.40 — through a <see cref="CanvasGroup"/> it dims whenever the button is not
        /// interactable. That rule is right, and it is about a control the player cannot use yet.
        /// This chip is not unavailable: it is the answer. Dimming it to 40% would make the
        /// language actually running the faintest thing in the row while the language on offer sat
        /// at full strength, which is precisely backwards, and it is the same mistake the base
        /// Button's disabled wash used to make here.
        ///
        /// Removing the group rather than fighting it: VariantButton null-checks before dimming,
        /// so with the component gone the state simply has no visual, and nothing re-applies an
        /// alpha on the next pointer event. Destruction lands before the first frame is drawn.
        /// </summary>
        static void NeutraliseDisabledDimming(Button chip)
        {
            var dimmer = chip.GetComponent<CanvasGroup>();
            if (dimmer != null)
            {
                UnityEngine.Object.Destroy(dimmer);
            }
        }

        static void OnChipClicked(string locale)
        {
            BootSequence.SwitchLocale(locale);
        }
    }
}
