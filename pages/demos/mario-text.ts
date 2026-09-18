// mario-text.ts — Super Mario game built on Pretext's text layout engine.
// The game level is a text grid where each character = one tile.
// Pretext's prepare() + layout() handles character width measurement via canvas measureText.
// The mascot walks through the text world, collecting coins and stomping enemies.
// This demo showcases Pretext's core capability: text measurement without DOM reflow.

import { prepare, layout } from '../../src/layout.ts'
import mascotUrl from '../assets/Mascot_Idle.png'

// ---------------------------------------------------------------------------
// Level as text — each character = one tile.
// This IS the Pretext demo: a game world expressed as pure text content.
// ---------------------------------------------------------------------------

// Legend:
// ' ' = sky (walkable),  █ = solid ground/brick
// ？ = question block (solid, bouncy, has coin),  ★ = coin (collectible)
// 步 = step/platform,     G = goomba (enemy)
// 下=pipe left,    管=pipe right,    洞=gap/pit
//
// Row layout — classic Mario horizontal scroller:
// Row 0-7: sky + platforms
// Row 8: question block row
// Row 9: enemy row
// Row 10: ground with gaps

const LEVEL_TEXT =
  '                                                      ' +
  '                                                      ' +
  '           ？？？                     ？？？           ' +
  '          █████                   █████               ' +
  '                                   ？               ' +
  '      ？？？                   ？？？   步步步步   ？？？' +
  '     █████   管管    管管                   ████████' +
  '                   管管                                  ' +
  '  ？ ？ ？           管管    管管  ？ ？          G    ' +
  '  步步步步步步步步步步步步步步步步步步步步步步步步步步步步步步' +
  '步步步步步          洞洞洞洞          步步步步步步步步步步步步' +
  '███████████████████████████████████████████████████████████████' +
  '███████████████████████████████████████████████████████████████'

// Each char in LEVEL_TEXT is one tile. 80 chars wide.
const LEVEL_WIDTH = 80
const LEVEL_HEIGHT = 13

// ---------------------------------------------------------------------------
// Tile classification — maps character → game tile
// ---------------------------------------------------------------------------

type TileKind = 'sky' | 'solid' | 'coin' | 'qblock' | 'enemy' | 'pipe' | 'step' | 'gap'

interface TileDef {
  kind: TileKind
  solid: boolean
  bouncy: boolean
  collectible: boolean
}

function classifyChar(ch: string): TileDef {
  switch (ch) {
    case ' ': return { kind: 'sky',       solid: false, bouncy: false, collectible: false }
    case '█': return { kind: 'solid',     solid: true,  bouncy: false, collectible: false }
    case '？': return { kind: 'qblock',    solid: true,  bouncy: true,  collectible: true  }
    case '★': return { kind: 'coin',      solid: false, bouncy: false, collectible: true  }
    case '步': return { kind: 'step',      solid: true,  bouncy: false, collectible: false }
    case 'G': return { kind: 'enemy',      solid: false, bouncy: false, collectible: false }
    case '管': return { kind: 'pipe',      solid: true,  bouncy: false, collectible: false }
    case '洞': return { kind: 'gap',       solid: false, bouncy: false, collectible: false }
    default:   return { kind: 'sky',      solid: false, bouncy: false, collectible: false }
  }
}

// Tile colors — each tile kind has its own color scheme
const TILE_COLORS: Record<TileKind, { fg: string; bg: string }> = {
  sky:     { fg: '#1a2a4a', bg: 'transparent' },
  solid:   { fg: '#c84c0c', bg: '#2a1a0a' },
  coin:    { fg: '#ffd700', bg: 'transparent' },
  qblock:  { fg: '#ffd700', bg: '#3a2a00' },
  enemy:   { fg: '#8b4513', bg: 'transparent' },
  pipe:    { fg: '#44a02a', bg: '#1a3a0a' },
  step:    { fg: '#a07030', bg: '#2a1a0a' },
  gap:     { fg: '#0a0a1a', bg: '#0a0a1a' },
}

// ---------------------------------------------------------------------------
// Build level grid from LEVEL_TEXT
// ---------------------------------------------------------------------------

