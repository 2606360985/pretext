// Knuth-Plass line breaking algorithm.
// Ported from src/line-break.ts — pure arithmetic, no DOM/canvas calls.

namespace Pretext
{
    /// <summary>
    /// Internal line cursor position used by the line walker.
    /// </summary>
    internal readonly struct LineBreakCursor
    {
        public int SegmentIndex { get; init; }
        public int GraphemeIndex { get; init; }

        public LineBreakCursor(int segmentIndex, int graphemeIndex)
        {
            SegmentIndex = segmentIndex;
            GraphemeIndex = graphemeIndex;
        }
    }

    /// <summary>
    /// Internal line representation (segment/grapheme positions, not text).
    /// </summary>
    internal readonly struct InternalLayoutLine
    {
        public int StartSegmentIndex { get; init; }
        public int StartGraphemeIndex { get; init; }
        public int EndSegmentIndex { get; init; }
        public int EndGraphemeIndex { get; init; }
        public float Width { get; init; }
    }

    /// <summary>
    /// Engine profile: browser-specific behavior flags.
    /// For Unity, we use safe defaults that match the Chrome profile.
    /// </summary>
    internal readonly struct EngineProfile
    {
        /// <summary>
        /// Tolerance for line fitting. Safari uses 1/64, others use 0.005.
        /// </summary>
        public float LineFitEpsilon { get; init; }

        /// <summary>
        /// Whether to carry CJK characters after closing quotes to the next line.
        /// Chrome-specific behavior.
        /// </summary>
        public bool CarryCJKAfterClosingQuote { get; init; }

        /// <summary>
        /// Safari-specific: use prefix widths for breakable runs.
        /// </summary>
        public bool PreferPrefixWidthsForBreakableRuns { get; init; }

        /// <summary>
        /// Safari-specific: prefer early soft-hyphen break.
        /// </summary>
        public bool PreferEarlySoftHyphenBreak { get; init; }

        /// <summary>
        /// Default profile for Unity (matches Chrome-safe behavior).
        /// </summary>
        public static EngineProfile Unity => new()
        {
            LineFitEpsilon = 0.005f,
            CarryCJKAfterClosingQuote = false,
            PreferPrefixWidthsForBreakableRuns = false,
            PreferEarlySoftHyphenBreak = false,
        };
    }

    /// <summary>
    /// Line breaking engine: pure arithmetic algorithms for counting and walking
    /// through line break positions using prepared segment widths.
    /// </summary>
    internal static class LineBreaker
    {
        private static readonly EngineProfile Profile = EngineProfile.Unity;

        /// <summary>Returns true if a break is allowed after the given segment kind.</summary>
        private static bool CanBreakAfter(SegmentKind kind)
        {
            return kind == SegmentKind.Space ||
                   kind == SegmentKind.PreservedSpace ||
                   kind == SegmentKind.Tab ||
                   kind == SegmentKind.ZeroWidthBreak ||
                   kind == SegmentKind.SoftHyphen;
        }

        /// <summary>Returns true if this is a simple collapsible space (no grapheme-level breaking needed).</summary>
        private static bool IsSimpleCollapsibleSpace(SegmentKind kind) => kind == SegmentKind.Space;

        /// <summary>Compute tab advance to the next tab stop.</summary>
        private static float GetTabAdvance(float lineWidth, float tabStopAdvance)
        {
            if (tabStopAdvance <= 0) return 0;
            float remainder = lineWidth % tabStopAdvance;
            if (System.Math.Abs(remainder) <= 1e-6f) return tabStopAdvance;
            return tabStopAdvance - remainder;
        }

        /// <summary>
        /// Get the width of a single grapheme in a breakable segment,
        /// handling Safari prefix-width shim if needed.
        /// </summary>
        private static float GetBreakableAdvance(float[] graphemeWidths, float[]? graphemePrefixWidths, int graphemeIndex, bool preferPrefixWidths)
        {
            if (!preferPrefixWidths || graphemePrefixWidths == null)
                return graphemeWidths[graphemeIndex];

            float current = graphemePrefixWidths[graphemeIndex];
            float previous = graphemeIndex > 0 ? graphemePrefixWidths[graphemeIndex - 1] : 0f;
            return current - previous;
        }

