import { afterNextRender, Component, DestroyRef, effect, ElementRef, inject, Injector, input, signal, untracked, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule } from '@angular/material/paginator';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api, routeGuid } from '../../core/api';
import { SHARED } from '../../core/ui';
import { decodeNativeContext } from './native-source-contracts';
import { TabDrafts } from '../../core/tab-drafts';
import { arr, bool, guid, nat, nullable, obj, oneOf, sha256, text } from '../../core/decode';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';

interface Period { id: string; revision: string; code: string; start: string; end: string; currency: string; status: string }
interface OpeningLine { accountCode: string; accountName: string; debit: string; credit: string }
interface OpeningItem { id: string; role: 'AR' | 'AP'; counterpartyId: string; counterpartyName: string; reference: string; dueDate: string | null; accountId: string; accountCode: string; amount: string }
interface OpeningItemInput { role: 'AR' | 'AP'; counterpartyId: string; reference: string; dueDate: string; accountCode: string; amount: string }
interface Counterparty { id: string; clientId: string; displayName: string; role: string }
interface Opening { id: string; clientId: string; periodId: string; chartVersionId: string; periodRevision: string; asOfDate: string; currency: string;
  evidenceReference: string; evidenceSha256: string; manifestSha256: string; createdByUserId: string; createdAt: string;
  approvedByUserId: string | null; approvedAt: string | null; lines: OpeningLine[]; openItems: OpeningItem[] }
interface EntryLine { accountCode: string; debit: string; credit: string }

