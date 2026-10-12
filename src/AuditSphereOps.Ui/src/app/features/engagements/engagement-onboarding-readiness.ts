import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { money } from '../../core/decode';
import { OnboardingReadinessReport, decodeOnboardingReadinessReport } from './engagement-workflow-contracts';

const APPROVAL_LABELS: Record<string, string> = {
  PENDING: 'Pending',
  AWAITING_EVIDENCE: 'Awaiting evidence',
  APPROVED: 'Approved',
  REJECTED: 'Rejected',
  STALE: 'Stale',
};

/** Statuses that are satisfied. Anything else is shown as pending or blocked, never as cleared. */
const SATISFIED = new Set(['CURRENT', 'ISSUED', 'PAID', 'ACTIVE']);
const PENDING = new Set(['AWAITING_CLIENT', 'ELIGIBLE', 'READY_FOR_REVIEW', 'NOT_INVOICED', 'INVOICED_UNPAID', 'PARTIALLY_ALLOCATED']);

/**
 * Dual-Key onboarding readiness (WF-03). Client consent, Partner risk clearance, the engagement letter and the advance payment
 * are separate cards with their own persisted evidence. Nothing here is an approval control: each card links to the existing
 * protected screen, and the protected commands re-check when they run.
 */
@Component({
  selector: 'audit-onboarding-readiness',
  imports: [DatePipe, RouterLink, MatButtonModule],
  templateUrl: './engagement-onboarding-readiness.html',
  styleUrl: './engagement-onboarding-readiness.scss',
})
export class EngagementOnboardingReadiness {
  readonly engagementId = input.required<string>();
  readonly data = signal<OnboardingReadinessReport | null>(null);
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

  /** The STE v2.2 approval label for a card. Only Approved clears a key. */
  approvalLabel(displayStatus: string): string {
    return APPROVAL_LABELS[displayStatus] ?? 'Pending';
  }

  tone(status: string): 'satisfied' | 'pending' | 'blocked' {
    if (SATISFIED.has(status)) return 'satisfied';
    if (PENDING.has(status)) return 'pending';
    return 'blocked';
  }

  amount(value: string | null): string {
    return money(value);
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
      .get<unknown>(`/api/ui/engagements/${id}/onboarding-readiness`)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          try {
            const report = decodeOnboardingReadinessReport(value);
            if (report.engagementId.toLowerCase() !== id) throw new Error('Engagement context mismatch');
            this.data.set(report);
          } catch {
            this.data.set(null);
            this.error.set('Unsupported onboarding readiness response format.');
          }
          this.loading.set(false);
        },
        error: (err) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          this.data.set(null);
          this.loading.set(false);
          if (err.status === 401) {
            this.session.clear();
            return;
          }
          // The server answers 403 for any refusal, including a role that may not see commercial evidence; say so plainly.
          this.error.set(
            err.status === 403
              ? 'Onboarding readiness is not available to your role on this engagement.'
              : 'Onboarding readiness unavailable.',
          );
        },
      });
  }
}
