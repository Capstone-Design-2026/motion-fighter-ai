"""Prepare the art of the Underground Pit arena.
  raw/*.png (generated on a flat magenta background)  ->  ready/*.png
  - cut-outs: key out the magenta, clean the edges, bleed colours outward so filtering shows no fringe
  - strips  : keep the full width and append a mirrored copy, so the strip repeats seamlessly around the ring
  - hanging : split the three props into separate pictures
  - corner  : the red corner is the blue one with its blues turned to red
  - floor   : mirrored 2x2 tile, a normal map from its brightness, and a worn paint ring that follows the stones
  - fx      : soft sprites for glows, flames, the light shaft and haze
usage: process_pit.py <raw dir> <ready dir>
"""
import os, sys
import cv2
import numpy as np

raw, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
rng = np.random.default_rng(7)


def read(name):
    im = cv2.imread(os.path.join(raw, name + '.png'), cv2.IMREAD_COLOR)
    if im is None: raise SystemExit('missing ' + name)
    return im.astype(np.float32)


def key(bgr):
    """magenta key -> (rgb float BGR order, alpha 0..1)"""
    b, g, r = bgr[..., 0], bgr[..., 1], bgr[..., 2]
    spill = np.minimum(r, b) - g
    alpha = 1.0 - np.clip((spill - 45.0) / 80.0, 0, 1)
    cast = np.clip(spill, 0, None) * 0.95
    rgb = np.clip(np.dstack([b - cast, g, r - cast]), 0, 255)
    alpha = cv2.erode(alpha, np.ones((3, 3), np.uint8), iterations=1)
    alpha = cv2.GaussianBlur(alpha, (0, 0), 0.8)
    # bleed the object's colours into the transparent area
    weight = (alpha > 0.6).astype(np.float32)
    acc, wacc = rgb * weight[..., None], weight.copy()
    for _ in range(6):
        acc = cv2.GaussianBlur(acc, (0, 0), 6); wacc = cv2.GaussianBlur(wacc, (0, 0), 6)
        fill = acc / np.maximum(wacc[..., None], 1e-4)
        rgb = np.where(weight[..., None] > 0, rgb, fill)
        known = np.maximum(weight, (wacc > 0.02).astype(np.float32))
        acc, wacc = rgb * known[..., None], known
    return rgb, alpha


