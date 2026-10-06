import { Component, DestroyRef, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { Api, routeGuid } from '../../core/api';
import { arr, guid, instant, int, nullable, obj, text } from '../../core/decode';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';

const draft = obj({ workpaperId: guid, draftId: nullable(guid), baseWorkpaperRevision: int, baseInputGeneration: int, basePolicyGeneration: int, draftRevision: int,
  workPerformed: text, conclusion: text, lastSaveId: guid, lastSavedAt: nullable(instant), lifecycle: text });
const evidenceItem = obj({ linkId: guid, uploadIntentId: guid, fileName: text, contentSha256: text, note: nullable(text), linkedAt: instant });
export const decodeWorkpaper = obj({ id: guid, engagementId: guid, index: text, title: text, objective: text, templateVersion: text, procedure: text,
  linkedProcedureTitle: nullable(text), revision: int, status: text, workPerformed: nullable(text), conclusion: nullable(text), createdAt: instant, submittedAt: nullable(instant),
  submissions: arr(obj({ revision: int, submittedAt: instant, conclusion: nullable(text) }), 1000), draft,
  evidence: nullable(arr(evidenceItem, 1000)), physicalEvidence: nullable(arr(text, 1000)) });
type Draft = ReturnType<typeof draft>;
type DraftSaveAttempt = {
  workpaperId: string;
  expectedDraftRevision: number;
  baseWorkpaperRevision: number;
  baseInputGeneration: number;
  basePolicyGeneration: number;
  saveId: string;
  workPerformed: string;
  conclusion: string;
};
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
          <label>Work performed <textarea name="work" rows="5" [(ngModel)]="work" (ngModelChange)="changed()" [disabled]="busy() || conflict() || uncertain()" required></textarea></label>
          <label>Conclusion <textarea name="conclusion" rows="3" [(ngModel)]="conclusion" (ngModelChange)="changed()" [disabled]="busy() || conflict() || uncertain()" required></textarea></label>
          <p class="actions">
            <button matButton="outlined" (click)="save()" [disabled]="busy() || conflict() || uncertain() || saving()">Save draft</button>
            <button matButton="outlined" (click)="discard()" [disabled]="busy() || conflict() || uncertain() || saving() || !state || state.draftRevision < 1">Discard draft</button>
            <button matButton="filled" (click)="submit()" [disabled]="busy() || conflict() || uncertain()">Submit for review</button>
            <button matButton="outlined" (click)="reload()" [disabled]="busy() || saving()">{{ uncertain() ? 'Discard unconfirmed edits and reload' : 'Reload current target' }}</button></p>
          <p role="status" aria-live="polite">{{ status() }}</p>
          @if (uncertain()) {
            <section class="panel" role="region" aria-label="Workpaper save recovery">
              <h3>Draft save outcome needs review</h3>
              <p>Check the saved draft before repeating the action. AuditSphere will not send another save automatically.</p>
              <p class="actions">
                <button matButton="outlined" (click)="checkSaveOutcome()" [disabled]="saving()">Check saved draft</button>
                @if (retryReady()) {
                  <button matButton="outlined" (click)="retryExactSave()" [disabled]="saving()">Retry the same draft save</button>
                }
              </p>
            </section>
          }
          <p><small>Drafts are saved on the server and survive a browser refresh. Submission waits for the last acknowledged draft and freezes that exact content; a changed target is refused without overwriting anything.</small></p>
        </section>
      } @else {
        <section class="panel"><h2>Submitted content</h2>
          <dl class="facts"><dt>Work performed</dt><dd>{{ w.workPerformed }}</dd><dt>Conclusion</dt><dd>{{ w.conclusion }}</dd><dt>Submitted at</dt><dd>{{ w.submittedAt ? w.submittedAt.slice(0, 16).replace('T', ' ') + ' UTC' : '' }}</dd></dl>
          <p>A frozen submission is immutable; review targets this exact revision.</p></section>
      }
      @if (message()) { <p [attr.role]="failed() ? 'alert' : 'status'" [class.error-text]="failed()">{{ message() }}</p> }
    }
  `,
})
export class WorkpaperEditor {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly wp = this.api.resource(() => (this.id() ? `/api/ui/audit/workpapers/${this.id()}` : null), decodeWorkpaper, 'The requested workpaper is unavailable in your current scope.');
  readonly busy = signal(false);
  readonly saving = signal(false);
  readonly conflict = signal(false);
  readonly uncertain = signal(false);
  readonly retryReady = signal(false);
  readonly status = signal('Unsaved changes');
  readonly message = signal('');
  readonly failed = signal(false);
  state: Draft | null = null;
  work = '';
  conclusion = '';
  private dirty = false;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private pendingSave: Promise<boolean> | null = null;
  private uncertainSave: DraftSaveAttempt | null = null;
  private observedRouteId: string | null | undefined;
  private readonly dialogs = inject(MatDialog);

  constructor() {
    effect(() => {
      const routeId = this.id();
      const w = this.wp.data();
      untracked(() => {
        clearTimeout(this.timer);
        if (this.observedRouteId !== routeId) {
          this.observedRouteId = routeId;
          this.message.set('');
          this.failed.set(false);
        }
        this.state = w?.draft ?? null; this.work = w?.draft.workPerformed ?? ''; this.conclusion = w?.draft.conclusion ?? ''; this.dirty = false;
        this.uncertainSave = null; this.uncertain.set(false); this.retryReady.set(false);
        this.conflict.set(w?.draft.lifecycle === 'TARGET_CHANGED');
        this.status.set(this.conflict() ? 'Target changed — reload before editing.' : w?.draft.lastSavedAt && w.draft.draftRevision > 0
          ? `Saved at ${w.draft.lastSavedAt.slice(0, 19).replace('T', ' ')} UTC` : 'Unsaved changes');
      });
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }
  private report(ok: boolean, text: string): void { this.failed.set(!ok); this.message.set(text); }
  changed(): void {
    this.dirty = true; this.status.set('Unsaved changes');
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.save(), AUTOSAVE_MS);
  }
  async save(): Promise<boolean> {
    const w = this.wp.data(); const s = this.state;
    clearTimeout(this.timer);
    if (!w || !s || this.conflict() || this.uncertain()) return this.pendingSave ?? false;
    if (this.saving()) return this.pendingSave ?? false;
    this.saving.set(true); this.status.set('Saving…');
    const work = this.work, conclusion = this.conclusion;
    const attempt: DraftSaveAttempt = {
      workpaperId: w.id,
      expectedDraftRevision: s.draftRevision,
      baseWorkpaperRevision: w.revision,
      baseInputGeneration: s.baseInputGeneration,
      basePolicyGeneration: s.basePolicyGeneration,
      saveId: crypto.randomUUID(),
      workPerformed: work,
      conclusion,
    };
    this.pendingSave = (async (): Promise<boolean> => {
      try {
        const r = await this.api.command<Draft>(`/api/ui/audit/workpapers/${w.id}/draft`, this.saveBody(attempt));
        if (!r.ok) {
          if (r.unknown) {
            this.uncertainSave = attempt;
            this.uncertain.set(true);
            this.retryReady.set(false);
            this.status.set('Save outcome needs review. Check the persisted draft before continuing.');
            this.report(false, r.message);
            return false;
          }
          const conflict = r.status === 409 || ['revision.stale', 'generation.stale', 'draft.target-changed', 'draft.conflict'].includes(r.code);
          this.conflict.set(conflict);
          this.status.set(conflict ? 'Conflict — a newer draft or target exists. Reload the current workpaper.' : 'Not saved — changes since the last save are not yet saved.');
          this.report(false, r.message);
          return false;
        }
        this.state = r.value;
        this.dirty = this.work !== work || this.conclusion !== conclusion;
        this.status.set(this.dirty ? 'Unsaved changes' : `Saved at ${(r.value.lastSavedAt ?? '').slice(0, 19).replace('T', ' ')} UTC`);
        this.report(true, 'Draft saved.');
        return !this.dirty;
      } finally { this.saving.set(false); this.pendingSave = null; }
    })();
    return this.pendingSave;
  }

  private saveBody(attempt: DraftSaveAttempt) {
    return {
      expectedDraftRevision: attempt.expectedDraftRevision,
      baseWorkpaperRevision: attempt.baseWorkpaperRevision,
      baseInputGeneration: attempt.baseInputGeneration,
      basePolicyGeneration: attempt.basePolicyGeneration,
      saveId: attempt.saveId,
      workPerformed: attempt.workPerformed,
      conclusion: attempt.conclusion,
    };
  }

  async checkSaveOutcome(): Promise<void> {
    const attempt = this.uncertainSave;
    if (!attempt || this.saving()) return;
    this.saving.set(true);
    this.retryReady.set(false);
    this.status.set('Checking the persisted draft…');
    try {
      const saved = await this.api.get(`/api/ui/audit/workpapers/${attempt.workpaperId}`, decodeWorkpaper);
      if (this.wp.data()?.id !== attempt.workpaperId || this.uncertainSave?.saveId !== attempt.saveId) return;
      const d = saved.draft;
      const sameTarget = saved.revision === attempt.baseWorkpaperRevision &&
        d.baseWorkpaperRevision === attempt.baseWorkpaperRevision &&
        d.baseInputGeneration === attempt.baseInputGeneration &&
        d.basePolicyGeneration === attempt.basePolicyGeneration;
      if (saved.id === attempt.workpaperId && saved.status === 'WORKING' && sameTarget &&
        d.lastSaveId === attempt.saveId && d.draftRevision === attempt.expectedDraftRevision + 1 &&
        d.workPerformed === attempt.workPerformed.trim() && d.conclusion === attempt.conclusion.trim()) {
        this.state = d;
        this.dirty = this.work !== attempt.workPerformed || this.conclusion !== attempt.conclusion;
        this.uncertainSave = null;
        this.uncertain.set(false);
        this.conflict.set(false);
        this.status.set(this.dirty ? 'The draft was saved. Newer edits remain unsaved.' : `Saved at ${(d.lastSavedAt ?? '').slice(0, 19).replace('T', ' ')} UTC`);
        this.report(true, 'The persisted draft confirms that save.');
        return;
      }
      if (saved.id === attempt.workpaperId && saved.status === 'WORKING' && sameTarget &&
        d.draftRevision === attempt.expectedDraftRevision &&
        (d.lifecycle === 'ACTIVE' || d.lifecycle === 'NONE')) {
        this.retryReady.set(true);
        this.status.set('This exact save is not in the persisted draft. You can safely retry the same save.');
        this.report(false, 'The save was not found. Retry will use the same request ID and content.');
        return;
      }
      this.conflict.set(true);
      this.status.set('The persisted draft or target changed. Reload the current target before editing.');
      this.report(false, 'The current draft does not match the unconfirmed save. Reload the current target to review it.');
    } catch (e) {
      if (e instanceof Error && e.message === 'Not available in your current scope.') {
        this.uncertainSave = null;
        this.uncertain.set(false);
        this.retryReady.set(false);
        this.conflict.set(false);
        this.wp.reload();
        return;
      }
      this.status.set('The save outcome could not be checked. Your text is still on this page.');
      this.report(false, e instanceof Error ? e.message : 'The saved draft could not be checked.');
    } finally {
      this.saving.set(false);
    }
  }

  async retryExactSave(): Promise<void> {
    const attempt = this.uncertainSave;
    if (!attempt || !this.retryReady() || this.saving()) return;
    this.saving.set(true);
    this.retryReady.set(false);
    this.status.set('Retrying the same reviewed draft save…');
    try {
      const r = await this.api.command<Draft>(`/api/ui/audit/workpapers/${attempt.workpaperId}/draft`, this.saveBody(attempt));
      if (!r.ok) {
        if (r.unknown) {
          this.status.set('Retry outcome needs review. Check the persisted draft again.');
          this.report(false, r.message);
        } else {
          const conflict = r.status === 409 || ['revision.stale', 'generation.stale', 'draft.target-changed', 'draft.conflict'].includes(r.code);
          this.conflict.set(conflict);
          if (!conflict) {
            this.uncertainSave = null;
            this.uncertain.set(false);
          }
          this.status.set(conflict ? 'Conflict — reload the current workpaper.' : 'The save was refused; your edits remain on this page.');
          this.report(false, r.message);
        }
        return;
      }
      this.state = r.value;
      this.dirty = this.work !== attempt.workPerformed || this.conclusion !== attempt.conclusion;
      this.uncertainSave = null;
      this.uncertain.set(false);
      this.conflict.set(false);
      this.status.set(this.dirty ? 'Unsaved changes' : `Saved at ${(r.value.lastSavedAt ?? '').slice(0, 19).replace('T', ' ')} UTC`);
      this.report(true, 'The exact draft save was confirmed.');
    } finally {
      this.saving.set(false);
    }
  }
  /** Leave guard: navigation waits for an unresolved save outcome, then an acknowledged save, an
   * explicit discard, or staying protects the typed content. A conflicted target cannot save this
   * content at all and its refusal is already reported, so leaving it is honest. */
  async confirmNavigation(): Promise<boolean> {
    if (this.uncertain()) {
      this.status.set('Check the unconfirmed save before leaving this workpaper.');
      return false;
    }
    if (!this.wp.data() || this.conflict()) return true;
    if (this.saving() && this.pendingSave) await this.pendingSave.catch(() => false);
    if (!this.dirty) return true;
    const decision = await firstValueFrom(this.dialogs.open(UnsavedChangesDialog).afterClosed());
    if (decision === 'save') return this.save();
    if (decision === 'discard') return this.discard();
    return false;
  }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty || this.saving() || this.uncertain()) { event.preventDefault(); event.returnValue = ''; }
  }
  async submit(): Promise<void> {
    const w = this.wp.data();
    if (!w || this.busy() || this.uncertain()) return;
    this.busy.set(true);
    try {
      if (this.dirty && !(await this.save())) return;
      const s = this.state;
      if (!s || s.draftRevision < 1) { this.report(false, 'Save the current draft before submitting it.'); return; }
      const r = await this.api.command(`/api/ui/audit/workpapers/${w.id}/submit`, { expectedRevision: w.revision, workPerformed: this.work, conclusion: this.conclusion,
        expectedDraftRevision: s.draftRevision, draftSaveId: s.lastSaveId });
      this.report(r.ok, r.ok ? 'The submission was recorded.' : r.message);
      if (r.ok) this.wp.reload();
    } finally { this.busy.set(false); }
  }
  async discard(): Promise<boolean> {
    const w = this.wp.data(); const s = this.state;
    if (!w || !s || this.busy() || this.uncertain()) return false;
    this.busy.set(true);
    try {
      const r = await this.api.command(`/api/ui/audit/workpapers/${w.id}/draft/discard`, { expectedDraftRevision: s.draftRevision });
      this.report(r.ok, r.ok ? 'Draft discarded.' : r.message);
      if (r.ok) this.wp.reload();
      return r.ok;
    } finally { this.busy.set(false); }
  }
  reload(): void {
    this.message.set('');
    this.uncertainSave = null;
    this.uncertain.set(false);
    this.retryReady.set(false);
    this.conflict.set(false);
    this.wp.reload();
  }
}
