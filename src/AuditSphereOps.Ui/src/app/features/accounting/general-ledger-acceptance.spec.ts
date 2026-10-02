import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { SourceAcceptance, decodeGeneralLedgerAcceptance } from './source-acceptance';

const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const url = `/api/ui/gl-sources/${id}/acceptance`,
  revision = 'a'.repeat(64);
const source = {
  importBatchId: id,
  clientId: id,
  engagementId: id,
  periodId: id,
  periodCode: '2026',
  bookId: null,
  currency: 'QAR',
  entity: 'SYN-GL',
  importState: 'SEALED',
  rowCount: 150,
  rawFileSha256: 'c'.repeat(64),
  sourceHash: 'b'.repeat(64),
  profileVersion: 'gl-v1',
  parserVersion: 'parser-v1',
  importedByUserId: other,
  importedAt: '2026-01-01T00:00:00Z',
  inputGeneration: 1,
  receipt: null,
  selected: null,
  revision,
  canAccept: true,
  blocker: null,
};
const accepted = {
  ...source,
  revision: 'd'.repeat(64),
  inputGeneration: 2,
  canAccept: false,
  blocker: 'Already accepted.',
  receipt: {
    id: other,
    firmId: id,
    clientId: id,
    engagementId: id,
    sourceKind: 'GL',
    trialBalanceDatasetId: null,
    importBatchId: id,
    sourceIdentityHash: source.sourceHash,
    decision: 'ACCEPTED',
    evidenceReference: 'Independent GL review',
    acceptedByUserId: id,
    createdAt: '2026-01-01T00:00:00Z',
  },
  selected: {
    decisionId: other,
    sourceKind: 'GL',
    trialBalanceDatasetId: null,
    importBatchId: id,
    sourceIdentityHash: source.sourceHash,
    acceptedByUserId: id,
    acceptedAt: '2026-01-01T00:00:00Z',
    inputGeneration: 2,
  },
};

describe('Native independent GL source acceptance', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [SourceAcceptance],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: new BehaviorSubject(convertToParamMap({ id })),
            snapshot: { data: { sourceKind: 'GL' } },
          },
        },
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
    TestBed.resetTestingModule();
  });
  async function open(value: object = source) {
    const f = TestBed.createComponent(SourceAcceptance);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(url).flush(value);
    await f.whenStable();
    f.detectChanges();
    return { f, c: f.componentInstance, http };
  }
  function assent(c: SourceAcceptance) {
    c.model.set({ evidenceReference: 'Independent GL review', reviewed: true });
    c.recordAssent({ target: { checked: true } } as unknown as Event);
  }
  it('shows real batch metadata, no TB validation verdict, and submits reviewed GL intent once', async () => {
    const { f, c, http } = await open();
    expect(c.review(), c.error()).not.toBeNull();
    TestBed.tick();
    f.detectChanges();
    const text = f.nativeElement.textContent;
    expect(text).toContain('GL import batch');
    expect(text).toContain('150');
    expect(text).toContain('completeness');
    expect(text).not.toContain('Worker validation');
    expect(text).not.toContain('Source revision');
    assent(c);
    const pending = c.accept();
    const write = http.expectOne(url);
    expect(write.request.body).toEqual({
      revision,
      reviewed: true,
      evidenceReference: 'Independent GL review',
    });
    write.flush({ value: other });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(url).flush(accepted);
    await pending;
    expect(c.review()?.receipt?.importBatchId).toBe(id);
    expect(c.canSubmit()).toBe(false);
    f.destroy();
  });
  it('retains the unknown-outcome write fence across reload and reconciles without automatic retries', async () => {
    const first = await open();
    assent(first.c);
    const pending = first.c.accept();
    first.http.expectOne(url).flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    expect(first.c.uncertain()).toBe(true);
    first.f.destroy();
    const { f, c, http } = await open(accepted);
    expect(c.uncertain()).toBe(true);
    expect(c.model().reviewed).toBe(false);
    expect(await c.confirmNavigation()).toBe(false);
    c.acknowledge();
    expect(c.uncertain()).toBe(true);
    const reading = c.refresh(true);
    http.expectOne(url).flush(accepted);
    await reading;
    c.acknowledge();
    expect(c.uncertain()).toBe(false);
    expect(c.canSubmit()).toBe(false);
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
  });
  it('removes stale or failed protected reads and rejects another source or current pointer identity', async () => {
    const { f, c, http } = await open();
    assent(c);
    const reading = c.refresh();
    expect(c.review()).toBeNull();
    http.expectOne(url).flush({ ...source, importBatchId: other });
    await reading;
    expect(c.review()).toBeNull();
    const retry = c.refresh();
    http.expectOne(url).flush(source);
    await retry;
    TestBed.inject(SessionService).current.set(null);
    TestBed.inject(SessionService).invalidation.update((v) => v + 1);
    TestBed.tick();
    expect(c.review()).toBeNull();
    expect(c.model().evidenceReference).toBe('');
    f.destroy();
    expect(() =>
      decodeGeneralLedgerAcceptance({ ...source, rawFileSha256: 'invalid' }, ''),
    ).toThrow();
    expect(() =>
      decodeGeneralLedgerAcceptance(
        { ...accepted, receipt: { ...accepted.receipt, trialBalanceDatasetId: id } },
        '',
      ),
    ).toThrow();
    expect(() =>
      decodeGeneralLedgerAcceptance(
        { ...accepted, selected: { ...accepted.selected, sourceKind: 'TB' } },
        '',
      ),
    ).toThrow();
    expect(() =>
      decodeGeneralLedgerAcceptance(
        { ...accepted, selected: { ...accepted.selected, sourceIdentityHash: 'e'.repeat(64) } },
        '',
      ),
    ).toThrow();
  });
});
