// CJK line-breaking character sets (kinsoku rules).
// Ported from src/analysis.ts — these are the character sets that cannot start/end a line.

namespace Pretext
{
    /// <summary>
    /// Static CJK and punctuation character sets for line-breaking rules.
    /// Used internally by TextAnalyzer and PretextCore.
    /// </summary>
    internal static class KinsokuData
    {
        /// <summary>
        /// Characters that cannot start a line (must stay with the preceding character).
        /// Includes: full-width punctuation, CJK comma/period, Japanese iteration marks.
        /// </summary>
        public static readonly HashSet<int> KinsokuStart = new()
        {
            0xFF0C, // Full-width comma ，
            0xFF0E, // Full-width period ．
            0xFF01, // Full-width exclamation ！
            0xFF1A, // Full-width colon ：
            0xFF1B, // Full-width semicolon ；
            0xFF1F, // Full-width question mark ？
            0x3001, // CJK comma 、.
            0x3002, // CJK period 。
            0x30FB, // CJK middle dot ・
            0xFF09, // Full-width right parenthesis ）
            0x3015, // Right tortoise shell bracket 」
            0x3009, // Right angle bracket 》
            0x300B, // Right double angle bracket 》
            0x300D, // Right corner bracket 』
            0x300F, // Right white corner bracket 』
            0x3011, // Right black lenticular bracket 】
            0x3017, // Right white lenticular bracket 』
            0x3019, // Right white tortoise shell bracket 』
            0x301B, // Right white square bracket 』
            0x30FC, // Hiragana/Katakana-hiragana mid-dot ー
            0x3005, // Ideographic iteration mark 々
            0x303B, // Ideographic variation indicator 〻
            0x309D, // Hiragana iteration mark ゞ
            0x309E, // Hiragana voiced iteration mark ゟ
            0x30FD, // Katakana iteration mark ヽ
            0x30FE, // Katakana voiced iteration mark ヾ
        };

        /// <summary>
        /// Characters that cannot end a line (must stay with the following character).
        /// Includes: opening quotes, parentheses, brackets, and CJK opening brackets.
        /// </summary>
        public static readonly HashSet<int> KinsokuEnd = new()
        {
            '"',   // Left double quote (ASCII)
            '(',   // Left parenthesis
            '[',   // Left bracket
            '{',   // Left brace
            0x201C, // Left double quotation mark "
            0x2018, // Left single quotation mark '
            0x00AB, // Left guillemet «
            0x2039, // Single left-pointing angle quotation mark ‹
            0xFF08, // Full-width left parenthesis （
            0x3014, // Left tortoise shell bracket 【
            0x3008, // Left angle bracket 〈
            0x300A, // Left double angle bracket 《
            0x300C, // Left corner bracket 「
            0x300E, // Left white corner bracket 『
            0x3010, // Left black lenticular bracket 【
            0x3016, // Left white lenticular bracket 『
            0x3018, // Left white tortoise shell bracket 『
            0x301A, // Left white square bracket 『
        };

        /// <summary>
        /// Punctuation that cannot be left alone at the start of a line.
        /// When attached to a word, the punctuation stays with the word.
        /// </summary>
        public static readonly HashSet<int> LeftStickyPunctuation = new()
        {
            '.', ',', '!', '?', ':', ';',
            0x060C, // Arabic comma ،
            0x061B, // Arabic semicolon ؛
            0x061F, // Arabic question mark ؟
            0x0964, // Devanagari danda |
            0x0965, // Devanagari double danda ||
            0x104A, // Myanmar sign little section ၊
            0x104B, // Myanmar sign section ။
            0x104C, // Myanmar symbol westward arrow ၌
            0x104D, // Myanmar symbol sawan ၍
            0x104F, // Myanmar symbol htone hakan ၏
            ')', ']', '}',
            '%',
            0x201D, // Right double quotation mark "
            0x2019, // Right single quotation mark '
            0x00BB, // Right guillemet »
            0x203A, // Single right-pointing angle quotation mark ›
            0x2026, // Ellipsis …
        };

        /// <summary>
        /// Characters that cannot be separated from the following text.
        /// Forward-sticky clusters are grouped and break as a unit.
        /// </summary>
        public static readonly HashSet<int> ForwardStickyGlue = new()
        {
            '\'', // Left single quote (ASCII)
            0x2019, // Right single quotation mark ' (apostrophe style)
        };

