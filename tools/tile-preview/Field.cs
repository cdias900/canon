using System;
using System.IO;
using UnityEngine;
using SheepGate.Art;

public static class Field
{
    const int S = 32;
    const int N = 16;                 // 16x16 cells of 32px = 512x512
    static int _w = S * N, _h = S * N;
    static byte[] _out;

    static void Blit(PixelCanvas c, int tx, int ty)
    {
        Color32[] px = c.ToArray();
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            Color32 p = px[(S - 1 - y) * S + x];
            int i = ((ty + y) * _w + tx + x) * 4;
            _out[i] = p.r; _out[i+1] = p.g; _out[i+2] = p.b; _out[i+3] = 255;
        }
    }
    static int Sd(string k) { return ValueNoise.SeedFrom(k); }
    static PixelCanvas F(int v) { return TileArt.FallenWall(Sd(v == 0 ? "tile_fallen_wall" : "tile_fallen_wall_" + v)); }
    static PixelCanvas G(int v) { return TileArt.Ground(Sd(v == 0 ? "tile_ground" : "tile_ground_" + v)); }
    static PixelCanvas R() { return TileArt.RubbleTile(Sd("tile_rubble")); }

    public static int Main(string[] args)
    {
        float density = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
        bool old = args.Length > 2 && args[2] == "old";
        _out = new byte[_w * _h * 4];
        for (int gy = 0; gy < N; gy++)
        for (int gx = 0; gx < N; gx++)
        {
            bool hit = ValueNoise.Cell(4242, gx, gy) < density;
            int gv = Math.Abs(gx * 73856093 ^ gy * 19349663) % 6;
            int fv = Math.Abs(gx * 40503 ^ gy * 30011) % 12;
            Blit(hit ? (old ? R() : F(fv)) : G(gv), gx * S, gy * S);
        }
        File.WriteAllBytes(args[0], _out);
        Console.WriteLine(_w + " " + _h);
        return 0;
    }
}
