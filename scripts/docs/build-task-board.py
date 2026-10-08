#!/usr/bin/env python3
"""Generate the agent task board from the task cards, the STE user stories and the codebase audit record.

Usage (from anywhere):
    python3 scripts/docs/build-task-board.py          # rewrite the board
    python3 scripts/docs/build-task-board.py --check  # exit 1 when the board is stale

Sources: docs/task_breakdown/tasks/ (card front matter is the R2R status authority),
docs/execution/auditsphere-execution-user-stories-ste-v21-*.md (story headings and status lines),
docs/execution/auditsphere-execution-report-task-codebase-audit-current.json (audit verdicts).
Standard library only. Edit the sources, not the board.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
EXEC = ROOT / 'docs' / 'execution'
AUDIT = EXEC / 'auditsphere-execution-report-task-codebase-audit-current.json'
BOARD = EXEC / 'auditsphere-execution-index-task-board-current.md'
STE_NXT = EXEC / 'auditsphere-execution-user-stories-ste-v21-remaining-proposed.md'
STE_GAP = EXEC / 'auditsphere-execution-user-stories-ste-v21-gap-closure-current.md'
CARDS = ROOT / 'docs' / 'task_breakdown' / 'tasks'

NEXT_ACTION = {
    'COMPLETE': 'Verify the checklist, then record COMPLETED with task_status.py',
    'PARTIAL': 'Build the open gaps',
    'NOT_STARTED': 'Build the owned requests',
    'CONFLICTS_WITH_AGENTS': 'Owner decision: accept or reject the variation, then rewrite the contract',
    'OWNER_DECISION': 'Owner decision',
    'EXTERNAL_BLOCKED': 'Needs live evidence; stays BLOCKED_EXTERNAL',
    'NOT_AUDITED': 'Audit against the codebase before building',
}
MAX_GAPS_SHOWN = 12
MAX_TEXT = 280


def clean(text):
    """Keep generated text free of backticks so the docs gate does not treat audit prose as repo paths."""
    text = re.sub(r'/Users/[^/\s]+/Repos/[^/\s]+/', '', text)
    return text.replace('`', '').replace('|', '/').replace('\n', ' ').strip()


def clip(text, limit=MAX_TEXT):
    text = clean(text)
    return text if len(text) <= limit else text[:limit - 1].rstrip() + '…'


def load_cards():
    cards = {}
    for path in sorted(CARDS.rglob('auditsphere-*-task-t*.md')):
        text = path.read_text(encoding='utf-8')
        front = re.match(r'---\n(.*?)\n---', text, re.S)
        if not front:
            raise SystemExit(f'Missing front matter: {path}')
        meta = {}
        for line in front.group(1).splitlines():
            if ':' in line:
                key, value = line.split(':', 1)
                meta[key.strip()] = value.strip().strip('"')
        heading = re.search(r'^# (T\d{3})\s+\S\s+(.+)$', text, re.M)
        if not heading or heading.group(1) != meta['id']:
            raise SystemExit(f'Card heading does not match its id: {path}')
        cards[meta['id']] = {
            'family': meta['work_package'],
            'title': heading.group(2).strip(),
            'status': meta['status'],
            'depends_on': re.findall(r'T\d{3}', meta.get('depends_on', '')),
        }
    return cards


def load_stories(path, pattern):
    lines = path.read_text(encoding='utf-8').split('\n')
    stories = {}
    for index, line in enumerate(lines):
        match = re.match(pattern, line)
        if not match:
            continue
        status = '—'
        for follow in lines[index + 1:index + 4]:
            if follow.startswith('*Status'):
                body = re.sub(r'^\*Status[^:]*:\s*', '', follow).lstrip('*').strip()
                status = re.split(r'\.\s|\.$', body)[0].strip('* ')
                break
        stories[match.group(1)] = {'title': match.group(2).strip(), 'status': status}
    return stories


def main():
    record = json.loads(AUDIT.read_text(encoding='utf-8'))
    audited = {item['id']: item for item in record['items']}
    cards = load_cards()
    nxt = load_stories(STE_NXT, r'^## (STE-NXT-\d{3}) — (.+)$')
    gap = load_stories(STE_GAP, r'^# (STE-GAP-\d{3}) — (.+)$')

    rows = []
    for card_id in sorted(cards):
        card = cards[card_id]
        blockers = [f'{d} ({cards[d]["status"]})' for d in card['depends_on'] if cards[d]['status'] != 'COMPLETED']
        rows.append({'id': card_id, 'family': card['family'], 'title': card['title'], 'record': card['status'],
                     'deps': blockers, 'state': card['status']})
    for story_id, story in list(nxt.items()) + list(gap.items()):
        family = 'STE-NXT' if story_id.startswith('STE-NXT') else 'STE-GAP'
        rows.append({'id': story_id, 'family': family, 'title': story['title'], 'record': story['status'],
                     'deps': [], 'state': story['status']})

    for row in rows:
        item = audited.get(row['id'])
        if item:
            row['verdict'] = item['verdict']
            row['missing'] = item['missing']
            row['conflicts'] = item['conflicts']
            row['summary'] = item['summary']
            row['refutation'] = item.get('refutation', '')
        elif row['id'] in cards and cards[row['id']]['status'] == 'COMPLETED':
            row['verdict'] = 'NOT_AUDITED'
            row['missing'] = []
            row['conflicts'] = []
            row['summary'] = 'Completed in the card front matter before this audit; not re-audited.'
            row['refutation'] = ''
        else:
            row['verdict'] = 'NOT_AUDITED'
            row['missing'] = []
            row['conflicts'] = []
            row['summary'] = 'Added after the audit ran; not audited.'
            row['refutation'] = ''

    verdict_counts = {}
    for row in rows:
        verdict_counts[row['verdict']] = verdict_counts.get(row['verdict'], 0) + 1

    lines = [
        '# AuditSphereOps — Agent task board',
        '',
        '**Status:** CURRENT. Generated by `scripts/docs/build-task-board.py`; do not edit by hand.',
        '',
        f'**Audit record:** [codebase audit of commit `{record["commit"]}`](auditsphere-execution-report-task-codebase-audit-current.json) ({record["audited"]}). Verdicts are static reading of the code; tests were not executed in that pass.',
        '',
        '**How an agent uses this board:** find the ID in section 2, read its card (R2R and AUD in `docs/task_breakdown/tasks/`) or its story (STE in `docs/execution/`), take its open gaps from section 4, and respect its blockers. Do not mark a status from this board; record status only through the rules below.',
        '',
        '**Completion rules (R2R and AUD cards):** `docs/task_breakdown/tools/task_status.py` validates and records status. A card becomes COMPLETED only with an independent APPROVED review, the full reviewed commit SHA, an evidence file, all six completion checklist items verified, and every dependency already COMPLETED. The helper refuses any other transition.',
        '',
        '## 1. Summary',
        '',
        '| Code verdict | Items |',
        '| --- | ---: |',
    ]
    for verdict in ['COMPLETE', 'PARTIAL', 'NOT_STARTED', 'CONFLICTS_WITH_AGENTS', 'OWNER_DECISION', 'EXTERNAL_BLOCKED', 'NOT_AUDITED']:
        lines.append(f'| {verdict} | {verdict_counts.get(verdict, 0)} |')
    lines.append(f'| **Total** | **{len(rows)}** |')
    lines += [
        '',
        f'Card status in the front matter: {sum(1 for c in cards.values() if c["status"] == "COMPLETED")} COMPLETED, '
        f'{sum(1 for c in cards.values() if c["status"] == "IN_REVIEW")} IN_REVIEW, '
        f'{sum(1 for c in cards.values() if c["status"] == "NOT_STARTED")} NOT_STARTED of {len(cards)} cards. '
        'Nothing is COMPLETED from the audit: no item met every completion test in the card.',
        '',
        '## 2. Board',
        '',
        '| ID | Family | Title | Record | Code verdict | Blocked by | Gaps | Next action |',
        '| --- | --- | --- | --- | --- | --- | ---: | --- |',
    ]
    for row in rows:
        blocked = list(row['deps'])
        if row['verdict'] in ('CONFLICTS_WITH_AGENTS', 'OWNER_DECISION') and row['state'] != 'COMPLETED':
            blocked.append('owner decision')
        if row['verdict'] == 'EXTERNAL_BLOCKED':
            blocked.append('live evidence')
        if row['state'] == 'COMPLETED':
            action = 'None; completed'
        else:
            action = NEXT_ACTION[row['verdict']]
        verdict = row['verdict'] if row['state'] != 'COMPLETED' or row['verdict'] in ('COMPLETE', 'NOT_AUDITED') else 'superseded: ' + row['verdict']
        lines.append('| {id} | {family} | {title} | {record} | {verdict} | {blocked} | {gaps} | {action} |'.format(
            id=row['id'], family=row['family'], title=clean(row['title']), record=clean(row['record']),
            verdict=verdict, blocked=clean(', '.join(blocked)) or '—',
            gaps=len(row['missing']) if row['verdict'] != 'NOT_AUDITED' and row['state'] != 'COMPLETED' else '—', action=action))

    decisions = [r for r in rows if r['verdict'] in ('CONFLICTS_WITH_AGENTS', 'OWNER_DECISION') and r['state'] != 'COMPLETED']
    lines += [
        '',
        '## 3. Decisions for the owner',
        '',
        'T002 was COMPLETED on 2026-10-09 under the owner-delegated static-service variation (MediatR and bUnit declined). The helper now lets T003 start; the audit verdicts on T002 and on the cards that were rewritten are superseded by that decision and are shown as such in section 2.',
        '',
    ]
    for row in decisions:
        basis = row['conflicts'][0] if row['conflicts'] else row['summary']
        lines.append(f'- **{row["id"]}** ({row["verdict"]}): {clip(basis, 260)}')

    lines += ['', '## 4. Open gaps by item', '']
    for row in rows:
        if not row['missing']:
            continue
        lines.append(f'### {row["id"]} — {clean(row["title"])} ({row["verdict"]})')
        lines.append('')
        for gap_text in row['missing'][:MAX_GAPS_SHOWN]:
            lines.append(f'- {clip(gap_text)}')
        extra = len(row['missing']) - MAX_GAPS_SHOWN
        if extra > 0:
            lines.append(f'- {extra} more in the [audit record](auditsphere-execution-report-task-codebase-audit-current.json).')
        lines.append('')

    lines += ['---', '', 'Regenerate: `python3 scripts/docs/build-task-board.py`. Check freshness: `python3 scripts/docs/build-task-board.py --check`.', '']
    generated = '\n'.join(lines)

    if '--check' in sys.argv:
        current = BOARD.read_text(encoding='utf-8') if BOARD.exists() else ''
        if current != generated:
            print('Task board is stale: run python3 scripts/docs/build-task-board.py')
            return 1
        print('Task board is current.')
        return 0
    BOARD.write_text(generated, encoding='utf-8')
    print(f'Wrote {BOARD.relative_to(ROOT)} with {len(rows)} items.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