        /// <summary>
        /// Fit soft-hyphen break within a breakable segment.
        /// </summary>
        private static (int fitCount, float fittedWidth) FitSoftHyphenBreak(
            float[] graphemeWidths, float initialWidth, float maxWidth,
            float lineFitEpsilon, float discretionaryHyphenWidth, bool cumulativeWidths)
        {
            int fitCount = 0;
            float fittedWidth = initialWidth;

            while (fitCount < graphemeWidths.Length)
            {
                float nextWidth = cumulativeWidths
                    ? initialWidth + graphemeWidths[fitCount]
                    : fittedWidth + graphemeWidths[fitCount];
                float nextLineWidth = fitCount + 1 < graphemeWidths.Length
                    ? nextWidth + discretionaryHyphenWidth
                    : nextWidth;
                if (nextLineWidth > maxWidth + lineFitEpsilon) break;
                fittedWidth = nextWidth;
                fitCount++;
            }

            return (fitCount, fittedWidth);
        }

        /// <summary>Find the chunk index for a given segment index.</summary>
        private static int FindChunkIndexForStart(LineChunk[] chunks, int segmentIndex)
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                if (segmentIndex < chunks[i].ConsumedEndSegmentIndex) return i;
            }
            return -1;
        }

        /// <summary>
        /// Normalize a line start cursor to the next valid position (skipping whitespace).
        /// </summary>
        public static LineBreakCursor? NormalizeLineStart(LineChunk[] chunks, float[] widths, SegmentKind[] kinds, LineBreakCursor start)
        {
            int segmentIndex = start.SegmentIndex;
            int graphemeIndex = start.GraphemeIndex;

            if (segmentIndex >= widths.Length) return null;
            if (graphemeIndex > 0) return start;

            int chunkIndex = FindChunkIndexForStart(chunks, segmentIndex);
            if (chunkIndex < 0) return null;

            ref readonly var chunk = ref chunks[chunkIndex];
            if (chunk.StartSegmentIndex == chunk.EndSegmentIndex && segmentIndex == chunk.StartSegmentIndex)
                return new LineBreakCursor(segmentIndex, 0);

            if (segmentIndex < chunk.StartSegmentIndex) segmentIndex = chunk.StartSegmentIndex;
            while (segmentIndex < chunk.EndSegmentIndex)
            {
                var kind = kinds[segmentIndex];
                if (kind != SegmentKind.Space && kind != SegmentKind.ZeroWidthBreak && kind != SegmentKind.SoftHyphen)
                    return new LineBreakCursor(segmentIndex, 0);
                segmentIndex++;
            }

            if (chunk.ConsumedEndSegmentIndex >= widths.Length) return null;
            return new LineBreakCursor(chunk.ConsumedEndSegmentIndex, 0);
        }

        /// <summary>
        /// Count the number of lines without building line objects (hot path).
        /// </summary>
        public static int CountPreparedLines(LineChunk[] chunks, float[] widths, SegmentKind[] kinds,
            float[][] breakableWidths, float[][] breakablePrefixWidths, float maxWidth)
        {
            bool simpleFastPath = chunks.Length <= 1;
            if (simpleFastPath)
            {
                return CountPreparedLinesSimple(widths, kinds, breakableWidths, breakablePrefixWidths, maxWidth);
            }
            return WalkPreparedLinesCount(chunks, widths, kinds, breakableWidths, breakablePrefixWidths, maxWidth);
        }

        /// <summary>Simple single-chunk line counter.</summary>
        private static int CountPreparedLinesSimple(float[] widths, SegmentKind[] kinds,
            float[][] breakableWidths, float[][] breakablePrefixWidths, float maxWidth)
        {
            if (widths.Length == 0) return 0;
            float epsilon = Profile.LineFitEpsilon;

            int lineCount = 0;
            float lineW = 0;
            bool hasContent = false;

            void PlaceOnFreshLine(int segIdx)
            {
                float w = widths[segIdx];
                float[]? gWidths = breakableWidths[segIdx];
                if (w > maxWidth && gWidths != null)
                {
                    float[]? gPrefixWidths = breakablePrefixWidths[segIdx];
                    lineW = 0;
                    for (int g = 0; g < gWidths.Length; g++)
                    {
                        float gw = GetBreakableAdvance(gWidths, gPrefixWidths, g, Profile.PreferPrefixWidthsForBreakableRuns);
                        if (lineW > 0 && lineW + gw > maxWidth + epsilon)
                        {
                            lineCount++;
                            lineW = gw;
                        }
                        else
                        {
                            if (lineW == 0) lineCount++;
                            lineW += gw;
                        }
                    }
                }
                else
                {
                    lineW = w;
                    lineCount++;
                }
                hasContent = true;
            }

            for (int i = 0; i < widths.Length; i++)
            {
                float w = widths[i];
                var kind = kinds[i];

                if (!hasContent)
                {
                    PlaceOnFreshLine(i);
                    continue;
                }

                float newW = lineW + w;
                if (newW > maxWidth + epsilon)
                {
                    if (IsSimpleCollapsibleSpace(kind)) continue;
                    lineW = 0;
                    hasContent = false;
                    PlaceOnFreshLine(i);
                    continue;
                }

                lineW = newW;
            }

            if (!hasContent) return lineCount + 1;
            return lineCount;
        }

        /// <summary>Count lines using the full walker (multi-chunk).</summary>
        private static int WalkPreparedLinesCount(LineChunk[] chunks, float[] widths, SegmentKind[] kinds,
            float[][] breakableWidths, float[][] breakablePrefixWidths, float maxWidth)
        {
            int count = 0;
            WalkPreparedLines(chunks, widths, kinds, breakableWidths, breakablePrefixWidths,
                maxWidth, 0f, 0f, false, 0, 0, 0, 0, -1, 0, 0, SegmentKind.Text,
                (ref bool hasContent, ref float lineW, ref int pendingBreakSegIdx,
                 ref float pendingBreakFitWidth, ref float pendingBreakPaintWidth,
                 ref SegmentKind pendingBreakKind) =>
            {
                count++;
                hasContent = false;
                lineW = 0;
                pendingBreakSegIdx = -1;
                pendingBreakFitWidth = 0;
                pendingBreakPaintWidth = 0;
                pendingBreakKind = SegmentKind.Text;
            });
            return count;
        }

        /// <summary>
        /// Walk through all lines, calling onLine for each.
        /// Returns total line count.
        /// </summary>
        public static int WalkPreparedLines(LineChunk[] chunks, float[] widths, SegmentKind[] kinds,
            float[][] breakableWidths, float[][] breakablePrefixWidths, float maxWidth,
            float tabStopAdvance, float discretionaryHyphenWidth,
            Action<InternalLayoutLine>? onLine)
        {
            return WalkPreparedLines(chunks, widths, kinds, breakableWidths, breakablePrefixWidths,
                maxWidth, tabStopAdvance, discretionaryHyphenWidth,
                true, 0, 0, 0, 0, -1, 0, 0, SegmentKind.Text, onLine);
        }

        private delegate void OnFreshLineDelegate(
            ref bool hasContent, ref float lineW, ref int pendingBreakSegIdx,
            ref float pendingBreakFitWidth, ref float pendingBreakPaintWidth,
            ref SegmentKind pendingBreakKind);

        private static int WalkPreparedLines(LineChunk[] chunks, float[] widths, SegmentKind[] kinds,
            float[][] breakableWidths, float[][] breakablePrefixWidths, float maxWidth,
            float tabStopAdvance, float discretionaryHyphenWidth,
            bool hasOnLine, int initStartSegIdx, int initStartGraphemeIdx, int initEndSegIdx,
            int initEndGraphemeIdx, int initPendingBreak, float initPendingBreakFitWidth,
            float initPendingBreakPaintWidth, SegmentKind initPendingBreakKind,
            Action<InternalLayoutLine>? onLine)
        {
            if (widths.Length == 0 || chunks.Length == 0) return 0;
            float epsilon = Profile.LineFitEpsilon;

            int lineCount = 0;
            float lineW = 0;
            bool hasContent = false;
            int lineStartSegIdx = initStartSegIdx;
            int lineStartGraphemeIdx = initStartGraphemeIdx;
            int lineEndSegIdx = initEndSegIdx;
            int lineEndGraphemeIdx = initEndGraphemeIdx;
            int pendingBreakSegIdx = initPendingBreak;
            float pendingBreakFitWidth = initPendingBreakFitWidth;
            float pendingBreakPaintWidth = initPendingBreakPaintWidth;
            SegmentKind pendingBreakKind = initPendingBreakKind;

            void ClearPendingBreak()
            {
                pendingBreakSegIdx = -1;
                pendingBreakFitWidth = 0;
                pendingBreakPaintWidth = 0;
                pendingBreakKind = SegmentKind.Text;
            }

            void EmitCurrentLine(int endSegIdx = lineEndSegIdx, int endGraphemeIdx = lineEndGraphemeIdx, float width = lineW)
            {
                lineCount++;
                onLine?.Invoke(new InternalLayoutLine
                {
                    StartSegmentIndex = lineStartSegIdx,
                    StartGraphemeIndex = lineStartGraphemeIdx,
                    EndSegmentIndex = endSegIdx,
                    EndGraphemeIndex = endGraphemeIdx,
                    Width = width,
                });
                lineW = 0;
                hasContent = false;
                ClearPendingBreak();
            }

            void StartLineAtSegment(int segIdx, float w)
            {
                hasContent = true;
                lineStartSegIdx = segIdx;
                lineStartGraphemeIdx = 0;
                lineEndSegIdx = segIdx + 1;
                lineEndGraphemeIdx = 0;
                lineW = w;
            }

            void StartLineAtGrapheme(int segIdx, int graphemeIdx, float w)
            {
                hasContent = true;
                lineStartSegIdx = segIdx;
                lineStartGraphemeIdx = graphemeIdx;
                lineEndSegIdx = segIdx;
                lineEndGraphemeIdx = graphemeIdx + 1;
                lineW = w;
            }

            void AppendWholeSegment(int segIdx, float w)
            {
                if (!hasContent) { StartLineAtSegment(segIdx, w); return; }
                lineW += w;
                lineEndSegIdx = segIdx + 1;
                lineEndGraphemeIdx = 0;
            }

            void UpdatePendingBreakForWholeSegment(int segIdx, float segWidth)
            {
                var kind = kinds[segIdx];
                if (!CanBreakAfter(kind)) return;
                float fitAdvance = kind == SegmentKind.Tab ? 0 : (segIdx < widths.Length ? widths[segIdx] : 0);
                float paintAdvance = kind == SegmentKind.Tab ? segWidth : (segIdx < widths.Length ? widths[segIdx] : 0);
                pendingBreakSegIdx = segIdx + 1;
                pendingBreakFitWidth = lineW - segWidth + fitAdvance;
                pendingBreakPaintWidth = lineW - segWidth + paintAdvance;
                pendingBreakKind = kind;
            }

            void AppendBreakableSegmentFrom(int segIdx, int startGraphemeIdx)
            {
                float[]? gWidths = breakableWidths[segIdx];
                if (gWidths == null) return;
                float[]? gPrefixWidths = breakablePrefixWidths[segIdx];

                for (int g = startGraphemeIdx; g < gWidths.Length; g++)
                {
                    float gw = GetBreakableAdvance(gWidths, gPrefixWidths, g, Profile.PreferPrefixWidthsForBreakableRuns);

                    if (!hasContent)
                    {
                        StartLineAtGrapheme(segIdx, g, gw);
                        continue;
                    }

                    if (lineW + gw > maxWidth + epsilon)
                    {
                        EmitCurrentLine();
                        StartLineAtGrapheme(segIdx, g, gw);
                    }
                    else
                    {
                        lineW += gw;
                        lineEndSegIdx = segIdx;
                        lineEndGraphemeIdx = g + 1;
                    }
                }

                if (hasContent && lineEndSegIdx == segIdx && lineEndGraphemeIdx == gWidths.Length)
                {
                    lineEndSegIdx = segIdx + 1;
                    lineEndGraphemeIdx = 0;
                }
            }

            bool ContinueSoftHyphenBreakableSegment(int segIdx)
            {
                if (pendingBreakKind != SegmentKind.SoftHyphen) return false;
                float[]? gWidths = breakableWidths[segIdx];
                if (gWidths == null) return false;

                float[] fitWidths = Profile.PreferPrefixWidthsForBreakableRuns
                    ? (breakablePrefixWidths[segIdx] ?? gWidths)
                    : gWidths;
                bool usesPrefixWidths = fitWidths != gWidths;

                var (fitCount, fittedWidth) = FitSoftHyphenBreak(
                    fitWidths, lineW, maxWidth, epsilon, discretionaryHyphenWidth, usesPrefixWidths);

                if (fitCount == 0) return false;

                lineW = fittedWidth;
                lineEndSegIdx = segIdx;
                lineEndGraphemeIdx = fitCount;
                ClearPendingBreak();

                if (fitCount == gWidths.Length)
                {
                    lineEndSegIdx = segIdx + 1;
                    lineEndGraphemeIdx = 0;
                    return true;
                }

                EmitCurrentLine(segIdx, fitCount, fittedWidth + discretionaryHyphenWidth);
                AppendBreakableSegmentFrom(segIdx, fitCount);
                return true;
            }

            // Iterate through chunks
            for (int chunkIdx = 0; chunkIdx < chunks.Length; chunkIdx++)
            {
                ref readonly var chunk = ref chunks[chunkIdx];
                if (chunk.StartSegmentIndex == chunk.EndSegmentIndex)
                {
                    // Empty chunk: emit empty line
                    lineCount++;
                    onLine?.Invoke(new InternalLayoutLine
                    {
                        StartSegmentIndex = chunk.StartSegmentIndex,
                        StartGraphemeIndex = 0,
                        EndSegmentIndex = chunk.ConsumedEndSegmentIndex,
                        EndGraphemeIndex = 0,
                        Width = 0,
                    });
                    ClearPendingBreak();
                    continue;
                }

                hasContent = false;
                lineW = 0;
                lineStartSegIdx = chunk.StartSegmentIndex;
                lineStartGraphemeIdx = 0;
                lineEndSegIdx = chunk.StartSegmentIndex;
                lineEndGraphemeIdx = 0;
                ClearPendingBreak();

                for (int i = chunk.StartSegmentIndex; i < chunk.EndSegmentIndex; i++)
                {
                    var kind = kinds[i];
                    float w = kind == SegmentKind.Tab
                        ? GetTabAdvance(lineW, tabStopAdvance)
                        : widths[i];

                    if (kind == SegmentKind.SoftHyphen)
                    {
                        if (hasContent)
                        {
                            lineEndSegIdx = i + 1;
                            lineEndGraphemeIdx = 0;
                            pendingBreakSegIdx = i + 1;
                            pendingBreakFitWidth = lineW + discretionaryHyphenWidth;
                            pendingBreakPaintWidth = lineW + discretionaryHyphenWidth;
                            pendingBreakKind = kind;
                        }
                        continue;
                    }

                    if (!hasContent)
                    {
                        if (w > maxWidth && breakableWidths[i] != null)
                        {
                            AppendBreakableSegmentFrom(i, 0);
                        }
                        else
                        {
                            StartLineAtSegment(i, w);
                        }
                        UpdatePendingBreakForWholeSegment(i, w);
                        continue;
                    }

                    float newW = lineW + w;
                    if (newW > maxWidth + epsilon)
                    {
                        float currentBreakFitWidth = lineW + (kind == SegmentKind.Tab ? 0 : widths[i]);
                        float currentBreakPaintWidth = lineW + (kind == SegmentKind.Tab ? w : widths[i]);

                        if (pendingBreakKind == SegmentKind.SoftHyphen &&
                            Profile.PreferEarlySoftHyphenBreak &&
                            pendingBreakFitWidth <= maxWidth + epsilon)
                        {
                            EmitCurrentLine(pendingBreakSegIdx, 0, pendingBreakPaintWidth);
                            continue;
                        }

                        if (pendingBreakKind == SegmentKind.SoftHyphen &&
                            ContinueSoftHyphenBreakableSegment(i))
                        {
                            i++;
                            continue;
                        }

                        if (CanBreakAfter(kind) && currentBreakFitWidth <= maxWidth + epsilon)
                        {
                            AppendWholeSegment(i, w);
                            EmitCurrentLine(i + 1, 0, currentBreakPaintWidth);
                            i++;
                            continue;
                        }

                        if (pendingBreakSegIdx >= 0 && pendingBreakFitWidth <= maxWidth + epsilon)
                        {
                            EmitCurrentLine(pendingBreakSegIdx, 0, pendingBreakPaintWidth);
                            continue;
                        }

                        if (w > maxWidth && breakableWidths[i] != null)
                        {
                            EmitCurrentLine();
                            AppendBreakableSegmentFrom(i, 0);
                            i++;
                            continue;
                        }

                        EmitCurrentLine();
                        continue;
                    }

                    AppendWholeSegment(i, w);
                    UpdatePendingBreakForWholeSegment(i, w);
                }

                if (hasContent)
                {
                    float finalPaintWidth =
                        pendingBreakSegIdx == chunk.ConsumedEndSegmentIndex
                            ? pendingBreakPaintWidth
                            : lineW;
                    EmitCurrentLine(chunk.ConsumedEndSegmentIndex, 0, finalPaintWidth);
                }
            }

            return lineCount;
        }

        /// <summary>
        /// Layout a single line starting from the given cursor.
        /// Returns the line, or null if the text is exhausted.
        /// </summary>
        public static InternalLayoutLine? LayoutNextLineRange(LineChunk[] chunks, float[] widths,
            SegmentKind[] kinds, float[][] breakableWidths, float[][] breakablePrefixWidths,
            float maxWidth, float tabStopAdvance, float discretionaryHyphenWidth,
            LayoutCursor start)
        {
            var cursor = new LineBreakCursor(start.SegmentIndex, start.GraphemeIndex);
            var normalized = NormalizeLineStart(chunks, widths, kinds, cursor);
            if (normalized == null) return null;

            int startSegIdx = normalized.Value.SegmentIndex;
            int startGraphemeIdx = normalized.Value.GraphemeIndex;

            // Simple path: single chunk
            if (chunks.Length <= 1)
            {
                return LayoutNextLineRangeSimple(widths, kinds, breakableWidths, breakablePrefixWidths,
                    maxWidth, normalized.Value, discretionaryHyphenWidth);
            }

            int chunkIndex = FindChunkIndexForStart(chunks, startSegIdx);
            if (chunkIndex < 0) return null;

            ref readonly var chunk = ref chunks[chunkIndex];
            if (chunk.StartSegmentIndex == chunk.EndSegmentIndex)
            {
                return new InternalLayoutLine
                {
                    StartSegmentIndex = chunk.StartSegmentIndex,
                    StartGraphemeIndex = 0,
                    EndSegmentIndex = chunk.ConsumedEndSegmentIndex,
                    EndGraphemeIndex = 0,
                    Width = 0,
                };
            }

            float epsilon = Profile.LineFitEpsilon;
            float lineW = 0;
            bool hasContent = false;
            int lineStartSegIdx = startSegIdx;
            int lineStartGraphemeIdx = startGraphemeIdx;
            int lineEndSegIdx = startSegIdx;
            int lineEndGraphemeIdx = startGraphemeIdx;
            int pendingBreakSegIdx = -1;
            float pendingBreakFitWidth = 0;
            float pendingBreakPaintWidth = 0;
            SegmentKind pendingBreakKind = SegmentKind.Text;

            void ClearPendingBreak()
            {
                pendingBreakSegIdx = -1;
                pendingBreakFitWidth = 0;
                pendingBreakPaintWidth = 0;
                pendingBreakKind = SegmentKind.Text;
            }

            InternalLayoutLine? FinishLine(int endSegIdx = lineEndSegIdx, int endGraphemeIdx = lineEndGraphemeIdx, float width = lineW)
            {
                if (!hasContent) return null;
                return new InternalLayoutLine
                {
                    StartSegmentIndex = lineStartSegIdx,
                    StartGraphemeIndex = lineStartGraphemeIdx,
                    EndSegmentIndex = endSegIdx,
                    EndGraphemeIndex = endGraphemeIdx,
                    Width = width,
                };
            }

            void StartLineAtSegment(int segIdx, float w)
            {
                hasContent = true;
                lineEndSegIdx = segIdx + 1;
                lineEndGraphemeIdx = 0;
                lineW = w;
            }

            void StartLineAtGrapheme(int segIdx, int graphemeIdx, float w)
            {
                hasContent = true;
                lineEndSegIdx = segIdx;
                lineEndGraphemeIdx = graphemeIdx + 1;
                lineW = w;
            }

            void AppendWholeSeg(int segIdx, float w)
            {
                if (!hasContent) { StartLineAtSegment(segIdx, w); return; }
                lineW += w;
                lineEndSegIdx = segIdx + 1;
                lineEndGraphemeIdx = 0;
            }

            void UpdatePendingBreak(int segIdx, float segWidth)
            {
                var kind = kinds[segIdx];
                if (!CanBreakAfter(kind)) return;
                float fitAdvance = kind == SegmentKind.Tab ? 0 : widths[segIdx];
                float paintAdvance = kind == SegmentKind.Tab ? segWidth : widths[segIdx];
                pendingBreakSegIdx = segIdx + 1;
                pendingBreakFitWidth = lineW - segWidth + fitAdvance;
                pendingBreakPaintWidth = lineW - segWidth + paintAdvance;
                pendingBreakKind = kind;
            }

            InternalLayoutLine? AppendBreakableFrom(int segIdx, int startGraphemeIdx)
            {
                float[]? gWidths = breakableWidths[segIdx];
                if (gWidths == null) return null;
                float[]? gPrefixWidths = breakablePrefixWidths[segIdx];

                for (int g = startGraphemeIdx; g < gWidths.Length; g++)
                {
                    float gw = GetBreakableAdvance(gWidths, gPrefixWidths, g, Profile.PreferPrefixWidthsForBreakableRuns);

                    if (!hasContent)
                    {
                        StartLineAtGrapheme(segIdx, g, gw);
                        continue;
                    }

                    if (lineW + gw > maxWidth + epsilon)
                        return FinishLine();
                    lineW += gw;
                    lineEndSegIdx = segIdx;
                    lineEndGraphemeIdx = g + 1;
                }

                if (hasContent && lineEndSegIdx == segIdx && lineEndGraphemeIdx == gWidths.Length)
                {
                    lineEndSegIdx = segIdx + 1;
                    lineEndGraphemeIdx = 0;
                }
                return null;
            }

            InternalLayoutLine? MaybeFinishAtSoftHyphen(int segIdx)
            {
                if (pendingBreakKind != SegmentKind.SoftHyphen || pendingBreakSegIdx < 0) return null;
                float[]? gWidths = breakableWidths[segIdx];
                if (gWidths != null)
                {
                    float[] fitWidths = Profile.PreferPrefixWidthsForBreakableRuns
                        ? (breakablePrefixWidths[segIdx] ?? gWidths)
                        : gWidths;
                    bool usesPrefixWidths = fitWidths != gWidths;
                    var (fitCount, fittedWidth) = FitSoftHyphenBreak(
                        fitWidths, lineW, maxWidth, epsilon, discretionaryHyphenWidth, usesPrefixWidths);

                    if (fitCount == gWidths.Length)
                    {
                        lineW = fittedWidth;
                        lineEndSegIdx = segIdx + 1;
                        lineEndGraphemeIdx = 0;
                        ClearPendingBreak();
                        return null;
                    }
                    if (fitCount > 0)
                        return FinishLine(segIdx, fitCount, fittedWidth + discretionaryHyphenWidth);
                }

                if (pendingBreakFitWidth <= maxWidth + epsilon)
                    return FinishLine(pendingBreakSegIdx, 0, pendingBreakPaintWidth);
                return null;
            }

            for (int i = startSegIdx; i < chunk.EndSegmentIndex; i++)
            {
                var kind = kinds[i];
                int sgStartIdx = i == startSegIdx ? startGraphemeIdx : 0;
                float w = kind == SegmentKind.Tab ? GetTabAdvance(lineW, tabStopAdvance) : widths[i];

                if (kind == SegmentKind.SoftHyphen && sgStartIdx == 0)
                {
                    if (hasContent)
                    {
                        lineEndSegIdx = i + 1;
                        lineEndGraphemeIdx = 0;
                        pendingBreakSegIdx = i + 1;
                        pendingBreakFitWidth = lineW + discretionaryHyphenWidth;
                        pendingBreakPaintWidth = lineW + discretionaryHyphenWidth;
                        pendingBreakKind = kind;
                    }
                    continue;
                }

                if (!hasContent)
                {
                    if (sgStartIdx > 0)
                    {
                        var line = AppendBreakableFrom(i, sgStartIdx);
                        if (line != null) return line;
                    }
                    else if (w > maxWidth && breakableWidths[i] != null)
                    {
                        var line = AppendBreakableFrom(i, 0);
                        if (line != null) return line;
                    }
                    else
                    {
                        StartLineAtSegment(i, w);
                    }
                    UpdatePendingBreak(i, w);
                    continue;
                }

                float newW = lineW + w;
                if (newW > maxWidth + epsilon)
                {
                    float currentBreakFitWidth = lineW + (kind == SegmentKind.Tab ? 0 : widths[i]);
                    float currentBreakPaintWidth = lineW + (kind == SegmentKind.Tab ? w : widths[i]);

                    if (pendingBreakKind == SegmentKind.SoftHyphen &&
                        Profile.PreferEarlySoftHyphenBreak &&
                        pendingBreakFitWidth <= maxWidth + epsilon)
                    {
                        return FinishLine(pendingBreakSegIdx, 0, pendingBreakPaintWidth);
                    }

                    var softLine = MaybeFinishAtSoftHyphen(i);
                    if (softLine != null) return softLine;

                    if (CanBreakAfter(kind) && currentBreakFitWidth <= maxWidth + epsilon)
                    {
                        AppendWholeSeg(i, w);
                        return FinishLine(i + 1, 0, currentBreakPaintWidth);
                    }

                    if (pendingBreakSegIdx >= 0 && pendingBreakFitWidth <= maxWidth + epsilon)
                        return FinishLine(pendingBreakSegIdx, 0, pendingBreakPaintWidth);

                    if (w > maxWidth && breakableWidths[i] != null)
                    {
                        var currentLine = FinishLine();
                        if (currentLine != null) return currentLine;
                        var line = AppendBreakableFrom(i, 0);
                        if (line != null) return line;
                    }

                    return FinishLine();
                }

                AppendWholeSeg(i, w);
                UpdatePendingBreak(i, w);
            }

            if (pendingBreakSegIdx == chunk.ConsumedEndSegmentIndex && lineEndGraphemeIdx == 0)
                return FinishLine(chunk.ConsumedEndSegmentIndex, 0, pendingBreakPaintWidth);
            return FinishLine(chunk.ConsumedEndSegmentIndex, 0, lineW);
        }

        /// <summary>Simple path for LayoutNextLineRange (single chunk).</summary>
        private static InternalLayoutLine? LayoutNextLineRangeSimple(float[] widths, SegmentKind[] kinds,
            float[][] breakableWidths, float[][] breakablePrefixWidths, float maxWidth,
            LineBreakCursor normalizedStart, float discretionaryHyphenWidth)
        {
            if (widths.Length == 0) return null;
            float epsilon = Profile.LineFitEpsilon;

            float lineW = 0;
            bool hasContent = false;
            int lineStartSegIdx = normalizedStart.SegmentIndex;
            int lineStartGraphemeIdx = normalizedStart.GraphemeIndex;
            int lineEndSegIdx = lineStartSegIdx;
            int lineEndGraphemeIdx = lineStartGraphemeIdx;
            int pendingBreakSegIdx = -1;
            float pendingBreakPaintWidth = 0;

            InternalLayoutLine? FinishLine(int endSegIdx = lineEndSegIdx, int endGraphemeIdx = lineEndGraphemeIdx, float width = lineW)
            {
                if (!hasContent) return null;
                return new InternalLayoutLine
                {
                    StartSegmentIndex = lineStartSegIdx,
                    StartGraphemeIndex = lineStartGraphemeIdx,
                    EndSegmentIndex = endSegIdx,
                    EndGraphemeIndex = endGraphemeIdx,
                    Width = width,
                };
            }

            void StartLineAtSegment(int segIdx, float w)
            {
                hasContent = true;
                lineEndSegIdx = segIdx + 1;
                lineEndGraphemeIdx = 0;
                lineW = w;
            }

            void StartLineAtGrapheme(int segIdx, int graphemeIdx, float w)
            {
                hasContent = true;
                lineEndSegIdx = segIdx;
                lineEndGraphemeIdx = graphemeIdx + 1;
                lineW = w;
            }

            void AppendWholeSeg(int segIdx, float w)
            {
                if (!hasContent) { StartLineAtSegment(segIdx, w); return; }
                lineW += w;
                lineEndSegIdx = segIdx + 1;
                lineEndGraphemeIdx = 0;
            }

            void UpdatePendingBreak(int segIdx, float segWidth)
            {
                if (!CanBreakAfter(kinds[segIdx])) return;
                pendingBreakSegIdx = segIdx + 1;
                pendingBreakPaintWidth = lineW - segWidth;
            }

            InternalLayoutLine? AppendBreakableFrom(int segIdx, int startGraphemeIdx)
            {
                float[]? gWidths = breakableWidths[segIdx];
                if (gWidths == null) return null;
                float[]? gPrefixWidths = breakablePrefixWidths[segIdx];

                for (int g = startGraphemeIdx; g < gWidths.Length; g++)
                {
                    float gw = GetBreakableAdvance(gWidths, gPrefixWidths, g, Profile.PreferPrefixWidthsForBreakableRuns);

                    if (!hasContent)
                    {
                        StartLineAtGrapheme(segIdx, g, gw);
                        continue;
                    }

                    if (lineW + gw > maxWidth + epsilon)
                        return FinishLine();
                    lineW += gw;
                    lineEndSegIdx = segIdx;
                    lineEndGraphemeIdx = g + 1;
                }

                if (hasContent && lineEndSegIdx == segIdx && lineEndGraphemeIdx == gWidths.Length)
                {
                    lineEndSegIdx = segIdx + 1;
                    lineEndGraphemeIdx = 0;
                }
                return null;
            }

            for (int i = normalizedStart.SegmentIndex; i < widths.Length; i++)
            {
                float w = widths[i];
                var kind = kinds[i];
                int sgStartIdx = i == normalizedStart.SegmentIndex ? normalizedStart.GraphemeIndex : 0;

                if (!hasContent)
                {
                    if (sgStartIdx > 0)
                    {
                        var line = AppendBreakableFrom(i, sgStartIdx);
                        if (line != null) return line;
                    }
                    else if (w > maxWidth && breakableWidths[i] != null)
                    {
                        var line = AppendBreakableFrom(i, 0);
                        if (line != null) return line;
                    }
                    else
                    {
                        StartLineAtSegment(i, w);
                    }
                    UpdatePendingBreak(i, w);
                    continue;
                }

                float newW = lineW + w;
                if (newW > maxWidth + epsilon)
                {
                    if (CanBreakAfter(kind))
                    {
                        AppendWholeSeg(i, w);
                        return FinishLine(i + 1, 0, lineW - w);
                    }

                    if (pendingBreakSegIdx >= 0)
                        return FinishLine(pendingBreakSegIdx, 0, pendingBreakPaintWidth);

                    if (w > maxWidth && breakableWidths[i] != null)
                    {
                        var currentLine = FinishLine();
                        if (currentLine != null) return currentLine;
                        var line = AppendBreakableFrom(i, 0);
                        if (line != null) return line;
                    }

                    return FinishLine();
                }

                AppendWholeSeg(i, w);
                UpdatePendingBreak(i, w);
            }

            return FinishLine();
        }
    }
}
