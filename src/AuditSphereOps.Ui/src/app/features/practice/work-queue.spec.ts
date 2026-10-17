import { describe, expect, it } from 'vitest';
import { decodeActivity, decodeQueueSummary, hasBlockingPrerequisites } from './work-queue';

const activity = {
  engagementId: 'b43c00a8-a7a1-4efb-8e3d-1fd88f7ee447',
  taskId: '3c2d3786-43bb-4353-9bf7-9710c54f5315',
  from: '2026-07-15',
  to: '2026-10-12',
  events: [{
    id: '86feee9b-4a84-4502-b85c-c38cac5fe0f5',
    kind: 'TASK_REASSIGNED',
    sourceLabel: 'Operational Task',
    actorDisplayName: 'Engagement manager',
    assigneeDisplayName: 'Audit staff',
    taskRevision: 4,
    previousDueDate: null,
    newDueDate: null,
    occurredAt: '2026-10-12T06:30:00Z',
  }],
  hasMore: true,
  nextCursor: { occurredAt: '2026-10-12T06:30:00Z', activityId: '86feee9b-4a84-4502-b85c-c38cac5fe0f5' },
};

describe('workflow task activity contract', () => {
  it('decodes the source, actor, revision, timestamp and stable page cursor', () => {
    const decoded = decodeActivity(activity, 'activity');
    expect(decoded.events[0].sourceLabel).toBe('Operational Task');
    expect(decoded.events[0].actorDisplayName).toBe('Engagement manager');
    expect(decoded.events[0].assigneeDisplayName).toBe('Audit staff');
    expect(decoded.events[0].taskRevision).toBe(4);
    expect(decoded.nextCursor?.activityId).toBe(activity.events[0].id);
  });

  it('rejects events from an unimplemented source or malformed actor and timestamp', () => {
    expect(() => decodeActivity({ ...activity, events: [{ ...activity.events[0], sourceLabel: 'Risk Decision' }] }, 'activity')).toThrow();
    expect(() => decodeActivity({ ...activity, events: [{ ...activity.events[0], actorDisplayName: '' }] }, 'activity')).toThrow();
    expect(() => decodeActivity({ ...activity, events: [{ ...activity.events[0], occurredAt: 'yesterday' }] }, 'activity')).toThrow();
  });

  it('supports an empty bounded timeline without an invented cursor', () => {
    const decoded = decodeActivity({ ...activity, events: [], hasMore: false, nextCursor: null }, 'activity');
    expect(decoded.events).toEqual([]);
    expect(decoded.hasMore).toBe(false);
    expect(decoded.nextCursor).toBeNull();
  });

  it('decodes an exact due-date change with nullable before and after dates', () => {
    const event = { ...activity.events[0], kind: 'TASK_DUE_DATE_CHANGED',
      previousDueDate: '2026-10-31', newDueDate: null };
    const decoded = decodeActivity({ ...activity, events: [event], hasMore: false, nextCursor: null }, 'activity');
    expect(decoded.events[0].previousDueDate).toBe('2026-10-31');
    expect(decoded.events[0].newDueDate).toBeNull();
  });
});

const summaryViews = ['MY_TASKS', 'DUE_TODAY', 'OVERDUE', 'WAITING_FOR_CLIENT', 'UNDER_REVIEW', 'CHANGES_REQUIRED', 'UPCOMING'];
const summary = (counts: Record<string, string> = {}) => ({
  asOfDateUtc: '2026-10-15',
  counts: summaryViews.map(view => ({ view, count: counts[view] ?? '0' })),
});

