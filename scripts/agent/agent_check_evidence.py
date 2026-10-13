"""Pure, bounded evidence parsing and pilot comparison. No commands or database access."""
import math
import re
import xml.etree.ElementTree as ET


def sanitize(text):
    text = re.sub(r'\x1b\[[0-9;]*m', '', text)
    text = re.sub(r'-----BEGIN [^-]*PRIVATE KEY-----.*?-----END [^-]*PRIVATE KEY-----',
                  '[private key redacted]', text, flags=re.S)
    text = re.sub(r'(?im)^.*(?:host|server)\s*=[^\n]+', '[connection redacted]', text)
    text = re.sub(r'(?i)\b(Bearer|Basic)\s+[^\s,;]+', r'\1 [redacted]', text)
    text = re.sub(r'''(?i)((?:password|secret|token|authorization|api[_-]?key|installation[_ -]?proof)\s*["']?\s*[:=]\s*)(?:"[^"\n]*"|'[^'\n]*'|[^\s,;]+)''',
                  r'\1[redacted]', text)
    text = re.sub(r'https?://[^\s]+', '[URL redacted]', text)
    text = re.sub(r'[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}', '[email redacted]', text)
    return re.sub(r'\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '[token redacted]', text)


def failure_excerpt(output):
    # Keep nearby Expected/Actual lines even when the assertion heading is elsewhere.
    lines = sanitize(output).splitlines()
    selected = set()
    for i, line in enumerate(lines):
        if re.search(r'(?i)fail|error|assert|expected|actual|request.?id|trace.?id', line):
            selected.update(range(max(0, i - 1), min(len(lines), i + 3)))
    return [lines[i].strip()[:800] for i in sorted(selected)[:12]]


def request_ids(text):
    # Values only, bounded; never return headers, URLs or whole log lines.
    return sorted(set(re.findall(r'(?i)\b(?:request[ _-]?id|trace[ _-]?id)\s*[:=]\s*([a-z0-9:-]{8,128})\b', text)))[:8]


def parse_trx(directory):
    evidence = dict(executedTests=0, passedTests=0, failedTests=0, skippedTests=0, failures=[], resultErrors=[])
    files = sorted(directory.glob('*.trx'))
    if not files:
        evidence['resultErrors'].append('No TRX result was emitted.')
    for file in files:
        try:
            root = ET.parse(file).getroot()
            counters = [n for n in root.iter() if n.tag.rsplit('}', 1)[-1] == 'Counters']
            if len(counters) != 1:
                raise ValueError('Expected one result counter.')
            c = counters[0].attrib
            executed, passed, failed = (int(c[k]) for k in ('executed', 'passed', 'failed'))
            if min(executed, passed, failed) < 0 or passed + failed > executed:
                raise ValueError('Inconsistent result counters.')
            evidence['executedTests'] += executed
            evidence['passedTests'] += passed
            evidence['failedTests'] += failed
            rows = [n for n in root.iter() if n.tag.rsplit('}', 1)[-1] == 'UnitTestResult']
            evidence['skippedTests'] += sum(r.get('outcome') == 'NotExecuted' for r in rows)
            if len([r for r in rows if r.get('outcome') == 'Passed']) != passed:
                raise ValueError('Passing results do not match counters.')
            for row in rows:
                if row.get('outcome') in ('Passed', 'NotExecuted'):
                    continue
                if len(evidence['failures']) >= 8:
                    break
                def contents(name):
                    return '\n'.join(''.join(n.itertext()) for n in row.iter()
                                     if n.tag.rsplit('}', 1)[-1] == name)
                message = sanitize(contents('Message'))
                logs = sanitize(contents('StdOut') + '\n' + contents('StdErr'))
                evidence['failures'].append({
                    'test': sanitize(row.get('testName', 'Unknown test'))[:300],
                    'outcome': sanitize(row.get('outcome', 'Unknown'))[:40],
                    'assertion': message[:1600],
                    'stackTrace': sanitize(contents('StackTrace'))[:1600],
                    'requestIds': request_ids(message + '\n' + logs),
                    'logExcerpt': failure_excerpt(logs)
                })
        except (OSError, ET.ParseError, KeyError, ValueError):
            evidence['resultErrors'].append('Malformed or inconsistent TRX evidence; inspect the test command locally.')
    return evidence


METRICS = ('discoverySeconds', 'editingSeconds', 'preflightSeconds', 'testWaitSeconds',
           'environmentFailures', 'reworkCount', 'acceptanceRegressions')


def finite_nonnegative(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and value >= 0


def compare_reports(baseline, candidate):
    """Describe measured deltas for identical cohorts; never infer a causal speedup."""
    reasons = []
    for label, report in [('baseline', baseline), ('candidate', candidate)]:
        if report.get('schemaVersion') != 2 or not report.get('cohort'):
            reasons.append(label + ' needs a current runner report with an explicit cohort.')
        if report.get('status') != 'FOCUSED_CHECKS_PASSED' or not report.get('sourceUnchangedDuringRun'):
            reasons.append(label + ' must have a stable, passing focused run.')
        if not report.get('measurementContext'):
            reasons.append(label + ' needs --measurement-context naming the comparable task and environment.')
        checks = report.get('checks', [])
        if not checks or len(checks) != len(report.get('cohort', [])) or any(
                r.get('status') != 'PASS' or not finite_nonnegative(r.get('executedTests')) or
                r.get('executedTests') == 0 or r.get('passedTests') != r.get('executedTests') for r in checks):
            reasons.append(label + ' lacks passing executed-test evidence for every selected check.')
    if baseline.get('capability') != candidate.get('capability') or baseline.get('cohort') != candidate.get('cohort'):
        reasons.append('Capability or selected test cohort differs.')
    if baseline.get('measurementContext') != candidate.get('measurementContext'):
        reasons.append('Task/environment measurement contexts differ.')
    if [p.get('command') for p in baseline.get('preparations', [])] != [p.get('command') for p in candidate.get('preparations', [])]:
        reasons.append('Build preparations differ.')
    if [(c.get('name'), c.get('executedTests')) for c in baseline.get('checks', [])] != [(c.get('name'), c.get('executedTests')) for c in candidate.get('checks', [])]:
        reasons.append('Executed test counts differ; runtime measurements would compare different workloads.')
    if reasons:
        return {'status': 'NOT_COMPARABLE', 'reasons': reasons, 'deltas': {}, 'causalImprovement': 'NOT_ESTABLISHED'}
    deltas, missing = {}, []
    for name in METRICS:
        before = baseline.get('metrics', {}).get(name)
        after = candidate.get('metrics', {}).get(name)
        if not finite_nonnegative(before) or not finite_nonnegative(after):
            missing.append(name)
            continue
        deltas[name] = {'baseline': before, 'candidate': after, 'change': round(after - before, 3)}
    return {'status': 'COMPARABLE', 'reasons': [], 'deltas': deltas, 'unmeasured': missing,
            'causalImprovement': 'NOT_ESTABLISHED',
            'note': 'Candidate minus baseline. Discovery, editing, rework and independent acceptance are operator measurements; repeated comparable tasks are needed to assess improvement.'}
