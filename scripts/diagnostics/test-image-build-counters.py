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


def access_bytes():
    return b"""probe_version=1
target_pid=101
target_start_ticks=7001
observer_uid=0
target_uid=unavailable
root_stat_exit=0
root_stat_result=accessible
socket_stat_exit=1
socket_stat_result=permission_denied
output_write_exit=0
output_write_result=accessible
output_cleanup=removed
"""


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

    def exercise_capture(self, *, stale=False, failed=False, wrong_owner=False, timed_out=False, state_unavailable=False):
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
                        output = args[-1] + " " + ("f" * 32 if wrong_owner else "a" * 32)
                    elif "Running" in args[3]:
                        output, code = ("", 1) if state_unavailable else ("1 false false", 0)
                    else:
                        output = "0 false"
                elif args[1] == "exec":
                    output = f"counter_target pid=101 start_ticks={7002 if stale else 7001} inner_pid=7 uid=0"
                elif args[1] == "create":
                    output = ("d" if args[args.index("--entrypoint") + 1] == "sh" else "c") * 64
                elif args[1] != "rm":
                    raise AssertionError("Unexpected Docker operation")
                if kwargs.get("check") and code:
                    raise subprocess.CalledProcessError(code, ["private-not-emitted"])
                return subprocess.CompletedProcess(args, code, output, "")

            def collector(observer_id, seconds=12):
                calls.append(["docker", "start", "-a", observer_id])
                if observer_id == "d" * 64:
                    self.assertEqual(3, seconds)
                    return 0, access_bytes()
                (directory / "output" / "counters.csv").write_bytes(csv_bytes())
                if timed_out:
                    raise subprocess.TimeoutExpired([PRIVATE], 12, output=PRIVATE.encode())
                return (1, b"System.UnauthorizedAccessException: " + PRIVATE.encode()) if failed else (0, b"")

            with patch.object(counter.subprocess, "run", side_effect=docker), patch.object(counter, "run_collector", side_effect=collector):
                if wrong_owner:
                    with self.assertRaises(Exception):
                        counter.capture(directory, sample)
                    self.assertFalse(any(args[1] == "rm" for args in calls))
                    return
                result = counter.capture(directory, sample)
                self.assertEqual("unavailable" if stale or failed or timed_out else "observed", result["status"])
                if failed:
                    self.assertEqual("permission_denied_message", result["tool_output_category"])
                    self.assertEqual("command_exit", result["error_kind"])
                    self.assertEqual(1, result["command_exit"])
                    self.assertEqual({"status": "observed", "exit_code": 1, "oom_killed": False, "running": False}, result["collector_container"])
                if timed_out:
                    self.assertEqual("timeout", result["error_kind"])
                    self.assertEqual("unclassified", result["tool_output_category"])
                    self.assertEqual({"status": "unavailable"}, result["collector_container"])
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
                    self.assertEqual(["c" * 64] + (["d" * 64] if failed else []), removals)
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

    def test_collector_messages_reduce_to_fixed_categories_without_private_payloads(self):
        cases = [
            (b"System.UnauthorizedAccessException " + PRIVATE.encode(), "permission_denied_message"),
            (b"Failed to connect " + PRIVATE.encode(), "connection_or_missing_path_message"),
            (b"You must install or update .NET " + PRIVATE.encode(), "runtime_dependency_message"),
            (b"Unrecognized command " + PRIVATE.encode(), "cli_argument_message"),
            (PRIVATE.encode(), "unclassified"),
            (b"", "empty"),
            (b"x" * 16385, "unavailable_oversize"),
        ]
        for raw, expected in cases:
            with self.subTest(category=expected):
                self.assertEqual(expected, counter.classify_collector_output(raw))
                self.assertNotIn(PRIVATE, counter.classify_collector_output(raw))

    def test_timed_out_collector_and_unavailable_container_state_do_not_become_zero_or_success(self):
        self.exercise_capture(timed_out=True, state_unavailable=True)

    def test_collector_pipe_drains_noisy_output_with_bounded_retention_and_preserves_exit(self):
        real_popen = subprocess.Popen
        child = None

        def owned_child(_args, **kwargs):
            nonlocal child
            child = real_popen([sys.executable, "-c", "import sys;sys.stdout.buffer.write(b'x'*1048576);sys.stdout.flush();sys.exit(7)"], **kwargs)
            return child

        with patch.object(counter.subprocess, "Popen", side_effect=owned_child):
            status, raw = counter.run_collector("c" * 64)
        self.assertEqual(7, status)
        self.assertEqual(16385, len(raw))
        self.assertEqual("unavailable_oversize", counter.classify_collector_output(raw))
        self.assertEqual(7, child.poll())

    def test_collector_deadline_reaps_only_the_owned_client_and_caps_timeout_output(self):
        real_popen = subprocess.Popen
        child = None

        def owned_child(_args, **kwargs):
            nonlocal child
            child = real_popen([sys.executable, "-c", "import sys,time;sys.stdout.buffer.write(b'x'*1048576);sys.stdout.flush();time.sleep(30)"], **kwargs)
            return child

        with patch.object(counter.subprocess, "Popen", side_effect=owned_child), self.assertRaises(subprocess.TimeoutExpired) as failure:
            counter.run_collector("c" * 64)
        self.assertEqual(12, failure.exception.timeout)
        self.assertEqual(16385, len(failure.exception.output))
        self.assertIsNotNone(child.poll())

    def test_selector_initialization_cancellation_still_reaps_the_owned_client(self):
        real_popen = subprocess.Popen
        child = None

        def owned_child(_args, **kwargs):
            nonlocal child
            child = real_popen([sys.executable, "-c", "import time;time.sleep(30)"], **kwargs)
            return child

        try:
            with patch.object(counter.subprocess, "Popen", side_effect=owned_child), patch.object(counter.selectors, "DefaultSelector", side_effect=InterruptedError(PRIVATE)), self.assertRaises(InterruptedError):
                counter.run_collector("c" * 64)
            self.assertIsNotNone(child.poll())
        finally:
            # The deliberate missing-guard counterfactual must also leave no owned child behind.
            if child is not None and child.poll() is None:
                child.kill()
                child.wait(timeout=3)


    def test_access_protocol_requires_all_fields_and_closed_numeric_private_safe_values(self):
        raw = access_bytes()
        value = counter.parse_access_metadata(raw, 101, 7001)
        self.assertEqual({"exit_code": 1, "result": "permission_denied"}, value["socket_stat"])
        self.assertEqual({"status": "unavailable"}, value["target_uid"])
        self.assertEqual({"status": "observed", "value": 0}, value["observer_uid"])
        self.assertEqual("observed_metadata", value["status"])
        readonly = raw.replace(b"output_write_exit=0", b"output_write_exit=2").replace(b"output_write_result=accessible", b"output_write_result=read_only_filesystem").replace(b"output_cleanup=removed", b"output_cleanup=not_created")
        self.assertEqual({"exit_code": 2, "result": "read_only_filesystem"}, counter.parse_access_metadata(readonly, 101, 7001)["output_write"])
        for invalid in (b"", raw * 2, b"x" * 4097, raw.replace(b"socket_stat_result=permission_denied\n", b""),
                        raw.replace(b"socket_stat_exit=1", b"socket_stat_exit=0"), raw.replace(b"observer_uid=0", b"observer_uid=4294967296"),
                        raw.replace(b"socket_stat_exit=1", b"socket_stat_exit=256"), raw.replace(b"permission_denied", PRIVATE.encode()),
                        raw + b"private_path=" + PRIVATE.encode(), raw.replace(b"output_cleanup=removed", b"output_cleanup=not_created")):
            with self.subTest(size=len(invalid)), self.assertRaises(Exception):
                counter.parse_access_metadata(invalid, 101, 7001)
        with self.assertRaises(Exception):
            counter.parse_access_metadata(raw, 101, 7002)
        with self.assertRaises(Exception):
            counter.parse_access_metadata(raw.replace(b"output_write_exit=0", b"output_write_exit=00").replace(b"output_cleanup=removed", b"output_cleanup=not_created"), 101, 7001)

    def test_permission_failure_probe_is_once_only_and_keeps_original_error_and_exact_cleanup(self):
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)
            (directory / "bundle").mkdir()
            counter.save(directory, {"owner": "a" * 32, "builder_id": BUILDER, "stage": -1, "attempt": 0, "observer": None, "ready": True})
            sample = directory / "sample"
            sample.write_text("IMAGE_MEMORY_SAMPLE phase=periodic\nbuilder_process kind=csc pid=101 start_ticks=7001\nbuilder_process_VmRSS=524288\n")
            creates, removals, starts = [], [], []

            def docker(args, **kwargs):
                output = ""
                if args[1] == "create":
                    creates.append(args)
                    output = chr(ord("c") + len(creates) - 1) * 64
                elif args[1] == "inspect":
                    if args[-1] == BUILDER: output = BUILDER + " true"
                    elif "Labels" in args[3]: output = args[-1] + " " + "a" * 32
                    elif "Running" in args[3]: output = "1 false false"
                    else: output = "0 false"
                elif args[1] == "exec": output = "counter_target pid=101 start_ticks=7001 inner_pid=7 uid=0"
                elif args[1] == "rm": removals.append(args[-1])
                else: raise AssertionError("unexpected command")
                return subprocess.CompletedProcess(args, 0, output, "")

            def collect(cid, seconds=12):
                starts.append((cid, seconds))
                # Attempt state must be durable before starting the extra observer.
                if cid == "d" * 64:
                    self.assertTrue(counter.load(directory)["access_probe_attempted"])
                    return 0, access_bytes()
                return 1, b"Permission denied " + PRIVATE.encode()

            with patch.object(counter.subprocess, "run", side_effect=docker), patch.object(counter, "run_collector", side_effect=collect):
                first = counter.capture(directory, sample)
                sample.write_text(sample.read_text().replace("524288", "4194304"))
                second = counter.capture(directory, sample)
            self.assertEqual("unavailable", first["status"])
            self.assertEqual("collector_execution", first["reason"])
            self.assertEqual(1, first["command_exit"])
            self.assertEqual({"status": "observed", "exit_code": 1, "oom_killed": False, "running": False}, first["collector_container"])
            self.assertEqual("observed_metadata", first["access_probe"]["status"])
            self.assertNotIn("access_probe", second)
            self.assertEqual([("c" * 64, 12), ("d" * 64, 3), ("e" * 64, 12)], starts)
            self.assertEqual(["c" * 64, "d" * 64, "e" * 64], removals)
            self.assertEqual(["dotnet", "sh", "dotnet"], [x[x.index("--entrypoint") + 1] for x in creates])
            for args in creates:
                self.assertIn("--read-only", args)
                self.assertEqual("none", args[args.index("--network") + 1])
                self.assertIn("container:" + BUILDER, args)
                self.assertFalse(any(x in args for x in ("--privileged", "--cap-add", "--env")))
            self.assertNotIn(PRIVATE, json.dumps(first))

    def test_access_probe_unknown_create_keeps_its_name_and_blocks_later_captures(self):
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)
            (directory / "output").mkdir()
            state = {"owner": "a" * 32, "builder_id": BUILDER, "stage": 0, "attempt": 1, "observer": None, "ready": True}
            counter.save(directory, state)
            def command(args, **kwargs):
                if args[1] == "exec": return "counter_target pid=101 start_ticks=7001 inner_pid=7 uid=0"
                if args[1] == "create": raise subprocess.TimeoutExpired([PRIVATE], 3)
                if args[1] == "ps": return ""
                raise AssertionError("unexpected command")
            absent = subprocess.CompletedProcess([], 1, "", "")
            with patch.object(counter, "run", side_effect=command), patch.object(counter.subprocess, "run", return_value=absent), self.assertRaises(ValueError):
                counter.observe_access_failure(directory, state, 101, 7001, 7)
            saved = counter.load(directory)
            self.assertTrue(saved["access_probe_attempted"])
            self.assertTrue(saved["create_pending"])
            self.assertEqual("auditsphere-counter-" + "a" * 32 + "-1", saved["observer"])
            self.assertIsNone(counter.capture(directory, directory / "missing-sample"))

    def test_access_nonzero_partial_or_timed_out_output_is_unavailable_without_raw_details(self):
        for outcome in ["nonzero", "partial", "timeout"]:
            with self.subTest(outcome=outcome), tempfile.TemporaryDirectory() as root:
                directory = Path(root)
                (directory / "output").mkdir()
                state = {"owner": "a" * 32, "builder_id": BUILDER, "stage": 0, "attempt": 1, "observer": None, "ready": True}
                counter.save(directory, state)
                removals = []
                def command(args, **kwargs):
                    if args[1] == "exec": return "counter_target pid=101 start_ticks=7001 inner_pid=7 uid=0"
                    if args[1] == "create": return "d" * 64
                    if args[1] == "inspect": return "0 false"
                    raise AssertionError("unexpected command")
                def cleanup_command(args, **kwargs):
                    if args[1] == "inspect": return subprocess.CompletedProcess(args, 0, "d" * 64 + " " + "a" * 32, "")
                    raise AssertionError("unexpected cleanup")
                def run_command(args, **kwargs):
                    if args[1] == "rm": removals.append(args[-1]); return ""
                    return command(args, **kwargs)
                def collect(_cid, seconds):
                    self.assertEqual(3, seconds)
                    if outcome == "timeout": raise subprocess.TimeoutExpired([PRIVATE], 3, output=PRIVATE.encode())
                    return (7 if outcome == "nonzero" else 0), PRIVATE.encode()
                with patch.object(counter, "run", side_effect=run_command), patch.object(counter.subprocess, "run", side_effect=cleanup_command), patch.object(counter, "run_collector", side_effect=collect):
                    result = counter.observe_access_failure(directory, state, 101, 7001, 7)
                self.assertEqual("unavailable", result["status"])
                self.assertEqual("metadata_validation" if outcome == "partial" else "probe_execution", result["reason"])
                if outcome == "nonzero": self.assertEqual(7, result["command_exit"])
                if outcome == "timeout": self.assertEqual("timeout", result["error_kind"])
                self.assertEqual(["d" * 64], removals)
                self.assertNotIn(PRIVATE, json.dumps(result))

    def test_access_client_three_second_bound_reaps_its_owned_process(self):
        real_popen = subprocess.Popen
        child = None
        def owned_child(_args, **kwargs):
            nonlocal child
            child = real_popen([sys.executable, "-c", "import time;time.sleep(30)"], **kwargs)
            return child
        with patch.object(counter.subprocess, "Popen", side_effect=owned_child), self.assertRaises(subprocess.TimeoutExpired) as failure:
            counter.run_collector("d" * 64, seconds=3)
        self.assertEqual(3, failure.exception.timeout)
        self.assertIsNotNone(child.poll())

    def test_cancellation_during_collector_state_read_does_not_start_access_probe(self):
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)
            (directory / "bundle").mkdir()
            counter.save(directory, {"owner": "a" * 32, "builder_id": BUILDER, "stage": -1, "attempt": 0, "observer": None, "ready": True})
            sample = directory / "sample"
            sample.write_text("IMAGE_MEMORY_SAMPLE phase=periodic\nbuilder_process kind=csc pid=101 start_ticks=7001\nbuilder_process_VmRSS=524288\n")
            creates, removals = [], []
            def docker(args, **kwargs):
                output = ""
                if args[1] == "create": creates.append(args); output = "c" * 64
                elif args[1] == "exec": output = "counter_target pid=101 start_ticks=7001 inner_pid=7 uid=0"
                elif args[1] == "inspect":
                    if args[-1] == BUILDER: output = BUILDER + " true"
                    elif "Labels" in args[3]: output = "c" * 64 + " " + "a" * 32
                    elif "Running" in args[3]: raise InterruptedError(PRIVATE)
                elif args[1] == "rm": removals.append(args[-1])
                else: raise AssertionError("unexpected command")
                return subprocess.CompletedProcess(args, 0, output, "")
            with patch.object(counter.subprocess, "run", side_effect=docker), patch.object(counter, "run_collector", return_value=(1, b"Permission denied")), self.assertRaises(InterruptedError):
                counter.capture(directory, sample)
            self.assertEqual(1, len(creates))
            self.assertEqual("dotnet", creates[0][creates[0].index("--entrypoint") + 1])
            self.assertEqual(["c" * 64], removals)
            self.assertFalse(counter.load(directory).get("access_probe_attempted", False))

    def test_access_shell_exclusively_creates_and_preserves_existing_nonregular_entries(self):
        # Filesystem/stdin adapter changes only the two fixed roots; actual shipping shell logic runs.
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root)
            proc = directory / "proc"
            output = directory / "output"
            (proc / "101").mkdir(parents=True)
            (proc / "self").mkdir()
            output.mkdir()
            (proc / "101" / "stat").write_text("101 (csc) " + " ".join(["S"] + ["0"] * 18 + ["7001"]) + "\n")
            (proc / "101" / "status").write_text("Uid:\t0\t0\t0\t0\n")
            (proc / "self" / "status").write_text("Uid:\t0\t0\t0\t0\n")
            existing = output / "counter-access-probe"
            existing.symlink_to("/dev/null")
            fifo = output / "counter-access-probe.AAAAAA"
            import os
            os.mkfifo(fifo)
            script = Path(counter.__file__).with_name("image-build-counter-access.sh").read_text()
            adapted = script.replace("/proc/", str(proc) + "/").replace("/output/", str(output) + "/")
            result = subprocess.run(["sh", "-s", "--", "101", "7001", "7"], input=adapted, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=3)
            self.assertEqual(0, result.returncode)
            observed = counter.parse_access_metadata(result.stdout.encode(), 101, 7001)
            self.assertEqual({"exit_code": 0, "result": "accessible"}, observed["output_write"])
            self.assertEqual("removed", observed["output_cleanup"])
            self.assertTrue(existing.is_symlink())
            self.assertTrue(fifo.exists())
            self.assertEqual({existing.name, fifo.name}, {p.name for p in output.iterdir()})


if __name__ == "__main__":
    unittest.main()
