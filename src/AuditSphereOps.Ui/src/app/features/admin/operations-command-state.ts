import { DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { OperationAction, OperationInput, OperationPreview, OperationReceipt, decodeOperationInput, decodeOperationLookup, decodeOperationPreview,
  decodeOperationReceipt, decodeOperationState, matchesOperationTarget } from './operations-command-contracts';

const base = '/api/ui/operations/commands';
export function operationStateUrl(operationId: string | null = null, recoverySessionId: string | null = null): string {
  const query = new URLSearchParams(); if (operationId) query.set('operationId', operationId); if (recoverySessionId) query.set('recoverySessionId', recoverySessionId);
  return base + '/state' + (query.size ? '?' + query : '');
}
export class OperationsCommandState {
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly drafts = inject(TabDrafts);
  readonly preview = signal<OperationPreview | null>(null); readonly receipt = signal<OperationReceipt | null>(null);
  readonly pending = signal<PendingRequestReference | null>(null); readonly absent = signal(false);
  private readonly pendingIntent = signal<{ action: OperationAction; targetId: string | null } | null>(null);
  readonly reviewed = signal(false); readonly busy = signal(false); readonly message = signal(''); readonly failed = signal(false);
  private readonly storageUnavailable = signal(false); readonly uncertain = computed(() => !!this.pending() || this.storageUnavailable());
  private generation = 0; private operation = 0; private executing = false; private destroyed = false; private actorKey = '';
  private input: (() => unknown) | null = null; private fingerprint = ''; private previewCurrent: (() => boolean) | null = null;
  constructor(private readonly context: () => unknown | null, private readonly reload: () => void) {
    const navigation = inject(Router).events.subscribe(event => { if (event instanceof NavigationStart) this.invalidate(); });
    effect(() => {
      const data = this.context(), actor = this.session.current(), epoch = this.session.invalidation();
      untracked(() => {
        const key = `${actor?.firmId}:${actor?.userId}:${actor?.generation}:${epoch}:${actor?.staff}`;
        if (key !== this.actorKey) { this.actorKey = key; ++this.operation; this.executing = false; this.busy.set(false); this.pending.set(null); this.pendingIntent.set(null); this.receipt.set(null); this.storageUnavailable.set(false); }
        this.invalidate(true);
        if (data && actor?.staff) {
          const stored = this.drafts.readPendingRequest(this.scope());
          if (stored.state === 'ready') this.pending.set(stored.draft.value);
          this.storageUnavailable.set(stored.state === 'unavailable');
        }
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.invalidate(); navigation.unsubscribe(); });
  }
  private scope(basis = '0'.repeat(64)) { return { entity: `operations-command/${this.session.current()?.firmId}`, baseRevision: basis }; }
  private ready() { return !this.destroyed && !!this.context() && !!this.session.current()?.staff; }
  canPrepare(action: OperationAction, targetId: string | null = null): boolean {
    const pending = this.pending();
    const retainedIntent = this.pendingIntent();
    return this.ready() && !this.busy() && !this.storageUnavailable() && (!pending || this.absent() &&
      (!retainedIntent || retainedIntent.action === action && retainedIntent.targetId === targetId));
  }
  invalidate(preserveReceipt = false): void {
    ++this.generation; this.preview.set(null); this.reviewed.set(false); this.input = null; this.fingerprint = ''; this.previewCurrent = null;
    if (!preserveReceipt) this.receipt.set(null);
    if (!this.executing) { ++this.operation; this.busy.set(false); }
  }
  inputChanged(): void { this.invalidate(true); }
  private ticket() {
    const generation = this.generation, context = this.context(), actor = this.session.current(), epoch = this.session.invalidation();
    return () => !this.destroyed && generation === this.generation && context === this.context() && actor === this.session.current() && epoch === this.session.invalidation();
  }
  async prepare(readInput: () => unknown): Promise<void> {
    let input: OperationInput;
    try { input = decodeOperationInput(readInput(), 'intent'); } catch { this.failed.set(true); this.message.set('Complete this operation’s exact required fields before preview.'); return; }
    if (!this.canPrepare(input.action, input.operationId ?? input.recoverySessionId)) return;
    this.invalidate(); const valid = this.ticket(), operation = ++this.operation, fingerprint = JSON.stringify(readInput());
    const retained = this.pending(); this.busy.set(true); this.failed.set(false); this.message.set(''); let reauthorize = false, readingState = true;
    try {
      const state = await this.api.get(operationStateUrl(input.operationId, input.recoverySessionId), decodeOperationState);
      if (!valid()) return; readingState = false;
      if (!matchesOperationTarget(input, state.before)) throw new Error('Wrong target');
      const permitted = { RETRY: state.canRetry, CANCEL: state.canCancel, LIFT_QUARANTINE: state.canLiftQuarantine, APPROVE_RECOVERY_RESTART: state.canApproveRecoveryRestart }[input.action];
      if (!permitted) { this.failed.set(true); this.message.set([...state.blockers, ...({ RETRY: state.retryBlockers, CANCEL: state.cancelBlockers, LIFT_QUARANTINE: state.liftQuarantineBlockers, APPROVE_RECOVERY_RESTART: state.approveRecoveryRestartBlockers }[input.action])].join(' ') || 'This action is not available for the current operation or recovery session.'); return; }
      const requestId = retained?.requestId ?? crypto.randomUUID();
      const result = await this.api.command(base + '/preview', { requestId, input, reviewBasis: state.reviewBasis, expectedRequestHash: null, reviewed: false });
      if (!valid()) return;
      if (!result.ok) { reauthorize = !result.unknown && [401, 403, 404].includes(result.status); this.failed.set(true); this.message.set(result.message); return; }
      const preview = decodeOperationPreview(result.value, 'preview');
      if (preview.requestId !== requestId || preview.reviewBasis !== state.reviewBasis || JSON.stringify(preview.before) !== JSON.stringify(state.before) ||
        JSON.stringify(preview.canonicalInput) !== JSON.stringify(input) || JSON.stringify(readInput()) !== fingerprint) throw new Error('Changed preview');
      if (retained && preview.requestHash !== retained.requestHash) {
        this.failed.set(true); this.message.set('This pending identity belongs to a different original action or target. Re-enter the exact original intent to check it; do not submit a changed request.'); return;
      }
      this.pendingIntent.set({ action: input.action, targetId: input.operationId ?? input.recoverySessionId });
      this.preview.set(preview); this.previewCurrent = valid; this.input = readInput; this.fingerprint = fingerprint;
      if (!preview.canProceed) { this.failed.set(true); this.message.set(preview.blockers.join(' ')); }
    } catch { if (valid()) { reauthorize = readingState; this.failed.set(true); this.message.set('The exact operation preview could not be verified. Refresh and review again.'); } }
    finally { if (operation === this.operation) { this.busy.set(false); if (reauthorize && valid()) this.reload(); } }
  }
  private matches(receipt: OperationReceipt, reference: PendingRequestReference): boolean {
    return receipt.actorId === this.session.current()?.userId && receipt.actorEpoch === this.session.current()?.generation &&
      receipt.requestId === reference.requestId && receipt.requestHash === reference.requestHash;
  }
  private accept(receipt: OperationReceipt): boolean {
    this.receipt.set(receipt);
    if (!this.drafts.clear(this.scope().entity)) { this.message.set('Receipt verified, but its tab checkpoint could not be cleared. Restore storage and look up the original receipt again.'); return false; }
    this.pending.set(null); this.pendingIntent.set(null); this.absent.set(false); this.failed.set(false); this.message.set('Original operator command receipt verified. This is not proof of a successful provider effect.'); return true;
  }
  async execute(): Promise<void> {
    const preview = this.preview();
    if (!this.ready() || !this.previewCurrent?.() || this.busy() || !preview?.canProceed || !this.reviewed() || !this.input || JSON.stringify(this.input()) !== this.fingerprint || this.storageUnavailable() ||
      this.pending() && (this.pending()!.requestId !== preview.requestId || this.pending()!.requestHash !== preview.requestHash || !this.absent())) return;
    const reference = { requestId: preview.requestId, requestHash: preview.requestHash };
    if (!this.drafts.save(this.scope(preview.reviewBasis), reference, pendingRequestReference, true)) { this.failed.set(true); this.message.set('The request reference could not be saved. No command was sent.'); return; }
    const valid = this.ticket(), operation = ++this.operation; let refresh = false;
    this.pendingIntent.set({ action: preview.canonicalInput.action, targetId: preview.canonicalInput.operationId ?? preview.canonicalInput.recoverySessionId });
    this.pending.set(reference); this.executing = true; this.busy.set(true); this.reviewed.set(false); this.absent.set(false);
    try {
      const result = await this.api.command(base + '/actions', { requestId: preview.requestId, input: preview.canonicalInput, reviewBasis: preview.reviewBasis, expectedRequestHash: preview.requestHash, reviewed: true });
      if (!valid()) return; this.preview.set(null);
      if (!result.ok) { this.failed.set(true); this.message.set(result.message + ' Look up the retained original request before another command.'); return; }
      const receipt = decodeOperationReceipt(result.value, 'receipt');
      if (!this.matches(receipt, reference) || receipt.action !== preview.canonicalInput.action || JSON.stringify(receipt.preview) !== JSON.stringify(preview)) throw new Error('Wrong original receipt');
      refresh = this.accept(receipt);
    } catch { if (valid()) { this.failed.set(true); this.message.set('The operator command outcome is not confirmed. Keep the original request and look up its receipt; do not resend blindly.'); } }
    finally { if (operation === this.operation) { this.executing = false; this.busy.set(false); if (refresh && valid()) this.reload(); } }
  }
  async reconcile(): Promise<void> {
    const reference = this.pending(); if (!reference || !this.ready() || this.busy()) return;
    this.invalidate(true); const valid = this.ticket(), operation = ++this.operation; let refresh = false, reauthorize = false;
    this.busy.set(true); this.absent.set(false);
    try {
      // Lookup is actor-owned and authorized by the endpoint, independent of the worker's current head.
      const lookup = await this.api.get(base + '/receipts/' + reference.requestId + '?requestHash=' + reference.requestHash, decodeOperationLookup);
      if (!valid()) return;
      if (!lookup.found) { this.absent.set(true); this.message.set('No receipt exists at lookup time. A delayed command may still commit. Retain the same ID/hash and re-enter the exact original intent for fresh review before explicit resubmission.'); return; }
      if (!this.matches(lookup.receipt!, reference)) throw new Error('Wrong actor-owned receipt');
      refresh = this.accept(lookup.receipt!);
    } catch { if (valid()) { reauthorize = true; this.failed.set(true); this.message.set('Original receipt lookup could not be verified. The retained request remains blocked.'); } }
    finally { if (operation === this.operation) { this.busy.set(false); if ((refresh || reauthorize) && valid()) this.reload(); } }
  }
}
