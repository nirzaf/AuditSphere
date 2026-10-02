import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, timeout } from 'rxjs';

export interface Session {
  userId: string;
  firmId: string;
  generation: string;
  staff: boolean;
}
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  readonly current = signal<Session | null>(null);
  readonly checked = signal(false);
  readonly invalidation = signal(0);
  private logoutRequested = false;
  private pending?: Promise<boolean>;
  refresh(): Promise<boolean> {
    if (!this.pending)
      this.pending = this.read().finally(() => {
        this.pending = undefined;
      });
    return this.pending;
  }
  private async read(): Promise<boolean> {
    if (this.logoutRequested) return false;
    const started = this.invalidation();
    try {
      const value = await firstValueFrom(
        this.http.get<unknown>('/api/ui/session').pipe(timeout(10000)),
      );
      if (!value || typeof value !== 'object') throw new Error('Invalid session');
      const s = value as Record<string, unknown>;
      if (
        !['userId', 'firmId', 'generation'].every((k) => typeof s[k] === 'string') ||
        typeof s['staff'] !== 'boolean'
      )
        throw new Error('Invalid session');
      const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
      if (!uuid.test(String(s['userId'])) || !uuid.test(String(s['firmId'])) || !/^[0-9]{1,19}$/.test(String(s['generation']))) throw new Error('Invalid session identity');
      if (started !== this.invalidation()) return false;
      const next = s as unknown as Session;
      const old = this.current();
      if (
        old &&
        (old.userId !== next.userId ||
          old.firmId !== next.firmId ||
          old.generation !== next.generation)
      )
        this.clear();
      if (
        !old ||
        old.userId !== next.userId ||
        old.firmId !== next.firmId ||
        old.generation !== next.generation ||
        old.staff !== next.staff
      )
        this.current.set(next);
      return true;
    } catch {
      this.clear();
      return false;
    } finally {
      this.checked.set(true);
    }
  }
  async signOut(): Promise<void> {
    this.logoutRequested = true;
    this.clear();
    try {
      await firstValueFrom(this.http.post('/api/ui/sign-out', null).pipe(timeout(10000)));
    } finally {
      this.clear();
    }
  }
  clear(): void {
    this.current.set(null);
    this.invalidation.update((n) => n + 1);
  }
}
