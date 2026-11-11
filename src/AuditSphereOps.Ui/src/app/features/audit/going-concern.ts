import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { goingConcernRecord, goingConcernState, goingConcernPreview, goingConcernReceipt, goingConcernLookup } from './going-concern-contracts';

type RecordView = ReturnType<typeof goingConcernRecord>;
type State = ReturnType<typeof goingConcernState>;
type Preview = ReturnType<typeof goingConcernPreview>;
type Receipt = ReturnType<typeof goingConcernReceipt>;
type Action = 'PREPARE' | 'REVIEW';
type Fields = Pick<RecordView, 'procedureId' | 'assessmentDate' | 'periodCoveredTo' | 'forecastReviewOutcome' |
  'materialUncertaintyIdentified' | 'disclosureAdequate' | 'currency' | 'rationale' | 'evidenceReferences'>;
type Pending = { version: 1; firmId: string; actorId: string; actorEpoch: string; engagementId: string; clientId: string;
  assessmentDate: string; requestId: string; requestHash: string; action: Action; assessmentId: string | null;
  expectedRevision: string; reviewBasis: string; basisHash: string | null; contentHash: string };
type Intent = { body: { requestId: string; expectedRevision: string; reviewBasis: string; fields: Fields | null };
  preview: Preview; pending: Pending; draft: string; state: State };
const hashPattern = /^[a-f0-9]{64}$/;
const long = (s: string) => /^(0|[1-9][0-9]{0,18})$/.test(s) && BigInt(s) <= 9223372036854775807n;
const trim = (s: string) => s.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
const validDate = (s: string) => /^\d{4}-\d{2}-\d{2}$/.test(s) && s >= '0001-01-01' &&
  Number.isFinite(Date.parse(s + 'T00:00:00Z')) && new Date(s + 'T00:00:00Z').toISOString().slice(0, 10) === s;
const emptyForm = () => ({ assessmentDate: '', periodCoveredTo: '', procedureId: '', forecastReviewOutcome: '',
  materialUncertaintyIdentified: '', disclosureAdequate: '', currency: '', rationale: '', evidenceReferences: '' });
const digest = async (value: unknown): Promise<string> => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',
  new TextEncoder().encode(JSON.stringify(value)))), byte => byte.toString(16).padStart(2, '0')).join('');
function content(record: Fields) {
  return [record.procedureId?.toLowerCase() ?? null, record.assessmentDate, record.periodCoveredTo, record.forecastReviewOutcome,
    record.materialUncertaintyIdentified, record.disclosureAdequate, record.currency, record.rationale, record.evidenceReferences];
}

