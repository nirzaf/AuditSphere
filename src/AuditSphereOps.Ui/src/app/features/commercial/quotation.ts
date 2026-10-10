import {
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { Drafts } from '../../core/drafts';
import { CommercialFormDraft, textFields } from './commercial-form-draft';
import { exactDecimal, guidPattern } from '../../core/contracts';

export interface QuotationSaveIntent {
  proposalId: string;
  requestId: string;
  proposalRevision: string;
  expectedRevision: string;
  lines: { rateCardId: string; hours: string }[];
  complexity: string;
  risk: string;
  discount: string;
  nonStandardTerms: boolean;
  note: string | null;
  submissionPending: true;
}

export function validQuotationIntent(value: unknown): QuotationSaveIntent | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const v = value as Record<string, unknown>;
  const tf = (k: string, max: number) => {
    const val = v[k];
    return typeof val === 'string' && val.length <= max ? val : null;
  };
  const proposalId = tf('proposalId', 36);
  const requestId = tf('requestId', 36);
  const proposalRevision = tf('proposalRevision', 19);
  const expectedRevision = tf('expectedRevision', 19);
  const complexity = tf('complexity', 40);
  const risk = tf('risk', 40);
  const discount = tf('discount', 40);
  const nonStandardTerms = typeof v['nonStandardTerms'] === 'boolean' ? v['nonStandardTerms'] : null;
  const note =
    v['note'] === null || (typeof v['note'] === 'string' && v['note'].length <= 2000)
      ? (v['note'] as string | null)
      : undefined;
  if (
    !proposalId ||
    !guidPattern.test(proposalId) ||
    !requestId ||
    !guidPattern.test(requestId) ||
    !proposalRevision ||
    !/^\d{1,19}$/.test(proposalRevision) ||
    !expectedRevision ||
    !/^\d{1,19}$/.test(expectedRevision) ||
    !complexity ||
    !exactDecimal(complexity) ||
    !risk ||
    !exactDecimal(risk) ||
    !discount ||
    !exactDecimal(discount) ||
    nonStandardTerms === null ||
    note === undefined ||
    v['submissionPending'] !== true ||
    !Array.isArray(v['lines']) ||
    v['lines'].length < 1 ||
    v['lines'].length > 100
  )
    return null;
  const lines: { rateCardId: string; hours: string }[] = [];
  for (const raw of v['lines']) {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
    const r = raw as Record<string, unknown>;
    const rateCardId =
      typeof r['rateCardId'] === 'string' && r['rateCardId'].length <= 36 ? r['rateCardId'] : null;
    const hours = typeof r['hours'] === 'string' && r['hours'].length <= 40 ? r['hours'] : null;
    if (!rateCardId || !guidPattern.test(rateCardId) || !hours || !exactDecimal(hours)) return null;
    lines.push({ rateCardId, hours });
  }
  return {
    proposalId,
    requestId,
    proposalRevision,
    expectedRevision,
    lines,
    complexity,
    risk,
    discount,
    nonStandardTerms,
    note,
    submissionPending: true,
  };
}
export function quotationDraft(
  value: unknown,
): {
  lines: { rateCardId: string; hours: string }[];
  complexity: string;
  risk: string;
  discount: string;
  nonStandard: boolean;
  note: string;
} | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const v = value as Record<string, unknown>,
    fields = textFields(v, { complexity: 40, risk: 40, discount: 40, note: 2000 });
  if (
    !fields ||
    typeof v['nonStandard'] !== 'boolean' ||
    !Array.isArray(v['lines']) ||
    v['lines'].length < 1 ||
    v['lines'].length > 100
  )
    return null;
  const lines: { rateCardId: string; hours: string }[] = [];
  for (const raw of v['lines']) {
    const line = textFields(raw, { rateCardId: 36, hours: 40 });
    if (!line || (line.rateCardId !== '' && !guidPattern.test(line.rateCardId))) return null;
    lines.push(line);
  }
  return { ...fields, lines, nonStandard: v['nonStandard'] };
}
interface Rate {
  id: string;
  role: string;
  activity: string;
  rate: string;
}
interface AmountLine {
  role: string;
  activity: string;
  hours: string;
  rate: string;
  amount: string;
  rateCardId: string;
}
interface Amounts {
  baseAmount: string;
  complexityAmount: string;
  riskPremiumAmount: string;
  discountAmount: string;
  fee: string;
  lines: AmountLine[];
}
interface Rule {
  key: string;
  role: string;
  requirement: string;
  approved: boolean;
  canApprove: boolean;
  approvedBy: string | null;
  approvedAt: string | null;
  reason: string | null;
  approvalId: string | null;
  canRevoke: boolean;
  revokedAt: string | null;
  revocationReason: string | null;
}
interface Version {
  id: string;
  revision: string;
  status: string;
  complexity: string;
  risk: string;
  discount: string;
  nonStandardTerms: boolean;
  note: string | null;
  validUntil: string | null;
  approvalsStand: boolean;
  expired: boolean;
  amounts: Amounts;
  rules: Rule[];
}
interface Workspace {
  proposalId: string;
  proposalRevision: string;
  currency: string;
  editable: boolean;
  rates: Rate[];
  versions: Version[];
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object';
const revision = (v: unknown): v is string => typeof v === 'string' && /^\d{1,19}$/.test(v);
const identity = (v: unknown): v is string => typeof v === 'string' && guidPattern.test(v);
export function decodeAmounts(value: unknown): Amounts {
  if (
    !object(value) ||
    !['baseAmount', 'complexityAmount', 'riskPremiumAmount', 'discountAmount', 'fee'].every((k) =>
      exactDecimal(value[k]),
    ) ||
    !Array.isArray(value['lines']) ||
    value['lines'].length > 100
  )
    throw new Error('Invalid amounts');
  for (const l of value['lines'])
    if (
      !object(l) ||
      !['role', 'activity'].every((k) => typeof l[k] === 'string') ||
      !['hours', 'rate', 'amount'].every((k) => exactDecimal(l[k])) ||
      !identity(l['rateCardId'])
    )
      throw new Error('Invalid line');
  return value as unknown as Amounts;
}
export function decodeQuotation(value: unknown): Workspace {
  if (
    !object(value) ||
    !identity(value['proposalId']) ||
    !revision(value['proposalRevision']) ||
    typeof value['currency'] !== 'string' ||
    typeof value['editable'] !== 'boolean' ||
    !Array.isArray(value['rates']) ||
    value['rates'].length > 200 ||
    !Array.isArray(value['versions']) ||
    value['versions'].length > 100
  )
    throw new Error('Invalid quotation');
  for (const r of value['rates'])
    if (
      !object(r) ||
      !identity(r['id']) ||
      typeof r['role'] !== 'string' ||
      typeof r['activity'] !== 'string' ||
      !exactDecimal(r['rate'])
    )
      throw new Error('Invalid rate');
  for (const v of value['versions']) {
    if (
      !object(v) ||
      !identity(v['id']) ||
      !revision(v['revision']) ||
      typeof v['status'] !== 'string' ||
      !['complexity', 'risk', 'discount'].every((k) => exactDecimal(v[k])) ||
      typeof v['nonStandardTerms'] !== 'boolean' ||
      !(v['note'] === null || typeof v['note'] === 'string') ||
      !(v['validUntil'] === null || typeof v['validUntil'] === 'string') ||
      typeof v['approvalsStand'] !== 'boolean' ||
      typeof v['expired'] !== 'boolean' ||
      !Array.isArray(v['rules']) ||
      v['rules'].length > 100
    )
      throw new Error('Invalid quotation revision');
    decodeAmounts(v['amounts']);
    for (const r of v['rules'])
      if (
        !object(r) ||
        !['key', 'role', 'requirement'].every((k) => typeof r[k] === 'string') ||
        typeof r['approved'] !== 'boolean' ||
        typeof r['canApprove'] !== 'boolean' ||
        !(r['approvedBy'] === null || identity(r['approvedBy'])) ||
        !(r['approvedAt'] === null || typeof r['approvedAt'] === 'string') ||
        !(r['reason'] === null || typeof r['reason'] === 'string') ||
        !(r['approvalId'] === null || identity(r['approvalId'])) ||
        typeof r['canRevoke'] !== 'boolean' ||
        r['approved'] !== (r['approvalId'] !== null) ||
        !(r['revokedAt'] === null || typeof r['revokedAt'] === 'string') ||
        !(r['revocationReason'] === null || typeof r['revocationReason'] === 'string')
      )
        throw new Error('Invalid approval');
  }
  return value as unknown as Workspace;
}
@Component({
  selector: 'audit-quotation',
  imports: [FormsModule, MatButtonModule, MatProgressBarModule],
  template: `
    <section aria-labelledby="quotation-heading">
      <h2 id="quotation-heading">Calculated quotation</h2>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" aria-label="Loading quotation" />
      }
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
      <button matButton [disabled]="busy()" (click)="load()">Refresh quotation</button>
      <button
        matButton
        [disabled]="busy() || uncertain() || !data()?.editable || !tabDraft.scope()"
        (click)="saveTabDraft()"
      >
        Save quotation tab draft
      </button>
      <button
        matButton
        [disabled]="busy() || uncertain() || !data()?.editable || !tabDraft.scope()"
        (click)="recoverTabDraft()"
      >
        Recover quotation tab draft
      </button>
      @if (data(); as w) {
        @if (w.editable) {
          @if (!w.rates.length) {
            <p>No approved rates in {{ w.currency }}. Approve rate cards before quoting.</p>
          } @else {
            <form (ngSubmit)="preview()">
              <fieldset [disabled]="busy()">
                <legend>Hours at approved rates</legend>
                @for (line of lines; track $index; let i = $index) {
                  <label
                    >Role and activity {{ i + 1
                    }}<select
                      [name]="'rate' + i"
                      [(ngModel)]="line.rateCardId"
                      (ngModelChange)="invalidatePreview()"
                    >
                      @for (r of w.rates; track r.id) {
                        <option [value]="r.id">
                          {{ r.role }} · {{ r.activity }} · {{ r.rate }} {{ w.currency }}/hour
                        </option>
                      }
                    </select></label
                  >
                  <label
                    >Hours {{ i + 1
                    }}<input
                      [name]="'hours' + i"
                      [(ngModel)]="line.hours"
                      inputmode="decimal"
                      required
                      (ngModelChange)="invalidatePreview()"
                  /></label>
                  <button
                    matButton
                    type="button"
                    [disabled]="lines.length === 1"
                    (click)="remove(i)"
                  >
                    Remove line {{ i + 1 }}
                  </button>
                }
                <button matButton type="button" [disabled]="lines.length >= 100" (click)="add()">
                  Add line
                </button>
                <label
                  >Complexity factor (0.5–3)<input
                    name="complexity"
                    [(ngModel)]="complexity"
                    inputmode="decimal"
                    (ngModelChange)="invalidatePreview()"
                /></label>
                <label
                  >Risk premium %<input
                    name="risk"
                    [(ngModel)]="risk"
                    inputmode="decimal"
                    (ngModelChange)="invalidatePreview()"
                /></label>
                <label
                  >Discount %<input
                    name="discount"
                    [(ngModel)]="discount"
                    inputmode="decimal"
                    (ngModelChange)="invalidatePreview()"
                /></label>
                <label
                  ><input
                    type="checkbox"
                    name="nonStandard"
                    [(ngModel)]="nonStandard"
                    (ngModelChange)="invalidatePreview()"
                  />Non-standard terms</label
                >
                @if (nonStandard) {
                  <label
                    >Terms description<textarea
                      name="note"
                      [(ngModel)]="note"
                      maxlength="2000"
                      (ngModelChange)="invalidatePreview()"
                    ></textarea>
                  </label>
                }
                <button matButton type="submit">Calculate preview</button>
              </fieldset>
            </form>
            @if (previewAmounts(); as a) {
              <p role="status">
                Server preview: {{ a.fee }} {{ w.currency }}. Base {{ a.baseAmount }} · complexity
                {{ a.complexityAmount }} · risk {{ a.riskPremiumAmount }} · discount
                {{ a.discountAmount }}
              </p>
              <label
                ><input type="checkbox" [(ngModel)]="reviewed" />I reviewed these inputs and the
                server-calculated total.</label
              >
              <button matButton [disabled]="!reviewed || busy() || uncertain()" (click)="save()">
                Save quotation version
              </button>
            }
          }
        } @else {
          <p>This proposal is read-only. Revise it to reprice.</p>
        }
        @if (uncertain()) {
          <p>
            The previous command outcome was not confirmed. Compare persisted versions and approvals
            before proceeding.
          </p>
          @if (recoverableSave()) {
            <label
              ><input type="checkbox" [(ngModel)]="reviewed" [disabled]="busy()" />I reviewed these
              inputs and confirm the saved quotation request.</label
            >
            <button
              matButton
              [disabled]="!reviewed || busy()"
              (click)="resolveSavedQuotationRequest()"
            >
              Resolve saved quotation request
            </button>
          }
          <button matButton [disabled]="busy()" (click)="acknowledgeOutcome()">
            I reviewed the persisted outcome
          </button>
        }
        <h3>Latest 100 quotation versions</h3>
        @for (v of w.versions; track v.id) {
          <section>
            <h4>
              Revision {{ v.revision }} · {{ v.status }} · {{ v.amounts.fee }} {{ w.currency }}
              @if (v.validUntil) {
                <span> · valid until {{ v.validUntil.slice(0, 16).replace('T', ' ') }} UTC</span>
              }
            </h4>
            @if (v.status === 'APPROVED' && !v.approvalsStand) {
              <p role="alert">
                A required approval was revoked. This revision cannot be dispatched or accepted
                until a fresh approval is recorded.
              </p>
            }
            @if (v.expired) {
              <p role="alert">
                The validity window has elapsed. This offer cannot be dispatched or accepted;
                revise the proposal to issue a fresh quotation.
              </p>
            }
            <div class="table-scroll">
              <table>
                <caption>
                  Revision
                  {{
                    v.revision
                  }}
                  approved-rate breakdown
                </caption>
                <thead>
                  <tr>
                    <th>Role</th>
                    <th>Activity</th>
                    <th>Hours</th>
                    <th>Rate</th>
                    <th>Amount</th>
                    <th>Rate identity</th>
                  </tr>
                </thead>
                <tbody>
                  @for (l of v.amounts.lines; track l.rateCardId) {
                    <tr>
                      <td>{{ l.role }}</td>
                      <td>{{ l.activity }}</td>
                      <td>{{ l.hours }}</td>
                      <td>{{ l.rate }}</td>
                      <td>{{ l.amount }}</td>
                      <td>
                        <code>{{ l.rateCardId }}</code>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <p>
              Base {{ v.amounts.baseAmount }} · complexity ×{{ v.complexity }}:
              {{ v.amounts.complexityAmount }} · risk {{ v.risk }}%:
              {{ v.amounts.riskPremiumAmount }} · discount {{ v.discount }}%:
              {{ v.amounts.discountAmount }}
            </p>
            @if (v.nonStandardTerms) {
              <p>Non-standard terms: {{ v.note }}</p>
            }
            <ul>
              @for (r of v.rules; track r.key) {
                <li>
                  {{ r.role }} · {{ r.requirement }}
                  @if (r.approved) {
                    <p>Approved by {{ r.approvedBy }} at {{ r.approvedAt }}: {{ r.reason }}</p>
                    @if (r.canRevoke) {
                      <label
                        >Revocation reason<textarea
                          [(ngModel)]="revocations[v.id + r.key]"
                          maxlength="1000"
                        ></textarea>
                      </label>
                      <button
                        matButton
                        [disabled]="
                          busy() || uncertain() || (revocations[v.id + r.key] ?? '').trim().length < 5
                        "
                        (click)="revoke(v, r)"
                      >
                        Revoke this approval
                      </button>
                    }
                  } @else if (r.canApprove) {
                    @if (r.revokedAt) {
                      <p>
                        Previous approval revoked at {{ r.revokedAt }}: {{ r.revocationReason }}
                      </p>
                    }
                    <label
                      >Approval reason<textarea
                        [(ngModel)]="reasons[v.id + r.key]"
                        maxlength="1000"
                      ></textarea>
                    </label>
                    <button
                      matButton
                      [disabled]="busy() || uncertain() || !reasons[v.id + r.key]"
                      (click)="approve(v, r)"
                    >
                      Approve as {{ r.role }}
                    </button>
                  } @else {
                    @if (r.revokedAt) {
                      <p>
                        Previous approval revoked at {{ r.revokedAt }}: {{ r.revocationReason }}
                      </p>
                    }
                    <p>Awaiting authorized independent review.</p>
                  }
                </li>
              }
            </ul>
            @if (v.status === 'DRAFT') {
              <button matButton [disabled]="busy() || uncertain()" (click)="submit(v)">
                {{
                  v.rules.length
                    ? 'Submit for approval'
                    : 'Approve quotation (no matrix approval required)'
                }}
              </button>
            }
          </section>
        } @empty {
          <p>No calculated quotations yet.</p>
        }
      }
      <p role="status">{{ message() }}</p>
    </section>
  `,
})
export class Quotation {
  readonly proposalId = input.required<string>();
  readonly changed = output<void>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(Drafts);
  private read?: Subscription;
  private write?: Subscription;
  private fence = 0;
  private refreshParentAfterUncertainSave = false;
  private reviewedBody?: object;
  readonly savePending = signal(false);
  readonly recoverableSave = signal(false);
  private requestId: string = crypto.randomUUID();
  private savedIntent: QuotationSaveIntent | null = null;
  readonly data = signal<Workspace | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly previewAmounts = signal<Amounts | null>(null);
  private intentScope(): string {
    return 'commercial-quotation-save:' + this.proposalId();
  }
  private persistIntent(intent: QuotationSaveIntent): boolean {
    this.drafts.save(this.intentScope(), intent);
    const saved = this.drafts.load(this.intentScope(), validQuotationIntent);
    return !!saved && saved.requestId === intent.requestId;
  }
  readonly tabDraft = new CommercialFormDraft(
    () => ({
      lines: this.lines.map((x) => ({ ...x })),
      complexity: this.complexity,
      risk: this.risk,
      discount: this.discount,
      nonStandard: this.nonStandard,
      note: this.note,
    }),
    quotationDraft,
  );
  saveTabDraft(): boolean {
    if (!this.data()?.editable || this.busy() || this.uncertain()) return false;
    const saved = this.tabDraft.save();
    this.message.set(
      saved
        ? 'Unsubmitted quotation inputs saved without preview or review assent.'
        : 'Tab draft could not be saved.',
    );
    return saved;
  }
  recoverTabDraft(): void {
    const w = this.data();
    if (!w?.editable || this.busy() || this.uncertain()) return;
    const fields = this.tabDraft.recover();
    if (
      fields &&
      fields.lines.every((l) => !l.rateCardId || w.rates.some((r) => r.id === l.rateCardId))
    ) {
      this.lines = fields.lines;
      this.complexity = fields.complexity;
      this.risk = fields.risk;
      this.discount = fields.discount;
      this.nonStandard = fields.nonStandard;
      this.note = fields.note;
      this.invalidatePreview();
      this.message.set(
        'Quotation inputs recovered. Calculate a fresh server preview and review it again.',
      );
    } else this.message.set('No compatible quotation tab draft is available.');
  }
  lines = [{ rateCardId: '', hours: '1' }];
  complexity = '1';
  risk = '0';
  discount = '0';
  nonStandard = false;
  note = '';
  reviewed = false;
  reasons: Record<string, string> = {};
  revocations: Record<string, string> = {};
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
        this.refreshParentAfterUncertainSave = false;
        this.savePending.set(false);
        this.recoverableSave.set(false);
        this.savedIntent = null;
        this.requestId = crypto.randomUUID();
        this.data.set(null);
        this.busy.set(false);
        this.uncertain.set(false);
        this.message.set('');
        this.reasons = {};
        this.revocations = {};
        this.lines = [{ rateCardId: '', hours: '1' }];
        this.complexity = '1';
        this.risk = this.discount = '0';
        this.nonStandard = false;
        this.note = '';
        this.invalidatePreview();
        if (staff) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.fence++;
      this.read?.unsubscribe();
      this.write?.unsubscribe();
    });
  }
  acknowledgeOutcome(): void {
    if (!this.data() || this.busy()) return;
    this.uncertain.set(false);
    this.savePending.set(false);
    this.recoverableSave.set(false);
    this.invalidatePreview();
    this.message.set(
      'Persisted state reviewed. Calculate a fresh preview or choose the current approval action.',
    );
  }
  invalidatePreview(): void {
    this.previewAmounts.set(null);
    this.reviewedBody = undefined;
    this.reviewed = false;
  }
  add(): void {
    if (this.lines.length < 100)
      this.lines.push({ rateCardId: this.data()?.rates[0]?.id ?? '', hours: '1' });
    this.invalidatePreview();
  }
  remove(i: number): void {
    if (this.lines.length > 1) this.lines.splice(i, 1);
    this.invalidatePreview();
  }
  private body(): object | null {
    const w = this.data();
    if (
      !w ||
      ![this.complexity, this.risk, this.discount, ...this.lines.map((l) => l.hours)].every(
        exactDecimal,
      )
    )
      return null;
    const lines = this.lines.map((l) => {
      const r = w.rates.find((r) => r.id === l.rateCardId);
      return r ? { role: r.role, activity: r.activity, hours: l.hours, rateCardId: r.id } : null;
    });
    if (lines.some((l) => l === null)) return null;
    return {
      proposalRevision: w.proposalRevision,
      revision: w.versions[0]?.revision ?? '0',
      lines,
      complexity: this.complexity,
      risk: this.risk,
      discount: this.discount,
      nonStandardTerms: this.nonStandard,
      note: this.note || null,
    };
  }
  preview(): void {
    const body = this.body();
    if (!body || this.busy()) {
      this.message.set('Choose approved rates and exact decimal inputs.');
      return;
    }
    this.invalidatePreview();
    const fence = this.fence;
    this.busy.set(true);
    this.write = this.http
      .post<unknown>('/api/ui/proposals/' + this.proposalId() + '/quotation/preview', body)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          try {
            this.previewAmounts.set(decodeAmounts(value));
            this.reviewedBody = body;
          } catch {
            this.message.set('Unsupported preview response.');
          }
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          this.message.set(
            'Preview unavailable. Check inputs, approved rates and current revisions.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  save(): void {
    const w = this.data();
    if (
      !this.reviewed ||
      !this.previewAmounts() ||
      this.busy() ||
      this.uncertain() ||
      this.savePending() ||
      !w
    )
      return;
    const intent: QuotationSaveIntent = {
      proposalId: this.proposalId(),
      requestId: this.requestId,
      proposalRevision: w.proposalRevision,
      expectedRevision: w.versions[0]?.revision ?? '0',
      lines: this.lines.map((x) => ({ rateCardId: x.rateCardId, hours: x.hours })),
      complexity: this.complexity,
      risk: this.risk,
      discount: this.discount,
      nonStandardTerms: this.nonStandard,
      note: this.note || null,
      submissionPending: true,
    };
    if (!this.persistIntent(intent)) {
      this.message.set(
        'The quotation was not sent because this browser could not save its recovery identity.',
      );
      return;
    }
    this.savedIntent = intent;
    this.savePending.set(true);
    this.recoverableSave.set(false);
    this.sendQuotationIntent(intent);
  }
  resolveSavedQuotationRequest(): void {
    const w = this.data();
    if (
      !w ||
      this.busy() ||
      !this.reviewed ||
      !this.savePending() ||
      !this.recoverableSave()
    )
      return;
    const saved = this.drafts.load(this.intentScope(), validQuotationIntent);
    if (
      !saved ||
      saved.proposalId !== this.proposalId() ||
      saved.requestId !== this.requestId
    ) {
      this.message.set(
        'The saved recovery details could not be verified. No retry was sent; refresh the page or contact an administrator.',
      );
      return;
    }
    this.sendQuotationIntent(saved);
  }
  private sendQuotationIntent(intent: QuotationSaveIntent): void {
    const w = this.data();
    if (!w || this.busy()) return;
    const fence = this.fence;
    this.busy.set(true);
    this.uncertain.set(false);
    this.recoverableSave.set(false);
    this.message.set('Saving reviewed quotation version…');
    const lines = intent.lines.map((l) => {
      const r = w.rates.find((rate) => rate.id === l.rateCardId);
      return r ? { role: r.role, activity: r.activity, hours: l.hours, rateCardId: r.id } : null;
    });
    if (lines.some((l) => l === null)) {
      this.busy.set(false);
      this.message.set('An approved rate changed; refresh the quotation.');
      return;
    }
    const body = {
      requestId: intent.requestId,
      proposalRevision: intent.proposalRevision,
      revision: intent.expectedRevision,
      lines,
      complexity: intent.complexity,
      risk: intent.risk,
      discount: intent.discount,
      nonStandardTerms: intent.nonStandardTerms,
      note: intent.note,
    };
    this.write = this.http
      .post<unknown>('/api/ui/proposals/' + intent.proposalId + '/quotation', body)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          const result = value as { id?: unknown };
          if (
            typeof result?.id !== 'string' ||
            (result.id.toLowerCase() !== intent.requestId.toLowerCase() &&
              !identity(result.id))
          ) {
            this.uncertain.set(true);
            this.savePending.set(true);
            this.recoverableSave.set(true);
            this.data.set(null);
            this.refreshParentAfterUncertainSave = true;
            this.invalidatePreview();
            this.message.set('Outcome unconfirmed. Refresh and review persisted versions.');
            return;
          }
          this.drafts.clear(this.intentScope());
          this.savePending.set(false);
          this.recoverableSave.set(false);
          this.uncertain.set(false);
          this.reviewed = false;
          this.requestId = crypto.randomUUID();
          this.savedIntent = null;
          this.invalidatePreview();
          this.message.set('Quotation version recorded.');
          this.tabDraft.submitted();
          this.changed.emit();
          this.load();
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          if (failure.status >= 400 && failure.status < 500) {
            this.drafts.clear(this.intentScope());
            this.savePending.set(false);
            this.recoverableSave.set(false);
            this.uncertain.set(false);
            this.reviewed = false;
            this.requestId = crypto.randomUUID();
            this.savedIntent = null;
            this.invalidatePreview();
            this.message.set(
              'Quotation version refused. Refresh and check current rates, revision and independent approval role.',
            );
          } else {
            this.uncertain.set(true);
            this.savePending.set(true);
            this.recoverableSave.set(true);
            this.data.set(null);
            this.refreshParentAfterUncertainSave = true;
            this.invalidatePreview();
            this.message.set(
              'Outcome unconfirmed. Refresh and review persisted versions before another command.',
            );
          }
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  submit(v: Version): void {
    this.command('/api/ui/quotations/' + v.id + '/submit', {});
  }
  approve(v: Version, r: Rule): void {
    this.command('/api/ui/quotations/' + v.id + '/approve', {
      ruleKey: r.key,
      reason: this.reasons[v.id + r.key],
    });
  }
  revoke(v: Version, r: Rule): void {
    const reason = (this.revocations[v.id + r.key] ?? '').trim();
    if (!r.canRevoke || !r.approvalId || reason.length < 5) return;
    this.command('/api/ui/quotations/approvals/' + r.approvalId + '/revoke', { reason });
  }
  private command(url: string, body: object, changesFee = false): void {
    if (!this.data() || this.busy() || this.uncertain()) return;
    const fence = this.fence;
    this.busy.set(true);
    this.message.set('Saving quotation action…');
    this.write = this.http
      .post(url, body)
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          if (changesFee && (!object(value) || !identity(value['id']))) {
            this.uncertain.set(true);
            this.data.set(null);
            this.refreshParentAfterUncertainSave = true;
            this.invalidatePreview();
            this.message.set('Outcome unconfirmed. Refresh and review persisted versions.');
            return;
          }
          this.invalidatePreview();
          this.message.set('Quotation action recorded.');
          if (changesFee) {
            this.tabDraft.submitted();
            this.changed.emit();
          }
          this.load();
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.busy.set(false);
          this.uncertain.set(!(failure.status >= 400 && failure.status < 500));
          this.invalidatePreview();
          if (this.uncertain()) {
            this.data.set(null);
            if (changesFee) this.refreshParentAfterUncertainSave = true;
          }
          this.message.set(
            this.uncertain()
              ? 'Outcome unconfirmed. Refresh and review persisted versions before another command.'
              : 'Action refused. Refresh and check current rates, revision and independent approval role.',
          );
          if (failure.status === 401) this.session.clear();
        },
      });
  }
  load(): void {
    this.read?.unsubscribe();
    this.data.set(null);
    this.error.set('');
    this.invalidatePreview();
    if (!guidPattern.test(this.proposalId()) || !this.session.current()?.staff) return;
    const fence = this.fence;
    this.loading.set(true);
    this.read = this.http
      .get<unknown>('/api/ui/proposals/' + this.proposalId() + '/quotation')
      .pipe(timeout(15000))
      .subscribe({
        next: (value) => {
          if (fence !== this.fence) return;
          try {
            const w = decodeQuotation(value);
            if (w.proposalId !== this.proposalId()) throw new Error('Wrong proposal');
            this.data.set(w);
            const pending = this.drafts.load(this.intentScope(), validQuotationIntent);
            if (pending && pending.proposalId.toLowerCase() === this.proposalId().toLowerCase()) {
              const matched = w.versions.some(
                (v) => v.id.toLowerCase() === pending.requestId.toLowerCase(),
              );
              if (matched) {
                this.drafts.clear(this.intentScope());
                this.savePending.set(false);
                this.recoverableSave.set(false);
                this.savedIntent = null;
                this.requestId = crypto.randomUUID();
                this.message.set('Persisted result found. The saved quotation revision was recorded.');
                this.changed.emit();
              } else {
                this.savedIntent = pending;
                this.requestId = pending.requestId;
                this.lines = pending.lines.map((l) => ({ ...l }));
                this.complexity = pending.complexity;
                this.risk = pending.risk;
                this.discount = pending.discount;
                this.nonStandard = pending.nonStandardTerms;
                this.note = pending.note ?? '';
                this.savePending.set(true);
                this.recoverableSave.set(true);
                this.uncertain.set(true);
                this.reviewed = false;
                this.message.set(
                  'A saved quotation request needs resolution. Review its unchanged terms to retry safely.',
                );
              }
            } else if (!this.lines[0].rateCardId) {
              const v = w.versions[0];
              this.lines = v
                ? v.amounts.lines.map((l) => ({ rateCardId: l.rateCardId, hours: l.hours }))
                : [{ rateCardId: w.rates[0]?.id ?? '', hours: '1' }];
              if (v) {
                this.complexity = v.complexity;
                this.risk = v.risk;
                this.discount = v.discount;
                this.nonStandard = v.nonStandardTerms;
                this.note = v.note ?? '';
              }
            }
            void this.tabDraft.bind(`commercial-quotation:${w.proposalId}`, w);
            if (this.refreshParentAfterUncertainSave) {
              this.refreshParentAfterUncertainSave = false;
              this.changed.emit();
            }
          } catch {
            this.error.set('Unsupported quotation response.');
          }
          this.loading.set(false);
        },
        error: (failure) => {
          if (fence !== this.fence) return;
          this.loading.set(false);
          this.error.set('Quotation unavailable. Current firm-wide commercial access is required.');
          if (failure.status === 401) this.session.clear();
        },
      });
  }
}
