export const blankLine = () => ({
  reference: '',
  snapshotId: '',
  sourceLineId: '',
  isMonetary: true,
  currency: '',
  foreignAmount: '',
  priorCarrying: '',
  historicalDate: '',
});
export const emptyRemeasurement = () => ({
  rateSet: '',
  policy: '',
  lines: [blankLine()],
  reviewed: false,
});
export type RemeasurementIntent = Omit<ReturnType<typeof emptyRemeasurement>, 'reviewed'>;
export function remeasurementIntent(
  model: ReturnType<typeof emptyRemeasurement>,
): RemeasurementIntent {
  const { reviewed, ...intent } = model;
  return intent;
}
function fields(raw: unknown, bounds: Record<string, number>): Record<string, string> | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const value = raw as Record<string, unknown>;
  if (Object.keys(value).some((k) => !(k in bounds))) return null;
  const result: Record<string, string> = {};
  for (const [key, max] of Object.entries(bounds)) {
    if (typeof value[key] !== 'string' || (value[key] as string).length > max) return null;
    result[key] = value[key] as string;
  }
  return result;
}
/** Bounded allowlisted intent. Review assent, credentials, files and client-supplied revisions are never recovered. */
export function validDraft(raw: unknown): RemeasurementIntent | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const { lines, ...head } = raw as Record<string, unknown>;
  const shared = fields(head, { rateSet: 36, policy: 36 });
  if (!shared || !Array.isArray(lines) || lines.length < 1 || lines.length > 500) return null;
  const result = [];
  for (const line of lines) {
    if (!line || typeof line !== 'object' || Array.isArray(line)) return null;
    const { isMonetary, ...rest } = line;
    const value = fields(rest, {
      reference: 200,
      snapshotId: 36,
      sourceLineId: 36,
      currency: 3,
      foreignAmount: 40,
      priorCarrying: 40,
      historicalDate: 10,
    });
    if (!value || typeof isMonetary !== 'boolean') return null;
    result.push({ ...value, isMonetary });
  }
  return { ...shared, lines: result } as RemeasurementIntent;
}
export function remeasurementAmount(raw: string): string | null {
  const value = raw.trim();
  return /^-?\d{1,14}(\.\d{1,6})?$/.test(value) ? value : null;
}
