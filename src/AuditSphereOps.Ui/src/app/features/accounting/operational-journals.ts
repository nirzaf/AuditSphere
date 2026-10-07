import { ChangeDetectorRef, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';

interface PeriodOption { id: string; code: string; start: string; end: string; currency: string; status: string }
interface JournalLine { lineNumber: number; accountId: string; accountCode: string; accountName: string; description: string; debit: string; credit: string }
interface ReviewDecision { revision: string; decision: string; reason: string; actorUserId: string; createdAt: string }
interface ReversalLink { originalJournalId: string; reversalJournalId: string; originalRevision: string; reason: string; evidenceReference: string; preparedByUserId: string; preparedAt: string; reversalStatus: string }
interface InvoiceOrigin { clientId: string; journalId: string; invoiceId: string; submissionId: string; draftRevision: string; journalSubmittedRevision: string }
interface Journal { id: string; clientId: string; periodId: string; journalNumber: string; description: string; postingDate: string; currency: string; status: string; revision: string; createdByUserId: string; lines: JournalLine[]; decisions: ReviewDecision[]; reversalOf?: ReversalLink | null; reversedBy?: ReversalLink | null; invoiceOrigin?: InvoiceOrigin | null }
interface JournalSummary { id: string; periodId: string; journalNumber: string; description: string; postingDate: string; currency: string; status: string; revision: string; createdByUserId: string; invoiceOrigin?: InvoiceOrigin | null }
interface JournalList { clientId: string; periodId: string | null; status: string | null; page: number; pageSize: number; totalJournals: number; bookkeepingActive: boolean; journals: JournalSummary[] }
interface SourceOrigin { sourceKind: string; sourceId: string; submissionId: string; sourceRevision: string | null; reference: string;
  manifestSha256: string | null; intentSha256: string | null; evidenceId: string | null; evidenceSha256: string | null; evidenceReference: string | null }
interface JournalSnapshot { journalId: string; clientId: string; revision: string; capturedAt: string; journalNumber: string;
  description: string; postingDate: string; currency: string; lines: JournalLine[]; invoiceOrigin?: InvoiceOrigin | null; sourceOrigins?: SourceOrigin[] }
interface JournalPreview { journalId: string; clientId: string; periodId: string; revision: string; status: string; currency: string;
  totalDebit: string; totalCredit: string; digest: string; lines: JournalLine[] }
interface TrialBalanceRow { accountId: string; accountCode: string; accountName: string; openingDebit: string; openingCredit: string; periodDebit: string; periodCredit: string; closingDebit: string; closingCredit: string }
interface TrialBalance { fromDate: string; toDate: string; source: string; openingDebit: string; openingCredit: string; periodDebit: string; periodCredit: string; closingDebit: string; closingCredit: string; rows: TrialBalanceRow[] }
interface LedgerView { bookkeepingActive: boolean; trialBalance: TrialBalance; clientId: string; periodId: string; periodCode: string; currency: string; basis: string; page: number; pageSize: number; totalEntries: number; periodTotalEntries: number; postingSnapshotThrough: string;
  accounts: { accountId: string; accountCode: string; accountName: string; debitMovement: string; creditMovement: string; netMovement: string }[];
  entries: { journalId: string; journalNumber: string; postingDate: string; lineNumber: number; accountCode: string; accountName: string; description: string; debit: string; credit: string; reversesJournalId?: string | null; reversedByJournalId?: string | null; reversedByStatus?: string | null }[] }
interface PostingReceipt { commandId: string; clientId: string; journalId: string; actorUserId: string; submittedRevision: string;
  postedRevision: string; previewDigest: string; intentHash: string; recordedAt: string; status: string; invoiceOrigin?: InvoiceOrigin | null }
interface PendingPosting { journal: Journal; commandId: string; previewDigest: string; reason: string; actorUserId: string }
const reportAmountPattern = /^(?:0|[1-9]\d{0,28})(?:\.\d{1,6})?$/;
const amountPattern = /^(?:0|[1-9]\d{0,14})(?:\.\d{1,6})?$/;
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid journal response');
  return value as Record<string, unknown>;
}
function invoiceOrigin(value: unknown, clientId: string, journalId: unknown, revision?: unknown, status?: unknown): InvoiceOrigin | null {
  if (value == null) return null;
  const v = object(value);
  if (v['clientId'] !== clientId || v['journalId'] !== journalId ||
      !['clientId', 'journalId', 'invoiceId', 'submissionId'].every(k => typeof v[k] === 'string' && guidPattern.test(String(v[k])) && v[k] !== '00000000-0000-0000-0000-000000000000') ||
      !['draftRevision', 'journalSubmittedRevision'].every(k => typeof v[k] === 'string' && /^[1-9]\d{0,18}$/.test(String(v[k])) && BigInt(String(v[k])) <= 9223372036854775807n) ||
      BigInt(String(v['journalSubmittedRevision'])) < 2n) throw new Error('Invalid invoice journal origin');
  if (revision !== undefined) {
    if (typeof revision !== 'string' || !/^[1-9]\d{0,18}$/.test(revision)) throw new Error('Invalid invoice journal revision');
    const submitted = BigInt(String(v['journalSubmittedRevision'])), current = BigInt(revision);
    if (!['DRAFT', 'SUBMITTED', 'RETURNED', 'POSTED'].includes(String(status)) || submitted > current ||
        (status === 'SUBMITTED' && submitted !== current) ||
        (['RETURNED', 'POSTED'].includes(String(status)) && submitted + 1n !== current)) throw new Error('Invoice origin does not match the journal revision');
  }
  return value as InvoiceOrigin;
}
export function decodeOperationalJournal(value: unknown, clientId: string): Journal {
  const v = object(value);
  const origin = invoiceOrigin(v['invoiceOrigin'], clientId, v['id'], v['revision'], v['status']);
  if (v['clientId'] !== clientId || typeof v['id'] !== 'string' || !guidPattern.test(v['id']) ||
      typeof v['periodId'] !== 'string' || !guidPattern.test(v['periodId']) ||
      !['journalNumber', 'description', 'postingDate', 'currency', 'status', 'revision'].every(k => typeof v[k] === 'string') ||
      !/^[1-9]\d{0,18}$/.test(String(v['revision'])) || typeof v['createdByUserId'] !== 'string' || !guidPattern.test(v['createdByUserId']) ||
      !Array.isArray(v['lines']) || v['lines'].length < 2 || v['lines'].length > (origin ? 101 : 100)) throw new Error('Invalid journal response');
  for (const raw of v['lines']) {
    const line = object(raw);
    if (!Number.isSafeInteger(line['lineNumber']) || Number(line['lineNumber']) < 1 ||
        typeof line['accountId'] !== 'string' || !guidPattern.test(line['accountId']) ||
        !['accountCode', 'accountName', 'description', 'debit', 'credit'].every(k => typeof line[k] === 'string') ||
        !amountPattern.test(String(line['debit'])) || !amountPattern.test(String(line['credit']))) throw new Error('Invalid journal line');
  }
  if (!Array.isArray(v['decisions']) || v['decisions'].length > 10000) throw new Error('Invalid journal review history');
  for (const raw of v['decisions']) {
    const d = object(raw);
    if (typeof d['revision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(d['revision']) ||
        !['APPROVE', 'RETURN'].includes(String(d['decision'])) || typeof d['reason'] !== 'string' || !d['reason'].trim() ||
        typeof d['actorUserId'] !== 'string' || !guidPattern.test(d['actorUserId']) || typeof d['createdAt'] !== 'string')
      throw new Error('Invalid journal review history');
  }
  for (const key of ['reversalOf', 'reversedBy']) {
    if (v[key] == null) continue;
    const link = object(v[key]);
    if (!['originalJournalId', 'reversalJournalId', 'preparedByUserId'].every(k => typeof link[k] === 'string' && guidPattern.test(String(link[k]))) ||
        typeof link['originalRevision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(link['originalRevision']) ||
        !['reason', 'evidenceReference', 'preparedAt', 'reversalStatus'].every(k => typeof link[k] === 'string' && String(link[k]).trim()) ||
        !['DRAFT', 'SUBMITTED', 'RETURNED', 'POSTED'].includes(String(link['reversalStatus'])) ||
        link[key === 'reversalOf' ? 'reversalJournalId' : 'originalJournalId'] !== v['id']) throw new Error('Invalid reversal lineage');
  }
  return v as unknown as Journal;
}
export function decodeJournalList(value: unknown, clientId: string, periodId: string | null, status: string | null, page: number): JournalList {
  const v = object(value);
  if (v['clientId'] !== clientId || v['periodId'] !== periodId || v['status'] !== status || v['page'] !== page || v['pageSize'] !== 25 ||
      typeof v['bookkeepingActive'] !== 'boolean' || !Number.isSafeInteger(v['totalJournals']) || Number(v['totalJournals']) < 0 ||
      !Array.isArray(v['journals']) || v['journals'].length > 25 || v['journals'].length > Number(v['totalJournals'])) throw new Error('Invalid journal list');
  const identities = new Set<string>();
  for (const raw of v['journals']) {
    const row = object(raw);
    if (!['id', 'periodId', 'createdByUserId'].every(k => typeof row[k] === 'string' && guidPattern.test(String(row[k]))) || identities.has(String(row['id'])) ||
        (periodId !== null && row['periodId'] !== periodId) || !['DRAFT', 'SUBMITTED', 'RETURNED', 'POSTED'].includes(String(row['status'])) ||
        (status !== null && row['status'] !== status) || typeof row['revision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(row['revision']) ||
        !['journalNumber', 'description', 'postingDate', 'currency'].every(k => typeof row[k] === 'string')) throw new Error('Invalid journal summary');
    invoiceOrigin(row['invoiceOrigin'], clientId, row['id'], row['revision'], row['status']);
    identities.add(String(row['id']));
  }
  return value as JournalList;
}
export function decodeOperationalLedger(value: unknown, clientId: string, periodId: string): LedgerView {
  const v = object(value);
  if (typeof v['bookkeepingActive'] !== 'boolean' || v['clientId'] !== clientId || v['periodId'] !== periodId ||
      !['periodCode', 'currency', 'basis'].every(k => typeof v[k] === 'string') ||
      !['page', 'pageSize', 'totalEntries', 'periodTotalEntries'].every(k => Number.isSafeInteger(v[k]) && Number(v[k]) >= 0) ||
      typeof v['postingSnapshotThrough'] !== 'string' || !/^(?:0|[1-9]\d{0,18})$/.test(v['postingSnapshotThrough']) || BigInt(v['postingSnapshotThrough']) > 9223372036854775807n ||
      Number(v['pageSize']) < 1 || Number(v['pageSize']) > 200 ||
      !Array.isArray(v['accounts']) || v['accounts'].length > 10000 || !Array.isArray(v['entries']) || v['entries'].length > 200) throw new Error('Invalid ledger response');
  const tb = object(v['trialBalance']);
  const columns = ['openingDebit', 'openingCredit', 'periodDebit', 'periodCredit', 'closingDebit', 'closingCredit'];
  const exact = (value: unknown): bigint => { if (typeof value !== 'string' || !reportAmountPattern.test(value)) throw new Error('Invalid trial balance amount'); const [whole, fraction = ''] = value.split('.'); return BigInt(whole) * 1000000n + BigInt(fraction.padEnd(6, '0')); };
  if (tb['source'] !== 'NATIVE_POSTED_PERIOD_ACTIVITY' || !['fromDate', 'toDate'].every(k => typeof tb[k] === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(String(tb[k]))) || String(tb['fromDate']) > String(tb['toDate']) || !Array.isArray(tb['rows']) || tb['rows'].length > 10000) throw new Error('Invalid trial balance context');
  const totals = columns.map(() => 0n); const identities = new Set<string>();
  for (const value of tb['rows']) {
    const row = object(value);
    if (typeof row['accountId'] !== 'string' || !guidPattern.test(row['accountId']) || identities.has(row['accountId']) || !['accountCode', 'accountName'].every(k => typeof row[k] === 'string')) throw new Error('Invalid trial balance account');
    identities.add(row['accountId']); const amounts = columns.map(k => exact(row[k])); amounts.forEach((x, i) => totals[i] += x);
    if ((amounts[0] > 0n && amounts[1] > 0n) || (amounts[4] > 0n && amounts[5] > 0n) || amounts[4] - amounts[5] !== amounts[0] - amounts[1] + amounts[2] - amounts[3]) throw new Error('Trial balance does not reconcile');
  }
  columns.forEach((k, i) => { if (exact(tb[k]) !== totals[i]) throw new Error('Invalid trial balance totals'); });
  if (totals[0] !== totals[1] || totals[2] !== totals[3] || totals[4] !== totals[5]) throw new Error('Trial balance is not balanced');
  for (const raw of v['accounts']) {
    const account = object(raw);
    if (typeof account['accountId'] !== 'string' || !guidPattern.test(account['accountId']) ||
        !['accountCode', 'accountName', 'debitMovement', 'creditMovement', 'netMovement'].every(k => typeof account[k] === 'string') ||
        ![account['debitMovement'], account['creditMovement']].every(x => reportAmountPattern.test(String(x))) ||
        !reportAmountPattern.test(String(account['netMovement']).replace(/^-/, ''))) throw new Error('Invalid ledger account');
  }
  for (const raw of v['entries']) {
    const entry = object(raw);
    for (const key of ['reversesJournalId', 'reversedByJournalId'])
      if (entry[key] != null && (typeof entry[key] !== 'string' || !guidPattern.test(String(entry[key])))) throw new Error('Invalid ledger correction identity');
    if (entry['reversedByJournalId'] != null && !['DRAFT', 'SUBMITTED', 'RETURNED', 'POSTED'].includes(String(entry['reversedByStatus']))) throw new Error('Invalid ledger correction status');
    if (typeof entry['journalId'] !== 'string' || !guidPattern.test(entry['journalId']) ||
        !['journalNumber', 'postingDate', 'accountCode', 'accountName', 'description', 'debit', 'credit'].every(k => typeof entry[k] === 'string') ||
        !/^\d{4}-\d{2}-\d{2}$/.test(String(entry['postingDate'])) ||
        ![entry['debit'], entry['credit']].every(x => amountPattern.test(String(x))) ||
        !Number.isSafeInteger(entry['lineNumber']) || Number(entry['lineNumber']) < 1) throw new Error('Invalid ledger entry');
  }
  return v as unknown as LedgerView;
}
export function decodeJournalPreview(value: unknown, journal: Journal): JournalPreview {
  if (journal.invoiceOrigin) throw new Error('Invoice accounting requires its invoice review preview');
  const p = object(value);
  if (p['journalId'] !== journal.id || p['clientId'] !== journal.clientId || p['periodId'] !== journal.periodId ||
      p['revision'] !== journal.revision || p['status'] !== journal.status || p['currency'] !== journal.currency ||
      typeof p['digest'] !== 'string' || !/^[a-f0-9]{64}$/.test(p['digest']) ||
      typeof p['totalDebit'] !== 'string' || typeof p['totalCredit'] !== 'string' ||
      !amountPattern.test(p['totalDebit']) || p['totalDebit'] !== p['totalCredit']) throw new Error('Invalid journal preview');
  const parsed = decodeOperationalJournal({ ...journal, lines: p['lines'] }, journal.clientId);
  if (JSON.stringify(parsed.lines) !== JSON.stringify(journal.lines)) throw new Error('Preview lines changed');
  return p as unknown as JournalPreview;
}
export function decodeJournalSnapshots(value: unknown, journal: Journal): JournalSnapshot[] {
  if (!Array.isArray(value) || value.length > 10000) throw new Error('Invalid journal versions');
  const seen = new Set<string>();
  for (const raw of value) {
    const v = object(raw);
    if (v['journalId'] !== journal.id || v['clientId'] !== journal.clientId || typeof v['revision'] !== 'string' ||
        !/^[1-9]\d{0,18}$/.test(v['revision']) || seen.has(v['revision']) ||
        !['capturedAt', 'journalNumber', 'description', 'postingDate', 'currency'].every(k => typeof v[k] === 'string'))
      throw new Error('Invalid submitted journal version');
    seen.add(v['revision']);
    decodeOperationalJournal({ ...journal, ...v, id: journal.id, status: 'SUBMITTED', decisions: [], invoiceOrigin: v['invoiceOrigin'] ?? null }, journal.clientId);
    if (v['sourceOrigins'] !== undefined) {
      if (!Array.isArray(v['sourceOrigins']) || v['sourceOrigins'].length > 4) throw new Error('Invalid source-document origins');
      for (const rawOrigin of v['sourceOrigins']) {
        const origin = object(rawOrigin);
        if (!['SALES_INVOICE', 'PURCHASE_INVOICE', 'SALES_CREDIT_NOTE', 'PURCHASE_CREDIT_NOTE', 'SALES_RECEIPT', 'SUPPLIER_PAYMENT'].includes(String(origin['sourceKind'])) ||
            !['sourceId', 'submissionId'].every(k => typeof origin[k] === 'string' && guidPattern.test(String(origin[k])) && origin[k] !== '00000000-0000-0000-0000-000000000000') ||
            typeof origin['reference'] !== 'string' || !origin['reference'].trim() ||
            (origin['sourceRevision'] != null && (typeof origin['sourceRevision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(origin['sourceRevision']))) ||
            (origin['manifestSha256'] != null && (typeof origin['manifestSha256'] !== 'string' || !/^[a-f0-9]{64}$/i.test(origin['manifestSha256']))) ||
            (origin['intentSha256'] != null && (typeof origin['intentSha256'] !== 'string' || !/^[a-f0-9]{64}$/i.test(origin['intentSha256']))) ||
            (origin['evidenceId'] != null && (typeof origin['evidenceId'] !== 'string' || !guidPattern.test(origin['evidenceId']))) ||
            (origin['evidenceSha256'] != null && (typeof origin['evidenceSha256'] !== 'string' || !/^[a-f0-9]{64}$/i.test(origin['evidenceSha256']))) ||
            (origin['evidenceReference'] != null && typeof origin['evidenceReference'] !== 'string')) throw new Error('Invalid source-document origin');
      }
    }
  }
  return value as JournalSnapshot[];
}
export function decodePostingReceipt(value: unknown, clientId: string, commandId: string, actorUserId: string): PostingReceipt {
  const v = object(value);
  if (v['clientId'] !== clientId || v['commandId'] !== commandId || v['actorUserId'] !== actorUserId ||
      typeof v['journalId'] !== 'string' || !guidPattern.test(v['journalId']) || v['status'] !== 'POSTED' ||
      typeof v['submittedRevision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(v['submittedRevision']) ||
      typeof v['postedRevision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(v['postedRevision']) ||
      BigInt(v['postedRevision']) !== BigInt(v['submittedRevision']) + 1n || typeof v['recordedAt'] !== 'string' ||
      !['previewDigest', 'intentHash'].every(k => typeof v[k] === 'string' && /^[a-f0-9]{64}$/.test(String(v[k]))))
    throw new Error('Invalid posting receipt');
  const origin = invoiceOrigin(v['invoiceOrigin'], clientId, v['journalId']);
  if (origin && origin.journalSubmittedRevision !== v['submittedRevision']) throw new Error('Invoice origin does not match the posting receipt');
  return v as unknown as PostingReceipt;
}
export function nativeJournalAmount(value: string): boolean { return /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/.test(value); }
function minor(value: string): bigint { if (!nativeJournalAmount(value)) throw new Error('Invalid native journal amount'); const [whole, fraction = ''] = value.split('.'); return BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0')); }

@Component({
  selector: 'audit-client-operational-journals',
  imports: [FormsModule, MatButtonModule],
  template: `
    <section aria-labelledby="operational-journals-heading">
      <h3 id="operational-journals-heading">Client bookkeeping journals</h3>
      <p>Native client book · {{ bookCurrency() }}. These official-book journals are separate from imported GL and reporting adjustments.</p>
      <section aria-label="Saved client journals">
        <h4>Saved journals</h4>
        <label for="native-list-period">Saved journal period</label><select id="native-list-period" [ngModel]="listPeriod" (ngModelChange)="listPeriod = $event; journalList.set(null)">
          <option value="">All periods</option>@for (p of periods(); track p.id) { <option [value]="p.id">{{ p.code }} · {{ p.currency }}</option> }
        </select>
        <label for="native-list-status">Saved journal status</label><select id="native-list-status" [ngModel]="listStatus" (ngModelChange)="listStatus = $event; journalList.set(null)">
          <option value="">All statuses</option><option value="DRAFT">Draft</option><option value="SUBMITTED">Awaiting independent review</option><option value="RETURNED">Returned</option><option value="POSTED">Posted</option>
        </select>
        <button matButton type="button" [disabled]="listLoading()" (click)="loadList(0)">Refresh saved journals</button>
        @if (listError()) { <p role="alert">{{ listError() }}</p> }
        @if (journalList(); as result) {
          <p>{{ result.totalJournals }} saved journals · Page {{ result.page + 1 }}. Each refresh uses current saved state.</p>
          @if (!result.journals.length) { <p>No journals match the selected filters.</p> }
          <div class="table-scroll"><table><caption>Saved native client journals</caption><thead><tr><th>Journal</th><th>Date</th><th>Description</th><th>Currency</th><th>Status</th><th>Origin</th><th>Revision</th></tr></thead>
            <tbody>@for (j of result.journals; track j.id) { <tr><td><button matButton type="button" [disabled]="busy()" (click)="lookupId = j.id; load()">{{ j.journalNumber }}</button></td><td>{{ j.postingDate }}</td><td>{{ j.description }}</td><td>{{ j.currency }}</td><td>{{ j.status }}</td><td>{{ j.invoiceOrigin ? 'Client sales invoice' : 'Manual journal' }}</td><td>{{ j.revision }}</td></tr> }</tbody>
          </table></div>
          <button matButton type="button" [disabled]="listLoading() || result.page === 0" (click)="loadList(result.page - 1)">Previous journals</button>
          <button matButton type="button" [disabled]="listLoading() || (result.page + 1) * result.pageSize >= result.totalJournals" (click)="loadList(result.page + 1)">Next journals</button>
        }
      </section>
      @if (serviceActive() === false) { <p role="status">Bookkeeping service is inactive. Retained journals, submitted history and posted reports remain available to authorized users. New bookkeeping commands are blocked.</p> }
      @if (error()) { <p role="alert">{{ error() }}</p> }
      @if (journal(); as j) {
        <article aria-label="Selected client journal">
          <h4>{{ j.journalNumber }} · {{ j.status }}</h4>
          <p>{{ j.description }} · {{ j.postingDate }} · {{ j.currency }} · revision {{ j.revision }}</p>
          @if (j.invoiceOrigin; as origin) {
            <section aria-label="Client sales invoice origin">
              <p>Client sales invoice {{ origin.invoiceId }} · invoice revision {{ origin.draftRevision }} · submission {{ origin.submissionId }}.</p>
              <p>Review and corrections belong to the invoice workflow. Journal history and ledger movement remain available here. Posting does not confirm invoice issue or delivery.</p>
            </section>
          }
          @if (j.reversalOf; as link) {
            <section aria-label="Reversal lineage"><p>Full reversal of {{ link.originalJournalId }} · {{ link.reason }} · evidence {{ link.evidenceReference }}</p>
              <button matButton type="button" [disabled]="busy()" (click)="lookupId = link.originalJournalId; load()">Open original journal</button>
            </section>
          }
          @if (j.reversedBy; as link) {
            <section aria-label="Linked correction"><p>Linked full reversal · {{ link.reversalStatus }}. The original gross posting is preserved; only a posted reversal changes ledger movement.</p>
              <button matButton type="button" [disabled]="busy()" (click)="lookupId = link.reversalJournalId; load()">Open reversal journal</button>
            </section>
          }
          @if (!j.invoiceOrigin && j.status === 'POSTED' && !j.reversedBy) {
            <details><summary>Prepare a full reversal</summary>
              <p>The original stays posted. A new balanced journal swaps its exact accounting sides and requires fresh independent review. Choose an explicit open correction period and date.</p>
              <label for="native-reversal-period">Reversal reporting period</label><select id="native-reversal-period" [(ngModel)]="reversalPeriodId" (ngModelChange)="reviewed.set(false)">
                <option value="">Choose open period</option>@for (p of periods(); track p.id) { <option [value]="p.id" [disabled]="p.status === 'CLOSED'">{{ p.code }} · {{ p.currency }} · {{ p.status }}</option> }
              </select>
              <label>Reversal journal number <input [(ngModel)]="reversalNumber" (ngModelChange)="reviewed.set(false)" maxlength="100" /></label>
              <label>Reversal accounting date <input type="date" [(ngModel)]="reversalDate" (ngModelChange)="reviewed.set(false)" /></label>
              <label>Correction reason <input [(ngModel)]="reversalReason" (ngModelChange)="reviewed.set(false)" maxlength="2000" /></label>
              <label>Correction evidence reference <input [(ngModel)]="reversalEvidence" (ngModelChange)="reviewed.set(false)" maxlength="1000" /></label>
              <label><input type="checkbox" [checked]="reviewed()" (change)="setReviewed($any($event.target).checked)" /> I reviewed the original, correction date, full reversed amounts, reason and evidence reference.</label>
              <button matButton type="button" [disabled]="busy() || uncertain() || !reviewed() || !reversalPeriodId || !reversalNumber.trim() || !reversalDate || !reversalReason.trim() || !reversalEvidence.trim() || serviceActive() === false" (click)="createReversal(j)">Save reversal draft</button>
            </details>
          }
          <div class="table-scroll"><table><caption>Immutable journal lines</caption><thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead>
            <tbody>@for (line of j.lines; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table></div>
          @if (j.decisions.length) {
            <section aria-label="Journal review history"><h5>Review history</h5>
              @for (d of j.decisions; track d.revision + ':' + d.decision) {
                <p>Revision {{ d.revision }} · {{ d.decision }} · {{ d.reason }} · {{ d.createdAt }}</p>
              }
            </section>
          }
          <button matButton type="button" [disabled]="busy()" (click)="loadSnapshots(j)">View submitted versions</button>
          @if (snapshots(); as versions) {
            @if (!versions.length) { <p>No submitted content was captured for this journal. Earlier review content is unavailable.</p> }
            @for (v of versions; track v.revision) {
              <details><summary>Submitted revision {{ v.revision }}</summary>
                <p>{{ v.description }} · {{ v.postingDate }} · {{ v.currency }} · captured {{ v.capturedAt }}</p>
                @if (v.sourceOrigins?.length) { <section aria-label="Immutable source document lineage"><h6>Linked source document versions</h6>
                  @for (origin of v.sourceOrigins; track origin.sourceKind + ':' + origin.submissionId) {
                    <p>{{ origin.sourceKind }} · {{ origin.reference }} · source {{ origin.sourceId }} · submission {{ origin.submissionId }}
                      @if (origin.sourceRevision) { · source revision {{ origin.sourceRevision }} }
                      @if (origin.manifestSha256) { · manifest SHA-256 {{ origin.manifestSha256 }} }
                      @if (origin.intentSha256) { · command intent SHA-256 {{ origin.intentSha256 }} }
                      @if (origin.evidenceId) { · evidence {{ origin.evidenceId }} }
                      @if (origin.evidenceSha256) { · evidence SHA-256 {{ origin.evidenceSha256 }} }
                      @if (origin.evidenceReference) { · evidence reference {{ origin.evidenceReference }} }
                    </p>
                  }
                  <p>Linked metadata identifies the immutable submitted source. Evidence bytes are not included in this journal snapshot.</p>
                </section> }
                <div class="table-scroll"><table><caption>Preserved submitted journal lines</caption>
                  <thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead>
                  <tbody>@for (line of v.lines; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody>
                </table></div>
              </details>
            }
          }
          @if (!j.invoiceOrigin && j.status !== 'POSTED') {
            <button matButton type="button" [disabled]="busy() || uncertain()" (click)="loadPreview(j)">Preview accounting effect</button>
            @if (preview(); as p) {
              <p role="status">Server-validated preview · {{ p.currency }} · Debits {{ p.totalDebit }} · Credits {{ p.totalCredit }} · revision {{ p.revision }}</p>
              <details><summary>Reviewed intent identity</summary><code>{{ p.digest }}</code></details>
            }
          }
          @if (!j.invoiceOrigin && j.status === 'DRAFT' && !j.reversalOf && j.createdByUserId === userId()) {
            <button matButton type="button" [disabled]="busy() || uncertain() || !!editing() || serviceActive() === false" (click)="editJournal(j)">Edit draft journal</button>
          }
          @if (!j.invoiceOrigin && j.status === 'RETURNED' && j.createdByUserId === userId()) {
            <button matButton type="button" [disabled]="busy() || uncertain() || !!editing() || serviceActive() === false" (click)="editJournal(j)">Edit returned journal</button>
          }
          @if (!j.invoiceOrigin && (j.status === 'DRAFT' || j.status === 'RETURNED') && !editing()) {
            <label><input type="checkbox" [checked]="reviewed()" (change)="setReviewed($any($event.target).checked)" /> I reviewed this client, journal, posting date, account selection and exact amounts.</label>
            <button matButton [disabled]="busy() || !preview() || !reviewed() || uncertain() || serviceActive() === false" (click)="submit(j)">Submit for independent review</button>
          }
          @if (!j.invoiceOrigin && j.status === 'SUBMITTED') {
            <label><input type="checkbox" [checked]="reviewed()" (change)="setReviewed($any($event.target).checked)" /> I independently reviewed this exact journal revision and its balanced lines.</label>
            <label>Review reason <input [(ngModel)]="reason" maxlength="2000" /></label>
            <button matButton [disabled]="busy() || !preview() || !reviewed() || !reason.trim() || j.createdByUserId === userId() || uncertain() || serviceActive() === false" (click)="approve(j)">Approve and post</button>
            <button matButton type="button" [disabled]="busy() || !reviewed() || !reason.trim() || j.createdByUserId === userId() || uncertain() || serviceActive() === false" (click)="returnJournal(j)">Return for rework</button>
          }
        </article>
      }
      <section aria-labelledby="posted-ledger-heading">
        <h4 id="posted-ledger-heading">Posted General Ledger activity</h4>
        <p>Native client journal movements for the selected period. Opening activity is from all earlier posted dates within this period. Date and account-code filters set the Trial Balance basis; source, reference and counterparty filters narrow matching ledger lines only. Approved native opening-balance snapshots are included in opening balances; prior-period carry-forward, imported GL and reporting adjustments are not included.</p>
        <label for="native-ledger-period">Ledger reporting period</label><select id="native-ledger-period" [ngModel]="ledgerPeriodId" (ngModelChange)="ledgerPeriodId = $event; ledgerFrom = ''; ledgerTo = ''; resetLedgerPaging()">
          <option value="">Choose period</option>@for (p of periods(); track p.id) { <option [value]="p.id">{{ p.code }} · {{ p.currency }} · {{ p.status }}</option> }
        </select>
        <label>Ledger from date <input type="date" [ngModel]="ledgerFrom" (ngModelChange)="ledgerFrom = $event; resetLedgerPaging()" /></label>
        <label>Ledger to date <input type="date" [ngModel]="ledgerTo" (ngModelChange)="ledgerTo = $event; resetLedgerPaging()" /></label>
        <label>Account code from <input maxlength="100" [ngModel]="ledgerAccountFrom" (ngModelChange)="ledgerAccountFrom = $event; resetLedgerPaging()" /></label>
        <label>Account code to <input maxlength="100" [ngModel]="ledgerAccountTo" (ngModelChange)="ledgerAccountTo = $event; resetLedgerPaging()" /></label>
        <label>Journal/source type <select [ngModel]="ledgerSourceType" (ngModelChange)="ledgerSourceType = $event; resetLedgerPaging()">
          <option value="">All posted sources</option><option value="NATIVE_JOURNAL">Native journal</option><option value="REVERSAL">Reversal</option>
          <option value="SALES_INVOICE">Sales invoice</option><option value="PURCHASE_INVOICE">Purchase invoice</option>
          <option value="SALES_CREDIT_NOTE">Sales credit note</option><option value="PURCHASE_CREDIT_NOTE">Purchase credit note</option>
          <option value="SALES_RECEIPT">Customer receipt</option><option value="SUPPLIER_PAYMENT">Supplier payment</option>
        </select></label>
        <label>Journal number or description <input maxlength="100" [ngModel]="ledgerReference" (ngModelChange)="ledgerReference = $event; resetLedgerPaging()" /></label>
        <label>Counterparty ID <input [ngModel]="ledgerCounterpartyId" (ngModelChange)="ledgerCounterpartyId = $event; resetLedgerPaging()" aria-describedby="ledger-counterparty-help" /></label>
        <small id="ledger-counterparty-help">Enter the customer or supplier ID shown on its profile.</small>
        <label><input type="checkbox" [ngModel]="includeZeroAccounts" (ngModelChange)="includeZeroAccounts = $event; resetLedgerPaging()" /> Include accounts with no posted activity</label>
        <button matButton type="button" [disabled]="busy() || !(ledgerPeriodId || periodId)" (click)="loadLedger()">Refresh posted ledger</button>
        @if (ledgerError()) { <p role="alert">{{ ledgerError() }}</p> }
        @if (ledger(); as l) {
          <p>{{ l.periodCode }} · {{ l.basis }} · {{ l.currency }} · {{ l.totalEntries }} matching posted lines of {{ l.periodTotalEntries }} in the date range</p>
          <p>Report snapshot through posting sequence {{ l.postingSnapshotThrough }}. Refresh to include later postings.</p>
          <div class="table-scroll"><table><caption>Official native Trial Balance · {{ l.trialBalance.fromDate }} to {{ l.trialBalance.toDate }}</caption>
            <thead><tr><th>Account</th><th>Opening debit</th><th>Opening credit</th><th>Period debits</th><th>Period credits</th><th>Closing debit</th><th>Closing credit</th></tr></thead>
            <tbody>@for (a of l.trialBalance.rows; track a.accountId) { <tr><td>{{ a.accountCode }} · {{ a.accountName }}</td><td>{{ a.openingDebit }}</td><td>{{ a.openingCredit }}</td><td>{{ a.periodDebit }}</td><td>{{ a.periodCredit }}</td><td>{{ a.closingDebit }}</td><td>{{ a.closingCredit }}</td></tr> }</tbody>
            <tfoot><tr><th>Full result totals</th><td>{{ l.trialBalance.openingDebit }}</td><td>{{ l.trialBalance.openingCredit }}</td><td>{{ l.trialBalance.periodDebit }}</td><td>{{ l.trialBalance.periodCredit }}</td><td>{{ l.trialBalance.closingDebit }}</td><td>{{ l.trialBalance.closingCredit }}</td></tr></tfoot>
          </table></div>
          @if (!l.entries.length) { <p>No posted native journal lines in this period.</p> }
          @if (l.accounts.length) { <div class="table-scroll"><table><caption>Account debit, credit and net movement</caption><thead><tr><th>Account</th><th>Debits</th><th>Credits</th><th>Net movement</th></tr></thead>
            <tbody>@for (a of l.accounts; track a.accountId) { <tr><td>{{ a.accountCode }} · {{ a.accountName }}</td><td>{{ a.debitMovement }}</td><td>{{ a.creditMovement }}</td><td>{{ a.netMovement }}</td></tr> }</tbody></table></div> }
          @if (l.entries.length) { <div class="table-scroll"><table><caption>Posted journal line detail</caption><thead><tr><th>Date / journal</th><th>Correction</th><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead>
            <tbody>@for (e of l.entries; track e.journalId + ':' + e.lineNumber) { <tr><td><button matButton type="button" (click)="lookupId = e.journalId; load()">{{ e.postingDate }} · {{ e.journalNumber }}</button></td>
              <td>@if (e.reversesJournalId) { Full reversal of {{ e.reversesJournalId }} }
                @if (e.reversedByJournalId) { Correction {{ e.reversedByJournalId }} · {{ e.reversedByStatus }} }</td>
              <td>{{ e.accountCode }} · {{ e.accountName }}</td><td>{{ e.description }}</td><td>{{ e.debit }}</td><td>{{ e.credit }}</td></tr> }</tbody></table></div> }
          <nav aria-label="Posted ledger pages">
            <button matButton type="button" [disabled]="busy() || l.page === 0" (click)="loadLedger(l.page - 1)">Previous ledger page</button>
            <span>Page {{ l.page + 1 }} of {{ ledgerPageCount() }}</span>
            <button matButton type="button" [disabled]="busy() || (l.page + 1) * l.pageSize >= l.totalEntries" (click)="loadLedger(l.page + 1)">Next ledger page</button>
          </nav>
        }
      </section>
      <form #createForm="ngForm" (ngSubmit)="createForm.valid && create()">
        <h4>{{ editing() ? (editing()?.status === "DRAFT" ? "Edit saved journal draft" : "Rework returned journal") : "Create manual journal draft" }}</h4>
        <label for="native-journal-period">Reporting period</label><select id="native-journal-period" [disabled]="!!editing()" name="period" [(ngModel)]="periodId" (ngModelChange)="reviewed.set(false)" required>
          <option value="">Choose period</option>@for (p of periods(); track p.id) { <option [value]="p.id" [disabled]="p.status === 'CLOSED'">{{ p.code }} · {{ p.currency }} · {{ p.status }}</option> }
        </select>
        <label>Journal number <input [disabled]="!!editing()" name="number" [(ngModel)]="number" (ngModelChange)="reviewed.set(false)" required maxlength="100" /></label>
        <label>Description <input name="description" [(ngModel)]="description" (ngModelChange)="reviewed.set(false)" required maxlength="1000" /></label>
        <label>Accounting date <input name="date" type="date" [(ngModel)]="postingDate" (ngModelChange)="reviewed.set(false)" required /></label>
        <div class="table-scroll"><table><caption>Balanced journal draft lines</caption><thead><tr><th>Account code</th><th>Description</th><th>Debit</th><th>Credit</th><th></th></tr></thead>
          <tbody>@for (line of lines(); track $index) { <tr>
            <td><input aria-label="Account code" [(ngModel)]="line.accountCode" [name]="'account' + $index" (ngModelChange)="reviewed.set(false)" required maxlength="100" /></td>
            <td><input aria-label="Line description" [(ngModel)]="line.description" [name]="'lineDescription' + $index" (ngModelChange)="reviewed.set(false)" maxlength="1000" /></td>
            <td><input aria-label="Debit" [(ngModel)]="line.debit" [name]="'debit' + $index" (ngModelChange)="reviewed.set(false)" inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,12})(?:\\.[0-9]{1,6})?" required /></td>
            <td><input aria-label="Credit" [(ngModel)]="line.credit" [name]="'credit' + $index" (ngModelChange)="reviewed.set(false)" inputmode="decimal" pattern="(?:0|[1-9][0-9]{0,12})(?:\\.[0-9]{1,6})?" required /></td>
            <td><button matButton type="button" [disabled]="lines().length <= 2" (click)="removeLine($index)">Remove</button></td>
          </tr> }</tbody></table></div>
        <p>Debits {{ totalDebit() }} · Credits {{ totalCredit() }} · {{ balanced() ? 'Balanced' : 'Out of balance' }}</p>
        <button matButton type="button" [disabled]="lines().length >= 100" (click)="addLine()">Add line</button>
        <label><input type="checkbox" [checked]="reviewed()" (change)="setReviewed($any($event.target).checked)" name="createReview" /> I reviewed the selected client, period, date and exact balanced intent.</label>
        <button matButton type="submit" [disabled]="serviceActive() === false || createForm.invalid || !balanced() || !reviewed() || busy() || uncertain()">{{ editing() ? (editing()?.status === "DRAFT" ? "Save draft changes" : "Save rework draft") : "Save journal draft" }}</button>
        @if (editing()) { <button matButton type="button" [disabled]="busy()" (click)="resetDraft()">Cancel journal editing</button> }
      </form>
      <label>Open a saved journal by ID <input [(ngModel)]="lookupId" /></label>
      <button matButton type="button" [disabled]="busy()" (click)="load()">Open journal</button>
      <section aria-label="Posting command recovery">
        <label>Posting command ID <input [readonly]="!!pendingPosting()" [(ngModel)]="receiptKey" /></label>
        <button matButton type="button" [disabled]="busy()" (click)="recoverPosting()">Recover posting receipt</button>
        @if (postingReceipt(); as r) {
          <p>Confirmed posting · {{ r.journalId }} · submitted revision {{ r.submittedRevision }} · posted revision {{ r.postedRevision }} · {{ r.recordedAt }}</p>
          @if (r.invoiceOrigin; as origin) { <p>Client sales invoice {{ origin.invoiceId }} · submission {{ origin.submissionId }}. This receipt confirms the ledger posting; invoice issue and delivery have separate evidence.</p> }
        }
        @if (pendingPosting(); as p) {
          <p>Original posting request · {{ p.journal.journalNumber }} · {{ p.journal.clientId }} · {{ p.journal.postingDate }} · {{ p.journal.currency }} · revision {{ p.journal.revision }} · command {{ p.commandId }}</p>
          <details><summary>Original posting amounts and review reason</summary>
            <p>{{ p.reason }}</p>
            <div class="table-scroll"><table><caption>Original posting request lines</caption>
              <thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead>
              <tbody>@for (line of p.journal.lines; track line.lineNumber) { <tr><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody>
            </table></div>
          </details>
          @if (retryOriginal()) {
            <label><input type="checkbox" [checked]="reviewed()" (change)="setReviewed($any($event.target).checked)" /> I reviewed the original posting request and want to retry that same command.</label>
            <button matButton type="button" [disabled]="busy() || !reviewed()" (click)="retryPosting()">Retry original posting</button>
          }
        }
      </section>
      @if (uncertain()) { <p role="alert">The last action outcome is unknown. Open the authorized journal by ID and inspect its persisted state before any further action.</p> }
    </section>
  `,
})
export class ClientOperationalJournals {
  readonly clientId = input.required<string>();
  readonly periods = input.required<PeriodOption[]>();
  readonly bookCurrency = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly session = inject(SessionService);
  readonly postingReceipt = signal<PostingReceipt | null>(null);
  readonly pendingPosting = signal<PendingPosting | null>(null);
  readonly retryOriginal = signal(false);
  receiptKey = '';
  reversalPeriodId = ''; reversalNumber = ''; reversalDate = ''; reversalReason = ''; reversalEvidence = '';
  readonly editing = signal<Journal | null>(null);
  readonly journal = signal<Journal | null>(null);
  readonly preview = signal<JournalPreview | null>(null);
  readonly snapshots = signal<JournalSnapshot[] | null>(null);
  readonly journalList = signal<JournalList | null>(null);
  readonly listLoading = signal(false); readonly listError = signal(''); readonly serviceActive = signal<boolean | null>(null);
  listPeriod = ''; listStatus = ''; private listRequest = 0; private listOperation?: Subscription;
  private ledgerRequest = 0;
  private readonly ledgerPageSize = 100;
  ledgerPeriodId = '';
  ledgerFrom = ''; ledgerTo = ''; ledgerAccountFrom = ''; ledgerAccountTo = ''; ledgerSourceType = ''; ledgerReference = ''; ledgerCounterpartyId = ''; includeZeroAccounts = false;
  readonly ledger = signal<LedgerView | null>(null);
  readonly ledgerPageCount = (): number => { const current = this.ledger(); return current ? Math.max(1, Math.ceil(current.totalEntries / current.pageSize)) : 1; };
  readonly busy = signal(false);
  readonly error = signal('');
  readonly ledgerError = signal('');
  readonly uncertain = signal(false);
  readonly lines = signal([{ accountCode: '', description: '', debit: '0', credit: '0' }, { accountCode: '', description: '', debit: '0', credit: '0' }]);
  periodId = ''; number = ''; description = ''; postingDate = ''; lookupId = ''; reason = '';
  readonly reviewed = signal(false);
  private operation?: Subscription;
  private readonly invalidate = effect(() => {
    const id = this.clientId(); this.session.invalidation();
    untracked(() => { this.operation?.unsubscribe(); this.listOperation?.unsubscribe(); ++this.listRequest; this.journalList.set(null); this.listLoading.set(false); this.listError.set(''); this.listPeriod = ''; this.listStatus = ''; this.serviceActive.set(null); this.journal.set(null); this.snapshots.set(null); this.preview.set(null); this.ledger.set(null); this.ledgerPeriodId = ''; this.ledgerFrom = ''; this.ledgerTo = ''; this.ledgerAccountFrom = ''; this.ledgerAccountTo = ''; this.ledgerSourceType = ''; this.ledgerReference = ''; this.ledgerCounterpartyId = ''; this.includeZeroAccounts = false; this.error.set(''); this.ledgerError.set(''); this.uncertain.set(false); this.busy.set(false); this.lookupId = ''; this.reason = ''; this.reversalPeriodId = ''; this.reversalNumber = ''; this.reversalDate = ''; this.reversalReason = ''; this.reversalEvidence = ''; this.receiptKey = ''; this.postingReceipt.set(null); this.pendingPosting.set(null); this.retryOriginal.set(false); this.resetDraft(); });
    void id;
  });
  constructor() { inject(DestroyRef).onDestroy(() => { this.operation?.unsubscribe(); this.listOperation?.unsubscribe(); }); }
  setReviewed(value: boolean): void {
    this.reviewed.set(value);
    // Render the acknowledged value before a subsequent edit can revoke it.
    this.changeDetector.detectChanges();
  }
  userId(): string { return this.session.current()?.userId ?? ''; }
  totalDebit(): string { return this.total('debit'); }
  totalCredit(): string { return this.total('credit'); }
  balanced(): boolean { try { const d = this.lines().reduce((sum, x) => sum + minor(x.debit), 0n); const c = this.lines().reduce((sum, x) => sum + minor(x.credit), 0n); return d > 0n && d === c; } catch { return false; } }
  private total(key: 'debit' | 'credit'): string {
    try { const units = this.lines().reduce((sum, x) => sum + minor(x[key]), 0n); return `${units / 1_000_000n}.${(units % 1_000_000n).toString().padStart(6, '0')}`; } catch { return '—'; }
  }
  addLine(): void { this.reviewed.set(false); if (this.lines().length < 100) this.lines.update(lines => [...lines, { accountCode: '', description: '', debit: '0', credit: '0' }]); }
  removeLine(index: number): void { this.reviewed.set(false); if (this.lines().length > 2) this.lines.update(lines => lines.filter((_, i) => i !== index)); }
  resetDraft(): void {
    this.editing.set(null); this.periodId = ''; this.number = ''; this.description = ''; this.postingDate = '';
    this.lines.set([{ accountCode: '', description: '', debit: '0', credit: '0' }, { accountCode: '', description: '', debit: '0', credit: '0' }]);
    this.reviewed.set(false);
  }
  editJournal(journal: Journal): void {
    if (journal.invoiceOrigin || this.busy() || this.uncertain() || journal.clientId !== this.clientId() || !['DRAFT', 'RETURNED'].includes(journal.status) || (journal.status === 'DRAFT' && !!journal.reversalOf) || journal.createdByUserId !== this.userId()) return;
    this.editing.set(journal); this.periodId = journal.periodId; this.number = journal.journalNumber;
    this.description = journal.description; this.postingDate = journal.postingDate;
    this.lines.set(journal.lines.map(({ accountCode, description, debit, credit }) => ({ accountCode, description, debit, credit })));
    this.preview.set(null); this.reviewed.set(false);
  }
  create(): void {
    const clientId = this.clientId(); const period = this.periods().find(p => p.id === this.periodId && p.status !== 'CLOSED');
    if (!period || !this.reviewed() || !this.balanced() || this.busy() || this.uncertain()) return;
    if (this.postingDate < period.start || this.postingDate > period.end || period.currency !== this.bookCurrency()) { this.error.set('Choose a posting date and period matching the native book currency.'); return; }
    const edit = this.editing();
    if (edit && (edit.invoiceOrigin || edit.clientId !== clientId || edit.id !== this.journal()?.id || edit.revision !== this.journal()?.revision || edit.periodId !== period.id)) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.error.set('');
    const url = `/api/ui/accounting/clients/${clientId}/operational-journals` + (edit ? `/${edit.id}/${edit.status === 'DRAFT' ? 'edit' : 'rework'}` : '');
    this.operation = this.http.post<{ id: string }>(url, {
      ...(edit ? { revision: edit.revision } : {}),
      periodId: period.id, journalNumber: this.number.trim(), description: this.description.trim(), postingDate: this.postingDate,
      reviewed: true, lines: this.lines().map(line => ({ ...line, accountCode: line.accountCode.trim(), description: line.description.trim() })),
    }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; this.busy.set(false); if (!value?.id || !guidPattern.test(value.id)) { this.failedUnknown(); return; } this.editing.set(null); this.lookupId = value.id; this.load(); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.failedUnknown(); if (failure.status === 401) this.session.clear(); },
    });
  }
  createReversal(journal: Journal): void {
    const period = this.periods().find(p => p.id === this.reversalPeriodId && p.status !== 'CLOSED');
    if (journal.invoiceOrigin || !period || !this.reviewed() || this.busy() || this.uncertain() || journal.clientId !== this.clientId() || journal.status !== 'POSTED' || journal.reversedBy) return;
    if (this.reversalDate < period.start || this.reversalDate > period.end || period.currency !== journal.currency) {
      this.error.set('Choose an explicit correction date within the open matching-currency period.'); return;
    }
    const generation = this.session.invalidation(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post<{ id: string }>(`/api/ui/accounting/clients/${journal.clientId}/operational-journals/${journal.id}/reversal`, {
      revision: journal.revision, periodId: period.id, journalNumber: this.reversalNumber.trim(), postingDate: this.reversalDate,
      reason: this.reversalReason.trim(), evidenceReference: this.reversalEvidence.trim(), reviewed: true,
    }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; this.busy.set(false);
        if (!value?.id || !guidPattern.test(value.id)) { this.failedUnknown(); return; }
        this.lookupId = value.id; this.reviewed.set(false); this.load(); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.failedUnknown(); if (failure.status === 401) this.session.clear(); },
    });
  }
  load(): void {
    const clientId = this.clientId(); const id = this.lookupId.trim();
    if (!guidPattern.test(id) || this.busy()) { if (id) this.error.set('Enter a valid journal ID.'); return; }
    this.editing.set(null);
    const generation = this.session.invalidation(); this.preview.set(null); this.snapshots.set(null); this.reviewed.set(false); this.busy.set(true); this.error.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${clientId}/operational-journals/${id}`).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; try { const journal = decodeOperationalJournal(value, clientId); if (journal.id !== id) throw new Error(); this.journal.set(journal); this.uncertain.set(!!this.pendingPosting()); this.reviewed.set(false); this.busy.set(false); if (journal.status === 'POSTED') this.loadLedger(0, journal.periodId); } catch { this.busy.set(false); this.error.set('Journal details could not be validated for this client.'); } },
      error: failure => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('Journal could not be loaded in this client scope.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  loadList(page = 0): void {
    this.listOperation?.unsubscribe(); const request = ++this.listRequest; const client = this.clientId();
    const generation = this.session.invalidation(); const period = this.listPeriod || null; const status = this.listStatus || null;
    const params: Record<string, string> = { page: String(page), pageSize: '25' };
    if (period) params['periodId'] = period; if (status) params['status'] = status;
    this.listLoading.set(true); this.listError.set(''); this.journalList.set(null);
    this.listOperation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/operational-journals`, { params }).pipe(timeout(15000)).subscribe({
      next: value => { if (request !== this.listRequest || generation !== this.session.invalidation() || client !== this.clientId()) return;
        this.listLoading.set(false); if ((this.listPeriod || null) !== period || (this.listStatus || null) !== status) return;
        try { const decoded = decodeJournalList(value, client, period, status, page); this.journalList.set(decoded); this.serviceActive.set(decoded.bookkeepingActive); }
        catch { this.listError.set('Saved journals could not be validated for these filters.'); } },
      error: failure => { if (request !== this.listRequest || generation !== this.session.invalidation() || client !== this.clientId()) return;
        this.listLoading.set(false); this.listError.set('Saved journals are unavailable. Refresh or retry.'); if (failure.status === 401) this.session.clear(); }
    });
  }
  resetLedgerPaging(): void { ++this.ledgerRequest; this.ledger.set(null); }
  loadLedger(page = 0, forPeriodId = this.ledgerPeriodId || this.periodId): void {
    const clientId = this.clientId(); const period = this.periods().find(p => p.id === forPeriodId);
    if (!period || this.busy()) return;
    this.ledgerPeriodId = forPeriodId;
    const priorLedger = this.ledger();
    if (page > 0 && (!priorLedger || priorLedger.periodId !== forPeriodId || priorLedger.pageSize !== this.ledgerPageSize)) return;
    const generation = this.session.invalidation(); this.ledgerError.set('');
    const request = ++this.ledgerRequest; const zeroAccounts = this.includeZeroAccounts;
    const fromDate = this.ledgerFrom; const toDate = this.ledgerTo; const accountCodeFrom = this.ledgerAccountFrom.trim(); const accountCodeTo = this.ledgerAccountTo.trim();
    const sourceType = this.ledgerSourceType; const reference = this.ledgerReference.trim(); const counterpartyId = this.ledgerCounterpartyId.trim();
    if ((accountCodeFrom && accountCodeTo && accountCodeFrom > accountCodeTo) || (counterpartyId && !guidPattern.test(counterpartyId))) {
      this.ledgerError.set('Check the account range and enter a valid customer or supplier ID.'); return;
    }
    const params: Record<string, string> = { periodId: forPeriodId, page: String(page), pageSize: String(this.ledgerPageSize), includeZeroAccounts: String(this.includeZeroAccounts) };
    if (page > 0 && priorLedger) params['postingSnapshotThrough'] = priorLedger.postingSnapshotThrough;
    if (fromDate) params['fromDate'] = fromDate; if (toDate) params['toDate'] = toDate;
    if (accountCodeFrom) params['accountCodeFrom'] = accountCodeFrom; if (accountCodeTo) params['accountCodeTo'] = accountCodeTo;
    if (sourceType) params['sourceType'] = sourceType; if (reference) params['reference'] = reference; if (counterpartyId) params['counterpartyId'] = counterpartyId;
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${clientId}/operational-ledger`, {
      params,
    }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || request !== this.ledgerRequest) return; try { const decoded = decodeOperationalLedger(value, clientId, forPeriodId); if (decoded.page !== page || (fromDate && decoded.trialBalance.fromDate !== fromDate) || (toDate && decoded.trialBalance.toDate !== toDate) || this.ledgerFrom !== fromDate || this.ledgerTo !== toDate || this.ledgerAccountFrom.trim() !== accountCodeFrom || this.ledgerAccountTo.trim() !== accountCodeTo || this.ledgerSourceType !== sourceType || this.ledgerReference.trim() !== reference || this.ledgerCounterpartyId.trim() !== counterpartyId || this.ledgerPeriodId !== forPeriodId || this.includeZeroAccounts !== zeroAccounts) return; this.ledger.set(decoded); this.serviceActive.set(decoded.bookkeepingActive); }
        catch { this.ledger.set(null); this.ledgerError.set('Posted ledger response did not match this client and period.'); } },
      error: failure => { if (generation === this.session.invalidation() && request === this.ledgerRequest) { this.ledger.set(null); this.ledgerError.set('Posted client ledger is unavailable. Retry or refresh the client.'); if (failure.status === 401) this.session.clear(); } },
    });
  }
  loadSnapshots(journal: Journal): void {
    if (this.busy() || journal.clientId !== this.clientId()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.snapshots.set(null); this.error.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${journal.clientId}/operational-journals/${journal.id}/snapshots`).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || this.journal()?.id !== journal.id) return;
        this.busy.set(false); try { this.snapshots.set(decodeJournalSnapshots(value, journal)); }
        catch { this.error.set('Submitted content did not match this client journal.'); } },
      error: failure => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('Submitted versions could not be loaded.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  loadPreview(journal: Journal): void {
    if (journal.invoiceOrigin || this.busy() || this.uncertain() || journal.clientId !== this.clientId()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.preview.set(null); this.reviewed.set(false); this.error.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${journal.clientId}/operational-journals/${journal.id}/preview`).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || journal.clientId !== this.clientId()) return;
        this.busy.set(false); try { this.preview.set(decodeJournalPreview(value, journal)); }
        catch { this.error.set('The server preview does not match this saved journal. Reload it before reviewing.'); } },
      error: failure => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('The journal preview is unavailable or its accounting context is no longer valid.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  submit(journal: Journal): void { this.act(journal, 'submit', { revision: journal.revision, previewDigest: this.preview()?.digest, reviewed: true }, 'Journal submitted for independent review.'); }
  returnJournal(journal: Journal): void { this.act(journal, 'return', { revision: journal.revision, reason: this.reason.trim(), reviewed: true }, 'Journal returned to its preparer.'); }
  approve(journal: Journal): void {
    if (journal.invoiceOrigin || this.pendingPosting() || !this.reviewed() || !this.preview() || this.preview()?.journalId !== journal.id ||
        this.preview()?.revision !== journal.revision || !this.reason.trim() || this.busy() || this.uncertain() ||
        journal.clientId !== this.clientId() || journal.createdByUserId === this.userId()) return;
    const command = { journal, commandId: crypto.randomUUID(), previewDigest: this.preview()!.digest, reason: this.reason.trim(), actorUserId: this.userId() };
    this.pendingPosting.set(command); this.receiptKey = command.commandId; this.postingReceipt.set(null);
    this.dispatchPosting(command);
  }
  recoverPosting(): void {
    const clientId = this.clientId(), commandId = this.receiptKey.trim(), actorUserId = this.userId();
    if (!guidPattern.test(commandId) || this.busy()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.retryOriginal.set(false); this.reviewed.set(false);
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${clientId}/operational-posting-receipts/${commandId}`).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return;
        try { const receipt = decodePostingReceipt(value, clientId, commandId, actorUserId); this.validatePendingReceipt(receipt);
          this.postingReceipt.set(receipt); this.pendingPosting.set(null); this.uncertain.set(false); this.busy.set(false);
          this.lookupId = receipt.journalId; this.load();
        } catch { this.failedUnknown(); } },
      error: failure => { if (generation !== this.session.invalidation()) return; this.busy.set(false);
        if (failure.status === 404 && this.pendingPosting()?.commandId === commandId) {
          this.retryOriginal.set(true); this.uncertain.set(true); this.error.set('No committed receipt was found. You may retry the original request using its same command identity.');
        } else { this.error.set('The original posting receipt is unavailable in this client and user scope.'); }
        if (failure.status === 401) this.session.clear(); },
    });
  }
  retryPosting(): void {
    const command = this.pendingPosting();
    if (!command || command.journal.invoiceOrigin || !this.retryOriginal() || !this.reviewed() || this.busy() || command.journal.clientId !== this.clientId() || command.actorUserId !== this.userId()) return;
    this.dispatchPosting(command);
  }
  private validatePendingReceipt(receipt: PostingReceipt): void {
    const pending = this.pendingPosting();
    if (pending && (receipt.invoiceOrigin || receipt.commandId !== pending.commandId || receipt.journalId !== pending.journal.id ||
      receipt.submittedRevision !== pending.journal.revision || receipt.previewDigest !== pending.previewDigest)) throw new Error('Receipt differs from the original request');
  }
  private dispatchPosting(command: PendingPosting): void {
    if (command.journal.invoiceOrigin) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.retryOriginal.set(false); this.error.set('');
    this.operation = this.http.post<unknown>(`/api/ui/accounting/clients/${command.journal.clientId}/operational-journals/${command.journal.id}/post`, {
      revision: command.journal.revision, reason: command.reason, previewDigest: command.previewDigest, reviewed: true, commandId: command.commandId,
    }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return;
        try { const receipt = decodePostingReceipt(value, command.journal.clientId, command.commandId, command.actorUserId);
          this.validatePendingReceipt(receipt); this.postingReceipt.set(receipt); this.pendingPosting.set(null);
          this.uncertain.set(false); this.busy.set(false); this.reviewed.set(false); this.lookupId = receipt.journalId; this.load();
        } catch { this.failedUnknown(); } },
      error: failure => { if (generation !== this.session.invalidation()) return;
        if (failure.status === 400 && ['request.invalid', 'revision.stale', 'gate.blocked', 'mapping.invalid', 'idempotency.conflict'].includes(failure.error?.code)) {
          this.busy.set(false); this.pendingPosting.set(null); this.retryOriginal.set(false); this.uncertain.set(false); this.reviewed.set(false);
          this.preview.set(null); this.error.set('The server refused this posting request. Reload the journal and obtain a fresh preview before another review.');
        } else { this.failedUnknown(); }
        if (failure.status === 401) this.session.clear(); },
    });
  }
  private act(journal: Journal, action: string, body: object, success: string): void {
    if (journal.invoiceOrigin || this.editing() || !this.reviewed() || (action !== 'return' && (!this.preview() || this.preview()?.journalId !== journal.id || this.preview()?.revision !== journal.revision)) || this.busy() || this.uncertain() || journal.clientId !== this.clientId()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post(`/api/ui/accounting/clients/${journal.clientId}/operational-journals/${journal.id}/${action}`, body).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set(success); this.reviewed.set(false); this.load(); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.failedUnknown(); if (failure.status === 401) this.session.clear(); },
    });
  }
  private failedUnknown(): void { this.busy.set(false); this.uncertain.set(true); this.reviewed.set(false); this.error.set('The journal action outcome could not be confirmed. Inspect the persisted journal before retrying.'); }
}
