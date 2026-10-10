import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, dec, guid, instant, nat, nullable, obj, oneOf, str, text } from '../../core/decode';
import { exactDecimal } from '../../core/contracts';
import { SHARED } from '../../core/ui';

const account = obj({ id: guid, code: text, name: text });
export const decodeEndOfService = obj({
  canRecordTreatment: bool, canConfirmTreatment: bool, canPrepareAccrual: bool, accrualBlockedReason: nullable(text),
  treatment: nullable(obj({
    id: guid, version: nat, status: oneOf('RECORDED', 'CONFIRMED'), measurementTreatment: str(4000),
    accountantName: text, accountantCredential: text, provisionAccount: text, expenseAccount: text,
    recordedBy: text, recordedAt: instant, confirmedBy: nullable(text), confirmedAt: nullable(instant),
    confirmationNote: nullable(text),
  })),
  treatmentVersion: nat, currency: nullable(text), postedProvisionBalance: dec,
  liabilityAccounts: arr(account, 500), expenseAccounts: arr(account, 500),
  openPeriods: arr(obj({ id: guid, periodCode: text }), 60),
  accruals: arr(obj({
    id: guid, journalId: guid, journalNumber: text, periodCode: text, amount: dec, currency: text, method: text,
    inputs: text, calculationDate: date, reason: text, journalStatus: text, preparedBy: text, treatmentVersion: nat,
    createdAt: instant,
  }), 100),
  maxEvidenceBytes: nat,
});

/** Two-decimal positive amount, kept as text so the entered figure is never passed through floating point. */
export const validAccrualAmount = (value: string): boolean =>
  /^\d{1,13}(\.\d{1,2})?$/.test(value) && exactDecimal(value) && !/^0+(\.0+)?$/.test(value);

