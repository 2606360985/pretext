import {
  prepareWithSegments,
  layoutNextLine,
  type PreparedTextWithSegments,
  type LayoutCursor,
} from '../../src/layout.ts'

// ---------------------------------------------------------------------------
// Content — Chinese text introducing Pretext's innovations
// ---------------------------------------------------------------------------

const textContent =
  'Pretext 是一个纯 JavaScript 文本排版引擎。它彻底绕过了浏览器 DOM 回流这一性能瓶颈，用 Canvas measureText 实现毫秒级的文本高度预测。' +
  '传统网页中，每次调用 getBoundingClientRect 或读取 offsetHeight，都会触发昂贵的同步布局回流。' +
  '当页面有数百个文本块时，这种开销每帧可超过三十毫秒。' +
  'Pretext 将文本测量分解为两个阶段：prepare() 完成一次性文本分析——分词、断行规则、Canvas 测量，返回预计算句柄；layout() 则是纯算术运算，五百段文本仅需零点零九毫秒完成重排。' +
  '窗口缩放时即时重排，告别布局抖动。聊天消息精确高度预测，无需猜测与缓存。虚拟列表真实行高计算，不依赖 DOM。' +
  '它支持所有语言——中文逐字符断行、阿拉伯文双向排版、泰文复杂断词、emoji 精确宽度校正。三大浏览器精度满分，七千六百八十项测试全部通过。' +
  '你现在看到的，正是它最强大的能力：layoutNextLine() 接口让每行文本拥有不同的可用宽度。文字正实时绕开你的笔触流淌——这是纯 CSS 永远做不到的效果。' +
  '拿起你的毛笔，在画布上自由书写，看文字如何像水流一样，优雅地避让你的每一个笔画。'

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const font = '16px "PingFang SC", "Microsoft YaHei", "Helvetica Neue", Helvetica, sans-serif'
const lineHeight = 28
const columnMaxWidth = 680
const columnPadding = 60
const brushMinRadius = 1.5
const brushMaxRadius = 18
const brushBaseSpeed = 500 // px/sec — strokes slower than this approach max width
const strokePad = 12       // extra clearance around each stroke point for text avoidance
const minLineWidth = 50    // segments narrower than this are skipped

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type BrushPoint = { x: number; y: number; radius: number; angle: number; pressure: number }
type Stroke = BrushPoint[]

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

let strokes: Stroke[] = []
let currentStroke: Stroke | null = null
let isDrawing = false
let lastPointer: { x: number; y: number; time: number } | null = null
let prepared: PreparedTextWithSegments = null!

// ---------------------------------------------------------------------------
// DOM
// ---------------------------------------------------------------------------

const canvas = document.getElementById('c') as HTMLCanvasElement
const ctx = canvas.getContext('2d')!
const statsEl = document.getElementById('stats')!

// ---------------------------------------------------------------------------
// Prepare text
// ---------------------------------------------------------------------------

function prepareText(): void {
  prepared = prepareWithSegments(textContent, font)
}

// ---------------------------------------------------------------------------
// Canvas sizing
// ---------------------------------------------------------------------------

function resizeCanvas(): void {
  const dpr = devicePixelRatio || 1
  const w = document.documentElement.clientWidth
  const h = document.documentElement.clientHeight
  canvas.width = w * dpr
  canvas.height = h * dpr
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
}

// ---------------------------------------------------------------------------
// Background — rice-paper-like warm surface
// ---------------------------------------------------------------------------

function drawBackground(w: number, h: number): void {
  // Solid warm base
  ctx.fillStyle = '#f4ece0'
  ctx.fillRect(0, 0, w, h)

  // Subtle center-lit vignette
  const grad = ctx.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, Math.max(w, h) * 0.7)
  grad.addColorStop(0, 'rgba(255,252,245,0.35)')
  grad.addColorStop(1, 'rgba(190,175,155,0.08)')
  ctx.fillStyle = grad
  ctx.fillRect(0, 0, w, h)

  // Faint fiber-like horizontal hints
  ctx.strokeStyle = 'rgba(180,160,130,0.025)'
  ctx.lineWidth = 0.5
  for (let fy = 18; fy < h; fy += 18) {
    ctx.beginPath()
    ctx.moveTo(0, fy)
    ctx.lineTo(w, fy)
    ctx.stroke()
  }
}

