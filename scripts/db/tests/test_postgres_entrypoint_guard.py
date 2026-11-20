"""Fail-closed tests for the self-managed PostgreSQL major-version guard."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
GUARD = REPOSITORY_ROOT / "infra/hostinger-acceptance/postgres-entrypoint-guard.sh"


class PostgresEntrypointGuardTests(unittest.TestCase):
    def run_guard(self, root: Path, entrypoint: Path, pgdata: Path | None = None):
        env = os.environ.copy()
        env.update({
            "AUDITSPHERE_POSTGRES_DATA_ROOT": str(root),
            "AUDITSPHERE_POSTGRES_ENTRYPOINT": str(entrypoint),
            "PGDATA": str(pgdata or root / "19/docker"),
        })
        return subprocess.run(
            ["/bin/sh", str(GUARD), "postgres", "-c", "fsync=on"],
            check=False,
            capture_output=True,
            text=True,
            env=env,
        )

    def make_entrypoint(self, directory: Path) -> tuple[Path, Path]:
        called = directory / "entrypoint-called"
        entrypoint = directory / "docker-entrypoint.sh"
        entrypoint.write_text(f'#!/bin/sh\nprintf "%s\\n" "$@" > "{called}"\n')
        entrypoint.chmod(0o755)
        return entrypoint, called

    def test_empty_volume_and_existing_postgres19_volume_delegate_to_official_entrypoint(self):
        for existing_cluster in (False, True):
            with self.subTest(existing_cluster=existing_cluster), tempfile.TemporaryDirectory() as temp:
                root = Path(temp) / "data"
                entrypoint, called = self.make_entrypoint(Path(temp))
                if existing_cluster:
                    pgdata = root / "19/docker"
                    pgdata.mkdir(parents=True)
                    (pgdata / "PG_VERSION").write_text("19\n")

                result = self.run_guard(root, entrypoint)

                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual("postgres\n-c\nfsync=on\n", called.read_text())

    def test_old_major_volume_is_refused_without_calling_official_entrypoint(self):
        for old_path in ("18/docker", "data", "."):
            with self.subTest(old_path=old_path), tempfile.TemporaryDirectory() as temp:
                root = Path(temp) / "data"
                old_cluster = root if old_path == "." else root / old_path
                old_cluster.mkdir(parents=True)
                (old_cluster / "PG_VERSION").write_text("18\n")
                entrypoint, called = self.make_entrypoint(Path(temp))

                result = self.run_guard(root, entrypoint)

                self.assertEqual(78, result.returncode)
                self.assertIn("existing PostgreSQL 18 cluster", result.stderr)
                self.assertFalse(called.exists())

    def test_wrong_major_in_target_or_wrong_pgdata_path_is_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "data"
            pgdata = root / "19/docker"
            pgdata.mkdir(parents=True)
            (pgdata / "PG_VERSION").write_text("18\n")
            entrypoint, called = self.make_entrypoint(Path(temp))

            result = self.run_guard(root, entrypoint)

            self.assertEqual(78, result.returncode)
            self.assertIn("contains PostgreSQL 18 data", result.stderr)
            self.assertFalse(called.exists())

            wrong_pgdata = root / "18/docker"
            result = self.run_guard(root, entrypoint, wrong_pgdata)

            self.assertEqual(78, result.returncode)
            self.assertIn("PGDATA must be", result.stderr)
            self.assertFalse(called.exists())

    def test_nonempty_target_without_pg_version_is_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "data"
            pgdata = root / "19/docker"
            pgdata.mkdir(parents=True)
            (pgdata / "unexpected-file").write_text("preserve me")
            entrypoint, called = self.make_entrypoint(Path(temp))

            result = self.run_guard(root, entrypoint)

            self.assertEqual(78, result.returncode)
            self.assertIn("non-empty but has no valid PostgreSQL 19 PG_VERSION", result.stderr)
            self.assertFalse(called.exists())


if __name__ == "__main__":
    unittest.main()
