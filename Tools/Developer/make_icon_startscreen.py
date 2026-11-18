"""시작 화면 배경(하늘·정자 풍경·소나무 가지)으로 만든 아이콘 시안 4종 + 실제 크기 비교 그림.
결과: <출력 폴더>/icon3_*.png, icon_options3_sheet.png
사용: py -3.11 Tools/Developer/make_icon_startscreen.py <출력 폴더>
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops, ImageEnhance

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
A = lambda *p: os.path.join(ROOT, *p)
S = 1024
FONT = r'C:\Windows\Fonts\HANBatangB.ttf'

art = Image.open(A('Assets/Resources/UI/Wuxia/StartCourtyard.png')).convert('RGBA')
art.alpha_composite(Image.open(A('Assets/Resources/UI/Wuxia/StartForeground.png')).convert('RGBA'))
AW, AH = art.size


def gold():
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    t = np.clip(xx / S * .55 + yy / S * .45, 0, 1)
    hi, mid, lo = np.array([252, 230, 172]), np.array([214, 166, 92]), np.array([120, 78, 34])
    c = np.where(t[..., None] < .5, hi + (mid - hi) * (t[..., None] / .5), mid + (lo - mid) * ((t[..., None] - .5) / .5))
    return Image.fromarray(np.clip(c + np.random.default_rng(3).normal(0, 8, (S, S, 1)), 0, 255).astype(np.uint8), 'RGB').convert('RGBA')


def crop_square(cx, cy, size):
    """그림에서 (cx, cy)(0~1) 가운데의 정사각형(크기는 그림 높이 비율)을 잘라 S x S 로."""
    side = int(AH * size); x = int(cx * AW - side / 2); y = int(cy * AH - side / 2)
    x = max(0, min(AW - side, x)); y = max(0, min(AH - side, y))
    return art.crop((x, y, x + side, y + side)).resize((S, S), Image.LANCZOS)


def punch(img, vignette=.45):
    """작은 크기에서도 읽히게: 색·대비를 조금 올리고 가장자리를 어둡게."""
    img = ImageEnhance.Color(img).enhance(1.18); img = ImageEnhance.Contrast(img).enhance(1.12)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    d = np.clip(np.sqrt((xx - S / 2) ** 2 + (yy - S / 2) ** 2) / (S * .72), 0, 1) ** 2
    arr = np.asarray(img, np.float32); arr[..., :3] *= (1 - vignette * d)[..., None]
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), 'RGBA')


def rounded_framed(img, frame_w=.018):
    c = img.copy()
    ring = Image.new('L', (S, S), 0)
    ImageDraw.Draw(ring).rounded_rectangle([S * .006, S * .006, S * .994, S * .994], radius=int(S * .175), outline=255, width=int(S * frame_w * 1.6))
    sh = Image.new('RGBA', (S, S), (0, 0, 0, 0)); sh.putalpha(ring.filter(ImageFilter.GaussianBlur(6)).point(lambda v: int(v * .6)))
    c.alpha_composite(ImageChops.offset(sh, 3, 5))
    g = gold(); g.putalpha(ring); c.alpha_composite(g)
    m = Image.new('L', (S, S), 0); ImageDraw.Draw(m).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * .18), fill=255)
    c.putalpha(ImageChops.multiply(c.split()[3], m))
    return c


def medallion(img):
    """먹색 바탕 위 금빛 원 안에 풍경(인장처럼)."""
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    d = np.clip(np.sqrt((xx - S / 2) ** 2 + (yy - S * .46) ** 2) / (S * .66), 0, 1) ** 1.6
    bg = (np.array([46, 36, 30], np.float32) * (1 - d[..., None]) + np.array([8, 7, 7], np.float32) * d[..., None]).astype(np.uint8)
    c = Image.fromarray(bg, 'RGB').convert('RGBA')
    circle = Image.new('L', (S, S), 0); ImageDraw.Draw(circle).ellipse([S * .1, S * .1, S * .9, S * .9], fill=255)
    pic = img.copy(); pic.putalpha(circle); c.alpha_composite(pic)
    ring = Image.new('L', (S, S), 0); ImageDraw.Draw(ring).ellipse([S * .09, S * .09, S * .91, S * .91], outline=255, width=int(S * .032))
    sh = Image.new('RGBA', (S, S), (0, 0, 0, 0)); sh.putalpha(ring.filter(ImageFilter.GaussianBlur(8))); c.alpha_composite(ImageChops.offset(sh, 4, 6))
    g = gold(); g.putalpha(ring); c.alpha_composite(g)
    m = Image.new('L', (S, S), 0); ImageDraw.Draw(m).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * .18), fill=255)
    c.putalpha(ImageChops.multiply(c.split()[3], m))
    return c


options = [
    ('A  정자와 노을', rounded_framed(punch(crop_square(.78, .5, 1.0)))),
    ('B  산과 해', rounded_framed(punch(crop_square(.6, .42, .86)))),
    ('C  소나무 절벽과 정자', rounded_framed(punch(crop_square(.62, .6, .82)))),
    ('D  금빛 원 안의 풍경', medallion(punch(crop_square(.74, .46, .9), .3))),
]
for i, (name, img) in enumerate(options, 1):
    img.save(os.path.join(OUT, 'icon3_%s.png' % 'ABCD'[i - 1]))
# 2026-11-16 사용자 선택: A(정자와 노을)가 게임 아이콘이다(RedboolBranding.IconPath).
options[0][1].save(A('Assets/Branding/HeukUnHyeop_Icon.png'))

font = ImageFont.truetype(FONT, 30); small = ImageFont.truetype(FONT, 20)
row_h = 350; Wd = 1500
sheet = Image.new('RGB', (Wd, 70 + row_h * len(options)), (34, 32, 32)); d = ImageDraw.Draw(sheet)
d.text((30, 20), '시작 화면 배경으로 만든 아이콘 · 왼쪽은 실제 크기(256 / 64 / 32 / 16px), 오른쪽은 작업 표시줄에 놓였을 때', font=small, fill=(220, 214, 200))
for r, (name, img) in enumerate(options):
    y = 70 + r * row_h
    d.text((30, y + 4), name, font=font, fill=(250, 232, 190)); y += 50
    x = 30
    for size in (256, 64, 32, 16):
        im = img.resize((size, size), Image.LANCZOS); sheet.paste(im, (x, y + (256 - size)), im); x += size + 40
    for bg, bx in (((32, 32, 36), 760), ((238, 238, 242), 1130)):
        d.rectangle([bx, y + 170, bx + 340, y + 230], fill=bg)
        for k in range(5):
            ix = bx + 18 + k * 64
            if k == 2:
                im = img.resize((32, 32), Image.LANCZOS); sheet.paste(im, (ix, y + 184), im); d.rectangle([ix + 4, y + 224, ix + 28, y + 227], fill=(110, 170, 255))
            else:
                d.rounded_rectangle([ix + 4, y + 188, ix + 28, y + 212], radius=6, fill=(90, 96, 110) if bg[0] < 100 else (170, 176, 188))
    big = img.resize((150, 150), Image.LANCZOS); sheet.paste(big, (960, y), big)
sheet.save(os.path.join(OUT, 'icon_options3_sheet.png'))
print('saved', os.path.join(OUT, 'icon_options3_sheet.png'))
