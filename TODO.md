Current baseline first:

- Main browser regression sweep is green in all three browsers.
- `STATUS.md` is the compact benchmark/accuracy snapshot.
- `corpora/STATUS.md` is the compact long-form corpus snapshot.
- `corpora/TAXONOMY.md` is the shared mismatch vocabulary.

What is already done:

- `prepare()` internals are split across `src/analysis.ts`, `src/measurement.ts`, and `src/bidi.ts`.
- `PreparedText` is opaque; `prepareWithSegments()` remains the rich escape hatch.
- Internal break kinds are richer than a single `isSpace` boolean.
- Segment metrics cache is richer than width-only.
- `setLocale(locale?)` exists and resets the shared analysis state for future `prepare()` calls.
- `layoutWithLines()` now exposes boundary cursors.
- `layoutNextLine()` now exists on the rich path for variable-width, per-row userland layout.
- `pages/demos/dynamic-layout.html` / `pages/demos/dynamic-layout.ts` are now the main rich editorial demo: fixed-height spread, continuous two-column flow, obstacle-aware title routing, live logo-driven reflow, and line-level hover tooling.
- `pages/demos/bubbles.html` / `pages/demos/bubbles.ts` keep the shrinkwrap / `walkLineRanges()` story honest.
- Japanese now has two canaries (`ja-rashomon`, `ja-kumo-no-ito`).
- Chinese now has a real long-form canary (`zh-zhufu`) with an actual font split (`Songti SC` vs `PingFang SC`).
- Chinese now also has a second canary (`zh-guxiang`) showing the same Chrome-positive / Safari-clean split by a different acquisition path.
- Myanmar now has two canaries.
- Urdu (`ur-chughd`) is now a real Nastaliq/Naskh canary.
- Dirty Lao is still rejected and should stay dropped unless acquisition changes.
- `corpus-taxonomy` is now a real Bun script instead of a documented-but-missing command.

Current frontiers:

1. Product-shaped canaries

- Mixed app text still has the extractor-sensitive `710px` soft-hyphen miss.
- Both extractors agree the full-corpus miss is real, but isolated paragraph/slice probes go exact in height.
- This is still one of the best “real app” stress cases because it is not just literary prose.

2. Long-form canaries that are still informative

- Japanese remains a real canary, but it is cleaner than before; the broad field is small and now looks mostly edge-fit rather than a missing obvious rule.
- Chinese is now the clearest active CJK canary. Safari anchors are exact, but Chrome keeps a broader positive one-line field and `PingFang SC` is worse than `Songti SC`.
- The second Chinese text says that field is not a one-story fluke; it is now a real class, mostly around quote / dash cluster glue policy rather than generic shaping mystery.
- Two tempting Chinese follow-ups were already tried and rejected: carrying stranded closing quotes like `」` / `』` forward, and coalescing standalone `——` / `……` runs. Both made `zh-zhufu` worse on the broader Chrome sweep.
- Myanmar remains a real canary. Safari still disagrees with some tempting Chrome-only heuristics.
- Urdu is now a real shaping/context canary, but it is off the active roadmap for now.

3. Font axis

- Chinese is the best current place to broaden the sampled font matrix.
- Japanese still belongs in that bucket, but it is no longer the most urgent CJK target.
- Myanmar font sensitivity is also still informative.

4. Userland line-layout substrate

- Keep improving `layoutWithLines()` only when the demos or real userland uses need more structure.
- Good next demos later:
  - “Old Man and the Sea” resize/reflow
  - richer editorial layouts that build on `dynamic-layout` without handing control back to CSS columns
  - maybe return to a synced multi-view demo later, but only if it justifies its own complexity again

Things not worth doing right now:

- Do not chase universal exactness as the product claim.
- Do not put measurement back in `layout()`.
- Do not resurrect dirty corpora just to cover another language.
- Do not overfit one-line misses in a single browser/corpus without broader evidence.
- Do not explode the public API with cache/engine knobs.
- Do not start broad perf work yet unless a change clearly regresses the hot path.
- Do not replace `Intl.Segmenter` / browser-oriented preprocessing with `text-shaper`'s pure TypeScript segmentation or its greedy glyph-line breaker. Keep that repo as reference material, not the default runtime path.

Best next tasks:

1. Keep broadening canaries only with clean sources.
2. Expand the sampled font matrix where canaries are still imperfect, especially Chinese.
3. Use `dynamic-layout`, `bubbles`, and `demo` to decide what extra rich-path metadata is actually worth keeping.
4. Revisit the mixed-app `710px` miss only if a cleaner paragraph-scale reproducer emerges.

Still-open design questions:

- Whether line-fit tolerance should stay as a browser shim or move toward runtime calibration.
- Whether explicit hard breaks / paragraph-aware layout should become first-class.
- Whether server canvas support should become an explicit supported backend.
- Whether the rich path eventually wants a fuller bidi metadata helper for custom rendering / selection-like work, without changing the hot-path layout architecture.
- Whether automatic hyphenation beyond manual soft hyphen is in scope, and if so whether it should stay entirely preprocess-driven or expose any language/pattern hooks.
- Whether intrinsic sizing / logical width APIs are needed beyond fixed-width height prediction.
- Whether bidi rendering concerns like selection/copy-paste belong here or stay out of scope.
