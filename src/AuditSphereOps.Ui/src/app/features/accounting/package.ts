import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodePackage = obj({ id: guid, engagementId: guid, status: text, framework: text, periodStart: text, periodEnd: text, currency: text, templateVersion: text,
  calculationHash: text, supplementaryHash: nullable(text), revision: nat, cashBeginning: nullable(dec), cashEnding: nullable(dec),
  validations: arr(obj({ code: text, passed: bool, detail: text }), 500), statementTotals: arr(obj({ section: text, amount: dec }), 500),
  cashFlow: arr(obj({ section: text, description: text, amount: dec }), 2000),
  disclosures: arr(obj({ code: text, response: text, notApplicable: bool, rationale: nullable(text) }), 2000),
  reviews: arr(obj({ stage: text, decision: text, evidenceMode: text, evidenceReference: text, packageHash: text, decidedAt: instant }), 500),
  artifact: nullable(obj({ artifactSha256Hex: text, byteCount: nat, renderedText: text })), canReview: bool, reviewStages: arr(text, 10), reviewDecisions: arr(text, 10) });
const ARTIFACTS: [string, string][] = [['financial-package-xlsx.v1', 'Download workbook'], ['financial-package-xlsx-controlled.v1', 'Download controlled workbook'],
  ['financial-package-docx.v1', 'Download Word document'], ['financial-package-pdf.v1', 'Download PDF']];

