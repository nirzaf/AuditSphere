#!/usr/bin/env python3
"""Optional, bounded compiler counters; no target configuration or build-policy changes."""
import csv
import datetime as dt
import decimal
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import selectors
import shutil
import signal
import ssl
import subprocess
import sys
import time
import urllib.request
import urllib.error
import uuid
import zipfile

PACKAGE_URL = "https://api.nuget.org/v3-flatcontainer/dotnet-counters/10.0.750501/dotnet-counters.10.0.750501.nupkg"
PACKAGE_SHA256 = "5b4e39b182b5d4e23ef4501101081b6adfa3ca8f9368cbf1c084b8eb7cba3090"
OBSERVER_IMAGE = "mcr.microsoft.com/dotnet/runtime@sha256:d399699ebc8a27ab34665707ae6dc8f77ae478bd319444841a33a2b3840c5c9a"
OWNER_LABEL = "io.auditsphere.diagnostic.owner"
SPECS = {
    "dotnet.gc.last_collection.heap.fragmentation.size": ("By", "Metric", {"gen0", "gen1", "gen2", "loh", "poh"}),
    "dotnet.gc.last_collection.heap.size": ("By", "Metric", {"gen0", "gen1", "gen2", "loh", "poh"}),
    "dotnet.gc.last_collection.memory.committed_size": ("By", "Metric", set()),
    "dotnet.process.memory.working_set": ("By", "Metric", set()),
    "dotnet.process.cpu.count": ("{cpu}", "Metric", set()),
    "dotnet.gc.heap.total_allocated": ("By / 1 sec", "Rate", set()),
    "dotnet.gc.collections": ("{collection} / 1 sec", "Rate", {"gen0", "gen1", "gen2"}),
    "dotnet.gc.pause.time": ("s / 1 sec", "Rate", set()),
}
COUNTERS = "System.Runtime[" + ",".join(SPECS) + "]"


class DiagnosticCommandFailure(ValueError):
    def __init__(self, exit_code):
        super().__init__("unavailable")
        self.exit_code = exit_code


class PreparationUnavailable(ValueError):
    def __init__(self, phase, error):
        super().__init__("unavailable")
        self.details = {"status": "unavailable", "reason": "preparation_failed", "phase": phase}
        if isinstance(error, DiagnosticCommandFailure):
            self.details.update(error_kind="command_exit", command_exit=error.exit_code)
        elif isinstance(error, urllib.error.HTTPError):
            self.details.update(error_kind="http_error", http_status=error.code)
        elif isinstance(error, (subprocess.TimeoutExpired, TimeoutError)):
            self.details["error_kind"] = "timeout"
        elif isinstance(error, ssl.SSLError):
            self.details["error_kind"] = "tls_error"
        elif isinstance(error, urllib.error.URLError):
            self.details["error_kind"] = "url_error"
        elif isinstance(error, FileNotFoundError):
            self.details["error_kind"] = "required_file_or_command_missing"
        elif isinstance(error, OSError):
            self.details["error_kind"] = "io_error"
        else:
            self.details["error_kind"] = "validation_or_other"


def require(condition):
    if not condition:
        raise ValueError("unavailable")


