using System.Collections.Generic;
using UnityEngine;

namespace INDiEA.AssetTags
{
    public static class AssetTagsColorUtilities
    {
        const float MinContrastWithWhite = 2f;
        const int LightnessSamples = 40;
        const float LightnessMin = 0.16f;
        const float LightnessMax = 0.78f;
        const int CandidateSamples = 24;

        public static Color GenerateTagColor(IEnumerable<Color> colorsToAvoid = null)
        {
            var avoid = CollectColors(colorsToAvoid);
            if (avoid.Count == 0)
                return GenerateReadableColor(Random.Range(0f, 360f));

            var best = new Color(0.12f, 0.14f, 0.22f, 1f);
            var bestScore = float.NegativeInfinity;
            var startHue = Random.Range(0f, 360f);
            for (var i = 0; i < CandidateSamples; i++)
            {
                var hue = Mathf.Repeat(startHue + i * 137.507764f, 360f);
                var color = GenerateReadableColor(hue);
                var score = DistanceToClosestColor(color, avoid);
                if (score <= bestScore)
                    continue;
                bestScore = score;
                best = color;
            }

            return best;
        }

        static Color GenerateReadableColor(float hue)
        {
            var best = new Color(0.12f, 0.14f, 0.22f, 1f);
            var bestScore = float.NegativeInfinity;

            for (var i = 0; i < LightnessSamples; i++)
            {
                var t = LightnessSamples <= 1 ? 0f : i / (float)(LightnessSamples - 1);
                var lightness = Mathf.Lerp(LightnessMin, LightnessMax, t);
                var chroma = OklabUtilities.MaxChromaInSrgbGamut(lightness, hue);
                if (chroma < 1e-5f)
                    continue;

                var color = OklabUtilities.OklchToSrgb(lightness, chroma, hue);
                if (GetContrastRatio(Color.white, color) < MinContrastWithWhite)
                    continue;

                var score = lightness + chroma;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = color;
                }
            }

            if (bestScore > float.NegativeInfinity)
                return best;

            for (var lightness = LightnessMax; lightness >= 0.1f; lightness -= 0.035f)
            {
                var chroma = OklabUtilities.MaxChromaInSrgbGamut(lightness, hue);
                var color = OklabUtilities.OklchToSrgb(lightness, chroma, hue);
                if (GetContrastRatio(Color.white, color) >= MinContrastWithWhite)
                    return color;
            }

            return best;
        }

        static List<Color> CollectColors(IEnumerable<Color> colors)
        {
            var result = new List<Color>();
            if (colors == null)
                return result;
            foreach (var color in colors)
                result.Add(new Color(color.r, color.g, color.b, 1f));
            return result;
        }

        static float DistanceToClosestColor(Color candidate, List<Color> existing)
        {
            var closest = float.MaxValue;
            for (var i = 0; i < existing.Count; i++)
            {
                var color = existing[i];
                var dr = candidate.r - color.r;
                var dg = candidate.g - color.g;
                var db = candidate.b - color.b;
                var distance = dr * dr + dg * dg + db * db;
                if (distance < closest)
                    closest = distance;
            }

            return closest;
        }

        public static Color OklchToSrgbClamped(float lightness, float chroma, float hueDegrees, float alpha = 1f) =>
            OklabUtilities.OklchToSrgbClamped(lightness, chroma, hueDegrees, alpha);

        static float GetContrastRatio(Color a, Color b)
        {
            var l1 = GetRelativeLuminance(a);
            var l2 = GetRelativeLuminance(b);
            var bright = Mathf.Max(l1, l2);
            var dark = Mathf.Min(l1, l2);
            return (bright + 0.05f) / (dark + 0.05f);
        }

        static float GetRelativeLuminance(Color color)
        {
            var r = ToLinear(color.r);
            var g = ToLinear(color.g);
            var b = ToLinear(color.b);
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        static float ToLinear(float channel)
        {
            return channel <= 0.03928f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
    }
}