@Component({
  selector: 'audit-going-concern', imports: [FormsModule, MatButtonModule], templateUrl: './going-concern.html',
})
export class GoingConcern {
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
  private key(): string { const o = this.owner(); return o ? `auditsphere-going-concern-pending-v1:${o.firmId}:${o.actorId}:${o.engagementId}` : ''; }
  private current(operation: number, context: string, date: string): boolean {
    return !this.destroyed && operation === this.operation && !!context && context === this.context &&
      context === this.identity() && date === this.form.assessmentDate;
  }
  private invalidate(clearState: boolean): void {
    ++this.operation; this.intent = null; this.preview.set(null); this.assent = false; this.busy.set(false); this.error.set('');
    if (clearState) this.state.set(null);
  }
  selectionChanged(): void { this.invalidate(true); this.history.set(''); const date = this.form.assessmentDate; Object.assign(this.form, emptyForm(), { assessmentDate: date }); }
  inputsChanged(): void { this.invalidate(false); }
  private readPending(): void {
    try {
      const raw = sessionStorage.getItem(this.key());
      if (raw === null) return;
      if (raw.length > 4096) throw new Error('Unbounded recovery reference');
      const p: Pending = JSON.parse(raw), owner = this.owner();
      if (!p || p.version !== 1 || !owner || p.firmId !== owner.firmId || p.actorId !== owner.actorId || p.engagementId !== owner.engagementId ||
        !long(p.actorEpoch) || !long(p.expectedRevision) || !guidPattern.test(p.clientId) || !guidPattern.test(p.requestId) ||
        !validDate(p.assessmentDate) || !hashPattern.test(p.requestHash) || !hashPattern.test(p.reviewBasis) || !hashPattern.test(p.contentHash) ||
        !['PREPARE', 'REVIEW'].includes(p.action) || (p.assessmentId !== null && !guidPattern.test(p.assessmentId)) ||
        (p.basisHash !== null && !hashPattern.test(p.basisHash)) || (p.assessmentId === null) !== (p.basisHash === null) ||
        (p.assessmentId === null && p.expectedRevision !== '0') || (p.action === 'REVIEW' && !p.assessmentId)) throw new Error('Invalid recovery reference');
      this.pending.set(p);
    } catch { this.unavailable.set(true); this.error.set('The recovery reference cannot be verified. No replacement request will be sent.'); }
  }
  async loadPending(): Promise<void> {
    if (this.busy()) return;
    const p = this.pending(); if (!p) return;
    this.form.assessmentDate = p.assessmentDate; this.selectionChanged(); await this.load();
  }
  private validRecord(r: RecordView, state: Pick<State, 'clientId' | 'engagementId' | 'assessmentDate'>): boolean {
    return r.clientId.toLowerCase() === state.clientId.toLowerCase() && r.engagementId.toLowerCase() === state.engagementId.toLowerCase() &&
      r.assessmentDate === state.assessmentDate && validDate(r.assessmentDate) && validDate(r.periodCoveredTo) && r.periodCoveredTo >= r.assessmentDate &&
      long(r.revision) && (r.reviewedRevision === null || long(r.reviewedRevision)) &&
      (r.reviewedByUserId === null ? r.reviewedAt === null && r.reviewedRevision === null :
        r.reviewedAt !== null && r.reviewedRevision !== null && long(r.reviewedRevision) &&
        r.reviewedByUserId.toLowerCase() !== r.recordedByUserId.toLowerCase() && BigInt(r.revision) === BigInt(r.reviewedRevision) + 1n) &&
      /^[A-Z]{3}$/.test(r.currency) &&
      ['NOT_ASSESSED', 'NO_MATERIAL_UNCERTAINTY', 'MATERIAL_UNCERTAINTY_DISCLOSED', 'INADEQUATE_DISCLOSURE'].includes(r.conclusion);
  }
  async load(): Promise<boolean> {
    const owner = this.owner(), date = this.form.assessmentDate;
    if (!owner || !validDate(date) || this.busy()) return false;
    this.invalidate(true);
    const op = this.operation, context = this.identity(); this.busy.set(true);
    try {
      const state = await this.api.get(`${this.base()}/state?assessmentDate=${date}`, goingConcernState);
      if (!this.current(op, context, date)) return false;
      if (state.engagementId.toLowerCase() !== owner.engagementId || state.assessmentDate !== date || !long(state.expectedRevision) ||
        !hashPattern.test(state.reviewBasis) || state.expectedRevision !== (state.record?.revision ?? '0') ||
        state.record && !this.validRecord(state.record, state)) throw new Error('The exact assessment basis could not be verified.');
      this.state.set(state); return true;
    } catch (e) { if (this.current(op, context, date)) this.error.set(e instanceof Error ? e.message : 'Assessment state unavailable.'); return false; }
    finally { if (this.current(op, context, date)) this.busy.set(false); }
  }
  private base(): string { return `/api/ui/engagements/${this.owner()!.engagementId}/completion/going-concern-assessments`; }
  private fields(): Fields | null {
    const f = this.form, evidence = f.evidenceReferences.split(/\r?\n/).map(trim).filter(Boolean);
    if (!validDate(f.assessmentDate) || !validDate(f.periodCoveredTo) || f.periodCoveredTo < f.assessmentDate ||
      !['true', 'false'].includes(f.materialUncertaintyIdentified) || !['true', 'false'].includes(f.disclosureAdequate) ||
      !trim(f.forecastReviewOutcome) || trim(f.forecastReviewOutcome).length > 4000 || !trim(f.rationale) || trim(f.rationale).length > 4000 ||
      !/^[A-Za-z]{3}$/.test(trim(f.currency)) || trim(f.procedureId) && !guidPattern.test(trim(f.procedureId)) ||
      evidence.length < 1 || evidence.length > 100 || evidence.some(x => x.length > 2000) || new Set(evidence).size !== evidence.length ||
      JSON.stringify(evidence).length > 20000) return null;
    return { procedureId: trim(f.procedureId).toLowerCase() || null, assessmentDate: f.assessmentDate, periodCoveredTo: f.periodCoveredTo,
      forecastReviewOutcome: trim(f.forecastReviewOutcome), materialUncertaintyIdentified: f.materialUncertaintyIdentified === 'true',
      disclosureAdequate: f.disclosureAdequate === 'true', currency: trim(f.currency).toUpperCase(), rationale: trim(f.rationale), evidenceReferences: evidence };
  }
  canPreview(action: Action): boolean {
    const s = this.state(); return !!s && s.assessmentDate === this.form.assessmentDate && s.engagementId.toLowerCase() === this.owner()?.engagementId &&
      !this.busy() && !this.pending() && !this.unavailable() && !s.blockers.includes('going-concern:ambiguous-assessment') &&
      (action === 'PREPARE' ? s.canPrepare && !!this.fields() : s.canReview && !!s.record &&
        !['NOT_ASSESSED', 'INADEQUATE_DISCLOSURE'].includes(s.record.conclusion) && s.record.recordedByUserId.toLowerCase() !== this.owner()?.actorId);
  }
  async preparePreview(action: Action): Promise<void> {
    if (!this.canPreview(action)) return;
    const state = this.state()!, owner = this.owner()!, date = this.form.assessmentDate, draft = JSON.stringify(this.form);
    const body = { requestId: crypto.randomUUID(), expectedRevision: state.expectedRevision, reviewBasis: state.reviewBasis,
      fields: action === 'PREPARE' ? this.fields() : null };
    this.invalidate(false); const op = this.operation, context = this.identity(); this.busy.set(true);
    try {
      const response = await this.api.command(`${this.base()}/${action === 'PREPARE' ? 'prepare' : `${state.record!.id}/review`}/preview`, body);
      if (!this.current(op, context, date) || draft !== JSON.stringify(this.form)) return;
      if (!response.ok) throw new Error(response.message);
      const preview = decode(goingConcernPreview, response.value);
      if (preview.requestId !== body.requestId || preview.action !== action || preview.reviewBasis !== body.reviewBasis ||
        preview.expectedRevision !== body.expectedRevision || !hashPattern.test(preview.requestHash) ||
        JSON.stringify(preview.currentRecord) !== JSON.stringify(state.record)) throw new Error('The preview does not match the selected assessment and exact intent.');
      const basisHash = state.record ? await digest(state.record) : null;
      if (!this.current(op, context, date) || draft !== JSON.stringify(this.form)) return;
      const contentHash = await digest(content(body.fields ?? state.record!));
      if (!this.current(op, context, date) || draft !== JSON.stringify(this.form)) return;
      const pending: Pending = { version: 1, ...owner, clientId: state.clientId.toLowerCase(), assessmentDate: date,
        requestId: body.requestId, requestHash: preview.requestHash, action, assessmentId: state.record?.id.toLowerCase() ?? null,
        expectedRevision: state.expectedRevision, reviewBasis: state.reviewBasis, basisHash, contentHash };
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
      (basis?.id.toLowerCase() ?? null) !== p.assessmentId || (basis?.revision ?? '0') !== p.expectedRevision ||
      !this.validRecord(result, p) || r.goingConcernAssessmentId.toLowerCase() !== result.id.toLowerCase() ||
      p.assessmentId !== null && result.id.toLowerCase() !== p.assessmentId || BigInt(result.revision) !== BigInt(p.expectedRevision) + 1n ||
      (p.action === 'PREPARE' ? result.recordedByUserId.toLowerCase() !== owner.actorId || result.reviewedByUserId !== null || result.reviewedAt !== null || result.reviewedRevision !== null
        : result.recordedByUserId.toLowerCase() === owner.actorId || result.reviewedByUserId?.toLowerCase() !== owner.actorId ||
          result.reviewedRevision !== p.expectedRevision || result.reviewedAt === null || ['NOT_ASSESSED', 'INADEQUATE_DISCLOSURE'].includes(result.conclusion))) return false;
    const basisHash = basis ? await digest(basis) : null;
    if (!check() || basisHash !== p.basisHash) return false;
    const contentHash = await digest(content(result));
    if (!check() || contentHash !== p.contentHash) return false;
    if (p.action === 'REVIEW') return !!basis && result.conclusion === basis.conclusion && result.recordedByUserId === basis.recordedByUserId && result.recordedAt === basis.recordedAt;
    // Verify the server's documented derivation only after explicit choices have been bound to the receipt.
    return result.conclusion === (!result.materialUncertaintyIdentified ? 'NO_MATERIAL_UNCERTAINTY' :
      result.disclosureAdequate ? 'MATERIAL_UNCERTAINTY_DISCLOSED' : 'INADEQUATE_DISCLOSURE');
  }
  private clearReference(key: string, raw: string): void {
    if (sessionStorage.getItem(key) !== raw) throw new Error('Recovery reference changed. It was not cleared.');
    sessionStorage.removeItem(key);
    if (sessionStorage.getItem(key) !== null) throw new Error('Recovery reference could not be cleared.');
    this.pending.set(null);
  }
  private async confirmed(p: Pending, key: string, raw: string, op: number, context: string): Promise<void> {
    if (!this.current(op, context, p.assessmentDate)) return;
    this.clearReference(key, raw); this.intent = null; this.preview.set(null); this.assent = false; this.busy.set(false);
    const message = `Confirmed ${p.action.toLowerCase()} receipt for request ${p.requestId}.`;
    this.history.set(`${message} Current state has not yet refreshed.`);
    const refreshOperation = this.operation + 1;
    const loaded = await this.load();
    if (!this.current(refreshOperation, context, p.assessmentDate)) return;
    this.history.set(`${message} ${loaded ? 'Exact assessment state refreshed. Completion horizon and unrelated gates still apply.' : 'Current state could not be refreshed. Do not rely on earlier approval.'}`);
    if (loaded) this.refreshed.emit();
  }
  async submit(): Promise<void> {
    const intent = this.intent;
    if (!intent || !this.assent || this.busy() || this.pending() || this.unavailable() || !intent.preview.canProceed || intent.preview.blockers.length ||
      intent.state !== this.state() || intent.draft !== JSON.stringify(this.form) || this.context !== this.identity() ||
      !this.canPreview(intent.pending.action)) return;
    const p = intent.pending, key = this.key(), raw = JSON.stringify(p), op = ++this.operation, context = this.identity();
    const check = () => this.current(op, context, p.assessmentDate);
    this.busy.set(true); this.error.set('');
    try {
      if (sessionStorage.getItem(key) !== null) throw new Error('An unresolved request already exists. No replacement request was sent.');
      sessionStorage.setItem(key, raw);
      if (sessionStorage.getItem(key) !== raw) throw new Error('The recovery reference could not be verified. No command was sent.');
      this.pending.set(p); this.state.set(null); this.preview.set(null); this.intent = null; this.assent = false;
      const response = await this.api.command(`${this.base()}/${p.action === 'PREPARE' ? 'prepare' : `${p.assessmentId}/review`}`,
        { ...intent.body, expectedRequestHash: p.requestHash, reviewed: true });
      if (!check()) return;
      if (!response.ok) {
        // Only a known first-send refusal permits replacement. Unknown and idempotency conflicts remain recoverable.
        const definitive = !response.unknown && ((response.status === 400 && ['mapping.invalid', 'gate.blocked', 'revision.stale', 'generation.stale'].includes(response.code)) ||
          response.status === 403 && response.code === 'scope.denied' || response.status === 409 && ['revision.stale', 'generation.stale'].includes(response.code));
        if (definitive) { this.clearReference(key, raw); this.error.set(`${response.message} Load the current basis before preparing a new intent.`); return; }
        throw new Error('Execution is unconfirmed. Look up the exact saved request; no replacement will be sent.');
      }
      const matches = await this.receiptMatches(decode(goingConcernReceipt, response.value), p, check);
      if (!check()) return;
      if (!matches) throw new Error('The response does not confirm this exact request. Its recovery reference remains saved.');
      await this.confirmed(p, key, raw, op, context);
    } catch (e) { if (check()) this.error.set(e instanceof Error ? e.message : 'The command could not be confirmed.'); }
    finally { if (check()) this.busy.set(false); }
  }
  async recover(): Promise<void> {
    const p = this.pending(), state = this.state(), owner = this.owner();
    if (!p || !state || !owner || this.busy() || this.unavailable() || state.assessmentDate !== p.assessmentDate ||
      state.clientId.toLowerCase() !== p.clientId || p.actorEpoch !== owner.actorEpoch) {
      this.error.set('Load the saved exact assessment scope in its original session lifetime before recovery.'); return;
    }
    const key = this.key(), op = ++this.operation, context = this.identity(), check = () => this.current(op, context, p.assessmentDate);
    this.busy.set(true); this.error.set('');
    try {
      const raw = sessionStorage.getItem(key);
      if (!raw || raw !== JSON.stringify(p)) throw new Error('The saved exact recovery reference changed. No request was sent.');
      const lookup = await this.api.get(`${this.base()}/receipts/${p.requestId}?requestHash=${p.requestHash}`, goingConcernLookup);
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
