import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormField, form, maxLength, disabled } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, UNKNOWN_OUTCOME } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { TabDrafts } from '../../core/tab-drafts';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import {
  catalogueDecoder,
  currencyDraft,
  decodeSet,
  emptyCurrencyIntent,
  exactRate,
  policyDecoder,
  PolicyReview,
  SetReview,
} from './currency-configuration-contracts';
const root = '/api/ui/accounting/currency-configuration';
@Component({
  selector: 'audit-currency-configuration',
  imports: [FormField, MatButtonModule, ...SHARED],
  template: `
    <audit-page-header
      title="FX rates & policies"
      description="Firm configuration · exact observations · independent approval. Approved records are immutable; new configuration requires a new code."
    />
    <p>
      Same-currency translation uses identity rate 1 without a market observation. Financial
      calculations and accounting approvals stay on the server.
    </p>
    <audit-state [loading]="catalogue.loading() || loading()" [error]="catalogue.error()" />
    <p role="status">{{ message() }}</p>
    @if (uncertain()) {
      <p role="alert">
        The last write has an unknown outcome. Refresh persisted state and reconcile it before a new
        action.
      </p>
      <button matButton [disabled]="!reconciledRead() || loading() || busy()" (click)="rebase()">
        I reviewed the refreshed persisted outcome
      </button>
    }
    @if (catalogue.data(); as c) {
      <div class="toolbar">
        <button matButton (click)="refresh()" [disabled]="busy()">Refresh persisted state</button>
        <button matButton (click)="choose('set')" [disabled]="busy() || !c.canWrite">
          New rate set
        </button>
        <button matButton (click)="choose('policy')" [disabled]="busy() || !c.canWrite">
          New translation policy
        </button>
      </div>
      @if (!c.canWrite) {
        <p role="note">
          Configuration is read-only in your current authority or firm safety state.
        </p>
      }
      <section>
        <h2>Rate sets</h2>
        @if (!c.rateSets.length) {
          <p>No rate sets configured.</p>
        }
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Code / version</th>
                <th>Source</th>
                <th>Effective dates</th>
                <th>Status</th>
                <th>Review</th>
              </tr>
            </thead>
            <tbody>
              @for (s of c.rateSets.slice(setPage() * 50, (setPage() + 1) * 50); track s.id) {
                <tr>
                  <td>{{ s.code }} / {{ s.version }}</td>
                  <td>{{ s.source }}</td>
                  <td>{{ s.effectiveFrom ?? 'Unset' }} → {{ s.effectiveTo ?? 'Unset' }}</td>
                  <td><audit-status [value]="s.status" /></td>
                  <td>
                    <button matButton [disabled]="busy()" (click)="choose('rate', s.id)">
                      Open {{ s.code }}
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <button matButton [disabled]="setPage() === 0" (click)="setPage.set(setPage() - 1)">
          Previous rate sets</button
        ><button
          matButton
          [disabled]="(setPage() + 1) * 50 >= c.rateSets.length"
          (click)="setPage.set(setPage() + 1)"
        >
          Next rate sets
        </button>
      </section>
      <section>
        <h2>Translation policies</h2>
        @if (!c.policies.length) {
          <p>No translation policies configured.</p>
        }
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Code</th>
                <th>Functional → presentation</th>
                <th>Purposes</th>
                <th>Status</th>
                <th>Review</th>
              </tr>
            </thead>
            <tbody>
              @for (p of c.policies.slice(policyPage() * 50, (policyPage() + 1) * 50); track p.id) {
                <tr>
                  <td>{{ p.code }}</td>
                  <td>{{ p.functionalCurrency }} → {{ p.presentationCurrency }}</td>
                  <td>
                    {{ p.closingRateRule }} / {{ p.averageRateRule }} / {{ p.historicalRateRule }}
                  </td>
                  <td><audit-status [value]="p.status" /></td>
                  <td>
                    <button matButton [disabled]="busy()" (click)="choose('policy-review', p.id)">
                      Open {{ p.code }}
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <button
          matButton
          [disabled]="policyPage() === 0"
          (click)="policyPage.set(policyPage() - 1)"
        >
          Previous policies</button
        ><button
          matButton
          [disabled]="(policyPage() + 1) * 50 >= c.policies.length"
          (click)="policyPage.set(policyPage() + 1)"
        >
          Next policies
        </button>
      </section>
      @if (setDetail(); as r) {
        <section class="review">
          <h2>Rate set {{ r.set.code }} · version {{ r.set.version }}</h2>
          <p>
            Source: {{ r.set.source }} · effective {{ r.set.effectiveFrom ?? 'Unset' }} →
            {{ r.set.effectiveTo ?? 'Unset' }}
          </p>
          <p class="identity">
            Set {{ r.set.id }} · prepared by {{ r.set.createdByUserId }} at {{ r.set.createdAt }} ·
            approved by {{ r.set.approvedByUserId ?? 'Pending independent reviewer' }}
            {{ r.set.approvedAt ?? '' }}
          </p>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Pair</th>
                  <th>Date / purpose</th>
                  <th>Exact rate</th>
                  <th>Direction</th>
                  <th>Observation</th>
                </tr>
              </thead>
              <tbody>
                @for (x of r.rates.slice(ratePage() * 50, (ratePage() + 1) * 50); track x.id) {
                  <tr>
                    <td>{{ x.fromCurrency }} → {{ x.toCurrency }}</td>
                    <td>{{ x.rateDate }} / {{ x.rateType }}</td>
                    <td>{{ x.rate }}</td>
                    <td>{{ x.direction }}</td>
                    <td class="identity">{{ x.id }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (!r.rates.length) {
            <p>Add a positive DIRECT observation before independent approval.</p>
          }
          <button matButton [disabled]="ratePage() === 0" (click)="ratePage.set(ratePage() - 1)">
            Previous observations</button
          ><button
            matButton
            [disabled]="(ratePage() + 1) * 50 >= r.rates.length"
            (click)="ratePage.set(ratePage() + 1)"
          >
            Next observations
          </button>
          @if (!r.canApprove && r.set.status === 'DRAFT') {
            <p>
              Approval requires an independent authorized reviewer, populated observations and
              effective dates.
            </p>
          }
          @if (r.canApprove) {
            <label
              ><input
                type="checkbox"
                [checked]="approvalReviewed()"
                (change)="approvalReviewed.set(!approvalReviewed())"
                [disabled]="busy()"
              />I independently reviewed this exact rate set and every observation.</label
            ><button
              matButton
              [disabled]="!approvalReviewed() || busy() || uncertain()"
              (click)="approve()"
            >
              Approve rate set
            </button>
          }
        </section>
      }
      @if (policyDetail(); as r) {
        <section class="review">
          <h2>Policy {{ r.policy.code }}</h2>
          <p>
            {{ r.policy.functionalCurrency }} → {{ r.policy.presentationCurrency }} ·
            {{ r.policy.closingRateRule }} / {{ r.policy.averageRateRule }} /
            {{ r.policy.historicalRateRule }}
          </p>
          <p class="identity">
            Policy {{ r.policy.id }} · prepared by {{ r.policy.createdByUserId }} at
            {{ r.policy.createdAt }} · approved by
            {{ r.policy.approvedByUserId ?? 'Pending independent reviewer' }}
            {{ r.policy.approvedAt ?? '' }}
          </p>
          @if (!r.canApprove && r.policy.status === 'DRAFT') {
            <p>The preparer cannot approve their own policy.</p>
          }
          @if (r.canApprove) {
            <label
              ><input
                type="checkbox"
                [checked]="approvalReviewed()"
                (change)="approvalReviewed.set(!approvalReviewed())"
                [disabled]="busy()"
              />I independently reviewed this exact policy.</label
            ><button
              matButton
              [disabled]="!approvalReviewed() || busy() || uncertain()"
              (click)="approve()"
            >
              Approve translation policy
            </button>
          }
        </section>
      }
      @if (editable()) {
        <section>
          <h2>
            {{
              mode() === 'set'
                ? 'Prepare rate set'
                : mode() === 'policy'
                  ? 'Prepare translation policy'
                  : 'Add observation'
            }}
          </h2>
          <form (submit)="$event.preventDefault(); submit()">
            <fieldset
              [disabled]="busy() || uncertain()"
              (input)="invalidateReview()"
              (change)="invalidateReview()"
            >
              <div class="fields">
                @if (mode() !== 'rate') {
                  <label>Code<input [formField]="fields.code" /></label>
                }
                @if (mode() === 'set') {
                  <label>Rate source<textarea [formField]="fields.source"></textarea></label
                  ><label>Version<input inputmode="numeric" [formField]="fields.version" /></label>
                  <label
                    >Effective from<input type="date" [formField]="fields.effectiveFrom" /></label
                  ><label>Effective to<input type="date" [formField]="fields.effectiveTo" /></label>
                } @else {
                  <label
                    >{{ mode() === 'policy' ? 'Functional currency' : 'From currency'
                    }}<input [formField]="fields.fromCurrency" placeholder="USD" /></label
                  ><label
                    >{{ mode() === 'policy' ? 'Presentation currency' : 'To currency'
                    }}<input [formField]="fields.toCurrency" placeholder="QAR"
                  /></label>
                  @if (mode() === 'rate') {
                    <label>Observation date<input type="date" [formField]="fields.date" /></label
                    ><label
                      >Purpose<select [formField]="fields.purpose">
                        <option>CLOSING</option>
                        <option>AVERAGE</option>
                        <option>HISTORICAL</option>
                      </select></label
                    ><label
                      >Exact DIRECT rate<input
                        inputmode="decimal"
                        [formField]="fields.rate"
                        placeholder="3.700000"
                    /></label>
                    <p>
                      DIRECT: multiply the source amount by this rate. Up to six decimal places;
                      inverse and same-currency observations are refused.
                    </p>
                  } @else {
                    <p>
                      Explicit purpose rules: closing CLOSING; average AVERAGE; historical
                      HISTORICAL.
                    </p>
                  }
                }
              </div>
            </fieldset>
            <div class="toolbar">
              <button matButton type="button" [disabled]="busy()" (click)="saveDraft()">
                Save tab draft</button
              ><button matButton type="button" [disabled]="busy()" (click)="recoverDraft()">
                Recover tab draft</button
              ><button matButton type="button" [disabled]="busy()" (click)="discardDraft()">
                Discard draft
              </button>
            </div>
            @if (stale()) {
              <p role="alert">
                The base revision changed. Refresh and explicitly review the current inputs before
                submission.
              </p>
              <button matButton type="button" (click)="rebase()">
                Review current base revision
              </button>
            }
            <p class="identity">Reviewed base {{ base() }}</p>
            @if (mode() === 'rate') {
              <p data-testid="currency-rate-intent" aria-live="polite">
                Proposed observation: {{ model().fromCurrency }} → {{ model().toCurrency }} ·
                {{ model().date }} · {{ model().purpose }} · DIRECT {{ model().rate }}
              </p>
            }
            <label
              ><input
                type="checkbox"
                [formField]="fields.reviewed"
                (change)="recordAssent($event)"
              />I reviewed the exact inputs and current persisted configuration.</label
            >
            <button
              matButton
              type="submit"
              [disabled]="!valid() || !reviewed() || stale() || busy() || uncertain()"
            >
              {{ mode() === 'rate' ? 'Add observation' : 'Create draft' }}
            </button>
          </form>
        </section>
      }
    }
  `,
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }
      section {
        margin-block: 1.5rem;
      }
      .toolbar {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
      }
      .fields {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(min(100%, 240px), 1fr));
        gap: 1rem;
      }
      label {
        display: block;
        margin-block: 0.6rem;
      }
      input:not([type='checkbox']),
      textarea,
      select {
        display: block;
        width: 100%;
        box-sizing: border-box;
        padding: 0.6rem;
      }
      fieldset {
        border: 0;
        padding: 0;
        min-width: 0;
      }
      .table-wrap {
        overflow: auto;
        max-width: 100%;
      }
      table {
        width: 100%;
        border-collapse: collapse;
      }
      td,
      th {
        padding: 0.6rem;
        text-align: left;
      }
      td {
        overflow-wrap: anywhere;
      }
      .identity {
        overflow-wrap: anywhere;
      }
      .review {
        border-inline-start: 3px solid var(--mat-sys-primary);
        padding: 1rem;
      }
    `,
  ],
})
export class CurrencyConfiguration {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly catalogue = this.api.resource(
    () => root,
    (raw) => {
      const c = catalogueDecoder(raw, '');
      if ([...c.rateSets, ...c.policies].some((x) => x.firmId !== this.session.current()?.firmId))
        throw new Error('Unsupported firm context');
      return c;
    },
  );
  readonly mode = signal<'set' | 'rate' | 'policy' | 'policy-review' | ''>('');
  readonly selected = signal('');
  readonly model = signal({ ...emptyCurrencyIntent(), reviewed: false });
  readonly fields = form(this.model, (p) => {
    maxLength(p.code, 100);
    maxLength(p.source, 2000);
    maxLength(p.rate, 40);
    maxLength(p.version, 10);
    maxLength(p.fromCurrency, 3);
    maxLength(p.toCurrency, 3);
    disabled(p.reviewed, () => this.busy() || this.stale() || this.uncertain());
  });
  readonly setDetail = signal<SetReview | null>(null);
  readonly policyDetail = signal<PolicyReview | null>(null);
  readonly base = signal('');
  readonly reviewed = computed(() => this.model().reviewed);
  readonly approvalReviewed = signal(false);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly reconciledRead = signal(false);
  private reconciliationRequested = false;
  readonly message = signal('');
  readonly setPage = signal(0);
  readonly policyPage = signal(0);
  readonly ratePage = signal(0);
  private baseline = JSON.stringify(emptyCurrencyIntent());
  private assentIntent = '';
  private saved = '';
  private request = 0;
  private destroyed = false;
  readonly currentRevision = computed(() =>
    this.mode() === 'rate'
      ? (this.setDetail()?.revision ?? '')
      : this.mode() === 'policy-review'
        ? (this.policyDetail()?.revision ?? '')
        : (this.catalogue.data()?.revision ?? ''),
  );
  readonly stale = computed(() => !this.base() || this.currentRevision() !== this.base());
  readonly editable = computed(
    () =>
      !!this.catalogue.data()?.canWrite &&
      (this.mode() === 'set' ||
        this.mode() === 'policy' ||
        (this.mode() === 'rate' && !!this.setDetail()?.canWrite)),
  );
  readonly valid = computed(() => {
    const m = this.model();
    const currency = (x: string) => /^[A-Z]{3}$/.test(x);
    const date = (x: string) => /^\d{4}-\d{2}-\d{2}$/.test(x);
    if (this.mode() === 'set')
      return (
        !!m.code.trim() &&
        !!m.source.trim() &&
        /^[1-9]\d{0,8}$/.test(m.version) &&
        (!m.effectiveFrom || date(m.effectiveFrom)) &&
        (!m.effectiveTo || date(m.effectiveTo)) &&
        (!m.effectiveFrom || !m.effectiveTo || m.effectiveFrom <= m.effectiveTo)
      );
    if (this.mode() === 'policy')
      return !!m.code.trim() && currency(m.fromCurrency) && currency(m.toCurrency);
    return (
      this.mode() === 'rate' &&
      currency(m.fromCurrency) &&
      currency(m.toCurrency) &&
      m.fromCurrency !== m.toCurrency &&
      date(m.date) &&
      ['CLOSING', 'AVERAGE', 'HISTORICAL'].includes(m.purpose) &&
      exactRate(m.rate)
    );
  });
  constructor() {
    effect(() => {
      const data = this.catalogue.data();
      if (!data && this.uncertain()) this.reconciliationRequested = true;
      if (data && this.reconciliationRequested && this.uncertain() && !this.selected()) {
        this.reconciledRead.set(true);
        this.reconciliationRequested = false;
      }
    });
    let signature = JSON.stringify(this.intent());
    let epoch = this.session.invalidation();
    effect(() => {
      const next = JSON.stringify(this.intent());
      if (
        next !== signature ||
        (this.model().reviewed && this.assentIntent !== next + this.base())
      ) {
        signature = next;
        untracked(() => this.fields.reviewed().reset(false));
      }
    });
    effect(() => {
      const n = this.session.invalidation();
      if (n !== epoch) {
        epoch = n;
        untracked(() => {
          this.request++;
          this.mode.set('');
          this.selected.set('');
          this.setDetail.set(null);
          this.policyDetail.set(null);
          this.model.set({ ...emptyCurrencyIntent(), reviewed: false });
          this.base.set('');
          this.model.update((m) => ({ ...m, reviewed: false }));
          this.approvalReviewed.set(false);
          this.message.set('Your session changed. Sign in again.');
        });
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request++;
    });
  }
  private intent() {
    const { reviewed, ...intent } = this.model();
    return intent;
  }
  private entity() {
    return `currency-config/${this.mode()}/${this.selected() || 'new'}`;
  }
  private dirty() {
    const value = JSON.stringify(this.intent());
    return value !== this.baseline && value !== this.saved;
  }
  invalidateReview() {
    this.fields.reviewed().reset(false);
    this.assentIntent = '';
  }
  confirmInputs() {
    const checked = !this.reviewed();
    this.assentIntent = checked ? JSON.stringify(this.intent()) + this.base() : '';
    this.model.update((m) => ({ ...m, reviewed: checked }));
  }
  recordAssent(event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    this.assentIntent = checked ? JSON.stringify(this.intent()) + this.base() : '';
    this.model.update((m) => ({ ...m, reviewed: checked }));
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty()) e.preventDefault();
  }
  async confirmNavigation(): Promise<boolean> {
    if (this.busy()) return false;
    if (!this.dirty() && !this.uncertain()) return true;
    const decision = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if (decision === 'save') return this.saveDraft();
    if (decision === 'discard') {
      this.discardDraft();
      return true;
    }
    return false;
  }
  saveDraft(): boolean {
    const ok = this.drafts.save(
      { entity: this.entity(), baseRevision: this.base() },
      this.intent(),
      currencyDraft,
      this.uncertain(),
    );
    this.message.set(
      ok
        ? 'Tab draft saved. It is not accounting evidence.'
        : 'Draft storage is unavailable; edits remain here.',
    );
    if (ok) this.saved = JSON.stringify(this.intent());
    return ok;
  }
  recoverDraft() {
    const d = this.drafts.read(
      { entity: this.entity(), baseRevision: this.currentRevision() },
      currencyDraft,
    );
    if (d.state !== 'ready') {
      this.message.set(
        d.state === 'stale'
          ? 'Draft context changed. Recovery refused.'
          : 'No recoverable tab draft.',
      );
      return;
    }
    if (d.draft.submissionPending) {
      this.uncertain.set(true);
      this.message.set(UNKNOWN_OUTCOME);
      return;
    }
    this.model.set({ ...d.draft.value, reviewed: false });
    this.base.set(this.currentRevision());
    this.model.update((m) => ({ ...m, reviewed: false }));
    this.saved = '';
    this.message.set('Draft recovered. Review every field again.');
  }
  discardDraft() {
    this.drafts.clear(this.entity());
    this.resetIntent();
  }
  private resetIntent() {
    this.model.set({ ...emptyCurrencyIntent(), reviewed: false });
    this.baseline = JSON.stringify(this.intent());
    this.saved = '';
    this.model.update((m) => ({ ...m, reviewed: false }));
  }
  rebase() {
    if (
      this.loading() ||
      !this.currentRevision() ||
      this.busy() ||
      (this.uncertain() && !this.reconciledRead())
    )
      return;
    this.base.set(this.currentRevision());
    this.model.update((m) => ({ ...m, reviewed: false }));
    this.uncertain.set(false);
    this.drafts.clear(this.entity());
    this.message.set(
      'Current base loaded. Review inputs and persisted state before any new action.',
    );
  }
  async choose(mode: 'set' | 'rate' | 'policy' | 'policy-review', id = '') {
    if (!(await this.confirmNavigation())) return;
    this.request++;
    this.mode.set(mode);
    this.selected.set(id);
    this.setDetail.set(null);
    this.policyDetail.set(null);
    this.resetIntent();
    this.base.set('');
    this.approvalReviewed.set(false);
    this.uncertain.set(false);
    this.ratePage.set(0);
    if (id) await this.detail();
    else this.base.set(this.currentRevision());
    const saved = this.drafts.read(
      { entity: this.entity(), baseRevision: this.currentRevision() },
      currencyDraft,
    );
    if (saved.state === 'ready' && saved.draft.submissionPending) {
      this.uncertain.set(true);
      this.reconciledRead.set(false);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  private async detail() {
    const n = ++this.request,
      id = this.selected(),
      mode = this.mode();
    this.loading.set(true);
    this.approvalReviewed.set(false);
    this.setDetail.set(null);
    this.policyDetail.set(null);
    try {
      const r =
        mode === 'rate'
          ? await this.api.get(root + '/sets/' + id, decodeSet)
          : await this.api.get(root + '/policies/' + id, policyDecoder);
      if (n !== this.request || this.destroyed || !this.session.current()) return;
      const record = 'set' in r ? r.set : r.policy;
      if (record.id !== id || record.firmId !== this.session.current()?.firmId)
        throw new Error('Unsupported configuration context.');
      if ('set' in r) this.setDetail.set(r);
      else this.policyDetail.set(r);
      if (!this.base()) this.base.set(this.currentRevision());
      if (this.uncertain()) this.reconciledRead.set(true);
    } catch (e) {
      if (n === this.request && !this.destroyed)
        this.message.set(e instanceof Error ? e.message : 'Unable to load configuration.');
    } finally {
      if (n === this.request) this.loading.set(false);
    }
  }
  refresh() {
    if (this.busy()) return;
    if (this.uncertain()) this.reconciliationRequested = true;
    this.catalogue.reload();
    this.approvalReviewed.set(false);
    this.model.update((m) => ({ ...m, reviewed: false }));
    if (this.selected()) void this.detail();
  }
  async submit() {
    if (
      !this.editable() ||
      !this.valid() ||
      !this.reviewed() ||
      this.assentIntent !== JSON.stringify(this.intent()) + this.base() ||
      this.stale() ||
      this.busy() ||
      this.uncertain()
    )
      return;
    const m = this.model(),
      mode = this.mode(),
      id = this.selected();
    const common = { revision: this.base(), reviewed: true };
    const body =
      mode === 'set'
        ? {
            ...common,
            code: m.code,
            source: m.source,
            version: Number(m.version),
            effectiveFrom: m.effectiveFrom || null,
            effectiveTo: m.effectiveTo || null,
          }
        : mode === 'policy'
          ? {
              ...common,
              code: m.code,
              functionalCurrency: m.fromCurrency,
              presentationCurrency: m.toCurrency,
              closingRule: 'CLOSING',
              averageRule: 'AVERAGE',
              historicalRule: 'HISTORICAL',
            }
          : {
              ...common,
              fromCurrency: m.fromCurrency,
              toCurrency: m.toCurrency,
              date: m.date,
              purpose: m.purpose,
              rate: m.rate,
              direction: 'DIRECT',
            };
    await this.write(
      mode === 'rate'
        ? `${root}/sets/${id}/rates`
        : `${root}/${mode === 'set' ? 'sets' : 'policies'}`,
      body,
      mode !== 'rate',
    );
  }
  async approve() {
    const d = this.mode() === 'rate' ? this.setDetail() : this.policyDetail();
    if (
      !d?.canApprove ||
      !this.approvalReviewed() ||
      this.loading() ||
      this.busy() ||
      this.uncertain()
    )
      return;
    await this.write(
      `${root}/${this.mode() === 'rate' ? 'sets' : 'policies'}/${this.selected()}/approve`,
      { revision: d.revision, reviewed: true },
      false,
    );
  }
  private async write(url: string, body: unknown, creation: boolean) {
    const epoch = this.session.invalidation();
    this.busy.set(true);
    this.reconciledRead.set(false);
    this.message.set('');
    const result = await this.api.command(url, body);
    if (epoch !== this.session.invalidation() || this.destroyed) {
      this.busy.set(false);
      return;
    }
    this.busy.set(false);
    this.model.update((m) => ({ ...m, reviewed: false }));
    this.approvalReviewed.set(false);
    if (!result.ok) {
      this.uncertain.set(result.unknown);
      this.message.set(result.message);
      if (!result.unknown) this.refresh();
      return;
    }
    if (
      creation
        ? typeof result.value !== 'string' || !guidPattern.test(result.value)
        : result.value !== true
    ) {
      this.uncertain.set(true);
      this.message.set(UNKNOWN_OUTCOME);
      return;
    }
    this.drafts.clear(this.entity());
    this.model.set({ ...emptyCurrencyIntent(), reviewed: false });
    this.baseline = JSON.stringify(this.intent());
    this.saved = '';
    this.base.set('');
    this.message.set(
      creation
        ? 'Draft created. Independent approval is required.'
        : 'Persisted. Refresh and review the new configuration.',
    );
    if (creation) {
      this.selected.set(result.value as string);
      this.mode.set(this.mode() === 'set' ? 'rate' : 'policy-review');
    }
    this.refresh();
  }
}
