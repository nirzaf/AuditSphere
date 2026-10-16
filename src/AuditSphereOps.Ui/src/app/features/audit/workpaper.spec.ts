import { describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { BehaviorSubject, Subject } from 'rxjs';
import { decodeWorkpaper, WorkpaperEditor } from './workpaper';
import { SessionService } from '../../core/session';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';

describe('Audit Workpaper Contracts', () => {
  it('decodes a valid workpaper payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      index: 'C.01',
      title: 'Cash and Bank Testing',
      objective: 'Verify existence and completeness of bank balances at year end.',
      templateVersion: '1.0',
      procedure: 'Perform bank reconciliation testing for all open bank accounts.',
      linkedProcedureTitle: 'Bank reconciliations',
      revision: 1,
      status: 'WORKING',
      workPerformed: 'Tested reconciliation between GL and bank statements.',
      conclusion: 'Reconciliation is complete and free of material difference.',
      createdAt: '2026-10-01T08:00:00Z',
      submittedAt: null,
      submissions: [
        {
          revision: 1,
          submittedAt: '2026-10-01T12:00:00Z',
          conclusion: 'Initial review conclusion.',
        },
      ],
      draft: {
        workpaperId: id1,
        draftId: id3,
        baseWorkpaperRevision: 1,
        baseInputGeneration: 1,
        basePolicyGeneration: 1,
        draftRevision: 2,
        workPerformed: 'Tested reconciliation between GL and bank statements.',
        conclusion: 'Reconciliation is complete and free of material difference.',
        lastSaveId: id2,
        lastSavedAt: '2026-10-01T11:45:00Z',
        lifecycle: 'ACTIVE',
      },
    };

    const decoded = decodeWorkpaper(raw, 'workpaper');
    expect(decoded.id).toBe(id1);
    expect(decoded.engagementId).toBe(id2);
    expect(decoded.index).toBe('C.01');
    expect(decoded.status).toBe('WORKING');
    expect(decoded.submissions.length).toBe(1);
    expect(decoded.draft.draftRevision).toBe('2');
    expect(decoded.draft.lifecycle).toBe('ACTIVE');
  });

  it('rejects invalid workpaper payloads', () => {
    expect(() => decodeWorkpaper(null, 'workpaper')).toThrow();
    expect(() => decodeWorkpaper({}, 'workpaper')).toThrow();
  });
});

const readUrl = `/api/ui/audit/workpapers/${id1}`;
const base = readUrl + '/commands';
import { commandPreview, commandReceipt, commandState, detail, requestHash, snapshot } from './workpaper-command.spec-support';
import { WorkpaperCommandReview } from './workpaper-command-review';
import { decodeWorkpaperCommandFields } from './workpaper-command-contracts';

