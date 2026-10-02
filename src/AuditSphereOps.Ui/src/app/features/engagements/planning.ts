import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
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
  candidates: { userId: string; name: string; department: string | null; certified: boolean }[];
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
  imports: [FormsModule, MatButtonModule, MatProgressBarModule],
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
      @for (person of planning.team; track person.assignmentId) {
        <p>
          {{ person.name }} · {{ person.levelLabel }} · {{ person.authorizationRole }} ·
          {{ person.certified ? 'Current certification' : 'No current certification on file' }}
        </p>
        @if (planning.canManageStaffing) {
          <button matButton [disabled]="busy()" (click)="requestRevocation(person)">
            Revoke {{ person.name }}
          </button>
        }
      } @empty {
        <p>Nobody is staffed on this engagement yet.</p>
      }
      @if (planning.canManageStaffing) {
        <section>
          <h3>Assign staff</h3>
          <p role="note">
            When dedicated client-site provisioning is enabled, this assignment authorizes Full
            Control over the entire client SharePoint site, including other engagements, sharing and
            deletion. AuditSphere access remains engagement scoped.
          </p>
          <form (ngSubmit)="assign()">
            <label
              >Person
              <select name="person" [(ngModel)]="selectedUser" [disabled]="busy()">
                <option value="">Select staff</option>
                @for (candidate of planning.candidates; track candidate.userId) {
                  <option [value]="candidate.userId">
                    {{ candidate.name }}{{ candidate.certified ? ' · Certified' : '' }}
                  </option>
                }
              </select></label
            >
            <label
              >Level
              <select name="level" [(ngModel)]="selectedLevel" [disabled]="busy()">
                @for (level of levels; track level.value) {
                  <option [value]="level.value">{{ level.label }}</option>
                }
              </select></label
            >
            <label
              ><input type="checkbox" name="reviewed" [(ngModel)]="reviewed" [disabled]="busy()" />
              I reviewed the engagement role and client-site access.</label
            >
            <button
              matButton
              type="submit"
              [disabled]="busy() || !reviewed || !selectedUser || uncertain()"
            >
              Add to team
            </button>
          </form>
          @if (revokeTarget(); as person) {
            <section role="alert">
              <p>
                Remove {{ person.name }} from this engagement and revoke the staffing role?
                SharePoint removal is scheduled when no active client assignment remains.
              </p>
              <button matButton [disabled]="busy()" (click)="revoke(person.assignmentId)">
                Confirm revocation</button
              ><button matButton [disabled]="busy()" (click)="revokeTarget.set(null)">
                Cancel
              </button>
            </section>
          }
          <p role="status">{{ commandStatus() }}</p>
        </section>
      }
      @if (planning.canManageStaffing) {
        <section aria-label="Budget preparation">
          <h3>Prepare budget version</h3>
          <p>
            Latest version {{ planning.latestBudgetVersion }}. An approved rate card is required for
            each role and activity. Another manager or partner must approve your draft.
          </p>
          <form (ngSubmit)="saveBudget()">
            <label
              >Currency
              <input
                name="currency"
                [(ngModel)]="currency"
                maxlength="3"
                required
                [disabled]="busy()"
            /></label>
            @for (line of budgetLines; track $index; let index = $index) {
              <fieldset>
                <legend>Budget line {{ index + 1 }}</legend>
                <label
                  >Role
                  <input
                    [name]="'role' + index"
                    [(ngModel)]="line.role"
                    maxlength="50"
                    required
                    [disabled]="busy()"
                /></label>
                <label
                  >Activity
                  <input
                    [name]="'activity' + index"
                    [(ngModel)]="line.activity"
                    maxlength="50"
                    required
                    [disabled]="busy()"
                /></label>
                <label
                  >Phase
                  <input
                    [name]="'phase' + index"
                    [(ngModel)]="line.phase"
                    maxlength="50"
                    required
                    [disabled]="busy()"
                /></label>
                <label
                  >Risk area
                  <input
                    [name]="'risk' + index"
                    [(ngModel)]="line.riskArea"
                    maxlength="120"
                    [disabled]="busy()"
                /></label>
                <label
                  >Forecast minutes
                  <input
                    type="number"
                    [name]="'minutes' + index"
                    [(ngModel)]="line.forecastMinutes"
                    min="1"
                    max="10000000"
                    step="1"
                    required
                    [disabled]="busy()"
                /></label>
                <button
                  matButton
                  type="button"
                  [disabled]="busy()"
                  (click)="budgetLines.splice(index, 1)"
                >
                  Remove line {{ index + 1 }}
                </button>
              </fieldset>
            }
            <button
              matButton
              type="button"
              [disabled]="busy() || budgetLines.length >= 200"
              (click)="addBudgetLine()"
            >
              Add line
            </button>
            <button
              matButton
              type="submit"
              [disabled]="busy() || uncertain() || !budgetLines.length"
            >
              Save budget version
            </button>
          </form>
          @if (planning.draft; as draft) {
            <h3>Draft version {{ draft.version }} · {{ draft.currency }}</h3>
            @for (line of draft.lines; track $index) {
              <p>
                {{ line.role }} · {{ line.activity }} · {{ line.phase }} · {{ line.riskArea }} ·
                {{ line.minutes }} minutes · {{ line.cost }}
              </p>
            }
            @if (draft.canApprove) {
              <label
                ><input type="checkbox" [(ngModel)]="budgetReviewed" [disabled]="busy()" /> I
                reviewed this draft and approve its budget.</label
              >
              <button
                matButton
                [disabled]="busy() || uncertain() || !budgetReviewed"
                (click)="approveBudget(draft.id)"
              >
                Approve draft budget
              </button>
            } @else {
              <p>A different authorized manager or partner must approve this draft.</p>
            }
          }
          <p role="status">{{ commandStatus() }}</p>
        </section>
      }
      <h3>Approved budget</h3>
      @if (planning.budget; as budget) {
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
      <a [href]="'/app/engagements/' + engagementId()">Manage staffing and budget</a>
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
  currency = 'QAR';
  budgetReviewed = false;
  budgetLines = [
    { role: 'Senior', activity: 'AUDIT', phase: 'PLANNING', riskArea: '', forecastMinutes: 480 },
  ];
  addBudgetLine(): void {
    this.budgetLines.push({
      role: 'Senior',
      activity: 'AUDIT',
      phase: 'PLANNING',
      riskArea: '',
      forecastMinutes: 480,
    });
  }
  saveBudget(): void {
    const planning = this.data();
    if (!planning?.canManageStaffing || this.busy() || this.uncertain()) return;
    if (
      !this.budgetLines.length ||
      this.budgetLines.some(
        (l) =>
          !Number.isSafeInteger(l.forecastMinutes) ||
          l.forecastMinutes < 1 ||
          l.forecastMinutes > 10000000,
      )
    ) {
      this.commandStatus.set('Enter valid whole forecast minutes for every line.');
      return;
    }
    this.command('budgets', {
      currency: this.currency,
      expectedVersion: planning.latestBudgetVersion,
      lines: this.budgetLines,
    });
  }
  approveBudget(id: string): void {
    if (!this.data()?.draft?.canApprove || !this.budgetReviewed || this.busy() || this.uncertain())
      return;
    this.command('budgets/' + id + '/approve', {});
  }
  selectedUser = '';
  selectedLevel = 'STAFF_ASSOCIATE';
  reviewed = false;
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly commandStatus = signal('');
  readonly revokeTarget = signal<TeamMember | null>(null);
  readonly engagementId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  readonly data = signal<Planning | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  constructor() {
    effect(() => {
      this.engagementId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
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
        this.selectedUser = '';
        this.reviewed = false;
        this.revokeTarget.set(null);
        this.commandStatus.set('');
        this.uncertain.set(false);
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }
  requestRevocation(person: TeamMember): void {
    if (!this.busy()) this.revokeTarget.set(person);
  }
  assign(): void {
    if (
      !this.data()?.canManageStaffing ||
      !this.reviewed ||
      !guid.test(this.selectedUser) ||
      this.busy() ||
      this.uncertain()
    )
      return;
    this.command('staffing', {
      userId: this.selectedUser,
      level: this.selectedLevel,
      reviewedClientSiteAccess: this.reviewed,
    });
  }
  revoke(id: string): void {
    if (!this.data()?.canManageStaffing || !guid.test(id) || this.busy() || this.uncertain())
      return;
    this.command('staffing/' + id + '/revoke', {});
  }
  private command(path: string, body: object): void {
    const id = this.engagementId();
    const generation = this.session.invalidation();
    this.busy.set(true);
    this.commandStatus.set('Saving planning change…');
    this.http
      .post('/api/ui/engagements/' + id + '/' + path, body)
      .pipe(timeout(15000))
      .subscribe({
        next: () => {
          this.busy.set(false);
          if (id !== this.engagementId() || generation !== this.session.invalidation()) return;
          this.selectedUser = '';
          this.reviewed = false;
          this.revokeTarget.set(null);
          this.budgetReviewed = false;
          this.commandStatus.set('Planning change recorded.');
          this.load();
        },
        error: (failure) => {
          this.busy.set(false);
          if (id !== this.engagementId() || generation !== this.session.invalidation()) return;
          if (failure.status >= 400 && failure.status < 500)
            this.commandStatus.set(
              'Planning change refused. Check scope, current revision, approved rates, rank and certification.',
            );
          else {
            this.uncertain.set(true);
            this.commandStatus.set(
              'Outcome unconfirmed. Refresh and review current assignments before another change.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(): void {
    this.request?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    const id = this.engagementId();
    if (!guid.test(id) || !this.session.current()?.staff) return;
    this.loading.set(true);
    const generation = this.session.invalidation();
    this.request = this.http
      .get<unknown>('/api/ui/engagements/' + id + '/planning')
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (generation !== this.session.invalidation()) return;
          try {
            this.data.set(decodePlanning(value));
          } catch {
            this.error.set('Planning returned an unsupported response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          this.loading.set(false);
          this.error.set('Planning unavailable. Check your access or retry.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
