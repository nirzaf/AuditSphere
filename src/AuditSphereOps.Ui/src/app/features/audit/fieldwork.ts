import { DatePipe } from '@angular/common';
import { afterNextRender, Component, computed, DestroyRef, effect, ElementRef, HostListener, inject, Injector, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { Api, CommandState, routeGuid } from '../../core/api';
import { decodeBankReconciliationCommandLookup, decodeBankReconciliationCommandPreview, decodeBankReconciliationCommandReceipt,
  decodeBankReconciliationCommandState, decodeBankReconciliationDetail, decodeBankReconciliationPage,
  decodeFieldwork, decodeManagerReview, decodeProgramOptions, focusRisks, type Fieldwork } from './fieldwork-contracts';
import { SessionService } from '../../core/session';
import { PlanningCommandState } from './planning-command-state';
import { PlanningCommandReview } from './planning-command-review';
import { SHARED } from '../../core/ui';
import { TabDrafts } from '../../core/tab-drafts';
import { FieldworkProcedureState } from './fieldwork-procedure-state';
import { ProcedureSubmittedEvidence } from './procedure-submitted-evidence';
import { FieldworkSamplingState } from './fieldwork-sampling-state';
import { FieldworkSourceIntakeState } from './fieldwork-source-intake-state';
import type { FieldworkCommandAcquire, FieldworkCommandRecovery } from './fieldwork-command-context';

type BankAction = 'CREATE' | 'REVIEW';
type BankCommandBody = {
  requestId: string; expectedGeneration: string; reviewBasis: string; expectedRequestHash: string | null; reviewed: boolean;
  create?: { procedureId: string | null; ledgerScheduleId: string; statementScheduleId: string; asOfDate: string;
    items: { stableItemId: string; itemType: string; signedAmount: string; description: string; sourceReference: string;
      evidenceReference: string; sourceScheduleId: string | null; proposedJournalId: string | null }[] };
  review?: { reconciliationId: string; decision: string; conclusion: string };
};
type BankItemDraft = NonNullable<BankCommandBody['create']>['items'][number];
type PendingBankCommand = { engagementId: string; action: BankAction; body: BankCommandBody; requestHash: string };

function bankCommandPending(raw: unknown): PendingBankCommand | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const p = raw as Record<string, unknown>, body = p['body'];
  const guid = (x: unknown): x is string => typeof x === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(x);
  const hash = (x: unknown): x is string => typeof x === 'string' && /^[a-f0-9]{64}$/.test(x);
  if (!guid(p['engagementId']) || (p['action'] !== 'CREATE' && p['action'] !== 'REVIEW') || !hash(p['requestHash']) ||
      !body || typeof body !== 'object' || Array.isArray(body)) return null;
  const b = body as Record<string, unknown>;
  if (!guid(b['requestId']) || !hash(b['reviewBasis']) || !hash(b['expectedRequestHash']) || b['expectedRequestHash'] !== p['requestHash'] || b['requestHash'] !== undefined ||
      typeof b['expectedGeneration'] !== 'string' || !/^[1-9][0-9]{0,18}$/.test(b['expectedGeneration']) || b['reviewed'] !== true) return null;
  if (p['action'] === 'CREATE') {
    const c = b['create'];
    if (!c || typeof c !== 'object' || Array.isArray(c) || b['review'] !== undefined) return null;
    const x = c as Record<string, unknown>;
    if (!guid(x['ledgerScheduleId']) || !guid(x['statementScheduleId']) || !(x['procedureId'] === null || guid(x['procedureId'])) ||
        typeof x['asOfDate'] !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(x['asOfDate']) ||
        !Array.isArray(x['items']) || x['items'].length < 1 || x['items'].length > 1000) return null;
    for (const item of x['items']) {
      if (!item || typeof item !== 'object' || Array.isArray(item)) return null;
      const i = item as Record<string, unknown>;
      if (typeof i['stableItemId'] !== 'string' || !i['stableItemId'].trim() || i['stableItemId'].length > 200 ||
          !['LEDGER', 'STATEMENT', 'TIMING', 'PROPOSED_CORRECTION'].includes(String(i['itemType'])) ||
          typeof i['signedAmount'] !== 'string' || !/^-?\d{1,13}(\.\d{1,6})?$/.test(i['signedAmount']) ||
          typeof i['description'] !== 'string' || !i['description'].trim() || i['description'].length > 1000 ||
          typeof i['sourceReference'] !== 'string' || !i['sourceReference'].trim() || i['sourceReference'].length > 500 ||
          typeof i['evidenceReference'] !== 'string' || !i['evidenceReference'].trim() || i['evidenceReference'].length > 2000 ||
          !(i['sourceScheduleId'] === null || guid(i['sourceScheduleId'])) || !(i['proposedJournalId'] === null || guid(i['proposedJournalId']))) return null;
    }
  } else {
    const r = b['review'];
    if (!r || typeof r !== 'object' || Array.isArray(r) || b['create'] !== undefined) return null;
    const x = r as Record<string, unknown>;
    if (!guid(x['reconciliationId']) || !['APPROVED', 'CHANGES_REQUIRED'].includes(String(x['decision'])) ||
        typeof x['conclusion'] !== 'string' || !x['conclusion'].trim() || x['conclusion'].length > 4000) return null;
  }
  return { engagementId: p['engagementId'], action: p['action'], body: b as unknown as BankCommandBody, requestHash: p['requestHash'] };
}

