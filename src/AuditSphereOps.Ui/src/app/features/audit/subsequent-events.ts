import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { subsequentEventRecord, subsequentEventState, subsequentEventPreview, subsequentEventReceipt, subsequentEventLookup, subsequentEventList } from './subsequent-events-contracts';

type RecordView = ReturnType<typeof subsequentEventRecord>;
type State = ReturnType<typeof subsequentEventState>;
type Preview = ReturnType<typeof subsequentEventPreview>;
type Receipt = ReturnType<typeof subsequentEventReceipt>;
type Action = 'PREPARE' | 'REVIEW';
type Fields = Pick<RecordView, 'procedureId' | 'periodEndDate' | 'eventDate' | 'description' | 'classification' |
  'adjustmentRequired' | 'disclosureRequired' | 'disclosureReference' | 'rationale' | 'currency' | 'financialEffect' | 'evidenceReferences'>;
type Pending = { version: 1; firmId: string; actorId: string; actorEpoch: string; engagementId: string; clientId: string;
  requestId: string; requestHash: string; action: Action; eventId: string | null;
  expectedRevision: string; reviewBasis: string; basisHash: string | null; contentHash: string };
type Intent = { body: { requestId: string; expectedRevision: string; reviewBasis: string; fields: Fields | null; selectedRecordId: string | null };
  preview: Preview; pending: Pending; draft: string; state: State };
const hashPattern = /^[a-f0-9]{64}$/;
const long = (s: unknown): s is string => typeof s === 'string' && /^(0|[1-9][0-9]{0,18})$/.test(s) && BigInt(s) <= 9223372036854775807n;
const trim = (s: string) => s.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
const validDate = (s: string) => /^\d{4}-\d{2}-\d{2}$/.test(s) && s >= '0001-01-01' &&
  Number.isFinite(Date.parse(s + 'T00:00:00Z')) && new Date(s + 'T00:00:00Z').toISOString().slice(0, 10) === s;
const emptyForm = () => ({ periodEndDate: '', eventDate: '', description: '', classification: '', procedureId: '',
  adjustmentRequired: '', disclosureRequired: '', disclosureReference: '', currency: '', rationale: '', financialEffect: '', evidenceReferences: '' });
const effectPattern = /^[+-]?[0-9]{1,13}(\.[0-9]{1,6})?$/;
const amount = (s: string | null): string | null => {
  if (s === null) return null;
  const negative = s.startsWith('-'), [whole, fraction = ''] = s.replace(/^[+-]/, '').split('.');
  const scaled = BigInt(whole) * 1000000n + BigInt(fraction.padEnd(6, '0'));
  return (negative && scaled !== 0n ? '-' : '') + scaled.toString();
};
const digest = async (value: unknown): Promise<string> => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',
  new TextEncoder().encode(JSON.stringify(value)))), byte => byte.toString(16).padStart(2, '0')).join('');
function content(record: Fields) {
  return [record.procedureId?.toLowerCase() ?? null, record.periodEndDate, record.eventDate, record.description, record.classification,
    record.adjustmentRequired, record.disclosureRequired, record.disclosureReference, record.rationale, record.currency,
    amount(record.financialEffect), record.evidenceReferences];
}

