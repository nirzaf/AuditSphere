import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import {
  EngagementActivity,
  activitySourceHrefAllowed,
  decodeEngagementActivity,
} from './engagement-activity';

const engagementA = '11111111-1111-4111-8111-111111111111';
const engagementB = '22222222-2222-4222-8222-222222222222';
const eventId = '33333333-3333-4333-8333-333333333333';
function utcDay(daysAgo: number): string {
  const value = new Date();
  value.setUTCHours(0, 0, 0, 0);
  value.setUTCDate(value.getUTCDate() - daysAgo);
  return value.toISOString().slice(0, 10);
}

function eventAtUtc(): string {
  return new Date(Date.now() - 60_000).toISOString();
}

function item(overrides: Record<string, unknown> = {}) {
  return {
    id: eventId,
    source: 2,
    kind: 'UPLOAD_INTENT_RECORDED',
    label: 'Document upload recorded',
    actorUserId: null,
    atUtc: eventAtUtc(),
    currentStatus: 'STARTED',
    sourceHref: `/app/engagements/${engagementA}/pbc`,
    ...overrides,
  };
}

function page(overrides: Record<string, unknown> = {}) {
  return {
    engagementId: engagementA,
    from: utcDay(89),
    to: utcDay(0),
    snapshotAtUtc: new Date().toISOString(),
    items: [item()],
    hasMore: false,
    nextCursor: null,
    ...overrides,
  };
}

@Component({
  standalone: true,
  imports: [EngagementActivity],
  template: `
    <details #panel (toggle)="open.set(panel.open)">
      <summary>Activity</summary>
      @if (open()) {
        <audit-engagement-activity [engagementId]="engagementId()" />
      }
    </details>
  `,
})
class ActivityTestHost {
  readonly engagementId = signal(engagementA);
  readonly open = signal(false);
}

