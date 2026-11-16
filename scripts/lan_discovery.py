"""Zero-server LAN discovery. Explicit IP is always available as a fallback."""
import json
import socket
import time

VERSION = 30


def discover(timeout=1.2, targets=None):
    found = {}
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        sock.bind(('', 0)); sock.settimeout(.1)
        targets = targets or [('255.255.255.255', 5067)]
        for address in targets:
            try: sock.sendto(b'REDBOOL_DISCOVER_V1', address)
            except OSError: pass
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            try: data, peer = sock.recvfrom(1024)
            except socket.timeout: continue
            except OSError: break
            try:
                row = json.loads(data)
                if (row.get('service') == 'REDBOOL' and row.get('view_version') == VERSION
                        and row.get('view_port') == 5067 and row.get('p2_port') == 5066):
                    found[peer[0]] = row
            except (ValueError, UnicodeError, AttributeError): continue
    return sorted(found)


if __name__ == '__main__':
    peers = discover()
    print('\n'.join(peers) if peers else 'No compatible host found. Start the host game; use its IPv4 if broadcasts are blocked.')
