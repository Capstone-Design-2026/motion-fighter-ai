# Multi-person X recovery without replacing existing gesture recognition

A held X could be reconstructed as guard by the tracked pose model, while a brief Lite elbow error released the X reservation and committed a punch. Keep Lite VIDEO for ordinary gestures and Full VIDEO X assistance. Add a fresh Full IMAGE observation of the exact same RGB frame, computed on one worker alongside the VIDEO models. Wait for that frame; never queue or reuse an old auxiliary result. The recovery observation participates only in X ownership/confirmation/release, not ordinary punch, raise or movement coordinates.

With recovery enabled, two valid open-pose observations release an X; agreeing extension releases immediately. If an uncommitted owned X has only one valid model, its extension must persist for 120 ms before cancelling the reservation. This suppression does not count toward the 700 ms confirmed X hold. Calibration and tracking safety reset the extension timer. A held fired X retains its latch. Existing single-auxiliary/no-auxiliary APIs remain supported; `--no-cross-redetect` selects the previous live inference configuration.

## Comparison on original multi-person recordings

Baseline is published v25 (`d6cf96f`); candidate v26 uses the same primary/VIDEO observations plus fresh same-frame IMAGE observations recomputed from the original RGB. Three complete recordings cover 43,158 processed frames. RGB review identified the changed intervals; these are development regression data, not an unseen-participant accuracy benchmark.

| Recording | X before → after | Reviewed X-as-punch errors removed | Newly recovered X |
|---|---:|---:|---:|
| 32a6e0753001473eb1b1ba40f2d7f69b (latest two-person) | 6 → 7 | 5 | 1 |
| 9946985b0d0d40fe9d38c6ab0c86ff14 (earlier two-person) | 9 → 12 | 1 | 3 |
| 46439e4a1949469597c80e39575325ba (three-person) | 16 → 17 | 1 | 1 |
| Total | 31 → 36 | 7 | 5 |

Removed false punches: latest 261.560 / 263.760 / 268.762 / 271.727 / 274.263 s; earlier two-person 375.442 s; three-person 206.586 s. All show crossed arms in the RGB review. The old test calling 261 s a true left punch was corrected after that review; the new recorded test asserts suppression of this error.

New X events: latest 202.021 s; earlier two-person 348.906 / 375.943 / 395.710 s; three-person 208.918 s. A previously successful long X hold in the three-person recording is recognized at 347.502 s instead of 349.536 s, about 2.03 s earlier, without duplication. Excluding the seven RGB-confirmed false punches, all 237 remaining prior ordinary-attack outputs and all 31 prior X hold outputs are preserved within the explicit comparison windows. The remaining outputs were not exhaustively relabelled, so preservation is not proof they are all correct. Block-to-right-punch at 212.054 and 451.319 s, and reviewed special-to-raise/punch follow-ups remain recognized.

The total event count decreases 275 → 273 because seven incorrect punches are removed and five correct specials are added. This is not an accuracy percentage. No new ordinary attack is added by the candidate in these recordings. Failing to emit a punch during an ambiguous X does not mean every X attempt is now recognized: some attempted X holds still fail to produce enough confirmed geometry. Model weights were not retrained.

## Rejected alternatives

Replacing Full VIDEO with Full IMAGE lost previously successful X detections. Keeping both preserves those successes. Simply resetting ordinary punch candidates during X ownership produced false opposite-hand punches and was not adopted. Running all models sequentially added unnecessary processing time; the same-frame parallel worker preserves the inference budget.

## Performance and scope

With the current native game running, saved RGB through all three models sustained 30.00 fps; model processing median/p95 15.69/17.13 ms. The game steady interval was 59.66 fps, frame p95 17.47 ms. Initial loading remains separate. A further actual live-pipeline check substitutes recorded RGB for the camera and includes inference, FSM, UDP and lossless recording; its measured results are retained in the release manifest. That check mocks preview rendering and is not new live-camera validation.

The live recorder now stores `processing_metadata.cross_recovery` with the same-frame IMAGE landmarks and timing, plus model-mode provenance and recovery timing in the summary. No original footage is edited. No camera is opened during development. Existing Unity code/content is unchanged by this recognition update.

## Validation

The regression suite includes existing punch/raise/block/lean/special tests, same-frame identity/dimension/mirror/visibility checks, no duplicate special, bounded lone-model extension handling, auxiliary live recording/shutdown, and complete three-recording comparison. Recorded fixtures are local and supplied via `REDBOOL_CROSS_REPLAY_SET`; missing fixtures skip those integration tests rather than implying they ran in CI. Results and exact published hashes are in the review artifacts and release manifest.

Final regression result: 280 tests passed, including complete three-recording comparisons with local fixtures enabled. The original 31 X hold outputs and all 237 other ordinary-attack outputs are preserved; the seven reviewed false punches are removed.

Final connected live-pipeline replay on this Mac uses DEV01 (the Unity receiver's configured actor), 3 s of a repeated recorded guard for calibration and then recorded RGB, with mocked preview rendering: 30.00 fps; capture-to-engine median/p95 19.92/24.19 ms. 1334 processed frames were recorded and integrity-checked. Event delivery: {"transmissions": 9, "GAME_BUSY": 5, "APPLIED": 4}. This is an automated replay, not new camera footage or new human accuracy measurement. Earlier throughput-only trials used an unmatched actor and did not validate delivery; only this final DEV01 trial supports the delivery result.
