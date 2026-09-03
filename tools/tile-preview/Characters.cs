using System;
using System.IO;
using UnityEngine;
using SheepGate.Art;

/// <summary>
/// The characters, at the size they are actually seen — for the one judgement no gate can make.
///
/// Every body in the game is drawn in C# from a build and a skin tone (CharacterArt.Body), and
/// the four tones have never been looked at side by side at 32×48: the e2e photographs them
/// inside a 1080×1920 village and the phone shows them one at a time. The top strip here is the
/// eight bodies at 1x, which is the honest size; below it, the same bodies at 4x, bare and
/// dressed in the first variant of each garment, facing front, side and back, and one walking
/// frame — enough to tell whether a silhouette reads as a person and whether the four tones read
/// as four people rather than as one person in four exposures.
///
/// It renders; it does not judge. What it produces is the sheet to put in front of somebody.
/// </summary>
public static class Characters
{
    const int W = CharacterArt.Width;
    const int H = CharacterArt.Height;
    static int _w, _h;
    static byte[] _out;

    static void Blit(PixelCanvas c, int tx, int ty, int scale)
    {
        Color32[] px = c.ToArray();      // Unity texture order: bottom-up
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            Color32 p = px[(H - 1 - y) * W + x];
            if (p.a == 0) continue;      // layers: a transparent pixel leaves what is under it
            for (int sy = 0; sy < scale; sy++)
            for (int sx = 0; sx < scale; sx++)
            {
                int a = tx + x * scale + sx, b = ty + y * scale + sy;
                if (a < 0 || b < 0 || a >= _w || b >= _h) continue;
                int i = (b * _w + a) * 4;
                _out[i] = p.r; _out[i + 1] = p.g; _out[i + 2] = p.b; _out[i + 3] = 255;
            }
        }
    }

    static void Bare(int variant, ArtFacing facing, ArtAnim anim, int frame, int tx, int ty, int scale)
    {
        Blit(CharacterArt.Body(variant, facing, anim, frame), tx, ty, scale);
    }

    static void Dressed(int variant, ArtFacing facing, ArtAnim anim, int frame, int tx, int ty, int scale)
    {
        // The order the game layers them (CharacterAppearance: Body, Legs, Top, Accessory, Hair),
        // minus the accessory — what is being judged is the body, and a band or a hood would only
        // hide it. First variant of each garment, so what differs between rows is only the body.
        Blit(CharacterArt.Body(variant, facing, anim, frame), tx, ty, scale);
        Blit(CharacterArt.Legs(0, facing), tx, ty, scale);
        Blit(CharacterArt.Top(0, facing), tx, ty, scale);
        Blit(CharacterArt.Hair(0, facing), tx, ty, scale);
    }

    static void Fill(int x0, int y0, int w, int h, byte r, byte g, byte b)
    {
        for (int y = y0; y < y0 + h && y < _h; y++)
        for (int x = x0; x < x0 + w && x < _w; x++)
        {
            int i = (y * _w + x) * 4;
            _out[i] = r; _out[i + 1] = g; _out[i + 2] = b; _out[i + 3] = 255;
        }
    }

    public static int Main(string[] args)
    {
        const int scale = 4, gap = 8;
        int variants = CharacterArt.BodyVariants;
        int cellW = W * scale + gap, cellH = H * scale + gap;
        int columns = 6;                       // bare front / side / back · dressed front / side / walk
        int strip = H + gap * 2;               // the 1x strip on top

        _w = columns * cellW + gap;
        _h = strip + variants * cellH + gap;
        _out = new byte[_w * _h * 4];

        // Two grounds, the game's own: the village ground so the tones are judged where they are
        // seen, and a plain dark band under the 1x strip so the true-size row has nothing busy
        // behind it.
        Fill(0, 0, _w, _h, 20, 20, 24);
        PixelCanvas ground = TileArt.Ground(ValueNoise.SeedFrom("tile_ground"));
        Color32[] gpx = ground.ToArray();
        for (int y = strip; y < _h; y++)
        for (int x = 0; x < _w; x++)
        {
            Color32 p = gpx[((32 - 1 - ((y / scale) % 32)) * 32) + ((x / scale) % 32)];
            int i = (y * _w + x) * 4;
            _out[i] = p.r; _out[i + 1] = p.g; _out[i + 2] = p.b; _out[i + 3] = 255;
        }

        // 1x: the honest size. Bare, front, idle — the eight bodies in a row, each tone twice.
        for (int v = 0; v < variants; v++)
        {
            Bare(v, ArtFacing.Down, ArtAnim.Idle, 0, gap + v * (W + gap), gap, 1);
        }

        // 4x: one row per body variant. Tone = v % SkinCount, build = v / SkinCount.
        for (int v = 0; v < variants; v++)
        {
            int ty = strip + v * cellH;
            Bare(v, ArtFacing.Down,  ArtAnim.Idle, 0, gap + 0 * cellW, ty, scale);
            Bare(v, ArtFacing.Right, ArtAnim.Idle, 0, gap + 1 * cellW, ty, scale);
            Bare(v, ArtFacing.Up,    ArtAnim.Idle, 0, gap + 2 * cellW, ty, scale);
            Dressed(v, ArtFacing.Down,  ArtAnim.Idle, 0, gap + 3 * cellW, ty, scale);
            Dressed(v, ArtFacing.Right, ArtAnim.Idle, 0, gap + 4 * cellW, ty, scale);
            Dressed(v, ArtFacing.Down,  ArtAnim.Walk, 1, gap + 5 * cellW, ty, scale);
        }

        File.WriteAllBytes(args[0], _out);
        Console.WriteLine(_w + " " + _h);
        return 0;
    }
}
