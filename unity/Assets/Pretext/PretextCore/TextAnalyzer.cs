// Text analysis: normalization, segmentation, punctuation merging, CJK kinsoku.
// Ported from src/analysis.ts — pure algorithmic, no browser APIs.
// Uses ISegmenter for grapheme/word boundary detection (injected, default: UnicodeRangeSegmenter).

namespace Pretext
{
    /// <summary>
    /// Result of text analysis: normalized text + parallel arrays of segment data.
    /// </summary>
    internal readonly struct TextAnalysis
    {
        public string Normalized { get; init; }
        public int Len { get; init; }
        public string[] Texts { get; init; }
        public bool[] IsWordLike { get; init; }
        public SegmentKind[] Kinds { get; init; }
        public int[] Starts { get; init; }
        public LineChunk[] Chunks { get; init; }

        public bool IsEmpty => Len == 0;
    }

    /// <summary>
    /// Analysis profile: engine-specific behavior flags.
    /// </summary>
    internal readonly struct AnalysisProfile
    {
        public bool CarryCJKAfterClosingQuote { get; init; }
    }

    /// <summary>
    /// Default segmenter using Unicode character ranges for grapheme/word boundaries.
    /// Works without ICU dependency. Less accurate than ICU4C BreakIterator but
    /// covers the common cases correctly.
    /// </summary>
    public sealed class UnicodeRangeSegmenter : ISegmenter
    {
        public int[] GetGraphemeBreaks(string text)
        {
            if (string.IsNullOrEmpty(text)) return [];

            int len = text.Length;
            var breaks = new System.Collections.Generic.List<int> { 0 };

            for (int i = 0; i < len;)
            {
                int cp = char.ConvertToUtf32(text, i);

                // Surrogate pair: next char is low surrogate
                if (i + 1 < len && char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                {
                    i += 2;
                    breaks.Add(i);
                    continue;
                }

                // Emoji presentation sequence: base + variation selector
                if (cp == 0xFE0F || cp == 0xFE1F)
                {
                    // Variation selector: break after
                    breaks.Add(i);
                    i++;
                    continue;
                }

                // Regional indicator: pair of letters
                if (cp >= 0x1F1E6 && cp <= 0x1F1FF)
                {
                    // Regional indicator: should pair with next RI
                    i++;
                    if (i < len)
                    {
                        int nextCp = char.ConvertToUtf32(text, i);
                        if (nextCp >= 0x1F1E6 && nextCp <= 0x1F1FF)
                        {
                            i++;
                        }
                    }
                    breaks.Add(i);
                    continue;
                }

                // Extended pictographic + ZWJ
                if (cp == 0x200D)
                {
                    // Zero-width joiner: no break here
                    i++;
                    continue;
                }

                // Combining enclosing mark
                if (KinsokuData.IsCombiningMark(cp))
                {
                    i++;
                    continue;
                }

                // Regular character: break after
                i++;
                breaks.Add(i);
            }

            return breaks.ToArray();
        }

        public int[] GetWordBreaks(string text)
        {
            if (string.IsNullOrEmpty(text)) return [];

            int len = text.Length;
            var breaks = new System.Collections.Generic.List<int> { 0 };

            int i = 0;
            while (i < len)
            {
                int cp = char.ConvertToUtf32(text, i);
                int charLen = char.IsHighSurrogate(text[i]) && i + 1 < len ? 2 : 1;

                // Skip non-word characters
                bool isWordChar = char.IsLetterOrDigit(text[i]) ||
                                  cp == 0x00A0 || // NBSP
                                  cp == 0x202F || // NNBSP
                                  cp == 0x2019;   // Right single quote (apostrophe)

                if (isWordChar)
                {
                    // Scan to end of word
                    int start = i;
                    while (i < len)
                    {
                        int nextCp = char.ConvertToUtf32(text, i);
                        int nextLen = char.IsHighSurrogate(text[i]) && i + 1 < len ? 2 : 1;

                        if (!char.IsLetterOrDigit(text[i]) &&
                            nextCp != 0x00A0 &&
                            nextCp != 0x202F &&
                            nextCp != 0x2019)
                            break;

                        i += nextLen;
                    }

                    // Handle surrogate pairs
                    while (i < len && char.IsHighSurrogate(text[i]) && i + 1 < len && char.IsLowSurrogate(text[i + 1]))
                        i += 2;
                    while (i < len && char.IsLowSurrogate(text[i]) && i > 0 && char.IsHighSurrogate(text[i - 1]))
                        i++;

                    if (i < len || start < len)
                        breaks.Add(System.Math.Min(i, len));
                }
                else
                {
                    i += charLen;
                    // Skip combining marks that follow
                    while (i < len && KinsokuData.IsCombiningMark(char.ConvertToUtf32(text, i)))
                        i += char.IsHighSurrogate(text[i]) && i + 1 < len ? 2 : 1;
                    if (i <= len)
                        breaks.Add(System.Math.Min(i, len));
                }
            }

            return breaks.ToArray();
        }
    }

