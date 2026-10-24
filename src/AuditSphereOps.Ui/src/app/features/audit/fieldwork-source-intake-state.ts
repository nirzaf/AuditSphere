import { computed, signal } from '@angular/core';
import { Api, type Resource } from '../../core/api';
import { DecodeError } from '../../core/decode';
import { SessionService } from '../../core/session';
import { pendingRequestReference, TabDrafts, type PendingRequestReference } from '../../core/tab-drafts';
import type { FieldworkCommandAcquire, FieldworkCommandRecovery } from './fieldwork-command-context';
import {
  decodeScheduleCapturePreview, decodeScheduleControlAccountPage, decodeScheduleReviewCandidates,
  decodeScheduleReviewSourceRows, decodeScheduleSourceHeaders, decodeScheduleSourceOptions, decodeScheduleSourceReceiptLookup,
  decodeScheduleSourceReceiptView, decodeScheduleSourceReviewPreview, type ScheduleCapturePreview,
  type ScheduleControlAccountPage, type ScheduleReviewCandidate, type ScheduleSourceHeaders,
  type ScheduleReviewSourceRowsPage, type ScheduleSourceOptions, type ScheduleSourceReceiptLookup,
  type ScheduleSourceReviewPreview,
} from './fieldwork-contracts';

type ScheduleSourceReceiptView = ReturnType<typeof decodeScheduleSourceReceiptView>;

type CaptureFields = {
  pbcAcceptanceReceiptId: string; acceptedSourceDecisionId: string; sourceDate: string;
  columns: { accountCode: string; description: string; signedAmount: string; currency: string;
    stableRowId: string | null; transactionDate: string | null; postingDate: string | null;
    deliveryDate: string | null; serviceDate: string | null };
  selectedControlAccountCodes: string[];
};
type CapturePending = { kind: 'CAPTURE'; requestId: string; requestHash: string; fields: CaptureFields;
  expectedManifestHash: string; expectedInputGeneration: string; expectedPeriodRevision: string };
type ReviewFields = { action: 'APPROVE' | 'RETURN'; reason: string; evidenceReference: string };
type ReviewPending = { kind: 'REVIEW'; requestId: string; requestHash: string; scheduleId: string;
  expectedCaptureHash: string; expectedInputGeneration: string; expectedReviewBasis: string; fields: ReviewFields };
type PendingIntent = CapturePending | ReviewPending;
type Pending = { kind: PendingIntent['kind']; reference: PendingRequestReference; intent: PendingIntent | null; absent: boolean };

const hashPattern = /^[0-9a-f]{64}$/;
const guidPattern = /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const positiveLongPattern = /^[1-9][0-9]{0,18}$/;

function isText(value: unknown, max = 2000): value is string { return typeof value === 'string' && value.length <= max; }
function isGuid(value: unknown): value is string { return typeof value === 'string' && guidPattern.test(value); }
function isHash(value: unknown): value is string { return typeof value === 'string' && hashPattern.test(value); }
function isPositiveLong(value: unknown): value is string {
  return typeof value === 'string' && positiveLongPattern.test(value) && BigInt(value) <= 9223372036854775807n;
}
function isIsoDate(value: string): boolean {
  const parsed = Date.parse(`${value}T00:00:00Z`);
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(parsed) &&
    new Date(parsed).toISOString().slice(0, 10) === value;
}
function validCaptureFields(value: unknown): value is CaptureFields {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const f = value as Record<string, unknown>, c = f['columns'];
  if (!isGuid(f['pbcAcceptanceReceiptId']) || !isGuid(f['acceptedSourceDecisionId']) ||
      typeof f['sourceDate'] !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(f['sourceDate']) ||
      !Array.isArray(f['selectedControlAccountCodes']) || f['selectedControlAccountCodes'].length < 1 ||
      f['selectedControlAccountCodes'].length > 20000 ||
      f['selectedControlAccountCodes'].some(x => !isText(x, 100) || !x.trim() || x.trim() !== x) ||
      new Set(f['selectedControlAccountCodes']).size !== f['selectedControlAccountCodes'].length ||
      !c || typeof c !== 'object' || Array.isArray(c)) return false;
  const columns = c as Record<string, unknown>;
  return ['accountCode', 'description', 'signedAmount', 'currency'].every(k => isText(columns[k], 256) && !!columns[k]) &&
    ['stableRowId', 'transactionDate', 'postingDate', 'deliveryDate', 'serviceDate'].every(k => columns[k] === null || isText(columns[k], 256));
}
function decodePendingIntent(raw: unknown): PendingIntent | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const p = raw as Record<string, unknown>;
  if (!isGuid(p['requestId']) || !isHash(p['requestHash'])) return null;
  if (p['kind'] === 'CAPTURE') {
    if (!validCaptureFields(p['fields']) || !isHash(p['expectedManifestHash']) ||
        !isPositiveLong(p['expectedInputGeneration']) || !isPositiveLong(p['expectedPeriodRevision'])) return null;
    return p as CapturePending;
  }
  if (p['kind'] === 'REVIEW') {
    const f = p['fields'] as Record<string, unknown> | null;
    if (!isGuid(p['scheduleId']) || !isHash(p['expectedCaptureHash']) || !isPositiveLong(p['expectedInputGeneration']) ||
        !isHash(p['expectedReviewBasis']) || !f || !['APPROVE', 'RETURN'].includes(String(f['action'])) ||
        !isText(f['reason']) || !f['reason'].trim() || !isText(f['evidenceReference'])) return null;
    return p as ReviewPending;
  }
  return null;
}

const blankColumns = () => ({ accountCode: '', description: '', signedAmount: '', currency: '',
  stableRowId: '', transactionDate: '', postingDate: '', deliveryDate: '', serviceDate: '' });

