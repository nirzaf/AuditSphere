import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { arr, bool, date, dec, guid, instant, nat, nullable, obj, oneOf, text } from '../../core/decode';
import type { Decoder } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const PERIOD_EVIDENCE_KINDS = [
  'period-references',
  'books',
  'trial-balances',
  'general-ledgers',
  'completeness',
  'reconciliations',
  'specialist-schedules',
  'pbc',
  'tasks',
] as const;
export type PeriodEvidenceKind = (typeof PERIOD_EVIDENCE_KINDS)[number];

const UNREPRESENTED_KINDS = [
  'bank-schedules',
  'receivables-schedules',
  'payables-schedules',
  'monthly-handover',
] as const;
type UnrepresentedKind = (typeof UNREPRESENTED_KINDS)[number];

const PAGE_SIZE = 25;
const evidenceKind = oneOf(...PERIOD_EVIDENCE_KINDS);
const unrepresentedKind = oneOf(...UNREPRESENTED_KINDS);
const fail = (path: string, expected: string): never => { throw new Error(`${path || 'response'}: expected ${expected}`); };

/** Read a signed Int64 JSON string without converting it to a JavaScript number. */
export const int64String: Decoder<string> = (value, path) => {
  if (typeof value !== 'string' || !/^(?:0|-?[1-9]\d*)$/.test(value)) return fail(path, 'invariant Int64 string');
  const exact = BigInt(value);
  if (exact < -9223372036854775808n || exact > 9223372036854775807n) return fail(path, 'invariant Int64 string');
  return value;
};

const decodeCategory = obj({ kind: evidenceKind, totalCount: nat, pageSize: nat });
export const decodePeriodEvidenceIndex = obj({
  periodId: guid,
  clientId: guid,
  categories: arr(decodeCategory, PERIOD_EVIDENCE_KINDS.length),
  unrepresentedCategories: arr(unrepresentedKind, UNREPRESENTED_KINDS.length),
});

export const decodePeriodReferenceEvidence = obj({
  id: guid,
  periodCode: text,
  status: text,
  relation: text,
  startDate: date,
  endDate: date,
});
export type PeriodReferenceEvidence = ReturnType<typeof decodePeriodReferenceEvidence>;

export const decodePeriodBookEvidence = obj({
  id: guid,
  code: text,
  basis: text,
  inclusionRule: text,
  currency: text,
  status: text,
  revision: int64String,
});
export type PeriodBookEvidence = ReturnType<typeof decodePeriodBookEvidence>;

export const decodePeriodTrialBalanceEvidence = obj({
  id: guid,
  engagementId: guid,
  bookId: nullable(guid),
  basis: nullable(text),
  sourceKind: text,
  revision: int64String,
  legalEntityKey: text,
  currency: text,
  rawFileSha256Hex: text,
  normalizedDatasetDigest: text,
  sha256Hex: text,
  balanced: bool,
  validationStatus: text,
  importState: text,
  controlTotal: dec,
  latestAcceptanceReceiptId: nullable(guid),
  importedAt: instant,
  importedByUserId: guid,
});
export type PeriodTrialBalanceEvidence = ReturnType<typeof decodePeriodTrialBalanceEvidence>;

export const decodePeriodGeneralLedgerEvidence = obj({
  id: guid,
  engagementId: guid,
  bookId: nullable(guid),
  sourceKind: text,
  profileVersion: text,
  parserVersion: text,
  rawFileSha256Hex: text,
  normalizedDatasetDigest: text,
  legalEntityKey: text,
  currency: text,
  rowCount: nat,
  expectedChunkCount: nat,
  expectedTransactionCount: nat,
  expectedLineCount: nat,
  acceptedChunkCount: nat,
  acceptedTransactionCount: nat,
  acceptedLineCount: nat,
  status: text,
  receiptReference: text,
  latestAcceptanceReceiptId: nullable(guid),
  createdByUserId: guid,
  createdAt: instant,
});
export type PeriodGeneralLedgerEvidence = ReturnType<typeof decodePeriodGeneralLedgerEvidence>;

