import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { WorkpaperCommandState } from './workpaper-command-state';
import { decodeWorkpaperCommandFields, decodeWorkpaperCommandReceipt } from './workpaper-command-contracts';
import { basis, commandPreview, commandReceipt, commandState, draftId, engagementId, requestHash, requestId, saveFields, snapshot, workpaperId } from './workpaper-command.spec-support';

function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
@Component({ template: '' })
class Harness {
  readonly id = signal<string | null>(workpaperId);
  readonly source = signal({ id: workpaperId, engagementId, revision: '1' });
  readonly draft = signal({ draftId: draftId as string | null, draftRevision: '2', lastSaveId: engagementId,
    baseWorkpaperRevision: '1', baseInputGeneration: '1', basePolicyGeneration: '1' });
  readonly flow = new WorkpaperCommandState(this.id, this.source, this.draft);
}
const settle = async () => { for (let i = 0; i < 10; i++) await Promise.resolve(); };
describe('Workpaper command lifetime and original receipt fences', () => {
  const session = { current: signal({ userId: workpaperId, firmId: workpaperId, generation: '1', staff: true }), invalidation: signal(0) };
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  const get = vi.fn(), command = vi.fn();
  beforeEach(() => {
    sessionStorage.clear(); get.mockReset(); command.mockReset(); session.invalidation.set(0);
    session.current.set({ userId: workpaperId, firmId: workpaperId, generation: '1', staff: true });
    params = new BehaviorSubject(convertToParamMap({ id: workpaperId }));
    TestBed.configureTestingModule({ imports: [Harness], providers: [{ provide: Api, useValue: { get, command } },
      { provide: SessionService, useValue: session }, { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    get.mockResolvedValue(commandState());
  });
  afterEach(() => { TestBed.resetTestingModule(); sessionStorage.clear(); });
  function open() { const fixture = TestBed.createComponent(Harness); fixture.detectChanges(); TestBed.tick(); return fixture; }
  function prepareSave(action: Promise<unknown>) {
    command.mockImplementation((url: string, body: { requestId: string; fields: unknown }) => url.endsWith('/prepare-save')
      ? Promise.resolve({ ok: true, value: { requestId: body.requestId, requestHash, canonicalInput: decodeWorkpaperCommandFields(body.fields, 'test') } }) : action);
  }
  it.each(['success', 'refusal', 'unknown'] as const)('discards late save %s after A-B-A and retains the original checkpoint', async outcome => {
    const fixture = open(), c = fixture.componentInstance, result = deferred<unknown>(); prepareSave(result.promise);
    const saving = c.flow.save(saveFields); await settle();
    const sent = command.mock.calls.find(([url]) => url.endsWith('/actions'))![1];
    params.next(convertToParamMap({ id: engagementId })); params.next(convertToParamMap({ id: workpaperId }));
    result.resolve(outcome === 'success' ? { ok: true, value: commandReceipt(sent) } : { ok: false, unknown: outcome === 'unknown', status: 409, code: 'revision.stale', message: 'Old refusal' });
    expect(await saving).toBeNull(); expect(c.flow.receipt()).toBeNull(); expect(c.flow.message()).not.toContain('Old refusal');
    expect(c.flow.uncertain()).toBe(true); expect(sessionStorage.length).toBe(1);
  });
  it.each(['session', 'destroy', 'source', 'draft'] as const)('discards a late save acknowledgement after %s changes', async change => {
    const fixture = open(), c = fixture.componentInstance, result = deferred<unknown>(); prepareSave(result.promise);
    const saving = c.flow.save(saveFields); await settle(); const sent = command.mock.calls.find(([url]) => url.endsWith('/actions'))![1];
    if (change === 'session') session.current.set({ ...session.current(), generation: '2' });
    if (change === 'destroy') fixture.destroy();
    if (change === 'source') c.source.set({ ...c.source() });
    if (change === 'draft') c.draft.set({ ...c.draft(), draftId: requestId });
    result.resolve({ ok: true, value: commandReceipt(sent) }); expect(await saving).toBeNull(); expect(c.flow.receipt()).toBeNull();
  });
  it('does not let old finally clear a newer recovery operation busy flag', async () => {
    const fixture = open(), c = fixture.componentInstance, result = deferred<unknown>(); prepareSave(result.promise);
    const saving = c.flow.save(saveFields); await settle();
    params.next(convertToParamMap({ id: engagementId })); params.next(convertToParamMap({ id: workpaperId }));
    const lookup = deferred<unknown>(); get.mockImplementation((url: string) => url.endsWith('/state') ? Promise.resolve(commandState()) : lookup.promise);
    const recovery = c.flow.reconcile(); await settle(); expect(c.flow.busy()).toBe(true);
    result.resolve({ ok: false, unknown: true, status: 0, code: 'outcome.unknown', message: 'Lost' }); await saving;
    expect(c.flow.busy()).toBe(true); lookup.resolve({ found: false, receipt: null }); await recovery; expect(c.flow.busy()).toBe(false);
  });
  it('requires current capability and exact draft identity before preparing a save', async () => {
    const c = open().componentInstance; get.mockResolvedValue({ ...commandState(), canSaveDraft: false, blockers: ['Read-only scope.'] });
    expect(await c.flow.save(saveFields)).toBeNull(); expect(command).not.toHaveBeenCalled();
    get.mockResolvedValue(commandState({ ...snapshot(), draft: { ...snapshot().draft!, id: requestId } }));
    expect(await c.flow.save(saveFields)).toBeNull(); expect(command).not.toHaveBeenCalled();
  });
  it('does not dispatch when tab checkpoint persistence fails', async () => {
    const c = open().componentInstance; prepareSave(Promise.resolve({ ok: true }));
    const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Storage unavailable'); });
    try { expect(await c.flow.save(saveFields)).toBeNull(); expect(command.mock.calls.some(([url]) => url.endsWith('/actions'))).toBe(false); }
    finally { storage.mockRestore(); }
  });
  it('blocks duplicate command dispatch programmatically while pending or busy', async () => {
    const c = open().componentInstance, result = deferred<unknown>(); prepareSave(result.promise);
    const first = c.flow.save(saveFields); await settle(); await c.flow.save(saveFields);
    expect(command.mock.calls.filter(([url]) => url.endsWith('/actions'))).toHaveLength(1);
    result.resolve({ ok: false, unknown: true, code: 'outcome.unknown', status: 0, message: 'Lost' }); await first;
    await c.flow.save(saveFields); expect(command.mock.calls.filter(([url]) => url.endsWith('/actions'))).toHaveLength(1);
  });
  it.each(['actor', 'epoch', 'scope', 'request', 'hash', 'action'] as const)('rejects a recovered receipt with mismatched %s', async mismatch => {
    const c = open().componentInstance, result = deferred<unknown>(); prepareSave(result.promise);
    const saving = c.flow.save(saveFields); await settle(); const sent = command.mock.calls.find(([url]) => url.endsWith('/actions'))![1];
    result.resolve({ ok: false, unknown: true, code: 'outcome.unknown', status: 0, message: 'Lost' }); await saving;
    const receipt = commandReceipt(sent);
    if (mismatch === 'actor') { receipt.actorId = engagementId; receipt.commandEvent.actorId = engagementId; }
    if (mismatch === 'epoch') { receipt.actorEpoch = '2'; receipt.commandEvent.actorEpoch = '2'; }
    if (mismatch === 'scope') { receipt.commandEvent.before.clientId = engagementId; receipt.commandEvent.after.clientId = engagementId; }
    if (mismatch === 'request') { receipt.requestId = requestId; receipt.commandEvent.after.draft!.lastSaveId = requestId; }
    if (mismatch === 'hash') receipt.requestHash = 'c'.repeat(64);
    if (mismatch === 'action') receipt.action = 'DISCARD_DRAFT';
    get.mockImplementation((url: string) => url.endsWith('/state') ? Promise.resolve(commandState()) : Promise.resolve({ found: true, receipt }));
    expect(await c.flow.reconcile()).toBeNull(); expect(c.flow.uncertain()).toBe(true);
  });
  it('does not confuse a later current draft with the original durable save event', async () => {
    const c = open().componentInstance, result = deferred<unknown>(); prepareSave(result.promise);
    const saving = c.flow.save(saveFields); await settle(); const sent = command.mock.calls.find(([url]) => url.endsWith('/actions'))![1];
    result.resolve({ ok: false, unknown: true, code: 'outcome.unknown', status: 0, message: 'Lost' }); await saving;
    const original = commandReceipt(sent), current = snapshot(); current.draft = { ...current.draft!, draftRevision: '9', workPerformed: 'Superseded original.' };
    get.mockImplementation((url: string) => url.endsWith('/state') ? Promise.resolve(commandState(current)) : Promise.resolve({ found: true, receipt: original }));
    expect((await c.flow.reconcile())?.commandEvent.after.draft?.workPerformed).toBe('Updated work.');
    expect(command.mock.calls.filter(([url]) => url.endsWith('/actions'))).toHaveLength(1); expect(c.flow.uncertain()).toBe(false);
  });
  it.each(['success', 'refusal'] as const)('discards late final preview %s after A-B-A', async outcome => {
    const c = open().componentInstance, preview = deferred<unknown>(); command.mockReturnValue(preview.promise);
    const preparing = c.flow.prepare(() => ({ action: 'DISCARD_DRAFT', discard: { expectedDraftRevision: '2' } })); await settle();
    const sent = command.mock.calls[0][1]; params.next(convertToParamMap({ id: engagementId })); params.next(convertToParamMap({ id: workpaperId }));
    preview.resolve(outcome === 'success' ? { ok: true, value: commandPreview(sent) } : { ok: false, unknown: false, code: 'revision.stale', status: 409, message: 'Old refusal' });
    expect(await preparing).toBe(false); expect(c.flow.preview()).toBeNull(); expect(c.flow.reviewed()).toBe(false);
  });
  it('discards a pending preview after an input A-B-A change without fencing acknowledged autosaves', async () => {
    const c = open().componentInstance, result = deferred<unknown>(); command.mockReturnValue(result.promise);
    let fields = { action: 'DISCARD_DRAFT', discard: { expectedDraftRevision: '2' } };
    const preparing = c.flow.prepare(() => fields); await settle(); const sent = command.mock.calls[0][1];
    fields = { ...fields, discard: { expectedDraftRevision: '3' } }; c.flow.inputChanged();
    fields = { ...fields, discard: { expectedDraftRevision: '2' } }; c.flow.inputChanged();
    result.resolve({ ok: true, value: commandPreview(sent) });
    expect(await preparing).toBe(false); expect(c.flow.preview()).toBeNull(); expect(c.flow.reviewed()).toBe(false);
  });
  it.each(['success', 'refusal'] as const)('discards late final execution %s after A-B-A', async outcome => {
    const c = open().componentInstance;
    command.mockImplementation((_url: string, body: { requestId: string; fields: unknown }) => Promise.resolve({ ok: true, value: commandPreview(body) }));
    expect(await c.flow.prepare(() => ({ action: 'DISCARD_DRAFT', discard: { expectedDraftRevision: '2' } }))).toBe(true);
    c.flow.reviewed.set(true); const result = deferred<unknown>(); command.mockReturnValue(result.promise);
    const prepared = c.flow.preview()!;
    const executing = c.flow.execute(); await settle();
    const dispatched = command.mock.calls.filter(([url]) => url.endsWith('/actions'));
    expect(dispatched).toHaveLength(1);
    const sent = dispatched[0][1];
    expect(sent.fields).toEqual(prepared.canonicalInput);
    expect(sent.reviewed).toBe(true);
    expect(c.flow.pending()).toHaveLength(1);
    expect(c.flow.pending()[0].reference).toEqual({ requestId: sent.requestId, requestHash: sent.expectedRequestHash });
    expect(c.flow.uncertain()).toBe(true);
    expect(sessionStorage.length).toBe(1);
    params.next(convertToParamMap({ id: engagementId })); params.next(convertToParamMap({ id: workpaperId }));
    result.resolve(outcome === 'success' ? { ok: true, value: commandReceipt(sent) }
      : { ok: false, unknown: false, status: 409, code: 'revision.stale', message: 'Old final refusal' });
    expect(await executing).toBeNull(); expect(c.flow.receipt()).toBeNull(); expect(c.flow.message()).not.toContain('Old final refusal');
    expect(c.flow.uncertain()).toBe(true); expect(c.flow.reviewed()).toBe(false);
  });
  it('explicit save retry prepares and dispatches the retained original identity and content, never newer edits', async () => {
    const originalSnapshot = snapshot();
    get.mockResolvedValue(commandState(originalSnapshot));
    const c = open().componentInstance, result = deferred<unknown>(); prepareSave(result.promise);
    expect(saveFields.save.expectedDraftRevision).toBe(originalSnapshot.draft!.draftRevision);
    expect(saveFields.save.baseWorkpaperRevision).toBe(originalSnapshot.revision);
    expect(saveFields.save.baseInputGeneration).toBe(originalSnapshot.inputGeneration);
    expect(saveFields.save.basePolicyGeneration).toBe(originalSnapshot.policyGeneration);
    const saving = c.flow.save(saveFields); await settle(); const sent = command.mock.calls.find(([url]) => url.endsWith('/actions'))![1];
    result.resolve({ ok: false, unknown: true, code: 'outcome.unknown', status: 0, message: 'Lost' }); await saving;
    get.mockImplementation((url: string) => url.endsWith('/state') ? Promise.resolve(commandState(originalSnapshot)) : Promise.resolve({ found: false, receipt: null }));
    await c.flow.reconcile(); expect(c.flow.uncertain()).toBe(true); expect(c.flow.absent()).toBe(true);
    expect(command.mock.calls.filter(([url]) => url.endsWith('/actions'))).toHaveLength(1);
    expect(c.flow.pending()[0].before).toEqual(originalSnapshot);
    expect(c.flow.pending()[0].fields).toEqual(sent.fields);
    expect(c.flow.pending()[0].reference).toEqual({ requestId: sent.requestId, requestHash: sent.expectedRequestHash });
    command.mockImplementation((url: string, body: { requestId: string; fields: unknown }) => Promise.resolve({ ok: true,
      value: url.endsWith('/prepare-save') ? { requestId: body.requestId, requestHash, canonicalInput: decodeWorkpaperCommandFields(body.fields, 'test') }
        : commandReceipt(body, originalSnapshot) }));
    const recovered = await c.flow.save({ ...saveFields, save: { ...saveFields.save, workPerformed: 'Newer unconfirmed edits.' } });
    expect(recovered?.requestId).toBe(sent.requestId); expect(recovered?.commandEvent.after.draft?.workPerformed).toBe(saveFields.save.workPerformed);
    const retry = command.mock.calls.at(-1)![1]; expect(retry.requestId).toBe(sent.requestId); expect(retry.expectedRequestHash).toBe(sent.expectedRequestHash);
    expect(retry.fields).toEqual(sent.fields); expect(retry.reviewed).toBe(false);
  });
  it.each(['SUBMIT', 'DISCARD_DRAFT'] as const)('does not preview %s when its scoped capability is refused', async action => {
    const c = open().componentInstance;
    get.mockResolvedValue({ ...commandState(), canSubmit: false, canDiscardDraft: false, blockers: ['Final publication is unavailable.'] });
    const fields = action === 'SUBMIT' ? { action, submit: { expectedRevision: '1', expectedDraftRevision: '2', draftSaveId: engagementId, workPerformed: 'Worked.', conclusion: 'Clear.' } }
      : { action, discard: { expectedDraftRevision: '2' } };
    expect(await c.flow.prepare(() => fields)).toBe(false); expect(command).not.toHaveBeenCalled();
  });
  it('validates exact final event effects and canonical timezone equivalence', () => {
    const receipt = commandReceipt({ requestId, fields: { action: 'DISCARD_DRAFT', discard: { expectedDraftRevision: '2' } }, reviewBasis: basis });
    receipt.commandEvent.before = { ...receipt.commandEvent.before, draft: { ...receipt.commandEvent.before.draft!, lastSavedAt: '2026-10-16T13:00:00+03:00' } };
    expect(decodeWorkpaperCommandReceipt(receipt, 'receipt').action).toBe('DISCARD_DRAFT');
    receipt.commandEvent.after.draft!.conclusion = 'Unexpected mutation';
    expect(() => decodeWorkpaperCommandReceipt(receipt, 'receipt')).toThrow();
  });
});