/** Typed, receipt-first API/Angular integration for accepted listing schedules. */
export class FieldworkSourceIntakeState {
  readonly intakeOffset = signal({ pbc: 0, trialBalance: 0 });
  readonly controlOffset = signal(0);
  readonly candidateOffset = signal(0);
  readonly reviewRowsOffset = signal(0);
  readonly selectedPbc = signal<ScheduleSourceOptions['pbcSources'][number] | null>(null);
  readonly selectedTrialBalance = signal<ScheduleSourceOptions['trialBalances'][number] | null>(null);
  readonly sourceDate = signal('');
  readonly selectedAccounts = signal<string[]>([]);
  readonly candidate = signal<ScheduleReviewCandidate | null>(null);
  readonly capturePreview = signal<ScheduleCapturePreview | null>(null);
  readonly reviewPreview = signal<ScheduleSourceReviewPreview | null>(null);
  readonly receipt = signal<ScheduleSourceReceiptView | null>(null);
  readonly pending = signal<Pending[]>([]);
  readonly storageProblem = signal(false);
  readonly busy = signal(false);
  readonly recoveryBusy = signal(false);
  readonly reviewedCapture = signal(false);
  readonly reviewedDecision = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly unresolved = computed(() => this.storageProblem() || this.pending().length > 0);

  readonly capture = {
    pbcReceiptId: '', tbDecisionId: '', sourceDate: '', columns: blankColumns(),
  };
  readonly review = { action: 'APPROVE' as 'APPROVE' | 'RETURN', reason: '', evidenceReference: '' };
  private captureRequestId: string = crypto.randomUUID();
  private reviewRequestId: string = crypto.randomUUID();
  private captureFingerprint = '';
  private reviewFingerprint = '';
  private operation = 0;
  private recoveryOperation = 0;
  private restoredOwner = '';

  readonly intakeOptions: Resource<ScheduleSourceOptions>;
  readonly sourceHeaders: Resource<ScheduleSourceHeaders>;
  readonly controlAccounts: Resource<ScheduleControlAccountPage>;
  readonly reviewCandidates: Resource<ReturnType<typeof decodeScheduleReviewCandidates>>;
  readonly reviewSourceRows: Resource<ScheduleReviewSourceRowsPage>;

  constructor(private readonly context: {
    id: () => string | null; api: Api; session: SessionService; drafts: TabDrafts;
    canManage: () => boolean; canReview: () => boolean; acquireCommand: FieldworkCommandAcquire;
    captureOwner: () => () => boolean; reload: () => void;
  }) {
    this.intakeOptions = context.api.resource(() => {
      const id = context.id(), page = this.intakeOffset();
      return id && context.canManage() ? `${this.base(id)}/options?pbcOffset=${page.pbc}&trialBalanceOffset=${page.trialBalance}&limit=25` : null;
    }, (raw, path) => {
      const value = decodeScheduleSourceOptions(raw, path);
      if (value.pbcSources.length > 25 || value.trialBalances.length > 25 ||
          new Set(value.pbcSources.map(x => x.receiptId)).size !== value.pbcSources.length ||
          new Set(value.trialBalances.map(x => x.decisionId)).size !== value.trialBalances.length)
        throw new DecodeError(`${path}: invalid source option page`);
      return value;
    }, 'Accepted listing and TB source options are unavailable in this authorized engagement.');
    this.sourceHeaders = context.api.resource(() => {
      const id = context.id(), source = this.selectedPbc();
      return id && context.canManage() && source ? `${this.base(id)}/pbc/${source.receiptId}/headers` : null;
    }, (raw, path) => {
      const value = decodeScheduleSourceHeaders(raw, path);
      if (value.receiptId !== this.selectedPbc()?.receiptId || value.profileVersion !== 'audit-client-listing-csv.v1' || value.headers.length < 1 ||
          new Set(value.headers).size !== value.headers.length)
        throw new DecodeError(`${path}: headers do not match the selected accepted listing`);
      return value;
    }, 'Verified headers are unavailable for the selected accepted listing.');
    this.controlAccounts = context.api.resource(() => {
      const id = context.id(), source = this.selectedTrialBalance(), date = this.sourceDate();
      return id && context.canManage() && source && /^\d{4}-\d{2}-\d{2}$/.test(date)
        ? `${this.base(id)}/trial-balances/${source.decisionId}/accounts?sourceDate=${encodeURIComponent(date)}&offset=${this.controlOffset()}&limit=100` : null;
    }, (raw, path) => {
      const value = decodeScheduleControlAccountPage(raw, path);
      if (value.offset !== this.controlOffset() || value.limit !== 100)
        throw new DecodeError(`${path}: control-account page does not match the requested range`);
      return value;
    }, 'Selected TB control accounts are unavailable in the current source context.');
    this.reviewCandidates = context.api.resource(() => {
      const id = context.id();
      return id && context.canReview() ? `${this.base(id)}/review-candidates?offset=${this.candidateOffset()}&limit=25` : null;
    }, (raw, path) => {
      const page = decodeScheduleReviewCandidates(raw, path);
      if (page.offset !== this.candidateOffset() || page.limit !== 25)
        throw new DecodeError(`${path}: review candidates do not match the requested page`);
      return page;
    }, 'Captured source schedules are unavailable in your current review scope.');
    this.reviewSourceRows = context.api.resource(() => {
      const id = context.id(), preview = this.reviewPreview();
      return id && context.canReview() && preview
        ? `${this.base(id)}/schedules/${preview.scheduleId}/review/source-rows?captureHash=${encodeURIComponent(preview.captureHash)}&offset=${this.reviewRowsOffset()}&limit=50` : null;
    }, (raw, path) => {
      const page = decodeScheduleReviewSourceRows(raw, path), preview = this.reviewPreview();
      if (!preview || page.captureHash !== preview.captureHash || page.offset !== this.reviewRowsOffset() ||
          page.limit !== 50 || page.totalRows !== preview.source.rowCount)
        throw new DecodeError(`${path}: retained rows do not match the exact reviewed capture`);
      return page;
    }, 'Retained source rows are unavailable for this reviewer and capture.');
    context.session.current();
    context.session.invalidation();
  }