export const decodePeriodCompletenessEvidence = obj({
  id: guid,
  engagementId: guid,
  bookId: nullable(guid),
  trialBalanceDatasetId: guid,
  importBatchId: guid,
  openingTrialBalanceDatasetId: nullable(guid),
  trialBalanceHash: text,
  generalLedgerHash: text,
  openingTrialBalanceHash: text,
  accountResidualDigest: text,
  openingMovementResidualDigest: text,
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
  status: text,
  incompleteExtract: bool,
  completenessDisclosure: text,
  evidenceReference: text,
  createdByUserId: guid,
  reviewedByUserId: nullable(guid),
  createdAt: instant,
  reviewedAt: nullable(instant),
});
export type PeriodCompletenessEvidence = ReturnType<typeof decodePeriodCompletenessEvidence>;

export const decodePeriodReconciliationProofEvidence = obj({
  id: guid,
  formulaVersion: text,
  sourceTotal: dec,
  glTotal: dec,
  itemsSignedTotal: dec,
  residual: dec,
  isReconciled: bool,
  itemCount: nat,
  itemManifestDigest: text,
  sourceHash: text,
  inputGeneration: int64String,
  createdByUserId: guid,
  createdAt: instant,
});
export type PeriodReconciliationProofEvidence = ReturnType<typeof decodePeriodReconciliationProofEvidence>;

export const decodePeriodReconciliationEvidence = obj({
  id: guid,
  engagementId: guid,
  bookId: nullable(guid),
  area: text,
  trialBalanceDatasetId: nullable(guid),
  importBatchId: nullable(guid),
  accountSelection: text,
  asOfDate: date,
  agingBasis: text,
  agingBucketRuleVersion: text,
  sourceTotal: dec,
  glTotal: dec,
  residual: dec,
  sourceHash: text,
  status: text,
  revision: int64String,
  supersedesReconciliationId: nullable(guid),
  inputGeneration: int64String,
  createdByUserId: guid,
  reviewedByUserId: nullable(guid),
  createdAt: instant,
  reviewedAt: nullable(instant),
  latestProof: nullable(decodePeriodReconciliationProofEvidence),
});
export type PeriodReconciliationEvidence = ReturnType<typeof decodePeriodReconciliationEvidence>;

export const decodePeriodSpecialistScheduleEvidence = obj({
  id: guid,
  engagementId: guid,
  revision: int64String,
  supersedesScheduleId: nullable(guid),
  area: text,
  methodologyVersion: text,
  managementAmount: dec,
  calculatedAmount: dec,
  difference: dec,
  assumptionsHash: text,
  evidenceReference: text,
  reviewConclusion: text,
  status: text,
  createdByUserId: guid,
  reviewedByUserId: nullable(guid),
  createdAt: instant,
  reviewedAt: nullable(instant),
});
export type PeriodSpecialistScheduleEvidence = ReturnType<typeof decodePeriodSpecialistScheduleEvidence>;

export const decodePeriodPbcEvidence = obj({
  id: guid,
  engagementId: guid,
  objective: text,
  periodStart: text,
  periodEnd: text,
  area: text,
  clientOwnerUserId: guid,
  firmOwnerUserId: guid,
  reviewerUserId: guid,
  dueDate: text,
  state: text,
  revision: int64String,
  acceptedByUserId: nullable(guid),
  acceptedAt: nullable(instant),
  latestAcceptanceReceiptId: nullable(guid),
  updatedAt: instant,
});
export type PeriodPbcEvidence = ReturnType<typeof decodePeriodPbcEvidence>;

export const decodePeriodTaskEvidence = obj({
  id: guid,
  engagementId: nullable(guid),
  title: text,
  status: text,
  assigneeUserId: nullable(guid),
  dueDate: nullable(date),
  phase: text,
  mappingVersionId: nullable(guid),
  fsliCode: nullable(text),
  createdAt: instant,
  completedAt: nullable(instant),
});
export type PeriodTaskEvidence = ReturnType<typeof decodePeriodTaskEvidence>;

interface EvidencePageBase {
  periodId: string;
  clientId: string;
  page: number;
  pageSize: number;
  totalCount: number;
}
type EvidencePageOf<K extends PeriodEvidenceKind, T> = EvidencePageBase & { kind: K; items: T[] };
export type PeriodEvidencePage =
  | EvidencePageOf<'period-references', PeriodReferenceEvidence>
  | EvidencePageOf<'books', PeriodBookEvidence>
  | EvidencePageOf<'trial-balances', PeriodTrialBalanceEvidence>
  | EvidencePageOf<'general-ledgers', PeriodGeneralLedgerEvidence>
  | EvidencePageOf<'completeness', PeriodCompletenessEvidence>
  | EvidencePageOf<'reconciliations', PeriodReconciliationEvidence>
  | EvidencePageOf<'specialist-schedules', PeriodSpecialistScheduleEvidence>
  | EvidencePageOf<'pbc', PeriodPbcEvidence>
  | EvidencePageOf<'tasks', PeriodTaskEvidence>;

