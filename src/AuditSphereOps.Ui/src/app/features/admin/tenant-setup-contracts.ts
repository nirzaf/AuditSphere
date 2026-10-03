import { bool, decode, Decoder, guid, instant, nullable, obj, oneOf, sha256, str } from '../../core/decode';

/** Exact positive Int64 revisions remain strings throughout browser transport. */
const revision: Decoder<string> = (value) => {
  if (typeof value !== 'string' || !/^[1-9][0-9]{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new Error('Unsupported setup revision.');
  return value;
};
export const setupFields = obj({ tenantDisplayName: nullable(str(300)),
  mailState: oneOf('CONFIGURED', 'NOT_CONFIGURED'), recordsState: oneOf('CONFIGURED', 'NOT_CONFIGURED') });
const previewShape = obj({ requestId: guid, draftId: guid, expectedRevision: ((value, path) => { const r = revision(value, path); if (r === '9223372036854775807') throw new Error('Unsupported editable revision.'); return r; }) as Decoder<string>, requestHash: sha256,
  reviewBasis: sha256, before: setupFields, fields: setupFields });
export const setupPreview = previewShape;
export const setupReceipt = obj({ id: guid, requestId: guid, draftId: guid, requestHash: sha256,
  appliedRevision: revision, previousFingerprint: sha256, appliedFingerprint: sha256, recordedAt: instant });
const lookupShape = obj({ found: bool, receipt: nullable(setupReceipt) });
export const setupReceiptLookup: Decoder<ReturnType<typeof lookupShape>> = (value, path) => {
  const result = lookupShape(value, path);
  if (result.found !== (result.receipt !== null)) throw new Error('Unsupported setup receipt lookup.');
  return result;
};
export type SetupFields = ReturnType<typeof setupFields>;
export type SetupPreview = ReturnType<typeof setupPreview>;
export type SetupReceipt = ReturnType<typeof setupReceipt>;
export function storedSetupFields(value: unknown): SetupFields | null {
  try {
    const fields = decode(setupFields, value);
    if (fields.tenantDisplayName && /[\u0000-\u001f\u007f-\u009f]/.test(fields.tenantDisplayName)) return null;
    return fields;
  } catch { return null; }
}