describe('work overview summary contract', () => {
  it('decodes seven exact count strings into the fixed card order without numeric conversion', () => {
    const source = summary({ MY_TASKS: '101', UPCOMING: '9223372036854775807' });
    const decoded = decodeQueueSummary({ ...source, counts: [...source.counts].reverse() }, 'summary');
    expect(decoded.asOfDateUtc).toBe('2026-10-15');
    expect(decoded.cards.map(x => x.key)).toEqual(summaryViews);
    expect(decoded.cards.map(x => x.label))
      .toEqual(['My tasks', 'Due today', 'Overdue', 'Waiting for client', 'Under review', 'Rework required', 'Next 7 days']);
    expect(decoded.cards[0].count).toBe('101');
    expect(decoded.cards[6].count).toBe('9223372036854775807');
  });

  it('rejects malformed, negative, scientific, padded and non-string counts', () => {
    for (const count of ['1e3', '-1', '01', '1.0', ' 1', '', '12345678901234567890', 101, null])
      expect(() => decodeQueueSummary({ ...summary(), counts: summary().counts.map(x => x.view === 'OVERDUE' ? { ...x, count } : x) }, 'summary')).toThrow();
  });

  it('rejects duplicate, missing, unexpected and extra views and a malformed as-of date', () => {
    const counts = summary().counts;
    expect(() => decodeQueueSummary({ ...summary(), counts: [...counts.slice(0, 6), { view: 'MY_TASKS', count: '1' }] }, 'summary')).toThrow();
    expect(() => decodeQueueSummary({ ...summary(), counts: counts.slice(0, 6) }, 'summary')).toThrow();
    expect(() => decodeQueueSummary({ ...summary(), counts: [...counts.slice(0, 6), { view: 'TEAM', count: '1' }] }, 'summary')).toThrow();
    expect(() => decodeQueueSummary({ ...summary(), counts: [...counts, { view: 'TEAM', count: '1' }] }, 'summary')).toThrow();
    expect(() => decodeQueueSummary({ ...summary(), asOfDateUtc: '15/10/2026' }, 'summary')).toThrow();
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { WorkflowWorkQueue } from './work-queue';

describe('workflow queue assignment filters', () => {
  it('sends the current session owner, resets paging when assignment changes, and clears assignment on view changes', () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const userId = '11111111-1111-4111-8111-111111111111';
    TestBed.inject(SessionService).current.set({ userId, firmId: userId, generation: '1', staff: true });
    const fixture = TestBed.createComponent(WorkflowWorkQueue);
    const http = TestBed.inject(HttpTestingController);
    const flushQueue = (url: string, view: string) => http.expectOne(url).flush({ view, tasks: [], hasMore: false, nextCursor: null });
    try {
      fixture.detectChanges(); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=MY_TASKS&pageSize=50&includeSummary=true', 'MY_TASKS');
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const component = fixture.componentInstance;
      component.selectView('TEAM'); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=TEAM&pageSize=50&includeSummary=true', 'TEAM');
      component.selectOwnerFilter('ME'); TestBed.tick();
      flushQueue(`/api/ui/work/tasks?view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}`, 'TEAM');
      component.nextPage({ dueDate: null, taskId: userId }); TestBed.tick();
      flushQueue(`/api/ui/work/tasks?view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}&afterTaskId=${userId}`, 'TEAM');
      component.selectOwnerFilter('UNASSIGNED'); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=TEAM&pageSize=50&includeSummary=true&unassignedOnly=true', 'TEAM');
      component.selectView('MY_TASKS'); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=MY_TASKS&pageSize=50&includeSummary=true', 'MY_TASKS');
      expect(component.ownerFilter()).toBe('ALL');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  const tasksUrl = (query: string) => `/api/ui/work/tasks?${query}`;
  const task = {
    taskId: '22222222-2222-4222-8222-222222222222', clientId: '33333333-3333-4333-8333-333333333333', clientName: 'Client A',
    engagementId: '44444444-4444-4444-8444-444444444444', engagementServiceRoute: 'AUDIT', engagementPeriod: 'FY2026',
    sourceKey: 'MANUAL:fixture', templateRevisionId: null, templateStepId: null,
    title: 'Prepare lead schedule', phase: 'FIELDWORK', ordinal: 1, taskStatus: 'OPEN',
    assigneeUserId: '11111111-1111-4111-8111-111111111111', assigneeDisplayName: 'Audit staff', dueDate: '2026-10-15',
    mappingVersionId: null, fsliCode: null, unmetTaskDependencies: 0, unmetEvidencePrerequisites: 0,
    canComplete: false, canChangeDueDate: false, canAssign: true, revision: 1, pbcRequestId: null, pbcState: null, reviewState: null, reviewComment: null,
  };
  const start = () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const userId = '11111111-1111-4111-8111-111111111111';
    const session = TestBed.inject(SessionService);
    session.current.set({ userId, firmId: userId, generation: '1', staff: true });
    const fixture = TestBed.createComponent(WorkflowWorkQueue);
    const http = TestBed.inject(HttpTestingController);
    const host = fixture.nativeElement as HTMLElement;
    const overview = () => host.querySelector('section[aria-labelledby="work-summary-heading"]') as HTMLElement;
    const cards = () => Array.from(overview().querySelectorAll<HTMLButtonElement>('button.summary-card'));
    const card = (label: string) => cards().find(x => x.querySelector('.summary-label')?.textContent?.trim() === label)!;
    const countOf = (label: string) => card(label).querySelector('.summary-count')?.textContent?.trim();
    return { fixture, http, session, userId, host, overview, cards, card, countOf, component: fixture.componentInstance };
  };

  it('shows prerequisite blocking in each layout from server facts without inventing permission or historical blockers', () => {
    expect(hasBlockingPrerequisites({ ...task, unmetTaskDependencies: 1 })).toBe(true);
    expect(hasBlockingPrerequisites({ ...task, unmetEvidencePrerequisites: 1 })).toBe(true);
    expect(hasBlockingPrerequisites({ ...task, pbcRequestId: task.taskId, pbcState: 'SENT' })).toBe(true);
    expect(hasBlockingPrerequisites({ ...task, pbcRequestId: task.taskId, pbcState: null })).toBe(true);
    expect(hasBlockingPrerequisites({ ...task, pbcRequestId: task.taskId, pbcState: 'ACCEPTED' })).toBe(false);
    expect(hasBlockingPrerequisites({ ...task, pbcRequestId: task.taskId, pbcState: 'CLOSED' })).toBe(false);
    expect(hasBlockingPrerequisites(task)).toBe(false); // canComplete=false alone is not prerequisite evidence.
    expect(hasBlockingPrerequisites({ ...task, taskStatus: 'COMPLETED', unmetTaskDependencies: 1 })).toBe(false);
    const { fixture, http, host, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [{ ...task, unmetTaskDependencies: 1, templateRevisionId: task.taskId, templateStepId: task.clientId, mappingVersionId: task.engagementId, fsliCode: 'CASH', pbcRequestId: task.taskId, pbcState: 'SENT', reviewState: 'CHANGES_REQUIRED', reviewComment: 'Reconcile the retained cash evidence' }], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      for (const display of ['LIST', 'BOARD', 'CALENDAR'] as const) {
        component.display.set(display); fixture.detectChanges(); TestBed.tick();
        const labels = host.querySelectorAll('.prerequisite-status');
        expect(labels.length).toBe(1);
        expect(labels[0].textContent).toContain('Prerequisites: blocked');
        expect(host.textContent).toContain('Source: MANUAL:fixture');
        expect(host.textContent).toContain('template revision ' + task.taskId);
        expect(host.textContent).toContain('step ' + task.clientId);
        expect(host.textContent).toContain('FSLI CASH');
        expect(host.textContent).toContain('mapping ' + task.engagementId);
        expect(host.textContent).toContain('Reviewer: Reconcile the retained cash evidence');
        expect(host.textContent).toContain('current audit result needs resubmission');
        expect(host.querySelector('a[href$="/pbc"]')?.textContent).toContain('PBC request');
        expect([...host.querySelectorAll('button')].some(button => button.textContent?.includes('View activity'))).toBe(true);
        expect(host.textContent).toContain(task.title);
      }
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('renders exact complete counts above the review inbox and opens each card view with paging and assignment reset', () => {
    const { fixture, http, userId, host, overview, cards, card, countOf, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null, summary: summary({ MY_TASKS: '101', OVERDUE: '1234567' }) });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();

      expect(overview().querySelector('h2#work-summary-heading')?.textContent?.trim()).toBe('Work overview');
      const inbox = host.querySelector('section[aria-labelledby="review-inbox-heading"]')!;
      expect(overview().compareDocumentPosition(inbox) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(cards().map(x => x.querySelector('.summary-label')?.textContent?.trim()))
        .toEqual(['My tasks', 'Due today', 'Overdue', 'Waiting for client', 'Under review', 'Rework required', 'Next 7 days']);
      expect(countOf('My tasks')).toBe('101');
      expect(countOf('Overdue')).toBe('1,234,567');
      expect(countOf('Due today')).toBe('0');
      expect(host.textContent).toContain('1 assigned to you.');
      expect(overview().textContent).toContain('As of 2026-10-15 (UTC)');
      expect(overview().textContent).toContain('Counts cover each default authorized view and may overlap. Assignment filters affect the list below.');
      expect(card('My tasks').getAttribute('aria-pressed')).toBe('true');
      expect(card('Due today').getAttribute('aria-pressed')).toBe('false');
      for (const item of cards()) {
        expect(item.type).toBe('button');
        expect(item.disabled).toBe(false);
        expect(item.tabIndex).toBe(0);
        item.focus();
        expect(document.activeElement).toBe(item);
      }

      const display = host.querySelector('nav[aria-label="Work queue display"]')!;
      expect(Array.from(display.querySelectorAll('button')).map(x => x.textContent?.trim())).toEqual(['List', 'Board', 'Calendar']);
      const buttons = Array.from(host.querySelectorAll('button')).map(x => x.textContent?.trim());
      expect(buttons).toContain('Review prerequisites');
      expect(buttons).toContain('Review task ordering');
      expect(buttons).toContain('View activity');

      component.selectView('TEAM'); TestBed.tick();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true')).flush({ view: 'TEAM', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      component.selectOwnerFilter('ME'); TestBed.tick();
      http.expectOne(tasksUrl(`view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}`)).flush({ view: 'TEAM', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      component.nextPage({ dueDate: '2026-10-15', taskId: task.taskId }); TestBed.tick();
      http.expectOne(tasksUrl(`view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}&afterDueDate=2026-10-15&afterTaskId=${task.taskId}`))
        .flush({ view: 'TEAM', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      card('Due today').click(); TestBed.tick();
      http.expectOne(tasksUrl('view=DUE_TODAY&pageSize=50&includeSummary=true')).flush({ view: 'DUE_TODAY', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      expect(component.selectedView()).toBe('DUE_TODAY');
      expect(component.ownerFilter()).toBe('ALL');
      expect(card('Due today').getAttribute('aria-pressed')).toBe('true');

      component.selectOwnerFilter('UNASSIGNED'); TestBed.tick();
      http.expectOne(tasksUrl('view=DUE_TODAY&pageSize=50&includeSummary=true&unassignedOnly=true')).flush({ view: 'DUE_TODAY', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      component.nextPage({ dueDate: null, taskId: task.taskId }); TestBed.tick();
      http.expectOne(tasksUrl(`view=DUE_TODAY&pageSize=50&includeSummary=true&unassignedOnly=true&afterTaskId=${task.taskId}`))
        .flush({ view: 'DUE_TODAY', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      card('Rework required').click(); TestBed.tick();
      http.expectOne(tasksUrl('view=CHANGES_REQUIRED&pageSize=50&includeSummary=true')).flush({ view: 'CHANGES_REQUIRED', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      expect(component.selectedView()).toBe('CHANGES_REQUIRED');
      expect(component.ownerFilter()).toBe('ALL');
      expect(host.textContent).toContain('Changes required — workflow tasks');

      expect(http.match(request => request.method !== 'GET')).toEqual([]);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('shows the unavailable notice for a legacy summary and the safe error for a malformed one, never zero cards', () => {
    const { fixture, http, host, overview, cards, component } = start();
    const unavailable = 'Summary unavailable — refresh after the dashboard API is updated.';
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true')).flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null, summary: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(overview().textContent).toContain(unavailable);
      expect(cards()).toEqual([]);
      expect(overview().textContent).not.toContain('As of');

      component.selectView('TEAM'); TestBed.tick();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true')).flush({ view: 'TEAM', tasks: [task], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(overview().textContent).toContain(unavailable);
      expect(cards()).toEqual([]);

      component.selectView('OVERDUE'); TestBed.tick();
      http.expectOne(tasksUrl('view=OVERDUE&pageSize=50&includeSummary=true'))
        .flush({ view: 'OVERDUE', tasks: [task], hasMore: false, nextCursor: null, summary: summary({ OVERDUE: '1e3' }) });
      TestBed.tick();
      expect(cards()).toEqual([]);
      expect(overview().textContent).not.toContain(unavailable);
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('Unsupported response.');
      expect(http.match(request => request.method !== 'GET')).toEqual([]);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('hides prior counts while loading and never renders counts from a delayed response after session invalidation', () => {
    const { fixture, http, session, userId, overview, cards, card, countOf, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null, summary: summary({ MY_TASKS: '101' }) });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(countOf('My tasks')).toBe('101');

      card('Due today').click(); TestBed.tick();
      const delayed = http.expectOne(tasksUrl('view=DUE_TODAY&pageSize=50&includeSummary=true'));
      expect(overview().textContent).toContain('Loading work overview.');
      expect(overview().textContent).not.toContain('101');
      expect(cards()).toEqual([]);

      component.activityTask.set({ engagementId: task.engagementId, taskId: task.taskId, title: task.title });
      session.clear(); TestBed.tick();
      expect(delayed.cancelled).toBe(true);
      expect(() => delayed.flush({ view: 'DUE_TODAY', tasks: [task], hasMore: false, nextCursor: null, summary: summary({ MY_TASKS: '101' }) })).toThrow();
      TestBed.tick();
      expect(component.activityTask()).toBeNull();
      expect(component.evidenceTask()).toBeNull();
      expect(component.orderingTask()).toBeNull();
      expect(cards()).toEqual([]);
      expect(overview().textContent).not.toContain('101');
      expect(overview().textContent).not.toContain('Loading work overview.');

      session.current.set({ userId, firmId: userId, generation: '2', staff: true }); TestBed.tick();
      http.expectOne(tasksUrl('view=DUE_TODAY&pageSize=50&includeSummary=true'))
        .flush({ view: 'DUE_TODAY', tasks: [task], hasMore: false, nextCursor: null, summary: summary({ DUE_TODAY: '5' }) });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(countOf('Due today')).toBe('5');
      expect(countOf('My tasks')).toBe('0');
      expect(overview().textContent).not.toContain('101');
      expect(http.match(request => request.method !== 'GET')).toEqual([]);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('requires an exact owner confirmation and reassigns against the loaded task revision', async () => {
    const { fixture, http, host, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const opening = component.openAssignment(task);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
        .flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Replacement staff' }]);
      await opening; fixture.detectChanges();
      expect(host.textContent).toContain(task.taskId);
      expect(host.textContent).toContain(task.engagementId);
      expect(host.textContent).toContain('Audit staff');
      expect(host.textContent).toContain('Replacement staff');
      const select = host.querySelector('select[id^="task-assignee-"]') as HTMLSelectElement;
      select.value = '55555555-5555-4555-8555-555555555555'; select.dispatchEvent(new Event('change')); fixture.detectChanges();
      const button = Array.from(host.querySelectorAll('button')).find(x => x.textContent?.trim() === 'Confirm assignment') as HTMLButtonElement;
      expect(button.disabled).toBe(true);
      component.assignmentReviewed.set(true); fixture.detectChanges(); expect(button.disabled).toBe(false);
      const saving = component.saveAssignment();
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`);
      expect(post.request.body).toEqual({ assigneeUserId: '55555555-5555-4555-8555-555555555555', expectedRevision: task.revision });
      post.flush({ value: true }); await saving;
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      expect(component.assignmentTask()).toBeNull();
      expect(component.assignmentMessage()).toContain('Replacement staff');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('refreshes on a stale revision and does not silently overwrite an ineligible-assignee refusal', async () => {
    const { fixture, http, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const opening = component.openAssignment(task);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
        .flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Replacement staff' }]); await opening;
      component.selectAssignmentOwner('55555555-5555-4555-8555-555555555555'); component.assignmentReviewed.set(true);
      const stale = component.saveAssignment();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`)
        .flush({ code: 'revision.stale', message: 'The task changed.' }, { status: 409, statusText: 'Conflict' });
      await stale;
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      expect(component.assignmentTask()).toBeNull();
      expect(component.assignmentMessage()).toContain('queue has been refreshed');

      const reopen = component.openAssignment(task);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
        .flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Replacement staff' }]); await reopen;
      component.selectAssignmentOwner('55555555-5555-4555-8555-555555555555'); component.assignmentReviewed.set(true);
      const refused = component.saveAssignment();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`)
        .flush({ code: 'scope.denied', message: 'The assignee is no longer eligible.' }, { status: 403, statusText: 'Forbidden' });
      await refused;
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      expect(component.assignmentFailed()).toBe(true);
      expect(component.assignmentMessage()).toContain('assignee is no longer eligible');
      expect(component.assignmentTask()).toBeNull();
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('keeps options for the selected task when an older options read completes later', async () => {
    const { fixture, http, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      const otherTask = { ...task, taskId: '66666666-6666-4666-8666-666666666666', title: 'Review revenue' };
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task, otherTask], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const first = component.openAssignment(task);
      const firstRead = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`);
      const second = component.openAssignment(otherTask);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
        .flush([{ userId: '77777777-7777-4777-8777-777777777777', displayName: 'Current eligible owner' }]);
      await second;
      firstRead.flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Earlier eligible owner' }]);
      await first;
      expect(component.assignmentTask()?.taskId).toBe(otherTask.taskId);
      expect(component.assignmentOptions().map(owner => owner.displayName)).toEqual(['Current eligible owner']);
      expect(component.assignmentLoading()).toBe(false);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('clears an in-flight assignment on session invalidation and ignores its late command response', async () => {
    const { fixture, http, session, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const opening = component.openAssignment(task);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
        .flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Replacement staff' }]); await opening;
      component.selectAssignmentOwner('55555555-5555-4555-8555-555555555555'); component.assignmentReviewed.set(true);
      const saving = component.saveAssignment();
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`);
      session.clear(); TestBed.tick();
      expect(component.assignmentTask()).toBeNull();
      expect(component.assignmentBusy()).toBe(false);
      post.flush({ value: true }); await saving;
      expect(component.assignmentOptions()).toEqual([]);
      expect(component.assignmentUncertain()).toBe(false);
      expect(component.assignmentMessage()).not.toContain('Task assigned');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('fences an unknown assignment and permits only the original request after current task and activity are inspected', async () => {
    const { fixture, http, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const opening = component.openAssignment(task);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
        .flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Replacement staff' }]); await opening;
      component.selectAssignmentOwner('55555555-5555-4555-8555-555555555555'); component.assignmentReviewed.set(true);
      const saving = component.saveAssignment();
      const original = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`);
      const originalBody = original.request.body;
      original.flush({}, { status: 503, statusText: 'Service Unavailable' }); await saving;
      expect(component.assignmentUncertain()).toBe(true);
      component.selectAssignmentOwner('77777777-7777-4777-8777-777777777777');
      component.closeAssignment(); await component.saveAssignment(); await component.retryAssignment();
      expect(component.assignmentUserId()).toBe(originalBody.assigneeUserId);
      expect(component.assignmentTask()?.taskId).toBe(task.taskId);
      http.expectNone(request => request.method === 'POST');
      const checking = component.recoverAssignment();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks`).flush({ engagementId: task.engagementId,
        truncated: false, tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`)
        .flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId, events: [], hasMore: false, nextCursor: null });
      await checking;
      expect(component.canRetryAssignment()).toBe(true);
      const retrying = component.retryAssignment();
      const retry = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`);
      expect(retry.request.body).toEqual(originalBody);
      retry.flush({ value: true }); await retrying;
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      expect(component.assignmentUncertain()).toBe(false);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('requires acknowledgement of an advanced owner and keeps wrong-engagement recovery unresolved', async () => {
    const { fixture, http, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const opening = component.openAssignment(task);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
        .flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Replacement staff' }]); await opening;
      component.selectAssignmentOwner('55555555-5555-4555-8555-555555555555'); component.assignmentReviewed.set(true);
      const saving = component.saveAssignment();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`)
        .flush({}, { status: 503, statusText: 'Service Unavailable' }); await saving;
      const current = { id: task.taskId, assigneeUserId: '77777777-7777-4777-8777-777777777777', assigneeDisplayName: 'Concurrent owner', revision: 2, status: 'IN_PROGRESS' };
      const wrongScope = component.recoverAssignment();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks`)
        .flush({ engagementId: '88888888-8888-4888-8888-888888888888', truncated: false, tasks: [current] });
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`)
        .flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId, events: [], hasMore: false, nextCursor: null });
      await wrongScope;
      expect(component.assignmentObservation()).toBeNull();
      expect(component.assignmentUncertain()).toBe(true);
      const checking = component.recoverAssignment();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks`)
        .flush({ engagementId: task.engagementId, truncated: false, tasks: [current] });
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`)
        .flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId, events: [], hasMore: false, nextCursor: null });
      await checking;
      expect(component.canRetryAssignment()).toBe(false);
      await component.retryAssignment();
      http.expectNone(request => request.method === 'POST');
      component.acknowledgeAssignmentObservation();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      expect(component.assignmentUncertain()).toBe(false);
      expect(component.assignmentMessage()).toContain('Concurrent owner at revision 2 acknowledged');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
