import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { arr, bool, date, dec, guid, instant, nullable, obj, sha256, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
export const clientPackage = obj({ packageId: guid, framework: text, periodStart: date, periodEnd: date, currency: text,
  templateVersion: text, packageHash: sha256, hasCashFlow: bool, hasDisclosures: bool, managementDecision: text,
  managementDecidedAt: nullable(instant), statementTotals: arr(obj({ statementSection: text, amount: dec }), 1000),
  // The exact sealed composition awaiting management approval, when one exists. The decision names this identity and digest.
  composition: nullable(obj({ presentationRevisionId: guid, compositionId: guid, compositionManifestDigest: sha256,
    pdfSha256Hex: sha256, wordSha256Hex: sha256, excelSha256Hex: sha256, selectedAt: instant })),
  latestDecisionId: nullable(guid) });
export type ClientPackageView = ReturnType<typeof clientPackage>;
export interface ClientDecisionForm { decision: string; evidenceReference: string; comment: string; reviewed: boolean }
/** The original intent of a management decision, kept until the server answers so a lost reply retries the same decision. */
export interface PendingClientDecision { requestId: string; key: string }

/** A retry keeps its identity only while the decision, its content and the reviewed report are unchanged. */
export function clientDecisionIntent(pending: PendingClientDecision | null, view: ClientPackageView, form: ClientDecisionForm,
  newRequestId: string): PendingClientDecision {
  const key = JSON.stringify([view.packageHash, view.composition?.compositionId ?? null, view.composition?.compositionManifestDigest ?? null,
    view.latestDecisionId, form.decision, form.evidenceReference, form.comment]);
  return pending && pending.key === key ? pending : { requestId: newRequestId, key };
}

/** The decision request. It names the exact composition the manager reviewed, or none when only the package awaits acknowledgement. */
export function clientDecisionRequest(view: ClientPackageView, form: ClientDecisionForm, requestId: string): Record<string, unknown> {
  return {
    decision: form.decision, evidenceReference: form.evidenceReference, comment: form.comment, reviewed: form.reviewed,
    expectedHash: view.packageHash,
    presentationRevisionId: view.composition?.presentationRevisionId ?? null,
    presentationCompositionId: view.composition?.compositionId ?? null,
    compositionManifestDigest: view.composition?.compositionManifestDigest ?? null,
    requestId, expectedLatestDecisionId: view.latestDecisionId,
  };
}
@Component({ selector: 'audit-client-package', imports: [ReactiveFormsModule, RouterLink, MatButtonModule, ...SHARED], template: `
  <nav aria-label="Package location"><a routerLink="/portal">Client portal</a> / <span>Financial package</span></nav>
  <audit-page-header title="Financial package" description="Management acknowledgement of a validated reporting package. This is not an audit opinion or proof of ledger posting." />
  <button matButton (click)="ws.reload()" [disabled]="busy()">Refresh package</button>
  <audit-state [loading]="ws.loading()" [error]="ws.error()" label="your shared package" />
  <audit-command-message [message]="message()" [failed]="failed()" />
  @if (ws.data(); as w) {
    <section class="panel"><h2>Management review</h2><audit-status [value]="w.managementDecision" />
      <dl><dt>Framework</dt><dd>{{ w.framework }}</dd><dt>Period</dt><dd>{{ w.periodStart }} to {{ w.periodEnd }}</dd>
        <dt>Currency</dt><dd>{{ w.currency }}</dd><dt>Template</dt><dd>{{ w.templateVersion }}</dd>
        <dt>Package SHA-256</dt><dd class="fingerprint">{{ w.packageHash }}</dd></dl>
      <p>{{ w.hasCashFlow && w.hasDisclosures ? 'Cash flow and disclosures supplied' : 'Supporting information incomplete' }}</p>
    </section>
    @if (w.composition; as c) {
      <section class="panel" aria-labelledby="composition-heading"><h2 id="composition-heading">Report awaiting your approval</h2>
        <p>Your decision covers exactly this sealed report: the PDF, Word and Excel files selected together by your firm. If the firm changes any statement, note or layout, a new report is sealed and needs your decision again.</p>
        <dl><dt>Report identity</dt><dd class="fingerprint">{{ c.compositionId }}</dd>
          <dt>Report manifest SHA-256</dt><dd class="fingerprint">{{ c.compositionManifestDigest }}</dd>
          <dt>PDF SHA-256</dt><dd class="fingerprint">{{ c.pdfSha256Hex }}</dd>
          <dt>Sealed</dt><dd><time [attr.datetime]="c.selectedAt">{{ c.selectedAt }}</time></dd></dl>
        <button matButton="outlined" type="button" (click)="downloadPdf(w)" [disabled]="downloading()">{{ downloading() ? 'Preparing…' : 'Download the report PDF' }}</button>
        @if (downloadStatus()) { <p role="status" aria-live="polite">{{ downloadStatus() }}</p> }
      </section>
    } @else {
      <section class="panel"><h2>Report awaiting your approval</h2><p>No sealed report has been issued for your review yet. A decision here acknowledges the package figures only; it is not approval of the final report.</p></section>
    }
    <section class="panel"><h2>Statement totals</h2><table><caption class="sr-only">Totals in {{ w.currency }}</caption><thead><tr><th scope="col">Section</th><th scope="col">Amount</th></tr></thead>
      <tbody>@for (t of w.statementTotals; track t.statementSection) { <tr><td>{{ t.statementSection }}</td><td>{{ t.amount | money }} {{ w.currency }}</td></tr> }</tbody></table></section>
    @if (w.managementDecision === 'PENDING' || w.managementDecision === 'CHANGES_REQUIRED') {
      <section class="panel"><h2>Record management decision</h2><form [formGroup]="form" (ngSubmit)="save()">
        <label for="management-decision">Decision</label><select id="management-decision" formControlName="decision"><option value="APPROVED">Approve</option><option value="CHANGES_REQUIRED">Changes required</option></select>
        <label for="management-evidence">Evidence reference</label><input id="management-evidence" formControlName="evidenceReference" maxlength="2000" />
        @if (form.controls.evidenceReference.touched && form.controls.evidenceReference.invalid) { <p role="alert">An evidence reference is required.</p> }
        <label for="management-comment">Comment</label><textarea id="management-comment" formControlName="comment" maxlength="4000"></textarea>
        <label><input type="checkbox" formControlName="reviewed" /> {{ w.composition ? 'I reviewed this exact report and its SHA-256 identities.' : 'I reviewed this exact package and its SHA-256 identity.' }}</label>
        <button matButton="filled" [disabled]="busy() || uncertain() || form.invalid">Record decision</button>
      </form></section>
    }
  }
`, styles: `.fingerprint { overflow-wrap: anywhere; } textarea { width: 100%; min-height: 6rem; }` })
export class ClientFinancialPackage {
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => this.id() ? `/api/ui/portal/accounting/packages/${this.id()}` : null, clientPackage, 'This package is unavailable in your current client scope.');
  readonly form = inject(FormBuilder).nonNullable.group({ decision: ['APPROVED', Validators.required], evidenceReference: ['', [Validators.required, Validators.maxLength(2000)]], comment: ['', Validators.maxLength(4000)], reviewed: [false, Validators.requiredTrue] });
  readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false);
  readonly downloading = signal(false); readonly downloadStatus = signal('');
  private pending: PendingClientDecision | null = null;
  constructor() { effect(() => { this.session.invalidation(); this.id(); this.pending = null; this.downloadStatus.set(''); if (!this.ws.data()) this.form.reset(); }); }
  async save(): Promise<void> {
    const w = this.ws.data(); if (!w || this.form.invalid || this.busy() || this.uncertain()) return;
    const form = this.form.getRawValue();
    // A lost reply keeps the same identity, so retrying after a reload recovers the stored decision instead of recording a second one.
    const intent = clientDecisionIntent(this.pending, w, form, crypto.randomUUID());
    this.pending = intent;
    const generation = this.session.invalidation(); this.busy.set(true);
    try { const r = await this.api.command(`/api/ui/portal/accounting/packages/${w.packageId}/decision`, clientDecisionRequest(w, form, intent.requestId));
      if (generation !== this.session.invalidation()) return;
      this.failed.set(!r.ok); this.message.set(r.ok ? (w.composition ? 'Management decision recorded for this exact report.' : 'Management decision recorded for this exact package.') : r.message);
      if (!r.ok && r.unknown) this.uncertain.set(true);
      if (r.ok || !r.unknown) this.pending = null;
      if (r.ok || (!r.ok && r.unknown)) this.ws.reload();
    } finally { this.busy.set(false); }
  }
  async downloadPdf(w: ClientPackageView): Promise<void> {
    const c = w.composition; if (!c || this.downloading()) return;
    this.downloading.set(true); this.downloadStatus.set('Preparing the sealed report PDF…');
    try {
      // The file is saved only when its fingerprint is the one shown above, so a changed report is never saved as the reviewed one.
      const r = await this.api.download(`/api/ui/portal/accounting/packages/${w.packageId}/compositions/${c.compositionId}/pdf`, {},
        (m) => m.headers['x-statement-composition-pdf-sha256'] === c.pdfSha256Hex);
      this.downloadStatus.set(r.ok ? 'Report PDF download started. Downloading does not record a decision.' : r.message);
    } finally { this.downloading.set(false); }
  }
}
