import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { openingBalanceRecord, openingBalanceState, openingBalancePreview, openingBalanceReceipt, openingBalanceLookup } from './opening-balances-contracts';

type RecordView = ReturnType<typeof openingBalanceRecord>;
type State = ReturnType<typeof openingBalanceState>;
type Preview = ReturnType<typeof openingBalancePreview>;
type Receipt = ReturnType<typeof openingBalanceReceipt>;
type Action = 'PREPARE' | 'REVIEW';
type Fields = Pick<RecordView, 'procedureId' | 'priorPeriodId' | 'priorReference' | 'asOfDate' | 'currency' |
  'openingSignedTotal' | 'agreedSignedTotal' | 'accountingPoliciesConsistent' | 'rationale' | 'evidenceReferences'>;
type Pending = { version: 1; firmId: string; actorId: string; actorEpoch: string; engagementId: string; clientId: string;
  asOfDate: string; requestId: string; requestHash: string; action: Action; verificationId: string | null;
  expectedRevision: string; reviewBasis: string; basisHash: string | null; contentHash: string; commentHash: string };
type Intent = { body: { requestId: string; expectedRevision: string; reviewBasis: string; fields: Fields | null; expectedVerificationId: string | null; comment: string | null };
  preview: Preview; pending: Pending; draft: string; state: State };
const hashPattern = /^[a-f0-9]{64}$/;
const long = (s: string) => typeof s === 'string' && /^(0|[1-9][0-9]{0,18})$/.test(s) && BigInt(s) <= 9223372036854775807n;
const trim = (s: string) => s.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
const validDate = (s: string) => /^\d{4}-\d{2}-\d{2}$/.test(s) && s >= '0001-01-01' &&
  Number.isFinite(Date.parse(s + 'T00:00:00Z')) && new Date(s + 'T00:00:00Z').toISOString().slice(0, 10) === s;
// Fixed six-place integer arithmetic preserves every monetary digit.
export function openingAmount(value: string): string | null {
  if (!/^-?\d{1,13}(\.\d{1,6})?$/.test(value)) return null;
  const negative = value.startsWith('-');
  const [whole, fraction = ''] = (negative ? value.slice(1) : value).split('.');
  const normalized = whole.replace(/^0+(?=\d)/, '') + (fraction.replace(/0+$/, '') ? '.' + fraction.replace(/0+$/, '') : '');
  return (negative && normalized !== '0' ? '-' : '') + normalized;
}
const units = (s: string) => { const [w, f = ''] = s.replace(/^-/, '').split('.'); return (s.startsWith('-') ? -1n : 1n) * BigInt(w + f.padEnd(6, '0')); };
const emptyForm = () => ({ asOfDate: '', procedureId: '', priorPeriodId: '', priorReference: '', openingSignedTotal: '',
  agreedSignedTotal: '', accountingPoliciesConsistent: '', currency: '', rationale: '', evidenceReferences: '', comment: '' });
const digest = async (value: unknown): Promise<string> => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',
  new TextEncoder().encode(JSON.stringify(value)))), byte => byte.toString(16).padStart(2, '0')).join('');
function content(record: Fields) {
  return [record.procedureId?.toLowerCase() ?? null, record.priorPeriodId?.toLowerCase() ?? null, record.priorReference,
    record.asOfDate, record.currency, openingAmount(record.openingSignedTotal), openingAmount(record.agreedSignedTotal),
    record.accountingPoliciesConsistent, record.rationale, record.evidenceReferences];
}

@Component({
  selector: 'audit-opening-balances', imports: [FormsModule, MatButtonModule], templateUrl: './opening-balances.html',
})
export class OpeningBalancesPanel {
  readonly engagementId = input.required<string>();
  readonly refreshed = output<void>();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly state = signal<State | null>(null);
  readonly preview = signal<Preview | null>(null);
  readonly pending = signal<Pending | null>(null);
  readonly busy = signal(false);
  readonly unavailable = signal(false);
  readonly error = signal('');
  readonly history = signal('');
  readonly form = emptyForm();
  assent = false;
  private operation = 0;
  private context = '';
  private destroyed = false;
  private intent: Intent | null = null;