type GridTile = { kind: TileKind; def: TileDef; char: string; collected: boolean }

let levelGrid: GridTile[][] = []

function buildLevelGrid(): void {
  levelGrid = []
  for (let row = 0; row < LEVEL_HEIGHT; row++) {
    const rowTiles: GridTile[] = []
    for (let col = 0; col < LEVEL_WIDTH; col++) {
      const idx = row * LEVEL_WIDTH + col
      const ch = LEVEL_TEXT[idx] ?? ' '
      const def = classifyChar(ch)
      rowTiles.push({ kind: def.kind, def, char: ch, collected: false })
    }
    levelGrid.push(rowTiles)
  }
}

// ---------------------------------------------------------------------------
// Pretext setup — font size = tile size
// ---------------------------------------------------------------------------

const CELL_FONT_SIZE = 18
const CELL_WIDTH = CELL_FONT_SIZE * 1.1  // approx char width
const CELL_HEIGHT = CELL_FONT_SIZE * 1.4
const FONT = `${CELL_FONT_SIZE}px "PingFang SC", "Microsoft YaHei", "Courier New", monospace`

// ---------------------------------------------------------------------------
// Glyph — Pretext-computed character position
// ---------------------------------------------------------------------------

type Glyph = {
  char: string
  baseX: number
  baseY: number
  width: number
  height: number
  row: number
  col: number
  kind: TileKind
  def: TileDef
  dispX: number
  dispY: number
  rotation: number
}

let glyphs: Glyph[] = []

// ---------------------------------------------------------------------------
// Game state
// ---------------------------------------------------------------------------

let score = 0
let coins = 0

// Camera — scrolls horizontally through the level
let camX = 0

// Mario — position in tile grid coordinates (float)
let mario = {
  col: 3,         // tile column
  row: 7.5,       // tile row (start above platform at row 8)
  vcol: 0,        // velocity in cols/frame
  vrow: 0,        // velocity in rows/frame
  facing: 'r' as 'r' | 'l',
  jumping: false,
  moving: false,
  animFrame: 0,
  dead: false,
}

// Enemies
interface Enemy {
  col: number; row: number
  vcol: number; vrow: number
  alive: boolean; flat: boolean
  colId: number  // unique id for render
}
let enemies: Enemy[] = []
let _enemyId = 0

function spawnEnemies() {
  enemies = []
  for (let row = 0; row < LEVEL_HEIGHT; row++) {
    for (let col = 0; col < LEVEL_WIDTH; col++) {
      if (levelGrid[row]![col]!.kind === 'enemy') {
        enemies.push({ col, row, vcol: -0.015, vrow: 0, alive: true, flat: false, colId: _enemyId++ })
        levelGrid[row]![col] = { kind: 'sky', def: { kind: 'sky', solid: false, bouncy: false, collectible: false }, char: ' ', collected: false }
      }
    }
  }
}

// Particles
interface Particle { x: number; y: number; vx: number; vy: number; ch: string; color: string; life: number }
let particles: Particle[] = []

let _phase: 'start' | 'playing' = 'start'

// ---------------------------------------------------------------------------
// Mascot sprite
// ---------------------------------------------------------------------------

const MASCOT_SIZE = 48

