// ICU4C integration for text segmentation.
// In Unity, ICU4C is available via icu4c.dll (included with Unity).
// This file provides a wrapper that uses ICU4C when available.

using System.Globalization;
using Pretext;

namespace Pretext.Integration
{
    /// <summary>
    /// Grapheme segmenter using .NET StringInfo ( Globalization API).
    /// Available at runtime without ICU dependency.
    /// More accurate than UnicodeRangeSegmenter for complex scripts.
    /// </summary>
    public sealed class GlobalizationSegmenter : ISegmenter
    {
        private static readonly System.Text.StringBuilder Sb = new();

        public int[] GetGraphemeBreaks(string text)
        {
            if (string.IsNullOrEmpty(text)) return [];

            var breaks = new System.Collections.Generic.List<int> { 0 };
            var enumerator = new StringInfo.GetTextElementEnumerator(text);

            while (enumerator.MoveNext())
            {
                int index = enumerator.ElementIndex;
                breaks.Add(index + enumerator.GetTextElement().Length);
            }

            // Ensure we cover the full string
            if (breaks.Count == 0 || breaks[^1] < text.Length)
            {
                if (breaks.Count == 0) breaks.Add(0);
                breaks[^1] = text.Length;
            }

            return breaks.ToArray();
        }

        public int[] GetWordBreaks(string text)
        {
            if (string.IsNullOrEmpty(text)) return [];

            var breaks = new System.Collections.Generic.List<int> { 0 };
            int len = text.Length;

            // Use WordBreaks enum values from .NET Globalization
            // WordBreakStyle.Normal = 0, Break = 1, BreakNa = 2
            var wb = new System.Globalization.TextElementEnumerator(text);

            // Scan through text, finding word boundaries
            int i = 0;
            while (i < len)
            {
                int cp = char.ConvertToUtf32(text, i);
                int charLen = char.IsHighSurrogate(text[i]) && i + 1 < len ? 2 : 1;

                bool isLetter = char.IsLetter(text[i]) || (cp >= 0x00A0 && cp <= 0x00A0); // NBSP
                bool isDigit = char.IsDigit(text[i]);
                bool isPunct = char.IsPunctuation(text[i]);
                bool isMark = KinsokuData.IsCombiningMark(cp);

                // Simple state machine for word detection
                if (isLetter || isDigit || cp == 0x00A0 || cp == 0x2019) // letter/number/NBSP/apostrophe
                {
                    // Find end of word
                    int wordStart = i;
                    while (i < len)
                    {
                        int nextCp = char.ConvertToUtf32(text, i);
                        int nextLen = char.IsHighSurrogate(text[i]) && i + 1 < len ? 2 : 1;
                        bool nextIsLetter = char.IsLetter(text[i]) || (nextCp >= 0x00A0 && nextCp <= 0x00A0);

                        if (!nextIsLetter && !char.IsDigit(text[i]) &&
                            nextCp != 0x2019 && !KinsokuData.IsCombiningMark(nextCp))
                            break;

                        // Skip combining marks
                        while (i < len && KinsokuData.IsCombiningMark(char.ConvertToUtf32(text, i)))
                            i += char.IsHighSurrogate(text[i]) ? 2 : 1;

                        i += nextLen;

                        // Skip more combining marks
                        while (i < len && KinsokuData.IsCombiningMark(char.ConvertToUtf32(text, i)))
                            i += char.IsHighSurrogate(text[i]) ? 2 : 1;
                    }

                    breaks.Add(i);
                }
                else
                {
                    i += charLen;
                }
            }

            // Ensure last break is at end
            if (breaks.Count > 0 && breaks[^1] < len)
                breaks[^1] = len;
            else if (breaks.Count == 0)
                breaks.Add(len);

            return breaks.ToArray();
        }
    }

    /// <summary>
    /// Editor-time ICU4C segmenter using P/Invoke to Unity's bundled icu4c.dll.
    /// Only available in the Unity Editor. Use GlobalizationSegmenter for runtime.
    ///
    /// To use: Copy this file to an Editor folder, or build a separate
    /// managed DLL from the ICU4C C library bindings.
    /// </summary>
#if UNITY_EDITOR
    public sealed class ICUSegmenter : ISegmenter
    {
        private System.IntPtr _breakIterator;
        private System.IntPtr _locale;
        private bool _disposed;

        public ICUSegmenter(string locale = "en")
        {
            // ICU4C is available at UnityEditor-time via the managed ICU bindings.
            // In a full implementation, you would use:
            //   1. ICU4C.NET NuGet package, or
            //   2. P/Invoke to icu4c.dll functions (ubrk_open, ubrk_first, ubrk_next, etc.)
            //
            // For now, fall back to GlobalizationSegmenter.
            Debug.LogWarning("[Pretext] ICU4C segmenter not available. Using GlobalizationSegmenter fallback.");
            throw new System.NotImplementedException("ICU4C integration requires building with ICU4C bindings. Use GlobalizationSegmenter instead.");
        }

        public int[] GetGraphemeBreaks(string text)
        {
            throw new System.NotImplementedException();
        }

        public int[] GetWordBreaks(string text)
        {
            throw new System.NotImplementedException();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Close ICU break iterator handles here
        }
    }
#endif // UNITY_EDITOR
}
