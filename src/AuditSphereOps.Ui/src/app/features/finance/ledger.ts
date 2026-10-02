import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { arr, bool, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeLedger = obj({ canClosePeriod: bool,
  periods: arr(obj({ id: guid, periodCode: text, status: text, revision: nat, closedAt: nullable(instant) }), 5000),
  accounts: arr(obj({ id: guid, code: text, name: text, accountType: text, normalSide: text, postingAllowed: bool }), 5000),
  postings: arr(obj({ id: guid, postedAt: instant, currency: text, postedByUserId: guid, reversalOfPostingId: nullable(guid) }), 5000) });

@Component({
  selector: 'audit-firm-ledger',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app">← Back to portfolio</a>
    <audit-page-header title="Firm ledger & financial operations" eyebrow="Firm economics"
      description="Restricted firm-ledger operations (§41.3). Firm accounts, balanced postings, and period close decisions enforce exact monetary balance and strict immutability." />
    <p class="actions"><button matButton="outlined" (click)="ledger.reload()">Refresh firm ledger</button><a routerLink="/app/finance/books">Firm books</a></p>
    <audit-state [loading]="ledger.loading()" [error]="ledger.error()" label="firm finance records" />
    @if (ledger.data(); as l) {
      <p role="status">{{ l.periods.length }} fiscal periods · {{ l.accounts.length }} firm accounts · {{ l.postings.length }} recent postings shown</p>
      <section class="panel" aria-labelledby="periods-heading">
        <h2 id="periods-heading">Fiscal periods</h2>
        <div class="table-scroll"><table><thead><tr><th>Period code</th><th>Status</th><th>Revision</th><th>Closed date</th><th><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>@for (p of l.periods; track p.id) {
            <tr><td><strong>{{ p.periodCode }}</strong></td><td><audit-status [value]="p.status" /></td><td>Rev {{ p.revision }}</td><td>{{ p.closedAt ? p.closedAt.slice(0, 16).replace('T', ' ') : '—' }}</td>
              <td>@if (p.status === 'OPEN' && l.canClosePeriod) { <button matButton="filled" (click)="closing.set(p.id); reason = 'Standard accounting period close'" [disabled]="cmd.busy()">Close period</button> }</td></tr>
          } @empty { <tr><td colspan="5">No fiscal periods are configured for this firm.</td></tr> }</tbody></table></div>
        @if (closing(); as id) {
          <div class="panel"><h3>Confirm period close</h3><p>Closing a fiscal period freezes it. All journals within the period must be posted prior to close.</p>
            <label>Close reason <input name="reason" [(ngModel)]="reason" required maxlength="500" /></label>
            <p class="actions"><button matButton="filled" (click)="close(id)" [disabled]="cmd.busy() || !reason.trim()">Confirm close</button><button matButton (click)="closing.set(null)" [disabled]="cmd.busy()">Cancel</button></p></div>
        }
      </section>
      <section class="panel" aria-labelledby="accounts-heading">
        <h2 id="accounts-heading">Chart of firm accounts</h2>
        <div class="table-scroll"><table><thead><tr><th>Code</th><th>Account name</th><th>Type</th><th>Normal side</th><th>Posting allowed</th></tr></thead>
          <tbody>@for (a of l.accounts; track a.id) { <tr><td><code>{{ a.code }}</code></td><td>{{ a.name }}</td><td>{{ a.accountType }}</td><td>{{ a.normalSide }}</td><td>{{ a.postingAllowed ? 'Yes' : 'No' }}</td></tr> }
          @empty { <tr><td colspan="5">No firm posting accounts are configured.</td></tr> }</tbody></table></div>
      </section>
      <section class="panel" aria-labelledby="postings-heading">
        <h2 id="postings-heading">Recent firm postings</h2>
        <div class="table-scroll"><table><thead><tr><th>Posting ID</th><th>Posted date</th><th>Currency</th><th>Posted by</th><th>Reversal</th></tr></thead>
          <tbody>@for (p of l.postings; track p.id) { <tr><td><code>{{ p.id }}</code></td><td>{{ p.postedAt.slice(0, 16).replace('T', ' ') }} UTC</td><td>{{ p.currency }}</td><td><code>{{ p.postedByUserId }}</code></td>
            <td>{{ p.reversalOfPostingId ? 'Reversal of ' + p.reversalOfPostingId : '—' }}</td></tr> }
          @empty { <tr><td colspan="5">No immutable postings recorded in the firm ledger yet.</td></tr> }</tbody></table></div>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class FirmLedger {
  private readonly api = inject(Api);
  readonly ledger = this.api.resource(() => '/api/ui/finance', decodeLedger, 'Sign in with an authorized finance identity to access the firm ledger.');
  readonly cmd = new CommandState(this.api);
  readonly closing = signal<string | null>(null);
  reason = 'Standard accounting period close';
  close(id: string): void {
    void this.cmd.run(`/api/ui/finance/periods/${id}/close`, { reason: this.reason }, 'Fiscal period successfully closed.', () => this.closing.set(null)).finally(() => this.ledger.reload());
  }
}
