import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, instant, nat, nullable, obj, text } from '../../core/decode';
import { AdministrationRuntime } from './runtime';
import { SHARED } from '../../core/ui';
export const adminOverview = obj({ tenantId: nullable(text), cards: arr(obj({ key: text, title: text, state: text, value: text, detail: text }), 50),
  progress: arr(obj({ key: text, title: text, state: text, detail: text, whyBlocked: nullable(text), requiredAction: nullable(text), requiredAuthority: nullable(text), lastVerification: nullable(instant) }), 50),
  capabilities: arr(obj({ capability: text, displayName: text, permission: text, enabled: bool, state: text, lastVerifiedAt: nullable(instant), stale: bool, diagnosticCode: text }), 50),
  sharePoint: obj({ siteUrl: nullable(text), siteId: nullable(text), driveId: nullable(text), rootFolderId: nullable(text), accessProfile: text,
    tenantAuthentication: text, applicationConsent: text, selectedSitePermission: text, libraryAccess: text, workspaceConfiguration: text, clientTemplate: text,
    readyClientWorkspaces: nat, blockedClientWorkspaces: nat, lastVerification: nullable(instant) }),
  mail: obj({ senderIdentity: text, transportState: text, permissionState: text, lastVerification: nullable(instant), lastFailure: nullable(text) }), securityWarnings: arr(text, 100),
  permissionMatrix: arr(obj({ capability: text, displayName: text, graphEndpoint: text, permission: text, permissionType: text, whyRequired: text, adminConsent: text, requiredEntraRole: text, optional: bool }), 50) });
@Component({ selector: 'audit-administration', imports: [AdministrationRuntime, RouterLink, MatButtonModule, ...SHARED], template: `
  <audit-page-header title="Administration" description="Verified Microsoft capabilities and local AuditSphere access. One verified capability does not imply that the others are available." />
  <nav class="actions" aria-label="Administration sections"><a matButton="filled" routerLink="/app/administration/users">Users & Access</a>
    <a matButton routerLink="/app/operations">Operations</a><a matButton routerLink="/app/administration/project-progress">Project progress</a>
    <a matButton routerLink="/app/administration/microsoft365/tenant-connection">Microsoft tenant connection</a></nav>
  <button matButton (click)="ws.reload()">Refresh verified status</button><audit-state [loading]="ws.loading()" [error]="ws.error()" label="firm administration" />
  <audit-administration-runtime />
  @if (ws.data(); as w) {
    <section class="metric-grid">@for(c of w.cards; track c.key) { <article class="panel"><h2>{{ c.title }}</h2><strong>{{ c.value }}</strong><audit-status [value]="c.state" /><p>{{ c.detail }}</p></article> }</section>
    <section class="panel"><h2>Setup progress</h2><ol>@for(s of w.progress; track s.key) { <li><strong>{{ s.title }}</strong><audit-status [value]="s.state" /><p>{{ s.detail }}</p>
      @if(s.whyBlocked) { <p>Why blocked: {{ s.whyBlocked }}</p> } @if(s.requiredAction) { <p>Required action: {{ s.requiredAction }}</p> } @if(s.requiredAuthority) { <p>Required Microsoft authority: {{ s.requiredAuthority }}</p> }
      <p>Last verification: {{ s.lastVerification ?? 'Not verified' }}</p></li> }</ol></section>
    <section class="panel"><h2>Capabilities and consent</h2>@for(c of w.capabilities; track c.capability) { <h3>{{ c.displayName }}</h3><audit-status [value]="c.state" /><p>{{ c.enabled ? 'Enabled' : 'Disabled' }} · {{ c.permission }} · {{ c.stale ? 'Verification stale' : 'Current status' }} · {{ c.lastVerifiedAt ?? 'Not verified' }}</p><p>{{ c.diagnosticCode }}</p> }</section>
    <section class="panel"><h2>Selected SharePoint resource</h2><p>{{ w.sharePoint.siteUrl ?? 'Not configured' }}</p>
      <dl class="facts"><dt>Tenant authentication</dt><dd>{{ w.sharePoint.tenantAuthentication }}</dd><dt>Application consent</dt><dd>{{ w.sharePoint.applicationConsent }}</dd>
        <dt>Selected site permission</dt><dd>{{ w.sharePoint.selectedSitePermission }}</dd><dt>Library access</dt><dd>{{ w.sharePoint.libraryAccess }}</dd><dt>Workspace configuration</dt><dd>{{ w.sharePoint.workspaceConfiguration }}</dd>
        <dt>Access profile</dt><dd>{{ w.sharePoint.accessProfile }}</dd><dt>Client template</dt><dd>{{ w.sharePoint.clientTemplate }}</dd></dl></section>
    <section class="panel"><h2>Outbound mail</h2><p>{{ w.mail.senderIdentity }} · {{ w.mail.transportState }} · permission {{ w.mail.permissionState }}</p><p>Last verification {{ w.mail.lastVerification ?? 'Not verified' }} · {{ w.mail.lastFailure }}</p></section>
    <section class="panel"><h2>Security warnings</h2><ul>@for(warning of w.securityWarnings; track warning) { <li>{{ warning }}</li> } @empty { <li>No current warnings recorded.</li> }</ul></section>
    <section class="panel"><h2>Permission matrix</h2>@for(p of w.permissionMatrix; track p.capability) { <details><summary>{{ p.displayName }} · {{ p.permission }} · {{ p.permissionType }}</summary><p>{{ p.graphEndpoint }}</p><p>{{ p.whyRequired }}</p><p>Administrator consent: {{ p.adminConsent }} · required role {{ p.requiredEntraRole }} · {{ p.optional ? 'Optional capability' : 'Required capability' }}</p></details> }</section>
  }
`, styles: `.metric-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 15rem), 1fr)); gap: 1rem; }` })
export class AdministrationOverview {
  private readonly api = inject(Api);
  readonly ws = this.api.resource(() => '/api/ui/administration/overview', adminOverview, 'Current firm-wide Administrator access is required.');
}
