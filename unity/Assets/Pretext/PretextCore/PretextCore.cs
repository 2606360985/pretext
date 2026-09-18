// Main public API: two-phase text prepare/layout.
// Ported from src/layout.ts — wires together TextAnalyzer, FontMeasurer, BidiResolver, LineBreaker.

namespace Pretext
{
    /// <summary>
    /// Interface for text width measurement.
    /// Implement this to use a specific font API (TMP, native Font, FreeType, etc.).
    /// </summary>
    public interface IFontMeasurer
    {
        /// <summary>
        /// Measure the total width of a string in pixels at the configured font size.
        /// </summary>
        float MeasureWidth(string text);

        /// <summary>
        /// Measure the width of individual grapheme clusters in a string.
        /// Returns an array of widths, one per grapheme.
        /// </summary>
        float[]? MeasureGraphemeWidths(string text);
    }

    /// <summary>
    /// Main entry point for Pretext text layout.
    /// Two-phase API: prepare() once, layout() many times at different widths.
    /// </summary>
    public static class PretextCore
    {
        /// <summary>
        /// Shared segmenter. Replace with ICU4C-backed implementation for full Unicode accuracy.
        /// </summary>
        public static ISegmenter Segmenter
        {
            get => TextAnalyzer.Segmenter;
            set => TextAnalyzer.Segmenter = value ?? new UnicodeRangeSegmenter();
        }

        /// <summary>
        /// Shared line text cache. Maps PreparedText → segmentIndex → grapheme strings.
        /// Used internally to avoid re-segmenting when building line text.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<PreparedText,
            System.Collections.Generic.Dictionary<int, string[]>> LineTextCaches = new(
                new PreparedTextComparer());

