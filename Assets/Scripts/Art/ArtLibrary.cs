using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// Every sprite in the game, generated procedurally as a Texture2D the first time it is
    /// asked for and cached from then on. There are no PNG assets, no sprite atlases and
    /// nothing to wire in the inspector: ArtLibrary.Get(key) is the whole art pipeline.
    ///
    /// ---------------------------------------------------------------------------------
    /// KEYS
    /// ---------------------------------------------------------------------------------
    /// Tiles, 32x32, pivot centered:
    ///     tile_ground  tile_rubble  tile_water  tile_house
    ///     wall_0 wall_1 wall_2 wall_3 wall_4      (0 = footing, 4 = finished and crenellated)
    ///
    /// Props, 32x32, pivot centered, transparent background:
    ///     prop_rubble  prop_well
    ///
    /// Character parts, 32x48, pivot bottom center (0.5, 0). Stack in this order:
    ///     body -> legs -> top -> acc
    ///     body_{0..1}_{dir}_{anim}_{frame}
    ///     legs_{0..3}   top_{0..3}   acc_{0..3}
    ///     dir   = down | up | left | right
    ///     anim  = idle | walk | work
    ///     frame = 0 | 1
    /// Trailing tokens are optional and default to down / idle / 0, so `top_2` is valid and
    /// so is `top_2_left_walk_1`. The extended form only changes shading and mirroring: the
    /// head, torso and thigh silhouettes are identical in every pose, which is what lets one
    /// static overlay sprite sit correctly on all 48 body sprites. See CharacterArt.
    ///
    /// UI, the original three, 32x32 at 32 pixels per unit. Nine-slice friendly: set
    /// Image.type = Sliced and the borders come along.
    ///     ui_panel   border 8    chrome
    ///     ui_bubble  border 9    dialogue and reader
    ///     ui_button  border 8    button with a bevel
    ///
    /// UI, Sistema Vale, at 100 pixels per unit so one sprite pixel is one canvas reference unit:
    ///     ui_frame_sm  ui_frame_md  ui_frame_lg     nine-sliced, one per design radius
    ///     ui_frame_scroll                           the pergaminho card, soft elevated edge
    ///     ui_focus_ring                             2 point ring, transparent centre
    ///     ui_bar_track  ui_bar_fill                 pill ended progress bar
    ///     ui_icon_check  ui_icon_close  ui_icon_dot
    ///     ui_icon_arrow  ui_icon_menu   ui_icon_lock
    ///     ui_icon_bag
    ///     ui_icon_help
    /// Every one of these is painted neutral — white body, form in the alpha channel — so that
    /// Image.color yields the design token colour exactly rather than sprite x tint. See UiArt.
    ///
    /// ---------------------------------------------------------------------------------
    /// CONVENTIONS
    /// ---------------------------------------------------------------------------------
    /// - World and character art: 32 pixels per unit, FilterMode.Point, no mipmaps, no
    ///   filtering, no compression. The Sistema Vale UI sprites are the one exception and say
    ///   why below.
    /// - Keys are case insensitive and trimmed.
    /// - An unknown key never returns null and never throws: it logs one warning and returns
    ///   a magenta checker, so a wrong key is loud in play mode instead of invisible.
    /// - GetTinted(key, tint) multiplies the cached pixels and caches the result per tint.
    ///   That is the NPC palette swap: same body, different cloth and skin.
    /// - Everything is deterministic. Texture detail comes from ValueNoise seeded by the key
    ///   string, never from UnityEngine.Random, so two runs produce identical bytes.
    /// - The palette is three base colors plus neutrals, defined once in ArtPalette.
    /// </summary>
    public static class ArtLibrary
    {
        public const float PixelsPerUnit = 32f;

        /// <summary>Tiles and props are centered on their cell.</summary>
        public static readonly Vector2 TilePivot = new Vector2(0.5f, 0.5f);

        /// <summary>Character parts are anchored at the feet so layers and sorting line up.</summary>
        public static readonly Vector2 CharacterPivot = new Vector2(0.5f, 0f);

        public static readonly Vector2 UiPivot = new Vector2(0.5f, 0.5f);

        static readonly Vector4 NoBorder = Vector4.zero;

        sealed class Entry
        {
            public Sprite sprite;
            public Color32[] pixels;
            public int width;
            public int height;
            public Vector2 pivot;
            public Vector4 border;

            /// <summary>
            /// Carried per entry rather than taken from <see cref="PixelsPerUnit"/> because the
            /// Sistema Vale UI sprites are generated at the canvas's own scale; see
            /// <see cref="UiArt.PixelsPerUnit"/>.
            /// </summary>
            public float pixelsPerUnit;

            /// <summary>
            /// Point everywhere except the Sistema Vale UI sprites, whose corners and icon
            /// strokes are antialiased curves rather than pixel art. Point sampling a curve that
            /// is being resampled at all turns its alpha ramp into a staircase, which is the one
            /// artefact those sprites exist to avoid.
            /// </summary>
            public FilterMode filterMode;
        }

        static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static readonly Dictionary<string, Sprite> TintCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        static readonly HashSet<string> WarnedKeys = new HashSet<string>(StringComparer.Ordinal);
        static Entry _fallback;

        /// <summary>Sprite for a key. Cached; generated on first use. Never null.</summary>
        public static Sprite Get(string key)
        {
            return Resolve(Normalize(key)).sprite;
        }

        /// <summary>
        /// Per-tint variant of a key, cached separately. Used for NPC palette swaps: the tint
        /// multiplies the source pixels, so it darkens and colors without flattening detail.
        /// </summary>
        public static Sprite GetTinted(string key, Color tint)
        {
            string normalized = Normalize(key);
            string tintKey = normalized + "#" + ColorUtility.ToHtmlStringRGBA(tint);

            Sprite cached;
            if (TintCache.TryGetValue(tintKey, out cached) && cached != null) return cached;

            Entry source = Resolve(normalized);
            Color32[] tinted = new Color32[source.pixels.Length];
            for (int i = 0; i < tinted.Length; i++) tinted[i] = ArtPalette.Multiply(source.pixels[i], tint);

            Sprite sprite = BuildSprite(tintKey, tinted, source.width, source.height, source.pivot,
                                        source.border, source.pixelsPerUnit, source.filterMode);
            TintCache[tintKey] = sprite;
            return sprite;
        }

        /// <summary>
        /// Drops every cached sprite. Textures created here are plain runtime objects, so the
        /// garbage collector reclaims them once nothing references them. Call this only when
        /// deliberately regenerating the art, never in the normal game flow.
        /// </summary>
        public static void ClearCache()
        {
            Cache.Clear();
            TintCache.Clear();
            WarnedKeys.Clear();
            _fallback = null;
        }

        // ------------------------------------------------------------------ internals

        static string Normalize(string key)
        {
            return string.IsNullOrEmpty(key) ? string.Empty : key.Trim().ToLowerInvariant();
        }

        static Entry Resolve(string key)
        {
            Entry entry;
            if (Cache.TryGetValue(key, out entry) && entry != null && entry.sprite != null) return entry;

            entry = Build(key);
            Cache[key] = entry;
            return entry;
        }

        static Entry Build(string key)
        {
            // A drawn tile wins over the generated one. Falling through rather than failing is
            // what lets the swap happen a key at a time, and what keeps the game running if the
            // sheet is ever missing.
            Color32[] drawn;
            int drawnSize;
            if (Tileset.TryGetPixels(key, out drawn, out drawnSize))
            {
                return Make(key, drawn, drawnSize, drawnSize, TilePivot, NoBorder,
                            PixelsPerUnit, FilterMode.Point);
            }

            int seed = ValueNoise.SeedFrom(key);

            switch (key)
            {
                case ArtKeys.TileGround: return World(key, TileArt.Ground(seed));
                case ArtKeys.TileRubble: return World(key, TileArt.RubbleTile(seed));
                case ArtKeys.TileWater: return World(key, TileArt.Water(seed));
                case ArtKeys.TileHouse: return World(key, TileArt.House(seed));
                case ArtKeys.PropRubble: return World(key, TileArt.PropRubble(seed));
                case ArtKeys.PropWell: return World(key, TileArt.PropWell(seed));
                case ArtKeys.PropMat: return World(key, TileArt.PropMat(seed));
                case ArtKeys.UiPanel: return Ui(key, UiArt.Panel(), UiArt.PanelBorder);
                case ArtKeys.UiBubble: return Ui(key, UiArt.Bubble(), UiArt.BubbleBorder);
                case ArtKeys.UiButton: return Ui(key, UiArt.Button(), UiArt.ButtonBorder);

                case ArtKeys.UiFrameSm: return Frame(key, UiArt.FrameSm(), UiArt.FrameSmBorder);
                case ArtKeys.UiFrameMd: return Frame(key, UiArt.FrameMd(), UiArt.FrameMdBorder);
                case ArtKeys.UiFrameLg: return Frame(key, UiArt.FrameLg(), UiArt.FrameLgBorder);
                case ArtKeys.UiFrameScroll: return Frame(key, UiArt.FrameScroll(), UiArt.FrameScrollBorder);
                case ArtKeys.UiFocusRing: return Frame(key, UiArt.FocusRing(), UiArt.FocusRingBorder);
                case ArtKeys.UiBarTrack: return Frame(key, UiArt.BarTrack(), UiArt.BarBorder);
                case ArtKeys.UiBarFill: return Frame(key, UiArt.BarFill(), UiArt.BarBorder);

                case ArtKeys.IconCheck: return Icon(key, UiArt.IconCheck());
                case ArtKeys.IconClose: return Icon(key, UiArt.IconClose());
                case ArtKeys.IconDot: return Icon(key, UiArt.IconDot());
                case ArtKeys.IconArrow: return Icon(key, UiArt.IconArrow());
                case ArtKeys.IconMenu: return Icon(key, UiArt.IconMenu());
                case ArtKeys.IconLock: return Icon(key, UiArt.IconLock());
                case ArtKeys.IconBag: return Icon(key, UiArt.IconBag());
                case ArtKeys.IconHelp: return Icon(key, UiArt.IconHelp());
            }

            // tile_ground_1 .. tile_ground_5. The seed comes from the key, so each variant is a
            // different draw of the same ground rather than a different kind of ground.
            if (key.StartsWith(ArtKeys.TileGround, StringComparison.Ordinal))
            {
                return World(key, TileArt.Ground(seed));
            }

            if (key.StartsWith(ArtKeys.WallPrefix, StringComparison.Ordinal))
            {
                int stage;
                if (ArtKeys.TryParseInt(key.Substring(ArtKeys.WallPrefix.Length), out stage))
                {
                    int clamped = Mathf.Clamp(stage, 0, ArtKeys.WallStageCount - 1);
                    if (clamped != stage) Warn(key, "wall stage out of range, clamped to " + clamped);
                    // Seed by the clamped stage so the same stage always looks the same.
                    return World(key, TileArt.Wall(clamped, ValueNoise.SeedFrom(ArtKeys.Wall(clamped))));
                }
            }

            int variant;
            ArtFacing facing;
            ArtAnim anim;
            int frame;

            if (ArtKeys.TryParsePart(key, ArtKeys.BodyPrefix, CharacterArt.BodyVariants, out variant, out facing, out anim, out frame))
                return Character(key, CharacterArt.Body(variant, facing, anim, frame));

            if (ArtKeys.TryParsePart(key, ArtKeys.LegsPrefix, CharacterArt.LegsVariants, out variant, out facing, out anim, out frame))
                return Character(key, CharacterArt.Legs(variant, facing));

            if (ArtKeys.TryParsePart(key, ArtKeys.TopPrefix, CharacterArt.TopVariants, out variant, out facing, out anim, out frame))
                return Character(key, CharacterArt.Top(variant, facing));

            if (ArtKeys.TryParsePart(key, ArtKeys.AccessoryPrefix, CharacterArt.AccessoryVariants, out variant, out facing, out anim, out frame))
                // anim and frame are passed through, not discarded: a wrist piece has to follow
                // the hand, and the pose-aware overload is the only thing that knows where the
                // hand is this frame. Dropping them pinned the beads to the idle pose, where
                // they floated off the body entirely during the work swing.
                return Character(key, CharacterArt.Accessory(variant, facing, anim, frame));

            if (ArtKeys.TryParsePart(key, ArtKeys.HairPrefix, CharacterArt.HairVariants, out variant, out facing, out anim, out frame))
                return Character(key, CharacterArt.Hair(variant, facing));

            Warn(key, "unknown art key");
            return Fallback();
        }

        static Entry World(string key, PixelCanvas canvas)
        {
            return Make(key, canvas, TilePivot, NoBorder);
        }

        static Entry Character(string key, PixelCanvas canvas)
        {
            return Make(key, canvas, CharacterPivot, NoBorder);
        }

        /// <summary>The original three UI frames, at the project's 32 pixels per unit.</summary>
        static Entry Ui(string key, PixelCanvas canvas, Vector4 border)
        {
            return Make(key, canvas, UiPivot, border);
        }

        /// <summary>
        /// A Sistema Vale frame, bar or ring. Generated at <see cref="UiArt.PixelsPerUnit"/> so
        /// its border and corner radius land in canvas reference units one for one, and sampled
        /// bilinearly because what it carries is a curve.
        /// </summary>
        static Entry Frame(string key, PixelCanvas canvas, Vector4 border)
        {
            return Make(key, canvas.ToArray(), canvas.Width, canvas.Height, UiPivot, border,
                        UiArt.PixelsPerUnit, FilterMode.Bilinear);
        }

        /// <summary>A status icon: same scale as the frames, no border, so it draws unsliced.</summary>
        static Entry Icon(string key, PixelCanvas canvas)
        {
            return Make(key, canvas.ToArray(), canvas.Width, canvas.Height, UiPivot, NoBorder,
                        UiArt.PixelsPerUnit, FilterMode.Bilinear);
        }

        static Entry Make(string key, PixelCanvas canvas, Vector2 pivot, Vector4 border)
        {
            return Make(key, canvas.ToArray(), canvas.Width, canvas.Height, pivot, border,
                        PixelsPerUnit, FilterMode.Point);
        }

        static Entry Make(string key, Color32[] pixels, int width, int height, Vector2 pivot, Vector4 border,
                          float pixelsPerUnit, FilterMode filterMode)
        {
            Entry entry = new Entry();
            entry.sprite = BuildSprite(key, pixels, width, height, pivot, border, pixelsPerUnit, filterMode);
            entry.pixels = pixels;
            entry.width = width;
            entry.height = height;
            entry.pivot = pivot;
            entry.border = border;
            entry.pixelsPerUnit = pixelsPerUnit;
            entry.filterMode = filterMode;
            return entry;
        }

        static Sprite BuildSprite(string name, Color32[] pixels, int width, int height, Vector2 pivot,
                                  Vector4 border, float pixelsPerUnit, FilterMode filterMode)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "art_" + name;
            texture.filterMode = filterMode;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                pivot,
                pixelsPerUnit,
                0u,
                SpriteMeshType.FullRect,
                border);
            sprite.name = name;
            return sprite;
        }

        static Entry Fallback()
        {
            if (_fallback != null && _fallback.sprite != null) return _fallback;

            PixelCanvas canvas = new PixelCanvas(TileArt.Size, TileArt.Size);
            for (int y = 0; y < TileArt.Size; y++)
            {
                for (int x = 0; x < TileArt.Size; x++)
                {
                    bool even = ((x / 8) + (y / 8)) % 2 == 0;
                    canvas.Set(x, y, even ? ArtPalette.Missing : ArtPalette.Ink);
                }
            }
            _fallback = Make("art_missing", canvas, TilePivot, NoBorder);
            return _fallback;
        }

        static void Warn(string key, string reason)
        {
            if (!WarnedKeys.Add(key)) return;
            Debug.LogWarning("[ArtLibrary] " + reason + ": '" + key + "'");
        }
    }
}
