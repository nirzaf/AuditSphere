#!/usr/bin/env python3
"""Release promotion gate: trusted full-regression evidence for one exact source commit.

Standard library only; this module never calls GitHub or any other network service. The workflow
shell owns every `gh api` request and saves the responses; this module only decides.

record  Runs in .github/workflows/release-verification.yml after the aggregate release-gate has
        confirmed that every required job succeeded. It writes one bounded JSON record naming the
        requested ref and the resolved source commit (distinct from the run's dispatch head_sha),
        the workflow definition ref/commit, the run id/attempt, the required job outcomes,
        non-empty TRX counters for every unfiltered .NET test project and the identities of the
        uploaded log/TRX evidence artifacts.

verify  Runs in .github/workflows/release.yml before any protected environment. It consumes the
        designated workflow's metadata, the artifacts named for the candidate commit, each linked
        run and its artifact listing, plus the downloaded evidence archives. A candidate is accepted
        only when a completed, successful schedule/manual run of the designated workflow, defined on
        the trusted branch of this repository, recorded exactly the candidate as its resolved commit
        for the run's current attempt, and its evidence artifacts are still retained. Anything
        missing, malformed, expired, stale or unrelated refuses the release.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import re
import sys
import zipfile

WORKFLOW_PATH = '.github/workflows/release-verification.yml'
SCHEMA = 'auditsphere.release-verification-evidence.v1'
EVIDENCE_ARTIFACT_PREFIX = 'release-verification-evidence-'
EVIDENCE_FILE = 'release-verification-evidence.json'
# Keep aligned with the release-gate `needs` list and artifact names in release-verification.yml.
REQUIRED_JOBS = ('resolve-ref', 'angular', 'dotnet-tests', 'release-contract')
TEST_COHORTS = ('domain', 'api', 'e2e')
REQUIRED_ARTIFACTS = tuple(sorted(
    [f'release-verification-logs-{key}' for key in ('angular', 'contract') + TEST_COHORTS]
    + [f'release-verification-trx-{key}' for key in TEST_COHORTS]))
TRUSTED_EVENTS = ('schedule', 'workflow_dispatch')
MAX_CANDIDATES = 10
MAX_ARCHIVE_BYTES = 256 * 1024
MAX_RECORD_BYTES = 64 * 1024
MAX_REASONS = 12

SHA = re.compile(r'[0-9a-f]{40}')
DIGEST = re.compile(r'sha256:[0-9a-f]{64}')
REPOSITORY = re.compile(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+')
BRANCH = re.compile(r'[A-Za-z0-9._/-]{1,255}')
JOB_RESULT = re.compile(r'([a-z0-9-]+)=([a-z_]*)')


def is_sha(value):
    return isinstance(value, str) and SHA.fullmatch(value) is not None


def is_count(value, minimum=0):
    return isinstance(value, int) and not isinstance(value, bool) and value >= minimum


def evidence_artifact_name(commit):
    return EVIDENCE_ARTIFACT_PREFIX + commit


def load_json_values(path):
    """Read one JSON document or concatenated documents (`gh api --paginate` writes one per page)."""
    text = Path(path).read_text(encoding='utf-8')
    decoder, values, index = json.JSONDecoder(), [], 0
    while True:
        while index < len(text) and text[index].isspace():
            index += 1
        if index >= len(text):
            return values
        value, index = decoder.raw_decode(text, index)
        values.append(value)


def load_document(path):
    values = load_json_values(path)
    if len(values) != 1 or not isinstance(values[0], dict):
        raise ValueError(f'{Path(path).name} is not exactly one JSON object')
    return values[0]


def load_artifacts(path):
    artifacts = []
    for page in load_json_values(path):
        if not isinstance(page, dict) or not isinstance(page.get('artifacts'), list):
            raise ValueError(f'{Path(path).name} is not an artifact listing')
        artifacts.extend(page['artifacts'])
    return artifacts


def parse_time(value):
    if not isinstance(value, str):
        raise ValueError('timestamp is missing')
    parsed = datetime.fromisoformat(value.replace('Z', '+00:00'))
    if parsed.tzinfo is None:
        raise ValueError('timestamp has no zone')
    return parsed


def retained(artifact, now):
    try:
        return artifact.get('expired') is False and parse_time(artifact.get('expires_at')) > now
    except ValueError:
        return False


def record_errors(record):
    """Structural checks shared by the producer self-check and the release-time consumer."""
    if not isinstance(record, dict):
        return ['evidence record is not a JSON object']
    errors = []

    def section(name):
        value = record.get(name)
        if isinstance(value, dict):
            return value
        errors.append(f'evidence record has no {name} object')
        return {}

    if record.get('schema') != SCHEMA:
        errors.append(f'evidence record schema is not {SCHEMA}')
    if not (isinstance(record.get('repository'), str) and REPOSITORY.fullmatch(record['repository'])):
        errors.append('evidence record repository is malformed')
    workflow, run, source = section('workflow'), section('run'), section('source')
    if workflow.get('path') != WORKFLOW_PATH:
        errors.append(f'evidence record does not name {WORKFLOW_PATH}')
    if not isinstance(workflow.get('ref'), str) or not is_sha(workflow.get('sha')):
        errors.append('evidence record workflow ref or commit is malformed')
    if not (is_count(run.get('id'), 1) and is_count(run.get('attempt'), 1)):
        errors.append('evidence record run id or attempt is malformed')
    if run.get('event') not in TRUSTED_EVENTS:
        errors.append('evidence record run event is not a schedule or manual dispatch')
    if not is_sha(run.get('headSha')):
        errors.append('evidence record run head commit is malformed')
    if not is_sha(source.get('resolvedCommit')):
        errors.append('evidence record resolved commit is malformed')
    requested = source.get('requestedRef')
    if not (isinstance(requested, str) and 0 < len(requested) <= 255 and requested.isprintable()):
        errors.append('evidence record requested ref is malformed')

    jobs = section('requiredJobs')
    for job in REQUIRED_JOBS:
        if jobs.get(job) != 'success':
            errors.append(f'required job {job} result is {jobs.get(job)!r}, not success')

    cohorts = section('testCohorts')
    for cohort in TEST_COHORTS:
        counters = cohorts.get(cohort) if isinstance(cohorts.get(cohort), dict) else {}
        executed, passed, failed = (counters.get(key) for key in ('executed', 'passed', 'failed'))
        if not (is_count(executed, 1) and is_count(passed) and is_count(failed)):
            errors.append(f'{cohort} tests have no non-empty execution evidence')
        elif failed or passed != executed:
            errors.append(f'{cohort} tests executed {executed} but passed {passed} and failed {failed}')

    artifacts = record.get('evidenceArtifacts')
    if not isinstance(artifacts, list) or len(artifacts) > 32:
        errors.append('evidence record has no bounded evidenceArtifacts list')
        artifacts = []
    names = set()
    for item in artifacts:
        valid = (isinstance(item, dict) and isinstance(item.get('name'), str)
                 and is_count(item.get('id'), 1) and is_count(item.get('sizeInBytes'), 1)
                 and (item.get('digest') is None
                      or (isinstance(item.get('digest'), str) and DIGEST.fullmatch(item['digest']))))
        if not valid or item['name'] in names:
            errors.append('an evidence artifact identity is malformed or duplicated')
            continue
        names.add(item['name'])
    for name in REQUIRED_ARTIFACTS:
        if name not in names:
            errors.append(f'evidence artifact {name} is not recorded')
    return errors


def load_trx_parser():
    # Reuse the existing bounded TRX counter parser rather than a second implementation.
    path = Path(__file__).resolve().parents[1] / 'agent' / 'agent_check_evidence.py'
    spec = importlib.util.spec_from_file_location('agent_check_evidence', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.parse_trx


def build_record(repository, workflow_ref, workflow_sha, run_id, run_attempt, event, head_sha,
                 requested_ref, resolved_commit, jobs, trx_root, run_artifacts, now=None):
    """Return (record, errors). A record is only meaningful when errors is empty."""
    now = now or datetime.now(timezone.utc)
    errors = []
    if not (isinstance(repository, str) and REPOSITORY.fullmatch(repository)):
        errors.append('repository is malformed')
    if not (isinstance(workflow_ref, str) and workflow_ref.startswith(f'{repository}/{WORKFLOW_PATH}@refs/')):
        errors.append(f'workflow ref does not name {WORKFLOW_PATH} in {repository}')
    for label, value in (('workflow commit', workflow_sha), ('run head commit', head_sha),
                         ('resolved commit', resolved_commit)):
        if not is_sha(value):
            errors.append(f'{label} is not a full lowercase 40-character SHA')
    if set(jobs) != set(REQUIRED_JOBS):
        errors.append('required job outcomes must be exactly: ' + ', '.join(REQUIRED_JOBS))
    for job in REQUIRED_JOBS:
        if job in jobs and jobs[job] != 'success':
            errors.append(f'required job {job} result is {jobs[job]!r}, not success')

    parse_trx = load_trx_parser()
    cohorts = {}
    for cohort in TEST_COHORTS:
        evidence = parse_trx(Path(trx_root) / f'release-verification-trx-{cohort}')
        if evidence['resultErrors']:
            errors.append(f'{cohort} TRX evidence is missing or malformed')
        cohorts[cohort] = dict(executed=evidence['executedTests'], passed=evidence['passedTests'],
                               failed=evidence['failedTests'], skipped=evidence['skippedTests'])

    identities = []
    for name in REQUIRED_ARTIFACTS:
        listed = [a for a in run_artifacts if isinstance(a, dict) and a.get('name') == name
                  and is_count(a.get('id'), 1) and retained(a, now)]
        if not listed:
            errors.append(f'evidence artifact {name} was not uploaded by this run')
            continue
        newest = max(listed, key=lambda a: a['id'])
        identities.append(dict(name=name, id=newest['id'], sizeInBytes=newest.get('size_in_bytes'),
                               digest=newest.get('digest')))

    record = dict(
        schema=SCHEMA, repository=repository,
        workflow=dict(path=WORKFLOW_PATH, ref=workflow_ref, sha=workflow_sha),
        run=dict(id=run_id, attempt=run_attempt, event=event, headSha=head_sha),
        source=dict(requestedRef=requested_ref, resolvedCommit=resolved_commit),
        requiredJobs={job: jobs.get(job) for job in REQUIRED_JOBS},
        testCohorts=cohorts, evidenceArtifacts=identities)
    for error in record_errors(record):
        if error not in errors:
            errors.append(error)
    return record, errors


def read_record(archive_path, digest):
    """Return (record, reasons) from one downloaded artifact archive, bounded and digest-checked."""
    try:
        if archive_path.stat().st_size > MAX_ARCHIVE_BYTES:
            return None, ['evidence archive exceeds its size bound']
        data = archive_path.read_bytes()
    except OSError:
        return None, ['evidence archive was not downloaded']
    if digest is not None:
        if not (isinstance(digest, str) and DIGEST.fullmatch(digest)):
            return None, ['evidence artifact digest is malformed']
        if 'sha256:' + hashlib.sha256(data).hexdigest() != digest:
            return None, ['evidence archive does not match the artifact digest']
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            members = archive.infolist()
            if len(members) != 1 or members[0].filename != EVIDENCE_FILE:
                return None, [f'evidence archive must contain only {EVIDENCE_FILE}']
            with archive.open(members[0]) as member:
                content = member.read(MAX_RECORD_BYTES + 1)
        if len(content) > MAX_RECORD_BYTES:
            return None, ['evidence record exceeds its size bound']
        return json.loads(content.decode('utf-8')), []
    except (zipfile.BadZipFile, zipfile.LargeZipFile, NotImplementedError, RuntimeError, EOFError,
            OSError, UnicodeDecodeError, ValueError):
        return None, ['evidence archive or record is malformed']


def run_reasons(repository, trusted_branch, workflow, run, run_id):
    reasons = []
    if run.get('id') != run_id:
        reasons.append('workflow run metadata does not belong to the evidence artifact')
    if run.get('workflow_id') != workflow['id'] or str(run.get('path', '')).split('@', 1)[0] != WORKFLOW_PATH:
        reasons.append(f'run is not the designated {WORKFLOW_PATH} workflow')
    owners = [run[key].get('full_name') if isinstance(run.get(key), dict) else None
              for key in ('repository', 'head_repository')]
    if owners != [repository, repository]:
        reasons.append('run did not execute this repository\'s own workflow source')
    if run.get('event') not in TRUSTED_EVENTS:
        reasons.append(f'run event {run.get("event")!r} is not a schedule or manual dispatch')
    if run.get('head_branch') != trusted_branch:
        reasons.append(f'run workflow definition came from {run.get("head_branch")!r}, not {trusted_branch!r}')
    if run.get('status') != 'completed':
        reasons.append(f'run status is {run.get("status")!r}, not completed')
    elif run.get('conclusion') != 'success':
        reasons.append(f'run concluded {run.get("conclusion")!r}, not success')
    if not is_count(run.get('run_attempt'), 1) or not is_sha(run.get('head_sha')):
        reasons.append('run attempt or head commit is malformed')
    return reasons


def binding_reasons(candidate_sha, repository, trusted_branch, record, run):
    reasons = []
    resolved = record['source']['resolvedCommit']
    if resolved != candidate_sha:
        reasons.append(f'recorded resolved commit {resolved} is not the release candidate')
    if record['repository'] != repository:
        reasons.append('record names a different repository')
    if record['workflow']['ref'] != f'{repository}/{WORKFLOW_PATH}@refs/heads/{trusted_branch}':
        reasons.append('record was not produced by the trusted branch copy of the workflow')
    if record['workflow']['sha'] != run.get('head_sha') or record['run']['headSha'] != run.get('head_sha'):
        reasons.append('recorded workflow commit differs from the run head commit')
    if record['run']['id'] != run.get('id'):
        reasons.append(f'record was produced by run {record["run"]["id"]}, not run {run.get("id")}')
    if record['run']['attempt'] != run.get('run_attempt'):
        reasons.append(f'record is from attempt {record["run"]["attempt"]}, '
                       f'not the run\'s current attempt {run.get("run_attempt")}')
    if record['run']['event'] != run.get('event'):
        reasons.append('recorded run event differs from the run')
    return reasons


def artifact_identity_reasons(record, run_artifacts, now):
    reasons = []
    by_id = {a.get('id'): a for a in run_artifacts if isinstance(a, dict) and is_count(a.get('id'), 1)}
    for item in record['evidenceArtifacts']:
        if item['name'] not in REQUIRED_ARTIFACTS:
            continue
        listed = by_id.get(item['id'])
        if listed is None or listed.get('name') != item['name']:
            reasons.append(f'recorded evidence artifact {item["name"]} is not retained by the run')
        elif not retained(listed, now):
            reasons.append(f'recorded evidence artifact {item["name"]} has expired')
        elif listed.get('size_in_bytes') != item['sizeInBytes'] or (
                item['digest'] is not None and listed.get('digest') != item['digest']):
            reasons.append(f'recorded evidence artifact {item["name"]} identity has changed')
    return reasons


def candidate_reasons(candidate_sha, repository, trusted_branch, workflow, artifact, runs_dir,
                      evidence_dir, now):
    """Return (reasons, run) for one evidence artifact; the candidate is valid only without reasons."""
    reasons = []
    linked = artifact.get('workflow_run') if isinstance(artifact.get('workflow_run'), dict) else {}
    run_id = linked.get('id')
    if not retained(artifact, now):
        reasons.append('evidence artifact has expired or has no known retention')
    if not is_count(artifact.get('size_in_bytes'), 1) or artifact['size_in_bytes'] > MAX_ARCHIVE_BYTES:
        reasons.append('evidence artifact is empty or exceeds its size bound')
    if not is_count(run_id, 1):
        return reasons + ['evidence artifact is not linked to a workflow run'], None
    try:
        run = load_document(Path(runs_dir) / f'{run_id}.json')
        run_artifacts = load_artifacts(Path(runs_dir) / f'{run_id}.artifacts.json')
    except (OSError, ValueError):
        return reasons + ['workflow run metadata for the evidence is unavailable'], None
    reasons += run_reasons(repository, trusted_branch, workflow, run, run_id)
    if linked.get('head_sha') != run.get('head_sha'):
        reasons.append('evidence artifact and workflow run disagree on the run head commit')
    record, archive_reasons = read_record(Path(evidence_dir) / f'{artifact["id"]}.zip', artifact.get('digest'))
    reasons += archive_reasons
    if record is None:
        return reasons, run
    structural = record_errors(record)
    if structural:
        return reasons + structural, run
    reasons += binding_reasons(candidate_sha, repository, trusted_branch, record, run)
    reasons += artifact_identity_reasons(record, run_artifacts, now)
    return reasons, run


def verify_release_source(candidate_sha, repository, trusted_branch, workflow_path, artifacts_path,
                          runs_dir, evidence_dir, now=None):
    now = now or datetime.now(timezone.utc)
    result = dict(accepted=False, candidateSha=candidate_sha, selected=None, candidates=[], errors=[])
    if not is_sha(candidate_sha):
        result['errors'].append('release candidate is not a full lowercase 40-character commit SHA')
    if not (isinstance(repository, str) and REPOSITORY.fullmatch(repository)):
        result['errors'].append('repository is malformed')
    if not (isinstance(trusted_branch, str) and BRANCH.fullmatch(trusted_branch)):
        result['errors'].append('trusted workflow branch is missing or malformed')
    if result['errors']:
        return result
    try:
        workflow = load_document(workflow_path)
        artifacts = load_artifacts(artifacts_path)
    except (OSError, ValueError):
        result['errors'].append('saved GitHub workflow or artifact listing is unreadable')
        return result
    if not is_count(workflow.get('id'), 1) or workflow.get('path') != WORKFLOW_PATH:
        result['errors'].append(f'designated workflow metadata does not describe {WORKFLOW_PATH}')
        return result

    name = evidence_artifact_name(candidate_sha)
    matching = sorted((a for a in artifacts if isinstance(a, dict) and a.get('name') == name
                       and is_count(a.get('id'), 1)), key=lambda a: a['id'], reverse=True)
    if not matching:
        result['errors'].append(f'no {name} artifact exists; run {WORKFLOW_PATH} for this exact commit')
        return result
    # Newest first; any one valid record for this exact commit may be reused, invalid ones never count.
    for artifact in matching[:MAX_CANDIDATES]:
        reasons, run = candidate_reasons(candidate_sha, repository, trusted_branch, workflow, artifact,
                                         runs_dir, evidence_dir, now)
        linked = artifact.get('workflow_run') if isinstance(artifact.get('workflow_run'), dict) else {}
        result['candidates'].append(dict(artifactId=artifact['id'], runId=linked.get('id'),
                                         reasons=reasons[:MAX_REASONS]))
        if not reasons and result['selected'] is None:
            result['selected'] = dict(commit=candidate_sha, runId=run['id'], runAttempt=run['run_attempt'],
                                      artifactId=artifact['id'], digestVerified=artifact.get('digest') is not None)
    result['accepted'] = result['selected'] is not None
    return result


def summary_lines(result):
    def cell(text):
        return str(text).replace('|', '\\|').replace('\n', ' ')

    lines = ['## Full-regression release evidence', '',
             f'- Candidate commit: `{cell(result["candidateSha"])}`',
             f'- Designated workflow: `{WORKFLOW_PATH}`']
    selected = result['selected']
    if selected:
        lines.append(f'- Result: **accepted**: run {selected["runId"]} attempt {selected["runAttempt"]}, '
                     f'evidence artifact {selected["artifactId"]}')
    else:
        lines.append('- Result: **release refused**')
    lines += [f'- {cell(error)}' for error in result['errors']]
    if result['candidates']:
        lines += ['', '| Evidence artifact | Workflow run | Outcome |', '| --- | --- | --- |']
        for candidate in result['candidates']:
            outcome = '; '.join(candidate['reasons']) or 'accepted'
            lines.append(f'| {candidate["artifactId"]} | {cell(candidate["runId"])} | {cell(outcome)} |')
    return lines


def command_record(args):
    jobs = {}
    for value in args.job:
        match = JOB_RESULT.fullmatch(value)
        if not match or match.group(1) in jobs:
            print(f'::error::Malformed or duplicated --job value: {value!r}', file=sys.stderr)
            return 2
        jobs[match.group(1)] = match.group(2)
    try:
        run_artifacts = load_artifacts(args.run_artifacts)
    except (OSError, ValueError):
        run_artifacts = []
    record, errors = build_record(args.repository, args.workflow_ref, args.workflow_sha, args.run_id,
                                  args.run_attempt, args.event, args.head_sha, args.requested_ref,
                                  args.resolved_commit, jobs, args.trx_root, run_artifacts)
    if errors:
        for error in errors:
            print(f'::error::Release verification evidence refused: {error}', file=sys.stderr)
        return 1
    Path(args.output).write_text(json.dumps(record, indent=2, sort_keys=True) + '\n', encoding='utf-8')
    print(f'Recorded release verification evidence for {args.resolved_commit} '
          f'(run {args.run_id} attempt {args.run_attempt}).')
    return 0


def command_verify(args):
    now = parse_time(args.now) if args.now else None
    result = verify_release_source(args.candidate_sha, args.repository, args.trusted_branch, args.workflow,
                                   args.artifacts, args.runs_dir, args.evidence_dir, now)
    lines = summary_lines(result)
    print('\n'.join(lines))
    if args.summary:
        with open(args.summary, 'a', encoding='utf-8') as summary:
            summary.write('\n'.join(lines) + '\n')
    if not result['accepted']:
        print(f'::error::No trusted successful {WORKFLOW_PATH} evidence exists for '
              f'{args.candidate_sha}; release refused.', file=sys.stderr)
        return 1
    if args.github_output:
        selected = result['selected']
        with open(args.github_output, 'a', encoding='utf-8') as output:
            output.write(f'commit={selected["commit"]}\nrun-id={selected["runId"]}\n'
                         f'run-attempt={selected["runAttempt"]}\nartifact-id={selected["artifactId"]}\n')
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest='command', required=True)

    record = commands.add_parser('record', help='write the aggregate evidence record (release-verification.yml)')
    record.add_argument('--repository', required=True)
    record.add_argument('--workflow-ref', required=True)
    record.add_argument('--workflow-sha', required=True)
    record.add_argument('--run-id', required=True, type=int)
    record.add_argument('--run-attempt', required=True, type=int)
    record.add_argument('--event', required=True)
    record.add_argument('--head-sha', required=True, help='the run metadata head_sha (dispatch head)')
    record.add_argument('--requested-ref', required=True)
    record.add_argument('--resolved-commit', required=True, help='the commit every job checked out')
    record.add_argument('--job', action='append', default=[], help='required job outcome as NAME=RESULT')
    record.add_argument('--trx-root', required=True, type=Path)
    record.add_argument('--run-artifacts', required=True, type=Path)
    record.add_argument('--output', required=True, type=Path)
    record.set_defaults(handler=command_record)

    verify = commands.add_parser('verify', help='accept or refuse a release candidate (release.yml)')
    verify.add_argument('--candidate-sha', required=True)
    verify.add_argument('--repository', required=True)
    verify.add_argument('--trusted-branch', required=True)
    verify.add_argument('--workflow', required=True, type=Path)
    verify.add_argument('--artifacts', required=True, type=Path)
    verify.add_argument('--runs-dir', required=True, type=Path)
    verify.add_argument('--evidence-dir', required=True, type=Path)
    verify.add_argument('--now', help='ISO-8601 evaluation time; defaults to the current UTC time')
    verify.add_argument('--github-output', type=Path)
    verify.add_argument('--summary', type=Path)
    verify.set_defaults(handler=command_verify)

    args = parser.parse_args(argv)
    return args.handler(args)


if __name__ == '__main__':
    sys.exit(main())
