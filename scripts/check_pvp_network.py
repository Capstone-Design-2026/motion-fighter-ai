"""Check the running host's LAN path without a camera, player pairing or gameplay input."""
import argparse
import datetime
import json
from pathlib import Path
import socket
import time
import uuid


def measure(host, port=5067, seconds=20):
    target = (socket.gethostbyname(host), port)
    pending = {}; timings = []; sent = 0; unexpected = 0
    start = time.perf_counter(); finish = start + seconds; next_send = start
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as connection:
        connection.bind(('0.0.0.0', 0)); connection.settimeout(.01)
        while time.perf_counter() < finish + 1:
            now = time.perf_counter()
            if now < finish and now >= next_send:
                token = uuid.uuid4().hex
                pending[token] = now; sent += 1
                connection.sendto(('REDBOOL_PING_V1:' + token).encode('ascii'), target)
                next_send = now + .05
            try:
                raw, peer = connection.recvfrom(512)
            except (socket.timeout, ConnectionResetError):
                continue
            arrived = time.perf_counter()
            token = raw.decode('ascii', errors='replace').removeprefix('REDBOOL_PONG_V1:')
            if peer != target or not raw.startswith(b'REDBOOL_PONG_V1:') or token not in pending:
                unexpected += 1; continue
            timings.append((arrived - pending.pop(token)) * 1000)
    ordered = sorted(timings)
    def percentile(fraction):
        return round(ordered[min(len(ordered)-1, int((len(ordered)-1)*fraction))], 2) if ordered else None
    return {'target': f'{target[0]}:{port}', 'seconds': seconds, 'sent': sent,
            'received': len(timings), 'loss_percent': round(100*(sent-len(timings))/max(1,sent), 2),
            'rtt_ms': {'p50': percentile(.5), 'p95': percentile(.95), 'max': percentile(1)},
            'unexpected': unexpected,
            'scope': 'Network round trip plus host game update scheduling; camera and recognition excluded',
            'note': 'Compare wired/Wi-Fi/hotspot with the same two running games. This is not recognition accuracy or proof of Wi-Fi hardware failure.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--host', required=True); parser.add_argument('--port', type=int, default=5067)
    parser.add_argument('--seconds', type=float, default=20)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 120: parser.error('--seconds must be 1..120')
    result = measure(args.host, args.port, args.seconds)
    path = args.output or Path(__file__).resolve().parents[1] / 'local_data' / 'network_checks' / (datetime.datetime.now().strftime('%Y%m%d_%H%M%S') + '.json')
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2)); print(path)
    if result['received'] == 0:
        raise SystemExit('No updated host response. Check that the updated host game is open, IP and firewall permit UDP5067.')


if __name__ == '__main__': main()