        private class PreparedTextComparer : System.Collections.Generic.IEqualityComparer<PreparedText>
        {
            public bool Equals(PreparedText x, PreparedText y) => ReferenceEquals(x, y);
            public int GetHashCode(PreparedText obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        /// <summary>
        /// Prepare text for layout. Segments the text, measures each segment via IFontMeasurer,
        /// and stores widths for fast relayout. Call once per text block.
        ///
        /// The returned PreparedText is width-independent — reuse it across different maxWidth values.
        /// </summary>
        /// <param name="text">The text to prepare.</param>
        /// <param name="measurer">Font measurer providing character widths.</param>
        /// <param name="mode">Whitespace handling mode (normal or pre-wrap).</param>
        /// <param name="includeSegments">If true, also compute bidi embedding levels for rich rendering.</param>
        /// <returns>Prepared text ready for layout().</returns>
        public static PreparedText Prepare(string text, IFontMeasurer measurer,
            WhiteSpaceMode mode = WhiteSpaceMode.Normal, bool includeSegments = false)
        {
            return PrepareInternal(text, measurer, mode, includeSegments);
        }

        /// <summary>
        /// Prepare text with rich segment information exposed.
        /// Use this when you need line text content for custom rendering.
        /// </summary>
        public static PreparedText PrepareWithSegments(string text, IFontMeasurer measurer,
            WhiteSpaceMode mode = WhiteSpaceMode.Normal)
        {
            return PrepareInternal(text, measurer, mode, true);
        }

        /// <summary>
        /// Layout prepared text at the given width. Pure arithmetic, no I/O.
        /// Hot path: ~0.0002ms per call. Call on every resize.
        /// </summary>
        /// <param name="prepared">Prepared text from Prepare().</param>
        /// <param name="maxWidth">Maximum line width in pixels.</param>
        /// <param name="lineHeight">Line height in pixels.</param>
        /// <returns>Layout result with line count and total height.</returns>
        public static LayoutResult Layout(PreparedText prepared, float maxWidth, float lineHeight)
        {
            if (prepared.Widths.Length == 0)
                return new LayoutResult(0, 0);

            int lineCount = LineBreaker.CountPreparedLines(
                prepared.Chunks,
                prepared.Widths,
                prepared.Kinds,
                prepared.BreakableWidths,
                prepared.BreakablePrefixWidths,
                maxWidth);

            return new LayoutResult(lineCount, lineCount * lineHeight);
        }

        /// <summary>
        /// Layout with full per-line information. Slower than Layout() but needed for rendering.
        /// </summary>
        public static LayoutLinesResult LayoutWithLines(PreparedText prepared, float maxWidth, float lineHeight)
        {
            if (prepared.Widths.Length == 0)
                return new LayoutLinesResult(0, 0, []);

            var lines = new System.Collections.Generic.List<LayoutLine>();
            int lineCount = LineBreaker.WalkPreparedLines(
                prepared.Chunks,
                prepared.Widths,
                prepared.Kinds,
                prepared.BreakableWidths,
                prepared.BreakablePrefixWidths,
                maxWidth,
                prepared.TabStopAdvance,
                prepared.DiscretionaryHyphenWidth,
                line =>
                {
                    var layoutLine = BuildLayoutLine(prepared, line);
                    lines.Add(layoutLine);
                });

            return new LayoutLinesResult(lineCount, lineCount * lineHeight, lines.ToArray());
        }

        /// <summary>
        /// Walk through line ranges without building text strings.
        /// Useful for shrinkwrap, binary search on width, etc.
        /// </summary>
        public static int WalkLineRanges(PreparedText prepared, float maxWidth, System.Action<LayoutLineRange> onLine)
        {
            if (prepared.Widths.Length == 0) return 0;

            int count = 0;
            LineBreaker.WalkPreparedLines(
                prepared.Chunks,
                prepared.Widths,
                prepared.Kinds,
                prepared.BreakableWidths,
                prepared.BreakablePrefixWidths,
                maxWidth,
                prepared.TabStopAdvance,
                prepared.DiscretionaryHyphenWidth,
                line =>
                {
                    count++;
                    onLine(new LayoutLineRange(line.Width,
                        new LayoutCursor(line.StartSegmentIndex, line.StartGraphemeIndex),
                        new LayoutCursor(line.EndSegmentIndex, line.EndGraphemeIndex)));
                });
            return count;
        }

        /// <summary>
        /// Layout one line at a time (iterator pattern). Call repeatedly with updated start cursor.
        /// </summary>
        public static LayoutLine? LayoutNextLine(PreparedText prepared, LayoutCursor start, float maxWidth)
        {
            var internalLine = LineBreaker.LayoutNextLineRange(
                prepared.Chunks,
                prepared.Widths,
                prepared.Kinds,
                prepared.BreakableWidths,
                prepared.BreakablePrefixWidths,
                maxWidth,
                prepared.TabStopAdvance,
                prepared.DiscretionaryHyphenWidth,
                start);

            if (internalLine == null) return null;
            return BuildLayoutLine(prepared, internalLine.Value);
        }

        /// <summary>
        /// Clear all internal caches. Call when cycling through many different fonts.
        /// </summary>
        public static void ClearCache()
        {
            LineTextCaches.Clear();
        }

        // ─── Internal Implementation ───────────────────────────────────────────

        private static PreparedText PrepareInternal(string text, IFontMeasurer measurer,
            WhiteSpaceMode mode, bool includeSegments)
        {
            // Step 1: Text analysis
            var profile = new AnalysisProfile
            {
                CarryCJKAfterClosingQuote = false, // Unity-safe
            };
            var analysis = TextAnalyzer.AnalyzeText(text, profile, mode);

            if (analysis.IsEmpty)
            {
                return CreateEmptyPrepared();
            }

            // Step 2: Measure segments
            float spaceWidth = measurer.MeasureWidth(" ");
            float hyphenWidth = measurer.MeasureWidth("-");
            float tabStopAdvance = spaceWidth * 8;

            var widths = new System.Collections.Generic.List<float>();
            var lineEndFitAdvances = new System.Collections.Generic.List<float>();
            var lineEndPaintAdvances = new System.Collections.Generic.List<float>();
            var kinds = new System.Collections.Generic.List<SegmentKind>();
            var breakableWidths = new System.Collections.Generic.List<float[]?>();
            var breakablePrefixWidths = new System.Collections.Generic.List<float[]?>();
            var segments = includeSegments ? new System.Collections.Generic.List<string>() : null;
            var segStarts = new System.Collections.Generic.List<int>();
            var preparedStartByAnalysis = new int[analysis.Len];
            var preparedEndByAnalysis = new int[analysis.Len];
            bool simpleFastPath = analysis.Chunks.Length <= 1;

            for (int mi = 0; mi < analysis.Len; mi++)
            {
                preparedStartByAnalysis[mi] = widths.Count;

                string segText = analysis.Texts[mi];
                bool segWordLike = analysis.IsWordLike[mi];
                var segKind = analysis.Kinds[mi];
                int segStart = analysis.Starts[mi];

                if (segKind == SegmentKind.SoftHyphen)
                {
                    AddMeasuredSegment(widths, lineEndFitAdvances, lineEndPaintAdvances, kinds,
                        breakableWidths, breakablePrefixWidths, segments, segStarts,
                        ref simpleFastPath, segText, 0, hyphenWidth, hyphenWidth, segKind, segStart, null, null);
                    preparedEndByAnalysis[mi] = widths.Count;
                    continue;
                }

                if (segKind == SegmentKind.HardBreak || segKind == SegmentKind.Tab)
                {
                    AddMeasuredSegment(widths, lineEndFitAdvances, lineEndPaintAdvances, kinds,
                        breakableWidths, breakablePrefixWidths, segments, segStarts,
                        ref simpleFastPath, segText, 0, 0, 0, segKind, segStart, null, null);
                    preparedEndByAnalysis[mi] = widths.Count;
                    continue;
                }

                float segWidth = measurer.MeasureWidth(segText);

                // CJK per-grapheme splitting
                if (segKind == SegmentKind.Text && ContainsCJK(segText))
                {
                    SplitCJKSegment(segText, segStart, measurer, widths, lineEndFitAdvances,
                        lineEndPaintAdvances, kinds, breakableWidths, breakablePrefixWidths,
                        segments, segStarts, ref simpleFastPath, segWidth);
                    preparedEndByAnalysis[mi] = widths.Count;
                    continue;
                }

                float lineEndFitAdv = segKind == SegmentKind.Space ||
                                      segKind == SegmentKind.PreservedSpace ||
                                      segKind == SegmentKind.ZeroWidthBreak
                    ? 0 : segWidth;
                float lineEndPaintAdv = segKind == SegmentKind.Space ||
                                        segKind == SegmentKind.ZeroWidthBreak
                    ? 0 : segWidth;

                float[]? graphemeWidths = null;
                float[]? graphemePrefixWidths = null;

                if (segWordLike && segText.Length > 1)
                {
                    graphemeWidths = measurer.MeasureGraphemeWidths(segText);
                    if (graphemeWidths != null && graphemeWidths.Length <= 1)
                        graphemeWidths = null;

                    // Prefix widths: cumulative widths
                    if (graphemeWidths != null && graphemeWidths.Length > 1)
                    {
                        graphemePrefixWidths = new float[graphemeWidths.Length];
                        float cumulative = 0;
                        for (int gi = 0; gi < graphemeWidths.Length; gi++)
                        {
                            cumulative += graphemeWidths[gi];
                            graphemePrefixWidths[gi] = cumulative;
                        }
                    }
                }

                AddMeasuredSegment(widths, lineEndFitAdvances, lineEndPaintAdvances, kinds,
                    breakableWidths, breakablePrefixWidths, segments, segStarts,
                    ref simpleFastPath, segText, segWidth, lineEndFitAdv, lineEndPaintAdv, segKind,
                    segStart, graphemeWidths, graphemePrefixWidths);
                preparedEndByAnalysis[mi] = widths.Count;
            }

            // Map analysis chunks to prepared chunks
            var preparedChunks = MapChunks(analysis.Chunks, preparedStartByAnalysis, preparedEndByAnalysis);

            // Compute bidi levels
            sbyte[]? segLevels = null;
            if (segments != null && segStarts.Count > 0)
            {
                segLevels = BidiTypes.ComputeSegmentLevels(analysis.Normalized, segStarts.ToArray());
            }

            return new PreparedText
            {
                Widths = widths.ToArray(),
                LineEndFitAdvances = lineEndFitAdvances.ToArray(),
                LineEndPaintAdvances = lineEndPaintAdvances.ToArray(),
                Kinds = kinds.ToArray(),
                SimpleLineWalkFastPath = simpleFastPath,
                SegLevels = segLevels ?? [],
                BreakableWidths = breakableWidths.ToArray(),
                BreakablePrefixWidths = breakablePrefixWidths.ToArray(),
                DiscretionaryHyphenWidth = hyphenWidth,
                TabStopAdvance = tabStopAdvance,
                Chunks = preparedChunks,
                Segments = segments?.ToArray(),
            };
        }

        private static void AddMeasuredSegment(
            System.Collections.Generic.List<float> widths,
            System.Collections.Generic.List<float> lineEndFitAdvances,
            System.Collections.Generic.List<float> lineEndPaintAdvances,
            System.Collections.Generic.List<SegmentKind> kinds,
            System.Collections.Generic.List<float[]?> breakableWidths,
            System.Collections.Generic.List<float[]?> breakablePrefixWidths,
            System.Collections.Generic.List<string>? segments,
            System.Collections.Generic.List<int> segStarts,
            ref bool simpleFastPath,
            string segText, float width, float lineEndFitAdv, float lineEndPaintAdv,
            SegmentKind kind, int start, float[]? graphemeWidths, float[]? graphemePrefixWidths)
        {
            if (kind != SegmentKind.Text && kind != SegmentKind.Space &&
                kind != SegmentKind.ZeroWidthBreak)
            {
                simpleFastPath = false;
            }

            widths.Add(width);
            lineEndFitAdvances.Add(lineEndFitAdv);
            lineEndPaintAdvances.Add(lineEndPaintAdv);
            kinds.Add(kind);
            breakableWidths.Add(graphemeWidths);
            breakablePrefixWidths.Add(graphemePrefixWidths);
            segStarts?.Add(start);
            segments?.Add(segText);
        }

        private static void SplitCJKSegment(
            string segText, int segStart, IFontMeasurer measurer,
            System.Collections.Generic.List<float> widths,
            System.Collections.Generic.List<float> lineEndFitAdvances,
            System.Collections.Generic.List<float> lineEndPaintAdvances,
            System.Collections.Generic.List<SegmentKind> kinds,
            System.Collections.Generic.List<float[]?> breakableWidths,
            System.Collections.Generic.List<float[]?> breakablePrefixWidths,
            System.Collections.Generic.List<string>? segments,
            System.Collections.Generic.List<int> segStarts,
            ref bool simpleFastPath,
            float totalWidth)
        {
            // Get per-grapheme widths
            float[]? gWidths = measurer.MeasureGraphemeWidths(segText);
            if (gWidths == null || gWidths.Length <= 1)
            {
                // Fallback: single segment
                AddMeasuredSegment(widths, lineEndFitAdvances, lineEndPaintAdvances, kinds,
                    breakableWidths, breakablePrefixWidths, segments, segStarts,
                    ref simpleFastPath, segText, totalWidth, totalWidth, totalWidth,
                    SegmentKind.Text, segStart, null, null);
                return;
            }

            // Split at kinsoku boundaries
            int unitStart = 0;
            for (int gi = 0; gi <= gWidths.Length; gi++)
            {
                if (gi < gWidths.Length)
                {
                    string unitText = segText[unitStart..(gi + 1)];
                    int unitCP = char.ConvertToUtf32(segText, unitStart);

                    // Check kinsoku boundaries
                    if (gi > 0)
                    {
                        int lastCP = char.ConvertToUtf32(segText, unitStart);
                        int firstCP = unitCP;

                        bool shouldSplit =
                            KinsokuData.KinsokuEnd.Contains(lastCP) ||
                            KinsokuData.KinsokuStart.Contains(firstCP) ||
                            KinsokuData.LeftStickyPunctuation.Contains(firstCP) ||
                            KinsokuData.ClosingQuoteChars.Contains(firstCP);

                        if (!shouldSplit)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                // Emit current unit
                if (gi > unitStart)
                {
                    string unitText = segText[unitStart..gi];
                    float unitWidth = 0;
                    for (int wi = unitStart; wi < gi; wi++) unitWidth += gWidths[wi];

                    AddMeasuredSegment(widths, lineEndFitAdvances, lineEndPaintAdvances, kinds,
                        breakableWidths, breakablePrefixWidths, segments, segStarts,
                        ref simpleFastPath, unitText, unitWidth, unitWidth, unitWidth,
                        SegmentKind.Text, segStart + unitStart, null, null);
                }

                unitStart = gi;
            }

            // Last unit
            if (unitStart < gWidths.Length)
            {
                string unitText = segText[unitStart..];
                float unitWidth = 0;
                for (int wi = unitStart; wi < gWidths.Length; wi++) unitWidth += gWidths[wi];

                AddMeasuredSegment(widths, lineEndFitAdvances, lineEndPaintAdvances, kinds,
                    breakableWidths, breakablePrefixWidths, segments, segStarts,
                    ref simpleFastPath, unitText, unitWidth, unitWidth, unitWidth,
                    SegmentKind.Text, segStart + unitStart, null, null);
            }
        }

        private static bool ContainsCJK(string text)
        {
            for (int i = 0; i < text.Length;)
            {
                int cp = char.ConvertToUtf32(text, i);
                if (KinsokuData.IsCJK(cp)) return true;
                i += char.IsHighSurrogate(text[i]) ? 2 : 1;
            }
            return false;
        }

        private static LineChunk[] MapChunks(LineChunk[] analysisChunks,
            int[] startByAnalysis, int[] endByAnalysis)
        {
            var prepared = new LineChunk[analysisChunks.Length];
            for (int i = 0; i < analysisChunks.Length; i++)
            {
                ref readonly var ac = ref analysisChunks[i];
                int startSegIdx = ac.StartSegmentIndex < startByAnalysis.Length
                    ? startByAnalysis[ac.StartSegmentIndex]
                    : (endByAnalysis.Length > 0 ? endByAnalysis[^1] : 0);
                int endSegIdx = ac.EndSegmentIndex < startByAnalysis.Length
                    ? startByAnalysis[ac.EndSegmentIndex]
                    : (endByAnalysis.Length > 0 ? endByAnalysis[^1] : 0);
                int consumedEndSegIdx = ac.ConsumedEndSegmentIndex < startByAnalysis.Length
                    ? startByAnalysis[ac.ConsumedEndSegmentIndex]
                    : (endByAnalysis.Length > 0 ? endByAnalysis[^1] : 0);

                prepared[i] = new LineChunk
                {
                    StartSegmentIndex = startSegIdx,
                    EndSegmentIndex = endSegIdx,
                    ConsumedEndSegmentIndex = consumedEndSegIdx,
                };
            }
            return prepared;
        }

        private static PreparedText CreateEmptyPrepared()
        {
            return new PreparedText
            {
                Widths = [],
                LineEndFitAdvances = [],
                LineEndPaintAdvances = [],
                Kinds = [],
                SimpleLineWalkFastPath = true,
                SegLevels = [],
                BreakableWidths = [],
                BreakablePrefixWidths = [],
                DiscretionaryHyphenWidth = 0,
                TabStopAdvance = 0,
                Chunks = [],
                Segments = [],
            };
        }

        /// <summary>
        /// Get cached grapheme splits for a segment.
        /// </summary>
        private static string[] GetSegmentGraphemes(PreparedText prepared, int segmentIndex)
        {
            if (prepared.Segments == null) return [prepared.Segments?[segmentIndex] ?? ""];

            if (!LineTextCaches.TryGetValue(prepared, out var cache))
            {
                cache = new System.Collections.Generic.Dictionary<int, string[]>();
                LineTextCaches[prepared] = cache;
            }

            if (cache.TryGetValue(segmentIndex, out var cached)) return cached;

            string seg = prepared.Segments[segmentIndex];
            int[] breaks = TextAnalyzer.Segmenter.GetGraphemeBreaks(seg);
            var graphemes = new string[System.Math.Max(0, breaks.Length - 1)];
            for (int i = 0; i < breaks.Length - 1; i++)
            {
                graphemes[i] = seg[breaks[i]..breaks[i + 1]];
            }
            cache[segmentIndex] = graphemes;
            return graphemes;
        }

        /// <summary>
        /// Build a LayoutLine from internal line data.
        /// </summary>
        private static LayoutLine BuildLayoutLine(PreparedText prepared, InternalLayoutLine line)
        {
            if (prepared.Segments == null)
            {
                return new LayoutLine("", line.Width,
                    new LayoutCursor(line.StartSegmentIndex, line.StartGraphemeIndex),
                    new LayoutCursor(line.EndSegmentIndex, line.EndGraphemeIndex));
            }

            bool hasDiscretionaryHyphen = line.EndSegmentIndex > 0 &&
                                           line.EndSegmentIndex <= prepared.Kinds.Length &&
                                           prepared.Kinds[line.EndSegmentIndex - 1] == SegmentKind.SoftHyphen &&
                                           !(line.StartSegmentIndex == line.EndSegmentIndex && line.StartGraphemeIndex > 0);

            var sb = new System.Text.StringBuilder();

            for (int i = line.StartSegmentIndex; i < line.EndSegmentIndex; i++)
            {
                if (prepared.Kinds[i] == SegmentKind.SoftHyphen ||
                    prepared.Kinds[i] == SegmentKind.HardBreak) continue;

                if (i == line.StartSegmentIndex && line.StartGraphemeIndex > 0)
                {
                    string[] graphemes = GetSegmentGraphemes(prepared, i);
                    for (int gi = line.StartGraphemeIndex; gi < graphemes.Length; gi++)
                        sb.Append(graphemes[gi]);
                }
                else
                {
                    sb.Append(prepared.Segments[i]);
                }
            }

            if (line.EndGraphemeIndex > 0)
            {
                if (hasDiscretionaryHyphen) sb.Append('-');
                string[] graphemes = GetSegmentGraphemes(prepared, line.EndSegmentIndex);
                int start = line.StartSegmentIndex == line.EndSegmentIndex ? line.StartGraphemeIndex : 0;
                for (int gi = start; gi < line.EndGraphemeIndex && gi < graphemes.Length; gi++)
                    sb.Append(graphemes[gi]);
            }
            else if (hasDiscretionaryHyphen)
            {
                sb.Append('-');
            }

            return new LayoutLine(sb.ToString(), line.Width,
                new LayoutCursor(line.StartSegmentIndex, line.StartGraphemeIndex),
                new LayoutCursor(line.EndSegmentIndex, line.EndGraphemeIndex));
        }
    }
}
