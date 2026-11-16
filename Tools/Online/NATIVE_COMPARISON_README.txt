Native LAN / Photon comparison

native_online_compare.py prepares a run unless --run is explicitly supplied.
It uses two actual native game windows and the matching Python sender.
The online run must create and join a real Photon room. Missing SDK, App ID,
room entry, input acknowledgement or complete snapshots is a failure; there is
no offline substitute for a successful connection.

Privacy and scope
Both games receive --redbool-external-camera. The owned camera backend never
starts, and the harness imports no camera, inference or model libraries. It sends
synthetic source states and actions through the real receiver and actual game.
It does not capture screenshots or video. Existing screenshot environment settings
are removed from the child environment. App ID values are never placed in the
command line or result report. Short-lived room codes remain only in local status
files used for automatic joining and become unusable when the run closes.

The source files do not launch Unity Editor or a native game during import/tests.
Only the launched child PIDs are terminated. Occupied input/display ports cause
failure rather than termination of an unrelated game.

Preparation (does not launch games or use the network):
  python Tools/Online/native_online_compare.py \
    --build /path/to/REDBOOL.app \
    --python-root /path/to/matching/python \
    --output /path/to/new-comparison-directory

When native execution is authorized, append --run. The default order is LAN then
Photon, each with 20 seconds of measured normal-attack combat after completing
the real practice sequence. --modes lan or --modes online selects one route.
--timeout 180 bounds startup/practice/combat for each route. The output directory
must not already exist. Use the existing development Python interpreter; only
standard-library modules and the matching pure Python sender/types are needed.

The runtime build must include RedboolOnlineNativeCheck. It also works on the
exact release binary: BOTH --redbool-external-camera and the explicit status
file environment variable are required. Its automation flags are --redbool-online-host and
--redbool-online-join CODE. REDBOOL_ONLINE_CHECK_STATUS is a per-process local
status-file path. These hooks are inactive in ordinary game sessions.

What comparison.json measures
- Actual native FPS/frame time records, with complete combat-window FPS separated
  from menu/practice samples and focused-frame fraction retained.
- Actual synthetic source publication cadence, sender-to-ACK round trips, and
  host application timestamps converted through P1 local clock synchronization.
- First successful attack's host/guest complete snapshot observation. Native
  Stopwatch/realtime anchors remove log polling from the estimated arrival time.
  The corresponding clock uncertainty is reported. A separate file-observation
  upper bound includes buffering and polling. Neither is physical screen latency.
- Original shared gameplay fields agree in host and guest snapshots, without
  rewind. This comparison is not a pixel, bone pose or hit-registration test.
- Existing local anticipation logs measure local action arrival to an actual
  bone write. They are presentation-only and not proof that a hit landed.
- Photon SDK ingress/egress byte-counter differences for each process during
  combat. These exclude transport headers and are not a billing estimate.

The two games share one computer and uplink. This is a real relay traversal but
not two geographically separated players or a physical-camera recognition test.
Full snapshot tracing adds identical diagnostic work to both modes. No artificial
WAN impairment is imposed by this harness; offline jitter/loss coverage lives in
the separate protocol tests. Special moves are used only to complete practice,
not in measured combat, so cut-in/time-scale effects do not confound this sample.

Local harness checks (no processes/network/camera):
  python Tools/Online/test_native_online_compare.py

Separate behavior checks after the20-second measurement
The harness holds each player's real BLOCK, confirms game_guard_active from the
authoritative ACK, then publishes release STATE and a normal punch together.
A pass requires APPLIED, a guard-inactive ACK, a higher guest attack serial, and
a changed complete guest bone pose. Host/guest bone quaternion differences must
remain below the existing0.0001 component tolerance. This verifies received pose
data, not physical display pixels.

For each player, SPECIAL then a normal punch is attempted only when fresh real
feedback says special_ready. Post-special readiness must come from a STATE sent
after the special ACK, preventing reordered old readiness from authorizing the
follow-up. Uncharged specials are reported unavailable; no charge/health or ACK
is fabricated. The I-key refill explicitly does not operate in PvP. Native
rematch has no existing command API and requires the host's resolved-round UI/R,
so this camera-free harness reports it unavailable instead of injecting input.
Each behavior check is bounded. Passed/failed/unavailable counts are separate;
unavailable coverage is never counted as a pass. These extra phases are excluded
from normal-combat RTT, source-cadence, traffic and complete FPS-window samples.
