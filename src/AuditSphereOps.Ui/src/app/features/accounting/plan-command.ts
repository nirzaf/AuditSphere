import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom, map } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts, PendingRequestReference, pendingRequestReference } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { decodePlanReview } from './adjustment-plans';
import { decodePlanCreation, decodePlanCommandPreview, decodePlanCommandReceipt, decodePlanCommandLookup,
  planFields, PlanFields } from './plan-command-contracts';

const empty = (): PlanFields => ({ reason: '', evidenceReference: '', journals: [] });
type Intent = PlanFields & { requestId: string; action: 'CREATE' | 'FINALIZE'; reviewBasis: string; reviewed: boolean };
@Component({ selector: 'audit-plan-command', imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './plan-command.html' })
export class PlanCommand implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly id = routeGuid();
  readonly action = toSignal(inject(ActivatedRoute).data.pipe(map(d => d['action'] === 'CREATE' ? 'CREATE' as const : 'FINALIZE' as const)), { initialValue: 'CREATE' as const });
  readonly page = signal(0);
  readonly options = this.api.resource(() => this.action() === 'CREATE' && this.id() ? this.base() + '?page=' + this.page() : null,
    (raw, path) => { const c = decodePlanCreation(raw, path); if (c.source.datasetId !== this.id()) throw new Error('Wrong source'); return c; });
  readonly plan = this.api.resource(() => this.action() === 'FINALIZE' && this.id() ? '/api/ui/accounting/adjustment-plans/' + this.id() : null,
    (raw, path) => { const c = decodePlanReview(raw, path); if (c.id !== this.id()) throw new Error('Wrong plan'); return c; });
  readonly context = computed(() => {
    if (this.action() === 'CREATE') { const c = this.options.data(); return c ? {
      datasetId: c.source.datasetId, revision: c.source.datasetRevision, digest: c.source.datasetDigest,
      period: c.source.periodCode, book: c.source.bookCode, currency: c.source.currency, basis: c.source.basis,
      reviewBasis: c.reviewBasis, available: c.source.canCreate, blocker: c.source.blocker } : null; }
    const p = this.plan.data(); return p ? { datasetId: p.datasetId, revision: p.sourceRevision,
      digest: p.sourceDigest, period: p.periodCode, book: p.bookCode, currency: p.currency, basis: p.basis,
      reviewBasis: p.reviewBasis, available: p.status === 'Draft' && p.blockers.length === 0,
      blocker: p.status !== 'Draft' ? 'Only a draft plan can be finalized.' : p.blockers[0] ?? null } : null;
  });
  readonly loading = computed(() => this.action() === 'CREATE' ? this.options.loading() : this.plan.loading());
  readonly error = computed(() => this.action() === 'CREATE' ? this.options.error() : this.plan.error());
  readonly model = signal(empty());
  readonly fields = form(this.model, p => { required(p.reason); maxLength(p.reason, 4000); required(p.evidenceReference); maxLength(p.evidenceReference, 2000); });
  readonly preview = signal<ReturnType<typeof decodePlanCommandPreview> | null>(null);
  readonly receipt = signal<ReturnType<typeof decodePlanCommandReceipt> | null>(null);
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly pending = signal<PendingRequestReference | null>(null);
  readonly draftAvailable = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  private readonly baseline = signal(JSON.stringify(empty()));
  readonly dirty = computed(() => JSON.stringify(this.model()) !== this.baseline());
  private generation = 0;
  private lastBasis = '';
  private request: Intent | null = null;
  private checkedAbsent = false;
  constructor() {
    effect(() => { this.id(); this.action(); this.session.invalidation(); untracked(() => {
      this.generation++; this.lastBasis = ''; this.page.set(0); this.model.set(empty()); this.baseline.set(JSON.stringify(empty()));
      this.preview.set(null); this.reviewed.set(false); this.receipt.set(null); this.busy.set(false); this.uncertain.set(false);
      this.pending.set(null); this.request = null; this.checkedAbsent = false; this.draftAvailable.set(false); this.message.set(''); this.failed.set(false);
    }); });
    effect(() => { const c = this.context(); untracked(() => {
      this.preview.set(null); this.reviewed.set(false);
      if (!c) { if (this.error()) { this.generation++; this.model.set(empty()); this.receipt.set(null); } return; }
      if (this.lastBasis && this.lastBasis !== c.reviewBasis) { this.generation++; this.model.set(empty()); this.baseline.set(JSON.stringify(empty())); this.receipt.set(null); }
      this.lastBasis = c.reviewBasis;
      this.draftAvailable.set(this.drafts.read(this.scope(), planFields).state === 'ready');
      const p = this.drafts.readPendingRequest(this.scope(true));
      if (p.state === 'ready') { this.pending.set(p.draft.value); this.uncertain.set(true); this.checkedAbsent = false; this.message.set(UNKNOWN_OUTCOME); }
    }); });
    effect(() => { this.model(); untracked(() => { this.preview.set(null); this.reviewed.set(false); }); });
  }
  private base() { return this.action() === 'CREATE' ? '/api/ui/datasets/' + this.id() + '/adjustment-plans' : '/api/ui/accounting/adjustment-plans/' + this.id() + '/finalization'; }
  private scope(pending = false) { return { entity: 'plan-' + this.action().toLowerCase() + (pending ? '-request/' : '-fields/') + this.id(), baseRevision: this.context()?.reviewBasis ?? '' }; }
  private intent(id: string, basis: string): Intent { return { ...structuredClone(this.model()), requestId: id, action: this.action(), reviewBasis: basis, reviewed: false }; }
  selected(id: string) { return this.model().journals.some(j => j.journalId === id); }
  select(id: string, revision: number, checked: boolean) {
    if (this.busy() || this.uncertain()) return;
    const row = this.options.data()?.journals.find(j => j.journalId === id && j.revision === revision);
    if (!row?.canInclude || (checked && this.model().journals.length >= 100)) return;
    this.model.update(m => ({ ...m, journals: checked ? [...m.journals.filter(j => j.journalId !== id), { journalId: id, revision }] : m.journals.filter(j => j.journalId !== id) }));
  }
  async prepare() {
    const c = this.context(), generation = this.generation;
    if (!c?.available || this.busy() || this.receipt() || !this.fields().valid() || (this.uncertain() && !this.checkedAbsent)) return;
    this.preview.set(null); this.reviewed.set(false); this.busy.set(true); this.failed.set(false);
    try {
      const r = this.intent(this.pending()?.requestId ?? crypto.randomUUID(), c.reviewBasis);
      const result = await this.api.command(this.base() + '/preview', r); if (generation !== this.generation) return;
      if (!result.ok) { this.message.set(result.message); this.failed.set(true); return; }
      const p = decodePlanCommandPreview(result.value);
      if (p.action !== this.action() || p.reviewBasis !== c.reviewBasis || this.context()?.reviewBasis !== c.reviewBasis ||
        (this.pending() && p.requestHash !== this.pending()!.requestHash) ||
        (p.action === 'CREATE' && (p.journals.length !== r.journals.length || r.journals.some(j => !p.journals.some(x => x.journalId === j.journalId && x.revision === j.revision)))))
        throw new Error('The reviewed source, selection or pending intent changed. Refresh or check its retained receipt.');
      this.request = r; this.preview.set(p); this.message.set('');
    } catch { if (generation === this.generation) { this.message.set('The plan preview could not be verified. Refresh the current context.'); this.failed.set(true); } }
    finally { if (generation === this.generation) this.busy.set(false); }
  }
  async execute() {
    const c = this.context(), p = this.preview(), r = this.request, generation = this.generation;
    if (!c?.available || !p?.canProceed || !r || !this.reviewed() || this.busy() || this.receipt() || r.reviewBasis !== c.reviewBasis ||
      JSON.stringify(this.intent(r.requestId, r.reviewBasis)) !== JSON.stringify(r)) return;
    this.reviewed.set(false); this.failed.set(false);
    const pending = { requestId: r.requestId, requestHash: p.requestHash };
    if (!this.drafts.save(this.scope(), this.model(), planFields, true) || !this.drafts.save(this.scope(true), pending, pendingRequestReference, true)) {
      this.message.set('This browser tab cannot retain a recovery reference. No command was sent.'); this.failed.set(true); return;
    }
    this.pending.set(pending); this.busy.set(true);
    const result = await this.api.command(this.base(), { ...r, reviewed: true }); if (generation !== this.generation) return;
    this.busy.set(false); this.preview.set(null);
    if (!result.ok) { this.message.set(result.message); this.failed.set(true); if (result.unknown) { this.uncertain.set(true); this.checkedAbsent = false; } else this.clearPending(); return; }
    try {
      const receipt = decodePlanCommandReceipt(result.value);
      if (!this.matches(receipt, pending) || (r.action === 'FINALIZE' && (receipt.resultHash !== p.resultHash || receipt.debits !== p.debits || receipt.credits !== p.credits || receipt.appliedCount !== p.appliedCount))) throw new Error('Unconfirmed receipt');
      this.receipt.set(receipt); this.clearPending(); this.drafts.clear(this.scope().entity); this.baseline.set(JSON.stringify(this.model()));
      this.message.set(r.action === 'CREATE' ? 'Draft plan and reviewed membership retained.' : 'Reviewed calculation and immutable plan result retained.');
    } catch { this.uncertain.set(true); this.message.set(UNKNOWN_OUTCOME); }
  }
  private matches(r: ReturnType<typeof decodePlanCommandReceipt>, p: PendingRequestReference) { return r.action === this.action() &&
    r.actorId === this.session.current()?.userId && r.requestId === p.requestId && r.requestHash === p.requestHash &&
    (this.action() === 'CREATE' ? r.datasetId === this.id() : r.planId === this.id() && r.datasetId === this.context()?.datasetId); }
  private clearPending() { this.drafts.clear(this.scope(true).entity); this.pending.set(null); this.uncertain.set(false); this.checkedAbsent = false; this.request = null; }
  async reconcile() {
    const p = this.pending(), generation = this.generation; if (!p || this.busy() || !this.context()) return;
    this.busy.set(true); this.preview.set(null); this.reviewed.set(false);
    try {
      const r = await this.api.get(this.base() + '/receipts/' + p.requestId + '?requestHash=' + p.requestHash, decodePlanCommandLookup);
      if (generation !== this.generation) return;
      if (r.found && r.receipt && this.matches(r.receipt, p)) { this.receipt.set(r.receipt); this.checkedAbsent = false; this.message.set('The retained receipt confirms this request. Acknowledge it before leaving.'); }
      else if (!r.found && !r.receipt) { this.checkedAbsent = true; this.message.set('No receipt is currently retained. Restore the exact original fields and freshly preview only the identical request.'); }
      else throw new Error('Unverified receipt');
    } catch { if (generation === this.generation) this.message.set('Receipt verification is unavailable. Keep the request reference before another command.'); }
    finally { if (generation === this.generation) this.busy.set(false); }
  }
  acknowledge() { const r = this.receipt(), p = this.pending(); if (!r || !p || !this.matches(r, p)) return;
    this.clearPending(); this.drafts.clear(this.scope().entity); this.baseline.set(JSON.stringify(this.model())); }
  saveDraft() { const ok = !this.busy() && !this.uncertain() && !this.receipt() && this.drafts.save(this.scope(), this.model(), planFields);
    this.message.set(ok ? 'Plan fields saved in this tab; fresh review is required after restoring.' : 'The tab draft could not be saved.'); return ok; }
  restoreDraft() { if (this.busy() || this.receipt()) return; const r = this.drafts.read(this.scope(), planFields);
    if (r.state !== 'ready') { this.message.set('The saved plan fields are stale or unavailable.'); return; }
    this.model.set(r.draft.value); this.preview.set(null); this.reviewed.set(false); this.draftAvailable.set(false); }
  discardDraft() { if (this.busy() || this.uncertain() || this.receipt()) return false; this.model.set(JSON.parse(this.baseline()));
    this.drafts.clear(this.scope().entity); this.preview.set(null); this.reviewed.set(false); return true; }
  async refresh() { if (this.busy() || (!this.uncertain() && this.dirty() && !(await this.confirmNavigation()))) return;
    this.preview.set(null); this.reviewed.set(false); (this.action() === 'CREATE' ? this.options : this.plan).reload(); }
  async confirmNavigation() { if (this.busy() || this.uncertain()) { this.message.set('Resolve the retained receipt before leaving an unconfirmed request.'); return false; }
    if (!this.dirty()) return true; const d = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    return d === 'save' ? this.saveDraft() : d === 'discard' ? this.discardDraft() : false; }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) { if (this.busy() || this.uncertain() || this.dirty()) { e.preventDefault(); e.returnValue = ''; } }
}
