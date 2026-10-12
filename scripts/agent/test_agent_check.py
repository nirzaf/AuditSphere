"""Safety guards for the opt-in capability runner; no application suites or database access."""
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('agent_check', Path(__file__).with_name('agent-check.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

class RunnerSafetyTests(unittest.TestCase):
    def test_zero_matching_tests_cannot_pass(self):
        check = dict(name='filtered', kind='dotnet', project='tests/example.csproj', filter='MissingTests')
        with patch.object(runner, 'invoke', return_value=dict(exitCode=0, seconds=0, output='No test matches')):
            result = runner.execute_check(check, 1)
        self.assertEqual('FAIL', result['status'])
        self.assertEqual(0, result['executedTests'])

    def test_report_redacts_connection_credentials_tokens_urls_and_mailboxes(self):
        raw = 'Host=server;Password=private-value\nsecret=private-secret https://example.test/?token=private-token user@example.test'
        text = runner.sanitize(raw)
        for secret in ['private-value', 'private-secret', 'private-token', 'user@example.test']:
            self.assertNotIn(secret, text)

    def test_external_connection_override_blocks_preflight(self):
        packet = dict(owningFiles=[], documents=[])
        checks = [dict(kind='dotnet', database=True, project='tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj', testFile='tests/AuditSphereOps.Domain.Tests/AuthorizationDecisionTests.cs')]
        with patch.dict(runner.os.environ, {'AUDITSPHERE_TEST_CONNECTION': 'Host=production;Database=real'}, clear=True), patch.object(runner, 'invoke', return_value=dict(exitCode=0, seconds=0, output='180006')):
            checks = runner.preflight(packet, checks)
        target = next(x for x in checks if x['name'] == 'Test database target')
        self.assertEqual('BLOCKED', target['status'])

if __name__ == '__main__':
    unittest.main()
