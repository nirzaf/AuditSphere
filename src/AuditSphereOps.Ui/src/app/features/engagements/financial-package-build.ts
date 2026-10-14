import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, date, guid, nat, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { decodeScheduleSources } from '../accounting/specialist-preparation';

export const decodeBuildInputs = obj({
  engagementId: guid,
  mappings: arr(obj({ id: guid, version: nat, datasetId: guid }), 100),
  plans: arr(obj({ id: guid, baseDatasetId: guid, resultHash: text }), 100),
  periods: arr(obj({ id: guid, periodCode: text, startDate: date, endDate: date, currency: text, basis: text }), 100),
  comparatives: arr(obj({ id: guid, framework: text, periodStart: text, periodEnd: text, currency: text, status: text }), 100),
});
export type BuildInputs = ReturnType<typeof decodeBuildInputs>;

export interface AllocationForm { generalLedgerLineId: string; signedAmount: string }
export interface MovementForm {
  section: string; description: string; amount: string; isNonCash: boolean; movementDate: string; activityCode: string;
  sourceReferences: string; rationale: string; allocations: AllocationForm[];
}
export interface DisclosureForm { code: string; response: string; notApplicable: boolean; rationale: string }
export interface BuildForm {
  mappingVersionId: string; adjustmentPlanId: string; periodId: string; framework: string; templateVersion: string;
  periodStart: string; periodEnd: string; cashBeginning: string; cashEnding: string; cashAccountCodes: string;
  openingEvidence: string; closingEvidence: string; comparativePackageId: string; comparativeBasis: string; comparativeEvidence: string;
  movements: MovementForm[]; disclosures: DisclosureForm[];
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const AMOUNT = /^-?\d{1,13}(\.\d{1,6})?$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const list = (value: string): string[] => value.split(',').map((x) => x.trim()).filter((x) => x.length > 0);

export function emptyBuildForm(): BuildForm {
  return { mappingVersionId: '', adjustmentPlanId: '', periodId: '', framework: 'IFRS', templateVersion: '', periodStart: '', periodEnd: '',
    cashBeginning: '0', cashEnding: '0', cashAccountCodes: '', openingEvidence: '', closingEvidence: '',
    comparativePackageId: '', comparativeBasis: '', comparativeEvidence: '', movements: [], disclosures: [] };
}

export function emptyMovement(): MovementForm {
  return { section: 'OPERATING', description: '', amount: '', isNonCash: false, movementDate: '', activityCode: '', sourceReferences: '',
    rationale: '', allocations: [] };
}

/**
 * The request the staff build screen sends. It refuses anything the server would reject for form reasons, so a plain typo never
 * reaches the worker. The server still decides every accounting rule, including whether a source line belongs to the period.
 */
export function toBuildRequest(form: BuildForm): { body: unknown } | { error: string } {
  if (!GUID.test(form.mappingVersionId) || !GUID.test(form.adjustmentPlanId)) return { error: 'Choose an approved mapping and a finalized adjustment plan.' };
  if (!DATE.test(form.periodStart) || !DATE.test(form.periodEnd)) return { error: 'Choose a reporting period.' };
  if (!form.templateVersion.trim() || form.templateVersion.length > 200) return { error: 'Enter a template version of up to 200 characters.' };
  if (!AMOUNT.test(form.cashBeginning) || !AMOUNT.test(form.cashEnding)) return { error: 'Opening and closing cash must be amounts with at most six decimals.' };
  const comparativeId = form.comparativePackageId.trim();
  if (comparativeId && (!GUID.test(comparativeId) || !form.comparativeBasis.trim() || !form.comparativeEvidence.trim()))
    return { error: 'A comparative package needs a valid package ID, basis, and evidence reference.' };
  if (form.movements.length === 0) return { error: 'Add at least one cash-flow movement.' };
  for (const [index, m] of form.movements.entries()) {
    const at = `Movement ${index + 1}`;
    if (!['OPERATING', 'INVESTING', 'FINANCING'].includes(m.section)) return { error: `${at}: choose a section.` };
    if (!m.description.trim() || m.description.length > 2000) return { error: `${at}: enter a description.` };
    if (!AMOUNT.test(m.amount)) return { error: `${at}: enter an amount with at most six decimals.` };
    if (!DATE.test(m.movementDate)) return { error: `${at}: enter the movement date.` };
    if (!/^[A-Za-z0-9_.-]{1,100}$/.test(m.activityCode.trim())) return { error: `${at}: enter an activity code.` };
    if (list(m.sourceReferences).length === 0) return { error: `${at}: enter at least one evidence reference.` };
    if (!m.rationale.trim() || m.rationale.length > 2000) return { error: `${at}: enter the classification rationale.` };
    for (const a of m.allocations) {
      if (!GUID.test(a.generalLedgerLineId)) return { error: `${at}: a source line is not valid.` };
      if (!AMOUNT.test(a.signedAmount) || Number(a.signedAmount) === 0) return { error: `${at}: a source contribution needs a non-zero amount.` };
    }
  }
  for (const d of form.disclosures) {
    if (!d.code.trim() || d.code.length > 100) return { error: 'Each disclosure needs a code.' };
    if (d.notApplicable ? !d.rationale.trim() : !d.response.trim()) return { error: `Disclosure ${d.code}: give a response, or a rationale when not applicable.` };
  }
  return {
    body: {
      adjustmentPlanId: form.adjustmentPlanId, mappingVersionId: form.mappingVersionId, framework: form.framework.trim(),
      periodStart: form.periodStart, periodEnd: form.periodEnd, templateVersion: form.templateVersion.trim(),
      supplementaryInformation: {
      // Keep accounting values as decimal strings through JSON serialization. Converting to JS
      // Number here can round values before the API's exact decimal converter sees them.
      cashBeginning: form.cashBeginning, cashEnding: form.cashEnding,
        comparative: comparativeId ? { packageId: comparativeId, basis: form.comparativeBasis.trim(), evidenceReference: form.comparativeEvidence.trim() } : null,
        cashFlowLines: form.movements.map((m) => ({
          section: m.section, description: m.description.trim(), amount: m.amount, isNonCash: m.isNonCash,
          movementDate: m.movementDate, activityCode: m.activityCode.trim(), sourceReferences: list(m.sourceReferences),
          classificationRationale: m.rationale.trim(),
          sourceAllocations: m.allocations.length
            ? m.allocations.map((a) => ({ generalLedgerLineId: a.generalLedgerLineId, signedAmount: a.signedAmount }))
            : null,
        })),
        disclosures: form.disclosures.map((d) => ({ code: d.code.trim(), response: d.notApplicable ? '' : d.response.trim(),
          notApplicable: d.notApplicable, rationale: d.notApplicable ? d.rationale.trim() : null })),
        equityLines: null, noteLines: null, fxCashEffects: null,
        cashAccountCodes: list(form.cashAccountCodes), openingCashEvidenceReferences: list(form.openingEvidence),
        closingCashEvidenceReferences: list(form.closingEvidence),
      },
    },
  };
}

@Component({
  selector: 'audit-financial-package-build',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a [routerLink]="['/app/engagements', id()]">Engagement</a> / <span>Build financial package</span></nav>
    <audit-page-header title="Build financial package" eyebrow="Financial reporting"
      description="Queue a reviewed build of a financial-statement package. Cash-flow movements can be linked to accepted general-ledger lines. The worker builds the package; review and approval happen afterwards." />
    <audit-state [loading]="inputs.loading()" [error]="inputs.error()" label="the build inputs" />
    @if (inputs.data(); as data) {
      <form class="panel" aria-label="Financial package build" (submit)="$event.preventDefault(); submit()">
        <h2>Basis</h2>
        <label>Approved mapping
          <select name="mapping" [(ngModel)]="form.mappingVersionId" (ngModelChange)="form.adjustmentPlanId = ''">
            <option value="">Choose a mapping</option>
            @for (m of data.mappings; track m.id) { <option [value]="m.id">Version {{ m.version }} · {{ m.id }}</option> }
          </select>
        </label>
        <label>Finalized adjustment plan
          <select name="plan" [(ngModel)]="form.adjustmentPlanId">
            <option value="">Choose a plan</option>
            @for (p of plansFor(data); track p.id) { <option [value]="p.id">{{ p.id }}</option> }
          </select>
        </label>
        <label>Reporting period
          <select name="period" [(ngModel)]="form.periodId" (ngModelChange)="choosePeriod(data)">
            <option value="">Choose a period</option>
            @for (p of data.periods; track p.id) { <option [value]="p.id">{{ p.periodCode }} · {{ p.startDate }} to {{ p.endDate }} · {{ p.currency }} · {{ p.basis }}</option> }
          </select>
        </label>
        <label>Framework <input name="framework" [(ngModel)]="form.framework" (ngModelChange)="clearComparative(data)" maxlength="50" /></label>
        <label>Template version <input name="template" [(ngModel)]="form.templateVersion" maxlength="200" required /></label>
        <h2>Comparative package</h2>
        <label>Comparative package (optional)
          <select name="comparativePackage" [(ngModel)]="form.comparativePackageId">
            <option value="">No comparative package</option>
            @for (p of comparativesFor(data); track p.id) { <option [value]="p.id">{{ p.periodStart }} to {{ p.periodEnd }} · {{ p.framework }} · {{ p.currency }} · {{ p.id }}</option> }
          </select>
        </label>
        @if (form.comparativePackageId.trim()) {
          <label>Comparative basis <input name="comparativeBasis" [(ngModel)]="form.comparativeBasis" maxlength="100" required /></label>
          <label>Comparative evidence reference <input name="comparativeEvidence" [(ngModel)]="form.comparativeEvidence" maxlength="2000" required /></label>
        }

        <h2>Cash</h2>
        <label>Opening cash <input name="cashBeginning" [(ngModel)]="form.cashBeginning" inputmode="decimal" /></label>
        <label>Closing cash <input name="cashEnding" [(ngModel)]="form.cashEnding" inputmode="decimal" /></label>
        <label>Cash account codes (comma separated) <input name="cashAccounts" [(ngModel)]="form.cashAccountCodes" /></label>
        <label>Opening cash evidence (comma separated) <input name="openingEvidence" [(ngModel)]="form.openingEvidence" /></label>
        <label>Closing cash evidence (comma separated) <input name="closingEvidence" [(ngModel)]="form.closingEvidence" /></label>

        <h2>Cash-flow movements</h2>
        <p><small>A movement that links to accepted general-ledger lines is source-resolved only when its links total its amount. Preparer text alone is never lineage.</small></p>
        @for (m of form.movements; track $index; let i = $index) {
          <fieldset class="movement">
            <legend>Movement {{ i + 1 }}</legend>
            <label>Section <select [name]="'section' + i" [(ngModel)]="m.section"><option>OPERATING</option><option>INVESTING</option><option>FINANCING</option></select></label>
            <label>Description <input [name]="'description' + i" [(ngModel)]="m.description" maxlength="2000" /></label>
            <label>Amount <input [name]="'amount' + i" [(ngModel)]="m.amount" inputmode="decimal" /></label>
            <label><input type="checkbox" [name]="'noncash' + i" [(ngModel)]="m.isNonCash" /> Noncash (excluded from the cash bridge)</label>
            <label>Movement date <input type="date" [name]="'date' + i" [(ngModel)]="m.movementDate" /></label>
            <label>Activity code <input [name]="'activity' + i" [(ngModel)]="m.activityCode" maxlength="100" /></label>
            <label>Evidence references (comma separated) <input [name]="'evidence' + i" [(ngModel)]="m.sourceReferences" /></label>
            <label>Classification rationale <textarea [name]="'rationale' + i" [(ngModel)]="m.rationale" maxlength="2000" rows="2"></textarea></label>
            <h3>Accepted source lines</h3>
            @if (m.allocations.length) {
              <ul>
                @for (a of m.allocations; track $index; let j = $index) {
                  <li><code>{{ a.generalLedgerLineId }}</code> <input [name]="'alloc' + i + '-' + j" [(ngModel)]="a.signedAmount" inputmode="decimal" aria-label="Contribution amount" />
                    <button matButton type="button" (click)="m.allocations.splice(j, 1)">Remove</button></li>
                }
              </ul>
            }
            <button matButton="outlined" type="button" (click)="pickerFor.set(i)">{{ pickerFor() === i ? 'Hide source lines' : 'Choose source lines' }}</button>
            <button matButton type="button" (click)="form.movements.splice(i, 1)">Remove movement</button>
          </fieldset>
        }
        <button matButton="outlined" type="button" (click)="form.movements.push(newMovement())">Add movement</button>

        @if (pickerFor() !== null && form.periodId) {
          <section aria-label="Accepted source lines" class="panel">
            <h3>Accepted general-ledger lines for the period</h3>
            <audit-state [loading]="sources.loading()" [error]="sources.error()" label="the accepted source lines" />
            @if (sources.data(); as page) {
              <div class="table-scroll"><table>
                <caption>Accepted general-ledger lines, page {{ page.page }}</caption>
                <thead><tr><th scope="col">Date</th><th scope="col">Journal</th><th scope="col">Account</th><th scope="col" class="number">Signed amount</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
                <tbody>@for (line of page.lines; track line.lineId) {
                  <tr><td>{{ line.postingDate }}</td><td>{{ line.journal }}</td><td>{{ line.accountCode }}</td><td class="number">{{ line.signedAmount }} {{ line.currency }}</td>
                    <td><button matButton="outlined" type="button" (click)="addSource(pickerFor()!, line.lineId, line.signedAmount)">Add to movement {{ pickerFor()! + 1 }}</button></td></tr>
                } @empty { <tr><td colspan="5">No accepted general-ledger lines in this period on this page.</td></tr> }</tbody></table></div>
              <p class="actions">
                <button matButton="outlined" type="button" [disabled]="sourcePage() <= 1" (click)="sourcePage.set(sourcePage() - 1)">Previous lines</button>
                <button matButton="outlined" type="button" [disabled]="!page.hasMore" (click)="sourcePage.set(sourcePage() + 1)">Next lines</button>
              </p>
            }
          </section>
        }

        <h2>Disclosures</h2>
        @for (d of form.disclosures; track $index; let k = $index) {
          <fieldset class="movement">
            <legend>Disclosure {{ k + 1 }}</legend>
            <label>Code <input [name]="'dcode' + k" [(ngModel)]="d.code" maxlength="100" /></label>
            <label><input type="checkbox" [name]="'dna' + k" [(ngModel)]="d.notApplicable" /> Not applicable</label>
            @if (d.notApplicable) {
              <label>Rationale <textarea [name]="'drat' + k" [(ngModel)]="d.rationale" maxlength="2000" rows="2"></textarea></label>
            } @else {
              <label>Response <textarea [name]="'dresp' + k" [(ngModel)]="d.response" maxlength="4000" rows="2"></textarea></label>
            }
            <button matButton type="button" (click)="form.disclosures.splice(k, 1)">Remove disclosure</button>
          </fieldset>
        }
        <button matButton="outlined" type="button" (click)="form.disclosures.push(newDisclosure())">Add disclosure</button>

        <p class="actions"><button matButton="filled" type="submit" [disabled]="cmd.busy()">{{ cmd.busy() ? 'Queuing…' : 'Queue package build' }}</button></p>
      </form>
      @if (formError()) { <p role="alert">{{ formError() }}</p> }
      @if (queued(); as id) {
        <section class="panel" role="status">
          <h2>Build queued</h2>
          <p>Operation <code>{{ id }}</code> is queued. The worker builds the package. It appears in <a routerLink="/app/accounting/reviews">package reviews</a> once it completes; a queued operation is not a built package.</p>
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class FinancialPackageBuild {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly inputs = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/financial-package-inputs` : null),
    decodeBuildInputs, 'The build inputs are not available in your current scope.');
  readonly cmd = new CommandState(this.api);
  readonly form: BuildForm = emptyBuildForm();
  readonly formError = signal('');
  readonly queued = signal<string | null>(null);
  readonly pickerFor = signal<number | null>(null);
  readonly sourcePage = signal(1);
  /** The period the source picker reads. A signal, so changing the period reloads the lines. */
  readonly chosenPeriod = signal('');
  readonly sources = this.api.resource(() => {
    const periodId = this.chosenPeriod();
    return this.id() && periodId && this.pickerFor() !== null
      ? `/api/ui/engagements/${this.id()}/specialist-preparation/source-lines?periodId=${periodId}&page=${this.sourcePage()}`
      : null;
  }, decodeScheduleSources, 'The accepted general-ledger lines are unavailable in your current scope.');

  newMovement(): MovementForm { return emptyMovement(); }
  newDisclosure(): DisclosureForm { return { code: '', response: '', notApplicable: false, rationale: '' }; }

  /** Only plans finalized on the chosen mapping's dataset can build that mapping, so the list follows the mapping. */
  plansFor(data: BuildInputs) {
    const mapping = data.mappings.find((m) => m.id === this.form.mappingVersionId);
    return mapping ? data.plans.filter((p) => p.baseDatasetId === mapping.datasetId) : [];
  }

  comparativesFor(data: BuildInputs) {
    const period = data.periods.find((p) => p.id === this.form.periodId);
    return period ? data.comparatives.filter((p) => p.status === 'VALIDATED' && p.framework === this.form.framework.trim() &&
      p.currency === period.currency && p.periodEnd < period.startDate) : [];
  }

  choosePeriod(data: BuildInputs): void {
    const period = data.periods.find((p) => p.id === this.form.periodId);
    this.form.periodStart = period?.startDate ?? '';
    this.form.periodEnd = period?.endDate ?? '';
    this.clearComparative(data);
    this.chosenPeriod.set(this.form.periodId);
    this.sourcePage.set(1);
  }

  clearComparative(data: BuildInputs): void {
    if (this.form.comparativePackageId && !this.comparativesFor(data).some((x) => x.id === this.form.comparativePackageId))
      this.form.comparativePackageId = '';
  }

  addSource(movement: number, lineId: string, signedAmount: string): void {
    const m = this.form.movements[movement];
    if (!m || m.allocations.some((a) => a.generalLedgerLineId === lineId)) return;
    m.allocations.push({ generalLedgerLineId: lineId, signedAmount });
  }

  submit(): void {
    this.formError.set('');
    const built = toBuildRequest(this.form);
    if ('error' in built) { this.formError.set(built.error); return; }
    void this.cmd.run<string>('/api/ui/accounting/packages/builds', built.body, 'Package build queued.', (operation) => this.queued.set(operation));
  }
}
