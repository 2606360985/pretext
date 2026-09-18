// Text measurement using TextMesh Pro FontAsset.
// Part of the Unity integration layer.

#if UNITY_2019_3_OR_NEWER
#define TMP_PACKAGE
#endif

#if TMP_PACKAGE
using UnityEngine;
using TMPro;

namespace Pretext.Integration
{
    /// <summary>
    /// Text width measurer using TextMesh Pro's FontAsset.
    /// Provides accurate glyph width measurement for PretextCore.
    /// </summary>
    public sealed class TMPFontMeasurer : Pretext.IFontMeasurer
    {
        private readonly TMP_FontAsset _fontAsset;
        private readonly float _fontSize;
        private readonly float _fontScale;

        // Cache: text → total width
        private readonly System.Collections.Generic.Dictionary<string, float> _widthCache = new();
        // Cache: text → per-grapheme widths
        private readonly System.Collections.Generic.Dictionary<string, float[]> _graphemeCache = new();

        /// <summary>
        /// Create a measurer for the given TMP FontAsset at the specified font size.
        /// </summary>
        /// <param name="fontAsset">The TMP_FontAsset to measure with.</param>
        /// <param name="fontSize">Font size in pixels.</param>
        public TMPFontMeasurer(TMP_FontAsset fontAsset, float fontSize)
        {
            _fontAsset = fontAsset ?? throw new System.ArgumentNullException(nameof(fontAsset));
            _fontSize = fontSize;
            // TMP internal scale: font asset uses font size internally
            // We need to compute the scale factor for point size conversion
            _fontScale = fontSize / _fontAsset.fontInfo.PointSize;
        }

        /// <inheritdoc />
        public float MeasureWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            if (_widthCache.TryGetValue(text, out float cached)) return cached;

            float totalWidth = 0;
            for (int i = 0; i < text.Length;)
            {
                float charWidth = GetCharacterWidth(text, ref i);
                totalWidth += charWidth;
            }

            _widthCache[text] = totalWidth;
            return totalWidth;
        }

        /// <inheritdoc />
        public float[]? MeasureGraphemeWidths(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            if (_graphemeCache.TryGetValue(text, out float[]? cached)) return cached;

            int[] graphemeBreaks = Pretext.TextAnalyzer.Segmenter.GetGraphemeBreaks(text);
            var widths = new float[System.Math.Max(0, graphemeBreaks.Length - 1)];

            for (int gi = 0; gi < widths.Length; gi++)
            {
                int start = graphemeBreaks[gi];
                int end = graphemeBreaks[gi + 1];
                string grapheme = text.Substring(start, end - start);
                widths[gi] = MeasureGraphemeWidth(grapheme);
            }

            _graphemeCache[text] = widths;
            return widths;
        }

        /// <summary>
        /// Measure the width of a single grapheme (may be multiple Unicode codepoints).
        /// </summary>
        public float MeasureGraphemeWidth(string grapheme)
        {
            float width = 0;
            for (int i = 0; i < grapheme.Length;)
            {
                width += GetCharacterWidth(grapheme, ref i);
            }
            return width;
        }

        /// <summary>
        /// Get the advance width of the next character in the string.
        /// Handles surrogate pairs and combines with variation selectors.
        /// Advances i past the processed codepoints.
        /// </summary>
        private float GetCharacterWidth(string text, ref int i)
        {
            if (i >= text.Length) return 0;

            int cp = char.ConvertToUtf32(text, i);
            int charLen = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;

            // Check for variation selector (U+FE0F = emoji presentation, U+FE0E = text presentation)
            bool hasVariationSelector = false;
            if (i + charLen < text.Length)
            {
                int nextCp = char.ConvertToUtf32(text, i + charLen);
                if (nextCp == 0xFE0F || nextCp == 0xFE0E)
                {
                    hasVariationSelector = true;
                }
            }

            float width = GetGlyphAdvance(cp);

            // If this is an emoji or has variation selector, also include variation selector width
            if (hasVariationSelector)
            {
                int vsCp = char.ConvertToUtf32(text, i + charLen);
                width += GetGlyphAdvance(vsCp);
                charLen += char.IsHighSurrogate(text[i + charLen]) ? 2 : 1;
            }

            i += charLen;
            return width;
        }

        /// <summary>
        /// Get the advance width of a character from the TMP FontAsset.
        /// </summary>
        private float GetGlyphAdvance(int unicode)
        {
            if (_fontAsset.characterTable.TrySearchForCharacterIndex(unicode, out TMP_Character character))
            {
                // GlyphMetrics: xAdvance is already the advance in font units
                // Scale by fontSize / pointSize to get pixel width
                return character.glyph.metrics.horizontalAdvance * _fontScale;
            }

            // Character not found in font: use a fallback
            // For ASCII, use a heuristic based on average character width
            if (unicode < 0x80)
            {
                // Approximate ASCII width: 0.5 * fontSize
                return _fontSize * 0.5f;
            }

            // Unknown character: use font size as rough estimate
            return _fontSize;
        }

        /// <summary>
        /// Clear measurement caches. Call when font or size changes.
        /// </summary>
        public void ClearCache()
        {
            _widthCache.Clear();
            _graphemeCache.Clear();
        }
    }

    /// <summary>
    /// Simpler measurer using Unity's built-in Font class (non-TMP).
    /// Less accurate but works without TextMeshPro.
    /// </summary>
    public sealed class BuiltinFontMeasurer : Pretext.IFontMeasurer
    {
        private readonly Font _font;
        private readonly int _fontSize;

        public BuiltinFontMeasurer(Font font, int fontSize)
        {
            _font = font ?? throw new System.ArgumentNullException(nameof(font));
            _fontSize = fontSize;
        }

        public float MeasureWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            CharacterInfo info;
            float totalWidth = 0;

            foreach (char ch in text)
            {
                if (_font.GetCharacterInfo(ch, out info, _fontSize))
                {
                    totalWidth += info.advance;
                }
                else
                {
                    // Fallback: estimate
                    totalWidth += _fontSize * 0.5f;
                }
            }

            return totalWidth;
        }

        public float[]? MeasureGraphemeWidths(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            int[] breaks = Pretext.TextAnalyzer.Segmenter.GetGraphemeBreaks(text);
            var widths = new float[System.Math.Max(0, breaks.Length - 1)];

            for (int gi = 0; gi < widths.Length; gi++)
            {
                int start = breaks[gi];
                int end = breaks[gi + 1];
                string grapheme = text.Substring(start, end - start);
                widths[gi] = MeasureWidth(grapheme);
            }

            return widths;
        }
    }
}
#endif // TMP_PACKAGE
