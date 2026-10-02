import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const deliverable = obj({ id: guid, kind: text, title: text, version: nat, signed: bool, current: bool, contentSha256: text, createdAt: instant });
export const decodeCompletion = obj({ engagementId: guid,
  gates: arr(obj({ name: text, status: text, tone: text, authority: nullable(text), date: nullable(instant) }), 50),
  representations: arr(obj({ code: text, title: text, narrative: text, obtained: bool }), 500),
  packageId: nullable(guid), packageStatus: text, partnerApproved: bool, eqrStatus: text, releaseCandidateId: nullable(guid), canPrepareRelease: bool,
  confirmations: arr(obj({ caseId: guid, type: text, respondent: text, bookedAmount: dec, currency: text, status: text, monitoring: text,
    daysSinceDispatch: nullable(int), critical: bool, criticalityRationale: nullable(text) }), 2000),
  deliverables: arr(deliverable, 500), clearance: nullable(obj({ clearedAt: instant })),
  opinion: nullable(obj({ opinionType: text, label: text, focusArea: nullable(text) })),
  opinionAreas: arr(obj({ id: guid, code: text, name: text }), 2000),
  shared: arr(obj({ title: text, openComments: arr(obj({ id: guid, body: text }), 500) }), 500),
  signedLetters: arr(obj({ id: guid, deliverableId: guid, version: nat, managementSignatory: text, contentSha256: text, uploadedAt: instant, verified: bool, current: bool }), 100),
  bundles: nullable(obj({ blockers: arr(text, 100), bundles: arr(obj({ id: guid, sha256: text, assembledAt: instant }), 100) })),
  opinionTypes: arr(text, 10),
  freeze: nullable(obj({ state: text, reportSignedAt: instant, dueAt: instant, externalReadOnly: text, daysRemaining: int,
    amendments: arr(obj({ id: guid, reason: text, openedAt: nullable(instant), closedAt: nullable(instant) }), 500) })),
  freezeDays: nat, trail: nullable(arr(obj({ at: instant, kind: text, actor: text, description: text, entityId: guid }), 20000)), trailCoverageNote: text,
  locks: arr(obj({ id: guid, documentKey: text, lockedBy: text, lockedAt: instant }), 500) });
type Completion = ReturnType<typeof decodeCompletion>;
const OPINION_LABELS: Record<string, string> = { UNMODIFIED: 'Clean (unmodified)', QUALIFIED: 'Qualified', ADVERSE: 'Adverse', DISCLAIMER: 'Disclaimer of opinion' };
const REPORTS: [string, string][] = [['AUDIT_FINDINGS_REPORT', 'Audit Findings Report'], ['MANAGEMENT_LETTER', 'Management Letter'],
  ['REPRESENTATION_LETTER', 'Representation Letter'], ['INDEPENDENT_AUDITORS_REPORT', "Independent Auditor's Report"]];