        /// <summary>
        /// Closing quote characters (used for CJK carry-after-closing-quote logic).
        /// </summary>
        public static readonly HashSet<int> ClosingQuoteChars = new()
        {
            0x201D, // Right double quotation mark "
            0x2019, // Right single quotation mark '
            0x00BB, // Right guillemet »
            0x203A, // Single right-pointing angle quotation mark ›
            0x300D, // Right corner bracket 』
            0x300F, // Right white corner bracket 』
            0x3011, // Right black lenticular bracket 】
            0x300B, // Right double angle bracket 》
            0x3009, // Right angle bracket 》
            0x3015, // Right tortoise shell bracket 』
            0xFF09, // Full-width right parenthesis ）
        };

        /// <summary>
        /// Arabic trailing punctuation that should not have a space before the next word.
        /// </summary>
        public static readonly HashSet<int> ArabicNoSpaceTrailingPunctuation = new()
        {
            ':', '.',
            0x060C, // Arabic comma ،
            0x061B, // Arabic semicolon ؛
        };

        /// <summary>
        /// Myanmar medial glue characters.
        /// </summary>
        public static readonly HashSet<int> MyanmarMedialGlue = new()
        {
            0x104F, // Myanmar symbol htone hakan ၏
        };

        /// <summary>
        /// Returns true if the given Unicode code point is in the CJK ranges.
        /// Includes: CJK Unified Ideographs, Extensions A-F, Compatibility Ideographs,
        /// Japanese Hiragana/Katakana, Korean Hangul, and full-width ASCII.
        /// </summary>
        public static bool IsCJK(int codePoint)
        {
            return
                (codePoint >= 0x4E00 && codePoint <= 0x9FFF) || // CJK Unified Ideographs
                (codePoint >= 0x3400 && codePoint <= 0x4DBF) || // CJK Extension A
                (codePoint >= 0x20000 && codePoint <= 0x2A6DF) || // CJK Extension B-F
                (codePoint >= 0x2A700 && codePoint <= 0x2B73F) || // CJK Extension G
                (codePoint >= 0x2B740 && codePoint <= 0x2B81F) || // CJK Extension H
                (codePoint >= 0x2B820 && codePoint <= 0x2CEAF) || // CJK Extension I
                (codePoint >= 0x2CEB0 && codePoint <= 0x2EBEF) || // CJK Extension J
                (codePoint >= 0x30000 && codePoint <= 0x3134F) || // CJK Extension K
                (codePoint >= 0xF900 && codePoint <= 0xFAFF) || // CJK Compatibility Ideographs
                (codePoint >= 0x2F800 && codePoint <= 0x2FA1F) || // CJK Compatibility Ideographs Supplement
                (codePoint >= 0x3000 && codePoint <= 0x303F) || // CJK Symbols and Punctuation
                (codePoint >= 0x3040 && codePoint <= 0x309F) || // Hiragana
                (codePoint >= 0x30A0 && codePoint <= 0x30FF) || // Katakana
                (codePoint >= 0xAC00 && codePoint <= 0xD7AF) || // Hangul Syllables
                (codePoint >= 0xFF00 && codePoint <= 0xFFEF);   // Full-width Forms
        }

        /// <summary>
        /// Returns true if the code point is a decimal digit (\p{Nd}).
        /// </summary>
        public static bool IsDecimalDigit(int codePoint)
        {
            return codePoint >= '0' && codePoint <= '9';
        }

        /// <summary>
        /// Returns true if the code point is a Unicode combining mark (\p{M}).
        /// </summary>
        public static bool IsCombiningMark(int codePoint)
        {
            // Simplified: combining marks are in ranges 0x0300-0x036F, 0x1DC0-0x1DFF, etc.
            // This covers most common combining marks used in Western and Arabic text.
            return
                (codePoint >= 0x0300 && codePoint <= 0x036F) || // Combining Diacritical Marks
                (codePoint >= 0x1AB0 && codePoint <= 0x1AFF) || // Combining Diacritical Marks Extended
                (codePoint >= 0x1DC0 && codePoint <= 0x1DFF) || // Combining Diacritical Marks Supplement
                (codePoint >= 0xFE20 && codePoint <= 0xFE2F);   // Combining Half Marks
        }
    }
}
