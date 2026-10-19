import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, type ParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { Drafts } from '../../core/drafts';
import { decodeOpportunities, Opportunities } from './opportunities';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const opportunity = {
  id,
  serviceRoute: 'Audit',
  entityScope: 'Entity',
  stage: 'DISCOVERY',
  expectedFee: '9007199254740993.01',
  currency: 'QAR',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  createdAt: '2026-10-18T09:00:00.1234567+03:00',
  proposalId: null,
  revision: '0',
};
const history = {
  id,
  action: 'CLIENT_CONVERSION_LINKED',
  occurredAt: '2026-10-18T10:00:00Z',
  actorName: 'Reviewer',
  actorUserId: id,
  isBackfill: false,
  fromState: 'ACCEPTED',
  toState: 'PROSPECT',
  sourceChannel: null,
  ownerName: null,
  ownerUserId: null,
  serviceRoute: 'Audit',
  opportunityId: id,
  proposalId: id,
  proposalRevision: '1',
  clientId: id,
};
const lead = {
  id,
  name: 'Lead',
  status: 'QUALIFIED',
  source: 'Referral',
  ownerUserId: null,
  ownerName: 'Not assigned',
  createdAt: '2026-10-18T09:00:00Z',
  opportunities: [opportunity],
  opportunitiesHasMore: false,
  history: [history],
  historyHasMore: false,
  hasLegacyHistoryGap: false,
};
describe('Opportunity contract', () => {
  it('preserves exact fee and initial revision', () => {
    const result = decodeOpportunities(lead).opportunities[0];
    expect(result.expectedFee).toBe('9007199254740993.01');
    expect(result.revision).toBe('0');
  });
  it('rejects numeric money, revisions and invalid identities', () => {
    for (const change of [{ expectedFee: 1.2 }, { revision: 0 }, { proposalId: 'guess' }, { createdAt: 'not-a-date' }])
      expect(() =>
        decodeOpportunities({ ...lead, opportunities: [{ ...opportunity, ...change }] }),
      ).toThrow();
  });
  it('requires the server paging flag and retains the exact opportunity timestamp', () => {
    expect(decodeOpportunities(lead).opportunities[0].createdAt).toBe(opportunity.createdAt);
    expect(() => decodeOpportunities({ ...lead, opportunitiesHasMore: undefined })).toThrow();
    expect(() => decodeOpportunities({ ...lead, opportunities: [], opportunitiesHasMore: true })).toThrow();
  });
  it('bounds the opportunity projection', () => {
    expect(() =>
      decodeOpportunities({ ...lead, opportunities: Array(101).fill(opportunity) }),
    ).toThrow();
  });
  it('retains a valid lead conversion identity in its activity history', () => {
    const result = decodeOpportunities(lead);
    expect(result.source).toBe('Referral');
    expect(result.history[0].action).toBe('CLIENT_CONVERSION_LINKED');
    expect(result.history[0].proposalRevision).toBe('1');
    expect(result.history[0].clientId).toBe(id);
  });
  it('rejects unknown activity and malformed conversion identities', () => {
    for (const change of [{ action: 'IMPLIED_APPROVAL' }, { clientId: 'unknown' }])
      expect(() =>
        decodeOpportunities({ ...lead, history: [{ ...history, ...change }] }),
      ).toThrow();
  });
});

