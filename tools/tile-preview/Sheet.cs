using System;
using System.IO;
using UnityEngine;
using SheepGate.Art;

public static class Sheet
{
    const int S = 32;
    const int Scale = 3;

    static int _w, _h;
    static byte[] _out;

    static int SeedFor(string key) { return ValueNoise.SeedFrom(key); }

    static PixelCanvas Fallen(int v) { return TileArt.FallenWall(SeedFor(v == 0 ? "tile_fallen_wall" : "tile_fallen_wall_" + v)); }
    static PixelCanvas GroundV(int v) { return TileArt.Ground(SeedFor(v == 0 ? "tile_ground" : "tile_ground_" + v)); }
    static PixelCanvas Rub() { return TileArt.RubbleTile(SeedFor("tile_rubble")); }

    static void Blit(PixelCanvas c, int tx, int ty)
    {
        Color32[] px = c.ToArray();      // Unity texture order: bottom-up
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Color32 p = px[(S - 1 - y) * S + x];
                for (int sy = 0; sy < Scale; sy++)
                for (int sx = 0; sx < Scale; sx++)
                {
                    int px2 = tx + x * Scale + sx, py2 = ty + y * Scale + sy;
                    if (px2 < 0 || py2 < 0 || px2 >= _w || py2 >= _h) continue;
                    int i = (py2 * _w + px2) * 4;
                    _out[i] = p.r; _out[i + 1] = p.g; _out[i + 2] = p.b; _out[i + 3] = 255;
                }
            }
        }
    }

    static int VariantAt(int x, int y)
    {
        int hash = x * 40503 ^ y * 30011;
        return Math.Abs(hash) % 8;
    }
    static int GroundAt(int x, int y)
    {
        int hash = x * 73856093 ^ y * 19349663;
        return Math.Abs(hash) % 6;
    }
    static bool IsFallen(int x, int y, float density)
    {
        return ValueNoise.Cell(12345, x, y) < density;
    }

    public static int Main(string[] args)
    {
        int T = S * Scale;
        int cols = 8;
        // rows: 1 strip row (variants+ground+rubble), gap, 6x6 mixed, gap, 6x6 dense, 6x6 rubble-today
        _w = Math.Max(cols * T + 7 * 6, 6 * T * 2 + 24);
        _h = T + 20 + 6 * T + 20 + 6 * T;
        _out = new byte[_w * _h * 4];
        for (int i = 0; i < _w * _h; i++) { _out[i * 4] = 20; _out[i * 4 + 1] = 20; _out[i * 4 + 2] = 24; _out[i * 4 + 3] = 255; }

        // Row 1: eight variants, spaced, then ground and today's rubble tile.
        for (int v = 0; v < 8; v++) Blit(Fallen(v), v * (T + 6), 0);

        int y0 = T + 20;
        // Left: 50% field. Right: 100% field (worst case).
        for (int gy = 0; gy < 6; gy++)
        for (int gx = 0; gx < 6; gx++)
        {
            PixelCanvas c = IsFallen(gx, gy, 0.5f) ? Fallen(VariantAt(gx, gy)) : GroundV(GroundAt(gx, gy));
            Blit(c, gx * T, y0 + gy * T);
            Blit(Fallen(VariantAt(gx + 11, gy + 7)), 6 * T + 24 + gx * T, y0 + gy * T);
        }

        int y1 = y0 + 6 * T + 20;
        // Bottom left: today's void band (rubble tile scattered), for comparison.
        // Bottom right: plain ground, the control.
        for (int gy = 0; gy < 6; gy++)
        for (int gx = 0; gx < 6; gx++)
        {
            PixelCanvas c = IsFallen(gx, gy, 0.5f) ? Rub() : GroundV(GroundAt(gx, gy));
            Blit(c, gx * T, y1 + gy * T);
            Blit(GroundV(GroundAt(gx, gy)), 6 * T + 24 + gx * T, y1 + gy * T);
        }

        File.WriteAllBytes(args[0], _out);
        Console.WriteLine(_w + " " + _h);
        return 0;
    }
}
