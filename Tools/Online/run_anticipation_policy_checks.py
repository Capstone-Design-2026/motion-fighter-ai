#!/usr/bin/env python3
"""Test the production anticipation time/dedup policy without game, camera or network."""
import argparse
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path,
                        default=Path("/Applications/Unity/Hub/Editor/6000.4.5f1/Unity.app"),
                        help="Installed Unity.app providing the C# compiler and Mono")
    args = parser.parse_args()
    scripting = args.unity / "Contents/Resources/Scripting"
    mono = scripting / "MonoBleedingEdge/bin/mono"
    compiler = scripting / "MonoBleedingEdge/lib/mono/4.5/csc.exe"
    if not mono.is_file() or not compiler.is_file():
        parser.error("Unity's bundled C# compiler/Mono was not found; pass --unity.")
    here = Path(__file__).resolve().parent
    source = here.parent.parent / "Assets/RedboolVision/Online/RedboolOnlineAnticipation.cs"
    text = source.read_text(encoding="utf-8")
    marker = "/// <summary>\n/// Online guest"
    if marker not in text or "public sealed class RedboolAnticipationWindow" not in text:
        parser.error("Production policy boundary changed; update this runner before testing.")
    # Compile the actual production policy, not a second implementation of it.
    policy = text.split(marker, 1)[0].replace("using UnityEngine;\n", "")
    with tempfile.TemporaryDirectory(prefix="redbool-anticipation-policy-") as temporary:
        folder = Path(temporary)
        extracted = folder / "Policy.cs"
        extracted.write_text(policy, encoding="utf-8")
        output = folder / "Checks.exe"
        subprocess.run([str(mono), str(compiler), "/nologo", "/warn:4",
                        "/out:" + str(output), str(extracted),
                        str(here / "RedboolAnticipationPolicyChecks.cs")], check=True)
        subprocess.run([str(mono), str(output)], check=True)
    print("Native rig sampling is a separate check: RedboolOnlineAnticipationCheck.Run.")


if __name__ == "__main__":
    main()
