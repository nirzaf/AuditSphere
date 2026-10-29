#!/usr/bin/env python3
"""Focused protocol/privacy/ownership checks; Docker and the network are not required."""
import csv
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import contextlib
import urllib.error

source = Path(sys.argv.pop(1)) if len(sys.argv) > 1 else Path(__file__).with_name("image-build-counters.py")
spec = importlib.util.spec_from_file_location("image_counters", source)
counter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(counter)
PRIVATE = "private-command-and-environment-must-not-be-emitted"
BUILDER = "b" * 64


def csv_bytes():
    # An independently specified two-frame runtime export, in the pinned CLI's public units.
    rows = []
    for second in (1, 2):
        timestamp = f"10/29/2026 12:00:0{second}"
        for metric in ("dotnet.gc.last_collection.heap.size", "dotnet.gc.last_collection.heap.fragmentation.size"):
            for generation, value in zip(("gen0", "gen1", "gen2", "loh", "poh"), (0, 0, 1024, 33554432, 0)):
                rows.append([timestamp, "System.Runtime", f"{metric} (By)[gc.heap.generation={generation}]", "Metric", str(value if metric.endswith("heap.size") else 0)])
        for name, kind, value in (
            ("dotnet.process.memory.working_set (By)", "Metric", "80000000"),
            ("dotnet.gc.last_collection.memory.committed_size (By)", "Metric", "34000000"),
            ("dotnet.process.cpu.count ({cpu})", "Metric", "4"),
            ("dotnet.gc.heap.total_allocated (By / 1 sec)", "Rate", "2048"),
            ("dotnet.gc.collections ({collection} / 1 sec)[gc.heap.generation=gen2]", "Rate", "0"),
            ("dotnet.gc.pause.time (s / 1 sec)", "Rate", "0.001"),
        ):
            rows.append([timestamp, "System.Runtime", name, kind, value])
    output = io.StringIO()
    writer = csv.writer(output)
    writer.writerow(["Timestamp", "Provider", "Counter Name", "Counter Type", "Mean/Increment"])
    writer.writerows(rows)
    return output.getvalue().encode()