  constructor() {
    effect(() => {
      const context = this.identity();
      untracked(() => {
        if (context === this.context) return;
        this.context = context; this.invalidate(true); this.history.set(''); Object.assign(this.form, emptyForm());
        this.pending.set(null); this.unavailable.set(false);
        if (context) this.readPending();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; ++this.operation; });
  }
  private owner() {
    const s = this.session.current(), engagementId = this.engagementId().toLowerCase();
    return s?.staff && guidPattern.test(engagementId) ? { firmId: s.firmId.toLowerCase(), actorId: s.userId.toLowerCase(),
      actorEpoch: s.generation, engagementId } : null;
  }
  private identity(): string { const o = this.owner(); return o ? JSON.stringify([o, this.session.invalidation()]) : ''; }
  private key(): string { const o = this.owner(); return o ? `auditsphere-opening-balances-pending-v1:${o.firmId}:${o.actorId}:${o.engagementId}` : ''; }
  private current(operation: number, context: string, date: string): boolean {
    return !this.destroyed && operation === this.operation && !!context && context === this.context &&
      context === this.identity() && date === this.form.asOfDate;
  }
  private invalidate(clearState: boolean): void {
    ++this.operation; this.intent = null; this.preview.set(null); this.assent = false; this.busy.set(false); this.error.set('');
    if (clearState) this.state.set(null);
  }
  selectionChanged(): void { this.invalidate(true); this.history.set(''); const date = this.form.asOfDate; Object.assign(this.form, emptyForm(), { asOfDate: date }); }
  inputsChanged(): void { this.invalidate(false); }
  private readPending(): void {
    try {
      const raw = sessionStorage.getItem(this.key());
      if (raw === null) return;
      if (raw.length > 4096) throw new Error('Unbounded recovery reference');
      const p: Pending = JSON.parse(raw), owner = this.owner();
      if (!p || p.version !== 1 || !owner || p.firmId !== owner.firmId || p.actorId !== owner.actorId || p.engagementId !== owner.engagementId ||
        !long(p.actorEpoch) || !long(p.expectedRevision) || !guidPattern.test(p.clientId) || !guidPattern.test(p.requestId) ||
        !validDate(p.asOfDate) || !hashPattern.test(p.requestHash) || !hashPattern.test(p.reviewBasis) || !hashPattern.test(p.contentHash) || !hashPattern.test(p.commentHash) ||
        !['PREPARE', 'REVIEW'].includes(p.action) || (p.verificationId !== null && !guidPattern.test(p.verificationId)) ||
        (p.basisHash !== null && !hashPattern.test(p.basisHash)) || (p.verificationId === null) !== (p.basisHash === null) ||
        (p.verificationId === null && p.expectedRevision !== '0') || (p.action === 'REVIEW' && !p.verificationId)) throw new Error('Invalid recovery reference');
      this.pending.set(p);
    } catch { this.unavailable.set(true); this.error.set('The recovery reference cannot be verified. No replacement request will be sent.'); }
  }
  async loadPending(): Promise<void> {
    if (this.busy()) return;
    const p = this.pending(); if (!p) return;
    this.form.asOfDate = p.asOfDate; this.selectionChanged(); await this.load();
  }
  private validRecord(r: RecordView, state: Pick<State, 'engagementId' | 'asOfDate'>): boolean {
    const opening = openingAmount(r.openingSignedTotal), agreed = openingAmount(r.agreedSignedTotal);
    if (opening === null || agreed === null || !/^-?\d{1,14}(\.\d{1,6})?$/.test(r.differenceAmount)) return false;
    const zeroDifference = units(r.differenceAmount) === 0n;
    const resolved = zeroDifference && r.conclusion !== 'NOT_VERIFIABLE';
    const currentReview = r.reviewedByUserId !== null && r.reviewedAt !== null && r.reviewedBasisRevision !== null &&
      long(r.reviewedBasisRevision) && BigInt(r.reviewedBasisRevision) > 0n && long(r.revision) && BigInt(r.revision) === BigInt(r.reviewedBasisRevision) + 1n &&
      r.reviewedByUserId.toLowerCase() !== r.recordedByUserId.toLowerCase() && resolved;
    return r.engagementId.toLowerCase() === state.engagementId.toLowerCase() && r.asOfDate === state.asOfDate && validDate(r.asOfDate) &&
      long(r.revision) && BigInt(r.revision) > 0n && (r.reviewedBasisRevision === null || long(r.reviewedBasisRevision)) &&
      /^[A-Z]{3}$/.test(r.currency) && units(opening) - units(agreed) === units(r.differenceAmount) &&
      r.comparisonResolved === resolved && r.currentIndependentReview === currentReview &&
      (r.conclusion === 'NOT_VERIFIABLE' || r.conclusion === (!zeroDifference ? 'DIFFERENCES_UNRESOLVED' : r.accountingPoliciesConsistent ? 'AGREED' : 'DIFFERENCES_RESOLVED'));
  }
  async load(): Promise<boolean> {
    const owner = this.owner(), date = this.form.asOfDate;
    if (!owner || !validDate(date) || this.busy()) return false;
    this.invalidate(true);
    const op = this.operation, context = this.identity(); this.busy.set(true);
    try {
      const state = await this.api.get(`${this.base()}/state?asOfDate=${date}`, openingBalanceState);
      if (!this.current(op, context, date)) return false;
      if (state.engagementId.toLowerCase() !== owner.engagementId || state.asOfDate !== date || !long(state.expectedRevision) ||
        !hashPattern.test(state.reviewBasis) || state.expectedRevision !== (state.record?.revision ?? '0') ||
        state.record && !this.validRecord(state.record, state)) throw new Error('The exact verification basis could not be verified.');
      this.state.set(state);
      if (state.record) { const r = state.record; Object.assign(this.form, { procedureId: r.procedureId ?? '', priorPeriodId: r.priorPeriodId ?? '',
        priorReference: r.priorReference, currency: r.currency, openingSignedTotal: r.openingSignedTotal, agreedSignedTotal: r.agreedSignedTotal,
        accountingPoliciesConsistent: String(r.accountingPoliciesConsistent), rationale: r.rationale, evidenceReferences: r.evidenceReferences.join('\n'), comment: '' }); }
      return true;
    } catch (e) { if (this.current(op, context, date)) this.error.set(e instanceof Error ? e.message : 'Opening-balance state unavailable.'); return false; }
    finally { if (this.current(op, context, date)) this.busy.set(false); }
  }
  private base(): string { return `/api/ui/engagements/${this.owner()!.engagementId}/completion/opening-balances`; }
  private fields(): Fields | null {
    const f = this.form, evidence = f.evidenceReferences.split(/\r?\n/).map(trim).filter(Boolean);
    const opening = openingAmount(trim(f.openingSignedTotal)), agreed = openingAmount(trim(f.agreedSignedTotal));
    if (!validDate(f.asOfDate) || !['true', 'false'].includes(f.accountingPoliciesConsistent) || opening === null || agreed === null ||
      !trim(f.priorReference) || trim(f.priorReference).length > 200 || !trim(f.rationale) || trim(f.rationale).length > 2000 ||
      !/^[A-Za-z]{3}$/.test(trim(f.currency)) || trim(f.procedureId) && !guidPattern.test(trim(f.procedureId)) ||
      trim(f.priorPeriodId) && !guidPattern.test(trim(f.priorPeriodId)) || evidence.length < 1 || evidence.length > 10000 ||
      JSON.stringify(evidence).length > 20000) return null;
    return { procedureId: trim(f.procedureId).toLowerCase() || null, priorPeriodId: trim(f.priorPeriodId).toLowerCase() || null,
      priorReference: trim(f.priorReference), asOfDate: f.asOfDate, currency: trim(f.currency).toUpperCase(),
      openingSignedTotal: opening, agreedSignedTotal: agreed, accountingPoliciesConsistent: f.accountingPoliciesConsistent === 'true',
      rationale: trim(f.rationale), evidenceReferences: evidence };
  }
  canPreview(action: Action): boolean {
    const s = this.state(); return !!s && s.asOfDate === this.form.asOfDate && s.engagementId.toLowerCase() === this.owner()?.engagementId &&
      !this.busy() && !this.pending() && !this.unavailable() &&
      (action === 'PREPARE' ? s.canPrepare && !!this.fields() : s.canReview && !!s.record &&
        s.record.comparisonResolved && !s.record.currentIndependentReview && this.form.comment.length <= 2000 && s.record.recordedByUserId.toLowerCase() !== this.owner()?.actorId);
  }
  async preparePreview(action: Action): Promise<void> {
    if (!this.canPreview(action)) return;
    const state = this.state()!, owner = this.owner()!, date = this.form.asOfDate, draft = JSON.stringify(this.form);
    const body = { requestId: crypto.randomUUID(), expectedRevision: state.expectedRevision, reviewBasis: state.reviewBasis,
      fields: action === 'PREPARE' ? this.fields() : null, expectedVerificationId: state.record?.id ?? null,
      comment: action === 'REVIEW' ? trim(this.form.comment) || null : null };
    this.invalidate(false); const op = this.operation, context = this.identity(); this.busy.set(true);
    try {
      const response = await this.api.command(`${this.base()}/${action === 'PREPARE' ? 'prepare' : `${state.record!.id}/review`}/preview`, body);
      if (!this.current(op, context, date) || draft !== JSON.stringify(this.form)) return;
      if (!response.ok) throw new Error(response.message);
      const preview = decode(openingBalancePreview, response.value);
      if (preview.requestId !== body.requestId || preview.action !== action || preview.reviewBasis !== body.reviewBasis ||
        preview.expectedRevision !== body.expectedRevision || !hashPattern.test(preview.requestHash) ||
        JSON.stringify(preview.currentRecord) !== JSON.stringify(state.record)) throw new Error('The preview does not match the selected verification and exact intent.');
      const basisHash = state.record ? await digest(state.record) : null;
      if (!this.current(op, context, date) || draft !== JSON.stringify(this.form)) return;
      const contentHash = await digest(content(body.fields ?? state.record!));
      if (!this.current(op, context, date) || draft !== JSON.stringify(this.form)) return;
      const commentHash = await digest(action === 'PREPARE' ? state.record?.reviewComment ?? null : body.comment);
      if (!this.current(op, context, date) || draft !== JSON.stringify(this.form)) return;
      const pending: Pending = { version: 1, ...owner, clientId: state.clientId.toLowerCase(), asOfDate: date,
        requestId: body.requestId, requestHash: preview.requestHash, action, verificationId: state.record?.id.toLowerCase() ?? null,
        expectedRevision: state.expectedRevision, reviewBasis: state.reviewBasis, basisHash, contentHash, commentHash };
      this.intent = { body, preview, pending, draft, state }; this.preview.set(preview);
    } catch (e) { if (this.current(op, context, date)) this.error.set(e instanceof Error ? e.message : 'Preview unavailable.'); }
    finally { if (this.current(op, context, date)) this.busy.set(false); }
  }
  private async receiptMatches(r: Receipt, p: Pending, check: () => boolean): Promise<boolean> {
    const owner = this.owner(), result = r.result, basis = r.preview.currentRecord;
    if (!check() || !owner || p.actorEpoch !== owner.actorEpoch || r.actorEpoch !== p.actorEpoch || r.actorId.toLowerCase() !== owner.actorId ||
      r.engagementId.toLowerCase() !== p.engagementId || r.requestId !== p.requestId || r.requestHash !== p.requestHash || r.action !== p.action ||
      r.preview.requestId !== p.requestId || r.preview.requestHash !== p.requestHash || r.preview.action !== p.action ||
      !r.preview.canProceed || r.preview.blockers.length !== 0 || r.preview.reviewBasis !== p.reviewBasis || r.preview.expectedRevision !== p.expectedRevision ||
      (basis?.id.toLowerCase() ?? null) !== p.verificationId || (basis?.revision ?? '0') !== p.expectedRevision ||
      !this.validRecord(result, p) || r.verificationId.toLowerCase() !== result.id.toLowerCase() ||
      p.verificationId !== null && result.id.toLowerCase() !== p.verificationId || BigInt(result.revision) !== BigInt(p.expectedRevision) + 1n ||
      (p.action === 'PREPARE' ? result.recordedByUserId.toLowerCase() !== owner.actorId || result.reviewedByUserId !== null || result.reviewedAt !== null || result.reviewedBasisRevision !== (basis?.reviewedBasisRevision ?? null) || result.reviewComment !== (basis?.reviewComment ?? null) || result.currentIndependentReview
        : result.recordedByUserId.toLowerCase() === owner.actorId || result.reviewedByUserId?.toLowerCase() !== owner.actorId ||
          result.reviewedBasisRevision !== p.expectedRevision || result.reviewedAt === null || !result.comparisonResolved || !result.currentIndependentReview)) return false;
    const basisHash = basis ? await digest(basis) : null;
    if (!check() || basisHash !== p.basisHash) return false;
    const contentHash = await digest(content(result));
    if (!check() || contentHash !== p.contentHash) return false;
    const commentHash = await digest(result.reviewComment);
    if (!check() || commentHash !== p.commentHash) return false;
    if (p.action === 'REVIEW') return !!basis && result.conclusion === basis.conclusion && result.recordedByUserId === basis.recordedByUserId && result.recordedAt === basis.recordedAt;
    return !result.currentIndependentReview;

  }
  private clearReference(key: string, raw: string): void {
    if (sessionStorage.getItem(key) !== raw) throw new Error('Recovery reference changed. It was not cleared.');
    sessionStorage.removeItem(key);
    if (sessionStorage.getItem(key) !== null) throw new Error('Recovery reference could not be cleared.');
    this.pending.set(null);
  }
  private async confirmed(p: Pending, key: string, raw: string, op: number, context: string): Promise<void> {
    if (!this.current(op, context, p.asOfDate)) return;
    this.clearReference(key, raw); this.intent = null; this.preview.set(null); this.assent = false; this.busy.set(false);
    const message = `Confirmed ${p.action.toLowerCase()} receipt for request ${p.requestId}.`;
    this.history.set(`${message} Current state has not yet refreshed.`);
    const refreshOperation = this.operation + 1;
    const loaded = await this.load();
    if (!this.current(refreshOperation, context, p.asOfDate)) return;
    this.history.set(`${message} ${loaded ? 'Exact verification state refreshed. Comparison and independent review remain separate; unrelated gates still apply.' : 'Current state could not be refreshed. Do not rely on earlier approval.'}`);
    if (loaded) this.refreshed.emit();
  }
  async submit(): Promise<void> {
    const intent = this.intent;
    if (!intent || !this.assent || this.busy() || this.pending() || this.unavailable() || !intent.preview.canProceed || intent.preview.blockers.length ||
      intent.state !== this.state() || intent.draft !== JSON.stringify(this.form) || this.context !== this.identity() ||
      !this.canPreview(intent.pending.action)) return;
    const p = intent.pending, key = this.key(), raw = JSON.stringify(p), op = ++this.operation, context = this.identity();
    const check = () => this.current(op, context, p.asOfDate);
    this.busy.set(true); this.error.set('');
    try {
      if (sessionStorage.getItem(key) !== null) throw new Error('An unresolved request already exists. No replacement request was sent.');
      sessionStorage.setItem(key, raw);
      if (sessionStorage.getItem(key) !== raw) throw new Error('The recovery reference could not be verified. No command was sent.');
      this.pending.set(p); this.state.set(null); this.preview.set(null); this.intent = null; this.assent = false;
      const response = await this.api.command(`${this.base()}/${p.action === 'PREPARE' ? 'prepare' : `${p.verificationId}/review`}`,
        { ...intent.body, expectedRequestHash: p.requestHash, reviewed: true });
      if (!check()) return;
      if (!response.ok) {
        // Only a known first-send refusal permits replacement. Unknown and idempotency conflicts remain recoverable.
        const definitive = !response.unknown && ((response.status === 400 && ['mapping.invalid', 'gate.blocked', 'revision.stale', 'generation.stale'].includes(response.code)) ||
          response.status === 403 && response.code === 'scope.denied' || response.status === 409 && ['revision.stale', 'generation.stale'].includes(response.code));
        if (definitive) { this.clearReference(key, raw); this.error.set(`${response.message} Load the current basis before preparing a new intent.`); return; }
        throw new Error('Execution is unconfirmed. Look up the exact saved request; no replacement will be sent.');
      }
      const matches = await this.receiptMatches(decode(openingBalanceReceipt, response.value), p, check);
      if (!check()) return;
      if (!matches) throw new Error('The response does not confirm this exact request. Its recovery reference remains saved.');
      await this.confirmed(p, key, raw, op, context);
    } catch (e) { if (check()) this.error.set(e instanceof Error ? e.message : 'The command could not be confirmed.'); }
    finally { if (check()) this.busy.set(false); }
  }
  async recover(): Promise<void> {
    const p = this.pending(), state = this.state(), owner = this.owner();
    if (!p || !state || !owner || this.busy() || this.unavailable() || state.asOfDate !== p.asOfDate ||
      state.clientId.toLowerCase() !== p.clientId || p.actorEpoch !== owner.actorEpoch) {
      this.error.set('Load the saved exact verification scope in its original session lifetime before recovery.'); return;
    }
    const key = this.key(), op = ++this.operation, context = this.identity(), check = () => this.current(op, context, p.asOfDate);
    this.busy.set(true); this.error.set('');
    try {
      const raw = sessionStorage.getItem(key);
      if (!raw || raw !== JSON.stringify(p)) throw new Error('The saved exact recovery reference changed. No request was sent.');
      const lookup = await this.api.get(`${this.base()}/receipts/${p.requestId}?requestHash=${p.requestHash}`, openingBalanceLookup);
      if (!check()) return;
      if (!lookup.found || !lookup.receipt) throw new Error('No exact receipt is available. The request remains unresolved; no replacement will be sent.');
      const matches = await this.receiptMatches(lookup.receipt, p, check);
      if (!check()) return;
      if (!matches) throw new Error('The receipt does not match the exact retained intent. Its reference remains saved.');
      await this.confirmed(p, key, raw, op, context);
    } catch (e) { if (check()) this.error.set(e instanceof Error ? e.message : 'Recovery unavailable.'); }
    finally { if (check()) this.busy.set(false); }
  }
}
