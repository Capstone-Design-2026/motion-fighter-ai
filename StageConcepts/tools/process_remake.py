"""Prepare remake stage art.
  cutouts: remove the solid chroma background, clean the edges, crop to the object
  floor  : build a mirrored 2x2 tile (always seamless) and a normal map from its brightness
usage: process_remake.py <stage dir> <magenta|green>
"""
import glob, os, sys
import cv2
import numpy as np

stage_dir, key = sys.argv[1], sys.argv[2]
out_dir = os.path.join(stage_dir, 'ready')
os.makedirs(out_dir, exist_ok=True)


def cutout(path):
    bgr = cv2.imread(path, cv2.IMREAD_COLOR).astype(np.float32)
    b, g, r = bgr[..., 0], bgr[..., 1], bgr[..., 2]
    if key == 'magenta':
        spill = np.minimum(r, b) - g              # how magenta a pixel is
    else:
        spill = g - np.maximum(r, b)              # how green a pixel is
    alpha = 1.0 - np.clip((spill - 45.0) / 80.0, 0, 1)
    # remove the colour cast the background leaves on edges
    cast = np.clip(spill, 0, None) * 0.95
    if key == 'magenta':
        r = r - cast; b = b - cast
    else:
        g = g - cast
    rgb = np.clip(np.dstack([b, g, r]), 0, 255)
    # tighten and soften the edge a little
    alpha = cv2.erode(alpha, np.ones((3, 3), np.uint8), iterations=1)
    alpha = cv2.GaussianBlur(alpha, (0, 0), 0.8)
    # spread object colours into the transparent area so no dark or coloured fringe shows when filtered
    weight = (alpha > 0.6).astype(np.float32)
    acc, wacc = rgb * weight[..., None], weight.copy()
    for _ in range(6):
        acc = cv2.GaussianBlur(acc, (0, 0), 6); wacc = cv2.GaussianBlur(wacc, (0, 0), 6)
        fill = acc / np.maximum(wacc[..., None], 1e-4)
        rgb = np.where(weight[..., None] > 0, rgb, fill)
        acc, wacc = rgb * np.maximum(weight, (wacc > 0.02).astype(np.float32))[..., None], np.maximum(weight, (wacc > 0.02).astype(np.float32))
    ys, xs = np.where(alpha > 0.08)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    pad = 6
    x0, y0 = max(0, x0 - pad), max(0, y0 - pad); x1 = min(alpha.shape[1], x1 + pad); y1 = min(alpha.shape[0], y1 + 2)
    rgba = np.dstack([rgb, alpha * 255.0])[y0:y1, x0:x1].astype(np.uint8)
    name = os.path.splitext(os.path.basename(path))[0]
    cv2.imwrite(os.path.join(out_dir, name + '.png'), rgba)
    # preview on a checkerboard
    h, w = rgba.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    board = (((yy // 24 + xx // 24) % 2) * 60 + 150).astype(np.float32)
    a = rgba[..., 3:4].astype(np.float32) / 255
    preview = rgba[..., :3].astype(np.float32) * a + board[..., None] * (1 - a)
    cv2.imwrite(os.path.join(out_dir, name + '_preview.jpg'), preview.astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 88])
    print(name, 'cutout', w, 'x', h, 'opaque %.0f%%' % (100 * (a > 0.5).mean()))


def floor(path):
    im = cv2.imread(path, cv2.IMREAD_COLOR)
    top = np.hstack([im, im[:, ::-1]])
    tile = np.vstack([top, top[::-1]])
    cv2.imwrite(os.path.join(out_dir, 'floor_albedo.png'), tile)
    gray = cv2.cvtColor(tile, cv2.COLOR_BGR2GRAY).astype(np.float32) / 255
    gray = cv2.GaussianBlur(gray, (0, 0), 1.2)
    dx = cv2.Sobel(gray, cv2.CV_32F, 1, 0, ksize=3); dy = cv2.Sobel(gray, cv2.CV_32F, 0, 1, ksize=3)
    strength = 2.2
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(gray)   # image y points down, texture v points up
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.dstack([nz / n, ny / n, nx / n]) * 0.5 + 0.5          # BGR order
    cv2.imwrite(os.path.join(out_dir, 'floor_normal.png'), (normal * 255).astype(np.uint8))
    print('floor tile', tile.shape[1], 'x', tile.shape[0])


for f in sorted(glob.glob(os.path.join(stage_dir, '*.png'))):
    if os.path.basename(f).startswith('floor'):
        floor(f)
    else:
        cutout(f)
