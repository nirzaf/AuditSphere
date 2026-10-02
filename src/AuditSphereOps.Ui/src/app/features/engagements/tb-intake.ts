import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, date, dec, decimalInput, decode, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeIntake = obj({ engagementId: guid, clientId: guid, datasets: arr(obj({ id: guid, periodCode: text, currency: text, importedAt: instant }), 50) });
export const decodePreview = obj({ fileSha256: text, canImport: bool, periods: arr(obj({ periodCode: text, periodId: nullable(guid), rowCount: nat,
  currency: nullable(text), netTotal: nullable(dec), balanced: bool, error: nullable(text) }), 120) });
const allocation = obj({ sourceAccountCode: text, destinationCode: text, statementSection: text, fraction: dec, rationale: text });
export const decodeMemory = obj({ datasetId: guid, sourceMappingVersionId: nullable(guid), sourceMappingVersion: nullable(nat),
  proposals: arr(obj({ accountCode: text, accountName: text, status: text, priorAccountName: nullable(text), allocations: arr(allocation, 100) }), 20000) });
const rate = obj({ fromCurrency: text, toCurrency: text, rateType: text, rateDate: date, rate: dec, direction: text, source: text, setVersion: nat });
export const decodeCurrencyReview = obj({ datasetId: guid, sourceCurrency: text, presentationCurrency: text, periodEnd: date, currentRate: nullable(rate),
  priorDatasetId: nullable(guid), priorRate: nullable(rate), thresholdPercent: dec, thresholdAmount: dec,
  lines: arr(obj({ accountCode: text, accountName: text, sourceAmount: dec, translatedAmount: dec, priorTranslatedAmount: nullable(dec),
    variance: nullable(dec), variancePercent: nullable(dec), highlighted: bool, highlightReason: nullable(text) }), 20000) });

