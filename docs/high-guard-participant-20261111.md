# High-guard participant replay — 2026-11-11

A second participant completed calibration but often kept the supporting hand at high block height. Raising the other arm could leave the bilateral block/raise latch engaged. This is a gesture-routing limitation, not evidence that a different person requires retraining or that calibration was omitted.

V21 adds a bounded high-block → new upward stroke path. A stable folded arm supplies the reference; a fresh upward excursion, asymmetric wrist heights, compact supporting arm, valid joints and recent block are required. Forward/unreturned punches are excluded. Lowering one hand from block is not an upward stroke. Holding the raise cannot repeat; a folded physical return is required to rearm. Ordinary punch and chest-X thresholds are unchanged.

Development evidence (not independent person-generalization accuracy):
- Latest two sessions: original 17/36 events retained; five video-reviewed missed raises recovered in the second session (about 113.08, 115.61, 123.31, 129.05, 162.29 seconds). No other new events in these replays.
- Prior 21 sessions / 116,186 frames: 20 event sequences unchanged. One old clip gains one video-confirmed left raise at frame 4070; no prior events removed.
- Existing 183 tests pass after restoring the Lite reference asset in the isolated test fixture; six new rule tests and four new participant recording tests also pass. Mirrored replay is synthetic, not another independent participant.
- Actual latest block → punch attempts within one second: 5/5 applied; recognized release → guard-off ACK 23–43 ms. First session adds 2/2. These are input/ACK measurements, not body-onset-to-display latency.
- Actual X events: two applied across both sessions; another rejected after player KO. No attack was recorded within five seconds after either applied X, and the round locked, so live post-special chaining is not proven by these recordings.
- Camera ~29.97–29.98 fps without capture gaps over 200 ms. Latest game mostly ~59–60 fps; one 247 ms frame remains unexplained. Do not equate percentile frame times with zero stutter.

New players should press C for their own comfortable guard. Helpers should step outside the camera view; the current pose pipeline tracks one person and does not guarantee multi-person identity locking. Very low off-frame hands remain intentionally rejected. No new model training or Lite→Full switch was made.
