import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { SourceAcceptance, decodeAcceptance, decodeAcceptanceDraft } from './source-acceptance';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const url = `/api/ui/datasets/${id}/source/acceptance`;
const revision = 'a'.repeat(64);
const source = {
  datasetId: id,
  clientId: id,
  engagementId: id,
  periodId: null,
  periodCode: null,
  sourceRevision: 1,
  currency: 'QAR',
  entity: 'SYN',
  importState: 'SEALED',
  validationStatus: 'Accepted',
  balanced: true,
  sourceHash: 'b'.repeat(64),
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
  revision: 'c'.repeat(64),
  inputGeneration: 2,
  canAccept: false,
  blocker: 'Already accepted.',
  receipt: {
    id: other,
    firmId: id,
    clientId: id,
    engagementId: id,
    sourceKind: 'TB',
    trialBalanceDatasetId: id,
    importBatchId: null,
    sourceIdentityHash: source.sourceHash,
    decision: 'ACCEPTED',
    evidenceReference: 'Independent review',
    acceptedByUserId: id,
    createdAt: '2026-01-01T00:00:00Z',
  },
  selected: {
    decisionId: other,
    sourceKind: 'TB',
    trialBalanceDatasetId: id,
    importBatchId: null,
    sourceIdentityHash: source.sourceHash,
    acceptedByUserId: id,
    acceptedAt: '2026-01-01T00:00:00Z',
    inputGeneration: 2,
  },
};
describe('Native independent source acceptance', () => {
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
          useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id })) },
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
  function intent(c: SourceAcceptance) {
    c.model.set({ evidenceReference: 'Independent review', reviewed: true });
    c.recordAssent({ target: { checked: true } } as unknown as Event);
  }
  it('submits exactly reviewed intent and retains an independent decision', async () => {
    const { f, c, http } = await open();
    expect(c.canSubmit()).toBe(false);
    intent(c);
    const pending = c.accept();
    const write = http.expectOne(url);
    expect(write.request.method).toBe('POST');
    expect(write.request.body).toEqual({
      revision,
      evidenceReference: 'Independent review',
      reviewed: true,
    });
    write.flush({ value: other });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(url).flush(accepted);
    await pending;
    f.detectChanges();
    expect(c.review()?.receipt?.id).toBe(other);
    expect(c.canSubmit()).toBe(false);
    expect(f.nativeElement.textContent).toContain('currently selected source');
    f.destroy();
  });
  it('never restores assent and fences pending outcomes until an explicit persisted read and acknowledgment', async () => {
    const { f, c, http } = await open();
    intent(c);
    expect(c.saveDraft()).toBe(true);
    c.model.set({ evidenceReference: 'Changed', reviewed: true });
    c.recoverDraft();
    expect(c.model().evidenceReference).toBe('Independent review');
    expect(c.model().reviewed).toBe(false);
    intent(c);
    const pending = c.accept();
    http.expectOne(url).flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    c.discardDraft();
    expect(c.uncertain()).toBe(true);
    c.acknowledge();
    expect(c.uncertain()).toBe(true);
    const reading = c.refresh(true);
    http.expectOne(url).flush(accepted);
    await reading;
    expect(c.reconciled()).toBe(true);
    c.acknowledge();
    expect(c.uncertain()).toBe(false);
    expect(c.model().reviewed).toBe(false);
    expect(c.canSubmit()).toBe(false);
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
  });
  it('changed metadata withdraws assent, failures remove protected source and session changes cancel late writes', async () => {
    const { f, c, http } = await open();
    intent(c);
    const refreshing = c.refresh();
    expect(c.review()).toBeNull();
    expect(c.model().reviewed).toBe(false);
    http.expectOne(url).flush({ ...source, revision: 'c'.repeat(64) });
    await refreshing;
    expect(c.model().evidenceReference).toBe('Independent review');
    intent(c);
    const pending = c.accept();
    const write = http.expectOne(url);
    TestBed.inject(SessionService).invalidation.update((v) => v + 1);
    TestBed.inject(SessionService).current.set(null);
    TestBed.tick();
    write.flush({ value: other });
    await pending;
    expect(c.review()).toBeNull();
    expect(c.model().evidenceReference).toBe('');
    f.destroy();
  });
  it('a pending checkpoint with a changed base cannot silently re-enable writes after reload', async () => {
    let { f, c, http } = await open();
    intent(c);
    const pending = c.accept();
    http.expectOne(url).flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    f.destroy();
    const reopened = await open({ ...source, revision: 'c'.repeat(64), inputGeneration: 2 });
    expect(reopened.c.uncertain()).toBe(true);
    expect(reopened.c.canSubmit()).toBe(false);
    expect(reopened.c.model().reviewed).toBe(false);
    reopened.f.destroy();
  });
  it('rejects malformed eligibility, receipt scope, current generation and any saved assent', () => {
    expect(() => decodeAcceptance({ ...source, validationStatus: 'Pending' }, '')).toThrow();
    expect(() =>
      decodeAcceptance({ ...accepted, receipt: { ...accepted.receipt, clientId: other } }, ''),
    ).toThrow();
    expect(() =>
      decodeAcceptance({ ...accepted, selected: { ...accepted.selected, inputGeneration: 3 } }, ''),
    ).toThrow();
    expect(decodeAcceptanceDraft({ evidenceReference: 'good', reviewed: true })).toBeNull();
    expect(decodeAcceptanceDraft({ evidenceReference: 'a'.repeat(2001) })).toBeNull();
    expect(decodeAcceptanceDraft({ evidenceReference: 'good' })).toEqual({
      evidenceReference: 'good',
    });
  });
});
