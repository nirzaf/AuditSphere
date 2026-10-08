/** Context a statement line passes to the analytical and fieldwork screens. Parsed defensively: a bad link fills nothing. */
export interface StatementLinkContext {
  area: string;
  line: string;
  periodStart: string;
  periodEnd: string;
  revision: string;
  returnUrl: string | null;
}

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const REVISION = /^[0-9a-f]{64}$/;
const UNSAFE = /[\\\u0000-\u001f\u007f]/;

/** Accepts only an in-app path. Absolute URLs, protocol-relative paths and backslashes are refused. */
export function safeReturnPath(value: string | null | undefined): string | null {
  if (!value || value.length > 300) return null;
  if (!value.startsWith('/app/') || value.startsWith('//') || UNSAFE.test(value)) return null;
  return value;
}

/** Reads the link context from a query map. Returns null unless every required key is present and well formed. */
export function statementLinkContext(query: { get(name: string): string | null } | null | undefined): StatementLinkContext | null {
  if (!query) return null;
  const area = (query.get('area') ?? '').trim();
  const line = (query.get('line') ?? '').trim();
  const periodStart = query.get('periodStart') ?? '';
  const periodEnd = query.get('periodEnd') ?? '';
  const revision = query.get('revision') ?? '';
  if (!area || area.length > 80 || !line || line.length > 100) return null;
  if (!DATE.test(periodStart) || !DATE.test(periodEnd) || !REVISION.test(revision)) return null;
  return { area, line, periodStart, periodEnd, revision, returnUrl: safeReturnPath(query.get('returnUrl')) };
}
