import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { arr, bool, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

export const decodeLedger = obj({ canCreateSetup: bool, canClosePeriod: bool,
  periods: arr(obj({ id: guid, periodCode: text, status: text, revision: nat, closedAt: nullable(instant) }), 5000),
  accounts: arr(obj({ id: guid, code: text, name: text, accountType: text, normalSide: text, postingAllowed: bool }), 5000),
  postings: arr(obj({ id: guid, postedAt: instant, currency: text, postedByUserId: guid, reversalOfPostingId: nullable(guid) }), 5000) });

type FirmLedgerSetupRequest =
  | { kind: 'account'; value: { code: string; name: string; accountType: string; normalSide: string; postingAllowed: boolean } }
  | { kind: 'period'; value: { periodCode: string } };
type FirmLedgerSetupResolution = 'checking' | 'saved' | 'absent' | 'conflict' | 'error' | null;

type CloseVerification = 'idle' | 'loading' | 'closed' | 'open' | 'changed' | 'error';
interface PendingClose {
  periodId: string;
  reason: string;
  revision: number | null;
}

@Component({
  selector: 'audit-firm-ledger',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app">← Back to portfolio</a>
    <audit-page-header title="Firm ledger & financial operations" eyebrow="Firm economics"
      description="Restricted firm-ledger operations (§41.3). Firm accounts, balanced postings, and period close decisions enforce exact monetary balance and strict immutability." />
    <p class="actions"><button matButton="outlined" (click)="ledger.reload()">Refresh firm ledger</button><a routerLink="/app/finance/books">Firm books</a><a routerLink="/app/finance/receivables-aging">Receivables ageing</a></p>
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
          @if (cmd.uncertain()) {
            <section class="panel" aria-labelledby="close-verification-heading">
              <h3 id="close-verification-heading">Verify saved period close</h3>
              <p>The close request may have been saved, but its response was not received. Check the persisted period before deciding what to do. AuditSphere will not repeat this request automatically.</p>
              @switch (closeVerification()) {
                @case ('idle') {
                  <button matButton="outlined" (click)="verifyClose()">Verify saved period state</button>
                }
                @case ('loading') { <p role="status">Checking the saved period state…</p> }
                @case ('closed') {
                  <p role="status">The saved period is CLOSED at revision {{ verifiedRevision() }}. No retry is needed.</p>
                  <button matButton="filled" (click)="finishVerifiedClose()">Acknowledge saved close</button>
                }
                @case ('open') {
                  <p role="status">The saved period remains OPEN at revision {{ verifiedRevision() }}. Review the current period and reason before you choose whether to retry.</p>
                  <button matButton="outlined" (click)="acknowledgeOpenPeriod()">Acknowledge period is still open</button>
                }
                @case ('changed') {
                  <p role="alert">The period changed while the request was unresolved. Discard this attempt, refresh the period, and review a new close action.</p>
                  <button matButton="outlined" (click)="discardChangedClose()">Discard unresolved close</button>
                }
                @case ('error') {
                  <p role="alert">The saved period state could not be verified. Keep this close unresolved and retry the state check.</p>
                  <button matButton="outlined" (click)="verifyClose()">Retry state check</button>
                }
              }
            </section>
          } @else {
            <div class="panel"><h3>Confirm period close</h3><p>Closing a fiscal period freezes it. All journals within the period must be posted prior to close.</p>
              <label>Close reason <input name="reason" [(ngModel)]="reason" required maxlength="500" /></label>
              <p class="actions"><button matButton="filled" (click)="close(id)" [disabled]="cmd.busy() || !reason.trim()">Confirm close</button><button matButton (click)="closing.set(null)" [disabled]="cmd.busy()">Cancel</button></p></div>
          }
        }
      </section>
      @if (l.canCreateSetup) {
        <section class="panel" aria-labelledby="ledger-setup-heading">
          <h2 id="ledger-setup-heading">Set up firm ledger</h2>
          <p>Finance Managers can add firm accounts and open fiscal months. Account codes and period codes are unique within this firm.</p>
          <div class="workspace-grid">
            <form class="inline-form" (submit)="$event.preventDefault(); createAccount()">
              <h3>New account</h3>
              <label>Account code <input name="accountCode" [(ngModel)]="accountDraft.code" maxlength="32" [disabled]="setupLocked()" required /></label>
              <label>Account name <input name="accountName" [(ngModel)]="accountDraft.name" maxlength="200" [disabled]="setupLocked()" required /></label>
              <label>Account type <select name="accountType" [(ngModel)]="accountDraft.accountType" [disabled]="setupLocked()">
                <option value="ASSET">Asset</option><option value="LIABILITY">Liability</option><option value="EQUITY">Equity</option>
                <option value="REVENUE">Revenue</option><option value="EXPENSE">Expense</option>
              </select></label>
              <label>Normal side <select name="normalSide" [(ngModel)]="accountDraft.normalSide" [disabled]="setupLocked()">
                <option value="DEBIT">Debit</option><option value="CREDIT">Credit</option>
              </select></label>
              <label class="checkbox-label"><input type="checkbox" name="postingAllowed" [(ngModel)]="accountDraft.postingAllowed" [disabled]="setupLocked()" /> Allow posting to this account</label>
              <button matButton="filled" type="submit" [disabled]="setupLocked() || !accountDraft.code.trim() || !accountDraft.name.trim()">Create account</button>
            </form>
            <form class="inline-form" (submit)="$event.preventDefault(); createPeriod()">
              <h3>New fiscal period</h3>
              <p>Open a month before recording or posting activity for that month.</p>
              <label>Fiscal month <input type="month" name="periodCode" [(ngModel)]="periodDraft.periodCode" [disabled]="setupLocked()" required /></label>
              <button matButton="filled" type="submit" [disabled]="setupLocked() || !periodDraft.periodCode">Create fiscal period</button>
            </form>
          </div>
          <audit-command-message [message]="setupCommand.message()" [failed]="setupCommand.failed()" />
          @if (pendingSetup(); as pending) {
            <section class="panel" aria-labelledby="setup-recovery-heading">
              <h3 id="setup-recovery-heading">Verify saved ledger setup</h3>
              <p>The response to the setup request was not confirmed. Check the persisted firm ledger before repeating it. AuditSphere will not send it again automatically.</p>
              <p><strong>Request:</strong> {{ pending.kind === 'account' ? 'Account ' + pending.value.code : 'Fiscal month ' + pending.value.periodCode }}</p>
              @if (setupVerificationMessage()) { <p [attr.role]="setupResolution() === 'conflict' || setupResolution() === 'error' ? 'alert' : 'status'">{{ setupVerificationMessage() }}</p> }
              @switch (setupResolution()) {
                @case ('checking') { <p role="status">Checking the exact persisted setup…</p> }
                @case ('saved') {
                  <button matButton="filled" type="button" (click)="acknowledgeSavedSetup()">Acknowledge saved setup</button>
                }
                @case ('absent') {
                  <p role="status">No matching setup record is present. You can retry this exact request or discard it and prepare a different one.</p>
                  <button matButton="filled" type="button" (click)="retryExactSetup()" [disabled]="setupCommand.busy()">Retry exact setup</button>
                  <button matButton="outlined" type="button" (click)="discardUnresolvedSetup()">Discard unresolved setup</button>
                }
                @case ('conflict') {
                  <p role="alert">This natural key exists with different saved values. No retry is available until you refresh and review the current firm setup.</p>
                  <button matButton="outlined" type="button" (click)="discardUnresolvedSetup()">Discard and refresh</button>
                }
                @case ('error') {
                  <button matButton="outlined" type="button" (click)="verifySetup()">Retry state check</button>
                }
                @default {
                  <button matButton="outlined" type="button" (click)="verifySetup()" [disabled]="setupCommand.busy()">Check saved setup</button>
                }
              }
            </section>
          }
        </section>
      }
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
    @if (!cmd.uncertain()) { <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" /> }
  `,
})
export class FirmLedger {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly ledger = this.api.resource(() => '/api/ui/finance', decodeLedger, 'Sign in with an authorized finance identity to access the firm ledger.');
  readonly cmd = new CommandState(this.api);
  readonly setupCommand = new CommandState(this.api);
  readonly pendingSetup = signal<FirmLedgerSetupRequest | null>(null);
  readonly setupResolution = signal<FirmLedgerSetupResolution>(null);
  readonly setupVerificationMessage = signal('');
  readonly accountDraft = { code: '', name: '', accountType: 'ASSET', normalSide: 'DEBIT', postingAllowed: true };
  readonly periodDraft = { periodCode: '' };
  readonly closing = signal<string | null>(null);
  readonly closeVerification = signal<CloseVerification>('idle');
  readonly verifiedRevision = signal<number | null>(null);
  private readonly pendingClose = signal<PendingClose | null>(null);
  reason = 'Standard accounting period close';

  constructor() {
    let generation = this.session.invalidation();
    effect(() => {
      const current = this.session.invalidation();
      this.session.current();
      untracked(() => {
        if (current === generation) return;
        generation = current;
        this.pendingSetup.set(null);
        this.setupResolution.set(null);
        this.setupVerificationMessage.set('');
        this.setupCommand.uncertain.set(false);
        this.setupCommand.failed.set(false);
        this.accountDraft.code = '';
        this.accountDraft.name = '';
        this.periodDraft.periodCode = '';
      });
    });
  }

  setupLocked(): boolean {
    return this.setupCommand.busy() || this.setupCommand.uncertain() || this.pendingSetup() !== null;
  }

  async createAccount(): Promise<void> {
    const value = { code: this.accountDraft.code.trim(), name: this.accountDraft.name.trim(),
      accountType: this.accountDraft.accountType, normalSide: this.accountDraft.normalSide,
      postingAllowed: this.accountDraft.postingAllowed };
    await this.sendSetup({ kind: 'account', value });
  }

  async createPeriod(): Promise<void> {
    const value = { periodCode: this.periodDraft.periodCode.trim() };
    await this.sendSetup({ kind: 'period', value });
  }

  private async sendSetup(request: FirmLedgerSetupRequest, retry = false): Promise<void> {
    if (this.setupCommand.busy() || !this.ledger.data()?.canCreateSetup) return;
    if (!retry) {
      if (this.pendingSetup() || this.setupCommand.uncertain()) return;
      this.pendingSetup.set(request);
      this.setupResolution.set(null);
      this.setupVerificationMessage.set('');
    }
    const path = request.kind === 'account' ? '/api/ui/finance/accounts' : '/api/ui/finance/periods';
    const saved = await this.setupCommand.run(path, request.value,
      request.kind === 'account' ? 'Firm account saved.' : 'Fiscal period opened.', () => {
        this.pendingSetup.set(null);
        this.setupResolution.set(null);
        this.setupVerificationMessage.set('');
        this.ledger.reload();
      });
    if (!saved && !this.setupCommand.uncertain()) this.pendingSetup.set(null);
    else if (!saved) this.setupResolution.set(null);
  }

  async verifySetup(): Promise<void> {
    const pending = this.pendingSetup();
    if (!pending || this.setupResolution() === 'checking') return;
    this.setupResolution.set('checking');
    this.setupVerificationMessage.set('');
    try {
      const persisted = await this.api.get('/api/ui/finance', decodeLedger);
      if (pending.kind === 'period') {
        const found = persisted.periods.find(period => period.periodCode === pending.value.periodCode);
        this.setupResolution.set(found ? 'saved' : 'absent');
        this.setupVerificationMessage.set(found
          ? `Persisted state confirms fiscal period ${found.periodCode} is ${found.status}.`
          : 'No period with this exact code is present in persisted firm ledger state.');
      } else {
        const found = persisted.accounts.find(account => account.code === pending.value.code);
        const matches = found && found.name === pending.value.name && found.accountType === pending.value.accountType &&
          found.normalSide === pending.value.normalSide && found.postingAllowed === pending.value.postingAllowed;
        this.setupResolution.set(found ? matches ? 'saved' : 'conflict' : 'absent');
        this.setupVerificationMessage.set(matches
          ? `Persisted state confirms account ${found!.code} ${found!.name}.`
          : found ? 'The account code exists with different saved values.'
            : 'No account with this exact code is present in persisted firm ledger state.');
      }
      this.ledger.reload();
    } catch {
      this.setupResolution.set('error');
      this.setupVerificationMessage.set('Persisted firm ledger state could not be verified. Keep this request unresolved and retry the state check.');
    }
  }

  async retryExactSetup(): Promise<void> {
    const pending = this.pendingSetup();
    if (!pending || this.setupResolution() !== 'absent' || this.setupCommand.busy()) return;
    this.setupCommand.uncertain.set(false);
    this.setupCommand.failed.set(false);
    this.setupResolution.set(null);
    this.setupVerificationMessage.set('');
    await this.sendSetup(pending, true);
  }

  acknowledgeSavedSetup(): void {
    if (!this.pendingSetup() || this.setupResolution() !== 'saved') return;
    this.clearUnresolvedSetup();
    this.setupCommand.message.set('Persisted firm ledger state confirms the setup request. It was not repeated.');
    this.ledger.reload();
  }

  discardUnresolvedSetup(): void {
    if (!this.pendingSetup() || !['absent', 'conflict'].includes(this.setupResolution() ?? '')) return;
    this.clearUnresolvedSetup();
    this.setupCommand.message.set('The unresolved setup request was discarded. Refresh the ledger before preparing another change.');
    this.ledger.reload();
  }

  private clearUnresolvedSetup(): void {
    this.setupCommand.uncertain.set(false);
    this.setupCommand.failed.set(false);
    this.pendingSetup.set(null);
    this.setupResolution.set(null);
    this.setupVerificationMessage.set('');
  }

  async close(id: string): Promise<void> {
    const period = this.ledger.data()?.periods.find(x => x.id === id);
    this.pendingClose.set({ periodId: id, reason: this.reason.trim(), revision: period?.revision ?? null });
    const succeeded = await this.cmd.run(`/api/ui/finance/periods/${id}/close`, { reason: this.reason }, 'Fiscal period successfully closed.', () => {
      this.closing.set(null);
      this.pendingClose.set(null);
      this.closeVerification.set('idle');
      this.verifiedRevision.set(null);
    });
    if (!succeeded && this.cmd.uncertain()) this.closeVerification.set('idle');
    else if (!succeeded) this.pendingClose.set(null);
    this.ledger.reload();
  }

  async verifyClose(): Promise<void> {
    const attempt = this.pendingClose();
    if (!attempt || !this.cmd.uncertain() || this.closeVerification() === 'loading') return;
    this.closeVerification.set('loading');
    try {
      const saved = await this.api.get('/api/ui/finance', decodeLedger);
      const period = saved.periods.find(x => x.id === attempt.periodId);
      if (!period) {
        this.closeVerification.set('error');
      } else {
        this.verifiedRevision.set(period.revision);
        if (period.status === 'CLOSED') this.closeVerification.set('closed');
        else if (period.status === 'OPEN' && period.revision === attempt.revision) this.closeVerification.set('open');
        else this.closeVerification.set('changed');
      }
      this.ledger.reload();
    } catch {
      this.closeVerification.set('error');
    }
  }

  finishVerifiedClose(): void {
    if (this.closeVerification() !== 'closed') return;
    this.clearUnknownClose();
    this.closing.set(null);
    this.ledger.reload();
  }

  acknowledgeOpenPeriod(): void {
    const attempt = this.pendingClose();
    if (this.closeVerification() !== 'open' || !attempt) return;
    this.reason = attempt.reason;
    this.clearUnknownClose();
    this.closeVerification.set('idle');
    this.pendingClose.set(null);
  }

  discardChangedClose(): void {
    if (this.closeVerification() !== 'changed') return;
    this.clearUnknownClose();
    this.pendingClose.set(null);
    this.closeVerification.set('idle');
    this.verifiedRevision.set(null);
    this.closing.set(null);
    this.ledger.reload();
  }

  private clearUnknownClose(): void {
    this.cmd.uncertain.set(false);
    this.cmd.failed.set(false);
    this.cmd.message.set('');
  }
}
