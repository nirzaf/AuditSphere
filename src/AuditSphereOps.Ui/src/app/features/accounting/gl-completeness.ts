import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import {
  CompletenessDraft,
  CompletenessPlan,
  CompletenessReview,
  CompletenessWorkspace,
  decodeCompletenessDraft,
  decodeCompletenessPlan,
  decodeCompletenessReview,
  decodeCompletenessWorkspace,
} from './gl-completeness-contracts';

const empty = (): CompletenessDraft => ({
  trialBalanceId: '',
  openingId: '',
  bridgeId: '',
  action: 'prepare',
  evidenceReference: '',
});
@Component({
  selector: 'audit-gl-completeness',
  imports: [...SHARED, MatButtonModule, RouterLink],
  templateUrl: './gl-completeness.html',
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }
      section {
        margin-block: 1.5rem;
      }
      fieldset {
        border: 0;
        padding: 0;
        min-width: 0;
      }
      label {
        display: block;
        margin-block: 1rem;
      }
      select,
      textarea {
        display: block;
        box-sizing: border-box;
        max-width: 100%;
        width: 100%;
        padding: 0.7rem;
      }
      dl {
        display: grid;
        grid-template-columns: minmax(100px, 180px) minmax(0, 1fr);
        gap: 0.6rem;
      }
      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }
      dt {
        font-weight: 600;
      }
      .identity {
        overflow-wrap: anywhere;
      }
      .toolbar {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
      }
      .warning {
        padding: 1rem;
        border-inline-start: 3px solid var(--mat-sys-error);
      }
      .table-wrap {
        overflow: auto;
        max-width: 100%;
      }
      table {
        border-collapse: collapse;
        width: 100%;
      }
      th,
      td {
        padding: 0.7rem;
        text-align: start;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
        white-space: nowrap;
      }
      caption {
        text-align: start;
        font-weight: 600;
        padding: 0.7rem;
      }
      @media (max-width: 500px) {
        dl {
          grid-template-columns: 1fr;
        }
        dd {
          margin-bottom: 0.6rem;
        }
      }
    `,
  ],
})
export class GeneralLedgerCompleteness implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly id = routeGuid();
  readonly workspace = signal<CompletenessWorkspace | null>(null);
  readonly plan = signal<CompletenessPlan | null>(null);
  readonly review = signal<CompletenessReview | null>(null);
  readonly model = signal<CompletenessDraft>(empty());
  readonly reviewed = signal(false);
  readonly assentGeneration = signal(0);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly reconciled = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  private request = 0;
  private destroyed = false;
  private context = '';
  private saved = '';
  private assent = '';
  private pending: CompletenessDraft | null = null;
  readonly canSubmit = computed(() => {
    const m = this.model(),
      p = this.plan(),
      r = this.review();
    return (
      !this.loading() &&
      !this.busy() &&
      !this.uncertain() &&
      this.reviewed() &&
      (m.action === 'prepare'
        ? !!p?.canPrepare &&
          m.trialBalanceId === p.closing.id &&
          (m.openingId || null) === (p.opening?.id ?? null) &&
          m.evidenceReference.trim().length > 0 &&
          m.evidenceReference.length <= 2000
        : !!r &&
          m.bridgeId === r.bridge.id &&
          (m.action === 'approve' ? r.canApprove : r.canReject))
    );
  });
  private endpoint() {
    return `/api/ui/gl-sources/${this.id()}/completeness`;
  }
  private entity() {
    return `gl-completeness:${this.id()}`;
  }
  private scope() {
    return { entity: this.entity(), baseRevision: this.workspace()?.context.revision ?? '' };
  }
  private intent() {
    return { ...this.model() };
  }
  private current(g: number, id: string, n: number) {
    return (
      !this.destroyed &&
      this.session.invalidation() === g &&
      this.id() === id &&
      this.request === n &&
      !!this.session.current()?.staff
    );
  }
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request++;
    });
    effect(() => {
      const s = this.session.current(),
        key = `${this.id()}:${s?.firmId}:${s?.userId}:${s?.generation}:${this.session.invalidation()}`;
      if (key === this.context) return;
      this.context = key;
      untracked(() => {
        this.request++;
        this.workspace.set(null);
        this.plan.set(null);
        this.review.set(null);
        this.model.set(empty());
        this.invalidateReview();
        this.loading.set(false);
        this.busy.set(false);
        this.uncertain.set(false);
        this.reconciled.set(false);
        this.error.set('');
        this.message.set('');
        this.saved = '';
        this.pending = null;
        if (s?.staff && this.id()) void this.refresh();
      });
    });
  }
  invalidateReview() {
    this.assent = '';
    this.reviewed.set(false);
    // Recreate the input even when check and edit events share one render turn.
    // A native checked property may otherwise outlive the cleared signal value.
    this.assentGeneration.update((x) => x + 1);
  }
  edit(key: keyof CompletenessDraft, value: string) {
    if (this.busy() || this.loading()) return;
    this.model.update((m) => ({ ...m, [key]: value }));
    this.invalidateReview();
    this.reconciled.set(false);
    if (key === 'trialBalanceId' || key === 'openingId') {
      this.model.update((m) => ({ ...m, action: 'prepare', bridgeId: '' }));
      this.plan.set(null);
      this.review.set(null);
    }
  }
  recordAssent(e: Event) {
    const checked = (e.target as HTMLInputElement).checked;
    this.reviewed.set(checked);
    const revision =
      this.model().action === 'prepare' ? this.plan()?.revision : this.review()?.revision;
    this.assent = checked && revision ? `${revision}:${JSON.stringify(this.intent())}` : '';
  }
  private validateWorkspace(w: CompletenessWorkspace) {
    if (w.context.source.batchId !== this.id()) throw new Error('Unsupported source context.');
  }
  async refresh(reconcile = false, page = 1, openingPage = 1) {
    if (this.busy() || !this.id() || !this.session.current()?.staff) return;
    const id = this.id()!,
      g = this.session.invalidation(),
      n = ++this.request,
      previous = this.workspace()?.context.revision;
    this.loading.set(true);
    this.workspace.set(null);
    this.plan.set(null);
    this.review.set(null);
    this.error.set('');
    this.invalidateReview();
    this.reconciled.set(false);
    try {
      const w = await this.api.get(
        `${this.endpoint()}?page=${page}&openingPage=${openingPage}`,
        decodeCompletenessWorkspace,
      );
      if (!this.current(g, id, n)) return;
      this.validateWorkspace(w);
      this.workspace.set(w);
      const draft = this.drafts.read(this.scope(), decodeCompletenessDraft);
      if (
        !previous &&
        (draft.state === 'stale' || (draft.state === 'ready' && draft.draft.submissionPending))
      ) {
        this.uncertain.set(true);
        this.pending = draft.state === 'ready' ? draft.draft.value : null;
        this.message.set(
          'A previous tab checkpoint needs persisted-state review. No automatic resubmission.',
        );
      }
      if (reconcile) {
        const intent = this.pending ?? this.intent();
        this.model.set(intent);
        if (intent.action === 'prepare' && intent.trialBalanceId) {
          const p = await this.api.get(this.planUrl(intent), decodeCompletenessPlan);
          if (!this.current(g, id, n)) return;
          this.validatePlan(p, intent);
          this.plan.set(p);
        } else if (intent.bridgeId) {
          const r = await this.api.get(
            `/api/ui/gl-completeness/${intent.bridgeId}?page=1`,
            decodeCompletenessReview,
          );
          if (!this.current(g, id, n)) return;
          this.validateReview(r, intent.bridgeId);
          this.review.set(r);
        } else
          throw new Error(
            'Choose the affected source pair or retained bridge, then read persisted completeness. Use Operations if the affected request is unavailable.',
          );
        this.reconciled.set(true);
      }
    } catch (e) {
      if (this.current(g, id, n)) {
        this.workspace.set(null);
        this.plan.set(null);
        this.review.set(null);
        this.model.set(empty());
        this.error.set(e instanceof Error ? e.message : 'Completeness is unavailable.');
      }
    } finally {
      if (this.current(g, id, n)) this.loading.set(false);
    }
  }
  private planUrl(m: CompletenessDraft) {
    return `${this.endpoint()}/plan?trialBalanceId=${m.trialBalanceId}${m.openingId ? '&openingId=' + m.openingId : ''}`;
  }
  private validatePlan(p: CompletenessPlan, m: CompletenessDraft) {
    if (
      p.context.source.batchId !== this.id() ||
      p.closing.id !== m.trialBalanceId ||
      (p.opening?.id ?? '') !== m.openingId ||
      p.context.revision !== this.workspace()?.context.revision
    )
      throw new Error('The source context changed. Refresh completeness.');
  }
  async loadPlan() {
    const m = this.intent();
    if (this.loading() || this.busy() || !guidPattern.test(m.trialBalanceId) || !this.workspace())
      return;
    const id = this.id()!,
      g = this.session.invalidation(),
      n = ++this.request;
    this.loading.set(true);
    this.plan.set(null);
    this.review.set(null);
    this.invalidateReview();
    this.error.set('');
    this.reconciled.set(false);
    try {
      const p = await this.api.get(this.planUrl(m), decodeCompletenessPlan);
      if (!this.current(g, id, n)) return;
      this.validatePlan(p, m);
      this.plan.set(p);
    } catch (e) {
      if (this.current(g, id, n)) {
        this.plan.set(null);
        this.review.set(null);
        this.error.set(e instanceof Error ? e.message : 'Source pair unavailable.');
      }
    } finally {
      if (this.current(g, id, n)) this.loading.set(false);
    }
  }
  private validateReview(r: CompletenessReview, bridgeId: string) {
    if (
      r.bridge.id !== bridgeId ||
      r.bridge.firmId !== this.session.current()?.firmId ||
      r.context.source.batchId !== this.id() ||
      r.context.revision !== this.workspace()?.context.revision ||
      (r.canReject && r.bridge.createdByUserId === this.session.current()?.userId)
    )
      throw new Error('Unsupported reviewer context.');
  }
  async loadReview(bridgeId: string, page = 1) {
    if (this.loading() || this.busy() || !this.workspace()) return;
    const id = this.id()!,
      g = this.session.invalidation(),
      n = ++this.request;
    this.loading.set(true);
    this.review.set(null);
    this.plan.set(null);
    this.invalidateReview();
    this.error.set('');
    this.reconciled.set(false);
    this.model.update((m) => ({
      ...m,
      bridgeId,
      action: m.action === 'reject' ? 'reject' : 'approve',
    }));
    try {
      const r = await this.api.get(
        `/api/ui/gl-completeness/${bridgeId}?page=${page}`,
        decodeCompletenessReview,
      );
      if (!this.current(g, id, n)) return;
      this.validateReview(r, bridgeId);
      this.review.set(r);
    } catch (e) {
      if (this.current(g, id, n)) {
        this.review.set(null);
        this.error.set(e instanceof Error ? e.message : 'Bridge unavailable.');
      }
    } finally {
      if (this.current(g, id, n)) this.loading.set(false);
    }
  }
  saveDraft(pending = false) {
    if (!this.workspace() || (!pending && (this.busy() || this.uncertain()))) return false;
    const saved = this.drafts.save(this.scope(), this.intent(), decodeCompletenessDraft, pending);
    if (saved) {
      this.saved = JSON.stringify(this.intent());
      if (!pending)
        this.message.set('Completeness draft saved in this tab. Review assent is not saved.');
    } else this.message.set('Tab storage is unavailable. No command was sent.');
    return saved;
  }
  recoverDraft() {
    if (this.busy() || this.loading() || this.uncertain() || !this.workspace()) return;
    const d = this.drafts.read(this.scope(), decodeCompletenessDraft);
    if (d.state !== 'ready') {
      this.message.set('No recoverable draft for this exact source context.');
      return;
    }
    if (d.draft.submissionPending) {
      this.pending = d.draft.value;
      this.uncertain.set(true);
      return;
    }
    this.model.set(d.draft.value);
    this.saved = JSON.stringify(this.intent());
    this.plan.set(null);
    this.review.set(null);
    this.invalidateReview();
    this.message.set('Draft recovered. Load the source plan or bridge and review again.');
  }
  discardDraft() {
    if (this.busy() || this.uncertain()) return;
    this.drafts.clear(this.entity());
    this.model.set(empty());
    this.plan.set(null);
    this.review.set(null);
    this.invalidateReview();
    this.saved = '';
  }
  acknowledge() {
    if (
      !this.uncertain() ||
      !this.reconciled() ||
      this.busy() ||
      this.loading() ||
      !this.workspace() ||
      (!this.plan() && !this.review())
    )
      return;
    if (!this.drafts.clear(this.entity())) {
      this.message.set(
        'Cannot clear the pending checkpoint. Keep the write fence and retry persisted-state review.',
      );
      return;
    }
    this.uncertain.set(false);
    this.reconciled.set(false);
    this.pending = null;
    this.invalidateReview();
    this.saved = '';
    this.message.set(
      'Persisted state acknowledged. Review any further action afresh; a prior operation must be resolved through Operations.',
    );
  }
  async submit() {
    const m = this.intent(),
      revision = m.action === 'prepare' ? this.plan()?.revision : this.review()?.revision;
    if (
      !this.canSubmit() ||
      !revision ||
      this.assent !== `${revision}:${JSON.stringify(m)}` ||
      !this.saveDraft(true)
    )
      return;
    const id = this.id()!,
      g = this.session.invalidation(),
      n = this.request;
    this.pending = m;
    this.busy.set(true);
    this.invalidateReview();
    this.message.set('');
    const outcome = await this.api.command(
      m.action === 'prepare' ? this.endpoint() : `/api/ui/gl-completeness/${m.bridgeId}/review`,
      m.action === 'prepare'
        ? {
            trialBalanceId: m.trialBalanceId,
            openingId: m.openingId || null,
            revision,
            evidenceReference: m.evidenceReference,
            reviewed: true,
          }
        : { revision, approve: m.action === 'approve', reviewed: true },
    );
    if (!this.current(g, id, n)) return;
    this.busy.set(false);
    const valid =
      outcome.ok &&
      (m.action === 'prepare'
        ? typeof outcome.value === 'string' && guidPattern.test(outcome.value)
        : outcome.value === true);
    if (valid && this.drafts.clear(this.entity())) {
      const value = outcome.value;
      this.pending = null;
      this.saved = '';
      this.model.set(empty());
      await this.refresh();
      if (m.action !== 'prepare' && this.workspace()) await this.loadReview(m.bridgeId);
      this.message.set(
        m.action === 'prepare'
          ? `Completeness operation ${value} queued. Refresh to observe the retained worker result; approval is a separate review.`
          : 'Independent completeness decision persisted.',
      );
    } else if (outcome.ok || outcome.unknown) {
      this.uncertain.set(true);
      this.reconciled.set(false);
      this.message.set(
        'The command outcome is not confirmed. No automatic retry. Read persisted completeness.',
      );
    } else {
      this.drafts.clear(this.entity());
      this.pending = null;
      this.plan.set(null);
      this.review.set(null);
      this.message.set(outcome.message);
    }
  }
  private dirty() {
    return (
      this.model().evidenceReference.length > 0 && JSON.stringify(this.intent()) !== this.saved
    );
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set(
        'Read and acknowledge persisted completeness before leaving this pending submission.',
      );
      return false;
    }
    if (!this.dirty()) return true;
    const choice = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if (choice === 'save') return this.saveDraft();
    if (choice === 'discard') {
      this.discardDraft();
      return true;
    }
    return false;
  }
}