  private base(id = this.context.id()): string { return `/api/ui/engagements/${id}/fieldwork/schedule-intake`; }
  private scope(kind: PendingIntent['kind'], part: 'intent' | 'reference' = 'intent') {
    return { entity: `audit-schedule-source-${kind.toLowerCase()}/${this.context.id()}/${part}`, baseRevision: '0'.repeat(64) };
  }
  private owner(): () => boolean { return this.context.captureOwner(); }
  private allowed(kind: PendingIntent['kind']): boolean {
    return !!this.context.id() && (kind === 'CAPTURE' ? this.context.canManage() : this.context.canReview()) &&
      !!this.context.session.current()?.staff;
  }
  private fail(message: string): void { this.failed.set(true); this.message.set(message); }
  clearMessage(): void { this.failed.set(false); this.message.set(''); }
  hasUnresolvedCommand(recovery?: FieldworkCommandRecovery): boolean {
    if (recovery === 'schedule-source-recovery' && !this.storageProblem() && this.pending().length === 1 && this.pending()[0].intent)
      return false;
    return this.unresolved();
  }
  canUseCapture(): boolean { return this.allowed('CAPTURE') && !this.busy() && !this.unresolved(); }
  canUseReview(): boolean { return this.allowed('REVIEW') && !this.busy() && !this.unresolved(); }
  setCaptureAssent(reviewed: boolean): void {
    this.reviewedCapture.set(reviewed && this.capturePreviewMatches() && this.allowed('CAPTURE') && !this.busy());
  }
  setReviewAssent(reviewed: boolean): void {
    this.reviewedDecision.set(reviewed && this.reviewPreviewMatches() && this.reviewEvidenceReady() &&
      this.allowed('REVIEW') && !this.busy());
  }

  workspaceChanged(): void {
    if (this.context.id() && (this.context.canManage() || this.context.canReview())) this.restorePending();
  }
  ownerChanged(): void {
    ++this.operation; ++this.recoveryOperation; this.busy.set(false); this.recoveryBusy.set(false);
    this.capturePreview.set(null); this.reviewPreview.set(null); this.reviewRowsOffset.set(0); this.receipt.set(null);
    this.reviewedCapture.set(false); this.reviewedDecision.set(false); this.captureFingerprint = ''; this.reviewFingerprint = '';
    this.selectedPbc.set(null); this.selectedTrialBalance.set(null); this.selectedAccounts.set([]);
    this.capture.pbcReceiptId = ''; this.capture.tbDecisionId = ''; this.capture.sourceDate = ''; this.sourceDate.set(''); this.capture.columns = blankColumns();
    this.review.action = 'APPROVE'; this.review.reason = ''; this.review.evidenceReference = ''; this.candidate.set(null);
    this.pending.set([]); this.storageProblem.set(false); this.restoredOwner = '';
    this.restorePending();
  }
  workspaceUnavailable(): void {
    if (!this.context.id()) return;
    ++this.operation; ++this.recoveryOperation; this.busy.set(false); this.recoveryBusy.set(false); this.capturePreview.set(null); this.reviewPreview.set(null);
    this.reviewedCapture.set(false); this.reviewedDecision.set(false); this.captureFingerprint = ''; this.reviewFingerprint = '';
  }
  private restorePending(): void {
    const actor = this.context.session.current();
    const owner = `${this.context.id() ?? ''}/${actor?.firmId ?? ''}/${actor?.userId ?? ''}/${actor?.generation ?? ''}/${this.context.session.invalidation()}`;
    if (owner === this.restoredOwner || !this.context.id() || !actor?.staff) return;
    this.restoredOwner = owner;
    const pending: Pending[] = [];
    let storageProblem = false;
    for (const kind of ['CAPTURE', 'REVIEW'] as const) {
      const intentRead = this.context.drafts.readPendingSubmission(this.scope(kind), decodePendingIntent);
      const referenceRead = this.context.drafts.readPendingRequest(this.scope(kind, 'reference'));
      const intent = intentRead.state === 'ready' && intentRead.draft.value.kind === kind ? intentRead.draft.value : null;
      const reference = referenceRead.state === 'ready' ? referenceRead.draft.value : null;
      if (intent && reference && (intent.requestId !== reference.requestId || intent.requestHash !== reference.requestHash)) {
        storageProblem = true;
        continue;
      }
      if (intent) {
        const exactReference = reference ?? { requestId: intent.requestId, requestHash: intent.requestHash };
        pending.push({ kind, reference: exactReference, intent, absent: false });
        if (!reference && !this.context.drafts.save(this.scope(kind, 'reference'), exactReference, pendingRequestReference, true))
          storageProblem = true;
      } else if (reference) pending.push({ kind, reference, intent: null, absent: false });
      else if (intentRead.state === 'unavailable' || intentRead.state === 'stale' ||
          referenceRead.state === 'unavailable' || referenceRead.state === 'stale') storageProblem = true;
    }
    this.pending.set(pending); this.storageProblem.set(storageProblem);
    if (pending.length) {
      this.message.set('A prior source-capture or review request needs exact receipt recovery. Do not start another source command.');
      for (const item of pending) void this.recover(item);
    }
  }

