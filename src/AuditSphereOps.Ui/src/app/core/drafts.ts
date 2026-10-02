import { Injectable, inject } from '@angular/core';
import { SessionService } from './session';

/**
 * Unsubmitted form drafts kept in this browser only, keyed by firm, user and scope so a different signed-in identity
 * never sees another user's draft. Drafts are cleared after a confirmed submission and on sign-out; storage failures
 * are ignored so the page always remains usable. Drafts are convenience state, never evidence.
 */
@Injectable({ providedIn: 'root' })
export class Drafts {
  private readonly session = inject(SessionService);
  private key(scope: string): string | null {
    const s = this.session.current();
    return s ? `auditsphere-draft:${s.firmId}:${s.userId}:${scope}` : null;
  }
  load<T>(scope: string, validate: (value: unknown) => T | null): T | null {
    const key = this.key(scope);
    if (!key) return null;
    try {
      const raw = localStorage.getItem(key);
      return raw ? validate(JSON.parse(raw)) : null;
    } catch {
      return null;
    }
  }
  save(scope: string, value: unknown): void {
    const key = this.key(scope);
    if (!key) return;
    try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* storage unavailable or full */ }
  }
  clear(scope: string): void {
    const key = this.key(scope);
    if (!key) return;
    try { localStorage.removeItem(key); } catch { /* ignore */ }
  }
  /** Removes every draft for the current identity (used on sign-out). */
  clearAll(): void {
    try {
      const s = this.session.current();
      if (!s) return;
      const prefix = `auditsphere-draft:${s.firmId}:${s.userId}:`;
      for (let i = localStorage.length - 1; i >= 0; i--) { const k = localStorage.key(i); if (k?.startsWith(prefix)) localStorage.removeItem(k); }
    } catch { /* ignore */ }
  }
}
