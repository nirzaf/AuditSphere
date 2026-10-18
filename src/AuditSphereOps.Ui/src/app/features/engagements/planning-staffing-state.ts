import { form, required, disabled, pattern, validate } from '@angular/forms/signals';
import { effect, signal, untracked } from '@angular/core';
import { Api } from '../../core/api';
import { PendingRequestReference, TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { SessionService } from '../../core/session';
import { StaffingChangeFields, StaffingChangePreview, StaffingChangeReceipt, decodeStaffingChangePreview, decodeStaffingChangeReceipt, decodeStaffingChangeLookup } from './staffing-change-contracts';
import { Planning, TeamMember, planningGuid } from './planning-contracts';
import type { PlanningStateContext } from './planning-state-context';

export class PlanningStaffingState {
  constructor(
    private readonly api: Api,
    private readonly drafts: TabDrafts,
    private readonly session: SessionService,
    private readonly context: PlanningStateContext,
  ) {
    effect(() => {
      const staffing =
        this.staffingModel().reviewed &&
        (!this.staffingIntent() || this.staffingAssent() !== this.staffingIntent());
      const confirmation =
        this.staffingConfirmationModel().reviewed && !this.staffingConfirmationReviewed;
      untracked(() => {
        if (confirmation) {
          this.staffingConfirmationReviewed = false;
          this.staffingConfirmationFields.reviewed().reset(false);
        }
        if (staffing) {
          this.reviewed = false;
          this.staffingFields.reviewed().reset(false);
        }
      });
    });
  }

  resetForOwner(): void {
    this.staffingModel.set({ userId: '', level: 'STAFF_ASSOCIATE', reviewed: false });
    this.staffingAssent.set('');
    this.revokeTarget.set(null);
    this.staffingConfirmationReviewed = false;
    this.staffingPreview.set(null);
    this.staffingReceipt.set(null);
    this.staffingPending.set(null);
  }

  clearProtectedEditor(): void {
    this.staffingModel.set({ userId: '', level: 'STAFF_ASSOCIATE', reviewed: false });
    this.staffingAssent.set('');
    this.revokeTarget.set(null);
    this.staffingConfirmationReviewed = false;
    this.staffingPreview.set(null);
    this.staffingReceipt.set(null);
    this.staffingPending.set(null);
  }

  restoreStaffingRequest(planning: Planning): boolean {
    if (!planning.canManageStaffing) {
      this.staffingPreview.set(null);
      this.staffingReceipt.set(null);
      this.staffingPending.set(null);
      this.staffingConfirmationReviewed = false;
      return false;
    }
    const pending = this.drafts.readPendingRequest(this.pendingStaffingScope());
    if (pending.state === 'ready') {
      this.staffingPending.set(pending.draft.value);
      this.context.commandStatus.set(
        'A prior staffing request needs reconciliation. Verify its retained receipt before another change.',
      );
    }
    return this.staffingPending() !== null;
  }
  readonly levels = [
    { value: 'ENGAGEMENT_PARTNER', label: 'Engagement Partner' },
    { value: 'AUDIT_MANAGER', label: 'Audit Manager' },
    { value: 'SENIOR_AUDITOR', label: 'Senior Auditor' },
    { value: 'STAFF_ASSOCIATE', label: 'Staff Associate' },
  ];
  readonly staffingModel = signal({ userId: '', level: 'STAFF_ASSOCIATE', reviewed: false });
  private readonly staffingAssent = signal('');
  readonly staffingFields = form(this.staffingModel, (p) => {
    required(p.userId);
    pattern(p.userId, planningGuid);
    validate(p.level, ({ value }) =>
      this.levels.some((l) => l.value === value()) ? undefined : { kind: 'staffingLevel' },
    );
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.context.busy() ||
        this.context.uncertain() ||
        !!this.context.hasBudgetPreparationPreview() ||
        !!this.context.hasBudgetApprovalPreview() ||
        !!this.staffingPreview() ||
        !this.context.data()?.canManageStaffing,
    );
  });
  private staffingIntent(): string {
    return this.context.owner() && planningGuid.test(this.selectedUser)
      ? JSON.stringify([this.context.owner(), this.context.readRevision(), this.selectedUser, this.selectedLevel])
      : '';
  }
  get selectedUser(): string {
    return this.staffingModel().userId;
  }
  set selectedUser(value: string) {
    this.staffingModel.update((m) => ({ ...m, userId: value, reviewed: false }));
    this.staffingAssent.set('');
  }
  get selectedLevel(): string {
    return this.staffingModel().level;
  }
  set selectedLevel(value: string) {
    this.staffingModel.update((m) => ({ ...m, level: value, reviewed: false }));
    this.staffingAssent.set('');
  }
  get reviewed(): boolean {
    const intent = this.staffingIntent();
    return !!intent && this.staffingModel().reviewed && this.staffingAssent() === intent;
  }
  set reviewed(value: boolean) {
    this.staffingModel.update((m) => ({ ...m, reviewed: value }));
    this.staffingAssent.set(value ? this.staffingIntent() : '');
  }
  readonly revokeTarget = signal<TeamMember | null>(null);
  requestRevocation(person: TeamMember): void {
    if (
      !this.context.busy() &&
      !this.context.uncertain() &&
      !(this.context.hasBudgetPreparationPreview() || this.context.hasBudgetApprovalPreview() || this.staffingPreview())
    )
      this.revokeTarget.set(person);
  }
  async assign(): Promise<void> {
    if (
      !this.context.data()?.canManageStaffing ||
      !this.reviewed ||
      this.staffingFields().invalid() ||
      this.context.busy() ||
      this.context.uncertain() ||
      this.context.hasBudgetPreparationPreview() ||
      this.context.hasBudgetApprovalPreview() ||
      this.staffingPreview()
    )
      return;
    await this.previewStaffingChange({
      action: 'ASSIGN',
      userId: this.selectedUser,
      level: this.selectedLevel,
      assignmentId: null,
    });
  }
  async revoke(id: string): Promise<void> {
    const person = this.revokeTarget();
    const level = this.levels.find((x) => x.label === person?.levelLabel)?.value;
    if (
      !person ||
      person.assignmentId !== id ||
      !level ||
      !this.context.data()?.canManageStaffing ||
      this.context.busy() ||
      this.context.uncertain() ||
      this.context.hasBudgetPreparationPreview() ||
      this.context.hasBudgetApprovalPreview() ||
      this.staffingPreview()
    )
      return;
    await this.previewStaffingChange({
      action: 'REVOKE',
      userId: person.userId,
      level,
      assignmentId: id,
    });
  }
  readonly staffingPreview = signal<StaffingChangePreview | null>(null);
  readonly staffingReceipt = signal<StaffingChangeReceipt | null>(null);
  readonly staffingPending = signal<PendingRequestReference | null>(null);
  private pendingStaffingScope(basis = '0'.repeat(64)) {
    return { entity: 'engagement-staffing-request/' + this.context.engagementId(), baseRevision: basis };
  }
  readonly staffingConfirmationModel = signal({ reviewed: false });
  private readonly staffingConfirmationAssent = signal('');
  readonly staffingConfirmationFields = form(this.staffingConfirmationModel, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.context.busy() ||
        this.context.uncertain() ||
        !this.staffingPreview() ||
        !this.context.data()?.canManageStaffing,
    );
  });
  private staffingConfirmationIntent(): string {
    return this.context.owner() && this.staffingPreview() && this.context.data()?.canManageStaffing
      ? JSON.stringify([this.context.owner(), this.context.readRevision(), this.staffingPreview(), this.context.data()?.team])
      : '';
  }
  get staffingConfirmationReviewed(): boolean {
    const intent = this.staffingConfirmationIntent();
    return (
      !!intent &&
      this.staffingConfirmationModel().reviewed &&
      this.staffingConfirmationAssent() === intent
    );
  }
  set staffingConfirmationReviewed(value: boolean) {
    this.staffingConfirmationModel.set({ reviewed: value });
    this.staffingConfirmationAssent.set(value ? this.staffingConfirmationIntent() : '');
  }
  cancelStaffingReview(): void {
    if (this.context.busy() || this.context.uncertain()) return;
    this.staffingPreview.set(null);
    this.staffingConfirmationReviewed = false;
  }
  private async previewStaffingChange(fields: StaffingChangeFields): Promise<void> {
    const owner = this.context.owner(),
      requestId = crypto.randomUUID(),
      captured = JSON.stringify(this.context.data()?.team);
    this.context.busy.set(true);
    this.reviewed = false;
    this.staffingConfirmationReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.context.engagementId() + '/staffing-change/preview',
      { requestId, fields },
    );
    if (this.context.destroyed() || owner !== this.context.owner()) return;
    this.context.busy.set(false);
    if (!result.ok) {
      this.context.commandStatus.set(result.message);
      if (result.status === 401 || result.status === 403) {
        this.context.clearProtectedEditor();
        this.context.load();
      }
      return;
    }
    try {
      const p = decodeStaffingChangePreview(result.value);
      if (
        p.engagementId !== this.context.engagementId() ||
        p.requestId !== requestId ||
        JSON.stringify(p.fields) !== JSON.stringify(fields) ||
        captured !== JSON.stringify(this.context.data()?.team)
      )
        throw new Error('Changed staffing context');
      this.staffingPreview.set(p);
      this.revokeTarget.set(null);
      this.context.commandStatus.set(
        'Review the exact staffing change and explicitly confirm. No change has been submitted.',
      );
    } catch {
      this.context.commandStatus.set(
        'Staffing review changed or returned an unsupported response. Refresh before reviewing again.',
      );
    }
  }
  async confirmStaffingChange(): Promise<void> {
    const p = this.staffingPreview(),
      owner = this.context.owner();
    if (
      !p ||
      !this.context.data()?.canManageStaffing ||
      !this.staffingConfirmationReviewed ||
      this.context.busy() ||
      this.context.uncertain()
    )
      return;
    const reference = { requestId: p.requestId, requestHash: p.requestHash };
    if (
      !this.drafts.save(
        this.pendingStaffingScope(p.reviewBasis),
        reference,
        pendingRequestReference,
        true,
      )
    ) {
      this.context.commandStatus.set('Recovery storage is unavailable. No staffing command was sent.');
      return;
    }
    this.staffingPending.set(reference);
    this.context.busy.set(true);
    this.staffingConfirmationReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.context.engagementId() + '/staffing-change',
      {
        requestId: p.requestId,
        fields: p.fields,
        requestHash: p.requestHash,
        reviewBasis: p.reviewBasis,
        reviewed: true,
      },
    );
    if (this.context.destroyed() || owner !== this.context.owner()) return;
    this.context.busy.set(false);
    this.staffingPreview.set(null);
    if (!result.ok) {
      this.context.commandStatus.set(result.message);
      if (result.unknown) this.context.uncertain.set(true);
      else {
        this.drafts.clear(this.pendingStaffingScope().entity);
        this.staffingPending.set(null);
        if (result.status === 401 || result.status === 403) {
          this.context.clearProtectedEditor();
          this.context.load();
        }
      }
      return;
    }
    try {
      const receipt = decodeStaffingChangeReceipt(result.value);
      if (
        !this.matchesStaffingReceipt(receipt, reference) ||
        JSON.stringify(receipt.preview) !== JSON.stringify(p)
      )
        throw new Error('Wrong receipt');
      this.staffingReceipt.set(receipt);
      this.context.uncertain.set(true);
      this.context.commandStatus.set(
        'Staffing change recorded with an immutable receipt. Acknowledge before continuing.',
      );
    } catch {
      this.context.uncertain.set(true);
      this.context.commandStatus.set(
        'Outcome unconfirmed. Verify the retained staffing receipt before another change.',
      );
    }
  }
  private matchesStaffingReceipt(r: StaffingChangeReceipt, p: PendingRequestReference): boolean {
    return (
      r.engagementId === this.context.engagementId() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async reconcileStaffingChange(): Promise<void> {
    const p = this.staffingPending(),
      owner = this.context.owner();
    if (!p || !this.context.data()?.canManageStaffing || this.context.busy()) return;
    this.context.busy.set(true);
    this.staffingPreview.set(null);
    this.staffingConfirmationReviewed = false;
    try {
      const lookup = await this.api.get(
        '/api/ui/engagements/' +
          this.context.engagementId() +
          '/staffing-change/receipts/' +
          p.requestId +
          '?requestHash=' +
          p.requestHash,
        decodeStaffingChangeLookup,
      );
      if (this.context.destroyed() || owner !== this.context.owner()) return;
      if (lookup.found && lookup.receipt && this.matchesStaffingReceipt(lookup.receipt, p)) {
        this.staffingReceipt.set(lookup.receipt);
        this.context.uncertain.set(true);
        this.context.commandStatus.set(
          'Retained receipt confirms the staffing change. Acknowledge before continuing.',
        );
      } else if (!lookup.found) {
        this.drafts.clear(this.pendingStaffingScope().entity);
        this.staffingPending.set(null);
        this.context.uncertain.set(false);
        this.context.commandStatus.set(
          'No committed staffing change was found. Refresh and explicitly review before a new request.',
        );
        this.context.load();
      } else throw new Error('Wrong receipt');
    } catch {
      if (!this.context.destroyed() && owner === this.context.owner()) {
        this.context.clearProtectedEditor();
        this.context.load();
        this.context.commandStatus.set(
          'Receipt unavailable. Revalidate access before reconciling again. No command was retried.',
        );
      }
    } finally {
      if (!this.context.destroyed() && owner === this.context.owner()) this.context.busy.set(false);
    }
  }
  acknowledgeStaffingChange(): void {
    if (!this.staffingReceipt() || this.context.busy()) return;
    if (!this.drafts.clear(this.pendingStaffingScope().entity)) {
      this.context.commandStatus.set('Recovery storage could not be cleared. Retry before continuing.');
      return;
    }
    this.staffingPending.set(null);
    this.staffingReceipt.set(null);
    this.context.uncertain.set(false);
    this.selectedUser = '';
    this.reviewed = false;
    this.revokeTarget.set(null);
    this.context.commandStatus.set('Staffing change acknowledged.');
    this.context.load();
  }
}
