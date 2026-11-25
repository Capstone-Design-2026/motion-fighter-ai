"""Send motion actions and movement to Unity over UDP as JSON messages."""

import json
import socket
import time

from config import UNITY_HOST, UNITY_PORT


TEST_PAYLOADS = [
    ("IDLE", "NEUTRAL", 1.0, 0.0),
    ("ATTACK", "RIGHT", 1.0, 0.7),
    ("BLOCK", "LEFT", 1.0, 0.7),
    ("JUMP", "RIGHT", 1.0, 0.6),
    ("CROUCH", "LEFT", 1.0, 0.6),
    ("NONE", "NEUTRAL", 1.0, 0.0),
]


def build_payload(
    action: str,
    movement: str,
    confidence: float,
    movement_strength: float,
) -> dict:
    """Create the Unity action and movement payload."""
    return {
        "action": action,
        "movement": movement,
        "confidence": float(confidence),
        "movement_strength": min(max(float(movement_strength), 0.0), 1.0),
        "timestamp": time.time(),
    }


def send_action(
    action: str,
    movement: str,
    confidence: float,
    movement_strength: float,
) -> bool:
    """Send one action and movement payload to Unity via UDP."""
    payload = build_payload(action, movement, confidence, movement_strength)
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
            for action, movement, confidence, movement_strength in TEST_PAYLOADS:
                send_action(action, movement, confidence, movement_strength)
                time.sleep(1)
    except KeyboardInterrupt:
        print("\nStopped Unity UDP sender.")


if __name__ == "__main__":
    main()
