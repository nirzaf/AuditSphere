import {
  Component,
  DestroyRef,
  HostListener,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { form, FormField, required, maxLength, applyEach } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, CommandState } from '../../core/api';
import { TabDrafts, DraftRead, DraftScope } from '../../core/tab-drafts';
import { Drafts } from '../../core/drafts';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import {
  arr,
  bool,
  date,
  dec,
  guid,
  instant,
  nat,
  nullable,
  obj,
  sha256,
  text,
} from '../../core/decode';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import {
  blankLine,
  emptyRemeasurement,
  remeasurementIntent,
  remeasurementAmount,
  validDraft,
} from './remeasurement-drafts';
export { validDraft } from './remeasurement-drafts';

export const decodeRemeasurementWorkspace = obj({
  contexts: arr(
    obj({
      periodId: guid,
      clientId: guid,
      engagementId: guid,
      clientName: text,
      periodCode: text,
      endDate: date,
      engagementLabel: text,
    }),
    20000,
  ),
  history: arr(
    obj({ id: guid, clientName: text, periodCode: text, status: text, createdAt: instant }),
    50,
  ),
});
export const decodeChoices = obj({
  functionalCurrency: text,
  baseRevision: sha256,
  hasMoreLines: bool,
  canPrepare: bool,
  rateSets: arr(obj({ id: guid, label: text }), 500),
  policies: arr(obj({ id: guid, label: text }), 500),
  glLines: arr(
    obj({ id: guid, currency: text, originalAmount: dec, functionalAmount: dec, label: text }),
    500,
  ),
});
export const decodeSchedule = obj({
  schedule: obj({
    id: guid,
    clientId: guid,
    engagementId: guid,
    periodId: guid,
    asOfDate: date,
    functionalCurrency: text,
    inputHash: sha256,
    itemCount: nat,
    totalForeignExchangeAdjustment: dec,
    status: text,
    createdByUserId: guid,
    approvedByUserId: nullable(guid),
    items: arr(
      obj({
        stableItemReference: text,
        evidenceSnapshotId: guid,
        evidenceSha256: sha256,
        sourceGeneralLedgerLineId: nullable(guid),
        sourceGlLineDigest: (value: unknown, path: string) =>
          value === '' ? '' : sha256(value, path),
        isMonetary: bool,
        foreignCurrency: text,
        foreignCurrencyAmount: dec,
        priorFunctionalCarryingAmount: dec,
        rateDate: date,
        rateType: text,
        appliedRate: dec,
        remeasuredFunctionalAmount: dec,
        foreignExchangeAdjustment: dec,
        roundingAdjustment: dec,
      }),
      500,
    ),
  }),
  reviewToken: sha256,
  canApprove: bool,
  rateSetVersionId: guid,
  translationPolicyVersionId: guid,
  rateSource: text,
  closingRule: text,
  historicalRule: text,
  blocker: nullable(text),
});

