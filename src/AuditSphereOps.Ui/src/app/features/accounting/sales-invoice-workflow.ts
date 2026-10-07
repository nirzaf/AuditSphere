import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';

interface PostingLine { lineNumber: number; accountId: string; accountCode: string; accountName: string; description: string; debit: string; credit: string }
interface Manifest {
  version: string; draftId: string; draftHash: string; profileId: string; profileRevision: string;
  receivableRoleId: string; receivableDecisionId: string; receivableAccountId: string; noTaxReason: string;
  sourceReceiptId: string | null; sourceReceiptHash: string | null; sourceBasis: string; mandateId: string;
  mandateGeneration: string; lines: PostingLine[];
}
interface SubmissionPreview { invoiceId: string; draftId: string; draftRevision: string; submissionId: null; currency: string; gross: string; digest: string; manifest: Manifest }
interface ReviewPreview { invoiceId: string; submissionId: string; journalId: string; journalRevision: string; digest: string; canPost: boolean; postingBlock: string | null; reviewContextJson: string; manifest: Manifest }
export interface SalesInvoiceLifecycle {
  invoiceId: string; clientId: string; draftRevision: string; state: 'DRAFT' | 'SUBMITTED' | 'RETURNED' | 'POSTED';
  bookkeepingActive: boolean; canRevise: boolean; canSubmit: boolean; canReview: boolean;
  posted: boolean; issued: false; deliveryState: 'NOT_REQUESTED'; submissionId: string | null; journalId: string | null;
  journalRevision: string | null; makerId: string | null; decision: 'APPROVE' | 'RETURN' | null; decisionReason: string | null;
  manifestHash: string | null; manifest: Manifest | null; openAmount: string | null; dueDate: string | null;
  periodId: string; currency: string; sourceLines: { lineNumber: number; description: string; accountCode: string; amount: string }[];
}
interface Receipt { commandId: string; invoiceId: string; submissionId: string; kind: 'SUBMIT' | 'REVIEW'; actorUserId: string; intentHash: string; outcome: 'SUBMITTED' | 'POSTED' | 'RETURNED'; decisionId: string | null }
interface Pending { client: string; invoice: string; actor: string; kind: 'SUBMIT' | 'REVIEW'; body: Record<string, unknown> }
const hash = /^[a-f0-9]{64}$/;
const revision = /^[1-9]\d{0,18}$/;
const exactMoney = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;
const uuid = (v: unknown): v is string => typeof v === 'string' && guidPattern.test(v);
const exactRevision = (v: unknown): v is string => typeof v === 'string' && revision.test(v);
const money = (v: unknown): v is string => typeof v === 'string' && exactMoney.test(v);
const minor = (v: string): bigint => { const [whole, fraction = ''] = v.split('.'); return BigInt(whole) * 1000000n + BigInt(fraction.padEnd(6, '0')); };
const text = (v: unknown, max = 2000): v is string => typeof v === 'string' && v.length <= max && !!v.trim();
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
function decodeManifest(value: unknown): Manifest {
  if (!object(value)) throw new Error('Missing manifest');
  const m = value as unknown as Manifest;
  if (m.version !== 'client-sales-submission-v1' || ![m.draftId, m.profileId, m.receivableRoleId, m.receivableDecisionId, m.receivableAccountId, m.mandateId].every(uuid) ||
      ![m.draftHash].every(x => typeof x === 'string' && hash.test(x)) || !exactRevision(m.profileRevision) || !exactRevision(m.mandateGeneration) || typeof m.noTaxReason !== 'string' || m.noTaxReason.length > 2000 ||
      typeof m.sourceBasis !== 'string' || m.sourceBasis.length > 2000 ||
      (m.sourceReceiptId === null ? m.sourceReceiptHash !== null || !m.sourceBasis.trim() : !uuid(m.sourceReceiptId) || typeof m.sourceReceiptHash !== 'string' || !hash.test(m.sourceReceiptHash)) ||
      !Array.isArray(m.lines) || m.lines.length < 2 || m.lines.length > 101) throw new Error('Invalid retained posting context');
  let debit = 0n, credit = 0n;
  for (const [i, l] of m.lines.entries()) {
    if (!object(l) || l.lineNumber !== i + 1 || !uuid(l.accountId) || !text(l.accountCode, 100) || !text(l.accountName, 200) || !text(l.description) || !money(l.debit) || !money(l.credit)) throw new Error('Invalid exact posting line');
    const d = minor(l.debit), c = minor(l.credit);
    if ((d > 0n) === (c > 0n) || (i === 0 ? l.accountId !== m.receivableAccountId || c !== 0n : d !== 0n)) throw new Error('Invalid invoice posting direction');
    debit += d; credit += c;
  }
  if (debit !== credit || new Set(m.lines.map(l => l.accountId)).size !== m.lines.length) throw new Error('Invalid posting controls');
  return m;
}
export function decodeInvoiceLifecycle(value: unknown, client: string, invoice: string): SalesInvoiceLifecycle {
  if (!object(value)) throw new Error('Missing lifecycle');
  const w = value as unknown as SalesInvoiceLifecycle;
  if (w.clientId !== client || w.invoiceId !== invoice || !uuid(invoice) || !uuid(client) || !exactRevision(w.draftRevision) ||
      !['DRAFT', 'SUBMITTED', 'RETURNED', 'POSTED'].includes(w.state) || ![w.bookkeepingActive, w.canRevise, w.canSubmit, w.canReview, w.posted].every(x => typeof x === 'boolean') ||
      w.issued !== false || w.deliveryState !== 'NOT_REQUESTED' || w.posted !== (w.state === 'POSTED') || !uuid(w.makerId) ||
      !uuid(w.periodId) || typeof w.currency !== 'string' || !/^[A-Z]{3}$/.test(w.currency) || !Array.isArray(w.sourceLines) || w.sourceLines.length > 100 || w.sourceLines.some((line, i) => !object(line) || line.lineNumber !== i + 1 || !text(line.description) || !text(line.accountCode, 100) || !money(line.amount)) ||
      (w.canRevise || w.canSubmit) && (!w.bookkeepingActive || !['DRAFT', 'RETURNED'].includes(w.state)) || w.canReview && (!w.bookkeepingActive || w.state !== 'SUBMITTED')) throw new Error('Invalid scoped invoice lifecycle');
  if (w.state === 'DRAFT' && w.submissionId === null) {
    if ([w.journalId, w.journalRevision, w.decision, w.decisionReason, w.manifestHash, w.manifest, w.openAmount, w.dueDate].some(x => x !== null)) throw new Error('Unexpected draft posting');
  } else {
    if (!uuid(w.submissionId) || !uuid(w.journalId) || !exactRevision(w.journalRevision) || typeof w.manifestHash !== 'string' || !hash.test(w.manifestHash)) throw new Error('Invalid invoice journal linkage');
    decodeManifest(w.manifest);
    if (w.state === 'SUBMITTED' ? w.decision !== null || w.decisionReason !== null : w.decision !== (w.posted ? 'APPROVE' : 'RETURN') || !text(w.decisionReason)) throw new Error('Invalid retained invoice decision');
    if (w.posted ? !money(w.openAmount) || typeof w.dueDate !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(w.dueDate) : w.openAmount !== null || w.dueDate !== null) throw new Error('Invalid open-item origin');
  }
  return w;
}

