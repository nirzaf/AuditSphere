import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { MappingDraft } from './mapping-draft';

const id = '11111111-1111-4111-8111-111111111111',
  actor = '22222222-2222-4222-8222-222222222222',
  next = '33333333-3333-4333-8333-333333333333';
const root = `/api/ui/accounting/mappings/${id}`,
  editorUrl = root + '/editor?page=1&search=';
const allocation = {
  sourceAccountCode: '1000',
  destinationCode: 'CASH',
  statementSection: 'Assets',
  fraction: '1',
  rationale: 'Synthetic retained allocation',
  auditArea: null,
  residualPolicy: null,
};
const editor = {
  baseMappingId: id,
  clientId: id,
  engagementId: id,
  datasetId: id,
  baseVersion: 1,
  inputGeneration: 1,
  revision: 'a'.repeat(64),
  taxonomyVersion: 'synthetic-v1',
  chartVersionId: id,
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  sourceAccountCount: 2,
  filteredCount: 2,
  page: 1,
  pageSize: 25,
  accounts: [
    {
      accountCode: '1000',
      accountName: 'Synthetic cash',
      amount: '100',
      currency: 'USD',
      allocations: [allocation],
    },
    {
      accountCode: '2000',
      accountName: 'Synthetic liability',
      amount: '-100',
      currency: 'USD',
      allocations: [],
    },
  ],
  destinations: [
    { code: 'CASH', name: 'Cash', statementSection: 'Assets' },
    { code: 'OTHER', name: 'Other assets', statementSection: 'Assets' },
  ],
  canCreate: true,
  blocker: null,
};
const staged = {
  accountCode: '1000',
  splits: [
    {
      destinationCode: 'CASH',
      fraction: '0.5',
      rationale: 'Synthetic explicit split',
      auditArea: '',
    },
    {
      destinationCode: 'OTHER',
      fraction: '0.5',
      rationale: 'Synthetic explicit split',
      auditArea: '',
    },
  ],
};
function plan(requestId: string) {
  return {
    baseMappingId: id,
    requestId,
    baseRevision: editor.revision,
    revision: 'b'.repeat(64),
    requestHash: 'c'.repeat(64),
    changedAccountCount: 1,
    allocationCount: 3,
    changes: [
      {
        accountCode: '1000',
        before: [allocation],
        after: staged.splits.map((s) => ({ ...allocation, ...s })),
      },
    ],
    canCreate: true,
    blocker: null,
  };
}
function receipt(requestId: string) {
  return {
    baseMappingId: id,
    requestId,
    mappingId: next,
    version: 2,
    status: 'DRAFT',
    requestHash: 'c'.repeat(64),
    createdBy: actor,
    createdAt: '2026-10-02T22:00:00Z',
  };
}

