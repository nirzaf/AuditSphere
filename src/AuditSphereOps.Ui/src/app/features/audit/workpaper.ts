import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { arr, guid, instant, int, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const draft = obj({ workpaperId: guid, draftId: nullable(guid), baseWorkpaperRevision: int, baseInputGeneration: int, basePolicyGeneration: int, draftRevision: int,
  workPerformed: text, conclusion: text, lastSaveId: guid, lastSavedAt: nullable(instant), lifecycle: text });
export const decodeWorkpaper = obj({ id: guid, engagementId: guid, index: text, title: text, objective: text, templateVersion: text, procedure: text,
  linkedProcedureTitle: nullable(text), revision: int, status: text, workPerformed: nullable(text), conclusion: nullable(text), createdAt: instant, submittedAt: nullable(instant),
  submissions: arr(obj({ revision: int, submittedAt: instant, conclusion: nullable(text) }), 1000), draft });
type Draft = ReturnType<typeof draft>;
const AUTOSAVE_MS = 1500;

/**
 * Working content is saved as a server-side draft guarded by the draft revision and base generations. Autosave runs
 * once after typing pauses; a refused or unacknowledged save is reported and never retried automatically.
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
      <section class="panel"><h2>Submission history</h2>
        <p>Submission freezes the content into an immutable snapshot: the reviewer sees exactly what was submitted, and a corrected conclusion requires a new workpaper revision.</p>
        <div class="table-scroll"><table><thead><tr><th scope="col">Rev</th><th scope="col">Submitted at (UTC)</th><th scope="col">Conclusion</th></tr></thead>
          <tbody>@for (s of w.submissions; track s.revision) { <tr><td>{{ s.revision }}</td><td>{{ s.submittedAt.slice(0, 16).replace('T', ' ') }}</td><td>{{ s.conclusion }}</td></tr> }
          @empty { <tr><td colspan="3">No submission has been frozen for this workpaper yet.</td></tr> }</tbody></table></div></section>
      @if (w.status === 'WORKING') {
        <section class="panel"><h2>Working content</h2>
          <label>Work performed <textarea name="work" rows="5" [(ngModel)]="work" (ngModelChange)="changed()" [disabled]="busy() || conflict()" required></textarea></label>
          <label>Conclusion <textarea name="conclusion" rows="3" [(ngModel)]="conclusion" (ngModelChange)="changed()" [disabled]="busy() || conflict()" required></textarea></label>
          <p class="actions">
            <button matButton="outlined" (click)="save()" [disabled]="busy() || conflict() || saving()">Save draft</button>
            <button matButton="outlined" (click)="discard()" [disabled]="busy() || conflict() || saving() || !state || state.draftRevision < 1">Discard draft</button>
            <button matButton="filled" (click)="submit()" [disabled]="busy() || conflict()">Submit for review</button>
            <button matButton="outlined" (click)="reload()" [disabled]="busy() || saving()">Reload current target</button></p>
          <p role="status" aria-live="polite">{{ status() }}</p>
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
  readonly status = signal('Unsaved changes');
  readonly message = signal('');
  readonly failed = signal(false);
  state: Draft | null = null;
  work = '';
  conclusion = '';
  private dirty = false;
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const w = this.wp.data();
      untracked(() => {
        clearTimeout(this.timer);
        this.state = w?.draft ?? null; this.work = w?.draft.workPerformed ?? ''; this.conclusion = w?.draft.conclusion ?? ''; this.dirty = false;
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
    if (!w || !s || this.saving() || this.conflict()) return false;
    this.saving.set(true); this.status.set('Saving…');
    const work = this.work, conclusion = this.conclusion;
    try {
      const r = await this.api.command<Draft>(`/api/ui/audit/workpapers/${w.id}/draft`, { expectedDraftRevision: s.draftRevision, baseWorkpaperRevision: w.revision,
        baseInputGeneration: s.baseInputGeneration, basePolicyGeneration: s.basePolicyGeneration, saveId: crypto.randomUUID(), workPerformed: work, conclusion });
      if (!r.ok) {
        const conflict = r.status === 409 || ['revision.stale', 'generation.stale', 'draft.target-changed', 'draft.conflict'].includes(r.code);
        this.conflict.set(conflict);
        this.status.set(conflict ? 'Conflict — a newer draft or target exists. Reload the current workpaper.' : 'Not saved — changes since the last save are not yet saved.');
        this.report(false, r.message);
        return false;
      }
      this.state = r.value;
      this.dirty = this.work !== work || this.conclusion !== conclusion;
      this.status.set(this.dirty ? 'Unsaved changes' : `Saved at ${(r.value.lastSavedAt ?? '').slice(0, 19).replace('T', ' ')} UTC`);
      if (!this.failed()) this.message.set('');
      return !this.dirty;
    } finally { this.saving.set(false); }
  }
  async submit(): Promise<void> {
    const w = this.wp.data();
    if (!w || this.busy()) return;
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
  async discard(): Promise<void> {
    const w = this.wp.data(); const s = this.state;
    if (!w || !s || this.busy()) return;
    this.busy.set(true);
    try {
      const r = await this.api.command(`/api/ui/audit/workpapers/${w.id}/draft/discard`, { expectedDraftRevision: s.draftRevision });
      this.report(r.ok, r.ok ? 'Draft discarded.' : r.message);
      if (r.ok) this.wp.reload();
    } finally { this.busy.set(false); }
  }
  reload(): void { this.message.set(''); this.wp.reload(); }
}
