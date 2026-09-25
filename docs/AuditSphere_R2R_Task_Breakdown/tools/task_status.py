#!/usr/bin/env python3
"""Local Markdown task tracking only. No Git, database, cloud or network writes.

Python 3.10+; standard library only. Run from any directory.
Task front matter is the status authority. Acceptance ledgers remain separately
maintained observed results. This helper validates structure, not test execution.
"""
from __future__ import annotations
import argparse
import collections
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlparse

ROOT = Path(__file__).resolve().parents[1]

STATUS_BANNER = '> STATUS: HISTORICAL_SOURCE'


def strip_status_banner(data: bytes) -> bytes:
    """Return the preserved-source payload without its approved status banner.

    The pinned source hashes cover the original requirement text byte-for-byte;
    a visible HISTORICAL_SOURCE banner is approved metadata in front of it and is
    excluded from the hash so the requirement-text pin keeps its meaning.
    """
    text = data.decode('utf-8')
    if not text.startswith(STATUS_BANNER):
        return data
    lines = text.splitlines(keepends=True)
    i = 0
    while i < len(lines) and lines[i].startswith('>'):
        i += 1
    if i < len(lines) and lines[i].strip() == '':
        i += 1
    return ''.join(lines[i:]).encode('utf-8')
STATES = {'NOT_STARTED','IN_PROGRESS','BLOCKED','IN_REVIEW','COMPLETED','REOPENED'}
TRANSITIONS = {
 'NOT_STARTED': {'IN_PROGRESS','BLOCKED'},
 'IN_PROGRESS': {'IN_REVIEW','BLOCKED'},
 'BLOCKED': {'IN_PROGRESS','NOT_STARTED'},
 'IN_REVIEW': {'COMPLETED','IN_PROGRESS','BLOCKED'},
 'COMPLETED': {'REOPENED'},
 'REOPENED': {'IN_PROGRESS','BLOCKED'},
}


def load_manifest():
    return json.loads((ROOT/'tracking/pack_manifest.json').read_text(encoding='utf-8'))


def read_task(path: Path):
    text=path.read_text(encoding='utf-8')
    if not text.startswith('---\n'):
        raise ValueError(f'Missing task front matter: {path}')
    head, body=text[4:].split('\n---\n',1)
    metadata={}
    for row in head.splitlines():
        key, raw=row.split(':',1)
        metadata[key]=json.loads(raw.strip())
    return metadata,body


def write_task(path: Path, metadata, body):
    text='---\n'+'\n'.join(f'{k}: {json.dumps(v,ensure_ascii=False)}' for k,v in metadata.items())+'\n---\n'+body
    temp=path.with_suffix('.md.tmp')
    temp.write_text(text,encoding='utf-8')
    temp.replace(path)


def load_tasks(manifest=None):
    manifest=manifest or load_manifest()
    return {row['id']: (row, *read_task(ROOT/row['file'])) for row in manifest['tasks']}


def readiness(task_id,tasks):
    row,m,_=tasks[task_id]
    missing=[d for d in m['depends_on'] if tasks[d][1]['status']!='COMPLETED']
    if m['status']=='COMPLETED':return 'COMPLETED'
    if missing:return 'WAITING: '+', '.join(missing)
    if m['status']=='BLOCKED':return 'BLOCKED: '+m.get('blocked_reason','Reason required')
    return 'READY FOR OWNER REVIEW' if task_id=='T001' else 'READY'


def task_link(task_id,tasks):
    return f'[{task_id}]({tasks[task_id][0]["file"]})'


