# 산길 척후병 360도 무대 · 컷씬 음성 정리

2026-10-17 / 적용 프로젝트: C:\Unity\VisionStriker

> 이후 자동 전투 카메라를 추가했다. 이동 중심에 따른 ±14도 회전, 근접 거리 5.4m, 수동 보기 후 2.5초 자동 복귀는 [CombatCamera.md](CombatCamera.md)를 참고한다.

## 적용 내용

- ScoutPass 전용으로 기존 평면 산 배경을 제거하고 180m × 180m 지형과 3겹의 폐곡선 산 능선을 만들었다.
- 사용자 제작 소품 4종(낮은 바위, 절벽 바위, 소나무, 목책)을 전후좌우에 배치했다. 기존 북쪽 호송 검문소는 유지했다.
- 낮은 풀과 자갈을 보충하고 소품 바닥 위치를 지형에 맞췄다. 카메라 회전 공간을 중앙에 확보했다.
- 구름을 별도 투명 이미지로 분리해 전 방향 하늘에 두 겹으로 흐르게 했다. 3D 산 자체는 움직이지 않는다.
- 두 캐릭터를 중심으로 추적·거리 조절하는 카메라를 적용했다. 캐릭터를 잇는 XZ 방향과 화면 비율로 프레임을 계산한다.
- 우클릭 드래그로 360도 회전, 우클릭 중 휠로 거리 조절, Home으로 전투 시점 복귀. UI 위에서 시작하는 드래그는 무시한다.
- KO/필살기 중 시점 조작을 잠그고, 기존 카메라 확대 연출이 끝난 뒤 추적을 재개한다. 확대 시 회전 방향에 맞춰 화면 평면으로 초점을 이동한다.
- 카메라 전용 소품 충돌체를 추가했다. 전투의 2D 물리 판정과 분리되어 있다.
- 각 소품의 LOD 3단계는 유지했다. 실기기별 목표 프레임 성능 측정은 별도다.
- 모든 컷씬 38개의 음성 103개 연결, 입 모양 진폭, 영상 입 추적 연결을 제거했다. Assets/StoryAudio 폴더의 WAV 103개와 추적 JSON 17개 및 메타를 삭제했다(44,314,913바이트).
- 자막, 캐릭터, 영상, 분기, Space 넘기기 데이터는 변경 전과 비교해 유지됨을 확인했다. 앞으로 새 녹음을 넣을 수 있는 재생 코드는 남겨뒀다.

## 범위

- 360도 무대는 산길 척후병의 ScoutPass에 적용했다. 다른 전투 씬은 기존 무대를 사용한다.
- 캐릭터 횡이동/3차원 타격 판정으로의 전환은 이번 변경에 포함되지 않는다. 현재 이동과 타격은 기존 2D 전투 평면을 사용한다.
- 기본 전투 중에는 자동으로 계속 회전하지 않는다. 영상의 자동 회전은 검토용이다.
- 카메라가 뒤편으로 돌아가면 키보드와 비전의 좌우 이동을 화면 기준으로 보정한다. 정확한 정면/후면 각도에서는 기존 부호를 유지해 방향이 떨리지 않게 한다.
- GameScene 씬 파일은 이번 변경 전 해시와 일치한다. 공용 카메라 스크립트는 orbitArena를 켠 ScoutPass에서 360 기능을 사용한다.
- Git 커밋/푸시는 하지 않았다.

## 확인 결과

- 기능 검사 55개 PASS, 실패 0개, 게임 실행 예외 0개(연결되지 않은 기존 MCP 플러그인 로그는 별도).
- 8방향 × 화면 비율 3종 × 전투장 위치 3곳 = 72회 프레임/소품 겹침 검사.
- 산 지형 유효성, 하늘 셰이더, 구름 속도와 깃발 움직임 검사.
- 실제 스트레이트 명중, 양쪽 벽 충돌, 스토리 전투 진입, 승리 후 지도 복귀, 패배 후 시작 화면 복귀 검사.
- 음성 없는 자막 표시 및 다음 대사 진행 검사. 기존 사용자 저장 데이터는 검증 후 복원했다.

## 미리보기

- index.html: 실제 Unity 영상과 8방향 화면.
- release/ScoutPass_360.mp4: 24초, 1280 × 720, 24fps.
- release/arena_overview.png: 전체 지형 구조.
- functional/checks.txt: 기능 검사 결과.
- preservation_audit.json: 대사·영상 데이터 보존 비교.

## 수정/추가 파일

- Assets/Scenes/ScoutPass.unity
- Assets/Scripts/Combat/Battle3DCamera.cs
- Assets/Scripts/Combat/BattleCameraObstacle.cs
- Assets/Scripts/Combat/HitImpact.cs
- Assets/Scripts/Player/PlayerController.cs (카메라 방향에 따른 좌우 이동 변환)
- Assets/Editor/ScoutArena360Builder.cs
- Assets/Editor/ScoutPassBuilder.cs (BuildAndPreview를 360도 빌드까지 연결)
- Assets/Editor/ScoutPassVerification.cs
- Assets/Editor/StoryVoiceVerification.cs (음성 제거 상태 검사 지원)
- Assets/Environments/ScoutPass/Shaders/ScoutSky.shader, ScoutTerrain.shader
- Assets/Environments/ScoutPass/Textures/CloudLayer.png
- Assets/Environments/ScoutPass/Meshes/Arena360Ground.asset, Arena360Grass.asset, MountainRing0~2.asset
- Assets/Environments/ScoutPass/Materials/CloudSky360.mat, MountainRing0~2.mat
- Assets/Resources/Story/Cutscenes/*.asset (음성 관련 필드만 제거)

Unity에서 Ctrl+R로 새로고침 후 ScoutPass를 열면 된다.
