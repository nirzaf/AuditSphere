import { Component, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { exactDecimal } from '../../core/contracts';
import { arr, bool, dec, guid, nat, nullable, obj, oneOf, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

/** Read-only statement presentation (unapproved draft). Every amount is a display string produced by the server. */
export const decodePresentation = obj({
  status: oneOf('UNAPPROVED_DRAFT'),
  displayDecimals: nat,
  header: obj({ entity: text, framework: text, currency: text, currentPeriodEnd: text, priorPeriodEnd: nullable(text), reference: text }),
  manifest: obj({
    packageId: guid, calculationHash: text, layoutVersionId: guid, layoutHash: text,
    comparativePackageId: nullable(guid), comparativeCalculationHash: nullable(text), builderVersion: text, displayPolicy: text,
  }),
  statements: arr(obj({
    code: text, title: text,
    rows: arr(obj({
      kind: oneOf('HEADING', 'LINE', 'SUBTOTAL', 'TOTAL'), code: text, label: text, depth: nat,
      noteReference: nullable(text), current: nullable(dec), prior: nullable(dec),
    }), 500),
  }), 5),
  checks: arr(obj({ code: text, passed: bool, detail: text }), 50),
  warnings: arr(obj({ code: text, detail: text }), 200),
});

/** Long calendar date in UTC, matching the server's presentation text (for example "31 December 2025"). */
export function longDate(isoDate: string): string {
  return new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })
    .format(new Date(`${isoDate}T00:00:00Z`));
}

/**
 * Formats a server display amount the way the server text does: grouped digits, negatives in brackets, zero as a dash.
 * The value is never converted through floating point; a non-exact value is shown as an em dash.
 */
export function presentationAmount(value: string | null | undefined, decimals = 0): string {
  if (value === null || value === undefined) return 'Not available';
  if (!exactDecimal(value)) return '—';
  if (/^-?0+(\.0+)?$/.test(value)) return '–';
  const negative = value.startsWith('-');
  const [whole, fraction = ''] = (negative ? value.slice(1) : value).split('.');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  const body = decimals > 0 ? `${grouped}.${fraction.padEnd(decimals, '0').slice(0, decimals)}` : grouped;
  return negative ? `(${body})` : body;
}

@Component({
  selector: 'audit-statement-presentation',
  imports: [MatButtonModule, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="presentation-heading">
      <h2 id="presentation-heading">Statement presentation (unapproved draft)</h2>
      <p role="alert">Unapproved presentation draft: not reviewed, not sealed and not for issue. Figures are read from the exact persisted package through the published statement layout.</p>
      <button matButton="outlined" (click)="presentation.reload()">Refresh presentation</button>
      <audit-state [loading]="presentation.loading()" [error]="presentation.error()" label="the statement presentation" />
      @if (presentation.data(); as p) {
        <p role="status">{{ p.checks.length }} checks · {{ failedCount(p.checks) }} failed · {{ p.warnings.length }} rounding note(s)</p>
        <p class="actions" aria-label="Presentation exports">
          @for (format of formats; track format[0]) {
            <button matButton="outlined" (click)="download(format[0])" [disabled]="busy()">{{ format[1] }}</button>
          }
        </p>
        @if (status()) { <p role="status" aria-live="polite">{{ status() }}</p> }
        <p><small>Layout <code>{{ p.manifest.layoutVersionId }}</code> · layout hash <code>{{ p.manifest.layoutHash.slice(0, 12) }}</code> · calculation <code>{{ p.manifest.calculationHash.slice(0, 12) }}</code> · {{ p.manifest.displayPolicy }}</small></p>
        @for (statement of p.statements; track statement.code) {
          <h3>{{ statement.title }}</h3>
          <div class="table-scroll">
            <table>
              <caption class="sr-only">{{ statement.title }}</caption>
              <thead>
                <tr>
                  <th scope="col">Line</th>
                  <th scope="col">Note</th>
                  <th scope="col" class="number">{{ currentHeading(p, statement.code) }}</th>
                  <th scope="col" class="number">{{ priorHeading(p, statement.code) }}</th>
                </tr>
              </thead>
              <tbody>
                @for (row of statement.rows; track row.code) {
                  <tr [class.subtotal]="row.kind === 'SUBTOTAL'" [class.total]="row.kind === 'TOTAL'" [class.heading]="row.kind === 'HEADING'">
                    <th scope="row" [style.padding-left.rem]="row.depth * 1.25">{{ row.label }}</th>
                    <td>{{ row.noteReference ?? '' }}</td>
                    <td class="number">{{ row.kind === 'HEADING' ? '' : amount(row.current, p.displayDecimals) }}</td>
                    <td class="number">{{ row.kind === 'HEADING' ? '' : amount(row.prior, p.displayDecimals) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
        @if (p.warnings.length) {
          <h3>Rounding notes</h3>
          <ul>@for (warning of p.warnings; track warning.detail) { <li>{{ warning.detail }}</li> }</ul>
        }
        <details>
          <summary>Presentation checks</summary>
          <ul>@for (check of p.checks; track check.code) { <li>{{ check.passed ? 'Passed' : 'Failed' }} · {{ check.code }} · {{ check.detail }}</li> }</ul>
        </details>
      }
    </section>
  `,
})
export class StatementPresentation {
  private readonly api = inject(Api);
  readonly packageId = input.required<string>();
  readonly presentation = this.api.resource(() => (this.packageId() ? `/api/ui/accounting/packages/${this.packageId()}/presentation` : null),
    decodePresentation, 'The statement presentation is unavailable in your current scope.');
  readonly busy = signal(false);
  readonly status = signal('');
  readonly formats: [string, string][] = [['pdf', 'Download PDF draft'], ['docx', 'Download Word draft'], ['xlsx', 'Download Excel draft']];

  amount(value: string | null, decimals: number): string {
    return presentationAmount(value, decimals);
  }

  failedCount(checks: { passed: boolean }[]): number {
    return checks.filter((check) => !check.passed).length;
  }

  currentHeading(p: { header: { currentPeriodEnd: string } }, statementCode: string): string {
    return statementCode === 'PROFIT_OR_LOSS' ? `Year ended ${longDate(p.header.currentPeriodEnd)}` : `As at ${longDate(p.header.currentPeriodEnd)}`;
  }

  priorHeading(p: { header: { priorPeriodEnd: string | null } }, statementCode: string): string {
    if (!p.header.priorPeriodEnd) return 'Prior period not supplied';
    return statementCode === 'PROFIT_OR_LOSS' ? `Year ended ${longDate(p.header.priorPeriodEnd)}` : `As at ${longDate(p.header.priorPeriodEnd)}`;
  }

  async download(format: string): Promise<void> {
    if (this.busy()) return;
    const presentation = this.presentation.data();
    if (!presentation) {
      this.status.set('Refresh the presentation before exporting so the selected layout version is explicit.');
      return;
    }
    this.busy.set(true);
    this.status.set('Preparing the version-bound statement presentation…');
    try {
      const outcome = await this.api.download(`/api/ui/accounting/packages/${this.packageId()}/presentation/export`, {
        format,
        layoutVersionId: presentation.manifest.layoutVersionId,
      });
      this.status.set(outcome.ok
        ? 'Download started. This is an unapproved draft, not an issued statement.'
        : outcome.message);
    } finally {
      this.busy.set(false);
    }
  }
}