const amountPattern = /^(?:0|[1-9]\d{0,14})(?:\.\d{1,6})?$/;
const hashPattern = /^[a-fA-F0-9]{64}$/;
const datePattern = /^\d{4}-\d{2}-\d{2}$/;
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid opening balance response');
  return value as Record<string, unknown>;
}
function validAmount(value: unknown): value is string { return typeof value === 'string' && amountPattern.test(value); }
function units(value: string): bigint { const [whole, fraction = ''] = value.split('.'); return BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0')); }

export function decodeOpeningBalance(value: unknown, clientId: string, period: Period): Opening | null {
  if (value === null) return null;
  const v = object(value);
  if (v['clientId'] !== clientId || v['periodId'] !== period.id ||
    !['id', 'chartVersionId', 'createdByUserId'].every(k => typeof v[k] === 'string' && guidPattern.test(String(v[k]))) ||
    typeof v['periodRevision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(v['periodRevision']) ||
    !['asOfDate', 'currency', 'evidenceReference', 'evidenceSha256', 'manifestSha256', 'createdAt'].every(k => typeof v[k] === 'string') ||
    v['asOfDate'] !== period.start || v['currency'] !== period.currency ||
    !hashPattern.test(String(v['evidenceSha256'])) || !hashPattern.test(String(v['manifestSha256'])) ||
    !String(v['evidenceReference']).trim() ||
    (v['approvedByUserId'] !== null && (typeof v['approvedByUserId'] !== 'string' || !guidPattern.test(v['approvedByUserId']))) ||
    (v['approvedAt'] !== null && typeof v['approvedAt'] !== 'string') ||
    !Array.isArray(v['lines']) || v['lines'].length < 2 || v['lines'].length > 5000)
    throw new Error('Opening balance response did not match the selected client period');
  const seen = new Set<string>(); let debit = 0n; let credit = 0n;
  for (const raw of v['lines']) {
    const line = object(raw);
    if (!['accountCode', 'accountName'].every(k => typeof line[k] === 'string' && String(line[k]).trim()) ||
      !validAmount(line['debit']) || !validAmount(line['credit']) || seen.has(String(line['accountCode'])) ||
      (units(line['debit']) > 0n && units(line['credit']) > 0n) || (units(line['debit']) === 0n && units(line['credit']) === 0n))
      throw new Error('Opening balance contains an invalid or repeated account row');
    seen.add(String(line['accountCode'])); debit += units(line['debit']); credit += units(line['credit']);
  }
  if (debit !== credit) throw new Error('Opening balance manifest is not balanced');
  if (!Array.isArray(v['openItems']) || v['openItems'].length > 5000) throw new Error('Opening item schedule is invalid');
  const references = new Set<string>();
  for (const raw of v['openItems']) {
    const item = object(raw); const identity = `${item['role']}|${item['counterpartyId']}|${item['reference']}`.toUpperCase();
    if (!['AR', 'AP'].includes(String(item['role'])) || typeof item['counterpartyId'] !== 'string' || !guidPattern.test(item['counterpartyId']) ||
      typeof item['counterpartyName'] !== 'string' || !item['counterpartyName'].trim() || typeof item['reference'] !== 'string' || !item['reference'].trim() ||
      !(item['dueDate'] === null || (typeof item['dueDate'] === 'string' && datePattern.test(item['dueDate']))) ||
      typeof item['accountCode'] !== 'string' || !item['accountCode'].trim() || typeof item['accountId'] !== 'string' || !guidPattern.test(item['accountId']) ||
      !validAmount(item['amount']) || units(item['amount']) <= 0n || references.has(identity)) throw new Error('Opening balance contains invalid AR/AP detail');
    references.add(identity);
  }
  return value as Opening;
}

interface SuccessorRequest {
  requestId: string; clientId: string; engagementId: string; periodId: string; expectedPeriodRevision: string;
  priorPeriodId: string; expectedPriorPeriodRevision: string; nativeTrialBalanceDatasetId: string;
  expectedSourceIdentityHash: string; evidenceReference: string; reviewBasis?: string; reviewed: boolean;
}
interface SuccessorApproval {
  requestId: string; clientId: string; engagementId: string; openingId: string; expectedPeriodRevision: string;
  expectedOpeningManifestHash: string; expectedSuccessorProofHash: string; reviewed: boolean;
}
type SuccessorPending = { action: 'CREATE'; request: SuccessorRequest; manifestHash: string } |
  { action: 'APPROVE'; request: SuccessorApproval; manifestHash: string };
const exactRevision = (v: unknown, path: string): string => {
  if (typeof v !== 'string' || !/^[1-9][0-9]{0,18}$/.test(v) || BigInt(v) > 9223372036854775807n)
    throw new Error(`${path}: invalid exact revision`);
  return v;
};
const successorLine = obj({ accountId: guid, accountCode: text, accountName: text,
  debit: (v: unknown) => { if (!validAmount(v)) throw new Error('Invalid exact debit'); return v; },
  credit: (v: unknown) => { if (!validAmount(v)) throw new Error('Invalid exact credit'); return v; } });
const successorControl = obj({ role: oneOf('AR', 'AP'), configurationId: guid, decisionId: guid, accountId: guid,
  closingDebit: (v: unknown) => { if (!validAmount(v)) throw new Error('Invalid exact control debit'); return v; },
  closingCredit: (v: unknown) => { if (!validAmount(v)) throw new Error('Invalid exact control credit'); return v; } });
export const decodeSuccessorCatalog = obj({ clientId: guid, periodId: guid, periodRevision: exactRevision,
  priorPeriodId: nullable(guid), priorPeriodRevision: nullable(exactRevision), priorPeriodCode: nullable(text), priorPeriodStatus: nullable(text),
  origin: oneOf('NONE', 'MANUAL', 'NATIVE_SUCCESSOR'), existingOpeningId: nullable(guid),
  retainedSuccessor: nullable(obj({ openingId: guid, engagementId: guid, requestId: guid })),
  canPrepare: bool, canApprove: bool, page: nat, pageSize: nat, total: nat,
  sources: arr(obj({ datasetId: guid, engagementId: guid, legalEntityKey: text, sourceIdentityHash: sha256,
    validationStatus: text, eligible: bool, blocker: nullable(text) }), 5), blocker: nullable(text) });
export const decodeSuccessorPreview = obj({ reviewBasis: sha256, sourceIdentityHash: sha256, prospectiveOpeningManifestHash: sha256,
  periodId: guid, periodRevision: exactRevision, priorPeriodId: guid, priorPeriodRevision: exactRevision, asOfDate: text,
  currency: text, basis: text, bookScope: oneOf('PERIOD_LEDGER_NO_BOOK'), lines: arr(successorLine, 5000),
  controls: arr(successorControl, 2), populationHash: sha256, ready: bool, blockers: arr(text, 100) });
type SuccessorCatalog = ReturnType<typeof decodeSuccessorCatalog>;
type SuccessorPreview = ReturnType<typeof decodeSuccessorPreview>;
interface SuccessorView { opening: Opening; sourceIdentityHash: string; proofHash: string; proofJson: string;
  isCurrent: boolean; currentnessBlocker: string | null; sourceDatasetId: string; priorPeriodId: string;
  engagementId: string; populationHash: string; controls: ReturnType<typeof successorControl>[] }
export function decodeSuccessorView(raw: unknown, clientId: string, period: Period): SuccessorView {
  const v = object(raw), opening = decodeOpeningBalance(v['opening'], clientId, period);
  if (!opening || typeof v['sourceIdentityHash'] !== 'string' || !hashPattern.test(v['sourceIdentityHash']) ||
      opening.evidenceSha256 !== v['sourceIdentityHash'] || typeof v['proofHash'] !== 'string' || !hashPattern.test(v['proofHash']) ||
      typeof v['proofJson'] !== 'string' || v['proofJson'].length > 10_000_000 || typeof v['isCurrent'] !== 'boolean' ||
      !(v['currentnessBlocker'] === null || typeof v['currentnessBlocker'] === 'string')) throw new Error('Invalid retained successor');
  const proof = object(JSON.parse(v['proofJson'])), facts = object(proof['Facts']);
  if (proof['Version'] !== 'native-opening-successor.v1' || proof['ClientId'] !== clientId || proof['OpeningId'] !== opening.id ||
      proof['OpeningManifestHash'] !== opening.manifestSha256 || proof['SourceIdentityHash'] !== v['sourceIdentityHash'] ||
      facts['TargetPeriodId'] !== opening.periodId || typeof proof['EngagementId'] !== 'string' || !guidPattern.test(proof['EngagementId']) ||
      typeof proof['PriorPeriodId'] !== 'string' || !guidPattern.test(proof['PriorPeriodId']) ||
      typeof proof['SourceDatasetId'] !== 'string' || !guidPattern.test(proof['SourceDatasetId']) ||
      typeof facts['PopulationHash'] !== 'string' || !hashPattern.test(facts['PopulationHash']) || !Array.isArray(facts['Controls']) || facts['Controls'].length > 2)
    throw new Error('Retained successor proof scope mismatch');
  const controls = facts['Controls'].map(raw => {
    const c = object(raw);
    return successorControl({ role: c['Role'], configurationId: c['ConfigurationId'], decisionId: c['DecisionId'], accountId: c['AccountId'],
      closingDebit: c['ClosingDebit'], closingCredit: c['ClosingCredit'] }, 'proof.controls');
  });
  return { opening, sourceIdentityHash: v['sourceIdentityHash'], proofHash: v['proofHash'], proofJson: v['proofJson'],
    isCurrent: v['isCurrent'], currentnessBlocker: v['currentnessBlocker'] as string | null, sourceDatasetId: proof['SourceDatasetId'],
    priorPeriodId: proof['PriorPeriodId'], engagementId: proof['EngagementId'], populationHash: facts['PopulationHash'], controls };
}
function decodeSuccessorPending(raw: unknown): SuccessorPending | null {
  try {
    const value = object(raw), r = object(value['request']);
    if (!['CREATE', 'APPROVE'].includes(String(value['action'])) || typeof value['manifestHash'] !== 'string' || !hashPattern.test(value['manifestHash']) ||
        !['requestId', 'clientId', 'engagementId'].every(k => typeof r[k] === 'string' && guidPattern.test(String(r[k]))) || r['reviewed'] !== true)
      return null;
    exactRevision(r['expectedPeriodRevision'], 'revision');
    if (value['action'] === 'CREATE') {
      if (!['periodId', 'priorPeriodId', 'nativeTrialBalanceDatasetId'].every(k => typeof r[k] === 'string' && guidPattern.test(String(r[k]))) ||
          !['expectedSourceIdentityHash', 'reviewBasis'].every(k => typeof r[k] === 'string' && hashPattern.test(String(r[k]))) ||
          typeof r['evidenceReference'] !== 'string' || !r['evidenceReference'].trim() || r['evidenceReference'].length > 1000) return null;
      exactRevision(r['expectedPriorPeriodRevision'], 'prior revision');
    } else if (typeof r['openingId'] !== 'string' || !guidPattern.test(r['openingId']) ||
        !['expectedOpeningManifestHash', 'expectedSuccessorProofHash'].every(k => typeof r[k] === 'string' && hashPattern.test(String(r[k])))) return null;
    const common = { requestId: r['requestId'] as string, clientId: r['clientId'] as string,
      engagementId: r['engagementId'] as string, expectedPeriodRevision: r['expectedPeriodRevision'] as string, reviewed: true };
    if (value['action'] === 'CREATE') return { action: 'CREATE', manifestHash: value['manifestHash'], request: { ...common,
      periodId: r['periodId'] as string, priorPeriodId: r['priorPeriodId'] as string,
      expectedPriorPeriodRevision: r['expectedPriorPeriodRevision'] as string,
      nativeTrialBalanceDatasetId: r['nativeTrialBalanceDatasetId'] as string,
      expectedSourceIdentityHash: r['expectedSourceIdentityHash'] as string,
      evidenceReference: r['evidenceReference'] as string, reviewBasis: r['reviewBasis'] as string } };
    if (value['manifestHash'] !== r['expectedOpeningManifestHash']) return null;
    return { action: 'APPROVE', manifestHash: value['manifestHash'], request: { ...common,
      openingId: r['openingId'] as string, expectedOpeningManifestHash: r['expectedOpeningManifestHash'] as string,
      expectedSuccessorProofHash: r['expectedSuccessorProofHash'] as string } };
  } catch { return null; }
}

@Component({
  selector: 'audit-client-operational-opening-balances',
  imports: [FormsModule, MatButtonModule, MatPaginatorModule, RouterLink],
  host: { '(document:pointerdown)': 'recordSuccessorInteraction()', '(document:keydown)': 'recordSuccessorInteraction()' },
  templateUrl: './operational-opening-balances.html'
})
export class ClientOperationalOpeningBalances {
  readonly clientId = input.required<string>();
  readonly periods = input.required<Period[]>();
  readonly currency = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly requestedSuccessorSource = inject(ActivatedRoute, { optional: true })?.snapshot?.queryParamMap.get('successorSource') ?? null;
  private readonly api = inject(Api);
  private readonly drafts = inject(TabDrafts);
  private readonly renderInjector = inject(Injector);
  private readonly successorHeading = viewChild<ElementRef<HTMLElement>>('successorHeading');
  private successorEpoch = 0;
  private successorRead = 0;
  private successorInteraction = 0;
  readonly successorMode = signal(false);
  readonly successorCatalog = signal<SuccessorCatalog | null>(null);
  readonly successorPreview = signal<SuccessorPreview | null>(null);
  readonly successorRecoveryReady = signal(false);
  readonly successorView = signal<SuccessorView | null>(null);
  readonly successorLoading = signal(false);
  readonly successorBusy = signal(false);
  readonly successorUncertain = signal(false);
  readonly successorReviewed = signal(false);
  readonly successorApprovalReviewed = signal(false);
  readonly successorPending = signal<SuccessorPending | null>(null);
  readonly successorMessage = signal('');
  readonly successorPage = signal(0);
  readonly successorLinePage = signal(0);
  successorSourceId = '';
  successorEvidence = '';
  private successorIntent: SuccessorRequest | null = null;

  readonly opening = signal<Opening | null>(null);
  readonly counterparties = signal<Counterparty[]>([]); readonly partyPage = signal(0); readonly partyTotal = signal(0);
  readonly loading = signal(false); readonly busy = signal(false); readonly uncertain = signal(false);
  readonly error = signal(''); readonly readSucceeded = signal(false); readonly reviewed = signal(false); readonly approvalReviewed = signal(false);
  readonly lines = signal<EntryLine[]>([{ accountCode: '', debit: '0', credit: '0' }, { accountCode: '', debit: '0', credit: '0' }]);
  readonly openItems = signal<OpeningItemInput[]>([]);
  periodId = ''; evidenceReference = ''; evidenceSha256 = '';
  private requestId = 0; private operation?: Subscription;
  private readonly invalidate = effect(() => {
    this.clientId(); this.periods(); this.currency(); this.session.invalidation();
    untracked(() => { this.operation?.unsubscribe(); ++this.requestId; this.resetSuccessor(); this.periodId = ''; this.opening.set(null); this.counterparties.set([]); this.partyPage.set(0); this.partyTotal.set(0); this.loading.set(false); this.busy.set(false); this.uncertain.set(false); this.error.set(''); this.readSucceeded.set(false); this.reset(); });
  });
  constructor() { inject(DestroyRef).onDestroy(() => { this.operation?.unsubscribe(); ++this.successorEpoch; }); }
  selected(): Period | undefined { return this.periods().find(p => p.id === this.periodId); }
  userId(): string { return this.session.current()?.userId ?? ''; }
  selectPeriod(id: string): void { if (this.successorBusy()) return; this.resetSuccessor(); this.periodId = id; this.opening.set(null); this.reset(); this.uncertain.set(false); this.error.set(''); this.readSucceeded.set(false); this.counterparties.set([]); this.partyPage.set(0); this.partyTotal.set(0); if (id) { this.load(); this.loadCounterparties(0); } }
  reset(): void { this.evidenceReference = ''; this.evidenceSha256 = ''; this.reviewed.set(false); this.approvalReviewed.set(false); this.lines.set([{ accountCode: '', debit: '0', credit: '0' }, { accountCode: '', debit: '0', credit: '0' }]); this.openItems.set([]); }
  edited(): void { this.reviewed.set(false); }
  addLine(): void { if (this.lines().length < 5000) this.lines.update(rows => [...rows, { accountCode: '', debit: '0', credit: '0' }]); this.edited(); }
  removeLine(index: number): void { if (this.lines().length > 2) this.lines.update(rows => rows.filter((_, i) => i !== index)); this.edited(); }
  addOpenItem(): void { if (this.openItems().length < 5000) this.openItems.update(rows => [...rows, { role: 'AR', counterpartyId: '', reference: '', dueDate: '', accountCode: '', amount: '' }]); this.edited(); }
  removeOpenItem(index: number): void { this.openItems.update(rows => rows.filter((_, i) => i !== index)); this.edited(); }
  loadCounterparties(page: number): void {
    const client = this.clientId(), generation = this.session.invalidation(); if (!guidPattern.test(client) || page < 0) return;
    this.http.get<unknown>(`/api/ui/accounting/clients/${client}/counterparties`, { params: { page: String(page), pageSize: '25' } }).pipe(timeout(15000)).subscribe({
      next: value => {
        if (client !== this.clientId() || generation !== this.session.invalidation()) return;
        const v = object(value); const rows = v['counterparties'];
        if (v['clientId'] !== client || v['page'] !== page || v['pageSize'] !== 25 || !Number.isSafeInteger(v['total']) || !Array.isArray(rows) || rows.length > 25 ||
          rows.some(raw => { const party = object(raw); return party['clientId'] !== client || typeof party['id'] !== 'string' || !guidPattern.test(party['id']) || typeof party['displayName'] !== 'string' || !['CUSTOMER', 'SUPPLIER', 'BOTH'].includes(String(party['role'])); })) {
          this.error.set('The client party list could not be validated.'); return;
        }
        const existing = this.counterparties(); const seen = new Set(existing.map(x => x.id));
        this.counterparties.set([...existing, ...(rows as Counterparty[]).filter(x => !seen.has(x.id))]); this.partyPage.set(page); this.partyTotal.set(Number(v['total']));
      }, error: () => { if (client === this.clientId() && generation === this.session.invalidation()) this.error.set('Client parties are unavailable for opening detail.'); }
    });
  }
  balanced(): boolean {
    const rows = this.lines();
    try { const codes = rows.map(x => x.accountCode.trim()); return rows.length >= 2 && codes.every(Boolean) && new Set(codes).size === codes.length &&
      rows.every(x => validAmount(x.debit) && validAmount(x.credit) && !(units(x.debit) > 0n && units(x.credit) > 0n) && (units(x.debit) > 0n || units(x.credit) > 0n)) &&
      rows.reduce((n, x) => n + units(x.debit), 0n) === rows.reduce((n, x) => n + units(x.credit), 0n) && rows.some(x => units(x.debit) > 0n); }
    catch { return false; }
  }
  total(side: 'debit' | 'credit'): string {
    try { const sum = this.lines().reduce((n, x) => n + (validAmount(x[side]) ? units(x[side]) : 0n), 0n); return `${sum / 1_000_000n}.${(sum % 1_000_000n).toString().padStart(6, '0')}`; }
    catch { return '—'; }
  }
  load(): void {
    const period = this.selected(); if (!period || this.busy() || this.successorBusy()) return;
    const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId();
    this.loading.set(true); this.error.set(''); this.readSucceeded.set(false);
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/operational-opening-balances/${period.id}`).pipe(timeout(15000)).subscribe({
      next: value => { if (request !== this.requestId || generation !== this.session.invalidation() || client !== this.clientId()) return;
        try { this.opening.set(decodeOpeningBalance(value, client, period)); void this.loadSuccessorCatalog(); this.readSucceeded.set(true); this.uncertain.set(false); this.approvalReviewed.set(false); this.error.set(''); }
        catch { this.opening.set(null); this.error.set('Opening snapshot did not match the selected client period.'); }
        this.loading.set(false);
      }, error: failure => { if (request === this.requestId) { this.loading.set(false); this.opening.set(null); this.error.set('Opening snapshot is unavailable. Retry before creating or reviewing it.'); if (failure.status === 401) this.session.clear(); } }
    });
  }
  create(): void {
    const period = this.selected(); if (!period || !this.manualReady() || period.status === 'CLOSED' || !this.readSucceeded() || this.opening() || this.busy() || this.uncertain() || !this.reviewed() || !this.balanced() || !hashPattern.test(this.evidenceSha256.trim())) return;
    const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post<unknown>(`/api/ui/accounting/clients/${client}/operational-opening-balances`, {
      periodId: period.id, periodRevision: period.revision, asOfDate: period.start, currency: period.currency,
      evidenceReference: this.evidenceReference.trim(), evidenceSha256: this.evidenceSha256.trim().toLowerCase(),
      lines: this.lines().map(x => ({ accountCode: x.accountCode.trim(), debit: x.debit, credit: x.credit })),
      openItems: this.openItems().map(x => ({ role: x.role, counterpartyId: x.counterpartyId, reference: x.reference.trim(),
        dueDate: x.dueDate || null, accountCode: x.accountCode.trim(), amount: x.amount })), reviewed: true
    }).pipe(timeout(20000)).subscribe({ next: () => { if (request !== this.requestId || generation !== this.session.invalidation()) return; this.busy.set(false); this.uncertain.set(true); this.load(); },
      error: failure => { if (request === this.requestId) { this.busy.set(false); const refused = failure.status >= 400 && failure.status < 500; this.uncertain.set(!refused);
        this.error.set(failure.status === 403 ? 'You are not authorized to create this opening snapshot.' : failure.status === 400 ?
          'The opening was refused. Check the balance lines and ensure every supplied AR/AP schedule exactly matches its approved control balance.' :
          'Creation outcome is unconfirmed. Refresh the selected period before retrying.'); if (failure.status === 401) this.session.clear(); } }
    });
  }
  approve(snapshot: Opening): void {
    const period = this.selected(); if (!period || !this.manualReady() || this.successorCatalog()?.origin !== 'MANUAL' || period.status === 'CLOSED' || period.revision !== snapshot.periodRevision || snapshot.createdByUserId === this.userId() || !this.approvalReviewed() || this.busy() || this.uncertain()) return;
    const request = ++this.requestId, generation = this.session.invalidation(), client = this.clientId(); this.busy.set(true); this.error.set('');
    this.operation = this.http.post(`/api/ui/accounting/clients/${client}/operational-opening-balances/${snapshot.id}/approve`, {
      periodRevision: snapshot.periodRevision, manifestSha256: snapshot.manifestSha256, reviewed: true
    }).pipe(timeout(20000)).subscribe({ next: () => { if (request !== this.requestId || generation !== this.session.invalidation()) return; this.busy.set(false); this.uncertain.set(true); this.load(); },
      error: failure => { if (request === this.requestId) { this.busy.set(false); const refused = failure.status >= 400 && failure.status < 500; this.uncertain.set(!refused);
        this.error.set(refused ? 'Approval was refused. Refresh and confirm the reviewer role, period revision, and exact manifest.' :
          'Approval outcome is unconfirmed. Refresh the selected period before another action.'); if (failure.status === 401) this.session.clear(); } }
    });
  }
  recordSuccessorInteraction(): void { ++this.successorInteraction; }
  private successorBase(): string { return `/api/ui/accounting/clients/${this.clientId()}/operational-opening-successors`; }
  private successorDraftScope() {
    // Storage namespace only; this sentinel is never a source/proof hash or a publication input.
    return { entity: `native-successor/${this.clientId()}/${this.periodId}`, baseRevision: '0'.repeat(64) };
  }
  private successorOwner(): () => boolean {
    const epoch = this.successorEpoch, client = this.clientId(), period = this.periodId,
      invalidation = this.session.invalidation(), actor = this.session.current()?.userId;
    return () => epoch === this.successorEpoch && client === this.clientId() && period === this.periodId &&
      invalidation === this.session.invalidation() && actor === this.session.current()?.userId;
  }
  private resetSuccessor(): void {
    ++this.successorEpoch; ++this.successorRead;
    this.successorMode.set(false); this.successorCatalog.set(null); this.successorView.set(null); this.successorPreview.set(null); this.successorRecoveryReady.set(false);
    this.successorLoading.set(false); this.successorBusy.set(false); this.successorUncertain.set(false);
    this.successorPending.set(null); this.successorReviewed.set(false); this.successorApprovalReviewed.set(false);
    this.successorMessage.set(''); this.successorPage.set(0); this.successorLinePage.set(0);
    this.successorSourceId = ''; this.successorEvidence = ''; this.successorIntent = null;
  }
  manualReady(): boolean { return !!this.successorCatalog() && !this.successorLoading() && !this.successorBusy() && !this.successorUncertain() && !this.successorMode(); }
  setSuccessorMode(native: boolean): void {
    if (this.busy() || this.successorBusy() || this.successorUncertain()) return;
    this.successorMode.set(native); this.successorEdited();
  }
  successorEdited(): void {
    if (this.successorPending()) return;
    this.successorPreview.set(null); this.successorIntent = null; this.successorReviewed.set(false); this.successorLinePage.set(0);
  }
  selectedSuccessor() { return this.successorCatalog()?.sources.find(x => x.datasetId === this.successorSourceId); }
  async loadSuccessorCatalog(page = this.successorPage()): Promise<void> {
    const period = this.selected(); if (!period || this.successorBusy()) return;
    const current = this.successorOwner(), read = ++this.successorRead, client = this.clientId();
    this.successorLoading.set(true); this.successorCatalog.set(null); this.successorPreview.set(null); this.successorRecoveryReady.set(false);
    this.successorView.set(null); this.successorReviewed.set(false); this.successorApprovalReviewed.set(false);
    try {
      const catalog = await this.api.get(`${this.successorBase()}/catalog/${period.id}?page=${page}&pageSize=5`, decodeSuccessorCatalog);
      if (!current() || read !== this.successorRead) return;
      if (catalog.clientId !== client || catalog.periodId !== period.id || catalog.page !== page || catalog.pageSize !== 5 ||
          catalog.sources.length !== Math.min(5, Math.max(0, catalog.total - page * 5)) ||
          catalog.origin === 'NONE' && (catalog.existingOpeningId !== null || catalog.retainedSuccessor !== null || catalog.canApprove) ||
          catalog.origin === 'MANUAL' && (catalog.existingOpeningId === null || catalog.retainedSuccessor !== null || catalog.canApprove) ||
          catalog.origin === 'NATIVE_SUCCESSOR' && (!catalog.retainedSuccessor || catalog.existingOpeningId !== catalog.retainedSuccessor.openingId))
        throw new Error('Successor catalogue did not match the exact client/target period.');
      this.successorCatalog.set(catalog); this.successorPage.set(page);
      if (this.requestedSuccessorSource && catalog.sources.some(x => x.datasetId === this.requestedSuccessorSource)) {
        this.successorSourceId = this.requestedSuccessorSource; this.successorMode.set(true);
      }
      const saved = this.drafts.readPendingSubmission(this.successorDraftScope(), decodeSuccessorPending);
      if (saved.state === 'ready') {
        const pending = saved.draft.value;
        if (pending.request.clientId !== client || pending.action === 'CREATE' && pending.request.periodId !== period.id)
          throw new Error('The retained successor request belongs to different scope.');
        this.successorPending.set(pending); this.successorUncertain.set(true); this.successorMode.set(true);
        this.successorMessage.set('An original request is unresolved. Read retained evidence or explicitly recover that exact request; no new intent is sent automatically.');
      } else if (saved.state !== 'absent') {
        this.successorUncertain.set(true); this.successorMode.set(true);
        this.successorMessage.set('The prior request checkpoint is unavailable or invalid. Reconcile retained evidence before sending any new opening action.');
      }
      if (catalog.retainedSuccessor) {
        const view = await this.api.get(`${this.successorBase()}/${catalog.retainedSuccessor.openingId}`,
          raw => decodeSuccessorView(raw, client, period));
        if (!current() || read !== this.successorRead) return;
        if (view.opening.id !== catalog.retainedSuccessor.openingId || view.engagementId !== catalog.retainedSuccessor.engagementId)
          throw new Error('Retained successor proof did not match the scoped catalogue.');
        this.successorView.set(view); this.opening.set(view.opening); this.successorMode.set(true);
        const pending = this.successorPending();
        const creationMatches = pending?.action === 'CREATE' && catalog.retainedSuccessor.requestId === pending.request.requestId &&
          view.opening.createdByUserId === this.userId() && view.sourceIdentityHash === pending.request.expectedSourceIdentityHash &&
          view.opening.manifestSha256 === pending.manifestHash;
        const approvalMatches = pending?.action === 'APPROVE' && view.opening.id === pending.request.openingId &&
          view.opening.manifestSha256 === pending.request.expectedOpeningManifestHash && view.proofHash === pending.request.expectedSuccessorProofHash &&
          view.opening.approvedByUserId === this.userId();
        if (creationMatches || approvalMatches) this.clearSuccessorPending('Matching retained successor evidence recovered. No new intent was created.');
      }
    } catch (error) {
      if (current() && read === this.successorRead) {
        this.successorCatalog.set(null); this.successorView.set(null);
        this.successorMessage.set(error instanceof Error ? error.message : 'Successor evidence is unavailable.');
      }
    } finally { if (current() && read === this.successorRead) this.successorLoading.set(false); }
  }
  private successorFocus(trigger: EventTarget | null, current: () => boolean): () => void {
    if (!(trigger instanceof HTMLElement) || trigger.ownerDocument.activeElement !== trigger) return () => undefined;
    const interaction = this.successorInteraction, doc = trigger.ownerDocument;
    return () => afterNextRender(() => {
      if (current() && interaction === this.successorInteraction &&
          (doc.activeElement === trigger || doc.activeElement === doc.body)) this.successorHeading()?.nativeElement.focus();
    }, { injector: this.renderInjector });
  }
  async previewSuccessor(trigger: EventTarget | null): Promise<void> {
    const catalog = this.successorCatalog(), source = this.selectedSuccessor();
    if (!catalog?.canPrepare || !source?.eligible || !catalog.priorPeriodId || !catalog.priorPeriodRevision ||
        this.busy() || this.successorBusy() || this.successorUncertain() || !this.successorEvidence.trim() || this.successorEvidence.trim().length > 1000) return;
    const current = this.successorOwner(), focus = this.successorFocus(trigger, current);
    const request: SuccessorRequest = { requestId: crypto.randomUUID(), clientId: this.clientId(), engagementId: source.engagementId,
      periodId: catalog.periodId, expectedPeriodRevision: catalog.periodRevision, priorPeriodId: catalog.priorPeriodId,
      expectedPriorPeriodRevision: catalog.priorPeriodRevision, nativeTrialBalanceDatasetId: source.datasetId,
      expectedSourceIdentityHash: source.sourceIdentityHash, evidenceReference: this.successorEvidence.trim(), reviewed: false };
    this.successorBusy.set(true); this.successorPreview.set(null); this.successorReviewed.set(false); this.successorMessage.set('');
    const result = await this.api.command<unknown>(this.successorBase() + '/preview', request);
    if (!current()) return;
    this.successorBusy.set(false);
    if (!result.ok) { this.successorMessage.set(result.message); return; }
    try {
      const preview = decodeSuccessorPreview(result.value, 'preview');
      if (preview.periodId !== request.periodId || preview.periodRevision !== request.expectedPeriodRevision ||
          preview.priorPeriodId !== request.priorPeriodId || preview.priorPeriodRevision !== request.expectedPriorPeriodRevision ||
          preview.sourceIdentityHash !== request.expectedSourceIdentityHash || preview.asOfDate !== this.selected()?.start ||
          preview.currency !== this.selected()?.currency || preview.lines.length < 2 || preview.controls.length !== 2)
        throw new Error('Preview did not match the exact successor intent.');
      this.successorIntent = request; this.successorPreview.set(preview); this.successorLinePage.set(0);
      focus();
    } catch { this.successorMessage.set('The successor preview could not be validated. No create action is available.'); }
  }
  private clearSuccessorPending(message: string): void {
    if (!this.drafts.clear(this.successorDraftScope().entity)) {
      this.successorMessage.set('Retained evidence is readable, but the pending checkpoint could not be cleared. Further writes stay blocked.'); return;
    }
    this.successorPending.set(null); this.successorUncertain.set(false); this.successorReviewed.set(false);
    this.successorApprovalReviewed.set(false); this.successorMessage.set(message);
  }
  async validateSuccessorRecovery(): Promise<void> {
    const pending = this.successorPending();
    if (pending?.action !== 'CREATE' || this.busy() || this.successorBusy() || this.successorLoading() || !this.successorCatalog()) return;
    const current = this.successorOwner();
    this.successorBusy.set(true); this.successorRecoveryReady.set(false); this.successorReviewed.set(false);
    this.successorPreview.set(null);
    // Resolve the original source directly, including when it is absent from the current candidate page.
    const result = await this.api.command<unknown>(this.successorBase() + '/preview', { ...pending.request, reviewed: false });
    if (!current()) return;
    this.successorBusy.set(false);
    if (!result.ok) { this.successorMessage.set(result.message); return; }
    try {
      const preview = decodeSuccessorPreview(result.value, 'recoveryPreview'), request = pending.request;
      if (!preview.ready || preview.reviewBasis !== request.reviewBasis ||
          preview.prospectiveOpeningManifestHash !== pending.manifestHash ||
          preview.periodId !== request.periodId || preview.periodRevision !== request.expectedPeriodRevision ||
          preview.priorPeriodId !== request.priorPeriodId || preview.priorPeriodRevision !== request.expectedPriorPeriodRevision ||
          preview.sourceIdentityHash !== request.expectedSourceIdentityHash || preview.asOfDate !== this.selected()?.start ||
          preview.currency !== this.selected()?.currency || preview.lines.length < 2 || preview.controls.length !== 2)
        throw new Error('The original preview changed. Retain the request and reconcile its outcome; no changed intent can be replayed.');
      this.successorPreview.set(preview); this.successorLinePage.set(0); this.successorRecoveryReady.set(true);
      this.successorMessage.set('Original intent is still exact and ready. Review the population and give fresh assent before explicit recovery.');
    } catch (error) { this.successorMessage.set(error instanceof Error ? error.message : 'Original preview is unavailable.'); }
  }
  async createSuccessor(): Promise<void> {
    const preview = this.successorPreview(); const old = this.successorPending();
    if (this.busy() || this.successorBusy() || !this.successorReviewed() || old && (old.action !== 'CREATE' || !this.successorRecoveryReady()) ||
        !old && (!preview?.ready || !this.successorIntent)) return;
    const pending: SuccessorPending = old ?? { action: 'CREATE', request: { ...this.successorIntent!, reviewBasis: preview!.reviewBasis, reviewed: true },
      manifestHash: preview!.prospectiveOpeningManifestHash };
    await this.sendSuccessor(pending);
  }
  async approveSuccessor(): Promise<void> {
    const view = this.successorView(), catalog = this.successorCatalog(), old = this.successorPending();
    if (!view || !catalog?.canApprove || !view.isCurrent || this.busy() || this.successorBusy() || !this.successorApprovalReviewed() ||
        view.opening.createdByUserId === this.userId() || old && old.action !== 'APPROVE') return;
    const pending: SuccessorPending = old ?? { action: 'APPROVE', manifestHash: view.opening.manifestSha256,
      request: { requestId: crypto.randomUUID(), clientId: this.clientId(), engagementId: view.engagementId, openingId: view.opening.id,
        expectedPeriodRevision: view.opening.periodRevision, expectedOpeningManifestHash: view.opening.manifestSha256,
        expectedSuccessorProofHash: view.proofHash, reviewed: true } };
    await this.sendSuccessor(pending);
  }
  private async sendSuccessor(pending: SuccessorPending): Promise<void> {
    if (!this.drafts.save(this.successorDraftScope(), pending, decodeSuccessorPending, true)) {
      this.successorMessage.set('The exact request could not be retained in this tab. No write was sent.'); return;
    }
    const current = this.successorOwner(), wasUncertain = this.successorUncertain();
    this.successorPending.set(pending); this.successorRecoveryReady.set(false); this.successorBusy.set(true); this.successorReviewed.set(false); this.successorApprovalReviewed.set(false);
    const path = pending.action === 'CREATE' ? '/create' : `/${pending.request.openingId}/approve`;
    const result = await this.api.command<unknown>(this.successorBase() + path, pending.request);
    if (!current()) return;
    this.successorBusy.set(false);
    if (!result.ok) {
      this.successorUncertain.set(result.unknown || wasUncertain); this.successorMessage.set(result.message);
      if (!result.unknown && !wasUncertain) {
        this.clearSuccessorPending(result.message); this.successorPreview.set(null); this.successorIntent = null;
      }
      return;
    }
    this.successorUncertain.set(true);
    await this.loadSuccessorCatalog();
  }

}

/** Route adapter only; the existing operational opening owner retains all manual/native actions. */
@Component({
  selector: 'audit-native-successor-opening-page',
  imports: [ClientOperationalOpeningBalances, RouterLink, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app/accounting">Accounting workspace</a> / Native successor opening</nav>
    <h1>Native successor opening</h1>
    <audit-state [loading]="context.loading()" [error]="context.error()" label="authorized native client periods" />
    @if (context.data(); as client) {
      @if (client.profile; as profile) {
        <p>{{ client.name }} · choose an existing target period with its actual predecessor linkage. No period is created or activated here.</p>
        @if (client.hasMorePeriods) { <p role="status">Only the latest 100 periods are available here. No undisplayed target is selected.</p> }
        <audit-client-operational-opening-balances [clientId]="client.clientId" [currency]="profile.currency" [periods]="client.periods" />
      }
    }
  `,
})
export class NativeSuccessorOpeningPage {
  private readonly api = inject(Api);
  readonly clientId = routeGuid('clientId');
  readonly context = this.api.resource(() => this.clientId() ? `/api/ui/accounting/clients/${this.clientId()}` : null,
    raw => decodeNativeContext(raw, this.clientId()!));
}
