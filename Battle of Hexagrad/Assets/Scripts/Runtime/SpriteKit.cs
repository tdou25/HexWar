using System;
using UnityEngine;

namespace HexWar.Play
{
    /// <summary>Procedurally generated sprites so the project needs zero imported art.</summary>
    public static class SpriteKit
    {
        public static Sprite Hex, Square, Circle, Ring, Diamond;
        static bool built;

        // With "Enter Play Mode Options > Reload Domain" off, statics survive but Unity destroys the
        // runtime-made textures/sprites on exit, so reset here and also null-check (Unity-aware) below.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { built = false; Hex = Square = Circle = Ring = Diamond = null; }

        public static void Build()
        {
            if (built && Hex != null && Square != null && Circle != null && Ring != null && Diamond != null) return;
            built = true;
            Hex = Make(256, 222, 128, (x, y) =>   // x,y in units of hex circumradius, centred
            {
                x = Mathf.Abs(x); y = Mathf.Abs(y);
                float m = Mathf.Max(y / 0.8660254f, (1.7320508f * x + y) / 1.7320508f);
                return Mathf.Clamp01((1f - m) * 40f);
            });
            Square = Make(4, 4, 4, (x, y) => 1f);
            Circle = Make(128, 128, 128, (x, y) => Mathf.Clamp01((1f - Mathf.Sqrt(x * x + y * y)) * 40f));
            Ring = Make(128, 128, 128, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                return Mathf.Clamp01((0.12f - Mathf.Abs(r - 0.88f)) * 40f);
            });
            Diamond = Make(128, 128, 128, (x, y) => Mathf.Clamp01((1f - (Mathf.Abs(x) + Mathf.Abs(y))) * 30f));
        }

        // fn gets coordinates in [-1,1] (x scaled so 1 == half texture *width*, y by the same pixel size)
        static Sprite Make(int w, int h, float ppu, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            float half = w * 0.5f;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = (i + 0.5f - w * 0.5f) / half, y = (j + 0.5f - h * 0.5f) / half;
                    px[j * w + i] = new Color(1, 1, 1, alpha(x, y));
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu);
        }
    }
}
