import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodePopulation = obj({ id: guid, engagementId: guid, purpose: text, assertion: text, sourceReceiptReference: text, extractionParameters: text, rowCount: nat,
  monetaryControlTotal: dec, currency: text, exclusions: nullable(text), status: text, createdAt: instant,
  selections: arr(obj({ method: text, status: text, rationale: text, itemCount: nat, testCount: nat, reviewedTestCount: nat }), 1000),
  evidence: arr(obj({ purpose: text, assertion: text, receiptToken: text }), 5000) });
export const decodeFinding = obj({ id: guid, engagementId: guid, findingType: text, impactDescription: text, monetaryAmount: nullable(dec), corrected: bool,
  managementResponse: nullable(text), status: text, createdAt: instant, professionalWorkBlocked: bool,
  letterDesignatedAt: nullable(instant), letterRecommendation: nullable(text) });
export const decodeReviewPoint = obj({ id: guid, engagementId: guid, targetKind: text, targetId: guid, targetRevision: int, raisedByUserId: guid, raisedAt: instant,
  significant: bool, cleared: bool, comment: text, status: text });
const gate = obj({ state: text, detail: text });
export const decodeRelease = obj({ id: guid, status: text, targetKind: text, targetRevision: int, revision: int, manifestDigest: text, createdAt: instant,
  checkpoint: gate, attestation: gate, lineage: gate, preflightReady: bool });
export const decodeArchive = obj({ id: guid, engagementId: guid, status: text, createdAt: instant, profileId: text, profileVersion: int, manifestVersion: nullable(int),
  manifestStatus: nullable(text), manifestDigest: nullable(text), completenessStatus: nullable(text), completenessException: nullable(text),
  entries: arr(obj({ ordinal: int, entryKind: text, relativeName: text, contentHash: text, byteCount: int }), 100), totalEntryCount: nat, nextOrdinal: nullable(int), activeHoldCount: nat,
  observedProtection: nullable(text), desiredLabel: nullable(text), observedLabel: nullable(text), actionState: nullable(text), externalReference: nullable(text) });
const sum = (rows: { reviewedTestCount: number }[]) => rows.reduce((n, r) => n + r.reviewedTestCount, 0);

