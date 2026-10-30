import { guidPattern } from '../../core/contracts';
import type { WorkQueueRegistryOwner } from './work-queue-action-registry';

/** Candidates are private discovery hints. They do not authorize or establish a task outcome. */
export interface WorkQueueReceiptCandidate {
  readonly kind: 'evidence' | 'ordering' | 'applicability';
  readonly engagementId: string;
  readonly taskId: string;
  readonly requestKey: string;
  /** Exact original spelling: the existing panels use case-sensitive storage keys. */
  readonly storageKey: string;
}

export type WorkQueueReceiptDiscoveryFailure = 'session_changed' | 'storage_unavailable' | 'storage_changed' | 'limit_exceeded' | 'invalid_candidate';
export type WorkQueueReceiptSnapshot =
  | { readonly complete: true; readonly candidates: readonly WorkQueueReceiptCandidate[] }
  | { readonly complete: false; readonly reason: WorkQueueReceiptDiscoveryFailure };
export type WorkQueueReceiptVerification =
  | { readonly valid: true }
  | { readonly valid: false; readonly reason: WorkQueueReceiptDiscoveryFailure };

export const WORK_QUEUE_RECEIPT_SCAN_MAX_ENTRIES = 500;
export const WORK_QUEUE_RECEIPT_SCAN_MAX_CHARS = 1_000_000;

const prefixes = new Map<string, WorkQueueReceiptCandidate['kind']>([
  ['auditsphere.workflow-evidence', 'evidence'],
  ['auditsphere.workflow-ordering', 'ordering'],
  ['auditsphere.workflow-applicability', 'applicability'],
]);

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value) && value.toLowerCase() !== '00000000-0000-0000-0000-000000000000';
}

function validOwner(owner: WorkQueueRegistryOwner | null): owner is WorkQueueRegistryOwner {
  return !!owner && owner.staff === true && isGuid(owner.firmId) && isGuid(owner.userId) &&
    typeof owner.generation === 'string' && /^(?:0|[1-9][0-9]{0,18})$/.test(owner.generation) &&
    BigInt(owner.generation) <= 9223372036854775807n &&
    Number.isSafeInteger(owner.invalidation) && owner.invalidation >= 0;
}

function sameOwner(a: WorkQueueRegistryOwner, b: WorkQueueRegistryOwner): boolean {
  return a.firmId.toLowerCase() === b.firmId.toLowerCase() && a.userId.toLowerCase() === b.userId.toLowerCase() &&
    a.generation === b.generation && a.invalidation === b.invalidation && a.staff === b.staff;
}

function parseCandidate(key: string, value: string, owner: WorkQueueRegistryOwner): WorkQueueReceiptCandidate | null {
  const parts = key.split(':');
  if (parts.length !== 5) return null;
  const kind = prefixes.get(parts[0]);
  if (!kind || !isGuid(parts[1]) || !isGuid(parts[2]) || !isGuid(parts[3]) || !isGuid(parts[4]) || !isGuid(value) ||
      parts[1].toLowerCase() !== owner.firmId.toLowerCase() || parts[2].toLowerCase() !== owner.userId.toLowerCase()) return null;
  return Object.freeze({ kind, engagementId: parts[3], taskId: parts[4], requestKey: value, storageKey: key });
}

/** Bounded, read-only per-tab discovery. The caller must authorize each exact task before disclosure. */
export class WorkQueueReceiptDiscovery {
  private readonly owners = new WeakMap<WorkQueueReceiptCandidate, WorkQueueRegistryOwner>();
  constructor(private readonly currentOwner: () => WorkQueueRegistryOwner | null,
    private readonly storage: () => Storage = () => sessionStorage) {}

  private current(owner: WorkQueueRegistryOwner): boolean {
    try {
      const current = this.currentOwner();
      return validOwner(owner) && validOwner(current) && sameOwner(owner, current);
    } catch { return false; }
  }

  /** Never returns partial candidates if the snapshot cannot be verified. */
  snapshot(owner: WorkQueueRegistryOwner): WorkQueueReceiptSnapshot {
    if (!this.current(owner)) return { complete: false, reason: 'session_changed' };
    const capturedOwner = { ...owner };
    try {
      const storage = this.storage();
      const length = storage.length;
      if (!Number.isSafeInteger(length) || length < 0 || length > WORK_QUEUE_RECEIPT_SCAN_MAX_ENTRIES)
        return { complete: false, reason: 'limit_exceeded' };
      const entries: { key: string; value: string }[] = [];
      const keys = new Set<string>();
      let chars = 0;
      for (let index = 0; index < length; index++) {
        const key = storage.key(index);
        if (key === null || keys.has(key)) return { complete: false, reason: 'storage_changed' };
        const value = storage.getItem(key);
        if (value === null) return { complete: false, reason: 'storage_changed' };
        chars += key.length + value.length;
        if (chars > WORK_QUEUE_RECEIPT_SCAN_MAX_CHARS) return { complete: false, reason: 'limit_exceeded' };
        keys.add(key); entries.push({ key, value });
      }
      if (storage.length !== length) return { complete: false, reason: 'storage_changed' };
      for (let index = 0; index < length; index++) {
        const entry = entries[index];
        if (storage.key(index) !== entry.key || storage.getItem(entry.key) !== entry.value)
          return { complete: false, reason: 'storage_changed' };
      }
      if (storage.length !== length) return { complete: false, reason: 'storage_changed' };
      if (!this.current(capturedOwner)) return { complete: false, reason: 'session_changed' };
      const candidates: WorkQueueReceiptCandidate[] = [];
      for (const { key, value } of entries) {
        const candidate = parseCandidate(key, value, capturedOwner);
        if (!candidate) {
          const parts = key.split(':');
          if (prefixes.has(parts[0]) && parts[1]?.toLowerCase() === capturedOwner.firmId.toLowerCase() &&
              parts[2]?.toLowerCase() === capturedOwner.userId.toLowerCase()) return { complete: false, reason: 'invalid_candidate' };
          continue;
        }
        this.owners.set(candidate, capturedOwner);
        candidates.push(candidate);
      }
      return { complete: true, candidates: Object.freeze(candidates) };
    } catch {
      return { complete: false, reason: this.current(capturedOwner) ? 'storage_unavailable' : 'session_changed' };
    }
  }

  /** Recheck immediately before use; this is not a substitute for the parent's authorized task read. */
  verify(owner: WorkQueueRegistryOwner, candidate: WorkQueueReceiptCandidate): WorkQueueReceiptVerification {
    if (!this.current(owner)) return { valid: false, reason: 'session_changed' };
    const capturedOwner = this.owners.get(candidate);
    if (!capturedOwner || !sameOwner(capturedOwner, owner)) return { valid: false, reason: 'invalid_candidate' };
    try {
      const storage = this.storage();
      if (storage.getItem(candidate.storageKey) !== candidate.requestKey ||
          storage.getItem(candidate.storageKey) !== candidate.requestKey) return { valid: false, reason: 'storage_changed' };
      return this.current(capturedOwner) ? { valid: true } : { valid: false, reason: 'session_changed' };
    } catch {
      return { valid: false, reason: this.current(capturedOwner) ? 'storage_unavailable' : 'session_changed' };
    }
  }
}
