import { describe, expect, it } from 'vitest';
import { decodeAnalytics } from './analytics';

const payload = {
  from: '2026-10-01',
  to: '2026-10-31',
  engagements: [],
  engagementPageSize: 50, hasMoreEngagements: false, nextEngagementId: null,
  departments: [],
  milestones: {
    due: 4,
    completed: 3,
    completedOnTime: 2,
    completedLate: 1,
    overdue: 1,
    completionPercent: '75.0',
    onTimePercent: '50.0',
  },
  workload: [
    { assigneeKey: 'staff-1', assigneeName: 'Audit staff', due: 2, completed: 1, open: 1, overdue: 1 },
    { assigneeKey: 'unassigned', assigneeName: 'Unassigned', due: 1, completed: 0, open: 1, overdue: 0 },
  ],
  phaseWorkload: [
    { phase: 'AUDIT_FIELDWORK', label: 'Audit Fieldwork', due: 1, completed: 1, open: 0, overdue: 0, completionPercent: '100.0' },
    { phase: 'UNASSIGNED_PHASE', label: 'Unassigned phase', due: 2, completed: 0, open: 2, overdue: 1, completionPercent: '0.0' },
  ],
  reviewTurnaround: { decisions: 4, reviewed: 3, changesRequired: 1, measured: 2, excluded: 2, averageHours: '7.5' },
  clientWait: { intervals: 3, completed: 1, open: 1, excluded: 1, averageCompletedHours: '4.0', averageOpenHours: '12.5', available: true, unavailableReason: null },
  definitions: [],
};

describe('Practice analytics task completion contract', () => {
  it('keeps the applicable-task counts and exact decimal percentages', () => {
    const decoded = decodeAnalytics(payload, 'analytics');
    expect(decoded.milestones.due).toBe(4);
    expect(decoded.milestones.completed).toBe(3);
    expect(decoded.milestones.completionPercent).toBe('75.0');
    expect(decoded.workload[0]).toEqual(payload.workload[0]);
    expect(decoded.workload[1].assigneeKey).toBe('unassigned');
    expect(decoded.reviewTurnaround.averageHours).toBe('7.5');
    expect(decoded.reviewTurnaround.excluded).toBe(2);
    expect(decoded.clientWait).toEqual(payload.clientWait);
    expect(decoded.phaseWorkload).toHaveLength(2);
    expect(decoded.phaseWorkload[0].completionPercent).toBe('100.0');
    expect(decoded.phaseWorkload[1].phase).toBe('UNASSIGNED_PHASE');
  });

  it('represents an empty review sample without inventing a zero-hour duration', () => {
    const decoded = decodeAnalytics({ ...payload, reviewTurnaround: { decisions: 0, reviewed: 0, changesRequired: 0, measured: 0, excluded: 0, averageHours: null } }, 'analytics');
    expect(decoded.reviewTurnaround.averageHours).toBeNull();
  });

  it('represents empty or over-limit client-wait evidence without inventing a duration', () => {
    const empty = decodeAnalytics({ ...payload, clientWait: { intervals: 0, completed: 0, open: 0, excluded: 0,
      averageCompletedHours: null, averageOpenHours: null, available: true, unavailableReason: null } }, 'analytics');
    expect(empty.clientWait.averageCompletedHours).toBeNull();
    const capped = decodeAnalytics({ ...payload, clientWait: { intervals: 0, completed: 0, open: 0, excluded: 0,
      averageCompletedHours: null, averageOpenHours: null, available: false, unavailableReason: 'Narrow the period.' } }, 'analytics');
    expect(capped.clientWait.available).toBe(false);
    expect(capped.clientWait.unavailableReason).toBe('Narrow the period.');
  });

  it('rejects numeric percentages instead of accepting a rounded browser number', () => {
    expect(() => decodeAnalytics({ ...payload, milestones: { ...payload.milestones, completionPercent: 75 } }, 'analytics')).toThrow();
  });

  it('rejects workload rows without bounded integer counts', () => {
    expect(() => decodeAnalytics({ ...payload, workload: [{ ...payload.workload[0], overdue: '1' }] }, 'analytics')).toThrow();
  });
});


