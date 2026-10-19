import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
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
    for (const change of [{ expectedFee: 1.2 }, { revision: 0 }, { proposalId: 'guess' }])
      expect(() =>
        decodeOpportunities({ ...lead, opportunities: [{ ...opportunity, ...change }] }),
      ).toThrow();
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

describe('Opportunity history focus recovery', () => {
  const priorId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Opportunities],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id })) } },
        {
          provide: SessionService,
          useValue: {
            current: signal({ userId: id, firmId: id, generation: '1', staff: true }),
            invalidation: signal(0),
            clear: () => undefined,
          },
        },
        { provide: Drafts, useValue: { load: () => null, save: () => undefined, clear: () => undefined } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  function open() {
    const fixture = TestBed.createComponent(Opportunities);
    fixture.detectChanges();
    TestBed.tick();
    const reads = http.match('/api/ui/leads/' + id).filter((request) => !request.cancelled);
    expect(reads).toHaveLength(1);
    reads[0].flush({ ...lead, historyHasMore: true });
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
});
