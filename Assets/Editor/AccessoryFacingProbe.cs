using System.Text;
using SheepGate.Art;
using SheepGate.Player;
using SheepGate.UI;
using UnityEngine;

namespace SheepGate.EditorTools
{
    /// <summary>
    /// TEMPORARY verification harness. Renders every accessory variant through the same key path
    /// the UI uses and compares the resulting pixels facing by facing. Delete after reading.
    /// </summary>
    public static class AccessoryFacingProbe
    {
        static Color32[] Pixels(string key)
        {
            Sprite sprite = ArtLibrary.Get(key);
            return sprite.texture.GetPixels32();
        }

        static int Differences(Color32[] a, Color32[] b)
        {
            int n = Mathf.Min(a.Length, b.Length);
            int diff = Mathf.Abs(a.Length - b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) diff++;
            }
            return diff;
        }

        static int Opaque(Color32[] a)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (a[i].a != 0) n++;
            return n;
        }

        public static void Run()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[PROBE] accessory variants: " + CharacterArt.AccessoryVariants);

            for (int v = 0; v < CharacterArt.AccessoryVariants; v++)
            {
                string kd = UiSpriteKeys.Accessory(v, FacingDirection.Down);
                string ku = UiSpriteKeys.Accessory(v, FacingDirection.Up);
                string kl = UiSpriteKeys.Accessory(v, FacingDirection.Left);
                string kr = UiSpriteKeys.Accessory(v, FacingDirection.Right);

                Color32[] d = Pixels(kd);
                Color32[] u = Pixels(ku);
                Color32[] l = Pixels(kl);
                Color32[] r = Pixels(kr);

                sb.AppendLine("[PROBE] variant " + v
                    + " keys L=" + kl + " R=" + kr
                    + " | opaque D=" + Opaque(d) + " U=" + Opaque(u) + " L=" + Opaque(l) + " R=" + Opaque(r)
                    + " | LvR=" + Differences(l, r)
                    + " DvU=" + Differences(d, u)
                    + " DvL=" + Differences(d, l)
                    + " DvR=" + Differences(d, r)
                    + " UvL=" + Differences(u, l)
                    + " UvR=" + Differences(u, r));
            }

            // The legacy no-facing overload must still resolve to the Down drawing, which is what
            // the wardrobe thumbnail and the backpack stage deliberately ask for.
            for (int v = 0; v < CharacterArt.AccessoryVariants; v++)
            {
                int same = Differences(Pixels(UiSpriteKeys.Accessory(v)),
                                       Pixels(UiSpriteKeys.Accessory(v, FacingDirection.Down)));
                sb.AppendLine("[PROBE] variant " + v + " bare-key vs Down diff=" + same);
            }

            // The bracelet is the only pose-dependent variant; prove the pose reaches it too.
            for (int v = 0; v < CharacterArt.AccessoryVariants; v++)
            {
                Color32[] idle = ArtCanvasPixels(v, ArtFacing.Right, ArtAnim.Idle, 0);
                Color32[] work = ArtCanvasPixels(v, ArtFacing.Right, ArtAnim.Work, 1);
                sb.AppendLine("[PROBE] variant " + v + " Right idle0 vs work1 diff=" + Differences(idle, work));
            }

            Debug.Log(sb.ToString());
        }

        static Color32[] ArtCanvasPixels(int variant, ArtFacing facing, ArtAnim anim, int frame)
        {
            PixelCanvas c = CharacterArt.Accessory(variant, facing, anim, frame);
            Color32[] px = new Color32[c.Width * c.Height];
            int i = 0;
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++) px[i++] = c.Get(x, y);
            return px;
        }
    }
}
