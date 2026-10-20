import { DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, NavigationStart, Router } from '@angular/router';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { FindingAction, FindingFields, FindingPreview, FindingReceipt, decodeFindingCommandState, decodeFindingFields, decodeFindingLookup,
  decodeFindingPreview, decodeFindingReceipt, findingActions, sameFindingFields } from './finding-command-contracts';

type Pending = { findingId: string; action: FindingAction; reference: PendingRequestReference; clientId?: string; engagementId?: string; basis?: string };
const unknown = 'The finding command outcome is not confirmed. Look up the exact retained request; do not repeat blindly.';
export class FindingCommandState {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  readonly preview = signal<FindingPreview | null>(null);
  readonly receipt = signal<FindingReceipt | null>(null);
  readonly pending = signal<Pending[]>([]);
  readonly absent = signal<FindingAction[]>([]);
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  private readonly storageUnavailable = signal(false);
  readonly uncertain = computed(() => this.pending().length > 0 || this.storageUnavailable());
  private generation = 0;
  private operation = 0;
  private executing = false;
  private destroyed = false;
  private scopeKey = '';
  private contextKey = '';
  private input: (() => unknown) | null = null;
  private fingerprint = '';
  constructor(private readonly id: () => string | null, private readonly context: () => { id: string; engagementId: string } | null,
    private readonly allowed: (action: FindingAction) => boolean, private readonly reload: () => void,
    private readonly accepted: (receipt: FindingReceipt) => void) {
    const params = inject(ActivatedRoute).paramMap.subscribe(() => this.invalidate());
    const route = inject(Router).events.subscribe(e => { if (e instanceof NavigationStart) this.invalidate(); });
    effect(() => {
      const id = this.id(), data = this.context(), actor = this.session.current(), epoch = this.session.invalidation();
      untracked(() => {
        const key = JSON.stringify([id, actor?.firmId, actor?.userId, actor?.generation, actor?.staff, epoch]);
        const contextKey = JSON.stringify([id, data?.id ?? null, data?.engagementId ?? null]);
        let changed = false;
        if (key !== this.scopeKey) {
          this.scopeKey = key; ++this.operation; this.executing = false; this.busy.set(false);
          this.pending.set([]); this.storageUnavailable.set(false); this.receipt.set(null); changed = true;
        }
        if (contextKey !== this.contextKey) { this.contextKey = contextKey; changed = true; }
        if (changed) this.invalidate(this.receipt()?.findingId === id && !!actor?.staff);
        if (id && data?.id === id && actor?.staff) this.restore(id);
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.invalidate(); params.unsubscribe(); route.unsubscribe(); });
  }
  invalidate(preserveReceipt = false, clearAbsence = true): void {
    ++this.generation; this.preview.set(null); this.reviewed.set(false); this.input = null; this.fingerprint = '';
    if (clearAbsence) this.absent.set([]);
    if (!preserveReceipt) { this.receipt.set(null); this.message.set(''); this.failed.set(false); }
    if (!this.executing) { ++this.operation; this.busy.set(false); }
  }
  inputChanged(): void { this.invalidate(false, false); }
  private ready() { return !this.destroyed && !!this.session.current()?.staff && !!this.id() && this.context()?.id === this.id(); }
  canPrepare(action: FindingAction): boolean {
    return this.ready() && this.allowed(action) && !this.busy() && (!this.uncertain() || !this.storageUnavailable() && this.pending().length === 1 &&
      this.pending()[0].findingId === this.id() && this.pending()[0].action === action && this.absent().includes(action));
  }
  private scope(id: string, action: FindingAction, basis = '0'.repeat(64)) { return { entity: `finding-command-request/${id}/${action}`, baseRevision: basis }; }
  private restore(id: string): void {
    let unavailable = false;
    for (const action of findingActions) {
      const saved = this.drafts.readPendingRequest(this.scope(id, action));
      if (saved.state === 'ready' && !this.pending().some(p => p.action === action)) this.pending.update(list => [...list, { findingId: id, action, reference: saved.draft.value }]);
      if (saved.state === 'unavailable') unavailable = true;
    }
    this.storageUnavailable.set(unavailable); if (this.uncertain()) this.message.set(unknown);
  }
  private base() { return `/api/ui/findings/${this.id()}/commands`; }
  private ticket() {
    const generation = this.generation, id = this.id(), actor = this.session.current(), epoch = this.session.invalidation(), data = this.context();
    return () => {
      const current = this.context(), currentActor = this.session.current();
      return !this.destroyed && generation === this.generation && id === this.id() && actor === currentActor && epoch === this.session.invalidation() &&
        data?.id === current?.id && data?.engagementId === current?.engagementId;
    };
  }
  async prepare(input: () => unknown): Promise<void> {
    if (!this.ready() || this.busy()) return;
    let fields: FindingFields;
    try { fields = decodeFindingFields(input(), 'intent'); } catch { this.failed.set(true); this.message.set('Complete the selected finding intent before preview.'); return; }
    if (!this.canPrepare(fields.action)) return;
    const retained = this.pending().find(p => p.action === fields.action);
    this.invalidate(); const valid = this.ticket(), operation = ++this.operation, id = this.id()!, fingerprint = JSON.stringify(input()); this.busy.set(true);
    try {
      const state = await this.api.get(this.base() + '/state', decodeFindingCommandState);
      if (!valid()) return;
      if (state.finding.findingId !== id || state.finding.engagementId !== this.context()?.engagementId) throw new Error('Wrong finding scope');
      const permitted = fields.action === 'RECORD_RESPONSE' ? state.canRecordResponse : state.canDesignate;
      if (!permitted) { this.failed.set(true); this.message.set((fields.action === 'RECORD_RESPONSE' ? state.responseBlockers : state.designationBlockers).join(' ') || 'This finding action is unavailable.'); return; }
      const request = { requestId: retained?.reference.requestId ?? crypto.randomUUID(), expectedRevision: state.finding.revision, reviewBasis: state.reviewBasis, fields, expectedRequestHash: null, reviewed: false };
      const result = await this.api.command(this.base() + '/preview', request);
      if (!valid()) return;
      if (!result.ok) { this.failed.set(true); this.message.set(result.message); return; }
      const preview = decodeFindingPreview(result.value, 'preview');
      if (preview.requestId !== request.requestId || preview.expectedRevision !== request.expectedRevision || preview.reviewBasis !== request.reviewBasis ||
        JSON.stringify(preview.before) !== JSON.stringify(state.finding) || !sameFindingFields(preview.canonicalInput, fields) || fingerprint !== JSON.stringify(input()) ||
        retained && preview.requestHash !== retained.reference.requestHash) throw new Error('Changed finding preview');
      this.preview.set(preview); this.input = input; this.fingerprint = fingerprint;
    } catch { if (valid()) { this.failed.set(true); this.message.set('The exact finding preview could not be verified. Refresh and review again.'); } }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
  async execute(): Promise<void> {
    const p = this.preview();
    if (!p?.canProceed || !this.ready() || !this.allowed(p.canonicalInput.action) || this.busy() || !this.reviewed() || !this.input || this.fingerprint !== JSON.stringify(this.input()) ||
      this.uncertain() && (this.storageUnavailable() || this.pending().length !== 1 || !this.pending().some(x => x.findingId === this.id() && x.action === p.canonicalInput.action && x.reference.requestId === p.requestId && x.reference.requestHash === p.requestHash))) return;
    const pending: Pending = { findingId: p.before.findingId, clientId: p.before.clientId, engagementId: p.before.engagementId, action: p.canonicalInput.action, basis: p.reviewBasis,
      reference: { requestId: p.requestId, requestHash: p.requestHash } };
    if (!this.drafts.save(this.scope(pending.findingId, pending.action, p.reviewBasis), pending.reference, pendingRequestReference, true)) { this.failed.set(true); this.message.set('The request reference could not be saved. No finding command was sent.'); return; }
    const valid = this.ticket(), operation = ++this.operation; let refresh = false;
    this.pending.set([pending]); this.executing = true; this.busy.set(true); this.reviewed.set(false); this.message.set('');
    try {
      const result = await this.api.command(this.base() + '/actions', { requestId: p.requestId, expectedRevision: p.expectedRevision, reviewBasis: p.reviewBasis,
        fields: p.canonicalInput, expectedRequestHash: p.requestHash, reviewed: true });
      if (!valid()) return;
      this.preview.set(null);
      if (!result.ok) { this.failed.set(true); this.message.set(result.message + ' ' + unknown); return; }
      const receipt = decodeFindingReceipt(result.value, 'receipt'); if (!this.matches(receipt, pending, p.before.clientId, p.before.engagementId)) throw new Error('Wrong receipt');
      refresh = this.accept(receipt, pending);
    } catch { if (valid()) { this.failed.set(true); this.message.set(unknown); } }
    finally { if (operation === this.operation) { this.executing = false; this.busy.set(false); if (!valid() && !this.destroyed && !refresh) this.message.set(unknown); if (refresh) this.reload(); } }
  }
  private matches(r: FindingReceipt, p: Pending, client: string, engagement: string): boolean {
    return r.actorId === this.session.current()?.userId && r.actorEpoch === this.session.current()?.generation && r.findingId === p.findingId &&
      r.preview.before.clientId === client && r.preview.before.engagementId === engagement && (!p.clientId || p.clientId === client) && (!p.engagementId || p.engagementId === engagement) &&
      (!p.basis || p.basis === r.reviewBasis) && r.action === p.action && r.requestId === p.reference.requestId && r.requestHash === p.reference.requestHash;
  }
  private accept(receipt: FindingReceipt, pending: Pending): boolean {
    this.receipt.set(receipt);
    if (!this.drafts.clear(this.scope(pending.findingId, pending.action).entity)) { this.message.set('Receipt verified, but the tab checkpoint could not be cleared. Restore storage and look up this receipt again.'); return false; }
    this.pending.update(list => list.filter(p => p !== pending)); this.absent.set([]); this.failed.set(false); this.message.set('Exact finding amendment receipt verified.'); this.accepted(receipt); return true;
  }
  canReconcile(action: FindingAction): boolean {
    const pending = this.pending().find(p => p.action === action);
    return this.ready() && !this.busy() && !!pending && pending.findingId === this.id();
  }
  async reconcile(action: FindingAction): Promise<void> {
    const pending = this.pending().find(p => p.action === action);
    if (!pending || !this.canReconcile(action)) return;
    this.preview.set(null); this.reviewed.set(false); this.absent.set([]); const valid = this.ticket(), operation = ++this.operation; let refresh = false; this.busy.set(true);
    try {
      const state = await this.api.get(this.base() + '/state', decodeFindingCommandState);
      if (!valid()) return;
      if (state.finding.findingId !== pending.findingId || state.finding.engagementId !== this.context()?.engagementId) throw new Error('Wrong recovered scope');
      const lookup = await this.api.get(this.base() + '/receipts/' + pending.reference.requestId + '?requestHash=' + pending.reference.requestHash, decodeFindingLookup);
      if (!valid()) return;
      if (lookup.found !== !!lookup.receipt) throw new Error('Inconsistent lookup');
      if (!lookup.found) { this.absent.set([action]); this.message.set('No receipt was found at lookup time. A delayed command may still commit. Retain the identity and prepare a fresh same-request review before submitting again.'); return; }
      if (!this.matches(lookup.receipt!, pending, state.finding.clientId, state.finding.engagementId)) throw new Error('Wrong recovered receipt');
      refresh = this.accept(lookup.receipt!, pending);
    } catch { if (valid()) { this.failed.set(true); this.message.set('Exact finding receipt lookup could not be verified. Keep the request identity and do not repeat blindly.'); } }
    finally { if (operation === this.operation) { this.busy.set(false); if (refresh) this.reload(); } }
  }
}
