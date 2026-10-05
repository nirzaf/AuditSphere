import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, decimalInput, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const procedure = obj({ id: guid, sourceProcedureId: text, sourceSectionNumber: nullable(int), sourceSectionTitle: nullable(text), title: text, applicabilityStatus: text, status: text });
export const decodeFieldwork = obj({ engagementId: guid, catalogProcedureCount: nat, catalogVersion: text,
  program: nullable(obj({ programCode: text, version: text, sourceHash: text })), procedures: arr(procedure, 5000),
  differences: arr(obj({ currency: text, differenceCount: nat, grossAmount: dec, signedNetAmount: dec, unadjustedGrossAmount: dec, unadjustedSignedNetAmount: dec,
    correctedGrossAmount: dec, correctedSignedNetAmount: dec }), 100),
  aggregate: nullable(obj({ id: guid, status: text, conclusion: text, preparedByMe: bool })), aggregateCurrentAndReviewed: bool,
  schedules: arr(obj({ id: guid, scheduleType: text, rowCount: nat, currency: text }), 1000), samplingMethods: arr(text, 20),
  samplingRuns: arr(obj({ id: guid, createdAt: instant, method: text, interval: nullable(dec), keyItemThreshold: nullable(dec), sampleSize: nullable(int), seed: nullable(int),
    selectedCount: nat, populationCount: nat, coveragePercent: dec, sourceDigest: text, reproduces: bool }), 10),
  evidenceCandidates: arr(obj({ uploadIntentId: guid, pbcRequestId: guid, requestArea: text, fileName: text, contentSha256: text, receivedAt: instant, superseded: bool }), 5000),
  physicalItems: arr(obj({ id: guid, fileIndex: text, boxReference: text, description: text, currentLocation: text,
    movements: arr(obj({ toLocation: text, movedAt: instant }), 1000), procedureTitles: arr(text, 1000) }), 5000),
  canManageFieldwork: bool, canViewReviewNotes: bool, canAddOrResolveReviewNotes: bool, canRespondToReviewNotes: bool });
export const decodeProcedureReview = obj({ procedureId: guid,
  currentResult: nullable(obj({ id: guid, revision: nat, workPerformed: text, conclusion: nullable(text) })),
  notes: arr(obj({ noteId: guid, resultRevision: nat, field: text, excerpt: text, startOffset: int, body: text, authorName: text, createdAt: instant, open: bool,
    events: arr(obj({ id: guid, kind: text, body: text, createdAt: instant }), 500) }), 500),
  evidence: arr(obj({ linkId: guid, uploadIntentId: guid, fileName: text, contentSha256: text, note: nullable(text), linkedAt: instant }), 1000) });
type Fieldwork = ReturnType<typeof decodeFieldwork>;