describe('Analytics economics page fences', () => {
  it('refuses an oversized page or continuation without its last engagement identity', () => {
    expect(() => decodeAnalytics({ ...payload, engagementPageSize: 101 }, 'analytics')).toThrow();
    expect(() => decodeAnalytics({ ...payload, hasMoreEngagements: true }, 'analytics')).toThrow();
    expect(() => decodeAnalytics({ ...payload, nextEngagementId: '11111111-1111-4111-8111-111111111111' }, 'analytics')).toThrow();
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { PracticeAnalytics } from './analytics';

describe('Analytics economics continuation', () => {
  it('sends the page cursor and resets it when the reporting period is applied again', () => {
    TestBed.configureTestingModule({ imports: [PracticeAnalytics], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const id = '11111111-1111-4111-8111-111111111111';
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
    const fixture = TestBed.createComponent(PracticeAnalytics);
    const http = TestBed.inject(HttpTestingController);
    const read = () => http.expectOne(request => request.url.startsWith('/api/ui/practice/analytics?'));
    try {
      fixture.detectChanges(); TestBed.tick();
      const first = read();
      expect(new URL(first.request.url, 'http://localhost').searchParams.get('engagementPageSize')).toBe('50');
      first.flush(payload);
      fixture.componentInstance.nextPage(id); TestBed.tick();
      const second = read();
      expect(new URL(second.request.url, 'http://localhost').searchParams.get('afterEngagementId')).toBe(id);
      second.flush(payload);
      fixture.componentInstance.apply(); TestBed.tick();
      const reset = read();
      expect(new URL(reset.request.url, 'http://localhost').searchParams.has('afterEngagementId')).toBe(false);
      reset.flush(payload);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});

describe('Analytics focused views and metric labels', () => {
  const engagement = {
    engagementId: '22222222-2222-4222-8222-222222222222', label: 'Synthetic client · AUDIT 2026-12-31', currency: 'QAR',
    budgetMinutes: 600, budgetValue: '30000.00', actualMinutes: 660, standardValue: '25000.00', actualCost: '10000.00',
    billed: '15000.00', collected: '6000.00', realizationPercent: '60.0', collectionPercent: '40.0', profit: '5000.00',
    marginPercent: '33.3', budgetVarianceMinutes: -60, costComplete: true, contractedFee: '20000.00', contractCurrency: 'QAR',
    lifetimeStandardValue: '25000.00', contractedFeeLessStandardValue: '-5000.00', nonChargeableMinutes: 30,
  };
  const mixed = {
    ...engagement, engagementId: '33333333-3333-4333-8333-333333333333', label: 'Mixed client · AUDIT 2026-12-31', currency: 'MIXED',
    budgetValue: null, standardValue: null, actualCost: null, billed: null, collected: null, realizationPercent: null,
    collectionPercent: null, profit: null, marginPercent: null, costComplete: false, contractedFee: null, contractCurrency: null,
    lifetimeStandardValue: null, contractedFeeLessStandardValue: null,
  };

  function render() {
    TestBed.configureTestingModule({ imports: [PracticeAnalytics], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const id = '11111111-1111-4111-8111-111111111111';
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
    const fixture = TestBed.createComponent(PracticeAnalytics);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(request => request.url.startsWith('/api/ui/practice/analytics?'))
      .flush({ ...payload, engagements: [engagement, mixed] });
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const select = (label: string) => {
      [...host.querySelectorAll<HTMLButtonElement>('.analytics-views button')].find(b => b.textContent?.trim() === label)!.click();
      fixture.detectChanges();
    };
    const headers = () => [...host.querySelectorAll('table')[0].querySelectorAll('thead th')].map(th => th.textContent?.trim());
    const cells = (label: string) => [...[...host.querySelectorAll('tbody tr')]
      .find(row => row.querySelector('th')?.textContent?.includes(label))!.querySelectorAll('td')].map(td => td.textContent?.trim());
    return { fixture, http, host, select, headers, cells };
  }

  it('keeps billing realization, billed less staff cost and contract contribution distinct and unaveraged', () => {
    const { fixture, http, host, select, headers, cells } = render();
    try {
      expect(host.querySelector('.analytics-views button[aria-pressed="true"]')?.textContent?.trim()).toBe('Engagement economics');
      expect(headers()).toContain('Billing realization');
      expect(headers()).toContain('Billed less recorded staff cost');
      expect(headers().some(h => /profit/i.test(h ?? ''))).toBe(false);
      expect(cells('Synthetic client')).toEqual(['QAR', '25,000.00', '10,000.00', '15,000.00', '6,000.00', '60%', '40%', '5,000.00', '33.3%']);
      expect(cells('Mixed client')).toEqual(['Mixed — financial totals unavailable', '—', '—', '—', '—', 'n/a', 'n/a', '—', 'n/a']);
      expect(host.textContent).toContain('the page is not a firm total');
      expect(host.textContent).toContain('not the firm\'s accounting profit');

      select('Contract comparison');
      expect(headers()).toEqual(['Engagement', 'Contract currency', 'Agreed contract fee', 'Lifetime standard value', 'Contract contribution']);
      expect(cells('Synthetic client')).toEqual(['QAR', '20,000.00', '25,000.00', '-5,000.00']);
      expect(cells('Mixed client')).toEqual(['Unavailable', 'Unavailable', 'Unavailable', 'Unavailable']);

      select('Capacity and time');
      expect(cells('Synthetic client')).toEqual(['10.0', '11.0', '0.5', '-1.0 overrun']);
      expect(host.textContent).toContain('Utilization by department');

      select('Operational delivery');
      expect(host.textContent).not.toContain('Synthetic client');
      expect(host.textContent).toContain('Task workload by assignee');
      expect(host.textContent).toContain('Definitions');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