describe('Workpaper authored-content and receipt protection', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let decisions: Subject<boolean | string | undefined>;
  const dialogSpy = { open: vi.fn(() => {
    decisions = new Subject<boolean | string | undefined>();
    return { afterClosed: () => decisions.asObservable(), close: (value: boolean) => decisions.next(value) };
  }) };
  const settle = async () => { for (let i = 0; i < 8; i++) await Promise.resolve(); };
  beforeEach(() => {
    sessionStorage.clear(); dialogSpy.open.mockClear();
    params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({ imports: [WorkpaperEditor], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: MatDialog, useValue: dialogSpy }, { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id1, firmId: id1, generation: '1', staff: true });
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); sessionStorage.clear(); vi.useRealTimers(); });
  function open() {
    const f = TestBed.createComponent(WorkpaperEditor); f.detectChanges(); http.expectOne(readUrl).flush(detail()); f.detectChanges(); TestBed.tick(); return f;
  }
  async function prepareSave() {
    http.expectOne(base + '/state').flush(commandState()); await settle();
    const prepare = http.expectOne(base + '/prepare-save');
    expect(prepare.request.body.reviewed).toBe(false);
    prepare.flush({ value: { requestId: prepare.request.body.requestId, requestHash,
      canonicalInput: decodeWorkpaperCommandFields(prepare.request.body.fields, 'test') } }); await settle();
    return http.expectOne(base + '/actions');
  }
  async function acknowledgeSave() {
    const action = await prepareSave();
    expect(action.request.body.expectedRequestHash).toBe(requestHash);
    action.flush({ value: commandReceipt(action.request.body) }); await settle();
  }
  async function prepareFinal() {
    http.expectOne(base + '/state').flush(commandState()); await settle();
    const preview = http.expectOne(base + '/preview'); preview.flush({ value: commandPreview(preview.request.body) }); await settle();
  }
  async function confirmFinal(c: WorkpaperEditor) {
    c.flow.reviewed.set(true); const result = c.flow.execute(); const action = http.expectOne(base + '/actions');
    expect(action.request.body.reviewed).toBe(true); action.flush({ value: commandReceipt(action.request.body) });
    expect(await result).not.toBeNull(); decisions.next(true); await settle();
  }
  it('offers stay or acknowledged save inside the debounce and does not dispatch while staying', async () => {
    const c = open().componentInstance; c.work = 'Typed before navigation.'; c.changed();
    const stay = c.confirmNavigation(); decisions.next('keep'); expect(await stay).toBe(false);
    http.expectNone(base + '/actions'); const nav = c.confirmNavigation(); decisions.next('save'); await settle();
    await acknowledgeSave(); expect(await nav).toBe(true);
  });
  it('waits for an in-flight save without duplicate dispatch and preserves newer typing', async () => {
    const c = open().componentInstance; c.work = 'Original typed snapshot.'; c.changed();
    const save = c.save(), duplicate = c.save(), nav = c.confirmNavigation();
    const action = await prepareSave(); c.work = 'Newer typed snapshot.'; c.changed();
    action.flush({ value: commandReceipt(action.request.body) });
    expect(await save).toBe(false); expect(await duplicate).toBe(false); await settle();
    expect(c.work).toBe('Newer typed snapshot.'); expect(c.state?.workPerformed).toBe('Original typed snapshot.');
    expect(c.state?.draftRevision).toBe('3'); decisions.next('keep'); expect(await nav).toBe(false);
    http.expectNone(base + '/actions');
  });
  it('rearms exactly one debounced autosave of newer edits after an in-flight save and Stay', async () => {
    vi.useFakeTimers(); const c = open().componentInstance;
    c.work = 'Original pending save.'; c.changed(); const saving = c.save();
    const firstAction = await prepareSave(), firstReceipt = commandReceipt(firstAction.request.body);
    c.work = 'Newer work preserved while saving.'; c.conclusion = 'Newer conclusion preserved while saving.'; c.changed();
    const navigation = c.confirmNavigation();
    firstAction.flush({ value: firstReceipt }); expect(await saving).toBe(false); await settle();
    expect(dialogSpy.open).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1000); http.expectNone(base + '/state'); http.expectNone(r => r.method === 'POST');
    decisions.next('keep'); expect(await navigation).toBe(false);
    expect(c.work).toBe('Newer work preserved while saving.'); expect(c.state?.draftRevision).toBe('3');
    await vi.advanceTimersByTimeAsync(749); http.expectNone(base + '/state'); http.expectNone(r => r.method === 'POST');
    await vi.advanceTimersByTimeAsync(1);
    const current = firstReceipt.commandEvent.after;
    http.expectOne(base + '/state').flush(commandState(current)); await settle();
    const prepare = http.expectOne(base + '/prepare-save');
    expect(prepare.request.body.requestId).not.toBe(firstAction.request.body.requestId);
    expect(prepare.request.body.fields.save).toEqual({ expectedDraftRevision: '3', baseWorkpaperRevision: '1',
      baseInputGeneration: '1', basePolicyGeneration: '1', workPerformed: 'Newer work preserved while saving.', conclusion: 'Newer conclusion preserved while saving.' });
    prepare.flush({ value: { requestId: prepare.request.body.requestId, requestHash,
      canonicalInput: decodeWorkpaperCommandFields(prepare.request.body.fields, 'test') } }); await settle();
    const secondAction = http.expectOne(base + '/actions');
    expect(secondAction.request.body.fields).toEqual(prepare.request.body.fields);
    secondAction.flush({ value: commandReceipt(secondAction.request.body, current) }); await settle();
    expect(c.state?.draftRevision).toBe('4'); expect(c.state?.workPerformed).toBe(c.work); expect(c.state?.conclusion).toBe(c.conclusion);
    await vi.advanceTimersByTimeAsync(10000); http.expectNone(base + '/state'); http.expectNone(r => r.method === 'POST');
  });
  it('blocks navigation on a known refused save, and permits a corrected save after a non-stale refusal', async () => {
    const c = open().componentInstance; c.work = 'Invalid content.'; c.changed();
    const nav = c.confirmNavigation(); decisions.next('save'); await settle(); const action = await prepareSave();
    action.flush({ code: 'request.invalid', message: 'The draft save was refused.' }, { status: 400, statusText: 'Bad Request' });
    expect(await nav).toBe(false); expect(c.flow.failed()).toBe(true); expect(c.uncertain()).toBe(false);
    c.work = 'Corrected content.'; c.changed(); const saved = c.save(); await acknowledgeSave();
    expect(await saved).toBe(true); expect(c.flow.failed()).toBe(false);
  });
  it('does not turn Stay after a refused save into an automatic retry', async () => {
    vi.useFakeTimers(); const c = open().componentInstance;
    c.work = 'Refused local content.'; c.changed(); const saving = c.save();
    const action = await prepareSave(); action.flush({ code: 'request.invalid', message: 'Correct this draft first.' }, { status: 400, statusText: 'Bad Request' });
    expect(await saving).toBe(false); expect(c.flow.failed()).toBe(true);
    const navigation = c.confirmNavigation(); decisions.next('keep'); expect(await navigation).toBe(false);
    await vi.advanceTimersByTimeAsync(10000); http.expectNone(base + '/state'); http.expectNone(r => r.method === 'POST');
    expect(c.work).toBe('Refused local content.');
  });
  it('does not let reload clear uncertainty; checkpoints contain only request reference and action scope', async () => {
    const f = open(), c = f.componentInstance; c.work = 'Private unsaved financial narrative.'; c.changed(); const save = c.save();
    const action = await prepareSave(); action.error(new ProgressEvent('error')); expect(await save).toBe(false);
    expect(c.uncertain()).toBe(true); const raw = sessionStorage.getItem(sessionStorage.key(0)!);
    expect(raw).toContain('SAVE_DRAFT'); expect(raw).not.toContain(c.work); expect(raw).not.toContain('reviewed');
    expect(Object.keys(JSON.parse(raw!).value).sort()).toEqual(['requestHash', 'requestId']);
    await c.save(); await c.submit(); await c.discard(); http.expectNone(base + '/actions');
    c.reload(); http.expectOne(readUrl).flush(detail()); f.detectChanges(); TestBed.tick(); expect(c.uncertain()).toBe(true);
  });
  it('recovers the original save receipt without POST and re-reads current content rather than rolling it back', async () => {
    const f = open(), c = f.componentInstance; c.work = 'Original saved intent.'; c.changed(); const saving = c.save();
    const action = await prepareSave(), original = commandReceipt(action.request.body); action.error(new ProgressEvent('error')); await saving;
    const recovery = c.checkSaveOutcome(); const current = snapshot(); current.draft = { ...current.draft!, draftRevision: '9', workPerformed: 'Later saved content.' };
    http.expectOne(base + '/state').flush(commandState(current)); await settle();
    http.expectOne(base + '/receipts/' + original.requestId + '?requestHash=' + requestHash).flush({ found: true, receipt: original });
    await recovery; expect(c.flow.receipt()?.commandEvent.after.draft?.workPerformed).toBe('Original saved intent.');
    http.expectNone(r => r.method === 'POST');
    http.expectOne(readUrl).flush({ ...detail(), draft: { ...detail().draft, draftRevision: 9, workPerformed: 'Later saved content.' } });
    f.detectChanges(); TestBed.tick(); expect(c.state?.draftRevision).toBe('9'); expect(c.work).toBe('Later saved content.');
  });
  it('shows newer local text after original save recovery finds a later submission and permits local-only abandonment', async () => {
    vi.useFakeTimers(); const f = open(), c = f.componentInstance;
    c.work = 'Original saved intent.'; c.changed(); const saving = c.save();
    const action = await prepareSave(), original = commandReceipt(action.request.body);
    c.work = 'Newer local work not in the submission.'; c.conclusion = 'Newer local conclusion not in the submission.'; c.changed();
    action.error(new ProgressEvent('error')); expect(await saving).toBe(false);
    expect(c.abandonLocalEdits()).toBe(false); expect(c.uncertain()).toBe(true);
    const current = { ...original.commandEvent.after, revision: '2', status: 'SUBMITTED_SNAPSHOT' as const,
      workPerformed: 'Later frozen work.', conclusion: 'Later frozen conclusion.', submittedAt: detail().createdAt,
      draft: { ...original.commandEvent.after.draft!, draftRevision: '4', workPerformed: 'Later frozen work.',
        conclusion: 'Later frozen conclusion.', lastSaveId: id3, lifecycle: 'CONSUMED' as const } };
    const currentDetail = { ...detail(), revision: '2', status: 'SUBMITTED_SNAPSHOT', workPerformed: current.workPerformed,
      conclusion: current.conclusion, submittedAt: current.submittedAt,
      submissions: [{ revision: '2', submittedAt: current.submittedAt, conclusion: current.conclusion }],
      draft: { ...detail().draft, draftId: null, baseWorkpaperRevision: '2', draftRevision: '0', workPerformed: current.workPerformed,
        conclusion: current.conclusion, lastSaveId: '00000000-0000-0000-0000-000000000000', lastSavedAt: null, lifecycle: 'CONSUMED' } };
    c.reload(); http.expectOne(readUrl).flush(currentDetail); f.detectChanges(); TestBed.tick();
    expect(c.wp.data()?.status).toBe('SUBMITTED_SNAPSHOT'); expect(c.uncertain()).toBe(true);
    expect(c.abandonLocalEdits()).toBe(false); expect(c.flow.pending()).toHaveLength(1);
    expect(c.work).toBe('Newer local work not in the submission.');
    const recovery = c.checkSaveOutcome();
    http.expectOne(base + '/state').flush({ ...commandState(current), canSaveDraft: false, canSubmit: false,
      canDiscardDraft: false, blockers: ['The submitted workpaper is immutable.'] }); await settle();
    http.expectOne(base + '/receipts/' + original.requestId + '?requestHash=' + requestHash).flush({ found: true, receipt: original });
    await recovery; http.expectNone(r => r.method === 'POST');
    http.expectOne(readUrl).flush(currentDetail);
    f.detectChanges(); TestBed.tick();
    expect(c.uncertain()).toBe(false); expect(c.wp.data()?.revision).toBe('2'); expect(c.state?.draftRevision).toBe('0');
    expect(c.flow.receipt()?.commandEvent.after.draft?.workPerformed).toBe('Original saved intent.');
    expect(c.wp.data()?.workPerformed).toBe('Later frozen work.');
    const preserved = f.nativeElement.querySelector('section[aria-label="Preserved local workpaper edits"]') as HTMLElement;
    expect(preserved).not.toBeNull();
    const textareas = preserved.querySelectorAll('textarea');
    expect(textareas[0].value).toBe('Newer local work not in the submission.');
    expect(textareas[1].value).toBe('Newer local conclusion not in the submission.');
    for (const field of textareas) { expect(field.readOnly).toBe(true); expect(field.disabled).toBe(false); }
    expect(preserved.textContent).toContain('No server draft is discarded and no submission is changed.');
    expect(await c.confirmNavigation()).toBe(false); expect(dialogSpy.open).not.toHaveBeenCalled();
    await c.save(); await c.submit(); await c.discard(); http.expectNone(r => r.method === 'POST');
    const head = c.wp.data(), receipt = c.flow.receipt();
    expect(c.abandonLocalEdits()).toBe(true); expect(c.hasPreservedLocalEdits()).toBe(false);
    expect(c.wp.data()).toBe(head); expect(c.flow.receipt()).toBe(receipt); expect(c.flow.pending()).toEqual([]);
    expect(c.wp.data()?.submissions).toHaveLength(1); expect(c.wp.data()?.workPerformed).toBe('Later frozen work.');
    expect(await c.confirmNavigation()).toBe(true); expect(dialogSpy.open).not.toHaveBeenCalled();
    const event = { preventDefault: vi.fn(), returnValue: undefined } as unknown as BeforeUnloadEvent;
    c.beforeUnload(event); expect(event.preventDefault).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(10000); http.expectNone(r => r.method === 'POST'); http.expectNone(base + '/state');
  });
  it('retains missing-receipt uncertainty across reload and requires exact same ID/hash preparation for retry', async () => {
    let f = open(), c = f.componentInstance; c.work = 'Original intent.'; c.changed(); const saving = c.save();
    const action = await prepareSave(), original = action.request.body; action.error(new ProgressEvent('error')); await saving;
    f.destroy(); f = open(); c = f.componentInstance; expect(c.uncertain()).toBe(true);
    const recovery = c.checkSaveOutcome(); http.expectOne(base + '/state').flush(commandState()); await settle();
    http.expectOne(base + '/receipts/' + original.requestId + '?requestHash=' + requestHash).flush({ found: false, receipt: null }); await recovery;
    expect(c.uncertain()).toBe(true); http.expectNone(r => r.method === 'POST');
    c.work = 'Original intent.'; c.changed(); const retry = c.retryPending(); http.expectOne(base + '/state').flush(commandState()); await settle();
    const prepare = http.expectOne(base + '/prepare-save'); expect(prepare.request.body.requestId).toBe(original.requestId);
    prepare.flush({ value: { requestId: original.requestId, requestHash: 'c'.repeat(64), canonicalInput: decodeWorkpaperCommandFields(prepare.request.body.fields, 'test') } });
    await retry; http.expectNone(base + '/actions'); expect(c.uncertain()).toBe(true);
  });
  it('clears protected editor content after lookup denial while retaining the pending identity', async () => {
    const f = open(), c = f.componentInstance; c.work = 'Private pending content.'; c.changed(); const saving = c.save();
    const action = await prepareSave(); action.error(new ProgressEvent('error')); await saving;
    const recovery = c.checkSaveOutcome();
    http.expectOne(base + '/state').flush({ message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' }); await recovery;
    http.expectOne(readUrl).flush({}, { status: 403, statusText: 'Forbidden' }); f.detectChanges(); TestBed.tick();
    expect(c.wp.data()).toBeNull(); expect(c.state).toBeNull(); expect(c.work).toBe(''); expect(c.uncertain()).toBe(true);
    expect(sessionStorage.length).toBe(1); http.expectNone(base + '/actions');
  });
  it('requires a second exact-draft review and fresh assent for navigation discard, with no autosave of local edits', async () => {
    const c = open().componentInstance; c.work = 'Never save these edits.'; c.changed();
    const nav = c.confirmNavigation(); decisions.next('discard'); await settle(); await prepareFinal();
    expect(dialogSpy.open).toHaveBeenLastCalledWith(WorkpaperCommandReview, expect.anything());
    expect(c.flow.preview()?.before.draft?.workPerformed).toBe('Worked.');
    await c.flow.execute(); http.expectNone(base + '/actions'); // no assent
    await confirmFinal(c); expect(await nav).toBe(true); http.expectOne(readUrl).flush(detail());
  });
  it('keeps unknown discard fail closed and recovers its original receipt', async () => {
    const c = open().componentInstance; const discarding = c.discard(); await prepareFinal(); c.flow.reviewed.set(true);
    const executed = c.flow.execute(), action = http.expectOne(base + '/actions'), original = commandReceipt(action.request.body);
    action.error(new ProgressEvent('error')); await executed; decisions.next(false); expect(await discarding).toBe(false);
    expect(c.uncertain()).toBe(true); expect(await c.confirmNavigation()).toBe(false); await c.discard(); http.expectNone(base + '/actions');
    const recovery = c.checkSaveOutcome(); http.expectOne(base + '/state').flush(commandState()); await settle();
    http.expectOne(base + '/receipts/' + original.requestId + '?requestHash=' + requestHash).flush({ found: true, receipt: original }); await recovery;
    expect(c.uncertain()).toBe(false); http.expectOne(readUrl).flush(detail());
  });
  it('invalidates assent when fields change and sends no final command', async () => {
    const c = open().componentInstance; const submitting = c.submit(); await prepareFinal();
    c.flow.reviewed.set(true); c.work = 'Changed after preview.'; c.changed();
    expect(c.flow.reviewed()).toBe(false); await c.flow.execute(); http.expectNone(base + '/actions');
    decisions.next(false); await submitting;
  });
  it('protects beforeunload while dirty, saving or uncertain and never retries automatically', async () => {
    vi.useFakeTimers(); const c = open().componentInstance; c.work = 'Dirty.'; c.changed();
    const event = { preventDefault: vi.fn(), returnValue: undefined } as unknown as BeforeUnloadEvent;
    c.beforeUnload(event); expect(event.preventDefault).toHaveBeenCalled(); await vi.advanceTimersByTimeAsync(749); http.expectNone(base + '/state');
    await vi.advanceTimersByTimeAsync(1); const action = await prepareSave(); action.error(new ProgressEvent('error')); await settle();
    await vi.advanceTimersByTimeAsync(10000); http.expectNone(base + '/actions'); expect(c.uncertain()).toBe(true);
  });
  it('renders valid exact detail and draft revisions above the JavaScript integer range', () => {
    const f = TestBed.createComponent(WorkpaperEditor); f.detectChanges();
    const raw = detail();
    http.expectOne(readUrl).flush({ ...raw, revision: '9007199254740993', submissions: [{ revision: '9007199254740993', submittedAt: raw.createdAt, conclusion: 'Exact.' }],
      draft: { ...raw.draft, baseWorkpaperRevision: '9007199254740993', draftRevision: '9007199254740993' } });
    f.detectChanges(); TestBed.tick();
    expect(f.componentInstance.wp.data()?.revision).toBe('9007199254740993');
    expect(f.nativeElement.textContent).toContain('Current revision 9007199254740993');
    expect(f.componentInstance.state?.draftRevision).toBe('9007199254740993');
  });
  it.each(['success', 'refusal'] as const)('does not apply a late save %s to a reopened A-B-A editor', async outcome => {
    const f = open(), c = f.componentInstance; c.work = 'Old editor lifetime content.'; c.changed(); const saving = c.save();
    const action = await prepareSave(), receipt = commandReceipt(action.request.body);
    params.next(convertToParamMap({ id: id2 })); params.next(convertToParamMap({ id: id1 }));
    f.detectChanges(); TestBed.tick(); http.expectOne(readUrl).flush(detail()); f.detectChanges(); TestBed.tick();
    if (outcome === 'success') action.flush({ value: receipt });
    else action.flush({ code: 'revision.stale', message: 'Old editor refused.' }, { status: 409, statusText: 'Conflict' });
    expect(await saving).toBe(false); expect(c.state?.draftRevision).toBe('2'); expect(c.work).toBe('Worked.');
    expect(c.message()).not.toBe('Draft saved.'); expect(c.flow.receipt()).toBeNull(); expect(c.uncertain()).toBe(true);
  });
  it('refuses wrong returned root or nested draft identity', () => {
    expect(() => decodeWorkpaper({ ...detail(), draft: { ...detail().draft, workpaperId: id2 } }, 'detail')).toThrow();
    const f = TestBed.createComponent(WorkpaperEditor); f.detectChanges(); http.expectOne(readUrl).flush(detail(id2));
    f.detectChanges(); expect(f.componentInstance.wp.data()).toBeNull(); expect(f.componentInstance.wp.error()).toContain('Unsupported response');
  });
});
