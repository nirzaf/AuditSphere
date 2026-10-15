import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SHARED } from '../../core/ui';
import { clientPackage } from './package';

@Component({ selector: 'audit-portal-report-action', imports: [RouterLink, MatButtonModule, ...SHARED], template: `
  <article class="panel report-action" aria-label="Current report response status">
    @if (unavailable()) {
      <p role="status">Report action unavailable. Refresh your portal.</p>
    } @else if (currentPackage(); as report) {
      <h3>{{ report.framework }} · {{ report.periodStart }} to {{ report.periodEnd }}</h3>
      <audit-status [value]="statusLabel(report)" />
      <a matButton="outlined" [routerLink]="['/portal/accounting/packages', report.packageId]">{{ linkLabel(report) }}</a>
    } @else if (workspace.loading()) {
      <p role="status">Loading report status…</p>
    }
  </article>
`, styles: `.report-action { margin-block: .75rem; }
  a { min-height: 44px; }` })
export class PortalReportAction {
  private readonly api = inject(Api);
  readonly packageId = input.required<string>();
  readonly workspace = this.api.resource(
    () => `/api/ui/portal/accounting/packages/${this.packageId()}`,
    clientPackage,
    'Report action unavailable. Refresh your portal.',
  );

  currentPackage() {
    const report = this.workspace.data();
    return report && report.packageId.toLowerCase() === this.packageId().toLowerCase() ? report : null;
  }

  unavailable(): boolean {
    const report = this.workspace.data();
    return !!this.workspace.error() || !!report && report.packageId.toLowerCase() !== this.packageId().toLowerCase();
  }

  statusLabel(report: ReturnType<typeof clientPackage>): string {
    if (!report.composition) return 'No current sealed report invitation';
    return report.managementDecision === 'APPROVED' ? 'Management response recorded' : 'Report response requested';
  }

  linkLabel(report: ReturnType<typeof clientPackage>): string {
    if (!report.composition) return 'View package status';
    return report.managementDecision === 'APPROVED' ? 'View report response' : 'Review exact report';
  }
}
