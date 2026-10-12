#!/usr/bin/env python3
"""Bounded task packets and opt-in focused verification; no provisioning or reset commands."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import shlex
import signal
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / 'docs/testing/agent-check-capabilities.json'


def sanitize(text):
    text = re.sub(r'\x1b\[[0-9;]*m', '', text)
    text = re.sub(r'(?i)(?:host|server)=[^\n]+', '[connection redacted]', text)
    text = re.sub(r'(?i)((?:password|secret|token|authorization|api[_-]?key)\s*[:=]\s*)[^\s,;]+', r'\1[redacted]', text)
    text = re.sub(r'https?://[^\s]+', '[URL redacted]', text)
    text = re.sub(r'[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}', '[email redacted]', text)
    text = re.sub(r'\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '[token redacted]', text)
    return text


def invoke(argv, timeout=15, env=None):
    started = time.monotonic()
    with tempfile.TemporaryFile(mode='w+b') as log:
        try:
            process = subprocess.Popen(argv, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT,
                                       start_new_session=(os.name != 'nt'))
        except OSError:
            return {'exitCode': 127, 'seconds': 0, 'output': 'Command unavailable; check the pinned tool installation.'}
        try:
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            if os.name != 'nt':
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            else:
                process.kill()
            process.wait()
            code = 124
        log.seek(0, os.SEEK_END)
        size = log.tell()
        log.seek(max(0, size - 100000))
        output = sanitize(log.read().decode('utf-8', errors='replace'))
    return {'exitCode': code, 'seconds': round(time.monotonic() - started, 3), 'output': output}


def source_state(paths):
    return {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() if (ROOT / p).is_file() else None for p in paths}


def preflight(packet, checks):
    results = []
    def record(name, ok, action):
        results.append({'name': name, 'status': 'PASS' if ok else 'BLOCKED', 'action': '' if ok else action})
    paths = packet['owningFiles'] + packet['documents'] + [c['testFile'] for c in checks]
    record('Packet references', all((ROOT / p).is_file() for p in paths), 'Update the packet to name existing source, task and test files.')
    kinds = {c['kind'] for c in checks}
    if 'dotnet' in kinds:
        pinned = json.loads((ROOT / 'global.json').read_text())['sdk']['version']
        sdk = invoke(['dotnet', '--version'])
        record('Pinned .NET SDK', sdk['exitCode'] == 0 and sdk['output'].strip() == pinned, 'Install the SDK pinned in global.json; the runner never installs it.')
        projects = {c['project'] for c in checks if c['kind'] == 'dotnet'}
        record('Restored .NET dependencies', all((ROOT / Path(p).parent / 'obj/project.assets.json').is_file() for p in projects), 'Run dotnet restore AuditSphereOps.slnx --locked-mode from the repository root.')
        ef_version = json.loads((ROOT / '.config/dotnet-tools.json').read_text())['tools']['dotnet-ef']['version']
        ef = invoke(['dotnet', 'ef', '--version'])
        record('Pinned EF tool', ef['exitCode'] == 0 and ef_version in ef['output'], 'Run dotnet tool restore from the repository root.')
    if 'angular' in kinds or any(c.get('browser') for c in checks):
        node = invoke(['node', '--version'])
        pinned = (ROOT / 'src/AuditSphereOps.Ui/.nvmrc').read_text().strip()
        record('Pinned Node', node['exitCode'] == 0 and node['output'].strip().lstrip('v') == pinned, 'Select the Node version in src/AuditSphereOps.Ui/.nvmrc.')
        record('Installed Angular dependencies', (ROOT / 'src/AuditSphereOps.Ui/node_modules/@angular/cli/bin/ng.js').is_file(), 'Run npm --prefix src/AuditSphereOps.Ui ci; the runner never installs packages.')
    unsafe = any(os.environ.get(k, '').lower() == 'true' for k in ['ExternalEffects__Enabled', 'DevelopmentIdentity__Enabled'])
    record('Safe test configuration', not unsafe, 'Use a clean local test shell with external effects and development identity disabled.')
    if any(c.get('database') for c in checks):
        connection = os.environ.get('AUDITSPHERE_TEST_CONNECTION', '')
        # Refuse overrides rather than partially parsing credential-bearing Npgsql strings.
        record('Test database target', not connection, 'Unset AUDITSPHERE_TEST_CONNECTION for this runner. It supports only 127.0.0.1:5433/auditsphere_tests with local postgres authentication.')
        pg_home = Path(os.environ.get('PGSQL_HOME', '/opt/homebrew/opt/postgresql@18/bin'))
        psql = shutil.which('psql') or str(pg_home / ('psql.exe' if os.name == 'nt' else 'psql'))
        env = os.environ.copy()
        for k in list(env):
            if k.startswith('PG'):
                env.pop(k, None)
        env['PGCONNECT_TIMEOUT'] = '5'
        db = invoke([psql, '-X', '-w', '-h', '127.0.0.1', '-p', '5433', '-U', 'postgres', '-d', 'auditsphere_tests', '-Atqc', 'show server_version_num'], env=env)
        observed = db['output'].strip() if db['exitCode'] == 0 and db['output'].strip().isdigit() else 'unavailable'
        record('PostgreSQL 18.6 test database', observed == '180006', 'Make the existing PostgreSQL 18.6 auditsphere_tests database reachable on port 5433. No database is created, reset or migrated by preflight.')
        results[-1]['observedVersionNumber'] = observed
    if any(c.get('browser') for c in checks):
        record('Angular browser bundle', (ROOT / 'src/AuditSphereOps.Ui/dist/auditsphere-ui/browser/index.html').is_file(), 'Build the current Angular UI before browser journeys; the fixtures serve the built bundle.')
        record('Chromium selected', os.environ.get('E2E_BROWSER', 'chromium') in ('', 'chromium'), 'Unset E2E_BROWSER or select chromium for this pilot.')
        cache = Path(os.environ.get('PLAYWRIGHT_BROWSERS_PATH', str(Path.home() / ('Library/Caches/ms-playwright' if sys.platform == 'darwin' else '.cache/ms-playwright'))))
        record('Playwright browser installed', cache.is_dir() and any(cache.glob('chromium*')), 'Install the project-pinned Playwright Chromium through the existing E2E setup. The runner never downloads a browser.')
    return results


def execute_check(check, timeout):
    with tempfile.TemporaryDirectory(prefix='auditsphere-agent-check-') as results:
        if check['kind'] == 'dotnet':
            argv = ['dotnet', 'test', check['project'], '--no-restore', '--configuration', 'Release', '--maxcpucount:1', '--filter', check['filter'], '--logger', 'trx', '--results-directory', results]
        else:
            argv = ['npm', '--prefix', 'src/AuditSphereOps.Ui', 'run', 'test:ci', '--', '--include', check['include']]
        env = os.environ.copy()
        env.update({'DOTNET_ENVIRONMENT': 'Test', 'ASPNETCORE_ENVIRONMENT': 'Test', 'ExternalEffects__Enabled': 'false', 'Application__AllowSimulationAdapters': 'true', 'DevelopmentIdentity__Enabled': 'false'})
        run = invoke(argv, timeout, env)
        count = 0
        failures = 0
        if check['kind'] == 'dotnet':
            for file in Path(results).glob('*.trx'):
                try:
                    root = ET.parse(file).getroot()
                except (OSError, ET.ParseError):
                    failures += 1
                    continue
                for node in root.iter():
                    if node.tag.endswith('Counters'):
                        count += int(node.attrib.get('executed', '0'))
                        failures += int(node.attrib.get('failed', '0')) + int(node.attrib.get('error', '0'))
        else:
            matches = re.findall(r'\bTests\s+(\d+) passed', run['output'])
            count = int(matches[-1]) if matches else 0
        evidence = [line.strip()[:800] for line in run['output'].splitlines() if re.search(r'(?i)failed|error|assert|expected|actual|request.?id|trace.?id', line)][:12]
        return {'name': check['name'], 'command': argv[:-1] + ['<TEMP_RESULTS>'] if check['kind'] == 'dotnet' else argv, 'status': 'PASS' if run['exitCode'] == 0 and count > 0 and failures == 0 else 'FAIL', 'exitCode': run['exitCode'], 'executedTests': count, 'failedTests': failures, 'seconds': run['seconds'], 'failureEvidence': evidence, 'diagnostic': 'Timed out; command group terminated.' if run['exitCode'] == 124 else 'No executed-test evidence; zero matches cannot pass.' if count == 0 else ''}


def validate_catalog(catalog):
    if catalog.get('schemaVersion') != 1 or not catalog.get('capabilities'):
        raise ValueError('Unsupported or empty capability catalog.')
    for packet in catalog['capabilities'].values():
        paths = packet['owningFiles'] + packet['documents'] + [f['source'] for f in packet['fixtures']]
        for check in packet['checks']:
            paths.append(check['testFile'])
            if check['kind'] == 'dotnet':
                paths.append(check['project'])
                if check['filter'] != 'FullyQualifiedName~' + Path(check['testFile']).stem:
                    raise ValueError('A .NET pilot must select its named test class.')
            elif check['kind'] == 'angular':
                if check['testFile'] != 'src/AuditSphereOps.Ui/' + check['include']:
                    raise ValueError('Angular test selection must match its packet file.')
            else:
                raise ValueError('Unsupported check kind.')
        for path in paths:
            resolved = (ROOT / path).resolve()
            if Path(path).is_absolute() or not resolved.is_relative_to(ROOT) or not resolved.is_file():
                raise ValueError('Packet references must name existing repository files.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capability', nargs='?')
    parser.add_argument('--list', action='store_true')
    parser.add_argument('--validate-catalog', action='store_true')
    parser.add_argument('--run', action='store_true', help='Opt in to tests; existing fixtures write disposable test databases and build outputs.')
    parser.add_argument('--browser', action='store_true', help='Include mapped synthetic Playwright journeys.')
    parser.add_argument('--layer', choices=['unit', 'api', 'browser'], help='Select one explicit layer instead of the default unit/API cohort.')
    parser.add_argument('--report', type=Path, help='Explicitly save sanitized JSON evidence.')
    parser.add_argument('--json', action='store_true', help='Print machine-readable evidence instead of the compact task packet.')
    parser.add_argument('--timeout', type=int, default=600)
    parser.add_argument('--discovery-seconds', type=float)
    parser.add_argument('--editing-seconds', type=float)
    parser.add_argument('--rework-count', type=int)
    args = parser.parse_args()
    catalog = json.loads(CATALOG.read_text())
    try:
        validate_catalog(catalog)
    except (KeyError, ValueError, OSError) as error:
        parser.error(str(error))
    if args.validate_catalog:
        print(f"Validated {len(catalog['capabilities'])} capability packets and their repository references.")
        return 0
    if args.list:
        print('\n'.join(f"{key}: {value['purpose']}" for key, value in catalog['capabilities'].items()))
        return 0
    if args.capability not in catalog['capabilities']:
        parser.error('Choose a capability from --list.')
    if args.timeout < 1 or any(x is not None and x < 0 for x in [args.discovery_seconds, args.editing_seconds, args.rework_count]):
        parser.error('Timeout must be positive; measurements must be non-negative.')
    packet = catalog['capabilities'][args.capability]
    checks = [c for c in packet['checks'] if (c['layer'] == args.layer if args.layer else args.browser or not c.get('browser'))]
    if not checks:
        parser.error('This packet has no checks in the selected layer.')
    commit = invoke(['git', 'rev-parse', 'HEAD'])['output'].strip()
    dirty = invoke(['git', 'status', '--porcelain'])['output'].splitlines()
    paths = packet['owningFiles'] + [c['testFile'] for c in checks]
    before = source_state(paths)
    tracked = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT).decode().split('\0')
    tracked = [p for p in tracked if Path(p).suffix in ('.cs', '.ts', '.json', '.props', '.csproj', '.slnx', '.py', '.sh', '.md') or p.endswith('.nvmrc')]
    fingerprint_before = source_state(tracked)
    start = time.monotonic()
    prerequisites = preflight(packet, checks)
    preflight_seconds = round(time.monotonic() - start, 3)
    blocked = any(p['status'] != 'PASS' for p in prerequisites)
    runs = []
    if args.run and not blocked:
        for check in checks:
            print(f"Checking {check['name']}…", file=sys.stderr, flush=True)
            runs.append(execute_check(check, args.timeout))
            if runs[-1]['status'] != 'PASS':
                break
    unchanged = fingerprint_before == source_state(tracked) and before == source_state(paths) and commit == invoke(['git', 'rev-parse', 'HEAD'])['output'].strip() and dirty == invoke(['git', 'status', '--porcelain'])['output'].splitlines()
    status = 'BLOCKED_PREREQUISITES' if blocked else 'PREFLIGHT_READY' if not args.run else 'SOURCE_CHANGED' if not unchanged else 'FOCUSED_CHECKS_PASSED' if len(runs) == len(checks) and all(r['status'] == 'PASS' for r in runs) else 'CHECKS_FAILED'
    report = {'capability': args.capability, 'selection': args.layer or ('unit-api-browser' if args.browser else 'unit-api'), 'status': status, 'recordedAtUtc': datetime.now(timezone.utc).isoformat(), 'sourceCommit': commit, 'testedCommit': commit if any(r['executedTests'] > 0 for r in runs) else None, 'workingTreeChanges': dirty, 'sourceUnchangedDuringRun': unchanged, 'sourceHashes': before, 'packet': packet, 'prerequisites': prerequisites, 'checks': runs, 'unexecutedChecks': [c['name'] for c in checks[len(runs):]], 'metrics': {'preflightSeconds': preflight_seconds, 'testWaitSeconds': round(sum(r['seconds'] for r in runs), 3), 'environmentFailures': sum(p['status'] == 'BLOCKED' for p in prerequisites), 'discoverySeconds': args.discovery_seconds, 'editingSeconds': args.editing_seconds, 'reworkCount': args.rework_count}, 'fullReleaseGate': 'NOT_RUN — retain AGENTS.md section 3 and independent acceptance', 'externalAcceptance': 'NOT_VERIFIED — see docs/execution/status.json externalGates', 'assumptions': ['Focused checks do not prove capability completion.', 'Existing owned fixtures create disposable synthetic test databases; preflight never resets or migrates databases.', 'No raw command logs are retained. Failure excerpts are bounded and sanitized; inspect before sharing.']}
    output = json.dumps(report, indent=2) + '\n'
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(output)
    if args.json:
        print(output, end='')
    else:
        print(f"{args.capability}: {status} · source {commit[:12]}")
        print(packet['purpose'])
        for label, values in [('Owning files', packet['owningFiles']), ('Invariants', packet['invariants']), ('Acceptance', packet['acceptanceCriteria']), ('Task and authority references', packet['documents'])]:
            print(label + ':')
            for value in values:
                print('  ' + value)
        for prerequisite in prerequisites:
            print(f"  {prerequisite['status']}: {prerequisite['name']}" + (' — ' + prerequisite['action'] if prerequisite['action'] else ''))
        for check in checks:
            command = ['dotnet', 'test', check['project'], '--no-restore', '--configuration', 'Release', '--maxcpucount:1', '--filter', check['filter']] if check['kind'] == 'dotnet' else ['npm', '--prefix', 'src/AuditSphereOps.Ui', 'run', 'test:ci', '--', '--include', check['include']]
            print('  Selected: ' + shlex.join(command))
        for run in runs:
            print(f"  {run['status']}: {run['name']} · {run['executedTests']} test(s) · {run['seconds']}s")
            for line in run['failureEvidence']:
                print('    ' + line)
        print('Full release gate and live acceptance: NOT_RUN / NOT_VERIFIED')
        if args.report:
            print('Evidence: ' + str(args.report.resolve()))
    return 0 if status in ('PREFLIGHT_READY', 'FOCUSED_CHECKS_PASSED') else 1


if __name__ == '__main__':
    raise SystemExit(main())
