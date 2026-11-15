"""Compare sampled host/guest payloads that the guest assembled. No video needed."""
import argparse
import json
from pathlib import Path
import re


def compare(host, guest):
    pattern = r'\[REDBOOL VIEW AUDIT\].*session=([a-f0-9]+) seq=(\d+) kind=(\d+) sha256=([A-F0-9]+)'
    def hashes(text):
        return {(s, int(n), int(k)): h for s,n,k,h in re.findall(pattern, text)}
    a = hashes(host); b = hashes(guest)
    applied = {(s,int(n)) for s,n in re.findall(r'\[REDBOOL VIEW APPLIED\] session=([a-f0-9]+) seq=(\d+)', guest)}
    complete = [f for f in applied if all((*f,k) in a and (*f,k) in b for k in (1,2,3))]
    mismatches = [dict(session=s,sequence=n,part=k) for s,n in complete for k in (1,2,3)
                  if a[s,n,k] != b[s,n,k]]
    return {'sampled_applied_frames':len(complete), 'mismatches':mismatches,
            'passed':bool(complete) and not mismatches,
            'scope':'1Hz sampled assembled game/pose payloads; not every rendered frame or camera-to-screen latency'}


if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('host',type=Path);p.add_argument('guest',type=Path)
    a=p.parse_args();result=compare(a.host.read_text(errors='replace'),a.guest.read_text(errors='replace'))
    print(json.dumps(result,indent=2));raise SystemExit(0 if result['passed'] else 1)
