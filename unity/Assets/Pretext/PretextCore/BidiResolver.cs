// Simplified Unicode Bidirectional Algorithm (UBA) for custom text rendering.
// Ported from src/bidi.ts — forked from pdf.js via Sebastian's text-layout.
// Used only for the rich prepareWithSegments() path to compute embedding levels
// for mixed LTR/RTL rendering. The line-breaking engine does not consume these levels.

namespace Pretext
{
    /// <summary>
    /// Bidirectional character types per Unicode Bidirectional Algorithm.
    /// </summary>
    internal enum BidiType : byte
    {
        L,   // Left-to-Right
        R,   // Right-to-Left
        AL,  // Arabic Letter (treated as R)
        AN,  // Arabic Number
        EN,  // European Number (digit)
        ES,  // European Separator
        ET,  // European Terminator
        CS,  // Common Separator
        ON,  // Other Neutral
        BN,  // Boundary Neutral
        B,   // Paragraph Separator
        S,   // Segment Separator
        WS,  // White Space
        NSM, // Non-Spacing Mark
    }

    /// <summary>
    /// Bidi character type lookup tables and classification.
    /// </summary>
    internal static class BidiTypes
    {
        // baseTypes[charCode] for charCode in 0x00..0xFF
        // Ported from bidi.ts baseTypes array
        private static readonly BidiType[] BaseTypes = new BidiType[256]
        {
            BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, // 0x00-0x07
            BidiType.BN, BidiType.S,  BidiType.B,  BidiType.S,  BidiType.WS, BidiType.BN, BidiType.BN, BidiType.BN, // 0x08-0x0F
            BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, // 0x10-0x17
            BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, // 0x18-0x1F
            BidiType.BN, BidiType.BN, BidiType.B,  BidiType.B,  BidiType.B,  BidiType.S,  BidiType.WS, BidiType.ON, // 0x20-0x27
            BidiType.ON, BidiType.ET, BidiType.ET, BidiType.ET, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, // 0x28-0x2F
            BidiType.ON, BidiType.CS, BidiType.ON, BidiType.CS, BidiType.ON, BidiType.EN, BidiType.EN, BidiType.EN, // 0x30-0x37
            BidiType.EN, BidiType.EN, BidiType.EN, BidiType.EN, BidiType.EN, BidiType.EN, BidiType.EN, BidiType.ON, // 0x38-0x3F
            BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.L,  // 0x40-0x47
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0x48-0x4F
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0x50-0x57
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, // 0x58-0x5F
            BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.L,  // 0x60-0x67
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0x68-0x6F
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0x70-0x77
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.BN, // 0x78-0x7F
            BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, // 0x80-0x87
            BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, // 0x88-0x8F
            BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, // 0x90-0x97
            BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, BidiType.BN, // 0x98-0x9F
            BidiType.CS, BidiType.ON, BidiType.ET, BidiType.ET, BidiType.ET, BidiType.ET, BidiType.ON, BidiType.ON, // 0xA0-0xA7
            BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.L,  BidiType.ON, BidiType.ON, BidiType.ON, // 0xA8-0xAF
            BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ET, BidiType.ET, BidiType.EN, BidiType.EN, // 0xB0-0xB7
            BidiType.ON, BidiType.L,  BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.ON, BidiType.L,  // 0xB8-0xBF
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0xC0-0xC7
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0xC8-0xCF
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.ON, // 0xD0-0xD7
            BidiType.L,  BidiType.ON, BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0xD8-0xDF
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0xE0-0xE7
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0xE8-0xEF
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.ON, BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0xF0-0xF7
            BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  BidiType.L,  // 0xF8-0xFF
        };

        // arabicTypes[charCode & 0xFF] for charCode in 0x0600..0x06FF
        private static readonly BidiType[] ArabicTypes = new BidiType[256]
        {
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0600-0x0607
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0608-0x060F
            BidiType.CS, BidiType.AL, BidiType.ON, BidiType.ON, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, // 0x0610-0x0617
            BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.AL, BidiType.AL, // 0x0618-0x061F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0620-0x0627
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0628-0x062F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0630-0x0637
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0638-0x063F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0640-0x0647
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0648-0x064F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0650-0x0657
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0658-0x065F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0660-0x0667
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0668-0x066F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, // 0x0670-0x0677
            BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.NSM, BidiType.AL, // 0x0678-0x067F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0680-0x0687
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0688-0x068F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0690-0x0697
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x0698-0x069F
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06A0-0x06A7
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06A8-0x06AF
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06B0-0x06B7
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06B8-0x06BF
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06C0-0x06C7
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06C8-0x06CF
            BidiType.AL, BidiType.AL, BidiType.AN, BidiType.AN, BidiType.AN, BidiType.AN, BidiType.AN, BidiType.AN, // 0x06D0-0x06D7
            BidiType.AN, BidiType.AN, BidiType.AN, BidiType.AN, BidiType.ET, BidiType.AN, BidiType.AN, BidiType.AL, // 0x06D8-0x06DF
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.NSM, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06E0-0x06E7
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06E8-0x06EF
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06F0-0x06F7
            BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, BidiType.AL, // 0x06F8-0x06FF
        };

        /// <summary>
        /// Classify a single Unicode code point into its Bidi type.
        /// </summary>
        public static BidiType ClassifyChar(int charCode)
        {
            if (charCode <= 0x00FF) return BaseTypes[charCode];
            if (charCode >= 0x0590 && charCode <= 0x05F4) return BidiType.R; // Hebrew
            if (charCode >= 0x0600 && charCode <= 0x06FF) return ArabicTypes[charCode & 0xFF];
            if (charCode >= 0x0700 && charCode <= 0x08AC) return BidiType.AL; // Syriac/NKo
            return BidiType.L;
        }

