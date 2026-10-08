import { TestBed } from '@angular/core/testing';
import { APP_BASE_HREF } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { CurrencyRemeasurement, decodeSchedule } from './remeasurement';
import { decode } from '../../core/decode';
import {
  emptyRemeasurement,
  validDraft,
  remeasurementIntent,
  remeasurementAmount,
} from './remeasurement-drafts';
import { SessionService } from '../../core/session';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const path = '/api/ui/accounting/remeasurement',
  choicesPath = path + '/choices?periodId=' + id + '&engagementId=' + id;
const context = {
  periodId: id,
  clientId: id,
  engagementId: id,
  clientName: 'Synthetic client',
  periodCode: 'FY26',
  endDate: '2026-12-31',
  engagementLabel: 'Audit',
};
const choices = {
  functionalCurrency: 'QAR',
  baseRevision: 'a'.repeat(64),
  canPrepare: true,
  hasMoreLines: false,
  rateSets: [{ id, label: 'Approved FX v1' }],
  policies: [{ id, label: 'Approved policy' }],
  glLines: [
    {
      id,
      currency: 'USD',
      originalAmount: '100.123456',
      functionalAmount: '370.456789',
      label: 'Sealed imported GL',
    },
  ],
};
const prepared = () => ({
  rateSet: id,
  policy: id,
  reviewed: false,
  lines: [
    {
      reference: 'open-1',
      snapshotId: id,
      sourceLineId: id,
      isMonetary: true,
      currency: 'USD',
      foreignAmount: '123456789.123456',
      priorCarrying: '370.123456',
      historicalDate: '',
    },
  ],
});
describe('Native remeasurement intent and revision controls', () => {
  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [CurrencyRemeasurement],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: APP_BASE_HREF, useValue: '/ui/' },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '0',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  function open() {
    const f = TestBed.createComponent(CurrencyRemeasurement),
      http = TestBed.inject(HttpTestingController);
    f.detectChanges();
    http.expectOne(path).flush({
      contexts: [context, { ...context, periodId: other, clientId: other }],
      history: [],
    });
    TestBed.tick();
    http.expectOne(choicesPath).flush(choices);
    TestBed.tick();
    f.detectChanges();
    expect((f.nativeElement.querySelector('select') as HTMLSelectElement).value).toBe(
      id + '|' + id,
    );
    return { f, c: f.componentInstance, http };
  }
  it('bounds allowlisted intent, rejects authorization and secret fields, preserves decimal text', () => {
    const intent = remeasurementIntent(prepared());
    expect(validDraft(intent)).toEqual(intent);
    for (const raw of [
      { ...intent, reviewed: true },
      { ...intent, token: 'never' },
      { ...intent, lines: [{ ...intent.lines[0], password: 'never' }] },
      { ...intent, lines: [] },
      { ...intent, lines: Array(501).fill(intent.lines[0]) },
    ])
      expect(validDraft(raw)).toBeNull();
    expect(remeasurementAmount('123456789.123456')).toBe('123456789.123456');
    for (const raw of ['1.1234567', '123456789012345', '1e3', '1,000.12'])
      expect(remeasurementAmount(raw)).toBeNull();
  });
  it('recovers explicitly from tab storage with review cleared', () => {
    const { f, c } = open();
    c.model.set(prepared());
    TestBed.tick();
    c.model.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    expect(c.canPrepare()).toBe(true);
    expect(c.saveDraft()).toBe(true);
    const saved = Object.values(sessionStorage).join();
    expect(saved).toContain('123456789.123456');
    expect(saved).not.toContain('reviewed');
    expect(localStorage.length).toBe(0);
    f.destroy();
    const next = open();
    expect(next.c.model().lines[0].reference).toBe('');
    expect(next.c.candidate()).toBe('ready');
    next.c.recoverDraft();
    TestBed.tick();
    expect(next.c.model().reviewed).toBe(false);
    expect(next.c.model().lines[0].foreignAmount).toBe('123456789.123456');
    expect(next.c.canPrepare()).toBe(false);
    next.f.destroy();
  });
  it('retains edits but blocks stale inputs until explicit rebase and fresh review', () => {
    const { f, c, http } = open();
    c.model.set(prepared());
    TestBed.tick();
    c.saveDraft();
    c.refreshInputs();
    http.expectOne(choicesPath).flush({ ...choices, baseRevision: 'b'.repeat(64) });
    TestBed.tick();
    expect(c.model().lines[0].reference).toBe('open-1');
    expect(c.stale()).toBe(true);
    expect(c.saveDraft()).toBe(false);
    c.model.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    expect(c.canPrepare()).toBe(false);
    c.rebase();
    TestBed.tick();
    expect(c.model().reviewed).toBe(false);
    c.model.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    expect(c.canPrepare()).toBe(true);
    f.destroy();
  });
  it('guards client context and clears previous line fields after explicit discard', async () => {
    const { f, c, http } = open();
    c.model.set(prepared());
    TestBed.tick();
    const dialog = vi
      .spyOn(TestBed.inject(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of('keep') } as never);
    const select = document.createElement('select');
    select.innerHTML = `<option value="${id}|${id}"></option><option value="${other}|${id}"></option>`;
    select.value = other + '|' + id;
    await c.contextChanged({ target: select } as unknown as Event);
    expect(c.context()).toBe(id + '|' + id);
    expect(select.value).toBe(id + '|' + id);
    expect(c.model().lines[0].reference).toBe('open-1');
    dialog.mockReturnValue({ afterClosed: () => of('discard') } as never);
    select.value = other + '|' + id;
    await c.contextChanged({ target: select } as unknown as Event);
    TestBed.tick();
    http.expectOne(path + '/choices?periodId=' + other + '&engagementId=' + id).flush(choices);
    TestBed.tick();
    expect(c.model().lines[0].reference).toBe('');
    f.destroy();
  });
  it('reports failed storage and prevents save-navigation from losing edits', async () => {
    const { f, c } = open();
    c.model.set(prepared());
    TestBed.tick();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('quota');
    });
    expect(c.saveDraft()).toBe(false);
    expect(c.draftMessage()).toContain('memory only');
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of('save'),
    } as never);
    expect(await c.confirmNavigation()).toBe(false);
    expect(c.model().lines[0].reference).toBe('open-1');
    f.destroy();
  });
  it('fences unknown submissions and never retries automatically', async () => {
    const { f, c, http } = open();
    c.model.set(prepared());
    TestBed.tick();
    c.model.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    const pending = c.prepare(),
      request = http.expectOne(path);
    expect(request.request.body.items[0].foreignAmount).toBe('123456789.123456');
    expect(request.request.body.reviewToken).toBe(choices.baseRevision);
    expect(request.request.body.reviewed).toBe(true);
    request.flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    http.expectOne(path).flush({ contexts: [context], history: [] });
    TestBed.tick();
    expect(c.cmd.uncertain()).toBe(true);
    expect(c.canPrepare()).toBe(false);
    expect(Object.values(sessionStorage).join()).toContain('"submissionPending":true');
    await c.prepare();
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
  });
  it('treats an unsupported successful response as unconfirmed instead of losing submitted intent', async () => {
    const { f, c, http } = open();
    c.model.set(prepared());
    TestBed.tick();
    c.model.update((m) => ({ ...m, reviewed: true }));
    TestBed.tick();
    const pending = c.prepare();
    http.expectOne(path).flush({ value: { unexpected: true } });
    await pending;
    http.expectOne(path).flush({ contexts: [context], history: [] });
    TestBed.tick();
    expect(c.cmd.uncertain()).toBe(true);
    expect(c.model().lines[0].reference).toBe('open-1');
    expect(Object.values(sessionStorage).join()).toContain('"submissionPending":true');
    http.expectNone(path + '/[object Object]');
    f.destroy();
  });
  it('retains historical missing GL lineage without accepting a malformed digest', () => {
    const raw = {
      schedule: {
        id,
        clientId: id,
        engagementId: id,
        periodId: id,
        asOfDate: '2026-12-31',
        functionalCurrency: 'QAR',
        inputHash: 'a'.repeat(64),
        itemCount: 1,
        totalForeignExchangeAdjustment: '0',
        status: 'APPROVED',
        createdByUserId: id,
        approvedByUserId: other,
        items: [
          {
            stableItemReference: 'historical',
            evidenceSnapshotId: id,
            evidenceSha256: 'a'.repeat(64),
            sourceGeneralLedgerLineId: null,
            sourceGlLineDigest: '',
            isMonetary: true,
            foreignCurrency: 'USD',
            foreignCurrencyAmount: '100',
            priorFunctionalCarryingAmount: '370',
            rateDate: '2026-12-31',
            rateType: 'CLOSING',
            appliedRate: '3.7',
            remeasuredFunctionalAmount: '370',
            foreignExchangeAdjustment: '0',
            roundingAdjustment: '0',
          },
        ],
      },
      reviewToken: 'b'.repeat(64),
      canApprove: false,
      rateSetVersionId: id,
      translationPolicyVersionId: id,
      rateSource: 'Synthetic historic evidence',
      closingRule: 'CLOSING',
      historicalRule: 'HISTORICAL',
      blocker: 'Historical workpaper is available for inspection only.',
    };
    expect(decode(decodeSchedule, raw).schedule.items[0].sourceGlLineDigest).toBe('');
    const { f, c } = open();
    c.review.set(decode(decodeSchedule, raw));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Client ' + id);
    expect(f.nativeElement.textContent).toContain('engagement ' + id);
    expect(f.nativeElement.textContent).toContain('period ' + id);
    expect(f.nativeElement.textContent).toContain('No immutable GL link recorded');
    f.destroy();
    raw.schedule.items[0].sourceGlLineDigest = 'malformed';
    expect(() => decode(decodeSchedule, raw)).toThrow();
  });
  it('withdraws intent and saved drafts when session authority changes', () => {
    const { f, c } = open();
    c.model.set(prepared());
    TestBed.tick();
    c.saveDraft();
    TestBed.inject(SessionService).current.set(null);
    TestBed.inject(SessionService).invalidation.update((n) => n + 1);
    TestBed.tick();
    expect(JSON.parse(JSON.stringify(c.model()))).toEqual(emptyRemeasurement());
    expect(c.review()).toBeNull();
    expect(sessionStorage.length).toBe(0);
    f.destroy();
  });
});
