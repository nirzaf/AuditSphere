import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { forkJoin, Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { SHARED } from '../../core/ui';
import {
  ClientContactRouting,
  ClientOrganizationHierarchy,
  decodeClientHierarchy,
  decodeClientRoutings,
} from './client-relationships-contracts';

@Component({
  selector: 'audit-client-relationships',
  imports: [RouterLink, MatButtonModule, MatProgressBarModule, ...SHARED],
  templateUrl: './client-relationships.html',
  styleUrl: './client-relationships.scss',
})
export class ClientRelationships {
  readonly clientId = input.required<string>();

  readonly hierarchy = signal<ClientOrganizationHierarchy | null>(null);
  readonly routings = signal<IReadOnlyList<ClientContactRouting> | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');

  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private destroyed = false;

  constructor() {
    effect(() => {
      const id = this.clientId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.hierarchy.set(null);
        this.routings.set(null);
        this.error.set('');
        this.loading.set(false);
        if (staff && id) this.load();
      });
    });

    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request?.unsubscribe();
    });
  }

  load(): void {
    const id = this.clientId()?.toLowerCase();
    if (!id || !guidPattern.test(id)) {
      this.error.set('Invalid client identifier.');
      return;
    }

    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');

    const hierarchy$ = this.http.get<unknown>(`/api/ui/clients/${id}/hierarchy`).pipe(timeout(10000));
    const routings$ = this.http.get<unknown>(`/api/ui/clients/${id}/routings`).pipe(timeout(10000));

    this.request = forkJoin({ hierarchy: hierarchy$, routings: routings$ }).subscribe({
      next: (res) => {
        if (this.destroyed) return;
        try {
          const h = decodeClientHierarchy(res.hierarchy);
          const r = decodeClientRoutings(res.routings);
          this.hierarchy.set(h);
          this.routings.set(r);
          this.loading.set(false);
        } catch {
          this.error.set('Unsupported client relationship or routing payload.');
          this.loading.set(false);
        }
      },
      error: (err) => {
        if (this.destroyed) return;
        const msg = err?.error?.message ?? err?.message ?? 'Failed to load client relationships.';
        this.error.set(typeof msg === 'string' ? msg : 'Failed to load client relationships.');
        this.loading.set(false);
      },
    });
  }
}
type IReadOnlyList<T> = readonly T[];
