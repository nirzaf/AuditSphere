"""Saved-response tests for exact-source release-verification evidence."""
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
        self.trx_root = self.root / 'trx'
        self.trx_root.mkdir()
        for cohort in verify_source.TEST_COHORTS:
            folder = self.trx_root / f'release-verification-trx-{cohort}'
            folder.mkdir()
            (folder / f'{cohort}.trx').write_text(
                '<TestRun xmlns="urn:trx"><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" skipped="0" /></ResultSummary>'
                '<Results><UnitTestResult testName="synthetic" outcome="Passed" /></Results></TestRun>', encoding='utf-8')
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
            {job: 'success' for job in verify_source.REQUIRED_JOBS}, self.trx_root, self.run_artifacts, self.now)
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


if __name__ == '__main__':
    unittest.main()
