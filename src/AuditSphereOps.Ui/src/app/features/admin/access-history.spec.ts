import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AccessHistory, AccessHistoryRow, humanize, shorten, summarizeAccessHistory } from './access-history';

const owner = 'Mohamed Farook Mohamed Fazrin';
const tenant = '4de3e6fd-51aa-4ba7-b2c5-82106d2e45f0';
const grantAt = '2026-10-26T18:08:13.574552+00:00';
const reason = 'Owner-directed operator grant on 2026-10-26: the firm owner instructed full firm-wide privileges.';
const row = (extra: Partial<AccessHistoryRow>): AccessHistoryRow => ({
  at: grantAt, operation: 'ROLE_GRANTED', target: owner, change: '', reason, actor: owner, result: 'OWNER_DIRECTED_OPERATOR',
  microsoftOperation: null, ...extra,
});
// The workspace returns role evidence first, then the matching Microsoft 365 administration events, newest first.
const rows: AccessHistoryRow[] = [
  row({ change: ' → FinanceReviewer' }), row({ change: ' → Partner' }), row({ change: ' → RelationshipManager' }),
  row({ change: 'NONE -> FinanceReviewer@FIRM_WIDE', result: 'APPLIED' }), row({ change: 'NONE -> Partner@FIRM_WIDE', result: 'APPLIED' }),
  row({ change: 'NONE -> RelationshipManager@FIRM_WIDE', result: 'APPLIED' }),
  row({ at: '2026-10-11T17:36:47.672211+00:00', operation: 'SELECTED_RESOURCE_BOUNDARY_VERIFIED', target: tenant,
    change: 'UNVERIFIED → VERIFIED', reason: 'Administrator requested exact resource and unrelated-site denial checks',
    result: 'exact-resource-and-control-denial' }),
  row({ at: '2026-10-11T11:00:22.852302+00:00', operation: 'CAPABILITIES_VERIFIED', target: tenant,
    change: '- → DIRECTORY_READ,OUTBOUND_MAIL', reason: 'Administrator requested capability verification', result: 'RECORDED' }),
  row({ at: '2026-10-10T10:04:38.691306+00:00', operation: 'FOLDER_TEMPLATE_SAVED', target: '-',
    change: 'NONE → 89c63840ef603ac98b522adf86d294132c0a3acaf5605c5ecd4cd1bb7cdbc768', reason: 'Administrator saved a reviewed folder manifest',
    result: '01a12545-91b0-776a-9a22-c0bac362fbe1' }),
  row({ at: '2026-10-09T22:51:48.514255+00:00', change: ' → Administrator', reason: 'BOOTSTRAP', result: 'BOOTSTRAP' }),
];

describe('access history summary', () => {
  it('merges paired role records and roles granted together into one readable entry', () => {
    const entries = summarizeAccessHistory(rows);
    expect(entries).toHaveLength(5);
    const grant = entries[0];
    expect(grant.event).toBe('3 roles granted');
    expect(grant.summary).toBe('Partner, Relationship Manager, Finance Reviewer · firm-wide');
    expect(grant.when).toBe('26 Oct 2026, 18:08');
    expect(grant.tags).toEqual(['Owner-directed']);
    expect(grant.reason).toBe(reason);
    expect(grant.records).toBe(6);
    expect(entries[4]).toMatchObject({ event: 'Role granted', summary: 'Administrator', reason: 'Bootstrap', tags: ['Bootstrap'] });
  });

  it('keeps identifiers short in the row and exact in the details', () => {
    const [, boundary, capabilities, folder] = summarizeAccessHistory(rows);
    expect(boundary).toMatchObject({ event: 'Site boundary verified', category: 'SITES', subject: '4de3e6fd…', subjectFull: tenant,
      summary: 'Unverified → Verified', tags: [] });
    expect(boundary.details).toContainEqual({ label: 'Result', value: 'exact-resource-and-control-denial' });
    expect(capabilities).toMatchObject({ category: 'MICROSOFT', summary: 'Directory read, Outbound mail', tags: ['Recorded'] });
    expect(folder.summary).toBe('89c63840…');
    expect(folder.details).toContainEqual({ label: 'Recorded changes', value: 'NONE → 89c63840ef603ac98b522adf86d294132c0a3acaf5605c5ecd4cd1bb7cdbc768' });
    expect(shorten('a 01a12545-91b0-776a-9a22-c0bac362fbe1')).toBe('a 01a12545…');
    expect(humanize('RelationshipManager')).toBe('Relationship Manager');
  });
});

describe('access history view', () => {
  function render(source: AccessHistoryRow[]) {
    const fixture = TestBed.createComponent(AccessHistory);
    fixture.componentRef.setInput('rows', source);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }

  it('searches across people, roles and reasons, filters by activity and reveals full details on request', () => {
    const { fixture, page, root } = render(rows);
    expect(root.querySelectorAll('.history-item')).toHaveLength(5);
    page.update(page.query, 'relationship manager');
    fixture.detectChanges();
    expect(root.querySelectorAll('.history-item')).toHaveLength(1);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Showing 1 of 5 events.');
    page.clear();
    page.update(page.category, 'SITES');
    fixture.detectChanges();
    expect([...root.querySelectorAll('.history-item .event')].map(e => e.textContent)).toEqual(['Site boundary verified', 'Folder template saved']);
    const details = root.querySelector<HTMLButtonElement>('.history-item button')!;
    details.click();
    fixture.detectChanges();
    expect(details.getAttribute('aria-expanded')).toBe('true');
    expect(root.querySelector('.details')?.textContent).toContain(tenant);
    page.update(page.query, 'no such entry');
    fixture.detectChanges();
    expect(root.textContent).toContain('No events match these filters.');
  });

  it('pages long histories instead of rendering every record', () => {
    const many = Array.from({ length: 30 }, (_, i) => row({ at: `2026-10-${String(30 - Math.floor(i / 2)).padStart(2, '0')}T0${i % 2}:00:00Z`,
      operation: 'CAPABILITIES_VERIFIED', target: tenant, change: `- → STEP_${i}`, result: 'RECORDED' }));
    const { fixture, root } = render(many);
    expect(root.querySelectorAll('.history-item')).toHaveLength(25);
    [...root.querySelectorAll('button')].find(b => b.textContent?.includes('Show 25 more'))!.click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.history-item')).toHaveLength(30);
  });
});
