import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';

interface OpenItem {
  kind: 'SALES_INVOICE' | 'SALES_CREDIT' | 'PURCHASE_INVOICE' | 'PURCHASE_CREDIT' | 'SALES_RECEIPT' | 'SUPPLIER_PAYMENT' | 'OPENING_AR_INVOICE' | 'OPENING_AP_INVOICE'; openItemId: string; sourceDocumentId: string;
  counterpartyId: string; counterpartyName: string; currency: string; originalAmount: string; appliedAmount: string;
  openAmount: string; dueDate: string | null; asOfDate: string; status: string;
}
interface TargetLine { lineNumber: number; targetKind: 'SALES_INVOICE' | 'PURCHASE_INVOICE' | 'OPENING_AR_INVOICE' | 'OPENING_AP_INVOICE'; targetOpenItemId: string; amount: string; reversesAllocationLineId: string | null }
interface Preview { submissionId: string; digest: string; disposition: 'ALLOCATE' | 'UNALLOCATE'; sourceKind: 'SALES_CREDIT' | 'PURCHASE_CREDIT' | 'SALES_RECEIPT' | 'SUPPLIER_PAYMENT';
  sourceItemId: string; counterpartyId: string; currency: string; sourceOriginalAmount: string; sourceAvailableAmount: string;
  lines: TargetLine[]; targetAvailableAmounts: string[] }
interface AllocationIntent { commandId: string; context: string; sourceKind: Preview['sourceKind']; sourceItemId: string; counterpartyId: string; currency: string;
  disposition: 'ALLOCATE' | 'UNALLOCATE'; targetKind: TargetLine['targetKind']; targetOpenItemId: string; amount: string; reference: string; reason: string; reversesAllocationLineId: string | null }
interface AllocationHistoryLine { allocationLineId: string; submissionId: string; sourceKind: Preview['sourceKind']; sourceItemId: string;
  targetKind: TargetLine['targetKind']; targetOpenItemId: string; counterpartyId: string; counterpartyName: string; currency: string;
  originalAmount: string; reversedAmount: string; reversibleAmount: string; reference: string; reason: string; manifestHash: string;
  createdByUserId: string; approvedByUserId: string; approvedAt: string }
interface AllocationHistoryPage { clientId: string; page: number; pageSize: number; total: number; bookkeepingActive: boolean; lines: AllocationHistoryLine[] }
type PendingPreview = Preview;
type AllocationRecoveryKind = 'SUBMIT' | 'REVIEW';
interface AllocationRecoveryRef { kind: AllocationRecoveryKind; commandId: string; clientId: string; actorUserId: string; submissionId: string | null; previewDigest: string; fingerprint: string }
interface AllocationCommandReceipt { kind: AllocationRecoveryKind; clientId: string; actorUserId: string; commandId: string; submissionId: string; decisionId: string | null;
  outcome: string; previewDigest: string; intentHash: string; sourceKind: string; sourceItemId: string; disposition: string; reference: string; reason: string;
  lines: TargetLine[]; decision: string | null; decisionReason: string | null }
interface AllocationCommandLookup { found: boolean; receipt: AllocationCommandReceipt | null }
interface SettlementOptions { periods: { id: string; code: string; startDate: string; endDate: string; currency: string }[]; counterparties: { id: string; role: string; displayName: string }[]; cashAccounts: { accountCode: string; accountName: string }[] }
interface SettlementPreview { digest: string; intentHash: string; sourceKind: string; amount: string; currency: string; controlAccountCode: string; cashAccountCode: string; reference: string; evidenceReference: string }
interface SettlementRecoveryRef { commandId: string; clientId: string; actorUserId: string; previewDigest: string; intentHash: string; journalId?: string }
interface SettlementLookup { found: boolean; clientId: string; actorUserId: string; commandId: string; intentHash: string | null;
  receipt: { commandId: string; journalId: string; outcome: string } | null }
interface ReconciliationRow { role: string; accountId: string; accountCode: string; accountName: string; ledgerBalance: string; openItemBalance: string; difference: string; openItemCount: number; status: string }
interface Reconciliation { clientId: string; periodId: string; periodCode: string; currency: string; asOfDate: string; ledgerBasis: string; allocationBasis: string; openingDetailStatus: string; unlinkedOpenItemCount: number; accounts: ReconciliationRow[]; reconciled: boolean }
const decimal = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;
const allocationSources = ['SALES_CREDIT', 'PURCHASE_CREDIT', 'SALES_RECEIPT', 'SUPPLIER_PAYMENT'];
const allocationTargets = ['SALES_INVOICE', 'PURCHASE_INVOICE', 'OPENING_AR_INVOICE', 'OPENING_AP_INVOICE'];
const allocationDigest = /^[0-9a-f]{64}$/;

