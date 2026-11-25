# Movement, waiting facing and crossed-arm assist

The demo accepts attacks independently of movement. Unity pauses travel during the authored attack and resumes a still-held direction afterward. Synthetic ±12-degree rotation/translation of recorded landmarks preserves jab, straight and both raises; this is a geometry regression, not accuracy on new people.

Unity: map recognized light lean .35 to .65 before existing 60ms smoothing; zero remains zero and packet expiry is unchanged. Camera-controlled player travel uses .55 instead of .40 of base speed. Player stride/lift increase 35%; AI tuning is unchanged. While calibration locks combat, both fighters update resting facing without clearing KO/story locks.

Python: Lite remains authoritative for punches, raises, block and movement. Optional --cross-pose runs verified Full on the same RGB frame for chest-X only. Reject mismatched actor/epoch/frame/timestamp/dimensions/mirroring, invalid shoulders and spatially different bodies. Never reuse an older auxiliary frame. Store auxiliary landmarks/timing in processing_metadata.cross_pose and model hash in the manifest. Calibration, dwell, one-event latch, event expiry and guard release remain. The learned-guard preset rejects this option explicitly.

Session 9946985b0d0d40fe9d38c6ab0c86ff14: Lite produced 82 events; assisted replay preserves them and adds five visually checked X events (224.69, 354.84, 370.64, 384.14, 420.61 seconds). This retrospective development check is not held-out accuracy. Full alone loses some working gestures, so it is not substituted globally.

Latest actual play: 13,726 processed frames, about 29.98 fps. First-hit/special intervals had no >50ms game frame; one 56.53ms stall preceded calibration/combat. Responses: 71 APPLIED and 11 GAME_BUSY due to hitstun/attack/round locks. Response counts are not recognition recall. A round-ending special cannot immediately chain another ordinary attack.

Global shoulder-axis rotation and automatic lean-reference adaptation were rejected after they changed previously working gesture classifications. Those experiments are excluded. Person-to-person punch variation and some crossed-arm misses remain. C is necessary on participant changes but cannot fix incorrect landmarks. Representative labeled multi-person actions and non-actions need person-separated validation before promoting a learned general-purpose model.

Validation: 46 Unity motion/preparation checks pass. All 60 walking/contact/stop/collision checks pass; six legacy idle mesh/hop thresholds fail identically before/after, so the complete old walking suite is NOT reported as passing. Local evidence: review_20261111_161324. Original recordings and unrelated materials/scenes/plugins are preserved. No real camera was opened during this work.
