using System;
using SheepGate.Core;
using SheepGate.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The first screen a new run shows. One body choice, three cosmetic slots, a live preview of
    /// the result in all four facings, and one button out.
    ///
    /// What is absent is the point: there is no account, no e-mail, no password, no age question
    /// and no question about religion. The player picks a look and starts. Anything else asked
    /// here would be a toll gate in front of a game that has not earned one yet.
    ///
    /// The player's name is not collected here either — nothing in this screen writes
    /// GameState.playerName.
    /// </summary>
    public static class CharacterCreationScreen
    {
        /// <summary>Scene loaded once the look is confirmed.</summary>
        public const string NextScene = "Game";

        /// <summary>Builds the screen. Called by the scene's bootstrap and nothing else.</summary>
        public static void Compose()
        {
            new Composer().Build();
        }

        /// <summary>
        /// Holds the in-progress selection. A plain object rather than a MonoBehaviour: the screen
        /// has no per-frame work, so button callbacks closing over this instance are enough.
        /// </summary>
        sealed class Composer
        {
            static readonly FacingDirection[] Directions =
            {
                FacingDirection.Down,
                FacingDirection.Left,
                FacingDirection.Right,
                FacingDirection.Up
            };

            static readonly string[] DirectionCaptions = { "frente", "esquerda", "direita", "costas" };

            const int BodyOptions = 2;
            const int CosmeticOptions = 4;

            const float SideMargin = 24f;
            const float ChipWidth = 216f;
            const float ChipSpacing = 24f;
            const float SlotRowHeight = 150f;
            const float SlotRowSpacing = 12f;
            const float SlotRowsTop = 760f;

            int _body;
            int _top;
            int _legs;
            int _accessory;

            readonly Image[] _bodyLayers = new Image[4];
            readonly Image[] _legsLayers = new Image[4];
            readonly Image[] _topLayers = new Image[4];
            readonly Image[] _accessoryLayers = new Image[4];

            Button[] _bodyChips;
            Button[] _topChips;
            Button[] _legsChips;
            Button[] _accessoryChips;

            public void Build()
            {
                LoadCurrentAppearance();

                Canvas canvas = UIKit.CreateCanvas("CharacterCreationCanvas", 0);
                var root = (RectTransform)canvas.transform;

                Image background = UIKit.CreatePanel(root, "Background", UIKit.Palette.Ink);
                UIKit.Stretch((RectTransform)background.transform);

                Text title = UIKit.CreateText(root, "Title", "Antes de subir a primeira pedra",
                    UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.MiddleLeft);
                UIKit.AnchorTop((RectTransform)title.transform, 80f, 48f, 48f, 90f);

                Text subtitle = UIKit.CreateText(root, "Subtitle", "Escolha como você aparece na obra.",
                    UIKit.FontSize.Body, UIKit.Palette.Muted, TextAnchor.MiddleLeft);
                UIKit.AnchorTop((RectTransform)subtitle.transform, 56f, 48f, 48f, 180f);

                BuildPreviewStrip(root);
                BuildSlots(root);

                Button start = UIKit.CreateButton(root, "Start", "Começar", UIKit.Palette.Clay, UIKit.Palette.Parchment, Confirm);
                var startRect = (RectTransform)start.transform;
                startRect.anchorMin = new Vector2(0.5f, 0f);
                startRect.anchorMax = new Vector2(0.5f, 0f);
                startRect.pivot = new Vector2(0.5f, 0f);
                startRect.sizeDelta = new Vector2(640f, 140f);
                startRect.anchoredPosition = new Vector2(0f, 96f);

                RefreshPreview();
                RefreshChips();
            }

            // -------------------------------------------------------------- preview

            void BuildPreviewStrip(RectTransform root)
            {
                Image strip = UIKit.CreatePanel(root, "Preview", UIKit.Palette.Panel);
                var stripRect = (RectTransform)strip.transform;
                UIKit.AnchorTop(stripRect, 460f, SideMargin, SideMargin, 260f);
                strip.raycastTarget = false;

                for (int i = 0; i < Directions.Length; i++)
                {
                    BuildPreviewCell(stripRect, i);
                }
            }

            void BuildPreviewCell(RectTransform stripRect, int index)
            {
                float left = index / (float)Directions.Length;
                float right = (index + 1) / (float)Directions.Length;

                RectTransform cell = UIKit.CreateRect("Cell_" + Directions[index].ToKey(), stripRect);
                cell.anchorMin = new Vector2(left, 0f);
                cell.anchorMax = new Vector2(right, 1f);
                cell.pivot = new Vector2(0.5f, 0.5f);
                cell.offsetMin = new Vector2(10f, 0f);
                cell.offsetMax = new Vector2(-10f, 0f);

                RectTransform figure = UIKit.CreateRect("Figure", cell);
                figure.anchorMin = new Vector2(0.5f, 1f);
                figure.anchorMax = new Vector2(0.5f, 1f);
                figure.pivot = new Vector2(0.5f, 1f);
                figure.sizeDelta = new Vector2(200f, 300f);
                figure.anchoredPosition = new Vector2(0f, -30f);

                // Draw order is the same as the in-world character: body, then legs, then top,
                // then the accessory on top of everything.
                _bodyLayers[index] = BuildLayer(figure, "Body");
                _legsLayers[index] = BuildLayer(figure, "Legs");
                _topLayers[index] = BuildLayer(figure, "Top");
                _accessoryLayers[index] = BuildLayer(figure, "Accessory");

                Text caption = UIKit.CreateText(cell, "Caption", DirectionCaptions[index],
                    UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.MiddleCenter);
                UIKit.AnchorBottom((RectTransform)caption.transform, 44f, 4f, 4f, 22f);
            }

            static Image BuildLayer(RectTransform figure, string name)
            {
                Image image = UIKit.CreatePanel(figure, name, Color.white, null);
                UIKit.Stretch((RectTransform)image.transform);
                image.raycastTarget = false;
                image.preserveAspect = true;
                return image;
            }

            void RefreshPreview()
            {
                for (int i = 0; i < Directions.Length; i++)
                {
                    FacingDirection direction = Directions[i];

                    ApplyLayer(
                        _bodyLayers[i],
                        UIKit.GetSprite(ArtKeys.Body(_body, direction, 0)),
                        Shade(UIKit.Palette.Stone, _body),
                        0f,
                        1f);

                    ApplyLayer(
                        _legsLayers[i],
                        UIKit.GetSprite(ArtKeys.Legs(_legs)),
                        Shade(UIKit.Palette.Night, _legs),
                        0.04f,
                        0.44f);

                    ApplyLayer(
                        _topLayers[i],
                        UIKit.GetSprite(ArtKeys.Top(_top)),
                        Shade(UIKit.Palette.Clay, _top),
                        0.44f,
                        0.76f);

                    ApplyLayer(
                        _accessoryLayers[i],
                        UIKit.GetSprite(ArtKeys.Accessory(_accessory)),
                        Shade(UIKit.Palette.Olive, _accessory),
                        0.78f,
                        0.97f);
                }
            }

            /// <summary>
            /// A present sprite fills the figure, exactly as the in-world character stacks its
            /// layers. A missing one degrades to a coloured band in that layer's part of the
            /// figure, so the options still read as different while the art is being made.
            /// </summary>
            static void ApplyLayer(Image image, Sprite sprite, Color fallback, float fallbackBottom, float fallbackTop)
            {
                if (image == null)
                {
                    return;
                }

                var rect = (RectTransform)image.transform;

                if (sprite != null)
                {
                    image.sprite = sprite;
                    image.color = Color.white;
                    image.preserveAspect = true;
                    SetVerticalBand(rect, 0f, 1f);
                    return;
                }

                image.sprite = null;
                image.color = fallback;
                image.preserveAspect = false;
                SetVerticalBand(rect, fallbackBottom, fallbackTop);
            }

            static void SetVerticalBand(RectTransform rect, float bottom, float top)
            {
                rect.anchorMin = new Vector2(0f, bottom);
                rect.anchorMax = new Vector2(1f, top);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            static Color Shade(Color baseColor, int index)
            {
                return Color.Lerp(baseColor, UIKit.Palette.Parchment, Mathf.Clamp01(index * 0.2f));
            }

            // -------------------------------------------------------------- slots

            void BuildSlots(RectTransform root)
            {
                _bodyChips = BuildSlotRow(root, 0, "Corpo", BodyOptions, SelectBody);
                _topChips = BuildSlotRow(root, 1, "Camisa", CosmeticOptions, SelectTop);
                _legsChips = BuildSlotRow(root, 2, "Calça", CosmeticOptions, SelectLegs);
                _accessoryChips = BuildSlotRow(root, 3, "Detalhe", CosmeticOptions, SelectAccessory);
            }

            Button[] BuildSlotRow(RectTransform root, int rowIndex, string label, int optionCount, Action<int> onSelect)
            {
                float top = SlotRowsTop + rowIndex * (SlotRowHeight + SlotRowSpacing);

                RectTransform row = UIKit.CreateRect("Slot_" + rowIndex, root);
                UIKit.AnchorTop(row, SlotRowHeight, SideMargin + 24f, SideMargin + 24f, top);

                Text caption = UIKit.CreateText(row, "Label", label, UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.MiddleLeft);
                UIKit.AnchorTop((RectTransform)caption.transform, 40f, 0f, 0f, 0f);

                RectTransform chipRow = UIKit.CreateRect("Chips", row);
                UIKit.AnchorBottom(chipRow, 96f, 0f, 0f, 4f);

                var chips = new Button[optionCount];
                for (int i = 0; i < optionCount; i++)
                {
                    int option = i;
                    Button chip = UIKit.CreateButton(chipRow, "Option_" + i, (i + 1).ToString(),
                        UIKit.Palette.PanelSoft, UIKit.Palette.Muted, () => onSelect(option));

                    var chipRect = (RectTransform)chip.transform;
                    chipRect.anchorMin = new Vector2(0f, 0f);
                    chipRect.anchorMax = new Vector2(0f, 1f);
                    chipRect.pivot = new Vector2(0f, 0.5f);
                    chipRect.sizeDelta = new Vector2(ChipWidth, 0f);
                    chipRect.anchoredPosition = new Vector2(i * (ChipWidth + ChipSpacing), 0f);

                    chips[i] = chip;
                }

                return chips;
            }

            void SelectBody(int option)
            {
                _body = Mathf.Clamp(option, 0, BodyOptions - 1);
                RefreshPreview();
                RefreshChips();
            }

            void SelectTop(int option)
            {
                _top = Mathf.Clamp(option, 0, CosmeticOptions - 1);
                RefreshPreview();
                RefreshChips();
            }

            void SelectLegs(int option)
            {
                _legs = Mathf.Clamp(option, 0, CosmeticOptions - 1);
                RefreshPreview();
                RefreshChips();
            }

            void SelectAccessory(int option)
            {
                _accessory = Mathf.Clamp(option, 0, CosmeticOptions - 1);
                RefreshPreview();
                RefreshChips();
            }

            void RefreshChips()
            {
                Highlight(_bodyChips, _body);
                Highlight(_topChips, _top);
                Highlight(_legsChips, _legs);
                Highlight(_accessoryChips, _accessory);
            }

            static void Highlight(Button[] chips, int selected)
            {
                if (chips == null)
                {
                    return;
                }

                for (int i = 0; i < chips.Length; i++)
                {
                    bool active = i == selected;
                    UIKit.TintButton(
                        chips[i],
                        active ? UIKit.Palette.Clay : UIKit.Palette.PanelSoft,
                        active ? UIKit.Palette.Parchment : UIKit.Palette.Muted);
                }
            }

            // -------------------------------------------------------------- state

            void LoadCurrentAppearance()
            {
                GameState state = ResolveState();
                AppearanceState appearance = state.appearance;
                if (appearance == null)
                {
                    return;
                }

                _body = Mathf.Clamp(appearance.body, 0, BodyOptions - 1);
                _top = Mathf.Clamp(appearance.top, 0, CosmeticOptions - 1);
                _legs = Mathf.Clamp(appearance.legs, 0, CosmeticOptions - 1);
                _accessory = Mathf.Clamp(appearance.accessory, 0, CosmeticOptions - 1);
            }

            void Confirm()
            {
                GameState state = ResolveState();

                if (state.appearance == null)
                {
                    state.appearance = new AppearanceState();
                }

                state.appearance.body = _body;
                state.appearance.top = _top;
                state.appearance.legs = _legs;
                state.appearance.accessory = _accessory;

                // Written now so the look survives the app being closed before the first day ends.
                SaveSystem.Save(state);

                SceneManager.LoadScene(NextScene);
            }

            static GameState ResolveState()
            {
                GameState state;
                if (ServiceLocator.TryGet(out state) && state != null)
                {
                    return state;
                }

                // Reachable when this scene is opened directly in the editor instead of through Boot.
                state = GameState.NewGame();
                ServiceLocator.Register(state);
                return state;
            }
        }
    }
}
