"""Safety guards for the opt-in capability runner; no application suites or database access."""
import importlib.util
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parent))
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


class SocketDiagnosticTests(unittest.TestCase):
    def test_socket_denial_is_not_reported_as_database_down(self):
        packet = dict(owningFiles=[], documents=[])
        checks = [dict(kind='dotnet', database=True, project='tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj', testFile='tests/AuditSphereOps.Domain.Tests/AuthorizationDecisionTests.cs')]
        with patch.dict(runner.os.environ, {}, clear=True), patch.object(runner, 'invoke', return_value=dict(exitCode=1, seconds=0, output='connection failed: Operation not permitted')):
            results = runner.preflight(packet, checks)
        database = next(x for x in results if x['name'] == 'PostgreSQL 18.6 test database')
        self.assertEqual('SOCKET_ACCESS_DENIED', database['failureKind'])
        self.assertIn('do not recreate or reset', database['action'])


def trx(executed=1, passed=1, failed=0, rows=None):
    if rows is None:
        rows = '<UnitTestResult testName="SyntheticPassed" outcome="Passed" />'
    return f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
      <Results>{rows}</Results><ResultSummary><Counters executed="{executed}" passed="{passed}" failed="{failed}" /></ResultSummary></TestRun>'''


class FailureEvidenceTests(unittest.TestCase):
    def parse(self, xml):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'result.trx').write_text(xml)
            return runner.parse_trx(path)

    def test_passing_trx_requires_matching_result_rows(self):
        result = self.parse(trx())
        self.assertEqual(1, result['passedTests'])
        self.assertEqual([], result['resultErrors'])
        self.assertTrue(self.parse(trx(rows=''))['resultErrors'])

    def test_trx_keeps_failed_assertion_trace_id_and_sanitized_logs(self):
        result = self.parse(trx(passed=0, failed=1, rows='''
          <UnitTestResult testName="SyntheticDenied" outcome="Failed"><Output>
          <ErrorInfo><Message>Assert.Equal failure\nExpected: 403\nActual: 200</Message><StackTrace>at Synthetic.Check()</StackTrace></ErrorInfo>
          <StdOut>requestId=00-abcd12345678-12345678-01\nAuthorization: Bearer private-token\nsecret="private multi word"\nuser@example.test</StdOut>
          </Output></UnitTestResult>'''))
        failure = result['failures'][0]
        self.assertIn('Expected: 403', failure['assertion'])
        self.assertIn('Actual: 200', failure['assertion'])
        self.assertEqual(['00-abcd12345678-12345678-01'], failure['requestIds'])
        self.assertIn('Synthetic.Check', failure['stackTrace'])
        serialized = json.dumps(result)
        for value in ['private-token', 'private multi word', 'user@example.test']:
            self.assertNotIn(value, serialized)

    def test_malformed_negative_and_nonnumeric_counters_fail(self):
        for xml in ['<broken', trx(executed=-1), trx(executed='garbage'), trx(passed=2)]:
            with self.subTest(xml=xml):
                self.assertTrue(self.parse(xml)['resultErrors'])

    def test_abort_never_counts_as_pass(self):
        result = self.parse(trx(passed=0, rows='<UnitTestResult testName="SyntheticAbort" outcome="Aborted"/>'))
        self.assertNotEqual(result['executedTests'], result['passedTests'])
        self.assertEqual('Aborted', result['failures'][0]['outcome'])

    def test_missing_result_is_explicit(self):
        with tempfile.TemporaryDirectory() as directory:
            result = runner.parse_trx(Path(directory))
        self.assertEqual(0, result['executedTests'])
        self.assertTrue(result['resultErrors'])

    def test_angular_failed_count_cannot_pass_even_with_zero_exit(self):
        check = dict(name='Angular', kind='angular', include='example.spec.ts')
        with patch.object(runner, 'invoke', return_value=dict(exitCode=0, seconds=1, output='Tests  1 failed | 3 passed (4)')):
            result = runner.execute_check(check, 1)
        self.assertEqual('FAIL', result['status'])
        self.assertEqual(4, result['executedTests'])
        self.assertEqual(1, result['failedTests'])

    def test_command_failure_and_timeout_never_pass(self):
        check = dict(name='Angular', kind='angular', include='example.spec.ts')
        for code, kind in [(1, 'COMMAND_FAILURE'), (124, 'TIMEOUT')]:
            with patch.object(runner, 'invoke', return_value=dict(exitCode=code, seconds=1, output='Tests  3 passed (3)')):
                result = runner.execute_check(check, 1)
            self.assertEqual('FAIL', result['status'])
            self.assertEqual(kind, result['failureKind'])

    def test_dotnet_success_uses_trx_not_console_claims(self):
        check = dict(name='Domain', kind='dotnet', project='example.csproj', filter='ExampleTests')
        def run(argv, timeout, env):
            (Path(argv[-1]) / 'result.trx').write_text(trx())
            return dict(exitCode=0, seconds=1, output='No counter text is needed')
        with patch.object(runner, 'invoke', side_effect=run):
            result = runner.execute_check(check, 1)
        self.assertEqual('PASS', result['status'])
        self.assertEqual(1, result['passedTests'])
        self.assertIsNone(result['failureKind'])

    def test_result_errors_cannot_be_hidden_by_zero_command_exit(self):
        check = dict(name='Domain', kind='dotnet', project='example.csproj', filter='ExampleTests')
        def run(argv, timeout, env):
            (Path(argv[-1]) / 'result.trx').write_text(trx(rows=''))
            return dict(exitCode=0, seconds=1, output='Passed!')
        with patch.object(runner, 'invoke', side_effect=run):
            result = runner.execute_check(check, 1)
        self.assertEqual('FAIL', result['status'])
        self.assertEqual('INVALID_RESULT', result['failureKind'])

    def test_skipped_angular_test_is_not_hidden_by_passing_tests(self):
        check = dict(name='Angular', kind='angular', include='example.spec.ts')
        with patch.object(runner, 'invoke', return_value=dict(exitCode=0, seconds=1, output='Tests  3 passed | 1 skipped (4)')):
            result = runner.execute_check(check, 1)
        self.assertEqual('FAIL', result['status'])
        self.assertEqual('SKIPPED_TESTS', result['failureKind'])

    def test_socket_denial_has_actionable_diagnostic(self):
        check = dict(name='Domain', kind='dotnet', project='example.csproj', filter='ExampleTests')
        with patch.object(runner, 'invoke', return_value=dict(exitCode=1, seconds=1, output='System.Net.Sockets.SocketException (13): Permission denied')):
            result = runner.execute_check(check, 1)
        self.assertEqual('SOCKET_ACCESS_DENIED', result['failureKind'])
        self.assertIn('permitted local shell', result['diagnostic'])

    def test_redaction_handles_quoted_json_bearer_and_private_keys(self):
        text = runner.sanitize('''{"client_secret": "two private words"}\nAuthorization: Bearer private-bearer\n-----BEGIN PRIVATE KEY-----\nprivate-material\n-----END PRIVATE KEY-----''')
        for value in ['two private words', 'private-bearer', 'private-material']:
            self.assertNotIn(value, text)

    def test_selected_commands_pin_safe_test_environment_and_clear_stale_handoff(self):
        check = dict(name='Angular', kind='angular', include='example.spec.ts')
        with patch.dict(runner.os.environ, {'AUDITSPHERE_BROWSER_FIXTURE_READY_FILE': 'old.json', 'AUDITSPHERE_TEST_CONNECTION': 'Host=external'}), patch.object(runner, 'invoke', return_value=dict(exitCode=0, seconds=1, output='Tests  1 passed')) as invoked:
            runner.execute_check(check, 1)
        env = invoked.call_args.args[2]
        self.assertNotIn('AUDITSPHERE_BROWSER_FIXTURE_READY_FILE', env)
        self.assertIn('Database=auditsphere_tests', env['AUDITSPHERE_TEST_CONNECTION'])
        self.assertEqual('false', env['ExternalEffects__Enabled'])


class ComparisonTests(unittest.TestCase):
    def report(self):
        return dict(schemaVersion=2, capability='engagement-ui', cohort=[dict(name='test', command=['safe'])],
                    status='FOCUSED_CHECKS_PASSED', sourceUnchangedDuringRun=True, measurementContext='same task - same machine',
                    checks=[dict(name='test', status='PASS', executedTests=1, passedTests=1)],
                    metrics=dict(discoverySeconds=30, editingSeconds=None, preflightSeconds=1, testWaitSeconds=5,
                                 environmentFailures=0, reworkCount=None, acceptanceRegressions=None))

    def test_same_cohort_reports_measured_deltas_without_inventing_missing_values(self):
        before, after = self.report(), self.report()
        after['metrics']['discoverySeconds'] = 10
        result = runner.compare_reports(before, after)
        self.assertEqual('COMPARABLE', result['status'])
        self.assertEqual(-20, result['deltas']['discoverySeconds']['change'])
        self.assertIn('acceptanceRegressions', result['unmeasured'])
        self.assertIn('editingSeconds', result['unmeasured'])
        self.assertEqual('NOT_ESTABLISHED', result['causalImprovement'])

    def test_changed_context_cohort_source_or_unrun_report_cannot_compare(self):
        for field, value in [('measurementContext', 'other'), ('cohort', []), ('sourceUnchangedDuringRun', False),
                             ('status', 'PREFLIGHT_READY'), ('schemaVersion', 1), ('capability', 'other'), ('checks', [])]:
            with self.subTest(field=field):
                before, after = self.report(), self.report()
                after[field] = value
                self.assertEqual('NOT_COMPARABLE', runner.compare_reports(before, after)['status'])

    def test_changed_test_counts_cannot_compare(self):
        before, after = self.report(), self.report()
        after['checks'][0].update(executedTests=2, passedTests=2)
        self.assertEqual('NOT_COMPARABLE', runner.compare_reports(before, after)['status'])

    def test_changed_build_preparation_cannot_compare(self):
        before, after = self.report(), self.report()
        after['preparations'] = [dict(command=['npm', 'run', 'build'])]
        self.assertEqual('NOT_COMPARABLE', runner.compare_reports(before, after)['status'])

    def test_nonfinite_and_boolean_measurements_stay_unmeasured(self):
        for value in [float('nan'), float('inf'), -1, True]:
            before, after = self.report(), self.report()
            after['metrics']['discoverySeconds'] = value
            result = runner.compare_reports(before, after)
            self.assertIn('discoverySeconds', result['unmeasured'])


class PacketAndSourceTests(unittest.TestCase):
    def test_real_catalog_and_every_acceptance_mapping_validate(self):
        catalog = json.loads(runner.CATALOG.read_text())
        runner.validate_catalog(catalog)
        first = next(iter(catalog['capabilities'].values()))
        first['verificationCoverage'][0]['checks'] = ['missing-check']
        with self.assertRaises(ValueError):
            runner.validate_catalog(catalog)

    def test_mislabelled_browser_layer_is_rejected(self):
        catalog = json.loads(runner.CATALOG.read_text())
        next(iter(catalog['capabilities'].values()))['checks'][0]['layer'] = 'browser'
        with self.assertRaises(ValueError):
            runner.validate_catalog(catalog)

    def test_snapshot_catches_html_css_and_new_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ['view.html', 'view.scss']:
                (root / name).write_text('initial')
            with patch.object(runner, 'ROOT', root), patch.object(runner.subprocess, 'check_output', return_value=b'view.html\0view.scss\0') as files:
                before = runner.source_snapshot()
                (root / 'view.html').write_text('changed')
                self.assertNotEqual(before, runner.source_snapshot())
                (root / 'new.txt').write_text('new')
                files.return_value = b'view.html\0view.scss\0new.txt\0'
                self.assertIn('new.txt', runner.source_snapshot())

    def test_report_cannot_overwrite_source_or_existing_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            report = root / 'artifacts/agent-check/existing.json'
            report.parent.mkdir(parents=True)
            report.write_text('unchanged')
            for path in [root / 'AGENTS.md', report]:
                with self.subTest(path=path), patch.object(runner, 'ROOT', root), patch.object(sys, 'argv', ['agent-check', '--list', '--report', str(path)]), contextlib.redirect_stderr(io.StringIO()):
                    with self.assertRaises(SystemExit) as error:
                        runner.main()
                    self.assertEqual(2, error.exception.code)
            self.assertEqual('unchanged', report.read_text())

    def test_exclusive_report_creation_refuses_concurrent_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'report.json'
            runner.write_report(path, 'first')
            with self.assertRaises(FileExistsError):
                runner.write_report(path, 'second')
            self.assertEqual('first', path.read_text())

    def test_browser_preparation_builds_only_when_selected_and_reports_failure(self):
        with patch.object(runner, 'invoke', return_value=dict(exitCode=1, seconds=1, output='error: synthetic build failure')) as invoke, contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual([], runner.prepare_browser([dict(browser=False)], 10))
            invoke.assert_not_called()
            result = runner.prepare_browser([dict(browser=True)], 10)
        self.assertEqual('FAIL', result[0]['status'])
        self.assertEqual(['npm', '--prefix', 'src/AuditSphereOps.Ui', 'run', 'build'], result[0]['command'])

    def test_alternate_browser_checkout_is_blocked(self):
        packet = dict(owningFiles=[], documents=[])
        checks = [dict(kind='dotnet', database=True, browser=True, project='tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj', testFile='tests/AuditSphereOps.E2E.Tests/AngularClientScopeEngagementParityJourneyTests.cs')]
        with patch.dict(runner.os.environ, {'AUDITSPHERE_TEST_REPOSITORY_ROOT': '/other/checkout'}, clear=True), patch.object(runner, 'invoke', return_value=dict(exitCode=0, seconds=0, output='180006')):
            result = runner.preflight(packet, checks)
        self.assertEqual('BLOCKED', next(p for p in result if p['name'] == 'Browser host source')['status'])

    def test_default_preflight_never_executes_tests_and_omitted_browser_stays_unverified(self):
        stdout = io.StringIO()
        with patch.object(sys, 'argv', ['agent-check', 'engagement-ui', '--json']), patch.object(runner, 'preflight', return_value=[]), patch.object(runner, 'source_snapshot', return_value={}), patch.object(runner, 'invoke', return_value=dict(output='synthetic-commit', exitCode=0, seconds=0)), patch.object(runner, 'execute_check') as execute, contextlib.redirect_stdout(stdout):
            self.assertEqual(0, runner.main())
        execute.assert_not_called()
        report = json.loads(stdout.getvalue())
        self.assertEqual('PREFLIGHT_READY', report['status'])
        self.assertIsNone(report['testedCommit'])
        self.assertTrue(report['omittedChecks'])
        self.assertTrue(all(c['status'] == 'NOT_VERIFIED' for c in report['coverage']))

    def test_nonfinite_measurement_rejected_before_any_preflight(self):
        with patch.object(sys, 'argv', ['agent-check', 'engagement-ui', '--discovery-seconds', 'nan']), patch.object(runner, 'preflight') as preflight, contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                runner.main()
        preflight.assert_not_called()

    def test_changed_source_invalidates_successful_checks_and_coverage(self):
        stdout = io.StringIO()
        passed = dict(name='Engagement UI contracts and lifetime', status='PASS', executedTests=7)
        with patch.object(sys, 'argv', ['agent-check', 'engagement-ui', '--run', '--json']), patch.object(runner, 'preflight', return_value=[]), patch.object(runner, 'source_snapshot', side_effect=[{'view.html': 'before'}, {'view.html': 'after'}]), patch.object(runner, 'invoke', return_value=dict(output='synthetic-commit', exitCode=0, seconds=0)), patch.object(runner, 'execute_check', return_value={**passed, 'seconds': 1}), contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(1, runner.main())
        report = json.loads(stdout.getvalue())
        self.assertEqual('SOURCE_CHANGED', report['status'])
        self.assertEqual(['view.html'], report['changedDuringRun'])
        self.assertIsNone(report['testedCommit'])
        self.assertTrue(all(c['status'] == 'SOURCE_CHANGED' for c in report['coverage']))

if __name__ == '__main__':
    unittest.main()