describe('Engagement activity panel', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ActivityTestHost],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({
      userId: engagementA, firmId: engagementA, generation: '1', staff: true,
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); }
  });

  function mountOpen() {
    const fixture = TestBed.createComponent(ActivityTestHost);
    fixture.detectChanges();
    TestBed.tick();
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
    TestBed.tick();
    return { fixture, details };
  }

  it('waits for native Activity disclosure, sends one bounded GET, and renders labels/status as text', () => {
    const fixture = TestBed.createComponent(ActivityTestHost);
    fixture.detectChanges();
    TestBed.tick();
    http.expectNone((request) => request.url.includes('/activity'));
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    expect(details.querySelector('summary')?.textContent).toContain('Activity');
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
    TestBed.tick();

    const request = http.expectOne((value) => value.url.includes(`/api/ui/engagements/${engagementA}/activity`));
    expect(request.request.method).toBe('GET');
    const query = new URL(request.request.urlWithParams, 'http://localhost').searchParams;
    expect(query.get('pageSize')).toBe('50');
    expect(query.get('from')).toBe(utcDay(89));
    expect(query.get('to')).toBe(utcDay(0));
    expect(query.has('afterAtUtc')).toBe(false);
    const injectedLabel = '<img onerror=alert(1)> recorded upload';
    const sources = [
      item({ id: '40000000-0000-4000-8000-000000000001', source: 1, kind: 'EMAIL',
        label: 'PBC email recorded', currentStatus: 'PROVIDER_ACCEPTED' }),
      item({ label: injectedLabel }),
      item({ id: '40000000-0000-4000-8000-000000000003', source: 3, kind: 'TASK_CREATED',
        label: 'Operational task activity', currentStatus: null, sourceHref: '/app/work' }),
      item({ id: '40000000-0000-4000-8000-000000000004', source: 4, kind: 'REVIEW_NOTE_CREATED',
        label: 'Review note created', currentStatus: null,
        sourceHref: `/app/engagements/${engagementA}/audit-fieldwork` }),
      item({ id: '40000000-0000-4000-8000-000000000005', source: 5, kind: 'RESPONSE',
        label: 'Review note response', currentStatus: null,
        sourceHref: `/app/engagements/${engagementA}/audit-fieldwork` }),
      item({ id: '40000000-0000-4000-8000-000000000006', source: 6, kind: 'CORRESPONDENCE_RECORDED',
        label: 'Correspondence routing recorded', currentStatus: 'RECORDED',
        sourceHref: `/app/engagements/${engagementA}/completion` }),
      item({ id: '40000000-0000-4000-8000-000000000007', source: 7, kind: 'PROPOSAL',
        label: 'Commercial notification recorded', currentStatus: 'QUEUED',
        sourceHref: `/app/engagements/${engagementA}/completion` }),
    ];
    request.flush(page({ items: sources }));
    TestBed.tick();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('img')).toBeNull();
    expect(root.textContent).toContain(injectedLabel);
    expect(root.textContent).toContain('Current stored status: STARTED');
    expect(root.textContent).toContain('PBC upload · UPLOAD_INTENT_RECORDED');
    for (const label of [
      'PBC email recorded', 'Operational task activity', 'Review note created', 'Review note response',
      'Correspondence routing recorded', 'Commercial notification recorded',
    ]) expect(root.textContent).toContain(label);
    for (const status of ['PROVIDER_ACCEPTED', 'STARTED', 'RECORDED', 'QUEUED'])
      expect(root.textContent).toContain(`Current stored status: ${status}`);
    expect(Array.from(root.querySelectorAll('a')).map((link) => link.getAttribute('href'))).toEqual([
      `/app/engagements/${engagementA}/pbc`, `/app/engagements/${engagementA}/pbc`, '/app/work',
      `/app/engagements/${engagementA}/audit-fieldwork`, `/app/engagements/${engagementA}/audit-fieldwork`,
      `/app/engagements/${engagementA}/completion`, `/app/engagements/${engagementA}/completion`,
    ]);
    expect(root.textContent).toContain('Open source workspace');
    http.expectNone((value) => value.method !== 'GET');
    fixture.destroy();
  });

  it('cancels the open read when the disclosure closes and when engagement or session changes', () => {
    const { fixture, details } = mountOpen();
    const pendingA = http.expectOne((value) => value.url.includes(`/engagements/${engagementA}/activity`));
    details.open = false;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
    TestBed.tick();
    expect(pendingA.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain('Document upload recorded');

    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
    TestBed.tick();
    const pendingAgain = http.expectOne((value) => value.url.includes(`/engagements/${engagementA}/activity`));
    fixture.componentInstance.engagementId.set(engagementB);
    fixture.detectChanges();
    TestBed.tick();
    expect(pendingAgain.cancelled).toBe(true);
    const requestB = http.expectOne((value) => value.url.includes(`/engagements/${engagementB}/activity`));
    requestB.flush(page({
      engagementId: engagementB,
      items: [item({ id: '44444444-4444-4444-8444-444444444444',
        sourceHref: `/app/engagements/${engagementB}/pbc`, label: 'Engagement B event' })],
    }));
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Engagement B event');
    expect(fixture.nativeElement.textContent).not.toContain('Document upload recorded');

    TestBed.inject(SessionService).clear();
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Engagement B event');
    http.expectNone((value) => value.method !== 'GET');
    fixture.destroy();
  });

  it('round-trips every cursor component with the snapshot and resets paging when dates change', () => {
    const { fixture } = mountOpen();
    const first = http.expectOne((value) => value.url.includes(`/engagements/${engagementA}/activity`));
    const firstItem = item();
    const cursor = { atUtc: firstItem.atUtc, source: 2, id: eventId };
    const firstPage = page({ items: [firstItem], hasMore: true, nextCursor: cursor });
    first.flush(firstPage);
    TestBed.tick();
    fixture.detectChanges();

    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button') as NodeListOf<HTMLButtonElement>);
    const nextButton = buttons.find((button) => button.textContent?.trim() === 'Next') as HTMLButtonElement;
    expect(nextButton.disabled).toBe(false);
    nextButton.click();
    TestBed.tick();
    const next = http.expectOne((value) => value.url.includes(`/engagements/${engagementA}/activity`));
    const query = new URL(next.request.urlWithParams, 'http://localhost').searchParams;
    expect(query.get('pageSize')).toBe('50');
    expect(query.get('snapshotAtUtc')).toBe(firstPage.snapshotAtUtc);
    expect(query.get('afterAtUtc')).toBe(cursor.atUtc);
    expect(query.get('afterSource')).toBe('2');
    expect(query.get('afterId')).toBe(eventId);
    next.flush(page({ items: [], hasMore: false, nextCursor: null }));
    TestBed.tick();
    fixture.detectChanges();

    const activity = fixture.debugElement.query(By.directive(EngagementActivity)).componentInstance as EngagementActivity;
    const changedFrom = utcDay(88);
    activity.changeFrom(changedFrom);
    TestBed.tick();
    const changed = http.expectOne((value) => value.url.includes(`/engagements/${engagementA}/activity`));
    const changedQuery = new URL(changed.request.urlWithParams, 'http://localhost').searchParams;
    expect(changedQuery.get('from')).toBe(changedFrom);
    expect(changedQuery.has('snapshotAtUtc')).toBe(false);
    expect(changedQuery.has('afterAtUtc')).toBe(false);
    expect(changedQuery.has('afterSource')).toBe(false);
    expect(changedQuery.has('afterId')).toBe(false);
    changed.flush(page({ from: changedFrom, items: [] }));
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No recorded activity in this date range.');
    fixture.destroy();
  });

  it('shows separate safe states for forbidden, malformed and empty reads and blocks invalid dates', () => {
    const { fixture } = mountOpen();
    const activity = fixture.debugElement.query(By.directive(EngagementActivity)).componentInstance as EngagementActivity;
    http.expectOne((value) => value.url.includes(`/engagements/${engagementA}/activity`))
      .flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Activity is unavailable for your current role or engagement.');

    activity.refresh();
    TestBed.tick();
    http.expectOne((value) => value.url.includes(`/engagements/${engagementA}/activity`))
      .flush(page({ items: [item({ sourceHref: 'https://outside.example.test/' })] }));
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unsupported response.');
    expect(fixture.nativeElement.textContent).not.toContain('Document upload recorded');

    activity.refresh();
    TestBed.tick();
    http.expectOne((value) => value.url.includes(`/engagements/${engagementA}/activity`))
      .flush(page({ items: [] }));
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No recorded activity in this date range.');

    activity.changeFrom('2026-02-30');
    TestBed.tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Choose a UTC date range no longer than 90 days.');
    http.expectNone((value) => value.url.includes(`/engagements/${engagementA}/activity`));
    fixture.destroy();
  });

  it('accepts only the four current-engagement workspaces and rejects malformed page identity or source ranks', () => {
    expect(activitySourceHrefAllowed(engagementA, `/app/engagements/${engagementA}/audit-fieldwork`)).toBe(true);
    expect(activitySourceHrefAllowed(engagementA, '/app/work')).toBe(true);
    expect(activitySourceHrefAllowed(engagementA, `/app/engagements/${engagementB}/pbc`)).toBe(false);
    expect(activitySourceHrefAllowed(engagementA, '/app/../admin')).toBe(false);
    expect(() => decodeEngagementActivity(page({ engagementId: engagementB }), engagementA)).toThrow();
    expect(() => decodeEngagementActivity(page({ items: [item({ source: 8 })] }), engagementA)).toThrow();
  });
});
