# 후반 스테이지 캐릭터 적용 · 2026-10-27

프로젝트: `C:\Unity\VisionStriker`

| 번호 | 캐릭터 | 지도 노드 | 전투 씬 | 모델 삼각형 수 |
| --- | --- | --- | --- | ---: |
| 07 | 능선 정예병 | east | EasternRidge | 18,581 |
| 08 | 수색대 추적자 | camp | SearchCamp | 10,710 |
| 09 | 포로 감시관 | rescue | PrisonStockade | 9,840 |
| 10 | 흑견 | entrance | BlackCloudEntrance | 10,350 |
| 11 | 무진 | final | SluiceFortress | 10,597 |

## 모델과 리깅

- Blender에 전달된 다섯 원본을 별도 백업하고 FBX/텍스처로 내보냈다.
- 07은 뼈대가 없는 메시였다. 10의 65개 뼈대를 체형에 맞추고 표면 대응으로 스킨을 이식했다. 손가락은 별도 자동 가중치와 혼합하고, 마스크와 얼굴 윗부분은 머리를 따라가도록 보완했다. 옷에 손의 움직임이 새는 연결을 정리했다.
- 08–11은 기존 Tripo 리깅을 유지했다. 모델당 정점 영향 뼈대는 최대 4개이며 미할당 정점은 없다.
- 손가락까지 Unity Humanoid로 매핑했다. 원본 컬러 텍스처를 URP Lit에 연결하고 07의 노멀맵도 사용한다. 텍스처 원본은 보존하며 런타임 최대 크기는 2048이다.
- 캐릭터별 시각적 신장은 1.78 / 1.85 / 1.94 / 1.89 / 2.00m이다. 기존 전투 판정과 능력치는 유지했다.

## 애니메이션

`Assets/Characters/StoryOpponents/<캐릭터>/`에 FBX, 재질, Visual 프리팹, OverrideController와 보정 클립을 둔다.

`Unified_Combat.controller`를 공통 사용한다. 기존 척후병의 **현재 승인된** Override 클립을 기준으로 하므로 최근 기본자세의 작은 바운스와 짧은 전후 스텝을 포함한다.

동작: IDLE, JAB, CROSS, HIGHKICK, LOWKICK, GUARD, GUARD_LOWKICK, HURT, WALK_FORWARD, WALK_BACK, KO.

공격/방어/피격의 근육 곡선과 타이밍은 같은 소스를 사용하고 `RootT.y`만 체형별 바닥 접촉에 맞춰 조정한다. 기존 척후병의 공중 구간과 KO 상승 구간은 유지한다. 방어 hold/hit/release 상태도 동일하다. `GroundedBoxingWalk`의 실제 이동량 기반 접지 보정이 적용된다.

## 스테이지 연결

- `RunConfig.asset`의 위 5개 `nodeEnemyVisuals`만 교체했다. 앞의 6개 노드와 플레이어는 기존 모델을 유지한다. (2026-11-04: 02–06도 전용 캐릭터로 교체. [StoryCharacters02-06](StoryCharacters02-06.md) 참고)
- 위 5개 전투 씬의 Enemy 시각 모델도 변경했다. 개별 씬을 열어 확인할 수 있다.
- `StoryStagesBuilder`가 개별 적 프리팹을 척후병으로 덮어쓰지 않도록 보완했다. 다시 만들 때 `StoryCharacterInstaller.ApplyConfiguredVisual`로 해당 모델을 씬에 배치한다.
- 맵 입장 시 기존 `BattleSceneBridge`가 설정된 Visual 프리팹을 사용한다. 전투 판정·AI·스테이지 능력치는 원래 컴포넌트를 사용한다.

## 검증과 미리보기

`StoryCharacterVerification`으로 실제 Play 모드에서 5개 맵 입장, 모델·재질·Humanoid 확인, 동작과 기본자세 연결, 가드 반응, 피격, 실제 이동의 발 접촉, 공격 피해, 가드 피해 감소, AI 접근, KO 및 승리 처리를 확인한다. 사용자의 진행 저장은 검사 전후 복원한다.

검증은 키보드/웹캠 입력 대신 제어된 전투 명령으로 진행한다. 웹캠 실사용 검사는 포함하지 않는다. 미리보기는 실제 Unity 씬 촬영이며 컷씬 음성이나 모델의 입 애니메이션을 추가한 작업은 아니다.

최종 검증 기록과 영상: `C:\Users\sbpar\Documents\VisionStriker_Characters07-11_Apply_20261027\index.html`

원본: 같은 폴더의 `Imported_Originals.blend`. 리깅 보완/내보내기용: `Prepared_Characters.blend`.
