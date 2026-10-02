import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { form, FormField, required, maxLength } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import {
  arr,
  bool,
  date,
  dec,
  guid,
  nat,
  nullable,
  obj,
  oneOf,
  sha256,
  text,
} from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const appliedRate = obj({
  fromCurrency: text,
  toCurrency: text,
  rateType: oneOf('CLOSING', 'IDENTITY'),
  rateDate: date,
  rate: dec,
  direction: oneOf('DIRECT'),
  source: text,
  setVersion: nat,
  rateSetId: nullable(guid),
  observationId: nullable(guid),
  setCode: nullable(text),
  effectiveFrom: nullable(date),
  effectiveTo: nullable(date),
});
const currencyReview = obj({
  datasetId: guid,
  clientId: guid,
  engagementId: guid,
  periodId: guid,
  sourceCurrency: text,
  presentationCurrency: text,
  periodEnd: date,
  currentRate: appliedRate,
  priorDatasetId: nullable(guid),
  priorPeriodEnd: nullable(date),
  priorRate: nullable(appliedRate),
  thresholdPercent: dec,
  thresholdAmount: dec,
  method: oneOf('UPLOAD_CLOSING_COMPARISON'),
  rounding: text,
  revision: sha256,
  lines: arr(
    obj({
      accountCode: text,
      accountName: text,
      sourceAmount: dec,
      translatedAmount: dec,
      priorTranslatedAmount: nullable(dec),
      variance: nullable(dec),
      variancePercent: nullable(dec),
      highlighted: bool,
      highlightReason: nullable(text),
    }),
    20000,
  ),
});
export function decodeCurrencyReview(value: unknown, path = '') {
  const v = currencyReview(value, path);
  function validRate(r: ReturnType<typeof appliedRate>, from: string, end: string): boolean {
    if (
      r.fromCurrency !== from ||
      r.toCurrency !== v.presentationCurrency ||
      r.rateDate !== end ||
      r.rate.startsWith('-') ||
      /^0+(?:\.0+)?$/.test(r.rate)
    )
      return false;
    if (r.rateType === 'IDENTITY')
      return (
        from === v.presentationCurrency &&
        /^1(?:\.0+)?$/.test(r.rate) &&
        r.setVersion === 0 &&
        r.rateSetId === null &&
        r.observationId === null &&
        r.source === 'Same currency'
      );
    return (
      from !== v.presentationCurrency &&
      r.rateSetId !== null &&
      r.observationId !== null &&
      r.setVersion > 0
    );
  }
  if (
    !/^[A-Z]{3}$/.test(v.sourceCurrency) ||
    !/^[A-Z]{3}$/.test(v.presentationCurrency) ||
    !validRate(v.currentRate, v.sourceCurrency, v.periodEnd) ||
    (v.priorDatasetId === null) !== (v.priorRate === null) ||
    (v.priorDatasetId === null) !== (v.priorPeriodEnd === null) ||
    (v.priorRate !== null &&
      !validRate(v.priorRate, v.priorRate.fromCurrency, v.priorPeriodEnd!)) ||
    new Set(v.lines.map((l) => l.accountCode)).size !== v.lines.length
  )
    throw new Error('Unsupported currency review context.');
  return v;
}
export function reviewThreshold(value: string): string | null {
  return /^\d{1,14}(\.\d{1,6})?$/.test(value.trim()) ? value.trim().replace(/^0+(?=\d)/, '') : null;
}
const blank = () => ({ presentation: '', thresholdPercent: '10', thresholdAmount: '0' });
@Component({
  selector: 'audit-intake-currency-review',
  imports: [FormField, MatButtonModule, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="currency-heading">
      <h2 id="currency-heading">Currency and movement review</h2>
      <p>
        Read-only upload comparison using closing rates for every account. Classification-based
        translation and remeasurement require their separate accounting workflows. These thresholds
        highlight movements; they grant no accounting approval.
      </p>
      <div class="inline-form">
        <label
          >Presentation currency <input [formField]="fields.presentation" autocomplete="off"
        /></label>
        <label
          >Highlight percent
          <input [formField]="fields.thresholdPercent" inputmode="decimal" autocomplete="off"
        /></label>
        <label
          >And absolute movement at least
          <input [formField]="fields.thresholdAmount" inputmode="decimal" autocomplete="off"
        /></label>
        <button matButton="outlined" (click)="load()" [disabled]="!canRead()">
          Review currency and movements
        </button>
      </div>
      @if (!datasetId()) {
        <p>Select an authorized dataset above.</p>
      }
      @if (datasetId() && !validInputs()) {
        <p>
          Enter a three-letter currency and non-negative plain decimal thresholds, up to six decimal
          places.
        </p>
      }
      @if (busy()) {
        <audit-state [loading]="true" label="currency and movement review" />
      }
      @if (error()) {
        <p role="alert" class="error-text">{{ error() }}</p>
      }
      @if (stale()) {
        <p role="alert">
          The review filters changed. Request a fresh review before using these results.
        </p>
      }
      @if (review(); as r) {
        <section aria-label="Currency review result" [attr.aria-busy]="busy()">
          <p>
            Dataset <code>{{ r.datasetId }}</code> · Client <code>{{ r.clientId }}</code> ·
            Engagement <code>{{ r.engagementId }}</code> · Period <code>{{ r.periodId }}</code>
          </p>
          <p>
            <strong>{{ r.sourceCurrency }} → {{ r.presentationCurrency }}</strong> · Period end
            {{ r.periodEnd }} · Method: upload closing-rate comparison · {{ r.rounding }}
          </p>
          @for (entry of rateContexts(); track entry.title) {
            <section [attr.aria-label]="entry.title">
              <h3>{{ entry.title }}</h3>
              @if (entry.rate.rateType === 'IDENTITY') {
                <p>
                  Same-currency identity: {{ entry.rate.fromCurrency }} →
                  {{ entry.rate.toCurrency }} at {{ entry.rate.rate }}. No market observation or
                  exchange-rate set is consumed.
                </p>
              } @else {
                <p>
                  Approved {{ entry.rate.rateType }} rate <strong>{{ entry.rate.rate }}</strong> ·
                  {{ entry.rate.fromCurrency }} → {{ entry.rate.toCurrency }} ·
                  {{ entry.rate.direction }} · {{ entry.rate.rateDate }}.
                </p>
                <p>
                  Source {{ entry.rate.source }} · Set {{ entry.rate.setCode }} v{{
                    entry.rate.setVersion
                  }}
                  · Set ID <code>{{ entry.rate.rateSetId }}</code> · Observation
                  <code>{{ entry.rate.observationId }}</code
                  >.
                </p>
                <p>
                  Effective {{ entry.rate.effectiveFrom ?? 'No start restriction' }} through
                  {{ entry.rate.effectiveTo ?? 'No end restriction' }}.
                </p>
              }
            </section>
          }
          @if (r.priorDatasetId) {
            <p>
              Prior authorized dataset <code>{{ r.priorDatasetId }}</code> · Period end
              {{ r.priorPeriodEnd }}.
            </p>
          } @else {
            <p>No authorized prior dataset is available for comparison.</p>
          }
          <p>
            Highlight thresholds: {{ r.thresholdPercent }}% and {{ r.thresholdAmount }}
            {{ r.presentationCurrency }}. New or missing accounts are flagged separately. These are
            review indicators, not approval decisions.
          </p>
          <p>
            <small
              >Review revision <code>{{ r.revision }}</code
              >. Recheck after source or rate changes.</small
            >
          </p>
          @if (!r.lines.length) {
            <p>No account balances in this authorized source.</p>
          } @else {
            <div class="table-scroll">
              <table aria-label="Currency review">
                <thead>
                  <tr>
                    <th>Account</th>
                    <th>Source ({{ r.sourceCurrency }})</th>
                    <th>Converted ({{ r.presentationCurrency }})</th>
                    <th>Prior ({{ r.presentationCurrency }})</th>
                    <th>Movement</th>
                    <th>Review indicator</th>
                  </tr>
                </thead>
                <tbody>
                  @for (l of rows(); track l.accountCode) {
                    <tr>
                      <td>{{ l.accountCode }} {{ l.accountName }}</td>
                      <td class="number">{{ l.sourceAmount }}</td>
                      <td class="number">{{ l.translatedAmount | money }}</td>
                      <td class="number">{{ l.priorTranslatedAmount | money }}</td>
                      <td class="number">
                        {{ l.variance | money
                        }}{{ l.variancePercent !== null ? ' (' + l.variancePercent + '%)' : '' }}
                      </td>
                      <td [class.error-text]="l.highlighted">
                        {{
                          l.highlightReason ??
                            (l.variance === null ? 'No prior comparison' : 'Within thresholds')
                        }}
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <nav aria-label="Currency review pages">
              <button matButton="text" (click)="page.set(page() - 1)" [disabled]="page() === 0">
                Previous accounts
              </button>
              <span>Accounts {{ page() * 50 + 1 }}–{{ end() }} of {{ r.lines.length }}</span>
              <button
                matButton="text"
                (click)="page.set(page() + 1)"
                [disabled]="end() === r.lines.length"
              >
                Next accounts
              </button>
            </nav>
          }
        </section>
      }
    </section>
  `,
})
export class IntakeCurrencyReview {
  readonly datasetId = input('');
  readonly clientId = input.required<string>();
  readonly engagementId = input.required<string>();
  readonly model = signal(blank());
  readonly fields = form(this.model, (p) => {
    required(p.presentation);
    maxLength(p.presentation, 3);
    required(p.thresholdPercent);
    required(p.thresholdAmount);
  });
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly review = signal<ReturnType<typeof decodeCurrencyReview> | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly page = signal(0);
  private reviewedIntent = '';
  private sequence = 0;
  private destroyed = false;
  readonly validInputs = computed(
    () =>
      /^[A-Za-z]{3}$/.test(this.model().presentation.trim()) &&
      reviewThreshold(this.model().thresholdPercent) !== null &&
      reviewThreshold(this.model().thresholdAmount) !== null,
  );
  readonly canRead = computed(
    () =>
      !!this.session.current()?.staff && !!this.datasetId() && this.validInputs() && !this.busy(),
  );
  readonly stale = computed(
    () => this.review() !== null && JSON.stringify(this.model()) !== this.reviewedIntent,
  );
  readonly rows = computed(
    () => this.review()?.lines.slice(this.page() * 50, this.page() * 50 + 50) ?? [],
  );
  readonly end = computed(() => Math.min((this.page() + 1) * 50, this.review()?.lines.length ?? 0));
  readonly rateContexts = computed(() => {
    const r = this.review();
    return r
      ? [
          { title: 'Current period rate', rate: r.currentRate },
          ...(r.priorRate ? [{ title: 'Prior period rate', rate: r.priorRate }] : []),
        ]
      : [];
  });
  constructor() {
    effect(() => {
      this.datasetId();
      this.clientId();
      this.engagementId();
      this.session.invalidation();
      this.session.current();
      untracked(() => {
        this.sequence++;
        this.review.set(null);
        this.error.set('');
        this.busy.set(false);
        this.page.set(0);
        this.model.set(blank());
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.sequence++;
    });
  }
  async load(): Promise<void> {
    if (!this.canRead()) return;
    const sequence = ++this.sequence,
      dataset = this.datasetId(),
      client = this.clientId(),
      engagement = this.engagementId(),
      intent = JSON.stringify(this.model());
    const { presentation, thresholdPercent, thresholdAmount } = this.model();
    this.busy.set(true);
    this.error.set('');
    this.review.set(null);
    this.page.set(0);
    try {
      const value = await this.api.get(
        `/api/ui/datasets/${dataset}/currency-review?presentation=${encodeURIComponent(presentation.trim().toUpperCase())}&percent=${reviewThreshold(thresholdPercent)}&amount=${reviewThreshold(thresholdAmount)}`,
        decodeCurrencyReview,
      );
      if (this.destroyed || sequence !== this.sequence || intent !== JSON.stringify(this.model()))
        return;
      if (
        value.datasetId !== dataset ||
        value.clientId !== client ||
        value.engagementId !== engagement ||
        value.presentationCurrency !== presentation.trim().toUpperCase() ||
        value.thresholdPercent !== reviewThreshold(thresholdPercent) ||
        value.thresholdAmount !== reviewThreshold(thresholdAmount)
      )
        throw new Error('Unsupported review context. Load the current dataset again.');
      this.reviewedIntent = intent;
      this.review.set(value);
    } catch (e) {
      if (!this.destroyed && sequence === this.sequence) this.error.set((e as Error).message);
    } finally {
      if (!this.destroyed && sequence === this.sequence) this.busy.set(false);
    }
  }
}
