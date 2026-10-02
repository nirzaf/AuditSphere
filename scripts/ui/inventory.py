"""Discover source UI controls; discovery is not reviewed parity evidence."""
import json, re, subprocess, hashlib, sys
from pathlib import Path
root = Path(__file__).resolve().parents[2]
items = []

def destination(path, routes):
    name = path.stem.lower()
    area = path.parent.name.lower()
    if any(route.startswith('/portal') for route in routes):
        story = 'US-038' if 'pbc' in name or 'package' in name else 'US-037'
        return {'feature': 'portal', 'story': story, 'status': 'PROPOSED_PENDING_PARITY_REVIEW'}
    families = [
        ('administration', ('administration','microsoft365','tenantconnection','accessnotassigned'), 'US-039'),
        ('practice/portfolio', ('portfolio','clientdetail','engagementdetail'), 'US-015'),
        ('practice/commercial', ('lead','proposal','quotation','feeagreement','commercial'), 'US-016'),
        ('acceptance', ('assessment','acceptance'), 'US-017'),
        ('practice/resources', ('resource','staffing','budget'), 'US-018'),
        ('practice/finance', ('practicetime','finance','invoice','firmbooks'), 'US-019'),
        ('accounting/setup', ('accountingworkspace','accountingperiod','rollforward','restatement'), 'US-020'),
        ('accounting/intake', ('accountingrecords','trialbalanceintake'), 'US-021'),
        ('accounting/mappings', ('mapping',), 'US-022'),
        ('accounting/statements', ('statementdrilldown',), 'US-023'),
        ('accounting/journals', ('journal','auditdifference'), 'US-024'),
        ('accounting/evidence', ('accountingevidence','accountingfinding'), 'US-025'),
        ('accounting/currency', ('remeasurement',), 'US-026'),
        ('consolidation', ('advancedconsolidation',), 'US-028'),
        ('consolidation', ('consolidation',), 'US-027'),
        ('reviews/packages', ('financialpackage','financialreview'), 'US-029'),
        ('audit/planning', ('auditplan','materiality','riskrouting'), 'US-030'),
        ('audit/fieldwork', ('auditfieldwork','population','auditprogramlibrary'), 'US-031'),
        ('audit/fieldwork', ('fieldworktools',), 'US-032'),
        ('audit/confirmations', ('confirmation',), 'US-033'),
        ('audit/review', ('workpaper','reviewpoint','finding','reviewnote'), 'US-034'),
        ('completion', ('completion','deliverable'), 'US-035'),
        ('records', ('release','archive','filerecords'), 'US-036'),
        ('portal', ('clientportal','delegation'), 'US-037'),
        ('documents/pbc', ('pbc',), 'US-038'),
        ('practice/analytics', ('technicallibrary','practiceanalytics','projectprogress'), 'US-040'),
        ('operations', ('operation',), 'US-041'),
        ('shell/search', ('globalsearch',), 'US-012'),
    ]
    for feature, patterns, story in families:
        if any(pattern in name or pattern == area for pattern in patterns):
            return {'feature': feature, 'story': story, 'status': 'PROPOSED_PENDING_PARITY_REVIEW'}
    return {'feature': 'shell/shared', 'story': 'US-005', 'status': 'PROPOSED_PENDING_PARITY_REVIEW'}

for path in sorted((root / 'src/AuditSphereOps.Web/Components').rglob('*.razor')):
    source = path.read_text()
    routes = re.findall(r'@page\s+"([^"]+)"', source)
    events = sorted(set(re.findall(r'(?:OnClick|OnSubmit|OnValidSubmit|@onclick|@onsubmit)\s*=\s*"([^"]+)"', source)))
    if not routes and not events: continue
    items.append(dict(source=str(path.relative_to(root)), sourceSha256=hashlib.sha256(source.encode()).hexdigest(), routes=routes, actions=events,
        injectedServices=re.findall(r'@inject\s+(\S+)\s+(\w+)', source),
        links=sorted(set(re.findall(r'(?:Href|href)="([^"]+)"', source))),
        asyncCalls=sorted(set(re.findall(r'\b(\w+Async)\s*\(', source))),
        draftHooks=sorted(set(re.findall(r'data-draft-[\w-]+|draft-state|reconnect-state|pbc-upload', source))),
        authorizationLines=[line.strip() for line in source.splitlines() if 'Authoriz' in line or '.Roles' in line],
        currentOwner='Blazor',
        destination=destination(path, routes), status='DISCOVERED_NOT_REVIEWED'))
out = root / 'docs/execution/angular-source-inventory.json'
if '--check' in sys.argv:
    if json.loads(out.read_text())['items'] != json.loads(json.dumps(items)):
        raise SystemExit('UI inventory is stale; regenerate and review affected source actions.')
    print('PASS: source discovery inventory matches current Razor source.')
    raise SystemExit(0)
out.write_text(json.dumps({'schemaVersion':1, 'sourceCommit':subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(), 'coverage':'Source discovery only; dynamic actions, scope and parity tests require manual review.', 'items':items}, indent=2)+'\n')
print(f'Discovered {len(items)} pages/action components')
