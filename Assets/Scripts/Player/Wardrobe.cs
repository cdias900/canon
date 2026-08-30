using System;
using System.Collections.Generic;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.Player
{
    /// <summary>
    /// The state layer behind the backpack: what the player is wearing, what still carries a "new"
    /// badge, and what happens to the six <see cref="AppearanceState"/> ints when either changes.
    ///
    /// Everything the wardrobe knows is read from two places and nowhere else: the catalogue
    /// (<see cref="CharacterCatalog"/>, which owns slots, anchors and every compatibility rule) and
    /// the two lists on <see cref="GameState"/>. This class adds no rule of its own; it sequences
    /// the ones that already exist and raises <see cref="Changed"/> so whoever draws the character
    /// can repaint. It never reaches into the scene, never loads a scene, and never allocates more
    /// than a short list per call, which is what keeps a repaint well inside a frame.
    ///
    /// ==================================================================================
    /// THREE PRODUCT RULES THIS CLASS HOLDS, AND HOW
    /// ==================================================================================
    /// * <b>Rule 7 — never punish.</b> Nothing here can take something away. A locked item is
    ///   refused with a locale key the panel prints beside the padlock and the condition, not
    ///   hidden and not silently dropped; a refused equip leaves the worn set exactly as it was; a
    ///   swap in a one-item slot is a swap, never a rejection (<see cref="CharacterCatalog.SlotHoldsMany"/>
    ///   says which slots those are); and no code path removes an id from <c>seenItems</c> or
    ///   re-locks an item — <see cref="UnlockEvaluator"/> only reads quantities that never fall.
    /// * <b>Rule 10 — never show progress toward a vocation.</b> This class reads no counter and no
    ///   score. It asks <see cref="UnlockEvaluator"/> a yes/no question and prints nothing but the
    ///   key that evaluator hands back. There is no "2 of 3" to compute here because there is no
    ///   fraction in the vocabulary to compute it from.
    /// * <b>Rule 18 — nothing is bought.</b> No price, no currency, no cost of any kind appears in
    ///   this file or in the data it reads. An item is available or it is not, and the only thing
    ///   that makes it available is something the player did in the world.
    ///
    /// ==================================================================================
    /// WHY <see cref="ApplyToAppearance"/> APPLIES NO CHARACTER FLOOR
    /// ==================================================================================
    /// <see cref="CharacterCatalog.Compose"/> can lay a character's own art down as a floor before
    /// the worn items go on top, and <see cref="CharacterPresets.ApplyTo"/> is the call that does
    /// exactly that — it is the character-creation path, and it runs once, when a character is
    /// chosen.
    ///
    /// Recomposing the look after an equip is a different job and must not do it. No catalogue item
    /// writes the <c>body</c> or <c>skin</c> layer — nothing in character_catalog.json names either
    /// one — so a floor pass on every equip would overwrite the build and skin tone the player set
    /// in creation, every time they put on a hat. Silently changing something the player chose is
    /// the punishment rule 7 forbids, and it would be invisible in the log.
    ///
    /// So the floor here is <b>the look the character already has</b>. Layers no worn item claims
    /// keep their current value, which is both what the task asks for and the only shape in which
    /// the character can never come out blank: with an empty worn set, an unauthored catalogue or
    /// an item id that does not resolve, the worst case is that the look does not change.
    ///
    /// ==================================================================================
    /// CONTENT GAPS THIS CLASS DEGRADES AROUND RATHER THAN CRASHING ON
    /// ==================================================================================
    /// * Five ids in character_presets.json (<c>base_adar</c>, <c>base_neriah</c>,
    ///   <c>hair_tied_back</c>, <c>outfit_carrier_tunic</c>, <c>outfit_surveyor_wrap</c>) are not in
    ///   the catalogue, and the <c>base</c> slot has no items at all — <see cref="AppearanceState"/>
    ///   has five render layers and no base layer. An unresolvable id is warned about once per
    ///   session and then skipped everywhere, and <see cref="ItemsForSlot"/> answering
    ///   <see cref="CharacterSlot.Base"/> with an empty array is correct, not a failure.
    /// * <see cref="AppearanceState"/> has one accessory layer, so only one accessory can draw even
    ///   though the accessory slot may legitimately hold several on distinct anchors. The most
    ///   recently equipped one wins, because a tap that changes nothing on screen reads as broken.
    /// * <c>tint_channels</c> is declared per item and there is nowhere in the save to put a swatch
    ///   choice. Recolouring is therefore not a thing this layer can persist, and it does not
    ///   pretend to: no method here reads or writes a tint.
    /// </summary>
    public static class Wardrobe
    {
        // ------------------------------------------------------------------ locale keys
        // A refusal is a key, never a sentence: the words live in
        // Resources/Data/locales/<locale>/ui.json and are read with Loc.T by whoever shows them.
        // The keys are constants so the panel and this file cannot spell one differently.

        /// <summary>Prefix every refusal key shares.</summary>
        public const string KeyRefusalPrefix = "backpack.refusal.";

        /// <summary>The item has not opened yet. The panel prints the condition itself, in full.</summary>
        public const string KeyRefusalLocked = KeyRefusalPrefix + "locked";

        /// <summary>The id is not in the catalogue this build shipped with. A content mistake.</summary>
        public const string KeyRefusalUnknownItem = KeyRefusalPrefix + "unknown_item";

        /// <summary>One of the two items names the other in its <c>incompatible_with</c> list.</summary>
        public const string KeyRefusalIncompatible = KeyRefusalPrefix + "incompatible";

        /// <summary>Something is already hanging on that anchor.</summary>
        public const string KeyRefusalSameAnchor = KeyRefusalPrefix + "same_anchor";

        /// <summary>A cloak has taken over both shoulders.</summary>
        public const string KeyRefusalCloakShoulder = KeyRefusalPrefix + "cloak_shoulder";

        /// <summary>The piece would cover the character's silhouette anomaly.</summary>
        public const string KeyRefusalCoversSilhouette = KeyRefusalPrefix + "covers_silhouette";

        // ------------------------------------------------------------------ the event

        /// <summary>
        /// Raised after the worn set or the badge state actually changed — never on a no-op, so a
        /// panel sweeping a whole slot of already-seen items fires nothing.
        ///
        /// Subscribers repaint; this class does not know what they are. A subscriber that throws is
        /// logged and skipped rather than allowed to leave the wardrobe half applied, which is what
        /// makes a stale subscriber from a torn-down scene an error in the log instead of a broken
        /// backpack. Scene teardown should still call <see cref="ClearSubscribers"/>.
        /// </summary>
        public static event Action Changed;

        static readonly CatalogItemDef[] NoItems = Array.Empty<CatalogItemDef>();

        // The slots the backpack actually draws, and therefore the only slots a badge can live in.
        // Both badge counts are sums over exactly this array — NewCount explains why that has to be
        // one set and not two, and why CharacterSlot.Base is absent from it rather than filtered out
        // further down.
        static readonly CharacterSlot[] BadgedSlotsInternal =
        {
            CharacterSlot.Hair,
            CharacterSlot.Outfit,
            CharacterSlot.Accessory
        };

        /// <summary>
        /// The slots that can carry a badge, in the order the backpack shows them.
        ///
        /// Exposed because the panel used to keep its own copy of this list, and the comment on
        /// <see cref="NewCount"/> claimed the two were one set when they were two. They agreed, so
        /// nothing was wrong today, and that is exactly the state a silent divergence starts from.
        /// The panel now builds its tab order from this, so "the badge total and the tabs walk the
        /// same set" is a fact about the program rather than a promise in a comment.
        /// </summary>
        public static IReadOnlyList<CharacterSlot> BadgedSlots
        {
            get { return BadgedSlotsInternal; }
        }

        // One warning per unresolvable id per session. A worn id that is not in the catalogue is a
        // content mistake worth seeing once; repeating it on every recompose would bury the log
        // under the same line and hide the next problem.
        static readonly HashSet<string> WarnedUnresolved = new HashSet<string>(StringComparer.Ordinal);

        static bool _presetsRequested;

        // ------------------------------------------------------------------ reading the catalogue

        /// <summary>
        /// Every catalogue item in this slot, in authored order, <b>locked ones included</b>.
        ///
        /// Locked items belong in the answer: rule 7 says a locked item is an invitation, and a
        /// wardrobe that hid what it cannot yet offer would look smaller than it is and would give
        /// the player nothing to want. Filtering by <see cref="IsUnlocked"/> is the caller's job,
        /// and the only correct use of it is deciding whether to draw a padlock.
        ///
        /// <see cref="CharacterSlot.Base"/> answers empty and always will: the catalogue has no
        /// base items and <see cref="AppearanceState"/> has no base layer to draw one into.
        /// </summary>
        public static CatalogItemDef[] ItemsForSlot(CharacterSlot slot)
        {
            CatalogItemDef[] all = CharacterCatalog.Items;
            if (all == null || all.Length == 0)
            {
                return NoItems;
            }

            var matching = new List<CatalogItemDef>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                CatalogItemDef item = all[i];
                if (BelongsToSlot(item, slot))
                {
                    matching.Add(item);
                }
            }

            return matching.Count == 0 ? NoItems : matching.ToArray();
        }

        /// <summary>Whether this item is unlocked in this run. A null run reads as unlocked.</summary>
        public static bool IsUnlocked(GameState state, string itemId)
        {
            CatalogItemDef item = CharacterCatalog.Item(itemId);
            return item != null && UnlockEvaluator.IsUnlocked(state, item);
        }

        // ------------------------------------------------------------------ what is worn

        /// <summary>Whether this exact item id is in the worn set.</summary>
        public static bool IsEquipped(GameState state, string itemId)
        {
            if (state == null || string.IsNullOrEmpty(itemId))
            {
                return false;
            }

            return Contains(Equipped(state), itemId);
        }

        /// <summary>
        /// The item worn in this slot, or null. Only meaningful for a slot that holds one; for
        /// <see cref="CharacterSlot.Accessory"/> it answers the last one put on, which is also the
        /// one the single accessory layer draws.
        /// </summary>
        public static string EquippedInSlot(GameState state, CharacterSlot slot)
        {
            if (state == null)
            {
                return null;
            }

            List<string> equipped = Equipped(state);
            for (int i = equipped.Count - 1; i >= 0; i--)
            {
                CatalogItemDef item = CharacterCatalog.Item(equipped[i]);
                if (item != null && item.Slot == slot)
                {
                    return item.id;
                }
            }

            return null;
        }

        /// <summary>
        /// Puts an item on, if the catalogue allows it beside what is already worn.
        ///
        /// The order is: resolve the id, refuse a locked item, let a one-item slot swap out its
        /// occupant, then ask <see cref="CharacterCatalog.CanEquip"/> about the set that would
        /// result. Nothing is written until that question comes back yes, so a refusal cannot leave
        /// the player half undressed — which is the point of doing the swap against a candidate list
        /// rather than against the real one.
        ///
        /// An item already worn is a success with nothing to do: tapping what you are wearing is not
        /// a mistake, and reporting it as a refusal would put a message on screen for a no-op.
        /// </summary>
        /// <param name="refusalKey">
        /// A locale key for ui.json explaining why, or null on success. Never a sentence, and never
        /// a scolding one when it is resolved: a refusal here means two pieces do not go together,
        /// not that the player did something wrong, and nothing was lost by trying.
        /// </param>
        public static bool TryEquip(GameState state, string itemId, out string refusalKey)
        {
            refusalKey = null;

            if (state == null)
            {
                Debug.LogWarning("[Wardrobe] TryEquip was given no game state; nothing was equipped.");
                refusalKey = KeyRefusalUnknownItem;
                return false;
            }

            CatalogItemDef item = CharacterCatalog.Item(itemId);
            if (item == null)
            {
                Debug.LogWarning("[Wardrobe] '" + (itemId ?? "null") +
                                 "' is not in character_catalog.json; nothing was equipped.");
                refusalKey = KeyRefusalUnknownItem;
                return false;
            }

            List<string> equipped = Equipped(state);
            if (Contains(equipped, item.id))
            {
                return true;
            }

            if (!UnlockEvaluator.IsUnlocked(state, item))
            {
                refusalKey = KeyRefusalLocked;
                return false;
            }

            // A slot that holds one takes the newcomer and lets the occupant go — the catalogue is
            // explicit that this is a swap and never a refusal. Building the candidate set first
            // means the compatibility question is asked about the set that would actually exist.
            bool swaps = !CharacterCatalog.SlotHoldsMany(item.Slot);
            var candidate = new List<string>(equipped.Count + 1);
            for (int i = 0; i < equipped.Count; i++)
            {
                CatalogItemDef worn = CharacterCatalog.Item(equipped[i]);
                if (swaps && worn != null && worn.Slot == item.Slot)
                {
                    continue;
                }

                candidate.Add(equipped[i]);
            }

            EquipConflict conflict;
            if (!CharacterCatalog.CanEquip(item.id, candidate, CharacterId(state), out conflict))
            {
                refusalKey = RefusalKey(conflict);
                return false;
            }

            // Committed. The newcomer goes last so that, where two worn items share one art layer,
            // the piece the player just chose is the one that draws.
            equipped.Clear();
            equipped.AddRange(candidate);
            equipped.Add(item.id);

            ApplyToAppearance(state);
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Takes an item off. Silent and harmless when it was not on.
        ///
        /// The layers it was drawing keep their last value rather than resetting to anything: no
        /// worn item claims them any more, and "leave what nobody claims alone" is what stops a
        /// character ever going blank. Putting a different piece on the same slot is what changes
        /// them, and that is the ordinary way out of a look the player is done with.
        /// </summary>
        public static void Unequip(GameState state, string itemId)
        {
            if (state == null || string.IsNullOrEmpty(itemId))
            {
                return;
            }

            List<string> equipped = Equipped(state);
            bool removed = false;
            for (int i = equipped.Count - 1; i >= 0; i--)
            {
                if (string.Equals(equipped[i], itemId, StringComparison.Ordinal))
                {
                    equipped.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            ApplyToAppearance(state);
            RaiseChanged();
        }

        // ------------------------------------------------------------------ the badge

        /// <summary>
        /// Whether this item still carries its badge: unlocked, and never looked at.
        ///
        /// A locked item is never new — its moment is the one where it opens, and announcing it
        /// early would spend the only surprise the wardrobe has.
        ///
        /// The test itself lives in <c>CarriesBadge</c>, which is also what the two counts below
        /// walk, so there is no reading of "new" that can be true for one item and false for the
        /// number that is supposed to include it.
        /// </summary>
        public static bool IsNew(GameState state, string itemId)
        {
            if (state == null || string.IsNullOrEmpty(itemId))
            {
                return false;
            }

            return CarriesBadge(state, Seen(state), CharacterCatalog.Item(itemId));
        }

        /// <summary>
        /// Spends the badge. Idempotent, and <see cref="Changed"/> is raised only when a badge was
        /// actually there to spend, so a panel marking a whole slot on open fires at most once.
        ///
        /// <b>A locked item is never marked.</b> The panel marks everything in a slot when the
        /// player looks at it, and locked items sit in those slots with their silhouette and their
        /// padlock showing. Marking one seen now would mean that on the day it finally opens it
        /// arrives with no badge at all — the wardrobe would have quietly thrown away the one
        /// moment the badge exists to announce.
        /// </summary>
        public static void MarkSeen(GameState state, string itemId)
        {
            if (!IsNew(state, itemId))
            {
                return;
            }

            Seen(state).Add(CharacterCatalog.Item(itemId).id);
            RaiseChanged();
        }

        /// <summary>
        /// Marks every item of one slot, as a slot's worth of badges expiring on open.
        ///
        /// Returns whether it actually spent anything, which is the same thing as whether it raised
        /// <see cref="Changed"/>. A caller that repaints on that event needs to know: without it,
        /// the honest choices are to repaint twice on every call or to repaint never, and a tab tap
        /// went through the first of those for four segments and eighteen rows.
        /// </summary>
        public static bool MarkSlotSeen(GameState state, CharacterSlot slot)
        {
            if (state == null)
            {
                return false;
            }

            CatalogItemDef[] items = ItemsForSlot(slot);
            bool spent = false;
            for (int i = 0; i < items.Length; i++)
            {
                if (!IsNew(state, items[i].id))
                {
                    continue;
                }

                Seen(state).Add(items[i].id);
                spent = true;
            }

            if (spent)
            {
                RaiseChanged();
            }

            return spent;
        }

        /// <summary>
        /// How many items still carry a badge. This is the number on the HUD button.
        ///
        /// It counts only what the backpack can actually put on screen, which is why
        /// <see cref="CharacterSlot.Base"/> is skipped rather than trusted to stay empty. The panel
        /// spends badges by sweeping the slots it draws, and this counts them; if the two ever walk
        /// different sets, an item in the gap is counted forever and never spendable, and a gold
        /// pill that cannot be cleared is precisely the kind of nagging this game does not do.
        ///
        /// Today no catalogue item is authored into the base slot, so the filter changes no number.
        /// It is here because <c>character_presets.json</c> already names <c>base_adar</c> and
        /// <c>base_neriah</c>, so the first person to author one would otherwise ship the stuck pill
        /// and have no obvious place to look for it.
        ///
        /// <b>Which is why this is no longer a sweep of its own.</b> The total is
        /// <see cref="NewCountForSlot"/> added up over <see cref="BadgedSlots"/> — the same array the
        /// panel draws a tab from — rather than a second walk of the catalogue that happens to agree
        /// with the per-slot one today. Two sweeps agree until someone edits one of them, and the
        /// failure that follows is silent: a pill that counts an item no tab can show, and no tab
        /// that can spend it. As a sum there is no arrangement of this file in which the HUD pill and
        /// the tab dots disagree. A slot leaves both numbers at once or enters both at once, and
        /// "skips <see cref="CharacterSlot.Base"/>" became a fact about the set instead of a
        /// <c>continue</c> inside a loop that a tidy-up could delete without noticing.
        /// </summary>
        public static int NewCount(GameState state)
        {
            int count = 0;
            for (int i = 0; i < BadgedSlotsInternal.Length; i++)
            {
                count += NewCountForSlot(state, BadgedSlotsInternal[i]);
            }

            return count;
        }

        /// <summary>
        /// How many items in one slot still carry a badge. This is the number behind a tab's dot.
        ///
        /// The same question <see cref="NewCount"/> asks, narrowed to one slot: unlocked, and never
        /// looked at. It is the half the total is built from, so the two cannot drift apart — the
        /// reasoning is written out in full over there and is the reason this method exists as the
        /// primitive rather than as a convenience beside a second sweep.
        ///
        /// Answers 0, without throwing, for a null run, an unloaded or empty catalogue, a slot that
        /// holds nothing, <see cref="CharacterSlot.Base"/>, and any value cast into
        /// <see cref="CharacterSlot"/> from outside its four names. Base is not a special case so
        /// much as an absence: it is not in <see cref="BadgedSlots"/>, the catalogue authors nothing
        /// into it, and <see cref="AppearanceState"/> has no base layer, so a badge there could never
        /// be looked at and therefore never spent. It answers 0 and always will.
        ///
        /// It hands back a count rather than a bool because the caller owns how it is said — but note
        /// what the sheet does with it, and keep doing that: a dot, never a numeral. Four numerals on
        /// four adjacent tabs read as a scoreboard to clear, which is the shape rule 10 keeps off the
        /// screen even when each number is individually harmless. The count is here to answer "is
        /// there anything in this tab I have not seen", and the panel spends it by looking.
        ///
        /// Allocates nothing. It walks the catalogue in place instead of through
        /// <see cref="ItemsForSlot"/>, because the HUD polls the total four times a second and three
        /// throwaway arrays per poll is a cost with no reader. The two still agree on which rows
        /// belong to a slot: both ask <c>BelongsToSlot</c>, which is the only place that test is
        /// written.
        /// </summary>
        public static int NewCountForSlot(GameState state, CharacterSlot slot)
        {
            if (state == null || !IsBadgedSlot(slot))
            {
                return 0;
            }

            CatalogItemDef[] all = CharacterCatalog.Items;
            if (all == null || all.Length == 0)
            {
                return 0;
            }

            // The seen list is read once and passed down: touching it per item would re-run its
            // repair sweep eighteen times for one number.
            List<string> seen = Seen(state);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                CatalogItemDef item = all[i];
                if (BelongsToSlot(item, slot) && CarriesBadge(state, seen, item))
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------ the look

        /// <summary>
        /// Writes the worn set into the six <see cref="AppearanceState"/> ints.
        ///
        /// The composition itself is <see cref="CharacterCatalog.Compose"/>, which already knows the
        /// draw order, that a hood hides the hair slot without unchoosing the hairstyle, and which
        /// <c>art_hooded</c> variant a covered piece should draw instead. Repeating any of that here
        /// would be a second copy of a rule that has to stay singular.
        ///
        /// The character id passed to it is deliberately null: see the note in this class's summary.
        /// The current look is the floor, so a layer no worn item claims keeps its value, and an
        /// empty or unresolvable worn set leaves the character exactly as they were rather than
        /// blanking them.
        ///
        /// Cheap on purpose — a dictionary lookup and a handful of int writes, no scene work and no
        /// reload — so a caller can repaint the world character straight out of
        /// <see cref="Changed"/>.
        /// </summary>
        public static void ApplyToAppearance(GameState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.appearance == null)
            {
                state.appearance = new AppearanceState();
            }

            List<string> equipped = Equipped(state);
            WarnAboutUnresolved(equipped);

            CharacterCatalog.Compose(state.appearance, null, equipped);
        }

        /// <summary>
        /// Which character this run is, inferred from the signature piece being worn — the coil of
        /// rope is Adar, the map tube is Neriah. Null when neither is on.
        ///
        /// Inferred rather than read because the save carries no character id: character creation
        /// writes the six appearance ints and nothing else. The one thing this answer is used for is
        /// protecting the silhouette anomaly, and a null answer degrades exactly the way
        /// <see cref="CharacterCatalog.CanEquip"/> already handles — no anchor is protected, which
        /// is the right reading of a character who is not wearing their signature piece to begin
        /// with.
        /// </summary>
        public static string CharacterId(GameState state)
        {
            if (state == null)
            {
                return null;
            }

            List<string> equipped = Equipped(state);
            if (equipped.Count == 0)
            {
                return null;
            }

            EnsurePresetsLoaded();

            PresetDef[] presets = CharacterPresets.All;
            for (int i = 0; presets != null && i < presets.Length; i++)
            {
                PresetDef preset = presets[i];
                if (preset == null || string.IsNullOrEmpty(preset.id) || string.IsNullOrEmpty(preset.signature_item))
                {
                    continue;
                }

                if (Contains(equipped, preset.signature_item))
                {
                    return preset.id;
                }
            }

            return null;
        }

        /// <summary>
        /// Drops every subscriber. Call on scene teardown, the way <see cref="InputLock.Clear"/> is
        /// called: a static event outlives the scene, and a subscriber from a scene that was rebuilt
        /// for a language switch would otherwise be woken up on every equip.
        /// </summary>
        public static void ClearSubscribers()
        {
            Changed = null;
        }

        // ------------------------------------------------------------------ internals

        // What counts as a row of a slot, asked in the same words by the list a tab draws
        // (ItemsForSlot) and by the number on that tab's dot (NewCountForSlot). Written twice, this
        // is exactly how a slot ends up with a badge for an item its own tab never shows — the
        // stranded badge NewCount's note is about, arriving through the other door.
        static bool BelongsToSlot(CatalogItemDef item, CharacterSlot slot)
        {
            return item != null && !string.IsNullOrEmpty(item.id) && item.Slot == slot;
        }

        // Membership of the rendered set. Reading it as a lookup rather than as an inequality is what
        // makes CharacterSlot.Base — and any int cast into the enum from outside its four names —
        // answer zero here, instead of being filtered somewhere later by a rule that has to be
        // remembered.
        static bool IsBadgedSlot(CharacterSlot slot)
        {
            for (int i = 0; i < BadgedSlotsInternal.Length; i++)
            {
                if (BadgedSlotsInternal[i] == slot)
                {
                    return true;
                }
            }

            return false;
        }

        // The badge test itself, in one place: unlocked, and not in the seen list. Everything that
        // asks about a badge — one item (IsNew), one slot (NewCountForSlot), the HUD total
        // (NewCount) — comes through here.
        //
        // The null guard is load-bearing and not defensive noise: UnlockEvaluator.IsUnlocked answers
        // true for a null item, so an unresolvable id would otherwise count as new forever and be
        // spendable by nothing, which is the stuck pill again.
        //
        // The seen list arrives as an argument rather than being read here, so a sweep pays its
        // repair pass once for the slot instead of once per item.
        static bool CarriesBadge(GameState state, List<string> seen, CatalogItemDef item)
        {
            if (item == null || string.IsNullOrEmpty(item.id))
            {
                return false;
            }

            return UnlockEvaluator.IsUnlocked(state, item) && !Contains(seen, item.id);
        }

        // The refusal reasons, one key each. SlotOccupied is deliberately absent: the catalogue's
        // own comment says a wardrobe must read it as "swap", and TryEquip has already done the swap
        // by the time it asks, so seeing one here means the swap missed a case and the log should
        // say so rather than the player being told no.
        static string RefusalKey(EquipConflict conflict)
        {
            if (conflict == null)
            {
                return KeyRefusalUnknownItem;
            }

            switch (conflict.reason)
            {
                case EquipConflictReason.Declared:
                    return KeyRefusalIncompatible;

                case EquipConflictReason.SameAnchor:
                    return KeyRefusalSameAnchor;

                case EquipConflictReason.ShoulderReplaced:
                    return KeyRefusalCloakShoulder;

                case EquipConflictReason.CoversSilhouette:
                    return KeyRefusalCoversSilhouette;

                case EquipConflictReason.SlotOccupied:
                    Debug.LogWarning("[Wardrobe] A one-item slot reported an occupant after the swap: " +
                                     conflict + ". Treating it as an incompatibility.");
                    return KeyRefusalIncompatible;
            }

            return KeyRefusalIncompatible;
        }

        // Both lists are public fields on a type that round-trips through Newtonsoft, so a save
        // written with an explicit null — or hand edited — can hand back null however carefully the
        // field initializers are written. SaveSystem.Repair does not know about these two lists, so
        // the repair happens here, on first touch, and includes the two shapes a hand edited save
        // can carry that no code path produces: an empty id and a duplicate.
        static List<string> Equipped(GameState state)
        {
            if (state.equippedItems == null)
            {
                state.equippedItems = new List<string>();
                return state.equippedItems;
            }

            Normalize(state.equippedItems);
            return state.equippedItems;
        }

        static List<string> Seen(GameState state)
        {
            if (state.seenItems == null)
            {
                state.seenItems = new List<string>();
                return state.seenItems;
            }

            Normalize(state.seenItems);
            return state.seenItems;
        }

        static void Normalize(List<string> ids)
        {
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                string id = ids[i];
                if (string.IsNullOrWhiteSpace(id))
                {
                    ids.RemoveAt(i);
                    continue;
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(ids[j], id, StringComparison.Ordinal))
                    {
                        ids.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        static bool Contains(List<string> ids, string id)
        {
            if (ids == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static void WarnAboutUnresolved(List<string> equipped)
        {
            for (int i = 0; i < equipped.Count; i++)
            {
                string id = equipped[i];
                if (CharacterCatalog.Item(id) != null || WarnedUnresolved.Contains(id))
                {
                    continue;
                }

                WarnedUnresolved.Add(id);
                Debug.LogWarning("[Wardrobe] Worn item '" + id + "' is not in character_catalog.json. " +
                                 "It draws nothing and is otherwise ignored; the rest of the look is unaffected.");
            }
        }

        // Nothing in the project loads the presets: BootSequence loads the catalogue and stops
        // there. They are read here for one field — signature_item — so the read is lazy, attempted
        // once, and loud on failure the same way every other content load in this project is.
        static void EnsurePresetsLoaded()
        {
            if (_presetsRequested || (CharacterPresets.All != null && CharacterPresets.All.Length > 0))
            {
                return;
            }

            _presetsRequested = true;
            CharacterPresets.LoadAll();
        }

        static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Wardrobe] A listener threw while repainting after a wardrobe change: " +
                               exception.Message);
            }
        }
    }
}
