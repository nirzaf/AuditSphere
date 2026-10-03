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
import {
  form,
  FormField,
  required,
  maxLength,
  applyEach,
  disabled,
  min,
  max,
  pattern,
  validate,
} from '@angular/forms/signals';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, firstValueFrom, timeout } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { PlanningNavigationDialog } from './planning-navigation';
import { SessionService } from '../../core/session';
import { TabDrafts, PendingRequestReference, pendingRequestReference } from '../../core/tab-drafts';
import { Api } from '../../core/api';
import {
  BudgetApprovalPreview,
  BudgetApprovalReceipt,
  decodeBudgetApprovalPreview,
  decodeBudgetApprovalReceipt,
  decodeBudgetApprovalLookup,
} from './budget-approval-contracts';
import {
  BudgetPreparationPreview,
  BudgetPreparationReceipt,
  decodeBudgetPreparationPreview,
  decodeBudgetPreparationReceipt,
  decodeBudgetPreparationLookup,
} from './budget-preparation-contracts';
import {
  StaffingChangeFields,
  StaffingChangePreview,
  StaffingChangeReceipt,
  decodeStaffingChangePreview,
  decodeStaffingChangeReceipt,
  decodeStaffingChangeLookup,
} from './staffing-change-contracts';
interface TeamMember {
  assignmentId: string;
  userId: string;
  name: string;
  levelLabel: string;
  authorizationRole: string;
  certified: boolean;
}
interface BudgetRow {
  phase: string;
  riskArea: string;
  forecastMinutes: number;
  actualMinutes: number;
  forecastCost: string;
  actualCost: string;
}
interface Budget {
  currency: string;
  id: string;
  version: string;
  rows: BudgetRow[];
  forecastMinutes: number;
  actualMinutes: number;
  forecastCost: string;
  actualCost: string;
}
interface DraftLine {
  role: string;
  activity: string;
  phase: string;
  riskArea: string | null;
  minutes: number;
  cost: string;
}
interface Draft {
  id: string;
  version: string;
  currency: string;
  canApprove: boolean;
  lines: DraftLine[];
}
interface Planning {
  team: TeamMember[];
  budget: Budget | null;
  budgetState: string;
  latestBudgetVersion: string;
  draft: Draft | null;
  canManageStaffing: boolean;
  canPrepareBudget: boolean;
  candidates: { userId: string; name: string; department: string | null; certified: boolean }[];
}
export interface PlanningEditableBudget {
  currency: string;
  lines: {
    role: string;
    activity: string;
    phase: string;
    riskArea: string;
    forecastMinutes: number;
  }[];
}
export function editablePlanningBudget(raw: unknown): PlanningEditableBudget | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const value = raw as Record<string, unknown>;
  if (
    Object.keys(value).sort().join(',') !== 'currency,lines' ||
    typeof value['currency'] !== 'string' ||
    !/^[A-Z]{3}$/.test(value['currency']) ||
    !Array.isArray(value['lines']) ||
    value['lines'].length < 1 ||
    value['lines'].length > 200
  )
    return null;
  const lines: PlanningEditableBudget['lines'] = [];
  for (const rawLine of value['lines']) {
    if (!rawLine || typeof rawLine !== 'object' || Array.isArray(rawLine)) return null;
    const line = rawLine as Record<string, unknown>;
    if (
      Object.keys(line).sort().join(',') !== 'activity,forecastMinutes,phase,riskArea,role' ||
      !['role', 'activity', 'phase'].every(
        (key) =>
          typeof line[key] === 'string' &&
          line[key].length >= 1 &&
          line[key].length <= 50 &&
          line[key] === line[key].trim() &&
          !/[\x00-\x1f\x7f]/.test(line[key]),
      ) ||
      typeof line['riskArea'] !== 'string' ||
      line['riskArea'].length > 120 ||
      /[\x00-\x1f\x7f]/.test(line['riskArea']) ||
      !Number.isSafeInteger(line['forecastMinutes']) ||
      Number(line['forecastMinutes']) < 1 ||
      Number(line['forecastMinutes']) > 10000000
    )
      return null;
    lines.push({
      role: line['role'] as string,
      activity: line['activity'] as string,
      phase: line['phase'] as string,
      riskArea: line['riskArea'],
      forecastMinutes: line['forecastMinutes'] as number,
    });
  }
  return { currency: value['currency'], lines };
}
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function exactDecimal(value: unknown): value is string {
  return typeof value === 'string' && /^-?\d{1,29}(?:\.\d{1,28})?$/.test(value);
}
export function decodePlanning(value: unknown): Planning {
  if (!value || typeof value !== 'object') throw new Error('Invalid planning');
  const v = value as Record<string, unknown>;
  if (
    !Array.isArray(v['team']) ||
    v['team'].length > 1000 ||
    !['APPROVED', 'UNAVAILABLE'].includes(v['budgetState'] as string)
  )
    throw new Error('Invalid team');
  for (const t of v['team'])
    if (
      !t ||
      !['assignmentId', 'userId'].every((k) => typeof t[k] === 'string' && guid.test(t[k])) ||
      !['name', 'levelLabel', 'authorizationRole'].every((k) => typeof t[k] === 'string') ||
      typeof t.certified !== 'boolean'
    )
      throw new Error('Invalid member');
  if (
    typeof v['canManageStaffing'] !== 'boolean' ||
    typeof v['canPrepareBudget'] !== 'boolean' ||
    !Array.isArray(v['candidates']) ||
    v['candidates'].length > 100
  )
    throw new Error('Invalid staffing permissions');
  for (const c of v['candidates'])
    if (
      !c ||
      typeof c.userId !== 'string' ||
      !guid.test(c.userId) ||
      typeof c.name !== 'string' ||
      typeof c.certified !== 'boolean' ||
      (c.department !== null && typeof c.department !== 'string')
    )
      throw new Error('Invalid candidate');
  if (typeof v['latestBudgetVersion'] !== 'string' || !/^\d{1,19}$/.test(v['latestBudgetVersion']))
    throw new Error('Invalid revision');
  const draft = v['draft'] as Draft | null;
  if (draft !== null) {
    if (
      !draft ||
      !guid.test(draft.id) ||
      typeof draft.version !== 'string' ||
      !/^\d{1,19}$/.test(draft.version) ||
      typeof draft.currency !== 'string' ||
      typeof draft.canApprove !== 'boolean' ||
      !Array.isArray(draft.lines) ||
      draft.lines.length > 200
    )
      throw new Error('Invalid draft');
    for (const line of draft.lines)
      if (
        !line ||
        !['role', 'activity', 'phase'].every(
          (k) => typeof (line as unknown as Record<string, unknown>)[k] === 'string',
        ) ||
        (line.riskArea !== null && typeof line.riskArea !== 'string') ||
        !Number.isSafeInteger(line.minutes) ||
        line.minutes < 1 ||
        !exactDecimal(line.cost)
      )
        throw new Error('Invalid draft line');
  }
  const budget = v['budget'] as Record<string, unknown> | null;
  if (budget !== null) {
    if (
      !budget ||
      typeof budget['currency'] !== 'string' ||
      typeof budget['id'] !== 'string' ||
      !guid.test(budget['id']) ||
      typeof budget['version'] !== 'string' ||
      !/^\d{1,19}$/.test(budget['version']) ||
      !Array.isArray(budget['rows']) ||
      budget['rows'].length > 1000
    )
      throw new Error('Invalid budget');
    for (const row of [budget, ...budget['rows']]) {
      if (
        !row ||
        !['forecastMinutes', 'actualMinutes'].every(
          (k) => Number.isSafeInteger(row[k]) && Number(row[k]) >= 0,
        ) ||
        !exactDecimal(row['forecastCost']) ||
        !exactDecimal(row['actualCost'])
      )
        throw new Error('Invalid financial quantity');
    }
    for (const row of budget['rows'])
      if (typeof row.phase !== 'string' || typeof row.riskArea !== 'string')
        throw new Error('Invalid phase');
  }
  if ((v['budgetState'] === 'APPROVED') !== (budget !== null))
    throw new Error('Inconsistent budget state');
  return v as unknown as Planning;
}
@Component({
  selector: 'audit-engagement-planning',
  imports: [FormField, RouterLink, MatButtonModule, MatProgressBarModule],
  template: ` <section aria-label="Engagement planning">
    <h2>Engagement team and budget</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading planning" />
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
      <button matButton (click)="load()">Retry planning</button>
    }
    @if (data(); as planning) {
      <h3>Engagement team</h3>
      @for (person of staffingPending() ? [] : planning.team; track person.assignmentId) {
        <p>
          {{ person.name }} · {{ person.levelLabel }} · {{ person.authorizationRole }} ·
          {{ person.certified ? 'Current certification' : 'No current certification on file' }}
        </p>
        @if (planning.canManageStaffing) {
          <button
            matButton
            [disabled]="busy() || uncertain() || !!budgetPreview()"
            (click)="requestRevocation(person)"
          >
            Revoke {{ person.name }}
          </button>
        }
      } @empty {
        <p>
          {{
            staffingPending()
              ? 'Staffing outcome requires receipt verification and acknowledgement before the team is refreshed.'
              : 'Nobody is staffed on this engagement yet.'
          }}
        </p>
      }
      @if (planning.canManageStaffing) {
        <section>
          <h3>Assign staff</h3>
          <p role="note">
            When dedicated client-site provisioning is enabled, this assignment authorizes Full
            Control over the entire client SharePoint site, including other engagements, sharing and
            deletion. AuditSphere access remains engagement scoped.
          </p>
          <form ngNoForm (submit)="$event.preventDefault(); assign()">
            <label [attr.for]="'staffing-person-' + engagementId()">Person</label>
            <select [id]="'staffing-person-' + engagementId()" [formField]="staffingFields.userId">
              <option value="">Select staff</option>
              @for (candidate of planning.candidates; track candidate.userId) {
                <option [value]="candidate.userId">
                  {{ candidate.name }}{{ candidate.certified ? ' · Certified' : '' }}
                </option>
              }
            </select>
            <label [attr.for]="'staffing-level-' + engagementId()">Level</label>
            <select [id]="'staffing-level-' + engagementId()" [formField]="staffingFields.level">
              @for (level of levels; track level.value) {
                <option [value]="level.value">{{ level.label }}</option>
              }
            </select>
            <label
              ><input
                type="checkbox"
                [formField]="staffingFields.reviewed"
                (change)="reviewed = checkboxChecked($event)"
              />
              I reviewed the engagement role and client-site access.</label
            >
            <button
              matButton
              type="submit"
              [disabled]="
                busy() ||
                !reviewed ||
                !selectedUser ||
                uncertain() ||
                !!budgetPreview() ||
                staffingFields().invalid()
              "
            >
              Review team assignment
            </button>
          </form>
          @if (revokeTarget(); as person) {
            <section role="alert">
              <p>
                Remove {{ person.name }} from this engagement and revoke the staffing role?
                SharePoint removal is scheduled when no active client assignment remains.
              </p>
              <button matButton [disabled]="busy()" (click)="revoke(person.assignmentId)">
                Review revocation</button
              ><button matButton [disabled]="busy()" (click)="revokeTarget.set(null)">
                Cancel
              </button>
            </section>
          }
          <p role="status">{{ commandStatus() }}</p>
        </section>
      }
      @if (staffingPreview(); as preview) {
        <section aria-label="Staffing change review">
          <h3>Review staffing change</h3>
          <p>
            {{ preview.fields.action === 'ASSIGN' ? 'Assign' : 'Revoke' }} {{ preview.userName }} ·
            {{ preview.authorizationRole }} · Engagement scope
          </p>
          <p>
            {{ preview.certified ? 'Current certification' : 'No current certification on file' }}
          </p>
          <p>
            {{
              preview.fields.action === 'REVOKE'
                ? preview.removesLocalGrant
                  ? 'Staffing-owned role will be revoked and protected sessions invalidated.'
                  : 'Independent role grants will remain unchanged.'
                : 'AuditSphere role applies only to this engagement.'
            }}
          </p>
          <p role="note">{{ preview.sharePointEffect }}</p>
          <label
            ><input
              type="checkbox"
              [formField]="staffingConfirmationFields.reviewed"
              (change)="staffingConfirmationReviewed = checkboxChecked($event)"
            />
            I reviewed this exact staffing change and its access effects.</label
          >
          <button
            matButton
            [disabled]="busy() || uncertain() || !staffingConfirmationReviewed"
            (click)="confirmStaffingChange()"
          >
            Confirm staffing change
          </button>
          <button matButton [disabled]="busy()" (click)="cancelStaffingReview()">
            Cancel staffing review
          </button>
        </section>
      }
      @if (staffingPending()) {
        <section aria-label="Staffing receipt recovery">
          @if (staffingReceipt(); as receipt) {
            <p>
              Staffing
              {{
                receipt.preview.fields.action === 'ASSIGN' ? 'assignment' : 'revocation'
              }}
              recorded for {{ receipt.preview.userName }}.
            </p>
            <p>Receipt {{ receipt.id }} · {{ receipt.createdAt }}</p>
            <button matButton [disabled]="busy()" (click)="acknowledgeStaffingChange()">
              Acknowledge staffing change
            </button>
          } @else {
            <p>
              A staffing request requires verification. No command will be automatically retried.
            </p>
          }
          <button matButton [disabled]="busy()" (click)="reconcileStaffingChange()">
            Verify staffing request receipt
          </button>
        </section>
      }
      @if (planning.canPrepareBudget) {
        <section aria-label="Budget preparation">
          <h3>Prepare budget version</h3>
          <p>
            Latest version {{ planning.latestBudgetVersion }}. An approved rate card is required for
            each role and activity. Another manager or partner must approve your draft.
          </p>
          <p>
            Save editable fields in this tab only. Restoring requires the same budget version and
            draft state; approval is never restored.
          </p>
          <button
            matButton
            type="button"
            [disabled]="busy() || uncertain() || !!budgetPreview()"
            (click)="saveEditableBudget()"
          >
            Save budget fields in this tab
          </button>
          <button
            matButton
            type="button"
            [disabled]="busy() || uncertain() || !!budgetPreview()"
            (click)="restoreEditableBudget()"
          >
            Restore saved budget fields
          </button>
          <button
            matButton
            type="button"
            [disabled]="busy() || uncertain() || !!budgetPreview()"
            (click)="discardEditableBudget()"
          >
            Discard saved budget fields
          </button>
          <p role="status">{{ draftStatus() }}</p>
          <form ngNoForm (submit)="$event.preventDefault(); saveBudget()">
            @if (budgetFields().invalid() && budgetFields().touched()) {
              <p role="alert">
                Check the budget fields: currency needs three letters; role, activity and phase are
                required (up to 50 characters); risk area allows 120 characters; forecast minutes
                must be a whole number from 1 to 10,000,000.
              </p>
            }
            <label>Currency <input [formField]="budgetFields.currency" /></label>
            @for (line of budgetLines; track $index; let index = $index) {
              <fieldset>
                <legend>Budget line {{ index + 1 }}</legend>
                <label>Role <input [formField]="budgetFields.lines[index].role" /></label>
                <label>Activity <input [formField]="budgetFields.lines[index].activity" /></label>
                <label>Phase <input [formField]="budgetFields.lines[index].phase" /></label>
                <label>Risk area <input [formField]="budgetFields.lines[index].riskArea" /></label>
                <label
                  >Forecast minutes
                  <input
                    type="number"
                    [formField]="budgetFields.lines[index].forecastMinutes"
                    step="1"
                /></label>
                <button
                  matButton
                  type="button"
                  [disabled]="busy() || uncertain() || !!budgetPreview()"
                  (click)="removeBudgetLine(index)"
                >
                  Remove line {{ index + 1 }}
                </button>
              </fieldset>
            }
            <button
              matButton
              type="button"
              [disabled]="busy() || uncertain() || !!budgetPreview() || budgetLines.length >= 200"
              (click)="addBudgetLine()"
            >
              Add line
            </button>
            <button
              matButton
              type="submit"
              [disabled]="busy() || uncertain() || !!budgetPreview() || !budgetLines.length"
            >
              Review budget preparation
            </button>
          </form>
          @if (budgetPreview(); as preview) {
            <section aria-label="Reviewed budget preparation">
              <h3>Review draft budget · {{ preview.fields.currency }}</h3>
              <p>
                Previous version {{ preview.fields.expectedVersion }} · Forecast
                {{ preview.forecastCost }}. Approved rate snapshot:
              </p>
              @for (line of preview.lines; track $index) {
                <p>
                  {{ line.role }} · {{ line.activity }} · {{ line.phase }} · {{ line.riskArea }} ·
                  {{ line.forecastMinutes }} minutes · rate {{ line.ratePerHour }} · forecast
                  {{ line.forecastCost }}
                </p>
              }
              <label
                ><input
                  type="checkbox"
                  [formField]="preparationFields.reviewed"
                  (change)="preparationReviewed = checkboxChecked($event)"
                />
                I reviewed these exact rates and authorize draft preparation.</label
              >
              <button
                matButton
                [disabled]="busy() || uncertain() || !preparationReviewed"
                (click)="confirmBudgetPreparation()"
              >
                Confirm draft budget preparation
              </button>
              <button matButton [disabled]="busy() || uncertain()" (click)="cancelBudgetReview()">
                Cancel budget review
              </button>
            </section>
          }
          @if (budgetPending()) {
            <button matButton [disabled]="busy()" (click)="reconcileBudgetPreparation()">
              Verify budget request receipt
            </button>
          }
          @if (budgetReceipt(); as receipt) {
            <section aria-label="Budget preparation receipt">
              <h3>Recorded draft budget</h3>
              <p>
                Budget {{ receipt.budgetId }} · {{ receipt.preview.fields.currency }} · forecast
                {{ receipt.preview.forecastCost }}. Approval remains separate.
              </p>
              <button matButton [disabled]="busy()" (click)="acknowledgeBudgetPreparation()">
                Acknowledge budget preparation
              </button>
            </section>
          }
          <p role="status">{{ commandStatus() }}</p>
        </section>
      }
      @if (!approvalPending() && planning.draft; as draft) {
        <section aria-label="Budget approval">
          <h3>Draft version {{ draft.version }} · {{ draft.currency }}</h3>
          @for (line of draft.lines; track $index) {
            <p>
              {{ line.role }} · {{ line.activity }} · {{ line.phase }} · {{ line.riskArea }} ·
              {{ line.minutes }} minutes · {{ line.cost }}
            </p>
          }
          @if (draft.canApprove) {
            <label
              ><input
                type="checkbox"
                [formField]="approvalFields.reviewed"
                (change)="budgetReviewed = checkboxChecked($event)"
              />
              I reviewed this draft for independent approval.</label
            >
            <button
              matButton
              [disabled]="busy() || uncertain() || !budgetReviewed"
              (click)="approveBudget(draft.id)"
            >
              Review budget approval
            </button>
          } @else {
            <p>A different authorized manager or partner must approve this draft.</p>
          }
          <p role="status">{{ commandStatus() }}</p>
        </section>
      }
      @if (approvalPreview(); as preview) {
        <section aria-label="Reviewed budget approval">
          <h3>Review independent budget approval</h3>
          <p>
            Draft {{ preview.fields.expectedVersion }} · {{ preview.currency }} · forecast
            {{ preview.forecastCost }}
          </p>
          @for (line of preview.lines; track $index) {
            <p>
              {{ line.role }} · {{ line.activity }} · {{ line.phase }} · {{ line.riskArea }} ·
              {{ line.minutes }} minutes · {{ line.cost }}
            </p>
          }
          <label
            ><input
              type="checkbox"
              [formField]="approvalConfirmationFields.reviewed"
              (change)="approvalConfirmationReviewed = checkboxChecked($event)"
            />I reviewed this exact draft and authorize independent approval.</label
          >
          <button
            matButton
            [disabled]="busy() || uncertain() || !approvalConfirmationReviewed"
            (click)="confirmBudgetApproval()"
          >
            Confirm budget approval
          </button>
          <button matButton [disabled]="busy() || uncertain()" (click)="cancelApprovalReview()">
            Cancel approval review
          </button>
        </section>
      }
      @if (approvalPending()) {
        <button matButton [disabled]="busy()" (click)="reconcileBudgetApproval()">
          Verify approval request receipt
        </button>
      }
      @if (approvalReceipt(); as receipt) {
        <section aria-label="Budget approval receipt">
          <h3>Recorded budget approval</h3>
          <p>
            Budget {{ receipt.budgetId }} · {{ receipt.preview.currency }} · forecast
            {{ receipt.preview.forecastCost }}.
          </p>
          <button matButton [disabled]="busy()" (click)="acknowledgeBudgetApproval()">
            Acknowledge budget approval
          </button>
        </section>
      }
      <h3>Approved budget</h3>
      @if (approvalPending()) {
        <p>
          Approval outcome requires receipt verification and acknowledgement before the current
          budget is refreshed.
        </p>
      } @else if (planning.budget; as budget) {
        <p>
          Version {{ budget.version }} · {{ budget.currency }}. Actuals include approved time only.
          Monetary values are shown exactly as supplied by the accounting service.
        </p>
        <div class="table-scroll">
          <table>
            <caption>
              Budget by phase and risk area
            </caption>
            <thead>
              <tr>
                <th>Phase</th>
                <th>Risk area</th>
                <th>Budget minutes</th>
                <th>Actual minutes</th>
                <th>Budget value</th>
                <th>Actual value</th>
              </tr>
            </thead>
            <tbody>
              @for (row of budget.rows; track $index) {
                <tr>
                  <td>{{ row.phase }}</td>
                  <td>{{ row.riskArea }}</td>
                  <td>{{ row.forecastMinutes }}</td>
                  <td>{{ row.actualMinutes }}</td>
                  <td>{{ row.forecastCost }}</td>
                  <td>{{ row.actualCost }}</td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <th>Total</th>
                <td></td>
                <td>{{ budget.forecastMinutes }}</td>
                <td>{{ budget.actualMinutes }}</td>
                <td>{{ budget.forecastCost }}</td>
                <td>{{ budget.actualCost }}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      } @else {
        <p>
          No approved budget is available. Review budget preparation and approval in the engagement
          workbench.
        </p>
      }
      <a [routerLink]="['/app/engagements', engagementId()]">Manage staffing and budget</a>
    }
  </section>`,
})
export class EngagementPlanning {
  readonly levels = [
    { value: 'ENGAGEMENT_PARTNER', label: 'Engagement Partner' },
    { value: 'AUDIT_MANAGER', label: 'Audit Manager' },
    { value: 'SENIOR_AUDITOR', label: 'Senior Auditor' },
    { value: 'STAFF_ASSOCIATE', label: 'Staff Associate' },
  ];
  private readonly drafts = inject(TabDrafts);
  readonly draftStatus = signal('');
  private budgetDraftScope(allowReviewed = false) {
    const planning = this.data();
    if (
      !planning?.canPrepareBudget ||
      this.busy() ||
      this.uncertain() ||
      (!allowReviewed && this.budgetPreview()) ||
      !!this.approvalPreview() ||
      !!this.staffingPreview() ||
      this.destroyed ||
      !this.owner()
    )
      return null;
    // The entity and session namespace bind identity; revision binds budget version and draft/approval state.
    return {
      entity: 'engagement-budget:' + this.engagementId(),
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
        this.busy() ||
        this.uncertain() ||
        !!this.budgetPreview() ||
        !!this.approvalPreview() ||
        !!this.staffingPreview() ||
        !this.data()?.canPrepareBudget,
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
      !this.data()?.canPrepareBudget ||
      this.busy() ||
      this.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
      this.staffingPreview()
    )
      return;
    this.budgetLines = this.budgetLines.filter((_, i) => i !== index);
  }
  addBudgetLine(): void {
    if (
      !this.data()?.canPrepareBudget ||
      this.busy() ||
      this.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
      this.staffingPreview() ||
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
  private readonly dialog = inject(MatDialog);
  private navigationPending = false;
  planningDirty(): boolean {
    return (
      (!!this.data()?.canManageStaffing || !!this.data()?.canPrepareBudget) &&
      (JSON.stringify(this.budgetModel()) !== this.budgetBaseline() ||
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
  private readonly api = inject(Api);
  readonly budgetPreview = signal<BudgetPreparationPreview | null>(null);
  readonly budgetReceipt = signal<BudgetPreparationReceipt | null>(null);
  readonly budgetPending = signal<PendingRequestReference | null>(null);
  private pendingBudgetScope(basis = '0'.repeat(64)) {
    return { entity: 'engagement-budget-request/' + this.engagementId(), baseRevision: basis };
  }
  async saveBudget(): Promise<void> {
    const planning = this.data(),
      owner = this.owner();
    if (
      !planning?.canPrepareBudget ||
      this.busy() ||
      this.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
      this.staffingPreview()
    )
      return;
    const editable = editablePlanningBudget({
      currency: this.currency.trim().toUpperCase(),
      lines: this.budgetLines,
    });
    if (!editable) {
      this.commandStatus.set('Enter bounded budget fields and whole forecast minutes.');
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
    this.busy.set(true);
    this.preparationReviewed = false;
    this.budgetReceipt.set(null);
    const result = await this.api.command(
      '/api/ui/engagements/' + this.engagementId() + '/budget-preparation/preview',
      { requestId, fields },
    );
    if (this.destroyed || owner !== this.owner()) return;
    this.busy.set(false);
    if (!result.ok) {
      this.commandStatus.set(result.message);
      if (result.status === 401 || result.status === 403) {
        this.clearProtectedEditor();
        this.load();
      }
      return;
    }
    try {
      const preview = decodeBudgetPreparationPreview(result.value);
      if (
        preview.engagementId !== this.engagementId() ||
        preview.requestId !== requestId ||
        this.data()?.latestBudgetVersion !== fields.expectedVersion ||
        JSON.stringify(preview.fields) !== JSON.stringify(fields)
      )
        throw new Error('Changed review');
      this.budgetPreview.set(preview);
      this.commandStatus.set(
        'Review the exact approved rates and forecast. This prepares a draft; it does not approve it.',
      );
    } catch {
      this.commandStatus.set(
        'Budget review returned an unsupported or changed response. Refresh before reviewing again.',
      );
    }
  }
  cancelBudgetReview(): void {
    if (this.busy() || this.uncertain()) return;
    this.budgetPreview.set(null);
    this.preparationReviewed = false;
  }
  async confirmBudgetPreparation(): Promise<void> {
    const p = this.budgetPreview(),
      owner = this.owner();
    if (
      !p ||
      !this.data()?.canPrepareBudget ||
      !this.preparationReviewed ||
      this.busy() ||
      this.uncertain()
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
      this.commandStatus.set('Recovery storage is unavailable. No budget command was sent.');
      return;
    }
    this.budgetPending.set(reference);
    this.busy.set(true);
    this.preparationReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.engagementId() + '/budget-preparation',
      {
        requestId: p.requestId,
        fields: p.fields,
        requestHash: p.requestHash,
        reviewBasis: p.reviewBasis,
        reviewed: true,
      },
    );
    if (this.destroyed || owner !== this.owner()) return;
    this.busy.set(false);
    this.budgetPreview.set(null);
    if (!result.ok) {
      this.commandStatus.set(result.message);
      if (result.unknown) this.uncertain.set(true);
      else {
        this.drafts.clear(this.pendingBudgetScope().entity);
        this.budgetPending.set(null);
        if (result.status === 401 || result.status === 403) {
          this.clearProtectedEditor();
          this.load();
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
      this.uncertain.set(true);
      this.commandStatus.set(
        'Draft budget recorded with an immutable receipt. Acknowledge the result before continuing.',
      );
    } catch {
      this.uncertain.set(true);
      this.commandStatus.set(
        'Outcome unconfirmed. Verify the retained budget receipt before another change.',
      );
    }
  }
  private matchesBudgetReceipt(r: BudgetPreparationReceipt, p: PendingRequestReference): boolean {
    return (
      r.engagementId === this.engagementId() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async reconcileBudgetPreparation(): Promise<void> {
    const p = this.budgetPending(),
      owner = this.owner();
    if (!p || !this.data()?.canPrepareBudget || this.busy()) return;
    this.busy.set(true);
    this.budgetPreview.set(null);
    this.preparationReviewed = false;
    try {
      const lookup = await this.api.get(
        '/api/ui/engagements/' +
          this.engagementId() +
          '/budget-preparation/receipts/' +
          p.requestId +
          '?requestHash=' +
          p.requestHash,
        decodeBudgetPreparationLookup,
      );
      if (this.destroyed || owner !== this.owner()) return;
      if (lookup.found && lookup.receipt && this.matchesBudgetReceipt(lookup.receipt, p)) {
        this.budgetReceipt.set(lookup.receipt);
        this.uncertain.set(true);
        this.commandStatus.set(
          'Retained receipt confirms the draft budget. Acknowledge the result before continuing.',
        );
      } else if (!lookup.found) {
        this.drafts.clear(this.pendingBudgetScope().entity);
        this.budgetPending.set(null);
        this.uncertain.set(false);
        this.commandStatus.set(
          'No committed preparation was found after reconciliation. Refresh and explicitly review current fields before a new request.',
        );
        this.load();
      } else throw new Error('Wrong receipt');
    } catch {
      if (!this.destroyed && owner === this.owner()) {
        this.clearProtectedEditor();
        this.load();
        this.commandStatus.set(
          'Receipt unavailable. Revalidate access before reconciling again. No command was retried.',
        );
      }
    } finally {
      if (!this.destroyed && owner === this.owner()) this.busy.set(false);
    }
  }
  acknowledgeBudgetPreparation(): void {
    if (!this.budgetReceipt() || this.busy()) return;
    if (!this.drafts.clear(this.pendingBudgetScope().entity)) {
      this.commandStatus.set(
        'Receipt acknowledged, but recovery storage could not be cleared. Retry clearing before continuing.',
      );
      return;
    }
    this.budgetPending.set(null);
    this.budgetReceipt.set(null);
    this.uncertain.set(false);
    this.commandStatus.set(
      'Budget preparation acknowledged. Approval remains a separate reviewed action.',
    );
    this.drafts.clear('engagement-budget:' + this.engagementId());
    this.load();
  }
  readonly approvalPreview = signal<BudgetApprovalPreview | null>(null);
  readonly approvalReceipt = signal<BudgetApprovalReceipt | null>(null);
  readonly approvalPending = signal<PendingRequestReference | null>(null);
  private pendingApprovalScope(basis = '0'.repeat(64)) {
    return {
      entity: 'engagement-budget-approval-request/' + this.engagementId(),
      baseRevision: basis,
    };
  }
  cancelApprovalReview(): void {
    if (this.busy() || this.uncertain()) return;
    this.approvalPreview.set(null);
    this.approvalConfirmationReviewed = false;
  }
  async approveBudget(id: string): Promise<void> {
    const draft = this.data()?.draft,
      owner = this.owner(),
      captured = JSON.stringify(draft);
    if (
      !draft?.canApprove ||
      draft.id !== id ||
      !this.budgetReviewed ||
      this.busy() ||
      this.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
      this.staffingPreview()
    )
      return;
    const requestId = crypto.randomUUID();
    this.busy.set(true);
    this.budgetReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.engagementId() + '/budget-approval/preview',
      { requestId, fields: { budgetId: id, expectedVersion: draft.version } },
    );
    if (this.destroyed || owner !== this.owner()) return;
    this.busy.set(false);
    if (!result.ok) {
      this.commandStatus.set(result.message);
      if (result.status === 401 || result.status === 403) {
        this.clearProtectedEditor();
        this.load();
      }
      return;
    }
    try {
      const preview = decodeBudgetApprovalPreview(result.value);
      if (
        captured !== JSON.stringify(this.data()?.draft) ||
        preview.engagementId !== this.engagementId() ||
        preview.requestId !== requestId ||
        preview.fields.budgetId !== id ||
        preview.fields.expectedVersion !== draft.version ||
        preview.currency !== draft.currency ||
        JSON.stringify(preview.lines) !== JSON.stringify(draft.lines)
      )
        throw new Error('Changed approval review');
      this.approvalPreview.set(preview);
      this.commandStatus.set(
        'Review the exact draft and explicitly confirm independent approval. No approval has been submitted.',
      );
    } catch {
      this.commandStatus.set(
        'Approval review changed or returned an unsupported response. Refresh before reviewing again.',
      );
    }
  }
  async confirmBudgetApproval(): Promise<void> {
    const p = this.approvalPreview(),
      owner = this.owner();
    if (
      !p ||
      !this.data()?.canManageStaffing ||
      !this.approvalConfirmationReviewed ||
      this.busy() ||
      this.uncertain()
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
      this.commandStatus.set('Recovery storage is unavailable. No budget command was sent.');
      return;
    }
    this.approvalPending.set(reference);
    this.busy.set(true);
    this.approvalConfirmationReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.engagementId() + '/budget-approval',
      {
        requestId: p.requestId,
        fields: p.fields,
        requestHash: p.requestHash,
        reviewBasis: p.reviewBasis,
        reviewed: true,
      },
    );
    if (this.destroyed || owner !== this.owner()) return;
    this.busy.set(false);
    this.approvalPreview.set(null);
    if (!result.ok) {
      this.commandStatus.set(result.message);
      if (result.unknown) this.uncertain.set(true);
      else {
        this.drafts.clear(this.pendingApprovalScope().entity);
        this.approvalPending.set(null);
        if (result.status === 401 || result.status === 403) {
          this.clearProtectedEditor();
          this.load();
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
      this.uncertain.set(true);
      this.commandStatus.set(
        'Budget approval recorded with an immutable receipt. Acknowledge the result before continuing.',
      );
    } catch {
      this.uncertain.set(true);
      this.commandStatus.set(
        'Outcome unconfirmed. Verify the retained budget receipt before another change.',
      );
    }
  }
  private matchesApprovalReceipt(r: BudgetApprovalReceipt, p: PendingRequestReference): boolean {
    return (
      r.engagementId === this.engagementId() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async reconcileBudgetApproval(): Promise<void> {
    const p = this.approvalPending(),
      owner = this.owner();
    if (!p || !this.data()?.canManageStaffing || this.busy()) return;
    this.busy.set(true);
    this.approvalPreview.set(null);
    this.approvalConfirmationReviewed = false;
    try {
      const lookup = await this.api.get(
        '/api/ui/engagements/' +
          this.engagementId() +
          '/budget-approval/receipts/' +
          p.requestId +
          '?requestHash=' +
          p.requestHash,
        decodeBudgetApprovalLookup,
      );
      if (this.destroyed || owner !== this.owner()) return;
      if (lookup.found && lookup.receipt && this.matchesApprovalReceipt(lookup.receipt, p)) {
        this.approvalReceipt.set(lookup.receipt);
        this.uncertain.set(true);
        this.commandStatus.set(
          'Retained receipt confirms the budget approval. Acknowledge the result before continuing.',
        );
      } else if (!lookup.found) {
        this.drafts.clear(this.pendingApprovalScope().entity);
        this.approvalPending.set(null);
        this.uncertain.set(false);
        this.commandStatus.set(
          'No committed approval was found after reconciliation. Refresh and explicitly review current fields before a new request.',
        );
        this.load();
      } else throw new Error('Wrong receipt');
    } catch {
      if (!this.destroyed && owner === this.owner()) {
        this.clearProtectedEditor();
        this.load();
        this.commandStatus.set(
          'Receipt unavailable. Revalidate access before reconciling again. No command was retried.',
        );
      }
    } finally {
      if (!this.destroyed && owner === this.owner()) this.busy.set(false);
    }
  }
  acknowledgeBudgetApproval(): void {
    if (!this.approvalReceipt() || this.busy()) return;
    if (!this.drafts.clear(this.pendingApprovalScope().entity)) {
      this.commandStatus.set(
        'Receipt acknowledged, but recovery storage could not be cleared. Retry clearing before continuing.',
      );
      return;
    }
    this.approvalPending.set(null);
    this.approvalReceipt.set(null);
    this.uncertain.set(false);
    this.commandStatus.set('Budget approval acknowledged. Approval has been recorded.');
    this.drafts.clear('engagement-budget:' + this.engagementId());
    this.load();
  }
  readonly staffingModel = signal({ userId: '', level: 'STAFF_ASSOCIATE', reviewed: false });
  readonly preparationModel = signal({ reviewed: false });
  readonly approvalModel = signal({ reviewed: false });
  readonly approvalConfirmationModel = signal({ reviewed: false });
  private readonly approvalConfirmationAssent = signal('');
  readonly approvalConfirmationFields = form(this.approvalConfirmationModel, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.busy() ||
        this.uncertain() ||
        !this.approvalPreview() ||
        !this.data()?.draft?.canApprove,
    );
  });
  private approvalConfirmationIntent(): string {
    return this.owner() && this.approvalPreview() && this.data()?.draft?.canApprove
      ? JSON.stringify([
          this.owner(),
          this.readRevision,
          this.approvalPreview(),
          this.data()?.draft,
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
  private readonly staffingAssent = signal('');
  private readonly preparationAssent = signal('');
  private readonly approvalAssent = signal('');
  readonly staffingFields = form(this.staffingModel, (p) => {
    required(p.userId);
    pattern(p.userId, guid);
    validate(p.level, ({ value }) =>
      this.levels.some((l) => l.value === value()) ? undefined : { kind: 'staffingLevel' },
    );
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.busy() ||
        this.uncertain() ||
        !!this.budgetPreview() ||
        !!this.approvalPreview() ||
        !!this.staffingPreview() ||
        !this.data()?.canManageStaffing,
    );
  });
  readonly preparationFields = form(this.preparationModel, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.busy() ||
        this.uncertain() ||
        !this.budgetPreview() ||
        !!this.approvalPreview() ||
        !!this.staffingPreview() ||
        !this.data()?.canPrepareBudget,
    );
  });
  readonly approvalFields = form(this.approvalModel, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.busy() ||
        this.uncertain() ||
        !!this.budgetPreview() ||
        !!this.approvalPreview() ||
        !!this.staffingPreview() ||
        !this.data()?.draft?.canApprove,
    );
  });
  private staffingIntent(): string {
    return this.owner() && guid.test(this.selectedUser)
      ? JSON.stringify([this.owner(), this.readRevision, this.selectedUser, this.selectedLevel])
      : '';
  }
  private preparationIntent(): string {
    return this.owner() && this.budgetPreview()
      ? JSON.stringify([this.owner(), this.readRevision, this.budgetPreview()])
      : '';
  }
  private approvalIntent(): string {
    return this.owner() && this.data()?.draft?.canApprove
      ? JSON.stringify([this.owner(), this.readRevision, this.data()?.draft])
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
  checkboxChecked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  readonly revokeTarget = signal<TeamMember | null>(null);
  readonly engagementId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
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
  constructor() {
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
        this.currency = 'QAR';
        this.budgetReviewed = false;
        this.budgetLines = [
          {
            role: 'Senior',
            activity: 'AUDIT',
            phase: 'PLANNING',
            riskArea: '',
            forecastMinutes: 480,
          },
        ];
        this.budgetBaseline.set(JSON.stringify(this.budgetModel()));
        this.selectedUser = '';
        this.selectedLevel = 'STAFF_ASSOCIATE';
        this.reviewed = false;
        this.revokeTarget.set(null);
        this.commandStatus.set('');
        this.draftStatus.set('');
        this.budgetPreview.set(null);
        this.budgetReceipt.set(null);
        this.budgetPending.set(null);
        this.approvalPreview.set(null);
        this.approvalReceipt.set(null);
        this.approvalPending.set(null);
        this.staffingPreview.set(null);
        this.staffingReceipt.set(null);
        this.staffingPending.set(null);
        this.staffingConfirmationReviewed = false;
        this.approvalConfirmationReviewed = false;
        this.preparationReviewed = false;
        this.uncertain.set(false);
        if (staff) this.load();
      });
    });
    effect(() => {
      const staff =
        this.staffingModel().reviewed &&
        (!this.staffingIntent() || this.staffingAssent() !== this.staffingIntent());
      const preparation =
        this.preparationModel().reviewed &&
        (!this.preparationIntent() || this.preparationAssent() !== this.preparationIntent());
      const approval =
        this.approvalModel().reviewed &&
        (!this.approvalIntent() || this.approvalAssent() !== this.approvalIntent());
      const staffingConfirmation =
        this.staffingConfirmationModel().reviewed && !this.staffingConfirmationReviewed;
      const confirmation =
        this.approvalConfirmationModel().reviewed && !this.approvalConfirmationReviewed;
      untracked(() => {
        if (staffingConfirmation) {
          this.staffingConfirmationReviewed = false;
          this.staffingConfirmationFields.reviewed().reset(false);
        }
        if (confirmation) {
          this.approvalConfirmationReviewed = false;
          this.approvalConfirmationFields.reviewed().reset(false);
        }
        if (staff) {
          this.reviewed = false;
          this.staffingFields.reviewed().reset(false);
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
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.lifetime++;
      this.readRevision++;
      this.request?.unsubscribe();
      this.clearProtectedEditor();
    });
  }
  requestRevocation(person: TeamMember): void {
    if (
      !this.busy() &&
      !this.uncertain() &&
      !(this.budgetPreview() || this.approvalPreview() || this.staffingPreview())
    )
      this.revokeTarget.set(person);
  }
  async assign(): Promise<void> {
    if (
      !this.data()?.canManageStaffing ||
      !this.reviewed ||
      this.staffingFields().invalid() ||
      this.busy() ||
      this.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
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
      !this.data()?.canManageStaffing ||
      this.busy() ||
      this.uncertain() ||
      this.budgetPreview() ||
      this.approvalPreview() ||
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
    return { entity: 'engagement-staffing-request/' + this.engagementId(), baseRevision: basis };
  }
  readonly staffingConfirmationModel = signal({ reviewed: false });
  private readonly staffingConfirmationAssent = signal('');
  readonly staffingConfirmationFields = form(this.staffingConfirmationModel, (p) => {
    validate(p.reviewed, ({ value }) => (value() ? undefined : { kind: 'reviewRequired' }));
    disabled(
      p,
      () =>
        this.busy() ||
        this.uncertain() ||
        !this.staffingPreview() ||
        !this.data()?.canManageStaffing,
    );
  });
  private staffingConfirmationIntent(): string {
    return this.owner() && this.staffingPreview() && this.data()?.canManageStaffing
      ? JSON.stringify([this.owner(), this.readRevision, this.staffingPreview(), this.data()?.team])
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
    if (this.busy() || this.uncertain()) return;
    this.staffingPreview.set(null);
    this.staffingConfirmationReviewed = false;
  }
  private async previewStaffingChange(fields: StaffingChangeFields): Promise<void> {
    const owner = this.owner(),
      requestId = crypto.randomUUID(),
      captured = JSON.stringify(this.data()?.team);
    this.busy.set(true);
    this.reviewed = false;
    this.staffingConfirmationReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.engagementId() + '/staffing-change/preview',
      { requestId, fields },
    );
    if (this.destroyed || owner !== this.owner()) return;
    this.busy.set(false);
    if (!result.ok) {
      this.commandStatus.set(result.message);
      if (result.status === 401 || result.status === 403) {
        this.clearProtectedEditor();
        this.load();
      }
      return;
    }
    try {
      const p = decodeStaffingChangePreview(result.value);
      if (
        p.engagementId !== this.engagementId() ||
        p.requestId !== requestId ||
        JSON.stringify(p.fields) !== JSON.stringify(fields) ||
        captured !== JSON.stringify(this.data()?.team)
      )
        throw new Error('Changed staffing context');
      this.staffingPreview.set(p);
      this.revokeTarget.set(null);
      this.commandStatus.set(
        'Review the exact staffing change and explicitly confirm. No change has been submitted.',
      );
    } catch {
      this.commandStatus.set(
        'Staffing review changed or returned an unsupported response. Refresh before reviewing again.',
      );
    }
  }
  async confirmStaffingChange(): Promise<void> {
    const p = this.staffingPreview(),
      owner = this.owner();
    if (
      !p ||
      !this.data()?.canManageStaffing ||
      !this.staffingConfirmationReviewed ||
      this.busy() ||
      this.uncertain()
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
      this.commandStatus.set('Recovery storage is unavailable. No staffing command was sent.');
      return;
    }
    this.staffingPending.set(reference);
    this.busy.set(true);
    this.staffingConfirmationReviewed = false;
    const result = await this.api.command(
      '/api/ui/engagements/' + this.engagementId() + '/staffing-change',
      {
        requestId: p.requestId,
        fields: p.fields,
        requestHash: p.requestHash,
        reviewBasis: p.reviewBasis,
        reviewed: true,
      },
    );
    if (this.destroyed || owner !== this.owner()) return;
    this.busy.set(false);
    this.staffingPreview.set(null);
    if (!result.ok) {
      this.commandStatus.set(result.message);
      if (result.unknown) this.uncertain.set(true);
      else {
        this.drafts.clear(this.pendingStaffingScope().entity);
        this.staffingPending.set(null);
        if (result.status === 401 || result.status === 403) {
          this.clearProtectedEditor();
          this.load();
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
      this.uncertain.set(true);
      this.commandStatus.set(
        'Staffing change recorded with an immutable receipt. Acknowledge before continuing.',
      );
    } catch {
      this.uncertain.set(true);
      this.commandStatus.set(
        'Outcome unconfirmed. Verify the retained staffing receipt before another change.',
      );
    }
  }
  private matchesStaffingReceipt(r: StaffingChangeReceipt, p: PendingRequestReference): boolean {
    return (
      r.engagementId === this.engagementId() &&
      r.actorId === this.session.current()?.userId &&
      r.requestId === p.requestId &&
      r.requestHash === p.requestHash
    );
  }
  async reconcileStaffingChange(): Promise<void> {
    const p = this.staffingPending(),
      owner = this.owner();
    if (!p || !this.data()?.canManageStaffing || this.busy()) return;
    this.busy.set(true);
    this.staffingPreview.set(null);
    this.staffingConfirmationReviewed = false;
    try {
      const lookup = await this.api.get(
        '/api/ui/engagements/' +
          this.engagementId() +
          '/staffing-change/receipts/' +
          p.requestId +
          '?requestHash=' +
          p.requestHash,
        decodeStaffingChangeLookup,
      );
      if (this.destroyed || owner !== this.owner()) return;
      if (lookup.found && lookup.receipt && this.matchesStaffingReceipt(lookup.receipt, p)) {
        this.staffingReceipt.set(lookup.receipt);
        this.uncertain.set(true);
        this.commandStatus.set(
          'Retained receipt confirms the staffing change. Acknowledge before continuing.',
        );
      } else if (!lookup.found) {
        this.drafts.clear(this.pendingStaffingScope().entity);
        this.staffingPending.set(null);
        this.uncertain.set(false);
        this.commandStatus.set(
          'No committed staffing change was found. Refresh and explicitly review before a new request.',
        );
        this.load();
      } else throw new Error('Wrong receipt');
    } catch {
      if (!this.destroyed && owner === this.owner()) {
        this.clearProtectedEditor();
        this.load();
        this.commandStatus.set(
          'Receipt unavailable. Revalidate access before reconciling again. No command was retried.',
        );
      }
    } finally {
      if (!this.destroyed && owner === this.owner()) this.busy.set(false);
    }
  }
  acknowledgeStaffingChange(): void {
    if (!this.staffingReceipt() || this.busy()) return;
    if (!this.drafts.clear(this.pendingStaffingScope().entity)) {
      this.commandStatus.set('Recovery storage could not be cleared. Retry before continuing.');
      return;
    }
    this.staffingPending.set(null);
    this.staffingReceipt.set(null);
    this.uncertain.set(false);
    this.selectedUser = '';
    this.reviewed = false;
    this.revokeTarget.set(null);
    this.commandStatus.set('Staffing change acknowledged.');
    this.load();
  }
  private clearProtectedEditor(): void {
    this.data.set(null);
    this.draftStatus.set('');
    this.budgetPreview.set(null);
    this.budgetReceipt.set(null);
    this.budgetPending.set(null);
    this.approvalPreview.set(null);
    this.approvalReceipt.set(null);
    this.approvalPending.set(null);
    this.staffingPreview.set(null);
    this.staffingReceipt.set(null);
    this.staffingPending.set(null);
    this.staffingConfirmationReviewed = false;
    this.approvalConfirmationReviewed = false;
    this.preparationReviewed = false;
    this.selectedUser = '';
    this.selectedLevel = 'STAFF_ASSOCIATE';
    this.reviewed = false;
    this.budgetLines = [];
    this.currency = '';
    this.budgetBaseline.set(JSON.stringify(this.budgetModel()));
    this.budgetReviewed = false;
    this.revokeTarget.set(null);
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
    if (this.destroyed || !guid.test(id) || !this.session.current()?.staff) return;
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
            if (planning.canPrepareBudget) {
              const pending = this.drafts.readPendingRequest(this.pendingBudgetScope());
              if (pending.state === 'ready') {
                this.budgetPending.set(pending.draft.value);
                this.uncertain.set(true);
                this.commandStatus.set(
                  'A prior budget request needs reconciliation. Verify its retained receipt before another change.',
                );
              }
            } else {
              this.budgetPending.set(null);
              this.budgetReceipt.set(null);
              this.budgetPreview.set(null);
              this.uncertain.set(false);
            }
            if (planning.canManageStaffing) {
              const staffing = this.drafts.readPendingRequest(this.pendingStaffingScope());
              if (staffing.state === 'ready') {
                this.staffingPending.set(staffing.draft.value);
                this.uncertain.set(true);
                this.commandStatus.set(
                  'A prior staffing request needs reconciliation. Verify its retained receipt before another change.',
                );
              }
              const pending = this.drafts.readPendingRequest(this.pendingApprovalScope());
              if (pending.state === 'ready') {
                this.approvalPending.set(pending.draft.value);
                this.uncertain.set(true);
                this.commandStatus.set(
                  'A prior approval needs reconciliation. Verify its retained receipt before another change.',
                );
              }
            } else {
              this.staffingPreview.set(null);
              this.staffingReceipt.set(null);
              this.staffingPending.set(null);
              this.staffingConfirmationReviewed = false;
              this.approvalPending.set(null);
              this.approvalReceipt.set(null);
              this.approvalPreview.set(null);
            }
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
