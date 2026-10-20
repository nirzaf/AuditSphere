import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { FindingCommandState } from './finding-command-state';
import { FindingAction, decodeFindingFields, decodeFindingPreview, decodeFindingReceipt, findingActions } from './finding-command-contracts';
import { findingTestId as id, findingTestPreview as preview, findingTestReceipt as receipt, findingTestState as state, findingTestHash as hash } from './finding-command-test-data';
function deferred<T>() { let resolve!: (v: T) => void, reject!: (e: Error) => void; const promise = new Promise<T>((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; }
const fields = (action: FindingAction) => decodeFindingFields(action === 'RECORD_RESPONSE' ? { action, response: { managementResponse: 'Exact response', corrected: false } } :
  action === 'DESIGNATE_MANAGEMENT_LETTER' ? { action, designation: { recommendation: 'Exact recommendation' } } : { action }, '');

describe('Finding reviewed command recovery', () => {
  let flow: FindingCommandState, params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let api: { get: ReturnType<typeof vi.fn>; command: ReturnType<typeof vi.fn> }, drafts: { save: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn>; readPendingRequest: ReturnType<typeof vi.fn> };
  let last: ReturnType<typeof preview>, saved: Map<string, unknown>, reload: ReturnType<typeof vi.fn<() => void>>, accepted: ReturnType<typeof vi.fn<() => void>>;
  const selected = signal<string | null>(id(1)), context = signal<{ id: string; engagementId: string } | null>({ id: id(1), engagementId: id(20) }), allowed = signal(true);
  const session = { current: signal({ staff: true, userId: id(90), firmId: id(91), generation: '9007199254740993' }), invalidation: signal(0) };
  const create = () => TestBed.runInInjectionContext(() => new FindingCommandState(() => selected(), () => context(), () => allowed(), reload, accepted));
  beforeEach(() => {
    selected.set(id(1)); context.set({ id: id(1), engagementId: id(20) }); allowed.set(true); session.invalidation.set(0);
    session.current.set({ staff: true, userId: id(90), firmId: id(91), generation: '9007199254740993' });
    params = new BehaviorSubject(convertToParamMap({ id: id(1) })); saved = new Map(); reload = vi.fn<() => void>(); accepted = vi.fn<() => void>();
    drafts = { save: vi.fn((scope, reference) => { saved.set(scope.entity, reference); return true; }), clear: vi.fn(entity => { saved.delete(entity); return true; }),
      readPendingRequest: vi.fn(scope => saved.has(scope.entity) ? { state: 'ready', draft: { value: saved.get(scope.entity), submissionPending: true } } : { state: 'absent' }) };
    api = { get: vi.fn(async (url, decoder) => decoder(url.includes('/receipts/') ? { found: true, receipt: receipt(last, id(90), session.current().generation) } : state(), '')),
      command: vi.fn(async (url, request) => { if (url.endsWith('/preview')) { last = preview(request); return { ok: true, value: last }; } return { ok: true, value: receipt(last, id(90), session.current().generation) }; }) };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api }, { provide: TabDrafts, useValue: drafts }, { provide: SessionService, useValue: session },
      { provide: ActivatedRoute, useValue: { paramMap: params } }, { provide: Router, useValue: { events: new Subject() } }] });
    flow = create(); TestBed.tick();
  });
  afterEach(() => TestBed.resetTestingModule());
  for (const action of findingActions) {
    it(`${action}: requires fresh preview and assent, dispatches once and retains exact amendment`, async () => {
      await flow.prepare(() => fields(action)); await flow.execute(); expect(api.command).toHaveBeenCalledTimes(1);
      flow.reviewed.set(true); await flow.execute(); await flow.execute(); expect(api.command).toHaveBeenCalledTimes(2);
      expect(api.command.mock.calls[1][1]).toMatchObject({ expectedRevision: '1', expectedRequestHash: hash, reviewed: true });
      expect(flow.receipt()?.amendment.after.revision).toBe('2'); expect(flow.receipt()?.actorEpoch).toBe('9007199254740993');
      expect(drafts.save.mock.calls[0][1]).toEqual({ requestId: last.requestId, requestHash: hash }); expect(reload).toHaveBeenCalledTimes(1);
    });
    it(`${action}: restores minimal pending identity after reload and lookup does not POST`, async () => {
      await flow.prepare(() => fields(action)); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'Lost response' }); await flow.execute();
      const restored = create(); TestBed.tick(); const before = api.command.mock.calls.length;
      expect(restored.uncertain()).toBe(true); expect(restored.preview()).toBeNull(); expect(restored.reviewed()).toBe(false);
      await restored.reconcile(action); expect(api.command).toHaveBeenCalledTimes(before); expect(restored.receipt()?.requestId).toBe(last.requestId); expect(restored.uncertain()).toBe(false);
    });
  }
  it('retains point-in-time absence and requires matching hash and fresh same-request assent', async () => {
    const input = () => fields('RECORD_RESPONSE'); await flow.prepare(input); const requestId = last.requestId;
    flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'Lost' }); await flow.execute();
    api.get.mockImplementationOnce(async (_url, decoder) => decoder(state(), '')).mockResolvedValueOnce({ found: false, receipt: null });
    await flow.reconcile('RECORD_RESPONSE'); expect(flow.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
    await flow.prepare(() => fields('DESIGNATE_MANAGEMENT_LETTER')); expect(api.command).toHaveBeenCalledTimes(2);
    await flow.prepare(input); expect(last.requestId).toBe(requestId); expect(flow.reviewed()).toBe(false);
    await flow.execute(); expect(api.command).toHaveBeenCalledTimes(3); flow.reviewed.set(true); await flow.execute(); expect(api.command.mock.calls.at(-1)![1].requestId).toBe(requestId);
  });
  it('waits for scope synchronization before exact receipt recovery and preserves equivalent refreshes', async () => {
    const action = 'DESIGNATE_MANAGEMENT_LETTER';
    await flow.prepare(() => fields(action)); flow.reviewed.set(true);
    api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'Lost response' }); await flow.execute();
    expect(flow.canReconcile(action)).toBe(true);

    context.set(null); TestBed.tick();
    expect(flow.uncertain()).toBe(true); expect(flow.canReconcile(action)).toBe(false);
    context.set({ id: id(1), engagementId: id(20) });
    expect(flow.canReconcile(action)).toBe(false);
    const readsBeforeScopeSettled = api.get.mock.calls.length;
    await flow.reconcile(action);
    expect(api.get).toHaveBeenCalledTimes(readsBeforeScopeSettled);
    TestBed.tick();
    expect(flow.canReconcile(action)).toBe(true);

    api.get.mockImplementationOnce(async (_url, decoder) => {
      context.set({ ...context()! }); TestBed.tick();
      return decoder(state(), '');
    }).mockImplementationOnce(async (_url, decoder) => decoder({ found: true, receipt: receipt(last, id(90), session.current().generation) }, ''));
    const priorReads = api.get.mock.calls.length;
    await flow.reconcile(action);
    expect(api.get).toHaveBeenCalledTimes(priorReads + 2);
    expect(String(api.get.mock.calls[priorReads + 1][0])).toContain('/receipts/');
    expect(flow.receipt()?.requestId).toBe(last.requestId); expect(flow.uncertain()).toBe(false);
  });
  for (const phase of ['state', 'preview', 'execute'] as const) for (const failure of [false, true]) {
    it(`discards A-B-A late ${phase} ${failure ? 'error' : 'success'}`, async () => {
      const input = () => fields('RECORD_RESPONSE');
      if (phase === 'execute') { await flow.prepare(input); flow.reviewed.set(true); }
      const delayed = deferred<unknown>(); if (phase === 'state') api.get.mockReturnValueOnce(delayed.promise); else api.command.mockReturnValueOnce(delayed.promise);
      const task = phase === 'execute' ? flow.execute() : flow.prepare(input); await Promise.resolve();
      const request = api.command.mock.calls.at(-1)?.[1];
      params.next(convertToParamMap({ id: id(2) })); params.next(convertToParamMap({ id: id(1) }));
      if (failure) delayed.reject(new Error('Late')); else delayed.resolve(phase === 'state' ? state() : { ok: true, value: phase === 'preview' ? preview(request) : receipt(last, id(90), session.current().generation) });
      await task; expect(flow.preview()).toBeNull(); expect(flow.receipt()).toBeNull(); expect(flow.reviewed()).toBe(false);
      if (phase === 'execute') { expect(flow.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled(); } expect(reload).not.toHaveBeenCalled();
    });
  }
  for (const failure of [false, true]) {
    it(`discards A-B-A late lookup ${failure ? 'error' : 'success'} without retiring uncertainty`, async () => {
      await flow.prepare(() => fields('RECORD_RESPONSE')); flow.reviewed.set(true);
      api.command.mockResolvedValueOnce({ ok: false, message: 'Unknown' }); await flow.execute();
      const delayed = deferred<unknown>();
      api.get.mockImplementationOnce(async (_url, decoder) => decoder(state(), '')).mockReturnValueOnce(delayed.promise);
      const reading = flow.reconcile('RECORD_RESPONSE'); await Promise.resolve();
      params.next(convertToParamMap({ id: id(2) })); params.next(convertToParamMap({ id: id(1) }));
      if (failure) delayed.reject(new Error('Late lookup')); else delayed.resolve({ found: true, receipt: receipt(last, id(90), session.current().generation) });
      await reading; expect(flow.uncertain()).toBe(true); expect(flow.receipt()).toBeNull(); expect(drafts.clear).not.toHaveBeenCalled();
    });
  }
  for (const key of ['clientId', 'engagementId'] as const) {
    it(`rejects a coherent receipt for another ${key}`, async () => {
      await flow.prepare(() => fields('RECORD_RESPONSE')); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, message: 'Unknown' }); await flow.execute();
      const original = receipt(last, id(90), session.current().generation);
      const before = { ...original.amendment.before, [key]: id(88) }, after = { ...original.amendment.after, [key]: id(88) };
      api.get.mockImplementationOnce(async (_url, decoder) => decoder(state(), '')).mockImplementationOnce(async (_url, decoder) => decoder({ found: true,
        receipt: { ...original, preview: { ...original.preview, before }, amendment: { ...original.amendment, before, after } } }, ''));
      await flow.reconcile('RECORD_RESPONSE'); expect(flow.uncertain()).toBe(true); expect(flow.receipt()).toBeNull(); expect(drafts.clear).not.toHaveBeenCalled();
    });
  }
  it('refuses changed hash on fresh same-request review after missing lookup', async () => {
    const input = () => fields('RECORD_RESPONSE'); await flow.prepare(input); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, message: 'Unknown' }); await flow.execute();
    api.get.mockImplementationOnce(async (_url, decoder) => decoder(state(), '')).mockResolvedValueOnce({ found: false, receipt: null }); await flow.reconcile('RECORD_RESPONSE');
    const reference = flow.pending()[0].reference;
    api.command.mockImplementationOnce(async (_url, request) => ({ ok: true, value: { ...preview(request), requestHash: 'c'.repeat(64) } }));
    await flow.prepare(input); expect(flow.preview()).toBeNull(); expect(flow.reviewed()).toBe(false); expect(flow.pending()[0].reference).toEqual(reference); expect(flow.uncertain()).toBe(true);
  });
  it('resets assent on changed input even A-B-A and never clears uncertainty on arbitrary reads', async () => {
    await flow.prepare(() => fields('RECORD_RESPONSE')); flow.reviewed.set(true); flow.inputChanged(); flow.inputChanged();
    expect(flow.reviewed()).toBe(false); expect(flow.preview()).toBeNull();
    await flow.prepare(() => fields('RECORD_RESPONSE')); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, message: 'Unknown' }); await flow.execute();
    context.set({ id: id(1), engagementId: id(20) }); TestBed.tick(); expect(flow.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
  });
  for (const field of ['actorId', 'actorEpoch', 'findingId', 'requestId', 'requestHash', 'action'] as const) {
    it(`rejects receipt with changed ${field}`, async () => {
      await flow.prepare(() => fields('RECORD_RESPONSE')); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, message: 'Unknown' }); await flow.execute();
      api.get.mockImplementationOnce(async (_url, decoder) => decoder(state(), '')).mockImplementationOnce(async (_url, decoder) => decoder({ found: true, receipt: {
        ...receipt(last, id(90), session.current().generation), [field]: field === 'actorEpoch' ? '9007199254740992' : field === 'requestHash' ? 'c'.repeat(64) : field === 'action' ? 'UNDESIGNATE_MANAGEMENT_LETTER' : id(88) } }, ''));
      await flow.reconcile('RECORD_RESPONSE'); expect(flow.uncertain()).toBe(true); expect(flow.receipt()).toBeNull(); expect(drafts.clear).not.toHaveBeenCalled();
    });
  }
  it('blocks programmatic duplicate, unauthorized and uncertain commands and unavailable storage', async () => {
    await flow.prepare(() => fields('RECORD_RESPONSE')); flow.reviewed.set(true); allowed.set(false); await flow.execute(); expect(api.command).toHaveBeenCalledTimes(1);
    allowed.set(true); drafts.save.mockReturnValueOnce(false); await flow.execute(); expect(api.command).toHaveBeenCalledTimes(1);
    const delayed = deferred<unknown>(); api.command.mockReturnValueOnce(delayed.promise); const task = flow.execute(); await flow.execute(); expect(api.command).toHaveBeenCalledTimes(2);
    delayed.resolve({ ok: false, message: 'Unknown' }); await task; await flow.execute(); expect(api.command).toHaveBeenCalledTimes(2);
  });
  it('fences session invalidation and destroyed lifetime', async () => {
    const delayed = deferred<unknown>(); api.get.mockReturnValueOnce(delayed.promise); const task = flow.prepare(() => fields('RECORD_RESPONSE'));
    session.invalidation.update(n => n + 1); TestBed.tick(); delayed.resolve(state()); await task; expect(api.command).not.toHaveBeenCalled();
    const later = deferred<unknown>(); api.get.mockReturnValueOnce(later.promise); const task2 = flow.prepare(() => fields('RECORD_RESPONSE'));
    TestBed.resetTestingModule(); later.resolve(state()); await task2; expect(api.command).not.toHaveBeenCalled();
  });
  it('strictly rejects ambiguous fields, lossy epochs and mismatched before/after effects', () => {
    expect(() => decodeFindingFields({ ...fields('RECORD_RESPONSE'), designation: { recommendation: 'Other' } }, '')).toThrow();
    const p = preview({ requestId: id(50), fields: fields('RECORD_RESPONSE') }), r = receipt(p);
    expect(() => decodeFindingPreview({ ...p, expectedRevision: '2' }, '')).toThrow();
    expect(() => decodeFindingReceipt({ ...r, actorEpoch: 1 }, '')).toThrow();
    expect(() => decodeFindingReceipt({ ...r, amendment: { ...r.amendment, after: { ...r.amendment.after, corrected: true } } }, '')).toThrow();
    expect(() => decodeFindingReceipt({ ...r, amendment: { ...r.amendment, after: { ...r.amendment.after, clientId: id(99) } } }, '')).toThrow();
  });
});
