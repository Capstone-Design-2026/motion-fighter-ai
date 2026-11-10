# Final Mac demo candidate — 2026-11-10

## Scope and selected implementation

Freeze MediaPipe Lite, 30fps capture/recognition, 15fps preview and the seated v20 recognition engine. Keep held-pose block release, independent attack return, left raise → HIGH_KICK, right raise → LOW_KICK, left punch → JAB, right punch → STRAIGHT, body lean movement and charged chest-X SPECIAL. No new camera session was captured and no model weights were retrained in this finalization.

Earlier actual Logistic/MLP/ExtraTrees training and Lite/Full comparison remain in the repository's v7 training report. Those trained models did not consistently improve continuous event behavior. Existing recognizer outputs or prompts were not relabelled as ground truth to inflate accuracy.

## Completed offline verification

- Inventory: 34 recording manifests with frame files (11 legacy collection sessions, 21 live demo sessions, two other vision sessions).
- Replayed all 21 live demo sessions, 116,186 recorded frames. Twenty sessions calibrated; the one without calibration emitted no attacks. No before-calibration event, duplicate event ID or expired drained event was observed. This is an invariant check, not exhaustive human-labelled accuracy.
- Compared v19 and v20 across the same 21 recordings and original processing clocks. Twenty recordings have identical attack frame/type results. Only the latest recording changes: three video-confirmed low right punches recovered, one existing punch about one frame earlier.
- Four previously video-reviewed event datasets: 12/12, 12/12, 11/11 and 16/16 window matches, no unmatched events. These are development data, partly used in earlier training/tuning, not new blind accuracy measurements.
- Full Python suite: 183 checks passed, including actual/mirrored stored recordings, punches, raises, held block/release, X follow-up, lean/neutral, stale/expired input, Windows UDP reset, recording and framing diagnostics.

## Changes

The camera preview now identifies off-frame or uncertain elbows/wrists and warns near the lower image edge. The trace and summary retain framing diagnostics for later review. This display-only addition does not alter recognition thresholds, calibration, safety gates or model assets. Five new framing checks and the full live recorder/publisher tests passed.

Unity's Continue button previously loaded the map even with a saved pending battle. That left pendingNodeId set and prevented map travel. Continue now restores the configured battle scene when a battle is pending; ordinary map saves still open the map. The resumed battle still waits for a fresh calibrated camera state.

## Unity verification

- Eleven configured story battle stages: correct scene routing, one receiver, preparation holds enemy/HP/timer, preserved calibration acceptance, real enemy-death handler, victory destination, cleared pending battle, saved completion and normal time scale after KO.
- Boss routes to ending and clears the completed save. Player loss routes to start and clears the defeated run. Direct duel restart installs one waiting receiver and preserves campaign saves.
- 152 campaign checks passed before the menu-only fix; eight targeted checks then passed on the final Continue code, including the actual button callback for battle and map saves. Synthetic damage bypasses skillful combat and cutscenes are marked seen in the test, so these checks do not establish human balance or every cinematic's quality.
- Mac build: 16 scenes, zero errors. 54 warnings remain (deprecated Unity APIs and optional project-service configuration among them); the full build message log is retained. This is not a warning-free build.
- Actual standalone app: four attack mappings applied, held block stayed held and released, 300ms input interruption released held guard, and diagnostics were present. No real camera was started for these checks.

## Final user acceptance and remaining limits

The final local package freezes source/model/game hashes, checks its files before launch, starts an unsaved direct duel and the camera together, and saves game and camera records together. Launcher tests cover camera-free check mode, occupied receiver prevention, failed startup summary and unique first-ACK rejection counting. It uses this Mac's existing Python virtual environment and is not a portable installer for another PC.

Only one final natural gameplay session is planned: fit low punches in frame, calibrate once, use the attacks/block/lean, trigger one charged X and a follow-up attack, restart once without restarting the camera, then rest for ten seconds and quit with Q. Final screen feel, framing and simultaneous live performance remain pending until that session. Automatic replay does not replace this acceptance.

Very low/short punches remain unreliable when the wrist/elbow leaves the image, including a one-frame loss. Keep the full low punch in view instead of accepting guessed off-screen joints. No guarantee is made for all users/cameras/lighting. Other presentation hardware needs its own installation check. Full cinematic/audio review, all human-played campaign branches, shops and broader polish are outside the local motion-demo acceptance; shop functionality remains a placeholder in the source and must not be advertised as complete.

GitHub PRs remain review branches; neither PR is merged by this workflow. Raw recordings and local Python environments are not uploaded.
