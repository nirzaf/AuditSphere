import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { PlanningCommandState } from './planning-command-state';
import { PlanningAction, decodePlanningFields, decodePlanningPreview, decodePlanningReceipt, planningPayloadKey, samePlanningFields } from './planning-command-contracts';
const a = '11111111-1111-4111-8111-111111111111', b = '22222222-2222-4222-8222-222222222222', c = '33333333-3333-4333-8333-333333333333', d = '44444444-4444-4444-8444-444444444444';
const hash = 'a'.repeat(64), basis = 'b'.repeat(64), epoch = '9007199254740993';
const cases: [PlanningAction, object][] = [
  ['CALCULATE_MATERIALITY', { calculate: { benchmarkKind: 'REVENUE', destinationCode: null, ratePercent: '1', performancePercent: '75', trivialPercent: '5', rationale: 'source', qualitativeConsiderations: null } }],
  ['ROUND_MATERIALITY', { round: { assessmentId: b, planningMateriality: '100', tolerableError: '75', sadThreshold: '5', rationale: 'rounding' } }],
  ['APPROVE_MATERIALITY', { approve: { assessmentId: b } }],
  ['CREATE_RISK', { createRisk: { area: 'CASH', assertion: 'Existence', description: 'Risk', drivers: 'Drivers', significanceDecision: 'SIGNIFICANT', response: 'Response', controlsConsidered: null } }],
  ['ASSESS_RISK', { assessRisk: { riskId: c, likelihood: 3, magnitude: 3, fraud: false, criticalEstimate: false, rationale: 'assessment' } }],
  ['ASSIGN_RISK_OWNER', { assignRiskOwner: { riskId: c, expectedAssessmentId: b, ownerUserId: d } }],
  ['CLEAR_RISK', { clearRisk: { riskId: c, expectedAssessmentId: b, note: 'independent review' } }],
  ['LINK_PROCEDURE_RISK', { linkProcedureRisk: { procedureId: d, riskId: c, expectedRiskId: null } }],
];
function deferred<T>() { let resolve!: (value: T) => void, reject!: (error: Error) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
const materiality = { assessmentId: b, calculationId: c, roundingDecisionId: null, preparedByUserId: c, state: 'DRAFT', benchmarkKind: 'REVENUE', destinationCode: null, currency: 'QAR', benchmarkAmount: '10000', ratePercent: '1', performancePercent: '75', trivialPercent: '5', computedPlanningMateriality: '100', computedTolerableError: '75', computedSadThreshold: '5', planningMateriality: '100', tolerableError: '75', sadThreshold: '5', rationale: 'source', qualitativeConsiderations: null, roundingRationale: null, roundedByUserId: null, mappingId: b, mappingVersion: '1', datasetId: c, datasetDigest: hash, policyVersion: 'v1', approvedByUserId: null, approvedAt: null };
const risk = { riskId: c, area: 'CASH', assertion: 'Existence', description: 'Risk', drivers: 'Drivers', significanceDecision: 'SIGNIFICANT', response: 'Response', controlsConsidered: null, assessmentId: b, likelihood: 3, magnitude: 3, fraud: false, criticalEstimate: false, band: 'RED', assessmentRationale: 'assessment', assessedByUserId: c, clearanceId: null, clearedByUserId: null, clearanceNote: null, ownerAssignmentId: null, ownerUserId: null, ownerStaffingLevel: null };
const state = (action: PlanningAction) => ({ engagementId: a, clientId: b, action, reviewBasis: basis, canProceed: true, blockers: [], materiality, risk,
  procedure: { procedureId: d, riskId: null, status: 'PLANNED', resultRevision: '0' }, owners: [{ userId: d, name: 'Owner', staffingLevel: 'AUDIT_MANAGER' }],
  source: { mappingId: b, mappingVersion: '1', datasetId: c, datasetDigest: hash, currency: 'QAR', benchmarks: [{ kind: 'REVENUE', destinationCode: null, label: 'Revenue', amount: '10000', lineCount: 1 }] } });
const preview = (request: { requestId: string; fields: unknown }) => {
  const canonicalInput = decodePlanningFields(request.fields, '');
  if (canonicalInput.calculate) canonicalInput.calculate = { ...canonicalInput.calculate, ratePercent: '1.000000', performancePercent: '75.000000', trivialPercent: '5.000000' };
  if (canonicalInput.round) canonicalInput.round = { ...canonicalInput.round, planningMateriality: '100.000000', tolerableError: '75.000000', sadThreshold: '5.000000' };
  return { requestId: request.requestId, requestHash: hash, reviewBasis: basis, canonicalInput, state: state(canonicalInput.action), summary: 'Exact planning effect', materialityOutcome: null, riskBandOutcome: null };
};
const receipt = (p: ReturnType<typeof preview>) => {
  const action = p.state.action;
  const resultId = action === 'APPROVE_MATERIALITY' ? b : d;
  const m = action.endsWith('MATERIALITY') ? { ...materiality, assessmentId: resultId,
    roundingDecisionId: action === 'ROUND_MATERIALITY' ? d : null, state: action === 'APPROVE_MATERIALITY' ? 'APPROVED' : 'DRAFT',
    approvedByUserId: action === 'APPROVE_MATERIALITY' ? a : null } : null;
  const r = action.includes('RISK') ? { ...risk, riskId: action === 'CREATE_RISK' ? d : c,
    assessmentId: action === 'ASSESS_RISK' ? d : b, ownerAssignmentId: action === 'ASSIGN_RISK_OWNER' ? d : null,
    ownerUserId: action === 'ASSIGN_RISK_OWNER' ? d : null, clearanceId: action === 'CLEAR_RISK' ? d : null, clearedByUserId: action === 'CLEAR_RISK' ? a : null } : null;
  return { id: d, actorId: a, actorEpoch: epoch, engagementId: a, clientId: b, requestId: p.requestId, requestHash: hash, reviewBasis: basis, action,
    createdAt: '2026-10-16T00:00:00Z', preview: p, result: { resultId, materiality: m, risk: r,
      procedure: action === 'LINK_PROCEDURE_RISK' ? { procedureId: d, riskId: c, status: 'PLANNED', resultRevision: '0' } : null, summary: 'Retained exact action' } };
};
describe('Eight reviewed audit planning actions', () => {
  let flow: PlanningCommandState, api: { get: ReturnType<typeof vi.fn>; command: ReturnType<typeof vi.fn> }, drafts: { save: ReturnType<typeof vi.fn>; readPendingRequest: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };
  let selected: PlanningAction, lastPreview: ReturnType<typeof preview>;
  const id = signal<string | null>(a), context = signal<{ engagementId: string } | null>({ engagementId: a }), writable = signal(true), externalBusy = signal(false);
  const session = { current: signal({ staff: true, userId: a, firmId: b, generation: epoch }), invalidation: signal(0) };
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>, saved: Map<string, unknown>, reload: ReturnType<typeof vi.fn<() => void>>;
  beforeEach(() => {
    selected = 'CALCULATE_MATERIALITY'; id.set(a); context.set({ engagementId: a }); writable.set(true); externalBusy.set(false); session.invalidation.set(0); session.current.set({ staff: true, userId: a, firmId: b, generation: epoch });
    params = new BehaviorSubject(convertToParamMap({ id: a })); saved = new Map(); reload = vi.fn<() => void>();
    drafts = { save: vi.fn((scope, reference) => { saved.set(scope.entity, reference); return true; }), clear: vi.fn(entity => { saved.delete(entity); return true; }),
      readPendingRequest: vi.fn(scope => saved.has(scope.entity) ? { state: 'ready', draft: { value: saved.get(scope.entity), submissionPending: true } } : { state: 'absent' }) };
    api = { get: vi.fn(async (url: string, decoder) => decoder(url.includes('/receipts/') ? { found: true, receipt: receipt(lastPreview) } : state(selected), '')),
      command: vi.fn(async (url: string, request) => { if (url.endsWith('/preview')) { lastPreview = preview(request); return { ok: true, value: lastPreview }; } return { ok: true, value: receipt(lastPreview) }; }) };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api }, { provide: SessionService, useValue: session }, { provide: TabDrafts, useValue: drafts },
      { provide: ActivatedRoute, useValue: { paramMap: params } }, { provide: Router, useValue: { events: new Subject() } }] });
    flow = TestBed.runInInjectionContext(() => new PlanningCommandState(() => id(), () => context(), () => writable(), () => externalBusy(), reload, Object.keys(planningPayloadKey) as PlanningAction[])); TestBed.tick();
  });
  afterEach(() => TestBed.resetTestingModule());
  for (const [action, payload] of cases) {
    it(`${action}: previews canonical fields, requires fresh assent and sends exactly once`, async () => {
      selected = action; await flow.prepare(() => ({ action, ...payload })); expect(flow.preview()?.state.action).toBe(action);
      await flow.execute(); expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
      flow.reviewed.set(true); await flow.execute(); await flow.execute();
      const writes = api.command.mock.calls.filter(c => c[0].endsWith('/actions')); expect(writes).toHaveLength(1);
      expect(writes[0][1].fields).toEqual(lastPreview.canonicalInput); expect(writes[0][1].expectedRequestHash).toBe(hash);
      expect(drafts.save.mock.calls[0][1]).toEqual({ requestId: lastPreview.requestId, requestHash: hash }); expect(flow.receipt()?.actorEpoch).toBe(epoch);
    });
    it(`${action}: retains identity after dropped response, restores after reload and recovers without POST`, async () => {
      selected = action; await flow.prepare(() => ({ action, ...payload })); flow.reviewed.set(true);
      api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await flow.execute(); expect(flow.uncertain()).toBe(true);
      const before = api.command.mock.calls.length;
      const restored = TestBed.runInInjectionContext(() => new PlanningCommandState(() => id(), () => context(), () => writable(), () => externalBusy(), reload, Object.keys(planningPayloadKey) as PlanningAction[])); TestBed.tick();
      expect(restored.uncertain()).toBe(true); expect(restored.reviewed()).toBe(false); expect(restored.preview()).toBeNull();
      await restored.reconcile(action); expect(restored.receipt()?.requestId).toBe(lastPreview.requestId); expect(restored.uncertain()).toBe(false); expect(api.command).toHaveBeenCalledTimes(before);
    });
  }
  it('retains a missing request and only re-previews its same ID/hash before explicit execution', async () => {
    const input = () => ({ action: selected, ...cases[0][1] }); await flow.prepare(input); const requestId = lastPreview.requestId;
    flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await flow.execute();
    api.get.mockImplementationOnce(async (_url, decoder) => decoder(state(selected), '')).mockResolvedValueOnce({ found: false, receipt: null });
    await flow.reconcile(selected); expect(flow.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
    await flow.prepare(input); expect(lastPreview.requestId).toBe(requestId); expect(flow.reviewed()).toBe(false);
    flow.reviewed.set(true); await flow.execute(); expect(api.command.mock.calls.at(-1)![1].requestId).toBe(requestId);
  });
  for (const failure of [false, true]) {
    it(`discards A-B-A late preview ${failure ? 'error' : 'success'}`, async () => {
      const model = flow.protect({ rate: '1' }), delayed = deferred<unknown>(); api.command.mockReturnValueOnce(delayed.promise);
      const preparing = flow.prepare(() => ({ action: selected, calculate: { ...(cases[0][1] as { calculate: object }).calculate, ratePercent: model.rate } })); await Promise.resolve();
      const request = api.command.mock.calls[0][1]; model.rate = '2'; model.rate = '1';
      if (failure) delayed.reject(new Error('late')); else delayed.resolve({ ok: true, value: preview(request) }); await preparing;
      expect(flow.preview()).toBeNull(); expect(flow.reviewed()).toBe(false); expect(flow.message()).toBe('');
    });
    it(`discards A-B-A late execution ${failure ? 'error' : 'success'} and keeps uncertainty`, async () => {
      await flow.prepare(() => ({ action: selected, ...cases[0][1] })); flow.reviewed.set(true); const delayed = deferred<unknown>(); api.command.mockReturnValueOnce(delayed.promise); const executing = flow.execute();
      params.next(convertToParamMap({ id: b })); params.next(convertToParamMap({ id: a }));
      if (failure) delayed.reject(new Error('late')); else delayed.resolve({ ok: true, value: receipt(lastPreview) }); await executing;
      expect(flow.receipt()).toBeNull(); expect(flow.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
    });
  }
  for (const field of ['actorId', 'actorEpoch', 'clientId', 'engagementId', 'action', 'requestId', 'requestHash'] as const) {
    it(`rejects recovered receipt with changed ${field}`, async () => {
      await flow.prepare(() => ({ action: selected, ...cases[0][1] })); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await flow.execute();
      api.get.mockImplementationOnce(async (_url, decoder) => decoder(state(selected), '')).mockImplementationOnce(async (_url, decoder) => decoder({ found: true, receipt: { ...receipt(lastPreview), [field]: field === 'actorEpoch' ? '9007199254740992' : field === 'action' ? 'CREATE_RISK' : field === 'requestHash' ? basis : d } }, ''));
      await flow.reconcile(selected); expect(flow.uncertain()).toBe(true); expect(flow.receipt()).toBeNull(); expect(drafts.clear).not.toHaveBeenCalled();
    });
  }
  it('prevents duplicate/busy/uncertain/held writes and refuses unavailable checkpoint storage', async () => {
    await flow.prepare(() => ({ action: selected, ...cases[0][1] })); flow.reviewed.set(true); externalBusy.set(true); await flow.execute(); expect(reload).not.toHaveBeenCalled();
    externalBusy.set(false); writable.set(false); await flow.execute(); expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
    writable.set(true); drafts.save.mockReturnValueOnce(false); await flow.execute(); expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
    const delayed = deferred<unknown>(); api.command.mockReturnValueOnce(delayed.promise); const executing = flow.execute(); await flow.execute(); expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(1);
    delayed.resolve({ ok: false, unknown: true, message: 'unknown' }); await executing; await flow.execute(); expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(1);
  });
  it('rejects ambiguous payloads, numeric finance and value-changing canonical previews', () => {
    expect(() => decodePlanningFields({ action: 'CREATE_RISK', ...cases[3][1], ...cases[2][1] }, '')).toThrow();
    expect(() => decodePlanningFields({ action: 'CALCULATE_MATERIALITY', calculate: { ...(cases[0][1] as { calculate: object }).calculate, ratePercent: 1 } }, '')).toThrow();
    const left = decodePlanningFields({ action: selected, ...cases[0][1] }, ''), canonical = preview({ requestId: d, fields: left }).canonicalInput;
    expect(samePlanningFields(left, canonical)).toBe(true); expect(samePlanningFields(left, { ...canonical, calculate: { ...canonical.calculate!, ratePercent: '1.000001' } })).toBe(false);
    const raw = preview({ requestId: d, fields: left }); expect(() => decodePlanningPreview({ ...raw, reviewBasis: hash }, '')).toThrow();
    expect(() => decodePlanningReceipt({ ...receipt(raw), actorEpoch: 9007199254740993 }, '')).toThrow();
  });
  for (const failure of [false, true]) {
    it(`fences late state ${failure ? 'error' : 'success'} after an A-B-A edit`, async () => {
      const model = flow.protect({ rate: '1' }), delayed = deferred<unknown>(); api.get.mockReturnValueOnce(delayed.promise);
      const reading = flow.prepare(() => ({ action: selected, calculate: { ...(cases[0][1] as { calculate: object }).calculate, ratePercent: model.rate } }));
      model.rate = '2'; model.rate = '1';
      if (failure) delayed.reject(new Error('obsolete')); else delayed.resolve(state(selected)); await reading;
      expect(api.command).not.toHaveBeenCalled(); expect(flow.preview()).toBeNull(); expect(flow.message()).toBe('');
    });
  }
  it('does not accept preview responses after session invalidation or destroyed lifetime', async () => {
    const first = deferred<unknown>(); api.command.mockReturnValueOnce(first.promise);
    const preparing = flow.prepare(() => ({ action: selected, ...cases[0][1] })); await Promise.resolve();
    const request = api.command.mock.calls[0][1]; saved.clear(); session.invalidation.update(n => n + 1); TestBed.tick();
    first.resolve({ ok: true, value: preview(request) }); await preparing; expect(flow.preview()).toBeNull();
    const second = deferred<unknown>(); api.command.mockReturnValueOnce(second.promise);
    const later = flow.prepare(() => ({ action: selected, ...cases[0][1] })); await Promise.resolve();
    const laterRequest = api.command.mock.calls.at(-1)![1]; TestBed.resetTestingModule();
    second.resolve({ ok: true, value: preview(laterRequest) }); await later; expect(flow.preview()).toBeNull();
  });
  it('does not retire uncertainty from an arbitrary workspace refresh or mismatched canonical hash', async () => {
    const input = () => ({ action: selected, ...cases[0][1] }); await flow.prepare(input); flow.reviewed.set(true);
    api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await flow.execute();
    context.set({ engagementId: a }); TestBed.tick(); expect(flow.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
    api.get.mockImplementationOnce(async (_url, decoder) => decoder(state(selected), '')).mockResolvedValueOnce({ found: false, receipt: null });
    await flow.reconcile(selected); const retained = flow.pending()[0].reference;
    api.command.mockImplementationOnce(async (_url, request) => ({ ok: true, value: { ...preview(request), requestHash: basis } }));
    await flow.prepare(input); expect(flow.preview()).toBeNull(); expect(flow.pending()[0].reference).toEqual(retained); expect(flow.uncertain()).toBe(true);
  });

});
