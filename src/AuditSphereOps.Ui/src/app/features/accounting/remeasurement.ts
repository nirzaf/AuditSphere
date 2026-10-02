import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { Drafts } from '../../core/drafts';
import { arr, bool, date, dec, decimalInput, guid, guid as g, instant, nat, nullable, obj, text } from '../../core/decode';
import { guidPattern } from '../../core/contracts';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

export const decodeRemeasurementWorkspace = obj({
  contexts: arr(obj({ periodId: guid, clientId: guid, engagementId: guid, clientName: text, periodCode: text, endDate: date, engagementLabel: text }), 20000),
  history: arr(obj({ id: guid, clientName: text, periodCode: text, status: text, createdAt: instant }), 50) });
export const decodeChoices = obj({ functionalCurrency: text, rateSets: arr(obj({ id: g, label: text }), 500), policies: arr(obj({ id: g, label: text }), 500),
  glLines: arr(obj({ id: g, currency: text, originalAmount: dec, functionalAmount: dec, label: text }), 500) });
export const decodeSchedule = obj({ id: guid, clientId: guid, engagementId: guid, periodId: guid, asOfDate: date, functionalCurrency: text, inputHash: text, itemCount: nat,
  totalForeignExchangeAdjustment: dec, status: text, createdByUserId: guid, approvedByUserId: nullable(guid),
  items: arr(obj({ stableItemReference: text, evidenceSnapshotId: guid, evidenceSha256: text, sourceGeneralLedgerLineId: nullable(guid), sourceGlLineDigest: text,
    isMonetary: bool, foreignCurrency: text, foreignCurrencyAmount: dec, priorFunctionalCarryingAmount: dec, rateDate: date, rateType: text, appliedRate: dec,
    remeasuredFunctionalAmount: dec, foreignExchangeAdjustment: dec, roundingAdjustment: dec }), 500) });
interface Line { reference: string; snapshotId: string; sourceLineId: string; isMonetary: boolean; currency: string; foreignAmount: string; priorCarrying: string; historicalDate: string }
interface Draft { context: string; rateSet: string; policy: string; lines: Line[] }
const blank = (): Line => ({ reference: '', snapshotId: '', sourceLineId: '', isMonetary: true, currency: '', foreignAmount: '', priorCarrying: '', historicalDate: '' });
const DRAFT = 'accounting-remeasurement';
export function validDraft(value: unknown): Draft | null {
  const v = value as Draft;
  if (!v || typeof v !== 'object' || typeof v.context !== 'string' || !Array.isArray(v.lines) || v.lines.length > 500) return null;
  return v.lines.every((l) => l && typeof l.reference === 'string' && typeof l.isMonetary === 'boolean') ? v : null;
}

