"""게임 시작 연출(GameIntro)에 쓰는 그림을 코드로 그린다. 결과: Assets/Resources/UI/Intro/*.png

먹구름 좌우 한 쌍, 붉은 낙관(黑雲峽), 먹 번짐, 먹물 방울, 검기 줄, 부드러운 빛, 한지 바탕.
그림 생성 서비스 없이 Pillow·numpy 로만 만든다. 다시 만들 때: py -3.11 Tools/Developer/make_intro_art.py
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'UI', 'Intro')
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1116)


def fractal(w, h, octaves=5, seed=0):
    """부드러운 프랙탈 잡음(0~1)."""
    r = np.random.default_rng(seed)
    acc = np.zeros((h, w), np.float32); amp = 1.0; total = 0
    for o in range(octaves):
        cell = max(2, int(min(w, h) / (2 ** (o + 1))))
        small = r.random((h // cell + 2, w // cell + 2)).astype(np.float32)
        layer = np.array(Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
        acc += layer * amp; total += amp; amp *= .55
    acc /= total
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-6)


def save(img, name):
    img.save(os.path.join(OUT, name)); print('saved', name, img.size)


# ── 먹구름 (왼쪽 덩어리). 바깥쪽은 꽉 차고 안쪽 가장자리는 구름결로 풀린다. 오른쪽은 좌우를 뒤집어 쓴다.
def cloud_half(w=1400, h=1200, seed=3):
    n = fractal(w, h, 6, seed)
    n2 = fractal(w, h, 4, seed + 9)
    x = np.linspace(0, 1, w, dtype=np.float32)[None, :]
    y = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    # 안쪽 가장자리 위치가 높이에 따라 출렁인다(뭉게구름 테두리).
    edge = .70 + .16 * (n2 - .5) * 2 + .05 * np.sin(y * 9.0 + 1.3)
    a = np.clip((edge - x) / .16 + (n - .5) * 1.2, 0, 1)
    a = a ** 1.2
    shade = .55 + .45 * n                       # 구름결 명암
    base = np.array([16, 13, 12], np.float32); lit = np.array([58, 50, 46], np.float32)
    rgb = base * (1 - shade[..., None] * .55) + lit * (shade[..., None] * .55) * (1 - x[..., None] * .2)
    # 테두리 쪽에 아주 옅은 금빛 기운(제목 금색과 어울리게)
    rim = np.clip(1 - np.abs(x - edge) / .07, 0, 1) * (a > .05) * .35
    rgb = rgb + np.array([70, 52, 26], np.float32) * rim[..., None]
    img = np.dstack([np.clip(rgb, 0, 255), a * 255]).astype(np.uint8)
    im = Image.fromarray(img, 'RGBA').filter(ImageFilter.GaussianBlur(1.2))
    return im


left = cloud_half(seed=3)
save(left, 'CloudLeft.png')
save(cloud_half(seed=17).transpose(Image.FLIP_LEFT_RIGHT), 'CloudRight.png')


# ── 붉은 낙관: 세로로 긴 인장에 黑雲峽 을 하얗게 새긴다(먹이 덜 묻은 자국 포함).
def seal():
    W, H = 360, 760
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    m = Image.new('L', (W, H), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([12, 12, W - 12, H - 12], radius=34, fill=255)
    font = ImageFont.truetype(r'C:\Windows\Fonts\HANBatangB.ttf', 224)
    text = Image.new('L', (W, H), 0)
    td = ImageDraw.Draw(text)
    for i, ch in enumerate('黑雲峽'):
        bw = td.textlength(ch, font=font)
        td.text(((W - bw) / 2, 22 + i * 236), ch, font=font, fill=255)
    text = text.filter(ImageFilter.GaussianBlur(.8))
    grain = fractal(W, H, 5, 41)
    speck = (np.array(rng.random((H, W)) > .985, np.float32))
    a = np.array(m, np.float32) / 255
    a *= np.clip(.78 + grain * .35, 0, 1)                 # 인주가 고르지 않게
    a *= 1 - np.array(text, np.float32) / 255 * .96      # 글자는 새김(비어 있음)
    a *= 1 - speck * .8
    edge = np.array(m.filter(ImageFilter.GaussianBlur(6)), np.float32) / 255
    a *= np.clip((edge - .25) * 3 + grain * .3, 0, 1)    # 테두리가 거칠게
    rgb = np.zeros((H, W, 3), np.float32)
    rgb[...] = (176, 30, 24)
    rgb *= (.85 + .3 * grain)[..., None]
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a * 255]).astype(np.uint8), 'RGBA')


save(seal(), 'Seal.png')


# ── 먹 번짐: 가운데서 퍼져 나간 큰 먹물 자국(가장자리 갈래 + 튄 방울).
def ink_blot(S=1400):
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    cx = cy = S / 2
    ang = np.arctan2(yy - cy, xx - cx); dist = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (S / 2)
    wob = np.zeros_like(ang)
    for k, amp in ((3, .05), (7, .06), (13, .04), (23, .03), (41, .02)):
        wob += amp * np.sin(ang * k + rng.uniform(0, 6.28))
    radius = .62 + wob
    n = fractal(S, S, 5, 77)
    a = np.clip((radius - dist) / .05 + (n - .5) * .8, 0, 1)
    # 갈래(가는 먹줄)
    rays = np.zeros_like(a)
    for _ in range(28):
        t = rng.uniform(0, 6.28); length = rng.uniform(.70, .98); width = rng.uniform(.006, .02)
        da = np.abs(np.angle(np.exp(1j * (ang - t))))
        rays = np.maximum(rays, np.clip(1 - da / (width / np.maximum(dist, .05)), 0, 1) * (dist < length) * (dist > .5))
    a = np.maximum(a, rays * .95)
    im = Image.fromarray((a * 255).astype(np.uint8), 'L')
    d = ImageDraw.Draw(im)
    for _ in range(140):                                  # 튄 방울
        t = rng.uniform(0, 6.28); r = rng.uniform(.62, .98) * S / 2; s = rng.uniform(2, 14)
        x, y = cx + np.cos(t) * r, cy + np.sin(t) * r
        d.ellipse([x - s, y - s, x + s, y + s], fill=int(rng.uniform(150, 255)))
    im = im.filter(ImageFilter.GaussianBlur(1.5))
    rgb = np.zeros((S, S, 3), np.uint8); rgb[...] = (8, 7, 7)
    return Image.fromarray(np.dstack([rgb, np.array(im)]), 'RGBA')


save(ink_blot(), 'InkBlot.png')


# ── 먹물 방울
def ink_drop():
    W, H = 140, 260
    m = Image.new('L', (W, H), 0); d = ImageDraw.Draw(m)
    d.ellipse([20, 120, 120, 220], fill=255)
    d.polygon([(70, 10), (24, 160), (116, 160)], fill=255)
    m = m.filter(ImageFilter.GaussianBlur(3))
    rgb = np.zeros((H, W, 3), np.uint8); rgb[...] = (10, 9, 9)
    im = Image.fromarray(np.dstack([rgb, np.array(m)]), 'RGBA')
    hi = Image.new('RGBA', (W, H), (0, 0, 0, 0)); ImageDraw.Draw(hi).ellipse([46, 140, 62, 170], fill=(255, 255, 255, 120))
    return Image.alpha_composite(im, hi.filter(ImageFilter.GaussianBlur(3)))


save(ink_drop(), 'InkDrop.png')


# ── 검기 줄: 가운데가 하얗게 빛나고 양 끝이 가늘어지는 금빛 줄.
def slash(W=2048, H=96):
    x = np.linspace(-1, 1, W, dtype=np.float32)[None, :]
    y = np.linspace(-1, 1, H, dtype=np.float32)[:, None]
    taper = np.clip(1 - np.abs(x) ** 3, 0, 1)
    core = np.exp(-(y / (.06 * taper + .01)) ** 2) * taper
    glow = np.exp(-(y / (.45 * taper + .02)) ** 2) * taper * .55
    a = np.clip(core + glow, 0, 1)
    rgb = np.dstack([255 * np.ones_like(a), 236 - 40 * (1 - core), 190 - 120 * (1 - core)])
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a * 255]).astype(np.uint8), 'RGBA')


save(slash(), 'Slash.png')


# ── 부드러운 빛(붓끝·섬광)
def glow(S=256):
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    d = np.sqrt((xx - S / 2) ** 2 + (yy - S / 2) ** 2) / (S / 2)
    a = np.clip(1 - d, 0, 1) ** 2.2
    rgb = np.full((S, S, 3), 255, np.uint8)
    return Image.fromarray(np.dstack([rgb, (a * 255).astype(np.uint8)]), 'RGBA')


save(glow(), 'Glow.png')


# ── 한지 바탕(먹 방울 연출): 따뜻한 미색에 섬유결
def paper(W=1920, H=1080):
    n = fractal(W, H, 6, 5); f = fractal(W, H, 3, 8)
    fibers = Image.new('L', (W, H), 0); d = ImageDraw.Draw(fibers)
    for _ in range(900):
        x, y = rng.uniform(0, W), rng.uniform(0, H); a = rng.uniform(0, 6.28); l = rng.uniform(10, 60)
        d.line([x, y, x + np.cos(a) * l, y + np.sin(a) * l], fill=int(rng.uniform(20, 60)), width=1)
    fib = np.array(fibers.filter(ImageFilter.GaussianBlur(.6)), np.float32) / 255
    base = np.array([233, 222, 199], np.float32)
    rgb = base * (.93 + .07 * n[..., None]) - 25 * fib[..., None] - 18 * (f[..., None] - .5)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    vig = ((xx / W - .5) ** 2 + (yy / H - .5) ** 2) * 1.3
    rgb *= (1 - vig[..., None] * .35)
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), 'RGB')


save(paper(), 'Paper.png')