const decodeEvidencePageEnvelope = obj({
  periodId: guid,
  clientId: guid,
  kind: evidenceKind,
  page: nat,
  pageSize: nat,
  totalCount: nat,
  items: arr((value: unknown) => value, PAGE_SIZE),
});

export function decodePeriodEvidencePage(value: unknown, path: string): PeriodEvidencePage {
  const envelope = decodeEvidencePageEnvelope(value, path);
  const itemsPath = path ? `${path}.items` : 'items';
  switch (envelope.kind) {
    case 'period-references':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodReferenceEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
    case 'books':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodBookEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
    case 'trial-balances':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodTrialBalanceEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
    case 'general-ledgers':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodGeneralLedgerEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
    case 'completeness':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodCompletenessEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
    case 'reconciliations':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodReconciliationEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
    case 'specialist-schedules':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodSpecialistScheduleEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
    case 'pbc':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodPbcEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
    case 'tasks':
      return { ...envelope, kind: envelope.kind, items: arr(decodePeriodTaskEvidence, PAGE_SIZE)(envelope.items, itemsPath) };
  }
  return fail(path, 'known period evidence category');
}

const CATEGORY_LABELS: Record<PeriodEvidenceKind, string> = {
  'period-references': 'Period references',
  books: 'Books',
  'trial-balances': 'Trial balances',
  'general-ledgers': 'General ledgers',
  completeness: 'Completeness',
  reconciliations: 'Reconciliations',
  'specialist-schedules': 'Specialist schedules',
  pbc: 'PBC requests',
  tasks: 'Tasks',
};
const UNREPRESENTED_LABELS: Record<UnrepresentedKind, string> = {
  'bank-schedules': 'Bank schedules',
  'receivables-schedules': 'Receivables schedules',
  'payables-schedules': 'Payables schedules',
  'monthly-handover': 'Monthly handover',
};

interface EvidenceFact { label: string; value: string; }
interface EvidenceRoute { label: string; route: string[]; }
interface EvidenceDisplayRow {
  id: string;
  title: string;
  facts: EvidenceFact[];
  notes: string[];
  links: EvidenceRoute[];
}

function fact(label: string, value: string | number | boolean | null | undefined): EvidenceFact {
  const display = value === null || value === undefined || value === '' ? 'Not recorded' : String(value);
  return { label, value: display };
}
function row(id: string, title: string, facts: EvidenceFact[], notes: string[] = [], links: EvidenceRoute[] = []): EvidenceDisplayRow {
  return { id, title, facts, notes, links };
}