interface CreditRow { creditNoteId: string; creditNoteReference: string; submissionId: string; state: string; currency: string; amount: string; postingDate: string; reason: string; makerId: string; decision: string | null; decisionReason: string | null; unappliedCustomerCredit: boolean; journalId: string | null }
interface CreditPostingLine { lineNumber: number; originalLineNumber: number; accountCode: string; accountName: string; description: string; debit: string; credit: string; creditAmount: string }
interface CreditManifest { reason: string; sourceBasis: string; originalTaxTreatment: string; lines: CreditPostingLine[] }
interface CreditPreview { creditNoteId: string; invoiceId: string; creditNoteReference: string; currency: string; totalCredit: string; digest: string; originalAmount: string; previouslyCredited: string; remainingCreditLimit: string; lines: CreditPostingLine[]; manifest: CreditManifest }
interface CreditReview { creditNoteId: string; invoiceId: string; submissionId: string; journalId: string; journalRevision: string; digest: string; canPost: boolean; postingBlock: string | null; manifest: CreditManifest }

@Component({ selector: 'audit-sales-credit-note-workflow', imports: [FormsModule, MatButtonModule], template: `<section aria-label="Client sales credit notes">
  <h5>Sales credit notes</h5><p>Credit notes are separate untaxed client records. VAT and tax modules remain optional. Posting creates an unapplied customer credit; it does not issue a refund or settle cash.</p>
  <button matButton type="button" [disabled]="busy()" (click)="refresh()">Refresh credit-note history</button>
  @if (error()) { <p role="alert">{{ error() }}</p> }
  @if (preview(); as p) { <p>Exact credit preview: {{ p.totalCredit }} {{ p.currency }} · Original {{ p.originalAmount }} · Previously credited {{ p.previouslyCredited }} · Remaining limit {{ p.remainingCreditLimit }}</p>
    <table><caption>Proposed reversing client-ledger lines</caption><thead><tr><th>Original line</th><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead><tbody>@for (line of p.lines; track line.lineNumber) { <tr><td>{{ line.originalLineNumber || 'Receivable' }}</td><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.description }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table>
    <p>Review line amounts, invoice, reason, date, source explanation and accounting effect before submission.</p>
    <label><input type="checkbox" [ngModel]="assent()" (ngModelChange)="assent.set($event)" /> I reviewed this positive credit and its effect on the original client invoice.</label>
    <button matButton type="button" [disabled]="busy() || !assent()" (click)="submit()">Submit credit note for independent review</button>
  }
  @if (lifecycle()?.makerId === userId() && !preview()) {
    <form #form="ngForm" (ngSubmit)="form.valid && previewCredit()"><fieldset [disabled]="busy()"><legend>Prepare a credit against the posted invoice</legend>
      <label>Credit note reference <input name="reference" [(ngModel)]="reference" required maxlength="100" /></label>
      <label>Posting date <input name="postingDate" type="date" [(ngModel)]="postingDate" required /></label>
      <label>Commercial reason <textarea name="reason" [(ngModel)]="reason" required maxlength="2000"></textarea></label>
      <label>Source evidence or explanation <textarea name="sourceBasis" [(ngModel)]="sourceBasis" required maxlength="2000"></textarea></label>
      <fieldset><legend>Positive credit by original invoice line</legend>@for (line of lifecycle()?.sourceLines ?? []; track line.lineNumber) {
        <label>Line {{ line.lineNumber }} · {{ line.description }} · {{ line.accountCode }} · Original {{ line.amount }} {{ currency() }}
          <input [name]="'credit-' + line.lineNumber" inputmode="decimal" [ngModel]="amounts[line.lineNumber] ?? ''" (ngModelChange)="amounts[line.lineNumber]=$event" /></label>
      }</fieldset>
      <button matButton type="submit" [disabled]="busy() || !(lifecycle()?.sourceLines?.length)">Preview positive credit</button>
    </fieldset></form>
  }
  @for (row of rows(); track row.submissionId) {
    <article><h6>{{ row.creditNoteReference }} · {{ row.state }}</h6><p>{{ row.amount }} {{ row.currency }} · {{ row.reason }} · {{ row.postingDate }}</p>
      @if (row.unappliedCustomerCredit) { <p>Posted as an unapplied customer credit. Apply it through the client settlement workflow when available.</p> }
      @if (row.state === 'SUBMITTED' && row.makerId !== userId()) {
        <button matButton type="button" [disabled]="busy()" (click)="previewReview(row)">Review credit note</button>
        @if (activeReview()?.submissionId === row.submissionId) { <p>Submitted credit {{ row.amount }} {{ row.currency }} · {{ activeReview()?.canPost ? 'Ready for review' : activeReview()?.postingBlock }}</p>
          <p>Original invoice {{ activeReview()?.invoiceId }} · Untaxed treatment {{ activeReview()?.manifest?.originalTaxTreatment }} · Source: {{ activeReview()?.manifest?.sourceBasis }}</p>
          <table><caption>Credit note journal lines</caption><thead><tr><th>Original line</th><th>Account</th><th>Debit</th><th>Credit</th></tr></thead><tbody>@for (line of activeReview()?.manifest?.lines ?? []; track line.lineNumber) { <tr><td>{{ line.originalLineNumber || 'Receivable' }}</td><td>{{ line.accountCode }} · {{ line.accountName }}</td><td>{{ line.debit }}</td><td>{{ line.credit }}</td></tr> }</tbody></table>
          <label>Independent review reason <textarea [(ngModel)]="reviewReason" maxlength="2000"></textarea></label>
          <button matButton type="button" [disabled]="busy() || !reviewReason.trim() || !activeReview()?.canPost" (click)="review(row, 'APPROVE')">Approve and post credit</button>
          <button matButton type="button" [disabled]="busy() || !reviewReason.trim()" (click)="review(row, 'RETURN')">Return credit note</button>
        }
      }
      @if (row.decision) { <p>Decision {{ row.decision }} · {{ row.decisionReason }}</p> }
    </article>
  }
</section>` })
export class SalesCreditNoteWorkflow {
  readonly clientId = input.required<string>(); readonly invoiceId = input.required<string>(); readonly lifecycle = input.required<SalesInvoiceLifecycle | null>();
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  readonly busy = signal(false); readonly error = signal(''); readonly preview = signal<CreditPreview | null>(null); readonly rows = signal<CreditRow[]>([]);
  readonly activeReview = signal<CreditReview | null>(null); readonly assent = signal(false);
  readonly userId = () => this.session.current()?.userId ?? ''; readonly currency = () => this.lifecycle()?.currency ?? '';
  reference = ''; reason = ''; sourceBasis = ''; postingDate = new Date().toISOString().slice(0, 10); reviewReason = ''; amounts: Record<number, string> = {};
  private body: Record<string, unknown> | null = null;
  constructor() { effect(() => { const lifecycle = this.lifecycle();
    if (lifecycle?.posted) untracked(() => this.refresh()); else untracked(() => this.rows.set([])); }); }
  private base(): string { return `/api/ui/accounting/clients/${this.clientId()}/sales-invoices/${this.invoiceId()}/credit-notes`; }
  refresh(): void { if (this.busy() || !this.lifecycle()?.posted) return; this.busy.set(true); this.http.get<CreditRow[]>(this.base()).pipe(timeout(15000)).subscribe({ next: rows => { this.rows.set(rows); this.busy.set(false); }, error: () => { this.busy.set(false); this.error.set('Credit-note history could not be refreshed.'); } }); }
  private request(): Record<string, unknown> | null {
    const lifecycle = this.lifecycle(); if (!lifecycle?.posted || !this.reference.trim() || !this.reason.trim() || !this.sourceBasis.trim()) return null;
    const lines = lifecycle.sourceLines.map(line => ({ originalLineNumber: line.lineNumber, amount: (this.amounts[line.lineNumber] ?? '').trim() })).filter(line => line.amount !== '' && Number(line.amount) > 0);
    if (!lines.length || lines.some(line => !/^\d{1,13}(?:\.\d{1,6})?$/.test(line.amount))) return null;
    return { creditNoteId: crypto.randomUUID(), creditNoteReference: this.reference.trim(), periodId: lifecycle.periodId,
      postingDate: this.postingDate, reason: this.reason.trim(), sourceReceiptId: null, sourceBasis: this.sourceBasis.trim(), lines };
  }
  previewCredit(): void { const credit = this.request(); if (!credit || this.busy()) { this.error.set('Enter a positive exact amount for at least one original line.'); return; }
    this.busy.set(true); this.error.set(''); this.body = credit;
    this.http.post<CreditPreview>(`${this.base()}/preview`, { ...credit, previewDigest: '', reviewed: false }).pipe(timeout(20000)).subscribe({
      next: p => { this.preview.set(p); this.assent.set(false); this.busy.set(false); }, error: () => { this.busy.set(false); this.error.set('Credit preview failed or timed out. Refresh invoice and history before trying again.'); this.body = null; } }); }
  submit(): void { const p = this.preview(), credit = this.body; if (!p || !credit || this.busy() || !this.assent()) return;
    this.busy.set(true); this.http.post<unknown>(this.base(), { commandId: crypto.randomUUID(), ...credit, previewDigest: p.digest, reviewed: true }).pipe(timeout(20000)).subscribe({
      next: () => { this.busy.set(false); this.preview.set(null); this.body = null; this.refresh(); }, error: () => { this.busy.set(false); this.error.set('Submission outcome is unconfirmed. Refresh credit-note history before another submission.'); this.preview.set(null); this.body = null; this.refresh(); } }); }
  previewReview(row: CreditRow): void { this.busy.set(true); this.http.get<CreditReview>(`/api/ui/accounting/clients/${this.clientId()}/sales-credit-note-submissions/${row.submissionId}/preview`).pipe(timeout(15000)).subscribe({
    next: value => { this.activeReview.set(value); this.reviewReason = ''; this.busy.set(false); }, error: () => { this.busy.set(false); this.error.set('Submitted credit-note review is unavailable.'); } }); }
  review(row: CreditRow, decision: 'APPROVE' | 'RETURN'): void { const p = this.activeReview(); if (!p || this.busy() || !this.reviewReason.trim()) return; this.busy.set(true);
    this.http.post('/api/ui/accounting/clients/' + this.clientId() + '/sales-credit-note-reviews', { commandId: crypto.randomUUID(), submissionId: row.submissionId, decision, reason: this.reviewReason.trim(), previewDigest: p.digest, reviewed: true }).pipe(timeout(20000)).subscribe({
      next: () => { this.activeReview.set(null); this.busy.set(false); this.refresh(); }, error: () => { this.busy.set(false); this.error.set('Review outcome is unconfirmed. Refresh credit-note history before taking another action.'); this.activeReview.set(null); this.refresh(); } }); }
}
export function decodeInvoiceSubmissionPreview(value: unknown, invoice: string, expectedRevision: string): SubmissionPreview {
  if (!object(value)) throw new Error('Missing submission preview');
  const p = value as unknown as SubmissionPreview;
  const m = decodeManifest(p.manifest);
  if (p.invoiceId !== invoice || !uuid(p.draftId) || p.draftId !== m.draftId || p.draftRevision !== expectedRevision || !exactRevision(p.draftRevision) || p.submissionId !== null ||
      typeof p.currency !== 'string' || !/^[A-Z]{3}$/.test(p.currency) || !money(p.gross) || minor(p.gross) !== minor(m.lines[0].debit) || typeof p.digest !== 'string' || !hash.test(p.digest)) throw new Error('Invalid exact invoice preview');
  return p;
}
export function decodeInvoiceReviewPreview(value: unknown, lifecycle: SalesInvoiceLifecycle): ReviewPreview {
  if (!object(value)) throw new Error('Missing review preview');
  const p = value as unknown as ReviewPreview;
  decodeManifest(p.manifest);
  if (p.invoiceId !== lifecycle.invoiceId || p.submissionId !== lifecycle.submissionId || p.journalId !== lifecycle.journalId || p.journalRevision !== lifecycle.journalRevision ||
      typeof p.digest !== 'string' || !hash.test(p.digest) || typeof p.canPost !== 'boolean' || (p.postingBlock !== null && !text(p.postingBlock)) || !text(p.reviewContextJson, 500000) ||
      JSON.stringify(p.manifest) !== JSON.stringify(lifecycle.manifest)) throw new Error('Invalid bound invoice review');
  return p;
}
function decodeReceipt(value: unknown, pending: Pending): Receipt {
  if (!object(value)) throw new Error('Missing receipt');
  const r = value as unknown as Receipt;
  const outcome = pending.kind === 'SUBMIT' ? 'SUBMITTED' : pending.body['decision'] === 'APPROVE' ? 'POSTED' : 'RETURNED';
  if (r.commandId !== pending.body['commandId'] || r.invoiceId !== pending.invoice || r.actorUserId !== pending.actor || r.kind !== pending.kind || r.outcome !== outcome ||
      !uuid(r.submissionId) || typeof r.intentHash !== 'string' || !hash.test(r.intentHash) ||
      (pending.kind === 'REVIEW' ? r.submissionId !== pending.body['submissionId'] || !uuid(r.decisionId) : r.decisionId !== null)) throw new Error('Receipt does not match original intent');
  return r;
}

