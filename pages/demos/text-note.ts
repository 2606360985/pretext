import {
  prepareWithSegments,
  layoutWithLines,
} from '../../src/layout.ts';

const FONT = '17px "Georgia", "Noto Serif SC", serif';
const LINE_HEIGHT = 1.75;
const LINE_HEIGHT_PX = 17 * LINE_HEIGHT;
const CONTAINER_WIDTH = 700; // approximate note width in px
const CARD_PADDING_TOP = 20;
const CARD_PADDING_BOTTOM = 14; // margin-bottom

let noteIndex = 0;
let flyingTexts: FlyingText[] = [];

const notesContainer = document.getElementById('notes-container')!;
const textInput = document.getElementById('text-input') as HTMLTextAreaElement;
const submitBtn = document.getElementById('submit-btn') as HTMLButtonElement;
const emptyState = document.getElementById('empty-state')!;

// ── Pretext measurement ──────────────────────────────────────────────────────

function measureTextLines(text: string): number {
  const prepared = prepareWithSegments(text, FONT);
  const { lines } = layoutWithLines(prepared, CONTAINER_WIDTH, LINE_HEIGHT_PX);
  return lines.length;
}

function getNoteHeight(text: string): number {
  const lineCount = measureTextLines(text);
  // 2 lines of padding (top+bottom) + line height * count
  const textHeight = lineCount * LINE_HEIGHT_PX;
  return CARD_PADDING_TOP + textHeight + CARD_PADDING_BOTTOM;
}

// ── Flying text ─────────────────────────────────────────────────────────────

class FlyingText {
  el: HTMLDivElement;
  startX: number;
  startY: number;
  endX: number;
  endY: number;
  startTime: number;
  duration: number;
  text: string;
  progress = 0;
  private raf = 0;

  // slight per-note personality
  private ease = this.randomEase();

  constructor(text: string, startX: number, startY: number, endX: number, endY: number) {
    this.text = text;
    this.startX = startX;
    this.startY = startY;
    this.endX = endX;
    this.endY = endY;
    this.startTime = performance.now();
    // farther = slightly longer, but cap between 500–900ms
    const dist = Math.hypot(endX - startX, endY - startY);
    this.duration = Math.min(900, Math.max(500, dist * 1.2));

    this.el = document.createElement('div');
    this.el.className = 'flying-text';
    this.el.textContent = text;
    this.el.style.left = `${startX}px`;
    this.el.style.top = `${startY}px`;
    document.body.appendChild(this.el);

    this.tick();
  }

  private randomEase(): (t: number) => number {
    // pick one of a few easing curves for variety
    const curves: Array<(t: number) => number> = [
      // cubic-out
      (t) => 1 - Math.pow(1 - t, 3),
      // quadratic-out
      (t) => 1 - (1 - t) * (1 - t),
      // quintic-out
      (t) => 1 - Math.pow(1 - t, 5),
      // back-out (slight overshoot)
      (t) => {
        const c1 = 1.70158;
        const c3 = c1 + 1;
        return 1 + c3 * Math.pow(t - 1, 3) + c1 * Math.pow(t - 1, 2);
      },
    ];
    return curves[Math.floor(Math.random() * curves.length)] as (t: number) => number;
  }

  tick = () => {
    const elapsed = performance.now() - this.startTime;
    this.progress = Math.min(1, elapsed / this.duration);

    const e = this.ease(this.progress);

    const x = this.startX + (this.endX - this.startX) * e;
    const y = this.startY + (this.endY - this.startY) * e;

    // subtle scale: starts at 0.9, peaks at ~1.05 mid-flight, back to 1
    const scalePeak = 1 + 0.05 * Math.sin(this.progress * Math.PI);
    const scale = 0.9 + 0.1 * e * scalePeak;

    // fade: full opacity mid-flight, slight fade at end
    const opacity = this.progress < 0.8
      ? 1
      : 1 - (this.progress - 0.8) / 0.2;

    // glow pulse: peaks mid-flight
    const glow = 30 + 20 * Math.sin(this.progress * Math.PI);

    this.el.style.transform = `translate(0, 0) scale(${scale})`;
    this.el.style.left = `${x}px`;
    this.el.style.top = `${y}px`;
    this.el.style.opacity = `${opacity}`;
    this.el.style.textShadow = `0 0 ${glow}px rgba(124, 106, 255, 0.6)`;

    if (this.progress < 1) {
      this.raf = requestAnimationFrame(this.tick);
    } else {
      this.destroy();
    }
  };

