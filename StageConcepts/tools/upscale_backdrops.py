import glob, os, sys
from PIL import Image, ImageFilter

src = sys.argv[1]
out = os.path.join(src, 'x2')
os.makedirs(out, exist_ok=True)
for path in sorted(glob.glob(os.path.join(src, '*.png'))):
    im = Image.open(path).convert('RGB')
    w, h = im.size
    big = im.resize((w * 2, h * 2), Image.LANCZOS)
    # 확대하면서 뭉개지는 윤곽을 살짝 되살린다 (과하면 테두리가 생기므로 약하게).
    big = big.filter(ImageFilter.UnsharpMask(radius=1.6, percent=70, threshold=2))
    big.save(os.path.join(out, os.path.basename(path)), optimize=True)
    print(os.path.basename(path), big.size)
