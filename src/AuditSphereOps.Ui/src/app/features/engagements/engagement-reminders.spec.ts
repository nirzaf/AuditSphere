import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { EngagementReminders, decodeEngagementDeadlineReminders } from './engagement-reminders';

const engagementId = '11111111-1111-4111-8111-111111111111';
const otherEngagementId = '22222222-2222-4222-8222-222222222222';
const userId = '33333333-3333-4333-8333-333333333333';
const firmId = '44444444-4444-4444-8444-444444444444';
const route = (id = engagementId) => `/api/ui/engagements/${id}/deadline-reminders`;

const row = (overrides: Record<string, unknown> = {}) => ({
  sourceId: '55555555-5555-4555-8555-555555555555',
  kind: 'TASK_DUE',
  title: 'Prepare planning memo',
  dueDate: '2026-10-16',
  tone: 'UPCOMING',
  message: 'Task is due within 7 days.',
  sourceHref: '/app/work',
  ...overrides,
});

const response = (id = engagementId, items: unknown[] = [], truncated = false) => ({
  engagementId: id,
  asOfDateUtc: '2026-10-15',
  items,
  truncated,
});

describe('Engagement deadline reminders', () => {
  let http: HttpTestingController;
  let visibilityDescriptor: PropertyDescriptor | undefined;

  beforeEach(() => {
    visibilityDescriptor = Object.getOwnPropertyDescriptor(document, 'visibilityState');
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    TestBed.configureTestingModule({
      imports: [EngagementReminders],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId, firmId, generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally {
      TestBed.resetTestingModule();
      if (visibilityDescriptor) Object.defineProperty(document, 'visibilityState', visibilityDescriptor);
      else Reflect.deleteProperty(document, 'visibilityState');
      vi.restoreAllMocks();
    }
  });

  function open(id = engagementId) {
    const fixture = TestBed.createComponent(EngagementReminders);
    fixture.componentRef.setInput('engagementId', id);
    fixture.detectChanges();
    TestBed.tick();
    return { fixture, component: fixture.componentInstance };
  }

  it('loads once on mount and supports an explicit read-only refresh', () => {
    const { fixture, component } = open();
    http.expectOne(route()).flush(response(engagementId, [row()]));
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Deadline reminders');
    expect(fixture.nativeElement.textContent).toContain('As of 2026-10-15 (UTC). Reminders do not clear workflow gates.');
    expect(fixture.nativeElement.textContent).toContain('Prepare planning memo');
    expect(fixture.nativeElement.textContent).toContain('2026-10-16');

    component.refresh();
    http.expectOne(route()).flush(response());
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No reminders in the current view.');
    http.expectNone(request => request.method !== 'GET');
    fixture.destroy();
  });

  it('polls every 60 seconds only while visible and refreshes once on visibility return', () => {
    const intervalSpy = vi.spyOn(globalThis, 'setInterval');
    const clearSpy = vi.spyOn(globalThis, 'clearInterval');
    const addSpy = vi.spyOn(document, 'addEventListener');
    const removeSpy = vi.spyOn(document, 'removeEventListener');
    const { fixture } = open();
    http.expectOne(route()).flush(response());
    TestBed.tick();

    const intervalCall = intervalSpy.mock.calls.find(call => call[1] === 60_000);
    expect(intervalCall).toBeDefined();
    const intervalDelay = intervalCall![1];
    const timer = intervalSpy.mock.results[intervalSpy.mock.calls.indexOf(intervalCall!)].value;
    const visibilityHandler = addSpy.mock.calls.find(call => call[0] === 'visibilitychange')![1];

    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' });
    (intervalCall![0] as () => void)();
    http.expectNone(route());
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    document.dispatchEvent(new Event('visibilitychange'));
    (intervalCall![0] as () => void)();
    http.expectOne(route()).flush(response());
    TestBed.tick();
    http.expectNone(route());

    TestBed.inject(SessionService).current.set(null);
    TestBed.tick();
    (intervalCall![0] as () => void)();
    http.expectNone(route());

    fixture.destroy();
    expect(intervalDelay).toBe(60_000);
    expect(clearSpy).toHaveBeenCalledWith(timer);
    expect(removeSpy).toHaveBeenCalledWith('visibilitychange', visibilityHandler);
    http.expectNone(route());
    intervalSpy.mockRestore();
    clearSpy.mockRestore();
    addSpy.mockRestore();
    removeSpy.mockRestore();
  });

  it('renders empty, truncated and failed states with distinct exact messages', () => {
    const { fixture, component } = open();
    http.expectOne(route()).flush(response());
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No reminders in the current view.');

    component.refresh();
    http.expectOne(route()).flush(response(engagementId, Array.from({ length: 50 }, (_, index) =>
      row({ sourceId: `55555555-5555-4555-8555-${String(index + 1).padStart(12, '0')}` })), true));
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('More reminders exist. Open My work or the planning workspace.');

    component.refresh();
    http.expectOne(route()).flush({ message: 'synthetic failure' }, { status: 500, statusText: 'Failure' });
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Reminders unavailable. Existing workflow gates are unchanged.');
    fixture.destroy();
  });

  it('clears and cancels stale reads when engagement or session changes', () => {
    const { fixture, component } = open();
    const first = http.expectOne(route());
    fixture.componentRef.setInput('engagementId', otherEngagementId);
    TestBed.tick();
    expect(first.cancelled).toBe(true);
    expect(component.reminders.data()).toBeNull();
    const second = http.expectOne(route(otherEngagementId));
    TestBed.inject(SessionService).current.set({
      userId: '66666666-6666-4666-8666-666666666666', firmId, generation: '2', staff: true,
    });
    TestBed.tick();
    expect(second.cancelled).toBe(true);
    expect(component.reminders.data()).toBeNull();
    const current = http.expectOne(route(otherEngagementId));
    current.flush(response(otherEngagementId, [row()]));
    TestBed.tick();
    expect(component.reminders.data()?.engagementId).toBe(otherEngagementId);
    http.expectNone(request => request.method !== 'GET');
    fixture.destroy();
  });

  it('rejects mismatched scopes, invalid dates, unknown values and unsafe source links', () => {
    expect(() => decodeEngagementDeadlineReminders(response(otherEngagementId), engagementId)).toThrow();
    expect(() => decodeEngagementDeadlineReminders(response(engagementId, [row({ dueDate: '2026-02-31' })]), engagementId)).toThrow();
    expect(() => decodeEngagementDeadlineReminders(response(engagementId, [row({ kind: 'UNKNOWN' })]), engagementId)).toThrow();
    expect(() => decodeEngagementDeadlineReminders(response(engagementId, [row({ sourceHref: 'https://example.test' })]), engagementId)).toThrow();
    expect(() => decodeEngagementDeadlineReminders(response(engagementId, Array.from({ length: 51 }, () => row())), engagementId)).toThrow();
  });
});
