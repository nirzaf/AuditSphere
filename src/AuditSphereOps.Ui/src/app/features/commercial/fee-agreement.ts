import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { CommercialFormDraft, textFields } from './commercial-form-draft';
import { exactDecimal, guidPattern } from '../../core/contracts';
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
}
const guid = (v: unknown): v is string => typeof v === 'string' && guidPattern.test(v);
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
    (v['agreementId'] === null ? v['milestones'].length !== 0 : v['milestones'].length !== 2)
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
@Component({
  selector: 'audit-fee-agreement',
  imports: [FormsModule, RouterLink, MatButtonModule, MatProgressBarModule],
  template: `
    <section aria-labelledby="fee-agreement-heading">
      <h2 id="fee-agreement-heading">Agreed fee and billing milestones</h2>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" aria-label="Loading fee agreement" />
      }
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
      <button matButton [disabled]="busy()" (click)="load()">Refresh fee agreement</button>
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
          ><input type="checkbox" [(ngModel)]="reviewed" [disabled]="busy()" />I reviewed this fee,
          milestone and selected action.</label
        >
        @if (!w.agreementId) {
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
          <div class="table-scroll">
            <table>
              <caption>
                Persisted fee milestones
              </caption>
              <thead>
                <tr>
                  <th>Milestone</th>
                  <th>Amount</th>
                  <th>State</th>
                  <th>Invoice</th>
                  <th>Paid</th>
                  <th>Outstanding</th>
                </tr>
              </thead>
              <tbody>
                @for (m of w.milestones; track m.id) {
                  <tr>
                    <td>{{ m.kind }}</td>
                    <td>{{ m.amount }} {{ w.currency }}</td>
                    <td>{{ m.state }}</td>
                    <td>
                      @if (m.invoiceId) {
                        <a [routerLink]="['/app/practice/invoices', m.invoiceId]">{{
                          m.invoiceNumber ?? 'Open invoice'
                        }}</a>
                        · {{ m.invoiceStatus }}
                      } @else {
                        Not invoiced
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
                  Draft advance invoice
                </button>
              } @else if (m.state !== 'PAID') {
                @if (m.invoiceStatus === 'POSTED' || m.invoiceStatus === 'SENT') {
                  <h3>Record manual advance payment</h3>
                  <label
                    >Amount received<input
                      [(ngModel)]="amount"
                      (ngModelChange)="reviewed = false"
                      inputmode="decimal"
                      [disabled]="busy()"
                  /></label>
                  <label
                    >Bank or transfer reference<input
                      [(ngModel)]="reference"
                      (ngModelChange)="reviewed = false"
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
                      (ngModelChange)="reviewed = false"
                      inputmode="decimal"
                      [disabled]="busy()"
                  /></label>
                  <label
                    >Bank or transfer reference<input
                      [(ngModel)]="reference"
                      (ngModelChange)="reviewed = false"
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
})
export class FeeAgreement {
  readonly proposalId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private read?: Subscription;
  private write?: Subscription;
  private fence = 0;
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
  saveTabDraft(): boolean {
    if (!this.data()?.canFinance || this.busy() || this.uncertain()) return false;
    const saved = this.tabDraft.save();
    this.message.set(
      saved
        ? 'Unsubmitted fee fields saved without review confirmation.'
        : 'Tab draft could not be saved.',
    );
    return saved;
  }
  recoverTabDraft(): void {
    const w = this.data();
    if (!w?.canFinance || this.busy() || this.uncertain()) return;
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
  constructor() {
    effect(() => {
      this.proposalId();
      this.session.invalidation();
      const staff = this.session.current()?.staff;
      untracked(() => {
        this.fence++;
        this.tabDraft.reset();
        this.read?.unsubscribe();
        this.write?.unsubscribe();
        this.data.set(null);
        this.busy.set(false);
        this.uncertain.set(false);
        this.message.set('');
        this.reviewed = false;
        this.amount = this.reference = this.engagementId = '';
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.fence++;
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
      true,
    );
  }
  link(): void {
    const w = this.data();
    if (w?.agreementId && w.engagements.some((e) => e.id === this.engagementId))
      this.command(
        '/api/ui/fee-agreements/' + w.agreementId + '/engagement',
        { engagementId: this.engagementId, reviewed: this.reviewed },
        true,
      );
  }
  private command(url: string, body: object, noContent = false, payment = false): void {
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
          };
          if (
            !noContent &&
            (!guid(r?.id) ||
              (payment &&
                (typeof r.paid !== 'boolean' ||
                  typeof r.emailQueued !== 'boolean' ||
                  !(r.receiptDocumentId === null || guid(r.receiptDocumentId)))))
          ) {
            this.uncertain.set(true);
            this.data.set(null);
            this.message.set('Outcome unconfirmed. Refresh persisted fee state.');
            return;
          }
          this.message.set(
            payment
              ? r.paid
                ? r.emailQueued
                  ? 'Advance paid. Receipt notification queued; delivery remains external.'
                  : 'Advance paid. Review receipt/profile/contact availability.'
                : 'Partial payment recorded. Review remaining outstanding amount.'
              : 'Fee action recorded. Invoice drafts still require finance review and posting.',
          );
          if (payment) this.reference = '';
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
    if (!guid(this.proposalId()) || !this.session.current()?.staff) return;
    const fence = this.fence;
    this.loading.set(true);
    this.read = this.http
      .get<unknown>('/api/ui/proposals/' + this.proposalId() + '/fee-agreement')
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          try {
            const w = decodeFeeAgreement(value);
            if (w.proposalId !== this.proposalId()) throw new Error('Wrong proposal');
            this.data.set(w);
            if (!this.amount)
              this.amount = w.milestones.find((m) => m.kind === 'ADVANCE')?.outstanding ?? '';
            void this.tabDraft.bind(`commercial-fee:${w.proposalId}`, w);
          } catch {
            this.error.set('Unsupported fee agreement response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.loading.set(false);
          this.error.set(
            'Fee agreement unavailable. Current firm-wide commercial access is required.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