def generated_blocks(tasks,manifest):
    c=collections.Counter(m['status'] for _,m,_ in tasks.values())
    counts=['| Tracking measure | Current value |','|---|---:|',f'| Numbered implementation tasks | {len(tasks)} |',f'| Original work packages | {len(manifest["original_work_packages"])} |']
    for state in ['COMPLETED','IN_REVIEW','IN_PROGRESS','BLOCKED','REOPENED','NOT_STARTED']:
        counts.append(f'| {state} | {c[state]} |')
    counts+=['','These are task-tracking totals, not a software-completion percentage or a transferred status from the source repository. Original criterion, fixture and integration acceptance are tracked separately.']
    plan=[]
    for wp in manifest['original_work_packages']:
        rows=[r for r in manifest['tasks'] if r['work_package']==wp['id']]
        complete=sum(tasks[r['id']][1]['status']=='COMPLETED' for r in rows)
        n=wp['id'][-2:]
        plan += [f'<a id="wp-{n}"></a>',f'### {wp["id"]} — {wp["description"]}','',
          f'**Original dependencies:** {wp["depends"]}. **Task gate:** {complete}/{len(rows)} COMPLETED.',
          f'**Original exit evidence:** {wp["exit"]}','',
          '| Done | Task file | Direct dependencies | Status | Readiness | Owner |','|---|---|---|---|---|---|']
        for row in rows:
            m=tasks[row['id']][1];title=tasks[row['id']][2].split('\n',1)[0].removeprefix('# ')
            checkbox='[x]' if m['status']=='COMPLETED' else '[ ]'
            deps=', '.join(task_link(d,tasks) for d in m['depends_on']) or 'Owner/scope review'
            plan.append(f'| {checkbox} | [{title}]({row["file"]}) | {deps} | {m["status"]} | {readiness(row["id"],tasks).replace("|","/")} | {m["owner"] or "Unassigned"} |')
        plan+=['']
    nexts=[k for k,(_,m,_) in tasks.items() if m['status'] in {'NOT_STARTED','REOPENED'} and readiness(k,tasks).startswith('READY')]
    nexttext='\n'.join('- '+task_link(k,tasks)+' — '+tasks[k][2].split('\n',1)[0].split(' — ',1)[-1] for k in nexts) or 'No unstarted task is ready. Finish/review active tasks or resolve the recorded blockers.'
    return {'PROGRESS':'\n'.join(counts),'TASKS':'\n'.join(plan).rstrip(),'NEXT':nexttext}


def refresh():
    manifest=load_manifest();tasks=load_tasks(manifest)
    path=ROOT/'00_INDEX.md';text=path.read_text(encoding='utf-8')
    for name,new in generated_blocks(tasks,manifest).items():
        pattern=rf'<!-- BEGIN {name} -->\n.*?<!-- END {name} -->'
        replacement=f'<!-- BEGIN {name} -->\n{new}\n<!-- END {name} -->'
        text,n=re.subn(pattern,lambda m:replacement,text,flags=re.S)
        if n!=1:raise ValueError(f'Expected one index block: {name}')
    path.write_text(text,encoding='utf-8')


def evidence_exists(value):
    if value.startswith(('https://','http://')):return True
    if not value:return False
    return (ROOT/value.split('#',1)[0]).is_file()


def completion_errors(task_id,tasks):
    _,m,body=tasks[task_id];errors=[]
    for field in ['owner','reviewer','reviewed_commit','evidence_ref']:
        if not str(m.get(field,'')).strip():errors.append(f'{task_id}: {field} must be recorded')
    if m.get('review_decision')!='APPROVED':errors.append(f'{task_id}: review_decision must be APPROVED')
    if m.get('owner','').strip().casefold()==m.get('reviewer','').strip().casefold():errors.append(f'{task_id}: independent reviewer must differ from task owner')
    if not re.fullmatch(r'[0-9a-fA-F]{40}',m.get('reviewed_commit','')):errors.append(f'{task_id}: use the full 40-character reviewed commit SHA')
    if not evidence_exists(m.get('evidence_ref','')):errors.append(f'{task_id}: evidence_ref must be an existing pack-relative file or recorded HTTP(S) evidence URL')
    if task_id=='T001' and not m.get('approval_ref','').strip():errors.append('T001: owner scope/blueprint approval_ref is required')
    section=re.search(r'<!-- COMPLETION-CHECKLIST -->(.*?)<!-- END-COMPLETION-CHECKLIST -->',body,re.S)
    if not section or '- [ ]' in section[1] or len(re.findall(r'- \[[xX]\]',section[1]))!=6:
        errors.append(f'{task_id}: all six completion checklist entries must be checked after real verification')
    for d in m['depends_on']:
        if tasks[d][1]['status']!='COMPLETED':errors.append(f'{task_id}: dependency {d} is not COMPLETED')
    if task_id in {'T051','T054'}:
        ac=(ROOT/'coverage/02_Acceptance_Criteria_Tracking.md').read_text(encoding='utf-8')
        at=(ROOT/'coverage/03_Integration_Journey_Tracking.md').read_text(encoding='utf-8')
        fx=(ROOT/'coverage/04_Golden_Fixture_Tracking.md').read_text(encoding='utf-8')
        if len(re.findall(r'^\| PASS \|',ac,re.M))!=52:errors.append(f'{task_id}: all 52 criteria need separately recorded PASS evidence')
        if len(re.findall(r'^\| PASS \|',at,re.M))!=30:errors.append(f'{task_id}: all 30 journeys need separately recorded PASS evidence')
        if len(re.findall(r'^\| APPROVED \| PASS \|',fx,re.M))!=8:errors.append(f'{task_id}: all 8 fixtures need approved policy and observed PASS evidence')
    return errors


