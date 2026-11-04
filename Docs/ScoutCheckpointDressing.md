# ScoutPass checkpoint dressing — 2026-10-17

Applied to `Assets/Scenes/ScoutPass.unity` only. Existing combat-camera behavior is retained.

- Clustered the checkpoint gate, cult flags, cart and provisions into a visible destination.
- Reused project rice chest, straw bags, jars, bench and brazier; added simple wooden crates, fence segments, low rocks and pine scrub.
- Added a small smoke plume and ember particles to the brazier (56 smoke / 32 ember maximum). The warm point light casts no additional shadows.
- Added ground color variation, moss islands, cart ruts and fight-area scuffs in the existing terrain shader.
- Warmed the main sunlight and balanced it with cooler ambient light.
- Decorative props do not add 2D combat collision. Larger props have trigger camera-obstacle hulls used by the existing camera.
- `ScoutArena360Builder` calls `ScoutStageDressing.ApplyToScene()` so rebuilding the 360-degree arena retains these additions. `Tools > VisionStriker > Dress Scout Checkpoint` can regenerate the decoration deterministically.

## Separate animation study

The breathing idle and alternating boxing steps are PREVIEW ONLY. They are not assigned to production animators, prefabs or combat views. `BoxingRhythmPreview` creates an unsaved editor scene, works with copies, exports draft Humanoid clips outside the project, and removes its temporary editor assets. It changes neither gameplay movement speed nor the production walking correction.

Review videos and verification reports are stored in:
`C:\Users\sbpar\Documents\VisionStriker_ScoutRefinement_20261017`

The preview tests 1.8 m/s travel with a 0.56-second alternating step cycle. It is a new motion direction for review, not a claim of final foot-contact behavior at every gameplay speed.
