using System;
using System.Globalization;
using SheepGate.Core;
using SheepGate.Economy;
using SheepGate.Player;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// One catalogue item, as a full-width row: the piece drawn on the character's own body, its
    /// name, its description, and — when it has not opened yet — a padlock and the condition in
    /// full.
    ///
    /// ==================================================================================
    /// WHY THIS IS A SHARED COMPONENT AND NOT A COPY IN EACH SCREEN
    /// ==================================================================================
    /// Two screens list the wardrobe now: the backpack, and the second step of character creation.
    /// The row they show has to be the same row, and not "the same row as far as anyone has
    /// noticed" — because almost everything that makes this row correct is arithmetic that looks
    /// like nothing when it drifts.
    ///
    /// Two of those numbers were measured on a real phone and cannot be re-derived by reading the
    /// design document:
    /// <list type="bullet">
    /// <item><b>The name line uses <see cref="DesignTokens.Space.S4"/>, not S8.</b> The layout
    /// contract sized the name box off a 200-point text column; a phone gives it 162.6, and at S8
    /// gaps the name has 72.7 points left — less than the widest single word in the catalogue.
    /// Legacy <see cref="Text"/> breaks a word it cannot fit, so "Túnica de carregador" rendered as
    /// "Túnica de / carregad / or". S4 gives sixteen points back and clears "ferramentas" (84).</item>
    /// <item><b>Every width comes from the caller's real content width</b>, which the caller derives
    /// from <see cref="UIKit.CanvasWidth"/> and never from <see cref="UIKit.ReferenceWidth"/>. The
    /// canvas is 1080 units wide only on a device whose aspect is exactly 1080x1920; a phone
    /// reports about 977, and a width pinned to the reference overflows by that difference.
    /// <c>tools/e2e.sh</c> launches the macOS player at exactly 1080x1920 and structurally cannot
    /// see it.</item>
    /// </list>
    ///
    /// ==================================================================================
    /// WHY A ROW CANNOT CLIP
    /// ==================================================================================
    /// Not "is unlikely to". Cannot. <b>Every width in the chain is decided before any text is
    /// measured</b>, so no <see cref="Text"/> is ever measured against a width it will not have,
    /// and every height is then pinned from that measurement:
    /// <list type="number">
    /// <item>the row is <see cref="Metrics.RowWidth"/>, derived from the container, not the frame;</item>
    /// <item>the text column is <see cref="Metrics.TextColumnWidth"/>, with <c>flexibleWidth = 0</c>;</item>
    /// <item>the status slot is width-permanent, so the name box never widens;</item>
    /// <item>each label is given its box, asked for its height, and pinned to both.</item>
    /// </list>
    /// There is no <see cref="ContentSizeFitter"/> anywhere in a row and no width is flexible, so
    /// nothing can renegotiate afterwards. The row's own height is composed in code —
    /// <c>max(48, 24 + max(thumb, column))</c> — rather than left to a layout group to discover.
    ///
    /// ==================================================================================
    /// THE RULES THIS ROW CARRIES
    /// ==================================================================================
    /// * <b>Rule 7 — never punish.</b> A locked row is the largest, most-written row on the screen:
    ///   the piece is drawn in full colour on the character's own body, its name is normal weight,
    ///   its description reads the same as everyone else's, and underneath it the condition is
    ///   spelled out whole in the brightest ink the row has. <see cref="Selectable.interactable"/>
    ///   stays true on purpose — the Secondary variant draws a non-interactable control at 0.40
    ///   opacity, which is exactly the disabled-looking void the rule forbids. Tapping a locked row
    ///   answers calmly and costs nothing.
    /// * <b>Rule 2 — never colour alone.</b> Worn is a clay ring <i>and</i> a check; locked is a
    ///   padlock. No state here is carried by a colour on its own.
    /// * <b>Rule 10 — no progress toward anything.</b> Nothing in a row counts, ranks or measures.
    /// * <b>Rule 18 — nothing is bought with money.</b> This entry used to read "nothing is bought:
    ///   no price, no currency, no timer", and after the extraction that is no longer the whole
    ///   truth: a locked row in the <i>backpack</i> carries a talent price. The rule it was
    ///   protecting is intact, and the distinction is the whole point. AGENTS.md's rule 18 forbids
    ///   <i>selling the shortcut</i> — monetisation may be a new season or a cosmetic, never a
    ///   resource, never a timer, never a shortcut past the work. Talents are earned by turning up,
    ///   spend only on cosmetics, and buy no stone, no timber, no stage of wall and no hour of
    ///   anyone's day. There is still no timer and no "unlock now", and no real money touches a row.
    ///   <para>
    ///   <b>Not finished, and knowingly so.</b> The price is displayed; nothing spends it yet. Until
    ///   the purchase path lands, every locked backpack row shows a number the player cannot pay,
    ///   which is in tension with rule 7 above — a locked row is supposed to be an invitation, and
    ///   an unpayable price is closer to a shopfront with the door locked. It is staged deliberately
    ///   rather than shipped as the finished design. The price is drawn <b>under</b> the unlock
    ///   sentence and never instead of it, so the route that <i>does</i> work is still the one the
    ///   row leads with and still its brightest line.
    ///   </para>
    ///
    /// ==================================================================================
    /// WHAT THE TWO CALLERS DO DIFFERENTLY, AND WHY
    /// ==================================================================================
    /// Two flags, and both of them are the difference between a wardrobe you are shopping in and a
    /// wardrobe you are being introduced to.
    /// <list type="bullet">
    /// <item><b><c>showNewBadge</c>: the backpack passes true, character creation passes false.</b>
    /// On a fresh save every free item is unlocked-and-never-seen, so creation would otherwise draw
    /// a NOVO badge on every row it lists — and if it also marked them seen, the first backpack open
    /// would arrive with no badges and no HUD pill, spending the one moment a badge exists to
    /// announce. Creation therefore shows no badge and marks nothing seen. Gold appears nowhere on
    /// that screen except the focus ring.</item>
    /// <item><b><c>showTalentPrice</c>: the backpack passes true, character creation leaves it at
    /// its default false.</b> Nothing is bought during creation — the player has no talents, no
    /// check-in has happened, and the screen's whole job is "here is who you are", not "here is what
    /// it costs". A price there would be worse than a price nowhere: it would open the game on a
    /// shopfront. False is the default for exactly that reason, so a third caller that has not
    /// thought about it gets the answer rule 18 would give.</item>
    /// </list>
    /// </summary>
    public static class WardrobeRow
    {
        // ------------------------------------------------------------------ locale keys
        // Every word a row shows is a key, and two tables feed it. These are ui.json, read with
        // Loc.T. An item's name and description come from catalog.json and arrive already merged
        // onto CatalogItemDef by CharacterCatalog. An unlock sentence comes from catalog.json's
        // "unlock" table through UnlockEvaluator.Sentence, which returns a finished sentence and
        // never a key.
        //
        // They keep their "backpack." namespace after the extraction on purpose. Each of them has
        // exactly one reader now — this file — so there is nowhere for two spellings to diverge,
        // and renaming a key nothing can disagree about is churn in two locale files for no
        // player-visible gain. The tab labels were the opposite case: two call sites, so they moved
        // to a shared "slot." namespace.
        //
        // The constants are named "...Key" on purpose: the content validator treats a const string
        // whose name ends in a player-text noun as a hardcoded sentence, and "Key" is the escape
        // hatch it offers for exactly this.

        const string NewBadgeKey = "backpack.new";
        const string UnnamedItemKey = "backpack.item.unnamed";
        const string UnavailableKey = "backpack.unavailable";

        // The accessible name of a row carries its state, and the punctuation joining the two is
        // authored inside the locale string rather than concatenated here. "{0}, em uso" is one
        // translatable sentence; name + ", " + state is three fragments and a comma nobody can
        // translate.
        const string StateEquippedKey = "backpack.state.equipped";
        const string StateLockedKey = "backpack.state.locked";
        const string StateNewKey = "backpack.state.new";

        // ------------------------------------------------------------------ fixed metrics
        // Reserved heights are computed from the type scale, never written as a float. A body line
        // is 15.17 design points of type in a 22.24-point line box, and the only honest way to say
        // that in code is size times leading.

        /// <summary>A body line's box: what one line of description or condition occupies.</summary>
        public static readonly float BodyLineHeight = DesignTokens.Type.Body * DesignTokens.Type.BodyLeading;

        static readonly float MinimumLineHeight = DesignTokens.Type.Minimum * DesignTokens.Type.BodyLeading;

        /// <summary>The thumbnail's side. Fixed: it is a figure box, not a share of the width.</summary>
        public static readonly float ThumbSize = DesignTokens.Px(56f);

        /// <summary>
        /// The lane a permanent vertical scrollbar occupies beside a list, and the gap before it.
        /// Named here because the row's width and the caller's column padding have to subtract the
        /// same number, and two screens now do the subtracting.
        /// </summary>
        public static readonly float ScrollbarWidth = DesignTokens.Space.S8;

        /// <summary>The NOVO badge's outside height: one minimum line and its padding.</summary>
        static readonly float BadgeHeight = MinimumLineHeight + 2f * DesignTokens.Space.S4;

        /// <summary>
        /// The gap between the three things in a row's name line — the name, the NOVO badge and the
        /// status slot — and the badge's own side padding. Both are <c>Space.S4</c> where the layout
        /// contract asks for <c>Space.S8</c>. See this class's summary: it is a phone-measured fix,
        /// and correcting it back to the contract's number reintroduces a broken word.
        /// </summary>
        static readonly float NameRowSpacing = DesignTokens.Space.S4;

        static readonly float BadgeSidePadding = DesignTokens.Space.S4;

        // ------------------------------------------------------------------ metrics

        /// <summary>
        /// The three widths a row is built from, derived once by the screen that owns the list.
        ///
        /// A struct passed down rather than mutable statics on this class. The backpack could get
        /// away with statics because one sheet exists at a time; two screens cannot, and a static
        /// that one screen sets and another reads is the quietest possible way for a row to be laid
        /// out against a width it does not have.
        /// </summary>
        public struct Metrics
        {
            /// <summary>The row's own outside width.</summary>
            public float RowWidth;

            /// <summary>What the thumbnail and the three gaps leave for words.</summary>
            public float TextColumnWidth;

            /// <summary>The thumbnail's side.</summary>
            public float ThumbSize;
        }

        /// <summary>
        /// The metrics for a list whose content column is <paramref name="contentWidth"/> wide.
        ///
        /// <b><paramref name="contentWidth"/> comes from the caller, derived from
        /// <see cref="UIKit.CanvasWidth"/> and clamped by the container's own <c>rect.width</c>
        /// when that is greater than 1. Never <see cref="UIKit.ReferenceWidth"/>.</b>
        ///
        /// The scrollbar's lane is subtracted here, which means the caller must also reserve it as
        /// right-hand padding on the column the rows are parented into — otherwise the row is built
        /// narrower than the space it is given and sits with a gap on its right. Both callers do it
        /// on the <see cref="VerticalLayoutGroup"/> of their scroll content.
        /// </summary>
        public static Metrics MetricsFor(float contentWidth)
        {
            float rowWidth = contentWidth - (ScrollbarWidth + DesignTokens.Space.S8);

            var metrics = new Metrics
            {
                RowWidth = rowWidth,
                ThumbSize = ThumbSize,
                TextColumnWidth = rowWidth - (3f * DesignTokens.Space.S12 + ThumbSize)
            };

            if (metrics.TextColumnWidth < DesignTokens.Px(80f))
            {
                Debug.LogError("[WardrobeRow] A content width of " + contentWidth + " leaves only " +
                               metrics.TextColumnWidth + " units for the text column. Item names and " +
                               "unlock sentences will break mid-word. The caller is deriving its width " +
                               "from something narrower than a phone.");
            }

            return metrics;
        }

        // ------------------------------------------------------------------ the view

        /// <summary>One built row, and the parts of it a repaint touches.</summary>
        public sealed class View
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
            /// <see cref="LayoutElement"/> in place and the arithmetic intact.
            /// </summary>
            public Image Status;

            /// <summary>
            /// The thumbnail's body layer, so the tone can be repainted without rebuilding the row.
            ///
            /// Character creation needs this and the backpack does not: creation lets the player
            /// change their skin tone on the previous step, come back, and expects every thumbnail
            /// to be wearing the piece on <i>their</i> body rather than on the one the row happened
            /// to be built with.
            /// </summary>
            public Image Body;

            /// <summary>The name as the row displays it, before any state word is folded in.</summary>
            public string ItemName;

            /// <summary>True when the item was locked at build time. Unlocks never regress.</summary>
            public bool Locked;

            /// <summary>True when the badge was showing at build time. Captured, never recomputed.</summary>
            public bool IsNew;

            /// <summary>The unlock sentence, reused verbatim in the accessible name.</summary>
            public string UnlockSentence;
        }

        // ------------------------------------------------------------------ building

        /// <summary>
        /// Builds one row and returns it, or null when the item cannot be drawn.
        ///
        /// Built on <see cref="UIKit.CreateButton(Transform, string, string, UIKit.ButtonVariant, Action)"/>
        /// so the row is a real design-system control with the focus ring, the hover and pressed
        /// fills and the hairline border that come with it, and then re-laid as a horizontal group.
        /// The pieces the kit already parented are marked <c>ignoreLayout</c> first: the border and
        /// the focus ring are stretched to the whole control and would otherwise be laid out as two
        /// more columns of the row.
        ///
        /// The handler is passed <i>through</i> the kit, which wraps it in
        /// <c>AudioDirector.Play(AudioKeys.Confirm)</c>. That is deliberate and is the opposite of
        /// what a tab bar does: an equip is a confirm, and it happens once per decision rather than
        /// a dozen times while comparing.
        /// </summary>
        /// <param name="metrics">Widths from <see cref="MetricsFor"/>, derived from the real canvas.</param>
        /// <param name="bodyArtVariant"><see cref="AppearanceState.BodyArtVariant"/> of whoever the row is drawn on.</param>
        /// <param name="locked">
        /// Captured by the caller from <see cref="Wardrobe.IsUnlocked"/>. Passed in rather than read
        /// here so that this class never reaches for a <see cref="GameState"/> it was not handed,
        /// and so that it sits beside <paramref name="isNew"/>, whose read order genuinely matters.
        /// </param>
        /// <param name="showNewBadge">False suppresses the NOVO pill entirely. Creation passes false.</param>
        /// <param name="isNew">
        /// Read by the caller <b>before</b> anything is marked seen. Every row is built while the
        /// badge is still there to read, and this flag is then never recomputed.
        /// </param>
        /// <param name="namePrefix">GameObject name prefix. Both callers pass "Chip_".</param>
        /// <param name="showTalentPrice">
        /// True draws the talent price under a locked row's unlock sentence. <b>Defaults to false,
        /// and the default is the rule rather than a convenience</b> — see the class summary: the
        /// backpack opts in, character creation does not, and a caller that has not decided gets the
        /// answer rule 18 would give. Trailing and optional on purpose, so that adding it could not
        /// silently reorder the three bools above it at an existing call site.
        /// </param>
        public static View Build(RectTransform content, CatalogItemDef item, CharacterSlot slot,
                                 Metrics metrics, int bodyArtVariant, bool locked, bool showNewBadge,
                                 bool isNew, string namePrefix, Action<string, CharacterSlot> onTap,
                                 bool showTalentPrice = false)
        {
            if (item == null || string.IsNullOrEmpty(item.id))
            {
                Debug.LogWarning("[WardrobeRow] An item in slot " + slot + " has no id and was skipped.");
                return null;
            }

            string itemId = item.id;
            bool badged = showNewBadge && isNew;

            // Resolved once and passed down: a missing name is an error in the log, and one row
            // should account for one line of it, not two.
            string itemName = DisplayName(item);

            // UnlockEvaluator.Sentence returns the finished sentence — resolved against
            // catalog.json's "unlock" table with the threshold and the plural already applied — so
            // it never passes through Loc, and the accessible name reuses this exact string rather
            // than rephrasing it. Two wordings of one condition are two conditions to a listener.
            string unlockSentence = locked ? UnlockEvaluator.Sentence(item.unlock_condition) : null;

            // No label text: the row builds its own beside a thumbnail, and a centred label
            // stretched across the whole control has nowhere to go in that arrangement.
            Action tap = onTap == null ? (Action)null : () => onTap(itemId, slot);
            Button chip = UIKit.CreateButton(content, namePrefix + itemId, string.Empty,
                UIKit.ButtonVariant.Secondary, tap);
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

            Image body;
            BuildThumb(chipRect, item, metrics, bodyArtVariant, out body);

            RectTransform textColumn = UIKit.CreateRect("Text", chipRect);
            UIKit.VerticalGroup(textColumn.gameObject, DesignTokens.Space.S12, new RectOffset());
            LayoutElement textLayout = UIKit.Layout(textColumn);
            textLayout.minWidth = metrics.TextColumnWidth;
            textLayout.preferredWidth = metrics.TextColumnWidth;
            textLayout.flexibleWidth = 0f;

            Image status;
            float nameRowHeight = BuildNameRow(textColumn, itemName, badged, metrics, out status);
            float columnHeight = nameRowHeight;

            string description = item.description;
            if (!string.IsNullOrWhiteSpace(description))
            {
                // Type.Body and Ink.Secondary. This is the string that tells visually identical
                // pieces apart, so it is prose rather than a label, and Type.Minimum is a floor for
                // labels. Ink.Muted measured 3.57:1 against a 4.5:1 requirement and is used nowhere.
                Text descriptionText = UIKit.CreateText(textColumn, "Description", description,
                    DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft,
                    DesignTokens.TypeRole.Body);
                columnHeight += DesignTokens.Space.S12 + PinText(descriptionText, metrics.TextColumnWidth);
            }

            if (locked && !string.IsNullOrEmpty(unlockSentence))
            {
                // Ink.Primary, which makes this the brightest line in a locked row, on purpose.
                // Rule 7 makes a locked row an invitation, and the sentence saying how to open it
                // is the invitation. The ink step from Secondary to Primary is also what separates
                // the condition from the description without spending vertical air.
                Text unlock = UIKit.CreateText(textColumn, "Unlock", unlockSentence,
                    DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                    DesignTokens.TypeRole.Body);
                columnHeight += DesignTokens.Space.S12 + PinText(unlock, metrics.TextColumnWidth);
            }

            if (locked && showTalentPrice)
            {
                // Under the unlock sentence, not instead of it. The sentence is still the row's
                // invitation and still its brightest line; the price is a second route that does
                // not yet exist, so it must not displace the one that does. The order of these two
                // blocks IS that promise — this one comes second and its condition says nothing
                // about whether the sentence was drawn, so a priced row can never be a row with no
                // way in written on it.
                columnHeight += DesignTokens.Space.S12
                    + BuildPriceRow(textColumn, metrics, TalentPrice.For(itemId));
            }

            textLayout.minHeight = columnHeight;
            textLayout.preferredHeight = columnHeight;
            textLayout.flexibleHeight = 0f;

            float rowHeight = Mathf.Max(UIKit.ButtonMinHeight,
                2f * DesignTokens.Space.S12 + Mathf.Max(metrics.ThumbSize, columnHeight));

            LayoutElement chipLayout = UIKit.Layout(chip);
            chipLayout.minWidth = metrics.RowWidth;
            chipLayout.preferredWidth = metrics.RowWidth;
            chipLayout.flexibleWidth = 0f;
            chipLayout.minHeight = rowHeight;
            chipLayout.preferredHeight = rowHeight;
            chipLayout.flexibleHeight = 0f;

            // Drawn over the row rather than by recolouring the kit's border: VariantButton owns
            // that border's colour and repaints it on the next pointer event, so a tint applied
            // from here would survive exactly until the finger moved. Clay and not gold — gold
            // means "not yet seen" and nothing else, so "the one you are wearing" is the same clay
            // as "the tab you are on".
            Image ring = UIKit.CreatePanel(chipRect, "Selected", DesignTokens.Brand.Primary,
                UiSpriteKeys.FocusRing);
            UIKit.Stretch((RectTransform)ring.transform);
            ring.raycastTarget = false;
            ring.gameObject.SetActive(false);
            UIKit.Layout(ring).ignoreLayout = true;

            var view = new View
            {
                ItemId = itemId,
                Slot = slot,
                Root = chip.gameObject,
                Ring = ring,
                Status = status,
                Body = body,
                ItemName = itemName,
                Locked = locked,
                IsNew = badged,
                UnlockSentence = unlockSentence
            };

            AccessibleLabel.Apply(view.Root, AccessibleNameFor(view, false));
            return view;
        }

        /// <summary>
        /// What a list says when its slot draws nothing. Plainly, and without blame: nothing the
        /// player did caused it, and nothing of theirs was lost.
        ///
        /// A list that draws nothing still exists, is still enabled, and says so in a sentence. The
        /// alternative — remove the section, leave the screen one heading shorter — is both the
        /// layout instability Apple warns about and a quiet punishment for not having found
        /// anything yet.
        /// </summary>
        public static void BuildEmpty(RectTransform content, Metrics metrics)
        {
            Text empty = UIKit.CreateText(content, "Empty", Loc.T(UnavailableKey), DesignTokens.Type.Body,
                DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);
            PinText(empty, metrics.RowWidth);
        }

        /// <summary>
        /// The name, the badge when the piece is new, and the one status icon the row can carry.
        /// Returns the line's height and hands back the status image, so a repaint can swap its
        /// sprite without walking the hierarchy.
        ///
        /// <b>The status slot is width-permanent.</b> It is built whether or not it has anything to
        /// show, and hidden with <see cref="Behaviour.enabled"/> rather than by deactivating it. A
        /// layout group drops an inactive child; the name label would then widen by an icon's
        /// width, a name that was one line could rewrap to two, and the height pinned a moment ago
        /// would clip. It would only show on the rows nobody is wearing.
        ///
        /// The badge needs no such treatment: whether it is drawn is captured once at build time
        /// and cannot change while the list is up.
        ///
        /// The badge's width is measured rather than assumed. NOVO and NEW are different lengths
        /// and a third locale is a third length, so the name box is
        /// <c>column − (icon + gap) − (badge + gap)</c> computed from the badge actually built.
        /// </summary>
        static float BuildNameRow(RectTransform textColumn, string itemName, bool badged, Metrics metrics,
                                  out Image status)
        {
            RectTransform row = UIKit.CreateRect("Name", textColumn);
            UIKit.HorizontalGroup(row.gameObject, NameRowSpacing, new RectOffset(), TextAnchor.MiddleLeft);

            Text nameLabel = UIKit.CreateText(row, "Label", itemName, DesignTokens.Type.Body,
                DesignTokens.Ink.Primary, TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);

            float badgeWidth = 0f;
            if (badged)
            {
                badgeWidth = BuildNewBadge(row);
            }

            // Gold when it marks what is new; clay when it marks what is worn; the quieter
            // secondary ink under the padlock, because a lock is neither new nor current and gold
            // would say it was. The sprite and the colour are both set in Apply.
            status = UIKit.CreateIcon(row, "Status", UiSpriteKeys.IconCheck,
                DesignTokens.Brand.Primary, UIKit.IconSize);
            status.enabled = false;

            float nameBox = metrics.TextColumnWidth - (UIKit.IconSize + NameRowSpacing);
            if (badged)
            {
                nameBox -= badgeWidth + NameRowSpacing;
            }

            float labelHeight = PinText(nameLabel, nameBox);

            float height = Mathf.Max(labelHeight, UIKit.IconSize);
            if (badged)
            {
                height = Mathf.Max(height, BadgeHeight);
            }

            LayoutElement rowLayout = UIKit.Layout(row);
            rowLayout.minWidth = metrics.TextColumnWidth;
            rowLayout.preferredWidth = metrics.TextColumnWidth;
            rowLayout.flexibleWidth = 0f;
            rowLayout.minHeight = height;
            rowLayout.preferredHeight = height;
            rowLayout.flexibleHeight = 0f;

            return height;
        }

        /// <summary>
        /// The talent price on a locked row: the coin, then the number, in gold. Returns its height.
        ///
        /// The same <see cref="UiSpriteKeys.IconCoin"/> the HUD spends for a talent balance, on
        /// purpose — a second coin glyph invented for prices would read as a second currency. It
        /// carries no caption for the reason the HUD's talents readout gives: a coin beside a number
        /// is legible as money without one.
        ///
        /// <b>Gold carries two meanings on a sheet that shows this, and they are told apart by
        /// shape.</b> "Not yet seen" is the NOVO badge and the tab dot — always a filled shape with
        /// no glyph in it, always cleared by looking. "A talent" is this coin — always the coin
        /// glyph, always beside a number. The two can never collide in one row:
        /// <see cref="Wardrobe.IsNew"/> refuses a badge on a locked item, and only a locked item is
        /// priced.
        ///
        /// <b>The known risk, recorded rather than designed away.</b> A coin and a number on a row
        /// can be misread as "you have 12" instead of "this costs 12". The backpack answers that by
        /// carrying the balance in its header, on all four tabs; the word that would remove the
        /// ambiguity outright was left off because the brief asked for the icon and the value. If
        /// playtesting shows the misread, a label is the fix, not a different glyph.
        ///
        /// Mono for the number, like every other quantity in the game, so the digits are tabular and
        /// a two-digit price does not shuffle the row against a one-digit one.
        /// </summary>
        static float BuildPriceRow(RectTransform textColumn, Metrics metrics, int price)
        {
            RectTransform row = UIKit.CreateRect("Price", textColumn);
            UIKit.HorizontalGroup(row.gameObject, NameRowSpacing, new RectOffset(), TextAnchor.MiddleLeft);

            UIKit.CreateIcon(row, "Icon", UiSpriteKeys.IconCoin, DesignTokens.Brand.Secondary,
                UIKit.IconSize);

            Text amount = UIKit.CreateText(row, "Amount", price.ToString(CultureInfo.InvariantCulture),
                DesignTokens.Type.Mono, DesignTokens.Brand.Secondary, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Mono);

            float amountHeight = PinText(amount,
                metrics.TextColumnWidth - (UIKit.IconSize + NameRowSpacing));
            float height = Mathf.Max(amountHeight, UIKit.IconSize);

            LayoutElement rowLayout = UIKit.Layout(row);
            rowLayout.minWidth = metrics.TextColumnWidth;
            rowLayout.preferredWidth = metrics.TextColumnWidth;
            rowLayout.flexibleWidth = 0f;
            rowLayout.minHeight = height;
            rowLayout.preferredHeight = height;
            rowLayout.flexibleHeight = 0f;

            return height;
        }

        /// <summary>
        /// The NOVO badge, and the width it took.
        ///
        /// Gold, because gold means "not yet seen" and that is this badge's whole job. It is not a
        /// call to action and does not spend the one gold action a screen is allowed.
        ///
        /// Never on a locked item. <see cref="Wardrobe.IsNew"/> already refuses one, which is what
        /// keeps the announcement for the day the item actually opens — so a badge and a padlock
        /// can never appear in the same row.
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
        /// On the body and not alone, because several items in this catalogue share art indices
        /// with items the player already has, and a bracelet floating on a blank square is
        /// unreadable at any size. Seeing it worn is also what makes a locked row an invitation
        /// rather than a listing: the player is looking at themselves in it.
        /// </summary>
        static void BuildThumb(RectTransform chipRect, CatalogItemDef item, Metrics metrics,
                               int bodyArtVariant, out Image body)
        {
            RectTransform thumb = UIKit.CreateRect("Thumb", chipRect);
            LayoutElement thumbLayout = UIKit.Layout(thumb);
            thumbLayout.minWidth = metrics.ThumbSize;
            thumbLayout.preferredWidth = metrics.ThumbSize;
            thumbLayout.minHeight = metrics.ThumbSize;
            thumbLayout.preferredHeight = metrics.ThumbSize;
            thumbLayout.flexibleWidth = 0f;
            thumbLayout.flexibleHeight = 0f;

            Image surface = UIKit.CreatePanel(thumb, "Surface", DesignTokens.Surface.Panel,
                UiSpriteKeys.FrameSm);
            UIKit.Stretch((RectTransform)surface.transform);
            surface.raycastTarget = false;

            RectTransform figureArea = UIKit.CreateRect("FigureArea", thumb);
            UIKit.Stretch(figureArea, DesignTokens.Space.S4, DesignTokens.Space.S4,
                          DesignTokens.Space.S4, DesignTokens.Space.S4);

            RectTransform figure = CharacterFigure.CreateFigureRect(figureArea, "Figure");

            body = CharacterFigure.BuildLayer(figure, "Body");
            CharacterFigure.ApplyBody(body, bodyArtVariant, ShadeIndexFor(bodyArtVariant),
                FacingDirection.Down);

            // The plain block, never the hooded one: a thumbnail shows the piece itself, and which
            // variant it draws in a set depends on what else is worn, which the row cannot know.
            ItemArtDef art = CharacterCatalog.ArtFor(item, false);
            if (art == null)
            {
                return;
            }

            if (art.legs.HasValue)
            {
                CharacterFigure.ApplyLayer(CharacterFigure.BuildLayer(figure, "Legs"),
                    UIKit.GetSprite(UiSpriteKeys.Legs(art.legs.Value)),
                    CharacterFigure.Shade(DesignTokens.Ambient.Sky, art.legs.Value), 0.04f, 0.44f);
            }

            if (art.top.HasValue)
            {
                CharacterFigure.ApplyLayer(CharacterFigure.BuildLayer(figure, "Top"),
                    UIKit.GetSprite(UiSpriteKeys.Top(art.top.Value)),
                    CharacterFigure.Shade(DesignTokens.Brand.Primary, art.top.Value), 0.44f, 0.76f);
            }

            if (art.accessory.HasValue)
            {
                CharacterFigure.ApplyLayer(CharacterFigure.BuildLayer(figure, "Accessory"),
                    UIKit.GetSprite(UiSpriteKeys.Accessory(art.accessory.Value)),
                    CharacterFigure.Shade(DesignTokens.Ambient.Growth, art.accessory.Value), 0.78f, 0.97f);
            }

            if (art.hair.HasValue)
            {
                CharacterFigure.ApplyLayer(CharacterFigure.BuildLayer(figure, "Hair"),
                    UIKit.GetSprite(SheepGate.Art.ArtKeys.Hair(art.hair.Value,
                        UiSpriteKeys.ToArtFacing(FacingDirection.Down))),
                    CharacterFigure.Shade(DesignTokens.Ambient.Sky, art.hair.Value), 0.76f, 1f);
            }
        }

        // ------------------------------------------------------------------ repainting

        /// <summary>
        /// Repaints one row's state: the ring, the status icon and the announced name.
        ///
        /// <paramref name="worn"/> is what the slot is actually showing, not merely what is in the
        /// worn list — the accessory slot may legitimately hold several pieces while
        /// <see cref="AppearanceState"/> has one accessory layer, so marking every one of them
        /// would put a check beside items the figure is not drawing. The caller answers it with
        /// <c>Wardrobe.EquippedInSlot(state, view.Slot) == view.ItemId</c>.
        /// </summary>
        public static void Apply(View view, bool worn)
        {
            if (view == null)
            {
                return;
            }

            if (view.Ring != null)
            {
                view.Ring.gameObject.SetActive(worn);
            }

            if (view.Status != null)
            {
                if (worn)
                {
                    view.Status.sprite = UIKit.GetSprite(UiSpriteKeys.IconCheck);
                    view.Status.color = DesignTokens.Brand.Primary;
                    view.Status.enabled = true;
                }
                else if (view.Locked)
                {
                    view.Status.sprite = UIKit.GetSprite(UiSpriteKeys.IconLock);
                    view.Status.color = DesignTokens.Ink.Secondary;
                    view.Status.enabled = true;
                }
                else
                {
                    // Hidden, never removed. The layout slot it occupies is what keeps the name box
                    // the width every height in this row was measured against.
                    view.Status.enabled = false;
                }
            }

            // Re-applied here and not only at build time: the announced state would otherwise go
            // stale the moment the player equipped something.
            AccessibleLabel.Apply(view.Root, AccessibleNameFor(view, worn));
        }

        /// <summary>
        /// Repaints only the body under the thumbnail's piece.
        ///
        /// Character creation calls this on every visible row after the player changes their skin
        /// tone: the piece is unchanged, the person wearing it is not, and rebuilding the list to
        /// say so would throw away the scroll position and every measured height with it.
        /// </summary>
        public static void ApplyBody(View view, int bodyArtVariant)
        {
            if (view == null || view.Body == null)
            {
                return;
            }

            CharacterFigure.ApplyBody(view.Body, bodyArtVariant, ShadeIndexFor(bodyArtVariant),
                FacingDirection.Down);
        }

        /// <summary>
        /// The fallback shade index a thumbnail's body must degrade to, unpacked from the packed
        /// variant it is drawn with.
        ///
        /// <b>It is the build, and it has to be.</b> A body sprite that is missing degrades to
        /// <see cref="CharacterFigure.Shade"/> of <c>Neutral.N500</c> walked by an index, and
        /// <see cref="CharacterFigure.Apply"/> — the painter every full figure in the game goes
        /// through — walks it by <c>AppearanceState.body</c>, the build. A thumbnail that passed a
        /// flat 0 instead would draw a build-1 character in a different colour from the stage
        /// directly above it, on the one code path where nobody would ever notice: the path that
        /// only runs when an art layer is missing in the first place. That is precisely the "two
        /// screens drawing the same character differently" bug <see cref="CharacterFigure"/> exists
        /// to prevent.
        ///
        /// <c>BodyArtVariant</c> is <c>build * SkinTones + tone</c>, so the integer division is the
        /// build back out again, exactly and without the caller having to hand it over separately.
        /// </summary>
        static int ShadeIndexFor(int bodyArtVariant)
        {
            return Mathf.Max(0, bodyArtVariant) / AppearanceState.SkinTones;
        }

        // ------------------------------------------------------------------ the tap

        /// <summary>
        /// A tap on a row. Three outcomes, and none of them takes anything away:
        /// <list type="bullet">
        /// <item><b>The piece is not worn.</b> It goes on, through <see cref="Wardrobe.TryEquip"/>,
        /// which does the swap a one-item slot needs and asks the catalogue about the set that
        /// would result before it writes anything. A refusal leaves the player dressed exactly as
        /// they were and puts one calm sentence on screen.</item>
        /// <item><b>The piece is worn, in a slot that holds several.</b> It comes off.</item>
        /// <item><b>The piece is worn, in a slot that holds one.</b> Nothing happens. A hairstyle
        /// and an outfit are always being worn, so a second tap on the current one is not a way to
        /// undress — it is a tap on the answer that is already selected. The way out of a look is
        /// another look.</item>
        /// </list>
        ///
        /// A locked row is tapped through this same path on purpose. It is a live control, it
        /// answers with the refusal key the wardrobe hands back, and it costs nothing — the
        /// alternative, disabling it, is what would draw the row at 0.40 opacity and turn an
        /// invitation into the disabled void rule 7 forbids.
        /// </summary>
        /// <param name="refusalSentence">
        /// Null on success; otherwise the finished sentence, already through <see cref="Loc"/>.
        /// </param>
        /// <returns>True when the tap changed something or was a deliberate no-op.</returns>
        public static bool Tap(GameState state, string itemId, CharacterSlot slot, out string refusalSentence)
        {
            refusalSentence = null;

            if (string.Equals(Wardrobe.EquippedInSlot(state, slot), itemId, StringComparison.Ordinal))
            {
                if (CharacterCatalog.SlotHoldsMany(slot))
                {
                    Wardrobe.Unequip(state, itemId);
                }

                return true;
            }

            string refusalKey;
            if (Wardrobe.TryEquip(state, itemId, out refusalKey))
            {
                return true;
            }

            refusalSentence = string.IsNullOrEmpty(refusalKey) ? null : Loc.T(refusalKey);
            return false;
        }

        // ------------------------------------------------------------------ shared helpers

        /// <summary>
        /// Gives a label its box, asks it how tall it is in that box, and pins it to both.
        ///
        /// This is the whole anti-clipping mechanism in four lines, and it is public because three
        /// screens need it now. Legacy <see cref="Text"/> measures its wrapped height against
        /// <c>rect.width</c>, so the width has to be real before the question is asked — which is
        /// why the anchors are collapsed to a point first, making <c>sizeDelta</c> the actual size
        /// rather than an offset from a parent that has not been laid out yet. Both dimensions then
        /// go onto a <see cref="LayoutElement"/> with zero flexibility, so no layout group
        /// downstream can renegotiate the width the height was measured against.
        /// </summary>
        public static float PinText(Text text, float boxWidth)
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
        /// reserve is a design decision instead of a consequence: a refusal line's headroom, the
        /// one line a player's own name is allowed, and a label that is empty at build time and
        /// would otherwise measure as nothing.
        /// </summary>
        public static void PinTextBox(Text text, float boxWidth, float height)
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
        /// The name a screen reader would say, with the row's state folded into it.
        ///
        /// Composed name-first and nested, so the separators and the word order are the locale's
        /// business and no punctuation literal reaches this file: the base name, then
        /// <c>backpack.state.new</c> around it if it is new, then <c>backpack.state.equipped</c>
        /// around that if it is worn. A locked row takes a single form carrying the unlock sentence
        /// <b>verbatim</b> — the same sentence the row prints, never a rephrasing of it.
        ///
        /// <b>A locked row must never map to a disabled state in speech.</b> Rule 7 keeps it fully
        /// interactable, and announcing it as unavailable would undo in speech exactly what the
        /// visual design refuses to do.
        /// </summary>
        static string AccessibleNameFor(View view, bool worn)
        {
            if (view == null)
            {
                return string.Empty;
            }

            if (view.Locked)
            {
                return Loc.T(StateLockedKey, view.ItemName, view.UnlockSentence ?? string.Empty);
            }

            string composed = view.ItemName;

            if (view.IsNew)
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

            Debug.LogError("[WardrobeRow] Item '" + (item != null ? item.id : "null") +
                           "' has no display name in locale " + CharacterCatalog.LoadedLocale +
                           ". Add it to Resources/Data/locales/" + CharacterCatalog.LoadedLocale +
                           "/catalog.json under \"items\".");
            return Loc.T(UnnamedItemKey);
        }
    }
}