    /// <summary>
    /// Text analyzer: normalizes whitespace, segments text, merges punctuation,
    /// applies CJK kinsoku rules, and prepares data for line breaking.
    /// </summary>
    internal static class TextAnalyzer
    {
        /// <summary>
        /// Global segmenter instance. Can be replaced for testing or ICU injection.
        /// </summary>
        public static ISegmenter Segmenter { get; set; } = new UnicodeRangeSegmenter();

        /// <summary>
        /// Normalize whitespace according to CSS white-space: normal behavior.
        /// Collapses [ \\t\\n\\r\\f]+ to single space, trims ends.
        /// </summary>
        public static string NormalizeWhitespaceNormal(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            bool needsNorm =
                text.IndexOfAny(['\t', '\n', '\r', '\f']) >= 0 ||
                text.Contains("  ", System.StringComparison.Ordinal) ||
                text[0] == ' ' ||
                text[text.Length - 1] == ' ';

            if (!needsNorm) return text;

            var sb = new System.Text.StringBuilder();
            bool lastWasSpace = false;

            for (int i = 0; i < text.Length;)
            {
                int cp = char.ConvertToUtf32(text, i);
                bool isWS = cp == ' ' || cp == '\t' || cp == '\n' || cp == '\r' || cp == '\f';
                int charLen = char.IsHighSurrogate(text[i]) ? 2 : 1;

                if (isWS)
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    if (lastWasSpace && sb.Length > 0) { /* already added space above */ }
                    sb.Append(text, i, charLen);
                    lastWasSpace = false;
                }

                i += charLen;
            }

            // Trim trailing space
            string result = sb.ToString();
            if (result.Length > 0 && result[0] == ' ')
                result = result[1..];
            if (result.Length > 0 && result[^1] == ' ')
                result = result[..^1];

            return result.Length == 0 ? text : result;
        }

