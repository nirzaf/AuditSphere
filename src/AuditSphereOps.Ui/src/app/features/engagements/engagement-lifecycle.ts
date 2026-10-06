import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import {
  CANONICAL_STAGES,
  EngagementLifecycleReport,
  decodeEngagementLifecycleReport,
} from './engagement-lifecycle-contracts';

@Component({
  selector: 'audit-engagement-lifecycle',
  imports: [RouterLink, MatButtonModule],
  templateUrl: './engagement-lifecycle.html',
  styleUrl: './engagement-lifecycle.scss',
})
export class EngagementLifecycle {
  readonly engagementId = input.required<string>();
  readonly stages = CANONICAL_STAGES;
  readonly data = signal<EngagementLifecycleReport | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');

  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private destroyed = false;

  constructor() {
    effect(() => {
      const id = this.engagementId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.data.set(null);
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
    const id = this.engagementId()?.toLowerCase();
    if (!id || !guidPattern.test(id)) {
      this.error.set('Invalid engagement identifier.');
      return;
    }

    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');

    const generation = this.session.invalidation();
    this.request = this.http
      .get<unknown>(`/api/ui/engagements/${id}/lifecycle`)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          try {
            const report = decodeEngagementLifecycleReport(value);
            if (report.engagementId.toLowerCase() !== id) {
              throw new Error('Engagement context mismatch');
            }
            this.data.set(report);
          } catch {
            this.data.set(null);
            this.error.set('Unsupported lifecycle response format.');
          }
          this.loading.set(false);
        },
        error: (err) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          this.data.set(null);
          this.loading.set(false);
          this.error.set(
            err.status === 403
              ? 'Access denied to engagement lifecycle.'
              : 'Engagement lifecycle projection unavailable.',
          );
        },
      });
  }
}
