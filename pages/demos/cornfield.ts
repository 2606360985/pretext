import {
  prepareWithSegments,
  layoutWithLines,
  type PreparedTextWithSegments,
} from '../../src/layout.ts'
import playerUpUrl from '../assets/playerup.png'
import playerDownUrl from '../assets/playerdown.png'
import playerLeftUrl from '../assets/playerright.png'
import playerRightUrl from '../assets/playerleft.png'

// ---------------------------------------------------------------------------
// Content — dense multilingual text that fills the "field"
// ---------------------------------------------------------------------------

const fieldText =
  'Pretext 是一个纯 JavaScript 文本排版引擎，它彻底绕过浏览器 DOM 回流，用 Canvas measureText 实现毫秒级文本高度预测。' +
  '传统网页中每次读取 offsetHeight 都会触发昂贵的同步布局回流，数百个文本块的开销每帧超过三十毫秒。' +
  'Pretext 将测量分为两阶段：prepare() 完成分词、断行、Canvas 测量；layout() 纯算术，五百段文本仅零点零九毫秒。' +
  '窗口缩放即时重排，聊天消息精确高度预测，虚拟列表真实行高。' +
  '支持所有语言：中文逐字符断行、阿拉伯文双向排版、泰文断词、emoji 宽度校正。' +
  '三大浏览器七千六百八十项测试全部通过。layoutNextLine() 让每行拥有不同宽度。' +
  '文字如田野中的庄稼，一行行整齐排列。当角色走过，文字像玉米一样被拨开，优雅地让出道路。' +
  '这是纯 CSS 永远做不到的效果——每个文字独立响应角色的位置，实时位移与旋转。' +
  '拿起键盘，在文字的田野中漫步，感受每一个字被推开又缓缓归位的弹性。' +
  '活字印刷术是中国古代四大发明之一，它的发明对人类文明产生了深远影响。' +
  '在活字印刷术出现之前，书籍复制主要靠手工抄写，不仅速度缓慢而且容易出错。' +
  '北宋庆历年间毕昇发明泥活字印刷术，将每个字刻在小方块上排列组合即可印刷。' +
  '用完后可拆开重新组合，大大提高了印刷效率。元代王祯发明木活字并创造转轮排字架。' +
  '活字印刷术使知识传播更加迅速广泛，推动文化教育普及，促进社会进步发展。' +
  '文字組版とは文字を配置して読みやすく美しい紙面を作り上げる技術のことです。' +
  '日本語の組版は漢字ひらがなカタカナラテン文字が混在し世界でも特に複雑な体系を持っています。' +
  'Typography is the art of arranging type to make written language legible readable and appealing when displayed. ' +
  'The arrangement involves typefaces point sizes line lengths spacing and letter adjustments. ' +
  'Good typography is invisible — the reader focuses on content not the mechanics of presentation. ' +
  'Pretext brings this invisible craft to programmatic layouts where CSS alone cannot reach.'

// Repeat to fill large viewports
const fullText = (fieldText + ' ').repeat(4)

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const font = '17px "PingFang SC", "Microsoft YaHei", "Helvetica Neue", Helvetica, sans-serif'
const lineHeight = 26
const charGap = 1
const pushRadius = 80
const pushStrength = 45
const rotateStrength = 0.35
const springBack = 0.12
const playerSpeed = 3.5
const maxTrailLen = 20

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type Glyph = {
  char: string
  baseX: number
  baseY: number
  dispX: number
  dispY: number
  rotation: number
  width: number
}

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

let glyphs: Glyph[] = []
let playerX = 0
let playerY = 0
let targetX = 0
let targetY = 0
let playerAngle = Math.PI / 2
let playerDir: 'up' | 'down' | 'left' | 'right' = 'down'
const keysDown = new Set<string>()
let useMouseTarget = false

const trail: { x: number; y: number }[] = []

// ---------------------------------------------------------------------------
// Player sprite — replace the src URL with your own image
// ---------------------------------------------------------------------------

const PLAYER_SIZE = 160