def parse_counters(raw):
    require(len(raw) <= 65536)
    rows = csv.DictReader(io.StringIO(raw.decode("utf-8-sig")))
    require(rows.fieldnames == ["Timestamp", "Provider", "Counter Name", "Counter Type", "Mean/Increment"])
    frames, series, count = {}, set(), 0
    for row in rows:
        count += 1
        require(count <= 512 and set(row) == set(rows.fieldnames) and row["Provider"] == "System.Runtime")
        match = re.fullmatch(r"(dotnet\.[a-z0-9_.]+) \(([^)]+)\)(?:\[gc.heap.generation=(gen0|gen1|gen2|loh|poh)\])?", row["Counter Name"])
        require(match is not None and match[1] in SPECS)
        metric, generation = match[1], match[3]
        unit, kind, generations = SPECS[metric]
        require(match[2] == unit and row["Counter Type"] == kind)
        require(generation in generations if generations else generation is None)
        value = decimal.Decimal(row["Mean/Increment"])
        require(value.is_finite() and 0 <= value < decimal.Decimal("1e18"))
        timestamp = dt.datetime.strptime(row["Timestamp"], "%m/%d/%Y %H:%M:%S").isoformat()
        key = metric + ("/" + generation if generation else "")
        series.add(key)
        require(len(series) <= 32)
        frame = frames.setdefault(timestamp, {})
        require(key not in frame)
        frame[key] = int(value) if value == value.to_integral_value() else float(value)
    heap_keys = {"dotnet.gc.last_collection.heap.size/" + g for g in ("gen0", "gen1", "gen2", "loh", "poh")}
    required = heap_keys | {"dotnet.gc.last_collection.heap.fragmentation.size/" + g for g in ("gen0", "gen1", "gen2", "loh", "poh")} | {"dotnet.gc.last_collection.memory.committed_size", "dotnet.process.memory.working_set", "dotnet.process.cpu.count"}
    complete = [{"tool_local_timestamp": stamp, "values": values, "observed_generation_sum_bytes": sum(values[k] for k in heap_keys)} for stamp, values in sorted(frames.items()) if required.issubset(values)]
    require(len(complete) >= 2)
    return {
        "status": "observed", "row_count": count, "complete_sample_count": len(complete),
        "partial_samples_rejected": len(frames) - len(complete),
        "series_schema": {key: {"unit": SPECS[key.split("/", 1)[0]][0], "kind": SPECS[key.split("/", 1)[0]][1]} for key in sorted(series)},
        "samples": complete,
        "limits": "Exported timestamp groups are not atomic GC snapshots or a managed/native RSS decomposition. Rate values are per second, not cumulative totals. Missing fields are never zero-filled. Independent maxima are not additive.",
    }


def extract_bundle(raw, destination):
    require(len(raw) == 6689943 and hashlib.sha256(raw).hexdigest() == PACKAGE_SHA256)
    prefix = "tools/net8.0/any/"
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = [entry for entry in archive.infolist() if entry.filename.startswith(prefix) and not entry.is_dir()]
        require(len(entries) == 51 and sum(e.file_size for e in entries) <= 64 * 1024 * 1024)
        names = set()
        for entry in entries:
            relative = PurePosixPath(entry.filename[len(prefix):])
            require(not relative.is_absolute() and ".." not in relative.parts and "\\" not in str(relative))
            require(str(relative) not in names and entry.file_size <= 32 * 1024 * 1024)
            require((entry.external_attr >> 16) & 0o170000 != 0o120000)
            names.add(str(relative))
        require({"dotnet-counters.dll", "dotnet-counters.deps.json", "dotnet-counters.runtimeconfig.json"}.issubset(names))
        for entry in entries:
            target = destination / entry.filename[len(prefix):]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read(entry))


def run(args, seconds=3, input_text=None):
    result = subprocess.run(args, input=input_text, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, timeout=seconds)
    if result.returncode != 0:
        raise DiagnosticCommandFailure(result.returncode)
    require(len(result.stdout) <= 16384)
    return result.stdout.strip()


def save(directory, state):
    temporary = directory / "state.new"
    temporary.write_text(json.dumps(state))
    temporary.replace(directory / "state.json")


def load(directory):
    require(directory.is_dir() and directory.stat().st_uid == os.getuid())
    state = json.loads((directory / "state.json").read_text())
    require(re.fullmatch(r"[a-f0-9]{32}", state["owner"]) is not None)
    require(re.fullmatch(r"[a-f0-9]{64}", state["builder_id"]) is not None)
    return state


