import { DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { Api, CommandState, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { MaintenanceAction, MaintenancePreview, MaintenanceReceipt, PendingMaintenance,
  sameMaintenanceIntent, decodeMaintenanceIntent, decodeMaintenanceState, decodeMaintenancePreview, decodeMaintenanceReceipt, decodeMaintenanceLookup } from './period-maintenance-contracts';

/** Page-local orchestration: no command retries, editable drafts, automatic assent or history-based receipts. */
export class ReviewedPeriodMaintenance {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  readonly cmd = new CommandState(this.api);
  readonly preview = signal<MaintenancePreview | null>(null);
  readonly receipt = signal<MaintenanceReceipt | null>(null);
  readonly pending = signal<PendingMaintenance[]>([]);
  readonly absent = signal<MaintenanceAction[]>([]);
  readonly reviewed = signal(false);
  private generation = 0;
  private operation = 0;
  private destroyed = false;
  private executing = false;
  private clientId = '';
  private inputSnapshot = '';
  private currentInput: (() => unknown) | null = null;
  constructor(private readonly actions: readonly MaintenanceAction[]) {
    const route = inject(Router).events.subscribe(event => { if (event instanceof NavigationStart) this.invalidate(); });
    effect(() => {
      this.session.current(); this.session.invalidation();
      untracked(() => {
        ++this.operation; this.executing = false; this.cmd.busy.set(false);
        this.clientId = ''; this.pending.set([]); this.absent.set([]); this.cmd.uncertain.set(false); this.invalidate();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.invalidate(); route.unsubscribe(); });
  }
  invalidate(preserveReceipt = false): void {
    ++this.generation;
    this.preview.set(null); if (!preserveReceipt) this.receipt.set(null); this.reviewed.set(false); this.currentInput = null; this.inputSnapshot = '';
    this.absent.set([]);
    if (this.executing) this.cmd.uncertain.set(true);
    else { ++this.operation; this.cmd.busy.set(false); }
    if (!preserveReceipt) { this.cmd.message.set(''); this.cmd.failed.set(false); }
  }
  changeClient(clientId: string): void {
    this.invalidate();
    this.clientId = clientId;
    // Keep outstanding uncertainty even if the operator selects a different client.
    // Only session invalidation or a matching receipt can retire a pending request.
    if (this.pending().length) this.cmd.uncertain.set(true);
  }
  /** Called only after a fresh authorized /context read. Only request identity crosses a changed base. */
  restorePending(clientId: string): void {
    if (clientId !== this.clientId || !this.session.current()?.staff || this.pending().some(p => p.clientId !== clientId)) return;
    for (const action of this.actions) {
      const saved = this.drafts.readPendingRequest(this.scope(clientId, action));
      if (saved.state === 'ready' && !this.pending().some(p => p.action === action)) {
        this.pending.update(list => [...list, { clientId, action, reference: saved.draft.value }]);
        this.cmd.uncertain.set(true); this.cmd.message.set(UNKNOWN_OUTCOME);
      } else if (saved.state === 'unavailable') {
        this.cmd.uncertain.set(true); this.cmd.message.set('Tab recovery storage is unavailable. Restore access before submitting a command.');
      }
    }
  }
  private scope(clientId: string, action: MaintenanceAction, reviewBasis = '0'.repeat(64)) {
    // readPendingRequest ignores this base for identity-only recovery after an authorized client read.
    return { entity: `period-maintenance-request/${clientId}/${action}`, baseRevision: reviewBasis };
  }
  private ticket() {
    const generation = this.generation, actor = this.session.current(), epoch = this.session.invalidation();
    return () => !this.destroyed && generation === this.generation && actor === this.session.current() && epoch === this.session.invalidation();
  }
  private base(client = this.clientId) { return `/api/ui/accounting/period-maintenance/clients/${client}`; }
  canPrepare(action: MaintenanceAction): boolean {
    return !this.destroyed && !!this.session.current()?.staff && !!this.clientId && !this.cmd.busy() &&
      (!this.cmd.uncertain() && !this.pending().length || this.pending().length === 1 && this.pending()[0].clientId === this.clientId && this.pending()[0].action === action && this.absent().includes(action));
  }
  async prepare(action: MaintenanceAction, periodId: string | null, restatementId: string | null, input: () => unknown): Promise<void> {
    if (!this.canPrepare(action) || !this.actions.includes(action)) return;
    const retained = this.pending().find(p => p.action === action);
    this.invalidate();
    const client = this.clientId, valid = this.ticket(), operation = ++this.operation, snapshot = JSON.stringify(input());
    this.cmd.busy.set(true);
    const query = new URLSearchParams({ action });
    if (periodId) query.set('periodId', periodId);
    if (restatementId) query.set('restatementId', restatementId);
    try {
      const state = await this.api.get(this.base() + '/state?' + query, decodeMaintenanceState);
      if (!valid()) return;
      if (state.clientId !== client || state.action !== action || (periodId && state.periodId !== periodId) || state.restatementId !== restatementId) throw new Error('Wrong maintenance state');
      if (!state.canProceed) { this.cmd.message.set(state.blockers.join(' ')); this.cmd.failed.set(true); return; }
      if (snapshot !== JSON.stringify(input())) return;
      const request = decodeMaintenanceIntent({ requestId: retained?.reference.requestId ?? crypto.randomUUID(), reviewBasis: state.reviewBasis, action,
        rollForward: null, restatement: null, restatementId, reviewed: false, expectedRequestHash: null, ...input() as object }, 'request');
      const outcome = await this.api.command(this.base() + '/preview', request);
      if (!valid()) return;
      if (!outcome.ok) { this.cmd.failed.set(true); this.cmd.message.set(outcome.message); return; }
      const preview = decodeMaintenancePreview(outcome.value, 'preview');
      const canonical = preview.canonicalInput;
      if (preview.requestId !== request.requestId || canonical.requestId !== request.requestId || canonical.action !== action ||
        canonical.reviewBasis !== state.reviewBasis || preview.state.reviewBasis !== state.reviewBasis || preview.state.clientId !== client ||
        preview.state.action !== action || preview.state.periodId !== state.periodId || preview.state.restatementId !== restatementId ||
        canonical.restatementId !== restatementId || canonical.reviewed || canonical.expectedRequestHash !== null ||
        (action === 'ROLL_FORWARD' && (!canonical.rollForward || canonical.restatement || canonical.rollForward.clientId !== client || canonical.rollForward.priorPeriodId !== state.periodId)) ||
        (action === 'CREATE_RESTATEMENT' && (!canonical.restatement || canonical.rollForward || canonical.restatement.clientId !== client || canonical.restatement.periodId !== state.periodId)) ||
        (action === 'APPROVE_RESTATEMENT' && (canonical.rollForward || canonical.restatement)) || snapshot !== JSON.stringify(input())) throw new Error('Wrong maintenance preview');
      if (!sameMaintenanceIntent(canonical, request)) throw new Error('Canonical input differs from exact intent');
      if (preview.approvalDetail?.createdByUserId === this.session.current()?.userId) throw new Error('Independent reviewer required');
      if (retained && preview.requestHash !== retained.reference.requestHash) throw new Error('Recovered request differs from retained intent');
      this.preview.set(preview); this.currentInput = input; this.inputSnapshot = snapshot;
      this.cmd.message.set(preview.state.canProceed ? '' : preview.state.blockers.join(' '));
    } catch {
      if (valid()) { this.cmd.failed.set(true); this.cmd.message.set('The maintenance preview could not be verified. Refresh the source and preview again.'); }
    } finally { if (operation === this.operation) this.cmd.busy.set(false); }
  }
  async execute(): Promise<void> {
    const preview = this.preview(), actor = this.session.current();
    if (!preview?.state.canProceed || this.cmd.busy() || this.destroyed || (this.cmd.uncertain() && !this.pending().some(p => p.action === preview.state.action && p.reference.requestId === preview.requestId && p.reference.requestHash === preview.requestHash)) || !this.reviewed() || !actor?.staff || preview.state.clientId !== this.clientId || !this.actions.includes(preview.state.action) || !this.currentInput || this.inputSnapshot !== JSON.stringify(this.currentInput())) return;
    const reference = { requestId: preview.requestId, requestHash: preview.requestHash };
    const pending: PendingMaintenance = { clientId: this.clientId, action: preview.state.action, reference,
      expected: { reviewBasis: preview.state.reviewBasis, periodId: preview.state.periodId, restatementId: preview.state.restatementId } };
    if (!this.drafts.save(this.scope(this.clientId, pending.action, preview.state.reviewBasis), reference, pendingRequestReference, true)) {
      this.cmd.failed.set(true); this.cmd.message.set('The exact recovery reference could not be saved in this tab. Command was not sent.'); return;
    }
    const valid = this.ticket(), operation = ++this.operation;
    this.pending.set([pending]); this.cmd.busy.set(true); this.executing = true; this.reviewed.set(false); this.cmd.message.set('');
    try {
      const outcome = await this.api.command(this.base() + '/actions', { ...preview.canonicalInput, reviewed: true, expectedRequestHash: preview.requestHash });
      if (!valid()) return;
      this.preview.set(null);
      if (!outcome.ok) {
        // Even a refusal may follow concurrent publication. Only exact receipt lookup resolves the checkpoint.
        this.cmd.uncertain.set(true); this.cmd.failed.set(true); this.cmd.message.set(outcome.message + ' Look up the retained exact request before another command.'); return;
      }
      const receipt = decodeMaintenanceReceipt(outcome.value, 'receipt');
      if (!this.matches(receipt, pending)) throw new Error('Wrong receipt');
      this.accept(receipt, pending);
    } catch {
      if (valid()) { this.cmd.uncertain.set(true); this.cmd.failed.set(true); this.cmd.message.set(UNKNOWN_OUTCOME); }
    } finally {
      if (operation === this.operation) {
        this.executing = false; this.cmd.busy.set(false);
        if (!valid() && !this.destroyed) { this.cmd.uncertain.set(true); this.cmd.message.set(UNKNOWN_OUTCOME); }
      }
    }
  }
  private matches(receipt: MaintenanceReceipt, pending: PendingMaintenance): boolean {
    const actor = this.session.current(), expected = pending.expected;
    return receipt.clientId === pending.clientId && receipt.actorId === actor?.userId && receipt.actorEpoch === actor?.generation &&
      receipt.requestId === pending.reference.requestId && receipt.requestHash === pending.reference.requestHash && receipt.action === pending.action &&
      (!expected || receipt.reviewBasis === expected.reviewBasis &&
        (pending.action === 'ROLL_FORWARD' ? receipt.result.periodId !== expected.periodId : receipt.result.periodId === expected.periodId) &&
        (pending.action !== 'APPROVE_RESTATEMENT' || receipt.result.restatementId === expected.restatementId)) &&
      (pending.action === 'ROLL_FORWARD' ? receipt.result.status === 'DRAFT' && receipt.result.restatementId === null :
        !!receipt.result.restatementId && receipt.result.status === (pending.action === 'CREATE_RESTATEMENT' ? 'SUBMITTED' : 'APPROVED'));
  }
  private accept(receipt: MaintenanceReceipt, pending: PendingMaintenance): void {
    this.receipt.set(receipt);
    if (!this.drafts.clear(this.scope(pending.clientId, pending.action).entity)) {
      this.cmd.uncertain.set(true); this.cmd.message.set('Exact receipt verified, but its tab checkpoint could not be cleared. Restore storage and look up the receipt again.'); return;
    }
    this.pending.update(list => list.filter(p => p.action !== pending.action)); this.absent.update(list => list.filter(a => a !== pending.action));
    this.cmd.uncertain.set(this.pending().length > 0); this.cmd.failed.set(false);
    this.cmd.message.set(pending.action === 'ROLL_FORWARD' ? 'Draft period and opening bridge created; a reviewer must approve the opening bridge separately.' :
      pending.action === 'CREATE_RESTATEMENT' ? 'Restatement request created; an independent reviewer must approve it.' : 'Restatement independently approved.');
  }
  async reconcile(action: MaintenanceAction): Promise<void> {
    const pending = this.pending().find(p => p.action === action);
    if (!pending || this.cmd.busy() || this.destroyed || !this.session.current()?.staff || pending.clientId !== this.clientId) return;
    this.preview.set(null); this.reviewed.set(false); this.absent.update(list => list.filter(a => a !== action));
    const valid = this.ticket(), operation = ++this.operation;
    this.cmd.busy.set(true);
    try {
      const result = await this.api.get(this.base() + '/receipts/' + pending.reference.requestId + '?requestHash=' + pending.reference.requestHash, decodeMaintenanceLookup);
      if (!valid()) return;
      if (result.found !== !!result.receipt) throw new Error('Inconsistent lookup');
      if (!result.found) {
        this.absent.update(list => [...list, action]); this.cmd.uncertain.set(true);
        this.cmd.message.set('No receipt was found at lookup time. A delayed command may still commit. The exact request identity is retained; prepare a fresh review of this same intent before any explicit resubmission.'); return;
      }
      if (!this.matches(result.receipt!, pending)) throw new Error('Wrong recovered receipt');
      this.accept(result.receipt!, pending);
    } catch {
      if (valid()) { this.cmd.uncertain.set(true); this.cmd.failed.set(true); this.cmd.message.set('Exact receipt lookup could not be verified. Keep the pending request; do not repeat the command.'); }
    } finally { if (operation === this.operation) this.cmd.busy.set(false); }
  }
}
