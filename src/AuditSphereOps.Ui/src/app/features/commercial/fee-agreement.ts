import { Component, DestroyRef, HostListener, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom, Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { CommercialFormDraft, textFields } from './commercial-form-draft';
import { exactDecimal, guidPattern } from '../../core/contracts';
import { StatusChip } from '../../core/ui';
interface Milestone {
  id: string;
  kind: string;
  amount: string;
  state: string;
  invoiceId: string | null;
  invoiceNumber: string | null;
  invoiceStatus: string | null;
  allocated: string;
  outstanding: string;
  paidAt: string | null;
}
interface Engagement {
  id: string;
  serviceRoute: string;
  periodStart: string;
  periodEnd: string;
}
interface Workspace {
  proposalId: string;
  agreementId: string | null;
  clientId: string | null;
  clientName?: string | null;
  engagementId: string | null;
  fee: string;
  currency: string;
  advancePercent: string;
  canCreate: boolean;
  canFinance: boolean;
  releaseRecorded: boolean;
  creationBlockers: string[];
  milestones: Milestone[];
  engagements: Engagement[];
  advancePreparation: AdvancePreparation;
}
/** The nine states of the 50% advance invoice; mirrors AdvanceInvoicePreparationStates on the server (STE 4.1.5). */
export const advancePreparationStates = [
  'NOT_APPLICABLE', 'AWAITING_ENGAGEMENT_LETTER', 'PENDING_AUTOMATION_DISABLED', 'AWAITING_AUTOMATION', 'QUEUED',
  'BLOCKED', 'DRAFT_CREATED', 'CANCELLED', 'OFFICIAL_INVOICE',
] as const;
export type AdvancePreparationState = (typeof advancePreparationStates)[number];
/** Explicit state of the 50% advance invoice. Pending is shown as pending, never as complete. */
export interface AdvancePreparation {
  state: AdvancePreparationState;
  message: string;
}
const guid = (v: unknown): v is string => typeof v === 'string' && guidPattern.test(v);
const advancePreparationValid = (value: unknown): boolean => {
  if (!value || typeof value !== 'object') return false;
  const p = value as Record<string, unknown>;
  return (advancePreparationStates as readonly unknown[]).includes(p['state']) &&
    typeof p['message'] === 'string' && p['message'].length > 0 && p['message'].length <= 500;
};
export function decodeFeeAgreement(value: unknown): Workspace {
  if (!value || typeof value !== 'object') throw new Error('Invalid fee agreement');
  const v = value as Record<string, unknown>;
  if (
    !guid(v['proposalId']) ||
    !['agreementId', 'clientId', 'engagementId'].every((k) => v[k] === null || guid(v[k])) ||
    !exactDecimal(v['fee']) ||
    !exactDecimal(v['advancePercent']) ||
    typeof v['currency'] !== 'string' ||
    !['canCreate', 'canFinance', 'releaseRecorded'].every((k) => typeof v[k] === 'boolean') ||
    !Array.isArray(v['creationBlockers']) ||
    v['creationBlockers'].length > 20 ||
    !v['creationBlockers'].every((x) => typeof x === 'string') ||
    !Array.isArray(v['milestones']) ||
    !Array.isArray(v['engagements']) ||
    v['engagements'].length > 100 ||
    (v['agreementId'] === null ? v['milestones'].length !== 0 : v['milestones'].length !== 2) ||
    !advancePreparationValid(v['advancePreparation'])
  )
    throw new Error('Invalid fee agreement');
  for (const m of v['milestones'])
    if (
      !m ||
      !guid(m.id) ||
      !['ADVANCE', 'BALANCE'].includes(m.kind) ||
      typeof m.state !== 'string' ||
      !['amount', 'allocated', 'outstanding'].every((k) => exactDecimal(m[k])) ||
      !(m.invoiceId === null || guid(m.invoiceId)) ||
      !['invoiceNumber', 'invoiceStatus', 'paidAt'].every(
        (k) => m[k] === null || typeof m[k] === 'string',
      )
    )
      throw new Error('Invalid milestone');
  if (new Set(v['milestones'].map((m) => m.kind)).size !== v['milestones'].length)
    throw new Error('Duplicate milestone');
  for (const e of v['engagements'])
    if (
      !e ||
      !guid(e.id) ||
      !['serviceRoute', 'periodStart', 'periodEnd'].every((k) => typeof e[k] === 'string')
    )
      throw new Error('Invalid engagement');
  return v as unknown as Workspace;
}
const exactKeys = (value: unknown, keys: readonly string[]): value is Record<string, unknown> =>
  !!value && typeof value === 'object' && !Array.isArray(value) &&
  Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key));
