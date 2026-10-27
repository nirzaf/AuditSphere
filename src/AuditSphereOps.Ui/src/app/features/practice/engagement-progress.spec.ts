import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { appLink, ClientProgress, decodeEngagementProgress, decodeProgressList, EngagementProgressTracker, PortfolioProgress } from './engagement-progress';

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
  steps: [step('COMPLETED'), step('PENDING', 'MANDATORY', { taskId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd' }),
    step('NOT_APPLICABLE', 'CONDITIONAL', { taskId: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', applicabilityCondition: 'Only where inventory exists' })],
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
  beforeEach(() => {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
  });
  afterEach(() => {
    try { TestBed.inject(HttpTestingController).verify(); }
    finally {
      TestBed.resetTestingModule();
      vi.useRealTimers();
      vi.restoreAllMocks();
    }
  });

  function setup<T>(component: Type<T>) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
    return { http: TestBed.inject(HttpTestingController), fixture: TestBed.createComponent(component) };
  }

  it('explains that a completed task plan remains gated when its service profile is unapproved', () => {
    const { http, fixture } = setup(EngagementProgressTracker);
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id}/progress`).flush(progress({ serviceRoute: 'AccountingOnly',
      lifecycleRoute: false, stageLabel: null, stageIndex: null, percentComplete: 99, completedRequired: 2,
      pendingRequired: 0, gates: ['Completion profile is not approved for this service (D03).'] }));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.tracker-figure')?.textContent).toContain('99%');
    expect(element.querySelector('.tracker-figure')?.textContent).toContain('All required steps are complete; open gates prevent completion.');
    expect(element.querySelector('.tracker-gates')?.textContent).toContain('Completion profile is not approved for this service (D03).');
  });

  it('does not describe all steps as done when 99% is rounded from a plan with pending work', () => {
    const { http, fixture } = setup(EngagementProgressTracker);
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    http.expectOne(`/api/ui/engagements/${id}/progress`).flush(progress({ percentComplete: 99,
      applicableRequired: 200, completedRequired: 199, pendingRequired: 1 }));
    fixture.detectChanges();
    const gates = (fixture.nativeElement as HTMLElement).querySelector('.tracker-gates');
    expect(gates?.querySelector('h3')?.textContent).toBe('Open gates');
    expect(gates?.textContent).toContain('Materiality is not approved.');
  });

  it('refreshes changed evidence in the mounted tracker without collapsing steps or losing focus', () => {
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

    const details = element.querySelector('details')!;
    const summary = details.querySelector('summary')!;
    details.open = true;
    summary.focus();
    expect(document.activeElement).toBe(summary);
    vi.advanceTimersByTime(30_000);
    fixture.detectChanges();
    const refresh = http.expectOne(`/api/ui/engagements/${id}/progress`);
    // Returning focus while a read is pending must not cancel/restart or overlap it.
    window.dispatchEvent(new Event('focus'));
    window.dispatchEvent(new Event('focus'));
    http.expectNone(`/api/ui/engagements/${id}/progress`);
    expect(refresh.cancelled).toBe(false);
    expect(element.querySelector('.tracker-figure strong')?.textContent).toBe('50%');
    expect(fixture.componentInstance.view.loading()).toBe(true);
    expect(element.querySelector('[aria-label="Loading engagement progress"]')).not.toBeNull();
    refresh.flush(progress({ percentComplete: 0, completedRequired: 0, blockedRequired: 1,
      steps: [step('BLOCKED', 'MANDATORY', { condition: 'Required evidence is stale.' })] }));
    fixture.detectChanges();
    expect(element.querySelector('.tracker-figure strong')?.textContent).toBe('0%');
    expect(element.querySelector('[aria-label="Required step counts"]')?.textContent).toContain('1 blocked');
    expect(details.textContent).toContain('Required evidence is stale.');
    expect(element.querySelector('details')).toBe(details);
    expect(details.open).toBe(true);
    expect(document.activeElement).toBe(summary);
  });

  it.each([
    { name: 'client', component: ClientProgress, url: `/api/ui/clients/${clientId}/progress` },
    { name: 'portfolio', component: PortfolioProgress, url: '/api/ui/portfolio/progress' },
  ])('refreshes the mounted $name summary by interval and on focus return', ({ component, url }) => {
    const { http, fixture } = setup<ClientProgress | PortfolioProgress>(component);
    if (component === ClientProgress) fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();
    const pageSize = component === ClientProgress ? 50 : 10;
    const page = (extra: object = {}) => ({ items: [progress(extra)], total: pageSize + 1, page: 0, pageSize });
    http.expectOne(url).flush(page({ percentComplete: 100, completedRequired: 2, pendingRequired: 0, gateOpen: false, gates: [] }));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const link = element.querySelector<HTMLAnchorElement>('tbody th a')!;
    const nextPage = [...element.querySelectorAll('button')].find(button => button.textContent?.includes('Next progress page'));
    if (nextPage) expect(nextPage.disabled).toBe(false);
    link.focus();
    vi.advanceTimersByTime(30_000);
    fixture.detectChanges();
    expect(element.querySelector('.progress-cell strong')?.textContent).toBe('100%');
    expect(fixture.componentInstance.view.loading()).toBe(true);
    expect(element.querySelector('audit-state [role="progressbar"]')).not.toBeNull();
    if (nextPage) expect(nextPage.disabled).toBe(true);
    http.expectOne(url).flush(page({ percentComplete: 0, completedRequired: 0, blockedRequired: 1 }));
    fixture.detectChanges();
    expect(element.querySelector('.progress-cell strong')?.textContent).toBe('0%');
    expect(fixture.componentInstance.view.loading()).toBe(false);
    if (nextPage) expect(nextPage.disabled).toBe(false);
    expect(element.querySelector('tbody th a')).toBe(link);
    expect(document.activeElement).toBe(link);
    window.dispatchEvent(new Event('focus'));
    http.expectOne(url).flush(page());
    fixture.detectChanges();
    expect(element.querySelector('.progress-cell strong')?.textContent).toBe('50%');
  });

  it('pauses reads while hidden, resumes on return and cleans up requests, timers and listeners on destroy', () => {
    const intervals = vi.spyOn(globalThis, 'setInterval');
    const cleared = vi.spyOn(globalThis, 'clearInterval');
    const removeVisibility = vi.spyOn(document, 'removeEventListener');
    const removeFocus = vi.spyOn(window, 'removeEventListener');
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    const { http, fixture } = setup(EngagementProgressTracker);
    const url = `/api/ui/engagements/${id}/progress`;
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    http.expectNone(url);
    expect(vi.getTimerCount()).toBe(0);
    visibility.mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    fixture.detectChanges();
    http.expectOne(url).flush(progress());
    fixture.detectChanges();
    expect(vi.getTimerCount()).toBe(1);
    window.dispatchEvent(new Event('focus'));
    const hiddenRead = http.expectOne(url);
    visibility.mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    fixture.detectChanges();
    expect(hiddenRead.cancelled).toBe(true);
    expect(vi.getTimerCount()).toBe(0);
    vi.advanceTimersByTime(60_000);
    window.dispatchEvent(new Event('focus'));
    http.expectNone(url);
    visibility.mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    const resumed = http.expectOne(url);
    resumed.flush(progress());
    fixture.detectChanges();
    window.dispatchEvent(new Event('focus'));
    const destroyedRead = http.expectOne(url);
    fixture.destroy();
    expect(destroyedRead.cancelled).toBe(true);
    expect(removeVisibility).toHaveBeenCalledWith('visibilitychange', expect.any(Function));
    expect(removeFocus).toHaveBeenCalledWith('focus', expect.any(Function));
    // jsdom may still have Angular's scheduled animation frame; assert the read/poll timers directly.
    intervals.mock.results.forEach((result, index) => {
      if ([20_000, 30_000].includes(intervals.mock.calls[index][1]!))
        expect(cleared).toHaveBeenCalledWith(result.value);
    });
    document.dispatchEvent(new Event('visibilitychange'));
    window.dispatchEvent(new Event('focus'));
    vi.advanceTimersByTime(60_000);
    http.expectNone(url);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('never retains old data across session or record changes, including late responses before effects run', () => {
    const { http, fixture } = setup(EngagementProgressTracker);
    const session = TestBed.inject(SessionService);
    const view = fixture.componentInstance.view;
    const url = `/api/ui/engagements/${id}/progress`;
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    http.expectOne(url).flush(progress());
    fixture.detectChanges();
    window.dispatchEvent(new Event('focus'));
    const oldActor = http.expectOne(url);
    session.current.set({ userId: taskId, firmId: clientId, generation: '2', staff: true });
    expect(view.data()).toBeNull();
    oldActor.flush(progress({ clientName: 'Old identity data' }));
    expect(view.data()).toBeNull();
    fixture.detectChanges();
    http.expectOne(url).flush(progress());
    fixture.detectChanges();
    window.dispatchEvent(new Event('focus'));
    const oldRecord = http.expectOne(url);
    fixture.componentRef.setInput('engagementId', taskId);
    expect(view.data()).toBeNull();
    oldRecord.flush(progress({ clientName: 'Old record data' }));
    expect(view.data()).toBeNull();
    fixture.detectChanges();
    const newRecord = http.expectOne(`/api/ui/engagements/${taskId}/progress`);
    expect((fixture.nativeElement as HTMLElement).querySelector('.tracker-figure')).toBeNull();
    session.clear();
    expect(view.data()).toBeNull();
    fixture.detectChanges();
    expect(newRecord.cancelled).toBe(true);
    expect(vi.getTimerCount()).toBe(0);
    vi.advanceTimersByTime(60_000);
    window.dispatchEvent(new Event('focus'));
    http.expectNone(request => request.url.includes('/progress'));
  });

  it('removes retained figures when a refresh is denied or cannot be decoded', () => {
    const { http, fixture } = setup(EngagementProgressTracker);
    const url = `/api/ui/engagements/${id}/progress`;
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    http.expectOne(url).flush(progress());
    fixture.detectChanges();
    window.dispatchEvent(new Event('focus'));
    http.expectOne(url).flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(fixture.componentInstance.view.data()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('not available in your current scope');
    window.dispatchEvent(new Event('focus'));
    http.expectOne(url).flush(progress());
    fixture.detectChanges();
    window.dispatchEvent(new Event('focus'));
    http.expectOne(url).flush(progress({ percentComplete: 100 }));
    fixture.detectChanges();
    expect(fixture.componentInstance.view.data()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Unsupported response');
  });

  it('pages client progress with authorized totals and resets to the unpaged URL when the client changes', () => {
    const { http, fixture } = setup(ClientProgress);
    const url = `/api/ui/clients/${clientId}/progress`;
    fixture.componentRef.setInput('clientId', clientId);
    fixture.detectChanges();
    const first = { items: Array.from({ length: 50 }, (_, index) => progress({
      engagementId: `00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
    })), total: 51, page: 0, pageSize: 50 };
    const last = { items: [progress()], total: 51, page: 1, pageSize: 50 };
    http.expectOne(url).flush(first);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const nav = () => element.querySelector<HTMLElement>('nav[aria-label="Client engagement progress pages"]')!;
    const buttons = () => [...nav().querySelectorAll<HTMLButtonElement>('button')];
    expect(element.querySelectorAll('tbody tr')).toHaveLength(50);
    expect(nav().textContent).toContain('Page 1 · 51 engagements in scope');
    expect(buttons().map(button => button.disabled)).toEqual([true, false]);
    const heading = element.querySelector<HTMLElement>('h2')!;
    buttons()[1].focus();
    expect(document.activeElement).toBe(buttons()[1]);
    buttons()[1].click();
    fixture.detectChanges();
    expect(document.activeElement).toBe(heading);
    expect(element.textContent).not.toContain('Harbour Traders');
    const next = http.expectOne(url + '?page=1');
    window.dispatchEvent(new Event('focus'));
    http.expectNone(url + '?page=1');
    expect(next.cancelled).toBe(false);
    next.flush(last);
    fixture.detectChanges();
    expect(element.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(nav().textContent).toContain('Page 2 · 51 engagements in scope');
    expect(buttons().map(button => button.disabled)).toEqual([false, true]);
    expect(document.activeElement).toBe(heading);
    buttons()[0].focus();
    buttons()[0].click();
    fixture.detectChanges();
    http.expectOne(url).flush(first);
    fixture.detectChanges();
    expect(nav().textContent).toContain('Page 1 · 51 engagements in scope');
    expect(buttons().map(button => button.disabled)).toEqual([true, false]);
    expect(document.activeElement).toBe(heading);
    buttons()[1].click();
    fixture.detectChanges();
    http.expectOne(url + '?page=1').flush(last);
    fixture.detectChanges();
    window.dispatchEvent(new Event('focus'));
    const oldClient = http.expectOne(url + '?page=1');
    fixture.componentRef.setInput('clientId', taskId);
    expect(fixture.componentInstance.page()).toBe(0);
    expect(fixture.componentInstance.view.data()).toBeNull();
    fixture.detectChanges();
    expect(oldClient.cancelled).toBe(true);
    expect(element.textContent).not.toContain('Harbour Traders');
    http.expectOne(`/api/ui/clients/${taskId}/progress`).flush({
      items: [progress({ clientId: taskId, clientName: 'New client' })], total: 1, page: 0, pageSize: 50,
    });
    fixture.detectChanges();
    expect(nav().textContent).toContain('Page 1 · 1 engagement in scope');
    expect(buttons().map(button => button.disabled)).toEqual([true, true]);
    expect(element.textContent).toContain('New client');
    http.expectNone(url + '?page=0');
    http.expectNone(`/api/ui/clients/${taskId}/progress?page=1`);
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
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Harbour Traders');
    http.expectOne('/api/ui/portfolio/progress?page=1').flush({ items: [], total: 11, page: 1, pageSize: 10 });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No engagements in your current scope.');
    http.verify();
  });
});
