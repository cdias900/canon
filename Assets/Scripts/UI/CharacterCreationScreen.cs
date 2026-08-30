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
        /// Runs the same screen inside a scene that is already playing, handing control back through
        /// <paramref name="onDone"/> instead of loading the game scene. The opening cutscene uses
        /// this: the player is walked into a house, dresses, and walks back out, so the wardrobe has
        /// to be a beat in the story rather than a screen in front of it.
        /// </summary>
        public static void Compose(Action onDone)
        {
            Compose(onDone, StandaloneSortingOrder);
        }

        /// <summary>Standalone screen: nothing else is on screen, so any order will do.</summary>
        public const int StandaloneSortingOrder = 0;

        /// <summary>
        /// Above the opening's blackout. The cutscene fades to black on its own canvas before the
        /// wardrobe appears, so a screen drawn underneath that would be invisible.
        /// </summary>
        public const int CutsceneSortingOrder = 500;

        public static void Compose(Action onDone, int sortingOrder)
        {
            new Composer(onDone, sortingOrder).Build();
        }

        /// <summary>
        /// Holds the in-progress selection. A plain object rather than a MonoBehaviour: the screen
        /// has no per-frame work, so button callbacks closing over this instance are enough.
        /// </summary>
        sealed class Composer
        {
            /// <summary>Set when the screen runs inside a live scene; null means load the game scene.</summary>
            readonly Action _onDone;

            Canvas _canvas;

            /// <summary>
            /// Removes the screen. Only used in the in-scene path: the scene-loading path is torn
            /// down by the scene change itself.
            /// </summary>
            void Teardown()
            {
                if (_canvas != null)
                {
                    UnityEngine.Object.Destroy(_canvas.gameObject);
                    _canvas = null;
                }
            }

            readonly int _sortingOrder;

            public Composer() : this(null, StandaloneSortingOrder)
            {
            }

            public Composer(Action onDone, int sortingOrder)
            {
                _onDone = onDone;
                _sortingOrder = sortingOrder;
            }

            static readonly FacingDirection[] Directions =
            {
                FacingDirection.Down,
                FacingDirection.Left,
                FacingDirection.Right,
                FacingDirection.Up
            };

            // Keys, not words. An array of literals is the one shape that slips past a check on
            // call arguments, which is exactly how these four stayed Portuguese in the English
            // build until a screenshot showed them.
            static readonly string[] DirectionCaptionKeys =
            {
                "creation.facing.front",
                "creation.facing.left",
                "creation.facing.right",
                "creation.facing.back"
            };

            const int BodyOptions = 2;
            const int CosmeticOptions = 4;
            const int SkinOptions = 4;
            const int HairOptions = 4;

            /// <summary>How many slot rows <see cref="BuildSlots"/> lays out. Drives the scroll height.</summary>
            const int SlotCount = 6;

            const int NameCharacterLimit = 16;

            // ---------------------------------------------------------------- geometry
            //
            // Every length below is written in design points and converted by DesignTokens.Px, so
            // it can be read straight against the design document. All of it was re-measured when
            // the screen moved onto DesignTokens.Type, and none of the old numbers survived: the
            // captions used to be drawn at 26 reference units (9.4 design points, under the
            // system's floor of 12) and the chips were 74 units tall against a 48 point touch
            // target, which is 133. Type here is between 1.2x and 1.7x what it was, and a touch
            // target is 1.8x, so nothing that was tuned by eye at the old scale is still right.
            //
            // The one number that is NOT a constant is the height of the header. The title is
            // Display, the largest step in the system, and whether it takes two lines or three
            // depends on the language and on how wide the canvas comes out — 1080 units in the
            // desktop e2e player, about 977 on the phone, because the scaler matches width and
            // height equally. Guessing that wrap is how a title ends up sitting on a subtitle, so
            // the header is measured after it is built and everything below it is placed against
            // the measurement.

            /// <summary>Screen gutter, left and right, for every element on this screen.</summary>
            static readonly float SideMargin = DesignTokens.Space.Gutter;

            /// <summary>Clear space above the header, inside the safe area.</summary>
            static readonly float HeaderTop = DesignTokens.Space.S12;

            /// <summary>
            /// The eyebrow shares its line with the language toggle, so the row is as tall as
            /// whichever of the two is taller.
            /// </summary>
            static readonly float EyebrowRowHeight = Mathf.Max(DesignTokens.Px(22f), LanguageToggle.Height);

            /// <summary>
            /// The header this screen falls back to when the measurement cannot be believed: the
            /// eyebrow row, three Display lines and two body lines, which is the tallest wrap
            /// either language plausibly produces. Stacked out of the tokens the header actually
            /// uses — 34 point Display lines and the 22 point body leading — rather than written
            /// as a round number, so it stays right if the type scale moves.
            /// </summary>
            static readonly float DefaultHeaderHeight =
                EyebrowRowHeight + 2f * DesignTokens.Space.S8 +
                3f * DesignTokens.Type.Display + 2f * DesignTokens.Px(22f);

            /// <summary>
            /// The range a real measurement can fall in: two Display lines and no subtitle at the
            /// bottom, six Display lines at the top. Outside it the layout pass measured something
            /// that is not this header — a canvas with no size yet gives a wrapped title one word
            /// per line — and <see cref="DefaultHeaderHeight"/> is used instead. Clamping to the
            /// bound would be worse: it would keep the wrong number and only bound how wrong.
            /// </summary>
            static readonly float MinHeaderHeight =
                EyebrowRowHeight + 2f * DesignTokens.Space.S8 + 2f * DesignTokens.Type.Display;

            static readonly float MaxHeaderHeight =
                EyebrowRowHeight + 2f * DesignTokens.Space.S8 + 6f * DesignTokens.Type.Display;

            /// <summary>Bottom row: the clay action, the quiet one above it, and the home indicator.</summary>
            static readonly float StartHeight = DesignTokens.Px(56f);
            static readonly float SkipHeight = UIKit.ButtonMinHeight;
            static readonly float ButtonGap = DesignTokens.Space.S12;
            static readonly float StartBottom = DesignTokens.Space.SafeAreaBottom;
            static readonly float SkipBottom = StartBottom + StartHeight + ButtonGap;

            /// <summary>Clearance kept above the two buttons at the bottom.</summary>
            static readonly float OptionsBottom = SkipBottom + SkipHeight + DesignTokens.Space.S16;

            /// <summary>Preview strip: a panel of four cells, one per facing.</summary>
            static readonly float PreviewTop = DesignTokens.Space.S8;

            /// <summary>
            /// Tall enough that the figure is limited by the width of its cell and not by this
            /// number, on the narrower of the two canvases the game runs on. Below about 153
            /// design points the strip is what crops the figure, and the four facings come out
            /// smaller than the row they sit in could draw them.
            /// </summary>
            static readonly float PreviewHeight = DesignTokens.Px(160f);
            static readonly float PreviewPadding = DesignTokens.Space.S12;
            static readonly float CaptionHeight = DesignTokens.Px(20f);
            static readonly float CaptionBottom = DesignTokens.Space.S8;

            /// <summary>What the caption reserves at the bottom of a cell: its own band plus air.</summary>
            static readonly float CaptionBandHeight =
                CaptionBottom + CaptionHeight + DesignTokens.Space.S8;

            /// <summary>
            /// The character sprites are 32x48, and the preview figure is fitted to that ratio
            /// rather than given a size. A fixed 200x300 fitted the 1080-unit reference and
            /// overflowed its cell on the phone, where the canvas is about a hundred units
            /// narrower; fitting means the figure is as large as the cell allows on both.
            /// </summary>
            static readonly float FigureAspect =
                SheepGate.Art.CharacterArt.Width / (float)SheepGate.Art.CharacterArt.Height;

            static readonly float NameTop = PreviewTop + PreviewHeight + DesignTokens.Space.S24;
            static readonly float NameLabelHeight = DesignTokens.Px(20f);

            /// <summary>The field is a touch target, so it is at least 48 design points tall.</summary>
            static readonly float NameFieldHeight = DesignTokens.Px(52f);
            static readonly float NameRowHeight =
                NameLabelHeight + DesignTokens.Space.S8 + NameFieldHeight;

            /// <summary>Horizontal padding inside the name field, at the new body size.</summary>
            static readonly float FieldPadding = DesignTokens.Space.S16;

            static readonly float SlotLabelHeight = DesignTokens.Px(20f);
            static readonly float SlotChipHeight = UIKit.ButtonMinHeight;
            static readonly float SlotRowHeight =
                SlotLabelHeight + DesignTokens.Space.S8 + SlotChipHeight;
            static readonly float SlotRowSpacing = DesignTokens.Space.S12;
            static readonly float SlotRowsTop = NameTop + NameRowHeight + DesignTokens.Space.S24;

            /// <summary>
            /// Height of the scrolling content: the last row's bottom edge plus a little air, so a
            /// drag to the end does not leave the final option flush against the frame.
            /// </summary>
            static readonly float OptionsContentHeight =
                SlotRowsTop + SlotCount * SlotRowHeight + (SlotCount - 1) * SlotRowSpacing +
                DesignTokens.Space.S32;

            /// <summary>
            /// Columns in the chip grid: one per option of the widest slot. A slot with fewer
            /// options leaves its right-hand cells empty instead of stretching two chips across
            /// the screen, so every row's first chip starts at the same x and the rows read as a
            /// grid rather than as six unrelated controls.
            /// </summary>
            const int ChipColumns = 4;

            /// <summary>Clear space between two chips. The design system's floor between targets.</summary>
            static readonly float ChipGap = DesignTokens.Space.TouchGap;

            /// <summary>Inset of the selection check from the chip's top-right corner.</summary>
            static readonly float SelectionMarkInset = DesignTokens.Space.S4;

            /// <summary>Width of the always-visible scrollbar. The content is over twice the viewport.</summary>
            static readonly float ScrollbarWidth = DesignTokens.Space.S8;

            int _body;
            int _top;
            int _legs;
            int _accessory;

            readonly Image[] _bodyLayers = new Image[4];
            readonly Image[] _legsLayers = new Image[4];
            readonly Image[] _topLayers = new Image[4];
            readonly Image[] _accessoryLayers = new Image[4];
            readonly Image[] _hairLayers = new Image[4];

            OptionChip[] _bodyChips;
            OptionChip[] _skinChips;
            OptionChip[] _hairChips;
            int _skin;
            int _hair;
            string _name = string.Empty;
            OptionChip[] _topChips;
            OptionChip[] _legsChips;
            OptionChip[] _accessoryChips;

            /// <summary>
            /// One selectable option, and the two marks that say it is the chosen one.
            ///
            /// The chip cannot be recoloured to show selection the way it used to be: a variant
            /// button owns its colours across seven states and repaints them on the next pointer
            /// event, so a tint applied from here would survive until the finger moved. Selection
            /// is therefore drawn on top — a 2px gold border, which is what the design system
            /// specifies — and carries a check as well, because a state that is only a colour is
            /// a state some players cannot see.
            /// </summary>
            sealed class OptionChip
            {
                /// <summary>The control itself. The handle anything that wants to disable a chip needs.</summary>
                public Button Control;

                /// <summary>The 2px Brand.Secondary border that says this option is the chosen one.</summary>
                public Image Ring;

                /// <summary>The check beside it, so the state is not carried by a colour alone.</summary>
                public Image Mark;
            }

            public void Build()
            {
                LoadCurrentAppearance();

                Canvas canvas = UIKit.CreateCanvas("CharacterCreationCanvas", _sortingOrder);
                _canvas = canvas;
                RectTransform root = UIKit.SafeArea(canvas);

                Image background = UIKit.CreatePanel(root, "Background", DesignTokens.Surface.Background);
                UIKit.Stretch((RectTransform)background.transform);

                // Bled past the safe area on purpose. The screen background is the bottom of the
                // stack, and stopping it at the inset leaves a strip of whatever is behind the
                // canvas along the camera housing, which reads as a rendering fault.
                UIKit.Bleed(background);

                float headerHeight = BuildHeader(root);
                float optionsTop = HeaderTop + headerHeight + DesignTokens.Space.S16;

                // Everything between the header and the buttons scrolls. It used to be absolutely
                // positioned against the canvas, which fitted only as long as the canvas was the
                // whole screen: once the layout honours the safe area the phone is ~200 units
                // shorter, and the last option row ended up underneath the buttons. Scrolling is
                // the fix that does not need retuning on the next device — and it needs it less
                // now, because the design system's type and touch targets made this content over
                // twice the height of its viewport.
                ScrollRect options = UIKit.CreateScrollView(root, "Options", out RectTransform optionsContent);
                var optionsRect = (RectTransform)options.transform;
                optionsRect.anchorMin = new Vector2(0f, 0f);
                optionsRect.anchorMax = new Vector2(1f, 1f);
                optionsRect.offsetMin = new Vector2(0f, OptionsBottom);
                optionsRect.offsetMax = new Vector2(0f, -optionsTop);

                // The helper lays its content out with a vertical group; this screen positions by
                // hand, so the group and its fitter are switched off rather than fought.
                VerticalLayoutGroup group = optionsContent.GetComponent<VerticalLayoutGroup>();
                if (group != null)
                {
                    group.enabled = false;
                }

                ContentSizeFitter contentFitter = optionsContent.GetComponent<ContentSizeFitter>();
                if (contentFitter != null)
                {
                    contentFitter.enabled = false;
                }

                optionsContent.sizeDelta = new Vector2(0f, OptionsContentHeight);

                // Permanent, not auto-hiding. There is a screen and a half below the fold now and
                // nothing else on the screen says so; the bar sits in the gutter the content
                // already leaves free, so it costs no width.
                UIKit.AttachVerticalScrollbar(options, ScrollbarWidth);

                BuildPreviewStrip(optionsContent);
                BuildNameField(optionsContent);
                BuildSlots(optionsContent);

                Button skip = UIKit.CreateButton(root, "QuickStart", Loc.T("creation.quick_start"),
                    UIKit.ButtonVariant.Secondary, QuickStart);
                UIKit.AnchorBottom((RectTransform)skip.transform, SkipHeight, SideMargin, SideMargin, SkipBottom);

                // Clay, not gold. The design system allows one gold call to action per screen and
                // this screen spends its gold on the selected chip, which is the thing the player
                // is actually looking for while they are here.
                Button start = UIKit.CreateButton(root, "Start", Loc.T("creation.start"),
                    UIKit.ButtonVariant.Primary, Confirm);
                UIKit.AnchorBottom((RectTransform)start.transform, StartHeight, SideMargin, SideMargin, StartBottom);

                RefreshPreview();
                RefreshChips();
            }

            // -------------------------------------------------------------- header

            /// <summary>
            /// Builds the eyebrow, title and subtitle as a measured column, and returns how tall
            /// they came out.
            ///
            /// The height is measured rather than declared because the title is Display — 34
            /// design points — and the number of lines it takes is decided by the language and by
            /// the canvas width, neither of which this file can know. A constant tuned against the
            /// Portuguese title in the desktop player is a constant that clips the English one on
            /// a phone, and a screen builder cannot see that happen.
            /// </summary>
            float BuildHeader(RectTransform root)
            {
                RectTransform header = UIKit.CreateRect("Header", root);
                header.anchorMin = new Vector2(0f, 1f);
                header.anchorMax = new Vector2(1f, 1f);
                header.pivot = new Vector2(0.5f, 1f);
                header.sizeDelta = new Vector2(-2f * SideMargin, 0f);
                header.anchoredPosition = new Vector2(0f, -HeaderTop);

                UIKit.VerticalGroup(header.gameObject, DesignTokens.Space.S8, new RectOffset());
                ContentSizeFitter fitter = header.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                RectTransform eyebrowRow = UIKit.CreateRect("EyebrowRow", header);
                LayoutElement eyebrowLayout = UIKit.Layout(eyebrowRow);
                eyebrowLayout.minHeight = EyebrowRowHeight;
                eyebrowLayout.preferredHeight = EyebrowRowHeight;

                // This is the only setup moment the game has, and the first screen a player can
                // read at their own pace. If the system language guessed wrong, this is where they
                // find out and where they can say so.
                RectTransform languageRow = LanguageToggle.Create(eyebrowRow);
                languageRow.anchorMin = new Vector2(1f, 0.5f);
                languageRow.anchorMax = new Vector2(1f, 0.5f);
                languageRow.pivot = new Vector2(1f, 0.5f);
                languageRow.sizeDelta = new Vector2(LanguageToggle.Width, LanguageToggle.Height);
                languageRow.anchoredPosition = Vector2.zero;

                // The eyebrow stops short of the toggle rather than guessing at a margin: it is
                // left aligned and the toggle is right aligned on the same line, and an eyebrow
                // with two-letter buttons sitting on top of it is the first thing a player sees.
                Text eyebrow = UIKit.CreateText(eyebrowRow, "Eyebrow", Loc.T("creation.eyebrow"),
                    DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.MiddleLeft,
                    DesignTokens.TypeRole.Mono);
                UIKit.Stretch((RectTransform)eyebrow.transform, 0f,
                    LanguageToggle.Width + DesignTokens.Space.S12, 0f, 0f);

                // Upper aligned, both of them. Inside a vertical group the block grows downward as
                // it wraps, and a middle-aligned title that gains a line would climb into the row
                // above it as well as pushing the one below.
                UIKit.CreateText(header, "Title", Loc.T("creation.title"),
                    DesignTokens.Type.Display, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                    DesignTokens.TypeRole.Display);

                UIKit.CreateText(header, "Subtitle", Loc.T("creation.subtitle"),
                    DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);

                UIKit.RebuildNow(header);

                // Checked, not trusted, and loudly. A layout that ran before the canvas had a size
                // does not report a wrong-looking number, it reports a plausible one, and a header
                // measured at the wrong width either puts the scroll view over the title or leaves
                // a third of the screen empty — neither of which logs anything on its own. This is
                // the failure mode this project has already shipped twice.
                float measured = header.rect.height;
                if (measured >= MinHeaderHeight && measured <= MaxHeaderHeight)
                {
                    return measured;
                }

                Debug.LogWarning("[CharacterCreation] The header measured " + measured +
                                 " units, outside the plausible range " + MinHeaderHeight + " to " +
                                 MaxHeaderHeight + ". The layout pass probably ran before the canvas " +
                                 "had a size. Falling back to " + DefaultHeaderHeight + ".");
                return DefaultHeaderHeight;
            }

            // -------------------------------------------------------------- preview

            void BuildPreviewStrip(RectTransform root)
            {
                Image strip = UIKit.CreateCard(root, "Preview", UIKit.CardStyle.Panel);
                var stripRect = (RectTransform)strip.transform;
                UIKit.AnchorTop(stripRect, PreviewHeight, SideMargin, SideMargin, PreviewTop);
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
                cell.offsetMin = new Vector2(DesignTokens.Space.S4, 0f);
                cell.offsetMax = new Vector2(-DesignTokens.Space.S4, 0f);

                // What is left of the cell once the caption has its band. The figure is fitted
                // inside this rather than sized, so the same code gives the largest figure the
                // cell allows at 1080 units across and at the ~977 a phone actually reports.
                RectTransform figureArea = UIKit.CreateRect("FigureArea", cell);
                UIKit.Stretch(figureArea, 0f, 0f, PreviewPadding, CaptionBandHeight);

                RectTransform figure = UIKit.CreateRect("Figure", figureArea);
                UIKit.Stretch(figure);

                var aspect = figure.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                aspect.aspectRatio = FigureAspect;

                // Draw order is the same as the in-world character: body, then legs, then top,
                // then the accessory on top of everything.
                _bodyLayers[index] = BuildLayer(figure, "Body");
                _legsLayers[index] = BuildLayer(figure, "Legs");
                _topLayers[index] = BuildLayer(figure, "Top");
                _accessoryLayers[index] = BuildLayer(figure, "Accessory");
                _hairLayers[index] = BuildLayer(figure, "Hair");

                Text caption = UIKit.CreateText(cell, "Caption", Loc.T(DirectionCaptionKeys[index]),
                    DesignTokens.Type.Minimum, DesignTokens.Ink.Muted, TextAnchor.MiddleCenter);
                UIKit.AnchorBottom((RectTransform)caption.transform, CaptionHeight, 0f, 0f, CaptionBottom);
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
                        UIKit.GetSprite(UiSpriteKeys.Body(BodyArtVariant, direction, 0)),
                        Shade(DesignTokens.Neutral.N500, _body),
                        0f,
                        1f);

                    ApplyLayer(
                        _legsLayers[i],
                        UIKit.GetSprite(UiSpriteKeys.Legs(_legs)),
                        Shade(DesignTokens.Ambient.Sky, _legs),
                        0.04f,
                        0.44f);

                    ApplyLayer(
                        _topLayers[i],
                        UIKit.GetSprite(UiSpriteKeys.Top(_top)),
                        Shade(DesignTokens.Brand.Primary, _top),
                        0.44f,
                        0.76f);

                    // Hair last so it sits over the head, and across the full figure because the
                    // sprite is head-height already.
                    ApplyLayer(
                        _hairLayers[i],
                        UIKit.GetSprite(SheepGate.Art.ArtKeys.Hair(_hair, UiSpriteKeys.ToArtFacing(direction))),
                        Shade(DesignTokens.Ambient.Sky, _hair),
                        0.76f,
                        1f);

                    ApplyLayer(
                        _accessoryLayers[i],
                        UIKit.GetSprite(UiSpriteKeys.Accessory(_accessory)),
                        Shade(DesignTokens.Ambient.Growth, _accessory),
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
                return Color.Lerp(baseColor, DesignTokens.Ink.Primary, Mathf.Clamp01(index * 0.2f));
            }

            // -------------------------------------------------------------- slots

            void BuildSlots(RectTransform root)
            {
                _bodyChips = BuildSlotRow(root, 0, Loc.T("creation.slot.body"), BodyOptions, SelectBody);
                _skinChips = BuildSlotRow(root, 1, Loc.T("creation.slot.skin"), SkinOptions, SelectSkin);
                _hairChips = BuildSlotRow(root, 2, Loc.T("creation.slot.hair"), HairOptions, SelectHair);
                _topChips = BuildSlotRow(root, 3, Loc.T("creation.slot.top"), CosmeticOptions, SelectTop);
                _legsChips = BuildSlotRow(root, 4, Loc.T("creation.slot.legs"), CosmeticOptions, SelectLegs);
                _accessoryChips = BuildSlotRow(root, 5, Loc.T("creation.slot.accessory"), CosmeticOptions, SelectAccessory);
            }

            OptionChip[] BuildSlotRow(RectTransform root, int rowIndex, string label, int optionCount, Action<int> onSelect)
            {
                float top = SlotRowsTop + rowIndex * (SlotRowHeight + SlotRowSpacing);

                RectTransform row = UIKit.CreateRect("Slot_" + rowIndex, root);
                UIKit.AnchorTop(row, SlotRowHeight, SideMargin, SideMargin, top);

                Text caption = UIKit.CreateText(row, "Label", label, DesignTokens.Type.Minimum,
                    DesignTokens.Ink.Muted, TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);
                UIKit.AnchorTop((RectTransform)caption.transform, SlotLabelHeight, 0f, 0f, 0f);

                RectTransform chipRow = UIKit.CreateRect("Chips", row);

                // The chips take the whole bottom of the row and are exactly a touch target tall.
                // The label used to sit in 40 units of a 118 unit row with 74 left for the chips;
                // at the design system's floor of 48 design points a chip is 133 on its own, which
                // is why the row grew rather than the chips shrinking to fit the old one.
                UIKit.AnchorBottom(chipRow, SlotChipHeight, 0f, 0f, 0f);

                // Wider than the grid would mean chips drawn on top of each other, so a slot that
                // outgrew the grid widens it instead. Every slot in this build fits ChipColumns.
                int columns = Mathf.Max(ChipColumns, optionCount);

                var chips = new OptionChip[optionCount];
                for (int i = 0; i < optionCount; i++)
                {
                    chips[i] = BuildChip(chipRow, i, columns, onSelect);
                }

                return chips;
            }

            /// <summary>
            /// One chip, placed in a proportional grid cell rather than at a pixel offset.
            ///
            /// The old row placed four 216-unit chips at fixed offsets, which fits the 1080-unit
            /// reference and does not fit a phone: the scaler matches width and height equally, so
            /// a tall handset reports a canvas about 977 units across and the fourth chip ran off
            /// the screen. Anchoring to a fraction of the row makes the arithmetic the layout's
            /// problem and holds at any width.
            /// </summary>
            OptionChip BuildChip(RectTransform chipRow, int index, int columns, Action<int> onSelect)
            {
                Button button = UIKit.CreateButton(chipRow, "Option_" + index, (index + 1).ToString(),
                    UIKit.ButtonVariant.Secondary, () => onSelect(index));

                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(index / (float)columns, 0f);
                rect.anchorMax = new Vector2((index + 1) / (float)columns, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                // Half the gap on each side of a shared edge, none against the row's own edges.
                rect.offsetMin = new Vector2(index == 0 ? 0f : ChipGap * 0.5f, 0f);
                rect.offsetMax = new Vector2(index == columns - 1 ? 0f : -ChipGap * 0.5f, 0f);

                // The selected state, drawn over the chip. The ring is the design system's 2px
                // Brand.Secondary border, stretched to the chip's own rect so it reads as a border
                // and not as the focus ring, which sits two points further out and belongs to
                // keyboard focus. Both use the same sprite; only their rects differ.
                Image ring = UIKit.CreatePanel(rect, "Selected", DesignTokens.Brand.Secondary,
                    UiSpriteKeys.FocusRing);
                UIKit.Stretch((RectTransform)ring.transform);
                ring.raycastTarget = false;
                ring.gameObject.SetActive(false);

                // And a check, because gold on its own is a state carried by colour alone. It sits
                // in the corner rather than beside the digit: the label is centred and one
                // character wide, so the corner is the one place nothing else wants.
                Image mark = UIKit.CreateIcon(rect, "SelectedMark", UiSpriteKeys.IconCheck,
                    DesignTokens.Brand.Secondary, UIKit.IconSize);
                var markRect = (RectTransform)mark.transform;
                markRect.anchorMin = new Vector2(1f, 1f);
                markRect.anchorMax = new Vector2(1f, 1f);
                markRect.pivot = new Vector2(1f, 1f);
                markRect.anchoredPosition = new Vector2(-SelectionMarkInset, -SelectionMarkInset);
                mark.gameObject.SetActive(false);

                return new OptionChip { Control = button, Ring = ring, Mark = mark };
            }

            /// <summary>Build and skin share a sprite, so the preview asks for the packed variant.</summary>
            int BodyArtVariant
            {
                get { return Mathf.Clamp(_body, 0, BodyOptions - 1) * AppearanceState.SkinTones + Mathf.Clamp(_skin, 0, SkinOptions - 1); }
            }

            void SelectSkin(int option)
            {
                _skin = Mathf.Clamp(option, 0, SkinOptions - 1);
                RefreshChips();
                RefreshPreview();
            }

            void SelectHair(int option)
            {
                _hair = Mathf.Clamp(option, 0, HairOptions - 1);
                RefreshChips();
                RefreshPreview();
            }

            /// <summary>
            /// For players who do not want to spend time here. ESCOPO asks for a pre-selection, and
            /// the honest version of that is a button that dresses you and moves on - not a default
            /// that quietly decides for someone who did want to choose.
            /// </summary>
            void QuickStart()
            {
                _body = 0;
                _skin = 1;
                _hair = 0;
                _top = 1;
                _legs = 2;
                _accessory = 0;
                RefreshChips();
                RefreshPreview();
                Confirm();
            }

            void BuildNameField(RectTransform root)
            {
                RectTransform row = UIKit.CreateRect("NameRow", root);
                UIKit.AnchorTop(row, NameRowHeight, SideMargin, SideMargin, NameTop);

                Text caption = UIKit.CreateText(row, "Label", Loc.T("creation.name_label"),
                    DesignTokens.Type.Minimum, DesignTokens.Ink.Muted, TextAnchor.MiddleLeft,
                    DesignTokens.TypeRole.BodyStrong);
                UIKit.AnchorTop((RectTransform)caption.transform, NameLabelHeight, 0f, 0f, 0f);

                InputField field = UIKit.CreateInputField(row, "NameInput", Loc.T("creation.name_placeholder"),
                    NameCharacterLimit, value => _name = value);
                UIKit.AnchorBottom((RectTransform)field.transform, NameFieldHeight, 0f, 0f, 0f);

                // The helper builds the field on the old panel sprite and the old body size,
                // because it is shared with screens that have not moved yet. Restyling it here is
                // what a call site can do without changing the kit under those screens.
                var fieldBackground = field.GetComponent<Image>();
                if (fieldBackground != null)
                {
                    fieldBackground.sprite = UIKit.GetSprite(UiSpriteKeys.FrameMd);
                    fieldBackground.type = UIKit.TypeFor(fieldBackground.sprite);
                    fieldBackground.color = DesignTokens.Surface.Card;
                }

                StyleFieldText(field.textComponent, DesignTokens.Ink.Primary);
                StyleFieldText(field.placeholder as Text, DesignTokens.Ink.Muted);

                field.text = _name;
            }

            /// <summary>
            /// Puts the design system's body type on one of the field's two Text children, and
            /// re-insets it: the helper's 20 units of padding were measured against a smaller
            /// face, and at the new size a name starts almost against the corner radius.
            /// </summary>
            static void StyleFieldText(Text text, Color color)
            {
                if (text == null)
                {
                    return;
                }

                Font font = DesignTokens.Font(DesignTokens.TypeRole.Body);
                text.font = font;
                text.fontSize = DesignTokens.Type.Body;
                text.lineSpacing = UIKit.LineSpacingFor(font, UIKit.LeadingFor(DesignTokens.TypeRole.Body));
                text.color = color;
                UIKit.Stretch((RectTransform)text.transform, FieldPadding, FieldPadding, 0f, 0f);
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
                Highlight(_skinChips, _skin);
                Highlight(_hairChips, _hair);
                Highlight(_topChips, _top);
                Highlight(_legsChips, _legs);
                Highlight(_accessoryChips, _accessory);
            }

            static void Highlight(OptionChip[] chips, int selected)
            {
                if (chips == null)
                {
                    return;
                }

                for (int i = 0; i < chips.Length; i++)
                {
                    OptionChip chip = chips[i];
                    if (chip == null)
                    {
                        continue;
                    }

                    bool active = i == selected;

                    if (chip.Ring != null)
                    {
                        chip.Ring.gameObject.SetActive(active);
                    }

                    if (chip.Mark != null)
                    {
                        chip.Mark.gameObject.SetActive(active);
                    }
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
                _skin = Mathf.Clamp(appearance.skin, 0, SkinOptions - 1);
                _hair = Mathf.Clamp(appearance.hair, 0, HairOptions - 1);

                GameState current;
                _name = ServiceLocator.TryGet(out current) && current != null && current.playerName != null
                    ? current.playerName
                    : string.Empty;
            }

            void Confirm()
            {
                GameState state = ResolveState();

                if (state.appearance == null)
                {
                    state.appearance = new AppearanceState();
                }

                state.appearance.body = _body;
                state.appearance.skin = _skin;
                state.appearance.hair = _hair;
                state.appearance.top = _top;
                state.appearance.legs = _legs;
                state.appearance.accessory = _accessory;

                // Trimmed, and blank is allowed: the wall records whoever turned up, and refusing
                // to start until someone types a name would gate the game on a form field.
                state.playerName = (_name ?? string.Empty).Trim();

                // Written now so the look survives the app being closed before the first day ends.
                SaveSystem.Save(state);

                if (_onDone != null)
                {
                    Teardown();
                    _onDone();
                    return;
                }

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
