"""Synthetic binary fixtures only: no native execution, network or camera."""
import contextlib
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import sys

import importlib.util

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("check_macos_minimum", ROOT / "scripts/check_macos_minimum.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def packed(value):
    a, b, c = gate.version(value)
    return a << 16 | b << 8 | c


def build_command(minimum="14.0", endian="<", platform=1, sdk="27.0"):
    return struct.pack(endian + "6I", 0x32, 24, platform, packed(minimum), packed(sdk), 0)


def thin(minimum="14.0", cpu=gate.ARM64, endian="<", is64=True, old=False, commands=None):
    if commands is None:
        commands = [struct.pack(endian + "4I", 0x24, 16, packed(minimum), packed("27.0"))
                    if old else build_command(minimum, endian)]
    header = [0xFEEDFACF if is64 else 0xFEEDFACE, cpu, 0, 6, len(commands), sum(map(len, commands)), 0]
    if is64:
        header.append(0)
    return struct.pack(endian + "%dI" % len(header), *header) + b"".join(commands)


def fat(parts, endian=">", is64=False):
    entry_size = 32 if is64 else 20
    offset = 8 + entry_size * len(parts)
    entries = []
    content = []
    for cpu, body in parts:
        fields = [cpu, 0, offset, len(body), 2]
        if is64:
            fields.append(0)
        entries.append(struct.pack(endian + ("2I2Q2I" if is64 else "5I"), *fields))
        content.append(body)
        offset += len(body)
    return struct.pack(endian + "2I", 0xCAFEBABF if is64 else 0xCAFEBABE, len(parts)) + b"".join(entries + content)


class MinimumGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def file(self, name, body):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(body)
        return path

    def rejected(self, body, message):
        path = self.file("REDBOOLCamera", body)
        report = gate.audit(path)
        self.assertFalse(report["passed"])
        self.assertTrue(any(message in error["error"] for error in report["errors"]), report)

    def test_thin_modern_and_legacy_minimum_both_endiannesses(self):
        for endian in ("<", ">"):
            for old in (False, True):
                with self.subTest(endian=endian, old=old):
                    path = self.file("Python", thin(endian=endian, old=old))
                    report = gate.audit(path)
                    self.assertTrue(report["passed"], report)
                    self.assertEqual(report["native_files"][0]["slices"][0]["minimum_macos"], "14.0.0")

    def test_sdk_is_not_mistaken_for_minimum(self):
        self.assertTrue(gate.audit(self.file("Python", thin(commands=[build_command("12.0", sdk="27.0")])))["passed"])

    def test_same_host_build_minimum_27_rejected(self):
        self.rejected(thin("27.0"), "exceeds supported target 14.0.0")

    def test_target_configurable_including_patch_boundary(self):
        path = self.file("Python", thin("14.0.1"))
        self.assertFalse(gate.audit(path, "14.0")["passed"])
        self.assertTrue(gate.audit(path, "14.0.1")["passed"])
        self.assertTrue(gate.audit(path, "26.5.2")["passed"])

    def test_fat32_and_fat64_both_endiannesses(self):
        parts = [(gate.ARM64, thin("12.0")), (0x01000007, thin("13.0", cpu=0x01000007))]
        for endian in ("<", ">"):
            for is64 in (False, True):
                with self.subTest(endian=endian, is64=is64):
                    report = gate.audit(self.file("Python", fat(parts, endian, is64)))
                    self.assertTrue(report["passed"], report)
                    self.assertEqual(report["slice_count"], 2)

    def test_x86_only_rejected_even_when_minimum_supported(self):
        self.rejected(thin("10.15", cpu=0x01000007), "missing required arm64")

    def test_every_shipped_slice_is_checked(self):
        self.rejected(fat([(gate.ARM64, thin("12.0")), (0x01000007, thin("27.0", cpu=0x01000007))]), "x86_64 minimum macOS 27.0.0")

    def test_32_bit_macho_parsed_then_rejected_for_arm64(self):
        self.rejected(thin("10.9", cpu=7, is64=False, old=True), "missing required arm64")

    def test_arm64_cannot_use_32_bit_header(self):
        self.rejected(thin(is64=False), "32-bit Mach-O header")

    def test_missing_minimum_is_an_explicit_error(self):
        self.rejected(thin(commands=[]), "missing macOS minimum-version")

    def test_zero_minimum_rejected(self):
        self.rejected(thin(commands=[struct.pack("<6I", 0x32, 24, 1, 0, packed("27.0"), 0)]), "missing/zero")

    def test_non_macos_platform_and_old_apple_os_commands_rejected(self):
        for command in [build_command(platform=6), struct.pack("<4I", 0x25, 16, packed("14"), packed("27"))]:
            with self.subTest(command=command):
                self.rejected(thin(commands=[command]), "macOS")

    def test_conflicting_minimum_commands_rejected(self):
        self.rejected(thin(commands=[build_command("12"), build_command("14")]), "conflicting")

    def test_build_version_tool_count_checked(self):
        self.rejected(thin(commands=[struct.pack("<6I", 0x32, 24, 1, packed("14"), packed("27"), 1)]), "tool count")

    def test_truncated_headers_and_load_commands(self):
        for body in (thin()[:10], thin()[:-1], thin(commands=[struct.pack("<2I", 0x32, 8)])):
            with self.subTest(length=len(body)):
                report = gate.audit(self.file("Python", body))
                self.assertFalse(report["passed"])

    def test_zero_length_command_and_count_size_disagreement(self):
        self.rejected(thin(commands=[struct.pack("<2I", 0x11, 0)]), "load-command size")
        body = bytearray(thin()); struct.pack_into("<I", body, 16, 0)
        self.rejected(body, "count and sizeofcmds disagree")

    def test_fat_overlap_out_of_bounds_and_header_mismatch(self):
        original = fat([(gate.ARM64, thin()), (0x01000007, thin(cpu=0x01000007))])
        overlap = bytearray(original); struct.pack_into(">I", overlap, 36, 48)
        self.rejected(overlap, "overlapping")
        outside = bytearray(original); struct.pack_into(">I", outside, 16, len(original))
        self.rejected(outside, "slice bounds")
        mismatch = bytearray(original); struct.pack_into(">I", mismatch, 8, 7)
        self.rejected(mismatch, "disagrees with thin")

    def test_fat64_reserved_and_alignment_checked(self):
        body = bytearray(fat([(gate.ARM64, thin())], is64=True))
        struct.pack_into(">I", body, 36, 1)
        self.rejected(body, "reserved")
        body = bytearray(fat([(gate.ARM64, thin())]))
        struct.pack_into(">I", body, 24, 63)
        self.rejected(body, "alignment")

    def test_directory_checks_nested_extension_and_framework_binary(self):
        self.file("REDBOOLCamera", thin("12"))
        self.file("_internal/Python.framework/Versions/3.11/Python", thin("12"))
        self.file("_internal/numpy/extension.so", thin("27"))
        self.file("_internal/assets/model.task", b"not a native binary")
        report = gate.audit(self.root, required_paths=("REDBOOLCamera",))
        self.assertFalse(report["passed"])
        self.assertEqual(report["native_file_count"], 3)
        self.assertEqual(report["ignored_files"], 1)
        self.assertEqual(report["errors"][0]["path"], "_internal/numpy/extension.so")

    def test_compatible_framework_symlink_deduplicated(self):
        native = self.file("Python.framework/Versions/3.11/Python", thin("12"))
        (self.root / "Python.framework/Versions/Current").symlink_to("3.11", target_is_directory=True)
        (self.root / "Python.framework/Python").symlink_to("Versions/Current/Python")
        report = gate.audit(self.root, required_paths=("Python.framework/Python",))
        self.assertTrue(report["passed"], report)
        self.assertEqual(report["native_file_count"], 1)
        self.assertEqual(len(report["symlinks"]), 2)

    def test_external_and_dangling_symlinks_fail_closed(self):
        self.file("REDBOOLCamera", thin())
        (self.root / "outside").symlink_to(self.root.parent, target_is_directory=True)
        (self.root / "broken").symlink_to("nonexistent")
        report = gate.audit(self.root)
        self.assertFalse(report["passed"])
        self.assertEqual(len(report["errors"]), 2)

    def test_invalid_library_alias_cannot_hide_behind_earlier_plain_file(self):
        self.file("a-data", b"invalid native")
        self.file("REDBOOLCamera", thin())
        (self.root / "z-alias.so").symlink_to("a-data")
        self.assertFalse(gate.audit(self.root)["passed"])

    def test_no_native_files_and_missing_required_executable_rejected(self):
        self.file("readme.txt", b"text")
        self.assertFalse(gate.audit(self.root)["passed"])
        self.file("library.dylib", thin())
        report = gate.audit(self.root, required_paths=("REDBOOLCamera",))
        self.assertFalse(report["passed"])
        self.assertIn("required executable", report["errors"][0]["error"])

    def test_cli_writes_failure_report_and_nonzero_exit_without_mutation(self):
        path = self.file("REDBOOLCamera", thin("27"))
        before = path.read_bytes()
        output = self.root / "result.json"
        with contextlib.redirect_stdout(io.StringIO()):
            status = gate.main([str(self.root), "--max-macos", "14.0", "--require", "REDBOOLCamera", "--report", str(output)])
        self.assertEqual(status, 1)
        self.assertFalse(json.loads(output.read_text())["passed"])
        self.assertEqual(path.read_bytes(), before)

    def test_invalid_target_fails_with_report(self):
        for target in ("", "banana", "0.0", "14.256", "14.0.0.1"):
            with self.subTest(target=target), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(gate.main([str(self.root), "--max-macos", target]), 1)


class BuildMinimumIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.lite = self.root / "lite.task"; self.lite.write_bytes(b"lite fixture")
        self.full = self.root / "full.task"; self.full.write_bytes(b"full fixture")
        self.output = self.root / "output"
        self.work = self.root / "work"
        self.folder = self.output / "REDBOOLCamera"
        self.argv = ["build", "--pose", str(self.lite), "--cross-pose", str(self.full),
                     "--output", str(self.output), "--work", str(self.work)]
        specification = importlib.util.spec_from_file_location("build_camera_backend_for_minimum_test", ROOT / "scripts/build_camera_backend.py")
        self.build = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(self.build)
        sys.path.insert(0, str(ROOT / "src"))
        self.provenance = {"source_sha256": "a" * 64, "release": "v36.3"}

    def run_build(self, native, *, system="Darwin", extra=(), stale=False, assertions=None):
        calls = []
        def fake_run(command, **kwargs):
            calls.append(command)
            if "PyInstaller" in command:
                self.folder.mkdir(parents=True, exist_ok=True)
                executable = "REDBOOLCamera.exe" if system == "Windows" else "REDBOOLCamera"
                (self.folder / executable).write_bytes(native)
                if stale:
                    (self.folder / "backend-build.json").write_text('{"old_success":true}')
            elif assertions:
                assertions(command)
        with patch.object(sys, "argv", self.argv + list(extra)), \
             patch.object(sys, "platform", "win32" if system == "Windows" else "darwin"), \
             patch.object(self.build.platform, "system", return_value=system), \
             patch.object(self.build.platform, "machine", return_value="AMD64" if system == "Windows" else "arm64"), \
             patch("redbool_demo.backend_provenance.source_provenance", return_value=self.provenance), \
             patch.object(self.build.subprocess, "run", side_effect=fake_run), \
             contextlib.redirect_stdout(io.StringIO()) as stdout:
            self.build.main()
        return calls, stdout.getvalue()

    def test_preparation_only_explicitly_reports_gate_pending(self):
        calls, stdout = self.run_build(b"unused", extra=("--check",))
        data = json.loads(stdout)
        self.assertEqual(calls, [])
        self.assertEqual(data["macos_compatibility"]["status"], "pending")
        self.assertEqual(data["macos_compatibility"]["maximum_macos"], "14.0.0")
        self.assertFalse(self.output.exists())

    def test_invalid_target_rejected_before_copy_or_subprocess(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as raised:
            self.run_build(b"unused", extra=("--max-macos", "14.999"))
        self.assertEqual(raised.exception.code, 2)
        self.assertFalse(self.work.exists())
        self.assertFalse(self.output.exists())

    def test_failure_removes_stale_success_and_never_executes_backend(self):
        executed = []
        with self.assertRaisesRegex(RuntimeError, "compatibility gate failed"):
            self.run_build(thin("27.0"), stale=True, assertions=executed.append)
        self.assertFalse((self.folder / "backend-build.json").exists())
        self.assertEqual(executed, [])
        report = json.loads((self.folder / "backend-macos-minimum.json").read_text())
        self.assertFalse(report["passed"])
        self.assertIn("27.0.0", report["errors"][0]["error"])

    def test_missing_minimum_blocks_success_metadata_and_execution(self):
        executed = []
        with self.assertRaisesRegex(RuntimeError, "compatibility gate failed"):
            self.run_build(thin(commands=[]), assertions=executed.append)
        self.assertEqual(executed, [])
        self.assertFalse((self.folder / "backend-build.json").exists())

    def test_successful_native_gate_precedes_check_and_records_report_hash(self):
        observations = []
        def check(command):
            metadata = json.loads((self.folder / "backend-build.json").read_text())
            evidence = self.folder / "backend-macos-minimum.json"
            self.assertTrue(json.loads(evidence.read_text())["passed"])
            self.assertEqual(metadata["macos_compatibility"]["status"], "passed")
            self.assertEqual(metadata["macos_compatibility"]["report_sha256"], gate.file_hash(evidence))
            self.assertEqual(command, [str(self.folder / "REDBOOLCamera"), "--check"])
            observations.append(True)
        calls, _ = self.run_build(thin("14.0.1"), extra=("--max-macos", "14.0.1"), assertions=check)
        self.assertEqual(len(calls), 2)
        self.assertEqual(observations, [True])

    def test_windows_build_remains_ungated_and_uses_original_exe_check(self):
        calls, _ = self.run_build(b"MZ synthetic Windows executable", system="Windows")
        self.assertEqual(len(calls), 2)
        self.assertEqual(calls[1], [str(self.folder / "REDBOOLCamera.exe"), "--check"])
        self.assertNotIn("macos_compatibility", json.loads((self.folder / "backend-build.json").read_text()))
        self.assertFalse((self.folder / "backend-macos-minimum.json").exists())


if __name__ == "__main__":
    unittest.main()