describe('Opportunity workspace paging and focus recovery', () => {
  const priorId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
  const secondPriorId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
  const otherLeadId = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
  let http: HttpTestingController;
  let routeParams: BehaviorSubject<ParamMap>;
  let session: {
    current: WritableSignal<{ userId: string; firmId: string; generation: string; staff: boolean } | null>;
    invalidation: WritableSignal<number>;
    clear: () => void;
  };
  beforeEach(() => {
    routeParams = new BehaviorSubject(convertToParamMap({ id }));
    const current = signal<{ userId: string; firmId: string; generation: string; staff: boolean } | null>({
      userId: id,
      firmId: id,
      generation: '1',
      staff: true,
    });
    const invalidation = signal(0);
    session = {
      current,
      invalidation,
      clear: () => {
        current.set(null);
        invalidation.update((value) => value + 1);
      },
    };
    TestBed.configureTestingModule({
      imports: [Opportunities],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: routeParams } },
        { provide: SessionService, useValue: session },
        { provide: Drafts, useValue: { load: () => null, save: () => undefined, clear: () => undefined } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  function open(overrides: Partial<typeof lead> = {}) {
    const fixture = TestBed.createComponent(Opportunities);
    fixture.detectChanges();
    TestBed.tick();
    const reads = http.match('/api/ui/leads/' + id).filter((request) => !request.cancelled);
    expect(reads).toHaveLength(1);
    reads[0].flush({ ...lead, historyHasMore: true, ...overrides });
    TestBed.tick();
    fixture.detectChanges();
    return fixture;
  }
  function loadOlder(fixture: ReturnType<typeof open>) {
    const button = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older activity') as HTMLButtonElement;
    button.click();
    return http.expectOne((request) =>
      request.urlWithParams.startsWith('/api/ui/leads/' + id + '?historyBeforeOccurredAt='),
    );
  }
  function loadOlderOpportunities(fixture: ReturnType<typeof open>) {
    const button = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older opportunities') as HTMLButtonElement;
    button.click();
    return http.expectOne((request) =>
      request.urlWithParams.startsWith('/api/ui/leads/' + id + '?opportunityBeforeCreatedAt='),
    );
  }
  function startKeyboardOpportunityLoad(fixture: ReturnType<typeof open>) {
    const button = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older opportunities') as HTMLButtonElement;
    button.focus();
    button.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    button.click();
    const request = http.expectOne((pending) =>
      pending.urlWithParams.startsWith('/api/ui/leads/' + id + '?opportunityBeforeCreatedAt='),
    );
    return { button, request };
  }
  const finalPage = {
    ...lead,
    history: [{ ...history, id: priorId, action: 'OPPORTUNITY_CREATED', occurredAt: '2026-10-18T09:00:00Z' }],
    historyHasMore: false,
  };
  it('moves focus to the named history section when the focused load button is removed', () => {
    const fixture = open();
    const loadButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older activity') as HTMLButtonElement;
    expect(loadButton).toBeTruthy();
    loadButton.focus();
    expect(document.activeElement).toBe(loadButton);

    loadOlder(fixture).flush(finalPage);
    fixture.detectChanges();
    TestBed.tick();

    const section = fixture.nativeElement.querySelector('section[aria-labelledby="lead-history-heading"]') as HTMLElement;
    expect(section.tabIndex).toBe(-1);
    expect(loadButton.isConnected).toBe(false);
    expect(document.activeElement).toBe(section);
    fixture.destroy();
  });
  it('does not steal focus after a pointer interaction on the nonfocusable history heading', () => {
    const fixture = open();
    const loadButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older activity') as HTMLButtonElement;
    loadButton.focus();
    const request = loadOlder(fixture);
    const heading = fixture.nativeElement.querySelector('#lead-history-heading') as HTMLElement;
    heading.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    heading.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(document.activeElement).toBe(loadButton);

    request.flush(finalPage);
    fixture.detectChanges();
    TestBed.tick();

    expect(loadButton.isConnected).toBe(false);
    expect(document.activeElement).toBe(document.body);
    fixture.destroy();
  });
  it('appends and deduplicates older opportunities without replacing workspace state or timestamp precision', () => {
    const fixture = open({ opportunitiesHasMore: true });
    const component = fixture.componentInstance;
    const currentHistory = component.data()!.history;
    component.selected.set(opportunity);
    component.draft.serviceRoute = 'Draft in progress';
    component.profile = 'Reviewed profile';
    component.proposalReviewed = true;

    const loadButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older opportunities') as HTMLButtonElement;
    loadButton.focus();
    loadButton.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    loadButton.click();
    const request = http.expectOne((pending) =>
      pending.urlWithParams.startsWith('/api/ui/leads/' + id + '?opportunityBeforeCreatedAt='),
    );
    const query = new URLSearchParams(request.request.urlWithParams.split('?')[1]);
    expect(query.get('opportunityBeforeCreatedAt')).toBe(opportunity.createdAt);
    expect(query.get('opportunityBeforeId')).toBe(opportunity.id);

    const older = {
      ...opportunity,
      id: priorId,
      serviceRoute: 'Older audit',
      createdAt: '2026-10-17T09:00:00.7654321+03:00',
    };
    const oldest = {
      ...opportunity,
      id: secondPriorId,
      serviceRoute: 'Oldest audit',
      createdAt: '2026-10-16T09:00:00.1234567+03:00',
    };
    request.flush({
      ...lead,
      name: 'Stale page metadata',
      history: [],
      opportunities: [opportunity, older, older, oldest],
      opportunitiesHasMore: true,
    });
    fixture.detectChanges();
    TestBed.tick();

    expect(component.data()!.opportunities.map((item) => item.id)).toEqual([id, priorId, secondPriorId]);
    expect(component.data()!.name).toBe('Lead');
    expect(component.data()!.history).toBe(currentHistory);
    expect(component.selected()).toBe(opportunity);
    expect(component.draft.serviceRoute).toBe('Draft in progress');
    expect(component.profile).toBe('Reviewed profile');
    expect(component.proposalReviewed).toBe(true);
    const firstNewHeading = fixture.nativeElement.querySelectorAll('section[aria-labelledby="lead-opportunities-heading"] article h3')[1] as HTMLElement;
    expect(firstNewHeading.textContent).toContain('Older audit');
    expect(document.activeElement).toBe(firstNewHeading);
    expect(fixture.nativeElement.querySelector('section[aria-labelledby="lead-opportunities-heading"]')).toBeTruthy();
    fixture.destroy();
  });
  it('falls back to the named opportunities section when the final older page adds no rows', () => {
    const fixture = open({ opportunitiesHasMore: true });
    const loadButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older opportunities') as HTMLButtonElement;
    expect(loadButton).toBeTruthy();
    loadButton.focus();
    loadButton.click();
    const request = http.expectOne((pending) =>
      pending.urlWithParams.startsWith('/api/ui/leads/' + id + '?opportunityBeforeCreatedAt='),
    );
    request.flush({ ...lead, opportunities: [], opportunitiesHasMore: false });
    fixture.detectChanges();
    TestBed.tick();

    const section = fixture.nativeElement.querySelector('section[aria-labelledby="lead-opportunities-heading"]') as HTMLElement;
    expect(section.tabIndex).toBe(-1);
    expect(fixture.nativeElement.querySelector('#lead-opportunities-heading')?.textContent).toBe('Opportunities');
    expect(document.activeElement).toBe(section);
    fixture.destroy();
  });
  it('does not steal focus after a later pointer interaction during older-opportunity loading', () => {
    const fixture = open({ opportunitiesHasMore: true });
    const loadButton = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older opportunities') as HTMLButtonElement;
    loadButton.focus();
    loadButton.click();
    const request = http.expectOne((pending) =>
      pending.urlWithParams.startsWith('/api/ui/leads/' + id + '?opportunityBeforeCreatedAt='),
    );
    fixture.componentInstance.loadOlderHistory();
    const historyRequest = http.expectOne((pending) =>
      pending.urlWithParams.startsWith('/api/ui/leads/' + id + '?historyBeforeOccurredAt='),
    );
    const heading = fixture.nativeElement.querySelector('#lead-opportunities-heading') as HTMLElement;
    heading.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    heading.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    request.flush({ ...lead, opportunities: [{ ...opportunity, id: priorId }], opportunitiesHasMore: false });
    historyRequest.flush(finalPage);
    fixture.detectChanges();
    TestBed.tick();

    expect(fixture.componentInstance.data()!.opportunities.map((item) => item.id)).toEqual([id, priorId]);
    expect(fixture.componentInstance.data()!.history.map((event) => event.id)).toEqual([priorId, id]);
    expect(loadButton.isConnected).toBe(false);
    expect(document.activeElement).toBe(document.body);
    fixture.destroy();
  });
  it('restores retry focus after decode and transient failures but yields to later interaction', () => {
    const fixture = open({ opportunitiesHasMore: true });
    const first = startKeyboardOpportunityLoad(fixture);
    first.request.flush({ ...lead, id: otherLeadId });
    fixture.detectChanges();
    TestBed.tick();
    expect(document.activeElement).toBe(first.button);
    expect(fixture.nativeElement.textContent).toContain('could not be read safely');

    const second = startKeyboardOpportunityLoad(fixture);
    second.request.flush({ code: 'temporarily.unavailable' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    TestBed.tick();
    expect(document.activeElement).toBe(second.button);
    expect(fixture.nativeElement.textContent).toContain('Older opportunities are unavailable');

    const third = startKeyboardOpportunityLoad(fixture);
    const refresh = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Refresh persisted state') as HTMLButtonElement;
    refresh.focus();
    refresh.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    third.request.flush({ code: 'temporarily.unavailable' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    TestBed.tick();
    expect(document.activeElement).toBe(refresh);
    fixture.destroy();
  });
  it.each(['route', 'session', 'refresh'] as const)('cancels older-opportunity work on %s changes', (boundary) => {
    const fixture = open({ opportunitiesHasMore: true });
    const request = loadOlderOpportunities(fixture);
    if (boundary === 'route') {
      routeParams.next(convertToParamMap({ id: otherLeadId }));
      const next = http.expectOne('/api/ui/leads/' + otherLeadId);
      expect(request.cancelled).toBe(true);
      next.flush({ ...lead, id: otherLeadId, opportunitiesHasMore: false });
      TestBed.tick();
      fixture.detectChanges();
      expect(fixture.componentInstance.data()?.id).toBe(otherLeadId);
    } else if (boundary === 'session') {
      session.current.set(null);
      session.invalidation.update((value) => value + 1);
      TestBed.tick();
      fixture.detectChanges();
      expect(request.cancelled).toBe(true);
      expect(fixture.componentInstance.data()).toBeNull();
      http.expectNone('/api/ui/leads/' + id);
    } else {
      const refresh = [...fixture.nativeElement.querySelectorAll('button')]
        .find((element) => element.textContent.trim() === 'Refresh persisted state') as HTMLButtonElement;
      refresh.click();
      const refreshed = http.expectOne('/api/ui/leads/' + id);
      expect(request.cancelled).toBe(true);
      refreshed.flush({ ...lead, opportunitiesHasMore: true });
      TestBed.tick();
      fixture.detectChanges();
      expect(fixture.componentInstance.data()?.opportunities).toEqual([opportunity]);
    }
    fixture.destroy();
  });
  it('clears the protected workspace after a denied older-opportunities request without retrying', () => {
    const fixture = open({ opportunitiesHasMore: true });
    const retry = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Load older opportunities') as HTMLButtonElement;
    retry.focus();
    const request = loadOlderOpportunities(fixture);
    const refresh = [...fixture.nativeElement.querySelectorAll('button')]
      .find((element) => element.textContent.trim() === 'Refresh persisted state') as HTMLButtonElement;
    refresh.focus();
    refresh.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    request.flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    TestBed.tick();

    expect(fixture.componentInstance.data()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Current firm-wide commercial authority is required.');
    expect(document.activeElement).toBe(refresh);
    http.expectNone('/api/ui/leads/' + id);
    fixture.destroy();
  });
});