@Component({
  selector: 'audit-subsequent-events', imports: [FormsModule, MatButtonModule], templateUrl: './subsequent-events.html',
})
export class SubsequentEvents {
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
  readonly selectedId = signal<string | null>(null);
  readonly records = signal<RecordView[]>([]);
  readonly hasMore = signal(false);
  readonly pageOffset = signal(0);
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
        this.pending.set(null); this.unavailable.set(false); this.selectedId.set(null); this.records.set([]); this.hasMore.set(false); this.pageOffset.set(0);
        if (context) this.readPending();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; ++this.operation; });
  }
  private owner() {
    const s = this.session.current(), engagementId = this.engagementId().toLowerCase();
    return s?.staff && long(s.generation) && guidPattern.test(engagementId) ? { firmId: s.firmId.toLowerCase(), actorId: s.userId.toLowerCase(),
      actorEpoch: s.generation, engagementId } : null;
  }
  private identity(): string { const o = this.owner(); return o ? JSON.stringify([o, this.session.invalidation()]) : ''; }
  private key(): string { const o = this.owner(); return o ? `auditsphere-subsequent-events-pending-v1:${o.firmId}:${o.actorId}:${o.engagementId}` : ''; }
  private current(operation: number, context: string, selection: string | null): boolean {
    return !this.destroyed && operation === this.operation && !!context && context === this.context &&
      context === this.identity() && selection === this.selectedId();
  }
  private invalidate(clearState: boolean): void {
    ++this.operation; this.intent = null; this.preview.set(null); this.assent = false; this.busy.set(false); this.error.set('');
    if (clearState) this.state.set(null);
  }
  select(id: string | null): void {
    if (this.busy() || (id !== null && !guidPattern.test(id))) return;
    this.selectedId.set(id?.toLowerCase() ?? null); this.invalidate(true); this.history.set(''); Object.assign(this.form, emptyForm());
  }
  async loadList(offset = 0): Promise<void> {
    if (!this.owner() || this.busy() || !Number.isSafeInteger(offset) || offset < 0 || offset > 100000) return;
    this.invalidate(true); const op = this.operation, context = this.identity(), selection = this.selectedId(); this.busy.set(true);
    try {
      const scope = await this.api.get(`${this.base()}/state`, subsequentEventState);
      if (!this.current(op, context, selection)) return;
      if (scope.engagementId.toLowerCase() !== this.owner()!.engagementId || scope.selectedRecordId !== null || scope.record !== null ||
        scope.expectedRevision !== '0' || !hashPattern.test(scope.reviewBasis)) throw new Error('The event list scope could not be verified.');
      const list = await this.api.get(`${this.base()}?skip=${offset}`, subsequentEventList);
      if (!this.current(op, context, selection)) return;
      if (new Set(list.records.map(r => r.id.toLowerCase())).size !== list.records.length ||
        list.records.some(r => !this.validRecord(r, scope)))
        throw new Error('The event list could not be verified.');
      this.records.set(list.records); this.pageOffset.set(offset); this.hasMore.set(list.hasMore);
    } catch (e) { if (this.current(op, context, selection)) { this.records.set([]); this.hasMore.set(false); this.error.set(e instanceof Error ? e.message : 'Event list unavailable.'); } }
    finally { if (this.current(op, context, selection)) this.busy.set(false); }
  }
  inputsChanged(): void { this.invalidate(false); }
  private readPending(): void {
    try {
      const raw = sessionStorage.getItem(this.key());
      if (raw === null) return;
      if (raw.length > 4096) throw new Error('Unbounded recovery reference');
      const p: Pending = JSON.parse(raw), owner = this.owner();
      if (!p || typeof p !== 'object' || Array.isArray(p) || Object.keys(p).length !== 14 || p.version !== 1 || !owner || p.firmId !== owner.firmId || p.actorId !== owner.actorId || p.engagementId !== owner.engagementId ||
        !long(p.actorEpoch) || !long(p.expectedRevision) || typeof p.clientId !== 'string' || !guidPattern.test(p.clientId) || typeof p.requestId !== 'string' || !guidPattern.test(p.requestId) ||
        typeof p.requestHash !== 'string' || !hashPattern.test(p.requestHash) || typeof p.reviewBasis !== 'string' || !hashPattern.test(p.reviewBasis) || typeof p.contentHash !== 'string' || !hashPattern.test(p.contentHash) ||
        !['PREPARE', 'REVIEW'].includes(p.action) || (p.eventId !== null && (typeof p.eventId !== 'string' || !guidPattern.test(p.eventId))) ||
        (p.basisHash !== null && (typeof p.basisHash !== 'string' || !hashPattern.test(p.basisHash))) || (p.eventId === null) !== (p.basisHash === null) ||
        (p.eventId === null && p.expectedRevision !== '0') || (p.action === 'REVIEW' && !p.eventId)) throw new Error('Invalid recovery reference');
      this.pending.set(p);
    } catch { this.unavailable.set(true); this.error.set('The recovery reference cannot be verified. No replacement request will be sent.'); }
  }
  async loadPending(): Promise<void> {
    if (this.busy()) return;
    const p = this.pending(); if (!p) return;
    this.select(p.eventId); await this.load();
  }
  private validRecord(r: RecordView, scope: { clientId: string; engagementId: string }): boolean {
    return r.clientId.toLowerCase() === scope.clientId.toLowerCase() && r.engagementId.toLowerCase() === scope.engagementId.toLowerCase() &&
      validDate(r.periodEndDate) && validDate(r.eventDate) && r.eventDate >= r.periodEndDate &&
      long(r.revision) && r.revision !== '0' && (r.reviewedRevision === null || long(r.reviewedRevision)) &&
      (r.reviewedByUserId === null ? r.reviewedAt === null && r.reviewedRevision === null :
        r.reviewedAt !== null && r.reviewedRevision !== null && long(r.reviewedRevision) && r.reviewedRevision !== '0' &&
        r.classification !== 'PENDING_ASSESSMENT' && r.reviewedByUserId.toLowerCase() !== r.recordedByUserId.toLowerCase() &&
        BigInt(r.revision) === BigInt(r.reviewedRevision) + 1n) &&
      /^[A-Z]{3}$/.test(r.currency) && !!trim(r.description) && r.description === trim(r.description) &&
      (r.financialEffect === null || effectPattern.test(r.financialEffect)) &&
      (r.classification === 'PENDING_ASSESSMENT' || !!r.rationale && !!trim(r.rationale)) &&
      (r.classification !== 'ADJUSTING' || r.adjustmentRequired) && (r.classification !== 'NON_ADJUSTING' || !r.adjustmentRequired) &&
      (!r.disclosureRequired || !!r.disclosureReference && !!trim(r.disclosureReference)) &&
      r.evidenceReferences.every(x => !!trim(x) && x === trim(x)) && JSON.stringify(r.evidenceReferences).length <= 20000;
  }
  async load(): Promise<boolean> {
    const owner = this.owner(), selection = this.selectedId();
    if (!owner || this.busy()) return false;
    this.invalidate(true);
    const op = this.operation, context = this.identity(); this.busy.set(true);
    try {
      const state = await this.api.get(`${this.base()}/state${selection ? `?selectedRecordId=${selection}` : ''}`, subsequentEventState);
      if (!this.current(op, context, selection)) return false;
      if (state.engagementId.toLowerCase() !== owner.engagementId || state.selectedRecordId?.toLowerCase() !== selection && !(state.selectedRecordId === null && selection === null) || !long(state.expectedRevision) ||
        !hashPattern.test(state.reviewBasis) || state.expectedRevision !== (state.record?.revision ?? '0') ||
        (state.record?.id.toLowerCase() ?? null) !== selection || state.record && !this.validRecord(state.record, state)) throw new Error('The exact event basis could not be verified.');
      this.state.set(state);
      if (state.record) {
        const r = state.record;
        Object.assign(this.form, { periodEndDate: r.periodEndDate, eventDate: r.eventDate, description: r.description, classification: r.classification, currency: r.currency, procedureId: r.procedureId ?? '', financialEffect: r.financialEffect ?? '',
          adjustmentRequired: String(r.adjustmentRequired), disclosureRequired: String(r.disclosureRequired),
          disclosureReference: r.disclosureReference ?? '', rationale: r.rationale ?? '', evidenceReferences: r.evidenceReferences.join('\n') });
      }
      return true;
    } catch (e) { if (this.current(op, context, selection)) this.error.set(e instanceof Error ? e.message : 'Event state unavailable.'); return false; }
    finally { if (this.current(op, context, selection)) this.busy.set(false); }
  }
  private base(): string { return `/api/ui/engagements/${this.owner()!.engagementId}/completion/subsequent-events`; }
  private fields(): Fields | null {
    const f = this.form, evidence = f.evidenceReferences.split(/\r?\n/).map(trim).filter(Boolean);
    if (!validDate(f.periodEndDate) || !validDate(f.eventDate) || f.eventDate < f.periodEndDate ||
      !['true', 'false'].includes(f.adjustmentRequired) || !['true', 'false'].includes(f.disclosureRequired) ||
      !['ADJUSTING', 'NON_ADJUSTING', 'PENDING_ASSESSMENT'].includes(f.classification) ||
      !trim(f.description) || trim(f.description).length > 4000 || trim(f.rationale).length > 4000 || trim(f.disclosureReference).length > 200 ||
      !/^[A-Za-z]{3}$/.test(trim(f.currency)) || trim(f.procedureId) && !guidPattern.test(trim(f.procedureId)) ||
      f.financialEffect !== '' && !effectPattern.test(f.financialEffect) || JSON.stringify(evidence).length > 20000) return null;
    return { procedureId: trim(f.procedureId).toLowerCase() || null, periodEndDate: f.periodEndDate, eventDate: f.eventDate,
      description: trim(f.description), classification: f.classification as Fields['classification'], adjustmentRequired: f.adjustmentRequired === 'true',
      disclosureRequired: f.disclosureRequired === 'true', disclosureReference: trim(f.disclosureReference) || null, rationale: trim(f.rationale) || null,
      currency: trim(f.currency).toUpperCase(), financialEffect: f.financialEffect === '' ? null : f.financialEffect, evidenceReferences: evidence };
  }
  canPreview(action: Action): boolean {
    const s = this.state(); return !!s && s.selectedRecordId === this.selectedId() && s.engagementId.toLowerCase() === this.owner()?.engagementId &&
      !this.busy() && !this.pending() && !this.unavailable() &&
      (action === 'PREPARE' ? s.canPrepare && !!this.fields() && (!s.record || s.record.eventDate === this.form.eventDate && s.record.description === trim(this.form.description)) : s.canReview && !!s.record &&
        s.record.classification !== 'PENDING_ASSESSMENT' && s.record.reviewedByUserId === null && s.record.recordedByUserId.toLowerCase() !== this.owner()?.actorId);
  }
  async preparePreview(action: Action): Promise<void> {
    if (!this.canPreview(action)) return;
    const state = this.state()!, owner = this.owner()!, selection = this.selectedId(), draft = JSON.stringify(this.form);
    const body = { requestId: crypto.randomUUID(), expectedRevision: state.expectedRevision, reviewBasis: state.reviewBasis,
      fields: action === 'PREPARE' ? this.fields() : null, selectedRecordId: selection };
    this.invalidate(false); const op = this.operation, context = this.identity(); this.busy.set(true);
    try {
      const response = await this.api.command(`${this.base()}/${action === 'PREPARE' ? 'prepare' : `${state.record!.id}/review`}/preview`, body);
      if (!this.current(op, context, selection) || draft !== JSON.stringify(this.form)) return;
      if (!response.ok) throw new Error(response.message);
      const preview = decode(subsequentEventPreview, response.value);
      if (preview.requestId !== body.requestId || preview.action !== action || preview.reviewBasis !== body.reviewBasis ||
        preview.expectedRevision !== body.expectedRevision || !hashPattern.test(preview.requestHash) ||
        JSON.stringify(preview.currentRecord) !== JSON.stringify(state.record)) throw new Error('The preview does not match the selected event and exact intent.');
      const basisHash = state.record ? await digest(state.record) : null;
      if (!this.current(op, context, selection) || draft !== JSON.stringify(this.form)) return;
      const contentHash = await digest(content(body.fields ?? state.record!));
      if (!this.current(op, context, selection) || draft !== JSON.stringify(this.form)) return;
      const pending: Pending = { version: 1, ...owner, clientId: state.clientId.toLowerCase(), requestId: body.requestId, requestHash: preview.requestHash, action, eventId: state.record?.id.toLowerCase() ?? null,
        expectedRevision: state.expectedRevision, reviewBasis: state.reviewBasis, basisHash, contentHash };
      this.intent = { body, preview, pending, draft, state }; this.preview.set(preview);
    } catch (e) { if (this.current(op, context, selection)) this.error.set(e instanceof Error ? e.message : 'Preview unavailable.'); }
    finally { if (this.current(op, context, selection)) this.busy.set(false); }
  }
  private async receiptMatches(r: Receipt, p: Pending, check: () => boolean): Promise<boolean> {
    const owner = this.owner(), result = r.result, basis = r.preview.currentRecord;
    if (!check() || !owner || p.actorEpoch !== owner.actorEpoch || r.actorEpoch !== p.actorEpoch || r.actorId.toLowerCase() !== owner.actorId ||
      r.engagementId.toLowerCase() !== p.engagementId || r.requestId !== p.requestId || r.requestHash !== p.requestHash || r.action !== p.action ||
      r.preview.requestId !== p.requestId || r.preview.requestHash !== p.requestHash || r.preview.action !== p.action ||
      !r.preview.canProceed || r.preview.blockers.length !== 0 || r.preview.reviewBasis !== p.reviewBasis || r.preview.expectedRevision !== p.expectedRevision ||
      (basis?.id.toLowerCase() ?? null) !== p.eventId || (basis?.revision ?? '0') !== p.expectedRevision ||
      !this.validRecord(result, p) || r.subsequentEventReviewId.toLowerCase() !== result.id.toLowerCase() ||
      p.eventId !== null && result.id.toLowerCase() !== p.eventId || BigInt(result.revision) !== BigInt(p.expectedRevision) + 1n ||
      (p.action === 'PREPARE' ? result.recordedByUserId.toLowerCase() !== owner.actorId || result.reviewedByUserId !== null || result.reviewedAt !== null || result.reviewedRevision !== null
        : result.recordedByUserId.toLowerCase() === owner.actorId || result.reviewedByUserId?.toLowerCase() !== owner.actorId ||
          result.reviewedRevision !== p.expectedRevision || result.reviewedAt === null || result.classification === 'PENDING_ASSESSMENT')) return false;
    const basisHash = basis ? await digest(basis) : null;
    if (!check() || basisHash !== p.basisHash) return false;
    const contentHash = await digest(content(result));
    if (!check() || contentHash !== p.contentHash) return false;
    return p.action !== 'REVIEW' || !!basis && result.recordedByUserId === basis.recordedByUserId && result.recordedAt === basis.recordedAt;
  }

  private clearReference(key: string, raw: string): void {
    if (sessionStorage.getItem(key) !== raw) throw new Error('Recovery reference changed. It was not cleared.');
    sessionStorage.removeItem(key);
    if (sessionStorage.getItem(key) !== null) throw new Error('Recovery reference could not be cleared.');
    this.pending.set(null);
  }
  private async confirmed(p: Pending, key: string, raw: string, op: number, context: string, resultId: string): Promise<void> {
    if (!this.current(op, context, p.eventId)) return;
    this.clearReference(key, raw); this.intent = null; this.preview.set(null); this.assent = false; this.busy.set(false);
    const message = `Confirmed ${p.action.toLowerCase()} receipt for request ${p.requestId}.`;
    this.history.set(`${message} Current state has not yet refreshed.`); this.selectedId.set(resultId.toLowerCase());
    const refreshOperation = this.operation + 1;
    const loaded = await this.load();
    if (!this.current(refreshOperation, context, resultId.toLowerCase())) return;
    this.history.set(`${message} ${loaded ? 'Exact event state refreshed. Unrelated completion gates still apply.' : 'Current state could not be refreshed. Do not rely on earlier approval.'}`);
    if (loaded) this.refreshed.emit();
  }
  async submit(): Promise<void> {
    const intent = this.intent;
    if (!intent || !this.assent || this.busy() || this.pending() || this.unavailable() || !intent.preview.canProceed || intent.preview.blockers.length ||
      intent.state !== this.state() || intent.draft !== JSON.stringify(this.form) || this.context !== this.identity() ||
      !this.canPreview(intent.pending.action)) return;
    const p = intent.pending, key = this.key(), raw = JSON.stringify(p), op = ++this.operation, context = this.identity();
    const check = () => this.current(op, context, p.eventId);
    this.busy.set(true); this.error.set('');
    try {
      if (sessionStorage.getItem(key) !== null) throw new Error('An unresolved request already exists. No replacement request was sent.');
      sessionStorage.setItem(key, raw);
      if (sessionStorage.getItem(key) !== raw) throw new Error('The recovery reference could not be verified. No command was sent.');
      this.pending.set(p); this.state.set(null); this.preview.set(null); this.intent = null; this.assent = false;
      const response = await this.api.command(`${this.base()}/${p.action === 'PREPARE' ? 'prepare' : `${p.eventId}/review`}`,
        { ...intent.body, expectedRequestHash: p.requestHash, reviewed: true });
      if (!check()) return;
      if (!response.ok) {
        // Only a known first-send refusal permits replacement. Unknown and idempotency conflicts remain recoverable.
        const definitive = !response.unknown && ((response.status === 400 && ['mapping.invalid', 'gate.blocked', 'revision.stale', 'generation.stale'].includes(response.code)) ||
          response.status === 403 && response.code === 'scope.denied' || response.status === 409 && ['revision.stale', 'generation.stale'].includes(response.code));
        if (definitive) { this.clearReference(key, raw); this.error.set(`${response.message} Load the current basis before preparing a new intent.`); return; }
        throw new Error('Execution is unconfirmed. Look up the exact saved request; no replacement will be sent.');
      }
      const receipt = decode(subsequentEventReceipt, response.value);
      const matches = await this.receiptMatches(receipt, p, check);
      if (!check()) return;
      if (!matches) throw new Error('The response does not confirm this exact request. Its recovery reference remains saved.');
      await this.confirmed(p, key, raw, op, context, receipt.result.id);
    } catch (e) { if (check()) this.error.set(e instanceof Error ? e.message : 'The command could not be confirmed.'); }
    finally { if (check()) this.busy.set(false); }
  }
  async recover(): Promise<void> {
    const p = this.pending(), state = this.state(), owner = this.owner();
    if (!p || !state || !owner || this.busy() || this.unavailable() || state.selectedRecordId !== p.eventId ||
      state.clientId.toLowerCase() !== p.clientId || p.actorEpoch !== owner.actorEpoch) {
      this.error.set('Load the saved exact event scope in its original session lifetime before recovery.'); return;
    }
    const key = this.key(), op = ++this.operation, context = this.identity(), check = () => this.current(op, context, p.eventId);
    this.busy.set(true); this.error.set('');
    try {
      const raw = sessionStorage.getItem(key);
      if (!raw || raw !== JSON.stringify(p)) throw new Error('The saved exact recovery reference changed. No request was sent.');
      const lookup = await this.api.get(`${this.base()}/receipts/${p.requestId}?requestHash=${p.requestHash}`, subsequentEventLookup);
      if (!check()) return;
      if (!lookup.found || !lookup.receipt) throw new Error('No exact receipt is available. The request remains unresolved; no replacement will be sent.');
      const matches = await this.receiptMatches(lookup.receipt, p, check);
      if (!check()) return;
      if (!matches) throw new Error('The receipt does not match the exact retained intent. Its reference remains saved.');
      await this.confirmed(p, key, raw, op, context, lookup.receipt.result.id);
    } catch (e) { if (check()) this.error.set(e instanceof Error ? e.message : 'Recovery unavailable.'); }
    finally { if (check()) this.busy.set(false); }
  }
}
