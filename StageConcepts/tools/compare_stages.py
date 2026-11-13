"""Before/after pictures for the 11 story stages: the scene the game loads now (left) vs the new version (right).
Takes the same frame from Docs/StoryStages/Video and Docs/HybridStages/Video (both were recorded with the same moves).
Writes one picture per stage and three review sheets to Docs/HybridStages/Compare.
usage: compare_stages.py   (run from the project root)
"""
import os
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

STAGES = [
    (1, '01_scout', '1_scout_moon', '산길 · 달밤 설산', '하늘 교체'),
    (2, '02_river', '2_river_morning', '계곡 · 아침 협곡', '하늘 교체'),
    (3, '03_ridge', '3_ridge_sunrise', '산등성이 · 일출 운해', '리메이크'),
    (4, '04_old_ferry', '4_ferry_dawn', '옛 나루 · 새벽 물안개', '하늘 교체'),
    (5, '05_gate', '5_gate_storm', '폐쇄된 관문 · 먹구름 고갯길', '하늘 교체'),
    (6, '06_quarry', '6_quarry_gold', '채석장 · 황금빛 먼지', '리메이크'),
    (7, '07_east', '7_east_twilight', '동쪽 능선 · 해 진 뒤 노을', '리메이크'),
    (8, '08_camp', '8_camp_stars', '추포대 야영지 · 은하수', '리메이크'),
    (9, '09_rescue', '9_rescue_predawn', '포로 뇌옥 · 동트기 전 안개', '리메이크'),
    (10, '10_entrance', '10_entrance_blackcloud', '흑운협 입구 · 붉은 먹구름', '하늘 교체'),
    (11, '11_final', '11_final_sunset', '수문 요새 · 불타는 노을', '리메이크'),
]
FRAME = 0.5            # 영상의 절반 지점 (주인공이 왼쪽으로 걸어가 카메라가 옮겨 간 구도)
W, H, BAR = 960, 540, 64
OUT = 'Docs/HybridStages/Compare'
os.makedirs(OUT, exist_ok=True)
bold = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 30)
small = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 22)


def grab(path, at):
    cap = cv2.VideoCapture(path)
    n = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    cap.set(cv2.CAP_PROP_POS_FRAMES, int(n * at))
    ok, frame = cap.read()
    if not ok:
        raise RuntimeError('cannot read ' + path)
    frame = cv2.resize(frame, (W, H), interpolation=cv2.INTER_AREA)
    return Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))


rows = []
for number, before, after, title, method in STAGES:
    im = Image.new('RGB', (W * 2 + 8, H + BAR), (14, 14, 16))
    im.paste(grab('Docs/StoryStages/VideoOriginal/%s.mp4' % before, FRAME), (0, BAR))   # 새 무대를 연결하기 전에 찍어 둔 원래 무대
    im.paste(grab('Docs/HybridStages/Video/%s.mp4' % after, FRAME), (W + 8, BAR))
    d = ImageDraw.Draw(im)
    d.text((18, 12), '%d. %s' % (number, title), font=bold, fill=(240, 232, 214))
    d.text((W - 150, 20), '지금 게임', font=small, fill=(170, 170, 175))
    d.text((W + 26, 20), '새 버전 (%s)' % method, font=small, fill=(250, 196, 96) if method == '리메이크' else (150, 200, 250))
    name = '%02d_%s.jpg' % (number, after.split('_', 1)[1])
    im.save(os.path.join(OUT, name), quality=92)
    rows.append(im)
    print(name)

for i, part in enumerate([rows[0:4], rows[4:8], rows[8:11]]):
    sheet = Image.new('RGB', (rows[0].width, sum(r.height for r in part)), (14, 14, 16))
    y = 0
    for r in part:
        sheet.paste(r, (0, y)); y += r.height
    sheet.save(os.path.join(OUT, '00_sheet_%d.jpg' % (i + 1)), quality=90)
    print('sheet', i + 1, sheet.size)
