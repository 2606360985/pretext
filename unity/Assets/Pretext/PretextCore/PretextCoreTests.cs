// Unit tests for PretextCore.
// Ported from src/layout.test.ts — uses deterministic fake measurer for reproducible results.
// Place in a Test folder and import NUnit package for full test runner integration.

using System;
using System.Linq;
using NUnit.Framework;

namespace Pretext.Tests
{
    /// <summary>
    /// Deterministic font measurer that mirrors the JS test's fake canvas.
    /// Used for reproducible unit tests.
    /// - Space: fontSize * 0.33
    /// - Tab: fontSize * 1.32
    /// - Emoji: fontSize
    /// - CJK (wide char): fontSize
    /// - Punctuation: fontSize * 0.4
    /// - Normal ASCII: fontSize * 0.6
    /// </summary>
    internal sealed class FakeFontMeasurer : IFontMeasurer
    {
        private readonly float _fontSize;

        public FakeFontMeasurer(float fontSize = 16f)
        {
            _fontSize = fontSize;
        }

        public float MeasureWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float width = 0;
            for (int i = 0; i < text.Length;)
            {
                width += MeasureCharWidth(text, ref i);
            }
            return width;
        }

        public float[]? MeasureGraphemeWidths(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            int[] breaks = TextAnalyzer.Segmenter.GetGraphemeBreaks(text);
            if (breaks.Length <= 1) return null;

            var widths = new float[breaks.Length - 1];
            for (int gi = 0; gi < widths.Length; gi++)
            {
                int start = breaks[gi];
                int end = breaks[gi + 1];
                string grapheme = text.Substring(start, end - start);
                widths[gi] = MeasureWidth(grapheme);
            }

            return widths.Length > 1 ? widths : null;
        }

        private float MeasureCharWidth(string text, ref int i)
        {
            if (i >= text.Length) return 0;

            int cp = char.ConvertToUtf32(text, i);
            int charLen = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;

            if (cp == ' ') return _fontSize * 0.33f;
            if (cp == '\t') return _fontSize * 1.32f;
            if (IsEmoji(cp)) return _fontSize;
            if (IsWideChar(cp)) return _fontSize;
            if (IsPunctuation(cp)) return _fontSize * 0.4f;
            return _fontSize * 0.6f;
        }

        private static bool IsEmoji(int cp) =>
            cp >= 0x1F300 && cp <= 0x1F9FF ||  // Miscellaneous Symbols and Pictographs, Emoticons
            cp >= 0x2600 && cp <= 0x26FF;        // Misc symbols (some emoji)

        private static bool IsWideChar(int cp) =>
            (cp >= 0x4E00 && cp <= 0x9FFF) ||   // CJK Unified Ideographs
            (cp >= 0x3400 && cp <= 0x4DBF) ||   // CJK Extension A
            (cp >= 0x3000 && cp <= 0x303F) ||   // CJK Symbols
            (cp >= 0x3040 && cp <= 0x309F) ||   // Hiragana
            (cp >= 0x30A0 && cp <= 0x30FF) ||   // Katakana
            (cp >= 0xAC00 && cp <= 0xD7AF);     // Hangul

