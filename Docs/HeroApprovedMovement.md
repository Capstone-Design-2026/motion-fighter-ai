# Hero02 approved idle and movement

> **2026-11-12 변경:** 주인공 이동을 대시 없이 **승인 걷기(스텝, `HERO_FORWARD_STEP`)** 로 통일했다. 승인 기본자세(IDLE)는 그대로 쓴다.
> 앞으로는 스텝을 반복하고, 뒤로는 같은 클립을 거꾸로 재생한다(`HERO_BACKWARD_STEP`, 컨트롤러에 추가한 속도 -1 상태). 입력을 떼면 바로 멈춘다.
> 빠르기는 `RunConfig`의 **Hero Walk Speed** 배율로 정한다(기본 2.61배, 초당 약 1.0m). 1배는 클립 그대로(한 스텝 0.667초에 약 0.26m, 초당 약 0.38m)다. 재생과 이동을 같은 비율로 올려 보폭이 그대로라 발이 미끄러지지 않는다.
> 방식은 `RunConfig`의 **Hero Movement** 로 바꾼다: `ApprovedWalk`(기본) / `DashAndStep`(아래 설명의 대시 → 이어 달리기 → 스텝, 뒤로는 백대시) / `CommonWalk`(적과 같은 걷기).

Hero02 uses the approved Jin-inspired guard idle and X-axis authored movement in
GameScene, ScoutPass, and the ten StoryStages scenes. The shared enemy controller
and existing scene objects are unchanged.

| Input | Animation |
| --- | --- |
| Neutral | Approved guard idle with light knee motion |
| One forward recognition | Forward dash, then idle |
| Repeated or held forward | Forward dash → continuation → repeating footstep |
| Backward, including held input | Backward dash only |

The dash lasts 0.6 seconds; the approved faster forward step lasts 20/30 seconds.
Recognition edges and sustained input determine continuation; repeated UDP packets
alone do not count as separate gestures. Travel is applied through Rigidbody2D
along the combat X axis, scaled to the visual. Attack, guard, hit reaction, round
lock, preparation lock and special moves interrupt movement. Existing guard and
special input handling from feature/unity is retained.

## Assets and integration

- `Assets/Characters/Hero02/ApprovedIdle/Hero_Idle_Base_Final.fbx` supplies the
  approved mesh, avatar and idle. Hero02_Visual retains its existing prefab GUID.
- `ApprovedMovement/Hero_ApprovedMovement.controller` preserves all current combat
  and special states, changes idle, and adds four `HERO_*` movement states.
- `HeroAuthoredMovement` and `HeroMovementSequence` control displacement and phases.
  PlayerController forwards movement input, and Fighter3DView plays the authored
  clips without the procedural walking override.
- Forward continuation and step FBXs include the skinned mesh to preserve the
  neutral bind skeleton. The corrected standalone `.anim` files retain their GUIDs.
  Do not replace them with the earlier skeleton-only exports: those bent the legs
  backward and lifted the feet during continuous forward movement.
- The live movement `.anim` assets include corrected extra bone paths. Reimporting
  an FBX does not automatically regenerate those standalone runtime clips.

## Validation for this integration

- Compiled all 133 current runtime C# sources against Unity 6000.4.5f1 references:
  no errors; existing obsolete-API warnings remain.
- Executed 24 movement cases at 30/60/120 Hz with both facing directions, including
  single recognition, repeated recognition, held forward and held backward. Also
  checked six cancellation cases and the approved step duration.
- Verified all new asset GUID references resolve and have no duplicates. All
  existing combat/special controller states are retained unchanged.
- Compared all twelve scenes object-by-object: only the Player component list and
  its new HeroAuthoredMovement component change.
- All nine submitted FBX/animation files match the already validated live Unity
  assets byte-for-byte. The earlier actual Play-mode check covered single dash,
  continuous forward, backward repeats, lock and attack interruption. Its grounding
  check sampled 148 rendered poses, with sole height -0.000362 to +0.003652 model
  units. That Play-mode run preceded integration with the latest remote scripts;
  the merged source was checked by compilation and movement-sequence tests above.

Suggested scene smoke check after pulling: single forward, forward held for at
least three seconds, held backward, then interrupt travel with attack/guard/special
and with the vision-preparation/round locks.
