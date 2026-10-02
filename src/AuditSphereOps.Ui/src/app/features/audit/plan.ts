import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, decimalInput, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const materiality = obj({ id: guid, preparedByUserId: guid, benchmarkSource: text, benchmarkVersion: text, rationale: text, benchmarkAmount: dec, rateApplied: dec,
  overallMateriality: dec, performanceMateriality: dec, clearlyTrivialThreshold: dec, qualitativeConsiderations: nullable(text), status: text,
  approvedByUserId: nullable(guid), approvedAt: nullable(instant) });
const source = obj({ mappingVersionId: guid, mappingVersion: nat, datasetId: guid, datasetDigest: text, currency: text,
  options: arr(obj({ kind: text, destinationCode: nullable(text), label: text, amount: nullable(dec), lineCount: nat }), 500) });
const calculation = obj({ assessmentId: guid, state: text, route: text, benchmarkKind: text, destinationCode: nullable(text), benchmarkAmount: dec, currency: text,
  sourceLineCount: nat, mappingVersionNumber: nat, ratePercent: dec, performancePercent: dec, trivialPercent: dec, planningMateriality: dec,
  tolerableError: dec, sadThreshold: dec, policyVersion: text });
const routing = obj({ riskId: guid, area: text, assertion: text, significanceDecision: text, assessmentId: nullable(guid), band: nullable(text), likelihood: nullable(int),
  magnitude: nullable(int), fraudRisk: bool, route: text, partnerReviewRequired: bool, partnerCleared: bool, partnerName: nullable(text), ownerName: nullable(text), ownerLevel: nullable(text) });
export const decodePlan = obj({ engagementId: guid, professionalWorkBlocked: bool, materiality: nullable(materiality), canApproveMateriality: bool,
  risks: arr(obj({ id: guid, accountArea: text, description: text, assertion: text, severity: text, status: text }), 2000),
  populations: arr(obj({ id: guid, purpose: text, assertion: text, rowCount: nat, monetaryControlTotal: dec, currency: text, status: text }), 2000),
  findings: arr(obj({ id: guid, findingType: text, status: text, monetaryAmount: nullable(dec) }), 2000),
  workpapers: arr(obj({ id: guid, index: text, title: text, status: text, revision: nat }), 5000),
  materialitySource: nullable(source), materialitySourceMessage: nullable(text), latestCalculation: nullable(calculation),
  rateRanges: arr(obj({ kind: text, minRatePercent: dec, maxRatePercent: dec }), 50), performanceMin: dec, performanceMax: dec, trivialMin: dec, trivialMax: dec,
  riskRuleVersion: text, routing: arr(routing, 2000),
  team: arr(obj({ assignmentId: guid, userId: guid, name: text, level: text, levelLabel: text, authorizationRole: text, certified: bool }), 500),
  canAssignOwners: bool, isPartner: bool });
type Plan = ReturnType<typeof decodePlan>;
interface RowInput { likelihood: number; magnitude: number; fraud: boolean; rationale: string; owner: string; note: string }