@Component({
  selector: 'audit-remeasurement',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a routerLink="/app/accounting">Client accounting</a> / <span>Currency remeasurement</span></nav>
    <audit-page-header title="Currency remeasurement workpapers" eyebrow="Client accounting · FX"
      description="Source-evidence-linked remeasurement calculations using approved rate sets and translation policies, with independent approval." />
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="scoped periods and approved inputs" />
    @if (ws.data(); as w) {
      <p role="status">{{ w.contexts.length }} scoped periods · {{ count(w, 'SUBMITTED') }} awaiting approval · {{ w.history.length }} recent workpapers · {{ count(w, 'APPROVED') }} approved</p>
      <p>Approval records a workpaper only; it does not post an adjustment journal or clear any release/records gate.</p>
      <section class="panel" aria-labelledby="prepare-heading">
        <h2 id="prepare-heading">Prepare workpaper</h2>
        <p><small>Link each item to an imported GL line and an evidence snapshot. The GL link is corroborating lineage only; it does not prove the amount is still outstanding. Unsubmitted fields are kept in this browser for this signed-in user and cleared after a confirmed submission. Do not use a shared browser profile for confidential work.</small></p>
        <div class="inline-form">
          <label>Client period and engagement <select name="ctx" [(ngModel)]="d.context" (ngModelChange)="contextChanged(w, $event)" required><option value="">Select a scoped period</option>
            @for (c of w.contexts; track c.periodId + c.engagementId) { <option [value]="c.periodId + '|' + c.engagementId">{{ c.clientName }} · {{ c.periodCode }} · {{ c.endDate }} · {{ c.engagementLabel }}</option> }</select></label>
          <label>Approved rate set <select name="rate" [(ngModel)]="d.rateSet" (ngModelChange)="persist()" required><option value="">Select an approved rate set</option>
            @for (r of choices()?.rateSets ?? []; track r.id) { <option [value]="r.id">{{ r.label }}</option> }</select></label>
          <label>Approved translation policy <select name="policy" [(ngModel)]="d.policy" (ngModelChange)="persist()" required><option value="">Select an approved policy</option>
            @for (p of choices()?.policies ?? []; track p.id) { <option [value]="p.id">{{ p.label }}</option> }</select></label>
        </div>
        @if (choicesError()) { <p role="alert" class="error-text">{{ choicesError() }}</p> }
        @for (line of d.lines; track $index; let i = $index) {
          <fieldset class="panel"><legend>Item {{ i + 1 }}</legend>
            <div class="inline-form">
              <label>Stable source reference <input [name]="'ref' + i" [(ngModel)]="line.reference" (ngModelChange)="persist()" maxlength="200" required /></label>
              <label>Evidence snapshot ID <input [name]="'snap' + i" [(ngModel)]="line.snapshotId" (ngModelChange)="persist()" required /></label>
              <label>Imported GL source line <select [name]="'gl' + i" [(ngModel)]="line.sourceLineId" (ngModelChange)="sourceChanged(line)" required><option value="">Select a sealed GL line</option>
                @for (s of choices()?.glLines ?? []; track s.id) { <option [value]="s.id">{{ s.label }}</option> }</select></label>
              @if (source(line); as s) { <p><small>GL source: {{ s.currency }} {{ s.originalAmount | money }} · functional {{ s.functionalAmount | money }} {{ choices()?.functionalCurrency }} · {{ s.label }}. Enter the independently confirmed open balance below.</small></p> }
              <label>Classification <select [name]="'mon' + i" [(ngModel)]="line.isMonetary" (ngModelChange)="persist()"><option [ngValue]="true">Monetary — closing rate</option><option [ngValue]="false">Non-monetary — historical cost</option></select></label>
              <label>Foreign currency <input [name]="'cur' + i" [(ngModel)]="line.currency" (ngModelChange)="persist()" maxlength="3" required /></label>
              <label>Foreign-currency amount <input [name]="'fa' + i" inputmode="decimal" [(ngModel)]="line.foreignAmount" (ngModelChange)="persist()" required /></label>
              <label>Prior functional carrying amount <input [name]="'pc' + i" inputmode="decimal" [(ngModel)]="line.priorCarrying" (ngModelChange)="persist()" required /></label>
              <label>Historical rate date {{ line.isMonetary ? '(optional for monetary items)' : '' }} <input type="date" [name]="'hd' + i" [(ngModel)]="line.historicalDate" (ngModelChange)="persist()" [required]="!line.isMonetary" /></label>
              <button matButton type="button" (click)="remove(i)" [disabled]="cmd.busy()">Remove item</button>
            </div>
          </fieldset>
        } @empty { <p>Select a period to add evidence-backed line items.</p> }
        <p class="actions"><button matButton="outlined" (click)="add()" [disabled]="cmd.busy() || !d.context || d.lines.length >= 500">Add item</button>
          <button matButton="filled" (click)="prepare(w)" [disabled]="cmd.busy()">{{ cmd.busy() ? 'Saving…' : 'Prepare workpaper' }}</button></p>
      </section>
      @if (schedule(); as s) {
        <section class="panel" aria-labelledby="schedule-heading">
          <h2 id="schedule-heading">Workpaper · {{ s.status }}</h2>
          <p>As of {{ s.asOfDate }} · functional currency {{ s.functionalCurrency }} · {{ s.itemCount }} item(s) · FX adjustment <strong>{{ s.totalForeignExchangeAdjustment | money: 6 }} {{ s.functionalCurrency }}</strong></p>
          <p>Manifest <code>{{ s.inputHash }}</code></p>
          <div class="table-scroll"><table aria-label="Currency remeasurement item calculations">
            <thead><tr><th>Reference</th><th>Evidence snapshot / SHA-256</th><th>Imported GL line / digest</th><th>Classification</th><th>Rate</th><th class="number">Remeasured</th><th class="number">FX adjustment</th></tr></thead>
            <tbody>@for (x of s.items; track $index) {
              <tr><td>{{ x.stableItemReference }}</td><td><code>{{ x.evidenceSnapshotId }}</code><br /><code>{{ x.evidenceSha256 }}</code></td><td><code>{{ x.sourceGeneralLedgerLineId }}</code><br /><code>{{ x.sourceGlLineDigest }}</code></td>
                <td>{{ x.isMonetary ? 'Monetary' : 'Historical-cost non-monetary' }}</td><td>{{ x.rateType }} · {{ x.rateDate }} · {{ x.appliedRate }}</td>
                <td class="number">{{ x.remeasuredFunctionalAmount }}</td><td class="number">{{ x.foreignExchangeAdjustment }}</td></tr> }</tbody>
          </table></div>
          @if (s.status === 'SUBMITTED' && s.createdByUserId !== userId()) { <button matButton="filled" (click)="approve(s.id)" [disabled]="cmd.busy()">Approve independently</button> }
          <p><small>Prepared by <code>{{ s.createdByUserId }}</code>{{ s.approvedByUserId ? ' · Approved by ' + s.approvedByUserId : '' }}</small></p>
        </section>
      }
      <section class="panel" aria-labelledby="history-heading">
        <h2 id="history-heading">Recent scoped workpapers</h2>
        <div class="table-scroll"><table aria-label="Recent scoped currency remeasurement workpapers">
          <thead><tr><th>Client</th><th>Period</th><th>Status</th><th>Created</th><th>Action</th></tr></thead>
          <tbody>@for (h of w.history; track h.id) { <tr><td>{{ h.clientName }}</td><td>{{ h.periodCode }}</td><td><audit-status [value]="h.status" /></td><td>{{ h.createdAt.slice(0, 16).replace('T', ' ') }} UTC</td>
            <td><button matButton (click)="open(h.id)">Open / review</button></td></tr> }
          @empty { <tr><td colspan="5">No saved workpapers are available in the current scope.</td></tr> }</tbody>
        </table></div>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class CurrencyRemeasurement {
  private readonly api = inject(Api);
  private readonly drafts = inject(Drafts);
  private readonly session = inject(SessionService);
  readonly ws = this.api.resource(() => '/api/ui/accounting/remeasurement', decodeRemeasurementWorkspace,
    'An explicitly scoped internal accounting-preparer or reviewer grant is required.');
  readonly cmd = new CommandState(this.api);
  readonly choices = signal<ReturnType<typeof decodeChoices> | null>(null);
  readonly choicesError = signal('');
  readonly schedule = signal<ReturnType<typeof decodeSchedule> | null>(null);
  readonly userId = () => this.session.current()?.userId ?? '';
  d: Draft = { context: '', rateSet: '', policy: '', lines: [] };
  private restored = false;

  constructor() {
    effect(() => {
      const w = this.ws.data();
      untracked(() => {
        if (!w || this.restored) return;
        this.restored = true;
        const draft = this.drafts.load(DRAFT, validDraft);
        const first = w.contexts[0];
        const context = draft && w.contexts.some((c) => c.periodId + '|' + c.engagementId === draft.context) ? draft.context : first ? first.periodId + '|' + first.engagementId : '';
        if (draft && context === draft.context) this.d = { ...draft };
        else this.d = { context, rateSet: '', policy: '', lines: context ? [blank()] : [] };
        if (context) void this.loadChoices(w, context, !!draft && context === draft.context);
      });
    });
  }
  count(w: ReturnType<typeof decodeRemeasurementWorkspace>, status: string): number { return w.history.filter((h) => h.status === status).length; }
  persist(): void { this.drafts.save(DRAFT, this.d); }
  source(line: Line) { return this.choices()?.glLines.find((s) => s.id === line.sourceLineId) ?? null; }
  sourceChanged(line: Line): void { const s = this.source(line); if (s) line.currency = s.currency; this.persist(); }
  add(): void { if (this.d.lines.length < 500) this.d.lines.push(blank()); this.persist(); }
  remove(i: number): void { this.d.lines.splice(i, 1); if (!this.d.lines.length) this.d.lines.push(blank()); this.persist(); }
  contextChanged(w: ReturnType<typeof decodeRemeasurementWorkspace>, value: string): void {
    this.d.rateSet = ''; this.d.policy = '';
    if (!this.d.lines.length) this.d.lines.push(blank());
    void this.loadChoices(w, value, false); this.persist();
  }
  private async loadChoices(w: ReturnType<typeof decodeRemeasurementWorkspace>, value: string, keepSelections: boolean): Promise<void> {
    this.choices.set(null); this.choicesError.set('');
    const [periodId, engagementId] = value.split('|');
    if (!periodId || !engagementId) return;
    try {
      const c = await this.api.get(`/api/ui/accounting/remeasurement/choices?periodId=${periodId}&engagementId=${engagementId}`, decodeChoices);
      if (this.d.context !== value) return;
      this.choices.set(c);
      if (!keepSelections || !c.rateSets.some((r) => r.id === this.d.rateSet)) this.d.rateSet = c.rateSets.length === 1 ? c.rateSets[0].id : '';
      if (!keepSelections || !c.policies.some((p) => p.id === this.d.policy)) this.d.policy = c.policies.length === 1 ? c.policies[0].id : '';
      for (const l of this.d.lines) if (l.sourceLineId && !c.glLines.some((s) => s.id === l.sourceLineId)) l.sourceLineId = '';
    } catch (e) { this.choicesError.set((e as Error).message); }
  }
  prepare(w: ReturnType<typeof decodeRemeasurementWorkspace>): void {
    const ctx = w.contexts.find((c) => c.periodId + '|' + c.engagementId === this.d.context);
    const complete = this.d.lines.length > 0 && this.d.lines.every((l) => guidPattern.test(l.snapshotId.trim()) && guidPattern.test(l.sourceLineId) && l.reference.trim() &&
      decimalInput(l.foreignAmount, 6) !== null && decimalInput(l.priorCarrying, 6) !== null && (l.isMonetary || l.historicalDate));
    if (!ctx || !this.d.rateSet || !this.d.policy || !complete) {
      this.cmd.failed.set(true);
      this.cmd.message.set('Select a scoped period and approved inputs, and complete each line with a valid snapshot ID; historical rate dates are required for non-monetary items.');
      return;
    }
    void this.cmd.run<string>('/api/ui/accounting/remeasurement', { clientId: ctx.clientId, engagementId: ctx.engagementId, periodId: ctx.periodId, rateSetId: this.d.rateSet,
      policyId: this.d.policy, asOfDate: ctx.endDate, items: this.d.lines.map((l) => ({ reference: l.reference.trim(), snapshotId: l.snapshotId.trim(), sourceLineId: l.sourceLineId,
        isMonetary: l.isMonetary, currency: l.currency.toUpperCase(), foreignAmount: decimalInput(l.foreignAmount, 6), priorCarrying: decimalInput(l.priorCarrying, 6),
        historicalRateDate: l.historicalDate || null })) },
      'Submitted workpaper saved. Review the source and calculation before independent approval.', (id) => {
        this.drafts.clear(DRAFT); this.d = { ...this.d, lines: [blank()] }; void this.open(id);
      }).finally(() => this.ws.reload());
  }
  async open(id: string): Promise<void> {
    this.schedule.set(null);
    try { this.schedule.set(await this.api.get(`/api/ui/accounting/remeasurement/${id}`, decodeSchedule)); }
    catch { this.cmd.failed.set(true); this.cmd.message.set('Schedule is unavailable in the current authorization scope.'); }
  }
  approve(id: string): void {
    void this.cmd.run(`/api/ui/accounting/remeasurement/${id}/approve`, {}, 'Independent approval recorded after current evidence and calculation revalidation. No journal was posted.',
      () => void this.open(id)).finally(() => this.ws.reload());
  }
}
