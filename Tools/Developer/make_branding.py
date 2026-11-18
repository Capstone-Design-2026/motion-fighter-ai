"""흑운협 아이콘(1024) · 시작 화면 로고를 제목 붓글씨(HeukUnHyeopTitle.png)로 만든다."""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageFont
from scipy import ndimage

ROOT = r'C:\Unity\VisionStriker'
TITLE = os.path.join(ROOT, r'Assets\Resources\UI\Wuxia\HeukUnHyeopTitle.png')
OUT = os.path.join(ROOT, r'Assets\Branding')
PREVIEW = sys.argv[1] if len(sys.argv) > 1 else OUT
os.makedirs(OUT, exist_ok=True)

title = Image.open(TITLE).convert('RGBA')
alpha = np.array(title)[:, :, 3]

# ── "黑" 한 글자: 이어진 덩어리 중 무게중심이 첫 글자 칸(x<640)에 있는 것만 남긴다.
lab, n = ndimage.label(alpha > 20)
keep = np.zeros_like(alpha, dtype=bool)
for i in range(1, n + 1):
    ys, xs = np.nonzero(lab == i)
    if xs.mean() < 640 and xs.max() < 700:
        keep |= lab == i
glyph = np.array(title).copy()
glyph[~keep, 3] = 0
glyph = Image.fromarray(glyph)
glyph = glyph.crop(glyph.getbbox())


def radial(size, inner, outer, power=1.6):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    d = np.sqrt((xx - size / 2) ** 2 + (yy - size * .46) ** 2) / (size * .62)
    t = np.clip(d, 0, 1) ** power
    c = np.array(inner, np.float32) * (1 - t[..., None]) + np.array(outer, np.float32) * t[..., None]
    return Image.fromarray(c.astype(np.uint8), 'RGB')


def clouds(size, seed, tint, strength):
    """먹구름: 큰 타원 몇 겹을 흐리게 겹쳐 '黑雲'의 구름결을 만든다."""
    rng = np.random.default_rng(seed)
    layer = Image.new('L', (size, size), 0)
    d = ImageDraw.Draw(layer)
    for _ in range(26):
        cx, cy = rng.uniform(.0, 1.0) * size, rng.uniform(.25, 1.0) * size
        rx, ry = rng.uniform(.10, .30) * size, rng.uniform(.04, .10) * size
        d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=int(rng.uniform(60, 140)))
    layer = layer.filter(ImageFilter.GaussianBlur(size * .035))
    col = Image.new('RGB', (size, size), tint)
    return col, layer.point(lambda v: int(v * strength))


