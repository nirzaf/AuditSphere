import {
  Component,
  DestroyRef,
  HostListener,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, firstValueFrom, timeout } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { FormField } from '@angular/forms/signals';
import { PlanningNavigationDialog } from './planning-navigation';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { Api } from '../../core/api';
import {
  Planning,
  PlanningEditableBudget,
  TeamMember,
  budgetVarianceLabel,
  decodePlanning,
  planningGuid,
} from './planning-contracts';
import { PlanningBudgetState } from './planning-budget-state';
import { PlanningStaffingState } from './planning-staffing-state';
import type { PlanningStateContext } from './planning-state-context';
@Component({
  selector: 'audit-engagement-planning',
  imports: [FormField, RouterLink, MatButtonModule, MatProgressBarModule],
  templateUrl: './planning.html',
})
export class EngagementPlanning {
  readonly budgetVariance = budgetVarianceLabel;
  private readonly dialog = inject(MatDialog);
  private navigationPending = false;
  planningDirty(): boolean {
    return (
      (!!this.data()?.canManageStaffing || !!this.data()?.canPrepareBudget) &&
      (this.budgetState.hasUnsavedEdits() ||
        !!this.selectedUser ||
        this.selectedLevel !== 'STAFF_ASSOCIATE' ||
        this.reviewed ||
        this.budgetReviewed ||
        !!this.revokeTarget() ||
        !!this.budgetPreview() ||
        !!this.approvalPreview() ||
        !!this.staffingPreview() ||
        this.preparationReviewed)
    );
  }
  async confirmNavigation(): Promise<boolean> {
    if (this.destroyed || !this.owner()) return true;
    if (this.loading()) return false;
    if (!this.data()) return true;
    if (this.busy() || this.uncertain()) {
      this.commandStatus.set(
        'Verify the planning outcome and acknowledge its result before leaving.',
      );
      return false;
    }
    if (!this.planningDirty()) return true;
    if (this.navigationPending) return false;
    const owner = this.owner();
    this.navigationPending = true;
    try {
      const choice = await firstValueFrom(this.dialog.open(PlanningNavigationDialog).afterClosed());
      if (
        this.destroyed ||
        owner !== this.owner() ||
        (!this.data()?.canManageStaffing && !this.data()?.canPrepareBudget)
      )
        return true;
      if (this.busy() || this.uncertain()) return false;
      if (choice === 'discard') {
        this.clearProtectedEditor();
        return true;
      }
      if (choice !== 'save') return false;
      if (
        this.selectedUser ||
        this.selectedLevel !== 'STAFF_ASSOCIATE' ||
        this.reviewed ||
        this.budgetReviewed ||
        this.revokeTarget()
      ) {
        this.commandStatus.set(
          'Budget drafts cannot save staffing or approval choices. Keep editing or explicitly discard those choices before leaving.',
        );
        return false;
      }
      const saved = this.saveEditableBudget(true);
      if (saved) this.cancelBudgetReview();
      return saved;
    } finally {
      this.navigationPending = false;
    }
  }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (
      !this.destroyed &&
      this.owner() &&
      (this.planningDirty() || this.busy() || this.uncertain())
    ) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
  checkboxChecked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  readonly engagementId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private lifetime = 0;
  private readRevision = 0;
  private destroyed = false;
  private owner(): string {
    const s = this.session.current();
    return s?.staff
      ? `${s.firmId}:${s.userId}:${s.generation}:${this.session.invalidation()}:${this.engagementId()}:${this.lifetime}`
      : '';
  }
  readonly data = signal<Planning | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  private request?: Subscription;
  private readonly drafts = inject(TabDrafts);
  private readonly api = inject(Api);
  readonly budgetState!: PlanningBudgetState;
  readonly staffingState!: PlanningStaffingState;