function records(value: unknown): Record<string, unknown>[] {
  if (!Array.isArray(value) || value.length > 1000) throw new Error('Invalid open-item response');
  return value.map(x => { if (!x || typeof x !== 'object' || Array.isArray(x)) throw new Error('Invalid open-item row'); return x as Record<string, unknown>; });
}
function decodeBalances(value: unknown): OpenItem[] {
  return records(value).map(x => {
    const kinds = ['SALES_INVOICE', 'SALES_CREDIT', 'PURCHASE_INVOICE', 'PURCHASE_CREDIT', 'SALES_RECEIPT', 'SUPPLIER_PAYMENT', 'OPENING_AR_INVOICE', 'OPENING_AP_INVOICE'];
    if (!kinds.includes(String(x['kind'])) || ![x['openItemId'], x['sourceDocumentId'], x['counterpartyId']].every(v => typeof v === 'string' && guidPattern.test(v)) ||
      typeof x['counterpartyName'] !== 'string' || typeof x['currency'] !== 'string' || !/^[A-Z]{3}$/.test(x['currency']) ||
      !['originalAmount', 'appliedAmount', 'openAmount'].every(k => typeof x[k] === 'string' && decimal.test(x[k])) ||
      typeof x['asOfDate'] !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(x['asOfDate']) || typeof x['status'] !== 'string' ||
      !(x['dueDate'] === null || (typeof x['dueDate'] === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(x['dueDate'])))) throw new Error('Invalid scoped open-item data');
    return x as unknown as OpenItem;
  });
}
function decodeAllocationPreview(value: unknown, requireSubmission: boolean): Preview {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid allocation manifest');
  const x = value as Record<string, unknown>;
  const submissionId = x['submissionId'];
  const lines = records(x['lines']).map(value => {
    const line = value as Record<string, unknown>;
    if (!Number.isInteger(line['lineNumber']) || Number(line['lineNumber']) < 1 ||
      !allocationTargets.includes(String(line['targetKind'])) || typeof line['targetOpenItemId'] !== 'string' || !guidPattern.test(line['targetOpenItemId']) ||
      typeof line['amount'] !== 'string' || !decimal.test(line['amount']) || BigInt(micros(line['amount'])) <= 0n ||
      !(line['reversesAllocationLineId'] === null || (typeof line['reversesAllocationLineId'] === 'string' && guidPattern.test(line['reversesAllocationLineId']))))
      throw new Error('Invalid allocation target line');
    return line as unknown as TargetLine;
  });
  const available = x['targetAvailableAmounts'];
  if ((requireSubmission && (typeof submissionId !== 'string' || !guidPattern.test(submissionId) || submissionId === '00000000-0000-0000-0000-000000000000')) ||
    (!requireSubmission && (typeof submissionId !== 'string' || !guidPattern.test(submissionId))) ||
    typeof x['digest'] !== 'string' || !allocationDigest.test(x['digest']) ||
    !['ALLOCATE', 'UNALLOCATE'].includes(String(x['disposition'])) || !allocationSources.includes(String(x['sourceKind'])) ||
    typeof x['sourceItemId'] !== 'string' || !guidPattern.test(x['sourceItemId']) || typeof x['counterpartyId'] !== 'string' || !guidPattern.test(x['counterpartyId']) ||
    typeof x['currency'] !== 'string' || !/^[A-Z]{3}$/.test(x['currency']) ||
    !['sourceOriginalAmount', 'sourceAvailableAmount'].every(k => typeof x[k] === 'string' && decimal.test(x[k])) ||
    lines.length < 1 || lines.length > 100 || new Set(lines.map(line => line.lineNumber)).size !== lines.length ||
    new Set(lines.map(line => `${line.targetKind}:${line.targetOpenItemId}`)).size !== lines.length ||
    !Array.isArray(available) || available.length !== lines.length || !available.every(v => typeof v === 'string' && decimal.test(v)))
    throw new Error('Invalid allocation manifest');
  return { submissionId: submissionId as string, digest: x['digest'] as string, disposition: x['disposition'] as Preview['disposition'],
    sourceKind: x['sourceKind'] as Preview['sourceKind'], sourceItemId: x['sourceItemId'] as string, counterpartyId: x['counterpartyId'] as string,
    currency: x['currency'] as string, sourceOriginalAmount: x['sourceOriginalAmount'] as string, sourceAvailableAmount: x['sourceAvailableAmount'] as string,
    lines, targetAvailableAmounts: available as string[] };
}
function micros(value: string): string {
  const [whole, fraction = ''] = value.split('.');
  return `${whole}${fraction.padEnd(6, '0')}`;
}
function normalizedAmount(value: string): string {
  const [whole, fraction = ''] = value.split('.');
  const exactFraction = fraction.replace(/0+$/, '');
  return exactFraction ? `${whole}.${exactFraction}` : whole;
}
function allocationTotal(lines: TargetLine[]): string {
  const total = lines.reduce((sum, line) => sum + BigInt(micros(line.amount)), 0n);
  const digits = total.toString().padStart(7, '0');
  return `${digits.slice(0, -6)}.${digits.slice(-6)}`;
}
function decodeAllocationHistory(value: unknown, client: string, page: number): AllocationHistoryPage {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid allocation history');
  const result = value as AllocationHistoryPage;
  if (result.clientId !== client || result.page !== page || result.pageSize !== 25 || !Number.isSafeInteger(result.total) || result.total < 0 ||
    typeof result.bookkeepingActive !== 'boolean' || !Array.isArray(result.lines) || result.lines.length > 25 || result.lines.length > result.total)
    throw new Error('Invalid scoped allocation history');
  const seen = new Set<string>();
  for (const line of result.lines) {
    if (!line || typeof line !== 'object' || ![line.allocationLineId, line.submissionId, line.sourceItemId, line.targetOpenItemId,
      line.counterpartyId, line.createdByUserId, line.approvedByUserId].every(id => typeof id === 'string' && guidPattern.test(id)) ||
      seen.has(line.allocationLineId) || line.createdByUserId === line.approvedByUserId || !allocationSources.includes(line.sourceKind) ||
      !allocationTargets.includes(line.targetKind) || typeof line.currency !== 'string' || !/^[A-Z]{3}$/.test(line.currency) ||
      ![line.originalAmount, line.reversedAmount, line.reversibleAmount].every(amount => typeof amount === 'string' && decimal.test(amount)) ||
      BigInt(micros(line.originalAmount)) <= 0n || BigInt(micros(line.originalAmount)) !== BigInt(micros(line.reversedAmount)) + BigInt(micros(line.reversibleAmount)) ||
      ![line.counterpartyName, line.reference, line.reason].every(text => typeof text === 'string' && !!text.trim()) ||
      typeof line.manifestHash !== 'string' || !allocationDigest.test(line.manifestHash) || typeof line.approvedAt !== 'string' || !Number.isFinite(Date.parse(line.approvedAt)))
      throw new Error('Invalid approved allocation line');
    seen.add(line.allocationLineId);
  }
  return result;
}
const allocationRecoveryStorageKey = 'auditsphere:open-item-allocation-recovery:v1';
const settlementRecoveryStorageKey = 'auditsphere:manual-settlement-recovery:v1';
// Canonical local match only: fixed property order, trimmed text, line-number order, and decimal strings
// normalized from at most six fractional places. This SHA-256 is not the server IntentHash; the latter
// remains opaque and is returned only as supporting receipt evidence.
async function recoveryFingerprint(value: unknown): Promise<string> {
  const digest = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(value)));
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}
async function submitFingerprint(clientId: string, actorUserId: string, body: Record<string, unknown>): Promise<string> {
  const lines = Array.isArray(body['lines']) ? (body['lines'] as Record<string, unknown>[]).map(line => ({
    lineNumber: line['lineNumber'], targetKind: line['targetKind'], targetOpenItemId: line['targetOpenItemId'],
    amount: typeof line['amount'] === 'string' && decimal.test(line['amount']) ? normalizedAmount(line['amount']) : line['amount'],
    reversesAllocationLineId: line['reversesAllocationLineId'] ?? null
  })).sort((a, b) => Number(a.lineNumber) - Number(b.lineNumber)) : [];
  return recoveryFingerprint({ kind: 'SUBMIT', clientId, actorUserId, commandId: body['commandId'], sourceKind: body['sourceKind'], sourceItemId: body['sourceItemId'],
    disposition: body['disposition'], reference: String(body['reference'] ?? '').trim(), reason: String(body['reason'] ?? '').trim(),
    previewDigest: body['previewDigest'], lines });
}
function reviewFingerprint(ref: Pick<AllocationRecoveryRef, 'clientId' | 'actorUserId' | 'commandId' | 'submissionId' | 'previewDigest'>,
  decision: string, reason: string): Promise<string> {
  return recoveryFingerprint({ kind: 'REVIEW', clientId: ref.clientId, actorUserId: ref.actorUserId, commandId: ref.commandId,
    submissionId: ref.submissionId, decision, reason: reason.trim(), previewDigest: ref.previewDigest });
}
function decodeAllocationCommandLookup(value: unknown): AllocationCommandLookup {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid allocation command lookup');
  const x = value as Record<string, unknown>;
  if (typeof x['found'] !== 'boolean') throw new Error('Invalid allocation command lookup');
  if (!x['found']) { if (x['receipt'] !== null) throw new Error('Invalid empty allocation command lookup'); return { found: false, receipt: null }; }
  const raw = x['receipt'];
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error('Invalid allocation command receipt');
  const r = raw as Record<string, unknown>;
  const lines = records(r['lines']).map(line => {
    if (!Number.isInteger(line['lineNumber']) || typeof line['targetKind'] !== 'string' || typeof line['targetOpenItemId'] !== 'string' ||
      !guidPattern.test(line['targetOpenItemId']) || typeof line['amount'] !== 'string' || !decimal.test(line['amount']) ||
      !(line['reversesAllocationLineId'] === null || (typeof line['reversesAllocationLineId'] === 'string' && guidPattern.test(line['reversesAllocationLineId']))))
      throw new Error('Invalid allocation command receipt line');
    return line as unknown as TargetLine;
  });
  if (!['SUBMIT', 'REVIEW'].includes(String(r['kind'])) || ![r['clientId'], r['actorUserId'], r['commandId'], r['submissionId']].every(id => typeof id === 'string' && guidPattern.test(id)) ||
    !(r['decisionId'] === null || (typeof r['decisionId'] === 'string' && guidPattern.test(r['decisionId']))) ||
    !['outcome', 'previewDigest', 'intentHash', 'sourceKind', 'disposition', 'reference', 'reason'].every(k => typeof r[k] === 'string') ||
    typeof r['sourceItemId'] !== 'string' || !guidPattern.test(r['sourceItemId']) || !allocationDigest.test(String(r['previewDigest'])) || !allocationDigest.test(String(r['intentHash'])) ||
    !(r['decision'] === null || typeof r['decision'] === 'string') || !(r['decisionReason'] === null || typeof r['decisionReason'] === 'string') || lines.length < 1 || lines.length > 100)
    throw new Error('Invalid allocation command receipt');
  return { found: true, receipt: { kind: r['kind'] as AllocationRecoveryKind, clientId: r['clientId'] as string, actorUserId: r['actorUserId'] as string,
    commandId: r['commandId'] as string, submissionId: r['submissionId'] as string, decisionId: r['decisionId'] as string | null,
    outcome: r['outcome'] as string, previewDigest: r['previewDigest'] as string, intentHash: r['intentHash'] as string, sourceKind: r['sourceKind'] as string,
    sourceItemId: r['sourceItemId'] as string, disposition: r['disposition'] as string, reference: r['reference'] as string, reason: r['reason'] as string,
    lines, decision: r['decision'] as string | null, decisionReason: r['decisionReason'] as string | null } };
}

