import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { arr, bool, dec, decimalInput, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

/** One charge-out rate version. CanApprove is computed by the server for the current actor; the screen only renders it. */
const version = obj({ id: guid, version: nat, ratePerHour: dec, status: text, preparedByUserId: guid, approvedByUserId: nullable(guid),
  approvedAt: nullable(instant), createdAt: instant, canApprove: bool });
const slot = obj({ role: text, activity: text, currency: text, approved: nullable(version), draft: nullable(version) });
const baselineLine = obj({ role: text, ratePerHour: dec, state: text, cardId: nullable(guid), approvedByUserId: nullable(guid) });
export const decodeRateCards = obj({ canRevise: bool, slots: arr(slot, 5000), steBaseline: arr(baselineLine, 20) });

@Component({
  selector: 'audit-practice-rate-cards',
  imports: [FormsModule, MatButtonModule, ...SHARED],
  template: `
    <audit-page-header title="Charge-out rates" eyebrow="Practice management"
      description="A rate is recorded as a draft by one person and takes effect only when a different firm-wide approver approves it. Quotations and time capture use approved rates only." />
    <audit-state [loading]="rates.loading()" [error]="rates.error()" label="charge-out rates" />
    @if (rates.data(); as rv) {
      <section class="panel" aria-labelledby="baseline-heading">
        <h2 id="baseline-heading">STE QAR baseline</h2>
        <p>These are the standard charge-out values of an hour of billable work. They are not the actual cost of staff, which is recorded separately and is never used as the charge-out value.</p>
        <div class="table-scroll"><table>
          <caption>STE baseline by role</caption>
          <thead><tr><th scope="col">Role</th><th scope="col" class="number">Standard charge-out (QAR per hour)</th><th scope="col">Baseline state</th></tr></thead>
          <tbody>@for (b of rv.steBaseline; track b.role) { <tr><td>{{ b.role }}</td><td class="number">{{ b.ratePerHour | money }}</td><td><audit-status [value]="b.state" /></td></tr> }</tbody>
        </table></div>
        @if (rv.steBaseline.some((b) => b.state === 'MISSING')) {
          <button matButton="filled" (click)="initializeBaseline()" [disabled]="cmd.busy()">Create missing STE baseline drafts</button>
          <p><small>Creating drafts never approves them. Each draft needs approval by a different firm-wide approver.</small></p>
        }
      </section>
      <section class="panel" aria-labelledby="slots-heading">
        <h2 id="slots-heading">Rates by role, activity and currency</h2>
        <div class="table-scroll"><table>
          <caption>Approved and pending charge-out rates</caption>
          <thead><tr>
            <th scope="col">Role</th><th scope="col">Activity</th><th scope="col">Currency</th>
            <th scope="col" class="number">Approved per hour</th><th scope="col">Approved version</th>
            <th scope="col" class="number">Draft per hour</th><th scope="col">Draft version</th><th scope="col">Draft status</th>
            <th scope="col"><span class="sr-only">Action</span></th>
          </tr></thead>
          <tbody>@for (s of rv.slots; track s.role + '|' + s.activity + '|' + s.currency) {
            <tr>
              <td>{{ s.role }}</td><td>{{ s.activity }}</td><td>{{ s.currency }}</td>
              <td class="number">@if (s.approved; as a) { {{ a.ratePerHour | money }} } @else { None approved }</td>
              <td>@if (s.approved; as a) { {{ a.version }} }</td>
              <td class="number">@if (s.draft; as d) { {{ d.ratePerHour | money }} }</td>
              <td>@if (s.draft; as d) { {{ d.version }} }</td>
              <td>@if (s.draft; as d) { <audit-status [value]="d.status" /> }</td>
              <td>
                @if (s.draft; as d) {
                  @if (d.canApprove) {
                    <button matButton (click)="approveRate(d.id)" [disabled]="cmd.busy()">Approve draft</button>
                  } @else {
                    <small>Awaiting a different approver</small>
                  }
                }
              </td>
            </tr>
          } @empty { <tr><td colspan="9">No charge-out rates are recorded for this firm.</td></tr> }</tbody>
        </table></div>
      </section>
      @if (rv.canRevise) {
        <section class="panel" aria-labelledby="draft-heading">
          <h2 id="draft-heading">Record a rate draft</h2>
          <p>Leave the current version blank for a new rate. When revising, enter the version you read so a concurrent change is refused.</p>
          <form class="inline-form" (submit)="$event.preventDefault(); reviseRate()">
            <label>Role <input name="rateRole" [(ngModel)]="rate.role" required maxlength="100" /></label>
            <label>Activity <input name="rateActivity" [(ngModel)]="rate.activity" required maxlength="100" /></label>
            <label>Currency <input name="rateCurrency" [(ngModel)]="rate.currency" required maxlength="3" /></label>
            <label>Charge-out per hour <input name="rateValue" inputmode="decimal" [(ngModel)]="rate.ratePerHour" required /></label>
            <label>Current version (optional) <input name="rateVersion" inputmode="numeric" [(ngModel)]="rate.expectedVersion" /></label>
            <button matButton="outlined" type="submit" [disabled]="cmd.busy()">Record rate for approval</button>
          </form>
        </section>
      }
    } @else if (!rates.error()) {
      <p>Loading charge-out rates…</p>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class PracticeRateCards {
  private readonly api = inject(Api);
  readonly rates = this.api.resource(() => '/api/ui/practice/rate-cards', decodeRateCards,
    'Charge-out rate governance is available to Managers, Partners and Administrators in firm scope.');
  readonly cmd = new CommandState(this.api);
  rate = { role: '', activity: 'General', currency: 'QAR', ratePerHour: '', expectedVersion: '' };

  initializeBaseline(): void {
    this.cmd.run('/api/ui/practice/rate-cards/ste-baseline', {},
      'STE baseline drafts were created. Each draft needs approval by a different authorized approver.').finally(() => this.rates.reload());
  }

  reviseRate(): void {
    const value = decimalInput(this.rate.ratePerHour, 2);
    const version = this.rate.expectedVersion.trim() ? Math.trunc(Number(this.rate.expectedVersion)) : null;
    if (value === null || (version !== null && !Number.isSafeInteger(version))) {
      this.cmd.failed.set(true);
      this.cmd.message.set('Enter the charge-out rate as a number, and the current version as a whole number when given.');
      return;
    }
    this.cmd.run('/api/ui/practice/rate-cards', { role: this.rate.role.trim(), activity: this.rate.activity.trim(),
      currency: this.rate.currency.trim().toUpperCase(), ratePerHour: value, expectedVersion: version },
      'The rate was recorded as a draft and needs approval before it is used.',
      () => (this.rate = { role: '', activity: 'General', currency: 'QAR', ratePerHour: '', expectedVersion: '' }))
      .finally(() => this.rates.reload());
  }

  approveRate(id: string): void {
    this.cmd.run(`/api/ui/practice/rate-cards/${id}/approve`, {}, 'The charge-out rate was approved.').finally(() => this.rates.reload());
  }
}
