import { Component, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { exactDecimal } from '../../core/contracts';
import { arr, bool, dec, decode, guid, nat, nullable, obj, oneOf, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

/** Read-only statement presentation (unapproved draft). Every amount is a display string produced by the server. */
export const decodePresentation = obj({
  status: oneOf('UNAPPROVED_DRAFT'),
  canPin: bool,
  displayDecimals: nat,
  header: obj({ entity: text, framework: text, currency: text, currentPeriodEnd: text, priorPeriodEnd: nullable(text), reference: text }),
  manifest: obj({
    packageId: guid, calculationHash: text, layoutVersionId: guid, layoutHash: text,
    comparativePackageId: nullable(guid), comparativeCalculationHash: nullable(text), builderVersion: text, displayPolicy: text,
    noteSourceManifestHash: nullable(text),
  }),
  statements: arr(obj({
    code: text, title: text,
    rows: arr(obj({
      kind: oneOf('HEADING', 'LINE', 'SUBTOTAL', 'TOTAL'), code: text, label: text, depth: nat,
      noteReference: nullable(text), current: nullable(dec), prior: nullable(dec),
    }), 500),
  }), 5),
  notes: arr(obj({
    number: nat, code: text,
    current: obj({ state: oneOf('NOT_SUPPLIED', 'PREPARER_SUPPLIED_UNREVIEWED', 'PREPARER_MARKED_NOT_APPLICABLE_UNREVIEWED'), text: nullable(text), rationale: nullable(text) }),
    comparative: obj({ state: oneOf('NOT_SUPPLIED', 'PREPARER_SUPPLIED_UNREVIEWED', 'PREPARER_MARKED_NOT_APPLICABLE_UNREVIEWED'), text: nullable(text), rationale: nullable(text) }),
    amounts: arr(obj({
      faceDestinationCode: text, label: text, statementSection: text, currency: text, current: nullable(dec), comparative: nullable(dec),
      currentEvidenceReference: nullable(text), comparativeEvidenceReference: nullable(text),
    }), 500),
  }), 1200),
  checks: arr(obj({ code: text, passed: bool, detail: text }), 50),
  warnings: arr(obj({ code: text, detail: text }), 200),
});

const decodePresentationPin = obj({
  revisionId: guid, packageId: guid, packageCalculationHash: text, periodId: nullable(guid), bookId: nullable(guid),
  basis: nullable(text), framework: text, taxonomyVersion: text, templateVersion: text, calculationEngineVersion: text,
  currency: text, periodStart: text, periodEnd: text, layoutVersionId: guid, layoutHash: text,
  comparativePackageId: nullable(guid), comparativeCalculationHash: nullable(text), comparativeBasis: nullable(text),
  comparativeEvidenceReference: nullable(text), noteSourceManifestHash: text, builderVersion: text,
  rendererVersion: text, displayPolicy: text, status: oneOf('UNAPPROVED_DRAFT'), revisionHash: text,
  snapshotSha256Hex: text, createdAt: text, sourceIsCurrent: bool, artifactFormats: arr(oneOf('pdf', 'docx', 'xlsx'), 3),
});

const decodePresentationPins = arr(decodePresentationPin, 50);
type PresentationFormat = 'pdf' | 'docx' | 'xlsx';

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
        <p role="status">{{ p.checks.length }} checks · {{ failedCount(p.checks) }} failed · {{ p.warnings.length }} readiness note(s)</p>
        <p class="actions" aria-label="Presentation exports">
          @if (p.canPin) { <button matButton="outlined" (click)="pinCurrent()" [disabled]="busy()">Save pinned draft</button> }
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
        <h3>Notes and disclosures · preparer supplied, unreviewed</h3>
        <p>Disclosure applicability and note content have not been independently reviewed or approved. Source references are preparer-entered and not independently verified.</p>
        @if (p.notes.length === 0) {
          <p>No package note disclosures or note schedules were supplied.</p>
        }
        @for (note of p.notes; track note.code) {
          <article class="panel" aria-label="Unreviewed financial statement note">
            <h4>Note {{ note.number }} · {{ note.code }}</h4>
            <p><strong>Current period · {{ note.current.state }}</strong></p>
            @if (note.current.text) { <p>{{ note.current.text }}</p> }
            @if (note.current.rationale) { <p>Preparer rationale: {{ note.current.rationale }}</p> }
            <p><strong>Comparative period · {{ note.comparative.state }}</strong></p>
            @if (note.comparative.text) { <p>{{ note.comparative.text }}</p> }
            @if (note.comparative.rationale) { <p>Preparer rationale: {{ note.comparative.rationale }}</p> }
            @if (note.amounts.length > 0) {
              <div class="table-scroll">
                <table>
                  <caption>Preparer supplied note schedule, unreviewed</caption>
                  <thead><tr><th scope="col">Face line</th><th scope="col">Statement</th><th scope="col">Currency</th><th scope="col" class="number">Current amount</th><th scope="col" class="number">Comparative amount</th></tr></thead>
                  <tbody>
                    @for (amountLine of note.amounts; track amountLine.faceDestinationCode) {
                      <tr>
                        <th scope="row">{{ amountLine.faceDestinationCode }} · {{ amountLine.label }}</th>
                        <td>{{ amountLine.statementSection }}</td>
                        <td>{{ amountLine.currency }}</td>
                        <td class="number">{{ amount(amountLine.current, p.displayDecimals) }}</td>
                        <td class="number">{{ amount(amountLine.comparative, p.displayDecimals) }}</td>
                      </tr>
                      @if (amountLine.currentEvidenceReference) { <tr><td colspan="5"><small>Current preparer reference, not independently verified: {{ amountLine.currentEvidenceReference }}</small></td></tr> }
                      @if (amountLine.comparativeEvidenceReference) { <tr><td colspan="5"><small>Comparative preparer reference, not independently verified: {{ amountLine.comparativeEvidenceReference }}</small></td></tr> }
                    }
                  </tbody>
                </table>
              </div>
            } @else {
              <p>No numeric note schedule was supplied for this note.</p>
            }
          </article>
        }
        @if (p.warnings.length) {
          <h3>Readiness and rounding notes</h3>
          <ul>@for (warning of p.warnings; track warning.detail) { <li>{{ warning.detail }}</li> }</ul>
        }
        <details>
          <summary>Presentation checks</summary>
          <ul>@for (check of p.checks; track check.code) { <li>{{ check.passed ? 'Passed' : 'Failed' }} · {{ check.code }} · {{ check.detail }}</li> }</ul>
        </details>
      }
      <h3>Saved pinned drafts</h3>
      <audit-state [loading]="pins.loading()" [error]="pins.error()" label="saved pinned drafts" />
      @for (pin of pins.data() ?? []; track pin.revisionId) {
        <article class="panel" aria-label="Saved statement presentation draft">
          <p><strong>Unapproved draft · {{ pin.sourceIsCurrent ? 'sources current' : 'source basis changed' }}</strong> · saved {{ pin.createdAt }}</p>
          <p><small>{{ pin.framework }} · taxonomy {{ pin.taxonomyVersion }} · template {{ pin.templateVersion }} · engine {{ pin.calculationEngineVersion }} · {{ pin.currency }} · {{ pin.periodStart }} to {{ pin.periodEnd }} · {{ pin.basis ?? 'basis not recorded' }}</small></p>
          <p><small>Period <code>{{ pin.periodId ?? 'not linked' }}</code> · book <code>{{ pin.bookId ?? 'not linked' }}</code> · comparative basis {{ pin.comparativeBasis ?? 'none' }}</small></p>
          <p><small>Revision <code>{{ pin.revisionId }}</code> · layout <code>{{ pin.layoutVersionId }}</code> · layout hash <code>{{ pin.layoutHash.slice(0, 12) }}</code></small></p>
          <p><small>Package hash <code>{{ pin.packageCalculationHash.slice(0, 12) }}</code> · note source <code>{{ pin.noteSourceManifestHash.slice(0, 12) }}</code> · snapshot hash <code>{{ pin.snapshotSha256Hex.slice(0, 12) }}</code> · {{ pin.displayPolicy }}</small></p>
          <p class="actions" aria-label="Saved draft formats">
            @for (format of formats; track format[0]) {
              <button matButton="outlined" (click)="downloadPinned(format[0], pin.revisionId)" [disabled]="busy() || (!pin.sourceIsCurrent && !pin.artifactFormats.includes(format[0]))">
                {{ pin.artifactFormats.includes(format[0]) ? 'Download saved' : 'Render and save' }} {{ format[0] }}
              </button>
            }
          </p>
        </article>
      }
    </section>
  `,
})
export class StatementPresentation {
  private readonly api = inject(Api);
  readonly packageId = input.required<string>();
  readonly presentation = this.api.resource(() => (this.packageId() ? `/api/ui/accounting/packages/${this.packageId()}/presentation` : null),
    decodePresentation, 'The statement presentation is unavailable in your current scope.');
  readonly pins = this.api.resource(() => (this.packageId() ? `/api/ui/accounting/packages/${this.packageId()}/presentation/pins` : null),
    decodePresentationPins, 'Saved statement drafts are unavailable in your current scope.');
  readonly busy = signal(false);
  readonly status = signal('');
  readonly formats: [PresentationFormat, string][] = [['pdf', 'Download PDF draft'], ['docx', 'Download Word draft'], ['xlsx', 'Download Excel draft']];

  amount(value: string | null, decimals: number): string {
    return presentationAmount(value, decimals);
  }

  failedCount(checks: { passed: boolean }[]): number {
    return checks.filter((check) => !check.passed).length;
  }

  currentHeading(p: { header: { currentPeriodEnd: string } }, statementCode: string): string {
    return this.isPeriodStatement(statementCode) ? `Year ended ${longDate(p.header.currentPeriodEnd)}` : `As at ${longDate(p.header.currentPeriodEnd)}`;
  }

  priorHeading(p: { header: { priorPeriodEnd: string | null } }, statementCode: string): string {
    if (!p.header.priorPeriodEnd) return 'Prior period not supplied';
    return this.isPeriodStatement(statementCode) ? `Year ended ${longDate(p.header.priorPeriodEnd)}` : `As at ${longDate(p.header.priorPeriodEnd)}`;
  }

  private isPeriodStatement(statementCode: string): boolean {
    return ['PROFIT_OR_LOSS', 'CASH_FLOWS', 'CHANGES_IN_EQUITY'].includes(statementCode);
  }

  async pinCurrent(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.status.set('Saving the exact unapproved draft snapshot…');
    try {
      const pin = await this.createPin();
      if (pin) this.status.set(`Pinned unapproved draft ${pin.revisionId}. It is not reviewed, sealed or issued.`);
    } finally {
      this.busy.set(false);
    }
  }

  async download(format: PresentationFormat): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.status.set('Pinning the exact draft and saving its rendered bytes…');
    try {
      const current = this.presentation.data();
      if (current && !current.canPin) {
        const outcome = await this.api.download(
          `/api/ui/accounting/packages/${this.packageId()}/presentation/export`,
          { format, layoutVersionId: current.manifest.layoutVersionId },
          (metadata) => metadata.fileName.endsWith(`.${format}`) &&
            /^[0-9a-f]{64}$/.test(metadata.headers['x-statement-presentation-sha256'] ?? ''));
        this.status.set(outcome.ok
          ? 'Draft preview downloaded. It was not pinned, reviewed, sealed or issued.'
          : outcome.message);
        return;
      }
      const pin = await this.createPin();
      if (!pin) return;
      await this.downloadPinnedBytes(format, pin.revisionId);
    } finally {
      this.busy.set(false);
    }
  }

  async downloadPinned(format: PresentationFormat, revisionId: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    try { await this.downloadPinnedBytes(format, revisionId); }
    finally { this.busy.set(false); }
  }

  private async createPin(): Promise<{ revisionId: string } | null> {
    const presentation = this.presentation.data();
    if (!presentation) {
      this.status.set('Refresh the presentation before saving it so the exact layout version is selected.');
      return null;
    }
    const result = await this.api.command<unknown>(
      `/api/ui/accounting/packages/${this.packageId()}/presentation/pins`,
      { layoutVersionId: presentation.manifest.layoutVersionId });
    if (!result.ok) {
      this.status.set(result.message);
      return null;
    }
    try {
      const pin = decode(decodePresentationPin, result.value);
      this.pins.reload();
      return pin;
    } catch {
      this.status.set('The saved draft response was unsupported. Refresh saved drafts before retrying.');
      return null;
    }
  }

  private async downloadPinnedBytes(format: PresentationFormat, revisionId: string): Promise<void> {
    this.status.set('Rendering or retrieving the exact saved draft bytes…');
    const outcome = await this.api.download(
      `/api/ui/accounting/packages/${this.packageId()}/presentation/pins/${revisionId}/artifacts`,
      { format },
      (metadata) => metadata.fileName.endsWith(`.${format}`) &&
        metadata.headers['x-statement-presentation-revision'] === revisionId &&
        /^[0-9a-f]{64}$/.test(metadata.headers['x-statement-presentation-sha256'] ?? ''));
    if (outcome.ok) this.pins.reload();
    this.status.set(outcome.ok
      ? `Saved draft ${revisionId} downloaded. It remains unapproved and is not an issued statement.`
      : outcome.message);
  }
}