@Component({
  selector: 'audit-fieldwork',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatTabsModule, PlanningCommandReview, ProcedureSubmittedEvidence, ...SHARED],
  templateUrl: './fieldwork.html',
  styles: '.submitted-evidence-json { white-space:pre-wrap; overflow-wrap:anywhere; max-inline-size:100%; }',
})
export class AuditFieldwork {
  private readonly api = inject(Api);
  private readonly tabDrafts = inject(TabDrafts);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly samplingPreviewTrigger = viewChild<unknown, ElementRef<HTMLButtonElement>>('samplingPreviewTrigger', { read: ElementRef });
  private readonly renderInjector = inject(Injector);
  private readonly samplingPreviewHeading = viewChild<ElementRef<HTMLElement>>('samplingPreviewHeading');
  private readonly procedurePanel = viewChild<ElementRef<HTMLElement>>('procedurePanel');
  private readonly procedurePreviewHeading = viewChild<ElementRef<HTMLElement>>('procedurePreviewHeading');
  private focusInteraction = 0;
  readonly id = routeGuid();
  readonly statementFocus = { line: this.route.snapshot?.queryParamMap.get('line') ?? null, area: this.route.snapshot?.queryParamMap.get('area') ?? null };
  readonly focusedRisks = computed(() => focusRisks(this.ws.data()?.risks ?? [], this.statementFocus.line));
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/fieldwork` : null), decodeFieldwork,
    'Fieldwork unavailable: an authorized internal engagement scope is required.');
  readonly programOptions = this.api.resource(() => this.id() && this.ws.data()?.canManageFieldwork
    ? `/api/ui/engagements/${this.id()}/fieldwork/program-options` : null, decodeProgramOptions,
    'Published programme options unavailable for this authorized engagement.');
  readonly managerReview = this.api.resource(() => this.id() && this.ws.data()?.canReviewSelections
    ? `/api/ui/engagements/${this.id()}/manager-review` : null, decodeManagerReview,
    'Manager review summary unavailable: an authorized reviewer scope is required.');
  readonly bankInspectionOpen = signal(false);
  readonly bankOffset = signal(0);
  readonly bankSelection = signal<string | null>(null);
  readonly bankItemOffset = signal(0);
  readonly bankCommandState = this.api.resource(() => this.bankInspectionOpen() && this.id() &&
    (this.ws.data()?.canManageFieldwork || this.ws.data()?.canReviewSelections)
      ? `/api/ui/engagements/${this.id()}/fieldwork/bank-reconciliations/command-state${this.bankSelection() ? `?reconciliationId=${this.bankSelection()}` : ''}`
      : null, decodeBankReconciliationCommandState,
    'Bank command eligibility and exact source basis are unavailable for this engagement.');
  readonly bankLedgerScheduleId = signal('');
  readonly bankStatementScheduleId = signal('');
  readonly bankAsOfDate = signal('');
  readonly bankItems = signal<BankItemDraft[]>([]);
  readonly bankReviewDecision = signal('APPROVED');
  readonly bankReviewConclusion = signal('');
  readonly bankCommandPreview = signal<ReturnType<typeof decodeBankReconciliationCommandPreview> | null>(null);
  readonly bankCommandReceipt = signal<ReturnType<typeof decodeBankReconciliationCommandReceipt> | null>(null);
  readonly bankCommandPending = signal<PendingBankCommand | null>(null);
  readonly bankCommandAbsent = signal(false);
  readonly bankCommandReviewed = signal(false);
  readonly bankCommandBusy = signal(false);
  readonly bankCommandMessage = signal('');
  readonly bankCommandFailed = signal(false);
  readonly bankCommandStorageUnavailable = signal(false);
  private bankCommandBody: BankCommandBody | null = null;
  private bankCommandFingerprint = '';
  private readonly bankCommandHeading = viewChild<ElementRef<HTMLElement>>('bankCommandHeading');
  private readonly bankInspectionHeading = viewChild<ElementRef<HTMLElement>>('bankInspectionHeading');
  private bankFocusRequest: { current: () => boolean; interaction: number; origin: HTMLElement; version: number;
    target: 'command' | 'inspection' } | null = null;
  private bankFocusVersion = 0;
  readonly bankList = this.api.resource(() => this.bankInspectionOpen() && this.id() && this.ws.data()?.canReviewSelections && this.managerReview.data()
    ? `/api/ui/engagements/${this.id()}/fieldwork/bank-reconciliations?offset=${this.bankOffset()}&limit=25` : null,
    (raw, path) => {
      const page = decodeBankReconciliationPage(raw, path);
      if (page.engagementId !== this.id() || page.offset !== this.bankOffset() || page.limit !== 25)
        throw new Error('Bank evidence does not match this engagement or page.');
      return page;
    }, 'Bank reconciliation inspection is unavailable in your current reviewer scope.');
  readonly bankDetail = this.api.resource(() => this.bankList.data()?.rows.some(row => row.id === this.bankSelection())
    ? `/api/ui/engagements/${this.id()}/fieldwork/bank-reconciliations/${this.bankSelection()}?offset=${this.bankItemOffset()}&limit=100` : null,
    (raw, path) => {
      const detail = decodeBankReconciliationDetail(raw, path);
      if (detail.engagementId !== this.id() || detail.reconciliation.id !== this.bankSelection() ||
          detail.offset !== this.bankItemOffset() || detail.limit !== 100)
        throw new Error('Bank evidence does not match the selected record or page.');
      return detail;
    }, 'The selected bank reconciliation is unavailable in your current reviewer scope.');
  readonly cmd = new CommandState(this.api);
  readonly riskLink = new PlanningCommandState(() => this.id(), () => this.ws.data(),
    () => !!this.ws.data()?.canManageFieldwork && !this.ws.loading() && !this.ws.error(),
    () => this.otherFieldworkCommandBlocked(), () => this.ws.reload(), ['LINK_PROCEDURE_RISK']);
  readonly section = signal<number | null>(null);
  readonly toolTab = signal(0);
  private readonly samplingToolTabIndex = 1;
  readonly procedure: FieldworkProcedureState;
  readonly sampling: FieldworkSamplingState;
  readonly sourceIntake: FieldworkSourceIntakeState;
  private activeEngagementId: string | null = null;
  private readonly riskLinkSelections = new Map<string, string>();
  private readonly riskLinkBases = new Map<string, string | null>();
  private readonly riskLinkSession = inject(SessionService);
  aggregateConclusion = '';
  readonly previousScopeOutcomeUncertain = signal(false);
  private ownerGeneration = 0;
  private commandGeneration = 0;
  private observedOwner = '';
  private destroyed = false;
  private readonly pendingPriorScopeCommands = new Set<number>();
  private priorScopeCommandUnknown = false;

  constructor() {
    const context = {
      api: this.api, tabDrafts: this.tabDrafts, id: this.id, ws: this.ws, cmd: this.cmd,
      session: this.riskLinkSession, send: (url: string, body: unknown, ok: string, after?: () => void) => this.send(url, body, ok, after),
      acquireCommand: (recovery?: FieldworkCommandRecovery) => this.acquireFieldworkCommand(recovery),
      captureOwner: () => this.captureOwner(), destroyRef: this.destroyRef,
    };
    this.procedure = new FieldworkProcedureState(context);
    this.sampling = new FieldworkSamplingState({ ...context });
    this.sourceIntake = new FieldworkSourceIntakeState({
      id: this.id, api: this.api, session: this.riskLinkSession, drafts: this.tabDrafts,
      canManage: () => !!this.ws.data()?.canManageFieldwork && !this.ws.loading() && !this.ws.error(),
      canReview: () => !!this.ws.data()?.canReviewSelections && !this.ws.loading() && !this.ws.error(),
      acquireCommand: this.acquireFieldworkCommand, captureOwner: () => this.captureOwner(), reload: () => this.ws.reload(),
    });
    this.destroyRef.onDestroy(() => { this.destroyed = true; ++this.ownerGeneration; ++this.commandGeneration; });
    effect(() => {
      this.id(); this.riskLinkSession.current(); this.riskLinkSession.invalidation();
      untracked(() => this.observeOwner());
    });
    effect(() => {
      this.ws.data();
      untracked(() => { this.procedure.workspaceChanged(); this.sourceIntake.workspaceChanged(); });
    });
    effect(() => {
      this.id(); this.riskLinkSession.current(); this.riskLinkSession.invalidation();
      untracked(() => { this.riskLinkSelections.clear(); this.riskLinkBases.clear(); });
    });
    effect(() => {
      const w = this.ws.data();
      untracked(() => {
        if (!w) return;
        if (!w.canManageFieldwork) this.riskLinkSelections.clear();
        for (const [id] of this.riskLinkSelections) if (!w.procedures.some(p => p.id === id)) this.riskLinkSelections.delete(id);
        for (const item of w.procedures) {
          if (this.riskLinkBases.has(item.id) && this.riskLinkBases.get(item.id) !== item.riskId) this.riskLinkSelections.delete(item.id);
          this.riskLinkBases.set(item.id, item.riskId);
        }
      });
    });
    effect(() => {
      this.bankList.data(); this.bankList.error(); const listLoading = this.bankList.loading();
      this.bankDetail.data(); this.bankDetail.error(); const detailLoading = this.bankDetail.loading();
      untracked(() => {
        const request = this.bankFocusRequest;
        if (!request || listLoading || detailLoading || !this.bankInspectionOpen()) return;
        this.bankFocusRequest = null;
        afterNextRender(() => {
          const doc = request.origin.ownerDocument;
          if (!request.current() || request.version !== this.bankFocusVersion || request.interaction !== this.focusInteraction ||
              !this.bankInspectionOpen() || (doc.activeElement !== request.origin && doc.activeElement !== doc.body)) return;
          (request.target === 'command' ? this.bankCommandHeading() ?? this.bankInspectionHeading()
            : this.bankInspectionHeading() ?? this.bankCommandHeading())?.nativeElement.focus();
        }, { injector: this.renderInjector });
      });
    });
    effect(() => {
      const state = this.bankCommandState.data(); this.bankCommandState.loading(); this.bankCommandState.error();
      untracked(() => { if (state && Array.isArray(state.sources)) this.chooseDefaultBankSources(state); });
    });
    // Aggregate conclusion presentation stays in the host; sampling history is owned by its state object.
    effect(() => {
      const w = this.ws.data();
      this.ws.loading(); this.ws.error();
      const engagementId = this.id();
      untracked(() => {
        if (engagementId !== this.activeEngagementId) {
          this.activeEngagementId = engagementId;
          this.riskLinkSelections.clear();
        }
        this.aggregateConclusion = w?.aggregate?.conclusion ?? '';
        this.sampling.workspaceUnavailable();
        this.sourceIntake.workspaceUnavailable();
      });
    });
  }
  private observeOwner(): void {
    const actor = this.riskLinkSession.current();
    const owner = `${this.id() ?? ''}:${actor?.userId ?? ''}:${actor?.generation ?? ''}:${this.riskLinkSession.invalidation()}`;
    if (owner === this.observedOwner) return;
    this.observedOwner = owner;
    const wasBusy = this.cmd.busy();
    if (wasBusy) this.pendingPriorScopeCommands.add(this.commandGeneration);
    ++this.ownerGeneration; ++this.commandGeneration;
    this.procedure.ownerChanged(); this.sampling.ownerChanged(); this.sourceIntake.ownerChanged();
    this.bankInspectionOpen.set(false); this.bankSelection.set(null); this.bankOffset.set(0); this.bankItemOffset.set(0);
    this.bankLedgerScheduleId.set(''); this.bankStatementScheduleId.set(''); this.bankAsOfDate.set(''); this.bankItems.set([]);
    this.bankCommandPreview.set(null); this.bankCommandReceipt.set(null); this.bankCommandPending.set(null);
    this.bankCommandAbsent.set(false); this.bankCommandReviewed.set(false); this.bankCommandMessage.set(''); this.bankCommandFailed.set(false);
    this.bankCommandStorageUnavailable.set(false);
    this.bankCommandBody = null; this.bankCommandFingerprint = '';
    this.bankFocusRequest = null; ++this.bankFocusVersion;
    this.cmd.busy.set(false); this.cmd.message.set(''); this.cmd.failed.set(false); this.cmd.code.set('');
    this.refreshPriorScopeNotice();
    this.restoreBankPending();
  }
  private requestBankFocus(trigger: EventTarget | null): void {
    ++this.bankFocusVersion;
    this.bankFocusRequest = trigger instanceof HTMLElement && trigger.ownerDocument.activeElement === trigger
      ? { origin: trigger, interaction: this.focusInteraction, current: this.captureOwner(), version: this.bankFocusVersion,
        target: trigger.getAttribute('aria-controls') === 'bank-reconciliation-command-workspace' ? 'command' : 'inspection' } : null;
  }
  openBankInspection(trigger: EventTarget | null): void {
    const w = this.ws.data();
    if (!w || (!w.canManageFieldwork && (!w.canReviewSelections || !this.managerReview.data()))) return;
    if (this.bankInspectionOpen()) { (this.bankCommandHeading() ?? this.bankInspectionHeading())?.nativeElement.focus(); return; }
    this.requestBankFocus(trigger);
    this.bankInspectionOpen.set(true);
    this.restoreBankPending();
  }
  closeBankInspection(trigger: EventTarget | null): void {
    const ownsFocus = trigger instanceof HTMLElement && trigger.ownerDocument.activeElement === trigger;
    // The count trigger remains mounted: return focus before removing the inspection panel.
    if (ownsFocus) (this.bankInspectionTrigger() ?? this.bankCommandTrigger())?.nativeElement.focus();
    this.bankInspectionOpen.set(false); this.bankSelection.set(null); this.bankFocusRequest = null; ++this.bankFocusVersion;
    this.bankCommandPreview.set(null); this.bankCommandReviewed.set(false); this.bankCommandBody = null; this.bankCommandFingerprint = '';
  }
  private readonly bankInspectionTrigger = viewChild<unknown, ElementRef<HTMLButtonElement>>('bankInspectionTrigger', { read: ElementRef });
  private readonly bankCommandTrigger = viewChild<unknown, ElementRef<HTMLButtonElement>>('bankCommandTrigger', { read: ElementRef });
  bankPage(offset: number, trigger: EventTarget | null): void {
    if (this.bankList.loading() || !this.bankList.data() || offset < 0) return;
    this.requestBankFocus(trigger); this.bankSelection.set(null); this.bankItemOffset.set(0); this.bankOffset.set(offset);
  }
  inspectBank(id: string, trigger: EventTarget | null): void {
    if (!this.bankList.data()?.rows.some(row => row.id === id) || this.bankDetail.loading()) return;
    this.requestBankFocus(trigger); this.bankItemOffset.set(0); this.bankSelection.set(id); this.bankCommandInputChanged();
  }
  bankItemsPage(offset: number, trigger: EventTarget | null): void {
    if (this.bankDetail.loading() || !this.bankDetail.data() || offset < 0) return;
    this.requestBankFocus(trigger); this.bankItemOffset.set(offset);
  }
  refreshBankInspection(trigger: EventTarget | null): void {
    if (this.bankList.loading() || this.bankDetail.loading()) return;
    this.requestBankFocus(trigger); this.bankList.reload();
  }
  private chooseDefaultBankSources(state: ReturnType<typeof decodeBankReconciliationCommandState>): void {
    if (this.bankSelection() || this.bankLedgerScheduleId() || this.bankStatementScheduleId()) return;
    const ledger = state.sources.find(x => x.scheduleType === 'BANK_LEDGER' && x.asOfDate);
    const statement = ledger && state.sources.find(x => x.scheduleType === 'BANK_STATEMENT' && x.asOfDate === ledger.asOfDate && x.currency === ledger.currency);
    if (!ledger || !statement) return;
    this.bankLedgerScheduleId.set(ledger.id); this.bankStatementScheduleId.set(statement.id); this.bankAsOfDate.set(ledger.asOfDate!);
  }
  setBankLedgerSchedule(id: string): void {
    this.bankLedgerScheduleId.set(id); this.bankCommandInputChanged();
    const ledger = this.bankCommandState.data()?.sources.find(x => x.id === id);
    const statement = this.bankCommandState.data()?.sources.find(x => x.scheduleType === 'BANK_STATEMENT' &&
      x.currency === ledger?.currency && x.asOfDate === ledger.asOfDate);
    if (statement) { this.bankStatementScheduleId.set(statement.id); this.bankAsOfDate.set(statement.asOfDate ?? ''); }
  }
  setBankStatementSchedule(id: string): void {
    this.bankStatementScheduleId.set(id); this.bankCommandInputChanged();
    const statement = this.bankCommandState.data()?.sources.find(x => x.id === id);
    if (statement?.asOfDate) this.bankAsOfDate.set(statement.asOfDate);
  }
  addBankItem(): void {
    const items = this.bankItems();
    this.bankItems.set([...items, { stableItemId: `bank-item-${crypto.randomUUID()}`, itemType: 'TIMING', signedAmount: '0.000000',
      description: '', sourceReference: '', evidenceReference: '', sourceScheduleId: this.bankStatementScheduleId() || null, proposedJournalId: null }]);
    this.bankCommandInputChanged();
  }
  bankSources(type: 'BANK_LEDGER' | 'BANK_STATEMENT') {
    return this.bankCommandState.data()?.sources.filter(x => x.scheduleType === type) ?? [];
  }
  updateBankItem(item: BankItemDraft, patch: Partial<BankItemDraft>): void {
    Object.assign(item, patch); this.bankItems.set([...this.bankItems()]); this.bankCommandInputChanged();
  }
  removeBankItem(item: BankItemDraft): void {
    this.bankItems.set(this.bankItems().filter(x => x !== item)); this.bankCommandInputChanged();
  }
  bankItemTypeChanged(item: BankItemDraft, type: string): void {
    item.itemType = type;
    item.sourceScheduleId = type === 'LEDGER' ? this.bankLedgerScheduleId() || null :
      type === 'PROPOSED_CORRECTION' ? null : this.bankStatementScheduleId() || null;
    if (type !== 'PROPOSED_CORRECTION') item.proposedJournalId = null;
    this.bankItems.set([...this.bankItems()]); this.bankCommandInputChanged();
  }
  bankCommandInputChanged(): void {
    if (this.bankCommandBusy()) return;
    this.bankCommandPreview.set(null); this.bankCommandReviewed.set(false); this.bankCommandReceipt.set(null);
    this.bankCommandBody = null; this.bankCommandFingerprint = '';
    if (!this.bankCommandPending()) { this.bankCommandMessage.set(''); this.bankCommandFailed.set(false); }
  }
  private bankInputFingerprint(action: BankAction): string {
    return action === 'CREATE' ? JSON.stringify({ ledger: this.bankLedgerScheduleId(), statement: this.bankStatementScheduleId(),
      asOfDate: this.bankAsOfDate(), items: this.bankItems() }) : JSON.stringify({ reconciliationId: this.bankSelection(),
      decision: this.bankReviewDecision(), conclusion: this.bankReviewConclusion() });
  }
  private bankCommandPath(): string { return `/api/ui/engagements/${this.id()}/fieldwork/bank-reconciliations`; }
  private bankCommandScope(pending: PendingBankCommand, basis: string) {
    return { entity: `audit-bank-reconciliation/${pending.engagementId}/${pending.action}`, baseRevision: basis };
  }
  private restoreBankPending(): void {
    const engagementId = this.id();
    if (!engagementId || !this.riskLinkSession.current()?.staff) return;
    const entity = `audit-bank-reconciliation/${engagementId}/CREATE`;
    const create = this.tabDrafts.readPendingSubmission({ entity, baseRevision: '0'.repeat(64) }, bankCommandPending);
    const reviewEntity = `audit-bank-reconciliation/${engagementId}/REVIEW`;
    const review = this.tabDrafts.readPendingSubmission({ entity: reviewEntity, baseRevision: '0'.repeat(64) }, bankCommandPending);
    if (create.state === 'unavailable' || review.state === 'unavailable') {
      this.bankCommandStorageUnavailable.set(true); this.bankCommandFailed.set(true);
      this.bankCommandMessage.set('Saved bank-command recovery storage is unavailable. Do not submit a replacement request.'); return;
    }
    this.bankCommandStorageUnavailable.set(false);
    const pending = create.state === 'ready' ? create.draft.value : review.state === 'ready' ? review.draft.value : null;
    if (!pending || pending.engagementId !== engagementId) return;
    this.bankCommandPending.set(pending); this.bankCommandAbsent.set(false); this.bankCommandPreview.set(null);
    this.bankCommandReviewed.set(false); this.bankCommandMessage.set('A bank command may have completed. Look up its retained receipt before any retry.');
  }
  private beginBankOperation(allowPending = false): (() => void) | null {
    if (this.cmd.busy() || this.bankCommandBusy() || this.riskLink.busy() || this.riskLink.uncertain() ||
        this.procedure.hasUnresolvedCommand() || this.sourceIntake.hasUnresolvedCommand() ||
        this.sampling.samplingBusy() || this.sampling.sampleActionBusy() ||
        this.bankCommandPending() && !allowPending) return null;
    const owner = this.captureOwner(), operation = ++this.commandGeneration;
    this.cmd.busy.set(true); this.bankCommandBusy.set(true);
    return () => { this.bankCommandBusy.set(false); if (operation === this.commandGeneration && owner()) this.cmd.busy.set(false); };
  }
  async prepareBankCreate(): Promise<void> {
    const state = this.bankCommandState.data();
    if (!state?.canCreate || this.bankCommandPending() || this.bankCommandBusy()) return;
    const items = this.bankItems().map(x => ({ ...x, signedAmount: x.signedAmount.trim() }));
    const body: BankCommandBody = { requestId: crypto.randomUUID(), expectedGeneration: state.expectedGeneration,
      reviewBasis: state.reviewBasis, expectedRequestHash: null, reviewed: false,
      create: { procedureId: null, ledgerScheduleId: this.bankLedgerScheduleId(), statementScheduleId: this.bankStatementScheduleId(),
        asOfDate: this.bankAsOfDate(), items } };
    await this.previewBankCommand('CREATE', body);
  }
  async prepareBankReview(): Promise<void> {
    const state = this.bankCommandState.data(), record = state?.record;
    if (!state?.canReview || !record || this.bankCommandPending() || this.bankCommandBusy()) return;
    const body: BankCommandBody = { requestId: crypto.randomUUID(), expectedGeneration: state.expectedGeneration,
      reviewBasis: state.reviewBasis, expectedRequestHash: null, reviewed: false,
      review: { reconciliationId: record.id, decision: this.bankReviewDecision(), conclusion: this.bankReviewConclusion() } };
    await this.previewBankCommand('REVIEW', body);
  }
  private async previewBankCommand(action: BankAction, body: BankCommandBody): Promise<void> {
    const release = this.beginBankOperation(); if (!release) return;
    const owner = this.captureOwner();
    this.bankCommandMessage.set(''); this.bankCommandFailed.set(false); this.bankCommandReceipt.set(null);
    try {
      const suffix = action === 'CREATE' ? '/create/preview' : `/${body.review!.reconciliationId}/review/preview`;
      const result = await this.api.command(this.bankCommandPath() + suffix, body);
      if (!owner()) return;
      if (!result.ok) { this.bankCommandFailed.set(true); this.bankCommandMessage.set(result.message); return; }
      const preview = decodeBankReconciliationCommandPreview(result.value, 'bank-reconciliation.preview');
      const state = this.bankCommandState.data();
      if (!state || preview.action !== action || preview.requestId !== body.requestId || preview.reviewBasis !== body.reviewBasis ||
          preview.expectedGeneration !== body.expectedGeneration || state.reviewBasis !== body.reviewBasis || !preview.canProceed)
        throw new Error('The bank command preview does not match the current source basis.');
      this.bankCommandBody = body; this.bankCommandFingerprint = this.bankInputFingerprint(action);
      this.bankCommandPreview.set(preview); this.bankCommandReviewed.set(false);
      if (action === 'CREATE' && preview.calculated) this.bankCommandMessage.set(preview.summary);
      else this.bankCommandMessage.set(preview.summary);
    } catch {
      if (owner()) { this.bankCommandFailed.set(true); this.bankCommandMessage.set('The exact bank-command preview could not be verified. Refresh sources and review again.'); }
    } finally { release(); }
  }
  async executeBankCommand(): Promise<void> {
    const preview = this.bankCommandPreview(), body = this.bankCommandBody, state = this.bankCommandState.data();
    const pending = this.bankCommandPending();
    if (!preview?.canProceed || !body || !state || !this.bankCommandReviewed() || state.reviewBasis !== preview.reviewBasis ||
        state.expectedGeneration !== preview.expectedGeneration || this.bankInputFingerprint(preview.action as BankAction) !== this.bankCommandFingerprint ||
        this.bankCommandStorageUnavailable() || pending && (!this.bankCommandAbsent() || pending.requestHash !== preview.requestHash || pending.body.requestId !== preview.requestId)) return;
    const action = preview.action as BankAction;
    const submitted: BankCommandBody = { ...body, expectedRequestHash: preview.requestHash, reviewed: true };
    const recovery: PendingBankCommand = { engagementId: this.id()!, action, body: submitted, requestHash: preview.requestHash };
    const scope = this.bankCommandScope(recovery, preview.reviewBasis);
    if (!this.tabDrafts.save(scope, recovery, bankCommandPending, true)) {
      this.bankCommandFailed.set(true); this.bankCommandMessage.set('The exact request could not be saved for recovery. No bank command was sent.'); return;
    }
    const release = this.beginBankOperation(!!pending); if (!release) return;
    this.bankCommandPending.set(recovery); this.bankCommandAbsent.set(false); this.bankCommandPreview.set(null);
    this.bankCommandReviewed.set(false); this.bankCommandMessage.set('');
    const owner = this.captureOwner();
    try {
      const suffix = action === 'CREATE' ? '/create' : `/${submitted.review!.reconciliationId}/review`;
      const result = await this.api.command(this.bankCommandPath() + suffix, submitted);
      if (!owner()) return;
      if (!result.ok) {
        this.bankCommandFailed.set(true);
        this.bankCommandMessage.set(result.unknown || result.code === 'idempotency.conflict'
          ? 'The outcome is uncertain. Look up this exact receipt before retrying.'
          : result.message);
        if (!result.unknown && result.code !== 'idempotency.conflict') {
          this.tabDrafts.clear(scope.entity); this.bankCommandPending.set(null);
        }
        return;
      }
      const receipt = decodeBankReconciliationCommandReceipt(result.value, 'bank-reconciliation.receipt');
      if (!this.matchesBankReceipt(receipt, recovery, state.clientId)) throw new Error('The retained bank receipt does not match this request.');
      this.bankCommandReceipt.set(receipt);
      const cleared = this.tabDrafts.clear(scope.entity);
      if (cleared) this.bankCommandPending.set(null);
      this.bankCommandAbsent.set(false); this.bankCommandFailed.set(false);
      this.bankCommandMessage.set(cleared ? receipt.preview.summary : 'Receipt verified. Recovery reference could not be cleared; retain it and look up the same receipt again.');
      if (action === 'CREATE') this.bankSelection.set(receipt.bankReconciliationId);
      this.bankCommandState.reload(); this.bankList.reload(); this.bankDetail.reload();
    } catch {
      if (owner()) { this.bankCommandFailed.set(true); this.bankCommandMessage.set('The bank-command response was lost or could not be verified. Look up this exact receipt; do not submit a new request.'); }
    } finally { release(); }
  }
  private matchesBankReceipt(receipt: ReturnType<typeof decodeBankReconciliationCommandReceipt>, pending: PendingBankCommand, clientId?: string): boolean {
    const actor = this.riskLinkSession.current();
    return !!actor && receipt.actorId === actor.userId && receipt.actorEpoch === actor.generation &&
      receipt.engagementId === pending.engagementId && (!clientId || receipt.clientId === clientId) &&
      receipt.action === pending.action && receipt.requestId === pending.body.requestId &&
      receipt.requestHash === pending.requestHash && receipt.reviewBasis === pending.body.reviewBasis &&
      (pending.action !== 'REVIEW' || receipt.bankReconciliationId === pending.body.review?.reconciliationId);
  }
  async reconcileBankCommand(): Promise<void> {
    const pending = this.bankCommandPending();
    if (!pending || this.bankCommandBusy() || pending.engagementId !== this.id()) return;
    const owner = this.captureOwner(); this.bankCommandBusy.set(true); this.bankCommandFailed.set(false);
    try {
      const target = pending.action === 'REVIEW' ? `?reconciliationId=${pending.body.review!.reconciliationId}` : '';
      const state = await this.api.get(this.bankCommandPath() + '/command-state' + target, decodeBankReconciliationCommandState);
      if (!owner() || state.engagementId !== pending.engagementId || !state.canCreate && pending.action === 'CREATE' || !state.canReview && pending.action === 'REVIEW')
        throw new Error('The current bank-command scope is no longer authorized.');
      const query = new URLSearchParams({ requestHash: pending.requestHash, action: pending.action });
      const lookup = await this.api.get(this.bankCommandPath() + `/receipts/${pending.body.requestId}?${query}`, decodeBankReconciliationCommandLookup);
      if (!owner()) return;
      if (!lookup.found) {
        this.bankCommandAbsent.set(true);
        this.bankCommandMessage.set('No receipt was found at lookup time. A delayed request may still commit. The original intent is retained; retry only after reviewing it again.');
        return;
      }
      if (!lookup.receipt || !this.matchesBankReceipt(lookup.receipt, pending, state.clientId)) throw new Error('The recovered receipt does not match this actor, scope or request.');
      this.bankCommandReceipt.set(lookup.receipt);
      const cleared = this.tabDrafts.clear(this.bankCommandScope(pending, pending.body.reviewBasis).entity);
      if (cleared) this.bankCommandPending.set(null);
      this.bankCommandAbsent.set(false);
      this.bankCommandMessage.set(cleared ? lookup.receipt.preview.summary : 'Receipt verified. Recovery reference could not be cleared; look up this same receipt again.');
      if (pending.action === 'CREATE') this.bankSelection.set(lookup.receipt.bankReconciliationId);
      this.bankCommandState.reload(); this.bankList.reload(); this.bankDetail.reload();
    } catch {
      if (owner()) { this.bankCommandFailed.set(true); this.bankCommandMessage.set('Exact bank receipt lookup failed. Keep the original request identity and do not retry blindly.'); }
    } finally { this.bankCommandBusy.set(false); }
  }
  async retryBankCommandAfterLookup(): Promise<void> {
    const pending = this.bankCommandPending();
    if (!pending || !this.bankCommandAbsent() || this.bankCommandBusy() || pending.engagementId !== this.id()) return;
    const owner = this.captureOwner(); this.bankCommandBusy.set(true); this.bankCommandFailed.set(false);
    try {
      const target = pending.action === 'REVIEW' ? `?reconciliationId=${pending.body.review!.reconciliationId}` : '';
      const state = await this.api.get(this.bankCommandPath() + '/command-state' + target, decodeBankReconciliationCommandState);
      if (!owner() || state.reviewBasis !== pending.body.reviewBasis || state.expectedGeneration !== pending.body.expectedGeneration)
        throw new Error('The source basis changed; exact retry is no longer available.');
      if (pending.action === 'CREATE') {
        this.bankLedgerScheduleId.set(pending.body.create!.ledgerScheduleId); this.bankStatementScheduleId.set(pending.body.create!.statementScheduleId);
        this.bankAsOfDate.set(pending.body.create!.asOfDate); this.bankItems.set(pending.body.create!.items.map(x => ({ ...x })));
      } else {
        this.bankSelection.set(pending.body.review!.reconciliationId); this.bankReviewDecision.set(pending.body.review!.decision);
        this.bankReviewConclusion.set(pending.body.review!.conclusion);
      }
      const previewRequest = { ...pending.body, expectedRequestHash: null, reviewed: false };
      const suffix = pending.action === 'CREATE' ? '/create/preview' : `/${pending.body.review!.reconciliationId}/review/preview`;
      const result = await this.api.command(this.bankCommandPath() + suffix, previewRequest);
      if (!owner()) return;
      if (!result.ok) throw new Error(result.message);
      const preview = decodeBankReconciliationCommandPreview(result.value, 'bank-reconciliation.retry-preview');
      if (preview.requestId !== pending.body.requestId || preview.action !== pending.action ||
          preview.requestHash !== pending.requestHash || preview.reviewBasis !== pending.body.reviewBasis || !preview.canProceed)
        throw new Error('The exact request no longer produces the saved preview.');
      this.bankCommandBody = previewRequest; this.bankCommandFingerprint = this.bankInputFingerprint(pending.action);
      this.bankCommandPreview.set(preview); this.bankCommandReviewed.set(false);
      this.bankCommandMessage.set('The saved request matches the current basis. Review it and explicitly confirm the exact retry.');
    } catch {
      if (owner()) { this.bankCommandFailed.set(true); this.bankCommandMessage.set('The saved request no longer matches the current basis. Keep it for support; no retry was sent.'); }
    } finally { this.bankCommandBusy.set(false); }
  }
  bankGenerationLabel(state: string): string {
    return state === 'CURRENT_GENERATION' ? 'Matches current input generation' : state === 'STALE_GENERATION'
      ? 'Retained evidence: input generation changed' : 'Current input generation unavailable';
  }
  @HostListener('document:pointerdown')
  @HostListener('document:keydown')
  recordFocusInteraction(): void { this.focusInteraction++; }

  async previewSampling(): Promise<void> {
    const trigger = this.samplingPreviewTrigger()?.nativeElement;
    const document = trigger?.ownerDocument;
    const origin = document?.activeElement;
    const ownsFocus = !!origin && !!trigger?.form?.contains(origin);
    const interaction = this.focusInteraction, current = this.captureOwner();
    await this.sampling.sample();
    const preview = this.sampling.samplingPreview();
    if (!document || !ownsFocus || !preview) return;
    afterNextRender(() => {
      if (!current() || interaction !== this.focusInteraction || this.toolTab() !== this.samplingToolTabIndex ||
          this.sampling.samplingPreview() !== preview || !this.sampling.previewMatchesInputs() ||
          (document.activeElement !== origin && document.activeElement !== document.body)) return;
      this.samplingPreviewHeading()?.nativeElement.focus();
    }, { injector: this.renderInjector });
  }

  async previewProcedure(action: 'SUBMIT_RESULT' | 'REVIEW_RESULT'): Promise<void> {
    const panel = this.procedurePanel()?.nativeElement;
    const document = panel?.ownerDocument, origin = document?.activeElement;
    const ownsFocus = !!origin && !!panel?.contains(origin);
    const interaction = this.focusInteraction, current = this.captureOwner();
    await this.procedure.prepareProcedureCommand(action);
    const preview = this.procedure.procedureCommandPreview();
    if (!document || !ownsFocus || !preview) return;
    afterNextRender(() => {
      if (!current() || interaction !== this.focusInteraction ||
          this.procedure.procedureCommandPreview() !== preview ||
          (document.activeElement !== origin && document.activeElement !== document.body)) return;
      this.procedurePreviewHeading()?.nativeElement.focus();
    }, { injector: this.renderInjector });
  }

  private captureOwner(): () => boolean {
    const engagement = this.id(), actor = this.riskLinkSession.current(), epoch = this.riskLinkSession.invalidation(), generation = this.ownerGeneration;
    return () => !this.destroyed && generation === this.ownerGeneration && engagement === this.id() &&
      actor === this.riskLinkSession.current() && epoch === this.riskLinkSession.invalidation();
  }
  private refreshPriorScopeNotice(): void {
    this.previousScopeOutcomeUncertain.set(this.priorScopeCommandUnknown || this.pendingPriorScopeCommands.size > 0);
  }

  private commandBlocked(recovery?: FieldworkCommandRecovery): boolean {
    return this.cmd.uncertain() || this.riskLink.busy() || this.riskLink.uncertain() ||
      this.procedure.hasUnresolvedCommand(recovery) || this.sourceIntake.hasUnresolvedCommand(recovery) ||
      this.sampling.samplingBusy() || this.sampling.sampleActionBusy() || this.bankCommandBusy() || !!this.bankCommandPending();
  }
  private otherFieldworkCommandBlocked(): boolean {
    return this.cmd.busy() || this.cmd.uncertain() || this.procedure.hasUnresolvedCommand() || this.sourceIntake.hasUnresolvedCommand() ||
      this.sampling.samplingBusy() || this.sampling.sampleActionBusy() || this.bankCommandBusy() || !!this.bankCommandPending();
  }
  fieldworkCommandBlocked(recovery?: FieldworkCommandRecovery): boolean {
    return this.cmd.busy() || this.commandBlocked(recovery);
  }
  private acquireFieldworkCommand: FieldworkCommandAcquire = (recovery) => {
    if (this.fieldworkCommandBlocked(recovery)) return null;
    const owner = this.captureOwner(), operation = ++this.commandGeneration;
    this.cmd.busy.set(true);
    return () => {
      if (operation === this.commandGeneration && owner()) this.cmd.busy.set(false);
    };
  };

  count(w: Fieldwork, status: string): number { return w.procedures.filter((p) => p.applicabilityStatus === status).length; }
  reviewed(w: Fieldwork): number { return w.procedures.filter((p) => p.evidenceState === 'REVIEWED_CURRENT').length; }
  riskSelection(p: Fieldwork['procedures'][number]): string { return this.riskLinkSelections.get(p.id) ?? p.riskId ?? ''; }
  setRiskSelection(procedureId: string, riskId: string): void { this.riskLink.inputChanged(); this.riskLinkSelections.set(procedureId, riskId); }
  riskLabel(w: Fieldwork, riskId: string | null): string {
    if (!riskId) return 'No risk linked';
    const risk = w.risks.find((x) => x.id === riskId);
    return risk ? `${risk.area} · ${risk.effectiveBand ?? risk.band ?? 'Not assessed'}${risk.significanceDecision === 'SIGNIFICANT' ? ' · Significant' : ''}` : 'Linked risk is outside this list';
  }
  riskFor(w: Fieldwork, riskId: string | null) { return riskId ? w.risks.find((x) => x.id === riskId) : undefined; }
  async saveRiskLink(procedureId: string): Promise<void> {
    const w = this.ws.data(), p = w?.procedures.find(p => p.id === procedureId);
    if (!w?.canManageFieldwork || !p || this.cmd.busy() || this.cmd.uncertain()) return;
    const riskId = this.riskSelection(p);
    if (riskId === (p.riskId ?? '') || (riskId && !w.risks.some(r => r.id === riskId))) return;
    await this.riskLink.prepare(() => ({ action: 'LINK_PROCEDURE_RISK', linkProcedureRisk: { procedureId,
      riskId: this.riskSelection(p) || null, expectedRiskId: p.riskId } }));
  }
  applicable(w: Fieldwork) { return w.procedures.filter((p) => p.applicabilityStatus === 'APPLICABLE'); }
  reviewable(w: Fieldwork) { return w.canManageFieldwork ? this.applicable(w) : w.procedures; }
  sections(w: Fieldwork) {
    const map = new Map<number, { key: number; title: string; count: number }>();
    for (const p of w.procedures) {
      const key = p.sourceSectionNumber ?? -2;
      const g = map.get(key) ?? { key, title: p.sourceSectionTitle ?? '', count: 0 };
      g.count++; map.set(key, g);
    }
    return [...map.values()].sort((a, b) => a.key - b.key);
  }
  firstSection(w: Fieldwork): number { return w.procedures.length ? (w.procedures[0].sourceSectionNumber ?? -2) : 0; }
  visible(w: Fieldwork) {
    const s = this.section() ?? this.firstSection(w);
    return s === 0 ? w.procedures : w.procedures.filter((p) => (p.sourceSectionNumber ?? -2) === s);
  }
  send(url: string, body: unknown, ok: string, after?: () => void): Promise<boolean> {
    const release = this.acquireFieldworkCommand();
    if (!release) return Promise.resolve(false);
    const owner = this.captureOwner(), operation = this.commandGeneration;
    this.cmd.message.set(''); this.cmd.failed.set(false); this.cmd.code.set('');
    return this.api.command(url, body).then(result => {
      if (!owner() || operation !== this.commandGeneration) {
        if (!result.ok && result.unknown) {
          this.priorScopeCommandUnknown = true;
          this.cmd.uncertain.set(true);
        }
        return false;
      }
      this.cmd.failed.set(!result.ok); this.cmd.code.set(result.ok ? '' : result.code);
      if (!result.ok && result.unknown) this.cmd.uncertain.set(true);
      this.cmd.message.set(result.ok ? ok : result.message);
      if (result.ok) after?.();
      return result.ok;
    }).finally(() => {
      if (operation !== this.commandGeneration) {
        this.pendingPriorScopeCommands.delete(operation);
        this.refreshPriorScopeNotice();
        return;
      }
      release();
      if (owner()) this.ws.reload();
    });
  }
  aggregate(w: Fieldwork): void { void this.send(`/api/ui/engagements/${this.id()}/fieldwork/aggregate`, { conclusion: this.aggregateConclusion }, 'Aggregate conclusion recorded.'); }

}
