import { form, required, maxLength, applyEach, disabled, min, max, pattern, validate } from '@angular/forms/signals';
import { effect, signal, untracked } from '@angular/core';
import { Api } from '../../core/api';
import { PendingRequestReference, TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { SessionService } from '../../core/session';
import { BudgetApprovalPreview, BudgetApprovalReceipt, decodeBudgetApprovalPreview, decodeBudgetApprovalReceipt, decodeBudgetApprovalLookup } from './budget-approval-contracts';
import { BudgetPreparationPreview, BudgetPreparationReceipt, decodeBudgetPreparationPreview, decodeBudgetPreparationReceipt, decodeBudgetPreparationLookup } from './budget-preparation-contracts';
import { Planning, PlanningEditableBudget, editablePlanningBudget, planningGuid } from './planning-contracts';
import type { PlanningStateContext } from './planning-state-context';

export class PlanningBudgetState {
  constructor(
    private readonly api: Api,
    private readonly drafts: TabDrafts,
    private readonly session: SessionService,
    private readonly context: PlanningStateContext,
  ) {
    effect(() => {
      const preparation =
        this.preparationModel().reviewed &&
        (!this.preparationIntent() || this.preparationAssent() !== this.preparationIntent());
      const approval =
        this.approvalModel().reviewed &&
        (!this.approvalIntent() || this.approvalAssent() !== this.approvalIntent());
      const confirmation =
        this.approvalConfirmationModel().reviewed && !this.approvalConfirmationReviewed;
      untracked(() => {
        if (confirmation) {
          this.approvalConfirmationReviewed = false;
          this.approvalConfirmationFields.reviewed().reset(false);
        }
        if (preparation) {
          this.preparationReviewed = false;
          this.preparationFields.reviewed().reset(false);
        }
        if (approval) {
          this.budgetReviewed = false;
          this.approvalFields.reviewed().reset(false);
        }
      });
    });
  }

  hasUnsavedEdits(): boolean {
    return JSON.stringify(this.budgetModel()) !== this.budgetBaseline();
  }

  resetForOwner(): void {
    this.currency = 'QAR';
    this.budgetLines = [
      { role: 'Senior', activity: 'AUDIT', phase: 'PLANNING', riskArea: '', forecastMinutes: 480 },
    ];
    this.budgetBaseline.set(JSON.stringify(this.budgetModel()));
    this.preparationReviewed = false;
    this.budgetReviewed = false;
    this.approvalConfirmationReviewed = false;
    this.draftStatus.set('');
    this.budgetPreview.set(null);
    this.budgetReceipt.set(null);
    this.budgetPending.set(null);
    this.approvalPreview.set(null);
    this.approvalReceipt.set(null);
    this.approvalPending.set(null);
  }

  clearProtectedEditor(): void {
    this.draftStatus.set('');
    this.budgetPreview.set(null);
    this.budgetReceipt.set(null);
    this.budgetPending.set(null);
    this.approvalPreview.set(null);
    this.approvalReceipt.set(null);
    this.approvalPending.set(null);
    this.preparationReviewed = false;
    this.budgetReviewed = false;
    this.approvalConfirmationReviewed = false;
    this.budgetLines = [];
    this.currency = '';
    this.budgetBaseline.set(JSON.stringify(this.budgetModel()));
  }

  restorePreparationRequest(planning: Planning): boolean {
    if (!planning.canPrepareBudget) {
      this.budgetPending.set(null);
      this.budgetReceipt.set(null);
      this.budgetPreview.set(null);
      return false;
    }
    const pending = this.drafts.readPendingRequest(this.pendingBudgetScope());
    if (pending.state === 'ready') {
      this.budgetPending.set(pending.draft.value);
      this.context.commandStatus.set(
        'A prior budget request needs reconciliation. Verify its retained receipt before another change.',
      );
    }
    return this.budgetPending() !== null;
  }

  restoreApprovalRequest(planning: Planning): boolean {
    if (!planning.canManageStaffing) {
      this.approvalPending.set(null);
      this.approvalReceipt.set(null);
      this.approvalPreview.set(null);
      return false;
    }
    const pending = this.drafts.readPendingRequest(this.pendingApprovalScope());
    if (pending.state === 'ready') {
      this.approvalPending.set(pending.draft.value);
      this.context.commandStatus.set(
        'A prior approval needs reconciliation. Verify its retained receipt before another change.',
      );
    }
    return this.approvalPending() !== null;
  }
  readonly draftStatus = signal('');
  private budgetDraftScope(allowReviewed = false) {
    const planning = this.context.data();
    if (
      !planning?.canPrepareBudget ||
      this.context.busy() ||
      this.context.uncertain() ||
      (!allowReviewed && this.budgetPreview()) ||
      !!this.approvalPreview() ||
      !!this.context.hasStaffingPreview() ||
      this.context.destroyed() ||
      !this.context.owner()
    )
      return null;
    // The entity and session namespace bind identity; revision binds budget version and draft/approval state.
    return {
      entity: 'engagement-budget:' + this.context.engagementId(),
      baseRevision:
        BigInt(planning.latestBudgetVersion).toString(16).padStart(60, '0') +
        (planning.draft ? '0001' : '0000'),
    };
  }
  saveEditableBudget(allowReviewed = false): boolean {
    const scope = this.budgetDraftScope(allowReviewed);
    if (!scope) return false;
    const saved = this.drafts.save(
      scope,
      { currency: this.currency, lines: this.budgetLines },
      editablePlanningBudget,
    );
    this.draftStatus.set(
      saved
        ? 'Editable budget fields saved in this tab. Review is not saved.'
        : 'Budget fields could not be saved. Check bounded fields and tab storage.',
    );
    if (saved) this.budgetBaseline.set(JSON.stringify(this.budgetModel()));
    return saved;
  }
  restoreEditableBudget(): void {
    const scope = this.budgetDraftScope();
    if (!scope) return;
    const result = this.drafts.read(scope, editablePlanningBudget);
    this.budgetReviewed = false;
    if (result.state === 'ready' && !result.draft.submissionPending) {
      this.currency = result.draft.value.currency;
      this.budgetLines = result.draft.value.lines.map((line) => ({ ...line }));
      this.draftStatus.set(
        'Editable fields restored. Review current rates and explicitly approve separately.',
      );
    } else {
      this.draftStatus.set(
        result.state === 'stale'
          ? 'Saved budget fields are stale. They were not restored.'
          : 'No usable saved budget fields are available.',
      );
    }
  }
  discardEditableBudget(): void {
    const scope = this.budgetDraftScope();
    if (!scope) return;
    this.draftStatus.set(
      this.drafts.clear(scope.entity)
        ? 'Saved budget fields discarded.'
        : 'Saved fields could not be discarded from tab storage.',
    );
  }
  readonly budgetModel = signal<PlanningEditableBudget>({
    currency: 'QAR',
    lines: [
      { role: 'Senior', activity: 'AUDIT', phase: 'PLANNING', riskArea: '', forecastMinutes: 480 },
    ],
  });
  private readonly budgetBaseline = signal(JSON.stringify(this.budgetModel()));
  readonly budgetFields = form(this.budgetModel, (p) => {
    required(p.currency);
    maxLength(p.currency, 3);
    pattern(p.currency, /^[A-Za-z]{3}$/);
    disabled(
      p,
      () =>
        this.context.busy() ||
        this.context.uncertain() ||
        !!this.budgetPreview() ||
        !!this.approvalPreview() ||
        !!this.context.hasStaffingPreview() ||
        !this.context.data()?.canPrepareBudget,
    );
    applyEach(p.lines, (l) => {
      required(l.role);
      maxLength(l.role, 50);
      required(l.activity);
      maxLength(l.activity, 50);
      required(l.phase);
      maxLength(l.phase, 50);
      maxLength(l.riskArea, 120);
      required(l.forecastMinutes);
      min(l.forecastMinutes, 1);
      max(l.forecastMinutes, 10000000);
      validate(l.forecastMinutes, ({ value }) =>
        Number.isInteger(value())
          ? undefined
          : { kind: 'wholeMinutes', message: 'Enter whole forecast minutes.' },
      );
    });
  });
  get currency(): string {
    return this.budgetModel().currency;
  }
  set currency(value: string) {
    this.budgetModel.update((m) => ({ ...m, currency: value }));
  }
  get budgetLines(): PlanningEditableBudget['lines'] {
    return this.budgetModel().lines;
  }
  set budgetLines(value: PlanningEditableBudget['lines']) {
    this.budgetModel.update((m) => ({ ...m, lines: value.map((l) => ({ ...l })) }));
  }
  removeBudgetLine(index: number): void {
    if (
      !this.context.data()?.canPrepareBudget ||
      this.context.busy() ||
      this.context.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
      this.context.hasStaffingPreview()
    )
      return;
    this.budgetLines = this.budgetLines.filter((_, i) => i !== index);
  }
  addBudgetLine(): void {
    if (
      !this.context.data()?.canPrepareBudget ||
      this.context.busy() ||
      this.context.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
      this.context.hasStaffingPreview() ||
      this.budgetLines.length >= 200
    )
      return;
    this.budgetLines = [
      ...this.budgetLines,
      {
        role: 'Senior',
        activity: 'AUDIT',
        phase: 'PLANNING',
        riskArea: '',
        forecastMinutes: 480,
      },
    ];
  }
  readonly budgetPreview = signal<BudgetPreparationPreview | null>(null);
  readonly budgetReceipt = signal<BudgetPreparationReceipt | null>(null);
  readonly budgetPending = signal<PendingRequestReference | null>(null);
  private pendingBudgetScope(basis = '0'.repeat(64)) {
    return { entity: 'engagement-budget-request/' + this.context.engagementId(), baseRevision: basis };
  }
  async saveBudget(): Promise<void> {
    const planning = this.context.data(),
      owner = this.context.owner();
    if (
      !planning?.canPrepareBudget ||
      this.context.busy() ||
      this.context.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
      this.context.hasStaffingPreview()
    )
      return;
    const editable = editablePlanningBudget({
      currency: this.currency.trim().toUpperCase(),
      lines: this.budgetLines,
    });
    if (!editable) {
      this.context.commandStatus.set('Enter bounded budget fields and whole forecast minutes.');
      return;
    }
    const fields = {
      currency: editable.currency,
      expectedVersion: planning.latestBudgetVersion,
      lines: editable.lines.map((l) => ({
        role: l.role,
        activity: l.activity,
        forecastMinutes: l.forecastMinutes,
        phase: l.phase.toUpperCase(),
        riskArea: l.riskArea.trim() || null,
      })),
    };
    const requestId = crypto.randomUUID();
    this.context.busy.set(true);
    this.preparationReviewed = false;
    this.budgetReceipt.set(null);
    const result = await this.api.command(
      '/api/ui/engagements/' + this.context.engagementId() + '/budget-preparation/preview',
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
      const preview = decodeBudgetPreparationPreview(result.value);
      if (
        preview.engagementId !== this.context.engagementId() ||
        preview.requestId !== requestId ||
        this.context.data()?.latestBudgetVersion !== fields.expectedVersion ||
        JSON.stringify(preview.fields) !== JSON.stringify(fields)
      )
        throw new Error('Changed review');
      this.budgetPreview.set(preview);
      this.context.commandStatus.set(
        'Review the exact approved rates and forecast. This prepares a draft; it does not approve it.',
      );
    } catch {
      this.context.commandStatus.set(
        'Budget review returned an unsupported or changed response. Refresh before reviewing again.',
      );
    }
  }
  cancelBudgetReview(): void {
    if (this.context.busy() || this.context.uncertain()) return;
    this.budgetPreview.set(null);
    this.preparationReviewed = false;
  }
  async confirmBudgetPreparation(): Promise<void> {
    const p = this.budgetPreview(),
      owner = this.context.owner();
    if (
      !p ||
      !this.context.data()?.canPrepareBudget ||
      !this.preparationReviewed ||
      this.context.busy() ||
      this.context.uncertain()
    )
      return;
    const reference = { requestId: p.requestId, requestHash: p.requestHash };
    if (
      !this.drafts.save(
        this.pendingBudgetScope(p.reviewBasis),
        reference,
        pendingRequestReference,
        true,
      )
    ) {
      this.context.commandStatus.set('Recovery storage is unavailable. No budget command was sent.');
      return;
    }
    this.budgetPending.set(reference);
    this.context.busy.set(true);
    this.preparationReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.context.engagementId() + '/budget-preparation',
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
    this.budgetPreview.set(null);
    if (!result.ok) {
      this.context.commandStatus.set(result.message);
      if (result.unknown) this.context.uncertain.set(true);
      else {
        this.drafts.clear(this.pendingBudgetScope().entity);
        this.budgetPending.set(null);
        if (result.status === 401 || result.status === 403) {
          this.context.clearProtectedEditor();
          this.context.load();
        }
      }
      return;
    }
    try {
      const receipt = decodeBudgetPreparationReceipt(result.value);
      if (
        !this.matchesBudgetReceipt(receipt, reference) ||
        JSON.stringify(receipt.preview) !== JSON.stringify(p)
      )
        throw new Error('Wrong receipt');
      this.budgetReceipt.set(receipt);
      this.context.uncertain.set(true);
      this.context.commandStatus.set(
        'Draft budget recorded with an immutable receipt. Acknowledge the result before continuing.',
      );
    } catch {
      this.context.uncertain.set(true);
      this.context.commandStatus.set(
        'Outcome unconfirmed. Verify the retained budget receipt before another change.',
      );
    }
  }
  private matchesBudgetReceipt(r: BudgetPreparationReceipt, p: PendingRequestReference): boolean {
    return (
      r.engagementId === this.context.engagementId() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async reconcileBudgetPreparation(): Promise<void> {
    const p = this.budgetPending(),
      owner = this.context.owner();
    if (!p || !this.context.data()?.canPrepareBudget || this.context.busy()) return;
    this.context.busy.set(true);
    this.budgetPreview.set(null);
    this.preparationReviewed = false;
    try {
      const lookup = await this.api.get(
        '/api/ui/engagements/' +
          this.context.engagementId() +
          '/budget-preparation/receipts/' +
          p.requestId +
          '?requestHash=' +
          p.requestHash,
        decodeBudgetPreparationLookup,
      );
      if (this.context.destroyed() || owner !== this.context.owner()) return;
      if (lookup.found && lookup.receipt && this.matchesBudgetReceipt(lookup.receipt, p)) {
        this.budgetReceipt.set(lookup.receipt);
        this.context.uncertain.set(true);
        this.context.commandStatus.set(
          'Retained receipt confirms the draft budget. Acknowledge the result before continuing.',
        );
      } else if (!lookup.found) {
        this.drafts.clear(this.pendingBudgetScope().entity);
        this.budgetPending.set(null);
        this.context.uncertain.set(false);
        this.context.commandStatus.set(
          'No committed preparation was found after reconciliation. Refresh and explicitly review current fields before a new request.',
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
  acknowledgeBudgetPreparation(): void {
    if (!this.budgetReceipt() || this.context.busy()) return;
    if (!this.drafts.clear(this.pendingBudgetScope().entity)) {
      this.context.commandStatus.set(
        'Receipt acknowledged, but recovery storage could not be cleared. Retry clearing before continuing.',
      );
      return;
    }
    this.budgetPending.set(null);
    this.budgetReceipt.set(null);
    this.context.uncertain.set(false);
    this.context.commandStatus.set(
      'Budget preparation acknowledged. Approval remains a separate reviewed action.',
    );
    this.drafts.clear('engagement-budget:' + this.context.engagementId());
    this.context.load();
  }
  readonly approvalPreview = signal<BudgetApprovalPreview | null>(null);
  readonly approvalReceipt = signal<BudgetApprovalReceipt | null>(null);
  readonly approvalPending = signal<PendingRequestReference | null>(null);
  private pendingApprovalScope(basis = '0'.repeat(64)) {
    return {
      entity: 'engagement-budget-approval-request/' + this.context.engagementId(),
      baseRevision: basis,
    };
  }
  cancelApprovalReview(): void {
    if (this.context.busy() || this.context.uncertain()) return;
    this.approvalPreview.set(null);
    this.approvalConfirmationReviewed = false;
  }
  async approveBudget(id: string): Promise<void> {
    const draft = this.context.data()?.draft,
      owner = this.context.owner(),
      captured = JSON.stringify(draft);
    if (
      !draft?.canApprove ||
      draft.id !== id ||
      !this.budgetReviewed ||
      this.context.busy() ||
      this.context.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
      this.context.hasStaffingPreview()
    )
      return;
    const requestId = crypto.randomUUID();
    this.context.busy.set(true);
    this.budgetReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.context.engagementId() + '/budget-approval/preview',
      { requestId, fields: { budgetId: id, expectedVersion: draft.version } },
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
      const preview = decodeBudgetApprovalPreview(result.value);
      if (
        captured !== JSON.stringify(this.context.data()?.draft) ||
        preview.engagementId !== this.context.engagementId() ||
        preview.requestId !== requestId ||
        preview.fields.budgetId !== id ||
        preview.fields.expectedVersion !== draft.version ||
        preview.currency !== draft.currency ||
        JSON.stringify(preview.lines) !== JSON.stringify(draft.lines)
      )
        throw new Error('Changed approval review');
      this.approvalPreview.set(preview);
      this.context.commandStatus.set(
        'Review the exact draft and explicitly confirm independent approval. No approval has been submitted.',
      );
    } catch {
      this.context.commandStatus.set(
        'Approval review changed or returned an unsupported response. Refresh before reviewing again.',
      );
    }
  }
  async confirmBudgetApproval(): Promise<void> {
    const p = this.approvalPreview(),
      owner = this.context.owner();
    if (
      !p ||
      !this.context.data()?.canManageStaffing ||
      !this.approvalConfirmationReviewed ||
      this.context.busy() ||
      this.context.uncertain()
    )
      return;
    const reference = { requestId: p.requestId, requestHash: p.requestHash };
    if (
      !this.drafts.save(
        this.pendingApprovalScope(p.reviewBasis),
        reference,
        pendingRequestReference,
        true,
      )
    ) {
      this.context.commandStatus.set('Recovery storage is unavailable. No budget command was sent.');
      return;
    }
    this.approvalPending.set(reference);
    this.context.busy.set(true);
    this.approvalConfirmationReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.context.engagementId() + '/budget-approval',
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
    this.approvalPreview.set(null);
    if (!result.ok) {
      this.context.commandStatus.set(result.message);
      if (result.unknown) this.context.uncertain.set(true);
      else {
        this.drafts.clear(this.pendingApprovalScope().entity);
        this.approvalPending.set(null);
        if (result.status === 401 || result.status === 403) {
          this.context.clearProtectedEditor();
          this.context.load();
        }
      }
      return;
    }
    try {
      const receipt = decodeBudgetApprovalReceipt(result.value);
      if (
        !this.matchesApprovalReceipt(receipt, reference) ||
        JSON.stringify(receipt.preview) !== JSON.stringify(p)
      )
        throw new Error('Wrong receipt');
      this.approvalReceipt.set(receipt);
      this.context.uncertain.set(true);
      this.context.commandStatus.set(
        'Budget approval recorded with an immutable receipt. Acknowledge the result before continuing.',
      );
    } catch {
      this.context.uncertain.set(true);
      this.context.commandStatus.set(
        'Outcome unconfirmed. Verify the retained budget receipt before another change.',
      );
    }
  }
  private matchesApprovalReceipt(r: BudgetApprovalReceipt, p: PendingRequestReference): boolean {
    return (
      r.engagementId === this.context.engagementId() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async reconcileBudgetApproval(): Promise<void> {
    const p = this.approvalPending(),
      owner = this.context.owner();
    if (!p || !this.context.data()?.canManageStaffing || this.context.busy()) return;
    this.context.busy.set(true);
    this.approvalPreview.set(null);
    this.approvalConfirmationReviewed = false;
    try {
      const lookup = await this.api.get(
        '/api/ui/engagements/' +
          this.context.engagementId() +
          '/budget-approval/receipts/' +
          p.requestId +
          '?requestHash=' +
          p.requestHash,
        decodeBudgetApprovalLookup,
      );
      if (this.context.destroyed() || owner !== this.context.owner()) return;
      if (lookup.found && lookup.receipt && this.matchesApprovalReceipt(lookup.receipt, p)) {
        this.approvalReceipt.set(lookup.receipt);
        this.context.uncertain.set(true);
        this.context.commandStatus.set(
          'Retained receipt confirms the budget approval. Acknowledge the result before continuing.',
        );
      } else if (!lookup.found) {
        this.drafts.clear(this.pendingApprovalScope().entity);
        this.approvalPending.set(null);
        this.context.uncertain.set(false);
        this.context.commandStatus.set(
          'No committed approval was found after reconciliation. Refresh and explicitly review current fields before a new request.',
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
  acknowledgeBudgetApproval(): void {
    if (!this.approvalReceipt() || this.context.busy()) return;
    if (!this.drafts.clear(this.pendingApprovalScope().entity)) {
      this.context.commandStatus.set(
        'Receipt acknowledged, but recovery storage could not be cleared. Retry clearing before continuing.',
      );
      return;
    }
    this.approvalPending.set(null);
    this.approvalReceipt.set(null);
    this.context.uncertain.set(false);
    this.context.commandStatus.set('Budget approval acknowledged. Approval has been recorded.');
    this.drafts.clear('engagement-budget:' + this.context.engagementId());
    this.context.load();
  }
  readonly preparationModel = signal({ reviewed: false });
  readonly approvalModel = signal({ reviewed: false });
  readonly approvalConfirmationModel = signal({ reviewed: false });
  private readonly approvalConfirmationAssent = signal('');
  readonly approvalConfirmationFields = form(this.approvalConfirmationModel, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.context.busy() ||
        this.context.uncertain() ||
        !this.approvalPreview() ||
        !this.context.data()?.draft?.canApprove,
    );
  });
  private approvalConfirmationIntent(): string {
    return this.context.owner() && this.approvalPreview() && this.context.data()?.draft?.canApprove
      ? JSON.stringify([
          this.context.owner(),
          this.context.readRevision(),
          this.approvalPreview(),
          this.context.data()?.draft,
        ])
      : '';
  }
  get approvalConfirmationReviewed(): boolean {
    const intent = this.approvalConfirmationIntent();
    return (
      !!intent &&
      this.approvalConfirmationModel().reviewed &&
      this.approvalConfirmationAssent() === intent
    );
  }
  set approvalConfirmationReviewed(value: boolean) {
    this.approvalConfirmationModel.set({ reviewed: value });
    this.approvalConfirmationAssent.set(value ? this.approvalConfirmationIntent() : '');
  }
  private readonly preparationAssent = signal('');
  private readonly approvalAssent = signal('');
  readonly preparationFields = form(this.preparationModel, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.context.busy() ||
        this.context.uncertain() ||
        !this.budgetPreview() ||
        !!this.approvalPreview() ||
        !!this.context.hasStaffingPreview() ||
        !this.context.data()?.canPrepareBudget,
    );
  });
  readonly approvalFields = form(this.approvalModel, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.context.busy() ||
        this.context.uncertain() ||
        !!this.budgetPreview() ||
        !!this.approvalPreview() ||
        !!this.context.hasStaffingPreview() ||
        !this.context.data()?.draft?.canApprove,
    );
  });
  private preparationIntent(): string {
    return this.context.owner() && this.budgetPreview()
      ? JSON.stringify([this.context.owner(), this.context.readRevision(), this.budgetPreview()])
      : '';
  }
  private approvalIntent(): string {
    return this.context.owner() && this.context.data()?.draft?.canApprove
      ? JSON.stringify([this.context.owner(), this.context.readRevision(), this.context.data()?.draft])
      : '';
  }
  get preparationReviewed(): boolean {
    const intent = this.preparationIntent();
    return !!intent && this.preparationModel().reviewed && this.preparationAssent() === intent;
  }
  set preparationReviewed(value: boolean) {
    this.preparationModel.set({ reviewed: value });
    this.preparationAssent.set(value ? this.preparationIntent() : '');
  }
  get budgetReviewed(): boolean {
    const intent = this.approvalIntent();
    return !!intent && this.approvalModel().reviewed && this.approvalAssent() === intent;
  }
  set budgetReviewed(value: boolean) {
    this.approvalModel.set({ reviewed: value });
    this.approvalAssent.set(value ? this.approvalIntent() : '');
  }
}
