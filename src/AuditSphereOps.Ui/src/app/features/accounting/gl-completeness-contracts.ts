import {
  arr,
  bool,
  date,
  dec,
  guid,
  instant,
  nat,
  nullable,
  obj,
  oneOf,
  sha256,
  str,
} from '../../core/decode';
const selected = (kind: 'TB' | 'GL') =>
  obj({
    decisionId: guid,
    sourceKind: oneOf(kind),
    trialBalanceDatasetId: nullable(guid),
    importBatchId: nullable(guid),
    sourceIdentityHash: sha256,
    acceptedByUserId: guid,
    acceptedAt: instant,
    inputGeneration: nat,
  });
const source = obj({
  batchId: guid,
  clientId: guid,
  engagementId: guid,
  periodId: guid,
  periodCode: str(100),
  bookId: nullable(guid),
  entity: str(500),
  currency: str(10),
  importState: oneOf('SEALED'),
  rawFileSha256: sha256,
  sourceHash: sha256,
  profileVersion: str(200),
  parserVersion: str(200),
  importedByUserId: guid,
  importedAt: instant,
  selected: nullable(selected('GL')),
});
const context = obj({
  source,
  periodStatus: str(30),
  basis: str(100),
  startDate: date,
  endDate: date,
  priorPeriodId: nullable(guid),
  bookCode: nullable(str(100)),
  inputGeneration: nat,
  selectedTrialBalance: nullable(selected('TB')),
  revision: sha256,
  canPrepare: bool,
  blocker: nullable(str(2000)),
});
const tb = obj({
  id: guid,
  periodId: guid,
  periodCode: str(100),
  bookId: nullable(guid),
  revision: nat,
  sourceHash: sha256,
  importedAt: instant,
});
const sourcePage = obj({ items: arr(tb, 20), totalCount: nat, page: nat, pageSize: nat });
const status = oneOf('RECONCILED', 'UNRECONCILED', 'APPROVED', 'REJECTED');
const summary = obj({
  id: guid,
  trialBalanceDatasetId: guid,
  openingTrialBalanceDatasetId: nullable(guid),
  status,
  incompleteExtract: bool,
  absoluteResidual: dec,
  createdAt: instant,
});
const workspace = obj({
  context,
  closingSources: sourcePage,
  openingSources: sourcePage,
  bridges: arr(summary, 20),
  moreBridges: bool,
});
const operation = obj({
  id: guid,
  status: str(100),
  originatorId: nullable(guid),
  evidenceReference: str(2000),
  openingTrialBalanceDatasetId: nullable(guid),
  resultIdentity: nullable(str(200)),
  createdAt: instant,
});
const plan = obj({
  context,
  closing: tb,
  opening: nullable(tb),
  existingBridgeId: nullable(guid),
  operations: arr(operation, 20),
  revision: sha256,
  canPrepare: bool,
  blocker: nullable(str(2000)),
});
const bridge = obj({
  id: guid,
  firmId: guid,
  clientId: guid,
  engagementId: guid,
  periodId: guid,
  bookId: nullable(guid),
  trialBalanceDatasetId: guid,
  importBatchId: guid,
  openingTrialBalanceDatasetId: nullable(guid),
  trialBalanceHash: sha256,
  generalLedgerHash: sha256,
  openingTrialBalanceHash: str(64),
  accountResidualDigest: sha256,
  openingMovementResidualDigest: sha256,
  trialBalanceAccountCount: nat,
  generalLedgerAccountCount: nat,
  matchedAccountCount: nat,
  mismatchedAccountCount: nat,
  openingMovementMismatchedAccountCount: nat,
  journalExceptionCount: nat,
  openingAmount: dec,
  movementAmount: dec,
  closingAmount: dec,
  openingMovementResidual: dec,
  absoluteResidual: dec,
  coverageStart: date,
  coverageEnd: date,
  status,
  evidenceReference: str(2000),
  incompleteExtract: bool,
  completenessDisclosure: str(2000),
  createdByUserId: guid,
  createdAt: instant,
  reviewedByUserId: nullable(guid),
  reviewedAt: nullable(instant),
});
const residual = obj({
  accountCode: str(100),
  closing: dec,
  movement: dec,
  opening: nullable(dec),
  movementClosingDifference: dec,
  openingMovementDifference: nullable(dec),
});
const residualPage = obj({ items: arr(residual, 50), totalCount: nat, page: nat, pageSize: nat });
const review = obj({
  context,
  bridge,
  residuals: residualPage,
  revision: sha256,
  canApprove: bool,
  canReject: bool,
  approvalBlocker: nullable(str(2000)),
  reviewBlocker: nullable(str(2000)),
});
function checkContext(c: ReturnType<typeof context>) {
  const s = c.source,
    g = s.selected,
    t = c.selectedTrialBalance;
  if (
    c.startDate > c.endDate ||
    (c.canPrepare && (c.blocker !== null || !['ACTIVE', 'DRAFT'].includes(c.periodStatus))) ||
    (!c.canPrepare && !c.blocker) ||
    (g &&
      (g.importBatchId === null ||
        g.trialBalanceDatasetId !== null ||
        g.inputGeneration !== c.inputGeneration ||
        (g.importBatchId === s.batchId && g.sourceIdentityHash !== s.sourceHash))) ||
    (t &&
      (t.trialBalanceDatasetId === null ||
        t.importBatchId !== null ||
        t.inputGeneration !== c.inputGeneration))
  )
    throw new Error('Unsupported completeness context');
}
function checkPage(
  p: { page: number; pageSize: number; totalCount: number; items: unknown[] },
  size: number,
) {
  if (
    p.page < 1 ||
    p.pageSize !== size ||
    p.items.length > p.totalCount ||
    (p.page - 1) * size > 2147483647
  )
    throw new Error('Unsupported completeness page');
}
function checkTb(t: ReturnType<typeof tb>, c: ReturnType<typeof context>, opening: boolean) {
  if (
    t.revision < 1 ||
    t.periodId !== (opening ? c.priorPeriodId : c.source.periodId) ||
    (!opening && t.bookId !== c.source.bookId) ||
    (opening && !c.bookCode)
  )
    throw new Error('Unsupported source pair');
}
export function decodeCompletenessWorkspace(raw: unknown) {
  const w = workspace(raw, '');
  checkContext(w.context);
  checkPage(w.closingSources, 20);
  checkPage(w.openingSources, 20);
  for (const t of w.closingSources.items) checkTb(t, w.context, false);
  for (const t of w.openingSources.items) checkTb(t, w.context, true);
  if (new Set(w.bridges.map((x) => x.id)).size !== w.bridges.length)
    throw new Error('Unsupported bridge history');
  return w;
}
export function decodeCompletenessPlan(raw: unknown) {
  const p = plan(raw, '');
  checkContext(p.context);
  checkTb(p.closing, p.context, false);
  if (p.opening) checkTb(p.opening, p.context, true);
  if (
    (p.canPrepare &&
      (!p.context.canPrepare ||
        p.blocker !== null ||
        p.existingBridgeId !== null ||
        p.operations.length > 0)) ||
    (!p.canPrepare && !p.blocker)
  )
    throw new Error('Unsupported completeness plan');
  return p;
}
export function decodeCompletenessReview(raw: unknown) {
  const r = review(raw, '');
  checkContext(r.context);
  checkPage(r.residuals, 50);
  const b = r.bridge,
    s = r.context.source;
  if (
    b.clientId !== s.clientId ||
    b.engagementId !== s.engagementId ||
    b.periodId !== s.periodId ||
    b.bookId !== s.bookId ||
    b.importBatchId !== s.batchId ||
    b.generalLedgerHash !== s.sourceHash ||
    (b.openingTrialBalanceDatasetId === null
      ? b.openingTrialBalanceHash !== ''
      : !/^[a-f0-9]{64}$/i.test(b.openingTrialBalanceHash)) ||
    (r.canReject &&
      (r.reviewBlocker !== null ||
        !r.context.canPrepare ||
        !['RECONCILED', 'UNRECONCILED'].includes(b.status))) ||
    (r.canApprove &&
      (!r.canReject ||
        r.approvalBlocker !== null ||
        b.status !== 'RECONCILED' ||
        b.incompleteExtract ||
        b.openingTrialBalanceDatasetId === null ||
        b.mismatchedAccountCount !== 0 ||
        b.openingMovementMismatchedAccountCount !== 0 ||
        b.journalExceptionCount !== 0 ||
        !/^-?0(?:\.0+)?$/.test(b.absoluteResidual) ||
        !/^-?0(?:\.0+)?$/.test(b.openingMovementResidual))) ||
    (!r.canApprove && !r.approvalBlocker) ||
    (!r.canReject && !r.reviewBlocker) ||
    r.residuals.items.some(
      (x) => (x.opening === null) !== (x.openingMovementDifference === null),
    ) ||
    new Set(r.residuals.items.map((x) => x.accountCode)).size !== r.residuals.items.length
  )
    throw new Error('Unsupported completeness review');
  return r;
}
export type CompletenessWorkspace = ReturnType<typeof decodeCompletenessWorkspace>;
export type CompletenessPlan = ReturnType<typeof decodeCompletenessPlan>;
export type CompletenessReview = ReturnType<typeof decodeCompletenessReview>;
export interface CompletenessDraft {
  trialBalanceId: string;
  openingId: string;
  bridgeId: string;
  action: 'prepare' | 'approve' | 'reject';
  evidenceReference: string;
}
export function decodeCompletenessDraft(raw: unknown): CompletenessDraft | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const v = raw as Record<string, unknown>,
    keys = ['trialBalanceId', 'openingId', 'bridgeId', 'action', 'evidenceReference'];
  if (
    Object.keys(v).length !== keys.length ||
    keys.some((k) => typeof v[k] !== 'string') ||
    !['prepare', 'approve', 'reject'].includes(v['action'] as string) ||
    (v['evidenceReference'] as string).length > 2000
  )
    return null;
  for (const k of ['trialBalanceId', 'openingId', 'bridgeId'])
    if (
      v[k] !== '' &&
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v[k] as string)
    )
      return null;
  return v as unknown as CompletenessDraft;
}