@Component({
  selector: 'audit-plan',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a [routerLink]="['/app/engagements', id()]">Engagement</a> / <span>Audit plan</span></nav>
    <audit-page-header title="Audit plan & strategy" eyebrow="Engagement audit" description="Planning, risk assessment, materiality, and program adoption for this engagement." />
    <audit-state [loading]="plan.loading()" [error]="plan.error()" label="audit plan" />
    @if (plan.data(); as p) {
      <p class="actions" aria-label="Audit plan navigation">
        <audit-status [value]="p.professionalWorkBlocked ? 'PLANNING_BLOCKED' : 'PLANNING_ACTIVE'" />
        <a [href]="'/app/engagements/' + p.engagementId + '/audit-fieldwork'">Fieldwork control center</a>
        <a href="/app/audit/library">Program library</a>
        <a [href]="'/app/engagements/' + p.engagementId + '/completion'">Completion checklist</a>
      </p>
      <p role="status">{{ p.risks.length }} identified risks · {{ p.populations.length }} populations · {{ p.findings.length }} findings · {{ p.workpapers.length }} workpapers</p>
      @if (p.professionalWorkBlocked) { <p role="alert">Professional work is blocked on this engagement; planning commands are refused until the hold is cleared.</p> }
      <button matButton="outlined" (click)="plan.reload()">Refresh plan</button>

      <section class="panel" aria-labelledby="materiality-engine-heading">
        <h2 id="materiality-engine-heading">Materiality calculator</h2>
        @if (p.latestCalculation; as c) {
          <p><audit-status [value]="c.state" /> {{ c.route }}</p>
          <dl class="facts" aria-label="Materiality thresholds">
            <dt>Planning materiality (PM)</dt><dd>{{ c.planningMateriality | money }} {{ c.currency }}</dd>
            <dt>Tolerable error (TE)</dt><dd>{{ c.tolerableError | money }} {{ c.currency }}</dd>
            <dt>SAD threshold</dt><dd>{{ c.sadThreshold | money }} {{ c.currency }}</dd>
          </dl>
          <p><small>{{ c.benchmarkKind }}{{ c.destinationCode ? ' (' + c.destinationCode + ')' : '' }} of {{ c.benchmarkAmount | money }} {{ c.currency }} from {{ c.sourceLineCount }} mapped line(s), mapping version {{ c.mappingVersionNumber }}; rate {{ c.ratePercent }}%, TE {{ c.performancePercent }}% of PM, SAD {{ c.trivialPercent }}% of PM ({{ c.policyVersion }}).</small></p>
          @if (c.state === 'DRAFT') { <button matButton="outlined" (click)="send('/api/ui/materiality/' + c.assessmentId + '/approve', {}, 'Materiality approved.')" [disabled]="cmd.busy()">Approve calculated materiality</button> }
        }
        @if (p.materialitySource; as s) {
          <p>Source: mapping version {{ s.mappingVersion }} over trial balance <code>{{ s.datasetDigest.slice(0, 12) }}</code> ({{ s.currency }}).</p>
          <div class="inline-form">
            <label>Benchmark <select name="option" [(ngModel)]="calc.option">
              @for (o of s.options; track o.kind + (o.destinationCode ?? '')) { @if (o.amount !== null) { <option [value]="o.kind + '|' + (o.destinationCode ?? '')">{{ o.label }} — {{ o.amount | money }}</option> } }</select></label>
            <label>Rate % <input name="rate" inputmode="decimal" [(ngModel)]="calc.rate" /></label>
            <label>TE % of PM <input name="te" inputmode="decimal" [(ngModel)]="calc.performance" /></label>
            <label>SAD % of PM <input name="sad" inputmode="decimal" [(ngModel)]="calc.trivial" /></label>
          </div>
          @if (range(p); as r) { <p><small>Policy range for this benchmark: {{ r.minRatePercent }}%–{{ r.maxRatePercent }}%; TE {{ p.performanceMin }}–{{ p.performanceMax }}%; SAD {{ p.trivialMin }}–{{ p.trivialMax }}%.</small></p> }
          <label>Rationale for the benchmark <textarea name="calcRationale" [(ngModel)]="calc.rationale" maxlength="2000" rows="2"></textarea></label>
          <button matButton="filled" (click)="calculate(p)" [disabled]="cmd.busy() || !option(p)">Calculate materiality</button>
        } @else { <p>{{ p.materialitySourceMessage ?? 'The mapped trial balance is unavailable.' }}</p> }
      </section>

      <section class="panel" aria-labelledby="materiality-heading">
        <h2 id="materiality-heading">Materiality (§19.2)</h2>
        @if (p.materiality; as m) {
          <dl class="facts">
            <dt>Overall</dt><dd>{{ m.overallMateriality | money }}</dd><dt>Performance</dt><dd>{{ m.performanceMateriality | money }}</dd>
            <dt>Clearly trivial</dt><dd>{{ m.clearlyTrivialThreshold | money }}</dd><dt>Benchmark</dt><dd>{{ m.benchmarkSource }} ({{ m.benchmarkVersion }})</dd>
            <dt>Benchmark amount</dt><dd>{{ m.benchmarkAmount | money }} × {{ m.rateApplied }}</dd><dt>Rationale</dt><dd>{{ m.rationale }}</dd><dt>Status</dt><dd>{{ m.status }}</dd>
          </dl>
          @if (p.canApproveMateriality) { <button matButton="filled" (click)="send('/api/ui/materiality/' + m.id + '/approve', {}, 'The materiality assessment was independently approved.')" [disabled]="cmd.busy()">Approve materiality</button> }
          @if (m.approvedAt) { <p><small>Approved by {{ m.approvedByUserId }} at {{ m.approvedAt.slice(0, 16).replace('T', ' ') }}.</small></p> }
          @if (m.qualitativeConsiderations) { <p>Qualitative considerations: {{ m.qualitativeConsiderations }}</p> }
        } @else { <p>No materiality assessment has been recorded for this engagement.</p> }
        <details><summary>Record a materiality assessment</summary>
          <form class="inline-form" (submit)="$event.preventDefault(); recordMateriality()">
            <label>Benchmark source <input name="mb" [(ngModel)]="mat.benchmark" maxlength="200" required /></label>
            <label>Benchmark version <input name="mv" [(ngModel)]="mat.version" maxlength="100" required /></label>
            <label>Benchmark amount <input name="ma" inputmode="decimal" [(ngModel)]="mat.amount" required /></label>
            <label>Applied rate (0–1) <input name="mr" inputmode="decimal" [(ngModel)]="mat.rate" required /></label>
            <label>Overall materiality <input name="mo" inputmode="decimal" [(ngModel)]="mat.overall" required /></label>
            <label>Performance materiality <input name="mp" inputmode="decimal" [(ngModel)]="mat.performance" required /></label>
            <label>Clearly trivial threshold <input name="mt" inputmode="decimal" [(ngModel)]="mat.trivial" required /></label>
            <label>Rationale <textarea name="mra" [(ngModel)]="mat.rationale" rows="2" required></textarea></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy() || p.professionalWorkBlocked">Record assessment</button>
          </form></details>
      </section>

      <section class="panel" aria-labelledby="risks-heading">
        <h2 id="risks-heading">Identified risks (§19.3)</h2>
        <div class="table-scroll"><table>
          <thead><tr><th scope="col">Area</th><th scope="col">Description</th><th scope="col">Assertion</th><th scope="col">Severity</th><th scope="col">Status</th></tr></thead>
          <tbody>@for (r of p.risks; track r.id) { <tr><td>{{ r.accountArea }}</td><td>{{ r.description }}</td><td>{{ r.assertion }}</td><td><audit-status [value]="r.severity" /></td><td>{{ r.status }}</td></tr> }
          @empty { <tr><td colspan="5">No audit risks have been recorded for this engagement.</td></tr> }</tbody>
        </table></div>
        <details><summary>Record an identified risk</summary>
          <form class="inline-form" (submit)="$event.preventDefault(); recordRisk()">
            <label>Account or disclosure area <input name="ra" [(ngModel)]="risk.area" maxlength="200" required /></label>
            <label>Assertion <input name="rs" [(ngModel)]="risk.assertion" maxlength="100" required /></label>
            <label>Risk description <textarea name="rd" [(ngModel)]="risk.description" rows="2" required></textarea></label>
            <label>Drivers <textarea name="rdr" [(ngModel)]="risk.drivers" rows="2" required></textarea></label>
            <label>Significance decision <select name="rsig" [(ngModel)]="risk.significance"><option value="NORMAL">NORMAL</option><option value="SIGNIFICANT">SIGNIFICANT</option></select></label>
            <label>Planned response <textarea name="rre" [(ngModel)]="risk.response" rows="2" required></textarea></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy() || p.professionalWorkBlocked">Record risk</button>
          </form></details>
      </section>

      <section class="panel" aria-labelledby="risk-routing-heading">
        <h2 id="risk-routing-heading">Risk routing</h2>
        <p><small>Green: a Staff Associate may perform. Amber: Senior Auditor or above performs, Manager reviews. Red (any significant or fraud risk, or likelihood × magnitude ≥ 6): Audit Manager or above performs and the Engagement Partner must review before completion. ({{ p.riskRuleVersion }})</small></p>
        @for (row of p.routing; track row.riskId) {
          <article class="panel" [attr.data-risk]="row.area">
            <p><strong>{{ row.area }}</strong> — {{ row.assertion }} <span class="status-chip" [attr.aria-label]="'Band ' + (row.band ?? 'not assessed')">{{ row.band ?? 'NOT ASSESSED' }}</span>
              @if (row.partnerReviewRequired) { <span [class.error-text]="!row.partnerCleared">{{ row.partnerCleared ? 'Partner reviewed (' + row.partnerName + ')' : 'Partner review required' }}</span> }</p>
            <p><small>{{ row.route }} {{ row.ownerName ? 'Owner: ' + row.ownerName + ' (' + row.ownerLevel + ').' : '' }}</small></p>
            <div class="inline-form">
              <label>Likelihood <select [(ngModel)]="input(row).likelihood" [name]="'l-' + row.riskId"><option [ngValue]="1">1 Low</option><option [ngValue]="2">2 Moderate</option><option [ngValue]="3">3 High</option></select></label>
              <label>Magnitude <select [(ngModel)]="input(row).magnitude" [name]="'m-' + row.riskId"><option [ngValue]="1">1 Low</option><option [ngValue]="2">2 Moderate</option><option [ngValue]="3">3 High</option></select></label>
              <label><span><input type="checkbox" [(ngModel)]="input(row).fraud" [name]="'f-' + row.riskId" /> Fraud risk</span></label>
              <label>Rationale <input [(ngModel)]="input(row).rationale" [name]="'r-' + row.riskId" maxlength="500" [attr.aria-label]="'Band rationale for ' + row.area" /></label>
              <button matButton="outlined" (click)="assess(row.riskId)" [disabled]="cmd.busy()" [attr.aria-label]="'Assess band for ' + row.area">Assess band</button>
            </div>
            @if (row.band && p.canAssignOwners && p.team.length) {
              <div class="inline-form">
                <label>Owner <select [(ngModel)]="input(row).owner" [name]="'o-' + row.riskId" [attr.aria-label]="'Owner for ' + row.area"><option value="">Select</option>@for (t of p.team; track t.userId) { <option [value]="t.userId">{{ t.name }} ({{ t.levelLabel }})</option> }</select></label>
                <button matButton (click)="assign(row.riskId)" [disabled]="cmd.busy() || !input(row).owner" [attr.aria-label]="'Assign owner for ' + row.area">Assign owner</button>
              </div>
            }
            @if (row.partnerReviewRequired && !row.partnerCleared && p.isPartner) {
              <div class="inline-form">
                <label>Partner review note <input [(ngModel)]="input(row).note" [name]="'n-' + row.riskId" maxlength="1000" [attr.aria-label]="'Partner review note for ' + row.area" /></label>
                <button matButton="filled" (click)="clear(row.riskId)" [disabled]="cmd.busy()" [attr.aria-label]="'Record Partner review for ' + row.area">Record Partner review</button>
              </div>
            }
          </article>
        } @empty { <p>No risks are recorded yet.</p> }
      </section>

      <section class="panel" aria-labelledby="populations-heading">
        <h2 id="populations-heading">Populations (§20.1)</h2>
        <div class="table-scroll"><table>
          <thead><tr><th scope="col">Purpose</th><th scope="col">Assertion</th><th scope="col" class="number">Rows</th><th scope="col" class="number">Control total</th><th scope="col">Status</th></tr></thead>
          <tbody>@for (x of p.populations; track x.id) { <tr><td><a [href]="'/app/audit/populations/' + x.id">{{ x.purpose }}</a></td><td>{{ x.assertion }}</td><td class="number">{{ x.rowCount }}</td><td class="number">{{ x.currency }} {{ x.monetaryControlTotal | money }}</td><td>{{ x.status }}</td></tr> }
          @empty { <tr><td colspan="5">No population has been extracted for this engagement.</td></tr> }</tbody>
        </table></div>
        <details><summary>Record a population version</summary>
          <form class="inline-form" (submit)="$event.preventDefault(); recordPopulation()">
            <label>Purpose <input name="pp" [(ngModel)]="pop.purpose" maxlength="300" required /></label>
            <label>Assertion <input name="pa" [(ngModel)]="pop.assertion" maxlength="100" required /></label>
            <label>Source receipt reference <input name="pr" [(ngModel)]="pop.receipt" maxlength="200" required /></label>
            <label>Extraction parameters <textarea name="pe" [(ngModel)]="pop.extraction" rows="2" required></textarea></label>
            <label>Row count <input name="pn" type="number" min="0" [(ngModel)]="pop.rows" required /></label>
            <label>Monetary control total <input name="pt" inputmode="decimal" [(ngModel)]="pop.total" required /></label>
            <label>Currency (ISO 4217) <input name="pc" [(ngModel)]="pop.currency" maxlength="3" required /></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy() || p.professionalWorkBlocked">Record population</button>
          </form></details>
      </section>

      <section class="panel" aria-labelledby="findings-heading">
        <h2 id="findings-heading">Findings (§23)</h2>
        <ul>@for (f of p.findings; track f.id) { <li>{{ f.findingType }} · {{ f.status }} · {{ f.monetaryAmount === null ? 'unquantified' : (f.monetaryAmount | money) }} <a [href]="'/app/findings/' + f.id">Open</a></li> }
          @empty { <li>No finding has been recorded for this engagement.</li> }</ul>
        <details><summary>Record a finding</summary>
          <form class="inline-form" (submit)="$event.preventDefault(); recordFinding()">
            <label>Finding type <input name="ft" [(ngModel)]="finding.type" maxlength="100" required /></label>
            <label>Impact description <textarea name="fi" [(ngModel)]="finding.impact" rows="2" required></textarea></label>
            <label>Monetary amount (optional) <input name="fa" inputmode="decimal" [(ngModel)]="finding.amount" /></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy() || p.professionalWorkBlocked">Record finding</button>
          </form></details>
      </section>

      <section class="panel" aria-labelledby="workpapers-heading">
        <h2 id="workpapers-heading">Audit workpapers (§21.1)</h2>
        <ul>@for (w of p.workpapers; track w.id) { <li>{{ w.index }} — {{ w.title }} <small>Status: {{ w.status }} · Revision {{ w.revision }}</small> <a [href]="'/app/audit/workpapers/' + w.id">Open</a></li> }
          @empty { <li>No workpapers created for this audit plan yet.</li> }</ul>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class AuditPlan {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly plan = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/audit-plan` : null), decodePlan,
    'Sign in with an authorized internal staff identity assigned to this engagement to view its audit plan.');
  readonly cmd = new CommandState(this.api);
  private readonly inputs = new Map<string, RowInput>();
  calc = { option: '', rate: '1', performance: '75', trivial: '5', rationale: '' };
  mat = { benchmark: '', version: '', amount: '', rate: '', overall: '', performance: '', trivial: '', rationale: '' };
  risk = { area: '', assertion: '', description: '', drivers: '', significance: 'NORMAL', response: '' };
  pop = { purpose: '', assertion: '', receipt: '', extraction: '', rows: 0, total: '', currency: '' };
  finding = { type: '', impact: '', amount: '' };

  option(p: Plan): string {
    if (this.calc.option) return this.calc.option;
    const first = p.materialitySource?.options.find((o) => o.amount !== null && !o.amount.startsWith('-') && !/^0(\.0*)?$/.test(o.amount));
    return first ? first.kind + '|' + (first.destinationCode ?? '') : '';
  }
  range(p: Plan) { const kind = this.option(p).split('|')[0]; return p.rateRanges.find((r) => r.kind === kind) ?? null; }
  input(row: { riskId: string; likelihood: number | null; magnitude: number | null; fraudRisk: boolean }): RowInput {
    let i = this.inputs.get(row.riskId);
    if (!i) this.inputs.set(row.riskId, (i = { likelihood: row.likelihood ?? 1, magnitude: row.magnitude ?? 1, fraud: row.fraudRisk, rationale: '', owner: '', note: '' }));
    return i;
  }
  send(url: string, body: unknown, ok: string, after?: () => void): void {
    this.cmd.run(url, body, ok, after).finally(() => this.plan.reload());
  }
  private invalid(m: string): void { this.cmd.failed.set(true); this.cmd.message.set(m); }
  private base(): string { return `/api/ui/engagements/${this.id()}/audit-plan`; }
  calculate(p: Plan): void {
    const [kind, destination] = this.option(p).split('|');
    const rate = decimalInput(this.calc.rate), performance = decimalInput(this.calc.performance), trivial = decimalInput(this.calc.trivial);
    if (!kind || rate === null || performance === null || trivial === null) return this.invalid('Choose a benchmark and enter the percentages as numbers.');
    this.send(this.base() + '/materiality/calculate', { benchmarkKind: kind, destinationCode: destination || null, ratePercent: rate, performancePercent: performance,
      trivialPercent: trivial, rationale: this.calc.rationale }, 'Materiality calculated; an independent Manager or Partner must approve it.');
  }
  recordMateriality(): void {
    const m = this.mat;
    const values = [m.amount, m.overall, m.performance, m.trivial].map((v) => decimalInput(v, 6));
    const rate = decimalInput(m.rate, 6);
    if (values.some((v) => v === null) || rate === null) return this.invalid('Enter every materiality amount and the rate as numbers.');
    this.send(this.base() + '/materiality', { benchmarkSource: m.benchmark, benchmarkVersion: m.version, rationale: m.rationale, benchmarkAmount: values[0], rateApplied: rate,
      overallMateriality: values[1], performanceMateriality: values[2], clearlyTrivialThreshold: values[3] }, 'The materiality assessment was recorded as a draft.',
      () => (this.mat = { benchmark: '', version: '', amount: '', rate: '', overall: '', performance: '', trivial: '', rationale: '' }));
  }
  recordRisk(): void {
    const r = this.risk;
    this.send(this.base() + '/risks', { area: r.area, assertion: r.assertion, description: r.description, drivers: r.drivers, significance: r.significance, response: r.response },
      'The identified risk was recorded.', () => (this.risk = { area: '', assertion: '', description: '', drivers: '', significance: 'NORMAL', response: '' }));
  }
  recordPopulation(): void {
    const x = this.pop;
    const total = decimalInput(x.total, 6);
    const rows = Math.trunc(Number(x.rows));
    if (total === null || !Number.isSafeInteger(rows) || rows < 0) return this.invalid('Enter the row count and monetary control total as numbers.');
    this.send(this.base() + '/populations', { purpose: x.purpose, assertion: x.assertion, sourceReceiptReference: x.receipt, extractionParameters: x.extraction,
      rowCount: rows, monetaryControlTotal: total, currency: x.currency.toUpperCase() }, 'The population version was recorded as pending approval.',
      () => (this.pop = { purpose: '', assertion: '', receipt: '', extraction: '', rows: 0, total: '', currency: '' }));
  }
  recordFinding(): void {
    const amount = this.finding.amount.trim() ? decimalInput(this.finding.amount, 6) : '';
    if (amount === null) return this.invalid('Enter the monetary amount as a number or leave it blank.');
    this.send(this.base() + '/findings', { findingType: this.finding.type, impactDescription: this.finding.impact, monetaryAmount: amount || null },
      'The finding was recorded as open.', () => (this.finding = { type: '', impact: '', amount: '' }));
  }
  assess(riskId: string): void {
    const i = this.inputs.get(riskId)!;
    this.send(`/api/ui/risks/${riskId}/band`, { likelihood: i.likelihood, magnitude: i.magnitude, fraud: i.fraud, rationale: i.rationale }, 'Band assessed.');
  }
  assign(riskId: string): void { this.send(`/api/ui/risks/${riskId}/owner`, { ownerUserId: this.inputs.get(riskId)!.owner }, 'Owner assigned.'); }
  clear(riskId: string): void { this.send(`/api/ui/risks/${riskId}/partner-review`, { note: this.inputs.get(riskId)!.note }, 'Partner review recorded.'); }
}
