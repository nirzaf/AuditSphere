import { DestroyRef, Injectable, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Subscription, timeout } from 'rxjs';
import { NAVIGATION } from '../navigation';
import { SessionService } from './session';

export type WorkspaceSectionAvailability = 'available' | 'not_enabled' | 'not_authorized';
export interface WorkspaceAvailabilityView {
  userId: string;
  firmId: string;
  generation: string;
  staff: boolean;
  destinations: string[];
  tasks: WorkspaceSectionAvailability;
  reviews: WorkspaceSectionAvailability;
  templates: WorkspaceSectionAvailability;
  validForMs: number;
}

/** Presentation hints only. Every destination and action still performs its own server authorization. */
@Injectable({ providedIn: 'root' })
export class WorkspaceAvailability {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly destroy = inject(DestroyRef);
  private readonly knownDestinations = new Set(NAVIGATION.flatMap(group => group.items.map(item => item.id)));
  private request?: Subscription;
  private expiry?: ReturnType<typeof setTimeout>;
  private renewal?: ReturnType<typeof setTimeout>;
  private deadline = 0;
  private wallDeadline = 0;
  private serial = 0;
  private owner = '';
  readonly value = signal<WorkspaceAvailabilityView | null>(null);
  readonly state = signal<'idle' | 'loading' | 'ready' | 'unavailable'>('idle');
  readonly current = (): WorkspaceAvailabilityView | null => {
    const view = this.value(); // Always track the signal, including before the first response.
    return this.sessionOwner() === this.owner && performance.now() < this.deadline && Date.now() < this.wallDeadline ? view : null;
  };
  /** Changes when the accepted availability changes or is cleared, fencing dependent selections. */
  readonly revision = signal(0);

  constructor() {
    effect(() => {
      const actor = this.session.current();
      const invalidation = this.session.invalidation();
      const owner = actor ? `${actor.firmId}:${actor.userId}:${actor.generation}:${actor.staff}:${invalidation}` : '';
      untracked(() => {
        if (owner === this.owner) return;
        this.owner = owner;
        this.clear('idle');
        if (actor?.staff) this.refresh();
      });
    });
    const refresh = () => { if (document.visibilityState === 'visible') this.refresh(); };
    const timer = setInterval(refresh, 30000);
    window.addEventListener('focus', refresh);
    document.addEventListener('visibilitychange', refresh);
    this.destroy.onDestroy(() => {
      ++this.serial;
      this.request?.unsubscribe();
      if (this.expiry !== undefined) clearTimeout(this.expiry);
      if (this.renewal !== undefined) clearTimeout(this.renewal);
      clearInterval(timer);
      window.removeEventListener('focus', refresh);
      document.removeEventListener('visibilitychange', refresh);
    });
  }

  refresh(): void {
    const actor = this.session.current();
    if (!actor?.staff || !this.owner) return;
    if (this.value() && (performance.now() >= this.deadline || Date.now() >= this.wallDeadline)) this.clear('unavailable');
    if (this.request && !this.request.closed) return;
    const owner = this.owner;
    const serial = ++this.serial;
    const startedAt = performance.now();
    const startedWall = Date.now();
    if (!this.value()) this.state.set('loading');
    this.request = this.http.get<unknown>('/api/ui/workspace-availability').pipe(timeout(20000)).subscribe({
      next: value => {
        if (serial !== this.serial || owner !== this.owner || owner !== this.sessionOwner()) return;
        // A delayed renewal cannot bridge a period in which the old grant expired.
        if (this.value() && (performance.now() >= this.deadline || Date.now() >= this.wallDeadline)) {
          this.clear('unavailable');
          this.refresh();
          return;
        }
        const decoded = this.decode(value);
        if (!decoded || decoded.userId.toLowerCase() !== actor.userId.toLowerCase() ||
            decoded.firmId.toLowerCase() !== actor.firmId.toLowerCase() ||
            decoded.generation !== actor.generation || decoded.staff !== actor.staff) {
          this.clear('unavailable');
          return;
        }
        // Subtract the full request duration: response transit must not extend an expiring grant.
        const remaining = decoded.validForMs - Math.max(performance.now() - startedAt, Date.now() - startedWall);
        if (remaining <= 0) { this.clear('unavailable'); return; }
        if (this.expiry !== undefined) clearTimeout(this.expiry);
        if (this.renewal !== undefined) clearTimeout(this.renewal);
        this.deadline = performance.now() + remaining;
        this.wallDeadline = Date.now() + remaining;
        // Renew ahead of expiry, but never extend stale state if the refresh stalls.
        this.renewal = setTimeout(() => this.refresh(), remaining - Math.min(5000, remaining / 2));
        this.expiry = setTimeout(() => { this.clear('unavailable'); this.refresh(); }, remaining);
        const previous = this.value();
        const stable = (view: WorkspaceAvailabilityView) => JSON.stringify({ ...view, validForMs: 0 });
        if (!previous || stable(previous) !== stable(decoded)) {
          this.value.set(decoded);
          this.revision.update(value => value + 1);
        }
        this.state.set('ready');
      },
      error: (error: unknown) => {
        if (serial !== this.serial || owner !== this.owner || owner !== this.sessionOwner()) return;
        this.clear('unavailable');
        if (error instanceof HttpErrorResponse && error.status === 401) this.session.clear();
      },
    });
  }

  /** A forbidden section read invalidates the hint until the next bounded refresh. */
  invalidate(): void { this.clear('unavailable'); }

  private clear(state: 'idle' | 'unavailable'): void {
    ++this.serial;
    this.request?.unsubscribe();
    this.request = undefined;
    if (this.expiry !== undefined) clearTimeout(this.expiry);
    if (this.renewal !== undefined) clearTimeout(this.renewal);
    this.expiry = undefined; this.renewal = undefined; this.deadline = 0; this.wallDeadline = 0;
    this.value.set(null);
    this.state.set(state);
    this.revision.update(value => value + 1);
  }

  private sessionOwner(): string {
    const actor = this.session.current();
    return actor ? `${actor.firmId}:${actor.userId}:${actor.generation}:${actor.staff}:${this.session.invalidation()}` : '';
  }

  private decode(value: unknown): WorkspaceAvailabilityView | null {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
    const data = value as Record<string, unknown>;
    const state = (value: unknown): value is WorkspaceSectionAvailability =>
      value === 'available' || value === 'not_enabled' || value === 'not_authorized';
    if (typeof data['userId'] !== 'string' || typeof data['firmId'] !== 'string' ||
        typeof data['generation'] !== 'string' || typeof data['staff'] !== 'boolean' ||
        !Array.isArray(data['destinations']) || data['destinations'].length > this.knownDestinations.size ||
        !data['destinations'].every(id => typeof id === 'string' && this.knownDestinations.has(id)) ||
        new Set(data['destinations']).size !== data['destinations'].length ||
        !state(data['tasks']) || !state(data['reviews']) || !state(data['templates']) ||
        typeof data['validForMs'] !== 'number' || !Number.isInteger(data['validForMs']) ||
        data['validForMs'] < 1 || data['validForMs'] > 30000) return null;
    return { userId: data['userId'], firmId: data['firmId'], generation: data['generation'], staff: data['staff'],
      destinations: [...data['destinations'] as string[]].sort(), tasks: data['tasks'], reviews: data['reviews'], templates: data['templates'], validForMs: data['validForMs'] };
  }
}
