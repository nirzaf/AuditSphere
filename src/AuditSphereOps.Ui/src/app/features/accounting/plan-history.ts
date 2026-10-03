import { Component, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SHARED } from '../../core/ui';
import { decodePlanHistory } from './plan-command-contracts';

@Component({ selector: 'audit-plan-history', imports: [MatButtonModule, ...SHARED], template: `
  <section class="panel" aria-label="Retained native plan command history"><h2>Retained command history</h2>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="retained native plan commands" />
    @if (ws.data(); as h) {
      <div class="table-scroll" tabindex="0" role="region" aria-label="Plan command history scroll area"><table>
        <caption>Immutable native creation and calculation evidence</caption><thead><tr><th>Command</th><th>Actor</th><th>Rationale / evidence</th><th>Retained result</th><th>Timestamp</th></tr></thead>
        <tbody>@for (r of h.items; track r.id) { <tr><td>{{ r.action }}<br /><code>{{ r.requestId }}</code></td>
          <td><code>{{ r.actorId }}</code></td><td>{{ r.reason }}<br />{{ r.evidenceReference }}</td>
          <td>{{ r.status }} @if (r.resultHash) { <p>{{ r.debits | money:6 }} debits · {{ r.credits | money:6 }} credits · {{ r.appliedCount }} applied revisions</p><code>{{ r.resultHash }}</code> }</td>
          <td>{{ r.createdAt }} UTC</td></tr> } @empty { <tr><td colspan="5">No native plan command events are retained for this plan.</td></tr> }</tbody></table></div>
      <nav aria-label="Plan command history pages" class="actions"><button matButton (click)="page.set(h.page-1)" [disabled]="h.page===0 || ws.loading()">Previous plan commands</button>
        <span>Page {{ h.page+1 }}</span><button matButton (click)="page.set(h.page+1)" [disabled]="!h.hasMore || ws.loading()">Next plan commands</button></nav>
    }
  </section>` })
export class PlanHistory {
  readonly planId = input.required<string>();
  readonly page = signal(0);
  readonly ws = inject(Api).resource(() => '/api/ui/accounting/adjustment-plans/' + this.planId() + '/history?page=' + this.page(), decodePlanHistory);
}
