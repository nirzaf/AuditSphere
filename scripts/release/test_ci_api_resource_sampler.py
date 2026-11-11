"""Controlled shell/sampler checks only; never starts dotnet, PostgreSQL or CI."""
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('sampler', ROOT / 'ci-api-resource-sampler.py')
sampler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sampler)


class SamplerTests(unittest.TestCase):
    def test_missing_fields_and_fixed_keys(self):
        self.assertEqual(sampler.counters('/nonexistent', ('oom',)), {'oom': None})
        sample = sampler.sample()
        self.assertEqual(set(sample), {'kind', 'sampler_pid', 'unix_seconds', 'memory_kib',
            'cgroup_memory_events', 'cgroup_cpu', 'disk_available_bytes', 'host_cpu_ticks',
            'host_process_rss_kib', 'host_process_count'})
        json.dumps(sample, allow_nan=False)

    def exercise(self, status, cancel=False, enabled=True):
        with tempfile.TemporaryDirectory() as directory:
            env = dict(os.environ, VERIFICATION_LOG_DIR=directory,
                       TEST_PROJECT_KEY='api' if enabled else 'domain', GITHUB_STEP_SUMMARY='')
            command = ['bash', str(ROOT / 'run-verification-step.sh'),
                       'Full api test project (no --filter)', 'bash', '-c',
                       f'sleep 1; exit {status}']
            child = subprocess.Popen(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            if cancel:
                time.sleep(.5)
                child.send_signal(signal.SIGTERM)
            stdout, stderr = child.communicate(timeout=10)
            self.assertEqual(child.returncode, 143 if cancel else status, stderr.decode())
            records = [json.loads(line) for line in stdout.decode().splitlines() if line.startswith('{"kind":')]
            self.assertEqual(bool(records), enabled)
            for record in records:
                with self.assertRaises(ProcessLookupError):
                    os.kill(record['sampler_pid'], 0)
            if enabled:
                self.assertIn('API_RUNNER_RESOURCES', next(Path(directory).glob('*.log')).read_text())

    def test_success_reaps_sampler(self):
        self.exercise(0)

    def test_failure_preserves_exit(self):
        self.exercise(7)

    def test_cancellation_reaps_only_sampler(self):
        # Independent sibling survives; no process-group or broad-name kill is permitted.
        sibling = subprocess.Popen(['sleep', '10'])
        try:
            self.exercise(0, cancel=True)
            self.assertIsNone(sibling.poll())
        finally:
            sibling.terminate()
            sibling.wait(timeout=3)

    def test_other_gate_unchanged(self):
        self.exercise(7, enabled=False)


if __name__ == '__main__':
    unittest.main()
