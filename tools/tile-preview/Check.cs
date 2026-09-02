using System;
using System.Collections.Generic;
using UnityEngine;
using SheepGate.Art;

public static class Check
{
    public static int Main(string[] args)
    {
        // Every exact palette colour, plus SoftShade composited once over each of them.
        var allowed = new HashSet<int>();
        Color32[] baseColours = {
            ArtPalette.Ink, ArtPalette.Shadow, ArtPalette.Neutral, ArtPalette.Light, ArtPalette.Paper,
            ArtPalette.StoneDeep, ArtPalette.StoneDark, ArtPalette.StoneMid, ArtPalette.StoneLight, ArtPalette.StonePale,
            ArtPalette.ClayDeep, ArtPalette.ClayDark, ArtPalette.ClayMid, ArtPalette.ClayLight, ArtPalette.ClayPale,
            ArtPalette.TealDeep, ArtPalette.TealDark, ArtPalette.TealMid, ArtPalette.TealLight, ArtPalette.TealPale };
        foreach (var c in baseColours)
        {
            allowed.Add(Key(c));
            var canvas = new PixelCanvas(1, 1);
            canvas.Set(0, 0, c);
            canvas.Blend(0, 0, ArtPalette.SoftShade);
            allowed.Add(Key(canvas.ToArray()[0]));
        }

        int bad = 0, transparent = 0;
        var offenders = new HashSet<int>();
        for (int v = 0; v < ArtKeysCount; v++)
        {
            string key = v == 0 ? "tile_fallen_wall" : "tile_fallen_wall_" + v;
            Color32[] px = TileArt.FallenWall(ValueNoise.SeedFrom(key)).ToArray();
            foreach (var p in px)
            {
                if (p.a != 255) transparent++;
                if (!allowed.Contains(Key(p))) { bad++; offenders.Add(Key(p)); }
            }
        }
        Console.WriteLine("variants=" + ArtKeysCount + "  off-palette pixels=" + bad
            + "  distinct offenders=" + offenders.Count + "  non-opaque pixels=" + transparent);
        foreach (var o in offenders) Console.WriteLine("  offender rgba=" + (o >> 24 & 255) + "," + (o >> 16 & 255) + "," + (o >> 8 & 255) + "," + (o & 255));
        return bad == 0 && transparent == 0 ? 0 : 1;
    }

    const int ArtKeysCount = 12;
    static int Key(Color32 c) { return (c.r << 24) | (c.g << 16) | (c.b << 8) | c.a; }
}
