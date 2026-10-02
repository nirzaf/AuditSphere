import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TrialBalanceUpload } from './tb-upload';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { uploadCheckpoint } from './tb-upload-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const root = `/api/ui/engagements/${id}/tb-intake`;
const review = {
  engagementId: id,
  clientId: id,
  fileSha256: 'f'.repeat(64),
  revision: 'a'.repeat(64),
  canImport: true,
  periods: [
    {
      periodCode: 'FY26',
      periodId: id,
      periodRevision: 'b'.repeat(64),
      rowCount: 2,
      currency: 'QAR',
      netTotal: '0.000000',
      balanced: true,
      error: null,
      datasetId: null,
      importState: null,
      validationStatus: null,
      operationId: null,
      operationState: null,
    },
  ],
};
const persisted = {
  ...review,
  periods: [
    { ...review.periods[0], datasetId: id, importState: 'SEALED', validationStatus: 'Pending' },
  ],
};
describe('Native reviewed trial-balance upload', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [TrialBalanceUpload],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
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
    sessionStorage.clear();
  });
  function open() {
    const f = TestBed.createComponent(TrialBalanceUpload);
    f.componentRef.setInput('engagementId', id);
    f.componentRef.setInput('clientId', id);
    f.detectChanges();
    TestBed.tick();
    return { f, c: f.componentInstance, http: TestBed.inject(HttpTestingController) };
  }
  const file = () => new File(['Synthetic upload'], 'synthetic.csv', { type: 'text/csv' });
  async function pick(
    c: TrialBalanceUpload,
    http: HttpTestingController,
    response: unknown = review,
  ) {
    const p = c.pick({ target: { files: [file()], value: '' } } as unknown as Event);
    http.expectOne(root + '/preview').flush({ value: response });
    await p;
    TestBed.tick();
  }
  function assent(c: TrialBalanceUpload) {
    c.assent({ target: { checked: true } } as unknown as Event);
    c.model.set({ reviewed: true });
  }
  it('bounds metadata recovery and never persists file bytes, rows or assent', () => {
    const metadata = { fileName: 'synthetic.csv', byteCount: 16, fileSha256: 'f'.repeat(64) };
    expect(uploadCheckpoint(metadata)).toEqual(metadata);
    for (const extra of [
      { reviewed: true },
      { password: 'unused' },
      { bytes: 'unused' },
      { rows: [] },
    ])
      expect(uploadCheckpoint({ ...metadata, ...extra })).toBeNull();
    expect(uploadCheckpoint({ ...metadata, byteCount: 26 * 1024 * 1024 })).toBeNull();
  });
  it('requires fresh exact review and validates successful period identities', async () => {
    const { c, http } = open();
    await pick(c, http);
    await c.importFile();
    http.expectNone(root + '/import');
    assent(c);
    const p = c.importFile();
    const req = http.expectOne(root + '/import');
    expect(req.request.body.get('fileSha256')).toBe(review.fileSha256);
    expect(req.request.body.get('revision')).toBe(review.revision);
    expect(sessionStorage.length).toBe(1);
    expect(sessionStorage.getItem(sessionStorage.key(0)!)).not.toContain('reviewed');
    req.flush({ value: persisted });
    await p;
    expect(c.allImported()).toBe(true);
    expect(c.uncertain()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    assent(c);
    await c.importFile();
    http.expectNone(root + '/import');
  });
  it('refuses an invalid period and mismatched preview context', async () => {
    const { c, http } = open();
    await pick(c, http, {
      ...review,
      canImport: false,
      periods: [{ ...review.periods[0], balanced: false, error: 'Not balanced' }],
    });
    assent(c);
    await c.importFile();
    http.expectNone(root + '/import');
    expect(c.canImport()).toBe(false);
    const p = c.reconcile();
    http.expectOne(root + '/reconcile').flush({ value: { ...review, clientId: other } });
    await p;
    expect(c.review()).toBeNull();
    expect(c.message()).toContain('mismatched');
  });
  it('fences unknown imports until a successful persisted read and explicit acknowledgment', async () => {
    const { c, http } = open();
    await pick(c, http);
    assent(c);
    const p = c.importFile();
    http.expectOne(root + '/import').flush({}, { status: 503, statusText: 'Unknown' });
    await p;
    expect(c.uncertain()).toBe(true);
    await c.importFile();
    http.expectNone(root + '/import');
    c.acknowledge();
    expect(c.uncertain()).toBe(true);
    const read = c.reconcile();
    http.expectOne(root + '/reconcile').flush({ value: persisted });
    await read;
    expect(c.uncertain()).toBe(true);
    c.acknowledge();
    expect(c.uncertain()).toBe(false);
    expect(c.model().reviewed).toBe(false);
  });
  it('treats malformed success as unknown and never follows it with an automatic write', async () => {
    const { c, http } = open();
    await pick(c, http);
    assent(c);
    const p = c.importFile();
    http.expectOne(root + '/import').flush({ value: { ...persisted, fileSha256: 'd'.repeat(64) } });
    await p;
    expect(c.uncertain()).toBe(true);
    await c.importFile();
    http.expectNone(root + '/import');
    const read = c.reconcile();
    http.expectOne(root + '/reconcile').flush({ value: { ...review, fileSha256: 'c'.repeat(64) } });
    await read;
    expect(c.review()).toBeNull();
    expect(c.reconciled()).toBe(false);
    expect(c.uncertain()).toBe(true);
  });
  it('recovers metadata with fresh assent and conservatively retains a pending submission fence', async () => {
    const first = open();
    await pick(first.c, first.http);
    assent(first.c);
    const p = first.c.importFile();
    first.http.expectOne(root + '/import').flush({}, { status: 503, statusText: 'Unknown' });
    await p;
    first.f.destroy();
    const second = open();
    await pick(second.c, second.http);
    expect(second.c.availableDraft()).toBe(true);
    expect(second.c.uncertain()).toBe(true);
    second.c.recover();
    expect(second.c.model().reviewed).toBe(false);
    await second.c.importFile();
    second.http.expectNone(root + '/import');
  });
  it('clears reviews and file references on revocation and refuses late context responses', async () => {
    const { f, c, http } = open();
    const p = c.pick({ target: { files: [file()], value: '' } } as unknown as Event);
    const req = http.expectOne(root + '/preview');
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    req.flush({ value: review });
    await p;
    expect(c.review()).toBeNull();
    expect(c.hasFile()).toBe(false);
    expect(c.model().reviewed).toBe(false);
    f.componentRef.setInput('engagementId', other);
    TestBed.tick();
    expect(c.review()).toBeNull();
  });
  it('withdraws rendered review when refreshing exact inputs without replaying commands', async () => {
    const { f, c, http } = open();
    await pick(c, http);
    f.detectChanges();
    const checkbox = f.nativeElement.querySelector('input[type=checkbox]') as HTMLInputElement;
    checkbox.click();
    TestBed.tick();
    f.detectChanges();
    expect(c.model().reviewed).toBe(true);
    const p = c.reconcile();
    http.expectOne(root + '/reconcile').flush({ value: { ...review, revision: 'c'.repeat(64) } });
    await p;
    TestBed.tick();
    f.detectChanges();
    expect(c.model().reviewed).toBe(false);
    expect(
      (f.nativeElement.querySelector('input[type=checkbox]') as HTMLInputElement).checked,
    ).toBe(false);
    await c.importFile();
    http.expectNone(root + '/import');
  });
});
