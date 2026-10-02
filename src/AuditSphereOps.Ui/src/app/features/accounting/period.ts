import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, routeGuid } from '../../core/api';
import { date, guid, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodePeriodRecord = obj({ id: guid, clientId: guid, clientName: text, engagement: text, periodCode: text, startDate: date, endDate: date, basis: text,
  book: text, currency: text, status: text, revision: nat, priorPeriodId: nullable(guid), packageId: nullable(guid), packageLabel: nullable(text),
  mappingId: nullable(guid), mappingLabel: nullable(text),
  pbc: nullable(obj({ engagementId: guid, owner: text, dueDate: text, state: text })), task: nullable(obj({ title: text, owner: text, dueDate: text, state: text })) });

@Component({
  selector: 'audit-accounting-period',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting">← Back to accounting workspace</a>
    <audit-page-header title="Accounting period" eyebrow="Accounting record" description="This page is a read-only exact-record view. Opening a period does not broaden client or engagement authorization." />
    <button matButton="outlined" (click)="period.reload()">Refresh period</button>
    <audit-state [loading]="period.loading()" [error]="period.error()" label="scoped period" />
    @if (period.data(); as p) {
      <section class="panel" aria-labelledby="period-heading">
        <h2 id="period-heading">{{ p.clientName }} · {{ p.periodCode }}</h2>
        <dl class="facts"><dt>Legal entity</dt><dd>{{ p.clientName }}</dd><dt>Engagement</dt><dd>{{ p.engagement }}</dd><dt>Period</dt><dd>{{ p.startDate }} — {{ p.endDate }}</dd>
          <dt>Basis</dt><dd>{{ p.basis }}</dd><dt>Book</dt><dd>{{ p.book }}</dd><dt>Currency</dt><dd><code>{{ p.currency }}</code></dd><dt>Status</dt><dd><audit-status [value]="p.status" /></dd>
          <dt>Revision</dt><dd>{{ p.revision }}</dd><dt>Prior period</dt><dd>{{ p.priorPeriodId ?? 'Not recorded' }}</dd><dt>Latest package</dt><dd>{{ p.packageLabel ?? 'Not recorded' }}</dd>
          <dt>Latest mapping</dt><dd>{{ p.mappingLabel ?? 'Not recorded' }}</dd></dl>
      </section>
      <section class="panel" aria-labelledby="workflow-heading">
        <h2 id="workflow-heading">Workflow handoffs</h2>
        @if (p.task; as t) { <p><strong>Work task:</strong> {{ t.title }} · <strong>Owner:</strong> {{ t.owner }} · <strong>Due:</strong> {{ t.dueDate }} · <strong>State:</strong> {{ t.state }}</p> }
        @if (p.pbc; as b) { <p><strong>PBC owner:</strong> {{ b.owner }} · <strong>Due:</strong> {{ b.dueDate }} · <strong>State:</strong> {{ b.state }}</p> }
        @else if (!p.task) { <p>No open PBC request is linked to this period. No task owner or due date is invented.</p> }
        <p class="actions">
          @if (p.packageId) { <a matButton="outlined" [routerLink]="['/app/accounting/packages', p.packageId]">Open package</a> }
          @if (p.mappingId) { <a matButton="outlined" [routerLink]="['/app/accounting/mappings', p.mappingId]">Open mapping</a> }
          @if (p.pbc) { <a matButton="outlined" [routerLink]="['/app/engagements', p.pbc.engagementId, 'pbc']">Open PBC request</a> }
          <a matButton="outlined" routerLink="/app/accounting/evidence">Open evidence</a>
          <a matButton="outlined" routerLink="/app/accounting/rollforward">Roll forward</a>
          <a matButton="outlined" routerLink="/app/accounting/restatements">Restatements</a>
        </p>
      </section>
    }
  `,
})
export class AccountingPeriodRecord {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly period = this.api.resource(() => (this.id() ? `/api/ui/accounting/periods/${this.id()}` : null), decodePeriodRecord,
    'This period is not visible under the current accounting grant.');
}
