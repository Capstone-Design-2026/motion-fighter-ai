#!/usr/bin/env python3
"""Offline wire/state checks and real-Unity no-SDK compilation; never connects to Cloud."""
import argparse
import pathlib
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=pathlib.Path,
                        default=pathlib.Path("/Applications/Unity/Hub/Editor/6000.4.5f1/Unity.app"),
                        help="Installed macOS Unity.app")
    args = parser.parse_args()
    scripting = args.unity / "Contents/Resources/Scripting"
    mono = scripting / "MonoBleedingEdge/bin/mono"
    csc = scripting / "MonoBleedingEdge/lib/mono/4.5/csc.exe"
    if not mono.is_file() or not csc.is_file():
        parser.error("Unity's bundled Mono/C# compiler was not found; pass --unity.")
    here = pathlib.Path(__file__).resolve().parent
    project = here.parent.parent
    source = project / "Assets/RedboolVision/Online"
    files = [source / "RedboolPhotonWire.cs", source / "RedboolPhotonSession.cs"]

    def run(*command):
        subprocess.run([str(arg) for arg in command], check=True)

    with tempfile.TemporaryDirectory(prefix="redbool-photon-tests-") as directory:
        output = pathlib.Path(directory)
        test = output / "RedboolPhotonTests.exe"
        run(mono, csc, "/nologo", "/warn:4", "/define:PHOTON_REALTIME_5_OR_NEWER,REDBOOL_PHOTON_CONTRACT_TEST",
            "/out:" + str(test), *files,
            here / "RedboolPhotonContractDoubles.cs", here / "RedboolPhotonTests.cs")
        run(mono, test)
        refs = [scripting / "Managed/UnityEngine/UnityEngine.CoreModule.dll",
                scripting / "Managed/UnityEngine/UnityEngine.JSONSerializeModule.dll",
                scripting / "MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll"]
        run(mono, csc, "/nologo", "/warn:4", "/target:library", "/out:" + str(output / "RedboolPhotonNoSdk.dll"),
            *("/reference:" + str(ref) for ref in refs), *files)
    print("PASS: no-SDK branch compiled against installed Unity assemblies.")
    print("Real Photon SDK compilation and two-device Cloud checks remain separate requirements.")


if __name__ == "__main__":
    main()