def md_anchors(text):
    result=set(re.findall(r'<a\s+id="([^"]+)"',text))
    seen=collections.Counter()
    for raw in re.findall(r'^#{1,6}\s+(.+)$',text,re.M):
        val=raw.replace('`','').replace('*','').lower()
        val=re.sub(r'[^\w\s-]','',val)
        val=val.replace(' ','-')
        count=seen[val];seen[val]+=1
        result.add(val+(f'-{count}' if count else ''))
    return result


def validate_audit_workflow(manifest, tasks):
    errors = []
    stats = {}
    audit_meta = manifest.get('sources', {}).get('audit_workflow')
    if not audit_meta:
        errors.append('Manifest missing sources.audit_workflow metadata')
        return errors, stats
    audit_file = ROOT / audit_meta['file']
    if not audit_file.is_file():
        errors.append(f"Audit source file missing: {audit_meta['file']}")
        return errors, stats
    audit_bytes = audit_file.read_bytes()
    if not audit_bytes.decode('utf-8').startswith(STATUS_BANNER):
        errors.append(f"{audit_file.name}: preserved source must start with the HISTORICAL_SOURCE status banner")
    actual_sha = hashlib.sha256(strip_status_banner(audit_bytes)).hexdigest()
    if actual_sha != audit_meta['sha256']:
        errors.append(f"Audit source hash mismatch: expected {audit_meta['sha256']}, got {actual_sha}")
    audit_text = audit_file.read_text(encoding='utf-8')
    src_stories = set(re.findall(r'^###\s+(AS-AUD-\d{3})\b', audit_text, re.M))
    src_acs = set(re.findall(r'\b(AS-AUD-\d{3}-AC\d{2})\b', audit_text))
    src_awps = set(re.findall(r'\b(AWP-\d{2}-\d{2})\b', audit_text))
    if len(src_stories) != 28: errors.append(f"Audit source must contain 28 stories, found {len(src_stories)}")
    if len(src_acs) != 258: errors.append(f"Audit source must contain 258 acceptance criteria, found {len(src_acs)}")
    if len(src_awps) != 165: errors.append(f"Audit source must contain 165 AWP procedures, found {len(src_awps)}")
    trace_file = ROOT / 'tracking/AUDIT_WORKFLOW_TRACEABILITY.md'
    if not trace_file.is_file():
        errors.append('Audit traceability ledger missing')
        return errors, stats
    trace_text = trace_file.read_text(encoding='utf-8')
    ac_rows = []
    awp_rows = []
    for line in trace_text.splitlines():
        if line.startswith('| AS-AUD-'):
            ac_rows.append([p.strip() for p in line.split('|')[1:-1]])
        elif line.startswith('| `AWP-'):
            awp_rows.append([p.strip() for p in line.split('|')[1:-1]])
    if len(ac_rows) != 258:
        errors.append(f"Traceability ledger must contain 258 AC rows, found {len(ac_rows)}")
    ledger_acs = [r[1].strip('`') for r in ac_rows]
    if len(set(ledger_acs)) != len(ledger_acs):
        errors.append('Duplicate AC rows in traceability ledger')
    if set(ledger_acs) != src_acs:
        errors.append('Traceability ledger ACs do not match source ACs')
    if len(awp_rows) != 165:
        errors.append(f"Traceability ledger must contain 165 AWP rows, found {len(awp_rows)}")
    ledger_awps = [r[0].strip('`') for r in awp_rows]
    if len(set(ledger_awps)) != len(ledger_awps):
        errors.append('Duplicate AWP rows in traceability ledger')
    if set(ledger_awps) != src_awps:
        errors.append('Traceability ledger AWPs do not match source AWPs')
    disp_counts = collections.Counter(r[4].strip('`') for r in ac_rows)
    valid_disps = {'MERGE_EXISTING', 'NEW_TASK_REQUIRED', 'REFERENCE_ONLY', 'BLOCKED_EXTERNAL'}
    if not set(disp_counts.keys()).issubset(valid_disps):
        errors.append(f"Invalid dispositions found: {set(disp_counts.keys()) - valid_disps}")
    if sum(disp_counts.values()) != 258:
        errors.append(f"Disposition counts sum to {sum(disp_counts.values())}, expected 258")
    for disp, count in disp_counts.items():
        pattern = rf"`{disp}`:\s*{count}\b"
        if not re.search(pattern, trace_text):
            errors.append(f"Traceability summary mismatch for {disp}: expected {count}")
    task_expected_acs = collections.defaultdict(set)
    for r in ac_rows:
        ac = r[1].strip('`')
        tid = r[3].strip('*')
        task_expected_acs[tid].add(ac)
    task_expected_awps = collections.defaultdict(set)
    for r in awp_rows:
        awp = r[0].strip('`')
        tid = r[5].strip('*')
        task_expected_awps[tid].add(awp)
    mapped_tasks = set(task_expected_acs.keys()) | set(task_expected_awps.keys())
    for tid, (row, m, body) in tasks.items():
        trace_sec = re.search(r"## Audit workflow source traceability.*?(?=\n## |\Z)", body, re.DOTALL)
        expected_acs = task_expected_acs.get(tid, set())
        expected_awps = task_expected_awps.get(tid, set())
        if expected_acs or expected_awps:
            if not trace_sec:
                errors.append(f"{tid}: missing ## Audit workflow source traceability section")
                continue
            sec_text = trace_sec.group(0)
            local_acs = set(re.findall(r"\b(AS-AUD-\d{3}-AC\d{2})\b", sec_text))
            if local_acs != expected_acs:
                errors.append(f"{tid}: local AC traceability drift: missing={expected_acs - local_acs}, extra={local_acs - expected_acs}")
            local_primary_awps = set(re.findall(r"\|\s*(AWP-\d{2}-\d{2})\s*\|\s*Covered\s*\|", sec_text))
            if local_primary_awps != expected_awps:
                errors.append(f"{tid}: local primary AWP traceability drift: missing={expected_awps - local_primary_awps}, extra={local_primary_awps - expected_awps}")
        else:
            if trace_sec:
                sec_text = trace_sec.group(0)
                local_acs = set(re.findall(r"\b(AS-AUD-\d{3}-AC\d{2})\b", sec_text))
                local_awps = set(re.findall(r"\|\s*(AWP-\d{2}-\d{2})\s*\|\s*Covered\s*\|", sec_text))
                if local_acs or local_awps:
                    errors.append(f"{tid}: claims unmapped audit ACs={local_acs}, AWPs={local_awps}")
    # Source IDs must preserve the original procedure meaning: every AWP ID a task card cites is
    # anchored to the exact preserved source wording, so a card cannot silently reassign meaning.
    source_procedures = {m.group(1): m.group(2).strip() for m in
                         re.finditer(r'^\| `(AWP-\d{2}-\d{2})` \| ([^|]+?) \|', audit_text, re.M)}
    required_handover_inputs = ['Reviewed-through date', 'Execution/traceability reconciliation', 'Operator handover record']
    for tid, (row, m, body) in tasks.items():
        cited_awps = set(re.findall(r'\b(AWP-\d{2}-\d{2})\b', body))
        drifting = sorted(a for a in cited_awps
                          if a in source_procedures and source_procedures[a].rstrip('.').strip() not in body)
        if drifting:
            errors.append(f"{tid}: source procedure meaning drift for {drifting}")
        # The final audit handover is a human decision gate: it must declare the acceptance
        # inputs it depends on instead of claiming acceptance without evidence.
        if 'AS-AUD-028-AC10' in body:
            gate = re.search(r'## Audit acceptance and handover gate(.*?)(?=\n## |\Z)', body, re.DOTALL)
            if not gate:
                errors.append(f"{tid}: audit acceptance and handover gate must declare its acceptance inputs")
            else:
                missing_inputs = [i for i in required_handover_inputs if i not in gate.group(1)]
                if missing_inputs:
                    errors.append(f"{tid}: audit handover gate missing declared inputs: {missing_inputs}")
                if 'Dependencies:' not in gate.group(1):
                    errors.append(f"{tid}: audit handover gate must declare its dependency gate")
    stats = {
        'audit_source_sha256': actual_sha,
        'audit_stories': len(src_stories),
        'audit_criteria': len(src_acs),
        'audit_procedures': len(src_awps),
        'audit_mapped_tasks': len(mapped_tasks),
        'audit_dispositions': dict(disp_counts)
    }
    return errors, stats