// ---------------------------------------------------------------------------
// Brush ink rendering — calligraphy-style with directional ellipses & ink wash
// ---------------------------------------------------------------------------

// Simple seeded-ish hash for deterministic per-step jitter
function jitter(seed: number, range: number): number {
  const s = Math.sin(seed * 127.1 + seed * 311.7) * 43758.5453
  return (s - Math.floor(s) - 0.5) * range
}

function interpolatePoint(a: BrushPoint, b: BrushPoint, t: number): BrushPoint {
  return {
    x: a.x + (b.x - a.x) * t,
    y: a.y + (b.y - a.y) * t,
    radius: a.radius + (b.radius - a.radius) * t,
    angle: a.angle + (b.angle - a.angle) * t,
    pressure: a.pressure + (b.pressure - a.pressure) * t,
  }
}

function drawBrushDab(x: number, y: number, r: number, angle: number, pressure: number, seed: number): void {
  // A calligraphy brush tip is an angled ellipse, not a circle.
  // Major axis = along stroke direction, minor axis = perpendicular (thinner).
  const aspect = 0.35 + pressure * 0.25 // flatter when light
  const rx = r
  const ry = r * aspect

  ctx.save()
  ctx.translate(x + jitter(seed, 0.6), y + jitter(seed + 7, 0.6))
  ctx.rotate(angle)
  ctx.beginPath()
  ctx.ellipse(0, 0, rx, ry, 0, 0, Math.PI * 2)
  ctx.fill()
  ctx.restore()
}

function drawStroke(stroke: Stroke): void {
  if (stroke.length === 0) return

  // Pass 1 — outer ink wash (bleed halo)
  ctx.fillStyle = 'rgba(30,16,6,0.06)'
  for (let i = 0; i < stroke.length; i++) {
    const pt = stroke[i]!
    const prev = i > 0 ? stroke[i - 1]! : pt
    const dx = pt.x - prev.x
    const dy = pt.y - prev.y
    const dist = Math.sqrt(dx * dx + dy * dy)
    const steps = Math.max(1, Math.ceil(dist / 3))
    for (let s = 0; s <= steps; s++) {
      const t = s / steps
      const ip = interpolatePoint(prev, pt, t)
      drawBrushDab(ip.x, ip.y, ip.radius * 1.8, ip.angle, ip.pressure, i * 1000 + s)
    }
  }

  // Pass 2 — mid-tone body
  ctx.fillStyle = 'rgba(26,14,5,0.18)'
  for (let i = 0; i < stroke.length; i++) {
    const pt = stroke[i]!
    const prev = i > 0 ? stroke[i - 1]! : pt
    const dx = pt.x - prev.x
    const dy = pt.y - prev.y
    const dist = Math.sqrt(dx * dx + dy * dy)
    const steps = Math.max(1, Math.ceil(dist / 2))
    for (let s = 0; s <= steps; s++) {
      const t = s / steps
      const ip = interpolatePoint(prev, pt, t)
      drawBrushDab(ip.x, ip.y, ip.radius * 1.15, ip.angle, ip.pressure, i * 2000 + s)
    }
  }

  // Pass 3 — dark core
  ctx.fillStyle = 'rgba(20,10,2,0.50)'
  for (let i = 0; i < stroke.length; i++) {
    const pt = stroke[i]!
    const prev = i > 0 ? stroke[i - 1]! : pt
    const dx = pt.x - prev.x
    const dy = pt.y - prev.y
    const dist = Math.sqrt(dx * dx + dy * dy)
    const steps = Math.max(1, Math.ceil(dist / 1.5))
    for (let s = 0; s <= steps; s++) {
      const t = s / steps
      const ip = interpolatePoint(prev, pt, t)
      drawBrushDab(ip.x, ip.y, ip.radius * 0.6, ip.angle, ip.pressure * 0.9, i * 3000 + s)
    }
  }

  // Pass 4 — dry-brush edge streaks on fast/light sections
  ctx.fillStyle = 'rgba(60,34,14,0.06)'
  for (let i = 1; i < stroke.length; i++) {
    const pt = stroke[i]!
    if (pt.pressure > 0.6) continue // only on lighter touches
    const prev = stroke[i - 1]!
    const dist = Math.sqrt((pt.x - prev.x) ** 2 + (pt.y - prev.y) ** 2)
    const streaks = Math.ceil(dist / 4)
    for (let s = 0; s < streaks; s++) {
      const t = s / streaks
      const ip = interpolatePoint(prev, pt, t)
      const offset = ip.radius * 0.9
      const perpX = Math.cos(ip.angle + Math.PI / 2)
      const perpY = Math.sin(ip.angle + Math.PI / 2)
      for (let k = -2; k <= 2; k++) {
        const spread = (k / 2) * offset + jitter(i * 5000 + s * 7 + k, offset * 0.3)
        ctx.beginPath()
        ctx.arc(ip.x + perpX * spread, ip.y + perpY * spread, 0.5 + Math.random() * 0.8, 0, Math.PI * 2)
        ctx.fill()
      }
    }
  }
}

