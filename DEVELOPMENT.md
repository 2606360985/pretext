## Development Setup

```sh
bun install
bun start        # http://localhost:3000 — demo pages with full code reload (clears stale :3000 listeners first)
bun run check    # typecheck + lint
bun test         # lightweight invariants against the shipped implementation
bun run accuracy-check         # Chrome browser sweep
bun run accuracy-check:safari  # Safari browser sweep
bun run accuracy-check:firefox # Firefox browser sweep
bun run benchmark-check        # Chrome benchmark snapshot (short corpus + long-form corpora)
bun run corpus-font-matrix --id=ar-risalat-al-ghufran-part-1 --samples=5  # sampled cross-font corpus check
```

Pages:
- `/demos/dynamic-layout` — fixed-height editorial spread with two-column flow, obstacle avoidance, live logo-driven reflow
- `/demos/bubbles` — bubble shrinkwrap demo
- `/accuracy` — sweep across fonts, sizes, widths, i18n texts
- `/benchmark` — performance comparison
- `/emoji-test` — canvas vs DOM emoji width sweep
- `/corpus` — long-form corpora + diagnostics (`font=` / `lineHeight=` query params supported)

## Research

See [RESEARCH.md](RESEARCH.md) for the full exploration log: every approach we tried, benchmarks, the system-ui font discovery, punctuation accumulation error analysis, emoji width tables, HarfBuzz RTL bug, server-side engine comparison, and what Sebastian already knew.

## Current Statuses

See:
- [STATUS.md](STATUS.md) for the current compact benchmark snapshot
- [benchmarks/chrome.json](benchmarks/chrome.json) and [benchmarks/safari.json](benchmarks/safari.json) for the checked-in current benchmark snapshots
- [pages/benchmark.ts](pages/benchmark.ts) for the live benchmark harness

## Accuracy

Tested across 4 fonts × 8 sizes × 8 widths × 30 i18n texts (7680 tests):

| Browser | Match rate | Tests | Remaining mismatches |
|---|---|---|---|
| Chrome | 100.00% | 7680 | None on the current browser sweep |
| Safari | 100.00% | 7680 | None on the current browser sweep |
| Firefox | 100.00% | 7680 | None on the current browser sweep |

Tested across 4 fonts (Helvetica Neue, Georgia, Verdana, Courier New) × 8 sizes × 8 widths × 30 i18n texts. See [STATUS.md](STATUS.md) for the compact current snapshot, [corpora/STATUS.md](corpora/STATUS.md) for the long-form corpus canaries, and [RESEARCH.md](RESEARCH.md) for the exploration log.

## i18n

- **Line breaking**: `Intl.Segmenter` with `granularity: 'word'` handles CJK (per-character breaks), Thai, Arabic, and all scripts the browser supports.
- **Bidi metadata**: the rich `prepareWithSegments()` path can attach simplified UAX #9-style embedding levels for mixed LTR/RTL custom rendering. Line breaking itself does not consume those levels, and pure LTR text still fast-paths with zero extra bidi work.
- **Shaping**: canvas `measureText()` uses the browser's font engine, so ligatures, kerning, and contextual forms (Arabic connected letters) are handled correctly.
- **Emoji**: auto-corrected. Chrome/Firefox canvas inflates emoji widths at small font sizes on macOS; the library detects and compensates automatically.

## Known limitations

- **CSS config**: targets a common app-text configuration (`white-space: normal`, `word-break: normal`, `overflow-wrap: break-word`, `line-break: auto`). Source newlines are treated as collapsible whitespace, not explicit `<br>`/paragraph breaks. Other configurations (`break-all`, `keep-all`, `strict`, `loose`, `anywhere`) are untested.
- **In-word breaks**: because the default target includes `overflow-wrap: break-word`, very narrow widths may break inside words, but only at grapheme boundaries. The engine will not cut through raw UTF-16/code-unit boundaries or split an emoji cluster in half.
- **`line-height`**: the library does not infer CSS line height. Pass the exact value you render with into `layout()` / `layoutWithLines()`. `line-height: normal` differs across fonts and browsers.
- **Soft hyphens**: unbroken soft hyphens stay invisible, but if the engine chooses that break, the rich APIs expose a visible trailing `-` in the rendered line text.
- **`system-ui` font**: canvas and DOM resolve this CSS keyword to different font variants at certain sizes on macOS. Use a named font (Inter, Helvetica, Arial, etc.) for guaranteed accuracy. See [RESEARCH.md](RESEARCH.md#discovery-system-ui-font-resolution-mismatch).
- **Server-side**: importing the module is now safe in non-DOM runtimes, but actual server-side measurement is still not zero-config. Calling `prepare()` without `OffscreenCanvas` or a DOM canvas path will still need an explicit canvas-backed backend. We keep a HarfBuzz (WASM) backend around for headless probes and research.

## How it works

1. **Text analysis**: normalize collapsible whitespace, segment with `Intl.Segmenter('word')`, merge punctuation, and carry opening punctuation forward so browser break opportunities are modeled more closely.
2. **CJK splitting + kinsoku**: CJK word segments are re-split into individual graphemes, since CSS allows line breaks between any CJK characters. Kinsoku shori rules keep CJK punctuation (，。「」 etc.) attached to their adjacent characters so they can't be separated across line breaks.
3. **Measurement + caching**: each final segment is measured via canvas `measureText()` and cached in a per-font segment-metrics cache. Common words across texts share not just widths but lazily-derived segment data such as grapheme widths for breakable words. The cache has no eviction — it grows monotonically per font string. For a typical single-font comment feed this is a few KB; `clearCache()` exists for manual eviction if needed.
4. **Emoji correction**: canvas `measureText` inflates emoji widths on Chrome/Firefox at font sizes <24px on macOS. Auto-detected by measuring a reference emoji; correction subtracted per emoji grapheme. Safari is unaffected (correction = 0).
5. **Bidi metadata**: on the rich path, characters can be classified into bidi types and mapped to approximate embedding levels for custom rendering. Pure LTR text skips this entirely, and the line breaker itself does not read those levels.
6. **Layout** (per resize): walk the cached widths, accumulate per line, break when exceeding `maxWidth`. Trailing whitespace hangs past the edge (CSS behavior). Non-space overflow (words, emoji, punctuation) triggers a line break. Segments wider than `maxWidth` are broken at grapheme boundaries.