def prepare(directory, builder):
    phase = "builder_identity"
    try:
        directory.mkdir(mode=0o700)
        builder_id = run(["docker", "inspect", "--format", "{{.Id}}", builder])
        require(re.fullmatch(r"[a-f0-9]{64}", builder_id) is not None)
        save(directory, {"owner": uuid.uuid4().hex, "builder_id": builder_id, "stage": -1, "attempt": 0, "observer": None, "observer_id": None, "create_pending": False, "ready": False})
        phase = "tool_download"
        started = time.monotonic()
        with urllib.request.urlopen(PACKAGE_URL, timeout=10) as response:
            raw = bytearray()
            while True:
                block = response.read(65536)
                if not block:
                    break
                raw.extend(block)
                require(len(raw) <= 6689943 and time.monotonic() - started < 20)
        phase = "tool_bundle"
        bundle = directory / "bundle"
        bundle.mkdir(mode=0o700)
        extract_bundle(bytes(raw), bundle)
        # Cache only this public diagnostic runtime; never alter the compiler filesystem.
        phase = "runtime_pull"
        run(["docker", "pull", "--platform", "linux/amd64", OBSERVER_IMAGE], seconds=30)
        phase = "preparation_state"
        state = load(directory)
        state["ready"] = True
        save(directory, state)
    except Exception as error:
        # Fixed phases and numeric status only; never echo exception messages or tool output.
        raise PreparationUnavailable(phase, error) from None
    return {"status": "ready", "tool_version": "10.0.750501", "package_sha256": PACKAGE_SHA256, "observer_image": OBSERVER_IMAGE}


def select_target(sample, stage):
    sample = sample.rsplit("IMAGE_MEMORY_SAMPLE ", 1)[-1]
    candidates = []
    for match in re.finditer(r"^builder_process kind=csc pid=([0-9]+) start_ticks=([0-9]+)\n(.*?)(?=^builder_process kind=|\Z)", sample, re.M | re.S):
        rss = re.search(r"^builder_process_VmRSS=([0-9]+)$", match[3], re.M)
        if rss:
            eligible = [n for n, threshold in enumerate((512 * 1024, 4 * 1024**2, 10 * 1024**2, 14 * 1024**2)) if int(rss[1]) >= threshold and n > stage]
            if eligible:
                candidates.append((max(eligible), int(rss[1]), int(match[1]), int(match[2])))
    return max(candidates) if candidates else None


def validate_target(builder_id, pid, start):
    helper = Path(__file__).with_name("image-build-counter-target.sh").read_text()
    output = run(["docker", "exec", "-i", builder_id, "sh", "-s", "--", str(pid), str(start)], input_text=helper)
    match = re.fullmatch(r"counter_target pid=([0-9]+) start_ticks=([0-9]+) inner_pid=([0-9]+) uid=([0-9]+)", output)
    require(match is not None and int(match[1]) == pid and int(match[2]) == start)
    return int(match[3])


def classify_collector_output(raw):
    # Raw tool messages remain private and are never included in the report.
    if len(raw) > 16384:
        return "unavailable_oversize"
    value = raw.lower()
    if not value.strip():
        return "empty"
    if any(marker in value for marker in (b"unauthorizedaccessexception", b"permission denied", b"access is denied")):
        return "permission_denied_message"
    if any(marker in value for marker in (b"could not connect", b"failed to connect", b"unable to connect", b"no such file or directory", b"does not exist")):
        return "connection_or_missing_path_message"
    if any(marker in value for marker in (b"you must install or update .net", b"libhostfxr", b"libicu")):
        return "runtime_dependency_message"
    if any(marker in value for marker in (b"unrecognized command", b"unrecognized option", b"required argument")):
        return "cli_argument_message"
    return "unclassified"


def observe_collector_state(observer_id):
    try:
        output = run(["docker", "inspect", "--format", "{{.State.ExitCode}} {{.State.OOMKilled}} {{.State.Running}}", observer_id])
        match = re.fullmatch(r"([0-9]{1,3}) (true|false) (true|false)", output)
        require(match is not None and int(match[1]) <= 255)
        return {"status": "observed", "exit_code": int(match[1]), "oom_killed": match[2] == "true", "running": match[3] == "true"}
    except InterruptedError:
        raise
    except Exception:
        return {"status": "unavailable"}


