# 주인공 잽 — 승준 v8 게임 적용

> **2026-11-13 21:23 이후**: 주인공 잽은 승준이 직접 넣은 v10(`Assets/Characters/Hero02/AuthoredPunches/Hero_JAB.anim`, 커밋 `9637a7e8`)으로 바뀌었고, 적의 잽도 그 동작을 옮긴 것으로 바뀌었다(`Docs/EnemyHeroMotion.md`). 아래 내용과 수치는 그 전의 v8 기준 기록이다. `HeroJabBaker.Bake`는 `-heroJabV8`를 붙여야만 돌아간다(그냥 돌리면 v8로 되돌아가기 때문).

2026-11-13 적용. 주인공(Hero02)의 잽을 승준이 만든 Hero02 잽 v8로 바꿨다.
동작 자체의 설명(타이밍, 발 디딤, 팔 펴짐)은 `Docs/HeroJabV8.md`에 있다. 이 문서는 그 동작을 게임에 넣은 방법과 확인한 내용이다.

적은 계속 공용 잽(`Unified_Common_JAB`)을 쓴다. 바뀐 것은 `Hero_ApprovedMovement.controller`의 `JAB` 상태가 재생하는 클립 하나뿐이고, 판정 시간·피해·후딜 같은 전투 코드는 그대로다.

## 무엇이 달라졌나

같은 전투 씬(SealedGate)에서 같은 검사로 바꾸기 전과 후를 쟀다.

| 항목 | 이전 (공용 잽) | 지금 (승준 v8) |
| --- | --- | --- |
| 클립 길이 | 0.60초 | 0.45초 |
| 맞는 시각 (60fps, 여유 있는 거리) | 0.167초 | 0.133초 |
| 맞는 시각 (30fps) | 0.200초 | 0.133초 |
| 왼팔이 가장 펴졌을 때 팔꿈치 각도 | 153.5도 | 172.2도 |
| 잽이 닿는 가장 먼 거리 (몸 중심 사이) | 1.080m | 0.965m |
| 잽 전체 시간 (다시 움직일 수 있을 때까지) | 0.60초 | 0.60초 (그대로) |

- **더 빨리 맞는다.** 주먹이 0.133초에 다 뻗는다. 잽 판정 구간(0.12~0.215초, `CombatContactWorld.Window`)은 그대로 두었고 그 안에 들어온다.
- **닿는 거리가 약 11.5cm 짧아졌다.** 이전 잽은 앞발을 크게 내디뎠고(약 0.21m), v8은 제자리에서 앞발을 들어 짧게 디딘다(약 0.11m). 아래 "상대별 닿는 거리" 참고.
- **잽 전체 시간은 그대로다.** 클립은 0.45초에 끝나고, 남은 0.15초(막히거나 헛치면 더 길다)는 마지막 프레임인 기본자세로 서 있는다.

## 상대별 닿는 거리

적은 자기 잽이 닿는 거리(최대 1.0m)까지 다가와 공격을 시작한다(`EnemyAI.StrikeRange`). 이전 잽은 어느 상대에게나 1.0m 넘게 닿아서 주인공이 항상 더 멀리서 칠 수 있었다. v8은 상대와 거의 같은 거리에서 닿는다.

| 상대 | 주인공 잽이 닿는 거리 | 상대가 공격을 시작하는 거리 | 차이 |
| --- | --- | --- | --- |
| 02_ValleyAmbusher | 0.940m | 0.860m | +0.080 |
| 03_HiredBandit | 0.980m | 0.950m | +0.030 |
| 04_FerryRaider | 0.930m | 0.810m | +0.120 |
| 05_Cheolmun | 0.955m | 0.970m | −0.015 |
| 06_QuarryElite | 0.945m | 0.905m | +0.040 |
| 07_RidgeElite | 0.925m | 0.835m | +0.090 |
| 08_Tracker | 0.965m | 0.890m | +0.075 |
| 09_Warden | 0.975m | 0.960m | +0.015 |
| 10_Heukgyeon | 0.940m | 0.905m | +0.035 |
| 11_Mujin | 0.980m | 0.975m | +0.005 |

몸 중심 사이 거리이고, 상대가 서 있는 자세일 때 값이다(24fps 기준으로 쟀다. 프레임이 높으면 조금 더 닿는다). 철문(05)만 주인공보다 1.5cm 멀리서 칠 수 있고, 옥졸장(09)과 무진(11)은 거의 같다. 상대가 방어를 올리고 있으면 주인공 잽이 닿는 거리는 0.88~0.97m다.

