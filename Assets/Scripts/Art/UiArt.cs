using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// The three nine-slice UI frames. Each is a 32x32 rounded rectangle whose corner radius
    /// fits inside its border, so an Image set to Type = Sliced scales it to any size without
    /// distorting the corners or breaking the highlight lines.
    ///
    /// Border order matches UnityEngine.Sprite: (left, bottom, right, top).
    /// </summary>
    public static class UiArt
    {
        public const int Size = 32;

        public static readonly Vector4 PanelBorder = new Vector4(8f, 8f, 8f, 8f);
        public static readonly Vector4 BubbleBorder = new Vector4(9f, 9f, 9f, 9f);
        public static readonly Vector4 ButtonBorder = new Vector4(8f, 8f, 8f, 8f);

        /// <summary>Dark chrome: HUD blocks, the end of day panel, the contest frame.</summary>
        public static PixelCanvas Panel()
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            canvas.FillRoundedRect(0, 0, Size, Size, 7, ArtPalette.Ink);
            canvas.FillRoundedRect(1, 1, Size - 2, Size - 2, 6, ArtPalette.Shadow);
            canvas.FillRoundedRect(2, 2, Size - 4, Size - 4, 5, ArtPalette.StoneDeep);
            // A highlight along the top and a shadow along the bottom. Both span the whole
            // stretchable middle, so they stay continuous at any panel width.
            canvas.HLine(3, 2, Size - 6, ArtPalette.Neutral);
            canvas.HLine(3, Size - 3, Size - 6, ArtPalette.Ink);
            return canvas;
        }

        /// <summary>Light chrome: the dialogue bubble and the chapter reader page.</summary>
        public static PixelCanvas Bubble()
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            canvas.FillRoundedRect(0, 0, Size, Size, 8, ArtPalette.Ink);
            canvas.FillRoundedRect(1, 1, Size - 2, Size - 2, 7, ArtPalette.StoneDark);
            canvas.FillRoundedRect(2, 2, Size - 4, Size - 4, 6, ArtPalette.Paper);
            canvas.HLine(4, Size - 4, Size - 8, ArtPalette.Light);
            canvas.HLine(4, 3, Size - 8, ArtPalette.Paper);
            return canvas;
        }

        /// <summary>Clay button with a top highlight and a two pixel bottom bevel.</summary>
        public static PixelCanvas Button()
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            canvas.FillRoundedRect(0, 0, Size, Size, 6, ArtPalette.Ink);
            canvas.FillRoundedRect(1, 1, Size - 2, Size - 2, 5, ArtPalette.ClayDeep);
            canvas.FillRoundedRect(2, 2, Size - 4, Size - 6, 4, ArtPalette.ClayMid);
            canvas.HLine(4, 3, Size - 8, ArtPalette.ClayLight);
            canvas.HLine(4, Size - 7, Size - 8, ArtPalette.ClayDark);
            return canvas;
        }
    }
}