  choosePbc(receiptId: string): void {
    const source = this.intakeOptions.data()?.pbcSources.find(x => x.receiptId === receiptId) ?? null;
    if (!source || !this.canUseCapture()) return;
    this.selectedPbc.set(source); this.capture.pbcReceiptId = source.receiptId;
    this.captureChanged();
  }
  chooseTrialBalance(decisionId: string): void {
    const source = this.intakeOptions.data()?.trialBalances.find(x => x.decisionId === decisionId) ?? null;
    if (!source || !this.canUseCapture()) return;
    this.selectedTrialBalance.set(source); this.capture.tbDecisionId = source.decisionId;
    this.capture.sourceDate = source.periodEnd; this.sourceDate.set(source.periodEnd);
    this.selectedAccounts.set([]); this.controlOffset.set(0); this.captureChanged();
  }
  setSourceDate(value: string): void {
    this.capture.sourceDate = value; this.sourceDate.set(value); this.selectedAccounts.set([]); this.controlOffset.set(0);
    this.captureChanged();
  }
  setColumn(key: keyof ReturnType<typeof blankColumns>, value: string): void {
    this.capture.columns[key] = value; this.captureChanged();
  }
  private captureChanged(): void {
    if (this.unresolved()) return;
    this.capturePreview.set(null); this.reviewedCapture.set(false); this.captureFingerprint = '';
    this.captureRequestId = crypto.randomUUID(); this.clearMessage();
  }
  setControlAccount(code: string, checked: boolean): void {
    if (!this.canUseCapture() || !this.controlAccounts.data()?.items.some(x => x.accountCode === code)) return;
    const selected = new Set(this.selectedAccounts());
    checked ? selected.add(code) : selected.delete(code);
    this.selectedAccounts.set([...selected].sort()); this.captureChanged();
  }
  accountSelected(code: string): boolean { return this.selectedAccounts().includes(code); }
  nextIntakePage(kind: 'pbc' | 'trialBalance', next: boolean): void {
    const data = this.intakeOptions.data();
    if (!data || this.intakeOptions.loading()) return;
    const key = kind === 'pbc' ? 'nextPbcOffset' : 'nextTrialBalanceOffset';
    if (next && !(kind === 'pbc' ? data.hasMorePbcSources : data.hasMoreTrialBalances)) return;
    this.intakeOffset.update(page => ({ ...page, [kind]: next ? data[key] : Math.max(0, page[kind] - 25) }));
  }
  nextControlPage(next: boolean): void {
    const page = this.controlAccounts.data();
    if (!page || this.controlAccounts.loading()) return;
    const offset = next ? page.offset + page.limit : Math.max(0, page.offset - page.limit);
    if (offset === page.offset || next && !page.hasMore) return;
    this.controlOffset.set(offset);
  }
  candidatePage(next: boolean): void {
    const page = this.reviewCandidates.data();
    if (!page || this.reviewCandidates.loading()) return;
    const offset = next ? page.offset + page.limit : Math.max(0, page.offset - page.limit);
    if (offset === page.offset || next && !page.hasMore) return;
    this.candidateOffset.set(offset);
  }
  reviewRowsPage(next: boolean): void {
    const page = this.reviewSourceRows.data();
    if (!page || this.reviewSourceRows.loading()) return;
    const offset = next ? page.offset + page.limit : Math.max(0, page.offset - page.limit);
    if (offset === page.offset || next && !page.hasMore) return;
    this.reviewedDecision.set(false);
    this.reviewRowsOffset.set(offset);
  }
  chooseCandidate(scheduleId: string): void {
    const candidate = this.reviewCandidates.data()?.items.find(x => x.scheduleId === scheduleId) ?? null;
    if (!candidate || !this.canUseReview()) return;
    this.candidate.set(candidate); this.review.action = 'APPROVE'; this.review.reason = ''; this.review.evidenceReference = '';
    this.reviewPreview.set(null); this.reviewRowsOffset.set(0); this.reviewedDecision.set(false); this.reviewRequestId = crypto.randomUUID(); this.reviewFingerprint = ''; this.clearMessage();
  }
  reviewChanged(): void {
    if (this.unresolved()) return;
    this.reviewPreview.set(null); this.reviewRowsOffset.set(0); this.reviewedDecision.set(false); this.reviewFingerprint = '';
    this.reviewRequestId = crypto.randomUUID(); this.clearMessage();
  }
  private currentCaptureFields(): CaptureFields | null {
    const pbc = this.selectedPbc(), tb = this.selectedTrialBalance(), headers = this.sourceHeaders.data();
    const fields: CaptureFields = { pbcAcceptanceReceiptId: this.capture.pbcReceiptId, acceptedSourceDecisionId: this.capture.tbDecisionId,
      sourceDate: this.capture.sourceDate, columns: { accountCode: this.capture.columns.accountCode, description: this.capture.columns.description,
        signedAmount: this.capture.columns.signedAmount, currency: this.capture.columns.currency,
        stableRowId: this.capture.columns.stableRowId || null, transactionDate: this.capture.columns.transactionDate || null,
        postingDate: this.capture.columns.postingDate || null, deliveryDate: this.capture.columns.deliveryDate || null,
        serviceDate: this.capture.columns.serviceDate || null }, selectedControlAccountCodes: [...this.selectedAccounts()].sort() };
    const date = fields.sourceDate;
    const requiredColumns = [fields.columns.accountCode, fields.columns.description, fields.columns.signedAmount, fields.columns.currency];
    const mapped = Object.values(fields.columns).filter((x): x is string => !!x);
    if (!pbc || !tb || pbc.receiptId !== fields.pbcAcceptanceReceiptId || tb.decisionId !== fields.acceptedSourceDecisionId ||
        pbc.periodStart !== tb.periodStart || pbc.periodEnd !== tb.periodEnd || pbc.entityScope !== tb.legalEntityKey ||
        !headers || headers.receiptId !== pbc.receiptId || !requiredColumns.every(x => x && headers.headers.includes(x)) ||
        !isIsoDate(date) || date !== tb.periodEnd ||
        fields.selectedControlAccountCodes.length < 1 ||
        mapped.length !== new Set(mapped).size || mapped.some(x => !headers.headers.includes(x))) return null;
    return fields;
  }
  private captureIntentFingerprint(requestId: string, fields: CaptureFields): string {
    return JSON.stringify({ requestId, fields: { ...fields, selectedControlAccountCodes: [...fields.selectedControlAccountCodes].sort() } });
  }
  async previewCapture(): Promise<void> {
    const fields = this.currentCaptureFields();
    if (!fields || !this.canUseCapture()) { this.fail('Select an accepted PBC listing, accepted TB, in-period source date, explicit CSV column mapping and at least one exact control account.'); return; }
    await this.requestCapturePreview(this.captureRequestId, fields, null);
  }
  private async requestCapturePreview(requestId: string, fields: CaptureFields, expectedHash: string | null): Promise<void> {
    if (this.busy()) return;
    const release = this.context.acquireCommand(expectedHash ? 'schedule-source-recovery' : undefined);
    if (!release) return;
    if (!this.allowed('CAPTURE')) { release(); return; }
    const owner = this.owner(), operation = ++this.operation;
    this.busy.set(true); this.failed.set(false); this.message.set(''); this.capturePreview.set(null); this.reviewedCapture.set(false);
    try {
      const result = await this.context.api.command(this.base() + '/capture/preview', { requestId, fields });
      if (!owner() || operation !== this.operation) return;
      if (!result.ok) { this.fail(result.message); return; }
      const preview = decodeScheduleCapturePreview(result.value, 'schedule capture preview');
      if (preview.requestId !== requestId || this.captureIntentFingerprint(requestId, preview.canonicalInput) !==
          this.captureIntentFingerprint(requestId, fields) || expectedHash && preview.requestHash !== expectedHash)
        throw new Error('capture preview mismatch');
      this.capturePreview.set(preview);
      this.captureFingerprint = this.captureIntentFingerprint(requestId, fields);
      if (expectedHash) this.message.set('The retained capture intent is unchanged. Review this refreshed exact preview and give new explicit assent before retrying.');
    } catch {
      if (owner()) this.fail('The exact schedule capture preview could not be verified. Refresh the sources and review again.');
    } finally {
      if (operation === this.operation) this.busy.set(false);
      release();
    }
  }
  capturePreviewMatches(): boolean {
    const preview = this.capturePreview();
    const retained = this.pending().find(x => x.kind === 'CAPTURE' && x.intent?.kind === 'CAPTURE');
    if (preview && retained?.intent?.kind === 'CAPTURE') {
      const formFields: CaptureFields = { pbcAcceptanceReceiptId: this.capture.pbcReceiptId,
        acceptedSourceDecisionId: this.capture.tbDecisionId, sourceDate: this.capture.sourceDate,
        columns: { accountCode: this.capture.columns.accountCode, description: this.capture.columns.description,
          signedAmount: this.capture.columns.signedAmount, currency: this.capture.columns.currency,
          stableRowId: this.capture.columns.stableRowId || null, transactionDate: this.capture.columns.transactionDate || null,
          postingDate: this.capture.columns.postingDate || null, deliveryDate: this.capture.columns.deliveryDate || null,
          serviceDate: this.capture.columns.serviceDate || null }, selectedControlAccountCodes: [...this.selectedAccounts()].sort() };
      return preview.requestId === retained.intent.requestId && preview.requestHash === retained.intent.requestHash &&
        preview.manifestHash === retained.intent.expectedManifestHash &&
        preview.inputGeneration === retained.intent.expectedInputGeneration &&
        preview.periodRevision === retained.intent.expectedPeriodRevision &&
        this.captureIntentFingerprint(preview.requestId, preview.canonicalInput) === this.captureIntentFingerprint(preview.requestId, retained.intent.fields) &&
        this.captureFingerprint === this.captureIntentFingerprint(preview.requestId, retained.intent.fields) &&
        this.captureIntentFingerprint(preview.requestId, formFields) === this.captureIntentFingerprint(preview.requestId, retained.intent.fields);
    }
    const fields = this.currentCaptureFields();
    return !!preview && !!fields && preview.requestId === this.captureRequestId &&
      this.captureFingerprint === this.captureIntentFingerprint(preview.requestId, fields);
  }
  canRecordCapture(): boolean {
    const preview = this.capturePreview();
    const pending = this.pending().find(x => x.kind === 'CAPTURE');
    const exactRecovery = !!pending?.absent && pending.intent?.kind === 'CAPTURE' &&
      pending.reference.requestId === preview?.requestId && pending.reference.requestHash === preview?.requestHash;
    return !!preview && this.capturePreviewMatches() && this.reviewedCapture() && this.allowed('CAPTURE') &&
      !this.busy() && (!this.unresolved() || exactRecovery);
  }
  async recordCapture(): Promise<void> {
    const preview = this.capturePreview(), fields = this.currentCaptureFields();
    const retained = this.pending().find(x => x.kind === 'CAPTURE');
    const exactRecovery = !!retained?.absent && retained.intent?.kind === 'CAPTURE' && retained.reference.requestId === preview?.requestId &&
      retained.reference.requestHash === preview?.requestHash;
    const intentFields = fields ?? (exactRecovery && retained?.intent?.kind === 'CAPTURE' ? retained.intent.fields : null);
    if (!preview || !intentFields || !this.canRecordCapture() || this.unresolved() && !exactRecovery) return;
    const pendingIntent: CapturePending = { kind: 'CAPTURE', requestId: preview.requestId, requestHash: preview.requestHash, fields: intentFields,
      expectedManifestHash: preview.manifestHash, expectedInputGeneration: preview.inputGeneration, expectedPeriodRevision: preview.periodRevision };
    await this.execute(pendingIntent);
  }

