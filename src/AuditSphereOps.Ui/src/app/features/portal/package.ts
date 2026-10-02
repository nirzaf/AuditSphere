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
  managementDecidedAt: nullable(instant), statementTotals: arr(obj({ statementSection: text, amount: dec }), 1000) });
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
    <section class="panel"><h2>Statement totals</h2><table><caption class="sr-only">Totals in {{ w.currency }}</caption><thead><tr><th scope="col">Section</th><th scope="col">Amount</th></tr></thead>
      <tbody>@for (t of w.statementTotals; track t.statementSection) { <tr><td>{{ t.statementSection }}</td><td>{{ t.amount | money }} {{ w.currency }}</td></tr> }</tbody></table></section>
    @if (w.managementDecision === 'PENDING' || w.managementDecision === 'CHANGES_REQUIRED') {
      <section class="panel"><h2>Record management decision</h2><form [formGroup]="form" (ngSubmit)="save()">
        <label for="management-decision">Decision</label><select id="management-decision" formControlName="decision"><option value="APPROVED">Approve</option><option value="CHANGES_REQUIRED">Changes required</option></select>
        <label for="management-evidence">Evidence reference</label><input id="management-evidence" formControlName="evidenceReference" maxlength="2000" />
        @if (form.controls.evidenceReference.touched && form.controls.evidenceReference.invalid) { <p role="alert">An evidence reference is required.</p> }
        <label for="management-comment">Comment</label><textarea id="management-comment" formControlName="comment" maxlength="4000"></textarea>
        <label><input type="checkbox" formControlName="reviewed" /> I reviewed this exact package and its SHA-256 identity.</label>
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
  constructor() { effect(() => { this.session.invalidation(); if (!this.ws.data()) this.form.reset(); }); }
  async save(): Promise<void> {
    const w = this.ws.data(); if (!w || this.form.invalid || this.busy() || this.uncertain()) return;
    const generation = this.session.invalidation(); this.busy.set(true);
    try { const r = await this.api.command(`/api/ui/portal/accounting/packages/${w.packageId}/decision`, { ...this.form.getRawValue(), expectedHash: w.packageHash });
      if (generation !== this.session.invalidation()) return;
      this.failed.set(!r.ok); this.message.set(r.ok ? 'Management decision recorded for this exact package.' : r.message);
      if (!r.ok && r.unknown) this.uncertain.set(true);
      if (r.ok || (!r.ok && r.unknown)) this.ws.reload();
    } finally { this.busy.set(false); }
  }
}
