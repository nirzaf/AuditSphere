import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { decodeTime, PracticeTime } from './time';

const userId = '11111111-1111-4111-8111-111111111111';
const taskId = '22222222-2222-4222-8222-222222222222';
const entryId = '33333333-3333-4333-8333-333333333333';
const workspace = {
  firmWide: true, isApprover: false, periods: [], engagements: [],
  openTasks: [{ id: taskId, title: 'Prepare lead schedule', status: 'OPEN', mappingVersionId: null, fsliCode: null }],
  myEntries: [], awaitingApproval: [],
};

function open() {
  TestBed.configureTestingModule({
    imports: [PracticeTime],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const session = TestBed.inject(SessionService);
  session.current.set({ userId, firmId: userId, generation: '1', staff: true });
  const fixture = TestBed.createComponent(PracticeTime);
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges(); TestBed.tick();
  http.expectOne('/api/ui/practice/time').flush(workspace);
  TestBed.tick();
  return { fixture, component: fixture.componentInstance, http, session };
}

describe('foreground practice time tracker integration', () => {
  it('defaults an absent legacy start minute to 09:00 and sends midnight explicitly as zero', async () => {
    const oldEntry = {
      id: entryId, workDate: '2026-10-15', taskId, taskTitle: 'Prepare lead schedule', userId,
      durationMinutes: 30, activity: 'Fieldwork', billableClassification: 'BILLABLE', narrative: '', status: 'DRAFT',
      mappingVersionId: null, fsliCode: null,
    };
    expect(decode(decodeTime, { ...workspace, myEntries: [oldEntry] }).myEntries[0].startMinute).toBe(540);
    const { fixture, component, http } = open();
    try {
      component.draft = { ...component.draft, task: taskId, date: '2026-10-15', startTime: '00:00', minutes: 30 };
      const saving = component.saveDraft();
      const post = http.expectOne('/api/ui/practice/time/entries');
      expect(post.request.body.startMinute).toBe(0);
      post.flush({ value: entryId }); await saving;
      http.expectOne('/api/ui/practice/time').flush(workspace);

      component.draft.minutes = 1.5;
      await component.saveDraft();
      http.expectNone(request => request.method === 'POST');
      expect(component.cmd.message()).toContain('whole-minute duration');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); }
  });

  it('requires explicit task selection, pauses active time, and saves reviewed segments one at a time', async () => {
    const now = new Date('2026-10-15T09:00:00Z').getTime();
    const clock = vi.spyOn(Date, 'now').mockReturnValue(now);
    const { fixture, component, http } = open();
    try {
      const data = component.view.data()!;
      expect(component.canStartTimer(data)).toBe(false);
      component.startTimer(data);
      expect(component.tracker().phase).toBe('IDLE');

      component.trackerTask.set(taskId);
      component.startTimer(data);
      expect(component.tracker().phase).toBe('RUNNING');
      clock.mockReturnValue(now + 10 * 60_000);
      component.pauseTimer();
      clock.mockReturnValue(now + 20 * 60_000);
      component.resumeTimer();
      clock.mockReturnValue(now + 25 * 60_000);
      component.stopTimer();
      expect(component.timerReview().rows.map(x => [x.workDate, x.startMinute, x.durationMinutes]))
        .toEqual([['2026-10-15', 540, 10], ['2026-10-15', 560, 5]]);
      expect(component.timerReview().excludedMilliseconds).toBe(0);
      http.expectNone(request => request.method === 'POST');

      component.useSegment(component.timerReview().rows[0]);
      expect(component.draft.startTime).toBe('09:00');
      expect(component.draft.minutes).toBe(10);
      const saving = component.saveDraft();
      const post = http.expectOne('/api/ui/practice/time/entries');
      expect(post.request.body).toMatchObject({ taskId, workDate: '2026-10-15', startMinute: 540, durationMinutes: 10 });
      post.flush({ value: entryId });
      await saving;
      expect(component.savedSegments().has(1)).toBe(true);
      expect(component.savedSegments().has(2)).toBe(false);
      http.expectOne('/api/ui/practice/time').flush(workspace);
      fixture.detectChanges();
    } finally {
      fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); clock.mockRestore();
    }
  });

  it('keeps a definite overlap refusal reviewable and freezes additional saves after an unknown result', async () => {
    const { fixture, component, http } = open();
    try {
      const now = new Date('2026-10-15T09:00:00Z').getTime();
      vi.spyOn(Date, 'now').mockReturnValue(now);
      component.trackerTask.set(taskId);
      component.startTimer(component.view.data()!);
      vi.spyOn(Date, 'now').mockReturnValue(now + 10 * 60_000);
      component.stopTimer();
      component.useSegment(component.timerReview().rows[0]);
      const first = component.saveDraft();
      http.expectOne('/api/ui/practice/time/entries').flush(
        { code: 'time.overlap', message: 'The time entry overlaps another active entry.' },
        { status: 400, statusText: 'Bad Request' },
      );
      await first;
      expect(component.savedSegments().has(1)).toBe(false);
      expect(component.draft.minutes).toBe(10);
      http.expectOne('/api/ui/practice/time').flush(workspace);

      const second = component.saveDraft();
      http.expectOne('/api/ui/practice/time/entries').flush(
        { message: 'temporary failure' }, { status: 503, statusText: 'Service Unavailable' },
      );
      await second;
      expect(component.cmd.uncertain()).toBe(true);
      expect(component.unconfirmedSegment()).toBe(1);
      http.expectOne('/api/ui/practice/time').flush(workspace);
      component.refreshRecordedTime();
      http.expectOne('/api/ui/practice/time').flush(workspace);
      const blockedRetry = component.saveDraft();
      await blockedRetry;
      http.expectNone(request => request.method === 'POST');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); vi.restoreAllMocks(); }
  });

  it('pauses on page hide, clears timer state on session invalidation, and requires an explicit discard before navigation', async () => {
    const { fixture, component, http, session } = open();
    const now = new Date('2026-10-15T09:00:00Z').getTime();
    const clock = vi.spyOn(Date, 'now').mockReturnValue(now);
    try {
      const dialog = TestBed.inject(MatDialog);
      vi.spyOn(dialog, 'open').mockReturnValueOnce({ afterClosed: () => of('keep') } as never)
        .mockReturnValueOnce({ afterClosed: () => of('discard') } as never);
      component.trackerTask.set(taskId);
      component.startTimer(component.view.data()!);
      clock.mockReturnValue(now + 60_000);
      const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
      document.dispatchEvent(new Event('visibilitychange'));
      visibility.mockRestore();
      expect(component.tracker().phase).toBe('PAUSED');
      clock.mockReturnValue(now + 120_000);
      component.resumeTimer();
      clock.mockReturnValue(now + 180_000);
      window.dispatchEvent(new Event('pagehide'));
      expect(component.tracker().phase).toBe('PAUSED');
      expect(await component.confirmNavigation()).toBe(false);
      expect(await component.confirmNavigation()).toBe(true);
      expect(component.tracker().phase).toBe('IDLE');
      component.trackerTask.set(taskId);
      component.startTimer(component.view.data()!);
      session.clear(); TestBed.tick();
      expect(component.tracker().phase).toBe('IDLE');
      expect(component.trackerTask()).toBe('');
      expect(await component.confirmNavigation()).toBe(true);
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); vi.restoreAllMocks(); }
  });

  it('freezes an unmeasurable backwards-clock interval without recording its open part', () => {
    const { fixture, component, http } = open();
    const now = new Date('2026-10-15T09:00:00Z').getTime();
    const clock = vi.spyOn(Date, 'now').mockReturnValue(now);
    try {
      component.trackerTask.set(taskId);
      component.startTimer(component.view.data()!);
      clock.mockReturnValue(now - 1);
      component.pauseTimer();
      expect(component.tracker().phase).toBe('STOPPED');
      expect(component.tracker().intervals).toEqual([]);
      expect(component.timerNotice()).toBe('Timer clock or interval limit changed. The open interval was not recorded; review completed intervals or enter time manually.');
    } finally { fixture.destroy(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); clock.mockRestore(); }
  });
});
