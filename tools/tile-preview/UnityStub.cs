// Minimal UnityEngine surface so the real Art files compile outside Unity.
using System;

namespace UnityEngine
{
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) : this(r, g, b, 1f) { }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero { get { return new Vector4(0, 0, 0, 0); } }
    }

    public static class Mathf
    {
        public const float Deg2Rad = 0.0174532924f;
        public static int Abs(int v) { return Math.Abs(v); }
        public static float Abs(float v) { return Math.Abs(v); }
        public static int CeilToInt(float v) { return (int)Math.Ceiling(v); }
        public static int FloorToInt(float v) { return (int)Math.Floor(v); }
        public static int RoundToInt(float v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Sin(float v) { return (float)Math.Sin(v); }
        public static float Cos(float v) { return (float)Math.Cos(v); }
    }

    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Point, Bilinear }
    public enum TextureWrapMode { Clamp }

    public class Texture2D
    {
        public string name;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Texture2D(int w, int h, TextureFormat f, bool mips) { }
        public void SetPixels32(Color32[] p) { }
        public void Apply(bool a, bool b) { }
    }
}
