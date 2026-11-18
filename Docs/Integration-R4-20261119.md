# R4 integration review (2026-11-19)

Unity base: R3 b76933b. Team source: feature/unity 78780ab, including 5cb9184, 7401d58, 9c656fa and d1514fc. All remote branches/PR heads were checked on 2026-11-19; feature/pvp-lobby-tutorial did not advance.

Integrated: READY/FIGHT and round locks in story/PvP, story KO lettering, full 100 HP at every stage including resumed saves, map/HUD text, grounded walking, PvP palm cut-in and effective camera pose, underground material/presentation adjustments, authored BootScene opening and tutorial BGM.

Preserved: R3 camera runtime, personal calibration, original 150ms input leases, bounded online presence/recovery, host-authoritative mouse/keyboard selection, separate practice completion feedback, diagnostics export, hidden-by-default operator tools, approved desktop icon, bundle identity/save location and macOS camera privacy checks. Original v36.3 and R3 packages remain separate and unchanged.

Both upstream and R3 previously used view version 33 for different changes. The integrated layout is version 34 and Photon application version redbool-online-view34; both computers must update. Old guests must be rejected rather than freezing on READY/FIGHT.

Camera backend remains the exact R3 native binaries from Python commit 3b07244a3c430280624a1c7a59d573f592929b09. Four supplied journals reproduce 123 events with exact discrete decisions/timestamps (only reported floating-point roundoff across architectures). These unlabelled coordinates cannot prove recall of attempted but unobserved gestures, or identify which bystander the pose model selected.

615: story combat reaches ScoutPass and RiverAmbush; 41 recognized commands, 4 GAME_BUSY (3 hit-stunned, 1 dead). Camera processing 23.2fps, post-loading game roughly 59–60fps. 743: three camera runs with 17/20/45 commands; latest reaches three arenas, camera 19.3fps, heavy arenas roughly 41–55fps. No recorded socket error or exception. Scene loads still contain long frames; not an uninterrupted-FPS guarantee.

Added rendering-only relief after two complete, focused five-second windows above 23ms/frame with advancing camera input. It never changes the pose model, recognition thresholds, event expiry or network pacing. Background/modal/loading intervals break the streak; the budget remains stable until the next camera process.

Validation: RedboolR4IntegrationCheck.Run, four ordered landmark journal replays, Mac/Windows release builds, packaged native synthetic story/lifecycle and two-instance LAN/Photon tests. Consult the package manifest for exact completed evidence; physical webcams and remote Windows gameplay remain field verification.

## Native calibration-wait responsiveness

Native R4 and preserved R3 both reproduced preview slowdown in the uncalibrated PvP waiting room: no calibrated gesture input renewed the existing macOS activity lease, despite advancing local camera/JPEG work. The sampled main thread mostly waited in the AppKit event loop. `RedboolCameraRuntime` now renews the same bounded activity lease only for advancing camera frames or successfully decoded newer previews. No tracking/readiness, deadline, recognition, or transport acceptance changes. Duplicate/stale responses do not renew it; stop/loss still expires the lease. Re-run native preview/lifecycle checks and compare retained failing runs before release.
