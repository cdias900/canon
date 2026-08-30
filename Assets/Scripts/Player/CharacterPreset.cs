using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.Player
{
    /// <summary>
    /// How acceptable a typed name is. Two of the four values are acceptable, which is the point:
    /// a player who clears the field has chosen the fallback, not made a mistake.
    /// </summary>
    public enum NameValidity
    {
        /// <summary>Within the length bounds. The game will use it verbatim.</summary>
        Ok,

        /// <summary>The field is empty. Acceptable — the game calls the player by the fallback word.</summary>
        Unset,

        /// <summary>One character. Short enough that it reads as a slip rather than as a choice.</summary>
        TooShort,

        /// <summary>Longer than the field holds.</summary>
        TooLong
    }

    /// <summary>
    /// The four colours a character's key art is built from. Roles, not garments: Adar's
    /// <see cref="PaletteRole.Outer"/> is a cloak and Neriah's is an overpiece, and naming the role
    /// after either one would make the other entry read as a mistake.
    /// </summary>
    public enum PaletteRole
    {
        Skin,
        Tunic,
        Outer,
        Linen
    }

    /// <summary>
    /// The four colours of one character, as #RRGGBB. Straight from the character direction
    /// document, one copy, shared by every language.
    /// </summary>
    public sealed class PresetPaletteDef
    {
        [JsonProperty("skin")] public string skin;
        [JsonProperty("tunic")] public string tunic;

        /// <summary>The layer over the tunic: a cloak on Adar, an overpiece on Neriah.</summary>
        [JsonProperty("outer")] public string outer;

        [JsonProperty("linen")] public string linen;

        [JsonIgnore] public Color Skin = Color.white;
        [JsonIgnore] public Color Tunic = Color.white;
        [JsonIgnore] public Color Outer = Color.white;
        [JsonIgnore] public Color Linen = Color.white;

        /// <summary>The colour for one role, resolved at load time.</summary>
        public Color Role(PaletteRole role)
        {
            switch (role)
            {
                case PaletteRole.Skin: return Skin;
                case PaletteRole.Tunic: return Tunic;
                case PaletteRole.Outer: return Outer;
                case PaletteRole.Linen: return Linen;
            }

            return Color.white;
        }

        /// <summary>The authored hex for one role, for a verification message that has to quote it.</summary>
        public string Hex(PaletteRole role)
        {
            switch (role)
            {
                case PaletteRole.Skin: return skin;
                case PaletteRole.Tunic: return tunic;
                case PaletteRole.Outer: return outer;
                case PaletteRole.Linen: return linen;
            }

            return null;
        }
    }

    /// <summary>
    /// What a character is wearing before the player touches anything, as catalogue item ids.
    ///
    /// A preset is a starting point, not an identity: nothing here locks a slot. Every one of these
    /// ids is an ordinary catalogue item the player may swap in the wardrobe, and the wardrobe is
    /// free to offer the whole catalogue from the first minute.
    /// </summary>
    public sealed class PresetDefaultItems
    {
        // The JSON key is "base", which is a C# keyword and cannot name a field. The attribute is
        // what binds the two; do not rename the key to match the field.
        [JsonProperty("base")] public string base_item;

        [JsonProperty("hair")] public string hair;
        [JsonProperty("outfit")] public string outfit;

        /// <summary>
        /// Accessories beyond the signature piece. Usually empty: the character document gives each
        /// character one accessory, and that one is <see cref="PresetDef.signature_item"/>.
        /// </summary>
        [JsonProperty("accessories")] public string[] accessories;
    }

    /// <summary>
    /// One playable character's starting point, from <c>Resources/Data/character_presets.json</c>.
    ///
    /// This is not <see cref="CharacterPresetDef"/>, and the two are not rivals. The catalogue's
    /// character block is the wardrobe's view of a character — the art layers it starts from and the
    /// anchor nothing may cover. This is character creation's view: the same identity plus the
    /// things creation needs and the wardrobe does not — the suggested name, the one-line
    /// personality, the key-art palette, and which catalogue items the character walks in wearing.
    ///
    /// Both files carry <c>id</c>, <c>accent</c> and <c>silhouette_anchor</c>, because this file has
    /// to be readable before <c>character_catalog.json</c> exists and because a creation screen that
    /// cannot draw an accent until the wardrobe loads is a worse trade than a duplicated hex string.
    /// <see cref="CharacterPresets.VerifyAgainstCatalog"/> is what stops the two drifting: it says
    /// exactly which field disagrees, and it stays silent while the catalogue is unauthored.
    ///
    /// Appearance is fixed, not progressive. Nothing here is a tier, nothing here changes with
    /// progress, and no field in this type is read from a score, a level or a currency — the game
    /// has none of the three. The only thing that ever changes a character's look is the player
    /// choosing something in the wardrobe.
    /// </summary>
    public sealed class PresetDef
    {
        [JsonProperty("id")] public string id;

        /// <summary>
        /// Accent colour as #RRGGBB. Adar is sky (#4E86A8), Neriah is growth (#4E9A6A) — the two
        /// ambient colours the design system gives the world as it heals. The hex is repeated here
        /// rather than taken from the UI layer so that content stays readable outside Unity and the
        /// player module keeps no dependency on the UI module.
        /// </summary>
        [JsonProperty("accent")] public string accent;

        /// <summary>
        /// The anchor this character's silhouette anomaly occupies, and which no other item may
        /// cover: Adar's coil of rope on <c>shoulder_r</c>, Neriah's map tube across
        /// <c>back_center</c>. It is how each of them is read at a glance from any distance.
        /// </summary>
        [JsonProperty("silhouette_anchor")] public string silhouette_anchor;

        /// <summary>
        /// The catalogue item that draws the silhouette anomaly: Adar's coil of rope, Neriah's map
        /// tube.
        ///
        /// It is an accessory like any other, and — this is the part that was wrong for two rounds —
        /// <b>it is anchored where the anomaly is</b>. <c>acc_rope_coil</c> declares
        /// <c>shoulder_r</c>, <c>acc_map_tube</c> declares <c>back_center</c>, matching the
        /// character's own <see cref="silhouette_anchor"/>.
        ///
        /// This field used to say the opposite, and defend it: the signature piece was authored with
        /// an empty anchor so that <see cref="CharacterCatalog.CanEquip"/> — which refuses any item
        /// covering the character's <c>silhouette_anchor</c> — would let it through. That worked by
        /// making the item invisible to the anchor system, which also made a second shoulder piece
        /// wearable straight through the coil of rope. The refusal is now handled where it belongs,
        /// by an exemption in <c>CanEquip</c> and <c>FindConflicts</c> keyed on the catalogue's own
        /// <c>signature_item</c>, so the anchor can be honest again.
        ///
        /// The catalogue carries the same field, and its copy is the one the equip checks read;
        /// this one is what character creation and <see cref="CharacterPresets.DefaultEquipped"/>
        /// use. <see cref="CharacterPresets.VerifyAgainstCatalog"/> reports a disagreement by name.
        /// </summary>
        [JsonProperty("signature_item")] public string signature_item;

        /// <summary>The catalogue items the character starts in. Every one of them is swappable.</summary>
        [JsonProperty("default_items")] public PresetDefaultItems default_items;

        /// <summary>The four key-art colours, from the character direction document.</summary>
        [JsonProperty("palette")] public PresetPaletteDef palette;

        /// <summary>Default swatch index on the catalogue's hair channel.</summary>
        [JsonProperty("hair_tint")] public int hair_tint;

        /// <summary>Default swatch index on the catalogue's fabric channel.</summary>
        [JsonProperty("fabric_tint")] public int fabric_tint;

        /// <summary>
        /// The layer indices to fall back on when the catalogue cannot dress the character —
        /// before <c>character_catalog.json</c> is authored, or when an item id in
        /// <see cref="default_items"/> does not resolve. Without it a creation screen would show an
        /// empty body on a build whose wardrobe content had not landed yet.
        ///
        /// When the catalogue can dress the character, it wins: see
        /// <see cref="CharacterPresets.ApplyTo"/>.
        /// </summary>
        [JsonProperty("art")] public ItemArtDef art;

        /// <summary>
        /// The name offered in the field. A suggestion and nothing more — the player types over it,
        /// at creation or at any point afterwards, and the game uses whatever they typed.
        /// Merged in from the locale file; never authored in character_presets.json.
        /// </summary>
        [JsonProperty("suggested_name")] public string suggested_name;

        /// <summary>
        /// The one-line personality, merged in from the locale file. Two short sentences, no more:
        /// "Carregador de pedras. Fala pouco." Both characters are ordinary people who decided to
        /// stay, and a line that promised a hero would be describing a different game.
        /// </summary>
        [JsonProperty("personality")] public string personality;

        [JsonIgnore] public CharacterAnchor SilhouetteAnchor;
        [JsonIgnore] public Color Accent = Color.white;
    }

    /// <summary>Root of Resources/Data/character_presets.json.</summary>
    public sealed class PresetsFile
    {
        [JsonProperty("presets")] public PresetDef[] presets;
    }

    /// <summary>Player-facing half of a preset, from locales/&lt;locale&gt;/presets.json.</summary>
    public sealed class PresetStrings
    {
        [JsonProperty("suggested_name")] public string suggested_name;
        [JsonProperty("personality")] public string personality;
    }

    /// <summary>Root of Resources/Data/locales/&lt;locale&gt;/presets.json.</summary>
    public sealed class PresetStringsFile
    {
        [JsonProperty("presets")] public Dictionary<string, PresetStrings> presets;

        /// <summary>
        /// The name-field hints, keyed by the suffix of
        /// <see cref="CharacterPresets.NameHintKey"/>: unset, too_short, too_long.
        /// </summary>
        [JsonProperty("name")] public Dictionary<string, string> name;

        /// <summary>What the game calls a player who typed no name.</summary>
        [JsonProperty("player_name_fallback")] public string player_name_fallback;
    }

    /// <summary>
    /// The two playable characters, as they are offered at character creation.
    ///
    /// The split is the one the rest of the project already uses, and the reason is balance rather
    /// than tidiness. Structure and numbers live in <c>Resources/Data/character_presets.json</c>,
    /// one copy, so a palette or a default item cannot drift between languages. Every string a
    /// player reads lives in <c>Resources/Data/locales/&lt;locale&gt;/presets.json</c> and is merged
    /// onto the same objects, so consumers see one object with every field filled.
    ///
    /// Three things this type deliberately does not do:
    ///
    /// * <b>It never writes <see cref="GameState"/>.</b> A suggested name is a pre-fill for a text
    ///   field, not an assignment to <c>playerName</c>; a name the player never looked at is not a
    ///   name they chose. Writing the save is the creation screen's job, at confirm, and this class
    ///   is what it reads to do it: <see cref="Get"/> resolves the chosen id, which the screen
    ///   records in <see cref="GameState.characterId"/>; <see cref="DefaultEquipped"/> gives the
    ///   item ids it seeds <see cref="GameState.equippedItems"/> with; and <see cref="ApplyTo"/>
    ///   lays the look down over the tone the player picked. Nothing here does any of that on its
    ///   own, because none of it should happen while the player is still browsing.
    /// * <b>It never locks a slot.</b> A preset says what the character walks in wearing and stops
    ///   there. Everything in it is swappable in the wardrobe, immediately and afterwards.
    /// * <b>It reads no score.</b> There is no tier, no progressive asset swap and no unlock in this
    ///   file — both characters are offered in full from the first minute. Unlocks belong to
    ///   catalogue items and live in <see cref="UnlockEvaluator"/>, which has its own rules about
    ///   what an unlock condition may say out loud.
    ///
    /// Every property is non-null before and after <see cref="LoadAll()"/>. A missing or malformed
    /// file logs an error and leaves the empty default in place, so a content mistake degrades
    /// character creation instead of stopping the build.
    ///
    /// The type is plural and the file is singular on purpose: the file is named for the thing it
    /// models, the type for the collection it holds.
    /// </summary>
    public static class CharacterPresets
    {
        const string ResourceFolder = "Data/";
        const string PresetsFileName = "character_presets";
        const string LocaleFileName = "/presets";

        // ------------------------------------------------------------------ name rules
        // Straight from the character direction document: 2 to 16 characters, the suggestion
        // pre-filled, changeable at any time. Public constants because the bound has to be quoted
        // in a hint and enforced by a field limit, and those are two different call sites.

        /// <summary>Shortest name the game accepts, once trimmed. An empty field is not a name.</summary>
        public const int MinNameLength = 2;

        /// <summary>Longest name the game accepts. Also the character limit of the input field.</summary>
        public const int MaxNameLength = 16;

        /// <summary>
        /// The token dialogue uses where the player's own name belongs. The same token the wardrobe
        /// catalogue uses — one spelling, referenced rather than repeated, because a second literal
        /// is how the two would end up disagreeing.
        /// </summary>
        public const string PlayerNameToken = CharacterCatalog.PlayerNameToken;

        /// <summary>Locale key for the word the game uses when the player typed no name.</summary>
        public const string KeyPlayerNameFallback = "player_name_fallback";

        /// <summary>Locale key prefix for the name-field hints.</summary>
        public const string KeyNamePrefix = "name.";

        public const string KeyNameUnset = KeyNamePrefix + "unset";
        public const string KeyNameTooShort = KeyNamePrefix + "too_short";
        public const string KeyNameTooLong = KeyNamePrefix + "too_long";

        // ------------------------------------------------------------------ state

        public static PresetDef[] All { get; private set; } = Array.Empty<PresetDef>();

        /// <summary>The locale whose strings are merged into the presets in memory.</summary>
        public static string LoadedLocale { get; private set; } = string.Empty;

        static readonly Dictionary<string, PresetDef> PresetsById =
            new Dictionary<string, PresetDef>(StringComparer.Ordinal);

        static readonly Dictionary<string, string> Strings =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Reads the presets for the active locale.</summary>
        public static void LoadAll()
        {
            LoadAll(Locales.Active);
        }

        /// <summary>
        /// Reads the presets, taking player-facing strings from this locale. Safe to call more than
        /// once; the last read wins, which is what a locale switch relies on.
        /// </summary>
        public static void LoadAll(string locale)
        {
            string canonical = Locales.Canonical(locale) ?? Locales.Source;
            LoadedLocale = canonical;

            // ---- structure and numbers: one copy, shared by every language
            PresetsFile file = LoadObject<PresetsFile>(ResourceFolder + PresetsFileName);
            All = file != null && file.presets != null ? file.presets : Array.Empty<PresetDef>();

            ResolveTokens();
            Reindex();

            // ---- strings: one file per language, merged onto the objects above
            MergeStrings(Locales.ResourceFolder(canonical) + LocaleFileName);
        }

        /// <summary>The preset with this id, or null.</summary>
        public static PresetDef Get(string id)
        {
            PresetDef preset;
            return !string.IsNullOrEmpty(id) && PresetsById.TryGetValue(id, out preset) ? preset : null;
        }

        /// <summary>
        /// The preset a creation screen opens on. The first authored one, so the order in the file
        /// is the order on screen and neither character is privileged by code.
        /// </summary>
        public static PresetDef Default
        {
            get { return All.Length > 0 ? All[0] : null; }
        }

        /// <summary>Every preset id, in authored order.</summary>
        public static string[] Ids()
        {
            var ids = new List<string>(All.Length);
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i] != null && !string.IsNullOrEmpty(All[i].id))
                {
                    ids.Add(All[i].id);
                }
            }

            return ids.ToArray();
        }

        // ------------------------------------------------------------------ the starting look

        /// <summary>
        /// The catalogue items this preset starts in, in the order they should be equipped. This is
        /// the list character creation writes into <see cref="GameState.equippedItems"/> at confirm,
        /// which is what makes the backpack open on a fresh save already showing what the character
        /// is wearing instead of nothing at all.
        ///
        /// The order matters and is not cosmetic. <see cref="AppearanceState"/> has a single
        /// accessory layer, and <see cref="CharacterCatalog.Compose"/> applies accessories in the
        /// order it is given them, so the last accessory in this list is the one that draws. The
        /// signature piece therefore goes last: an ordinary bag applied after it would erase the
        /// coil of rope or the map tube from the silhouette without erroring, and a character who
        /// silently stops being recognisable is the exact failure the anchor rules exist to prevent.
        ///
        /// This ordering makes the default set safe. It does not make an arbitrary set safe — see
        /// the note in <see cref="VerifyAgainstCatalog"/>.
        ///
        /// <b>An id the catalogue cannot resolve is skipped, never blanked around.</b> The two files
        /// are authored by different hands, so a preset can legitimately name an item on a build
        /// where the catalogue has not caught up. Dropping only the id that does not resolve keeps
        /// the rest of the character dressed and, more importantly, keeps it out of the save:
        /// <see cref="GameState.equippedItems"/> is persisted, so an id seeded here that draws
        /// nothing would sit in the file being warned about long after the mistake was fixed.
        ///
        /// Skipping is silent on purpose. It is not this method's job to report content mistakes —
        /// <see cref="VerifyAgainstCatalog"/> is the loud path and names every unresolvable default
        /// id, once, with what to change; this one runs on every character switch in creation, and
        /// a log here would bury that message under its own repetitions.
        /// </summary>
        public static List<string> DefaultEquipped(string presetId)
        {
            var equipped = new List<string>(4);

            PresetDef preset = Get(presetId);
            if (preset == null)
            {
                return equipped;
            }

            PresetDefaultItems items = preset.default_items;
            if (items != null)
            {
                AddIfResolvable(equipped, items.base_item);
                AddIfResolvable(equipped, items.outfit);
                AddIfResolvable(equipped, items.hair);

                if (items.accessories != null)
                {
                    for (int i = 0; i < items.accessories.Length; i++)
                    {
                        AddIfResolvable(equipped, items.accessories[i]);
                    }
                }
            }

            // Last, always. See the remark above.
            AddIfResolvable(equipped, preset.signature_item);

            return equipped;
        }

        // Appends an id that is worth wearing: named, not already in the list, and something the
        // catalogue can actually turn into art. Filtering here rather than in the caller is what
        // keeps the authored order intact — a later pass over the finished list could not tell the
        // signature piece from an ordinary accessory, and the signature piece has to stay last.
        static void AddIfResolvable(List<string> into, string id)
        {
            if (string.IsNullOrEmpty(id) || into.Contains(id) || !Resolvable(id))
            {
                return;
            }

            into.Add(id);
        }

        // Whether the catalogue can draw this id. While the catalogue is unauthored there is
        // nothing to ask, so the answer is yes and the authored ids pass through untouched: they
        // start resolving on the build where character_catalog.json lands, whereas a character
        // blanked because a file had not arrived yet would stay blank in the save that recorded it.
        // Same test ApplyTo uses to decide whether the catalogue can dress anyone at all.
        static bool Resolvable(string id)
        {
            if (CharacterCatalog.Items == null || CharacterCatalog.Items.Length == 0)
            {
                return true;
            }

            return CharacterCatalog.Item(id) != null;
        }

        /// <summary>
        /// Writes this preset's starting look onto an <see cref="AppearanceState"/>.
        ///
        /// The preset's own <c>art</c> block goes down first as a floor, then the catalogue dresses
        /// the character over it. When the catalogue is unauthored or an item id does not resolve,
        /// the floor is what remains and the character still draws — a creation screen that renders
        /// an empty body because content landed in a different order is a bug nobody can see from
        /// the log.
        ///
        /// <b>The skin tone is not part of the look this writes.</b> A character supplies the build,
        /// the face and the silhouette; the tone belongs to whoever is playing, and choosing Adar
        /// or Neriah must never decide it for them. So <see cref="AppearanceState.skin"/> is read
        /// before the floors go down and put back after — including over
        /// <see cref="CharacterCatalog.Compose"/>, which lays the catalogue's own character block
        /// down as a floor from a file this class does not load and cannot correct.
        ///
        /// The restore cannot erase anything real: no catalogue item names the skin layer, because
        /// a tone is never something worn, never earned and never taken away. If one ever did, this
        /// line is the one that refuses it, and refusing it is the correct reading of the rule.
        ///
        /// Both floors are meant to be authored without a tone in the first place — see the check
        /// in <see cref="ResolveTokens"/>, which says so out loud for the presets file. This is the
        /// guarantee behind that check rather than a substitute for it: the check reports the
        /// mistake, this makes it harmless while it is being fixed.
        /// </summary>
        public static void ApplyTo(AppearanceState into, string presetId)
        {
            if (into == null)
            {
                return;
            }

            PresetDef preset = Get(presetId);
            if (preset == null)
            {
                Debug.LogError("[Presets] No preset '" + presetId + "' in character_presets.json; the look is unchanged.");
                return;
            }

            // The player's, from before this call and after it. See the remark above.
            int chosenTone = into.skin;

            if (preset.art != null && !preset.art.IsEmpty)
            {
                preset.art.ApplyTo(into);
            }

            if (CharacterCatalog.Items != null && CharacterCatalog.Items.Length > 0)
            {
                CharacterCatalog.Compose(into, presetId, DefaultEquipped(presetId));
            }

            into.skin = chosenTone;
        }

        // ------------------------------------------------------------------ the player's name

        /// <summary>
        /// The name to pre-fill the field with. A suggestion: this is the only thing a preset says
        /// about the player's name, and nothing here writes it anywhere.
        /// </summary>
        public static string SuggestedName(string presetId)
        {
            PresetDef preset = Get(presetId);
            return preset != null && preset.suggested_name != null ? preset.suggested_name : string.Empty;
        }

        /// <summary>Trims a typed name. What the field holds is not always what the player meant.</summary>
        public static string Sanitize(string name)
        {
            return name == null ? string.Empty : name.Trim();
        }

        /// <summary>
        /// How acceptable a typed name is. Note that an empty field is <see cref="NameValidity.Unset"/>
        /// and not an error: clearing the name is a choice, and the game answers it with the
        /// fallback word rather than with a refusal.
        /// </summary>
        public static NameValidity Validate(string name)
        {
            string trimmed = Sanitize(name);

            if (trimmed.Length == 0) return NameValidity.Unset;
            if (trimmed.Length < MinNameLength) return NameValidity.TooShort;
            if (trimmed.Length > MaxNameLength) return NameValidity.TooLong;

            return NameValidity.Ok;
        }

        /// <summary>True when this validity should let the player carry on. Two of the four do.</summary>
        public static bool IsAcceptable(NameValidity validity)
        {
            return validity == NameValidity.Ok || validity == NameValidity.Unset;
        }

        /// <summary>True when this typed name should let the player carry on.</summary>
        public static bool IsAcceptable(string name)
        {
            return IsAcceptable(Validate(name));
        }

        /// <summary>
        /// The locale key for the hint under the name field, or null when there is nothing to say.
        /// A key, never a sentence: the words live in <c>locales/&lt;locale&gt;/presets.json</c>.
        /// </summary>
        public static string NameHintKey(NameValidity validity)
        {
            switch (validity)
            {
                case NameValidity.Unset: return KeyNameUnset;
                case NameValidity.TooShort: return KeyNameTooShort;
                case NameValidity.TooLong: return KeyNameTooLong;
            }

            return null;
        }

        /// <summary>
        /// The finished hint for the name field, in the loaded locale. Empty when the name is fine.
        ///
        /// The unset hint takes the fallback word rather than <see cref="PlayerName"/>: the field is
        /// empty right now, and quoting the name from a previous run would answer a question the
        /// player did not ask.
        /// </summary>
        public static string NameHint(NameValidity validity)
        {
            switch (validity)
            {
                case NameValidity.Unset: return Text(KeyNameUnset, FallbackName());
                case NameValidity.TooShort: return Text(KeyNameTooShort, MinNameLength);
                case NameValidity.TooLong: return Text(KeyNameTooLong, MaxNameLength);
            }

            return string.Empty;
        }

        /// <summary>
        /// What to call the player. Their own name when they typed one, otherwise the authored
        /// fallback for this locale — "viajante" in pt-BR, "traveller" in en. Neither word appears
        /// in this file: both are content, reached by key.
        /// </summary>
        public static string PlayerName(GameState state)
        {
            if (state != null && !string.IsNullOrWhiteSpace(state.playerName))
            {
                return state.playerName.Trim();
            }

            return FallbackName();
        }

        /// <summary>
        /// Replaces every <see cref="PlayerNameToken"/> in an authored line with the player's name.
        ///
        /// Used sparingly and only at moments of recognition. A name in every line stops being
        /// recognition and starts being mail merge, and the character document is explicit about it.
        /// </summary>
        public static string ApplyPlayerName(string text, GameState state)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(PlayerNameToken, StringComparison.Ordinal) < 0)
            {
                return text;
            }

            return text.Replace(PlayerNameToken, PlayerName(state));
        }

        static string FallbackName()
        {
            return Text(KeyPlayerNameFallback);
        }

        // ------------------------------------------------------------------ strings
        // These do not go through Loc: Loc reads ui.json and only ui.json. Presets keep their own
        // table for the same reason the wardrobe catalogue does — the strings belong to the content
        // file they describe. The miss behaviour is copied from Loc on purpose: a visible marker on
        // screen, an error in the log, and never a fallback to another language, because a silent
        // fallback that looks like real content hides a missing translation until a player finds it.

        /// <summary>True when this key exists in the loaded table.</summary>
        public static bool HasText(string key)
        {
            return !string.IsNullOrEmpty(key) && Strings.ContainsKey(key);
        }

        /// <summary>The string for this key, or a visible marker on a miss.</summary>
        public static string Text(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return Missing(string.Empty);
            }

            string value;
            if (Strings.TryGetValue(key, out value) && value != null)
            {
                return value;
            }

            Debug.LogError("[Presets] Missing key '" + key + "' in locale " + LoadedLocale +
                           ". Add it to Resources/Data/locales/" + LoadedLocale + "/presets.json.");
            return Missing(key);
        }

        /// <summary>
        /// The string for this key with positional arguments substituted. A malformed format string
        /// degrades to the raw template rather than throwing mid-frame.
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
                Debug.LogError("[Presets] Key '" + key + "' has a bad format string in locale " +
                               LoadedLocale + ": " + exception.Message);
                return template;
            }
        }

        // ------------------------------------------------------------------ agreement with the catalogue

        /// <summary>
        /// Checks every preset against the wardrobe catalogue and returns how many problems it
        /// found, each one already logged with what to change.
        ///
        /// Call it once after both <see cref="CharacterCatalog.LoadAll()"/> and
        /// <see cref="LoadAll()"/>, not from either loader: the two files are authored by different
        /// hands and land in either order, and a check that ran inside a load would fire on the
        /// build where only one of them had arrived.
        ///
        /// <b>Who calls it, so that it never stops being called.</b> <c>BootSequence.ApplyLocale</c>
        /// does, immediately after the two loads it sits between — every boot and every locale
        /// switch. <c>Wardrobe.EnsurePresetsLoaded</c> does too, and only on the path where the
        /// wardrobe itself had to load the presets: a scene opened without the boot sequence, where
        /// nothing else would ask. A change that moves either load has to bring its call with it.
        /// This method was written and then reached by nothing for a release, which reads in a diff
        /// exactly like coverage and reports precisely as much as no check at all.
        ///
        /// It returns 0 and logs nothing while the catalogue is unauthored. That is deliberate. This
        /// file has to be usable before <c>character_catalog.json</c> exists, and an error per field
        /// per preset on every boot is how a log stops being read.
        ///
        /// What it cannot check: whether a set the PLAYER assembles keeps the silhouette anomaly
        /// visible. <see cref="AppearanceState"/> has one accessory layer, so a second accessory
        /// drawn after the signature piece replaces it on screen while every anchor rule still
        /// passes. The wardrobe has to keep the signature piece last, or the art has to grow a
        /// layer. See <see cref="DefaultEquipped"/>.
        /// </summary>
        public static int VerifyAgainstCatalog()
        {
            bool haveItems = CharacterCatalog.Items != null && CharacterCatalog.Items.Length > 0;
            bool haveCharacters = CharacterCatalog.Characters != null && CharacterCatalog.Characters.Length > 0;

            if (!haveItems && !haveCharacters)
            {
                return 0;
            }

            int problems = 0;

            for (int i = 0; i < All.Length; i++)
            {
                PresetDef preset = All[i];
                if (preset == null || string.IsNullOrEmpty(preset.id))
                {
                    continue;
                }

                if (haveCharacters)
                {
                    problems += VerifyIdentity(preset);
                }

                if (haveItems)
                {
                    problems += VerifyItems(preset);
                    problems += VerifyTints(preset);
                }
            }

            return problems;
        }

        static int VerifyIdentity(PresetDef preset)
        {
            CharacterPresetDef twin = CharacterCatalog.Character(preset.id);
            if (twin == null)
            {
                Debug.LogError("[Presets] '" + preset.id + "' is in character_presets.json but not in the " +
                               "\"characters\" block of character_catalog.json, so the wardrobe cannot protect its " +
                               "silhouette anchor. Add it.");
                return 1;
            }

            int problems = 0;

            if (twin.SilhouetteAnchor != preset.SilhouetteAnchor)
            {
                Debug.LogError("[Presets] '" + preset.id + "' has silhouette_anchor " + preset.SilhouetteAnchor +
                               " in character_presets.json and " + twin.SilhouetteAnchor +
                               " in character_catalog.json. The catalogue's copy is the one that refuses items, " +
                               "so a disagreement leaves the anomaly unprotected.");
                problems++;
            }

            // The exemption that lets a character wear its own defining piece is read off the
            // CATALOGUE's copy of signature_item, never off this file's — CharacterCatalog cannot
            // ask CharacterPresets without closing a dependency cycle. So a disagreement here is not
            // cosmetic duplication: it is the exemption pointing at the wrong item, or at nothing.
            //
            // The dangerous shape is precise, and it is the one worth being loud about: the item is
            // anchored (so the character's silhouette anchor is genuinely occupied) and the
            // catalogue does not name it as the signature (so the exemption never fires). The player
            // takes their coil of rope off and can never put it back on, and the refusal they read
            // says the piece would hide what makes them recognisable.
            //
            // Everything else here stays quiet: while the item's anchor is blank there is nothing to
            // be exempt from, and content lands file by file.
            if (!string.IsNullOrEmpty(preset.signature_item)
                && !string.Equals(twin.signature_item, preset.signature_item, StringComparison.Ordinal))
            {
                CatalogItemDef signature = CharacterCatalog.Item(preset.signature_item);
                bool anchored = signature != null &&
                                (signature.Anchor != CharacterAnchor.None || signature.replaces_shoulder);

                if (anchored || !string.IsNullOrEmpty(twin.signature_item))
                {
                    Debug.LogError("[Presets] '" + preset.id + "' names '" + preset.signature_item +
                                   "' as its signature_item in character_presets.json, and " +
                                   (string.IsNullOrEmpty(twin.signature_item)
                                       ? "character_catalog.json names none"
                                       : "character_catalog.json names '" + twin.signature_item + "'") +
                                   ". The catalogue's copy is the one CanEquip and FindConflicts read, so the " +
                                   "character is about to be refused its own defining piece. Author the two " +
                                   "to agree.");
                    problems++;
                }
            }

            if (!SameColor(twin.Accent, preset.Accent))
            {
                Debug.LogError("[Presets] '" + preset.id + "' has accent '" + preset.accent +
                               "' in character_presets.json and '" + twin.accent +
                               "' in character_catalog.json.");
                problems++;
            }

            // The same rule the presets file is held to, checked on the copy this class cannot
            // correct: CharacterCatalog owns that file, and CharacterCatalog.Compose lays its "art"
            // block down as a floor. A tone there would take the player's choice away on every
            // recomposition — CharacterPresets.ApplyTo puts it back on the creation path, but that
            // is one caller of Compose and not the only one. Reported, never silently patched: this
            // method's job is to say which file to change.
            if (twin.art != null && twin.art.skin.HasValue)
            {
                Debug.LogError("[Presets] The \"characters\" block of '" + preset.id +
                               "' in character_catalog.json names a skin tone (\"skin\": " +
                               twin.art.skin.Value + "). Remove the key: a character declares a build, " +
                               "and the tone is the player's choice, so a character floor that carries one " +
                               "overwrites it every time the look is recomposed.");
                problems++;
            }

            return problems;
        }

        static int VerifyItems(PresetDef preset)
        {
            int problems = 0;

            PresetDefaultItems items = preset.default_items;
            if (items != null)
            {
                problems += VerifySlot(preset, items.base_item, CharacterSlot.Base);
                problems += VerifySlot(preset, items.hair, CharacterSlot.Hair);
                problems += VerifySlot(preset, items.outfit, CharacterSlot.Outfit);

                if (items.accessories != null)
                {
                    for (int i = 0; i < items.accessories.Length; i++)
                    {
                        problems += VerifySlot(preset, items.accessories[i], CharacterSlot.Accessory);
                    }
                }
            }

            problems += VerifySignature(preset);

            // The set as a whole, through the same call the wardrobe uses. A preset that cannot be
            // worn is the one content mistake a player would meet before touching anything.
            List<EquipConflict> conflicts = CharacterCatalog.FindConflicts(DefaultEquipped(preset.id), preset.id);
            for (int i = 0; i < conflicts.Count; i++)
            {
                Debug.LogError("[Presets] The default set of '" + preset.id + "' cannot be worn: " +
                               conflicts[i] + ".");
                problems++;
            }

            return problems;
        }

        static int VerifySlot(PresetDef preset, string itemId, CharacterSlot expected)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            CatalogItemDef item = CharacterCatalog.Item(itemId);
            if (item == null)
            {
                Debug.LogError("[Presets] '" + preset.id + "' starts in '" + itemId +
                               "', which is not in character_catalog.json. Add the item, or fix the id in " +
                               "character_presets.json.");
                return 1;
            }

            if (item.Slot != expected)
            {
                Debug.LogError("[Presets] '" + preset.id + "' names '" + itemId + "' as its " + expected +
                               " but the catalogue puts it in the " + item.Slot + " slot.");
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// The signature piece carries the silhouette anomaly, and it is the one item whose
        /// authoring the wardrobe cannot infer. It must be an accessory; it must hang where the
        /// anomaly hangs, or nowhere yet; and it must not replace the shoulder layer, because a
        /// piece that takes both shoulders is a cloak and a cloak is what the anchor exists to
        /// refuse.
        ///
        /// <b>What this check used to demand, and why it is inverted.</b> It used to require an
        /// EMPTY anchor, because <see cref="CharacterCatalog.CanEquip"/> refused anything occupying
        /// the character's <c>silhouette_anchor</c> and an anchorless item occupies nothing. That
        /// was a workaround wearing a validator's clothes: it bought a clean equip by taking the
        /// coil of rope out of the anchor system entirely, so a second shoulder piece could be worn
        /// straight through it. The catalogue now exempts a character's own signature by id, so the
        /// anchor is authored truthfully and this check asks the honest question instead: does the
        /// piece hang where the character says the anomaly is?
        ///
        /// An empty anchor stays legal and silent. Content lands file by file, and while the anchor
        /// is blank nothing is broken — the exemption has nothing to do, and no other item is being
        /// let through. What is NOT silent is the state in between: an anchored signature piece with
        /// no <c>signature_item</c> on the catalogue's side of the fence, which is checked in
        /// <see cref="VerifyIdentity"/> because that is the file where the exemption is read.
        /// </summary>
        static int VerifySignature(PresetDef preset)
        {
            if (string.IsNullOrEmpty(preset.signature_item))
            {
                Debug.LogError("[Presets] '" + preset.id + "' has no signature_item, so nothing draws its " +
                               "silhouette anomaly.");
                return 1;
            }

            CatalogItemDef item = CharacterCatalog.Item(preset.signature_item);
            if (item == null)
            {
                Debug.LogError("[Presets] The signature item '" + preset.signature_item + "' of '" + preset.id +
                               "' is not in character_catalog.json.");
                return 1;
            }

            int problems = 0;

            if (item.Slot != CharacterSlot.Accessory)
            {
                Debug.LogError("[Presets] The signature item '" + item.id + "' is in the " + item.Slot +
                               " slot; it has to be an accessory.");
                problems++;
            }

            if (item.replaces_shoulder)
            {
                Debug.LogError("[Presets] The signature item '" + item.id + "' of '" + preset.id +
                               "' sets replaces_shoulder. A piece that takes both shoulders is a cloak, and " +
                               "a cloak over the silhouette anomaly is exactly what the anchor rules exist " +
                               "to refuse. Author it with \"replaces_shoulder\": false and a single anchor.");
                problems++;
            }
            else if (item.Anchor != CharacterAnchor.None && item.Anchor != preset.SilhouetteAnchor)
            {
                Debug.LogError("[Presets] '" + preset.id + "' says its silhouette anomaly is at " +
                               preset.SilhouetteAnchor + ", but its signature item '" + item.id +
                               "' is anchored to " + item.Anchor + ". The piece that draws the anomaly has to " +
                               "hang where the anomaly is, or the anchor reserves a spot nothing occupies " +
                               "while the piece hangs somewhere it was never meant to.");
                problems++;
            }

            return problems;
        }

        static int VerifyTints(PresetDef preset)
        {
            int problems = 0;

            int hairSwatches = CharacterCatalog.SwatchCount(TintChannel.Hair);
            if (hairSwatches > 0 && (preset.hair_tint < 0 || preset.hair_tint >= hairSwatches))
            {
                Debug.LogError("[Presets] '" + preset.id + "' starts on hair swatch " + preset.hair_tint +
                               ", and the catalogue palette has " + hairSwatches + ".");
                problems++;
            }

            int fabricSwatches = CharacterCatalog.SwatchCount(TintChannel.Fabric);
            if (fabricSwatches > 0 && (preset.fabric_tint < 0 || preset.fabric_tint >= fabricSwatches))
            {
                Debug.LogError("[Presets] '" + preset.id + "' starts on fabric swatch " + preset.fabric_tint +
                               ", and the catalogue palette has " + fabricSwatches + ".");
                problems++;
            }

            return problems;
        }

        static bool SameColor(Color a, Color b)
        {
            const float Tolerance = 0.5f / 255f;
            return Mathf.Abs(a.r - b.r) < Tolerance
                   && Mathf.Abs(a.g - b.g) < Tolerance
                   && Mathf.Abs(a.b - b.b) < Tolerance;
        }

        // ------------------------------------------------------------------ token parsing

        static void ResolveTokens()
        {
            for (int i = 0; i < All.Length; i++)
            {
                PresetDef preset = All[i];
                if (preset == null)
                {
                    continue;
                }

                CharacterAnchor anchor;
                if (CharacterCatalog.TryParseAnchor(preset.silhouette_anchor, out anchor))
                {
                    preset.SilhouetteAnchor = anchor;
                }
                else
                {
                    preset.SilhouetteAnchor = CharacterAnchor.None;
                    Debug.LogError("[Presets] '" + preset.id + "' has an unknown silhouette_anchor '" +
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
                    Debug.LogError("[Presets] '" + preset.id + "' has an accent that is not #RRGGBB: '" +
                                   preset.accent + "'.");
                }

                ResolvePalette(preset);

                // A character declares a build; the tone belongs to the player. A floor that named
                // one would replace a choice the player made, on every recomposition, without a
                // word — so the value is dropped here rather than tolerated, and the block is
                // checked for emptiness afterwards, so an "art" block that named nothing but a tone
                // is reported for what it leaves behind: nothing to draw.
                if (preset.art != null && preset.art.skin.HasValue)
                {
                    Debug.LogError("[Presets] '" + preset.id + "' names a skin tone (\"skin\": " +
                                   preset.art.skin.Value + ") in its \"art\" block in character_presets.json. " +
                                   "Remove the key: a character supplies the build and the player supplies the " +
                                   "tone, and a floor that carries one overwrites the player's choice every time " +
                                   "the look is recomposed. Ignoring it.");
                    preset.art.skin = null;
                }

                if (preset.art == null || preset.art.IsEmpty)
                {
                    Debug.LogError("[Presets] '" + preset.id + "' has no \"art\" block, so it draws nothing " +
                                   "until character_catalog.json can dress it.");
                }

                // Player-facing words in the shared file would be one language for every language.
                // Clearing them rather than trusting them keeps the miss visible: the merge below
                // logs the gap and the screen shows the marker.
                if (preset.suggested_name != null || preset.personality != null)
                {
                    Debug.LogError("[Presets] '" + preset.id + "' carries player-facing text in " +
                                   "character_presets.json. suggested_name and personality belong in " +
                                   "Resources/Data/locales/<locale>/presets.json; the shared file holds structure " +
                                   "and numbers only. Ignoring them.");
                    preset.suggested_name = null;
                    preset.personality = null;
                }
            }
        }

        static void ResolvePalette(PresetDef preset)
        {
            PresetPaletteDef palette = preset.palette;
            if (palette == null)
            {
                Debug.LogError("[Presets] '" + preset.id + "' has no \"palette\" block.");
                return;
            }

            palette.Skin = ParseRole(preset.id, palette, PaletteRole.Skin);
            palette.Tunic = ParseRole(preset.id, palette, PaletteRole.Tunic);
            palette.Outer = ParseRole(preset.id, palette, PaletteRole.Outer);
            palette.Linen = ParseRole(preset.id, palette, PaletteRole.Linen);
        }

        static Color ParseRole(string presetId, PresetPaletteDef palette, PaletteRole role)
        {
            Color parsed;
            if (ParseColor(palette.Hex(role), out parsed))
            {
                return parsed;
            }

            Debug.LogError("[Presets] The " + role + " colour of '" + presetId + "' is not #RRGGBB: '" +
                           palette.Hex(role) + "'.");
            return Color.white;
        }

        static bool ParseColor(string hex, out Color color)
        {
            color = Color.white;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out color);
        }

        // ------------------------------------------------------------------ merging and indexing

        static void Reindex()
        {
            PresetsById.Clear();

            for (int i = 0; i < All.Length; i++)
            {
                PresetDef preset = All[i];
                if (preset == null || string.IsNullOrEmpty(preset.id))
                {
                    Debug.LogError("[Presets] Skipping a preset with no id in character_presets.json.");
                    continue;
                }

                if (PresetsById.ContainsKey(preset.id))
                {
                    Debug.LogError("[Presets] Duplicate preset id '" + preset.id + "'; the first one wins.");
                    continue;
                }

                PresetsById[preset.id] = preset;
            }
        }

        // A merge never invents a string. When the locale file has no entry for an id the field is
        // left null and the consumer's own fallback makes the gap visible, exactly as GameData does.
        static void MergeStrings(string resourcePath)
        {
            Strings.Clear();

            PresetStringsFile strings = LoadObject<PresetStringsFile>(resourcePath);
            if (strings == null)
            {
                return;
            }

            if (strings.name != null)
            {
                foreach (KeyValuePair<string, string> pair in strings.name)
                {
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                    {
                        Strings[KeyNamePrefix + pair.Key] = pair.Value;
                    }
                }
            }

            if (strings.player_name_fallback != null)
            {
                Strings[KeyPlayerNameFallback] = strings.player_name_fallback;
            }

            for (int i = 0; i < All.Length; i++)
            {
                PresetDef preset = All[i];
                if (preset == null || string.IsNullOrEmpty(preset.id))
                {
                    continue;
                }

                PresetStrings entry = null;
                if (strings.presets != null)
                {
                    strings.presets.TryGetValue(preset.id, out entry);
                }

                if (entry != null)
                {
                    preset.suggested_name = entry.suggested_name;
                    preset.personality = entry.personality;
                }
                else
                {
                    Debug.LogError("[Presets] Locale " + LoadedLocale + " has no \"presets\" entry '" + preset.id +
                                   "' in presets.json.");
                }
            }
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
                Debug.LogError("[Presets] Could not load Resources/" + resourcePath + ".json: " + exception.Message);
                return null;
            }

            if (asset == null)
            {
                Debug.LogError("[Presets] Missing content file Resources/" + resourcePath +
                               ".json; character creation will have no one to offer.");
                return null;
            }

            string text = asset.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogError("[Presets] Content file Resources/" + resourcePath + ".json is empty.");
                return null;
            }

            return text;
        }

        static void LogParseFailure(string resourcePath, string reason)
        {
            Debug.LogError("[Presets] Could not parse Resources/" + resourcePath + ".json: " + reason +
                           ". Using an empty default.");
        }

        // Angle quotes rather than plain brackets, matching Loc: a marker on screen cannot be
        // mistaken for authored punctuation, and it survives a grep for the key that produced it.
        static string Missing(string key)
        {
            return "⟨" + key + "⟩";
        }
    }
}