function drawAllStrokes(): void {
  for (const stroke of strokes) drawStroke(stroke)
  if (currentStroke) drawStroke(currentStroke)
}

// ---------------------------------------------------------------------------
// Obstacle: find ALL free horizontal segments for a text line (both sides)
// ---------------------------------------------------------------------------

type FreeSegment = { left: number; width: number }

function collectAllPoints(): BrushPoint[] {
  const pts: BrushPoint[] = []
  for (const s of strokes) for (const p of s) pts.push(p)
  if (currentStroke) for (const p of currentStroke) pts.push(p)
  return pts
}

function getFreeSegments(
  y: number,
  lh: number,
  colLeft: number,
  colWidth: number,
  allPoints: BrushPoint[],
): FreeSegment[] {
  const lineTop = y
  const lineBot = y + lh

  // Collect blocked x-intervals from stroke points overlapping this line
  const blocked: [number, number][] = []
  for (const pt of allPoints) {
    const er = pt.radius + strokePad
    if (pt.y + er < lineTop || pt.y - er > lineBot) continue
    const closestY = Math.max(lineTop, Math.min(pt.y, lineBot))
    const dy = closestY - pt.y
    const rSq = er * er - dy * dy
    if (rSq <= 0) continue
    const halfChord = Math.sqrt(rSq)
    const bL = Math.max(colLeft, pt.x - halfChord)
    const bR = Math.min(colLeft + colWidth, pt.x + halfChord)
    if (bL < bR) blocked.push([bL, bR])
  }

  const colRight = colLeft + colWidth
  if (blocked.length === 0) return [{ left: colLeft, width: colWidth }]

  // Sort & merge overlapping blocked intervals
  blocked.sort((a, b) => a[0] - b[0])
  const merged: [number, number][] = [blocked[0]!]
  for (let i = 1; i < blocked.length; i++) {
    const prev = merged[merged.length - 1]!
    const cur = blocked[i]!
    if (cur[0] <= prev[1]) {
      prev[1] = Math.max(prev[1], cur[1])
    } else {
      merged.push(cur)
    }
  }

  // Collect ALL free segments between blocked intervals (not just the widest)
  const segments: FreeSegment[] = []
  let freeStart = colLeft
  for (const [bStart, bEnd] of merged) {
    if (bStart > freeStart) {
      const w = bStart - freeStart
      if (w >= minLineWidth) segments.push({ left: freeStart, width: w })
    }
    freeStart = bEnd
  }
  if (freeStart < colRight) {
    const w = colRight - freeStart
    if (w >= minLineWidth) segments.push({ left: freeStart, width: w })
  }

  return segments
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------

function render(): void {
  requestAnimationFrame(render)

  const w = document.documentElement.clientWidth
  const h = document.documentElement.clientHeight

  ctx.clearRect(0, 0, w, h)
  drawBackground(w, h)
  drawAllStrokes()

  // Column geometry
  const colWidth = Math.min(columnMaxWidth, w - columnPadding * 2)
  const colLeft = (w - colWidth) / 2
  const allPts = collectAllPoints()

  // --- Text layout: fill ALL free segments per row (both sides of strokes) ---
  const layoutStart = performance.now()

  ctx.font = font
  ctx.textBaseline = 'top'

  let y = 110
  let cursor: LayoutCursor = { segmentIndex: 0, graphemeIndex: 0 }
  let lineCount = 0
  let exhausted = false
  const yEnd = h - 50

  while (y + lineHeight <= yEnd && !exhausted) {
    const segments = getFreeSegments(y, lineHeight, colLeft, colWidth, allPts)

    if (segments.length === 0) {
      // Row is fully blocked by ink — skip row but don't advance cursor
      y += lineHeight
      continue
    }

    // Lay text into each free segment on this row (left to right)
    for (let si = 0; si < segments.length; si++) {
      const seg = segments[si]!
      const line = layoutNextLine(prepared, cursor, seg.width)
      if (line === null) { exhausted = true; break }

      // Subtle warmth near strokes
      let minDist = Infinity
      const lmx = seg.left + seg.width / 2
      const lmy = y + lineHeight / 2
      for (const pt of allPts) {
        const d = Math.sqrt((pt.x - lmx) ** 2 + (pt.y - lmy) ** 2)
        if (d < minDist) minDist = d
      }
      const proximity = allPts.length > 0 ? Math.max(0, 1 - minDist / 180) : 0
      const r = Math.round(44 + proximity * 50)
      const g = Math.round(30 - proximity * 10)
      const b = Math.round(16 - proximity * 8)
      const alpha = 0.72 + proximity * 0.28
      ctx.fillStyle = `rgba(${r},${g},${b},${alpha})`

      ctx.fillText(line.text, seg.left, y)

      cursor = line.end
      lineCount++
    }

    y += lineHeight
  }

  const layoutMs = performance.now() - layoutStart
  statsEl.textContent = `layout ${layoutMs.toFixed(2)}ms · ${lineCount} lines · ${allPts.length} ink pts`
}

// ---------------------------------------------------------------------------
// Pointer events — brush drawing
// ---------------------------------------------------------------------------

let smoothRadius = brushMaxRadius * 0.6

function startStroke(x: number, y: number): void {
  isDrawing = true
  lastPointer = { x, y, time: performance.now() }
  smoothRadius = brushMaxRadius * 0.6
  currentStroke = [{ x, y, radius: smoothRadius, angle: 0, pressure: 0.7 }]
}

function continueStroke(x: number, y: number): void {
  if (!isDrawing || !currentStroke || !lastPointer) return
  const now = performance.now()
  const dt = Math.max(1, now - lastPointer.time)
  const dx = x - lastPointer.x
  const dy = y - lastPointer.y
  const dist = Math.sqrt(dx * dx + dy * dy)
  if (dist < 1.5) return // discard sub-pixel jitter
  const speed = (dist / dt) * 1000
  const speedFactor = Math.min(1, speed / brushBaseSpeed)
  const targetRadius = brushMaxRadius - (brushMaxRadius - brushMinRadius) * speedFactor
  // Smooth radius transition (ink doesn't jump width)
  smoothRadius += (targetRadius - smoothRadius) * 0.35
  const angle = Math.atan2(dy, dx)
  const pressure = 1 - speedFactor
  currentStroke.push({ x, y, radius: smoothRadius, angle, pressure })
  lastPointer = { x, y, time: now }
}

function endStroke(): void {
  if (currentStroke && currentStroke.length > 0) strokes.push(currentStroke)
  currentStroke = null
  isDrawing = false
  lastPointer = null
}

canvas.addEventListener('mousedown', e => startStroke(e.clientX, e.clientY))
canvas.addEventListener('mousemove', e => continueStroke(e.clientX, e.clientY))
canvas.addEventListener('mouseup', () => endStroke())
canvas.addEventListener('mouseleave', () => { if (isDrawing) endStroke() })

canvas.addEventListener('touchstart', e => {
  e.preventDefault()
  const t = e.touches[0]
  if (t) startStroke(t.clientX, t.clientY)
}, { passive: false })

canvas.addEventListener('touchmove', e => {
  e.preventDefault()
  const t = e.touches[0]
  if (t) continueStroke(t.clientX, t.clientY)
}, { passive: false })

canvas.addEventListener('touchend', e => {
  e.preventDefault()
  endStroke()
}, { passive: false })

document.addEventListener('keydown', e => {
  if (e.key === 'c' || e.key === 'C') {
    strokes = []
    currentStroke = null
  }
})

window.addEventListener('resize', resizeCanvas)

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

document.fonts.ready.then(() => {
  resizeCanvas()
  prepareText()
  requestAnimationFrame(render)
})
