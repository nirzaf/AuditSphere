import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { exactDecimal, guidPattern } from '../../core/contracts';
import { guid, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

interface AllocationDraft { lineId: string; amount: string; }
interface MovementDraft {
  section: string; description: string; amount: string; isNonCash: boolean; movementDate: string;
  activityCode: string; sourceReferences: string; rationale: string; allocations: AllocationDraft[];
}
interface DisclosureDraft { code: string; response: string; notApplicable: boolean; rationale: string; }
interface EquityDraft {
  lineCode: string; description: string; opening: string; profitOrLoss: string; oci: string;
  capital: string; dividends: string; closing: string; evidence: string;
}
const decodeBuildOperation = obj({ id: guid, status: text, packageId: nullable(guid), errorCode: nullable(text) });

@Component({
  selector: 'audit-financial-package-build',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a routerLink="/app/accounting">Client accounting</a> / <span>Build package</span></nav>
    <audit-page-header title="Prepare financial-package inputs" eyebrow="Client accounting" description="Create a durable package calculation from an approved mapping and finalized adjustment plan. Cash-flow contributions must link to accepted general-ledger lines." />
    <section class="panel" aria-labelledby="scope-heading">
      <h2 id="scope-heading">Approved source and reporting period</h2>
      <p>Use the exact mapping and adjustment-plan IDs shown in their current workspaces. The server rechecks their firm, client, engagement, revision, approval and period scope.</p>
      <div class="inline-form">
        <label>Adjustment plan ID <input name="plan" [(ngModel)]="planId" [pattern]="guidPattern" required autocomplete="off" /></label>
        <label>Approved mapping ID <input name="mapping" [(ngModel)]="mappingId" [pattern]="guidPattern" required autocomplete="off" /></label>
        <label>Framework <input name="framework" [(ngModel)]="framework" maxlength="100" required /></label>
        <label>Period start <input name="periodStart" [(ngModel)]="periodStart" type="date" required /></label>
        <label>Period end <input name="periodEnd" [(ngModel)]="periodEnd" type="date" required /></label>
        <label>Template version <input name="template" [(ngModel)]="templateVersion" maxlength="100" required /></label>
      </div>
    </section>

    <section class="panel" aria-labelledby="cash-heading">
      <h2 id="cash-heading">Opening and closing cash</h2>
      <p>Enter exact decimal amounts. List the cash-account population and evidence references used for both balances.</p>
      <div class="inline-form">
        <label>Opening cash <input name="cashBeginning" [(ngModel)]="cashBeginning" inputmode="decimal" required /></label>
        <label>Closing cash <input name="cashEnding" [(ngModel)]="cashEnding" inputmode="decimal" required /></label>
        <label>Cash account codes, comma separated <input name="cashAccounts" [(ngModel)]="cashAccountCodes" required /></label>
        <label>Opening evidence references <input name="openingEvidence" [(ngModel)]="openingEvidence" required /></label>
        <label>Closing evidence references <input name="closingEvidence" [(ngModel)]="closingEvidence" required /></label>
      </div>
    </section>

    <section class="panel" aria-labelledby="movements-heading">
      <h2 id="movements-heading">Cash-flow movements and accepted-line allocations</h2>
      <p>Each movement needs an accepted GL line allocation. Amounts follow the signed source line. Preparers cannot replace source identity with free-text references.</p>
      @for (line of movements; track $index; let i = $index) {
        <fieldset class="panel"><legend>Movement {{ i + 1 }}</legend>
          <div class="inline-form">
            <label>Section <select [name]="'section-'+i" [(ngModel)]="line.section"><option value="OPERATING">Operating</option><option value="INVESTING">Investing</option><option value="FINANCING">Financing</option></select></label>
            <label>Description <input [name]="'description-'+i" [(ngModel)]="line.description" maxlength="500" required /></label>
            <label>Signed amount <input [name]="'amount-'+i" [(ngModel)]="line.amount" inputmode="decimal" required /></label>
            <label>Movement date <input [name]="'date-'+i" [(ngModel)]="line.movementDate" type="date" required /></label>
            <label>Activity code <input [name]="'activity-'+i" [(ngModel)]="line.activityCode" maxlength="100" required /></label>
            <label>Evidence references <input [name]="'refs-'+i" [(ngModel)]="line.sourceReferences" required /></label>
            <label>Classification rationale <textarea [name]="'rationale-'+i" [(ngModel)]="line.rationale" maxlength="2000" required></textarea></label>
            <label><input [name]="'noncash-'+i" type="checkbox" [(ngModel)]="line.isNonCash" /> Noncash movement</label>
          </div>
          <h3>Source contributions</h3>
          @for (allocation of line.allocations; track $index; let j = $index) {
            <div class="inline-form">
              <label>Accepted GL line ID <input [name]="'line-'+i+'-'+j" [(ngModel)]="allocation.lineId" [pattern]="guidPattern" required autocomplete="off" /></label>
              <label>Signed contribution <input [name]="'contribution-'+i+'-'+j" [(ngModel)]="allocation.amount" inputmode="decimal" required /></label>
              <button matButton="outlined" type="button" (click)="removeAllocation(i,j)">Remove contribution</button>
            </div>
          }
          <p class="actions"><button matButton="outlined" type="button" (click)="addAllocation(i)">Add accepted-line contribution</button>
            <button matButton="outlined" type="button" (click)="removeMovement(i)">Remove movement</button></p>
        </fieldset>
      }
      <button matButton="outlined" type="button" (click)="addMovement()">Add movement</button>
    </section>

    <section class="panel" aria-labelledby="disclosures-heading">
      <h2 id="disclosures-heading">Disclosure responses</h2>
      @for (item of disclosures; track $index; let i = $index) {
        <div class="inline-form">
          <label>Disclosure code <input [name]="'disclosure-code-'+i" [(ngModel)]="item.code" maxlength="100" required /></label>
          <label>Response <textarea [name]="'disclosure-response-'+i" [(ngModel)]="item.response" maxlength="4000"></textarea></label>
          <label><input [name]="'disclosure-na-'+i" type="checkbox" [(ngModel)]="item.notApplicable" /> Not applicable</label>
          <label>Not-applicable rationale <input [name]="'disclosure-rationale-'+i" [(ngModel)]="item.rationale" maxlength="2000" /></label>
          <button matButton="outlined" type="button" (click)="disclosures.splice(i,1)">Remove response</button>
        </div>
      }
      <button matButton="outlined" type="button" (click)="addDisclosure()">Add disclosure response</button>
    </section>

    <section class="panel" aria-labelledby="equity-heading">
      <h2 id="equity-heading">Equity roll-forward</h2>
      <p>Supply typed equity movements; amounts are exact signed decimals and evidence references remain user supplied.</p>
      @for (line of equityLines; track $index; let i = $index) {
        <fieldset class="panel"><legend>Equity line {{ i + 1 }}</legend>
          <div class="inline-form">
            <label>Line code <input [name]="'equity-code-'+i" [(ngModel)]="line.lineCode" required /></label>
            <label>Description <input [name]="'equity-description-'+i" [(ngModel)]="line.description" required /></label>
            <label>Opening <input [name]="'equity-open-'+i" [(ngModel)]="line.opening" inputmode="decimal" required /></label>
            <label>Profit or loss <input [name]="'equity-profit-'+i" [(ngModel)]="line.profitOrLoss" inputmode="decimal" required /></label>
            <label>OCI <input [name]="'equity-oci-'+i" [(ngModel)]="line.oci" inputmode="decimal" required /></label>
            <label>Capital movements <input [name]="'equity-capital-'+i" [(ngModel)]="line.capital" inputmode="decimal" required /></label>
            <label>Dividends <input [name]="'equity-dividends-'+i" [(ngModel)]="line.dividends" inputmode="decimal" required /></label>
            <label>Closing <input [name]="'equity-close-'+i" [(ngModel)]="line.closing" inputmode="decimal" required /></label>
            <label>Evidence reference <input [name]="'equity-evidence-'+i" [(ngModel)]="line.evidence" required /></label>
            <button matButton="outlined" type="button" (click)="equityLines.splice(i,1)">Remove equity line</button>
          </div>
        </fieldset>
      }
      <button matButton="outlined" type="button" (click)="addEquityLine()">Add equity line</button>
    </section>

    <section class="panel" aria-labelledby="submit-heading">
      <h2 id="submit-heading">Queue package calculation</h2>
      <p>This queues one idempotent calculation. It does not approve, release or deliver a report. Packages with incomplete source links remain drafts.</p>
      <button matButton="filled" type="button" (click)="submit()" [disabled]="busy()">{{ busy() ? 'Queueing…' : 'Queue package build' }}</button>
      @if (operationId()) {
        <p role="status">Build operation: <code>{{ operationId() }}</code> · {{ operation.data()?.status ?? 'Loading state' }}
          <button matButton="outlined" type="button" (click)="operation.reload()">Refresh operation</button>
          @if (operation.data()?.packageId; as id) { <a [routerLink]="['/app/accounting/packages',id]">Open resulting package</a> }
          <a routerLink="/app/operations">Durable operations</a>
        </p>
      }
      @if (message()) { <p role="status" [attr.aria-live]="'polite'">{{ message() }}</p> }
    </section>
  `,
})
export class FinancialPackageBuild {
  private readonly api = inject(Api);
  readonly guidPattern = guidPattern;
  readonly busy = signal(false);
  readonly message = signal('');
  readonly operationId = signal('');
  readonly operation = this.api.resource(() => this.operationId() ? `/api/ui/accounting/package-builds/${this.operationId()}` : null,
    decodeBuildOperation, 'This package-build operation is not available in the current firm scope.');
  planId = '';
  mappingId = '';
  framework = 'IFRS';
  periodStart = '';
  periodEnd = '';
  templateVersion = '';
  cashBeginning = '';
  cashEnding = '';
  cashAccountCodes = '';
  openingEvidence = '';
  closingEvidence = '';
  movements: MovementDraft[] = [this.newMovement()];
  disclosures: DisclosureDraft[] = [{ code: '', response: '', notApplicable: false, rationale: '' }];
  equityLines: EquityDraft[] = [];

  addMovement(): void { this.movements = [...this.movements, this.newMovement()]; }
  removeMovement(i: number): void { this.movements = this.movements.filter((_, index) => index !== i); }
  addAllocation(i: number): void { this.movements[i].allocations = [...this.movements[i].allocations, { lineId: '', amount: '' }]; }
  removeAllocation(i: number, j: number): void { this.movements[i].allocations = this.movements[i].allocations.filter((_, index) => index !== j); }
  addDisclosure(): void { this.disclosures = [...this.disclosures, { code: '', response: '', notApplicable: false, rationale: '' }]; }
  addEquityLine(): void { this.equityLines = [...this.equityLines, this.newEquityLine()]; }

  async submit(): Promise<void> {
    this.message.set(''); this.operationId.set('');
    const ids = [this.planId, this.mappingId, ...this.movements.flatMap((x) => x.allocations.map((a) => a.lineId))];
    const amounts = [this.cashBeginning, this.cashEnding, ...this.movements.flatMap((x) => [x.amount, ...x.allocations.map((a) => a.amount)]),
      ...this.equityLines.flatMap((x) => [x.opening, x.profitOrLoss, x.oci, x.capital, x.dividends, x.closing])];
    if (ids.some((x) => !guidPattern.test(x)) || !this.framework.trim() || !this.periodStart || !this.periodEnd || !this.templateVersion.trim() ||
        !this.cashAccountCodes.trim() || !this.openingEvidence.trim() || !this.closingEvidence.trim() ||
        !this.movements.length || this.movements.some((x) => !x.description.trim() || !x.activityCode.trim() || !x.rationale.trim() ||
          !x.movementDate || !x.sourceReferences.trim() || !x.allocations.length) ||
        !this.disclosures.length || this.disclosures.some((x) => !x.code.trim() || (!x.notApplicable && !x.response.trim()) || (x.notApplicable && !x.rationale.trim())) ||
        !this.equityLines.length || this.equityLines.some((x) => !x.lineCode.trim() || !x.description.trim() || !x.evidence.trim()) ||
        amounts.some((x) => !exactDecimal(x))) {
      this.message.set('Complete the package scope, exact decimal amounts, cash evidence, disclosures, equity roll-forward and at least one accepted GL contribution for each movement.'); return;
    }
    const body = {
      adjustmentPlanId: this.planId, mappingVersionId: this.mappingId, framework: this.framework,
      periodStart: this.periodStart, periodEnd: this.periodEnd, templateVersion: this.templateVersion,
      supplementaryInformation: {
        cashBeginning: this.cashBeginning, cashEnding: this.cashEnding,
        cashAccountCodes: this.split(this.cashAccountCodes),
        openingCashEvidenceReferences: this.split(this.openingEvidence),
        closingCashEvidenceReferences: this.split(this.closingEvidence),
        cashFlowLines: this.movements.map((x) => ({
          section: x.section, description: x.description, amount: x.amount, isNonCash: x.isNonCash,
          movementDate: x.movementDate, activityCode: x.activityCode,
          sourceReferences: this.split(x.sourceReferences), classificationRationale: x.rationale,
          sourceAllocations: x.allocations.map((a) => ({ generalLedgerLineId: a.lineId, signedAmount: a.amount })),
        })),
        disclosures: this.disclosures.map((x) => ({ code: x.code, response: x.response,
          notApplicable: x.notApplicable, rationale: x.notApplicable ? x.rationale : null })),
        equityLines: this.equityLines.map((x) => ({ lineCode: x.lineCode, description: x.description,
          openingAmount: x.opening, profitOrLossAmount: x.profitOrLoss, ociAmount: x.oci,
          capitalMovementAmount: x.capital, dividendsAmount: x.dividends, closingAmount: x.closing,
          evidenceReference: x.evidence })),
      },
    };
    this.busy.set(true);
    const result = await this.api.command<{ value?: string }>('/api/ui/accounting/packages/build', body).finally(() => this.busy.set(false));
    if (!result.ok) { this.message.set(result.message); return; }
    this.operationId.set(typeof result.value === 'string' ? result.value : result.value?.value ?? '');
    this.message.set('Build operation queued. Refresh durable operations before repeating if its outcome is unknown.');
  }

  private split(value: string): string[] { return value.split(',').map((x) => x.trim()).filter(Boolean); }
  private newMovement(): MovementDraft { return { section: 'OPERATING', description: '', amount: '', isNonCash: false,
    movementDate: '', activityCode: '', sourceReferences: '', rationale: '', allocations: [{ lineId: '', amount: '' }] }; }
  private newEquityLine(): EquityDraft { return { lineCode: '', description: '', opening: '0', profitOrLoss: '0',
    oci: '0', capital: '0', dividends: '0', closing: '0', evidence: '' }; }
}