  async previewReview(): Promise<void> {
    const candidate = this.candidate(), fields: ReviewFields = { action: this.review.action,
      reason: this.review.reason.trim(), evidenceReference: this.review.evidenceReference.trim() };
    if (!candidate || !this.canUseReview() || !fields.reason || fields.reason.length > 2000 || fields.evidenceReference.length > 2000) {
      this.fail('Choose an unreviewed captured schedule and enter a bounded independent-review reason.'); return;
    }
    const requestId = this.reviewRequestId;
    if (this.busy()) return;
    const release = this.context.acquireCommand();
    if (!release) return;
    const owner = this.owner(), operation = ++this.operation;
    this.busy.set(true); this.failed.set(false); this.message.set(''); this.reviewPreview.set(null); this.reviewRowsOffset.set(0); this.reviewedDecision.set(false);
    try {
      const result = await this.context.api.command(this.base() + `/schedules/${candidate.scheduleId}/review/preview`, { requestId, fields });
      if (!owner() || operation !== this.operation) return;
      if (!result.ok) { this.fail(result.message); return; }
      const preview = decodeScheduleSourceReviewPreview(result.value, 'schedule source review preview');
      if (preview.requestId !== requestId || preview.scheduleId !== candidate.scheduleId ||
          preview.canonicalInput.action !== fields.action || preview.canonicalInput.reason !== fields.reason ||
          preview.canonicalInput.evidenceReference !== fields.evidenceReference)
        throw new Error('review preview mismatch');
      this.reviewPreview.set(preview); this.reviewFingerprint = JSON.stringify({ requestId, scheduleId: candidate.scheduleId, fields });
    } catch { if (owner()) this.fail('The exact independent-review preview could not be verified. Refresh the candidate and review again.'); }
    finally { if (operation === this.operation) this.busy.set(false); release(); }
  }
  reviewPreviewMatches(): boolean {
    const preview = this.reviewPreview(), candidate = this.candidate();
    const retained = this.pending().find(x => x.kind === 'REVIEW' && x.intent?.kind === 'REVIEW');
    if (preview && retained?.intent?.kind === 'REVIEW') {
      const fields: ReviewFields = { action: this.review.action, reason: this.review.reason.trim(), evidenceReference: this.review.evidenceReference.trim() };
      return preview.requestId === retained.intent.requestId && preview.requestHash === retained.intent.requestHash &&
        preview.scheduleId === retained.intent.scheduleId &&
        preview.canonicalInput.action === retained.intent.fields.action &&
        preview.canonicalInput.reason === retained.intent.fields.reason &&
        preview.canonicalInput.evidenceReference === retained.intent.fields.evidenceReference &&
        JSON.stringify(fields) === JSON.stringify(retained.intent.fields);
    }
    const fields: ReviewFields = { action: this.review.action, reason: this.review.reason.trim(), evidenceReference: this.review.evidenceReference.trim() };
    return !!preview && !!candidate && preview.scheduleId === candidate.scheduleId &&
      preview.requestId === this.reviewRequestId &&
      this.reviewFingerprint === JSON.stringify({ requestId: preview.requestId, scheduleId: candidate.scheduleId, fields });
  }
  reviewEvidenceReady(): boolean {
    const preview = this.reviewPreview(), page = this.reviewSourceRows.data();
    return !!preview && !!page && !this.reviewSourceRows.loading() && !this.reviewSourceRows.error() &&
      page.captureHash === preview.captureHash && page.offset === this.reviewRowsOffset() &&
      page.limit === 50 && page.totalRows === preview.source.rowCount;
  }
  canRecordReview(): boolean {
    const preview = this.reviewPreview(), pending = this.pending().find(x => x.kind === 'REVIEW');
    const exactRecovery = !!pending?.absent && pending.intent?.kind === 'REVIEW' &&
      pending.reference.requestId === preview?.requestId && pending.reference.requestHash === preview?.requestHash &&
      pending.intent.scheduleId === preview?.scheduleId;
    return !!preview && (this.candidate() !== null || exactRecovery) && this.reviewPreviewMatches() && this.reviewEvidenceReady() &&
      this.reviewedDecision() && this.allowed('REVIEW') && !this.busy() &&
      (!this.unresolved() || exactRecovery) && (preview.canonicalInput.action !== 'APPROVE' || preview.canApprove);
  }
  async recordReview(): Promise<void> {
    const preview = this.reviewPreview(), candidate = this.candidate();
    const retained = this.pending().find(x => x.kind === 'REVIEW');
    const exactRecovery = !!retained?.absent && retained.intent?.kind === 'REVIEW' && retained.reference.requestId === preview?.requestId &&
      retained.reference.requestHash === preview?.requestHash && retained.intent.scheduleId === preview?.scheduleId;
    if (!preview || (!candidate && !exactRecovery) || !this.canRecordReview() || this.unresolved() && !exactRecovery) return;
    const pendingIntent: ReviewPending = { kind: 'REVIEW', requestId: preview.requestId, requestHash: preview.requestHash,
      scheduleId: preview.scheduleId, expectedCaptureHash: preview.captureHash, expectedInputGeneration: preview.inputGeneration,
      expectedReviewBasis: preview.reviewBasis, fields: preview.canonicalInput };
    await this.execute(pendingIntent);
  }

