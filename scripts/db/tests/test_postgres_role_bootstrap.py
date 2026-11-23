"""Fail-closed tests for the Hostinger PostgreSQL 19 service-role bootstrap."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
BOOTSTRAP = REPOSITORY_ROOT / "infra/hostinger-acceptance/postgres-role-bootstrap.sh"


class PostgresRoleBootstrapTests(unittest.TestCase):
    def test_refuses_postgresql18_before_running_any_role_ddl(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            psql = directory / "psql"
            calls = directory / "psql-calls.txt"
            psql.write_text(
                "#!/bin/sh\n"
                f"printf '%s\\n' \"$*\" >> '{calls}'\n"
                "printf '180006\\n'\n"
            )
            psql.chmod(0o755)

            env = os.environ.copy()
            env.update({
                "PATH": f"{directory}:{env['PATH']}",
                "POSTGRES_USER": "bootstrap_test",
                "POSTGRES_DB": "auditsphere",
                "AUDITSPHERE_RUNTIME_DB_PASSWORD": "disposable-test-value",
                "AUDITSPHERE_MIGRATOR_DB_PASSWORD": "disposable-test-value",
            })
            result = subprocess.run(
                ["/bin/sh", str(BOOTSTRAP)],
                check=False,
                capture_output=True,
                text=True,
                env=env,
            )

            self.assertEqual(78, result.returncode)
            self.assertIn("restricted to PostgreSQL 19", result.stderr)
            self.assertEqual(1, len(calls.read_text().splitlines()))


if __name__ == "__main__":
    unittest.main()
