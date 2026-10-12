import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { EngagementWorkflowReport, decodeEngagementWorkflowReport, workbenchLinksFor, WorkbenchLink } from './engagement-workflow-contracts';

const PHASE_STATUS_LABELS: Record<string, string> = {
  NOT_STARTED: 'Not started',
  IN_PROGRESS: 'In progress',
  BLOCKED: 'Blocked',
  COMPLETED: 'Completed',
  ARCHIVED: 'Archived',
};

/**
 * Seven-phase engagement workbench (WF-02). The server projects the phase from the canonical lifecycle; this view renders
 * that projection and never mutates a stage. Completed phases mean the workflow moved past them, not that a regulatory
 * gate was cleared on the basis of checklist progress.
 */
@Component({
  selector: 'audit-engagement-workflow',
  imports: [RouterLink, MatButtonModule],
  templateUrl: './engagement-workflow.html',
  styleUrl: './engagement-workflow.scss',
})
export class EngagementWorkflow {
  readonly engagementId = input.required<string>();
  readonly data = signal<EngagementWorkflowReport | null>(null);
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

  workbenchLinks(report: EngagementWorkflowReport): WorkbenchLink[] {
    return workbenchLinksFor(report.currentPhase.code, report.engagementId, report.clientId);
  }

  /** True when the responsible role is an engagement staffing level, so an empty name list means the level is unstaffed. */
  staffedRole(role: string): boolean {
    return role === 'EngagementPartner' || role === 'AuditManager' || role === 'SeniorAuditor';
  }

  statusLabel(status: string): string {
    return PHASE_STATUS_LABELS[status] ?? 'Unknown';
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
      .get<unknown>(`/api/ui/engagements/${id}/workflow`)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          try {
            const report = decodeEngagementWorkflowReport(value);
            if (report.engagementId.toLowerCase() !== id) throw new Error('Engagement context mismatch');
            this.data.set(report);
          } catch {
            this.data.set(null);
            this.error.set('Unsupported workflow response format.');
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
          this.error.set(
            err.status === 403
              ? 'The engagement workflow is not available to your role on this engagement.'
              : 'Engagement workflow unavailable.',
          );
        },
      });
  }
}