@Component({
  selector: 'audit-tb-intake',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <audit-page-header title="Trial balance intake" eyebrow="Engagement"
      description="Upload one Excel or CSV file with several periods, reuse approved mappings, and review currency conversion and movements before mapping approval." />
    <a [routerLink]="['/app/engagements', id()]">← Back to engagement</a>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="trial balance intake" />
    @if (ws.data(); as w) {
      <section class="panel" aria-labelledby="mp-heading">
        <h2 id="mp-heading">Multi-period upload</h2>
        <p>Columns: PeriodCode, AccountCode, AccountName, NetClosingBalance (or Debit and Credit), Currency, Entity, MappingCode. Each period is validated and imported as its own dataset; nothing is imported unless every period passes.</p>
        <label>Trial balance file <input type="file" accept=".xlsx,.csv" (change)="pick($event)" [disabled]="cmd.busy()" /></label>
        @if (preview(); as p) {
          <div class="table-scroll"><table aria-label="Periods in the file">
            <thead><tr><th>Period</th><th class="number">Rows</th><th>Currency</th><th class="number">Net</th><th>Status</th></tr></thead>
            <tbody>@for (r of p.periods; track r.periodCode) {
              <tr><td>{{ r.periodCode }}</td><td class="number">{{ r.rowCount }}</td><td>{{ r.currency }}</td><td class="number">{{ r.netTotal | money }}</td>
                <td [class.error-text]="r.error">{{ r.error ?? 'Ready' }}</td></tr> }</tbody>
          </table></div>
          <p><small>File SHA-256 <code>{{ p.fileSha256 }}</code></small></p>
          <button matButton="filled" (click)="import()" [disabled]="cmd.busy() || !p.canImport">Import {{ p.periods.length }} period(s)</button>
        }
      </section>
      <section class="panel" aria-labelledby="mm-heading">
        <h2 id="mm-heading">Mapping memory and currency review</h2>
        <label>Dataset <select [(ngModel)]="datasetId" name="dataset" (ngModelChange)="memory.set(null); review.set(null)"><option value="">Select</option>
          @for (d of w.datasets; track d.id) { <option [value]="d.id">{{ d.periodCode }} · {{ d.currency }} · {{ d.importedAt.slice(0, 16).replace('T', ' ') }}</option> }</select></label>
        <div class="inline-form">
          <button matButton="outlined" (click)="propose()" [disabled]="cmd.busy() || !datasetId">Propose mapping from history</button>
          <label>Presentation currency <input [(ngModel)]="presentation" name="presentation" maxlength="3" /></label>
          <label>Highlight % <input [(ngModel)]="thresholdPercent" name="pct" inputmode="decimal" /></label>
          <label>and amount ≥ <input [(ngModel)]="thresholdAmount" name="amt" inputmode="decimal" /></label>
          <button matButton="outlined" (click)="reviewCurrency()" [disabled]="cmd.busy() || !datasetId">Review currency and movements</button>
        </div>
        @if (readError()) { <p role="alert" class="error-text">{{ readError() }}</p> }
        @if (memory(); as m) {
          <p>{{ m.sourceMappingVersion === null ? 'No approved mapping exists for this client yet; every account is new.' : 'Proposals from approved mapping v' + m.sourceMappingVersion + '.' }}</p>
          <div class="table-scroll"><table aria-label="Mapping proposals">
            <thead><tr><th>Account</th><th>Name</th><th>Status</th><th>Proposed destination</th></tr></thead>
            <tbody>@for (p of m.proposals; track p.accountCode) {
              <tr><td>{{ p.accountCode }}</td><td>{{ p.accountName }}{{ p.priorAccountName && p.status === 'NAME_CHANGED' ? ' (was ' + p.priorAccountName + ')' : '' }}</td>
                <td><audit-status [value]="p.status" /></td>
                <td>@if (p.allocations.length) { {{ describe(p.allocations) }} } @else {
                  <input [attr.aria-label]="'Destination for ' + p.accountCode" placeholder="Destination" [(ngModel)]="entry(p.accountCode).destination" [name]="'d-' + p.accountCode" />
                  <input [attr.aria-label]="'Section for ' + p.accountCode" placeholder="Section" [(ngModel)]="entry(p.accountCode).section" [name]="'s-' + p.accountCode" /> }</td></tr> }</tbody>
          </table></div>
          <div class="inline-form">
            <label>Taxonomy version <input [(ngModel)]="taxonomy" name="taxonomy" /></label>
            <button matButton="filled" (click)="createDraft(m.datasetId)" [disabled]="cmd.busy()">Create draft mapping for approval</button>
          </div>
        }
        @if (review(); as r) {
          @if (r.currentRate; as c) {
            <p>Rate: <strong>{{ c.rate }}</strong> {{ r.sourceCurrency }}→{{ r.presentationCurrency }} ({{ c.rateType }}, {{ c.rateDate }}, {{ c.direction }}, {{ c.source }} v{{ c.setVersion }}){{ r.priorRate ? '; prior period at ' + r.priorRate.rate + '.' : '; no prior period to compare.' }}</p>
          }
          <div class="table-scroll"><table aria-label="Currency review">
            <thead><tr><th>Account</th><th class="number">Source</th><th class="number">Converted</th><th class="number">Prior</th><th>Movement</th></tr></thead>
            <tbody>@for (l of r.lines; track l.accountCode) {
              <tr><td>{{ l.accountCode }} {{ l.accountName }}</td><td class="number">{{ l.sourceAmount | money }}</td><td class="number">{{ l.translatedAmount | money }}</td>
                <td class="number">{{ l.priorTranslatedAmount | money }}</td><td [class.error-text]="l.highlighted">{{ l.highlightReason ?? (l.variance | money) }}</td></tr> }</tbody>
          </table></div>
        }
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class TrialBalanceIntake {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/tb-intake` : null), decodeIntake,
    'Trial balance intake requires an accounting or engagement assignment.');
  readonly cmd = new CommandState(this.api);
  readonly preview = signal<ReturnType<typeof decodePreview> | null>(null);
  readonly memory = signal<ReturnType<typeof decodeMemory> | null>(null);
  readonly review = signal<ReturnType<typeof decodeCurrencyReview> | null>(null);
  readonly readError = signal('');
  private file: File | null = null;
  private readonly entries = new Map<string, { destination: string; section: string }>();
  datasetId = '';
  presentation = 'QAR';
  thresholdPercent = '10';
  thresholdAmount = '0';
  taxonomy = 'tax-v1';

  entry(code: string) {
    let e = this.entries.get(code);
    if (!e) this.entries.set(code, (e = { destination: '', section: '' }));
    return e;
  }
  describe(a: { destinationCode: string; statementSection: string }[]): string { return a.map((x) => `${x.destinationCode} (${x.statementSection})`).join(', '); }
  private form(): FormData { const f = new FormData(); f.set('file', this.file!); return f; }
  async pick(event: Event): Promise<void> {
    this.preview.set(null);
    const f = (event.target as HTMLInputElement).files?.[0] ?? null;
    if (!f || f.size === 0 || f.size > 25 * 1024 * 1024) { this.file = null; this.cmd.failed.set(true); this.cmd.message.set('Choose an Excel or CSV file up to 25 MB.'); return; }
    this.file = f;
    await this.cmd.run<unknown>(`/api/ui/engagements/${this.id()}/tb-intake/preview`, this.form(), '', (value) => {
      try { this.preview.set(decode(decodePreview, value)); } catch { this.cmd.failed.set(true); this.cmd.message.set('Unsupported preview response.'); }
    });
  }
  import(): void {
    if (!this.file) return;
    this.cmd.run<{ periodCode: string }[]>(`/api/ui/engagements/${this.id()}/tb-intake/import`, this.form(), 'Imported as separate datasets; each is validated by its own operation.', () => {
      this.preview.set(null); this.file = null;
    }).finally(() => this.ws.reload());
  }
  async propose(): Promise<void> {
    this.readError.set(''); this.review.set(null); this.entries.clear();
    try { this.memory.set(await this.api.get(`/api/ui/datasets/${this.datasetId}/mapping-memory`, decodeMemory)); } catch (e) { this.readError.set((e as Error).message); }
  }
  async reviewCurrency(): Promise<void> {
    this.readError.set(''); this.memory.set(null);
    const pct = decimalInput(this.thresholdPercent), amt = decimalInput(this.thresholdAmount);
    if (pct === null || amt === null) { this.readError.set('Enter thresholds as numbers.'); return; }
    try {
      this.review.set(await this.api.get(`/api/ui/datasets/${this.datasetId}/currency-review?presentation=${encodeURIComponent(this.presentation.trim().toUpperCase())}&percent=${pct}&amount=${amt}`, decodeCurrencyReview));
    } catch (e) { this.readError.set((e as Error).message); }
  }
  createDraft(datasetId: string): void {
    const allocations = [...this.entries].filter(([, v]) => v.destination.trim()).map(([accountCode, v]) => ({ accountCode, destination: v.destination, section: v.section }));
    this.cmd.run(`/api/ui/datasets/${datasetId}/draft-mapping`, { taxonomyVersion: this.taxonomy, allocations },
      'Draft mapping created; a reviewer must approve it before it becomes current.');
  }
}
