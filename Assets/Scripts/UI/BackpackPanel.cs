using System;
using System.Collections.Generic;
using System.Globalization;
using SheepGate.Core;
using SheepGate.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The backpack: the player's character, the pieces they can put on it, and the materials they
    /// are carrying. Opened from the HUD, closed by its own button or by the scrim.
    ///
    /// ==================================================================================
    /// WARDROBE FIRST, AND WHY
    /// ==================================================================================
    /// The figure and the slot rows take the whole sheet; the materials are one compact strip along
    /// the bottom. That split is not taste. The POC has three materials and eighteen garments, and
    /// the garment half is the one carrying the design's idea — a locked piece the player can see,
    /// name and reach is a reason to come back tomorrow, and a stone counter is not. A grid of
    /// material tiles would have taken the space that argument needs.
    ///
    /// ==================================================================================
    /// THE FOUR RULES THIS SCREEN CAN BREAK, AND HOW IT DOES NOT
    /// ==================================================================================
    /// * <b>Rule 7 — never punish.</b> A locked row is the largest, most-written row on the sheet:
    ///   the piece is drawn in full colour on the character's own body, its name is normal weight,
    ///   its description reads the same as everyone else's, and underneath it the condition is
    ///   spelled out whole. It is never dimmed, never disabled, never a blank slot and never a
    ///   scold. <see cref="Button.interactable"/> stays true on a locked chip on purpose: the
    ///   variant button draws a non-interactable control at 0.40 opacity, which is exactly the
    ///   disabled-looking void this rule forbids. Tapping one answers calmly and costs nothing.
    /// * <b>Rule 10 — never show progress toward a vocation.</b> Nothing on this screen counts
    ///   anything. There is no "3 of 18 unlocked" summary, no fraction beside a condition, no bar.
    ///   A condition is met or it is not, and the sentence that says so comes from
    ///   <see cref="UnlockEvaluator"/>, whose vocabulary cannot name a scored action to begin with.
    ///   Do not add a total here, however harmless it looks: a wardrobe completion count is a task
    ///   list wearing a different hat.
    /// * <b>Rule 18 — nothing is bought.</b> No price, no currency, no timer, no "unlock now". The
    ///   only way an item opens is something the player did in the valley.
    /// * <b>Rule 13 — the smell checklist.</b> No religious iconography anywhere in here; the only
    ///   sprites are the design system's lock and check, and the character's own art layers.
    ///
    /// ==================================================================================
    /// THE FOUR CONTENT GAPS THIS SCREEN DEGRADES AROUND
    /// ==================================================================================
    /// 1. <b>The base slot has no items and cannot have any.</b> <see cref="AppearanceState"/> has
    ///    five render layers and no base layer, and the catalogue authors none.
    ///    <see cref="Wardrobe.ItemsForSlot"/> answering <see cref="CharacterSlot.Base"/> with an
    ///    empty array is correct, so this screen simply never asks: <see cref="SlotOrder"/> lists
    ///    the three slots that have contents. Any slot that comes back empty is skipped whole — an
    ///    empty heading over nothing would read as a fault.
    /// 2. <b>Five preset item ids are not in the catalogue.</b> Anything that does not resolve is
    ///    skipped with one warning and never becomes a blank row.
    /// 3. <b>Four locked items draw the same art as items the player already owns.</b> That is why
    ///    a chip is a row with a name and a sentence rather than a bare tile: two identical
    ///    thumbnails are still two clearly different items when each carries its own name. A tile
    ///    grid would have shipped four pairs of pieces the player cannot tell apart.
    /// 4. <b>tint_channels has nowhere to persist a swatch.</b> The save carries six ints and no
    ///    colour choice, and <see cref="CharacterAppearance"/> applies one tint to all five layers.
    ///    So this screen offers no recolouring at all. Showing swatches that could not be saved
    ///    would be the worse failure — the player picks a colour, leaves, and finds it gone.
    ///
    /// ==================================================================================
    /// SURFACES
    /// ==================================================================================
    /// The sheet is <see cref="UIKit.CardStyle.Card"/> over the scrim <see cref="ModalRoot"/> lays,
    /// which is the design system's elevation 2. The figure stands on a nested
    /// <see cref="UIKit.CardStyle.Panel"/>. <see cref="UIKit.CardStyle.Scroll"/> — the pergaminho,
    /// the project default for anything read at length — is deliberately not used: nothing here is
    /// read at length. The longest string on the sheet is one unlock sentence, and putting the
    /// wardrobe on a near-white reading surface would invert a screen whose subject is a character
    /// drawn in the game's own palette.
    /// </summary>
    public sealed class BackpackPanel : MonoBehaviour
    {
        /// <summary>The id this panel occupies in the modal stack.</summary>
        public const string ModalId = "backpack";

        // ------------------------------------------------------------------ locale keys
        // Every word on this screen is a key. Two tables feed it and they are not interchangeable:
        // the keys below are ui.json, read with Loc.T; an item's name and description come from
        // catalog.json and arrive already merged onto CatalogItemDef by CharacterCatalog; and an
        // unlock sentence comes from catalog.json's "unlock" table through
        // UnlockEvaluator.Sentence, which returns the finished sentence and not a key.
        //
        // The constants are named "...Key" on purpose. The content validator treats a const string
        // whose name ends in a player-text noun as a hardcoded sentence, and "TitleKey" ends in
        // "Key", which is the escape hatch it offers for exactly this.

        const string TitleKey = "backpack.title";
        const string CloseKey = "backpack.close";
        const string NewBadgeKey = "backpack.new";
        const string UnnamedItemKey = "backpack.item.unnamed";
        const string UnavailableKey = "backpack.unavailable";

        /// <summary>Headings of the three slots that have contents, in the order they are drawn.</summary>
        static readonly string[] SlotHeadingKeys =
        {
            "backpack.slot.hair",
            "backpack.slot.outfit",
            "backpack.slot.accessory"
        };

        /// <summary>Labels of the three materials, in the order the strip lists them.</summary>
        static readonly string[] MaterialLabelKeys =
        {
            "backpack.material.stone",
            "backpack.material.timber",
            "backpack.material.blocks"
        };

        // ------------------------------------------------------------------ structure

        /// <summary>
        /// The slots the sheet draws, in order. <see cref="CharacterSlot.Base"/> is absent because
        /// it has no items and no render layer to draw one into; see gap 1 in the summary.
        /// </summary>
        static readonly CharacterSlot[] SlotOrder =
        {
            CharacterSlot.Hair,
            CharacterSlot.Outfit,
            CharacterSlot.Accessory
        };

        /// <summary>GameObject names of the three sections. English, stable, and new to this build.</summary>
        static readonly string[] SlotObjectNames = { "Slot_hair", "Slot_outfit", "Slot_accessory" };

        /// <summary>GameObject names of the three material readouts.</summary>
        static readonly string[] MaterialObjectNames = { "Material_stone", "Material_timber", "Material_blocks" };

        // ------------------------------------------------------------------ metrics
        // Design points throughout, converted once. The sheet is a bottom sheet: a generous strip
        // of scrim is left above it so that "the scrim closes it too" is a real target and not a
        // twenty-point sliver nobody can hit.

        static readonly float SheetTop = DesignTokens.Px(72f);
        static readonly float SheetPadding = DesignTokens.Space.S20;
        static readonly float HeaderHeight = DesignTokens.Space.TouchTarget;
        static readonly float FigureHeight = DesignTokens.Px(168f);
        static readonly float FigureCaptionHeight = DesignTokens.Px(22f);
        static readonly float StageHeight = FigureHeight + FigureCaptionHeight;
        static readonly float MessageHeight = DesignTokens.Px(38f);
        static readonly float StripHeight = DesignTokens.Px(54f);
        static readonly float ThumbSize = DesignTokens.Px(56f);
        static readonly float ScrollbarWidth = DesignTokens.Space.S8;

        /// <summary>Top edge of the figure stage, measured from the top of the sheet.</summary>
        static readonly float StageTop = SheetPadding + HeaderHeight + DesignTokens.Space.S16;

        /// <summary>Top edge of the scrolling slot list.</summary>
        static readonly float SlotsTop = StageTop + StageHeight + DesignTokens.Space.S16;

        /// <summary>Bottom edge of the scrolling slot list, measured from the bottom of the sheet.</summary>
        static readonly float SlotsBottom =
            SheetPadding + StripHeight + DesignTokens.Space.S8 + MessageHeight + DesignTokens.Space.S8;

        /// <summary>
        /// The character sprites are 32x48, and both the big figure and every thumbnail are fitted
        /// to that ratio rather than given a size. Fitting is what keeps the figure as large as its
        /// box allows on a 1080-unit reference and on the ~977 units a phone actually reports.
        /// </summary>
        static readonly float FigureAspect =
            SheepGate.Art.CharacterArt.Width / (float)SheepGate.Art.CharacterArt.Height;

        /// <summary>The five art layers, in draw order. The last one drawn sits on top.</summary>
        const int LayerBody = 0;
        const int LayerLegs = 1;
        const int LayerTop = 2;
        const int LayerAccessory = 3;
        const int LayerHair = 4;
        const int LayerCount = 5;

        // ------------------------------------------------------------------ state

        static BackpackPanel _instance;

        GameState _state;
        bool _closed;
        bool _subscribed;

        readonly Image[] _layers = new Image[LayerCount];
        readonly List<ChipView> _chips = new List<ChipView>();

        Text _message;
        Text[] _materialCounts;
        ScrollRect _scroll;

        /// <summary>
        /// A look to draw when no run is in progress. Never written to the save — it exists so that
        /// a panel opened outside a game still draws a character instead of throwing.
        /// </summary>
        readonly AppearanceState _fallbackLook = new AppearanceState();

        /// <summary>One row of the wardrobe, and the parts of it that change on a repaint.</summary>
        sealed class ChipView
        {
            /// <summary>Catalogue id. Also the suffix of the GameObject name.</summary>
            public string ItemId;

            /// <summary>Which slot it belongs to, so a repaint can ask what that slot is showing.</summary>
            public CharacterSlot Slot;

            /// <summary>The 2px Brand.Secondary border that says this is the piece being worn.</summary>
            public Image Ring;

            /// <summary>Lock or check. A sprite, never a character — no bundled font carries either.</summary>
            public Image Status;

            /// <summary>True when the item was locked at build time. Unlocks never regress.</summary>
            public bool Locked;
        }

        // ------------------------------------------------------------------ opening and closing

        /// <summary>True while the sheet is up. Never creates a modal root to answer.</summary>
        public static bool IsOpen
        {
            get { return _instance != null; }
        }

        /// <summary>
        /// Opens the sheet, or hands back the one already up. Opening twice is a no-op rather than
        /// an error: the HUD button can be tapped twice before the first tap has finished.
        /// </summary>
        public static BackpackPanel Show()
        {
            if (_instance != null)
            {
                return _instance;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[BackpackPanel] No modal root is available; the backpack cannot open.");
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

            var panel = container.gameObject.AddComponent<BackpackPanel>();
            panel.Build(container);
            return panel;
        }

        /// <summary>Shuts the sheet. Safe to call twice.</summary>
        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;

            // Released here and not only in OnDestroy: Destroy is deferred to the end of the frame,
            // so a Show() later in this same frame would otherwise be handed back a panel that is
            // already on its way out and hand the player a sheet that vanishes.
            if (_instance == this)
            {
                _instance = null;
            }

            ModalRoot.CloseId(ModalId);
        }

        void OnDestroy()
        {
            if (_subscribed)
            {
                Wardrobe.Changed -= Refresh;
                _subscribed = false;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ------------------------------------------------------------------ building

        void Build(RectTransform container)
        {
            _instance = this;
            _state = TryGetState();

            EnsureCatalogue();

            // The worn set and the six appearance ints are recomposed once on open, so the figure
            // below is drawn from the same state the world character is drawn from. Idempotent: an
            // empty worn set changes nothing, which is why it is safe to run on every open.
            Wardrobe.ApplyToAppearance(_state);

            BuildScrimDismiss(container);

            Image sheet = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Card);
            var sheetRect = (RectTransform)sheet.transform;
            UIKit.Stretch(sheetRect, DesignTokens.Space.Gutter, DesignTokens.Space.Gutter,
                          SheetTop, DesignTokens.Space.SafeAreaBottom);

            BuildHeader(sheetRect);
            BuildStage(sheetRect);
            RectTransform slots = BuildSlots(sheetRect);
            BuildMessage(sheetRect);
            BuildMaterials(sheetRect);

            // One paint, through the same method every later change goes through, so the sheet a
            // player opens and the sheet they see after an equip cannot drift apart.
            Refresh();

            // Legacy Text measures its wrapped height against the width it currently has, so a
            // column of wrapping sentences can come out mis-sized on the frame it was built. One
            // forced rebuild settles every row before the player sees it, and the list is only sent
            // back to the top afterwards — a scroll position set against an unmeasured column is
            // applied to a height that is about to change.
            UIKit.RebuildNow(slots);
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }

            // Every row already read its badge while the badge was still there; spending them now,
            // after the sheet exists, is what makes them expire on the next open rather than on
            // this one. Order is load-bearing: doing this before BuildSlots would draw a sheet with
            // no badges on it at all.
            SpendBadges();

            Wardrobe.Changed += Refresh;
            _subscribed = true;
        }

        /// <summary>
        /// Makes the scrim a way out. A click handler and not a <see cref="Button"/>: a Selectable
        /// over the whole screen would join the keyboard navigation order as a control with no
        /// visible focus ring, which is the one state the design system does not allow.
        /// </summary>
        void BuildScrimDismiss(RectTransform container)
        {
            Transform scrim = container.Find("Scrim");
            if (scrim == null)
            {
                Debug.LogWarning("[BackpackPanel] The modal container has no Scrim child, so tapping " +
                                 "outside the sheet will not close it. The close button still does.");
                return;
            }

            var dismiss = scrim.gameObject.AddComponent<BackpackScrimDismiss>();
            dismiss.Panel = this;
        }

        void BuildHeader(RectTransform sheetRect)
        {
            RectTransform header = UIKit.CreateRect("Header", sheetRect);
            UIKit.AnchorTop(header, HeaderHeight, SheetPadding, SheetPadding, SheetPadding);

            Text title = UIKit.CreateText(header, "Title", Loc.T(TitleKey), DesignTokens.Type.Title,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft, DesignTokens.TypeRole.Title);
            UIKit.Stretch((RectTransform)title.transform, 0f,
                          DesignTokens.Space.TouchTarget + DesignTokens.Space.TouchGap, 0f, 0f);

            Button close = UIKit.CreateIconButton(header, "CloseButton", UiSpriteKeys.IconClose,
                Loc.T(CloseKey), Close);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(DesignTokens.Space.TouchTarget, DesignTokens.Space.TouchTarget);
            closeRect.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// The character, large, at the top: five stacked images and the player's own name under
        /// them. One facing is enough — this is a wardrobe, not a turntable, and the four-facing
        /// strip character creation uses is there to answer a different question.
        /// </summary>
        void BuildStage(RectTransform sheetRect)
        {
            Image stage = UIKit.CreateCard(sheetRect, "CharacterStage", UIKit.CardStyle.Panel);
            var stageRect = (RectTransform)stage.transform;
            UIKit.AnchorTop(stageRect, StageHeight, SheetPadding, SheetPadding, StageTop);
            stage.raycastTarget = false;

            RectTransform figureArea = UIKit.CreateRect("FigureArea", stageRect);
            UIKit.Stretch(figureArea, DesignTokens.Space.S12, DesignTokens.Space.S12,
                          DesignTokens.Space.S12, FigureCaptionHeight + DesignTokens.Space.S8);

            RectTransform figure = UIKit.CreateRect("Figure", figureArea);
            UIKit.Stretch(figure);

            var aspect = figure.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = FigureAspect;

            // Same draw order as the in-world character: body, legs, top, accessory, then hair over
            // the head. Sibling order is the draw order, so this loop cannot be reordered casually.
            _layers[LayerBody] = BuildLayer(figure, "Body");
            _layers[LayerLegs] = BuildLayer(figure, "Legs");
            _layers[LayerTop] = BuildLayer(figure, "Top");
            _layers[LayerAccessory] = BuildLayer(figure, "Accessory");
            _layers[LayerHair] = BuildLayer(figure, "Hair");

            Text caption = UIKit.CreateText(stageRect, "Caption", CharacterCatalog.PlayerName(_state),
                DesignTokens.Type.Minimum, DesignTokens.Ink.Muted, TextAnchor.MiddleCenter,
                DesignTokens.TypeRole.BodyStrong);
            UIKit.AnchorBottom((RectTransform)caption.transform, FigureCaptionHeight, 0f, 0f,
                               DesignTokens.Space.S8);
        }

        /// <summary>
        /// The scrolling wardrobe: one section per slot, one row per item, locked items included.
        /// A slot with nothing in it is skipped whole rather than drawn as an empty heading.
        /// </summary>
        RectTransform BuildSlots(RectTransform sheetRect)
        {
            RectTransform content;
            ScrollRect scroll = UIKit.CreateScrollView(sheetRect, "Slots", out content);
            var scrollRect = (RectTransform)scroll.transform;
            UIKit.Stretch(scrollRect, SheetPadding, SheetPadding, SlotsTop, SlotsBottom);

            // The kit's default column padding is measured for a full-bleed screen; this one already
            // sits inside the sheet's own padding, so only the scrollbar's lane is reserved.
            var column = content.GetComponent<VerticalLayoutGroup>();
            if (column != null)
            {
                column.spacing = DesignTokens.Space.S24;
                column.padding = new RectOffset(
                    0,
                    Mathf.RoundToInt(ScrollbarWidth + DesignTokens.Space.S8),
                    0,
                    Mathf.RoundToInt(DesignTokens.Space.S24));
            }

            UIKit.AttachVerticalScrollbar(scroll, ScrollbarWidth);

            int sections = 0;
            for (int i = 0; i < SlotOrder.Length; i++)
            {
                if (BuildSection(content, SlotOrder[i], SlotHeadingKeys[i], SlotObjectNames[i]))
                {
                    sections++;
                }
            }

            if (sections == 0)
            {
                // The catalogue did not load, or shipped with no wearable items. Say so plainly and
                // without blame: nothing the player did caused it, and nothing of theirs was lost.
                Debug.LogError("[BackpackPanel] No slot has any item. character_catalog.json is missing " +
                               "or empty, so the wardrobe has nothing to show.");
                UIKit.CreateText(content, "Unavailable", Loc.T(UnavailableKey), DesignTokens.Type.Body,
                    DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
            }

            _scroll = scroll;
            return content;
        }

        /// <summary>One slot's heading and rows. False when the slot has nothing to show.</summary>
        bool BuildSection(RectTransform content, CharacterSlot slot, string headingKey, string objectName)
        {
            CatalogItemDef[] items = Wardrobe.ItemsForSlot(slot);
            if (items == null || items.Length == 0)
            {
                return false;
            }

            RectTransform section = UIKit.CreateRect(objectName, content);
            UIKit.VerticalGroup(section.gameObject, DesignTokens.Space.TouchGap, new RectOffset());

            Text heading = UIKit.CreateText(section, "Heading", Loc.T(headingKey),
                DesignTokens.Type.Minimum, DesignTokens.Ink.Muted, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.BodyStrong);
            LayoutElement headingLayout = UIKit.Layout(heading);
            headingLayout.minHeight = DesignTokens.Px(20f);

            int drawn = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (BuildChip(section, items[i], slot))
                {
                    drawn++;
                }
            }

            if (drawn == 0)
            {
                // Everything in the slot was unresolvable. Leaving a heading over nothing would
                // read as a broken screen, so the section goes with it. Deactivated before it is
                // destroyed because Destroy is deferred to the end of the frame, and a layout group
                // skips an inactive child but would still measure a doomed one.
                section.gameObject.SetActive(false);
                Destroy(section.gameObject);
                return false;
            }

            return true;
        }

        /// <summary>
        /// One item, as a full-width row: the piece drawn on the character's own body, its name,
        /// its description, and — when it has not opened yet — a padlock and the condition in full.
        ///
        /// Built on <see cref="UIKit.CreateButton"/> so the row is a real design-system control with
        /// the focus ring, the hover and pressed fills and the hairline border that come with it,
        /// and then re-laid as a horizontal group. The pieces the kit already parented are marked
        /// <c>ignoreLayout</c> first: the border and the focus ring are stretched to the whole
        /// control and would otherwise be laid out as two more columns of the row.
        ///
        /// The height is the group's, not the kit's. <see cref="LayoutElement.preferredHeight"/> is
        /// cleared to -1 so the layout system skips it and reads the group instead, while
        /// <see cref="LayoutElement.minHeight"/> holds the design system's 48-point touch target as
        /// a floor. No <see cref="ContentSizeFitter"/> anywhere: the row sits inside a vertical group
        /// that already controls its height, and a fitter under a group fights it every frame.
        /// </summary>
        bool BuildChip(RectTransform section, CatalogItemDef item, CharacterSlot slot)
        {
            if (item == null || string.IsNullOrEmpty(item.id))
            {
                Debug.LogWarning("[BackpackPanel] An item in slot " + slot + " has no id and was skipped.");
                return false;
            }

            string itemId = item.id;
            bool locked = !Wardrobe.IsUnlocked(_state, itemId);

            // Read while the badge is still there to read. Every row is built before
            // SpendBadges runs, which is the whole of the badge's lifetime on screen: the player
            // sees it on this open and not on the next one. Reordering those two would spend the
            // badges first and draw a sheet with none, so the one moment the badge exists to
            // announce would happen behind the player's back.
            bool isNew = Wardrobe.IsNew(_state, itemId);

            // No label text: the row builds its own, beside a thumbnail, and a centred label
            // stretched across the whole control has nowhere to go in that arrangement. The name
            // still reaches anything that asks the control what it is, through AccessibleLabel.
            // Resolved once and passed down: a missing name is an error in the log, and one row
            // should account for one line of it, not two.
            string itemName = DisplayName(item);

            Button chip = UIKit.CreateButton(section, "Chip_" + itemId, string.Empty,
                UIKit.ButtonVariant.Secondary, () => OnChipTapped(itemId, slot));
            var chipRect = (RectTransform)chip.transform;
            AccessibleLabel.Apply(chip.gameObject, itemName);

            for (int i = 0; i < chipRect.childCount; i++)
            {
                LayoutElement ignored = UIKit.Layout(chipRect.GetChild(i));
                if (ignored != null)
                {
                    ignored.ignoreLayout = true;
                }
            }

            UIKit.HorizontalGroup(chip.gameObject, DesignTokens.Space.S12, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S12),
                Mathf.RoundToInt(DesignTokens.Space.S12),
                Mathf.RoundToInt(DesignTokens.Space.S12),
                Mathf.RoundToInt(DesignTokens.Space.S12)), TextAnchor.MiddleLeft);

            LayoutElement chipLayout = UIKit.Layout(chip);
            chipLayout.minHeight = UIKit.ButtonMinHeight;
            chipLayout.preferredHeight = -1f;

            BuildThumb(chipRect, item);

            RectTransform textColumn = UIKit.CreateRect("Text", chipRect);
            UIKit.VerticalGroup(textColumn.gameObject, DesignTokens.Space.S4, new RectOffset());
            LayoutElement textLayout = UIKit.Layout(textColumn);
            textLayout.preferredWidth = 0f;
            textLayout.flexibleWidth = 1f;

            Image status = BuildNameRow(textColumn, itemName, isNew);

            string description = item.description;
            if (!string.IsNullOrWhiteSpace(description))
            {
                UIKit.CreateText(textColumn, "Description", description, DesignTokens.Type.Minimum,
                    DesignTokens.Ink.Muted, TextAnchor.UpperLeft);
            }

            if (locked)
            {
                // Written out in full, in the body size, in the ink a sentence the player is meant
                // to read gets. UnlockEvaluator.Sentence returns the finished sentence — it is
                // resolved against catalog.json's "unlock" table, with the threshold and the plural
                // already applied — so it is never passed through Loc.
                string sentence = UnlockEvaluator.Sentence(item.unlock_condition);
                if (!string.IsNullOrEmpty(sentence))
                {
                    UIKit.CreateText(textColumn, "Unlock", sentence, DesignTokens.Type.Body,
                        DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
                }
            }

            // Drawn over the row rather than by recolouring the kit's border: VariantButton owns
            // that border's colour and repaints it on the next pointer event, so a tint applied
            // from here would survive exactly until the finger moved. Same sprite as the focus
            // ring, pulled in to the control's own rect — the focus ring sits two points further
            // out, which is what keeps "worn" and "focused" apart.
            Image ring = UIKit.CreatePanel(chipRect, "Selected", DesignTokens.Brand.Secondary,
                UiSpriteKeys.FocusRing);
            UIKit.Stretch((RectTransform)ring.transform);
            ring.raycastTarget = false;
            ring.gameObject.SetActive(false);
            UIKit.Layout(ring).ignoreLayout = true;

            _chips.Add(new ChipView
            {
                ItemId = itemId,
                Slot = slot,
                Ring = ring,
                Status = status,
                Locked = locked
            });

            return true;
        }

        /// <summary>
        /// The name, the badge when the piece is new, and the one status icon the row can carry.
        /// Returns the status image so a repaint can swap its sprite without walking the hierarchy.
        /// </summary>
        static Image BuildNameRow(RectTransform textColumn, string itemName, bool isNew)
        {
            RectTransform row = UIKit.CreateRect("Name", textColumn);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S8, new RectOffset(),
                TextAnchor.MiddleLeft);

            Text label = UIKit.CreateText(row, "Label", itemName, DesignTokens.Type.Body,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);

            // Preferred width zero and all the flexible width: the name then takes whatever the
            // badge and the icon leave, instead of asking for its unwrapped length and squeezing
            // them out of the row.
            LayoutElement labelLayout = UIKit.Layout(label);
            labelLayout.preferredWidth = 0f;
            labelLayout.flexibleWidth = 1f;

            if (isNew)
            {
                BuildNewBadge(row);
            }

            // One icon slot, built once and switched on a repaint. Gold when it marks what is worn,
            // because gold is this system's "touched" colour; the quieter secondary ink under the
            // padlock, because a lock is neither new nor touchable and gold would say it was.
            Image status = UIKit.CreateIcon(row, "Status", UiSpriteKeys.IconCheck,
                DesignTokens.Brand.Secondary, UIKit.IconSize);
            status.gameObject.SetActive(false);
            return status;
        }

        /// <summary>
        /// The NOVO badge: gold, because the design system's gold marks what is new, and that is
        /// this badge's whole job. It is not a call to action and does not spend the one gold CTA a
        /// screen is allowed — this sheet has no Quest button at all, because it has no single
        /// action to nominate.
        /// </summary>
        static void BuildNewBadge(RectTransform row)
        {
            Image badge = UIKit.CreatePanel(row, "New", DesignTokens.Brand.Secondary, UiSpriteKeys.FrameSm);
            badge.raycastTarget = false;

            var badgeRect = (RectTransform)badge.transform;
            UIKit.HorizontalGroup(badge.gameObject, 0f, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S8),
                Mathf.RoundToInt(DesignTokens.Space.S8),
                Mathf.RoundToInt(DesignTokens.Space.S4),
                Mathf.RoundToInt(DesignTokens.Space.S4)), TextAnchor.MiddleCenter);

            UIKit.CreateText(badgeRect, "Label", Loc.T(NewBadgeKey), DesignTokens.Type.Minimum,
                DesignTokens.Ink.OnSecondary, TextAnchor.MiddleCenter, DesignTokens.TypeRole.BodyStrong);
        }

        /// <summary>
        /// The piece, drawn on the character's own body at thumbnail size.
        ///
        /// On the body and not alone, because four locked items in this catalogue write the same art
        /// indices as items the player already has, and a bracelet floating on a blank square is
        /// unreadable at any size. Seeing it worn is also what makes the locked row an invitation
        /// rather than a listing: the player is looking at themselves in it.
        /// </summary>
        void BuildThumb(RectTransform chipRect, CatalogItemDef item)
        {
            RectTransform thumb = UIKit.CreateRect("Thumb", chipRect);
            LayoutElement thumbLayout = UIKit.Layout(thumb);
            thumbLayout.minWidth = ThumbSize;
            thumbLayout.preferredWidth = ThumbSize;
            thumbLayout.minHeight = ThumbSize;
            thumbLayout.preferredHeight = ThumbSize;
            thumbLayout.flexibleWidth = 0f;

            Image surface = UIKit.CreatePanel(thumb, "Surface", DesignTokens.Surface.Panel,
                UiSpriteKeys.FrameSm);
            UIKit.Stretch((RectTransform)surface.transform);
            surface.raycastTarget = false;

            RectTransform figureArea = UIKit.CreateRect("FigureArea", thumb);
            UIKit.Stretch(figureArea, DesignTokens.Space.S4, DesignTokens.Space.S4,
                          DesignTokens.Space.S4, DesignTokens.Space.S4);

            RectTransform figure = UIKit.CreateRect("Figure", figureArea);
            UIKit.Stretch(figure);

            var aspect = figure.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = FigureAspect;

            AppearanceState look = Look();

            Image body = BuildLayer(figure, "Body");
            ApplyLayer(body, UIKit.GetSprite(UiSpriteKeys.Body(look.BodyArtVariant, FacingDirection.Down, 0)),
                Shade(DesignTokens.Neutral.N500, look.body), 0f, 1f);

            // The plain block, never the hooded one: a thumbnail shows the piece itself, and which
            // variant it draws in a set depends on what else is worn, which the row cannot know.
            ItemArtDef art = CharacterCatalog.ArtFor(item, false);
            if (art == null)
            {
                return;
            }

            if (art.legs.HasValue)
            {
                ApplyLayer(BuildLayer(figure, "Legs"), UIKit.GetSprite(UiSpriteKeys.Legs(art.legs.Value)),
                    Shade(DesignTokens.Ambient.Sky, art.legs.Value), 0.04f, 0.44f);
            }

            if (art.top.HasValue)
            {
                ApplyLayer(BuildLayer(figure, "Top"), UIKit.GetSprite(UiSpriteKeys.Top(art.top.Value)),
                    Shade(DesignTokens.Brand.Primary, art.top.Value), 0.44f, 0.76f);
            }

            if (art.accessory.HasValue)
            {
                ApplyLayer(BuildLayer(figure, "Accessory"),
                    UIKit.GetSprite(UiSpriteKeys.Accessory(art.accessory.Value)),
                    Shade(DesignTokens.Ambient.Growth, art.accessory.Value), 0.78f, 0.97f);
            }

            if (art.hair.HasValue)
            {
                ApplyLayer(BuildLayer(figure, "Hair"),
                    UIKit.GetSprite(SheepGate.Art.ArtKeys.Hair(art.hair.Value,
                        UiSpriteKeys.ToArtFacing(FacingDirection.Down))),
                    Shade(DesignTokens.Ambient.Sky, art.hair.Value), 0.76f, 1f);
            }
        }

        /// <summary>
        /// The line that answers a tap the wardrobe could not carry out.
        ///
        /// <c>Ink.Secondary</c> and never <c>Feedback.Error</c>. Two pieces that do not go together
        /// is information about the pieces, not a mistake the player made, and nothing was lost by
        /// trying — colouring it as an error would make a wardrobe scold, which is the shape rule 7
        /// exists to keep out of this game.
        ///
        /// The line's height is reserved whether or not it has anything in it, so the sheet does
        /// not jump under the player's finger the first time it does.
        /// </summary>
        void BuildMessage(RectTransform sheetRect)
        {
            _message = UIKit.CreateText(sheetRect, "Message", string.Empty, DesignTokens.Type.Body,
                DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft);
            UIKit.AnchorBottom((RectTransform)_message.transform, MessageHeight, SheetPadding, SheetPadding,
                SheetPadding + StripHeight + DesignTokens.Space.S8);
        }

        /// <summary>
        /// The materials, as one strip and not a grid: three of them, and the wardrobe above is what
        /// this sheet is for.
        ///
        /// Every cell carries its label and its count together, and the count is mono so its digits
        /// are tabular and do not shuffle sideways as they climb. A material at zero still shows —
        /// a strip that hid what the player lacks would teach them nothing about what the wall needs
        /// next, which is the one thing this readout is for.
        ///
        /// The numbers are read straight off <see cref="GameState"/>. That is the same storage
        /// <c>ResourceSystem.Stone</c>, <c>ResourceSystem.Timber</c> and <c>ResourceSystem.Blocks</c>
        /// wrap, and reading it directly is what guarantees the strip and the figure above it are
        /// describing the same run.
        /// </summary>
        void BuildMaterials(RectTransform sheetRect)
        {
            Image strip = UIKit.CreateCard(sheetRect, "Materials", UIKit.CardStyle.Panel);
            var stripRect = (RectTransform)strip.transform;
            UIKit.AnchorBottom(stripRect, StripHeight, SheetPadding, SheetPadding, SheetPadding);
            strip.raycastTarget = false;

            UIKit.HorizontalGroup(strip.gameObject, DesignTokens.Space.S8, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S12),
                Mathf.RoundToInt(DesignTokens.Space.S12),
                0,
                0), TextAnchor.MiddleLeft);

            _materialCounts = new Text[MaterialLabelKeys.Length];
            for (int i = 0; i < MaterialLabelKeys.Length; i++)
            {
                RectTransform cell = UIKit.CreateRect(MaterialObjectNames[i], stripRect);
                UIKit.HorizontalGroup(cell.gameObject, DesignTokens.Space.S8, new RectOffset(),
                    TextAnchor.MiddleLeft);
                LayoutElement cellLayout = UIKit.Layout(cell);
                cellLayout.preferredWidth = 0f;
                cellLayout.flexibleWidth = 1f;

                Text label = UIKit.CreateText(cell, "Label", Loc.T(MaterialLabelKeys[i]),
                    DesignTokens.Type.Minimum, DesignTokens.Ink.Muted, TextAnchor.MiddleLeft);
                LayoutElement labelLayout = UIKit.Layout(label);
                labelLayout.preferredWidth = 0f;
                labelLayout.flexibleWidth = 1f;

                _materialCounts[i] = UIKit.CreateText(cell, "Count", string.Empty, DesignTokens.Type.Mono,
                    DesignTokens.Ink.Primary, TextAnchor.MiddleRight, DesignTokens.TypeRole.Mono);
            }
        }

        // ------------------------------------------------------------------ interaction

        /// <summary>
        /// A tap on a row.
        ///
        /// Three outcomes, and none of them takes anything away:
        /// * <b>The piece is not worn.</b> It goes on, through <see cref="Wardrobe.TryEquip"/>,
        ///   which does the swap a one-item slot needs and asks the catalogue about the set that
        ///   would result before it writes anything. A refusal leaves the player dressed exactly as
        ///   they were and puts one calm sentence on the sheet.
        /// * <b>The piece is worn, in a slot that holds several.</b> It comes off. The accessory
        ///   layer then falls back to whichever accessory is still worn; when it was the only one,
        ///   the layer keeps the look it had, which is <see cref="Wardrobe.Unequip"/>'s documented
        ///   behaviour and the reason a character can never come out blank.
        /// * <b>The piece is worn, in a slot that holds one.</b> Nothing happens. A hairstyle and an
        ///   outfit are always being worn, so a second tap on the current one is not a way to
        ///   undress — it is a tap on the answer that is already selected. The way out of a look is
        ///   another look.
        ///
        /// A locked row is tapped through the same path on purpose. It is a live control, it answers
        /// with the refusal key the wardrobe hands back, and it costs nothing — the alternative,
        /// disabling it, is what would draw the row at 0.40 opacity and turn an invitation into the
        /// disabled void rule 7 forbids.
        /// </summary>
        void OnChipTapped(string itemId, CharacterSlot slot)
        {
            if (string.Equals(Wardrobe.EquippedInSlot(_state, slot), itemId, StringComparison.Ordinal))
            {
                if (CharacterCatalog.SlotHoldsMany(slot))
                {
                    Wardrobe.Unequip(_state, itemId);
                }

                ShowMessage(null);
                return;
            }

            string refusalKey;
            if (Wardrobe.TryEquip(_state, itemId, out refusalKey))
            {
                ShowMessage(null);
                return;
            }

            ShowMessage(string.IsNullOrEmpty(refusalKey) ? null : Loc.T(refusalKey));
        }

        void ShowMessage(string sentence)
        {
            if (_message != null)
            {
                _message.text = sentence ?? string.Empty;
            }
        }

        // ------------------------------------------------------------------ repainting

        /// <summary>
        /// Redraws everything that can have changed. Wired to <see cref="Wardrobe.Changed"/>, so an
        /// equip from anywhere lands here and the figure follows in the same frame as the ring.
        /// </summary>
        void Refresh()
        {
            RefreshFigure();
            RefreshMaterials();

            for (int i = 0; i < _chips.Count; i++)
            {
                ChipView chip = _chips[i];
                if (chip == null)
                {
                    continue;
                }

                // "Worn" is what the slot is actually showing, not merely what is in the worn list.
                // The accessory slot may legitimately hold several pieces while AppearanceState has
                // one accessory layer, so marking every one of them with a ring would put a check
                // beside items the figure is not drawing.
                bool worn = string.Equals(Wardrobe.EquippedInSlot(_state, chip.Slot), chip.ItemId,
                    StringComparison.Ordinal);

                if (chip.Ring != null)
                {
                    chip.Ring.gameObject.SetActive(worn);
                }

                if (chip.Status == null)
                {
                    continue;
                }

                if (worn)
                {
                    chip.Status.sprite = UIKit.GetSprite(UiSpriteKeys.IconCheck);
                    chip.Status.color = DesignTokens.Brand.Secondary;
                    chip.Status.gameObject.SetActive(true);
                }
                else if (chip.Locked)
                {
                    chip.Status.sprite = UIKit.GetSprite(UiSpriteKeys.IconLock);
                    chip.Status.color = DesignTokens.Ink.Secondary;
                    chip.Status.gameObject.SetActive(true);
                }
                else
                {
                    chip.Status.gameObject.SetActive(false);
                }
            }
        }

        void RefreshFigure()
        {
            AppearanceState look = Look();

            ApplyLayer(_layers[LayerBody],
                UIKit.GetSprite(UiSpriteKeys.Body(look.BodyArtVariant, FacingDirection.Down, 0)),
                Shade(DesignTokens.Neutral.N500, look.body), 0f, 1f);

            ApplyLayer(_layers[LayerLegs], UIKit.GetSprite(UiSpriteKeys.Legs(look.legs)),
                Shade(DesignTokens.Ambient.Sky, look.legs), 0.04f, 0.44f);

            ApplyLayer(_layers[LayerTop], UIKit.GetSprite(UiSpriteKeys.Top(look.top)),
                Shade(DesignTokens.Brand.Primary, look.top), 0.44f, 0.76f);

            ApplyLayer(_layers[LayerAccessory], UIKit.GetSprite(UiSpriteKeys.Accessory(look.accessory)),
                Shade(DesignTokens.Ambient.Growth, look.accessory), 0.78f, 0.97f);

            ApplyLayer(_layers[LayerHair],
                UIKit.GetSprite(SheepGate.Art.ArtKeys.Hair(look.hair,
                    UiSpriteKeys.ToArtFacing(FacingDirection.Down))),
                Shade(DesignTokens.Ambient.Sky, look.hair), 0.76f, 1f);
        }

        void RefreshMaterials()
        {
            if (_materialCounts == null)
            {
                return;
            }

            int stone = _state != null ? Mathf.Max(0, _state.stone) : 0;
            int timber = _state != null ? Mathf.Max(0, _state.timber) : 0;
            int blocks = _state != null ? Mathf.Max(0, _state.blocks) : 0;

            SetCount(0, stone);
            SetCount(1, timber);
            SetCount(2, blocks);
        }

        void SetCount(int index, int value)
        {
            if (index < 0 || index >= _materialCounts.Length || _materialCounts[index] == null)
            {
                return;
            }

            _materialCounts[index].text = value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Spends every badge in the three slots the sheet shows.
        ///
        /// After the rows exist, never before. Each row read <see cref="Wardrobe.IsNew"/> while it
        /// was still true, so this is what makes the badges the player is looking at the last ones
        /// they will see for those items. <see cref="Wardrobe.MarkSlotSeen"/> refuses to spend a
        /// locked item's badge, which is what keeps the announcement for the day it actually opens.
        /// </summary>
        void SpendBadges()
        {
            for (int i = 0; i < SlotOrder.Length; i++)
            {
                Wardrobe.MarkSlotSeen(_state, SlotOrder[i]);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// The item's player-facing name, or a visible stand-in.
        ///
        /// The name lives in <c>locales/&lt;locale&gt;/catalog.json</c> and is merged onto the
        /// definition at load time, so a missing one means a translation gap. Showing the raw id
        /// would put an English identifier on a pt-BR screen; showing a localised placeholder keeps
        /// the row readable and makes the gap obvious to whoever is looking at it.
        /// </summary>
        static string DisplayName(CatalogItemDef item)
        {
            if (item != null && !string.IsNullOrWhiteSpace(item.display))
            {
                return item.display;
            }

            Debug.LogError("[BackpackPanel] Item '" + (item != null ? item.id : "null") +
                           "' has no display name in locale " + CharacterCatalog.LoadedLocale +
                           ". Add it to Resources/Data/locales/" + CharacterCatalog.LoadedLocale +
                           "/catalog.json under \"items\".");
            return Loc.T(UnnamedItemKey);
        }

        /// <summary>The look to draw. Never null, so no code path here can blank the character.</summary>
        AppearanceState Look()
        {
            if (_state != null && _state.appearance != null)
            {
                return _state.appearance;
            }

            return _fallbackLook;
        }

        /// <summary>
        /// Loads the catalogue if nothing has. The boot sequence normally does it long before the
        /// backpack opens; this covers a scene that came up without one, and answers the otherwise
        /// silent failure where the sheet opens completely empty.
        /// </summary>
        static void EnsureCatalogue()
        {
            if (CharacterCatalog.Items != null && CharacterCatalog.Items.Length > 0)
            {
                return;
            }

            Debug.LogWarning("[BackpackPanel] The character catalogue was not loaded before the backpack " +
                             "opened; loading it now. The boot sequence should have done this.");
            CharacterCatalog.LoadAll();
        }

        static GameState TryGetState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }

        static Image BuildLayer(RectTransform figure, string objectName)
        {
            Image image = UIKit.CreatePanel(figure, objectName, Color.white, null);
            UIKit.Stretch((RectTransform)image.transform);
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        /// <summary>
        /// A present sprite fills the figure, exactly as the in-world character stacks its layers.
        /// A missing one degrades to a coloured band in that layer's part of the figure, so the
        /// pieces still read as different from one another while art is being made — the same
        /// fallback the character creation screen uses, for the same reason.
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
    }

    /// <summary>
    /// Closes the backpack when the scrim behind it is tapped.
    ///
    /// A click handler rather than a <see cref="Button"/>: a Selectable stretched over the whole
    /// screen would enter the keyboard navigation order as a control with no visible focus ring,
    /// and the design system's rule is that every control has one. This has no visual state at all,
    /// which is correct — the scrim is a way out, not a control.
    /// </summary>
    sealed class BackpackScrimDismiss : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>The sheet this scrim belongs to.</summary>
        public BackpackPanel Panel;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Panel != null)
            {
                Panel.Close();
            }
        }
    }
}
