# Current Status

Compact current snapshot for the main browser sweep and benchmark numbers.

Use this file for "where are we right now?".
Use `RESEARCH.md` for why the numbers changed and what was tried.
Use `corpora/STATUS.md` for the long-form corpus canaries.

## Browser Accuracy

Official browser regression sweep:

| Browser | Status |
|---|---|
| Chrome | `7680/7680` |
| Safari | `7680/7680` |
| Firefox | `7680/7680` |

Notes:
- This is the 4-font × 8-size × 8-width × 30-text browser corpus.
- The public accuracy page is effectively a regression gate now, not the main steering metric.

## Benchmark Snapshot

Current benchmark snapshots are checked in as:
- `benchmarks/chrome.json`
- `benchmarks/safari.json`

`STATUS.md` stays the compact dashboard. The JSON files are the machine-readable source for the current benchmark numbers.
These snapshot files were captured on `2026-03-26`.

### Top-level batch

| Browser | `prepare()` | `layout()` | DOM batch | DOM interleaved |
|---|---:|---:|---:|---:|
| Chrome | `17.95ms` | `0.11ms` | `5.30ms` | `56.25ms` |
| Safari | `18.50ms` | `0.23ms` | `104.00ms` | `180.50ms` |

### Long-form corpus stress (Chrome baseline)

| Corpus | analyze() | measure() | prepare() | layout() | segs (analyze→prepared) | lines @ 300px |
|---|---:|---:|---:|---:|---:|---:|
| Japanese prose (story 2) | `5.70ms` | `8.40ms` | `14.00ms` | `0.03ms` | `1,773→2,667` | `193` |
| Japanese prose | `4.40ms` | `10.30ms` | `15.70ms` | `0.04ms` | `3,606→5,044` | `380` |
| Korean prose | `2.80ms` | `10.50ms` | `13.90ms` | `0.06ms` | `5,282→9,679` | `428` |
| Chinese prose | `9.80ms` | `19.20ms` | `27.50ms` | `0.07ms` | `5,433→7,949` | `626` |
| Chinese prose (story 2) | `4.20ms` | `11.70ms` | `17.30ms` | `0.04ms` | `3,271→4,745` | `375` |
| Thai prose | `14.80ms` | `9.90ms` | `26.80ms` | `0.06ms` | `10,281→10,281` | `1,024` |
| Myanmar prose | `0.70ms` | `1.00ms` | `1.70ms` | `0.01ms` | `797→797` | `81` |
| Myanmar prose (story 2) | `0.40ms` | `0.90ms` | `1.30ms` | `<0.01ms` | `498→498` | `54` |
| Urdu prose | `2.70ms` | `4.70ms` | `7.40ms` | `0.03ms` | `6,051→6,051` | `351` |
| Khmer prose | `6.20ms` | `5.60ms` | `11.80ms` | `0.06ms` | `11,109→11,109` | `591` |
| Hindi prose | `4.30ms` | `8.50ms` | `13.80ms` | `0.05ms` | `9,958→9,958` | `653` |
| Arabic prose | `49.50ms` | `46.50ms` | `97.50ms` | `0.21ms` | `37,603→37,603` | `2,643` |

Notes:
- Chrome remains the main maintained performance baseline. Safari snapshots are still useful, but they are noisier and warm up less predictably.
- The checked-in JSON snapshots are cold checker runs. Ad hoc page-driven numbers, especially in Safari, can differ after warmup.
- Refresh the benchmark JSON snapshots when a diff changes benchmark methodology or the text engine's hot path (`src/analysis.ts`, `src/measurement.ts`, `src/line-break.ts`, `src/layout.ts`, `src/bidi.ts`, or `pages/benchmark.ts`).
- `layout()` remains the resize hot path; `prepare()` is where script-specific cost still lives.
- Long-form corpus rows now split `prepare()` into analysis and measurement phases, which makes it easier to tell whether a script is expensive because of segmentation/glue work or because of raw width measurement volume.

## Pointers

- Current machine-readable benchmark snapshots: `benchmarks/chrome.json`, `benchmarks/safari.json`
- Long-form corpus canary status: `corpora/STATUS.md`
- Full exploration log: `RESEARCH.md`