function decodeFinanceFeeAgreement(value: unknown, expectedAgreementId: string): Workspace {
  const workspaceKeys = [
    'proposalId', 'agreementId', 'clientId', 'clientName', 'engagementId', 'fee', 'currency', 'advancePercent',
    'canCreate', 'canFinance', 'releaseRecorded', 'creationBlockers', 'milestones', 'engagements',
    'advancePreparation',
  ];
  if (!exactKeys(value, workspaceKeys)) throw new Error('Invalid finance fee agreement');
  const raw = value;
  const milestones = raw['milestones'];
  const engagements = raw['engagements'];
  const creationBlockers = raw['creationBlockers'];
  const clientName = raw['clientName'];
  if (!Array.isArray(milestones) || milestones.some((item) => !exactKeys(item, [
    'id', 'kind', 'amount', 'state', 'invoiceId', 'invoiceNumber', 'invoiceStatus', 'allocated',
    'outstanding', 'paidAt',
  ])) || !Array.isArray(creationBlockers) || creationBlockers.length !== 0 ||
      !Array.isArray(engagements) || engagements.length !== 0 ||
      !exactKeys(raw['advancePreparation'], ['state', 'message']))
    throw new Error('Invalid sanitized finance fee agreement');
  const workspace = decodeFeeAgreement(value);
  if (workspace.agreementId !== expectedAgreementId || workspace.canCreate || !workspace.canFinance ||
      !guid(workspace.clientId) || typeof clientName !== 'string' || clientName.trim().length === 0 ||
      clientName.length > 300 || new Set(workspace.engagements.map((engagement) => engagement.id)).size !==
        workspace.engagements.length)
    throw new Error('Unexpected finance fee agreement identity or authority');
  return workspace;
}
@Component({
  selector: 'audit-fee-agreement',
  imports: [FormsModule, RouterLink, MatButtonModule, MatProgressBarModule, StatusChip],
  template: `
    @if (financeMode()) { <a routerLink="/app/finance/fee-agreements">← Back to fee settlements</a> }
    <section [class.finance-mode]="financeMode()" aria-labelledby="fee-agreement-heading">
      <h2 id="fee-agreement-heading">{{ financeMode() ? 'Fee settlement' : 'Agreed fee and billing milestones' }}</h2>
      @if (financeMode()) {
        @if (data(); as identity) {
          <dl class="finance-identity">
            <div><dt>Client</dt><dd>{{ identity.clientName }}</dd></div>
            <div><dt>Client ID</dt><dd><code>{{ identity.clientId }}</code></dd></div>
            <div><dt>Fee agreement ID</dt><dd><code>{{ identity.agreementId }}</code></dd></div>
          </dl>
        }
      }
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" aria-label="Loading fee agreement" />
      }
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
      <button matButton [disabled]="busy()" (click)="load()">Refresh fee agreement</button>
      @if (!financeMode()) {
        <button
          matButton
          [disabled]="busy() || uncertain() || !data()?.canFinance || !tabDraft.scope()"
          (click)="saveTabDraft()"
        >
          Save fee tab draft
        </button>
        <button
          matButton
          [disabled]="busy() || uncertain() || !data()?.canFinance || !tabDraft.scope()"
          (click)="recoverTabDraft()"
        >
          Recover fee tab draft
        </button>
      } @else {
        <button
          matButton
          [disabled]="busy() || uncertain() || !data()?.canFinance || !financeTabDraft.scope()"
          (click)="saveFinanceTabDraft()"
        >
          Save fee settlement draft
        </button>
        <button
          matButton
          [disabled]="busy() || uncertain() || !data()?.canFinance || !financeTabDraft.scope()"
          (click)="recoverFinanceTabDraft()"
        >
          Recover fee settlement draft
        </button>
      }
      @if (data(); as w) {
        <p>
          Fee {{ w.fee }} {{ w.currency }} · {{ w.advancePercent }}% advance; remainder on final
          delivery and sign-off.
        </p>
        <p>
          Invoices follow firm finance review and posting. Payments are recorded manually; this does
          not initiate a bank payment.
        </p>
        <label
          ><input type="checkbox" [(ngModel)]="reviewed" (ngModelChange)="feeAssentChanged()" [disabled]="busy()" />I reviewed this fee,
          milestone and selected action.</label
        >
        @if (!w.agreementId) {
          <p>
            <audit-status [value]="w.advancePreparation.state" />
            <small role="status" data-testid="advance-preparation">{{ w.advancePreparation.message }}</small>
          </p>
          <ul>
            @for (b of w.creationBlockers; track b) {
              <li>{{ b }}</li>
            }
          </ul>
          <button
            matButton
            [disabled]="!w.canCreate || !reviewed || busy() || uncertain()"
            (click)="create()"
          >
            Create fee agreement
          </button>
        } @else {
          <div class="table-scroll"
            [attr.role]="financeMode() ? 'region' : null"
            [attr.aria-label]="financeMode() ? 'Fee milestone details' : null"
            [attr.tabindex]="financeMode() ? 0 : null">
            <table>
              <caption>
                Persisted fee milestones
              </caption>
              <thead>
                <tr>
                  <th scope="col">Milestone</th>
                  <th scope="col">Amount</th>
                  <th scope="col">State</th>
                  <th scope="col">Invoice</th>
                  <th scope="col">Paid</th>
                  <th scope="col">Outstanding</th>
                </tr>
              </thead>
              <tbody>
                @for (m of w.milestones; track m.id) {
                  <tr>
                    <td>
                      {{ m.kind }}
                      @if (m.kind === 'ADVANCE') {
                        <div>
                          <audit-status [value]="w.advancePreparation.state" />
                        </div>
                        <small role="status" data-testid="advance-preparation">{{ w.advancePreparation.message }}</small>
                      }
                    </td>
                    <td>{{ m.amount }} {{ w.currency }}</td>
                    <td>{{ m.state }}</td>
                    <td>
                      @if (m.invoiceId) {
                        <a [routerLink]="['/app/practice/invoices', m.invoiceId]">{{
                          m.invoiceNumber ?? 'Open invoice'
                        }}</a>
                        · {{ m.invoiceStatus }}
                      } @else {
                        Not linked to an invoice
                      }
                    </td>
                    <td>{{ m.allocated }}</td>
                    <td>{{ m.outstanding }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (!w.canFinance) {
            <p>
              A current FinanceManager or FinanceReviewer assignment for this client is required for
              invoice and payment commands.
            </p>
          }
          @for (m of w.milestones; track m.id) {
            @if (m.kind === 'ADVANCE') {
              @if (!m.invoiceId) {
                <button
                  matButton
                  [disabled]="!w.canFinance || !reviewed || busy() || uncertain()"
                  (click)="invoice('advance')"
                >
                  {{ financeMode() ? 'Prepare or recover advance invoice' : 'Draft advance invoice' }}
                </button>
              } @else if (m.state !== 'PAID') {
                @if (m.invoiceStatus === 'POSTED' || m.invoiceStatus === 'SENT') {
                  <h3>Record manual advance payment</h3>
                  <label
                    >Amount received<input
                      [(ngModel)]="amount"
                      (ngModelChange)="feeFieldsChanged()"
                      inputmode="decimal"
                      [disabled]="busy()"
                  /></label>
                  <label
                    >Bank or transfer reference<input
                      [(ngModel)]="reference"
                      (ngModelChange)="feeFieldsChanged()"
                      maxlength="120"
                      [disabled]="busy()"
                  /></label>
                  <p>
                    A reference records one payment. Reusing it with a different amount is refused.
                    A fully paid advance may generate a receipt and queue one notification; queued
                    does not mean delivered.
                  </p>
                  <button
                    matButton
                    [disabled]="
                      !w.canFinance || !reviewed || !reference.trim() || busy() || uncertain()
                    "
                    (click)="payment()"
                  >
                    Record advance payment
                  </button>
                } @else {
                  <p>
                    The advance invoice must be reviewed and posted in firm finance before recording
                    payment.
                  </p>
                }
              } @else {
                <p>Advance paid {{ m.paidAt }}.</p>
              }
            }
            @if (m.kind === 'BALANCE' && !m.invoiceId) {
              <button
                matButton
                [disabled]="
                  !w.canFinance ||
                  !w.releaseRecorded ||
                  !advancePaid() ||
                  !reviewed ||
                  busy() ||
                  uncertain()
                "
                (click)="invoice('balance')"
              >
                Draft balance invoice
              </button>
              @if (!w.releaseRecorded || !advancePaid()) {
                <p>
                  The advance must be paid and the linked engagement must have an issued final
                  release before balance invoicing.
                </p>
              }
            }
            @if (m.kind === 'BALANCE' && m.invoiceId) {
              @if (m.state !== 'PAID') {
                @if (m.invoiceStatus === 'POSTED' || m.invoiceStatus === 'SENT') {
                  <label
                    >Amount received<input
                      [(ngModel)]="amount"
                      (ngModelChange)="feeFieldsChanged()"
                      inputmode="decimal"
                      [disabled]="busy()"
                  /></label>
                  <label
                    >Bank or transfer reference<input
                      [(ngModel)]="reference"
                      (ngModelChange)="feeFieldsChanged()"
                      maxlength="120"
                      [disabled]="busy()"
                  /></label>
                  <button
                    matButton
                    [disabled]="
                      !w.canFinance || !reviewed || !reference.trim() || busy() || uncertain()
                    "
                    (click)="payment('balance')"
                  >
                    Record balance payment
                  </button>
                } @else {
                  <p>
                    The balance invoice must be reviewed and posted in firm finance before recording
                    payment.
                  </p>
                }
              } @else {
                <p>Balance paid {{ m.paidAt }}.</p>
              }
            }
          }
          @if (!financeMode()) {
            @if (!w.engagementId) {
              <h3>Link accepted engagement</h3>
              <label
                >Engagement<select
                  [(ngModel)]="engagementId"
                  (ngModelChange)="reviewed = false"
                  [disabled]="busy()"
                >
                  <option value="">Choose an engagement</option>
                  @for (e of w.engagements; track e.id) {
                    <option [value]="e.id">
                      {{ e.serviceRoute }} · {{ e.periodStart }} to {{ e.periodEnd }}
                    </option>
                  }
                </select></label
              >
              <p>
                Only this client's engagements are listed. A link cannot be changed after it is
                recorded.
              </p>
              <button
                matButton
                [disabled]="!engagementId || !reviewed || busy() || uncertain()"
                (click)="link()"
              >
                Link engagement
              </button>
              @if (!w.engagements.length) {
                <p>No engagements are available for this client yet.</p>
              }
            } @else {
              <p>
                Linked engagement <code>{{ w.engagementId }}</code> · final release
                {{ w.releaseRecorded ? 'Recorded' : 'Not recorded' }}.
              </p>
            }
          } @else if (w.engagementId) {
            <p>
              Linked engagement <code>{{ w.engagementId }}</code> · final release
              {{ w.releaseRecorded ? 'Recorded' : 'Not recorded' }}.
            </p>
          }
        }
        @if (uncertain()) {
          <button matButton [disabled]="busy()" (click)="acknowledge()">
            I reviewed the persisted fee outcome
          </button>
        }
      }
      <p role="status">{{ message() }}</p>
    </section>
  `,
  styles: `
    :host { display:block; min-width:0; max-width:100%; }
    section { min-width:0; max-width:100%; }
    section.finance-mode { overflow-wrap:anywhere; }
    .finance-mode .finance-identity { display:grid; gap:.4rem; min-width:0; }
    .finance-mode .finance-identity > div { min-width:0; }
    .finance-mode .finance-identity dd { margin-inline-start:0; overflow-wrap:anywhere; }
    .finance-mode .finance-identity code { overflow-wrap:anywhere; }
    .finance-mode .table-scroll { min-width:0; max-width:100%; overflow-x:auto; overscroll-behavior-inline:contain; }
    .finance-mode table { width:100%; min-width:42rem; border-collapse:collapse; }
    .finance-mode th, .finance-mode td { padding:.55rem .65rem; text-align:start; vertical-align:top; overflow-wrap:anywhere; }
  `,
})
export class FeeAgreement implements NavigationProtected {
  readonly proposalId = input('');
  readonly agreementId = input('');
  readonly financeMode = computed(() => !this.proposalId() && !!this.agreementId());
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly dialog = inject(MatDialog);
  private read?: Subscription;
  private write?: Subscription;
  private fence = 0;
  private destroyed = false;
  private navigationDialog: MatDialogRef<UnsavedChangesDialog> | null = null;
  private readonly navigationDialogOwner = signal<string | null>(null);
  readonly data = signal<Workspace | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly message = signal('');
  readonly tabDraft = new CommercialFormDraft(
    () => ({ amount: this.amount, reference: this.reference, engagementId: this.engagementId }),
    (v) => textFields(v, { amount: 40, reference: 120, engagementId: 36 }),
  );
  readonly financeTabDraft = new CommercialFormDraft(
    () => ({ amount: this.amount, reference: this.reference }),
    (v) => textFields(v, { amount: 40, reference: 120 }),
  );
  saveTabDraft(): boolean {
    if (this.financeMode() || !this.data()?.canFinance || this.busy() || this.uncertain()) return false;
    const saved = this.tabDraft.save();
    this.message.set(
      saved
        ? 'Unsubmitted fee fields saved without review confirmation.'
        : 'Tab draft could not be saved.',
    );
    return saved;
  }
  saveFinanceTabDraft(): boolean {
    if (!this.financeMode() || !this.data()?.canFinance || this.busy() || this.uncertain()) return false;
    const saved = this.financeTabDraft.save();
    this.message.set(
      saved
        ? 'Unsubmitted fee settlement fields saved without review confirmation.'
        : 'Fee settlement draft could not be saved.',
    );
    return saved;
  }
  recoverFinanceTabDraft(): void {
    const w = this.data();
    if (!this.financeMode() || !w?.canFinance || this.busy() || this.uncertain()) return;
    const fields = this.financeTabDraft.recover();
    if (fields) {
      this.amount = fields.amount;
      this.reference = fields.reference;
      this.engagementId = '';
      this.reviewed = false;
      this.message.set('Fee settlement fields recovered. Review the current milestone and action again.');
    } else this.message.set('No compatible fee settlement draft is available.');
  }
  recoverTabDraft(): void {
    const w = this.data();
    if (this.financeMode() || !w?.canFinance || this.busy() || this.uncertain()) return;
    const fields = this.tabDraft.recover();
    if (
      fields &&
      (!fields.engagementId || w.engagements.some((e) => e.id === fields.engagementId))
    ) {
      this.amount = fields.amount;
      this.reference = fields.reference;
      this.engagementId = fields.engagementId;
      this.reviewed = false;
      this.message.set('Fee fields recovered. Review the current milestone and action again.');
    } else this.message.set('No compatible fee tab draft is available.');
  }
  reviewed = false;
  amount = '';
  reference = '';
  engagementId = '';
  private identity(): { mode: 'proposal' | 'agreement'; id: string } | null {
    const proposalId = this.proposalId();
    const agreementId = this.agreementId();
    if (guid(proposalId) && agreementId === '') return { mode: 'proposal', id: proposalId };
    if (guid(agreementId) && proposalId === '') return { mode: 'agreement', id: agreementId };
    return null;
  }
  private navigationOwner(): string {
    const session = this.session.current();
    const scope = this.financeTabDraft.scope();
    return JSON.stringify({
      fence: this.fence,
      identity: this.identity(),
      sessionEpoch: this.session.invalidation(),
      session: session && [session.firmId, session.userId, session.generation, session.staff],
      scope: scope && [scope.entity, scope.baseRevision],
      workspace: this.data(),
      draft: this.financeTabDraft.recover(),
      fields: [this.amount, this.reference, this.engagementId],
      reviewed: this.reviewed,
      busy: this.busy(),
      uncertain: this.uncertain(),
    });
  }
  private closeNavigationDialog(): void {
    this.navigationDialogOwner.set(null);
    const dialog = this.navigationDialog;
    this.navigationDialog = null;
    dialog?.close();
  }
  private invalidateNavigationOwner(): void {
    this.fence++;
    this.closeNavigationDialog();
  }
  feeFieldsChanged(): void {
    this.reviewed = false;
    if (this.navigationDialogOwner()) this.closeNavigationDialog();
  }
  feeAssentChanged(): void {
    if (this.navigationDialogOwner()) this.closeNavigationDialog();
  }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.financeMode() && (this.busy() || this.uncertain() || this.financeTabDraft.dirty())) {
      event.preventDefault(); event.returnValue = '';
    }
  }

  async confirmNavigation(): Promise<boolean> {
    if (!this.financeMode()) return true;
    if (this.navigationDialog) return false;
    if (this.busy() || this.uncertain()) {
      this.message.set(this.busy()
        ? 'A fee action is in progress. Wait for it to finish before leaving this page.'
        : 'The fee action outcome is unconfirmed. Refresh and reconcile persisted state before leaving this page.');
      return false;
    }
    if (!this.financeTabDraft.dirty()) return true;
    if (!this.financeTabDraft.scope() || !this.data()?.canFinance) {
      this.message.set('The current finance scope is still loading. Keep this page open until it is ready.');
      return false;
    }
    const owner = this.navigationOwner();
    const dialog = this.dialog.open(UnsavedChangesDialog);
    this.navigationDialog = dialog;
    this.navigationDialogOwner.set(owner);
    const choice = await firstValueFrom(dialog.afterClosed());
    const ownsResponse = this.navigationDialog === dialog && this.navigationDialogOwner() === owner;
    if (this.navigationDialog === dialog) {
      this.navigationDialog = null;
      if (this.navigationDialogOwner() === owner) this.navigationDialogOwner.set(null);
    }
    if (!ownsResponse || this.destroyed || owner !== this.navigationOwner() || this.busy() || this.uncertain()) return false;
    if (choice === 'save') {
      const saved = this.saveFinanceTabDraft();
      if (saved) this.reviewed = false;
      return saved;
    }
    if (choice === 'discard') {
      const discarded = this.financeTabDraft.discard();
      if (!discarded) this.message.set('The fee settlement draft could not be cleared. Keep this page open.');
      if (discarded) this.reviewed = false;
      return discarded;
    }
    return false;
  }
  constructor() {
    effect(() => {
      const owner = this.navigationDialogOwner();
      if (owner !== null && owner !== this.navigationOwner())
        untracked(() => this.closeNavigationDialog());
    });
    effect(() => {
      this.proposalId();
      this.agreementId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.invalidateNavigationOwner();
        this.tabDraft.reset();
        this.financeTabDraft.reset();
        this.read?.unsubscribe();
        this.write?.unsubscribe();
        this.data.set(null);
        this.loading.set(false);
        this.busy.set(false);
        this.uncertain.set(false);
        this.message.set('');
        this.reviewed = false;
        this.amount = this.reference = this.engagementId = '';
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.invalidateNavigationOwner();
      this.read?.unsubscribe();
      this.write?.unsubscribe();
    });
  }
  advancePaid(): boolean {
    return this.data()?.milestones.some((m) => m.kind === 'ADVANCE' && m.state === 'PAID') ?? false;
  }
  acknowledge(): void {
    if (this.data() && !this.busy()) {
      this.uncertain.set(false);
      this.reviewed = false;
      this.message.set(
        'Persisted fee state reviewed. Review the current action before proceeding.',
      );
    }
  }
  create(): void {
    if (this.financeMode() || !guid(this.proposalId()) || this.agreementId()) return;
    this.command('/api/ui/proposals/' + this.proposalId() + '/fee-agreement', {
      reviewed: this.reviewed,
    });
  }
  invoice(kind: 'advance' | 'balance'): void {
    const w = this.data();
    if (w?.agreementId && w.canFinance)
      this.command('/api/ui/fee-agreements/' + w.agreementId + '/' + kind + '-invoice', {
        reviewed: this.reviewed,
      });
  }
  payment(kind: 'advance' | 'balance' = 'advance'): void {
    const w = this.data();
    if (!w?.agreementId || !w.canFinance) return;
    if (!exactDecimal(this.amount)) {
      this.message.set('Enter an exact decimal amount.');
      return;
    }
    this.command(
      '/api/ui/fee-agreements/' + w.agreementId + '/' + kind + '-payment',
      { amount: this.amount, reference: this.reference, reviewed: this.reviewed },
      false,
      kind,
    );
  }
  link(): void {
    const w = this.data();
    if (!this.financeMode() && w?.agreementId && w.engagements.some((e) => e.id === this.engagementId))
      this.command(
        '/api/ui/fee-agreements/' + w.agreementId + '/engagement',
        { engagementId: this.engagementId, reviewed: this.reviewed },
        true,
      );
  }
  private command(url: string, body: object, noContent = false, payment: false | 'advance' | 'balance' = false): void {
    if (!this.data() || !this.reviewed || this.busy() || this.uncertain()) return;
    const fence = this.fence;
    this.busy.set(true);
    this.message.set('Saving reviewed fee action…');
    this.write = this.http
      .post<unknown>(url, body)
      .pipe(timeout(30000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          this.reviewed = false;
          const r = value as {
            id?: unknown;
            paid?: unknown;
            emailQueued?: unknown;
            receiptDocumentId?: unknown;
            voucherDocumentId?: unknown;
            voucherEmailQueued?: unknown;
          };
          if (
            !noContent &&
            (!guid(r?.id) ||
              (payment &&
                (typeof r.paid !== 'boolean' ||
                  typeof r.emailQueued !== 'boolean' ||
                  !(r.receiptDocumentId === null || guid(r.receiptDocumentId)) ||
                  typeof r.voucherEmailQueued !== 'boolean' ||
                  !(r.voucherDocumentId === null || guid(r.voucherDocumentId)))))
          ) {
            this.uncertain.set(true);
            this.data.set(null);
            this.message.set('Outcome unconfirmed. Refresh persisted fee state.');
            return;
          }
          // Every payment has its own voucher; a missing voucher means the profile or contact needs attention, and recording the same
          // reference again completes it without a second payment.
          const voucher = payment
            ? r.voucherDocumentId
              ? r.voucherEmailQueued
                ? ' Payment voucher issued and its notification queued.'
                : ' Payment voucher issued; no voucher email was queued (check client contact).'
              : ' No payment voucher yet: configure the firm profile, then record the same reference again.'
            : '';
          this.message.set(
            (payment
              ? r.paid
                ? r.emailQueued
                  ? `${payment === 'balance' ? 'Final balance' : 'Advance'} paid. Receipt notification queued; delivery remains external.`
                  : `${payment === 'balance' ? 'Final balance' : 'Advance'} paid. Review receipt/profile/contact availability.`
                : `Partial ${payment === 'balance' ? 'final-balance' : 'advance'} payment recorded. Review remaining outstanding amount.`
              : 'Fee action recorded. Invoice drafts still require finance review and posting.') + voucher,
          );
          if (payment) {
            this.reference = '';
            if (this.financeMode()) this.financeTabDraft.submitted();
          }
          this.load();
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          this.reviewed = false;
          this.uncertain.set(!(failure.status >= 400 && failure.status < 500));
          this.data.set(null);
          this.message.set(
            this.uncertain()
              ? 'Outcome unconfirmed. Refresh and compare persisted invoices/payments before another command.'
              : 'Action not completed. Refresh persisted invoices/payments and check finance scope, accepted fee, invoice status, payment reference or final-release prerequisites.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(): void {
    this.read?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.reviewed = false;
    this.loading.set(false);
    const identity = this.identity();
    if (!this.session.current()?.staff) return;
    if (!identity) {
      this.error.set('Open exactly one fee agreement from a valid proposal or agreement link.');
      return;
    }
    const fence = this.fence;
    this.loading.set(true);
    const endpoint = identity.mode === 'agreement'
      ? '/api/ui/finance/fee-agreements/' + encodeURIComponent(identity.id)
      : '/api/ui/proposals/' + encodeURIComponent(identity.id) + '/fee-agreement';
    this.read = this.http
      .get<unknown>(endpoint)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          try {
            const w = identity.mode === 'agreement'
              ? decodeFinanceFeeAgreement(value, identity.id)
              : decodeFeeAgreement(value);
            if (identity.mode === 'proposal' && w.proposalId !== identity.id) throw new Error('Wrong proposal');
            this.data.set(w);
            if (!this.amount)
              this.amount = w.milestones.find((m) => m.kind === 'ADVANCE')?.outstanding ?? '';
            if (identity.mode === 'proposal') void this.tabDraft.bind(`commercial-fee:${w.proposalId}`, w);
            else void this.financeTabDraft.bind(`finance-fee:${w.agreementId}`, w);
          } catch {
            this.error.set('Unsupported fee agreement response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.loading.set(false);
          this.error.set(identity.mode === 'agreement'
            ? 'Fee settlement unavailable. Current finance access is required.'
            : 'Fee agreement unavailable. Current firm-wide commercial access is required.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