describe('Reviewed new mapping version workflow', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [MappingDraft],
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
      userId: actor,
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
  async function open(data = editor) {
    const f = TestBed.createComponent(MappingDraft);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(editorUrl).flush(data);
    await f.whenStable();
    TestBed.tick();
    return { f, c: f.componentInstance, http };
  }
  async function prepare(c: MappingDraft, http: HttpTestingController) {
    c.changes.set(structuredClone([staged]));
    const task = c.reviewBatch(),
      read = http.expectOne(root + '/draft-preview');
    const requestId = read.request.body.requestId as string;
    read.flush({ value: plan(requestId) });
    await task;
    TestBed.tick();
    return requestId;
  }
  function assent(c: MappingDraft) {
    c.reviewModel.set({ reviewed: true });
    c.assent({ target: { checked: true } } as unknown as Event);
  }
  it('keeps exact splits and undo outside paged DOM and requires an explicit paste preview', async () => {
    const { f, c, http } = await open();
    c.edit('1000');
    c.active.set({ ...staged, splits: [{ ...staged.splits[0], fraction: '0.1234567' }] });
    c.stage();
    expect(c.changes()).toEqual([]);
    c.active.set(structuredClone(staged));
    c.stage();
    expect(c.changes()).toEqual([staged]);
    expect(c.active().accountCode).toBe('');
    c.undo();
    expect(c.changes()).toEqual([]);
    c.paste.set({ text: '1000\tCASH\t1\tSynthetic pasted allocation' });
    c.previewPaste();
    expect(c.changes()).toEqual([]);
    expect(c.localBatch().length).toBe(1);
    c.stageBatch();
    expect(c.changes()[0].accountCode).toBe('1000');
    expect(c.paste().text).toBe('');
    c.filterModel.set({ search: 'cash' });
    c.filter();
    TestBed.tick();
    http
      .expectOne(root + '/editor?page=1&search=cash')
      .flush({ ...editor, filteredCount: 1, accounts: [editor.accounts[0]] });
    await f.whenStable();
    TestBed.tick();
    expect(c.changes().length).toBe(1);
    f.destroy();
  });
  it('creates once with fresh exact-preview assent and leaves no review in tab checkpoints', async () => {
    const { f, c, http } = await open();
    const requestId = await prepare(c, http);
    await c.create();
    http.expectNone(root + '/draft');
    assent(c);
    const task = c.create(),
      write = http.expectOne(root + '/draft');
    expect(write.request.body.intent.requestId).toBe(requestId);
    expect(write.request.body.reviewed).toBe(true);
    for (let i = 0; i < sessionStorage.length; i++)
      expect(sessionStorage.getItem(sessionStorage.key(i)!)!).not.toContain('reviewed');
    expect(c.reviewModel().reviewed).toBe(false);
    await c.create();
    http.expectNone(root + '/draft');
    write.flush({ value: receipt(requestId) });
    await task;
    http.expectOne(editorUrl).flush({ ...editor, revision: 'd'.repeat(64) });
    await f.whenStable();
    TestBed.tick();
    expect(c.receipt()?.mappingId).toBe(next);
    expect(c.uncertain()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    expect(c.changes()).toEqual([]);
    expect(c.stale()).toBe(false);
    f.destroy();
  });
  it('recovers a lost accepted response after reload with a changed base by receipt identity only', async () => {
    const first = await open(),
      requestId = await prepare(first.c, first.http);
    assent(first.c);
    const task = first.c.create();
    first.http.expectOne(root + '/draft').flush({}, { status: 503, statusText: 'Unknown' });
    await task;
    first.http.expectOne(editorUrl).flush({ ...editor, revision: 'd'.repeat(64) });
    await first.f.whenStable();
    TestBed.tick();
    expect(first.c.uncertain()).toBe(true);
    expect(await first.c.confirmNavigation()).toBe(false);
    first.f.destroy();
    const { f, c, http } = await open({ ...editor, revision: 'd'.repeat(64) });
    expect(c.uncertain()).toBe(true);
    expect(c.changes()).toEqual([]);
    const read = c.readReceipt();
    http.expectOne(root + `/draft-receipts/${requestId}`).flush(receipt(requestId));
    await read;
    expect(c.receipt()?.mappingId).toBe(next);
    c.acknowledgeReceipt();
    expect(c.uncertain()).toBe(false);
    expect(await c.confirmNavigation()).toBe(true);
    http.expectNone(root + '/draft');
    f.destroy();
  });
  it('allows an explicit fresh review of only the same pending request when no receipt is yet retained', async () => {
    const { f, c, http } = await open(),
      requestId = await prepare(c, http);
    assent(c);
    const first = c.create(),
      write = http.expectOne(root + '/draft'),
      original = write.request.body;
    write.flush({}, { status: 503, statusText: 'Unknown' });
    await first;
    http.expectOne(editorUrl).flush(editor);
    await f.whenStable();
    TestBed.tick();
    const read = c.readReceipt();
    http.expectOne(root + `/draft-receipts/${requestId}`).flush(null);
    await read;
    c.acknowledgeReceipt();
    expect(c.uncertain()).toBe(true);
    const review = c.reviewSameRequest(),
      preview = http.expectOne(root + '/draft-preview');
    expect(preview.request.body.requestId).toBe(requestId);
    preview.flush({ value: plan(requestId) });
    await review;
    TestBed.tick();
    expect(c.retryExact()).toBe(true);
    assent(c);
    const retry = c.create(),
      second = http.expectOne(root + '/draft');
    expect(second.request.body).toEqual(original);
    second.flush({ value: receipt(requestId) });
    await retry;
    http.expectOne(editorUrl).flush({ ...editor, revision: 'd'.repeat(64) });
    await f.whenStable();
    TestBed.tick();
    expect(c.uncertain()).toBe(false);
    f.destroy();
  });
  it('retains fields on a stale write and clears all protected values when the session changes', async () => {
    const { f, c, http } = await open();
    await prepare(c, http);
    assent(c);
    const task = c.create();
    http
      .expectOne(root + '/draft')
      .flush(
        { code: 'revision.stale', message: 'Changed' },
        { status: 409, statusText: 'Changed' },
      );
    await task;
    http.expectOne(editorUrl).flush({ ...editor, revision: 'd'.repeat(64) });
    await f.whenStable();
    TestBed.tick();
    expect(c.changes()).toEqual([staged]);
    expect(c.stale()).toBe(true);
    expect(c.reviewModel().reviewed).toBe(false);
    c.reviewCurrentBase();
    expect(c.stale()).toBe(false);
    expect(c.preview()).toBeNull();
    TestBed.inject(SessionService).current.set(null);
    TestBed.inject(SessionService).invalidation.update((n) => n + 1);
    TestBed.tick();
    expect(c.editor.data()).toBeNull();
    expect(c.changes()).toEqual([]);
    expect(c.receipt()).toBeNull();
    expect(c.message()).toBe('');
    f.destroy();
  });
  it('requires safe checkpoint storage and rejects a wrong-context creation receipt', async () => {
    const { f, c, http } = await open(),
      requestId = await prepare(c, http);
    assent(c);
    const broken = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Full');
    });
    await c.create();
    http.expectNone(root + '/draft');
    expect(c.message()).toContain('No mapping creation was sent');
    broken.mockRestore();
    const task = c.create();
    http.expectOne(root + '/draft').flush({ value: { ...receipt(requestId), requestId: id } });
    await task;
    expect(c.uncertain()).toBe(true);
    expect(c.receipt()).toBeNull();
    expect(c.canCreate()).toBe(false);
    f.destroy();
  });
  it('preserves the pending receipt fence when clearing editable storage succeeds but clearing metadata fails', async () => {
    const first = await open(),
      requestId = await prepare(first.c, first.http);
    assent(first.c);
    const remove = Storage.prototype.removeItem;
    const denied = vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(function (
      this: Storage,
      key: string,
    ) {
      if (key.endsWith('/pending')) throw new Error('Unavailable');
      remove.call(this, key);
    });
    const task = first.c.create();
    first.http.expectOne(root + '/draft').flush({ value: receipt(requestId) });
    await task;
    expect(first.c.uncertain()).toBe(true);
    expect(sessionStorage.length).toBe(1);
    denied.mockRestore();
    first.f.destroy();
    const { f, c, http } = await open({ ...editor, revision: 'd'.repeat(64) });
    expect(c.uncertain()).toBe(true);
    const read = c.readReceipt();
    http.expectOne(root + `/draft-receipts/${requestId}`).flush(receipt(requestId));
    await read;
    c.acknowledgeReceipt();
    expect(c.uncertain()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    f.destroy();
  });
  it('recovers partial active, selection, batch and paste fields only after an explicit fresh-base recovery', async () => {
    const first = await open();
    first.c.edit('1000');
    first.c.active.update((a) => ({
      ...a,
      splits: [{ ...a.splits[0], fraction: '', rationale: '' }],
    }));
    first.c.selected.set(['3000']);
    first.c.batch.set({
      destinationCode: 'CASH',
      rationale: 'Synthetic batch draft',
      auditArea: '',
    });
    first.c.paste.set({ text: '3000\tCASH\t1\tSynthetic paste draft' });
    expect(first.c.saveDraft()).toBe(true);
    first.f.destroy();
    const { f, c, http } = await open();
    expect(c.active().accountCode).toBe('');
    expect(c.paste().text).toBe('');
    c.recoverDraft();
    expect(c.active().splits[0].fraction).toBe('');
    expect(c.selected()).toEqual(['3000']);
    expect(c.batch().rationale).toBe('Synthetic batch draft');
    expect(c.paste().text).toContain('Synthetic paste draft');
    expect(c.preview()).toBeNull();
    expect(c.reviewModel().reviewed).toBe(false);
    http.expectNone(root + '/draft');
    f.destroy();
  });
  it('keeps normal form typing shortcuts local and provides arrow navigation only on account edit buttons', async () => {
    const { f, c } = await open();
    f.detectChanges();
    const buttons = [
      ...(f.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
        '[data-account-edit]',
      ),
    ];
    buttons[0].focus();
    buttons[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(buttons[1]);
    buttons[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'F2', bubbles: true }));
    expect(c.active().accountCode).toBe('1000');
    c.active.update((a) => ({
      ...a,
      splits: [{ ...a.splits[0], rationale: 'Unsaved temporary rationale' }],
    }));
    c.cancelEdit();
    expect(c.changes()).toEqual([]);
    f.destroy();
  });
});