@Component({ selector: 'audit-sales-invoice-workflow', imports: [FormsModule, MatButtonModule, SalesCreditNoteWorkflow], template: `<section aria-label="Client sales invoice workflow">
  <h4>Invoice accounting lifecycle</h4>
  <button matButton type="button" [disabled]="busy() || !!pending()" (click)="refresh()">Refresh invoice lifecycle</button>
  @if (error()) { <p role="alert">{{ error() }}</p> }
  @if (pending(); as p) {
    <p role="status">{{ p.kind === 'SUBMIT' ? 'Submission' : 'Review' }} outcome is unconfirmed. The original request is locked. Recover it before another action.</p>
    <p>Original request: {{ p.body['commandId'] }}</p>
    <button matButton type="button" [disabled]="busy()" (click)="recover()">Recover original invoice command</button>
    @if (retry()) { <label><input type="checkbox" [ngModel]="retryAssent()" (ngModelChange)="retryAssent.set($event)" /> I reviewed the confirmed absence and will retry this unchanged request.</label><button matButton type="button" [disabled]="busy() || !retryAssent()" (click)="sendPending()">Retry original invoice command</button> }
  }
  @if (receipt(); as r) { <p role="status">Original {{ r.kind }} command confirmed: {{ r.outcome }}. Request {{ r.commandId }} · Submission {{ r.submissionId }}</p> }
  @if (lifecycle(); as w) {
    <p>Client {{ w.clientId }} · Invoice {{ w.invoiceId }} · Current preparation revision {{ w.draftRevision }}</p>
    <p>Accounting state: {{ w.state }} · Posted: {{ w.posted ? 'Yes' : 'No' }} · Issued: No · Delivery: NOT_REQUESTED</p>
    <p>Posting does not issue an invoice document or deliver it. Document issuance and delivery are unavailable in this slice.</p>
    @if (!w.bookkeepingActive) { <p role="status">Bookkeeping service is inactive. Authorized history remains available.</p> }
    @if (w.journalId) { <p>Client journal {{ w.journalId }} · Revision {{ w.journalRevision }} · Submission {{ w.submissionId }}</p> }
    @if (w.decision) { <p>Retained decision: {{ w.decision }} · {{ w.decisionReason }}</p> }
    @if (w.posted) { <p>Original receivable open amount: {{ w.openAmount }} · Due {{ w.dueDate }}. No settlement or allocation workflow is available here.</p> }
    @if (w.posted) { <audit-sales-credit-note-workflow [clientId]="w.clientId" [invoiceId]="w.invoiceId" [lifecycle]="w" /> }
    @if (w.canSubmit && w.makerId === userId()) {
      <form #form="ngForm" (ngSubmit)="form.valid && previewSubmission()"><fieldset [disabled]="busy() || !!pending()"><legend>Prepare exact submission preview</legend>
        <label>Approved receivables role ID <input name="arRole" [(ngModel)]="receivableRoleId" (ngModelChange)="changed()" required [pattern]="guidPattern.source" maxlength="36" /></label>
        <p>Select the identity of an independently approved AR role from Client posting account roles. The server checks its client, chart and effective dates.</p>
        <label>Optional no-tax context <textarea name="noTaxReason" [(ngModel)]="noTaxReason" (ngModelChange)="changed()" maxlength="2000"></textarea></label>
        <p>The untaxed preparation path does not require the optional tax module. A tax profile can be enabled and independently reviewed when required.</p>
        <label>Source receipt ID (optional) <input name="sourceReceipt" [(ngModel)]="sourceReceiptId" (ngModelChange)="changed()" [pattern]="guidPattern.source" maxlength="36" /></label>
        <label>Source basis or explanation <textarea name="sourceBasis" [(ngModel)]="sourceBasis" (ngModelChange)="changed()" [required]="!sourceReceiptId.trim()" maxlength="2000"></textarea></label>
        <p>A receipt preserves source identity. It does not prove external storage or that attachment bytes are included. An explanation is retained for independent review.</p>
        <button matButton type="submit" [disabled]="form.invalid || busy() || !!pending()">Preview invoice submission</button>
      </fieldset></form>
    }
    @if (submissionPreview(); as p) {
      <p>Previewed preparation revision {{ p.draftRevision }} · {{ p.gross }} {{ p.currency }}</p>
      <label><input type="checkbox" [disabled]="busy() || !!pending()" [ngModel]="submitAssent()" (ngModelChange)="submitAssent.set($event)" /> I checked this client, exact preparation revision, receivables role, no-tax reason, source basis and proposed client-ledger lines.</label>
      <button matButton type="button" [disabled]="busy() || !!pending() || !submitAssent() || !w.canSubmit || w.makerId !== userId()" (click)="submit()">Submit invoice for independent review</button>
    }
    @if (w.state === 'RETURNED' && w.canRevise) { <p>Prepare a new revision before resubmission. The returned submission and journal snapshot remain retained.</p> }
    @if (w.canReview && w.makerId !== userId()) {
      <button matButton type="button" [disabled]="busy() || !!pending()" (click)="previewReview()">Preview submitted invoice review</button>
      @if (reviewPreview(); as p) {
        @if (!p.canPost) { <p role="alert">Posting is blocked: {{ p.postingBlock || 'Current posting context is not eligible' }}. A retained return decision may still be available.</p> }
        <form #reviewForm="ngForm" (ngSubmit)="reviewForm.valid && review()"><fieldset [disabled]="busy() || !!pending()"><legend>Independent accounting decision</legend>
          <label>Invoice review decision <select aria-label="Invoice review decision" name="decision" [(ngModel)]="decision" (ngModelChange)="reviewAssent.set(false)" required><option value="">Choose decision</option><option value="APPROVE" [disabled]="!p.canPost">Approve and post to client ledger</option><option value="RETURN">Return for a new preparation revision</option></select></label>
          <label>Invoice review reason <textarea name="reviewReason" [(ngModel)]="reviewReason" (ngModelChange)="reviewAssent.set(false)" required maxlength="2000"></textarea></label>
          <label><input type="checkbox" name="reviewAssent" [ngModel]="reviewAssent()" (ngModelChange)="reviewAssent.set($event)" /> I independently reviewed this submitted revision, source basis, no-tax reason and exact client-ledger effect. Approval commits the accounting posting and receivable open item.</label>
          <button matButton type="submit" [disabled]="reviewForm.invalid || !reviewAssent() || busy() || !!pending() || decision === 'APPROVE' && !p.canPost">Record independent invoice decision</button>
        </fieldset></form>
      }
    }
    @if (displayManifest(); as m) {
      <details open><summary>{{ submissionPreview() || reviewPreview() ? 'Server-previewed' : 'Retained submitted' }} accounting effect</summary>
        <p>Optional no-tax context: {{ m.noTaxReason || 'None recorded; the tax module is not enabled for this posting' }}</p><p>Source receipt: {{ m.sourceReceiptId || 'None selected' }} · Source basis: {{ m.sourceBasis || 'Receipt identity selected' }}</p>
        <p>Accepted mandate {{ m.mandateId }} · Generation {{ m.mandateGeneration }} · Profile revision {{ m.profileRevision }}</p>
        <table><caption>Exact proposed or retained client-ledger lines</caption><thead><tr><th>Account</th><th>Description</th><th>Debit</th><th>Credit</th></tr></thead><tbody>@for (l of m.lines; track l.lineNumber) { <tr><td>{{ l.accountCode }} · {{ l.accountName }}</td><td>{{ l.description }}</td><td>{{ l.debit }}</td><td>{{ l.credit }}</td></tr> }</tbody></table>
      </details>
    }
  }
</section>` })
export class SalesInvoiceWorkflow {
  readonly clientId = input.required<string>(); readonly invoiceId = input.required<string>(); readonly preparationRevision = input('');
  readonly lifecycleChange = output<SalesInvoiceLifecycle | null>(); readonly guidPattern = guidPattern;
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  readonly lifecycle = signal<SalesInvoiceLifecycle | null>(null); readonly submissionPreview = signal<SubmissionPreview | null>(null); readonly reviewPreview = signal<ReviewPreview | null>(null);
  readonly receipt = signal<Receipt | null>(null); readonly pending = signal<Pending | null>(null); readonly busy = signal(false); readonly error = signal('');
  readonly submitAssent = signal(false); readonly reviewAssent = signal(false); readonly retryAssent = signal(false); readonly retry = signal(false);
  readonly userId = () => this.session.current()?.userId ?? '';
  receivableRoleId = ''; noTaxReason = ''; sourceReceiptId = ''; sourceBasis = ''; decision = ''; reviewReason = '';
  private request = 0; private operation?: Subscription; private previewBody: Record<string, unknown> | null = null;
  private readonly context = effect(() => {
    this.clientId(); this.invoiceId(); this.preparationRevision(); this.session.invalidation(); this.session.current();
    untracked(() => { this.operation?.unsubscribe(); ++this.request; this.busy.set(false); this.pending.set(null); this.receipt.set(null); this.setLifecycle(null); this.clearPreviews();
      this.receivableRoleId = ''; this.noTaxReason = ''; this.sourceReceiptId = ''; this.sourceBasis = ''; this.decision = ''; this.reviewReason = ''; this.error.set(''); this.retry.set(false); this.retryAssent.set(false); this.refresh(); });
  });
  constructor() { inject(DestroyRef).onDestroy(() => this.operation?.unsubscribe()); }
  displayManifest(): Manifest | null { return this.submissionPreview()?.manifest ?? this.reviewPreview()?.manifest ?? this.lifecycle()?.manifest ?? null; }
  private setLifecycle(value: SalesInvoiceLifecycle | null): void { this.lifecycle.set(value); this.lifecycleChange.emit(value); }
  private clearPreviews(): void { this.submissionPreview.set(null); this.reviewPreview.set(null); this.previewBody = null; this.submitAssent.set(false); this.reviewAssent.set(false); }
  changed(): void { if (!this.pending()) this.clearPreviews(); }
  private valid(client: string, invoice: string, actor: string, generation: number, request: number): boolean { return client === this.clientId() && invoice === this.invoiceId() && actor === this.userId() && generation === this.session.invalidation() && request === this.request; }
  private base(client = this.clientId()): string { return `/api/ui/accounting/clients/${client}`; }
  refresh(): void {
    if (this.busy() || this.pending() || !uuid(this.clientId()) || !uuid(this.invoiceId()) || !uuid(this.userId())) return;
    const client = this.clientId(), invoice = this.invoiceId(), actor = this.userId(), generation = this.session.invalidation(), request = ++this.request;
    this.busy.set(true); this.clearPreviews(); this.setLifecycle(null); this.error.set('');
    this.operation = this.http.get<unknown>(`${this.base(client)}/sales-invoices/${invoice}/lifecycle`).pipe(timeout(15000)).subscribe({
      next: value => { if (!this.valid(client, invoice, actor, generation, request)) return; this.busy.set(false); try { this.setLifecycle(decodeInvoiceLifecycle(value, client, invoice)); } catch { this.error.set('Invoice lifecycle could not be validated. Refresh before acting.'); } },
      error: e => this.failure(e, client, invoice, actor, generation, request, 'Invoice lifecycle unavailable. Refresh before acting.')
    });
  }
  previewSubmission(): void {
    const w = this.lifecycle();
    if (this.busy() || this.pending() || !w?.canSubmit || w.makerId !== this.userId() || !uuid(this.receivableRoleId.trim()) || this.noTaxReason.length > 2000 || (this.sourceReceiptId.trim() ? !uuid(this.sourceReceiptId.trim()) : !this.sourceBasis.trim())) return;
    const client = this.clientId(), invoice = this.invoiceId(), actor = this.userId(), generation = this.session.invalidation(), request = ++this.request;
    const body = { commandId: crypto.randomUUID(), expectedDraftRevision: w.draftRevision, receivableRoleId: this.receivableRoleId.trim(), noTaxReason: this.noTaxReason.trim(), sourceReceiptId: this.sourceReceiptId.trim() || null, sourceBasis: this.sourceBasis.trim(), previewDigest: '', reviewed: false };
    this.busy.set(true); this.clearPreviews(); this.error.set('');
    this.operation = this.http.post<unknown>(`${this.base(client)}/sales-invoices/${invoice}/preview`, body).pipe(timeout(15000)).subscribe({
      next: value => { if (!this.valid(client, invoice, actor, generation, request)) return; this.busy.set(false); try { const p = decodeInvoiceSubmissionPreview(value, invoice, w.draftRevision); if (p.manifest.receivableRoleId !== body.receivableRoleId || p.manifest.noTaxReason !== body.noTaxReason || p.manifest.sourceReceiptId !== body.sourceReceiptId || p.manifest.sourceBasis !== body.sourceBasis) throw new Error('Changed preview intent'); this.submissionPreview.set(p); this.previewBody = { ...body, previewDigest: p.digest, reviewed: true }; } catch { this.error.set('Submission preview did not match this exact intent.'); } },
      error: e => this.failure(e, client, invoice, actor, generation, request, 'Submission preview refused. Check current service, revision, period, approved AR role and source basis.')
    });
  }
  submit(): void {
    const w = this.lifecycle(), p = this.submissionPreview();
    if (this.busy() || this.pending() || !w?.canSubmit || w.makerId !== this.userId() || !this.submitAssent() || !p || !this.previewBody || p.draftRevision !== w.draftRevision) return;
    this.pending.set({ client: this.clientId(), invoice: this.invoiceId(), actor: this.userId(), kind: 'SUBMIT', body: { ...this.previewBody } }); this.sendPending(true);
  }
  previewReview(): void {
    const w = this.lifecycle();
    if (this.busy() || this.pending() || !w?.canReview || w.makerId === this.userId() || !uuid(w.submissionId)) return;
    const client = this.clientId(), invoice = this.invoiceId(), actor = this.userId(), generation = this.session.invalidation(), request = ++this.request;
    this.busy.set(true); this.clearPreviews(); this.error.set(''); this.decision = ''; this.reviewReason = '';
    this.operation = this.http.get<unknown>(`${this.base(client)}/sales-invoice-submissions/${w.submissionId}/preview`).pipe(timeout(15000)).subscribe({
      next: value => { if (!this.valid(client, invoice, actor, generation, request)) return; this.busy.set(false); try { this.reviewPreview.set(decodeInvoiceReviewPreview(value, w)); } catch { this.error.set('Review preview did not match this exact submitted revision. Refresh the lifecycle.'); } },
      error: e => this.failure(e, client, invoice, actor, generation, request, 'Submitted review context unavailable. Refresh the lifecycle.')
    });
  }
  review(): void {
    const w = this.lifecycle(), p = this.reviewPreview();
    if (this.busy() || this.pending() || !w?.canReview || w.makerId === this.userId() || !p || !this.reviewAssent() || !['APPROVE', 'RETURN'].includes(this.decision) || !this.reviewReason.trim() || this.decision === 'APPROVE' && !p.canPost) return;
    this.pending.set({ client: this.clientId(), invoice: this.invoiceId(), actor: this.userId(), kind: 'REVIEW', body: { commandId: crypto.randomUUID(), submissionId: p.submissionId, decision: this.decision, reason: this.reviewReason.trim(), previewDigest: p.digest, reviewed: true } }); this.sendPending(true);
  }
  sendPending(initial = false): void {
    const p = this.pending();
    if (!p || this.busy() || p.client !== this.clientId() || p.invoice !== this.invoiceId() || p.actor !== this.userId() || !initial && (!this.retry() || !this.retryAssent())) return;
    const generation = this.session.invalidation(), request = ++this.request;
    this.busy.set(true); this.retry.set(false); this.retryAssent.set(false); this.submitAssent.set(false); this.reviewAssent.set(false); this.error.set('');
    const path = p.kind === 'SUBMIT' ? `/sales-invoices/${p.invoice}/submit` : '/sales-invoice-reviews';
    this.operation = this.http.post<unknown>(this.base(p.client) + path, p.body).pipe(timeout(15000)).subscribe({
      next: value => this.accept(value, p, generation, request),
      error: e => { if (!this.valid(p.client, p.invoice, p.actor, generation, request)) return; this.busy.set(false); if (e.status === 400 || e.status === 403) { this.pending.set(null); this.clearPreviews(); this.setLifecycle(null); this.error.set('Invoice command refused. Refresh the lifecycle and review current authority, revision and source context.'); } else this.error.set('Invoice command outcome is unconfirmed. Recover the original request.'); if (e.status === 401) this.session.clear(); }
    });
  }
  recover(): void {
    const p = this.pending(); if (!p || this.busy() || p.client !== this.clientId() || p.invoice !== this.invoiceId() || p.actor !== this.userId()) return;
    const generation = this.session.invalidation(), request = ++this.request; this.busy.set(true); this.retry.set(false); this.retryAssent.set(false);
    this.operation = this.http.get<unknown>(`${this.base(p.client)}/sales-invoice-requests/${p.body['commandId']}`, { params: { kind: p.kind } }).pipe(timeout(15000)).subscribe({
      next: value => this.accept(value, p, generation, request),
      error: e => { if (!this.valid(p.client, p.invoice, p.actor, generation, request)) return; this.busy.set(false); if (e.status === 404) { this.retry.set(true); this.error.set('No original command outcome was found. Explicitly review and retry the unchanged request if appropriate.'); } else this.error.set('Recovery unavailable. The original command remains locked.'); if (e.status === 401) this.session.clear(); }
    });
  }
  private accept(value: unknown, p: Pending, generation: number, request: number): void {
    if (!this.valid(p.client, p.invoice, p.actor, generation, request)) return;
    this.busy.set(false);
    try { this.receipt.set(decodeReceipt(value, p)); this.pending.set(null); this.retry.set(false); this.retryAssent.set(false); this.clearPreviews(); this.refresh(); }
    catch { this.error.set('Invoice response did not match the original command. Recover it before another action.'); }
  }
  private failure(e: { status: number }, client: string, invoice: string, actor: string, generation: number, request: number, message: string): void {
    if (!this.valid(client, invoice, actor, generation, request)) return;
    this.busy.set(false); this.error.set(message); if (e.status === 401) this.session.clear();
  }
}
