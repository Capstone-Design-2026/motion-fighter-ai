# V2 첫 구현 결과 — 2026-10-08

이 변경은 V1 `be56617`에서 분리한 Work 임시 복제본의 `vision-v2` 브랜치에서 처음 작성되었다. 당시에는 원본 저장소를 변경하지 않았고 임시 origin도 원본 로컬 저장소를 가리켜 push하지 않았다. Recovery 후 실제 개발 저장소는 `/Users/bin/Documents/ChatGPT/redbool/motion-fighter-ai`, 브랜치는 `feature/vision-v2`이며 현재 origin은 GitHub의 `Capstone-Design-2026/motion-fighter-ai` 저장소다.

## 이번 변경

- calibration factory에 보정 시간·최소 표본 수를 전달하여 생성 오류 수정.
- 카메라를 열기 전에 detector를 생성.
- CSV schema 2: frame ID, monotonic capture-return 시각, 단계, pose/score 유효성, baseline·보정 score·MAD 기록.
- 준비/정착/기록 단계의 프레임 및 포즈 소실도 저장. 유효하지 않은 score는 빈 셀로 저장.
- 센서 미인식 비율의 분모에 포즈 소실 프레임 포함. 유효한 어깨 score만 단계별 분포에 사용.
- 타이머를 monotonic으로 변경.

CSV 분석 시 단계별 자세 분포에는 `stage == RECORDING` 및 `score_valid == 1`을 필터링한다. 타임라인 분석에는 전체 행을 사용한다. R로 단계를 재시작하면 기존 동작처럼 해당 phase의 이전 행을 제거한다. report의 센서 비율은 재시도까지 포함한 전체 관측 기준이다. capture-return 시각은 실제 카메라 노출 시각이 아니다.

## 검증

기존 53개와 신규 3개를 합쳐 56개 테스트 통과. 신규 검증은 실제 factory 생성, 포즈 소실 CSV 보존, 소실 중 baseline 유지/보정 score 결측을 확인한다. diff 공백 검사 통과. V1 모델 자산 3개는 복사 후 SHA256 일치 확인.

실제 카메라 실행과 Unity 연동은 아직 검증하지 않았다. 이번 변경은 전체 V2 수집기 완성이 아니다. 다음 구현에는 원본 관절/선택적 영상·세션 manifest, 중단 시 부분 로그 보존, replay, 수집 안내 문자 깨짐 개선이 남아 있다. calibration report의 추천값은 여전히 raw score 기반이므로 새 보정 threshold로 바로 적용하지 않는다.
