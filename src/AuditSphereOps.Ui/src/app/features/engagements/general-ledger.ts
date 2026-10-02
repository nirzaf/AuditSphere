import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { form, FormField, maxLength } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import {
  decodeLedgerCatalogue,
  decodeLedgerJournal,
  decodeLedgerSource,
} from './general-ledger-contracts';
const blank = () => ({ account: '', from: '', to: '', journal: '', counterparty: '' });
@Component({
  selector: 'audit-general-ledger',
  imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  template: `
    <audit-page-header
      title="General ledger source inspection"
      description="Inspect sealed client ledger records with bounded server searches. Human review and completeness remain separate gates."
    />
    @if (id()) {
      <a matButton [routerLink]="['/app/engagements', id()]">Back to engagement</a>
    } @else {
      <p role="alert">Choose an available engagement from the portfolio.</p>
    }
    <button matButton="outlined" (click)="refreshCatalogue()">Refresh ledger sources</button>
    <audit-state
      [loading]="catalogue.loading()"
      [error]="catalogue.error()"
      label="ledger sources"
    />
    @if (catalogue.data(); as c) {
      <a matButton [routerLink]="['/app/engagements', c.engagementId, 'general-ledger', 'upload']">Upload general ledger</a>
      <section class="panel" aria-labelledby="catalogue-heading">
        <h2 id="catalogue-heading">Sealed ledger sources</h2>
        <p>
          Client <code>{{ c.clientId }}</code> · Engagement <code>{{ c.engagementId }}</code>
        </p>
        @for (item of c.items; track item.id) {
          <article class="source-choice">
            <div>
              <strong>{{ item.entity }} · {{ item.periodCode }} · {{ item.currency }}</strong>
              <p>
                <code>{{ item.id }}</code> · {{ item.rowCount }} source rows · {{ item.importedAt }}
              </p>
            </div>
            <button
              matButton="outlined"
              (click)="choose(item.id)"
              [attr.aria-pressed]="batchId() === item.id"
              [attr.aria-label]="'Inspect ledger ' + item.id"
            >
              Inspect source
            </button>
          </article>
        } @empty {
          <p>No sealed general ledger sources are available in this engagement.</p>
        }
        <nav class="actions" aria-label="Ledger source pages">
          <button matButton (click)="changeCatalogue(-1)" [disabled]="c.page === 1">
            Previous ledger sources
          </button>
          <span
            >Page {{ c.page }} of {{ pages(c.totalCount, 20) }} · {{ c.totalCount }} sources</span
          >
          <button
            matButton
            (click)="changeCatalogue(1)"
            [disabled]="c.page >= pages(c.totalCount, 20)"
          >
            Next ledger sources
          </button>
        </nav>
      </section>
    }
    @if (batchId()) {
      <section class="panel" aria-labelledby="ledger-heading">
        <h2 id="ledger-heading">Ledger records</h2>
        <form class="filters" (submit)="$event.preventDefault(); apply()">
          <label>Account code prefix <input [formField]="fields.account" /></label>
          <label>Posted from <input type="date" [formField]="fields.from" /></label>
          <label>Posted to <input type="date" [formField]="fields.to" /></label>
          <label>Stable journal ID <input [formField]="fields.journal" /></label>
          <label>Intercompany counterparty <input [formField]="fields.counterparty" /></label>
          <div class="actions">
            <button matButton="filled" type="submit" [disabled]="!validFilters()">
              Apply ledger filters
            </button>
            <button matButton type="button" (click)="refreshSource()">
              Refresh ledger records
            </button>
          </div>
        </form>
        @if (!validFilters()) {
          <p role="alert">Use bounded filters and a valid posting date range.</p>
        }
        @if (stale()) {
          <p role="status">Filters changed. Apply them to inspect the current result.</p>
        }
        <audit-state [loading]="source.loading()" [error]="source.error()" label="ledger records" />
        @if (source.data(); as s) {
          <a
            matButton
            [routerLink]="['/app/accounting/gl-sources', s.context.batchId, 'acceptance']"
            >Review general ledger source acceptance</a
          >
          <a matButton [routerLink]="['/app/accounting/gl-sources', s.context.batchId, 'completeness']">Prepare and review GL completeness</a>
          <dl>
            <dt>Immutable source</dt>
            <dd>
              <code>{{ s.context.batchId }}</code>
            </dd>
            <dt>Reporting context</dt>
            <dd>{{ s.context.entity }} · {{ s.context.periodCode }} · {{ s.context.currency }}</dd>
            <dt>Source state</dt>
            <dd><audit-status [value]="s.context.importState" /></dd>
            <dt>File digest</dt>
            <dd>
              <code>{{ s.context.rawFileSha256 }}</code>
            </dd>
            <dt>Normalized source digest</dt>
            <dd>
              <code>{{ s.context.sourceHash }}</code>
            </dd>
            <dt>Import profile / parser</dt>
            <dd>{{ s.context.profileVersion }} / {{ s.context.parserVersion }}</dd>
            <dt>Imported by / at</dt>
            <dd>
              <code>{{ s.context.importedByUserId }}</code> · {{ s.context.importedAt }}
            </dd>
          </dl>
          @if (s.context.selected; as selected) {
            <p>
              {{
                selected.importBatchId === s.context.batchId
                  ? 'This is the independently selected GL source.'
                  : 'Another GL source is independently selected.'
              }}
            </p>
            <p>
              Selected source <code>{{ selected.importBatchId }}</code> · Decision
              <code>{{ selected.decisionId }}</code> · Reviewer
              <code>{{ selected.acceptedByUserId }}</code> · {{ selected.acceptedAt }} · Generation
              {{ selected.inputGeneration }}
            </p>
          } @else {
            <p>No GL source has been independently accepted for this engagement.</p>
          }
          <p>
            Selection is per engagement and source kind, across reporting periods. Sealing and
            debit/credit balance do not prove completeness, acceptance or a professional conclusion.
          </p>
          @if (!stale()) {
            <p>
              {{ s.rows.totalCount }} filtered lines · Debit {{ s.rows.totalDebit }} · Credit
              {{ s.rows.totalCredit }} ·
              {{ s.rows.balanced ? 'Arithmetic balance' : 'Debit/credit difference' }}
            </p>
            <div class="table-scroll" tabindex="0" role="region" aria-label="General ledger lines">
              <table>
                <caption>
                  Exact amounts for the filtered ledger population
                </caption>
                <thead>
                  <tr>
                    <th scope="col">Posting date</th>
                    <th scope="col">Journal / line</th>
                    <th scope="col">Account</th>
                    <th scope="col">Debit</th>
                    <th scope="col">Credit</th>
                    <th scope="col">Functional amount</th>
                    <th scope="col">Original amount</th>
                    <th scope="col">Dimensions</th>
                    <th scope="col">Review</th>
                  </tr>
                </thead>
                <tbody>
                  @for (r of s.rows.items; track r.lineId) {
                    <tr>
                      <td>{{ r.postingDate }}</td>
                      <td>{{ r.stableJournalId }} / {{ r.stableLineId }}</td>
                      <td>{{ r.accountCode }}</td>
                      <td>{{ r.debit }}</td>
                      <td>{{ r.credit }}</td>
                      <td>{{ r.functionalAmount }}</td>
                      <td>{{ r.originalAmount }} {{ r.originalCurrency }}</td>
                      <td>
                        {{ r.branch }} / {{ r.costCentre }} / {{ r.department }} / {{ r.project }} /
                        {{ r.counterparty }}
                      </td>
                      <td>
                        <button
                          matButton
                          (click)="journalId.set(r.stableJournalId)"
                          [attr.aria-label]="
                            'Inspect journal ' + r.stableJournalId + ' from line ' + r.stableLineId
                          "
                        >
                          Inspect journal
                        </button>
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td colspan="9">No lines match these filters.</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <nav class="actions" aria-label="Ledger line pages">
              <button matButton (click)="changeRows(-1)" [disabled]="s.rows.page === 1">
                Previous ledger lines
              </button>
              <span>Page {{ s.rows.page }} of {{ pages(s.rows.totalCount, 100) }}</span>
              <button
                matButton
                (click)="changeRows(1)"
                [disabled]="s.rows.page >= pages(s.rows.totalCount, 100)"
              >
                Next ledger lines
              </button>
            </nav>
          }
        }
      </section>
    }
    @if (journalId() && !stale()) {
      <section class="panel" aria-labelledby="journal-heading">
        <h2 id="journal-heading">Journal detail</h2>
        <button matButton (click)="journalId.set(null)">Close journal detail</button>
        <audit-state
          [loading]="journal.loading()"
          [error]="journal.error()"
          label="journal detail"
        />
        @if (journal.data(); as j) {
          <p>
            {{ j.journal.stableJournalId }} · {{ j.journal.documentNumber }} ·
            {{ j.journal.postingDate }} · {{ j.journal.currency }}
          </p>
          <p>
            {{ j.journal.isManual ? 'Manual journal' : 'Source journal' }} ·
            {{ j.journal.isYearEnd ? 'Year end' : 'In-period' }}
            @if (j.journal.reversalReference) {
              · Reversal {{ j.journal.reversalReference }}
            }
          </p>
          <p>
            All {{ j.journal.lineCount }} lines in this journal · Debit {{ j.journal.totalDebit }} ·
            Credit {{ j.journal.totalCredit }} ·
            {{ j.journal.balanced ? 'Arithmetic balance' : 'Debit/credit difference' }}
          </p>
          <p>
            Interactive journals are limited to {{ j.journalLineLimit }} lines. Oversized journals
            are refused without truncating the verdict.
          </p>
          <div class="table-scroll" tabindex="0" role="region" aria-label="Complete journal lines">
            <table>
              <caption>
                Every line of this bounded source journal
              </caption>
              <thead>
                <tr>
                  <th scope="col">Line</th>
                  <th scope="col">Account</th>
                  <th scope="col">Debit</th>
                  <th scope="col">Credit</th>
                  <th scope="col">Functional amount</th>
                  <th scope="col">Original amount</th>
                  <th scope="col">Counterparty</th>
                </tr>
              </thead>
              <tbody>
                @for (r of j.journal.lines; track r.lineId) {
                  <tr>
                    <td>{{ r.stableLineId }}</td>
                    <td>{{ r.accountCode }}</td>
                    <td>{{ r.debit }}</td>
                    <td>{{ r.credit }}</td>
                    <td>{{ r.functionalAmount }}</td>
                    <td>{{ r.originalAmount }} {{ r.originalCurrency }}</td>
                    <td>{{ r.counterparty }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    }
  `,
  styles: [
    '.filters{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:1rem}.filters label{display:flex;flex-direction:column;gap:.4rem}.source-choice{display:flex;flex-wrap:wrap;justify-content:space-between;gap:1rem}dl dd,code,p{overflow-wrap:anywhere}table{min-width:950px}.table-scroll:focus-visible{outline:2px solid var(--mat-sys-primary)}',
  ],
})
export class GeneralLedger {
  readonly id = routeGuid();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly cataloguePage = signal(1);
  readonly batchId = signal<string | null>(null);
  readonly rowPage = signal(1);
  readonly journalId = signal<string | null>(null);
  readonly model = signal(blank());
  readonly fields = form(this.model, (p) => {
    maxLength(p.account, 100);
    maxLength(p.journal, 200);
    maxLength(p.counterparty, 200);
    maxLength(p.from, 10);
    maxLength(p.to, 10);
  });
  readonly applied = signal(blank());
  readonly stale = computed(
    () => JSON.stringify(this.normalized()) !== JSON.stringify(this.applied()),
  );
  readonly validFilters = computed(
    () =>
      this.fields().valid() &&
      ['', this.model().from, this.model().to].every((d) => !d || /^\d{4}-\d{2}-\d{2}$/.test(d)) &&
      (!this.model().from || !this.model().to || this.model().from <= this.model().to),
  );
  readonly catalogue = this.api.resource(
    () => (this.id() ? `${this.base()}?page=${this.cataloguePage()}` : null),
    (raw) => {
      const c = decodeLedgerCatalogue(raw);
      if (c.engagementId !== this.id() || c.page !== this.cataloguePage())
        throw new Error('Wrong ledger catalogue');
      return c;
    },
  );
  readonly source = this.api.resource(
    () =>
      this.batchId() && this.catalogue.data()
        ? `${this.base()}/${this.batchId()}?${this.filtersQuery()}`
        : null,
    (raw) => {
      const s = decodeLedgerSource(raw);
      const chosen = this.catalogue.data()?.items.find((x) => x.id === this.batchId());
      if (
        !chosen ||
        chosen.sourceHash !== s.context.sourceHash ||
        chosen.periodId !== s.context.periodId ||
        chosen.periodCode !== s.context.periodCode ||
        chosen.bookId !== s.context.bookId ||
        chosen.currency !== s.context.currency ||
        chosen.entity !== s.context.entity ||
        s.context.engagementId !== this.id() ||
        s.context.batchId !== this.batchId() ||
        s.context.clientId !== this.catalogue.data()?.clientId ||
        s.rows.page !== this.rowPage() ||
        JSON.stringify(s.filter) !== JSON.stringify(this.expectedFilter())
      )
        throw new Error('Wrong ledger source');
      return s;
    },
  );
  readonly journal = this.api.resource(
    () =>
      this.journalId() && this.source.data() && !this.stale()
        ? `${this.base()}/${this.batchId()}/journal?journal=${encodeURIComponent(this.journalId()!)}&source=${this.source.data()!.context.sourceHash}&selection=${this.source.data()!.context.selected?.decisionId ?? ''}`
        : null,
    (raw) => {
      const j = decodeLedgerJournal(raw);
      if (
        j.journal.stableJournalId !== this.journalId() ||
        JSON.stringify(j.context) !== JSON.stringify(this.source.data()?.context)
      )
        throw new Error('Wrong journal context');
      return j;
    },
  );
  constructor() {
    effect(() => {
      this.id();
      this.session.invalidation();
      untracked(() => {
        this.batchId.set(null);
        this.journalId.set(null);
        this.cataloguePage.set(1);
        this.rowPage.set(1);
        this.model.set(blank());
        this.applied.set(blank());
      });
    });
  }
  private base() {
    return `/api/ui/engagements/${this.id()}/general-ledger`;
  }
  private normalized() {
    const m = this.model();
    return {
      account: m.account.trim(),
      from: m.from,
      to: m.to,
      journal: m.journal.trim(),
      counterparty: m.counterparty.trim(),
    };
  }
  private expectedFilter() {
    const a = this.applied();
    return {
      accountCodePrefix: a.account,
      postedFrom: a.from || null,
      postedTo: a.to || null,
      stableJournalId: a.journal,
      counterparty: a.counterparty,
    };
  }
  private filtersQuery() {
    const a = this.applied();
    const q = new URLSearchParams({
      account: a.account,
      journal: a.journal,
      counterparty: a.counterparty,
      page: String(this.rowPage()),
    });
    if (a.from) q.set('from', a.from);
    if (a.to) q.set('to', a.to);
    return q.toString();
  }
  pages(total: number, size: number) {
    return Math.max(1, Math.ceil(total / size));
  }
  choose(id: string) {
    if (!this.catalogue.data()?.items.some((x) => x.id === id)) return;
    this.journalId.set(null);
    this.rowPage.set(1);
    this.model.set(blank());
    this.applied.set(blank());
    this.batchId.set(id);
  }
  apply() {
    if (!this.validFilters()) return;
    this.journalId.set(null);
    this.rowPage.set(1);
    const previous = JSON.stringify(this.applied());
    this.applied.set(this.normalized());
    if (JSON.stringify(this.applied()) === previous) this.source.reload();
  }
  refreshSource() {
    this.journalId.set(null);
    this.source.reload();
  }
  refreshCatalogue() {
    this.batchId.set(null);
    this.journalId.set(null);
    this.catalogue.reload();
  }
  changeRows(delta: number) {
    const r = this.source.data()?.rows;
    if (!r || this.stale()) return;
    const page = r.page + delta;
    if (page < 1 || page > this.pages(r.totalCount, 100)) return;
    this.journalId.set(null);
    this.rowPage.set(page);
  }
  changeCatalogue(delta: number) {
    const c = this.catalogue.data();
    if (!c) return;
    const page = c.page + delta;
    if (page < 1 || page > this.pages(c.totalCount, 20)) return;
    this.batchId.set(null);
    this.journalId.set(null);
    this.cataloguePage.set(page);
  }
}
