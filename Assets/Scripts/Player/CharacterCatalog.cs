using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.Player
{
    /// <summary>Which of the four wardrobe slots an item belongs to.</summary>
    /// <remarks>
    /// The names come straight from the character direction document: Base + Hair + Outfit +
    /// Accessories. Only <see cref="Accessory"/> may hold more than one item at a time, and even
    /// then only when the pieces sit on different anchors.
    /// </remarks>
    public enum CharacterSlot
    {
        Base,
        Hair,
        Outfit,
        Accessory
    }

    /// <summary>
    /// Where on the body an item hangs. <see cref="None"/> is for items that are not pinned to a
    /// point at all — a tunic, a hairstyle, the base itself.
    ///
    /// Two items may never share an anchor, and no item may ever occupy the anchor a character's
    /// silhouette anomaly lives on: the coil of rope on Adar's right shoulder and the map tube
    /// across Neriah's back are how each of them is read at a glance, and a bag that covers one
    /// turns two distinct characters into the same dark blob.
    /// </summary>
    public enum CharacterAnchor
    {
        None,
        ShoulderR,
        ShoulderL,
        WaistFront,
        WaistSide,
        WristR,
        Neck,
        BackCenter
    }

    /// <summary>
    /// A colour channel an item exposes for recolouring. Hair and fabric are separate channels so
    /// six hair colours and four fabric colours cost no new art: the sprite is drawn once and the
    /// swatch multiplies it.
    /// </summary>
    public enum TintChannel
    {
        None,
        Hair,
        Fabric
    }

    /// <summary>Why two equipped items cannot be worn together.</summary>
    public enum EquipConflictReason
    {
        /// <summary>The item names the other one in its own <c>incompatible_with</c> list.</summary>
        Declared,

        /// <summary>Both pieces hang from the same anchor.</summary>
        SameAnchor,

        /// <summary>A cloak takes over both shoulders, so nothing else may sit on one.</summary>
        ShoulderReplaced,

        /// <summary>The item would cover this character's silhouette anomaly.</summary>
        CoversSilhouette,

        /// <summary>
        /// Two items in a slot that holds one. Reported for completeness; a wardrobe should treat
        /// this as "swap", never as "refused" — see <see cref="CharacterCatalog.SlotHoldsMany"/>.
        /// </summary>
        SlotOccupied
    }

    /// <summary>One reason one equipped item cannot sit beside another.</summary>
    public sealed class EquipConflict
    {
        public string itemId;

        /// <summary>The item it collides with. Null when the collision is with the character itself.</summary>
        public string otherItemId;

        public EquipConflictReason reason;

        /// <summary>The anchor the collision happened on, when the reason is anchor-shaped.</summary>
        public CharacterAnchor anchor;

        public override string ToString()
        {
            return "'" + (itemId ?? "?") + "' vs " +
                   (otherItemId == null ? "the character" : "'" + otherItemId + "'") +
                   " (" + reason + ", " + anchor + ")";
        }
    }

    /// <summary>
    /// Which art layer indices an item writes into <see cref="AppearanceState"/>.
    ///
    /// Every field is nullable and a null means "this item does not touch that layer". That is what
    /// lets an Outfit set <c>top</c> and <c>legs</c> at once while a hairstyle sets only <c>hair</c>,
    /// without an item ever having to restate the layers it does not care about.
    ///
    /// Nothing here is a sprite key. The catalogue stores plain ints, exactly the ints
    /// <see cref="AppearanceState"/> already stores, and <see cref="CharacterAppearance"/> builds
    /// the key at render time. That is deliberate: it means adopting the catalogue changes no saved
    /// field and needs no save migration.
    /// </summary>
    public sealed class ItemArtDef
    {
        [JsonProperty("body")] public int? body;
        [JsonProperty("skin")] public int? skin;
        [JsonProperty("hair")] public int? hair;
        [JsonProperty("top")] public int? top;
        [JsonProperty("legs")] public int? legs;
        [JsonProperty("accessory")] public int? accessory;

        /// <summary>True when the block names no layer at all, which is a content mistake.</summary>
        [JsonIgnore]
        public bool IsEmpty
        {
            get
            {
                return !body.HasValue && !skin.HasValue && !hair.HasValue
                       && !top.HasValue && !legs.HasValue && !accessory.HasValue;
            }
        }

        /// <summary>Writes the layers this block names onto a look, leaving the others alone.</summary>
        public void ApplyTo(AppearanceState state)
        {
            if (state == null)
            {
                return;
            }

            if (body.HasValue) state.body = Sanitize(body.Value);
            if (skin.HasValue) state.skin = Sanitize(skin.Value);
            if (hair.HasValue) state.hair = Sanitize(hair.Value);
            if (top.HasValue) state.top = Sanitize(top.Value);
            if (legs.HasValue) state.legs = Sanitize(legs.Value);
            if (accessory.HasValue) state.accessory = Sanitize(accessory.Value);
        }

        /// <summary>
        /// True when every layer this block names already matches the look.
        ///
        /// Note for whoever builds the wardrobe: an item that names a subset of another item's
        /// layers can match at the same time as that other item. Ask the slot which item is worn
        /// (one per slot, by id) rather than deriving the worn set from the layer indices alone.
        /// </summary>
        public bool Matches(AppearanceState state)
        {
            if (state == null)
            {
                return false;
            }

            if (body.HasValue && state.body != Sanitize(body.Value)) return false;
            if (skin.HasValue && state.skin != Sanitize(skin.Value)) return false;
            if (hair.HasValue && state.hair != Sanitize(hair.Value)) return false;
            if (top.HasValue && state.top != Sanitize(top.Value)) return false;
            if (legs.HasValue && state.legs != Sanitize(legs.Value)) return false;
            if (accessory.HasValue && state.accessory != Sanitize(accessory.Value)) return false;
            return true;
        }

        // A negative index would hide the layer silently rather than draw a wrong sprite, which is
        // the harder bug to see. The upper bound is left to CharacterAppearance, which owns the
        // per-layer variant counts and already clamps.
        static int Sanitize(int value)
        {
            return value < 0 ? 0 : value;
        }
    }

    /// <summary>
    /// One entry in the wardrobe, from <c>Resources/Data/character_catalog.json</c>.
    ///
    /// Field names mirror the JSON keys one to one, which is why they are snake_case: the file is
    /// hand edited outside Unity and has to stay readable. <c>display</c> and <c>description</c>
    /// are the only player-facing fields, and they are merged in from
    /// <c>Resources/Data/locales/&lt;locale&gt;/catalog.json</c> at load time.
    /// </summary>
    public sealed class CatalogItemDef
    {
        [JsonProperty("id")] public string id;

        /// <summary>Slot token: base, hair, outfit or accessory.</summary>
        [JsonProperty("slot")] public string slot;

        /// <summary>
        /// Anchor token: shoulder_r, shoulder_l, waist_front, waist_side, wrist_r, neck,
        /// back_center, or omitted for a piece that hangs from nothing in particular.
        /// </summary>
        [JsonProperty("anchor")] public string anchor;

        /// <summary>
        /// What the player has to have done for this item to be available. Null means available
        /// from the first minute.
        /// </summary>
        [JsonProperty("unlock_condition")] public UnlockCondition unlock_condition;

        /// <summary>Item ids this piece cannot be worn with, beyond what the anchor rules already say.</summary>
        [JsonProperty("incompatible_with")] public string[] incompatible_with;

        /// <summary>Recolourable channels: any of "hair" and "fabric". Empty means fixed colours.</summary>
        [JsonProperty("tint_channels")] public string[] tint_channels;

        /// <summary>Layer indices this item writes into the look.</summary>
        [JsonProperty("art")] public ItemArtDef art;

        /// <summary>
        /// Alternate layer indices used instead of <c>art</c> while the equipped set hides the hair
        /// slot. This is the <c>_hooded</c> variant from the character document: a hood does not
        /// delete the hairstyle from the wardrobe, it changes which sprite the covered pieces draw.
        /// </summary>
        [JsonProperty("art_hooded")] public ItemArtDef art_hooded;

        /// <summary>
        /// Slots this item hides while it is worn. A hood carries <c>["hair"]</c>. Hiding is not a
        /// conflict: the hairstyle stays chosen and comes back the moment the hood comes off.
        /// </summary>
        [JsonProperty("hides_slots")] public string[] hides_slots;

        /// <summary>
        /// True for a cloak. A cloak replaces the shoulder layer, so it occupies both shoulder
        /// anchors and nothing else may sit on either of them while it is worn.
        /// </summary>
        [JsonProperty("replaces_shoulder")] public bool replaces_shoulder;

        /// <summary>Player-facing name. Merged in from the locale file; never authored here.</summary>
        [JsonProperty("display")] public string display;

        /// <summary>Player-facing one-liner. Merged in from the locale file; never authored here.</summary>
        [JsonProperty("description")] public string description;

        // ---- resolved at load time from the tokens above, so nothing parses a string per frame.

        [JsonIgnore] public CharacterSlot Slot;
        [JsonIgnore] public CharacterAnchor Anchor;
        [JsonIgnore] public TintChannel[] TintChannels = Array.Empty<TintChannel>();
        [JsonIgnore] public CharacterSlot[] HiddenSlots = Array.Empty<CharacterSlot>();

        /// <summary>True when this item exposes that colour channel.</summary>
        public bool HasTintChannel(TintChannel channel)
        {
            if (channel == TintChannel.None || TintChannels == null)
            {
                return false;
            }

            for (int i = 0; i < TintChannels.Length; i++)
            {
                if (TintChannels[i] == channel) return true;
            }

            return false;
        }

        /// <summary>
        /// Every anchor this item takes up. Normally just its own; a cloak takes both shoulders.
        /// </summary>
        public void CollectAnchors(List<CharacterAnchor> into)
        {
            if (into == null)
            {
                return;
            }

            if (replaces_shoulder)
            {
                if (!into.Contains(CharacterAnchor.ShoulderR)) into.Add(CharacterAnchor.ShoulderR);
                if (!into.Contains(CharacterAnchor.ShoulderL)) into.Add(CharacterAnchor.ShoulderL);
            }

            if (Anchor != CharacterAnchor.None && !into.Contains(Anchor))
            {
                into.Add(Anchor);
            }
        }
    }

    /// <summary>
    /// One playable character, from the <c>characters</c> section of character_catalog.json.
    ///
    /// A character is a starting look and a silhouette, not a class: nothing here changes with
    /// progress, and there are no tiers. The only thing that ever changes a character's look is
    /// the player choosing something in the wardrobe.
    /// </summary>
    public sealed class CharacterPresetDef
    {
        [JsonProperty("id")] public string id;

        /// <summary>Accent colour as #RRGGBB. Adar is sky, Neriah is growth.</summary>
        [JsonProperty("accent")] public string accent;

        /// <summary>
        /// The anchor the character's silhouette anomaly occupies. Nothing may be equipped there.
        /// Adar: shoulder_r, the coil of rope. Neriah: back_center, the map tube.
        /// </summary>
        [JsonProperty("silhouette_anchor")] public string silhouette_anchor;

        /// <summary>The look a run starts with before the player touches anything.</summary>
        [JsonProperty("art")] public ItemArtDef art;

        /// <summary>Default swatch index on the hair channel.</summary>
        [JsonProperty("hair_tint")] public int hair_tint;

        /// <summary>Default swatch index on the fabric channel.</summary>
        [JsonProperty("fabric_tint")] public int fabric_tint;

        /// <summary>
        /// The name offered in the field, from the locale file. It is a suggestion: the player can
        /// type over it, and the game uses whatever they typed.
        /// </summary>
        [JsonProperty("suggested_name")] public string suggested_name;

        /// <summary>Player-facing one-liner, from the locale file.</summary>
        [JsonProperty("description")] public string description;

        [JsonIgnore] public CharacterAnchor SilhouetteAnchor;
        [JsonIgnore] public Color Accent = Color.white;
    }

    /// <summary>The recolouring swatches, shared by every language.</summary>
    public sealed class CatalogPaletteDef
    {
        /// <summary>Six hair colours as #RRGGBB.</summary>
        [JsonProperty("hair")] public string[] hair;

        /// <summary>Four fabric colours as #RRGGBB.</summary>
        [JsonProperty("fabric")] public string[] fabric;
    }

    /// <summary>Root of Resources/Data/character_catalog.json.</summary>
    public sealed class CharacterCatalogFile
    {
        [JsonProperty("characters")] public CharacterPresetDef[] characters;
        [JsonProperty("items")] public CatalogItemDef[] items;
        [JsonProperty("palette")] public CatalogPaletteDef palette;
    }

    /// <summary>Player-facing half of a catalogue item, from locales/&lt;locale&gt;/catalog.json.</summary>
    public sealed class CatalogItemStrings
    {
        [JsonProperty("display")] public string display;
        [JsonProperty("description")] public string description;
    }

    /// <summary>Player-facing half of a character, from locales/&lt;locale&gt;/catalog.json.</summary>
    public sealed class CatalogCharacterStrings
    {
        [JsonProperty("suggested_name")] public string suggested_name;
        [JsonProperty("description")] public string description;
    }

    /// <summary>Root of Resources/Data/locales/&lt;locale&gt;/catalog.json.</summary>
    public sealed class CatalogStringsFile
    {
        [JsonProperty("items")] public Dictionary<string, CatalogItemStrings> items;
        [JsonProperty("characters")] public Dictionary<string, CatalogCharacterStrings> characters;

        /// <summary>
        /// The sentence printed under a locked item, one entry per key
        /// <see cref="UnlockEvaluator.LocaleKey"/> can return.
        /// </summary>
        [JsonProperty("unlock")] public Dictionary<string, string> unlock;

        /// <summary>What the game calls the player before they have typed a name.</summary>
        [JsonProperty("player_name_fallback")] public string player_name_fallback;
    }

    /// <summary>
    /// The wardrobe catalogue: who the playable characters are, what can be worn, what unlocks it,
    /// and what may not be worn together.
    ///
    /// The split is the one the rest of the project already uses. Structure and numbers live in
    /// <c>Resources/Data/character_catalog.json</c> and there is exactly one copy, so a slot, an
    /// anchor or an unlock threshold cannot drift between languages. Every string a player reads
    /// lives in <c>Resources/Data/locales/&lt;locale&gt;/catalog.json</c> and is merged onto the
    /// same objects once loaded, so consumers see one object with every field filled.
    ///
    /// Three product rules shaped this type and are worth stating where they can be read:
    ///
    /// * Appearance is fixed, not progressive. There are no tiers and no automatic asset swaps
    ///   with level. Nothing in this file reads a score, a level or a currency, because the game
    ///   has none of the three.
    /// * A locked item is an invitation, never a denial (rule 7). The catalogue therefore hands
    ///   back locked items too, silhouette and all; hiding them would make the wardrobe look
    ///   smaller than it is, and nothing here can ever take an unlocked item back —
    ///   <see cref="UnlockEvaluator"/> only reads quantities that never decrease.
    /// * No religious iconography, ever (rule 13). That is a content rule this loader cannot
    ///   enforce; it belongs to whoever authors the JSON and to the content review.
    ///
    /// Every property is non-null before and after <see cref="LoadAll()"/>. A missing or malformed
    /// file logs an error and leaves the empty default in place, so a content mistake degrades the
    /// wardrobe instead of stopping the build.
    /// </summary>
    public static class CharacterCatalog
    {
        const string ResourceFolder = "Data/";
        const string CatalogFileName = "character_catalog";
        const string LocaleFileName = "/catalog";

        /// <summary>
        /// The token dialogue uses where the player's own name belongs. Used sparingly and only at
        /// moments of recognition — a name in every line stops being recognition and starts being
        /// mail merge.
        /// </summary>
        public const string PlayerNameToken = "{player_name}";

        public static CharacterPresetDef[] Characters { get; private set; } = Array.Empty<CharacterPresetDef>();

        public static CatalogItemDef[] Items { get; private set; } = Array.Empty<CatalogItemDef>();

        public static CatalogPaletteDef Palette { get; private set; } = EmptyPalette();

        /// <summary>The locale whose strings are merged into the catalogue in memory.</summary>
        public static string LoadedLocale { get; private set; } = string.Empty;

        static readonly Dictionary<string, CatalogItemDef> ItemsById =
            new Dictionary<string, CatalogItemDef>(StringComparer.Ordinal);

        static readonly Dictionary<string, CharacterPresetDef> CharactersById =
            new Dictionary<string, CharacterPresetDef>(StringComparer.Ordinal);

        static readonly Dictionary<string, string> UnlockStrings =
            new Dictionary<string, string>(StringComparer.Ordinal);

        static string _playerNameFallback = string.Empty;

        /// <summary>Reads the catalogue for the active locale.</summary>
        public static void LoadAll()
        {
            LoadAll(Locales.Active);
        }

        /// <summary>
        /// Reads the catalogue, taking player-facing strings from this locale. Safe to call more
        /// than once; the last read wins, which is what a locale switch relies on.
        /// </summary>
        public static void LoadAll(string locale)
        {
            string canonical = Locales.Canonical(locale) ?? Locales.Source;
            LoadedLocale = canonical;

            // ---- structure and numbers: one copy, shared by every language
            CharacterCatalogFile file = LoadObject<CharacterCatalogFile>(ResourceFolder + CatalogFileName);
            Characters = file != null && file.characters != null ? file.characters : Array.Empty<CharacterPresetDef>();
            Items = file != null && file.items != null ? file.items : Array.Empty<CatalogItemDef>();
            Palette = file != null && file.palette != null ? file.palette : EmptyPalette();

            ResolveTokens();
            Reindex();

            // ---- strings: one file per language, merged onto the objects above
            MergeStrings(Locales.ResourceFolder(canonical) + LocaleFileName);
        }

        /// <summary>The item with this id, or null.</summary>
        public static CatalogItemDef Item(string id)
        {
            CatalogItemDef item;
            return !string.IsNullOrEmpty(id) && ItemsById.TryGetValue(id, out item) ? item : null;
        }

        /// <summary>The character with this id, or null.</summary>
        public static CharacterPresetDef Character(string id)
        {
            CharacterPresetDef preset;
            return !string.IsNullOrEmpty(id) && CharactersById.TryGetValue(id, out preset) ? preset : null;
        }

        /// <summary>
        /// True for the slot that may hold several pieces at once. Only accessories may, and even
        /// then only on distinct anchors. Everything else is one item at a time, and a wardrobe
        /// should treat picking a second one as a swap rather than as a refusal.
        /// </summary>
        public static bool SlotHoldsMany(CharacterSlot slot)
        {
            return slot == CharacterSlot.Accessory;
        }

        // ------------------------------------------------------------------ strings
        // These do not go through Loc: Loc reads ui.json and only ui.json. The catalogue keeps its
        // own table for the same reason npcs and vocations keep theirs — the strings belong to the
        // content file they describe. The miss behaviour is copied from Loc on purpose: a visible
        // marker on screen, an error in the log, and never a fallback to another language, because
        // a silent fallback that looks like real content hides a missing translation until a player
        // finds it.

        /// <summary>True when this unlock key exists in the loaded table.</summary>
        public static bool HasText(string key)
        {
            return !string.IsNullOrEmpty(key) && UnlockStrings.ContainsKey(key);
        }

        /// <summary>The unlock sentence for this key, or a visible marker on a miss.</summary>
        public static string Text(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return Missing(string.Empty);
            }

            string value;
            if (UnlockStrings.TryGetValue(key, out value) && value != null)
            {
                return value;
            }

            Debug.LogError("[Catalog] Missing unlock key '" + key + "' in locale " + LoadedLocale +
                           ". Add it to Resources/Data/locales/" + LoadedLocale + "/catalog.json under \"unlock\".");
            return Missing(key);
        }

        /// <summary>
        /// The unlock sentence with positional arguments substituted, as in "Etapa {0} da muralha".
        /// A malformed format string degrades to the raw template rather than throwing mid-frame.
        /// </summary>
        public static string Text(string key, params object[] args)
        {
            string template = Text(key);
            if (args == null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(template, args);
            }
            catch (FormatException exception)
            {
                Debug.LogError("[Catalog] Unlock key '" + key + "' has a bad format string in locale " +
                               LoadedLocale + ": " + exception.Message);
                return template;
            }
        }

        /// <summary>
        /// What to call the player. Their own name when they typed one, otherwise the authored
        /// fallback for this locale — "viajante" in pt-BR, "traveller" in en.
        /// </summary>
        public static string PlayerName(GameState state)
        {
            if (state != null && !string.IsNullOrWhiteSpace(state.playerName))
            {
                return state.playerName.Trim();
            }

            if (!string.IsNullOrEmpty(_playerNameFallback))
            {
                return _playerNameFallback;
            }

            Debug.LogError("[Catalog] Locale " + LoadedLocale + " has no \"player_name_fallback\" in catalog.json.");
            return Missing("player_name_fallback");
        }

        /// <summary>Replaces every <see cref="PlayerNameToken"/> in an authored line.</summary>
        public static string ApplyPlayerName(string text, GameState state)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(PlayerNameToken, StringComparison.Ordinal) < 0)
            {
                return text;
            }

            return text.Replace(PlayerNameToken, PlayerName(state));
        }

        // ------------------------------------------------------------------ colour

        /// <summary>
        /// The swatch on a channel, as a colour. Out of range wraps rather than clamping, so a
        /// wardrobe that cycles through swatches never gets stuck on the last one.
        /// </summary>
        public static Color Swatch(TintChannel channel, int index)
        {
            string[] swatches = SwatchesOf(channel);
            if (swatches == null || swatches.Length == 0)
            {
                Debug.LogError("[Catalog] No swatches for the " + channel + " channel in character_catalog.json.");
                return Color.white;
            }

            int wrapped = index % swatches.Length;
            if (wrapped < 0) wrapped += swatches.Length;

            Color parsed;
            if (ParseColor(swatches[wrapped], out parsed))
            {
                return parsed;
            }

            Debug.LogError("[Catalog] Swatch " + wrapped + " on the " + channel + " channel is not a #RRGGBB colour: '" +
                           swatches[wrapped] + "'.");
            return Color.white;
        }

        /// <summary>How many swatches a channel offers. Six for hair, four for fabric.</summary>
        public static int SwatchCount(TintChannel channel)
        {
            string[] swatches = SwatchesOf(channel);
            return swatches == null ? 0 : swatches.Length;
        }

        static string[] SwatchesOf(TintChannel channel)
        {
            if (Palette == null) return null;
            if (channel == TintChannel.Hair) return Palette.hair;
            if (channel == TintChannel.Fabric) return Palette.fabric;
            return null;
        }

        static bool ParseColor(string hex, out Color color)
        {
            color = Color.white;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out color);
        }

        // ------------------------------------------------------------------ compatibility

        /// <summary>
        /// Which slots are not drawn while this set is worn. A hood puts the hair slot in here; the
        /// hairstyle itself stays chosen, and comes back when the hood comes off.
        /// </summary>
        public static HashSet<CharacterSlot> HiddenSlots(IEnumerable<string> equippedIds)
        {
            var hidden = new HashSet<CharacterSlot>();
            if (equippedIds == null)
            {
                return hidden;
            }

            foreach (string id in equippedIds)
            {
                CatalogItemDef item = Item(id);
                if (item == null || item.HiddenSlots == null)
                {
                    continue;
                }

                for (int i = 0; i < item.HiddenSlots.Length; i++)
                {
                    hidden.Add(item.HiddenSlots[i]);
                }
            }

            return hidden;
        }

        /// <summary>
        /// Which art block an item draws with, given whether a hood is up. Falls back to the plain
        /// block when the item has no hooded variant, which is the common case.
        /// </summary>
        public static ItemArtDef ArtFor(CatalogItemDef item, bool hoodUp)
        {
            if (item == null)
            {
                return null;
            }

            if (hoodUp && item.art_hooded != null && !item.art_hooded.IsEmpty)
            {
                return item.art_hooded;
            }

            return item.art;
        }

        /// <summary>
        /// Composes a look: the character's starting art, then each equipped item in slot order,
        /// swapping to the <c>_hooded</c> variant wherever the set hides the hair slot.
        ///
        /// Writes into the <see cref="AppearanceState"/> the save already carries — plain ints per
        /// layer, no new fields, no migration.
        /// </summary>
        public static void Compose(AppearanceState into, string characterId, IEnumerable<string> equippedIds)
        {
            if (into == null)
            {
                return;
            }

            CharacterPresetDef preset = Character(characterId);
            if (preset != null && preset.art != null)
            {
                preset.art.ApplyTo(into);
            }

            if (equippedIds == null)
            {
                return;
            }

            HashSet<CharacterSlot> hidden = HiddenSlots(equippedIds);
            bool hoodUp = hidden.Contains(CharacterSlot.Hair);

            // Slot order matches the draw order the art layers already use, so an Outfit cannot
            // land after the Accessory that is meant to sit over it.
            ApplySlot(into, equippedIds, CharacterSlot.Base, hidden, hoodUp);
            ApplySlot(into, equippedIds, CharacterSlot.Outfit, hidden, hoodUp);
            ApplySlot(into, equippedIds, CharacterSlot.Hair, hidden, hoodUp);
            ApplySlot(into, equippedIds, CharacterSlot.Accessory, hidden, hoodUp);
        }

        static void ApplySlot(AppearanceState into, IEnumerable<string> equippedIds, CharacterSlot slot,
            HashSet<CharacterSlot> hidden, bool hoodUp)
        {
            if (hidden.Contains(slot))
            {
                return;
            }

            foreach (string id in equippedIds)
            {
                CatalogItemDef item = Item(id);
                if (item == null || item.Slot != slot)
                {
                    continue;
                }

                ItemArtDef art = ArtFor(item, hoodUp);
                if (art != null)
                {
                    art.ApplyTo(into);
                }
            }
        }

        /// <summary>
        /// Every reason the pieces in this set cannot be worn together, plus every piece that would
        /// cover the character's silhouette anomaly. An empty list means the set is wearable.
        ///
        /// Conflicts are reported symmetrically once, first item first, so a wardrobe can show the
        /// pair without deciding which of the two is at fault.
        /// </summary>
        public static List<EquipConflict> FindConflicts(IEnumerable<string> equippedIds, string characterId)
        {
            var conflicts = new List<EquipConflict>();
            if (equippedIds == null)
            {
                return conflicts;
            }

            var items = new List<CatalogItemDef>();
            foreach (string id in equippedIds)
            {
                CatalogItemDef item = Item(id);
                if (item == null)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        Debug.LogWarning("[Catalog] Equipped item '" + id + "' is not in character_catalog.json.");
                    }

                    continue;
                }

                if (!items.Contains(item))
                {
                    items.Add(item);
                }
            }

            CharacterAnchor protectedAnchor = Character(characterId) != null
                ? Character(characterId).SilhouetteAnchor
                : CharacterAnchor.None;

            for (int i = 0; i < items.Count; i++)
            {
                CatalogItemDef item = items[i];

                // The silhouette anomaly is a collision with the character, not with another item,
                // so it is checked once per item rather than in the pairwise pass. Adar plus any
                // cloak lands here without a special case: a cloak takes both shoulders, and one of
                // Adar's shoulders is where his coil of rope lives.
                if (protectedAnchor != CharacterAnchor.None)
                {
                    var covered = new List<CharacterAnchor>(2);
                    item.CollectAnchors(covered);
                    if (covered.Contains(protectedAnchor))
                    {
                        conflicts.Add(new EquipConflict
                        {
                            itemId = item.id,
                            otherItemId = null,
                            reason = EquipConflictReason.CoversSilhouette,
                            anchor = protectedAnchor
                        });
                    }
                }

                for (int j = i + 1; j < items.Count; j++)
                {
                    EquipConflict conflict = PairConflict(item, items[j]);
                    if (conflict != null)
                    {
                        conflicts.Add(conflict);
                    }
                }
            }

            return conflicts;
        }

        /// <summary>
        /// Whether one more item can join a set. The candidate is tested against the set and
        /// against the character; <paramref name="conflict"/> carries the first reason it cannot.
        /// </summary>
        public static bool CanEquip(string candidateId, IEnumerable<string> equippedIds, string characterId,
            out EquipConflict conflict)
        {
            conflict = null;

            CatalogItemDef candidate = Item(candidateId);
            if (candidate == null)
            {
                return false;
            }

            CharacterPresetDef preset = Character(characterId);
            CharacterAnchor protectedAnchor = preset != null ? preset.SilhouetteAnchor : CharacterAnchor.None;

            if (protectedAnchor != CharacterAnchor.None)
            {
                var covered = new List<CharacterAnchor>(2);
                candidate.CollectAnchors(covered);
                if (covered.Contains(protectedAnchor))
                {
                    conflict = new EquipConflict
                    {
                        itemId = candidate.id,
                        otherItemId = null,
                        reason = EquipConflictReason.CoversSilhouette,
                        anchor = protectedAnchor
                    };
                    return false;
                }
            }

            if (equippedIds == null)
            {
                return true;
            }

            foreach (string id in equippedIds)
            {
                CatalogItemDef worn = Item(id);
                if (worn == null || worn == candidate)
                {
                    continue;
                }

                EquipConflict pair = PairConflict(candidate, worn);
                if (pair != null)
                {
                    conflict = pair;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The one place the document's compatibility rules are spelled out. In order:
        /// a declared incompatibility; two pieces in a slot that holds one; a shared anchor,
        /// including the pair of shoulders a cloak takes over.
        ///
        /// A hood hiding the hair slot is deliberately NOT a conflict — see
        /// <see cref="HiddenSlots"/>. Nothing the player chose is ever thrown away.
        /// </summary>
        static EquipConflict PairConflict(CatalogItemDef a, CatalogItemDef b)
        {
            if (a == null || b == null || a == b)
            {
                return null;
            }

            if (Declares(a, b.id) || Declares(b, a.id))
            {
                return new EquipConflict
                {
                    itemId = a.id,
                    otherItemId = b.id,
                    reason = EquipConflictReason.Declared,
                    anchor = CharacterAnchor.None
                };
            }

            if (a.Slot == b.Slot && !SlotHoldsMany(a.Slot))
            {
                return new EquipConflict
                {
                    itemId = a.id,
                    otherItemId = b.id,
                    reason = EquipConflictReason.SlotOccupied,
                    anchor = CharacterAnchor.None
                };
            }

            var anchorsA = new List<CharacterAnchor>(2);
            var anchorsB = new List<CharacterAnchor>(2);
            a.CollectAnchors(anchorsA);
            b.CollectAnchors(anchorsB);

            for (int i = 0; i < anchorsA.Count; i++)
            {
                if (!anchorsB.Contains(anchorsA[i]))
                {
                    continue;
                }

                bool cloakInvolved = a.replaces_shoulder || b.replaces_shoulder;
                return new EquipConflict
                {
                    itemId = a.id,
                    otherItemId = b.id,
                    reason = cloakInvolved ? EquipConflictReason.ShoulderReplaced : EquipConflictReason.SameAnchor,
                    anchor = anchorsA[i]
                };
            }

            return null;
        }

        static bool Declares(CatalogItemDef item, string otherId)
        {
            if (item == null || item.incompatible_with == null || string.IsNullOrEmpty(otherId))
            {
                return false;
            }

            for (int i = 0; i < item.incompatible_with.Length; i++)
            {
                if (string.Equals(item.incompatible_with[i], otherId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ token parsing
        // The JSON carries snake_case tokens and Newtonsoft matches enum member names, not tokens,
        // so every enum is parsed by hand here. An unrecognised token is loud and then harmless:
        // the field falls back to its neutral value rather than throwing during boot.

        public static bool TryParseSlot(string token, out CharacterSlot slot)
        {
            slot = CharacterSlot.Accessory;
            if (string.IsNullOrEmpty(token)) return false;

            switch (token.Trim().ToLowerInvariant())
            {
                case "base": slot = CharacterSlot.Base; return true;
                case "hair": slot = CharacterSlot.Hair; return true;
                case "outfit": slot = CharacterSlot.Outfit; return true;
                case "accessory": slot = CharacterSlot.Accessory; return true;
                default: return false;
            }
        }

        public static bool TryParseAnchor(string token, out CharacterAnchor anchor)
        {
            anchor = CharacterAnchor.None;
            if (string.IsNullOrEmpty(token)) return false;

            switch (token.Trim().ToLowerInvariant())
            {
                case "": return false;
                case "none": return true;
                case "shoulder_r": anchor = CharacterAnchor.ShoulderR; return true;
                case "shoulder_l": anchor = CharacterAnchor.ShoulderL; return true;
                case "waist_front": anchor = CharacterAnchor.WaistFront; return true;
                case "waist_side": anchor = CharacterAnchor.WaistSide; return true;
                case "wrist_r": anchor = CharacterAnchor.WristR; return true;
                case "neck": anchor = CharacterAnchor.Neck; return true;
                case "back_center": anchor = CharacterAnchor.BackCenter; return true;
                default: return false;
            }
        }

        public static bool TryParseTintChannel(string token, out TintChannel channel)
        {
            channel = TintChannel.None;
            if (string.IsNullOrEmpty(token)) return false;

            switch (token.Trim().ToLowerInvariant())
            {
                case "hair": channel = TintChannel.Hair; return true;
                case "fabric": channel = TintChannel.Fabric; return true;
                default: return false;
            }
        }

        static void ResolveTokens()
        {
            for (int i = 0; i < Items.Length; i++)
            {
                CatalogItemDef item = Items[i];
                if (item == null)
                {
                    continue;
                }

                CharacterSlot slot;
                if (TryParseSlot(item.slot, out slot))
                {
                    item.Slot = slot;
                }
                else
                {
                    item.Slot = CharacterSlot.Accessory;
                    Debug.LogError("[Catalog] Item '" + item.id + "' has an unknown slot '" + item.slot +
                                   "'. Expected base, hair, outfit or accessory.");
                }

                CharacterAnchor anchor;
                if (string.IsNullOrEmpty(item.anchor))
                {
                    item.Anchor = CharacterAnchor.None;
                }
                else if (TryParseAnchor(item.anchor, out anchor))
                {
                    item.Anchor = anchor;
                }
                else
                {
                    item.Anchor = CharacterAnchor.None;
                    Debug.LogError("[Catalog] Item '" + item.id + "' has an unknown anchor '" + item.anchor + "'.");
                }

                item.TintChannels = ParseChannels(item.id, item.tint_channels);
                item.HiddenSlots = ParseSlots(item.id, item.hides_slots);

                if (item.art == null || item.art.IsEmpty)
                {
                    Debug.LogError("[Catalog] Item '" + item.id + "' has no \"art\" block, so it would change nothing when worn.");
                }
            }

            for (int i = 0; i < Characters.Length; i++)
            {
                CharacterPresetDef preset = Characters[i];
                if (preset == null)
                {
                    continue;
                }

                CharacterAnchor anchor;
                if (TryParseAnchor(preset.silhouette_anchor, out anchor))
                {
                    preset.SilhouetteAnchor = anchor;
                }
                else
                {
                    preset.SilhouetteAnchor = CharacterAnchor.None;
                    Debug.LogError("[Catalog] Character '" + preset.id + "' has an unknown silhouette_anchor '" +
                                   preset.silhouette_anchor + "'; nothing will be protected from being covered.");
                }

                Color accent;
                if (ParseColor(preset.accent, out accent))
                {
                    preset.Accent = accent;
                }
                else
                {
                    preset.Accent = Color.white;
                    Debug.LogError("[Catalog] Character '" + preset.id + "' has an accent that is not #RRGGBB: '" +
                                   preset.accent + "'.");
                }
            }
        }

        static TintChannel[] ParseChannels(string itemId, string[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
            {
                return Array.Empty<TintChannel>();
            }

            var resolved = new List<TintChannel>(tokens.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                TintChannel channel;
                if (TryParseTintChannel(tokens[i], out channel))
                {
                    if (!resolved.Contains(channel)) resolved.Add(channel);
                }
                else
                {
                    Debug.LogError("[Catalog] Item '" + itemId + "' names an unknown tint channel '" + tokens[i] +
                                   "'. Expected hair or fabric.");
                }
            }

            return resolved.ToArray();
        }

        static CharacterSlot[] ParseSlots(string itemId, string[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
            {
                return Array.Empty<CharacterSlot>();
            }

            var resolved = new List<CharacterSlot>(tokens.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                CharacterSlot slot;
                if (TryParseSlot(tokens[i], out slot))
                {
                    if (!resolved.Contains(slot)) resolved.Add(slot);
                }
                else
                {
                    Debug.LogError("[Catalog] Item '" + itemId + "' hides an unknown slot '" + tokens[i] + "'.");
                }
            }

            return resolved.ToArray();
        }

        // ------------------------------------------------------------------ merging and indexing

        static void Reindex()
        {
            ItemsById.Clear();
            CharactersById.Clear();

            for (int i = 0; i < Items.Length; i++)
            {
                CatalogItemDef item = Items[i];
                if (item == null || string.IsNullOrEmpty(item.id))
                {
                    Debug.LogError("[Catalog] Skipping an item with no id in character_catalog.json.");
                    continue;
                }

                if (ItemsById.ContainsKey(item.id))
                {
                    Debug.LogError("[Catalog] Duplicate item id '" + item.id + "' in character_catalog.json; the first one wins.");
                    continue;
                }

                ItemsById[item.id] = item;
            }

            for (int i = 0; i < Characters.Length; i++)
            {
                CharacterPresetDef preset = Characters[i];
                if (preset == null || string.IsNullOrEmpty(preset.id))
                {
                    Debug.LogError("[Catalog] Skipping a character with no id in character_catalog.json.");
                    continue;
                }

                if (CharactersById.ContainsKey(preset.id))
                {
                    Debug.LogError("[Catalog] Duplicate character id '" + preset.id + "'; the first one wins.");
                    continue;
                }

                CharactersById[preset.id] = preset;
            }
        }

        // A merge never invents a string. When the locale file has no entry for an id the field is
        // left null and the consumer's own fallback makes the gap visible, exactly as GameData does.
        static void MergeStrings(string resourcePath)
        {
            UnlockStrings.Clear();
            _playerNameFallback = string.Empty;

            CatalogStringsFile strings = LoadObject<CatalogStringsFile>(resourcePath);
            if (strings == null)
            {
                return;
            }

            if (strings.unlock != null)
            {
                foreach (KeyValuePair<string, string> pair in strings.unlock)
                {
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                    {
                        UnlockStrings[pair.Key] = pair.Value;
                    }
                }
            }

            _playerNameFallback = strings.player_name_fallback ?? string.Empty;

            for (int i = 0; i < Items.Length; i++)
            {
                CatalogItemDef item = Items[i];
                if (item == null || string.IsNullOrEmpty(item.id))
                {
                    continue;
                }

                CatalogItemStrings entry = null;
                if (strings.items != null)
                {
                    strings.items.TryGetValue(item.id, out entry);
                }

                if (entry != null)
                {
                    item.display = entry.display;
                    item.description = entry.description;
                }
                else
                {
                    LogMissingString("items", item.id);
                }
            }

            for (int i = 0; i < Characters.Length; i++)
            {
                CharacterPresetDef preset = Characters[i];
                if (preset == null || string.IsNullOrEmpty(preset.id))
                {
                    continue;
                }

                CatalogCharacterStrings entry = null;
                if (strings.characters != null)
                {
                    strings.characters.TryGetValue(preset.id, out entry);
                }

                if (entry != null)
                {
                    preset.suggested_name = entry.suggested_name;
                    preset.description = entry.description;
                }
                else
                {
                    LogMissingString("characters", preset.id);
                }
            }
        }

        static void LogMissingString(string section, string key)
        {
            Debug.LogError("[Catalog] Locale " + LoadedLocale + " has no \"" + section + "\" entry '" + key +
                           "' in catalog.json.");
        }

        // ------------------------------------------------------------------ reading
        // Same shape as GameData.LoadObject: a missing or malformed file is an error in the log and
        // an empty default in memory, never an exception during boot.

        static T LoadObject<T>(string resourcePath) where T : class
        {
            string json = ReadText(resourcePath);
            if (json == null)
            {
                return null;
            }

            try
            {
                T parsed = JsonConvert.DeserializeObject<T>(json);
                if (parsed == null)
                {
                    LogParseFailure(resourcePath, "the file did not contain an object");
                }

                return parsed;
            }
            catch (Exception exception)
            {
                LogParseFailure(resourcePath, exception.Message);
                return null;
            }
        }

        static string ReadText(string resourcePath)
        {
            TextAsset asset;
            try
            {
                asset = Resources.Load<TextAsset>(resourcePath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Catalog] Could not load Resources/" + resourcePath + ".json: " + exception.Message);
                return null;
            }

            if (asset == null)
            {
                Debug.LogError("[Catalog] Missing content file Resources/" + resourcePath + ".json; the wardrobe will be empty.");
                return null;
            }

            string text = asset.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogError("[Catalog] Content file Resources/" + resourcePath + ".json is empty.");
                return null;
            }

            return text;
        }

        static void LogParseFailure(string resourcePath, string reason)
        {
            Debug.LogError("[Catalog] Could not parse Resources/" + resourcePath + ".json: " + reason +
                           ". Using an empty default.");
        }

        static CatalogPaletteDef EmptyPalette()
        {
            return new CatalogPaletteDef
            {
                hair = Array.Empty<string>(),
                fabric = Array.Empty<string>()
            };
        }

        // Square brackets, not the angle quotes this used to use. The marker has to be VISIBLE to
        // do its job, and U+27E8/U+27E9 are in none of the three bundled families, so the fallback
        // rendered as two empty boxes around the key — a missing-glyph bug wrapped around a
        // missing-key bug. Brackets are ASCII, every family has them, and the string still cannot
        // be mistaken for authored punctuation and still greps back to the key that produced it.
        static string Missing(string key)
        {
            return "[" + key + "]";
        }
    }
}