def gold_fill(size):
    """금색 면: 왼쪽 위가 밝고 오른쪽 아래가 어두운 금, 잔결을 조금 넣는다."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32) / size
    t = np.clip((xx * .55 + yy * .45), 0, 1)
    hi, mid, lo = np.array([250, 226, 168]), np.array([212, 166, 92]), np.array([120, 78, 34])
    c = np.where(t[..., None] < .5, hi + (mid - hi) * (t[..., None] / .5), mid + (lo - mid) * ((t[..., None] - .5) / .5))
    noise = np.random.default_rng(3).normal(0, 9, (size, size, 1))
    return Image.fromarray(np.clip(c + noise, 0, 255).astype(np.uint8), 'RGB')


def icon(size=1024):
    S = size
    base = radial(S, (52, 40, 34), (10, 8, 8))
    col, mask = clouds(S, 7, (88, 74, 66), .55)
    base = Image.composite(col, base, mask)
    canvas = base.convert('RGBA')

    # 붉은 기운(팀 RedBool): 원 안쪽 아래에 아주 옅게
    glow = Image.new('L', (S, S), 0)
    ImageDraw.Draw(glow).ellipse([S * .22, S * .52, S * .78, S * .98], fill=90)
    glow = glow.filter(ImageFilter.GaussianBlur(S * .08))
    canvas = Image.composite(Image.new('RGBA', (S, S), (150, 28, 22, 255)), canvas, glow.point(lambda v: int(v * .55)))

    # 금빛 두 겹 원테
    ring = Image.new('L', (S, S), 0)
    d = ImageDraw.Draw(ring)
    r1, w1 = S * .445, int(S * .022)
    d.ellipse([S / 2 - r1, S / 2 - r1, S / 2 + r1, S / 2 + r1], outline=255, width=w1)
    r2 = S * .405
    d.ellipse([S / 2 - r2, S / 2 - r2, S / 2 + r2, S / 2 + r2], outline=255, width=max(2, int(S * .006)))
    shadow = ring.filter(ImageFilter.GaussianBlur(S * .012))
    canvas = Image.composite(Image.new('RGBA', (S, S), (0, 0, 0, 255)), canvas, shadow.point(lambda v: int(v * .7)))
    canvas = Image.composite(gold_fill(S).convert('RGBA'), canvas, ring)
    # 원 안쪽은 조금 더 어둡게 해 글자를 띄운다
    inner = Image.new('L', (S, S), 0)
    ImageDraw.Draw(inner).ellipse([S / 2 - r2 + 4, S / 2 - r2 + 4, S / 2 + r2 - 4, S / 2 + r2 - 4], fill=70)
    inner = inner.filter(ImageFilter.GaussianBlur(S * .03))
    canvas = Image.composite(Image.new('RGBA', (S, S), (6, 5, 5, 255)), canvas, inner)

    # 黑: 원 안에 크게, 그림자와 함께
    g = glyph.copy()
    target = S * .60
    scale = target / max(g.size)
    g = g.resize((int(g.width * scale), int(g.height * scale)), Image.LANCZOS)
    gx, gy = (S - g.width) // 2, int((S - g.height) / 2 - S * .005)
    sh = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    a = g.split()[3].filter(ImageFilter.GaussianBlur(S * .012))
    sh.paste(Image.new('RGBA', g.size, (0, 0, 0, 255)), (gx + int(S * .012), gy + int(S * .016)), a)
    canvas = Image.alpha_composite(canvas, Image.fromarray((np.array(sh) * np.array([1, 1, 1, .85])).astype(np.uint8)))
    canvas.alpha_composite(g, (gx, gy))

    # 둥근 모서리 사각형으로 잘라 바탕화면에서 보기 좋게(투명 모서리)
    m = Image.new('L', (S, S), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * .18), fill=255)
    canvas.putalpha(ImageChops.multiply(canvas.split()[3], m))
    return canvas


def splash_logo():
    """시작 화면 로고: 제목 붓글씨 + 아래에 한글 제목(2026-11-16 팀 이름은 뺐다). 배경은 투명(Unity 시작 화면 배경색이 깔린다)."""
    W, H = 2048, 1100
    canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    t = title.copy()
    scale = (W * .86) / t.width
    t = t.resize((int(t.width * scale), int(t.height * scale)), Image.LANCZOS)
    tx, ty = (W - t.width) // 2, 40
    sh = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    sh.paste(Image.new('RGBA', t.size, (0, 0, 0, 200)), (tx + 10, ty + 14), t.split()[3].filter(ImageFilter.GaussianBlur(10)))
    canvas = Image.alpha_composite(canvas, sh)
    canvas.alpha_composite(t, (tx, ty))
    d = ImageDraw.Draw(canvas)
    font_k = ImageFont.truetype(r'C:\Windows\Fonts\HANBatangB.ttf', 88)
    font_e = ImageFont.truetype(r'C:\Windows\Fonts\HANBatang.ttf', 46)
    y = ty + t.height + 24
    for text, font, color, gap in (('흑  운  협', font_k, (238, 214, 164, 255), 0),):
        w = d.textlength(text, font=font)
        d.text(((W - w) / 2, y), text, font=font, fill=color)
        y += font.size + gap
    # 한글 제목과 팀 이름 사이 금색 가는 줄
    return canvas.crop(canvas.getbbox())


ic = icon(1024)
# 2026-11-16: 게임 아이콘은 시작 화면 배경 아이콘(make_icon_startscreen.py)으로 바꿨다. 예전 黑 아이콘은 미리보기 폴더에만 남긴다.
ic.save(os.path.join(PREVIEW, 'icon_hanja.png'))
sp = splash_logo()
sp.save(os.path.join(OUT, 'HeukUnHyeop_SplashLogo.png'))

# 미리보기: 아이콘 크기별 + 시작 화면 모습(배경색 위)
prev = Image.new('RGB', (1800, 1080), (20, 18, 18))
prev.paste(ic.resize((512, 512), Image.LANCZOS), (60, 60), ic.resize((512, 512), Image.LANCZOS))
x = 620
for s in (256, 128, 64, 48, 32, 16):
    small = ic.resize((s, s), Image.LANCZOS)
    prev.paste(small, (x, 60 + (256 - s)), small)
    x += s + 30
bg = Image.new('RGB', (1100, 620), (20, 17, 16))
lg = sp.copy(); lg.thumbnail((760, 420), Image.LANCZOS)
bg.paste(lg, ((1100 - lg.width) // 2, (620 - lg.height) // 2), lg)
prev.paste(bg, (640, 400))
ImageDraw.Draw(prev).text((660, 1030), 'startup screen preview (Unity splash background 0.08,0.07,0.065)', fill=(160, 150, 140))
prev.save(os.path.join(PREVIEW, 'branding_preview.png'))
print('ok', ic.size, sp.size)