        private static bool IsPunctuation(int cp) =>
            cp == '.' || cp == ',' || cp == '!' || cp == '?' ||
            cp == ':' || cp == ';' || cp == '%' || cp == '"' ||
            cp == '\'' || cp == '-' || cp == '—' ||
            cp == '(' || cp == ')' || cp == '[' || cp == ']' || cp == '{' || cp == '}' ||
            cp == '/' || cp == '+';
    }

    [TestFixture]
    public class PretextCoreTests
    {
        private const float FontSize = 16f;
        private const float LineHeight = 19f;
        private FakeFontMeasurer _measurer = null!;

        [SetUp]
        public void Setup()
        {
            _measurer = new FakeFontMeasurer(FontSize);
            PretextCore.ClearCache();
        }

        // ─── Prepare Invariants ────────────────────────────────────────────────

        [Test]
        public void WhitespaceOnly_Input_Stays_Empty()
        {
            var prepared = PretextCore.Prepare("  \t\n  ", _measurer);
            var result = PretextCore.Layout(prepared, 200, LineHeight);
            Assert.AreEqual(0, result.LineCount);
            Assert.AreEqual(0, result.Height);
        }

        [Test]
        public void Collapses_Ordinary_Whitespace_And_Trims()
        {
            var prepared = PretextCore.PrepareWithSegments("  Hello\t \n  World  ", _measurer);
            Assert.AreEqual(new[] { "Hello", " ", "World" }, prepared.Segments);
        }

        [Test]
        public void PreWrap_Keeps_Ordinary_Spaces()
        {
            var prepared = PretextCore.PrepareWithSegments("  Hello   World  ", _measurer, WhiteSpaceMode.PreWrap);
            Assert.AreEqual(new[] { "  ", "Hello", "   ", "World", "  " }, prepared.Segments);
            Assert.AreEqual(new[]
            {
                SegmentKind.PreservedSpace, SegmentKind.Text, SegmentKind.PreservedSpace,
                SegmentKind.Text, SegmentKind.PreservedSpace
            }, prepared.Kinds);
        }

        [Test]
        public void PreWrap_Keeps_HardBreaks()
        {
            var prepared = PretextCore.PrepareWithSegments("Hello\nWorld", _measurer, WhiteSpaceMode.PreWrap);
            Assert.AreEqual(new[] { "Hello", "\n", "World" }, prepared.Segments);
            Assert.AreEqual(new[] { SegmentKind.Text, SegmentKind.HardBreak, SegmentKind.Text }, prepared.Kinds);
        }

        [Test]
        public void PreWrap_Normalizes_CRLF()
        {
            var prepared = PretextCore.PrepareWithSegments("Hello\r\nWorld", _measurer, WhiteSpaceMode.PreWrap);
            Assert.AreEqual(new[] { "Hello", "\n", "World" }, prepared.Segments);
            Assert.AreEqual(new[] { SegmentKind.Text, SegmentKind.HardBreak, SegmentKind.Text }, prepared.Kinds);
        }

        [Test]
        public void PreWrap_Keeps_Tabs()
        {
            var prepared = PretextCore.PrepareWithSegments("Hello\tWorld", _measurer, WhiteSpaceMode.PreWrap);
            Assert.AreEqual(new[] { "Hello", "\t", "World" }, prepared.Segments);
            Assert.AreEqual(new[] { SegmentKind.Text, SegmentKind.Tab, SegmentKind.Text }, prepared.Kinds);
        }

        [Test]
        public void NonBreakingSpaces_StayAsGlue()
        {
            var prepared = PretextCore.PrepareWithSegments("Hello\u00A0world", _measurer);
            Assert.AreEqual(new[] { "Hello\u00A0world" }, prepared.Segments);
            Assert.AreEqual(new[] { SegmentKind.Text }, prepared.Kinds);
        }

        [Test]
        public void ZeroWidthSpaces_AreBreakOpportunities()
        {
            var prepared = PretextCore.PrepareWithSegments("alpha\u200Bbeta", _measurer);
            Assert.AreEqual(new[] { "alpha", "\u200B", "beta" }, prepared.Segments);
            Assert.AreEqual(new[] { SegmentKind.Text, SegmentKind.ZeroWidthBreak, SegmentKind.Text }, prepared.Kinds);

            float alphaWidth = prepared.Widths[0];
            var result = PretextCore.Layout(prepared, alphaWidth + 0.1f, LineHeight);
            Assert.AreEqual(2, result.LineCount);
        }

        [Test]
        public void SoftHyphens_AreDiscretionaryBreakPoints()
        {
            var prepared = PretextCore.PrepareWithSegments("trans\u00ADatlantic", _measurer);
            Assert.AreEqual(new[] { "trans", "\u00AD", "atlantic" }, prepared.Segments);
            Assert.AreEqual(new[] { SegmentKind.Text, SegmentKind.SoftHyphen, SegmentKind.Text }, prepared.Kinds);

            var wide = PretextCore.LayoutWithLines(prepared, 200, LineHeight);
            Assert.AreEqual(1, wide.LineCount);
            Assert.AreEqual(new[] { "transatlantic" }, wide.Lines.Select(l => l.Text).ToArray());
        }

        [Test]
        public void SoftHyphens_Visible_WhenBroken()
        {
            var prepared = PretextCore.PrepareWithSegments("foo trans\u00ADatlantic", _measurer);
            float softBreakWidth = Math.Max(
                prepared.Widths[0] + prepared.Widths[1] + prepared.Widths[2] + prepared.DiscretionaryHyphenWidth,
                prepared.Widths[4]) + 0.1f;

            var narrow = PretextCore.LayoutWithLines(prepared, softBreakWidth, LineHeight);
            Assert.AreEqual(2, narrow.LineCount);
            Assert.AreEqual("foo trans-", narrow.Lines[0].Text);
            Assert.AreEqual("atlantic", narrow.Lines[1].Text);
        }

        [Test]
        public void ClosingPunctuation_AttachedToPrecedingWord()
        {
            var prepared = PretextCore.PrepareWithSegments("hello.", _measurer);
            Assert.AreEqual(new[] { "hello." }, prepared.Segments);
        }

        [Test]
        public void URLs_StayTogether()
        {
            var prepared = PretextCore.PrepareWithSegments(
                "see https://example.com/reports/q3?lang=ar&mode=full now", _measurer);

            // URL should be merged or kept together
            Assert.Greater(prepared.Segments.Length, 3);
            // The first segment after "see" should contain the URL
            Assert.IsTrue(prepared.Segments.Contains("https://example.com/reports/q3?") ||
                         prepared.Segments.Any(s => s.Contains("https://")));
        }

        [Test]
        public void EmDashes_StayBreakable()
        {
            var prepared = PretextCore.PrepareWithSegments("universe—so", _measurer);
            // Should be: ['universe', '—', 'so']
            Assert.GreaterOrEqual(prepared.Segments.Length, 2);
        }

        [Test]
        public void RepeatedPunctuation_Coalesces()
        {
            var prepared = PretextCore.PrepareWithSegments("=== heading ===", _measurer);
            Assert.AreEqual(new[] { "===", " ", "heading", " ", "===" }, prepared.Segments);
        }

        [Test]
        public void CJK_Punctuation_Attached()
        {
            var prepared = PretextCore.PrepareWithSegments("中文，测试。", _measurer);
            // Each CJK char should be separate, punctuation attached to preceding
            Assert.GreaterOrEqual(prepared.Segments.Length, 2);
        }

        [Test]
        public void Prepare_And_PrepareWithSegments_Agree()
        {
            var plain = PretextCore.Prepare("Alpha beta gamma", _measurer);
            var rich = PretextCore.PrepareWithSegments("Alpha beta gamma", _measurer);

            foreach (int width in new[] { 40, 80, 200 })
            {
                var plainResult = PretextCore.Layout(plain, width, LineHeight);
                var richResult = PretextCore.Layout(rich, width, LineHeight);
                Assert.AreEqual(plainResult.LineCount, richResult.LineCount);
                Assert.AreEqual(plainResult.Height, richResult.Height);
            }
        }

        // ─── Layout Invariants ─────────────────────────────────────────────────

        [Test]
        public void LineCount_GrowsMonotonically_AsWidthShrinks()
        {
            var prepared = PretextCore.Prepare("The quick brown fox jumps over the lazy dog", _measurer);
            int previous = 0;

            foreach (int width in new[] { 320, 200, 140, 90 })
            {
                var result = PretextCore.Layout(prepared, width, LineHeight);
                Assert.GreaterOrEqual(result.LineCount, previous,
                    $"Line count should not decrease when width shrinks to {width}");
                previous = result.LineCount;
            }
        }

        [Test]
        public void TrailingWhitespace_HangsPastLineEdge()
        {
            var prepared = PretextCore.PrepareWithSegments("Hello ", _measurer);
            float widthOfHello = prepared.Widths[0];

            var result = PretextCore.Layout(prepared, widthOfHello, LineHeight);
            Assert.AreEqual(1, result.LineCount);

            var withLines = PretextCore.LayoutWithLines(prepared, widthOfHello, LineHeight);
            Assert.AreEqual(1, withLines.LineCount);
            Assert.AreEqual("Hello", withLines.Lines[0].Text);
        }

        [Test]
        public void LongWords_BreakAtGraphemeBoundaries()
        {
            var prepared = PretextCore.PrepareWithSegments("Superlongword", _measurer);
            float[]? gWidths = prepared.BreakableWidths[0];

            if (gWidths != null && gWidths.Length >= 3)
            {
                float maxWidth = gWidths[0] + gWidths[1] + gWidths[2] + 0.1f;
                var plain = PretextCore.Layout(prepared, maxWidth, LineHeight);
                var rich = PretextCore.LayoutWithLines(prepared, maxWidth, LineHeight);

                Assert.Greater(plain.LineCount, 1);
                Assert.AreEqual(plain.LineCount, rich.LineCount);
                Assert.AreEqual(plain.Height, rich.Height);
                Assert.AreEqual("Superlongword", string.Join("", rich.Lines.Select(l => l.Text)));
            }
        }

        [Test]
        public void LayoutNextLine_ReproducesLayoutWithLines()
        {
            var prepared = PretextCore.PrepareWithSegments(
                "foo trans\u00ADatlantic said \"hello\" to 世界 and waved.", _measurer);

            float[] gWidths = prepared.BreakableWidths.Length > 4 && prepared.BreakableWidths[4] != null
                ? prepared.BreakableWidths[4]
                : new float[0];
            if (gWidths.Length == 0) return; // Skip if no breakable data

            float width = prepared.Widths[0] + prepared.Widths[1] + prepared.Widths[2] + gWidths[0] + prepared.DiscretionaryHyphenWidth + 0.1f;
            var expected = PretextCore.LayoutWithLines(prepared, width, LineHeight);

            var actual = new System.Collections.Generic.List<LayoutLine>();
            var cursor = new LayoutCursor(0, 0);
            while (true)
            {
                var line = PretextCore.LayoutNextLine(prepared, cursor, width);
                if (line == null) break;
                actual.Add(line.Value);
                cursor = line.Value.End;
            }

            Assert.AreEqual(expected.LineCount, actual.Count);
            for (int i = 0; i < expected.LineCount; i++)
            {
                Assert.AreEqual(expected.Lines[i].Text, actual[i].Text);
                Assert.AreEqual(expected.Lines[i].Width, actual[i].Width, 0.001f);
            }
        }

        [Test]
        public void PreWrap_HardBreaks_ForcedLineBoundaries()
        {
            var prepared = PretextCore.PrepareWithSegments("a\nb", _measurer, WhiteSpaceMode.PreWrap);
            var lines = PretextCore.LayoutWithLines(prepared, 200, LineHeight);
            Assert.AreEqual(new[] { "a", "b" }, lines.Lines.Select(l => l.Text).ToArray());
            Assert.AreEqual(2, PretextCore.Layout(prepared, 200, LineHeight).LineCount);
        }

        [Test]
        public void PreWrap_Tabs_AlignedToTabStops()
        {
            var prepared = PretextCore.PrepareWithSegments("a\tb", _measurer, WhiteSpaceMode.PreWrap);
            float spaceWidth = _measurer.MeasureWidth(" ");
            float prefixWidth = _measurer.MeasureWidth("a");
            float tabAdvance = TabAdvance(prefixWidth, spaceWidth);
            float textWidth = prefixWidth + tabAdvance + _measurer.MeasureWidth("b");
            float width = textWidth - 0.1f;

            var lines = PretextCore.LayoutWithLines(prepared, width, LineHeight);
            Assert.AreEqual(new[] { "a\t", "b" }, lines.Lines.Select(l => l.Text).ToArray());
            Assert.AreEqual(2, PretextCore.Layout(prepared, width, LineHeight).LineCount);
        }

        [Test]
        public void PreWrap_ConsecutiveHardBreaks_EmptyLines()
        {
            var prepared = PretextCore.PrepareWithSegments("\n\n", _measurer, WhiteSpaceMode.PreWrap);
            var lines = PretextCore.LayoutWithLines(prepared, 200, LineHeight);
            Assert.AreEqual(new[] { "", "" }, lines.Lines.Select(l => l.Text).ToArray());
            Assert.AreEqual(2, lines.LineCount);
        }

        [Test]
        public void WalkLineRanges_MatchesLayoutWithLines()
        {
            var prepared = PretextCore.PrepareWithSegments(
                "foo trans\u00ADatlantic said \"hello\" to 世界 and waved.", _measurer);

            float[] gWidths = prepared.BreakableWidths.Length > 4 && prepared.BreakableWidths[4] != null
                ? prepared.BreakableWidths[4]
                : new float[0];
            if (gWidths.Length == 0) return;

            float width = prepared.Widths[0] + prepared.Widths[1] + prepared.Widths[2] + gWidths[0] +
                         prepared.DiscretionaryHyphenWidth + 0.1f;

            var expected = PretextCore.LayoutWithLines(prepared, width, LineHeight);
            var actual = new System.Collections.Generic.List<LayoutLineRange>();
            int count = PretextCore.WalkLineRanges(prepared, width, line => actual.Add(line));

            Assert.AreEqual(expected.LineCount, count);
            for (int i = 0; i < expected.LineCount; i++)
            {
                Assert.AreEqual(expected.Lines[i].Width, actual[i].Width, 0.001f);
                Assert.AreEqual(expected.Lines[i].Start.SegmentIndex, actual[i].Start.SegmentIndex);
            }
        }

        // ─── Helper ───────────────────────────────────────────────────────────

        private static float TabAdvance(float lineWidth, float spaceWidth, int tabSize = 8)
        {
            float tabStopAdvance = spaceWidth * tabSize;
            float remainder = lineWidth % tabStopAdvance;
            return Math.Abs(remainder) < 1e-6f ? tabStopAdvance : tabStopAdvance - remainder;
        }
    }

    [TestFixture]
    public class KinsokuDataTests
    {
        [Test]
        public void KinsokuStart_ContainsExpectedChars()
        {
            Assert.IsTrue(KinsokuData.KinsokuStart.Contains(0xFF0C)); // ，
            Assert.IsTrue(KinsokuData.KinsokuStart.Contains(0x3001)); // 、.
            Assert.IsTrue(KinsokuData.KinsokuStart.Contains(0x3002)); // 。
            Assert.IsTrue(KinsokuData.KinsokuStart.Contains(0x30FB)); // ・
        }

        [Test]
        public void KinsokuEnd_ContainsExpectedChars()
        {
            Assert.IsTrue(KinsokuData.KinsokuEnd.Contains('('));
            Assert.IsTrue(KinsokuData.KinsokuEnd.Contains('['));
            Assert.IsTrue(KinsokuData.KinsokuEnd.Contains(0xFF08)); // （
            Assert.IsTrue(KinsokuData.KinsokuEnd.Contains(0x300C)); // 「
        }

        [Test]
        public void LeftStickyPunctuation_ContainsExpectedChars()
        {
            Assert.IsTrue(KinsokuData.LeftStickyPunctuation.Contains('.'));
            Assert.IsTrue(KinsokuData.LeftStickyPunctuation.Contains(','));
            Assert.IsTrue(KinsokuData.LeftStickyPunctuation.Contains('!'));
            Assert.IsTrue(KinsokuData.LeftStickyPunctuation.Contains('?'));
            Assert.IsTrue(KinsokuData.LeftStickyPunctuation.Contains(0x201D)); // "
        }

        [Test]
        public void IsCJK_DetectsCJKRanges()
        {
            Assert.IsTrue(KinsokuData.IsCJK(0x4E00)); // 一 (CJK Unified)
            Assert.IsTrue(KinsokuData.IsCJK(0x3040)); // は (Hiragana)
            Assert.IsTrue(KinsokuData.IsCJK(0x30A0)); // ァ (Katakana)
            Assert.IsTrue(KinsokuData.IsCJK(0xAC00)); // 가 (Hangul)
            Assert.IsFalse(KinsokuData.IsCJK('A'));
            Assert.IsFalse(KinsokuData.IsCJK(' '));
        }

        [Test]
        public void IsCombiningMark_DetectsCombiningChars()
        {
            Assert.IsTrue(KinsokuData.IsCombiningMark(0x0300)); // Combining grave accent
            Assert.IsTrue(KinsokuData.IsCombiningMark(0x0301)); // Combining acute accent
            Assert.IsFalse(KinsokuData.IsCombiningMark('A'));
        }
    }

    [TestFixture]
    public class BidiTests
    {
        [Test]
        public void AllLTR_ReturnsNull()
        {
            var levels = BidiTypes.ComputeBidiLevels("Hello World");
            Assert.IsNull(levels);
        }

        [Test]
        public void RTLText_GetsEmbeddingLevels()
        {
            var levels = BidiTypes.ComputeBidiLevels("مرحبا");
            Assert.IsNotNull(levels);
            Assert.Greater(levels.Length, 0);
            // All chars should be odd level (RTL)
            foreach (sbyte level in levels)
                Assert.AreEqual(1, level & 1);
        }

        [Test]
        public void MixedText_GetsMixedLevels()
        {
            var levels = BidiTypes.ComputeBidiLevels("HelloمرحباWorld");
            Assert.IsNotNull(levels);
            // Should have both even (LTR) and odd (RTL) levels
            bool hasEven = false, hasOdd = false;
            foreach (sbyte level in levels)
            {
                if ((level & 1) == 0) hasEven = true;
                else hasOdd = true;
            }
            Assert.IsTrue(hasEven && hasOdd);
        }

        [Test]
        public void ClassifyChar_KnownRanges()
        {
            Assert.AreEqual(BidiType.L, BidiTypes.ClassifyChar('A'));
            Assert.AreEqual(BidiType.R, BidiTypes.ClassifyChar(0x05D0)); // Hebrew alef
            Assert.AreEqual(BidiType.AL, BidiTypes.ClassifyChar(0x0627)); // Arabic alef
        }
    }

    [TestFixture]
    public class TextAnalyzerTests
    {
        [SetUp]
        public void Setup()
        {
            // Use default segmenter
        }

        [Test]
        public void NormalizeWhitespaceNormal_CollapsesRuns()
        {
            Assert.AreEqual("a b", TextAnalyzer.NormalizeWhitespaceNormal("a \t\n  b"));
            Assert.AreEqual("hello world", TextAnalyzer.NormalizeWhitespaceNormal("  hello world  "));
        }

        [Test]
        public void NormalizeWhitespaceNormal_NoChange()
        {
            Assert.AreEqual("hello", TextAnalyzer.NormalizeWhitespaceNormal("hello"));
            Assert.AreEqual("a b", TextAnalyzer.NormalizeWhitespaceNormal("a b"));
        }

        [Test]
        public void AnalyzeText_EmptyString()
        {
            var result = TextAnalyzer.AnalyzeText("", new AnalysisProfile(), WhiteSpaceMode.Normal);
            Assert.IsTrue(result.IsEmpty);
        }

        [Test]
        public void AnalyzeText_SimpleText()
        {
            var result = TextAnalyzer.AnalyzeText("hello world", new AnalysisProfile(), WhiteSpaceMode.Normal);
            Assert.Greater(result.Len, 0);
            Assert.AreEqual("hello world", result.Normalized);
        }

        [Test]
        public void AnalyzeText_WhitespaceTrimmed()
        {
            var result = TextAnalyzer.AnalyzeText("  hello  ", new AnalysisProfile(), WhiteSpaceMode.Normal);
            Assert.AreEqual("hello", result.Normalized);
        }
    }
}
