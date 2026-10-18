import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { arr, decode, guid, obj, str } from '../../core/decode';
import {
  EngagementMilestonePlan, EngagementWorkflowReport, PhaseChecklist, WorkbenchLink, WorkflowMilestoneRow,
  decodeEngagementMilestonePlan, decodeEngagementWorkflowReport, decodeEngagementWorkflowTasks, EngagementWorkflowTask,
  phaseChecklists, projectWorkflowMilestones, workbenchLinksFor,
} from './engagement-workflow-contracts';
import { EngagementReminders } from './engagement-reminders';
import { WorkflowTaskCreate } from './workflow-task-create';

const PHASE_STATUS_LABELS: Record<string, string> = {
  NOT_STARTED: 'Not started',
  IN_PROGRESS: 'In progress',
  BLOCKED: 'Blocked',
  COMPLETED: 'Completed',
  ARCHIVED: 'Archived',
};
const assigneeOptionsDecoder = arr(obj({ userId: guid, displayName: str(200) }), 200);

/**
 * Seven-phase engagement workbench (WF-02). The server projects the phase from the canonical lifecycle; this view renders
 * that projection and never mutates a stage. Completed phases mean the workflow moved past them, not that a regulatory
 * gate was cleared on the basis of checklist progress.
 */
@Component({
  selector: 'audit-engagement-workflow',
  imports: [RouterLink, MatButtonModule, EngagementReminders, WorkflowTaskCreate],
  templateUrl: './engagement-workflow.html',
  styleUrl: './engagement-workflow.scss',
})
export class EngagementWorkflow {
  readonly engagementId = input.required<string>();
  readonly data = signal<EngagementWorkflowReport | null>(null);
  readonly milestonePlan = signal<EngagementMilestonePlan | null>(null);
  readonly milestonePlanLoaded = signal(false);
  readonly milestonePlanLoading = signal(false);
  readonly milestonePlanError = signal('');
  readonly loading = signal(false);
  readonly error = signal('');
  /** Operational checklists per phase, or null when the task experience is off or closed to this role. */
  readonly checklists = signal<PhaseChecklist[] | null>(null);
  readonly checklistsTruncated = signal(false);
  readonly checklistError = signal('');
  /** Available only after the feature-gated, engagement-authorized task read succeeds. */
  readonly taskCreationAvailable = signal(false);
  readonly taskAssignmentAvailable = signal(false);
  readonly assigneeOptions = signal<{ userId: string; displayName: string }[]>([]);
  readonly assignmentTask = signal<EngagementWorkflowTask | null>(null);
  readonly assignmentUserId = signal('');
  readonly assignmentReviewed = signal(false);
  readonly assignmentBusy = signal(false);
  readonly assignmentMessage = signal('');
  readonly assignmentFailed = signal(false);

  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private milestoneRequest?: Subscription;
  private taskRequest?: Subscription;
  private assignmentRequest?: Subscription;
  private destroyed = false;

