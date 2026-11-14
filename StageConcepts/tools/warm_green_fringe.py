"""Turn the yellow-green halo that a green chroma background leaves around fire-lit edges into warm orange.
Use after process_remake.py on a cutout whose subject has no real green or yellow-green in it.
usage: warm_green_fringe.py <ready/cutout.png>
"""
import os, sys
import cv2
import numpy as np

path = sys.argv[1]
rgba = cv2.imread(path, cv2.IMREAD_UNCHANGED).astype(np.float32)
b, g, r, a = rgba[..., 0], rgba[..., 1], rgba[..., 2], rgba[..., 3]
limit = np.maximum(r * 0.74, b)          # green may not exceed what an orange or a neutral grey would have
changed = g > limit
g2 = np.minimum(g, limit)
out = np.dstack([b, g2, r, a]).astype(np.uint8)
cv2.imwrite(path, out)
h, w = out.shape[:2]
yy, xx = np.mgrid[0:h, 0:w]
board = (((yy // 24 + xx // 24) % 2) * 60 + 150).astype(np.float32)
al = out[..., 3:4].astype(np.float32) / 255
preview = out[..., :3].astype(np.float32) * al + board[..., None] * (1 - al)
cv2.imwrite(os.path.splitext(path)[0] + '_preview.jpg', preview.astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 88])
print('warmed %.1f%% of pixels' % (100.0 * changed.mean()))