function resolveAssetUrl(url: string): string {
  if (/^(?:[a-z]+:)?\/\//i.test(url) || url.startsWith('data:') || url.startsWith('blob:')) return url
  if (url.startsWith('/')) return new URL(url, window.location.origin).href
  return new URL(url, import.meta.url).href
}

function loadImg(url: string): { img: HTMLImageElement; ready: boolean } {
  const entry = { img: new Image(), ready: false }
  entry.img.onload = () => { entry.ready = true }
  entry.img.onerror = () => { console.warn(`${url} failed to load`) }
  entry.img.src = resolveAssetUrl(url)
  return entry
}

const sprites = {
  up:    loadImg(playerUpUrl),
  down:  loadImg(playerDownUrl),
  left:  loadImg(playerLeftUrl),
  right: loadImg(playerRightUrl),
}

// ---------------------------------------------------------------------------
// DOM
// ---------------------------------------------------------------------------

const canvas = document.getElementById('c') as HTMLCanvasElement
const ctx = canvas.getContext('2d')!
const statsEl = document.getElementById('stats')!

// Suppress unused variable warning — prepared is consumed by layoutWithLines
let _prepared: PreparedTextWithSegments = null!

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
// Build the glyph field from Pretext layout
// ---------------------------------------------------------------------------

function buildField(): void {
  const w = document.documentElement.clientWidth
  const h = document.documentElement.clientHeight

  const colWidth = w - 40
  const colLeft = 20

  _prepared = prepareWithSegments(fullText, font)
  const { lines } = layoutWithLines(_prepared, colWidth, lineHeight)

  glyphs = []

  ctx.font = font
  for (let li = 0; li < lines.length; li++) {
    const line = lines[li]!
    const y = 20 + li * lineHeight
    if (y > h + lineHeight) break

    let x = colLeft
    for (let ci = 0; ci < line.text.length; ci++) {
      const ch = line.text[ci]!
      if (ch === ' ') {
        x += ctx.measureText(' ').width + charGap
        continue
      }
      const charWidth = ctx.measureText(ch).width
      glyphs.push({
        char: ch,
        baseX: x,
        baseY: y,
        dispX: 0,
        dispY: 0,
        rotation: 0,
        width: charWidth,
      })
      x += charWidth + charGap
    }
  }

  if (playerX === 0 && playerY === 0) {
    playerX = w / 2
    playerY = h / 2
    targetX = playerX
    targetY = playerY
  }
}

// ---------------------------------------------------------------------------
// Background
// ---------------------------------------------------------------------------

function drawBackground(w: number, h: number): void {
  const grad = ctx.createLinearGradient(0, 0, 0, h)
  grad.addColorStop(0, '#1e3516')
  grad.addColorStop(0.5, '#1a2e12')
  grad.addColorStop(1, '#14250e')
  ctx.fillStyle = grad
  ctx.fillRect(0, 0, w, h)
}

// ---------------------------------------------------------------------------
// Draw the player character
// ---------------------------------------------------------------------------

function drawPlayer(): void {
  // Footprint trail
  ctx.fillStyle = 'rgba(10,20,6,0.15)'
  for (let i = 0; i < trail.length; i++) {
    const t = trail[i]!
    const age = 1 - i / trail.length
    const r = 3 + age * 2
    ctx.globalAlpha = age * 0.3
    ctx.beginPath()
    ctx.ellipse(t.x - 4, t.y, r, r * 0.6, 0, 0, Math.PI * 2)
    ctx.fill()
    ctx.beginPath()
    ctx.ellipse(t.x + 4, t.y, r, r * 0.6, 0, 0, Math.PI * 2)
    ctx.fill()
  }
  ctx.globalAlpha = 1

  // Shadow
  ctx.fillStyle = 'rgba(0,0,0,0.25)'
  ctx.beginPath()
  ctx.ellipse(playerX, playerY + 12, 18, 8, 0, 0, Math.PI * 2)
  ctx.fill()

  // Character with bobbing, direction-based sprite
  ctx.save()
  ctx.translate(playerX, playerY)
  const bob = Math.sin(performance.now() / 180) * 2
  const sprite = sprites[playerDir]
  if (sprite.ready) {
    ctx.drawImage(sprite.img, -PLAYER_SIZE / 2, -PLAYER_SIZE / 2 + bob - 4, PLAYER_SIZE, PLAYER_SIZE)
  } else {
    // Fallback emoji while image loads or if missing
    ctx.font = '36px serif'
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText('\u{1F33E}', 0, bob - 4) // 🌾 fallback
  }
  ctx.restore()

  // Warm ground-light glow
  const glow = ctx.createRadialGradient(playerX, playerY, 0, playerX, playerY, pushRadius * 1.2)
  glow.addColorStop(0, 'rgba(180,210,120,0.06)')
  glow.addColorStop(0.5, 'rgba(140,180,80,0.02)')
  glow.addColorStop(1, 'rgba(0,0,0,0)')
  ctx.fillStyle = glow
  ctx.beginPath()
  ctx.arc(playerX, playerY, pushRadius * 1.2, 0, Math.PI * 2)
  ctx.fill()
}

// ---------------------------------------------------------------------------
// Direction helper
// ---------------------------------------------------------------------------

function angleToDir(angle: number): 'up' | 'down' | 'left' | 'right' {
  // angle from Math.atan2: 0=right, π/2=down, ±π=left, -π/2=up
  const a = ((angle % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI) // normalize to [0, 2π)
  if (a >= Math.PI * 0.25 && a < Math.PI * 0.75) return 'down'
  if (a >= Math.PI * 0.75 && a < Math.PI * 1.25) return 'left'
  if (a >= Math.PI * 1.25 && a < Math.PI * 1.75) return 'up'
  return 'right'
}

// ---------------------------------------------------------------------------
// Update loop
// ---------------------------------------------------------------------------

function updatePlayer(): void {
  let dx = 0
  let dy = 0

  if (keysDown.has('w') || keysDown.has('arrowup')) dy -= 1
  if (keysDown.has('s') || keysDown.has('arrowdown')) dy += 1
  if (keysDown.has('a') || keysDown.has('arrowleft')) dx -= 1
  if (keysDown.has('d') || keysDown.has('arrowright')) dx += 1

  if (dx !== 0 || dy !== 0) {
    useMouseTarget = false
    const len = Math.sqrt(dx * dx + dy * dy)
    playerX += (dx / len) * playerSpeed
    playerY += (dy / len) * playerSpeed
    playerAngle = Math.atan2(dy, dx)
    playerDir = angleToDir(playerAngle)
  } else if (useMouseTarget) {
    const tdx = targetX - playerX
    const tdy = targetY - playerY
    const dist = Math.sqrt(tdx * tdx + tdy * tdy)
    if (dist > 4) {
      playerX += (tdx / dist) * playerSpeed
      playerY += (tdy / dist) * playerSpeed
      playerAngle = Math.atan2(tdy, tdx)
      playerDir = angleToDir(playerAngle)
    }
  }

  const w = document.documentElement.clientWidth
  const h = document.documentElement.clientHeight
  playerX = Math.max(20, Math.min(w - 20, playerX))
  playerY = Math.max(20, Math.min(h - 20, playerY))

  // Update trail
  if (trail.length === 0 || Math.abs(trail[0]!.x - playerX) + Math.abs(trail[0]!.y - playerY) > 12) {
    trail.unshift({ x: playerX, y: playerY })
    if (trail.length > maxTrailLen) trail.pop()
  }
}

function updateGlyphs(): void {
  for (const g of glyphs) {
    const gcx = g.baseX + g.width / 2
    const gcy = g.baseY + lineHeight / 2
    const dx = gcx - playerX
    const dy = gcy - playerY
    const dist = Math.sqrt(dx * dx + dy * dy)

    if (dist < pushRadius && dist > 0.1) {
      const factor = (1 - dist / pushRadius) ** 2
      const pushX = (dx / dist) * pushStrength * factor
      const pushY = (dy / dist) * pushStrength * factor
      const targetRot = factor * rotateStrength * (dx > 0 ? 1 : -1)

      g.dispX += (pushX - g.dispX) * 0.4
      g.dispY += (pushY - g.dispY) * 0.4
      g.rotation += (targetRot - g.rotation) * 0.4
    } else {
      g.dispX *= (1 - springBack)
      g.dispY *= (1 - springBack)
      g.rotation *= (1 - springBack)
    }
  }
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------

function render(): void {
  requestAnimationFrame(render)

  const w = document.documentElement.clientWidth
  const h = document.documentElement.clientHeight

  updatePlayer()
  updateGlyphs()

  ctx.clearRect(0, 0, w, h)
  drawBackground(w, h)

  const layoutStart = performance.now()

  ctx.font = font
  ctx.textBaseline = 'top'

  let visibleCount = 0
  for (const g of glyphs) {
    const x = g.baseX + g.dispX
    const y = g.baseY + g.dispY

    if (x + g.width < -20 || x > w + 20 || y + lineHeight < -20 || y > h + 20) continue
    visibleCount++

    const gcx = g.baseX + g.width / 2
    const gcy = g.baseY + lineHeight / 2
    const dist = Math.sqrt((gcx - playerX) ** 2 + (gcy - playerY) ** 2)
    const nearness = Math.max(0, 1 - dist / (pushRadius * 2))

    const r = Math.round(130 + nearness * 90)
    const gn = Math.round(175 + nearness * 60)
    const b = Math.round(100 + nearness * 40)
    const alpha = 0.65 + nearness * 0.35
    ctx.fillStyle = `rgba(${r},${gn},${b},${alpha})`

    if (Math.abs(g.rotation) > 0.005) {
      ctx.save()
      ctx.translate(x + g.width / 2, y + lineHeight / 2)
      ctx.rotate(g.rotation)
      ctx.fillText(g.char, -g.width / 2, -lineHeight / 2)
      ctx.restore()
    } else {
      ctx.fillText(g.char, x, y)
    }
  }

  drawPlayer()

  const layoutMs = performance.now() - layoutStart
  statsEl.textContent = `${visibleCount} glyphs · ${layoutMs.toFixed(1)}ms`
}

// ---------------------------------------------------------------------------
// Events
// ---------------------------------------------------------------------------

document.addEventListener('keydown', e => {
  keysDown.add(e.key.toLowerCase())
})
document.addEventListener('keyup', e => {
  keysDown.delete(e.key.toLowerCase())
})

canvas.addEventListener('click', e => {
  targetX = e.clientX
  targetY = e.clientY
  useMouseTarget = true
})

canvas.addEventListener('touchstart', e => {
  e.preventDefault()
  const t = e.touches[0]
  if (t) { targetX = t.clientX; targetY = t.clientY; useMouseTarget = true }
}, { passive: false })

canvas.addEventListener('touchmove', e => {
  e.preventDefault()
  const t = e.touches[0]
  if (t) { targetX = t.clientX; targetY = t.clientY; useMouseTarget = true }
}, { passive: false })

window.addEventListener('resize', () => {
  resizeCanvas()
  buildField()
})

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

document.fonts.ready.then(() => {
  resizeCanvas()
  buildField()
  requestAnimationFrame(render)
})