@Component({
  selector: 'audit-completion',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a [routerLink]="['/app/engagements', id()]">Engagement</a> / <span>Completion</span></nav>
    <audit-page-header title="Engagement completion checklist" eyebrow="Engagement audit" description="Completion gates, unresolved items, and the exact state required before release." />
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="completion state" />
    @if (ws.data(); as w) {
      <p class="actions" aria-label="Completion navigation"><a [routerLink]="['/app/engagements', w.engagementId, 'audit-plan']">Audit plan</a>
        <a [routerLink]="['/app/engagements', w.engagementId, 'audit-fieldwork']">Fieldwork control center</a><a [routerLink]="['/app/engagements', w.engagementId]">Engagement</a></p>

      <section class="panel" aria-labelledby="deliverables-heading">
        <h2 id="deliverables-heading">Review, opinion and deliverables</h2>
        <h3>Third-party confirmations</h3><a [routerLink]="['/app/engagements',id(),'confirmations']">Open confirmation dashboard</a>
        <div class="table-scroll"><table aria-label="Confirmations dashboard">
          <thead><tr><th>Type</th><th>Respondent</th><th class="number">Amount</th><th>Monitoring</th><th>Critical</th><th><span class="sr-only">Action</span></th></tr></thead>
          <tbody>@for (c of w.confirmations; track c.caseId) {
            <tr><td>{{ c.type }}</td><td>{{ c.respondent }}</td><td class="number">{{ c.bookedAmount | money }} {{ c.currency }}</td>
              <td><audit-status [value]="c.monitoring" />{{ c.daysSinceDispatch !== null ? ' ' + c.daysSinceDispatch + ' day(s)' : '' }}</td><td>{{ c.critical ? 'Critical' : '—' }}</td>
              <td><a [routerLink]="['/app/engagements',id(),'confirmations']">Review criticality and evidence</a></td></tr>
          } @empty { <tr><td colspan="6">No confirmations are recorded.</td></tr> }</tbody>
        </table></div>

        <h3>Summary Review Memorandum and Partner clearance</h3>
        <label>Reviewer recommendations <textarea name="rec" [(ngModel)]="f.recommendations" rows="2" maxlength="8000"></textarea></label>
        <button matButton="outlined" (click)="send(base() + '/srm', { recommendations: f.recommendations }, 'Summary Review Memorandum generated.')" [disabled]="cmd.busy()">Generate Summary Review Memorandum</button>
        @if (srm(w); as s) {
          <p>Latest SRM v{{ s.version }} is <strong>{{ s.current ? 'current' : 'stale' }}</strong>. {{ w.clearance ? 'Cleared by the Partner on ' + w.clearance.clearedAt.slice(0, 16).replace('T', ' ') + '.' : 'No current Partner clearance.' }}</p>
          <label>Key risk areas reviewed <input name="risks" [(ngModel)]="f.risks" maxlength="2000" /></label>
          <label>Financial-statement notes reviewed <input name="notes" [(ngModel)]="f.notes" maxlength="2000" /></label>
          <button matButton="filled" (click)="send(base() + '/clearance', { srmId: s.id, keyRiskAreasComment: f.risks, notesComment: f.notes }, 'Partner clearance recorded.')" [disabled]="cmd.busy()">Record Partner clearance</button>
        }

        <h3>Audit opinion</h3>
        <div class="inline-form">
          <label>Opinion <select name="op" [(ngModel)]="f.opinion">@for (t of w.opinionTypes; track t) { <option [value]="t">{{ label(t) }}</option> }</select></label>
          @if (f.opinion !== 'UNMODIFIED') {
            <label>Affected FSLI * <select name="focus" [(ngModel)]="f.focus" required><option value="">Select from the approved reporting taxonomy</option>
              @for (a of w.opinionAreas; track a.id) { <option [value]="a.id">{{ a.code }} — {{ a.name }}</option> }</select></label>
            @if (!w.opinionAreas.length) { <p role="alert">Approve the applicable reporting taxonomy before recording a modified opinion.</p> }
          }
        </div>
        @if (f.opinion !== 'UNMODIFIED') {
          <label>{{ f.opinion === 'QUALIFIED' ? 'Basis for Qualified Opinion' : f.opinion === 'ADVERSE' ? 'Basis for Adverse Opinion' : 'Basis for Disclaimer of Opinion' }}
            <textarea name="basis" [(ngModel)]="f.basis" rows="3" maxlength="4000" required></textarea></label>
        }
        <button matButton="filled" (click)="send(base() + '/opinion', { opinionType: f.opinion, focusArea: f.focus || null, basis: f.basis || null }, label(f.opinion) + ' opinion recorded.')" [disabled]="cmd.busy()">Record opinion (Engagement Partner)</button>
        @if (w.opinion; as o) { <p>Current opinion: <strong>{{ o.label }}</strong>{{ o.focusArea ? ' — ' + o.focusArea : '' }}.</p> }

        <h3>Deliverables</h3>
        <p class="actions">@for (r of reports; track r[0]) { <button matButton="outlined" (click)="report(r[0])" [disabled]="cmd.busy()">Generate {{ r[1] }}</button> }</p>
        @if (w.deliverables.length) {
          <div class="table-scroll"><table aria-label="Generated deliverables">
            <thead><tr><th>Document</th><th>Version</th><th>State</th><th>SHA-256</th><th><span class="sr-only">Actions</span></th></tr></thead>
            <tbody>@for (d of w.deliverables; track d.id) {
              <tr><td><a [href]="'/api/deliverables/' + d.id + '/download'">{{ d.title }}</a></td><td>v{{ d.version }}{{ d.signed ? ' signed' : '' }}</td><td>{{ d.current ? 'Current' : 'Stale' }}</td><td><code>{{ d.contentSha256.slice(0, 12) }}</code></td>
                <td class="actions">
                  @if (!d.signed && d.kind !== 'SUMMARY_REVIEW_MEMORANDUM' && d.kind !== 'HOLDING_LETTER') { <button matButton (click)="send('/api/ui/deliverables/' + d.id + '/share', {}, 'Shared with client management.')" [disabled]="cmd.busy()" [attr.aria-label]="'Share ' + d.title + ' v' + d.version + ' with the client'">Share with client</button> }
                  @if (!d.signed && d.kind === 'INDEPENDENT_AUDITORS_REPORT' && d.current) { <button matButton="filled" (click)="send('/api/ui/deliverables/' + d.id + '/sign', {}, 'Report signed; the signed version and its hash are recorded.')" [disabled]="cmd.busy()">Sign with registered signature</button> }
                </td></tr> }</tbody>
          </table></div>
        }
        @for (s of w.shared; track s.title) { @for (c of s.openComments; track c.id) {
          <div class="inline-form"><span>Client comment on {{ s.title }}: “{{ c.body }}”</span>
            <label>Resolution <input [(ngModel)]="f.resolution" [name]="'res-' + c.id" [attr.aria-label]="'Resolution for comment on ' + s.title" /></label>
            <button matButton (click)="send('/api/ui/deliverable-comments/' + c.id + '/resolve', { resolution: f.resolution }, 'Comment resolved.')" [disabled]="cmd.busy()">Resolve</button></div> } }

        <h3>Management-signed representation letter</h3>
        <p>Client management uploads the signed PDF for the exact shared version in the portal. The Engagement Partner verifies the signature, authority and completeness before signing the report.</p>
        @for (scan of w.signedLetters; track scan.id) {
          <div [attr.data-signed-letter]="scan.id">
            <a [href]="'/api/representation-scans/' + scan.id + '/download'">Management-signed LOR v{{ scan.version }} — {{ scan.managementSignatory }}</a>
            <audit-status [value]="scan.current ? (scan.verified ? 'VERIFIED' : 'PENDING_REVIEW') : 'STALE'" /> <code>{{ scan.contentSha256.slice(0, 12) }}</code>
            @if (scan.current && !scan.verified) {
              <label>Signature verification evidence <textarea [(ngModel)]="f.scanReason" [name]="'sr-' + scan.id" maxlength="2000" aria-label="Signature verification evidence"></textarea></label>
              <label><span><input type="checkbox" [(ngModel)]="f.scanReviewed" [name]="'sv-' + scan.id" /> I reviewed this exact scan and verified management authority, signature and completeness.</span></label>
              <button matButton="outlined" (click)="send('/api/ui/representation-scans/' + scan.id + '/verify', { expectedSha256: scan.contentSha256, reason: f.scanReason, reviewed: f.scanReviewed }, 'Signed representation verified for its exact version.')" [disabled]="cmd.busy() || !f.scanReviewed">Verify signed representation (Partner)</button>
            }
          </div>
        } @empty { <p>No signed representation scan has been uploaded.</p> }

        <h3>Five-part final bundle</h3>
        <p>Signed report and certified financial statements; management letter; representation template and management-signed scan; correspondence audit trail; posted final balance fee note.</p>
        @if (w.bundles; as b) {
          <ul class="blockers">@for (x of b.blockers; track $index) { <li>{{ x }}</li> }</ul>
          @for (x of b.bundles; track x.id) { <p><a [href]="'/api/deliverable-bundles/' + x.id + '/download'">Download five-part final bundle</a> <code>{{ x.sha256.slice(0, 12) }}</code> {{ x.assembledAt.slice(0, 10) }}</p> }
        }
        <label><span><input type="checkbox" name="stmts" [(ngModel)]="f.statementsReviewed" /> I reviewed the exact released financial statements for certification with this report.</span></label>
        <button matButton="filled" (click)="send(base() + '/bundle', { statementsReviewed: f.statementsReviewed }, 'Five-part final bundle assembled and available to the assigned client.')" [disabled]="cmd.busy() || !f.statementsReviewed || !w.bundles || w.bundles.blockers.length > 0">Assemble five-part bundle (Partner)</button>
        <h4>Partner signature specimen</h4>
        <p><small>A PNG image placed into the signed report. It is not a cryptographic signature; the signed document's hash is the evidence.</small></p>
        <label>Signature PNG <input type="file" accept=".png" (change)="upload($event, '/api/ui/signatures', 'Signature specimen registered.')" /></label>
        <h4>Official firm seal</h4>
        <p><small>A firm-wide Partner or Administrator registers the approved seal. Each report retains the exact seal version used.</small></p>
        <label>Seal PNG <input type="file" accept=".png" (change)="upload($event, '/api/ui/firm-seal', 'Approved firm seal registered.')" /></label>
      </section>

      <section class="panel" aria-labelledby="records-heading">
        <h2 id="records-heading">File freeze and activity trail</h2>
        @if (w.freeze; as fz) {
          <p><audit-status [value]="fz.state" /> Report signed {{ fz.reportSignedAt.slice(0, 10) }}; freeze due {{ fz.dueAt.slice(0, 10) }}{{ fz.state === 'SCHEDULED' ? ' (' + fz.daysRemaining + ' day(s) remaining)' : '' }}.</p>
          <p><small>SharePoint read-only: <strong>{{ fz.externalReadOnly }}</strong>. {{ fz.externalReadOnly === 'BLOCKED_EXTERNAL' ? 'AuditSphere refuses changes to the frozen file; the SharePoint folder is not confirmed read-only until an authorized tenant change is observed.' : '' }}</small></p>
          @if (fz.state === 'FROZEN') {
            <label>Reason for amendment <input name="amend" [(ngModel)]="f.amendment" maxlength="2000" /></label>
            <button matButton="outlined" (click)="send('/api/ui/engagements/' + w.engagementId + '/amendments', { reason: f.amendment }, 'Amendment requested; another Partner must approve it.')" [disabled]="cmd.busy()">Request amendment</button>
          }
          @for (a of fz.amendments; track a.id) {
            <div class="inline-form"><span>{{ a.reason }} — {{ a.closedAt ? 'closed' : a.openedAt ? 'open' : 'awaiting Partner approval' }}</span>
              @if (!a.openedAt) { <button matButton (click)="send('/api/ui/amendments/' + a.id + '/approve', {}, 'Amendment approved; the file is open for the documented change.')" [disabled]="cmd.busy()">Approve (another Partner)</button> }
              @else if (!a.closedAt) { <button matButton (click)="send('/api/ui/amendments/' + a.id + '/close', {}, 'Amendment closed; the file is frozen again.')" [disabled]="cmd.busy()">Close and re-freeze</button> }</div>
          }
        } @else { <p>The freeze is scheduled when the Engagement Partner signs the Independent Auditor's Report; the file then freezes {{ w.freezeDays }} days later.</p> }
        <h3>Document locks</h3>
        <div class="inline-form">
          <label>Document <input name="lock" [(ngModel)]="f.lockKey" maxlength="200" placeholder="e.g. WP-A1" /></label>
          <button matButton="outlined" (click)="send('/api/ui/engagements/' + w.engagementId + '/locks', { documentKey: f.lockKey }, 'Document locked for you.')" [disabled]="cmd.busy()">Lock for editing</button>
        </div>
        @for (l of w.locks; track l.id) {
          <div class="inline-form"><span>{{ l.documentKey }} locked by {{ l.lockedBy }} since {{ l.lockedAt.slice(0, 16).replace('T', ' ') }}</span>
            <button matButton (click)="send('/api/ui/locks/' + l.id + '/release', {}, 'Lock released.')" [disabled]="cmd.busy()" [attr.aria-label]="'Release lock on ' + l.documentKey">Release</button></div>
        }
        <h3>Activity trail</h3>
        <p><small>{{ w.trailCoverageNote }}</small></p>
        <label>Show <select name="kind" [ngModel]="kind()" (ngModelChange)="kind.set($event)"><option value="">All activity</option>
          @for (k of kinds; track k) { <option [value]="k">{{ k }}</option> }</select></label>
        @if (w.trail; as t) {
          <div class="table-scroll"><table aria-label="Activity trail">
            <thead><tr><th>When</th><th>Kind</th><th>Who</th><th>What</th></tr></thead>
            <tbody>@for (e of filtered(t); track $index) { <tr><td>{{ e.at.slice(0, 19).replace('T', ' ') }}</td><td>{{ e.kind }}</td><td>{{ e.actor }}</td><td>{{ e.description }}</td></tr> }</tbody>
          </table></div>
        } @else { <p>The activity trail is available to Managers, Partners and reviewers.</p> }
      </section>

      <p role="status">{{ w.gates.length }} gate rows · {{ obtained(w) }} / {{ w.representations.length }} representations obtained · release candidate {{ w.releaseCandidateId ? 'recorded' : 'not recorded' }}</p>
      <section class="panel" aria-labelledby="gates-heading">
        <h2 id="gates-heading">Completion gates</h2>
        <p>All gates must be satisfied before issuance. Projections reflect persisted commands; a paid invoice cannot clear any professional gate.</p>
        <div class="table-scroll"><table>
          <thead><tr><th scope="col">Gate</th><th scope="col">Status</th><th scope="col">Authority</th><th scope="col">Date</th></tr></thead>
          <tbody>@for (g of w.gates; track g.name) { <tr><td>{{ g.name }}</td><td><audit-status [value]="g.status" /></td><td>{{ g.authority ?? '—' }}</td><td>{{ g.date ? g.date.slice(0, 16).replace('T', ' ') : '—' }}</td></tr> }</tbody>
        </table></div>
      </section>
      <section class="panel" aria-labelledby="rep-heading">
        <h2 id="rep-heading">Written representations</h2>
        <p>Management representations are required before the report date. Each representation is version-bound to the exact final package.</p>
        @for (r of w.representations; track r.code) { <p><strong>{{ r.code }}</strong> {{ r.title }}: {{ r.narrative }} <audit-status [value]="r.obtained ? 'OBTAINED' : 'PENDING'" /></p> }
        @empty { <p>No written representations recorded for this engagement.</p> }
      </section>
      <section class="panel" aria-labelledby="fs-heading">
        <h2 id="fs-heading">Final financial statements</h2>
        <dl class="facts"><dt>Package ID</dt><dd>{{ w.packageId ?? 'None' }}</dd><dt>Package status</dt><dd>{{ w.packageStatus }}</dd>
          <dt>Partner approval</dt><dd>{{ w.partnerApproved ? 'Approved' : 'Pending' }}</dd><dt>EQR status</dt><dd>{{ w.eqrStatus }}</dd></dl>
      </section>
      <section class="panel" aria-labelledby="actions-heading">
        <h2 id="actions-heading">Actions</h2>
        <p class="actions">
          @if (w.releaseCandidateId) { <a matButton="filled" [routerLink]="['/app/releases', w.releaseCandidateId]">View release candidate</a> }
          @else if (w.canPrepareRelease && w.packageId) { <button matButton="filled" (click)="send(base() + '/release-candidate', { packageId: w.packageId }, 'Release candidate prepared from the exact reviewed package.')" [disabled]="cmd.busy()">{{ cmd.busy() ? 'Preparing…' : 'Prepare release candidate' }}</button> }
          @if (w.packageId) { <a matButton="outlined" [routerLink]="['/app/accounting/packages', w.packageId]">Open financial package</a> }
          <a matButton [routerLink]="['/app/engagements', w.engagementId]">Back to engagement</a>
        </p>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class EngagementCompletion {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/completion` : null), decodeCompletion,
    'Sign in with an authorized internal staff identity assigned to this engagement to view its completion checklist.');
  readonly cmd = new CommandState(this.api);
  readonly kind = signal('');
  readonly kinds = ['UPLOAD', 'EDIT', 'COMMENT', 'SIGN_OFF', 'FREEZE', 'DENIED'];
  readonly reports = REPORTS;
  f = { recommendations: '', risks: '', notes: '', opinion: 'UNMODIFIED', focus: '', basis: '', resolution: '', scanReason: '', scanReviewed: false,
    statementsReviewed: false, amendment: '', lockKey: '' };

  base(): string { return `/api/ui/engagements/${this.id()}/completion`; }
  label(t: string): string { return OPINION_LABELS[t] ?? t; }
  srm(w: Completion) { return w.deliverables.filter((d) => d.kind === 'SUMMARY_REVIEW_MEMORANDUM').sort((a, b) => b.version - a.version)[0] ?? null; }
  obtained(w: Completion): number { return w.representations.filter((r) => r.obtained).length; }
  filtered(t: NonNullable<Completion['trail']>) { const k = this.kind(); return k ? t.filter((e) => e.kind === k) : t; }
  send(url: string, body: unknown, ok: string): void { void this.cmd.run(url, body, ok).finally(() => this.ws.reload()); }
  report(kind: string): void {
    void this.cmd.run<{ message: string }>(this.base() + '/reports', { kind }, '', (v) => this.cmd.message.set(v.message)).finally(() => this.ws.reload());
  }
  upload(event: Event, url: string, ok: string): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file || file.size > 512 * 1024) { this.cmd.failed.set(true); this.cmd.message.set('Choose a PNG of at most 512 KB.'); return; }
    const form = new FormData(); form.set('file', file);
    void this.cmd.run(url, form, ok).finally(() => this.ws.reload());
  }
}
