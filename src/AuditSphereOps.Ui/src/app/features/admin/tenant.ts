import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { consentDestination, tenantWorkspace } from './tenant-contracts';

@Component({ selector: 'audit-tenant-connection', imports: [ReactiveFormsModule, RouterLink, MatButtonModule, ...SHARED], template: `
  <audit-page-header title="Microsoft tenant connection" description="Microsoft authenticates the tenant administrator and asks for consent. AuditSphere verifies the exact tenant and each capability separately." />
  <nav class="actions" aria-label="Microsoft administration"><a matButton routerLink="/app/administration">Administration overview</a><a matButton routerLink="/app/administration/users">Users & Access</a></nav>
  <button matButton (click)="ws.reload()" [disabled]="busy()">Refresh persisted status</button>
  <audit-state [loading]="ws.loading()" [error]="ws.error()" label="Microsoft tenant connection" />
  @if (ws.data(); as w) {
    @if(w.simulation) { <p role="status" class="notice">Simulation: these checks exercise the configured test provider. They do not prove live Microsoft consent.</p> }
    <section class="panel"><h2>Configured tenant</h2><p>{{ w.workspace.connection.expectedTenantId ?? 'No tenant setup draft is prepared.' }}</p>
      <dl class="facts"><dt>Connection</dt><dd><audit-status [value]="w.workspace.connection.connectionState ?? 'NOT_PREPARED'" /></dd>
        <dt>Consent</dt><dd><audit-status [value]="w.workspace.connection.consentState ?? 'NOT_VERIFIED'" /></dd>
        <dt>Latest attempt</dt><dd>{{ w.workspace.connection.lastAttemptState ?? 'None' }} · {{ w.workspace.connection.lastAttemptAt ?? 'Not attempted' }}</dd>
        <dt>Directory verification</dt><dd>{{ w.workspace.connection.directoryCapabilityState }} · {{ w.workspace.connection.directoryLastCheckedAt ?? 'Not observed' }}</dd></dl>
      @if(w.workspace.consentingAdministrator; as a) { <h3>Verified consenting identity</h3><p>Tenant {{ a.tenantId }} · object {{ a.objectId }} · {{ a.verifiedAt }}</p> }
      <p>AuditSphere never asks for a Microsoft password. The deployment supplies approved credentials privately; the browser receives none.</p>
      @if (!w.consentConfigured) { <p role="status">BLOCKED_EXTERNAL: configure the separately approved consent identity and fixed callback. A Microsoft tenant administrator must grant its documented permissions.</p> }
      @else if(!w.workspace.connection.draftId) { <p role="status">A prepared tenant setup draft is required before consent can begin.</p> }
      @else {
        <form [formGroup]="form" (ngSubmit)="connect()"><label><input type="checkbox" formControlName="reviewed" /> I reviewed the configured tenant and requested capability permissions.</label>
          <button matButton="filled" [disabled]="form.invalid || busy() || uncertain()">Connect Microsoft 365 tenant</button></form>
      }
    </section>
    <section class="panel"><h2>Granted capabilities</h2><p>Selected SharePoint site permission and exact library access require their separate resource verification.</p>
      <button matButton="filled" (click)="verify()" [disabled]="busy() || uncertain()">Verify all enabled capabilities</button>
      <div class="table-scroll"><table><caption>Independent Microsoft capability verification</caption><thead><tr><th>Capability</th><th>Permission</th><th>Configured</th><th>Verified state</th><th>Last check</th><th>Diagnostic</th></tr></thead><tbody>
        @for(c of w.workspace.capabilities; track c.capability) { <tr><td>{{ c.displayName }}</td><td>{{ c.permission }}</td><td>{{ c.enabled ? 'Enabled' : 'Disabled' }}</td><td><audit-status [value]="c.stale ? 'STALE' : c.state" /></td><td>{{ c.lastVerifiedAt ?? 'Not observed' }}</td><td>{{ c.diagnosticCode }}</td></tr> }
      </tbody></table></div><a matButton routerLink="/app/administration">Review permission matrix and external blockers</a>
    </section>
    @if(w.workspace.setupProgress; as p) { <section class="panel"><h2>Selected workspace setup</h2><ul>
      <li>Tenant recorded: {{ p.tenantRecorded ? 'Yes' : 'Pending' }}</li><li>Connection prepared: {{ p.connectionPrepared ? 'Yes' : 'Pending' }}</li>
      <li>Administrator consent verified: {{ p.consentVerified ? 'Yes' : 'Pending' }}</li><li>Selected resource evidence: {{ p.selectedResourcesEvidenceRecorded ? 'Recorded' : 'Pending' }}</li>
      <li>Client template approved: {{ p.clientTemplateApproved ? 'Yes' : 'Pending' }}</li><li>Workspace activated: {{ p.workspaceActivated ? 'Yes' : 'Pending' }}</li></ul></section> }
  }
  <audit-command-message [message]="message()" [failed]="failed()" />
` })
export class TenantConnection {
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  readonly ws = this.api.resource(() => '/api/ui/administration/microsoft365', tenantWorkspace, 'Current firm-wide Administrator access is required.');
  readonly form = inject(FormBuilder).nonNullable.group({ reviewed: [false, Validators.requiredTrue] });
  readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false);
  constructor() { effect(() => { if (!this.ws.data()) { this.form.reset(); this.message.set(''); } }); }
  async connect(): Promise<void> {
    const w = this.ws.data(), draftId = w?.workspace.connection.draftId, tenant = w?.workspace.connection.expectedTenantId;
    if (!w || !draftId || !tenant || !w.consentConfigured || this.form.invalid || this.busy() || this.uncertain()) return;
    const generation = this.session.invalidation(); this.busy.set(true);
    try {
      const result = await this.api.command('/api/ui/administration/microsoft365/connect', { draftId, reviewed: true });
      if (generation !== this.session.invalidation()) return;
      if (result.ok) location.assign(consentDestination(result.value, tenant, w.simulation));
      else { this.message.set(result.message); this.failed.set(true); if(result.unknown) this.uncertain.set(true); }
    } catch { this.failed.set(true); this.message.set('The consent redirect could not be verified. Refresh the persisted attempt state before starting again.'); this.uncertain.set(true); }
    finally { this.busy.set(false); }
  }
  async verify(): Promise<void> {
    if (this.busy() || this.uncertain()) return; const generation = this.session.invalidation(); this.busy.set(true);
    try { const result = await this.api.command('/api/ui/administration/microsoft365/verify'); if(generation !== this.session.invalidation()) return;
      this.failed.set(!result.ok); this.message.set(result.ok ? 'Verification results were recorded independently. Review each capability below.' : result.message);
      if(result.ok) this.ws.reload(); else if(result.unknown) this.uncertain.set(true);
    } finally { this.busy.set(false); }
  }
}
