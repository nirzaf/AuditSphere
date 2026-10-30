import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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

  it('decodes retained cancellation and reopening reasons and nullable legacy activity reasons', () => {
    const event = { ...activity.events[0], kind: 'TASK_CANCELLED', reason: 'Scope changed after fieldwork started.' };
    const decoded = decodeActivity({ ...activity, events: [event], hasMore: false, nextCursor: null }, 'activity');
    expect(decoded.events[0].reason).toBe(event.reason);
    const reopening = { ...event, kind: 'TASK_REOPENED', reason: 'The operational owner confirmed work can resume.' };
    expect(decodeActivity({ ...activity, events: [reopening], hasMore: false, nextCursor: null }, 'activity').events[0].reason)
      .toBe(reopening.reason);
    expect(decodeActivity(activity, 'legacy activity').events[0].reason).toBeNull();
    expect(decodeActivity({ ...activity, events: [{ ...activity.events[0], reason: null }] }, 'legacy null reason').events[0].reason).toBeNull();
    const maximum = 'x'.repeat(2000);
    expect(decodeActivity({ ...activity, events: [{ ...event, reason: maximum }] }, 'activity').events[0].reason).toBe(maximum);
    for (const reason of [undefined, null, '', '   ', 'x'.repeat(2001)])
      expect(() => decodeActivity({ ...activity, events: [{ ...event, reason }] }, 'activity')).toThrow();
    for (const reason of [undefined, null, '', '   ', 'x'.repeat(2001)])
      expect(() => decodeActivity({ ...activity, events: [{ ...reopening, reason }] }, 'activity')).toThrow();
  });

  it('requires the retained reason on hold and resumption events', () => {
    for (const kind of ['TASK_SUSPENDED', 'TASK_RESUMED']) {
      const event = { ...activity.events[0], kind, reason: 'Client asked to pause until the bank replies.' };
      expect(decodeActivity({ ...activity, events: [event], hasMore: false, nextCursor: null }, 'activity').events[0].kind).toBe(kind);
      for (const reason of [null, '   '])
        expect(() => decodeActivity({ ...activity, events: [{ ...event, reason }] }, 'activity')).toThrow();
    }
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
import type { ComponentFixture } from '@angular/core/testing';
import type { WritableSignal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { SessionService } from '../../core/session';
import { provideWorkspaceAvailabilityForTests } from '../../core/workspace-availability.testing';
import { WorkspaceAvailability, type WorkspaceAvailabilityView } from '../../core/workspace-availability';
import { WorkflowWorkQueue } from './work-queue';
import { WorkflowTaskCancellationPanel } from './workflow-task-cancellation';
import { WorkflowTaskApplicabilityPanel } from './workflow-task-applicability';

describe('workflow queue assignment filters', () => {
  beforeEach(() => {
    // Each synthetic case owns a separate storage object and its in-memory reconciliation fences.
    class CaseStorage implements Storage {
      private readonly values = new Map<string, string>();
      get length(): number { return this.values.size; }
      clear(): void { this.values.clear(); }
      getItem(key: string): string | null { return this.values.get(key) ?? null; }
      key(index: number): string | null { return [...this.values.keys()][index] ?? null; }
      removeItem(key: string): void { this.values.delete(key); }
      setItem(key: string, value: string): void { this.values.set(key, String(value)); }
    }
    vi.stubGlobal('Storage', CaseStorage);
    vi.stubGlobal('sessionStorage', new CaseStorage());
  });
  afterEach(() => vi.unstubAllGlobals());
  it('sends the current session owner, resets paging when assignment changes, and clears assignment on view changes', async () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideWorkspaceAvailabilityForTests()] });
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
      component.selectView('TEAM'); await fixture.whenStable(); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=TEAM&pageSize=50&includeSummary=true', 'TEAM');
      component.selectOwnerFilter('ME'); await fixture.whenStable(); TestBed.tick();
      flushQueue(`/api/ui/work/tasks?view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}`, 'TEAM');
      component.nextPage({ dueDate: null, taskId: userId }); TestBed.tick();
      flushQueue(`/api/ui/work/tasks?view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}&afterTaskId=${userId}`, 'TEAM');
      component.selectOwnerFilter('UNASSIGNED'); await fixture.whenStable(); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=TEAM&pageSize=50&includeSummary=true&unassignedOnly=true', 'TEAM');
      component.selectView('MY_TASKS'); await fixture.whenStable(); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=MY_TASKS&pageSize=50&includeSummary=true', 'MY_TASKS');
      expect(component.ownerFilter()).toBe('ALL');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('keyboard-scrolls the named task tables without consuming keys from their controls', () => {
    const { fixture, http, host } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();

      for (const label of ['Submitted procedure reviews', 'Operational task list']) {
        const region = host.querySelector<HTMLElement>(`[role="region"][aria-label="${label}"]`)!;
        expect(region.getAttribute('aria-describedby')).toBe('work-table-scroll-instructions');
        Object.defineProperties(region, {
          clientWidth: { configurable: true, value: 200 },
          scrollWidth: { configurable: true, value: 1000 },
        });
        region.focus();
        const right = new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true });
        region.dispatchEvent(right);
        expect(right.defaultPrevented).toBe(true);
        expect(region.scrollLeft).toBe(160);

        const end = new KeyboardEvent('keydown', { key: 'End', bubbles: true, cancelable: true });
        region.dispatchEvent(end);
        expect(region.scrollLeft).toBe(800);
        const home = new KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true });
        region.dispatchEvent(home);
        expect(region.scrollLeft).toBe(0);

        if (label === 'Operational task list') {
          const control = region.querySelector<HTMLElement>('button, a, input')!;
          control.focus();
          const nativeKey = new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true });
          control.dispatchEvent(nativeKey);
          expect(nativeKey.defaultPrevented).toBe(false);
          expect(region.scrollLeft).toBe(0);
        }
      }
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  const tasksUrl = (query: string) => `/api/ui/work/tasks?${query}`;
  const task = {
    taskId: '22222222-2222-4222-8222-222222222222', clientId: '33333333-3333-4333-8333-333333333333', clientName: 'Client A',
    engagementId: '44444444-4444-4444-8444-444444444444', engagementServiceRoute: 'AUDIT', engagementPeriod: 'FY2026',
    sourceKey: 'MANUAL:fixture', templateRevisionId: null, templateStepId: null, requiredTask: null,
    taskGroupName: 'Audit planning', applicabilityCondition: null,
    title: 'Prepare lead schedule', phase: 'FIELDWORK', ordinal: 1, taskStatus: 'OPEN',
    assigneeUserId: '11111111-1111-4111-8111-111111111111', assigneeDisplayName: 'Audit staff', dueDate: '2026-10-15',
    mappingVersionId: null, fsliCode: null, unmetTaskDependencies: 0, unmetEvidencePrerequisites: 0,
    canComplete: false, canChangeDueDate: false, canAssign: true, canCancel: false, canReopen: false, revision: 1, pbcRequestId: null, pbcState: null, reviewState: null, reviewComment: null,
    onHold: false, holdReason: null as string | null, canSuspend: false, canResume: false, manualRequiredTask: null as boolean | null,
  };
  const reminderWorkspace = (id: string, title: string, revision: number, status: string, engagementId = task.engagementId,
    reminderOrigin: {
      reminderRuleId?: string | null; reminderRuleRevisionId?: string | null; reminderOperationId?: string | null;
      reminderSource?: { sourceKind: 'TASK_DUE' | 'PBC_DUE'; sourceId: string; sourceRevision: number; dueRevision: number;
        sourceDueDate: string; sourceHref: string; recipientUserId: string; isSourceCurrent: boolean | null } | null;
      canOpenReminderRule?: boolean;
    } = {}) => ({
    engagementId, truncated: false,
    tasks: [{ id, title, phase: task.phase, ordinal: task.ordinal, status,
      assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, assignedRole: null,
      dueDate: task.dueDate, revision, reminderRuleId: reminderOrigin.reminderRuleId ?? null,
      reminderRuleRevisionId: reminderOrigin.reminderRuleRevisionId ?? null,
      reminderOperationId: reminderOrigin.reminderOperationId ?? null,
      ...(Object.prototype.hasOwnProperty.call(reminderOrigin, 'reminderSource') ? { reminderSource: reminderOrigin.reminderSource } : {}),
      ...(Object.prototype.hasOwnProperty.call(reminderOrigin, 'canOpenReminderRule') ? { canOpenReminderRule: reminderOrigin.canOpenReminderRule } : {}),
    }],
  });
  const cancelableTask = { ...task, canCancel: true };
  const reopenableTask = { ...task, taskStatus: 'CANCELLED', canCancel: false, canReopen: true };
  const legacyTask = Object.fromEntries(Object.entries(task).filter(([key]) => key !== 'canCancel' && key !== 'canReopen'));
  const start = () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideWorkspaceAvailabilityForTests()] });
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
  const openCancellation = (test: ReturnType<typeof start>, overrides: object = {}) => {
    const { fixture, http, host, component } = test;
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
      .flush({ view: 'MY_TASKS', tasks: [{ ...cancelableTask, ...overrides }], hasMore: false, nextCursor: null, summary: summary() });
    http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
    TestBed.tick();
    const trigger = host.querySelector<HTMLButtonElement>(`button[data-cancel-task="${task.taskId}"]`)!;
    trigger.focus(); trigger.click(); fixture.detectChanges(); TestBed.tick();
    return fixture.debugElement.query(By.directive(WorkflowTaskCancellationPanel)).componentInstance as WorkflowTaskCancellationPanel;
  };

  // Fail exactly the readback after a real registry write, leaving the written bytes intact.
  const failRegistryReadback = (registryKey: string, entryCount: number) => {
    const originalSet = Storage.prototype.setItem, originalGet = Storage.prototype.getItem;
    let failNextRead = false, injected = false;
    const write = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (this: Storage, key, value) {
      originalSet.call(this, key, value);
      if (!injected && this === sessionStorage && key === registryKey && JSON.parse(value).entries.length === entryCount) {
        injected = true; failNextRead = true;
      }
    });
    const read = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(function (this: Storage, key) {
      if (this === sessionStorage && key === registryKey && failNextRead) {
        failNextRead = false; throw new Error('Synthetic registry readback failure');
      }
      return originalGet.call(this, key);
    });
    return { injected: () => injected, restore: () => { read.mockRestore(); write.mockRestore(); } };
  };

  it('discovers off-page receipts only after exact reads and opens recovery despite another same-task key', async () => {
    const { fixture, http, host, component, userId } = start();
    const savedTask = '99999999-9999-4999-8999-999999999999';
    const key = '77777777-7777-4777-8777-777777777777';
    const suffix = `${userId}:${userId}:${task.engagementId}:${savedTask}`;
    const applicabilityKey = `auditsphere.workflow-applicability:${suffix}`;
    const orderingKey = `auditsphere.workflow-ordering:${suffix}`;
    const exactUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${savedTask}`;
    const panelUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks/${savedTask}/applicability`;
    const exact = { engagementId: task.engagementId, truncated: false, tasks: [{ id: savedTask, assigneeUserId: null,
      assigneeDisplayName: null, revision: 1, status: 'OPEN' }] };
    try {
      sessionStorage.setItem(applicabilityKey, key); sessionStorage.setItem(orderingKey, key);
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); component.findSavedReceipts(); fixture.detectChanges();
      expect(host.textContent).not.toContain(savedTask);
      http.expectOne(exactUrl).flush(exact);
      await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); await Promise.resolve();
      http.expectOne(exactUrl).flush(exact); await fixture.whenStable(); fixture.detectChanges();
      expect(host.textContent).toContain(savedTask);
      const entry = component.visibleSavedReceipts().find(item => item.candidate.kind === 'applicability')!;
      const opening = component.openSavedReceipt(entry);
      http.expectOne(exactUrl).flush(exact); await opening; TestBed.tick(); fixture.detectChanges();
      http.expectOne(panelUrl).flush({ engagementId: task.engagementId, taskId: savedTask, taskRevision: '1', latestProposalId: null,
        proposal: null, preview: null, canPropose: false, canApprove: false, canReturn: false, canRevoke: false });
      TestBed.tick();
      const panel = fixture.debugElement.query(By.directive(WorkflowTaskApplicabilityPanel)).componentInstance as WorkflowTaskApplicabilityPanel;
      expect(panel.pending()).toBe(key);
      expect(component.taskMutationBlocked(task.taskId)).toBe(false);
      component.complete(task.engagementId, savedTask, 1);
      http.expectNone(request => request.method === 'POST' && request.url.includes(savedTask));
      expect(sessionStorage.getItem(orderingKey)).toBe(key);
      component.closeSavedReceipt(); TestBed.tick();
      expect(sessionStorage.getItem(applicabilityKey)).toBe(key);
      const reopening = component.openSavedReceipt(entry);
      http.expectOne(exactUrl).flush(exact); await reopening; TestBed.tick();
      http.expectOne(panelUrl).flush({ engagementId: task.engagementId, taskId: savedTask, taskRevision: '1', latestProposalId: null,
        proposal: null, preview: null, canPropose: false, canApprove: false, canReturn: false, canRevoke: false });
      TestBed.tick();
      const reopened = fixture.debugElement.query(By.directive(WorkflowTaskApplicabilityPanel)).componentInstance as WorkflowTaskApplicabilityPanel;
      const recovering = reopened.recover();
      http.expectOne(`${panelUrl}/receipts/${key}`).flush({ engagementId: task.engagementId, taskId: savedTask,
        idempotencyKey: key, proposalId: '66666666-6666-4666-8666-666666666666', decisionId: null, version: '1' });
      await recovering; TestBed.tick();
      expect(sessionStorage.getItem(applicabilityKey)).toBeNull();
      expect(sessionStorage.getItem(orderingKey)).toBe(key);
      component.complete(task.engagementId, savedTask, 1);
      http.expectNone(request => request.method === 'POST' && request.url.includes(savedTask));
    } finally { sessionStorage.removeItem(applicabilityKey); sessionStorage.removeItem(orderingKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('does not disclose a delayed exact read after a session, availability or saved-key change', async () => {
    for (const change of ['session', 'availability', 'key'] as const) {
      const { fixture, http, host, component, userId, session } = start();
      const savedTask = '99999999-9999-4999-8999-999999999999';
      const storageKey = `auditsphere.workflow-evidence:${userId}:${userId}:${task.engagementId}:${savedTask}`;
      try {
        sessionStorage.setItem(storageKey, '77777777-7777-4777-8777-777777777777');
        fixture.detectChanges(); TestBed.tick();
        http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true')).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
        http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
        TestBed.tick(); component.findSavedReceipts();
        const request = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${savedTask}`);
        if (change === 'session') session.current.set({ userId, firmId: userId, generation: '2', staff: true });
        else if (change === 'availability') TestBed.inject(WorkspaceAvailability).invalidate();
        else sessionStorage.setItem(storageKey, '88888888-8888-4888-8888-888888888888');
        request.flush({ engagementId: task.engagementId, truncated: false, tasks: [{ id: savedTask, assigneeUserId: null,
          assigneeDisplayName: null, revision: 1, status: 'OPEN' }] });
        await Promise.resolve(); await Promise.resolve(); TestBed.tick(); fixture.detectChanges();
        expect(host.textContent).not.toContain(savedTask);
        expect(component.visibleSavedReceipts()).toEqual([]);
        expect(sessionStorage.getItem(storageKey)).not.toBeNull();
      } finally { sessionStorage.removeItem(storageKey); fixture.destroy(); TestBed.resetTestingModule(); }
    }
  });

  it('reports an incomplete scan for a malformed current-owner key without reading or deleting it', () => {
    const { fixture, http, component, userId } = start();
    const storageKey = `auditsphere.workflow-evidence:${userId}:${userId}:${task.engagementId}:invalid`;
    try {
      sessionStorage.setItem(storageKey, 'unrecognized');
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true')).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); component.findSavedReceipts();
      expect(component.receiptMessage()).toContain('incomplete');
      expect(sessionStorage.getItem(storageKey)).toBe('unrecognized');
      http.expectNone(request => request.url.includes('/workflow/tasks?taskId='));
    } finally { sessionStorage.removeItem(storageKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('fences a malformed same-task key without blocking another task', () => {
    const { fixture, http, component, userId } = start();
    const otherId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    const storageKey = `auditsphere.workflow-evidence:${userId}:${userId}:${task.engagementId}:${task.taskId}:extra`;
    try {
      sessionStorage.setItem(storageKey, 'malformed');
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task, { ...task, taskId: otherId }], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(component.taskMutationBlocked(task.taskId)).toBe(true);
      expect(component.taskMutationBlocked(otherId)).toBe(false);
      expect(sessionStorage.getItem(storageKey)).toBe('malformed');
    } finally { sessionStorage.removeItem(storageKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('keeps the applicability receipt panel through a queue refresh and removes it after confirmed scope loss', () => {
    const { fixture, http, host, component } = start();
    const conditional = { ...task, applicabilityCondition: 'Synthetic condition', requiredTask: true };
    const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [conditional], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); fixture.detectChanges();
      const trigger = [...host.querySelectorAll<HTMLButtonElement>('button')].find(x => x.textContent?.trim() === 'Review conditional applicability')!;
      trigger.click(); TestBed.tick(); fixture.detectChanges();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/applicability`).flush({
        engagementId: task.engagementId, taskId: task.taskId, taskRevision: '1', latestProposalId: null,
        proposal: null, preview: null, canPropose: false, canApprove: false, canReturn: false, canRevoke: false });
      TestBed.tick();
      const retainedPanel = host.querySelector('audit-workflow-task-applicability');
      expect(retainedPanel).not.toBeNull();
      component.queue.reload(); TestBed.tick();
      expect(component.applicabilityTask()?.taskId).toBe(task.taskId);
      fixture.detectChanges(); expect(host.querySelector('audit-workflow-task-applicability')).toBe(retainedPanel);
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [conditional], hasMore: false, nextCursor: null });
      TestBed.tick(); fixture.detectChanges(); expect(host.querySelector('audit-workflow-task-applicability')).not.toBeNull();
      component.queue.reload(); TestBed.tick(); http.expectOne(queueUrl).flush({}, { status: 403, statusText: 'Scope removed' });
      TestBed.tick(); fixture.detectChanges(); expect(component.applicabilityTask()).toBeNull();
      expect(host.querySelector('audit-workflow-task-applicability')).toBeNull(); http.verify();
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
  it('does not use the previous task workspace to reveal a newly selected task panel', () => {
    const { fixture, http, host, component } = start();
    const first = { ...task, applicabilityCondition: 'First condition', requiredTask: true };
    const second = { ...first, taskId: '99999999-9999-4999-8999-999999999999', title: 'Other task' };
    const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
    const workspace = (id: string) => `/api/ui/engagements/${task.engagementId}/workflow/tasks/${id}/applicability`;
    const result = (id: string) => ({ engagementId: task.engagementId, taskId: id, taskRevision: '1', latestProposalId: null,
      proposal: null, preview: null, canPropose: false, canApprove: false, canReturn: false, canRevoke: false });
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [first, second], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); component.selectApplicability(first); TestBed.tick(); fixture.detectChanges();
      http.expectOne(workspace(first.taskId)).flush(result(first.taskId)); TestBed.tick(); fixture.detectChanges();
      expect(host.querySelector('div[hidden] audit-workflow-task-applicability')).toBeNull();
      component.selectApplicability(second); fixture.detectChanges(); TestBed.tick(); fixture.detectChanges();
      expect(host.querySelector('div[hidden] audit-workflow-task-applicability')).toBeTruthy();
      http.expectOne(workspace(second.taskId)).flush(result(second.taskId)); TestBed.tick(); fixture.detectChanges();
      expect(host.querySelector('div[hidden] audit-workflow-task-applicability')).toBeNull();
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('preserves an unresolved applicability receipt through a transient availability loss', async () => {
    const { fixture, http, host, component, userId } = start();
    const conditional = { ...task, applicabilityCondition: 'Synthetic condition', requiredTask: true };
    const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
    const workspaceUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/applicability`;
    const storageKey = `auditsphere.workflow-applicability:${userId}:${userId}:${task.engagementId}:${task.taskId}`;
    const requestKey = '77777777-7777-4777-8777-777777777777';
    try {
      sessionStorage.setItem(storageKey, requestKey);
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [conditional], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); fixture.detectChanges();
      [...host.querySelectorAll<HTMLButtonElement>('button')].find(x => x.textContent?.trim() === 'Review conditional applicability')!.click();
      TestBed.tick(); fixture.detectChanges();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, taskId: task.taskId, taskRevision: '1', latestProposalId: null,
        proposal: null, preview: null, canPropose: false, canApprove: false, canReturn: false, canRevoke: false });
      TestBed.tick();
      const panel = fixture.debugElement.query(By.directive(WorkflowTaskApplicabilityPanel)).componentInstance as WorkflowTaskApplicabilityPanel;
      expect(panel.pending()).toBe(requestKey);
      const access = TestBed.inject(WorkspaceAvailability), previous = access.current();
      access.invalidate(); TestBed.tick(); fixture.detectChanges();
      expect(fixture.debugElement.query(By.directive(WorkflowTaskApplicabilityPanel)).componentInstance).toBe(panel);
      expect(host.querySelector('div[hidden] audit-workflow-task-applicability')).toBeTruthy();
      component.selectView('MY_TASKS'); await fixture.whenStable(); TestBed.tick();
      expect(panel.pending()).toBe(requestKey);
      (access.current as WritableSignal<WorkspaceAvailabilityView | null>).set(previous);
      (access.revision as WritableSignal<number>).update(value => value + 1);
      TestBed.tick(); fixture.detectChanges();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [conditional], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      expect(host.querySelector('div[hidden] audit-workflow-task-applicability')).toBeTruthy();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, taskId: task.taskId, taskRevision: '1', latestProposalId: null,
        proposal: null, preview: null, canPropose: false, canApprove: false, canReturn: false, canRevoke: false });
      TestBed.tick(); fixture.detectChanges();
      expect(host.querySelector('div[hidden] audit-workflow-task-applicability')).toBeNull();
      expect(panel.pending()).toBe(requestKey); expect(sessionStorage.getItem(storageKey)).toBe(requestKey);
      [...host.querySelectorAll<HTMLButtonElement>('button')].find(x => x.textContent?.trim() === 'Close conditional applicability')!.click();
      TestBed.tick(); fixture.detectChanges();
      expect(host.querySelector('audit-workflow-task-applicability')).toBeNull();
      [...host.querySelectorAll<HTMLButtonElement>('button')].find(x => x.textContent?.trim() === 'Review conditional applicability')!.click();
      TestBed.tick(); fixture.detectChanges();
      const reopened = fixture.debugElement.query(By.directive(WorkflowTaskApplicabilityPanel)).componentInstance as WorkflowTaskApplicabilityPanel;
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, taskId: task.taskId, taskRevision: '1', latestProposalId: null,
        proposal: null, preview: null, canPropose: false, canApprove: false, canReturn: false, canRevoke: false });
      TestBed.tick(); expect(reopened.pending()).toBe(requestKey);
      http.expectNone(request => request.method === 'POST'); http.verify({ ignoreCancelled: true });
    } finally { sessionStorage.removeItem(storageKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });
  const openReopening = (test: ReturnType<typeof start>) => {
    const { fixture, http, host } = test;
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
      .flush({ view: 'MY_TASKS', tasks: [reopenableTask], hasMore: false, nextCursor: null, summary: summary() });
    http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
    TestBed.tick();
    const trigger = host.querySelector<HTMLButtonElement>(`button[data-reopen-task="${task.taskId}"]`)!;
    trigger.focus(); trigger.click(); fixture.detectChanges(); TestBed.tick();
    return fixture.debugElement.query(By.directive(WorkflowTaskCancellationPanel)).componentInstance as WorkflowTaskCancellationPanel;
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
        .flush({ view: 'MY_TASKS', tasks: [
          { ...cancelableTask, requiredTask: true, unmetTaskDependencies: 1, templateRevisionId: task.taskId, templateStepId: task.clientId, mappingVersionId: task.engagementId, fsliCode: 'CASH', pbcRequestId: task.taskId, pbcState: 'SENT', reviewState: 'CHANGES_REQUIRED', reviewComment: 'Reconcile the retained cash evidence' },
          { ...task, taskId: '77777777-7777-4777-8777-777777777777', title: 'Resubmitted procedure work', reviewState: 'UNDER_REVIEW', reviewComment: 'Resubmitted for independent review' },
          { ...task, taskId: '55555555-5555-4555-8555-555555555555', title: 'Optional evidence check', requiredTask: false },
          { ...task, taskId: '88888888-8888-4888-8888-888888888888', title: 'Inventory count', requiredTask: true, applicabilityCondition: 'Only where inventory exists' },
          { ...task, taskId: '66666666-6666-4666-8666-666666666666', title: 'Manual follow-up', requiredTask: null },
        ], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(host.querySelector('#review-inbox-heading')?.textContent).toBe('Submitted work awaiting review');
      expect(host.textContent).toContain('Earlier operational tasks may still exist.');
      const reviewScroll = host.querySelector<HTMLElement>('[role="region"][aria-label="Submitted procedure reviews"]');
      expect(reviewScroll?.tabIndex).toBe(0);
      reviewScroll?.focus();
      expect(document.activeElement).toBe(reviewScroll);
      const taskScroll = host.querySelector<HTMLElement>('[role="region"][aria-label="Operational task list"]');
      expect(taskScroll?.tabIndex).toBe(0);
      taskScroll?.focus();
      expect(document.activeElement).toBe(taskScroll);
      for (const display of ['LIST', 'BOARD', 'CALENDAR'] as const) {
        component.display.set(display); fixture.detectChanges(); TestBed.tick();
        const labels = host.querySelectorAll('.prerequisite-status');
        expect(labels.length).toBe(1);
        expect(labels[0].textContent).toContain('Prerequisites: blocked');
        expect(host.textContent).toContain('Source: MANUAL:fixture');
        expect(host.textContent).toContain('Template task designation: Required');
        expect(host.textContent).toContain('Template task designation: Optional');
        expect(host.textContent).toContain('Template task designation: Conditional');
        expect(host.textContent).toContain('Applies when: Only where inventory exists');
        expect(host.textContent).toContain('Task group: Audit planning');
        expect(host.textContent).toContain('template revision ' + task.taskId);
        expect(host.textContent).toContain('step ' + task.clientId);
        expect(host.textContent).toContain('FSLI CASH');
        expect(host.textContent).toContain('mapping ' + task.engagementId);
        expect(host.textContent).toContain('Reviewer: Reconcile the retained cash evidence');
        expect(host.textContent).toContain('current audit result needs resubmission');
        expect(host.querySelector('a[href$="/pbc"]')?.textContent).toContain('PBC request');
        expect([...host.querySelectorAll('button')].some(button => button.textContent?.includes('View activity'))).toBe(true);
        expect(host.querySelectorAll('[data-cancel-task]')).toHaveLength(1);
        expect(host.querySelector('[data-cancel-task]')?.getAttribute('data-cancel-task')).toBe(task.taskId);
        expect(host.textContent).toContain(task.title);

        const renderedTasks = Array.from(host.querySelectorAll<HTMLElement>(
          display === 'LIST' ? 'tbody tr' : 'article.task-card'));
        const renderedTask = (title: string) => renderedTasks.find(item => item.textContent?.includes(title));
        const expectedFieldworkHref = `/app/engagements/${task.engagementId}/audit-fieldwork`;
        for (const title of [task.title, 'Resubmitted procedure work']) {
          const reviewTask = renderedTask(title);
          expect(reviewTask).toBeDefined();
          expect(reviewTask?.querySelector<HTMLAnchorElement>('a[href$="/audit-fieldwork"]')?.getAttribute('href'))
            .toBe(expectedFieldworkHref);
          expect(reviewTask?.querySelector('a[href$="/audit-fieldwork"]')?.textContent?.trim()).toBe('Open fieldwork');
        }
        const ordinaryTask = renderedTask('Optional evidence check');
        expect(ordinaryTask).toBeDefined();
        expect(ordinaryTask?.querySelector('a[href$="/audit-fieldwork"]')).toBeNull();
      }
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('fails closed when a legacy cancelled task omits the server-projected reopen affordance', () => {
    const { fixture, http, host } = start();
    const legacyCancelledTask = Object.fromEntries(Object.entries({ ...task, taskStatus: 'CANCELLED' })
      .filter(([key]) => key !== 'canReopen'));
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [legacyCancelledTask], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(host.querySelector('[data-reopen-task]')).toBeNull();
      expect(host.textContent).not.toContain('Reopen task');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('shows the explicit reopen action in list and board only for a server-authorized cancelled task', async () => {
    const { fixture, http, host, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.selectView('CANCELLED'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=CANCELLED&pageSize=50&includeSummary=true'))
        .flush({ view: 'CANCELLED', tasks: [reopenableTask], hasMore: false, nextCursor: null, summary: summary() });
      fixture.detectChanges(); TestBed.tick();
      for (const display of ['LIST', 'BOARD'] as const) {
        component.display.set(display); fixture.detectChanges(); TestBed.tick();
        const trigger = host.querySelector<HTMLButtonElement>(`button[data-reopen-task="${task.taskId}"]`)!;
        expect(trigger.textContent?.trim()).toBe('Reopen task');
        trigger.focus(); trigger.click(); fixture.detectChanges(); TestBed.tick();
        const panel = fixture.debugElement.query(By.directive(WorkflowTaskCancellationPanel)).componentInstance as WorkflowTaskCancellationPanel;
        expect(panel.action()).toBe('reopen');
        expect(host.querySelector('section[aria-labelledby="cancellation-heading"] h2')?.textContent?.trim()).toBe('Review task reopening');
        expect(host.querySelector('label[for="task-cancellation-reason"]')?.textContent).toContain('Reason for reopening');
        expect([...host.querySelectorAll('audit-workflow-task-cancellation button')].some(button => button.textContent?.trim() === 'Review reopening')).toBe(true);
        component.closeCancellation(); fixture.detectChanges(); TestBed.tick();
      }
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('offers completed-work correction only when the server allows reopening the completed task', async () => {
    const { fixture, http, host, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.selectView('COMPLETED'); await fixture.whenStable(); TestBed.tick();
      const otherTask = { ...task, taskId: '55555555-5555-4555-8555-555555555555', title: 'Dependent already done', taskStatus: 'COMPLETED' };
      http.expectOne(tasksUrl('view=COMPLETED&pageSize=50&includeSummary=true')).flush({ view: 'COMPLETED',
        tasks: [{ ...task, taskStatus: 'COMPLETED', canReopen: true }, otherTask], hasMore: false, nextCursor: null, summary: summary() });
      fixture.detectChanges(); TestBed.tick();
      expect(host.querySelector(`button[data-reopen-task="${otherTask.taskId}"]`)).toBeNull();
      const trigger = host.querySelector<HTMLButtonElement>(`button[data-reopen-task="${task.taskId}"]`)!;
      expect(trigger.textContent?.trim()).toBe('Reopen task');
      trigger.focus(); trigger.click(); fixture.detectChanges(); TestBed.tick();
      const panel = host.querySelector('section[aria-labelledby="cancellation-heading"]')!;
      expect(panel.querySelector('h2')?.textContent?.trim()).toBe('Review task reopening');
      expect(panel.textContent).toContain('returns this completed task to active work');
      expect(panel.textContent).toContain('tasks that depend on it wait until it is completed again');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('offers hold and resume only from server flags and explains each in the review panel', async () => {
    const { fixture, http, host, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.selectView('ON_HOLD'); await fixture.whenStable(); TestBed.tick();
      const held = { ...task, onHold: true, holdReason: 'Client asked to pause.', canResume: true };
      const readOnly = { ...task, taskId: '55555555-5555-4555-8555-555555555555', onHold: true, holdReason: 'Paused.' };
      http.expectOne(tasksUrl('view=ON_HOLD&pageSize=50&includeSummary=true'))
        .flush({ view: 'ON_HOLD', tasks: [held, readOnly], hasMore: false, nextCursor: null, summary: summary() });
      fixture.detectChanges(); TestBed.tick();
      expect(host.textContent).toContain('Hold reason: Client asked to pause.');
      expect(host.querySelector(`button[data-hold-task="${readOnly.taskId}"]`)).toBeNull();
      const resume = host.querySelector<HTMLButtonElement>(`button[data-hold-task="${task.taskId}"]`)!;
      expect(resume.textContent?.trim()).toBe('Resume task');
      resume.focus(); resume.click(); fixture.detectChanges(); TestBed.tick();
      const panel = () => host.querySelector('section[aria-labelledby="cancellation-heading"]')!;
      expect(panel().querySelector('h2')?.textContent?.trim()).toBe('Review resuming the task');
      expect(panel().textContent).toContain('Reason for resumption');
      component.closeCancellation(); fixture.detectChanges(); TestBed.tick();
      component.selectView('TEAM'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true'))
        .flush({ view: 'TEAM', tasks: [{ ...task, canSuspend: true }], hasMore: false, nextCursor: null, summary: summary() });
      fixture.detectChanges(); TestBed.tick();
      const hold = host.querySelector<HTMLButtonElement>(`button[data-hold-task="${task.taskId}"]`)!;
      expect(hold.textContent?.trim()).toBe('Put on hold');
      hold.focus(); hold.click(); fixture.detectChanges(); TestBed.tick();
      expect(panel().querySelector('h2')?.textContent?.trim()).toBe('Review putting the task on hold');
      expect(panel().textContent).toContain('cannot be completed or receive new time until a manager resumes it');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('restores focus to the current queue view when a reopened cancelled row has disappeared', async () => {
    const { fixture, http, host, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.selectView('CANCELLED'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=CANCELLED&pageSize=50&includeSummary=true'))
        .flush({ view: 'CANCELLED', tasks: [reopenableTask], hasMore: false, nextCursor: null, summary: summary() });
      fixture.detectChanges(); TestBed.tick();

      const trigger = host.querySelector<HTMLButtonElement>(`button[data-reopen-task="${task.taskId}"]`)!;
      trigger.focus(); trigger.click(); fixture.detectChanges(); TestBed.tick();
      expect(fixture.debugElement.query(By.directive(WorkflowTaskCancellationPanel))).not.toBeNull();

      component.queue.reload(); TestBed.tick();
      http.expectOne(tasksUrl('view=CANCELLED&pageSize=50&includeSummary=true'))
        .flush({ view: 'CANCELLED', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      fixture.detectChanges(); TestBed.tick();
      expect(host.querySelector('[data-reopen-task]')).toBeNull();

      component.closeCancellation(); fixture.detectChanges(); TestBed.tick();
      expect(host.querySelector('.queue-views button[aria-pressed="true"]')).toBe(document.activeElement);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('renders exact complete counts above the review inbox and opens each card view with paging and assignment reset', async () => {
    const { fixture, http, userId, host, overview, cards, card, countOf, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [legacyTask], hasMore: false, nextCursor: null, summary: summary({ MY_TASKS: '101', OVERDUE: '1234567' }) });
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
      expect(Array.from(display.querySelectorAll('button')).map(x => x.textContent?.trim())).toEqual(['List', 'Board', 'Calendar', 'Grouped']);
      const buttons = Array.from(host.querySelectorAll('button')).map(x => x.textContent?.trim());
      expect(buttons).toContain('Review prerequisites');
      expect(buttons).toContain('Review task ordering');
      expect(buttons).toContain('View activity');
      expect(host.querySelector('[data-cancel-task]')).toBeNull(); // Missing server affordance fails closed.
      // Time capture is a link to the existing time page with the task as context; it records nothing here.
      expect(host.querySelector(`a[href="/app/practice/time?taskId=${task.taskId}"]`)?.textContent).toBe('Record time for this task');

      component.selectView('TEAM'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true')).flush({ view: 'TEAM', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      component.selectOwnerFilter('ME'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl(`view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}`)).flush({ view: 'TEAM', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      component.nextPage({ dueDate: '2026-10-15', taskId: task.taskId }); TestBed.tick();
      http.expectOne(tasksUrl(`view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}&afterDueDate=2026-10-15&afterTaskId=${task.taskId}`))
        .flush({ view: 'TEAM', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      card('Due today').click(); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=DUE_TODAY&pageSize=50&includeSummary=true')).flush({ view: 'DUE_TODAY', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      expect(component.selectedView()).toBe('DUE_TODAY');
      expect(component.ownerFilter()).toBe('ALL');
      expect(card('Due today').getAttribute('aria-pressed')).toBe('true');

      component.selectOwnerFilter('UNASSIGNED'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=DUE_TODAY&pageSize=50&includeSummary=true&unassignedOnly=true')).flush({ view: 'DUE_TODAY', tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
      component.nextPage({ dueDate: null, taskId: task.taskId }); TestBed.tick();
      http.expectOne(tasksUrl(`view=DUE_TODAY&pageSize=50&includeSummary=true&unassignedOnly=true&afterTaskId=${task.taskId}`))
        .flush({ view: 'DUE_TODAY', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      card('Rework required').click(); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=CHANGES_REQUIRED&pageSize=50&includeSummary=true')).flush({ view: 'CHANGES_REQUIRED', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      expect(component.selectedView()).toBe('CHANGES_REQUIRED');
      expect(component.ownerFilter()).toBe('ALL');
      expect(host.textContent).toContain('Changes required — workflow tasks');
      expect([...host.querySelectorAll('[role="status"]')].some(x => x.textContent?.includes('Changes required: 0 in this authorized view.'))).toBe(true);
      component.selectView('UNDER_REVIEW'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=UNDER_REVIEW&pageSize=50&includeSummary=true'))
        .flush({ view: 'UNDER_REVIEW', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      expect([...host.querySelectorAll('[role="status"]')].some(x => x.textContent?.includes('Under review: 0 in this authorized view.'))).toBe(true);


      component.selectView('CANCELLED'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=CANCELLED&pageSize=50&includeSummary=true'))
        .flush({ view: 'CANCELLED', tasks: [{ ...task, taskStatus: 'CANCELLED' }], hasMore: false, nextCursor: null, summary: summary() });
      TestBed.tick();
      expect(component.selectedView()).toBe('CANCELLED');
      expect(cards()).toHaveLength(7);
      for (const layout of ['LIST', 'BOARD', 'CALENDAR'] as const) {
        component.display.set(layout); fixture.detectChanges(); TestBed.tick();
        expect(host.textContent).toContain(task.title);
      }

      expect(http.match(request => request.method !== 'GET')).toEqual([]);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('shows the unavailable notice for a legacy summary and the safe error for a malformed one, never zero cards', async () => {
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

      component.selectView('TEAM'); await fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true')).flush({ view: 'TEAM', tasks: [task], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(overview().textContent).toContain(unavailable);
      expect(cards()).toEqual([]);

      component.selectView('OVERDUE'); await fixture.whenStable(); TestBed.tick();
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

  it('hides prior counts while loading and never renders counts from a delayed response after session invalidation', async () => {
    const { fixture, http, session, userId, overview, cards, card, countOf, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null, summary: summary({ MY_TASKS: '101' }) });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(countOf('My tasks')).toBe('101');

      card('Due today').click(); await fixture.whenStable(); TestBed.tick();
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
      const key = 'auditsphere.work-queue-actions.v1:11111111-1111-4111-8111-111111111111:11111111-1111-4111-8111-111111111111';
      const saved = JSON.parse(sessionStorage.getItem(key)!);
      expect(saved.entries).toEqual([expect.objectContaining({ taskId: task.taskId, action: 'reassign', requestedAssigneeId: '55555555-5555-4555-8555-555555555555' })]);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('does not accept a completion response observed after availability changes', async () => {
    const { fixture, http, component } = start();
    const registryKey = 'auditsphere.work-queue-actions.v1:11111111-1111-4111-8111-111111111111:11111111-1111-4111-8111-111111111111';
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.complete(task.engagementId, task.taskId, task.revision);
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/complete`);
      const access = TestBed.inject(WorkspaceAvailability), previous = access.current();
      access.invalidate();
      post.flush({ value: true });
      await new Promise(resolve => setTimeout(resolve, 0));
      expect(component.registryRecords()).toHaveLength(1);
      expect(component.actionMessage()).not.toContain('Task completed.');
      const requestId = component.registryRecords()[0].requestId;
      (access.current as WritableSignal<WorkspaceAvailabilityView | null>).set(previous);
      (access.revision as WritableSignal<number>).update(value => value + 1);
      TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const unchanged = component.observeAction(requestId);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`).flush({
        engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName,
          revision: task.revision, status: 'IN_PROGRESS', dueDate: task.dueDate }],
      });
      await unchanged;
      component.acknowledgeAction(requestId);
      expect(component.registryRecords()).toHaveLength(1);
      const checking = component.observeAction(requestId);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`).flush({
        engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName,
          revision: task.revision + 1, status: 'COMPLETED', dueDate: task.dueDate }],
      });
      await checking;
      expect(component.actionObservations()[requestId]?.row.status).toBe('COMPLETED');
      component.acknowledgeAction(requestId);
      expect(component.registryRecords()).toHaveLength(0);
      expect(component.actionMessage()).toContain('outcome is not established');
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectNone(request => request.method === 'POST');
      http.verify({ ignoreCancelled: true });
    } finally { sessionStorage.removeItem(registryKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('retains independent uncertain actions and blocks only their tasks', async () => {
    const { fixture, http, component } = start();
    const other = { ...task, taskId: '55555555-5555-4555-8555-555555555555', canComplete: true, canChangeDueDate: true };
    const first = { ...task, canComplete: true, canChangeDueDate: true };
    const unrelated = { ...task, taskId: '66666666-6666-4666-8666-666666666666', canComplete: true };
    const registryKey = `auditsphere.work-queue-actions.v1:${task.assigneeUserId}:${task.assigneeUserId}`;
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [first, other, unrelated], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.complete(first.engagementId, first.taskId, first.revision);
      const completion = http.expectOne(`/api/ui/engagements/${first.engagementId}/workflow/tasks/${first.taskId}/complete`);
      component.changeDueDate(other.engagementId, other.taskId, other.revision, '2026-12-01');
      const due = http.expectOne(`/api/ui/engagements/${other.engagementId}/workflow/tasks/${other.taskId}/due-date`);
      expect(component.registryRecords()).toHaveLength(2);
      expect(JSON.parse(sessionStorage.getItem(registryKey)!).entries).toHaveLength(2);
      component.complete(first.engagementId, first.taskId, first.revision);
      http.expectNone(r => r.method === 'POST');
      completion.flush({}, { status: 503, statusText: 'Unknown' });
      due.flush({}, { status: 503, statusText: 'Unknown' });
      await new Promise(resolve => setTimeout(resolve, 0));
      expect(component.registryRecords()).toHaveLength(2);
      expect(component.registryRecords().find(x => x.taskId === other.taskId)?.requestedDueDate).toBe('2026-12-01');
      const pendingReads = http.match(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'));
      for (const read of pendingReads.filter(x => !x.cancelled)) read.flush({ view: 'MY_TASKS', tasks: [first, other, unrelated], hasMore: false, nextCursor: null });
      expect(component.taskMutationBlocked(first.taskId)).toBe(true);
      expect(component.taskMutationBlocked(other.taskId)).toBe(true);
      expect(component.taskMutationBlocked(unrelated.taskId)).toBe(false);
      component.complete(unrelated.engagementId, unrelated.taskId, unrelated.revision);
      http.expectOne(`/api/ui/engagements/${unrelated.engagementId}/workflow/tasks/${unrelated.taskId}/complete`).flush({ value: true });
      await new Promise(resolve => setTimeout(resolve, 0));
      expect(component.registryRecords()).toHaveLength(2);
      for (const read of http.match(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true')).filter(x => !x.cancelled))
        read.flush({ view: 'MY_TASKS', tasks: [first, other], hasMore: false, nextCursor: null });
      http.expectNone(r => r.method === 'POST'); http.verify({ ignoreCancelled: true });
    } finally { sessionStorage.removeItem(registryKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('does not dispatch if recovery storage cannot persist the reservation', () => {
    const { fixture, http, component } = start();
    const spy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage unavailable'); });
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.complete(task.engagementId, task.taskId, task.revision);
      http.expectNone(r => r.method === 'POST');
      expect(component.actionMessage()).toContain('No action was sent');
      http.verify({ ignoreCancelled: true });
    } finally { spy.mockRestore(); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('cleans an uninvoked parent reservation after its write succeeds but readback fails, without a POST', async () => {
    const { fixture, http, host, component, userId } = start();
    const registryKey = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
    let fault: ReturnType<typeof failRegistryReadback> | undefined;
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      fault = failRegistryReadback(registryKey, 1);
      component.complete(task.engagementId, task.taskId, task.revision);
      expect(fault.injected()).toBe(true);
      expect(component.unsentIds()).toHaveLength(1);
      expect(JSON.parse(sessionStorage.getItem(registryKey)!).entries).toHaveLength(1);
      http.expectNone(request => request.method === 'POST');
      const verified = component.verifyRetainedActions();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'OPEN' }] });
      await verified;
      fixture.detectChanges();
      [...host.querySelectorAll<HTMLButtonElement>('button')]
        .find(button => button.textContent?.includes('Remove unsent recovery reference'))!.click();
      expect(component.unsentIds()).toEqual([]);
      expect(JSON.parse(sessionStorage.getItem(registryKey)!).entries).toEqual([]);
      expect(component.actionMessage()).toContain('unsent recovery reference was removed');
      http.expectNone(request => request.method === 'POST');
      http.verify({ ignoreCancelled: true });
    } finally { fault?.restore(); sessionStorage.removeItem(registryKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('finishes parent definitive cleanup and refreshes the queue while preserving success or refusal', async () => {
    for (const outcome of ['success', 'refusal'] as const) {
      const { fixture, http, host, component, userId } = start();
      const registryKey = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
      const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
      let fault: ReturnType<typeof failRegistryReadback> | undefined;
      try {
        fixture.detectChanges(); TestBed.tick();
        http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
        http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
        TestBed.tick();
        component.complete(task.engagementId, task.taskId, task.revision);
        const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/complete`);
        fault = failRegistryReadback(registryKey, 0);
        if (outcome === 'success') post.flush({ value: true });
        else post.flush({ code: 'workflow.task-invalid', message: 'Action refused.' }, { status: 400, statusText: 'Bad Request' });
        await new Promise(resolve => setTimeout(resolve, 0));
        expect(fault.injected()).toBe(true);
        expect(JSON.parse(sessionStorage.getItem(registryKey)!).entries).toEqual([]);
        expect(component.definitiveCleanupIds()).toHaveLength(1);
        const verified = component.verifyRetainedActions();
        http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`).flush({ engagementId: task.engagementId, truncated: false,
          tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName,
            revision: task.revision + 1, status: outcome === 'success' ? 'COMPLETED' : 'OPEN' }] });
        await verified;
        fixture.detectChanges();
        [...host.querySelectorAll<HTMLButtonElement>('button')]
          .find(button => button.textContent?.includes('Finish recovery reference cleanup'))!.click();
        expect(component.definitiveCleanupIds()).toEqual([]);
        expect(component.actionFailed()).toBe(outcome === 'refusal');
        expect(component.actionMessage()).toBe(outcome === 'success' ? 'Task completed.' : 'The task action was refused. Review the current task.');
        http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
        http.expectNone(request => request.method === 'POST');
        http.verify({ ignoreCancelled: true });
      } finally { fault?.restore(); sessionStorage.removeItem(registryKey); fixture.destroy(); TestBed.resetTestingModule(); }
    }
  });

  it('rechecks exact task access before finalizing a cancellation whose removal readback failed', async () => {
    const test = start();
    const { fixture, http, host, component, userId } = test;
    const registryKey = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
    const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
    const exactUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    let fault: ReturnType<typeof failRegistryReadback> | undefined;
    try {
      const panel = openCancellation(test);
      panel.updateReason('The engagement scope changed.'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`);
      fault = failRegistryReadback(registryKey, 0);
      post.flush({ value: true }); await sending;
      expect(fault.injected()).toBe(true);
      expect(panel.state()).toBe('finalizing');
      expect(panel.activeRegistryRequest()).not.toBeNull();
      expect(JSON.parse(sessionStorage.getItem(registryKey)!).entries).toEqual([]);
      const access = TestBed.inject(WorkspaceAvailability), previous = access.current();
      access.invalidate(); TestBed.tick(); fixture.detectChanges();
      expect(panel.state()).toBe('finalizing');
      expect(host.querySelector('div[hidden] audit-workflow-task-cancellation')).toBeTruthy();
      (access.current as WritableSignal<WorkspaceAvailabilityView | null>).set(previous);
      (access.revision as WritableSignal<number>).update(value => value + 1);
      TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); fixture.detectChanges();
      expect(component.cancellationAccessCurrent()).toBe(false);
      panel.retryDefinitiveCleanup();
      expect(panel.state()).toBe('finalizing');
      const denied = component.verifyRetainedActions();
      http.expectOne(exactUrl).flush({}, { status: 403, statusText: 'Forbidden' }); await denied;
      TestBed.tick(); fixture.detectChanges();
      expect(component.cancellationAccessCurrent()).toBe(false);
      expect(host.querySelector('div[hidden] audit-workflow-task-cancellation')).toBeTruthy();
      expect(panel.activeRegistryRequest()).not.toBeNull();
      const verified = component.verifyRetainedActions();
      http.expectOne(exactUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName,
          revision: task.revision + 1, status: 'CANCELLED' }] });
      await verified; TestBed.tick(); fixture.detectChanges();
      expect(component.cancellationAccessCurrent()).toBe(true);
      [...host.querySelectorAll<HTMLButtonElement>('audit-workflow-task-cancellation button')]
        .find(button => button.textContent?.includes('Retry clearing completed recovery reference'))!.click();
      expect(panel.state()).toBe('confirmed');
      expect(panel.activeRegistryRequest()).toBeNull();
      expect(JSON.parse(sessionStorage.getItem(registryKey)!).entries).toEqual([]);
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectNone(request => request.method === 'POST');
      http.verify({ ignoreCancelled: true });
    } finally { fault?.restore(); sessionStorage.removeItem(registryKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('discovers failed generic and assignment acknowledgements and reconciles only after a fresh task read', async () => {
    for (const mode of ['generic', 'assignment'] as const) {
      const { fixture, http, component, userId } = start();
      const registryKey = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
      const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
      const exactUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
      const advanced = { engagementId: task.engagementId, truncated: false, tasks: [{ id: task.taskId,
        assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision + 1, status: 'IN_PROGRESS' }] };
      let fault: ReturnType<typeof failRegistryReadback> | undefined;
      try {
        fixture.detectChanges(); TestBed.tick();
        http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
        http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
        TestBed.tick();
        if (mode === 'assignment') {
          const opening = component.openAssignment(task);
          http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
            .flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Replacement' }]); await opening;
          component.selectAssignmentOwner('55555555-5555-4555-8555-555555555555'); component.assignmentReviewed.set(true);
          const sending = component.saveAssignment();
          http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`)
            .flush({}, { status: 503, statusText: 'Unknown' }); await sending;
          const checking = component.recoverAssignment();
          http.expectOne(exactUrl).flush(advanced);
          http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`)
            .flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId, events: [], hasMore: false, nextCursor: null });
          await checking;
        } else {
          component.complete(task.engagementId, task.taskId, task.revision);
          http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/complete`)
            .flush({}, { status: 503, statusText: 'Unknown' });
          await new Promise(resolve => setTimeout(resolve, 0));
          http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
          const checking = component.observeAction(component.registryRecords()[0].requestId);
          http.expectOne(exactUrl).flush(advanced); await checking;
        }
        const id = component.registryRecords()[0].requestId;
        fault = failRegistryReadback(registryKey, 0);
        if (mode === 'assignment') component.acknowledgeAssignmentObservation(); else component.acknowledgeAction(id);
        expect(fault.injected()).toBe(true);
        expect(component.reconciliation()).toMatchObject({ kind: 'acknowledgement', sameSession: true, item: { requestId: id } });
        expect(component.actionVerified(id)).toBe(false);
        component.finishReconciliation();
        expect(component.reconciliation()).not.toBeNull();
        const verified = component.verifyRetainedActions();
        http.expectOne(exactUrl).flush(advanced); await verified;
        component.finishReconciliation();
        expect(component.reconciliation()).toBeNull();
        expect(component.actionMessage()).toContain('outcome is not established');
        expect(component.assignmentTask()).toBeNull();
        http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
        http.expectNone(request => request.method === 'POST');
        http.verify({ ignoreCancelled: true });
      } finally { fault?.restore(); sessionStorage.removeItem(registryKey); fixture.destroy(); TestBed.resetTestingModule(); }
    }
  });

  it('recovers a same-session cleanup ticket after the queue is recreated without claiming the old outcome', async () => {
    const test = start();
    const { fixture, http, component, userId } = test;
    const key = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
    const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
    const exactUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    let fault: ReturnType<typeof failRegistryReadback> | undefined;
    let recreated: ComponentFixture<WorkflowWorkQueue> | undefined;
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); component.complete(task.engagementId, task.taskId, task.revision);
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/complete`);
      fault = failRegistryReadback(key, 0); post.flush({ value: true });
      await new Promise(resolve => setTimeout(resolve, 0));
      expect(component.reconciliation()).toMatchObject({ kind: 'definitive', sameSession: true });
      fault.restore(); fault = undefined; fixture.destroy();
      recreated = TestBed.createComponent(WorkflowWorkQueue); recreated.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); recreated.detectChanges();
      const next = recreated.componentInstance, host = recreated.nativeElement as HTMLElement;
      expect(next.reconciliation()).toMatchObject({ kind: 'definitive', sameSession: true });
      expect(host.textContent).not.toContain(task.taskId);
      next.finishReconciliation(); expect(next.reconciliation()).not.toBeNull();
      const access = TestBed.inject(WorkspaceAvailability), previous = access.current();
      const stale = next.verifyRetainedActions();
      const staleRead = http.expectOne(exactUrl);
      access.invalidate();
      staleRead.flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 2, status: 'COMPLETED' }] });
      await stale; next.finishReconciliation(); expect(next.reconciliation()).not.toBeNull();
      (access.current as WritableSignal<WorkspaceAvailabilityView | null>).set(previous);
      (access.revision as WritableSignal<number>).update(value => value + 1);
      TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const checking = next.verifyRetainedActions();
      http.expectOne(exactUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 2, status: 'COMPLETED' }] });
      await checking; next.finishReconciliation();
      expect(next.reconciliation()).toBeNull();
      expect(next.actionMessage()).toContain('outcome is not established');
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectNone(request => request.method === 'POST'); http.verify({ ignoreCancelled: true });
    } finally { fault?.restore(); recreated?.destroy(); fixture.destroy(); sessionStorage.removeItem(key); TestBed.resetTestingModule(); }
  });

  it('hands off a cancellation finalization after its panel and queue are destroyed', async () => {
    const test = start();
    const { fixture, http, userId } = test;
    const key = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
    const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
    let fault: ReturnType<typeof failRegistryReadback> | undefined;
    let recreated: ComponentFixture<WorkflowWorkQueue> | undefined;
    try {
      const panel = openCancellation(test);
      panel.updateReason('Reviewed reason'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`);
      fault = failRegistryReadback(key, 0); post.flush({ value: true }); await sending;
      expect(panel.state()).toBe('finalizing');
      fault.restore(); fault = undefined; fixture.destroy();
      recreated = TestBed.createComponent(WorkflowWorkQueue); recreated.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); recreated.detectChanges();
      const next = recreated.componentInstance;
      expect(next.reconciliation()).toMatchObject({ kind: 'definitive', sameSession: true, item: { action: 'cancel' } });
      expect((recreated.nativeElement as HTMLElement).textContent).not.toContain(task.taskId);
      const checking = next.verifyRetainedActions();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: null, assigneeDisplayName: null, revision: 2, status: 'CANCELLED' }] });
      await checking; next.finishReconciliation();
      expect(next.reconciliation()).toBeNull();
      expect(next.actionMessage()).toContain('outcome is not established');
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectNone(request => request.method === 'POST'); http.verify({ ignoreCancelled: true });
    } finally { fault?.restore(); recreated?.destroy(); fixture.destroy(); sessionStorage.removeItem(key); TestBed.resetTestingModule(); }
  });

  it('releases a prior-session serialization fence without a task read or storage write', async () => {
    const { fixture, http, component, session, userId } = start();
    const key = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
    const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
    let fault: ReturnType<typeof failRegistryReadback> | undefined;
    let recreated: ComponentFixture<WorkflowWorkQueue> | undefined;
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); component.complete(task.engagementId, task.taskId, task.revision);
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/complete`);
      fault = failRegistryReadback(key, 0); post.flush({ value: true });
      await new Promise(resolve => setTimeout(resolve, 0));
      fault.restore(); fault = undefined; fixture.destroy();
      session.current.set({ userId, firmId: userId, generation: '2', staff: true });
      recreated = TestBed.createComponent(WorkflowWorkQueue); recreated.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); recreated.detectChanges();
      const next = recreated.componentInstance, host = recreated.nativeElement as HTMLElement;
      const original = sessionStorage.getItem(key)!;
      expect(next.priorSessionFenceAvailable()).toBe(true);
      expect(host.textContent).not.toContain(task.taskId);
      session.current.set({ userId: '88888888-8888-4888-8888-888888888888', firmId: userId, generation: '2', staff: true });
      next.releasePriorSessionFence();
      expect(sessionStorage.getItem(key)).toBe(original);
      session.current.set({ userId, firmId: userId, generation: '2', staff: true });
      TestBed.inject(WorkspaceAvailability).invalidate(); TestBed.tick();
      expect(next.priorSessionFenceAvailable()).toBe(true);
      sessionStorage.setItem(key, JSON.stringify({ ...JSON.parse(original), unexpected: true }));
      next.releasePriorSessionFence();
      expect(next.priorSessionFenceAvailable()).toBe(true);
      expect(next.priorFenceMessage()).toContain('could not be checked');
      sessionStorage.setItem(key, original);
      next.loadRegistry();
      const writes = vi.spyOn(Storage.prototype, 'setItem');
      try { next.releasePriorSessionFence(); expect(writes).not.toHaveBeenCalled(); } finally { writes.mockRestore(); }
      expect(next.priorSessionFenceAvailable()).toBe(false);
      expect(sessionStorage.getItem(key)).toBe(original);
      expect(next.priorFenceMessage()).toBe('The storage check sent no task action and does not establish the outcome of any earlier action.');
      http.expectNone(request => request.method === 'POST' || request.url.includes('/workflow/tasks?taskId='));
      http.verify({ ignoreCancelled: true });
    } finally { fault?.restore(); recreated?.destroy(); fixture.destroy(); sessionStorage.removeItem(key); TestBed.resetTestingModule(); }
  });

  it('preserves an existing prior-session reservation when releasing its fence', () => {
    const { fixture, http, component, session, userId } = start();
    const key = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
    const queueUrl = tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true');
    let fault: ReturnType<typeof failRegistryReadback> | undefined;
    let recreated: ComponentFixture<WorkflowWorkQueue> | undefined;
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); fault = failRegistryReadback(key, 1);
      component.complete(task.engagementId, task.taskId, task.revision);
      expect(component.reconciliation()).toMatchObject({ kind: 'cleanup', sameSession: true });
      const original = sessionStorage.getItem(key)!;
      expect(JSON.parse(original).entries).toHaveLength(1);
      http.expectNone(request => request.method === 'POST');
      fault.restore(); fault = undefined; fixture.destroy();
      session.current.set({ userId, firmId: userId, generation: '2', staff: true });
      recreated = TestBed.createComponent(WorkflowWorkQueue); recreated.detectChanges(); TestBed.tick();
      http.expectOne(queueUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      const next = recreated.componentInstance;
      expect(next.priorSessionFenceAvailable()).toBe(true);
      next.releasePriorSessionFence();
      expect(next.priorSessionFenceAvailable()).toBe(false);
      expect(sessionStorage.getItem(key)).toBe(original);
      expect(next.registryRecords()).toHaveLength(1);
      http.expectNone(request => request.method === 'POST' || request.url.includes('/workflow/tasks?taskId='));
      http.verify({ ignoreCancelled: true });
    } finally { fault?.restore(); recreated?.destroy(); fixture.destroy(); sessionStorage.removeItem(key); TestBed.resetTestingModule(); }
  });

  it('fences a task with another panel receipt while allowing that panel to be reopened', () => {
    const { fixture, http, component, userId } = start();
    const storageKey = `auditsphere.workflow-evidence:${userId}:${userId}:${task.engagementId}:${task.taskId}`;
    try {
      sessionStorage.setItem(storageKey, '77777777-7777-4777-8777-777777777777');
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.complete(task.engagementId, task.taskId, task.revision);
      http.expectNone(r => r.method === 'POST');
      component.selectEvidence(task);
      expect(component.evidenceTask()?.taskId).toBe(task.taskId);
      expect(component.registryRecords()).toHaveLength(0);
      http.verify({ ignoreCancelled: true });
    } finally { sessionStorage.removeItem(storageKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('does not let a previous session response alter the new session or remove the retained action', async () => {
    const { fixture, http, component, session, userId } = start();
    const registryKey = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
    const nextUser = '55555555-5555-4555-8555-555555555555';
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.complete(task.engagementId, task.taskId, task.revision);
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/complete`);
      session.current.set({ userId: nextUser, firmId: userId, generation: '1', staff: true });
      TestBed.tick();
      const newQueue = http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'));
      post.flush({ value: true });
      await new Promise(resolve => setTimeout(resolve, 0));
      expect(newQueue.cancelled).toBe(false);
      expect(component.registryRecords()).toHaveLength(0);
      expect(component.actionMessage()).not.toContain('Task completed');
      expect(JSON.parse(sessionStorage.getItem(registryKey)!).entries).toHaveLength(1);
      newQueue.flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      for (const read of http.match('/api/ui/work/reviews?pageSize=50').filter(x => !x.cancelled))
        read.flush({ reviews: [], hasMore: false, nextCursor: null });
      http.verify({ ignoreCancelled: true });
    } finally { sessionStorage.removeItem(registryKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('keeps a retained action hidden until the exact current task is authorized', async () => {
    const { fixture, http, component, session, userId } = start();
    const registryKey = `auditsphere.work-queue-actions.v1:${userId}:${userId}`;
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      component.complete(task.engagementId, task.taskId, task.revision);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/complete`)
        .flush({}, { status: 503, statusText: 'Unknown' });
      await new Promise(resolve => setTimeout(resolve, 0));
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      const retainedId = component.registryRecords()[0].requestId;
      session.current.set({ userId, firmId: userId, generation: '2', staff: true });
      TestBed.tick();
      expect(component.registryRecords()).toHaveLength(1);
      expect(component.visibleActions()).toHaveLength(0);
      const checking = component.verifyRetainedActions();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`).flush({
        engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName,
          revision: task.revision + 1, status: 'COMPLETED', dueDate: task.dueDate }],
      });
      await checking;
      expect(component.visibleActions().map(x => x.requestId)).toEqual([retainedId]);
      component.acknowledgeAction(retainedId);
      expect(component.registryRecords()).toHaveLength(0);
      expect(component.actionMessage()).toContain('outcome is not established');
      for (const read of http.match(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true')).filter(x => !x.cancelled))
        read.flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null });
      for (const read of http.match('/api/ui/work/reviews?pageSize=50').filter(x => !x.cancelled))
        read.flush({ reviews: [], hasMore: false, nextCursor: null });
      http.verify({ ignoreCancelled: true });
    } finally { sessionStorage.removeItem(registryKey); fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('retains a submitted assignment when availability changes before its response', async () => {
    const { fixture, http, component } = start();
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const opening = component.openAssignment(task);
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`)
        .flush([{ userId: '55555555-5555-4555-8555-555555555555', displayName: 'Replacement staff' }]);
      await opening;
      component.selectAssignmentOwner('55555555-5555-4555-8555-555555555555'); component.assignmentReviewed.set(true);
      const sending = component.saveAssignment();
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reassign`);
      TestBed.inject(WorkspaceAvailability).invalidate();
      post.flush({ value: true }); await sending;
      expect(component.assignmentUncertain()).toBe(true);
      expect(component.assignmentTask()?.taskId).toBe(task.taskId);
      expect(component.canRetryAssignment()).toBe(false);
      http.expectNone(request => request.method === 'POST');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('fences an unknown assignment and permits only the original request after current task and activity are inspected', async () => {
    const { fixture, http, component } = start();
    const exactWorkspaceUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${encodeURIComponent(task.taskId)}`;
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
      const access = TestBed.inject(WorkspaceAvailability);
      const previous = access.current();
      access.invalidate(); TestBed.tick();
      expect(component.assignmentUncertain()).toBe(true);
      expect(component.assignmentTask()?.taskId).toBe(task.taskId);
      (access.current as WritableSignal<WorkspaceAvailabilityView | null>).set(previous);
      (access.revision as WritableSignal<number>).update(value => value + 1);
      TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(component.assignmentUncertain()).toBe(true);
      expect(component.assignmentAccessCurrent()).toBe(false);
      const verifyFirst = component.verifyRetainedActions();
      http.expectOne(exactWorkspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      await verifyFirst; TestBed.tick(); expect(component.assignmentAccessCurrent()).toBe(true);
      component.selectAssignmentOwner('77777777-7777-4777-8777-777777777777');
      component.closeAssignment(); await component.saveAssignment(); await component.retryAssignment();
      expect(component.assignmentUserId()).toBe(originalBody.assigneeUserId);
      expect(component.assignmentTask()?.taskId).toBe(task.taskId);
      http.expectNone(request => request.method === 'POST');
      const delayedChecking = component.recoverAssignment();
      const delayedTask = http.expectOne(exactWorkspaceUrl);
      const delayedActivity = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`);
      access.invalidate();
      delayedTask.flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      delayedActivity.flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId, events: [], hasMore: false, nextCursor: null });
      await delayedChecking;
      expect(component.canRetryAssignment()).toBe(false);
      (access.current as WritableSignal<WorkspaceAvailabilityView | null>).set(previous);
      (access.revision as WritableSignal<number>).update(value => value + 1);
      TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [task], hasMore: false, nextCursor: null });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      expect(component.assignmentAccessCurrent()).toBe(false);
      const verifyAgain = component.verifyRetainedActions();
      http.expectOne(exactWorkspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      await verifyAgain; TestBed.tick();
      const checking = component.recoverAssignment();
      // Exact lookup recovers a selected task that may sit beyond the unfiltered 500-task workspace window.
      http.expectOne(exactWorkspaceUrl).flush({ engagementId: task.engagementId,
        truncated: false, tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`)
        .flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId, events: [], hasMore: false, nextCursor: null });
      await checking;
      expect(component.canReviewAssignmentRetry()).toBe(true);
      expect(component.canRetryAssignment()).toBe(false);
      component.assignmentReviewed.set(true);
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

  it('retains a submitted cancellation when availability changes before its response', async () => {
    const test = start();
    const { fixture, http } = test;
    try {
      const panel = openCancellation(test);
      panel.updateReason('The engagement scope changed.'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      const post = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`);
      TestBed.inject(WorkspaceAvailability).invalidate();
      post.flush({ value: true }); await sending;
      expect(panel.state()).toBe('unknown');
      expect(panel.intent()?.reason).toBe('The engagement scope changed.');
      expect(panel.observed()).toBeNull();
      http.expectNone(request => request.method === 'POST');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('records the conditional reason without presenting cancellation as an applicability approval', () => {
    const test = start();
    const { fixture, http, host } = test;
    try {
      const panel = openCancellation(test, { requiredTask: true, templateStepId: task.taskId,
        applicabilityCondition: 'Only where the entity holds inventory' });
      expect(host.textContent).toContain('Applies when: Only where the entity holds inventory');
      expect(host.textContent).toContain('Cancelling this conditional step leaves it blocked in progress');
      expect(host.textContent).toContain('It is not an approved applicability decision');
      panel.updateReason('No inventory at the reporting date.');
      panel.prepareReview();
      expect(panel.intent()?.reason).toBe('No inventory at the reporting date.');
      panel.backToEditing();
      fixture.detectChanges();
      host.querySelector<HTMLInputElement>('#task-not-applicable')!.click();
      fixture.detectChanges();
      expect(panel.notApplicable()).toBe(true);
      panel.prepareReview();
      expect(panel.intent()?.reason).toBe('Not applicable: No inventory at the reporting date.');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('keeps exact cancellation intent after a lost reply and retries only after exact status inspection and renewed assent', async () => {
    const test = start();
    const { fixture, http, host, component } = test;
    const reason = '  Work stopped after the engagement scope changed.  ';
    const canonicalReason = reason.trim();
    const workspaceUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    try {
      const panel = openCancellation(test);
      expect(host.querySelector('section[aria-labelledby="cancellation-heading"] h2')?.textContent).toBe('Review task cancellation');
      expect(host.textContent).toContain(task.clientId);
      expect(host.textContent).toContain(task.engagementId);
      expect(host.textContent).toContain(`revision`);

      panel.updateReason('   '); panel.prepareReview();
      expect(panel.intent()).toBeNull();
      expect(panel.message()).toContain('non-empty cancellation reason');
      panel.updateReason('x'.repeat(2001)); panel.prepareReview();
      expect(panel.intent()).toBeNull();
      panel.updateReason(reason); panel.prepareReview();
      fixture.detectChanges(); TestBed.tick();
      expect(panel.intent()).toEqual({ action: 'cancel', expectedRevision: task.revision, reason: canonicalReason });
      expect(host.querySelector('.reviewed-intent .reason')?.textContent).toBe(canonicalReason);
      expect(host.textContent).toContain('I reviewed this exact task, engagement scope, revision, and reason.');
      expect(host.textContent).toContain('Confirm task cancellation');

      panel.assent.set(true);
      const sending = panel.submit();
      const postUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`;
      const post = http.expectOne(postUrl);
      const exactPayload = { expectedRevision: task.revision, reason: canonicalReason };
      expect(post.request.body).toEqual(exactPayload);
      post.flush({}, { status: 503, statusText: 'Service Unavailable' });
      await sending;
      fixture.detectChanges(); TestBed.tick();
      expect(panel.state()).toBe('unknown');
      const access = TestBed.inject(WorkspaceAvailability);
      const previous = access.current();
      access.invalidate(); TestBed.tick(); fixture.detectChanges();
      expect(panel.state()).toBe('unknown');
      expect(host.querySelector('div[hidden] audit-workflow-task-cancellation')).toBeTruthy();
      (access.current as WritableSignal<WorkspaceAvailabilityView | null>).set(previous);
      (access.revision as WritableSignal<number>).update(value => value + 1);
      TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [cancelableTask], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); fixture.detectChanges();
      expect(fixture.debugElement.query(By.directive(WorkflowTaskCancellationPanel)).componentInstance).toBe(panel);
      expect(panel.intent()).toEqual({ action: 'cancel', ...exactPayload });
      expect(host.querySelector('div[hidden] audit-workflow-task-cancellation')).toBeTruthy();
      const verifyFirst = component.verifyRetainedActions();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      await verifyFirst; TestBed.tick(); fixture.detectChanges();
      expect(host.querySelector('div[hidden] audit-workflow-task-cancellation')).toBeNull();
      expect(host.textContent).toContain('Check cancellation status');
      expect(host.querySelectorAll('audit-workflow-task-cancellation [role="alert"]')).toHaveLength(1);
      http.expectNone(request => request.method === 'POST');

      component.selectView('MY_TASKS');
      TestBed.tick(); fixture.detectChanges();
      expect(fixture.debugElement.query(By.directive(WorkflowTaskCancellationPanel)).componentInstance).toBe(panel);
      const delayedCheck = panel.checkStatus();
      const delayedRead = http.expectOne(workspaceUrl);
      access.invalidate();
      // The read can complete before the effect runs; it must not authorize a retry.
      delayedRead.flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      await delayedCheck;
      expect(panel.state()).toBe('unknown'); expect(panel.observed()).toBeNull();
      (access.current as WritableSignal<WorkspaceAvailabilityView | null>).set(previous);
      (access.revision as WritableSignal<number>).update(value => value + 1);
      TestBed.tick(); fixture.detectChanges();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [cancelableTask], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      http.expectNone(request => request.method === 'POST');
      const verifyAgain = component.verifyRetainedActions();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      await verifyAgain; TestBed.tick(); fixture.detectChanges();

      const inconsistentCheck = panel.checkStatus();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'CANCELLED' }] });
      await inconsistentCheck;
      expect(panel.state()).toBe('unknown');
      expect(panel.observed()).toBeNull();
      expect(panel.message()).toContain('did not advance the reviewed revision');
      expect(component.cancellationAccessCurrent()).toBe(false);
      const verifyAfterError = component.verifyRetainedActions();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      await verifyAfterError; TestBed.tick(); fixture.detectChanges();

      const unchangedCheck = panel.checkStatus();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision, status: 'IN_PROGRESS' }] });
      await unchangedCheck;
      expect(panel.state()).toBe('retryable');
      expect(panel.assent()).toBe(false);
      expect(panel.intent()).toEqual({ action: 'cancel', ...exactPayload });
      http.expectNone(request => request.method === 'POST');

      panel.assent.set(true);
      const retrying = panel.submit();
      const retry = http.expectOne(postUrl);
      expect(retry.request.body).toEqual(exactPayload);
      retry.flush({ value: true });
      await retrying;
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      expect(panel.state()).toBe('confirmed');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('resets recovery mode and ignores a late cancellation reply after the action input changes', async () => {
    const test = start();
    const { fixture, http, host, component } = test;
    try {
      const panel = openCancellation(test);
      panel.updateReason('The task is no longer required.');
      panel.prepareReview();
      expect(panel.intent()).toEqual({ action: 'cancel', expectedRevision: task.revision, reason: 'The task is no longer required.' });
      panel.assent.set(true);
      const sending = panel.submit();
      const delayedCancellation = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`);

      component.cancellationAction.set('reopen');
      fixture.detectChanges(); TestBed.tick();
      expect(panel.action()).toBe('reopen');
      expect(panel.intent()).toBeNull();
      expect(panel.state()).toBe('editing');
      expect(host.querySelector('section[aria-labelledby="cancellation-heading"] h2')?.textContent?.trim()).toBe('Review task reopening');
      expect([...host.querySelectorAll('audit-workflow-task-cancellation button')].some(button => button.textContent?.trim() === 'Review reopening')).toBe(true);

      delayedCancellation.flush({ value: true });
      await sending;
      fixture.detectChanges(); TestBed.tick();
      expect(panel.state()).toBe('editing');
      expect(panel.intent()).toBeNull();
      expect(host.textContent).not.toContain('Task cancellation response confirmed');
      http.expectNone(request => request.method === 'GET' && request.url === tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'));
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('retries reopening only after the exact cancelled revision is observed and assent is renewed', async () => {
    const test = start();
    const { fixture, http, host } = test;
    const reason = '  Work may resume under the updated engagement scope.  ';
    const exactReason = reason.trim();
    const workspaceUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    const reopenUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reopen`;
    try {
      const panel = openReopening(test);
      expect(panel.action()).toBe('reopen');
      expect(host.querySelector('section[aria-labelledby="cancellation-heading"] h2')?.textContent).toBe('Review task reopening');
      panel.updateReason(reason); panel.prepareReview();
      fixture.detectChanges(); TestBed.tick();
      expect(panel.intent()).toEqual({ action: 'reopen', expectedRevision: task.revision, reason: exactReason });
      expect(host.textContent).toContain('Confirm task reopening');
      panel.assent.set(true);
      const sending = panel.submit();
      const first = http.expectOne(reopenUrl);
      const exactPayload = { expectedRevision: task.revision, reason: exactReason };
      expect(first.request.body).toEqual(exactPayload);
      first.flush({}, { status: 503, statusText: 'Service Unavailable' });
      await sending;
      expect(panel.state()).toBe('unknown');

      const checking = panel.checkStatus();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName,
          revision: task.revision, status: 'CANCELLED' }] });
      await checking;
      fixture.detectChanges(); TestBed.tick();
      expect(panel.state()).toBe('retryable');
      expect(panel.assent()).toBe(false);
      expect(panel.intent()).toEqual({ action: 'reopen', ...exactPayload });
      http.expectNone(request => request.method === 'POST');

      panel.assent.set(true);
      const retrying = panel.submit();
      const retry = http.expectOne(reopenUrl);
      expect(retry.request.body).toEqual(exactPayload);
      retry.flush({ value: true }); await retrying;
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      expect(panel.state()).toBe('confirmed');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('treats an active higher revision as an observation and opens retained reopening activity', async () => {
    const test = start();
    const { fixture, http, host } = test;
    const reason = 'The operational pause has ended.';
    const workspaceUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    try {
      const panel = openReopening(test);
      panel.updateReason(reason); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/reopen`)
        .flush({}, { status: 503, statusText: 'Service Unavailable' });
      await sending;

      const checking = panel.checkStatus();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName,
          revision: task.revision + 1, status: 'IN_PROGRESS' }] });
      await checking;
      fixture.detectChanges(); TestBed.tick();
      expect(panel.state()).toBe('observed');
      expect(host.textContent).toContain('does not confirm who reopened the task or which reason was retained');
      expect(host.textContent).not.toContain('Task reopening response confirmed');

      panel.acknowledgeAndReviewActivity(); fixture.detectChanges(); TestBed.tick();
      expect(host.querySelector('audit-workflow-task-cancellation')).toBeNull();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`)
        .flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId,
          events: [{ ...activity.events[0], kind: 'TASK_REOPENED', taskRevision: task.revision + 1, reason }],
          hasMore: false, nextCursor: null });
      fixture.detectChanges(); TestBed.tick();
      expect(host.textContent).toContain('Task reopened');
      expect(host.textContent).toContain(`Reopening reason: ${reason}`);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('moves focus to retained task activity after cancellation observation is acknowledged', async () => {
    const test = start();
    const { fixture, http, host } = test;
    const reason = 'The engagement scope changed before this task could proceed.';
    const taskWorkspace = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    try {
      const panel = openCancellation(test);
      panel.updateReason(reason); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Service Unavailable' });
      await sending;

      const checking = panel.checkStatus();
      http.expectOne(taskWorkspace).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName,
          revision: task.revision + 1, status: 'CANCELLED' }] });
      await checking;
      fixture.detectChanges(); TestBed.tick();
      expect(panel.state()).toBe('observed');

      const acknowledge = [...host.querySelectorAll<HTMLButtonElement>('audit-workflow-task-cancellation button')]
        .find(button => button.textContent?.includes('Acknowledge cancellation and review activity'))!;
      acknowledge.focus();
      acknowledge.click();
      fixture.detectChanges(); TestBed.tick();
      expect(host.querySelector('audit-workflow-task-cancellation')).toBeNull();
      expect(host.querySelector('#task-activity-heading')?.textContent).toBe('Task activity');
      expect(document.activeElement).toBe(host.querySelector('#task-activity-heading'));

      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({}, { status: 503, statusText: 'Service Unavailable' });
      fixture.detectChanges(); TestBed.tick();
      expect(host.querySelector('#task-activity-heading')?.textContent).toBe('Task activity');
      expect(document.activeElement).toBe(host.querySelector('#task-activity-heading'));
      expect(host.textContent).toContain('Temporarily unavailable. Retry shortly.');

      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`)
        .flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId,
          events: [{ ...activity.events[0], kind: 'TASK_CANCELLED', taskRevision: task.revision + 1, reason }],
          hasMore: false, nextCursor: null });
      fixture.detectChanges(); TestBed.tick();
      expect(host.textContent).toContain(`Cancellation reason: ${reason}`);
      expect(document.activeElement).toBe(host.querySelector('#task-activity-heading'));
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('keeps a changed-revision cancellation outcome stale and does not offer an exact-payload retry', async () => {
    const test = start();
    const { fixture, http, host } = test;
    const reason = 'Work stopped after the engagement scope changed.';
    const workspaceUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    try {
      const panel = openCancellation(test);
      panel.updateReason(reason); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Service Unavailable' });
      await sending;

      const checking = panel.checkStatus();
      http.expectOne(workspaceUrl).flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: task.assigneeUserId, assigneeDisplayName: task.assigneeDisplayName, revision: task.revision + 1, status: 'IN_PROGRESS' }] });
      await checking;
      fixture.detectChanges(); TestBed.tick();
      expect(panel.state()).toBe('stale');
      expect(host.textContent).toContain('revision changed');
      expect(host.textContent).not.toContain('Confirm task cancellation');
      await panel.submit();
      http.expectNone(request => request.method === 'POST');

      panel.acknowledgeChangedTask(); fixture.detectChanges(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [{ ...cancelableTask, revision: task.revision + 1, taskStatus: 'IN_PROGRESS' }], hasMore: false, nextCursor: null, summary: summary() });
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('clears cancellation review on owner change and ignores a late exact-status result', async () => {
    const test = start();
    const { fixture, http, session, host, component } = test;
    const reason = 'Confidential scope change details.';
    try {
      const panel = openCancellation(test);
      panel.updateReason(reason); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Service Unavailable' });
      await sending;
      const checking = panel.checkStatus();
      const delayedStatus = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`);

      session.clear(); TestBed.tick();
      expect(component.cancellationTask()).toBeNull();
      const nextOwner = '99999999-9999-4999-8999-999999999999';
      session.current.set({ userId: nextOwner, firmId: task.clientId, generation: '2', staff: true }); TestBed.tick();
      delayedStatus.flush({ engagementId: task.engagementId, truncated: false,
        tasks: [{ id: task.taskId, assigneeUserId: null, assigneeDisplayName: null, revision: task.revision + 1, status: 'CANCELLED' }] });
      await checking;
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick();
      expect(component.cancellationTask()).toBeNull();
      expect(host.querySelector('audit-workflow-task-cancellation')).toBeNull();
      expect(host.textContent).not.toContain(reason);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('requires acknowledgement of an advanced owner and keeps wrong-engagement recovery unresolved', async () => {
    const { fixture, http, component } = start();
    const exactWorkspaceUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${encodeURIComponent(task.taskId)}`;
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
      http.expectOne(exactWorkspaceUrl)
        .flush({ engagementId: '88888888-8888-4888-8888-888888888888', truncated: false, tasks: [current] });
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`)
        .flush({ ...activity, engagementId: task.engagementId, taskId: task.taskId, events: [], hasMore: false, nextCursor: null });
      await wrongScope;
      expect(component.assignmentObservation()).toBeNull();
      expect(component.assignmentUncertain()).toBe(true);
      const checking = component.recoverAssignment();
      http.expectOne(exactWorkspaceUrl)
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

  it('restores queue controls through history while keeping the exact reminder and resetting population cursors', async () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [
      provideRouter([{ path: 'app/work', component: WorkflowWorkQueue }]), provideLocationMocks(),
      provideHttpClient(), provideHttpClientTesting(), provideWorkspaceAvailabilityForTests(),
    ] });
    const userId = task.assigneeUserId!;
    TestBed.inject(SessionService).current.set({ userId, firmId: userId, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const location = TestBed.inject(Location);
    // The harness navigates imperatively without app bootstrap, which normally installs this listener.
    router.setUpLocationChangeListener();
    const harness = await RouterTestingHarness.create();
    const reminderUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    const queueResponse = (view: string) => ({ view, tasks: [task], hasMore: false, nextCursor: null, summary: summary() });
    try {
      const component = await harness.navigateByUrl(`/app/work?engagementId=${task.engagementId}&taskId=${task.taskId}` +
        '&queueView=TEAM&queueAssignment=ME&queueDisplay=BOARD', WorkflowWorkQueue);
      TestBed.tick();
      http.expectOne(tasksUrl(`view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}`)).flush(queueResponse('TEAM'));
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      http.expectOne(reminderUrl).flush(reminderWorkspace(task.taskId, task.title, task.revision, task.taskStatus));
      TestBed.tick(); harness.fixture.detectChanges();
      expect(component.selectedView()).toBe('TEAM');
      expect(component.ownerFilter()).toBe('ME');
      expect(component.display()).toBe('BOARD');
      expect(component.reminderTaskSelection()).toEqual({ engagementId: task.engagementId, taskId: task.taskId });

      component.selectDisplay('CALENDAR'); await harness.fixture.whenStable(); TestBed.tick();
      await harness.fixture.whenStable();
      expect(router.parseUrl(router.url).queryParams['queueDisplay']).toBe('CALENDAR');
      http.expectNone(request => request.url === '/api/ui/work/tasks');

      component.selectOwnerFilter('UNASSIGNED'); await harness.fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true&unassignedOnly=true')).flush({ ...queueResponse('TEAM'), tasks: [] });
      await harness.fixture.whenStable();
      component.nextPage({ dueDate: null, taskId: task.taskId }); TestBed.tick();
      http.expectOne(tasksUrl(`view=TEAM&pageSize=50&includeSummary=true&unassignedOnly=true&afterTaskId=${task.taskId}`))
        .flush({ ...queueResponse('TEAM'), tasks: [] });
      harness.fixture.detectChanges();

      const dueToday = [...harness.routeNativeElement!.querySelectorAll<HTMLButtonElement>('button.summary-card')]
        .find(button => button.textContent?.includes('Due today'))!;
      dueToday.click(); await harness.fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=DUE_TODAY&pageSize=50&includeSummary=true')).flush(queueResponse('DUE_TODAY'));
      await harness.fixture.whenStable();
      expect(component.ownerFilter()).toBe('ALL');
      expect(harness.routeNativeElement!.textContent).toContain('As of 2026-10-15 (UTC)');
      expect(router.parseUrl(router.url).queryParams).toMatchObject({ engagementId: task.engagementId, taskId: task.taskId });

      location.back();
      expect(router.parseUrl(location.path()).queryParams).toMatchObject({ queueView: 'TEAM', queueAssignment: 'UNASSIGNED',
        queueDisplay: 'CALENDAR', engagementId: task.engagementId, taskId: task.taskId });
      await vi.waitFor(() => expect(component.selectedView()).toBe('TEAM'));
      TestBed.tick();
      expect(component.ownerFilter()).toBe('UNASSIGNED');
      expect(component.display()).toBe('CALENDAR');
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true&unassignedOnly=true')).flush({ ...queueResponse('TEAM'), tasks: [] });
      location.forward();
      expect(router.parseUrl(location.path()).queryParams).toMatchObject({ queueView: 'DUE_TODAY', queueAssignment: 'ALL',
        queueDisplay: 'CALENDAR', engagementId: task.engagementId, taskId: task.taskId });
      await vi.waitFor(() => expect(component.selectedView()).toBe('DUE_TODAY'));
      TestBed.tick();
      expect(component.ownerFilter()).toBe('ALL');
      http.expectOne(tasksUrl('view=DUE_TODAY&pageSize=50&includeSummary=true')).flush(queueResponse('DUE_TODAY'));
      expect(component.reminderTaskSelection()).toEqual({ engagementId: task.engagementId, taskId: task.taskId });
      http.expectNone(reminderUrl);
      http.verify({ ignoreCancelled: true });
    } finally { harness.fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('blocks invalid queue links without broadening and resets only on explicit request', async () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [
      provideRouter([{ path: 'app/work', component: WorkflowWorkQueue }]),
      provideHttpClient(), provideHttpClientTesting(), provideWorkspaceAvailabilityForTests(),
    ] });
    const userId = task.assigneeUserId!;
    TestBed.inject(SessionService).current.set({ userId, firmId: userId, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const harness = await RouterTestingHarness.create();
    const reminderUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    const base = `/app/work?engagementId=${task.engagementId}&taskId=${task.taskId}`;
    try {
      const component = await harness.navigateByUrl(`${base}&queueView=TEAM&queueView=OVERDUE`, WorkflowWorkQueue);
      TestBed.tick();
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      http.expectOne(reminderUrl).flush(reminderWorkspace(task.taskId, task.title, task.revision, task.taskStatus));
      http.expectNone(request => request.url === '/api/ui/work/tasks');
      for (const invalid of ['queueView=BOGUS', 'queueAssignment=ME', 'queueView=TEAM&queueDisplay=BOARD&queueDisplay=LIST']) {
        await harness.navigateByUrl(`${base}&${invalid}`, WorkflowWorkQueue);
        TestBed.tick(); harness.fixture.detectChanges();
        expect(component.queueLocationValid()).toBe(false);
        expect(harness.routeNativeElement!.textContent).toContain('This queue link has invalid filters');
        http.expectNone(request => request.url === '/api/ui/work/tasks');
      }
      expect(component.reminderTaskSelection()).toEqual({ engagementId: task.engagementId, taskId: task.taskId });
      const reset = [...harness.routeNativeElement!.querySelectorAll<HTMLButtonElement>('button')]
        .find(button => button.textContent?.includes('Reset queue filters'))!;
      reset.click();
      await harness.fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      expect(component.queueLocationValid()).toBe(true);
      expect(router.parseUrl(router.url).queryParams).toMatchObject({ engagementId: task.engagementId, taskId: task.taskId });
      http.expectNone(reminderUrl);
      http.verify({ ignoreCancelled: true });
    } finally { harness.fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('cancels an old queue read when navigation changes to invalid filters', async () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [
      provideRouter([{ path: 'app/work', component: WorkflowWorkQueue }]),
      provideHttpClient(), provideHttpClientTesting(), provideWorkspaceAvailabilityForTests(),
    ] });
    const userId = task.assigneeUserId!;
    TestBed.inject(SessionService).current.set({ userId, firmId: userId, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    try {
      const component = await harness.navigateByUrl('/app/work?queueView=TEAM&queueAssignment=ME', WorkflowWorkQueue);
      TestBed.tick();
      const prior = http.expectOne(tasksUrl(`view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}`));
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      await harness.navigateByUrl('/app/work?queueView=TEAM&queueAssignment=ME&queueAssignment=ALL', WorkflowWorkQueue);
      TestBed.tick(); harness.fixture.detectChanges();
      expect(prior.cancelled).toBe(true);
      expect(component.queue.data()).toBeNull();
      expect(harness.routeNativeElement!.querySelector('nav[aria-label="Work queue views"]')).toBeNull();
      component.refreshTaskWorkspaces();
      http.expectNone(request => request.url === '/api/ui/work/tasks');
      http.verify({ ignoreCancelled: true });
    } finally { harness.fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('does not let restored filters bypass current availability or session invalidation', async () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [
      provideRouter([{ path: 'app/work', component: WorkflowWorkQueue }]),
      provideHttpClient(), provideHttpClientTesting(),
    ] });
    const userId = task.assigneeUserId!;
    const session = TestBed.inject(SessionService);
    session.current.set({ userId, firmId: userId, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    try {
      const component = await harness.navigateByUrl('/app/work?queueView=TEAM&queueAssignment=ME&queueDisplay=BOARD', WorkflowWorkQueue);
      TestBed.tick();
      http.expectNone(request => request.url === '/api/ui/work/tasks');
      http.expectOne('/api/ui/workspace-availability').flush({ userId, firmId: userId, generation: '1', staff: true,
        destinations: ['my-work'], tasks: 'available', reviews: 'not_authorized', templates: 'not_authorized', validForMs: 30000 });
      TestBed.tick();
      const pending = http.expectOne(tasksUrl(`view=TEAM&pageSize=50&includeSummary=true&assigneeUserId=${userId}`));
      session.clear(); TestBed.tick(); harness.fixture.detectChanges();
      expect(pending.cancelled).toBe(true);
      expect(component.queue.data()).toBeNull();
      expect(harness.routeNativeElement!.textContent).not.toContain('Team: 0');
      http.expectNone(request => request.url === '/api/ui/work/tasks');
      http.verify({ ignoreCancelled: true });
    } finally { harness.fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('keeps an unknown cancellation through an invalid queue route without resending it', async () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [
      provideRouter([{ path: 'app/work', component: WorkflowWorkQueue }]),
      provideHttpClient(), provideHttpClientTesting(), provideWorkspaceAvailabilityForTests(),
    ] });
    const userId = task.assigneeUserId!;
    TestBed.inject(SessionService).current.set({ userId, firmId: userId, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    try {
      await harness.navigateByUrl('/app/work', WorkflowWorkQueue);
      TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [cancelableTask], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      TestBed.tick(); harness.fixture.detectChanges();
      const host = harness.routeNativeElement!;
      host.querySelector<HTMLButtonElement>(`button[data-cancel-task="${task.taskId}"]`)!.click();
      TestBed.tick(); harness.fixture.detectChanges();
      const panel = harness.routeDebugElement!.query(By.directive(WorkflowTaskCancellationPanel)).componentInstance as WorkflowTaskCancellationPanel;
      panel.updateReason('Scope changed.'); panel.prepareReview(); panel.assent.set(true);
      const sending = panel.submit();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`)
        .flush({}, { status: 503, statusText: 'Service Unavailable' });
      await sending; TestBed.tick();
      expect(panel.state()).toBe('unknown');
      const intent = panel.intent();
      await harness.navigateByUrl('/app/work?queueView=TEAM&queueView=OVERDUE', WorkflowWorkQueue);
      TestBed.tick(); harness.fixture.detectChanges();
      expect(harness.routeDebugElement!.query(By.directive(WorkflowTaskCancellationPanel)).componentInstance).toBe(panel);
      expect(panel.state()).toBe('unknown');
      expect(panel.intent()).toEqual(intent);
      http.expectNone(request => request.method === 'POST');
      http.expectNone(request => request.url === '/api/ui/work/tasks');
      http.verify({ ignoreCancelled: true });
    } finally { harness.fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('opens the exact reminder task read-only until a queue row matches its current revision and status', async () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [
      provideRouter([{ path: 'app/work', component: WorkflowWorkQueue }]),
      provideHttpClient(), provideHttpClientTesting(), provideWorkspaceAvailabilityForTests(),
    ] });
    TestBed.inject(SessionService).current.set({ userId: task.assigneeUserId!, firmId: task.assigneeUserId!, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    const workspaceUrl = `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${task.taskId}`;
    try {
      const component = await harness.navigateByUrl(
        `/app/work?engagementId=${task.engagementId}&taskId=${task.taskId}`, WorkflowWorkQueue);
      const host = harness.routeNativeElement!;
      const selectedPanel = () => host.querySelector<HTMLElement>('section[aria-labelledby="reminder-task-heading"]')!;
      const reminderRuleId = '66666666-6666-4666-8666-666666666666';
      const reminderRevisionId = '77777777-7777-4777-8777-777777777777';
      const reminderOperationId = '88888888-8888-4888-8888-888888888888';
      const sourceTaskId = '99999999-9999-4999-8999-999999999999';
      const sourceRecipientId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      http.expectOne(workspaceUrl).flush(reminderWorkspace(task.taskId, task.title, task.revision, task.taskStatus,
        task.engagementId, { reminderRuleId, reminderRuleRevisionId: reminderRevisionId, reminderOperationId,
          reminderSource: { sourceKind: 'TASK_DUE', sourceId: sourceTaskId, sourceRevision: 2, dueRevision: 3,
            sourceDueDate: '2026-10-10', sourceHref: `/app/work?engagementId=${task.engagementId}&taskId=${sourceTaskId}`,
            recipientUserId: sourceRecipientId, isSourceCurrent: true }, canOpenReminderRule: true }));
      await harness.fixture.whenStable(); harness.fixture.detectChanges();

      expect(component.selectedView()).toBe('MY_TASKS');
      expect(host.textContent).toContain('Task selected from deadline reminder');
      expect(host.textContent).toContain(task.title);
      expect(host.querySelector('#reminder-task-heading')).toBe(document.activeElement);
      expect(selectedPanel().querySelector('[role="status"]')?.textContent).toContain(`${task.title} loaded from the reminder link`);
      expect(host.textContent).toContain('The selected task is read-only');
      const origin = selectedPanel().querySelector<HTMLAnchorElement>('a[href^="/app/practice/reminder-rules"]');
      const taskSource = selectedPanel().querySelector<HTMLAnchorElement>('a[href^="/app/work?"]');
      expect(selectedPanel().textContent).toContain('Reminder origin');
      expect(selectedPanel().textContent).toContain(reminderOperationId);
      expect(selectedPanel().textContent).toContain('Source is current');
      expect(selectedPanel().textContent).toContain('Source revision');
      expect(origin?.getAttribute('href')).toBe(`/app/practice/reminder-rules?ruleId=${reminderRuleId}&revisionId=${reminderRevisionId}`);
      expect(taskSource?.getAttribute('href')).toBe(`/app/work?engagementId=${task.engagementId}&taskId=${sourceTaskId}`);
      expect([...selectedPanel().querySelectorAll('button')].some(button => button.textContent?.trim() === 'Complete task')).toBe(false);

      // Exact task workspaces do not depend on membership in the current queue page.
      const workspaceButton = (label: string) => [...selectedPanel().querySelectorAll<HTMLButtonElement>('button')]
        .find(button => button.textContent?.trim() === label)!;
      workspaceButton('Review prerequisites').click(); TestBed.tick();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/evidence`)
        .flush({ engagementId: task.engagementId, taskId: task.taskId, taskRevision: task.revision, canManage: false, rules: [] });
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      expect(component.evidencePanel()?.authorizedWorkspace()).toBeTruthy();
      expect(component.evidencePanel()?.workspace.data()?.canManage).toBe(false);
      component.evidenceTask.set(null); TestBed.tick();
      workspaceButton('Review task ordering').click(); TestBed.tick();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/dependencies`)
        .flush({ message: 'Scope denied' }, { status: 403, statusText: 'Forbidden' });
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      expect(component.orderingPanel()?.authorizedWorkspace()).toBe(false);
      component.orderingTask.set(null); TestBed.tick();
      http.expectNone(request => request.method === 'POST');

      component.selectView('TEAM'); await harness.fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true'))
        .flush({ view: 'TEAM', tasks: [{ ...task, revision: task.revision + 1, canComplete: true }],
          hasMore: false, nextCursor: null, summary: summary() });
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      expect(selectedPanel().textContent).toContain('no currently loaded queue row matches its current status and revision');
      expect([...selectedPanel().querySelectorAll('button')].some(button => button.textContent?.trim() === 'Complete task')).toBe(false);

      component.selectView('MY_TASKS'); await harness.fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      component.selectView('TEAM'); await harness.fixture.whenStable(); TestBed.tick();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true'))
        .flush({ view: 'TEAM', tasks: [{ ...task, canComplete: true }], hasMore: false, nextCursor: null, summary: summary() });
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      const completeButton = [...selectedPanel().querySelectorAll<HTMLButtonElement>('button')]
        .find(button => button.textContent?.trim() === 'Complete task')!;
      expect(completeButton).toBeTruthy();
      completeButton.click();
      const completion = http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/complete`);
      expect(completion.request.body).toEqual({ expectedRevision: task.revision });
      completion.flush({ value: true });
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      http.expectOne(tasksUrl('view=TEAM&pageSize=50&includeSummary=true'))
        .flush({ view: 'TEAM', tasks: [{ ...task, taskStatus: 'COMPLETED', revision: task.revision + 1,
          canComplete: false, canChangeDueDate: false, canAssign: false }], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne(workspaceUrl).flush(reminderWorkspace(task.taskId, task.title, task.revision + 1, 'COMPLETED', task.engagementId,
        { reminderRuleId, reminderRuleRevisionId: reminderRevisionId, reminderOperationId,
          reminderSource: { sourceKind: 'PBC_DUE', sourceId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', sourceRevision: 4,
            dueRevision: 5, sourceDueDate: '2026-10-11', sourceHref: `/app/engagements/${task.engagementId}/pbc`,
            recipientUserId: sourceRecipientId, isSourceCurrent: null } }));
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      expect(selectedPanel().querySelector('audit-status .status-chip')?.textContent?.trim()).toBe('completed');
      expect(selectedPanel().textContent).toContain(`revision ${task.revision + 1}`);
      expect(selectedPanel().textContent).toContain('Current source status unavailable');
      expect(selectedPanel().querySelector('a[href^="/app/engagements/"]')?.getAttribute('href'))
        .toBe(`/app/engagements/${task.engagementId}/pbc`);
      // CanOpenReminderRule is intentionally omitted to cover older API payloads and the false default.
      expect(selectedPanel().querySelector('a[href^="/app/practice/reminder-rules"]')).toBeNull();
      expect([...selectedPanel().querySelectorAll('button')].some(button => button.textContent?.trim() === 'Complete task')).toBe(false);
      http.verify({ ignoreCancelled: true });
    } finally { harness.fixture.destroy(); TestBed.resetTestingModule(); }
  });

  it('clears the prior reminder task and cancels its exact read when the route query selects another task', async () => {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [
      provideRouter([{ path: 'app/work', component: WorkflowWorkQueue }]),
      provideHttpClient(), provideHttpClientTesting(), provideWorkspaceAvailabilityForTests(),
    ] });
    TestBed.inject(SessionService).current.set({ userId: task.assigneeUserId!, firmId: task.assigneeUserId!, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    const workspaceUrl = (taskId: string) => `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${taskId}`;
    const nextTaskId = '55555555-5555-4555-8555-555555555555';
    try {
      await harness.navigateByUrl(`/app/work?engagementId=${task.engagementId}&taskId=${task.taskId}`, WorkflowWorkQueue);
      http.expectOne(tasksUrl('view=MY_TASKS&pageSize=50&includeSummary=true'))
        .flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: summary() });
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const priorRead = http.expectOne(workspaceUrl(task.taskId));

      const component = await harness.navigateByUrl(
        `/app/work?engagementId=${task.engagementId}&taskId=not-a-guid`, WorkflowWorkQueue);
      expect(priorRead.cancelled).toBe(true);
      const host = harness.routeNativeElement!;
      expect(host.textContent).not.toContain(task.title);
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('This task link is invalid');
      http.expectNone(request => request.url.includes('/workflow/tasks?taskId=not-a-guid'));

      await harness.navigateByUrl(`/app/work?engagementId=${task.engagementId}&taskId=${nextTaskId}`, WorkflowWorkQueue);
      http.expectOne(workspaceUrl(nextTaskId)).flush(reminderWorkspace(task.taskId, 'Wrong task returned', 1, 'IN_PROGRESS'));
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      expect(component.reminderTaskSelection()).toEqual({ engagementId: task.engagementId, taskId: nextTaskId });
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('Unsupported response.');
      expect(host.textContent).not.toContain('Review the new task');
      expect(host.textContent).not.toContain(task.title);
      const selectedPanel = host.querySelector<HTMLElement>('section[aria-labelledby="reminder-task-heading"]')!;
      expect([...selectedPanel.querySelectorAll('button')].some(button => button.textContent?.trim() === 'Complete task')).toBe(false);

      const validTaskWithBadSourceId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
      await harness.navigateByUrl(`/app/work?engagementId=${task.engagementId}&taskId=${validTaskWithBadSourceId}`, WorkflowWorkQueue);
      http.expectOne(workspaceUrl(validTaskWithBadSourceId)).flush(reminderWorkspace(validTaskWithBadSourceId, 'Selected task', 1, 'OPEN', task.engagementId,
        { reminderSource: { sourceKind: 'PBC_DUE', sourceId: nextTaskId, sourceRevision: 1, dueRevision: 1,
          sourceDueDate: '2026-10-10', sourceHref: '/app/engagements/cccccccc-cccc-4ccc-8ccc-cccccccccccc/pbc',
          recipientUserId: task.assigneeUserId!, isSourceCurrent: null } }));
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('Unsupported response.');
      expect(host.querySelector('a[href*="/app/engagements/"]')).toBeNull();

      const legacyTaskId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
      await harness.navigateByUrl(`/app/work?engagementId=${task.engagementId}&taskId=${legacyTaskId}`, WorkflowWorkQueue);
      http.expectOne(workspaceUrl(legacyTaskId)).flush(reminderWorkspace(legacyTaskId, 'Legacy selected task', 1, 'OPEN'));
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      expect(host.textContent).toContain('Legacy selected task');
      expect(host.textContent).not.toContain('Unsupported response.');
      expect(host.textContent).not.toContain('Reminder origin');
      component.openLinkedTaskPanel('activity'); TestBed.tick();
      const activityRead = http.expectOne(request => request.url.startsWith(
        `/api/ui/engagements/${task.engagementId}/workflow/tasks/${legacyTaskId}/activity?`));
      await harness.navigateByUrl(`/app/work?engagementId=${task.engagementId}&taskId=${nextTaskId}`, WorkflowWorkQueue);
      expect(activityRead.cancelled).toBe(true);
      expect(component.activityTask()).toBeNull();
      component.openLinkedTaskPanel('evidence'); TestBed.tick();
      http.expectNone(request => request.url.endsWith('/evidence'));
      http.expectOne(workspaceUrl(nextTaskId)).flush({ message: 'Scope denied' }, { status: 403, statusText: 'Forbidden' });
      await harness.fixture.whenStable(); harness.fixture.detectChanges();
      component.openLinkedTaskPanel('evidence'); TestBed.tick();
      http.expectNone(request => request.url.endsWith('/evidence'));

      // A route change must not replace an unresolved exact-task workspace.
      await harness.navigateByUrl(`/app/work?engagementId=${task.engagementId}&taskId=${legacyTaskId}`, WorkflowWorkQueue);
      http.expectOne(workspaceUrl(legacyTaskId)).flush(reminderWorkspace(legacyTaskId, 'Legacy selected task', 1, 'OPEN'));
      await harness.fixture.whenStable();
      const receiptKey = '77777777-7777-4777-8777-777777777777';
      const storageKey = `auditsphere.workflow-evidence:${task.assigneeUserId}:${task.assigneeUserId}:${task.engagementId}:${legacyTaskId}`;
      sessionStorage.setItem(storageKey, receiptKey);
      component.openLinkedTaskPanel('evidence'); TestBed.tick();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${legacyTaskId}/evidence`)
        .flush({ engagementId: task.engagementId, taskId: legacyTaskId, taskRevision: 1, canManage: false, rules: [] });
      await harness.fixture.whenStable();
      const pendingPanel = component.evidencePanel()!;
      expect(pendingPanel.pending()).toBe(receiptKey);
      await harness.navigateByUrl(`/app/work?engagementId=${task.engagementId}&taskId=${nextTaskId}`, WorkflowWorkQueue);
      http.expectOne(workspaceUrl(nextTaskId)).flush(reminderWorkspace(nextTaskId, 'Next selected task', 1, 'OPEN'));
      await harness.fixture.whenStable();
      component.openLinkedTaskPanel('evidence'); TestBed.tick();
      expect(component.evidencePanel()).toBe(pendingPanel);
      expect(component.evidenceTask()?.taskId).toBe(legacyTaskId);
      expect(pendingPanel.pending()).toBe(receiptKey);
      expect(sessionStorage.getItem(storageKey)).toBe(receiptKey);
      expect(component.actionMessage()).toContain('Resolve or explicitly close');
      http.expectNone(request => request.url.endsWith('/evidence'));
      // Selecting the original task remains available for receipt recovery.
      component.selectEvidence({ engagementId: task.engagementId, taskId: legacyTaskId, title: 'Legacy selected task' });
      TestBed.tick();
      http.expectOne(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${legacyTaskId}/evidence`)
        .flush({ engagementId: task.engagementId, taskId: legacyTaskId, taskRevision: 1, canManage: false, rules: [] });
      TestBed.tick();
      expect(component.evidencePanel()).toBe(pendingPanel);
      expect(pendingPanel.pending()).toBe(receiptKey);
      http.expectNone(request => request.method === 'POST');
      http.verify({ ignoreCancelled: true });
    } finally { harness.fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
