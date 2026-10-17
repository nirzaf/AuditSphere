import { Component, DestroyRef, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { Api, routeGuid } from '../../core/api';
import { Decoder, arr, guid, instant, nullable, obj, str, text } from '../../core/decode';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { SessionService } from '../../core/session';
import { WorkpaperCommandState } from './workpaper-command-state';
import { WorkpaperCommandReview } from './workpaper-command-review';
import { WorkpaperAction, WorkpaperOwnedDraft, workpaperRevision } from './workpaper-command-contracts';

// Legacy detail numbers are accepted only while exact; command revisions stay strings throughout.
const detailRevision: Decoder<string> = (value, path) => workpaperRevision(typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? String(value) : value, path);

const draft = obj({ workpaperId: guid, draftId: nullable(guid), baseWorkpaperRevision: detailRevision, baseInputGeneration: detailRevision, basePolicyGeneration: detailRevision, draftRevision: detailRevision,
  workPerformed: str(100000), conclusion: str(20000), lastSaveId: guid, lastSavedAt: nullable(instant), lifecycle: text });
const evidenceItem = obj({ linkId: guid, uploadIntentId: guid, fileName: text, contentSha256: text, note: nullable(text), linkedAt: instant });
const workpaperShape = obj({ id: guid, engagementId: guid, index: text, title: text, objective: text, templateVersion: text, procedure: text,
  linkedProcedureTitle: nullable(text), revision: detailRevision, status: text, workPerformed: nullable(str(100000)), conclusion: nullable(str(20000)), createdAt: instant, submittedAt: nullable(instant),
  submissions: arr(obj({ revision: detailRevision, submittedAt: instant, conclusion: nullable(text) }), 1000), draft,
  evidence: nullable(arr(evidenceItem, 1000)), physicalEvidence: nullable(arr(text, 1000)) });
export const decodeWorkpaper: Decoder<ReturnType<typeof workpaperShape>> = (value, path) => {
  const w = workpaperShape(value, path);
  if (w.draft.workpaperId !== w.id) throw new Error(`${path}: wrong draft workpaper identity`);
  return w;
};
type Draft = ReturnType<typeof draft>;
const AUTOSAVE_MS = 750;

/**
 * Working content is saved as a server-side draft guarded by the draft revision and base generations. Autosave runs
 * once after typing pauses; a refused or unacknowledged save is reported and never retried automatically. A deliberate
 * navigation waits for any unresolved save outcome and then protects the typed content with an acknowledged save,
 * an explicit discard, or by staying; a refused save blocks leaving with the failure visible.
 */
@Component({
  selector: 'audit-workpaper',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / @if (wp.data(); as w) { <a [routerLink]="['/app/engagements', w.engagementId, 'audit-plan']">Audit plan</a> / } <span>Workpaper</span></nav>
    <audit-page-header [title]="wp.data() ? 'Workpaper: ' + wp.data()!.title : 'Workpaper'" eyebrow="Audit evidence" description="Scoped workpaper detail, procedure linkage, preparer notes, and review status." />
    <audit-state [loading]="wp.loading()" [error]="wp.error()" label="workpaper" />
    @if (wp.data(); as w) {
      <p class="actions"><audit-status [value]="w.status" /><a [routerLink]="['/app/engagements', w.engagementId, 'audit-plan']">Audit plan</a><a [routerLink]="['/app/engagements', w.engagementId, 'audit-fieldwork']">Fieldwork control center</a></p>
      <p role="status">Current revision {{ w.revision }} · {{ w.submissions.length }} frozen submissions · linked planned procedure {{ w.linkedProcedureTitle ? 'yes' : 'no' }}</p>
      <section class="panel"><h2>Workpaper details</h2>
        <dl class="facts"><dt>Index</dt><dd>{{ w.index }}</dd><dt>Objective</dt><dd>{{ w.objective }}</dd><dt>Template version</dt><dd>{{ w.templateVersion }}</dd><dt>Procedure</dt><dd>{{ w.procedure }}</dd>
          <dt>Linked procedure</dt><dd>{{ w.linkedProcedureTitle ?? 'Not linked to a planned procedure' }}</dd><dt>Current revision</dt><dd>{{ w.revision }}</dd><dt>Created</dt><dd>{{ w.createdAt.slice(0, 16).replace('T', ' ') }} UTC</dd></dl></section>
      @if (w.evidence?.length || w.physicalEvidence?.length) {
        <section class="panel"><h2>Linked evidence</h2>
          @if (w.evidence?.length) {
            <h3>Digital evidence</h3>
            <ul aria-label="Linked digital evidence">@for (e of w.evidence; track e.linkId) { <li>{{ e.fileName }} <code>{{ e.contentSha256.slice(0, 12) }}</code>{{ e.note ? ' — ' + e.note : '' }}</li> }</ul>
          }
          @if (w.physicalEvidence?.length) {
            <h3>Physical evidence</h3>
            <ul aria-label="Linked physical evidence">@for (p of w.physicalEvidence; track p) { <li>{{ p }}</li> }</ul>
          }
        </section>
      }
      <section class="panel"><h2>Submission history</h2>
        <p>Submission freezes the content into an immutable snapshot: the reviewer sees exactly what was submitted, and a corrected conclusion requires a new workpaper revision.</p>
        <div class="table-scroll"><table><thead><tr><th scope="col">Rev</th><th scope="col">Submitted at (UTC)</th><th scope="col">Conclusion</th></tr></thead>
          <tbody>@for (s of w.submissions; track s.revision) { <tr><td>{{ s.revision }}</td><td>{{ s.submittedAt.slice(0, 16).replace('T', ' ') }}</td><td>{{ s.conclusion }}</td></tr> }
          @empty { <tr><td colspan="3">No submission has been frozen for this workpaper yet.</td></tr> }</tbody></table></div></section>
      @if (w.status === 'WORKING') {
        <section class="panel"><h2>Working content</h2>
          <label>Work performed <textarea name="work" rows="5" [(ngModel)]="work" (ngModelChange)="changed()" [readonly]="busy() || conflict() || uncertain() && !retryReadyForSave() && !freshSaveAwaitingFirstResponse()" required></textarea></label>
          <label>Conclusion <textarea name="conclusion" rows="3" [(ngModel)]="conclusion" (ngModelChange)="changed()" [readonly]="busy() || conflict() || uncertain() && !retryReadyForSave() && !freshSaveAwaitingFirstResponse()" required></textarea></label>
          <p class="actions">
            <button matButton="outlined" (click)="save()" [disabled]="busy() || conflict() || uncertain() || saving()">Save draft</button>
            <button matButton="outlined" (click)="discard()" [disabled]="busy() || conflict() || uncertain() || saving() || !state || state.draftRevision === '0'">Discard draft</button>
            <button matButton="filled" (click)="submit()" [disabled]="busy() || conflict() || uncertain()">Submit for review</button>
            <button matButton="outlined" (click)="reload()" [disabled]="busy() || saving()">Refresh current target</button></p>
          <p role="status" aria-live="polite">{{ status() }}</p>
          <p><small>Drafts are saved on the server and survive a browser refresh. Submission waits for the last acknowledged draft and freezes that exact content; a changed target is refused without overwriting anything.</small></p>
        </section>
      } @else {
        <section class="panel"><h2>Submitted content</h2>
          <dl class="facts"><dt>Work performed</dt><dd>{{ w.workPerformed }}</dd><dt>Conclusion</dt><dd>{{ w.conclusion }}</dd><dt>Submitted at</dt><dd>{{ w.submittedAt ? w.submittedAt.slice(0, 16).replace('T', ' ') + ' UTC' : '' }}</dd></dl>
          <p>A frozen submission is immutable; review targets this exact revision.</p></section>
      }
      @if (hasPreservedLocalEdits()) {
        <section class="panel" aria-label="Preserved local workpaper edits">
          <h2>Preserved local edits</h2>
          <p>The original save receipt is resolved, but the current workpaper has since been submitted. These newer local edits are not part of that frozen submission and cannot be saved to it. Copy any text you need before leaving.</p>
          <label>Preserved local work performed <textarea name="preserved-work" rows="5" [value]="work" readonly></textarea></label>
          <label>Preserved local conclusion <textarea name="preserved-conclusion" rows="3" [value]="conclusion" readonly></textarea></label>
          <p>Abandonment clears only these local edits. No server draft is discarded and no submission is changed.</p>
          <button matButton="outlined" (click)="abandonLocalEdits()" [disabled]="busy() || saving() || uncertain()">Abandon local edits only</button>
        </section>
      }
      @if (uncertain()) {
        <section class="panel" role="region" aria-label="Workpaper request recovery">
          <h2>Recover original workpaper request</h2>
          <p role="alert">The outcome is not confirmed. Matching current content or history is not an actor-owned receipt. No command will be retried automatically.</p>
          @for (p of flow.pending(); track p.action) {
            <p>{{ p.action }} · request {{ p.reference.requestId }} · <code>{{ p.reference.requestHash }}</code></p>
          }
          <button matButton="outlined" (click)="checkSaveOutcome()" [disabled]="busy() || saving()">Look up original workpaper receipt</button>
          @if (flow.absent() && flow.pending().length === 1) {
            <p>No receipt was found at lookup time; a delayed command may still commit. Retry retains the exact request ID and hash. After reload, re-enter the original save content if needed; changed intent cannot reuse this identity.</p>
            <button matButton="outlined" (click)="retryPending()" [disabled]="busy() || saving()">Prepare same workpaper request again</button>
          }
        </section>
      }
      @if (flow.receipt(); as r) {
        <section class="panel" aria-label="Verified workpaper receipt"><h2>Verified original workpaper receipt</h2>
          <p>{{ r.action }} · request {{ r.requestId }} · <code>{{ r.requestHash }}</code></p>
          <p>Actor {{ r.actorId }} · session {{ r.actorEpoch }} · event {{ r.commandEvent.id }}</p>
          <p>Original draft {{ r.commandEvent.after.draft?.id }} · revision {{ r.commandEvent.before.draft?.draftRevision ?? '0' }} → {{ r.commandEvent.after.draft?.draftRevision }}</p>
          <p>Original workpaper revision {{ r.commandEvent.before.revision }} → {{ r.commandEvent.after.revision }} · {{ r.commandEvent.after.status }}</p>
          <p>This acknowledges the original request. Later saved content may differ.</p>
        </section>
      }
      @if (flow.message()) { <p [attr.role]="flow.failed() || uncertain() ? 'alert' : 'status'">{{ flow.message() }}</p> }
      @if (message()) { <p [attr.role]="failed() ? 'alert' : 'status'" [class.error-text]="failed()">{{ message() }}</p> }
    }
  `,
})
export class WorkpaperEditor {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly id = routeGuid();
  private readonly routeEpoch = signal(0);
  readonly wp = this.api.resource(() => { this.routeEpoch(); return this.id() ? `/api/ui/audit/workpapers/${this.id()}` : null; }, (value, path) => {
    const w = decodeWorkpaper(value, path);
    if (w.id !== this.id()) throw new Error('Wrong returned workpaper identity');
    return w;
  }, 'The requested workpaper is unavailable in your current scope.');
  state: Draft | null = null;
  readonly flow = new WorkpaperCommandState(this.id, this.wp.data, () => this.state);
  private readonly finalOpen = signal(false);
  private readonly navigationOpen = signal(false);
  private readonly startingFreshSave = signal(false);
  readonly freshSaveAwaitingFirstResponse = computed(() => {
    const pending = this.flow.pending();
    return this.startingFreshSave() && this.saving() && this.flow.busy() && !this.flow.absent() && pending.length === 1 &&
      pending[0]?.action === 'SAVE_DRAFT' && pending[0]?.workpaperId === this.id();
  });
  readonly busy = computed(() => this.finalOpen() || this.flow.busy() && !this.saving());
  readonly saving = signal(false);
  readonly conflict = this.flow.conflict;
  readonly uncertain = this.flow.uncertain;
  readonly retryReady = this.flow.absent;
  readonly retryReadyForSave = computed(() => this.flow.absent() && this.flow.pending().length === 1 &&
    this.flow.pending()[0]?.action === 'SAVE_DRAFT' && this.flow.pending()[0]?.workpaperId === this.id());
  readonly status = signal('Unsaved changes');
  readonly message = signal('');
  readonly failed = signal(false);
  work = '';
  conclusion = '';
  private dirty = false;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private pendingSave: Promise<boolean> | null = null;
  private operation = 0;
  private lifetime = 0;
  private finalOperation = 0;
  private observedActor: object | null = null;
  private observedSessionInvalidation = -1;
  private observedRouteId: string | null | undefined;
  private readonly dialogs = inject(MatDialog);
  private reviewDialog: ReturnType<MatDialog['open']> | null = null;

  constructor() {
    const params = inject(ActivatedRoute).paramMap.subscribe(() => this.resetLifetime());
    effect(() => {
      const routeId = this.id(), w = this.wp.data(), actor = this.session.current(); const invalidation = this.session.invalidation(); this.routeEpoch(); const loading = this.wp.loading(), readError = this.wp.error();
      untracked(() => {
        ++this.operation; ++this.lifetime; ++this.finalOperation; clearTimeout(this.timer); this.saving.set(false); this.startingFreshSave.set(false); this.pendingSave = null;
        this.reviewDialog?.close(false); this.reviewDialog = null; this.finalOpen.set(false); this.navigationOpen.set(false);
        const changedRoute = this.observedRouteId !== routeId;
        const sameActor = this.observedActor === actor && this.observedSessionInvalidation === invalidation;
        this.observedActor = actor; this.observedSessionInvalidation = invalidation;
        this.observedRouteId = routeId;
        if (changedRoute) { this.message.set(''); this.failed.set(false); this.dirty = false; }
        if (!w || !actor?.staff) {
          this.state = null;
          if (!((loading || readError === 'Temporarily unavailable. Retry shortly.') && !changedRoute && sameActor && actor?.staff)) { this.work = ''; this.conclusion = ''; this.dirty = false; }
          return;
        }
        // A refreshed read cannot silently discard newer edits or resolve a pending request.
        const preserve = !changedRoute && this.dirty;
        this.state = w.draft; this.flow.conflict.set(w.draft.lifecycle === 'TARGET_CHANGED');
        if (!preserve) { this.work = w.draft.workPerformed; this.conclusion = w.draft.conclusion; }
        this.status.set(preserve ? 'Local edits remain unsaved; review the refreshed target before saving.' : this.savedStatus(w.draft));
      });
    });
    inject(DestroyRef).onDestroy(() => { this.resetLifetime(); params.unsubscribe(); });
  }
  private resetLifetime(): void {
    this.routeEpoch.update(value => value + 1);
    ++this.operation; ++this.lifetime; ++this.finalOperation; clearTimeout(this.timer); this.pendingSave = null; this.saving.set(false); this.startingFreshSave.set(false);
    this.reviewDialog?.close(false); this.reviewDialog = null; this.finalOpen.set(false); this.navigationOpen.set(false);
    this.state = null; this.work = ''; this.conclusion = ''; this.dirty = false;
  }
  private savedStatus(d: Draft): string {
    return d.lifecycle === 'TARGET_CHANGED' ? 'Target changed — reload before editing.' : d.lastSavedAt && d.draftRevision !== '0'
      ? `Saved at ${d.lastSavedAt.slice(0, 19).replace('T', ' ')} UTC` : 'Unsaved changes';
  }
  private report(ok: boolean, text: string): void { this.failed.set(!ok); this.message.set(text); }
  changed(): void {
    this.dirty = true; this.flow.inputChanged(); this.status.set('Unsaved changes'); clearTimeout(this.timer);
    if (!this.uncertain() && !this.finalOpen() && !this.navigationOpen()) this.timer = setTimeout(() => void this.save(), AUTOSAVE_MS);
  }
  private saveFields() {
    const w = this.wp.data(), s = this.state;
    return w && s ? { action: 'SAVE_DRAFT', save: { expectedDraftRevision: s.draftRevision, baseWorkpaperRevision: w.revision,
      baseInputGeneration: s.baseInputGeneration, basePolicyGeneration: s.basePolicyGeneration,
      workPerformed: this.work.trim(), conclusion: this.conclusion.trim() } } : null;
  }
  save(): Promise<boolean> { return this.saveDraft(false); }
  private async saveDraft(insideFinal: boolean): Promise<boolean> {
    clearTimeout(this.timer);
    if (this.saving()) return this.pendingSave ?? false;
    if (!this.flow.ready() || this.wp.data()?.status !== 'WORKING' || !this.state || this.conflict() || this.uncertain() || this.flow.busy() || this.finalOpen() && !insideFinal) return false;
    const valid = this.flow.ticket(), operation = ++this.operation, fields = this.saveFields(), work = this.work, conclusion = this.conclusion;
    this.startingFreshSave.set(true); this.saving.set(true); this.status.set('Saving…');
    this.pendingSave = (async () => {
      try {
        const receipt = await this.flow.save(fields);
        if (!valid() || operation !== this.operation) return false;
        if (!receipt) { this.status.set(this.uncertain() ? 'Save outcome needs review.' : 'Not saved — your edits remain on this page.'); return false; }
        this.acknowledgeSave(receipt.commandEvent.after.draft!, work, conclusion);
        this.report(true, 'Draft saved.'); return !this.dirty;
      } finally {
        if (operation === this.operation) { this.saving.set(false); this.startingFreshSave.set(false); this.pendingSave = null; }
      }
    })();
    return this.pendingSave;
  }
  private acknowledgeSave(d: WorkpaperOwnedDraft, sentWork: string, sentConclusion: string): void {
    this.state = { workpaperId: this.id()!, draftId: d.id, baseWorkpaperRevision: d.baseWorkpaperRevision,
      baseInputGeneration: d.baseInputGeneration, basePolicyGeneration: d.basePolicyGeneration, draftRevision: d.draftRevision,
      workPerformed: d.workPerformed, conclusion: d.conclusion, lastSaveId: d.lastSaveId, lastSavedAt: d.lastSavedAt, lifecycle: d.lifecycle };
    this.dirty = this.work !== sentWork || this.conclusion !== sentConclusion;
    this.status.set(this.dirty ? 'Draft saved; newer edits remain unsaved.' : this.savedStatus(this.state));
    if (this.dirty && !this.finalOpen() && !this.navigationOpen()) {
      clearTimeout(this.timer); this.timer = setTimeout(() => void this.save(), AUTOSAVE_MS);
    }
  }
  async checkSaveOutcome(): Promise<void> {
    if (this.saving() || this.finalOpen()) return;
    const valid = this.flow.ticket(), receipt = await this.flow.reconcile();
    if (!valid()) return;
    if (!receipt) { if (this.flow.failed()) this.wp.reload(); return; }
    // An original receipt is historical evidence. Re-read the authorized current row rather than reverting to its after-state.
    if (receipt.action === 'SAVE_DRAFT') {
      const original = receipt.commandEvent.after.draft!;
      this.dirty = this.dirty && (this.work.trim() !== original.workPerformed || this.conclusion.trim() !== original.conclusion);
    }
    this.report(true, 'The original workpaper receipt was verified. Refreshing current persisted state.');
    this.wp.reload();
  }
  async retryExactSave(): Promise<void> { await this.retryPending(); }
  async retryPending(): Promise<void> {
    const pending = this.flow.pending()[0];
    if (!pending || !this.flow.canPrepare(pending.action) || this.saving() || this.finalOpen()) return;
    if (pending.action !== 'SAVE_DRAFT') { await this.finalAction(pending.action, true); return; }
    const valid = this.flow.ticket(), operation = ++this.operation; this.saving.set(true);
    try {
      const receipt = await this.flow.save(this.saveFields());
      if (!valid() || operation !== this.operation || !receipt) return;
      const original = receipt.commandEvent.after.draft!;
      this.dirty = this.dirty && (this.work.trim() !== original.workPerformed || this.conclusion.trim() !== original.conclusion);
      this.report(true, 'The original draft save receipt was verified. Refreshing current persisted state.'); this.wp.reload();
    } finally { if (operation === this.operation) this.saving.set(false); }
  }
  async confirmNavigation(): Promise<boolean> {
    clearTimeout(this.timer);
    const inFlightSave = this.saving() ? this.pendingSave : null;
    // A dispatched save already has a pending checkpoint. Await its acknowledgement before deciding whether it is uncertain.
    if (this.navigationOpen() || this.finalOpen() || (this.uncertain() || this.flow.busy()) && !inFlightSave) return false;
    const lifetime = this.lifetime; let stayed = false; this.navigationOpen.set(true);
    try {
      if (inFlightSave) await inFlightSave.catch(() => false);
      if (lifetime !== this.lifetime || this.uncertain()) return false;
      if (!this.wp.data() || this.conflict()) return true;
      if (!this.dirty) return true;
      if (this.wp.data()?.status === 'SUBMITTED_SNAPSHOT') {
        this.report(false, 'Copy the preserved local edits, then explicitly abandon local edits before leaving. The submitted workpaper cannot accept a save or server discard.');
        return false;
      }
      const valid = this.flow.ticket();
      const dialog = this.dialogs.open(UnsavedChangesDialog);
      this.reviewDialog = dialog;
      const decision = await firstValueFrom(dialog.afterClosed());
      if (this.reviewDialog === dialog) this.reviewDialog = null;
      if (!valid() || this.uncertain()) return false;
      if (decision === 'save') return await this.save();
      if (decision === 'discard') return await this.discard();
      stayed = true; return false;
    } finally {
      if (lifetime === this.lifetime) {
        this.navigationOpen.set(false);
        if (stayed) this.rearmAutosaveAfterStay();
      }
    }
  }
  private rearmAutosaveAfterStay(): void {
    clearTimeout(this.timer);
    if (this.dirty && this.flow.ready() && this.wp.data()?.status === 'WORKING' && this.state &&
      !this.saving() && !this.flow.busy() && !this.finalOpen() && !this.navigationOpen() && !this.uncertain() && !this.conflict() && !this.flow.failed())
      this.timer = setTimeout(() => void this.save(), AUTOSAVE_MS);
  }
  hasPreservedLocalEdits(): boolean {
    const current = this.wp.data(), receipt = this.flow.receipt(), actor = this.session.current();
    return this.dirty && this.flow.ready() && current?.status === 'SUBMITTED_SNAPSHOT' && receipt?.action === 'SAVE_DRAFT' &&
      receipt.workpaperId === current.id && receipt.actorId === actor?.userId && receipt.actorEpoch === actor?.generation && !this.uncertain();
  }
  abandonLocalEdits(): boolean {
    if (!this.hasPreservedLocalEdits() || this.busy() || this.saving()) return false;
    const current = this.wp.data()!;
    clearTimeout(this.timer);
    this.work = current.draft.workPerformed; this.conclusion = current.draft.conclusion; this.dirty = false;
    this.status.set('Local edits abandoned. No server draft was discarded and no submission was changed.');
    this.report(true, this.status());
    return true;
  }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty || this.saving() || this.uncertain() || this.finalOpen() || this.flow.busy()) { event.preventDefault(); event.returnValue = ''; }
  }
  async submit(): Promise<void> { await this.finalAction('SUBMIT'); }
  async discard(): Promise<boolean> { return this.finalAction('DISCARD_DRAFT'); }
  private finalFields(action: WorkpaperAction) {
    const w = this.wp.data(), s = this.state;
    if (!w || !s) return null;
    return action === 'DISCARD_DRAFT' ? { action, discard: { expectedDraftRevision: s.draftRevision } } : {
      action, submit: { expectedRevision: w.revision, expectedDraftRevision: s.draftRevision, draftSaveId: s.lastSaveId,
        workPerformed: this.work.trim(), conclusion: this.conclusion.trim() } };
  }
  private async finalAction(action: WorkpaperAction, recovery = false): Promise<boolean> {
    clearTimeout(this.timer);
    if (!this.flow.ready() || this.wp.data()?.status !== 'WORKING' || this.finalOpen() || this.flow.busy() && !this.saving() || this.conflict() || this.uncertain() && !recovery ||
      recovery && !this.flow.canPrepare(action)) return false;
    this.finalOpen.set(true); const lifetime = this.lifetime, finalOperation = ++this.finalOperation;
    try {
      if (this.saving() && this.pendingSave) await this.pendingSave;
      if (lifetime !== this.lifetime || this.uncertain() && !recovery) return false;
      if (action === 'SUBMIT' && this.dirty && !recovery && !(await this.saveDraft(true))) return false;
      if (lifetime !== this.lifetime) return false;
      const valid = this.flow.ticket();
      if (!this.state || this.state.draftRevision === '0') { this.report(false, 'Save the current draft before reviewing this action.'); return false; }
      if (!(await this.flow.prepare(() => this.finalFields(action))) || !valid()) return false;
      const dialog = this.dialogs.open(WorkpaperCommandReview, { data: { flow: this.flow }, disableClose: true, width: '760px', maxWidth: '95vw' });
      this.reviewDialog = dialog;
      const accepted = await firstValueFrom(dialog.afterClosed());
      if (!valid() || !accepted || this.uncertain()) return false;
      this.dirty = false;
      this.report(true, action === 'SUBMIT' ? 'The submission was recorded.' : 'Draft discarded.'); this.wp.reload(); return true;
    } finally {
      // A later editor lifetime owns its own dialog/busy state.
      if (finalOperation === this.finalOperation) {
        this.reviewDialog = null; this.finalOpen.set(false); this.navigationOpen.set(false); this.flow.resetReview();
      }
    }
  }
  reload(): void {
    if (this.busy() || this.saving()) return;
    clearTimeout(this.timer); this.flow.resetReview(); this.flow.conflict.set(false); this.wp.reload();
  }
}
