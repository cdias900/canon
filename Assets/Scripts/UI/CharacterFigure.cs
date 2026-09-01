using SheepGate.Core;
using SheepGate.Player;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The five-layer paper doll, in the one place both screens that draw it can reach.
    ///
    /// The character is not a sprite. It is five stacked <see cref="Image"/>s — body, legs, top,
    /// accessory, hair — each fitted to the same 32x48 rect, and the stack is the same one the
    /// in-world character uses. Sibling order <i>is</i> draw order, so the order this class parents
    /// them in is load-bearing and cannot be reshuffled for tidiness.
    ///
    /// <b>Why this is a file and not two private copies.</b> The character creation preview and the
    /// backpack's stage carried byte-identical copies of <c>BuildLayer</c>, <c>ApplyLayer</c>,
    /// <c>SetVerticalBand</c> and <c>Shade</c>, and the creation copy also carried a second copy of
    /// the fallback bands. Those bands are the interesting part: they are what makes an unfinished
    /// art layer read as a coloured strip in roughly the right place on the body rather than as a
    /// full-figure block of colour, and there is no automated gate that would notice one copy
    /// drifting from the other. Two screens drawing the same character differently is a bug nobody
    /// would file, because each screen looks self-consistent.
    ///
    /// <b>The fallback is a feature, not a stopgap.</b> The catalogue promises more pieces than the
    /// art draws, and a missing sprite degrades to a colour band shaded by the layer index. That is
    /// what lets a wardrobe row for an unfinished piece still read as a different piece from the one
    /// above it while the shape is being drawn.
    /// </summary>
    public static class CharacterFigure
    {
        // The five art layers, in draw order. The last one drawn sits on top. These indices are the
        // positions in the array Build() returns, and every caller reads the array by name rather
        // than by a number written at the call site.

        public const int LayerBody = 0;
        public const int LayerLegs = 1;
        public const int LayerTop = 2;
        public const int LayerAccessory = 3;
        public const int LayerHair = 4;
        public const int LayerCount = 5;

        /// <summary>
        /// GameObject names of the five layers. Spelled out rather than generated, because these
        /// are handles <c>tools/e2e.sh</c> reads the figure by: the hair layer's sprite changing is
        /// how the run proves an equip reached the character.
        /// </summary>
        static readonly string[] LayerObjectNames = { "Body", "Legs", "Top", "Accessory", "Hair" };

        /// <summary>
        /// The character sprites are 32x48, and every figure in the interface is <i>fitted</i> to
        /// that ratio rather than given a size.
        ///
        /// Fitting rather than sizing is what keeps one number right on both canvases the game
        /// runs on. A fixed 200x300 box fits the 1080-unit reference and overflows its cell on a
        /// phone, where the scaler reports about 977 units across; an
        /// <see cref="AspectRatioFitter"/> in <see cref="AspectRatioFitter.AspectMode.FitInParent"/>
        /// mode is as large as the parent allows at either width.
        /// </summary>
        public static readonly float FigureAspect =
            SheepGate.Art.CharacterArt.Width / (float)SheepGate.Art.CharacterArt.Height;

        /// <summary>
        /// A rect inside <paramref name="parent"/> that holds the figure at the character ratio.
        ///
        /// Stretched to the parent and then constrained by the fitter, so the caller decides the
        /// box and this decides the shape inside it. The parent is expected to be the area left
        /// over once captions and padding have taken theirs — this method reserves nothing.
        /// </summary>
        public static RectTransform CreateFigureRect(RectTransform parent, string objectName)
        {
            RectTransform figure = UIKit.CreateRect(objectName, parent);
            UIKit.Stretch(figure);

            var aspect = figure.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = FigureAspect;
            return figure;
        }

        /// <summary>
        /// The five layers, parented in draw order, all blank.
        ///
        /// Every one of them is created even when the look has nothing to put in it: a layer that
        /// is built lazily arrives at the end of the sibling list and draws over the hair.
        /// </summary>
        public static Image[] Build(RectTransform figure)
        {
            var layers = new Image[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                layers[i] = BuildLayer(figure, LayerObjectNames[i]);
            }

            return layers;
        }

        /// <summary>One blank layer, stretched to the figure and out of the raycast.</summary>
        public static Image BuildLayer(RectTransform figure, string objectName)
        {
            Image image = UIKit.CreatePanel(figure, objectName, Color.white, null);
            UIKit.Stretch((RectTransform)image.transform);
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        /// <summary>
        /// Paints a whole look onto a stack built by <see cref="Build"/>.
        ///
        /// The four worn layers read their indices straight off <see cref="AppearanceState"/>;
        /// the body reads <see cref="AppearanceState.BodyArtVariant"/>, which is where the build
        /// and the player's skin tone are packed into the one sprite that carries both.
        /// </summary>
        public static void Apply(Image[] layers, AppearanceState look, FacingDirection facing)
        {
            if (layers == null || look == null)
            {
                return;
            }

            ApplyLayer(At(layers, LayerBody),
                UIKit.GetSprite(UiSpriteKeys.Body(look.BodyArtVariant, facing, 0)),
                Shade(DesignTokens.Neutral.N500, look.body), 0f, 1f);

            ApplyLayer(At(layers, LayerLegs),
                UIKit.GetSprite(UiSpriteKeys.Legs(look.legs)),
                Shade(DesignTokens.Ambient.Sky, look.legs), 0.04f, 0.44f);

            ApplyLayer(At(layers, LayerTop),
                UIKit.GetSprite(UiSpriteKeys.Top(look.top)),
                Shade(DesignTokens.Brand.Primary, look.top), 0.44f, 0.76f);

            // The accessory takes the facing for the same reason the body and the hair do, and it
            // is the layer where forgetting shows least and matters most: it is anchored to a named
            // body part rather than mirrored, so a facing-free key does not draw a slightly wrong
            // accessory, it draws the front one on a back view.
            ApplyLayer(At(layers, LayerAccessory),
                UIKit.GetSprite(UiSpriteKeys.Accessory(look.accessory, facing)),
                Shade(DesignTokens.Ambient.Growth, look.accessory), 0.78f, 0.97f);

            // Hair across the full figure, not just the head band: the sprite is head-height
            // already, and cropping it to a band would move it down the body.
            ApplyLayer(At(layers, LayerHair),
                UIKit.GetSprite(SheepGate.Art.ArtKeys.Hair(look.hair, UiSpriteKeys.ToArtFacing(facing))),
                Shade(DesignTokens.Ambient.Sky, look.hair), 0.76f, 1f);
        }

        /// <summary>
        /// Paints only the body layer of a stack. Used when the tone changes and nothing else has:
        /// the piece is the same piece, the person wearing it is not.
        /// </summary>
        public static void ApplyBody(Image[] layers, int bodyArtVariant, int shadeIndex, FacingDirection facing)
        {
            ApplyBody(At(layers, LayerBody), bodyArtVariant, shadeIndex, facing);
        }

        /// <summary>
        /// The same, on a lone body layer. Thumbnails build only the layers their piece needs, so
        /// they hold one <see cref="Image"/> rather than a stack of five.
        /// </summary>
        public static void ApplyBody(Image body, int bodyArtVariant, int shadeIndex, FacingDirection facing)
        {
            ApplyLayer(body,
                UIKit.GetSprite(UiSpriteKeys.Body(bodyArtVariant, facing, 0)),
                Shade(DesignTokens.Neutral.N500, shadeIndex), 0f, 1f);
        }

        /// <summary>
        /// A present sprite fills the figure, exactly as the in-world character stacks its layers.
        /// A missing one degrades to a coloured band in that layer's part of the figure, so the
        /// pieces still read as different from one another while art is being drawn.
        /// </summary>
        public static void ApplyLayer(Image image, Sprite sprite, Color fallback, float fallbackBottom, float fallbackTop)
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

        /// <summary>Anchors a layer to a horizontal slice of the figure, full width.</summary>
        public static void SetVerticalBand(RectTransform rect, float bottom, float top)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, bottom);
            rect.anchorMax = new Vector2(1f, top);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// The fallback colour for one variant of a layer: the layer's base colour walked toward
        /// the primary ink, so two variants of the same layer are never the same flat block.
        /// </summary>
        public static Color Shade(Color baseColor, int index)
        {
            return Color.Lerp(baseColor, DesignTokens.Ink.Primary, Mathf.Clamp01(index * 0.2f));
        }

        /// <summary>A layer by index, or null. Never throws on a stack that was built short.</summary>
        static Image At(Image[] layers, int index)
        {
            return layers != null && index >= 0 && index < layers.Length ? layers[index] : null;
        }
    }
}