@Component({
  selector: 'audit-remeasurement',
  imports: [FormField, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb">
      <a routerLink="/app">Portfolio</a> / <a routerLink="/app/accounting">Client accounting</a> /
      <span>Currency remeasurement</span>
    </nav>
    <audit-page-header
      title="Currency remeasurement workpapers"
      eyebrow="Client accounting · FX"
      description="Evidence-linked calculations from approved rates and policies, followed by independent human approval."
    />
    <audit-state
      [loading]="ws.loading()"
      [error]="ws.error()"
      label="scoped periods and approved inputs"
    />
    @if (ws.data(); as w) {
      <p role="status">
        {{ w.contexts.length }} available scoped period/engagement choices ·
        {{ w.history.length }} recent workpapers (up to 50)
      </p>
      <p>
        Approval records a workpaper. It does not post a journal or clear release and records gates.
      </p>
      <label
        >Client period and engagement
        <select [value]="context()" (change)="contextChanged($event)" [disabled]="cmd.busy()">
          <option value="" [selected]="!context()">Select a scoped period</option>
          @for (c of w.contexts; track c.periodId + c.engagementId) {
            <option
              [value]="c.periodId + '|' + c.engagementId"
              [selected]="context() === c.periodId + '|' + c.engagementId"
            >
              {{ c.clientName }} · {{ c.periodCode }} · {{ c.endDate }} · {{ c.engagementLabel }}
            </option>
          }
        </select></label
      >
      @if (!w.contexts.length) {
        <p>No accounting period is available in your current assignments.</p>
      }
      <button matButton (click)="refreshInputs()" [disabled]="cmd.busy() || !context()">
        Refresh approved inputs
      </button>
      <audit-state
        [loading]="choices.loading()"
        [error]="choices.error()"
        label="approved rates, policies and sealed GL lines"
      />
      @if (choices.data(); as c) {
        <section class="panel" aria-label="Remeasurement tab draft">
          <h2>Unsubmitted tab draft</h2>
          <p>
            Save explicitly in this tab for up to four hours. Recovery requires this identity,
            session and current approved-input revision. Drafts are not evidence; review assent and
            files are never saved.
          </p>
          <button
            matButton
            (click)="saveDraft()"
            [disabled]="cmd.busy() || !hasIntent() || stale()"
          >
            Save workpaper draft in tab
          </button>
          @if (candidate() === 'ready') {
            <button matButton (click)="recoverDraft()" [disabled]="cmd.busy() || hasIntent()">
              Recover workpaper draft
            </button>
          }
          @if (candidate() === 'stale') {
            <p role="status">
              Saved draft expired or its approved inputs changed. It cannot be recovered.
            </p>
          }
          @if (candidate() === 'unavailable') {
            <p role="status">Tab storage is unavailable. Edits remain in memory only.</p>
          }
          @if (hasIntent() || candidate() === 'ready' || candidate() === 'stale') {
            <button matButton (click)="discardDraft()" [disabled]="cmd.busy()">
              Discard workpaper draft
            </button>
          }
          @if (stale()) {
            <p role="alert">
              Approved inputs changed. Edits are retained, but submission is blocked.
            </p>
            <button matButton (click)="rebase()" [disabled]="cmd.busy()">
              Use refreshed approved inputs
            </button>
          }
          @if (draftMessage()) {
            <p role="status">{{ draftMessage() }}</p>
          }
        </section>
        <section class="panel" aria-labelledby="prepare-heading">
          <h2 id="prepare-heading">Prepare workpaper</h2>
          <p>
            Functional currency {{ c.functionalCurrency }}. A sealed GL line supplies corroborating
            lineage; enter the independently confirmed open balance and exact evidence snapshot
            below.
          </p>
          @if (c.hasMoreLines) {
            <p role="status">
              Showing the first 500 sealed foreign-currency GL lines. This is a bounded selection,
              not the whole ledger.
            </p>
          }
          @if (!c.rateSets.length || !c.policies.length || !c.glLines.length) {
            <p role="alert">
              Preparation is blocked until approved rates, a functional-currency policy and sealed
              foreign-currency GL lines are available.
            </p>
          }
          @if (!c.canPrepare) {
            <p role="alert">
              Preparation is unavailable under current professional-work authorization.
            </p>
          }
          <div class="inline-form">
            <label
              >Approved rate set
              <select [formField]="draftForm.rateSet">
                <option value="">Select an approved rate set</option>
                @for (r of c.rateSets; track r.id) {
                  <option [value]="r.id">{{ r.label }}</option>
                }
              </select></label
            >
            <label
              >Approved translation policy
              <select [formField]="draftForm.policy">
                <option value="">Select an approved policy</option>
                @for (p of c.policies; track p.id) {
                  <option [value]="p.id">{{ p.label }}</option>
                }
              </select></label
            >
          </div>
          @for (line of model().lines; track $index; let i = $index) {
            <fieldset class="panel">
              <legend>Item {{ i + 1 }}</legend>
              <div class="inline-form">
                <label
                  >Stable source reference <input [formField]="draftForm.lines[i].reference"
                /></label>
                <label
                  >Evidence snapshot ID <input [formField]="draftForm.lines[i].snapshotId"
                /></label>
                <label
                  >Imported GL source line
                  <select [value]="line.sourceLineId" (change)="sourceChanged(i, $event)">
                    <option value="">Select a sealed GL line</option>
                    @for (s of c.glLines; track s.id) {
                      <option [value]="s.id">{{ s.label }}</option>
                    }
                  </select></label
                >
                @if (source(line.sourceLineId); as s) {
                  <p>
                    Source {{ s.currency }} {{ s.originalAmount }} · prior imported functional
                    {{ s.functionalAmount }} {{ c.functionalCurrency }}
                  </p>
                }
                <label
                  ><input type="checkbox" [formField]="draftForm.lines[i].isMonetary" /> Monetary
                  item (closing rate); unchecked means historical-cost non-monetary</label
                >
                <label>Foreign currency <input [formField]="draftForm.lines[i].currency" /></label>
                <label
                  >Foreign-currency amount
                  <input inputmode="decimal" [formField]="draftForm.lines[i].foreignAmount"
                /></label>
                <label
                  >Prior functional carrying amount
                  <input inputmode="decimal" [formField]="draftForm.lines[i].priorCarrying"
                /></label>
                <label
                  >Historical rate date
                  <input type="date" [formField]="draftForm.lines[i].historicalDate"
                /></label>
                <button matButton (click)="remove(i)" [disabled]="cmd.busy()">Remove item</button>
              </div>
            </fieldset>
          }
          <p>
            Amounts use plain decimal text: up to 14 whole digits and 6 decimal places. No
            client-side floating-point calculation is performed.
          </p>
          <label
            ><input type="checkbox" [formField]="draftForm.reviewed" /> I reviewed this exact
            client, engagement, period, evidence and approved rate inputs.</label
          >
          <p class="actions">
            <button
              matButton="outlined"
              (click)="add()"
              [disabled]="cmd.busy() || model().lines.length >= 500"
            >
              Add item
            </button>
            <button matButton="filled" (click)="prepare()" [disabled]="!canPrepare()">
              {{ cmd.busy() ? 'Saving…' : 'Prepare reviewed workpaper' }}
            </button>
          </p>
        </section>
      }
      @if (review(); as r) {
        @let s = r.schedule;
        <section class="panel" aria-labelledby="schedule-heading">
          <h2 id="schedule-heading">Workpaper · {{ s.status }}</h2>
          <p>
            Client <code>{{ s.clientId }}</code> · engagement <code>{{ s.engagementId }}</code> ·
            period <code>{{ s.periodId }}</code>
          </p>
          <p>
            As of {{ s.asOfDate }} · functional currency {{ s.functionalCurrency }} ·
            {{ s.itemCount }} item(s) · FX adjustment
            <strong>{{ s.totalForeignExchangeAdjustment }} {{ s.functionalCurrency }}</strong>
          </p>
          <p>
            Rate-set version <code>{{ r.rateSetVersionId }}</code> · provenance {{ r.rateSource }} ·
            policy <code>{{ r.translationPolicyVersionId }}</code> · closing {{ r.closingRule }} /
            historical {{ r.historicalRule }}
          </p>
          <p>
            Manifest <code>{{ s.inputHash }}</code>
          </p>
          @if (r.blocker) {
            <p role="alert">{{ r.blocker }}</p>
          }
          <div class="table-scroll">
            <table aria-label="Currency remeasurement item calculations">
              <thead>
                <tr>
                  <th>Reference / classification</th>
                  <th>Evidence and GL lineage</th>
                  <th>Direction, date and rate</th>
                  <th>Open balance / prior carrying</th>
                  <th>Remeasured / FX movement</th>
                </tr>
              </thead>
              <tbody>
                @for (x of s.items; track x.stableItemReference) {
                  <tr>
                    <td>
                      {{ x.stableItemReference }}<br />{{
                        x.isMonetary ? 'Monetary — closing' : 'Historical-cost non-monetary'
                      }}
                    </td>
                    <td>
                      <code>{{ x.evidenceSnapshotId }}</code
                      ><br /><code>{{ x.evidenceSha256 }}</code
                      ><br /><code>{{ x.sourceGeneralLedgerLineId }}</code
                      ><br />
                      @if (x.sourceGlLineDigest) {
                        <code>{{ x.sourceGlLineDigest }}</code>
                      } @else {
                        <span>No immutable GL link recorded on this historical workpaper.</span>
                      }
                    </td>
                    <td>
                      {{ x.foreignCurrency }} → {{ s.functionalCurrency }} · {{ x.rateType }} ·
                      {{ x.rateDate }} · {{ x.appliedRate }}
                      @if (x.foreignCurrency === s.functionalCurrency) {
                        <br />Identity rate 1; no market-rate observation required.
                      }
                    </td>
                    <td>
                      {{ x.foreignCurrencyAmount }} {{ x.foreignCurrency }}<br />{{
                        x.priorFunctionalCarryingAmount
                      }}
                      {{ s.functionalCurrency }}
                    </td>
                    <td>
                      {{ x.remeasuredFunctionalAmount }} {{ s.functionalCurrency }}<br />FX
                      {{ x.foreignExchangeAdjustment }} · rounding {{ x.roundingAdjustment }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (r.canApprove) {
            <label
              ><input
                type="checkbox"
                [checked]="approvalReviewed()"
                (change)="approvalReviewed.set($any($event.target).checked)"
              />
              I independently reviewed this exact workpaper and current source revisions.</label
            >
            <button
              matButton="filled"
              (click)="approve()"
              [disabled]="cmd.busy() || cmd.uncertain() || !approvalReviewed()"
            >
              Approve independently
            </button>
          }
          <p>
            Prepared by <code>{{ s.createdByUserId }}</code
            >{{ s.approvedByUserId ? ' · Approved by ' + s.approvedByUserId : '' }}
          </p>
        </section>
      }
      <section class="panel" aria-labelledby="history-heading">
        <h2 id="history-heading">Recent scoped workpapers</h2>
        <div class="table-scroll">
          <table aria-label="Recent scoped currency remeasurement workpapers">
            <thead>
              <tr>
                <th>Client</th>
                <th>Period</th>
                <th>Status</th>
                <th>Created</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              @for (h of w.history; track h.id) {
                <tr>
                  <td>{{ h.clientName }}</td>
                  <td>{{ h.periodCode }}</td>
                  <td><audit-status [value]="h.status" /></td>
                  <td>{{ h.createdAt.slice(0, 16).replace('T', ' ') }} UTC</td>
                  <td>
                    <button matButton (click)="open(h.id)" [disabled]="cmd.busy()">
                      Open / review
                    </button>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="5">No saved workpapers are available in the current scope.</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }
    @if (cmd.uncertain()) {
      <p role="alert">
        The submission outcome is unknown. Refresh persisted workpapers and review before repeating
        an action.
      </p>
      <button matButton (click)="refreshPersisted()" [disabled]="cmd.busy()">
        Refresh persisted workpapers
      </button>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
  styles: [
    `
      code {
        overflow-wrap: anywhere;
      }
      fieldset {
        min-inline-size: 0;
      }
      nav {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
      }
    `,
  ],
})
export class CurrencyRemeasurement {
  private readonly api = inject(Api);
  private readonly drafts = inject(TabDrafts);
  private readonly session = inject(SessionService);
  private readonly dialogs = inject(MatDialog);
  readonly ws = this.api.resource(
    () => '/api/ui/accounting/remeasurement',
    decodeRemeasurementWorkspace,
  );
  readonly context = signal('');
  readonly choices = this.api.resource(() => {
    const [period, engagement] = this.context().split('|');
    return period && engagement
      ? `/api/ui/accounting/remeasurement/choices?periodId=${period}&engagementId=${engagement}`
      : null;
  }, decodeChoices);
  readonly model = signal(emptyRemeasurement());
  readonly draftForm = form(this.model, (p) => {
    required(p.rateSet);
    required(p.policy);
    applyEach(p.lines, (l) => {
      required(l.reference);
      maxLength(l.reference, 200);
      required(l.snapshotId);
      maxLength(l.snapshotId, 36);
      required(l.currency);
      maxLength(l.currency, 3);
      required(l.foreignAmount);
      required(l.priorCarrying);
      maxLength(l.foreignAmount, 40);
      maxLength(l.priorCarrying, 40);
      maxLength(l.historicalDate, 10);
    });
  });
  readonly cmd = new CommandState(this.api);
  readonly candidate = signal<DraftRead<unknown>['state']>('absent');
  readonly draftMessage = signal('');
  readonly review = signal<ReturnType<typeof decodeSchedule> | null>(null);
  readonly approvalReviewed = signal(false);
  private base = '';
  private baseline = JSON.stringify(remeasurementIntent(emptyRemeasurement()));
  private saved = '';
  private signature = '';
  private leaveDialog: MatDialogRef<UnsavedChangesDialog> | null = null;
  private refreshing = false;
  private readSequence = 0;
  constructor() {
    // Unsupported unversioned cross-tab drafts are never imported into a reviewed workpaper.
    inject(Drafts).clear('accounting-remeasurement');
    effect(() => {
      this.session.invalidation();
      untracked(() => {
        this.leaveDialog?.close('keep');
        this.readSequence++;
        this.context.set('');
        this.reset();
        this.review.set(null);
        this.approvalReviewed.set(false);
        this.cmd.uncertain.set(false);
        this.cmd.message.set('');
        this.draftMessage.set('');
      });
    });
    effect(() => {
      const w = this.ws.data();
      if (w)
        untracked(() => {
          if (
            this.context() &&
            !w.contexts.some((c) => c.periodId + '|' + c.engagementId === this.context())
          ) {
            this.readSequence++;
            this.context.set('');
            this.reset();
            this.review.set(null);
          }
          if (!this.context() && w.contexts[0])
            this.context.set(w.contexts[0].periodId + '|' + w.contexts[0].engagementId);
          if (this.refreshing) {
            this.refreshing = false;
            this.cmd.uncertain.set(false);
            this.model.update((m) => ({ ...m, reviewed: false }));
            this.draftMessage.set(
              'Persisted workpapers refreshed. Inspect the saved result and review before submitting again.',
            );
          }
        });
    });
    effect(() => {
      const c = this.choices.data();
      if (c)
        untracked(() => {
          if (!this.hasIntent()) this.reset(c);
          else {
            this.model.update((m) => ({ ...m, reviewed: false }));
            if (this.stale()) this.saved = '';
          }
          this.checkCandidate();
        });
    });
    effect(() => {
      const signature = JSON.stringify(remeasurementIntent(this.model()));
      const base = this.choices.data()?.baseRevision;
      if (signature !== this.signature)
        untracked(() => {
          this.signature = signature;
          if (this.hasIntent() && !this.base && base) this.base = base;
          this.model.update((m) => ({ ...m, reviewed: false }));
        });
    });
    inject(DestroyRef).onDestroy(() => this.leaveDialog?.close('keep'));
  }
  private reset(c: ReturnType<typeof decodeChoices> | null = null): void {
    const m = emptyRemeasurement();
    m.rateSet = c?.rateSets.length === 1 ? c.rateSets[0].id : '';
    m.policy = c?.policies.length === 1 ? c.policies[0].id : '';
    this.model.set(m);
    this.baseline = JSON.stringify(remeasurementIntent(m));
    this.saved = '';
    this.base = '';
    this.candidate.set('absent');
  }
  scope(): DraftScope | null {
    const c = this.choices.data();
    return this.context() && c
      ? {
          entity: 'remeasurement/' + this.context().replace('|', '/'),
          baseRevision: c.baseRevision,
        }
      : null;
  }
  hasIntent(): boolean {
    return JSON.stringify(remeasurementIntent(this.model())) !== this.baseline;
  }
  dirty(): boolean {
    return this.hasIntent() && JSON.stringify(remeasurementIntent(this.model())) !== this.saved;
  }
  stale(): boolean {
    return !!this.base && this.base !== this.choices.data()?.baseRevision;
  }
  private checkCandidate(): void {
    const scope = this.scope();
    this.candidate.set(scope ? this.drafts.read(scope, validDraft).state : 'absent');
  }
  saveDraft(pending = false): boolean {
    const scope = this.scope();
    if (!scope || this.stale() || !this.hasIntent()) {
      this.draftMessage.set(
        'Load and review the current approved inputs before saving this draft.',
      );
      return false;
    }
    const ok = this.drafts.save(scope, remeasurementIntent(this.model()), validDraft, pending);
    this.draftMessage.set(
      ok
        ? 'Workpaper draft saved in this tab only. Recover explicitly and review again.'
        : 'Tab storage is unavailable. Edits remain in memory only.',
    );
    if (ok) this.saved = JSON.stringify(remeasurementIntent(this.model()));
    this.checkCandidate();
    return ok;
  }
  recoverDraft(): void {
    const scope = this.scope();
    if (!scope || this.hasIntent() || this.cmd.busy()) return;
    const read = this.drafts.read(scope, validDraft);
    this.candidate.set(read.state);
    if (read.state !== 'ready') return;
    const v = read.draft.value,
      c = this.choices.data()!;
    if (
      (v.rateSet && !c.rateSets.some((r) => r.id === v.rateSet)) ||
      (v.policy && !c.policies.some((p) => p.id === v.policy)) ||
      v.lines.some((l) => l.sourceLineId && !c.glLines.some((x) => x.id === l.sourceLineId))
    ) {
      this.candidate.set('stale');
      return;
    }
    this.model.set({ ...v, reviewed: false });
    this.base = scope.baseRevision;
    this.saved = JSON.stringify(v);
    this.cmd.uncertain.set(read.draft.submissionPending);
    this.draftMessage.set(
      read.draft.submissionPending
        ? 'Recovered intent has an unconfirmed submission. Refresh persisted workpapers before any retry.'
        : 'Recovered unsubmitted intent. Review the current evidence and inputs again.',
    );
  }
  discardDraft(): void {
    const scope = this.scope();
    const erased = scope ? this.drafts.clear(scope.entity) : false;
    this.reset(this.choices.data());
    this.checkCandidate();
    this.draftMessage.set(
      erased
        ? 'Unsubmitted draft discarded.'
        : 'Edits cleared from memory; storage removal could not be confirmed.',
    );
  }
  rebase(): void {
    const c = this.choices.data();
    if (!c || this.cmd.busy()) return;
    this.base = c.baseRevision;
    this.saved = '';
    this.model.update((m) => ({ ...m, reviewed: false }));
    this.draftMessage.set('Refreshed inputs selected. Check every evidence line and review again.');
  }
  source(id: string) {
    return this.choices.data()?.glLines.find((s) => s.id === id) ?? null;
  }
  sourceChanged(i: number, e: Event): void {
    const id = (e.target as HTMLSelectElement).value,
      source = this.source(id);
    this.model.update((m) => ({
      ...m,
      lines: m.lines.map((l, n) =>
        n === i ? { ...l, sourceLineId: id, currency: source?.currency ?? '' } : l,
      ),
    }));
  }
  add(): void {
    if (!this.cmd.busy() && this.model().lines.length < 500)
      this.model.update((m) => ({ ...m, lines: [...m.lines, blankLine()] }));
  }
  remove(i: number): void {
    if (this.cmd.busy()) return;
    this.model.update((m) => {
      const lines = m.lines.filter((_, n) => n !== i);
      return { ...m, lines: lines.length ? lines : [blankLine()] };
    });
  }
  async contextChanged(e: Event): Promise<void> {
    const select = e.target as HTMLSelectElement,
      next = select.value,
      previous = this.context();
    select.value = previous;
    if (next === previous || !(await this.confirmNavigation())) return;
    this.readSequence++;
    this.context.set(next);
    this.reset();
    this.review.set(null);
    this.approvalReviewed.set(false);
    this.draftMessage.set('');
    select.value = next;
  }
  refreshInputs(): void {
    if (this.cmd.busy()) return;
    this.approvalReviewed.set(false);
    this.model.update((m) => ({ ...m, reviewed: false }));
    this.choices.reload();
  }
  refreshPersisted(): void {
    if (this.cmd.busy()) return;
    this.refreshing = true;
    this.review.set(null);
    this.approvalReviewed.set(false);
    this.ws.reload();
    this.choices.reload();
  }
  canPrepare(): boolean {
    const m = this.model(),
      c = this.choices.data();
    return (
      !this.cmd.busy() &&
      !this.cmd.uncertain() &&
      !this.stale() &&
      !!c?.canPrepare &&
      !!m.reviewed &&
      c.rateSets.some((r) => r.id === m.rateSet) &&
      c.policies.some((p) => p.id === m.policy) &&
      m.lines.length > 0 &&
      m.lines.every(
        (l) =>
          !!l.reference.trim() &&
          guidPattern.test(l.snapshotId.trim()) &&
          !!this.source(l.sourceLineId) &&
          /^[A-Z]{3}$/.test(l.currency.toUpperCase()) &&
          remeasurementAmount(l.foreignAmount) !== null &&
          remeasurementAmount(l.priorCarrying) !== null &&
          (l.isMonetary || /^\d{4}-\d{2}-\d{2}$/.test(l.historicalDate)),
      )
    );
  }
  async prepare(): Promise<void> {
    if (!this.canPrepare()) return;
    const c = this.choices.data()!,
      m = this.model(),
      ctx = this.ws
        .data()
        ?.contexts.find((c) => c.periodId + '|' + c.engagementId === this.context());
    if (!ctx) return;
    const epoch = this.session.invalidation(),
      selected = this.context();
    this.saveDraft(true);
    const ok = await this.cmd.run<string>(
      '/api/ui/accounting/remeasurement',
      {
        clientId: ctx.clientId,
        engagementId: ctx.engagementId,
        periodId: ctx.periodId,
        rateSetId: m.rateSet,
        policyId: m.policy,
        asOfDate: ctx.endDate,
        reviewToken: c.baseRevision,
        reviewed: m.reviewed,
        items: m.lines.map((l) => ({
          reference: l.reference.trim(),
          snapshotId: l.snapshotId.trim(),
          sourceLineId: l.sourceLineId,
          isMonetary: l.isMonetary,
          currency: l.currency.toUpperCase(),
          foreignAmount: remeasurementAmount(l.foreignAmount),
          priorCarrying: remeasurementAmount(l.priorCarrying),
          historicalRateDate: l.historicalDate || null,
        })),
      },
      'Submitted workpaper saved. Independent review is required.',
      (id) => {
        if (epoch !== this.session.invalidation() || selected !== this.context()) return;
        if (typeof id !== 'string' || !guidPattern.test(id)) {
          this.cmd.uncertain.set(true);
          this.cmd.failed.set(true);
          this.cmd.message.set(
            'The returned workpaper identity is unsupported. Refresh persisted workpapers before repeating this action.',
          );
          return;
        }
        const scope = this.scope();
        const cleared = scope ? this.drafts.clear(scope.entity) : false;
        this.reset(c);
        this.draftMessage.set(
          cleared
            ? 'Confirmed workpaper saved. The tab draft was removed.'
            : 'Confirmed workpaper saved; tab draft removal could not be confirmed.',
        );
        void this.open(id);
      },
    );
    if (epoch !== this.session.invalidation() || selected !== this.context()) return;
    this.model.update((m) => ({ ...m, reviewed: false }));
    if (!ok && !this.cmd.uncertain()) this.saveDraft(false);
    this.ws.reload();
  }
  async open(id: string): Promise<void> {
    this.review.set(null);
    this.approvalReviewed.set(false);
    const epoch = this.session.invalidation(),
      read = ++this.readSequence,
      context = this.context();
    try {
      const r = await this.api.get('/api/ui/accounting/remeasurement/' + id, decodeSchedule);
      if (
        epoch === this.session.invalidation() &&
        read === this.readSequence &&
        context === this.context()
      )
        this.review.set(r);
    } catch {
      if (epoch === this.session.invalidation() && read === this.readSequence) {
        this.cmd.failed.set(true);
        this.cmd.message.set('Workpaper is unavailable in the current scope.');
      }
    }
  }
  async approve(): Promise<void> {
    const r = this.review();
    if (!r?.canApprove || !this.approvalReviewed() || this.cmd.busy() || this.cmd.uncertain())
      return;
    const epoch = this.session.invalidation();
    await this.cmd.run(
      `/api/ui/accounting/remeasurement/${r.schedule.id}/approve`,
      { reviewToken: r.reviewToken, reviewed: true },
      'Independent approval recorded. No journal was posted.',
    );
    if (epoch !== this.session.invalidation()) return;
    this.approvalReviewed.set(false);
    await this.open(r.schedule.id);
    this.ws.reload();
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent): void {
    if (this.dirty() || this.cmd.busy()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
  async confirmNavigation(): Promise<boolean> {
    if (this.cmd.busy()) return false;
    if (!this.dirty()) return true;
    if (this.leaveDialog) return false;
    const epoch = this.session.invalidation(),
      context = this.context();
    this.leaveDialog = this.dialogs.open(UnsavedChangesDialog, { disableClose: true });
    const choice = await firstValueFrom(this.leaveDialog.afterClosed());
    this.leaveDialog = null;
    if (epoch !== this.session.invalidation() || context !== this.context()) return false;
    if (choice === 'save') return this.saveDraft(this.cmd.uncertain());
    if (choice === 'discard') {
      this.discardDraft();
      return true;
    }
    return false;
  }
}
