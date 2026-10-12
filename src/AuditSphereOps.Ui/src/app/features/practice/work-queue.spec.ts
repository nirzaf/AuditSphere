import { describe, expect, it } from 'vitest';
import { decodeActivity } from './work-queue';

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
      flushQueue('/api/ui/work/tasks?view=MY_TASKS&pageSize=50', 'MY_TASKS');
      http.expectOne('/api/ui/work/reviews?pageSize=50').flush({ reviews: [], hasMore: false, nextCursor: null });
      const component = fixture.componentInstance;
      component.selectView('TEAM'); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=TEAM&pageSize=50', 'TEAM');
      component.selectOwnerFilter('ME'); TestBed.tick();
      flushQueue(`/api/ui/work/tasks?view=TEAM&pageSize=50&assigneeUserId=${userId}`, 'TEAM');
      component.nextPage({ dueDate: null, taskId: userId }); TestBed.tick();
      flushQueue(`/api/ui/work/tasks?view=TEAM&pageSize=50&assigneeUserId=${userId}&afterTaskId=${userId}`, 'TEAM');
      component.selectOwnerFilter('UNASSIGNED'); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=TEAM&pageSize=50&unassignedOnly=true', 'TEAM');
      component.selectView('MY_TASKS'); TestBed.tick();
      flushQueue('/api/ui/work/tasks?view=MY_TASKS&pageSize=50', 'MY_TASKS');
      expect(component.ownerFilter()).toBe('ALL');
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
