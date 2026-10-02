import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, routeGuid } from '../../core/api';
import { arr, bool, dec, guid, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const line = obj({ destinationCode: text, statementSection: text, auditArea: text, amount: dec, sourceAccountCount: nat, sourceAccounts: arr(text),
  procedures: arr(obj({ procedureId: guid, sourceProcedureId: text, title: text, status: text, section: nullable(text) }), 1000) });
const statement = obj({ title: text, lines: arr(line, 2000), total: dec, totalLabel: text });
export const decodeStatements = obj({ mappingVersionId: guid, mappingVersion: nat, datasetDigest: text, currency: text,
  profitOrLoss: statement, financialPosition: statement, balances: bool });

@Component({
  selector: 'audit-statement-drilldown',
  imports: [RouterLink, ...SHARED],
  template: `
    <audit-page-header title="Financial statements" eyebrow="Engagement"
      description="Profit or loss and financial position generated from the current approved mapping over the sealed trial balance. Select a line to see its audit procedures." />
    <a [routerLink]="['/app/engagements', id()]">← Back to engagement</a>
    <audit-state [loading]="view.loading()" [error]="view.error()" label="statements" />
    @if (view.data(); as v) {
      <p>Mapping version {{ v.mappingVersion }} over trial balance <code>{{ v.datasetDigest.slice(0, 12) }}</code>, {{ v.currency }}.
        {{ v.balances ? 'The statements agree.' : 'The statements do not agree; review the mapping.' }}</p>
      @for (s of [v.profitOrLoss, v.financialPosition]; track s.title) {
        <section class="panel" [attr.aria-label]="s.title">
          <h2>{{ s.title }}</h2>
          <div class="table-scroll"><table>
            <thead><tr><th scope="col">Line</th><th scope="col">Section</th><th scope="col" class="number">Amount</th><th scope="col">Audit procedures</th></tr></thead>
            <tbody>
              @for (l of s.lines; track l.destinationCode) {
                <tr><th scope="row"><button type="button" class="linklike" [attr.aria-expanded]="open() === s.title + l.destinationCode" (click)="toggle(s.title + l.destinationCode)">{{ l.destinationCode }}</button></th>
                  <td>{{ l.statementSection }}</td><td class="number">{{ l.amount | money }}</td><td>{{ l.procedures.length }}</td></tr>
                @if (open() === s.title + l.destinationCode) {
                  <tr><td colspan="4">
                    <p>Audit area <strong>{{ l.auditArea }}</strong>; source accounts {{ l.sourceAccounts.join(', ') }}.</p>
                    @if (!l.procedures.length) { <p>No audit procedures cover this area yet. <a [routerLink]="['/app/engagements', id(), 'audit-fieldwork']">Insert an ad hoc step</a>.</p> }
                    @else { <ul [attr.aria-label]="'Procedures for ' + l.destinationCode">@for (p of l.procedures; track p.procedureId) {
                      <li><a [routerLink]="['/app/engagements', id(), 'audit-fieldwork']" [fragment]="'procedure-' + p.procedureId">{{ p.sourceProcedureId }} · {{ p.title }}</a> <audit-status [value]="p.status" /></li> }</ul> }
                  </td></tr>
                }
              }
            </tbody>
            <tfoot><tr><th scope="row">{{ s.totalLabel }}</th><td></td><td class="number"><strong>{{ s.total | money }}</strong></td><td></td></tr></tfoot>
          </table></div>
        </section>
      }
    }
  `,
})
export class StatementDrillDown {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly view = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/statements` : null), decodeStatements,
    'Statements are unavailable: an approved mapping over a sealed trial balance and an engagement assignment are required.');
  readonly open = signal<string | null>(null);
  toggle(key: string): void { this.open.update((o) => (o === key ? null : key)); }
}
