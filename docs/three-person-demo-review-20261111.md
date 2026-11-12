# Three-person seated demo review — 2026-11-11

The integration retains the existing preview orientation, per-person calibration, same-frame Full X assist, held-block release, effect warmup, scene preparation gate, waiting-facing correction, and camera walking improvements.

The latest recording exposed camera-directed short punches whose image elbow still looks folded, and compact X poses whose estimated wrists briefly fail to cross. The fallback now completes an already established punch onset only with independent elbow opening and a supporting guard. Shared elbow-depth changes while leaning, a recent block, vertical lowering, and held/extended attacks cannot use this fallback.

The X assist uses visible inward fist landmarks only inside the existing bilateral approach envelope. It does not widen ownership or replace the 700 ms confirmed hold, tracking freshness, release latch, or same-frame/same-body Full validation. A broader candidate was rejected after it suppressed a later cross and confused a block-to-raise transition.

Unity now shows whether a detected gesture was applied, blocked by hit stun/game state, or needs special charge. ACKs additionally report fighter facing/positions and movement/stride state. Gameplay timing and rejection rules are unchanged. Recording shutdown now reports full video verification progress and closes the owned game promptly while retaining the complete frame/hash validation.

## Observed live recording

Session `46439e4a1949469597c80e39575325ba`, v23, 15,626 processed frames, about 521 seconds. Three participants were identified by the visible changes of seated person, not biometric identification. The user reports C calibration at each change; the recording also contains five calibration markers/completions in total. There is no online model training during play. Improvement later in the third person's segment cannot be attributed to automatic learning.

- Camera 29.97 fps; capture-to-engine median/p95 23.38/29.91 ms.
- Game approximately 60 fps. No logged frame >50 ms after initial loading; camera footage does not prove every visual hitch was absent.
- 83 recognition events: 60 APPLIED, 21 GAME_BUSY, 2 NOT_CHARGED. These are delivery counts, not recognition accuracy.
- 24 attacks recognized while leaning: 18 APPLIED; six rejected during hit stun. All four punch/kick gestures are represented.
- Four reviewed block-release-to-punch sequences within one second were APPLIED.
- Nine of fifteen detected X events were APPLIED; the others were rejected during game state/charge restrictions.
- Facing, stride and actual fighter trajectory were not present in the old log, and no game-screen video was recorded. The matching build and native motion checks support implementation correctness, not a visual reconstruction of that play.

## Offline before/after

The latest frame-for-frame replay includes the recorded auxiliary Full landmarks and original processing clocks/calibration markers. It reproduces all 83 original events before changes. Final replay emits 86 events: LEFT_PUNCH 30→32, SPECIAL 15→16, and all other action totals unchanged.

- Third participant's visible short left punches at 286.4 and 287.5 seconds: missed → detected.
- Third participant's held X around 348–350 seconds: missed → detected at 349.5 seconds.
- Block-to-left-raise at 301.3 seconds remains a raise. Reviewed held-arm and rest intervals remain quiet.
- At 136.3 seconds an earlier, real X now fires. A later X around 139.8 seconds is consequently blocked by the existing repeat latch because the intervening hands-down pose lost valid wrist tracking. This is a remaining limitation, not a claimed recovery of every old event. Do not rearm a fired X merely on tracking loss.
- The third participant's X attempts around 353 and 358 seconds still lack sufficient reliable geometry in the conservative final candidate. The broader candidate recovered more but damaged other transitions, so it is not deployed.

Legacy primary-only replay covers 25 earlier sessions, 149,448 frames. Three additional visible punches (fb045e frame3352, 06e4c0 frame1595, a2131e frame2653), one additional visible X (4e0830 frame4769), and earlier confirmation of the same X (frame4547→4506) were reviewed against RGB. The initial candidate's false punch during lean-only motion at 27da90 frame5989 is removed. No legacy event is lost beyond that same-hold timing shift. An additional latest-session primary-only replay brings the regression sweep to 26 sessions / 165,074 frames; it is separate from the live-equivalent dual-landmark replay.

These are development recordings used to tune the rules, not held-out participant accuracy. No model weights were retrained. There is no basis yet for claiming uniformly high accuracy across new people.

## Validation and remaining work

Final Python suite: 261 tests passed, including exact-clock recorded regressions, synthetic mirroring, false-positive controls, transport and recording integrity checks. Unity native build: 16 scenes, zero errors, 52 warnings. All 46 preparation/motion checks pass. Native probe verifies all four attack mappings, held block, release after lost state, and facing/movement diagnostic fields. The launcher shutdown-order check passes without opening a camera. Existing walking idle-mesh/hop checks have six unchanged failures before/after; they are not reported as a completely passing walking suite.

Concurrent recorded-RGB inference and native combat benchmark: 1,200 frames at 30.00 fps, Lite+Full inference median/p95 14.68/15.05 ms; game steady interval 59.67 fps, frame p95/max 17.26/28.47 ms. The only >50 ms stalls were loading frames 1 and 2 (2,534/287 ms). Hit and special recovery checks pass. This is a camera-free load check, excluding live capture/preview/recording. Recorded originals were not modified. The new release has not yet been played by a human.

Remaining risk is occlusion and variation in camera-directed punches/compact crosses, not a demonstrated frame-rate bottleneck in this recording. Preserve a single C calibration per participant change and keep both forearms in view. Future model comparisons need participant-separated labeled attempts and negative examples; repeated tuning and scoring on the same person's recordings cannot establish generalization. Do not request more recordings just to inflate this regression score.
