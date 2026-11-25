# Mirrored camera preview — 2026-11-11

The seated demo now defaults to a mirror-style preview so physical left/right movement matches the screen. `--no-mirror` explicitly selects the original camera view. Inference uses the original BGR/RGB frame, anatomical landmark IDs and gameplay mappings are unchanged, and encoded recordings remain unmirrored. The copied preview is reflected before drawing joints/text, so landmark numbers remain readable.

Validation: three preview/default-option tests, the full synthetic live recording/shutdown test with mirror enabled, and existing viewer quality tests. The recording test asserts mirror_display=true, source_mirrored=false, video.mirror=false and lossless video. No real camera was opened during these checks.

Latest v21 actual gameplay (20261111_152548): ScoutPass confirmed throughout. Four block-to-punch follow-ups applied; 16 raise events (12 applied, four busy due to combat state). One special applied at 143.317 s; game readiness returned at 144.738 s and the next jab applied at 149.785 s. Subsequent punches also applied. Camera ~29.8fps, game mostly ~59–60fps; isolated ~182/183ms game frames remain, so zero stutter is not claimed. This change does not tune recognition thresholds or game performance.
