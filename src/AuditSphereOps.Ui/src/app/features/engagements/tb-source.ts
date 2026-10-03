import {
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { form, FormField, maxLength } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { arr, bool, dec, guid, instant, nat, nullable, obj, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const shape = obj({
  datasetId: guid,
  clientId: guid,
  engagementId: guid,
  periodId: nullable(guid),
  periodCode: nullable(str(100)),
  revision: nat,
  currency: str(10),
  entity: str(500),
  sourceKind: str(50),
  importState: str(50),
  validationStatus: str(50),
  balanced: bool,
  rawFileSha256: str(64),
  normalizedDigest: str(100),
  accountCodeFilter: str(100),
  rows: obj({
    items: arr(
      obj({
        id: guid,
        datasetId: guid,
        accountCode: str(500),
        accountName: str(2000),
        amount: dec,
        sourceDebit: nullable(dec),
        sourceCredit: nullable(dec),
        currency: str(10),
        entity: str(500),
        mappingCode: nullable(str(500)),
      }),
      100,
    ),
    totalCount: nat,
    page: nat,
    pageSize: nat,
    totalAmount: dec,
    totalDebit: nullable(dec),
    totalCredit: nullable(dec),
  }),
  issues: obj({
    items: arr(
      obj({
        id: guid,
        rowKey: str(500),
        severity: str(50),
        code: str(100),
        message: str(2000),
        createdAt: instant,
      }),
      100,
    ),
    totalCount: nat,
    page: nat,
    pageSize: nat,
  }),
  exportRowLimit: nat,
  exportByteLimit: nat,
});
export function decodeSource(raw: unknown) {
  const v = shape(raw, '');
  if (
    v.revision < 1 ||
    v.importState !== 'SEALED' ||
    v.rows.page < 1 ||
    v.issues.page < 1 ||
    v.rows.pageSize !== 100 ||
    v.issues.pageSize !== 100 ||
    v.rows.items.some((r) => r.datasetId !== v.datasetId) ||
    v.rows.items.length > v.rows.totalCount ||
    v.issues.items.length > v.issues.totalCount ||
    v.exportRowLimit !== 50000 ||
    v.exportByteLimit !== 25 * 1024 * 1024 ||
    (v.rawFileSha256 && !/^[a-f0-9]{64}$/i.test(v.rawFileSha256))
  )
    throw new Error('Unsupported source response');
  return v;
}

@Component({
  selector: 'audit-tb-source',
  imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="source-heading">
      <h2 id="source-heading">Imported source records</h2>
      @if (!datasetId()) {
        <p>Select a dataset to inspect its sealed rows and persisted validation issues.</p>
      } @else {
        <form (submit)="$event.preventDefault(); applyFilter()" class="inline-form">
          <label>Account code prefix <input [formField]="fields.filter" /></label>
          <button
            matButton="outlined"
            type="submit"
            [disabled]="!fields().valid() || downloading()"
          >
            Apply source filter
          </button>
          <button matButton type="button" (click)="refresh()" [disabled]="downloading()">
            Refresh source
          </button>
        </form>
        @if (stale()) {
          <p role="status">Filter edited. Apply it before exporting or reading another page.</p>
        }
        <audit-state
          [loading]="source.loading()"
          [error]="source.error()"
          label="scoped source records"
        />
        @if (source.data(); as s) {
          <a matButton [routerLink]="['/app/accounting/sources', s.datasetId, 'acceptance']"
            >Review source acceptance</a
          >
          <a matButton [routerLink]="['/app/accounting/sources', s.datasetId, 'journal-draft']">Prepare new adjustment journal</a>
          <a matButton [routerLink]="['/app/accounting/sources', s.datasetId, 'adjustment-plan']">Prepare adjustment plan</a>
          <dl class="source-context">
            <dt>Source</dt>
            <dd>
              <code>{{ s.datasetId }}</code> · revision {{ s.revision }}
            </dd>
            <dt>Client</dt>
            <dd>
              <code>{{ s.clientId }}</code>
            </dd>
            <dt>Engagement</dt>
            <dd>
              <code>{{ s.engagementId }}</code>
            </dd>
            <dt>Period</dt>
            <dd>
              {{ s.periodCode ?? 'Legacy period not recorded' }}
              @if (s.periodId) {
                <code>{{ s.periodId }}</code>
              }
            </dd>
            <dt>Context</dt>
            <dd>{{ s.currency }} · {{ s.entity || 'Entity not recorded' }} · {{ s.sourceKind }}</dd>
            <dt>Sealing</dt>
            <dd><audit-status [value]="s.importState" /></dd>
            <dt>Validation</dt>
            <dd>
              <audit-status [value]="s.validationStatus" /> ·
              {{ s.balanced ? 'Balanced' : 'Unbalanced' }}
            </dd>
            <dt>Import bytes SHA-256</dt>
            <dd>
              <code>{{ s.rawFileSha256 || 'Not recorded for this legacy source' }}</code>
            </dd>
            <dt>Normalized source digest</dt>
            <dd>
              <code>{{ s.normalizedDigest || 'Not recorded for this legacy source' }}</code>
            </dd>
          </dl>
          <p>
            Source inspection and export do not approve GL completeness, a mapping or financial
            statements.
          </p>
          @if (s.validationStatus !== 'Accepted' || !s.balanced) {
            <p role="status">
              This source is not eligible for mapping. Review persisted issues or refresh while
              validation is pending.
            </p>
          }
          <p>
            {{ s.rows.totalCount }} matching rows · exact filtered net {{ s.rows.totalAmount }}
            {{ s.currency }} · debit {{ s.rows.totalDebit ?? 'Not supplied' }} · credit
            {{ s.rows.totalCredit ?? 'Not supplied' }}
          </p>
          <div
            class="table-scroll"
            tabindex="0"
            role="region"
            aria-label="Scrollable trial balance source rows"
          >
            <table>
              <caption>
                Trial balance source rows
              </caption>
              <thead>
                <tr>
                  <th>Account</th>
                  <th>Name</th>
                  <th>Signed amount</th>
                  <th>Debit</th>
                  <th>Credit</th>
                  <th>Currency</th>
                  <th>Entity</th>
                  <th>Mapping code</th>
                </tr>
              </thead>
              <tbody>
                @for (r of s.rows.items; track r.id) {
                  <tr>
                    <td>{{ r.accountCode }}</td>
                    <td>{{ r.accountName }}</td>
                    <td class="number">{{ r.amount }}</td>
                    <td class="number">{{ r.sourceDebit ?? '—' }}</td>
                    <td class="number">{{ r.sourceCredit ?? '—' }}</td>
                    <td>{{ r.currency }}</td>
                    <td>{{ r.entity }}</td>
                    <td>{{ r.mappingCode ?? '—' }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="8">No source rows match this filter.</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <nav class="actions" aria-label="Source row pages">
            <button
              matButton
              type="button"
              (click)="rowPage.set(rowPage() - 1)"
              [disabled]="stale() || downloading() || s.rows.page === 1"
            >
              Previous source rows
            </button>
            <span>Page {{ s.rows.page }} of {{ pages(s.rows.totalCount) }}</span>
            <button
              matButton
              type="button"
              (click)="rowPage.set(rowPage() + 1)"
              [disabled]="stale() || downloading() || s.rows.page >= pages(s.rows.totalCount)"
            >
              Next source rows
            </button>
          </nav>
          <h3>Persisted validation issues</h3>
          <p>{{ s.issues.totalCount }} issues for this exact source revision.</p>
          @for (i of s.issues.items; track i.id) {
            <article>
              <strong>{{ i.rowKey }} · {{ i.severity }} · {{ i.code }}</strong>
              <p>{{ i.message }}</p>
            </article>
          } @empty {
            <p>No persisted issues in this page. Pending validation is not acceptance.</p>
          }
          <nav class="actions" aria-label="Validation issue pages">
            <button
              matButton
              type="button"
              (click)="issuePage.set(issuePage() - 1)"
              [disabled]="stale() || downloading() || s.issues.page === 1"
            >
              Previous validation issues
            </button>
            <span>Page {{ s.issues.page }} of {{ pages(s.issues.totalCount) }}</span>
            <button
              matButton
              type="button"
              (click)="issuePage.set(issuePage() + 1)"
              [disabled]="stale() || downloading() || s.issues.page >= pages(s.issues.totalCount)"
            >
              Next validation issues
            </button>
          </nav>
          <p>
            CSV exports the complete sealed source, ignoring this view's filter. Limit:
            {{ s.exportRowLimit }} rows and {{ s.exportByteLimit }} bytes. Exact amounts and dataset
            revision are retained; text formula prefixes are escaped.
          </p>
          <button
            matButton="outlined"
            type="button"
            (click)="exportSource()"
            [disabled]="stale() || downloading()"
          >
            {{ downloading() ? 'Exporting source…' : 'Export complete source CSV' }}
          </button>
        }
        @if (message()) {
          <p [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p>
        }
      }
    </section>
  `,
  styles: [
    'table { min-width: 900px; } .source-context dd { overflow-wrap: anywhere; } .source-context code { display: inline-block; } .table-scroll:focus-visible { outline: 2px solid var(--mat-sys-primary); }',
  ],
})
export class TrialBalanceSource {
  readonly datasetId = input<string | null>(null);
  readonly clientId = input.required<string>();
  readonly engagementId = input.required<string>();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly model = signal({ filter: '' });
  readonly fields = form(this.model, (p) => maxLength(p.filter, 100));
  readonly appliedFilter = signal('');
  readonly rowPage = signal(1);
  readonly issuePage = signal(1);
  readonly downloading = signal(false);
  readonly message = signal('');
  readonly failed = signal(false);
  readonly stale = computed(() => this.model().filter.trim() !== this.appliedFilter());
  private generation = 0;
  readonly source = this.api.resource(
    () => {
      const id = this.datasetId();
      return id
        ? `/api/ui/datasets/${id}/source?filter=${encodeURIComponent(this.appliedFilter())}&page=${this.rowPage()}&issuePage=${this.issuePage()}`
        : null;
    },
    (raw) => {
      const s = decodeSource(raw);
      if (
        s.datasetId !== this.datasetId() ||
        s.clientId !== this.clientId() ||
        s.engagementId !== this.engagementId() ||
        s.accountCodeFilter !== this.appliedFilter() ||
        s.rows.page !== this.rowPage() ||
        s.issues.page !== this.issuePage()
      )
        throw new Error('Unsupported source context');
      return s;
    },
  );
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.generation++;
    });
    effect(() => {
      this.datasetId();
      this.clientId();
      this.engagementId();
      this.session.invalidation();
      untracked(() => {
        this.generation++;
        this.model.set({ filter: '' });
        this.appliedFilter.set('');
        this.rowPage.set(1);
        this.issuePage.set(1);
        this.message.set('');
        this.failed.set(false);
        this.downloading.set(false);
      });
    });
  }
  applyFilter(): void {
    if (!this.fields().valid() || this.downloading()) return;
    this.appliedFilter.set(this.model().filter.trim());
    this.rowPage.set(1);
    this.message.set('');
  }
  refresh(): void {
    this.message.set('');
    this.source.reload();
  }
  pages(n: number): number {
    return Math.max(1, Math.ceil(n / 100));
  }
  async exportSource(): Promise<void> {
    const s = this.source.data();
    if (!s || this.stale() || this.downloading() || !this.session.current()?.staff) return;
    const sequence = this.generation,
      epoch = this.session.invalidation();
    this.downloading.set(true);
    this.message.set('');
    try {
      const result = await this.api.download(
        `/api/ui/datasets/${s.datasetId}/source/export`,
        { revision: s.revision },
        (m) =>
          sequence === this.generation &&
          epoch === this.session.invalidation() &&
          this.source.data() === s &&
          !this.stale() &&
          m.headers['x-dataset-id'] === s.datasetId &&
          m.headers['x-dataset-revision'] === String(s.revision) &&
          m.fileName === `auditsphere-tb-${s.datasetId}-r${s.revision}.csv` &&
          m.contentType.startsWith('text/csv') &&
          m.byteCount <= s.exportByteLimit,
      );
      if (sequence !== this.generation || epoch !== this.session.invalidation()) return;
      this.failed.set(!result.ok);
      this.message.set(
        result.ok
          ? 'Complete source CSV saved. This does not grant source acceptance or mapping approval.'
          : result.message,
      );
    } finally {
      if (sequence === this.generation) this.downloading.set(false);
    }
  }
}