@Component({
  selector: 'audit-population',
  imports: [RouterLink, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / @if (rec.data(); as p) { <a [routerLink]="['/app/engagements', p.engagementId, 'audit-fieldwork']">Fieldwork</a> / } <span>Population</span></nav>
    <audit-page-header title="Population record" eyebrow="Audit sampling" description="Scoped audit population rows, sampling evidence, and objective-level progression." />
    <audit-state [loading]="rec.loading()" [error]="rec.error()" label="population" />
    @if (rec.data(); as p) {
      <p class="actions"><audit-status [value]="p.status" /><a [routerLink]="['/app/engagements', p.engagementId, 'audit-fieldwork']">Fieldwork control center</a><a [routerLink]="['/app/engagements', p.engagementId, 'audit-plan']">Audit plan</a></p>
      <p role="status">{{ p.rowCount }} population rows · {{ p.selections.length }} selection sets · {{ reviewed(p.selections) }} reviewed item tests</p>
      <section class="panel"><h2>Population details (§20.1)</h2>
        <dl class="facts"><dt>Purpose</dt><dd>{{ p.purpose }}</dd><dt>Assertion</dt><dd>{{ p.assertion }}</dd><dt>Source receipt reference</dt><dd>{{ p.sourceReceiptReference }}</dd>
          <dt>Extraction parameters</dt><dd>{{ p.extractionParameters }}</dd><dt>Row count</dt><dd>{{ p.rowCount }}</dd><dt>Monetary control total</dt><dd>{{ p.currency }} {{ p.monetaryControlTotal | money }}</dd>
          <dt>Exclusions</dt><dd>{{ p.exclusions ?? 'None recorded' }}</dd><dt>Status</dt><dd>{{ p.status }}</dd><dt>Extracted</dt><dd>{{ p.createdAt.slice(0, 16).replace('T', ' ') }} UTC</dd></dl></section>
      <section class="panel"><h2>Selections and test results</h2>
        <p><small>Counts repeat recorded selections and item-test reviews for this population version. They do not establish sampling sufficiency or an audit conclusion.</small></p>
        <div class="table-scroll"><table><thead><tr><th scope="col">Method</th><th scope="col">Status</th><th scope="col">Items</th><th scope="col">Tests</th><th scope="col">Reviewed tests</th><th scope="col">Rationale</th></tr></thead>
          <tbody>@for (s of p.selections; track $index) { <tr><td>{{ s.method }}</td><td><audit-status [value]="s.status" /></td><td>{{ s.itemCount }}</td><td>{{ s.testCount }}</td><td>{{ s.reviewedTestCount }}</td><td>{{ s.rationale }}</td></tr> }
          @empty { <tr><td colspan="6">No selection has been recorded for this population version.</td></tr> }</tbody></table></div></section>
      <section class="panel"><h2>Linked evidence</h2>
        <ul>@for (e of p.evidence; track $index) { <li>{{ e.purpose }} ({{ e.assertion }}) — receipt {{ e.receiptToken }}</li> } @empty { <li>No source receipts are linked to this engagement as evidence.</li> }</ul></section>
    }
  `,
})
export class PopulationRecord {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly rec = this.api.resource(() => (this.id() ? `/api/ui/audit/populations/${this.id()}` : null), decodePopulation, 'The requested population is unavailable in your current scope.');
  readonly reviewed = sum;
}

@Component({
  selector: 'audit-finding',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <span>Finding</span></nav>
    <audit-page-header title="Finding" eyebrow="Audit findings" description="Finding detail, management response and in-period correction status." />
    <button matButton="outlined" (click)="rec.reload()" [disabled]="cmd.busy()">Refresh finding</button>
    <audit-state [loading]="rec.loading()" [error]="rec.error()" label="finding" />
    @if (rec.data(); as f) {
      <p class="actions"><audit-status [value]="f.status" /><a [routerLink]="['/app/engagements', f.engagementId, 'audit-plan']">Audit plan</a><a [routerLink]="['/app/engagements', f.engagementId]">Engagement</a></p>
      <p role="status">Amount recorded {{ f.monetaryAmount === null ? '—' : (f.monetaryAmount | money) }} · corrected in period {{ f.corrected ? 'yes' : 'no' }} · management response {{ f.managementResponse === null ? 'not recorded' : 'recorded' }}</p>
      <section class="panel"><h2>Finding details (§23)</h2>
        <dl class="facts"><dt>Type</dt><dd>{{ f.findingType }}</dd><dt>Impact description</dt><dd>{{ f.impactDescription }}</dd>
          <dt>Monetary amount</dt><dd>{{ f.monetaryAmount === null ? 'Not quantified' : (f.monetaryAmount | money) }}</dd><dt>Corrected in the period under audit</dt><dd>{{ f.corrected ? 'Yes' : 'No' }}</dd>
          <dt>Status</dt><dd>{{ f.status }}</dd><dt>Raised</dt><dd>{{ f.createdAt.slice(0, 16).replace('T', ' ') }} UTC</dd></dl></section>
      <section class="panel"><h2>Management response & action</h2>
        @if (f.managementResponse) { <blockquote>{{ f.managementResponse }}</blockquote> } @else { <p>No management response has been recorded.</p> }</section>
      <section class="panel"><h2>Record management response</h2>
        <form (submit)="$event.preventDefault(); respond(f.id)">
          <label>Management response <textarea name="resp" [(ngModel)]="response" rows="4" required maxlength="8000"></textarea></label>
          <label><span><input type="checkbox" name="corrected" [(ngModel)]="corrected" /> Management corrected the matter within the period under audit (optional)</span></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy() || f.professionalWorkBlocked">Record response</button>
        </form>
        <p><small>Correction inside the audited period and future-period remediation are tracked separately; filing a response here does not state an audit conclusion.</small></p></section>
      <section class="panel"><h2>Management letter designation</h2>
        <p>Designation status: <audit-status [value]="f.letterDesignatedAt ? 'DESIGNATED' : 'INTERNAL_ONLY'" /></p>
        @if (f.letterRecommendation) { <p>Recommendation held for the client-facing letter:</p><blockquote>{{ f.letterRecommendation }}</blockquote> }
        <form (submit)="$event.preventDefault(); designate(f.id, designatedFlag)">
          <label><span><input type="checkbox" name="designated" [(ngModel)]="designatedFlag" /> Designate this matter for the client-facing management letter</span></label>
          <label>Recommendation rendered in the letter <textarea name="rec" [(ngModel)]="designation" rows="3" [required]="designatedFlag" maxlength="2000"></textarea></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy() || f.professionalWorkBlocked">Record designation</button>
        </form>
        <p><small>Only designated matters with a complete recommendation are rendered in the management letter; findings left internal-only never appear in client reporting. A Manager, Partner or Administrator records the designation, and changing a designated matter invalidates the generated letter.</small></p></section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class FindingRecord {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly rec = this.api.resource(() => (this.id() ? `/api/ui/findings/${this.id()}` : null), decodeFinding, 'The requested finding is unavailable in your current scope.');
  readonly cmd = new CommandState(this.api);
  response = '';
  corrected = false;
  designatedFlag = false;
  designation = '';
  constructor() {
    effect(() => {
      const f = this.rec.data();
      if (f && !this.response && f.managementResponse !== null) {
        this.response = f.managementResponse;
        this.corrected = f.corrected;
      }
      if (f && !this.designation && f.letterRecommendation !== null) {
        this.designation = f.letterRecommendation;
        this.designatedFlag = f.letterDesignatedAt !== null;
      }
    });
  }
  respond(id: string): void {
    void this.cmd.run(`/api/ui/findings/${id}/response`, { managementResponse: this.response, corrected: this.corrected }, 'The management response was recorded.').finally(() => this.rec.reload());
  }
  designate(id: string, designated: boolean): void {
    void this.cmd.run(`/api/ui/findings/${id}/management-letter-designation`, { designated, recommendation: designated ? this.designation : null },
      designated ? 'The matter was designated for the management letter.' : 'The matter was returned to internal-only.').finally(() => this.rec.reload());
  }
}

@Component({
  selector: 'audit-review-point',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <span>Review point</span></nav>
    <audit-page-header title="Review point" eyebrow="Engagement review" description="Review point detail, comment thread, and independent clearing decision." />
    <audit-state [loading]="rec.loading()" [error]="rec.error()" label="review point" />
    @if (rec.data(); as r) {
      <p class="actions"><audit-status [value]="r.status" /><a [routerLink]="['/app/engagements', r.engagementId]">Engagement</a><a [routerLink]="['/app/engagements', r.engagementId, 'completion']">Completion checklist</a></p>
      @if (r.significant && !r.cleared) { <p role="status">This significant review point remains open and blocks the engagement completion gate.</p> }
      <section class="panel" aria-labelledby="context-heading"><h2 id="context-heading">Review context</h2>
        <dl class="facts"><dt>Target kind</dt><dd>{{ r.targetKind }}</dd><dt>Target ID</dt><dd>{{ r.targetId }}</dd><dt>Target revision</dt><dd>{{ r.targetRevision }}</dd>
          <dt>Raised by</dt><dd>User {{ r.raisedByUserId.slice(0, 8) }}</dd><dt>Raised at</dt><dd>{{ r.raisedAt.slice(0, 16).replace('T', ' ') }} UTC</dd>
          <dt>Blocks gate</dt><dd>{{ r.significant ? 'Yes (Significant)' : 'No' }}</dd><dt>Status</dt><dd>{{ r.status }}</dd></dl></section>
      <section class="panel" aria-labelledby="comment-heading"><h2 id="comment-heading">Review comment</h2><p>{{ r.comment }}</p></section>
      <section class="panel" aria-labelledby="actions-heading"><h2 id="actions-heading">Review disposition</h2>
        <p class="actions">
          @if (!r.cleared) { <button matButton="filled" (click)="set(r.id, true)" [disabled]="cmd.busy()">Clear review point</button> }
          @else { <button matButton="outlined" (click)="set(r.id, false)" [disabled]="cmd.busy()">Reopen review point</button> }
          <a matButton [routerLink]="['/app/engagements', r.engagementId]">Back to engagement</a></p></section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class ReviewPointRecord {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly rec = this.api.resource(() => (this.id() ? `/api/ui/reviews/${this.id()}` : null), decodeReviewPoint, 'The requested review point was not found in the current firm scope.');
  readonly cmd = new CommandState(this.api);
  set(id: string, cleared: boolean): void { void this.cmd.run(`/api/ui/reviews/${id}/disposition`, { cleared }, 'Disposition updated successfully.').finally(() => this.rec.reload()); }
}

@Component({
  selector: 'audit-release',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <span>Release candidate</span></nav>
    <audit-page-header title="Release candidate" eyebrow="Release control" description="Exact package release gates and the guarded issue command." />
    <audit-state [loading]="rec.loading()" [error]="rec.error()" label="release candidate" />
    @if (rec.data(); as c) {
      <p class="actions" aria-label="Release candidate status"><audit-status [value]="c.status" /></p>
      <section class="panel" aria-labelledby="candidate-heading"><h2 id="candidate-heading"><code>{{ c.id }}</code></h2>
        <dl class="facts"><dt>Target</dt><dd>{{ c.targetKind }} / revision {{ c.targetRevision }}</dd><dt>Candidate revision</dt><dd>{{ c.revision }}</dd>
          <dt>Manifest digest</dt><dd><code>{{ c.manifestDigest }}</code></dd><dt>Created</dt><dd>{{ c.createdAt.slice(0, 16).replace('T', ' ') }}</dd></dl></section>
      @if (c.status === 'ISSUED') {
        <section class="panel" role="status"><h2>Issued</h2><p>This candidate already has an immutable release event. Repeating the same authorized request is idempotent.</p></section>
      } @else {
        @if (!c.preflightReady) { <p role="status">Required preflight evidence is incomplete. The issue action stays unavailable until the configured checks pass; the server rechecks every gate at issuance.</p> }
        <section class="panel" aria-labelledby="gates-heading"><h2 id="gates-heading">Release evidence gates (§24.4, §25.7)</h2>
          <p>Every gate requires stored, read-back evidence. Caller assertions are not accepted.</p>
          <dl class="facts"><dt>External checkpoint</dt><dd [class.error-text]="c.checkpoint.state !== 'VERIFIED'">{{ c.checkpoint.state.toLowerCase() }} — {{ c.checkpoint.detail }}</dd>
            <dt>Protection attestation</dt><dd [class.error-text]="c.attestation.state !== 'VERIFIED' && c.attestation.state !== 'ABSENT'">{{ c.attestation.state.toLowerCase() }} — {{ c.attestation.detail }}</dd>
            <dt>Signature lineage</dt><dd [class.error-text]="c.lineage.state !== 'VERIFIED' && c.lineage.state !== 'ABSENT'">{{ c.lineage.state.toLowerCase() }} — {{ c.lineage.detail }}</dd></dl>
          <p><small>No external records-provider acceptance is claimed by this workbench.</small></p></section>
        <section class="panel" aria-labelledby="issue-heading"><h2 id="issue-heading">Issue exact package</h2>
          <p>This command rechecks current scope, approval applicability, revisions, generations, holds, manifest and recovery mode. It does not sign files or call a provider.</p>
          <label>Authorized release key <input type="password" name="releaseKey" [(ngModel)]="key" maxlength="200" autocomplete="off" required /></label>
          <button matButton="filled" (click)="issue(c.id, c.revision, c.manifestDigest)" [disabled]="cmd.busy() || !c.preflightReady || !key.trim()">{{ cmd.busy() ? 'Issuing…' : 'Issue release' }}</button>
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class ReleaseCandidate {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly rec = this.api.resource(() => (this.id() ? `/api/ui/releases/${this.id()}` : null), decodeRelease, 'The candidate is not available in the current firm scope.');
  readonly cmd = new CommandState(this.api);
  private readonly reconcileIssued = effect(() => {
    const candidate = this.rec.data();
    if (candidate?.status === 'ISSUED' && this.cmd.uncertain()) {
      this.cmd.uncertain.set(false);
      this.cmd.failed.set(false);
      this.cmd.message.set('Release confirmed: the current candidate is issued.');
    }
  });
  key = '';
  issue(id: string, revision: number, digest: string): void {
    const key = this.key; this.key = '';
    void this.cmd.run<string>(`/api/ui/releases/${id}/issue`, { expectedRevision: revision, manifestDigest: digest, releaseKey: key }, '',
      (eventId) => this.cmd.message.set(`Release event: ${eventId}`)).finally(() => this.rec.reload());
  }
}

@Component({
  selector: 'audit-archive',
  imports: [RouterLink, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <span>Archive</span></nav>
    <audit-page-header title="Records archive" eyebrow="Records evidence" description="Immutable manifest entries, hashes, holds and protection observations for the authorized archive." />
    <audit-state [loading]="rec.loading()" [error]="rec.error()" label="archive" />
    @if (rec.data(); as a) {
      <p class="actions"><audit-status [value]="a.status" /><a [routerLink]="['/app/engagements', a.engagementId]">Engagement</a></p>
      <p role="status">Manifest version {{ a.manifestVersion ?? '—' }} · {{ a.totalEntryCount }} manifest entries · {{ a.activeHoldCount }} active local holds</p>
      <section class="panel" aria-labelledby="archive-summary"><h2 id="archive-summary">Archive manifest</h2>
        <dl class="facts"><dt>Archive ID</dt><dd><code>{{ a.id }}</code></dd><dt>Scope</dt><dd><code>{{ a.engagementId }}</code></dd><dt>Created</dt><dd>{{ a.createdAt }}</dd>
          <dt>Records profile</dt><dd>{{ a.profileId }} v{{ a.profileVersion }}</dd><dt>Protection</dt><dd>{{ a.observedProtection ?? 'Not observed' }}</dd>
          <dt>Manifest hash</dt><dd><code>{{ a.manifestDigest ?? 'Not built' }}</code></dd><dt>Active holds</dt><dd>{{ a.activeHoldCount }}</dd></dl>
        @if (a.completenessStatus === 'INCOMPLETE') { <p role="alert">Manifest incomplete: {{ a.completenessException }}</p> }
        @if (a.actionState === 'REQUESTED') { <p>Records action requested; this is not evidence of Purview protection.</p> }</section>
      @if (a.manifestStatus === 'REVIEWED' && a.completenessStatus === 'COMPLETE') {
        <section class="panel" aria-labelledby="archive-export"><h2 id="archive-export">Read-only archive export</h2>
          <p>The export contains the reviewed structured snapshot and full manifest index. External document bytes remain in their source repository and are represented by references and SHA-256 hashes.</p>
          <a mat-stroked-button [href]="'/api/ui/records/archives/' + a.id + '/export'">Download read-only evidence export</a>
          <p><small>Provider protection remains separate; an export does not claim external immutability.</small></p>
        </section>
      }
      <section class="panel" aria-labelledby="archive-contents"><h2 id="archive-contents">Archive contents</h2>
        <p role="status">Showing {{ visibleEntries(a).length }} of {{ a.totalEntryCount }} entries.</p>
        <div class="table-scroll"><table><thead><tr><th scope="col">Index</th><th scope="col">Kind</th><th scope="col">Source</th><th scope="col">Hash</th><th scope="col" class="number">Bytes</th></tr></thead>
          <tbody>@for (e of visibleEntries(a); track e.ordinal) { <tr><td>{{ e.ordinal }}</td><td>{{ e.entryKind }}</td><td>{{ e.relativeName }}</td><td><code>{{ e.contentHash }}</code></td><td class="number">{{ e.byteCount }}</td></tr> }
          @empty { <tr><td colspan="5">No persisted manifest entries.</td></tr> }</tbody></table></div></section>
        @if (nextOrdinal() !== null) { <button mat-stroked-button type="button" [disabled]="loadingEntries()" (click)="loadMore(a)">{{ loadingEntries() ? 'Loading entries…' : 'Load next 100 entries' }}</button> }
        @if (entriesError()) { <p role="alert">{{ entriesError() }}</p> }
      <section class="panel" aria-labelledby="records-evidence"><h2 id="records-evidence">Records evidence</h2>
        <dl class="facts"><dt>Desired label</dt><dd>{{ a.desiredLabel ?? 'Not requested' }}</dd><dt>Observed label</dt><dd>{{ a.observedLabel ?? 'Not observed' }}</dd>
          <dt>Action state</dt><dd>{{ a.actionState ?? 'Not requested' }}</dd><dt>External reference</dt><dd>{{ a.externalReference ?? '—' }}</dd>
          <dt>Hold evidence</dt><dd>{{ a.activeHoldCount > 0 ? 'Observed active hold' : 'No active local hold' }}</dd></dl>
        <p><small>A local action or legal hold records the requested/observed evidence only; it does not assert Microsoft Purview behavior without an external observation.</small></p></section>
    }
  `,
})
export class ArchiveRecord {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly rec = this.api.resource(() => (this.id() ? `/api/ui/records/archives/${this.id()}` : null), decodeArchive, 'The archive is not available in the current scope.');
  readonly additionalEntries = signal<ReturnType<typeof decodeArchive>['entries']>([]);
  readonly nextOrdinal = signal<number | null>(null);
  readonly loadingEntries = signal(false);
  readonly entriesError = signal('');

  constructor() {
    effect(() => {
      this.id();
      const archive = this.rec.data();
      this.additionalEntries.set([]);
      this.nextOrdinal.set(archive?.nextOrdinal ?? null);
      this.entriesError.set('');
    });
  }

  visibleEntries(archive: ReturnType<typeof decodeArchive>) {
    return [...archive.entries, ...this.additionalEntries()];
  }

  async loadMore(archive: ReturnType<typeof decodeArchive>) {
    const cursor = this.nextOrdinal();
    if (this.loadingEntries() || cursor === null) return;
    const id = archive.id;
    this.loadingEntries.set(true);
    this.entriesError.set('');
    try {
      const next = await this.api.get(`/api/ui/records/archives/${id}?afterOrdinal=${cursor}`, decodeArchive);
      if (this.id() !== id || this.rec.data()?.id !== id || next.id !== id || next.manifestVersion !== archive.manifestVersion) return;
      const seen = new Set(this.visibleEntries(archive).map(entry => entry.ordinal));
      this.additionalEntries.update(current => [...current, ...next.entries.filter(entry => !seen.has(entry.ordinal))]);
      this.nextOrdinal.set(next.nextOrdinal);
    } catch (error) {
      this.entriesError.set(error instanceof Error ? error.message : 'Entries could not be loaded. Retry shortly.');
    } finally {
      this.loadingEntries.set(false);
    }
  }
}
