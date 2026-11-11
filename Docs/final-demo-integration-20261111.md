# Mac demo integration — 2026-11-11

Latest upstream Unity source: `9a47a0158c008bf848ad90c46b294b7b9aebd145` (`feature/unity`). PR #7 is already merged. This incorporates the team's combat/AI rules, QiPalm special and cut-in, animation and clothing fixes. Python PR #6 is also merged; `feature/vision-v2` at `d7f9547` has the same tree as validated `3c0e485`. No recognition thresholds, training data, or models changed in this integration.

## Compatibility fix

The revised `Fighter.CanAct` can be true during an ordinary attack; the existing Python `ReadyGate` interprets `game_can_act` as permission to start the next attack. The receiver now reports `Fighter.CanAttack` to retain that wire contract. Additive `game_special_active` diagnostics distinguish QiPalm animation/recovery from recognition misses. Combat locks and event expiry remain enforced.

The camera gesture stays chest X → one `SPECIAL`, which starts QiPalm and requests release after its minimum charge time. It consumes one charge, deduplicates retries, and unlocks after the animation/recovery. Separate charge/release camera gestures are not part of this demo. The four existing attack mappings and held-pose block are unchanged. F8 is optional keyboard-only testing; leave it off for camera play.

## Validation

- Latest campaign: 155 checks across 11 battle stages, calibration gates, saved battle Continue, wins/losses, unsaved direct duel and restart.
- Actual engine → UDP → Unity: 23 Unity checks and 7 sender checks. Four attacks, held guard, empty-charge rejection, one charged special despite duplicated event, post-QiPalm straight, time restoration, and 130 busy ACK samples never advertised attack readiness.
- QiPalm native Play-mode verification: 31 checks, zero runtime errors, including damage/chip, interrupt/refund, automatic release, whiff, AI guard response, KO/victory and restored original save.
- Mac standalone build: 16 scenes, 0 errors, 64 warnings (existing API/services and shader warnings). Native app passes the four-attack/held-guard probe and releases guard after 300 ms of lost input.
- Recognition source is byte-identical to the previous validated release (183 Python tests, 21 replayed sessions / 116,186 frames). Those historical results are not new independent accuracy measurements.

The isolated Mac project uses Unity 6000.4.5f1. Editor-generated package/NuGet/material changes are not part of this source patch. Local build and standalone test evidence is retained in `integration_20261111` alongside the final release. Camera recording and simultaneous game/camera performance still need one real-play acceptance run; no camera was opened during integration.

## Reproduce the motion check

Use an isolated project, a free UDP port 5065, and a fresh shared output folder. Run `RedboolMotionPlayCheck.Run` in Unity batch mode with `REDBOOL_MOTION_CHECK_OUTPUT` and `REDBOOL_UDP_CHECK_DIR`. At the same time, run `Tools/check_motion_udp.py <REDBOOL_UDP_CHECK_DIR>` with the paired Python environment and `PYTHONPATH` pointing to Python `src` and `tests`. The batch runner restores the original campaign save.