  constructor() {
    effect(() => {
      const id = this.engagementId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.request?.unsubscribe();
        this.milestoneRequest?.unsubscribe();
        this.taskRequest?.unsubscribe();
        this.checklists.set(null);
        this.taskCreationAvailable.set(false);
        this.taskAssignmentAvailable.set(false);
        this.assigneeOptions.set([]);
        this.assignmentTask.set(null);
        this.assignmentUserId.set('');
        this.assignmentReviewed.set(false);
        this.assignmentRequest?.unsubscribe();
        this.checklistError.set('');
        this.data.set(null);
        this.milestonePlan.set(null);
        this.milestonePlanLoaded.set(false);
        this.milestonePlanLoading.set(false);
        this.milestonePlanError.set('');
        this.error.set('');
        this.loading.set(false);
        if (staff && id) this.load();
      });
    });

    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request?.unsubscribe();
      this.milestoneRequest?.unsubscribe();
      this.taskRequest?.unsubscribe();
      this.assignmentRequest?.unsubscribe();
    });
  }

  workbenchLinks(report: EngagementWorkflowReport): WorkbenchLink[] {
    return workbenchLinksFor(report.currentPhase.code, report.engagementId, report.clientId);
  }

  /** True when the responsible role is an engagement staffing level, so an empty name list means the level is unstaffed. */
  staffedRole(role: string): boolean {
    return role === 'EngagementPartner' || role === 'AuditManager' || role === 'SeniorAuditor';
  }

  phaseStatus(report: EngagementWorkflowReport, index: number): string {
    return report.phases[index - 1]?.status ?? 'NOT_STARTED';
  }

  scheduledMilestones(): WorkflowMilestoneRow[] {
    const plan = this.milestonePlan();
    return plan ? projectWorkflowMilestones(plan) : [];
  }

  /**
   * Tasks are a separate read from the phase projection. A server with the task experience switched off answers 404, and a
   * role outside task work answers 403; both simply mean there is no checklist to show.
   */
  private loadChecklists(id: string, generation: number): void {
    this.taskRequest?.unsubscribe();
    this.checklistError.set('');
    this.taskRequest = this.http
      .get<unknown>(`/api/ui/engagements/${id}/workflow/tasks`)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          try {
            const workspace = decodeEngagementWorkflowTasks(value);
            if (workspace.engagementId.toLowerCase() !== id) throw new Error('Engagement context mismatch');
            this.checklists.set(phaseChecklists(workspace.tasks));
            this.checklistsTruncated.set(workspace.truncated);
            this.taskCreationAvailable.set(true);
            this.loadAssigneeOptions(id, generation);
          } catch {
            this.checklists.set(null);
            this.taskCreationAvailable.set(false);
            this.checklistError.set('Unsupported task checklist response format.');
          }
        },
        error: (err) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          this.checklists.set(null);
          this.taskCreationAvailable.set(false);
          if (err.status === 401) {
            this.session.clear();
            return;
          }
          this.checklistError.set(err.status === 403 || err.status === 404 ? '' : 'Phase checklists unavailable.');
        },
      });
  }

  onWorkflowTaskCreated(): void {
    this.load();
  }

  openTaskAssignment(task: EngagementWorkflowTask): void {
    if (!this.taskAssignmentAvailable() || !['OPEN', 'IN_PROGRESS'].includes(task.status) || this.assignmentBusy()) return;
    this.assignmentTask.set(task); this.assignmentUserId.set(''); this.assignmentReviewed.set(false);
    this.assignmentMessage.set(''); this.assignmentFailed.set(false);
  }

  selectTaskAssignee(userId: string): void {
    this.assignmentUserId.set(userId); this.assignmentReviewed.set(false); this.assignmentMessage.set('');
  }

  selectedTaskAssignee(): { userId: string; displayName: string } | null {
    return this.assigneeOptions().find(x => x.userId === this.assignmentUserId()) ?? null;
  }

  cancelTaskAssignment(): void {
    if (this.assignmentBusy()) return;
    this.assignmentTask.set(null); this.assignmentUserId.set(''); this.assignmentReviewed.set(false);
  }

  saveTaskAssignment(): void {
    const task = this.assignmentTask(); const owner = this.selectedTaskAssignee(); const report = this.data();
    if (!task || !owner || !report || !this.assignmentReviewed() || this.assignmentBusy() || owner.userId === task.assigneeUserId) return;
    const action = task.assigneeUserId ? 'reassign' : 'assign';
    const url = `/api/ui/engagements/${report.engagementId}/workflow/tasks/${task.id}/${action}`;
    this.assignmentBusy.set(true); this.assignmentFailed.set(false); this.assignmentMessage.set('');
    this.assignmentRequest = this.http.post<unknown>(url, { assigneeUserId: owner.userId, expectedRevision: task.revision })
      .pipe(timeout(20000)).subscribe({
        next: () => {
          this.assignmentBusy.set(false); this.assignmentTask.set(null); this.assignmentUserId.set('');
          this.assignmentReviewed.set(false); this.assignmentMessage.set(`Task assigned to ${owner.displayName}.`);
          this.load();
        },
        error: (error: { status?: number }) => {
          this.assignmentBusy.set(false); this.assignmentFailed.set(true);
          if (error.status === 401) { this.session.clear(); return; }
          if (error.status === 409) {
            this.assignmentMessage.set('The task changed before assignment. Its current owner and revision have been refreshed; review the exact change again.');
            this.assignmentTask.set(null); this.load(); return;
          }
          this.assignmentMessage.set(error.status === 403
            ? 'Assignment was refused because the assignee is no longer eligible or your authority changed. The current task was refreshed.'
            : 'Assignment was not confirmed. Refresh the engagement task list before trying again.');
          this.assignmentTask.set(null); this.load();
        },
      });
  }

  private loadAssigneeOptions(id: string, generation: number): void {
    this.assignmentRequest?.unsubscribe();
    this.assignmentRequest = this.http.get<unknown>(`/api/ui/engagements/${id}/workflow/tasks/assignees`)
      .pipe(timeout(15000)).subscribe({
        next: value => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          try { this.assigneeOptions.set(decode(assigneeOptionsDecoder, value)); this.taskAssignmentAvailable.set(true); }
          catch { this.assigneeOptions.set([]); this.taskAssignmentAvailable.set(false); }
        },
        error: error => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          this.assigneeOptions.set([]); this.taskAssignmentAvailable.set(false);
          if (error.status === 401) this.session.clear();
        },
      });
  }

  taskStateLabel(status: string): string {
    return ({ OPEN: 'Open', IN_PROGRESS: 'In progress', COMPLETED: 'Done', CANCELLED: 'Cancelled' } as Record<string, string>)[status] ?? 'Unknown';
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
    this.loadChecklists(id, generation);
    this.loadMilestones(id, generation);
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

  private loadMilestones(id: string, generation: number): void {
    this.milestoneRequest?.unsubscribe();
    this.milestonePlan.set(null);
    this.milestonePlanLoaded.set(false);
    this.milestonePlanError.set('');
    this.milestonePlanLoading.set(true);
    this.milestoneRequest = this.http.get<unknown>(`/api/ui/engagements/${id}/milestones`)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          try {
            if (value === null) {
              this.milestonePlan.set(null);
            } else {
              const plan = decodeEngagementMilestonePlan(value);
              if (plan.engagementId.toLowerCase() !== id) throw new Error('Engagement context mismatch');
              this.milestonePlan.set(plan);
            }
            this.milestonePlanLoaded.set(true);
          } catch {
            this.milestonePlanError.set('Saved milestone schedule has an unsupported response format.');
          }
          this.milestonePlanLoading.set(false);
        },
        error: (error) => {
          if (this.destroyed || generation !== this.session.invalidation()) return;
          this.milestonePlan.set(null);
          this.milestonePlanLoading.set(false);
          if (error.status === 401) {
            this.session.clear();
            return;
          }
          this.milestonePlanError.set(error.status === 403
            ? 'The saved milestone schedule is unavailable to your role on this engagement.'
            : 'Saved milestone schedule unavailable. Refresh the workflow to retry.');
        },
      });
  }
}
