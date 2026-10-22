"""Offline launcher contract: no SDK, listener, database or authentication changes."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "backend-watch.sh"


class BackendWatchTests(unittest.TestCase):
    def test_scoped_debug_arguments_and_occupied_port(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            (folder / "dotnet").write_text(
                '#!/usr/bin/env python3\nimport json,os,sys\n'
                'print(json.dumps({"args":sys.argv[1:],"env":dict(os.environ)}))\n')
            (folder / "lsof").write_text('#!/bin/sh\nexit "${PROBE_LISTENING:-1}"\n')
            for path in folder.iterdir():
                path.chmod(0o700)
            env = dict(os.environ, PATH=f"{directory}:{os.environ['PATH']}",
                       DOTNET_ENVIRONMENT="Production", AngularUi__Enabled="true")
            for listening in ("1", "0"):
                with self.subTest(listening=listening):
                    result = subprocess.run(["bash", str(SCRIPT)], env=dict(env, PROBE_LISTENING=listening),
                                            capture_output=True, text=True, check=False)
                    if listening == "0":
                        self.assertEqual(result.returncode, 1)
                        self.assertIn("Port 5100 is occupied", result.stderr)
                        self.assertEqual(result.stdout, "")
                    else:
                        self.assertEqual(result.returncode, 0, result.stderr)
                        observed = json.loads(result.stdout)
                        self.assertEqual(observed["args"], ["watch", "--non-interactive", "--project",
                            "src/AuditSphereOps.Api/AuditSphereOps.Api.csproj", "run", "--configuration",
                            "Debug", "--no-restore", "--no-launch-profile", "--property:BuildInParallel=false",
                            "--", "--urls", "http://localhost:5100"])
                        for key in ("DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT"):
                            self.assertEqual(observed["env"][key], "Development")
                        for key in ("AngularUi__Enabled", "AngularUi__CanonicalRoutes"):
                            self.assertEqual(observed["env"][key], "false")
            self.assertEqual(env["DOTNET_ENVIRONMENT"], "Production")
            self.assertEqual(env["AngularUi__Enabled"], "true")

    def test_rejects_arguments_before_starting_sdk(self):
        result = subprocess.run(["bash", str(SCRIPT), "--production"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn("Usage:", result.stderr)


if __name__ == "__main__":
    unittest.main()
