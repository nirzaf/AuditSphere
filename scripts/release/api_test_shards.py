#!/usr/bin/env python3
"""Complete API coverage through verified, sequential, fresh-testhost shards.

Containment by process exit, not a production rate-limiter fix. No test is omitted.
Uses only the SDK and xUnit assemblies already pinned/restored by the release job.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

MAX_ROWS = 24
MAX_COLLECTIONS = 4
RSS_LIMIT = 3 * 1024**3
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def write(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")


def digest(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def inventory(xunit, vstest):
    require(xunit and vstest, "Empty discovery")
    require(len({x["id"] for x in xunit}) == len(xunit), "Duplicate xUnit ID")
    require(len({x["id"] for x in vstest}) == len(vstest), "Duplicate VSTest ID")
    require(len({x["xunitId"] for x in vstest}) == len(vstest), "Duplicate adapter xUnit ID")
    mapped = {x["xunitId"]: x for x in vstest}
    require(set(mapped) == {x["id"] for x in xunit}, "Discovery sets differ")
    groups = defaultdict(list)
    rows = []
    for x in xunit:
        require(x["kind"] == "Xunit.Sdk.XunitTestCase" and x["skip"] is None,
                "Deferred, custom, error or skipped test case")
        v = mapped[x["id"]]
        require(all(v[k] == x[k] for k in ("className", "method", "display")), "Discovery metadata differs")
        require(v["fqn"] == x["className"] + "." + x["method"], "Unexpected method identity")
        require(all(isinstance(x[k], str) and x[k] for k in ("id", "collection", "className", "method", "display")), "Empty identity")
        row = {**v, "xunitId": x["id"]}
        groups[x["collection"]].append(row)
        rows.append(row)
    require(len({(r["className"], r["method"], r["display"]) for r in rows}) == len(rows), "Ambiguous TRX identity")
    # xUnit collection GUIDs are per-discovery values. Canonicalize actual membership,
    # never infer collections from class names or source attributes.
    class_groups = defaultdict(set)
    for members in groups.values():
        key = hashlib.sha256("\n".join(sorted(r["xunitId"] for r in members)).encode()).hexdigest()
        for row in members:
            row["collection"] = key
            class_groups[row["className"]].add(key)
    require(all(len(g) == 1 for g in class_groups.values()), "Class split across collections")
    return sorted(rows, key=lambda r: r["id"])


def partition(rows):
    groups = defaultdict(list)
    for row in rows:
        groups[row["collection"]].append(row["id"])
    shards, current, count = [], [], 0
    for key in sorted(groups):
        group = sorted(groups[key])
        require(0 < len(group) <= MAX_ROWS, "Collection exceeds bounded shard size; never split fixtures")
        if current and (len(current) + len(group) > MAX_ROWS or count == MAX_COLLECTIONS):
            shards.append(sorted(current))
            current, count = [], 0
        current.extend(group)
        count += 1
    if current:
        shards.append(sorted(current))
    require(shards and all(shards), "Empty shard assignment")
    require(Counter(i for s in shards for i in s) == Counter(r["id"] for r in rows), "Invalid exact partition")
    return shards


def reconcile(path, expected, rows):
    require(expected and len(set(expected)) == len(expected), "Empty/duplicate result assignment")
    root = ET.parse(path).getroot()
    results = root.findall("t:Results/t:UnitTestResult", NS)
    definitions = root.findall("t:TestDefinitions/t:UnitTest", NS)
    entries = root.findall("t:TestEntries/t:TestEntry", NS)
    for container, children in (("Results", results), ("TestDefinitions", definitions), ("TestEntries", entries)):
        nodes = root.findall("t:" + container, NS)
        require(len(nodes) == 1 and len(nodes[0]) == len(children), "Unexpected TRX structure")
    executions = [r.get("executionId") for r in results]
    require(all(executions) and len(set(executions)) == len(executions), "Missing/duplicate execution ID")
    require(Counter((e.get("testId"), e.get("executionId")) for e in entries) ==
            Counter((r.get("testId"), r.get("executionId")) for r in results), "TRX entries differ")
    require(Counter(r.get("testId") for r in results) == Counter(expected), "TRX result IDs differ")
    require(Counter(d.get("id") for d in definitions) == Counter(expected), "TRX definition IDs differ")
    mapped = {r["id"]: r for r in rows}
    definitions = {d.get("id"): d for d in definitions}
    for result in results:
        require(result.get("outcome") == "Passed", "Non-passing TRX outcome")
        definition = definitions[result.get("testId")]
        row = mapped[result.get("testId")]
        method = definition.find("t:TestMethod", NS)
        execution = definition.find("t:Execution", NS)
        require(method is not None and execution is not None, "Incomplete test definition")
        require(method.get("className") == row["className"] and method.get("name") == row["method"], "TRX method differs")
        require(result.get("testName") == row["display"] and definition.get("name") == row["display"], "TRX display differs")
        require(result.get("executionId") == execution.get("id"), "TRX execution differs")
    summaries = root.findall("t:ResultSummary", NS)
    require(len(summaries) == 1, "Ambiguous TRX summary")
    summary = summaries[0]
    require(summary is not None and summary.get("outcome") == "Completed", "TRX run not completed")
    counter_nodes = summary.findall("t:Counters", NS)
    require(len(counter_nodes) == 1, "Ambiguous TRX counters")
    counters = counter_nodes[0]
    require(counters is not None, "Missing counters")
    for key, value in counters.attrib.items():
        require(int(value) == (len(expected) if key in {"total", "executed", "passed"} else 0), "Inconsistent TRX counters")
    require(all(counters.get(k) == str(len(expected)) for k in ("total", "executed", "passed")), "Missing TRX counts")
    require(not root.findall(".//t:RunInfo[@outcome='Error']", NS), "TRX run error")
    events = json.loads(path.with_name("events.json").read_text())
    require(events["complete"] is True and events["errors"] == [] and type(events["total"]) is int and
            events["total"] == len(expected), "Runner aborted/error/incomplete")
    require(Counter(r["id"] for r in events["results"]) == Counter(expected), "Runner event IDs differ")
    require(all(r["outcome"] == "Passed" for r in events["results"]), "Runner event failed/skipped")



def retained_count(directory, repository, source_sha, run_id, run_attempt, *, expected_sdk, expected_target_framework):
    """Reconcile one downloaded API artifact using the same discovery/result owners.

    Only the canonical manifest-assigned shard paths contribute. Extra TRXs, partial
    coverage, stale source/build identity or inconsistent callbacks refuse aggregation.
    This reads evidence only; it never discovers or executes tests.
    """
    require(directory.is_dir() and not directory.is_symlink(), "Linked/missing API artifact root")
    output = directory / "api-shards"
    def read(name):
        path = output / name
        require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 32 * 1024**2,
                "Missing, linked or oversized shard evidence")
        return json.loads(path.read_text())

    require(output.is_dir() and not output.is_symlink(), "Missing canonical API shard evidence")
    require(not any(p.is_symlink() for p in directory.rglob("*")), "Linked API artifact evidence")
    coverage, source = read("coverage.json"), read("source.json")
    require(coverage["status"] == "PASSED" and coverage["source"] == source_sha, "Incomplete/stale API coverage")
    require(source["schemaVersion"] == 1 and source["repository"] == repository and
            source["resolvedCommit"] == source_sha and type(source["runId"]) is int and source["runId"] == run_id and
            type(source["runAttempt"]) is int and 1 <= source["runAttempt"] <= run_attempt and
            source["artifactName"] == f"release-verification-test-build-{source_sha}-{run_id}-api" and
            source["project"] == "tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj" and
            source["assembly"] == "AuditSphereOps.Api.Tests" and source["configuration"] == "Release" and
            source["targetFramework"] == expected_target_framework and source["operatingSystem"] == "Linux" and
            source["dotnetSdk"] == coverage["sdk"] == expected_sdk, "Unbound API build evidence")
    rows = inventory(read("xunit.json"), read("vstest.json"))
    assignments = partition(rows)
    require(rows == read("inventory.json") and assignments == read("assignments.json"), "Discovery/assignment evidence differs")
    expected_counts = {"discovered": len(rows), "collections": len({r["collection"] for r in rows}),
                       "concurrency": 1, "maxRowsPerShard": MAX_ROWS,
                       "maxCollectionsPerShard": MAX_COLLECTIONS, "rssLimitBytes": RSS_LIMIT}
    require(all(type(coverage[k]) is int and coverage[k] == v for k, v in expected_counts.items()),
            "Invalid API coverage counters/limits")
    require(isinstance(coverage["shards"], list) and len(coverage["shards"]) == len(assignments), "Incomplete shard list")
    expected_paths = {output / f"shard-{i:03}" / "results.trx" for i in range(len(assignments))}
    require(set(directory.rglob("*.trx")) == expected_paths, "Unexpected/missing API TRX paths")
    require({p.name for p in output.glob("shard-*") if p.is_dir()} ==
            {f"shard-{i:03}" for i in range(len(assignments))}, "Unexpected shard directories")
    elapsed = 0.0
    for index, ids in enumerate(assignments):
        shard = output / f"shard-{index:03}"
        require(not shard.is_symlink() and read(f"shard-{index:03}/ids.json") == ids, "Shard assignment differs")
        item = coverage["shards"][index]
        require(type(item["index"]) is int and item["index"] == index and
                type(item["passed"]) is int and item["passed"] == len(ids), "Shard counters differ")
        for name, key in (("results.trx", "trxSha256"), ("events.json", "eventsSha256")):
            path = shard / name
            require(path.is_file() and not path.is_symlink() and digest(path) == item[key], "Shard evidence digest differs")
        reconcile(shard / "results.trx", ids, rows)
        outcome = read(f"shard-{index:03}.exit.json")
        require(type(outcome["exit"]) is int and outcome["exit"] == 0 and outcome["reason"] is None and
                outcome["groupDrained"] is True and type(outcome["peakRssBytes"]) is int and
                0 <= outcome["peakRssBytes"] <= RSS_LIMIT, "Shard execution/cleanup evidence failed")
        seconds = outcome["seconds"]
        require(type(seconds) in (int, float) and math.isfinite(seconds) and 0 <= seconds <= 21 * 60 + 30,
                "Invalid shard elapsed time")
        elapsed += seconds
    # bounded() allows up to ten seconds to reap plus ten to drain; retain a
    # thirty-second cleanup/sampling margin, not an unbounded deadline exemption.
    require(elapsed <= 150 * 60 + 30, "Aggregate shard deadline exceeded")
    binary_hashes = read("binary-hashes.json")
    def sha256(value):
        return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)
    required = {"AuditSphereOps.Api.Tests" + suffix for suffix in (".dll", ".pdb", ".deps.json", ".runtimeconfig.json")} | {"xunit.runner.json"}
    require(isinstance(binary_hashes, dict) and required <= set(binary_hashes) and
            all(isinstance(k, str) and k and not Path(k).is_absolute() and ".." not in Path(k).parts and
                "\\" not in k and sha256(v) for k, v in binary_hashes.items()), "Invalid API binary manifest")
    require(digest(output / "binary-hashes.json") == coverage["binaryManifestSha256"], "Binary manifest digest differs")
    helpers = coverage["helpers"]
    require(isinstance(helpers, dict) and set(helpers) == {"Discover.cs", "VsDiscover.cs"}, "Missing retained helper identities")
    for name, expected in helpers.items():
        path = output / "helper-source" / name
        require(sha256(expected) and path.is_file() and digest(path) == expected, "Retained helper digest differs")
    for name, reason in (("deadline", "Deadline"), ("cancel", "Synthetic cancellation"), ("leader-exit", None)):
        outcome = read(f"supervisor-checks/{name}.exit.json")
        seconds = outcome["seconds"]
        require(type(seconds) in (int, float) and math.isfinite(seconds) and 0 <= seconds <= 35,
                "Invalid Linux self-check elapsed time")
        require(type(outcome["exit"]) is int and (outcome["exit"] == 0 if reason is None else outcome["exit"] != 0) and
                outcome["reason"] == reason and outcome["groupDrained"] is True, "Linux ownership self-check failed")
    return len(rows)


def group_state(pgid):
    """Linux runner only; count every member of our owned process group."""
    total, alive = 0, []
    for proc in Path("/proc").glob("[0-9]*"):
        try:
            fields = (proc / "stat").read_text().rsplit(")", 1)[1].split()
            if int(fields[2]) == pgid:
                total += int(fields[21]) * os.sysconf("SC_PAGE_SIZE")
                if fields[0] != "Z":
                    alive.append(int(proc.name))
        except (FileNotFoundError, ProcessLookupError):
            pass
    return total, alive


def bounded(command, log, timeout, env):
    require(Path("/proc/self/stat").exists(), "Bounded runner requires Linux /proc")
    start = time.monotonic()
    with log.open("w") as stream:
        stream.write(json.dumps(command) + "\n")
        stream.flush()
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, env=env, start_new_session=True)
        reason, peak, drained = None, 0, False
        try:
            while process.poll() is None:
                rss, _ = group_state(process.pid)
                peak = max(peak, rss)
                if peak > RSS_LIMIT or time.monotonic() - start > timeout:
                    reason = "RSS limit" if peak > RSS_LIMIT else "Deadline"
                    break
                time.sleep(0.25)
        except BaseException as error:
            reason = str(error) or type(error).__name__
            raise
        finally:
            # Only the session/process group created by this exact Popen is owned.
            # Kill even after leader exit to prevent inherited testhosts surviving.
            handlers = {sig: signal.signal(sig, signal.SIG_IGN) for sig in (signal.SIGTERM, signal.SIGINT)}
            try:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait(timeout=10)
                cleanup_deadline = time.monotonic() + 10
                while time.monotonic() < cleanup_deadline:
                    _, alive = group_state(process.pid)
                    if not alive:
                        drained = True
                        break
                    time.sleep(0.1)
            except BaseException as error:
                reason = f"{reason or ''} Cleanup: {error}"
                raise
            finally:
                write(log.with_suffix(".exit.json"), {"exit": process.returncode, "reason": reason, "peakRssBytes": peak,
                                                      "seconds": time.monotonic() - start, "processGroup": process.pid,
                                                      "groupDrained": drained})
                for sig, handler in handlers.items():
                    signal.signal(sig, handler)
        require(reason is None and process.returncode == 0 and drained,
                f"Command failed: {log.name} ({reason}, exit {process.returncode}, drained {drained})")


def verify_linux_supervisor(output):
    """Mandatory real-platform containment checks; no API fixtures or skipped fallback."""
    require(Path("/proc/self/stat").exists(), "Linux supervisor verification requires /proc")
    checks = output / "supervisor-checks"
    checks.mkdir()
    sleeper = [sys.executable, "-c", "import time; time.sleep(30)"]
    try:
        bounded(sleeper, checks / "deadline.log", 0.3, os.environ.copy())
        raise ValueError("Supervisor failed to enforce deadline")
    except ValueError:
        result = json.loads((checks / "deadline.exit.json").read_text())
        require(result["reason"] == "Deadline" and result["groupDrained"] is True and result["exit"] != 0,
                "Deadline containment self-check failed")
    def cancel(signum, frame):
        raise InterruptedError("Synthetic cancellation")
    old = signal.signal(signal.SIGALRM, cancel)
    signal.setitimer(signal.ITIMER_REAL, 0.3)
    try:
        try:
            bounded(sleeper, checks / "cancel.log", 5, os.environ.copy())
            raise ValueError("Supervisor failed cancellation")
        except InterruptedError:
            result = json.loads((checks / "cancel.exit.json").read_text())
            require(result["reason"] == "Synthetic cancellation" and result["groupDrained"] is True,
                    "Cancellation containment self-check failed")
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, old)
    bounded([sys.executable, "-c", "import os,time; pid=os.fork(); time.sleep(30) if pid == 0 else None"],
            checks / "leader-exit.log", 5, os.environ.copy())


def run(args):
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    manifest = {"status": "FAILED", "shards": [], "source": args.source}
    try:
        verify_linux_supervisor(output)
        require(subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip() == args.source, "Source SHA differs")
        require(not subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=no"], text=True).strip(), "Dirty tracked source")
        source = json.loads(args.provenance.read_text())
        require(source["resolvedCommit"] == args.source and str(source["runId"]) == os.environ["GITHUB_RUN_ID"] and
                source["assembly"] == "AuditSphereOps.Api.Tests", "Unbound artifact")
        shutil.copyfile(args.provenance, output / "source.json")
        dotnet = Path(shutil.which("dotnet")).resolve()
        version = json.loads(Path("global.json").read_text())["sdk"]["version"]
        require(subprocess.check_output([str(dotnet), "--version"], text=True).strip() == version, "Wrong SDK")
        sdk = dotnet.parent / "sdk" / version
        binary = args.assembly.resolve().parent
        config = json.loads((binary / "xunit.runner.json").read_text())
        require(config == {"$schema": "https://xunit.net/schema/current/xunit.runner.schema.json", "diagnosticMessages": False,
                           "parallelizeAssembly": False, "parallelizeTestCollections": True, "maxParallelThreads": 2}, "Runner config changed; review discovery compatibility")
        hashes = {str(p.relative_to(binary)): digest(p) for p in sorted(binary.rglob("*")) if p.is_file()}
        write(output / "binary-hashes.json", hashes)
        manifest["binaryManifestSha256"] = digest(output / "binary-hashes.json")
        manifest["sdk"] = version
        manifest["runnerHashes"] = {p.name: digest(p) for p in sdk.glob("*TestPlatform*.dll")}
        manifest["dotnetHostSha256"] = digest(dotnet)
        env = {**os.environ, "DOTNET_ROOT": str(dotnet.parent)}
        def command(cmd, name, timeout=240):
            bounded(cmd, output / (name + ".log"), timeout, env)
        # SDK targeting pack chosen by its bundled framework version, not newest installed.
        runtime = json.loads((sdk / "dotnet.runtimeconfig.json").read_text())["runtimeOptions"]
        require(source["targetFramework"] == runtime["tfm"], "Artifact target differs from the pinned SDK runtime")
        ref = dotnet.parent / "packs/Microsoft.NETCore.App.Ref" / runtime["framework"]["version"] / "ref" / runtime["tfm"]
        require(ref.is_dir(), "Matching SDK targeting pack missing")
        common = [str(dotnet), str(sdk / "Roslyn/bincore/csc.dll"), "-nologo", "-target:exe", "-nullable:enable", "-nowarn:1701"] + ["-r:" + str(p) for p in sorted(ref.glob("*.dll"))]
        helpers = Path(__file__).parent / "api-test-shards"
        retained_sources = output / "helper-source"
        retained_sources.mkdir()
        for path in [Path(__file__), *helpers.glob("*.cs")]:
            shutil.copyfile(path, retained_sources / path.name)
        for name, refs in [("Discover", [binary / p for p in ("xunit.abstractions.dll", "xunit.core.dll", "xunit.execution.dotnet.dll")]),
                           ("VsDiscover", [sdk / p for p in ("Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll", "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll")])]:
            command(common + ["-r:" + str(p) for p in refs] + ["-out:" + str(output / (name + ".dll")), str(helpers / (name + ".cs"))], "compile-" + name)
        manifest["helpers"] = {p.name: digest(p) for p in helpers.glob("*.cs")}
        base = [str(dotnet), "exec", "--runtimeconfig", str(args.assembly.resolve().with_suffix(".runtimeconfig.json"))]
        command(base + ["--depsfile", str(args.assembly.resolve().with_suffix(".deps.json")), str(output / "Discover.dll"), str(args.assembly.resolve()), str(output / "xunit.json")], "discover-xunit")
        vs = base + [str(output / "VsDiscover.dll"), str(sdk), str(args.assembly.resolve()), str(output / "vstest.json")]
        command(vs, "discover-vstest")
        rows = inventory(json.loads((output / "xunit.json").read_text()), json.loads((output / "vstest.json").read_text()))
        shards = partition(rows)
        write(output / "inventory.json", rows)
        write(output / "assignments.json", shards)
        manifest.update({"discovered": len(rows), "collections": len({r["collection"] for r in rows}), "concurrency": 1,
                         "maxRowsPerShard": MAX_ROWS, "maxCollectionsPerShard": MAX_COLLECTIONS, "rssLimitBytes": RSS_LIMIT})
        deadline = time.monotonic() + 150 * 60
        for index, ids in enumerate(shards):
            require(time.monotonic() < deadline, "Aggregate deadline")
            shard = output / f"shard-{index:03}"
            shard.mkdir()
            write(shard / "ids.json", ids)
            require(all(digest(binary / p) == h for p, h in hashes.items()), "Binaries changed before shard")
            command(vs + [str(shard / "ids.json"), str(shard)], f"shard-{index:03}", min(21 * 60, deadline - time.monotonic()))
            reconcile(shard / "results.trx", ids, rows)
            manifest["shards"].append({"index": index, "passed": len(ids), "trxSha256": digest(shard / "results.trx"), "eventsSha256": digest(shard / "events.json")})
            write(output / "coverage.json", manifest)
        require(all(digest(binary / p) == h for p, h in hashes.items()), "Binaries changed after execution")
        require(sum(s["passed"] for s in manifest["shards"]) == len(rows), "Incomplete aggregate")
        manifest["status"] = "PASSED"
    except BaseException as error:
        manifest["error"] = str(error)
        raise
    finally:
        write(output / "coverage.json", manifest)


if __name__ == "__main__":
    def interrupted(signum, frame):
        raise InterruptedError(f"Signal {signum}")
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", type=Path, required=True)
    parser.add_argument("--provenance", type=Path, required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", type=Path, required=True)
    run(parser.parse_args())
