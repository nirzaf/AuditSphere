import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { GeneralLedgerUpload } from './gl-upload';
import { decodeGlUpload, glUploadCheckpoint } from './gl-upload-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222',
  root = `/api/ui/engagements/${id}/general-ledger`;
const review = {
  clientId: id,
  engagementId: id,
  periodId: id,
  periodCode: 'FY26',
  bookId: id,
  bookCode: 'STAT',
  entity: 'SYN-CSV',
  currency: 'QAR',
  fileSha256: 'f'.repeat(64),
  revision: 'a'.repeat(64),
  journalCount: 1,
  lineCount: 1,
  debit: '100.123456',
  credit: '100.123456',
  sample: [
    {
      journalId: 'J-1',
      lineId: 'L-1',
      postingDate: '2026-06-30',
      accountCode: '1000',
      debit: '100.123456',
      credit: '0',
      originalCurrency: 'QAR',
      originalAmount: '100.123456',
      functionalAmount: '100.123456',
    },
  ],
  importBatchId: null,
  importState: null,
  sourceHash: null,
  canImport: true,
  blocker: null,
};
const persisted = {
  ...review,
  importBatchId: other,
  importState: 'SEALED',
  sourceHash: 'b'.repeat(64),
  canImport: false,
  blocker: 'Already retained.',
};
describe('Native exact GL upload', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [GeneralLedgerUpload],
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
    sessionStorage.clear();
    vi.restoreAllMocks();
  });
  async function open() {
    const f = TestBed.createComponent(GeneralLedgerUpload);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne(root + '?page=1')
      .flush({ clientId: id, engagementId: id, items: [], totalCount: 0, page: 1, pageSize: 20 });
    await f.whenStable();
    TestBed.tick();
    await new Promise((r) => setTimeout(r, 10));
    return { f, c: f.componentInstance, http };
  }
  async function pick(
    c: GeneralLedgerUpload,
    http: HttpTestingController,
    response: unknown = review,
    action = 'preview',
  ) {
    const task = c.pick({
      target: { files: [new File(['Synthetic GL input'], 'source.csv')], value: '' },
    } as unknown as Event);
    http.expectOne(root + '/' + action).flush({ value: response });
    await task;
    TestBed.tick();
  }
  function assent(c: GeneralLedgerUpload) {
    c.model.set({ reviewed: true });
    c.assent({ target: { checked: true } } as unknown as Event);
  }
  it('requires fresh review, sends once, retains the exact receipt and excludes files and assent from storage', async () => {
    const { f, c, http } = await open();
    await pick(c, http);
    await c.importFile();
    http.expectNone(root + '/import');
    assent(c);
    const task = c.importFile();
    const write = http.expectOne(root + '/import');
    expect(write.request.body.get('revision')).toBe(review.revision);
    expect(write.request.body.get('fileSha256')).toBe(review.fileSha256);
    expect(sessionStorage.length).toBe(1);
    const checkpoint = sessionStorage.getItem(sessionStorage.key(0)!)!;
    expect(checkpoint).not.toContain('100.123456');
    expect(checkpoint).not.toContain('reviewed');
    write.flush({ value: persisted });
    await task;
    expect(c.review()?.importBatchId).toBe(other);
    expect(c.canImport()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    await c.importFile();
    http.expectNone(root + '/import');
    f.destroy();
  });
  it('recovers an unknown import across reload with the exact file and manual persisted-state acknowledgement', async () => {
    const first = await open();
    await pick(first.c, first.http);
    assent(first.c);
    const task = first.c.importFile();
    first.http.expectOne(root + '/import').flush({}, { status: 503, statusText: 'Unknown' });
    await task;
    expect(first.c.uncertain()).toBe(true);
    first.f.destroy();
    const { f, c, http } = await open();
    expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    await pick(c, http, persisted, 'reconcile');
    expect(c.uncertain()).toBe(true);
    c.acknowledge();
    expect(c.uncertain()).toBe(false);
    expect(c.model().reviewed).toBe(false);
    http.expectNone(root + '/import');
    f.destroy();
  });
  it('clears failed or foreign reads and protected content on session invalidation', async () => {
    const { f, c, http } = await open();
    await pick(c, http);
    const read = c.reconcile();
    expect(c.review()).toBeNull();
    http.expectOne(root + '/reconcile').flush({ value: { ...review, clientId: other } });
    await read;
    expect(c.review()).toBeNull();
    expect(c.message()).toContain('mismatched');
    TestBed.inject(SessionService).current.set(null);
    TestBed.inject(SessionService).invalidation.update((x) => x + 1);
    TestBed.tick();
    expect(c.hasFile()).toBe(false);
    expect(c.fileName()).toBe('');
    expect(c.review()).toBeNull();
    f.destroy();
  });
  it('refuses writes if checkpoint storage fails and treats malformed success as unknown', async () => {
    const { f, c, http } = await open();
    await pick(c, http);
    assent(c);
    const broken = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Full');
    });
    await c.importFile();
    http.expectNone(root + '/import');
    expect(c.message()).toContain('unavailable');
    broken.mockRestore();
    const task = c.importFile();
    http.expectOne(root + '/import').flush({ value: { ...persisted, fileSha256: 'c'.repeat(64) } });
    await task;
    expect(c.uncertain()).toBe(true);
    expect(c.canImport()).toBe(false);
    f.destroy();
  });
  it('allows replacing an invalid file and correcting bytes after a definitive atomic refusal', async () => {
    const { f, c, http } = await open();
    const failed = c.pick({
      target: { files: [new File(['invalid'], 'source.csv')], value: '' },
    } as unknown as Event);
    http
      .expectOne(root + '/preview')
      .flush({ message: 'Malformed file' }, { status: 400, statusText: 'Refused' });
    await failed;
    await pick(c, http);
    assent(c);
    const write = c.importFile();
    http
      .expectOne(root + '/import')
      .flush({ message: 'Context changed' }, { status: 409, statusText: 'Changed' });
    await write;
    expect(sessionStorage.length).toBe(0);
    await pick(c, http, { ...review, fileSha256: 'c'.repeat(64) });
    expect(c.review()?.fileSha256).toBe('c'.repeat(64));
    expect(c.model().reviewed).toBe(false);
    http.expectNone(root + '/import');
    f.destroy();
  });
  it('bounds the runtime and metadata contracts without interpreting money as numbers', () => {
    expect(decodeGlUpload(review).debit).toBe('100.123456');
    expect(() => decodeGlUpload({ ...review, sample: Array(21).fill(review.sample[0]) })).toThrow();
    expect(() => decodeGlUpload({ ...review, debit: 100.123456 })).toThrow();
    expect(() => decodeGlUpload({ ...persisted, canImport: true })).toThrow();
    const metadata = { fileName: 'source.csv', fileSha256: 'f'.repeat(64), byteCount: 20 };
    expect(glUploadCheckpoint(metadata)).toEqual(metadata);
    expect(glUploadCheckpoint({ ...metadata, reviewed: true })).toBeNull();
    expect(glUploadCheckpoint({ ...metadata, byteCount: 10000001 })).toBeNull();
  });
});