def validate():
    errors=[];manifest=load_manifest();tasks=load_tasks(manifest)
    ids=[r['id'] for r in manifest['tasks']]
    if len(ids)!=len(manifest['tasks']) or len(set(ids))!=len(manifest['tasks']):errors.append(f"Task inventory must contain exactly {len(manifest['tasks'])} distinct IDs")
    source=ROOT/'source/ORIGINAL_R2R_Blueprint_Modules_20-26.md'
    source_bytes=source.read_bytes()
    if not source_bytes.decode('utf-8').startswith(STATUS_BANNER):errors.append('ORIGINAL_R2R_Blueprint_Modules_20-26.md: preserved source must start with the HISTORICAL_SOURCE status banner')
    if hashlib.sha256(strip_status_banner(source_bytes)).hexdigest()!=manifest['source_sha256']:errors.append('Original source hash mismatch')
    source_text=source.read_text(encoding='utf-8')
    source_commands=set(re.findall(r'^\| `(\w+(?:Command|Query))\(',source_text,re.M))
    source_rows={re.match(r'^\| `(\w+(?:Command|Query))\(',line)[1]: line for line in source_text.splitlines() if re.match(r'^\| `(\w+(?:Command|Query))\(',line)}
    assigned=[c for r in manifest['tasks'] for c in r['commands']]
    if len(assigned)!=111 or len(set(assigned))!=111 or set(assigned)!=source_commands:errors.append('Every original request must have exactly one owner')
    if len(manifest['criteria'])!=52 or len(manifest['journeys'])!=30 or len(manifest['fixtures'])!=8:errors.append('Original acceptance inventory mismatch')
    for row,m,body in tasks.values():
        task_id=row['id']
        actual_requests=re.findall(r'^\| `(\w+(?:Command|Query))\(',body,re.M)
        if actual_requests!=row['commands']:errors.append(f'{task_id}: owned request table differs from the registry')
        for command in row['commands']:
            if source_rows[command] not in body:errors.append(f'{task_id}: original contract row changed for {command}')
        if m['id']!=task_id or m['depends_on']!=row['depends_on'] or m['work_package']!=row['work_package']:errors.append(f'{task_id}: dependency/identity drift; revise manifest and index through the coordinator')
        if m['status'] not in STATES:errors.append(f'{task_id}: unsupported status')
        for d in m['depends_on']:
            if d not in tasks:errors.append(f'{task_id}: missing dependency {d}')
            elif ids.index(d)>=ids.index(task_id):errors.append(f'{task_id}: dependency violates the recommended numerical order')
        if m['status']=='COMPLETED':errors+=completion_errors(task_id,tasks)
        if m['status']=='BLOCKED' and not m.get('blocked_reason',''):errors.append(f'{task_id}: blocker reason required')
        if m['status'] in {'IN_PROGRESS','IN_REVIEW'}:
            if any(tasks[d][1]['status']!='COMPLETED' for d in m['depends_on']):errors.append(f'{task_id}: active while a hard dependency is incomplete')
    # Acyclic dependency graph.
    temporary=set();permanent=set()
    def visit(k):
        if k in temporary:raise ValueError(f'Dependency cycle at {k}')
        if k in permanent:return
        temporary.add(k)
        for d in tasks[k][1]['depends_on']:
            if d in tasks:visit(d)
        temporary.remove(k);permanent.add(k)
    try:
        for k in tasks:visit(k)
    except ValueError as ex:errors.append(str(ex))
    # Preserve all original work-package completion prerequisites transitively.
    def ancestors(k):
        out=set()
        for d in tasks[k][1]['depends_on']:
            out.add(d);out.update(ancestors(d))
        return out
    if not any('cycle' in e.lower() for e in errors):
        for wp in manifest['original_work_packages']:
            members=[r['id'] for r in manifest['tasks'] if r['work_package']==wp['id']]
            needed=re.findall(r'R2R-\d{2}',wp['depends'])
            inherited=ancestors(members[0])
            for predecessor in needed:
                for pr in [r['id'] for r in manifest['tasks'] if r['work_package']==predecessor]:
                    if pr not in inherited:errors.append(f'{wp["id"]}: original prerequisite {pr} is not enforced')
    # Validate every local Markdown link and explicit/heading anchor.
    md_files=list(ROOT.rglob('*.md'));cache={p.resolve():p.read_text(encoding='utf-8') for p in md_files}
    anchors={p:md_anchors(t) for p,t in cache.items()};local_count=0
    for path,text in cache.items():
        text=re.sub(r'```.*?```','',text,flags=re.S)
        for match in re.finditer(r'\]\(([^)]+)\)',text):
            target=match[1].strip().split(' "',1)[0]
            if urlparse(target).scheme or target.startswith('//'):continue
            base,_,fragment=target.partition('#');dest=(path.parent/unquote(base)).resolve() if base else path
            local_count+=1
            try:dest.relative_to(ROOT.resolve())
            except ValueError:errors.append(f'{path.name}: link escapes pack: {target}');continue
            if not dest.is_file():errors.append(f'{path.name}: missing link target: {target}')
            elif fragment and unquote(fragment) not in anchors.get(dest,set()):errors.append(f'{path.name}: missing anchor: {target}')
    for x in manifest['criteria']:
        if x['wording'] not in (ROOT/'coverage/02_Acceptance_Criteria_Tracking.md').read_text(encoding='utf-8'):errors.append(f'{x["id"]}: original wording missing')
    index=(ROOT/'00_INDEX.md').read_text(encoding='utf-8')
    for name,expected in generated_blocks(tasks,manifest).items():
        current=re.search(rf'<!-- BEGIN {name} -->\n(.*?)\n<!-- END {name} -->',index,re.S)
        if not current or current[1]!=expected:errors.append(f'Index {name} is stale; run refresh')
    audit_errors, audit_stats = validate_audit_workflow(manifest, tasks)
    errors.extend(audit_errors)
    out_stats = {
        'tasks': len(tasks),
        'work_packages': len(manifest['original_work_packages']),
        'commands_queries': len(source_commands),
        'criteria': 52,
        'journeys': 30,
        'fixtures': 8,
        'markdown_files': len(md_files),
        'local_links_checked': local_count,
        'original_source_sha256': manifest['source_sha256']
    }
    out_stats.update(audit_stats)
    return errors, out_stats


