#!/usr/bin/env python3
"""Start, inspect and stop the RUN-005 synthetic browser workspace (loopback only, external effects off).

  start  [--hold-seconds 1800] [--ready-timeout 600]   launches the opt-in launcher test, waits for READY
  status                                                prints the manifest (URLs and fixture ids, no secrets)
  stop                                                  requests teardown through the stop file

The workspace runs SyntheticWorkspaceLauncherTests, which owns and drops its own schema in the loopback
auditsphere_tests database. Output lives under .local/autonomous/synthetic/ (ignored by git).
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / ".local" / "autonomous" / "synthetic"
MANIFEST = ROOT / "manifest.json"
STOP = Path(str(MANIFEST) + ".stop")
PROJECT = REPO / "tests" / "AuditSphereOps.E2E.Tests" / "AuditSphereOps.E2E.Tests.csproj"


def start(hold: int, ready_timeout: int) -> int:
    if MANIFEST.exists():
        print(json.dumps({"status": "ALREADY_RUNNING", "manifest": str(MANIFEST)}))
        return 1
    ROOT.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, AUDITSPHERE_SYNTHETIC_WORKSPACE_MANIFEST=str(MANIFEST),
               AUDITSPHERE_SYNTHETIC_WORKSPACE_HOLD_SECONDS=str(hold))
    log = open(ROOT / "launcher.log", "w", encoding="utf-8")
    process = subprocess.Popen(
        ["dotnet", "test", str(PROJECT), "--no-restore", "-c", "Release", "--maxcpucount:1",
         "--filter", "FullyQualifiedName~SyntheticWorkspaceLauncherTests"],
        cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    (ROOT / "launcher.pid").write_text(str(process.pid))
    deadline = time.monotonic() + ready_timeout
    while time.monotonic() < deadline:
        if MANIFEST.exists():
            print(json.dumps({"status": "READY", "pid": process.pid, "manifest": json.loads(MANIFEST.read_text())}, indent=2))
            return 0
        if process.poll() is not None:
            print(json.dumps({"status": "FAILED", "exitCode": process.returncode, "log": str(ROOT / "launcher.log")}))
            return 1
        time.sleep(2)
    STOP.touch()
    print(json.dumps({"status": "TIMED_OUT", "log": str(ROOT / "launcher.log")}))
    return 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    s = sub.add_parser("start")
    s.add_argument("--hold-seconds", type=int, default=1800)
    s.add_argument("--ready-timeout", type=int, default=600)
    sub.add_parser("status")
    sub.add_parser("stop")
    args = parser.parse_args(argv)
    if args.command == "start":
        return start(args.hold_seconds, args.ready_timeout)
    if args.command == "status":
        print(MANIFEST.read_text() if MANIFEST.exists() else json.dumps({"status": "NOT_RUNNING"}))
        return 0 if MANIFEST.exists() else 1
    if not MANIFEST.exists():
        print(json.dumps({"status": "NOT_RUNNING"}))
        return 0
    STOP.touch()
    print(json.dumps({"status": "STOP_REQUESTED"}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
