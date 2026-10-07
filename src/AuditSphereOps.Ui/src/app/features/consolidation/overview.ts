import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { arr, bool, dec, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const report = obj({ scopeVersionId: guid, runId: nullable(guid), state: text, approvedAt: nullable(instant),
  lines: arr(obj({ component: text, taxonomyCode: text, componentAmount: dec, alignmentAmount: dec, eliminationAmount: dec, consolidatedAmount: dec, currency: text }), 20000) });
const scope = obj({ id: guid, groupName: text, version: int, method: text, currency: text, ownershipEdges: nat, groupedMatches: nat, outsideReviews: nat, openMatchCount: nat,
  componentCount: nat, approvedComponentCount: nat, externalPackCount: nat, approvedExternalPackCount: nat, advancedScheduleCount: nat, approvedAdvancedScheduleCount: nat,
  advancedExecutionCount: nat, approvedAdvancedExecutionCount: nat, advancedScheduleStatus: text, advancedScheduleGuidance: text, rateSetStatus: text,
  translationPolicyStatus: text, eliminationJournalCount: nat, approvedEliminationJournalCount: nat, status: text, runStatus: text, isAdvanced: bool, report: nullable(report) });
export const decodeConsolidation = arr(obj({ id: guid, code: text, name: text, scopes: arr(scope, 1000) }), 500);
type Group = ReturnType<typeof decodeConsolidation>[number];

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
  imports: [RouterLink, ...SHARED],
  template: `
    <nav aria-label="Consolidation location"><a routerLink="/app/accounting">Accounting</a> / <span>Group consolidation</span></nav>
    <audit-page-header title="Group consolidation" eyebrow="Group accounting" description="Approved group perimeters and component packs. Consolidation never mutates component client books." />
    <p><small>Only explicitly granted group scopes appear here. The enabled first profile is same-currency, fully-owned consolidation; configured foreign-operation translation is shown separately, while advanced methods remain fail-closed until approved source-bound schedules and execution verification are complete.</small></p>
    <audit-state [loading]="groups.loading()" [error]="groups.error()" label="approved group scopes" />
    @if (groups.data(); as g) {
      @if (!g.length) { <section class="panel" role="status"><h2>No group access</h2><p>No explicit group access grant is available for the current identity.</p></section> }
      @else {
        @let all = scopes(g);
        <p role="status">{{ g.length }} groups · {{ all.length }} scope versions · {{ approvedPins(all) }} approved component pins</p>
        <nav aria-label="Group consolidation tabs" class="actions"><a [routerLink]="[]" fragment="perimeter">Perimeter</a><a [routerLink]="[]" fragment="packs">Component packs</a><a [routerLink]="[]" fragment="fx">FX</a><a [routerLink]="[]" fragment="advanced">Advanced schedules</a><a [routerLink]="[]" fragment="intercompany">Intercompany</a><a [routerLink]="[]" fragment="eliminations">Eliminations</a></nav>
        <section class="panel" id="perimeter" aria-labelledby="perimeter-heading"><h2 id="perimeter-heading">Perimeter</h2>
          @for (grp of g; track grp.id) {
            <h3 [id]="'group-' + grp.id">{{ grp.name }} <code>{{ grp.code }}</code></h3>
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
    }
  `,
})
export class ConsolidationOverview {
  private readonly api = inject(Api);
  readonly groups = this.api.resource(() => '/api/ui/consolidation', decodeConsolidation, 'No explicit group access grant is available for the current identity.');
  scopes(g: Group[]) { return g.flatMap((x) => x.scopes); }
  approvedPins(s: { approvedComponentCount: number }[]): number { return s.reduce((n, x) => n + x.approvedComponentCount, 0); }
  formatUtcInstant(value: string | null): string { return displayUtcInstant(value); }
  net(lines: { consolidatedAmount: string }[]): string { return sumDecimals(lines.map((l) => l.consolidatedAmount)); }
}
