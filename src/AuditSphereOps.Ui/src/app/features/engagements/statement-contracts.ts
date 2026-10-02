import { guidPattern } from '../../core/contracts';
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
  sha256,
  str,
  oneOf,
} from '../../core/decode';
const basis = obj({
  engagementId: guid,
  clientId: guid,
  mappingId: guid,
  mappingVersion: nat,
  datasetId: guid,
  datasetRevision: nat,
  datasetDigest: sha256,
  currency: str(10),
  periodStart: date,
  periodEnd: date,
  taxonomyVersion: str(100),
  chartVersionId: nullable(guid),
  generation: nat,
  reviewerId: nullable(guid),
  reviewedAt: nullable(instant),
  revision: sha256,
});
const pageShape = obj({
  basis,
  section: oneOf('profit', 'position'),
  title: str(200),
  totalLabel: str(200),
  total: dec,
  balances: bool,
  lineCount: nat,
  filteredCount: nat,
  page: nat,
  pageSize: nat,
  lines: arr(
    obj({
      destinationCode: str(100),
      statementSection: str(100),
      auditArea: str(500),
      amount: dec,
      accountCount: nat,
      procedureCount: nat,
    }),
    25,
  ),
});
const detailShape = obj({
  basis,
  section: oneOf('profit', 'position'),
  destinationCode: str(100),
  statementSection: str(100),
  lineTotal: dec,
  accountCount: nat,
  page: nat,
  pageSize: nat,
  accounts: arr(obj({ accountCode: str(100), accountName: str(2000), amount: dec }), 25),
  procedureCount: nat,
  procedurePage: nat,
  procedures: arr(
    obj({
      procedureId: guid,
      sourceProcedureId: str(100),
      title: str(2000),
      status: str(100),
      section: nullable(str(500)),
    }),
    25,
  ),
});
export const decodeStatementEvidence = obj({
  procedureId: guid,
  currentResult: nullable(
    obj({ id: guid, revision: nat, workPerformed: str(), conclusion: nullable(str()) }),
  ),
  evidence: arr(
    obj({
      linkId: guid,
      uploadIntentId: guid,
      fileName: str(500),
      contentSha256: sha256,
      note: nullable(str()),
      linkedAt: instant,
    }),
    1000,
  ),
});
export const decodeStatementPage = (value: unknown, path: string) => {
  const p = pageShape(value, path);
  if (
    p.pageSize !== 25 ||
    p.page < 1 ||
    p.filteredCount > p.lineCount ||
    p.lines.length > p.filteredCount
  )
    throw new Error('Unsupported statement page');
  return p;
};
export const decodeStatementContributions = (value: unknown, path: string) => {
  const d = detailShape(value, path);
  if (
    d.pageSize !== 25 ||
    d.page < 1 ||
    d.procedurePage < 1 ||
    d.accounts.length > d.accountCount ||
    d.procedures.length > d.procedureCount
  )
    throw new Error('Unsupported statement detail');
  return d;
};
export type StatementPage = ReturnType<typeof decodeStatementPage>;
export type StatementDetail = ReturnType<typeof decodeStatementContributions>;

export interface StatementLocation {
  section: 'profit' | 'position';
  filter: string;
  page: number;
  line: string;
  lineSection: string;
  accounts: number;
  procedures: number;
  procedure: string;
  basis: string;
}
/** Only bounded navigation metadata lives in the URL. No financial rows or cached totals are restored. */
export function statementLocation(get: (key: string) => string | null): StatementLocation | null {
  const section = get('section') ?? 'profit',
    filter = get('filter') ?? '',
    line = get('line') ?? '',
    lineSection = get('lineSection') ?? '';
  const integer = (key: string) => {
    const v = get(key) ?? '1';
    return /^[1-9]\d{0,3}$/.test(v) && Number(v) <= 1000 ? Number(v) : 0;
  };
  const page = integer('page'),
    accounts = integer('accounts'),
    procedures = integer('procedures'),
    procedure = get('procedure') ?? '',
    revision = get('basis') ?? '';
  if (
    !['profit', 'position'].includes(section) ||
    filter.length > 80 ||
    line.length > 100 ||
    lineSection.length > 100 ||
    !page ||
    !accounts ||
    !procedures ||
    !!line !== !!lineSection ||
    (procedure && !guidPattern.test(procedure)) ||
    (revision && !/^[0-9a-f]{64}$/.test(revision))
  )
    return null;
  return {
    section: section as 'profit' | 'position',
    filter,
    page,
    line,
    lineSection,
    accounts,
    procedures,
    procedure,
    basis: revision,
  };
}
export function sameStatementBasis(
  page: StatementPage,
  detail: StatementDetail,
  line: string,
  section: string,
): boolean {
  return (
    JSON.stringify(detail.basis) === JSON.stringify(page.basis) &&
    detail.section === page.section &&
    detail.destinationCode === line &&
    detail.statementSection === section
  );
}
