import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, CommandState, queryParam } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { arr, bool, dec, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { ConsolidationGroupMembership } from './group-membership';

const report = obj({ scopeVersionId: guid, runId: nullable(guid), state: text, approvedAt: nullable(instant),
  lines: arr(obj({ component: text, taxonomyCode: text, componentAmount: dec, alignmentAmount: dec, eliminationAmount: dec, consolidatedAmount: dec, currency: text }), 20000) });
const scope = obj({ id: guid, groupName: text, version: int, method: text, currency: text, ownershipEdges: nat, groupedMatches: nat, outsideReviews: nat, openMatchCount: nat,
  componentCount: nat, approvedComponentCount: nat, externalPackCount: nat, approvedExternalPackCount: nat, advancedScheduleCount: nat, approvedAdvancedScheduleCount: nat,
  advancedExecutionCount: nat, approvedAdvancedExecutionCount: nat, advancedScheduleStatus: text, advancedScheduleGuidance: text, rateSetStatus: text,
  translationPolicyStatus: text, eliminationJournalCount: nat, approvedEliminationJournalCount: nat, status: text, runStatus: text, isAdvanced: bool, report: nullable(report) });
export const decodeConsolidation = obj({ canCreateGroup: bool, groups: arr(obj({ id: guid, code: text, name: text, scopes: arr(scope, 1000) }), 500) });
const periodOption = obj({ id: guid, clientName: text, code: text, startDate: text, endDate: text, basis: text, currency: text, status: text });
const methodOption = obj({ code: text, label: text, enabled: bool, guidance: text });
const rateSetOption = obj({ id: guid, code: text, version: int, effectiveFrom: nullable(text), effectiveTo: nullable(text) });
const policyOption = obj({ id: guid, code: text, functionalCurrency: text, presentationCurrency: text,
  closingRateType: text, averageRateType: text, historicalRateType: text });
const priorScopeOption = obj({ id: guid, periodId: guid, version: int, method: text, currency: text, approvedRun: text });
const existingScopeOption = obj({ id: guid, periodId: guid, reportingCurrency: text, method: text, openingBasis: text,
  exchangeRateSetVersionId: nullable(guid), translationPolicyVersionId: nullable(guid), translationRateDate: nullable(text),
  translationRateType: text, priorScopeVersionId: nullable(guid), groupRevision: nat, status: text });
const decodeScopeOptions = obj({ groupId: guid, groupCode: text, groupName: text, groupRevision: nat, canCreateScope: bool,
  periods: arr(periodOption, 500), currencies: arr(text, 100), methods: arr(methodOption, 20),
  approvedRateSets: arr(rateSetOption, 200), approvedTranslationPolicies: arr(policyOption, 200),
  approvedPriorScopes: arr(priorScopeOption, 100), existingScopes: arr(existingScopeOption, 500), guidance: text });
type ScopeOptions = ReturnType<typeof decodeScopeOptions>;
type ScopeIntent = { groupId: string; groupRevision: number; periodId: string; reportingCurrency: string; method: string;
  openingBasis: string; exchangeRateSetVersionId: string | null; translationPolicyVersionId: string | null;
  translationRateDate: string | null; translationRateType: string | null; priorScopeVersionId: string | null };
type Group = ReturnType<typeof decodeConsolidation>['groups'][number];
type ScopeVersion = Group['scopes'][number];
type CorrectionContext =
  | { state: 'none' | 'pending' | 'invalid' | 'unavailable' | 'wrong-group' }
  | { state: 'valid'; group: Group; scope: ScopeVersion };

/** Query-string correction identities are only usable when both match a scope in the authorized overview. */
export function resolveCorrectionContext(groups: Group[] | null, scopeValue: string | null,
  groupValue: string | null): CorrectionContext {
  if (scopeValue === null && groupValue === null) return { state: 'none' };
  if (!scopeValue || !groupValue || !guidPattern.test(scopeValue) || !guidPattern.test(groupValue)) return { state: 'invalid' };
  if (!groups) return { state: 'pending' };

  const scopeId = scopeValue.toLowerCase();
  const groupId = groupValue.toLowerCase();
  const group = groups.find(x => x.id.toLowerCase() === groupId);
  if (!group) return { state: 'unavailable' };
  const scope = group.scopes.find(x => x.id.toLowerCase() === scopeId);
  if (scope) return { state: 'valid', group, scope };
  return groups.some(x => x.scopes.some(s => s.id.toLowerCase() === scopeId))
    ? { state: 'wrong-group' }
    : { state: 'unavailable' };
}

/** Exact-decimal sum of report lines without floating point (scaled BigInt). */
export function sumDecimals(values: string[]): string {
  const places = Math.max(0, ...values.map((v) => (v.split('.')[1] ?? '').length));
  const total = values.reduce((acc, v) => {
    const neg = v.startsWith('-'); const [w, f = ''] = (neg ? v.slice(1) : v).split('.');
    const n = BigInt(w + f.padEnd(places, '0'));
    return acc + (neg ? -n : n);
  }, 0n);
  const neg = total < 0n; const digits = (neg ? -total : total).toString().padStart(places + 1, '0');
  return (neg ? '-' : '') + (places ? digits.slice(0, -places) + '.' + digits.slice(-places) : digits);
}

export function displayUtcInstant(value: string | null): string {
  return value ? `${new Date(value).toISOString().slice(0, 16).replace('T', ' ')} UTC` : '';
}

@Component({
  selector: 'audit-consolidation',
  imports: [FormsModule, RouterLink, ConsolidationGroupMembership, ...SHARED],
  template: `
    <nav aria-label="Consolidation location"><a routerLink="/app/accounting">Accounting</a> / <span>Group consolidation</span></nav>
    <audit-page-header title="Group consolidation" eyebrow="Group accounting" description="Approved group perimeters and component packs. Consolidation never mutates component client books." />
    <p><small>Only explicitly granted group scopes appear here. The enabled first profile is same-currency, fully-owned consolidation; configured foreign-operation translation is shown separately, while advanced methods remain fail-closed until approved source-bound schedules and execution verification are complete.</small></p>
    @let correction = correctionContext();
    @switch (correction.state) {
      @case ('pending') { <p role="status">Checking the correction source against the groups and scope versions available to this identity…</p> }
      @case ('valid') {
        <section class="panel" aria-labelledby="component-correction-heading">
          <h2 id="component-correction-heading">Correct a component in a new scope version</h2>
          <p>The selected source is in {{ correction.group.name }} ({{ correction.group.code }}), scope version {{ correction.scope.version }}. The existing scope, component and package evidence stays intact.</p>
          <ol>
            <li>Correct the underlying entity records through their existing accounting workflow and obtain a newly reviewed component package.</li>
            <li>Use the existing scope form below to create a new version for this same authorized group and reporting period. Review the period and other inputs before creating it.</li>
            <li>Open the new scope and submit the corrected package for the normal independent component review.</li>
          </ol>
          <p><small>This handoff never copies a component, creates a scope, replaces a pin, or approves evidence. The original scope, component, package and prior run remain available as historical evidence.</small></p>
          <p><a [routerLink]="[]" [queryParams]="{ correctionScopeId: correction.scope.id, correctionGroupId: correction.group.id }" fragment="create-scope-heading">Continue to the existing new-scope form</a></p>
          <p><a routerLink="/app/consolidation" [queryParams]="{}">Exit correction handoff and open ordinary scope creation</a></p>
        </section>
      }
      @case ('none') { }
      @case ('invalid') { <section class="panel" role="alert"><h2>Correction source could not be verified</h2><p>The handoff needs both a valid scope and group identity. Reopen it from the current authorized component workspace; no correction action was taken.</p><p><a routerLink="/app/consolidation" [queryParams]="{}">Open ordinary scope creation</a></p></section> }
      @case ('unavailable') { <section class="panel" role="alert"><h2>Correction source is unavailable</h2><p>This scope/group pair is not present in the current authorized overview. Reopen the handoff from a current component workspace; no correction action was taken.</p><p><a routerLink="/app/consolidation" [queryParams]="{}">Open ordinary scope creation</a></p></section> }
      @case ('wrong-group') { <section class="panel" role="alert"><h2>Correction source group does not match</h2><p>The selected scope does not belong to the supplied authorized group. Reopen the handoff from that scope’s component workspace; no correction action was taken.</p><p><a routerLink="/app/consolidation" [queryParams]="{}">Open ordinary scope creation</a></p></section> }
    }
    <audit-state [loading]="groups.loading()" [error]="groups.error()" label="the accounting group overview" />
    @if (groups.data(); as g) {
      @if (g.canCreateGroup) {
        <section class="panel" aria-labelledby="create-group-heading">
          <h2 id="create-group-heading">Create accounting group</h2>
          <p><small>This is the legal-entity group for consolidation, not a Microsoft group. Creating it requires a current firm-wide Manager, Partner, or Administrator grant and grants the creator explicit access to this group only.</small></p>
          <form (ngSubmit)="createGroup()">
            <label>Group code * <input name="groupCode" [(ngModel)]="groupCode" maxlength="100" required /></label>
            <label>Group name * <input name="groupName" [(ngModel)]="groupName" maxlength="300" required /></label>
            @if (createValidation()) { <p role="alert">{{ createValidation() }}</p> }
            <button type="submit" [disabled]="createCommand.busy() || createCommand.uncertain()">{{ createCommand.busy() ? 'Creating…' : 'Create accounting group' }}</button>
          </form>
          @if (createCommand.uncertain()) {
            <section class="recovery" aria-labelledby="group-recovery-heading">
              <h3 id="group-recovery-heading">Creation outcome needs review</h3>
              <p role="alert">{{ createCommand.message() }} AuditSphere will not repeat this request automatically.</p>
              <button type="button" (click)="recoverGroup()" [disabled]="checkingCreateRecovery()">{{ checkingCreateRecovery() ? 'Checking saved groups…' : 'Check for the exact saved group' }}</button>
              @if (createRecoveryError()) { <p role="alert">{{ createRecoveryError() }}</p> }
            </section>
          }
          @if (!createCommand.uncertain()) { <audit-command-message [message]="createCommand.message()" [failed]="createCommand.failed()" /> }
        </section>
      }
      @if (g.groups.length) {
        <section class="panel" aria-labelledby="create-scope-heading">
          <h2 id="create-scope-heading">Start or roll forward a consolidation scope</h2>
          <p><small>Select only from this identity’s explicitly granted groups and the periods, methods and approved FX inputs returned for that group. The saved scope records the group revision and, for a roll-forward, the approved prior scope and run basis. Creating a scope does not change component books or approve the perimeter.</small></p>
          <label>Accounting group * <select name="scopeGroup" [ngModel]="scopeGroupId()" (ngModelChange)="selectScopeGroup($event)" [disabled]="scopeCommand.busy() || scopeCommand.uncertain() || correctionContext().state === 'valid'">
            <option value="">Choose an authorized group</option>
            @for (grp of g.groups; track grp.id) { <option [value]="grp.id">{{ grp.name }} · {{ grp.code }}</option> }
          </select></label>
          @if (scopeOptions.loading()) { <p role="status">Loading permitted scope options…</p> }
          @if (scopeOptions.error()) { <p role="alert">{{ scopeOptions.error() }}</p> }
          @if (scopeOptions.data(); as options) {
            @if (!options.canCreateScope) { <p role="alert">This identity can read this group but cannot create a consolidation scope.</p> }
            @else {
              <p role="status">Group revision {{ options.groupRevision }} · {{ options.guidance }}</p>
              <form (ngSubmit)="createScope()">
                <label>Reporting period * <select name="scopePeriod" [(ngModel)]="scopePeriodId" required>
                  <option value="">Choose an authorized member reporting period</option>
                  @for (period of options.periods; track period.id) {
                    <option [value]="period.id">{{ period.clientName }} · {{ period.code }} · {{ period.startDate }} to {{ period.endDate }} · {{ period.currency }} ({{ period.status }})</option>
                  }
                </select></label>
                <label>Method * <select name="scopeMethod" [(ngModel)]="scopeMethod" required>
                  <option value="">Choose a supported method</option>
                  @for (method of options.methods; track method.code) { <option [value]="method.code" [disabled]="!method.enabled">{{ method.label }}{{ method.enabled ? '' : ' · unavailable' }}</option> }
                </select></label>
                @if (selectedMethod(options); as selected) { <p><small>{{ selected.guidance }}</small></p> }
                <label>Reporting currency * <select name="scopeCurrency" [(ngModel)]="scopeCurrency" required>
                  <option value="">Choose a currency</option>
                  @for (currency of currenciesFor(options); track currency) { <option [value]="currency">{{ currency }}</option> }
                </select></label>
                <label>Opening basis * <select name="scopeOpeningBasis" [(ngModel)]="scopeBasis" required>
                  <option value="INITIAL">Initial consolidation</option>
                  <option value="PRIOR" [disabled]="!options.approvedPriorScopes.length">Roll forward from an approved scope</option>
                </select></label>
                @if (scopeBasis === 'PRIOR') {
                  <label>Approved prior scope * <select name="priorScope" [(ngModel)]="scopePriorId" required>
                    <option value="">Choose an approved scope with an approved run</option>
                    @for (prior of options.approvedPriorScopes; track prior.id) { <option [value]="prior.id">v{{ prior.version }} · {{ prior.method }} · {{ prior.currency }} · {{ prior.approvedRun }}</option> }
                  </select></label>
                }
                @if (scopeMethod === foreignMethod) {
                  <label>Approved translation policy * <select name="scopePolicy" [(ngModel)]="scopePolicyId" required>
                    <option value="">Choose an approved policy for the reporting currency</option>
                    @for (policy of policiesFor(options); track policy.id) { <option [value]="policy.id">{{ policy.code }} · {{ policy.functionalCurrency }} → {{ policy.presentationCurrency }}</option> }
                  </select></label>
                  <label>Approved exchange-rate set * <select name="scopeRateSet" [(ngModel)]="scopeRateSetId" required>
                    <option value="">Choose an approved rate set</option>
                    @for (rateSet of options.approvedRateSets; track rateSet.id) { <option [value]="rateSet.id">{{ rateSet.code }} v{{ rateSet.version }} · {{ rateSet.effectiveFrom ?? 'any start' }} – {{ rateSet.effectiveTo ?? 'open' }}</option> }
                  </select></label>
                  <label>Translation rate date * <input type="date" name="scopeRateDate" [(ngModel)]="scopeRateDate" required /></label>
                  <label>Translation rate type * <select name="scopeRateType" [(ngModel)]="scopeRateType" required>
                    <option value="">Choose a type permitted by the selected policy</option>
                    @for (rateType of rateTypesFor(options); track rateType) { <option [value]="rateType">{{ rateType }}</option> }
                  </select></label>
                }
                @if (scopeValidation()) { <p role="alert">{{ scopeValidation() }}</p> }
                <button type="submit" [disabled]="scopeCommand.busy() || scopeCommand.uncertain() || !options.canCreateScope || !options.periods.length">
                  {{ scopeCommand.busy() ? 'Creating scope…' : 'Create scope version' }}
                </button>
              </form>
            }
          }
          @if (scopeCommand.uncertain()) {
            <section class="recovery" aria-labelledby="scope-recovery-heading">
              <h3 id="scope-recovery-heading">Scope creation outcome needs review</h3>
              <p role="alert">{{ scopeCommand.message() }} The command will not be sent again automatically.</p>
              <button type="button" (click)="recoverScope()" [disabled]="checkingScopeRecovery()">{{ checkingScopeRecovery() ? 'Checking saved scope versions…' : 'Re-read this group’s saved scope versions' }}</button>
              @if (scopeRecoveryError()) { <p role="alert">{{ scopeRecoveryError() }}</p> }
            </section>
          } @else { <audit-command-message [message]="scopeCommand.message()" [failed]="scopeCommand.failed()" /> }
        </section>
      }
      @if (!g.groups.length) { <section class="panel" role="status"><h2>No group access</h2><p>No explicit accounting-group grant is available for the current identity. A firm-wide group creator can create a group above; the creator receives an explicit grant only to that new group.</p></section> }
      @else {
        @let all = scopes(g.groups);
        <p role="status">{{ g.groups.length }} groups · {{ all.length }} scope versions · {{ approvedPins(all) }} approved component pins</p>
        <nav aria-label="Group consolidation tabs" class="actions"><a [routerLink]="[]" fragment="perimeter">Perimeter</a><a [routerLink]="[]" fragment="packs">Component packs</a><a [routerLink]="[]" fragment="fx">FX</a><a [routerLink]="[]" fragment="advanced">Advanced schedules</a><a [routerLink]="[]" fragment="intercompany">Intercompany</a><a [routerLink]="[]" fragment="eliminations">Eliminations</a></nav>
        <section class="panel" id="perimeter" aria-labelledby="perimeter-heading"><h2 id="perimeter-heading">Perimeter</h2>
          @for (grp of g.groups; track grp.id) {
            <h3 [id]="'group-' + grp.id">{{ grp.name }} <code>{{ grp.code }}</code></h3>
            <p><button type="button" (click)="openGroup(grp.id)" [attr.aria-expanded]="selectedGroupId() === grp.id" aria-controls="group-membership-panel">{{ selectedGroupId() === grp.id ? 'Perimeter open' : 'Manage legal-entity perimeter' }}</button></p>
            <div class="table-scroll"><table><thead><tr><th>Version</th><th>Method</th><th>Currency</th><th>Ownership edges</th><th>Grouped matches</th><th>Outside reviews</th><th>Perimeter</th><th>Latest run</th><th><span class="sr-only">Workflow</span></th></tr></thead>
              <tbody>@for (s of grp.scopes; track s.id) { <tr><td>v{{ s.version }}</td><td><code>{{ s.method }}</code></td><td>{{ s.currency }}</td><td>{{ s.ownershipEdges }}</td><td>{{ s.groupedMatches }}</td><td>{{ s.outsideReviews }}</td>
                <td><audit-status [value]="s.status" /></td><td><audit-status [value]="s.runStatus" /></td>
                <td>
                  <a [routerLink]="['/app/consolidation/scopes', s.id]">Workspace</a>
                  @if (s.isAdvanced) { · <a [routerLink]="['/app/consolidation/advanced', s.id]">Advanced</a> }
                </td></tr> }
              @empty { <tr><td colspan="9">No consolidation perimeter versions are available.</td></tr> }</tbody></table></div>
          }</section>
        <section class="panel" id="packs" aria-labelledby="packs-heading"><h2 id="packs-heading">Component packs</h2>
          <p><small>Counts show what is persisted for each approved scope. Consolidation consumes only approved compatible packs; missing packs are not treated as zero.</small></p>
          <div class="table-scroll"><table><thead><tr><th>Group / scope</th><th>Components</th><th>Approved components</th><th>External packs</th><th>Approved packs</th></tr></thead>
            <tbody>@for (s of all; track s.id) { <tr><td><a [routerLink]="['/app/consolidation/scopes', s.id]">{{ s.groupName }} · v{{ s.version }}</a></td><td>{{ s.componentCount }}</td><td>{{ s.approvedComponentCount }}</td><td>{{ s.externalPackCount }}</td><td>{{ s.approvedExternalPackCount }}</td></tr> }</tbody></table></div></section>
        <section class="panel" id="fx" aria-labelledby="fx-heading"><h2 id="fx-heading">FX</h2>
          <p><small>The enabled first profile is same-currency. Foreign-operation translation is shown only when the scope pins both a rate set and a translation policy; no rate of 1 is inferred.</small></p>
          <div class="table-scroll"><table><thead><tr><th>Group / scope</th><th>Reporting currency</th><th>Rate set</th><th>Translation policy</th></tr></thead>
            <tbody>@for (s of all; track s.id) { <tr><td><a [routerLink]="['/app/consolidation/scopes', s.id]">{{ s.groupName }} · v{{ s.version }}</a></td><td><code>{{ s.currency }}</code></td><td><audit-status [value]="s.rateSetStatus" /></td><td><audit-status [value]="s.translationPolicyStatus" /></td></tr> }</tbody></table></div></section>
        <section class="panel" id="advanced" aria-labelledby="advanced-heading"><h2 id="advanced-heading">Advanced schedules</h2>
          <p><small>Advanced-method schedules are source-bound, maker/checker-reviewed inputs. A complete approved schedule is necessary but does not enable an advanced profile until balanced current/comparative statement evidence is verified and separately approved.</small></p>
          <div class="table-scroll"><table><thead><tr><th>Group / scope</th><th>Method</th><th>Submitted</th><th>Approved</th><th>Executions</th><th>Readiness</th></tr></thead>
            <tbody>@for (s of all; track s.id) { <tr><td><a [routerLink]="['/app/consolidation/scopes', s.id]">{{ s.groupName }} · v{{ s.version }}</a></td><td><code>{{ s.method }}</code></td><td>{{ s.advancedScheduleCount }}</td><td>{{ s.approvedAdvancedScheduleCount }}</td>
              <td>{{ s.advancedExecutionCount }} / {{ s.approvedAdvancedExecutionCount }}</td><td [title]="s.advancedScheduleGuidance"><audit-status [value]="s.advancedScheduleStatus" /></td></tr> }</tbody></table></div></section>
        <section class="panel" id="intercompany" aria-labelledby="intercompany-heading"><h2 id="intercompany-heading">Intercompany</h2>
          <p><small>Grouped matches, outside-perimeter reviews and unresolved match rows remain visible before any automatic elimination.</small></p>
          <div class="table-scroll"><table><thead><tr><th>Group / scope</th><th>Grouped matches</th><th>Outside reviews</th><th>Open matches</th></tr></thead>
            <tbody>@for (s of all; track s.id) { <tr><td><a [routerLink]="['/app/consolidation/scopes', s.id]">{{ s.groupName }} · v{{ s.version }}</a></td><td>{{ s.groupedMatches }}</td><td>{{ s.outsideReviews }}</td><td>{{ s.openMatchCount }}</td></tr> }</tbody></table></div></section>
        <section class="panel" id="eliminations" aria-labelledby="eliminations-heading"><h2 id="eliminations-heading">Eliminations</h2>
          <p><small>Persisted consolidation journals are separate from component books. Unsupported elimination natures fail closed in the calculation service.</small></p>
          <div class="table-scroll"><table><thead><tr><th>Group / scope</th><th>Journals</th><th>Approved journals</th><th>Latest run</th></tr></thead>
            <tbody>@for (s of all; track s.id) { <tr><td><a [routerLink]="['/app/consolidation/scopes', s.id]">{{ s.groupName }} · v{{ s.version }}</a></td><td>{{ s.eliminationJournalCount }}</td><td>{{ s.approvedEliminationJournalCount }}</td><td><audit-status [value]="s.runStatus" /></td></tr> }</tbody></table></div></section>
        <section class="panel" id="group-report" aria-labelledby="group-report-heading"><h2 id="group-report-heading">Approved group reports</h2>
          <p><small>Only the latest approved report with inputs that still match its manifest is shown. This group-level summary does not grant access to component workpapers.</small></p>
          @for (s of all; track s.id) {
            <h3>{{ s.groupName }} · v{{ s.version }}</h3>
            @if (s.isAdvanced) { <p>Open the method workflow for its separately reviewed statement execution. <a [routerLink]="['/app/consolidation/advanced', s.id]">Open advanced workflow</a></p> }
            @else if (s.report?.state === 'CURRENT_APPROVED') {
              <p>Run {{ s.report!.runId }} · approved {{ formatUtcInstant(s.report!.approvedAt) }}</p>
              <div class="table-scroll"><table><caption class="sr-only">Current approved group report for {{ s.groupName }} version {{ s.version }}</caption>
                <thead><tr><th>Component / adjustment</th><th>Reporting code</th><th class="number">Component</th><th class="number">Alignment</th><th class="number">Elimination</th><th class="number">Consolidated</th><th>Currency</th></tr></thead>
                <tbody>@for (l of s.report!.lines; track $index) { <tr><td>{{ l.component }}</td><td><code>{{ l.taxonomyCode }}</code></td><td class="number">{{ l.componentAmount | money }}</td><td class="number">{{ l.alignmentAmount | money }}</td>
                  <td class="number">{{ l.eliminationAmount | money }}</td><td class="number">{{ l.consolidatedAmount | money }}</td><td>{{ l.currency }}</td></tr> }</tbody>
                <tfoot><tr><th colspan="5">Net total</th><td class="number">{{ net(s.report!.lines) | money }}</td><td>{{ s.currency }}</td></tr></tfoot></table></div>
            } @else if (s.report?.state === 'STALE') { <p role="status">The latest approval is historical: a current component, perimeter, rate, match or group journal no longer matches the run. The result is withheld; rebuild and review the group run.</p> }
            @else { <p>No current approved group report is available. Latest run state: {{ s.runStatus }}.</p> }
          }</section>
      }
      @if (selectedGroup(); as selected) {
        <p><button type="button" (click)="closeGroup()">Close {{ selected.name }} perimeter</button></p>
        <audit-consolidation-group-membership [groupId]="selected.id" />
      }
    }
  `,
})
export class ConsolidationOverview {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly groups = this.api.resource(() => '/api/ui/consolidation', decodeConsolidation, 'No explicit accounting-group grant is available for the current identity.');
  private readonly correctionScopeParam = queryParam('correctionScopeId');
  private readonly correctionGroupParam = queryParam('correctionGroupId');
  readonly correctionContext = computed(() => resolveCorrectionContext(
    this.groups.data()?.groups ?? null, this.correctionScopeParam(), this.correctionGroupParam()));
  readonly createCommand = new CommandState(this.api);
  readonly scopeCommand = new CommandState(this.api);
  readonly selectedGroupId = signal('');
  private readonly chosenScopeGroupId = signal('');
  readonly scopeGroupId = computed(() => {
    const chosen = this.chosenScopeGroupId();
    if (chosen) return chosen;
    const correction = this.correctionContext();
    return correction.state === 'valid' ? correction.group.id : '';
  });
  readonly scopeOptions = this.api.resource(() => this.scopeGroupId() ? `/api/ui/consolidation/groups/${this.scopeGroupId()}/scope-options` : null,
    decodeScopeOptions, 'Scope options are unavailable for this group.');
  readonly checkingCreateRecovery = signal(false);
  readonly checkingScopeRecovery = signal(false);
  readonly createRecoveryError = signal('');
  readonly scopeRecoveryError = signal('');
  readonly createValidation = signal('');
  readonly scopeValidation = signal('');
  readonly pendingGroup = signal<{ code: string; name: string } | null>(null);
  readonly pendingScope = signal<ScopeIntent | null>(null);
  readonly selectedGroup = computed(() => this.groups.data()?.groups.find(x => x.id === this.selectedGroupId()) ?? null);
  groupCode = '';
  groupName = '';
  scopePeriodId = '';
  scopeMethod = '';
  scopeCurrency = '';
  scopeBasis: 'INITIAL' | 'PRIOR' = 'INITIAL';
  scopePriorId = '';
  scopePolicyId = '';
  scopeRateSetId = '';
  scopeRateDate = '';
  scopeRateType = '';
  readonly foreignMethod = 'CONTROLLED_FOREIGN_OPERATION_TRANSLATION_V1';
  scopes(g: Group[]) { return g.flatMap((x) => x.scopes); }
  approvedPins(s: { approvedComponentCount: number }[]): number { return s.reduce((n, x) => n + x.approvedComponentCount, 0); }
  formatUtcInstant(value: string | null): string { return displayUtcInstant(value); }
  net(lines: { consolidatedAmount: string }[]): string { return sumDecimals(lines.map((l) => l.consolidatedAmount)); }

  selectScopeGroup(groupId: string): void {
    this.chosenScopeGroupId.set(groupId);
    this.scopePeriodId = '';
    this.scopeMethod = '';
    this.scopeCurrency = '';
    this.scopePriorId = '';
    this.scopePolicyId = '';
    this.scopeRateSetId = '';
    this.scopeRateDate = '';
    this.scopeRateType = '';
    this.scopeBasis = 'INITIAL';
    this.scopeValidation.set('');
  }

  selectedMethod(options: ScopeOptions): ScopeOptions['methods'][number] | null {
    return options.methods.find(x => x.code === this.scopeMethod) ?? null;
  }

  currenciesFor(options: ScopeOptions): string[] {
    const period = options.periods.find(x => x.id === this.scopePeriodId);
    if (this.scopeMethod !== this.foreignMethod) return period ? [period.currency] : [];
    return options.approvedTranslationPolicies
      .map(x => x.presentationCurrency).filter((value, index, all) => all.indexOf(value) === index);
  }

  policiesFor(options: ScopeOptions): ScopeOptions['approvedTranslationPolicies'] {
    return options.approvedTranslationPolicies.filter(x => x.presentationCurrency === this.scopeCurrency);
  }

  rateTypesFor(options: ScopeOptions): string[] {
    const policy = this.policiesFor(options).find(x => x.id === this.scopePolicyId);
    return policy ? [policy.closingRateType, policy.averageRateType, policy.historicalRateType]
      .filter((value, index, all) => !!value && all.indexOf(value) === index) : [];
  }

  async createScope(): Promise<void> {
    this.scopeValidation.set('');
    const options = this.scopeOptions.data();
    if (!options || !options.canCreateScope || this.scopeCommand.busy() || this.scopeCommand.uncertain()) return;
    const correction = this.correctionContext();
    if (correction.state !== 'none' && correction.state !== 'valid') {
      this.scopeValidation.set('The correction source is not verified in the current authorized group list. Reopen the handoff from its component workspace before creating a correction scope.');
      return;
    }
    if (options.groupId.toLowerCase() !== this.scopeGroupId().toLowerCase() ||
        correction.state === 'valid' && correction.group.id.toLowerCase() !== options.groupId.toLowerCase()) {
      this.scopeValidation.set('The current scope options do not match the authorized correction group. Refresh the source workspace and review the group before continuing.');
      return;
    }
    const period = options.periods.find(x => x.id === this.scopePeriodId);
    if (correction.state === 'valid') {
      const source = options.existingScopes.find(x => x.id.toLowerCase() === correction.scope.id.toLowerCase());
      if (!source || source.periodId !== this.scopePeriodId) {
        this.scopeValidation.set('A component correction must retain the original reporting period. Load the current source scope and select that same period before creating its new version.');
        return;
      }
    }
    if (!period || !options.methods.some(x => x.code === this.scopeMethod && x.enabled) ||
        !this.currenciesFor(options).includes(this.scopeCurrency)) {
      this.scopeValidation.set('Choose a permitted period, enabled method and currency from the current group options.');
      return;
    }
    const isForeign = this.scopeMethod === this.foreignMethod;
    const priorId = this.scopeBasis === 'PRIOR' ? this.scopePriorId : null;
    if (this.scopeBasis === 'PRIOR' && !options.approvedPriorScopes.some(x => x.id === priorId)) {
      this.scopeValidation.set('Choose an approved prior scope with an approved run, or select an initial consolidation.');
      return;
    }
    if (isForeign && (!this.policiesFor(options).some(x => x.id === this.scopePolicyId) ||
        !options.approvedRateSets.some(x => x.id === this.scopeRateSetId) || !this.scopeRateDate ||
        !this.rateTypesFor(options).includes(this.scopeRateType))) {
      this.scopeValidation.set('Choose an approved matching policy, rate set, rate date and policy-approved rate type.');
      return;
    }
    const intent: ScopeIntent = {
      groupId: options.groupId, groupRevision: options.groupRevision, periodId: period.id,
      reportingCurrency: this.scopeCurrency, method: this.scopeMethod,
      openingBasis: priorId ? 'APPROVED_PRIOR_SCOPE' : 'INITIAL_CONSOLIDATION',
      exchangeRateSetVersionId: isForeign ? this.scopeRateSetId : null,
      translationPolicyVersionId: isForeign ? this.scopePolicyId : null,
      translationRateDate: isForeign ? this.scopeRateDate : null,
      translationRateType: isForeign ? this.scopeRateType : null,
      priorScopeVersionId: priorId
    };
    this.pendingScope.set(intent);
    const accepted = await this.scopeCommand.run<string>('/api/ui/consolidation/scopes', {
      groupId: intent.groupId, periodId: intent.periodId, reportingCurrency: intent.reportingCurrency,
      method: intent.method, openingBasis: intent.openingBasis,
      exchangeRateSetVersionId: intent.exchangeRateSetVersionId,
      translationPolicyVersionId: intent.translationPolicyVersionId,
      translationRateDate: intent.translationRateDate, translationRateType: intent.translationRateType,
      priorScopeVersionId: intent.priorScopeVersionId, expectedGroupRevision: intent.groupRevision
    }, 'Consolidation scope created.', id => {
      if (guidPattern.test(id)) void this.router.navigate(['/app/consolidation/scopes', id.toLowerCase()]);
    });
    if (accepted) this.pendingScope.set(null);
    else if (!this.scopeCommand.uncertain()) {
      this.pendingScope.set(null);
      if (this.scopeCommand.code() === 'generation.stale') {
        this.scopeOptions.reload();
        this.scopeValidation.set('The group basis changed. Current options have been reloaded; review them before starting a new scope.');
      }
    }
  }

  async recoverScope(): Promise<void> {
    const intent = this.pendingScope();
    if (!intent || !this.scopeCommand.uncertain() || this.checkingScopeRecovery()) return;
    this.checkingScopeRecovery.set(true);
    this.scopeRecoveryError.set('');
    try {
      const saved = await this.api.get(`/api/ui/consolidation/groups/${intent.groupId}/scope-options`, decodeScopeOptions);
      const matches = saved.existingScopes.filter(x => x.periodId === intent.periodId);
      const exact = saved.existingScopes.filter(x => x.periodId === intent.periodId &&
        x.reportingCurrency === intent.reportingCurrency && x.method === intent.method &&
        x.openingBasis === intent.openingBasis && x.exchangeRateSetVersionId === intent.exchangeRateSetVersionId &&
        x.translationPolicyVersionId === intent.translationPolicyVersionId && x.translationRateDate === intent.translationRateDate &&
        x.translationRateType === (intent.translationRateType ?? '') && x.priorScopeVersionId === intent.priorScopeVersionId &&
        x.groupRevision === intent.groupRevision);
      if (exact.length === 1) {
        this.scopeCommand.uncertain.set(false);
        this.scopeCommand.failed.set(false);
        this.scopeCommand.message.set('The exact scope version is present in the authorized group record. It was not created again.');
        this.pendingScope.set(null);
        await this.router.navigate(['/app/consolidation/scopes', exact[0].id]);
      } else {
        const summary = matches.length ? 'A scope with this period is visible, but its pinned inputs do not match the saved request.' : 'No matching scope is visible yet.';
        this.scopeRecoveryError.set(`${summary} The outcome remains unknown; do not resubmit. Refresh the authorized group options and resolve the result before starting another scope.`);
      }
    } catch (error) {
      this.scopeRecoveryError.set(error instanceof Error ? error.message : 'The authorized scope list could not be checked. The command remains fenced.');
    } finally {
      this.checkingScopeRecovery.set(false);
    }
  }

  async createGroup(): Promise<void> {
    this.createValidation.set('');
    if (this.createCommand.busy() || this.createCommand.uncertain() || !this.groups.data()?.canCreateGroup) return;
    const intent = { code: this.groupCode.trim(), name: this.groupName.trim() };
    if (!intent.code || intent.code.length > 100 || !intent.name || intent.name.length > 300) {
      this.createValidation.set('Enter a group code (up to 100 characters) and group name (up to 300 characters).');
      return;
    }
    this.pendingGroup.set(intent);
    const accepted = await this.createCommand.run<string>('/api/ui/consolidation/groups', intent, 'Accounting group created.', value => {
      if (guidPattern.test(value)) this.selectedGroupId.set(value.toLowerCase());
    });
    if (accepted) {
      this.groups.reload();
      if (this.selectedGroupId()) await this.router.navigate([], { fragment: `group-${this.selectedGroupId()}` });
      this.pendingGroup.set(null);
    } else if (!this.createCommand.uncertain()) {
      this.pendingGroup.set(null);
    }
  }

  async recoverGroup(): Promise<void> {
    const intent = this.pendingGroup();
    if (!intent || !this.createCommand.uncertain() || this.checkingCreateRecovery()) return;
    this.checkingCreateRecovery.set(true);
    this.createRecoveryError.set('');
    try {
      const saved = await this.api.get('/api/ui/consolidation', decodeConsolidation);
      const matches = saved.groups.filter(x => x.code === intent.code && x.name === intent.name);
      if (matches.length === 1) {
        const group = matches[0];
        this.selectedGroupId.set(group.id);
        this.createCommand.uncertain.set(false);
        this.createCommand.failed.set(false);
        this.createCommand.message.set('The exact accounting group is present in the permitted group list. It was not created again.');
        this.pendingGroup.set(null);
        this.groups.reload();
        await this.router.navigate([], { fragment: `group-${group.id}` });
      } else {
        this.createRecoveryError.set('No single exact code-and-name match is visible yet. The outcome remains unknown; do not submit this request again. Refresh the permitted group list before taking further action.');
      }
    } catch (error) {
      this.createRecoveryError.set(error instanceof Error ? error.message : 'The permitted group list could not be checked. The command remains fenced.');
    } finally {
      this.checkingCreateRecovery.set(false);
    }
  }

  async openGroup(groupId: string): Promise<void> {
    this.selectedGroupId.set(groupId);
    await this.router.navigate([], { fragment: `group-${groupId}` });
  }

  async closeGroup(): Promise<void> {
    this.selectedGroupId.set('');
    await this.router.navigate([], { fragment: 'perimeter' });
  }
}
