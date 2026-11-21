# Two-screen LAN PvP (v28)

Two separate Windows or macOS game processes show the authored ScoutPass arena. The host owns combat, movement, health, contacts, charge, timer and round result. The guest is a visual replica and never runs a second combat simulation. Both local camera recognizers send their existing v26 commands directly to host UDP 5065 (P1) / 5066 (P2). Camera video stays on the laptop that captured it.

This extends v27 personal practice, not the recognition model. The Python payload remains commit `08aeebb`: v26 plus bounded per-person movement ranges collected from verified tutorial cycles. `C` still recalibrates the same baseline; subsequent tutorial samples refine it. Insufficient or inconsistent samples retain v26 defaults. Story gameplay/save behavior and the frozen v26/v27 release folders are preserved.

## Entry points

- Host: `--redbool-pvp --redbool-require-view`
- Guest: `--redbool-pvp-client HOST_IPV4`
- Shared-screen legacy mode: `--redbool-pvp`
- Normal launch: existing story/menu.
- Main menu also exposes host/join controls with an IPv4 input.
- Optional guest display port `--redbool-view-port PORT` supports diagnostic network proxies; production default is 5067.

The host uses Hero02/Seojin for P1 and the existing authored Cheolmun visual (`RunConfig` gate mapping) for P2. Gameplay stats remain cloned player stats, no AI controller is adopted. Original texture colors remain, with 1P/2P UI color identification. Both charge meters reuse `WuxiaCombatBar`; full charge displays the crossed-arm special instruction. The obsolete UDP webcam thumbnail is disabled only in PvP.

## Display channel

`RedboolViewProtocol`, `RedboolViewLink`, `RedboolViewMirror` implement a versioned UDP channel on 5067. Three self-contained datagrams describe one frame: world and two actors. Frames are presented only once all matching parts arrive. Receive work is bounded (128 datagrams/tick, four pending frames). Duplicate/old/out-of-order/incomplete/oversized/invalid-number frames cannot rewind state. The socket is pinned to one endpoint and per-connection nonce; host sessions are distinct. This is a trusted local-network channel, not an Internet matchmaking/security service.

30Hz snapshots contain roots, visual transforms, humanoid joint rotations, pelvis position, camera pose/FOV, health, charge, personal-practice state, timed effects and wave positions. Quaternion quantization preserves compact datagrams. The guest interpolates complete poses over approximately 50ms and never extrapolates attacks or damage. Particle simulation is local presentation; gameplay results are authoritative. Effect IDs prevent duplicate hit effects. Charge/ready effects follow replicated hands; guest waves have gameplay scripts/colliders disabled.

Missing guest acknowledgements or snapshots for 250ms pauses both players and the timer in two-screen mode. Missing camera commands retain the existing 150ms input lease. Reconnection needs fresh complete frames, camera inputs and a one-second neutral hold. Attacks issued during pauses are rejected, not queued. Host `R` rematches without repeating practice for the same people; `Esc` leaves to the story menu. P2 cannot change the host score, timer or save through the display protocol.

## Build and offline checks

Unity 6000.4.5f1, Mono backend. `RedboolLocalDemoBuild.Run` builds all 16 enabled scenes; `REDBOOL_BUILD_OUTPUT` chooses the executable path and `REDBOOL_BUILD_TARGET=windows` selects StandaloneWindows64 (otherwise macOS). Windows Mono build support is required on the Mac editor. The Windows package includes Python 3.11 win_amd64 wheels; installation is offline after Python itself is available.

`RedboolViewSelfCheck.Run` exercises binary roundtrips, quantization, MTU bounds, reorder, missing parts, duplicates, replay/session rejection and malformed data. Latest check: 3,828 assertions including exhaustive truncations and randomized malformed datagrams; maximum datagram 592 bytes, worst-case snapshot 1,748 bytes (52,440 payload bytes/s at 30Hz).

`RedboolPvpPlayCheck.Run` keeps the original combat isolation/save regression suite. With `REDBOOL_DUAL_CLIENT` pointing to a built game, it additionally starts a real second game, verifies complete-frame acknowledgements, kills/restarts only that owned process and checks paused/resumed combat. Final release run passed 65 checks, including both full charge bars and ready instructions. The second process received 41 special-pose frames, 7 wave frames and 82 frames carrying impact effects, including rematch round 1. It also covers both specials, special-followup attacks, held-block release, contact damage for both real models, person replacement and rematch.

The native two-game check runs both builds on one Mac with synthetic camera input and saved RGB inference, never opening a physical camera. A deterministic proxy adds 12–37ms per-hop delay, 5% random loss, 3% duplicate packets and a 1.2s cut. Latest run compared 1,768 complete received frames against their host frames: identical health/charge/practice/round/root positions, quaternion component error <= 0.00002131, no sequence rewind; 273 datagrams dropped. Saved-video inference processed 1,200 frames at 29.997fps. Steady gameplay windows were about 59.3–59.7fps with frame p95 <=17.6ms; the deliberate cut/recovery window on the guest was 56.8fps. Screenshots/diagnostic writes are enabled in that check and can add isolated 49–83ms stalls. Initial scene/shader preparation occurs while combat is paused. Synthetic command acknowledgement p95 was about 19ms on loopback; this is not physical LAN or camera-to-screen latency.

Diagnostics are opt-in: `REDBOOL_VIEW_TRACE_FILE` writes host/received frames; `REDBOOL_VIEW_CAPTURE_DIR` records a few screenshots. Ordinary launchers do not enable either. `REDBOOL PERF` logs remain available for actual exhibition checks.

Windows build/import success is separate from actual Windows execution. Two Windows laptops, their cameras/drivers/firewall and real exhibition LAN still require one end-to-end field test. No zero-latency claim is made: the second screen has network plus interpolation/display delay even though camera commands are not routed through it.

## Upstream comparison

Inspected `origin/feature/unity` at `e656fbe`. Its new changes are approved Hero02 idle/dash/step movement, closer camera composition and measured AI jab range. These change existing gameplay and are not merged into this display-mode extension. Cheolmun was already available in the validated integration. The current motion movement/guard/special/story behavior remains the baseline.

Re-run the native load check with the packaged camera environment activated and `PYTHONPATH` set to the package's `src`. Set `REDBOOL_RELEASE` to the package, `REDBOOL_CHECK_OUTPUT` to a new output directory, and `REDBOOL_RECORDED_VIDEO` to an existing recording's video file. Run `python Tools/DualScreen/native_dual_check.py`. This starts only owned test games and uses synthetic camera commands. Never point it at an active demonstration match; the required ports must be free.

The `Tools/DualScreen` launchers are distribution templates: place them beside the corresponding game build, the validated camera `src` tree, Lite/Full model assets and `release-manifest.json`. Windows also needs the downloaded Python 3.11 win_amd64 wheels under `windows_wheels`. These templates do not download raw camera sessions or bundle training data. Their file checker validates every packaged payload before launch.

Final deployment-settings performance pass (screenshots and detailed frame tracing disabled): host gameplay windows 59.9–60.0fps; guest 59.59–59.78fps, saved-video inference 29.999fps (1,200 frames), host frame p95 <=16.76ms and guest <=17.37ms in gameplay windows. The largest gameplay frame was 35.65ms on the guest. Full metrics are included in the release performance JSON. The earlier screenshot/trace run is retained separately as the state-equivalence evidence. Story startup/continue/loss/save regression: 12 checks pass.
