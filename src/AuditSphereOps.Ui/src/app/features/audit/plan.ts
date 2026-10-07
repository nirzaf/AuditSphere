import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { SessionService } from '../../core/session';
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
const routing = obj({ riskId: guid, area: text, assertion: text, significanceDecision: text, assessmentId: nullable(guid), assessedByUserId: nullable(guid), band: nullable(text), likelihood: nullable(int),
  magnitude: nullable(int), fraudRisk: bool, route: text, partnerReviewRequired: bool, partnerCleared: bool, partnerName: nullable(text), ownerName: nullable(text), ownerLevel: nullable(text) });
const fsliRow = obj({
  destinationCode: text, statementSection: text, auditArea: nullable(text), balance: dec, absoluteBalance: dec,
  currency: text, tolerableError: dec, planningMateriality: dec, band: text, criticalEstimate: bool,
  highInherentRisk: bool, significantRisk: bool, fraudRisk: bool, performerRole: text, reviewerRole: text, explanation: text
});
const milestonePlan = obj({
  id: guid, engagementId: guid, periodEnd: text, statutoryFilingCutoff: text,
  fieldworkStartDate: text, draftReportDate: text, finalReportDate: text, archiveDeadlineDate: text,
  adjustmentReason: nullable(text), warningOverrideReason: nullable(text), warnings: arr(text, 50),
  revision: nat, scheduledByUserId: guid, scheduledAt: instant, canConfigure: bool
});
export const decodePlan = obj({ engagementId: guid, professionalWorkBlocked: bool, materiality: nullable(materiality), canApproveMateriality: bool,
  risks: arr(obj({ id: guid, accountArea: text, description: text, assertion: text, severity: text, status: text }), 2000),
  populations: arr(obj({ id: guid, purpose: text, assertion: text, rowCount: nat, monetaryControlTotal: dec, currency: text, status: text }), 2000),
  findings: arr(obj({ id: guid, findingType: text, status: text, monetaryAmount: nullable(dec) }), 2000),
  workpapers: arr(obj({ id: guid, index: text, title: text, status: text, revision: nat }), 5000),
  materialitySource: nullable(source), materialitySourceMessage: nullable(text), latestCalculation: nullable(calculation),
  rateRanges: arr(obj({ kind: text, minRatePercent: dec, maxRatePercent: dec }), 50), performanceMin: dec, performanceMax: dec, trivialMin: dec, trivialMax: dec,
  riskRuleVersion: text, routing: arr(routing, 2000),
  team: arr(obj({ assignmentId: guid, userId: guid, name: text, level: text, levelLabel: text, authorizationRole: text, certified: bool }), 500),
  canAssignOwners: bool, isPartner: bool, fsliStratification: arr(fsliRow, 500),
  milestonePlan: nullable(milestonePlan) });
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
        <a [routerLink]="['/app/engagements', p.engagementId, 'audit-fieldwork']">Fieldwork control center</a>
        <a routerLink="/app/audit/library">Program library</a>
        <a [routerLink]="['/app/engagements', p.engagementId, 'completion']">Completion checklist</a>
      </p>
      <p role="status">{{ p.risks.length }} identified risks · {{ p.populations.length }} populations · {{ p.findings.length }} findings · {{ p.workpapers.length }} workpapers</p>
      @if (p.professionalWorkBlocked) { <p role="alert">Professional work is blocked on this engagement; planning commands are refused until the hold is cleared.</p> }
      <button matButton="outlined" (click)="plan.reload()">Refresh plan</button>

      <section class="panel" aria-labelledby="milestones-heading">
        <h2 id="milestones-heading">Statutory filing & operational schedule (§4.2.2)</h2>
        <p>Statutory filing deadlines must be explicitly stored and never silently assumed. Milestones calculate fieldwork commencement, draft delivery, final signed report, and the 60-day archive freeze (ISA 230). Scheduling warnings require documented partner or manager override.</p>
        @if (p.milestonePlan; as m) {
          <dl class="facts" aria-label="Statutory milestones">
            <div><dt>Period end</dt><dd>{{ m.periodEnd }}</dd></div>
            <div><dt>Statutory filing cutoff</dt><dd><strong>{{ m.statutoryFilingCutoff }}</strong></dd></div>
            <div><dt>Fieldwork start</dt><dd>{{ m.fieldworkStartDate }}</dd></div>
            <div><dt>Draft report delivery</dt><dd>{{ m.draftReportDate }}</dd></div>
            <div><dt>Final signed report</dt><dd>{{ m.finalReportDate }}</dd></div>
            <div><dt>60-day archive freeze (ISA 230)</dt><dd><strong>{{ m.archiveDeadlineDate }}</strong></dd></div>
            <div><dt>Revision</dt><dd>{{ m.revision }}</dd></div>
          </dl>
          @if (m.warnings.length > 0) {
            <div role="alert">
              <strong>Scheduling warnings:</strong>
              <ul>@for (w of m.warnings; track w) { <li>{{ w }}</li> }</ul>
              @if (m.warningOverrideReason) { <p><em>Override reason:</em> {{ m.warningOverrideReason }}</p> }
            </div>
          }
          @if (m.adjustmentReason) { <p><em>Adjustment reason:</em> {{ m.adjustmentReason }}</p> }
        } @else {
          <p>No statutory milestone plan has been recorded for this engagement yet.</p>
        }
        @if (p.canAssignOwners && !p.professionalWorkBlocked) {
          <details>
            <summary>{{ p.milestonePlan ? 'Update statutory milestones & schedule' : 'Configure statutory milestones' }}</summary>
            <form (ngSubmit)="saveMilestones()">
              <label>Statutory filing cutoff (YYYY-MM-DD) <input name="mc" type="date" [(ngModel)]="milestone.cutoff" required /></label>
              <label>Fieldwork commencement date (optional override) <input name="mf" type="date" [(ngModel)]="milestone.fieldwork" /></label>
              <label>Draft report delivery date (optional override) <input name="md" type="date" [(ngModel)]="milestone.draft" /></label>
              <label>Final signed report date (optional override) <input name="mr" type="date" [(ngModel)]="milestone.finalReport" /></label>
              <label>Adjustment reason (required if modifying defaults) <textarea name="ma" [(ngModel)]="milestone.adjustment" rows="2"></textarea></label>
              <label>Warning override reason (required if warnings triggered) <textarea name="mo" [(ngModel)]="milestone.override" rows="2"></textarea></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">Save milestone schedule</button>
            </form>
          </details>
        }
      </section>

      <section class="panel" aria-labelledby="materiality-engine-heading">
        <h2 id="materiality-engine-heading">Materiality calculator</h2>
        <p>PM means planning materiality: the selected benchmark amount multiplied by the base rate. TE (tolerable error) is 50–75% of PM; SAD (clearly trivial threshold) is 3–5% of PM. The benchmark-rate ranges follow the active STE policy. Profit before tax is currently derived from mapped balances excluding tax and has no normalization-adjustment workflow; do not treat it as normalized when adjustments are needed.</p>
        @if (p.latestCalculation; as c) {
          <p><audit-status [value]="c.state" /> {{ c.route }}</p>
          <dl class="facts" aria-label="Materiality thresholds">
            <dt>Planning materiality (PM)</dt><dd>{{ c.planningMateriality | money }} {{ c.currency }}</dd>
            <dt>Tolerable error (TE)</dt><dd>{{ c.tolerableError | money }} {{ c.currency }}</dd>
            <dt>SAD threshold</dt><dd>{{ c.sadThreshold | money }} {{ c.currency }}</dd>
          </dl>
          <p><small>{{ c.benchmarkKind }}{{ c.destinationCode ? ' (' + c.destinationCode + ')' : '' }} of {{ c.benchmarkAmount | money }} {{ c.currency }} from {{ c.sourceLineCount }} mapped line(s), mapping version {{ c.mappingVersionNumber }}; rate {{ c.ratePercent }}%, TE {{ c.performancePercent }}% of PM, SAD {{ c.trivialPercent }}% of PM ({{ c.policyVersion }}).</small></p>
          @if (c.state === 'DRAFT' && p.canApproveMateriality) { <button matButton="outlined" (click)="send('/api/ui/materiality/' + c.assessmentId + '/approve', {}, 'Partner materiality approval recorded.')" [disabled]="cmd.busy()">Approve calculated materiality</button> }
        }
        @if (p.materialitySource; as s) {
          <p>Source: mapping version {{ s.mappingVersion }} over trial balance <code>{{ s.datasetDigest.slice(0, 12) }}</code> ({{ s.currency }}).</p>
          <div class="inline-form">
            <label>Benchmark <select name="option" [(ngModel)]="calc.option">
              @for (o of s.options; track o.kind + (o.destinationCode ?? '')) { @if (o.amount !== null) { <option [value]="o.kind + '|' + (o.destinationCode ?? '')">{{ o.label }} — {{ o.amount | money }}</option> } }</select></label>
            <label>Base rate % <input name="rate" inputmode="decimal" [(ngModel)]="calc.rate" /></label>
            <label>TE % of PM <input name="te" inputmode="decimal" [(ngModel)]="calc.performance" /></label>
            <label>SAD % of PM <input name="sad" inputmode="decimal" [(ngModel)]="calc.trivial" /></label>
          </div>
          @if (range(p); as r) { <p><small>Policy range for this benchmark: {{ r.minRatePercent }}%–{{ r.maxRatePercent }}%; TE {{ p.performanceMin }}–{{ p.performanceMax }}% of PM; SAD {{ p.trivialMin }}–{{ p.trivialMax }}% of PM.</small></p> }
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
          @if (p.canApproveMateriality) { <button matButton="filled" (click)="send('/api/ui/materiality/' + m.id + '/approve', {}, 'Partner materiality approval recorded.')" [disabled]="cmd.busy()">Approve materiality</button> }
          @if (m.approvedAt) { <p><small>Approved by {{ m.approvedByUserId }} at {{ m.approvedAt.slice(0, 16).replace('T', ' ') }}.</small></p> }
          @if (m.status === 'RECALCULATION_REQUIRED') { <p class="warning-text">A prior approval is not a verified independent Partner approval under the current policy. Recalculate and obtain Partner approval.</p> }
          @if (m.qualitativeConsiderations) { <p>Qualitative considerations: {{ m.qualitativeConsiderations }}</p> }
        } @else { <p>No materiality assessment has been recorded for this engagement.</p> }
        @if (p.materiality && !p.latestCalculation) {
          <p class="muted">This historical/manual assessment is read-only and cannot receive a new approval. Recalculate materiality from the current approved mapping and sealed trial balance.</p>
        }
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
              <label>Rationale <input [(ngModel)]="input(row).rationale" [name]="'r-' + row.riskId" maxlength="500" required [attr.aria-label]="'Band rationale for ' + row.area" /></label>
              <button matButton="outlined" (click)="assess(row.riskId)" [disabled]="cmd.busy() || !input(row).rationale.trim()" [attr.aria-label]="'Assess band for ' + row.area">Assess band</button>
            </div>
            @if (row.band && p.canAssignOwners && p.team.length) {
              <div class="inline-form">
                <label>Owner <select [(ngModel)]="input(row).owner" [name]="'o-' + row.riskId" [attr.aria-label]="'Owner for ' + row.area"><option value="">Select</option>@for (t of p.team; track t.userId) { <option [value]="t.userId">{{ t.name }} ({{ t.levelLabel }})</option> }</select></label>
                <button matButton (click)="assign(row.riskId)" [disabled]="cmd.busy() || !input(row).owner" [attr.aria-label]="'Assign owner for ' + row.area">Assign owner</button>
              </div>
            }
            @if (row.partnerReviewRequired && !row.partnerCleared && p.isPartner && row.assessedByUserId !== session.current()?.userId) {
              <div class="inline-form">
                <label>Partner review note <input [(ngModel)]="input(row).note" [name]="'n-' + row.riskId" maxlength="1000" required [attr.aria-label]="'Partner review note for ' + row.area" /></label>
                <button matButton="filled" (click)="clear(row.riskId)" [disabled]="cmd.busy() || !input(row).note.trim()" [attr.aria-label]="'Record Partner review for ' + row.area">Record Partner review</button>
              </div>
            }
          </article>
        } @empty { <p>No risks are recorded yet.</p> }
      </section>

      @if (p.fsliStratification.length) {
        <section class="panel" aria-labelledby="fsli-stratification-heading">
          <h2 id="fsli-stratification-heading">FSLI Risk Stratification</h2>
          <p><small>Financial statement line items classified against Tolerable Error (TE) and Planning Materiality (PM) under STE 2.1 §4.2.4. Green: balance &lt; TE; Amber: TE &le; balance &le; PM; Red: balance &gt; PM or qualitative triggers (critical estimates, high inherent risk, fraud risk).</small></p>
          <div class="table-scroll"><table>
            <thead><tr><th scope="col">Code</th><th scope="col">Section</th><th scope="col">Area</th><th scope="col" class="number">Balance</th><th scope="col">Band</th><th scope="col">Execution</th><th scope="col">Review</th><th scope="col">Explanation</th></tr></thead>
            <tbody>@for (row of p.fsliStratification; track row.destinationCode + row.statementSection) {
              <tr>
                <td><code>{{ row.destinationCode }}</code></td>
                <td>{{ row.statementSection }}</td>
                <td>{{ row.auditArea ?? '—' }}</td>
                <td class="number">{{ row.balance | money }} {{ row.currency }}</td>
                <td><span class="status-chip" [attr.aria-label]="'Band ' + row.band">{{ row.band }}</span></td>
                <td><small>{{ row.performerRole }}</small></td>
                <td><small>{{ row.reviewerRole }}</small></td>
                <td><small>{{ row.explanation }}</small></td>
              </tr>
            }</tbody>
          </table></div>
        </section>
      }

      <section class="panel" aria-labelledby="populations-heading">
        <h2 id="populations-heading">Populations (§20.1)</h2>
        <div class="table-scroll"><table>
          <thead><tr><th scope="col">Purpose</th><th scope="col">Assertion</th><th scope="col" class="number">Rows</th><th scope="col" class="number">Control total</th><th scope="col">Status</th></tr></thead>
          <tbody>@for (x of p.populations; track x.id) { <tr><td><a [routerLink]="['/app/audit/populations', x.id]">{{ x.purpose }}</a></td><td>{{ x.assertion }}</td><td class="number">{{ x.rowCount }}</td><td class="number">{{ x.currency }} {{ x.monetaryControlTotal | money }}</td><td>{{ x.status }}</td></tr> }
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
        <ul>@for (f of p.findings; track f.id) { <li>{{ f.findingType }} · {{ f.status }} · {{ f.monetaryAmount === null ? 'unquantified' : (f.monetaryAmount | money) }} <a [routerLink]="['/app/findings', f.id]">Open</a></li> }
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
        <ul>@for (w of p.workpapers; track w.id) { <li>{{ w.index }} — {{ w.title }} <small>Status: {{ w.status }} · Revision {{ w.revision }}</small> <a [routerLink]="['/app/audit/workpapers', w.id]">Open</a></li> }
          @empty { <li>No workpapers created for this audit plan yet.</li> }</ul>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class AuditPlan {
  private readonly api = inject(Api);
  readonly session = inject(SessionService);
  readonly id = routeGuid();
  readonly plan = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/audit-plan` : null), decodePlan,
    'Sign in with an authorized internal staff identity assigned to this engagement to view its audit plan.');
  readonly cmd = new CommandState(this.api);
  private readonly inputs = new Map<string, RowInput>();
  calc = { option: '', rate: '1', performance: '75', trivial: '5', rationale: '' };
  risk = { area: '', assertion: '', description: '', drivers: '', significance: 'NORMAL', response: '' };
  pop = { purpose: '', assertion: '', receipt: '', extraction: '', rows: 0, total: '', currency: '' };
  finding = { type: '', impact: '', amount: '' };
  milestone = { cutoff: '', fieldwork: '', draft: '', finalReport: '', adjustment: '', override: '' };

  saveMilestones(): void {
    const cutoff = this.milestone.cutoff.trim();
    if (!cutoff) return this.invalid('Enter the statutory filing cutoff date in YYYY-MM-DD format.');
    this.send(`/api/ui/engagements/${this.id()}/milestones`, {
      statutoryFilingCutoff: cutoff,
      fieldworkStartDate: this.milestone.fieldwork.trim() || null,
      draftReportDate: this.milestone.draft.trim() || null,
      finalReportDate: this.milestone.finalReport.trim() || null,
      adjustmentReason: this.milestone.adjustment.trim() || null,
      warningOverrideReason: this.milestone.override.trim() || null,
    }, 'Statutory milestones and archive deadline recorded.');
  }

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
      trivialPercent: trivial, rationale: this.calc.rationale }, 'Materiality calculated; independent Engagement Partner materiality approval is required.');
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
