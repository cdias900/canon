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
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour
    {
        /// <summary>Above the HUD (50) and below the dialogue canvas (100) and every modal (300).</summary>
        public const int CanvasSortingOrder = 90;

        const float SheetMargin = 26f;
        const float PlateDropPixels = 14f;

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
            layout.padding = new RectOffset(16, 16, 4, 6);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = plate.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text label = UIKit.CreateText(plate, "Text", text, UIKit.FontSize.Small, ink, TextAnchor.MiddleCenter);
            label.raycastTarget = false;
        }

        /// <summary>The cartouche, in the water above the island where a chart keeps its name.</summary>
        void BuildTitle(RectTransform sheet)
        {
            RectTransform card = Card(sheet, "TitleCard", new RectOffset(22, 22, 12, 14), 2f);
            card.anchorMin = new Vector2(0.5f, 1f);
            card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -30f);

            Text heading = UIKit.CreateText(card, "Heading", Loc.T("world.map.heading"),
                UIKit.FontSize.Body, PlateInk, TextAnchor.MiddleCenter);
            heading.raycastTarget = false;

            Text note = UIKit.CreateText(card, "Note", Loc.T("world.map.note"),
                UIKit.FontSize.Small, MutedInk, TextAnchor.MiddleCenter);
            note.raycastTarget = false;
        }

        void BuildCompass(RectTransform sheet)
        {
            Text north = UIKit.CreateText(sheet, "CompassNorth", Loc.T("world.map.north"),
                UIKit.FontSize.Body, PlateInk, TextAnchor.MiddleCenter);
            var rect = (RectTransform)north.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(58f, 44f);
            rect.anchoredPosition = new Vector2(-34f, -34f);
            north.raycastTarget = false;

            RectTransform needle = UIKit.CreateRect("CompassNeedle", sheet);
            needle.anchorMin = new Vector2(1f, 1f);
            needle.anchorMax = new Vector2(1f, 1f);
            needle.pivot = new Vector2(0.5f, 1f);
            needle.sizeDelta = new Vector2(3f, 34f);
            needle.anchoredPosition = new Vector2(-59f, -78f);

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
            RectTransform card = Card(sheet, "ScaleBar", new RectOffset(18, 18, 10, 12), 5f);
            card.anchorMin = new Vector2(1f, 0f);
            card.anchorMax = new Vector2(1f, 0f);
            card.pivot = new Vector2(1f, 0f);
            card.anchoredPosition = new Vector2(-30f, 30f);

            RectTransform bar = UIKit.CreateRect("Bar", card);
            var barLayout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 3f;
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
                element.preferredWidth = 26f;
                element.preferredHeight = 9f;
            }

            Text label = UIKit.CreateText(card, "Label", Loc.T("world.map.scale"),
                UIKit.FontSize.Small, MutedInk, TextAnchor.MiddleLeft);
            label.raycastTarget = false;
        }

        void BuildLegend(RectTransform sheet)
        {
            RectTransform card = Card(sheet, "Legend", new RectOffset(18, 18, 10, 12), 5f);
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(0f, 0f);
            card.pivot = new Vector2(0f, 0f);
            card.anchoredPosition = new Vector2(30f, 30f);

            AddLegendRow(card, "LegendHome", HomePlateFill, Loc.T("world.map.legend.home"));
            AddLegendRow(card, "LegendClosed", PlateInk, Loc.T("world.map.legend.closed"));
            AddLegendRow(card, "LegendRoad", MutedInk, Loc.T("world.map.legend.road"));
        }

        void AddLegendRow(RectTransform parent, string name, Color swatchColour, string text)
        {
            RectTransform row = UIKit.CreateRect(name, parent);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
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
            element.preferredWidth = 22f;
            element.preferredHeight = 14f;

            Text label = UIKit.CreateText(row, "Text", text, UIKit.FontSize.Small, PlateInk, TextAnchor.MiddleLeft);
            label.raycastTarget = false;
        }

        void BuildCloseButton(RectTransform root)
        {
            Button close = UIKit.CreateButton(root, "CloseMap", Loc.T("world.map.close"),
                UIKit.Palette.Clay, UIKit.Palette.Parchment, CloseNow);
            UIKit.AnchorCorner((RectTransform)close.transform, new Vector2(0.5f, 0f),
                new Vector2(330f, 118f), new Vector2(0f, 26f));
        }

        /// <summary>A parchment card that grows to whatever is put inside it.</summary>
        static RectTransform Card(RectTransform parent, string name, RectOffset padding, float spacing)
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
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        void CloseNow()
        {
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ ink

        static readonly Color BackdropColour = new Color(0.13f, 0.12f, 0.10f, 1f);
        static readonly Color PlateFill = new Color(0.93f, 0.90f, 0.83f, 0.97f);
        static readonly Color PlateInk = new Color(0.15f, 0.14f, 0.12f, 1f);
        static readonly Color MutedInk = new Color(0.38f, 0.35f, 0.30f, 1f);
        static readonly Color HomePlateFill = new Color(0.76f, 0.40f, 0.27f, 0.97f);
        static readonly Color HomePlateInk = new Color(0.97f, 0.94f, 0.89f, 1f);
    }
}