@Component({
  selector: 'audit-fieldwork',
  imports: [FormsModule, RouterLink, MatButtonModule, MatTabsModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a [routerLink]="['/app/engagements', id()]">Engagement</a> / <span>Audit fieldwork</span></nav>
    <audit-page-header title="Controlled audit fieldwork" eyebrow="Engagement audit" [description]="ws.data()?.canManageFieldwork ? 'Fieldwork procedures, differences, and aggregate conclusion on this engagement.' : 'Review submitted procedure results and resolve review notes for this engagement.'" />
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="fieldwork coverage" />
    @if (ws.data(); as w) {
      @if (w.program) { <p role="status">{{ w.procedures.length }} adopted procedures · {{ count(w, 'APPLICABLE') }} applicable · {{ reviewed(w) }} reviewed</p> }
      @if (w.canManageFieldwork || w.canViewReviewNotes) {
      <section class="panel" aria-labelledby="fieldwork-tools-heading">
        <h2 id="fieldwork-tools-heading">{{ w.canManageFieldwork ? 'Fieldwork tools' : 'Procedure review' }}</h2>
        @if (w.canManageFieldwork) { <a [routerLink]="['/app/engagements',id(),'confirmations']">Confirmation dashboard</a> }
        <mat-tab-group [selectedIndex]="toolTab()" (selectedIndexChange)="toolTab.set($event)">
          @if (w.canManageFieldwork) {
          <mat-tab label="Sampling">
            <p>Runs the sampling engine over an approved schedule. Parameters, seed and the exact source digest are logged, so the selection can be re-performed.</p>
            <form class="inline-form" (submit)="$event.preventDefault(); sample()">
              <label>Procedure <select name="sp" [(ngModel)]="s.procedure"><option value="">Select</option>@for (p of applicable(w); track p.id) { <option [value]="p.id">{{ p.sourceProcedureId }} · {{ p.title }}</option> }</select></label>
              <label>Approved schedule <select name="ss" [(ngModel)]="s.schedule"><option value="">Select</option>@for (x of w.schedules; track x.id) { <option [value]="x.id">{{ x.scheduleType }} · {{ x.rowCount }} rows ({{ x.currency }})</option> }</select></label>
              <label>Method <select name="sm" [(ngModel)]="s.method">@for (m of w.samplingMethods; track m) { <option [value]="m">{{ m === 'SYSTEMATIC' ? 'Systematic random' : m }}</option> }</select></label>
              @if (s.method === 'MUS') { <label>Interval <input name="si" inputmode="decimal" [(ngModel)]="s.interval" /></label> }
              @if (s.method === 'KEY_ITEM' || s.method === 'STRATIFIED') { <label>Key-item threshold <input name="sk" inputmode="decimal" [(ngModel)]="s.key" /></label> }
              @if (countBased()) {
                <label>Sample size <input name="sz" type="number" min="1" [(ngModel)]="s.size" /></label>
                <label>Seed <input name="sd" type="number" [(ngModel)]="s.seed" /></label>
              }
              <label>Rationale <input name="sr" [(ngModel)]="s.rationale" maxlength="500" /></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">Run sampling</button>
            </form>
            @if (w.samplingRuns.length) {
              <div class="table-scroll"><table aria-label="Sampling calculation log">
                <thead><tr><th>When</th><th>Method</th><th>Parameters</th><th>Selected</th><th>Coverage</th><th>Re-performed</th></tr></thead>
                <tbody>@for (r of w.samplingRuns; track r.id) {
                  <tr><td>{{ r.createdAt.slice(0, 16).replace('T', ' ') }}</td><td>{{ r.method }}</td><td>{{ parameters(r) }}</td><td>{{ r.selectedCount }} of {{ r.populationCount }}</td>
                    <td>{{ r.coveragePercent | money }}%</td><td [class.error-text]="!r.reproduces">{{ r.reproduces ? 'Matches' : 'Source changed' }}</td></tr> }</tbody>
              </table></div>
            }
          </mat-tab>
          <mat-tab label="Client evidence">
            <p>Link a received client upload to a procedure. The link pins the provider receipt digest; a superseded version cannot be linked.</p>
            <form class="inline-form" (submit)="$event.preventDefault(); linkEvidence()">
              <label>Procedure <select name="ep" [(ngModel)]="e.procedure" (ngModelChange)="selectProcedure($event)"><option value="">Select</option>@for (p of applicable(w); track p.id) { <option [value]="p.id">{{ p.sourceProcedureId }} · {{ p.title }}</option> }</select></label>
              <label>Received upload <select name="eu" [(ngModel)]="e.upload"><option value="">Select</option>@for (c of w.evidenceCandidates; track c.uploadIntentId) { <option [value]="c.uploadIntentId">{{ c.requestArea }} · {{ c.fileName }} · {{ c.receivedAt.slice(0, 16).replace('T', ' ') }}{{ c.superseded ? ' (superseded)' : '' }}</option> }</select></label>
              <label>Note <input name="en" [(ngModel)]="e.note" maxlength="500" /></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">Link evidence</button>
            </form>
            @if (review()?.evidence?.length) { <ul aria-label="Linked client evidence">@for (l of review()!.evidence; track l.linkId) { <li>{{ l.fileName }} <code>{{ l.contentSha256.slice(0, 12) }}</code>{{ l.note ? ' — ' + l.note : '' }}</li> }</ul> }
          </mat-tab>
          <mat-tab label="Physical files">
            <form class="inline-form" (submit)="$event.preventDefault(); register()">
              <label>File index <input name="pi" [(ngModel)]="ph.index" maxlength="40" placeholder="X-1" /></label>
              <label>Box <input name="pb" [(ngModel)]="ph.box" maxlength="40" placeholder="Box 3" /></label>
              <label>Description <input name="pd" [(ngModel)]="ph.description" maxlength="300" /></label>
              <label>Location <input name="pl" [(ngModel)]="ph.location" maxlength="200" /></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">Register file</button>
            </form>
            @for (item of w.physicalItems; track item.id) {
              <article class="panel" [attr.data-file-index]="item.fileIndex">
                <p><strong>{{ item.fileIndex }}</strong> · {{ item.boxReference }} · {{ item.description }} — at <em>{{ item.currentLocation }}</em></p>
                <p><small>History: {{ history(item.movements) }}</small></p>
                <p><small>Procedures: {{ item.procedureTitles.length ? item.procedureTitles.join(', ') : 'none' }}</small></p>
                <div class="inline-form">
                  <label>Link to procedure <select [(ngModel)]="ph.linkTarget" [name]="'pt-' + item.id" [attr.aria-label]="'Procedure for ' + item.fileIndex"><option value="">Select</option>@for (p of applicable(w); track p.id) { <option [value]="p.id">{{ p.sourceProcedureId }} · {{ p.title }}</option> }</select></label>
                  <button matButton (click)="linkPhysical(item.id)" [disabled]="cmd.busy() || !ph.linkTarget" [attr.aria-label]="'Link ' + item.fileIndex">Link</button>
                  <label>Move to <input [(ngModel)]="ph.moveTo" [name]="'mv-' + item.id" [attr.aria-label]="'New location for ' + item.fileIndex" /></label>
                  <button matButton (click)="move(item.id)" [disabled]="cmd.busy()" [attr.aria-label]="'Move ' + item.fileIndex">Record move</button>
                </div>
              </article>
            }
          </mat-tab>
          <mat-tab label="Ad hoc steps">
            <p>Add a step for an unusual risk or transaction. The adopted programme's baseline is untouched; the step must be performed and reviewed before completion.</p>
            <form class="inline-form" (submit)="$event.preventDefault(); adHoc()">
              <label>Title <input name="at" [(ngModel)]="ah.title" maxlength="300" /></label>
              <label>Step <textarea name="aw" [(ngModel)]="ah.wording" maxlength="4000" rows="2"></textarea></label>
              <label>Why it is needed <input name="ar" [(ngModel)]="ah.reason" maxlength="500" /></label>
              <label>Audit area <input name="as" [(ngModel)]="ah.section" maxlength="100" /></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">Insert step</button>
            </form>
          </mat-tab>
          }
          @if (w.canViewReviewNotes) {
          <mat-tab label="Review notes">
            <p>Anchor a note to text in the current submitted result. A result cannot be approved while a note is open; the preparer responds and a reviewer resolves.</p>
            @if (reviewable(w).length) {
              <label>Procedure <select [(ngModel)]="e.procedure" name="rp" (ngModelChange)="selectProcedure($event)"><option value="">Select</option>@for (p of reviewable(w); track p.id) { <option [value]="p.id">{{ p.sourceProcedureId }} · {{ p.title }}</option> }</select></label>
            } @else { <p role="status">No submitted procedure results are ready for review.</p> }
            @if (reviewError()) { <p role="alert" class="error-text">{{ reviewError() }}</p> }
            @if (review()?.currentResult; as r) {
              <div aria-label="Current submitted result">
                <p><strong>Revision {{ r.revision }} — work performed:</strong> {{ r.workPerformed }}</p>
                <p><strong>Conclusion:</strong> {{ r.conclusion }}</p>
              </div>
              @if (w.canAddOrResolveReviewNotes) {
              <form class="inline-form" (submit)="$event.preventDefault(); addNote(r.id)">
                <label>Field <select name="nf" [(ngModel)]="n.field"><option value="WORK_PERFORMED">Work performed</option><option value="CONCLUSION">Conclusion</option></select></label>
                <label>Quoted text <input name="ne" [(ngModel)]="n.excerpt" maxlength="500" /></label>
                <label>Note <input name="nb" [(ngModel)]="n.body" maxlength="4000" /></label>
                <button matButton="filled" type="submit" [disabled]="cmd.busy()">Add note</button>
              </form>
              }
            }
            @for (note of review()?.notes ?? []; track note.noteId) {
              <article class="panel" [attr.data-note]="note.excerpt">
                <p><mark>“{{ note.excerpt }}”</mark> (r{{ note.resultRevision }}, {{ note.field.toLowerCase().replace('_', ' ') }}) — {{ note.body }} <small>{{ note.authorName }}</small> <strong>{{ note.open ? 'Open' : 'Resolved' }}</strong></p>
                @for (ev of note.events; track ev.id) { <p><small>{{ ev.kind.toLowerCase() }}: {{ ev.body }}</small></p> }
                <div class="inline-form">
                  @if (w.canRespondToReviewNotes) {
                    <label>Reply <input [(ngModel)]="n.reply" [name]="'nr-' + note.noteId" [attr.aria-label]="'Reply to note on ' + note.excerpt" /></label>
                    <button matButton (click)="noteEvent(note.noteId, 'RESPONSE')" [disabled]="cmd.busy()">Respond</button>
                  }
                  @if (note.open && w.canAddOrResolveReviewNotes) { <button matButton (click)="noteEvent(note.noteId, 'RESOLVED')" [disabled]="cmd.busy()" [attr.aria-label]="'Resolve note on ' + note.excerpt">Resolve</button> }
                </div>
              </article>
            }
          </mat-tab>
          }
        </mat-tab-group>
      </section>
      }

      @if (w.canManageFieldwork) {
      <section class="panel" aria-labelledby="program-heading">
        <h2 id="program-heading">Versioned audit program</h2>
        <p>The controlled source catalog contains {{ w.catalogProcedureCount }} procedures across 20 sections. Applicability and evidence status are persisted per engagement.</p>
        @if (w.program; as p) {
          <dl class="facts"><dt>Program</dt><dd>{{ p.programCode }} v{{ p.version }}</dd><dt>Source hash</dt><dd><code>{{ p.sourceHash }}</code></dd>
            <dt>Adopted procedures</dt><dd>{{ w.procedures.length }}</dd><dt>Applicable / reviewed</dt><dd>{{ count(w, 'APPLICABLE') }} / {{ reviewed(w) }}</dd></dl>
          <p class="actions"><a [routerLink]="['/app/engagements', w.engagementId, 'audit-plan']">Open planning</a><a routerLink="/app/audit/library">Program library</a><a [routerLink]="['/app/engagements', w.engagementId, 'completion']">Open completion</a></p>
        } @else {
          <p>No published program has been adopted for this engagement.</p>
          <button matButton="filled" (click)="send('/api/ui/engagements/' + w.engagementId + '/fieldwork/program', {}, 'Program published and adopted.')" [disabled]="cmd.busy()">Publish and adopt {{ w.catalogVersion }}</button>
        }
      </section>

      @if (w.differences.length) {
        <section class="panel" aria-labelledby="difference-summary-heading">
          <h2 id="difference-summary-heading">Aggregate differences and reporting assessment</h2>
          <p>Review gross and signed amounts separately by currency. These totals do not determine materiality or an audit opinion; the engagement team records the professional conclusion.</p>
          <div class="table-scroll"><table>
            <thead><tr><th scope="col">Currency</th><th scope="col" class="number">Count</th><th scope="col" class="number">Gross</th><th scope="col" class="number">Signed/net</th><th scope="col" class="number">Unadjusted gross</th><th scope="col" class="number">Unadjusted net</th><th scope="col" class="number">Corrected gross</th></tr></thead>
            <tbody>@for (d of w.differences; track d.currency) { <tr><td>{{ d.currency }}</td><td class="number">{{ d.differenceCount }}</td><td class="number">{{ d.grossAmount | money }}</td><td class="number">{{ d.signedNetAmount | money }}</td>
              <td class="number">{{ d.unadjustedGrossAmount | money }}</td><td class="number">{{ d.unadjustedSignedNetAmount | money }}</td><td class="number">{{ d.correctedGrossAmount | money }}</td></tr> }</tbody>
          </table></div>
          @if (w.aggregate; as a) {
            <dl class="facts"><dt>Assessment</dt><dd>{{ a.status }}</dd><dt>Current and independently reviewed</dt><dd>{{ w.aggregateCurrentAndReviewed ? 'Yes' : 'No — refresh the assessment before completion' }}</dd>
              <dt>Professional conclusion / reporting impact</dt><dd>{{ a.conclusion }}</dd></dl>
            @if (a.status !== 'REVIEWED' && !a.preparedByMe) { <button matButton="filled" (click)="send('/api/ui/area-assessments/' + a.id + '/review', {}, 'Aggregate conclusion reviewed.')" [disabled]="cmd.busy()">Review aggregate conclusion</button> }
          }
          <form (submit)="$event.preventDefault(); aggregate(w)">
            <label>Required: professional aggregate conclusion and reporting impact <textarea name="agg" [(ngModel)]="aggregateConclusion" maxlength="4000" rows="4" required></textarea></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy() || !aggregateConclusion.trim()">Record aggregate conclusion</button>
          </form>
        </section>
      }

      @if (w.program) {
        <section class="panel" aria-labelledby="coverage-heading">
          <h2 id="coverage-heading">Procedure applicability and evidence coverage</h2>
          <p>An applicable procedure cannot be completed from a title alone. Submit work, evidence and a current independent review through the guarded commands.</p>
          <div class="inline-form">
            <label>Audit section <select name="section" [ngModel]="section() ?? firstSection(w)" (ngModelChange)="section.set($event)">
              <option [ngValue]="0">All sections</option>
              @for (g of sections(w); track g.key) { <option [ngValue]="g.key">{{ g.key === -2 ? 'Unclassified' : 'Section ' + g.key }} — {{ g.title }} ({{ g.count }})</option> }</select></label>
            <span>Showing {{ visible(w).length }} of {{ w.procedures.length }} authorized procedures</span>
          </div>
          <div class="table-scroll"><table>
            <thead><tr><th scope="col">Source</th><th scope="col">Section</th><th scope="col">Procedure</th><th scope="col">Applicability</th><th scope="col">Evidence</th><th scope="col">Action</th></tr></thead>
            <tbody>@for (p of visible(w); track p.id) {
              <tr [id]="'procedure-' + p.id"><td><code>{{ p.sourceProcedureId }}</code></td><td>{{ p.sourceSectionTitle }}</td><td>{{ p.title }}</td><td>{{ p.applicabilityStatus }}</td><td>{{ p.status }}</td>
                <td>@switch (p.applicabilityStatus) {
                  @case ('PENDING') { <span class="actions">
                    <button matButton="filled" (click)="decide(p.id, 'APPLICABLE')" [disabled]="cmd.busy()">Applicable</button>
                    <button matButton="filled" (click)="decide(p.id, 'NA_PENDING_REVIEW')" [disabled]="cmd.busy()">N/A for review</button></span> }
                  @case ('APPLICABLE') { <small>Execute from source workflow</small> }
                  @default { <small>Reviewer disposition required</small> } }</td></tr> }</tbody>
          </table></div>
        </section>
      }
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class AuditFieldwork {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/fieldwork` : null), decodeFieldwork,
    'Fieldwork unavailable: an authorized internal engagement scope is required.');
  readonly cmd = new CommandState(this.api);
  readonly section = signal<number | null>(null);
  readonly toolTab = signal(0);
  readonly review = signal<ReturnType<typeof decodeProcedureReview> | null>(null);
  readonly reviewError = signal('');
  s = { procedure: '', schedule: '', method: 'MUS', interval: '', key: '', size: null as number | null, seed: null as number | null, rationale: '' };
  e = { procedure: '', upload: '', note: '' };
  ph = { index: '', box: '', description: '', location: '', linkTarget: '', moveTo: '' };
  ah = { title: '', wording: '', reason: '', section: '' };
  n = { field: 'WORK_PERFORMED', excerpt: '', body: '', reply: '' };
  aggregateConclusion = '';
  readonly countBased = () => ['RANDOM', 'SYSTEMATIC', 'STRATIFIED'].includes(this.s.method);

  constructor() {
    // Seed the conclusion editor from the persisted assessment whenever the workspace reloads.
    effect(() => { const w = this.ws.data(); untracked(() => (this.aggregateConclusion = w?.aggregate?.conclusion ?? '')); });
  }
  count(w: Fieldwork, status: string): number { return w.procedures.filter((p) => p.applicabilityStatus === status).length; }
  reviewed(w: Fieldwork): number { return w.procedures.filter((p) => p.status === 'REVIEWED').length; }
  applicable(w: Fieldwork) { return w.procedures.filter((p) => p.applicabilityStatus === 'APPLICABLE'); }
  reviewable(w: Fieldwork) { return w.canManageFieldwork ? this.applicable(w) : w.procedures; }
  sections(w: Fieldwork) {
    const map = new Map<number, { key: number; title: string; count: number }>();
    for (const p of w.procedures) {
      const key = p.sourceSectionNumber ?? -2;
      const g = map.get(key) ?? { key, title: p.sourceSectionTitle ?? '', count: 0 };
      g.count++; map.set(key, g);
    }
    return [...map.values()].sort((a, b) => a.key - b.key);
  }
  firstSection(w: Fieldwork): number { return w.procedures.length ? (w.procedures[0].sourceSectionNumber ?? -2) : 0; }
  visible(w: Fieldwork) {
    const s = this.section() ?? this.firstSection(w);
    return s === 0 ? w.procedures : w.procedures.filter((p) => (p.sourceSectionNumber ?? -2) === s);
  }
  parameters(r: { interval: string | null; keyItemThreshold: string | null; sampleSize: number | null; seed: number | null; sourceDigest: string }): string {
    return [r.interval ? `interval ${r.interval}` : null, r.keyItemThreshold ? `key items ≥ ${r.keyItemThreshold}` : null, r.sampleSize !== null ? `size ${r.sampleSize}` : null,
      r.seed !== null ? `seed ${r.seed}` : null, `source ${r.sourceDigest.slice(0, 10)}`].filter((x) => x).join(', ');
  }
  history(m: { toLocation: string; movedAt: string }[]): string { return m.map((x) => `${x.toLocation} (${x.movedAt.slice(0, 10)})`).join(' → '); }
  send(url: string, body: unknown, ok: string, after?: () => void): Promise<boolean> {
    return this.cmd.run(url, body, ok, after).finally(() => this.ws.reload());
  }
  private invalid(m: string): void { this.cmd.failed.set(true); this.cmd.message.set(m); }
  private base(): string { return `/api/ui/engagements/${this.id()}/fieldwork`; }
  decide(procedureId: string, decision: string): void { void this.send(`/api/ui/procedures/${procedureId}/applicability`, { decision }, 'Recorded.'); }
  aggregate(w: Fieldwork): void { void this.send(this.base() + '/aggregate', { conclusion: this.aggregateConclusion }, 'Aggregate conclusion recorded.'); }
  sample(): void {
    const s = this.s;
    const interval = s.interval.trim() ? decimalInput(s.interval) : '', key = s.key.trim() ? decimalInput(s.key) : '';
    if (!s.procedure || !s.schedule || interval === null || key === null) return this.invalid('Choose a procedure and an approved schedule, and enter amounts as numbers.');
    this.cmd.run<{ selectedCount: number; populationCount: number; coveragePercent: string }>(this.base() + '/sampling', { procedureId: s.procedure, scheduleId: s.schedule,
      method: s.method, interval: interval || null, keyItemThreshold: key || null, sampleSize: s.size, seed: s.seed, rationale: s.rationale }, '',
      (v) => this.cmd.message.set(`Selected ${v.selectedCount} of ${v.populationCount} items (${v.coveragePercent}% coverage); the selection awaits review.`)).finally(() => this.ws.reload());
  }
  async selectProcedure(procedureId: string): Promise<void> {
    this.review.set(null); this.reviewError.set('');
    if (!procedureId) return;
    try { this.review.set(await this.api.get(`/api/ui/procedures/${procedureId}/review`, decodeProcedureReview)); } catch (e) { this.reviewError.set((e as Error).message); }
  }
  private refreshReview = () => { void this.selectProcedure(this.e.procedure); };
  linkEvidence(): void {
    if (!this.e.procedure || !this.e.upload) return this.invalid('Choose a procedure and a received upload.');
    void this.send(`/api/ui/procedures/${this.e.procedure}/evidence`, { uploadIntentId: this.e.upload, note: this.e.note || null }, 'Evidence linked.', this.refreshReview);
  }
  register(): void {
    const index = this.ph.index;
    void this.send(this.base() + '/physical', { fileIndex: index, boxReference: this.ph.box, description: this.ph.description, location: this.ph.location },
      `Registered ${index.trim().toUpperCase()}.`, () => (this.ph = { ...this.ph, index: '', box: '', description: '', location: '' }));
  }
  linkPhysical(itemId: string): void { void this.send(`/api/ui/physical-items/${itemId}/link`, { procedureId: this.ph.linkTarget }, 'Physical file linked.'); }
  move(itemId: string): void { void this.send(`/api/ui/physical-items/${itemId}/move`, { toLocation: this.ph.moveTo }, 'Movement recorded.', () => (this.ph.moveTo = '')); }
  adHoc(): void {
    void this.send(this.base() + '/ad-hoc', { title: this.ah.title, wording: this.ah.wording, reason: this.ah.reason, sectionTitle: this.ah.section || null },
      'Ad hoc step inserted; it must be performed and reviewed before completion.', () => (this.ah = { title: '', wording: '', reason: '', section: '' }));
  }
  addNote(resultId: string): void {
    void this.send('/api/ui/review-notes', { resultId, field: this.n.field, excerpt: this.n.excerpt, body: this.n.body }, 'Note added.',
      () => { this.n = { ...this.n, excerpt: '', body: '' }; this.refreshReview(); });
  }
  noteEvent(noteId: string, kind: 'RESPONSE' | 'RESOLVED'): void {
    void this.send(`/api/ui/review-notes/${noteId}/events`, { kind, body: this.n.reply }, kind === 'RESOLVED' ? 'Note resolved.' : 'Response added.',
      () => { this.n.reply = ''; this.refreshReview(); });
  }
}
