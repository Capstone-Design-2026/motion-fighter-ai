# Two-person Hero02 demo review — 2026-11-11

Recording: `32a6e0753001473eb1b1ba40f2d7f69b`, play `20261111_181141_6919647a`.
This is reviewed development data, not an independently labelled recognition-accuracy benchmark.

## Recorded game behavior

- Both participants recalibrated: about 47.50 s and 168.78 s. The person switch is visible in the RGB recording.
- Camera: 13,806 processed frames, about 29.95 fps; capture-to-engine median/p95 23.28/29.41 ms.
- Game: post-loading performance intervals at least 59.54 fps; sampled median frame 16.67 ms, maximum 39.45 ms after 50 s. Initial loading has longer stalls. These metrics describe the captured v24 build.
- 101 recognized events: 83 APPLIED, 18 GAME_BUSY (15 hit-stun, 2 attacking, 1 dead/input-locked). APPLIED is receipt of a game action, not proof of intended gesture accuracy or damage.
- 16 held-block release intervals clear in the game within 2.3–59.2 ms after the recognizer releases block.
- All six recognized X/SPECIAL events were APPLIED. Game attack availability returned after 1.43–1.70 s. Reviewed raise/punch follow-ups execute; the late right punch at 443.36 s was rejected during hit-stun, not held by the special latch.
- 5,582 preparation-facing samples point at the opponent. Hero02 remains in the integrated game.

## Recognition correction

The second participant's real right punch at 212.05 s was missed after a high block. Tracking recovery put the hand in PREPARING; small settling wrist motion repeatedly reset the guard dwell. Permit speed up to 2.0 (formerly 1.2) only while PREPARING, within 1.2 s of a recognized block, with no unreturned punch, compact calibrated hand position and tightly folded 2D/3D elbow. The original 150 ms dwell remains. RETURNING/EXTENDED and ordinary recovery retain their thresholds.

Recorded dual-pose replay changes 101 to 102 events: exactly the right punch at 212.05 s is added; no original events are removed. This proves recognition recovery offline, not a new live game application. Engine metadata is updated to v25.

## X remains a known limitation

The second participant attempted additional X poses that the pose models represented as an ordinary guard. Around 268.76, 271.73 and 274.26 s, actual crossed arms could be emitted as RIGHT_PUNCH. Successful special count is not total attempt count. The second participant has 20 left punches, 25 right punches, 13 left raises, 12 right raises and five recognized specials in the original trace, including these misclassifications; these counts are not recall or accuracy.

A fresh Full IMAGE detector was compared with Full VIDEO on this and two prior recordings. It adds a genuine X at 202.29 s and removes the wrong punch at 268.76 s here, but loses previous valid X detections (including 420.61 s in the earlier two-person recording). It is not deployed. Broader return-speed and cross-candidate reset variants also created false punches and are rejected. Lite VIDEO plus Full VIDEO X assistance remains the default; no weights are retrained and camera resolution/fps is unchanged.

Remaining recognition work should use the already recorded crossed-arm positives and confusable guard/punch negatives, with participant-separated evaluation. A hand/forearm-aware or image-based crossing cue must prove both missed-X recovery and no added punches/false specials before deployment. Repeated filming by the same primary participant does not establish generalization.

## Upstream game content

Integrate feature/unity through f032b0b (b91676f + f032b0b): Hero02 special cut-in, dialogue portraits, 38 replaced prologue clips, 25 map location images, map route/current-location presentation, and improved clue journal. These 124 files do not overlap the validated receiver, held-block, walking, preparation or recognition changes. Existing local rendering settings/plugins are preserved.

Validation outcomes and release installation are recorded in the adjacent review artifacts and release manifest. No raw recording is modified; no camera is started during this review. Full campaign/manual animation aesthetics and recognition on unseen participants remain outside the automated checks.

## Final replay checks

- 27 recordings / 178,880 frames replayed through the primary pipeline. All 26 prior recordings preserve their event labels/counts within the 15-frame matching window; no added/lost historical events and no expired/uncalibrated events. The latest same-frame dual-pose replay separately confirms exactly one recovered punch.
- Build: 16 scenes, zero errors, 52 warnings. Eight additional logged warnings are deprecated Unity APIs in existing combat/receiver code; remaining warnings include existing environment shaders and unused TestScene scripts. They do not establish a warning-free build.

- All 270 Python tests pass across the full run and targeted rerun: 262 passed initially; 8 local UDP tests initially hit environment permission errors and passed with local network access. No assertion failures remain.
- Unity: 60 motion/preparation checks pass, 12 Hero02 combat-scene structural checks pass, actual JAB/STRAIGHT/HIGH_KICK/LOW_KICK contact damage confirmed. Map/journal screenshot checks pass and sampled layouts inspected.
- With native game plus saved RGB inference, Lite+Full processing holds 30.00 fps (median/p95 14.73/20.90 ms), game steady interval 59.67 fps (frame p95 17.45 ms). This excludes live camera capture/preview/recording; initial loading is excluded from steady-state conclusions. New cut-in specials complete and recover.
- Native UDP probe passes all four attacks, held guard and immediate release; stale state releases guard, diagnostics/facing remain compatible.
