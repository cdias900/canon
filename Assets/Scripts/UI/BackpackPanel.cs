using System;
using System.Collections.Generic;
using System.Globalization;
using SheepGate.Core;
using SheepGate.Economy;
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
    /// FOUR TABS, AND WHY THE SHEET STOPPED BEING ONE SURFACE
    /// ==================================================================================
    /// The previous version of this screen put the figure, three slot sections and a materials
    /// strip on one scrolling surface. Measured on an iPhone 17 Pro, the list showed about 1.7
    /// rows and clipped its own sentences mid-word, because the stage was spending 190 design
    /// points at the top and the strip and the refusal line were spending another 108 at the
    /// bottom of a 750-point card.
    ///
    /// So the sheet is now four tabs over one content band, and everything the redesign buys is
    /// vertical: the stage went from 190 to 124 by turning horizontal, the materials strip and the
    /// bottom refusal band are gone from the wardrobe entirely, and the three slot headings went
    /// with them — a tab <i>is</i> its heading. The list shows 2.10 rows at the very worst string
    /// in either locale and 2.70 typically, and the item description was promoted from
    /// <c>Type.Minimum</c> to <c>Type.Body</c> in the same move rather than in spite of it.
    ///
    /// Only one number moves between tabs: the top of <c>TabContent</c>. A wardrobe tab starts
    /// under the character stage at 224; Materiais takes the stage's band as well and starts at
    /// 84. Everything else — header, divider, tab bar — is anchored once and never rebuilt, which
    /// is what makes a tab change a swap rather than a rebuild of the sheet.
    ///
    /// ==================================================================================
    /// THE RULES THIS SCREEN CAN BREAK, AND HOW IT DOES NOT
    /// ==================================================================================
    /// * <b>Rule 7 — never punish.</b> A locked row is the largest, most-written row on the sheet:
    ///   the piece is drawn in full colour on the character's own body, its name is normal weight,
    ///   its description reads the same as everyone else's, and underneath it the condition is
    ///   spelled out whole in the brightest ink the row has. It is never dimmed, never disabled,
    ///   never a blank slot and never a scold. <see cref="Button.interactable"/> stays true on a
    ///   locked row on purpose: the Secondary variant draws a non-interactable control at 0.40
    ///   opacity, which is exactly the disabled-looking void this rule forbids. Tapping one answers
    ///   calmly and costs nothing.
    /// * <b>Rule 10 — never show progress toward anything.</b> Nothing on this sheet counts. No
    ///   "3 of 18", no fraction beside a condition, no bar, no "next unlock" teaser, and — the one
    ///   that had to be decided rather than inherited — <b>a tab carries a dot, never a numeral</b>.
    ///   Four counts on four adjacent tabs read as a scoreboard to clear even though each one is
    ///   individually harmless. The dot answers "is there anything in here I have not seen", which
    ///   is the whole question, and looking spends it.
    /// * <b>Rule 18 — nothing is bought with money.</b> This entry used to read "nothing is
    ///   bought: no price, no currency, no timer, no unlock now", and that is no longer true: a
    ///   locked row now carries a talent price. The rule it was protecting is intact, and the
    ///   distinction is the whole point. CLAUDE.md's rule 18 forbids <i>selling the shortcut</i> —
    ///   monetisation may be a new season or a cosmetic, never a resource, never a timer, never a
    ///   shortcut past the work. Talents are earned by turning up, spend only on cosmetics, and buy
    ///   no stone, no timber, no stage of wall and no hour of anyone's day. There is still no
    ///   timer and no "unlock now", and no real money touches this sheet.
    ///   <para>
    ///   <b>Not finished, and knowingly so.</b> The price is displayed; nothing spends it yet.
    ///   Until the purchase path lands, every locked row shows a number the player cannot pay,
    ///   which is in tension with rule 7 above — a locked row is supposed to be an invitation, and
    ///   an unpayable price is closer to a shopfront with the door locked. It is staged
    ///   deliberately rather than shipped as the finished design. The unlock sentence is still
    ///   present and still the brightest line on the row, so the route that <i>does</i> work is
    ///   still the one the row leads with.
    ///   </para>
    /// * <b>Rule 13 — the smell checklist.</b> No religious iconography anywhere in here; the only
    ///   sprites are the design system's lock, check and dot, and the character's own art layers.
    ///   The tab bar is also silent — see <see cref="BuildSegment"/>.
    ///
    /// ==================================================================================
    /// THE CONTENT GAPS THIS SCREEN DEGRADES AROUND
    /// ==================================================================================
    /// 1. <b>The base slot has no items and cannot have any.</b> <see cref="AppearanceState"/> has
    ///    five render layers and no base layer, and the catalogue authors none. There is no base
    ///    tab, and <see cref="Wardrobe.ItemsForSlot"/> answering <see cref="CharacterSlot.Base"/>
    ///    with an empty array is correct rather than a failure.
    /// 2. <b>Five preset item ids are not in the catalogue.</b> Anything that does not resolve is
    ///    skipped with a warning and never becomes a blank row.
    /// 3. <b>Four locked items draw the same art as items the player already owns.</b> That is why
    ///    a row carries a name, a description <i>and</i> a condition rather than a bare tile: two
    ///    identical thumbnails are still two clearly different items when each carries its own
    ///    sentence. A tile grid would have shipped four pairs the player cannot tell apart.
    /// 4. <b>tint_channels has nowhere to persist a swatch.</b> The save carries six ints and no
    ///    colour choice, so this screen offers no recolouring at all. Showing swatches that could
    ///    not be saved would be the worse failure — the player picks a colour, leaves, finds it
    ///    gone.
    /// 5. <b>Known, separately owned, and now more visible:</b> <c>GameState.equippedItems</c> is
    ///    not seeded and <c>CharacterPresets.DefaultEquipped</c> is not called, so a fresh save
    ///    shows no equipped ring anywhere and unequipping an accessory leaves its art on the body.
    ///    Both are the same gap — the save carries no character identity until character creation
    ///    migrates to presets — and the Detalhes tab is where it shows most, because that tab now
    ///    draws six full-height rows with none of them ringed. This redesign made it legible; it
    ///    did not cause it, and it is not fixed here.
    ///
    /// ==================================================================================
    /// COLOUR, AND THE TWO MEANINGS GOLD IS ALLOWED
    /// ==================================================================================
    /// <b>Gold carries two meanings here, and they are told apart by shape.</b> It used to carry
    /// exactly one — "not yet seen" — and the talent price added the second, so the rule is
    /// restated rather than quietly broken.
    /// <list type="number">
    /// <item><b>"Not yet seen"</b>, spent by the NOVO badge and the tab dot, both cleared by
    /// looking. Always a filled shape with no glyph in it.</item>
    /// <item><b>"A talent"</b>, spent by the coin on a locked row's price — the same coin the HUD
    /// and the Materiais tab use for the same thing. Always the coin glyph, always beside a
    /// number.</item>
    /// </list>
    /// The two never collide on one row: a locked item is never new (<see cref="Wardrobe.IsNew"/>
    /// refuses a badge on one), so a NOVO badge and a price cannot appear together, and the tab dot
    /// lives on the tab bar rather than in the list. What keeps this honest is that neither meaning
    /// is carried by gold <i>alone</i> — one is a bare fill, the other is a coin with a quantity.
    ///
    /// Why the single-meaning rule was worth having: before it, gold carried three unrelated jobs
    /// at once. Adding a second meaning is a real cost, paid here for the talent economy; a third
    /// would put the sheet back where it started.
    ///
    /// Equipped is therefore <b>clay</b>, not gold: the <c>Selected</c> ring and the check are
    /// <c>Brand.Primary</c>, so clay means "the current one" in both places it appears, the tab you
    /// are on and the piece you are wearing.
    /// The focus ring stays gold as the single accepted exception — design-system rule 5 fixes it
    /// globally across every variant, it is chrome rather than content, and touch never produces it.
    ///
    /// <c>Ink.Muted</c> appears nowhere on this sheet any more. It measures 3.57:1 on
    /// <c>Surface.Card</c> and 3.98:1 on <c>Surface.Panel</c> against a 4.5:1 requirement, and it
    /// was being used at <c>Type.Minimum</c> — the smallest text in the game — on the item
    /// description, the stage caption and the material labels. All of them moved to
    /// <c>Ink.Secondary</c>. <c>Ink.Faint</c> is not a fallback: at 2.40:1 it fails body text, large
    /// text and the 3:1 non-text floor at once, so it cannot even carry a border. There is no
    /// readable ink below <c>Ink.Secondary</c> on this palette, which is why a third text tier here
    /// comes from size and weight and never from a dimmer ink.
    ///
    /// ==================================================================================
    /// SURFACES
    /// ==================================================================================
    /// The sheet is <see cref="UIKit.CardStyle.Card"/> over the scrim <see cref="ModalRoot"/> lays,
    /// which is the design system's elevation 2. The figure and the material cards stand on nested
    /// <see cref="UIKit.CardStyle.Panel"/>s. <see cref="UIKit.CardStyle.Scroll"/> — the pergaminho,
    /// the project default for anything read at length — is deliberately not used: nothing here is
    /// read at length, and putting the wardrobe on a near-white reading surface would invert a
    /// screen whose subject is a character drawn in the game's own palette.
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

        // The accessible name of a row carries its state, and the punctuation that joins the two is
        // authored inside the locale string rather than concatenated here. "{0}, em uso" is one
        // translatable sentence; name + ", " + state is three fragments and a comma nobody can
        // translate. It also keeps the separator out of this file, which is the rule.
        /// <summary>Width of a study card's own button. Wide enough for one word in both locales.</summary>
        static readonly float StudyButtonWidth = DesignTokens.Px(120f);

        /// <summary>How a reader opened from a study card is recorded. Telemetry, so English.</summary>
        const string StudyTrigger = "profile_study";

        const string StateEquippedKey = "backpack.state.equipped";
        const string StateLockedKey = "backpack.state.locked";
        const string StateNewKey = "backpack.state.new";

        /// <summary>Label per content tab. Only the three wardrobe ones reach the lower bar.</summary>
        static readonly string[] TabLabelKeys =
        {
            "backpack.tab.profile",
            "slot.hair",
            "slot.outfit",
            "slot.accessory",
            "backpack.tab.materials"
        };

        /// <summary>Labels of the three materials, in the order the grid lists them.</summary>
        static readonly string[] MaterialLabelKeys =
        {
            "backpack.material.stone",
            "backpack.material.timber",
            "backpack.material.blocks",
            "backpack.material.talents"
        };

        // ------------------------------------------------------------------ two bars, two levels
        //
        // Three sections along the top — Perfil, Itens, Aparência — and Aparência opens three of its
        // own along the bottom: Cabelo, Roupa, Extras.
        //
        // <b>Why the levels sit at opposite ends.</b> The design system anchors a segmented row at
        // the bottom of its card and gives the reason: that is where the thumb is, and it is the
        // most-tapped control on the surface. That reason belongs to Cabelo/Roupa/Extras, tapped
        // over and over while dressing a character, so they keep the bottom. Choosing a section is
        // done once per visit, so it goes up top, where it reads as the sheet's own navigation.
        //
        // <b>Three cells per bar is also what made Aparência sayable.</b> Measured in Manrope Bold
        // at Type.Body against the 68.91 this project records for Materiais: at five cells a label
        // has 57.3 points and Personalização needs 112.8; at three it has 95.5 and Aparência needs
        // 74.6. Materiais and Detalhes did not survive the squeeze either, so they were re-authored
        // as Itens and Extras — the remedy docs/design-system.md prescribes, and the same one that
        // turned Acessórios into Detalhes before.

        const int TabIndexProfile = 0;
        const int TabIndexHair = 1;
        const int TabIndexOutfit = 2;
        const int TabIndexAccessory = 3;
        const int TabIndexMaterials = 4;
        const int TabCount = 5;

        const int SectionProfile = 0;
        const int SectionItems = 1;
        const int SectionAppearance = 2;
        const int SectionCount = 3;

        /// <summary>Which content tab each section opens. Aparência goes back to the last one used.</summary>
        static readonly int[] SectionLanding = { TabIndexProfile, TabIndexMaterials, TabIndexHair };

        static readonly string[] SectionLabelKeys =
        {
            "backpack.tab.profile",
            "backpack.tab.materials",
            "backpack.tab.appearance"
        };

        // The middle cell keeps the Materials names on purpose: it is still the control that opens
        // the materials list, and tools/e2e.sh drives this sheet by exactly these strings.
        static readonly string[] SectionObjectNames =
        {
            "SegmentProfile", "SegmentMaterials", "SegmentAppearance"
        };

        static readonly string[] SectionLabelObjectNames =
        {
            "SegmentLabelProfile", "SegmentLabelMaterials", "SegmentLabelAppearance"
        };

        static readonly string[] SectionFillObjectNames =
        {
            "SegmentFillProfile", "SegmentFillMaterials", "SegmentFillAppearance"
        };

        static readonly string[] SectionRimObjectNames =
        {
            "SegmentRimProfile", "SegmentRimMaterials", "SegmentRimAppearance"
        };

        /// <summary>Aparência carries the dot for the three lists folded inside it.</summary>
        static readonly string[] SectionBadgeObjectNames = { null, null, "SegmentBadgeAppearance" };

        /// <summary>
        /// The slot behind each tab. The fourth entry is <see cref="CharacterSlot.Base"/> and is
        /// never read: Materiais is not a slot, has no catalogue items and has no seen-state.
        ///
        /// This is the trap the container's name sets. "SlotSegments" holds a segment that is not a
        /// slot, and <see cref="IsWardrobeTab"/> is the only thing that decides which is which —
        /// never the name of the object, and never the array index by eye.
        /// </summary>
        static readonly CharacterSlot[] TabSlots = BuildTabSlots();

        /// <summary>
        /// The slot behind each tab, taken from <see cref="Wardrobe.BadgedSlots"/> rather than
        /// spelled again here. The three wardrobe tabs ARE the badged slots, in their order, and
        /// writing them out twice is how the HUD pill and the tab dots would come to disagree
        /// without anyone editing either on purpose.
        ///
        /// The Materiais tab has no wardrobe slot, so it parks on <see cref="CharacterSlot.Base"/>
        /// — the one slot the catalogue never draws — and every wardrobe-only path tests the tab
        /// index rather than reading this. If the badged set ever grows past the tabs that can show
        /// it, that is the stuck-pill bug in <see cref="Wardrobe.NewCount"/>'s comment arriving for
        /// real, so it is reported rather than silently truncated.
        /// </summary>
        static CharacterSlot[] BuildTabSlots()
        {
            IReadOnlyList<CharacterSlot> badged = Wardrobe.BadgedSlots;
            var slots = new CharacterSlot[TabCount];

            int wardrobeTabs = TabCount - 1;
            if (badged.Count != wardrobeTabs)
            {
                Debug.LogError("[BackpackPanel] Wardrobe.BadgedSlots has " + badged.Count +
                               " slot(s) but the sheet has " + wardrobeTabs + " wardrobe tab(s). A " +
                               "badged slot with no tab counts toward the HUD pill and can never be " +
                               "spent, so the player would carry a badge they cannot clear.");
            }

            for (int i = 0; i < wardrobeTabs; i++)
            {
                slots[i] = i < badged.Count ? badged[i] : CharacterSlot.Base;
            }

            slots[TabIndexMaterials] = CharacterSlot.Base;
            return slots;
        }

        // GameObject names, spelled out rather than concatenated from a suffix: these are the
        // handles tools/e2e.sh drives the sheet by, and a grep for one of them should land on the
        // line that creates it.

        static readonly string[] TabObjectNames = { "TabProfile", "TabHair", "TabOutfit", "TabAccessory", "TabMaterials" };

        static readonly string[] SegmentObjectNames =
        {
            null, "SegmentHair", "SegmentOutfit", "SegmentAccessory", null
        };

        static readonly string[] SegmentLabelObjectNames =
        {
            null, "SegmentLabelHair", "SegmentLabelOutfit", "SegmentLabelAccessory", null
        };

        static readonly string[] SegmentFillObjectNames =
        {
            null, "SegmentFillHair", "SegmentFillOutfit", "SegmentFillAccessory", null
        };

        static readonly string[] SegmentRimObjectNames =
        {
            null, "SegmentRimHair", "SegmentRimOutfit", "SegmentRimAccessory", null
        };

        /// <summary>Dot names. The fourth is null: Materiais never carries one.</summary>
        static readonly string[] SegmentBadgeObjectNames =
        {
            null, "SegmentBadgeHair", "SegmentBadgeOutfit", "SegmentBadgeAccessory", null
        };

        /// <summary>GameObject names of the three material readouts.</summary>
        static readonly string[] MaterialObjectNames = { "Material_stone", "Material_timber", "Material_blocks", "Material_talents" };

        // ------------------------------------------------------------------ metrics
        // Design points throughout, converted once. The sheet is a bottom sheet: a generous strip
        // of scrim is left above it so that "the scrim closes it too" is a real target and not a
        // twenty-point sliver nobody can hit.
        //
        // The whole vertical budget of the card is written out here and sums exactly, which is the
        // point of writing it out: a wardrobe tab is 20 + 48 + 16 + 124 + 16 + FLEX + 12 + 2 + 12 +
        // 48 + 20 = 750, and Materiais is the same sum without the stage and its gap. TabContent is
        // the only band that flexes, so a shorter device loses list rows and nothing else.

        static readonly float SheetTop = DesignTokens.Px(72f);
        static readonly float SheetPadding = DesignTokens.Space.S20;
        static readonly float HeaderHeight = DesignTokens.Space.TouchTarget;

        /// <summary>
        /// Width reserved in the header for the coin, its gap and four mono digits. Reserved rather
        /// than measured so the title's inset is a constant — see <see cref="BuildHeader"/>.
        /// </summary>
        static readonly float BalanceWidth =
            UIKit.IconSize + DesignTokens.Space.S4 + DesignTokens.Px(52f);
        static readonly float StageHeight = DesignTokens.Px(124f);
        static readonly float FigureBoxHeight = DesignTokens.Px(100f);
        static readonly float ThumbSize = DesignTokens.Px(56f);
        static readonly float ScrollbarWidth = DesignTokens.Space.S8;
        static readonly float SegmentBarHeight = DesignTokens.Space.TouchTarget;
        static readonly float DividerHeight = DesignTokens.Px(2f);
        static readonly float DotSize = DesignTokens.Px(8f);
        static readonly float MaterialCardHeight = DesignTokens.Px(120f);

        /// <summary>
        /// Left and right inset of the tab bar, and it is <see cref="DesignTokens.Space.S12"/>
        /// rather than <see cref="SheetPadding"/> for one measured reason.
        ///
        /// At the S20 inset the cells are 77 design points and the widest label — Materiais and
        /// Materials, both 68.91 — clears the cell edge by 4.05 points, one space character. At S12
        /// the cells are 81 and it clears by 6.05. That 6 points is the entire margin the label fit
        /// has, so the band is deliberately wider than the content column above it. The cost is
        /// that the band does not align with the header and the list, and it is paid for by drawing
        /// <c>TabDivider</c> at full card width: a rule the band sits under reads as a region
        /// boundary, while a rule narrower than the band reads as a mistake.
        /// </summary>
        static readonly float SegmentBarInset = DesignTokens.Space.S12;

        /// <summary>Top edge of the section bar, measured from the top of the sheet.</summary>
        static readonly float SectionBarTop = SheetPadding + HeaderHeight + DesignTokens.Space.S16;

        /// <summary>Top edge of the rule under the section bar.</summary>
        static readonly float SectionDividerTop =
            SectionBarTop + SegmentBarHeight + DesignTokens.Space.S12;

        /// <summary>
        /// Top of the content band on Perfil and Itens: under the section bar and its rule. Neither
        /// carries the character stage — one is a readout, the other a grid of counts.
        /// </summary>
        static readonly float SectionContentTop =
            SectionDividerTop + DividerHeight + DesignTokens.Space.S12;

        /// <summary>Top edge of the character stage. Only Aparência shows one.</summary>
        static readonly float StageTop = SectionContentTop;

        /// <summary>Top of the content band on a wardrobe tab: under the stage.</summary>
        static readonly float WardrobeContentTop = StageTop + StageHeight + DesignTokens.Space.S16;

        /// <summary>Bottom of the content band with no lower bar: the sheet's own padding.</summary>
        static readonly float PlainContentBottom = SheetPadding;

        /// <summary>Bottom of the content band: the tab bar, the divider and the gaps around them.</summary>
        static readonly float ContentBottom =
            SheetPadding + SegmentBarHeight + DesignTokens.Space.S12 + DividerHeight + DesignTokens.Space.S12;

        /// <summary>Bottom edge of the divider, measured from the bottom of the sheet.</summary>
        static readonly float DividerBottom = SheetPadding + SegmentBarHeight + DesignTokens.Space.S12;

        // ---------------------------------------------------------------- horizontal derivation
        // Every width in a row is a build-time constant derived from these, and that is what makes
        // clipping arithmetically impossible rather than merely unlikely: no Text is ever measured
        // against a width it will not have.
        //
        // ==================================================================================
        // WHY THESE ARE MEASURED AND NOT WRITTEN DOWN
        // ==================================================================================
        // They used to be `static readonly` off UIKit.ReferenceWidth, which reads as the safe
        // thing to do and is the one arrangement that cannot work. The canvas is 1080 units wide
        // ONLY on a device whose aspect is exactly 1080x1920. Everywhere else the scaler's
        // match-0.5 rule hands the layout a different number — an iPhone 17 Pro reports about 976
        // — and every width pinned to 1080 then overflows its parent by the difference. Measured
        // on that phone before this was fixed: the rows ran past the scroll viewport and clipped
        // the description mid-word ("num rabc"), and "Materiais" spilled off the card entirely.
        //
        // It survived tools/e2e.sh because the macOS player is launched at exactly 1080x1920,
        // where the reference and the truth agree. It is the layout bug e2e structurally cannot
        // see, which is why the phone pass exists.
        //
        // So the chain starts at UIKit.CanvasWidth() — clamped by the container the sheet is
        // actually parented into, in case the safe area is narrower still — and everything below
        // is derived from that, once, in RecomputeMetrics(). They are mutable statics rather than
        // instance fields because one sheet exists at a time and every read happens inside a
        // method Build() calls; the RATIOS between them are what the layout contract fixes, never
        // the absolute numbers.

        /// <summary>The card: the available width less a gutter on each side.</summary>
        static float CardWidth = UIKit.ReferenceWidth - 2f * DesignTokens.Space.Gutter;

        /// <summary>The card's content column: the card less its own padding.</summary>
        static float ContentWidth = CardWidth - 2f * SheetPadding;

        static float SegmentBarWidth = CardWidth - 2f * SegmentBarInset;
        /// <summary>Cell width. Both bars carry three cells, so one number serves both.</summary>
        static float SegmentWidth = SegmentBarWidth / SectionCount;

        /// <summary>Row width: the content column less the scrollbar's lane and its gap.</summary>
        static float RowWidth = ContentWidth - (ScrollbarWidth + DesignTokens.Space.S8);

        /// <summary>The text column of a row: everything the thumbnail and three gaps leave.</summary>
        static float TextColumnWidth = RowWidth - (3f * DesignTokens.Space.S12 + ThumbSize);

        /// <summary>
        /// A material card: half the content column, less the gutter between the two of them.
        /// The contract's 148 design points is what this evaluates to at the reference width; the
        /// division is what keeps the pair inside a narrower one.
        /// </summary>
        static float MaterialCardWidth = (ContentWidth - DesignTokens.Space.S12) / 2f;

        /// <summary>
        /// Re-derives every horizontal measurement from the width this device actually gives the
        /// sheet. Called once, first thing in <see cref="Build"/>, before anything is laid out.
        /// </summary>
        static void RecomputeMetrics(RectTransform container)
        {
            float available = UIKit.CanvasWidth();

            // A safe area with a real horizontal inset (a landscape orientation, a future device)
            // is narrower than the canvas; a canvas created this frame reports a rect of zero.
            // Taking the smaller of the two only when the container has a usable width covers both
            // without trusting either on its own.
            if (container != null && container.rect.width > 1f)
            {
                available = Mathf.Min(available, container.rect.width);
            }

            CardWidth = available - 2f * DesignTokens.Space.Gutter;
            ContentWidth = CardWidth - 2f * SheetPadding;
            SegmentBarWidth = CardWidth - 2f * SegmentBarInset;
            SegmentWidth = SegmentBarWidth / SectionCount;
            RowWidth = ContentWidth - (ScrollbarWidth + DesignTokens.Space.S8);
            TextColumnWidth = RowWidth - (3f * DesignTokens.Space.S12 + ThumbSize);
            MaterialCardWidth = (ContentWidth - DesignTokens.Space.S12) / 2f;
            StageTextWidth = ContentWidth -
                (DesignTokens.Space.S12 + FigureBoxWidth + DesignTokens.Space.S16 + DesignTokens.Space.S12);
        }

        /// <summary>
        /// The character sprites are 32x48, and both the big figure and every thumbnail are fitted
        /// to that ratio rather than given a size. Fitting is what keeps the figure as large as its
        /// box allows on a 1080-unit reference and on the ~977 units a phone actually reports.
        /// </summary>
        static readonly float FigureAspect =
            SheepGate.Art.CharacterArt.Width / (float)SheepGate.Art.CharacterArt.Height;

        /// <summary>Width of the figure's box on the stage, derived from its height and its ratio.</summary>
        static readonly float FigureBoxWidth = FigureBoxHeight * FigureAspect;

        /// <summary>
        /// The stage's text column: what the figure and the three gaps leave of the panel.
        /// Re-derived in <see cref="RecomputeMetrics"/> along with everything else it depends on.
        /// </summary>
        static float StageTextWidth = ContentWidth -
            (DesignTokens.Space.S12 + FigureBoxWidth + DesignTokens.Space.S16 + DesignTokens.Space.S12);

        // ---------------------------------------------------------------- line boxes
        // Reserved heights are computed from the type scale, never written as a float. A body line
        // is 15.17 design points of type in a 22.24-point line box, and the only honest way to say
        // that in code is size times leading.

        static readonly float BodyLineHeight = DesignTokens.Type.Body * DesignTokens.Type.BodyLeading;
        static readonly float MinimumLineHeight = DesignTokens.Type.Minimum * DesignTokens.Type.BodyLeading;
        static readonly float TitleLineHeight = DesignTokens.Type.Title * DesignTokens.Type.TitleLeading;

        /// <summary>The NOVO badge's outside height: one minimum line and its padding.</summary>
        static readonly float BadgeHeight = MinimumLineHeight + 2f * DesignTokens.Space.S4;

        /// <summary>
        /// The gap between the three things in a row's name line — the name, the NOVO badge and the
        /// status slot — and the badge's own side padding. Both are Space.S4 where the layout
        /// contract asks for Space.S8, and both were narrowed for the same measured reason.
        ///
        /// The contract sized the name box off a 200-point text column. A real phone gives that
        /// column 162.6 points, and after the permanent status slot and a NOVO badge at S8 gaps the
        /// name has 72.7 points left — less than the widest single word an item name contains.
        /// Legacy Text breaks a word that cannot fit, so "Túnica de carregador" rendered as
        /// "Túnica de / carregad / or" on an iPhone 17 Pro. Nothing clipped and no height was
        /// wrong; it was simply unreadable.
        ///
        /// Sixteen points come back at S4 — two gaps and two paddings — which puts the name box at
        /// 88.7 and clears the two longest words in the catalogue, "carregador" (77) and
        /// "ferramentas" (84). The badge is still a pill and still reads as one; what it loses is
        /// air it was spending on a column that does not have any. Both were left at S8 in the
        /// contract because the contract was measured against a card 38 points wider than any
        /// phone has.
        /// </summary>
        static readonly float NameRowSpacing = DesignTokens.Space.S4;

        static readonly float BadgeSidePadding = DesignTokens.Space.S4;

        /// <summary>
        /// Height reserved for the refusal line, whether or not it has anything in it.
        ///
        /// The longest refusal in either locale wraps to two lines in the stage's 201.33-point text
        /// column; three are reserved as headroom. That reserve is also an acceptance rule: no
        /// <c>backpack.refusal.*</c> string may exceed three lines at that width.
        /// </summary>
        static readonly float RefusalHeight = 3f * BodyLineHeight;

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

        /// <summary>Which tab is up. -1 until <see cref="Build"/> selects the first one.</summary>
        int _selected = -1;

        /// <summary>The column the study cards live in, so an answer can replace them in place.</summary>
        RectTransform _studies;

        /// <summary>The section cells along the top. No Scroll or Content: they open other tabs.</summary>
        TabView[] _sections;

        /// <summary>The lower bar and its rule, hidden whenever the section has nothing under it.</summary>
        RectTransform _slotBar;
        Image _slotDivider;

        /// <summary>
        /// Which wardrobe tab Aparência returns to. Remembering it is what makes the section bar a
        /// place to leave and come back to rather than a control that resets the player's work.
        /// </summary>
        int _lastWardrobe = TabIndexHair;

        readonly Image[] _layers = new Image[LayerCount];
        readonly List<RowView> _rows = new List<RowView>();

        TabView[] _tabs;
        RectTransform _tabContent;
        GameObject _stage;
        Text _refusal;
        CanvasGroup _refusalGroup;
        Coroutine _refusalFade;
        Text[] _materialCounts;
        Text _balanceCount;

        /// <summary>
        /// A look to draw when no run is in progress. Never written to the save — it exists so that
        /// a panel opened outside a game still draws a character instead of throwing.
        /// </summary>
        readonly AppearanceState _fallbackLook = new AppearanceState();

        /// <summary>
        /// One tab, and everything a repaint or a selection change needs from it.
        ///
        /// It carries no index of its own: the position in <see cref="_tabs"/> is the index, and a
        /// second copy of it is a field that can go stale against the array it describes. Nothing
        /// holds the segment Button either, for the same reason — nothing repaints it, and the four
        /// graphics that <i>are</i> repainted are the four below.
        /// </summary>
        sealed class TabView
        {
            /// <summary>The scroll view. Its GameObject <i>is</i> TabHair/TabOutfit/... .</summary>
            public ScrollRect Scroll;

            /// <summary>The column the rows or the grid live in.</summary>
            public RectTransform Content;

            /// <summary>The clay fill behind a selected label. Absent means unselected.</summary>
            public Image Fill;

            /// <summary>The 2px clay boundary of a selected cell.</summary>
            public Image Rim;

            /// <summary>The label, which changes weight and ink with selection.</summary>
            public Text Label;

            /// <summary>The gold "not yet seen" dot. Null on Materiais, which never carries one.</summary>
            public Image Dot;

            /// <summary>
            /// Whether this tab has been shown once. A layout rebuild and a scroll-to-top only work
            /// on an active object, so both are deferred to a tab's first activation rather than
            /// run at build time on three hidden panels.
            /// </summary>
            public bool Settled;
        }

        /// <summary>One row of a wardrobe tab, and the parts of it a repaint touches.</summary>
        sealed class RowView
        {
            /// <summary>Catalogue id. Also the suffix of the GameObject name.</summary>
            public string ItemId;

            /// <summary>Which slot it belongs to, so a repaint can ask what that slot is showing.</summary>
            public CharacterSlot Slot;

            /// <summary>The control itself, so the accessible name can be re-applied on a repaint.</summary>
            public GameObject Root;

            /// <summary>The clay ring that says this is the piece being worn.</summary>
            public Image Ring;

            /// <summary>
            /// Lock or check. A sprite, never a character — no bundled font carries either.
            ///
            /// <b>Toggled with <see cref="Behaviour.enabled"/> and never with SetActive.</b> The
            /// slot it occupies is width-permanent: a layout group drops an inactive child, the
            /// name label beside it would widen, a one-line name would rewrap to two, and the
            /// height pinned at build time would then clip. Turning the pixels off leaves the
            /// LayoutElement in place and the arithmetic intact.
            /// </summary>
            public Image Status;

            /// <summary>The name as the row displays it, before any state word is folded in.</summary>
            public string ItemName;

            /// <summary>True when the item was locked at build time. Unlocks never regress.</summary>
            public bool Locked;

            /// <summary>True when the badge was showing at build time. Captured, never recomputed.</summary>
            public bool IsNew;

            /// <summary>The unlock sentence, reused verbatim in the accessible name.</summary>
            public string UnlockSentence;
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
                Wardrobe.Changed -= OnWardrobeChanged;
                _subscribed = false;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ------------------------------------------------------------------ building

        /// <summary>
        /// Lays the whole sheet, then shows one tab.
        ///
        /// The order at the end is load-bearing and is the part four readings of this file will get
        /// wrong. Every row reads <see cref="Wardrobe.IsNew"/> while the badge is still there, so
        /// all four tabs are built <i>before</i> anything is marked seen; then Cabelo is selected,
        /// which is what spends that slot's badges; and only then does the panel subscribe to
        /// <see cref="Wardrobe.Changed"/>. Marking first would draw a sheet with no badges on it and
        /// throw away the one moment a badge exists to announce. Subscribing first would mean the
        /// first <c>MarkSlotSeen</c> repainted a sheet that <see cref="SelectTab"/> is about to
        /// repaint anyway.
        /// </summary>
        void Build(RectTransform container)
        {
            _instance = this;
            _state = TryGetState();

            // Before anything is laid out: this device's real width, not the reference one.
            RecomputeMetrics(container);

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
            BuildSectionBar(sheetRect);
            BuildStage(sheetRect);
            BuildDivider(sheetRect);
            BuildSegmentBar(sheetRect);
            BuildTabs(sheetRect);

            // Selects, paints and spends Cabelo's badges in one call, through exactly the same path
            // a later tap takes. The sheet a player opens and the sheet they see after a tab change
            // cannot drift apart, because there is only one method that produces either.
            SelectTab(TabIndexHair);

            Wardrobe.Changed += OnWardrobeChanged;
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

        /// <summary>
        /// The sheet's title, the talent balance, and the way out.
        ///
        /// <b>The balance is here because the prices are.</b> A locked row shows a coin and a
        /// number, and a price with no balance anywhere near it can be read as the balance — "you
        /// have 12" instead of "this costs 12". The Materiais tab does carry the count, but it is
        /// one tab away from every row that quotes a price, which is exactly when the reader cannot
        /// check. In the header it is on screen on all four tabs, beside the word that says whose
        /// pocket it is.
        ///
        /// It is not a score. Rule 10 bars progress toward something — a fraction, a bar, a "next
        /// unlock" — and a currency balance is a quantity the player holds, the same kind of number
        /// as the stone and timber counts this sheet has always shown.
        ///
        /// The width is reserved rather than fitted. A <see cref="ContentSizeFitter"/> here would
        /// make the title's right inset depend on a width that is zero for the first frame, and the
        /// title would jump once as the count arrived. Four mono digits is more talents than this
        /// economy can currently pay in a year of daily check-ins, so the reserve does not clip.
        /// </summary>
        void BuildHeader(RectTransform sheetRect)
        {
            RectTransform header = UIKit.CreateRect("Header", sheetRect);
            UIKit.AnchorTop(header, HeaderHeight, SheetPadding, SheetPadding, SheetPadding);

            Text title = UIKit.CreateText(header, "Title", Loc.T(TitleKey), DesignTokens.Type.Title,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft, DesignTokens.TypeRole.Title);
            UIKit.Stretch((RectTransform)title.transform, 0f,
                          DesignTokens.Space.TouchTarget + DesignTokens.Space.TouchGap
                              + BalanceWidth + DesignTokens.Space.TouchGap, 0f, 0f);

            BuildBalance(header);

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
        /// The coin and the count, sitting between the title and the close button. Same coin, same
        /// gold and same mono digits as every other talent figure in the game, so the number here
        /// and the number on a price are visibly the same currency.
        /// </summary>
        void BuildBalance(RectTransform header)
        {
            RectTransform balance = UIKit.CreateRect("Balance", header);
            UIKit.HorizontalGroup(balance.gameObject, DesignTokens.Space.S4, new RectOffset(),
                TextAnchor.MiddleRight);

            balance.anchorMin = new Vector2(1f, 0.5f);
            balance.anchorMax = new Vector2(1f, 0.5f);
            balance.pivot = new Vector2(1f, 0.5f);
            balance.sizeDelta = new Vector2(BalanceWidth, DesignTokens.Space.TouchTarget);
            balance.anchoredPosition =
                new Vector2(-(DesignTokens.Space.TouchTarget + DesignTokens.Space.TouchGap), 0f);

            UIKit.CreateIcon(balance, "Icon", UiSpriteKeys.IconCoin, DesignTokens.Brand.Secondary,
                UIKit.IconSize);

            _balanceCount = UIKit.CreateText(balance, "Count", string.Empty, DesignTokens.Type.Mono,
                DesignTokens.Brand.Secondary, TextAnchor.MiddleRight, DesignTokens.TypeRole.Mono);
            _balanceCount.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>
        /// The character, and beside them their name and the line that answers a refused tap.
        ///
        /// The stage went horizontal to pay for the list. A vertical stage — figure over caption —
        /// wanted 190 design points and gave the sheet 1.7 rows; a 100-point figure with a 201-point
        /// text column beside it wants 124 and gives it 2.7. The 66 points that buys are the whole
        /// reason this arrangement exists, and the text column is not filler: it is the only place
        /// on the sheet where a refusal can live without either covering the tab bar or reflowing
        /// the list.
        ///
        /// One facing is enough — this is a wardrobe, not a turntable, and the four-facing strip
        /// character creation uses is there to answer a different question.
        ///
        /// Hidden on Materiais, where there is nothing to dress and nothing to refuse.
        /// </summary>
        void BuildStage(RectTransform sheetRect)
        {
            Image stage = UIKit.CreateCard(sheetRect, "CharacterStage", UIKit.CardStyle.Panel);
            var stageRect = (RectTransform)stage.transform;
            UIKit.AnchorTop(stageRect, StageHeight, SheetPadding, SheetPadding, StageTop);
            stage.raycastTarget = false;
            _stage = stage.gameObject;

            RectTransform figureArea = UIKit.CreateRect("FigureArea", stageRect);
            figureArea.anchorMin = new Vector2(0f, 0.5f);
            figureArea.anchorMax = new Vector2(0f, 0.5f);
            figureArea.pivot = new Vector2(0f, 0.5f);
            figureArea.sizeDelta = new Vector2(FigureBoxWidth, FigureBoxHeight);
            figureArea.anchoredPosition = new Vector2(DesignTokens.Space.S12, 0f);

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

            RectTransform stageText = UIKit.CreateRect("StageText", stageRect);
            UIKit.Stretch(stageText);
            stageText.offsetMin = new Vector2(
                DesignTokens.Space.S12 + FigureBoxWidth + DesignTokens.Space.S16, DesignTokens.Space.S12);
            stageText.offsetMax = new Vector2(-DesignTokens.Space.S12, -DesignTokens.Space.S12);
            UIKit.VerticalGroup(stageText.gameObject, DesignTokens.Space.S8, new RectOffset());

            // The one string on this sheet that is allowed to be cut off, and it is the one the
            // player wrote themselves. A name long enough to wrap gets its first line; everything
            // else on the sheet is measured and pinned so that it cannot reach this state.
            Text playerName = UIKit.CreateText(stageText, "PlayerName", CharacterCatalog.PlayerName(_state),
                DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);
            playerName.verticalOverflow = VerticalWrapMode.Truncate;
            PinTextBox(playerName, StageTextWidth, BodyLineHeight);

            BuildRefusal(stageText);
        }

        /// <summary>
        /// The line that answers a tap the wardrobe could not carry out.
        ///
        /// It lives beside the figure rather than under the list, and that placement is the answer
        /// to three separate problems at once. It never covers the tab bar, because it is 500 points
        /// above it. It never reflows the list, because it is outside every ScrollRect and its
        /// height never changes. And it costs the vertical budget nothing, because it fills space
        /// beside a 66-point-wide figure in a 308-point-wide panel that would otherwise be empty —
        /// the alternative, a reserved band above the tab row, would have rebuilt exactly the dead
        /// strip this redesign exists to remove.
        ///
        /// It also sits under the thing whose non-change it explains: a refusal means the figure did
        /// not change, and the sentence saying so is next to the figure. The honest cost is that it
        /// is far from the finger that caused it. Accepted — a refusal is an explanation, not an
        /// acknowledgement of a tap.
        ///
        /// <c>Ink.Secondary</c> and never <c>Feedback.Error</c>. Two pieces that do not go together
        /// is information about the pieces, not a mistake the player made, and nothing was lost by
        /// trying. That is a tone rule, and on this palette it is also a legibility one:
        /// <c>Feedback.Error</c> measures 3.22:1 on <c>Surface.Card</c> and fails AA outright, so
        /// obeying rule 7 here costs nothing and gains a readable sentence.
        /// </summary>
        void BuildRefusal(RectTransform stageText)
        {
            _refusal = UIKit.CreateText(stageText, "RefusalMessage", string.Empty, DesignTokens.Type.Body,
                DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
            PinTextBox(_refusal, StageTextWidth, RefusalHeight);

            _refusalGroup = _refusal.gameObject.AddComponent<CanvasGroup>();
            _refusalGroup.alpha = 1f;
        }

        /// <summary>
        /// The rule above the tab bar, at full card width.
        ///
        /// Full width and not the content column's, because the bar under it is inset less than the
        /// content above it: a rule that spans the card reads as a region boundary, while a rule
        /// narrower than the band it introduces reads as a band that missed its margin.
        ///
        /// <c>Surface.Border</c> is not used and cannot be. It measures 1.04:1 against
        /// <c>Surface.Card</c> — a token whose name promises a hairline it is incapable of drawing.
        /// This is the kit's own Secondary-button border instead, parchment at 20%, which measures
        /// 1.80:1 and is visible.
        /// </summary>
        void BuildDivider(RectTransform sheetRect)
        {
            // A plain quad and not the panel sprite: the frames are nine-slice with rounded corners,
            // and a two-point-tall rounded rectangle draws as a dotted line.
            Image divider = UIKit.CreatePanel(sheetRect, "TabDivider",
                UIKit.WithAlpha(DesignTokens.Ink.Primary, 0.20f), null);
            UIKit.AnchorBottom((RectTransform)divider.transform, DividerHeight, 0f, 0f, DividerBottom);
            divider.raycastTarget = false;
            _slotDivider = divider;
        }

        /// <summary>
        /// The three sections, along the top: Perfil, Itens, Aparência.
        ///
        /// Anchored to the top rather than the bottom, which is a deliberate departure from the
        /// design system's placement rule and not an oversight. That rule's own reason — the thumb,
        /// and the most-tapped control — is what keeps the wardrobe's three cells at the bottom. A
        /// section is chosen once per visit, so up here it reads as the sheet's own navigation
        /// instead of competing for the same corner.
        /// </summary>
        void BuildSectionBar(RectTransform sheetRect)
        {
            RectTransform bar = UIKit.CreateRect("SectionSegments", sheetRect);
            UIKit.AnchorTop(bar, SegmentBarHeight, SegmentBarInset, SegmentBarInset, SectionBarTop);
            UIKit.HorizontalGroup(bar.gameObject, 0f, new RectOffset(), TextAnchor.MiddleCenter);

            _sections = new TabView[SectionCount];
            for (int i = 0; i < SectionCount; i++)
            {
                int captured = i;
                _sections[i] = BuildSegment(bar, i, SectionLabelKeys[i], SectionObjectNames,
                    SectionFillObjectNames, SectionRimObjectNames, SectionLabelObjectNames,
                    SectionBadgeObjectNames, () => SelectSection(captured));
            }

            Image divider = UIKit.CreatePanel(sheetRect, "SectionDivider",
                UIKit.WithAlpha(DesignTokens.Ink.Primary, 0.20f), null);
            UIKit.AnchorTop((RectTransform)divider.transform, DividerHeight, 0f, 0f, SectionDividerTop);
            divider.raycastTarget = false;
        }

        /// <summary>
        /// The three wardrobe slots, along the bottom, on screen only while Aparência is the
        /// section. Keeps the object names the rest of the project drives this bar by.
        ///
        /// The four tabs, as a segmented control along the bottom of the sheet card.
        ///
        /// <b>Bottom, inside the card.</b> A 750-point sheet puts a top-anchored row of tabs in the
        /// stretch zone, and this row is the most-tapped control on the screen. The two objections
        /// to a bottom-anchored row are both answered here: it is inside a modal card that already
        /// sits 21 points inside the screen on both sides, and it is text-only rather than the
        /// icon-over-label stack that is the bottom-navigation signature. It clears the home
        /// indicator by 42 points — 20 above the card's own edge, plus the card's 22-point inset.
        ///
        /// <b>The cells abut.</b> No <c>Space.TouchGap</c> between them, which is the one place on
        /// this sheet where the house 48/8 rule's second half is deliberately not applied. WCAG 2.2
        /// SC 2.5.8's spacing exception is written for targets under 24x24; these are 81x48 and
        /// clear the criterion outright, every vendor draws a segmented control as touching
        /// segments, and a gap would cost 6 points of label width per cell — which is the entire
        /// margin the label fit has.
        ///
        /// <b>No track behind the row.</b> Measured and rejected: parchment at 8% over
        /// <c>Surface.Card</c> is 1.24:1, <c>Surface.Panel</c> is 1.11:1, <c>Neutral.N800</c> is
        /// 1.20:1. On this palette nothing is simultaneously subtle and visible, so the row reads as
        /// one control from the full-width rule above it, four equal cells, and one filled cell.
        ///
        /// <b>No swipe between tabs.</b> Tap only. uGUI has no pager, the kit's scroll factory sets
        /// <c>horizontal = false</c>, the sheet already spends vertical drag on scrolling and on
        /// dismissal, and a bottom-placed row carries no swipe expectation to disappoint. The cost
        /// is that a power user cannot flick, which is small: an unsignposted gesture is never tried
        /// by anyone who was not told about it.
        /// </summary>
        void BuildSegmentBar(RectTransform sheetRect)
        {
            _slotBar = UIKit.CreateRect("SlotSegments", sheetRect);
            UIKit.AnchorBottom(_slotBar, SegmentBarHeight, SegmentBarInset, SegmentBarInset, SheetPadding);
            UIKit.HorizontalGroup(_slotBar.gameObject, 0f, new RectOffset(), TextAnchor.MiddleCenter);

            _tabs = new TabView[TabCount];
            for (int i = TabIndexHair; i <= TabIndexAccessory; i++)
            {
                int captured = i;
                _tabs[i] = BuildSegment(_slotBar, i, TabLabelKeys[i], SegmentObjectNames,
                    SegmentFillObjectNames, SegmentRimObjectNames, SegmentLabelObjectNames,
                    SegmentBadgeObjectNames, () => SelectTab(captured));
            }

            // Perfil and Itens have no cell of their own down here; their content still needs a
            // TabView to live in, so they get one with everything but the panel left null.
            _tabs[TabIndexProfile] = new TabView();
            _tabs[TabIndexMaterials] = new TabView();
        }

        /// <summary>
        /// One cell of the tab bar.
        ///
        /// <b>Three construction traps, all of them fatal if missed.</b>
        ///
        /// First, the handler is attached after the fact.
        /// <see cref="UIKit.CreateButton(Transform, string, string, UIKit.ButtonVariant, Action)"/>
        /// wraps every handler it is <i>given</i> in <c>AudioDirector.Play(AudioKeys.Confirm)</c>,
        /// so this passes null and calls <c>onClick.AddListener</c> itself. <b>The tab bar is
        /// silent.</b> Tab switching is the highest-frequency interaction on the sheet — a player
        /// comparing two hats crosses this row a dozen times — and a cue on every crossing stops
        /// being feedback and becomes noise. A row tap keeps the confirm cue, because an equip is a
        /// confirm. Nothing celebratory fires anywhere on this sheet.
        ///
        /// Second, the Ghost variant's fill is <c>Color.clear</c> with <c>raycastTarget = true</c>,
        /// and that Image is kept. An unselected cell with no graphic is invisible to a finger and
        /// invisible to the e2e runner, which raycasts at an object's centre.
        ///
        /// Third, the kit's own Label, Border and fill are never recoloured for the selected state.
        /// <see cref="VariantButton"/> owns those and repaints them on the next pointer event, so a
        /// tint applied from here would survive exactly until the finger moved. The selected look is
        /// drawn as this method's own children instead.
        ///
        /// <b>The selected state is three channels, two of them not colour:</b> a fill that is there
        /// or is not, a label weight of 700 or 400, and an ink of <c>Ink.OnPrimary</c> or
        /// <c>Ink.Secondary</c>. Colour is never alone.
        ///
        /// <b>Why the fill is two tokens.</b> Neither clay token satisfies both criteria on its own.
        /// <c>Ink.OnPrimary</c> on <c>Brand.Primary</c> is 3.82:1 and fails SC 1.4.3, because a
        /// 15.17-point bold label is normal text and the bold large-text threshold is 18.5px.
        /// <c>Brand.PrimaryDark</c> on <c>Surface.Card</c> is 2.56:1 and fails SC 1.4.11 for a state
        /// indicator. Solving for a single fill needs a relative luminance in [0.14965, 0.16964] and
        /// no Brand token lands there — Primary is 0.20889, PrimaryDark 0.12062. So the fill carries
        /// the label (5.79:1, AA) and the rim carries the boundary (3.89:1, SC 1.4.11).
        ///
        /// <b>Escalate upward, do not fix here:</b> <c>Ink.OnPrimary</c> on <c>Brand.Primary</c>
        /// failing means every Primary-variant button in the game has a failing label. The blast
        /// radius is <see cref="UIKit.SkinFor"/>, not this sheet.
        ///
        /// <b>One cell of either bar.</b> The names, the label and what a tap does arrive as arguments
        /// rather than being looked up from the index, because the two bars index different things:
        /// the lower one by content tab, the upper one by section.
        /// </summary>
        TabView BuildSegment(RectTransform bar, int index, string labelKey, string[] objectNames,
                             string[] fillNames, string[] rimNames, string[] labelNames,
                             string[] badgeNames, Action onSelect)
        {
            string cellLabel = Loc.T(labelKey);

            Button cell = UIKit.CreateButton(bar, objectNames[index], string.Empty,
                UIKit.ButtonVariant.Ghost, null);
            cell.onClick.AddListener(() => onSelect());
            AccessibleLabel.Apply(cell.gameObject, cellLabel);

            var cellRect = (RectTransform)cell.transform;

            // Fixed widths, never content-driven. The selected label changes weight, and cells that
            // measured themselves would re-lay the whole row on every tap.
            LayoutElement cellLayout = UIKit.Layout(cell);
            cellLayout.minWidth = SegmentWidth;
            cellLayout.preferredWidth = SegmentWidth;
            cellLayout.flexibleWidth = 0f;
            cellLayout.minHeight = SegmentBarHeight;
            cellLayout.preferredHeight = SegmentBarHeight;
            cellLayout.flexibleHeight = 0f;

            Image fill = UIKit.CreatePanel(cellRect, fillNames[index],
                DesignTokens.Brand.PrimaryDark, UiSpriteKeys.FrameMd);
            UIKit.Stretch((RectTransform)fill.transform);
            fill.raycastTarget = false;
            UIKit.Layout(fill).ignoreLayout = true;
            fill.gameObject.SetActive(false);

            // The cell's own rect, with no outset: the focus ring sits four points further out, and
            // that gap is what keeps "selected" and "focused" from reading as the same thing.
            Image rim = UIKit.CreatePanel(cellRect, rimNames[index],
                DesignTokens.Brand.Primary, UiSpriteKeys.FocusRing);
            UIKit.Stretch((RectTransform)rim.transform);
            rim.raycastTarget = false;
            UIKit.Layout(rim).ignoreLayout = true;
            rim.gameObject.SetActive(false);

            Text label = UIKit.CreateText(cellRect, labelNames[index], cellLabel,
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.MiddleCenter,
                DesignTokens.TypeRole.Body);

            // A one-line rect, centred vertically, and flush to the cell rather than inset. The
            // height matters: in a full-height rect a two-line label would fit inside the cell and
            // clip silently, whereas in a one-line rect it spills below the cell into the sheet's
            // bottom padding, where the daily e2e screenshot catches it in both locales.
            // Over-limit fails loudly and never ellipsises. The remedy is to re-author the word as
            // a real word — not to shrink the type, not to make the cells unequal, and not to add
            // a fifth tab.
            //
            // The layout contract asks for a Space.S4 inset either side, and this deliberately does
            // not apply it. The contract sized the cell at 81 design points off a 348-point card;
            // a real phone gives the card 310 points and the cell 71.6, and 8 points of inset then
            // leaves a 63.6-point box for a label whose widest word — Materiais / Materials — is
            // 68.9 points bold. Flush, the box is 71.6 and the worst label clears it by 2.7. The
            // labels are centred and every other word is far shorter, so the inset was buying
            // nothing that the centring does not already give.
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(1f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(0f, -0.5f * BodyLineHeight);
            labelRect.offsetMax = new Vector2(0f, 0.5f * BodyLineHeight);
            UIKit.Layout(label).ignoreLayout = true;

            // A cell carries a dot when its own table names one. That is what lets Aparência carry
            // the dot for the three lists inside it while Perfil and Itens carry none, without this
            // method knowing which bar it is building.
            Image dot = null;
            if (badgeNames != null && index < badgeNames.Length && !string.IsNullOrEmpty(badgeNames[index]))
            {
                // Top-right, and lifted clear of the label rather than merely tucked into the
                // corner. The dot occupies x 65..73 and y 4..12 inside an 81x48 cell; the widest
                // label's ink runs 6.05..74.95 and its line box 12.88..35.12, so without this
                // offset the dot would sit on top of "Materiais". The offset is the whole point.
                dot = UIKit.CreateIcon(cellRect, badgeNames[index], UiSpriteKeys.IconDot,
                    DesignTokens.Brand.Secondary, DotSize);
                var dotRect = (RectTransform)dot.transform;
                dotRect.anchorMin = new Vector2(1f, 1f);
                dotRect.anchorMax = new Vector2(1f, 1f);
                dotRect.pivot = new Vector2(1f, 1f);
                dotRect.anchoredPosition = new Vector2(-DesignTokens.Space.S8, -DesignTokens.Space.S4);
                dot.raycastTarget = false;
                UIKit.Layout(dot).ignoreLayout = true;
                dot.enabled = false;
            }

            // The kit parented the focus ring before these children existed, so it would otherwise
            // draw underneath the clay fill. Rule 5 fixes the ring globally; this only fixes where
            // it lands in the stack.
            Transform focusRing = cellRect.Find("FocusRing");
            if (focusRing != null)
            {
                focusRing.SetAsLastSibling();
            }

            // The pressed state of a selected cell. VariantButton repaints only the graphics it was
            // bound to, and the clay fill is not one of them, so the press has to be carried here.
            var press = cell.gameObject.AddComponent<SegmentPressTint>();
            press.Bind(fill, DesignTokens.Brand.PrimaryDark, DesignTokens.Brand.Primary);

            return new TabView
            {
                Fill = fill,
                Rim = rim,
                Label = label,
                Dot = dot
            };
        }

        /// <summary>
        /// The content band, and the four panels that share it.
        ///
        /// <c>TabContent</c> is one rect that all four tabs stretch to, and its top anchor is the
        /// only thing <see cref="SelectTab"/> moves. Each tab is its own <see cref="ScrollRect"/>,
        /// which is what gives each of them its own scroll offset for as long as the sheet is open.
        /// </summary>
        void BuildTabs(RectTransform sheetRect)
        {
            _tabContent = UIKit.CreateRect("TabContent", sheetRect);
            UIKit.Stretch(_tabContent, SheetPadding, SheetPadding, WardrobeContentTop, ContentBottom);

            for (int i = 0; i < TabCount; i++)
            {
                RectTransform content;
                ScrollRect scroll = UIKit.CreateScrollView(_tabContent, TabObjectNames[i], out content);
                UIKit.Stretch((RectTransform)scroll.transform);

                // The kit's default column padding is measured for a full-bleed screen; this one
                // already sits inside the sheet's own padding, so the only reservation left to make
                // is the scrollbar's lane — and Materiais has no scrollbar, so it makes none.
                var column = content.GetComponent<VerticalLayoutGroup>();
                if (column != null)
                {
                    column.spacing = DesignTokens.Space.S12;
                    column.padding = new RectOffset(
                        0,
                        i != TabIndexMaterials ? Mathf.RoundToInt(ScrollbarWidth + DesignTokens.Space.S8) : 0,
                        0,
                        Mathf.RoundToInt(DesignTokens.Space.S24));
                }

                _tabs[i].Scroll = scroll;
                _tabs[i].Content = content;

                if (IsWardrobeTab(i))
                {
                    UIKit.AttachVerticalScrollbar(scroll, ScrollbarWidth);
                    BuildWardrobeTab(content, TabSlots[i]);
                }
                else if (i == TabIndexProfile)
                {
                    UIKit.AttachVerticalScrollbar(scroll, ScrollbarWidth);
                    BuildProfileTab(content);
                }
                else
                {
                    BuildMaterialsTab(content);
                }

                scroll.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// One slot's rows, locked ones included, as one full-width scrolling column.
        ///
        /// No section heading — the tab is the heading — and no two-column grid, because a locked
        /// row has to print a whole sentence and rule 7 outranks any grid.
        ///
        /// A tab that draws nothing still exists, is still enabled, and says so in a sentence. The
        /// old behaviour — return false, destroy the section, leave the sheet a heading shorter —
        /// is deliberately not ported: a tab row that changes shape with save state is both the
        /// instability Apple warns about and a quiet punishment for not having found anything yet.
        /// </summary>
        void BuildWardrobeTab(RectTransform content, CharacterSlot slot)
        {
            CatalogItemDef[] items = Wardrobe.ItemsForSlot(slot);
            int drawn = 0;

            for (int i = 0; items != null && i < items.Length; i++)
            {
                if (BuildRow(content, items[i], slot))
                {
                    drawn++;
                }
            }

            if (drawn == 0)
            {
                BuildEmpty(content, slot);
            }
        }

        /// <summary>
        /// What a tab says when its slot draws nothing. Plainly, and without blame: nothing the
        /// player did caused it, and nothing of theirs was lost.
        /// </summary>
        void BuildEmpty(RectTransform content, CharacterSlot slot)
        {
            Debug.LogWarning("[BackpackPanel] Slot " + slot + " drew no rows. Either character_catalog.json " +
                             "has no items for it, or every one of them failed to resolve.");

            Text empty = UIKit.CreateText(content, "Empty", Loc.T(UnavailableKey), DesignTokens.Type.Body,
                DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
            PinText(empty, RowWidth);
        }

        /// <summary>
        /// One item, as a full-width row: the piece drawn on the character's own body, its name,
        /// its description, and — when it has not opened yet — a padlock and the condition in full.
        ///
        /// ==================================================================================
        /// WHY THIS ROW CANNOT CLIP
        /// ==================================================================================
        /// Not "is unlikely to". Cannot. The mechanism is that <b>every width in the chain is a
        /// build-time constant</b>, so no <see cref="Text"/> is ever measured against a width it
        /// will not have, and every height is then pinned from that measurement:
        /// <list type="number">
        /// <item>the row is <see cref="RowWidth"/>, derived from the card, not from the design frame;</item>
        /// <item>the text column is <see cref="TextColumnWidth"/>, with <c>flexibleWidth = 0</c>;</item>
        /// <item>the status slot is width-permanent, so the name box never widens;</item>
        /// <item>each label is given its box, asked for its height, and pinned to both.</item>
        /// </list>
        /// There is no <see cref="ContentSizeFitter"/> anywhere in a row and no width is flexible,
        /// so nothing can renegotiate afterwards. The row's own height is then composed in code —
        /// <c>max(48, 24 + max(thumb, column))</c> — rather than left to a layout group to discover.
        ///
        /// The worst real string in the catalogue is the English <c>acc_tool_bag</c> while locked:
        /// a one-line name, a three-line description and a three-line condition, 205.30 design
        /// points of row. The tallest pt-BR row is 183.06. Both fit, and the formula covers the
        /// two-line-name case as well — "Bolsa de ferramentas" is 156.2 points wide, which is one
        /// line in the 168.17-point locked box and two in the 110.11-point badged one.
        ///
        /// Built on <see cref="UIKit.CreateButton"/> so the row is a real design-system control with
        /// the focus ring, the hover and pressed fills and the hairline border that come with it,
        /// and then re-laid as a horizontal group. The pieces the kit already parented are marked
        /// <c>ignoreLayout</c> first: the border and the focus ring are stretched to the whole
        /// control and would otherwise be laid out as two more columns of the row.
        /// </summary>
        bool BuildRow(RectTransform content, CatalogItemDef item, CharacterSlot slot)
        {
            if (item == null || string.IsNullOrEmpty(item.id))
            {
                Debug.LogWarning("[BackpackPanel] An item in slot " + slot + " has no id and was skipped.");
                return false;
            }

            string itemId = item.id;
            bool locked = !Wardrobe.IsUnlocked(_state, itemId);

            // Read while the badge is still there to read. Every row in every tab is built before
            // the first MarkSlotSeen runs, and this bool is then never recomputed: the badges on the
            // tab the player is looking at survive the whole open, while the tab's dot clears the
            // instant it is looked at. That asymmetry is the design — a dot that survives being
            // looked at is the nagging version.
            bool isNew = Wardrobe.IsNew(_state, itemId);

            // Resolved once and passed down: a missing name is an error in the log, and one row
            // should account for one line of it, not two.
            string itemName = DisplayName(item);

            // UnlockEvaluator.Sentence returns the finished sentence — resolved against catalog.json's
            // "unlock" table with the threshold and the plural already applied — so it never passes
            // through Loc, and the accessible name reuses this exact string rather than rephrasing it.
            string unlockSentence = locked ? UnlockEvaluator.Sentence(item.unlock_condition) : null;

            // No label text: the row builds its own beside a thumbnail, and a centred label
            // stretched across the whole control has nowhere to go in that arrangement. The kit's
            // confirm cue is kept here on purpose — an equip is a confirm; it is only the tab bar
            // that is silent.
            Button chip = UIKit.CreateButton(content, "Chip_" + itemId, string.Empty,
                UIKit.ButtonVariant.Secondary, () => OnRowTapped(itemId, slot));
            var chipRect = (RectTransform)chip.transform;

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

            BuildThumb(chipRect, item);

            RectTransform textColumn = UIKit.CreateRect("Text", chipRect);
            UIKit.VerticalGroup(textColumn.gameObject, DesignTokens.Space.S12, new RectOffset());
            LayoutElement textLayout = UIKit.Layout(textColumn);
            textLayout.minWidth = TextColumnWidth;
            textLayout.preferredWidth = TextColumnWidth;
            textLayout.flexibleWidth = 0f;

            Image status;
            float nameRowHeight = BuildNameRow(textColumn, itemName, isNew, out status);
            float columnHeight = nameRowHeight;

            string description = item.description;
            if (!string.IsNullOrWhiteSpace(description))
            {
                // Type.Body and Ink.Secondary, and both halves of that are a change from the old
                // sheet. This is the string that tells four visually identical locked items apart,
                // so it is prose rather than a label, and 12 points is a floor for labels. The old
                // Ink.Muted measured 3.57:1 against a 4.5:1 requirement — the sheet's worst
                // legibility failure, on its most load-bearing sentence, at the smallest size in the
                // game. The cost of the promotion is 79 to 156 points of column height per tab, and
                // it is paid knowingly.
                Text descriptionText = UIKit.CreateText(textColumn, "Description", description,
                    DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft,
                    DesignTokens.TypeRole.Body);
                columnHeight += DesignTokens.Space.S12 + PinText(descriptionText, TextColumnWidth);
            }

            if (locked && !string.IsNullOrEmpty(unlockSentence))
            {
                // Ink.Primary, which makes this the brightest line in a locked row, on purpose.
                // Rule 7 makes a locked row an invitation, and the sentence that says how to open it
                // is the invitation. The ink step from Secondary to Primary is also what separates
                // the condition from the description without spending vertical air — WCAG 1.4.8's
                // AAA paragraph gap here would be 33.4 points against a column spacing of 12, so the
                // separation is carried by ink and by the padlock in the name row instead.
                Text unlock = UIKit.CreateText(textColumn, "Unlock", unlockSentence,
                    DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                    DesignTokens.TypeRole.Body);
                columnHeight += DesignTokens.Space.S12 + PinText(unlock, TextColumnWidth);
            }

            if (locked)
            {
                // Under the unlock sentence, not instead of it. The sentence is still the row's
                // invitation and still its brightest line; the price is a second route that does
                // not yet exist, so it must not displace the one that does.
                columnHeight += DesignTokens.Space.S12
                    + BuildPriceRow(textColumn, TalentPrice.For(itemId));
            }

            textLayout.minHeight = columnHeight;
            textLayout.preferredHeight = columnHeight;
            textLayout.flexibleHeight = 0f;

            float rowHeight = Mathf.Max(UIKit.ButtonMinHeight,
                2f * DesignTokens.Space.S12 + Mathf.Max(ThumbSize, columnHeight));

            LayoutElement chipLayout = UIKit.Layout(chip);
            chipLayout.minHeight = rowHeight;
            chipLayout.preferredHeight = rowHeight;
            chipLayout.flexibleHeight = 0f;

            // Drawn over the row rather than by recolouring the kit's border: VariantButton owns
            // that border's colour and repaints it on the next pointer event, so a tint applied from
            // here would survive exactly until the finger moved. Clay and not gold — gold on this
            // sheet means "not yet seen" and nothing else, so "the one you are wearing" is the same
            // clay as "the tab you are on".
            Image ring = UIKit.CreatePanel(chipRect, "Selected", DesignTokens.Brand.Primary,
                UiSpriteKeys.FocusRing);
            UIKit.Stretch((RectTransform)ring.transform);
            ring.raycastTarget = false;
            ring.gameObject.SetActive(false);
            UIKit.Layout(ring).ignoreLayout = true;

            var row = new RowView
            {
                ItemId = itemId,
                Slot = slot,
                Root = chip.gameObject,
                Ring = ring,
                Status = status,
                ItemName = itemName,
                Locked = locked,
                IsNew = isNew,
                UnlockSentence = unlockSentence
            };

            AccessibleLabel.Apply(row.Root, AccessibleNameFor(row, false));
            _rows.Add(row);
            return true;
        }

        /// <summary>
        /// The name, the badge when the piece is new, and the one status icon the row can carry.
        /// Returns the row's height and hands back the status image, so a repaint can swap its
        /// sprite without walking the hierarchy.
        ///
        /// <b>The status slot is width-permanent.</b> It is built whether or not it has anything to
        /// show, and it is hidden with <see cref="Behaviour.enabled"/> rather than by deactivating
        /// it. A layout group drops an inactive child; the name label would then widen by 31.83
        /// points, a name that was one line could rewrap to two, and the height pinned a moment ago
        /// would clip. This is the exact failure the whole pinning policy exists to prevent, and it
        /// would only appear on the rows nobody is wearing.
        ///
        /// The badge does not need the same treatment: <see cref="Wardrobe.IsNew"/> is captured once
        /// at build time and cannot change while the sheet is open.
        ///
        /// The badge's width is measured rather than assumed. NOVO and NEW are different lengths and
        /// a third locale would be a third length, so the name box is
        /// <c>200 − (icon + 8) − (badge + 8)</c> computed from the badge that was actually built.
        /// </summary>
        /// <summary>
        /// The talent price on a locked row: the coin, then the number, in gold.
        ///
        /// The same <see cref="UiSpriteKeys.IconCoin"/> the HUD spends for a talent balance, on
        /// purpose — a second coin glyph invented for prices would read as a second currency. It
        /// carries no caption for the reason <c>BuildTalentsReadout</c> gives: a coin beside a
        /// number is legible as money without one.
        ///
        /// <b>The known risk, recorded rather than designed away.</b> A coin and a number on a row
        /// can be misread as "you have 12" instead of "this costs 12", and the sheet has no balance
        /// beside it to settle the question — the balance lives on the Materiais tab and in the
        /// drawer. The word that would remove the ambiguity was left off because the brief asked
        /// for the icon and the value; if playtesting shows the misread, a label is the fix, not a
        /// different glyph.
        ///
        /// Mono for the number, like every other quantity in the game, so the digits are tabular
        /// and a two-digit price does not shuffle the row against a one-digit one.
        /// </summary>
        static float BuildPriceRow(RectTransform textColumn, int price)
        {
            RectTransform row = UIKit.CreateRect("Price", textColumn);
            UIKit.HorizontalGroup(row.gameObject, NameRowSpacing, new RectOffset(),
                TextAnchor.MiddleLeft);

            UIKit.CreateIcon(row, "Icon", UiSpriteKeys.IconCoin, DesignTokens.Brand.Secondary,
                UIKit.IconSize);

            Text amount = UIKit.CreateText(row, "Amount", price.ToString(CultureInfo.InvariantCulture),
                DesignTokens.Type.Mono, DesignTokens.Brand.Secondary, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Mono);

            float amountHeight = PinText(amount, TextColumnWidth - (UIKit.IconSize + NameRowSpacing));
            float height = Mathf.Max(amountHeight, UIKit.IconSize);

            LayoutElement rowLayout = UIKit.Layout(row);
            rowLayout.minWidth = TextColumnWidth;
            rowLayout.preferredWidth = TextColumnWidth;
            rowLayout.flexibleWidth = 0f;
            rowLayout.minHeight = height;
            rowLayout.preferredHeight = height;
            rowLayout.flexibleHeight = 0f;

            return height;
        }

        static float BuildNameRow(RectTransform textColumn, string itemName, bool isNew, out Image status)
        {
            RectTransform row = UIKit.CreateRect("Name", textColumn);
            UIKit.HorizontalGroup(row.gameObject, NameRowSpacing, new RectOffset(),
                TextAnchor.MiddleLeft);

            Text label = UIKit.CreateText(row, "Label", itemName, DesignTokens.Type.Body,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);

            float badgeWidth = 0f;
            if (isNew)
            {
                badgeWidth = BuildNewBadge(row);
            }

            // Gold when it marks what is new; clay when it marks what is worn; the quieter secondary
            // ink under the padlock, because a lock is neither new nor current and gold would say it
            // was. The sprite and the colour are both set in Refresh.
            status = UIKit.CreateIcon(row, "Status", UiSpriteKeys.IconCheck,
                DesignTokens.Brand.Primary, UIKit.IconSize);
            status.enabled = false;

            float nameBox = TextColumnWidth - (UIKit.IconSize + NameRowSpacing);
            if (isNew)
            {
                nameBox -= badgeWidth + NameRowSpacing;
            }

            float labelHeight = PinText(label, nameBox);

            float height = Mathf.Max(labelHeight, UIKit.IconSize);
            if (isNew)
            {
                height = Mathf.Max(height, BadgeHeight);
            }

            LayoutElement rowLayout = UIKit.Layout(row);
            rowLayout.minWidth = TextColumnWidth;
            rowLayout.preferredWidth = TextColumnWidth;
            rowLayout.flexibleWidth = 0f;
            rowLayout.minHeight = height;
            rowLayout.preferredHeight = height;
            rowLayout.flexibleHeight = 0f;

            return height;
        }

        /// <summary>
        /// The NOVO badge, and the width it took.
        ///
        /// Gold, because on this sheet gold means "not yet seen" and that is this badge's whole job
        /// — the same meaning the tab dot carries, and the only meaning gold is allowed here. It is
        /// not a call to action and does not spend the one gold CTA a screen is allowed; this sheet
        /// has no Quest button at all, because it has no single action to nominate.
        ///
        /// Never on a locked item. <see cref="Wardrobe.IsNew"/> already refuses one, and that is
        /// what keeps the announcement for the day the item actually opens — so a badge and a
        /// padlock can never appear in the same row.
        /// </summary>
        static float BuildNewBadge(RectTransform row)
        {
            Image badge = UIKit.CreatePanel(row, "New", DesignTokens.Brand.Secondary, UiSpriteKeys.FrameSm);
            badge.raycastTarget = false;

            var badgeRect = (RectTransform)badge.transform;
            UIKit.HorizontalGroup(badge.gameObject, 0f, new RectOffset(
                Mathf.RoundToInt(BadgeSidePadding),
                Mathf.RoundToInt(BadgeSidePadding),
                Mathf.RoundToInt(DesignTokens.Space.S4),
                Mathf.RoundToInt(DesignTokens.Space.S4)), TextAnchor.MiddleCenter);

            Text text = UIKit.CreateText(badgeRect, "Label", Loc.T(NewBadgeKey), DesignTokens.Type.Minimum,
                DesignTokens.Ink.OnSecondary, TextAnchor.MiddleCenter, DesignTokens.TypeRole.BodyStrong);

            // Measured, not assumed: the word is a different length in every locale, and the name
            // box beside it is derived from whatever this comes back as.
            float textWidth = text.preferredWidth;
            PinTextBox(text, textWidth, MinimumLineHeight);

            float badgeWidth = textWidth + 2f * BadgeSidePadding;
            LayoutElement badgeLayout = UIKit.Layout(badge);
            badgeLayout.minWidth = badgeWidth;
            badgeLayout.preferredWidth = badgeWidth;
            badgeLayout.flexibleWidth = 0f;
            badgeLayout.minHeight = BadgeHeight;
            badgeLayout.preferredHeight = BadgeHeight;
            badgeLayout.flexibleHeight = 0f;

            return badgeWidth;
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
            thumbLayout.flexibleHeight = 0f;

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
        /// Perfil: how far the run has come, what else there is to do, and what is worth reading
        /// next. Three stacked sections in one scrolling column.
        ///
        /// <b>The meter obeys design system rule 1</b> — label, bar and fraction, never a bare bar.
        /// It does not break rule 10 either, and the difference is worth being precise about: rule
        /// 10 forbids showing progress toward a <i>vocation</i>, because a player who can see how
        /// many actions are left turns discovery into a task list. This bar names no archetype, is
        /// read by nothing, and buys nothing.
        ///
        /// <b>The studies never print a reference.</b> Each card is an authored line about something
        /// the player did; chapter and verse is <c>ScriptureVisibility</c>'s to reveal, exactly as
        /// for every citation in the game (rule 12) — and the button that opens the whole chapter is
        /// on every card from the first minute.
        ///
        /// Coins are not here yet. Talents exist in the save now, but paying a mission out of a
        /// balance another branch owns is that branch's call to make.
        /// </summary>
        void BuildProfileTab(RectTransform content)
        {
            GameState state = _state;

            BuildProfileMeter(content, state);
            BuildProfileSection(content, "MissionsHeading", Loc.T("profile.missions"), null);

            IReadOnlyList<ExtraMission> missions = StudyDesk.MissionsFor(state);
            for (int i = 0; i < missions.Count; i++)
            {
                BuildProfileCard(content, "Mission_" + missions[i].Id,
                    Loc.T(missions[i].TitleKey), Loc.T(missions[i].LineKey), null);
            }

            BuildProfileSection(content, "StudiesHeading", Loc.T("profile.studies"),
                Loc.T("profile.studies.hint"));

            // The authored suggestions go up straight away, and the endpoint — when one is
            // configured — replaces them if it answers. Drawing the offline list first is what
            // keeps this from ever being a spinner: the screen is complete before the request
            // leaves, and a player whose server is down never learns that one exists.
            _studies = UIKit.CreateRect("Studies", content);
            UIKit.VerticalGroup(_studies.gameObject, DesignTokens.Space.S12, new RectOffset());

            FillStudies(StudyDesk.SuggestFor(state));
            RequestStudies(state);
        }

        /// <summary>Draws a list of studies into the studies column, replacing whatever was there.</summary>
        void FillStudies(IReadOnlyList<Study> studies)
        {
            if (_studies == null)
            {
                return;
            }

            for (int i = _studies.childCount - 1; i >= 0; i--)
            {
                Destroy(_studies.GetChild(i).gameObject);
            }

            for (int i = 0; i < studies.Count; i++)
            {
                Study study = studies[i];

                // A written suggestion is already the sentence; only the authored table holds keys.
                // Passing written words to Loc.T would look up a key nothing has and render the
                // sentence as itself wrapped in the missing-key marker.
                string title = study.IsLiteral ? study.TitleKey : Loc.T(study.TitleKey);
                string line = study.IsLiteral ? study.LineKey : Loc.T(study.LineKey);

                BuildProfileCard(_studies, "Study_" + study.Id, title, line, study.Reference);
            }
        }

        /// <summary>
        /// Asks the endpoint for suggestions and swaps them in if they arrive.
        ///
        /// The words come back written rather than as keys, which is the one place on this screen
        /// where a string the player reads was not authored into a locale file. That is inherent to
        /// asking a model for a sentence, and it is why the server does the checking: a suggestion
        /// that quoted the corpus, or pointed at a passage this build does not ship, never gets
        /// this far. What arrives here is text and a reference, and the reference still resolves
        /// through the same reader as every other citation.
        /// </summary>
        void RequestStudies(GameState state)
        {
            if (!StudyService.IsConfigured)
            {
                return;
            }

            StudyService.Request(state, remote =>
            {
                // The sheet may well be gone by the time an answer lands.
                if (_closed || _studies == null)
                {
                    return;
                }

                var studies = new List<Study>(remote.Count);
                for (int i = 0; i < remote.Count; i++)
                {
                    StudyService.RemoteStudy study = remote[i];
                    studies.Add(Study.Written("remote_" + i, study.Title, study.Line, study.Reference));
                }

                FillStudies(studies);
            });
        }

        /// <summary>The bar, its label and its fraction, plus one line saying what moves it.</summary>
        void BuildProfileMeter(RectTransform content, GameState state)
        {
            ProgressBar meter = UIKit.CreateProgress(content, "EngagementMeter", Loc.T("profile.meter"));
            meter.SetValue(EngagementMeter.Value(state), EngagementMeter.Ceiling);

            Text hint = UIKit.CreateText(content, "EngagementHint", Loc.T("profile.meter.hint"),
                DesignTokens.Type.Minimum, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            SizeProfileText(hint, RowWidth);
        }

        /// <summary>A section heading, and the line under it when the section has one.</summary>
        void BuildProfileSection(RectTransform content, string name, string heading, string hint)
        {
            Text title = UIKit.CreateText(content, name, heading, DesignTokens.Type.Body,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);
            SizeProfileText(title, RowWidth);

            if (string.IsNullOrEmpty(hint))
            {
                return;
            }

            Text line = UIKit.CreateText(content, name + "Hint", hint, DesignTokens.Type.Minimum,
                DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            SizeProfileText(line, RowWidth);
        }

        /// <summary>
        /// One card: a title, a sentence, and — when there is a passage behind it — the button that
        /// opens the chapter.
        ///
        /// Full width and prose, per the design system's rule 12: a row that has to print a sentence
        /// gets one full-width column, and the two-column grid is for label-plus-number cards.
        ///
        /// <b>Panel plus a hairline</b>, which is the arrangement the materials cards on this same
        /// sheet already arrived at. Not Card: the sheet is a Card, so a Card inside it is the same
        /// fill on the same fill, and the first build of this screen drew four of them and they were
        /// invisible. Not Scroll either, and that one had to be seen to be believed — parchment
        /// reads beautifully and then every button on it disappears, because the kit has no button
        /// variant drawn for a light surface, so Secondary's label came out cream on cream.
        /// </summary>
        void BuildProfileCard(RectTransform content, string name, string title, string line, string reference)
        {
            Image card = UIKit.CreateCard(content, name, UIKit.CardStyle.Panel);
            var cardRect = (RectTransform)card.transform;

            Image hairline = UIKit.CreatePanel(cardRect, "Hairline",
                UIKit.WithAlpha(DesignTokens.Ink.Primary, 0.20f), UiSpriteKeys.FocusRing);
            UIKit.Stretch((RectTransform)hairline.transform);
            hairline.raycastTarget = false;
            UIKit.Layout(hairline).ignoreLayout = true;

            int pad = Mathf.RoundToInt(DesignTokens.Space.S16);
            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S8, new RectOffset(pad, pad, pad, pad));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement cardLayout = UIKit.Layout(card);
            cardLayout.minWidth = RowWidth;
            cardLayout.preferredWidth = RowWidth;
            cardLayout.flexibleWidth = 0f;

            float inner = RowWidth - 2f * DesignTokens.Space.S16;

            Text heading = UIKit.CreateText(cardRect, "Title", title, DesignTokens.Type.Body,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);
            SizeProfileText(heading, inner);

            // Body rather than Minimum: at Minimum the leading is still measured for Body, and the
            // two lines of a sentence drift far enough apart to read as two separate remarks.
            Text body = UIKit.CreateText(cardRect, "Line", line, DesignTokens.Type.Body,
                DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            SizeProfileText(body, inner);

            if (string.IsNullOrEmpty(reference))
            {
                return;
            }

            // The button sits in a row of its own so it keeps its own width. Dropped straight into
            // the card's column it stretches to the full card, which reads as a banner rather than
            // a control and puts a 300-point tap target on a one-word label.
            RectTransform actions = UIKit.CreateRect(name + "Actions", cardRect);
            UIKit.HorizontalGroup(actions.gameObject, DesignTokens.Space.S8, new RectOffset(),
                                  TextAnchor.MiddleLeft);
            UIKit.Layout(actions).minHeight = UIKit.ButtonMinHeight;

            string chapter = reference;
            Button open = UIKit.CreateButton(actions, "Open", Loc.T("profile.study.open"),
                UIKit.ButtonVariant.Secondary, () => OpenStudy(chapter));

            LayoutElement openLayout = UIKit.Layout(open);
            openLayout.minWidth = StudyButtonWidth;
            openLayout.preferredWidth = StudyButtonWidth;
            openLayout.flexibleWidth = 0f;

            RectTransform spacer = UIKit.CreateRect(name + "ActionsSpacer", actions);
            UIKit.Layout(spacer).flexibleWidth = 1f;
        }

        /// <summary>
        /// Opens the chapter behind a study, through the same reader every other citation uses.
        ///
        /// Three things this gets right. The sheet closes first: the reader is the screen this whole
        /// game is trying to reach, and opening it under a modal still on screen would put it behind
        /// what the player just left. The reader takes a <b>chapter</b>, so a verse reference is cut
        /// back to one. And <c>gameAsked</c> is false, because nobody prompted this — a study opened
        /// from the profile is the player's own move, which is what <c>unprompted_read</c> counts.
        /// </summary>
        void OpenStudy(string reference)
        {
            Close();
            SheepGate.Scripture.ChapterReaderUI.Open(ChapterOf(reference), StudyTrigger, false);
        }

        /// <summary>The chapter a reference sits in: NEH.4.17 becomes NEH.4.</summary>
        static string ChapterOf(string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return reference;
            }

            string[] parts = reference.Split('.');
            return parts.Length >= 2 ? parts[0] + "." + parts[1] : reference;
        }

        /// <summary>
        /// Pins a text to a width and lets its height follow the words.
        ///
        /// <b>The height is deliberately not written out.</b> The first version set preferredHeight
        /// from Text.preferredHeight right here, which reads as the careful thing to do and is the
        /// one arrangement that cannot work: at construction the label has not been given the width
        /// above yet, so it reports the height of the wrong line count. On the phone that produced
        /// cards several hundred points tall with their text somewhere in the middle of them.
        /// </summary>
        static void SizeProfileText(Text text, float width)
        {
            if (text == null)
            {
                return;
            }

            LayoutElement layout = UIKit.Layout(text);
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
        }

        // ------------------------------------------------------------------ the materials tab

        /// <summary>
        /// The three materials, as a two-column grid with the whole content band to itself.
        ///
        /// The character is absent here, which is what pays for the grid: 572 points instead of the
        /// 432 a wardrobe tab has. Two explicit rows rather than a
        /// <see cref="GridLayoutGroup"/>, so a row can sit short without a special cell size — as
        /// row two did while stone, timber and blocks were the whole list.
        ///
        /// <b>The rows are meaningful.</b> Row one is what the valley gives you; row two is what
        /// those two become, plus the talents a check-in pays. The 320 points below the grid stay
        /// empty on purpose: rule 10 forbids everything that would obviously fill them — a
        /// completion count, a "next unlock", a bar — and there is nothing else honest to put
        /// there. Calm and short beats padded.
        ///
        /// The scroll view is kept even though four cards cannot overflow, for two reasons. A
        /// fifth material later scrolls without a rewrite, and the kit's transparent
        /// raycast-target viewport Image is what makes a grid of non-button cards draggable at all —
        /// these cards are not buttons and have no graphic a finger can hit. No scrollbar is
        /// attached: a permanent bar with a full-height handle on a tab that does not scroll reads
        /// as decoration, and without its lane the grid gets the full 308 points.
        /// </summary>
        void BuildMaterialsTab(RectTransform content)
        {
            RectTransform grid = UIKit.CreateRect("MaterialsGrid", content);
            UIKit.VerticalGroup(grid.gameObject, DesignTokens.Space.S12, new RectOffset());
            LayoutElement gridLayout = UIKit.Layout(grid);
            gridLayout.minHeight = 2f * MaterialCardHeight + DesignTokens.Space.S12;
            gridLayout.preferredHeight = gridLayout.minHeight;
            gridLayout.flexibleHeight = 0f;

            _materialCounts = new Text[MaterialLabelKeys.Length];

            RectTransform first = BuildMaterialsRow(grid);
            BuildMaterialCard(first, 0);
            BuildMaterialCard(first, 1);

            RectTransform second = BuildMaterialsRow(grid);
            BuildMaterialCard(second, 2);
            BuildMaterialCard(second, 3);
        }

        /// <summary>One row of the material grid. Both rows carry the same name; both are handles.</summary>
        static RectTransform BuildMaterialsRow(RectTransform grid)
        {
            RectTransform row = UIKit.CreateRect("MaterialsRow", grid);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S12, new RectOffset(),
                TextAnchor.MiddleLeft);

            LayoutElement rowLayout = UIKit.Layout(row);
            rowLayout.minHeight = MaterialCardHeight;
            rowLayout.preferredHeight = MaterialCardHeight;
            rowLayout.flexibleHeight = 0f;
            return row;
        }

        /// <summary>
        /// One material: its word above its number.
        ///
        /// <b>A deliberate deviation from design-system rule 8</b>, which asks for a quantity in
        /// mono "with the resource's label beside them". Side by side inside a 148-point card, the
        /// four-digit <c>Type.Title</c> mono reserve leaves a 57.73-point label box and "Madeira" is
        /// 56.46 — 1.27 points of slack, one re-translation from clipping, in the least important
        /// component on the sheet. Stacking gives the label and the number the full 116 points each.
        /// The rule's teeth are still met: every number has its word, and no number is a bare icon.
        /// Record this; do not "fix" it back into a clipping hazard.
        ///
        /// <b>No icon.</b> UiArt ships Check, Close, Dot, Arrow, Menu, Lock and Bag, and none of
        /// them reads as stone, timber or a block. Rule 7 forbids an icon <i>without</i> a number,
        /// not a number without an icon, so three sprites are not invented for this.
        ///
        /// The count is mono so its digits are tabular: IBM Plex Mono Medium's digit advance is
        /// exactly 0.6em, so four digits occupy 50.27 points and the number cannot shuffle sideways
        /// as it climbs. It is read straight off <see cref="GameState"/>, which is the same storage
        /// <c>ResourceSystem</c> wraps — reading it directly is what guarantees the grid and the
        /// figure above it are describing the same run.
        ///
        /// A material at zero still shows its card. Hiding what the player lacks would teach them
        /// nothing about what the wall needs next, which is the one thing this readout is for. And
        /// no prices, no timers, no currency, no "you need N more": rules 18 and 10.
        /// </summary>
        void BuildMaterialCard(RectTransform row, int index)
        {
            Image card = UIKit.CreateCard(row, MaterialObjectNames[index], UIKit.CardStyle.Panel);
            var cardRect = (RectTransform)card.transform;
            card.raycastTarget = false;

            LayoutElement cardLayout = UIKit.Layout(card);
            cardLayout.minWidth = MaterialCardWidth;
            cardLayout.preferredWidth = MaterialCardWidth;
            cardLayout.flexibleWidth = 0f;
            cardLayout.minHeight = MaterialCardHeight;
            cardLayout.preferredHeight = MaterialCardHeight;
            cardLayout.flexibleHeight = 0f;

            // Without this the card is invisible: Surface.Panel on Surface.Card measures 1.11:1 and
            // Surface.Border measures 1.04:1. This is the kit's own Secondary-button hairline,
            // parchment at 20%, which measures 1.80:1. SC 1.4.11 does not bind a non-interactive
            // container, and no token on this palette clears 3:1 without becoming a text colour.
            // ignoreLayout because the card carries a vertical group and a stretched child would
            // otherwise be laid out as a row of it.
            Image hairline = UIKit.CreatePanel(cardRect, "Hairline",
                UIKit.WithAlpha(DesignTokens.Ink.Primary, 0.20f), UiSpriteKeys.FocusRing);
            UIKit.Stretch((RectTransform)hairline.transform);
            hairline.raycastTarget = false;
            UIKit.Layout(hairline).ignoreLayout = true;

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S8, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S16),
                Mathf.RoundToInt(DesignTokens.Space.S16),
                Mathf.RoundToInt(DesignTokens.Space.S16),
                Mathf.RoundToInt(DesignTokens.Space.S16)), TextAnchor.MiddleLeft);

            float box = MaterialCardWidth - 2f * DesignTokens.Space.S16;

            // Ink.Secondary at Type.Body, promoted from the old Ink.Muted at Type.Minimum, which
            // measured 3.98:1 on this very surface against a 4.5:1 requirement.
            Text label = UIKit.CreateText(cardRect, "Label", Loc.T(MaterialLabelKeys[index]),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Body);
            PinText(label, box);

            Text count = UIKit.CreateText(cardRect, "Count", string.Empty, DesignTokens.Type.Title,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft, DesignTokens.TypeRole.Mono);
            PinTextBox(count, box, TitleLineHeight);

            _materialCounts[index] = count;
        }

        // ------------------------------------------------------------------ interaction

        /// <summary>
        /// Opens a section. Aparência returns to whichever of its three lists was last open, so
        /// stepping out to Itens and back does not undo where the player was.
        /// </summary>
        void SelectSection(int section)
        {
            if (section < 0 || section >= SectionCount)
            {
                return;
            }

            SelectTab(section == SectionAppearance ? _lastWardrobe : SectionLanding[section]);
        }

        /// <summary>The section a content tab belongs to.</summary>
        static int SectionOf(int tabIndex)
        {
            if (tabIndex == TabIndexProfile)
            {
                return SectionProfile;
            }

            return tabIndex == TabIndexMaterials ? SectionItems : SectionAppearance;
        }

        /// <summary>
        /// Shows one tab, and spends that slot's badges.
        ///
        /// The badge economy lives here, and it is the part with a rule behind it: <b>every tab
        /// spends when it is looked at, including the one shown first</b>. That is what makes the
        /// dots do their work instead of all dying on open — an unvisited tab keeps its dot, and the
        /// tab you are on loses it the moment you arrive. The NOVO badges inside the tab do not
        /// vanish with it, because every row captured <see cref="Wardrobe.IsNew"/> at build time;
        /// they last the whole open and are gone on the next one.
        ///
        /// Materiais spends nothing and never carries a dot. Materials are not catalogue items and
        /// have no seen-state, and the container being called "SlotSegments" does not make its
        /// fourth cell a slot.
        ///
        /// Tapping the tab already up is a no-op, never a toggle-off, and selection is not
        /// remembered across opens: every open starts on Cabelo, deterministically.
        /// </summary>
        void SelectTab(int index)
        {
            if (_tabs == null || index < 0 || index >= _tabs.Length)
            {
                return;
            }

            if (index == _selected)
            {
                return;
            }

            _selected = index;

            for (int i = 0; i < _tabs.Length; i++)
            {
                TabView tab = _tabs[i];
                if (tab != null && tab.Scroll != null)
                {
                    tab.Scroll.gameObject.SetActive(i == index);
                }
            }

            bool wardrobe = IsWardrobeTab(index);

            if (wardrobe)
            {
                _lastWardrobe = index;
            }

            // The stage is the wardrobe's: it exists to show a piece on a body while it is being
            // chosen. Perfil and Itens have nothing to put on a body, so they take its band.
            if (_stage != null)
            {
                _stage.SetActive(wardrobe);
            }

            // The lower bar belongs to Aparência and goes away with it, rule and all. Three slot
            // cells left standing under a list of materials would offer a choice that changes
            // nothing on screen.
            if (_slotBar != null)
            {
                _slotBar.gameObject.SetActive(wardrobe);
            }

            if (_slotDivider != null)
            {
                _slotDivider.gameObject.SetActive(wardrobe);
            }

            // Two numbers move with the section: the top, which the stage takes when there is one,
            // and the bottom, which the lower bar takes when it is up.
            if (_tabContent != null)
            {
                UIKit.Stretch(_tabContent, SheetPadding, SheetPadding,
                    wardrobe ? WardrobeContentTop : SectionContentTop,
                    wardrobe ? ContentBottom : PlainContentBottom);
            }

            ShowRefusal(null);
            SettleTab(_tabs[index]);

            // Exactly one repaint, always. MarkSlotSeen raises Changed and this panel is subscribed
            // to it, so the naive form repainted four segments and eighteen rows twice per tap.
            //
            // The obvious fix — let the event do it and return early — is wrong, and e2e caught it:
            // Build() calls SelectTab BEFORE it subscribes, so on the very first selection there is
            // no subscriber to repaint, and the sheet opened with no tab painted as current. That is
            // the whole failure mode of routing a guarantee through an event: it holds only once
            // somebody is listening. Suppressing the event and repainting here holds either way.
            _suppressChangedRepaint = true;
            try
            {
                if (wardrobe)
                {
                    Wardrobe.MarkSlotSeen(_state, TabSlots[index]);
                }
            }
            finally
            {
                _suppressChangedRepaint = false;
            }

            Refresh();
        }

        /// <summary>
        /// Measures and homes a tab, once, the first time it is shown.
        ///
        /// Legacy <see cref="Text"/> settles its wrapped height against the width it currently has,
        /// and neither a forced rebuild nor a scroll position can be applied to an inactive object,
        /// so this is deferred to activation rather than run at build time on three hidden panels.
        /// The order inside it matters for the same reason it did before: a scroll position set
        /// against an unmeasured column is applied to a height that is about to change.
        /// </summary>
        static void SettleTab(TabView tab)
        {
            if (tab == null || tab.Settled || tab.Content == null)
            {
                return;
            }

            tab.Settled = true;
            UIKit.RebuildNow(tab.Content);

            if (tab.Scroll != null)
            {
                tab.Scroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>
        /// A tap on a row.
        ///
        /// Three outcomes, and none of them takes anything away:
        /// * <b>The piece is not worn.</b> It goes on, through <see cref="Wardrobe.TryEquip"/>,
        ///   which does the swap a one-item slot needs and asks the catalogue about the set that
        ///   would result before it writes anything. A refusal leaves the player dressed exactly as
        ///   they were and puts one calm sentence beside the figure.
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
        void OnRowTapped(string itemId, CharacterSlot slot)
        {
            if (string.Equals(Wardrobe.EquippedInSlot(_state, slot), itemId, StringComparison.Ordinal))
            {
                if (CharacterCatalog.SlotHoldsMany(slot))
                {
                    Wardrobe.Unequip(_state, itemId);
                }

                ShowRefusal(null);
                return;
            }

            string refusalKey;
            if (Wardrobe.TryEquip(_state, itemId, out refusalKey))
            {
                ShowRefusal(null);
                return;
            }

            ShowRefusal(string.IsNullOrEmpty(refusalKey) ? null : Loc.T(refusalKey));
        }

        /// <summary>
        /// Puts a sentence beside the figure, or clears it.
        ///
        /// A sentence arrives on a <c>Motion.ToastIn</c> fade, and <b>that fade keeps running under
        /// reduced motion</b>. The design system suppresses parallax, pulse and shake; a fade is
        /// information, and a reduce-motion player who taps a row and sees nothing animate at all
        /// cannot tell whether the tap registered. It is never compensated for with a sound.
        ///
        /// Cleared on a successful equip, a successful unequip, and every tab change. Clearing sets
        /// the alpha outright rather than fading out: an empty label has nothing to fade, and a
        /// half-finished fade-in would otherwise leave a stale alpha behind for the next sentence.
        /// </summary>
        void ShowRefusal(string sentence)
        {
            if (_refusal == null)
            {
                return;
            }

            _refusal.text = sentence ?? string.Empty;

            if (_refusalGroup == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(sentence))
            {
                UIMotion.Stop(_refusalFade);
                _refusalFade = null;
                _refusalGroup.alpha = 1f;
                return;
            }

            // Stop the previous fade before starting another. Two tweens writing one alpha is not
            // a leak and converges on the same value, but the comment above promised a protection
            // the code did not have, and a tab switch mid-fade is an ordinary thing to do.
            UIMotion.Stop(_refusalFade);
            _refusalGroup.alpha = 0f;
            _refusalFade = UIMotion.Fade(_refusalGroup, 1f, DesignTokens.Motion.ToastIn);
        }


        /// <summary>
        /// Says so, loudly and once, when a tab label is wider than the cell holding it.
        ///
        /// "The four labels fit" is an acceptance criterion with no other guard. It is checked here
        /// rather than in the content validator because the only honest measurement uses the real
        /// font, at the real size, in the locale actually loaded, against a cell width derived from
        /// the width THIS device reports — and a script on a build machine has none of those. The
        /// widest case is the selected cell, which draws at BodyStrong, so that is the one that has
        /// to clear.
        ///
        /// The margin is genuinely thin: on an iPhone 17 Pro the cell is about 71.6 points and
        /// "Materiais" sets at about 68.9, which is one retranslation away from failing. A label
        /// that overflows does not throw and does not fail a hierarchy assertion — it just renders
        /// past its cell, which is exactly the class of defect that reached a build once already.
        /// </summary>
        static void WarnIfLabelOverflows(Text label, bool selected)
        {
            if (label == null || !selected || _overflowReported)
            {
                return;
            }

            float needed = label.preferredWidth;
            if (needed <= SegmentWidth)
            {
                return;
            }

            _overflowReported = true;
            Debug.LogError("[BackpackPanel] The tab label \"" + label.text + "\" needs " +
                           needed.ToString("0.0") + " units and its cell is " +
                           SegmentWidth.ToString("0.0") + ". It will render past the cell. Shorten " +
                           "the label in ui.json, or give the tab bar two lines — do not shrink the " +
                           "type, which is already at Type.Body and floors at Type.Minimum.");
        }

        /// <summary>One report per session. A label that does not fit does not fit on every repaint.</summary>
        static bool _overflowReported;


        /// <summary>
        /// The wardrobe changed somewhere. Repaints, unless this panel is the one that changed it
        /// and is about to repaint anyway — see <see cref="SelectTab"/>.
        /// </summary>
        void OnWardrobeChanged()
        {
            if (_suppressChangedRepaint)
            {
                return;
            }

            Refresh();
        }

        /// <summary>Set only across the one call in <see cref="SelectTab"/> that raises Changed itself.</summary>
        bool _suppressChangedRepaint;

        // ------------------------------------------------------------------ repainting

        /// <summary>
        /// Redraws everything that can have changed. Wired to <see cref="Wardrobe.Changed"/>, so an
        /// equip from anywhere lands here and the figure follows in the same frame as the ring.
        ///
        /// <b>It never rebuilds a row.</b> Rows are built once per open, with their heights pinned
        /// and their badges captured; this only swaps sprites, colours, rings and dots. A repaint
        /// that rebuilt rows would recompute <see cref="Wardrobe.IsNew"/> after the first
        /// <c>MarkSlotSeen</c> and wipe the badges the player is looking at.
        /// </summary>
        void Refresh()
        {
            RefreshFigure();
            RefreshBalance();
            RefreshMaterials();
            RefreshSegments();
            RefreshRows();
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

        /// <summary>
        /// The header's talent count, read straight off <see cref="GameState"/> like the Materiais
        /// grid below it, so the two readings of the same number cannot disagree.
        /// </summary>
        void RefreshBalance()
        {
            if (_balanceCount == null)
            {
                return;
            }

            int talents = _state != null ? Mathf.Max(0, _state.talents) : 0;
            _balanceCount.text = talents.ToString(CultureInfo.InvariantCulture);
        }

        void RefreshMaterials()
        {
            if (_materialCounts == null)
            {
                return;
            }

            SetCount(0, _state != null ? Mathf.Max(0, _state.stone) : 0);
            SetCount(1, _state != null ? Mathf.Max(0, _state.timber) : 0);
            SetCount(2, _state != null ? Mathf.Max(0, _state.blocks) : 0);
            SetCount(3, _state != null ? Mathf.Max(0, _state.talents) : 0);
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
        /// Repaints the tab bar: which cell is filled, and which cells still hold something unseen.
        ///
        /// The dot asks <see cref="Wardrobe.NewCountForSlot"/> rather than counting for itself, so
        /// the dots and the HUD's own pill are two readings of one number and cannot disagree. It is
        /// asked live on every repaint, which is why looking at a tab clears its dot immediately:
        /// <c>MarkSlotSeen</c> raises <c>Changed</c>, this runs, and the count is already zero.
        ///
        /// A dot, never a numeral — see the class summary for why four legal numbers make an
        /// illegal screen.
        /// </summary>
        void RefreshSegments()
        {
            if (_tabs == null)
            {
                return;
            }

            for (int i = 0; i < _tabs.Length; i++)
            {
                TabView tab = _tabs[i];
                if (tab == null)
                {
                    continue;
                }

                bool selected = i == _selected;

                if (tab.Fill != null)
                {
                    tab.Fill.gameObject.SetActive(selected);
                    tab.Fill.color = DesignTokens.Brand.PrimaryDark;
                }

                if (tab.Rim != null)
                {
                    tab.Rim.gameObject.SetActive(selected);
                }

                if (tab.Label != null)
                {
                    DesignTokens.TypeRole role = selected
                        ? DesignTokens.TypeRole.BodyStrong
                        : DesignTokens.TypeRole.Body;

                    Font font = DesignTokens.Font(role);
                    tab.Label.font = font;
                    tab.Label.lineSpacing = UIKit.LineSpacingFor(font, UIKit.LeadingFor(role));
                    tab.Label.color = selected ? DesignTokens.Ink.OnPrimary : DesignTokens.Ink.Secondary;

                    WarnIfLabelOverflows(tab.Label, selected);
                }

                if (tab.Dot != null)
                {
                    tab.Dot.enabled = IsWardrobeTab(i) &&
                                      Wardrobe.NewCountForSlot(_state, TabSlots[i]) > 0;
                }
            }

            RefreshSections();
        }

        /// <summary>
        /// Paints the top bar the same way, against the section the open tab belongs to.
        ///
        /// Aparência's dot is the three slot dots folded into one: with the wardrobe closed its
        /// cells are not on screen to carry their own, and a sheet that hid the fact that something
        /// new is waiting is a sheet nobody opens twice.
        /// </summary>
        void RefreshSections()
        {
            if (_sections == null)
            {
                return;
            }

            int active = SectionOf(_selected);

            for (int i = 0; i < _sections.Length; i++)
            {
                TabView section = _sections[i];
                if (section == null)
                {
                    continue;
                }

                bool selected = i == active;

                if (section.Fill != null)
                {
                    section.Fill.gameObject.SetActive(selected);
                    section.Fill.color = DesignTokens.Brand.PrimaryDark;
                }

                if (section.Rim != null)
                {
                    section.Rim.gameObject.SetActive(selected);
                }

                if (section.Label != null)
                {
                    DesignTokens.TypeRole role = selected
                        ? DesignTokens.TypeRole.BodyStrong
                        : DesignTokens.TypeRole.Body;

                    Font font = DesignTokens.Font(role);
                    section.Label.font = font;
                    section.Label.lineSpacing = UIKit.LineSpacingFor(font, UIKit.LeadingFor(role));
                    section.Label.color = selected ? DesignTokens.Ink.OnPrimary : DesignTokens.Ink.Secondary;
                }

                if (section.Dot != null)
                {
                    section.Dot.enabled = i == SectionAppearance && AnythingUnseenInWardrobe();
                }
            }
        }

        /// <summary>True when any of the three wardrobe slots holds something unlooked at.</summary>
        bool AnythingUnseenInWardrobe()
        {
            for (int i = TabIndexHair; i <= TabIndexAccessory; i++)
            {
                if (Wardrobe.NewCountForSlot(_state, TabSlots[i]) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        void RefreshRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                RowView row = _rows[i];
                if (row == null)
                {
                    continue;
                }

                // "Worn" is what the slot is actually showing, not merely what is in the worn list.
                // The accessory slot may legitimately hold several pieces while AppearanceState has
                // one accessory layer, so marking every one of them would put a check beside items
                // the figure is not drawing.
                bool worn = string.Equals(Wardrobe.EquippedInSlot(_state, row.Slot), row.ItemId,
                    StringComparison.Ordinal);

                if (row.Ring != null)
                {
                    row.Ring.gameObject.SetActive(worn);
                }

                if (row.Status != null)
                {
                    if (worn)
                    {
                        row.Status.sprite = UIKit.GetSprite(UiSpriteKeys.IconCheck);
                        row.Status.color = DesignTokens.Brand.Primary;
                        row.Status.enabled = true;
                    }
                    else if (row.Locked)
                    {
                        row.Status.sprite = UIKit.GetSprite(UiSpriteKeys.IconLock);
                        row.Status.color = DesignTokens.Ink.Secondary;
                        row.Status.enabled = true;
                    }
                    else
                    {
                        // Hidden, never removed. The layout slot it occupies is what keeps the name
                        // box the width every height in this row was measured against.
                        row.Status.enabled = false;
                    }
                }

                // Re-applied here and not only at build time: the announced state would otherwise go
                // stale the moment the player equipped something.
                AccessibleLabel.Apply(row.Root, AccessibleNameFor(row, worn));
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Whether this tab index is one of the three that owns a slot.</summary>
        static bool IsWardrobeTab(int index)
        {
            // A range between the two tabs that are not wardrobes, rather than "less than
            // Materiais", which is what it used to say and what silently made Perfil a wardrobe the
            // moment Perfil took index 0.
            return index > TabIndexProfile && index < TabIndexMaterials;
        }

        /// <summary>
        /// The name a screen reader would say, with the row's state folded into it.
        ///
        /// Composed name-first and nested, so the separators and the word order are the locale's
        /// business and no punctuation literal reaches this file: the base name, then
        /// <c>backpack.state.new</c> around it if it is new, then <c>backpack.state.equipped</c>
        /// around that if it is worn. A locked row takes a single form that carries the unlock
        /// sentence <b>verbatim</b> — the same sentence the row prints, never a rephrasing of it,
        /// because two wordings of one condition is two conditions as far as a listener can tell.
        ///
        /// Note for whoever builds the accessibility hierarchy: this project is on Unity 6 with
        /// <c>com.unity.modules.accessibility</c> in the manifest, so VoiceOver over uGUI is now
        /// possible and the older note that Unity publishes no accessibility tree is stale. When
        /// someone wires it up, <b>a locked row must not map to a disabled-style state</b> — rule 7
        /// keeps it fully interactable, and announcing it as unavailable would undo in speech
        /// exactly what the visual design refuses to do.
        /// </summary>
        static string AccessibleNameFor(RowView row, bool worn)
        {
            if (row == null)
            {
                return string.Empty;
            }

            if (row.Locked)
            {
                return Loc.T(StateLockedKey, row.ItemName, row.UnlockSentence ?? string.Empty);
            }

            string composed = row.ItemName;

            if (row.IsNew)
            {
                composed = Loc.T(StateNewKey, composed);
            }

            if (worn)
            {
                composed = Loc.T(StateEquippedKey, composed);
            }

            return composed;
        }

        /// <summary>
        /// Gives a label its box, asks it how tall it is in that box, and pins it to both.
        ///
        /// This is the whole anti-clipping mechanism in four lines. Legacy <see cref="Text"/>
        /// measures its wrapped height against <c>rect.width</c>, so the width has to be real before
        /// the question is asked — which is why the anchors are collapsed to a point first, making
        /// <c>sizeDelta</c> the actual size rather than an offset from a parent that has not been
        /// laid out yet. Both dimensions then go onto a <see cref="LayoutElement"/> with zero
        /// flexibility, so no layout group downstream can renegotiate the width the height was
        /// measured against.
        /// </summary>
        static float PinText(Text text, float boxWidth)
        {
            if (text == null)
            {
                return 0f;
            }

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(boxWidth, 0f);

            float height = text.preferredHeight;
            PinTextBox(text, boxWidth, height);
            return height;
        }

        /// <summary>
        /// The same pinning with the height decided rather than measured, for the labels whose
        /// reserve is a design decision instead of a consequence: the refusal line's three-line
        /// headroom, the one line the player's own name is allowed, and a count that is empty at
        /// build time and would otherwise measure as nothing.
        /// </summary>
        static void PinTextBox(Text text, float boxWidth, float height)
        {
            if (text == null)
            {
                return;
            }

            var rect = text.rectTransform;
            rect.sizeDelta = new Vector2(boxWidth, height);

            LayoutElement layout = UIKit.Layout(text);
            layout.minWidth = boxWidth;
            layout.preferredWidth = boxWidth;
            layout.flexibleWidth = 0f;
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

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
    /// The pressed state of a selected tab, and nothing else.
    ///
    /// <see cref="VariantButton"/> repaints only the graphics it was bound to — its own fill, its
    /// border, its label — and the clay fill behind a selected segment is none of those, so a press
    /// on the tab that is already up would otherwise show nothing at all. The kit's own ghost
    /// hover and press fills still play underneath, which is what gives an <i>unselected</i> cell
    /// its feedback; this covers the one cell that hides them.
    ///
    /// Deliberately silent. The tab bar plays no cue, and a press tint is the whole of the
    /// acknowledgement it gets.
    /// </summary>
    sealed class SegmentPressTint : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        Image _fill;
        Color _normal;
        Color _pressed;

        /// <summary>
        /// Binds the fill and its two colours. Both are passed in rather than read from
        /// <see cref="DesignTokens"/> here, so the pair stays decided in one place — the segment
        /// builder, where the contrast arithmetic that chose them is written down.
        /// </summary>
        public void Bind(Image fill, Color normal, Color pressed)
        {
            _fill = fill;
            _normal = normal;
            _pressed = pressed;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Apply(_pressed);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Apply(_normal);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Apply(_normal);
        }

        void Apply(Color color)
        {
            if (_fill != null)
            {
                _fill.color = color;
            }
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
