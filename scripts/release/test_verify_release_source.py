"""Saved-response tests for exact-source release-verification evidence."""
import copy
import shutil
import xml.etree.ElementTree as ET
import api_test_shards as shards
import hashlib
import importlib.util
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).with_name('verify-release-source.py')
SPEC = importlib.util.spec_from_file_location('verify_release_source', SCRIPT)
verify_source = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verify_source)


def api_fixture(folder, repository, source, run_id, attempt):
    output = folder / "api-shards"
    output.mkdir()
    def save(name, value):
        path = output / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value))
    xunit, vstest = [], []
    for i in range(10):
        x = dict(id=f"x{i}", collection=f"collection{i // 2}", className=f"C{i // 2}", method="Theory",
                 display=f"C{i // 2}.Theory(value: {i})", kind="Xunit.Sdk.XunitTestCase", skip=None)
        v = dict(id=f"v{i}", xunitId=x["id"], className=x["className"], method=x["method"], display=x["display"],
                 fqn=x["className"] + "." + x["method"])
        xunit.append(x); vstest.append(v)
    rows = shards.inventory(xunit, vstest)
    assignments = shards.partition(rows)
    save("xunit.json", xunit); save("vstest.json", vstest)
    save("inventory.json", rows); save("assignments.json", assignments)
    save("source.json", dict(schemaVersion=1, repository=repository, resolvedCommit=source, runId=run_id,
        runAttempt=attempt, artifactName=f"release-verification-test-build-{source}-{run_id}-api",
        project="tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj", assembly="AuditSphereOps.Api.Tests",
        configuration="Release", targetFramework="net10.0", operatingSystem="Linux", dotnetSdk="10.0.300"))
    save("binary-hashes.json", {name: "0" * 64 for name in
        ["AuditSphereOps.Api.Tests" + suffix for suffix in (".dll", ".pdb", ".deps.json", ".runtimeconfig.json")] + ["xunit.runner.json"]})
    (output / "helper-source").mkdir()
    for name in ("Discover.cs", "VsDiscover.cs"):
        (output / "helper-source" / name).write_text("// Synthetic retained helper " + name)
    helper_hashes = {name: shards.digest(output / "helper-source" / name) for name in ("Discover.cs", "VsDiscover.cs")}
    coverage = dict(status="PASSED", source=source, sdk="10.0.300", discovered=len(rows), collections=5,
        concurrency=1, maxRowsPerShard=shards.MAX_ROWS, maxCollectionsPerShard=shards.MAX_COLLECTIONS,
        rssLimitBytes=shards.RSS_LIMIT, helpers=helper_hashes, binaryManifestSha256=shards.digest(output / "binary-hashes.json"), shards=[])
    ns = "{" + shards.NS["t"] + "}"
    def element(parent, tag, **attrs):
        return ET.SubElement(parent, ns + tag, attrs)
    for index, ids in enumerate(assignments):
        name = f"shard-{index:03}"
        save(name + "/ids.json", ids)
        root = ET.Element(ns + "TestRun")
        results = element(root, "Results"); definitions = element(root, "TestDefinitions"); entries = element(root, "TestEntries")
        for row in [r for r in rows if r["id"] in ids]:
            element(results, "UnitTestResult", testId=row["id"], testName=row["display"], outcome="Passed", executionId=row["id"])
            definition = element(definitions, "UnitTest", id=row["id"], name=row["display"])
            element(definition, "Execution", id=row["id"])
            element(definition, "TestMethod", className=row["className"], name=row["method"])
            element(entries, "TestEntry", testId=row["id"], executionId=row["id"])
        summary = element(root, "ResultSummary", outcome="Completed")
        element(summary, "Counters", total=str(len(ids)), executed=str(len(ids)), passed=str(len(ids)), failed="0", notExecuted="0")
        ET.ElementTree(root).write(output / name / "results.trx")
        save(name + "/events.json", dict(complete=True, errors=[], total=len(ids), results=[dict(id=i, outcome="Passed") for i in ids]))
        save(name + ".exit.json", dict(exit=0, reason=None, groupDrained=True, peakRssBytes=100, seconds=1.0))
        coverage["shards"].append(dict(index=index, passed=len(ids), trxSha256=shards.digest(output / name / "results.trx"),
                                       eventsSha256=shards.digest(output / name / "events.json")))
    for name, reason in (("deadline", "Deadline"), ("cancel", "Synthetic cancellation"), ("leader-exit", None)):
        save("supervisor-checks/" + name + ".exit.json", dict(exit=0 if reason is None else -9, reason=reason, groupDrained=True, seconds=1.0))
    save("coverage.json", coverage)
    return output


class ReleaseEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.now = datetime(2026, 10, 17, tzinfo=timezone.utc)
        self.repo = 'nirzaf/AuditSphere'
        self.candidate = 'b' * 40
        self.workflow_sha = 'a' * 40
        self.run_id = 12345
        self.attempt = 2
        self.toolchain = dict(sourceSha=self.candidate, dotnetSdk="10.0.300", targetFramework="net10.0")
        self.trx_root = self.root / 'trx'
        self.trx_root.mkdir()
        for cohort in ('domain', 'e2e'):
            folder = self.trx_root / f'release-verification-trx-{cohort}'
            folder.mkdir()
            (folder / f'{cohort}.trx').write_text(
                '<TestRun xmlns="urn:trx"><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" skipped="0" /></ResultSummary>'
                '<Results><UnitTestResult testName="synthetic" outcome="Passed" /></Results></TestRun>', encoding='utf-8')
        self.api_folder = self.trx_root / 'release-verification-trx-api'
        self.api_folder.mkdir()
        api_fixture(self.api_folder, self.repo, self.candidate, self.run_id, 1)
        self.run_artifacts = []
        self.artifact_ids = {}
        for index, name in enumerate(verify_source.REQUIRED_ARTIFACTS, 1):
            item = dict(id=100 + index, name=name, size_in_bytes=20, digest=None,
                        expired=False, expires_at=(self.now + timedelta(days=20)).isoformat())
            self.run_artifacts.append(item)
            self.artifact_ids[name] = item['id']
        self.record, errors = verify_source.build_record(
            self.repo, f'{self.repo}/{verify_source.WORKFLOW_PATH}@refs/heads/master', self.workflow_sha,
            self.run_id, self.attempt, 'workflow_dispatch', self.workflow_sha, 'master', self.candidate,
            {job: 'success' for job in verify_source.REQUIRED_JOBS}, self.trx_root, self.run_artifacts, self.now, toolchain=self.toolchain)
        self.assertEqual([], errors)

    def tearDown(self):
        self.temp.cleanup()

    def save_candidate(self, record=None, *, run_attempt=None):
        run = dict(id=self.run_id, workflow_id=77, path=verify_source.WORKFLOW_PATH,
                   repository={'full_name': self.repo}, head_repository={'full_name': self.repo},
                   event='workflow_dispatch', head_branch='master', status='completed', conclusion='success',
                   run_attempt=self.attempt if run_attempt is None else run_attempt, head_sha=self.workflow_sha)
        run_artifacts = [dict(item) for item in self.run_artifacts]
        record = dict(self.record if record is None else record)
        record_path = self.root / 'release-verification-evidence.json'
        record_path.write_text(json.dumps(record), encoding='utf-8')
        archive_path = self.root / 'evidence.zip'
        with zipfile.ZipFile(archive_path, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.write(record_path, verify_source.EVIDENCE_FILE)
        digest = 'sha256:' + hashlib.sha256(archive_path.read_bytes()).hexdigest()
        evidence_artifact_id = 999
        evidence_artifact = dict(id=evidence_artifact_id,
            name=verify_source.evidence_artifact_name(self.candidate), size_in_bytes=archive_path.stat().st_size,
            digest=digest, expired=False, expires_at=(self.now + timedelta(days=20)).isoformat(),
            workflow_run={'id': self.run_id, 'head_sha': self.workflow_sha})
        archive_listing = run_artifacts + [evidence_artifact]
        artifacts_path = self.root / 'repository-artifacts.json'
        artifacts_path.write_text(json.dumps({'artifacts': [evidence_artifact]}), encoding='utf-8')
        runs_dir = self.root / 'runs'; runs_dir.mkdir(exist_ok=True)
        (runs_dir / f'{self.run_id}.json').write_text(json.dumps(run), encoding='utf-8')
        (runs_dir / f'{self.run_id}.artifacts.json').write_text(json.dumps({'artifacts': archive_listing}), encoding='utf-8')
        evidence_dir = self.root / 'evidence'; evidence_dir.mkdir(exist_ok=True)
        (evidence_dir / f'{evidence_artifact_id}.zip').write_bytes(archive_path.read_bytes())
        return artifacts_path, runs_dir, evidence_dir

    def verify(self, candidate=None, record=None, *, run_attempt=None):
        artifacts, runs, evidence = self.save_candidate(record, run_attempt=run_attempt)
        return verify_source.verify_release_source(candidate or self.candidate, self.repo, 'master',
            self.root / 'workflow.json', artifacts, runs, evidence, self.now)

    def test_exact_source_and_full_test_evidence_are_accepted(self):
        (self.root / 'workflow.json').write_text(json.dumps({'id': 77, 'path': verify_source.WORKFLOW_PATH}), encoding='utf-8')
        result = self.verify()
        self.assertTrue(result['accepted'], result)
        self.assertEqual(self.run_id, result['selected']['runId'])

    def test_wrong_resolved_source_is_rejected(self):
        (self.root / 'workflow.json').write_text(json.dumps({'id': 77, 'path': verify_source.WORKFLOW_PATH}), encoding='utf-8')
        other = dict(self.record)
        other['source'] = dict(self.record['source'], resolvedCommit='c' * 40)
        result = self.verify(record=other)
        self.assertFalse(result['accepted'])

    def test_superseded_run_attempt_is_rejected(self):
        (self.root / 'workflow.json').write_text(json.dumps({'id': 77, 'path': verify_source.WORKFLOW_PATH}), encoding='utf-8')
        result = self.verify(run_attempt=self.attempt + 1)
        self.assertFalse(result['accepted'])


    def test_nested_api_evidence_and_existing_top_level_cohorts(self):
        self.assertEqual(self.record['testCohorts'], dict(
            api=dict(executed=10, passed=10, failed=0, skipped=0),
            domain=dict(executed=1, passed=1, failed=0, skipped=0),
            e2e=dict(executed=1, passed=1, failed=0, skipped=0)))

    def test_api_evidence_refuses_partial_stale_duplicate_and_failed_shapes(self):
        def update(path, transform):
            value = json.loads(path.read_text()); transform(value); path.write_text(json.dumps(value))
        def changed_result(output, outcome=None):
            path = output / 'shard-000/results.trx'
            tree = ET.parse(path); results = tree.getroot().find('t:Results', shards.NS)
            if outcome:
                results[0].set('outcome', outcome)
            else:
                results.append(copy.deepcopy(results[0]))
            tree.write(path)
            update(output / 'coverage.json', lambda c: c['shards'][0].update(trxSha256=shards.digest(path)))
        def changed_event(output):
            path = output / 'shard-000/events.json'
            update(path, lambda e: e['results'][0].update(id='unexpected'))
            update(output / 'coverage.json', lambda c: c['shards'][0].update(eventsSha256=shards.digest(path)))
        def aborted(output):
            path = output / 'shard-000/results.trx'
            tree = ET.parse(path); tree.getroot().find('t:ResultSummary', shards.NS).set('outcome', 'Aborted'); tree.write(path)
            update(output / 'coverage.json', lambda c: c['shards'][0].update(trxSha256=shards.digest(path)))
        def bad_binary(output):
            path = output / 'binary-hashes.json'; path.write_text('{}')
            update(output / 'coverage.json', lambda c: c.update(binaryManifestSha256=shards.digest(path)))
        mutations = {
            'missing manifest': lambda p: (p / 'coverage.json').unlink(),
            'malformed manifest': lambda p: (p / 'coverage.json').write_text('{'),
            'legacy top-level only': lambda p: shutil.rmtree(p),
            'partial status': lambda p: update(p / 'coverage.json', lambda c: c.update(status='FAILED')),
            'wrong source': lambda p: update(p / 'source.json', lambda c: c.update(resolvedCommit='c' * 40)),
            'wrong run': lambda p: update(p / 'source.json', lambda c: c.update(runId=999)),
            'future attempt': lambda p: update(p / 'source.json', lambda c: c.update(runAttempt=3)),
            'wrong target framework': lambda p: update(p / 'source.json', lambda c: c.update(targetFramework='net11.0')),
            'self-selected SDK': lambda p: (update(p / 'source.json', lambda c: c.update(dotnetSdk='11.0.100')),
                                            update(p / 'coverage.json', lambda c: c.update(sdk='11.0.100'))),
            'wrong repository': lambda p: update(p / 'source.json', lambda c: c.update(repository='other/repo')),
            'partial shard list': lambda p: update(p / 'coverage.json', lambda c: c['shards'].pop()),
            'duplicate shard index': lambda p: update(p / 'coverage.json', lambda c: c['shards'][1].update(index=0)),
            'wrong total': lambda p: update(p / 'coverage.json', lambda c: c.update(discovered=9)),
            'wrong inventory': lambda p: update(p / 'inventory.json', lambda c: c.pop()),
            'wrong assignment': lambda p: update(p / 'assignments.json', lambda c: c[0].append(c[0][0])),
            'missing trx': lambda p: (p / 'shard-000/results.trx').unlink(),
            'linked evidence': lambda p: (p / 'linked').symlink_to(p / 'source.json'),
            'extra trx': lambda p: shutil.copyfile(p / 'shard-000/results.trx', p.parent / 'stale.trx'),
            'wrong digest': lambda p: update(p / 'coverage.json', lambda c: c['shards'][0].update(trxSha256='0' * 64)),
            'duplicate result with matching digest': lambda p: changed_result(p),
            'skipped result with matching digest': lambda p: changed_result(p, 'NotExecuted'),
            'failed result with matching digest': lambda p: changed_result(p, 'Failed'),
            'failed shard exit': lambda p: update(p / 'shard-000.exit.json', lambda c: c.update(exit=1)),
            'undrained shard': lambda p: update(p / 'shard-000.exit.json', lambda c: c.update(groupDrained=False)),
            'callback mismatch with matching digest': changed_event,
            'aborted summary with matching digest': aborted,
            'RSS exceeded': lambda p: update(p / 'shard-000.exit.json', lambda c: c.update(peakRssBytes=shards.RSS_LIMIT + 1)),
            'deadline exceeded': lambda p: update(p / 'shard-000.exit.json', lambda c: c.update(seconds=1291)),
            'NaN seconds': lambda p: update(p / 'shard-000.exit.json', lambda c: c.update(seconds=float('nan'))),
            'missing seconds': lambda p: update(p / 'shard-000.exit.json', lambda c: c.pop('seconds')),
            'empty binary manifest with matching digest': bad_binary,
            'missing helper': lambda p: (p / 'helper-source/Discover.cs').unlink(),
            'changed helper': lambda p: (p / 'helper-source/Discover.cs').write_text('changed'),
            'missing Linux proof': lambda p: (p / 'supervisor-checks/cancel.exit.json').unlink(),
        }
        for label, mutate in mutations.items():
            with self.subTest(label=label):
                shutil.rmtree(self.api_folder); self.api_folder.mkdir()
                output = api_fixture(self.api_folder, self.repo, self.candidate, self.run_id, 1)
                mutate(output)
                _, errors = verify_source.build_record(
                    self.repo, f'{self.repo}/{verify_source.WORKFLOW_PATH}@refs/heads/master', self.workflow_sha,
                    self.run_id, self.attempt, 'workflow_dispatch', self.workflow_sha, 'master', self.candidate,
                    {job: 'success' for job in verify_source.REQUIRED_JOBS}, self.trx_root, self.run_artifacts, self.now, toolchain=self.toolchain)
                self.assertIn('api shard evidence is missing, malformed, incomplete or unbound', errors)


    def test_api_artifact_root_symlink_is_refused(self):
        alias = self.root / 'linked-api'
        alias.symlink_to(self.api_folder, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'artifact root'):
            shards.retained_count(alias, self.repo, self.candidate, self.run_id, self.attempt,
                                  expected_sdk="10.0.300", expected_target_framework="net10.0")


    def test_source_checkout_pins_framework_and_sdk_without_an_allowlist(self):
        root = self.root / 'source-contract'; root.mkdir()
        project = root / 'tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj'
        project.parent.mkdir(parents=True); project.write_text('<Project><PropertyGroup /></Project>')
        for target, sdk in [('net10.0', '10.0.300'), ('net11.0', '11.0.100')]:
            (root / 'global.json').write_text(json.dumps(dict(sdk=dict(version=sdk, rollForward='disable', allowPrerelease=False))))
            (root / 'Directory.Build.props').write_text(f'<Project><PropertyGroup><TargetFramework>{target}</TargetFramework></PropertyGroup></Project>')
            contract = verify_source.source_toolchain(root, self.candidate)
            self.assertEqual(dict(sourceSha=self.candidate, dotnetSdk=sdk, targetFramework=target), contract)
            project.write_text(f'<Project><PropertyGroup><TargetFramework>{target}</TargetFramework></PropertyGroup></Project>')
            self.assertEqual(contract, verify_source.source_toolchain(root, self.candidate))
            project.write_text('<Project><PropertyGroup /></Project>')
        project.write_text('<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        with self.assertRaisesRegex(ValueError, 'toolchain'):
            verify_source.source_toolchain(root, self.candidate)


    def test_new_framework_requires_matching_source_contract(self):
        output = self.api_folder / 'api-shards'
        source = json.loads((output / 'source.json').read_text())
        source.update(targetFramework='net11.0', dotnetSdk='11.0.100')
        (output / 'source.json').write_text(json.dumps(source))
        coverage = json.loads((output / 'coverage.json').read_text()); coverage['sdk'] = '11.0.100'
        (output / 'coverage.json').write_text(json.dumps(coverage))
        contract = dict(sourceSha=self.candidate, dotnetSdk='11.0.100', targetFramework='net11.0')
        args = (self.repo, f'{self.repo}/{verify_source.WORKFLOW_PATH}@refs/heads/master', self.workflow_sha,
                self.run_id, self.attempt, 'workflow_dispatch', self.workflow_sha, 'master', self.candidate,
                {job: 'success' for job in verify_source.REQUIRED_JOBS}, self.trx_root, self.run_artifacts, self.now)
        record, errors = verify_source.build_record(*args, toolchain=contract)
        self.assertEqual([], errors)
        self.assertEqual(10, record['testCohorts']['api']['passed'])
        for wrong in (self.toolchain, dict(contract, sourceSha='c' * 40)):
            _, errors = verify_source.build_record(*args, toolchain=wrong)
            self.assertIn('api shard evidence is missing, malformed, incomplete or unbound', errors)


if __name__ == '__main__':
    unittest.main()
