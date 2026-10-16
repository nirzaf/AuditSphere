import { DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { WorkpaperAction, WorkpaperCommandFields, WorkpaperCommandPreview, WorkpaperCommandReceipt, WorkpaperCommandSnapshot,
  decodeWorkpaperCommandFields, decodeWorkpaperCommandLookup, decodeWorkpaperCommandPreview, decodeWorkpaperCommandReceipt,
  decodeWorkpaperCommandState, decodeWorkpaperSaveIntent, sameWorkpaperFields, sameWorkpaperSnapshot, workpaperActions } from './workpaper-command-contracts';

type Context = { id: string; engagementId: string; revision: string };
type EditorDraft = { draftId: string | null; draftRevision: string; lastSaveId: string; baseWorkpaperRevision: string; baseInputGeneration: string; basePolicyGeneration: string };
type Pending = { workpaperId: string; action: WorkpaperAction; reference: PendingRequestReference;
  fields?: WorkpaperCommandFields; before?: WorkpaperCommandSnapshot };
const unknown = 'The workpaper command outcome is not confirmed. Look up the original actor-owned receipt before continuing; do not repeat blindly.';

/** Owns one publication at a time. Only request identity/hash is stored; content and assent remain in memory. */
export class WorkpaperCommandState {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  readonly preview = signal<WorkpaperCommandPreview | null>(null);
  readonly receipt = signal<WorkpaperCommandReceipt | null>(null);
  readonly pending = signal<Pending[]>([]);
  readonly absent = signal(false);
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly conflict = signal(false);
  private readonly storageUnavailable = signal(false);
  readonly uncertain = computed(() => this.pending().length > 0 || this.storageUnavailable());
  private generation = 0;
  private operation = 0;
  private destroyed = false;
  private scopeKey = '';
  private input: (() => unknown) | null = null;
  private inputGeneration = 0;
  private fingerprint = '';
  constructor(private readonly id: () => string | null, private readonly context: () => Context | null,
    private readonly draft: () => EditorDraft | null) {
    // Synchronous parameter notifications fence A-B-A even when Angular effects coalesce.
    const params = inject(ActivatedRoute).paramMap.subscribe(() => {
      this.invalidate(); this.receipt.set(null); this.conflict.set(false); this.message.set(''); this.failed.set(false);
    });
    effect(() => {
      const id = this.id(), source = this.context(), actor = this.session.current(), epoch = this.session.invalidation();
      untracked(() => {
        const key = `${id}/${actor?.firmId}/${actor?.userId}/${actor?.generation}/${actor?.staff}/${epoch}`;
        if (key !== this.scopeKey) {
          this.scopeKey = key; this.pending.set([]); this.receipt.set(null); this.storageUnavailable.set(false); this.conflict.set(false);
          this.message.set(''); this.failed.set(false);
        }
        this.invalidate();
        if (id && source?.id === id && actor?.staff) this.restore(id);
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.invalidate(); params.unsubscribe(); });
  }
  invalidate(): void {
    ++this.generation; ++this.operation; this.busy.set(false); this.resetReview(); this.absent.set(false);
  }
  inputChanged(): void { ++this.inputGeneration; this.resetReview(); }
  resetReview(): void { this.preview.set(null); this.reviewed.set(false); this.input = null; this.fingerprint = ''; }
  ticket(): () => boolean {
    const generation = this.generation, actor = this.session.current(), epoch = this.session.invalidation(), id = this.id(), source = this.context(), draftId = this.draft()?.draftId;
    return () => !this.destroyed && generation === this.generation && actor === this.session.current() && epoch === this.session.invalidation() &&
      id === this.id() && source === this.context() && draftId === this.draft()?.draftId;
  }
  ready(): boolean { return !this.destroyed && !!this.session.current()?.staff && !!this.id() && this.context()?.id === this.id(); }
  private base(id = this.id()) { return `/api/ui/audit/workpapers/${id}/commands`; }
  private scope(id: string, action: WorkpaperAction) { return { entity: `workpaper-command-request/${id}/${action}`, baseRevision: '0'.repeat(64) }; }
  private restore(id: string): void {
    let unavailable = false;
    for (const action of workpaperActions) {
      const saved = this.drafts.readPendingRequest(this.scope(id, action));
      if (saved.state === 'ready' && !this.pending().some(p => p.action === action)) this.pending.update(list => [...list, { workpaperId: id, action, reference: saved.draft.value }]);
      if (saved.state === 'unavailable') unavailable = true;
    }
    this.storageUnavailable.set(unavailable);
    if (this.uncertain()) this.message.set(unavailable
      ? 'Tab request storage is unavailable. Restore access to tab storage and refresh the current target before sending another command.' : unknown);
  }
  canPrepare(action: WorkpaperAction): boolean {
    return this.ready() && !this.busy() && (!this.uncertain() || !this.storageUnavailable() && this.pending().length === 1 &&
      this.pending()[0].action === action && this.pending()[0].workpaperId === this.id() && this.absent());
  }
  private readFields(input: unknown): WorkpaperCommandFields | null {
    try { return decodeWorkpaperCommandFields(input, 'intent'); }
    catch { this.failed.set(true); this.message.set('Complete the exact selected workpaper intent before continuing.'); return null; }
  }
  private assertState(s: WorkpaperCommandSnapshot, fields?: WorkpaperCommandFields): void {
    const current = this.context(), draft = this.draft();
    if (!current || s.workpaperId !== this.id() || s.engagementId !== current.engagementId || s.revision !== current.revision)
      throw new Error('Wrong workpaper target or revision');
    if (!fields) return;
    if (!draft || (s.draft?.lifecycle === 'ACTIVE' ? s.draft.id : null) !== draft.draftId) throw new Error('Changed draft identity');
    const expected = fields.save?.expectedDraftRevision ?? fields.submit?.expectedDraftRevision ?? fields.discard?.expectedDraftRevision;
    const revision = s.draft?.lifecycle === 'ACTIVE' ? s.draft.draftRevision : '0';
    if (expected !== revision || revision !== draft.draftRevision) throw new Error('Changed acknowledged draft revision');
    if (fields.save && (fields.save.baseWorkpaperRevision !== s.revision || fields.save.baseInputGeneration !== s.inputGeneration || fields.save.basePolicyGeneration !== s.policyGeneration))
      throw new Error('Changed save safety basis');
  }
  private persist(p: Pending): boolean {
    if (!this.drafts.save({ ...this.scope(p.workpaperId, p.action), baseRevision: p.reference.requestHash }, p.reference, pendingRequestReference, true)) {
      this.failed.set(true); this.message.set('The request checkpoint could not be saved. No workpaper command was sent.'); return false;
    }
    this.pending.set([p]); this.absent.set(false); return true;
  }
  private clear(p: Pending): boolean {
    if (!this.drafts.clear(this.scope(p.workpaperId, p.action).entity)) {
      this.failed.set(true); this.message.set('The request checkpoint could not be cleared. Keep the identity and look up its receipt again.'); return false;
    }
    this.pending.update(list => list.filter(item => item !== p)); this.absent.set(false); return true;
  }
  private matches(r: WorkpaperCommandReceipt, p: Pending, source: WorkpaperCommandSnapshot): boolean {
    const before = r.commandEvent.before;
    if (r.actorId !== this.session.current()?.userId || r.actorEpoch !== this.session.current()?.generation || r.workpaperId !== p.workpaperId ||
      r.action !== p.action || r.requestId !== p.reference.requestId || r.requestHash !== p.reference.requestHash ||
      before.clientId !== source.clientId || before.engagementId !== source.engagementId || p.before && !sameWorkpaperSnapshot(before, p.before)) return false;
    if (p.fields?.save) {
      const fields = p.fields.save, d = r.commandEvent.after.draft!;
      return fields.workPerformed === d.workPerformed && fields.conclusion === d.conclusion && fields.baseWorkpaperRevision === d.baseWorkpaperRevision &&
        fields.baseInputGeneration === d.baseInputGeneration && fields.basePolicyGeneration === d.basePolicyGeneration &&
        fields.expectedDraftRevision === (before.draft?.lifecycle === 'ACTIVE' ? before.draft.draftRevision : '0');
    }
    return !p.fields || !!r.preview && sameWorkpaperFields(p.fields, r.preview.canonicalInput);
  }
  private accept(r: WorkpaperCommandReceipt, p: Pending): WorkpaperCommandReceipt | null {
    this.receipt.set(r); if (!this.clear(p)) return null;
    this.failed.set(false); this.conflict.set(false); this.message.set('Original actor-owned workpaper receipt verified.'); return r;
  }
  private refused(message: string, code: string, status: number): void {
    this.failed.set(true); this.message.set(message);
    this.conflict.set(status === 409 || ['revision.stale', 'generation.stale', 'draft.target-changed', 'draft.conflict'].includes(code));
  }
  async save(input: unknown): Promise<WorkpaperCommandReceipt | null> {
    if (!this.canPrepare('SAVE_DRAFT')) return null;
    const fields = this.readFields(input);
    if (!fields?.save || !this.canPrepare('SAVE_DRAFT')) return null;
    const retained = this.pending()[0];
    // An in-memory original request must never be replaced with later typed content.
    const exact = retained?.fields ?? fields;
    const valid = this.ticket(), operation = ++this.operation, id = this.id()!;
    this.busy.set(true); this.message.set('Preparing the exact draft save…');
    try {
      const state = await this.api.get(this.base(id) + '/state', decodeWorkpaperCommandState);
      if (!valid()) return null;
      this.assertState(state.workpaper, exact);
      if (!state.canSaveDraft) { this.failed.set(true); this.message.set(state.blockers.join(' ') || 'Saving this draft is unavailable.'); return null; }
      const request = { requestId: retained?.reference.requestId ?? crypto.randomUUID(), fields: exact, reviewBasis: null, expectedRequestHash: null, reviewed: false };
      const prepared = await this.api.command(this.base(id) + '/prepare-save', request);
      if (!valid()) return null;
      if (!prepared.ok) { this.refused(prepared.message, prepared.code, prepared.status); return null; }
      const intent = decodeWorkpaperSaveIntent(prepared.value, 'save intent');
      if (intent.requestId !== request.requestId || intent.canonicalInput.action !== 'SAVE_DRAFT' || !sameWorkpaperFields(intent.canonicalInput, exact) ||
        retained && intent.requestHash !== retained.reference.requestHash) throw new Error('Changed original save intent');
      const pending: Pending = { workpaperId: id, action: 'SAVE_DRAFT', fields: exact, before: state.workpaper,
        reference: { requestId: intent.requestId, requestHash: intent.requestHash } };
      if (!this.persist(pending)) return null;
      const result = await this.api.command(this.base(id) + '/actions', { ...request, fields: intent.canonicalInput, expectedRequestHash: intent.requestHash });
      if (!valid()) return null;
      if (!result.ok) {
        this.refused(result.message, result.code, result.status);
        if (!result.unknown && !retained) this.clear(pending);
        else this.message.update(message => message + ' ' + unknown);
        return null;
      }
      const receipt = decodeWorkpaperCommandReceipt(result.value, 'save receipt');
      if (!this.matches(receipt, pending, state.workpaper)) throw new Error('Wrong original save receipt');
      return this.accept(receipt, pending);
    } catch { if (valid()) { this.failed.set(true); this.message.set(this.uncertain() ? unknown : 'The exact draft save could not be prepared or verified. Refresh and review the current target.'); } return null; }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
  async prepare(input: () => unknown): Promise<boolean> {
    if (!this.ready() || this.busy()) return false;
    const fields = this.readFields(input());
    if (!fields || fields.save || !this.canPrepare(fields.action)) return false;
    const retained = this.pending()[0], fingerprint = JSON.stringify(input());
    this.resetReview(); const ticket = this.ticket(), inputGeneration = this.inputGeneration,
      valid = () => ticket() && inputGeneration === this.inputGeneration, operation = ++this.operation, id = this.id()!; this.busy.set(true);
    try {
      const state = await this.api.get(this.base(id) + '/state', decodeWorkpaperCommandState);
      if (!valid()) return false;
      this.assertState(state.workpaper, fields);
      if (!(fields.submit ? state.canSubmit : state.canDiscardDraft)) { this.failed.set(true); this.message.set(state.blockers.join(' ') || 'This final workpaper action is unavailable.'); return false; }
      const request = { requestId: retained?.reference.requestId ?? crypto.randomUUID(), fields, reviewBasis: state.reviewBasis, expectedRequestHash: null, reviewed: false };
      const result = await this.api.command(this.base(id) + '/preview', request);
      if (!valid()) return false;
      if (!result.ok) { this.refused(result.message, result.code, result.status); return false; }
      const preview = decodeWorkpaperCommandPreview(result.value, 'preview');
      if (preview.requestId !== request.requestId || preview.reviewBasis !== request.reviewBasis || !sameWorkpaperFields(preview.canonicalInput, fields) ||
        !sameWorkpaperSnapshot(preview.before, state.workpaper) || fingerprint !== JSON.stringify(input()) || retained && preview.requestHash !== retained.reference.requestHash)
        throw new Error('Changed exact final action');
      this.failed.set(false); this.message.set(''); this.preview.set(preview); this.input = input; this.fingerprint = fingerprint; return true;
    } catch { if (valid()) { this.failed.set(true); this.message.set('The exact workpaper preview could not be verified. Refresh and review again.'); } return false; }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
  async execute(): Promise<WorkpaperCommandReceipt | null> {
    const preview = this.preview();
    if (!preview?.canProceed || !this.reviewed() || !this.input || this.fingerprint !== JSON.stringify(this.input()) || !this.canPrepare(preview.canonicalInput.action)) return null;
    const retained = this.pending()[0], valid = this.ticket(), operation = ++this.operation;
    const pending: Pending = { workpaperId: preview.before.workpaperId, action: preview.canonicalInput.action, fields: preview.canonicalInput, before: preview.before,
      reference: { requestId: preview.requestId, requestHash: preview.requestHash } };
    if (retained && (retained.reference.requestId !== preview.requestId || retained.reference.requestHash !== preview.requestHash) || !this.persist(pending)) return null;
    this.busy.set(true); this.reviewed.set(false);
    try {
      const result = await this.api.command(this.base(pending.workpaperId) + '/actions', { requestId: preview.requestId, fields: preview.canonicalInput,
        reviewBasis: preview.reviewBasis, expectedRequestHash: preview.requestHash, reviewed: true });
      if (!valid()) return null;
      this.resetReview();
      if (!result.ok) {
        this.refused(result.message, result.code, result.status);
        if (!result.unknown && !retained) this.clear(pending);
        else this.message.update(message => message + ' ' + unknown);
        return null;
      }
      const receipt = decodeWorkpaperCommandReceipt(result.value, 'receipt');
      if (!this.matches(receipt, pending, preview.before)) throw new Error('Wrong final action receipt');
      return this.accept(receipt, pending);
    } catch { if (valid()) { this.failed.set(true); this.message.set(unknown); this.resetReview(); } return null; }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
  async reconcile(): Promise<WorkpaperCommandReceipt | null> {
    const pending = this.pending()[0];
    if (!pending || this.pending().length !== 1 || !this.ready() || pending.workpaperId !== this.id() || this.busy()) return null;
    this.resetReview(); this.absent.set(false); const valid = this.ticket(), operation = ++this.operation; this.busy.set(true);
    try {
      const state = await this.api.get(this.base() + '/state', decodeWorkpaperCommandState);
      if (!valid()) return null;
      // Recovery checks scope, not current revision/content: original receipts survive later saves and submissions.
      if (state.workpaper.workpaperId !== pending.workpaperId || state.workpaper.engagementId !== this.context()?.engagementId) throw new Error('Wrong recovery target');
      const lookup = await this.api.get(this.base() + '/receipts/' + pending.reference.requestId + '?requestHash=' + pending.reference.requestHash, decodeWorkpaperCommandLookup);
      if (!valid()) return null;
      if (!lookup.found) {
        this.absent.set(true); this.failed.set(false); this.message.set('No receipt was found at lookup time. A delayed command may still commit. Retain this identity and explicitly prepare the same request before retrying.'); return null;
      }
      if (!this.matches(lookup.receipt!, pending, state.workpaper)) throw new Error('Wrong recovered receipt');
      return this.accept(lookup.receipt!, pending);
    } catch { if (valid()) { this.failed.set(true); this.message.set('The original workpaper receipt could not be verified. Retain the request identity and do not repeat blindly.'); } return null; }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
}
