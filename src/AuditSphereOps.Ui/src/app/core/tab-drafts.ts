import { Injectable, effect, inject, untracked } from '@angular/core';
import { SessionService } from './session';

const prefix = 'auditsphere-tab-draft-v1:';
const lifetime = 4 * 60 * 60 * 1000;
const maximumBytes = 400000;
export interface DraftScope {
  entity: string;
  baseRevision: string;
}
export interface TabDraft<T> {
  value: T;
  submissionPending: boolean;
}
export interface PendingRequestReference {
  requestId: string;
  requestHash: string;
}
export function pendingRequestReference(raw: unknown): PendingRequestReference | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const r = raw as Record<string, unknown>;
  return Object.keys(r).length === 2 &&
    typeof r['requestId'] === 'string' &&
    /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(r['requestId']) &&
    typeof r['requestHash'] === 'string' &&
    /^[a-f0-9]{64}$/.test(r['requestHash'])
    ? { requestId: r['requestId'], requestHash: r['requestHash'] }
    : null;
}
export type DraftRead<T> =
  { state: 'ready'; draft: TabDraft<T> } | { state: 'absent' | 'stale' | 'unavailable' };

/** Explicit, tab-only convenience storage. Never stores review authorization, secrets or files.
 * Callers must supply a bounded allowlist decoder. Recovery always requires a fresh authorized base revision.
 * No automatic save, application of recovered fields, or business command is performed here. */
@Injectable({ providedIn: 'root' })
export class TabDrafts {
  private readonly session = inject(SessionService);
  constructor() {
    let epoch = this.session.invalidation();
    effect(() => {
      const next = this.session.invalidation();
      if (next !== epoch) {
        epoch = next;
        untracked(() => this.clearAll());
      }
    });
  }
  private key(entity: string): string | null {
    const s = this.session.current();
    return s?.staff && /^[a-zA-Z0-9:/-]{1,200}$/.test(entity)
      ? `${prefix}${s.firmId}:${s.userId}:${s.generation}:${entity}`
      : null;
  }
  save<T>(
    scope: DraftScope,
    value: unknown,
    validate: (raw: unknown) => T | null,
    submissionPending = false,
  ): boolean {
    const key = this.key(scope['entity']),
      s = this.session.current();
    if (!key || !s || !/^[a-f0-9]{64}$/.test(scope['baseRevision'])) return false;
    try {
      const safe = validate(value);
      if (!safe) return false;
      this.prune(key);
      const now = Date.now();
      const raw = JSON.stringify({
        schemaVersion: 1,
        firmId: s.firmId,
        userId: s.userId,
        generation: s.generation,
        entity: scope['entity'],
        baseRevision: scope['baseRevision'],
        savedAt: now,
        expiresAt: now + lifetime,
        submissionPending,
        value: safe,
      });
      if (raw.length > maximumBytes) return false;
      sessionStorage.setItem(key, raw);
      return true;
    } catch {
      return false;
    }
  }
  read<T>(scope: DraftScope, validate: (raw: unknown) => T | null): DraftRead<T> {
    return this.readCore(scope, validate);
  }
  /** Only after a fresh authorized entity read: recover a pending request's identity
   * across a changed base, so its persisted receipt can be read. This never recovers
   * editable fields, assent or an executable command against a stale revision. */
  readPendingRequest(scope: DraftScope): DraftRead<PendingRequestReference> {
    const r = this.readCore(scope, pendingRequestReference, true);
    return r.state === 'ready' && !r.draft.submissionPending ? { state: 'stale' } : r;
  }
  private readCore<T>(
    scope: DraftScope,
    validate: (raw: unknown) => T | null,
    pendingReferenceOnly = false,
  ): DraftRead<T> {
    const key = this.key(scope['entity']),
      s = this.session.current();
    if (!key || !s || !/^[a-f0-9]{64}$/.test(scope.baseRevision)) return { state: 'absent' };
    try {
      const raw = sessionStorage.getItem(key);
      if (!raw) return { state: 'absent' };
      if (raw.length > maximumBytes) {
        sessionStorage.removeItem(key);
        return { state: 'stale' };
      }
      let e: Record<string, unknown>;
      try {
        e = JSON.parse(raw);
      } catch {
        sessionStorage.removeItem(key);
        return { state: 'stale' };
      }
      const now = Date.now();
      if (
        !e ||
        e['schemaVersion'] !== 1 ||
        e['firmId'] !== s.firmId ||
        e['userId'] !== s.userId ||
        e['generation'] !== s.generation ||
        e['entity'] !== scope['entity'] ||
        typeof e['savedAt'] !== 'number' ||
        typeof e['expiresAt'] !== 'number' ||
        e['savedAt'] > now ||
        e['expiresAt'] <= now ||
        e['expiresAt'] - e['savedAt'] !== lifetime ||
        typeof e['submissionPending'] !== 'boolean' ||
        typeof e['baseRevision'] !== 'string' ||
        !/^[a-f0-9]{64}$/.test(e['baseRevision'])
      ) {
        sessionStorage.removeItem(key);
        return { state: 'stale' };
      }
      const value = validate(e['value']);
      if (!value || (!pendingReferenceOnly && e['baseRevision'] !== scope['baseRevision']))
        return { state: 'stale' };
      return { state: 'ready', draft: { value, submissionPending: e['submissionPending'] } };
    } catch {
      return { state: 'unavailable' };
    }
  }
  clear(entity: string): boolean {
    const key = this.key(entity);
    if (!key) return false;
    try {
      sessionStorage.removeItem(key);
      return true;
    } catch {
      return false;
    }
  }
  clearAll(): void {
    try {
      for (let i = sessionStorage.length - 1; i >= 0; i--) {
        const key = sessionStorage.key(i);
        if (key?.startsWith(prefix)) sessionStorage.removeItem(key);
      }
    } catch {
      /* UI state is still cleared; unavailable storage cannot be claimed erased. */
    }
  }
  private prune(writingKey: string): void {
    const s = this.session.current();
    const identity = s ? `${prefix}${s.firmId}:${s.userId}:${s.generation}:` : '';
    let count = 0;
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
      const key = sessionStorage.key(i);
      if (!key?.startsWith(prefix)) continue;
      let expired = true;
      try {
        const e = JSON.parse(sessionStorage.getItem(key) ?? 'null');
        expired = !e || typeof e['expiresAt'] !== 'number' || e['expiresAt'] <= Date.now();
      } catch {
        /* discard */
      }
      if (!key.startsWith(identity) || expired) sessionStorage.removeItem(key);
      else count++;
    }
    if (count >= 50 && !sessionStorage.getItem(writingKey)) throw new Error('Draft limit');
  }
}