def run_collector(observer_id, seconds=12):
    args = ["docker", "start", "-a", observer_id]
    retained = bytearray()
    deadline = time.monotonic() + seconds
    process = None
    selector = None
    try:
        process = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        selector = selectors.DefaultSelector()
        selector.register(process.stdout, selectors.EVENT_READ)
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0 or not selector.select(remaining):
                raise subprocess.TimeoutExpired(args, seconds)
            chunk = os.read(process.stdout.fileno(), 4096)
            if not chunk:
                break
            # Drain excess so the tool cannot block on a full pipe; retain only the bounded prefix.
            retained.extend(chunk[:max(0, 16385 - len(retained))])
        status = process.wait(timeout=max(0, deadline - time.monotonic()))
        return status, bytes(retained)
    except subprocess.TimeoutExpired as error:
        error.output = bytes(retained)
        raise
    finally:
        try:
            if selector is not None:
                selector.close()
        finally:
            if process is not None:
                try:
                    process.stdout.close()
                finally:
                    if process.poll() is None:
                        process.kill()
                        process.wait(timeout=3)


def remove_observer(directory, state):
    name = state.get("observer")
    if name is None:
        return
    require(name == "auditsphere-counter-" + state["owner"] + "-" + str(state["attempt"]))
    # The exact owned name is saved before create, including an unknown create outcome.
    identity = state.get("observer_id") or name
    check = subprocess.run(["docker", "inspect", "--format", '{{.Id}} {{index .Config.Labels "' + OWNER_LABEL + '"}}', identity], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, timeout=3)
    if check.returncode == 0:
        match = re.fullmatch(r"([a-f0-9]{64}) ([a-f0-9]{32})", check.stdout.strip())
        require(match is not None and match[2] == state["owner"])
        require(state.get("observer_id") is None or state["observer_id"] == match[1])
        # Name reuse after inspect cannot redirect removal to another container.
        run(["docker", "rm", "-f", match[1]], seconds=3)
    else:
        # Daemon failure is not proof of absence; retain state for the final cleanup attempt.
        filter_value = "id=" + identity if state.get("observer_id") else "name=^/" + name + "$"
        require(run(["docker", "ps", "-a", "--filter", filter_value, "--format", "{{.ID}}"], seconds=3) == "")
        # A timed-out create may still complete after an empty listing. Preserve its exact name.
        require(not state.get("create_pending", False))
    state["observer"] = None
    state["observer_id"] = None
    state["create_pending"] = False
    save(directory, state)


def parse_access_metadata(raw, pid, start):
    require(0 < len(raw) <= 4096)
    fields = {}
    for line in raw.decode("ascii").splitlines():
        key, value = line.split("=", 1)
        require(key not in fields)
        fields[key] = value
    required = {"probe_version", "target_pid", "target_start_ticks", "observer_uid", "target_uid", "output_cleanup"}
    required |= {prefix + suffix for prefix in ("root_stat", "socket_stat", "output_write") for suffix in ("_exit", "_result")}
    require(set(fields) == required)
    require(fields["probe_version"] == "1" and fields["target_pid"] == str(pid) and fields["target_start_ticks"] == str(start))
    result = {"status": "observed_metadata", "target_pid": pid, "target_start_ticks": start}
    for key in ("observer_uid", "target_uid"):
        value = fields[key]
        require(value == "unavailable" or re.fullmatch(r"[0-9]{1,10}", value) is not None and int(value) <= 4294967295)
        result[key] = {"status": "unavailable"} if value == "unavailable" else {"status": "observed", "value": int(value)}
    for prefix in ("root_stat", "socket_stat", "output_write"):
        code, category = fields[prefix + "_exit"], fields[prefix + "_result"]
        require(re.fullmatch(r"[0-9]{1,3}", code) is not None and int(code) <= 255)
        require(category in {"accessible", "permission_denied", "missing", "read_only_filesystem", "other"})
        require((int(code) == 0) == (category == "accessible"))
        result[prefix] = {"exit_code": int(code), "result": category}
    cleanup = fields["output_cleanup"]
    require(cleanup in {"removed", "not_created", "unavailable"})
    require((int(fields["output_write_exit"]) != 0) == (cleanup == "not_created"))
    result["output_cleanup"] = cleanup
    return result


