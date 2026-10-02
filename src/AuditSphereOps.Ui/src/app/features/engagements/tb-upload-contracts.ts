import { arr, bool, dec, guid, nat, nullable, obj, sha256, str } from '../../core/decode';

export const decodeUploadReview = obj({
  engagementId: guid,
  clientId: guid,
  fileSha256: sha256,
  revision: sha256,
  canImport: bool,
  periods: arr(
    obj({
      periodCode: str(100),
      periodId: nullable(guid),
      periodRevision: nullable(sha256),
      rowCount: nat,
      currency: nullable(str(3)),
      netTotal: nullable(dec),
      balanced: bool,
      error: nullable(str(2000)),
      datasetId: nullable(guid),
      importState: nullable(str(30)),
      validationStatus: nullable(str(30)),
      operationId: nullable(guid),
      operationState: nullable(str(60)),
    }),
    24,
  ),
});
export type UploadReview = ReturnType<typeof decodeUploadReview>;
export interface UploadCheckpoint {
  fileName: string;
  fileSha256: string;
  byteCount: number;
}
/** Metadata only: no file bytes, financial rows, credentials or assent survive recovery. */
export function uploadCheckpoint(raw: unknown): UploadCheckpoint | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const x = raw as Record<string, unknown>;
  if (
    Object.keys(x).sort().join(',') !== 'byteCount,fileName,fileSha256' ||
    typeof x['fileName'] !== 'string' ||
    x['fileName'].length > 255 ||
    !/\.(csv|xlsx)$/i.test(x['fileName']) ||
    typeof x['fileSha256'] !== 'string' ||
    !/^[a-f0-9]{64}$/.test(x['fileSha256']) ||
    !Number.isSafeInteger(x['byteCount']) ||
    Number(x['byteCount']) < 1 ||
    Number(x['byteCount']) > 25 * 1024 * 1024
  )
    return null;
  return x as unknown as UploadCheckpoint;
}