  destroy() {
    cancelAnimationFrame(this.raf);
    this.el.remove();
    const idx = flyingTexts.indexOf(this);
    if (idx !== -1) flyingTexts.splice(idx, 1);
  }
}

// ── Note card ───────────────────────────────────────────────────────────────

function createNoteCard(text: string, index: number): HTMLElement {
  const card = document.createElement('div');
  card.className = 'note-card just-landed';
  card.innerHTML = `
    <div class="note-meta">#${String(index).padStart(3, '0')}</div>
    <div class="note-text">${escapeHtml(text)}</div>
  `;

  // remove animation class after it plays
  card.addEventListener('animationend', () => {
    card.classList.remove('just-landed');
    card.classList.add('landed');
  }, { once: true });

  return card;
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

// ── Submit logic ────────────────────────────────────────────────────────────

function submitNote() {
  const text = textInput.value.trim();
  if (!text) return;

  noteIndex++;

  // hide empty state
  emptyState.style.display = 'none';

  // ── Pretext: predict where the new note will land ───────────────────
  // High-performance: getNoteHeight() uses canvas measurement, no DOM reads
  void getNoteHeight(text); // called to warm up / verify font readiness

  // Total height of existing notes above the new one
  let accumulatedHeight = 0;
  for (const child of notesContainer.children) {
    if (child === emptyState) continue;
    accumulatedHeight += (child as HTMLElement).offsetHeight;
  }

  // Target position: bottom of existing notes + container's padding-top
  const containerRect = notesContainer.getBoundingClientRect();
  const containerScrollTop = notesContainer.scrollTop;
  const targetY = containerRect.top - containerScrollTop + accumulatedHeight + 32; // 32px padding

  // Starting position: near the input area
  const inputRect = textInput.getBoundingClientRect();
  const startX = inputRect.left;
  const startY = inputRect.top + inputRect.height / 2 - 9; // approx center of first line

  // End position: center the text horizontally in the note area
  const endX = containerRect.left + 24; // 24px card padding
  const endY = targetY;

  // ── Launch flying text ────────────────────────────────────────────────
  const flying = new FlyingText(text, startX, startY, endX, endY);
  flyingTexts.push(flying);

  // ── After animation ~65% complete, add the note card ─────────────────
  const cardDelay = (flying.duration * 0.65) | 0;
  setTimeout(() => {
    const card = createNoteCard(text, noteIndex);
    notesContainer.appendChild(card);

    // scroll into view smoothly
    setTimeout(() => {
      card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }, 50);
  }, cardDelay);

  // ── Clear input ───────────────────────────────────────────────────────
  textInput.value = '';
  textInput.style.height = 'auto';
  textInput.rows = 1;
  submitBtn.disabled = true;
}

// ── Input handling ──────────────────────────────────────────────────────────

function autoGrow(el: HTMLTextAreaElement) {
  el.style.height = 'auto';
  el.style.height = `${el.scrollHeight}px`;
  submitBtn.disabled = el.value.trim() === '';
}

textInput.addEventListener('input', () => autoGrow(textInput));

textInput.addEventListener('keydown', (e: KeyboardEvent) => {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    if (textInput.value.trim()) {
      submitNote();
    }
  }
});

submitBtn.addEventListener('click', () => {
  if (textInput.value.trim()) {
    submitNote();
  }
});

// ── Init ────────────────────────────────────────────────────────────────────

document.fonts.ready.then(() => {
  textInput.focus();
});