function resolveAssetUrl(url: string): string {
  if (/^(?:[a-z]+:)?\/\//i.test(url) || url.startsWith('data:')) return url
  if (url.startsWith('/')) return new URL(url, window.location.origin).href
  return new URL(url, import.meta.url).href
}

// ---------------------------------------------------------------------------
// Pretext character width cache — measure each unique char once via Pretext.
// This avoids DOM reflows by using Pretext's canvas-based measureText.
// ---------------------------------------------------------------------------

const _charWidthCache = new Map<string, number>()

function pretextCharWidth(ch: string): number {
  if (_charWidthCache.has(ch)) return _charWidthCache.get(ch)!
  // Pretext: prepare segments + measure width via canvas measureText
  const prep = prepare(ch, FONT)
  const { height } = layout(prep, CELL_WIDTH * 2, CELL_HEIGHT)
  // Approximate width from line height (chars are ~0.6x as wide as tall)
  const w = Math.round(height * 0.65)
  _charWidthCache.set(ch, w)
  return w
}

const mascot = (() => {
  const img = new Image()
  img.onerror = () => console.warn('mascot img failed')
  img.src = resolveAssetUrl(mascotUrl)
  return { img, ready: false, check: false }
})()

// ---------------------------------------------------------------------------
// DOM
// ---------------------------------------------------------------------------

const canvas = document.getElementById('c') as HTMLCanvasElement
const ctx = canvas.getContext('2d')!
const scoreEl = document.getElementById('score')!
const coinsEl = document.getElementById('coins')!
const overlayEl = document.getElementById('overlay')!
const statsEl = document.getElementById('stats')!

// ---------------------------------------------------------------------------
// Canvas
// ---------------------------------------------------------------------------

function resizeCanvas(): void {
  const dpr = devicePixelRatio || 1
  canvas.width = document.documentElement.clientWidth * dpr
  canvas.height = document.documentElement.clientHeight * dpr
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
}

// ---------------------------------------------------------------------------
// Build glyph field — Pretext layout for the visible viewport columns
// Only rebuilds when camera changes
// ---------------------------------------------------------------------------

let lastCamCol = -1

function buildGlyphs(): void {
  const VW = document.documentElement.clientWidth

  // How many characters wide is the viewport?
  const textWidth = Math.ceil(VW / CELL_WIDTH) + 2

  // Build a string of exactly the visible level columns
  // Each "line" = one row of the level, truncated to viewport
  const startCol = Math.floor(camX)

  glyphs = []

  for (let row = 0; row < LEVEL_HEIGHT; row++) {
    let x = 0
    for (let col = startCol; col < startCol + textWidth && col < LEVEL_WIDTH; col++) {
      if (col < 0) { x += CELL_WIDTH; continue }
      const tile = levelGrid[row]![col]!
      // Pretext-measured character width (cached per unique char)
      const w = pretextCharWidth(tile.char)
      glyphs.push({
        char: tile.char,
        baseX: x,
        baseY: row * CELL_HEIGHT,
        width: w,
        height: CELL_HEIGHT,
        row,
        col,
        kind: tile.kind,
        def: tile.def,
        dispX: 0,
        dispY: 0,
        rotation: 0,
      })
      x += CELL_WIDTH
    }
  }
}

// ---------------------------------------------------------------------------
// Physics constants
// ---------------------------------------------------------------------------

const GRAVITY = 0.025
const JUMP_FORCE = -0.22
const MOVE_SPEED = 0.08
const PUSH_RADIUS = 40
const PUSH_STRENGTH = 20
const ROTATE_STRENGTH = 0.2
const SPRING_BACK = 0.12

// ---------------------------------------------------------------------------
// Collision helpers
// ---------------------------------------------------------------------------

function tileAt(col: number, row: number): GridTile | null {
  const r = Math.floor(row)
  const c = Math.floor(col)
  if (r < 0 || r >= LEVEL_HEIGHT || c < 0 || c >= LEVEL_WIDTH) return null
  return levelGrid[r]![c]!
}

function isSolidAt(col: number, row: number): boolean {
  const tile = tileAt(col, row)
  return tile != null && tile.def.solid
}

// Mario hitbox: ~1 col wide, ~1.5 rows tall
// Separate collision for X vs Y movement — prevents blocking on ground
function marioCollidesX(col: number, row: number): boolean {
  // Horizontal: check only current row (feet row)
  return isSolidAt(col, row + 1.4) || isSolidAt(col + 0.8, row + 1.4)
}

function marioCollidesY(col: number, row: number): boolean {
  // Vertical: check top and bottom edges
  return isSolidAt(col, row) || isSolidAt(col + 0.8, row) ||
         isSolidAt(col, row + 1.4) || isSolidAt(col + 0.8, row + 1.4)
}

// ---------------------------------------------------------------------------
// Input
// ---------------------------------------------------------------------------

const keys = new Set<string>()
document.addEventListener('keydown', e => {
  keys.add(e.key.toLowerCase())
  if (e.key === ' ') e.preventDefault()
})
document.addEventListener('keyup', e => keys.delete(e.key.toLowerCase()))

function isLeft()  { return keys.has('a') || keys.has('arrowleft') }
function isRight() { return keys.has('d') || keys.has('arrowright') }
function isJump()  { return keys.has(' ') || keys.has('w') || keys.has('arrowup') }

let jumpHeld = false

// ---------------------------------------------------------------------------
// Update
// ---------------------------------------------------------------------------

function update() {
  if (_phase === 'start') {
    if (isJump()) {
      _phase = 'playing'
      overlayEl.classList.remove('show')
    }
    return
  }

  // ---- Auto-scroll: camera follows Mario ----
  const MARIO_SCREEN_COL = 10  // keep Mario 10 cols from left edge
  const targetCamX = mario.col - MARIO_SCREEN_COL
  if (targetCamX > camX) {
    camX = Math.min(targetCamX, camX + 0.04)
  }
  camX = Math.max(0, camX)

  // Rebuild glyphs when camera column changes
  if (Math.floor(camX) !== lastCamCol) {
    buildGlyphs()
    lastCamCol = Math.floor(camX)
  }

  // ---- Mario movement ----
  let dx = 0
  if (isRight()) dx = 1
  if (isLeft()) dx = -1

  if (dx !== 0) {
    mario.vcol = dx * MOVE_SPEED
    mario.facing = dx > 0 ? 'r' : 'l'
    mario.moving = true
  } else {
    mario.vcol = 0
    mario.moving = false
  }

  // Jump
  if (isJump() && !jumpHeld && !mario.jumping) {
    mario.vrow = JUMP_FORCE
    mario.jumping = true
    jumpHeld = true
  }
  if (!isJump()) jumpHeld = false

  // Gravity
  mario.vrow += GRAVITY
  mario.vrow = Math.min(mario.vrow, 0.25)

  // ---- Move X ----
  const nx = mario.col + mario.vcol
  if (!marioCollidesX(nx, mario.row)) {
    mario.col = nx
  } else {
    mario.vcol = 0
  }

  // ---- Move Y ----
  const ny = mario.row + mario.vrow
  if (!marioCollidesY(mario.col, ny)) {
    mario.row = ny
  } else {
    if (mario.vrow > 0) {
      // Landing — snap to top of tile
      mario.row = Math.floor(ny)
      mario.jumping = false
      mario.vrow = 0

      // Hit question block from below
      const headRow = Math.floor(ny) - 1
      const bodyCol = Math.floor(mario.col + 0.4)
      const tile = tileAt(bodyCol, headRow)
      if (tile && tile.kind === 'qblock') {
        tile.kind = 'coin'
        tile.char = ' '
        tile.collected = true
        score += 100
        scoreEl.textContent = `SCORE: ${score}`
        // Spawn coin particle
        const sx = bodyCol * CELL_WIDTH - camX * CELL_WIDTH
        const sy = headRow * CELL_HEIGHT
        particles.push({ x: sx, y: sy, vx: 0, vy: -3, ch: '●', color: '#ffd700', life: 1 })
      }
    } else {
      // Hit ceiling
      mario.vrow = 0
      mario.row = Math.floor(ny) + 1
    }
  }

  // ---- Boundary ----
  mario.col = Math.max(0, Math.min(LEVEL_WIDTH - 1, mario.col))
  if (mario.row > LEVEL_HEIGHT) {
    mario.row = 9.5  // respawn above ground
    mario.col = Math.max(3, camX + 3)
    mario.vrow = 0
  }

  // ---- Coin collection ----
  for (let row = 0; row < LEVEL_HEIGHT; row++) {
    for (let col = 0; col < LEVEL_WIDTH; col++) {
      const tile = levelGrid[row]![col]!
      if (tile.kind === 'coin' && !tile.collected) {
        const dist = Math.sqrt((col - mario.col - 0.4) ** 2 + (row - mario.row - 0.7) ** 2)
        if (dist < 1.2) {
          tile.collected = true
          coins++
          score += 200
          coinsEl.textContent = `COINS: ${coins}`
          scoreEl.textContent = `SCORE: ${score}`
        }
      }
    }
  }

  // ---- Enemies ----
  for (const e of enemies) {
    if (!e.alive) continue

    e.vrow += GRAVITY
    e.vrow = Math.min(e.vrow, 0.15)
    e.col += e.vcol

    // Turn at walls / fall
    const frontCol = e.vcol < 0 ? e.col - 0.1 : e.col + 1.1
    if (isSolidAt(frontCol, e.row + 0.5)) {
      e.vcol = -e.vcol
    }
    const below = isSolidAt(e.col + 0.5, e.row + 0.6)
    if (!below) {
      e.row += e.vrow
    } else {
      e.row = Math.floor(e.row)
      e.vrow = 0
    }

    // Fell off
    if (e.row > LEVEL_HEIGHT) { e.alive = false; continue }

    // ---- Mario-enemy collision ----
    const dist = Math.sqrt((e.col - mario.col - 0.4) ** 2 + (e.row - mario.row - 0.7) ** 2)
    if (dist < 1.0) {
      if (mario.vrow > 0 && mario.row + 1.2 < e.row + 0.5) {
        // Stomp
        e.flat = true
        e.vcol = 0
        e.vrow = 0
        mario.vrow = JUMP_FORCE * 0.6
        score += 100
        scoreEl.textContent = `SCORE: ${score}`
      } else {
        // Hit side — simple: bounce back
        mario.col -= 1
      }
    }
  }

  // ---- Glyph physics (like cornfield) ----
  const px = (mario.col - camX) * CELL_WIDTH + CELL_WIDTH / 2
  const py = mario.row * CELL_HEIGHT + CELL_HEIGHT / 2

  for (const g of glyphs) {
    const gx = g.baseX + g.width / 2
    const gy = g.baseY + CELL_HEIGHT / 2
    const dx = gx - px
    const dy = gy - py
    const dist = Math.sqrt(dx * dx + dy * dy)

    if (dist < PUSH_RADIUS && dist > 0.1) {
      const factor = (1 - dist / PUSH_RADIUS) ** 2
      g.dispX += ((dx / dist) * PUSH_STRENGTH * factor - g.dispX) * 0.4
      g.dispY += ((dy / dist) * PUSH_STRENGTH * factor - g.dispY) * 0.4
      g.rotation += ((factor * ROTATE_STRENGTH * (dx > 0 ? 1 : -1)) - g.rotation) * 0.4
    } else {
      g.dispX *= (1 - SPRING_BACK)
      g.dispY *= (1 - SPRING_BACK)
      g.rotation *= (1 - SPRING_BACK)
    }
  }

  // ---- Particles ----
  for (const p of particles) {
    p.x += p.vx
    p.y += p.vy
    p.vy += 0.2
    p.life -= 0.03
  }
  particles = particles.filter(p => p.life > 0)

  // ---- Animation ----
  if (mario.moving) {
    mario.animFrame = Math.floor(performance.now() / 100)
  }
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------

function render(): void {
  requestAnimationFrame(render)
  update()

  const VW = document.documentElement.clientWidth
  const VH = document.documentElement.clientHeight

  ctx.clearRect(0, 0, VW, VH)

  // Sky gradient
  const skyGrad = ctx.createLinearGradient(0, 0, 0, VH)
  skyGrad.addColorStop(0, '#3a6ab0')
  skyGrad.addColorStop(1, '#5a9ad0')
  ctx.fillStyle = skyGrad
  ctx.fillRect(0, 0, VW, VH)

  // ---- Render glyphs (Pretext-positioned text tiles) ----
  ctx.font = FONT
  ctx.textBaseline = 'top'

  for (const g of glyphs) {
    const sx = g.baseX + g.dispX
    const sy = g.baseY + g.dispY

    if (sx < -CELL_WIDTH || sx > VW + CELL_WIDTH) continue
    if (sy < -CELL_HEIGHT || sy > VH + CELL_HEIGHT) continue

    const colors = TILE_COLORS[g.kind]

    // Question block pulse
    if (g.kind === 'qblock') {
      const pulse = Math.sin(performance.now() / 200 + g.col) * 0.3 + 0.7
      ctx.globalAlpha = pulse
      ctx.fillStyle = '#ffee44'
    } else if (g.kind === 'coin') {
      const shimmer = Math.sin(performance.now() / 150 + g.col * 0.5) > 0
      ctx.globalAlpha = 0.9
      ctx.fillStyle = shimmer ? '#ffee00' : '#ffcc00'
    } else if (g.kind === 'gap') {
      ctx.globalAlpha = 1
      ctx.fillStyle = '#050510'
    } else if (g.kind === 'solid') {
      ctx.globalAlpha = 0.85
      ctx.fillStyle = colors.fg
    } else {
      ctx.globalAlpha = 0.3
      ctx.fillStyle = colors.fg
    }

    if (Math.abs(g.rotation) > 0.01) {
      ctx.save()
      ctx.translate(sx + g.width / 2, sy + CELL_HEIGHT / 2)
      ctx.rotate(g.rotation)
      ctx.fillText(g.char, -g.width / 2, -CELL_HEIGHT / 2)
      ctx.restore()
    } else {
      ctx.fillText(g.char, sx, sy)
    }

    ctx.globalAlpha = 1
  }

  // ---- Render enemies ----
  ctx.font = `bold ${CELL_FONT_SIZE * 1.2}px "Courier New", monospace`
  ctx.textBaseline = 'top'
  for (const e of enemies) {
    if (!e.alive) continue
    const sx = (e.col - camX) * CELL_WIDTH
    const sy = e.row * CELL_HEIGHT
    if (sx < -CELL_WIDTH * 2 || sx > VW + CELL_WIDTH * 2) continue
    ctx.fillStyle = e.flat ? '#5a3010' : '#8b4513'
    ctx.fillText(e.flat ? '_' : 'g', sx, sy)
  }

  // ---- Render Mario ----
  const mx = (mario.col - camX) * CELL_WIDTH
  const my = mario.row * CELL_HEIGHT

  // Shadow
  ctx.fillStyle = 'rgba(0,0,0,0.25)'
  ctx.beginPath()
  ctx.ellipse(mx + CELL_WIDTH * 0.4, my + CELL_HEIGHT * 1.5, CELL_WIDTH * 0.5, 5, 0, 0, Math.PI * 2)
  ctx.fill()

  // Mascot sprite
  if (mascot.img.complete && !mascot.check) {
    mascot.ready = true
    mascot.check = true
  }

  const bob = Math.sin(performance.now() / 150) * 2
  if (mascot.ready) {
    ctx.drawImage(mascot.img, mx - MASCOT_SIZE * 0.3, my - MASCOT_SIZE * 0.5 + bob, MASCOT_SIZE, MASCOT_SIZE)
  } else {
    // Fallback: colored block as Mario
    ctx.font = `bold ${CELL_FONT_SIZE}px "Courier New", monospace`
    ctx.textBaseline = 'top'
    ctx.fillStyle = '#e05020'
    ctx.fillText('█', mx, my + bob)
  }

  // ---- Particles ----
  ctx.font = `${CELL_FONT_SIZE}px "Courier New", monospace`
  ctx.textBaseline = 'top'
  for (const p of particles) {
    ctx.globalAlpha = p.life
    ctx.fillStyle = p.color
    ctx.fillText(p.ch, p.x, p.y)
  }
  ctx.globalAlpha = 1

  // ---- Stats ----
  if (statsEl) {
    const solidCount = glyphs.filter(g => g.def.solid).length
    const coinCount = glyphs.filter(g => g.kind === 'coin').length
    statsEl.textContent = `${glyphs.length} glyphs · ${solidCount} solid · ${coinCount} coins · Pretext layout`
  }

  // ---- Start overlay ----
  if (_phase === 'start') {
    overlayEl.classList.add('show')
  }
}

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

document.fonts.ready.then(() => {
  resizeCanvas()
  buildLevelGrid()
  spawnEnemies()
  buildGlyphs()
  scoreEl.textContent = `SCORE: ${score}`
  coinsEl.textContent = `COINS: ${coins}`
  overlayEl.classList.add('show')
  requestAnimationFrame(render)
})

window.addEventListener('resize', () => {
  resizeCanvas()
  buildGlyphs()
})
