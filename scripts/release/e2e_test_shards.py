#!/usr/bin/env python3
"""Split browser tests by complete xUnit collections and reconcile all runner results."""
import argparse
from collections import Counter, defaultdict
import json
import os
from pathlib import Path
import shutil
import subprocess

from api_test_shards import digest, inventory, reconcile, require, write

SHARD_COUNT = 4


def assignments(rows):
    groups = defaultdict(list)
    for row in rows:
        groups[row["collection"]].append(row["id"])
    shards = [[] for _ in range(SHARD_COUNT)]
    for _, ids in sorted(groups.items(), key=lambda item: (-len(item[1]), item[0])):
        min(shards, key=len).extend(ids)
    require(all(shards) and Counter(i for s in shards for i in s) == Counter(r["id"] for r in rows),
            "Incomplete or duplicate E2E partition")
    return [sorted(shard) for shard in shards]


def load(path):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 32 * 1024**2,
            f"Missing, linked or oversized evidence: {path.name}")
    return json.loads(path.read_text())


def discover(assembly, directory):
    dotnet = Path(shutil.which("dotnet")).resolve()
    version = load(Path("global.json"))["sdk"]["version"]
    require(subprocess.check_output([str(dotnet), "--version"], text=True).strip() == version, "Wrong SDK")
    sdk = dotnet.parent / "sdk" / version
    runtime = load(sdk / "dotnet.runtimeconfig.json")["runtimeOptions"]
    ref = dotnet.parent / "packs/Microsoft.NETCore.App.Ref" / runtime["framework"]["version"] / "ref" / runtime["tfm"]
    require(ref.is_dir(), "Missing SDK targeting pack")
    common = [str(dotnet), str(sdk / "Roslyn/bincore/csc.dll"), "-nologo", "-target:exe",
              "-nullable:enable", "-nowarn:1701"] + ["-r:" + str(p) for p in sorted(ref.glob("*.dll"))]
    helpers = Path(__file__).with_name("api-test-shards")
    base = [str(dotnet), "exec", "--runtimeconfig", str(assembly.with_suffix(".runtimeconfig.json"))]
    for name, refs in [("Discover", [assembly.parent / p for p in
                        ("xunit.abstractions.dll", "xunit.core.dll", "xunit.execution.dotnet.dll")]),
                       ("VsDiscover", [sdk / p for p in ("Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll",
                        "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll")])]:
        binary = directory / (name + ".dll")
        subprocess.run(common + ["-r:" + str(p) for p in refs] +
                       ["-out:" + str(binary), str(helpers / (name + ".cs"))], check=True, timeout=120)
        if name == "Discover":
            subprocess.run(base + ["--depsfile", str(assembly.with_suffix(".deps.json")),
                           str(binary), str(assembly), str(directory / "xunit.json")], check=True, timeout=240)
    command = base + [str(directory / "VsDiscover.dll"), str(sdk), str(assembly), str(directory / "vstest.json")]
    subprocess.run(command, check=True, timeout=240)
    return command


def run(args):
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    assembly = args.assembly.resolve()
    require(subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip() == args.source, "Source differs")
    fingerprint = digest(assembly)
    command = discover(assembly, output)
    rows = inventory(load(output / "xunit.json"), load(output / "vstest.json"))
    ids = assignments(rows)[args.shard]
    write(output / "ids.json", ids)
    # Real adapter objects are rediscovered and matched verbatim by the existing runner.
    subprocess.run(command + [str(output / "ids.json"), str(output), "8400000"], check=True, timeout=8700)
    reconcile(output / "results.trx", ids, rows)
    require(digest(assembly) == fingerprint, "E2E assembly changed during execution")
    write(output / "receipt.json", {"source": args.source, "repository": os.environ["GITHUB_REPOSITORY"],
          "runId": int(os.environ["GITHUB_RUN_ID"]), "attempt": int(os.environ["GITHUB_RUN_ATTEMPT"]),
          "shard": args.shard, "assemblySha256": fingerprint,
          "trxSha256": digest(output / "results.trx"), "eventsSha256": digest(output / "events.json")})
    print(f"E2E shard {args.shard + 1}/{SHARD_COUNT}: {len(ids)} passed of {len(rows)} discovered in the complete suite.")


def aggregate(root, output, source, repository, run_id, attempt):
    slots = {f"browser-e2e-shard-{i}" for i in range(SHARD_COUNT)}
    require({p.name for p in root.iterdir()} == slots, "Missing or extra E2E shard artifact")
    baseline = assembly = None
    paths = []
    for shard in range(SHARD_COUNT):
        path = root / f"browser-e2e-shard-{shard}"
        require(path.is_dir() and not any(p.is_symlink() for p in [path, *path.iterdir()]), "Linked shard artifact")
        receipt = load(path / "receipt.json")
        require(receipt["source"] == source and receipt["repository"] == repository and
                type(receipt["runId"]) is int and receipt["runId"] == run_id and
                type(receipt["attempt"]) is int and 1 <= receipt["attempt"] <= attempt and
                type(receipt["shard"]) is int and receipt["shard"] == shard, "Unbound shard evidence")
        fingerprint = receipt["assemblySha256"]
        require(isinstance(fingerprint, str) and len(fingerprint) == 64 and
                all(c in "0123456789abcdef" for c in fingerprint), "Invalid assembly fingerprint")
        rows = inventory(load(path / "xunit.json"), load(path / "vstest.json"))
        if baseline is None:
            baseline, assembly = rows, fingerprint
        require(rows == baseline and assembly == fingerprint, "Shard discovery or assembly differs")
        ids = assignments(rows)[shard]
        require(load(path / "ids.json") == ids, "Shard assignment differs")
        trx = path / "results.trx"
        require(digest(trx) == receipt["trxSha256"] and
                digest(path / "events.json") == receipt["eventsSha256"], "Shard evidence digest differs")
        reconcile(trx, ids, rows)
        paths.append(trx)
    output.mkdir(parents=True, exist_ok=False)
    for shard, trx in enumerate(paths):
        shutil.copyfile(trx, output / f"e2e-shard-{shard}.trx")
    write(output / "coverage.json", {"source": source, "repository": repository, "runId": run_id,
          "assemblySha256": assembly, "passed": len(baseline), "shards": [len(s) for s in assignments(baseline)]})
    print(f"Verified {len(baseline)} passing browser tests across {SHARD_COUNT} isolated runners.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="mode", required=True)
    worker = commands.add_parser("run")
    worker.add_argument("--assembly", required=True, type=Path)
    worker.add_argument("--shard", required=True, type=int, choices=range(SHARD_COUNT))
    gate = commands.add_parser("aggregate")
    gate.add_argument("--artifacts", required=True, type=Path)
    for command in (worker, gate):
        command.add_argument("--source", required=True)
        command.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.mode == "run":
        run(args)
    else:
        aggregate(args.artifacts, args.output, args.source, os.environ["GITHUB_REPOSITORY"],
                  int(os.environ["GITHUB_RUN_ID"]), int(os.environ["GITHUB_RUN_ATTEMPT"]))
