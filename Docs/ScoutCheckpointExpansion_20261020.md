# ScoutPass checkpoint expansion — 2026-10-20

Applied scene: `Assets/Scenes/ScoutPass.unity`.

- Added an open convoy shelter with rice chest, stacked straw bags, jars, bench and basket.
- Added a raised wooden lookout with deck, railing, cross braces, ladder and a moving cloth banner.
- Moved the existing convoy cart into the left supply area. Added timber and supplies on the right.
- Added four shallow landscape banks, a curved road behind the existing gate, stone edging and inset slabs.
- Added restrained grass, leaf litter, small rocks and scrub; dressed the reverse/flank views as well.
- Repositioned four existing pine trees whose branches intersected the new structures.
- Reused project prefabs and textures. No Tripo generation, paid generation or new downloaded assets.
- Combined simple structural meshes by material. Existing props retain their LODs and shared materials.
- New scenery has zero 2D combat colliders. Eighteen camera-only trigger hulls prevent camera entry.

Regeneration: `ScoutStageDressing.ApplyToScene()` calls `ScoutCheckpointExpansion.Apply()`.
The expansion replaces its own `Checkpoint Surroundings` root on each rebuild.
Existing player/enemy animations, runtime gameplay scripts and story routes were not edited by this task.

Verification:
- 43 checks passed; 72 orbit poses over 8 angles, 3 aspect ratios and 3 fighter positions.
- Automatic movement camera, framing, direction changes, pause/KO ownership, shader compilation,
  actual straight hit and save restoration passed. Captured runtime errors: 0.
- The older camera verifier used 1.6 m/s despite the current boxing travel multiplier.
  Updated only the editor verifier to use the production speed and a measured 3.6 m travel leg.
  Camera/gameplay code was not changed. The >10-degree automatic yaw-span assertion remains.
- SHA-256 comparison: 331 protected files unchanged.
- Video: 24 s, 1280x720, 24 fps, silent. Controlled input through PlayerController and production camera;
  this offline capture is not an FPS benchmark.

Review: `C:\Users\sbpar\Documents\VisionStriker_CheckpointExpansion_20261020\index.html`
Backups and validation are retained in the same directory.