@Component({
  selector: 'audit-financial-package',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a routerLink="/app/accounting">Client accounting</a> / <a routerLink="/app/accounting/reviews">Package reviews</a> / <span>Package</span></nav>
    <audit-page-header title="Financial statement package" eyebrow="Client accounting" description="Deterministic package calculations, validation status, immutable review decisions, and artifact outputs." />
    <button matButton="outlined" (click)="pkg.reload()">Refresh package</button>
    <audit-state [loading]="pkg.loading()" [error]="pkg.error()" label="the scoped package" />
    @if (pkg.data(); as p) {
      <p class="actions" aria-label="Package navigation"><audit-status [value]="p.status" /><a [routerLink]="['/app/engagements', p.engagementId]">Engagement</a><a routerLink="/app/accounting/reviews">Package reviews</a></p>
      <p role="status">{{ passed(p) }} validations passed · {{ p.validations.length - passed(p) }} failed · {{ p.reviews.length }} review decisions · {{ p.statementTotals.length }} statement lines</p>
      <section class="panel" aria-labelledby="package-heading">
        <h2 id="package-heading"><code>{{ p.id }}</code></h2>
        <dl class="facts"><dt>Framework</dt><dd>{{ p.framework }}</dd><dt>Period</dt><dd>{{ p.periodStart }} to {{ p.periodEnd }}</dd><dt>Currency</dt><dd>{{ p.currency }}</dd>
          <dt>Template</dt><dd>{{ p.templateVersion }}</dd><dt>Calculation hash</dt><dd><code>{{ p.calculationHash }}</code></dd>
          @if (p.supplementaryHash) { <dt>Supplementary hash</dt><dd><code>{{ p.supplementaryHash }}</code></dd> }</dl>
      </section>
      @if (p.status === 'REVIEW_REQUIRED') {
        <section class="panel" role="status"><h2>Review required</h2><p>Cash-flow workings and disclosure responses are not part of this package. It cannot satisfy a complete financial-statement gate.</p></section>
      } @else {
        <section class="panel" role="status"><h2>Inputs validated</h2><p>Supplementary information reconciles deterministically. Accounting and management approval are still separate decisions.</p></section>
      }
      <section class="panel" aria-labelledby="validation-heading">
        <h2 id="validation-heading">Validation results</h2>
        <div class="table-scroll"><table><thead><tr><th scope="col">Check</th><th scope="col">Result</th><th scope="col">Detail</th></tr></thead>
          <tbody>@for (v of p.validations; track v.code) { <tr><td>{{ v.code }}</td><td>{{ v.passed ? 'PASS' : 'REVIEW' }}</td><td>{{ v.detail }}</td></tr> }</tbody></table></div>
      </section>
      @if (p.canReview) {
        <section class="panel" aria-labelledby="review-heading">
          <h2 id="review-heading">Package review decisions</h2>
          <p>Every decision is recorded against this package's exact revision, generation and calculation hash. Decisions are append-only; a changed package must be reviewed again.</p>
          @if (p.status === 'VALIDATED') {
            <form class="inline-form" (submit)="$event.preventDefault(); review(p.id)">
              <label>Stage <select name="stage" [(ngModel)]="r.stage">@for (s of p.reviewStages; track s) { <option [value]="s">{{ s }}</option> }</select></label>
              <label>Decision <select name="decision" [(ngModel)]="r.decision">@for (d of p.reviewDecisions; track d) { <option [value]="d">{{ d }}</option> }</select></label>
              <label>Evidence mode <select name="mode" [(ngModel)]="r.mode"><option value="SIGNED_IN">SIGNED_IN</option><option value="OFFLINE">OFFLINE</option></select></label>
              <label>Evidence reference <input name="ref" [(ngModel)]="r.reference" maxlength="2000" required /></label>
              <label>Comment (optional) <textarea name="comment" [(ngModel)]="r.comment" maxlength="4000" rows="3"></textarea></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">{{ cmd.busy() ? 'Recording…' : 'Record decision' }}</button>
            </form>
            <p><small>Management approval uses offline evidence on this staff surface; signed-in management approval must be completed by the authorized client-user surface.</small></p>
          } @else { <p>Review decisions are available only after the package reaches <code>VALIDATED</code>.</p> }
          <div class="table-scroll"><table><caption class="sr-only">Immutable package review decisions</caption>
            <thead><tr><th scope="col">Stage</th><th scope="col">Decision</th><th scope="col">Evidence</th><th scope="col">Package hash</th><th scope="col">Decided at</th></tr></thead>
            <tbody>@for (x of p.reviews; track $index) { <tr><td>{{ x.stage }}</td><td><audit-status [value]="x.decision" /></td><td>{{ x.evidenceMode }}: {{ x.evidenceReference }}</td><td><code>{{ x.packageHash.slice(0, 12) }}</code></td><td>{{ x.decidedAt.slice(0, 16).replace('T', ' ') }}</td></tr> }
            @empty { <tr><td colspan="5">No decisions recorded for this package version.</td></tr> }</tbody></table></div>
        </section>
      }
      <section class="panel" aria-labelledby="statement-heading">
        <h2 id="statement-heading">Mapped statement totals</h2>
        <div class="table-scroll"><table><thead><tr><th scope="col">Section</th><th scope="col" class="number">Amount</th></tr></thead>
          <tbody>@for (t of p.statementTotals; track t.section) { <tr><td>{{ t.section }}</td><td class="number">{{ t.amount | money }} {{ p.currency }}</td></tr> }</tbody></table></div>
      </section>
      @if (p.cashFlow.length) {
        <section class="panel" aria-labelledby="cash-flow-heading">
          <h2 id="cash-flow-heading">Cash-flow workings</h2>
          <p>Opening {{ p.cashBeginning | money }} {{ p.currency }}; closing {{ p.cashEnding | money }} {{ p.currency }}.</p>
          <div class="table-scroll"><table><thead><tr><th scope="col">Section</th><th scope="col">Description</th><th scope="col" class="number">Amount</th></tr></thead>
            <tbody>@for (c of p.cashFlow; track $index) { <tr><td>{{ c.section }}</td><td>{{ c.description }}</td><td class="number">{{ c.amount | money }} {{ p.currency }}</td></tr> }</tbody></table></div>
        </section>
      }
      @if (p.disclosures.length) {
        <section class="panel" aria-labelledby="disclosure-heading">
          <h2 id="disclosure-heading">Disclosure responses</h2>
          <div class="table-scroll"><table><thead><tr><th scope="col">Code</th><th scope="col">Response</th><th scope="col">Status</th></tr></thead>
            <tbody>@for (d of p.disclosures; track d.code) { <tr><td>{{ d.code }}</td><td>{{ d.notApplicable ? d.rationale : d.response }}</td><td>{{ d.notApplicable ? 'Not applicable' : 'Answered' }}</td></tr> }</tbody></table></div>
        </section>
      }
      @if (p.artifact; as a) {
        <section class="panel" aria-labelledby="artifact-heading">
          <h2 id="artifact-heading">Deterministic package artifact</h2>
          <p>This is the persisted artifact for the exact package revision and template. Downloading it does not release or deliver the package.</p>
          <dl class="facts"><dt>Artifact SHA-256</dt><dd><code>{{ a.artifactSha256Hex }}</code></dd><dt>Artifact size</dt><dd>{{ a.byteCount }} bytes</dd></dl>
          <p class="actions">
            <button matButton="filled" (click)="download(p.id, 'financial-package-text.v1')" [disabled]="downloading()">{{ downloading() ? 'Preparing…' : 'Download artifact' }}</button>
            @for (x of artifacts; track x[0]) { <button matButton="outlined" (click)="download(p.id, x[0])" [disabled]="downloading()">{{ x[1] }}</button> }
          </p>
          @if (artifactStatus()) { <p role="status" aria-live="polite">{{ artifactStatus() }}</p> }
          <details><summary>View canonical artifact text</summary><pre style="white-space: pre-wrap; overflow-wrap: anywhere"><code>{{ a.renderedText }}</code></pre></details>
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class FinancialPackage {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly pkg = this.api.resource(() => (this.id() ? `/api/ui/accounting/packages/${this.id()}` : null), decodePackage,
    'The package is not available in the current firm scope.');
  readonly cmd = new CommandState(this.api);
  readonly downloading = signal(false);
  readonly artifactStatus = signal('');
  readonly artifacts = ARTIFACTS;
  r = { stage: 'ACCOUNTING_REVIEW', decision: 'APPROVED', mode: 'SIGNED_IN', reference: '', comment: '' };
  passed(p: ReturnType<typeof decodePackage>): number { return p.validations.filter((v) => v.passed).length; }
  review(id: string): void {
    void this.cmd.run(`/api/ui/accounting/packages/${id}/reviews`, { stage: this.r.stage, decision: this.r.decision, evidenceMode: this.r.mode,
      evidenceReference: this.r.reference, comment: this.r.comment || null }, 'Decision recorded against the current package version.',
      () => (this.r = { ...this.r, reference: '', comment: '' })).finally(() => this.pkg.reload());
  }
  async download(id: string, version: string): Promise<void> {
    if (this.downloading()) return;
    this.downloading.set(true); this.artifactStatus.set('Preparing the version-bound export artifact…');
    try {
      const r = await this.api.download(`/api/ui/accounting/packages/${id}/artifact`, { artifactVersion: version });
      this.artifactStatus.set(r.ok ? 'Artifact download started. Release and delivery remain separate controls.' : r.message);
    } finally { this.downloading.set(false); }
  }
}
