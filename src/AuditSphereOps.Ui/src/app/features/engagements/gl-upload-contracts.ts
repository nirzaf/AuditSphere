import { arr, bool, date, dec, guid, nat, nullable, obj, sha256, str } from '../../core/decode';
import { uploadCheckpoint } from './tb-upload-contracts';
const shape = obj({
  clientId: guid,
  engagementId: guid,
  periodId: guid,
  periodCode: str(100),
  bookId: nullable(guid),
  bookCode: str(100),
  entity: str(200),
  currency: str(3),
  fileSha256: sha256,
  revision: sha256,
  journalCount: nat,
  lineCount: nat,
  debit: dec,
  credit: dec,
  sample: arr(
    obj({
      journalId: str(200),
      lineId: str(200),
      postingDate: date,
      accountCode: str(100),
      debit: dec,
      credit: dec,
      originalCurrency: str(3),
      originalAmount: dec,
      functionalAmount: dec,
    }),
    20,
  ),
  importBatchId: nullable(guid),
  importState: nullable(str(30)),
  sourceHash: nullable(sha256),
  canImport: bool,
  blocker: nullable(str(2000)),
});
export function decodeGlUpload(raw: unknown, path = 'response') {
  const p = shape(raw, path);
  if (
    p.journalCount < 1 ||
    p.journalCount > 1000 ||
    p.lineCount < 1 ||
    p.lineCount > 5000 ||
    p.sample.length !== Math.min(20, p.lineCount) ||
    !/^[A-Z]{3}$/.test(p.currency) ||
    (p.importBatchId && (!p.importState || !p.sourceHash)) ||
    (p.canImport && (p.importBatchId || p.blocker))
  )
    throw new Error('Unsupported GL upload state');
  return p;
}
export type GlUploadReview = ReturnType<typeof decodeGlUpload>;
export function glUploadCheckpoint(raw: unknown) {
  const p = uploadCheckpoint(raw);
  return p && /\.csv$/i.test(p.fileName) && p.byteCount <= 10000000 ? p : null;
}