@Component({
  selector: 'audit-end-of-service',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/finance">← Back to firm finance</a>
    <audit-page-header title="End-of-service provision" eyebrow="Firm books"
      description="Monthly accruals entered by a person, with the calculation basis. AuditSphere stores and posts what is entered; it does not calculate or assess the obligation." />
    <audit-state [loading]="workspace.loading()" [error]="workspace.error()" label="end-of-service provision" />
    @if (message()) { <p [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p> }
    @if (workspace.data(); as w) {
      <section class="panel" aria-labelledby="treatment-heading">
        <h2 id="treatment-heading">Accounting treatment</h2>
        @if (w.treatment; as t) {
          <p><audit-status [value]="t.status" /> Version {{ t.version }}</p>
          <dl>
            <dt>Measurement basis and standard</dt><dd class="pre">{{ t.measurementTreatment }}</dd>
            <dt>Named by</dt><dd>{{ t.accountantName }} · {{ t.accountantCredential }}</dd>
            <dt>Provision account</dt><dd>{{ t.provisionAccount }}</dd>
            <dt>Expense account</dt><dd>{{ t.expenseAccount }}</dd>
            <dt>Recorded</dt><dd>{{ t.recordedBy }} · {{ t.recordedAt.slice(0, 16).replace('T', ' ') }} UTC</dd>
            <dt>Partner confirmation</dt>
            <dd>
              @if (t.confirmedBy && t.confirmedAt) {
                {{ t.confirmedBy }} · {{ t.confirmedAt.slice(0, 16).replace('T', ' ') }} UTC — {{ t.confirmationNote }}
              } @else { Not confirmed. No accrual can be prepared or posted. }
            </dd>
          </dl>
          @if (w.canConfirmTreatment) {
            <form (ngSubmit)="confirm(t.id)">
              <label for="confirmation-note">Confirmation note</label>
              <textarea id="confirmation-note" name="confirmationNote" maxlength="1000" [(ngModel)]="confirmationNote" [disabled]="busy()"></textarea>
              <label class="check"><input type="checkbox" name="confirmReviewed" [(ngModel)]="confirmReviewed" [disabled]="busy()" />
                I am confirming, as a Partner of the firm, the treatment named by the accountant above.</label>
              <button matButton="filled" type="submit" [disabled]="busy() || !confirmReviewed || confirmationNote.trim().length < 5">Confirm treatment</button>
            </form>
          }
        } @else {
          <p>No treatment is recorded. Accruals stay blocked until finance records the treatment named by a qualified accountant and a Partner confirms it.</p>
        }
        @if (w.canRecordTreatment) {
          <form (ngSubmit)="record(w.treatmentVersion)">
            <h3>{{ w.treatment ? 'Record a new treatment version' : 'Record the treatment' }}</h3>
            <label for="treatment-text">Measurement basis and the standard it follows (as named by the accountant)</label>
            <textarea id="treatment-text" name="treatmentText" maxlength="4000" [(ngModel)]="treatmentText" (ngModelChange)="treatmentReviewed = false" [disabled]="busy()"></textarea>
            <label for="accountant-name">Qualified accountant</label>
            <input id="accountant-name" name="accountantName" maxlength="200" [(ngModel)]="accountantName" (ngModelChange)="treatmentReviewed = false" [disabled]="busy()" />
            <label for="accountant-credential">Qualification or membership reference</label>
            <input id="accountant-credential" name="accountantCredential" maxlength="300" [(ngModel)]="accountantCredential" (ngModelChange)="treatmentReviewed = false" [disabled]="busy()" />
            <label for="provision-account">Provision account (liability)</label>
            <select id="provision-account" name="provisionAccount" [(ngModel)]="provisionAccountId" (ngModelChange)="treatmentReviewed = false" [disabled]="busy()">
              <option value="">Select an account</option>
              @for (a of w.liabilityAccounts; track a.id) { <option [value]="a.id">{{ a.code }} · {{ a.name }}</option> }
            </select>
            <label for="expense-account">Expense account</label>
            <select id="expense-account" name="expenseAccount" [(ngModel)]="expenseAccountId" (ngModelChange)="treatmentReviewed = false" [disabled]="busy()">
              <option value="">Select an account</option>
              @for (a of w.expenseAccounts; track a.id) { <option [value]="a.id">{{ a.code }} · {{ a.name }}</option> }
            </select>
            @if (!w.liabilityAccounts.length || !w.expenseAccounts.length) {
              <p>Create a credit-normal liability account and a debit-normal expense account in the firm ledger first.</p>
            }
            <label class="check"><input type="checkbox" name="treatmentReviewed" [(ngModel)]="treatmentReviewed" [disabled]="busy()" />
              I recorded the treatment exactly as the accountant named it. A Partner must confirm it before any accrual.</label>
            <button matButton="outlined" type="submit" [disabled]="busy() || !treatmentReady()">Record treatment</button>
          </form>
        }
      </section>

      <section class="panel" aria-labelledby="accrual-heading">
        <h2 id="accrual-heading">Prepare a monthly accrual</h2>
        <p>Provision carried in posted journals: <strong>{{ w.postedProvisionBalance | money }} {{ w.currency ?? '' }}</strong></p>
        @if (w.accrualBlockedReason) { <p role="alert">{{ w.accrualBlockedReason }}</p> }
        @if (w.canPrepareAccrual) {
          <form (ngSubmit)="prepare(w.maxEvidenceBytes)">
            <label for="accrual-period">Period</label>
            <select id="accrual-period" name="accrualPeriod" [(ngModel)]="periodId" (ngModelChange)="touched()" [disabled]="busy()">
              <option value="">Select an open period</option>
              @for (p of w.openPeriods; track p.id) { <option [value]="p.id">{{ p.periodCode }}</option> }
            </select>
            <label for="accrual-amount">Amount ({{ w.currency }})</label>
            <input id="accrual-amount" name="accrualAmount" inputmode="decimal" maxlength="16" [(ngModel)]="amount" (ngModelChange)="touched()" [disabled]="busy()" />
            <label for="accrual-method">Calculation method</label>
            <textarea id="accrual-method" name="accrualMethod" maxlength="2000" [(ngModel)]="method" (ngModelChange)="touched()" [disabled]="busy()"></textarea>
            <label for="accrual-inputs">Calculation inputs</label>
            <textarea id="accrual-inputs" name="accrualInputs" maxlength="4000" [(ngModel)]="inputs" (ngModelChange)="touched()" [disabled]="busy()"></textarea>
            <label for="accrual-date">Date of the calculation</label>
            <input id="accrual-date" name="accrualDate" type="date" [(ngModel)]="calculationDate" (ngModelChange)="touched()" [disabled]="busy()" />
            <label for="accrual-reason">Reason for this entry</label>
            <textarea id="accrual-reason" name="accrualReason" maxlength="1000" [(ngModel)]="reason" (ngModelChange)="touched()" [disabled]="busy()"></textarea>
            <label for="accrual-evidence">Calculation workings (optional, up to 5 MB)</label>
            <input id="accrual-evidence" type="file" (change)="pick($event)" [disabled]="busy()" />
            <label class="check"><input type="checkbox" name="accrualReviewed" [(ngModel)]="accrualReviewed" [disabled]="busy()" />
              I entered the amount and its basis from a calculation performed outside AuditSphere.</label>
            <button matButton="filled" type="submit" [disabled]="busy() || !accrualReady()">Save accrual as a draft journal</button>
          </form>
          <p>The draft debits the confirmed expense account and credits the provision account. Submit it, then a different finance reviewer approves it before posting, from the <a routerLink="/app/finance">firm ledger</a>. A correction is a reversing journal followed by a new accrual.</p>
        }
      </section>

      <section class="panel" aria-labelledby="accruals-heading">
        <h2 id="accruals-heading">Accruals and their entered basis</h2>
        <div class="table-scroll"><table><caption class="sr-only">End-of-service accrual journals with the entered calculation basis</caption>
          <thead><tr><th scope="col">Journal</th><th scope="col">Period</th><th scope="col" class="number">Amount</th><th scope="col">Basis</th><th scope="col">Prepared by</th><th scope="col">Journal status</th></tr></thead>
          <tbody>@for (a of w.accruals; track a.id) { <tr>
            <th scope="row">{{ a.journalNumber }}</th><td>{{ a.periodCode }}</td>
            <td class="number">{{ a.amount | money }} {{ a.currency }}</td>
            <td><strong>Method:</strong> {{ a.method }}<small><strong>Inputs:</strong> {{ a.inputs }}</small>
              <small>Calculated {{ a.calculationDate }} · treatment version {{ a.treatmentVersion }}</small><small><strong>Reason:</strong> {{ a.reason }}</small></td>
            <td>{{ a.preparedBy }}</td><td><audit-status [value]="a.journalStatus" /></td>
          </tr> } @empty { <tr><td colspan="6">No accrual has been prepared.</td></tr> }</tbody></table></div>
      </section>
    }
  `,
  styles: `
    form { display:grid; gap:.5rem; max-width:44rem; margin-top:1rem; }
    textarea { min-height:5rem; }
    input, select, textarea { min-height:2.75rem; padding:.45rem .65rem; }
    .check { display:flex; gap:.5rem; align-items:flex-start; }
    .check input { min-height:auto; margin-top:.25rem; }
    dl { display:grid; grid-template-columns:minmax(10rem,16rem) 1fr; gap:.35rem 1rem; }
    dt { font-weight:600; } dd { margin:0; } .pre { white-space:pre-wrap; }
    td small { display:block; margin-top:.25rem; color:var(--text-muted,#566575); }
    td, th { vertical-align:top; }
  `,
})
export class EndOfServiceProvision {
  private readonly api = inject(Api);
  readonly workspace = this.api.resource(() => '/api/ui/finance/end-of-service', decodeEndOfService,
    'The end-of-service provision is limited to firm-wide FinanceManager, FinanceReviewer and Partner grants.');
  readonly busy = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);

  treatmentText = ''; accountantName = ''; accountantCredential = ''; provisionAccountId = ''; expenseAccountId = '';
  treatmentReviewed = false;
  confirmationNote = ''; confirmReviewed = false;
  periodId = ''; amount = ''; method = ''; inputs = ''; calculationDate = ''; reason = ''; accrualReviewed = false;
  private evidence: File | null = null;
  /** Kept across an unconfirmed outcome so an identical retry returns the same draft instead of adding a second one. */
  private requestId: string | null = null;

  treatmentReady(): boolean {
    return this.treatmentReviewed && this.treatmentText.trim().length >= 20 && this.accountantName.trim().length >= 2 &&
      this.accountantCredential.trim().length >= 2 && !!this.provisionAccountId && !!this.expenseAccountId &&
      this.provisionAccountId !== this.expenseAccountId;
  }
  accrualReady(): boolean {
    return this.accrualReviewed && !!this.periodId && validAccrualAmount(this.amount.trim()) && this.method.trim().length >= 5 &&
      this.inputs.trim().length >= 5 && this.reason.trim().length >= 5 && /^\d{4}-\d{2}-\d{2}$/.test(this.calculationDate) &&
      this.calculationDate <= new Date().toISOString().slice(0, 10);
  }
  /** Any edit withdraws the reviewed confirmation and makes the next save a new request. */
  touched(): void { this.accrualReviewed = false; this.requestId = null; }
  pick(event: Event): void {
    this.evidence = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.touched();
  }

  async record(expectedVersion: number): Promise<void> {
    if (this.busy() || !this.treatmentReady()) return;
    await this.run(() => this.api.command('/api/ui/finance/end-of-service/treatment', {
      measurementTreatment: this.treatmentText.trim(), accountantName: this.accountantName.trim(),
      accountantCredential: this.accountantCredential.trim(), provisionAccountId: this.provisionAccountId,
      expenseAccountId: this.expenseAccountId, expectedVersion, reviewed: true,
    }), 'Treatment recorded. A Partner other than you must confirm it before any accrual.', () => {
      this.treatmentText = this.accountantName = this.accountantCredential = this.provisionAccountId = this.expenseAccountId = '';
      this.treatmentReviewed = false;
    });
  }

  async confirm(id: string): Promise<void> {
    const note = this.confirmationNote.trim();
    if (this.busy() || !this.confirmReviewed || note.length < 5) return;
    await this.run(() => this.api.command(`/api/ui/finance/end-of-service/treatment/${id}/confirm`, { note, reviewed: true }),
      'Treatment confirmed. Finance can now prepare accruals under it.', () => { this.confirmationNote = ''; this.confirmReviewed = false; });
  }

  async prepare(maxEvidenceBytes: number): Promise<void> {
    if (this.busy() || !this.accrualReady()) return;
    if (this.evidence && (this.evidence.size < 1 || this.evidence.size > maxEvidenceBytes)) {
      this.failed.set(true); this.message.set('The supporting document must be between 1 byte and 5 MB.'); return;
    }
    this.requestId ??= crypto.randomUUID();
    const form = new FormData();
    form.set('periodId', this.periodId); form.set('amount', this.amount.trim()); form.set('method', this.method.trim());
    form.set('inputs', this.inputs.trim()); form.set('calculationDate', this.calculationDate); form.set('reason', this.reason.trim());
    form.set('requestId', this.requestId); form.set('reviewed', 'true');
    if (this.evidence) form.set('evidence', this.evidence, this.evidence.name);
    await this.run(() => this.api.upload('/api/ui/finance/end-of-service/accruals', form),
      'Accrual saved as a draft journal. Submit it for independent review from the firm ledger.', () => {
        this.amount = this.method = this.inputs = this.reason = this.calculationDate = this.periodId = '';
        this.accrualReviewed = false; this.evidence = null; this.requestId = null;
      }, true);
  }

  private async run(send: () => Promise<{ ok: boolean; unknown?: boolean; message?: string }>, done: string, reset: () => void, keepRequestOnUnknown = false): Promise<void> {
    this.busy.set(true); this.message.set(''); this.failed.set(false);
    const outcome = await send();
    this.busy.set(false);
    if (outcome.ok) { reset(); this.message.set(done); }
    else {
      this.failed.set(true);
      this.message.set(outcome.message ?? 'The request was not accepted.');
      if (!(keepRequestOnUnknown && outcome.unknown)) this.requestId = null;
      this.accrualReviewed = this.treatmentReviewed = this.confirmReviewed = false;
    }
    this.workspace.reload();
  }
}
