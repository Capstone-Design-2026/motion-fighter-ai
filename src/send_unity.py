"""Send motion actions to Unity over UDP as JSON messages."""

import json
import socket
import time

from config import UNITY_HOST, UNITY_PORT


TEST_ACTIONS = ["IDLE", "ATTACK", "JUMP", "CROUCH", "BLOCK", "NONE"]


def build_payload(action: str, confidence: float) -> dict:
    """Create the Unity action payload."""
    return {
        "action": action,
        "confidence": float(confidence),
        "timestamp": time.time(),
    }


def send_action(action: str, confidence: float) -> bool:
    """Send one action payload to Unity via UDP."""
    payload = build_payload(action, confidence)
    message = json.dumps(payload).encode("utf-8")

    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.sendto(message, (UNITY_HOST, UNITY_PORT))
    except OSError as exc:
        print(f"[UDP ERROR] Failed to send payload: {payload} ({exc})")
        return False

    print(f"[UDP SEND] {payload}")
    return True


def main():
    """Continuously send test actions to Unity once per second."""
    print("=== Motion Fighter Unity UDP Sender ===")
    print(f"Target: {UNITY_HOST}:{UNITY_PORT}")
    print("Press Ctrl+C to stop.")

    try:
        while True:
            for action in TEST_ACTIONS:
                send_action(action, 1.0)
                time.sleep(1)
    except KeyboardInterrupt:
        print("\nStopped Unity UDP sender.")


if __name__ == "__main__":
    main()
