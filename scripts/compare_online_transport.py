#!/usr/bin/env python3
"""Deterministic offline transport comparison; never opens a real socket or camera."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import statistics
import sys

PROJECT = Path(__file__).resolve().parents[1]
# A report-directory snapshot remains runnable with --python-project.
if not (PROJECT / "tests/test_online_transport.py").is_file():
    PROJECT = Path(__file__).resolve().parent.parent / "python"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class LinkPolicy:
    """The same semantic packet attempt gets the same impairment in both profiles."""
    def __init__(self, rtt, scenario, trial):
        self.rtt, self.scenario, self.trial = rtt, scenario, trial
        self.attempts = Counter()
        self.counts = Counter()

    def __call__(self, direction, packet):
        kind = packet.get("kind") if direction == "input" else (
            "SYNC" if packet.get("status") == "SYNC" else "ATTACK" if packet.get("event_id") else "STATE")
        key = (direction, kind, packet.get("event_id", ""))
        attempt = self.attempts[key]
        self.attempts[key] += 1
        self.counts[direction + "_attempts"] += 1
        material = json.dumps([self.rtt, self.scenario, self.trial, key, attempt], separators=(",", ":"))
        value = hashlib.sha256(material.encode()).digest()
        if self.scenario == "jitter8_loss10" and int.from_bytes(value[:4], "big") % 100 < 10:
            self.counts[direction + "_dropped"] += 1
            return None
        jitter = (int.from_bytes(value[4:8], "big") % 17 - 8) if self.scenario != "clean" else 0
        return max(1, self.rtt / 2 + jitter)


def simulate(harness, profile, rtt, scenario, age, trial):
    MS, OFFSET = harness.MS, harness.OFFSET
    wire = harness.Wire(profile=profile, one_way_ms=rtt / 2)
    link = LinkPolicy(rtt, scenario, trial)
    wire.policy = link
    # Production DemoSender (which inherits Sender), existing reference receiver,
    # existing Wire clock/ACK/serialization path. Only the virtual link is varied.
    wire.service()

    def pump(duration, state_interval=None):
        for elapsed in range(5, duration + 1, 5):
            wire.advance(5)
            if state_interval and elapsed % state_interval == 0:
                wire.state()
            wire.service()

    # Establish the calibration generation before every aged source, then keep
    # its live lease current; otherwise INPUT_LOCKED would confound event age.
    pump(1200, 20)
    offered = wire.now
    original = offered - age * MS
    event = harness.ActionEvent(
        f"probe-{trial:02d}", "P2", "online-test", 100,
        original, original, original + 150 * MS, "ATTACK", "LEFT", {}, "offline-comparison")
    initial_diag = wire.sender.transport_diagnostics()
    wire.sender.offer(event, calibration_generation=1)
    wire.service()
    pump(400, 20)
    packets = [p for p in wire.sent if p.get("event_id") == event.event_id]
    outcomes = [(p, result, at) for p, result, at in wire.outcomes if p.get("event_id") == event.event_id]
    applied = [(p, result, at) for p, result, at in outcomes if result["status"] == "APPLIED"]
    acks = [row for row in wire.rows if row.get("type") == "ack" and row["ack"].get("event_id") == event.event_id]
    statuses = Counter(result["status"] for _, result, _ in outcomes)
    if applied:
        disposition = "applied"
    elif not packets:
        disposition = "not_sent"
    elif outcomes:
        disposition = "receiver_rejected"
    else:
        disposition = "all_attempts_lost"

    invariant_checks = 0
    def require(condition, name):
        nonlocal invariant_checks
        invariant_checks += 1
        if not condition:
            raise AssertionError(f"{profile}/{rtt}/{scenario}/{age}/{trial}: {name}")

    require(len(packets) <= 3, "bounded event retries")
    require(len(applied) <= 1, "original event applied no more than once")
    for packet in packets:
        require(packet["event_id"] == event.event_id and packet["source_frame_id"] == event.source_frame_id
                and packet["capture_ns"] == event.source_capture_ns
                and packet["commit_ns"] == event.committed_ns
                and packet["event_expires_ns"] == event.expires_ns, "original identity and timestamps preserved")
        require(packet["sent_ns"] + packet["ttl_ms"] * MS <= event.expires_ns,
                "retry TTL does not renew original deadline")
    for packet, result, at in applied:
        require(at < event.expires_ns, "application before original deadline in true virtual clock")
        require(result["applied_ns"] + packet["clock_uncertainty_ns"] <
                event.expires_ns + packet["receiver_offset_ns"], "application respects uncertainty-adjusted deadline")

    first_apply = min((at for _, _, at in applied), default=None)
    event_delivery = dict(wire.sender.delivery)
    # Drain all preceding state packets. Probe a single aged STATE separately,
    # without later STATE overtaking or keeping its guard lease alive.
    pump(200)
    state_offered = wire.now
    state_packet = wire.state(capture_age_ms=age, block=True)
    state_result, state_applied_at, state_deadline = None, None, None
    for _ in range(40):
        wire.advance(5)
        wire.service()
        matches = [(result, at) for p, result, at in wire.outcomes if p["sequence"] == state_packet["sequence"]]
        if matches:
            state_result, state_applied_at = matches[0]
            state_deadline = wire.receiver.deadline if state_result["status"] == "APPLIED" else None
            break
    if state_deadline is not None:
        require(state_deadline <= state_packet["capture_ns"] + state_packet["receiver_offset_ns"]
                + 150 * MS - state_packet["clock_uncertainty_ns"], "state lease preserves source capture deadline")
        # Check expiry using the receiver's actual virtual clock, without renewal.
        remaining = max(0, state_deadline - (wire.now + OFFSET))
        wire.advance((remaining + MS - 1) // MS + 1)
        require(not wire.receiver.block, "guard expires without a fresh source")

    row = {
        "profile": profile, "nominal_rtt_ms": rtt, "scenario": scenario, "trial": trial,
        "source_and_commit_age_at_offer_ms": age,
        "synchronized_at_offer": initial_diag["synchronized"],
        "actual_selected_clock_uncertainty_ms": (initial_diag["clock_uncertainty_ns"] / MS
                                                 if initial_diag["clock_uncertainty_ns"] is not None else None),
        "last_actual_corrected_sync_rtt_ms": (initial_diag["last_sync_corrected_rtt_ns"] / MS
                                              if initial_diag["last_sync_corrected_rtt_ns"] is not None else None),
        "original_event_id": event.event_id,
        "original_capture_ns": event.source_capture_ns, "original_commit_ns": event.committed_ns,
        "original_expires_ns": event.expires_ns, "offered_ns": offered,
        "event_disposition": disposition, "event_attempts": len(packets),
        "event_receiver_status_counts": dict(statuses), "event_sender_delivery_counts": event_delivery,
        "simulated_offer_to_apply_ms": (first_apply - offered) / MS if first_apply is not None else None,
        "simulated_commit_to_apply_ms": (first_apply - original) / MS if first_apply is not None else None,
        "original_deadline_remaining_at_apply_ms": (event.expires_ns - first_apply) / MS if first_apply is not None else None,
        "simulated_first_ack_after_offer_ms": min(((r["ack_receive_ns"] - offered) / MS for r in acks), default=None),
        "aged_state_status": state_result["status"] if state_result else "lost",
        "simulated_aged_state_offer_to_apply_ms": ((state_applied_at - state_offered) / MS
                                                   if state_result and state_result["status"] == "APPLIED" else None),
        "state_capture_ns": state_packet["capture_ns"], "state_receiver_deadline_ns": state_deadline,
        "link_counts_including_warmup_and_state_probe": dict(link.counts),
        "invariant_checks": invariant_checks,
    }
    wire.sender.close()
    return row


def fmt(value):
    return "—" if value is None else f"{value:.1f}"


def write_report(result, destination):
    rows, trials = result["cases"], result["method"]["trials_per_cell"]
    grouped = defaultdict(list)
    for row in rows:
        grouped[(row["scenario"], row["nominal_rtt_ms"], row["source_and_commit_age_at_offer_ms"], row["profile"])].append(row)

    def cell(scenario, rtt, age, profile):
        group = grouped[scenario, rtt, age, profile]
        values = [r["simulated_offer_to_apply_ms"] for r in group if r["event_disposition"] == "applied"]
        return f"{len(values)}/{trials}; {fmt(statistics.median(values) if values else None)}/{fmt(max(values) if values else None)}"

    text = [
        "REDBOOL — 오프라인 입력 전송 비교", "",
        "이 수치는 실제 인터넷 또는 화면 지연 측정값이 아니다.",
        "동일한 합성 입력을 실제 DemoSender/Sender와 기존 Wire/ReceiverState에 통과시킨 결정적 가상시간 결과다.",
        "카메라·인식·Unity 실행/렌더링·Photon SDK·Cloud·실제 OS 소켓/패킷 크기/대역폭은 포함하지 않는다.", "",
        "방법",
        "- LAN과 online은 새 코드의 network_profile 두 설정을 비교한다. 이전 제품과의 실기 벤치마크가 아니다.",
        "- 명목 왕복 지연: 20/40/60/80/100/120 ms, 왕복 양쪽에 절반씩 부여.",
        "- 조건: clean, 각 방향 ±8 ms 결정적 지터, 같은 지터+해시 기반 10% 패킷 드롭 선택.",
        f"- 각 조건/입력 나이/프로필당 {trials}개 고정 fixture. 통계적 모집단 성공률 추정이 아니다.",
        "- 송신 service 5 ms, 가상의 수신 처리 대기 10 ms, 준비/유지 STATE 20 ms. 이 주기는 실측 프레임 시간이 아니다.",
        "- 1200 ms 준비 후 공격 1개. 입력 나이는 capture=commit부터 offer까지 0/20/40/80/140 ms.",
        "- 원래 만료는 commit+150 ms. 재전송도 원래 ID/시각/만료를 유지한다.",
        "- 같은 이벤트의 재전송은 별도 성공으로 세지 않는다. 수신 APPLIED가 성공이며 ACK 도착 지연과 구분한다.",
        "- 실제 가상 SYNC 왕복은 지터·송신 service 간격의 영향으로 명목값과 다를 수 있다. JSON/CSV에 별도 기록했다.",
        "- aged STATE는 공격 검사가 끝난 뒤 독립 probe로 검사한다. 이후 STATE가 lease를 갱신하지 않게 한다.", "",
        "신선한 공격(age=0): 적용 원래 이벤트 수/fixture 수; offer→apply 중앙값/최댓값(ms)",
        "조건 | 명목 RTT | LAN 적용; 중앙/최대 | online 적용; 중앙/최대",
    ]
    for scenario in result["method"]["scenarios"]:
        for rtt in result["method"]["nominal_rtt_ms"]:
            text.append(f"{scenario} | {rtt} | {cell(scenario,rtt,0,'lan')} | {cell(scenario,rtt,0,'online')}")
    text += ["", "명목 RTT와 가상 SYNC 실제 표본의 구분", "조건 | 명목 RTT | offer 직전 마지막 corrected SYNC RTT 범위(ms)"]
    for scenario in result["method"]["scenarios"]:
        for rtt in result["method"]["nominal_rtt_ms"]:
            samples = [r["last_actual_corrected_sync_rtt_ms"] for r in rows
                       if r["scenario"] == scenario and r["nominal_rtt_ms"] == rtt
                       and r["last_actual_corrected_sync_rtt_ms"] is not None]
            text.append(f"{scenario} | {rtt} | {fmt(min(samples) if samples else None)}–{fmt(max(samples) if samples else None)}")
    text += ["", "clean 공격: 원래 입력이 이미 오래됐을 때 적용 수 (각 칸 LAN/online)",
             "명목 RTT | age 0 | age 20 | age 40 | age 80 | age 140"]
    for rtt in result["method"]["nominal_rtt_ms"]:
        values = []
        for age in result["method"]["source_and_commit_age_ms"]:
            counts = [sum(r["event_disposition"] == "applied" for r in grouped["clean", rtt, age, profile]) for profile in ("lan", "online")]
            values.append(f"{counts[0]}/{counts[1]}")
        text.append(f"{rtt} | " + " | ".join(values))
    text += ["", "clean aged STATE: 수신 결과 (각 칸 LAN / online)",
             "명목 RTT | age 0 | age 20 | age 40 | age 80 | age 140"]
    for rtt in result["method"]["nominal_rtt_ms"]:
        values = []
        for age in result["method"]["source_and_commit_age_ms"]:
            states = [",".join(sorted({r["aged_state_status"] for r in grouped["clean", rtt, age, profile]})) for profile in ("lan", "online")]
            values.append(" / ".join(states))
        text.append(f"{rtt} | " + " | ".join(values))
    counts = Counter(r["event_disposition"] for r in rows)
    rejected = Counter()
    for row in rows:
        rejected.update({k: v for k, v in row["event_receiver_status_counts"].items() if k != "APPLIED"})
    text += ["", "전체 fixture 집계", f"원래 이벤트: {dict(counts)}",
             *[f"{profile} 원래 이벤트: {dict(Counter(r['event_disposition'] for r in rows if r['profile'] == profile))}"
               for profile in ("lan", "online")],
             "이 합계는 임의로 선택한 조건 격자의 합이며 실사용 성공률이 아니다.",
             f"수신한 시도별 비적용 결과(재전송 포함): {dict(rejected)}",
             f"원래 만료/중복 방지/STATE 만료 검증: {result['invariant_checks']}회 통과", "",
             "해석",
             "online 설정은 허용되는 시계 불확실성을 25→75 ms로 바꿔 더 긴 경로에서도 동기화할 기회를 준다.",
             "같은 경로를 물리적으로 빠르게 만들지는 않는다. 두 설정 모두 적용한 clean fixture의 offer→apply는 동일하다.",
             "적용 불가를 0 ms 지연으로 계산하지 않는다. not_sent는 동기화/남은 수명 때문에 실제 공격 패킷이 나가지 않은 경우다.",
             "receiver_rejected는 패킷은 도착했지만 만료·순서·입력 상태 등으로 적용하지 않은 원래 이벤트다.",
             "같은 원래 이벤트가 한 번 적용된 뒤 재전송이 거절된 경우, 원래 이벤트는 applied로만 집계한다.",
             "길어진 경로와 이미 오래된 입력은 150 ms 원래 수명 및 보수적 불확실성 공제를 소모하므로 계속 거절될 수 있다.",
             "STATE/공격 순서 분리 등 프로필 동작 차이도 포함된다. bounded retry는 두 설정에 공통이다. 오직 시계 제한만 바꾼 실험은 아니다.",
             "이 결과로 실제 카메라→화면 지연, 손동작 인식률, Photon 서울 성능, 실사용 성공률을 주장할 수 없다.", "",
             "재현", result["reproduction"], "",
             "입력 소스 SHA-256", *[f"{path}: {value}" for path, value in result["source_sha256"].items()], ""]
    (destination / "latency-comparison.txt").write_text("\n".join(text), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-project", type=Path, default=PROJECT)
    parser.add_argument("--output", type=Path, default=PROJECT.parent / "verification")
    parser.add_argument("--trials", type=int, default=12)
    args = parser.parse_args()
    if not 1 <= args.trials <= 100:
        parser.error("--trials must be 1..100")
    project = args.python_project.resolve()
    sys.path.insert(0, str(project / "src"))
    harness_file = project / "tests/test_online_transport.py"
    spec = importlib.util.spec_from_file_location("redbool_existing_online_harness", harness_file)
    harness = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(harness)
    paths = [harness_file, project / "src/realtime_vision/transport.py", project / "src/redbool_demo/transport.py",
             project / "src/action_engine/types.py", project / "scripts/compare_online_transport.py"]
    before = {str(path.relative_to(project)): digest(path) for path in paths}
    method = {"nominal_rtt_ms": [20, 40, 60, 80, 100, 120], "scenarios": ["clean", "jitter8", "jitter8_loss10"],
              "source_and_commit_age_ms": [0, 20, 40, 80, 140], "trials_per_cell": args.trials,
              "sender_service_ms": 5, "receiver_processing_wait_ms": 10, "state_interval_ms": 20,
              "warmup_ms": 1200, "event_original_ttl_ms": 150,
              "excluded": ["camera", "recognition", "Unity execution/rendering", "Photon SDK/Cloud", "OS networking", "bandwidth/packet-size cost"]}
    rows = [simulate(harness, profile, rtt, scenario, age, trial)
            for scenario in method["scenarios"] for rtt in method["nominal_rtt_ms"]
            for age in method["source_and_commit_age_ms"] for trial in range(args.trials) for profile in ("lan", "online")]
    after = {str(path.relative_to(project)): digest(path) for path in paths}
    if before != after:
        raise AssertionError("Source or existing test harness changed during comparison")
    result = {"schema_version": 1, "scope": "deterministic offline command-path simulation; no measured frame or Cloud latency",
              "method": method, "source_sha256": before, "invariant_checks": sum(r["invariant_checks"] for r in rows),
              "reproduction": f'"{sys.executable}" "{project / "scripts/compare_online_transport.py"}" --trials {args.trials}',
              "cases": rows}
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "latency-comparison.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    with (args.output / "latency-comparison.csv").open("w", newline="", encoding="utf-8") as file:
        writer = csv.DictWriter(file, fieldnames=list(rows[0]))
        writer.writeheader()
        for row in rows:
            writer.writerow({k: json.dumps(v, sort_keys=True) if isinstance(v, dict) else v for k, v in row.items()})
    write_report(result, args.output)
    snapshot = args.output / "latency-comparison-runner.py"
    if snapshot.resolve() != Path(__file__).resolve():
        shutil.copyfile(__file__, snapshot)
    print(f"PASS: {len(rows)} original-event fixtures; {result['invariant_checks']} deadline/dedup checks.")
    print(f"Report: {args.output / 'latency-comparison.txt'}")
    print("Offline simulation only. No camera, rendered frames, Photon Cloud or real sockets were used.")


if __name__ == "__main__":
    main()