  private async execute(intent: PendingIntent): Promise<void> {
    const prior = this.pending().find(x => x.kind === intent.kind);
    const recovery = !!prior?.absent && prior.intent?.requestId === intent.requestId && prior.intent.requestHash === intent.requestHash;
    if (this.busy()) return;
    const release = this.context.acquireCommand(recovery ? 'schedule-source-recovery' : undefined);
    if (!release || this.unresolved() && !recovery) { release?.(); return; }
    const scope = this.scope(intent.kind);
    const reference: PendingRequestReference = { requestId: intent.requestId, requestHash: intent.requestHash };
    const savedIntent = this.context.drafts.save(scope, intent, decodePendingIntent, true);
    const savedReference = savedIntent && this.context.drafts.save(this.scope(intent.kind, 'reference'), reference,
      pendingRequestReference, true);
    if (!savedIntent || !savedReference) {
      const clearedIntent = this.context.drafts.clear(scope.entity);
      const clearedReference = this.context.drafts.clear(this.scope(intent.kind, 'reference').entity);
      if (!clearedIntent || !clearedReference) this.storageProblem.set(true);
      this.fail('The exact request reference could not be retained. No schedule command was sent.'); release(); return;
    }
    const owner = this.owner(), operation = ++this.operation;
    this.pending.set([{ kind: intent.kind, reference, intent, absent: false }]);
    this.busy.set(true); this.failed.set(false); this.message.set('');
    this.reviewedCapture.set(false); this.reviewedDecision.set(false);
    try {
      const url = intent.kind === 'CAPTURE' ? this.base() + '/capture/actions' : this.base() + `/schedules/${intent.scheduleId}/review/actions`;
      const body = intent.kind === 'CAPTURE'
        ? { requestId: intent.requestId, fields: intent.fields, expectedManifestHash: intent.expectedManifestHash,
            expectedInputGeneration: intent.expectedInputGeneration, expectedPeriodRevision: intent.expectedPeriodRevision,
            expectedRequestHash: intent.requestHash, reviewed: true }
        : { requestId: intent.requestId, expectedCaptureHash: intent.expectedCaptureHash, expectedInputGeneration: intent.expectedInputGeneration,
            expectedReviewBasis: intent.expectedReviewBasis, fields: intent.fields, expectedRequestHash: intent.requestHash, reviewed: true };
      const result = await this.context.api.command(url, body);
      if (!owner() || operation !== this.operation) return;
      if (!result.ok && !result.unknown) {
        this.clearPending(intent.kind); this.fail(result.message); return;
      }
      if (result.ok) {
        try {
          const receipt = decodeScheduleSourceReceiptView(result.value, 'schedule source receipt');
          if (!this.receiptMatches(receipt, intent)) throw new DecodeError('Receipt does not match the submitted source request.');
          if (await this.accept(intent.kind, receipt)) return;
        } catch { /* Recover only through the actor-owned exact receipt endpoint. */ }
      }
      this.message.set('The command outcome is unresolved. Looking up the exact actor-owned receipt; do not submit a new request.');
      await this.recover({ kind: intent.kind, reference, intent, absent: false });
    } catch {
      if (owner()) {
        this.failed.set(true); this.message.set('The command outcome could not be confirmed. Looking up its exact receipt; do not submit a new request.');
        await this.recover({ kind: intent.kind, reference, intent, absent: false });
      }
    } finally {
      if (operation === this.operation) this.busy.set(false);
      release();
    }
  }
  private receiptMatches(view: ScheduleSourceReceiptView, intent: PendingIntent): boolean {
    const actor = this.context.session.current(), r = view.receipt;
    return !!actor && r.requestId === intent.requestId && r.requestHash === intent.requestHash &&
      r.engagementId === this.context.id() && r.firmId === actor.firmId && r.actorUserId === actor.userId &&
      r.actorEpoch === actor.generation && r.action === (intent.kind === 'CAPTURE' ? 'CAPTURE' : intent.fields.action) &&
      (intent.kind === 'CAPTURE'
        ? r.manifestHash === intent.expectedManifestHash && r.inputGeneration === intent.expectedInputGeneration
        : r.inputGeneration === intent.expectedInputGeneration && r.reviewId !== null && r.scheduleId === intent.scheduleId);
  }
  private async accept(kind: PendingIntent['kind'], receipt: ScheduleSourceReceiptView): Promise<boolean> {
    if (!this.clearPendingMarkers(kind)) {
      this.storageProblem.set(true); this.failed.set(true);
      this.message.set('The original receipt is verified, but its pending marker could not be cleared. Keep this workspace blocked until tab storage is available.');
      return false;
    }
    this.pending.update(items => items.filter(x => x.kind !== kind));
    this.storageProblem.set(false); this.receipt.set(receipt); this.failed.set(false);
    this.message.set(receipt.receipt.action === 'CAPTURE' ? 'The exact listing schedule was captured and retained.' :
      receipt.receipt.action === 'APPROVE' ? 'The independent source review was retained.' : 'The source schedule was returned with the recorded reason.');
    this.capturePreview.set(null); this.reviewPreview.set(null); this.context.reload();
    this.intakeOptions.reload(); this.reviewCandidates.reload();
    return true;
  }
  private clearPending(kind: PendingIntent['kind']): void {
    if (this.clearPendingMarkers(kind)) {
      this.pending.update(items => items.filter(x => x.kind !== kind));
      this.storageProblem.set(false);
    } else this.storageProblem.set(true);
  }
  private clearPendingMarkers(kind: PendingIntent['kind']): boolean {
    const intentCleared = this.context.drafts.clear(this.scope(kind).entity);
    const referenceCleared = this.context.drafts.clear(this.scope(kind, 'reference').entity);
    return intentCleared && referenceCleared;
  }
  async recover(item: Pending): Promise<void> {
    if (this.recoveryBusy() || !this.allowed(item.kind)) return;
    const owner = this.owner(), operation = ++this.recoveryOperation;
    this.recoveryBusy.set(true);
    try {
      const route = item.kind === 'CAPTURE' ? 'capture' : 'review';
      const lookup = await this.context.api.get(this.base() + `/${route}/receipts/${item.reference.requestId}?requestHash=${item.reference.requestHash}`,
        decodeScheduleSourceReceiptLookup);
      if (!owner() || operation !== this.recoveryOperation || !this.allowed(item.kind)) return;
      if (lookup.found !== !!lookup.receipt) throw new DecodeError('Inconsistent exact receipt lookup.');
      if (!lookup.found) {
        this.pending.update(items => items.map(x => x.kind === item.kind ? { ...x, absent: true } : x));
        this.capturePreview.set(null); this.reviewPreview.set(null); this.reviewedCapture.set(false); this.reviewedDecision.set(false);
        this.message.set('No receipt was present in the serialized lookup. The original request may still be in flight; keep its identity and only prepare the exact same intent for renewed assent.');
        this.failed.set(false); return;
      }
      const receipt = lookup.receipt!;
      if (item.intent) {
        if (!this.receiptMatches(receipt, item.intent)) throw new DecodeError('Receipt does not match the exact retained intent.');
      } else {
        // Reference-only recovery cannot reconstruct fields, so validate only scope and actor ownership.
        const actor = this.context.session.current(), r = receipt.receipt;
        if (!actor || r.requestId !== item.reference.requestId || r.requestHash !== item.reference.requestHash ||
            r.engagementId !== this.context.id() || r.firmId !== actor.firmId || r.actorUserId !== actor.userId ||
            r.actorEpoch !== actor.generation || (item.kind === 'CAPTURE' ? r.action !== 'CAPTURE' : !['APPROVE', 'RETURN'].includes(r.action)))
          throw new DecodeError('Receipt ownership mismatch.');
      }
      await this.accept(item.kind, receipt);
    } catch {
      if (owner()) { this.failed.set(true); this.message.set('The exact actor-owned receipt could not be verified. Keep the request identity and do not submit another command.'); }
    } finally { if (operation === this.recoveryOperation) this.recoveryBusy.set(false); }
  }
  async resumePending(kind: PendingIntent['kind']): Promise<void> {
    const pending = this.pending().find(x => x.kind === kind);
    if (!pending?.absent || !pending.intent || this.busy() || !this.allowed(kind)) return;
    if (kind === 'CAPTURE') {
      const intent = pending.intent as CapturePending;
      this.captureRequestId = intent.requestId; this.capture.pbcReceiptId = intent.fields.pbcAcceptanceReceiptId;
      this.capture.tbDecisionId = intent.fields.acceptedSourceDecisionId; this.capture.sourceDate = intent.fields.sourceDate;
      this.sourceDate.set(intent.fields.sourceDate);
      this.capture.columns = { accountCode: intent.fields.columns.accountCode, description: intent.fields.columns.description,
        signedAmount: intent.fields.columns.signedAmount, currency: intent.fields.columns.currency,
        stableRowId: intent.fields.columns.stableRowId ?? '', transactionDate: intent.fields.columns.transactionDate ?? '',
        postingDate: intent.fields.columns.postingDate ?? '', deliveryDate: intent.fields.columns.deliveryDate ?? '',
        serviceDate: intent.fields.columns.serviceDate ?? '' };
      this.selectedAccounts.set([...intent.fields.selectedControlAccountCodes]);
      await this.requestCapturePreview(intent.requestId, intent.fields, intent.requestHash);
    } else {
      const intent = pending.intent as ReviewPending;
      this.reviewRequestId = intent.requestId; this.review.action = intent.fields.action;
      this.review.reason = intent.fields.reason; this.review.evidenceReference = intent.fields.evidenceReference;
      if (this.busy()) return;
      const release = this.context.acquireCommand('schedule-source-recovery');
      if (!release) return;
      const owner = this.owner(), operation = ++this.operation; this.busy.set(true); this.reviewPreview.set(null); this.reviewRowsOffset.set(0); this.reviewedDecision.set(false);
      try {
        const result = await this.context.api.command(this.base() + `/schedules/${intent.scheduleId}/review/preview`,
          { requestId: intent.requestId, fields: intent.fields });
        if (!owner() || operation !== this.operation) return;
        if (!result.ok) { this.fail(result.message); return; }
        const preview = decodeScheduleSourceReviewPreview(result.value, 'schedule source review preview');
        if (preview.requestId !== intent.requestId || preview.scheduleId !== intent.scheduleId || preview.requestHash !== intent.requestHash ||
            preview.canonicalInput.action !== intent.fields.action || preview.canonicalInput.reason !== intent.fields.reason ||
            preview.canonicalInput.evidenceReference !== intent.fields.evidenceReference)
          throw new DecodeError('Retained review request no longer matches the exact preview.');
        this.reviewPreview.set(preview); this.reviewFingerprint = JSON.stringify({ requestId: intent.requestId, scheduleId: intent.scheduleId, fields: intent.fields });
        this.message.set('The retained review intent is unchanged. Review this exact preview and give new explicit assent before retrying.');
      } catch { if (owner()) this.fail('The exact retained review preview could not be verified. Keep the original request pending.'); }
      finally { if (operation === this.operation) this.busy.set(false); release(); }
    }
  }
}
