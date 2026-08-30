using System;
using SheepGate.Art;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Player;
using SheepGate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.World
{
    /// <summary>
    /// The map the player opens from the HUD: a chart of the region, drawn on parchment.
    ///
    /// It is a drawing rather than the live world seen from far away, and that is the whole
    /// design. The world at that distance is the same ground tile everywhere under different
    /// tints, which reads as camouflage; a chart is made of lines, and lines say coast, road and
    /// high ground in one stroke each. It is also the artefact this fiction would produce — a
    /// survey by somebody who walked the ground before saying a word about repairing it.
    ///
    /// The opening keeps the world view. That beat has to push the camera in from the region to
    /// the city in one continuous move, which a sheet of paper cannot do.
    ///
    /// Everything drawn on the sheet is on the design system's pergaminho ink: <c>Ink.OnScroll</c>
    /// and <c>Ink.OnScrollMuted</c> over <c>Surface.Scroll</c>. The screen was carrying six
    /// hand-mixed colours before, all of them approximations of tokens that already existed.
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour
    {
        /// <summary>Above the HUD (50) and below the dialogue canvas (100) and every modal (300).</summary>
        public const int CanvasSortingOrder = 90;

        /// <summary>Gap between the safe area and the sheet, so the chart reads as paper on a table.</summary>
        static readonly float SheetMargin = DesignTokens.Space.S12;

        /// <summary>How far a name plate hangs below the place it names.</summary>
        static readonly float PlateDropPixels = DesignTokens.Space.S8;

        /// <summary>
        /// Width of the way out. Wide enough for "Voltar para a cidade" on one line at the design
        /// system's body size — at the width this button used to be, the label wrapped to two
        /// lines inside a control that was also below the 48 point touch floor.
        /// </summary>
        static readonly float CloseButtonWidth = DesignTokens.Px(280f);

        /// <summary>Clearance above the home indicator, on top of the safe area the button is in.</summary>
        static readonly float CloseButtonMargin = DesignTokens.Space.SafeAreaBottom;

        /// <summary>
        /// How far the legend and the scale bar sit above the bottom of the sheet.
        ///
        /// Derived from the close button rather than chosen, because the button is anchored to the
        /// safe area and the cards are anchored to the sheet inside it: with both numbers written
        /// by hand the two overlapped, and at the design system's type they would have overlapped
        /// by more. Subtracting <see cref="SheetMargin"/> converts the button's own clearance out
        /// of safe-area coordinates and into the sheet's.
        /// </summary>
        static readonly float CornerCardInsetY =
            CloseButtonMargin + UIKit.ButtonMinHeight + DesignTokens.Space.S12 - SheetMargin;

        static readonly float CornerCardInsetX = DesignTokens.Space.S12;

        /// <summary>
        /// Width of the text column inside the cartouche, and the only thing stopping it from
        /// growing into the compass.
        ///
        /// A card that sizes itself to its longest line is a card whose width is a property of the
        /// translation, and at the design system's Title size "The cities of the exile" is wide
        /// enough to reach the corner where the compass is. Capping the column makes the heading
        /// wrap instead, so the cartouche is the same shape in every language and the collision
        /// cannot come back with the next one.
        /// </summary>
        static readonly float CardTextWidth = DesignTokens.Px(170f);

        static WorldMapView _current;

        Canvas _canvas;
        PlayerController _player;
        bool _inputLocked;

        /// <summary>True while the map is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null; }
        }

        /// <summary>
        /// Opens the map, or does nothing when it is already up or when something else owns the
        /// screen. A cutscene, a line of dialogue and a modal all mean the player is being shown
        /// something, and covering that with a chart cuts across it.
        /// </summary>
        public static void Open()
        {
            if (_current != null || !CanOpen())
            {
                return;
            }

            var host = new GameObject("WorldMapView");
            host.AddComponent<WorldMapView>();
        }

        /// <summary>Closes the map if it is open. Safe to call when it is not.</summary>
        public static void Close()
        {
            if (_current != null)
            {
                _current.CloseNow();
            }
        }

        public static void Toggle()
        {
            if (_current != null)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>
        /// Whether the map may be opened right now. Public so the HUD can grey its own button out
        /// instead of offering one that would silently do nothing.
        /// </summary>
        public static bool CanOpen()
        {
            if (IntroCutscene.IsPlaying || ModalRoot.IsOpen)
            {
                return false;
            }

            DialogueSystem dialogue = FindFirstObjectByType<DialogueSystem>();
            return dialogue == null || !dialogue.IsPlaying;
        }

        void Awake()
        {
            if (_current != null && _current != this)
            {
                Destroy(gameObject);
                return;
            }

            _current = this;
            Build();
        }

        void OnDestroy()
        {
            if (_current != this)
            {
                return;
            }

            _current = null;

            // Whatever went wrong, the player must not be left unable to move.
            if (_inputLocked)
            {
                _inputLocked = false;
                InputLock.Pop();
            }

            if (_player != null)
            {
                _player.InputEnabled = true;
            }

            HUD hud = HUD.Current;
            if (hud != null)
            {
                hud.SetVisible(true);
            }
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            InputLock.Push();
            _inputLocked = true;

            _player = FindFirstObjectByType<PlayerController>();
            if (_player != null)
            {
                _player.InputEnabled = false;
            }

            HUD hud = HUD.Current;
            if (hud != null)
            {
                hud.SetVisible(false);
            }

            _canvas = UIKit.CreateCanvas("WorldMapViewCanvas", CanvasSortingOrder);
            _canvas.transform.SetParent(transform, false);

            // The backdrop covers the whole screen, chrome included: a chart with the village
            // showing around its edges would read as a window rather than as a sheet of paper.
            Image backdrop = UIKit.Bleed(UIKit.CreatePanel((RectTransform)_canvas.transform, "Backdrop", BackdropColour));
            backdrop.raycastTarget = true;

            RectTransform root = UIKit.SafeArea(_canvas);
            RectTransform sheet = BuildSheet(root);

            BuildPlates(sheet);
            BuildTitle(sheet);
            BuildCompass(sheet);
            BuildScaleBar(sheet);
            BuildLegend(sheet);
            BuildCloseButton(root);
        }

        /// <summary>
        /// The chart itself, fitted inside the safe area without distortion. An aspect fitter
        /// rather than a stretch: a map stretched to the shape of a phone is a map that lies about
        /// which way is further.
        /// </summary>
        RectTransform BuildSheet(RectTransform root)
        {
            RectTransform sheet = UIKit.CreateRect("Sheet", root);
            UIKit.Stretch(sheet, SheetMargin, SheetMargin, SheetMargin, SheetMargin);

            var image = sheet.gameObject.AddComponent<Image>();
            image.sprite = MapChartArt.Get();
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;

            return sheet;
        }

        /// <summary>
        /// A name plate on every place. Anchored by the fraction the drawing reports rather than
        /// by a measured pixel, so the plates keep their places whatever the sheet is scaled to.
        /// </summary>
        void BuildPlates(RectTransform sheet)
        {
            AddPlate(sheet, "PlateHome", Loc.T("world.map.here"), MapChartArt.HomeAnchor,
                HomePlateFill, HomePlateInk);

            for (int i = 0; i < MapChartArt.PlaceCount; i++)
            {
                Vector2 anchor = MapChartArt.PlaceAnchor(i);
                anchor.y -= MapChartArt.PlateDrop;
                AddPlate(sheet, "Plate" + i, Loc.T("world.map.closed"), anchor, PlateFill, PlateInk);
            }
        }

        void AddPlate(RectTransform sheet, string name, string text, Vector2 anchor, Color fill, Color ink)
        {
            RectTransform plate = UIKit.CreateRect(name, sheet);
            plate.anchorMin = anchor;
            plate.anchorMax = anchor;
            plate.pivot = new Vector2(0.5f, 1f);
            plate.anchoredPosition = new Vector2(0f, -PlateDropPixels);

            var image = plate.gameObject.AddComponent<Image>();
            image.sprite = ArtLibrary.Get(ArtKeys.UiBubble);
            image.type = Image.Type.Sliced;
            image.color = fill;
            image.raycastTarget = false;

            var layout = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = Inset(DesignTokens.Space.S12, DesignTokens.Space.S4);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = plate.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The floor of the type scale, at body-strong weight. A plate is one or two words on a
            // small parchment tab, and weight is what keeps them legible there without making the
            // tab big enough to cover the place it points at.
            Text label = UIKit.CreateText(plate, "Text", text, DesignTokens.Type.Minimum, ink,
                TextAnchor.MiddleCenter, DesignTokens.TypeRole.BodyStrong);
            label.raycastTarget = false;
        }

        /// <summary>The cartouche, in the water above the island where a chart keeps its name.</summary>
        void BuildTitle(RectTransform sheet)
        {
            RectTransform card = Card(sheet, "TitleCard",
                Inset(DesignTokens.Space.S16, DesignTokens.Space.S8), DesignTokens.Space.S4,
                TextAnchor.UpperCenter);
            card.anchorMin = new Vector2(0.5f, 1f);
            card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -DesignTokens.Space.S12);

            Text heading = UIKit.CreateText(card, "Heading", Loc.T("world.map.heading"),
                DesignTokens.Type.Title, PlateInk, TextAnchor.MiddleCenter, DesignTokens.TypeRole.Title);
            heading.raycastTarget = false;
            UIKit.Layout(heading).preferredWidth = CardTextWidth;

            Text note = UIKit.CreateText(card, "Note", Loc.T("world.map.note"),
                DesignTokens.Type.Minimum, MutedInk, TextAnchor.MiddleCenter);
            note.raycastTarget = false;
            UIKit.Layout(note).preferredWidth = CardTextWidth;
        }

        void BuildCompass(RectTransform sheet)
        {
            Text north = UIKit.CreateText(sheet, "CompassNorth", Loc.T("world.map.north"),
                DesignTokens.Type.Title, PlateInk, TextAnchor.MiddleCenter, DesignTokens.TypeRole.Title);
            var rect = (RectTransform)north.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(CompassLetterWidth, CompassLetterHeight);
            rect.anchoredPosition = new Vector2(-DesignTokens.Space.S16, -DesignTokens.Space.S16);
            north.raycastTarget = false;

            RectTransform needle = UIKit.CreateRect("CompassNeedle", sheet);
            needle.anchorMin = new Vector2(1f, 1f);
            needle.anchorMax = new Vector2(1f, 1f);
            needle.pivot = new Vector2(0.5f, 1f);
            needle.sizeDelta = new Vector2(NeedleThickness, DesignTokens.Px(14f));

            // Centred under the letter, which is itself pinned to the corner: both offsets are read
            // off the letter's own box rather than measured a second time.
            needle.anchoredPosition = new Vector2(
                -(DesignTokens.Space.S16 + CompassLetterWidth * 0.5f),
                -(DesignTokens.Space.S16 + CompassLetterHeight + DesignTokens.Space.S4));

            var image = needle.gameObject.AddComponent<Image>();
            image.color = PlateInk;
            image.raycastTarget = false;
        }

        /// <summary>
        /// The scale bar, measured in days walked rather than in units. Nobody in this fiction owns
        /// a surveyor's chain, and a distance the player can feel is worth more than one they would
        /// have to convert.
        /// </summary>
        void BuildScaleBar(RectTransform sheet)
        {
            RectTransform card = Card(sheet, "ScaleBar",
                Inset(DesignTokens.Space.S12, DesignTokens.Space.S8), DesignTokens.Space.S4);
            card.anchorMin = new Vector2(1f, 0f);
            card.anchorMax = new Vector2(1f, 0f);
            card.pivot = new Vector2(1f, 0f);
            card.anchoredPosition = new Vector2(-CornerCardInsetX, CornerCardInsetY);

            RectTransform bar = UIKit.CreateRect("Bar", card);
            var barLayout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = DesignTokens.Px(2f);
            barLayout.childAlignment = TextAnchor.MiddleLeft;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = false;
            barLayout.childForceExpandHeight = false;

            for (int i = 0; i < 4; i++)
            {
                RectTransform tick = UIKit.CreateRect("Tick" + i, bar);
                var tickImage = tick.gameObject.AddComponent<Image>();
                tickImage.color = i % 2 == 0 ? PlateInk : MutedInk;
                tickImage.raycastTarget = false;

                var element = tick.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = DesignTokens.Px(12f);
                element.preferredHeight = DesignTokens.Px(4f);
            }

            Text label = UIKit.CreateText(card, "Label", Loc.T("world.map.scale"),
                DesignTokens.Type.Minimum, MutedInk, TextAnchor.MiddleLeft);
            label.raycastTarget = false;
        }

        void BuildLegend(RectTransform sheet)
        {
            RectTransform card = Card(sheet, "Legend",
                Inset(DesignTokens.Space.S12, DesignTokens.Space.S8), DesignTokens.Space.S4);
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(0f, 0f);
            card.pivot = new Vector2(0f, 0f);
            card.anchoredPosition = new Vector2(CornerCardInsetX, CornerCardInsetY);

            AddLegendRow(card, "LegendHome", HomePlateFill, Loc.T("world.map.legend.home"));
            AddLegendRow(card, "LegendClosed", PlateInk, Loc.T("world.map.legend.closed"));
            AddLegendRow(card, "LegendRoad", MutedInk, Loc.T("world.map.legend.road"));
        }

        /// <summary>
        /// One key row: a swatch and the word for it. Both, always — the design system's rule that
        /// colour never carries a meaning on its own is exactly what a legend is for.
        /// </summary>
        void AddLegendRow(RectTransform parent, string name, Color swatchColour, string text)
        {
            RectTransform row = UIKit.CreateRect(name, parent);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = DesignTokens.Space.S8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            RectTransform swatch = UIKit.CreateRect("Swatch", row);
            var image = swatch.gameObject.AddComponent<Image>();
            image.color = swatchColour;
            image.raycastTarget = false;

            var element = swatch.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = DesignTokens.Px(12f);
            element.preferredHeight = DesignTokens.Px(8f);

            Text label = UIKit.CreateText(row, "Text", text, DesignTokens.Type.Minimum, PlateInk,
                TextAnchor.MiddleLeft);
            label.raycastTarget = false;
        }

        /// <summary>
        /// The way back. Clay, because it is the one action on the screen and it sits over the dark
        /// backdrop rather than on the sheet, where the filled variants are the only ones that keep
        /// their contrast.
        /// </summary>
        void BuildCloseButton(RectTransform root)
        {
            Button close = UIKit.CreateButton(root, "CloseMap", Loc.T("world.map.close"),
                UIKit.ButtonVariant.Primary, CloseNow);
            UIKit.AnchorCorner((RectTransform)close.transform, new Vector2(0.5f, 0f),
                new Vector2(CloseButtonWidth, UIKit.ButtonMinHeight),
                new Vector2(0f, CloseButtonMargin));
        }

        /// <summary>
        /// A parchment card that grows to whatever is put inside it. The caller says how its
        /// contents sit across the card, because a cartouche centres its two lines and a key does
        /// not.
        /// </summary>
        static RectTransform Card(RectTransform parent, string name, RectOffset padding, float spacing,
            TextAnchor alignment = TextAnchor.UpperLeft)
        {
            RectTransform rect = UIKit.CreateRect(name, parent);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ArtLibrary.Get(ArtKeys.UiBubble);
            image.type = Image.Type.Sliced;
            image.color = PlateFill;
            image.raycastTarget = false;

            var column = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = padding;
            column.spacing = spacing;
            column.childAlignment = alignment;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        /// <summary>
        /// A symmetric padding from two spacing tokens. <see cref="RectOffset"/> takes whole
        /// pixels, and the tokens are the design document's points converted, so they round here
        /// rather than at every call site.
        /// </summary>
        static RectOffset Inset(float horizontal, float vertical)
        {
            int x = Mathf.RoundToInt(horizontal);
            int y = Mathf.RoundToInt(vertical);
            return new RectOffset(x, x, y, y);
        }

        void CloseNow()
        {
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ ink and metrics

        static readonly float CompassLetterWidth = DesignTokens.Px(28f);
        static readonly float CompassLetterHeight = DesignTokens.Px(26f);
        static readonly float NeedleThickness = DesignTokens.Px(1.5f);

        /// <summary>Below the sheet: the table the chart is lying on, not a colour of its own.</summary>
        static readonly Color BackdropColour = DesignTokens.Surface.Background;

        /// <summary>
        /// The plates are pergaminho, very slightly transparent so the chart under them is still
        /// felt. Opacity is the one modification the design system allows to a token; a fifth
        /// off-white would not have been.
        /// </summary>
        static readonly Color PlateFill = UIKit.WithAlpha(DesignTokens.Surface.Scroll, 0.97f);
        static readonly Color PlateInk = DesignTokens.Ink.OnScroll;
        static readonly Color MutedInk = DesignTokens.Ink.OnScrollMuted;

        /// <summary>This city, in the action colour. The one plate that is not a place to go.</summary>
        static readonly Color HomePlateFill = UIKit.WithAlpha(DesignTokens.Brand.Primary, 0.97f);
        static readonly Color HomePlateInk = DesignTokens.Ink.OnPrimary;
    }
}
