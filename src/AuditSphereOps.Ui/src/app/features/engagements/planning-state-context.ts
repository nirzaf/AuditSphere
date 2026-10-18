import type { WritableSignal } from '@angular/core';
import type { Planning } from './planning-contracts';

/** Page-owned capabilities shared by the budget and staffing state holders. */
export interface PlanningStateContext {
  readonly engagementId: () => string;
  readonly data: () => Planning | null;
  readonly busy: WritableSignal<boolean>;
  readonly uncertain: WritableSignal<boolean>;
  readonly commandStatus: WritableSignal<string>;
  readonly owner: () => string;
  readonly readRevision: () => number;
  readonly destroyed: () => boolean;
  readonly load: () => void;
  readonly clearProtectedEditor: () => void;
  readonly hasBudgetPreparationPreview: () => boolean;
  readonly hasBudgetApprovalPreview: () => boolean;
  readonly hasStaffingPreview: () => boolean;
}