function displayRows(page: PeriodEvidencePage | null): EvidenceDisplayRow[] {
  if (!page) return [];
  switch (page.kind) {
    case 'period-references':
      return page.items.map(x => row(x.id, `Period reference · ${x.periodCode}`, [
        fact('Relation', x.relation), fact('Period code', x.periodCode), fact('Recorded status', x.status),
        fact('Start date', x.startDate), fact('End date', x.endDate),
      ]));
    case 'books':
      return page.items.map(x => row(x.id, `Book · ${x.code}`, [
        fact('Basis', x.basis), fact('Inclusion rule', x.inclusionRule), fact('Currency', x.currency),
        fact('Recorded status', x.status), fact('Revision', x.revision),
      ]));
    case 'trial-balances':
      return page.items.map(x => row(x.id, `Trial balance · ${x.legalEntityKey}`, [
        fact('Source kind', x.sourceKind), fact('Engagement ID', x.engagementId), fact('Book ID', x.bookId),
        fact('Basis', x.basis), fact('Revision', x.revision), fact('Currency', x.currency),
        fact('Import validation', x.validationStatus), fact('Import state', x.importState),
        fact('Balanced flag', x.balanced), fact('Control total', `${x.controlTotal} ${x.currency}`),
        fact('Raw file SHA-256', x.rawFileSha256Hex), fact('Normalized dataset digest', x.normalizedDatasetDigest),
        fact('Dataset SHA-256', x.sha256Hex), fact('Retained acceptance receipt ID', x.latestAcceptanceReceiptId),
        fact('Imported at', x.importedAt), fact('Imported by user ID', x.importedByUserId),
      ], [
        'Import validation describes worker validation only. A retained receipt does not identify the currently selected source or a professional acceptance conclusion.',
      ], x.sourceKind === 'Raw' && x.importState === 'SEALED'
        ? [{ label: 'Open source acceptance', route: ['/app/accounting/sources', x.id, 'acceptance'] }]
        : []));
    case 'general-ledgers':
      return page.items.map(x => row(x.id, `General ledger · ${x.legalEntityKey}`, [
        fact('Source kind', x.sourceKind), fact('Engagement ID', x.engagementId), fact('Book ID', x.bookId),
        fact('Currency', x.currency), fact('Revision profile', x.profileVersion), fact('Parser version', x.parserVersion),
        fact('Row count', x.rowCount), fact('Expected chunks', x.expectedChunkCount),
        fact('Expected transactions', x.expectedTransactionCount), fact('Expected lines', x.expectedLineCount),
        fact('Importer accepted chunks', x.acceptedChunkCount), fact('Importer accepted transactions', x.acceptedTransactionCount),
        fact('Importer accepted lines', x.acceptedLineCount), fact('Retained GL source status', x.status),
        fact('Receipt reference', x.receiptReference), fact('Retained acceptance receipt ID', x.latestAcceptanceReceiptId),
        fact('Raw file SHA-256', x.rawFileSha256Hex), fact('Normalized dataset digest', x.normalizedDatasetDigest),
        fact('Created at', x.createdAt), fact('Created by user ID', x.createdByUserId),
      ], [
        'Sealed GL source history does not establish completeness. Importer acceptance counts are validation evidence; a retained receipt does not identify the currently selected source.',
      ], x.sourceKind === 'GL' && x.status === 'SEALED'
        ? [
            { label: 'Open source acceptance', route: ['/app/accounting/gl-sources', x.id, 'acceptance'] },
            { label: 'Open GL completeness', route: ['/app/accounting/gl-sources', x.id, 'completeness'] },
          ]
        : []));
    case 'completeness':
      return page.items.map(x => row(x.id, `Completeness assessment · ${x.coverageStart} to ${x.coverageEnd}`, [
        fact('Engagement ID', x.engagementId), fact('Book ID', x.bookId),
        fact('Trial balance dataset ID', x.trialBalanceDatasetId), fact('Import batch ID', x.importBatchId),
        fact('Opening trial balance dataset ID', x.openingTrialBalanceDatasetId),
        fact('Trial balance hash', x.trialBalanceHash), fact('General ledger hash', x.generalLedgerHash),
        fact('Opening trial balance hash', x.openingTrialBalanceHash), fact('Account residual digest', x.accountResidualDigest),
        fact('Opening movement residual digest', x.openingMovementResidualDigest),
        fact('Trial balance account count', x.trialBalanceAccountCount), fact('General ledger account count', x.generalLedgerAccountCount),
        fact('Matched account count', x.matchedAccountCount), fact('Mismatched account count', x.mismatchedAccountCount),
        fact('Opening movement mismatched account count', x.openingMovementMismatchedAccountCount),
        fact('Journal exception count', x.journalExceptionCount), fact('Opening amount', x.openingAmount),
        fact('Movement amount', x.movementAmount), fact('Closing amount', x.closingAmount),
        fact('Opening movement residual', x.openingMovementResidual), fact('Absolute residual', x.absoluteResidual),
        fact('Persisted assessment status', x.status), fact('Incomplete extract flag', x.incompleteExtract),
        fact('Completeness disclosure', x.completenessDisclosure), fact('Evidence reference', x.evidenceReference),
        fact('Created at', x.createdAt), fact('Created by user ID', x.createdByUserId),
        fact('Reviewed at', x.reviewedAt), fact('Reviewed by user ID', x.reviewedByUserId),
      ], [
        'This is a retained completeness assessment. It does not replace current source acceptance or establish a professional readiness conclusion.',
      ], [{ label: 'Open GL completeness', route: ['/app/accounting/gl-sources', x.importBatchId, 'completeness'] }]));
    case 'reconciliations':
      return page.items.map(x => row(x.id, `Reconciliation · ${x.area}`, [
        fact('Engagement ID', x.engagementId), fact('Book ID', x.bookId), fact('Trial balance dataset ID', x.trialBalanceDatasetId),
        fact('Import batch ID', x.importBatchId), fact('Account selection', x.accountSelection),
        fact('As of date', x.asOfDate), fact('Aging basis', x.agingBasis),
        fact('Aging bucket rule version', x.agingBucketRuleVersion), fact('Source total', x.sourceTotal),
        fact('GL total', x.glTotal), fact('Residual', x.residual), fact('Source hash', x.sourceHash),
        fact('Persisted reconciliation status', x.status), fact('Revision', x.revision),
        fact('Superseded reconciliation ID', x.supersedesReconciliationId), fact('Input generation', x.inputGeneration),
        fact('Created at', x.createdAt), fact('Created by user ID', x.createdByUserId),
        fact('Reviewed at', x.reviewedAt), fact('Reviewed by user ID', x.reviewedByUserId),
        fact('Latest proof ID', x.latestProof?.id), fact('Proof formula version', x.latestProof?.formulaVersion),
        fact('Proof source total', x.latestProof?.sourceTotal), fact('Proof GL total', x.latestProof?.glTotal),
        fact('Proof signed item total', x.latestProof?.itemsSignedTotal), fact('Proof residual', x.latestProof?.residual),
        fact('Recorded proof reconciled flag', x.latestProof?.isReconciled), fact('Proof item count', x.latestProof?.itemCount),
        fact('Proof item manifest digest', x.latestProof?.itemManifestDigest), fact('Proof source hash', x.latestProof?.sourceHash),
        fact('Proof input generation', x.latestProof?.inputGeneration), fact('Proof created at', x.latestProof?.createdAt),
        fact('Proof created by user ID', x.latestProof?.createdByUserId),
      ], ['This retained proof result is not a current-validity determination. Open reconciliation evidence for live freshness checks.'], [{ label: 'Open reconciliation evidence', route: ['/app/accounting/reconciliations', x.id] }]));
    case 'specialist-schedules':
      return page.items.map(x => row(x.id, `Specialist schedule · ${x.area}`, [
        fact('Engagement ID', x.engagementId), fact('Revision', x.revision), fact('Superseded schedule ID', x.supersedesScheduleId),
        fact('Methodology version', x.methodologyVersion), fact('Management amount', x.managementAmount),
        fact('Calculated amount', x.calculatedAmount), fact('Difference', x.difference),
        fact('Assumptions hash', x.assumptionsHash), fact('Evidence reference', x.evidenceReference),
        fact('Recorded review conclusion', x.reviewConclusion), fact('Persisted schedule status', x.status),
        fact('Created at', x.createdAt), fact('Created by user ID', x.createdByUserId),
        fact('Reviewed at', x.reviewedAt), fact('Reviewed by user ID', x.reviewedByUserId),
      ]));
    case 'pbc':
      return page.items.map(x => row(x.id, `PBC request · ${x.objective}`, [
        fact('Engagement ID', x.engagementId), fact('Period start', x.periodStart), fact('Period end', x.periodEnd),
        fact('Area', x.area), fact('Client owner user ID', x.clientOwnerUserId),
        fact('Firm owner user ID', x.firmOwnerUserId), fact('Reviewer user ID', x.reviewerUserId),
        fact('Due date', x.dueDate), fact('Recorded state', x.state), fact('Revision', x.revision),
        fact('Accepted by user ID', x.acceptedByUserId), fact('Accepted at', x.acceptedAt),
        fact('Retained acceptance receipt ID', x.latestAcceptanceReceiptId), fact('Updated at', x.updatedAt),
      ], [
        'PBC requests match the client and exact period dates; they do not carry a reporting-period ID.',
        'Acceptance fields and receipt IDs are retained request history; they do not establish current source selection or period readiness.',
      ]));
    case 'tasks':
      return page.items.map(x => row(x.id, `Task · ${x.title}`, [
        fact('Engagement', x.engagementId ?? 'Period-only task'), fact('Recorded status', x.status),
        fact('Assignee user ID', x.assigneeUserId), fact('Due date', x.dueDate), fact('Phase', x.phase),
        fact('Mapping version ID', x.mappingVersionId), fact('Financial statement line code', x.fsliCode),
        fact('Created at', x.createdAt), fact('Completed at', x.completedAt),
      ]));
  }
}

