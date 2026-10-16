import { DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, NavigationStart, Router } from '@angular/router';
import { Api, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { PlanningAction, PlanningFields, PlanningPreview, PlanningReceipt, decodePlanningFields, decodePlanningLookup,
  decodePlanningPreview, decodePlanningReceipt, decodePlanningState, planningPayloadKey, samePlanningFields } from './planning-command-contracts';

type Pending = { engagementId: string; action: PlanningAction; reference: PendingRequestReference; clientId?: string; basis?: string };
export class PlanningCommandState {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  readonly preview = signal<PlanningPreview | null>(null);
  readonly receipt = signal<PlanningReceipt | null>(null);
  readonly pending = signal<Pending[]>([]);
  readonly absent = signal<PlanningAction[]>([]);
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly uncertain = computed(() => this.pending().length > 0 || this.storageUnavailable());
  private readonly storageUnavailable = signal(false);
  private generation = 0;
  private operation = 0;
  private executing = false;
  private destroyed = false;
  private input: (() => unknown) | null = null;
  private fingerprint = '';
  private sessionKey = '';
  constructor(private readonly engagement: () => string | null, private readonly context: () => { engagementId: string } | null,
    private readonly canWrite: () => boolean, private readonly otherBusy: () => boolean, private readonly reload: () => void,
    private readonly actions: readonly PlanningAction[]) {
    const params = inject(ActivatedRoute).paramMap.subscribe(() => this.invalidate());
    const route = inject(Router).events.subscribe(e => { if (e instanceof NavigationStart) this.invalidate(); });
    effect(() => {
      const id = this.engagement(), data = this.context(), actor = this.session.current(), epoch = this.session.invalidation();
      untracked(() => {
        const key = `${actor?.firmId}:${actor?.userId}:${actor?.generation}:${actor?.staff}:${epoch}`;
        if (key !== this.sessionKey) { this.sessionKey = key; ++this.operation; this.executing = false; this.busy.set(false); this.receipt.set(null); this.pending.set([]); this.storageUnavailable.set(false); }
        this.invalidate(!!this.receipt() && this.receipt()?.engagementId === id && !!actor?.staff);
        if (id && data?.engagementId === id && actor?.staff) this.restore(id);
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.invalidate(); params.unsubscribe(); route.unsubscribe(); });
  }
  invalidate(preserveReceipt = false, clearAbsence = true): void {
    ++this.generation; this.preview.set(null); this.reviewed.set(false); if (clearAbsence) this.absent.set([]); this.input = null; this.fingerprint = '';
    if (!preserveReceipt) { this.receipt.set(null); this.message.set(''); this.failed.set(false); }
    if (!this.executing) { ++this.operation; this.busy.set(false); }
  }
  inputChanged(): void { this.invalidate(false, false); }
  protect<T extends object>(value: T): T {
    return new Proxy(value, { set: (target, key, next) => { if (Reflect.get(target, key) !== next) this.inputChanged(); return Reflect.set(target, key, next); } });
  }
  private ready() { return !this.destroyed && !!this.session.current()?.staff && !!this.engagement() && this.context()?.engagementId === this.engagement(); }
  canPrepare(action: PlanningAction): boolean {
    return this.ready() && this.canWrite() && this.actions.includes(action) && !this.busy() && !this.otherBusy() &&
      (!this.uncertain() || !this.storageUnavailable() && this.pending().length === 1 && this.pending()[0].engagementId === this.engagement() && this.pending()[0].action === action && this.absent().includes(action));
  }
  private scope(id: string, action: PlanningAction, basis = '0'.repeat(64)) { return { entity: `audit-planning-request/${id}/${action}`, baseRevision: basis }; }
  private restore(id: string): void {
    if (this.pending().some(p => p.engagementId !== id)) return;
    let unavailable = false;
    // Identity only: a fresh authorized workspace read precedes cross-base recovery.
    for (const action of Object.keys(planningPayloadKey) as PlanningAction[]) {
      const saved = this.drafts.readPendingRequest(this.scope(id, action));
      if (saved.state === 'ready' && !this.pending().some(p => p.action === action)) this.pending.update(list => [...list, { engagementId: id, action, reference: saved.draft.value }]);
      if (saved.state === 'unavailable') unavailable = true;
    }
    this.storageUnavailable.set(unavailable);
    if (this.uncertain()) this.message.set(UNKNOWN_OUTCOME);
  }
  private base(id = this.engagement()) { return `/api/ui/engagements/${id}/audit-plan/commands`; }
  private ticket() {
    const generation = this.generation, id = this.engagement(), actor = this.session.current(), epoch = this.session.invalidation(), data = this.context();
    return () => !this.destroyed && generation === this.generation && id === this.engagement() && actor === this.session.current() && epoch === this.session.invalidation() && data === this.context();
  }
  async prepare(input: () => unknown): Promise<void> {
    if (!this.ready() || this.busy() || this.otherBusy()) return;
    let fields: PlanningFields;
    try { fields = decodePlanningFields(input(), 'intent'); } catch { this.failed.set(true); this.message.set('Complete the selected planning intent before preview.'); return; }
    if (!this.canPrepare(fields.action)) return;
    const retained = this.pending().find(p => p.action === fields.action);
    this.invalidate(); const valid = this.ticket(), operation = ++this.operation, id = this.engagement()!, snapshot = JSON.stringify(input());
    this.busy.set(true);
    const query = new URLSearchParams({ action: fields.action });
    const assessment = fields.round?.assessmentId ?? fields.approve?.assessmentId, risk = fields.assessRisk?.riskId ?? fields.assignRiskOwner?.riskId ?? fields.clearRisk?.riskId ?? fields.linkProcedureRisk?.riskId;
    if (assessment) query.set('assessmentId', assessment); if (risk) query.set('riskId', risk); if (fields.linkProcedureRisk) query.set('procedureId', fields.linkProcedureRisk.procedureId);
    try {
      const state = await this.api.get(this.base() + '/state?' + query, decodePlanningState);
      if (!valid()) return;
      if (state.engagementId !== id || state.action !== fields.action ||
        (assessment && state.materiality?.assessmentId !== assessment) || (risk && state.risk?.riskId !== risk) ||
        (fields.linkProcedureRisk && state.procedure?.procedureId !== fields.linkProcedureRisk.procedureId)) throw new Error('Wrong planning scope');
      if (fields.approve && state.materiality?.preparedByUserId === this.session.current()?.userId || fields.clearRisk && state.risk?.assessedByUserId === this.session.current()?.userId) throw new Error('Independent review required');
      if (!state.canProceed) { this.failed.set(true); this.message.set(state.blockers.join(' ')); return; }
      const request = { requestId: retained?.reference.requestId ?? crypto.randomUUID(), reviewBasis: state.reviewBasis, fields, expectedRequestHash: null, reviewed: false };
      const result = await this.api.command(this.base() + '/preview', request);
      if (!valid()) return;
      if (!result.ok) { this.failed.set(true); this.message.set(result.message); return; }
      const preview = decodePlanningPreview(result.value, 'preview');
      if (preview.requestId !== request.requestId || preview.reviewBasis !== request.reviewBasis || preview.state.engagementId !== id || preview.state.clientId !== state.clientId ||
        !samePlanningFields(preview.canonicalInput, fields) || snapshot !== JSON.stringify(input()) || (retained && preview.requestHash !== retained.reference.requestHash)) throw new Error('Changed planning preview');
      this.preview.set(preview); this.input = input; this.fingerprint = snapshot;
      if (!preview.state.canProceed) { this.failed.set(true); this.message.set(preview.state.blockers.join(' ')); }
    } catch { if (valid()) { this.failed.set(true); this.message.set('The exact planning preview could not be verified. Refresh the source and review again.'); } }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
  async execute(): Promise<void> {
    const p = this.preview();
    if (!p?.state.canProceed || !this.ready() || !this.canWrite() || !this.actions.includes(p.state.action) || this.otherBusy() || this.busy() || !this.reviewed() || !this.input || this.fingerprint !== JSON.stringify(this.input()) ||
      (this.uncertain() && (this.storageUnavailable() || this.pending().length !== 1 || !this.pending().some(x => x.engagementId === this.engagement() && x.action === p.state.action && x.reference.requestId === p.requestId && x.reference.requestHash === p.requestHash)))) return;
    const pending: Pending = { engagementId: p.state.engagementId, clientId: p.state.clientId, action: p.state.action, basis: p.reviewBasis, reference: { requestId: p.requestId, requestHash: p.requestHash } };
    if (!this.drafts.save(this.scope(pending.engagementId, pending.action, p.reviewBasis), pending.reference, pendingRequestReference, true)) { this.failed.set(true); this.message.set('The exact request reference could not be saved. No planning command was sent.'); return; }
    const valid = this.ticket(), operation = ++this.operation; let refresh = false;
    this.pending.set([pending]); this.executing = true; this.busy.set(true); this.reviewed.set(false); this.message.set('');
    try {
      const result = await this.api.command(this.base() + '/actions', { requestId: p.requestId, reviewBasis: p.reviewBasis, fields: p.canonicalInput, expectedRequestHash: p.requestHash, reviewed: true });
      if (!valid()) return;
      this.preview.set(null);
      if (!result.ok) { this.failed.set(true); this.message.set(result.message + ' Look up the exact retained request before another planning command.'); return; }
      const receipt = decodePlanningReceipt(result.value, 'receipt'); if (!this.matches(receipt, pending, p.state.clientId)) throw new Error('Wrong receipt');
      refresh = this.accept(receipt, pending);
    } catch { if (valid()) { this.failed.set(true); this.message.set(UNKNOWN_OUTCOME); } }
    finally { if (operation === this.operation) { this.executing = false; this.busy.set(false); if (!valid() && !this.destroyed && !refresh) this.message.set(UNKNOWN_OUTCOME); if (refresh) this.reload(); } }
  }
  private matches(r: PlanningReceipt, pending: Pending, client: string): boolean {
    return r.actorId === this.session.current()?.userId && r.actorEpoch === this.session.current()?.generation && r.engagementId === pending.engagementId && r.clientId === client &&
      (!pending.clientId || r.clientId === pending.clientId) && (!pending.basis || r.reviewBasis === pending.basis) && r.action === pending.action && r.requestId === pending.reference.requestId && r.requestHash === pending.reference.requestHash;
  }
  private accept(receipt: PlanningReceipt, pending: Pending): boolean {
    this.receipt.set(receipt);
    if (!this.drafts.clear(this.scope(pending.engagementId, pending.action).entity)) { this.message.set('Receipt verified, but the tab checkpoint could not be cleared. Restore storage and look up this receipt again.'); return false; }
    this.pending.update(list => list.filter(p => p !== pending)); this.absent.set([]); this.failed.set(false); this.message.set(receipt.result.summary);
    return true;
  }
  async reconcile(action: PlanningAction): Promise<void> {
    const pending = this.pending().find(p => p.action === action);
    if (!pending || !this.ready() || pending.engagementId !== this.engagement() || this.busy() || this.otherBusy()) return;
    this.preview.set(null); this.reviewed.set(false); this.absent.set([]);
    const valid = this.ticket(), operation = ++this.operation; let refresh = false; this.busy.set(true);
    try {
      const state = await this.api.get(this.base() + '/state?action=' + action, decodePlanningState);
      if (!valid()) return;
      if (state.engagementId !== pending.engagementId || state.action !== action) throw new Error('Wrong recovered scope');
      const lookup = await this.api.get(this.base() + '/receipts/' + pending.reference.requestId + '?requestHash=' + pending.reference.requestHash, decodePlanningLookup);
      if (!valid()) return;
      if (lookup.found !== !!lookup.receipt) throw new Error('Inconsistent lookup');
      if (!lookup.found) { this.absent.set([action]); this.message.set('No receipt was found at lookup time. A delayed command may still commit. Retain this identity and prepare a fresh same-request review before explicitly submitting again.'); return; }
      if (!this.matches(lookup.receipt!, pending, state.clientId)) throw new Error('Wrong recovered receipt');
      refresh = this.accept(lookup.receipt!, pending);
    } catch { if (valid()) { this.failed.set(true); this.message.set('Exact planning receipt lookup could not be verified. Keep the request identity and do not repeat blindly.'); } }
    finally { if (operation === this.operation) { this.busy.set(false); if (refresh) this.reload(); } }
  }
}
