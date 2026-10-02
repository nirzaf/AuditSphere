import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { arr, dec, guid, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeMapping = obj({ id: guid, status: text, datasetId: guid, chartLabel: text, taxonomyVersion: text, taxonomyScope: text, periodStart: text, periodEnd: text,
  version: nat, generation: nat, sourceAccountCount: nat,
  unmapped: arr(obj({ accountCode: text, accountName: text, amount: dec, currency: text }), 20000),
  suggestions: arr(obj({ accountCode: text, accountName: text, candidates: arr(obj({ code: text, name: text }), 5), state: text }), 20000),
  splits: arr(obj({ accountCode: text, destinationCount: nat, totalFraction: dec }), 20000),
  impacts: arr(obj({ destinationCode: text, statementSection: text, amount: dec, sourceCount: nat }), 20000),
  priorMappingId: nullable(guid), priorVersion: nullable(nat), priorStatus: nullable(text), priorAllocationCount: nat,
  comparison: arr(obj({ sourceAccountCode: text, prior: text, current: text, status: text }), 20000),
  allocations: arr(obj({ sourceAccountCode: text, destinationCode: text, statementSection: text, fraction: dec, rationale: text }), 50000) });

@Component({
  selector: 'audit-mapping-workbench',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting/mappings">← COA and mappings</a>
    <audit-page-header title="Accounting mapping" eyebrow="Accounting record" description="Client chart and reporting taxonomy mappings, lineage, validation, and report impact." />
    <button matButton="outlined" (click)="mapping.reload()">Refresh mapping</button>
    <audit-state [loading]="mapping.loading()" [error]="mapping.error()" label="the scoped mapping" />
    @if (mapping.data(); as m) {
      <section class="panel" aria-labelledby="mapping-heading">
        <p class="eyebrow"><audit-status [value]="m.status" /></p>
        <h2 id="mapping-heading"><code>{{ m.id }}</code></h2>
        <dl class="facts"><dt>Dataset</dt><dd><code>{{ m.datasetId }}</code></dd><dt>Client chart</dt><dd>{{ m.chartLabel }}</dd><dt>Taxonomy</dt><dd>{{ m.taxonomyVersion }}</dd>
          <dt>Taxonomy scope</dt><dd>{{ m.taxonomyScope }}</dd><dt>Period</dt><dd>{{ m.periodStart }} to {{ m.periodEnd }}</dd><dt>Version</dt><dd>{{ m.version }}</dd><dt>Generation</dt><dd>{{ m.generation }}</dd></dl>
      </section>
      <p role="status">{{ m.sourceAccountCount }} source accounts · {{ m.unmapped.length }} unmapped · {{ m.splits.length }} split · {{ m.impacts.length }} report destinations</p>
      <nav aria-label="Mapping sections" class="actions"><a [routerLink]="[]" fragment="lineage-heading">Lineage</a><a [routerLink]="[]" fragment="comparison-heading">Comparison</a><a [routerLink]="[]" fragment="unmapped-heading">Unmapped</a><a [routerLink]="[]" fragment="impact-heading">Report impact</a><a [routerLink]="[]" fragment="allocation-heading">Allocations</a></nav>
      <section class="panel" aria-labelledby="lineage-heading">
        <h2 id="lineage-heading">Mapping lineage</h2>
        <p>Mappings are immutable versions. A new source dataset, chart or taxonomy requires a new applicability decision.</p>
        @if (m.priorMappingId) { <p>Prior version: <a [routerLink]="['/app/accounting/mappings', m.priorMappingId]">v{{ m.priorVersion }}</a> <small>({{ m.priorStatus }}, {{ m.priorAllocationCount }} allocations)</small></p> }
        @else { <p>This is the first mapping version in the current engagement.</p> }
        <p><strong>{{ m.status === 'APPROVED' ? 'RETAINED APPROVAL: inspect current applicability before building a package' : 'APPLICABILITY PENDING: independent mapping approval required' }}</strong></p>
      </section>
      <section class="panel" aria-labelledby="comparison-heading">
        <h2 id="comparison-heading">Current and prior allocation comparison</h2>
        <p>This comparison is read-only. A changed source, chart or taxonomy must be reviewed in a new mapping version; prior allocations are never silently reused.</p>
        <div class="table-scroll"><table><thead><tr><th scope="col">Source account</th><th scope="col">Prior allocation</th><th scope="col">Current allocation</th><th scope="col">Change</th></tr></thead>
          <tbody>@for (c of m.comparison; track c.sourceAccountCode) { <tr><td><code>{{ c.sourceAccountCode }}</code></td><td>{{ c.prior }}</td><td>{{ c.current }}</td><td><audit-status [value]="c.status" /></td></tr> }
          @empty { <tr><td colspan="4">No prior allocation comparison is available.</td></tr> }</tbody></table></div>
      </section>
      <section class="panel" aria-labelledby="unmapped-heading">
        <h2 id="unmapped-heading">Unmapped source accounts</h2>
        @if (!m.unmapped.length) { <p role="status">Every non-zero source account has an allocation.</p> }
        @else {
          <p>These accounts must be resolved in a new mapping version before the package can be built.</p>
          <div class="table-scroll"><table><thead><tr><th scope="col">Account</th><th scope="col">Name</th><th scope="col" class="number">Balance</th><th scope="col">Currency</th></tr></thead>
            <tbody>@for (u of m.unmapped; track u.accountCode) { <tr><td><code>{{ u.accountCode }}</code></td><td>{{ u.accountName }}</td><td class="number">{{ u.amount | money }}</td><td>{{ u.currency }}</td></tr> }</tbody></table></div>
        }
      </section>
      <section class="panel" aria-labelledby="suggestions-heading">
        <h2 id="suggestions-heading">Review-only mapping suggestions</h2>
        <p>Suggestions use account-name tokens only. They are not allocations and must be independently reviewed before anyone creates a new mapping version.</p>
        <div class="table-scroll"><table><thead><tr><th scope="col">Account</th><th scope="col">Candidate destinations</th><th scope="col">Review state</th></tr></thead>
          <tbody>@for (s of m.suggestions; track s.accountCode) { <tr><td><code>{{ s.accountCode }}</code><small>{{ s.accountName }}</small></td><td>{{ candidates(s.candidates) }}</td><td>{{ s.state }}</td></tr> }
          @empty { <tr><td colspan="3">No review-only candidates are available for the unresolved accounts.</td></tr> }</tbody></table></div>
      </section>
      <section class="panel" aria-labelledby="split-heading">
        <h2 id="split-heading">Split and ambiguous allocations</h2>
        @if (m.splits.length) {
          <p>Splits are deliberate allocations and must total 100%; review the rationale before approval.</p>
          <div class="table-scroll"><table><thead><tr><th scope="col">Account</th><th scope="col">Destinations</th><th scope="col">Total fraction</th></tr></thead>
            <tbody>@for (s of m.splits; track s.accountCode) { <tr><td><code>{{ s.accountCode }}</code></td><td>{{ s.destinationCount }}</td><td>{{ s.totalFraction | percent }}</td></tr> }</tbody></table></div>
        } @else { <p>No source account is split across multiple destinations.</p> }
        <p><small>Automatic suggestions are not treated as approved mappings; unresolved suggestions remain visible as unmapped accounts.</small></p>
      </section>
      <section class="panel" aria-labelledby="impact-heading">
        <h2 id="impact-heading">Report impact</h2>
        <div class="table-scroll"><table><thead><tr><th scope="col">Destination</th><th scope="col">Statement section</th><th scope="col">Source accounts</th><th scope="col" class="number">Amount</th></tr></thead>
          <tbody>@for (i of m.impacts; track i.destinationCode + i.statementSection) { <tr><td>{{ i.destinationCode }}</td><td>{{ i.statementSection }}</td><td>{{ i.sourceCount }}</td><td class="number">{{ i.amount | money }}</td></tr> }</tbody></table></div>
      </section>
      <section class="panel" aria-labelledby="allocation-heading">
        <h2 id="allocation-heading">Allocations</h2>
        <div class="table-scroll"><table><thead><tr><th scope="col">Source account</th><th scope="col">Destination</th><th scope="col">Statement section</th><th scope="col">Fraction</th><th scope="col">Rationale</th></tr></thead>
          <tbody>@for (a of m.allocations; track $index) { <tr><td><code>{{ a.sourceAccountCode }}</code></td><td>{{ a.destinationCode }}</td><td>{{ a.statementSection }}</td><td>{{ a.fraction | percent }}</td><td>{{ a.rationale }}</td></tr> }</tbody></table></div>
      </section>
      @if (m.status === 'DRAFT') {
        <section class="panel" aria-labelledby="approve-heading">
          <h2 id="approve-heading">Approve mapping</h2>
          <p>Open the independent review of the accepted dataset, full allocation, applicability and current firm/client generation.</p>
          <a matButton="filled" [routerLink]="['/app/accounting/mappings', m.id, 'approval']">Review mapping approval</a>
        </section>
      } @else { <section class="panel" role="status"><h2>Reviewed mapping</h2><p>This immutable version retains its historical approval. Current source, chart, taxonomy and generation gates still apply to a package build.</p><a matButton="outlined" [routerLink]="['/app/accounting/mappings',m.id,'approval']">Inspect current mapping applicability</a></section> }
    }
  `,
})
export class MappingWorkbench {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly mapping = this.api.resource(() => (this.id() ? `/api/ui/accounting/mappings/${this.id()}` : null), decodeMapping,
    'The mapping is not available in the current firm scope.');
  candidates(c: { code: string; name: string }[]): string { return c.map((x) => `${x.code} (${x.name})`).join(', '); }
}
