import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { appLink, decodeEngagementProgress, decodeProgressList, EngagementProgressTracker, PortfolioProgress } from './engagement-progress';

const id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const clientId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const taskId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const step = (state: string, requirement = 'MANDATORY', extra: object = {}) => ({
  taskId, title: 'Agree the engagement scope', phase: 'ONBOARDING', ordinal: 1, requirement, state, condition: null,
  assigneeUserId: null, assigneeName: null, dueDate: '2027-02-01', applicabilityCondition: null, ...extra,
});
const progress = (extra: object = {}) => ({
  engagementId: id, clientId, clientName: 'Harbour Traders', serviceRoute: 'FinancialStatementAudit',
  periodStart: '2026-01-01', periodEnd: '2026-12-31', lifecycleRoute: true, phase: 'Client onboarding',
  stageLabel: 'Engagement Acceptance', stageIndex: 3, percentComplete: 50, applicableRequired: 2, completedRequired: 1,
  pendingRequired: 1, blockedRequired: 0, notApplicable: 1, optionalApplicable: 1, optionalCompleted: 0, gateOpen: true,
  gates: ['Materiality is not approved.'], disclosure: null, responsibleName: 'Dana Reviewer', responsibleRole: 'AuditManager',
  nextDueDate: '2027-02-01', nextAction: 'Agree the engagement scope',
  nextActionLink: `/app/work?engagementId=${id}&taskId=${taskId}`,
  steps: [step('COMPLETED'), step('PENDING'), step('NOT_APPLICABLE', 'CONDITIONAL', { applicabilityCondition: 'Only where inventory exists' })],
  evaluatedAtUtc: '2026-10-26T09:30:00Z', ...extra,
});

describe('engagement progress contract', () => {
  it('accepts consistent figures and rejects totals or completion the server cannot have produced', () => {
    expect(decodeEngagementProgress(progress(), '').percentComplete).toBe(50);
    expect(decodeEngagementProgress(progress({ applicableRequired: 0, completedRequired: 0, pendingRequired: 0, percentComplete: null }), '')
      .percentComplete).toBeNull();
    expect(() => decodeEngagementProgress(progress({ pendingRequired: 2 }), '')).toThrow();
    // A plan with every required step done still reads below 100% while a gate is open.
    expect(() => decodeEngagementProgress(progress({ completedRequired: 2, pendingRequired: 0, percentComplete: 100 }), '')).toThrow();
    expect(decodeEngagementProgress(progress({ completedRequired: 2, pendingRequired: 0, percentComplete: 99 }), '').gateOpen).toBe(true);
    expect(() => decodeEngagementProgress(progress({ percentComplete: 75 }), '')).toThrow();
    expect(() => decodeEngagementProgress(progress({ gates: [] }), '')).toThrow();
    expect(() => decodeEngagementProgress(progress({ nextActionLink: 'https://example.test/app/work' }), '')).toThrow();
    expect(() => decodeProgressList({ items: [progress(), progress()], total: 2, page: 0, pageSize: 1 }, '')).toThrow();
  });

  it('splits an in-app next-action link into a router path and query', () => {
    expect(appLink(`/app/work?engagementId=${id}&taskId=${taskId}`)).toEqual({ path: '/app/work', query: { engagementId: id, taskId } });
    expect(appLink(`/app/engagements/${id}/audit-plan`)).toEqual({ path: `/app/engagements/${id}/audit-plan`, query: {} });
    expect(appLink('//elsewhere.test/app')).toBeNull();
  });
});

describe('engagement progress tracker', () => {
  function setup<T>(component: Type<T>) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
    return { http: TestBed.inject(HttpTestingController), fixture: TestBed.createComponent(component) };
  }

  it('shows the percentage, counts, open gates and a deep link to the next action', () => {
    const { http, fixture } = setup(EngagementProgressTracker);
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id}/progress`).flush(progress());
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.tracker-figure strong')?.textContent).toBe('50%');
    expect(element.querySelector('[aria-label="Required step counts"]')?.textContent).toContain('1 not applicable');
    expect(element.querySelector('.tracker-gates')?.textContent).toContain('Materiality is not approved.');
    const next = element.querySelector<HTMLAnchorElement>('dl.facts a');
    expect(next?.getAttribute('href')).toBe(`/app/work?engagementId=${id}&taskId=${taskId}`);
    expect(element.querySelector('details')?.textContent).toContain('Applies when: Only where inventory exists');
  });

  it('pages the portfolio summary without averaging engagements together', () => {
    const { http, fixture } = setup(PortfolioProgress);
    fixture.detectChanges();
    http.expectOne('/api/ui/portfolio/progress').flush({ items: [progress()], total: 11, page: 0, pageSize: 10 });
    fixture.detectChanges();
    const next = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find(button => button.textContent?.includes('Next progress page'))!;
    expect(next.disabled).toBe(false);
    next.click();
    fixture.detectChanges();
    http.expectOne('/api/ui/portfolio/progress?page=1').flush({ items: [], total: 11, page: 1, pageSize: 10 });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No engagements in your current scope.');
    http.verify();
  });
});