@Component({ selector: 'audit-open-item-allocations', imports: [FormsModule, MatButtonModule], template: `
  <section aria-label="Client receivables, payables and allocations">
    <h3>Receivables, payables and credit allocations</h3>
    <p>Balances are derived from posted client invoices, credits, and reviewed settlements. Recording a settlement creates a client ledger journal; it does not initiate or verify a bank payment.</p>
    @if (visibleRecoveries().length) { <section aria-label="Unconfirmed allocation command recovery"><h4>Unconfirmed allocation commands</h4>
      <p>A timeout or an empty lookup does not prove that a delayed command cannot commit. Keep the original intent fenced until a matching retained result is found and acknowledged.</p>
      @for (ref of visibleRecoveries(); track ref.kind + ':' + ref.commandId) { <article>
        <p>{{ ref.kind === 'SUBMIT' ? 'Allocation submission' : 'Allocation review' }} · command <code>{{ ref.commandId }}</code> · preview <code>{{ ref.previewDigest }}</code></p>
        @if (recoveryMessage()[ref.kind + ':' + ref.commandId]; as message) { <p role="status">{{ message }}</p> }
        <button matButton type="button" [disabled]="busy()" (click)="recoverCommand(ref)">Check retained outcome</button>
        @if (ref.kind === 'SUBMIT' && recoveryNotFound()[ref.kind + ':' + ref.commandId] && retryableSubmit(ref)) { <button matButton type="button" [disabled]="busy()" (click)="retrySameSubmission(ref)">Explicitly retry the identical submission</button> }
        @if (recoveryMatched()[ref.kind + ':' + ref.commandId]) { <button matButton type="button" [disabled]="busy()" (click)="acknowledgeRecovery(ref)">Acknowledge confirmed outcome and clear fence</button> }
      </article> }
    </section> }
    <label>Ageing as of <input type="date" [(ngModel)]="asOfDate" name="asOfDate" /></label>
    <button matButton type="button" [disabled]="busy()" (click)="load()">Refresh balances</button>
    @if (reconciliation(); as recon) { <section aria-label="Receivables and payables control reconciliation"><h4>AR/AP control reconciliation · {{ recon.periodCode }} · {{ recon.asOfDate }}</h4>
      <p>Ledger basis: approved opening position and posted native client journals through the selected date. Current approved allocations are included because they have no separate effective date.
        @if (recon.openingDetailStatus === 'REVIEWED_OPENING_DETAIL_INCLUDED') { Reviewed opening invoice detail is included in ageing and control totals, and can be used as a target for independently reviewed settlements. }
        @else if (recon.openingDetailStatus === 'AGGREGATE_OPENING_DETAIL_NOT_INCLUDED') { Opening balances are aggregate-only; invoice ageing and settlement are incomplete at cutover. }
        @else if (recon.openingDetailStatus === 'NO_APPROVED_OPENING') { No approved cutover opening applies to this period. }
      </p>
      @if (recon.unlinkedOpenItemCount) { <p role="alert">{{ recon.unlinkedOpenItemCount }} open item(s) could not be linked to one approved control-account line.</p> }
      <div class="table-scroll"><table><caption>AR/AP control balances and open item totals</caption><thead><tr><th>Role</th><th>Control account</th><th>Ledger</th><th>Open items</th><th>Difference</th><th>Status</th></tr></thead><tbody>
        @for (row of recon.accounts; track row.accountId) { <tr><td>{{ row.role }}</td><td>{{ row.accountCode }} · {{ row.accountName }}</td><td>{{ row.ledgerBalance }} {{ recon.currency }}</td><td>{{ row.openItemBalance }} {{ recon.currency }} ({{ row.openItemCount }})</td><td>{{ row.difference }}</td><td>{{ row.status }}</td></tr> }
        @empty { <tr><td colspan="6">No approved AR/AP control-account activity is available.</td></tr> }
      </tbody></table></div><p role="status">{{ recon.reconciled ? 'AR/AP balances reconcile.' : 'AR/AP balances do not reconcile.' }}</p>
    </section> }
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (busy()) { <p role="status">Updating client balances…</p> }
    <div class="table-scroll"><table><caption>Open client items and as-of ageing</caption><thead><tr><th>Type</th><th>Party</th><th>Document</th><th>Due</th><th>Currency</th><th>Original</th><th>Applied</th><th>Open</th><th>Status</th></tr></thead><tbody>
      @for (row of balances(); track row.openItemId) { <tr><td>{{ row.kind.replaceAll('_', ' ') }}</td><td>{{ row.counterpartyName }}</td><td>{{ row.sourceDocumentId }}</td><td>{{ row.dueDate ?? '—' }}</td><td>{{ row.currency }}</td><td>{{ row.originalAmount }}</td><td>{{ row.appliedAmount }}</td><td>{{ row.openAmount }}</td><td>{{ row.status }}</td></tr> }
      @empty { <tr><td colspan="9">No posted open items are available for this client and date.</td></tr> }
    </tbody></table></div>
    <section aria-label="Record client settlement">
      <h4>Record a settlement already made</h4>
      <p>Use this for a receipt or supplier payment completed outside AuditSphere. The journal will follow independent submission, review, and posting.</p>
      @if (visibleSettlementRecoveries().length) { <section aria-label="Unconfirmed manual settlement draft">
        <p>A previous settlement request has an unknown outcome. Its original intent is locked until the originating preparer verifies and acknowledges the exact journal draft. A lookup miss does not prove the delayed request cannot commit.</p>
        @for (ref of visibleSettlementRecoveries(); track ref.commandId) { <article>
          <p>Command <code>{{ ref.commandId }}</code> · preview <code>{{ ref.previewDigest }}</code> · intent <code>{{ ref.intentHash }}</code></p>
          @if (settlementRecoveryMessage()[ref.commandId]; as message) { <p role="status">{{ message }}</p> }
          <button matButton type="button" [disabled]="settlementRecoveryBusy()[ref.commandId]" (click)="recoverSettlement(ref)">Check retained settlement draft</button>
          @if (settlementRecoveryMatched()[ref.commandId]) { <button matButton type="button" (click)="acknowledgeSettlement(ref)">Acknowledge exact journal draft</button> }
        </article> }
      </section> }
      @if (settlementRecoveryStorageUnavailable()) { <p role="alert">Settlement recovery references could not be read or saved. Do not edit or create a settlement until recovery storage is available.</p> }
      <fieldset [disabled]="busy() || hasUnresolvedSettlement()">
      <label>Type <select name="settlementKind" [(ngModel)]="settlementKind" (ngModelChange)="settlementChanged(); loadSettlementOptions()"><option value="SALES_RECEIPT">Customer receipt</option><option value="SUPPLIER_PAYMENT">Supplier payment</option></select></label>
      <label>Posting date <input type="date" name="settlementDate" [(ngModel)]="settlementDate" (ngModelChange)="settlementChanged(); loadSettlementOptions()" /></label>
      <label>Period <select name="settlementPeriod" [(ngModel)]="settlementPeriodId" (ngModelChange)="settlementChanged()" required><option value="">Choose an open period</option>@for (period of settlementOptions()?.periods ?? []; track period.id) { <option [value]="period.id">{{ period.code }} · {{ period.currency }}</option> }</select></label>
      <label>Customer or supplier <select name="settlementParty" [(ngModel)]="settlementPartyId" (ngModelChange)="settlementChanged()" required><option value="">Choose a party</option>@for (party of settlementOptions()?.counterparties ?? []; track party.id) { <option [value]="party.id">{{ party.displayName }}</option> }</select></label>
      <label>Cash or bank ledger account <select name="settlementCash" [(ngModel)]="settlementCashCode" (ngModelChange)="settlementChanged()" required><option value="">Choose an asset account</option>@for (account of settlementOptions()?.cashAccounts ?? []; track account.accountCode) { <option [value]="account.accountCode">{{ account.accountCode }} · {{ account.accountName }}</option> }</select></label>
      <label>Amount <input name="settlementAmount" [(ngModel)]="settlementAmount" (ngModelChange)="settlementChanged()" required inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,6})?" /></label>
      <label>External reference <input name="settlementReference" [(ngModel)]="settlementReference" (ngModelChange)="settlementChanged()" required maxlength="200" /></label>
      <label>Evidence reference <input name="settlementEvidence" [(ngModel)]="settlementEvidence" (ngModelChange)="settlementChanged()" required maxlength="1000" /></label>
      <label>Journal number <input name="settlementNumber" [(ngModel)]="settlementNumber" (ngModelChange)="settlementChanged()" required maxlength="100" /></label>
      <label>Description <input name="settlementDescription" [(ngModel)]="settlementDescription" (ngModelChange)="settlementChanged()" required maxlength="1000" /></label>
      <label><input name="settlementReviewed" type="checkbox" [(ngModel)]="settlementReviewed" (ngModelChange)="settlementAssentChanged()" /> I reviewed the party, amount, date, reference, and evidence.</label>
      <button matButton type="button" [disabled]="busy() || !settlementValid() || !settlementReviewed" (click)="previewSettlement()">Preview settlement journal</button>
      @if (settlementPreview(); as p) { <p role="status">{{ p.sourceKind.replaceAll('_', ' ') }} · {{ p.amount }} {{ p.currency }} · @if (p.sourceKind === 'SALES_RECEIPT') { Dr {{ p.cashAccountCode }} / Cr {{ p.controlAccountCode }} } @else { Dr {{ p.controlAccountCode }} / Cr {{ p.cashAccountCode }} } · reference {{ p.reference }}.</p>
        <button matButton type="button" [disabled]="busy() || !settlementReviewed" (click)="createSettlement()">Create draft for independent review</button> }
      @if (settlementReceipt()) { <p role="status">Operational journal draft {{ settlementReceipt() }} was acknowledged. It is not posted and no bank payment was initiated. Continue in Client operational journals for independent review and posting.</p> }
      </fieldset>
    </section>
    <section aria-label="Approved allocation history"><h4>Approved allocation history</h4>
      <p>Reverse an exact approved allocation line through a separate request and independent approval. The original evidence remains retained.</p>
      <button matButton type="button" [disabled]="busy() || historyBusy() || hasUnresolvedSubmission()" (click)="loadAllocationHistory(0)">Load approved allocations</button>
      @if (historyBusy()) { <p role="status">Loading approved allocation history…</p> }
      @if (historyError()) { <p role="alert">{{ historyError() }}</p> }
      @if (allocationHistory(); as h) {
        <p>{{ h.total }} approved original line(s) · page {{ h.page + 1 }}. Reversible amounts include current approved reversals.</p>
        @if (!h.bookkeepingActive) { <p role="status">Bookkeeping service is inactive. History remains available; new unallocation requests are blocked.</p> }
        @for (line of h.lines; track line.allocationLineId) { <article>
          <p>{{ line.counterpartyName }} · {{ line.sourceKind }} <code>{{ line.sourceItemId }}</code> → {{ line.targetKind }} <code>{{ line.targetOpenItemId }}</code></p>
          <p>Original {{ line.originalAmount }} · reversed {{ line.reversedAmount }} · reversible {{ line.reversibleAmount }} {{ line.currency }}.</p>
          <p>Original reference {{ line.reference }} · {{ line.reason }} · approved {{ line.approvedAt }}. Retained line <code>{{ line.allocationLineId }}</code> · evidence hash <code>{{ line.manifestHash }}</code>.</p>
          <button matButton type="button" [disabled]="busy() || historyBusy() || hasUnresolvedSubmission() || !h.bookkeepingActive || !lineReversible(line)" (click)="selectUnallocation(line)">Prepare unallocation of this line</button>
        </article> } @empty { <p>No approved original allocations on this page.</p> }
        <nav aria-label="Approved allocation pages"><button matButton type="button" [disabled]="busy() || historyBusy() || hasUnresolvedSubmission() || h.page === 0" (click)="loadAllocationHistory(h.page - 1)">Previous approved allocations</button>
          <button matButton type="button" [disabled]="busy() || historyBusy() || hasUnresolvedSubmission() || (h.page + 1) * h.pageSize >= h.total" (click)="loadAllocationHistory(h.page + 1)">Next approved allocations</button></nav>
      }
    </section>
    <form #allocation="ngForm" (ngSubmit)="allocation.valid && previewAllocation()">
      <fieldset class="allocation-fields" [disabled]="busy() || historyBusy() || hasUnresolvedSubmission()">
        @if (selectedReversal(); as original) {
          <h4>Unallocate an approved line</h4>
          <p>{{ original.counterpartyName }} · {{ original.currency }} · {{ original.sourceKind }} <code>{{ original.sourceItemId }}</code> → {{ original.targetKind }} <code>{{ original.targetOpenItemId }}</code>.</p>
          <p>Original line <code>{{ original.allocationLineId }}</code> · reversible {{ original.reversibleAmount }} {{ original.currency }}. Enter a positive partial amount or the full remaining amount.</p>
          <button matButton type="button" (click)="startSeparateAllocation()">Prepare a separate allocation</button>
        } @else {
        <h4>Apply a credit or imported settlement</h4>
        <label>Settlement source <select name="source" [(ngModel)]="sourceId" (ngModelChange)="allocationChanged()" required><option value="">Choose an approved credit or sealed imported receipt/payment</option>@for (row of credits(); track row.openItemId) { <option [value]="row.openItemId">{{ row.kind.replaceAll('_', ' ') }} · {{ row.counterpartyName }} · {{ row.openAmount }} {{ row.currency }}</option> }</select></label>
        <label>Target invoice <select name="target" [(ngModel)]="targetId" (ngModelChange)="allocationChanged()" required><option value="">Choose a matching invoice</option>@for (row of targets(); track row.openItemId) { <option [value]="row.openItemId">{{ row.kind.replaceAll('_', ' ') }} · {{ row.counterpartyName }} · {{ row.openAmount }} {{ row.currency }}</option> }</select></label>
        }
        <label>Amount <input name="amount" [(ngModel)]="amount" (ngModelChange)="allocationChanged()" required inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,6})?" /></label>
        <label>Reference <input name="reference" [(ngModel)]="reference" (ngModelChange)="allocationChanged()" required maxlength="200" /></label>
        <label>Reason <input name="reason" [(ngModel)]="reason" (ngModelChange)="allocationChanged()" required maxlength="2000" /></label>
        <button matButton type="submit" [disabled]="allocation.invalid || busy() || historyBusy() || hasUnresolvedSubmission()">{{ selectedReversal() ? 'Preview exact unallocation' : 'Preview allocation' }}</button>
      </fieldset>
    </form>
    @if (hasUnresolvedSubmission()) { <p role="status">A previous allocation submission for this client is unconfirmed. Recover and acknowledge its exact retained receipt before starting another allocation.</p> }
    @if (preview(); as p) { <section aria-label="Allocation preview"><h4>Exact allocation manifest for review</h4>
      <p>{{ p.disposition }} · source {{ p.sourceKind }} · {{ p.sourceItemId }} · party {{ p.counterpartyId }} · original {{ p.sourceOriginalAmount }} {{ p.currency }} · {{ p.sourceAvailableAmount }} {{ p.currency }} available at preview.</p>
      <div class="table-scroll"><table><caption>Exact target lines and amounts in the allocation preview</caption><thead><tr><th>Line</th><th>Target type</th><th>Target open item</th><th>Reverses line</th><th>Allocation amount</th><th>Available at preview</th></tr></thead><tbody>
        @for (line of p.lines; track line.lineNumber; let i = $index) { <tr><td>{{ line.lineNumber }}</td><td>{{ line.targetKind }}</td><td><code>{{ line.targetOpenItemId }}</code></td><td>{{ line.reversesAllocationLineId ?? '—' }}</td><td>{{ line.amount }} {{ p.currency }}</td><td>{{ p.targetAvailableAmounts[i] }} {{ p.currency }}</td></tr> }
      </tbody><tfoot><tr><th colspan="4">Total requested</th><td>{{ totalAllocation(p) }} {{ p.currency }}</td><td></td></tr></tfoot></table></div>
      <p>Reference: {{ previewIntent?.reference }} · Reason: {{ previewIntent?.reason }}</p>
      <p>Preview digest <code>{{ p.digest }}</code>. Displayed availability is retained preview data; submission and approval are checked by the server against current balances.</p>
      <label><input name="makerManifestAssent" type="checkbox" [ngModel]="reviewed" (ngModelChange)="setMakerAssent($event)" [disabled]="busy()" /> I reviewed this exact source, allocation intent, reference, reason, target lines, amounts, currency, and preview digest.</label>
      <button matButton type="button" [disabled]="!canSubmitAllocation()" (click)="submitAllocation()">Submit this exact manifest for independent approval</button></section> }
    @if (receipt()) { <p role="status">Allocation request {{ receipt() }} was submitted. A different authorized reviewer must approve it.</p> }
    @if (pendingLoadedFor() === currentScopeKey() && pending().length) { <section aria-label="Pending allocation reviews"><h4>Pending allocations for independent review</h4>@for (p of pending(); track p.submissionId) { <article>
      <p>{{ p.disposition }} · {{ p.sourceKind }} · source <code>{{ p.sourceItemId }}</code> · party <code>{{ p.counterpartyId }}</code> · original {{ p.sourceOriginalAmount }} {{ p.currency }} · {{ p.sourceAvailableAmount }} {{ p.currency }} available at submission.</p>
      <div class="table-scroll"><table><caption>Retained allocation manifest · {{ p.submissionId }}</caption><thead><tr><th>Line</th><th>Target type</th><th>Target open item</th><th>Reverses line</th><th>Allocation amount</th><th>Available at submission</th></tr></thead><tbody>
        @for (line of p.lines; track line.lineNumber; let i = $index) { <tr><td>{{ line.lineNumber }}</td><td>{{ line.targetKind }}</td><td><code>{{ line.targetOpenItemId }}</code></td><td>{{ line.reversesAllocationLineId ?? '—' }}</td><td>{{ line.amount }} {{ p.currency }}</td><td>{{ p.targetAvailableAmounts[i] }} {{ p.currency }}</td></tr> }
      </tbody><tfoot><tr><th colspan="4">Total requested</th><td>{{ totalAllocation(p) }} {{ p.currency }}</td><td></td></tr></tfoot></table></div>
      <p>Retained preview digest <code>{{ p.digest }}</code>. The balances above are the stored preview, not a current balance check. Approval rechecks current availability and may be refused if it changed.</p>
      <label><input type="checkbox" [name]="'manifest-assent-' + p.submissionId" [ngModel]="reviewAssented(p)" (ngModelChange)="setReviewAssent(p, $event)" [disabled]="busy()" /> I reviewed this exact retained allocation manifest and digest.</label>
      <label>Decision reason <input [(ngModel)]="reviewReason[p.submissionId]" [name]="'reason-' + p.submissionId" required maxlength="2000" /></label>
      <button matButton type="button" [disabled]="busy() || hasUnresolvedReview(p.submissionId) || !reviewAssented(p) || !reviewReason[p.submissionId]?.trim()" (click)="review(p, 'APPROVE')">Approve this exact allocation</button>
      <button matButton type="button" [disabled]="busy() || hasUnresolvedReview(p.submissionId) || !reviewAssented(p) || !reviewReason[p.submissionId]?.trim()" (click)="review(p, 'RETURN')">Return this exact allocation for correction</button></article> }</section> }
  </section>`,
  styles: `
    .allocation-fields { display:flex; flex-wrap:wrap; align-items:start; gap:1rem; min-inline-size:0; max-inline-size:100%; box-sizing:border-box; }
    .allocation-fields > h4, .allocation-fields > p { flex-basis:100%; min-inline-size:0; overflow-wrap:anywhere; }
    .allocation-fields > label { display:grid; gap:.35rem; min-inline-size:0; max-inline-size:100%; }
    .allocation-fields input, .allocation-fields select { min-inline-size:0; max-inline-size:100%; box-sizing:border-box; }
  `,
})
export class OpenItemAllocations {
  readonly clientId = input.required<string>();
  readonly periods = input<{ id: string; code: string; startDate: string; endDate: string; currency: string }[]>([]);
  readonly balances = signal<OpenItem[]>([]); readonly pending = signal<PendingPreview[]>([]); readonly preview = signal<Preview | null>(null);
  readonly pendingLoadedFor = signal('');
  readonly allocationHistory = signal<AllocationHistoryPage | null>(null); readonly historyBusy = signal(false); readonly historyError = signal('');
  readonly selectedReversal = signal<AllocationHistoryLine | null>(null);
  readonly reconciliation = signal<Reconciliation | null>(null);
  readonly busy = signal(false); readonly error = signal(''); readonly receipt = signal('');
  readonly settlementOptions = signal<SettlementOptions | null>(null); readonly settlementPreview = signal<SettlementPreview | null>(null); readonly settlementReceipt = signal('');
  readonly settlementRecoveries = signal<SettlementRecoveryRef[]>([]);
  readonly settlementRecoveryMessage = signal<Record<string, string>>({});
  readonly settlementRecoveryMatched = signal<Record<string, boolean>>({});
  readonly settlementRecoveryBusy = signal<Record<string, boolean>>({});
  readonly settlementRecoveryStorageUnavailable = signal(false);
  asOfDate = new Date().toISOString().slice(0, 10); sourceId = ''; targetId = ''; amount = ''; reference = ''; reason = ''; reviewed = false;
  previewIntent: AllocationIntent | null = null;
  settlementKind = 'SALES_RECEIPT'; settlementDate = new Date().toISOString().slice(0, 10); settlementPeriodId = ''; settlementPartyId = ''; settlementCashCode = '';
  settlementAmount = ''; settlementReference = ''; settlementEvidence = ''; settlementNumber = ''; settlementDescription = ''; settlementReviewed = false;
  private settlementCommandId = crypto.randomUUID();
  readonly reviewReason: Record<string, string> = {};
  readonly reviewAssents = signal<Record<string, string>>({});
  readonly recoveries = signal<AllocationRecoveryRef[]>([]);
  readonly recoveryMessage = signal<Record<string, string>>({});
  readonly recoveryMatched = signal<Record<string, boolean>>({});
  readonly recoveryNotFound = signal<Record<string, boolean>>({});
  readonly recoveryStorageUnavailable = signal(false);
  private readonly http = inject(HttpClient); private readonly destroyRef = inject(DestroyRef); private readonly session = inject(SessionService); private operation?: Subscription;
  private balanceRequest = 0; private pendingRequest = 0; private allocationRequest = 0; private reviewRequest = 0; private historyRequest = 0; private historyOperation?: Subscription; private historyScope = '';
  private previewContext = ''; private makerAssentKey = '';
  private settlementPreviewRequest = '';
  private readonly inMemorySubmitBodies = new Map<string, Record<string, unknown>>();
  constructor() {
    effect(() => { const client = this.clientId(); this.session.current(); this.session.invalidation();
      if (client) untracked(() => { this.resetAllocationScope(); this.loadRecoveryReferences(); this.loadSettlementRecoveryReferences(); this.balances.set([]); this.reconciliation.set(null); this.load(); this.loadSettlementOptions(); }); });
    this.destroyRef.onDestroy(() => { this.operation?.unsubscribe(); this.historyOperation?.unsubscribe(); });
  }
  currentScopeKey(): string {
    const actor = this.session.current();
    return [this.clientId(), actor?.firmId ?? '', actor?.userId ?? '', actor?.generation ?? '', this.session.invalidation()].join('|');
  }
  visibleRecoveries(): AllocationRecoveryRef[] {
    const actor = this.session.current();
    return this.recoveries().filter(x => x.clientId === this.clientId() && x.actorUserId === actor?.userId);
  }
  visibleSettlementRecoveries(): SettlementRecoveryRef[] {
    const actor = this.session.current();
    return this.settlementRecoveries().filter(x => x.clientId === this.clientId() && x.actorUserId === actor?.userId);
  }
  hasUnresolvedSettlement(): boolean {
    const actor = this.session.current();
    return this.settlementRecoveryStorageUnavailable() || this.settlementRecoveries().some(x => x.actorUserId === actor?.userId);
  }
  private loadSettlementRecoveryReferences(): void {
    try {
      const raw = globalThis.sessionStorage?.getItem(settlementRecoveryStorageKey);
      if (!raw) { this.settlementRecoveries.set([]); this.settlementRecoveryStorageUnavailable.set(false); return; }
      const value: unknown = JSON.parse(raw);
      if (!Array.isArray(value) || value.length > 20) throw new Error('Invalid manual settlement recovery references');
      const refs = value.map((item): SettlementRecoveryRef => {
        if (!item || typeof item !== 'object' || Array.isArray(item)) throw new Error('Invalid settlement recovery reference');
        const x = item as Record<string, unknown>;
        if (![x['commandId'], x['clientId'], x['actorUserId']].every(v => typeof v === 'string' && guidPattern.test(v)) ||
          typeof x['previewDigest'] !== 'string' || !allocationDigest.test(x['previewDigest']) ||
          typeof x['intentHash'] !== 'string' || !allocationDigest.test(x['intentHash']) ||
          !(x['journalId'] === undefined || (typeof x['journalId'] === 'string' && guidPattern.test(x['journalId'])))) throw new Error('Invalid settlement recovery reference');
        return x as unknown as SettlementRecoveryRef;
      });
      this.settlementRecoveries.set(refs); this.settlementRecoveryStorageUnavailable.set(false);
      for (const ref of this.visibleSettlementRecoveries()) this.recoverSettlement(ref);
    } catch {
      this.settlementRecoveries.set([]); this.settlementRecoveryStorageUnavailable.set(true);
      this.error.set('Manual settlement recovery data could not be read. Do not create another settlement until the original outcome is resolved.');
    }
  }
  private saveSettlementRecoveryReferences(refs: SettlementRecoveryRef[]): boolean {
    try {
      if (refs.length > 20) return false;
      const storage = globalThis.sessionStorage;
      if (!storage) return false;
      storage.setItem(settlementRecoveryStorageKey, JSON.stringify(refs));
      this.settlementRecoveries.set(refs); this.settlementRecoveryStorageUnavailable.set(false); return true;
    } catch { this.settlementRecoveryStorageUnavailable.set(true); return false; }
  }
  recoverSettlement(ref: SettlementRecoveryRef): void {
    const actor = this.session.current();
    if (!this.visibleSettlementRecoveries().some(x => x.commandId === ref.commandId) || ref.clientId !== this.clientId() ||
      ref.actorUserId !== actor?.userId || this.settlementRecoveryBusy()[ref.commandId]) return;
    const context = this.currentScopeKey();
    this.settlementRecoveryBusy.update(state => ({ ...state, [ref.commandId]: true }));
    this.settlementRecoveryMatched.update(state => ({ ...state, [ref.commandId]: false }));
    this.settlementRecoveryMessage.update(state => ({ ...state, [ref.commandId]: 'Checking the originating actor’s retained settlement record…' }));
    this.http.get<unknown>(`/api/ui/accounting/clients/${ref.clientId}/manual-settlement-requests/${ref.commandId}`).pipe(timeout(15000)).subscribe({
      next: raw => {
        if (context !== this.currentScopeKey()) return;
        const finish = (message: string, matched: boolean) => {
          this.settlementRecoveryBusy.update(state => ({ ...state, [ref.commandId]: false }));
          this.settlementRecoveryMessage.update(state => ({ ...state, [ref.commandId]: message }));
          this.settlementRecoveryMatched.update(state => ({ ...state, [ref.commandId]: matched }));
        };
        try {
          if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error('Invalid settlement lookup');
          const result = raw as SettlementLookup;
          if (typeof result.found !== 'boolean' || result.clientId !== ref.clientId || result.actorUserId !== ref.actorUserId || result.commandId !== ref.commandId)
            throw new Error('Mismatched settlement lookup');
          if (!result.found) {
            if (result.receipt !== null || result.intentHash !== null) throw new Error('Invalid empty settlement lookup');
            finish('No retained draft is visible yet. The original request may still commit, so its intent remains locked.', false); return;
          }
          if (result.intentHash !== ref.intentHash || !result.receipt || result.receipt.commandId !== ref.commandId ||
            typeof result.receipt.journalId !== 'string' || !guidPattern.test(result.receipt.journalId) || result.receipt.outcome !== 'DRAFT') {
            finish('The retained record did not match this actor, client, command, and exact intent hash. The fence remains.', false); return;
          }
          const matchedRef = { ...ref, journalId: result.receipt.journalId };
          if (!this.saveSettlementRecoveryReferences(this.settlementRecoveries().map(x => x.commandId === ref.commandId ? matchedRef : x))) {
            finish('The exact draft was found, but its receipt reference could not be retained. Keep the fence and retry recovery.', false); return;
          }
          finish(`Exact operational journal draft ${result.receipt.journalId} found. Acknowledge it to clear the fence; it is not posted.`, true);
        } catch { finish('The recovery response was invalid or mismatched. The original settlement remains fenced.', false); }
      },
      error: () => {
        if (context !== this.currentScopeKey()) return;
        this.settlementRecoveryBusy.update(state => ({ ...state, [ref.commandId]: false }));
        this.settlementRecoveryMessage.update(state => ({ ...state, [ref.commandId]: 'The retained draft could not be checked. Its outcome remains unknown and the original intent stays locked.' }));
      }
    });
  }
  acknowledgeSettlement(ref: SettlementRecoveryRef): void {
    if (!this.settlementRecoveryMatched()[ref.commandId]) return;
    const journalId = this.settlementRecoveries().find(x => x.commandId === ref.commandId)?.journalId ?? ref.journalId ?? '';
    if (!guidPattern.test(journalId)) return;
    const remaining = this.settlementRecoveries().filter(x => x.commandId !== ref.commandId);
    if (!this.saveSettlementRecoveryReferences(remaining)) {
      this.settlementRecoveryMessage.update(state => ({ ...state, [ref.commandId]: 'The recovery fence could not be cleared from tab storage. Keep it and retry after storage is available.' })); return;
    }
    // The acknowledgement only records discovery of a draft. It does not approve or post the journal.
    this.settlementReceipt.set(journalId);
    this.settlementPreview.set(null); this.settlementPreviewRequest = ''; this.settlementReviewed = false; this.settlementCommandId = crypto.randomUUID();
    this.settlementRecoveryMatched.update(state => { const next = { ...state }; delete next[ref.commandId]; return next; });
  }
  private recoveryKey(ref: Pick<AllocationRecoveryRef, 'kind' | 'commandId'>): string { return `${ref.kind}:${ref.commandId}`; }
  private loadRecoveryReferences(): void {
    try {
      const raw = globalThis.sessionStorage?.getItem(allocationRecoveryStorageKey);
      if (!raw) { this.recoveries.set([]); this.recoveryStorageUnavailable.set(false); return; }
      const value: unknown = JSON.parse(raw);
      if (!Array.isArray(value) || value.length > 20) throw new Error('Invalid allocation recovery references');
      const refs = value.map((item): AllocationRecoveryRef => {
        if (!item || typeof item !== 'object' || Array.isArray(item)) throw new Error('Invalid allocation recovery reference');
        const x = item as Record<string, unknown>;
        if (!['SUBMIT', 'REVIEW'].includes(String(x['kind'])) || ![x['commandId'], x['clientId'], x['actorUserId']].every(v => typeof v === 'string' && guidPattern.test(v)) ||
          !(x['submissionId'] === null || (typeof x['submissionId'] === 'string' && guidPattern.test(x['submissionId']))) ||
          typeof x['previewDigest'] !== 'string' || !allocationDigest.test(x['previewDigest']) || typeof x['fingerprint'] !== 'string' || !allocationDigest.test(x['fingerprint']))
          throw new Error('Invalid allocation recovery reference');
        return x as unknown as AllocationRecoveryRef;
      });
      this.recoveries.set(refs); this.recoveryStorageUnavailable.set(false);
    } catch { this.recoveries.set([]); this.recoveryStorageUnavailable.set(true); this.error.set('The tab recovery reference could not be read. Do not resend or start another allocation review in this tab.'); }
  }
  private saveRecoveryReferences(refs: AllocationRecoveryRef[]): boolean {
    try {
      const storage = globalThis.sessionStorage;
      if (!storage) return false;
      storage.setItem(allocationRecoveryStorageKey, JSON.stringify(refs));
      this.recoveries.set(refs); this.recoveryStorageUnavailable.set(false); return true;
    } catch { return false; }
  }
  hasUnresolvedSubmission(): boolean { return this.recoveryStorageUnavailable() || this.visibleRecoveries().some(x => x.kind === 'SUBMIT'); }
  hasUnresolvedReview(submissionId: string): boolean { return this.recoveryStorageUnavailable() || this.visibleRecoveries().some(x => x.kind === 'REVIEW' && x.submissionId === submissionId); }
  retryableSubmit(ref: AllocationRecoveryRef): boolean { return this.inMemorySubmitBodies.has(this.recoveryKey(ref)); }
  private updateRecoveryState(ref: AllocationRecoveryRef, message: string, matched: boolean, notFound = false): void {
    const key = this.recoveryKey(ref);
    this.recoveryMessage.update(current => ({ ...current, [key]: message }));
    this.recoveryMatched.update(current => ({ ...current, [key]: matched }));
    this.recoveryNotFound.update(current => ({ ...current, [key]: notFound }));
  }
  private removeRecovery(ref: AllocationRecoveryRef): boolean {
    const remaining = this.recoveries().filter(x => this.recoveryKey(x) !== this.recoveryKey(ref));
    if (!this.saveRecoveryReferences(remaining)) { this.error.set('The recovery fence could not be cleared in this tab. Keep it and refresh before starting another command.'); return false; }
    this.inMemorySubmitBodies.delete(this.recoveryKey(ref));
    this.recoveryMessage.update(current => { const next = { ...current }; delete next[this.recoveryKey(ref)]; return next; });
    this.recoveryMatched.update(current => { const next = { ...current }; delete next[this.recoveryKey(ref)]; return next; });
    this.recoveryNotFound.update(current => { const next = { ...current }; delete next[this.recoveryKey(ref)]; return next; });
    return true;
  }
  private recoveredSubmitFingerprint(receipt: AllocationCommandReceipt): Promise<string> {
    return submitFingerprint(receipt.clientId, receipt.actorUserId, { commandId: receipt.commandId, sourceKind: receipt.sourceKind,
      sourceItemId: receipt.sourceItemId, disposition: receipt.disposition, reference: receipt.reference, reason: receipt.reason,
      previewDigest: receipt.previewDigest, lines: receipt.lines });
  }
  recoverCommand(ref: AllocationRecoveryRef): void {
    const actor = this.session.current(), context = this.currentScopeKey();
    if (!this.visibleRecoveries().some(x => this.recoveryKey(x) === this.recoveryKey(ref)) || ref.clientId !== this.clientId() || ref.actorUserId !== actor?.userId || this.busy()) return;
    const request = ++this.reviewRequest, key = this.recoveryKey(ref);
    this.busy.set(true); this.error.set(''); this.updateRecoveryState(ref, '', false);
    this.http.get<unknown>(`/api/ui/accounting/clients/${ref.clientId}/open-item-allocation-requests/${ref.commandId}`, { params: { kind: ref.kind } }).pipe(timeout(15000)).subscribe({
      next: async raw => {
        if (request !== this.reviewRequest || context !== this.currentScopeKey()) return;
        try {
          const lookup = decodeAllocationCommandLookup(raw);
          if (!lookup.found) { this.busy.set(false); this.updateRecoveryState(ref, 'No retained receipt is visible yet. A delayed command may still commit; the intent remains fenced.', false, true); return; }
          const receipt = lookup.receipt!;
          const exactContext = receipt.kind === ref.kind && receipt.clientId === ref.clientId && receipt.actorUserId === ref.actorUserId &&
            receipt.commandId === ref.commandId && receipt.previewDigest === ref.previewDigest &&
            (ref.kind !== 'REVIEW' || receipt.submissionId === ref.submissionId);
          const exactIntent = ref.kind === 'SUBMIT' ? await this.recoveredSubmitFingerprint(receipt) === ref.fingerprint :
            (receipt.decision === 'APPROVE' || receipt.decision === 'RETURN') && !!receipt.decisionReason &&
            await reviewFingerprint(ref, receipt.decision, receipt.decisionReason) === ref.fingerprint;
          if (request !== this.reviewRequest || context !== this.currentScopeKey()) { this.busy.set(false); return; }
          const exactOutcome = ref.kind === 'SUBMIT' ? receipt.outcome === 'SUBMITTED' && receipt.decisionId === null :
            receipt.outcome === (receipt.decision === 'APPROVE' ? 'APPROVED' : 'RETURNED') && !!receipt.decisionId;
          if (!exactContext || !exactIntent || !exactOutcome) { this.busy.set(false); this.updateRecoveryState(ref, 'The retained record does not match this tab, actor, command, digest, and original intent. The fence remains.', false); return; }
          const confirmedRef = ref.kind === 'SUBMIT' ? { ...ref, submissionId: receipt.submissionId } : ref;
          if (confirmedRef !== ref && !this.saveRecoveryReferences(this.recoveries().map(x => this.recoveryKey(x) === key ? confirmedRef : x))) {
            this.busy.set(false); this.updateRecoveryState(ref, 'A matching receipt was found, but its reference could not be retained. Keep the fence and retry recovery.', false); return;
          }
          this.busy.set(false);
          this.updateRecoveryState(confirmedRef, `Matching retained ${receipt.outcome.toLowerCase()} result found (record ${receipt.decisionId ?? receipt.submissionId}). Acknowledge it to clear the fence.`, true);
        } catch { this.busy.set(false); this.updateRecoveryState(ref, 'The recovery response was invalid or belonged to a different context. The fence remains.', false); }
      },
      error: () => {
        if (request !== this.reviewRequest || context !== this.currentScopeKey()) return;
        this.busy.set(false); this.updateRecoveryState(ref, 'The receipt could not be checked. The outcome remains unknown and the intent stays fenced.', false);
      }
    });
  }
  acknowledgeRecovery(ref: AllocationRecoveryRef): void {
    if (!this.recoveryMatched()[this.recoveryKey(ref)] || this.busy()) return;
    const current = this.recoveries().find(x => this.recoveryKey(x) === this.recoveryKey(ref)) ?? ref;
    if (!this.removeRecovery(current)) return;
    if (current.kind === 'SUBMIT' && current.submissionId) {
      this.preview.set(null); this.previewIntent = null; this.previewContext = ''; this.receipt.set(current.submissionId);
    } else if (current.kind === 'REVIEW') this.load();
  }
  private dispatchSameSubmission(ref: AllocationRecoveryRef): void {
    const body = this.inMemorySubmitBodies.get(this.recoveryKey(ref));
    if (!body || this.busy() || ref.kind !== 'SUBMIT') return;
    const context = this.currentScopeKey(), request = ++this.allocationRequest;
    this.busy.set(true); this.error.set(''); this.updateRecoveryState(ref, 'Retrying the exact original allocation request with the same command identity.', false);
    this.http.post<unknown>(`/api/ui/accounting/clients/${ref.clientId}/open-item-allocations/submit`, body).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.allocationRequest || context !== this.currentScopeKey()) return;
        const result = value && typeof value === 'object' ? value as Record<string, unknown> : {};
        this.busy.set(false);
        if (result['commandId'] !== ref.commandId || result['submissionId'] !== ref.submissionId && ref.submissionId !== null || result['outcome'] !== 'SUBMITTED') {
          this.updateRecoveryState(ref, 'The retry response did not match the original command. Check the retained outcome; the fence remains.', false); return;
        }
        if (typeof result['submissionId'] !== 'string' || !guidPattern.test(result['submissionId'])) {
          this.updateRecoveryState(ref, 'The retry response did not contain a valid retained submission identity. Check recovery; the fence remains.', false); return;
        }
        const confirmedRef = { ...ref, submissionId: result['submissionId'] };
        if (!this.saveRecoveryReferences(this.recoveries().map(x => this.recoveryKey(x) === this.recoveryKey(ref) ? confirmedRef : x))) {
          this.updateRecoveryState(ref, 'The same command succeeded, but its result reference could not be retained. Keep the fence and check recovery.', false); return;
        }
        this.updateRecoveryState(confirmedRef, 'The original submission returned a matching response. Acknowledge it to clear the fence.', true);
      },
      error: () => {
        if (request !== this.allocationRequest || context !== this.currentScopeKey()) return;
        this.busy.set(false); this.updateRecoveryState(ref, 'The same-identity retry also has an unknown outcome. Check the retained receipt again; the fence remains.', false);
      }
    });
  }
  retrySameSubmission(ref: AllocationRecoveryRef): void {
    if (!this.recoveryNotFound()[this.recoveryKey(ref)] || !this.retryableSubmit(ref) || this.hasUnresolvedReview(ref.submissionId ?? '')) return;
    this.dispatchSameSubmission(ref);
  }
  credits(): OpenItem[] { return this.balances().filter(x => ['SALES_CREDIT', 'PURCHASE_CREDIT', 'SALES_RECEIPT', 'SUPPLIER_PAYMENT'].includes(x.kind) && x.openAmount !== '0.000000'); }
  targets(): OpenItem[] { const source = this.credits().find(x => x.openItemId === this.sourceId); return this.balances().filter(x => ['SALES_INVOICE', 'PURCHASE_INVOICE', 'OPENING_AR_INVOICE', 'OPENING_AP_INVOICE'].includes(x.kind) && x.openAmount !== '0.000000' && (!source || x.counterpartyId === source.counterpartyId && x.currency === source.currency && (['SALES_CREDIT', 'SALES_RECEIPT'].includes(source.kind) ? ['SALES_INVOICE', 'OPENING_AR_INVOICE'].includes(x.kind) : ['PURCHASE_INVOICE', 'OPENING_AP_INVOICE'].includes(x.kind)))); }
  selectedParty(): string { return this.credits().find(x => x.openItemId === this.sourceId)?.counterpartyName ?? ''; }
  totalAllocation(preview: Preview): string { return allocationTotal(preview.lines); }
  private resetAllocationScope(): void {
    this.balanceRequest++; this.pendingRequest++; this.allocationRequest++; this.reviewRequest++; this.historyRequest++;
    this.historyOperation?.unsubscribe(); this.historyBusy.set(false); this.historyError.set(''); this.allocationHistory.set(null); this.historyScope = ''; this.selectedReversal.set(null);
    this.sourceId = this.targetId = this.amount = this.reference = this.reason = "";
    Object.keys(this.reviewReason).forEach(key => delete this.reviewReason[key]);
    this.reviewAssents.set({}); this.pending.set([]); this.pendingLoadedFor.set("");
    this.preview.set(null); this.previewIntent = null; this.previewContext = ""; this.reviewed = false; this.makerAssentKey = "";
    this.receipt.set("");
    this.settlementPreview.set(null); this.settlementPreviewRequest = ''; this.settlementReceipt.set('');
    this.settlementReviewed = false; this.settlementCommandId = crypto.randomUUID();
  }
  private invalidateMakerPreview(): void {
    this.allocationRequest++; this.preview.set(null); this.previewIntent = null; this.previewContext = "";
    this.reviewed = false; this.makerAssentKey = "";
  }
  allocationChanged(): void { this.invalidateMakerPreview(); }
  lineReversible(line: AllocationHistoryLine): boolean { return BigInt(micros(line.reversibleAmount)) > 0n; }
  loadAllocationHistory(page = 0): void {
    if (this.busy() || this.hasUnresolvedSubmission() || !Number.isSafeInteger(page) || page < 0 || page > 10000) return;
    const client = this.clientId(), context = this.currentScopeKey(), request = ++this.historyRequest;
    if (!guidPattern.test(client) || !this.session.current()) return;
    this.historyOperation?.unsubscribe(); this.historyBusy.set(true); this.historyError.set(''); this.historyScope = '';
    this.allocationHistory.set(null); this.selectedReversal.set(null); this.invalidateMakerPreview();
    this.historyOperation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/open-item-allocations/history`, { params: { page: String(page), pageSize: '25' } }).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.historyRequest || context !== this.currentScopeKey()) return;
        this.historyBusy.set(false);
        try { this.allocationHistory.set(decodeAllocationHistory(value, client, page)); this.historyScope = context; }
        catch { this.historyError.set('Approved allocation history could not be validated. No unallocation is available from this response.'); }
      }, error: () => {
        if (request !== this.historyRequest || context !== this.currentScopeKey()) return;
        this.historyBusy.set(false); this.historyError.set('Approved allocation history is unavailable in this scope. Refresh before preparing an unallocation.');
      }
    });
  }
  selectUnallocation(line: AllocationHistoryLine): void {
    const history = this.allocationHistory();
    if (this.busy() || this.historyBusy() || this.hasUnresolvedSubmission() || this.historyScope !== this.currentScopeKey() ||
      !history?.bookkeepingActive || !history.lines.includes(line) || !this.lineReversible(line)) return;
    this.invalidateMakerPreview(); this.selectedReversal.set(line); this.sourceId = line.sourceItemId; this.targetId = line.targetOpenItemId;
    this.amount = line.reversibleAmount; this.reference = ''; this.reason = '';
  }
  startSeparateAllocation(): void {
    if (this.busy() || this.hasUnresolvedSubmission()) return;
    this.selectedReversal.set(null); this.sourceId = this.targetId = this.amount = this.reference = this.reason = ''; this.invalidateMakerPreview();
  }
  load(): void {
    const client = this.clientId(); if (!guidPattern.test(client)) return;
    const context = this.currentScopeKey(), request = ++this.balanceRequest;
    this.busy.set(true); this.error.set(""); this.invalidateMakerPreview(); this.receipt.set("");
    this.pending.set([]); this.pendingLoadedFor.set(""); this.reviewAssents.set({});
    this.operation?.unsubscribe();
    this.operation = this.http.get<unknown>("/api/ui/accounting/clients/" + client + "/open-item-balances", { params: { asOf: this.asOfDate } }).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.balanceRequest || context !== this.currentScopeKey()) return;
        try { this.balances.set(decodeBalances(value)); this.loadReconciliation(client); this.loadPending(client, context); }
        catch { this.busy.set(false); this.error.set("The client balance response was not valid. Refresh the client and review persisted postings."); }
      },
      error: () => {
        if (request !== this.balanceRequest || context !== this.currentScopeKey()) return;
        this.busy.set(false); this.error.set("Client balances could not be loaded.");
      }
    });
  }
  private loadReconciliation(client: string): void {
    const period = this.periods().find(x => x.currency === this.balances()[0]?.currency && this.asOfDate >= x.startDate && this.asOfDate <= x.endDate) ??
      this.periods().find(x => this.asOfDate >= x.startDate && this.asOfDate <= x.endDate);
    if (!period) { this.reconciliation.set(null); return; }
    this.http.get<Reconciliation>(`/api/ui/accounting/clients/${client}/open-item-control-reconciliation`, { params: { periodId: period.id, asOf: this.asOfDate } }).pipe(timeout(15000)).subscribe({
      next: value => this.reconciliation.set(value), error: () => this.reconciliation.set(null)
    });
  }
  private loadPending(client: string, context: string): void {
    const request = ++this.pendingRequest;
    this.pending.set([]); this.pendingLoadedFor.set("");
    this.http.get<unknown>("/api/ui/accounting/clients/" + client + "/open-item-allocation-submissions").pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.pendingRequest || context !== this.currentScopeKey()) return;
        try {
          const rows = records(value);
          if (rows.length > 100) throw new Error("Too many pending allocation manifests");
          const decoded = rows.map(row => decodeAllocationPreview(row, true));
          if (new Set(decoded.map(row => row.submissionId)).size !== decoded.length) throw new Error("Duplicate pending allocation");
          this.pending.set(decoded); this.pendingLoadedFor.set(context);
        } catch {
          this.pending.set([]); this.pendingLoadedFor.set("");
          this.error.set("Pending allocation manifests could not be read. Refresh this client before reviewing.");
        }
        this.busy.set(false);
      },
      error: () => {
        if (request !== this.pendingRequest || context !== this.currentScopeKey()) return;
        this.pending.set([]); this.pendingLoadedFor.set(""); this.busy.set(false);
        this.error.set("Pending allocation reviews could not be loaded for this client and session. Refresh before continuing.");
      }
    });
  }
  loadSettlementOptions(): void {
    const client = this.clientId(); if (!guidPattern.test(client) || !/^\d{4}-\d{2}-\d{2}$/.test(this.settlementDate)) return;
    this.http.get<SettlementOptions>(`/api/ui/accounting/clients/${client}/manual-settlement-options`, { params: { postingDate: this.settlementDate, sourceKind: this.settlementKind } }).pipe(timeout(15000)).subscribe({
      next: options => { this.settlementOptions.set(options); if (!options.periods.some(x => x.id === this.settlementPeriodId)) this.settlementPeriodId = options.periods[0]?.id ?? ''; if (!options.counterparties.some(x => x.id === this.settlementPartyId)) this.settlementPartyId = ''; if (!options.cashAccounts.some(x => x.accountCode === this.settlementCashCode)) this.settlementCashCode = options.cashAccounts[0]?.accountCode ?? ''; },
      error: () => { this.settlementOptions.set(null); this.error.set('Manual settlement options could not be loaded for this client and date.'); }
    });
  }
  settlementValid(): boolean { return !!this.settlementPeriodId && !!this.settlementPartyId && !!this.settlementCashCode && decimal.test(this.settlementAmount) && Number(this.settlementAmount) > 0 && !!this.settlementReference.trim() && !!this.settlementEvidence.trim() && !!this.settlementNumber.trim() && !!this.settlementDescription.trim(); }
  settlementChanged(): void {
    if (this.hasUnresolvedSettlement()) return;
    this.settlementPreview.set(null); this.settlementPreviewRequest = ''; this.settlementReceipt.set(''); this.settlementReviewed = false; this.settlementCommandId = crypto.randomUUID();
  }
  settlementAssentChanged(): void {
    if (this.hasUnresolvedSettlement()) return;
    this.settlementPreview.set(null); this.settlementPreviewRequest = ''; this.settlementCommandId = crypto.randomUUID();
  }
  private settlementBody(digest = ''): Record<string, unknown> { return { commandId: this.settlementCommandId, periodId: this.settlementPeriodId, counterpartyId: this.settlementPartyId, sourceKind: this.settlementKind, journalNumber: this.settlementNumber.trim(), description: this.settlementDescription.trim(), postingDate: this.settlementDate, cashAccountCode: this.settlementCashCode, amount: this.settlementAmount, reference: this.settlementReference.trim(), evidenceReference: this.settlementEvidence.trim(), previewDigest: digest, reviewed: this.settlementReviewed }; }
  previewSettlement(): void {
    if (this.hasUnresolvedSettlement() || !this.settlementValid() || !this.settlementReviewed) return;
    const context = this.currentScopeKey(), body = this.settlementBody(); this.busy.set(true); this.error.set(''); this.settlementReceipt.set('');
    this.http.post<SettlementPreview>(`/api/ui/accounting/clients/${this.clientId()}/manual-settlements/preview`, body, { observe: 'response' }).pipe(timeout(15000)).subscribe({
      next: response => {
        if (context !== this.currentScopeKey()) return;
        const value = response.body;
        const intentHash = response.headers.get('X-AuditSphere-Intent-Hash');
        if (!value || !allocationDigest.test(value.digest) || !intentHash || !allocationDigest.test(intentHash)) {
          this.settlementPreview.set(null); this.settlementPreviewRequest = ''; this.error.set('The settlement preview did not include valid intent evidence.'); this.busy.set(false); return;
        }
        this.settlementPreview.set({ ...value, intentHash }); this.settlementPreviewRequest = JSON.stringify(body); this.busy.set(false);
      }, error: () => { if (context !== this.currentScopeKey()) return; this.error.set('Settlement preview failed. Confirm the open period, approved AR/AP control, active party, and cash account.'); this.busy.set(false); }
    });
  }
  createSettlement(): void {
    const preview = this.settlementPreview();
    if (this.hasUnresolvedSettlement() || !preview || !this.settlementReviewed || !this.session.current() ||
      JSON.stringify(this.settlementBody()) !== this.settlementPreviewRequest) return;
    const context = this.currentScopeKey(), body = this.settlementBody(preview.digest);
    const ref: SettlementRecoveryRef = { commandId: this.settlementCommandId, clientId: this.clientId(), actorUserId: this.session.current()!.userId,
      previewDigest: preview.digest, intentHash: preview.intentHash };
    if (!guidPattern.test(ref.clientId) || !guidPattern.test(ref.actorUserId) || !this.saveSettlementRecoveryReferences([...this.settlementRecoveries(), ref])) {
      this.error.set('The safe recovery reference could not be saved. No settlement request was sent.'); return;
    }
    this.busy.set(true); this.error.set('');
    this.http.post<{ commandId: string; journalId: string; outcome: string }>(`/api/ui/accounting/clients/${ref.clientId}/manual-settlements`, body).pipe(timeout(15000)).subscribe({
      next: result => {
        if (context !== this.currentScopeKey()) return;
        this.busy.set(false);
        if (result?.commandId !== ref.commandId || result.outcome !== 'DRAFT' || !guidPattern.test(result.journalId)) {
          this.error.set('The settlement response did not match the retained command. Its outcome remains fenced for recovery.'); return;
        }
        this.recoverSettlement(ref);
      },
      error: () => { if (context !== this.currentScopeKey()) return; this.error.set('Settlement outcome is unconfirmed. The exact original intent is locked while its retained draft is checked.'); this.busy.set(false); }
    });
  }
  private currentAllocationIntent(): AllocationIntent | null {
    if (!decimal.test(this.amount) || BigInt(micros(this.amount)) <= 0n || !this.reference.trim() || !this.reason.trim()) return null;
    const original = this.selectedReversal();
    if (original) {
      if (this.historyScope !== this.currentScopeKey() || !this.allocationHistory()?.bookkeepingActive || this.historyBusy() ||
        this.historyError() || BigInt(micros(this.amount)) > BigInt(micros(original.reversibleAmount))) return null;
      return { commandId: crypto.randomUUID(), context: this.currentScopeKey(), sourceKind: original.sourceKind,
        sourceItemId: original.sourceItemId, counterpartyId: original.counterpartyId, currency: original.currency, disposition: 'UNALLOCATE',
        targetKind: original.targetKind, targetOpenItemId: original.targetOpenItemId, amount: this.amount,
        reference: this.reference.trim(), reason: this.reason.trim(), reversesAllocationLineId: original.allocationLineId };
    }
    const source = this.credits().find(x => x.openItemId === this.sourceId);
    const target = this.targets().find(x => x.openItemId === this.targetId);
    if (!source || !target) return null;
    return { commandId: crypto.randomUUID(), context: this.currentScopeKey(), sourceKind: source.kind as AllocationIntent["sourceKind"],
      sourceItemId: source.openItemId, counterpartyId: source.counterpartyId, currency: source.currency, disposition: "ALLOCATE",
      targetKind: target.kind as AllocationIntent["targetKind"], targetOpenItemId: target.openItemId, amount: this.amount,
      reference: this.reference.trim(), reason: this.reason.trim(), reversesAllocationLineId: null };
  }
  private intentStillMatchesForm(intent: AllocationIntent): boolean {
    if (intent.disposition === 'UNALLOCATE') {
      const original = this.selectedReversal();
      return !!original && this.historyScope === this.currentScopeKey() && !!this.allocationHistory()?.bookkeepingActive &&
        intent.context === this.currentScopeKey() && original.allocationLineId === intent.reversesAllocationLineId &&
        original.sourceKind === intent.sourceKind && original.sourceItemId === intent.sourceItemId && original.counterpartyId === intent.counterpartyId &&
        original.currency === intent.currency && original.targetKind === intent.targetKind && original.targetOpenItemId === intent.targetOpenItemId &&
        this.amount === intent.amount && this.reference.trim() === intent.reference && this.reason.trim() === intent.reason &&
        BigInt(micros(intent.amount)) <= BigInt(micros(original.reversibleAmount));
    }
    if (this.selectedReversal()) return false;
    const source = this.credits().find(x => x.openItemId === this.sourceId);
    const target = this.targets().find(x => x.openItemId === this.targetId);
    return intent.context === this.currentScopeKey() && source?.kind === intent.sourceKind && source.openItemId === intent.sourceItemId &&
      source.counterpartyId === intent.counterpartyId && source.currency === intent.currency && target?.kind === intent.targetKind &&
      target.openItemId === intent.targetOpenItemId && this.amount === intent.amount &&
      this.reference.trim() === intent.reference && this.reason.trim() === intent.reason;
  }
  private previewMatchesIntent(preview: Preview, intent: AllocationIntent): boolean {
    const line = preview.lines.length === 1 ? preview.lines[0] : null;
    return preview.submissionId === "00000000-0000-0000-0000-000000000000" && preview.disposition === intent.disposition &&
      preview.sourceKind === intent.sourceKind && preview.sourceItemId === intent.sourceItemId && preview.counterpartyId === intent.counterpartyId &&
      preview.currency === intent.currency && !!line && line.targetKind === intent.targetKind && line.targetOpenItemId === intent.targetOpenItemId &&
      line.reversesAllocationLineId === intent.reversesAllocationLineId && normalizedAmount(line.amount) === normalizedAmount(intent.amount);
  }
  private currentMakerAssentKey(): string {
    const preview = this.preview(), intent = this.previewIntent;
    return preview && intent ? [this.currentScopeKey(), preview.submissionId, preview.digest, JSON.stringify(preview), JSON.stringify(intent)].join("|") : "";
  }
  setMakerAssent(checked: boolean): void {
    const preview = this.preview(), intent = this.previewIntent;
    if (!checked || !preview || !intent || this.previewContext !== this.currentScopeKey() ||
      !this.intentStillMatchesForm(intent) || !this.previewMatchesIntent(preview, intent)) {
      this.reviewed = false; this.makerAssentKey = ""; return;
    }
    this.reviewed = true; this.makerAssentKey = this.currentMakerAssentKey();
  }
  canSubmitAllocation(): boolean {
    const preview = this.preview(), intent = this.previewIntent;
    return !this.busy() && !!preview && !!intent && this.previewContext === this.currentScopeKey() &&
      this.intentStillMatchesForm(intent) && this.previewMatchesIntent(preview, intent) &&
      !this.hasUnresolvedSubmission() && this.reviewed && this.makerAssentKey === this.currentMakerAssentKey();
  }
  previewAllocation(): void {
    if (this.busy() || this.historyBusy() || this.hasUnresolvedSubmission()) return;
    const intent = this.currentAllocationIntent();
    if (!intent) { if (this.selectedReversal()) this.error.set('Enter a positive amount within the selected approved line’s reversible balance and provide a reference and reason.'); return; }
    const context = intent.context, request = ++this.allocationRequest;
    this.preview.set(null); this.previewIntent = intent; this.previewContext = context;
    this.reviewed = false; this.makerAssentKey = ""; this.busy.set(true); this.error.set("");
    const body = { commandId: intent.commandId, sourceKind: intent.sourceKind, sourceItemId: intent.sourceItemId, disposition: intent.disposition,
      reference: intent.reference, reason: intent.reason,
      lines: [{ lineNumber: 1, targetKind: intent.targetKind, targetOpenItemId: intent.targetOpenItemId, amount: intent.amount, reversesAllocationLineId: intent.reversesAllocationLineId }],
      previewDigest: "" };
    this.http.post<unknown>("/api/ui/accounting/clients/" + this.clientId() + "/open-item-allocations/preview", body).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.allocationRequest || context !== this.currentScopeKey()) { if (context === this.currentScopeKey()) this.busy.set(false); return; }
        try {
          const preview = decodeAllocationPreview(value, false);
          if (!this.intentStillMatchesForm(intent) || !this.previewMatchesIntent(preview, intent)) throw new Error("Preview does not match the submitted intent");
          this.preview.set(preview); this.busy.set(false);
        } catch {
          this.preview.set(null); this.previewIntent = null; this.previewContext = ""; this.busy.set(false);
          this.error.set("The allocation preview did not match the selected client, source, target, amount, or manifest. Refresh before continuing.");
        }
      },
      error: () => {
        if (request !== this.allocationRequest || context !== this.currentScopeKey()) return;
        this.busy.set(false); this.preview.set(null); this.previewIntent = null; this.previewContext = "";
        this.error.set("The allocation could not be previewed. Check that the source and invoice still have available balances.");
      }
    });
  }
  async submitAllocation(): Promise<void> {
    if (!this.canSubmitAllocation() || this.hasUnresolvedSubmission()) return;
    const preview = this.preview()!, intent = this.previewIntent!, context = this.currentScopeKey(), request = ++this.allocationRequest;
    const body = { commandId: intent.commandId, sourceKind: preview.sourceKind, sourceItemId: preview.sourceItemId, disposition: preview.disposition,
      reference: intent.reference, reason: intent.reason, lines: preview.lines, previewDigest: preview.digest, reviewed: true };
    const actor = this.session.current()?.userId;
    if (!actor) { this.error.set('The current actor is unavailable. No allocation command was sent.'); return; }
    this.busy.set(true);
    let fingerprint: string;
    try { fingerprint = await submitFingerprint(this.clientId(), actor, body); }
    catch { this.busy.set(false); this.error.set('This browser cannot create a private recovery fingerprint. No allocation command was sent.'); return; }
    if (context !== this.currentScopeKey()) { this.busy.set(false); return; }
    const ref: AllocationRecoveryRef = { kind: 'SUBMIT', commandId: intent.commandId, clientId: this.clientId(), actorUserId: actor,
      submissionId: null, previewDigest: preview.digest, fingerprint };
    if (!this.saveRecoveryReferences([...this.recoveries(), ref])) { this.busy.set(false); this.error.set('This tab could not retain the allocation recovery reference. No command was sent.'); return; }
    const key = this.recoveryKey(ref); this.inMemorySubmitBodies.set(key, body);
    this.error.set(''); this.reviewed = false; this.makerAssentKey = '';
    this.http.post<unknown>("/api/ui/accounting/clients/" + this.clientId() + "/open-item-allocations/submit", body).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.allocationRequest || context !== this.currentScopeKey()) return;
        const result = value && typeof value === 'object' ? value as Record<string, unknown> : {};
        if (result['commandId'] !== ref.commandId || result['outcome'] !== 'SUBMITTED' || typeof result['submissionId'] !== "string" ||
          !guidPattern.test(result['submissionId'])) {
          this.busy.set(false); this.error.set("The submission response was invalid. Refresh pending allocations to confirm the retained request."); return;
        }
        const submissionId = result['submissionId'];
        if (!this.removeRecovery(ref)) { this.busy.set(false); return; }
        this.preview.set(null); this.previewIntent = null; this.previewContext = ""; this.busy.set(false);
        this.load(); this.receipt.set(submissionId);
      },
      error: failure => {
        if (request !== this.allocationRequest || context !== this.currentScopeKey()) return;
        this.busy.set(false); this.reviewed = false; this.makerAssentKey = "";
        this.error.set(failure.status >= 400 && failure.status < 500 ?
          "Submission was refused or its response was lost. Check the actor-owned retained outcome before changing intent." :
          "Submission outcome is unconfirmed. Check the actor-owned retained outcome; do not start a different allocation yet.");
      }
    });
  }
  private exactPending(p: PendingPreview): boolean {
    return this.pendingLoadedFor() === this.currentScopeKey() &&
      this.pending().some(current => current.submissionId === p.submissionId && current.digest === p.digest &&
        JSON.stringify(current) === JSON.stringify(p));
  }
  private reviewAssentKey(p: PendingPreview): string {
    return [this.currentScopeKey(), p.submissionId, p.digest, JSON.stringify(p)].join("|");
  }
  reviewAssented(p: PendingPreview): boolean {
    return this.exactPending(p) && this.reviewAssents()[p.submissionId] === this.reviewAssentKey(p);
  }
  setReviewAssent(p: PendingPreview, checked: boolean): void {
    if (!checked || !this.exactPending(p)) {
      this.reviewAssents.update(current => { const next = { ...current }; delete next[p.submissionId]; return next; });
      return;
    }
    this.reviewAssents.update(current => ({ ...current, [p.submissionId]: this.reviewAssentKey(p) }));
  }
  async review(p: PendingPreview, decision: "APPROVE" | "RETURN"): Promise<void> {
    if (this.busy() || this.hasUnresolvedReview(p.submissionId) || !this.reviewAssented(p) || !this.reviewReason[p.submissionId]?.trim()) return;
    const context = this.currentScopeKey(), request = ++this.reviewRequest;
    const actor = this.session.current()?.userId;
    if (!actor) { this.error.set('The current actor is unavailable. No review command was sent.'); return; }
    const body = { commandId: crypto.randomUUID(), submissionId: p.submissionId, decision,
      reason: this.reviewReason[p.submissionId].trim(), previewDigest: p.digest, reviewed: true };
    this.busy.set(true);
    let fingerprint: string;
    try { fingerprint = await reviewFingerprint({ commandId: body.commandId, clientId: this.clientId(), actorUserId: actor,
      submissionId: p.submissionId, previewDigest: p.digest }, decision, body.reason); }
    catch { this.busy.set(false); this.error.set('This browser cannot create a private recovery fingerprint. No review decision was sent.'); return; }
    if (context !== this.currentScopeKey()) { this.busy.set(false); return; }
    const ref: AllocationRecoveryRef = { kind: 'REVIEW', commandId: body.commandId, clientId: this.clientId(), actorUserId: actor,
      submissionId: p.submissionId, previewDigest: p.digest, fingerprint };
    if (!this.saveRecoveryReferences([...this.recoveries(), ref])) { this.busy.set(false); this.error.set('This tab could not retain the review recovery reference. No decision was sent.'); return; }
    this.error.set('');
    this.reviewAssents.update(current => { const next = { ...current }; delete next[p.submissionId]; return next; });
    this.http.post("/api/ui/accounting/clients/" + this.clientId() + "/open-item-allocation-reviews", body).pipe(timeout(15000)).subscribe({
      next: value => {
        if (request !== this.reviewRequest || context !== this.currentScopeKey()) return;
        const result = value && typeof value === 'object' ? value as Record<string, unknown> : {};
        if (result['commandId'] !== ref.commandId || result['submissionId'] !== ref.submissionId ||
          result['outcome'] !== (decision === 'APPROVE' ? 'APPROVED' : 'RETURNED') || typeof result['decisionId'] !== 'string' || !guidPattern.test(result['decisionId'])) {
          this.busy.set(false); this.error.set('The review response did not match this command. Recover its retained outcome before continuing.'); return;
        }
        if (!this.removeRecovery(ref)) { this.busy.set(false); return; }
        this.busy.set(false); this.load();
      },
      error: failure => {
        if (request !== this.reviewRequest || context !== this.currentScopeKey()) return;
        this.busy.set(false);
        this.error.set(failure.status >= 400 && failure.status < 500 ?
          "The review was refused or its response was lost. Check the actor-owned retained outcome before attempting another decision." :
          "The review outcome is unconfirmed. Check its actor-owned retained outcome; do not issue a new review command yet.");
        if (failure.status === 401) this.session.clear();
      }
    });
  }
}