        /// <summary>
        /// Compute embedding levels for the given string using the Unicode Bidirectional Algorithm.
        /// Returns null if the text has no bidirectional content (all L).
        /// </summary>
        public static sbyte[]? ComputeBidiLevels(string str)
        {
            int len = str.Length;
            if (len == 0) return null;

            var types = new BidiType[len];
            int numBidi = 0;

            for (int i = 0; i < len; i++)
            {
                var t = ClassifyChar(char.ConvertToUtf32(str, i));
                if (t == BidiType.R || t == BidiType.AL || t == BidiType.AN) numBidi++;
                types[i] = t;
            }

            if (numBidi == 0) return null;

            // Determine base level: if >30% of chars are bidi, use RTL
            int startLevel = ((float)numBidi / len) < 0.3f ? (sbyte)0 : (sbyte)1;
            var levels = new sbyte[len];
            for (int i = 0; i < len; i++) levels[i] = startLevel;

            BidiType e = (startLevel & 1) == 1 ? BidiType.R : BidiType.L;
            BidiType sor = e;

            // W1: NSM takes direction from preceding character
            BidiType lastType = sor;
            for (int i = 0; i < len; i++)
            {
                if (types[i] == BidiType.NSM) types[i] = lastType;
                else lastType = types[i];
            }

            // W2: EN takes direction from preceding strong char
            lastType = sor;
            for (int i = 0; i < len; i++)
            {
                var t = types[i];
                if (t == BidiType.EN) types[i] = lastType == BidiType.AL ? BidiType.AN : BidiType.EN;
                else if (t == BidiType.R || t == BidiType.L || t == BidiType.AL) lastType = t;
            }

            // W3: AL becomes R
            for (int i = 0; i < len; i++)
            {
                if (types[i] == BidiType.AL) types[i] = BidiType.R;
            }

            // W4: ES/CS resolve between EN/EN and EN/AN
            for (int i = 1; i < len - 1; i++)
            {
                if (types[i] == BidiType.ES && types[i - 1] == BidiType.EN && types[i + 1] == BidiType.EN)
                    types[i] = BidiType.EN;
                if (types[i] == BidiType.CS &&
                    (types[i - 1] == BidiType.EN || types[i - 1] == BidiType.AN) &&
                    types[i + 1] == types[i - 1])
                    types[i] = types[i - 1];
            }

            // W5: ET sequences become EN
            for (int i = 0; i < len; i++)
            {
                if (types[i] != BidiType.EN) continue;
                for (int j = i - 1; j >= 0 && types[j] == BidiType.ET; j--) types[j] = BidiType.EN;
                for (int j = i + 1; j < len && types[j] == BidiType.ET; j++) types[j] = BidiType.EN;
            }

            // W6: Other separators become ON
            for (int i = 0; i < len; i++)
            {
                var t = types[i];
                if (t == BidiType.WS || t == BidiType.ES || t == BidiType.ET || t == BidiType.CS)
                    types[i] = BidiType.ON;
            }

            // W7: EN takes direction from preceding L
            lastType = sor;
            for (int i = 0; i < len; i++)
            {
                var t = types[i];
                if (t == BidiType.EN) types[i] = lastType == BidiType.L ? BidiType.L : BidiType.EN;
                else if (t == BidiType.R || t == BidiType.L) lastType = t;
            }

            // N1-N2: Neutrals resolve to surrounding strong direction
            for (int i = 0; i < len; i++)
            {
                if (types[i] != BidiType.ON) continue;
                int end = i + 1;
                while (end < len && types[end] == BidiType.ON) end++;
                BidiType before = i > 0 ? types[i - 1] : sor;
                BidiType after = end < len ? types[end] : sor;
                BidiType bDir = before != BidiType.L ? BidiType.R : BidiType.L;
                BidiType aDir = after != BidiType.L ? BidiType.R : BidiType.L;
                if (bDir == aDir)
                {
                    for (int j = i; j < end; j++) types[j] = bDir;
                }
                i = end - 1;
            }

            // N2: Remaining ON resolve to embedding direction
            for (int i = 0; i < len; i++)
            {
                if (types[i] == BidiType.ON) types[i] = e;
            }

            // I1-I2: Compute embedding levels
            for (int i = 0; i < len; i++)
            {
                var t = types[i];
                if ((levels[i] & 1) == 0)
                {
                    if (t == BidiType.R) levels[i]++;
                    else if (t == BidiType.AN || t == BidiType.EN) levels[i] += 2;
                }
                else
                {
                    if (t == BidiType.L || t == BidiType.AN || t == BidiType.EN) levels[i]++;
                }
            }

            return levels;
        }

        /// <summary>
        /// Compute bidi embedding levels for segment start positions in the normalized text.
        /// Returns null if the text is all LTR.
        /// </summary>
        public static sbyte[]? ComputeSegmentLevels(string normalized, int[] segStarts)
        {
            var bidiLevels = ComputeBidiLevels(normalized);
            if (bidiLevels == null) return null;

            var segLevels = new sbyte[segStarts.Length];
            for (int i = 0; i < segStarts.Length; i++)
            {
                int pos = segStarts[i];
                if (pos < bidiLevels.Length)
                    segLevels[i] = bidiLevels[pos];
            }
            return segLevels;
        }
    }
}
