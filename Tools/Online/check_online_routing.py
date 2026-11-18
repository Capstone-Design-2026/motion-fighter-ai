"""Compile real SDK-independent C# wire sources and run offline contract checks."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    compiler, runtime = shutil.which('mcs'), shutil.which('mono')
    if not compiler or not runtime:
        parser.error('Existing mcs and mono are required; no packages are downloaded by this check')
    sources = [root/'Assets/RedboolVision/Online/RedboolOnlineWire.cs',
               root/'Assets/RedboolVision/Online/RedboolPhotonWire.cs',
               Path(__file__).with_name('test_online_routing.cs')]
    with tempfile.TemporaryDirectory(prefix='redbool-online-wire-') as folder:
        executable = Path(folder)/'OnlineRoutingCheck.exe'
        compile_result = subprocess.run([compiler, '-langversion:latest', '-out:'+str(executable),
            *map(str, sources)], capture_output=True, text=True)
        if compile_result.returncode:
            raise SystemExit(compile_result.stdout + compile_result.stderr)
        result = subprocess.run([runtime, str(executable)], capture_output=True, text=True)
        try:
            report = json.loads(result.stdout)
        except ValueError:
            raise SystemExit(result.stdout + result.stderr)
    report['source_sha256'] = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    report['compiler'] = compiler
    report['runtime'] = runtime
    report['exit_code'] = result.returncode
    report['compiler_messages'] = compile_result.stdout + compile_result.stderr
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(report, indent=2))
    raise SystemExit(result.returncode)


if __name__ == '__main__':
    main()
