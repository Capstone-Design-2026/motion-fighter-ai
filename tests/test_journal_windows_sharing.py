"""Bounded index publication under actual/simulated Windows sharing locks."""
import ctypes
import errno
import json
import os
from pathlib import Path
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

from redbool_demo import landmark_recording as recording


def locked_error(code=32):
    error = PermissionError(errno.EACCES, 'synthetic Windows sharing lock')
    error.winerror = code
    return error


class IndexSharingTests(unittest.TestCase):
    def test_transient_reader_lock_preserves_old_index_then_publishes_exact_new_bytes(self):
        with tempfile.TemporaryDirectory() as folder:
            destination = Path(folder) / recording.INDEX
            source = destination.with_suffix('.tmp')
            destination.write_bytes(b'old complete index'); source.write_bytes(b'new complete index')
            original = os.replace; calls = []
            def replace(a, b):
                calls.append((a, b))
                if len(calls) == 1:
                    self.assertEqual(destination.read_bytes(), b'old complete index')
                    raise locked_error(5)
                return original(a, b)
            with patch.object(recording.os, 'replace', side_effect=replace):
                recording._replace_index(source, destination)
            self.assertEqual(len(calls), 2)
            self.assertEqual(destination.read_bytes(), b'new complete index')
            self.assertFalse(source.exists())

    def test_persistent_lock_is_bounded_and_does_not_destroy_either_file(self):
        now = [0.]
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'new.tmp'; source.write_bytes(b'new')
            destination = Path(folder) / 'index.json'; destination.write_bytes(b'old')
            def sleep(seconds):
                now[0] += seconds
            with patch.object(recording.os, 'replace', side_effect=locked_error()) as replace, \
                    patch.object(recording.time, 'monotonic', side_effect=lambda: now[0]), \
                    patch.object(recording.time, 'sleep', side_effect=sleep), \
                    self.assertRaises(PermissionError):
                recording._replace_index(source, destination)
            self.assertLessEqual(replace.call_count, 26)
            self.assertAlmostEqual(now[0], .250)
            self.assertEqual(source.read_bytes(), b'new')
            self.assertEqual(destination.read_bytes(), b'old')

    def test_unrelated_errors_and_non_windows_permissions_are_not_retried(self):
        for error in (PermissionError(errno.EACCES, 'not a Windows sharing code'),
                      locked_error(112), OSError(errno.ENOSPC, 'full')):
            with self.subTest(error=error), patch.object(recording.os, 'replace', side_effect=error) as replace, \
                    patch.object(recording.time, 'sleep') as sleep, self.assertRaises(OSError):
                recording._replace_index('unused.tmp', 'unused.json')
            self.assertEqual(replace.call_count, 1)
            sleep.assert_not_called()

    def test_real_writer_recovers_short_index_conflict_without_missing_rows(self):
        original = os.replace
        injected = []
        def replace(a, b):
            if Path(b).name == recording.INDEX and Path(b).exists() and not injected:
                injected.append(True)
                raise locked_error(32)
            return original(a, b)
        with tempfile.TemporaryDirectory() as folder, patch.object(recording.os, 'replace', side_effect=replace):
            journal = recording.LandmarkJournal(Path(folder) / 'recognition', chunk_rows=1)
            try:
                for i in range(5):
                    self.assertTrue(journal.offer('header', i=i))
                health = journal.close()
                self.assertEqual(health['state'], 'complete', health)
                self.assertEqual([row['i'] for row in recording.iter_journal(journal.directory)], list(range(5)))
                index = json.loads((journal.directory / recording.INDEX).read_text())
                self.assertTrue(index['complete']); self.assertEqual(index['coverage']['gaps'], [])
                self.assertEqual(injected, [True])
            finally:
                journal.close()

    @unittest.skipUnless(os.name == 'nt', 'Windows native file-sharing semantics required')
    def test_native_reader_handle_blocks_replace_then_short_release_allows_publication(self):
        from ctypes import wintypes
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        create = kernel.CreateFileW
        create.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                           wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
        create.restype = wintypes.HANDLE
        close = kernel.CloseHandle; close.argtypes = (wintypes.HANDLE,); close.restype = wintypes.BOOL
        with tempfile.TemporaryDirectory() as folder:
            destination = Path(folder) / recording.INDEX; destination.write_bytes(b'old')
            source = Path(folder) / 'index.tmp'; source.write_bytes(b'new')
            # Permit readers/writers, deliberately omit FILE_SHARE_DELETE.
            handle = create(str(destination), 0x80000000, 0x1 | 0x2, None, 3, 0x80, None)
            self.assertNotEqual(handle, ctypes.c_void_p(-1).value)
            done = threading.Event(); failures = []
            def publish():
                try:
                    recording._replace_index(source, destination)
                except BaseException as exc:
                    failures.append(exc)
                finally:
                    done.set()
            worker = None
            try:
                # Positive control proves the original one-shot replace fails
                # under this same real Windows reader, without mocking the OS.
                with self.assertRaises(PermissionError) as blocked:
                    os.replace(source, destination)
                self.assertIn(blocked.exception.winerror, (5, 32, 33))
                worker = threading.Thread(target=publish); worker.start()
                self.assertFalse(done.wait(.025))
                self.assertTrue(close(handle)); handle = None
                self.assertTrue(done.wait(2), 'Publication did not finish after reader closed')
                self.assertEqual(failures, [])
                self.assertEqual(destination.read_bytes(), b'new')
            finally:
                if handle is not None:
                    close(handle)
                if worker is not None:
                    worker.join(2)


if __name__ == '__main__':
    unittest.main()
