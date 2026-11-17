"""Read-only, fail-closed deployment gate for an Apple Silicon macOS package.

Reads Mach-O load commands directly; never loads a library, invokes a packaged
executable, opens a camera, or patches binary metadata. Requires arm64 in every
native file and checks every shipped slice against --max-macos (default 14.0).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import struct
import sys

THIN = {b"\xce\xfa\xed\xfe": ("<", False), b"\xfe\xed\xfa\xce": (">", False),
        b"\xcf\xfa\xed\xfe": ("<", True), b"\xfe\xed\xfa\xcf": (">", True)}
FAT = {b"\xca\xfe\xba\xbe": (">", False), b"\xbe\xba\xfe\xca": ("<", False),
       b"\xca\xfe\xba\xbf": (">", True), b"\xbf\xba\xfe\xca": ("<", True)}
ARM64 = 0x0100000C
ARCHITECTURES = {ARM64: "arm64", 0x01000007: "x86_64", 7: "i386", 12: "arm"}
MACOS = 1


class MachOError(ValueError):
    pass


def version(value: str) -> tuple[int, int, int]:
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){0,2}", value):
        raise ValueError("macOS version must be MAJOR[.MINOR[.PATCH]]")
    fields = tuple(int(x) for x in value.split("."))
    fields += (0,) * (3 - len(fields))
    if fields[0] < 1 or fields[0] > 65535 or any(x > 255 for x in fields[1:]):
        raise ValueError("macOS version is outside Mach-O version ranges")
    return fields


def format_version(value: tuple[int, int, int]) -> str:
    return ".".join(str(x) for x in value)


def unpack_version(value: int) -> tuple[int, int, int]:
    return value >> 16, (value >> 8) & 255, value & 255


def read_at(stream, offset: int, size: int, limit: int) -> bytes:
    if offset < 0 or size < 0 or offset + size > limit:
        raise MachOError("truncated or out-of-bounds Mach-O data")
    stream.seek(offset)
    result = stream.read(size)
    if len(result) != size:
        raise MachOError("file changed or truncated while reading")
    return result


def thin_slice(stream, start: int, size: int, file_size: int) -> dict:
    end = start + size
    if end > file_size or size < 4:
        raise MachOError("invalid slice bounds")
    magic = read_at(stream, start, 4, end)
    if magic not in THIN:
        raise MachOError("fat slice does not contain a thin Mach-O header")
    endian, is64 = THIN[magic]
    header_size = 32 if is64 else 28
    fields = struct.unpack(endian + ("8I" if is64 else "7I"), read_at(stream, start, header_size, end))
    _, cpu, subtype, filetype, count, command_bytes, *_ = fields
    if count > 65536 or command_bytes > size - header_size:
        raise MachOError("invalid load-command table bounds")
    if cpu == ARM64 and not is64:
        raise MachOError("arm64 slice uses a 32-bit Mach-O header")
    position = start + header_size
    command_end = position + command_bytes
    minima = []
    for _ in range(count):
        command, length = struct.unpack(endian + "2I", read_at(stream, position, 8, command_end))
        if length < 8 or length % 4 or position + length > command_end:
            raise MachOError("invalid load-command size")
        if command == 0x32:  # LC_BUILD_VERSION
            if length < 24:
                raise MachOError("truncated LC_BUILD_VERSION")
            platform, minimum, sdk, tools = struct.unpack(endian + "4I", read_at(stream, position + 8, 16, command_end))
            if platform != MACOS:
                raise MachOError("LC_BUILD_VERSION targets platform %d, not macOS" % platform)
            if length != 24 + tools * 8:
                raise MachOError("invalid LC_BUILD_VERSION tool count")
            minima.append((minimum, "LC_BUILD_VERSION"))
        elif command == 0x24:  # LC_VERSION_MIN_MACOSX
            if length != 16:
                raise MachOError("invalid LC_VERSION_MIN_MACOSX size")
            minimum, sdk = struct.unpack(endian + "2I", read_at(stream, position + 8, 8, command_end))
            minima.append((minimum, "LC_VERSION_MIN_MACOSX"))
        elif command in (0x25, 0x2F, 0x30):
            raise MachOError("minimum-version command targets an Apple OS other than macOS")
        position += length
    if position != command_end:
        raise MachOError("load-command count and sizeofcmds disagree")
    if not minima:
        raise MachOError("missing macOS minimum-version load command")
    if len({entry[0] for entry in minima}) != 1:
        raise MachOError("conflicting macOS minimum-version load commands")
    minimum = unpack_version(minima[0][0])
    if minimum[0] == 0:
        raise MachOError("missing/zero macOS minimum version")
    return {"architecture": ARCHITECTURES.get(cpu, "cpu_0x%08x" % cpu), "cpu_type": cpu,
            "cpu_subtype": subtype, "file_type": filetype, "offset": start, "size": size,
            "minimum_macos": format_version(minimum), "minimum_commands": [x[1] for x in minima]}


def inspect_macho(path: Path) -> list[dict] | None:
    """Return all validated slices, or None for a file without a Mach-O magic."""
    with path.open("rb") as stream:
        file_size = os.fstat(stream.fileno()).st_size
        magic = stream.read(4)
        if magic in THIN:
            return [thin_slice(stream, 0, file_size, file_size)]
        if magic not in FAT:
            return None
        endian, is64 = FAT[magic]
        count = struct.unpack(endian + "I", read_at(stream, 4, 4, file_size))[0]
        entry_size = 32 if is64 else 20
        if count < 1 or count > 1024:
            raise MachOError("invalid fat slice count")
        table_end = 8 + count * entry_size
        read_at(stream, 8, count * entry_size, file_size)
        spans = []
        slices = []
        for index in range(count):
            entry = struct.unpack(endian + ("2I2Q2I" if is64 else "5I"),
                                  read_at(stream, 8 + index * entry_size, entry_size, file_size))
            cpu, subtype, offset, size, alignment = entry[:5]
            if is64 and entry[5] != 0:
                raise MachOError("nonzero reserved field in fat64 architecture table")
            if offset < table_end or size == 0 or offset + size > file_size:
                raise MachOError("invalid fat slice bounds")
            if alignment > 63 or offset % (1 << alignment):
                raise MachOError("invalid fat slice alignment")
            if any(offset < other_end and other_start < offset + size for other_start, other_end in spans):
                raise MachOError("overlapping fat slices")
            spans.append((offset, offset + size))
            item = thin_slice(stream, offset, size, file_size)
            if item["cpu_type"] != cpu or item["cpu_subtype"] != subtype:
                raise MachOError("fat architecture table disagrees with thin header")
            slices.append(item)
        return slices


def file_hash(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def audit(root: Path, max_macos: str = "14.0", required_paths: tuple[str, ...] = ()) -> dict:
    target = version(max_macos)
    root = root.resolve(strict=True)
    report = {"schema": "redbool.macos-native-minimum/1", "root": str(root),
              "maximum_macos": format_version(target), "required_architecture": "arm64",
              "all_shipped_slices_checked": True, "scope": "Static deployment metadata only; no OS runtime compatibility claim",
              "passed": False, "native_files": [], "errors": [], "symlinks": [], "ignored_files": 0}
    errors = report["errors"]
    seen = set()
    native_paths = set()

    def failure(path: str, message: str):
        errors.append({"path": path, "error": message})

    def check_link(path: Path) -> bool:
        relative = str(path.relative_to(root))
        try:
            resolved = path.resolve(strict=True)
            resolved.relative_to(root)
        except (OSError, ValueError, RuntimeError) as exception:
            failure(relative, "broken, cyclic, or package-escaping symlink: " + str(exception))
            return False
        report["symlinks"].append({"path": relative, "target": str(resolved.relative_to(root))})
        return True

    def check_file(path: Path):
        relative = str(path.relative_to(root)) if root.is_dir() else root.name
        try:
            resolved = path.resolve(strict=True)
            if not stat.S_ISREG(resolved.stat().st_mode):
                failure(relative, "package entry is not a regular file")
                return
            if resolved in seen:
                return
            slices = inspect_macho(resolved)
            if slices is None:
                if path.suffix.lower() in (".so", ".dylib", ".bundle"):
                    failure(relative, "native-library filename has no Mach-O header")
                else:
                    report["ignored_files"] += 1
                return
            seen.add(resolved)
            native_paths.add(resolved)
            row = {"path": relative, "sha256": file_hash(resolved), "slices": slices}
            report["native_files"].append(row)
            if not any(item["cpu_type"] == ARM64 for item in slices):
                failure(relative, "missing required arm64 slice")
            for item in slices:
                if version(item["minimum_macos"]) > target:
                    failure(relative, "%s minimum macOS %s exceeds supported target %s" %
                            (item["architecture"], item["minimum_macos"], format_version(target)))
        except (OSError, ValueError, struct.error) as exception:
            failure(relative, str(exception))

    if root.is_dir():
        def walk_error(exception):
            failure(str(exception.filename), "unreadable package directory: " + str(exception))
        for current, directories, files in os.walk(root, followlinks=False, onerror=walk_error):
            directories.sort(); files.sort()
            for name in list(directories):
                path = Path(current) / name
                if path.is_symlink():
                    check_link(path)
                    directories.remove(name)
            for name in files:
                path = Path(current) / name
                if path.is_symlink() and not check_link(path):
                    continue
                check_file(path)
    elif root.is_file():
        check_file(root)
    else:
        failure(str(root), "package path is not a regular file or directory")
    for relative in required_paths:
        try:
            path = (root / relative).resolve(strict=True)
            path.relative_to(root)
            if path not in native_paths:
                failure(relative, "required executable was not validated as Mach-O")
        except (OSError, ValueError, RuntimeError) as exception:
            failure(relative, "missing or invalid required executable: " + str(exception))
    if not report["native_files"]:
        failure(str(root), "no valid Mach-O native files found")
    report["native_file_count"] = len(report["native_files"])
    report["slice_count"] = sum(len(row["slices"]) for row in report["native_files"])
    report["passed"] = not errors
    return report


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--max-macos", default="14.0", help="Oldest macOS promised to customers; default:14.0")
    parser.add_argument("--require", action="append", default=[], metavar="RELATIVE_PATH",
                        help="Require a packaged executable to be a validated Mach-O; repeatable")
    parser.add_argument("--report", type=Path, help="Write complete JSON evidence here, even on failure")
    args = parser.parse_args(argv)
    try:
        result = audit(args.package, args.max_macos, tuple(args.require))
    except (OSError, ValueError, RuntimeError) as exception:
        result = {"passed": False, "root": str(args.package), "errors": [{"error": str(exception)}]}
    encoded = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(encoded, encoding="utf-8")
    print(encoded, end="")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
