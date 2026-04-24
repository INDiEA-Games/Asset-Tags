// OKLab / OKLCH conversion math source:
// - Bjorn Ottosson, "A perceptual color space for image processing"
// - https://bottosson.github.io/posts/oklab/
//
// License notice:
// - Conversion formulas are based on the reference implementation published by Bjorn Ottosson.
// - The reference implementation is released under the MIT License.

using UnityEngine;

namespace INDiEA.AssetTags
{
    public static class OklabUtilities
    {
        public static Color OklchToSrgb(float lightness, float chroma, float hueDegrees, float alpha = 1f)
        {
            var hueRadians = hueDegrees * Mathf.Deg2Rad;
            var a = chroma * Mathf.Cos(hueRadians);
            var b = chroma * Mathf.Sin(hueRadians);
            return OklabToSrgb(lightness, a, b, alpha);
        }

        public static Color OklabToSrgb(float lightness, float a, float b, float alpha = 1f)
        {
            var l_ = lightness + 0.3963377774f * a + 0.2158037573f * b;
            var m_ = lightness - 0.1055613458f * a - 0.0638541728f * b;
            var s_ = lightness - 0.0894841775f * a - 1.2914855480f * b;

            var l = l_ * l_ * l_;
            var m = m_ * m_ * m_;
            var s = s_ * s_ * s_;

            var linearR = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
            var linearG = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
            var linearB = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

            var srgbR = LinearToSrgb(linearR);
            var srgbG = LinearToSrgb(linearG);
            var srgbB = LinearToSrgb(linearB);

            return new Color(
                Mathf.Clamp01(srgbR),
                Mathf.Clamp01(srgbG),
                Mathf.Clamp01(srgbB),
                Mathf.Clamp01(alpha));
        }

        public static bool IsInSrgbGamut(float lightness, float chroma, float hueDegrees)
        {
            var hueRadians = hueDegrees * Mathf.Deg2Rad;
            var a = chroma * Mathf.Cos(hueRadians);
            var b = chroma * Mathf.Sin(hueRadians);

            var l_ = lightness + 0.3963377774f * a + 0.2158037573f * b;
            var m_ = lightness - 0.1055613458f * a - 0.0638541728f * b;
            var s_ = lightness - 0.0894841775f * a - 1.2914855480f * b;

            var l = l_ * l_ * l_;
            var m = m_ * m_ * m_;
            var s = s_ * s_ * s_;

            var linearR = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
            var linearG = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
            var linearB = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

            return linearR >= 0f && linearR <= 1f
                && linearG >= 0f && linearG <= 1f
                && linearB >= 0f && linearB <= 1f;
        }

        public static float MaxChromaInSrgbGamut(float lightness, float hueDegrees)
        {
            const float cap = 0.48f;
            if (!IsInSrgbGamut(lightness, 1e-7f, hueDegrees))
                return 0f;

            if (IsInSrgbGamut(lightness, cap, hueDegrees))
                return cap;

            var lo = 0f;
            var hi = cap;
            for (var i = 0; i < 32; i++)
            {
                var mid = (lo + hi) * 0.5f;
                if (IsInSrgbGamut(lightness, mid, hueDegrees))
                    lo = mid;
                else
                    hi = mid;
            }

            return lo;
        }

        public static Color OklchToSrgbClamped(float lightness, float chroma, float hueDegrees, float alpha = 1f)
        {
            var c = Mathf.Max(0f, chroma);
            var maxC = MaxChromaInSrgbGamut(lightness, hueDegrees);
            var mapped = Mathf.Min(c, maxC);
            return OklchToSrgb(lightness, mapped, hueDegrees, alpha);
        }

        static float LinearToSrgb(float linear)
        {
            if (linear <= 0f)
                return 0f;
            if (linear >= 1f)
                return 1f;
            return linear <= 0.0031308f
                ? 12.92f * linear
                : 1.055f * Mathf.Pow(linear, 1f / 2.4f) - 0.055f;
        }
    }
}