def save(name, rgb, alpha):
    rgba = np.dstack([rgb, alpha * 255.0]).clip(0, 255).astype(np.uint8)
    cv2.imwrite(os.path.join(out, name + '.png'), rgba)
    h, w = rgba.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    board = (((yy // 24 + xx // 24) % 2) * 60 + 150).astype(np.float32)
    a = rgba[..., 3:4].astype(np.float32) / 255
    cv2.imwrite(os.path.join(out, '_preview_' + name + '.jpg'), (rgba[..., :3] * a + board[..., None] * (1 - a)).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 85])
    print('%-14s %4d x %4d  opaque %.0f%%' % (name, w, h, 100 * (a > 0.5).mean()))
    return w, h


def rows(alpha):
    ys = np.where(alpha.max(axis=1) > 0.08)[0]
    return max(0, ys.min() - 4), min(alpha.shape[0], ys.max() + 2)


def cols(alpha):
    xs = np.where(alpha.max(axis=0) > 0.08)[0]
    return max(0, xs.min() - 6), min(alpha.shape[1], xs.max() + 7)


def single(name, outname=None):
    rgb, alpha = key(read(name))
    y0, y1 = rows(alpha); x0, x1 = cols(alpha)
    save(outname or name, rgb[y0:y1, x0:x1], alpha[y0:y1, x0:x1])
    return rgb[y0:y1, x0:x1], alpha[y0:y1, x0:x1]


def strip(name):
    rgb, alpha = key(read(name))
    y0, y1 = rows(alpha)
    rgb, alpha = rgb[y0:y1], alpha[y0:y1]
    save(name, np.hstack([rgb, rgb[:, ::-1]]), np.hstack([alpha, alpha[:, ::-1]]))


def torch_columns(name, lo_frac, hi_frac):
    """x positions (0..1 across the single, unmirrored picture) and heights (0..1 from the bottom) of the brightest flame spots"""
    bgr = read(name); rgb, alpha = key(bgr)
    y0, y1 = rows(alpha); bgr = bgr[y0:y1]
    h, w = bgr.shape[:2]
    warm = np.clip(bgr[..., 2] - 150, 0, None) * np.clip(bgr[..., 1] - 90, 0, None) / 100.0 * (bgr[..., 0] < 150)
    warm[: int(h * (1 - hi_frac))] = 0; warm[int(h * (1 - lo_frac)):] = 0
    warm = cv2.GaussianBlur(warm, (0, 0), 9)
    spots = []
    work = warm.copy()
    for _ in range(12):
        y, x = np.unravel_index(np.argmax(work), work.shape)
        if work[y, x] < warm.max() * 0.35: break
        spots.append((x / w, 1 - y / h))
        cv2.circle(work, (int(x), int(y)), int(w * 0.07), 0, -1)
    spots.sort()
    print('%-14s flame spots (u, v from bottom): %s' % (name, ' '.join('(%.3f, %.3f)' % s for s in spots)))
    return spots


# ── cut-outs ──
single('gate')
strip('palisade')
strip('gallery')
strip('stalactites')
blue_rgb, blue_a = single('corner_blue')

# red corner: turn blues into reds, leave wood and iron alone
hsv = cv2.cvtColor(blue_rgb.clip(0, 255).astype(np.uint8), cv2.COLOR_BGR2HSV).astype(np.int32)
blueish = (hsv[..., 0] >= 88) & (hsv[..., 0] <= 135) & (hsv[..., 1] > 50)
hsv[..., 0] = np.where(blueish, np.round((hsv[..., 0] - 113) * 0.3).astype(np.int32) % 180, hsv[..., 0])   # every blue lands close to pure red
hsv[..., 1] = np.where(blueish, np.clip(hsv[..., 1] * 1.05, 0, 255), hsv[..., 1])
red_rgb = cv2.cvtColor(hsv.astype(np.uint8), cv2.COLOR_HSV2BGR).astype(np.float32)
save('corner_red', red_rgb[:, ::-1], blue_a[:, ::-1])       # mirrored, so the two corners face each other

# hanging props: split at the empty columns
rgb, alpha = key(read('hanging'))
filled = alpha.max(axis=0) > 0.08
parts, start = [], None
for x, f in enumerate(filled):
    if f and start is None: start = x
    if not f and start is not None:
        if x - start > 40: parts.append((start, x))
        start = None
if start is not None: parts.append((start, len(filled)))
for (x0, x1), name in zip(parts, ['hang_cage', 'hang_brazier', 'hang_lamp']):
    x0, x1 = max(0, x0 - 6), min(alpha.shape[1], x1 + 6)
    sub_a = alpha[:, x0:x1]; ys = np.where(sub_a.max(axis=1) > 0.08)[0]
    save(name, rgb[ys.min():ys.max() + 2, x0:x1], sub_a[ys.min():ys.max() + 2])
print('hanging parts', parts)

# ── cavern wall: opaque, mirrored so it wraps around ──
cav = read('cavern')
cv2.imwrite(os.path.join(out, 'cavern.png'), np.hstack([cav, cav[:, ::-1]]).astype(np.uint8))
print('cavern         %d x %d' % (cav.shape[1] * 2, cav.shape[0]))

# ── where the painted flames are (the builder puts glows and lights there) ──
with open(os.path.join(out, 'flames.txt'), 'w') as f:
    for name, lo, hi in (('palisade', .35, .95), ('gate', .15, .7), ('gallery', .2, .98)):
        for u, v in torch_columns(name, lo, hi): f.write('%s %.4f %.4f\n' % (name, u, v))

# ── floor ──
def seamless(im):
    """make a picture repeat without a seam and without mirroring: cross-fade its borders with a half-shifted copy, one axis at a time"""
    im = im.astype(np.float32); h, w = im.shape[:2]
    def ramp(n):
        d = np.abs(np.arange(n, dtype=np.float32) + .5 - n / 2) / (n / 2)       # 0 centre .. 1 border
        t = np.clip((0.94 - d) / 0.26, 0, 1)
        return t * t * (3 - 2 * t)
    mx = ramp(w)[None, :, None]; im = im * mx + np.roll(im, w // 2, axis=1) * (1 - mx)
    my = ramp(h)[:, None, None]; im = im * my + np.roll(im, h // 2, axis=0) * (1 - my)
    return im.clip(0, 255).astype(np.uint8)

tile = seamless(read('floor'))                                 # one tile = 4 m
cv2.imwrite(os.path.join(out, 'floor_albedo.png'), tile)
def bump(img, k):
    g = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY).astype(np.float32) / 255
    height = 1.0 - cv2.GaussianBlur(g, (0, 0), 1.4)            # pale sand lies in the joints, so brighter = lower
    pad = cv2.copyMakeBorder(height, 8, 8, 8, 8, cv2.BORDER_WRAP)
    dx = cv2.Sobel(pad, cv2.CV_32F, 1, 0, ksize=3)[8:-8, 8:-8]; dy = cv2.Sobel(pad, cv2.CV_32F, 0, 1, ksize=3)[8:-8, 8:-8]
    nx, ny, nz = -dx * k, dy * k, np.ones_like(g)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    return (np.dstack([nz / n, ny / n, nx / n]) * 0.5 + 0.5).clip(0, 1)
cv2.imwrite(os.path.join(out, 'floor_normal.png'), (bump(tile, 2.4) * 255).astype(np.uint8))
print('floor tile     %d x %d (seamless, not mirrored)' % (tile.shape[1], tile.shape[0]))

# worn paint ring, drawn over the middle 2 x 2 floor tiles (8 m): paint stays on the stones and is gone from the sandy joints.
# Stored for a 2x-multiply blend: 0.5 = no change. Linear texture.
big = np.tile(tile, (2, 2, 1)); gray = cv2.cvtColor(big, cv2.COLOR_BGR2GRAY).astype(np.float32) / 255
S = big.shape[0]; M = 8.0                                      # pixels, metres
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
mx, mz = (xx / S - .5) * M, (yy / S - .5) * M
rad = np.sqrt(mx * mx + mz * mz)
def band(d, half, soft=.02): return np.clip((half - np.abs(d)) / soft, 0, 1)
ring_red = band(rad - 3.45, .13)
ring_pale = np.maximum(band(rad - 3.12, .035), band(rad - 0.5, .03))
ticks = np.maximum(band(np.abs(mx) - 0.95, .04) * (np.abs(mz) < .55), 0)      # the two starting lines
pale = np.maximum(ring_pale, ticks)
stone = np.clip((0.56 - cv2.GaussianBlur(gray, (0, 0), 1.0)) / 0.16, 0, 1)     # 1 on dark stone, 0 in pale sand
noise = cv2.GaussianBlur(rng.random((S, S)).astype(np.float32), (0, 0), 9); noise = (noise - noise.min()) / (noise.max() - noise.min())
patch = cv2.GaussianBlur(rng.random((S, S)).astype(np.float32), (0, 0), 70); patch = (patch - patch.min()) / (patch.max() - patch.min())
wear = stone * np.clip((noise - .30) / .25, 0, 1) * (0.35 + 0.65 * np.clip((patch - .25) / .4, 0, 1))
decal = np.full((S, S, 3), 0.5, np.float32)
red = np.array([0.24, 0.26, 0.78], np.float32); chalk = np.array([0.70, 0.80, 0.86], np.float32)   # BGR
for mask, colour, strength in ((ring_red, red, .95), (pale, chalk, .8)):
    m = (mask * wear * strength)[..., None]
    decal = decal * (1 - m) + colour * m
cv2.imwrite(os.path.join(out, 'floor_ring.png'), (decal * 255).clip(0, 255).astype(np.uint8))
print('floor ring     %d x %d (paint coverage %.1f%%)' % (S, S, 100 * ((ring_red + pale) * wear > .3).mean()))

# large uneven shading over the whole pit floor (40 m): drifts of pale sand, darker trodden and damp patches, darker toward the wall.
# It never repeats, so it hides the 4 m repeat of the tile. 2x-multiply, 0.5 = no change, linear.
N = 1024; yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
def cloud(sigma):
    c = cv2.GaussianBlur(rng.random((N, N)).astype(np.float32), (0, 0), sigma); return (c - c.min()) / (c.max() - c.min())
big_patch = cloud(60) * .6 + cloud(24) * .3 + cloud(9) * .1
sx, sz = (xx / N - .5) * 40.0, (.5 - yy / N) * 40.0           # metres; image top = far side (+z)
r = np.sqrt(sx * sx + sz * sz)
toward_wall = np.clip((r - 6.0) / 6.0, 0, 1)                  # 0 in the middle, 1 at the wall
light_pool = np.exp(-((sx / 5.5) ** 2 + ((sz - 2.4) / 4.2) ** 2))   # where the roof shaft lands
value = 0.5 + (big_patch - .5) * .22 - toward_wall * .13 + light_pool * .10
warm = (big_patch - .5) * .05                                  # sand drifts are a little warmer, damp patches a little cooler
shade = np.dstack([value - warm, value, value + warm])         # BGR
cv2.imwrite(os.path.join(out, 'floor_shade.png'), (shade * 255).clip(0, 255).astype(np.uint8))
print('floor shade    %d x %d' % (N, N))

# ── effect sprites (white, shape in alpha) ──
def sprite(name, a):
    h, w = a.shape
    cv2.imwrite(os.path.join(out, name + '.png'), np.dstack([np.full((h, w, 3), 255, np.uint8), (a.clip(0, 1) * 255).astype(np.uint8)]))
    print('%-14s %d x %d' % (name, w, h))

n = 128; yy, xx = np.mgrid[0:n, 0:n].astype(np.float32); d = np.sqrt((xx - n / 2 + .5) ** 2 + (yy - n / 2 + .5) ** 2) / (n / 2)
sprite('fx_glow', np.clip(1 - d, 0, 1) ** 2.2)
# flame: a teardrop with a ragged edge
n = 128; yy, xx = np.mgrid[0:n, 0:n].astype(np.float32); u = (xx + .5) / n * 2 - 1; v = 1 - (yy + .5) / n      # v: 0 bottom .. 1 top
half = 0.62 * np.sqrt(np.clip(v * 1.6, 0, 1)) * np.clip(1 - v, 0, 1) ** 0.75 * 1.9
rag = cv2.GaussianBlur(rng.random((n, n)).astype(np.float32), (0, 0), 5); rag = (rag - rag.min()) / (rag.max() - rag.min())
sprite('fx_flame', np.clip((half * (0.75 + 0.5 * rag) - np.abs(u)) / .22, 0, 1) * np.clip(v / .08, 0, 1))
# light shaft: bright core, soft sides, long streaks, fading toward the floor end
w, h = 128, 512; yy, xx = np.mgrid[0:h, 0:w].astype(np.float32); u = (xx + .5) / w * 2 - 1; v = 1 - (yy + .5) / h
streak = cv2.GaussianBlur(rng.random((1, w)).astype(np.float32), (0, 0), 2.5); streak = (streak - streak.min()) / (streak.max() - streak.min())
sprite('fx_shaft', np.clip(1 - np.abs(u), 0, 1) ** 1.6 * (0.55 + 0.45 * np.repeat(streak, h, axis=0)) * np.clip(v / .25, 0, 1) ** 1.2 * np.clip((1 - v) / .12, 0, 1))
# haze puff
n = 256; yy, xx = np.mgrid[0:n, 0:n].astype(np.float32); d = np.sqrt((xx - n / 2) ** 2 + (yy - n / 2) ** 2) / (n / 2)
cloud = sum(cv2.GaussianBlur(rng.random((n, n)).astype(np.float32), (0, 0), s) * wgt for s, wgt in ((28, 1.0), (12, .5), (5, .25)))
cloud = (cloud - cloud.min()) / (cloud.max() - cloud.min())
sprite('fx_haze', np.clip(1 - d, 0, 1) ** 1.4 * np.clip((cloud - .25) / .5, 0, 1))
