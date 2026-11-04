# Combat framing — 2026-10-27

## Applied result

Applied a closer fighting view to ScoutPass, all ten StoryStages battle scenes, and GameScene. With the same actors, pose, root positions (x = ±1.6), FOV and output resolution, visible character height increased by 17.2–17.6% in story arenas and 19.7% in GameScene. Enlargement varies with separation and pose; keeping both fighters visible takes priority.

HUD assets, combat timing, animation assets and scene routing were not changed by this camera update. Existing 360-degree arena/manual orbit controls and movement-driven camera angles remain in use. GameScene retains its side-follow mode.

## Settings and implementation

- Minimum distance: story arena 4.6; GameScene 5.35.
- Focus height above the higher root: 1.2; combat pitch: 7 degrees.
- Horizontal/vertical framing fractions: 0.90 / 0.80.
- Existing FOV values remain 47 (story) and 43 (GameScene).
- Base framing follows actor roots with the existing exponential smoothing.
- Cached head, hip, hand, foot and toe landmarks provide a final visibility check after animation and camera effects. The camera retreats only as needed to retain the silhouettes during high kicks, special zooms and KO. Runtime code does not bake meshes.
- ScoutPassBuilder, ScoutArena360Builder and StoryStagesBuilder reuse CombatFramingInstaller.ApplyToCamera so rebuilt scenes retain the settings.

## Verification

Unity 6000.4.5f1 compiled and executed the installed scenes in Play Mode: **201 checks passed, zero failed, zero captured gameplay errors**.

Checks covered all twelve scenes: saved settings, measured enlargement, approach/separation, animated high kicks, 4:3 / 16:10 / 21:9 framing, four orbit quadrants in the eleven orbit arenas, special zoom and FOV restoration, and actual finishing-hit KO with zoom. Character visibility was measured using baked skinned vertices in editor verification only. Player progress was restored byte-for-byte (matching SHA256 hashes).

The verification uses controlled fighter positions and temporarily disables input, AI and battle scene transitions in unsaved Play Mode. It is a camera regression check, not a new end-to-end campaign or performance benchmark. The log also includes the pre-existing missing-material HitVfx warning; this update did not modify hit-effect assets.

## Review files

Local gallery: `C:/Users/sbpar/Documents/VisionStriker_CombatFraming_20261027/index.html`.

Includes 72 actual Unity screenshots and three short camera-test videos (ScoutPass, EasternRidge, SluiceFortress). Before images use the former framing equations at the identical pose; the videos change root spacing programmatically to exercise framing. Installation backups, applied.txt, checks.txt, metrics.csv and unity.log are stored beside the gallery. Temporary video source frames were removed after MP4 decoding succeeded.

No Git commit or push was performed for this update.