  get draftStatus() { return this.budgetState.draftStatus; }
  get levels() { return this.staffingState.levels; }
  get budgetModel() { return this.budgetState.budgetModel; }
  get budgetFields() { return this.budgetState.budgetFields; }
  get currency(): string { return this.budgetState.currency; }
  set currency(value: string) { this.budgetState.currency = value; }
  get budgetLines(): PlanningEditableBudget['lines'] { return this.budgetState.budgetLines; }
  set budgetLines(value: PlanningEditableBudget['lines']) { this.budgetState.budgetLines = value; }
  get budgetPreview() { return this.budgetState.budgetPreview; }
  get budgetReceipt() { return this.budgetState.budgetReceipt; }
  get budgetPending() { return this.budgetState.budgetPending; }
  get approvalPreview() { return this.budgetState.approvalPreview; }
  get approvalReceipt() { return this.budgetState.approvalReceipt; }
  get approvalPending() { return this.budgetState.approvalPending; }
  get preparationModel() { return this.budgetState.preparationModel; }
  get approvalModel() { return this.budgetState.approvalModel; }
  get approvalConfirmationModel() { return this.budgetState.approvalConfirmationModel; }
  get approvalConfirmationFields() { return this.budgetState.approvalConfirmationFields; }
  get approvalConfirmationReviewed(): boolean { return this.budgetState.approvalConfirmationReviewed; }
  set approvalConfirmationReviewed(value: boolean) { this.budgetState.approvalConfirmationReviewed = value; }
  get preparationFields() { return this.budgetState.preparationFields; }
  get approvalFields() { return this.budgetState.approvalFields; }
  get preparationReviewed(): boolean { return this.budgetState.preparationReviewed; }
  set preparationReviewed(value: boolean) { this.budgetState.preparationReviewed = value; }
  get budgetReviewed(): boolean { return this.budgetState.budgetReviewed; }
  set budgetReviewed(value: boolean) { this.budgetState.budgetReviewed = value; }
  get staffingModel() { return this.staffingState.staffingModel; }
  get staffingFields() { return this.staffingState.staffingFields; }
  get selectedUser(): string { return this.staffingState.selectedUser; }
  set selectedUser(value: string) { this.staffingState.selectedUser = value; }
  get selectedLevel(): string { return this.staffingState.selectedLevel; }
  set selectedLevel(value: string) { this.staffingState.selectedLevel = value; }
  get reviewed(): boolean { return this.staffingState.reviewed; }
  set reviewed(value: boolean) { this.staffingState.reviewed = value; }
  get revokeTarget() { return this.staffingState.revokeTarget; }
  get staffingPreview() { return this.staffingState.staffingPreview; }
  get staffingReceipt() { return this.staffingState.staffingReceipt; }
  get staffingPending() { return this.staffingState.staffingPending; }
  get staffingConfirmationModel() { return this.staffingState.staffingConfirmationModel; }
  get staffingConfirmationFields() { return this.staffingState.staffingConfirmationFields; }
  get staffingConfirmationReviewed(): boolean { return this.staffingState.staffingConfirmationReviewed; }
  set staffingConfirmationReviewed(value: boolean) { this.staffingState.staffingConfirmationReviewed = value; }
  saveEditableBudget(allowReviewed = false): boolean { return this.budgetState.saveEditableBudget(allowReviewed); }
  restoreEditableBudget(): void { this.budgetState.restoreEditableBudget(); }
  discardEditableBudget(): void { this.budgetState.discardEditableBudget(); }
  removeBudgetLine(index: number): void { this.budgetState.removeBudgetLine(index); }
  addBudgetLine(): void { this.budgetState.addBudgetLine(); }
  saveBudget(): Promise<void> { return this.budgetState.saveBudget(); }
  cancelBudgetReview(): void { this.budgetState.cancelBudgetReview(); }
  confirmBudgetPreparation(): Promise<void> { return this.budgetState.confirmBudgetPreparation(); }
  reconcileBudgetPreparation(): Promise<void> { return this.budgetState.reconcileBudgetPreparation(); }
  acknowledgeBudgetPreparation(): void { this.budgetState.acknowledgeBudgetPreparation(); }
  cancelApprovalReview(): void { this.budgetState.cancelApprovalReview(); }
  approveBudget(id: string): Promise<void> { return this.budgetState.approveBudget(id); }
  confirmBudgetApproval(): Promise<void> { return this.budgetState.confirmBudgetApproval(); }
  reconcileBudgetApproval(): Promise<void> { return this.budgetState.reconcileBudgetApproval(); }
  acknowledgeBudgetApproval(): void { this.budgetState.acknowledgeBudgetApproval(); }
  requestRevocation(person: TeamMember): void { this.staffingState.requestRevocation(person); }
  assign(): Promise<void> { return this.staffingState.assign(); }
  revoke(id: string): Promise<void> { return this.staffingState.revoke(id); }
  cancelStaffingReview(): void { this.staffingState.cancelStaffingReview(); }
  confirmStaffingChange(): Promise<void> { return this.staffingState.confirmStaffingChange(); }
  reconcileStaffingChange(): Promise<void> { return this.staffingState.reconcileStaffingChange(); }
  acknowledgeStaffingChange(): void { this.staffingState.acknowledgeStaffingChange(); }
  constructor() {
    let budgetState: PlanningBudgetState;
    let staffingState: PlanningStaffingState;
    const context: PlanningStateContext = {
      engagementId: () => this.engagementId(),
      data: () => this.data(),
      busy: this.busy,
      uncertain: this.uncertain,
      commandStatus: this.commandStatus,
      owner: () => this.owner(),
      readRevision: () => this.readRevision,
      destroyed: () => this.destroyed,
      load: () => this.load(),
      clearProtectedEditor: () => this.clearProtectedEditor(),
      hasBudgetPreparationPreview: () => budgetState?.budgetPreview() != null,
      hasBudgetApprovalPreview: () => budgetState?.approvalPreview() != null,
      hasStaffingPreview: () => staffingState?.staffingPreview() != null,
    };
    budgetState = new PlanningBudgetState(this.api, this.drafts, this.session, context);
    staffingState = new PlanningStaffingState(this.api, this.drafts, this.session, context);
    this.budgetState = budgetState;
    this.staffingState = staffingState;
    effect(() => {
      this.engagementId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.lifetime++;
        this.busy.set(false);
        this.loading.set(false);
        this.request?.unsubscribe();
        this.data.set(null);
        this.budgetState.resetForOwner();
        this.staffingState.resetForOwner();
        this.commandStatus.set('');
        this.draftStatus.set('');
        this.uncertain.set(false);
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.lifetime++;
      this.readRevision++;
      this.request?.unsubscribe();
      this.clearProtectedEditor();
    });
  }
  private clearProtectedEditor(): void {
    this.data.set(null);
    this.budgetState.clearProtectedEditor();
    this.staffingState.clearProtectedEditor();
  }
  load(): void {
    this.staffingPreview.set(null);
    this.staffingConfirmationReviewed = false;
    this.approvalPreview.set(null);
    this.approvalConfirmationReviewed = false;
    this.reviewed = false;
    this.budgetReviewed = false;
    this.preparationReviewed = false;
    this.budgetPreview.set(null);
    const revision = ++this.readRevision;
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    const id = this.engagementId();
    this.loading.set(false);
    if (this.destroyed || !planningGuid.test(id) || !this.session.current()?.staff) return;
    this.loading.set(true);
    const owner = this.owner();
    this.request = this.http
      .get<unknown>('/api/ui/engagements/' + id + '/planning')
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (this.destroyed || owner !== this.owner() || revision !== this.readRevision) return;
          try {
            const planning = decodePlanning(value);
            this.data.set(planning);
            const hasBudgetRequest = this.budgetState.restorePreparationRequest(planning);
            const hasStaffingRequest = this.staffingState.restoreStaffingRequest(planning);
            const hasApprovalRequest = this.budgetState.restoreApprovalRequest(planning);
            this.uncertain.set(hasBudgetRequest || hasStaffingRequest || hasApprovalRequest);
          } catch {
            this.clearProtectedEditor();
            this.error.set('Planning returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (this.destroyed || owner !== this.owner() || revision !== this.readRevision) return;
          this.loading.set(false);
          this.clearProtectedEditor();
          this.error.set('Planning unavailable. Check your access or retry.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
