"""Package exactly six pilot metadata logs; never include source video."""

import argparse
import json
from pathlib import Path
import re
import zipfile


TRANSFER_FILES = ("manifest.json", "summary.json", "frames.jsonl", "annotations.jsonl",
                  "video_frames.jsonl", "diagnostics.jsonl")


def package_session(directory):
    directory = Path(directory).resolve()
    for name in TRANSFER_FILES:
        path = directory / name
        if path.is_symlink() or not path.is_file():
            raise ValueError(f"Required regular session file missing or symlink: {path}")
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    subject = manifest.get("subject_id", "")
    session = manifest.get("session_id", "")
    if subject not in ("P01", "P02", "P03", "P04", "P05"):
        raise ValueError("Use an anonymous subject_id P01–P05; names are not allowed in ZIP filenames")
    if not isinstance(session, str) or not re.fullmatch(r"[0-9a-f]{32}", session):
        raise ValueError("Expected the collector's 32-character hexadecimal session_id")
    destination = directory.parent / f"{subject}_{session}_metadata.zip"
    # Exclusive creation avoids overwriting a previous handoff. Fixed arcnames
    # omit local username/path and an allowlist excludes every video format.
    with zipfile.ZipFile(destination, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in TRANSFER_FILES:
            archive.write(directory / name, arcname=name)
    return destination


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("session", type=Path, help="Exact session directory printed by the collector")
    opts = parser.parse_args(argv)
    try:
        print(package_session(opts.session))
    except (OSError, ValueError) as exc:
        parser.exit(1, f"Cannot package session: {exc}\n")


if __name__ == "__main__":
    main()
