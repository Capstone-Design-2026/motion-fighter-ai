# Hero02 / hero-face 주인공 적용

`Hero02_Visual.prefab`은 hero-face-refined-v1 디자인의 게임용 외형입니다.
원본 Blender 파일은 변경하지 않았습니다.

- Humanoid: 몸통과 팔다리 17개 뼈. Unity Avatar 생성 및 유효성 검사 완료.
- 모델: LOD0 60,000 triangles, 2K 텍스처, URP/Lit.
- 텍스처: BaseColor(sRGB), Normal(Normal Map), MetallicSmoothness(linear, 알파=smoothness).
- 애니메이션: 기존 Unified_Combat.controller를 그대로 사용합니다.
- 적용: GameScene, ScoutPass, StoryStages의 전투 씬 10개.
- PlayerController / UDP 수신 / Fighter / Health / 카메라 / 충돌 루트는 유지하고 Fighter3DView의 visualRoot와 modelAnimator만 연결했습니다.
- TestScene은 기존 2D 테스트 씬이므로 변경하지 않았습니다.

Unity에서 Assets/Scenes/GameScene.unity를 열고 Play하면 새 주인공이 나옵니다.
기존 인식 백엔드의 UDP 포트는 5055입니다. 인식 결과는 기존 PlayerController로 전달됩니다.
방어 높이는 기존 규칙대로 상대의 공격 예고에 따라 자동 선택됩니다.

검증 도구: HeroFaceInstaller.Apply, HeroFaceVerification.Run (batchmode용).
검증 보고서와 게임 화면: 프로젝트 Logs/HeroFaceIntegration.txt 및 Logs/HeroFace/.
검증 패킷은 로컬 UDP로 전송합니다. 실제 사람의 웹캠 인식 정확도 검증과는 별개입니다.

한계: 손가락/표정 전용 뼈와 옷감 시뮬레이션은 없습니다. 기존 전신 애니메이션은 리타게팅하지만 손가락 움켜쥠이나 옷자락의 정밀 변형은 별도 보완 대상입니다.