def observe_access_failure(directory, state, pid, start, inner):
    state["access_probe_attempted"] = True
    save(directory, state)  # One attempt, including cleanup/create failures and unknown outcomes.
    phase = "counter_cleanup"
    try:
        remove_observer(directory, state)
        phase = "target_validation"
        require(validate_target(state["builder_id"], pid, start) == inner)
        script = Path(__file__).with_name("image-build-counter-access.sh").read_text()
        name = "auditsphere-counter-" + state["owner"] + "-" + str(state["attempt"])
        state.update(observer=name, observer_id=None, create_pending=True)
        save(directory, state)
        phase = "observer_creation"
        observer_id = run(["docker", "create", "--platform", "linux/amd64", "--name", name, "--label", OWNER_LABEL + "=" + state["owner"],
             "--network", "none", "--read-only", "--pids-limit", "64", "--tmpfs", "/tmp:rw,noexec,nosuid,size=32m",
             "--pid", "container:" + state["builder_id"], "--mount", "type=bind,src=" + str((directory / "output").resolve()) + ",dst=/output",
             "--entrypoint", "sh", OBSERVER_IMAGE, "-c", script, "counter-access", str(pid), str(start), str(inner)], seconds=3)
        require(re.fullmatch(r"[a-f0-9]{64}", observer_id) is not None)
        state.update(observer_id=observer_id, create_pending=False)
        save(directory, state)
        require(validate_target(state["builder_id"], pid, start) == inner)
        phase = "probe_execution"
        status, raw = run_collector(observer_id, seconds=3)
        if status != 0:
            raise DiagnosticCommandFailure(status)
        phase = "target_final_validation"
        require(validate_target(state["builder_id"], pid, start) == inner)
        require(run(["docker", "inspect", "--format", "{{.State.ExitCode}} {{.State.OOMKilled}}", observer_id]) == "0 false")
        phase = "metadata_validation"
        return parse_access_metadata(raw, pid, start)
    except Exception as error:
        result = {"status": "unavailable", "reason": phase}
        if isinstance(error, DiagnosticCommandFailure):
            result.update(error_kind="command_exit", command_exit=error.exit_code)
        elif isinstance(error, subprocess.TimeoutExpired):
            result["error_kind"] = "timeout"
        return result
    finally:
        remove_observer(directory, state)


