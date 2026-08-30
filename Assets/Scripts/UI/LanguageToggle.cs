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
    /// There is no settings screen in this build, so the toggle is placed where a player who needs
    /// it will actually be: on the character creation screen, which is the game's only setup beat,
    /// and in the HUD for the rest of the run. It is deliberately absent during the cutscene, where
    /// the HUD is hidden and nothing is meant to compete with the opening.
    ///
    /// Switching reloads the scene. See BootSequence.SwitchLocale for why.
    /// </summary>
    public static class LanguageToggle
    {
        /// <summary>
        /// Width of one chip. Two of them plus the spacing is the control's width.
        ///
        /// UIKit.CreateButton insets its label by 18px on each side, so the usable width is this
        /// minus 36. At 76 that left 40px for a two-letter code at button size and both chips
        /// rendered their label stacked vertically.
        /// </summary>
        public const float ChipWidth = 116f;

        public const float Height = 64f;

        const float Spacing = 8f;

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
        /// </summary>
        public static RectTransform Create(RectTransform parent, string name = "LanguageToggle")
        {
            RectTransform row = UIKit.CreateRect(name, parent);

            string active = Locales.Active;
            for (int i = 0; i < Locales.Supported.Length; i++)
            {
                string locale = Locales.Supported[i];
                bool isActive = locale == active;

                // Both labels are parchment. The dim label the inactive chip used to carry was
                // Muted on PanelSoft, which is about 4:1 against its own background — under the
                // readable threshold for text this small, and unreadable is the one thing a
                // language control cannot afford. The lit chip is told apart by its clay fill.
                Button chip = UIKit.CreateButton(
                    row,
                    "Locale_" + locale,
                    Locales.ShortLabel(locale),
                    isActive ? UIKit.Palette.Clay : UIKit.Palette.PanelSoft,
                    UIKit.Palette.Parchment,
                    () => OnChipClicked(locale));

                // The active chip is not a no-op waiting to happen: it is not clickable at all,
                // so a player cannot ask for a scene reload that would change nothing.
                chip.interactable = !isActive;

                if (isActive)
                {
                    // Non-interactable is how the chip refuses the tap, not a statement that it is
                    // unavailable, so it must not inherit the 35% wash Button paints on a disabled
                    // graphic — that wash is what made the current language the faintest thing in
                    // the row, which is precisely backwards.
                    ColorBlock colours = chip.colors;
                    colours.disabledColor = Color.white;
                    chip.colors = colours;
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

        static void OnChipClicked(string locale)
        {
            BootSequence.SwitchLocale(locale);
        }
    }
}