@Component({
  selector: 'audit-accounting-period-evidence',
  imports: [MatButtonModule, RouterLink, ...SHARED],
  template: `
    <section id="period-evidence" class="panel" aria-labelledby="period-evidence-heading">
      <h2 id="period-evidence-heading">Period evidence</h2>
      <p>
        This read-only summary lists retained records in the exact client and period scope. Import validation,
        current source selection, GL completeness and professional conclusions are separate checks.
      </p>
      <button matButton="outlined" type="button" (click)="refresh()" [disabled]="index.loading()">
        Refresh period evidence
      </button>
      <audit-state [loading]="index.loading()" [error]="index.error()" label="period evidence index" />
      @if (index.data(); as summary) {
        <label class="category-picker" for="period-evidence-category">Evidence category</label>
        <select
          id="period-evidence-category"
          [value]="selectedKind()"
          [disabled]="pageData.loading()"
          (change)="selectCategory($event, summary.categories)"
        >
          @for (category of summary.categories; track category.kind) {
            <option [value]="category.kind" [selected]="category.kind === selectedKind()">
              {{ categoryLabel(category.kind) }} · {{ category.totalCount }}
            </option>
          }
        </select>

        <audit-state [loading]="pageData.loading()" [error]="pageData.error()" label="evidence page" />
        @if (pageData.data(); as result) {
          <div class="page-summary" role="status">
            {{ categoryLabel(result.kind) }} · {{ result.totalCount }} records · Page {{ result.page }} of
            {{ pageCount(result.totalCount, result.pageSize) }}
          </div>
          <div class="table-scroll" tabindex="0" role="region" aria-label="Period evidence table scroll area">
            <table>
              <caption>{{ categoryLabel(result.kind) }} {{ result.kind === 'pbc' ? 'matching this client and period date range' : 'for this exact reporting period' }}</caption>
              <thead><tr><th scope="col">Evidence record</th><th scope="col">Retained details</th><th scope="col">Related checks</th></tr></thead>
              <tbody>
                @for (record of rows(); track record.id) {
                  <tr>
                    <td data-label="Evidence record"><strong>{{ record.title }}</strong><small><code>{{ record.id }}</code></small></td>
                    <td data-label="Retained details">
                      <dl class="facts">
                        @for (item of record.facts; track item.label) {
                          <dt>{{ item.label }}</dt><dd>{{ item.value }}</dd>
                        }
                      </dl>
                      @for (note of record.notes; track note) { <p class="semantic-note">{{ note }}</p> }
                    </td>
                    <td data-label="Related checks">
                      @for (link of record.links; track link.label) {
                        <a class="evidence-link" [routerLink]="link.route">{{ link.label }}</a>
                      } @empty { <span>—</span> }
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="3">No records are returned for this category page.</td></tr>
                }
              </tbody>
            </table>
          </div>
          <nav class="pagination" aria-label="Period evidence pagination">
            <button matButton="outlined" type="button" (click)="page.set(result.page - 1)"
              [disabled]="result.page <= 1 || pageData.loading()">Previous evidence page</button>
            <span>Page {{ result.page }} of {{ pageCount(result.totalCount, result.pageSize) }}</span>
            <button matButton="outlined" type="button" (click)="page.set(result.page + 1)"
              [disabled]="result.page * result.pageSize >= result.totalCount || pageData.loading()">Next evidence page</button>
          </nav>
        }

        @if (summary.unrepresentedCategories.length) {
          <section class="coverage-note" aria-labelledby="period-evidence-coverage-heading">
            <h3 id="period-evidence-coverage-heading">Not assessed in this view</h3>
            <p>These categories are outside this panel's typed sources; this does not establish whether records exist elsewhere.</p>
            <ul>
              @for (kind of summary.unrepresentedCategories; track kind) {
                <li>{{ unrepresentedLabel(kind) }}</li>
              }
            </ul>
          </section>
        }
      }
    </section>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .category-picker { display: block; margin-block: 1rem .35rem; font-weight: 600; }
    select { min-height: 2.75rem; max-width: 100%; width: min(100%, 28rem); }
    .page-summary { margin-block: 1rem .5rem; }
    .table-scroll { max-width: 100%; overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: .7rem; vertical-align: top; overflow-wrap: anywhere; }
    td { border-top: 1px solid var(--as-line); }
    small { display: block; margin-block-start: .25rem; }
    .facts { display: grid; grid-template-columns: minmax(8.5rem, .42fr) minmax(0, 1fr); gap: .25rem .75rem; margin: 0; }
    .facts dt { font-weight: 600; }
    .facts dd { min-width: 0; margin: 0; overflow-wrap: anywhere; }
    .semantic-note { margin-block: .75rem 0; }
    .evidence-link { display: block; margin-block-end: .5rem; }
    .pagination { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem; margin-block-start: 1rem; }
    .coverage-note { margin-block-start: 1.25rem; border-top: 1px solid var(--as-line); padding-block-start: .75rem; }
    @media (max-width: 40rem) {
      .panel { padding: 1rem; }
      thead { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }
      tbody, tbody tr, tbody td { display: block; width: 100%; box-sizing: border-box; }
      tbody tr { padding: .5rem 0; border-top: 1px solid var(--as-line); }
      tbody td { border: 0; padding: .45rem 0; }
      tbody td::before { content: attr(data-label); display: block; margin-block-end: .25rem; font-weight: 700; }
      .facts { grid-template-columns: minmax(7rem, .38fr) minmax(0, 1fr); gap: .3rem .6rem; }
      .pagination { align-items: stretch; }
      .pagination button, .pagination span { flex: 1 1 100%; }
      .pagination span { text-align: center; }
    }
  `],
})
export class AccountingPeriodEvidence {
  readonly periodId = input.required<string>();
  readonly clientId = input.required<string>();
  readonly selectedKind = signal<PeriodEvidenceKind>('books');
  readonly page = signal(1);
  private readonly api = inject(Api);