def capture(directory, sample_path):
    state = load(directory)
    require(state["ready"])
    if state["attempt"] >= 4 or state["observer"] is not None:
        return None
    with sample_path.open("rb") as sample_file:
        sample_file.seek(0, 2)
        size = sample_file.tell()
        sample_file.seek(max(0, size - 65536))
        target = select_target(sample_file.read().decode("utf-8"), state["stage"])
    if target is None:
        return None
    stage, rss, pid, start = target
    state.update(stage=stage, attempt=state["attempt"] + 1)
    save(directory, state)  # Unavailable attempts consume their growth stage too.
    identity = {"attempt": state["attempt"], "stage": stage, "pid": pid, "start_ticks": start, "sampled_rss_kib": rss}
    phase = "target_validation"
    collector_output_category = "not_observed"
    try:
        require(run(["docker", "inspect", "--format", "{{.Id}} {{.State.Running}}", state["builder_id"]]) == state["builder_id"] + " true")
        inner = validate_target(state["builder_id"], pid, start)
        output = directory / "output"
        output.mkdir(mode=0o700, exist_ok=True)
        csv_path = output / "counters.csv"
        csv_path.unlink(missing_ok=True)
        name = "auditsphere-counter-" + state["owner"] + "-" + str(state["attempt"])
        state["observer"] = name
        state["create_pending"] = True
        save(directory, state)
        phase = "observer_creation"
        observer_id = run(["docker", "create", "--platform", "linux/amd64", "--name", name, "--label", OWNER_LABEL + "=" + state["owner"],
             "--network", "none", "--read-only", "--pids-limit", "64", "--tmpfs", "/tmp:rw,noexec,nosuid,size=32m",
             "--pid", "container:" + state["builder_id"], "--mount", "type=bind,src=" + str((directory / "bundle").resolve()) + ",dst=/tools,readonly",
             "--mount", "type=bind,src=" + str(output.resolve()) + ",dst=/output", "--entrypoint", "dotnet", OBSERVER_IMAGE,
             "/tools/dotnet-counters.dll", "collect", "--diagnostic-port", f"/proc/{pid}/root/tmp/dotnet-diagnostic-{inner}-{start}-socket,connect",
             "--resume-runtime:false", "--duration", "00:00:05", "--refresh-interval", "1", "--maxTimeSeries", "32", "--maxHistograms", "1",
             "--counters", COUNTERS, "--format", "csv", "--output", "/output/counters.csv"], seconds=3)
        require(re.fullmatch(r"[a-f0-9]{64}", observer_id) is not None)
        state.update(observer_id=observer_id, create_pending=False)
        save(directory, state)
        # Recheck after creation and after collection; never label a stale PID as observed.
        require(validate_target(state["builder_id"], pid, start) == inner)
        phase = "collector_execution"
        status, diagnostic_output = run_collector(observer_id)
        collector_output_category = classify_collector_output(diagnostic_output)
        if status != 0:
            raise subprocess.CalledProcessError(status, ["docker", "start", "-a", observer_id])
        phase = "target_final_validation"
        require(validate_target(state["builder_id"], pid, start) == inner)
        require(run(["docker", "inspect", "--format", "{{.State.ExitCode}} {{.State.OOMKilled}}", observer_id]) == "0 false")
        phase = "counter_validation"
        with csv_path.open("rb") as counters_file:
            parsed = parse_counters(counters_file.read(65537))
        return {**identity, **parsed}
    except Exception as error:
        result = {**identity, "status": "unavailable", "reason": phase}
        if phase == "collector_execution":
            result["tool_output_category"] = collector_output_category
            result["collector_container"] = observe_collector_state(observer_id)
            if isinstance(error, subprocess.CalledProcessError):
                result.update(error_kind="command_exit", command_exit=error.returncode)
            elif isinstance(error, subprocess.TimeoutExpired):
                result["error_kind"] = "timeout"
                if error.output is not None:
                    result["tool_output_category"] = classify_collector_output(error.output)
            elif isinstance(error, OSError):
                result["error_kind"] = "io_error"
            else:
                result["error_kind"] = "validation_or_other"
            if result["tool_output_category"] == "permission_denied_message" and not state.get("access_probe_attempted", False):
                result["access_probe"] = observe_access_failure(directory, state, pid, start, inner)
        return result
    finally:
        remove_observer(directory, state)
        (directory / "output" / "counters.csv").unlink(missing_ok=True)


def cleanup(directory):
    if not (directory / "state.json").exists():
        # A failed builder identity probe can leave only our just-created empty directory.
        directory.rmdir()
        return
    state = load(directory)
    remove_observer(directory, state)
    shutil.rmtree(directory)


def main():
    # SIGTERM from the owning sampler unwinds capture cleanup rather than orphaning it.
    def interrupted(_signum, _frame):
        raise InterruptedError("unavailable")
    signal.signal(signal.SIGTERM, interrupted)
    action, directory = sys.argv[1], Path(sys.argv[2])
    try:
        if action == "prepare":
            result = prepare(directory, sys.argv[3])
        elif action == "capture":
            result = capture(directory, Path(sys.argv[3]))
        elif action == "cleanup":
            cleanup(directory)
            result = {"status": "cleaned"}
        else:
            raise ValueError("unavailable")
        if result is not None:
            print("IMAGE_MANAGED_COUNTERS " + json.dumps(result, separators=(",", ":")))
        return 0
    except PreparationUnavailable as error:
        print("IMAGE_MANAGED_COUNTERS " + json.dumps(error.details, separators=(",", ":")))
        return 1
    except Exception:
        # Never echo paths, provider payloads, process arguments or environment in errors.
        print('IMAGE_MANAGED_COUNTERS {"status":"unavailable","reason":"diagnostic_failed"}')
        return 1


if __name__ == "__main__":
    sys.exit(main())
