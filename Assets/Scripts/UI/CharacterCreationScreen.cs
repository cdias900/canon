using System;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The first screen a new run shows: <b>who you are, and then how you look.</b>
    ///
    /// ==================================================================================
    /// WHY THIS IS TWO STEPS AND NOT SIX SLIDERS
    /// ==================================================================================
    /// The screen this replaced offered six raw art indices — body, skin, hair, top, legs,
    /// accessory — and the backpack offered catalogue items. They were two vocabularies for one
    /// wardrobe, and nobody had introduced them: in minute one the player picked a hairstyle that
    /// the backpack would later tell them they had not unlocked. That is the contradiction this
    /// screen exists to end. Everything here is a catalogue item, unlocked or not, drawn by the
    /// same <see cref="WardrobeRow"/> the backpack draws.
    ///
    /// So creation is now: <b>choose Adar or Neriah, then customise.</b> The build, the face and
    /// the silhouette belong to the character — they are not a slider, and the two of them are not
    /// two ends of one. Choosing is a step with weight rather than the first row of a long scroll,
    /// because a choice placed at the top of a scroll scrolls away and reads as a setting.
    ///
    /// <b>Skin tone is the one exception, and it is deliberate.</b> The character supplies the
    /// build; the tone stays with whoever is playing. Two fixed characters would otherwise mean two
    /// fixed tones, and in a game for 13-19 year olds across Brazil that narrows who can see
    /// themselves on screen. Mechanically the two live in separate ints —
    /// <see cref="AppearanceState.body"/> is the build, <see cref="AppearanceState.skin"/> is the
    /// tone — and <see cref="AppearanceState.BodyArtVariant"/> packs them at read time.
    /// <see cref="CharacterPresets.ApplyTo"/> restores the tone after laying a character's floor,
    /// which is the mechanism that makes the exception hold rather than a convention anyone has to
    /// remember. <b>This screen is also the only place in the game where the tone can be set at
    /// all</b> — there is no tone tab in the backpack — which is why it is offered plainly, with no
    /// default presented as normal, no label, no ordering cue, and the same ring every other
    /// control on the screen uses.
    ///
    /// ==================================================================================
    /// WHAT IS ABSENT, AND WHY THAT IS THE POINT
    /// ==================================================================================
    /// No account, no e-mail, no password, no age question, no question about religion. The player
    /// picks a look and starts. Anything else asked here would be a toll gate in front of a game
    /// that has not earned one yet. The name field is optional and a blank name is a valid answer:
    /// the valley calls you by a fallback word rather than refusing to open.
    ///
    /// ==================================================================================
    /// THE VERTICAL BUDGET, AND WHO LOSES WHEN IT DOES NOT ADD UP
    /// ==================================================================================
    /// This screen has less height than it has content, and pretending otherwise is what broke it
    /// once already. Both steps therefore allocate rather than lay out and hope, and both start
    /// from the thing the step exists for:
    ///
    /// * <b>Step 2 gives the list <see cref="Composer.MinimumVisibleRows"/> rows of the tallest row
    ///   in the running locale first</b>, then the fixed chrome — the eyebrow row, the tab bar, the
    ///   action row, the safe-area lane — and the preview strip takes what is left. The strip is
    ///   last because it is the only band on the step that is not a touch target, a safe-area inset
    ///   or the list. On a 1080x1920 canvas that leaves it under its preferred height and it draws
    ///   compact; on a phone it draws in full. See <see cref="Composer.ApplyVerticalBudget"/>.
    /// * <b>Step 1 measures its own words before deciding how tall anything is</b> — the card's
    ///   personality line and the hint under the name field both — caps the cards so the tone row
    ///   always peeks above the fold, and scrolls for the rest. See
    ///   <see cref="Composer.CardHeight"/> and <see cref="Composer.MeasureNameHint"/>.
    ///
    /// Two consequences are written here because they read as mistakes otherwise. <b>The chosen
    /// character's name lives in the eyebrow row</b>, in the gap Back and the language toggle leave
    /// empty, rather than on a line of its own. <b>The refusal lives on the preview strip</b>, drawn
    /// over its foot when there is something to say, rather than in a reserved band that is empty in
    /// every session where nothing was refused. Both moved for the same 100-odd units, and both are
    /// the backpack's own answer to the same problem.
    ///
    /// ==================================================================================
    /// THE RULES THIS SCREEN CARRIES
    /// ==================================================================================
    /// * <b>Rule 7 — never punish.</b> Locked pieces are listed from the first minute, in full
    ///   colour, with their condition spelled out and still tappable. Re-choosing the character you
    ///   already chose is a no-op rather than a reset of everything you swapped.
    ///   <c>ToCustomise</c> is never disabled — with nothing chosen it answers in a sentence, and a
    ///   disabled control at 0.40 opacity is the void this rule forbids.
    /// * <b>Rule 2 — never colour alone.</b> Every chosen thing on this screen — a character card,
    ///   a tone swatch, a worn piece, the selected tab — carries a ring <i>and</i> a mark or a
    ///   weight change.
    /// * <b>Rule 10 — no progress toward anything.</b> Nothing here counts. No "2 of 6", no
    ///   fraction, no bar, no "next unlock".
    /// * <b>Rule 18 — nothing is bought.</b> No price, no currency, no timer.
    /// * <b>Rule 13 — the smell checklist.</b> No jargon, no religious vocabulary, and nothing that
    ///   reads as a form.
    ///
    /// ==================================================================================
    /// COLOUR
    /// ==================================================================================
    /// <b>Gold appears nowhere on this screen except the focus ring.</b> Gold means "not yet seen"
    /// and is spent by the backpack's NOVO badge and tab dot; creation draws neither — see
    /// <see cref="BuildTabRows"/> for why it must not — so the design system's one-gold-action
    /// budget is unspent and both primary actions are clay.
    ///
    /// <c>DesignTokens.Ink.Muted</c> appears nowhere. It measures 3.57:1 on <c>Surface.Card</c> and
    /// 3.98:1 on <c>Surface.Panel</c> against a 4.5:1 requirement, and the screen this replaced used
    /// it on the eyebrow, the preview captions, the name label and every slot label. All of those
    /// are <c>Ink.Secondary</c> now. <c>Ink.Faint</c> is not a fallback: at 2.40:1 it fails body
    /// text, large text and the 3:1 non-text floor at once. There is no readable ink below
    /// <c>Ink.Secondary</c> on this palette, so a third text tier comes from size and weight.
    ///
    /// ==================================================================================
    /// WHAT A LANGUAGE SWITCH DOES, AND WHY THE SCREEN HOLDS NO DRAFT
    /// ==================================================================================
    /// <see cref="LanguageToggle"/> reloads the active scene, so in the cutscene path the whole
    /// intro replays and this screen is composed from scratch. Everything survives that only
    /// because the screen mutates the live <see cref="GameState"/> as the player works rather than
    /// holding a draft — including the typed name, which is why it is written on every keystroke
    /// and not only at confirm. It is also why <see cref="Composer.Build"/> opens directly on step 2
    /// when the run already knows its character: landing back on the character choice for changing
    /// language would read as the game forgetting.
    ///
    /// Nothing reaches disk until confirm, so quitting mid-creation leaves the previous save intact.
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
        /// Holds the screen and the handles a repaint needs. A plain object rather than a
        /// MonoBehaviour: the screen has no per-frame work, so button callbacks closing over this
        /// instance are enough.
        /// </summary>
        sealed class Composer
        {
            // ============================================================== locale keys
            // Every word the player reads is a key. Two tables feed this screen and they are not
            // interchangeable: the keys below are ui.json, read with Loc.T; an item's name and
            // description come from catalog.json through CharacterCatalog; a character's suggested
            // name and personality come from presets.json through CharacterPresets; and an unlock
            // sentence comes from catalog.json's "unlock" table through UnlockEvaluator.Sentence,
            // which returns a finished sentence and never a key.
            //
            // The constants end in "Key" on purpose. The content validator treats a const string
            // whose name ends in a player-text noun as a hardcoded sentence, and "Key" is the
            // escape hatch it offers for exactly this.

            const string EyebrowKey = "creation.eyebrow";
            const string TitleKey = "creation.title";
            const string SubtitleKey = "creation.subtitle";
            const string CustomiseTitleKey = "creation.customise.title";
            const string QuickStartKey = "creation.quick_start";
            const string ContinueKey = "creation.continue";
            const string BackKey = "creation.back";
            const string StartKey = "creation.start";
            const string ChooseFirstKey = "creation.choose_first";
            const string ChosenStateKey = "creation.state.chosen";
            const string ToneOptionKey = "creation.tone.option";
            const string ToneLabelKey = "creation.slot.skin";
            const string NameLabelKey = "creation.name_label";
            const string NamePlaceholderKey = "creation.name_placeholder";

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

            /// <summary>
            /// The three tab labels, in the order the bar draws them.
            ///
            /// A shared <c>slot.</c> namespace rather than <c>backpack.tab.</c>: these words have
            /// two call sites now, and two call sites is where drift lives. The row's own strings
            /// keep their <c>backpack.</c> names because after the extraction they have exactly one
            /// reader and cannot diverge.
            /// </summary>
            static readonly string[] TabLabelKeys = { "slot.hair", "slot.outfit", "slot.accessory" };

            // ============================================================== GameObject names
            // Spelled out rather than concatenated from a suffix: these are the handles
            // tools/e2e.sh drives the screen by, and a grep for one of them should land on the line
            // that creates it.

            static readonly string[] TabObjectNames = { "TabHair", "TabOutfit", "TabAccessory" };

            static readonly string[] SegmentObjectNames =
            {
                "SegmentHair", "SegmentOutfit", "SegmentAccessory"
            };

            static readonly string[] SegmentLabelObjectNames =
            {
                "SegmentLabelHair", "SegmentLabelOutfit", "SegmentLabelAccessory"
            };

            static readonly string[] SegmentFillObjectNames =
            {
                "SegmentFillHair", "SegmentFillOutfit", "SegmentFillAccessory"
            };

            static readonly string[] SegmentRimObjectNames =
            {
                "SegmentRimHair", "SegmentRimOutfit", "SegmentRimAccessory"
            };

            static readonly string[] ToneObjectNames =
            {
                "ToneOption_0", "ToneOption_1", "ToneOption_2", "ToneOption_3"
            };

            static readonly FacingDirection[] Directions =
            {
                FacingDirection.Down,
                FacingDirection.Left,
                FacingDirection.Right,
                FacingDirection.Up
            };

            // ============================================================== geometry
            //
            // Every length below is written in design points and converted by DesignTokens.Px, so it
            // can be read straight against the design document.
            //
            // Two numbers are NOT constants and are measured instead. The header, because the title
            // is Display — the largest step in the system — and whether it takes two lines or three
            // depends on the language and on how wide the canvas comes out. And the vertical budget
            // of step 2, because how tall a row is depends on the longest string in the catalogue in
            // the locale that is running. Guessing either is how a title ends up sitting on a
            // subtitle, or a list ends up showing one and a half rows.

            /// <summary>Screen gutter, left and right, for every element on this screen.</summary>
            static readonly float SideMargin = DesignTokens.Space.Gutter;

            /// <summary>Clear space above the header, inside the safe area.</summary>
            static readonly float HeaderTop = DesignTokens.Space.S12;

            /// <summary>
            /// The eyebrow shares its line with the language toggle, so the row is as tall as
            /// whichever of the two is taller. On step 2 the same row holds the Back button, which
            /// is a touch target and therefore exactly as tall as the toggle.
            /// </summary>
            static readonly float EyebrowRowHeight = Mathf.Max(DesignTokens.Px(22f), LanguageToggle.Height);

            /// <summary>
            /// The header this screen falls back to when the measurement cannot be believed: the
            /// eyebrow row, three Display lines and two body lines, which is the tallest wrap either
            /// language plausibly produces. Stacked out of the tokens the header actually uses
            /// rather than written as a round number, so it stays right if the type scale moves.
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

            /// <summary>
            /// The bottom of both steps: <b>one row holding the quiet action and the clay one, side
            /// by side</b>, over the home indicator.
            ///
            /// They used to be stacked, and stacking cost a whole touch target plus a gap — 166
            /// canvas units — of the one budget this screen has too little of. The measured
            /// consequence was that step 2's list could not show two rows of the tallest item in the
            /// catalogue on a 1080x1920 canvas no matter what else was given up. A row of two is the
            /// ordinary shape for "leave" beside "go on", it keeps both controls a full touch target
            /// tall, and the width each one gets is <i>measured from its own label</i> rather than
            /// split down the middle — see <see cref="BuildActionRow"/>, because
            /// "Doesn't matter, let's go" and "Start" are not the same size in any language.
            /// </summary>
            static readonly float ActionRowHeight = DesignTokens.Px(56f);

            /// <summary>Clear space under the action row: the home indicator's lane.</summary>
            static readonly float ActionRowBottom = DesignTokens.Space.SafeAreaBottom;

            /// <summary>The gap above the action row, and the one between the step's own bands.</summary>
            static readonly float BandGap = DesignTokens.Space.S8;

            /// <summary>What the action row and its clearances take out of a step's height.</summary>
            static readonly float ActionsBlockHeight = ActionRowBottom + ActionRowHeight + BandGap;

            /// <summary>The gap between the two controls of the action row.</summary>
            static readonly float ActionGap = DesignTokens.Space.S12;

            /// <summary>A body line's box. One line of prose, including its leading.</summary>
            static readonly float BodyLineHeight = WardrobeRow.BodyLineHeight;

            static readonly float TitleLineHeight = DesignTokens.Type.Title * DesignTokens.Type.TitleLeading;

            /// <summary>Width of the always-visible scrollbar beside a list.</summary>
            static readonly float ScrollbarWidth = WardrobeRow.ScrollbarWidth;

            /// <summary>Inset of a selection check from its control's top-right corner.</summary>
            static readonly float SelectionMarkInset = DesignTokens.Space.S4;

            // ------------------------------------------------------------- step 1 geometry

            /// <summary>
            /// Gap between the three blocks of step 1's scroll.
            ///
            /// <c>S16</c> and not <c>S24</c>, and the two steps down it took — the message band left
            /// the scroll and the gaps tightened — are what make the whole step fit inside a real
            /// phone's viewport without scrolling at all. On the 1080x1920 canvas it still scrolls,
            /// and the tone row is held above the fold on purpose; see <see cref="CardHeight"/>.
            /// </summary>
            static readonly float SectionGap = DesignTokens.Space.S16;

            /// <summary>Air after the last block, so a drag to the end is not flush to the frame.</summary>
            static readonly float ScrollTail = DesignTokens.Space.S24;

            /// <summary>
            /// The largest a character card may be. The ceiling exists because a card that fills the
            /// viewport hides the tone row entirely and the player never learns it is there.
            ///
            /// <b>There is no matching constant for the floor.</b> There used to be two — a 120-point
            /// "error under this" and a 240-point clamp — and because the clamp sat above the error
            /// the error could never fire: a negative band came out of the arithmetic and left the
            /// screen pinned at 240 with no log line. A card's floor is now derived from what the
            /// card actually has to hold, measured, in <see cref="CardMinimumHeight"/>.
            /// </summary>
            static readonly float CardCeiling = DesignTokens.Px(340f);

            /// <summary>The accent stripe along a card's top edge. Decoration; it carries no state.</summary>
            static readonly float CardAccentHeight = DesignTokens.Px(2f);

            static readonly float CardPadding = DesignTokens.Space.S12;
            static readonly float SlotLabelHeight = DesignTokens.Px(20f);

            /// <summary>
            /// A tone swatch. Taller than it is wide on a phone, because what it draws is a figure
            /// at 32:48 and a square box would letterbox it. Comfortably past the 48-point touch
            /// target on both axes.
            /// </summary>
            static readonly float ToneSwatchHeight = DesignTokens.Px(72f);

            static readonly float ToneRowHeight =
                SlotLabelHeight + DesignTokens.Space.S8 + ToneSwatchHeight;

            /// <summary>
            /// The smallest figure a character card is allowed to draw, and it is
            /// <see cref="ToneSwatchHeight"/> rather than a number of its own.
            ///
            /// The rule it encodes is comparative and therefore cannot go stale: <b>the person you
            /// are choosing is never drawn smaller than the skin tone underneath them.</b> A card
            /// that fell under this would be asking the player to pick a character off a figure
            /// smaller than a swatch, which is the point at which the choice stops being a choice
            /// between two people and becomes a choice between two words.
            /// </summary>
            static readonly float CardFigureMinHeight = ToneSwatchHeight;

            /// <summary>
            /// What has to stay visible under the cards so the player knows the step continues.
            ///
            /// The cards are capped so that this much of the tone row is inside the viewport: its
            /// label and half a swatch. A scroll view whose first block fills the fold is a scroll
            /// view nobody scrolls, and the tone is the one thing on this screen that belongs to the
            /// player rather than to the character — losing it below the fold loses decision 1b.
            /// </summary>
            static readonly float TonePeekHeight =
                SlotLabelHeight + DesignTokens.Space.S8 + 0.5f * ToneSwatchHeight;

            /// <summary>The field is a touch target, so it is at least 48 design points tall.</summary>
            static readonly float NameFieldHeight = DesignTokens.Px(52f);

            /// <summary>
            /// Everything in the name row above the hint: the slot label, the field, and the two
            /// gaps. Fixed, because all three of them are.
            ///
            /// <b>The hint is not part of this and is measured instead</b> — see
            /// <see cref="MeasureNameHint"/>. It used to be one <see cref="BodyLineHeight"/> here,
            /// which is a guess about a sentence in a language nobody laying this out is reading,
            /// and at a real phone's width the guess is wrong in both of them.
            /// </summary>
            static readonly float NameRowFixed =
                SlotLabelHeight + DesignTokens.Space.S8 + NameFieldHeight + DesignTokens.Space.S8;

            /// <summary>
            /// The three hints the row has to be able to hold. Every one of them is measured, not
            /// just the one showing: the row is built once and the sentence changes under the
            /// player's fingers. <c>Ok</c> is absent because its hint is empty.
            /// </summary>
            static readonly NameValidity[] MeasuredNameHints =
            {
                NameValidity.Unset, NameValidity.TooShort, NameValidity.TooLong
            };

            /// <summary>Horizontal padding inside the name field, at the design system's body size.</summary>
            static readonly float FieldPadding = DesignTokens.Space.S16;

            // ------------------------------------------------------------- step 2 geometry

            /// <summary>
            /// The four-facing strip's preferred height, and the height under which it stops drawing
            /// the facing captions. Above <see cref="PreviewHeightMin"/> a cell holds a figure over a
            /// caption; below it the caption band is what would be cropping the figure, so the words
            /// move to <see cref="AccessibleLabel"/> and the figure keeps the room.
            /// </summary>
            static readonly float PreviewHeightMax = DesignTokens.Px(160f);
            static readonly float PreviewHeightMin = DesignTokens.Px(120f);

            static readonly float PreviewPadding = DesignTokens.Space.S12;

            /// <summary>Headroom over a compact strip's figures, which stand on its lower edge.</summary>
            static readonly float PreviewCompactPadding = DesignTokens.Space.S4;

            /// <summary>
            /// The height under which the strip is no longer worth its band, expressed against the
            /// thumbnail every row of the list already carries.
            ///
            /// <b>The rule is comparative on purpose.</b> The strip exists to answer a question the
            /// list cannot — what the piece looks like from the side and from behind, which is where
            /// an accessory anchored to a shoulder or a back actually shows. A strip whose figures
            /// came out smaller than the thumbnail on the row beside them would be answering that
            /// question worse than the thing it sits above, and at that point the band belongs to the
            /// list. Falling under it is reported, never quietly absorbed.
            /// </summary>
            static readonly float PreviewHeightFloor =
                WardrobeRow.ThumbSize + DesignTokens.Space.S4;

            static readonly float CaptionHeight = DesignTokens.Px(20f);
            static readonly float CaptionBottom = DesignTokens.Space.S8;

            /// <summary>What the caption reserves at the bottom of a preview cell: its band plus air.</summary>
            static readonly float CaptionBandHeight =
                CaptionBottom + CaptionHeight + DesignTokens.Space.S8;

            static readonly float SegmentBarHeight = DesignTokens.Space.TouchTarget;

            /// <summary>
            /// How many rows the list must be able to show at the tallest row in the running
            /// locale. The backpack fought its way from 1.7 to 2.10 by turning its stage sideways;
            /// two is the floor below which a list stops reading as a list and starts reading as a
            /// single item with something peeking underneath.
            ///
            /// <b>This number does not move.</b> Everything else on step 2 is allocated after it —
            /// see <see cref="ApplyVerticalBudget"/>, which now hands the list its two rows first
            /// and spends what is left, instead of laying out the chrome and discovering afterwards
            /// that the list got 0.93 of a row.
            /// </summary>
            const float MinimumVisibleRows = 2.0f;

            // ============================================================== state

            /// <summary>Set when the screen runs inside a live scene; null means load the game scene.</summary>
            readonly Action _onDone;

            readonly int _sortingOrder;

            Canvas _canvas;
            GameState _state;

            /// <summary>What the name field holds. Mirrored into the save on every keystroke.</summary>
            string _name = string.Empty;

            InputField _nameField;
            Text _nameHint;

            /// <summary>The name row's own rect, positioned once the blocks above it are placed.</summary>
            RectTransform _nameRow;

            /// <summary>
            /// How tall the name row came out, <b>measured from the hint it has to hold</b>. Read by
            /// <see cref="CardHeight"/>, which is why the row is built before the cards are sized.
            /// The initial value is the one-line reserve the row had before it was measured, and it
            /// stands only for the window between construction and <see cref="BuildNameRow"/>.
            /// </summary>
            float _nameRowHeight = NameRowFixed + BodyLineHeight;

            Text _eyebrow;
            Button _back;
            Text _screenTitle;
            Text _screenSubtitle;

            RectTransform _step1;
            RectTransform _step2;

            Text _choice;
            CanvasGroup _choiceGroup;
            Coroutine _choiceFade;

            /// <summary>
            /// The band the choice message reserves above step 1's action row, <b>measured from the
            /// sentence it will hold</b> rather than reserved as a line count. Two words more in one
            /// language is the whole difference between one line and two.
            /// </summary>
            float _choiceHeight;

            Text _refusal;
            CanvasGroup _refusalGroup;
            Coroutine _refusalFade;

            /// <summary>
            /// The opaque bar the refusal is written on, inside the preview panel. Sized on every
            /// sentence, because it is the sentence that decides how tall it is.
            /// </summary>
            RectTransform _refusalBanner;

            /// <summary>The width a refusal sentence is measured against. Set when the bar is built.</summary>
            float _refusalWidth;

            Text _chosenName;

            readonly List<CardView> _cards = new List<CardView>();
            readonly List<ToneView> _tones = new List<ToneView>();

            /// <summary>The five art layers of each of the four preview facings.</summary>
            readonly Image[][] _previewLayers = new Image[4][];

            TabView[] _tabs;
            int _selectedTab = -1;

            /// <summary>Row widths for this device. Derived once, before anything is laid out.</summary>
            WardrobeRow.Metrics _rowMetrics;

            /// <summary>The content column: the canvas less a gutter on each side.</summary>
            float _contentWidth = UIKit.ReferenceWidth - 2f * DesignTokens.Space.Gutter;

            /// <summary>The safe area's height, measured or computed. Drives both steps' budgets.</summary>
            float _rootHeight = UIKit.ReferenceHeight;

            /// <summary>
            /// One character card, and everything a repaint touches. The preset id is the identity;
            /// nothing here caches a <see cref="PresetDef"/>, because a language switch reloads the
            /// presets table and a held reference would point into the old one.
            /// </summary>
            sealed class CardView
            {
                public string PresetId;
                public GameObject Root;
                public Image Ring;
                public Image Mark;
                public Image[] Layers;

                /// <summary>The suggested name, as this card shows it. Reused in the announced name.</summary>
                public string CharacterName;

                /// <summary>
                /// How tall this card's own words came out: the name's line plus the personality
                /// line's <i>measured</i> height. The row reserves the largest of them.
                /// </summary>
                public float CaptionBand;

                /// <summary>The two bands laid out once every card has measured its words.</summary>
                public RectTransform Caption;

                public RectTransform FigureArea;
            }

            /// <summary>One tone swatch: the body it draws, and the two marks that say it is chosen.</summary>
            sealed class ToneView
            {
                public int Tone;
                public GameObject Root;
                public Image Ring;
                public Image Mark;
                public Image Body;
            }

            /// <summary>One tab of step 2, and the four graphics a selection change repaints.</summary>
            sealed class TabView
            {
                public CharacterSlot Slot;

                /// <summary>The scroll view. Its GameObject <i>is</i> TabHair/TabOutfit/TabAccessory.</summary>
                public ScrollRect Scroll;

                public RectTransform Content;

                /// <summary>The clay fill behind a selected label. Absent means unselected.</summary>
                public Image Fill;

                /// <summary>The 2px clay boundary of a selected cell.</summary>
                public Image Rim;

                /// <summary>The label, which changes weight and ink with selection.</summary>
                public Text Label;

                public readonly List<WardrobeRow.View> Rows = new List<WardrobeRow.View>();

                /// <summary>
                /// Whether this tab has been shown once. A layout rebuild and a scroll-to-top only
                /// work on an active object, so both are deferred to a tab's first activation
                /// rather than run at build time on three hidden panels.
                /// </summary>
                public bool Settled;
            }

            public Composer() : this(null, StandaloneSortingOrder)
            {
            }

            public Composer(Action onDone, int sortingOrder)
            {
                _onDone = onDone;
                _sortingOrder = sortingOrder;
            }

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

            // ============================================================== building

            /// <summary>
            /// Lays both steps on one canvas and shows whichever one the run is in.
            ///
            /// The order matters in two places. The content data is loaded first, because every
            /// width and every height below is derived from strings that are not in memory until it
            /// is. And step 2 is built rows-first: how tall the tallest row comes out is what
            /// decides how much room the preview above it may keep, so the chrome cannot be placed
            /// until the list has been measured.
            /// </summary>
            public void Build()
            {
                _state = ResolveState();
                EnsureContent();

                if (_state.appearance == null)
                {
                    _state.appearance = new AppearanceState();
                }

                _name = _state.playerName ?? string.Empty;

                // The worn set and the appearance ints are recomposed once on open, so the figures
                // below are drawn from the same state the world character is drawn from. Idempotent:
                // an empty worn set changes nothing, which is why it is safe on a fresh save, and it
                // is what makes a mid-creation language switch come back showing the swaps the
                // player had already made.
                Wardrobe.ApplyToAppearance(_state);

                Canvas canvas = UIKit.CreateCanvas("CharacterCreationCanvas", _sortingOrder);
                _canvas = canvas;
                RectTransform root = UIKit.SafeArea(canvas);

                Image background = UIKit.CreatePanel(root, "Background", DesignTokens.Surface.Background);
                UIKit.Stretch((RectTransform)background.transform);

                // Bled past the safe area on purpose. The screen background is the bottom of the
                // stack, and stopping it at the inset leaves a strip of whatever is behind the
                // canvas along the camera housing, which reads as a rendering fault.
                UIKit.Bleed(background);

                RecomputeMetrics();

                float headerHeight = BuildHeader(root);

                BuildStep1(root, HeaderTop + headerHeight + DesignTokens.Space.S16);
                BuildStep2(root);

                // Step 2 when the run already knows its character. That is what makes a language
                // switch — which reloads the scene and rebuilds this screen — land the player where
                // they were instead of back at the choice.
                ShowStep(CharacterPresets.Get(_state.characterId) != null);
            }

            /// <summary>
            /// Loads the catalogue and the presets if nothing has.
            ///
            /// The boot sequence normally does both long before this screen appears; this covers a
            /// scene opened directly in the editor, and answers the otherwise silent failure where
            /// creation comes up with no characters and no wardrobe.
            /// </summary>
            static void EnsureContent()
            {
                if (CharacterCatalog.Items == null || CharacterCatalog.Items.Length == 0)
                {
                    Debug.LogWarning("[CharacterCreation] The character catalogue was not loaded before " +
                                     "creation opened; loading it now. The boot sequence should have done this.");
                    CharacterCatalog.LoadAll();
                }

                if (CharacterPresets.All == null || CharacterPresets.All.Length == 0)
                {
                    Debug.LogWarning("[CharacterCreation] The character presets were not loaded before " +
                                     "creation opened; loading them now. The boot sequence should have done this.");
                    CharacterPresets.LoadAll();
                }
            }

            /// <summary>
            /// Re-derives every horizontal measurement, and the screen's height, from what this
            /// device actually gives the layout. Called once, before anything is laid out.
            ///
            /// <b>The chain starts at <see cref="UIKit.CanvasWidth"/> and never at
            /// <see cref="UIKit.ReferenceWidth"/>.</b> The canvas is 1080 units wide only on a
            /// device whose aspect is exactly 1080x1920; an iPhone 17 Pro reports about 977, and
            /// every width pinned to the reference overflows its parent by the difference — which
            /// clips descriptions mid-word and pushes the fourth preview cell off the screen.
            /// <c>tools/e2e.sh</c> launches the macOS player at exactly 1080x1920, where the two
            /// numbers agree, so it structurally cannot see this.
            ///
            /// <b>Nothing here reads a RectTransform.</b> It cannot: this runs on the frame the
            /// canvas is created, before the <see cref="UnityEngine.UI.CanvasScaler"/> has set the
            /// canvas's scale factor, so every rect under it is still measured in raw screen pixels.
            /// Both axes therefore come from <see cref="Screen.safeArea"/>'s share of
            /// <see cref="Screen.width"/> and <see cref="Screen.height"/> — a ratio, which is the
            /// one thing about the inset that is true before a layout pass — applied to the scaler's
            /// own formula. That is what a rect would report one frame later.
            /// </summary>
            void RecomputeMetrics()
            {
                // The safe area's horizontal inset, applied as the ratio it is rather than read off
                // the rect it will eventually produce.
                //
                // <b>This used to take Mathf.Min of the canvas width and root.rect.width, and that
                // reading is wrong on every phone.</b> The rect of a canvas created this frame is
                // still in raw screen pixels: the CanvasScaler has not run, so canvas.scaleFactor is
                // 1 and a 402-point-wide phone reports a safe area 402 units across instead of the
                // 977 it will have one frame later. The min then took the pixel count, every width
                // downstream was derived from a third of the screen, and WardrobeRow.MetricsFor came
                // out with a NEGATIVE text column — item names breaking mid-word, a character card
                // needing more than the viewport, and the action button drawing one letter per line.
                // It survived every run of tools/e2e.sh because the macOS player is exactly
                // 1080x1920, where the pixel count and the canvas unit are the same number.
                //
                // Screen.safeArea and Screen.width are both in pixels, so their ratio is the one
                // thing about the inset that is true before any layout has happened — which is the
                // same technique SafeAreaHeight() already uses on the other axis, for the same
                // reason. On a screen with no inset the ratio is 1 and this is CanvasWidth()
                // unchanged, which is why the desktop numbers do not move.
                float available = UIKit.CanvasWidth();

                Rect safe = Screen.safeArea;
                if (safe.width > 1f && Screen.width > 1f)
                {
                    available *= safe.width / Screen.width;
                }

                _contentWidth = available - 2f * SideMargin;
                _rowMetrics = WardrobeRow.MetricsFor(_contentWidth);

                // The height comes from the same formula for the same reason, and no longer from
                // root.rect.height at all.
                //
                // The reading it replaces was guarded by a plausible range rather than by anything
                // that knew what units it was in, and raw pixels walk straight through that guard on
                // real hardware: an iPhone 17 Pro reports 2622 pixels tall, so its unscaled safe-area
                // rect lands inside the 1108-3877 band the range allowed and was accepted as canvas
                // units — about a quarter too tall, which is a quarter of step 2's budget spent on
                // room that is not there. SafeAreaHeight() is the scaler's own formula scaled by the
                // inset's share of the screen, so it is what the rect would have reported one frame
                // later, and it needs no range to be believed.
                _rootHeight = SafeAreaHeight();
            }

            /// <summary>
            /// The height of the safe area in canvas units, from the scaler's own formula rather
            /// than from a rect.
            ///
            /// This is <see cref="UIKit.CanvasWidth"/>'s companion: the same log-space lerp between
            /// the two axis ratios, taken on the height instead of the width, then scaled by the
            /// share of the screen the safe area occupies. It exists so that a screen composed
            /// before its first layout pass still has a believable number to budget against.
            /// </summary>
            static float SafeAreaHeight()
            {
                float width = Screen.width;
                float height = Screen.height;
                if (width <= 0f || height <= 0f)
                {
                    return UIKit.ReferenceHeight;
                }

                float logWidth = Mathf.Log(width / UIKit.ReferenceWidth, 2f);
                float logHeight = Mathf.Log(height / UIKit.ReferenceHeight, 2f);
                float scale = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, UIKit.CanvasMatch));
                float canvasHeight = scale > 0f ? height / scale : UIKit.ReferenceHeight;

                Rect safe = Screen.safeArea;
                if (safe.height > 1f && height > 1f)
                {
                    canvasHeight *= safe.height / height;
                }

                return canvasHeight;
            }

            // ============================================================== the shared header

            /// <summary>
            /// Builds the eyebrow row, the title and the subtitle as a measured column, and returns
            /// how tall they came out on step 1.
            ///
            /// <b>The eyebrow row belongs to neither step.</b> It is built once and stays up
            /// throughout, because the language toggle has to be reachable on both: this is the only
            /// setup moment the game has, and if the system language guessed wrong this is where the
            /// player finds out. What swaps inside the row is which control sits on the left — the
            /// eyebrow on step 1, the Back button on step 2 — so the toggle never moves under the
            /// finger reaching for it.
            ///
            /// The height is measured rather than declared because the title is Display — 34 design
            /// points — and the number of lines it takes is decided by the language and by the
            /// canvas width, neither of which this file can know. A constant tuned against the
            /// Portuguese title in the desktop player is a constant that clips the English one on a
            /// phone, and a screen builder cannot see that happen.
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

                RectTransform languageRow = LanguageToggle.Create(eyebrowRow);
                languageRow.anchorMin = new Vector2(1f, 0.5f);
                languageRow.anchorMax = new Vector2(1f, 0.5f);
                languageRow.pivot = new Vector2(1f, 0.5f);
                languageRow.sizeDelta = new Vector2(LanguageToggle.Width, LanguageToggle.Height);
                languageRow.anchoredPosition = Vector2.zero;

                // The eyebrow stops short of the toggle rather than guessing at a margin: it is left
                // aligned and the toggle is right aligned on the same line, and an eyebrow with
                // two-letter buttons sitting on top of it is the first thing a player sees.
                _eyebrow = UIKit.CreateText(eyebrowRow, "Eyebrow", Loc.T(EyebrowKey),
                    DesignTokens.Type.Mono, DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft,
                    DesignTokens.TypeRole.Mono);
                UIKit.Stretch((RectTransform)_eyebrow.transform, 0f,
                    LanguageToggle.Width + DesignTokens.Space.S12, 0f, 0f);

                // Same line, same left edge, and a full touch target. Going back changes nothing
                // else: the character, the tone, the name and every swap stay exactly as they were.
                _back = UIKit.CreateButton(eyebrowRow, "BackToCharacter", Loc.T(BackKey),
                    UIKit.ButtonVariant.Secondary, () => ShowStep(false));
                var backRect = (RectTransform)_back.transform;
                backRect.anchorMin = new Vector2(0f, 0.5f);
                backRect.anchorMax = new Vector2(0f, 0.5f);
                backRect.pivot = new Vector2(0f, 0.5f);
                backRect.sizeDelta = new Vector2(BackButtonWidth(_back), DesignTokens.Space.TouchTarget);
                backRect.anchoredPosition = Vector2.zero;
                _back.gameObject.SetActive(false);

                // The chosen character's name rides in the middle of this row rather than on a line
                // of its own at the top of step 2. That line cost a full Title box plus its gap —
                // 100 canvas units — out of the one budget step 2 has too little of, and it was
                // spending it on a word that fits in a gap the language toggle and the Back button
                // leave empty on every device. It is centred between the two of them, and its box
                // stops short of both: a name long enough to reach either control would otherwise
                // draw underneath it.
                _chosenName = UIKit.CreateText(eyebrowRow, "ChosenName", string.Empty,
                    DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.MiddleCenter,
                    DesignTokens.TypeRole.Title);
                UIKit.Stretch((RectTransform)_chosenName.transform,
                    BackButtonWidth(_back) + DesignTokens.Space.S12,
                    LanguageToggle.Width + DesignTokens.Space.S12, 0f, 0f);
                _chosenName.gameObject.SetActive(false);

                // Upper aligned, both of them. Inside a vertical group the block grows downward as
                // it wraps, and a middle-aligned title that gains a line would climb into the row
                // above it as well as pushing the one below.
                _screenTitle = UIKit.CreateText(header, "Title", Loc.T(TitleKey),
                    DesignTokens.Type.Display, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                    DesignTokens.TypeRole.Display);

                _screenSubtitle = UIKit.CreateText(header, "Subtitle", Loc.T(SubtitleKey),
                    DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);

                UIKit.RebuildNow(header);

                // Checked, not trusted, and loudly. A layout that ran before the canvas had a size
                // does not report a wrong-looking number, it reports a plausible one, and a header
                // measured at the wrong width either puts the scroll view over the title or leaves a
                // third of the screen empty — neither of which logs anything on its own. This is the
                // failure mode this project has already shipped twice.
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

            /// <summary>
            /// How wide the Back button has to be for its own word, <b>measured rather than
            /// estimated</b>.
            ///
            /// "Voltar" and "Back" are different lengths and a third locale is a third length, so
            /// the width is read off the label the kit just built — the same measure-do-not-assume
            /// move the NOVO badge makes for the same reason. A per-character estimate would be one
            /// magic number that happens to fit the two words in the tree today. Never narrower than
            /// a touch target.
            ///
            /// It is <see cref="LabelWidth"/> under a name that says which button, because two of
            /// the three widths on this screen that come from a word are read the same way and a
            /// second copy of the arithmetic is a second thing to keep in step.
            /// </summary>
            static float BackButtonWidth(Button back)
            {
                return LabelWidth(back);
            }

            // ============================================================== step 1

            /// <summary>
            /// Who you are: the two characters, the tone that stays yours, and your name.
            ///
            /// <b>Built bottom-up, and the order is the fix.</b> The action row and the message band
            /// under the scroll are built first, because how tall the message comes out in this
            /// locale is what decides where the scroll view ends — and the version this replaced
            /// reserved two lines for it <i>inside</i> the scroll, where the one sentence that makes
            /// the primary button explain itself could scroll off the bottom of the screen. Tapping
            /// Continuar with nothing chosen then did nothing and said nothing, which on the first
            /// screen of the game is the worst failure this file has ever had.
            ///
            /// Everything between the header and the action row is one scroll view, and on a short
            /// canvas it genuinely scrolls: the cards are capped so that <see cref="TonePeekHeight"/>
            /// of the tone row is always above the fold, which is the affordance that says so.
            /// </summary>
            void BuildStep1(RectTransform root, float top)
            {
                _step1 = UIKit.CreateRect("Step1", root);
                UIKit.Stretch(_step1);

                BuildActionRow(_step1, QuickStartFromChoice, "ToCustomise", ContinueKey, ToCustomise);
                BuildChoiceMessage(_step1);

                float bottom = ActionsBlockHeight + _choiceHeight + BandGap;

                ScrollRect options = UIKit.CreateScrollView(_step1, "Options", out RectTransform content);
                var optionsRect = (RectTransform)options.transform;
                optionsRect.anchorMin = new Vector2(0f, 0f);
                optionsRect.anchorMax = new Vector2(1f, 1f);
                optionsRect.offsetMin = new Vector2(0f, bottom);
                optionsRect.offsetMax = new Vector2(0f, -top);

                // The helper lays its content out with a vertical group; this step positions by hand,
                // so the group and its fitter are switched off rather than fought.
                VerticalLayoutGroup group = content.GetComponent<VerticalLayoutGroup>();
                if (group != null)
                {
                    group.enabled = false;
                }

                ContentSizeFitter contentFitter = content.GetComponent<ContentSizeFitter>();
                if (contentFitter != null)
                {
                    contentFitter.enabled = false;
                }

                float viewport = _rootHeight - top - bottom;

                // Cards first, at whatever height the step can afford, and the height is decided from
                // what the cards themselves measure — see BuildCharacterChoice, which builds the two
                // of them, reads the tallest caption off the strings this locale actually has, and
                // reports back how tall a card had to be.
                // The name row is built first and placed last, and the inversion is the same one
                // step 2 makes: how tall it comes out is an input to how tall a card may be, because
                // CardHeight takes the name row out of the band before the cards get theirs. Its
                // hint wraps to two lines at a real phone's width in both locales, and a card sized
                // against a one-line reserve is a card sized against a row that does not exist.
                // Where the row sits is decided below, once the cards and the tone row have taken
                // their cursor; sibling order inside the scroll decides draw order only, and nothing
                // in this column overlaps.
                BuildNameRow(content);

                float cardHeight = BuildCharacterChoice(content, 0f, viewport);

                float cursor = cardHeight + SectionGap;

                BuildToneRow(content, cursor);
                cursor += ToneRowHeight + SectionGap;

                UIKit.AnchorTop(_nameRow, _nameRowHeight, SideMargin, SideMargin, cursor);
                cursor += _nameRowHeight + ScrollTail;

                content.sizeDelta = new Vector2(0f, cursor);

                // Permanent, not auto-hiding. There is content below the fold on a short device and
                // nothing else on the screen says so; the bar sits in the gutter the content already
                // leaves free, so it costs no width.
                UIKit.AttachVerticalScrollbar(options, ScrollbarWidth);
            }

            /// <summary>
            /// How tall a character card may be, given the viewport and what a card has to hold.
            ///
            /// Three numbers, in this order:
            /// <list type="number">
            /// <item><b>The band</b> — what the scroll has left once the tone row, the name row and
            /// the gaps have taken theirs. It can come out negative, and on a 1080x1920 canvas it
            /// does.</item>
            /// <item><b>The floor</b> — <paramref name="minimum"/>, derived in
            /// <see cref="CardMinimumHeight"/> from the card's own measured caption. Below it the
            /// card cannot draw what it has to draw, and that is the only condition worth an error
            /// here.</item>
            /// <item><b>The ceiling</b> — the design ceiling, or whatever leaves
            /// <see cref="TonePeekHeight"/> of the tone row above the fold, whichever is smaller.
            /// </item>
            /// </list>
            ///
            /// <b>The band being smaller than the floor is not an error.</b> This is a scroll view
            /// with a permanent scrollbar; a step whose content is taller than its viewport is what
            /// a scroll view is for, and the tone row peeking under the cards is what tells the
            /// player so. What the version this replaced did was worse than either: it clamped the
            /// negative band up to a 240-point preference that sat <i>above</i> its own 120-point
            /// error threshold, so the error could not fire, the cards were oversized for the space,
            /// and the name field went under the fold with nothing above it saying there was more.
            /// A clamp hiding a negative is an exception caught and carried on with.
            /// </summary>
            float CardHeight(float viewport, float minimum)
            {
                float band = viewport - (ToneRowHeight + _nameRowHeight + 2f * SectionGap + ScrollTail);

                if (viewport < minimum)
                {
                    Debug.LogError("[CharacterCreation] Step 1 has a viewport of " + viewport +
                                   " units and a character card needs at least " + minimum +
                                   " to hold a figure over a name over its personality line. The card " +
                                   "cannot be shown whole at all on this canvas, so the choice the " +
                                   "step exists for is being made off a cropped figure. The header or " +
                                   "the action row has to give a band back — never the type, and " +
                                   "never the figure below " + CardFigureMinHeight + ".");
                }

                float ceiling = Mathf.Min(CardCeiling, viewport - SectionGap - TonePeekHeight);
                if (ceiling < minimum)
                {
                    // The peek cannot be honoured and the card's own floor wins: a card too short to
                    // read is a worse trade than a tone row that starts below the fold, and the
                    // scrollbar is still there to say the step continues.
                    ceiling = minimum;
                }

                return Mathf.Clamp(band, minimum, ceiling);
            }

            /// <summary>
            /// The two characters, side by side.
            ///
            /// Built by walking <see cref="CharacterPresets.Ids"/> rather than from two hardcoded
            /// ids, so a third character is a change to <c>character_presets.json</c> and nothing
            /// else. <b>Nothing is pre-selected.</b> <see cref="CharacterPresets.Default"/> exists,
            /// and pre-selecting it here would privilege whichever character happens to be authored
            /// first — which its own doc comment says the code must not do. The player who does not
            /// want to choose has <c>QuickStart</c>, which is the honest form of a default: a button
            /// that dresses you and moves on, rather than a decision made quietly on your behalf.
            /// </summary>
            /// <returns>How tall the row of cards came out, so the caller can carry on below it.</returns>
            float BuildCharacterChoice(RectTransform content, float top, float viewport)
            {
                RectTransform row = UIKit.CreateRect("CharacterChoice", content);

                string[] ids = CharacterPresets.Ids();
                if (ids.Length == 0)
                {
                    Debug.LogError("[CharacterCreation] character_presets.json has no characters, so the " +
                                   "first step of creation has nothing to choose between. QuickStart is " +
                                   "the only way past this screen.");
                    UIKit.AnchorTop(row, 0f, SideMargin, SideMargin, top);
                    return 0f;
                }

                float cardWidth = (_contentWidth - DesignTokens.Space.S12) / ids.Length;
                float textWidth = cardWidth - 2f * CardPadding;

                // Pass one: build the cards and let each one measure its own caption against the
                // strings this locale actually has. The tallest of them is what every card reserves,
                // so the two figures sit on the same line as each other whatever their personality
                // lines came out at.
                var built = new List<CardView>();
                float captionBand = 0f;

                for (int i = 0; i < ids.Length; i++)
                {
                    CardView view = BuildCharacterCard(row, ids[i], i, ids.Length, textWidth);
                    built.Add(view);
                    captionBand = Mathf.Max(captionBand, view.CaptionBand);
                }

                // Pass two: the caption is measured, so the card's own floor is known, so the row can
                // be given a height and every card can be told where its figure ends.
                float cardHeight = CardHeight(viewport, CardMinimumHeight(captionBand));
                UIKit.AnchorTop(row, cardHeight, SideMargin, SideMargin, top);

                for (int i = 0; i < built.Count; i++)
                {
                    LayOutCard(built[i], captionBand);
                    _cards.Add(built[i]);
                }

                RefreshCards();
                return cardHeight;
            }

            /// <summary>
            /// The shortest a card can be and still hold what a card holds: its padding, the accent
            /// stripe, a figure no smaller than a tone swatch, and the measured caption under it.
            ///
            /// Written as the sum of the bands it is made of rather than as a round number, so it
            /// stays right when the type scale moves or a personality line gains a word.
            /// </summary>
            static float CardMinimumHeight(float captionBand)
            {
                return CardPadding + CardAccentHeight + DesignTokens.Space.S8 +
                       CardFigureMinHeight +
                       DesignTokens.Space.S8 + captionBand + CardPadding;
            }

            /// <summary>
            /// One character: their figure facing the player, their name, and the one line that says
            /// who they are.
            ///
            /// The figure is drawn from a throwaway <see cref="AppearanceState"/> so that looking at
            /// a character costs nothing — nothing in this method touches the run. The tone on that
            /// throwaway is the player's current one, which is what makes both cards redraw when a
            /// swatch is tapped: the question the card answers is "what would I look like as this
            /// person", and answering it on somebody else's skin would be a strange thing to show.
            ///
            /// The card is a Secondary button wearing the panel's frame. Its <i>colour</i> is left
            /// to the variant on purpose: <see cref="VariantButton"/> owns that colour across seven
            /// states and repaints it on the next pointer event, so a panel colour written here
            /// would survive exactly until the finger moved, and a panel-coloured child drawn over
            /// it would swallow the hover and pressed states the design system requires every
            /// control to show.
            ///
            /// <b>The personality line is measured, not assumed.</b> It used to reserve two body
            /// lines and pin the label to them, and Adar's "Carregador de pedras. Fala pouco." takes
            /// three at this width — so the word "pouco." painted outside the card, on top of the
            /// "Pele" label, on the first screen of the game. A reserved line count is a guess about
            /// a string in a language nobody writing the layout is reading; <see cref="WardrobeRow.PinText"/>
            /// asks the label how tall it is in the box it will have and pins it to the answer,
            /// which is the same anti-clipping move every other measured band on this screen makes.
            /// </summary>
            CardView BuildCharacterCard(RectTransform row, string presetId, int index, int count,
                                        float textWidth)
            {
                PresetDef preset = CharacterPresets.Get(presetId);

                Button card = UIKit.CreateButton(row, "CharacterCard_" + presetId, string.Empty,
                    UIKit.ButtonVariant.Secondary, () => SelectCharacter(presetId));
                var cardRect = (RectTransform)card.transform;
                cardRect.anchorMin = new Vector2(index / (float)count, 0f);
                cardRect.anchorMax = new Vector2((index + 1) / (float)count, 1f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.offsetMin = new Vector2(index == 0 ? 0f : DesignTokens.Space.S12 * 0.5f, 0f);
                cardRect.offsetMax = new Vector2(index == count - 1 ? 0f : -DesignTokens.Space.S12 * 0.5f, 0f);

                Image surface = card.GetComponent<Image>();
                if (surface != null)
                {
                    surface.sprite = UIKit.GetSprite(UIKit.FrameFor(UIKit.CardStyle.Panel));
                    surface.type = UIKit.TypeFor(surface.sprite);
                }

                // The character's accent, along the top edge. Decoration and nothing else: it never
                // says chosen, never says locked, and never changes.
                Image accent = UIKit.CreatePanel(cardRect, "Accent",
                    preset != null ? preset.Accent : DesignTokens.Ink.Secondary, null);
                UIKit.AnchorTop((RectTransform)accent.transform, CardAccentHeight,
                    CardPadding, CardPadding, CardPadding);
                accent.raycastTarget = false;
                UIKit.Layout(accent).ignoreLayout = true;

                string characterName = preset != null && !string.IsNullOrWhiteSpace(preset.suggested_name)
                    ? preset.suggested_name
                    : presetId;

                string personality = preset != null ? preset.personality : null;

                RectTransform caption = UIKit.CreateRect("Caption", cardRect);
                UIKit.Layout(caption).ignoreLayout = true;
                UIKit.VerticalGroup(caption.gameObject, DesignTokens.Space.S4, new RectOffset(),
                    TextAnchor.UpperCenter);

                Text nameLabel = UIKit.CreateText(caption, "Name", characterName, DesignTokens.Type.Title,
                    DesignTokens.Ink.Primary, TextAnchor.UpperCenter, DesignTokens.TypeRole.Title);
                WardrobeRow.PinTextBox(nameLabel, textWidth, TitleLineHeight);

                float captionBand = TitleLineHeight;

                if (!string.IsNullOrWhiteSpace(personality))
                {
                    Text personalityLabel = UIKit.CreateText(caption, "Personality", personality,
                        DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperCenter,
                        DesignTokens.TypeRole.Body);
                    captionBand += DesignTokens.Space.S4 + WardrobeRow.PinText(personalityLabel, textWidth);
                }

                RectTransform figureArea = UIKit.CreateRect("FigureArea", cardRect);
                UIKit.Layout(figureArea).ignoreLayout = true;

                RectTransform figure = CharacterFigure.CreateFigureRect(figureArea, "Figure");
                Image[] layers = CharacterFigure.Build(figure);

                Image ring = BuildSelectionRing(cardRect);
                Image mark = BuildSelectionMark(cardRect);

                return new CardView
                {
                    PresetId = presetId,
                    Root = card.gameObject,
                    Ring = ring,
                    Mark = mark,
                    Layers = layers,
                    CharacterName = characterName,
                    CaptionBand = captionBand,
                    Caption = caption,
                    FigureArea = figureArea
                };
            }

            /// <summary>
            /// Gives a built card its two bands, once the tallest caption across all the cards is
            /// known. Split out of <see cref="BuildCharacterCard"/> because a card cannot be told
            /// where its figure ends until every card has measured its own words.
            /// </summary>
            static void LayOutCard(CardView view, float captionBand)
            {
                if (view == null)
                {
                    return;
                }

                if (view.Caption != null)
                {
                    UIKit.AnchorBottom(view.Caption, captionBand, CardPadding, CardPadding, CardPadding);
                }

                if (view.FigureArea != null)
                {
                    UIKit.Stretch(view.FigureArea, CardPadding, CardPadding,
                        CardPadding + CardAccentHeight + DesignTokens.Space.S8,
                        CardPadding + captionBand + DesignTokens.Space.S8);
                }
            }

            /// <summary>
            /// Skin tone: four swatches, and nothing that ranks them.
            ///
            /// No swatch carries a word, a star or a "recommended". All four are the same size with
            /// the same spacing, and the ring that marks the chosen one is the same ring a worn
            /// piece and a chosen character get. Four unlabelled swatches told apart by colour alone
            /// would break rule 2, so the ring is joined by a check.
            ///
            /// <b>The one thing the design cannot fix</b> is that a fresh save opens on tone 0,
            /// because <see cref="GameState.NewGame"/> leaves it there and some figure has to be
            /// drawn. Randomising the opening tone would dissolve that and also make every e2e
            /// screenshot non-deterministic; the trade belongs to whoever owns the save, not to this
            /// screen.
            ///
            /// Each swatch draws build 0's body at that tone rather than the chosen character's.
            /// Build 0's four variants are the ones guaranteed to exist, and the swatch's job is to
            /// show the tone rather than the person — the cards above are where the tone is seen on
            /// the character, and they redraw as soon as this is tapped.
            /// </summary>
            void BuildToneRow(RectTransform content, float top)
            {
                RectTransform row = UIKit.CreateRect("ToneRow", content);
                UIKit.AnchorTop(row, ToneRowHeight, SideMargin, SideMargin, top);

                Text label = UIKit.CreateText(row, "ToneLabel", Loc.T(ToneLabelKey),
                    DesignTokens.Type.Minimum, DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft,
                    DesignTokens.TypeRole.BodyStrong);
                UIKit.AnchorTop((RectTransform)label.transform, SlotLabelHeight, 0f, 0f, 0f);

                RectTransform swatches = UIKit.CreateRect("ToneSwatches", row);
                UIKit.AnchorBottom(swatches, ToneSwatchHeight, 0f, 0f, 0f);

                int count = AppearanceState.SkinTones;
                for (int i = 0; i < count && i < ToneObjectNames.Length; i++)
                {
                    _tones.Add(BuildToneSwatch(swatches, i, count));
                }

                RefreshTones();
            }

            ToneView BuildToneSwatch(RectTransform parent, int tone, int count)
            {
                Button swatch = UIKit.CreateButton(parent, ToneObjectNames[tone], string.Empty,
                    UIKit.ButtonVariant.Secondary, () => SelectTone(tone));

                var rect = (RectTransform)swatch.transform;
                rect.anchorMin = new Vector2(tone / (float)count, 0f);
                rect.anchorMax = new Vector2((tone + 1) / (float)count, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                // Half the gap on each side of a shared edge, none against the row's own edges.
                rect.offsetMin = new Vector2(tone == 0 ? 0f : DesignTokens.Space.TouchGap * 0.5f, 0f);
                rect.offsetMax = new Vector2(tone == count - 1 ? 0f : -DesignTokens.Space.TouchGap * 0.5f, 0f);

                RectTransform figureArea = UIKit.CreateRect("FigureArea", rect);
                UIKit.Stretch(figureArea, DesignTokens.Space.S4, DesignTokens.Space.S4,
                    DesignTokens.Space.S4, DesignTokens.Space.S4);
                UIKit.Layout(figureArea).ignoreLayout = true;

                RectTransform figure = CharacterFigure.CreateFigureRect(figureArea, "Figure");
                Image body = CharacterFigure.BuildLayer(figure, "Body");

                // Build 0 with this tone: the packing AppearanceState documents, written out here
                // because the swatch has no AppearanceState of its own to ask.
                CharacterFigure.ApplyBody(body, 0 * AppearanceState.SkinTones + tone, tone,
                    FacingDirection.Down);

                Image ring = BuildSelectionRing(rect);
                Image mark = BuildSelectionMark(rect);

                return new ToneView { Tone = tone, Root = swatch.gameObject, Ring = ring, Mark = mark, Body = body };
            }

            /// <summary>
            /// The name field, and the line under it that says what a blank one means.
            ///
            /// <b>Every keystroke is written straight into the run.</b> A language switch reloads the
            /// scene and rebuilds this screen, and a name held only in a local field would be gone.
            /// A blank name is allowed and blocks nothing: <see cref="CharacterPresets.NameHint"/>
            /// says the valley will call you by the fallback word, which is information rather than
            /// an error.
            ///
            /// <b>The row is built here and positioned by the caller</b>, because how tall it comes
            /// out is measured rather than declared and <see cref="CardHeight"/> needs the answer
            /// before the cards above it can be sized. It leaves <see cref="_nameRow"/> and
            /// <see cref="_nameRowHeight"/> behind; <see cref="BuildStep1"/> anchors it once the
            /// cards and the tone row have taken their share of the column.
            /// </summary>
            void BuildNameRow(RectTransform content)
            {
                RectTransform row = UIKit.CreateRect("NameRow", content);
                _nameRow = row;

                Text label = UIKit.CreateText(row, "Label", Loc.T(NameLabelKey),
                    DesignTokens.Type.Minimum, DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft,
                    DesignTokens.TypeRole.BodyStrong);
                UIKit.AnchorTop((RectTransform)label.transform, SlotLabelHeight, 0f, 0f, 0f);

                _nameField = UIKit.CreateInputField(row, "NameInput", Loc.T(NamePlaceholderKey),
                    CharacterPresets.MaxNameLength, OnNameTyped);
                UIKit.AnchorTop((RectTransform)_nameField.transform, NameFieldHeight, 0f, 0f,
                    SlotLabelHeight + DesignTokens.Space.S8);

                // The helper builds the field on the old panel sprite and the old body size, because
                // it is shared with screens that have not moved yet. Restyling it here is what a
                // call site can do without changing the kit under those screens.
                var fieldBackground = _nameField.GetComponent<Image>();
                if (fieldBackground != null)
                {
                    fieldBackground.sprite = UIKit.GetSprite(UiSpriteKeys.FrameMd);
                    fieldBackground.type = UIKit.TypeFor(fieldBackground.sprite);
                    fieldBackground.color = DesignTokens.Surface.Card;
                }

                StyleFieldText(_nameField.textComponent, DesignTokens.Ink.Primary);
                StyleFieldText(_nameField.placeholder as Text, DesignTokens.Ink.Secondary);

                _nameField.text = _name;

                // UpperLeft and not MiddleLeft, now that the box can be taller than the sentence in
                // it. The box is sized for the tallest of the three hints and the shortest of them
                // is one line, so a middle-aligned hint would sit half a line lower when the player
                // types two characters than it did when the field was empty — the line moving on its
                // own, under the finger, for no reason the player can see. Anchored to the top of
                // the band, it stays the same distance under the field whatever it says.
                _nameHint = UIKit.CreateText(row, "NameHint", string.Empty, DesignTokens.Type.Body,
                    DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);

                float hintHeight = MeasureNameHint();
                _nameRowHeight = NameRowFixed + hintHeight;

                UIKit.AnchorTop(row, _nameRowHeight, SideMargin, SideMargin, 0f);
                UIKit.AnchorBottom((RectTransform)_nameHint.transform, hintHeight, 0f, 0f, 0f);

                // Measuring pinned the label to a LayoutElement carrying the last sentence asked
                // about, which is not the band it ended up with. Nothing on this row is laid out by
                // a group, so that element is inert either way — saying so here is what stops the
                // next person who adds a group to this row from inheriting a stale height.
                UIKit.Layout(_nameHint).ignoreLayout = true;
            }

            /// <summary>
            /// How much room the hint under the name field needs in this locale at this width: the
            /// tallest of every sentence it can hold, never under one <see cref="BodyLineHeight"/>.
            ///
            /// <b>Measured, for the same reason a card's personality line is measured.</b> The
            /// reserve was one body line, and one body line is a guess about a string in a language
            /// nobody writing the layout is reading. At 402x874 — a real iPhone, and about a hundred
            /// canvas units narrower than the 1080-wide reference the desktop e2e player runs at —
            /// the unset hint wraps in both locales and the second line was cropped: pt-BR lost
            /// "viajante." and en lost "traveller.", which is the fallback word the sentence exists
            /// to say. A truncated sentence is not a cosmetic defect.
            ///
            /// <b>All three hints are measured and not just the one showing.</b> The row is built
            /// once and the sentence changes on every keystroke, so a box sized for
            /// "Use ao menos 2 caracteres." would crop the unset line the moment the field is
            /// cleared. The fourth validity has no sentence at all and is covered by the floor.
            /// </summary>
            float MeasureNameHint()
            {
                if (_nameHint == null)
                {
                    return BodyLineHeight;
                }

                float tallest = BodyLineHeight;

                for (int i = 0; i < MeasuredNameHints.Length; i++)
                {
                    _nameHint.text = CharacterPresets.NameHint(MeasuredNameHints[i]);
                    tallest = Mathf.Max(tallest, WardrobeRow.PinText(_nameHint, _contentWidth));
                }

                // The measuring left the label holding the last sentence it was asked about, and
                // pinned to a box of that sentence's height. Both are put back: the text by the one
                // method that owns what this label says, the box by the caller's AnchorBottom.
                RefreshNameHint();
                return tallest;
            }

            /// <summary>
            /// Puts the design system's body type on one of the field's two Text children, and
            /// re-insets it: the helper's padding was measured against a smaller face, and at the
            /// current size a name starts almost against the corner radius.
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

            /// <summary>
            /// The band that answers <c>ToCustomise</c> when nothing is chosen yet.
            ///
            /// <b>Pinned directly above the action row, outside the scroll.</b> It used to be the
            /// last block <i>inside</i> step 1's scrolling column, and on any canvas where the step
            /// overflowed — which is every canvas, since the cards, the tone row and the name row
            /// together are taller than a 1080x1920 viewport — the sentence arrived below the fold.
            /// The observed result on a device was a primary call to action that answered a tap with
            /// no ring, no message and no movement, twice in a row, on the first screen of the game.
            /// A message that explains a button belongs beside the button.
            ///
            /// Reserved and empty rather than grown on demand, so the button the player just tapped
            /// does not move — and the reserve is <b>measured from the sentence</b> rather than
            /// counted in lines, because it is one line in one language and can be two in the next.
            /// The sentence arrives on a fade, and <b>that fade keeps running under reduced
            /// motion</b>: the design system suppresses parallax, pulse and shake, but a fade is
            /// information, and a player who taps and sees nothing move cannot tell whether the tap
            /// registered.
            ///
            /// <c>Ink.Secondary</c> and never <c>Feedback.Error</c>. Nothing went wrong: the player
            /// asked to go on before answering the one question this step asks, and the screen is
            /// telling them what it still needs. A scold here would be rule 7 broken in the first
            /// ten seconds of the game.
            /// </summary>
            void BuildChoiceMessage(RectTransform parent)
            {
                // The label IS the band, rather than a Text inside a wrapper rect. That is not
                // tidiness: the end-to-end run reads this line with TextOf("ChoiceMessage"), which
                // asks the object of that name for its own Text component and finds nothing on a
                // wrapper. Same shape as RefusalMessage, for the same reason.
                //
                // Built holding the sentence so that it can be measured against the width it will
                // have, then emptied. Measuring an empty label answers with the height of nothing.
                _choice = UIKit.CreateText(parent, "ChoiceMessage", Loc.T(ChooseFirstKey),
                    DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);

                // Anchors collapsed to the parent's bottom-left corner before the question is asked,
                // so sizeDelta IS the box rather than an offset from a parent that has not been laid
                // out yet. Legacy Text measures its wrapped height against rect.width, and a width
                // that is still zero answers with one word per line.
                var choiceRect = (RectTransform)_choice.transform;
                choiceRect.anchorMin = Vector2.zero;
                choiceRect.anchorMax = Vector2.zero;
                choiceRect.pivot = Vector2.zero;
                choiceRect.sizeDelta = new Vector2(_contentWidth, 0f);

                _choiceHeight = Mathf.Max(BodyLineHeight, _choice.preferredHeight);

                choiceRect.sizeDelta = new Vector2(_contentWidth, _choiceHeight);
                choiceRect.anchoredPosition = new Vector2(SideMargin, ActionsBlockHeight);
                _choice.text = string.Empty;

                _choiceGroup = _choice.gameObject.AddComponent<CanvasGroup>();
                _choiceGroup.alpha = 1f;
            }

            /// <summary>
            /// The bottom of a step: the quiet way out and the way on, <b>side by side in one row</b>,
            /// each as wide as its own label needs.
            ///
            /// Stacked, these two spent a whole touch target and a gap more than they had to, and
            /// that band is the difference between step 2's list showing two rows of the tallest
            /// item in the catalogue and showing 0.93 of one. Side by side is also the ordinary
            /// shape for "leave" beside "go on", and neither control loses a millimetre of height:
            /// both are the full 56-point action row.
            ///
            /// <b>The split is measured, never a fraction.</b> "Tanto faz, vamos" and "Doesn't
            /// matter, let's go" are not the same width and neither is half of anything, so each
            /// button asks its own label how wide it is — the same move <see cref="BackButtonWidth"/>
            /// makes — the quiet one takes exactly what its word needs, and the primary takes the
            /// remainder. Usually that leaves the primary the wider of the two; in English at the
            /// narrowest canvas it does not, because "Doesn't matter, let's go" is four times the
            /// word "Start". <b>That is allowed and is not a hierarchy failure</b>: the primary is
            /// the clay one at full action-row height beside a quiet one, and capping the quiet
            /// button to make a sentence in this comment true would wrap its label for nobody's
            /// benefit. When the two labels genuinely cannot both fit on one line the widths are
            /// scaled rather than a label cut, and the label wraps inside a row tall enough for two
            /// body lines. Nothing is ever truncated and nothing is ever shrunk.
            ///
            /// <b>Scaling has a floor under it, and the floor is a word.</b> Scaling two widths to
            /// fit is only honest while each one still holds the longest word of its own label —
            /// under that, Unity's wrap breaks the word itself and "Continue" comes out as "Continu"
            /// over "e", which is what a proportional split produced in English at 402x874. So each
            /// button is floored at <see cref="WordFloor"/>, the primary takes its floor out of the
            /// quiet one rather than out of its own word, and when both floors and the gap will not
            /// fit the row that is an error carrying the numbers — never a shorter label and never
            /// smaller type.
            /// </summary>
            void BuildActionRow(RectTransform parent, Action onSkip, string primaryName,
                                string primaryLabelKey, Action onPrimary)
            {
                RectTransform bar = UIKit.CreateRect("Actions", parent);
                UIKit.AnchorBottom(bar, ActionRowHeight, SideMargin, SideMargin, ActionRowBottom);

                // The GameObject name and the escape hatch it is are unchanged from every version of
                // this screen: tools/e2e.sh reaches for QuickStart on both steps, and it is the one
                // control here that has kept its name through every rebuild.
                Button skip = UIKit.CreateButton(bar, "QuickStart", Loc.T(QuickStartKey),
                    UIKit.ButtonVariant.Secondary, onSkip);

                // "ToCustomise" and not "Continue", and the name is not a matter of taste.
                // E2ERunner does a global Find("Continue") as its dialogue-advance fallback, and the
                // dialogue canvas can still be alive underneath the cutscene blackout while this
                // screen is up — so a control named Continue here would be tapped by the day loop.
                //
                // Clay, not gold: gold on this screen means nothing at all, and the design system's
                // one gold action per screen is deliberately left unspent.
                Button primary = UIKit.CreateButton(bar, primaryName, Loc.T(primaryLabelKey),
                    UIKit.ButtonVariant.Primary, onPrimary);

                float available = _contentWidth - ActionGap;
                float skipWidth = LabelWidth(skip);
                float primaryWidth = LabelWidth(primary);

                // What each button may never be drawn under: its own longest word. The only floor
                // here was the touch target, and a touch target is narrower than the word "Continue"
                // needs at this type size — which is how the primary action on the first screen of
                // the game came to render "Continu" over "e" at 402x874. See WordFloor.
                float skipFloor = WordFloor(skip);
                float primaryFloor = WordFloor(primary);

                if (skipWidth + primaryWidth > available)
                {
                    float scale = available / (skipWidth + primaryWidth);
                    skipWidth = Mathf.Max(skipFloor, skipWidth * scale);
                }

                primaryWidth = Mathf.Max(primaryFloor, available - skipWidth);

                // The primary taking its floor is taken back out of the quiet button, down to the
                // quiet button's own floor and no further. That is the whole trade: the quiet label
                // wraps between two of its words, which the row is tall enough for, so that the
                // primary label does not have to break inside one of its own.
                if (skipWidth + primaryWidth > available)
                {
                    skipWidth = Mathf.Max(skipFloor, available - primaryWidth);
                }

                if (skipWidth + primaryWidth > available)
                {
                    // Both floors together are wider than the row. Nothing here shortens a label or
                    // scales the type to make the sentence fit: the label is content and
                    // DesignTokens.Type.Minimum is a floor rather than a budget. The buttons are
                    // drawn at their floors and overhang, which is visible, and the numbers that
                    // would have to move are named.
                    Debug.LogError("[CharacterCreation] The action row needs " +
                                   (skipFloor + primaryFloor + ActionGap) + " units for '" +
                                   skip.name + "' (" + skipFloor + ") and '" + primary.name + "' (" +
                                   primaryFloor + ") plus their gap, and the content column is " +
                                   _contentWidth + " units wide in locale " + Loc.LoadedLocale +
                                   ". Those are the widths of the longest word in each label, so the " +
                                   "row cannot hold both without breaking a word. Shorten one of the " +
                                   "two labels in that locale, or stack the row again and give step " +
                                   "2's list the band back some other way — never the type, and " +
                                   "never a touch target.");
                }

                PinActionButton(skip, 0f, skipWidth);
                PinActionButton(primary, 1f, primaryWidth);
            }

            /// <summary>What separates two words for <see cref="WordFloor"/>: whitespace, nothing else.</summary>
            static readonly char[] WordSeparators = { ' ', '\t', '\n', '\r' };

            /// <summary>
            /// The narrowest a button may be drawn: <b>its own longest word</b>, measured, plus the
            /// padding the kit insets a label by. Never under a touch target.
            ///
            /// A floor and not a preference, because of how legacy <see cref="Text"/> wraps. Its
            /// <c>HorizontalWrapMode.Wrap</c> breaks <i>inside</i> a word when the word is wider
            /// than the box rather than letting it overhang, so a primary button a few units
            /// narrower than the word "Continue" does not overflow — it renders "Continu" above
            /// "e". A label wrapping between two of its words is a layout; a label wrapping through
            /// the middle of one is a sentence cut in half with both halves still on screen, and it
            /// is the same defect as a cropped line whatever the mechanism.
            ///
            /// <b>Nothing satisfies this by shortening the label or scaling the type.</b> The label
            /// is a content decision and <c>DesignTokens.Type.Minimum</c> is a floor rather than a
            /// budget; when two floors will not fit one row, <see cref="BuildActionRow"/> reports it
            /// with the numbers in it.
            ///
            /// The measurement is the one <c>Text.preferredWidth</c> makes of itself — the
            /// component's own generation settings, through a generator of our own — rather than
            /// assigning each word to the label and reading the width back. The label holds its
            /// sentence throughout, which matters because this runs on a live control the player is
            /// about to see.
            /// </summary>
            static float WordFloor(Button button)
            {
                Text label = button != null ? button.GetComponentInChildren<Text>(true) : null;
                if (label == null || string.IsNullOrEmpty(label.text))
                {
                    return DesignTokens.Space.TouchTarget;
                }

                TextGenerationSettings settings = label.GetGenerationSettings(Vector2.zero);
                var generator = new TextGenerator();

                float widest = 0f;
                string[] words = label.text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < words.Length; i++)
                {
                    widest = Mathf.Max(widest,
                        generator.GetPreferredWidth(words[i], settings) / label.pixelsPerUnit);
                }

                return Mathf.Max(DesignTokens.Space.TouchTarget, widest + 2f * UIKit.ButtonPadding);
            }

            /// <summary>How wide a button has to be for its own label. Never under a touch target.</summary>
            static float LabelWidth(Button button)
            {
                float measured = 0f;

                Text word = button != null ? button.GetComponentInChildren<Text>(true) : null;
                if (word != null)
                {
                    measured = word.preferredWidth;
                }

                return Mathf.Max(DesignTokens.Space.TouchTarget, measured + 2f * UIKit.ButtonPadding);
            }

            /// <summary>Pins one control of the action row to an edge of it: 0 is left, 1 is right.</summary>
            static void PinActionButton(Button button, float edge, float width)
            {
                if (button == null)
                {
                    return;
                }

                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(edge, 0f);
                rect.anchorMax = new Vector2(edge, 1f);
                rect.pivot = new Vector2(edge, 0.5f);
                rect.sizeDelta = new Vector2(width, 0f);
                rect.anchoredPosition = Vector2.zero;
                UIKit.Layout(button).ignoreLayout = true;
            }

            // ============================================================== step 2

            /// <summary>
            /// How you look: the character you chose, in four facings, over the wardrobe.
            ///
            /// <b>Built rows first, chrome second.</b> How tall a row comes out depends on the
            /// longest name, description and unlock sentence in the running locale, which this file
            /// cannot know and must not guess. So the three lists are built, the tallest row is
            /// measured, and only then is the vertical budget spent — see
            /// <see cref="ApplyVerticalBudget"/> for the order things are cut in when it does not
            /// fit.
            ///
            /// <b>The tab bar sits above the rows rather than at the bottom.</b> That is a
            /// deliberate divergence from the backpack, which anchors its bar to the bottom of its
            /// sheet, and it is the only one: the bottom of this screen is already occupied by the
            /// action row, and a second bottom band would stack two bars on the home indicator.
            ///
            /// <b>The four-facing strip survives.</b> It is not replaced by the backpack's single
            /// horizontal figure, because accessories are anchored to named body parts now — a coil
            /// of rope on the right shoulder, a map tube across the back — and the back and side
            /// facings are the only place a player ever sees them. What it gave up instead is
            /// everything that used to sit around it: the character's name moved into the eyebrow
            /// row, the refusal moved onto the strip itself, the rule under the tab bar went, and
            /// the two bottom buttons became one row. Between them those four are 360 canvas units,
            /// and they are the reason the list can show two rows of the tallest item in the
            /// catalogue instead of 0.93 of one.
            /// </summary>
            void BuildStep2(RectTransform root)
            {
                _step2 = UIKit.CreateRect("Step2", root);
                UIKit.Stretch(_step2);

                BuildActionRow(_step2, Confirm, "Start", StartKey, Confirm);

                RectTransform tabContent = UIKit.CreateRect("TabContent", _step2);
                BuildTabs(tabContent);

                float preview;
                bool keepCustomiseTitle;
                ApplyVerticalBudget(out preview, out keepCustomiseTitle);

                float cursor = HeaderTop + EyebrowRowHeight + BandGap;

                // No Display type on this step. The screen already spent its one Display line on
                // step 1, and the vertical band is what the rows need.
                if (keepCustomiseTitle)
                {
                    Text customise = UIKit.CreateText(_step2, "CustomiseTitle", Loc.T(CustomiseTitleKey),
                        DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft);
                    UIKit.AnchorTop((RectTransform)customise.transform, BodyLineHeight,
                        SideMargin, SideMargin, cursor);
                    cursor += BodyLineHeight + BandGap;
                }

                BuildPreviewStrip(_step2, cursor, preview);
                cursor += preview + BandGap;

                BuildSegmentBar(_step2, cursor);
                cursor += SegmentBarHeight + BandGap;

                UIKit.Stretch(tabContent, SideMargin, SideMargin, cursor, ActionsBlockHeight);

                SelectTab(0);
            }

            /// <summary>
            /// Spends step 2's height, <b>list first</b>.
            ///
            /// This is the inversion that fixes the step. It used to lay the chrome out at its
            /// preferred sizes, ask what was left, and cut three things one at a time until the list
            /// had two rows — an order that reads as careful and cannot work, because on a 1080x1920
            /// canvas the sum of the preferences is larger than the canvas and every cut still lands
            /// short. What it produced was a screen that showed 0.93 of a row and an error nobody
            /// could act on.
            ///
            /// So the arithmetic runs the other way now:
            /// <list type="number">
            /// <item>the tallest row in the running locale is measured off the rows that were just
            /// built — never assumed, because it depends on the longest description and unlock
            /// sentence in a language this file cannot read;</item>
            /// <item><see cref="MinimumVisibleRows"/> of it is set aside first, before anything
            /// else is placed;</item>
            /// <item>the fixed chrome — the eyebrow row, the tab bar, the action row and the gaps —
            /// takes its share, and none of it is negotiable: every piece of it is a touch target,
            /// a safe-area inset or the gap between two of them;</item>
            /// <item>whatever is left over is the preview strip's, clamped between
            /// <see cref="PreviewHeightFloor"/> and <see cref="PreviewHeightMax"/>. The step's own
            /// subtitle is kept only if doing so still leaves the strip a full
            /// <see cref="PreviewHeightMin"/>.</item>
            /// </list>
            ///
            /// <b>That clamp is an invariant and not a preference: the strip is never drawn under
            /// <see cref="PreviewHeightFloor"/>, whatever the leftover comes out at.</b> There is
            /// therefore exactly one way this step can fail — the floor eats into the list, and the
            /// list falls under <see cref="MinimumVisibleRows"/> — and it is an error every time.
            /// There is no second, quieter failure where the strip is allowed to shrink away
            /// instead; that fork existed, and it made a one-unit strip the non-fatal outcome.
            ///
            /// <b>Type is never shrunk, a touch target is never shrunk, and a sentence is never
            /// truncated.</b> Those three are what a builder reaches for when a layout will not fit,
            /// and they are the three this method may not touch. What it may spend is the preview's
            /// height, which is why the preview is last in the queue rather than first — and when
            /// even the preview's floor cannot buy the list its two rows, that is reported with
            /// every number in it and nothing is quietly given back.
            ///
            /// <b>The one honest trade, stated out loud:</b> at 1080x1920 — the aspect the desktop
            /// e2e player runs at, and a canvas 150 design points shorter than any phone this game
            /// has been played on — the leftover lands under <see cref="PreviewHeightMin"/> and the
            /// strip draws compact, without its facing captions. The rows keep their two, because
            /// between a list that cannot show what it lists and a preview drawn at the size of the
            /// thumbnails beside it, the list is the thing the step exists for.
            /// </summary>
            void ApplyVerticalBudget(out float preview, out bool keepCustomiseTitle)
            {
                preview = PreviewHeightMax;
                keepCustomiseTitle = true;

                float tallest = TallestRowHeight();
                if (tallest <= 0f)
                {
                    // No rows were drawn at all, which BuildTabRows has already reported. There is
                    // nothing to budget against, so the chrome keeps its preferences.
                    return;
                }

                float needed = MinimumVisibleRows * tallest;
                float spare = _rootHeight - FixedChrome() - needed;

                float titleCost = BodyLineHeight + BandGap;
                keepCustomiseTitle = spare - titleCost >= PreviewHeightMin;
                if (keepCustomiseTitle)
                {
                    spare -= titleCost;
                }

                preview = Mathf.Clamp(spare, PreviewHeightFloor, PreviewHeightMax);

                if (spare >= PreviewHeightFloor)
                {
                    return;
                }

                // Under the strip's own floor — and the clamp above is what holds, at every value
                // of spare rather than only at the ones that reach here negative.
                //
                // This branch used to fork, and the fork ran the severity backwards. Between zero
                // and the floor the strip yielded instead: preview = spare, with no lower bound at
                // all, on the argument that the list is what the step exists for and a short strip
                // is cosmetic. The argument is right about the list and wrong about "cosmetic" —
                // with nothing under it, a spare of one unit drew a one-unit strip and warned, while
                // a spare of minus one kept the clamp, drew the strip at its floor and raised an
                // error. The visibly broken outcome was the quiet one, and the floor the error
                // below reports held only in the cases where it had not been needed.
                //
                // So the floor holds, and the shortfall lands where it can be counted: the list is
                // short by exactly (PreviewHeightFloor - spare), which is the one condition
                // MinimumVisibleRows is a floor against. One outcome, one severity, and both
                // monotone in spare. The lever is still the same one — shorten the longest
                // catalogue string in the running locale — and it is named in the error.
                float band = needed + spare - PreviewHeightFloor;

                Debug.LogError("[CharacterCreation] Step 2 shows " + (band / tallest).ToString("0.00") +
                               " rows at the tallest row in locale " + Loc.LoadedLocale + " (" + tallest +
                               " units), against a floor of " + MinimumVisibleRows +
                               ". The band is " + band + " units of a " + _rootHeight +
                               "-unit safe area, with the preview already at its floor of " +
                               PreviewHeightFloor + " and " + FixedChrome() +
                               " units of chrome that is all touch target and safe area. Shorten the " +
                               "longest catalogue string in that locale, or take a band out of the " +
                               "chrome — never the type and never a touch target.");
            }

            /// <summary>
            /// Everything on step 2 that cannot be spent: the eyebrow row and its clearance, the tab
            /// bar, the action row, the home indicator's lane, and the four gaps between them.
            ///
            /// Written as the sum of its bands rather than as a number, because that is the list a
            /// reader has to argue with before claiming the step has room for anything else.
            /// </summary>
            static float FixedChrome()
            {
                return HeaderTop + EyebrowRowHeight + BandGap +   // the row with Back and the toggle
                       BandGap +                                  // under the preview strip
                       SegmentBarHeight + BandGap +               // the slot bar and its clearance
                       ActionsBlockHeight;                        // the action row and the indicator
            }

            /// <summary>
            /// The tallest row across all three tabs, read off the heights the rows pinned
            /// themselves to. Zero when nothing was drawn.
            /// </summary>
            float TallestRowHeight()
            {
                float tallest = 0f;

                for (int i = 0; _tabs != null && i < _tabs.Length; i++)
                {
                    TabView tab = _tabs[i];
                    if (tab == null)
                    {
                        continue;
                    }

                    for (int r = 0; r < tab.Rows.Count; r++)
                    {
                        WardrobeRow.View view = tab.Rows[r];
                        if (view == null || view.Root == null)
                        {
                            continue;
                        }

                        var layout = view.Root.GetComponent<LayoutElement>();
                        if (layout != null)
                        {
                            tallest = Mathf.Max(tallest, layout.preferredHeight);
                        }
                    }
                }

                return tallest;
            }

            /// <summary>
            /// The four facings, as one panel of four cells, with the refusal written across its
            /// foot.
            ///
            /// Each cell fits its figure to the character ratio rather than sizing it, so the same
            /// code gives the largest figure the cell allows at 1080 units across and at the ~977 a
            /// phone reports.
            ///
            /// <b>Two shapes, decided by the height the budget could afford.</b> At
            /// <see cref="PreviewHeightMin"/> and above a cell is a figure over its facing caption.
            /// Below it there is no caption band: the words would be cropping the figure to say
            /// something the picture already says, so they move to <see cref="AccessibleLabel"/> —
            /// where a screen reader still reads them out — and the figures stand on the panel's
            /// lower edge with <see cref="PreviewCompactPadding"/> of headroom. Nothing is shrunk
            /// that a player reads; a caption at <c>Type.Minimum</c> is the design system's floor
            /// and is never taken below it.
            /// </summary>
            void BuildPreviewStrip(RectTransform parent, float top, float height)
            {
                Image strip = UIKit.CreateCard(parent, "Preview", UIKit.CardStyle.Panel);
                var stripRect = (RectTransform)strip.transform;
                UIKit.AnchorTop(stripRect, height, SideMargin, SideMargin, top);
                strip.raycastTarget = false;

                bool captions = height >= PreviewHeightMin;

                for (int i = 0; i < Directions.Length; i++)
                {
                    BuildPreviewCell(stripRect, i, captions);
                }

                BuildRefusal(stripRect);
            }

            void BuildPreviewCell(RectTransform stripRect, int index, bool caption)
            {
                float left = index / (float)Directions.Length;
                float right = (index + 1) / (float)Directions.Length;

                string facing = Loc.T(DirectionCaptionKeys[index]);

                RectTransform cell = UIKit.CreateRect("Cell_" + Directions[index].ToKey(), stripRect);
                cell.anchorMin = new Vector2(left, 0f);
                cell.anchorMax = new Vector2(right, 1f);
                cell.pivot = new Vector2(0.5f, 0.5f);
                cell.offsetMin = new Vector2(DesignTokens.Space.S4, 0f);
                cell.offsetMax = new Vector2(-DesignTokens.Space.S4, 0f);

                // Said out loud in both shapes, so the compact strip loses the printed word and not
                // the word itself.
                AccessibleLabel.Apply(cell.gameObject, facing);

                RectTransform figureArea = UIKit.CreateRect("FigureArea", cell);
                UIKit.Stretch(figureArea, 0f, 0f,
                    caption ? PreviewPadding : PreviewCompactPadding,
                    caption ? CaptionBandHeight : 0f);

                RectTransform figure = CharacterFigure.CreateFigureRect(figureArea, "Figure");
                _previewLayers[index] = CharacterFigure.Build(figure);

                if (!caption)
                {
                    return;
                }

                Text label = UIKit.CreateText(cell, "Caption", facing,
                    DesignTokens.Type.Minimum, DesignTokens.Ink.Secondary, TextAnchor.MiddleCenter,
                    DesignTokens.TypeRole.BodyStrong);
                UIKit.AnchorBottom((RectTransform)label.transform, CaptionHeight, 0f, 0f, CaptionBottom);
            }

            /// <summary>
            /// The line that answers a tap the wardrobe could not carry out.
            ///
            /// <b>It lives on the preview panel, and it costs the vertical budget nothing.</b> It
            /// used to have a reserved band of its own between the strip and the tab bar — two body
            /// lines of empty space on every frame of every session in which nothing was refused,
            /// sitting directly on top of the band the list was short of. This is the backpack's own
            /// answer to the same problem, arrived at for the same reason: put the sentence on the
            /// stage, not under the list.
            ///
            /// The strip has no spare width beside four figures the way the backpack's stage has
            /// beside one, so the bar is drawn <i>over</i> the foot of the strip rather than beside
            /// it, on its own opaque ground. That is the cost, stated plainly: while a refusal is up
            /// it covers the facing captions and the figures' feet. It is up only after a tap that
            /// could not be carried out, it clears on the next tab change and on the next piece that
            /// does go on, and it sits under the thing whose non-change it explains.
            ///
            /// <b>Its height is measured from the sentence, every time.</b> A reserved line count is
            /// a guess about a string in a language nobody writing the layout is reading — see
            /// <see cref="ShowRefusal"/>, which asks the label how tall it is in the width it has and
            /// grows the bar to the answer. Nothing here can clip a refusal, in any locale, at any
            /// width.
            ///
            /// It carries the same GameObject name the backpack's does, so one e2e helper reads
            /// both; the two screens never coexist. <c>Ink.Secondary</c> and never
            /// <c>Feedback.Error</c>: two pieces that do not go together is information about the
            /// pieces, not a mistake the player made, and nothing was lost by trying. On this
            /// palette that tone rule is also a legibility one — <c>Feedback.Error</c> measures
            /// 3.22:1 on a card and fails AA outright.
            /// </summary>
            void BuildRefusal(RectTransform stripRect)
            {
                Image banner = UIKit.CreatePanel(stripRect, "RefusalBanner",
                    DesignTokens.Surface.Panel, UiSpriteKeys.FrameMd);
                banner.raycastTarget = false;
                UIKit.Layout(banner).ignoreLayout = true;

                _refusalBanner = (RectTransform)banner.transform;
                _refusalBanner.anchorMin = new Vector2(0f, 0f);
                _refusalBanner.anchorMax = new Vector2(1f, 0f);
                _refusalBanner.pivot = new Vector2(0.5f, 0f);
                _refusalBanner.offsetMin = new Vector2(DesignTokens.Space.S4, 0f);
                _refusalBanner.offsetMax = new Vector2(-DesignTokens.Space.S4, 0f);
                _refusalBanner.sizeDelta = new Vector2(_refusalBanner.sizeDelta.x, BodyLineHeight);

                _refusalWidth = _contentWidth - 2f * DesignTokens.Space.S4 - 2f * DesignTokens.Space.S8;

                // The label is the object the run reads, with no wrapper: the end-to-end run reads
                // this line with TextOf("RefusalMessage"), which asks the object of that name for its
                // own Text component and finds nothing on a wrapper.
                _refusal = UIKit.CreateText(_refusalBanner, "RefusalMessage", string.Empty,
                    DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.MiddleCenter);

                // Collapsed to the bar's centre before it is sized, so sizeDelta is the box rather
                // than an offset from a parent whose own height changes with every sentence.
                var refusalRect = _refusal.rectTransform;
                refusalRect.anchorMin = new Vector2(0.5f, 0.5f);
                refusalRect.anchorMax = new Vector2(0.5f, 0.5f);
                refusalRect.pivot = new Vector2(0.5f, 0.5f);
                refusalRect.anchoredPosition = Vector2.zero;
                WardrobeRow.PinTextBox(_refusal, _refusalWidth, BodyLineHeight);

                // The group holds the ground as well as the words, so an empty refusal is an invisible
                // bar rather than an opaque stripe across the figures' feet.
                _refusalGroup = banner.gameObject.AddComponent<CanvasGroup>();
                _refusalGroup.alpha = 0f;
            }

            /// <summary>
            /// The three slots, as a segmented control above the list.
            ///
            /// The order comes from <see cref="Wardrobe.BadgedSlots"/> and is never spelled again:
            /// the wardrobe tabs <i>are</i> the badged slots, in their order, and writing them out
            /// twice is how the backpack's dots and this bar would come to disagree without anyone
            /// editing either on purpose. A badged set that no longer matches the bar is reported
            /// rather than silently truncated.
            ///
            /// <b>The cells abut.</b> No gap between them, which is the one place on this screen
            /// where the house 48/8 rule's second half is not applied: WCAG 2.2 SC 2.5.8's spacing
            /// exception is written for targets under 24x24, these clear the criterion outright, and
            /// every vendor draws a segmented control as touching segments.
            ///
            /// <b>The bar is silent.</b> The kit wraps any handler it is given in a confirm cue, so
            /// this passes null and wires the listener itself. Crossing the bar a dozen times while
            /// comparing two pieces is the highest-frequency interaction on the screen, and a cue on
            /// every crossing stops being feedback. A row tap keeps its cue — an equip is a confirm.
            /// </summary>
            void BuildSegmentBar(RectTransform parent, float top)
            {
                RectTransform bar = UIKit.CreateRect("SlotSegments", parent);
                UIKit.AnchorTop(bar, SegmentBarHeight, SideMargin, SideMargin, top);
                UIKit.HorizontalGroup(bar.gameObject, 0f, new RectOffset(), TextAnchor.MiddleCenter);

                float cellWidth = _contentWidth / _tabs.Length;

                for (int i = 0; i < _tabs.Length; i++)
                {
                    BuildSegment(bar, i, cellWidth);
                }
            }

            void BuildSegment(RectTransform bar, int index, float cellWidth)
            {
                int captured = index;
                string cellLabel = Loc.T(TabLabelKeys[index]);

                Button cell = UIKit.CreateButton(bar, SegmentObjectNames[index], string.Empty,
                    UIKit.ButtonVariant.Ghost, null);
                cell.onClick.AddListener(() => SelectTab(captured));
                AccessibleLabel.Apply(cell.gameObject, cellLabel);

                var cellRect = (RectTransform)cell.transform;

                // Fixed widths, never content-driven. The selected label changes weight, and cells
                // that measured themselves would re-lay the whole row on every tap.
                LayoutElement cellLayout = UIKit.Layout(cell);
                cellLayout.minWidth = cellWidth;
                cellLayout.preferredWidth = cellWidth;
                cellLayout.flexibleWidth = 0f;
                cellLayout.minHeight = SegmentBarHeight;
                cellLayout.preferredHeight = SegmentBarHeight;
                cellLayout.flexibleHeight = 0f;

                // Neither clay token satisfies both criteria on its own: Ink.OnPrimary on
                // Brand.Primary is 3.82:1 and fails SC 1.4.3 for a 15-point bold label, and
                // Brand.PrimaryDark on the background is too quiet for SC 1.4.11. So the fill
                // carries the label and the rim carries the boundary.
                Image fill = UIKit.CreatePanel(cellRect, SegmentFillObjectNames[index],
                    DesignTokens.Brand.PrimaryDark, UiSpriteKeys.FrameMd);
                UIKit.Stretch((RectTransform)fill.transform);
                fill.raycastTarget = false;
                UIKit.Layout(fill).ignoreLayout = true;
                fill.gameObject.SetActive(false);

                // The cell's own rect, with no outset: the focus ring sits further out, and that gap
                // is what keeps "selected" and "focused" from reading as the same thing.
                Image rim = UIKit.CreatePanel(cellRect, SegmentRimObjectNames[index],
                    DesignTokens.Brand.Primary, UiSpriteKeys.FocusRing);
                UIKit.Stretch((RectTransform)rim.transform);
                rim.raycastTarget = false;
                UIKit.Layout(rim).ignoreLayout = true;
                rim.gameObject.SetActive(false);

                Text label = UIKit.CreateText(cellRect, SegmentLabelObjectNames[index], cellLabel,
                    DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.MiddleCenter,
                    DesignTokens.TypeRole.Body);

                // A one-line rect, centred vertically, and flush to the cell. The height matters: in
                // a full-height rect a two-line label would fit inside the cell and clip silently,
                // whereas in a one-line rect it spills below, where the daily screenshot catches it.
                // Three cells rather than the backpack's four gives every label about a third more
                // room, so the same words fit here with margin to spare.
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = new Vector2(0f, 0.5f);
                labelRect.anchorMax = new Vector2(1f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.offsetMin = new Vector2(0f, -0.5f * BodyLineHeight);
                labelRect.offsetMax = new Vector2(0f, 0.5f * BodyLineHeight);
                UIKit.Layout(label).ignoreLayout = true;

                // The kit parented the focus ring before these children existed, so it would
                // otherwise draw underneath the clay fill.
                Transform focusRing = cellRect.Find("FocusRing");
                if (focusRing != null)
                {
                    focusRing.SetAsLastSibling();
                }

                _tabs[index].Fill = fill;
                _tabs[index].Rim = rim;
                _tabs[index].Label = label;
            }

            /// <summary>
            /// The three lists, one scroll view each, all stretched to the same band.
            ///
            /// Each tab keeps its own <see cref="ScrollRect"/> so that each keeps its own scroll
            /// offset for as long as the screen is up, and only the selected one is active.
            /// </summary>
            void BuildTabs(RectTransform tabContent)
            {
                IReadOnlyList<CharacterSlot> badged = Wardrobe.BadgedSlots;
                if (badged.Count != TabObjectNames.Length)
                {
                    Debug.LogError("[CharacterCreation] Wardrobe.BadgedSlots has " + badged.Count +
                                   " slot(s) but this screen has " + TabObjectNames.Length + " tab(s). A " +
                                   "slot with no tab here cannot be customised at creation at all, and " +
                                   "the backpack and this screen would be listing different wardrobes.");
                }

                _tabs = new TabView[TabObjectNames.Length];

                for (int i = 0; i < _tabs.Length; i++)
                {
                    var tab = new TabView
                    {
                        Slot = i < badged.Count ? badged[i] : CharacterSlot.Base
                    };
                    _tabs[i] = tab;

                    RectTransform content;
                    ScrollRect scroll = UIKit.CreateScrollView(tabContent, TabObjectNames[i], out content);
                    UIKit.Stretch((RectTransform)scroll.transform);

                    // The kit's default column padding is measured for a full-bleed screen; this one
                    // already sits inside the screen's gutters, so the only reservation left to make
                    // is the scrollbar's lane — which is the same lane WardrobeRow.MetricsFor took
                    // out of the row's width, and the two have to agree or the row sits short.
                    var column = content.GetComponent<VerticalLayoutGroup>();
                    if (column != null)
                    {
                        column.spacing = DesignTokens.Space.S12;
                        column.padding = new RectOffset(
                            0,
                            Mathf.RoundToInt(ScrollbarWidth + DesignTokens.Space.S8),
                            0,
                            Mathf.RoundToInt(DesignTokens.Space.S24));
                    }

                    tab.Scroll = scroll;
                    tab.Content = content;

                    UIKit.AttachVerticalScrollbar(scroll, ScrollbarWidth);
                    BuildTabRows(tab);

                    scroll.gameObject.SetActive(false);
                }
            }

            /// <summary>
            /// One slot's rows, locked ones included, as one full-width scrolling column.
            ///
            /// <b>Creation draws no NOVO badge and marks nothing seen.</b> On a fresh save every
            /// free item is unlocked-and-never-seen, so this screen would otherwise open covered in
            /// gold pills — and if it marked them, the player's first backpack would arrive with no
            /// badges and no HUD pill at all, spending the one moment a badge exists to announce on
            /// a screen that was never about announcing. So <c>showNewBadge: false</c> everywhere,
            /// and <see cref="Wardrobe.MarkSeen"/> and <see cref="Wardrobe.MarkSlotSeen"/> are not
            /// called from this file at all.
            ///
            /// A tab that draws nothing still exists, is still enabled, and says so in a sentence
            /// rather than becoming a shorter screen.
            /// </summary>
            void BuildTabRows(TabView tab)
            {
                CatalogItemDef[] items = Wardrobe.ItemsForSlot(tab.Slot);
                int bodyArtVariant = Look().BodyArtVariant;

                for (int i = 0; items != null && i < items.Length; i++)
                {
                    CatalogItemDef item = items[i];
                    if (item == null || string.IsNullOrEmpty(item.id))
                    {
                        continue;
                    }

                    bool locked = !Wardrobe.IsUnlocked(_state, item.id);

                    WardrobeRow.View view = WardrobeRow.Build(tab.Content, item, tab.Slot, _rowMetrics,
                        bodyArtVariant, locked, false, false, "Chip_", OnRowTapped);

                    if (view != null)
                    {
                        tab.Rows.Add(view);
                    }
                }

                if (tab.Rows.Count == 0)
                {
                    Debug.LogWarning("[CharacterCreation] Slot " + tab.Slot + " drew no rows. Either " +
                                     "character_catalog.json has no items for it, or every one of them " +
                                     "failed to resolve.");
                    WardrobeRow.BuildEmpty(tab.Content, _rowMetrics);
                }
            }

            // ============================================================== behaviour

            /// <summary>
            /// Swaps which step is up. The eyebrow row and the language toggle stay put; only the
            /// control on the left of that row, the header's two lines, and the body change.
            /// </summary>
            void ShowStep(bool customise)
            {
                if (_step1 != null)
                {
                    _step1.gameObject.SetActive(!customise);
                }

                if (_step2 != null)
                {
                    _step2.gameObject.SetActive(customise);
                }

                if (_eyebrow != null)
                {
                    _eyebrow.gameObject.SetActive(!customise);
                }

                if (_back != null)
                {
                    _back.gameObject.SetActive(customise);
                }

                // The chosen character's name shares the eyebrow row with Back and the toggle, so it
                // comes and goes with the step the way Back does rather than with a step's body.
                if (_chosenName != null)
                {
                    _chosenName.gameObject.SetActive(customise);
                }

                if (_screenTitle != null)
                {
                    _screenTitle.gameObject.SetActive(!customise);
                }

                if (_screenSubtitle != null)
                {
                    _screenSubtitle.gameObject.SetActive(!customise);
                }

                if (!customise)
                {
                    RefreshCards();
                    RefreshTones();
                    return;
                }

                // Entering the wardrobe: the tone may have changed on the step behind, so every
                // thumbnail is repainted onto the body the player is actually wearing.
                ShowRefusal(null);
                RefreshChosenName();
                RefreshThumbBodies();
                RefreshRows();
                RefreshPreview();
            }

            /// <summary>
            /// Moves to the wardrobe, or says why it cannot.
            ///
            /// <b>The button is never disabled.</b> A disabled Secondary or Primary control draws at
            /// 0.40 opacity, which is the disabled-looking void rule 7 forbids; a live button that
            /// answers in one calm sentence costs the player nothing and tells them what to do.
            /// </summary>
            void ToCustomise()
            {
                if (CharacterPresets.Get(_state.characterId) == null)
                {
                    ShowChoiceMessage(Loc.T(ChooseFirstKey));
                    return;
                }

                ShowChoiceMessage(null);
                ShowStep(true);
            }

            /// <summary>
            /// Picks a character: their build, their default wardrobe, and a suggested name.
            ///
            /// <b>Re-picking the character you already have does nothing but repaint.</b> Re-seeding
            /// would throw away every swap made in step 2 by a player who went back and tapped the
            /// same card to look at it again — rule 7 in its most literal form.
            ///
            /// The order below is the whole method. The worn set is replaced rather than merged,
            /// because it describes one character; <see cref="CharacterPresets.DefaultEquipped"/>
            /// already returns it in the order that matters, with the signature accessory last so
            /// that the single accessory art layer draws the piece the character is recognised by.
            /// <see cref="CharacterPresets.ApplyTo"/> then lays the floor and puts the player's skin
            /// tone back, which is the mechanism that keeps the tone theirs.
            /// </summary>
            void SelectCharacter(string presetId)
            {
                if (string.Equals(_state.characterId, presetId, StringComparison.Ordinal))
                {
                    RefreshCards();
                    return;
                }

                string previous = _state.characterId;
                _state.characterId = presetId;

                if (_state.equippedItems == null)
                {
                    _state.equippedItems = new List<string>();
                }

                _state.equippedItems.Clear();
                _state.equippedItems.AddRange(CharacterPresets.DefaultEquipped(presetId));

                CharacterPresets.ApplyTo(_state.appearance, presetId);

                ApplyNamePrefill(previous, presetId);

                RefreshCards();
                RefreshThumbBodies();
                RefreshRows();
                RefreshPreview();
                RefreshChosenName();
            }

            /// <summary>
            /// Fills the name field with the character's suggested name — but only when doing so
            /// cannot destroy something the player wrote.
            ///
            /// The field is filled when it is empty, and when it still holds the previous
            /// character's suggestion untouched. Anything else is a name the player typed, and
            /// overwriting it because they looked at the other character is a punishment.
            /// </summary>
            void ApplyNamePrefill(string previousPresetId, string presetId)
            {
                string current = CharacterPresets.Sanitize(_name);
                string previousSuggestion = CharacterPresets.Sanitize(
                    CharacterPresets.SuggestedName(previousPresetId));

                bool untouched = current.Length == 0 ||
                                 (previousSuggestion.Length > 0 &&
                                  string.Equals(current, previousSuggestion, StringComparison.Ordinal));

                if (!untouched)
                {
                    return;
                }

                string suggestion = CharacterPresets.SuggestedName(presetId);
                _name = suggestion ?? string.Empty;
                _state.playerName = CharacterPresets.Sanitize(_name);

                if (_nameField != null)
                {
                    _nameField.text = _name;
                }

                RefreshNameHint();
            }

            /// <summary>Writes the tone onto the run and repaints everything that draws a body.</summary>
            void SelectTone(int tone)
            {
                _state.appearance.skin = Mathf.Clamp(tone, 0, AppearanceState.SkinTones - 1);

                RefreshTones();
                RefreshCards();
                RefreshThumbBodies();
                RefreshPreview();
            }

            /// <summary>
            /// Every keystroke, straight into the run. See <see cref="BuildNameRow"/>: a language
            /// switch reloads the scene, and a name held only here would not survive it.
            /// </summary>
            void OnNameTyped(string value)
            {
                _name = value ?? string.Empty;
                _state.playerName = CharacterPresets.Sanitize(_name);
                RefreshNameHint();
            }

            /// <summary>
            /// A tap on a wardrobe row, through the one policy both screens share.
            ///
            /// A locked row arrives here like any other and is answered with a sentence rather than
            /// with a control that could not be tapped in the first place.
            /// </summary>
            void OnRowTapped(string itemId, CharacterSlot slot)
            {
                string sentence;
                WardrobeRow.Tap(_state, itemId, slot, out sentence);

                ShowRefusal(sentence);
                RefreshRows();
                RefreshPreview();
            }

            /// <summary>
            /// Shows one tab and hides the other two. Silent, and it clears any refusal on the way:
            /// a sentence about a piece in the tab you just left is a sentence about nothing.
            /// </summary>
            void SelectTab(int index)
            {
                if (_tabs == null || index < 0 || index >= _tabs.Length || index == _selectedTab)
                {
                    return;
                }

                _selectedTab = index;
                ShowRefusal(null);

                for (int i = 0; i < _tabs.Length; i++)
                {
                    TabView tab = _tabs[i];
                    if (tab == null)
                    {
                        continue;
                    }

                    bool selected = i == index;

                    if (tab.Scroll != null)
                    {
                        tab.Scroll.gameObject.SetActive(selected);
                    }

                    if (tab.Fill != null)
                    {
                        tab.Fill.gameObject.SetActive(selected);
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
                    }

                    if (selected && !tab.Settled)
                    {
                        // A rebuild and a scroll-to-top only work on an active object, so both wait
                        // for the tab's first showing rather than running on a hidden panel.
                        tab.Settled = true;
                        UIKit.RebuildNow(tab.Content);

                        if (tab.Scroll != null)
                        {
                            tab.Scroll.verticalNormalizedPosition = 1f;
                        }
                    }
                }

                RefreshRows();
            }

            // ============================================================== repainting

            void RefreshCards()
            {
                for (int i = 0; i < _cards.Count; i++)
                {
                    CardView card = _cards[i];
                    if (card == null)
                    {
                        continue;
                    }

                    bool chosen = string.Equals(_state.characterId, card.PresetId, StringComparison.Ordinal);

                    if (card.Ring != null)
                    {
                        card.Ring.gameObject.SetActive(chosen);
                    }

                    if (card.Mark != null)
                    {
                        card.Mark.gameObject.SetActive(chosen);
                    }

                    // A throwaway look, so that seeing a character costs the run nothing. The tone
                    // is seeded from the player's and ApplyTo restores it after composing.
                    var preview = new AppearanceState { skin = Look().skin };
                    CharacterPresets.ApplyTo(preview, card.PresetId);
                    CharacterFigure.Apply(card.Layers, preview, FacingDirection.Down);

                    AccessibleLabel.Apply(card.Root, chosen
                        ? Loc.T(ChosenStateKey, card.CharacterName)
                        : card.CharacterName);
                }
            }

            void RefreshTones()
            {
                int chosen = Look().skin;

                for (int i = 0; i < _tones.Count; i++)
                {
                    ToneView tone = _tones[i];
                    if (tone == null)
                    {
                        continue;
                    }

                    bool selected = tone.Tone == chosen;

                    if (tone.Ring != null)
                    {
                        tone.Ring.gameObject.SetActive(selected);
                    }

                    if (tone.Mark != null)
                    {
                        tone.Mark.gameObject.SetActive(selected);
                    }

                    string spoken = Loc.T(ToneOptionKey, tone.Tone + 1);
                    AccessibleLabel.Apply(tone.Root, selected ? Loc.T(ChosenStateKey, spoken) : spoken);
                }
            }

            void RefreshNameHint()
            {
                if (_nameHint != null)
                {
                    _nameHint.text = CharacterPresets.NameHint(CharacterPresets.Validate(_name));
                }
            }

            void RefreshChosenName()
            {
                if (_chosenName != null)
                {
                    _chosenName.text = CharacterPresets.SuggestedName(_state.characterId);
                }
            }

            /// <summary>Ring and status on every row of every tab, from what each slot is showing.</summary>
            void RefreshRows()
            {
                for (int i = 0; _tabs != null && i < _tabs.Length; i++)
                {
                    TabView tab = _tabs[i];
                    if (tab == null)
                    {
                        continue;
                    }

                    string worn = Wardrobe.EquippedInSlot(_state, tab.Slot);

                    for (int r = 0; r < tab.Rows.Count; r++)
                    {
                        WardrobeRow.View view = tab.Rows[r];
                        WardrobeRow.Apply(view, view != null &&
                            string.Equals(worn, view.ItemId, StringComparison.Ordinal));
                    }
                }
            }

            /// <summary>Repaints the body under every thumbnail. Cheap, and the tone changed.</summary>
            void RefreshThumbBodies()
            {
                int variant = Look().BodyArtVariant;

                for (int i = 0; _tabs != null && i < _tabs.Length; i++)
                {
                    TabView tab = _tabs[i];
                    if (tab == null)
                    {
                        continue;
                    }

                    for (int r = 0; r < tab.Rows.Count; r++)
                    {
                        WardrobeRow.ApplyBody(tab.Rows[r], variant);
                    }
                }
            }

            void RefreshPreview()
            {
                AppearanceState look = Look();

                for (int i = 0; i < Directions.Length; i++)
                {
                    CharacterFigure.Apply(_previewLayers[i], look, Directions[i]);
                }
            }

            /// <summary>
            /// Puts a sentence across the foot of the preview, or takes it away.
            ///
            /// <b>The bar is sized to the sentence before it is shown.</b> The label is measured in
            /// the width it actually has and the ground behind it grows to the answer, so a refusal
            /// cannot be clipped by a band somebody reserved in another language. Cleared, the whole
            /// group goes to zero — the ground with the words, because an opaque stripe across four
            /// figures' feet with nothing written on it would be worse than the message it is not
            /// showing.
            /// </summary>
            void ShowRefusal(string sentence)
            {
                if (_refusal != null && _refusalBanner != null && !string.IsNullOrEmpty(sentence))
                {
                    _refusal.text = sentence;
                    float height = WardrobeRow.PinText(_refusal, _refusalWidth);
                    _refusal.rectTransform.anchoredPosition = Vector2.zero;
                    _refusalBanner.sizeDelta = new Vector2(_refusalBanner.sizeDelta.x,
                        height + 2f * DesignTokens.Space.S8);
                }

                _refusalFade = Announce(_refusal, _refusalGroup, _refusalFade, sentence, 0f);
            }

            void ShowChoiceMessage(string sentence)
            {
                _choiceFade = Announce(_choice, _choiceGroup, _choiceFade, sentence, 1f);
            }

            /// <summary>
            /// The fade both announced bands share. Returns the tween handle to store, or null.
            ///
            /// A sentence arrives on a fade, and that fade keeps running under reduced motion — the
            /// design system suppresses parallax, pulse and shake, but a fade is information.
            /// Clearing sets the alpha outright rather than fading out: an empty label has nothing
            /// to fade, and a half-finished fade-in would otherwise leave a stale alpha behind for
            /// the next sentence. The previous tween is stopped first, because tapping two rows in
            /// quick succession is an ordinary thing to do.
            ///
            /// <paramref name="clearedAlpha"/> is what the group rests at with nothing to say, and
            /// the two callers want different answers. A bare label on the step's own ground rests
            /// at 1 — there is nothing to see either way, and leaving it at 0 would mean the next
            /// sentence had two things to undo. A label on its own opaque ground rests at 0, because
            /// the ground is visible even when the words are not.
            /// </summary>
            static Coroutine Announce(Text target, CanvasGroup group, Coroutine running, string sentence,
                                      float clearedAlpha)
            {
                if (target == null)
                {
                    return null;
                }

                target.text = sentence ?? string.Empty;

                if (group == null)
                {
                    return null;
                }

                UIMotion.Stop(running);

                if (string.IsNullOrEmpty(sentence))
                {
                    group.alpha = clearedAlpha;
                    return null;
                }

                group.alpha = 0f;
                return UIMotion.Fade(group, 1f, DesignTokens.Motion.ToastIn);
            }

            // ============================================================== the two ways out

            /// <summary>
            /// For players who do not want to spend time here.
            ///
            /// It selects the first authored character, leaves the tone as the run carries it, and
            /// confirms — skipping the wardrobe entirely. This is the honest form of a
            /// pre-selection: a button that dresses you and moves on, rather than a default that
            /// quietly decides for someone who did want to choose.
            ///
            /// The GameObject name and its position are unchanged from the screen this replaced,
            /// because <c>tools/e2e.sh</c> taps it from the very first frame of creation.
            /// </summary>
            void QuickStartFromChoice()
            {
                if (CharacterPresets.Get(_state.characterId) == null)
                {
                    PresetDef fallback = CharacterPresets.Default;
                    if (fallback != null)
                    {
                        SelectCharacter(fallback.id);
                    }
                }

                Confirm();
            }

            /// <summary>
            /// Writes the run and leaves.
            ///
            /// There are no six ints to copy any more: the screen has been mutating the live
            /// <see cref="GameState"/> all along, which is what makes
            /// <see cref="Wardrobe.TryEquip"/> and its refusals usable here at all, and what lets a
            /// scene-reloading language switch pick up exactly where it left off. So this trims the
            /// name one last time, makes sure the run knows who it is, and saves.
            /// </summary>
            void Confirm()
            {
                _state.playerName = CharacterPresets.Sanitize(_name);

                if (CharacterPresets.Get(_state.characterId) == null)
                {
                    PresetDef fallback = CharacterPresets.Default;
                    if (fallback == null)
                    {
                        Debug.LogError("[CharacterCreation] Creation is confirming with no character and " +
                                       "character_presets.json has none to fall back on. The run will have " +
                                       "no character id, so the wardrobe cannot protect a silhouette and " +
                                       "the backpack will infer the character from what is worn.");
                    }
                    else
                    {
                        SelectCharacter(fallback.id);
                    }
                }

                // Written now so the look survives the app being closed before the first day ends.
                SaveSystem.Save(_state);

                if (_onDone != null)
                {
                    Teardown();
                    _onDone();
                    return;
                }

                SceneManager.LoadScene(NextScene);
            }

            // ============================================================== small shared pieces

            /// <summary>
            /// The clay ring that says "this one", drawn over a control rather than by recolouring
            /// it: <see cref="VariantButton"/> owns a control's own colours across seven states and
            /// repaints them on the next pointer event, so a tint applied from here would survive
            /// exactly until the finger moved.
            ///
            /// Clay and not gold, everywhere on this screen and on the backpack: "the one you are
            /// wearing", "the character you chose", "the tone you picked" and "the tab you are on"
            /// are all the same idea and all wear the same colour.
            /// </summary>
            static Image BuildSelectionRing(RectTransform control)
            {
                Image ring = UIKit.CreatePanel(control, "Selected", DesignTokens.Brand.Primary,
                    UiSpriteKeys.FocusRing);
                UIKit.Stretch((RectTransform)ring.transform);
                ring.raycastTarget = false;
                UIKit.Layout(ring).ignoreLayout = true;
                ring.gameObject.SetActive(false);
                return ring;
            }

            /// <summary>
            /// The check beside the ring, because a state carried by a colour alone is a state some
            /// players cannot see. Top-right, which is the one corner nothing else on these controls
            /// wants.
            /// </summary>
            static Image BuildSelectionMark(RectTransform control)
            {
                Image mark = UIKit.CreateIcon(control, "SelectedMark", UiSpriteKeys.IconCheck,
                    DesignTokens.Brand.Primary, UIKit.IconSize);
                var markRect = (RectTransform)mark.transform;
                markRect.anchorMin = new Vector2(1f, 1f);
                markRect.anchorMax = new Vector2(1f, 1f);
                markRect.pivot = new Vector2(1f, 1f);
                markRect.anchoredPosition = new Vector2(-SelectionMarkInset, -SelectionMarkInset);
                mark.raycastTarget = false;
                UIKit.Layout(mark).ignoreLayout = true;
                mark.gameObject.SetActive(false);
                return mark;
            }

            /// <summary>The look to draw. Never null, so no code path here can blank the character.</summary>
            AppearanceState Look()
            {
                if (_state.appearance == null)
                {
                    _state.appearance = new AppearanceState();
                }

                return _state.appearance;
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