  readonly index = this.api.resource(
    () => {
      const periodId = this.periodId();
      this.clientId();
      return periodId ? `/api/ui/accounting/periods/${periodId}/evidence` : null;
    },
    (raw, path) => {
      const result = decodePeriodEvidenceIndex(raw, path);
      if (result.periodId !== this.periodId() || result.clientId !== this.clientId()) throw new Error('Unexpected period evidence scope');
      if (result.categories.length !== PERIOD_EVIDENCE_KINDS.length ||
          new Set(result.categories.map(category => category.kind)).size !== PERIOD_EVIDENCE_KINDS.length ||
          result.categories.some(category => category.pageSize !== PAGE_SIZE) ||
          result.unrepresentedCategories.length !== UNREPRESENTED_KINDS.length ||
          new Set(result.unrepresentedCategories).size !== UNREPRESENTED_KINDS.length) {
        throw new Error('Unexpected period evidence categories');
      }
      return result;
    },
    'This period evidence is unavailable in the current client scope.',
  );

  readonly pageData = this.api.resource(
    () => {
      const periodId = this.periodId();
      const clientId = this.clientId();
      const summary = this.index.data();
      const kind = this.selectedKind();
      const page = this.page();
      if (!periodId || !clientId || !summary || !summary.categories.some(category => category.kind === kind)) return null;
      return `/api/ui/accounting/periods/${periodId}/evidence/${kind}?page=${page}`;
    },
    (raw, path) => {
      const result = decodePeriodEvidencePage(raw, path);
      if (result.periodId !== this.periodId() || result.clientId !== this.clientId() ||
          result.kind !== this.selectedKind() || result.page < 1 || result.page !== this.page() || result.pageSize !== PAGE_SIZE ||
          result.totalCount < result.items.length || result.items.length > result.pageSize ||
          result.items.length !== Math.max(0, Math.min(result.pageSize, result.totalCount - (result.page - 1) * result.pageSize)) ||
          result.page > Math.max(1, Math.ceil(result.totalCount / result.pageSize))) {
        throw new Error('Unexpected period evidence page');
      }
      return result;
    },
    'This evidence page is unavailable in the current client scope.',
  );

  readonly rows = computed(() => displayRows(this.pageData.data()));

  constructor() {
    effect(() => {
      this.periodId();
      this.clientId();
      this.selectedKind.set('books');
      this.page.set(1);
    });
  }

  categoryLabel(kind: PeriodEvidenceKind): string { return CATEGORY_LABELS[kind]; }
  unrepresentedLabel(kind: UnrepresentedKind): string { return UNREPRESENTED_LABELS[kind]; }
  pageCount(totalCount: number, pageSize: number): number { return Math.max(1, Math.ceil(totalCount / pageSize)); }

  selectCategory(event: Event, categories: ReturnType<typeof decodePeriodEvidenceIndex>['categories']): void {
    const kind = (event.target as HTMLSelectElement).value;
    if (categories.some(category => category.kind === kind)) {
      this.selectedKind.set(kind as PeriodEvidenceKind);
      this.page.set(1);
    }
  }

  refresh(): void {
    this.page.set(1);
    this.index.reload();
  }
}