        /// <summary>
        /// Normalize whitespace according to CSS white-space: pre-wrap behavior.
        /// Only converts CRLF to LF.
        /// </summary>
        public static string NormalizeWhitespacePreWrap(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>
        /// Classify a single character into its SegmentBreakKind.
        /// </summary>
        private static SegmentKind ClassifySegmentBreakChar(int cp, bool preserveOrdinarySpaces, bool preserveHardBreaks)
        {
            if (preserveOrdinarySpaces || preserveHardBreaks)
            {
                if (cp == ' ') return SegmentKind.PreservedSpace;
                if (cp == '\t') return SegmentKind.Tab;
                if (preserveHardBreaks && cp == '\n') return SegmentKind.HardBreak;
            }
            if (cp == ' ') return SegmentKind.Space;
            if (cp == 0x00A0 || cp == 0x202F || cp == 0x2060 || cp == 0xFEFF) return SegmentKind.Glue;
            if (cp == 0x200B) return SegmentKind.ZeroWidthBreak;
            if (cp == 0x00AD) return SegmentKind.SoftHyphen;
            return SegmentKind.Text;
        }

        /// <summary>
        /// Returns true if the segment ends with a closing quote character.
        /// </summary>
        private static bool EndsWithClosingQuote(string seg)
        {
            for (int i = seg.Length - 1; i >= 0; i--)
            {
                int cp = char.ConvertToUtf32(seg, i);
                if (KinsokuData.ClosingQuoteChars.Contains(cp)) return true;
                if (!KinsokuData.LeftStickyPunctuation.Contains(cp)) return false;
            }
            return false;
        }

        /// <summary>
        /// Check if a segment consists entirely of escaped quote characters (prefix backslashes + quote chars).
        /// </summary>
        private static bool IsEscapedQuoteClusterSegment(string seg)
        {
            bool sawQuote = false;
            for (int i = 0; i < seg.Length;)
            {
                int cp = char.ConvertToUtf32(seg, i);
                int charLen = char.IsHighSurrogate(seg[i]) ? 2 : 1;
                if (cp == '\\' || KinsokuData.IsCombiningMark(cp)) { i += charLen; continue; }
                if (KinsokuData.KinsokuEnd.Contains(cp) ||
                    KinsokuData.LeftStickyPunctuation.Contains(cp) ||
                    KinsokuData.ForwardStickyGlue.Contains(cp))
                {
                    sawQuote = true;
                    i += charLen;
                    continue;
                }
                return false;
            }
            return sawQuote;
        }

        /// <summary>
        /// Check if segment is forward-sticky (all forward-sticky or kinsoku-end chars).
        /// </summary>
        private static bool IsForwardStickyClusterSegment(string seg)
        {
            if (seg.Length == 0) return false;
            for (int i = 0; i < seg.Length;)
            {
                int cp = char.ConvertToUtf32(seg, i);
                int charLen = char.IsHighSurrogate(seg[i]) ? 2 : 1;
                if (!KinsokuData.KinsokuEnd.Contains(cp) &&
                    !KinsokuData.ForwardStickyGlue.Contains(cp) &&
                    !KinsokuData.IsCombiningMark(cp))
                    return false;
                i += charLen;
            }
            return true;
        }

        /// <summary>
        /// Check if a segment is left-sticky (ends with left-sticky punctuation).
        /// </summary>
        private static bool IsLeftStickyPunctuationSegment(string seg)
        {
            if (IsEscapedQuoteClusterSegment(seg)) return true;
            bool sawPunctuation = false;
            for (int i = 0; i < seg.Length;)
            {
                int cp = char.ConvertToUtf32(seg, i);
                int charLen = char.IsHighSurrogate(seg[i]) ? 2 : 1;
                if (KinsokuData.LeftStickyPunctuation.Contains(cp))
                {
                    sawPunctuation = true;
                    i += charLen;
                    continue;
                }
                if (KinsokuData.IsCombiningMark(cp)) { i += charLen; continue; }
                return false;
            }
            return sawPunctuation;
        }

        /// <summary>
        /// Check if a segment cannot start a CJK line.
        /// </summary>
        private static bool IsCJKLineStartProhibitedSegment(string seg)
        {
            if (seg.Length == 0) return false;
            for (int i = 0; i < seg.Length;)
            {
                int cp = char.ConvertToUtf32(seg, i);
                int charLen = char.IsHighSurrogate(seg[i]) ? 2 : 1;
                if (!KinsokuData.KinsokuStart.Contains(cp) && !KinsokuData.LeftStickyPunctuation.Contains(cp))
                    return false;
                i += charLen;
            }
            return true;
        }

        /// <summary>
        /// Split trailing forward-sticky cluster from the end of text.
        /// </summary>
        private static (string head, string tail)? SplitTrailingForwardStickyCluster(string text)
        {
            int splitIndex = text.Length;
            while (splitIndex > 0)
            {
                int prev = splitIndex - 1;
                // Handle surrogate pair
                if (char.IsLowSurrogate(text[prev]) && prev > 0 && char.IsHighSurrogate(text[prev - 1]))
                    prev--;
                int cp = char.ConvertToUtf32(text, prev);
                if (KinsokuData.IsCombiningMark(cp) || KinsokuData.KinsokuEnd.Contains(cp) || KinsokuData.ForwardStickyGlue.Contains(cp))
                {
                    splitIndex = prev;
                    continue;
                }
                break;
            }

            if (splitIndex <= 0 || splitIndex >= text.Length) return null;
            return (text[..splitIndex], text[splitIndex..]);
        }

        /// <summary>
        /// Returns true if segment contains a decimal digit.
        /// </summary>
        private static bool SegmentContainsDecimalDigit(string seg)
        {
            foreach (char ch in seg)
                if (char.IsDigit(ch)) return true;
            return false;
        }

        /// <summary>
        /// Returns true if segment is a numeric run (digits + joiner chars).
        /// </summary>
        private static readonly HashSet<int> NumericJoinerChars = new()
        {
            ':', '-', '/', '×', ',', '.', '+',
            0x2013, // en dash –
            0x2014, // em dash —
        };

        private static bool IsNumericRunSegment(string seg)
        {
            if (seg.Length == 0) return false;
            foreach (char ch in seg)
            {
                if (char.IsDigit(ch)) continue;
                if (NumericJoinerChars.Contains(ch)) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if segment contains Arabic script characters.
        /// </summary>
        private static bool ContainsArabicScript(string seg)
        {
            foreach (char ch in seg)
            {
                int cp = char.ConvertToUtf32(seg, 0);
                if ((cp >= 0x0600 && cp <= 0x06FF) || (cp >= 0x0750 && cp <= 0x077F) || (cp >= 0x08A0 && cp <= 0x08FF))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if segment ends with Arabic no-space punctuation.
        /// </summary>
        private static bool EndsWithArabicNoSpacePunctuation(string seg)
        {
            if (!ContainsArabicScript(seg) || seg.Length == 0) return false;
            int lastCp = char.ConvertToUtf32(seg, seg.Length - 1);
            return KinsokuData.ArabicNoSpaceTrailingPunctuation.Contains(lastCp);
        }

        /// <summary>
        /// Returns true if segment ends with Myanmar medial glue.
        /// </summary>
        private static bool EndsWithMyanmarMedialGlue(string seg)
        {
            if (seg.Length == 0) return false;
            int lastCp = char.ConvertToUtf32(seg, seg.Length - 1);
            return KinsokuData.MyanmarMedialGlue.Contains(lastCp);
        }

        /// <summary>
        /// Merge URL-like runs (scheme://... or www...) into single breakable units.
        /// </summary>
        private static string[] MergeUrlLikeRuns(string[] texts, bool[] isWordLike, SegmentKind[] kinds, int[] starts)
        {
            int len = texts.Length;
            for (int i = 0; i < len; i++)
            {
                string text = texts[i];
                if (kinds[i] != SegmentKind.Text) continue;

                bool isUrlStart = text.StartsWith("www.", System.StringComparison.Ordinal) ||
                                  (text.Length > 2 && text.Contains(':') && text.Contains("//"));

                if (!isUrlStart) continue;

                // URL scheme detection
                bool hasScheme = text.Contains("://", System.StringComparison.Ordinal);
                if (!hasScheme && !text.StartsWith("www.", System.StringComparison.Ordinal)) continue;

                int j = i + 1;
                while (j < len && !IsTextRunBoundary(kinds[j]))
                {
                    texts[i] += texts[j];
                    isWordLike[i] = true;
                    bool endsQuery = texts[j].Contains('?');
                    kinds[j] = SegmentKind.Text;
                    texts[j] = "";
                    j++;
                    if (endsQuery) break;
                }
            }

            return CompactArrays(texts, isWordLike, kinds, starts);
        }

        /// <summary>
        /// Merge URL query strings into single text segments.
        /// </summary>
        private static string[] MergeUrlQueryRuns(string[] texts, bool[] isWordLike, SegmentKind[] kinds, int[] starts)
        {
            int len = texts.Length;
            var resultTexts = new System.Collections.Generic.List<string>();
            var resultWordLike = new System.Collections.Generic.List<bool>();
            var resultKinds = new System.Collections.Generic.List<SegmentKind>();
            var resultStarts = new System.Collections.Generic.List<int>();

            for (int i = 0; i < len; i++)
            {
                resultTexts.Add(texts[i]);
                resultWordLike.Add(isWordLike[i]);
                resultKinds.Add(kinds[i]);
                resultStarts.Add(starts[i]);

                if (kinds[i] != SegmentKind.Text) continue;

                bool hasQuery = texts[i].Contains('?');
                bool hasUrl = texts[i].Contains("://", System.StringComparison.Ordinal) ||
                             texts[i].StartsWith("www.", System.StringComparison.Ordinal);

                if (!hasQuery || !hasUrl) continue;

                int nextIdx = i + 1;
                if (nextIdx >= len || IsTextRunBoundary(kinds[nextIdx])) continue;

                // Collect all non-boundary segments as query
                var queryParts = new System.Collections.Generic.List<string>();
                int queryStart = starts[nextIdx];
                int j = nextIdx;
                while (j < len && !IsTextRunBoundary(kinds[j]))
                {
                    queryParts.Add(texts[j]);
                    j++;
                }

                if (queryParts.Count > 0)
                {
                    resultTexts.Add(string.Join("", queryParts));
                    resultWordLike.Add(true);
                    resultKinds.Add(SegmentKind.Text);
                    resultStarts.Add(queryStart);
                    i = j - 1;
                }
            }

            return CompactArrays(
                resultTexts.ToArray(),
                resultWordLike.ToArray(),
                resultKinds.ToArray(),
                resultStarts.ToArray());
        }

        /// <summary>
        /// Merge numeric runs (e.g., "7:00" -> "7:00" as one segment).
        /// </summary>
        private static string[] MergeNumericRuns(string[] texts, bool[] isWordLike, SegmentKind[] kinds, int[] starts)
        {
            int len = texts.Length;
            var result = new System.Collections.Generic.List<string>();
            var resultWL = new System.Collections.Generic.List<bool>();
            var resultK = new System.Collections.Generic.List<SegmentKind>();
            var resultS = new System.Collections.Generic.List<int>();

            for (int i = 0; i < len; i++)
            {
                if (kinds[i] == SegmentKind.Text &&
                    IsNumericRunSegment(texts[i]) &&
                    SegmentContainsDecimalDigit(texts[i]))
                {
                    string merged = texts[i];
                    int start = starts[i];
                    int j = i + 1;
                    while (j < len &&
                           kinds[j] == SegmentKind.Text &&
                           IsNumericRunSegment(texts[j]))
                    {
                        merged += texts[j];
                        j++;
                    }

                    result.Add(merged);
                    resultWL.Add(true);
                    resultK.Add(SegmentKind.Text);
                    resultS.Add(start);
                    i = j - 1;
                    continue;
                }

                result.Add(texts[i]);
                resultWL.Add(isWordLike[i]);
                resultK.Add(kinds[i]);
                resultS.Add(starts[i]);
            }

            return CompactArrays(result.ToArray(), resultWL.ToArray(), resultK.ToArray(), resultS.ToArray());
        }

        /// <summary>
        /// Merge ASCII punctuation chains (e.g., "word,:" -> "word,:" as one breakable unit).
        /// </summary>
        private static string[] MergeAsciiPunctuationChains(string[] texts, bool[] isWordLike, SegmentKind[] kinds, int[] starts)
        {
            int len = texts.Length;
            var result = new System.Collections.Generic.List<string>();
            var resultWL = new System.Collections.Generic.List<bool>();
            var resultK = new System.Collections.Generic.List<SegmentKind>();
            var resultS = new System.Collections.Generic.List<int>();

            for (int i = 0; i < len; i++)
            {
                if (kinds[i] == SegmentKind.Text && isWordLike[i] && IsAsciiWordPunctuationChain(texts[i]))
                {
                    string merged = texts[i];
                    int start = starts[i];
                    int j = i + 1;

                    while (EndsWithPunctuationJoiner(merged) &&
                           j < len &&
                           kinds[j] == SegmentKind.Text &&
                           isWordLike[j] &&
                           IsAsciiWordPunctuationChain(texts[j]))
                    {
                        merged += texts[j];
                        j++;
                    }

                    result.Add(merged);
                    resultWL.Add(true);
                    resultK.Add(SegmentKind.Text);
                    resultS.Add(start);
                    i = j - 1;
                    continue;
                }

                result.Add(texts[i]);
                resultWL.Add(isWordLike[i]);
                resultK.Add(kinds[i]);
                resultS.Add(starts[i]);
            }

            return CompactArrays(result.ToArray(), resultWL.ToArray(), resultK.ToArray(), resultS.ToArray());
        }

        private static bool IsAsciiWordPunctuationChain(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char ch in s)
            {
                if (char.IsLetterOrDigit(ch)) continue;
                if (ch == ',' || ch == ':' || ch == ';' || ch == '_') continue;
                return false;
            }
            return true;
        }

        private static bool EndsWithPunctuationJoiner(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char last = s[^1];
            return last == ',' || last == ':' || last == ';';
        }

        /// <summary>
        /// Split hyphenated numeric runs (e.g., SSN-like patterns).
        /// </summary>
        private static string[] SplitHyphenatedNumericRuns(string[] texts, bool[] isWordLike, SegmentKind[] kinds, int[] starts)
        {
            int len = texts.Length;
            var result = new System.Collections.Generic.List<string>();
            var resultWL = new System.Collections.Generic.List<bool>();
            var resultK = new System.Collections.Generic.List<SegmentKind>();
            var resultS = new System.Collections.Generic.List<int>();

            for (int i = 0; i < len; i++)
            {
                if (kinds[i] == SegmentKind.Text && texts[i].Contains('-'))
                {
                    var parts = texts[i].Split('-');
                    bool shouldSplit = parts.Length > 1;
                    if (shouldSplit)
                    {
                        foreach (var part in parts)
                        {
                            bool valid = part.Length > 0 &&
                                         SegmentContainsDecimalDigit(part) &&
                                         IsNumericRunSegment(part);
                            if (!valid) { shouldSplit = false; break; }
                        }
                    }

                    if (shouldSplit)
                    {
                        int offset = 0;
                        foreach (var part in parts)
                        {
                            string splitText = part.Length > 0 ? part + (part != parts[^1] ? "-" : "") : "";
                            result.Add(splitText);
                            resultWL.Add(true);
                            resultK.Add(SegmentKind.Text);
                            resultS.Add(starts[i] + offset);
                            offset += splitText.Length;
                        }
                        continue;
                    }
                }

                result.Add(texts[i]);
                resultWL.Add(isWordLike[i]);
                resultK.Add(kinds[i]);
                resultS.Add(starts[i]);
            }

            return CompactArrays(result.ToArray(), resultWL.ToArray(), resultK.ToArray(), resultS.ToArray());
        }

        /// <summary>
        /// Merge glue-connected text runs (NBSP with adjacent text).
        /// </summary>
        private static string[] MergeGlueConnectedTextRuns(string[] texts, bool[] isWordLike, SegmentKind[] kinds, int[] starts)
        {
            int len = texts.Length;
            int read = 0;
            var resultTexts = new System.Collections.Generic.List<string>();
            var resultWL = new System.Collections.Generic.List<bool>();
            var resultK = new System.Collections.Generic.List<SegmentKind>();
            var resultS = new System.Collections.Generic.List<int>();

            while (read < len)
            {
                string text = texts[read];
                bool wordLike = isWordLike[read];
                SegmentKind kind = kinds[read];
                int start = starts[read];

                if (kind == SegmentKind.Glue)
                {
                    string glueText = text;
                    int glueStart = start;
                    read++;

                    while (read < len && kinds[read] == SegmentKind.Glue)
                    {
                        glueText += texts[read];
                        read++;
                    }

                    if (read < len && kinds[read] == SegmentKind.Text)
                    {
                        text = glueText + texts[read];
                        wordLike = isWordLike[read];
                        kind = SegmentKind.Text;
                        start = glueStart;
                        read++;
                    }
                    else
                    {
                        resultTexts.Add(glueText);
                        resultWL.Add(false);
                        resultK.Add(SegmentKind.Glue);
                        resultS.Add(glueStart);
                        continue;
                    }
                }
                else
                {
                    read++;
                }

                if (kind == SegmentKind.Text)
                {
                    while (read < len && kinds[read] == SegmentKind.Glue)
                    {
                        string glueStr = "";
                        while (read < len && kinds[read] == SegmentKind.Glue)
                        {
                            glueStr += texts[read];
                            read++;
                        }

                        if (read < len && kinds[read] == SegmentKind.Text)
                        {
                            text += glueStr + texts[read];
                            wordLike = wordLike || isWordLike[read];
                            read++;
                            continue;
                        }

                        text += glueStr;
                    }
                }

                resultTexts.Add(text);
                resultWL.Add(wordLike);
                resultK.Add(kind);
                resultS.Add(start);
            }

            return CompactArrays(
                resultTexts.ToArray(),
                resultWL.ToArray(),
                resultK.ToArray(),
                resultS.ToArray());
        }

        /// <summary>
        /// Carry trailing forward-sticky punctuation across CJK boundaries.
        /// </summary>
        private static string[] CarryTrailingForwardStickyAcrossCJKBoundary(
            string[] texts, bool[] isWordLike, SegmentKind[] kinds, int[] starts)
        {
            for (int i = 0; i < texts.Length - 1; i++)
            {
                if (kinds[i] != SegmentKind.Text || kinds[i + 1] != SegmentKind.Text) continue;
                if (!KinsokuData.IsCJK(char.ConvertToUtf32(texts[i], 0)) ||
                    !KinsokuData.IsCJK(char.ConvertToUtf32(texts[i + 1], 0))) continue;

                var split = SplitTrailingForwardStickyCluster(texts[i]);
                if (split == null) continue;

                texts[i] = split.Value.head;
                texts[i + 1] = split.Value.tail + texts[i + 1];
                starts[i + 1] = starts[i] + split.Value.head.Length;
            }

            return texts;
        }

        /// <summary>
        /// Returns true if the segment kind is a text run boundary.
        /// </summary>
        private static bool IsTextRunBoundary(SegmentKind kind)
        {
            return kind == SegmentKind.Space ||
                   kind == SegmentKind.PreservedSpace ||
                   kind == SegmentKind.ZeroWidthBreak ||
                   kind == SegmentKind.HardBreak;
        }

        /// <summary>
        /// Compact arrays by removing empty-string entries.
        /// </summary>
        private static string[] CompactArrays(string[] texts, bool[] isWordLike, SegmentKind[] kinds, int[] starts)
        {
            int compactLen = 0;
            for (int read = 0; read < texts.Length; read++)
            {
                if (texts[read].Length == 0) continue;
                if (compactLen != read)
                {
                    texts[compactLen] = texts[read];
                    isWordLike[compactLen] = isWordLike[read];
                    kinds[compactLen] = kinds[read];
                    starts[compactLen] = starts[read];
                }
                compactLen++;
            }

            if (compactLen == texts.Length) return texts;

            var compactedTexts = new string[compactLen];
            var compactedWL = new bool[compactLen];
            var compactedK = new SegmentKind[compactLen];
            var compactedS = new int[compactLen];
            Array.Copy(texts, compactedTexts, compactLen);
            Array.Copy(isWordLike, compactedWL, compactLen);
            Array.Copy(kinds, compactedK, compactLen);
            Array.Copy(starts, compactedS, compactLen);
            return compactedTexts;
        }

        /// <summary>
        /// Split leading space and combining marks from a segment (for Arabic).
        /// </summary>
        private static (string space, string marks)? SplitLeadingSpaceAndMarks(string seg)
        {
            if (seg.Length < 2 || seg[0] != ' ') return null;
            string marks = seg[1..];
            bool allMarks = true;
            foreach (char ch in marks)
            {
                int cp = char.ConvertToUtf32(marks, 0);
                if (!KinsokuData.IsCombiningMark(cp)) { allMarks = false; break; }
            }
            if (allMarks) return (" ", marks);
            return null;
        }

        /// <summary>
        /// Main entry point: analyze text and produce normalized segments + chunks.
        /// </summary>
        public static TextAnalysis AnalyzeText(string text, AnalysisProfile profile, WhiteSpaceMode mode)
        {
            bool preserveOrdinarySpaces = mode == WhiteSpaceMode.PreWrap;
            bool preserveHardBreaks = mode == WhiteSpaceMode.PreWrap;

            string normalized = mode == WhiteSpaceMode.PreWrap
                ? NormalizeWhitespacePreWrap(text)
                : NormalizeWhitespaceNormal(text);

            if (normalized.Length == 0)
            {
                return new TextAnalysis
                {
                    Normalized = normalized,
                    Len = 0,
                    Texts = [],
                    IsWordLike = [],
                    Kinds = [],
                    Starts = [],
                    Chunks = [],
                };
            }

            // Phase 1: Word segmentation
            int[] wordBreaks = Segmenter.GetWordBreaks(normalized);
            var texts = new System.Collections.Generic.List<string>();
            var isWordLike = new System.Collections.Generic.List<bool>();
            var kinds = new System.Collections.Generic.List<SegmentKind>();
            var starts = new System.Collections.Generic.List<int>();

            for (int wi = 0; wi < wordBreaks.Length - 1; wi++)
            {
                int wordStart = wordBreaks[wi];
                int wordEnd = wordBreaks[wi + 1];
                string word = normalized[wordStart..wordEnd];
                bool wordIsWordLike = false;
                foreach (char c in word)
                    if (char.IsLetterOrDigit(c) || c == '_') { wordIsWordLike = true; break; }

                int segOffset = 0;
                for (int si = 0; si < word.Length;)
                {
                    int cp = char.ConvertToUtf32(word, si);
                    int charLen = char.IsHighSurrogate(word[si]) ? 2 : 1;
                    SegmentKind kind = ClassifySegmentBreakChar(cp, preserveOrdinarySpaces, preserveHardBreaks);
                    bool isText = kind == SegmentKind.Text;
                    bool pieceWordLike = isText && wordIsWordLike;

                    // Collect same-kind runs
                    int runStart = si;
                    while (si < word.Length)
                    {
                        int nextCp = char.ConvertToUtf32(word, si);
                        int nextLen = char.IsHighSurrogate(word[si]) ? 2 : 1;
                        SegmentKind nextKind = ClassifySegmentBreakChar(nextCp, preserveOrdinarySpaces, preserveHardBreaks);
                        bool nextIsText = nextKind == SegmentKind.Text;
                        bool nextPieceWordLike = nextIsText && wordIsWordLike;

                        if (nextKind == kind && nextPieceWordLike == pieceWordLike)
                        {
                            si += nextLen;
                            continue;
                        }
                        break;
                    }

                    if (si > runStart)
                    {
                        texts.Add(word[runStart..si]);
                        isWordLike.Add(pieceWordLike);
                        kinds.Add(kind);
                        starts.Add(wordStart + runStart);
                    }
                    else
                    {
                        texts.Add(word.Substring(si, charLen));
                        isWordLike.Add(pieceWordLike);
                        kinds.Add(kind);
                        starts.Add(wordStart + si);
                        si += charLen;
                    }
                }
            }

            int len = texts.Count;

            // Phase 2: Punctuation merging
            var mergedTexts = new System.Collections.Generic.List<string>(len);
            var mergedWL = new System.Collections.Generic.List<bool>(len);
            var mergedK = new System.Collections.Generic.List<SegmentKind>(len);
            var mergedS = new System.Collections.Generic.List<int>(len);

            for (int i = 0; i < len; i++)
            {
                string pieceText = texts[i];
                SegmentKind pieceKind = kinds[i];
                bool pieceIsText = pieceKind == SegmentKind.Text;

                if (mergedWL.Count > 0)
                {
                    string lastText = mergedTexts[^1];
                    SegmentKind lastKind = mergedK[^1];
                    bool lastIsText = lastKind == SegmentKind.Text;
                    string lastNorm = lastText.Normalize(System.Text.NormalizationForm.FormC);
                    string pieceNorm = pieceText.Normalize(System.Text.NormalizationForm.FormC);

                    bool shouldMerge = false;

                    // Carry CJK after closing quote
                    if (profile.CarryCJKAfterClosingQuote &&
                        pieceIsText && lastIsText &&
                        KinsokuData.IsCJK(char.ConvertToUtf32(pieceNorm, 0)) &&
                        KinsokuData.IsCJK(char.ConvertToUtf32(lastNorm, 0)) &&
                        EndsWithClosingQuote(lastNorm))
                    {
                        shouldMerge = true;
                    }
                    // CJK line start prohibited
                    else if (pieceIsText && lastIsText &&
                             IsCJKLineStartProhibitedSegment(pieceNorm) &&
                             KinsokuData.IsCJK(char.ConvertToUtf32(lastNorm, 0)))
                    {
                        shouldMerge = true;
                    }
                    // Myanmar medial glue
                    else if (pieceIsText && lastIsText && EndsWithMyanmarMedialGlue(lastNorm))
                    {
                        shouldMerge = true;
                    }
                    // Arabic no-space punctuation
                    else if (pieceIsText && !mergedWL[^1] && EndsWithArabicNoSpacePunctuation(lastNorm))
                    {
                        shouldMerge = true;
                    }
                    // Repeated single char
                    else if (pieceIsText && !pieceIsText && pieceText.Length == 1 &&
                             !IsLeftStickyPunctuationSegment(pieceText) &&
                             pieceText != "-" && pieceText != "—")
                    {
                        string last0 = lastNorm;
                        if (last0.Length > 0 && last0[^1] == pieceText[0]) shouldMerge = true;
                    }
                    // Left-sticky punctuation
                    else if (pieceIsText && !mergedWL[^1] &&
                             (IsLeftStickyPunctuationSegment(pieceNorm) ||
                              (pieceText == "-" && mergedWL[^1])))
                    {
                        shouldMerge = true;
                    }

                    if (shouldMerge)
                    {
                        mergedTexts[^1] += pieceText;
                        mergedWL[^1] = mergedWL[^1] || pieceIsText;
                        continue;
                    }
                }

                mergedTexts.Add(pieceText);
                mergedWL.Add(mergedWL.Count > 0 ? mergedWL[^1] : false);
                mergedK.Add(pieceKind);
                mergedS.Add(starts[i]);
            }

            // Escape quote cluster merging
            for (int i = 1; i < mergedTexts.Count; i++)
            {
                if (mergedK[i] == SegmentKind.Text && !mergedWL[i] &&
                    IsEscapedQuoteClusterSegment(mergedTexts[i]) &&
                    mergedK[i - 1] == SegmentKind.Text)
                {
                    mergedTexts[i - 1] += mergedTexts[i];
                    mergedWL[i - 1] = mergedWL[i - 1] || mergedWL[i];
                    mergedTexts[i] = "";
                }
            }

            // Forward-sticky cluster merging
            for (int i = mergedTexts.Count - 2; i >= 0; i--)
            {
                if (mergedK[i] == SegmentKind.Text && !mergedWL[i] &&
                    IsForwardStickyClusterSegment(mergedTexts[i]))
                {
                    int j = i + 1;
                    while (j < mergedTexts.Count && mergedTexts[j].Length == 0) j++;
                    if (j < mergedTexts.Count && mergedK[j] == SegmentKind.Text)
                    {
                        mergedTexts[j] = mergedTexts[i] + mergedTexts[j];
                        mergedS[j] = mergedS[i];
                        mergedTexts[i] = "";
                    }
                }
            }

            // Compact
            var compacted = CompactArrays(
                mergedTexts.ToArray(),
                mergedWL.ToArray(),
                mergedK.ToArray(),
                mergedS.ToArray());

            // Apply merge pipeline
            compacted = MergeGlueConnectedTextRuns(compacted, mergedWL.ToArray(), mergedK.ToArray(), mergedS.ToArray());
            compacted = CarryTrailingForwardStickyAcrossCJKBoundary(
                MergeAsciiPunctuationChains(
                    SplitHyphenatedNumericRuns(
                        MergeNumericRuns(
                            MergeUrlQueryRuns(
                                MergeUrlLikeRuns(
                                    compacted, mergedWL.ToArray(), mergedK.ToArray(), mergedS.ToArray()))))),
                mergedWL.ToArray(), mergedK.ToArray(), mergedS.ToArray());

            // Arabic space+marks split
            for (int i = 0; i < compacted.Length - 1; i++)
            {
                var split = SplitLeadingSpaceAndMarks(compacted[i]);
                if (split == null) continue;
                if ((mergedK[i] != SegmentKind.Space && mergedK[i] != SegmentKind.PreservedSpace) ||
                    mergedK[i + 1] != SegmentKind.Text ||
                    !ContainsArabicScript(compacted[i + 1])) continue;

                compacted[i] = split.Value.space;
                mergedK[i] = mergedK[i] == SegmentKind.PreservedSpace
                    ? SegmentKind.PreservedSpace
                    : SegmentKind.Space;
                compacted[i + 1] = split.Value.marks + compacted[i + 1];
                mergedS[i + 1] = mergedS[i] + 1;
            }

            // Compile chunks
            LineChunk[] chunks = CompileChunks(compacted, mergedK.ToArray(), preserveHardBreaks);

            return new TextAnalysis
            {
                Normalized = normalized,
                Len = compacted.Length,
                Texts = compacted,
                IsWordLike = mergedWL.ToArray(),
                Kinds = mergedK.ToArray(),
                Starts = mergedS.ToArray(),
                Chunks = chunks,
            };
        }

        /// <summary>
        /// Compile analysis chunks from segment kinds.
        /// </summary>
        private static LineChunk[] CompileChunks(string[] texts, SegmentKind[] kinds, bool preserveHardBreaks)
        {
            if (texts.Length == 0) return [];
            if (!preserveHardBreaks)
            {
                return [new LineChunk
                {
                    StartSegmentIndex = 0,
                    EndSegmentIndex = texts.Length,
                    ConsumedEndSegmentIndex = texts.Length,
                }];
            }

            var chunks = new System.Collections.Generic.List<LineChunk>();
            int startSegIdx = 0;

            for (int i = 0; i < texts.Length; i++)
            {
                if (kinds[i] == SegmentKind.HardBreak)
                {
                    chunks.Add(new LineChunk
                    {
                        StartSegmentIndex = startSegIdx,
                        EndSegmentIndex = i,
                        ConsumedEndSegmentIndex = i + 1,
                    });
                    startSegIdx = i + 1;
                }
            }

            if (startSegIdx < texts.Length)
            {
                chunks.Add(new LineChunk
                {
                    StartSegmentIndex = startSegIdx,
                    EndSegmentIndex = texts.Length,
                    ConsumedEndSegmentIndex = texts.Length,
                });
            }

            return chunks.ToArray();
        }
    }
}