class CounterTests(unittest.TestCase):
    def test_export_values_and_rate_semantics(self):
        result = counter.parse_counters(csv_bytes())
        self.assertEqual("observed", result["status"])
        self.assertEqual(2, result["complete_sample_count"])
        self.assertEqual(33555456, result["samples"][0]["observed_generation_sum_bytes"])
        self.assertEqual({"unit": "By / 1 sec", "kind": "Rate"}, result["series_schema"]["dotnet.gc.heap.total_allocated"])

    def test_incomplete_private_nonfinite_and_oversize_are_unavailable(self):
        valid = csv_bytes()
        missing = b"\r\n".join(line for line in valid.split(b"\r\n") if b"heap.size (By)[gc.heap.generation=gen2]" not in line)
        for raw in (b"", b"x" * 65537, valid.replace(b"System.Runtime", PRIVATE.encode()), valid.replace(b"80000000", b"NaN"), missing, valid.replace(b"By / 1 sec", b"By")):
            with self.subTest(raw_size=len(raw)), self.assertRaises(Exception):
                counter.parse_counters(raw)

    def test_current_compiler_only_and_growth_stage_is_not_retried(self):
        old = "IMAGE_MEMORY_SAMPLE phase=start\nbuilder_process kind=csc pid=10 start_ticks=20\nbuilder_process_VmRSS=15000000\n"
        current = "IMAGE_MEMORY_SAMPLE phase=periodic\nbuilder_process kind=dotnet pid=30 start_ticks=40\nbuilder_process_VmRSS=15000000\n"
        self.assertIsNone(counter.select_target(old + current, -1))
        current += "builder_process kind=csc pid=101 start_ticks=7001\nbuilder_process_VmRSS=5000000\n"
        self.assertEqual((1, 5000000, 101, 7001), counter.select_target(old + current, -1))
        self.assertIsNone(counter.select_target(current, 1))

    def test_package_hash_and_unsafe_archive_refusal(self):
        with tempfile.TemporaryDirectory() as root:
            destination = Path(root) / "bundle"
            destination.mkdir()
            with self.assertRaises(Exception):
                counter.extract_bundle(b"untrusted", destination)
            def bad_archive(padding):
                stream = io.BytesIO()
                with zipfile.ZipFile(stream, "w") as archive:
                    for name in ("dotnet-counters.dll", "dotnet-counters.deps.json", "dotnet-counters.runtimeconfig.json"):
                        archive.writestr("tools/net8.0/any/" + name, "data")
                    for i in range(47):
                        archive.writestr("tools/net8.0/any/file" + str(i), "x" * padding if i == 0 else "data")
                    archive.writestr("tools/net8.0/any/../escape", PRIVATE)
                return stream.getvalue()
            # Hash substitution is local to this test so traversal validation is actually reached.
            raw = bad_archive(6689943 - len(bad_archive(0)))
            self.assertEqual(6689943, len(raw))
            with patch.object(counter, "PACKAGE_SHA256", hashlib.sha256(raw).hexdigest()), self.assertRaises(ValueError):
                counter.extract_bundle(raw, destination)
            self.assertEqual([], list(Path(root).rglob("*"))[1:])

    def exercise_capture(self, *, stale=False, failed=False, wrong_owner=False):
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root) / "owned"
            directory.mkdir()
            (directory / "bundle").mkdir()
            state = {"owner": "a" * 32, "builder_id": BUILDER, "stage": -1, "attempt": 0, "observer": None, "ready": True}
            counter.save(directory, state)
            sample = Path(root) / "sample"
            sample.write_text("IMAGE_MEMORY_SAMPLE phase=periodic\nbuilder_process kind=csc pid=101 start_ticks=7001\nbuilder_process_VmRSS=524288\n")
            calls = []

            def docker(args, **kwargs):
                calls.append(args)
                output, code = "", 0
                if args[1] == "inspect":
                    if args[-1] == BUILDER:
                        output = BUILDER + " true"
                    elif "Labels" in args[3]:
                        output = "c" * 64 + " " + ("f" * 32 if wrong_owner else "a" * 32)
                    else:
                        output = "0 false"
                elif args[1] == "exec":
                    output = f"counter_target pid=101 start_ticks={7002 if stale else 7001} inner_pid=7 uid=0"
                elif args[1] == "create":
                    output = "c" * 64
                elif args[1] == "start":
                    code = 1 if failed else 0
                    (directory / "output" / "counters.csv").write_bytes(csv_bytes())
                elif args[1] != "rm":
                    raise AssertionError("Unexpected Docker operation")
                if kwargs.get("check") and code:
                    raise subprocess.CalledProcessError(code, ["private-not-emitted"])
                return subprocess.CompletedProcess(args, code, output, "")

            with patch.object(counter.subprocess, "run", side_effect=docker):
                if wrong_owner:
                    with self.assertRaises(Exception):
                        counter.capture(directory, sample)
                    self.assertFalse(any(args[1] == "rm" for args in calls))
                    return
                result = counter.capture(directory, sample)
                self.assertEqual("unavailable" if stale or failed else "observed", result["status"])
                self.assertEqual(1, counter.load(directory)["attempt"])
                self.assertIsNone(counter.capture(directory, sample))
                exhausted = counter.load(directory)
                exhausted["attempt"] = 4
                counter.save(directory, exhausted)
                self.assertIsNone(counter.capture(directory, sample))
                self.assertFalse((directory / "output" / "counters.csv").exists())
                if stale:
                    self.assertFalse(any(args[1] == "create" for args in calls))
                else:
                    create = next(args for args in calls if args[1] == "create")
                    self.assertIn("container:" + BUILDER, create)
                    self.assertIn("--resume-runtime:false", create)
                    self.assertIn("--read-only", create)
                    self.assertEqual("none", create[create.index("--network") + 1])
                    self.assertNotIn("--privileged", create)
                    self.assertNotIn("--env", create)
                    self.assertIn("/proc/101/root/tmp/dotnet-diagnostic-7-7001-socket,connect", create)
                    removals = [args[-1] for args in calls if args[1] == "rm"]
                    self.assertEqual(["c" * 64], removals)
                self.assertNotIn(PRIVATE, json.dumps(result))
                counter.cleanup(directory)
                self.assertFalse(directory.exists())

    def test_collected_output_uses_private_observer_and_owned_cleanup(self):
        self.exercise_capture()

    def test_stale_identity_and_collector_failure_remain_unavailable(self):
        self.exercise_capture(stale=True)
        self.exercise_capture(failed=True)

    def test_cleanup_refuses_different_owner(self):
        self.exercise_capture(wrong_owner=True)

    def test_pending_create_absence_does_not_discard_its_identity(self):
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)
            state = {"owner": "a" * 32, "builder_id": BUILDER, "attempt": 1, "observer": "auditsphere-counter-" + "a" * 32 + "-1", "observer_id": None, "create_pending": True}
            counter.save(directory, state)
            responses = [subprocess.CompletedProcess([], 1, "", ""), subprocess.CompletedProcess([], 0, "", "")]
            with patch.object(counter.subprocess, "run", side_effect=responses), self.assertRaises(ValueError):
                counter.remove_observer(directory, state)
            self.assertEqual("auditsphere-counter-" + "a" * 32 + "-1", counter.load(directory)["observer"])
            self.assertTrue(counter.load(directory)["create_pending"])

    def test_preparation_failure_reports_fixed_phase_and_numeric_status_without_private_data(self):
        cases = [
            ("builder_identity", counter.DiagnosticCommandFailure(125), None, None, "command_exit", {"command_exit": 125}),
            ("tool_download", None, urllib.error.HTTPError(PRIVATE, 403, PRIVATE, {}, None), None, "http_error", {"http_status": 403}),
            ("tool_bundle", None, None, False, "validation_or_other", {}),
            ("runtime_pull", counter.DiagnosticCommandFailure(125), None, True, "command_exit", {"command_exit": 125}),
        ]
        for phase, command_error, download_error, skip_bundle, kind, numeric in cases:
            with self.subTest(phase=phase), tempfile.TemporaryDirectory() as root:
                output = io.StringIO()
                calls = 0

                def command(_args, **_kwargs):
                    nonlocal calls
                    calls += 1
                    if phase == "builder_identity" or calls == 2:
                        raise command_error
                    return BUILDER

                with contextlib.ExitStack() as stack:
                    stack.enter_context(patch.object(counter, "run", side_effect=command))
                    stack.enter_context(patch.object(counter.urllib.request, "urlopen", side_effect=download_error) if download_error else patch.object(counter.urllib.request, "urlopen", return_value=io.BytesIO(b"invalid-package")))
                    if skip_bundle:
                        stack.enter_context(patch.object(counter, "extract_bundle"))
                    stack.enter_context(patch.object(counter.signal, "signal"))
                    stack.enter_context(patch.object(sys, "argv", [str(source), "prepare", str(Path(root) / "owned"), "builder_fixture"]))
                    stack.enter_context(contextlib.redirect_stdout(output))
                    code = counter.main()
                self.assertEqual(1, code)
                self.assertNotIn(PRIVATE, output.getvalue())
                expected = {"status": "unavailable", "reason": "preparation_failed", "phase": phase, "error_kind": kind, **numeric}
                self.assertEqual(expected, json.loads(output.getvalue().removeprefix("IMAGE_MANAGED_COUNTERS ")))


if __name__ == "__main__":
    unittest.main()
