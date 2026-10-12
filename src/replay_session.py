"""Deterministic, camera-free fast replay of session schema v1 observations."""

import argparse
import json
from pathlib import Path
import warnings

from session_recording import check_schema, load_manifest


def iter_frames(session_dir, *, allow_truncated_tail=True):
    """Read fresh on every call; retain missing poses and nulls verbatim.

    Only a malformed final line without a newline can be skipped for crash
    recovery. Other corruption, schema mismatches and sequence errors fail.
    """
    load_manifest(session_dir)
    previous_ns = None
    previous_attempt_id = 0
    expected_id = 1
    def reject_constant(value):
        raise ValueError(f"Nonstandard JSON constant: {value}")
    with (Path(session_dir) / "frames.jsonl").open("rb") as stream:
        for line in stream:
            try:
                record = json.loads(line, parse_constant=reject_constant)
            except (json.JSONDecodeError, UnicodeDecodeError):
                if allow_truncated_tail and not line.endswith(b"\n"):
                    warnings.warn("Ignoring truncated final frame record", RuntimeWarning)
                    return
                raise
            check_schema(record)
            if type(record.get("frame_id")) is not int or record["frame_id"] != expected_id:
                raise ValueError(f"Expected frame_id {expected_id}")
            if record.get("capture_success") is not True:
                raise ValueError("Capture failures belong in diagnostics.jsonl")
            if "capture_attempt_id" in record:
                attempt_id = record["capture_attempt_id"]
                if type(attempt_id) is not int or attempt_id < expected_id or attempt_id <= previous_attempt_id:
                    raise ValueError("Invalid or non-increasing capture_attempt_id")
                previous_attempt_id = attempt_id
            stamp = record.get("capture_return_monotonic_ns")
            if type(stamp) is not int or stamp < 0 or (previous_ns is not None and stamp < previous_ns):
                raise ValueError("Invalid or decreasing capture timestamp")
            yield record
            expected_id += 1
            previous_ns = stamp


def iter_timed_frames(session_dir, **options):
    """Yield (relative seconds, gap seconds, original record), with no sleeps."""
    origin = previous = None
    for record in iter_frames(session_dir, **options):
        stamp = record["capture_return_monotonic_ns"]
        if origin is None:
            origin = previous = stamp
        yield (stamp - origin) / 1e9, (stamp - previous) / 1e9, record
        previous = stamp


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("session_dir", type=Path)
    parser.add_argument("--mode", choices=["fast"], default="fast")
    parser.add_argument("--strict-tail", action="store_true")
    args = parser.parse_args()
    for relative, gap, record in iter_timed_frames(
        args.session_dir, allow_truncated_tail=not args.strict_tail
    ):
        print(json.dumps({"relative_seconds": relative, "gap_seconds": gap,
                          "frame": record}, allow_nan=False))


if __name__ == "__main__":
    main()
