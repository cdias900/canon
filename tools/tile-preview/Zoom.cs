using System;
using System.IO;
using UnityEngine;
using SheepGate.Art;

public static class Zoom
{
    const int S = 32;
    static int _w, _h;
    static byte[] _out;

    static void Blit(PixelCanvas c, int tx, int ty, int scale)
    {
        Color32[] px = c.ToArray();
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            Color32 p = px[(S - 1 - y) * S + x];
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

    static int Sd(string k) { return ValueNoise.SeedFrom(k); }
    static PixelCanvas F(int v) { return TileArt.FallenWall(Sd(v == 0 ? "tile_fallen_wall" : "tile_fallen_wall_" + v)); }

    public static int Main(string[] args)
    {
        int sc = 7, T = S * sc, gap = 8;
        _w = 6 * (T + gap);
        _h = 3 * (T + gap);
        _out = new byte[_w * _h * 4];
        for (int i = 0; i < _w * _h; i++) { _out[i*4]=20;_out[i*4+1]=20;_out[i*4+2]=24;_out[i*4+3]=255; }

        for (int v = 0; v < 6; v++) Blit(F(v), v * (T + gap), 0, sc);
        for (int v = 6; v < 8; v++) Blit(F(v), (v - 6) * (T + gap), T + gap, sc);
        // Reference: the wall this stone came off, and the in-city rubble it must not look like.
        Blit(TileArt.Wall(2, Sd("wall_2")), 2 * (T + gap), T + gap, sc);
        Blit(TileArt.Wall(3, Sd("wall_3")), 3 * (T + gap), T + gap, sc);
        Blit(TileArt.RubbleTile(Sd("tile_rubble")), 4 * (T + gap), T + gap, sc);
        Blit(TileArt.Ground(Sd("tile_ground")), 5 * (T + gap), T + gap, sc);

        // A 2x2 of one variant against itself: the self-seam test.
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            Blit(F(3), x * T, 2 * (T + gap) + y * (T / 2), sc);

        File.WriteAllBytes(args[0], _out);
        Console.WriteLine(_w + " " + _h);
        return 0;
    }
}