`StoryCharacterVerification`은 이제 스테이지마다 이 두 거리를 재서 기록하고, 주인공 잽이 상대보다 5cm 넘게 짧아지면 실패로 처리한다. 5cm는 이번에 정한 기준이다. 또 "상대가 막으면 주인공 잽이 0 피해"를 확인하는 거리를 고정 0.9m에서 "실제로 닿는 거리 − 4cm(최대 0.9m)"로 바꿨다. 0.9m에서는 v8이 몸집이 작은 두 상대(04, 07)의 올린 방어에 닿지 않기 때문이다.

## 넣은 방법

방어 동작과 같은 경로다(`Docs/HeroHighGuard.md`).

1. `ArtSource/Hero02/Jab/export_jab_motion.py`가 Blender 원본의 `Hero_Jab_Left_v8_Extended` 28프레임(60fps)을 `Hero_JAB.motion.json`으로 내보낸다. 뼈마다 승인 기본자세의 두 번째 프레임에서 얼마나 돌았는지를 적는다.
2. `HeroJabBaker.Bake`가 그 자세를 주인공 뼈대에 얹어 Humanoid 클립 `Hero_JAB.anim`으로 굽고 `JAB` 상태에 연결한다. 손가락·삼각근 등 Humanoid 밖의 뼈 11개는 Transform 커브로 들어간다.
3. 클립 이름은 `_JAB`로 끝나야 한다. `CombatReach`가 이 이름으로 잽 클립을 찾는다.

속도는 바꾸지 않았다(원본 그대로 0.45초).

### 가져오지 않은 것

v8 원본에는 왼팔뚝 비틀림을 잡아 주는 교정용 뼈 `ForearmTwistBase.L`과 그에 맞춘 스키닝이 있다. 게임의 주인공 모델에는 이 뼈가 없어서 동작만 가져왔다. 구운 결과를 네 방향에서 그려 봤을 때 뻗은 팔에 눈에 띄는 찌그러짐은 없었다. 이 교정을 게임 모델에 넣으려면 모델을 바꾸고, 이 뼈에 키가 없는 다른 모든 주인공 동작에서도 뼈가 팔뚝을 따라가게 해야 한다.

## 파일

- `Assets/Characters/Hero02/ApprovedJab/Hero_JAB.anim` — Humanoid 클립 (60fps, 0.45초).
- `Assets/Characters/Hero02/ApprovedJab/Hero_JAB.motion.json` — Blender에서 내보낸 동작(뼈 32개, 28프레임).
- `ArtSource/Hero02/Jab/export_jab_motion.py` — 위 JSON을 만드는 스크립트.
- `Assets/Editor/HeroJabBaker.cs` — 굽기. 공통 부분은 `HeroGuardBaker.BakeSet`을 쓴다.
- `Assets/Editor/HeroJabPlayCheck.cs` — 전투 씬을 재생해 잽을 확인하고 화면을 저장한다.

## 다시 굽기

동작이 바뀌면 Unity 에디터를 닫고 아래를 순서대로 실행한다.

```
blender -b ArtSource/Hero02/Jab/Hero_Jab_v8.blend --python ArtSource/Hero02/Jab/export_jab_motion.py
Unity -batchmode -projectPath <프로젝트> -executeMethod HeroJabBaker.Bake -heroMotionReport <폴더>
Unity -batchmode -projectPath <프로젝트> -executeMethod HeroJabPlayCheck.Run -heroMotionReport <폴더> -redbool-keyboard
```

새 버전의 동작 이름이나 프레임 범위가 다르면 내보내기 스크립트의 확인 줄(동작 이름, 1~28프레임, 60fps)과 `HeroJabBaker`의 `ContactTime`·`Length`를 함께 고친다.

## 2026-11-13 검증 결과

- 굽기: Blender 자세와 Unity에 얹은 자세의 관절 위치 차이 최대 0.03%(키 대비), 구운 클립을 재생한 결과와의 차이 최대 0.09%.
- 전투 씬(SealedGate) 재생 확인 20/20 통과.
  - 여유 있는 거리(0.865m)에서 60fps·30fps 모두 판정 구간 안에서 맞는다.
  - 닿는 한계 바로 안(0.955m)에서는 맞고, 한계 밖(1.065m)에서는 헛친다.
  - 상대가 막고 있으면 피해가 없고 막기 반응이 나온다.
  - 맞힌 뒤·헛친 뒤·막힌 뒤 모두 기본자세로 돌아와 다시 조작할 수 있고, 주인공 위치는 움직이지 않는다.
- 회귀: 방어 재생 확인 39개, 캠페인 재생 검사 155개, 파운장 검사 33개, 스테이지별 캐릭터 검사 562개 통과(실행 오류 0).
  스테이지별 검사는 위의 거리 측정 20개가 더해져 542개에서 562개가 됐다.
- 사람이 에디터에서 직접 쳐 본 것은 아니다. 카메라 인식으로 칠 때의 느낌(짧아진 거리, 빨라진 타격)은 따로 확인이 필요하다.