def log_change(task_id,old,new,actor,reason):
    stamp=dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')
    clean=lambda x:str(x).replace('|','/').replace('\n',' ')
    with (ROOT/'tracking/STATUS_HISTORY.md').open('a',encoding='utf-8') as f:
        f.write(f'| {stamp} | {task_id} | {old} | {new} | {clean(actor)} | {clean(reason)} |\n')


def set_status(args):
    manifest=load_manifest();tasks=load_tasks(manifest)
    if args.task not in tasks:raise ValueError(f'Unknown task {args.task}')
    row,m,body=tasks[args.task];m=dict(m);old=m['status'];new=args.status
    if new not in TRANSITIONS[old]:raise ValueError(f'Invalid transition {old} -> {new}')
    for key,attr in [('owner','owner'),('reviewer','reviewer'),('reviewed_commit','commit'),('evidence_ref','evidence'),('approval_ref','approval'),('review_decision','review_decision'),('branch','branch'),('issue_pr','pr')]:
        value=getattr(args,attr,None)
        if value is not None:m[key]=value
    if not m.get('owner','').strip():raise ValueError('Record the task owner with --owner before changing status')
    if new in {'BLOCKED','REOPENED'} and not args.reason:raise ValueError('An explicit --reason is required')
    if new in {'IN_PROGRESS','IN_REVIEW','COMPLETED'}:
        missing=[d for d in m['depends_on'] if tasks[d][1]['status']!='COMPLETED']
        if missing:raise ValueError('Incomplete dependencies: '+', '.join(missing))
    m['status']=new;m['updated_at']=dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')
    m['blocked_reason']=args.reason if new in {'BLOCKED','REOPENED'} else ''
    proposed=dict(tasks);proposed[args.task]=(row,m,body)
    if new=='COMPLETED':
        errs=completion_errors(args.task,proposed)
        if errs:raise ValueError('\n'.join(errs))
    write_task(ROOT/row['file'],m,body)
    log_change(args.task,old,new,m['owner'],args.reason or 'Observed status update; see task evidence')
    # Invalidate downstream acceptance if a completed dependency is reopened.
    if old=='COMPLETED':
        affected={args.task}
        changed=True
        while changed:
            changed=False
            for k,(_,tm,_) in tasks.items():
                if k not in affected and any(d in affected for d in tm['depends_on']):affected.add(k);changed=True
        for k in sorted(affected-{args.task}):
            tr,tm,tb=tasks[k]
            if tm['status'] in {'COMPLETED','IN_PROGRESS','IN_REVIEW'}:
                old_child=tm['status'];tm=dict(tm);tm['status']='BLOCKED'
                tm['blocked_reason']=f'Upstream {args.task} reopened: {args.reason}'
                tm['updated_at']=m['updated_at']
                write_task(ROOT/tr['file'],tm,tb)
                log_change(k,old_child,'BLOCKED',m['owner'],tm['blocked_reason'])
    refresh()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    sub=parser.add_subparsers(dest='command',required=True)
    sub.add_parser('refresh',help='Refresh the one master index from task metadata')
    sub.add_parser('validate',help='Check dependencies, original IDs, evidence metadata, links and index freshness')
    sub.add_parser('next',help='List unstarted/reopened tasks whose hard prerequisites are completed')
    p=sub.add_parser('set',help='Record an evidence-based local task-status transition')
    p.add_argument('task');p.add_argument('status',choices=sorted(STATES))
    for arg in ['owner','reviewer','commit','evidence','approval','review-decision','reason','branch','pr']:p.add_argument('--'+arg)
    args=parser.parse_args()
    try:
        if args.command=='refresh':refresh();print('Master index refreshed.')
        elif args.command=='set':set_status(args);print(f'{args.task}: {args.status}; index refreshed.')
        elif args.command=='next':
            tasks=load_tasks()
            for k,(_,m,b) in tasks.items():
                if m['status'] in {'NOT_STARTED','REOPENED'} and readiness(k,tasks).startswith('READY'):print(k,readiness(k,tasks),b.split('\n',1)[0])
        elif args.command=='validate':
            errors,stats=validate();print(json.dumps({'valid':not errors,'checks':stats,'errors':errors},indent=2))
            return 1 if errors else 0
    except (ValueError,KeyError,OSError,json.JSONDecodeError) as ex:
        print(f'ERROR: {ex}',file=sys.stderr);return 1
    return 0

if __name__=='__main__':sys.exit(main())
