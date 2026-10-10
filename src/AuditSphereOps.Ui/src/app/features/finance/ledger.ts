import { Component, ElementRef, effect, inject, signal, untracked, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { arr, bool, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

export const decodeLedger = obj({ canCreateSetup: bool, canClosePeriod: bool, canReviewJournals: bool,
  canPostJournals: bool, canCreateJournals: bool, canReversePostings: bool, canReopenPeriod: bool,
  periods: arr(obj({ id: guid, periodCode: text, status: text, revision: nat, closedAt: nullable(instant) }), 5000),
  accounts: arr(obj({ id: guid, code: text, name: text, accountType: text, normalSide: text, postingAllowed: bool }), 5000),
  postings: arr(obj({ id: guid, periodId: nullable(guid), journalId: nullable(guid), postedAt: instant, currency: text, postedByUserId: guid, reversalOfPostingId: nullable(guid) }), 5000),
  journals: arr(obj({ id: guid, periodId: guid, periodCode: text, journalNumber: text, sourceKind: text, sourceKey: text,
    sourceRevision: nat, postingPurpose: text, currency: text, status: text, createdByUserId: guid,
    approvedByUserId: nullable(guid), createdAt: instant, approvedAt: nullable(instant), postedAt: nullable(instant),
    supportingEvidenceFileName: nullable(text), supportingEvidenceSha256: nullable(text),
    lines: arr(obj({ firmAccountId: guid, accountCode: text, accountName: text, description: text, debit: text, credit: text }), 200) }), 50) });

interface JournalDraftLine { firmAccountId: string; description: string; debit: string; credit: string }
interface JournalDraft {
  periodId: string; journalNumber: string; sourceKey: string; postingPurpose: string; currency: string; lines: JournalDraftLine[];
  supportingEvidence: File | null; supportingEvidenceSha256: string | null;
}
type JournalCreateState = 'checking' | 'saved' | 'absent' | 'conflict' | 'error' | null;
type JournalActionKind = 'submit' | 'review' | 'post';
type JournalActionState = 'checking' | 'saved' | 'unchanged' | 'changed' | 'error' | null;
interface PendingJournalAction { id: string; action: JournalActionKind; previousStatus: string; nextStatus: string }

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
    <p class="actions"><button matButton="outlined" (click)="ledger.reload()">Refresh firm ledger</button><a routerLink="/app/finance/books">Firm books</a><a routerLink="/app/finance/receivables-aging">Receivables ageing</a><a routerLink="/app/finance/end-of-service">End-of-service provision</a></p>
    <audit-state [loading]="ledger.loading()" [error]="ledger.error()" label="firm finance records" />
    @if (ledger.data(); as l) {
      <p role="status">{{ l.periods.length }} fiscal periods · {{ l.accounts.length }} firm accounts · {{ l.postings.length }} recent postings shown</p>
      <section class="panel" aria-labelledby="periods-heading">
        <h2 id="periods-heading">Fiscal periods</h2>
        <div class="table-scroll"><table><thead><tr><th>Period code</th><th>Status</th><th>Revision</th><th>Closed date</th><th><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>@for (p of l.periods; track p.id) {
            <tr><td><strong>{{ p.periodCode }}</strong></td><td><audit-status [value]="p.status" /></td><td>Rev {{ p.revision }}</td><td>{{ p.closedAt ? p.closedAt.slice(0, 16).replace('T', ' ') : '—' }}</td>
              <td>@if (p.status === 'OPEN' && l.canClosePeriod) { <button matButton="filled" (click)="closing.set(p.id); reason = 'Standard accounting period close'" [disabled]="cmd.busy()">Close period</button> }
                  @if (p.status === 'CLOSED' && l.canReopenPeriod) { <button matButton="outlined" (click)="reopening.set(p.id); reopenReason = 'Correction required for fiscal period'" [disabled]="cmd.busy()">Reopen period</button> }</td></tr>
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
        @if (reopening(); as id) {
          <div class="panel"><h3>Confirm period reopen</h3><p>Reopening a fiscal period allows authorized journals to be posted or reversed within it. A recorded reason is required.</p>
            <label>Reopen reason <input name="reopenReason" [(ngModel)]="reopenReason" required maxlength="500" /></label>
            <p class="actions"><button matButton="filled" (click)="reopen(id)" [disabled]="cmd.busy() || !reopenReason.trim()">Confirm reopen</button><button matButton (click)="reopening.set(null)" [disabled]="cmd.busy()">Cancel</button></p></div>
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
          @if (setupCommand.message()) { <audit-command-message [message]="setupCommand.message()" [failed]="setupCommand.failed()" /> }
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
      <section class="panel" aria-labelledby="journal-heading">
        <h2 id="journal-heading">Manual journals</h2>
        <p>Prepare balanced firm-book entries for an open fiscal period. Submission and approval are separate steps; the person who prepares a journal cannot approve it.</p>
        @if (l.canCreateJournals) {
          <form class="inline-form" (submit)="$event.preventDefault(); createJournal()">
            <label>Fiscal period <select name="journalPeriod" [(ngModel)]="journalDraft.periodId" [disabled]="journalCreateLocked()" required>
              <option value="">Select an open period</option>
              @for (p of l.periods; track p.id) { @if (p.status === 'OPEN') { <option [value]="p.id">{{ p.periodCode }}</option> } }
            </select></label>
            <label>Journal number <input name="journalNumber" [(ngModel)]="journalDraft.journalNumber" maxlength="64" [disabled]="journalCreateLocked()" required /></label>
            <label>Entry type <select name="journalPurpose" [(ngModel)]="journalDraft.postingPurpose" [disabled]="journalCreateLocked()">
              <option value="MANUAL">Manual adjustment</option>
              <option value="OPENING_BALANCE">Opening balance</option>
              <option value="PARTNER_DRAWING">Partner drawing</option>
            </select></label>
            <label>Source record reference <input name="sourceReference" [(ngModel)]="journalDraft.sourceKey" maxlength="200" [disabled]="journalCreateLocked()" required />
              <small>Use a unique reference from the supporting schedule or authorization.</small></label>
            <label>Supporting document <input #journalEvidenceInput name="journalEvidence" type="file" [disabled]="journalCreateLocked()" [required]="requiresJournalEvidence()" (change)="setJournalEvidence($event)" />
              <small>Required for opening balances and Partner drawings; maximum 5 MB. The exact file is retained with the journal.</small></label>
            @if (journalDraft.supportingEvidence) { <p role="status">Attached: {{ journalDraft.supportingEvidence.name }} ({{ journalDraft.supportingEvidence.size }} bytes)</p> }
            @if (evidenceMessage()) { <p role="alert">{{ evidenceMessage() }}</p> }
            <label>Functional currency <input name="journalCurrency" [(ngModel)]="journalDraft.currency" maxlength="3" [disabled]="journalCreateLocked()" required /></label>
            <fieldset class="journal-lines">
              <legend>Journal lines</legend>
              @for (line of journalDraft.lines; track $index; let row = $index) {
                <div class="workspace-grid">
                  <label>Line {{ row + 1 }} account <select [name]="'journalAccount' + row" [attr.name]="'journalAccount' + row" [(ngModel)]="line.firmAccountId" [disabled]="journalCreateLocked()" required>
                    <option value="">Select account</option>
                    @for (a of l.accounts; track a.id) { @if (a.postingAllowed) { <option [value]="a.id">{{ a.code }} · {{ a.name }} ({{ a.accountType }})</option> } }
                  </select></label>
                  <label>Description <input [name]="'journalDescription' + row" [attr.name]="'journalDescription' + row" [(ngModel)]="line.description" maxlength="300" [disabled]="journalCreateLocked()" required /></label>
                  <label>Debit <input [name]="'journalDebit' + row" [attr.name]="'journalDebit' + row" inputmode="decimal" [(ngModel)]="line.debit" [disabled]="journalCreateLocked()" /></label>
                  <label>Credit <input [name]="'journalCredit' + row" [attr.name]="'journalCredit' + row" inputmode="decimal" [(ngModel)]="line.credit" [disabled]="journalCreateLocked()" /></label>
                  <button matButton="outlined" type="button" (click)="removeJournalLine(row)" [disabled]="journalCreateLocked() || journalDraft.lines.length <= 2">Remove line</button>
                </div>
              }
              <button matButton="outlined" type="button" (click)="addJournalLine()" [disabled]="journalCreateLocked() || journalDraft.lines.length >= 200">Add journal line</button>
            </fieldset>
            <button matButton="filled" type="submit" [disabled]="journalCreateLocked() || !journalDraft.periodId || !journalDraft.journalNumber.trim() || !journalDraft.sourceKey.trim() || !journalEvidenceReady() || !journalLinesReady()">Save draft journal</button>
          </form>
          @if (pendingJournal(); as pending) {
            <section class="panel" aria-labelledby="journal-create-recovery-heading">
              <h3 id="journal-create-recovery-heading">Verify the saved journal</h3>
              <p>The save response was not confirmed. Check the exact source reference and journal contents before any retry.</p>
              @if (journalCreateMessage()) { <p [attr.role]="journalCreateState() === 'conflict' || journalCreateState() === 'error' ? 'alert' : 'status'">{{ journalCreateMessage() }}</p> }
              @switch (journalCreateState()) {
                @case ('checking') { <p role="status">Checking persisted journal state…</p> }
                @case ('saved') { <button matButton="filled" type="button" (click)="acknowledgeSavedJournal()">Acknowledge saved journal</button> }
                @case ('absent') {
                  <p role="status">No journal with this exact source reference is present. You can deliberately retry the unchanged request.</p>
                  <button matButton="filled" type="button" (click)="retryExactJournal()" [disabled]="journalCommand.busy()">Retry exact journal</button>
                  <button matButton="outlined" type="button" (click)="discardUnresolvedJournal()">Discard unresolved journal</button>
                }
                @case ('conflict') { <button matButton="outlined" type="button" (click)="discardUnresolvedJournal()">Discard and refresh</button> }
                @case ('error') { <button matButton="outlined" type="button" (click)="verifyJournal()">Retry state check</button> }
                @default { <button matButton="outlined" type="button" (click)="verifyJournal()" [disabled]="journalCommand.busy()">Check saved journal</button> }
              }
            </section>
          }
        }
        @if (pendingJournalAction(); as action) {
          <section class="panel" aria-labelledby="journal-action-recovery-heading">
            <h3 id="journal-action-recovery-heading">Verify the journal action</h3>
            <p>The {{ action.action }} response was not confirmed. Refresh the exact journal and reconcile its persisted status before another command.</p>
            @if (journalActionMessage()) { <p [attr.role]="journalActionState() === 'changed' || journalActionState() === 'error' ? 'alert' : 'status'">{{ journalActionMessage() }}</p> }
            @switch (journalActionState()) {
              @case ('checking') { <p role="status">Checking saved journal status…</p> }
              @case ('saved') { <button matButton="filled" type="button" (click)="acknowledgeJournalAction()">Acknowledge saved action</button> }
              @case ('unchanged') {
                <p role="status">The journal is still {{ action.previousStatus }}. Deliberately retry the same action or discard this attempt.</p>
                <button matButton="filled" type="button" (click)="retryJournalAction()" [disabled]="journalActionCommand.busy()">Retry exact action</button>
                <button matButton="outlined" type="button" (click)="discardJournalAction()">Discard unresolved action</button>
              }
              @case ('changed') { <button matButton="outlined" type="button" (click)="discardJournalAction()">Discard and refresh</button> }
              @case ('error') { <button matButton="outlined" type="button" (click)="verifyJournalAction()">Retry state check</button> }
              @default { <button matButton="outlined" type="button" (click)="verifyJournalAction()" [disabled]="journalActionCommand.busy()">Check saved status</button> }
            }
          </section>
        }
        <div class="table-scroll"><table>
          <caption>Recent firm journal drafts and their review status</caption>
          <thead><tr><th scope="col">Journal</th><th scope="col">Period</th><th scope="col">Source / type</th><th scope="col">Currency</th><th scope="col">Status</th><th scope="col">Lines</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>@for (j of l.journals; track j.id) {
            <tr><th scope="row">{{ j.journalNumber }}</th><td>{{ j.periodCode }}</td><td>{{ j.sourceKind }} · {{ j.postingPurpose }}<small>{{ j.sourceKey }}</small>
              @if (j.supportingEvidenceSha256) { <button matButton type="button" (click)="downloadJournalEvidence(j.id, j.supportingEvidenceSha256!)">Download source: {{ j.supportingEvidenceFileName }}</button> }
            </td>
              <td>{{ j.currency }}</td><td><audit-status [value]="j.status" /></td><td>{{ j.lines.length }}</td>
              <td class="actions">
                @if (j.status === 'DRAFT' && l.canCreateJournals) { <button matButton (click)="actOnJournal(j.id, 'submit')" [disabled]="journalActionLocked()">Submit for review</button> }
                @if (j.status === 'REVIEW_REQUIRED' && l.canReviewJournals && !journalPreparedByCurrentUser(j.createdByUserId)) { <button matButton="filled" (click)="actOnJournal(j.id, 'review')" [disabled]="journalActionLocked()">Review and approve</button> }
                @if (j.status === 'REVIEW_REQUIRED' && l.canReviewJournals && journalPreparedByCurrentUser(j.createdByUserId)) { <span>Prepared by you; another reviewer must approve</span> }
                @if (j.status === 'APPROVED' && l.canPostJournals) { <button matButton="filled" (click)="actOnJournal(j.id, 'post')" [disabled]="journalActionLocked()">Post</button> }
              </td></tr>
            <tr><td colspan="7"><details><summary>Review {{ j.journalNumber }} lines</summary>
              <div class="table-scroll"><table><caption>Lines for {{ j.journalNumber }}</caption><thead><tr><th scope="col">Account</th><th scope="col">Description</th><th scope="col" class="number">Debit</th><th scope="col" class="number">Credit</th></tr></thead>
                <tbody>@for (line of j.lines; track $index) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td class="number">{{ line.debit }}</td><td class="number">{{ line.credit }}</td></tr> }</tbody></table></div>
              <p>Prepared {{ j.createdAt.slice(0, 16).replace('T', ' ') }} UTC · {{ j.approvedByUserId ? 'Approved by ' + j.approvedByUserId : 'Not approved' }}{{ j.postedAt ? ' · Posted ' + j.postedAt.slice(0, 16).replace('T', ' ') + ' UTC' : '' }}</p>
            </details></td></tr>
          } @empty { <tr><td colspan="7">No manual or system journals are recorded in the recent firm ledger.</td></tr> }</tbody>
        </table></div>
        @if (journalCommand.message()) { <audit-command-message [message]="journalCommand.message()" [failed]="journalCommand.failed()" /> }
        @if (journalActionCommand.message()) { <audit-command-message [message]="journalActionCommand.message()" [failed]="journalActionCommand.failed()" /> }
        @if (evidenceDownloadMessage()) { <p role="status">{{ evidenceDownloadMessage() }}</p> }
      </section>
      <section class="panel" aria-labelledby="postings-heading">
        <h2 id="postings-heading">Recent firm postings</h2>
        <div class="table-scroll"><table><thead><tr><th>Posting ID</th><th>Posted date</th><th>Currency</th><th>Posted by</th><th>Reversal / status</th><th><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>@for (p of l.postings; track p.id) { <tr><td><code>{{ p.id }}</code></td><td>{{ p.postedAt.slice(0, 16).replace('T', ' ') }} UTC</td><td>{{ p.currency }}</td><td><code>{{ p.postedByUserId }}</code></td>
            <td>@if (p.reversalOfPostingId) { Reversal of <code>{{ p.reversalOfPostingId }}</code> } @else if (isPostingReversed(p.id, l.postings)) { Reversed } @else { Active }</td>
            <td>@if (!p.reversalOfPostingId && !isPostingReversed(p.id, l.postings) && l.canReversePostings) { <button matButton="outlined" (click)="startReversal(p.id, p.periodId)" [disabled]="cmd.busy()">Reverse</button> }</td></tr> }
          @empty { <tr><td colspan="6">No immutable postings recorded in the firm ledger yet.</td></tr> }</tbody></table></div>
        @if (reversingPosting(); as id) {
          <div class="panel"><h3>Reverse posting</h3><p>Reversing a posting records an offsetting entry in an open fiscal period (§41.5). The original posting remains immutable in history.</p>
            <label>Target open period <select name="reversalPeriod" [(ngModel)]="reversalPeriodId" [disabled]="cmd.busy()" required>
              <option value="">Select an open period</option>
              @for (p of l.periods; track p.id) { @if (p.status === 'OPEN') { <option [value]="p.id">{{ p.periodCode }}</option> } }
            </select></label>
            <label>Reversal reason <input name="reversalReason" [(ngModel)]="reversalReason" required maxlength="500" [disabled]="cmd.busy()" /></label>
            <p class="actions"><button matButton="filled" (click)="confirmReversal(id)" [disabled]="cmd.busy() || !reversalPeriodId || !reversalReason.trim()">Confirm reversal</button><button matButton (click)="cancelReversal()" [disabled]="cmd.busy()">Cancel</button></p></div>
        }
      </section>
    }
    @if (!cmd.uncertain() && cmd.message()) { <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" /> }
  `,
})
export class FirmLedger {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  @ViewChild('journalEvidenceInput') private journalEvidenceInput?: ElementRef<HTMLInputElement>;
  readonly ledger = this.api.resource(() => '/api/ui/finance', decodeLedger, 'Sign in with an authorized finance identity to access the firm ledger.');
  readonly cmd = new CommandState(this.api);
  readonly setupCommand = new CommandState(this.api);
  readonly journalCommand = new CommandState(this.api);
  readonly journalActionCommand = new CommandState(this.api);
  readonly pendingSetup = signal<FirmLedgerSetupRequest | null>(null);
  readonly setupResolution = signal<FirmLedgerSetupResolution>(null);
  readonly setupVerificationMessage = signal('');
  readonly accountDraft = { code: '', name: '', accountType: 'ASSET', normalSide: 'DEBIT', postingAllowed: true };
  readonly periodDraft = { periodCode: '' };
  journalDraft: JournalDraft = this.emptyJournalDraft();
  readonly pendingJournal = signal<JournalDraft | null>(null);
  readonly journalCreateState = signal<JournalCreateState>(null);
  readonly journalCreateMessage = signal('');
  readonly pendingJournalAction = signal<PendingJournalAction | null>(null);
  readonly journalActionState = signal<JournalActionState>(null);
  readonly journalActionMessage = signal('');
  readonly evidenceMessage = signal('');
  readonly evidenceDownloadMessage = signal('');
  readonly closing = signal<string | null>(null);
  readonly closeVerification = signal<CloseVerification>('idle');
  readonly verifiedRevision = signal<number | null>(null);
  private readonly pendingClose = signal<PendingClose | null>(null);
  reason = 'Standard accounting period close';
  readonly reopening = signal<string | null>(null);
  reopenReason = 'Correction required for fiscal period';
  readonly reversingPosting = signal<string | null>(null);
  reversalPeriodId = '';
  reversalReason = 'Correction of posted transaction';

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
        this.clearJournalDraft();
        this.pendingJournal.set(null);
        this.journalCreateState.set(null);
        this.journalCreateMessage.set('');
        this.journalCommand.uncertain.set(false);
        this.journalCommand.failed.set(false);
        this.pendingJournalAction.set(null);
        this.journalActionState.set(null);
        this.journalActionMessage.set('');
        this.journalActionCommand.uncertain.set(false);
        this.journalActionCommand.failed.set(false);
        this.reopening.set(null);
        this.reversingPosting.set(null);
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

  private emptyJournalDraft(): JournalDraft {
    return { periodId: '', journalNumber: '', sourceKey: '', postingPurpose: 'MANUAL', currency: '',
      lines: [this.emptyJournalLine(), this.emptyJournalLine()], supportingEvidence: null, supportingEvidenceSha256: null };
  }

  private clearJournalDraft(): void {
    this.journalDraft = this.emptyJournalDraft();
    if (this.journalEvidenceInput) this.journalEvidenceInput.nativeElement.value = '';
    this.evidenceMessage.set('');
  }

  requiresJournalEvidence(): boolean {
    return this.journalDraft.postingPurpose === 'OPENING_BALANCE' || this.journalDraft.postingPurpose === 'PARTNER_DRAWING';
  }

  journalEvidenceReady(): boolean {
    return !this.requiresJournalEvidence() || !!this.journalDraft.supportingEvidence &&
      this.journalDraft.supportingEvidence.size > 0 && this.journalDraft.supportingEvidence.size <= 5 * 1024 * 1024;
  }

  setJournalEvidence(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    if (file && (file.size < 1 || file.size > 5 * 1024 * 1024 || file.name.trim().length > 255)) {
      this.journalDraft.supportingEvidence = null;
      this.journalDraft.supportingEvidenceSha256 = null;
      input.value = '';
      this.evidenceMessage.set('Choose a document with a file name up to 255 characters and a size from 1 byte to 5 MB.');
      return;
    }
    this.journalDraft.supportingEvidence = file;
    this.journalDraft.supportingEvidenceSha256 = null;
    this.evidenceMessage.set('');
  }

  async downloadJournalEvidence(id: string, sha256: string): Promise<void> {
    this.evidenceDownloadMessage.set('');
    const result = await this.api.download(`/api/ui/finance/journals/${id}/evidence`, {}, metadata =>
      metadata.headers['x-firm-journal-evidence-sha256'] === sha256 && metadata.byteCount > 0);
    this.evidenceDownloadMessage.set(result.ok ? `Supporting document downloaded: ${result.value.fileName}.` : result.message);
  }

  private emptyJournalLine(): JournalDraftLine {
    return { firmAccountId: '', description: '', debit: '0', credit: '0' };
  }

  journalLinesReady(): boolean {
    if (this.journalDraft.lines.length < 2 || this.journalDraft.lines.length > 200) return false;
    return this.journalDraft.lines.every(line => {
      const debit = canonicalDecimal(line.debit.trim() || '0');
      const credit = canonicalDecimal(line.credit.trim() || '0');
      return !!line.firmAccountId && !!line.description.trim() && debit !== null && credit !== null &&
        (debit !== '0' || credit !== '0') && !(debit !== '0' && credit !== '0');
    });
  }

  journalCreateLocked(): boolean {
    return this.journalCommand.busy() || this.journalCommand.uncertain() || this.pendingJournal() !== null ||
      this.pendingJournalAction() !== null || !this.ledger.data()?.canCreateJournals;
  }

  journalActionLocked(): boolean {
    return this.journalActionCommand.busy() || this.journalActionCommand.uncertain() ||
      this.pendingJournalAction() !== null || this.pendingJournal() !== null;
  }

  journalPreparedByCurrentUser(userId: string): boolean {
    return this.session.current()?.userId === userId;
  }

  addJournalLine(): void {
    if (!this.journalCreateLocked() && this.journalDraft.lines.length < 200)
      this.journalDraft.lines.push(this.emptyJournalLine());
  }

  removeJournalLine(index: number): void {
    if (!this.journalCreateLocked() && this.journalDraft.lines.length > 2)
      this.journalDraft.lines.splice(index, 1);
  }

  async createJournal(): Promise<void> {
    if (this.journalCreateLocked() || !this.journalLinesReady()) return;
    const request: JournalDraft = {
      periodId: this.journalDraft.periodId,
      journalNumber: this.journalDraft.journalNumber.trim(),
      sourceKey: this.journalDraft.sourceKey.trim(),
      postingPurpose: this.journalDraft.postingPurpose,
      currency: this.journalDraft.currency.trim().toUpperCase(),
      lines: this.journalDraft.lines.map(line => ({ ...line, description: line.description.trim(),
        debit: line.debit.trim() || '0', credit: line.credit.trim() || '0' })),
      supportingEvidence: this.journalDraft.supportingEvidence,
      supportingEvidenceSha256: this.journalDraft.supportingEvidenceSha256,
    };
    if (request.supportingEvidence) {
      try {
        const digest = await crypto.subtle.digest('SHA-256', await request.supportingEvidence.arrayBuffer());
        request.supportingEvidenceSha256 = Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, '0')).join('');
      } catch {
        this.evidenceMessage.set('The supporting document could not be verified by this browser. Choose it again before saving.');
        return;
      }
    }
    this.pendingJournal.set(request);
    this.journalCreateState.set(null);
    this.journalCreateMessage.set('');
    await this.sendJournal(request);
  }

  private async sendJournal(request: JournalDraft, retry = false): Promise<void> {
    if (this.journalCommand.busy() || !this.ledger.data()?.canCreateJournals) return;
    if (!retry && this.pendingJournal() !== request) return;
    const form = new FormData();
    form.append('journal', JSON.stringify({ periodId: request.periodId, journalNumber: request.journalNumber,
      sourceKey: request.sourceKey, postingPurpose: request.postingPurpose, currency: request.currency, lines: request.lines }));
    if (request.supportingEvidence) form.append('evidence', request.supportingEvidence, request.supportingEvidence.name);
    const saved = await this.journalCommand.run('/api/ui/finance/journals', form,
      'Draft journal saved.', () => {
        this.pendingJournal.set(null);
        this.journalCreateState.set(null);
        this.journalCreateMessage.set('');
        this.clearJournalDraft();
        this.ledger.reload();
      });
    if (!saved && !this.journalCommand.uncertain()) this.pendingJournal.set(null);
  }

  async verifyJournal(): Promise<void> {
    const pending = this.pendingJournal();
    if (!pending || this.journalCreateState() === 'checking') return;
    this.journalCreateState.set('checking');
    this.journalCreateMessage.set('');
    try {
      const saved = await this.api.get('/api/ui/finance', decodeLedger);
      const sameSource = saved.journals.find(j => j.sourceKind === 'MANUAL' &&
        j.sourceKey === pending.sourceKey && j.postingPurpose === pending.postingPurpose);
      const sameNumber = saved.journals.find(j => j.journalNumber === pending.journalNumber);
      if (!sameSource) {
        this.journalCreateState.set(sameNumber ? 'conflict' : 'absent');
        this.journalCreateMessage.set(sameNumber
          ? 'The journal number is already assigned to a different source.'
          : 'No journal with this exact source reference is present in persisted firm ledger state.');
      } else if (journalMatchesDraft(sameSource, pending)) {
        this.journalCreateState.set('saved');
        this.journalCreateMessage.set(`Persisted state confirms journal ${sameSource.journalNumber} is ${sameSource.status}.`);
      } else {
        this.journalCreateState.set('conflict');
        this.journalCreateMessage.set('This source reference is bound to a different journal definition. Do not retry with changed content.');
      }
      this.ledger.reload();
    } catch {
      this.journalCreateState.set('error');
      this.journalCreateMessage.set('Persisted journal state could not be verified. Keep the request unresolved and retry the state check.');
    }
  }

  async retryExactJournal(): Promise<void> {
    const pending = this.pendingJournal();
    if (!pending || this.journalCreateState() !== 'absent' || this.journalCommand.busy()) return;
    this.journalCommand.uncertain.set(false);
    this.journalCommand.failed.set(false);
    this.journalCreateState.set(null);
    this.journalCreateMessage.set('');
    await this.sendJournal(pending, true);
  }

  acknowledgeSavedJournal(): void {
    if (this.journalCreateState() !== 'saved') return;
    this.clearUnresolvedJournal();
    this.journalCommand.message.set('Persisted firm ledger state confirms the journal request. It was not repeated.');
    this.clearJournalDraft();
    this.ledger.reload();
  }

  discardUnresolvedJournal(): void {
    if (!['absent', 'conflict'].includes(this.journalCreateState() ?? '')) return;
    this.clearUnresolvedJournal();
    this.journalCommand.message.set('The unresolved journal request was discarded. Refresh and review the source before preparing another entry.');
    this.ledger.reload();
  }

  private clearUnresolvedJournal(): void {
    this.journalCommand.uncertain.set(false);
    this.journalCommand.failed.set(false);
    this.pendingJournal.set(null);
    this.journalCreateState.set(null);
    this.journalCreateMessage.set('');
  }

  async actOnJournal(id: string, action: JournalActionKind, retry = false): Promise<void> {
    if (this.journalActionCommand.busy()) return;
    const journal = this.ledger.data()?.journals.find(x => x.id === id);
    if (!journal) return;
    const states = { submit: ['DRAFT', 'REVIEW_REQUIRED'], review: ['REVIEW_REQUIRED', 'APPROVED'], post: ['APPROVED', 'POSTED'] } as const;
    const [previousStatus, nextStatus] = states[action];
    if (!retry) {
      if (this.journalActionLocked() || journal.status !== previousStatus) return;
      this.pendingJournalAction.set({ id, action, previousStatus, nextStatus });
      this.journalActionState.set(null);
      this.journalActionMessage.set('');
    }
    const endpoint = action === 'submit' ? 'submit' : action === 'review' ? 'review' : 'post';
    const body = action === 'submit' ? {} : { confirmed: true };
    const success = await this.journalActionCommand.run(`/api/ui/finance/journals/${id}/${endpoint}`, body,
      action === 'submit' ? 'Journal submitted for independent review.' : action === 'review' ? 'Journal approved.' : 'Journal posted to the firm ledger.', () => {
        this.pendingJournalAction.set(null);
        this.journalActionState.set(null);
        this.journalActionMessage.set('');
        this.ledger.reload();
      });
    if (!success && !this.journalActionCommand.uncertain()) this.pendingJournalAction.set(null);
  }

  async verifyJournalAction(): Promise<void> {
    const pending = this.pendingJournalAction();
    if (!pending || this.journalActionState() === 'checking') return;
    this.journalActionState.set('checking');
    this.journalActionMessage.set('');
    try {
      const saved = await this.api.get('/api/ui/finance', decodeLedger);
      const journal = saved.journals.find(x => x.id === pending.id);
      if (!journal) this.journalActionState.set('changed');
      else if (journal.status === pending.nextStatus) {
        this.journalActionState.set('saved');
        this.journalActionMessage.set(`Persisted state confirms the journal is ${journal.status}.`);
      } else if (journal.status === pending.previousStatus) {
        this.journalActionState.set('unchanged');
        this.journalActionMessage.set(`Persisted state confirms the journal remains ${journal.status}.`);
      } else {
        this.journalActionState.set('changed');
        this.journalActionMessage.set(`The journal changed to ${journal.status}. Review its current state before continuing.`);
      }
      this.ledger.reload();
    } catch {
      this.journalActionState.set('error');
      this.journalActionMessage.set('Persisted journal state could not be verified. Keep this action unresolved and retry the state check.');
    }
  }

  async retryJournalAction(): Promise<void> {
    const pending = this.pendingJournalAction();
    if (!pending || this.journalActionState() !== 'unchanged' || this.journalActionCommand.busy()) return;
    this.journalActionCommand.uncertain.set(false);
    this.journalActionCommand.failed.set(false);
    this.journalActionState.set(null);
    await this.actOnJournal(pending.id, pending.action, true);
  }

  acknowledgeJournalAction(): void {
    if (this.journalActionState() !== 'saved') return;
    this.clearUnresolvedJournalAction();
    this.journalActionCommand.message.set('Persisted journal state confirms the action. It was not repeated.');
    this.ledger.reload();
  }

  discardJournalAction(): void {
    if (!['unchanged', 'changed'].includes(this.journalActionState() ?? '')) return;
    this.clearUnresolvedJournalAction();
    this.journalActionCommand.message.set('The unresolved action was discarded. Refresh and review the journal before continuing.');
    this.ledger.reload();
  }

  private clearUnresolvedJournalAction(): void {
    this.journalActionCommand.uncertain.set(false);
    this.journalActionCommand.failed.set(false);
    this.pendingJournalAction.set(null);
    this.journalActionState.set(null);
    this.journalActionMessage.set('');
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

  async reopen(id: string): Promise<void> {
    const succeeded = await this.cmd.run(`/api/ui/finance/periods/${id}/reopen`, { reason: this.reopenReason.trim() }, 'Fiscal period reopened.', () => {
      this.reopening.set(null);
      this.ledger.reload();
    });
    if (succeeded) {
      this.ledger.reload();
    }
  }

  isPostingReversed(postingId: string, postings: readonly DecodedFirmPosting[]): boolean {
    return postings.some(p => p.reversalOfPostingId === postingId);
  }

  startReversal(postingId: string, periodId: string | null): void {
    this.reversingPosting.set(postingId);
    const openPeriod = this.ledger.data()?.periods.find(p => p.status === 'OPEN' && (!periodId || p.id === periodId))
      ?? this.ledger.data()?.periods.find(p => p.status === 'OPEN');
    this.reversalPeriodId = openPeriod?.id ?? '';
    this.reversalReason = 'Correction of posted transaction';
  }

  cancelReversal(): void {
    this.reversingPosting.set(null);
    this.reversalPeriodId = '';
    this.reversalReason = 'Correction of posted transaction';
  }

  async confirmReversal(postingId: string): Promise<void> {
    if (!this.reversalPeriodId || !this.reversalReason.trim() || this.cmd.busy()) return;
    const succeeded = await this.cmd.run(`/api/ui/finance/postings/${postingId}/reverse`,
      { periodId: this.reversalPeriodId, reason: this.reversalReason.trim() },
      'Posting reversed with an offsetting entry.', () => {
        this.cancelReversal();
        this.ledger.reload();
      });
    if (succeeded) {
      this.ledger.reload();
    }
  }

  private clearUnknownClose(): void {
    this.cmd.uncertain.set(false);
    this.cmd.failed.set(false);
    this.cmd.message.set('');
  }
}

type DecodedFirmJournal = ReturnType<typeof decodeLedger>['journals'][number];
type DecodedFirmPosting = ReturnType<typeof decodeLedger>['postings'][number];

function canonicalDecimal(value: string): string | null {
  const match = /^(\d{1,14})(?:\.(\d{1,6}))?$/.exec(value.trim());
  if (!match) return null;
  const whole = BigInt(match[1]).toString();
  const fraction = (match[2] ?? '').replace(/0+$/, '');
  return fraction ? `${whole}.${fraction}` : whole;
}

function journalMatchesDraft(journal: DecodedFirmJournal, draft: JournalDraft): boolean {
  if (journal.periodId !== draft.periodId || journal.journalNumber !== draft.journalNumber ||
      journal.currency !== draft.currency.toUpperCase() || journal.postingPurpose !== draft.postingPurpose ||
      journal.supportingEvidenceSha256 !== draft.supportingEvidenceSha256 ||
      journal.lines.length !== draft.lines.length) return false;
  const fingerprint = (lines: readonly { firmAccountId: string; description: string; debit: string; credit: string }[]) =>
    lines.map(line => {
      const debit = canonicalDecimal(line.debit);
      const credit = canonicalDecimal(line.credit);
      return debit === null || credit === null ? null :
        `${line.firmAccountId}\u0000${line.description.trim()}\u0000${debit}\u0000${credit}`;
    }).sort();
  const persisted = fingerprint(journal.lines);
  const proposed = fingerprint(draft.lines);
  return !persisted.includes(null) && !proposed.includes(null) && JSON.stringify(persisted) === JSON.stringify(proposed);
}
